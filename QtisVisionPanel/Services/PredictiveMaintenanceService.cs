using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Integrazione AI (Fase 2) — Manutenzione predittiva (baseline statistica).
    ///
    /// Riceve in-memory gli snapshot di salute PC prodotti da <see cref="HealthSnapshotService"/>
    /// (CPU/RAM/disco) e applica statistica trasparente per anticipare i degradi:
    /// - proiezione lineare del riempimento disco -> ore stimate al raggiungimento della soglia;
    /// - EWMA per CPU/RAM sostenute sopra soglia (carico anomalo persistente);
    /// - trend positivo prolungato della memoria (pattern di leak).
    ///
    /// Gli avvisi sono ADVISORY: loggati nell'Events Monitor a livello Warning ed esposti via
    /// <see cref="MaintenancePredicted"/> e <see cref="GetActiveAlerts"/>. Nessun impatto su
    /// ispezione, scarto o contatori. Attivazione via
    /// <c>MachineRuntimeBindings.PredictiveMaintenanceEnabled</c> (default disabilitato sulle nuove configurazioni).
    ///
    /// Nota: assume la cadenza fissa di <see cref="HealthSnapshotService"/> (60s) per convertire
    /// la pendenza per-campione in ore.
    /// </summary>
    public class PredictiveMaintenanceService
    {
        private const int MaxWindow = 720;            // ~12h di storia a 60s/campione
        private const int MinSamples = 20;
        private const double SnapshotsPerHour = 60.0; // cadenza HealthSnapshotService = 60s
        private const double EwmaLambda = 0.2;

        private const double DiskTargetPercent = 95.0;   // soglia obiettivo per la proiezione disco
        private const double DiskHorizonHours = 336.0;    // avvisa solo se il disco arriva a soglia entro ~14 giorni
        private const double CpuSustainedPercent = 85.0;  // CPU EWMA sostenuta
        private const double MemorySustainedPercent = 90.0; // RAM EWMA sostenuta

        public sealed class MaintenanceAlert
        {
            public string Component { get; set; } // "Disk" | "CPU" | "Memory"
            public string Kind { get; set; }      // "projection" | "sustained-high"
            public string Message { get; set; }
            public double CurrentValue { get; set; }
            public double? EstimatedHoursToThreshold { get; set; }
            public DateTime DetectedAtUtc { get; set; }
        }

        public event Action<MaintenanceAlert> MaintenancePredicted;

        private readonly object _lock = new object();
        private readonly LinkedList<double> _cpu = new LinkedList<double>();
        private readonly LinkedList<double> _mem = new LinkedList<double>();
        private readonly LinkedList<double> _disk = new LinkedList<double>();
        private double? _cpuEwma;
        private double? _memEwma;
        private readonly Dictionary<string, bool> _activeByComponent =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MaintenanceAlert> _activeAlerts =
            new Dictionary<string, MaintenanceAlert>(StringComparer.OrdinalIgnoreCase);

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private volatile bool _enabled;

        public PredictiveMaintenanceService()
        {
            _enabled = ReadEnabledFlag();
            _logger.Info($"PREDICTIVE_MAINTENANCE_INIT|enabled={_enabled}");
        }

        public bool Enabled => _enabled;

        public void RefreshConfiguration() => _enabled = ReadEnabledFlag();

        public IReadOnlyList<MaintenanceAlert> GetActiveAlerts()
        {
            lock (_lock)
            {
                return _activeAlerts.Values.ToList();
            }
        }

        /// <summary>Osserva uno snapshot di salute. Chiamato in-memory da HealthSnapshotService.</summary>
        public void Observe(HealthSnapshotEntry entry)
        {
            if (!_enabled || entry == null)
                return;

            List<MaintenanceAlert> newAlerts = null;

            lock (_lock)
            {
                Push(_cpu, entry.CpuPercent);
                Push(_mem, entry.MemoryPercent);
                Push(_disk, entry.DiskMaxUsedPercent);

                _cpuEwma = Ewma(_cpuEwma, entry.CpuPercent);
                _memEwma = Ewma(_memEwma, entry.MemoryPercent);

                EvaluateDisk(entry, ref newAlerts);
                EvaluateSustained("CPU", _cpuEwma, _cpu.Count, CpuSustainedPercent, entry.CpuPercent, ref newAlerts);
                EvaluateSustained("Memory", _memEwma, _mem.Count, MemorySustainedPercent, entry.MemoryPercent, ref newAlerts);
            }

            if (newAlerts == null)
                return;

            foreach (var alert in newAlerts)
                RaiseAndLog(alert);
        }

        // ---- Valutazioni (sotto lock) ----

        private void EvaluateDisk(HealthSnapshotEntry entry, ref List<MaintenanceAlert> newAlerts)
        {
            const string component = "Disk";
            double current = entry.DiskMaxUsedPercent;

            if (current >= DiskTargetPercent || _disk.Count < MinSamples)
            {
                // Gia' sopra soglia: lo gestisce la diagnostica critica; qui niente "proiezione".
                if (current < DiskTargetPercent)
                    ClearActive(component);
                return;
            }

            double[] data = _disk.ToArray();
            double slopePerSample = LinearSlope(data);
            double slopePerHour = slopePerSample * SnapshotsPerHour;

            if (slopePerHour <= 1e-6)
            {
                ClearActive(component);
                return;
            }

            double hoursToThreshold = (DiskTargetPercent - current) / slopePerHour;
            if (double.IsNaN(hoursToThreshold) || double.IsInfinity(hoursToThreshold) || hoursToThreshold <= 0.0)
            {
                ClearActive(component);
                return;
            }

            if (hoursToThreshold > DiskHorizonHours)
            {
                // Trend presente ma lontano: non allarmare.
                ClearActive(component);
                return;
            }

            if (IsActive(component))
                return; // debounce

            SetActive(component);
            var alert = new MaintenanceAlert
            {
                Component = component,
                Kind = "projection",
                CurrentValue = current,
                EstimatedHoursToThreshold = hoursToThreshold,
                Message = string.Format(
                    "Disco al {0:0.0}%: al ritmo attuale raggiungera' {1:0}% tra circa {2:0} ore (~{3:0.0} giorni). Pianificare pulizia/archiviazione.",
                    current, DiskTargetPercent, hoursToThreshold, hoursToThreshold / 24.0),
                DetectedAtUtc = DateTime.UtcNow
            };
            _activeAlerts[component] = alert;
            (newAlerts ?? (newAlerts = new List<MaintenanceAlert>())).Add(alert);
        }

        private void EvaluateSustained(string component, double? ewma, int sampleCount, double threshold, double current, ref List<MaintenanceAlert> newAlerts)
        {
            if (!ewma.HasValue || sampleCount < MinSamples)
                return;

            if (ewma.Value < threshold)
            {
                ClearActive(component);
                return;
            }

            if (IsActive(component))
                return; // debounce

            SetActive(component);
            var alert = new MaintenanceAlert
            {
                Component = component,
                Kind = "sustained-high",
                CurrentValue = current,
                EstimatedHoursToThreshold = null,
                Message = string.Format(
                    "{0} sostenuta sopra il {1:0}% (media mobile {2:0.0}%). Verificare carico/processi anomali.",
                    component, threshold, ewma.Value),
                DetectedAtUtc = DateTime.UtcNow
            };
            _activeAlerts[component] = alert;
            (newAlerts ?? (newAlerts = new List<MaintenanceAlert>())).Add(alert);
        }

        // ---- Helper ----

        private static void Push(LinkedList<double> window, double value)
        {
            window.AddLast(value);
            while (window.Count > MaxWindow)
                window.RemoveFirst();
        }

        private static double Ewma(double? previous, double value)
        {
            return previous.HasValue ? EwmaLambda * value + (1.0 - EwmaLambda) * previous.Value : value;
        }

        private bool IsActive(string component)
        {
            return _activeByComponent.TryGetValue(component, out bool active) && active;
        }

        private void SetActive(string component)
        {
            _activeByComponent[component] = true;
        }

        private void ClearActive(string component)
        {
            if (IsActive(component))
            {
                _activeByComponent[component] = false;
                _activeAlerts.Remove(component);
            }
        }

        private static double LinearSlope(double[] data)
        {
            int n = data.Length;
            if (n < 2)
                return 0.0;

            double meanX = (n - 1) / 2.0;
            double meanY = data.Average();
            double num = 0.0, den = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = i - meanX;
                num += dx * (data[i] - meanY);
                den += dx * dx;
            }

            return den == 0.0 ? 0.0 : num / den;
        }

        private void RaiseAndLog(MaintenanceAlert alert)
        {
            try
            {
                _logger.Warn($"PREDICTIVE_MAINTENANCE|component={alert.Component}|kind={alert.Kind}" +
                             $"|value={alert.CurrentValue:0.0}|eta_hours={(alert.EstimatedHoursToThreshold.HasValue ? alert.EstimatedHoursToThreshold.Value.ToString("0.0") : "n/a")}");

                var metadata = new Dictionary<string, object>
                {
                    { "component", alert.Component },
                    { "kind", alert.Kind },
                    { "value", alert.CurrentValue.ToString("0.0") },
                    { "eta_hours", alert.EstimatedHoursToThreshold.HasValue ? alert.EstimatedHoursToThreshold.Value.ToString("0.0") : "n/a" }
                };

                // Advisory: livello Warning -> Events Monitor, senza attivare il badge errori.
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    LogLevel.Warn,
                    "PREDICTIVE_MAINTENANCE",
                    "PredictiveMaintenance",
                    alert.Message,
                    nameof(PredictiveMaintenanceService),
                    null,
                    metadata);

                MaintenancePredicted?.Invoke(alert);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "PREDICTIVE_MAINTENANCE_NOTIFY_FAILED");
            }
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.PredictiveMaintenanceEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
