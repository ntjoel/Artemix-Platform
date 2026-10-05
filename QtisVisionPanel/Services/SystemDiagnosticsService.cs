using MySql.Data.MySqlClient;
using NLog;
using QtisVisionPanel;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace QtisVisionPanel.Services
{
    public class SystemDiagnosticsService : INotifyPropertyChanged, IDisposableService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;

            public MemoryStatusEx()
            {
                Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            }
        }

        private sealed class DiagnosticsSnapshot
        {
            public double CpuUsagePercent { get; set; }
            public double TotalMemoryGb { get; set; }
            public double UsedMemoryGb { get; set; }
            public double MemoryUsagePercent { get; set; }
            public List<SystemDriveStatusItem> Drives { get; set; } = new List<SystemDriveStatusItem>();
            public List<TemperatureSensorStatusItem> TemperatureSensors { get; set; } = new List<TemperatureSensorStatusItem>();
            public List<SystemDiagnosticsAlertItem> Alerts { get; set; } = new List<SystemDiagnosticsAlertItem>();
            public TemperatureSensorStatusItem HottestCpuTemperatureSensor { get; set; }
            public TemperatureSensorStatusItem HottestDiskTemperatureSensor { get; set; }
            public TemperatureSensorStatusItem MemoryTemperatureSensor { get; set; }
            public DatabaseArchiveStatusItem DatabaseArchiveStatus { get; set; }
            public DiagnosticsSeverity OverallSeverity { get; set; }
            public string WorstDiskText { get; set; }
            public string CleanupRootsText { get; set; }
            public string CleanupPolicyText { get; set; }
            public string LastCleanupText { get; set; }
            public DateTime LastUpdatedAt { get; set; }
        }

        private sealed class CleanupResult
        {
            public string DriveRoot { get; set; }
            public int DeletedFolders { get; set; }
            public int DeletedFiles { get; set; }
            public double FinalUsedPercent { get; set; }
            public bool CleanupExecuted { get; set; }
            public bool TargetReached { get; set; }
            public string Details { get; set; }
        }

        private sealed class DatabaseArchiveCleanupResult
        {
            public int DeletedRows { get; set; }
            public double FinalSizeMb { get; set; }
            public bool CleanupExecuted { get; set; }
            public bool TargetReached { get; set; }
            public string Details { get; set; }
            public DateTime ExecutedAt { get; set; }
        }

        private sealed class DatabaseArchiveMeasurement
        {
            public double SizeMb { get; set; }
            public long RowCount { get; set; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

        private readonly ObservableCollection<SystemDriveStatusItem> _drives = new ObservableCollection<SystemDriveStatusItem>();
        private readonly ObservableCollection<SystemDiagnosticsAlertItem> _activeAlerts = new ObservableCollection<SystemDiagnosticsAlertItem>();
        private readonly ObservableCollection<TemperatureSensorStatusItem> _temperatureSensors = new ObservableCollection<TemperatureSensorStatusItem>();
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, DiagnosticsSeverity> _lastSeverityByKey =
            new Dictionary<string, DiagnosticsSeverity>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastCleanupByDrive =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly ApplicationEventLogger _eventLogger = ServiceLocator.ApplicationEventLogger;
        private readonly object _timerLock = new object();
        private readonly object _temperatureProviderLock = new object();

        private Timer _refreshTimer;
        private PerformanceCounter _cpuCounter;
        private IHardwareTemperatureProvider _hardwareTemperatureProvider;
        private string _hardwareTemperatureProviderMode;
        private string _lastHardwareTemperatureProviderLog;
        private bool _cpuCounterPrimed;
        private bool _isStarted;
        private volatile bool _isShuttingDown;
        private volatile bool _isDisposed;
        private double _cpuUsagePercent;
        private double _totalMemoryGb;
        private double _usedMemoryGb;
        private double _memoryUsagePercent;
        private DiagnosticsSeverity _overallSeverity = DiagnosticsSeverity.Healthy;
        private string _worstDiskText = GetMessage("Sub_entry_DiagnosticsNoDiskData", "No disk data");
        private string _cleanupRootsText = "-";
        private string _cleanupPolicyText = "-";
        private string _lastCleanupText = GetMessage("Sub_entry_DiagnosticsNoCleanupExecuted", "No cleanup executed");
        private TemperatureSensorStatusItem _hottestCpuTemperatureSensor;
        private TemperatureSensorStatusItem _hottestDiskTemperatureSensor;
        private TemperatureSensorStatusItem _memoryTemperatureSensor;
        private DatabaseArchiveStatusItem _databaseArchiveStatus;
        private DateTime _lastDatabaseArchiveCleanupAt = DateTime.MinValue;
        private int _lastDatabaseArchiveDeletedRows;
        private string _lastDatabaseArchiveCleanupDetails = GetMessage("Sub_entry_DatabaseArchiveNoCleanupExecuted", "No database cleanup executed");
        private bool _databaseArchiveCleanupExecuted;
        private DateTime _lastUpdatedAt = DateTime.MinValue;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<SystemDriveStatusItem> Drives => _drives;
        public ObservableCollection<SystemDiagnosticsAlertItem> ActiveAlerts => _activeAlerts;
        public ObservableCollection<TemperatureSensorStatusItem> TemperatureSensors => _temperatureSensors;
        public bool IsShuttingDown => _isShuttingDown;

        public TemperatureSensorStatusItem HottestCpuTemperatureSensor
        {
            get => _hottestCpuTemperatureSensor;
            private set
            {
                _hottestCpuTemperatureSensor = value ?? CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_CpuTemperature", "CPU temperature"),
                    DiagnosticsTemperatureSensorType.Cpu,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
                OnPropertyChanged();
            }
        }

        public TemperatureSensorStatusItem HottestDiskTemperatureSensor
        {
            get => _hottestDiskTemperatureSensor;
            private set
            {
                _hottestDiskTemperatureSensor = value ?? CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_HottestDiskTemperature", "Hottest disk temperature"),
                    DiagnosticsTemperatureSensorType.Disk,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
                OnPropertyChanged();
            }
        }

        public TemperatureSensorStatusItem MemoryTemperatureSensor
        {
            get => _memoryTemperatureSensor;
            private set
            {
                _memoryTemperatureSensor = value ?? CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                    DiagnosticsTemperatureSensorType.Memory,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
                OnPropertyChanged();
            }
        }

        public DatabaseArchiveStatusItem DatabaseArchiveStatus
        {
            get => _databaseArchiveStatus;
            private set
            {
                _databaseArchiveStatus = value ?? CreateUnavailableDatabaseArchiveStatus(GetSettings());
                OnPropertyChanged();
            }
        }

        public double CpuUsagePercent
        {
            get => _cpuUsagePercent;
            private set
            {
                if (Math.Abs(_cpuUsagePercent - value) > 0.01)
                {
                    _cpuUsagePercent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CpuUsageText));
                }
            }
        }

        public string CpuUsageText => string.Format("{0:0.0}%", CpuUsagePercent);

        public double TotalMemoryGb
        {
            get => _totalMemoryGb;
            private set
            {
                if (Math.Abs(_totalMemoryGb - value) > 0.01)
                {
                    _totalMemoryGb = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MemoryUsageText));
                }
            }
        }

        public double UsedMemoryGb
        {
            get => _usedMemoryGb;
            private set
            {
                if (Math.Abs(_usedMemoryGb - value) > 0.01)
                {
                    _usedMemoryGb = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MemoryUsageText));
                }
            }
        }

        public double MemoryUsagePercent
        {
            get => _memoryUsagePercent;
            private set
            {
                if (Math.Abs(_memoryUsagePercent - value) > 0.01)
                {
                    _memoryUsagePercent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MemoryUsageText));
                }
            }
        }

        public string MemoryUsageText
        {
            get
            {
                return string.Format("{0:0.0}% ({1:0.0}/{2:0.0} GB)", MemoryUsagePercent, UsedMemoryGb, TotalMemoryGb);
            }
        }

        public DiagnosticsSeverity OverallSeverity
        {
            get => _overallSeverity;
            private set
            {
                if (_overallSeverity != value)
                {
                    _overallSeverity = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(OverallStatusText));
                    OnPropertyChanged(nameof(OverallStatusBrush));
                }
            }
        }

        public string OverallStatusText
        {
            get
            {
                return GetSeverityText(OverallSeverity);
            }
        }

        public Brush OverallStatusBrush => CreateBrush(GetSeverityHex(OverallSeverity));

        public string WorstDiskText
        {
            get => _worstDiskText;
            private set
            {
                if (_worstDiskText != value)
                {
                    _worstDiskText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CleanupRootsText
        {
            get => _cleanupRootsText;
            private set
            {
                if (_cleanupRootsText != value)
                {
                    _cleanupRootsText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CleanupPolicyText
        {
            get => _cleanupPolicyText;
            private set
            {
                if (_cleanupPolicyText != value)
                {
                    _cleanupPolicyText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LastCleanupText
        {
            get => _lastCleanupText;
            private set
            {
                if (_lastCleanupText != value)
                {
                    _lastCleanupText = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime LastUpdatedAt
        {
            get => _lastUpdatedAt;
            private set
            {
                if (_lastUpdatedAt != value)
                {
                    _lastUpdatedAt = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LastUpdatedText));
                }
            }
        }

        public string LastUpdatedText => LastUpdatedAt == DateTime.MinValue
            ? GetMessage("Sub_entry_DiagnosticsNotUpdatedYet", "Not yet updated")
            : LastUpdatedAt.ToString("dd/MM/yyyy HH:mm:ss");

        public int ActiveAlertCount => _activeAlerts.Count;
        public bool HasActiveAlerts => ActiveAlertCount > 0;
        public int CriticalAlertCount => _activeAlerts.Count(x => x.Severity == DiagnosticsSeverity.Critical);
        public int WarningAlertCount => _activeAlerts.Count(x => x.Severity == DiagnosticsSeverity.Warning);

        // Badge proattivo Fase 9: preallarmi AI (SPC/salute PC) non ancora visti dall'operatore.
        // Distinto dagli allarmi critici di sistema sopra: qui Warning conta gia' (le derive AI
        // sono advisory per natura, il badge serve proprio a farle notare prima che diventino scarto).
        public int AiAdvisoryCount => ServiceLocator.MachineHealthNotificationService?.UnseenCount ?? 0;
        public bool HasAiAdvisories => AiAdvisoryCount > 0;
        public string ActiveAlertSummary => HasActiveAlerts
            ? string.Format(GetMessage("Sub_entry_DiagnosticsAlertsDetectedFormat", "{0} issue(s) detected"), ActiveAlertCount)
            : GetMessage("Sub_entry_DiagnosticsNominal", "PC diagnostics nominal");

        public SystemDiagnosticsService()
        {
            TryInitializeCpuCounter();
            HottestCpuTemperatureSensor = CreateUnavailableTemperatureSensor(
                GetMessage("Sub_entry_CpuTemperature", "CPU temperature"),
                DiagnosticsTemperatureSensorType.Cpu,
                GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            HottestDiskTemperatureSensor = CreateUnavailableTemperatureSensor(
                GetMessage("Sub_entry_HottestDiskTemperature", "Hottest disk temperature"),
                DiagnosticsTemperatureSensorType.Disk,
                GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            MemoryTemperatureSensor = CreateUnavailableTemperatureSensor(
                GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                DiagnosticsTemperatureSensorType.Memory,
                GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            DatabaseArchiveStatus = CreateUnavailableDatabaseArchiveStatus(new SystemDiagnosticsSettings());
        }

        public void Start()
        {
            if (_isStarted || _isShuttingDown || _isDisposed)
                return;

            EnsureConfigDefaults();

            lock (_timerLock)
            {
                if (_refreshTimer == null)
                {
                    _refreshTimer = new Timer(OnRefreshTimerTick, null, Timeout.Infinite, Timeout.Infinite);
                }
            }

            _isStarted = true;
            ConfigureTimer();
            RefreshNowAsync().SafeFireAndForget();
        }

        public async Task RefreshNowAsync()
        {
            if (_isShuttingDown || _isDisposed)
                return;

            bool lockTaken = false;
            try
            {
                lockTaken = await _refreshLock.WaitAsync(0).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                if (_isShuttingDown || _isDisposed)
                    return;

                throw;
            }

            if (!lockTaken)
                return;

            try
            {
                if (_isShuttingDown || _isDisposed)
                    return;

                EnsureConfigDefaults();
                var settings = GetSettings();
                if (!settings.Enabled)
                {
                    await ApplySnapshotAsync(new DiagnosticsSnapshot
                    {
                        CpuUsagePercent = 0,
                        TotalMemoryGb = 0,
                        UsedMemoryGb = 0,
                        MemoryUsagePercent = 0,
                        OverallSeverity = DiagnosticsSeverity.Healthy,
                        WorstDiskText = "Diagnostics disabled",
                        CleanupRootsText = GetCleanupRootsDisplayText(GetCleanupRoots()),
                        CleanupPolicyText = "Diagnostics disabled in configuration",
                        LastCleanupText = LastCleanupText,
                        HottestCpuTemperatureSensor = CreateUnavailableTemperatureSensor(
                            GetMessage("Sub_entry_CpuTemperature", "CPU temperature"),
                            DiagnosticsTemperatureSensorType.Cpu,
                            GetMessage("Sub_entry_TemperatureMonitoringDisabled", "Temperature monitoring disabled in configuration")),
                        HottestDiskTemperatureSensor = CreateUnavailableTemperatureSensor(
                            GetMessage("Sub_entry_HottestDiskTemperature", "Hottest disk temperature"),
                            DiagnosticsTemperatureSensorType.Disk,
                            GetMessage("Sub_entry_TemperatureMonitoringDisabled", "Temperature monitoring disabled in configuration")),
                        MemoryTemperatureSensor = CreateUnavailableTemperatureSensor(
                            GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                            DiagnosticsTemperatureSensorType.Memory,
                            GetMessage("Sub_entry_TemperatureMonitoringDisabled", "Temperature monitoring disabled in configuration")),
                        DatabaseArchiveStatus = CreateUnavailableDatabaseArchiveStatus(settings, GetMessage("Sub_entry_DatabaseArchiveMonitoringDisabled", "Database archive monitoring disabled in configuration")),
                        LastUpdatedAt = DateTime.Now
                    }).ConfigureAwait(false);
                    return;
                }

                var snapshot = await Task.Run(async () =>
                {
                    var cleanupRoots = GetCleanupRoots();
                    var rawDrives = CollectDriveStatus(cleanupRoots, settings);
                    var cleanupInfo = await ExecuteCleanupIfRequiredAsync(rawDrives, cleanupRoots, settings).ConfigureAwait(false);
                    var finalDrives = cleanupInfo != null ? CollectDriveStatus(cleanupRoots, settings) : rawDrives;
                    var temperatureSensors = CollectTemperatureSensors(settings);
                    var databaseArchiveStatus = EvaluateDatabaseArchiveStatus(settings);
                    return BuildSnapshot(finalDrives, cleanupRoots, settings, cleanupInfo, temperatureSensors, databaseArchiveStatus);
                }).ConfigureAwait(false);
                await ApplySnapshotAsync(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if ((_isShuttingDown || _isDisposed) && ex is ObjectDisposedException)
                {
                    return;
                }

                MainWindow.logger?.Error($"SystemDiagnosticsService refresh failed: {ex.Message}");
                _eventLogger.LogException("SYSTEM_DIAGNOSTICS_REFRESH_FAILED", ex, nameof(RefreshNowAsync), "System diagnostics refresh failed");
            }
            finally
            {
                if (lockTaken)
                {
                    try
                    {
                        _refreshLock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        if (!_isShuttingDown && !_isDisposed)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        public async Task ShutdownAsync()
        {
            _isShuttingDown = true;

            lock (_timerLock)
            {
                if (_refreshTimer != null)
                {
                    _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }

            await Task.Delay(50).ConfigureAwait(false);
            Dispose();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isShuttingDown = true;

            lock (_timerLock)
            {
                _refreshTimer?.Dispose();
                _refreshTimer = null;
            }

            if (_cpuCounter != null)
            {
                _cpuCounter.Dispose();
                _cpuCounter = null;
            }

            DisposeHardwareTemperatureProvider();

            // Do not dispose _refreshLock here. Timer callbacks use SafeFireAndForget
            // and can still be returning from RefreshNowAsync during application shutdown.
            // Disposing the semaphore in that window causes noisy ObjectDisposedException
            // logs even though shutdown is proceeding correctly.
        }

        private void OnRefreshTimerTick(object state)
        {
            if (_isShuttingDown || _isDisposed)
                return;

            RefreshNowAsync().SafeFireAndForget();
        }

        private void ConfigureTimer()
        {
            if (_isShuttingDown || _isDisposed)
                return;

            var settings = GetSettings();
            int intervalMs = Math.Max(5, settings.RefreshIntervalSeconds) * 1000;

            lock (_timerLock)
            {
                if (_refreshTimer != null)
                {
                    _refreshTimer.Change(0, intervalMs);
                }
            }
        }

        private DiagnosticsSnapshot BuildSnapshot(IList<SystemDriveStatusItem> drives, IList<string> cleanupRoots,
            SystemDiagnosticsSettings settings, CleanupResult cleanupResult, IList<TemperatureSensorStatusItem> temperatureSensors,
            DatabaseArchiveStatusItem databaseArchiveStatus)
        {
            var snapshot = new DiagnosticsSnapshot();
            snapshot.CpuUsagePercent = ReadCpuUsage();

            double totalMemoryGb;
            double usedMemoryGb;
            double memoryUsagePercent;
            ReadMemoryUsage(out totalMemoryGb, out usedMemoryGb, out memoryUsagePercent);

            snapshot.TotalMemoryGb = totalMemoryGb;
            snapshot.UsedMemoryGb = usedMemoryGb;
            snapshot.MemoryUsagePercent = memoryUsagePercent;
            snapshot.Drives = drives.ToList();
            snapshot.TemperatureSensors = (temperatureSensors ?? new List<TemperatureSensorStatusItem>()).ToList();
            snapshot.HottestCpuTemperatureSensor = snapshot.TemperatureSensors
                .Where(x => x.SensorType == DiagnosticsTemperatureSensorType.Cpu && x.IsAvailable && x.TemperatureC.HasValue)
                .OrderByDescending(x => x.TemperatureC.Value)
                .FirstOrDefault()
                ?? CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_CpuTemperature", "CPU temperature"),
                    DiagnosticsTemperatureSensorType.Cpu,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            snapshot.HottestDiskTemperatureSensor = snapshot.TemperatureSensors
                .Where(x => x.SensorType == DiagnosticsTemperatureSensorType.Disk && x.IsAvailable && x.TemperatureC.HasValue)
                .OrderByDescending(x => x.TemperatureC.Value)
                .FirstOrDefault()
                ?? CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_HottestDiskTemperature", "Hottest disk temperature"),
                    DiagnosticsTemperatureSensorType.Disk,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            snapshot.MemoryTemperatureSensor = snapshot.TemperatureSensors
                .Where(x => x.SensorType == DiagnosticsTemperatureSensorType.Memory ||
                            x.SensorType == DiagnosticsTemperatureSensorType.Motherboard)
                .OrderByDescending(x => x.IsAvailable && x.TemperatureC.HasValue ? x.TemperatureC.Value : double.MinValue)
                .FirstOrDefault()
                ?? CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                    DiagnosticsTemperatureSensorType.Memory,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            snapshot.DatabaseArchiveStatus = databaseArchiveStatus ?? CreateUnavailableDatabaseArchiveStatus(settings);
            snapshot.CleanupRootsText = GetCleanupRootsDisplayText(cleanupRoots);
            snapshot.CleanupPolicyText = string.Format(
                GetMessage("Sub_entry_DiagnosticsCleanupPolicyFormat", "Auto cleanup {0}. Start {1:0}% / Target {2:0}% / Cooldown {3} min"),
                settings.AutoCleanupEnabled ? GetMessage("Sub_entry_DiagnosticsCleanupEnabled", "enabled") : GetMessage("Sub_entry_DiagnosticsCleanupDisabled", "disabled"),
                settings.DiskCleanupStartPercent,
                settings.DiskCleanupTargetPercent,
                settings.CleanupCooldownMinutes);
            snapshot.LastCleanupText = cleanupResult != null && cleanupResult.CleanupExecuted
                ? string.Format(GetMessage("Sub_entry_DiagnosticsLastCleanupFormat", "Last cleanup {0}: {1}"), DateTime.Now.ToString("HH:mm:ss"), cleanupResult.Details)
                : LastCleanupText;
            snapshot.LastUpdatedAt = DateTime.Now;

            var alerts = new List<SystemDiagnosticsAlertItem>();

            EvaluateMetric(alerts,
                "cpu",
                GetMessage("Sub_entry_CpuLoad", "CPU load"),
                string.Format(GetMessage("Sub_entry_DiagnosticsCpuAlertMessage", "CPU usage {0:0.0}% exceeds configured threshold."), snapshot.CpuUsagePercent),
                snapshot.CpuUsagePercent >= settings.CpuCriticalPercent ? DiagnosticsSeverity.Critical :
                snapshot.CpuUsagePercent >= settings.CpuWarningPercent ? DiagnosticsSeverity.Warning :
                DiagnosticsSeverity.Healthy,
                "SystemDiagnosticsService",
                new Dictionary<string, object>
                {
                    { "cpu_usage_percent", snapshot.CpuUsagePercent.ToString("0.0") },
                    { "warning_threshold", settings.CpuWarningPercent.ToString("0.0") },
                    { "critical_threshold", settings.CpuCriticalPercent.ToString("0.0") }
                });

            EvaluateMetric(alerts,
                "ram",
                GetMessage("Sub_entry_RamUsage", "RAM usage"),
                string.Format(GetMessage("Sub_entry_DiagnosticsRamAlertMessage", "RAM usage {0:0.0}% ({1:0.0}/{2:0.0} GB) exceeds configured threshold."),
                    snapshot.MemoryUsagePercent,
                    snapshot.UsedMemoryGb,
                    snapshot.TotalMemoryGb),
                snapshot.MemoryUsagePercent >= settings.RamCriticalPercent ? DiagnosticsSeverity.Critical :
                snapshot.MemoryUsagePercent >= settings.RamWarningPercent ? DiagnosticsSeverity.Warning :
                DiagnosticsSeverity.Healthy,
                "SystemDiagnosticsService",
                new Dictionary<string, object>
                {
                    { "memory_usage_percent", snapshot.MemoryUsagePercent.ToString("0.0") },
                    { "used_memory_gb", snapshot.UsedMemoryGb.ToString("0.0") },
                    { "total_memory_gb", snapshot.TotalMemoryGb.ToString("0.0") }
                });

            foreach (var drive in snapshot.Drives)
            {
                EvaluateMetric(alerts,
                    "disk:" + drive.RootPath,
                    drive.DriveName,
                    string.Format(GetMessage("Sub_entry_DiagnosticsDiskAlertMessage", "Disk {0} is {1:0.0}% used. Free space: {2:0.0} GB."), drive.RootPath, drive.UsedPercent, drive.FreeSpaceGb),
                    drive.Severity,
                    "SystemDiagnosticsService",
                    new Dictionary<string, object>
                    {
                        { "drive", drive.RootPath },
                        { "used_percent", drive.UsedPercent.ToString("0.0") },
                        { "free_gb", drive.FreeSpaceGb.ToString("0.0") },
                        { "cleanup_target", drive.IsCleanupTarget }
                });
            }

            if (snapshot.DatabaseArchiveStatus.IsAvailable)
            {
                EvaluateMetric(alerts,
                    "db-archive:" + snapshot.DatabaseArchiveStatus.TableName,
                    GetMessage("Sub_entry_DatabaseArchiveTitle", "Database archive"),
                    string.Format(
                        GetMessage("Sub_entry_DatabaseArchiveAlertMessage", "Archive table {0} is {1:0.0} MB. Target size {2:0.0} MB, cleanup start {3:0.0} MB."),
                        snapshot.DatabaseArchiveStatus.TableName,
                        snapshot.DatabaseArchiveStatus.SizeMb,
                        snapshot.DatabaseArchiveStatus.CleanupTargetMb,
                        snapshot.DatabaseArchiveStatus.CleanupStartMb),
                    snapshot.DatabaseArchiveStatus.Severity,
                    "SystemDiagnosticsService",
                    new Dictionary<string, object>
                    {
                        { "table", snapshot.DatabaseArchiveStatus.TableName },
                        { "size_mb", snapshot.DatabaseArchiveStatus.SizeMb.ToString("0.0") },
                        { "row_count", snapshot.DatabaseArchiveStatus.EstimatedRowCount.ToString() },
                        { "cleanup_start_mb", snapshot.DatabaseArchiveStatus.CleanupStartMb.ToString("0.0") },
                        { "cleanup_target_mb", snapshot.DatabaseArchiveStatus.CleanupTargetMb.ToString("0.0") }
                    });
            }

            foreach (var sensor in snapshot.TemperatureSensors.Where(x => x.IsAvailable && x.TemperatureC.HasValue))
            {
                EvaluateMetric(alerts,
                    "temperature:" + sensor.SensorType + ":" + sensor.Name,
                    sensor.Name,
                    string.Format(
                        GetMessage("Sub_entry_DiagnosticsTemperatureAlertMessage", "{0} temperature is {1:0.0} C."),
                        sensor.Name,
                        sensor.TemperatureC.Value),
                    sensor.Severity,
                    "SystemDiagnosticsService",
                    new Dictionary<string, object>
                    {
                        { "sensor", sensor.Name },
                        { "sensor_type", sensor.SensorType.ToString() },
                        { "temperature_c", sensor.TemperatureC.Value.ToString("0.0") },
                        { "details", sensor.Details ?? string.Empty }
                    });
            }

            snapshot.Alerts = alerts
                .OrderByDescending(x => x.Severity)
                .ThenByDescending(x => x.Timestamp)
                .ToList();

            snapshot.OverallSeverity = snapshot.Alerts.Any()
                ? snapshot.Alerts.Max(x => x.Severity)
                : DiagnosticsSeverity.Healthy;

            var worstDrive = snapshot.Drives
                .OrderByDescending(x => x.UsedPercent)
                .FirstOrDefault();

            snapshot.WorstDiskText = worstDrive == null
                ? GetMessage("Sub_entry_DiagnosticsNoMountedDisksDetected", "No mounted disks detected")
                : string.Format("{0} - {1:0.0}% used", worstDrive.RootPath, worstDrive.UsedPercent);

            return snapshot;
        }

        private void EvaluateMetric(List<SystemDiagnosticsAlertItem> alerts, string key, string title, string message,
            DiagnosticsSeverity severity, string source, IDictionary<string, object> metadata)
        {
            DiagnosticsSeverity previousSeverity;
            if (!_lastSeverityByKey.TryGetValue(key, out previousSeverity))
            {
                previousSeverity = DiagnosticsSeverity.Healthy;
            }

            if (severity != DiagnosticsSeverity.Healthy)
            {
                alerts.Add(new SystemDiagnosticsAlertItem
                {
                    Key = key,
                    Title = title,
                    Message = message,
                    Category = "PC Diagnostics",
                    Severity = severity,
                    Timestamp = DateTime.Now
                });
            }

            if (previousSeverity == severity)
            {
                return;
            }

            _lastSeverityByKey[key] = severity;

            if (severity == DiagnosticsSeverity.Healthy)
            {
                _eventLogger.LogOperationalEvent(
                    LogLevel.Info,
                    "SYSTEM_DIAGNOSTICS_RECOVERED",
                    "SystemDiagnostics",
                    title + " recovered",
                    source,
                    message,
                    metadata);
                return;
            }

            _eventLogger.LogOperationalEvent(
                severity == DiagnosticsSeverity.Critical ? LogLevel.Error : LogLevel.Warn,
                severity == DiagnosticsSeverity.Critical ? "SYSTEM_DIAGNOSTICS_CRITICAL" : "SYSTEM_DIAGNOSTICS_WARNING",
                "SystemDiagnostics",
                title,
                source,
                message,
                metadata);
        }

        private async Task<CleanupResult> ExecuteCleanupIfRequiredAsync(IList<SystemDriveStatusItem> drives, IList<string> cleanupRoots,
            SystemDiagnosticsSettings settings)
        {
            if (!settings.AutoCleanupEnabled || cleanupRoots.Count == 0)
                return null;

            var targetDrive = drives
                .Where(x => x.IsCleanupTarget && x.UsedPercent >= settings.DiskCleanupStartPercent)
                .OrderByDescending(x => x.UsedPercent)
                .FirstOrDefault();

            if (targetDrive == null)
                return null;

            DateTime lastCleanupAt;
            if (_lastCleanupByDrive.TryGetValue(targetDrive.RootPath, out lastCleanupAt))
            {
                if ((DateTime.Now - lastCleanupAt).TotalMinutes < Math.Max(1, settings.CleanupCooldownMinutes))
                {
                    return null;
                }
            }

            _lastCleanupByDrive[targetDrive.RootPath] = DateTime.Now;

            var rootsForDrive = cleanupRoots
                .Where(path => string.Equals(Path.GetPathRoot(path), targetDrive.RootPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!rootsForDrive.Any())
            {
                _eventLogger.LogOperationalEvent(
                    LogLevel.Warn,
                    "DISK_CLEANUP_SKIPPED",
                    "SystemDiagnostics",
                    "Disk cleanup skipped because no configured image folders are present on the affected drive.",
                    nameof(ExecuteCleanupIfRequiredAsync),
                    targetDrive.RootPath,
                    new Dictionary<string, object>
                    {
                        { "drive", targetDrive.RootPath },
                        { "used_percent", targetDrive.UsedPercent.ToString("0.0") }
                    });
                return null;
            }

            _eventLogger.LogOperationalEvent(
                LogLevel.Warn,
                "DISK_CLEANUP_STARTED",
                "SystemDiagnostics",
                "Automatic disk cleanup started.",
                nameof(ExecuteCleanupIfRequiredAsync),
                targetDrive.RootPath,
                new Dictionary<string, object>
                {
                    { "drive", targetDrive.RootPath },
                    { "used_percent", targetDrive.UsedPercent.ToString("0.0") },
                    { "folders", string.Join(" | ", rootsForDrive) }
                });

            var result = await Task.Run(() => CleanupDrive(targetDrive.RootPath, rootsForDrive, settings)).ConfigureAwait(false);
            if (result.CleanupExecuted)
            {
                _eventLogger.LogOperationalEvent(
                    result.TargetReached ? LogLevel.Info : LogLevel.Warn,
                    result.TargetReached ? "DISK_CLEANUP_COMPLETED" : "DISK_CLEANUP_PARTIAL",
                    "SystemDiagnostics",
                    "Automatic disk cleanup completed.",
                    nameof(ExecuteCleanupIfRequiredAsync),
                    result.Details,
                    new Dictionary<string, object>
                    {
                        { "drive", result.DriveRoot },
                        { "deleted_folders", result.DeletedFolders },
                        { "deleted_files", result.DeletedFiles },
                        { "final_used_percent", result.FinalUsedPercent.ToString("0.0") }
                    });
            }
            else
            {
                _eventLogger.LogOperationalEvent(
                    LogLevel.Error,
                    "DISK_CLEANUP_FAILED",
                    "SystemDiagnostics",
                    "Automatic disk cleanup could not release space.",
                    nameof(ExecuteCleanupIfRequiredAsync),
                    result.Details,
                    new Dictionary<string, object>
                    {
                        { "drive", result.DriveRoot },
                        { "final_used_percent", result.FinalUsedPercent.ToString("0.0") }
                    });
            }

            return result;
        }

        private CleanupResult CleanupDrive(string driveRoot, IList<string> roots, SystemDiagnosticsSettings settings)
        {
            var result = new CleanupResult
            {
                DriveRoot = driveRoot,
                FinalUsedPercent = GetDriveUsedPercent(driveRoot)
            };

            if (result.FinalUsedPercent < settings.DiskCleanupStartPercent)
            {
                result.Details = "Drive no longer above cleanup threshold.";
                return result;
            }

            var candidateFolders = roots
                .Where(Directory.Exists)
                .SelectMany(root => SafeEnumeratePieceFolders(root))
                .OrderBy(path => GetLastWriteUtcSafe(path))
                .ToList();

            foreach (var folder in candidateFolders)
            {
                if (result.DeletedFolders >= Math.Max(1, settings.MaxDeletedFoldersPerCycle))
                    break;

                try
                {
                    Directory.Delete(folder, true);
                    result.DeletedFolders++;
                    result.CleanupExecuted = true;
                    CleanupEmptyAncestors(folder, roots);
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn($"Unable to delete folder '{folder}': {ex.Message}");
                }

                result.FinalUsedPercent = GetDriveUsedPercent(driveRoot);
                if (result.FinalUsedPercent <= settings.DiskCleanupTargetPercent)
                {
                    result.TargetReached = true;
                    break;
                }
            }

            if (!result.TargetReached)
            {
                var candidateFiles = roots
                    .Where(Directory.Exists)
                    .SelectMany(root => SafeEnumerateFiles(root))
                    .OrderBy(path => GetLastWriteUtcSafe(path))
                    .ToList();

                foreach (var file in candidateFiles)
                {
                    if (result.DeletedFolders + result.DeletedFiles >= Math.Max(1, settings.MaxDeletedFoldersPerCycle))
                        break;

                    try
                    {
                        File.Delete(file);
                        result.DeletedFiles++;
                        result.CleanupExecuted = true;
                    }
                    catch (Exception ex)
                    {
                        MainWindow.logger?.Warn($"Unable to delete file '{file}': {ex.Message}");
                    }

                    result.FinalUsedPercent = GetDriveUsedPercent(driveRoot);
                    if (result.FinalUsedPercent <= settings.DiskCleanupTargetPercent)
                    {
                        result.TargetReached = true;
                        break;
                    }
                }
            }

            if (!result.CleanupExecuted)
            {
                result.Details = "No removable image folders or files found in configured roots.";
            }
            else
            {
                result.Details = string.Format(
                    "Deleted {0} folder(s) and {1} file(s). Drive usage is now {2:0.0}%.",
                    result.DeletedFolders,
                    result.DeletedFiles,
                    result.FinalUsedPercent);
            }

            return result;
        }

        private IList<SystemDriveStatusItem> CollectDriveStatus(IList<string> cleanupRoots, SystemDiagnosticsSettings settings)
        {
            var cleanupRootsByDrive = cleanupRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .GroupBy(path => Path.GetPathRoot(path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.DriveType != DriveType.CDRom)
                .Select(drive =>
                {
                    var totalGb = BytesToGb(drive.TotalSize);
                    var freeGb = BytesToGb(drive.TotalFreeSpace);
                    var usedGb = Math.Max(0, totalGb - freeGb);
                    var usedPercent = totalGb <= 0 ? 0 : (usedGb / totalGb) * 100;
                    var freePercent = 100 - usedPercent;
                    var root = drive.RootDirectory.FullName;

                    List<string> rootsForDrive;
                    cleanupRootsByDrive.TryGetValue(root, out rootsForDrive);
                    rootsForDrive = rootsForDrive ?? new List<string>();

                    return new SystemDriveStatusItem
                    {
                        DriveName = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                            ? root.TrimEnd('\\')
                            : string.Format("{0} ({1})", drive.VolumeLabel, root.TrimEnd('\\')),
                        RootPath = root,
                        VolumeLabel = drive.VolumeLabel,
                        DriveType = drive.DriveType.ToString(),
                        TotalSizeGb = totalGb,
                        FreeSpaceGb = freeGb,
                        UsedSpaceGb = usedGb,
                        UsedPercent = usedPercent,
                        FreePercent = freePercent,
                        IsCleanupTarget = rootsForDrive.Any(),
                        CleanupFoldersDisplay = rootsForDrive.Any() ? string.Join(" | ", rootsForDrive) : GetMessage("Sub_entry_DiagnosticsNoCleanupFolderForDrive", "No cleanup folder configured for this drive"),
                        Severity = usedPercent >= settings.DiskCriticalPercent
                            ? DiagnosticsSeverity.Critical
                            : usedPercent >= settings.DiskWarningPercent
                                ? DiagnosticsSeverity.Warning
                                : DiagnosticsSeverity.Healthy
                    };
                })
                .OrderByDescending(x => x.UsedPercent)
                .ToList();
        }

        private void ReadMemoryUsage(out double totalMemoryGb, out double usedMemoryGb, out double memoryUsagePercent)
        {
            var memoryStatus = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(memoryStatus))
            {
                totalMemoryGb = 0;
                usedMemoryGb = 0;
                memoryUsagePercent = 0;
                return;
            }

            totalMemoryGb = BytesToGb((long)memoryStatus.TotalPhys);
            var freeGb = BytesToGb((long)memoryStatus.AvailPhys);
            usedMemoryGb = Math.Max(0, totalMemoryGb - freeGb);
            memoryUsagePercent = totalMemoryGb <= 0 ? 0 : (usedMemoryGb / totalMemoryGb) * 100;
        }

        private double ReadCpuUsage()
        {
            try
            {
                if (_cpuCounter == null)
                {
                    TryInitializeCpuCounter();
                }

                if (_cpuCounter == null)
                    return 0;

                var value = _cpuCounter.NextValue();
                if (!_cpuCounterPrimed)
                {
                    _cpuCounterPrimed = true;
                    return 0;
                }

                return Math.Max(0, Math.Min(100, value));
            }
            catch
            {
                return 0;
            }
        }

        private void TryInitializeCpuCounter()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
                _cpuCounterPrimed = false;
            }
            catch
            {
                _cpuCounter = null;
            }
        }

        private IList<string> GetCleanupRoots()
        {
            var roots = new List<string>();
            var config = MainWindow.configManager?.Config;
            if (config == null)
                return roots;

            if (!string.IsNullOrWhiteSpace(config.Configuration.ImageDir))
            {
                roots.Add(NormalizeDirectoryPath(config.Configuration.ImageDir));
            }

            var settings = GetSettings();
            if (!string.IsNullOrWhiteSpace(settings.AdditionalCleanupFolders))
            {
                foreach (var rawPath in settings.AdditionalCleanupFolders
                    .Split(new[] { ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    roots.Add(NormalizeDirectoryPath(rawPath));
                }
            }

            return roots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                var fullPath = Path.GetFullPath(path.Trim());
                return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private string GetCleanupRootsDisplayText(IList<string> cleanupRoots)
        {
            return cleanupRoots.Any()
                ? string.Join(Environment.NewLine, cleanupRoots)
                : GetMessage("Sub_entry_DiagnosticsNoCleanupFoldersConfigured", "No cleanup folders configured");
        }

        private SystemDiagnosticsSettings GetSettings()
        {
            var config = MainWindow.configManager?.Config;
            if (config == null)
                return new SystemDiagnosticsSettings();

            return config.SystemDiagnostics ?? new SystemDiagnosticsSettings();
        }

        private void EnsureConfigDefaults()
        {
            var config = MainWindow.configManager?.Config;
            if (config == null)
                return;

            var changed = false;
            if (config.SystemDiagnostics == null)
            {
                config.SystemDiagnostics = new SystemDiagnosticsSettings();
                changed = true;
            }

            if (config.SystemDiagnostics.RefreshIntervalSeconds <= 0)
            {
                config.SystemDiagnostics.RefreshIntervalSeconds = 10;
                changed = true;
            }

            if (config.SystemDiagnostics.DiskCleanupTargetPercent >= config.SystemDiagnostics.DiskCleanupStartPercent)
            {
                config.SystemDiagnostics.DiskCleanupTargetPercent = Math.Max(10, config.SystemDiagnostics.DiskCleanupStartPercent - 5);
                changed = true;
            }

            if (config.SystemDiagnostics.DatabaseArchiveCleanupTargetMb <= 0)
            {
                config.SystemDiagnostics.DatabaseArchiveCleanupTargetMb = 1536;
                changed = true;
            }

            if (config.SystemDiagnostics.DatabaseArchiveCleanupStartMb <= config.SystemDiagnostics.DatabaseArchiveCleanupTargetMb)
            {
                config.SystemDiagnostics.DatabaseArchiveCleanupStartMb = Math.Max(
                    config.SystemDiagnostics.DatabaseArchiveCleanupTargetMb + 128,
                    2048);
                changed = true;
            }

            if (config.SystemDiagnostics.DatabaseArchiveRetentionDays <= 0)
            {
                config.SystemDiagnostics.DatabaseArchiveRetentionDays = 90;
                changed = true;
            }

            if (config.SystemDiagnostics.DatabaseArchiveDeleteBatchSize <= 0)
            {
                config.SystemDiagnostics.DatabaseArchiveDeleteBatchSize = 5000;
                changed = true;
            }

            if (config.SystemDiagnostics.DatabaseArchiveCleanupCooldownMinutes <= 0)
            {
                config.SystemDiagnostics.DatabaseArchiveCleanupCooldownMinutes = 60;
                changed = true;
            }

            var normalizedTemperatureProvider = NormalizeTemperatureProvider(
                config.SystemDiagnostics.HardwareTemperatureProvider);
            if (!string.Equals(config.SystemDiagnostics.HardwareTemperatureProvider,
                normalizedTemperatureProvider, StringComparison.Ordinal))
            {
                config.SystemDiagnostics.HardwareTemperatureProvider = normalizedTemperatureProvider;
                changed = true;
            }

            if (config.SystemDiagnostics.HardwareTemperaturePollingSeconds < 5 ||
                config.SystemDiagnostics.HardwareTemperaturePollingSeconds > 300)
            {
                config.SystemDiagnostics.HardwareTemperaturePollingSeconds = Math.Max(
                    5,
                    Math.Min(300, config.SystemDiagnostics.HardwareTemperaturePollingSeconds));
                changed = true;
            }

            if (config.SystemDiagnostics.CpuTemperatureCriticalC < config.SystemDiagnostics.CpuTemperatureWarningC)
            {
                config.SystemDiagnostics.CpuTemperatureCriticalC = Math.Max(
                    config.SystemDiagnostics.CpuTemperatureWarningC + 5,
                    90);
                changed = true;
            }

            if (config.SystemDiagnostics.DiskTemperatureCriticalC < config.SystemDiagnostics.DiskTemperatureWarningC)
            {
                config.SystemDiagnostics.DiskTemperatureCriticalC = Math.Max(
                    config.SystemDiagnostics.DiskTemperatureWarningC + 5,
                    60);
                changed = true;
            }

            if (config.SystemDiagnostics.MemoryTemperatureCriticalC < config.SystemDiagnostics.MemoryTemperatureWarningC)
            {
                config.SystemDiagnostics.MemoryTemperatureCriticalC = Math.Max(
                    config.SystemDiagnostics.MemoryTemperatureWarningC + 5,
                    75);
                changed = true;
            }

            if (config.SystemDiagnostics.MotherboardTemperatureCriticalC < config.SystemDiagnostics.MotherboardTemperatureWarningC)
            {
                config.SystemDiagnostics.MotherboardTemperatureCriticalC = Math.Max(
                    config.SystemDiagnostics.MotherboardTemperatureWarningC + 5,
                    80);
                changed = true;
            }

            if (changed)
            {
                MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
            }
        }

        private List<TemperatureSensorStatusItem> CollectTemperatureSensors(SystemDiagnosticsSettings settings)
        {
            var sensors = new List<TemperatureSensorStatusItem>();

            if (!settings.TemperatureMonitoringEnabled)
            {
                DisposeHardwareTemperatureProvider();
                sensors.Add(CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_CpuTemperature", "CPU temperature"),
                    DiagnosticsTemperatureSensorType.Cpu,
                    GetMessage("Sub_entry_TemperatureMonitoringDisabled", "Temperature monitoring disabled in configuration")));
                sensors.Add(CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_HottestDiskTemperature", "Hottest disk temperature"),
                    DiagnosticsTemperatureSensorType.Disk,
                    GetMessage("Sub_entry_TemperatureMonitoringDisabled", "Temperature monitoring disabled in configuration")));
                sensors.Add(CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                    DiagnosticsTemperatureSensorType.Memory,
                    GetMessage("Sub_entry_TemperatureMonitoringDisabled", "Temperature monitoring disabled in configuration")));
                return sensors;
            }

            var providerMode = NormalizeTemperatureProvider(settings.HardwareTemperatureProvider);
            string providerStatus = null;
            if (!string.Equals(providerMode, "WindowsWmi", StringComparison.OrdinalIgnoreCase))
            {
                var readings = ReadLibreHardwareTemperatures(settings, providerMode, out providerStatus);
                sensors.AddRange(readings.Select(x => CreateTemperatureSensor(x, settings)));
            }
            else
            {
                DisposeHardwareTemperatureProvider();
            }

            var allowWmiFallback = string.Equals(providerMode, "Auto", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(providerMode, "WindowsWmi", StringComparison.OrdinalIgnoreCase);

            if (!sensors.Any(x => x.SensorType == DiagnosticsTemperatureSensorType.Disk && x.IsAvailable))
            {
                if (allowWmiFallback)
                {
                    sensors.AddRange(CollectDiskTemperatureSensors(settings));
                }
                else
                {
                    sensors.Add(CreateUnavailableTemperatureSensor(
                        GetMessage("Sub_entry_HottestDiskTemperature", "Hottest disk temperature"),
                        DiagnosticsTemperatureSensorType.Disk,
                        GetProviderUnavailableDetails(providerStatus)));
                }
            }

            if (!sensors.Any(x => (x.SensorType == DiagnosticsTemperatureSensorType.Memory ||
                                   x.SensorType == DiagnosticsTemperatureSensorType.Motherboard) && x.IsAvailable))
            {
                if (allowWmiFallback)
                {
                    sensors.Add(CollectMemoryTemperatureSensor(settings));
                }
                else
                {
                    sensors.Add(CreateUnavailableTemperatureSensor(
                        GetMessage("Sub_entry_SystemTemperature", "System / memory temperature"),
                        DiagnosticsTemperatureSensorType.Memory,
                        GetProviderUnavailableDetails(providerStatus)));
                }
            }

            if (!sensors.Any(x => x.SensorType == DiagnosticsTemperatureSensorType.Cpu && x.IsAvailable))
            {
                sensors.Add(CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_CpuTemperature", "CPU temperature"),
                    DiagnosticsTemperatureSensorType.Cpu,
                    GetProviderUnavailableDetails(providerStatus)));
            }

            return sensors;
        }

        private IReadOnlyList<HardwareTemperatureReading> ReadLibreHardwareTemperatures(
            SystemDiagnosticsSettings settings, string providerMode, out string status)
        {
            lock (_temperatureProviderLock)
            {
                try
                {
                    if (_hardwareTemperatureProvider == null ||
                        !string.Equals(_hardwareTemperatureProviderMode, providerMode, StringComparison.OrdinalIgnoreCase))
                    {
                        _hardwareTemperatureProvider?.Dispose();
                        _hardwareTemperatureProvider = new LibreHardwareTemperatureProvider();
                        _hardwareTemperatureProviderMode = providerMode;
                        _lastHardwareTemperatureProviderLog = null;
                    }

                    var readings = _hardwareTemperatureProvider.Read(
                        settings.HardwareTemperaturePollingSeconds,
                        out status);
                    LogHardwareTemperatureProviderStatus(providerMode, status, readings.Count);
                    return readings;
                }
                catch (Exception ex)
                {
                    status = ex.GetBaseException().Message;
                    LogHardwareTemperatureProviderStatus(providerMode, status, 0);
                    return Array.Empty<HardwareTemperatureReading>();
                }
            }
        }

        private TemperatureSensorStatusItem CreateTemperatureSensor(HardwareTemperatureReading reading,
            SystemDiagnosticsSettings settings)
        {
            double warningThreshold;
            double criticalThreshold;
            switch (reading.SensorType)
            {
                case DiagnosticsTemperatureSensorType.Cpu:
                    warningThreshold = settings.CpuTemperatureWarningC;
                    criticalThreshold = settings.CpuTemperatureCriticalC;
                    break;
                case DiagnosticsTemperatureSensorType.Disk:
                    warningThreshold = settings.DiskTemperatureWarningC;
                    criticalThreshold = settings.DiskTemperatureCriticalC;
                    break;
                case DiagnosticsTemperatureSensorType.Motherboard:
                    warningThreshold = settings.MotherboardTemperatureWarningC;
                    criticalThreshold = settings.MotherboardTemperatureCriticalC;
                    break;
                default:
                    warningThreshold = settings.MemoryTemperatureWarningC;
                    criticalThreshold = settings.MemoryTemperatureCriticalC;
                    break;
            }

            return CreateTemperatureSensor(
                reading.DisplayName,
                reading.SensorType,
                reading.TemperatureC,
                warningThreshold,
                criticalThreshold,
                reading.Details);
        }

        private static string NormalizeTemperatureProvider(string provider)
        {
            if (string.Equals(provider, "WindowsWmi", StringComparison.OrdinalIgnoreCase))
                return "WindowsWmi";
            if (string.Equals(provider, "LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase))
                return "LibreHardwareMonitor";
            return "Auto";
        }

        private string GetProviderUnavailableDetails(string status)
        {
            var providerIsActive = string.Equals(status, "Cached", StringComparison.OrdinalIgnoreCase) ||
                                   (!string.IsNullOrWhiteSpace(status) &&
                                    status.StartsWith("Active", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(status) || providerIsActive
                ? GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available")
                : string.Format(
                    GetMessage("Sub_entry_TemperatureProviderUnavailableFormat", "Hardware provider unavailable: {0}"),
                    status);
        }

        private void LogHardwareTemperatureProviderStatus(string mode, string status, int sensorCount)
        {
            var isActive = string.Equals(status, "Cached", StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrWhiteSpace(status) && status.StartsWith("Active", StringComparison.OrdinalIgnoreCase));
            var state = isActive ? "active" : (status ?? "unavailable");
            var signature = string.Format("{0}|{1}", mode, state);
            if (string.Equals(_lastHardwareTemperatureProviderLog, signature, StringComparison.Ordinal))
                return;

            _lastHardwareTemperatureProviderLog = signature;
            if (isActive)
            {
                MainWindow.logger?.Info(
                    $"SYSTEM_DIAGNOSTICS_TEMPERATURE_PROVIDER_ACTIVE|provider={mode}; sensors={sensorCount}");
            }
            else
            {
                MainWindow.logger?.Warn(
                    $"SYSTEM_DIAGNOSTICS_TEMPERATURE_PROVIDER_FALLBACK|provider={mode}; reason={state}");
            }
        }

        private void DisposeHardwareTemperatureProvider()
        {
            lock (_temperatureProviderLock)
            {
                _hardwareTemperatureProvider?.Dispose();
                _hardwareTemperatureProvider = null;
                _hardwareTemperatureProviderMode = null;
                _lastHardwareTemperatureProviderLog = null;
            }
        }

        private IEnumerable<TemperatureSensorStatusItem> CollectDiskTemperatureSensors(SystemDiagnosticsSettings settings)
        {
            var sensors = new List<TemperatureSensorStatusItem>();

            try
            {
                var diskNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var diskSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT DeviceId, FriendlyName FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject disk in diskSearcher.Get())
                    {
                        var deviceId = Convert.ToString(disk["DeviceId"]);
                        if (string.IsNullOrWhiteSpace(deviceId))
                            continue;

                        var friendlyName = Convert.ToString(disk["FriendlyName"]);
                        diskNamesById[deviceId] = string.IsNullOrWhiteSpace(friendlyName)
                            ? string.Format("{0} {1}", GetMessage("Sub_entry_DiskTemperature", "Disk temperature"), deviceId)
                            : friendlyName.Trim();
                    }
                }

                using (var counterSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT DeviceId, Temperature, TemperatureMax FROM MSFT_StorageReliabilityCounter"))
                {
                    foreach (ManagementObject counter in counterSearcher.Get())
                    {
                        var deviceId = Convert.ToString(counter["DeviceId"]);
                        var temperature = ConvertToNullableDouble(counter["Temperature"]);
                        if (!temperature.HasValue || temperature.Value <= 0 || temperature.Value > 120)
                            continue;

                        string diskName;
                        if (!diskNamesById.TryGetValue(deviceId ?? string.Empty, out diskName))
                        {
                            diskName = string.Format("{0} {1}",
                                GetMessage("Sub_entry_DiskTemperature", "Disk temperature"),
                                string.IsNullOrWhiteSpace(deviceId) ? "?" : deviceId);
                        }

                        var maxTemperature = ConvertToNullableDouble(counter["TemperatureMax"]);
                        var details = maxTemperature.HasValue && maxTemperature.Value > 0
                            ? string.Format(GetMessage("Sub_entry_DiskTemperatureDetailsFormat", "Peak {0:0.0} C"), maxTemperature.Value)
                            : GetMessage("Sub_entry_DiskTemperatureSourceWmi", "WMI storage reliability counter");

                        sensors.Add(CreateTemperatureSensor(
                            diskName,
                            DiagnosticsTemperatureSensorType.Disk,
                            temperature.Value,
                            settings.DiskTemperatureWarningC,
                            settings.DiskTemperatureCriticalC,
                            details));
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"SystemDiagnostics disk temperature unavailable: {ex.Message}");
            }

            if (!sensors.Any())
            {
                sensors.Add(CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_HottestDiskTemperature", "Hottest disk temperature"),
                    DiagnosticsTemperatureSensorType.Disk,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available")));
            }

            return sensors;
        }

        private TemperatureSensorStatusItem CollectMemoryTemperatureSensor(SystemDiagnosticsSettings settings)
        {
            try
            {
                TemperatureSensorStatusItem hottestZone = null;
                using (var thermalSearcher = new ManagementObjectSearcher(
                    @"root\WMI",
                    "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (ManagementObject zone in thermalSearcher.Get())
                    {
                        var currentTemperature = ConvertToNullableDouble(zone["CurrentTemperature"]);
                        var temperatureC = ConvertAcpiTemperatureToCelsius(currentTemperature);
                        if (!temperatureC.HasValue || temperatureC.Value < -20 || temperatureC.Value > 140)
                            continue;

                        var instanceName = Convert.ToString(zone["InstanceName"]);
                        var item = CreateTemperatureSensor(
                            GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                            DiagnosticsTemperatureSensorType.Memory,
                            temperatureC.Value,
                            settings.MemoryTemperatureWarningC,
                            settings.MemoryTemperatureCriticalC,
                            string.IsNullOrWhiteSpace(instanceName)
                                ? GetMessage("Sub_entry_MemoryTemperatureSourceAcpi", "ACPI thermal zone")
                                : instanceName);

                        if (hottestZone == null ||
                            (item.TemperatureC.HasValue && hottestZone.TemperatureC.HasValue && item.TemperatureC.Value > hottestZone.TemperatureC.Value))
                        {
                            hottestZone = item;
                        }
                    }
                }

                return hottestZone ?? CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                    DiagnosticsTemperatureSensorType.Memory,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"SystemDiagnostics memory temperature unavailable: {ex.Message}");
                return CreateUnavailableTemperatureSensor(
                    GetMessage("Sub_entry_MemoryTemperature", "Memory temperature"),
                    DiagnosticsTemperatureSensorType.Memory,
                    GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available"));
            }
        }

        private TemperatureSensorStatusItem CreateTemperatureSensor(string name,
            DiagnosticsTemperatureSensorType sensorType, double temperatureC, double warningThreshold,
            double criticalThreshold, string details)
        {
            return new TemperatureSensorStatusItem
            {
                Name = name,
                SensorType = sensorType,
                TemperatureC = temperatureC,
                Severity = temperatureC >= criticalThreshold
                    ? DiagnosticsSeverity.Critical
                    : temperatureC >= warningThreshold
                        ? DiagnosticsSeverity.Warning
                        : DiagnosticsSeverity.Healthy,
                IsAvailable = true,
                Details = details
            };
        }

        private TemperatureSensorStatusItem CreateUnavailableTemperatureSensor(string name,
            DiagnosticsTemperatureSensorType sensorType, string details)
        {
            return new TemperatureSensorStatusItem
            {
                Name = name,
                SensorType = sensorType,
                IsAvailable = false,
                Severity = DiagnosticsSeverity.Healthy,
                Details = details
            };
        }

        private DatabaseArchiveStatusItem EvaluateDatabaseArchiveStatus(SystemDiagnosticsSettings settings)
        {
            if (!settings.DatabaseArchiveMonitoringEnabled)
            {
                return CreateUnavailableDatabaseArchiveStatus(settings,
                    GetMessage("Sub_entry_DatabaseArchiveMonitoringDisabled", "Database archive monitoring disabled in configuration"));
            }

            var connectionString = BuildDatabaseConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return CreateUnavailableDatabaseArchiveStatus(settings,
                    GetMessage("Sub_entry_DatabaseArchiveConnectionUnavailable", "Database connection not configured"));
            }

            if (!IsSafeSqlIdentifier(settings.DatabaseArchiveTableName) || !IsSafeSqlIdentifier(settings.DatabaseArchiveTimestampColumn))
            {
                return CreateUnavailableDatabaseArchiveStatus(settings,
                    GetMessage("Sub_entry_DatabaseArchiveInvalidConfig", "Archive table configuration is invalid"));
            }

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    if (!DatabaseTableExists(connection, settings.DatabaseArchiveTableName))
                    {
                        return CreateUnavailableDatabaseArchiveStatus(settings,
                            string.Format(
                                GetMessage("Sub_entry_DatabaseArchiveTableMissingFormat", "Table {0} not found"),
                                settings.DatabaseArchiveTableName));
                    }

                    if (!DatabaseColumnExists(connection, settings.DatabaseArchiveTableName, settings.DatabaseArchiveTimestampColumn))
                    {
                        return CreateUnavailableDatabaseArchiveStatus(settings,
                            string.Format(
                                GetMessage("Sub_entry_DatabaseArchiveColumnMissingFormat", "Column {0} not found"),
                                settings.DatabaseArchiveTimestampColumn));
                    }

                    var measurement = ReadDatabaseArchiveMeasurement(connection, settings.DatabaseArchiveTableName);
                    var status = new DatabaseArchiveStatusItem
                    {
                        TableName = settings.DatabaseArchiveTableName,
                        TimestampColumn = settings.DatabaseArchiveTimestampColumn,
                        SizeMb = measurement.SizeMb,
                        EstimatedRowCount = measurement.RowCount,
                        CleanupStartMb = settings.DatabaseArchiveCleanupStartMb,
                        CleanupTargetMb = settings.DatabaseArchiveCleanupTargetMb,
                        RetentionDays = settings.DatabaseArchiveRetentionDays,
                        DeleteBatchSize = settings.DatabaseArchiveDeleteBatchSize,
                        CleanupExecuted = _databaseArchiveCleanupExecuted,
                        DeletedRowsLastCleanup = _lastDatabaseArchiveDeletedRows,
                        LastCleanupDetails = _lastDatabaseArchiveCleanupDetails,
                        LastCleanupAt = _lastDatabaseArchiveCleanupAt == DateTime.MinValue ? (DateTime?)null : _lastDatabaseArchiveCleanupAt,
                        IsAvailable = true
                    };

                    status.Severity = GetDatabaseArchiveSeverity(status.SizeMb, status.CleanupTargetMb, status.CleanupStartMb);

                    var cleanupResult = TryCleanupDatabaseArchive(connection, settings, status);
                    if (cleanupResult != null)
                    {
                        var refreshedMeasurement = ReadDatabaseArchiveMeasurement(connection, settings.DatabaseArchiveTableName);
                        status.SizeMb = refreshedMeasurement.SizeMb;
                        status.EstimatedRowCount = refreshedMeasurement.RowCount;
                        status.CleanupExecuted = cleanupResult.CleanupExecuted;
                        status.DeletedRowsLastCleanup = cleanupResult.DeletedRows;
                        status.LastCleanupDetails = cleanupResult.Details;
                        status.LastCleanupAt = cleanupResult.ExecutedAt;
                        status.Severity = GetDatabaseArchiveSeverity(status.SizeMb, status.CleanupTargetMb, status.CleanupStartMb);
                    }

                    return status;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"SystemDiagnostics database archive unavailable: {ex.Message}");
                return CreateUnavailableDatabaseArchiveStatus(settings,
                    GetMessage("Sub_entry_DatabaseArchiveUnavailable", "Archive status not available"));
            }
        }

        private DatabaseArchiveCleanupResult TryCleanupDatabaseArchive(MySqlConnection connection,
            SystemDiagnosticsSettings settings, DatabaseArchiveStatusItem status)
        {
            if (!settings.DatabaseArchiveAutoCleanupEnabled)
                return null;

            if (status.SizeMb < settings.DatabaseArchiveCleanupStartMb)
                return null;

            if (_lastDatabaseArchiveCleanupAt != DateTime.MinValue &&
                (DateTime.Now - _lastDatabaseArchiveCleanupAt).TotalMinutes < Math.Max(1, settings.DatabaseArchiveCleanupCooldownMinutes))
            {
                return null;
            }

            var tableName = settings.DatabaseArchiveTableName;
            var timestampColumn = settings.DatabaseArchiveTimestampColumn;
            var retentionCutoff = DateTime.Now.AddDays(-Math.Max(1, settings.DatabaseArchiveRetentionDays));
            var batchSize = Math.Max(100, settings.DatabaseArchiveDeleteBatchSize);

            _eventLogger.LogOperationalEvent(
                LogLevel.Warn,
                "DATABASE_ARCHIVE_CLEANUP_START",
                "SystemDiagnostics",
                "Automatic database archive cleanup started.",
                nameof(TryCleanupDatabaseArchive),
                tableName,
                new Dictionary<string, object>
                {
                    { "table", tableName },
                    { "current_size_mb", status.SizeMb.ToString("0.0") },
                    { "cleanup_start_mb", settings.DatabaseArchiveCleanupStartMb.ToString("0.0") },
                    { "cleanup_target_mb", settings.DatabaseArchiveCleanupTargetMb.ToString("0.0") },
                    { "retention_days", settings.DatabaseArchiveRetentionDays }
                });

            var result = new DatabaseArchiveCleanupResult
            {
                ExecutedAt = DateTime.Now,
                FinalSizeMb = status.SizeMb
            };

            var deleteSql = string.Format(
                "DELETE FROM `{0}` WHERE `{1}` IS NOT NULL AND `{1}` < @Cutoff ORDER BY `{1}` LIMIT {2};",
                tableName,
                timestampColumn,
                batchSize);

            while (result.FinalSizeMb > settings.DatabaseArchiveCleanupTargetMb)
            {
                int deletedRows;
                using (var deleteCommand = new MySqlCommand(deleteSql, connection))
                {
                    deleteCommand.Parameters.AddWithValue("@Cutoff", retentionCutoff);
                    deletedRows = deleteCommand.ExecuteNonQuery();
                }

                if (deletedRows <= 0)
                    break;

                result.DeletedRows += deletedRows;
                result.CleanupExecuted = true;
                result.FinalSizeMb = ReadDatabaseArchiveMeasurement(connection, tableName).SizeMb;
                if (result.FinalSizeMb <= settings.DatabaseArchiveCleanupTargetMb)
                {
                    result.TargetReached = true;
                    break;
                }
            }

            _lastDatabaseArchiveCleanupAt = result.ExecutedAt;
            _lastDatabaseArchiveDeletedRows = result.DeletedRows;
            _databaseArchiveCleanupExecuted = result.CleanupExecuted;

            if (!result.CleanupExecuted)
            {
                result.Details = string.Format(
                    GetMessage("Sub_entry_DatabaseArchiveCleanupSkippedFormat", "No row older than {0} day(s) available for cleanup."),
                    settings.DatabaseArchiveRetentionDays);
                _lastDatabaseArchiveCleanupDetails = result.Details;

                _eventLogger.LogOperationalEvent(
                    LogLevel.Warn,
                    "DATABASE_ARCHIVE_CLEANUP_SKIPPED",
                    "SystemDiagnostics",
                    "Automatic database archive cleanup could not delete rows.",
                    nameof(TryCleanupDatabaseArchive),
                    result.Details,
                    new Dictionary<string, object>
                    {
                        { "table", tableName },
                        { "retention_days", settings.DatabaseArchiveRetentionDays }
                    });
                return result;
            }

            result.Details = result.TargetReached
                ? string.Format(
                    GetMessage("Sub_entry_DatabaseArchiveCleanupCompletedFormat", "Archive reduced to {0:0.0} MB."),
                    result.FinalSizeMb)
                : string.Format(
                    GetMessage("Sub_entry_DatabaseArchiveCleanupPartialFormat", "Archive reduced to {0:0.0} MB but target was not reached."),
                    result.FinalSizeMb);

            _lastDatabaseArchiveCleanupDetails = result.Details;

            _eventLogger.LogOperationalEvent(
                result.TargetReached ? LogLevel.Info : LogLevel.Warn,
                result.TargetReached ? "DATABASE_ARCHIVE_CLEANUP_COMPLETED" : "DATABASE_ARCHIVE_CLEANUP_PARTIAL",
                "SystemDiagnostics",
                "Automatic database archive cleanup completed.",
                nameof(TryCleanupDatabaseArchive),
                result.Details,
                new Dictionary<string, object>
                {
                    { "table", tableName },
                    { "deleted_rows", result.DeletedRows },
                    { "final_size_mb", result.FinalSizeMb.ToString("0.0") }
                });

            return result;
        }

        private DatabaseArchiveMeasurement ReadDatabaseArchiveMeasurement(MySqlConnection connection, string tableName)
        {
            var measurement = new DatabaseArchiveMeasurement();

            const string sql = @"
SELECT 
    COALESCE((data_length + index_length) / 1024 / 1024, 0) AS SizeMb,
    COALESCE(table_rows, 0) AS RowCount
FROM information_schema.TABLES
WHERE table_schema = @SchemaName AND table_name = @TableName;";

            using (var command = new MySqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@SchemaName", MainWindow.configManager.Config.MySqlConnection.Db);
                command.Parameters.AddWithValue("@TableName", tableName);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        measurement.SizeMb = reader["SizeMb"] == DBNull.Value ? 0 : Convert.ToDouble(reader["SizeMb"]);
                        measurement.RowCount = reader["RowCount"] == DBNull.Value ? 0 : Convert.ToInt64(reader["RowCount"]);
                    }
                }
            }

            return measurement;
        }

        private bool DatabaseTableExists(MySqlConnection connection, string tableName)
        {
            const string sql = @"
SELECT COUNT(*)
FROM information_schema.TABLES
WHERE table_schema = @SchemaName AND table_name = @TableName;";

            using (var command = new MySqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@SchemaName", MainWindow.configManager.Config.MySqlConnection.Db);
                command.Parameters.AddWithValue("@TableName", tableName);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private bool DatabaseColumnExists(MySqlConnection connection, string tableName, string columnName)
        {
            const string sql = @"
SELECT COUNT(*)
FROM information_schema.COLUMNS
WHERE table_schema = @SchemaName AND table_name = @TableName AND column_name = @ColumnName;";

            using (var command = new MySqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@SchemaName", MainWindow.configManager.Config.MySqlConnection.Db);
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private DatabaseArchiveStatusItem CreateUnavailableDatabaseArchiveStatus(SystemDiagnosticsSettings settings, string details = null)
        {
            return new DatabaseArchiveStatusItem
            {
                TableName = settings?.DatabaseArchiveTableName ?? "tblgenerale",
                TimestampColumn = settings?.DatabaseArchiveTimestampColumn ?? "DataeOra",
                CleanupStartMb = settings?.DatabaseArchiveCleanupStartMb ?? 2048,
                CleanupTargetMb = settings?.DatabaseArchiveCleanupTargetMb ?? 1536,
                RetentionDays = settings?.DatabaseArchiveRetentionDays ?? 90,
                DeleteBatchSize = settings?.DatabaseArchiveDeleteBatchSize ?? 5000,
                IsAvailable = false,
                Severity = DiagnosticsSeverity.Healthy,
                CleanupExecuted = _databaseArchiveCleanupExecuted,
                DeletedRowsLastCleanup = _lastDatabaseArchiveDeletedRows,
                LastCleanupAt = _lastDatabaseArchiveCleanupAt == DateTime.MinValue ? (DateTime?)null : _lastDatabaseArchiveCleanupAt,
                LastCleanupDetails = details ?? _lastDatabaseArchiveCleanupDetails
            };
        }

        private DiagnosticsSeverity GetDatabaseArchiveSeverity(double sizeMb, double targetMb, double startMb)
        {
            if (sizeMb >= startMb)
                return DiagnosticsSeverity.Critical;

            if (sizeMb >= targetMb)
                return DiagnosticsSeverity.Warning;

            return DiagnosticsSeverity.Healthy;
        }

        private string BuildDatabaseConnectionString()
        {
            var config = MainWindow.configManager?.Config?.MySqlConnection;
            if (config == null ||
                string.IsNullOrWhiteSpace(config.Host) ||
                string.IsNullOrWhiteSpace(config.Db) ||
                string.IsNullOrWhiteSpace(config.User) ||
                string.IsNullOrWhiteSpace(config.port))
            {
                return null;
            }

            return string.Format(
                "Server={0};Database={1};Uid={2};Pwd={3};Port={4};Connection Timeout=5;Default Command Timeout=20;",
                config.Host,
                config.Db,
                config.User,
                config.Password,
                config.port);
        }

        private static bool IsSafeSqlIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            return identifier.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
        }

        private static double? ConvertToNullableDouble(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return null;
            }
        }

        private static double? ConvertAcpiTemperatureToCelsius(double? currentTemperature)
        {
            if (!currentTemperature.HasValue || currentTemperature.Value <= 0)
                return null;

            return (currentTemperature.Value - 2732d) / 10d;
        }

        private async Task ApplySnapshotAsync(DiagnosticsSnapshot snapshot)
        {
            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
            {
                ApplySnapshot(snapshot);
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(new Action(() => ApplySnapshot(snapshot)));
        }

        private void ApplySnapshot(DiagnosticsSnapshot snapshot)
        {
            CpuUsagePercent = snapshot.CpuUsagePercent;
            TotalMemoryGb = snapshot.TotalMemoryGb;
            UsedMemoryGb = snapshot.UsedMemoryGb;
            MemoryUsagePercent = snapshot.MemoryUsagePercent;
            OverallSeverity = snapshot.OverallSeverity;
            WorstDiskText = snapshot.WorstDiskText;
            CleanupRootsText = snapshot.CleanupRootsText;
            CleanupPolicyText = snapshot.CleanupPolicyText;
            LastCleanupText = snapshot.LastCleanupText;
            HottestDiskTemperatureSensor = snapshot.HottestDiskTemperatureSensor;
            HottestCpuTemperatureSensor = snapshot.HottestCpuTemperatureSensor;
            MemoryTemperatureSensor = snapshot.MemoryTemperatureSensor;
            DatabaseArchiveStatus = snapshot.DatabaseArchiveStatus;
            LastUpdatedAt = snapshot.LastUpdatedAt;

            _drives.Clear();
            foreach (var drive in snapshot.Drives)
            {
                _drives.Add(drive);
            }

            _temperatureSensors.Clear();
            foreach (var sensor in snapshot.TemperatureSensors)
            {
                _temperatureSensors.Add(sensor);
            }

            _activeAlerts.Clear();
            foreach (var alert in snapshot.Alerts)
            {
                _activeAlerts.Add(alert);
            }

            OnPropertyChanged(nameof(Drives));
            OnPropertyChanged(nameof(TemperatureSensors));
            OnPropertyChanged(nameof(ActiveAlerts));
            OnPropertyChanged(nameof(ActiveAlertCount));
            OnPropertyChanged(nameof(HasActiveAlerts));
            OnPropertyChanged(nameof(CriticalAlertCount));
            OnPropertyChanged(nameof(WarningAlertCount));
            OnPropertyChanged(nameof(ActiveAlertSummary));
            OnPropertyChanged(nameof(AiAdvisoryCount));
            OnPropertyChanged(nameof(HasAiAdvisories));
        }

        private IEnumerable<string> SafeEnumeratePieceFolders(string rootPath)
        {
            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(current);
                }
                catch
                {
                    continue;
                }

                foreach (var directory in directories)
                {
                    var name = Path.GetFileName(directory);
                    if (!string.IsNullOrWhiteSpace(name) &&
                        name.StartsWith("Piece_", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return directory;
                    }

                    pending.Push(directory);
                }
            }
        }

        private IEnumerable<string> SafeEnumerateFiles(string rootPath)
        {
            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(current);
                }
                catch
                {
                    directories = new string[0];
                }

                foreach (var directory in directories)
                {
                    pending.Push(directory);
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch
                {
                    files = new string[0];
                }

                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file);
                    if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return file;
                    }
                }
            }
        }

        private void CleanupEmptyAncestors(string deletedFolder, IEnumerable<string> cleanupRoots)
        {
            try
            {
                var normalizedRoots = cleanupRoots
                    .Select(NormalizeDirectoryPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToList();

                var parent = Directory.GetParent(deletedFolder);
                while (parent != null)
                {
                    var normalizedParent = NormalizeDirectoryPath(parent.FullName);
                    if (normalizedRoots.Any(root => string.Equals(root, normalizedParent, StringComparison.OrdinalIgnoreCase)))
                    {
                        break;
                    }

                    if (Directory.Exists(parent.FullName) &&
                        !Directory.EnumerateFileSystemEntries(parent.FullName).Any())
                    {
                        parent.Delete();
                        parent = parent.Parent;
                        continue;
                    }

                    break;
                }
            }
            catch
            {
            }
        }

        private double GetDriveUsedPercent(string driveRoot)
        {
            try
            {
                var drive = new DriveInfo(driveRoot);
                if (!drive.IsReady)
                    return 0;

                var totalGb = BytesToGb(drive.TotalSize);
                if (totalGb <= 0)
                    return 0;

                var freeGb = BytesToGb(drive.TotalFreeSpace);
                var usedGb = Math.Max(0, totalGb - freeGb);
                return (usedGb / totalGb) * 100;
            }
            catch
            {
                return 0;
            }
        }

        private DateTime GetLastWriteUtcSafe(string path)
        {
            try
            {
                return Directory.Exists(path)
                    ? Directory.GetLastWriteTimeUtc(path)
                    : File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MaxValue;
            }
        }

        private static double BytesToGb(long value)
        {
            return value / 1024d / 1024d / 1024d;
        }

        private static string GetSeverityHex(DiagnosticsSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticsSeverity.Critical:
                    return "#D64545";
                case DiagnosticsSeverity.Warning:
                    return "#D9942B";
                default:
                    return "#2E9E75";
            }
        }

        private static Brush CreateBrush(string colorHex)
        {
            return (Brush)new BrushConverter().ConvertFromString(colorHex);
        }

        private static string GetSeverityText(DiagnosticsSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticsSeverity.Critical:
                    return GetMessage("Sub_entry_DiagnosticsCritical", "Critical");
                case DiagnosticsSeverity.Warning:
                    return GetMessage("Sub_entry_Warning", "Warning");
                default:
                    return GetMessage("Sub_entry_DiagnosticsHealthy", "Healthy");
            }
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
