using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public class MachineController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly object _syncLock = new object();
        private readonly IIODeviceManager _ioManager;

        private long _nextProductId = 1;
        private long _lastMainEncoderCount; // accessed via Interlocked — do NOT add volatile
        private MachineRuntimeConfiguration _configuration;

        public MachineController(IIODeviceManager ioManager)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
        }

        public MachineRuntimeConfiguration Configuration => Volatile.Read(ref _configuration);

        public bool IsInitialized => Configuration != null;

        public string Status { get; private set; } = "Controller not initialized";

        public event EventHandler<TrackedProductEventArgs> ProductDetected;
        public event EventHandler<TrackedProductEventArgs> ProductUpdated;
        public event EventHandler<TrackedProductEventArgs> ProductReadyForReject;
        public event EventHandler<ProductInterventionEventArgs> InterventionPointReached;
        public event EventHandler<string> LogMessage;

        public List<TrackedProduct> ActiveProducts { get; } = new List<TrackedProduct>();

        public Task InitializeAsync(MachineRuntimeConfiguration configuration)
        {
            var validatedConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Volatile.Write(ref _configuration, validatedConfiguration);
            Status = $"Controller initialized with trigger mode {configuration.TriggerMode}";
            EmitLog(Status);
            return Task.CompletedTask;
        }

        public TrackedProduct RegisterProductDetection(long encoderCount)
        {
            EnsureInitialized();

            var mainEncoder = GetMainEncoderConfiguration();
            var detectionMachineMm = mainEncoder?.PhotocellMachineOffsetMm ?? 0.0;
            var triggerOffsetCounts = MachineRuntimeIo.ConvertMillimetersToCounts(mainEncoder?.TriggerOffsetMm ?? 0.0, mainEncoder);
            var rejectOffsetCounts = MachineRuntimeIo.ConvertMillimetersToCounts(mainEncoder?.RejectOffsetMm ?? 0.0, mainEncoder);

            var product = new TrackedProduct
            {
                ProductId = _nextProductId++,
                DetectionTimeUtc = DateTime.UtcNow,
                DetectionEncoderCount = encoderCount,
                DetectionMachinePositionMm = detectionMachineMm,
                CurrentEncoderCount = encoderCount,
                CurrentMachinePositionMm = detectionMachineMm,
                TriggerEncoderTarget = encoderCount + triggerOffsetCounts,
                RejectEncoderTarget = encoderCount + rejectOffsetCounts,
                State = TrackedProductState.Detected,
                Notes = "Created by photocell event.",
                InterventionStates = BuildInterventionStates(encoderCount, detectionMachineMm, mainEncoder)
            };

            lock (_syncLock)
            {
                ActiveProducts.Add(product);
                TrimQueueIfNeeded();
            }

            ProductDetected?.Invoke(this, new TrackedProductEventArgs(product, "Product detected"));
            EmitLog($"Tracked product {product.ProductId} creato a quota encoder {encoderCount} | quota macchina fotocellula {detectionMachineMm:0.0} mm");
            return product;
        }

        public bool ApplyInspectionResult(long productId, bool isAccepted, string source)
        {
            lock (_syncLock)
            {
                var product = ActiveProducts.FirstOrDefault(p => p.ProductId == productId);
                if (product == null)
                {
                    return false;
                }

                product.IsAccepted = isAccepted;
                product.InspectionSource = source;
                product.State = isAccepted ? TrackedProductState.Accepted : TrackedProductState.RejectedPending;
                product.Notes = isAccepted ? "Inspection OK." : "Inspection NOK. Reject pending.";

                ProductUpdated?.Invoke(this, new TrackedProductEventArgs(product, "Inspection result applied"));
                EmitLog($"Esito prodotto {product.ProductId}: {(isAccepted ? "OK" : "NOK")} da {source}");
                return true;
            }
        }

        public IReadOnlyList<TrackedProduct> UpdateEncoderPosition(int encoderChannel, long encoderCount)
        {
            EnsureInitialized();

            var mainEncoder = GetMainEncoderConfiguration();
            if (mainEncoder == null || MachineRuntimeIo.ParseChannelNumber(mainEncoder.Channel) != encoderChannel)
            {
                return Array.Empty<TrackedProduct>();
            }

            Interlocked.Exchange(ref _lastMainEncoderCount, encoderCount);
            // Percorso eseguito a ogni campione encoder: nessuna lista/LINQ finche' non
            // viene realmente raggiunto un punto di intervento.
            List<TrackedProduct> rejectReady = null;
            List<KeyValuePair<TrackedProduct, ProductInterventionState>> reachedInterventions = null;
            var nowUtc = DateTime.UtcNow;

            lock (_syncLock)
            {
                foreach (var product in ActiveProducts)
                {
                    product.CurrentEncoderCount = encoderCount;
                    product.CurrentMachinePositionMm = MachineRuntimeIo.ConvertCountsToMillimeters(encoderCount - product.DetectionEncoderCount, mainEncoder) + product.DetectionMachinePositionMm;

                    for (int pointIndex = 0; pointIndex < product.InterventionStates.Count; pointIndex++)
                    {
                        var point = product.InterventionStates[pointIndex];
                        if (point.Executed || encoderCount < point.TargetEncoderCount)
                        {
                            continue;
                        }

                        point.Executed = true;
                        point.ReachedEncoderCount = encoderCount;
                        point.ReachedTimeUtc = nowUtc;
                        point.ReachedLateCounts = Math.Max(0, encoderCount - point.TargetEncoderCount);
                        point.ReachedLateMm = MachineRuntimeIo.ConvertCountsToMillimeters(point.ReachedLateCounts, mainEncoder);
                        if (reachedInterventions == null)
                            reachedInterventions = new List<KeyValuePair<TrackedProduct, ProductInterventionState>>(4);
                        reachedInterventions.Add(new KeyValuePair<TrackedProduct, ProductInterventionState>(product, point));

                        if (string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase))
                        {
                            product.TriggerReached = true;
                            if (product.State == TrackedProductState.Detected)
                            {
                                product.State = TrackedProductState.AwaitingInspection;
                            }
                        }

                        if (string.Equals(point.ActionType, "Reject", StringComparison.OrdinalIgnoreCase) && product.IsAccepted == false)
                        {
                            if (rejectReady == null)
                                rejectReady = new List<TrackedProduct>(2);
                            if (!rejectReady.Contains(product))
                                rejectReady.Add(product);
                        }
                    }

                    if (product.IsAccepted == true && product.InterventionStates.All(p => p.Executed || string.Equals(p.ActionType, "Reject", StringComparison.OrdinalIgnoreCase)))
                    {
                        product.State = TrackedProductState.Completed;
                    }
                }

                ActiveProducts.RemoveAll(product => product.State == TrackedProductState.Completed);
            }

            if (reachedInterventions != null)
            {
                foreach (var reached in reachedInterventions)
                {
                    InterventionPointReached?.Invoke(this,
                        new ProductInterventionEventArgs(reached.Key, reached.Value, "Intervention point reached"));
                    EmitLog($"Prodotto {reached.Key.ProductId} raggiunto punto {reached.Value.PointCode} a quota encoder target {reached.Value.TargetEncoderCount} | encoder reale {reached.Value.ReachedEncoderCount} | ritardo +{reached.Value.ReachedLateCounts} counts ({reached.Value.ReachedLateMm:0.0} mm) | quota macchina {reached.Value.AbsoluteMachineQuotaMm:0.0} mm");
                }
            }

            if (rejectReady != null)
            {
                foreach (var product in rejectReady)
                {
                    ProductReadyForReject?.Invoke(this,
                        new TrackedProductEventArgs(product, "Product reached reject position"));
                    EmitLog($"Prodotto {product.ProductId} arrivato al punto scarto");
                }
            }

            return rejectReady ?? (IReadOnlyList<TrackedProduct>)Array.Empty<TrackedProduct>();
        }

        public long GetLastMainEncoderCount()
        {
            return Interlocked.Read(ref _lastMainEncoderCount);
        }

        private List<ProductInterventionState> BuildInterventionStates(long encoderCount, double detectionMachineMm, EncoderConfigurationTemplate mainEncoder)
        {
            var points = Configuration?.InterventionPoints?.Where(point => point.Enabled).OrderBy(point => point.EffectiveOffsetMm).ToList()
                         ?? new List<MachineInterventionPoint>();

            return points.Select(point => new ProductInterventionState
            {
                PointCode = point.PointCode,
                Description = point.Description,
                ActionType = point.ActionType,
                SignalCode = point.SignalCode,
                PulseMs = point.PulseMs,
                TargetOffsetMm = point.EffectiveOffsetMm,
                AbsoluteMachineQuotaMm = detectionMachineMm + point.EffectiveOffsetMm,
                TargetEncoderCount = encoderCount + MachineRuntimeIo.ConvertMillimetersToCounts(point.EffectiveOffsetMm, mainEncoder),
                Executed = false
            }).ToList();
        }

        private EncoderConfigurationTemplate GetMainEncoderConfiguration()
        {
            var axisCode = Configuration?.RuntimeBindings?.MainEncoderAxisCode;
            return Configuration?.EncoderTemplates?.FirstOrDefault(e => e.AxisName == axisCode)
                   ?? Configuration?.EncoderTemplates?.FirstOrDefault();
        }

        public void ClearActiveProducts()
        {
            lock (_syncLock)
            {
                ActiveProducts.Clear();
            }
            EmitLog("Active product queue cleared after configuration update");
        }

        private void TrimQueueIfNeeded()
        {
            var capacity = Configuration?.RuntimeBindings?.ProductQueueCapacity ?? 200;
            while (ActiveProducts.Count > capacity)
            {
                ActiveProducts.RemoveAt(0);
            }
        }

        private void EnsureInitialized()
        {
            if (Configuration == null)
            {
                throw new InvalidOperationException("MachineController not initialized.");
            }
        }

        private void EmitLog(string message)
        {
            Logger.Info(message);
            LogMessage?.Invoke(this, message);
        }
    }
}
