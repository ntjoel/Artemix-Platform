using System;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using NLog;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.Advantech;
using QtisVisionPanel.Models.MultiShotTrigger;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Services.MultiShotTrigger;
using QtisVisionPanel.Views;

namespace QtisVisionPanel.ViewModels
{
    public class MultiShotProfileOption
    {
        public string Key { get; set; }
        public string Label { get; set; }
    }

    /// <summary>
    /// ViewModel for the Digital I/O commissioning and machine configuration page.
    ///
    /// Responsibilities span three domains that share this single page:
    /// - physical I/O mapping (Advantech BDaq4 channels, signal polarity, names)
    /// - encoder configuration (counts/mm, MultiShot profiles for Side/Right/Bottom)
    /// - alarm output assignment and ejection alarm card management
    ///
    /// Configuration is loaded from and saved to <c>machine_runtime_config.xml</c>.
    /// Changes only take effect at runtime when the user presses Save and the
    /// machine is in manual stop (enforced by <see cref="MachineRuntimeService"/>).
    ///
    /// Audit events (<c>MACHINE_CONFIG_SAVE</c>) are written to <c>audit_log</c>
    /// after every successful save for traceability in production environments.
    /// </summary>
    public class DigitalIOViewModel : INotifyPropertyChanged, IDisposable
    {
        private sealed class PendingInterventionExecution
        {
            public ProductInterventionEventArgs EventArgs { get; set; }
            public DateTime EnqueuedAtUtc { get; set; }
        }

        private static readonly Logger Logger = MainWindow.logger;
        private IIODeviceManager _ioManager;
        private readonly MachineConfigurationService _machineConfigurationService;
        private readonly ToolFusionSnapshotService _toolFusionSnapshotService;
        private readonly PhotocellTimedTriggerService _timedTriggerService;
        private MachineController _machineController;
        private IMultiShotTriggerController _multiShotTriggerController;
        private readonly DispatcherTimer _refreshTimer;
        private readonly SemaphoreSlim _modeSwitchLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _recipeRuntimeConfigurationLock = new SemaphoreSlim(1, 1);
        private readonly DispatcherTimer _logTimer;
        private readonly DispatcherTimer _heartbeatTimer;
        private readonly DispatcherTimer _virtualConveyorTimer;
        private readonly DispatcherTimer _runtimePreviewApplyTimer;
        private readonly long[] _lastEncoderCounts = new long[4];
        private readonly DateTime[] _lastEncoderSampleTimes = new DateTime[4];
        private readonly double[] _filteredEncoderSpeedMetersPerMinute = new double[4];
        private readonly bool[] _encoderMetricReferenceReady = new bool[4];
        private readonly DateTime[] _encoderMetricWarmupUntilUtc = new DateTime[4];
        private readonly Dictionary<long, HashSet<string>> _executedTriggerOutputsByProduct = new Dictionary<long, HashSet<string>>();
        private readonly object _triggerOutputsLock = new object();
        private readonly object _outputChannelSemaphoresLock = new object();
        private readonly Dictionary<int, SemaphoreSlim> _outputChannelSemaphores = new Dictionary<int, SemaphoreSlim>();
        private readonly object _timedTriggerFastPathLock = new object();
        private readonly SemaphoreSlim _directOutputWriteLock = new SemaphoreSlim(1, 1);
        private readonly BlockingCollection<PendingInterventionExecution> _interventionExecutionQueue =
            new BlockingCollection<PendingInterventionExecution>(new ConcurrentQueue<PendingInterventionExecution>(), 256);
        private readonly Task _interventionExecutionTask;
        private TimedTriggerFastPathSnapshot _timedTriggerFastPath = TimedTriggerFastPathSnapshot.Disabled;
        private const long EncoderNoiseDeadbandCounts = 0;
        private const double EncoderStartupWarmupSeconds = 3.0;
        private const double EncoderStartupSpikeSpeedLimitMetersPerMinute = 120.0;
        private const double EncoderDisplayedSpeedLimitMetersPerMinute = 250.0;
        private const int VirtualConveyorEncoderChannel = 0;
        private const int RuntimePreviewApplyDebounceMs = 450;
        private const int PhotocellDuplicateDebounceMs = 80;
        private const double PhotocellDuplicateMinimumDistanceMm = 8.0;
        private const double CameraTriggerLateWarningMm = 5.0;
        private bool _isInitialized = false;
        private IntegratedAlarmCardService _alarmService;
        private EjectionAlarmConfig _selectedEjectionAlarm;
        private string _ejectionAlarmsSummary = "-";
        private bool _hasRealHardwareConnection;
        private MachineRuntimeConfiguration _machineConfiguration;
        private MachineRuntimeConfiguration _effectiveRuntimeConfiguration;
        private MachineMultiShotTriggerConfiguration _lastPersistedMultiShotConfig;
        private bool _isRefreshingMachineSignalStatuses;
        private bool _suppressConfigurationDirtyTracking;
        private bool _isConfigurationDirty;
        private DateTime? _lastConfigurationSaveLocalTime;
        private DateTime? _lastFusionExportLocalTime;
        private bool _heartbeatOutputState;
        private bool _isHeartbeatTickRunning;
        private bool _isVirtualConveyorTickRunning;
        private bool _isApplyingRuntimePreview;
        private string _pendingRuntimePreviewReason;
        private readonly Dictionary<string, MultiShotProfileSnapshot> _multiShotPreviousSnapshots =
            new Dictionary<string, MultiShotProfileSnapshot>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// In-memory machine configuration after applying the active recipe corrections.
        /// It is never written back to machine_runtime_config.xml.
        /// </summary>
        public MachineRuntimeConfiguration EffectiveRuntimeConfiguration => _effectiveRuntimeConfiguration;
        private string[] _activeVppCameraRoles = Array.Empty<string>();

        private bool IsActiveVppCameraRole(string role)
        {
            string normalized = ActiveVppCameraTriggerGate.NormalizeRole(role);
            return normalized != null && Volatile.Read(ref _activeVppCameraRoles)
                .Contains(normalized, StringComparer.OrdinalIgnoreCase);
        }
        private long _virtualConveyorCount;
        private double _virtualConveyorFractionalCounts;
        private DateTime _lastVirtualConveyorTickUtc = DateTime.MinValue;
        private DateTime _lastAcceptedPhotocellUtc = DateTime.MinValue;
        private long _lastAcceptedPhotocellEncoderCount = long.MinValue;
        private bool _mainEncoderReferenceReady;

        // La UI del contatore encoder non deve seguire la cadenza del poll. A 500 Hz ogni
        // tick allocava una closure, una DispatcherOperation e una stringa solo per muovere
        // un numero che l'occhio legge a 10 Hz: spazzatura in regime che alimenta le
        // collection gen0/gen1, le quali sospendono il processo e spostano la quota dei
        // trigger. Il percorso di controllo macchina resta a piena cadenza.
        private static readonly long EncoderUiUpdateIntervalTicks = Stopwatch.Frequency / 10;
        private readonly long[] _lastEncoderUiUpdateTicks = new long[4];
        private volatile bool _cachedExternalTriggerEnableActive;
        private DateTime? _lastTimedPhotocellEdgeLocalTime;
        private DateTime? _lastTimedTopTriggerLocalTime;
        private DateTime? _lastTimedSideTriggerLocalTime;
        private string _timedTriggerRuntimeStatus = "Timed trigger idle";
        private double? _lastTimedTopActualDelayMs;
        private double? _lastTimedSideActualDelayMs;
        private double _timedTopDelaySumMs;
        private double _timedSideDelaySumMs;
        private int _timedBatchesQueuedCount;
        private int _timedBatchesCompletedCount;
        private int _timedBatchesCancelledCount;
        private int _timedTriggerErrorCount;
        private int _timedTopShotsCount;
        private int _timedSideShotsCount;
        private DateTime _lastFastTimedPhotocellUtc = DateTime.MinValue;

        // Inter-arrival tracking: logs only, not used for delay scaling.
        private readonly Queue<double> _photocellInterArrivalBuffer = new Queue<double>();
        private readonly object _interArrivalBufferLock = new object();
        private double _lastSpeedCompensationFactor = 1.0;
        private double _lastInterArrivalMs;

        private sealed class TimedTriggerFastPathSnapshot
        {
            public static readonly TimedTriggerFastPathSnapshot Disabled = new TimedTriggerFastPathSnapshot
            {
                Enabled = false,
                ProductPhotocellChannel = -1,
                ProductPhotocellActiveElectricalState = true,
                ProductPhotocellSignalCode = string.Empty,
                RouteSummary = "disabled",
                Outputs = Array.Empty<PhotocellTimedTriggerOutputRequest>()
            };

            public bool Enabled { get; set; }

            public int ProductPhotocellChannel { get; set; }

            public bool ProductPhotocellActiveElectricalState { get; set; }

            public string ProductPhotocellSignalCode { get; set; }

            public double? SourceMachineQuotaMm { get; set; }

            public int DebounceMs { get; set; }

            public IReadOnlyList<PhotocellTimedTriggerOutputRequest> Outputs { get; set; }

            public string RouteSummary { get; set; }
        }

        private static string T(string key, string fallback = null)
        {
            return ToolLocalizationService.Current.Get(key, fallback);
        }

        private static MachineInterventionPoint CreateStandardInterventionPoint(
            string pointCode,
            double baseOffsetMm,
            string actionType,
            string signalCode,
            int pulseMs,
            bool isStandard)
        {
            switch (pointCode)
            {
                case "CAMERA_TRIGGER":
                    return new MachineInterventionPoint
                    {
                        PointCode = pointCode,
                        Description = T("Tool_DefaultPoint_CameraTrigger_Description", "Camera trigger shot"),
                        Enabled = true,
                        ReferenceCode = "PRODUCT_ZERO",
                        BaseOffsetMm = baseOffsetMm,
                        TrimOffsetMm = 0.0,
                        ActionType = actionType,
                        SignalCode = signalCode,
                        PulseMs = pulseMs,
                        IsStandard = isStandard,
                        Notes = T("Tool_DefaultPoint_CameraTrigger_Notes", "Trigger pulse when the acquisition position is reached.")
                    };
                case "REJECT":
                    return new MachineInterventionPoint
                    {
                        PointCode = pointCode,
                        Description = T("Tool_DefaultPoint_Reject_Description", "Reject intervention point"),
                        Enabled = true,
                        ReferenceCode = "PRODUCT_ZERO",
                        BaseOffsetMm = baseOffsetMm,
                        TrimOffsetMm = 0.0,
                        ActionType = actionType,
                        SignalCode = signalCode,
                        PulseMs = pulseMs,
                        IsStandard = isStandard,
                        Notes = T("Tool_DefaultPoint_Reject_Notes", "Activates reject only for NOK products when they reach the real machine position.")
                    };
                default:
                    return new MachineInterventionPoint
                    {
                        PointCode = pointCode,
                        Description = pointCode,
                        Enabled = true,
                        ReferenceCode = "PRODUCT_ZERO",
                        BaseOffsetMm = baseOffsetMm,
                        TrimOffsetMm = 0.0,
                        ActionType = actionType,
                        SignalCode = signalCode,
                        PulseMs = pulseMs,
                        IsStandard = isStandard,
                        Notes = string.Empty
                    };
            }
        }

        // Collezioni
        public ObservableCollection<IOChannel> InputChannels { get; } = new ObservableCollection<IOChannel>();
        public ObservableCollection<IOChannel> OutputChannels { get; } = new ObservableCollection<IOChannel>();
        public ObservableCollection<EncoderChannel> EncoderChannels { get; } = new ObservableCollection<EncoderChannel>();
        public ObservableCollection<IOLogEntry> LogEntries { get; } = new ObservableCollection<IOLogEntry>();
        public ObservableCollection<MachineSignalDefinition> MachineInputs { get; } = new ObservableCollection<MachineSignalDefinition>();
        public ObservableCollection<MachineSignalDefinition> MachineOutputs { get; } = new ObservableCollection<MachineSignalDefinition>();
        public ObservableCollection<EncoderConfigurationTemplate> EncoderTemplates { get; } = new ObservableCollection<EncoderConfigurationTemplate>();
        public ObservableCollection<MachineSequenceStep> MachineSequence { get; } = new ObservableCollection<MachineSequenceStep>();
        public ObservableCollection<TrackedProduct> TrackedProducts { get; } = new ObservableCollection<TrackedProduct>();
        public ObservableCollection<AdditionalRuntimeBindingDefinition> AdditionalRuntimeBindings { get; } = new ObservableCollection<AdditionalRuntimeBindingDefinition>();
        public ObservableCollection<MachineInterventionPoint> InterventionPoints { get; } = new ObservableCollection<MachineInterventionPoint>();
        public ObservableCollection<InterventionPointDisplayItem> InterventionTimelineItems { get; } = new ObservableCollection<InterventionPointDisplayItem>();
        public ObservableCollection<InterventionSignalAssignment> InterventionSignalAssignments { get; } = new ObservableCollection<InterventionSignalAssignment>();
        public ObservableCollection<InterventionRuntimeEvent> InterventionRuntimeEvents { get; } = new ObservableCollection<InterventionRuntimeEvent>();
        public ObservableCollection<MachineOperationalEvent> MachineOperationalEvents { get; } = new ObservableCollection<MachineOperationalEvent>();
        public ObservableCollection<MachineConfigurationBackupInfo> ConfigurationBackups { get; } = new ObservableCollection<MachineConfigurationBackupInfo>();
        public ObservableCollection<string> AvailableSignalCodes { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> TriggerModeOptions { get; } = new ObservableCollection<string> { "Hybrid", "Internal", "External" };
        public ObservableCollection<string> RejectModeOptions { get; } = new ObservableCollection<string> { "EncoderTracked", "Immediate", "Disabled" };
        public ObservableCollection<string> TriggerSchedulingModeOptions { get; } = new ObservableCollection<string> { "VirtualConveyor", "TimedFromPhotocell" };
        public ObservableCollection<string> InterventionActionOptions { get; } = new ObservableCollection<string> { "TriggerCamera", "Reject", "AlarmOn", "AlarmOff", "OutputPulse", "OutputOn", "OutputOff", "Marker" };
        public ObservableCollection<string> InterventionReferenceOptions { get; } = new ObservableCollection<string> { "PRODUCT_ZERO" };
        public ObservableCollection<EjectionAlarmConfig> EjectionAlarms { get; } = new ObservableCollection<EjectionAlarmConfig>();
        public IReadOnlyList<AlarmType> AlarmTypeValues { get; } = (AlarmType[])Enum.GetValues(typeof(AlarmType));
        public IReadOnlyList<SignalType> SignalTypeValues { get; } = (SignalType[])Enum.GetValues(typeof(SignalType));

        public ICollectionView MachineInputsView { get; }
        public ICollectionView MachineOutputsView { get; }
        public ToolLocalizationService L10n => ToolLocalizationService.Current;

        // Comandi
        public ICommand SetOutputCommand { get; }
        public ICommand SetAllOutputsHighCommand { get; }
        public ICommand SetAllOutputsLowCommand { get; }
        public ICommand ReadInputsCommand { get; }
        public ICommand ReadEncodersCommand { get; }
        public ICommand ResetEncodersCommand { get; }
        public ICommand StartEncodersCommand { get; }
        public ICommand StopEncodersCommand { get; }
        public ICommand SetEncoderPresetCommand { get; }
        public ICommand SaveConfigurationCommand { get; }
        public ICommand LoadConfigurationCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ApplyConnectionModeCommand { get; }
        public ICommand ToggleConnectionModeCommand { get; }
        public ICommand ApplySimulationEncoderSettingsCommand { get; }
        public ICommand StepSimulationEncoderCommand { get; }
        public ICommand CaptureCurrentEncoderCalibrationSpeedCommand { get; }
        public ICommand ApplyEncoderTachometerCalibrationCommand { get; }
        public ICommand SaveLogToFileCommand { get; }
        public ICommand SimulateProductPassageCommand { get; }
        public ICommand MarkLatestProductAcceptedCommand { get; }
        public ICommand MarkLatestProductRejectedCommand { get; }
        public ICommand ForceTriggerCycleCommand { get; }
        public ICommand ClearAlarmOutputCommand { get; }
        public ICommand OpenConfigurationFolderCommand { get; }
        public ICommand OpenConfigurationFileCommand { get; }
        public ICommand CreateConfigurationBackupCommand { get; }
        public ICommand RestoreConfigurationBackupCommand { get; }
        public ICommand RefreshConfigurationBackupsCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand ExportFusionSnapshotCommand { get; }
        public ICommand OpenFusionExportFolderCommand { get; }
        public ICommand ToggleHeartbeatCommand { get; }
        public ICommand AddInterventionPointCommand { get; }
        public ICommand DuplicateInterventionPointCommand { get; }
        public ICommand RemoveInterventionPointCommand { get; }
        public ICommand ClearMachineEventsCommand { get; }
        public ICommand SaveEjectionAlarmsCommand { get; }
        public ICommand ReloadEjectionAlarmsCommand { get; }
        public ICommand ResetSelectedEjectionAlarmCommand { get; }
        public ICommand ForceTimedTopTriggerCommand { get; }
        public ICommand ForceTimedSideTriggerCommand { get; }
        public ICommand SimulateTimedPhotocellEdgeCommand { get; }
        public ICommand ResetTimedTriggerDiagnosticsCommand { get; }

        // Properties for binding
        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { _isRefreshing = value; OnPropertyChanged(); }
        }

        private bool _preferSimulationMode = false;

        public bool PreferSimulationMode
        {
            get => _preferSimulationMode;
            set { _preferSimulationMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionModeSummary)); OnPropertyChanged(nameof(PreferredConnectionModeLabel)); OnPropertyChanged(nameof(ApplyConnectionModeButtonLabel)); OnPropertyChanged(nameof(ConnectionCheckStatus)); OnPropertyChanged(nameof(ConnectionCheckDetails)); }
        }

        private bool _isSimulationMode;
        public bool IsSimulationMode
        {
            get => _isSimulationMode;
            set
            {
                _isSimulationMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveConnectionModeLabel));
                OnPropertyChanged(nameof(ActiveConnectionModeDescription));
                OnPropertyChanged(nameof(ConnectionCheckStatus));
                OnPropertyChanged(nameof(ConnectionCheckDetails));
                OnPropertyChanged(nameof(ToggleConnectionModeButtonLabel));
                OnPropertyChanged(nameof(HeartbeatStatusSummary));
                OnPropertyChanged(nameof(HeartbeatDashboardSummary));
                OnPropertyChanged(nameof(EncoderDiagnosticGuidance));
                OnPropertyChanged(nameof(VirtualConveyorSummary));
                UpdateVirtualConveyorTimerState();
            }
        }

        private bool _isModeSwitching;
        public bool IsModeSwitching
        {
            get => _isModeSwitching;
            set { _isModeSwitching = value; OnPropertyChanged(); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string HardwareSectionTitle => T("Sub_entry_MachineDashboard", "Dashboard");

        public string DocumentationFolderPath => @"Documentation\MachineHardware";
        public string MachineConfigurationPath => _machineConfigurationService.GetConfigurationFilePath();

        public string StandaloneDataRootPath => _machineConfigurationService.GetConfigurationRootPath();
        public string MachineConfigurationTemplatePath => _machineConfigurationService.GetTemplateConfigurationFilePath();
        public string ConfigurationFolderPath => _machineConfigurationService.GetConfigurationRootPath();
        public string ConfigurationBackupFolderPath => _machineConfigurationService.GetBackupFolderPath();
        public string FusionExportFolderPath => _toolFusionSnapshotService.GetExportFolderPath();

        private MachineConfigurationBackupInfo _selectedConfigurationBackup;
        public MachineConfigurationBackupInfo SelectedConfigurationBackup
        {
            get => _selectedConfigurationBackup;
            set
            {
                _selectedConfigurationBackup = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedBackupSummary));
            }
        }

        public bool IsConfigurationDirty
        {
            get => _isConfigurationDirty;
            set
            {
                if (_isConfigurationDirty == value)
                {
                    return;
                }

                _isConfigurationDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConfigurationSaveStateLabel));
                OnPropertyChanged(nameof(ConfigurationSaveStateDescription));
            }
        }

        public string ConfigurationSaveStateLabel => IsConfigurationDirty
            ? T("Tool_Status_UnsavedChanges", "UNSAVED CHANGES")
            : T("Tool_Status_ConfigSaved", "CONFIGURATION SAVED");

        public string ConfigurationSaveStateDescription => IsConfigurationDirty
            ? T("Tool_Status_UnsavedChangesDescription", "The page contains local changes that have not yet been written to the active machine file.")
            : T("Tool_Status_ConfigSavedDescription", "The on-screen configuration is aligned with the active machine file used by runtime.");

        public string LastConfigurationSaveSummary => _lastConfigurationSaveLocalTime.HasValue
            ? string.Format(T("Tool_Status_LastSave", "Last save: {0}"), _lastConfigurationSaveLocalTime.Value.ToString("dd/MM/yyyy HH:mm:ss"))
            : T("Tool_Status_LastSaveNone", "Last save: not performed yet in this session");

        public string LastFusionExportSummary => _lastFusionExportLocalTime.HasValue
            ? string.Format(T("Tool_VM_LastFusionExportAt", "Last fusion snapshot exported at {0:HH:mm:ss}"), _lastFusionExportLocalTime.Value)
            : T("Tool_VM_NoFusionExport", "No fusion snapshot exported in this session");

        public string ConfigurationActiveFileSummary => string.Format(
            T("Tool_Summary_ActiveOperatingFile", "Active operating file: {0}"),
            MachineConfigurationPath);

        public string ConfigurationTemplateFileSummary => string.Format(
            T("Tool_Summary_BaseTemplate", "Base template: {0}"),
            MachineConfigurationTemplatePath);

        public string ConfigurationBackupSummary => ConfigurationBackups.Count == 0
            ? T("Tool_Summary_NoBackups", "No backups available")
            : string.Format(T("Tool_Summary_BackupsAvailable", "{0} backups available in {1}"), ConfigurationBackups.Count, ConfigurationBackupFolderPath);

        public string SelectedBackupSummary => SelectedConfigurationBackup == null
            ? T("Tool_Summary_SelectBackup", "Select a backup from the history list to inspect or restore it.")
            : string.Format(T("Tool_Summary_BackupSelected", "{0} | created on {1} | {2}"), SelectedConfigurationBackup.DisplayTitle, SelectedConfigurationBackup.DisplayTimestamp, SelectedConfigurationBackup.DisplaySize);

        public string ConnectionCheckStatus => PreferSimulationMode
            ? T("Tool_Status_SimulationRequested", "SIMULATION REQUESTED")
            : (_hasRealHardwareConnection ? T("Tool_Status_BoardsConnected", "BOARDS CONNECTED") : T("Tool_Status_BoardConnectionFailed", "BOARD CONNECTION FAILED"));

        public string ConnectionCheckDetails => PreferSimulationMode
            ? T("Tool_Status_SimulationDetails", "The app is running on the standalone I/O panel internal simulator.")
            : (_hasRealHardwareConnection
                ? T("Tool_Status_BoardsConnectedDetails", "The board connection is active. Reads and writes are routed to real hardware.")
                : T("Tool_Status_BoardConnectionFailedDetails", "Real hardware was requested, but the Advantech boards were not attached or are currently busy in another tool. Check DAQNavi Navigator, drivers, power, and board presence."));

        public string ToggleConnectionModeButtonLabel => IsSimulationMode
            ? T("Tool_Command_SwitchToRealHardware", "SWITCH TO REAL HARDWARE")
            : T("Tool_Command_SwitchToSimulation", "SWITCH TO SIMULATION");

        private bool _simulationUsePiecesPerMinute;
        public bool SimulationUsePiecesPerMinute
        {
            get => _simulationUsePiecesPerMinute;
            set
            {
                _simulationUsePiecesPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DerivedSimulationEncoderSpeedMmPerSecond));
                OnPropertyChanged(nameof(DerivedSimulationPiecesPerMinute));
                OnPropertyChanged(nameof(SimulationTransportSummary));
                OnPropertyChanged(nameof(VirtualConveyorSummary));
                OnPropertyChanged(nameof(MainEncoderPiecesSummary));
                MarkConfigurationDirty();
            }
        }

        private double _simulationTransportSpeedMetersPerMinute = 18.0;
        public double SimulationTransportSpeedMetersPerMinute
        {
            get => _simulationTransportSpeedMetersPerMinute;
            set
            {
                _simulationTransportSpeedMetersPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DerivedSimulationEncoderSpeedMmPerSecond));
                OnPropertyChanged(nameof(DerivedSimulationPiecesPerMinute));
                OnPropertyChanged(nameof(SimulationTransportSummary));
                OnPropertyChanged(nameof(VirtualConveyorSummary));
                OnPropertyChanged(nameof(MainEncoderPiecesSummary));
                MarkConfigurationDirty();
            }
        }

        private double _simulationPiecesPerMinute = 120.0;
        public double SimulationPiecesPerMinute
        {
            get => _simulationPiecesPerMinute;
            set
            {
                _simulationPiecesPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DerivedSimulationEncoderSpeedMmPerSecond));
                OnPropertyChanged(nameof(SimulationTransportSummary));
                OnPropertyChanged(nameof(VirtualConveyorSummary));
                OnPropertyChanged(nameof(MainEncoderPiecesSummary));
                MarkConfigurationDirty();
            }
        }

        private double _simulationProductPitchMm = 150.0;
        public double SimulationProductPitchMm
        {
            get => _simulationProductPitchMm;
            set
            {
                _simulationProductPitchMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DerivedSimulationEncoderSpeedMmPerSecond));
                OnPropertyChanged(nameof(DerivedSimulationPiecesPerMinute));
                OnPropertyChanged(nameof(SimulationTransportSummary));
                OnPropertyChanged(nameof(VirtualConveyorSummary));
                OnPropertyChanged(nameof(MainEncoderPiecesSummary));
                MarkConfigurationDirty();
            }
        }

        public double DerivedSimulationEncoderSpeedMmPerSecond => SimulationUsePiecesPerMinute
            ? Math.Max(0.0, SimulationPiecesPerMinute) * Math.Max(0.0, SimulationProductPitchMm) / 60.0
            : Math.Max(0.0, SimulationTransportSpeedMetersPerMinute) * 1000.0 / 60.0;

        public double DerivedSimulationPiecesPerMinute => SimulationProductPitchMm <= 0
            ? 0.0
            : (Math.Max(0.0, SimulationTransportSpeedMetersPerMinute) * 1000.0) / SimulationProductPitchMm;

        public string SimulationTransportSummary => SimulationUsePiecesPerMinute
            ? string.Format(
                T("Tool_VM_SimulationTransportSummaryFromThroughput", "Production throughput set to {0:0.0} pcs/min with a product pitch of {1:0.0} mm. Equivalent line speed {2:0.00} m/min."),
                SimulationPiecesPerMinute,
                SimulationProductPitchMm,
                SimulationPiecesPerMinute * SimulationProductPitchMm / 1000.0)
            : string.Format(
                T("Tool_VM_SimulationTransportSummaryFromSpeed", "Line speed set to {0:0.00} m/min. Estimated throughput {1:0.0} pcs/min with a product pitch of {2:0.0} mm."),
                SimulationTransportSpeedMetersPerMinute,
                DerivedSimulationPiecesPerMinute,
                SimulationProductPitchMm);

        private double _simulationEncoderStepMm = 100.0;
        public double SimulationEncoderStepMm
        {
            get => _simulationEncoderStepMm;
            set { _simulationEncoderStepMm = value; OnPropertyChanged(); }
        }

        private bool _simulationEncoderRunning = true;
        public bool SimulationEncoderRunning
        {
            get => _simulationEncoderRunning;
            set
            {
                _simulationEncoderRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VirtualConveyorSummary));
                UpdateVirtualConveyorTimerState();
                MarkConfigurationDirty();
            }
        }

        private bool _virtualConveyorEnabled;
        public bool VirtualConveyorEnabled
        {
            get => _virtualConveyorEnabled;
            set
            {
                var normalizedValue = IsTimedTriggerSchedulingActive ? false : value;
                if (_virtualConveyorEnabled == normalizedValue)
                {
                    return;
                }

                _virtualConveyorEnabled = normalizedValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VirtualConveyorSummary));
                OnPropertyChanged(nameof(EncoderDiagnosticGuidance));
                SyncUseEncoderTriggerFromLegacyMode();
                UpdateVirtualConveyorTimerState();
                MarkConfigurationDirty();
            }
        }

        public string VirtualConveyorSummary
        {
            get
            {
                if (!VirtualConveyorEnabled)
                {
                    return T("Tool_VM_VirtualConveyorDisabled", "Virtual conveyor disabled: real encoder/counter data drives intervention points.");
                }

                var speedMetersPerMinute = SimulationUsePiecesPerMinute
                    ? (SimulationPiecesPerMinute * SimulationProductPitchMm) / 1000.0
                    : SimulationTransportSpeedMetersPerMinute;

                return string.Format(
                    T("Tool_VM_VirtualConveyorEnabled", "Virtual conveyor active: real I/O + manual speed {0:0.00} m/min. Photocell creates the product zero and intervention points advance without a physical encoder."),
                    speedMetersPerMinute);
            }
        }

        public string TriggerModeDescription =>
            T("Tool_VM_TriggerModeDescription", "Standard template: product photocell, lighting, internal or external camera trigger, encoder tracking, reject, and alarm outputs.");

        public string TriggerSchedulingModeSummary
        {
            get
            {
                if (string.Equals(TriggerSchedulingMode, "TimedFromPhotocell", StringComparison.OrdinalIgnoreCase))
                {
                    return T("Tool_VM_TriggerSchedulingModeTimed", "TimedFromPhotocell active: photocell edge starts a deterministic scheduler that fires TOP and SIDE outputs after their configured delays. Virtual conveyor is disabled automatically. Configure delays and pulse widths below.");
                }

                return T("Tool_VM_TriggerSchedulingModeVirtual", "VirtualConveyor selected: the current runtime keeps using the existing tracked-position flow based on photocell plus encoder/virtual conveyor progression.");
            }
        }

        public string TriggerSourceSummary
        {
            get
            {
                if (UseEncoderTrigger)
                {
                    return T("Tool_VM_TriggerSourceEncoder", "Real encoder trigger active: the photocell creates product zero and TOP/SIDE/reject points are executed when the main encoder reaches the configured machine quotas.");
                }

                if (VirtualConveyorEnabled && !IsTimedTriggerSchedulingActive)
                {
                    return T("Tool_VM_TriggerSourceVirtualConveyor", "Virtual conveyor active: the machine uses real I/O but advances intervention points from the configured speed, without a physical encoder.");
                }

                return T("Tool_VM_TriggerSourceTimed", "Timed trigger active: the photocell edge starts TOP/SIDE pulses after the configured millisecond delays. Encoder tracking is not used for camera triggers.");
            }
        }

        public bool IsTimedPhotocellMachineFlow =>
            string.Equals(TriggerSchedulingMode, "TimedFromPhotocell", StringComparison.OrdinalIgnoreCase);


        public string PrimaryMachineFlowSummary
        {
            get
            {
                var photocell = ResolveInputSignalByCode(BoundProductPhotocellSignalCode);
                var top = ResolveOutputSignalByCode(BoundTopCameraTriggerSignalCode);
                var side = ResolveOutputSignalByCode(BoundSideCameraTriggerSignalCode);
                var reject = ResolveOutputSignalByCode(BoundRejectSignalCode);

                string DescribeInput(MachineSignalDefinition signal) =>
                    signal == null ? "n/d" : $"{signal.SignalCode} {signal.Board}/{signal.Channel}";
                string DescribeOutput(MachineSignalDefinition signal) =>
                    signal == null ? "n/d" : $"{signal.SignalCode} {signal.Board}/{signal.Channel}";

                return string.Format(
                    T("Tool_VM_PrimaryMachineFlowSummary", "Photocell {0} -> TOP {1} -> SIDE {2} -> Reject {3}"),
                    DescribeInput(photocell),
                    DescribeOutput(top),
                    DescribeOutput(side),
                    DescribeOutput(reject));
            }
        }

        public string SignalAssignmentGuidanceSummary =>
            IsTimedPhotocellMachineFlow
                ? T("Tool_VM_SignalAssignmentGuidanceTimed", "Recommended flow for this machine: one product photocell on DI00, then two timed trigger outputs TOP and SIDE. Legacy camera trigger and lighting bindings are optional and should stay secondary.")
                : T("Tool_VM_SignalAssignmentGuidanceLegacy", "Legacy tracked-position flow active: product photocell creates PRODUCT_ZERO and camera/reject outputs follow encoder or virtual-conveyor progression.");

        public string OptionalBindingsSummary =>
            T("Tool_VM_OptionalBindingsSummary", "Optional area: use these bindings only if the machine really needs legacy camera trigger fallback, separate lighting outputs, or external trigger consent.");

        public string TimedTriggerConfigurationSummary => string.Format(
            T("Tool_VM_TimedTriggerConfigurationSummary", "Photocell debounce {0} ms | retrigger gap {1} ms | TOP delay {2} ms pulse {3} ms | SIDE delay {4} ms pulse {5} ms"),
            PhotocellDebounceMs,
            MinimumRetriggerGapMs,
            TopTriggerBaseDelayMs,
            TopTriggerPulseMs,
            SideTriggerBaseDelayMs,
            SideTriggerPulseMs);

        public string TimedTriggerRuntimeStatus
        {
            get => _timedTriggerRuntimeStatus;
            set
            {
                if (string.Equals(_timedTriggerRuntimeStatus, value, StringComparison.Ordinal))
                {
                    return;
                }

                _timedTriggerRuntimeStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerLastEventsSummary));
            }
        }

        public string TimedTriggerLastEventsSummary => string.Format(
            T("Tool_VM_TimedTriggerLastEventsSummary", "Last edge {0} | TOP {1} | SIDE {2}"),
            _lastTimedPhotocellEdgeLocalTime?.ToString("HH:mm:ss.fff") ?? "-",
            _lastTimedTopTriggerLocalTime?.ToString("HH:mm:ss.fff") ?? "-",
            _lastTimedSideTriggerLocalTime?.ToString("HH:mm:ss.fff") ?? "-");

        public string TimedTriggerMetricsSummary => string.Format(
            T("Tool_VM_TimedTriggerMetricsSummary", "Queued {0} | Completed {1} | Cancelled {2} | Errors {3} | TOP shots {4} | SIDE shots {5}"),
            _timedBatchesQueuedCount,
            _timedBatchesCompletedCount,
            _timedBatchesCancelledCount,
            _timedTriggerErrorCount,
            _timedTopShotsCount,
            _timedSideShotsCount);

        public string TimedTriggerObservedDelaySummary => string.Format(
            T("Tool_VM_TimedTriggerObservedDelaySummary", "TOP last {0} ms avg {1} ms | SIDE last {2} ms avg {3} ms"),
            _lastTimedTopActualDelayMs?.ToString("0.0") ?? "-",
            _timedTopShotsCount > 0 ? (_timedTopDelaySumMs / _timedTopShotsCount).ToString("0.0") : "-",
            _lastTimedSideActualDelayMs?.ToString("0.0") ?? "-",
            _timedSideShotsCount > 0 ? (_timedSideDelaySumMs / _timedSideShotsCount).ToString("0.0") : "-");

        public string TimedTriggerOutputSummary
        {
            get
            {
                var topPoint = ResolveTimedTriggerInterventionPoint("TOP");
                var sidePoint = ResolveTimedTriggerInterventionPoint("SIDE");
                var topSignal = ResolveOutputSignalByCode(!string.IsNullOrWhiteSpace(topPoint?.SignalCode) ? topPoint.SignalCode : BoundCameraTriggerSignalCode);
                var sideSignal = ResolveOutputSignalByCode(sidePoint?.SignalCode);

                var topLabel = topSignal == null
                    ? T("Tool_VM_TimedTriggerOutputUnassigned", "TOP not assigned")
                    : $"TOP={topSignal.SignalCode} {topSignal.Board}/{topSignal.Channel}";
                var sideLabel = sideSignal == null
                    ? T("Tool_VM_TimedTriggerSideOutputUnassigned", "SIDE not assigned")
                    : $"SIDE={sideSignal.SignalCode} {sideSignal.Board}/{sideSignal.Channel}";
                return string.Format(
                    T("Tool_VM_TimedTriggerOutputSummary", "{0} | {1}"),
                    topLabel,
                    sideLabel);
            }
        }

        public string BoardsSummary =>
            T("Tool_VM_BoardsSummary", "Target hardware: PCIE-1756-BE for isolated digital I/O and PCIE-1884-AE for encoder counters and auxiliary digital I/O.");

        public string IntegrationStatus =>
            T("Tool_VM_IntegrationStatus", "Template ready for real signal integration. The view separates machine mapping, raw diagnostics, and encoder tracking.");

        public string ConnectionModeSummary => PreferSimulationMode
            ? T("Tool_Status_SimulationPreferred", "Simulation mode preferred for bench tests and first commissioning.")
            : T("Tool_Status_HardwarePreferred", "Real hardware preferred. Use this mode when the boards are installed and wired.");

        public string PreferredConnectionModeLabel => PreferSimulationMode
            ? T("Tool_Status_SimulationRequested", "SIMULATION REQUESTED")
            : T("Tool_Status_RealHardwareRequested", "REAL HARDWARE REQUESTED");

        public string ActiveConnectionModeLabel => IsSimulationMode
            ? T("Tool_Status_SimulationActive", "SIMULATION ACTIVE")
            : T("Tool_Status_RealHardwareActive", "REAL HARDWARE ACTIVE");

        public string ActiveConnectionModeDescription => IsSimulationMode
            ? T("Tool_Status_SimulationActiveDescription", "Outputs, encoder and diagnostics use the internal simulation state.")
            : T("Tool_Status_RealHardwareActiveDescription", "Commands and readings are routed to the installed Advantech boards.");

        public string ApplyConnectionModeButtonLabel => PreferSimulationMode
            ? T("Tool_VM_ApplySwitchSimulation", "Switch to simulation")
            : T("Tool_VM_ApplySwitchHardware", "Switch to real hardware");

        public string MainEncoderSpeedSummary => EncoderChannels.Count > 0
            ? EncoderChannels[GetMainEncoderChannelIndex()].SpeedDisplay
            : T("Tool_VM_MainEncoderSpeedZero", "0.00 m/min");

        public string MainEncoderPiecesSummary
        {
            get
            {
                if (SimulationProductPitchMm <= 0)
                {
                    return T("Tool_VM_MainEncoderPiecesZero", "0.0 pcs/min");
                }

                var speedMetersPerMinute = EncoderChannels.Count > 0 ? EncoderChannels[GetMainEncoderChannelIndex()].SpeedMetersPerMinute : 0.0;
                var piecesPerMinute = speedMetersPerMinute * 1000.0 / SimulationProductPitchMm;
                return $"{piecesPerMinute:0.0} pcs/min";
            }
        }

        public string MainEncoderRawSpeedSummary => EncoderChannels.Count > 0
            ? EncoderChannels[GetMainEncoderChannelIndex()].RawSpeedDisplay
            : T("Tool_VM_MainEncoderRawSpeedZero", "0.000 m/min raw");

        public string MainEncoderRawDeltaSummary => EncoderChannels.Count > 0
            ? EncoderChannels[GetMainEncoderChannelIndex()].DeltaCountsDisplay
            : T("Tool_VM_MainEncoderRawDeltaZero", "0 counts");

        public string MainEncoderSampleSummary => EncoderChannels.Count > 0
            ? EncoderChannels[GetMainEncoderChannelIndex()].SamplePeriodDisplay
            : T("Tool_VM_MainEncoderSampleNone", "Sample: n/a");

        public string MainEncoderCountsSummary => EncoderChannels.Count > 0
            ? string.Format(T("Tool_VM_MainEncoderRawCount", "Raw count: {0:N0}"), EncoderChannels[GetMainEncoderChannelIndex()].CounterValue)
            : T("Tool_VM_MainEncoderRawCountZero", "Raw count: 0");

        public string MainEncoderDiagnosticStateSummary => EncoderChannels.Count > 0
            ? EncoderChannels[GetMainEncoderChannelIndex()].DiagnosticState
            : T("Tool_VM_MainEncoderIdle", "Idle");

        public string ActiveInputsSummary => string.Format(
            T("Tool_VM_ActiveInputsSummary", "{0} / {1} active"),
            InputChannels.Count(c => c.IsActive),
            InputChannels.Count);

        public string ActiveOutputsSummary => string.Format(
            T("Tool_VM_ActiveOutputsSummary", "{0} / {1} ON"),
            OutputChannels.Count(c => c.IsActive),
            OutputChannels.Count);

        public string RefreshingSummary => string.Format(
            T("Tool_VM_Refreshing", "Refreshing: {0}"),
            IsRefreshing);

        public string TrackedProductsSummary => TrackedProducts.Count == 0
            ? T("Tool_VM_NoProductsInTracking", "No products in tracking")
            : string.Format(T("Tool_VM_ProductsInTracking", "{0} products in tracking"), TrackedProducts.Count);

        public string HeartbeatDashboardSummary => IsHeartbeatRunning
            ? string.Format(T("Tool_VM_HeartbeatRunningOn", "Heartbeat running on {0}"), BoundHeartbeatOutputSignalCode)
            : string.Format(T("Tool_VM_HeartbeatStoppedOn", "Heartbeat stopped on {0}"), BoundHeartbeatOutputSignalCode);

        public string EncoderDiagnosticGuidance => VirtualConveyorEnabled && !IsSimulationMode
            ? T("Tool_VM_EncoderGuidanceVirtualConveyor", "Virtual conveyor is active: real I/O is used, but encoder position is advanced from the configured manual speed.")
            : (IsSimulationMode
                ? T("Tool_VM_EncoderGuidanceSimulation", "In simulation, raw data follows the internal conveyor model. On real hardware, use raw counts and raw speed to separate noise from actual motion.")
                : T("Tool_VM_EncoderGuidanceHardware", "If raw count oscillates with no encoder connected, you are seeing noise or electrical disturbance. Filtered speed remains the operating reference."));

        public EncoderConfigurationTemplate MainEncoderTemplate =>
            EncoderTemplates.FirstOrDefault(encoder =>
                string.Equals(encoder.AxisName, BoundMainEncoderAxisCode, StringComparison.OrdinalIgnoreCase))
            ?? EncoderTemplates.FirstOrDefault();

        private double _encoderCalibrationTachometerSpeedMetersPerMinute = 40.0;
        public double EncoderCalibrationTachometerSpeedMetersPerMinute
        {
            get => _encoderCalibrationTachometerSpeedMetersPerMinute;
            set
            {
                _encoderCalibrationTachometerSpeedMetersPerMinute = Math.Max(0.0, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EncoderCalibrationSummary));
            }
        }

        private double _encoderCalibrationHmiSpeedMetersPerMinute;
        public double EncoderCalibrationHmiSpeedMetersPerMinute
        {
            get => _encoderCalibrationHmiSpeedMetersPerMinute;
            set
            {
                _encoderCalibrationHmiSpeedMetersPerMinute = Math.Max(0.0, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EncoderCalibrationSummary));
            }
        }

        public string EncoderCalibrationSummary
        {
            get
            {
                var encoder = MainEncoderTemplate;
                if (encoder == null)
                {
                    return T("Tool_VM_EncoderCalibrationNoMainEncoder", "Main encoder not configured.");
                }

                var hmiSpeed = ResolveEncoderCalibrationHmiSpeed();
                if (EncoderCalibrationTachometerSpeedMetersPerMinute <= 0.0 || hmiSpeed <= 0.0 || encoder.MillimetersPerRevolution <= 0.0)
                {
                    return string.Format(
                        T("Tool_VM_EncoderCalibrationWaiting", "Insert tachometer speed and HMI speed. Current main encoder mm/rev: {0:0.###}."),
                        encoder.MillimetersPerRevolution);
                }

                var factor = EncoderCalibrationTachometerSpeedMetersPerMinute / hmiSpeed;
                var newMillimetersPerRevolution = encoder.MillimetersPerRevolution * factor;
                var currentCountsPerMillimeter = CalculateCountsPerMillimeter(encoder);
                var newCountsPerMillimeter = currentCountsPerMillimeter / factor;
                return string.Format(
                    T("Tool_VM_EncoderCalibrationSummary", "Factor {0:0.###}: mm/rev {1:0.###} -> {2:0.###}; counts/mm {3:0.###} -> {4:0.###}."),
                    factor,
                    encoder.MillimetersPerRevolution,
                    newMillimetersPerRevolution,
                    currentCountsPerMillimeter,
                    newCountsPerMillimeter);
            }
        }

        public string MachineZeroSummary
        {
            get
            {
                var encoder = MainEncoderTemplate;
                if (encoder == null)
                {
                    return T("Tool_VM_MachineZeroNotConfigured", "Machine zero not configured");
                }

                return string.Format(
                    T("Tool_VM_MachineZeroSummary", "{0} | photocell at {1:0.0} mm"),
                    encoder.MachineZeroLabel,
                    encoder.PhotocellMachineOffsetMm);
            }
        }

        public string InterventionQuotaSummary
        {
            get
            {
                var encoder = MainEncoderTemplate;
                if (encoder == null)
                {
                    return T("Tool_VM_EventPositionsUnavailable", "Event positions not available");
                }

                var topTrigger = InterventionPoints.FirstOrDefault(p => p.Enabled && string.Equals(p.PointCode, "CAMERA_TRIGGER_TOP", StringComparison.OrdinalIgnoreCase));
                var sideTrigger = InterventionPoints.FirstOrDefault(p => p.Enabled && string.Equals(p.PointCode, "CAMERA_TRIGGER_SIDE", StringComparison.OrdinalIgnoreCase));
                var legacyTrigger = InterventionPoints.FirstOrDefault(p => p.Enabled && string.Equals(p.PointCode, "CAMERA_TRIGGER", StringComparison.OrdinalIgnoreCase));
                var reject = InterventionPoints.FirstOrDefault(p => p.Enabled && string.Equals(p.PointCode, "REJECT", StringComparison.OrdinalIgnoreCase));

                string Quota(MachineInterventionPoint point) => point == null ? "n/d" : $"{encoder.PhotocellMachineOffsetMm + point.EffectiveOffsetMm:0.0} mm";

                return string.Format(
                    T("Tool_VM_EventPositionsSummary", "Photocell {0:0.0} mm | TOP trigger {1} | SIDE trigger {2} | Legacy trigger {3} | Reject {4}"),
                    encoder.PhotocellMachineOffsetMm,
                    Quota(topTrigger),
                    Quota(sideTrigger),
                    Quota(legacyTrigger),
                    Quota(reject));
            }
        }

        public string InterventionQuotaFlowSummary
        {
            get
            {
                var encoder = MainEncoderTemplate;
                if (encoder == null)
                {
                    return T("Tool_VM_PositionCalculationHint", "Configure machine zero and the photocell position to enable position calculation.");
                }

                return string.Format(
                    T("Tool_VM_PositionFlowSummary", "Fixed machine zero: {0}. The photocell is at {1:0.0} mm machine position. Each event point keeps its own relative product position and generates an absolute machine position equal to photocell + event position."),
                    encoder.MachineZeroLabel,
                    encoder.PhotocellMachineOffsetMm);
            }
        }

        public string InterventionTimelineSummary => InterventionTimelineItems.Count == 0
            ? T("Tool_VM_NoInterventionPoints", "No intervention points configured")
            : string.Format(T("Tool_VM_InterventionPointsSorted", "{0} points sorted by ascending machine position"), InterventionTimelineItems.Count);

        public string MachineEventsSummary => MachineOperationalEvents.Count == 0
            ? T("Tool_VM_NoMachineEvents", "No machine events recorded in this session")
            : string.Format(T("Tool_VM_MachineEventsRecorded", "{0} machine events recorded"), MachineOperationalEvents.Count);

        public string LastMachineEventSummary => MachineOperationalEvents.Count == 0
            ? T("Tool_VM_NoRecentEvent", "No recent event")
            : $"{MachineOperationalEvents[0].Title} | {MachineOperationalEvents[0].Status}";

        public string MachineEventsGuidance => T("Tool_VM_MachineEventsGuidance", "This page shows only the operational events useful for commissioning and testing: product detected, trigger, positions reached, result, reject, alarm, and mode changes. The full log remains on the Log page.");

        public string MachineEventsFusionSummary => MachineOperationalEvents.Count == 0
            ? T("Tool_VM_NoFusionMappingYet", "No fusion-ready event mapping yet")
            : string.Format(
                T("Tool_VM_FusionEventsReady", "{0} runtime events now carry a main-project-compatible category and machine status"),
                MachineOperationalEvents.Count);

        public string LastMachineEventFusionSummary => MachineOperationalEvents.Count == 0
            ? T("Tool_VM_NoRecentFusionEvent", "No compatible event classification yet")
            : $"{MachineOperationalEvents[0].MainProjectCategory} | {MachineOperationalEvents[0].MainProjectMachineStatus}";

        public string MachineEventsFusionGuidance => T("Tool_VM_MachineEventsFusionGuidance", "Each event is classified with the coarse category and machine state expected by the refreshed main project, while preserving the richer tool-specific detail.");

        private MachineInterventionPoint _selectedInterventionPoint;
        public MachineInterventionPoint SelectedInterventionPoint
        {
            get => _selectedInterventionPoint;
            set
            {
                if (ReferenceEquals(_selectedInterventionPoint, value))
                {
                    return;
                }

                _selectedInterventionPoint = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedInterventionPointMachineQuotaSummary));
                OnPropertyChanged(nameof(SelectedInterventionPointRelativeQuotaSummary));
                OnPropertyChanged(nameof(SelectedInterventionPointSignalSummary));
                NotifySelectedInterventionOutputMappingChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string SelectedInterventionPointMachineQuotaSummary
        {
            get
            {
                var point = SelectedInterventionPoint;
                var photocellMachineOffset = MainEncoderTemplate?.PhotocellMachineOffsetMm ?? 0.0;
                if (point == null)
                {
                    return T("Tool_VM_SelectPointMachine", "Select a point to see the machine position.");
                }

                return string.Format(
                    T("Tool_VM_SelectedPointMachine", "{0:0.0} mm machine"),
                    photocellMachineOffset + point.EffectiveOffsetMm);
            }
        }

        public string SelectedInterventionPointRelativeQuotaSummary => SelectedInterventionPoint == null
            ? T("Tool_VM_SelectPointRelative", "Select a point to see the relative position.")
            : string.Format(
                T("Tool_VM_SelectedPointRelative", "{0:0.0} mm from PRODUCT_ZERO"),
                SelectedInterventionPoint.EffectiveOffsetMm);

        public string SelectedInterventionPointSignalSummary => SelectedInterventionPoint == null
            ? T("Tool_VM_SignalNotSelected", "Signal not selected")
            : (string.IsNullOrWhiteSpace(SelectedInterventionPoint.SignalCode)
                ? ResolveDefaultSignalCodeForAction(SelectedInterventionPoint.ActionType)
                : SelectedInterventionPoint.SignalCode);

        public IReadOnlyList<string> InterventionOutputBoardOptions { get; } =
            new[] { "PCIE-1756-BE", "PCIE-1884-AE" };

        public IReadOnlyList<MachineSignalPolarity> InterventionOutputPolarityOptions { get; } =
            Enum.GetValues(typeof(MachineSignalPolarity)).Cast<MachineSignalPolarity>().ToList();

        public string SelectedInterventionOutputBoard
        {
            get => ResolveSelectedInterventionOutput()?.Board ?? string.Empty;
            set => UpdateSelectedInterventionOutput(signal => signal.Board = value);
        }

        public string SelectedInterventionOutputChannel
        {
            get => ResolveSelectedInterventionOutput()?.Channel ?? string.Empty;
            set => UpdateSelectedInterventionOutput(signal => signal.Channel = value);
        }

        public MachineSignalPolarity SelectedInterventionOutputPolarity
        {
            get => ResolveSelectedInterventionOutput()?.Polarity ?? MachineSignalPolarity.ActiveHigh;
            set => UpdateSelectedInterventionOutput(signal => signal.Polarity = value);
        }

        public bool SelectedInterventionOutputIsReal
        {
            get => ResolveSelectedInterventionOutput()?.ReservedForRealSignal == true;
            set => UpdateSelectedInterventionOutput(signal => signal.ReservedForRealSignal = value);
        }

        public string SelectedInterventionOutputMappingStatus
        {
            get
            {
                var signal = ResolveSelectedInterventionOutput();
                if (signal == null)
                {
                    return T("Tool_Commissioning_OutputMappingMissing", "No global output mapping exists for the selected signal. Enter board and channel to create it.");
                }

                return string.Format(
                    T("Tool_Commissioning_OutputMappingSummary", "Global machine output: {0}/{1}, {2}, physical signal {3}"),
                    signal.Board,
                    signal.Channel,
                    signal.Polarity,
                    signal.ReservedForRealSignal ? "enabled" : "disabled");
            }
        }

        public string BoundTopCameraTriggerSignalCode
        {
            get
            {
                var point = ResolveTimedTriggerInterventionPoint("TOP");
                return !string.IsNullOrWhiteSpace(point?.SignalCode)
                    ? point.SignalCode
                    : BoundCameraTriggerSignalCode;
            }
            set
            {
                var point = GetOrCreateTimedTriggerInterventionPoint("TOP");
                var normalized = value ?? string.Empty;
                if (string.Equals(point.SignalCode, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                point.SignalCode = normalized;
                point.ActionType = "TriggerCamera";
                point.Enabled = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerOutputSummary));
                OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
                RefreshMachineSignalStatuses();
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("top camera trigger signal changed");
            }
        }

        public string BoundSideCameraTriggerSignalCode
        {
            get
            {
                var point = ResolveTimedTriggerInterventionPoint("SIDE");
                return point?.SignalCode ?? string.Empty;
            }
            set
            {
                var point = GetOrCreateTimedTriggerInterventionPoint("SIDE");
                var normalized = value ?? string.Empty;
                if (string.Equals(point.SignalCode, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                point.SignalCode = normalized;
                point.ActionType = "TriggerCamera";
                point.Enabled = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerOutputSummary));
                OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
                RefreshMachineSignalStatuses();
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("side camera trigger signal changed");
            }
        }



        public string BoundRightCameraTriggerSignalCode
        {
            get => InterventionPoints.FirstOrDefault(point => IsCameraTriggerPointForRole(point, "right"))?.SignalCode ?? string.Empty;
            set
            {
                var existing = InterventionPoints.FirstOrDefault(candidate => IsCameraTriggerPointForRole(candidate, "right"));
                var point = existing ?? GetOrCreateTimedTriggerInterventionPoint("REAR");
                if (existing == null) point.Enabled = false;
                var normalized = value ?? string.Empty;
                if (existing != null && string.Equals(point.SignalCode, normalized, StringComparison.OrdinalIgnoreCase)) return;
                point.SignalCode = normalized;
                point.ActionType = "TriggerCamera";
                OnPropertyChanged();
                OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
                RefreshMachineSignalStatuses();
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("right camera trigger signal changed");
            }
        }

        private string _machineControllerStatus = ToolLocalizationService.Current.Get("Tool_VM_ControllerNotInitialized", "Controller not initialized");
        public string MachineControllerStatus
        {
            get => _machineControllerStatus;
            set { _machineControllerStatus = value; OnPropertyChanged(); }
        }

        private string _lastTrackedProductStatus = ToolLocalizationService.Current.Get("Tool_VM_NoTrackedProduct", "No tracked product");
        public string LastTrackedProductStatus
        {
            get => _lastTrackedProductStatus;
            set { _lastTrackedProductStatus = value; OnPropertyChanged(); }
        }

        private string _machineTriggerMode = "Hybrid";
        public string MachineTriggerMode
        {
            get => _machineTriggerMode;
            set { var mode = string.IsNullOrWhiteSpace(value) ? "Hybrid" : (value.Trim().Equals("Internal", StringComparison.OrdinalIgnoreCase) ? "Internal" : (value.Trim().Equals("External", StringComparison.OrdinalIgnoreCase) ? "External" : "Hybrid")); _machineTriggerMode = mode; OnPropertyChanged(); MarkConfigurationDirty(); QueueRuntimePreviewApply("trigger mode changed"); }
        }

        private bool _useEncoderTrigger = true;
        private bool _isSynchronizingTriggerSource;
        public bool UseEncoderTrigger
        {
            get => _useEncoderTrigger;
            set
            {
                if (_useEncoderTrigger == value)
                {
                    return;
                }

                _useEncoderTrigger = value;

                if (!_isSynchronizingTriggerSource && !_suppressConfigurationDirtyTracking)
                {
                    _isSynchronizingTriggerSource = true;
                    if (value)
                    {
                        _triggerSchedulingMode = "VirtualConveyor";
                        _virtualConveyorEnabled = false;
                    }
                    else
                    {
                        _triggerSchedulingMode = "TimedFromPhotocell";
                        _virtualConveyorEnabled = false;
                    }
                    _isSynchronizingTriggerSource = false;
                }

                NotifyTriggerSourceProperties();
                MarkConfigurationDirty();
                UpdateVirtualConveyorTimerState();
                QueueRuntimePreviewApply("trigger source changed");
            }
        }

        private string _triggerSchedulingMode = "VirtualConveyor";
        public string TriggerSchedulingMode
        {
            get => _triggerSchedulingMode;
            set
            {
                var mode = string.Equals(value, "TimedFromPhotocell", StringComparison.OrdinalIgnoreCase)
                    ? "TimedFromPhotocell"
                    : "VirtualConveyor";
                if (string.Equals(_triggerSchedulingMode, mode, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _triggerSchedulingMode = mode;
                if (string.Equals(_triggerSchedulingMode, "TimedFromPhotocell", StringComparison.OrdinalIgnoreCase) && _virtualConveyorEnabled)
                {
                    _virtualConveyorEnabled = false;
                    OnPropertyChanged(nameof(VirtualConveyorEnabled));
                    OnPropertyChanged(nameof(VirtualConveyorSummary));
                    OnPropertyChanged(nameof(EncoderDiagnosticGuidance));
                }

                SyncUseEncoderTriggerFromLegacyMode();
                NotifyTriggerSourceProperties();
                MarkConfigurationDirty();
                UpdateVirtualConveyorTimerState();
                QueueRuntimePreviewApply("trigger scheduling mode changed");
            }
        }

        private string _machineRejectMode = "EncoderTracked";
        public string MachineRejectMode
        {
            get => _machineRejectMode;
            set
            {
                var mode = string.IsNullOrWhiteSpace(value) ? "EncoderTracked" : value.Trim();
                if (!string.Equals(mode, "Immediate", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    mode = "EncoderTracked";
                }

                _machineRejectMode = mode;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RejectModeOperatingSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("reject mode changed");
            }
        }

        private string _boundProductPhotocellSignalCode = "IN_PRODUCT_PHOTOCELL";
        public string BoundProductPhotocellSignalCode
        {
            get => _boundProductPhotocellSignalCode;
            set { _boundProductPhotocellSignalCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrimaryMachineFlowSummary)); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("product photocell binding changed"); }
        }

        private string _boundExternalTriggerEnableSignalCode = "IN_EXTERNAL_TRIGGER_ENABLE";
        public string BoundExternalTriggerEnableSignalCode
        {
            get => _boundExternalTriggerEnableSignalCode;
            set { _boundExternalTriggerEnableSignalCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(SignalAssignmentGuidanceSummary)); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("external trigger enable binding changed"); }
        }

        private string _boundCameraTriggerSignalCode = "OUT_CAMERA_TOP_TRIGGER";
        public string BoundCameraTriggerSignalCode
        {
            get => _boundCameraTriggerSignalCode;
            set { _boundCameraTriggerSignalCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimedTriggerOutputSummary)); OnPropertyChanged(nameof(PrimaryMachineFlowSummary)); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("camera trigger binding changed"); }
        }

        private string _boundRejectSignalCode = "OUT_REJECT_SOLENOID";
        public string BoundRejectSignalCode
        {
            get => _boundRejectSignalCode;
            set { _boundRejectSignalCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrimaryMachineFlowSummary)); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("reject output binding changed"); }
        }

        private string _boundGeneralAlarmSignalCode = "OUT_GENERAL_ALARM";
        public string BoundGeneralAlarmSignalCode
        {
            get => _boundGeneralAlarmSignalCode;
            set { _boundGeneralAlarmSignalCode = value; OnPropertyChanged(); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("general alarm binding changed"); }
        }

        private string _boundMainEncoderAxisCode = "ENC_CONVEYOR_MAIN";
        public string BoundMainEncoderAxisCode
        {
            get => _boundMainEncoderAxisCode;
            set { _boundMainEncoderAxisCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(MachineZeroSummary)); OnPropertyChanged(nameof(InterventionQuotaSummary)); OnPropertyChanged(nameof(InterventionQuotaFlowSummary)); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("main encoder axis changed"); }
        }

        public ObservableCollection<string> RejectActuatorTypeOptions { get; } =
            new ObservableCollection<string> { "AirBlast", "Monostable", "Bistable" };

        private string _rejectActuatorType = "Monostable";
        public string RejectActuatorType
        {
            get => _rejectActuatorType;
            set
            {
                var selected = RejectActuatorTypeOptions.Contains(value) ? value : "Monostable";
                if (_rejectActuatorType == selected) return;
                _rejectActuatorType = selected;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RejectActuatorSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("reject actuator type changed");
            }
        }

        private int _configuredRejectPulseMs = 80;
        public int ConfiguredRejectPulseMs
        {
            get => _configuredRejectPulseMs;
            set { _configuredRejectPulseMs = Math.Max(20, value); OnPropertyChanged(); MarkConfigurationDirty(); QueueRuntimePreviewApply("reject pulse changed"); }
        }

        private string _boundRejectOpenSignalCode = "OUT_REJECT_OPEN";
        public string BoundRejectOpenSignalCode
        {
            get => _boundRejectOpenSignalCode;
            set { _boundRejectOpenSignalCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(RejectActuatorSummary)); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("reject open output binding changed"); }
        }

        private string _boundRejectCloseSignalCode = "OUT_REJECT_CLOSE";
        public string BoundRejectCloseSignalCode
        {
            get => _boundRejectCloseSignalCode;
            set { _boundRejectCloseSignalCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(RejectActuatorSummary)); RefreshMachineSignalStatuses(); MarkConfigurationDirty(); QueueRuntimePreviewApply("reject close output binding changed"); }
        }

        private int _configuredRejectCloseDelayMs = 300;
        /// <summary>Tempo fra la salita di OPEN e l'impulso CLOSE dello scarto a due uscite.</summary>
        public int ConfiguredRejectCloseDelayMs
        {
            get => _configuredRejectCloseDelayMs;
            set { _configuredRejectCloseDelayMs = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(RejectActuatorSummary)); MarkConfigurationDirty(); QueueRuntimePreviewApply("reject close delay changed"); }
        }

        /// <summary>
        /// Descrive quale attuatore di scarto e' attivo: open/close se entrambe le uscite hanno un
        /// canale, altrimenti l'impulso singolo storico.
        /// </summary>
        public string RejectActuatorSummary
        {
            get
            {
                if (RejectActuatorType == "Bistable")
                    return "Bistabile non abilitato: servono i sensori di posizione OPEN e CLOSE sugli ingressi.";
                return $"{RejectActuatorType}: impulso singolo su {BoundRejectSignalCode} per {ConfiguredRejectPulseMs} ms; LOW al termine.";
            }
        }

        /// <summary>Canali selezionabili per le uscite: vuoto = non assegnato.</summary>
        public IReadOnlyList<string> OutputChannelOptions { get; } = BuildChannelOptions("DO", Pcie1756ChannelMap.DoChannelCount);

        /// <summary>Canali selezionabili per gli ingressi: vuoto = non assegnato.</summary>
        public IReadOnlyList<string> InputChannelOptions { get; } = BuildChannelOptions("DI", Pcie1756ChannelMap.DiChannelCount);

        private static IReadOnlyList<string> BuildChannelOptions(string prefix, int pcie1756Count)
        {
            var options = new List<string> { string.Empty };
            for (int channel = 0; channel < pcie1756Count; channel++)
            {
                options.Add($"{prefix}{channel:D2}");
            }

            for (int offset = 0; offset < Pcie1756ChannelMap.Pcie1884ChannelCount; offset++)
            {
                options.Add($"{prefix}{Pcie1756ChannelMap.Pcie1884ChannelBase + offset}");
            }

            return options;
        }

        private int _photocellDebounceMs = 80;
        public int PhotocellDebounceMs
        {
            get => _photocellDebounceMs;
            set
            {
                var normalized = Math.Max(0, value);
                if (_photocellDebounceMs == normalized)
                {
                    return;
                }

                _photocellDebounceMs = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerConfigurationSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("photocell debounce changed");
            }
        }

        private int _minimumRetriggerGapMs = 120;
        public int MinimumRetriggerGapMs
        {
            get => _minimumRetriggerGapMs;
            set
            {
                var normalized = Math.Max(0, value);
                if (_minimumRetriggerGapMs == normalized)
                {
                    return;
                }

                _minimumRetriggerGapMs = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerConfigurationSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("minimum retrigger gap changed");
            }
        }

        private int _topTriggerBaseDelayMs;
        public int TopTriggerBaseDelayMs
        {
            get => _topTriggerBaseDelayMs;
            set
            {
                var normalized = Math.Max(0, value);
                if (_topTriggerBaseDelayMs == normalized)
                {
                    return;
                }

                _topTriggerBaseDelayMs = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerConfigurationSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("top trigger base delay changed");
            }
        }

        private int _sideTriggerBaseDelayMs;
        public int SideTriggerBaseDelayMs
        {
            get => _sideTriggerBaseDelayMs;
            set
            {
                var normalized = Math.Max(0, value);
                if (_sideTriggerBaseDelayMs == normalized)
                {
                    return;
                }

                _sideTriggerBaseDelayMs = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerConfigurationSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("side trigger base delay changed");
            }
        }

        private int _topTriggerPulseMs = 40;
        public int TopTriggerPulseMs
        {
            get => _topTriggerPulseMs;
            set
            {
                var normalized = Math.Max(10, value);
                if (_topTriggerPulseMs == normalized)
                {
                    return;
                }

                _topTriggerPulseMs = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerConfigurationSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("top trigger pulse changed");
            }
        }

        private int _sideTriggerPulseMs = 40;
        public int SideTriggerPulseMs
        {
            get => _sideTriggerPulseMs;
            set
            {
                var normalized = Math.Max(10, value);
                if (_sideTriggerPulseMs == normalized)
                {
                    return;
                }

                _sideTriggerPulseMs = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TimedTriggerConfigurationSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("side trigger pulse changed");
            }
        }

        private readonly Dictionary<string, MultiShotTriggerOptions> _multiShotProfileEditors =
            new Dictionary<string, MultiShotTriggerOptions>(StringComparer.OrdinalIgnoreCase);
        private bool _isApplyingMultiShotEditorProfile;
        private string _selectedMultiShotProfileKey = "Side";

        public ObservableCollection<MultiShotProfileOption> MultiShotProfiles { get; } =
            new ObservableCollection<MultiShotProfileOption>
            {
                new MultiShotProfileOption { Key = "Side", Label = "Left / Side" },
                new MultiShotProfileOption { Key = "Right", Label = "Right (legacy profile may target Rear)" },
                new MultiShotProfileOption { Key = "Rear", Label = "Rear" },
                new MultiShotProfileOption { Key = "Bottom", Label = "Bottom" }
            };

        public ObservableCollection<string> DualIlluminationOutputLines { get; } =
            new ObservableCollection<string> { "Line3", "Line4" };

        public ObservableCollection<string> DualIlluminationOutputSources { get; } =
            new ObservableCollection<string> { "ExposureActive", "PulseOnStartofExposure" };

        public string SelectedMultiShotProfileKey
        {
            get => _selectedMultiShotProfileKey;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Side" : value.Trim();
                if (string.Equals(_selectedMultiShotProfileKey, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                PersistCurrentMultiShotEditorProfile();
                _selectedMultiShotProfileKey = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedMultiShotProfileLabel));
                OnPropertyChanged(nameof(CurrentMultiShotPreviousSnapshot));
                ApplyMultiShotEditorProfile(ResolveCurrentMultiShotEditorProfile());
            }
        }

        public string SelectedMultiShotProfileLabel
        {
            get
            {
                var profile = MultiShotProfiles.FirstOrDefault(item =>
                    string.Equals(item.Key, SelectedMultiShotProfileKey, StringComparison.OrdinalIgnoreCase));
                return profile?.Label ?? "Left / Side";
            }
        }

        /// <summary>
        /// Snapshot dell'ultimo salvataggio per il profilo attualmente visualizzato nell'editor.
        /// Usato in XAML per mostrare "Prec.: X" sotto ogni campo. Sostituito come oggetto
        /// intero tramite OnPropertyChanged — non richiede INPC interno.
        /// </summary>
        public MultiShotProfileSnapshot CurrentMultiShotPreviousSnapshot
        {
            get
            {
                var key = string.IsNullOrWhiteSpace(SelectedMultiShotProfileKey) ? "Side" : SelectedMultiShotProfileKey;
                return _multiShotPreviousSnapshots.TryGetValue(key, out var snap)
                    ? snap
                    : MultiShotProfileSnapshot.Empty;
            }
        }

        private bool _sideLeftMultiShotEnabled;
        public bool SideLeftMultiShotEnabled
        {
            get => _sideLeftMultiShotEnabled;
            set
            {
                if (_sideLeftMultiShotEnabled == value)
                {
                    return;
                }

                _sideLeftMultiShotEnabled = value;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot enabled changed");
            }
        }

        private string _sideLeftMultiShotTriggerOutputName = "OUT_CAMERA_SIDE_TRIGGER";
        public string SideLeftMultiShotTriggerOutputName
        {
            get => _sideLeftMultiShotTriggerOutputName;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "OUT_CAMERA_SIDE_TRIGGER" : value.Trim();
                if (string.Equals(_sideLeftMultiShotTriggerOutputName, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _sideLeftMultiShotTriggerOutputName = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot trigger output changed");
            }
        }

        private int _sideLeftMultiShotTriggerPulseMs = 10;
        public int SideLeftMultiShotTriggerPulseMs
        {
            get => _sideLeftMultiShotTriggerPulseMs;
            set
            {
                var normalized = Math.Max(1, value);
                if (_sideLeftMultiShotTriggerPulseMs == normalized)
                {
                    return;
                }

                _sideLeftMultiShotTriggerPulseMs = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot pulse changed");
            }
        }

        private int _sideLeftMultiShotMinimumInterShotIntervalMs = 30;
        public int SideLeftMultiShotMinimumInterShotIntervalMs
        {
            get => _sideLeftMultiShotMinimumInterShotIntervalMs;
            set
            {
                var normalized = Math.Max(0, value);
                if (_sideLeftMultiShotMinimumInterShotIntervalMs == normalized)
                {
                    return;
                }

                _sideLeftMultiShotMinimumInterShotIntervalMs = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot minimum interval changed");
            }
        }

        private int _sideLeftMultiShotShotCount = 1;
        public int SideLeftMultiShotShotCount
        {
            get => _sideLeftMultiShotShotCount;
            set
            {
                var normalized = Math.Max(1, Math.Min(value, SideLeftMultiShotMaxShotCount));
                if (_sideLeftMultiShotShotCount == normalized)
                {
                    return;
                }

                _sideLeftMultiShotShotCount = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot shot count changed");
            }
        }

        private int _sideLeftMultiShotMaxShotCount = 12;
        public int SideLeftMultiShotMaxShotCount
        {
            get => _sideLeftMultiShotMaxShotCount;
            set
            {
                var normalized = Math.Max(1, value);
                if (_sideLeftMultiShotMaxShotCount == normalized)
                {
                    return;
                }

                _sideLeftMultiShotMaxShotCount = normalized;
                if (_sideLeftMultiShotShotCount > normalized)
                {
                    _sideLeftMultiShotShotCount = normalized;
                    OnPropertyChanged(nameof(SideLeftMultiShotShotCount));
                }

                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot max shots changed");
            }
        }

        private long _sideLeftMultiShotInitialOffsetPulses;
        public long SideLeftMultiShotInitialOffsetPulses
        {
            get => _sideLeftMultiShotInitialOffsetPulses;
            set
            {
                var normalized = Math.Max(0, value);
                if (_sideLeftMultiShotInitialOffsetPulses == normalized)
                {
                    return;
                }

                _sideLeftMultiShotInitialOffsetPulses = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot initial offset changed");
            }
        }

        private long _sideLeftMultiShotStepPulses;
        public long SideLeftMultiShotStepPulses
        {
            get => _sideLeftMultiShotStepPulses;
            set
            {
                var normalized = Math.Max(0, value);
                if (_sideLeftMultiShotStepPulses == normalized)
                {
                    return;
                }

                _sideLeftMultiShotStepPulses = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot encoder step changed");
            }
        }

        private int _sideLeftMultiShotSessionTimeoutMs = 3000;
        public int SideLeftMultiShotSessionTimeoutMs
        {
            get => _sideLeftMultiShotSessionTimeoutMs;
            set
            {
                var normalized = Math.Max(250, value);
                if (_sideLeftMultiShotSessionTimeoutMs == normalized)
                {
                    return;
                }

                _sideLeftMultiShotSessionTimeoutMs = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot timeout changed");
            }
        }

        private bool _sideLeftMultiShotRequireMachineRunning = true;
        public bool SideLeftMultiShotRequireMachineRunning
        {
            get => _sideLeftMultiShotRequireMachineRunning;
            set
            {
                if (_sideLeftMultiShotRequireMachineRunning == value)
                {
                    return;
                }

                _sideLeftMultiShotRequireMachineRunning = value;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot run guard changed");
            }
        }

        private bool _sideLeftMultiShotPositiveDirection = true;
        public bool SideLeftMultiShotPositiveDirection
        {
            get => _sideLeftMultiShotPositiveDirection;
            set
            {
                if (_sideLeftMultiShotPositiveDirection == value)
                {
                    return;
                }

                _sideLeftMultiShotPositiveDirection = value;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left multishot direction changed");
            }
        }

        private double _sideLeftVisionProStepMm;
        public double SideLeftVisionProStepMm
        {
            get => _sideLeftVisionProStepMm;
            set
            {
                var normalized = Math.Max(0.0, value);
                if (Math.Abs(_sideLeftVisionProStepMm - normalized) < 0.0001)
                {
                    return;
                }

                _sideLeftVisionProStepMm = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left VisionPro stitching step changed");
            }
        }

        private double _sideLeftVisionProMmPerPixel;
        public double SideLeftVisionProMmPerPixel
        {
            get => _sideLeftVisionProMmPerPixel;
            set
            {
                var normalized = Math.Max(0.0, value);
                if (Math.Abs(_sideLeftVisionProMmPerPixel - normalized) < 0.000001)
                {
                    return;
                }

                _sideLeftVisionProMmPerPixel = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("side/left VisionPro calibration changed");
            }
        }

        private bool _sideLeftDualIlluminationEnabled;
        public bool SideLeftDualIlluminationEnabled
        {
            get => _sideLeftDualIlluminationEnabled;
            set
            {
                if (_sideLeftDualIlluminationEnabled == value)
                {
                    return;
                }

                _sideLeftDualIlluminationEnabled = value;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("dual illumination enabled changed");
            }
        }

        private bool _sideLeftDualIlluminationFrontFirst = true;
        public bool SideLeftDualIlluminationFrontFirst
        {
            get => _sideLeftDualIlluminationFrontFirst;
            set
            {
                if (_sideLeftDualIlluminationFrontFirst == value)
                {
                    return;
                }

                _sideLeftDualIlluminationFrontFirst = value;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("dual illumination first preset changed");
            }
        }

        private double _sideLeftDualIlluminationExposureUs = 500.0;
        public double SideLeftDualIlluminationExposureUs
        {
            get => _sideLeftDualIlluminationExposureUs;
            set
            {
                var normalized = Math.Max(1.0, Math.Min(1000000.0, value));
                if (Math.Abs(_sideLeftDualIlluminationExposureUs - normalized) < 0.0001)
                {
                    return;
                }

                _sideLeftDualIlluminationExposureUs = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("dual illumination exposure changed");
            }
        }

        private string _sideLeftDualIlluminationFrontOutputLine = "Line3";
        public string SideLeftDualIlluminationFrontOutputLine
        {
            get => _sideLeftDualIlluminationFrontOutputLine;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Line3" : value.Trim();
                if (string.Equals(_sideLeftDualIlluminationFrontOutputLine, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _sideLeftDualIlluminationFrontOutputLine = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("dual illumination front output changed");
            }
        }

        private string _sideLeftDualIlluminationBackOutputLine = "Line4";
        public string SideLeftDualIlluminationBackOutputLine
        {
            get => _sideLeftDualIlluminationBackOutputLine;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Line4" : value.Trim();
                if (string.Equals(_sideLeftDualIlluminationBackOutputLine, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _sideLeftDualIlluminationBackOutputLine = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("dual illumination back output changed");
            }
        }

        private string _sideLeftDualIlluminationOutputSource = "ExposureActive";
        public string SideLeftDualIlluminationOutputSource
        {
            get => _sideLeftDualIlluminationOutputSource;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "ExposureActive" : value.Trim();
                if (string.Equals(_sideLeftDualIlluminationOutputSource, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _sideLeftDualIlluminationOutputSource = normalized;
                OnPropertyChanged();
                OnMultiShotConfigurationChanged("dual illumination output source changed");
            }
        }

        public string SideLeftDualIlluminationSummary
        {
            get
            {
                if (!SideLeftDualIlluminationEnabled)
                {
                    return T("Tool_MultiShot_DualDisabledSummary", "Dual illumination is disabled; camera Cycling Presets are not changed.");
                }

                if (SideLeftMultiShotShotCount < 2 || SideLeftMultiShotShotCount % 2 != 0)
                {
                    return T("Tool_MultiShot_DualInvalidShots", "Dual illumination requires an even shot count of at least 2.");
                }

                string first = SideLeftDualIlluminationFrontFirst
                    ? T("Tool_MultiShot_DualFront", "Front")
                    : T("Tool_MultiShot_DualBack", "Backlight");
                return string.Format(
                    T("Tool_MultiShot_DualSummary", "DALSA presets: {0} first, exposure {1:0.###} us, Front={2}, Backlight={3}, source={4}."),
                    first,
                    SideLeftDualIlluminationExposureUs,
                    SideLeftDualIlluminationFrontOutputLine,
                    SideLeftDualIlluminationBackOutputLine,
                    SideLeftDualIlluminationOutputSource);
            }
        }

        public string SideLeftMultiShotConfigurationSummary
        {
            get
            {
                var state = SideLeftMultiShotEnabled
                    ? T("Tool_MultiShot_StateEnabled", "Enabled")
                    : T("Tool_MultiShot_StateDisabled", "Disabled");
                return string.Format(
                    T("Tool_MultiShot_ConfigSummary", "{0}: {1} shot(s), output {2}, pulse {3} ms, minimum interval {4} ms, camera offset {5} pulses, shot step {6} pulses."),
                    state,
                    SideLeftMultiShotShotCount,
                    SideLeftMultiShotTriggerOutputName,
                    SideLeftMultiShotTriggerPulseMs,
                    SideLeftMultiShotMinimumInterShotIntervalMs,
                    ResolvedSideLeftMultiShotInitialOffsetPulses,
                    ResolvedSideLeftMultiShotStepPulses);
            }
        }

        public double SideLeftMultiShotCameraOffsetMm => ResolveSideLeftCameraOffsetMm() ?? 0.0;

        public double SideLeftMultiShotCountsPerMillimeter => ResolveMainEncoderCountsPerMillimeter();

        public long ResolvedSideLeftMultiShotInitialOffsetPulses => ResolveSideLeftInitialOffsetPulses();

        public long ResolvedSideLeftMultiShotStepPulses => ResolveSideLeftStepPulses();

        public string SideLeftMultiShotGeometrySummary
        {
            get
            {
                var countsPerMillimeter = SideLeftMultiShotCountsPerMillimeter;
                var cameraOffsetMm = SideLeftMultiShotCameraOffsetMm;
                if (countsPerMillimeter <= 0.0)
                {
                    return T("Tool_MultiShot_GeometryInvalid", "Configure the main encoder calibration before enabling Side/Left MultiShot.");
                }

                return string.Format(
                    T("Tool_MultiShot_GeometrySummary", "First shot uses {0} at {1:0.###} mm from PRODUCT_ZERO -> {2} pulses. Shot distance uses Stitching step {3:0.###} mm -> {4} pulses. Counts/mm={5:0.###}."),
                    ResolveSideLeftCameraOffsetPointCode(),
                    cameraOffsetMm,
                    ResolvedSideLeftMultiShotInitialOffsetPulses,
                    SideLeftVisionProStepMm,
                    ResolvedSideLeftMultiShotStepPulses,
                    countsPerMillimeter);
            }
        }

        public string SideLeftVisionProStitchingSummary
        {
            get
            {
                if (!SideLeftMultiShotEnabled)
                {
                    return T("Tool_MultiShot_StitchingDisabledSummary", "VisionPro stitching parameters are written only when MultiShot is enabled.");
                }

                if (SideLeftVisionProStepMm <= 0.0 || SideLeftVisionProMmPerPixel <= 0.0)
                {
                    return T("Tool_MultiShot_StitchingInvalidSummary", "Set Step mm and mm/pixel greater than zero before production.");
                }

                return string.Format(
                    T("Tool_MultiShot_StitchingSummary", "VisionPro receives expectedFrames={0}, stepMm={1:0.###}, mmPerPixel={2:0.######}."),
                    SideLeftMultiShotShotCount,
                    SideLeftVisionProStepMm,
                    SideLeftVisionProMmPerPixel);
            }
        }

        private void OnMultiShotConfigurationChanged(string reason)
        {
            if (_isApplyingMultiShotEditorProfile)
            {
                OnPropertyChanged(nameof(SideLeftMultiShotConfigurationSummary));
                OnPropertyChanged(nameof(SideLeftVisionProStitchingSummary));
                OnPropertyChanged(nameof(SideLeftDualIlluminationSummary));
                RefreshMultiShotDerivedProperties();
                return;
            }

            if (!_isApplyingMultiShotEditorProfile)
            {
                PersistCurrentMultiShotEditorProfile();
            }

            OnPropertyChanged(nameof(SideLeftMultiShotConfigurationSummary));
            OnPropertyChanged(nameof(SideLeftVisionProStitchingSummary));
            OnPropertyChanged(nameof(SideLeftDualIlluminationSummary));
            RefreshMultiShotDerivedProperties();
            MarkConfigurationDirty();
            QueueRuntimePreviewApply(reason);
        }

        private string _boundHeartbeatOutputSignalCode = "OUT_HEARTBEAT";
        public string BoundHeartbeatOutputSignalCode
        {
            get => _boundHeartbeatOutputSignalCode;
            set
            {
                _boundHeartbeatOutputSignalCode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeartbeatStatusSummary));
                OnPropertyChanged(nameof(HeartbeatTargetSummary));
                OnPropertyChanged(nameof(HeartbeatDashboardSummary));
                RefreshMachineSignalStatuses();
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("heartbeat output binding changed");
                if (IsHeartbeatRunning)
                {
                    RestartHeartbeatAsync("heartbeat output change").SafeFireAndForget(ex => AddLogEntry($"Heartbeat restart error: {ex.Message}", IOLogLevel.Error, "IO", ex));
                }
            }
        }

        private bool _heartbeatEnabled = true;
        public bool HeartbeatEnabled
        {
            get => _heartbeatEnabled;
            set
            {
                _heartbeatEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeartbeatStatusSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("heartbeat enabled changed");
                if (!value && IsHeartbeatRunning)
                {
                    StopHeartbeatAsync("heartbeat disabled by configuration").SafeFireAndForget(ex => AddLogEntry($"Heartbeat stop error: {ex.Message}", IOLogLevel.Error, "IO", ex));
                }
            }
        }

        private bool _heartbeatAutoStartOnRealHardware = true;
        public bool HeartbeatAutoStartOnRealHardware
        {
            get => _heartbeatAutoStartOnRealHardware;
            set
            {
                _heartbeatAutoStartOnRealHardware = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeartbeatStatusSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("heartbeat auto-start changed");
            }
        }

        private int _heartbeatIntervalMs = 500;
        public int HeartbeatIntervalMs
        {
            get => _heartbeatIntervalMs;
            set
            {
                var normalized = Math.Max(100, value);
                if (_heartbeatIntervalMs == normalized)
                {
                    return;
                }

                _heartbeatIntervalMs = normalized;
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Interval = TimeSpan.FromMilliseconds(_heartbeatIntervalMs);
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(HeartbeatStatusSummary));
                MarkConfigurationDirty();
                QueueRuntimePreviewApply("heartbeat interval changed");
            }
        }

        private bool _isHeartbeatRunning;
        public bool IsHeartbeatRunning
        {
            get => _isHeartbeatRunning;
            set
            {
                if (_isHeartbeatRunning == value)
                {
                    return;
                }

                _isHeartbeatRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeartbeatStatusSummary));
                OnPropertyChanged(nameof(HeartbeatIndicatorLabel));
                OnPropertyChanged(nameof(HeartbeatDashboardSummary));
            }
        }

        public bool HeartbeatOutputState
        {
            get => _heartbeatOutputState;
            set
            {
                if (_heartbeatOutputState == value)
                {
                    return;
                }

                _heartbeatOutputState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeartbeatIndicatorLabel));
            }
        }

        public string HeartbeatIndicatorLabel => IsHeartbeatRunning
            ? (HeartbeatOutputState
                ? T("Tool_VM_HeartbeatOn", "HEARTBEAT ON")
                : T("Tool_VM_HeartbeatOff", "HEARTBEAT OFF"))
            : T("Tool_VM_HeartbeatStop", "HEARTBEAT STOP");

        public string HeartbeatTargetSummary
        {
            get
            {
                var signal = ResolveOutputSignalByCode(BoundHeartbeatOutputSignalCode);
                if (signal == null)
                {
                    return T("Tool_VM_HeartbeatSelectOutput", "Select a dedicated diagnostic output for heartbeat blinking.");
                }

                return string.Format(
                    T("Tool_VM_HeartbeatTargetSummary", "{0} on {1} ({2})"),
                    signal.SignalCode,
                    signal.Channel,
                    signal.Board);
            }
        }

        public string HeartbeatStatusSummary
        {
            get
            {
                if (!HeartbeatEnabled)
                {
                    return T("Tool_VM_HeartbeatDisabled", "Heartbeat disabled. No output will toggle automatically.");
                }

                var modeLabel = IsSimulationMode ? "SIM" : "HW";
                var autoStartLabel = HeartbeatAutoStartOnRealHardware
                    ? T("Tool_VM_HeartbeatAutostartEnabled", "HW autostart enabled")
                    : T("Tool_VM_HeartbeatAutostartDisabled", "HW autostart disabled");
                var runtimeLabel = IsHeartbeatRunning
                    ? T("Tool_VM_HeartbeatRunning", "heartbeat running")
                    : T("Tool_VM_HeartbeatStopped", "heartbeat stopped");
                return string.Format(
                    T("Tool_VM_HeartbeatStatusSummary", "{0} | {1} | interval {2} ms | {3}"),
                    runtimeLabel,
                    modeLabel,
                    HeartbeatIntervalMs,
                    autoStartLabel);
            }
        }

        public string RuntimeBindingSummary => string.Format(
            T("Tool_VM_RuntimeBindingSummary", "Photocell={0} | Trigger={1} | Reject={2} | Alarm={3} | Heartbeat={4} | Additional bindings={5}"),
            BoundProductPhotocellSignalCode,
            BoundCameraTriggerSignalCode,
            BoundRejectSignalCode,
            BoundGeneralAlarmSignalCode,
            BoundHeartbeatOutputSignalCode,
            AdditionalRuntimeBindings.Count);

        public string InterventionReferenceSummary => T("Tool_VM_InterventionReferenceSummary", "Standard reference: PRODUCT_ZERO = encoder position acquired on the active edge of the product photocell. If you change the photocell position from machine zero, the points' absolute machine positions change, not their relative product offsets.");

        public string InterventionRuntimeSummary => string.Format(
            T("Tool_VM_InterventionRuntimeSummary", "Active points: {0} | Runtime events recorded: {1}"),
            InterventionPoints.Count(point => point.Enabled),
            InterventionRuntimeEvents.Count);

        public string RejectModeOperatingSummary
        {
            get
            {
                switch (MachineRejectMode)
                {
                    case "Immediate":
                        return T("Tool_VM_RejectModeImmediate", "Immediate = as soon as a product is marked NOK, the reject output is activated immediately without waiting for encoder position.");
                    case "Disabled":
                        return T("Tool_VM_RejectModeDisabled", "Disabled = the NOK product stays tracked and logged, but no physical reject command is issued.");
                    default:
                        return T("Tool_VM_RejectModeEncoderTracked", "EncoderTracked = the product is rejected when it reaches the reject position calculated from photocell + encoder.");
                }
            }
        }

        private bool HasEnabledRejectInterventionPoint => InterventionPoints.Any(point => point.Enabled && string.Equals(point.ActionType, "Reject", StringComparison.OrdinalIgnoreCase));

        private bool _showOnlyRuntimeSignals;
        public bool ShowOnlyRuntimeSignals
        {
            get => _showOnlyRuntimeSignals;
            set { _showOnlyRuntimeSignals = value; OnPropertyChanged(); RefreshSignalViews(); }
        }

        private bool _showOnlyUnassignedSignals;
        public bool ShowOnlyUnassignedSignals
        {
            get => _showOnlyUnassignedSignals;
            set { _showOnlyUnassignedSignals = value; OnPropertyChanged(); RefreshSignalViews(); }
        }

        private bool _showOnlyRealSignals = true;
        public bool ShowOnlyRealSignals
        {
            get => _showOnlyRealSignals;
            set { _showOnlyRealSignals = value; OnPropertyChanged(); RefreshSignalViews(); }
        }

        private int _pollingInterval = 100;
        public int PollingInterval
        {
            get => _pollingInterval;
            set
            {
                if (value != _pollingInterval && value >= 10 && value <= 1000)
                {
                    _pollingInterval = value;
                    OnPropertyChanged();
                    UpdateRefreshTimer();
                }
            }
        }

        public DigitalIOViewModel()
        {
            _interventionExecutionTask = Task.Factory.StartNew(
                ProcessInterventionExecutionQueue,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            MachineInputsView = CollectionViewSource.GetDefaultView(MachineInputs);
            MachineOutputsView = CollectionViewSource.GetDefaultView(MachineOutputs);
            MachineInputsView.Filter = FilterMachineInput;
            MachineOutputsView.Filter = FilterMachineOutput;
            MachineInputs.CollectionChanged += OnMachineSignalsCollectionChanged;
            MachineOutputs.CollectionChanged += OnMachineSignalsCollectionChanged;
            EncoderTemplates.CollectionChanged += OnEncoderTemplatesCollectionChanged;
            AdditionalRuntimeBindings.CollectionChanged += OnAdditionalRuntimeBindingsCollectionChanged;
            InterventionPoints.CollectionChanged += OnInterventionPointsCollectionChanged;

            // Initialize channels
            InitializeChannels();
            InitializeMachineHardwareTemplate();
            RefreshInterventionTimelineItems();
            SelectedInterventionPoint = InterventionPoints.FirstOrDefault();
            RefreshAvailableSignalCodes();
            // Crea manager
            CreateIoManager();
            _machineConfigurationService = new MachineConfigurationService();
            _toolFusionSnapshotService = new ToolFusionSnapshotService(_machineConfigurationService);
            _timedTriggerService = new PhotocellTimedTriggerService();
            _timedTriggerService.EventRaised += OnTimedTriggerServiceEventRaised;
            RefreshConfigurationBackups();
            _machineController = new MachineController(_ioManager);
            _multiShotTriggerController = new MultiShotTriggerController(_ioManager);
            _multiShotTriggerController.EventRaised += OnMultiShotTriggerEventRaised;
            AttachMultiShotSessionPreparationHandler(_multiShotTriggerController);
            // DISABLED: AttachStitchingStatusProvider reads VisionPro COM objects from a
            // ThreadPool thread (Task.Run), which violates STA-apartment rules and causes
            // VisionPro to drop hardware triggers (9th frame not processed → isReady never
            // becomes True → COMPANION_TIMEOUT arrived=[]).  Re-enable only after the
            // read is marshalled back to the VisionPro event thread or WPF Dispatcher.
            // AttachStitchingStatusProvider(_multiShotTriggerController);
            ServiceLocator.MachineRuntimeService.ContinuousRunStateChanged += OnContinuousRunStateChanged;

            // Setup comandi
            SetOutputCommand = new RelayCommand<int>(SetOutput);
            SetAllOutputsHighCommand = new RelayCommand(async  _=> await SetAllOutputsHighAsync());
            SetAllOutputsLowCommand = new RelayCommand(async _ => await SetAllOutputsLowAsync());
            ReadInputsCommand = new RelayCommand(async _ => await ReadInputsAsync());
            ReadEncodersCommand = new RelayCommand(async _ => await ReadEncodersAsync());
            ResetEncodersCommand = new RelayCommand(async _ => await ResetEncodersAsync());
            StartEncodersCommand = new RelayCommand<int>(async (ch) => await StartEncoderAsync(ch));
            StopEncodersCommand = new RelayCommand<int>(async (ch) => await StopEncoderAsync(ch));
            SetEncoderPresetCommand = new RelayCommand<EncoderPresetData>(async (data) =>
                await SetEncoderPresetAsync(data.Channel, data.Preset));
            SaveConfigurationCommand = new RelayCommand(SaveConfiguration);
            LoadConfigurationCommand = new RelayCommand(LoadConfiguration);
            ClearLogCommand = new RelayCommand(ClearLog);
            ApplyConnectionModeCommand = new RelayCommand(async _ => await ReinitializeIoManagerAsync());
            ToggleConnectionModeCommand = new RelayCommand(async _ => await ToggleConnectionModeAsync());
            ApplySimulationEncoderSettingsCommand = new RelayCommand(_ => ApplySimulationEncoderSettings());
            StepSimulationEncoderCommand = new RelayCommand(async _ => await StepSimulationEncoderAsync());
            CaptureCurrentEncoderCalibrationSpeedCommand = new RelayCommand(_ => CaptureCurrentEncoderCalibrationSpeed());
            ApplyEncoderTachometerCalibrationCommand = new RelayCommand(_ => ApplyEncoderTachometerCalibration());
            SaveLogToFileCommand = new RelayCommand(async _ => await SaveLogToFileAsync());
            SimulateProductPassageCommand = new RelayCommand(async _ => await SimulateProductPassageAsync());
            MarkLatestProductAcceptedCommand = new RelayCommand(async _ => await ApplyLatestInspectionResultAsync(true));
            MarkLatestProductRejectedCommand = new RelayCommand(async _ => await ApplyLatestInspectionResultAsync(false));
            ForceTriggerCycleCommand = new RelayCommand(async _ =>
            {
                if (!ConfirmPrivilegedOperation(
                    "Conferma trigger manuale",
                    "Stai per forzare manualmente un ciclo trigger camera. Usare solo in manutenzione o commissioning. Continuare?"))
                {
                    return;
                }

                if (IsTimedTriggerSchedulingActive)
                {
                    QueueTimedPhotocellTriggerBatch(null, _machineController.GetLastMainEncoderCount());
                    AddLogEntry("TimedFromPhotocell manual batch queued from panel command.", IOLogLevel.Warning, "TRIGGER-TIMED");
                    return;
                }

                await PulseMappedOutputAsync(BoundCameraTriggerSignalCode, 40, "manual force");
                AddLogEntry("Internal trigger forced by manual command", IOLogLevel.Warning, "TRACK");
                AddMachineOperationalEvent("Trigger", "FORCED_TRIGGER", "Forced camera trigger", "Internal trigger cycle executed manually from the panel.", "Manual", null, BoundCameraTriggerSignalCode);
            });
            ForceTimedTopTriggerCommand = new RelayCommand(async _ => await ForceTimedOutputAsync("TOP"));
            ForceTimedSideTriggerCommand = new RelayCommand(async _ => await ForceTimedOutputAsync("SIDE"));
            SimulateTimedPhotocellEdgeCommand = new RelayCommand(_ => SimulateTimedPhotocellEdge());
            ResetTimedTriggerDiagnosticsCommand = new RelayCommand(_ => ResetTimedTriggerDiagnostics());
            ClearAlarmOutputCommand = new RelayCommand(async _ => await SetMappedOutputAsync(BoundGeneralAlarmSignalCode, false, "manual alarm reset"));
            OpenConfigurationFolderCommand = new RelayCommand(_ => OpenConfigurationFolder());
            OpenConfigurationFileCommand = new RelayCommand(_ => OpenConfigurationFile());
            CreateConfigurationBackupCommand = new RelayCommand(_ => CreateConfigurationBackup());
            RestoreConfigurationBackupCommand = new RelayCommand(_ => RestoreSelectedConfigurationBackup(), _ => SelectedConfigurationBackup != null);
            RefreshConfigurationBackupsCommand = new RelayCommand(_ => RefreshConfigurationBackups());
            OpenBackupFolderCommand = new RelayCommand(_ => OpenBackupFolder());
            ExportFusionSnapshotCommand = new RelayCommand(_ => ExportFusionSnapshot());
            OpenFusionExportFolderCommand = new RelayCommand(_ => OpenFusionExportFolder());
            ToggleHeartbeatCommand = new RelayCommand(async _ => await ToggleHeartbeatAsync());
            AddInterventionPointCommand = new RelayCommand(AddInterventionPoint);
            DuplicateInterventionPointCommand = new RelayCommand(DuplicateSelectedInterventionPoint, () => SelectedInterventionPoint != null);
            RemoveInterventionPointCommand = new RelayCommand(RemoveSelectedInterventionPoint, () => SelectedInterventionPoint != null);
            ClearMachineEventsCommand = new RelayCommand(ClearMachineEvents);
            _alarmService = ServiceLocator.AlarmService;
            if (_alarmService != null)
            {
                _alarmService.AlarmTriggered += OnEjectionAlarmTriggered;
                _alarmService.AlarmAcknowledged += OnEjectionAlarmAcknowledged;
            }
            SaveEjectionAlarmsCommand = new RelayCommand(async _ => await SaveEjectionAlarmsAsync());
            ReloadEjectionAlarmsCommand = new RelayCommand(async _ => await LoadEjectionAlarmsAsync());
            ResetSelectedEjectionAlarmCommand = new RelayCommand(
                async _ => await ResetSelectedEjectionAlarmAsync(),
                _ => SelectedEjectionAlarm != null);

            // Setup timer
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Tick += async (s, e) => await RefreshIOAsync();
            UpdateRefreshTimer();

            _logTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _logTimer.Tick += (s, e) => CleanOldLogEntries();
            _logTimer.Start();

            _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HeartbeatIntervalMs) };
            _heartbeatTimer.Tick += async (s, e) => await OnHeartbeatTimerTickAsync();
            // 20 ms gives ~1 mm resolution per tick at 3 m/min (50 mm/s x 0.020 s).
            // The 50 ms default exceeded the 2 mm gap between TOP/SIDE triggers at speeds
            // above 2.4 m/min, causing both to fire in the same tick (simultaneous double shot).
            _virtualConveyorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _virtualConveyorTimer.Tick += async (s, e) => await OnVirtualConveyorTimerTickAsync();
            _runtimePreviewApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RuntimePreviewApplyDebounceMs) };
            _runtimePreviewApplyTimer.Tick += async (s, e) => await OnRuntimePreviewApplyTimerTickAsync();
            TrackedProducts.CollectionChanged += OnTrackedProductsCollectionChanged;

            // Subscribe machine controller events
            SubscribeMachineControllerEvents();

            // Initialize runtime
            InitializeAsync().SafeFireAndForget();
        }

        private void OnTrackedProductsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(TrackedProductsSummary));
        }

        private async Task InitializeAsync()
        {
            StatusMessage = T("Tool_VM_StatusInitializingIo", "Initializing I/O devices...");
            AddLogEntry($"Starting standalone I/O tool session. BaseDir={AppDomain.CurrentDomain.BaseDirectory}", IOLogLevel.Info, "STARTUP"); await InitializeIoManagerAsync();
            await InitializeMachineRuntimeAsync();
            await LoadEjectionAlarmsAsync();

            if (_isInitialized)
            {
                StatusMessage = IsSimulationMode
                    ? T("Tool_VM_StatusSimulationActive", "Simulation mode active")
                    : T("Tool_VM_StatusIoInitialized", "I/O devices initialized");

                await RefreshOutputsAsync();
                AddLogEntry("I/O system initialized", IOLogLevel.Success, "STARTUP");
                AddLogEntry($"Active machine configuration: {MachineConfigurationPath}", IOLogLevel.Info, "CONFIG");
                AddLogEntry($"Automatic NLog files: {System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")}", IOLogLevel.Info, "LOG"); _refreshTimer.Start();
            }
            else
            {
                StatusMessage = T("Tool_VM_StatusIoInitError", "Error while initializing I/O devices");
                AddLogEntry("Error while initializing I/O devices", IOLogLevel.Error, "STARTUP");
            }
        }

        private void InitializeChannels()
        {
            // 32 input channels (PCIE-1756: DI00-DI31)
            for (int i = 0; i < Pcie1756ChannelMap.DiChannelCount; i++)
            {
                InputChannels.Add(new IOChannel
                {
                    ChannelNumber = i,
                    ChannelName = $"DI{i:00}",
                    IsActive = false,
                    State = "OFF",
                    Type = IOType.DigitalInput
                });
            }

            // 32 output channels (PCIE-1756: DO00-DO31)
            for (int i = 0; i < Pcie1756ChannelMap.DoChannelCount; i++)
            {
                OutputChannels.Add(new IOChannel
                {
                    ChannelNumber = i,
                    ChannelName = $"DO{i:00}",
                    IsActive = false,
                    State = "OFF",
                    StateText = "OFF",
                    Type = IOType.DigitalOutput
                });
            }

            // 4 encoders
            for (int i = 0; i < 4; i++)
            {
                EncoderChannels.Add(new EncoderChannel
                {
                    ChannelNumber = i,
                    ChannelName = $"ENC{i + 1}",
                    CounterValue = 0,
                    Status = "Ready",
                    Mode = EncoderMode.Quadrature,
                    PresetValue = 0,
                    IsCounting = true
                });
            }
        }

        private void InitializeMachineHardwareTemplate()
        {
            MachineInputs.Add(new MachineSignalDefinition
            {
                SignalCode = "IN_PRODUCT_PHOTOCELL",
                Description = "Photocell detecting product passage",
                Direction = MachineSignalDirection.Input,
                Category = MachineSignalCategory.PresenceSensor,
                Board = "PCIE-1756-BE",
                Channel = "DI00",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Master event for product tracking."
            });

            MachineInputs.Add(new MachineSignalDefinition
            {
                SignalCode = "IN_EXTERNAL_TRIGGER_ENABLE",
                Description = "External trigger mode enable or PLC consent",
                Direction = MachineSignalDirection.Input,
                Category = MachineSignalCategory.CameraTrigger,
                Board = "PCIE-1756-BE",
                Channel = "DI01",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Used when camera shot is commanded by an external controller."
            });

            MachineInputs.Add(new MachineSignalDefinition
            {
                SignalCode = "IN_REJECT_OPEN_FB",
                Description = "Reject gate open feedback",
                Direction = MachineSignalDirection.Input,
                Category = MachineSignalCategory.Reject,
                Board = "PCIE-1756-BE",
                Channel = "DI02",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Optional for bi-stable reject devices."
            });

            MachineInputs.Add(new MachineSignalDefinition
            {
                SignalCode = "IN_REJECT_CLOSED_FB",
                Description = "Reject gate closed feedback",
                Direction = MachineSignalDirection.Input,
                Category = MachineSignalCategory.Reject,
                Board = "PCIE-1756-BE",
                Channel = "DI03",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Optional for bi-stable reject devices."
            });

            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = "OUT_CAMERA_SIDE_TRIGGER",
                Description = "Camera Side hardware trigger pulse",
                Direction = MachineSignalDirection.Output,
                Category = MachineSignalCategory.CameraTrigger,
                Board = "PCIE-1756-BE",
                Channel = "DO01",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Real trigger output for side camera and related illuminator enable chain."
            });

            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = "OUT_CAMERA_TOP_TRIGGER",
                Description = "Camera Top hardware trigger pulse",
                Direction = MachineSignalDirection.Output,
                Category = MachineSignalCategory.CameraTrigger,
                Board = "PCIE-1756-BE",
                Channel = "DO02",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Real trigger output for top camera and related illuminator enable chain."
            });

            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = "OUT_REJECT_SOLENOID",
                Description = "Reject actuator command",
                Direction = MachineSignalDirection.Output,
                Category = MachineSignalCategory.Reject,
                Board = "PCIE-1756-BE",
                Channel = "DO03",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Mono actuator version. Use open/close pair when needed."
            });

            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = "OUT_GENERAL_ALARM",
                Description = "General fault output to stack light or PLC",
                Direction = MachineSignalDirection.Output,
                Category = MachineSignalCategory.Alarm,
                Board = "PCIE-1756-BE",
                Channel = "DO06",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Dedicated to blocking machine alarms."
            });

            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = "OUT_HEARTBEAT",
                Description = "Diagnostic heartbeat output for commissioning and board communication test",
                Direction = MachineSignalDirection.Output,
                Category = MachineSignalCategory.Alarm,
                Board = "PCIE-1756-BE",
                Channel = "DO07",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = false,
                Notes = "Dedicated blinking output for diagnostics. Prefer a spare channel or commissioning lamp."
            });

            EncoderTemplates.Add(new EncoderConfigurationTemplate
            {
                AxisName = "ENC_CONVEYOR_MAIN",
                Board = "PCIE-1884-AE",
                Channel = "Counter0",
                PulsesPerRevolution = 2048,
                MillimetersPerRevolution = 100.0,
                MachineZeroLabel = "INIZIO_TRASPORTO",
                PhotocellMachineOffsetMm = 35.5,
                TrackingMode = "Quadrature x4",
                TriggerOffsetMm = 0.0,
                RejectOffsetMm = 350.0,
                Notes = "Main conveyor reference. In virtual-conveyor mode product tracking starts from the photocell at 35.5 mm from machine zero."
            });

            InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_TOP", 74.0, "TriggerCamera", "OUT_CAMERA_TOP_TRIGGER", 40, true));
            InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_SIDE", 76.0, "TriggerCamera", "OUT_CAMERA_SIDE_TRIGGER", 40, true));
            InterventionPoints.Add(CreateStandardInterventionPoint("REJECT", 350.0, "Reject", "OUT_REJECT_SOLENOID", 80, true));

            MachineSequence.Add(new MachineSequenceStep
            {
                StepOrder = 10,
                EventName = "Product detect",
                Source = "IN_PRODUCT_PHOTOCELL",
                Action = "Create tracked product and latch encoder quota",
                Target = "Product tracking buffer",
                Timing = "Rising edge",
                Notes = "Start of the machine cycle."
            });

            MachineSequence.Add(new MachineSequenceStep
            {
                StepOrder = 20,
                EventName = "Top camera trigger",
                Source = "Tracking controller",
                Action = "Pulse top camera trigger output",
                Target = "OUT_CAMERA_TOP_TRIGGER",
                Timing = "At configured top camera machine position",
                Notes = "The trigger output can also enable the related illuminator through external wiring."
            });

            MachineSequence.Add(new MachineSequenceStep
            {
                StepOrder = 30,
                EventName = "Side camera trigger",
                Source = "Internal or external trigger mode",
                Action = "Pulse side camera trigger output",
                Target = "OUT_CAMERA_SIDE_TRIGGER",
                Timing = "At configured side camera machine position",
                Notes = "The trigger output can also enable the related illuminator through external wiring."
            });

            MachineSequence.Add(new MachineSequenceStep
            {
                StepOrder = 40,
                EventName = "Inspection result",
                Source = "Cognex result pair",
                Action = "Mark product OK or NOK",
                Target = "Tracked product record",
                Timing = "After image processing",
                Notes = "Result must stay linked to product ID."
            });

            MachineSequence.Add(new MachineSequenceStep
            {
                StepOrder = 50,
                EventName = "Reject execution",
                Source = "Tracked product reaching reject position",
                Action = "Command reject actuator only for NOK items",
                Target = "OUT_REJECT_SOLENOID",
                Timing = "At reject encoder quota",
                Notes = "Preferred industrial mode over fixed delay."
            });

            MachineSequence.Add(new MachineSequenceStep
            {
                StepOrder = 60,
                EventName = "Alarm escalation",
                Source = "Alarm rules",
                Action = "Raise warning or blocking output",
                Target = "OUT_GENERAL_ALARM",
                Timing = "On threshold reached",
                Notes = "To be linked later with runtime alarm services."
            });
        }


        private async Task RefreshIOAsync()
        {
            if (!_isInitialized || IsRefreshing) return;

            IsRefreshing = true;
            try
            {
                // Leggi input
                await _ioManager.UpdateAllInputsAsync(InputChannels);
                RefreshMachineDashboardProperties();

                // In hardware mode we rely on manager polling + CounterChanged events.
                // Reading encoders again from the UI timer can fight with the controller state.
                if (IsSimulationMode)
                {
                    await _ioManager.UpdateAllEncodersAsync(EncoderChannels);
                }
                UpdateEncoderMetrics();

                // Aggiorna stato
                StatusMessage = string.Format(
                    T("Tool_VM_StatusLastRefresh", "Last refresh: {0}"),
                    DateTime.Now.ToString("HH:mm:ss.fff"));
            }
            catch (Exception ex)
            {
                AddLogEntry($"I/O refresh error: {ex.Message}", IOLogLevel.Error, "IO", ex);
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private void SetOutput(int channelNumber)
        {
            var channel = OutputChannels.FirstOrDefault(c => c.ChannelNumber == channelNumber);
            if (channel == null)
            {
                return;
            }

            WriteSingleOutputAsync(channelNumber, channel.IsActive).SafeFireAndForget();
        }

        private async Task WriteSingleOutputAsync(int channelNumber, bool value)
        {
            try
            {
                await _ioManager.WriteOutputAsync(channelNumber, value);
                ApplyOutputState(channelNumber, value);
                AddLogEntry($"Output {channelNumber} set to {(value ? "HIGH" : "LOW")}", IOLogLevel.Info, "IO");
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error while setting output {channelNumber}: {ex.Message}", IOLogLevel.Error, "IO", ex);
            }
        }

        private async Task SetAllOutputsHighAsync()
        {
            if (!ConfirmPrivilegedOperation(
                "Conferma uscita generale",
                "Stai per forzare TUTTE le uscite digitali a HIGH. Eseguire solo in manutenzione e con macchina in sicurezza. Continuare?"))
            {
                return;
            }

            await _ioManager.WriteOutputsAsync(Enumerable.Repeat((byte)0xFF, 8).ToArray());
            await RefreshOutputsAsync();
            AddLogEntry("All outputs set to HIGH", IOLogLevel.Info, "IO");
        }

        private async Task SetAllOutputsLowAsync()
        {
            if (!ConfirmPrivilegedOperation(
                "Conferma reset uscite",
                "Stai per forzare TUTTE le uscite digitali a LOW. Continuare?"))
            {
                return;
            }

            await _ioManager.WriteOutputsAsync(new byte[8]);
            await RefreshOutputsAsync();
            AddLogEntry("All outputs set to LOW", IOLogLevel.Info, "IO");
        }

        private async Task RefreshOutputsAsync()
        {
            var values = await _ioManager.ReadAllOutputsAsync();
            await Application.Current.Dispatcher.InvokeAsync(() => ApplyOutputStates(values));
        }

        private void ApplyOutputStates(byte[] values)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < Math.Min(OutputChannels.Count, Pcie1756ChannelMap.DoChannelCount); i++)
            {
                int port = i / 8;
                int bit = i % 8;
                bool isActive = port < values.Length && (values[port] & (1 << bit)) != 0;
                ApplyOutputState(i, isActive);
            }
        }

        private void ApplyOutputState(int channelNumber, bool isActive)
        {
            var channel = OutputChannels.FirstOrDefault(c => c.ChannelNumber == channelNumber);
            if (channel == null)
            {
                return;
            }

            channel.IsActive = isActive;
            channel.State = isActive ? "ON" : "OFF";
            channel.StateText = isActive ? "ON" : "OFF";
            RefreshMachineDashboardProperties();
        }

        private async Task ReadInputsAsync()
        {
            await _ioManager.UpdateAllInputsAsync(InputChannels);
            RefreshMachineDashboardProperties();
            AddLogEntry("Inputs read manually", IOLogLevel.Info, "IO");
        }

        private async Task ReadEncodersAsync()
        {
            await _ioManager.UpdateAllEncodersAsync(EncoderChannels);
            UpdateEncoderMetrics();
            AddLogEntry("Encoders read manually", IOLogLevel.Info, "ENC");
        }

        private async Task ResetEncodersAsync()
        {
            for (int i = 0; i < EncoderChannels.Count; i++)
            {
                await _ioManager.ResetEncoderAsync(i);
                EncoderChannels[i].CounterValue = 0;
                EncoderChannels[i].Status = "Reset";
                EncoderChannels[i].SpeedMetersPerMinute = 0.0;
                EncoderChannels[i].RawSpeedMetersPerMinute = 0.0;
                EncoderChannels[i].LastDeltaCounts = 0;
                EncoderChannels[i].RawPulseFrequencyHz = 0.0;
                EncoderChannels[i].LastSampleMilliseconds = 0.0;
                EncoderChannels[i].DiagnosticState = "Reset";
                OnPropertyChanged(nameof(MainEncoderSpeedSummary));
                _lastEncoderCounts[i] = 0;
                _lastEncoderSampleTimes[i] = DateTime.MinValue;
                _filteredEncoderSpeedMetersPerMinute[i] = 0.0;
                _encoderMetricReferenceReady[i] = false;
                _encoderMetricWarmupUntilUtc[i] = DateTime.UtcNow.AddSeconds(EncoderStartupWarmupSeconds);
            }

            _virtualConveyorCount = 0;
            _virtualConveyorFractionalCounts = 0.0;
            _lastVirtualConveyorTickUtc = DateTime.UtcNow;
            ClearTriggerDiagnostics();
            AddLogEntry("All encoders reset", IOLogLevel.Info, "ENC");
        }

        private async Task StartEncoderAsync(int channel)
        {
            await _ioManager.StartEncoderAsync(channel);
            EncoderChannels[channel].IsCounting = true;
            EncoderChannels[channel].DiagnosticState = T("Tool_VM_EncoderWaitingFirstSample", "Waiting for first sample");
            _lastEncoderSampleTimes[channel] = DateTime.MinValue;
            AddLogEntry($"Encoder {channel} started", IOLogLevel.Info, "ENC");
        }

        private async Task StopEncoderAsync(int channel)
        {
            await _ioManager.StopEncoderAsync(channel);
            EncoderChannels[channel].IsCounting = false;
            AddLogEntry($"Encoder {channel} stopped", IOLogLevel.Info, "ENC");
        }

        private async Task SetEncoderPresetAsync(int channel, long preset)
        {
            await _ioManager.SetEncoderPresetAsync(channel, preset);
            EncoderChannels[channel].PresetValue = (int)preset;
            AddLogEntry($"Encoder preset {channel} set to {preset}", IOLogLevel.Info, "ENC");
        }

        private void MarkConfigurationDirty()
        {
            if (_suppressConfigurationDirtyTracking)
            {
                return;
            }

            IsConfigurationDirty = true;
        }

        private void QueueRuntimePreviewApply(string reason)
        {
            if (!_isInitialized || _suppressConfigurationDirtyTracking)
            {
                return;
            }

            RebuildTimedTriggerFastPathSnapshot();

            if (HasPendingConfigurationEdits())
            {
                _pendingRuntimePreviewReason = reason;
                AddLogEntry($"Runtime preview deferred while a configuration grid is still editing rows: {reason}", IOLogLevel.Info, "CONFIG");
                return;
            }

            _runtimePreviewApplyTimer.Stop();
            _pendingRuntimePreviewReason = reason;
            _runtimePreviewApplyTimer.Start();
        }

        private async Task OnRuntimePreviewApplyTimerTickAsync()
        {
            _runtimePreviewApplyTimer.Stop();
            var reason = string.IsNullOrWhiteSpace(_pendingRuntimePreviewReason)
                ? "machine configuration edited"
                : _pendingRuntimePreviewReason;
            _pendingRuntimePreviewReason = null;
            await ApplyRuntimeConfigurationPreviewAsync(reason);
        }

        private async Task ApplyRuntimeConfigurationPreviewAsync(string reason)
        {
            if (_isApplyingRuntimePreview || !_isInitialized || _suppressConfigurationDirtyTracking)
            {
                return;
            }

            if (HasPendingConfigurationEdits())
            {
                _pendingRuntimePreviewReason = reason;
                _runtimePreviewApplyTimer.Stop();
                _runtimePreviewApplyTimer.Start();
                AddLogEntry($"Runtime preview postponed because a configuration grid is still in edit mode: {reason}", IOLogLevel.Info, "CONFIG");
                return;
            }

            _isApplyingRuntimePreview = true;
            try
            {
                CancelPendingTimedTriggerBatches($"runtime preview apply: {reason}");
                _machineConfiguration = BuildRuntimeConfigurationFromView();
                await ConfigureEffectiveRuntimeAsync(
                    _machineConfiguration,
                    MainWindow.ConfigRecipeParam?.Config,
                    $"runtime preview: {reason}");
                MainWindow.ApplyVisionProStitchingParametersForLeftJobs(
                    $"{nameof(DigitalIOViewModel)} runtime preview: {reason}",
                    _machineConfiguration);
                _machineController.ClearActiveProducts();
                ClearTriggerDiagnostics();
                ClearTimedTriggerDiagnostics();
                ValidateCameraTriggerOutputMappings();
                RebuildTimedTriggerFastPathSnapshot();
                await ParkUnusedSpareOutputsLowAsync(reason);
                await EvaluateHeartbeatAutoStartAsync(reason);
                MachineControllerStatus = $"{_machineController.Status} | live preview {DateTime.Now:HH:mm:ss}";
                AddLogEntry($"Machine runtime preview reapplied after edit: {reason}. Active products cleared to avoid stale quotas.", IOLogLevel.Info, "CONFIG");
            }
            catch (Exception ex)
            {
                AddLogEntry($"Runtime preview apply error: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
            }
            finally
            {
                _isApplyingRuntimePreview = false;
            }
        }

        /// <summary>
        /// Rebuilds tracking and MultiShot from the global machine configuration plus
        /// the newly active recipe. The global configuration object remains unchanged.
        /// </summary>
        public async Task<MachineRuntimeConfiguration> ApplyActiveRecipeRuntimeAdjustmentsAsync(
            QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters.RecipeData recipe,
            string source)
        {
            MachineRuntimeConfiguration machineConfiguration =
                _machineConfiguration ?? _machineConfigurationService.Load();
            if (machineConfiguration == null)
            {
                AddLogEntry(
                    $"Recipe runtime corrections skipped because machine configuration is unavailable. Source={source}",
                    IOLogLevel.Warning,
                    "RECIPE");
                return null;
            }

            RecipeMachineRuntimeResolution resolution = await ConfigureEffectiveRuntimeAsync(
                machineConfiguration,
                recipe,
                source ?? "active recipe change");

            _machineController.ClearActiveProducts();
            ClearTriggerDiagnostics();
            ClearTimedTriggerDiagnostics();
            RebuildTimedTriggerFastPathSnapshot();
            ValidateCameraTriggerOutputMappings();

            string recipeName = recipe?.general_Info?.RecipeName ?? "-";
            string appliedSummary = resolution.AppliedAdjustments.Count == 0
                ? "machine defaults"
                : string.Join("; ", resolution.AppliedAdjustments);
            AddLogEntry(
                $"Active recipe machine corrections applied. Recipe={recipeName}; Source={source}; {appliedSummary}",
                resolution.IsValid ? IOLogLevel.Success : IOLogLevel.Warning,
                "RECIPE");

            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                resolution.IsValid ? LogLevel.Info : LogLevel.Warn,
                resolution.IsValid ? "RECIPE_MACHINE_RUNTIME_APPLIED" : "RECIPE_MACHINE_RUNTIME_APPLIED_WITH_FALLBACK",
                "Recipe",
                resolution.IsValid
                    ? "Recipe camera and MultiShot corrections applied"
                    : "Recipe camera and MultiShot corrections applied with safe fallback",
                nameof(DigitalIOViewModel),
                $"Recipe={recipeName}; Source={source}; Applied={appliedSummary}; Errors={string.Join(" | ", resolution.Errors)}");

            return resolution.Configuration;
        }

        public async Task ApplyActiveVppCameraRolesAsync(IEnumerable<string> cameraRoles, string source)
        {
            string[] roles = (cameraRoles ?? Enumerable.Empty<string>())
                .Select(ActiveVppCameraTriggerGate.NormalizeRole)
                .Where(role => role != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Volatile.Write(ref _activeVppCameraRoles, roles);
            CancelPendingTimedTriggerBatches($"VPP camera roles changed: {source}");

            MachineRuntimeConfiguration machineConfiguration =
                _machineConfiguration ?? _machineConfigurationService.Load();
            if (machineConfiguration == null)
            {
                AddLogEntry($"VPP_CAMERA_TRIGGER_GATE_PENDING|source={source}|roles={string.Join(",", roles)}|reason=no-machine-config",
                    IOLogLevel.Warning, "CONFIG");
                return;
            }

            await ConfigureEffectiveRuntimeAsync(
                machineConfiguration, MainWindow.ConfigRecipeParam?.Config, source);
            _machineController.ClearActiveProducts();
            ClearTriggerDiagnostics();
            ClearTimedTriggerDiagnostics();
            RebuildTimedTriggerFastPathSnapshot();
            AddLogEntry($"VPP_CAMERA_TRIGGER_GATE_APPLIED|source={source}|roles={string.Join(",", roles)}",
                IOLogLevel.Info, "CONFIG");
        }

        private async Task<RecipeMachineRuntimeResolution> ConfigureEffectiveRuntimeAsync(
            MachineRuntimeConfiguration machineConfiguration,
            QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters.RecipeData recipe,
            string source)
        {
            await _recipeRuntimeConfigurationLock.WaitAsync();
            try
            {
                RecipeMachineRuntimeResolution resolution =
                    RecipeMachineRuntimeResolver.Resolve(machineConfiguration, recipe);
                MachineRuntimeConfiguration effective = resolution.Configuration ?? machineConfiguration;

                string[] activeRoles = Volatile.Read(ref _activeVppCameraRoles);
                IReadOnlyList<string> masked = ActiveVppCameraTriggerGate.Apply(effective, activeRoles);
                foreach (string entry in masked)
                {
                    AddLogEntry($"VPP_CAMERA_TRIGGER_MASKED|source={source}|{entry}", IOLogLevel.Warning, "CONFIG");
                }

                _effectiveRuntimeConfiguration = effective;
                await _machineController.InitializeAsync(effective);
                await RefreshCachedExternalTriggerStateAsync();
                _multiShotTriggerController?.Configure(
                    effective,
                    () => ServiceLocator.MachineRuntimeService.IsContinuousRunActive);

                if (resolution.Errors.Count > 0)
                {
                    AddLogEntry(
                        $"Recipe runtime correction validation failed; safe values are active. Source={source}; {string.Join(" | ", resolution.Errors)}",
                        IOLogLevel.Error,
                        "RECIPE");
                }

                if (resolution.Warnings.Count > 0)
                {
                    AddLogEntry(
                        $"Recipe runtime correction warning. Source={source}; {string.Join(" | ", resolution.Warnings)}",
                        IOLogLevel.Warning,
                        "RECIPE");
                }

                return resolution;
            }
            finally
            {
                _recipeRuntimeConfigurationLock.Release();
            }
        }

        private void MarkConfigurationSaved()
        {
            IsConfigurationDirty = false;
            _lastConfigurationSaveLocalTime = DateTime.Now;
            OnPropertyChanged(nameof(LastConfigurationSaveSummary));
            OnPropertyChanged(nameof(ConfigurationActiveFileSummary));
        }

        private void RefreshConfigurationBackups()
        {
            ConfigurationBackups.Clear();
            foreach (var backup in _machineConfigurationService.GetAvailableBackups())
            {
                ConfigurationBackups.Add(backup);
            }

            if (SelectedConfigurationBackup != null)
            {
                SelectedConfigurationBackup = ConfigurationBackups.FirstOrDefault(item => string.Equals(item.FilePath, SelectedConfigurationBackup.FilePath, StringComparison.OrdinalIgnoreCase))
                    ?? ConfigurationBackups.FirstOrDefault();
            }
            else
            {
                SelectedConfigurationBackup = ConfigurationBackups.FirstOrDefault();
            }

            OnPropertyChanged(nameof(ConfigurationBackupSummary));
            OnPropertyChanged(nameof(ConfigurationBackupFolderPath));
        }

        private void OpenConfigurationFile()
        {
            try
            {
                var filePath = MachineConfigurationPath;
                var folder = ConfigurationFolderPath;

                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                if (!System.IO.File.Exists(filePath))
                {
                    _machineConfiguration = BuildRuntimeConfigurationFromView();
                    _machineConfigurationService.Save(_machineConfiguration);
                    MarkConfigurationSaved();
                    _lastConfigurationSaveLocalTime = DateTime.Now;
                    OnPropertyChanged(nameof(LastConfigurationSaveSummary));
                }

                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error opening config file: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
            }
        }

        private void OpenConfigurationFolder()
        {
            try
            {
                var folder = ConfigurationFolderPath;
                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error opening config folder: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
            }
        }

        private void OpenBackupFolder()
        {
            try
            {
                var folder = ConfigurationBackupFolderPath;
                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error opening backup folder: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
            }
        }

        private void OpenFusionExportFolder()
        {
            try
            {
                var folder = FusionExportFolderPath;
                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error opening fusion export folder: {ex.Message}", IOLogLevel.Error, "EXPORT", ex);
            }
        }

        private void ExportFusionSnapshot()
        {
            try
            {
                var snapshot = BuildFusionSnapshot();
                var filePath = _toolFusionSnapshotService.ExportSnapshot(snapshot);
                _lastFusionExportLocalTime = DateTime.Now;
                OnPropertyChanged(nameof(LastFusionExportSummary));
                AddLogEntry($"Fusion snapshot exported: {System.IO.Path.GetFileName(filePath)}", IOLogLevel.Success, "EXPORT");
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error exporting fusion snapshot: {ex.Message}", IOLogLevel.Error, "EXPORT", ex);
            }
        }

        private void CommitPendingSignalEdits()
        {
            CommitEditableView(MachineInputsView as IEditableCollectionView);
            CommitEditableView(MachineOutputsView as IEditableCollectionView);
            // InterventionPoints and AdditionalRuntimeBindings have no named view;
            // get the default CollectionView created by the DataGrid binding.
            CommitEditableView(CollectionViewSource.GetDefaultView(InterventionPoints) as IEditableCollectionView);
            CommitEditableView(CollectionViewSource.GetDefaultView(AdditionalRuntimeBindings) as IEditableCollectionView);
        }

        private static void CommitEditableView(IEditableCollectionView view)
        {
            if (view?.IsEditingItem == true) view.CommitEdit();
            if (view?.IsAddingNew == true) view.CommitNew();
        }

        private bool HasPendingConfigurationEdits()
        {
            return IsEditableViewBusy(MachineInputsView as IEditableCollectionView) ||
                   IsEditableViewBusy(MachineOutputsView as IEditableCollectionView) ||
                   IsEditableViewBusy(CollectionViewSource.GetDefaultView(InterventionPoints) as IEditableCollectionView) ||
                   IsEditableViewBusy(CollectionViewSource.GetDefaultView(AdditionalRuntimeBindings) as IEditableCollectionView);
        }

        private static bool IsEditableViewBusy(IEditableCollectionView view)
        {
            return view?.IsEditingItem == true || view?.IsAddingNew == true;
        }

        private void SaveConfiguration(object parameter)
        {
            try
            {
                // Commit any DataGrid row that is in AddNew or EditItem mode.
                // Without this, CollectionView.Refresh() later in this method throws
                // InvalidOperationException while a transaction is open (e.g. the user
                // clicked the empty "new row" at the bottom of the outputs table).
                CommitPendingSignalEdits();
                PrepareMachineOutputDefinitionsForSave();
                PersistCurrentMultiShotEditorProfile();
                if (!TryValidateMultiShotProfiles(out string multiShotValidationError))
                {
                    AddLogEntry(multiShotValidationError, IOLogLevel.Warning, "MULTISHOT");
                    new SystemNotificationWindow(
                        T("Tool_MultiShot_ValidationTitle", "Invalid MultiShot configuration"),
                        string.Format(
                            T("Tool_MultiShot_ValidationMessage", "Correct the following MultiShot and I/O values before saving:\n\n{0}"),
                            multiShotValidationError),
                        NotificationSeverity.Warning).ShowDialog();
                    return;
                }

                _runtimePreviewApplyTimer.Stop();
                CancelPendingTimedTriggerBatches("configuration save");

                // Valori precedenti = ultimo config scritto su XML (indipendente dal timer di preview
                // che aggiorna _machineConfiguration in tempo reale con i valori correnti dell'editor).
                var previousMultiShot = _lastPersistedMultiShotConfig;

                _machineConfiguration = BuildRuntimeConfigurationFromView();
                _machineConfigurationService.Save(_machineConfiguration);
                VerifyMachineOutputsPersisted(_machineConfiguration);
                _lastPersistedMultiShotConfig = _machineConfiguration.MachineMultiShotTrigger;
                _machineConfigurationService.EnsureLatestRuntimeBindingsSchema(_machineConfiguration, _machineConfigurationService.GetTemplateConfigurationFilePath());
                ConfigureEffectiveRuntimeAsync(
                    _machineConfiguration,
                    MainWindow.ConfigRecipeParam?.Config,
                    "configuration save").GetAwaiter().GetResult();
                MainWindow.ApplyVisionProStitchingParametersForLeftJobs(
                    $"{nameof(DigitalIOViewModel)} configuration save",
                    _machineConfiguration);
                // Clear stale tracked products so their old encoder targets do not fire
                // at the previous positions after the user changes intervention offsets.
                _machineController.ClearActiveProducts();
                ClearTriggerDiagnostics();
                ClearTimedTriggerDiagnostics();
                ValidateCameraTriggerOutputMappings();
                ValidateTriggerExecutionConfiguration();
                ParkUnusedSpareOutputsLowAsync("configuration save").GetAwaiter().GetResult();
                EvaluateHeartbeatAutoStartAsync("configuration save").GetAwaiter().GetResult();
                MachineControllerStatus = _machineController.Status;
                RefreshMachineSignalStatuses();
                MarkConfigurationSaved();
                RefreshConfigurationBackups();
                _ = ServiceLocator.AuditLogService?.LogAsync(
                    "MACHINE_CONFIG_SAVE", UserSession.CurrentUser,
                    $"outputs={MachineOutputs.Count}|inputs={MachineInputs.Count}|encoders={EncoderTemplates.Count}");

                if (previousMultiShot != null)
                {
                    PersistMultiShotHistoryToDbAsync(previousMultiShot, UserSession.CurrentUser)
                        .SafeFireAndForget(ex => Logger?.Warn(ex, "MULTISHOT_HISTORY_SAVE_FAILED"));
                    RefreshMultiShotPreviousSnapshots(previousMultiShot);
                }
                MachineControllerStatus = $"Configuration saved {DateTime.Now:HH:mm:ss}";
                AddLogEntry($"I/O configuration saved and verified at {_machineConfigurationService.GetConfigurationFilePath()}. Inputs={MachineInputs.Count}, Outputs={MachineOutputs.Count}, VisibleOutputs={MachineOutputsView.Cast<object>().Count()}, Encoders={EncoderTemplates.Count}, AdditionalBindings={AdditionalRuntimeBindings.Count}", IOLogLevel.Success, "CONFIG");
                ArmConfiguredEncoderChannelsAsync("configuration save").SafeFireAndForget(
                    ex => AddLogEntry($"Automatic encoder start failed after configuration save: {ex.Message}", IOLogLevel.Error, "ENC", ex));
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error while saving configuration: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
                new SystemNotificationWindow(
                    T("Tool_Config_SaveFailedTitle", "Machine configuration not saved"),
                    string.Format(
                        T("Tool_Config_SaveFailedMessage", "The configuration could not be saved and verified. No successful save is confirmed.\n\n{0}"),
                        ex.Message),
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private bool TryValidateMultiShotProfiles(out string errorMessage)
        {
            var errors = new List<string>();
            foreach (var profile in MultiShotProfiles)
            {
                var options = ResolveMultiShotProfileOption(profile.Key);
                string label = profile.Label ?? profile.Key;
                if (options?.Enabled == true)
                {
                    string outputName = NormalizeSignalCode(options.TriggerOutputName);
                    var exactMappings = MachineOutputs
                        .Where(signal => string.Equals(
                            NormalizeSignalCode(signal?.SignalCode),
                            outputName,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (exactMappings.Count > 1)
                    {
                        errors.Add(string.Format(
                            T("Tool_MultiShot_OutputDuplicate", "{0}: trigger output '{1}' is defined {2} times. Keep one physical output row only."),
                            label,
                            outputName,
                            exactMappings.Count));
                    }

                    var validMapping = exactMappings.FirstOrDefault(signal =>
                        MachineRuntimeIo.IsMappedPhysicalOutput(signal, MachineSignalCategory.CameraTrigger));
                    if (validMapping == null)
                    {
                        errors.Add(string.Format(
                            T("Tool_MultiShot_OutputInvalid", "{0}: trigger output '{1}' must be an Output / CameraTrigger row with a valid DO channel."),
                            label,
                            string.IsNullOrWhiteSpace(outputName) ? "-" : outputName));
                    }
                }

                var dual = options?.DualIllumination;
                if (dual?.Enabled != true)
                {
                    continue;
                }

                if (options.Enabled != true)
                {
                    errors.Add(string.Format(
                        T("Tool_MultiShot_DualValidationRequiresMultiShot", "{0}: MultiShot must be enabled."),
                        label));
                }

                if (options.ShotCount < 2 || options.ShotCount % 2 != 0)
                {
                    errors.Add(string.Format(
                        T("Tool_MultiShot_DualValidationEvenShots", "{0}: shot count must be even and at least 2."),
                        label));
                }

                if (dual.ExposureTimeUs <= 0.0 || double.IsNaN(dual.ExposureTimeUs) || double.IsInfinity(dual.ExposureTimeUs))
                {
                    errors.Add(string.Format(
                        T("Tool_MultiShot_DualValidationExposure", "{0}: exposure must be greater than 0 us."),
                        label));
                }

                bool frontLineSupported = DualIlluminationOutputLines.Any(line =>
                    string.Equals(line, dual.FrontOutputLine, StringComparison.OrdinalIgnoreCase));
                bool backLineSupported = DualIlluminationOutputLines.Any(line =>
                    string.Equals(line, dual.BackOutputLine, StringComparison.OrdinalIgnoreCase));
                if (!frontLineSupported || !backLineSupported ||
                    string.Equals(dual.FrontOutputLine, dual.BackOutputLine, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(string.Format(
                        T("Tool_MultiShot_DualValidationLines", "{0}: Front and Backlight must use different outputs (Line3 and Line4)."),
                        label));
                }

                if (!DualIlluminationOutputSources.Any(source =>
                        string.Equals(source, dual.ActiveOutputSource, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add(string.Format(
                        T("Tool_MultiShot_DualValidationSource", "{0}: unsupported active output source '{1}'."),
                        label,
                        dual.ActiveOutputSource));
                }
            }

            errorMessage = string.Join(Environment.NewLine, errors);
            return errors.Count == 0;
        }

        private void LoadConfiguration(object parameter)
        {
            try
            {
                _runtimePreviewApplyTimer.Stop();
                CancelPendingTimedTriggerBatches("configuration load");
                var loadedConfiguration = _machineConfigurationService.Load();
                if (loadedConfiguration == null)
                {
                    AddLogEntry("Machine configuration not found", IOLogLevel.Warning, "CONFIG");
                    return;
                }

                _machineConfiguration = loadedConfiguration;
                _lastPersistedMultiShotConfig = loadedConfiguration.MachineMultiShotTrigger;
                _suppressConfigurationDirtyTracking = true;
                ApplyRuntimeConfigurationToView(_machineConfiguration);
                if (!InterventionPoints.Any())
                {
                    EnsureDefaultInterventionPoints();
                }
                _machineConfiguration = BuildRuntimeConfigurationFromView();
                var configurationSchemaUpdated = _machineConfigurationService.EnsureLatestRuntimeBindingsSchema(_machineConfiguration);
                if (configurationSchemaUpdated)
                {
                    AddLogEntry("Machine runtime configuration upgraded with timed-trigger runtime bindings.", IOLogLevel.Info, "CONFIG");
                }

                _machineConfigurationService.EnsureLatestRuntimeBindingsSchema(_machineConfiguration, _machineConfigurationService.GetTemplateConfigurationFilePath());
                _suppressConfigurationDirtyTracking = false;
                MarkConfigurationSaved();
                ApplySimulationEncoderSettings();
                ConfigureEffectiveRuntimeAsync(
                    _machineConfiguration,
                    MainWindow.ConfigRecipeParam?.Config,
                    "configuration load").GetAwaiter().GetResult();
                MainWindow.ApplyVisionProStitchingParametersForLeftJobs(
                    $"{nameof(DigitalIOViewModel)} configuration load",
                    _machineConfiguration);
                ClearTriggerDiagnostics();
                ClearTimedTriggerDiagnostics();
                ValidateCameraTriggerOutputMappings();
                ValidateTriggerExecutionConfiguration();
                ParkUnusedSpareOutputsLowAsync("configuration load").GetAwaiter().GetResult();
                EvaluateHeartbeatAutoStartAsync("configuration load").GetAwaiter().GetResult();
                MachineControllerStatus = _machineController.Status;
                RefreshMachineSignalStatuses();
                RefreshConfigurationBackups();
                AddLogEntry($"I/O configuration loaded and applied. Inputs={MachineInputs.Count}, Outputs={MachineOutputs.Count}, Encoders={EncoderTemplates.Count}, AdditionalBindings={AdditionalRuntimeBindings.Count}", IOLogLevel.Success, "CONFIG");
                ArmConfiguredEncoderChannelsAsync("configuration load").SafeFireAndForget(
                    ex => AddLogEntry($"Automatic encoder start failed after configuration load: {ex.Message}", IOLogLevel.Error, "ENC", ex));
                LoadMultiShotHistoryFromDbAsync().SafeFireAndForget(
                    ex => Logger?.Warn(ex, "MULTISHOT_HISTORY_LOAD_FAILED"));
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error while loading configuration: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
            }
        }

        private void CreateConfigurationBackup()
        {
            try
            {
                if (_machineConfiguration == null || IsConfigurationDirty)
                {
                    _machineConfiguration = BuildRuntimeConfigurationFromView();
                    _machineConfigurationService.Save(_machineConfiguration);
                    MarkConfigurationSaved();
                    AddLogEntry("Active configuration saved before creating the backup.", IOLogLevel.Info, "CONFIG");
                }

                var backupPath = _machineConfigurationService.CreateBackupFromActiveConfiguration("manual");
                RefreshConfigurationBackups();

                if (string.IsNullOrWhiteSpace(backupPath))
                {
                    AddLogEntry("Configuration backup not created: active operating file is not available.", IOLogLevel.Warning, "CONFIG");
                    return;
                }

                SelectedConfigurationBackup = ConfigurationBackups.FirstOrDefault(item => string.Equals(item.FilePath, backupPath, StringComparison.OrdinalIgnoreCase));
                AddLogEntry($"Configuration backup created: {System.IO.Path.GetFileName(backupPath)}", IOLogLevel.Success, "CONFIG");
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error creating configuration backup: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
            }
        }

        private void RestoreSelectedConfigurationBackup()
        {
            try
            {
                if (SelectedConfigurationBackup == null)
                {
                    AddLogEntry("No backup selected for restore.", IOLogLevel.Warning, "CONFIG");
                    return;
                }

                if (!ConfirmPrivilegedOperation(
                    "Conferma ripristino configurazione",
                    $"Stai per ripristinare il backup '{SelectedConfigurationBackup.FileName}' come configurazione macchina attiva. Verra creato un backup di sicurezza prima del ripristino. Continuare?"))
                {
                    return;
                }

                var safeguardPath = _machineConfigurationService.CreateBackupFromActiveConfiguration("pre-restore");
                if (!string.IsNullOrWhiteSpace(safeguardPath))
                {
                    AddLogEntry($"Safety backup created before restore: {System.IO.Path.GetFileName(safeguardPath)}", IOLogLevel.Info, "CONFIG");
                }

                _machineConfigurationService.RestoreBackupToActiveConfiguration(SelectedConfigurationBackup.FilePath);
                AddLogEntry($"Backup restored to active operating file: {SelectedConfigurationBackup.FileName}", IOLogLevel.Success, "CONFIG");
                LoadConfiguration(null);
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error restoring configuration backup: {ex.Message}", IOLogLevel.Error, "CONFIG", ex);
            }
        }

        private void ClearLog(object parameter)
        {
            LogEntries.Clear();
            AddLogEntry("Log cleared", IOLogLevel.Info, "LOG");
        }

        private void ClearMachineEvents(object parameter)
        {
            MachineOperationalEvents.Clear();
            OnPropertyChanged(nameof(MachineEventsSummary));
            OnPropertyChanged(nameof(LastMachineEventSummary));
            OnPropertyChanged(nameof(MachineEventsFusionSummary));
            OnPropertyChanged(nameof(LastMachineEventFusionSummary));
            AddLogEntry("Machine event history cleared", IOLogLevel.Info, "TRACK");
        }

        private ToolFusionSnapshot BuildFusionSnapshot()
        {
            var runtimeConfiguration = BuildRuntimeConfigurationFromView();
            var latestCompatibleStatus = MachineOperationalEvents.FirstOrDefault()?.MainProjectMachineStatus ?? "Stopped";

            return new ToolFusionSnapshot
            {
                ConfigReference = new ToolFusionConfigReference
                {
                    ActiveConfigurationPath = MachineConfigurationPath,
                    TemplateConfigurationPath = MachineConfigurationTemplatePath,
                    BackupFolderPath = ConfigurationBackupFolderPath,
                    ExportFolderPath = FusionExportFolderPath,
                    ConfigurationName = runtimeConfiguration.ConfigurationName,
                    ConfigurationVersion = runtimeConfiguration.ConfigurationVersion
                },
                RuntimeSummary = new ToolFusionRuntimeSummary
                {
                    ActiveConnectionMode = ActiveConnectionModeLabel,
                    MachineControllerStatus = MachineControllerStatus,
                    TriggerMode = MachineTriggerMode,
                    RejectMode = MachineRejectMode,
                    MainEncoderAxisCode = BoundMainEncoderAxisCode,
                    MainProjectCompatibleMachineStatus = latestCompatibleStatus,
                    ActiveTrackedProducts = TrackedProducts.Count,
                    RuntimeEventCount = MachineOperationalEvents.Count
                },
                RuntimeEvents = MachineOperationalEvents
                    .Select(item => new ToolFusionRuntimeEventRecord
                    {
                        TimestampUtc = item.Timestamp.ToUniversalTime(),
                        EventCode = item.EventCode,
                        Category = item.Category,
                        Status = item.Status,
                        ProductId = item.ProductId,
                        SignalCode = item.SignalCode,
                        MachineQuotaMm = item.MachineQuotaMm,
                        Detail = item.Detail,
                        SourceArea = item.SourceArea,
                        Severity = item.Severity,
                        MainProjectCategory = item.MainProjectCategory,
                        MainProjectMachineStatus = item.MainProjectMachineStatus,
                        MainProjectVisionStatus = item.MainProjectVisionStatus,
                        MainProjectLogId = item.MainProjectLogId
                    })
                    .Take(200)
                    .ToList()
            };
        }

        private void UpdateRefreshTimer()
        {
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(PollingInterval);
        }

        private void UpdateVirtualConveyorTimerState()
        {
            if (_virtualConveyorTimer == null)
            {
                return;
            }

            // TimedFromPhotocell uses no encoder-based triggering. Running the virtual
            // conveyor in this mode is unnecessary and can interfere with diagnostics.
            if (IsTimedTriggerSchedulingActive)
            {
                if (_virtualConveyorTimer.IsEnabled)
                {
                    _virtualConveyorTimer.Stop();
                    _lastVirtualConveyorTickUtc = DateTime.MinValue;
                    AddLogEntry("Virtual conveyor stopped: TimedFromPhotocell mode does not use encoder-based triggering.", IOLogLevel.Info, "ENC");
                }
                return;
            }

            if (VirtualConveyorEnabled && SimulationEncoderRunning && !IsSimulationMode)
            {
                if (!_virtualConveyorTimer.IsEnabled)
                {
                    _lastVirtualConveyorTickUtc = DateTime.UtcNow;
                    _virtualConveyorTimer.Start();
                    AddLogEntry($"Virtual conveyor started at {GetVirtualConveyorSpeedMetersPerMinute():0.00} m/min", IOLogLevel.Success, "ENC");
                }

                return;
            }

            if (_virtualConveyorTimer.IsEnabled)
            {
                _virtualConveyorTimer.Stop();
                _lastVirtualConveyorTickUtc = DateTime.MinValue;
                AddLogEntry("Virtual conveyor stopped.", IOLogLevel.Info, "ENC");
            }
        }

        private double GetVirtualConveyorSpeedMetersPerMinute()
        {
            return SimulationUsePiecesPerMinute
                ? Math.Max(0.0, SimulationPiecesPerMinute) * Math.Max(0.0, SimulationProductPitchMm) / 1000.0
                : Math.Max(0.0, SimulationTransportSpeedMetersPerMinute);
        }

        private async Task OnVirtualConveyorTimerTickAsync()
        {
            if (_isVirtualConveyorTickRunning || !VirtualConveyorEnabled || IsSimulationMode)
            {
                return;
            }

            _isVirtualConveyorTickRunning = true;
            try
            {
                var now = DateTime.UtcNow;
                if (_lastVirtualConveyorTickUtc == DateTime.MinValue)
                {
                    _lastVirtualConveyorTickUtc = now;
                    return;
                }

                var elapsedSeconds = Math.Max(0.0, (now - _lastVirtualConveyorTickUtc).TotalSeconds);
                _lastVirtualConveyorTickUtc = now;

                if (!SimulationEncoderRunning || elapsedSeconds <= 0.0)
                {
                    return;
                }

                var countsPerMillimeter = GetCountsPerMillimeter(VirtualConveyorEncoderChannel);
                if (countsPerMillimeter <= 0.0)
                {
                    return;
                }

                var speedMmPerSecond = GetVirtualConveyorSpeedMetersPerMinute() * 1000.0 / 60.0;
                var exactDeltaCounts = speedMmPerSecond * elapsedSeconds * countsPerMillimeter + _virtualConveyorFractionalCounts;
                var deltaCounts = (long)Math.Floor(exactDeltaCounts);
                _virtualConveyorFractionalCounts = exactDeltaCounts - deltaCounts;
                if (deltaCounts <= 0)
                {
                    return;
                }

                _virtualConveyorCount += deltaCounts;
                ApplyVirtualEncoderSample(_virtualConveyorCount, deltaCounts, countsPerMillimeter, speedMmPerSecond);
                _machineController.UpdateEncoderPosition(VirtualConveyorEncoderChannel, _virtualConveyorCount);
                _multiShotTriggerController?.OnEncoderChanged(VirtualConveyorEncoderChannel, _virtualConveyorCount);
                await Task.CompletedTask;
            }
            finally
            {
                _isVirtualConveyorTickRunning = false;
            }
        }

        private void ApplyVirtualEncoderSample(long currentCount, long deltaCounts, double countsPerMillimeter, double speedMmPerSecond)
        {
            if (VirtualConveyorEncoderChannel < 0 || VirtualConveyorEncoderChannel >= EncoderChannels.Count)
            {
                return;
            }

            var channel = EncoderChannels[VirtualConveyorEncoderChannel];
            channel.CounterValue = currentCount;
            channel.Status = $"Virtual count: {currentCount}";
            channel.LastDeltaCounts = deltaCounts;
            channel.RawPulseFrequencyHz = speedMmPerSecond * countsPerMillimeter;
            channel.CountsPerMillimeter = countsPerMillimeter;
            channel.RawSpeedMetersPerMinute = GetVirtualConveyorSpeedMetersPerMinute();
            channel.SpeedMetersPerMinute = channel.RawSpeedMetersPerMinute;
            channel.EstimatedPiecesPerMinute = GetEstimatedPiecesPerMinute(channel.SpeedMetersPerMinute);
            channel.DiagnosticState = "Virtual conveyor HW mode";
            _filteredEncoderSpeedMetersPerMinute[VirtualConveyorEncoderChannel] = channel.SpeedMetersPerMinute;
            RefreshEncoderSummaryProperties();
        }

        private MachineSignalDefinition ResolveInputSignalByChannel(int channel)
        {
            return MachineInputs.FirstOrDefault(item => ParseChannelNumber(item?.Channel) == channel);
        }

        private MachineSignalDefinition ResolveInputSignalByCode(string signalCode)
        {
            var normalized = NormalizeSignalCode(signalCode);
            return MachineInputs.FirstOrDefault(item => string.Equals(NormalizeSignalCode(item?.SignalCode), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private MachineSignalDefinition ResolveOutputSignalByCode(string signalCode)
        {
            var candidates = GetCompatibleOutputSignalCodes(signalCode).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates
                .SelectMany((candidate, index) => MachineOutputs
                    .Where(item => string.Equals(NormalizeSignalCode(item?.SignalCode), candidate, StringComparison.OrdinalIgnoreCase))
                    .Select(item => new
                    {
                        Signal = item,
                        CandidateIndex = index,
                        HasPhysicalMapping = HasPhysicalMapping(item),
                        IsReal = item?.ReservedForRealSignal == true
                    }))
                .OrderByDescending(item => item.HasPhysicalMapping && item.IsReal)
                .ThenByDescending(item => item.HasPhysicalMapping)
                .ThenByDescending(item => item.IsReal)
                .ThenBy(item => item.CandidateIndex)
                .ThenByDescending(item => item.Signal?.IsRuntimeBound == true)
                .Select(item => item.Signal)
                .FirstOrDefault();
        }

        private void PrepareMachineOutputDefinitionsForSave()
        {
            var emptyRows = MachineOutputs
                .Where(signal => signal == null ||
                    (string.IsNullOrWhiteSpace(signal.SignalCode) &&
                     string.IsNullOrWhiteSpace(signal.Channel)))
                .ToList();
            foreach (var emptyRow in emptyRows)
            {
                MachineOutputs.Remove(emptyRow);
            }

            foreach (var signal in MachineOutputs.Where(signal => signal != null))
            {
                signal.SignalCode = NormalizeSignalCode(signal.SignalCode);
                signal.Board = (signal.Board ?? string.Empty).Trim();
                signal.Channel = (signal.Channel ?? string.Empty).Trim().ToUpperInvariant();
                signal.Direction = MachineSignalDirection.Output;
            }

            foreach (var point in InterventionPoints.Where(point =>
                point != null && string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase)))
            {
                var signal = ResolveOutputSignalByCode(point.SignalCode);
                if (signal != null)
                {
                    signal.Direction = MachineSignalDirection.Output;
                    signal.Category = MachineSignalCategory.CameraTrigger;
                }
            }

        }

        private void VerifyMachineOutputsPersisted(MachineRuntimeConfiguration expected)
        {
            var reloaded = _machineConfigurationService.Load();
            if (reloaded == null)
            {
                throw new System.IO.IOException("The machine configuration could not be read back after saving.");
            }

            var expectedOutputs = expected?.MachineOutputs ?? new List<MachineSignalDefinition>();
            var actualOutputs = reloaded.MachineOutputs ?? new List<MachineSignalDefinition>();
            foreach (var expectedSignal in expectedOutputs)
            {
                bool persisted = actualOutputs.Any(actualSignal =>
                    string.Equals(actualSignal?.SignalCode, expectedSignal?.SignalCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(actualSignal.Board, expectedSignal.Board, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(actualSignal.Channel, expectedSignal.Channel, StringComparison.OrdinalIgnoreCase) &&
                    actualSignal.Direction == MachineSignalDirection.Output &&
                    actualSignal.Category == expectedSignal.Category &&
                    actualSignal.Polarity == expectedSignal.Polarity &&
                    actualSignal.ReservedForRealSignal == expectedSignal.ReservedForRealSignal);
                if (!persisted)
                {
                    throw new System.IO.IOException($"Machine output '{expectedSignal?.SignalCode}' did not pass save/read-back verification.");
                }
            }
        }

        /// <summary>
        /// Riga di MachineOutputs con codice ESATTAMENTE uguale a quello richiesto.
        ///
        /// L'editor dei punti di intervento non puo' usare <see cref="ResolveOutputSignalByCode"/>:
        /// quello espande il codice negli alias (REAR/RIGHT, SIDE/LEFT) e ordina prima per
        /// mappatura fisica, tenendo la corrispondenza esatta solo come quarto criterio. E'
        /// giusto a runtime, dove serve trovare l'uscita fisica da impulsare comunque sia
        /// nominata; e' sbagliato nell'editor, dove l'operatore sta configurando UN segnale
        /// preciso. Con la riga REAR priva di canale e l'alias RIGHT mappato, l'editor
        /// leggeva e SCRIVEVA sulla riga dell'alias: il valore digitato finiva su un'altra
        /// camera e la riga REAR restava incompleta a ogni salvataggio.
        /// </summary>
        private MachineSignalDefinition ResolveExactOutputSignalByCode(string signalCode)
        {
            var normalized = NormalizeSignalCode(signalCode);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return MachineOutputs.FirstOrDefault(item =>
                string.Equals(NormalizeSignalCode(item?.SignalCode), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private MachineSignalDefinition ResolveSelectedInterventionOutput()
        {
            return ResolveExactOutputSignalByCode(SelectedInterventionPoint?.SignalCode);
        }

        private MachineSignalDefinition GetOrCreateSelectedInterventionOutput()
        {
            var signalCode = NormalizeSignalCode(SelectedInterventionPoint?.SignalCode);
            if (string.IsNullOrWhiteSpace(signalCode))
            {
                return null;
            }

            // Anche qui corrispondenza esatta: se la riga del codice richiesto non esiste va
            // creata, non presa quella dell'alias. Prendendo l'alias, la riga del segnale
            // realmente referenziato dal punto non veniva mai completata.
            var signal = ResolveExactOutputSignalByCode(signalCode);
            if (signal != null)
            {
                return signal;
            }

            signal = new MachineSignalDefinition
            {
                SignalCode = signalCode,
                Description = $"Hardware output for {SelectedInterventionPoint?.PointCode ?? signalCode}",
                Direction = MachineSignalDirection.Output,
                Category = string.Equals(SelectedInterventionPoint?.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase)
                    ? MachineSignalCategory.CameraTrigger
                    : MachineSignalCategory.Spare,
                Board = "PCIE-1756-BE",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = true,
                Notes = "Global machine mapping created from Intervention Point Editor."
            };
            MachineOutputs.Add(signal);
            return signal;
        }

        private void UpdateSelectedInterventionOutput(Action<MachineSignalDefinition> update)
        {
            var signal = GetOrCreateSelectedInterventionOutput();
            if (signal == null || update == null)
            {
                return;
            }

            update(signal);
            signal.Direction = MachineSignalDirection.Output;
            if (string.Equals(SelectedInterventionPoint?.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase))
            {
                signal.Category = MachineSignalCategory.CameraTrigger;
            }

            MarkConfigurationDirty();
            RefreshMachineSignalStatuses();
            NotifySelectedInterventionOutputMappingChanged();
        }

        private void NotifySelectedInterventionOutputMappingChanged()
        {
            OnPropertyChanged(nameof(SelectedInterventionOutputBoard));
            OnPropertyChanged(nameof(SelectedInterventionOutputChannel));
            OnPropertyChanged(nameof(SelectedInterventionOutputPolarity));
            OnPropertyChanged(nameof(SelectedInterventionOutputIsReal));
            OnPropertyChanged(nameof(SelectedInterventionOutputMappingStatus));
        }

        private static string NormalizeSignalCode(string signalCode)
        {
            return (signalCode ?? string.Empty).Trim();
        }

        private static IEnumerable<string> GetCompatibleOutputSignalCodes(string signalCode)
        {
            var normalized = NormalizeSignalCode(signalCode);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                yield break;
            }

            yield return normalized;

            if (string.Equals(normalized, "OUT_CAMERA_LEFT_TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                yield return "OUT_CAMERA_SIDE_TRIGGER";
            }
            else if (string.Equals(normalized, "OUT_CAMERA_SIDE_TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                yield return "OUT_CAMERA_LEFT_TRIGGER";
            }
        }

        private static bool HasPhysicalMapping(MachineSignalDefinition signal)
        {
            return signal != null &&
                   !string.IsNullOrWhiteSpace(signal.Board) &&
                   !string.IsNullOrWhiteSpace(signal.Channel);
        }

        private static string BuildOutputPhysicalKey(MachineSignalDefinition signal)
        {
            if (signal == null)
            {
                return string.Empty;
            }

            return $"{signal.Board ?? "BOARD?"}:{signal.Channel ?? "CHANNEL?"}";
        }

        private bool ShouldIgnorePhotocellDuplicate(long encoderCount, out double deltaMillimeters, out double elapsedMilliseconds)
        {
            deltaMillimeters = double.MaxValue;
            elapsedMilliseconds = double.MaxValue;

            if (_lastAcceptedPhotocellEncoderCount == long.MinValue || _lastAcceptedPhotocellUtc == DateTime.MinValue)
            {
                return false;
            }

            elapsedMilliseconds = Math.Max(0.0, (DateTime.UtcNow - _lastAcceptedPhotocellUtc).TotalMilliseconds);
            var mainEncoderChannel = GetEncoderTemplateChannelNumber(MainEncoderTemplate);
            var countsPerMillimeter = GetCountsPerMillimeter(mainEncoderChannel >= 0 ? mainEncoderChannel : VirtualConveyorEncoderChannel);
            deltaMillimeters = countsPerMillimeter <= 0.0
                ? double.MaxValue
                : Math.Abs(encoderCount - _lastAcceptedPhotocellEncoderCount) / countsPerMillimeter;

            var configuredTimeWindowMs = Math.Max(
                PhotocellDuplicateDebounceMs,
                Math.Max(PhotocellDebounceMs, MinimumRetriggerGapMs));

            // In TimedFromPhotocell mode there is no running encoder between products,
            // so deltaMillimeters is always 0 (encoder idle) or MaxValue (not calibrated).
            // OR logic would suppress every trigger after the first. Use time-only debounce.
            if (IsTimedTriggerSchedulingActive)
                return elapsedMilliseconds <= configuredTimeWindowMs;

            // VirtualConveyor: suppress if too recent OR too close in distance.
            return elapsedMilliseconds <= configuredTimeWindowMs ||
                   deltaMillimeters <= PhotocellDuplicateMinimumDistanceMm;
        }

        private bool TryRegisterTriggerOutputForProduct(TrackedProduct product, MachineSignalDefinition signal, ProductInterventionState state)
        {
            if (product == null || signal == null)
            {
                return true;
            }

            var physicalKey = BuildOutputPhysicalKey(signal);
            if (string.IsNullOrWhiteSpace(physicalKey))
            {
                return true;
            }

            lock (_triggerOutputsLock)
            {
                if (!_executedTriggerOutputsByProduct.TryGetValue(product.ProductId, out var outputs))
                {
                    outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _executedTriggerOutputsByProduct[product.ProductId] = outputs;
                }

                if (!outputs.Add(physicalKey))
                {
                    AddLogEntry(
                        $"Suppressed duplicate trigger pulse for product {product.ProductId}: point {state?.PointCode} maps again to physical output {physicalKey} ({signal.SignalCode}).",
                        IOLogLevel.Warning,
                        "TRACK");
                    return false;
                }

                return true;
            }
        }

        private void ClearTriggerOutputRegistrationsForProduct(long productId)
        {
            lock (_triggerOutputsLock)
            {
                _executedTriggerOutputsByProduct.Remove(productId);
            }
        }

        private static bool IsSignalLogicallyActive(MachineSignalDefinition signal, bool electricalState)
        {
            return MachineRuntimeIo.IsSignalLogicallyActive(signal, electricalState);
        }

        private static bool ToElectricalOutputState(MachineSignalDefinition signal, bool logicalState)
        {
            return MachineRuntimeIo.ToElectricalOutputState(signal, logicalState);
        }

        private async Task<bool> ReadMappedInputStateAsync(string signalCode)
        {
            var signal = ResolveInputSignalByCode(signalCode);
            var channel = ParseChannelNumber(signal?.Channel);
            if (!Pcie1756ChannelMap.IsValidInputChannel(channel))
            {
                return false;
            }

            var electricalState = await _ioManager.ReadInputAsync(channel);
            return IsSignalLogicallyActive(signal, electricalState);
        }

        private async Task RefreshCachedExternalTriggerStateAsync()
        {
            try
            {
                _cachedExternalTriggerEnableActive = await ReadMappedInputStateAsync(BoundExternalTriggerEnableSignalCode);
            }
            catch (Exception ex)
            {
                _cachedExternalTriggerEnableActive = false;
                Logger?.Debug(ex, "EXTERNAL_TRIGGER_ENABLE_CACHE_REFRESH_FAILED");
            }
        }

        private async Task SetMappedOutputAsync(string signalCode, bool logicalValue, string reason)
        {
            var signal = ResolveOutputSignalByCode(signalCode);
            var channel = ParseChannelNumber(signal?.Channel);
            if (signal == null || !Pcie1756ChannelMap.IsValidOutputChannel(channel))
            {
                AddLogEntry($"Output signal not configured: {signalCode}{DescribeOutOfRangeChannel(signal, AllowedOutputChannelRangeText)}", IOLogLevel.Warning, "IO");
                return;
            }

            var electricalValue = ToElectricalOutputState(signal, logicalValue);
            await _ioManager.WriteOutputAsync(channel, electricalValue);
            ApplyOutputState(channel, electricalValue);
            if (!string.Equals(reason, "diagnostic heartbeat", StringComparison.OrdinalIgnoreCase))
            {
                AddLogEntry(
                    $"{signal.SignalCode} -> {(logicalValue ? "ATTIVO" : "DISATTIVO")} [{signal.Board}/{signal.Channel}] logical={logicalValue} electrical={electricalValue} | {reason}",
                    IOLogLevel.Info,
                    "IO");
            }
        }

        /// <summary>
        /// <paramref name="requestedAtUtc"/> e' il momento in cui il punto di intervento e'
        /// stato raggiunto sull'encoder. Serve a misurare la latenza che conta davvero per la
        /// sincronizzazione camera: quella fra il raggiungimento della quota e il FRONTE DI
        /// SALITA fisico dell'uscita. INTERVENTION_DISPATCH_DELAY copre solo il tratto
        /// enqueue->dequeue e non include ne' la risoluzione del segnale ne' la scrittura.
        /// </summary>
        /// <returns>
        /// true se il fronte di salita e' arrivato in scheda (anche quando fallisce la sola
        /// discesa); false se la camera non ha ricevuto alcun trigger.
        /// </returns>
        private enum PulseExecutionOutcome { NoEdge, RisingEdgeOnly, Completed }

        private struct PulseExecutionResult
        {
            public PulseExecutionOutcome Outcome;
            public long RaisedStopwatchTicks;
        }

        private async Task<bool> PulseMappedOutputAsync(
            string signalCode, int pulseMs, string reason, DateTime? requestedAtUtc = null,
            string cameraRole = null, long productId = 0)
        {
            var result = await PulseMappedOutputDetailedAsync(
                signalCode, pulseMs, reason, requestedAtUtc, cameraRole, productId).ConfigureAwait(false);
            return result.Outcome != PulseExecutionOutcome.NoEdge;
        }

        private async Task<PulseExecutionResult> PulseMappedOutputDetailedAsync(
            string signalCode, int pulseMs, string reason, DateTime? requestedAtUtc = null,
            string cameraRole = null, long productId = 0)
        {
            if (cameraRole != null && !IsActiveVppCameraRole(cameraRole))
            {
                return new PulseExecutionResult { Outcome = PulseExecutionOutcome.NoEdge };
            }
            var signal = ResolveOutputSignalByCode(signalCode);
            var channel = ParseChannelNumber(signal?.Channel);
            if (signal == null || !Pcie1756ChannelMap.IsValidOutputChannel(channel))
            {
                AddLogEntry($"Output signal not configured: {signalCode}{DescribeOutOfRangeChannel(signal, AllowedOutputChannelRangeText)}", IOLogLevel.Warning, "IO");
                return new PulseExecutionResult { Outcome = PulseExecutionOutcome.NoEdge };
            }

            // Serialize concurrent pulses on the same physical channel so two callers
            // can never interleave their ON/OFF transitions on the same DO line.
            SemaphoreSlim sem;
            lock (_outputChannelSemaphoresLock)
            {
                if (!_outputChannelSemaphores.TryGetValue(channel, out sem))
                {
                    sem = new SemaphoreSlim(1, 1);
                    _outputChannelSemaphores[channel] = sem;
                }
            }

            var channelWait = Stopwatch.StartNew();
            await sem.WaitAsync();
            channelWait.Stop();
            try
            {
                if (cameraRole != null && !IsActiveVppCameraRole(cameraRole))
                {
                    return new PulseExecutionResult { Outcome = PulseExecutionOutcome.NoEdge };
                }
                var activeElectrical = ToElectricalOutputState(signal, true);
                var inactiveElectrical = ToElectricalOutputState(signal, false);
                int effectivePulseMs = Math.Max(20, pulseMs);
                long pulseQueuedTicks = Stopwatch.GetTimestamp();
                double preQueueLatencyMs = requestedAtUtc.HasValue
                    ? (DateTime.UtcNow - requestedAtUtc.Value).TotalMilliseconds
                    : -1.0;
                OutputPulseResult pulseResult;
                try
                {
                    pulseResult = await _ioManager.PulseOutputAsync(
                        channel, activeElectrical, inactiveElectrical, effectivePulseMs).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    ApplyOutputState(channel, inactiveElectrical);
                    AddLogEntry(
                        $"IO_PULSE_CANCELLED|signal={signal.SignalCode}|board={signal.Board}|channel={signal.Channel}|reason={reason}",
                        IOLogLevel.Warning,
                        "IO",
                        ex);
                    return new PulseExecutionResult { Outcome = PulseExecutionOutcome.NoEdge };
                }
                catch (Exception ex)
                {
                    // Un fronte rifiutato dalla scheda non deve comparire come Pulse START/END:
                    // con Rise la camera non ha ricevuto il trigger, con Fall la linea puo' essere
                    // rimasta alta dopo un trigger valido.
                    bool edgeGenerated = ex is OutputPulseException pulseException && pulseException.RisingEdgeGenerated;
                    ApplyOutputState(channel, inactiveElectrical);
                    AddLogEntry(
                        $"IO_PULSE_FAILED|signal={signal.SignalCode}|board={signal.Board}|channel={signal.Channel}" +
                        $"|edgeGenerated={edgeGenerated}|reason={reason}",
                        IOLogLevel.Error,
                        "IO",
                        ex);
                    if (cameraRole != null)
                    {
                        CameraDiagnostics.PulseFailed(cameraRole, productId, signal.SignalCode, edgeGenerated, ex);
                    }
                    return new PulseExecutionResult
                    {
                        Outcome = edgeGenerated ? PulseExecutionOutcome.RisingEdgeOnly : PulseExecutionOutcome.NoEdge
                    };
                }

                double schedulerQueueMs = (pulseResult.RaisedStopwatchTicks - pulseQueuedTicks) * 1000.0 /
                                          Stopwatch.Frequency;
                ApplyOutputState(channel, inactiveElectrical);

                // Latenza quota-raggiunta -> fronte di salita fisico: e' il numero che spiega
                // "l'uscita si alza in ritardo" osservato in campo. Include handoff al worker,
                // risoluzione segnale, attesa canale e scrittura BDaq.
                double raiseLatencyMs = requestedAtUtc.HasValue
                    ? preQueueLatencyMs + schedulerQueueMs
                    : -1.0;
                string raiseLatencyText = raiseLatencyMs >= 0
                    ? $" | raiseLatency={raiseLatencyMs:0.###} ms"
                    : string.Empty;

                AddLogEntry(
                    $"Pulse START {signal.SignalCode} [{signal.Board}/{signal.Channel}] width={effectivePulseMs} ms" +
                    $" | channelWait={channelWait.Elapsed.TotalMilliseconds:0.###} ms{raiseLatencyText} | reason={reason}",
                    IOLogLevel.Info,
                    "IO");

                if (raiseLatencyMs > 5.0)
                {
                    Logger?.Warn(
                        $"IO_PULSE_RAISE_LATE|signal={signal.SignalCode}|channel={channel}" +
                        $"|raiseLatencyMs={raiseLatencyMs:0.###}" +
                        $"|channelWaitMs={channelWait.Elapsed.TotalMilliseconds:0.###}|reason={reason}");
                }
                double actualHighMs = pulseResult.ActualHighMilliseconds;
                double overrunMs = actualHighMs - effectivePulseMs;
                if (cameraRole != null)
                {
                    CameraDiagnostics.PulseCompleted(
                        cameraRole, productId, signal.SignalCode, signal.Channel,
                        raiseLatencyMs, effectivePulseMs, actualHighMs);
                }
                AddLogEntry(
                    $"Pulse END {signal.SignalCode} [{signal.Board}/{signal.Channel}] requested={effectivePulseMs} ms" +
                    $" | actualHigh={actualHighMs:0.###} ms | overrun={overrunMs:+0.###;-0.###;0} ms | reason={reason}",
                    IOLogLevel.Info,
                    "IO");

                if (overrunMs > Math.Max(5.0, effectivePulseMs * 0.25))
                {
                    Logger?.Warn(
                        $"IO_PULSE_TIMING_OVERRUN|signal={signal.SignalCode}|channel={channel}" +
                        $"|requestedMs={effectivePulseMs}|actualHighMs={actualHighMs:0.###}" +
                        $"|overrunMs={overrunMs:0.###}|channelWaitMs={channelWait.Elapsed.TotalMilliseconds:0.###}" +
                        $"|reason={reason}");
                }

                return new PulseExecutionResult
                {
                    Outcome = PulseExecutionOutcome.Completed,
                    RaisedStopwatchTicks = pulseResult.RaisedStopwatchTicks
                };
            }
            finally
            {
                sem.Release();
            }
        }

        private void EnsureHeartbeatSignalExists()
        {
            if (MachineOutputs.Any(signal => string.Equals(signal.SignalCode, "OUT_HEARTBEAT", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = "OUT_HEARTBEAT",
                Description = "Diagnostic heartbeat output for commissioning and board communication test",
                Direction = MachineSignalDirection.Output,
                Category = MachineSignalCategory.Alarm,
                Board = "PCIE-1756-BE",
                Channel = "DO07",
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = false,
                Notes = "Dedicated blinking output for diagnostics. Prefer a spare channel or commissioning lamp."
            });
        }

        private async Task ToggleHeartbeatAsync()
        {
            if (IsHeartbeatRunning)
            {
                await StopHeartbeatAsync("stop manuale heartbeat");
                return;
            }

            await StartHeartbeatAsync(false, "avvio manuale heartbeat");
        }

        private async Task RestartHeartbeatAsync(string reason)
        {
            await StopHeartbeatAsync(reason);
            await StartHeartbeatAsync(false, reason);
        }

        private async Task StartHeartbeatAsync(bool automaticStart, string reason)
        {
            if (!HeartbeatEnabled)
            {
                AddLogEntry("Heartbeat non avviato: funzione disabilitata in configurazione.", IOLogLevel.Warning, "IO");
                return;
            }

            var signal = ResolveOutputSignalByCode(BoundHeartbeatOutputSignalCode);
            var channel = ParseChannelNumber(signal?.Channel);
            if (signal == null || !Pcie1756ChannelMap.IsValidOutputChannel(channel))
            {
                AddLogEntry($"Heartbeat not started: diagnostic output not configured.{DescribeOutOfRangeChannel(signal, AllowedOutputChannelRangeText)}", IOLogLevel.Warning, "IO");
                return;
            }

            if (automaticStart && IsSimulationMode)
            {
                await StopHeartbeatAsync("automatic heartbeat disabled in simulation");
                return;
            }

            if (_heartbeatTimer.IsEnabled)
            {
                return;
            }

            _heartbeatTimer.Interval = TimeSpan.FromMilliseconds(HeartbeatIntervalMs);
            HeartbeatOutputState = false;
            await SetMappedOutputAsync(BoundHeartbeatOutputSignalCode, false, "heartbeat initialization");
            _heartbeatTimer.Start();
            IsHeartbeatRunning = true;
            AddLogEntry($"Heartbeat {(automaticStart ? "automatic" : "manual")} started on {signal.SignalCode} ({signal.Channel}) with a {HeartbeatIntervalMs} ms period - {reason}", IOLogLevel.Success, "IO");
        }

        private async Task StopHeartbeatAsync(string reason)
        {
            if (_heartbeatTimer.IsEnabled)
            {
                _heartbeatTimer.Stop();
            }

            var wasRunning = IsHeartbeatRunning;
            IsHeartbeatRunning = false;
            HeartbeatOutputState = false;

            var signal = ResolveOutputSignalByCode(BoundHeartbeatOutputSignalCode);
            if (signal != null)
            {
                await SetMappedOutputAsync(BoundHeartbeatOutputSignalCode, false, reason);
            }

            if (wasRunning)
            {
                AddLogEntry($"Heartbeat stopped - {reason}", IOLogLevel.Info, "IO");
            }
        }

        private async Task OnHeartbeatTimerTickAsync()
        {
            if (_isHeartbeatTickRunning)
            {
                return;
            }

            _isHeartbeatTickRunning = true;
            try
            {
                if (!HeartbeatEnabled || string.IsNullOrWhiteSpace(BoundHeartbeatOutputSignalCode))
                {
                    await StopHeartbeatAsync("invalid heartbeat configuration");
                    return;
                }

                HeartbeatOutputState = !HeartbeatOutputState;
                await SetMappedOutputAsync(BoundHeartbeatOutputSignalCode, HeartbeatOutputState, "diagnostic heartbeat");
            }
            catch (Exception ex)
            {
                AddLogEntry($"Heartbeat error: {ex.Message}", IOLogLevel.Error, "IO", ex);
                await StopHeartbeatAsync("heartbeat runtime error");
            }
            finally
            {
                _isHeartbeatTickRunning = false;
            }
        }

        private async Task EvaluateHeartbeatAutoStartAsync(string reason)
        {
            if (HeartbeatEnabled && HeartbeatAutoStartOnRealHardware && !IsSimulationMode)
            {
                await StartHeartbeatAsync(true, reason);
                return;
            }

            if (IsHeartbeatRunning)
            {
                await StopHeartbeatAsync(reason);
            }
        }

        private async Task ExecuteTriggerCycleAsync(TrackedProduct product)
        {
            var label = $"product {product.ProductId}";
            var externalEnabled = await ReadMappedInputStateAsync(BoundExternalTriggerEnableSignalCode);
            var mode = string.IsNullOrWhiteSpace(MachineTriggerMode) ? "Hybrid" : MachineTriggerMode;
            if (string.Equals(mode, "External", StringComparison.OrdinalIgnoreCase)) { AddLogEntry($"TriggerMode=External: waiting for external trigger for {label}", IOLogLevel.Info, "TRACK"); return; }
            if (string.Equals(mode, "Hybrid", StringComparison.OrdinalIgnoreCase) && externalEnabled) { AddLogEntry($"{label}: external trigger enable active, waiting for external machine command", IOLogLevel.Info, "TRACK"); return; }
            if (string.Equals(mode, "Internal", StringComparison.OrdinalIgnoreCase) && externalEnabled) { AddLogEntry($"TriggerMode=Internal: external enable present but ignored for {label}", IOLogLevel.Info, "TRACK"); }
            await PulseMappedOutputAsync(BoundCameraTriggerSignalCode, 40, $"per {label}");
        }


        private async Task SimulateProductPassageAsync()
        {
            var signal = ResolveInputSignalByCode(BoundProductPhotocellSignalCode);
            var channel = ParseChannelNumber(signal?.Channel);
            if (signal == null || !Pcie1756ChannelMap.IsValidInputChannel(channel))
            {
                AddLogEntry($"Product photocell is not configured in runtime mapping{DescribeOutOfRangeChannel(signal, AllowedInputChannelRangeText)}", IOLogLevel.Warning, "TRACK");
                return;
            }

            if (!IsSimulationMode)
            {
                AddLogEntry("Product passage simulation is available only in simulation mode", IOLogLevel.Warning, "TRACK");
                return;
            }

            await _ioManager.PulseSimulationInputAsync(channel, 80);
            await ReadInputsAsync();
        }

        private async Task ForceTimedOutputAsync(string outputName)
        {
            if (!ConfirmPrivilegedOperation(
                "Conferma trigger manuale",
                $"Stai per forzare manualmente il trigger temporizzato {outputName}. Usare solo in manutenzione o commissioning. Continuare?"))
            {
                return;
            }

            var request = BuildTimedTriggerBatchRequest(null, _machineController.GetLastMainEncoderCount())
                .Outputs
                .FirstOrDefault(item => string.Equals(item.OutputName, outputName, StringComparison.OrdinalIgnoreCase));

            if (request == null || !request.Enabled || string.IsNullOrWhiteSpace(request.SignalCode))
            {
                AddLogEntry($"Timed trigger manual force skipped: {outputName} output is not configured.", IOLogLevel.Warning, "TRIGGER-TIMED");
                return;
            }

            await PulseMappedOutputAsync(request.SignalCode, request.PulseMs > 0 ? request.PulseMs : 40, $"manual timed trigger {outputName}");
            AddLogEntry($"Manual timed trigger executed on {outputName}: {request.SignalCode}", IOLogLevel.Warning, "TRIGGER-TIMED");
            AddMachineOperationalEvent(
                "Trigger",
                $"MANUAL_TIMED_{outputName}_TRIGGER",
                $"Manual timed {outputName} trigger",
                $"Manual timed trigger executed on {request.SignalCode}.",
                "Manual",
                null,
                request.SignalCode,
                null);
        }

        private void SimulateTimedPhotocellEdge()
        {
            if (!ConfirmPrivilegedOperation(
                "Conferma simulazione fotocellula",
                "Stai per simulare un fronte fotocellula temporizzato senza evento fisico reale. Continuare?"))
            {
                return;
            }

            QueueTimedPhotocellTriggerBatch(null, _machineController.GetLastMainEncoderCount());
            AddLogEntry("Manual timed photocell edge simulated from panel command.", IOLogLevel.Warning, "TRIGGER-TIMED");
            AddMachineOperationalEvent(
                "Trigger",
                "MANUAL_TIMED_PHOTOCELL_EDGE",
                "Manual timed photocell edge",
                "Timed trigger batch created manually without waiting for the real photocell input.",
                "Manual",
                null,
                BoundProductPhotocellSignalCode,
                MainEncoderTemplate?.PhotocellMachineOffsetMm);
        }

        private void ResetTimedTriggerDiagnostics()
        {
            ClearTimedTriggerDiagnostics();
            AddLogEntry("Timed trigger diagnostics reset from panel.", IOLogLevel.Info, "TRIGGER-TIMED");
        }

        private bool ConfirmPrivilegedOperation(string title, string message)
        {
            if (!(UserSession.IsAdministrator || UserSession.IsInstaller || UserSession.IsExpert))
            {
                new SystemNotificationWindow(
                    ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_AuthorizationRequiredTitle",
                        "Authorization required"),
                    ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_TechnicalRoleRequired",
                        "This action is reserved to Expert, Installer or Administrator."),
                    NotificationSeverity.Warning).ShowDialog();
                AddLogEntry($"Privileged operation blocked for role {UserSession.CurrentRole}: {title}", IOLogLevel.Warning, "AUTH");
                return false;
            }

            if (ServiceLocator.MachineRuntimeService?.IsContinuousRunHoldActive(MachineRuntimeService.HoldReasonManualStop) != true)
            {
                new SystemNotificationWindow(
                    ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_ManualStopRequiredTitle",
                        "Manual stop required"),
                    ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_ManualStopRequiredForTechnicalAction",
                        "Stop the machine manually before executing this technical action."),
                    NotificationSeverity.Warning).ShowDialog();
                AddLogEntry($"Privileged operation blocked because manual stop is not active: {title}", IOLogLevel.Warning, "AUTH");
                return false;
            }

            var dialog = new SystemNotificationWindow(title, message, NotificationSeverity.Warning, true);
            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        private async Task ApplyLatestInspectionResultAsync(bool accepted)
        {
            var latestProduct = TrackedProducts.FirstOrDefault(product =>
                product.State != TrackedProductState.Completed &&
                product.State != TrackedProductState.RejectedExecuted);

            if (latestProduct == null)
            {
                AddLogEntry("No product available to apply the result", IOLogLevel.Warning, "TRACK");
                return;
            }

            if (_machineController.ApplyInspectionResult(latestProduct.ProductId, accepted, "Standalone IO test app"))
            {
                latestProduct.IsAccepted = accepted;
                latestProduct.State = accepted ? TrackedProductState.Accepted : TrackedProductState.RejectedPending;
                LastTrackedProductStatus = string.Format(
                    T("Tool_VM_ProductStatusResult", "Product {0}: {1}"),
                    latestProduct.ProductId,
                    accepted ? "OK" : "NOK");
                await SetMappedOutputAsync(BoundGeneralAlarmSignalCode, !accepted, accepted ? "alarm reset after OK" : "alarm on NOK product");
                AddMachineOperationalEvent(
                    "Result",
                    accepted ? "RESULT_OK" : "RESULT_NOK",
                    accepted ? "Product result OK" : "Product result NOK",
                    accepted ? "The product has been marked as accepted." : "The product has been marked as rejected.",
                    accepted ? "OK" : "NOK",
                    latestProduct.ProductId,
                    accepted ? BoundGeneralAlarmSignalCode : BoundRejectSignalCode,
                    latestProduct.DetectionMachinePositionMm);
            }
            else
            {
                AddLogEntry("Unable to apply the result to the selected product", IOLogLevel.Warning, "TRACK");
            }
        }

        private async Task ExecuteRejectForProductAsync(TrackedProduct product, string reason)
        {
            if (product == null)
            {
                return;
            }

            if (string.Equals(MachineRejectMode, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                AddLogEntry($"Product {product.ProductId}: reject output inhibited by RejectMode=Disabled", IOLogLevel.Warning, "TRACK");
                return;
            }

            lock (product)
            {
                if (product.RejectExecuted || product.RejectAttempted)
                {
                    AddLogEntry($"Product {product.ProductId}: reject already attempted, no new activation", IOLogLevel.Info, "TRACK");
                    return;
                }
                product.RejectAttempted = true;
            }

            if (RejectActuatorType == "Bistable")
            {
                product.Notes = "Bistable reject unavailable: OPEN/CLOSE position sensors are not implemented.";
                LastTrackedProductStatus = $"Product {product.ProductId}: reject unavailable";
                AddLogEntry($"REJECT_BISTABLE_NOT_READY|product={product.ProductId}|position sensors not configured", IOLogLevel.Error, "TRACK");
                AddMachineOperationalEvent("Reject", "REJECT_NOT_EXECUTED", "Bistable reject unavailable",
                    product.Notes, "Failed", product.ProductId, BoundRejectSignalCode, product.CurrentMachinePositionMm);
                return;
            }

            PulseExecutionResult single = await PulseMappedOutputDetailedAsync(
                BoundRejectSignalCode, ConfiguredRejectPulseMs, $"for product {product.ProductId} ({reason})");
            bool completed = single.Outcome == PulseExecutionOutcome.Completed;

            if (!completed)
            {
                product.Notes = $"Reject output incomplete: {reason}.";
                LastTrackedProductStatus = $"Product {product.ProductId}: reject output failure";
                AddLogEntry($"REJECT_NOT_EXECUTED|product={product.ProductId}|reason={reason}", IOLogLevel.Error, "TRACK");
                AddMachineOperationalEvent(
                    "Reject", "REJECT_NOT_EXECUTED", "Reject output failure",
                    "A reject command did not complete; inspect output and actuator before resuming.",
                    "Failed", product.ProductId, BoundRejectSignalCode, product.CurrentMachinePositionMm);
                return;
            }

            product.RejectExecuted = true;
            product.State = TrackedProductState.RejectedExecuted;
            product.Notes = $"Reject executed: {reason}.";
            LastTrackedProductStatus = string.Format(
                T("Tool_VM_ProductStatusRejected", "Product {0}: REJECTED ({1})"),
                product.ProductId,
                MachineRejectMode);
            AddLogEntry($"Product {product.ProductId}: reject output activated ({MachineRejectMode})", IOLogLevel.Warning, "TRACK");
            var rejectPoint = product.InterventionStates.FirstOrDefault(p => string.Equals(p.ActionType, "Reject", StringComparison.OrdinalIgnoreCase));
            AddMachineOperationalEvent(
                "Reject",
                "REJECT_EXECUTED",
                "Reject executed",
                $"Reject output activated with mode {MachineRejectMode}.",
                "Executed",
                product.ProductId,
                BoundRejectSignalCode,
                rejectPoint?.AbsoluteMachineQuotaMm);
        }


        private bool UseInterventionPointRuntime => InterventionPoints.Any(point => point.Enabled);

        private bool IsTimedTriggerSchedulingActive =>
            string.Equals(TriggerSchedulingMode, "TimedFromPhotocell", StringComparison.OrdinalIgnoreCase);

        private bool ResolveUseEncoderTriggerFromLegacyMode(MachineRuntimeBindings bindings)
        {
            if (bindings == null)
            {
                return true;
            }

            if (bindings.UseEncoderTriggerSpecified)
            {
                return bindings.UseEncoderTrigger;
            }

            return string.Equals(bindings.TriggerSchedulingMode, "VirtualConveyor", StringComparison.OrdinalIgnoreCase) &&
                   !bindings.VirtualConveyorEnabled;
        }

        private void SyncUseEncoderTriggerFromLegacyMode()
        {
            if (_isSynchronizingTriggerSource)
            {
                return;
            }

            var useEncoder = string.Equals(_triggerSchedulingMode, "VirtualConveyor", StringComparison.OrdinalIgnoreCase) &&
                             !_virtualConveyorEnabled;
            if (_useEncoderTrigger == useEncoder)
            {
                return;
            }

            _isSynchronizingTriggerSource = true;
            _useEncoderTrigger = useEncoder;
            _isSynchronizingTriggerSource = false;
            OnPropertyChanged(nameof(UseEncoderTrigger));
            OnPropertyChanged(nameof(TriggerSourceSummary));
        }

        private void NotifyTriggerSourceProperties()
        {
            OnPropertyChanged(nameof(UseEncoderTrigger));
            OnPropertyChanged(nameof(VirtualConveyorEnabled));
            OnPropertyChanged(nameof(VirtualConveyorSummary));
            OnPropertyChanged(nameof(EncoderDiagnosticGuidance));
            OnPropertyChanged(nameof(TriggerSchedulingMode));
            OnPropertyChanged(nameof(TriggerSchedulingModeSummary));
            OnPropertyChanged(nameof(TriggerSourceSummary));
            OnPropertyChanged(nameof(IsTimedPhotocellMachineFlow));
            OnPropertyChanged(nameof(SignalAssignmentGuidanceSummary));
        }

        private bool IsSideLeftMultiShotEnabled()
        {
            return (_effectiveRuntimeConfiguration ?? _machineConfiguration)?.MachineMultiShotTrigger?.Side?.Enabled == true;
        }

        private bool IsSideLeftTriggerSignal(string signalCode)
        {
            if (string.IsNullOrWhiteSpace(signalCode))
            {
                return false;
            }

            return IsMultiShotTriggerSignal(signalCode);
        }

        private bool IsAnyMultiShotProfileEnabled()
        {
            return EnumerateMachineMultiShotProfiles()
                .Any(profile => profile.Value?.Enabled == true);
        }

        private bool IsMultiShotTriggerSignal(string signalCode)
        {
            if (string.IsNullOrWhiteSpace(signalCode))
            {
                return false;
            }

            foreach (var profile in EnumerateMachineMultiShotProfiles())
            {
                var options = profile.Value;
                if (options?.Enabled != true)
                {
                    continue;
                }

                if (string.Equals(signalCode, options.TriggerOutputName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (IsSignalCompatibleWithMultiShotProfile(signalCode, profile.Key, options))
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<KeyValuePair<string, MultiShotTriggerOptions>> EnumerateMachineMultiShotProfiles()
        {
            var configuration = (_effectiveRuntimeConfiguration ?? _machineConfiguration)?.MachineMultiShotTrigger;
            if (configuration == null)
            {
                yield break;
            }

            foreach (var profile in configuration.EnumerateProfiles())
            {
                yield return profile;
            }
        }

        private static bool IsSignalCompatibleWithMultiShotProfile(string signalCode, string profileKey, MultiShotTriggerOptions options)
        {
            var source = $"{profileKey} {options?.CameraRole} {options?.DisplayName}";
            if (source.IndexOf("RIGHT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("REAR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return signalCode.IndexOf("RIGHT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       signalCode.IndexOf("REAR", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (source.IndexOf("BOTTOM", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return signalCode.IndexOf("BOTTOM", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return signalCode.IndexOf("SIDE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   signalCode.IndexOf("LEFT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HasActiveCameraInterventionPoints => InterventionPoints.Any(point =>
            point.Enabled &&
            string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(point.SignalCode));

        private async Task ExecuteInterventionPointAsync(TrackedProduct product, ProductInterventionState state, string reason, DateTime? requestedAtUtc = null)
        {
            if (state == null)
            {
                return;
            }

            var action = state.ActionType ?? string.Empty;
            var signalCode = ResolveInterventionSignalCode(state);

            switch (action)
            {
                case "TriggerCamera":
                    if (ShouldSkipInternalTrigger(product, state))
                    {
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(signalCode))
                    {
                        AddLogEntry($"TriggerCamera point {state?.PointCode} skipped: SignalCode not configured. Assign a signal in the I/O configuration panel.", IOLogLevel.Warning, "TRACK");
                        break;
                    }
                    var resolvedTriggerSignalCode = signalCode;
                    var resolvedTriggerSignal = ResolveOutputSignalByCode(resolvedTriggerSignalCode);
                    string pointRole = ActiveVppCameraTriggerGate.NormalizeRole(state?.PointCode);
                    string signalRole = ActiveVppCameraTriggerGate.NormalizeRole(resolvedTriggerSignalCode);
                    string triggerRole = ResolveCameraRoleForTrigger(state, resolvedTriggerSignalCode);
                    if ((pointRole != null && signalRole != null &&
                         !string.Equals(pointRole, signalRole, StringComparison.OrdinalIgnoreCase)) ||
                        !IsActiveVppCameraRole(triggerRole))
                    {
                        AddLogEntry($"VPP_CAMERA_TRIGGER_SKIPPED|point={state?.PointCode}|signal={resolvedTriggerSignalCode}|role={triggerRole}|reason=absent-or-conflicting-role",
                            IOLogLevel.Warning, "TRACK");
                        break;
                    }
                    if (IsAnyMultiShotProfileEnabled() && IsSideLeftTriggerSignal(resolvedTriggerSignalCode))
                    {
                        AddLogEntry(
                            $"Single camera trigger skipped for product {product?.ProductId}: MultiShot controller owns {resolvedTriggerSignalCode}.",
                            IOLogLevel.Info,
                            "MULTISHOT");
                        break;
                    }

                    if (!TryRegisterTriggerOutputForProduct(product, resolvedTriggerSignal, state))
                    {
                        return;
                    }

                    var lateDetails = state.ReachedEncoderCount > 0
                        ? $" | target={state.TargetEncoderCount}, actual={state.ReachedEncoderCount}, late=+{state.ReachedLateCounts} counts ({state.ReachedLateMm:0.0} mm)"
                        : string.Empty;
                    var triggerLogLevel = state.ReachedLateMm > CameraTriggerLateWarningMm
                        ? IOLogLevel.Warning
                        : IOLogLevel.Info;

                    CameraTriggerTicket supersededTicket = CameraTriggerCorrelationTracker.Register(
                        triggerRole,
                        product?.ProductId ?? 0,
                        state.PointCode,
                        resolvedTriggerSignalCode);
                    if (supersededTicket != null)
                    {
                        AddLogEntry(
                            $"VISION_TRIGGER_TICKET_REALIGNED|role={triggerRole}|discardedProduct={supersededTicket.ProductId}" +
                            $"|newProduct={product?.ProductId ?? 0}|reason=previous trigger produced no claimable result",
                            IOLogLevel.Warning,
                            "TRACK");
                    }
                    CameraDiagnostics.TriggerPointReached(
                        triggerRole,
                        product?.ProductId ?? 0,
                        state.PointCode,
                        resolvedTriggerSignalCode,
                        state.TargetEncoderCount,
                        state.ReachedEncoderCount,
                        state.ReachedLateCounts,
                        state.ReachedLateMm);
                    CameraDiagnostics.TicketRegistered(
                        triggerRole,
                        product?.ProductId ?? 0,
                        state.PointCode,
                        supersededTicket?.ProductId);
                    bool edgeGenerated = await PulseMappedOutputAsync(
                        resolvedTriggerSignalCode,
                        state.PulseMs > 0 ? state.PulseMs : 40,
                        reason,
                        requestedAtUtc,
                        triggerRole,
                        product?.ProductId ?? 0);
                    if (!edgeGenerated)
                    {
                        // Nessun fronte in scheda: il ticket non riceverebbe mai un'immagine
                        // legittima e resterebbe esposto a un risultato non richiesto.
                        bool ticketWithdrawn = CameraTriggerCorrelationTracker.Withdraw(
                            triggerRole,
                            product?.ProductId ?? 0,
                            state.PointCode);
                        CameraDiagnostics.TicketWithdrawn(
                            triggerRole,
                            product?.ProductId ?? 0,
                            state.PointCode,
                            ticketWithdrawn);
                        AddLogEntry(
                            $"TRIGGER_NOT_EXECUTED|product={product?.ProductId}|point={state?.PointCode}" +
                            $"|signal={resolvedTriggerSignalCode}|ticketWithdrawn={ticketWithdrawn}{lateDetails}",
                            IOLogLevel.Error,
                            "TRACK");
                        break;
                    }

                    AddLogEntry(
                        $"Trigger request executed for product {product?.ProductId}: point {state?.PointCode} -> {resolvedTriggerSignalCode} [{resolvedTriggerSignal?.Board}/{resolvedTriggerSignal?.Channel}] at {state?.AbsoluteMachineQuotaMm:0.0} mm{lateDetails}",
                        triggerLogLevel,
                        "TRACK");
                    break;

                case "Reject":
                    if (product?.IsAccepted == false)
                    {
                        await ExecuteRejectForProductAsync(product, reason);
                    }
                    break;

                case "AlarmOn":
                    await SetMappedOutputAsync(string.IsNullOrWhiteSpace(signalCode) ? BoundGeneralAlarmSignalCode : signalCode, true, reason);
                    break;

                case "AlarmOff":
                    await SetMappedOutputAsync(string.IsNullOrWhiteSpace(signalCode) ? BoundGeneralAlarmSignalCode : signalCode, false, reason);
                    break;

                case "OutputOn":
                    if (!string.IsNullOrWhiteSpace(signalCode))
                    {
                        await SetMappedOutputAsync(signalCode, true, reason);
                    }
                    break;

                case "OutputOff":
                    if (!string.IsNullOrWhiteSpace(signalCode))
                    {
                        await SetMappedOutputAsync(signalCode, false, reason);
                    }
                    break;

                case "OutputPulse":
                    if (!string.IsNullOrWhiteSpace(signalCode))
                    {
                        await PulseMappedOutputAsync(signalCode, state.PulseMs > 0 ? state.PulseMs : 40, reason);
                    }
                    break;

                case "Marker":
                    AddLogEntry($"Product {product?.ProductId}: marker {state.PointCode} reached", IOLogLevel.Info, "TRACK");
                    break;
            }
        }

        private string ResolveInterventionSignalCode(ProductInterventionState state)
        {
            if (!string.IsNullOrWhiteSpace(state?.SignalCode))
            {
                return state.SignalCode;
            }

            switch (state?.ActionType)
            {
                case "TriggerCamera":
                    // A SIDE-named point with no explicit signal must not fall back to the
                    // TOP trigger output. Return empty so the executor skips it rather than
                    // double-pulsing OUT_CAMERA_TOP_TRIGGER for the same product.
                    if (state.PointCode?.IndexOf("SIDE", StringComparison.OrdinalIgnoreCase) >= 0)
                        return string.Empty;
                    return BoundCameraTriggerSignalCode;
                case "Reject": return BoundRejectSignalCode;
                case "AlarmOn":
                case "AlarmOff": return BoundGeneralAlarmSignalCode;
                default: return string.Empty;
            }
        }

        private string ResolveDefaultSignalCodeForAction(string actionType)
        {
            switch (actionType)
            {
                case "TriggerCamera":
                    return BoundCameraTriggerSignalCode;
                case "Reject":
                    return BoundRejectSignalCode;
                case "AlarmOn":
                case "AlarmOff":
                    return BoundGeneralAlarmSignalCode;
                default:
                    return string.Empty;
            }
        }

        private MachineInterventionPoint ResolveTimedTriggerInterventionPoint(string pointMarker)
        {
            return InterventionPoints.FirstOrDefault(point =>
                point.Enabled &&
                string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase) &&
                point.PointCode?.IndexOf(pointMarker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private MachineInterventionPoint GetOrCreateTimedTriggerInterventionPoint(string pointMarker)
        {
            var existing = InterventionPoints.FirstOrDefault(point =>
                string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase) &&
                point.PointCode?.IndexOf(pointMarker, StringComparison.OrdinalIgnoreCase) >= 0);
            if (existing != null)
            {
                return existing;
            }

            bool isTop = string.Equals(pointMarker, "TOP", StringComparison.OrdinalIgnoreCase);
            bool isRear = string.Equals(pointMarker, "REAR", StringComparison.OrdinalIgnoreCase);
            var defaultSignalCode = isTop ? "OUT_CAMERA_TOP_TRIGGER"
                : isRear ? "OUT_CAMERA_REAR_TRIGGER" : "OUT_CAMERA_SIDE_TRIGGER";
            var defaultOffset = isTop ? 74.0 : 76.0;
            var pointCode = isTop ? "CAMERA_TRIGGER_TOP"
                : isRear ? "CAMERA_TRIGGER_REAR" : "CAMERA_TRIGGER_SIDE";

            var created = CreateStandardInterventionPoint(pointCode, defaultOffset, "TriggerCamera", defaultSignalCode, 40, true);
            InterventionPoints.Add(created);
            return created;
        }

        private PhotocellTimedTriggerOutputRequest CreateTimedTriggerOutputRequest(string outputName, string signalCode, int delayMs, int pulseMs)
        {
            var signal = ResolveOutputSignalByCode(signalCode);
            var channel = ParseChannelNumber(signal?.Channel);
            var hasResolvedOutput = signal != null && Pcie1756ChannelMap.IsValidOutputChannel(channel) && !string.IsNullOrWhiteSpace(signalCode);

            return new PhotocellTimedTriggerOutputRequest
            {
                OutputName = outputName,
                SignalCode = signalCode,
                ChannelNumber = channel,
                Board = signal?.Board,
                PhysicalChannel = signal?.Channel,
                ActiveElectricalState = ToElectricalOutputState(signal, true),
                InactiveElectricalState = ToElectricalOutputState(signal, false),
                DelayMs = delayMs,
                PulseMs = pulseMs,
                Enabled = hasResolvedOutput && IsActiveVppCameraRole(outputName)
            };
        }

        private PhotocellTimedTriggerBatchRequest BuildTimedTriggerBatchRequest(MachineSignalDefinition mappedSignal, long encoderCount, DateTime? sourceEventTimestampUtc = null)
        {
            var topSignalCode = BoundTopCameraTriggerSignalCode;
            var sideSignalCode = BoundSideCameraTriggerSignalCode;
            var sideOutput = CreateTimedTriggerOutputRequest("SIDE", sideSignalCode, SideTriggerBaseDelayMs, SideTriggerPulseMs);
            if (IsSideLeftMultiShotEnabled())
            {
                sideOutput.Enabled = false;
            }

            return new PhotocellTimedTriggerBatchRequest
            {
                SourceSignalCode = mappedSignal?.SignalCode ?? BoundProductPhotocellSignalCode,
                SourceEncoderCount = encoderCount,
                SourceMachineQuotaMm = MainEncoderTemplate?.PhotocellMachineOffsetMm,
                SourceEventTimestampUtc = sourceEventTimestampUtc,
                Outputs = new[]
                {
                    CreateTimedTriggerOutputRequest("TOP", topSignalCode, TopTriggerBaseDelayMs, TopTriggerPulseMs),
                    sideOutput
                }
            };
        }

        private void QueueTimedPhotocellTriggerBatch(MachineSignalDefinition mappedSignal, long encoderCount, DateTime? sourceEventTimestampUtc = null)
        {
            var batch = BuildTimedTriggerBatchRequest(mappedSignal, encoderCount, sourceEventTimestampUtc);
            QueueTimedPhotocellTriggerBatch(batch, true);
        }

        private void QueueTimedPhotocellTriggerBatch(PhotocellTimedTriggerBatchRequest batch, bool logRouteBeforeQueue)
        {
            var hasActiveOutputs = batch.Outputs.Any(output => output.Enabled && !string.IsNullOrWhiteSpace(output.SignalCode) && output.ChannelNumber >= 0);
            if (!hasActiveOutputs)
            {
                AddLogEntry("TimedFromPhotocell selected but no active TOP/SIDE trigger outputs are configured.", IOLogLevel.Warning, "TRACK");
                TimedTriggerRuntimeStatus = T("Tool_VM_TimedTriggerNoOutputs", "Timed trigger idle: no active TOP/SIDE outputs configured.");
                return;
            }

            var routeSummary = string.Join(" | ", batch.Outputs
                .Where(output => output != null)
                .Select(output =>
                {
                    var physical = !string.IsNullOrWhiteSpace(output.Board) || !string.IsNullOrWhiteSpace(output.PhysicalChannel)
                        ? $"{output.Board}/{output.PhysicalChannel}"
                        : "unresolved";
                    if (string.Equals(physical, "unresolved", StringComparison.OrdinalIgnoreCase) &&
                        Application.Current.Dispatcher.CheckAccess())
                    {
                        var resolved = ResolveOutputSignalByCode(output.SignalCode);
                        physical = resolved == null
                            ? "unresolved"
                            : $"{resolved.Board}/{resolved.Channel}";
                    }

                    return $"{output.OutputName}: enabled={output.Enabled} signal={output.SignalCode} physical={physical} channel={output.ChannelNumber} delay={output.DelayMs}ms pulse={output.PulseMs}ms";
                }));

            if (logRouteBeforeQueue)
            {
                AddLogEntry(
                    $"Timed trigger route prepared from {batch.SourceSignalCode}: {routeSummary}",
                    IOLogLevel.Info,
                    "TRIGGER-TIMED");
            }
            else
            {
                Logger.Info($"[TRIGGER-TIMED] Timed trigger fast-path route prepared from {batch.SourceSignalCode}: {routeSummary}");
            }

            var batchId = _timedTriggerService.QueueBatch(batch, ExecuteTimedTriggerOutputAsync);
            _ = Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                LastTrackedProductStatus = string.Format(
                    T("Tool_VM_TimedTriggerBatchQueued", "Timed trigger batch {0} queued from photocell {1}."),
                    batchId,
                    batch.SourceSignalCode);
                TimedTriggerRuntimeStatus = string.Format(
                    T("Tool_VM_TimedTriggerBatchStatus", "Timed trigger batch {0} queued from photocell {1}."),
                    batchId,
                    batch.SourceSignalCode);
                AddMachineOperationalEvent(
                    "Trigger",
                    "TIMED_TRIGGER_BATCH_QUEUED",
                    "Timed trigger batch queued",
                    $"Photocell {batch.SourceSignalCode} queued deterministic TOP/SIDE trigger outputs. {routeSummary}",
                    "Queued",
                    batchId,
                    batch.SourceSignalCode,
                    batch.SourceMachineQuotaMm);
            }));
        }

        private Task ExecuteTimedTriggerOutputAsync(PhotocellTimedTriggerOutputRequest request)
        {
            if (request == null || !IsActiveVppCameraRole(request.OutputName))
            {
                return Task.CompletedTask;
            }
            if (request?.ChannelNumber >= 0)
            {
                return PulseResolvedTimedOutputAsync(request, $"timed photocell trigger {request.OutputName}");
            }

            // Fallback only for old/incomplete configurations. Normal timed mode uses
            // the pre-resolved channel snapshot and never waits for the UI dispatcher.
            return Application.Current.Dispatcher.InvokeAsync(
                () => PulseMappedOutputAsync(
                    request?.SignalCode,
                    request?.PulseMs > 0 ? request.PulseMs : 40,
                    $"timed photocell trigger {request?.OutputName ?? "UNKNOWN"}",
                    cameraRole: request?.OutputName)).Task.Unwrap();
        }

        private async Task PulseResolvedTimedOutputAsync(PhotocellTimedTriggerOutputRequest request, string reason)
        {
            var channel = request.ChannelNumber;
            var pulseMs = Math.Max(20, request.PulseMs > 0 ? request.PulseMs : 40);

            SemaphoreSlim channelSemaphore;
            lock (_outputChannelSemaphoresLock)
            {
                if (!_outputChannelSemaphores.TryGetValue(channel, out channelSemaphore))
                {
                    channelSemaphore = new SemaphoreSlim(1, 1);
                    _outputChannelSemaphores[channel] = channelSemaphore;
                }
            }

            await channelSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsActiveVppCameraRole(request.OutputName))
                {
                    return;
                }
                Logger.Info($"[TRIGGER-TIMED] Pulse START {request.SignalCode} [{request.Board}/{request.PhysicalChannel}] channel={channel} width={pulseMs} ms | reason={reason}");
                var pulseResult = await _ioManager.PulseOutputAsync(
                    channel,
                    request.ActiveElectricalState,
                    request.InactiveElectricalState,
                    pulseMs).ConfigureAwait(false);
                PostOutputStateUpdate(channel, request.InactiveElectricalState);
                Logger.Info($"[TRIGGER-TIMED] Pulse END {request.SignalCode} [{request.Board}/{request.PhysicalChannel}] channel={channel} requested={pulseMs} ms actual={pulseResult.ActualHighMilliseconds:0.###} ms | reason={reason}");
            }
            finally
            {
                channelSemaphore.Release();
            }
        }

        private async Task WriteTimedOutputElectricalAsync(int channel, bool electricalState)
        {
            await _directOutputWriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _ioManager.WriteOutputAsync(channel, electricalState).ConfigureAwait(false);
            }
            finally
            {
                _directOutputWriteLock.Release();
            }
        }

        private void PostOutputStateUpdate(int channel, bool electricalState)
        {
            Application.Current.Dispatcher.BeginInvoke(new System.Action(() => ApplyOutputState(channel, electricalState)));
        }

        private void OnTimedTriggerServiceEventRaised(object sender, PhotocellTimedTriggerEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            // Log via ApplicationEventLogger (EventAudit NLog target + database) on the background
            // thread - no UI dispatch needed. Same path used by all other operational events.
            var appLogger = ServiceLocator.ApplicationEventLogger;
            switch (e.Kind)
            {
                case PhotocellTimedTriggerEventKind.EdgeAccepted:
                {
                    var interArrival = _lastInterArrivalMs;
                    var speedFactor = _lastSpeedCompensationFactor;
                    var compensationActive = Math.Abs(speedFactor - 1.0) > 0.002;
                    appLogger.LogOperationalEvent(
                        LogLevel.Info,
                        "TRIGGER_PHOTOCELL_EDGE",
                        "Trigger",
                        $"Photocell edge accepted - batch {e.BatchId}  interArrival={interArrival:0.0} ms  speedFactor={speedFactor:0.000}{(compensationActive ? " [COMPENSATED]" : "")}",
                        nameof(DigitalIOViewModel),
                        e.Message,
                        new Dictionary<string, object>
                        {
                            { "batch_id", e.BatchId },
                            { "signal_code", e.SignalCode },
                            { "dispatch_ms", Math.Round(e.CompensationMs, 2) },
                            { "inter_arrival_ms", Math.Round(interArrival, 1) },
                            { "speed_factor", Math.Round(speedFactor, 4) },
                            { "speed_compensation_active", compensationActive }
                        });
                    break;
                }

                case PhotocellTimedTriggerEventKind.OutputOn:
                {
                    var deltaMs = e.TotalEdgeToOutputMs - e.DelayMs;
                    appLogger.LogOperationalEvent(
                        Math.Abs(deltaMs) > 10 ? LogLevel.Warn : LogLevel.Info,
                        $"TRIGGER_OUTPUT_RAISED_{e.OutputName?.ToUpperInvariant() ?? "OUT"}",
                        "Trigger",
                        $"{e.OutputName} trigger raised - edgeToOutput={e.TotalEdgeToOutputMs:0.0} ms  target={e.DelayMs} ms  delta={deltaMs:+0.0;-0.0;0.0} ms",
                        nameof(DigitalIOViewModel),
                        $"dispatch={e.CompensationMs:0.0} ms  wait={e.ElapsedMs:0.0} ms  signal={e.SignalCode}  batch={e.BatchId}",
                        new Dictionary<string, object>
                        {
                            { "batch_id", e.BatchId },
                            { "output_name", e.OutputName },
                            { "signal_code", e.SignalCode },
                            { "target_delay_ms", e.DelayMs },
                            { "edge_to_output_ms", Math.Round(e.TotalEdgeToOutputMs, 2) },
                            { "delta_ms", Math.Round(deltaMs, 2) },
                            { "dispatch_ms", Math.Round(e.CompensationMs, 2) },
                            { "wait_ms", Math.Round(e.ElapsedMs, 2) }
                        });
                    break;
                }

                case PhotocellTimedTriggerEventKind.OutputSkipped:
                    appLogger.LogOperationalEvent(
                        LogLevel.Warn,
                        "TRIGGER_OUTPUT_SKIPPED",
                        "Trigger",
                        $"Trigger output skipped - {e.OutputName}",
                        nameof(DigitalIOViewModel),
                        e.Message,
                        new Dictionary<string, object> { { "batch_id", e.BatchId }, { "output_name", e.OutputName } });
                    break;

                case PhotocellTimedTriggerEventKind.Error:
                    appLogger.LogOperationalEvent(
                        LogLevel.Error,
                        "TRIGGER_ERROR",
                        "Trigger",
                        "Timed trigger error",
                        nameof(DigitalIOViewModel),
                        e.Message,
                        new Dictionary<string, object> { { "batch_id", e.BatchId } });
                    break;
            }

            Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                switch (e.Kind)
                {
                    case PhotocellTimedTriggerEventKind.EdgeAccepted:
                        _lastTimedPhotocellEdgeLocalTime = DateTime.Now;
                        _timedBatchesQueuedCount++;
                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Info, "TRIGGER-TIMED");
                        break;
                    case PhotocellTimedTriggerEventKind.OutputScheduled:
                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Info, "TRIGGER-TIMED");
                        break;
                    case PhotocellTimedTriggerEventKind.OutputOn:
                        if (string.Equals(e.OutputName, "TOP", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastTimedTopTriggerLocalTime = DateTime.Now;
                            _lastTimedTopActualDelayMs = e.TotalEdgeToOutputMs;
                            _timedTopDelaySumMs += e.ElapsedMs;
                            _timedTopShotsCount++;
                        }
                        else if (string.Equals(e.OutputName, "SIDE", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastTimedSideTriggerLocalTime = DateTime.Now;
                            _lastTimedSideActualDelayMs = e.TotalEdgeToOutputMs;
                            _timedSideDelaySumMs += e.TotalEdgeToOutputMs;
                            _timedSideShotsCount++;
                        }

                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Success, "TRIGGER-TIMED");
                        AddMachineOperationalEvent(
                            "Trigger",
                            $"TIMED_{e.OutputName}_TRIGGER_ON",
                            $"Timed {e.OutputName} trigger ON",
                            $"edgeToOutput={e.TotalEdgeToOutputMs:0.0} ms (dispatch={e.CompensationMs:0.0} ms + wait={e.ElapsedMs:0.0} ms) | target={e.DelayMs} ms | delta={(e.TotalEdgeToOutputMs - e.DelayMs):+0.0;-0.0;0.0} ms",
                            "Executed",
                            e.BatchId,
                            e.SignalCode,
                            null);
                        break;
                    case PhotocellTimedTriggerEventKind.OutputOff:
                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Info, "TRIGGER-TIMED");
                        break;
                    case PhotocellTimedTriggerEventKind.OutputSkipped:
                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Warning, "TRIGGER-TIMED");
                        break;
                    case PhotocellTimedTriggerEventKind.BatchCancelled:
                        _timedBatchesCancelledCount++;
                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Warning, "TRIGGER-TIMED");
                        break;
                    case PhotocellTimedTriggerEventKind.BatchCompleted:
                        _timedBatchesCompletedCount++;
                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Info, "TRIGGER-TIMED");
                        break;
                    case PhotocellTimedTriggerEventKind.Error:
                        _timedTriggerErrorCount++;
                        TimedTriggerRuntimeStatus = e.Message;
                        AddLogEntry(e.Message, IOLogLevel.Error, "TRIGGER-TIMED");
                        break;
                }

                OnPropertyChanged(nameof(TimedTriggerLastEventsSummary));
                OnPropertyChanged(nameof(TimedTriggerMetricsSummary));
                OnPropertyChanged(nameof(TimedTriggerObservedDelaySummary));
                OnPropertyChanged(nameof(TimedTriggerOutputSummary));
            }));
        }

        private void CancelPendingTimedTriggerBatches(string reason)
        {
            _timedTriggerService.CancelAll(reason);
        }

        private void CancelMultiShotSession(string reason)
        {
            _multiShotTriggerController?.CancelActiveSession(reason);
        }

        private void OnContinuousRunStateChanged(object sender, bool isRunning)
        {
            if (!isRunning)
            {
                CancelMultiShotSession("continuous run stopped");
            }
        }

        private void OnMultiShotTriggerEventRaised(object sender, MultiShotTriggerEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            // Phase state must be updated before UI dispatch because the next
            // product can arrive while the dispatcher is still busy.
            MainWindow.HandleMultiShotCameraPhaseEvent(e);

            Application.Current?.Dispatcher?.BeginInvoke(new System.Action(() =>
            {
                var level = e.Kind == MultiShotTriggerEventKind.ConfigError ||
                            e.Kind == MultiShotTriggerEventKind.SessionTimeout ||
                            e.Kind == MultiShotTriggerEventKind.TriggerFailed ||
                            e.Kind == MultiShotTriggerEventKind.Cancelled ||
                            e.Kind == MultiShotTriggerEventKind.TargetTooLate
                    ? IOLogLevel.Warning
                    : IOLogLevel.Info;

                AddLogEntry(e.Message, level, "MULTISHOT");
            }));
        }

        private void DisposeMultiShotController()
        {
            if (_multiShotTriggerController == null)
            {
                return;
            }

            _multiShotTriggerController.EventRaised -= OnMultiShotTriggerEventRaised;
            _multiShotTriggerController.Dispose();
            _multiShotTriggerController = null;
        }

        private void ClearTimedTriggerDiagnostics()
        {
            _lastTimedPhotocellEdgeLocalTime = null;
            _lastTimedTopTriggerLocalTime = null;
            _lastTimedSideTriggerLocalTime = null;
            _lastTimedTopActualDelayMs = null;
            _lastTimedSideActualDelayMs = null;
            _timedTopDelaySumMs = 0.0;
            _timedSideDelaySumMs = 0.0;
            _timedBatchesQueuedCount = 0;
            _timedBatchesCompletedCount = 0;
            _timedBatchesCancelledCount = 0;
            _timedTriggerErrorCount = 0;
            _timedTopShotsCount = 0;
            _timedSideShotsCount = 0;
            _lastFastTimedPhotocellUtc = DateTime.MinValue;
            _lastSpeedCompensationFactor = 1.0;
            _lastInterArrivalMs = 0.0;
            lock (_interArrivalBufferLock)
                _photocellInterArrivalBuffer.Clear();
            TimedTriggerRuntimeStatus = T("Tool_VM_TimedTriggerIdle", "Timed trigger idle");
            OnPropertyChanged(nameof(TimedTriggerLastEventsSummary));
            OnPropertyChanged(nameof(TimedTriggerMetricsSummary));
            OnPropertyChanged(nameof(TimedTriggerObservedDelaySummary));
            OnPropertyChanged(nameof(TimedTriggerOutputSummary));
        }

        private void RebuildTimedTriggerFastPathSnapshot()
        {
            TimedTriggerFastPathSnapshot snapshot;

            if (!IsTimedTriggerSchedulingActive)
            {
                snapshot = TimedTriggerFastPathSnapshot.Disabled;
            }
            else
            {
                var photocellSignal = ResolveInputSignalByCode(BoundProductPhotocellSignalCode);
                var photocellChannel = ParseChannelNumber(photocellSignal?.Channel);
                var top = CreateTimedTriggerOutputRequest("TOP", BoundTopCameraTriggerSignalCode, TopTriggerBaseDelayMs, TopTriggerPulseMs);
                var side = CreateTimedTriggerOutputRequest("SIDE", BoundSideCameraTriggerSignalCode, SideTriggerBaseDelayMs, SideTriggerPulseMs);
                var outputs = new[] { top, side };
                var activeOutputs = outputs
                    .Where(output => output.Enabled && output.ChannelNumber >= 0 && !string.IsNullOrWhiteSpace(output.SignalCode))
                    .ToList();

                var routeSummary = string.Join(" | ", outputs.Select(output =>
                    $"{output.OutputName}: enabled={output.Enabled} signal={output.SignalCode} physical={output.Board}/{output.PhysicalChannel} channel={output.ChannelNumber} delay={output.DelayMs}ms pulse={output.PulseMs}ms"));

                snapshot = new TimedTriggerFastPathSnapshot
                {
                    Enabled = photocellChannel >= 0 && activeOutputs.Count > 0,
                    ProductPhotocellChannel = photocellChannel,
                    ProductPhotocellActiveElectricalState = photocellSignal?.Polarity == MachineSignalPolarity.ActiveLow ? false : true,
                    ProductPhotocellSignalCode = photocellSignal?.SignalCode ?? BoundProductPhotocellSignalCode,
                    SourceMachineQuotaMm = MainEncoderTemplate?.PhotocellMachineOffsetMm,
                    DebounceMs = Math.Max(0, Math.Max(PhotocellDebounceMs, MinimumRetriggerGapMs)),
                    Outputs = outputs,
                    RouteSummary = routeSummary
                };
            }

            lock (_timedTriggerFastPathLock)
            {
                _timedTriggerFastPath = snapshot;
            }
        }

        private bool TryHandleTimedPhotocellFastPath(IOEvent e)
        {
            TimedTriggerFastPathSnapshot snapshot;
            lock (_timedTriggerFastPathLock)
            {
                snapshot = _timedTriggerFastPath;
            }

            if (snapshot == null || !snapshot.Enabled || e == null || e.Channel != snapshot.ProductPhotocellChannel)
            {
                return false;
            }

            var logicalState = e.NewState == snapshot.ProductPhotocellActiveElectricalState;
            if (!logicalState)
            {
                return false;
            }

            var sourceTimestampUtc = NormalizeEventTimestampUtc(e.Timestamp);
            var elapsedFromLastAcceptedMs = (sourceTimestampUtc - _lastFastTimedPhotocellUtc).TotalMilliseconds;
            if (_lastFastTimedPhotocellUtc != DateTime.MinValue &&
                snapshot.DebounceMs > 0 &&
                elapsedFromLastAcceptedMs >= 0 &&
                elapsedFromLastAcceptedMs < snapshot.DebounceMs)
            {
                Logger.Warn($"[TRIGGER-TIMED] FAST_PATH_PHOTOCELL_IGNORED source={snapshot.ProductPhotocellSignalCode} channel={snapshot.ProductPhotocellChannel} elapsed={elapsedFromLastAcceptedMs:0.0} ms debounce={snapshot.DebounceMs} ms");
                return true;
            }

            _lastInterArrivalMs = elapsedFromLastAcceptedMs;
            _lastSpeedCompensationFactor = 1.0;
            _lastFastTimedPhotocellUtc = sourceTimestampUtc;

            var batch = new PhotocellTimedTriggerBatchRequest
            {
                SourceSignalCode = snapshot.ProductPhotocellSignalCode,
                SourceEncoderCount = ResolveEventMainEncoderCount(e),
                SourceMachineQuotaMm = snapshot.SourceMachineQuotaMm,
                SourceEventTimestampUtc = sourceTimestampUtc,
                Outputs = snapshot.Outputs
            };

            Logger.Info($"[TRIGGER-TIMED] FAST_PATH_PHOTOCELL_ACCEPTED source={snapshot.ProductPhotocellSignalCode} channel={snapshot.ProductPhotocellChannel} interArrival={elapsedFromLastAcceptedMs:0.0} ms timestampUtc={sourceTimestampUtc:O}");
            QueueTimedPhotocellTriggerBatch(batch, false);
            return true;
        }

        private static DateTime NormalizeEventTimestampUtc(DateTime timestamp)
        {
            if (timestamp == default(DateTime))
            {
                return DateTime.UtcNow;
            }

            if (timestamp.Kind == DateTimeKind.Utc)
            {
                return timestamp;
            }

            return timestamp.ToUniversalTime();
        }

        private void ValidateTriggerExecutionConfiguration()
        {
            var issues = new List<string>();

            if (UseEncoderTrigger && IsTimedTriggerSchedulingActive)
            {
                issues.Add("UseEncoderTrigger=true but TriggerSchedulingMode=TimedFromPhotocell. Real encoder trigger mode requires TriggerSchedulingMode=VirtualConveyor and VirtualConveyorEnabled=false.");
            }

            if (!UseEncoderTrigger && IsAnyMultiShotProfileEnabled())
            {
                issues.Add("MultiShot is enabled but UseEncoderTrigger=false. MultiShot requires encoder-tracked trigger mode; disable MultiShot or enable real encoder trigger.");
            }

            if (!UseEncoderTrigger &&
                !string.Equals(TriggerSchedulingMode, "TimedFromPhotocell", StringComparison.OrdinalIgnoreCase) &&
                !VirtualConveyorEnabled)
            {
                issues.Add("Trigger source is mixed: UseEncoderTrigger=false but legacy settings are TriggerSchedulingMode=VirtualConveyor and VirtualConveyorEnabled=false. Select real encoder trigger or timed trigger again from the panel.");
            }

            if (string.Equals(TriggerSchedulingMode, "TimedFromPhotocell", StringComparison.OrdinalIgnoreCase))
            {
                if (TopTriggerBaseDelayMs > 60000 || SideTriggerBaseDelayMs > 60000)
                {
                    issues.Add($"Timed trigger delays are unusually large: TOP={TopTriggerBaseDelayMs} ms, SIDE={SideTriggerBaseDelayMs} ms. Check unit conversion before testing on machine.");
                }

                var topPoint = ResolveTimedTriggerInterventionPoint("TOP");
                var sidePoint = ResolveTimedTriggerInterventionPoint("SIDE");
                if (topPoint == null || string.IsNullOrWhiteSpace(topPoint.SignalCode))
                {
                    issues.Add("Timed trigger TOP output is not fully configured.");
                }

                if (sidePoint == null || string.IsNullOrWhiteSpace(sidePoint.SignalCode))
                {
                    issues.Add("Timed trigger SIDE output is not fully configured.");
                }
            }

            if (string.Equals(BoundExternalTriggerEnableSignalCode, BoundProductPhotocellSignalCode, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"External trigger enable is mapped to the same signal as the product photocell ({BoundProductPhotocellSignalCode}).");
            }

            // Una riga senza SignalCode e' incompleta. Una riga con SignalCode ma senza canale e'
            // soltanto "non assegnata" (tipicamente predisposta dal VPP): segnala solo se un punto
            // abilitato la usa, altrimenti ogni avvio ripeterebbe un warning su uscite non usate.
            var invalidOutputs = MachineOutputs
                .Where(output => output != null && string.IsNullOrWhiteSpace(output.SignalCode))
                .ToList();
            if (invalidOutputs.Count > 0)
            {
                issues.Add($"Machine outputs contain {invalidOutputs.Count} incomplete row(s) without SignalCode.");
            }

            foreach (var point in InterventionPoints.Where(point => point != null && point.Enabled && !string.IsNullOrWhiteSpace(point.SignalCode)))
            {
                var resolved = ResolveOutputSignalByCode(point.SignalCode);
                if (resolved == null || !Pcie1756ChannelMap.IsValidOutputChannel(ParseChannelNumber(resolved.Channel)))
                {
                    issues.Add($"Enabled intervention point '{point.PointCode}' uses output '{point.SignalCode}' without an assigned channel: select the channel in the I/O page or disable the point.");
                }
            }

            // Stesso canale fisico usato da piu' segnali diversi. RIGHT e REAR
            // sono camere distinte e devono avere canali DO diversi.
            var sharedOutputChannels = MachineOutputs
                .Where(output => output != null && !string.IsNullOrWhiteSpace(output.SignalCode))
                .Select(output => new { Signal = output, Channel = ParseChannelNumber(output.Channel) })
                .Where(item => Pcie1756ChannelMap.IsValidOutputChannel(item.Channel))
                .GroupBy(item => item.Channel)
                .Select(group => new
                {
                    Channel = group.Key,
                    Codes = group
                        .Select(item => NormalizeSignalCode(item.Signal.SignalCode))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .Where(group => group.Codes
                    .Select(code => GetCompatibleOutputSignalCodes(code).OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase).First())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() > 1)
                .ToList();
            foreach (var group in sharedOutputChannels)
            {
                issues.Add($"Output channel DO{group.Channel:D2} is assigned to {group.Codes.Count} different signals ({string.Join(", ", group.Codes)}): the same terminal would be driven by different functions.");
            }

            // Output rows sharing the same SignalCode: the runtime drives only the row chosen by
            // ResolveOutputSignalByCode; the others stay visible in grids/combos. Log only.
            var duplicateOutputGroups = MachineOutputs
                .Where(output => output != null && !string.IsNullOrWhiteSpace(output.SignalCode))
                .GroupBy(output => NormalizeSignalCode(output.SignalCode), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();
            foreach (var group in duplicateOutputGroups)
            {
                var rows = group.ToList();
                var resolved = ResolveOutputSignalByCode(group.Key);
                var resolvedIndex = rows.FindIndex(row => ReferenceEquals(row, resolved));
                var rowsText = string.Join(", ", rows.Select((row, index) => $"[{index + 1}] {FormatSignalBoardChannel(row)}"));
                string resolvedText;
                if (resolved == null)
                {
                    resolvedText = "none";
                }
                else if (resolvedIndex >= 0)
                {
                    resolvedText = $"row [{resolvedIndex + 1}] {FormatSignalBoardChannel(resolved)}";
                }
                else
                {
                    resolvedText = $"{resolved.SignalCode} {FormatSignalBoardChannel(resolved)} (compatible alias)";
                }

                issues.Add($"Machine outputs define SignalCode '{group.Key}' {rows.Count} times ({rowsText}); runtime (ResolveOutputSignalByCode) uses {resolvedText}. Keep one output row only.");
            }

            // PCIE-1756 rows mapped on channels 32..63: accepted by the old 64-channel guards, but the
            // board has only 32 DI + 32 DO (4 + 4 ports of 8 bit). Log only.
            const int legacyPcie1756ChannelSpace = 64;
            var outOfRangePcie1756Rows = MachineInputs
                .Concat(MachineOutputs)
                .Where(signal => signal != null &&
                    !string.IsNullOrWhiteSpace(signal.Board) &&
                    signal.Board.IndexOf("1756", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(signal => new { Signal = signal, Channel = ParseChannelNumber(signal.Channel) })
                .Where(item => item.Channel >= Pcie1756ChannelMap.DoChannelCount && item.Channel < legacyPcie1756ChannelSpace)
                .ToList();
            foreach (var item in outOfRangePcie1756Rows)
            {
                var allowedRange = item.Signal.Direction == MachineSignalDirection.Input ? "DI00-DI31" : "DO00-DO31";
                issues.Add($"{item.Signal.Direction} '{item.Signal.SignalCode}' is mapped on {FormatSignalBoardChannel(item.Signal)}, outside the PCIE-1756 range {allowedRange} (32 DI + 32 DO). The channel is not read/driven; assign a channel in {allowedRange}.");
            }

            // Config.xml BlockingAlarmOutput / NonBlockingAlarmOutput: legacy fields, not read by the
            // runtime since 3.0.7.8 (alarm outputs are set per alarm card). Warn only, never rewrite.
            var appConfiguration = MainWindow.configManager?.Config?.Configuration;
            if (appConfiguration != null)
            {
                AddLegacyAlarmOutputIssue(issues, nameof(appConfiguration.BlockingAlarmOutput), appConfiguration.BlockingAlarmOutput);
                AddLegacyAlarmOutputIssue(issues, nameof(appConfiguration.NonBlockingAlarmOutput), appConfiguration.NonBlockingAlarmOutput);
            }

            if (issues.Count == 0)
            {
                AddLogEntry(
                    $"Trigger flow validation OK: useEncoder={UseEncoderTrigger}, mode={TriggerSchedulingMode}, virtualConveyor={VirtualConveyorEnabled}, photocell={BoundProductPhotocellSignalCode}, topDelay={TopTriggerBaseDelayMs} ms, sideDelay={SideTriggerBaseDelayMs} ms.",
                    IOLogLevel.Info,
                    "CONFIG");
                return;
            }

            foreach (var issue in issues)
            {
                AddLogEntry($"Trigger flow validation warning: {issue}", IOLogLevel.Warning, "CONFIG");
            }
        }

        private static string FormatSignalBoardChannel(MachineSignalDefinition signal)
        {
            var board = string.IsNullOrWhiteSpace(signal?.Board) ? "-" : signal.Board.Trim();
            var channel = string.IsNullOrWhiteSpace(signal?.Channel) ? "-" : signal.Channel.Trim();
            return $"{board}/{channel}";
        }

        private static void AddLegacyAlarmOutputIssue(List<string> issues, string fieldName, string value)
        {
            var channel = ParseChannelNumber(value);
            if (channel < 0 || Pcie1756ChannelMap.IsValidDoChannel(channel))
            {
                return;
            }

            issues.Add($"Config.xml {fieldName}='{value}' is outside the PCIE-1756 range DO00-DO31. The field is legacy and unused by the runtime (alarm outputs are set per alarm card); it is not rewritten.");
        }

        private const string AllowedOutputChannelRangeText = "DO00-DO31 (PCIE-1756) / DO100-DO103 (PCIE-1884)";
        private const string AllowedInputChannelRangeText = "DI00-DI31 (PCIE-1756) / DI100-DI103 (PCIE-1884)";

        private static string DescribeOutOfRangeChannel(MachineSignalDefinition signal, string allowedRange)
        {
            if (signal == null || string.IsNullOrWhiteSpace(signal.Channel))
            {
                return string.Empty;
            }

            return $" (channel '{signal.Channel}' outside allowed range {allowedRange})";
        }

        private Task ParkUnusedSpareOutputsLowAsync(string reason)
        {
            // No-op by design: the machine no longer ships a default "OUT_SPARE_DO00"
            // placeholder output (removed from InitializeMachineHardwareTemplate() on request,
            // since DO00/DO01 are not physically wired on this machine). This method previously
            // forced that specific hardcoded signal low and logged a warning whenever it was
            // missing from the loaded configuration - exactly the noise the user asked to
            // remove. Kept as a no-op (rather than deleted) so its four existing call sites
            // (runtime init, config load/save, live preview reapply) don't need to change.
            return Task.CompletedTask;
        }

        private bool ShouldSkipInternalTrigger(TrackedProduct product, ProductInterventionState state)
        {
            var externalEnabled = _cachedExternalTriggerEnableActive;
            if (string.Equals(MachineTriggerMode, "External", StringComparison.OrdinalIgnoreCase))
            {
                AddLogEntry($"{state.PointCode}: TriggerMode=External, waiting for machine trigger for product {product?.ProductId}", IOLogLevel.Info, "TRACK");
                return true;
            }

            if (string.Equals(MachineTriggerMode, "Hybrid", StringComparison.OrdinalIgnoreCase) && externalEnabled)
            {
                AddLogEntry($"{state.PointCode}: external trigger enable active, waiting for machine command for product {product?.ProductId}", IOLogLevel.Info, "TRACK");
                return true;
            }

            return false;
        }

        private void OnInputChanged(object sender, IOEvent e)
        {
            var sourceManager = sender;
            var machineController = _machineController;
            if (!ReferenceEquals(sourceManager, _ioManager) || machineController?.IsInitialized != true)
            {
                // Polling starts before the machine configuration is loaded. Its initial
                // electrical samples are synchronized explicitly after runtime initialization.
                return;
            }

            if (TryHandleTimedPhotocellFastPath(e))
            {
                return;
            }

            var eventMainEncoderCount = ResolveEventMainEncoderCount(e);
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            // The Advantech polling loop fires this on a background thread.
            // Everything below accesses ObservableCollections (MachineInputs, MachineOutputs,
            // InterventionPoints) and sets UI-bound properties, all of which require the UI
            // thread. Dispatch the entire handler body so all accesses are safe.
            dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (ReferenceEquals(sourceManager, _ioManager) &&
                    ReferenceEquals(machineController, _machineController) &&
                    machineController.IsInitialized)
                {
                    OnInputChangedCore(e, eventMainEncoderCount);
                }
            }));
        }

        private void OnInputChangedCore(IOEvent e, long eventMainEncoderCount)
        {
            var mappedSignal = ResolveInputSignalByChannel(e.Channel);
            var logicalState = IsSignalLogicallyActive(mappedSignal, e.NewState);

            if (string.Equals(mappedSignal?.SignalCode, BoundExternalTriggerEnableSignalCode, StringComparison.OrdinalIgnoreCase))
            {
                _cachedExternalTriggerEnableActive = logicalState;
            }

            if (mappedSignal != null)
            {
                AddLogEntry($"Input {mappedSignal.SignalCode} ({mappedSignal.Channel}) -> {(logicalState ? "ATTIVO" : "INATTIVO")}", IOLogLevel.Info, "IO");
            }
            else
            {
                AddLogEntry($"Input channel={e.Channel} -> {(e.NewState ? "HIGH" : "LOW")} [unmapped - no signal definition found for this channel]", IOLogLevel.Warning, "IO");
            }

            if (logicalState && mappedSignal != null &&
                !string.Equals(mappedSignal.SignalCode, BoundProductPhotocellSignalCode, StringComparison.OrdinalIgnoreCase))
            {
                AddLogEntry(
                    $"Input RISING on {mappedSignal.SignalCode} ({mappedSignal.Channel}) ignored by trigger flow: configured photocell={BoundProductPhotocellSignalCode}. If this is your real photocell, update ProductPhotocellSignalCode in the runtime binding.",
                    IOLogLevel.Warning,
                    "TRIGGER-TIMED");
            }

            if (logicalState && string.Equals(mappedSignal?.SignalCode, BoundProductPhotocellSignalCode, StringComparison.OrdinalIgnoreCase))
            {
                if (UseEncoderTrigger && !_mainEncoderReferenceReady)
                {
                    AddLogEntry(
                        $"Photocell trigger ignored because main encoder {BoundMainEncoderAxisCode} has not produced an initial synchronized sample yet.",
                        IOLogLevel.Warning,
                        "TRACK");
                    return;
                }

                var currentEncoderCount = eventMainEncoderCount;
                if (ShouldIgnorePhotocellDuplicate(currentEncoderCount, out var deltaMillimeters, out var elapsedMilliseconds))
                {
                    AddLogEntry(
                        $"Ignored duplicated photocell trigger on {mappedSignal.SignalCode}: delta={deltaMillimeters:0.00} mm, elapsed={elapsedMilliseconds:0.0} ms, encoder={currentEncoderCount}.",
                        IOLogLevel.Warning,
                        "TRACK");
                    return;
                }

                _lastAcceptedPhotocellUtc = DateTime.UtcNow;
                _lastAcceptedPhotocellEncoderCount = currentEncoderCount;
                var snapshotAgeMs = ResolveEventEncoderSnapshotAgeMs(e);
                AddLogEntry(
                    $"Photocell trigger accepted on {mappedSignal.SignalCode}: encoder={currentEncoderCount}, snapshotAge={snapshotAgeMs:0.0} ms, machine photocell quota={MainEncoderTemplate?.PhotocellMachineOffsetMm:0.0} mm.",
                    IOLogLevel.Info,
                    "TRACK");

                if (UseEncoderTrigger)
                {
                    _multiShotTriggerController?.OnProductTriggerReceived(currentEncoderCount);
                }
                else if (IsAnyMultiShotProfileEnabled())
                {
                    AddLogEntry(
                        "MultiShot skipped: UseEncoderTrigger=false. MultiShot requires encoder-tracked trigger mode.",
                        IOLogLevel.Warning,
                        "MULTISHOT");
                }

                if (IsTimedTriggerSchedulingActive)
                {
                    AddLogEntry(
                        $"TimedFromPhotocell active: edge {mappedSignal.SignalCode} queues deterministic TOP/SIDE pulses. Virtual conveyor flag={VirtualConveyorEnabled}.",
                        IOLogLevel.Info,
                        "TRIGGER-TIMED");
                    QueueTimedPhotocellTriggerBatch(mappedSignal, currentEncoderCount, NormalizeEventTimestampUtc(e.Timestamp));
                    return;
                }

                var product = _machineController.RegisterProductDetection(currentEncoderCount);
                if (UseInterventionPointRuntime)
                {
                    AddLogEntry(
                        VirtualConveyorEnabled
                            ? "Legacy tracked-position flow active: PRODUCT_ZERO created and intervention points will advance on the virtual conveyor."
                            : "Legacy tracked-position flow active with virtual conveyor disabled: PRODUCT_ZERO created, but camera/reject points now wait for real encoder progression. For direct delay-based pulses use TimedFromPhotocell.",
                        VirtualConveyorEnabled ? IOLogLevel.Info : IOLogLevel.Warning,
                        "TRACK");
                    LastTrackedProductStatus = string.Format(
                        T("Tool_VM_ProductStatusDetectedAttached", "Product {0}: detected and attached to machine zero"),
                        product.ProductId);
                    AddLogEntry($"Product {product.ProductId}: created from photocell, waiting for configured intervention points", IOLogLevel.Success, "TRACK");
                }
                else
                {
                    ExecuteTriggerCycleAsync(product).SafeFireAndForget(ex => AddLogEntry($"Trigger cycle error: {ex.Message}", IOLogLevel.Error, "TRACK", ex));
                }
            }
        }

        private void OnCounterChanged(object sender, EncoderEvent e)
        {
            if (!ReferenceEquals(sender, _ioManager))
            {
                return;
            }

            if (e.Channel >= 0 && e.Channel < EncoderChannels.Count)
            {
                // A nastro fermo la frequenza va a zero: quell'evento passa sempre, cosi'
                // il valore a riposo mostrato e' quello vero e non l'ultimo campione
                // sopravvissuto alla riduzione di cadenza.
                bool motionStopped = e.RawFrequencyHz < 0.1;
                long nowTicks = Stopwatch.GetTimestamp();
                bool dueForUiUpdate =
                    e.Channel >= _lastEncoderUiUpdateTicks.Length ||
                    nowTicks - _lastEncoderUiUpdateTicks[e.Channel] >= EncoderUiUpdateIntervalTicks;

                if (motionStopped || dueForUiUpdate)
                {
                    if (e.Channel < _lastEncoderUiUpdateTicks.Length)
                    {
                        // Scritto solo dal thread di polling: nessun lock necessario.
                        _lastEncoderUiUpdateTicks[e.Channel] = nowTicks;
                    }

                    // Copiati in locali: la closure non trattiene l'oggetto evento.
                    int channel = e.Channel;
                    long newValue = e.NewValue;
                    long delta = e.Delta;
                    double rawFrequencyHz = e.RawFrequencyHz;

                    PostUiUpdate(() =>
                    {
                        EncoderChannels[channel].CounterValue = newValue;
                        EncoderChannels[channel].Status = "Count: " + newValue;
                        EncoderChannels[channel].LastDeltaCounts = delta;
                        EncoderChannels[channel].RawPulseFrequencyHz = rawFrequencyHz;
                    });
                }
            }

        }

        /// <summary>
        /// Percorso encoder critico: contiene soltanto tracking quote e MultiShot. Non
        /// aggiorna collezioni WPF, non scrive log e non attende operazioni asincrone.
        /// </summary>
        private void OnCriticalCounterChanged(object sender, EncoderEvent e)
        {
            if (!ReferenceEquals(sender, _ioManager))
            {
                return;
            }

            // In virtual-conveyor real-HW mode the virtual timer is the sole authority
            // for the machine controller on this channel. A stale hardware counter event
            // (e.g. board init reporting count=0) would overwrite _lastMainEncoderCount
            // and make all intervention targets appear already-passed on the next virtual
            // tick, causing all outputs to fire simultaneously at photocell detection.
            if (VirtualConveyorEnabled && !IsSimulationMode && e.Channel == VirtualConveyorEncoderChannel)
                return;

            var machineController = _machineController;
            if (machineController?.IsInitialized != true)
            {
                // ArmConfiguredEncoderChannelsAsync reads and applies an authoritative
                // encoder snapshot as soon as machine runtime initialization completes.
                return;
            }

            if (e.Channel == GetMainEncoderChannelIndex())
            {
                _mainEncoderReferenceReady = true;
            }

            machineController.UpdateEncoderPosition(e.Channel, e.NewValue);
            _multiShotTriggerController?.OnEncoderChanged(e.Channel, e.NewValue);
        }

        private void OnLogMessage(object sender, string message)
        {
            AddLogEntry(message, IOLogLevel.Info, "CTRL");
        }

        private void CreateIoManager()
        {
            _ioManager = new AdvantechDeviceManager(PreferSimulationMode);
            ServiceLocator.IoManager = _ioManager;
            SubscribeIoManagerEvents();
        }

        private void SubscribeIoManagerEvents()
        {
            _ioManager.InputChanged += OnInputChanged;
            _ioManager.CriticalCounterChanged += OnCriticalCounterChanged;
            _ioManager.CounterChanged += OnCounterChanged;
            _ioManager.LogMessage += OnLogMessage;
        }

        private void UnsubscribeIoManagerEvents()
        {
            if (_ioManager == null)
            {
                return;
            }

            _ioManager.InputChanged -= OnInputChanged;
            _ioManager.CriticalCounterChanged -= OnCriticalCounterChanged;
            _ioManager.CounterChanged -= OnCounterChanged;
            _ioManager.LogMessage -= OnLogMessage;
        }

        private void SubscribeMachineControllerEvents()
        {
            if (_machineController == null)
            {
                return;
            }

            _machineController.ProductDetected += OnTrackedProductDetected;
            _machineController.ProductUpdated += OnTrackedProductUpdated;
            _machineController.ProductReadyForReject += OnTrackedProductReadyForReject;
            _machineController.InterventionPointReached += OnInterventionPointReached;
            _machineController.LogMessage += OnMachineControllerLogMessage;
        }

        private void UnsubscribeMachineControllerEvents()
        {
            if (_machineController == null)
            {
                return;
            }

            _machineController.ProductDetected -= OnTrackedProductDetected;
            _machineController.ProductUpdated -= OnTrackedProductUpdated;
            _machineController.ProductReadyForReject -= OnTrackedProductReadyForReject;
            _machineController.InterventionPointReached -= OnInterventionPointReached;
            _machineController.LogMessage -= OnMachineControllerLogMessage;
        }

        private void RecreateMachineController()
        {
            UnsubscribeMachineControllerEvents();
            _machineController = new MachineController(_ioManager);
            SubscribeMachineControllerEvents();
        }

        private void RecreateMultiShotController()
        {
            DisposeMultiShotController();
            _multiShotTriggerController = new MultiShotTriggerController(_ioManager);
            _multiShotTriggerController.EventRaised += OnMultiShotTriggerEventRaised;
            AttachMultiShotSessionPreparationHandler(_multiShotTriggerController);
            // AttachStitchingStatusProvider disabled — see comment in constructor.
        }

        private static void AttachMultiShotSessionPreparationHandler(IMultiShotTriggerController controller)
        {
            if (controller == null)
            {
                return;
            }

            controller.SessionPreparationHandler = MainWindow.PrepareMultiShotCameraPhase;
        }

        public void HandleVisionProRecoveryStarted(string reason)
        {
            CancelMultiShotSession("VisionPro recovery: " + (reason ?? "unspecified"));
        }

        private static void AttachStitchingStatusProvider(IMultiShotTriggerController controller)
        {
            if (controller == null) return;
            controller.StitchingStatusProvider = profileKey =>
            {
                string role = CameraConfigurationHelper.NormalizeCameraType(profileKey ?? string.Empty);
                object job;
                switch (role)
                {
                    case "left":
                    case "side":   job = MainWindow._sideJob;   break;
                    case "front":  job = MainWindow._frontJob;  break;
                    case "rear":   job = MainWindow._rearJob;   break;
                    case "bottom": job = MainWindow._bottomJob; break;
                    default:       job = MainWindow._sideJob;   break;
                }
                return ImageStitchingStatusReader.ReadFromJob(job);
            };
        }

        private async Task InitializeIoManagerAsync()
        {
            AddLogEntry($"Mode initialization requested: {(PreferSimulationMode ? "SIMULATION" : "REAL HARDWARE")}", IOLogLevel.Info, "HW"); _isInitialized = await _ioManager.InitializeAsync();
            IsSimulationMode = _ioManager.IsSimulationMode;
            _hasRealHardwareConnection = _isInitialized && !IsSimulationMode && !PreferSimulationMode;
            OnPropertyChanged(nameof(ConnectionModeSummary));
            OnPropertyChanged(nameof(ConnectionCheckStatus));
            OnPropertyChanged(nameof(ConnectionCheckDetails));

            if (_isInitialized)
            {
                AddLogEntry($"Active mode after init: {(IsSimulationMode ? "SIMULATION" : "REAL HARDWARE")}", IOLogLevel.Success, "HW");
                AddMachineOperationalEvent(
                    T("Tool_Event_CategoryConnection", "Connection"),
                    IsSimulationMode ? "MODE_SIMULATION" : "MODE_HARDWARE",
                    IsSimulationMode ? "Simulation mode active" : "Real hardware connected",
                    IsSimulationMode ? "The tool is running on the internal simulator." : "The real boards have been attached successfully.",
                    IsSimulationMode ? "SIM" : "HW");
                if (!_hasRealHardwareConnection && !PreferSimulationMode)
                {
                    AddLogEntry("Real hardware requested but not attached. The manager returned to simulation; the board may be busy in DAQNavi Navigator or another tool.", IOLogLevel.Warning, "HW");
                AddMachineOperationalEvent(
                    T("Tool_Event_CategoryConnection", "Connection"),
                    "HW_CONNECTION_FAILED",
                    T("Tool_Event_HardwareConnectionFailed_Title", "Hardware connection failed"),
                    T("Tool_Event_HardwareConnectionFailed_Message", "Real hardware requested but not attached; fallback to simulation."),
                    T("Tool_Event_StatusFallback", "Fallback"));
                }
            }
            else
            {
                AddLogEntry("I/O manager initialization failed", IOLogLevel.Error, "HW");
                AddMachineOperationalEvent("Connection", "INIT_FAILED", "I/O initialization failed", "The I/O manager could not initialize.", "Error");
            }
        }

        private async Task ReinitializeIoManagerAsync()
        {
            await _modeSwitchLock.WaitAsync();
            try
            {
                if (IsModeSwitching)
                {
                    return;
                }

                IsModeSwitching = true;
                StatusMessage = PreferSimulationMode
                    ? T("Tool_VM_StatusSwitchingSimulation", "Switching to simulation...")
                    : T("Tool_VM_StatusSwitchingHardware", "Switching to real hardware...");
                AddLogEntry($"Mode change requested: {(PreferSimulationMode ? "SIMULATION" : "REAL HARDWARE")}", IOLogLevel.Info, "HW");
                _refreshTimer.Stop();
                _virtualConveyorTimer.Stop();
                CancelPendingTimedTriggerBatches("connection mode change");
                CancelMultiShotSession("connection mode change");
                IsRefreshing = false;
                await StopHeartbeatAsync("connection mode change");
                UnsubscribeIoManagerEvents();
                UnsubscribeMachineControllerEvents();
                DisposeMultiShotController();
                (_ioManager as IDisposable)?.Dispose();
                await Task.Delay(250);
                CreateIoManager();
                RecreateMachineController();
                RecreateMultiShotController();
                await InitializeIoManagerAsync();
                await InitializeMachineRuntimeAsync();
                await RefreshOutputsAsync();
                StatusMessage = _hasRealHardwareConnection
                    ? T("Tool_VM_StatusRealIoInitialized", "Real I/O devices initialized")
                    : (IsSimulationMode
                        ? T("Tool_VM_StatusSimulationActive", "Simulation mode active")
                        : T("Tool_VM_StatusHardwareUnavailable", "Hardware connection not available"));
                AddLogEntry(
                    $"Connection mode applied: {(IsSimulationMode ? "SIMULATION" : "REAL HARDWARE")}. Check status: {ConnectionCheckStatus}",
                    _hasRealHardwareConnection || PreferSimulationMode ? IOLogLevel.Success : IOLogLevel.Warning,
                    "HW");
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                AddLogEntry($"I/O reinitialization error: {ex.Message}", IOLogLevel.Error, "HW", ex);
            }
            finally
            {
                IsModeSwitching = false;
                _modeSwitchLock.Release();
            }
        }

        private async Task ToggleConnectionModeAsync()
        {
            PreferSimulationMode = !PreferSimulationMode;
            await ReinitializeIoManagerAsync();
        }

        private void ApplySimulationEncoderSettings()
        {
            if (_ioManager == null)
            {
                return;
            }

            var mainEncoder = EncoderTemplates.FirstOrDefault();
            var countsPerMillimeter = 20.48;
            if (mainEncoder != null && mainEncoder.MillimetersPerRevolution > 0)
            {
                countsPerMillimeter = CalculateCountsPerMillimeter(mainEncoder);
            }

            _ioManager.ConfigureSimulationEncoder(
                DerivedSimulationEncoderSpeedMmPerSecond,
                countsPerMillimeter,
                SimulationEncoderRunning);

            UpdateVirtualConveyorTimerState();
            OnPropertyChanged(nameof(VirtualConveyorSummary));
        }

        private async Task StepSimulationEncoderAsync()
        {
            ApplySimulationEncoderSettings();
            await _ioManager.SimulateEncoderAdvanceAsync(SimulationEncoderStepMm);
            await ReadEncodersAsync();
            AddLogEntry($"Avanzamento encoder simulato di {SimulationEncoderStepMm:0.0} mm", IOLogLevel.Info, "ENC");
        }

        private void CaptureCurrentEncoderCalibrationSpeed()
        {
            var speed = GetCurrentMainEncoderSpeedMetersPerMinute();
            if (speed <= 0.0)
            {
                AddLogEntry(
                    T("Tool_VM_EncoderCalibrationCurrentSpeedUnavailable", "Current encoder speed is not available. Run the conveyor and try again."),
                    IOLogLevel.Warning,
                    "ENC");
                return;
            }

            EncoderCalibrationHmiSpeedMetersPerMinute = speed;
            AddLogEntry(
                string.Format(
                    T("Tool_VM_EncoderCalibrationCurrentSpeedCaptured", "Captured current HMI encoder speed: {0:0.00} m/min."),
                    speed),
                IOLogLevel.Info,
                "ENC");
        }

        private void ApplyEncoderTachometerCalibration()
        {
            var encoder = MainEncoderTemplate;
            if (encoder == null)
            {
                AddLogEntry(T("Tool_VM_EncoderCalibrationNoMainEncoder", "Main encoder not configured."), IOLogLevel.Warning, "ENC");
                return;
            }

            var tachometerSpeed = EncoderCalibrationTachometerSpeedMetersPerMinute;
            var hmiSpeed = ResolveEncoderCalibrationHmiSpeed();
            if (tachometerSpeed <= 0.0 || hmiSpeed <= 0.0)
            {
                AddLogEntry(
                    T("Tool_VM_EncoderCalibrationInvalidSpeed", "Encoder calibration requires tachometer speed and HMI speed greater than zero."),
                    IOLogLevel.Warning,
                    "ENC");
                return;
            }

            if (encoder.MillimetersPerRevolution <= 0.0)
            {
                AddLogEntry(
                    T("Tool_VM_EncoderCalibrationInvalidMmPerRev", "Encoder calibration requires current mm/rev greater than zero."),
                    IOLogLevel.Warning,
                    "ENC");
                return;
            }

            var factor = tachometerSpeed / hmiSpeed;
            if (factor < 0.001 || factor > 1000.0)
            {
                AddLogEntry(
                    string.Format(
                        T("Tool_VM_EncoderCalibrationFactorOutOfRange", "Calibration factor {0:0.###} is outside the accepted range 0.001..1000. Check tachometer and HMI speed values."),
                        factor),
                    IOLogLevel.Warning,
                    "ENC");
                return;
            }

            var oldMillimetersPerRevolution = encoder.MillimetersPerRevolution;
            var oldCountsPerMillimeter = CalculateCountsPerMillimeter(encoder);
            var newMillimetersPerRevolution = oldMillimetersPerRevolution * factor;
            encoder.MillimetersPerRevolution = newMillimetersPerRevolution;
            var newCountsPerMillimeter = CalculateCountsPerMillimeter(encoder);

            RefreshEncoderSummaryProperties();
            RefreshMultiShotDerivedProperties();
            OnPropertyChanged(nameof(EncoderCalibrationSummary));
            AddLogEntry(
                string.Format(
                    T("Tool_VM_EncoderCalibrationApplied", "Tachometer calibration applied on {0}: tachometer {1:0.00} m/min, HMI {2:0.00} m/min, mm/rev {3:0.###} -> {4:0.###}."),
                    encoder.AxisName,
                    tachometerSpeed,
                    hmiSpeed,
                    oldMillimetersPerRevolution,
                    newMillimetersPerRevolution),
                IOLogLevel.Success,
                "ENC");

            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                LogLevel.Info,
                "ENCODER_TACHOMETER_CALIBRATION_APPLIED",
                "Encoder",
                "Encoder tachometer calibration applied",
                nameof(DigitalIOViewModel),
                $"Axis={encoder.AxisName}; Tachometer={tachometerSpeed:0.###}; Hmi={hmiSpeed:0.###}; Factor={factor:0.######}; MmPerRev={oldMillimetersPerRevolution:0.######}->{newMillimetersPerRevolution:0.######}; CountsPerMm={oldCountsPerMillimeter:0.######}->{newCountsPerMillimeter:0.######}");

            PersistEncoderCalibration();
        }

        private void PersistEncoderCalibration()
        {
            try
            {
                _runtimePreviewApplyTimer.Stop();
                _machineConfiguration = BuildRuntimeConfigurationFromView();
                _machineConfigurationService.Save(_machineConfiguration);
                _lastPersistedMultiShotConfig = _machineConfiguration.MachineMultiShotTrigger;
                ConfigureEffectiveRuntimeAsync(
                    _machineConfiguration,
                    MainWindow.ConfigRecipeParam?.Config,
                    "encoder calibration").GetAwaiter().GetResult();
                MainWindow.ApplyVisionProStitchingParametersForLeftJobs(
                    $"{nameof(DigitalIOViewModel)} encoder calibration",
                    _machineConfiguration);
                _machineController.ClearActiveProducts();
                MarkConfigurationSaved();
                RefreshConfigurationBackups();
                AddLogEntry(
                    $"Encoder calibration saved automatically in {MachineConfigurationPath}. The calibrated mm/rev value will be reused at the next startup.",
                    IOLogLevel.Success,
                    "ENC");
            }
            catch (Exception ex)
            {
                AddLogEntry($"Unable to save encoder calibration: {ex.Message}", IOLogLevel.Error, "ENC", ex);
            }
        }

        private double ResolveEncoderCalibrationHmiSpeed()
        {
            return EncoderCalibrationHmiSpeedMetersPerMinute > 0.0
                ? EncoderCalibrationHmiSpeedMetersPerMinute
                : GetCurrentMainEncoderSpeedMetersPerMinute();
        }

        private double GetCurrentMainEncoderSpeedMetersPerMinute()
        {
            if (EncoderChannels.Count == 0)
            {
                return 0.0;
            }

            var index = GetMainEncoderChannelIndex();
            if (index < 0 || index >= EncoderChannels.Count)
            {
                return 0.0;
            }

            return Math.Max(0.0, EncoderChannels[index].SpeedMetersPerMinute);
        }

        private void UpdateEncoderMetrics()
        {
            var now = DateTime.UtcNow;

            if (IsSimulationMode)
            {
                var simulatedSpeed = SimulationEncoderRunning
                    ? (SimulationUsePiecesPerMinute
                        ? (SimulationPiecesPerMinute * SimulationProductPitchMm) / 1000.0
                        : SimulationTransportSpeedMetersPerMinute)
                    : 0.0;

                for (int i = 0; i < EncoderChannels.Count && i < 4; i++)
                {
                    var currentCount = EncoderChannels[i].CounterValue;
                    var deltaCounts = currentCount - _lastEncoderCounts[i];
                    EncoderChannels[i].SpeedMetersPerMinute = i == 0 ? Math.Max(0.0, simulatedSpeed) : 0.0;
                    EncoderChannels[i].RawSpeedMetersPerMinute = EncoderChannels[i].SpeedMetersPerMinute;
                    EncoderChannels[i].LastDeltaCounts = deltaCounts;
                    EncoderChannels[i].LastSampleMilliseconds = _lastEncoderSampleTimes[i] == DateTime.MinValue ? 0.0 : Math.Max(0.0, (now - _lastEncoderSampleTimes[i]).TotalMilliseconds);
                    EncoderChannels[i].CountsPerMillimeter = GetCountsPerMillimeter(i);
                    EncoderChannels[i].EstimatedPiecesPerMinute = GetEstimatedPiecesPerMinute(EncoderChannels[i].SpeedMetersPerMinute);
                    EncoderChannels[i].DiagnosticState = SimulationEncoderRunning
                        ? (i == 0 ? "Simulation running" : "Secondary axis stopped")
                        : "Simulation stopped";
                    _filteredEncoderSpeedMetersPerMinute[i] = EncoderChannels[i].SpeedMetersPerMinute;
                    _lastEncoderCounts[i] = currentCount;
                    _lastEncoderSampleTimes[i] = now;
                }

                PublishMainEncoderSpeed();
                RefreshEncoderSummaryProperties();
                return;
            }

            for (int i = 0; i < EncoderChannels.Count && i < 4; i++)
            {
                var currentCount = EncoderChannels[i].CounterValue;
                var lastTime = _lastEncoderSampleTimes[i];
                var lastCount = _lastEncoderCounts[i];

                if (!_encoderMetricReferenceReady[i])
                {
                    EncoderChannels[i].LastSampleMilliseconds = 0.0;
                    EncoderChannels[i].LastDeltaCounts = 0;
                    EncoderChannels[i].RawSpeedMetersPerMinute = 0.0;
                    EncoderChannels[i].SpeedMetersPerMinute = 0.0;
                    EncoderChannels[i].CountsPerMillimeter = GetCountsPerMillimeter(i);
                    EncoderChannels[i].EstimatedPiecesPerMinute = 0.0;
                    EncoderChannels[i].DiagnosticState = T("Tool_VM_EncoderWaitingFirstSample", "Waiting for first sample");
                    _filteredEncoderSpeedMetersPerMinute[i] = 0.0;
                    _lastEncoderCounts[i] = currentCount;
                    _lastEncoderSampleTimes[i] = now;
                    _encoderMetricReferenceReady[i] = true;
                    // Mirror ResetAllEncodersAsync: activate the spike filter for the warmup
                    // window so that a stale board-init count=0 followed by a real accumulated
                    // count does not produce a false 250 m/min reading on startup.
                    _encoderMetricWarmupUntilUtc[i] = now.Add(TimeSpan.FromSeconds(EncoderStartupWarmupSeconds));
                    continue;
                }

                if (lastTime != DateTime.MinValue)
                {
                    var elapsedSeconds = (now - lastTime).TotalSeconds;
                    EncoderChannels[i].LastSampleMilliseconds = Math.Max(0.0, elapsedSeconds * 1000.0);
                    if (elapsedSeconds >= 0.15)
                    {
                        var deltaCounts = currentCount - lastCount;
                        EncoderChannels[i].LastDeltaCounts = deltaCounts;
                        var countsPerMillimeter = GetCountsPerMillimeter(i);
                        EncoderChannels[i].CountsPerMillimeter = countsPerMillimeter;
                        if (countsPerMillimeter > 0)
                        {
                            var deltaMillimeters = deltaCounts / countsPerMillimeter;
                            var rawSpeedMetersPerMinute = Math.Abs(deltaMillimeters) * 60.0 / (1000.0 * elapsedSeconds);
                            if (Math.Abs(deltaCounts) <= EncoderNoiseDeadbandCounts)
                            {
                                rawSpeedMetersPerMinute = 0.0;
                            }

                            if (now <= _encoderMetricWarmupUntilUtc[i] &&
                                rawSpeedMetersPerMinute > EncoderStartupSpikeSpeedLimitMetersPerMinute)
                            {
                                EncoderChannels[i].LastDeltaCounts = 0;
                                EncoderChannels[i].RawSpeedMetersPerMinute = 0.0;
                                EncoderChannels[i].SpeedMetersPerMinute = 0.0;
                                EncoderChannels[i].EstimatedPiecesPerMinute = 0.0;
                                EncoderChannels[i].DiagnosticState = T("Tool_VM_EncoderStartupReferenceSynchronized", "Startup reference synchronized");
                                _filteredEncoderSpeedMetersPerMinute[i] = 0.0;
                                _lastEncoderCounts[i] = currentCount;
                                _lastEncoderSampleTimes[i] = now;
                                continue;
                            }

                            if (rawSpeedMetersPerMinute >= EncoderDisplayedSpeedLimitMetersPerMinute)
                            {
                                MainWindow.logger?.Warn(
                                    $"ENCODER_SPEED_CAP|ch={i}|raw={rawSpeedMetersPerMinute:0.0}|delta={deltaCounts}|elapsed={elapsedSeconds:0.000}s — capped at {EncoderDisplayedSpeedLimitMetersPerMinute} m/min");
                            }

                            rawSpeedMetersPerMinute = Math.Min(rawSpeedMetersPerMinute, EncoderDisplayedSpeedLimitMetersPerMinute);
                            EncoderChannels[i].RawSpeedMetersPerMinute = rawSpeedMetersPerMinute;

                            var previousFiltered = _filteredEncoderSpeedMetersPerMinute[i];
                            var filteredSpeed = previousFiltered <= 0.001
                                ? rawSpeedMetersPerMinute
                                : (previousFiltered * 0.75) + (rawSpeedMetersPerMinute * 0.25);

                            if (rawSpeedMetersPerMinute < 0.02)
                            {
                                filteredSpeed *= 0.6;
                                if (filteredSpeed < 0.02)
                                {
                                    filteredSpeed = 0.0;
                                }
                            }

                            _filteredEncoderSpeedMetersPerMinute[i] = filteredSpeed;
                            EncoderChannels[i].SpeedMetersPerMinute = filteredSpeed;
                            EncoderChannels[i].EstimatedPiecesPerMinute = GetEstimatedPiecesPerMinute(filteredSpeed);
                            EncoderChannels[i].DiagnosticState = rawSpeedMetersPerMinute <= 0.001
                                ? "Fermo / rumore sotto soglia"
                                : "Movimento rilevato";
                        }
                        else
                        {
                            EncoderChannels[i].DiagnosticState = "Counts/mm non configurato";
                        }

                        _lastEncoderCounts[i] = currentCount;
                        _lastEncoderSampleTimes[i] = now;
                    }
                }
                else
                {
                    EncoderChannels[i].LastSampleMilliseconds = 0.0;
                    EncoderChannels[i].LastDeltaCounts = 0;
                    EncoderChannels[i].RawSpeedMetersPerMinute = 0.0;
                    EncoderChannels[i].CountsPerMillimeter = GetCountsPerMillimeter(i);
                    EncoderChannels[i].EstimatedPiecesPerMinute = GetEstimatedPiecesPerMinute(EncoderChannels[i].SpeedMetersPerMinute);
                    EncoderChannels[i].DiagnosticState = T("Tool_VM_EncoderWaitingFirstSample", "Waiting for first sample");

                    _lastEncoderCounts[i] = currentCount;
                    _lastEncoderSampleTimes[i] = now;
                }
            }

            PublishMainEncoderSpeed();
            RefreshEncoderSummaryProperties();
        }

        private void PublishMainEncoderSpeed()
        {
            if (EncoderChannels.Count == 0)
            {
                ServiceLocator.MachineSpeedService.Clear("Encoder");
                return;
            }

            var mainChannel = EncoderChannels[GetMainEncoderChannelIndex()];
            if (UseEncoderTrigger || VirtualConveyorEnabled || IsSimulationMode)
            {
                ServiceLocator.MachineSpeedService.Publish(mainChannel.SpeedMetersPerMinute, "Encoder");
            }
            else
            {
                ServiceLocator.MachineSpeedService.Clear("Encoder");
            }
        }

        private void RefreshEncoderSummaryProperties()
        {
            OnPropertyChanged(nameof(MainEncoderSpeedSummary));
            OnPropertyChanged(nameof(MainEncoderPiecesSummary));
            OnPropertyChanged(nameof(MainEncoderRawSpeedSummary));
            OnPropertyChanged(nameof(MainEncoderRawDeltaSummary));
            OnPropertyChanged(nameof(MainEncoderSampleSummary));
            OnPropertyChanged(nameof(MainEncoderCountsSummary));
            OnPropertyChanged(nameof(MainEncoderDiagnosticStateSummary));
            OnPropertyChanged(nameof(EncoderCalibrationSummary));
        }

        private double GetEstimatedPiecesPerMinute(double speedMetersPerMinute)
        {
            if (SimulationProductPitchMm <= 0.0)
            {
                return 0.0;
            }

            return Math.Max(0.0, speedMetersPerMinute) * 1000.0 / SimulationProductPitchMm;
        }

        private void RefreshMachineDashboardProperties()
        {
            OnPropertyChanged(nameof(ActiveInputsSummary));
            OnPropertyChanged(nameof(ActiveOutputsSummary));
            OnPropertyChanged(nameof(TrackedProductsSummary));
            OnPropertyChanged(nameof(HeartbeatDashboardSummary));
        }

        private bool FilterMachineInput(object item)
        {
            return PassesSignalFilter(item as MachineSignalDefinition);
        }

        private bool FilterMachineOutput(object item)
        {
            return PassesSignalFilter(item as MachineSignalDefinition);
        }

        private bool PassesSignalFilter(MachineSignalDefinition signal)
        {
            if (signal == null)
            {
                return false;
            }

            if (ShowOnlyRuntimeSignals && !signal.IsRuntimeBound)
            {
                return false;
            }

            if (ShowOnlyUnassignedSignals && (signal.IsRuntimeBound || signal.IsAdditionalBinding))
            {
                return false;
            }

            if (ShowOnlyRealSignals && !signal.ReservedForRealSignal)
            {
                return false;
            }

            return true;
        }

        private void RefreshSignalViews()
        {
            SafeRefreshCollectionView(MachineInputsView);
            SafeRefreshCollectionView(MachineOutputsView);
        }

        private static void SafeRefreshCollectionView(ICollectionView view)
        {
            if (view == null) return;
            var editable = view as IEditableCollectionView;
            if (editable?.IsEditingItem == true || editable?.IsAddingNew == true) return;
            view.Refresh();
        }

        private void RefreshAvailableSignalCodes()
        {
            var codes = MachineInputs
                .Concat(MachineOutputs)
                .Select(signal => signal?.SignalCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToList();

            // Aggiornamento DIFFERENZIALE, mai Clear()+riaggiunta. La ComboBox del codice
            // segnale e' IsEditable con Text legato TwoWay a SelectedInterventionPoint.SignalCode
            // e UpdateSourceTrigger=PropertyChanged: svuotando l'ItemsSource mentre un elemento
            // e' selezionato, WPF azzera Text e riscrive stringa vuota sulla sorgente. Ogni
            // salvataggio cancellava cosi' il segnale del punto selezionato, che tornava vuoto
            // pur restando corretto a runtime. Se l'elenco non cambia la collezione non viene
            // toccata e la selezione resta intatta.
            for (int i = AvailableSignalCodes.Count - 1; i >= 0; i--)
            {
                if (!codes.Contains(AvailableSignalCodes[i], StringComparer.OrdinalIgnoreCase))
                {
                    AvailableSignalCodes.RemoveAt(i);
                }
            }

            foreach (var code in codes)
            {
                if (!AvailableSignalCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                {
                    AvailableSignalCodes.Add(code);
                }
            }

            // Nessun OnPropertyChanged sulla collezione: rimpiazzare l'ItemsSource provoca la
            // stessa perdita di selezione. ObservableCollection notifica gia' da se'.
        }

        private void OnMachineSignalsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<MachineSignalDefinition>())
                {
                    item.PropertyChanged -= OnMachineSignalPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<MachineSignalDefinition>())
                {
                    item.PropertyChanged += OnMachineSignalPropertyChanged;
                }
            }

            RefreshAvailableSignalCodes();
            RefreshMachineSignalStatuses();
            RefreshInterventionSignalAssignments();
            OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
            MarkConfigurationDirty();
            QueueRuntimePreviewApply("machine signal collection changed");
        }

        private void RefreshInterventionTimelineItems()
        {
            var photocellMachineOffset = MainEncoderTemplate?.PhotocellMachineOffsetMm ?? 0.0;

            var orderedItems = InterventionPoints
                .Select(point => new InterventionPointDisplayItem
                {
                    Enabled = point.Enabled,
                    IsStandard = point.IsStandard,
                    PointCode = point.PointCode,
                    Description = point.Description,
                    ActionType = point.ActionType,
                    SignalCode = string.IsNullOrWhiteSpace(point.SignalCode) ? ResolveDefaultSignalCodeForAction(point.ActionType) : point.SignalCode,
                    RelativeQuotaMm = point.EffectiveOffsetMm,
                    AbsoluteMachineQuotaMm = photocellMachineOffset + point.EffectiveOffsetMm,
                    Notes = point.Notes
                })
                .OrderBy(item => item.AbsoluteMachineQuotaMm)
                .ThenBy(item => item.PointCode)
                .ToList();

            for (var index = 0; index < orderedItems.Count; index++)
            {
                orderedItems[index].OrderIndex = index + 1;
            }

            ReplaceCollection(InterventionTimelineItems, orderedItems);
            RefreshInterventionSignalAssignments();
        }

        private long ResolveEventMainEncoderCount(IOEvent e)
        {
            var channel = GetEncoderTemplateChannelNumber(MainEncoderTemplate);
            if (channel < 0 || channel >= 4)
            {
                channel = 0;
            }

            if (e != null && e.TryGetEncoderCount(channel, out var snapshotCount))
            {
                return snapshotCount;
            }

            return _machineController?.GetLastMainEncoderCount() ?? 0;
        }

        private static double ResolveEventEncoderSnapshotAgeMs(IOEvent e)
        {
            if (e == null || e.EncoderSnapshotTimestamp == default(DateTime) || e.Timestamp == default(DateTime))
            {
                return 0.0;
            }

            return Math.Max(0.0, (e.Timestamp - e.EncoderSnapshotTimestamp).TotalMilliseconds);
        }

        private void RefreshInterventionSignalAssignments()
        {
            var photocellMachineOffset = MainEncoderTemplate?.PhotocellMachineOffsetMm ?? 0.0;

            var assignments = InterventionPoints
                .Select(point =>
                {
                    var signalCode = string.IsNullOrWhiteSpace(point.SignalCode)
                        ? ResolveDefaultSignalCodeForAction(point.ActionType)
                        : point.SignalCode;
                    signalCode = NormalizeSignalCode(signalCode);
                    var output = ResolveOutputSignalByCode(signalCode);
                    var input = output == null
                        ? ResolveInputSignalByCode(signalCode)
                        : null;
                    var signal = output ?? input;

                    return new InterventionSignalAssignment
                    {
                        Enabled = point.Enabled,
                        PointCode = point.PointCode,
                        Description = point.Description,
                        ActionType = point.ActionType,
                        RelativeQuotaMm = point.EffectiveOffsetMm,
                        AbsoluteMachineQuotaMm = photocellMachineOffset + point.EffectiveOffsetMm,
                        SignalCode = string.IsNullOrWhiteSpace(signalCode) ? "-" : signalCode,
                        Board = string.IsNullOrWhiteSpace(signal?.Board) ? "-" : signal.Board,
                        Channel = string.IsNullOrWhiteSpace(signal?.Channel) ? "-" : signal.Channel,
                        Polarity = signal == null ? "-" : signal.Polarity.ToString(),
                        PulseMs = point.PulseMs,
                        SignalStatus = signal == null
                            ? T("Tool_VM_PointSignalMissing", "Signal not found in machine I/O table")
                            : (signal.ReservedForRealSignal
                                ? T("Tool_VM_PointSignalReal", "Real machine signal")
                                : T("Tool_VM_PointSignalTest", "Test / simulation signal"))
                    };
                })
                .OrderBy(item => item.AbsoluteMachineQuotaMm)
                .ThenBy(item => item.PointCode)
                .ToList();

            for (var index = 0; index < assignments.Count; index++)
            {
                assignments[index].OrderIndex = index + 1;
            }

            ReplaceCollection(InterventionSignalAssignments, assignments);
            OnPropertyChanged(nameof(InterventionSignalAssignments));
        }

        private void AddInterventionPoint()
        {
            var nextIndex = InterventionPoints.Count + 1;
            var newPoint = new MachineInterventionPoint
            {
                PointCode = $"CUSTOM_{nextIndex:00}",
                Description = T("Tool_DefaultPoint_Custom_Description", "New intervention point"),
                Enabled = true,
                ReferenceCode = "PRODUCT_ZERO",
                BaseOffsetMm = 0.0,
                TrimOffsetMm = 0.0,
                ActionType = "OutputPulse",
                SignalCode = string.Empty,
                PulseMs = 40,
                IsStandard = false,
                Notes = T("Tool_DefaultPoint_Custom_Notes", "Point added manually")
            };

            InterventionPoints.Add(newPoint);
            SelectedInterventionPoint = newPoint;
        }

        private void DuplicateSelectedInterventionPoint()
        {
            if (SelectedInterventionPoint == null)
            {
                return;
            }

            var source = SelectedInterventionPoint;
            var duplicate = new MachineInterventionPoint
            {
                PointCode = $"{source.PointCode}_COPY",
                Description = $"{source.Description} (copy)",
                Enabled = source.Enabled,
                ReferenceCode = source.ReferenceCode,
                BaseOffsetMm = source.BaseOffsetMm,
                TrimOffsetMm = source.TrimOffsetMm,
                ActionType = source.ActionType,
                SignalCode = source.SignalCode,
                PulseMs = source.PulseMs,
                IsStandard = false,
                Notes = source.Notes
            };

            InterventionPoints.Add(duplicate);
            SelectedInterventionPoint = duplicate;
        }

        private void RemoveSelectedInterventionPoint()
        {
            if (SelectedInterventionPoint == null)
            {
                return;
            }

            var pointToRemove = SelectedInterventionPoint;
            var currentIndex = InterventionPoints.IndexOf(pointToRemove);
            InterventionPoints.Remove(pointToRemove);

            if (InterventionPoints.Count == 0)
            {
                SelectedInterventionPoint = null;
                return;
            }

            var safeIndex = Math.Max(0, Math.Min(currentIndex, InterventionPoints.Count - 1));
            SelectedInterventionPoint = InterventionPoints[safeIndex];
        }


        private void OnInterventionPointPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(InterventionRuntimeSummary));
            OnPropertyChanged(nameof(InterventionQuotaSummary));
            OnPropertyChanged(nameof(InterventionQuotaFlowSummary));
            OnPropertyChanged(nameof(InterventionTimelineSummary));
            OnPropertyChanged(nameof(SelectedInterventionPointMachineQuotaSummary));
            OnPropertyChanged(nameof(SelectedInterventionPointRelativeQuotaSummary));
            OnPropertyChanged(nameof(SelectedInterventionPointSignalSummary));
            if (e.PropertyName == nameof(MachineInterventionPoint.SignalCode) ||
                e.PropertyName == nameof(MachineInterventionPoint.ActionType))
            {
                NotifySelectedInterventionOutputMappingChanged();
            }
            OnPropertyChanged(nameof(BoundTopCameraTriggerSignalCode));
            OnPropertyChanged(nameof(BoundSideCameraTriggerSignalCode));
            OnPropertyChanged(nameof(BoundRightCameraTriggerSignalCode));
            OnPropertyChanged(nameof(TimedTriggerOutputSummary));
            OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
            RefreshInterventionTimelineItems();
            RefreshMultiShotDerivedProperties();
            MarkConfigurationDirty();
            QueueRuntimePreviewApply($"intervention point changed ({e.PropertyName})");
        }

        private void OnInterventionPointsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<MachineInterventionPoint>())
                {
                    item.PropertyChanged -= OnInterventionPointPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<MachineInterventionPoint>())
                {
                    item.PropertyChanged += OnInterventionPointPropertyChanged;
                }
            }

            OnPropertyChanged(nameof(InterventionRuntimeSummary));
            OnPropertyChanged(nameof(InterventionQuotaSummary));
            OnPropertyChanged(nameof(InterventionQuotaFlowSummary));
            OnPropertyChanged(nameof(InterventionTimelineSummary));
            OnPropertyChanged(nameof(BoundTopCameraTriggerSignalCode));
            OnPropertyChanged(nameof(BoundSideCameraTriggerSignalCode));
            OnPropertyChanged(nameof(BoundRightCameraTriggerSignalCode));
            OnPropertyChanged(nameof(TimedTriggerOutputSummary));
            OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
            RefreshInterventionTimelineItems();
            RefreshMultiShotDerivedProperties();

            if (SelectedInterventionPoint == null && InterventionPoints.Count > 0)
            {
                SelectedInterventionPoint = InterventionPoints[0];
            }
            else if (SelectedInterventionPoint != null && !InterventionPoints.Contains(SelectedInterventionPoint))
            {
                SelectedInterventionPoint = InterventionPoints.FirstOrDefault();
            }

            MarkConfigurationDirty();
            QueueRuntimePreviewApply("intervention point collection changed");
        }

        private void OnEncoderTemplatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(MachineZeroSummary));
            OnPropertyChanged(nameof(InterventionQuotaSummary));
            OnPropertyChanged(nameof(InterventionQuotaFlowSummary));
            OnPropertyChanged(nameof(InterventionTimelineSummary));
            OnPropertyChanged(nameof(SelectedInterventionPointMachineQuotaSummary));
            OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
            RefreshInterventionTimelineItems();
            RefreshEncoderSummaryProperties();
            RefreshMultiShotDerivedProperties();
            ConfigureIoManagerActiveEncoderChannels();
            MarkConfigurationDirty();
            QueueRuntimePreviewApply($"encoder template changed ({e.PropertyName})");
        }

        private void OnEncoderTemplatesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<EncoderConfigurationTemplate>())
                {
                    item.PropertyChanged -= OnEncoderTemplatePropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<EncoderConfigurationTemplate>())
                {
                    item.PropertyChanged += OnEncoderTemplatePropertyChanged;
                }
            }

            OnPropertyChanged(nameof(MachineZeroSummary));
            OnPropertyChanged(nameof(InterventionQuotaSummary));
            OnPropertyChanged(nameof(InterventionQuotaFlowSummary));
            OnPropertyChanged(nameof(InterventionTimelineSummary));
            OnPropertyChanged(nameof(SelectedInterventionPointMachineQuotaSummary));
            RefreshInterventionTimelineItems();
            RefreshEncoderSummaryProperties();
            RefreshMultiShotDerivedProperties();
            ConfigureIoManagerActiveEncoderChannels();
            MarkConfigurationDirty();
            QueueRuntimePreviewApply("encoder template collection changed");
        }
        private void OnAdditionalRuntimeBindingsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<AdditionalRuntimeBindingDefinition>())
                {
                    item.PropertyChanged -= OnAdditionalRuntimeBindingPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<AdditionalRuntimeBindingDefinition>())
                {
                    item.PropertyChanged += OnAdditionalRuntimeBindingPropertyChanged;
                }
            }

            RefreshMachineSignalStatuses();
            MarkConfigurationDirty();
            QueueRuntimePreviewApply("additional runtime binding collection changed");
        }

        private void OnAdditionalRuntimeBindingPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AdditionalRuntimeBindingDefinition.SignalCode) ||
                e.PropertyName == nameof(AdditionalRuntimeBindingDefinition.BindingCode))
            {
                RefreshMachineSignalStatuses();
                MarkConfigurationDirty();
                QueueRuntimePreviewApply($"additional runtime binding changed ({e.PropertyName})");
            }
        }

        private void OnMachineSignalPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // IsRuntimeBound, IsAdditionalBinding, RuntimeUsageLabel, SignalModeLabel are
            // computed status properties written by RefreshMachineSignalStatuses. Calling
            // RefreshSignalViews() in response would invoke CollectionView.Refresh() while
            // a DataGrid is in AddNew/EditItem mode, throwing InvalidOperationException and
            // aborting the save. These properties carry no user-editable state.
            if (e.PropertyName == nameof(MachineSignalDefinition.IsRuntimeBound) ||
                e.PropertyName == nameof(MachineSignalDefinition.IsAdditionalBinding) ||
                e.PropertyName == nameof(MachineSignalDefinition.RuntimeUsageLabel) ||
                e.PropertyName == nameof(MachineSignalDefinition.SignalModeLabel))
            {
                return;
            }

            if (e.PropertyName == nameof(MachineSignalDefinition.ReservedForRealSignal))
            {
                MarkConfigurationDirty();
                RefreshInterventionSignalAssignments();
                if (!HasPendingConfigurationEdits())
                {
                    RefreshSignalViews();
                }
                NotifySelectedInterventionOutputMappingChanged();
                return;
            }

            if (e.PropertyName == nameof(MachineSignalDefinition.SignalCode) ||
                e.PropertyName == nameof(MachineSignalDefinition.Channel) ||
                e.PropertyName == nameof(MachineSignalDefinition.Board) ||
                e.PropertyName == nameof(MachineSignalDefinition.Polarity))
            {
                if (e.PropertyName == nameof(MachineSignalDefinition.SignalCode))
                {
                    if (!HasPendingConfigurationEdits())
                    {
                        RefreshAvailableSignalCodes();
                RefreshMachineSignalStatuses();
                NotifySelectedInterventionOutputMappingChanged();
                    }
                    RefreshInterventionSignalAssignments();
                    MarkConfigurationDirty();
                    QueueRuntimePreviewApply($"machine signal code changed ({e.PropertyName})");
                    return;
                }

                if (!HasPendingConfigurationEdits())
                {
                    RefreshSignalViews();
                }
                RefreshInterventionSignalAssignments();
                MarkConfigurationDirty();
                QueueRuntimePreviewApply($"machine signal changed ({e.PropertyName})");
            }
        }

        private void RefreshMachineSignalStatuses()
        {
            if (_isRefreshingMachineSignalStatuses)
            {
                return;
            }

            _isRefreshingMachineSignalStatuses = true;
            try
            {
                var runtimeCodes = new[]
            {
                BoundProductPhotocellSignalCode,
                BoundExternalTriggerEnableSignalCode,
                BoundCameraTriggerSignalCode,
                BoundRejectSignalCode,
                BoundGeneralAlarmSignalCode,
                BoundHeartbeatOutputSignalCode,
                SideLeftMultiShotTriggerOutputName
            }
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var additionalBindingCodes = AdditionalRuntimeBindings
                .Select(binding => binding?.SignalCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var signal in MachineInputs.Concat(MachineOutputs))
            {
                if (signal == null)
                {
                    continue;
                }

                var signalCode = signal.SignalCode ?? string.Empty;
                signal.IsRuntimeBound = runtimeCodes.Contains(signalCode);
                signal.IsAdditionalBinding = !signal.IsRuntimeBound && additionalBindingCodes.Contains(signalCode);
                signal.RuntimeUsageLabel = signal.IsRuntimeBound
                    ? T("Tool_VM_RuntimeUsagePrimary", "Primary binding active")
                    : (signal.IsAdditionalBinding
                        ? T("Tool_VM_RuntimeUsageAdditional", "Additional binding configured")
                        : T("Tool_VM_RuntimeUsageUnassigned", "Not assigned to runtime"));
                signal.SignalModeLabel = signal.ReservedForRealSignal
                    ? T("Tool_VM_SignalModeReal", "Real machine signal")
                    : T("Tool_VM_SignalModeTest", "Test / simulation signal");
            }

                OnPropertyChanged(nameof(RuntimeBindingSummary));
                RefreshInterventionSignalAssignments();
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new System.Action(() => RefreshSignalViews()));
            }
            finally
            {
                _isRefreshingMachineSignalStatuses = false;
            }
        }

        private double GetCountsPerMillimeter(int channel)
        {
            var template = EncoderTemplates.FirstOrDefault(t => GetEncoderTemplateChannelNumber(t) == channel);
            if (template == null || template.MillimetersPerRevolution <= 0)
            {
                return 1.0;
            }

            return CalculateCountsPerMillimeter(template);
        }

        private static double CalculateCountsPerMillimeter(EncoderConfigurationTemplate template)
        {
            return MachineRuntimeIo.CalculateCountsPerMillimeter(template);
        }

        private static double ResolveEncoderTrackingMultiplier(string trackingMode)
        {
            return MachineRuntimeIo.ResolveEncoderTrackingMultiplier(trackingMode);
        }

        private int GetMainEncoderChannelIndex()
        {
            var channel = GetEncoderTemplateChannelNumber(MainEncoderTemplate);
            if (channel < 0 || channel >= EncoderChannels.Count)
            {
                return 0;
            }

            return channel;
        }

        private IEnumerable<int> GetConfiguredEncoderChannels()
        {
            var channels = EncoderTemplates
                .Select(GetEncoderTemplateChannelNumber)
                .Where(channel => channel >= 0 && channel < 4)
                .Distinct()
                .ToList();

            if (channels.Count == 0)
            {
                channels.Add(0);
            }

            return channels;
        }

        private void ConfigureIoManagerActiveEncoderChannels()
        {
            _ioManager?.ConfigureActiveEncoderChannels(GetConfiguredEncoderChannels());
        }

        private async Task ArmConfiguredEncoderChannelsAsync(string reason)
        {
            if (_ioManager == null || !_isInitialized)
            {
                return;
            }

            _mainEncoderReferenceReady = false;
            var configuredChannels = GetConfiguredEncoderChannels().Distinct().ToList();
            ConfigureIoManagerActiveEncoderChannels();

            foreach (var channel in configuredChannels)
            {
                await _ioManager.StartEncoderAsync(channel);
                if (channel >= 0 && channel < EncoderChannels.Count)
                {
                    EncoderChannels[channel].IsCounting = true;
                    EncoderChannels[channel].Status = "Automatic sampling active";
                    EncoderChannels[channel].DiagnosticState = T("Tool_VM_EncoderWaitingFirstSample", "Waiting for first sample");
                }
            }

            await _ioManager.UpdateAllEncodersAsync(EncoderChannels);

            var mainChannel = GetMainEncoderChannelIndex();
            foreach (var channel in configuredChannels)
            {
                if (channel < 0 || channel >= EncoderChannels.Count)
                {
                    continue;
                }

                var encoderCount = EncoderChannels[channel].CounterValue;
                _lastEncoderCounts[channel] = encoderCount;
                _lastEncoderSampleTimes[channel] = DateTime.UtcNow;
                _filteredEncoderSpeedMetersPerMinute[channel] = 0.0;
                _encoderMetricReferenceReady[channel] = false;
                _encoderMetricWarmupUntilUtc[channel] = DateTime.UtcNow.AddSeconds(EncoderStartupWarmupSeconds);
                _machineController.UpdateEncoderPosition(channel, encoderCount);
                _multiShotTriggerController?.OnEncoderChanged(channel, encoderCount);

                if (channel == mainChannel)
                {
                    _mainEncoderReferenceReady = true;
                }
            }

            AddLogEntry(
                $"Configured encoder channels started automatically ({string.Join(",", configuredChannels.Select(channel => $"ENC{channel + 1}"))}); main reference synchronized at {_machineController.GetLastMainEncoderCount()} counts - {reason}.",
                IOLogLevel.Success,
                "ENC");
            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                LogLevel.Info,
                "ENCODER_AUTOSTART_READY",
                "Encoder",
                "Encoder automatic sampling ready",
                nameof(DigitalIOViewModel),
                $"Channels={string.Join(",", configuredChannels)}; MainChannel={mainChannel}; Reference={_machineController.GetLastMainEncoderCount()}; Reason={reason}");
        }

        private int GetEncoderTemplateChannelNumber(EncoderConfigurationTemplate template)
        {
            return MachineRuntimeIo.GetEncoderChannelNumber(template);
        }
        private async Task SaveLogToFileAsync()
        {
            try
            {
                var cfgFolder = System.IO.Path.GetDirectoryName(_machineConfigurationService.GetConfigurationFilePath());
                System.IO.Directory.CreateDirectory(cfgFolder);
                var filePath = System.IO.Path.Combine(cfgFolder, $"io-diagnostic-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                var lines = LogEntries.Select(entry => $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}").ToArray();
                await Task.Run(() => System.IO.File.WriteAllLines(filePath, lines));
            }
            catch (Exception ex)
            {
                AddLogEntry($"Error while saving log: {ex.Message}", IOLogLevel.Error, "LOG", ex);
            }
        }

        private async Task InitializeMachineRuntimeAsync()
        {
            _machineConfiguration = _machineConfigurationService.LoadOrCreateDefault(
                MachineInputs,
                MachineOutputs,
                EncoderTemplates,
                InterventionPoints);

            _lastPersistedMultiShotConfig = _machineConfiguration?.MachineMultiShotTrigger;
            _suppressConfigurationDirtyTracking = true;
            CancelPendingTimedTriggerBatches("machine runtime initialization");
            ApplyRuntimeConfigurationToView(_machineConfiguration);
            EnsureHeartbeatSignalExists();
            if (!InterventionPoints.Any())
            {
                EnsureDefaultInterventionPoints();
            }
            _machineConfiguration = BuildRuntimeConfigurationFromView();
            var configurationSchemaUpdated = _machineConfigurationService.EnsureLatestRuntimeBindingsSchema(_machineConfiguration);
            if (configurationSchemaUpdated)
            {
                // L'aggiornamento avveniva solo in memoria e piu' sotto MarkConfigurationSaved()
                // marcava la configurazione come pulita senza averla scritta: i campi mancanti
                // venivano ricreati a ogni avvio e mai persistiti, quindi in
                // machine_runtime_config.xml restavano assenti e chi apre il file (o lo
                // visualizza dal pannello) non li trova. La riga successiva scrive sul
                // TEMPLATE, non sulla configurazione attiva. Qui si persiste quella attiva.
                try
                {
                    _machineConfigurationService.Save(_machineConfiguration);
                    AddLogEntry(
                        "Active machine runtime configuration upgraded to include timed-trigger runtime bindings and persisted.",
                        IOLogLevel.Info,
                        "CONFIG");
                }
                catch (Exception ex)
                {
                    AddLogEntry(
                        $"CONFIG_SCHEMA_UPGRADE_NOT_PERSISTED|{ex.Message}",
                        IOLogLevel.Warning,
                        "CONFIG",
                        ex);
                }
            }

            _machineConfigurationService.EnsureLatestRuntimeBindingsSchema(_machineConfiguration, _machineConfigurationService.GetTemplateConfigurationFilePath());
            _suppressConfigurationDirtyTracking = false;
            await ConfigureEffectiveRuntimeAsync(
                _machineConfiguration,
                MainWindow.ConfigRecipeParam?.Config,
                "machine runtime initialization");
            MainWindow.ApplyVisionProStitchingParametersForLeftJobs(
                $"{nameof(DigitalIOViewModel)} machine runtime initialization",
                _machineConfiguration);
            ClearTriggerDiagnostics();
            ClearTimedTriggerDiagnostics();
            ValidateCameraTriggerOutputMappings();
            ValidateTriggerExecutionConfiguration();
            RebuildTimedTriggerFastPathSnapshot();
            await ParkUnusedSpareOutputsLowAsync("machine runtime initialization");
            MachineControllerStatus = _machineController.Status;
            OnPropertyChanged(nameof(MachineConfigurationPath));
            RefreshMachineSignalStatuses();
            MarkConfigurationSaved();
            await EvaluateHeartbeatAutoStartAsync("machine runtime initialization");
            UpdateVirtualConveyorTimerState();
            await ArmConfiguredEncoderChannelsAsync("machine runtime initialization");
            AddLogEntry($"Runtime macchina inizializzato. TriggerMode={MachineTriggerMode}, UseEncoderTrigger={UseEncoderTrigger}, TriggerSchedulingMode={TriggerSchedulingMode}, RejectMode={MachineRejectMode}, MainEncoder={BoundMainEncoderAxisCode}, Heartbeat={BoundHeartbeatOutputSignalCode}", IOLogLevel.Info, "CONFIG");
        }

        private MachineRuntimeConfiguration BuildRuntimeConfigurationFromView()
        {
            var configuration = _machineConfigurationService.BuildDefault(
                MachineInputs,
                MachineOutputs,
                EncoderTemplates,
                InterventionPoints);

            if (_machineConfiguration != null)
            {
                configuration.ConfigurationName = _machineConfiguration.ConfigurationName;
                configuration.ConfigurationVersion = _machineConfiguration.ConfigurationVersion;
                configuration.DigitalIoBoard = _machineConfiguration.DigitalIoBoard;
                configuration.EncoderBoard = _machineConfiguration.EncoderBoard;
                configuration.Notes = _machineConfiguration.Notes;
            }

            configuration.MachineMultiShotTrigger = BuildMultiShotConfigurationFromView();

            configuration.TriggerMode = MachineTriggerMode;
            configuration.RejectMode = MachineRejectMode;

            // I binding runtime NON vengono piu' ricostruiti da zero: si parte dall'istanza gia'
            // caricata e si sovrascrivono soltanto i campi che questa vista possiede davvero.
            // Ricreare l'oggetto azzerava silenziosamente ogni campo non elencato qui - cioe' tutta
            // la configurazione AI (flag servizi, percorsi/versione dei modelli ONNX addestrati,
            // soglie MachineHealth*, retention), i parametri Email/SMTP e, non advisory,
            // MultiShotCompanionMaxLagMs e LivePreviewExposureUs. Il metodo viene invocato ad ogni
            // avvio (InitializeMachineRuntimeAsync) e ad ogni salvataggio/anteprima del pannello
            // I/O, quindi la perdita si ripresentava ad ogni riavvio della macchina.
            // Mantenere il merge: aggiungendo un nuovo campo a MachineRuntimeBindings deve bastare
            // non toccare questo metodo perche' il valore sopravviva.
            var runtimeBindings = _machineConfiguration?.RuntimeBindings ?? new MachineRuntimeBindings();

            runtimeBindings.UseEncoderTrigger = UseEncoderTrigger;
            runtimeBindings.UseEncoderTriggerSpecified = true;
            runtimeBindings.TriggerSchedulingMode = TriggerSchedulingMode;
            runtimeBindings.ProductPhotocellSignalCode = BoundProductPhotocellSignalCode;
            runtimeBindings.ExternalTriggerEnableSignalCode = BoundExternalTriggerEnableSignalCode;
            runtimeBindings.CameraTriggerSignalCode = BoundCameraTriggerSignalCode;
            runtimeBindings.RejectSignalCode = BoundRejectSignalCode;
            runtimeBindings.GeneralAlarmSignalCode = BoundGeneralAlarmSignalCode;
            runtimeBindings.MainEncoderAxisCode = BoundMainEncoderAxisCode;
            runtimeBindings.HeartbeatOutputSignalCode = BoundHeartbeatOutputSignalCode;
            runtimeBindings.HeartbeatEnabled = HeartbeatEnabled;
            runtimeBindings.HeartbeatAutoStartOnRealHardware = HeartbeatAutoStartOnRealHardware;
            runtimeBindings.HeartbeatIntervalMs = HeartbeatIntervalMs;
            runtimeBindings.ProductQueueCapacity = _machineConfiguration?.RuntimeBindings?.ProductQueueCapacity ?? 200;
            runtimeBindings.RejectPulseMs = ConfiguredRejectPulseMs;
            runtimeBindings.RejectActuatorType = RejectActuatorType;
            runtimeBindings.RejectOpenSignalCode = BoundRejectOpenSignalCode;
            runtimeBindings.RejectCloseSignalCode = BoundRejectCloseSignalCode;
            runtimeBindings.RejectCloseDelayMs = ConfiguredRejectCloseDelayMs;
            runtimeBindings.PhotocellDebounceMs = PhotocellDebounceMs;
            runtimeBindings.MinimumRetriggerGapMs = MinimumRetriggerGapMs;
            runtimeBindings.TopTriggerBaseDelayMs = TopTriggerBaseDelayMs;
            runtimeBindings.SideTriggerBaseDelayMs = SideTriggerBaseDelayMs;
            runtimeBindings.TopTriggerPulseMs = TopTriggerPulseMs;
            runtimeBindings.SideTriggerPulseMs = SideTriggerPulseMs;
            runtimeBindings.VirtualConveyorEnabled = VirtualConveyorEnabled;
            runtimeBindings.VirtualConveyorSpeedMetersPerMinute = SimulationTransportSpeedMetersPerMinute;
            runtimeBindings.VirtualConveyorUsePiecesPerMinute = SimulationUsePiecesPerMinute;
            runtimeBindings.VirtualConveyorPiecesPerMinute = SimulationPiecesPerMinute;
            runtimeBindings.VirtualConveyorProductPitchMm = SimulationProductPitchMm;
            runtimeBindings.EncoderCalibrationTachometerSpeedMetersPerMinute = this.EncoderCalibrationTachometerSpeedMetersPerMinute;

            configuration.RuntimeBindings = runtimeBindings;

                        configuration.InterventionPoints = InterventionPoints
                .Select(point => new MachineInterventionPoint
                {
                    PointCode = point.PointCode,
                    Description = point.Description,
                    Enabled = point.Enabled,
                    ReferenceCode = point.ReferenceCode,
                    BaseOffsetMm = point.BaseOffsetMm,
                    TrimOffsetMm = point.TrimOffsetMm,
                    ActionType = point.ActionType,
                    SignalCode = point.SignalCode,
                    PulseMs = point.PulseMs,
                    IsStandard = point.IsStandard,
                    Notes = point.Notes
                })
                .ToList();

            configuration.AdditionalRuntimeBindings = AdditionalRuntimeBindings
                .Select(binding => new AdditionalRuntimeBindingDefinition
                {
                    BindingCode = binding.BindingCode,
                    Description = binding.Description,
                    SignalCode = binding.SignalCode,
                    Notes = binding.Notes
                })
                .ToList();

            return configuration;
        }

        private void ApplyRuntimeConfigurationToView(MachineRuntimeConfiguration configuration)
        {
            var previousSuppressConfigurationDirtyTracking = _suppressConfigurationDirtyTracking;
            _suppressConfigurationDirtyTracking = true;
            try
            {
                ReplaceCollection(MachineInputs, configuration?.MachineInputs);
                ReplaceCollection(MachineOutputs, configuration?.MachineOutputs);
                ReplaceCollection(EncoderTemplates, configuration?.EncoderTemplates);
                ReplaceCollection(InterventionPoints, configuration?.InterventionPoints);
                SelectedInterventionPoint = InterventionPoints.FirstOrDefault();
                ReplaceCollection(AdditionalRuntimeBindings, configuration?.AdditionalRuntimeBindings);
                EnsureHeartbeatSignalExists();
                RefreshAvailableSignalCodes();

                MachineTriggerMode = configuration?.TriggerMode ?? "Hybrid";
                MachineRejectMode = configuration?.RejectMode ?? "EncoderTracked";
                TriggerSchedulingMode = configuration?.RuntimeBindings?.TriggerSchedulingMode ?? "VirtualConveyor";
                BoundProductPhotocellSignalCode = configuration?.RuntimeBindings?.ProductPhotocellSignalCode ?? "IN_PRODUCT_PHOTOCELL";
                BoundExternalTriggerEnableSignalCode = configuration?.RuntimeBindings?.ExternalTriggerEnableSignalCode ?? "IN_EXTERNAL_TRIGGER_ENABLE";
                BoundCameraTriggerSignalCode = configuration?.RuntimeBindings?.CameraTriggerSignalCode ?? "OUT_CAMERA_TOP_TRIGGER";
                BoundRejectSignalCode = configuration?.RuntimeBindings?.RejectSignalCode ?? "OUT_REJECT_SOLENOID";
                BoundGeneralAlarmSignalCode = configuration?.RuntimeBindings?.GeneralAlarmSignalCode ?? "OUT_GENERAL_ALARM";
                BoundMainEncoderAxisCode = configuration?.RuntimeBindings?.MainEncoderAxisCode ?? "ENC_CONVEYOR_MAIN";
                BoundHeartbeatOutputSignalCode = configuration?.RuntimeBindings?.HeartbeatOutputSignalCode ?? "OUT_HEARTBEAT";
                HeartbeatEnabled = configuration?.RuntimeBindings?.HeartbeatEnabled ?? true;
                HeartbeatAutoStartOnRealHardware = configuration?.RuntimeBindings?.HeartbeatAutoStartOnRealHardware ?? true;
                HeartbeatIntervalMs = configuration?.RuntimeBindings?.HeartbeatIntervalMs ?? 500;
                ConfiguredRejectPulseMs = configuration?.RuntimeBindings?.RejectPulseMs ?? 80;
                BoundRejectOpenSignalCode = string.IsNullOrWhiteSpace(configuration?.RuntimeBindings?.RejectOpenSignalCode)
                    ? "OUT_REJECT_OPEN"
                    : configuration.RuntimeBindings.RejectOpenSignalCode;
                BoundRejectCloseSignalCode = string.IsNullOrWhiteSpace(configuration?.RuntimeBindings?.RejectCloseSignalCode)
                    ? "OUT_REJECT_CLOSE"
                    : configuration.RuntimeBindings.RejectCloseSignalCode;
                RejectActuatorType = configuration?.RuntimeBindings?.RejectActuatorType ??
                    (IsRejectOpenCloseConfigured(out _, out _) || HasRejectOpenCloseChannelConflict()
                        ? "Bistable" : "Monostable");
                if (RejectActuatorType == "Bistable")
                {
                    AddLogEntry("REJECT_BISTABLE_DISABLED|position sensors OPEN/CLOSE are not implemented; reject pulses inhibited", IOLogLevel.Warning, "CONFIG");
                }
                ConfiguredRejectCloseDelayMs = configuration?.RuntimeBindings?.RejectCloseDelayMs ?? 300;
                PhotocellDebounceMs = configuration?.RuntimeBindings?.PhotocellDebounceMs ?? 80;
                MinimumRetriggerGapMs = configuration?.RuntimeBindings?.MinimumRetriggerGapMs ?? 120;
                TopTriggerBaseDelayMs = configuration?.RuntimeBindings?.TopTriggerBaseDelayMs ?? 0;
                SideTriggerBaseDelayMs = configuration?.RuntimeBindings?.SideTriggerBaseDelayMs ?? 0;
                TopTriggerPulseMs = configuration?.RuntimeBindings?.TopTriggerPulseMs ?? 40;
                SideTriggerPulseMs = configuration?.RuntimeBindings?.SideTriggerPulseMs ?? 40;
                VirtualConveyorEnabled = configuration?.RuntimeBindings?.VirtualConveyorEnabled ?? true;
                UseEncoderTrigger = ResolveUseEncoderTriggerFromLegacyMode(configuration?.RuntimeBindings);
                SimulationTransportSpeedMetersPerMinute = configuration?.RuntimeBindings?.VirtualConveyorSpeedMetersPerMinute ?? 18.0;
                SimulationUsePiecesPerMinute = configuration?.RuntimeBindings?.VirtualConveyorUsePiecesPerMinute ?? false;
                SimulationPiecesPerMinute = configuration?.RuntimeBindings?.VirtualConveyorPiecesPerMinute ?? 120.0;
                SimulationProductPitchMm = configuration?.RuntimeBindings?.VirtualConveyorProductPitchMm ?? 150.0;
                EncoderCalibrationTachometerSpeedMetersPerMinute = configuration?.RuntimeBindings?.EncoderCalibrationTachometerSpeedMetersPerMinute ?? 40.0;
                ApplyMultiShotConfigurationToView(configuration?.MachineMultiShotTrigger);
                ConfigureIoManagerActiveEncoderChannels();
            }
            finally
            {
                _suppressConfigurationDirtyTracking = previousSuppressConfigurationDirtyTracking;
            }

            OnPropertyChanged(nameof(RejectModeOperatingSummary));
            NotifyTriggerSourceProperties();
            OnPropertyChanged(nameof(BoundTopCameraTriggerSignalCode));
            OnPropertyChanged(nameof(BoundSideCameraTriggerSignalCode));
            OnPropertyChanged(nameof(BoundRightCameraTriggerSignalCode));
            OnPropertyChanged(nameof(PrimaryMachineFlowSummary));
            OnPropertyChanged(nameof(OptionalBindingsSummary));
            OnPropertyChanged(nameof(TimedTriggerConfigurationSummary));
            OnPropertyChanged(nameof(InterventionRuntimeSummary));
            OnPropertyChanged(nameof(MachineZeroSummary));
            OnPropertyChanged(nameof(InterventionQuotaSummary));
            OnPropertyChanged(nameof(HeartbeatTargetSummary));
            OnPropertyChanged(nameof(HeartbeatStatusSummary));
            RefreshMachineSignalStatuses();
            MigrateLegacyTwoCameraRuntimeConfiguration();
            RefreshAvailableSignalCodes();
            RefreshMachineSignalStatuses();
            RebuildTimedTriggerFastPathSnapshot();
        }

        private MachineMultiShotTriggerConfiguration BuildMultiShotConfigurationFromView()
        {
            PersistCurrentMultiShotEditorProfile();
            return new MachineMultiShotTriggerConfiguration
            {
                Side = BuildProfileForSave("Side"),
                Right = BuildProfileForSave("Right"),
                Rear = BuildProfileForSave("Rear"),
                Bottom = BuildProfileForSave("Bottom")
            };
        }

        private MultiShotTriggerOptions BuildProfileForSave(string profileKey)
        {
            var options = CloneMultiShotOptions(ResolveMultiShotProfileOption(profileKey));
            options.InitialOffsetPulses = ResolveMultiShotInitialOffsetPulses(profileKey, options.InitialOffsetPulses);
            options.StepPulses = ResolveMultiShotStepPulses(
                profileKey,
                options.VisionProStitching?.StepMm ?? 0.0,
                options.ShotCount,
                options.StepPulses);
            return options;
        }

        private void PersistCurrentMultiShotEditorProfile()
        {
            var profileKey = string.IsNullOrWhiteSpace(SelectedMultiShotProfileKey) ? "Side" : SelectedMultiShotProfileKey;
            var existing = ResolveMultiShotProfileOption(profileKey);
            var existingStitching = existing?.VisionProStitching ?? new MultiShotVisionProStitchingOptions();
            var existingDualIllumination = existing?.DualIllumination ?? new MultiShotDualIlluminationOptions();
            var defaults = CreateDefaultMultiShotOptions(profileKey);

            _multiShotProfileEditors[profileKey] = new MultiShotTriggerOptions
            {
                Enabled = SideLeftMultiShotEnabled,
                CameraRole = string.IsNullOrWhiteSpace(existing?.CameraRole) ? defaults.CameraRole : existing.CameraRole,
                DisplayName = string.IsNullOrWhiteSpace(existing?.DisplayName) ? defaults.DisplayName : existing.DisplayName,
                TriggerOutputName = string.IsNullOrWhiteSpace(SideLeftMultiShotTriggerOutputName) ? defaults.TriggerOutputName : SideLeftMultiShotTriggerOutputName,
                TriggerPulseMs = SideLeftMultiShotTriggerPulseMs,
                MinimumInterShotIntervalMs = SideLeftMultiShotMinimumInterShotIntervalMs,
                ShotCount = SideLeftMultiShotShotCount,
                InitialOffsetPulses = ResolveMultiShotInitialOffsetPulses(profileKey, SideLeftMultiShotInitialOffsetPulses),
                StepPulses = ResolveMultiShotStepPulses(profileKey, SideLeftVisionProStepMm, SideLeftMultiShotShotCount, SideLeftMultiShotStepPulses),
                MaxShotCount = SideLeftMultiShotMaxShotCount,
                SessionTimeoutMs = SideLeftMultiShotSessionTimeoutMs,
                TargetLateTolerancePulses = existing?.TargetLateTolerancePulses > 0 ? existing.TargetLateTolerancePulses : 500,
                RequireMachineRunning = SideLeftMultiShotRequireMachineRunning,
                PositiveDirection = SideLeftMultiShotPositiveDirection,
                VisionProStitching = new MultiShotVisionProStitchingOptions
                {
                    ToolBlockName = string.IsNullOrWhiteSpace(existingStitching.ToolBlockName) ? "ImageStitching" : existingStitching.ToolBlockName,
                    ExpectedFramesInputName = string.IsNullOrWhiteSpace(existingStitching.ExpectedFramesInputName) ? "expectedFrames" : existingStitching.ExpectedFramesInputName,
                    StepMmInputName = string.IsNullOrWhiteSpace(existingStitching.StepMmInputName) ? "stepMm" : existingStitching.StepMmInputName,
                    MmPerPixelInputName = string.IsNullOrWhiteSpace(existingStitching.MmPerPixelInputName) ? "mmPerPixel" : existingStitching.MmPerPixelInputName,
                    StepMm = SideLeftVisionProStepMm,
                    MmPerPixel = SideLeftVisionProMmPerPixel
                },
                DualIllumination = new MultiShotDualIlluminationOptions
                {
                    Enabled = SideLeftDualIlluminationEnabled,
                    FrontFirst = SideLeftDualIlluminationFrontFirst,
                    ExposureTimeUs = SideLeftDualIlluminationExposureUs,
                    FrontOutputLine = SideLeftDualIlluminationFrontOutputLine,
                    BackOutputLine = SideLeftDualIlluminationBackOutputLine,
                    ActiveOutputSource = SideLeftDualIlluminationOutputSource,
                    DualIlluminationEnabledInputName = string.IsNullOrWhiteSpace(existingDualIllumination.DualIlluminationEnabledInputName)
                        ? "dualIlluminationEnabled"
                        : existingDualIllumination.DualIlluminationEnabledInputName,
                    FrontFirstInputName = string.IsNullOrWhiteSpace(existingDualIllumination.FrontFirstInputName)
                        ? "frontFirst"
                        : existingDualIllumination.FrontFirstInputName
                }
            };
        }

        private MultiShotTriggerOptions ResolveCurrentMultiShotEditorProfile()
        {
            return ResolveMultiShotProfileOption(SelectedMultiShotProfileKey);
        }

        private MultiShotTriggerOptions ResolveMultiShotProfileOption(string profileKey)
        {
            profileKey = string.IsNullOrWhiteSpace(profileKey) ? "Side" : profileKey;
            if (_multiShotProfileEditors.TryGetValue(profileKey, out var profile) && profile != null)
            {
                return profile;
            }

            var fromConfiguration = ResolveMultiShotProfileFromConfiguration(_machineConfiguration?.MachineMultiShotTrigger, profileKey);
            if (fromConfiguration != null)
            {
                var clone = CloneMultiShotOptions(fromConfiguration);
                _multiShotProfileEditors[profileKey] = clone;
                return clone;
            }

            var defaultProfile = CreateDefaultMultiShotOptions(profileKey);
            _multiShotProfileEditors[profileKey] = defaultProfile;
            return defaultProfile;
        }

        private static MultiShotTriggerOptions ResolveMultiShotProfileFromConfiguration(MachineMultiShotTriggerConfiguration configuration, string profileKey)
        {
            if (configuration == null)
            {
                return null;
            }

            if (string.Equals(profileKey, "Right", StringComparison.OrdinalIgnoreCase))
            {
                return configuration.Right;
            }
            if (string.Equals(profileKey, "Rear", StringComparison.OrdinalIgnoreCase))
            {
                return configuration.Rear;
            }

            if (string.Equals(profileKey, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return configuration.Bottom;
            }

            return configuration.Side;
        }

        private static MultiShotTriggerOptions CreateDefaultMultiShotOptions(string profileKey)
        {
            if (string.Equals(profileKey, "Right", StringComparison.OrdinalIgnoreCase))
            {
                return MultiShotTriggerOptions.CreateDefault("Rear", "Right", "OUT_CAMERA_REAR_TRIGGER");
            }
            if (string.Equals(profileKey, "Rear", StringComparison.OrdinalIgnoreCase))
            {
                return MultiShotTriggerOptions.CreateDefault("Rear", "Rear", "OUT_CAMERA_REAR_TRIGGER");
            }

            if (string.Equals(profileKey, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return MultiShotTriggerOptions.CreateDefault("Bottom", "Bottom", "OUT_CAMERA_BOTTOM_TRIGGER");
            }

            return MultiShotTriggerOptions.CreateDefault("Side", "Left", "OUT_CAMERA_SIDE_TRIGGER");
        }

        private static MultiShotTriggerOptions CloneMultiShotOptions(MultiShotTriggerOptions source)
        {
            source = source ?? new MultiShotTriggerOptions();
            var stitching = source.VisionProStitching ?? new MultiShotVisionProStitchingOptions();
            var dualIllumination = source.DualIllumination ?? new MultiShotDualIlluminationOptions();
            return new MultiShotTriggerOptions
            {
                Enabled = source.Enabled,
                CameraRole = source.CameraRole,
                DisplayName = source.DisplayName,
                TriggerOutputName = source.TriggerOutputName,
                TriggerPulseMs = source.TriggerPulseMs,
                MinimumInterShotIntervalMs = source.MinimumInterShotIntervalMs,
                ShotCount = source.ShotCount,
                InitialOffsetPulses = source.InitialOffsetPulses,
                StepPulses = source.StepPulses,
                MaxShotCount = source.MaxShotCount,
                SessionTimeoutMs = source.SessionTimeoutMs,
                TargetLateTolerancePulses = source.TargetLateTolerancePulses,
                RequireMachineRunning = source.RequireMachineRunning,
                PositiveDirection = source.PositiveDirection,
                VisionProStitching = new MultiShotVisionProStitchingOptions
                {
                    ToolBlockName = stitching.ToolBlockName,
                    ExpectedFramesInputName = stitching.ExpectedFramesInputName,
                    StepMmInputName = stitching.StepMmInputName,
                    MmPerPixelInputName = stitching.MmPerPixelInputName,
                    StepMm = stitching.StepMm,
                    MmPerPixel = stitching.MmPerPixel
                },
                DualIllumination = new MultiShotDualIlluminationOptions
                {
                    Enabled = dualIllumination.Enabled,
                    FrontFirst = dualIllumination.FrontFirst,
                    ExposureTimeUs = dualIllumination.ExposureTimeUs,
                    FrontOutputLine = dualIllumination.FrontOutputLine,
                    BackOutputLine = dualIllumination.BackOutputLine,
                    ActiveOutputSource = dualIllumination.ActiveOutputSource,
                    DualIlluminationEnabledInputName = dualIllumination.DualIlluminationEnabledInputName,
                    FrontFirstInputName = dualIllumination.FrontFirstInputName
                }
            };
        }

        private void RefreshMultiShotDerivedProperties()
        {
            OnPropertyChanged(nameof(SideLeftMultiShotConfigurationSummary));
            OnPropertyChanged(nameof(SideLeftMultiShotGeometrySummary));
            OnPropertyChanged(nameof(SideLeftMultiShotCameraOffsetMm));
            OnPropertyChanged(nameof(SideLeftMultiShotCountsPerMillimeter));
            OnPropertyChanged(nameof(ResolvedSideLeftMultiShotInitialOffsetPulses));
            OnPropertyChanged(nameof(ResolvedSideLeftMultiShotStepPulses));
            OnPropertyChanged(nameof(SideLeftVisionProStitchingSummary));
            OnPropertyChanged(nameof(SideLeftDualIlluminationSummary));
        }

        private long ResolveSideLeftInitialOffsetPulses()
        {
            return ResolveMultiShotInitialOffsetPulses(SelectedMultiShotProfileKey, SideLeftMultiShotInitialOffsetPulses);
        }

        private long ResolveMultiShotInitialOffsetPulses(string profileKey, long fallbackPulses)
        {
            var cameraOffsetMm = ResolveMultiShotCameraOffsetMm(profileKey);
            var countsPerMillimeter = ResolveMainEncoderCountsPerMillimeter();
            if (cameraOffsetMm.HasValue && countsPerMillimeter > 0.0)
            {
                return Math.Max(0, (long)Math.Round(cameraOffsetMm.Value * countsPerMillimeter, MidpointRounding.AwayFromZero));
            }

            return Math.Max(0, fallbackPulses);
        }

        private long ResolveSideLeftStepPulses()
        {
            return ResolveMultiShotStepPulses(
                SelectedMultiShotProfileKey,
                SideLeftVisionProStepMm,
                SideLeftMultiShotShotCount,
                SideLeftMultiShotStepPulses);
        }

        private long ResolveMultiShotStepPulses(string profileKey, double stepMm, int shotCount, long fallbackPulses)
        {
            var countsPerMillimeter = ResolveMainEncoderCountsPerMillimeter();
            if (stepMm > 0.0 && countsPerMillimeter > 0.0)
            {
                var pulses = (long)Math.Round(stepMm * countsPerMillimeter, MidpointRounding.AwayFromZero);
                return shotCount > 1 ? Math.Max(1, pulses) : Math.Max(0, pulses);
            }

            return Math.Max(0, fallbackPulses);
        }

        private double? ResolveSideLeftCameraOffsetMm()
        {
            return ResolveMultiShotCameraOffsetMm(SelectedMultiShotProfileKey);
        }

        private double? ResolveMultiShotCameraOffsetMm(string profileKey)
        {
            var point = ResolveMultiShotCameraOffsetPoint(profileKey);

            if (point != null)
            {
                return Math.Max(0.0, point.EffectiveOffsetMm);
            }

            return null;
        }

        private string ResolveSideLeftCameraOffsetPointCode()
        {
            var point = ResolveMultiShotCameraOffsetPoint(SelectedMultiShotProfileKey);
            if (point != null)
            {
                return point.PointCode;
            }

            if (string.Equals(SelectedMultiShotProfileKey, "Right", StringComparison.OrdinalIgnoreCase))
            {
                return "CAMERA_TRIGGER_RIGHT/CAMERA_TRIGGER_REAR";
            }

            if (string.Equals(SelectedMultiShotProfileKey, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return "CAMERA_TRIGGER_BOTTOM";
            }

            return "CAMERA_TRIGGER_LEFT/CAMERA_TRIGGER_SIDE";
        }

        private MachineInterventionPoint ResolveMultiShotCameraOffsetPoint(string profileKey)
        {
            if (string.Equals(profileKey, "Right", StringComparison.OrdinalIgnoreCase))
            {
                return InterventionPoints.FirstOrDefault(item =>
                           item.Enabled &&
                           string.Equals(item.PointCode, "CAMERA_TRIGGER_RIGHT", StringComparison.OrdinalIgnoreCase))
                       ?? InterventionPoints.FirstOrDefault(item =>
                           item.Enabled &&
                           string.Equals(item.PointCode, "CAMERA_TRIGGER_REAR", StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(profileKey, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return InterventionPoints.FirstOrDefault(item =>
                    item.Enabled &&
                    string.Equals(item.PointCode, "CAMERA_TRIGGER_BOTTOM", StringComparison.OrdinalIgnoreCase));
            }

            return InterventionPoints.FirstOrDefault(item =>
                       item.Enabled &&
                       string.Equals(item.PointCode, "CAMERA_TRIGGER_LEFT", StringComparison.OrdinalIgnoreCase))
                   ?? InterventionPoints.FirstOrDefault(item =>
                       item.Enabled &&
                       string.Equals(item.PointCode, "CAMERA_TRIGGER_SIDE", StringComparison.OrdinalIgnoreCase));
        }

        private double ResolveMainEncoderCountsPerMillimeter()
        {
            var encoder = MainEncoderTemplate;
            return encoder == null ? 0.0 : CalculateCountsPerMillimeter(encoder);
        }

        private void ApplyMultiShotConfigurationToView(MachineMultiShotTriggerConfiguration configuration)
        {
            _multiShotProfileEditors.Clear();
            _multiShotProfileEditors["Side"] = CloneMultiShotOptions(configuration?.Side ?? CreateDefaultMultiShotOptions("Side"));
            _multiShotProfileEditors["Right"] = CloneMultiShotOptions(configuration?.Right ?? CreateDefaultMultiShotOptions("Right"));
            _multiShotProfileEditors["Rear"] = CloneMultiShotOptions(configuration?.Rear ?? CreateDefaultMultiShotOptions("Rear"));
            _multiShotProfileEditors["Bottom"] = CloneMultiShotOptions(configuration?.Bottom ?? CreateDefaultMultiShotOptions("Bottom"));

            ApplyMultiShotEditorProfile(ResolveCurrentMultiShotEditorProfile());
        }

        private void ApplyMultiShotEditorProfile(MultiShotTriggerOptions options)
        {
            options = options ?? CreateDefaultMultiShotOptions(SelectedMultiShotProfileKey);
            var defaults = CreateDefaultMultiShotOptions(SelectedMultiShotProfileKey);
            var stitching = options.VisionProStitching ?? new MultiShotVisionProStitchingOptions();
            var dualIllumination = options.DualIllumination ?? new MultiShotDualIlluminationOptions();

            _isApplyingMultiShotEditorProfile = true;
            try
            {
                SideLeftMultiShotEnabled = options.Enabled;
                SideLeftMultiShotTriggerOutputName = string.IsNullOrWhiteSpace(options.TriggerOutputName) ? defaults.TriggerOutputName : options.TriggerOutputName;
                SideLeftMultiShotTriggerPulseMs = options.TriggerPulseMs > 0 ? options.TriggerPulseMs : 10;
                SideLeftMultiShotMinimumInterShotIntervalMs = options.MinimumInterShotIntervalMs >= 0 ? options.MinimumInterShotIntervalMs : 30;
                SideLeftMultiShotMaxShotCount = options.MaxShotCount > 0 ? options.MaxShotCount : 12;
                SideLeftMultiShotShotCount = options.ShotCount > 0 ? Math.Min(options.ShotCount, SideLeftMultiShotMaxShotCount) : 1;
                SideLeftMultiShotInitialOffsetPulses = Math.Max(0, options.InitialOffsetPulses);
                SideLeftMultiShotStepPulses = Math.Max(0, options.StepPulses);
                SideLeftMultiShotSessionTimeoutMs = options.SessionTimeoutMs > 0 ? options.SessionTimeoutMs : 3000;
                SideLeftMultiShotRequireMachineRunning = options.RequireMachineRunning;
                SideLeftMultiShotPositiveDirection = options.PositiveDirection;
                SideLeftVisionProStepMm = Math.Max(0.0, stitching.StepMm);
                SideLeftVisionProMmPerPixel = Math.Max(0.0, stitching.MmPerPixel);
                SideLeftDualIlluminationEnabled = dualIllumination.Enabled;
                SideLeftDualIlluminationFrontFirst = dualIllumination.FrontFirst;
                SideLeftDualIlluminationExposureUs = dualIllumination.ExposureTimeUs > 0.0
                    ? dualIllumination.ExposureTimeUs
                    : 500.0;
                SideLeftDualIlluminationFrontOutputLine = string.IsNullOrWhiteSpace(dualIllumination.FrontOutputLine)
                    ? "Line3"
                    : dualIllumination.FrontOutputLine;
                SideLeftDualIlluminationBackOutputLine = string.IsNullOrWhiteSpace(dualIllumination.BackOutputLine)
                    ? "Line4"
                    : dualIllumination.BackOutputLine;
                SideLeftDualIlluminationOutputSource = string.IsNullOrWhiteSpace(dualIllumination.ActiveOutputSource)
                    ? "ExposureActive"
                    : dualIllumination.ActiveOutputSource;
            }
            finally
            {
                _isApplyingMultiShotEditorProfile = false;
            }

            RefreshMultiShotDerivedProperties();
            OnPropertyChanged(nameof(SideLeftVisionProStitchingSummary));
            OnPropertyChanged(nameof(SideLeftDualIlluminationSummary));
        }

        private void RefreshMultiShotPreviousSnapshots(MachineMultiShotTriggerConfiguration config)
        {
            if (config == null) return;
            _multiShotPreviousSnapshots["Side"]   = MultiShotProfileSnapshot.FromOptions(config.Side);
            _multiShotPreviousSnapshots["Right"]  = MultiShotProfileSnapshot.FromOptions(config.Right);
            _multiShotPreviousSnapshots["Rear"]   = MultiShotProfileSnapshot.FromOptions(config.Rear);
            _multiShotPreviousSnapshots["Bottom"] = MultiShotProfileSnapshot.FromOptions(config.Bottom);
            OnPropertyChanged(nameof(CurrentMultiShotPreviousSnapshot));
        }

        private async Task PersistMultiShotHistoryToDbAsync(MachineMultiShotTriggerConfiguration config, string actor)
        {
            using (var conn = await MultiShotParamHistoryRepository.OpenConnectionAsync())
            {
                await MultiShotParamHistoryRepository.SaveSnapshotAsync(
                    conn, "Side",   MultiShotProfileSnapshot.FromOptions(config.Side).ToDictionary(),   actor);
                await MultiShotParamHistoryRepository.SaveSnapshotAsync(
                    conn, "Right",  MultiShotProfileSnapshot.FromOptions(config.Right).ToDictionary(),  actor);
                await MultiShotParamHistoryRepository.SaveSnapshotAsync(
                    conn, "Bottom", MultiShotProfileSnapshot.FromOptions(config.Bottom).ToDictionary(), actor);
            }
        }

        private async Task LoadMultiShotHistoryFromDbAsync()
        {
            using (var conn = await MultiShotParamHistoryRepository.OpenConnectionAsync())
            {
                var all = await MultiShotParamHistoryRepository.LoadAllAsync(conn);
                foreach (var kv in all)
                {
                    _multiShotPreviousSnapshots[kv.Key] = MultiShotProfileSnapshot.FromDictionary(kv.Value);
                }
            }
            _ = Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                OnPropertyChanged(nameof(CurrentMultiShotPreviousSnapshot))));
        }

        private void ClearTriggerDiagnostics()
        {
            lock (_triggerOutputsLock)
            {
                _executedTriggerOutputsByProduct.Clear();
            }
            _lastAcceptedPhotocellUtc = DateTime.MinValue;
            _lastAcceptedPhotocellEncoderCount = long.MinValue;
        }

        private void ValidateCameraTriggerOutputMappings()
        {
            var duplicatePhysicalOutputs = InterventionPoints
                .Where(point => point.Enabled && string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase))
                .Select(point => new
                {
                    PointCode = point.PointCode,
                    Signal = ResolveOutputSignalByCode(ResolveInterventionSignalCode(new ProductInterventionState
                    {
                        ActionType = point.ActionType,
                        SignalCode = point.SignalCode
                    }))
                })
                .Where(item => item.Signal != null)
                .GroupBy(item => BuildOutputPhysicalKey(item.Signal), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();

            foreach (var duplicate in duplicatePhysicalOutputs)
            {
                var involvedPoints = string.Join(", ", duplicate.Select(item => item.PointCode));
                AddLogEntry($"Camera trigger mapping overlap detected on {duplicate.Key}: points {involvedPoints} share the same physical output. The second pulse for the same product will be suppressed.", IOLogLevel.Warning, "TRACK");
            }
        }

        private void MigrateLegacyTwoCameraRuntimeConfiguration()
        {
            NormalizeOutputSignal(
                "OUT_LIGHT_SIDE",
                "OUT_CAMERA_SIDE_TRIGGER",
                "Camera Side hardware trigger pulse",
                MachineSignalCategory.CameraTrigger,
                "DO01",
                true,
                "Real trigger output for side camera and related illuminator enable chain.");

            NormalizeOutputSignal(
                "OUT_CAMERA_TRIGGER",
                "OUT_CAMERA_TOP_TRIGGER",
                "Camera Top hardware trigger pulse",
                MachineSignalCategory.CameraTrigger,
                "DO02",
                true,
                "Real trigger output for top camera and related illuminator enable chain.");

            // Le uscite camera mancanti nascono senza canale: il canale si sceglie dall'elenco
            // nella pagina I/O. Prima venivano create con DO01/DO02/DO04/DO05 di default, che
            // su una macchina cablata diversamente pilotavano un'uscita sbagliata.
            EnsureOutputSignal("OUT_CAMERA_SIDE_TRIGGER", "Camera Side hardware trigger pulse", MachineSignalCategory.CameraTrigger, string.Empty, true, "Real trigger output for side camera and related illuminator enable chain.");
            EnsureOutputSignal("OUT_CAMERA_TOP_TRIGGER", "Camera Top hardware trigger pulse", MachineSignalCategory.CameraTrigger, string.Empty, true, "Real trigger output for top camera and related illuminator enable chain.");
            EnsureOutputSignal("OUT_CAMERA_REAR_TRIGGER", "Camera Rear/Right hardware trigger pulse", MachineSignalCategory.CameraTrigger, string.Empty, true, "Optional trigger output for rear camera used as right-side complementary view.");
            EnsureOutputSignal("OUT_CAMERA_BOTTOM_TRIGGER", "Camera Bottom hardware trigger pulse", MachineSignalCategory.CameraTrigger, string.Empty, true, "Optional trigger output for bottom camera. Can be single-shot or MultiShot according to machine setup.");
            RemoveDuplicateOutputSignals("OUT_CAMERA_SIDE_TRIGGER");
            RemoveDuplicateOutputSignals("OUT_CAMERA_TOP_TRIGGER");
            RemoveDuplicateOutputSignals("OUT_CAMERA_REAR_TRIGGER");
            RemoveDuplicateOutputSignals("OUT_CAMERA_BOTTOM_TRIGGER");

            BoundCameraTriggerSignalCode = MapLegacyCameraSignalCode(BoundCameraTriggerSignalCode, "OUT_CAMERA_TOP_TRIGGER");

            foreach (var point in InterventionPoints)
            {
                // Normalize legacy signal names; saved config remains source of truth.
                point.SignalCode = MapLegacyCameraSignalCode(point.SignalCode, point.SignalCode);
            }

            var legacyTrigger = InterventionPoints.FirstOrDefault(point =>
                string.Equals(point.PointCode, "CAMERA_TRIGGER", StringComparison.OrdinalIgnoreCase));

            if (legacyTrigger != null && !InterventionPoints.Any(point => string.Equals(point.PointCode, "CAMERA_TRIGGER_TOP", StringComparison.OrdinalIgnoreCase)))
            {
                legacyTrigger.PointCode = "CAMERA_TRIGGER_TOP";
                legacyTrigger.Description = "Trigger camera top";
                legacyTrigger.ActionType = "TriggerCamera";
                legacyTrigger.SignalCode = "OUT_CAMERA_TOP_TRIGGER";
                legacyTrigger.BaseOffsetMm = 74.0;
                legacyTrigger.TrimOffsetMm = 0.0;
                legacyTrigger.PulseMs = legacyTrigger.PulseMs > 0 ? legacyTrigger.PulseMs : 40;
                legacyTrigger.Enabled = true;
                legacyTrigger.IsStandard = true;
                legacyTrigger.Notes = AppendMigrationNote(legacyTrigger.Notes, "Migrated from legacy single camera trigger to TOP camera trigger.");
            }

            if (!InterventionPoints.Any(point => string.Equals(point.PointCode, "CAMERA_TRIGGER_TOP", StringComparison.OrdinalIgnoreCase)))
            {
                InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_TOP", 74.0, "TriggerCamera", "OUT_CAMERA_TOP_TRIGGER", 40, true));
            }

            if (!InterventionPoints.Any(point => string.Equals(point.PointCode, "CAMERA_TRIGGER_SIDE", StringComparison.OrdinalIgnoreCase)))
            {
                InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_SIDE", 76.0, "TriggerCamera", "OUT_CAMERA_SIDE_TRIGGER", 40, true));
            }

            if (!InterventionPoints.Any(point => string.Equals(point.PointCode, "CAMERA_TRIGGER_REAR", StringComparison.OrdinalIgnoreCase)) &&
                !InterventionPoints.Any(point => string.Equals(point.PointCode, "CAMERA_TRIGGER_RIGHT", StringComparison.OrdinalIgnoreCase)))
            {
                InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_REAR", 76.0, "TriggerCamera", "OUT_CAMERA_REAR_TRIGGER", 40, true));
            }

            if (!InterventionPoints.Any(point => string.Equals(point.PointCode, "CAMERA_TRIGGER_BOTTOM", StringComparison.OrdinalIgnoreCase)))
            {
                InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_BOTTOM", 76.0, "TriggerCamera", "OUT_CAMERA_BOTTOM_TRIGGER", 40, true));
            }
        }

        private void NormalizeOutputSignal(string oldSignalCode, string newSignalCode, string description, MachineSignalCategory category, string channel, bool realSignal, string notes)
        {
            // Only migrate when the signal still carries the legacy code. If it already has
            // the new code the migration was already applied and the user may have customized
            // Board/Channel - those values must not be overwritten by hardcoded defaults.
            // EnsureOutputSignal (called immediately after) handles the case where neither
            // old nor new code exists (first run or manually deleted entry).
            var signal = MachineOutputs.FirstOrDefault(item =>
                string.Equals(item.SignalCode, oldSignalCode, StringComparison.OrdinalIgnoreCase));

            if (signal == null)
            {
                return;
            }

            signal.SignalCode = newSignalCode;
            signal.Description = description;
            signal.Direction = MachineSignalDirection.Output;
            signal.Category = category;
            signal.Board = "PCIE-1756-BE";
            signal.Channel = channel;
            signal.Polarity = MachineSignalPolarity.ActiveHigh;
            signal.ReservedForRealSignal = realSignal;
            signal.Notes = notes;
        }

        private void EnsureOutputSignal(string signalCode, string description, MachineSignalCategory category, string channel, bool realSignal, string notes)
        {
            if (MachineOutputs.Any(item => string.Equals(item.SignalCode, signalCode, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = signalCode,
                Description = description,
                Direction = MachineSignalDirection.Output,
                Category = category,
                Board = "PCIE-1756-BE",
                Channel = channel,
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = realSignal,
                Notes = notes
            });
        }

        private void RemoveDuplicateOutputSignals(string signalCode)
        {
            var duplicates = MachineOutputs
                .Where(item => string.Equals(item.SignalCode, signalCode, StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .ToList();

            foreach (var duplicate in duplicates)
            {
                MachineOutputs.Remove(duplicate);
            }
        }

        private sealed class CameraIoProvisioningSpec
        {
            public string Role;
            public string DisplayName;
            public string SignalCode;
            public string PointCode;
            public string[] CompatibleSignalCodes;
        }

        // Codici canonici gia' usati dal runtime e dalla migrazione storica: la camera fisica
        // Left usa i codici SIDE, la Right i codici REAR (alias LEFT/RIGHT riconosciuti).
        private static readonly CameraIoProvisioningSpec[] CameraIoProvisioningSpecs =
        {
            new CameraIoProvisioningSpec { Role = "top", DisplayName = "Top", SignalCode = "OUT_CAMERA_TOP_TRIGGER", PointCode = "CAMERA_TRIGGER_TOP", CompatibleSignalCodes = new[] { "OUT_CAMERA_TOP_TRIGGER", "OUT_CAMERA_TRIGGER" } },
            new CameraIoProvisioningSpec { Role = "left", DisplayName = "Left", SignalCode = "OUT_CAMERA_SIDE_TRIGGER", PointCode = "CAMERA_TRIGGER_SIDE", CompatibleSignalCodes = new[] { "OUT_CAMERA_SIDE_TRIGGER", "OUT_CAMERA_LEFT_TRIGGER" } },
            new CameraIoProvisioningSpec { Role = "right", DisplayName = "Right", SignalCode = "OUT_CAMERA_RIGHT_TRIGGER", PointCode = "CAMERA_TRIGGER_RIGHT", CompatibleSignalCodes = new[] { "OUT_CAMERA_RIGHT_TRIGGER" } },
            new CameraIoProvisioningSpec { Role = "rear", DisplayName = "Rear", SignalCode = "OUT_CAMERA_REAR_TRIGGER", PointCode = "CAMERA_TRIGGER_REAR", CompatibleSignalCodes = new[] { "OUT_CAMERA_REAR_TRIGGER" } },
            new CameraIoProvisioningSpec { Role = "front", DisplayName = "Front", SignalCode = "OUT_CAMERA_FRONT_TRIGGER", PointCode = "CAMERA_TRIGGER_FRONT", CompatibleSignalCodes = new[] { "OUT_CAMERA_FRONT_TRIGGER" } },
            new CameraIoProvisioningSpec { Role = "bottom", DisplayName = "Bottom", SignalCode = "OUT_CAMERA_BOTTOM_TRIGGER", PointCode = "CAMERA_TRIGGER_BOTTOM", CompatibleSignalCodes = new[] { "OUT_CAMERA_BOTTOM_TRIGGER" } }
        };

        /// <summary>
        /// Predispone l'I/O a partire dalle camere presenti nel VPP: per ogni camera un'uscita
        /// trigger e un punto intervento, per la macchina gli allarmi bloccante/non bloccante e
        /// lo scarto a due uscite (OPEN/CLOSE). Aggiunge solo cio' che manca: i segnali nascono
        /// senza canale (da scegliere dall'elenco) e i punti nascono disabilitati, quindi nulla
        /// viene pilotato finche' l'operatore non assegna l'uscita e abilita il punto.
        /// </summary>
        public Task ProvisionCameraIoAsync(IReadOnlyCollection<string> cameraRoles, string source)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(() => ProvisionCameraIoCore(cameraRoles, source)).Task;
        }

        private void ProvisionCameraIoCore(IReadOnlyCollection<string> cameraRoles, string source)
        {
            if (_machineConfiguration == null)
            {
                AddLogEntry($"IO_PROVISIONING_SKIPPED|source={source}|reason=machine configuration not loaded", IOLogLevel.Warning, "CONFIG");
                return;
            }

            bool wasDirty = IsConfigurationDirty;
            var added = new List<string>();
            var physicalRoles = (cameraRoles ?? Array.Empty<string>())
                .Select(role => CameraConfigurationHelper.NormalizePhysicalCameraRole(role))
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // On a newly commissioned four-camera machine the historical disabled
            // Right profile still points at REAR. Convert only that untouched default;
            // an enabled or edited legacy profile keeps its electrical mapping.
            var legacyRight = _machineConfiguration.MachineMultiShotTrigger?.Right;
            if (!wasDirty && physicalRoles.Contains("right") && physicalRoles.Contains("rear") &&
                legacyRight?.Enabled == false &&
                string.Equals(legacyRight.CameraRole, "Rear", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(legacyRight.TriggerOutputName, "OUT_CAMERA_REAR_TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                var rightProfile = CloneMultiShotOptions(legacyRight);
                rightProfile.CameraRole = "Right";
                rightProfile.DisplayName = "Right";
                rightProfile.TriggerOutputName = "OUT_CAMERA_RIGHT_TRIGGER";
                _multiShotProfileEditors["Right"] = rightProfile;
                added.Add("MULTISHOT_RIGHT_PROFILE");
            }

            foreach (string role in physicalRoles)
            {
                var spec = CameraIoProvisioningSpecs.FirstOrDefault(item => string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase));
                if (spec == null)
                {
                    continue;
                }

                if (!MachineOutputs.Any(output => spec.CompatibleSignalCodes.Any(code =>
                        string.Equals(NormalizeSignalCode(output?.SignalCode), code, StringComparison.OrdinalIgnoreCase))))
                {
                    AddUnassignedOutput(spec.SignalCode, $"Camera {spec.DisplayName} hardware trigger pulse", MachineSignalCategory.CameraTrigger);
                    added.Add(spec.SignalCode);
                }

                if (!InterventionPoints.Any(point => IsCameraTriggerPointForRole(point, role)))
                {
                    InterventionPoints.Add(new MachineInterventionPoint
                    {
                        PointCode = spec.PointCode,
                        Description = $"Trigger camera {spec.DisplayName.ToLowerInvariant()}",
                        Enabled = false,
                        ReferenceCode = "PRODUCT_ZERO",
                        BaseOffsetMm = 0.0,
                        TrimOffsetMm = 0.0,
                        ActionType = "TriggerCamera",
                        SignalCode = spec.SignalCode,
                        PulseMs = 40,
                        IsStandard = true,
                        Notes = "Predisposto dal VPP: assegnare il canale di uscita, impostare la quota e abilitare."
                    });
                    added.Add(spec.PointCode);
                }
            }

            if (EnsureUnassignedOutput("OUT_BLOCKING_ALARMS", "Blocking alarm output (consecutive rejects)", MachineSignalCategory.Alarm)) added.Add("OUT_BLOCKING_ALARMS");
            if (EnsureUnassignedOutput("OUT_NON_BLOCKING_ALARMS", "Non blocking alarm output (consecutive rejects)", MachineSignalCategory.Alarm)) added.Add("OUT_NON_BLOCKING_ALARMS");
            if (EnsureUnassignedOutput(BoundRejectOpenSignalCode, "Reject actuator OPEN command", MachineSignalCategory.Reject)) added.Add(BoundRejectOpenSignalCode);
            if (EnsureUnassignedOutput(BoundRejectCloseSignalCode, "Reject actuator CLOSE command", MachineSignalCategory.Reject)) added.Add(BoundRejectCloseSignalCode);

            int normalizedChannels = NormalizeSignalChannelNames();

            if (added.Count == 0 && normalizedChannels == 0)
            {
                AddLogEntry(
                    $"IO_PROVISIONING_UP_TO_DATE|source={source}|cameras={string.Join(",", physicalRoles)}",
                    IOLogLevel.Info,
                    "CONFIG");
                return;
            }

            RefreshAvailableSignalCodes();
            RefreshMachineSignalStatuses();
            OnPropertyChanged(nameof(RejectActuatorSummary));

            string summary =
                $"source={source}|cameras={string.Join(",", physicalRoles)}|added={string.Join(",", added)}" +
                $"|normalizedChannels={normalizedChannels}";
            if (wasDirty)
            {
                // Modifiche dell'operatore non ancora salvate: non scriverle al suo posto.
                MarkConfigurationDirty();
                AddLogEntry($"IO_PROVISIONING_APPLIED_NOT_SAVED|{summary}|reason=unsaved operator edits pending", IOLogLevel.Warning, "CONFIG");
                return;
            }

            try
            {
                _machineConfiguration = BuildRuntimeConfigurationFromView();
                _machineConfigurationService.Save(_machineConfiguration);
                MarkConfigurationSaved();
                AddLogEntry($"IO_PROVISIONING_APPLIED|{summary}", IOLogLevel.Info, "CONFIG");
            }
            catch (Exception ex)
            {
                MarkConfigurationDirty();
                AddLogEntry($"IO_PROVISIONING_NOT_PERSISTED|{summary}", IOLogLevel.Error, "CONFIG", ex);
            }
        }

        private static bool IsCameraTriggerPointForRole(MachineInterventionPoint point, string physicalRole)
        {
            if (point == null || !string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string roleFromPoint = CameraConfigurationHelper.NormalizePhysicalCameraRole(point.PointCode);
            if (string.IsNullOrWhiteSpace(roleFromPoint))
            {
                roleFromPoint = CameraConfigurationHelper.NormalizePhysicalCameraRole(point.SignalCode);
            }

            return string.Equals(roleFromPoint, physicalRole, StringComparison.OrdinalIgnoreCase);
        }

        private bool EnsureUnassignedOutput(string signalCode, string description, MachineSignalCategory category)
        {
            if (string.IsNullOrWhiteSpace(signalCode) ||
                MachineOutputs.Any(output => string.Equals(NormalizeSignalCode(output?.SignalCode), NormalizeSignalCode(signalCode), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            AddUnassignedOutput(signalCode, description, category);
            return true;
        }

        private void AddUnassignedOutput(string signalCode, string description, MachineSignalCategory category)
        {
            MachineOutputs.Add(new MachineSignalDefinition
            {
                SignalCode = signalCode,
                Description = description,
                Direction = MachineSignalDirection.Output,
                Category = category,
                Board = "PCIE-1756-BE",
                Channel = string.Empty,
                Polarity = MachineSignalPolarity.ActiveHigh,
                ReservedForRealSignal = false,
                Notes = "Predisposta automaticamente: scegliere il canale dall'elenco."
            });
        }

        /// <summary>
        /// Porta i canali alla forma dell'elenco di selezione (DO6 -> DO06, di0 -> DI00), cosi' che
        /// i menu a tendina mostrino il valore esistente. Il canale fisico non cambia.
        /// </summary>
        private int NormalizeSignalChannelNames()
        {
            int changed = 0;
            foreach (var signal in MachineOutputs.Concat(MachineInputs))
            {
                string normalized = NormalizeChannelName(signal?.Channel, signal?.Direction == MachineSignalDirection.Input ? "DI" : "DO");
                if (signal != null && normalized != null && !string.Equals(normalized, signal.Channel, StringComparison.Ordinal))
                {
                    signal.Channel = normalized;
                    changed++;
                }
            }

            return changed;
        }

        private static string NormalizeChannelName(string channel, string expectedPrefix)
        {
            if (string.IsNullOrWhiteSpace(channel))
            {
                return null;
            }

            string trimmed = channel.Trim();
            if (!trimmed.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(trimmed.Substring(expectedPrefix.Length), out int number))
            {
                return null;
            }

            if (Pcie1756ChannelMap.IsPcie1884Channel(number))
            {
                return $"{expectedPrefix}{number}";
            }

            return number >= 0 && number < 100 ? $"{expectedPrefix}{number:D2}" : null;
        }

        private bool IsRejectOpenCloseConfigured(out MachineSignalDefinition openSignal, out MachineSignalDefinition closeSignal)
        {
            openSignal = ResolveOutputSignalByCode(BoundRejectOpenSignalCode);
            closeSignal = ResolveOutputSignalByCode(BoundRejectCloseSignalCode);
            int openChannel = ParseChannelNumber(openSignal?.Channel);
            int closeChannel = ParseChannelNumber(closeSignal?.Channel);
            return openSignal != null && closeSignal != null &&
                   Pcie1756ChannelMap.IsValidOutputChannel(openChannel) &&
                   Pcie1756ChannelMap.IsValidOutputChannel(closeChannel) &&
                   openChannel != closeChannel;
        }

        private bool HasRejectOpenCloseChannelConflict()
        {
            var openSignal = ResolveOutputSignalByCode(BoundRejectOpenSignalCode);
            var closeSignal = ResolveOutputSignalByCode(BoundRejectCloseSignalCode);
            int openChannel = ParseChannelNumber(openSignal?.Channel);
            int closeChannel = ParseChannelNumber(closeSignal?.Channel);
            return Pcie1756ChannelMap.IsValidOutputChannel(openChannel) &&
                   Pcie1756ChannelMap.IsValidOutputChannel(closeChannel) &&
                   openChannel == closeChannel;
        }

        private static string MapLegacyCameraSignalCode(string signalCode, string fallback)
        {
            if (string.IsNullOrWhiteSpace(signalCode))
            {
                return fallback;
            }

            if (string.Equals(signalCode, "OUT_LIGHT_TOP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(signalCode, "OUT_CAMERA_TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                return "OUT_CAMERA_TOP_TRIGGER";
            }

            if (string.Equals(signalCode, "OUT_LIGHT_SIDE", StringComparison.OrdinalIgnoreCase))
            {
                return "OUT_CAMERA_SIDE_TRIGGER";
            }

            return signalCode;
        }

        private static string AppendMigrationNote(string existingNotes, string migrationNote)
        {
            if (string.IsNullOrWhiteSpace(existingNotes))
            {
                return migrationNote;
            }

            if (existingNotes.IndexOf(migrationNote, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return existingNotes;
            }

            return existingNotes.TrimEnd() + " " + migrationNote;
        }

        private static void ReplaceCollection<T>(
            ObservableCollection<T> target,
            System.Collections.Generic.IEnumerable<T> source)
        {
            target.Clear();
            foreach (var item in source ?? Enumerable.Empty<T>())
            {
                target.Add(item);
            }
        }


        private void EnsureDefaultInterventionPoints()
        {
            if (InterventionPoints.Any())
            {
                return;
            }

            InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_TOP", 74.0, "TriggerCamera", "OUT_CAMERA_TOP_TRIGGER", 40, true));
            InterventionPoints.Add(CreateStandardInterventionPoint("CAMERA_TRIGGER_SIDE", 76.0, "TriggerCamera", "OUT_CAMERA_SIDE_TRIGGER", 40, true));
            InterventionPoints.Add(CreateStandardInterventionPoint("REJECT", 350.0, "Reject", "OUT_REJECT_SOLENOID", 80, true));
        }
        private static int ParseChannelNumber(string channel)
        {
            return MachineRuntimeIo.ParseChannelNumber(channel);
        }

        private void OnTrackedProductDetected(object sender, TrackedProductEventArgs e)
        {
            if (e?.Product == null)
            {
                return;
            }

            PostUiUpdate(() =>
            {
                TrackedProducts.Insert(0, e.Product);
                if (TrackedProducts.Count > 200)
                {
                    TrackedProducts.RemoveAt(TrackedProducts.Count - 1);
                }

                LastTrackedProductStatus = string.Format(
                    T("Tool_VM_ProductStatusTrackingStarted", "Product {0}: photocell at {1:0.0} mm machine, tracking started"),
                    e.Product.ProductId,
                    e.Product.DetectionMachinePositionMm);
            });

            AddLogEntry(
                $"Product {e.Product.ProductId} detected at encoder position {e.Product.DetectionEncoderCount} | machine position {e.Product.DetectionMachinePositionMm:0.0} mm",
                IOLogLevel.Success, "TRACK");
            AddMachineOperationalEvent(
                "Product",
                "PRODUCT_DETECTED",
                "Product detected",
                "Photocell active and product tracking started.",
                T("Tool_Event_StatusTrackingActive", "Tracking active"),
                e.Product.ProductId,
                BoundProductPhotocellSignalCode,
                e.Product.DetectionMachinePositionMm);
        }

        private void OnTrackedProductUpdated(object sender, TrackedProductEventArgs e)
        {
            AddLogEntry(
                $"Product {e.Product.ProductId} updated: state {e.Product.State}",
                IOLogLevel.Info, "TRACK");

            if (e.Product.State == TrackedProductState.Completed ||
                e.Product.State == TrackedProductState.RejectedExecuted)
            {
                ClearTriggerOutputRegistrationsForProduct(e.Product.ProductId);
            }
        }


        private void OnInterventionPointReached(object sender, ProductInterventionEventArgs e)
        {
            if (e?.Product == null || e.InterventionState == null)
            {
                return;
            }

            // The Advantech polling thread must only detect quotas. Hardware writes,
            // operational logging and UI publication run on the dedicated intervention
            // worker so a 20-150 ms board call cannot delay the next encoder sample.
            var pending = new PendingInterventionExecution
            {
                EventArgs = e,
                EnqueuedAtUtc = DateTime.UtcNow
            };
            if (!_interventionExecutionQueue.IsAddingCompleted && _interventionExecutionQueue.TryAdd(pending))
            {
                return;
            }

            AddLogEntry(
                $"INTERVENTION_QUEUE_FULL|product={e.Product.ProductId}|point={e.InterventionState.PointCode}|Trigger not executed.",
                IOLogLevel.Error,
                "TRACK");
        }

        private void ProcessInterventionExecutionQueue()
        {
            try
            {
                Thread.CurrentThread.Name = "Qtis.InterventionOutput";
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            }
            catch
            {
            }

            try
            {
                foreach (PendingInterventionExecution pending in _interventionExecutionQueue.GetConsumingEnumerable())
                {
                    ProductInterventionEventArgs e = pending?.EventArgs;
                    if (e?.Product == null || e.InterventionState == null)
                    {
                        continue;
                    }

                    double dispatchDelayMs = (DateTime.UtcNow - pending.EnqueuedAtUtc).TotalMilliseconds;
                    if (dispatchDelayMs > 5.0)
                    {
                        AddLogEntry(
                            $"INTERVENTION_DISPATCH_DELAY|product={e.Product.ProductId}|point={e.InterventionState.PointCode}" +
                            $"|delay={dispatchDelayMs:F1}ms|queue={_interventionExecutionQueue.Count}",
                            dispatchDelayMs > 20.0 ? IOLogLevel.Warning : IOLogLevel.Info,
                            "TRACK");
                    }

                    ExecuteInterventionPointAsync(
                            e.Product,
                            e.InterventionState,
                            $"punto intervento {e.InterventionState.PointCode} per prodotto {e.Product.ProductId}",
                            pending.EnqueuedAtUtc)
                        .SafeFireAndForget(ex => AddLogEntry(
                            $"Intervention point execution error {e.InterventionState.PointCode}: {ex.Message}",
                            IOLogLevel.Error,
                            "TRACK",
                            ex));

                    PublishInterventionReached(e);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "INTERVENTION_EXECUTION_WORKER_FAILED");
            }
        }

        private void PublishInterventionReached(ProductInterventionEventArgs e)
        {

            PostUiUpdate(() =>
            {
                InterventionRuntimeEvents.Insert(0, new InterventionRuntimeEvent
                {
                    Timestamp = DateTime.Now,
                    ProductId = e.Product.ProductId,
                    PointCode = e.InterventionState.PointCode,
                    Description = e.InterventionState.Description,
                    ActionType = e.InterventionState.ActionType,
                    SignalCode = ResolveInterventionSignalCode(e.InterventionState),
                    OffsetMm = e.InterventionState.TargetOffsetMm,
                    Status = T("Tool_Event_StatusReached", "Reached"),
                    Notes = e.Message
                });

                if (InterventionRuntimeEvents.Count > 300)
                {
                    InterventionRuntimeEvents.RemoveAt(InterventionRuntimeEvents.Count - 1);
                }
            });

            AddMachineOperationalEvent(
                T("Tool_Event_CategoryPosition", "Position"),
                e.InterventionState.PointCode,
                string.Format(T("Tool_Event_PositionReached_Title", "Point {0} reached"), e.InterventionState.PointCode),
                e.Message,
                T("Tool_Event_StatusReached", "Reached"),
                e.Product.ProductId,
                ResolveInterventionSignalCode(e.InterventionState),
                e.InterventionState.AbsoluteMachineQuotaMm);
        }

        private static string ResolveCameraRoleForTrigger(ProductInterventionState state, string signalCode)
        {
            string source = $"{state?.PointCode} {signalCode}";
            if (source.IndexOf("BOTTOM", StringComparison.OrdinalIgnoreCase) >= 0) return "bottom";
            if (source.IndexOf("FRONT", StringComparison.OrdinalIgnoreCase) >= 0) return "front";
            if (source.IndexOf("RIGHT", StringComparison.OrdinalIgnoreCase) >= 0) return "right";
            if (source.IndexOf("REAR", StringComparison.OrdinalIgnoreCase) >= 0) return "rear";
            if (source.IndexOf("SIDE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("LEFT", StringComparison.OrdinalIgnoreCase) >= 0) return "side";
            if (source.IndexOf("TOP", StringComparison.OrdinalIgnoreCase) >= 0) return "top";
            return null;
        }

        private void OnTrackedProductReadyForReject(object sender, TrackedProductEventArgs e)
        {
            var rejectPoint = e.Product.InterventionStates.FirstOrDefault(p =>
                string.Equals(p.ActionType, "Reject", StringComparison.OrdinalIgnoreCase));

            AddLogEntry(
                $"Product {e.Product.ProductId} ready for reject at encoder position {e.Product.RejectEncoderTarget} | machine position {rejectPoint?.AbsoluteMachineQuotaMm:0.0} mm",
                IOLogLevel.Warning, "TRACK");
            AddMachineOperationalEvent(
                T("Tool_Event_CategoryReject", "Reject"),
                "REJECT_READY",
                T("Tool_Event_RejectReady_Title", "Product ready for reject"),
                T("Tool_Event_RejectReady_Message", "The NOK product has reached the reject position."),
                T("Tool_Event_StatusPending", "Pending"),
                e.Product.ProductId,
                BoundRejectSignalCode,
                rejectPoint?.AbsoluteMachineQuotaMm);

            if (string.Equals(MachineRejectMode, "EncoderTracked", StringComparison.OrdinalIgnoreCase) && !HasEnabledRejectInterventionPoint)
            {
                ExecuteRejectForProductAsync(e.Product, "encoder position reached").SafeFireAndForget(ex => AddLogEntry($"Reject activation error: {ex.Message}", IOLogLevel.Error, "TRACK", ex));
            }
        }

        private void OnMachineControllerLogMessage(object sender, string message)
        {
            AddLogEntry(message, IOLogLevel.Info, "CTRL");
        }

        private void AddMachineOperationalEvent(
            string category,
            string eventCode,
            string title,
            string detail,
            string status,
            long? productId = null,
            string signalCode = null,
            double? machineQuotaMm = null)
        {
            var fusionCategory = ResolveFusionCategory(eventCode, category);
            var sourceArea = ResolveEventSourceArea(eventCode, category);
            var severity = ResolveEventSeverity(eventCode, status);
            var mainProjectCompatibility = ToolMainProjectEventMapper.Map(category, eventCode, status, fusionCategory, sourceArea, severity);

            PostUiUpdate(() =>
            {
                MachineOperationalEvents.Insert(0, new MachineOperationalEvent
                {
                    Timestamp = DateTime.Now,
                    Category = category,
                    FusionCategory = fusionCategory,
                    SourceArea = sourceArea,
                    EventCode = eventCode,
                    Title = title,
                    Detail = detail,
                    Status = status,
                    Severity = severity,
                    MainProjectCategory = mainProjectCompatibility.Category,
                    MainProjectMachineStatus = mainProjectCompatibility.MachineStatus,
                    MainProjectVisionStatus = mainProjectCompatibility.VisionStatus,
                    MainProjectLogId = mainProjectCompatibility.LogId,
                    ProductId = productId,
                    SignalCode = signalCode,
                    MachineQuotaMm = machineQuotaMm
                });

                if (MachineOperationalEvents.Count > 300)
                {
                    MachineOperationalEvents.RemoveAt(MachineOperationalEvents.Count - 1);
                }

                OnPropertyChanged(nameof(MachineEventsSummary));
                OnPropertyChanged(nameof(LastMachineEventSummary));
                OnPropertyChanged(nameof(MachineEventsFusionSummary));
                OnPropertyChanged(nameof(LastMachineEventFusionSummary));
            });
        }

        private static string ResolveFusionCategory(string eventCode, string category)
        {
            if (string.IsNullOrWhiteSpace(eventCode) && string.IsNullOrWhiteSpace(category))
            {
                return "Machine";
            }

            var code = (eventCode ?? string.Empty).ToUpperInvariant();
            var localCategory = category ?? string.Empty;

            if (code.Contains("FAILED") || code.Contains("ALARM") || string.Equals(localCategory, "Alarm", System.StringComparison.OrdinalIgnoreCase))
            {
                return "Alarm";
            }

            if (code.Contains("RESULT") || code.Contains("TRIGGER"))
            {
                return "Command";
            }

            if (code.Contains("MODE_") || code.Contains("INIT_") || string.Equals(localCategory, "Connection", System.StringComparison.OrdinalIgnoreCase))
            {
                return "Machine";
            }

            return "Runtime";
        }

        private static string ResolveEventSourceArea(string eventCode, string category)
        {
            var code = (eventCode ?? string.Empty).ToUpperInvariant();

            if (code.Contains("TRIGGER"))
            {
                return "Trigger";
            }

            if (code.Contains("REJECT") || string.Equals(category, "Reject", System.StringComparison.OrdinalIgnoreCase))
            {
                return "Reject";
            }

            if (code.Contains("MODE_") || code.Contains("INIT_") || string.Equals(category, "Connection", System.StringComparison.OrdinalIgnoreCase))
            {
                return "Connection";
            }

            if (string.Equals(category, "Position", System.StringComparison.OrdinalIgnoreCase))
            {
                return "Tracking";
            }

            if (string.Equals(category, "Product", System.StringComparison.OrdinalIgnoreCase) || string.Equals(category, "Result", System.StringComparison.OrdinalIgnoreCase))
            {
                return "ProductTracking";
            }

            return "MachineRuntime";
        }

        private static string ResolveEventSeverity(string eventCode, string status)
        {
            var code = (eventCode ?? string.Empty).ToUpperInvariant();
            var normalizedStatus = (status ?? string.Empty).ToUpperInvariant();

            if (code.Contains("FAILED") || normalizedStatus.Contains("ERROR"))
            {
                return "Error";
            }

            if (normalizedStatus.Contains("PENDING") || normalizedStatus.Contains("FALLBACK") || normalizedStatus.Contains("NOK"))
            {
                return "Warning";
            }

            return "Info";
        }

        private void AddLogEntry(string message, IOLogLevel level, string category = "APP", Exception exception = null)
        {
            var formattedMessage = $"[{category}] {message}";

            switch (level)
            {
                case IOLogLevel.Warning:
                    Logger.Warn(exception, formattedMessage);
                    break;
                case IOLogLevel.Error:
                    Logger.Error(exception, formattedMessage);
                    break;
                default:
                    Logger.Info(exception, formattedMessage);
                    break;
            }

            PostUiUpdate(() =>
            {
                LogEntries.Insert(0, new IOLogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = formattedMessage,
                    Level = level
                });

                // Mantieni massimo 1000 voci nel log
                if (LogEntries.Count > 1000)
                {
                    LogEntries.RemoveAt(LogEntries.Count - 1);
                }
            });
        }

        private void PostUiUpdate(System.Action action)
        {
            if (action == null)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action);
        }

        private void CleanOldLogEntries()
        {
            var cutoff = DateTime.Now.AddHours(-1); // Mantieni solo ultima ora
            var oldEntries = LogEntries.Where(e => e.Timestamp < cutoff).ToList();

            foreach (var entry in oldEntries)
            {
                LogEntries.Remove(entry);
            }
        }

        public EjectionAlarmConfig SelectedEjectionAlarm
        {
            get => _selectedEjectionAlarm;
            set { _selectedEjectionAlarm = value; OnPropertyChanged(); }
        }

        public string EjectionAlarmsSummary
        {
            get => _ejectionAlarmsSummary;
            private set { _ejectionAlarmsSummary = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Ejection Alarms

        private async Task LoadEjectionAlarmsAsync()
        {
            try
            {
                if (_alarmService == null) return;
                await _alarmService.ReloadAlarmsAsync();
                var alarms = _alarmService.GetAllAlarms();
                _ = Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    EjectionAlarms.Clear();
                    foreach (var a in alarms)
                        EjectionAlarms.Add(a);
                    RefreshEjectionAlarmsSummary();
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "EJECTION_ALARMS_LOAD|phase=load");
            }
        }

        private async Task SaveEjectionAlarmsAsync()
        {
            try
            {
                if (_alarmService == null) return;
                var alarms = EjectionAlarms.ToList();
                foreach (var alarm in alarms)
                    await _alarmService.UpdateAlarmAsync(alarm.Name, alarm);
                RefreshEjectionAlarmsSummary();
                AddLogEntry($"Ejection alarm IO config saved ({alarms.Count} alarms)", IOLogLevel.Success, "ALARMS");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "EJECTION_ALARMS_SAVE|phase=save");
            }
        }

        private async Task ResetSelectedEjectionAlarmAsync()
        {
            if (SelectedEjectionAlarm == null) return;
            _alarmService?.AcknowledgeAlarm(SelectedEjectionAlarm.Name, UserSession.CurrentUser ?? "unknown");
            SelectedEjectionAlarm.IsTriggered = false;
            SelectedEjectionAlarm.TriggerCount = 0;
            RefreshEjectionAlarmsSummary();
            // Turn off the physical output that was latched by this alarm
            var channelId = SelectedEjectionAlarm.OutputChannelId;
            if (!string.IsNullOrEmpty(channelId) &&
                channelId.StartsWith("DO", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(channelId.Substring(2), out int ch) &&
                Pcie1756ChannelMap.IsValidDoChannel(ch))
            {
                try
                {
                    await _ioManager.WriteOutputAsync(ch, false);
                    AddLogEntry($"Output {channelId} disattivato (reset allarme {SelectedEjectionAlarm.Name})", IOLogLevel.Info, "ALARMS");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"ALARM_RESET_OUTPUT|alarm={SelectedEjectionAlarm.Name}|channel={channelId}");
                }
            }
        }

        private void OnEjectionAlarmTriggered(object sender, AlarmTriggeredEventArgs e)
        {
            if (e?.Alarm == null) return;
            Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                // UI-only: update the DataGrid mirror in the Allarmi Scarto tab.
                // Physical IO write is handled by MainWindow.OnAlarmTriggered (always active).
                var match = EjectionAlarms.FirstOrDefault(a => a.Name == e.Alarm.Name);
                if (match != null)
                {
                    match.IsTriggered = true;
                    match.TriggerCount++;
                    if (!string.IsNullOrEmpty(e.Alarm.OutputChannelId))
                        match.OutputChannelId = e.Alarm.OutputChannelId;
                    match.PulseDurationMs = e.Alarm.PulseDurationMs;
                    RefreshEjectionAlarmsSummary();
                    AddLogEntry($"Ejection alarm triggered: {match.Name} (count={match.TriggerCount})", IOLogLevel.Warning, "ALARMS");
                }
            }));
        }

        private void OnEjectionAlarmAcknowledged(object sender, AlarmAcknowledgedEventArgs e)
        {
            if (e?.Alarm == null) return;
            Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                var match = EjectionAlarms.FirstOrDefault(a => a.Name == e.Alarm.Name);
                if (match != null)
                {
                    match.IsTriggered = false;
                    match.TriggerCount = e.Alarm.TriggerCount;
                    match.OutputChannelId = e.Alarm.OutputChannelId;
                    match.PulseDurationMs = e.Alarm.PulseDurationMs;
                    RefreshEjectionAlarmsSummary();
                    AddLogEntry($"Ejection alarm acknowledged: {match.Name}", IOLogLevel.Info, "ALARMS");
                }
            }));
        }

        private void RefreshEjectionAlarmsSummary()
        {
            int enabled = EjectionAlarms.Count(a => a.Enabled);
            int triggered = EjectionAlarms.Count(a => a.IsTriggered);
            EjectionAlarmsSummary = triggered > 0
                ? $"{enabled}/{EjectionAlarms.Count} enabled - {triggered} TRIGGERED"
                : $"{enabled}/{EjectionAlarms.Count} enabled";
        }

        // ---------------------------------------------------------------------------

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _refreshTimer.Stop();
            _logTimer.Stop();
            _heartbeatTimer.Stop();
            _virtualConveyorTimer.Stop();
            _runtimePreviewApplyTimer.Stop();
            try
            {
                _interventionExecutionQueue.CompleteAdding();
                _interventionExecutionTask.Wait(1000);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "INTERVENTION_EXECUTION_WORKER_SHUTDOWN");
            }
            CancelPendingTimedTriggerBatches("viewmodel dispose");
            CancelMultiShotSession("viewmodel dispose");
            _timedTriggerService.EventRaised -= OnTimedTriggerServiceEventRaised;
            _timedTriggerService.Dispose();
            ServiceLocator.MachineRuntimeService.ContinuousRunStateChanged -= OnContinuousRunStateChanged;
            DisposeMultiShotController();
            UnsubscribeIoManagerEvents();
            UnsubscribeMachineControllerEvents();
            if (!string.IsNullOrWhiteSpace(BoundHeartbeatOutputSignalCode))
            {
                // Un errore di scrittura non deve impedire il rilascio dei controlli DAQ qui sotto.
                try
                {
                    SetMappedOutputAsync(BoundHeartbeatOutputSignalCode, false, "chiusura applicazione").GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"HEARTBEAT_SHUTDOWN_RESET_FAILED|signal={BoundHeartbeatOutputSignalCode}");
                }
            }
            if (_alarmService != null)
            {
                _alarmService.AlarmTriggered -= OnEjectionAlarmTriggered;
                _alarmService.AlarmAcknowledged -= OnEjectionAlarmAcknowledged;
            }
            (_ioManager as IDisposable)?.Dispose();
            _interventionExecutionQueue.Dispose();
        }
    }

    public class EncoderPresetData
    {
        public int Channel { get; set; }
        public long Preset { get; set; }
    }
}
