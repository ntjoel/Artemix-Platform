using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public enum PhotocellTimedTriggerEventKind
    {
        EdgeAccepted,
        OutputSkipped,
        OutputScheduled,
        OutputOn,
        OutputOff,
        BatchCompleted,
        BatchCancelled,
        Error
    }

    public sealed class PhotocellTimedTriggerOutputRequest
    {
        public string OutputName { get; set; }

        public string SignalCode { get; set; }

        public int ChannelNumber { get; set; } = -1;

        public string Board { get; set; }

        public string PhysicalChannel { get; set; }

        public bool ActiveElectricalState { get; set; } = true;

        public bool InactiveElectricalState { get; set; }

        public int DelayMs { get; set; }

        public int PulseMs { get; set; }

        public bool Enabled { get; set; }
    }

    public sealed class PhotocellTimedTriggerBatchRequest
    {
        public string SourceSignalCode { get; set; }

        public long SourceEncoderCount { get; set; }

        public double? SourceMachineQuotaMm { get; set; }

        public DateTime? SourceEventTimestampUtc { get; set; }

        public IReadOnlyList<PhotocellTimedTriggerOutputRequest> Outputs { get; set; } =
            Array.Empty<PhotocellTimedTriggerOutputRequest>();
    }

    public sealed class PhotocellTimedTriggerEventArgs : EventArgs
    {
        public PhotocellTimedTriggerEventKind Kind { get; set; }

        public long? BatchId { get; set; }

        public string OutputName { get; set; }

        public string SignalCode { get; set; }

        public int DelayMs { get; set; }

        public int PulseMs { get; set; }

        public double ElapsedMs { get; set; }

        public double CompensationMs { get; set; }

        public double EffectiveDelayMs { get; set; }

        /// <summary>
        /// Total time from hardware photocell edge (SourceEventTimestampUtc) to output active write.
        /// Equals CompensationMs (dispatch latency) + ElapsedMs (wait inside service).
        /// </summary>
        public double TotalEdgeToOutputMs { get; set; }

        public string Message { get; set; }
    }

    public sealed class PhotocellTimedTriggerService : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _sync = new object();
        private CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private long _lastBatchId;

        public event EventHandler<PhotocellTimedTriggerEventArgs> EventRaised;

        public long QueueBatch(
            PhotocellTimedTriggerBatchRequest batch,
            Func<PhotocellTimedTriggerOutputRequest, Task> outputExecutor)
        {
            if (batch == null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            if (outputExecutor == null)
            {
                throw new ArgumentNullException(nameof(outputExecutor));
            }

            var batchId = Interlocked.Increment(ref _lastBatchId);
            var referenceMs = _clock.Elapsed.TotalMilliseconds;
            var sourceLatencyCompensationMs = CalculateSourceLatencyCompensation(batch.SourceEventTimestampUtc);
            var lifetimeToken = _lifetimeCts.Token;
            var outputs = (batch.Outputs ?? Array.Empty<PhotocellTimedTriggerOutputRequest>()).ToList();

            RaiseEvent(new PhotocellTimedTriggerEventArgs
            {
                Kind = PhotocellTimedTriggerEventKind.EdgeAccepted,
                BatchId = batchId,
                SignalCode = batch.SourceSignalCode,
                ElapsedMs = 0.0,
                CompensationMs = sourceLatencyCompensationMs,
                Message = $"PHOTOCELL_EDGE_ACCEPTED batch={batchId} source={batch.SourceSignalCode} encoder={batch.SourceEncoderCount} quota={batch.SourceMachineQuotaMm:0.0} mm eventAge={sourceLatencyCompensationMs:0.0} ms"
            });

            foreach (var skipped in outputs.Where(item => item == null || !item.Enabled || string.IsNullOrWhiteSpace(item.SignalCode)))
            {
                RaiseEvent(new PhotocellTimedTriggerEventArgs
                {
                    Kind = PhotocellTimedTriggerEventKind.OutputSkipped,
                    BatchId = batchId,
                    OutputName = skipped?.OutputName,
                    SignalCode = skipped?.SignalCode,
                    DelayMs = skipped?.DelayMs ?? 0,
                    PulseMs = skipped?.PulseMs ?? 0,
                    Message = $"TIMED_TRIGGER_OUTPUT_SKIPPED batch={batchId} output={skipped?.OutputName ?? "UNKNOWN"} signal={(string.IsNullOrWhiteSpace(skipped?.SignalCode) ? "<not-configured>" : skipped.SignalCode)}"
                });
            }

            var activeOutputs = outputs
                .Where(item => item != null && item.Enabled && !string.IsNullOrWhiteSpace(item.SignalCode))
                .ToList();

            if (activeOutputs.Count == 0)
            {
                RaiseEvent(new PhotocellTimedTriggerEventArgs
                {
                    Kind = PhotocellTimedTriggerEventKind.BatchCompleted,
                    BatchId = batchId,
                    Message = $"TRIGGER_BATCH_COMPLETED batch={batchId} no active outputs configured."
                });
                return batchId;
            }

            Task.Run(async () =>
            {
                try
                {
                    var tasks = activeOutputs
                        .Select(item => RunOutputAsync(batchId, referenceMs, sourceLatencyCompensationMs, item, outputExecutor, lifetimeToken))
                        .ToArray();

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    RaiseEvent(new PhotocellTimedTriggerEventArgs
                    {
                        Kind = PhotocellTimedTriggerEventKind.BatchCompleted,
                        BatchId = batchId,
                        ElapsedMs = _clock.Elapsed.TotalMilliseconds - referenceMs,
                        Message = $"TRIGGER_BATCH_COMPLETED batch={batchId} outputs={activeOutputs.Count}"
                    });
                }
                catch (OperationCanceledException)
                {
                    RaiseEvent(new PhotocellTimedTriggerEventArgs
                    {
                        Kind = PhotocellTimedTriggerEventKind.BatchCancelled,
                        BatchId = batchId,
                        ElapsedMs = _clock.Elapsed.TotalMilliseconds - referenceMs,
                        Message = $"TRIGGER_BATCH_CANCELLED batch={batchId}"
                    });
                }
                catch (Exception ex)
                {
                    RaiseEvent(new PhotocellTimedTriggerEventArgs
                    {
                        Kind = PhotocellTimedTriggerEventKind.Error,
                        BatchId = batchId,
                        ElapsedMs = _clock.Elapsed.TotalMilliseconds - referenceMs,
                        Message = $"TRIGGER_BATCH_ERROR batch={batchId}: {ex.Message}"
                    });
                }
            }, lifetimeToken);

            return batchId;
        }

        public void CancelAll(string reason)
        {
            CancellationTokenSource sourceToCancel;
            lock (_sync)
            {
                sourceToCancel = _lifetimeCts;
                _lifetimeCts = new CancellationTokenSource();
            }

            try
            {
                sourceToCancel.Cancel();
            }
            finally
            {
                sourceToCancel.Dispose();
            }

            RaiseEvent(new PhotocellTimedTriggerEventArgs
            {
                Kind = PhotocellTimedTriggerEventKind.BatchCancelled,
                Message = $"TRIGGER_BATCH_CANCELLED all pending batches cleared | reason={reason}"
            });
        }

        private async Task RunOutputAsync(
            long batchId,
            double referenceMs,
            double sourceLatencyCompensationMs,
            PhotocellTimedTriggerOutputRequest output,
            Func<PhotocellTimedTriggerOutputRequest, Task> outputExecutor,
            CancellationToken cancellationToken)
        {
            var effectiveDelayMs = Math.Max(0, output.DelayMs - sourceLatencyCompensationMs);
            RaiseEvent(new PhotocellTimedTriggerEventArgs
            {
                Kind = PhotocellTimedTriggerEventKind.OutputScheduled,
                BatchId = batchId,
                OutputName = output.OutputName,
                SignalCode = output.SignalCode,
                DelayMs = output.DelayMs,
                PulseMs = output.PulseMs,
                CompensationMs = sourceLatencyCompensationMs,
                EffectiveDelayMs = effectiveDelayMs,
                Message = $"TIMED_TRIGGER_SCHEDULED batch={batchId} output={output.OutputName} signal={output.SignalCode} delay={output.DelayMs} ms effectiveDelay={effectiveDelayMs:0.0} ms compensation={sourceLatencyCompensationMs:0.0} ms pulse={output.PulseMs} ms"
            });

            await WaitUntilDelayElapsedAsync(referenceMs, effectiveDelayMs, cancellationToken).ConfigureAwait(false);

            var onElapsedMs = _clock.Elapsed.TotalMilliseconds - referenceMs;
            var totalEdgeToOutputMs = sourceLatencyCompensationMs + onElapsedMs;
            RaiseEvent(new PhotocellTimedTriggerEventArgs
            {
                Kind = PhotocellTimedTriggerEventKind.OutputOn,
                BatchId = batchId,
                OutputName = output.OutputName,
                SignalCode = output.SignalCode,
                DelayMs = output.DelayMs,
                PulseMs = output.PulseMs,
                ElapsedMs = onElapsedMs,
                CompensationMs = sourceLatencyCompensationMs,
                EffectiveDelayMs = effectiveDelayMs,
                TotalEdgeToOutputMs = totalEdgeToOutputMs,
                Message = $"TIMED_TRIGGER_OUTPUT_ON batch={batchId} output={output.OutputName} signal={output.SignalCode} edgeToOutput={totalEdgeToOutputMs:0.0} ms (dispatch={sourceLatencyCompensationMs:0.0}+wait={onElapsedMs:0.0}) target={output.DelayMs} ms pulse={output.PulseMs} ms"
            });

            await outputExecutor(output).ConfigureAwait(false);

            var offElapsedMs = _clock.Elapsed.TotalMilliseconds - referenceMs;
            RaiseEvent(new PhotocellTimedTriggerEventArgs
            {
                Kind = PhotocellTimedTriggerEventKind.OutputOff,
                BatchId = batchId,
                OutputName = output.OutputName,
                SignalCode = output.SignalCode,
                DelayMs = output.DelayMs,
                PulseMs = output.PulseMs,
                ElapsedMs = offElapsedMs,
                CompensationMs = sourceLatencyCompensationMs,
                EffectiveDelayMs = effectiveDelayMs,
                Message = $"TIMED_TRIGGER_OUTPUT_OFF batch={batchId} output={output.OutputName} signal={output.SignalCode} completedAt={offElapsedMs:0.0} ms"
            });
        }

        private async Task WaitUntilDelayElapsedAsync(double referenceMs, double delayMs, CancellationToken cancellationToken)
        {
            var targetMs = Math.Max(0, delayMs);

            // Coarse wait: yield the thread for all but the last 20ms.
            // Task.Delay resolution on Windows without a multimedia timer is ~15.6ms,
            // so we must stop using it before we get within that window.
            var coarseRemainingMs = targetMs - (_clock.Elapsed.TotalMilliseconds - referenceMs);
            if (coarseRemainingMs > 20)
            {
                await Task.Delay(Math.Max(1, (int)coarseRemainingMs - 20), cancellationToken).ConfigureAwait(false);
            }

            // Spin-wait for the last ≤20ms. Burns one CPU thread but eliminates
            // Windows timer quantization jitter (~15ms) from the final trigger edge.
            var spinDeadlineMs = referenceMs + targetMs;
            while (_clock.Elapsed.TotalMilliseconds < spinDeadlineMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.SpinWait(200);
            }
        }

        private static double CalculateSourceLatencyCompensation(DateTime? sourceEventTimestampUtc)
        {
            if (!sourceEventTimestampUtc.HasValue)
            {
                return 0.0;
            }

            var sourceUtc = sourceEventTimestampUtc.Value.Kind == DateTimeKind.Utc
                ? sourceEventTimestampUtc.Value
                : sourceEventTimestampUtc.Value.ToUniversalTime();
            var elapsedMs = (DateTime.UtcNow - sourceUtc).TotalMilliseconds;
            if (double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs))
            {
                return 0.0;
            }

            return Math.Max(0.0, Math.Min(5000.0, elapsedMs));
        }

        private void RaiseEvent(PhotocellTimedTriggerEventArgs args)
        {
            EventRaised?.Invoke(this, args);
        }

        public void Dispose()
        {
            CancelAll("service dispose");
            lock (_sync)
            {
                _lifetimeCts.Dispose();
            }
        }
    }
}
