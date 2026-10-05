using NLog;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Fondazione dati AI (Fase 0) — snapshot periodico dello stato di salute del PC.
    ///
    /// Legge le metriche gia esposte da <see cref="SystemDiagnosticsService"/>
    /// (CPU, RAM, disco) e le persiste a intervallo fisso in <c>tbl_health_snapshots</c>
    /// tramite <see cref="HealthSnapshotRepository"/>, costruendo la serie storica per
    /// la manutenzione predittiva.
    ///
    /// Attivazione controllata dal flag <c>MachineRuntimeBindings.DataFoundationCaptureEnabled</c>
    /// (default: disabilitato sulle nuove configurazioni), riletto ad ogni tick. Additivo e non bloccante: in caso di
    /// errore o DB non disponibile degrada silenziosamente.
    /// </summary>
    public class HealthSnapshotService : IDisposable
    {
        private const int SnapshotIntervalSeconds = 60;
        private const int InitialDelaySeconds = 45; // attende warm-up di DB e diagnostica

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly HealthSnapshotRepository _repository = new HealthSnapshotRepository();
        private readonly object _timerLock = new object();
        private Timer _timer;
        private bool _isStarted;
        private bool _isDisposed;

        public void Start()
        {
            if (_isStarted || _isDisposed)
                return;

            // Assicura che la diagnostica stia effettivamente aggiornando le metriche.
            try
            {
                ServiceLocator.SystemDiagnosticsService?.Start();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "HEALTH_SNAPSHOT_DIAGNOSTICS_START_FAILED");
            }

            lock (_timerLock)
            {
                if (_timer == null)
                {
                    _timer = new Timer(
                        OnTick,
                        null,
                        TimeSpan.FromSeconds(InitialDelaySeconds),
                        TimeSpan.FromSeconds(SnapshotIntervalSeconds));
                }
            }

            _isStarted = true;
            _logger.Info($"HEALTH_SNAPSHOT_STARTED|interval={SnapshotIntervalSeconds}s");
        }

        // async void ammesso SOLO come callback del timer (stesso pattern di OutputWatchdogService.OnPollTick)
        private async void OnTick(object state)
        {
            if (_isDisposed)
                return;

            try
            {
                await CaptureSnapshotAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "HEALTH_SNAPSHOT_TICK_FAILED");
            }
        }

        private async Task CaptureSnapshotAsync()
        {
            if (!ReadEnabledFlag())
                return;

            var diagnostics = ServiceLocator.SystemDiagnosticsService;
            if (diagnostics == null)
                return;

            double diskMaxUsedPercent = 0.0;
            try
            {
                var drives = diagnostics.Drives;
                if (drives != null)
                {
                    // Copia difensiva: la collection e' aggiornata dal thread UI.
                    foreach (var drive in System.Linq.Enumerable.ToList(drives))
                    {
                        if (drive != null && drive.UsedPercent > diskMaxUsedPercent)
                            diskMaxUsedPercent = drive.UsedPercent;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "HEALTH_SNAPSHOT_DISK_READ_SKIPPED");
            }

            var entry = new HealthSnapshotEntry
            {
                Timestamp = DateTime.Now,
                CpuPercent = diagnostics.CpuUsagePercent,
                MemoryPercent = diagnostics.MemoryUsagePercent,
                MemoryUsedGb = diagnostics.UsedMemoryGb,
                MemoryTotalGb = diagnostics.TotalMemoryGb,
                DiskMaxUsedPercent = diskMaxUsedPercent
            };

            // Fase 2: alimenta la manutenzione predittiva in-memory prima della scrittura DB.
            ServiceLocator.PredictiveMaintenanceService?.Observe(entry);

            await _repository.InsertAsync(entry).ConfigureAwait(false);
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.DataFoundationCaptureEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            lock (_timerLock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}
