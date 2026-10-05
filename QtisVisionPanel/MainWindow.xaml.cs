
using Cognex.VisionPro;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using Mysqlx;
using Newtonsoft.Json;
using NLog;
using QtisVisionPanel.Cls_Config;
using QtisVisionPanel.Cls_Vpro;
using QtisVisionPanel.Database;
using QtisVisionPanel.Database.ProductionRecord;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.MultiShotTrigger;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Services.MultiShotTrigger;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Views;
using QtisVisionPanel.Views.UserControls.DisplayRecord;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using System.Windows.Data;
using System.Windows.Documents;

using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Serialization;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;



namespace QtisVisionPanel
{
    /// <summary>
    /// Main industrial HMI shell.
    ///
    /// This class is the runtime orchestrator of the application and owns the
    /// machine-side integration points that are too stateful to live entirely
    /// inside view models:
    /// - VisionPro startup and job orchestration
    /// - watchdog / auto-recovery
    /// - inspection-result processing and image save flow
    /// - recipe runtime reload
    /// - coordinated shutdown
    ///
    /// View models drive the UI, but MainWindow remains the operational hub for
    /// hardware/runtime behavior.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Configuration variables
        public static string language;
        public static AsyncConfigManagerXml configManager = new AsyncConfigManagerXml(@"C:\\QtisVision\\cfg\\Config.xml");
        public static AsyncRecipeParam ConfigRecipeParam;
        public static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public static bool IsShuttingDown = false;
        public static bool ApplyCameraTriggerDelayToHardwareAtStartup =>
            configManager?.Config?.Configuration?.ApplyCameraTriggerDelayToHardwareAtStartup ?? false;
        public static MainWindow MainView;
        public static string recipePath;
        public static ProduzioneRecord _produzioneRecord = new ProduzioneRecord();
        public static InspectionProcessor _inspectionProcessor;
      
        // Threading and Timing
        private readonly System.Timers.Timer _statusTimer = new System.Timers.Timer();
        private readonly System.Timers.Timer _visionHealthTimer = new System.Timers.Timer(1500);
        private readonly SemaphoreSlim _visionRecoverySemaphore = new SemaphoreSlim(1, 1);
        private int _visionRecoveryAttemptCount = 0;
        // True after the "manual intervention required" popup has fired once.
        // Suppresses further popup dialogs; resets when VisionPro recovers or starts manually.
        private volatile bool _visionManualInterventionRequired = false;
        private readonly object _visionHealthLock = new object();
        private static readonly TimeSpan VisionResultTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan VisionMultiShotResultTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan VisionSyntheticResultTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan VisionProcessingStallGrace = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan VisionRecoveryCooldown = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan VisionStartupGracePeriod = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan VisionStateTransitionCooldown = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan GigEStartupDiscoveryTimeout = TimeSpan.FromSeconds(20);
        private const int GigEStartupDiscoveryPollMs = 300;
        private const int GigEStartupStableSamples = 3;
        private const int GigEStartupVppReloadMaxAttempts = 3;
        private const int GigEStartupManagerReleaseDelayMs = 750;
        private const int GigEStartupBindingSettleMs = 1000;
        private DateTime _lastVisionResultAtUtc = DateTime.MinValue;
        private DateTime _lastTopResultAtUtc = DateTime.MinValue;
        private DateTime _lastSideResultAtUtc = DateTime.MinValue;
        private DateTime _lastFrontResultAtUtc = DateTime.MinValue;
        private readonly Dictionary<string, DateTime> _lastVisionResultByRole =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastProcessedPairAtUtc = DateTime.MinValue;

        // Istante in cui il primo risultato non ancora appaiato ha iniziato ad attendere.
        // DateTime.MinValue significa "nessun risultato in attesa": il watchdog deve
        // misurare da quanto un risultato aspetta di diventare una coppia elaborata, NON
        // l'eta' assoluta dell'ultima coppia. Con l'eta' assoluta, qualsiasi pausa di
        // produzione piu' lunga del timeout faceva scattare un finto "pipeline stalled"
        // al primo prodotto successivo, e il recovery scartava i risultati in volo.
        private DateTime _pendingPairSinceUtc = DateTime.MinValue;
        private DateTime _lastVisionRecoveryAttemptAtUtc = DateTime.MinValue;
        // Cached once at machine-start to avoid disk I/O in every 20ms polling cycle.
        private volatile bool _isMultiShotConfigured;
        // Synthetic QuickBuild jobs run independently and can have very different cycle times.
        // Cached at continuous-run start so production watchdog checks do not inspect Cognex
        // acquisition objects on every timer tick.
        private volatile bool _isSyntheticVisionRuntime;
        // Lag "stesso-prodotto" (ms) distinto dal timeout di attesa. Single-shot: costante 5s.
        // MultiShot: valore fisico da config (MultiShotCompanionMaxLagMs), cachato all'avvio.
        private const double SingleShotCompanionMaxLagMs = 5000.0;
        private volatile int _multiShotCompanionMaxLagMs = 15000;
        private DateTime _lastContinuousRunStartedAtUtc = DateTime.MinValue;
        private DateTime _suppressVisionAutoRecoveryUntilUtc = DateTime.MinValue;
        private DateTime _lastDiscardedVisionResultLogAtUtc = DateTime.MinValue;
        private int _consecutiveMissingUserResults;
        private int _consecutiveRunStatusErrors;
        private int _visionHealthMonitorRunning;
        private int _visionRecoveryQueued;
        private string _lastVisionIssueDetails;
        private bool _hasReceivedVisionHeartbeatSinceRunStart;
        private bool _isVisionStateChangeInProgress;


        public static CancellationTokenSource _cts = new CancellationTokenSource();

        // Delegates to MachineRuntimeService (thread-safe, lock-protected).
        // ICognexJobManager and other callsites write here; all reads go through the service lock.
        public static bool _isContinuousRunActive
        {
            get => ServiceLocator.MachineRuntimeService?.IsContinuousRunActive ?? false;
            set => ServiceLocator.MachineRuntimeService?.UpdateContinuousRunState(value);
        }

        public static CancellationTokenSource _continuousRunCts;
        public static readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _disposed = false;

        #region Synchronization Variables - MODIFICATO
        // Vision Components
        public static ICognexJobManager _cognexManager;
        // Dictionary per mappare Job ID -> Job Name
        public static Dictionary<int, string> JobMapping { get; private set; } = new Dictionary<int, string>();
        public static Dictionary<int, string> JobRoleMapping { get; set; } = new Dictionary<int, string>();
        public static volatile bool IsJobEditorLivePreviewActive;
        // blocchi usati in vision pro
        public static CogToolBlock _topToolBlockReults, _sideToolBlockResults,_frontTBResults,_RightTBResults,_RearTBResults,_bottomTBResults, _topToolBlockMmpx, _sideToolBlockMmpx,_FrontTBMmpx,_RearTBMmpx,_BottomTBMmpx;
        public static CogToolGroup _topToolGroup, _sideToolGroup,_BottomToolGroup,_RearToolGroup,_FrontToolGroup;
        public static CogRecordDisplay _topDisplay, _sideDisplay,_BottomDisplay,_RearDisplay,_FrontDisplay;
        public static ICogRecord _topSaveRecord, _sideSaveRecord, _frontSaveRecord, _rightSaveRecord, _rearSaveRecord, _bottomSaveRecord;
        public static ICogRecord _topDisplaySaveRecord, _sideDisplaySaveRecord, _frontDisplaySaveRecord, _rightDisplaySaveRecord, _rearDisplaySaveRecord, _bottomDisplaySaveRecord;
        private CognexEventManager _eventManager;
        // variabili di sincronizzazione eventi di risultati di ogni telecamera
        public static ICogRecord _topRecord = null, _frontRecord = null, _bottomRecord = null, _rightRecord = null, _rearRecord = null, _sideRecord = null;

        /// <summary>
        /// Sostituisce un record Cognex dismettendo il precedente per prevenire memory leak.
        /// CogRecord/CogImage non implementano IDisposable esplicitamente ma accumulano
        /// memoria nativa se non rilasciati tramite GC. Il set esplicito a null libera
        /// il riferimento per la raccolta del GC.
        /// </summary>
        private static void SetRecord(ref ICogRecord field, ICogRecord newRecord)
        {
            // Dispose the previous record before replacing: CogRecord holds unmanaged
            // Cognex memory that the GC alone cannot reclaim in time at 180 ppm.
            try { (field as IDisposable)?.Dispose(); } catch { }
            field = newRecord;
        }
        // Camera result queues are drained whenever continuous mode stops so stale
        // frames cannot be paired with the next product.
        private readonly ConcurrentQueue<CameraResult> _topQueue = new ConcurrentQueue<CameraResult>();
        private readonly ConcurrentQueue<CameraResult> _leftQueue = new ConcurrentQueue<CameraResult>();
        private readonly ConcurrentQueue<CameraResult> _frontQueue = new ConcurrentQueue<CameraResult>();
        private readonly ConcurrentQueue<CameraResult> _rightQueue = new ConcurrentQueue<CameraResult>();
        private readonly ConcurrentQueue<CameraResult> _rearQueue = new ConcurrentQueue<CameraResult>();
        private readonly ConcurrentQueue<CameraResult> _bottomQueue = new ConcurrentQueue<CameraResult>();
        private readonly SemaphoreSlim _processSemaphore = new SemaphoreSlim(1, 1);
        private long _visionResultSequence;
        private readonly ConcurrentDictionary<string, DateTime> _toolBlockResolutionWarningTimes =
            new ConcurrentDictionary<string, DateTime>();

        public static Dictionary<string, CogRecordDisplay> _recordDisplays;

        public static bool isInit = false;
        public static CogJob _topJob, _sideJob,_bottomJob,_frontJob,_rightJob,_rearJob;
        private static readonly Lazy<DualIlluminationPhaseCoordinator> _dualIlluminationPhaseCoordinator =
            new Lazy<DualIlluminationPhaseCoordinator>(() =>
                new DualIlluminationPhaseCoordinator(ServiceLocator.ApplicationEventLogger));

        private readonly object _syncLock = new object();

        // Fase 3 Step 1: display logic extracted to CameraDisplayManager.
        // Keep bootstrap lazy because runtime recipe/vision refresh can happen
        // before Window_Loaded completes and before UI helpers were previously created.
        private CameraDisplayManager _cameraDisplayManager;
        // Lazy — same reason as CameraDisplayManager: initialized once after window load.
        private InspectionOrchestrator _inspectionOrchestrator;
        // Rate-limiter: at most one COMPANION_TIMEOUT popup per 30 seconds.
        private DateTime _lastCompanionTimeoutNotificationAt = DateTime.MinValue;

        // Add these class-level variables
        private readonly TimeSpan _minProcessingInterval = TimeSpan.FromMilliseconds(100); // 2 FPS max

        private DateTime _lastProcessingTime = DateTime.MinValue;


        public static DateTime _lastTopAcqTime = DateTime.MinValue;
        public static DateTime _lastSideAcqTime = DateTime.MinValue;
        public static DateTime _lastFrontAcqTime = DateTime.MinValue;
        #endregion
        // db user manage
        public static Cls_CheckUser _userManager;
        public static InserdataInDb _insertDataInDb;
        public static Cls_InitializzeDb _initializzeDb;
        public  static Database.AlarmCardRepository _alarmCardRepository;


        // public static SaveImage.SaveImage _saveImage;
        public static SaveImage.SaveImage _saveImage;
        public static int[] when_save_FAIL, when_save_OK, when_save_nct;
        public static int CountImage_FAIL = 0, CountImage_OK = 0, CountImage_nct = 0;
        public static CogJobResultHistoryCollection mHistoryCollection = new CogJobResultHistoryCollection();
        public static ICounterUpdater counterUpdater;
        // SOSTITUITO: ora usiamo CounterManager invece dei vecchi contatori
        public static CounterManager CounterManager => ServiceLocator.CounterManager;

        public static Dictionary<string, bool> EnabledInspectionFeatures { get; private set; }
        delegate void myJobManagerDelegate(Object Sender,
       CogJobManagerActionEventArgs e);
       
        //update ui
        private readonly ApplicationEventLogger _applicationEventLogger;
        private Window _opcUaRecipeNotificationWindow;


        #endregion

        private static void ConfigureRuntimeScheduling()
        {
            int logicalProcessors = Math.Max(1, Environment.ProcessorCount);
            ThreadPool.GetMinThreads(out int currentWorkerThreads, out int currentCompletionPortThreads);

            int targetWorkerThreads = Math.Max(currentWorkerThreads, logicalProcessors);
            int targetCompletionPortThreads = Math.Max(currentCompletionPortThreads, logicalProcessors);
            bool threadPoolUpdated = ThreadPool.SetMinThreads(
                targetWorkerThreads,
                targetCompletionPortThreads);

            string affinity;
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    affinity = $"0x{process.ProcessorAffinity.ToInt64():X}";
                }
            }
            catch (Exception ex)
            {
                affinity = "unavailable:" + ex.Message;
            }

            logger.Info(
                "VISION_RUNTIME_SCHEDULER|logicalProcessors={0}|affinity={1}|" +
                "threadPoolMinWorker={2}|threadPoolMinIo={3}|updated={4}",
                logicalProcessors,
                affinity,
                targetWorkerThreads,
                targetCompletionPortThreads,
                threadPoolUpdated);
        }

        public MainWindow()
        {
            ConfigureRuntimeScheduling();

            InitializeComponent();
            ApplyResponsiveLayoutResources(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

            MainView = this;
            _applicationEventLogger = ServiceLocator.ApplicationEventLogger;
            ServiceLocator.MachineRuntimeService.AttachMainWindow(this);
            ServiceLocator.OperatorInactivityService.AttachMainWindow(this);
            ServiceLocator.AutoRecipeSwitchService.AttachMainWindow(this);
            ServiceLocator.OpcUaClientService.OpcStartRequested += OnOpcUaStartRequested;
            ServiceLocator.OpcUaClientService.OpcStopRequested += OnOpcUaStopRequested;
            ServiceLocator.OpcUaClientService.OpcRecipeChangeRequested += OnOpcUaRecipeChangeRequested;

            PreviewMouseMove += OnPanelUserInteraction;
            PreviewMouseDown += OnPanelUserInteraction;
            PreviewTouchDown += OnPanelUserInteraction;
            PreviewTouchMove += OnPanelUserInteraction;
            PreviewStylusDown += OnPanelUserInteraction;
            PreviewStylusMove += OnPanelUserInteraction;
            PreviewKeyDown += OnPanelUserInteraction;

        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyResponsiveLayoutResources(ActualWidth, ActualHeight);
            
            // A4: unica istanza EjectionAlarmManager Ã¨ quella di IntegratedAlarmCardService.
            ServiceLocator.AlarmService.AlarmTriggered += OnAlarmTriggered;
            // Resetta gli indicatori di stato
            if (TopCameraView.Instance != null)
                TopCameraView.Instance.ResetAllStatusIndicators();

            if (SideCameraView.Instance != null)
                SideCameraView.Instance.ResetAllStatusIndicators();

            LeftCameraView.Instance?.ResetAllStatusIndicators();
            RightCameraView.Instance?.ResetAllStatusIndicators();

            FrontCameraView.Instance?.ResetTraceabilityStatus();
            _recordDisplays = new Dictionary<string, CogRecordDisplay>();

            if (TopCameraView.Instance?.CogRecordsDisplay1 != null)
            {
                _recordDisplays["T"] = TopCameraView.Instance.CogRecordsDisplay1;
            }
            else
            {
                logger?.Warn("TopCameraView display is not available during Window_Loaded.");
            }

            if (SideCameraView.Instance?.CogRecordsDisplay1 != null)
            {
                _recordDisplays["F"] = SideCameraView.Instance.CogRecordsDisplay1;
            }
            else if (LeftCameraView.Instance?.ImageDisplay == null)
            {
                logger?.Warn("Side/Left camera display is not available during Window_Loaded.");
            }

            if (LeftCameraView.Instance?.ImageDisplay != null)
            {
                _recordDisplays["L"] = LeftCameraView.Instance.ImageDisplay;
            }

            if (FrontCameraView.Instance?.CogRecordsDisplay1 != null)
            {
                _recordDisplays["FR"] = FrontCameraView.Instance.CogRecordsDisplay1;
            }
            else
            {
                logger?.Warn("FrontCameraView display is not available during Window_Loaded.");
            }

            if (RearCameraView.Instance?.ImageDisplay != null)
            {
                _recordDisplays["R"] = RearCameraView.Instance.ImageDisplay;
            }
            else if (RightCameraView.Instance?.ImageDisplay == null)
            {
                logger?.Warn("Rear/Right camera display is not available during Window_Loaded.");
            }

            if (RightCameraView.Instance?.ImageDisplay != null)
            {
                _recordDisplays["RI"] = RightCameraView.Instance.ImageDisplay;
            }

            if (BottomCameraView.Instance?.ImageDisplay != null)
            {
                _recordDisplays["B"] = BottomCameraView.Instance.ImageDisplay;
            }
            else
            {
                logger?.Warn("BottomCameraView display is not available during Window_Loaded.");
            }

            await InitializeUIComponentsAsync();
            UpdateUIForCurrentUser();
            // Aggiorna lo stato dei pulsanti dopo un breve ritardo per permettere l'inizializzazione
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DataContext is MainViewModel mainVm)
                {
                    // Aspetta che il sistema di visione sia inizializzato
                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            mainVm.UpdateControlButtonsState();
                            mainVm.MarkSystemAsReady();
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    });
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
            _saveImage = new SaveImage.SaveImage();
            when_save_FAIL = _saveImage.saving_array_definition(configManager.Config._SaveImagePercentage.PercSaveDefect);
            when_save_OK = _saveImage.saving_array_definition(configManager.Config._SaveImagePercentage.PercSaveOk);
            EnsureCameraDisplayManagerInitialized();

            // La configurazione ispezioni viene caricata prima che le viste camera esistano,
            // quindi il refresh fatto in quel momento cadeva su Instance ancora null e le
            // icone restavano quelle di default dello XAML (tutte visibili) fino al primo
            // pezzo. Qui le viste ci sono: si applica lo stato reale gia' all'avvio.
            RefreshCameraFeatureVisibility();
            EnsureCameraDisplayManagerInitialized().ApplyOutcomeBackgroundToAllViews(null);

            ServiceLocator.OperatorInactivityService.Start();
            ServiceLocator.OperatorInactivityService.RegisterActivity("window-loaded");

        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayoutResources(e.NewSize.Width, e.NewSize.Height);
        }

        private void ApplyResponsiveLayoutResources(double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            bool isCompact = width <= 1366 || height <= 768;
            bool isMedium = !isCompact && (width <= 1680 || height <= 900);

            if (isCompact)
            {
                SetResponsiveLayoutResource("ShellOuterBorderMargin", new Thickness(2));
                SetResponsiveLayoutResource("ShellInnerMargin", new Thickness(4));
                SetResponsiveLayoutResource("ShellContentPadding", new Thickness(3));
                SetResponsiveLayoutResource("ShellHostPadding", new Thickness(1));
                ApplyShellGridMetrics(6d, 180d, 4d);

                SetResponsiveLayoutResource("TopBarPadding", new Thickness(4));
                SetResponsiveLayoutResource("TopBarStatusPadding", new Thickness(8, 6, 8, 6));
                SetResponsiveLayoutResource("TopBarStatusMinWidth", 144d);
                SetResponsiveLayoutResource("TopBarStatusLabelFontSize", 8d);
                SetResponsiveLayoutResource("TopBarStatusValueFontSize", 12d);
                SetResponsiveLayoutResource("TopBarMetricCardPadding", new Thickness(6, 5, 6, 5));
                SetResponsiveLayoutResource("TopBarMetricCardMargin", new Thickness(0, 0, 4, 0));
                SetResponsiveLayoutResource("TopBarMetricLabelFontSize", 8d);
                SetResponsiveLayoutResource("TopBarMetricValueFontSize", 12d);

                SetResponsiveLayoutResource("NavigationMinWidth", 176d);
                SetResponsiveLayoutResource("NavigationHeaderMargin", new Thickness(6, 6, 6, 4));
                SetResponsiveLayoutResource("NavigationHeaderPadding", new Thickness(8));
                SetResponsiveLayoutResource("NavigationLogoHeight", 28d);
                SetResponsiveLayoutResource("NavigationTitleFontSize", 12d);
                SetResponsiveLayoutResource("NavigationSummaryFontSize", 9d);
                SetResponsiveLayoutResource("NavigationDateFontSize", 9d);
                SetResponsiveLayoutResource("NavigationSectionHeaderFontSize", 8d);
                SetResponsiveLayoutResource("NavigationSectionHeaderMargin", new Thickness(4, 6, 0, 3));
                SetResponsiveLayoutResource("NavigationMenuHeaderFontSize", 10d);
                SetResponsiveLayoutResource("NavigationSubMenuHeaderFontSize", 9d);
                SetResponsiveLayoutResource("NavigationButtonPadding", new Thickness(7, 5, 7, 5));
                SetResponsiveLayoutResource("NavigationMenuStackMargin", new Thickness(6, 0, 6, 4));
                SetResponsiveLayoutResource("NavigationFooterMargin", new Thickness(6, 0, 6, 6));
                SetResponsiveLayoutResource("NavigationFooterPadding", new Thickness(5));

                SetResponsiveLayoutResource("CameraInfoPadding", new Thickness(6, 5, 6, 5));
                SetResponsiveLayoutResource("CameraInfoFontSize", 10d);
                SetResponsiveLayoutResource("CameraDisplayHostHeight", 210d);
                SetResponsiveLayoutResource("CameraDisplayHostMinHeight", 186d);
                SetResponsiveLayoutResource("CameraFeatureLabelFontSize", 10d);
                SetResponsiveLayoutResource("CameraFeatureIconSize", 30d);
                SetResponsiveLayoutResource("CameraFeatureIconMargin", new Thickness(3));
                return;
            }

            if (isMedium)
            {
                SetResponsiveLayoutResource("ShellOuterBorderMargin", new Thickness(3));
                SetResponsiveLayoutResource("ShellInnerMargin", new Thickness(6));
                SetResponsiveLayoutResource("ShellContentPadding", new Thickness(4));
                SetResponsiveLayoutResource("ShellHostPadding", new Thickness(2));
                ApplyShellGridMetrics(8d, 192d, 6d);

                SetResponsiveLayoutResource("TopBarPadding", new Thickness(6));
                SetResponsiveLayoutResource("TopBarStatusPadding", new Thickness(10, 7, 10, 7));
                SetResponsiveLayoutResource("TopBarStatusMinWidth", 156d);
                SetResponsiveLayoutResource("TopBarStatusLabelFontSize", 9d);
                SetResponsiveLayoutResource("TopBarStatusValueFontSize", 13d);
                SetResponsiveLayoutResource("TopBarMetricCardPadding", new Thickness(8, 6, 8, 6));
                SetResponsiveLayoutResource("TopBarMetricCardMargin", new Thickness(0, 0, 6, 0));
                SetResponsiveLayoutResource("TopBarMetricLabelFontSize", 9d);
                SetResponsiveLayoutResource("TopBarMetricValueFontSize", 13d);

                SetResponsiveLayoutResource("NavigationMinWidth", 190d);
                SetResponsiveLayoutResource("NavigationHeaderMargin", new Thickness(8, 8, 8, 6));
                SetResponsiveLayoutResource("NavigationHeaderPadding", new Thickness(9));
                SetResponsiveLayoutResource("NavigationLogoHeight", 30d);
                SetResponsiveLayoutResource("NavigationTitleFontSize", 13d);
                SetResponsiveLayoutResource("NavigationSummaryFontSize", 10d);
                SetResponsiveLayoutResource("NavigationDateFontSize", 10d);
                SetResponsiveLayoutResource("NavigationSectionHeaderFontSize", 9d);
                SetResponsiveLayoutResource("NavigationSectionHeaderMargin", new Thickness(4, 8, 0, 4));
                SetResponsiveLayoutResource("NavigationMenuHeaderFontSize", 11d);
                SetResponsiveLayoutResource("NavigationSubMenuHeaderFontSize", 10d);
                SetResponsiveLayoutResource("NavigationButtonPadding", new Thickness(8, 6, 8, 6));
                SetResponsiveLayoutResource("NavigationMenuStackMargin", new Thickness(8, 0, 8, 6));
                SetResponsiveLayoutResource("NavigationFooterMargin", new Thickness(8, 0, 8, 8));
                SetResponsiveLayoutResource("NavigationFooterPadding", new Thickness(6));

                SetResponsiveLayoutResource("CameraInfoPadding", new Thickness(8, 6, 8, 6));
                SetResponsiveLayoutResource("CameraInfoFontSize", 11d);
                SetResponsiveLayoutResource("CameraDisplayHostHeight", 230d);
                SetResponsiveLayoutResource("CameraDisplayHostMinHeight", 205d);
                SetResponsiveLayoutResource("CameraFeatureLabelFontSize", 11d);
                SetResponsiveLayoutResource("CameraFeatureIconSize", 34d);
                SetResponsiveLayoutResource("CameraFeatureIconMargin", new Thickness(4));
                return;
            }

            SetResponsiveLayoutResource("ShellOuterBorderMargin", new Thickness(8));
            SetResponsiveLayoutResource("ShellInnerMargin", new Thickness(8));
            SetResponsiveLayoutResource("ShellContentPadding", new Thickness(6));
            SetResponsiveLayoutResource("ShellHostPadding", new Thickness(2));
            ApplyShellGridMetrics(8d, 220d, 8d);

            SetResponsiveLayoutResource("TopBarPadding", new Thickness(8));
            SetResponsiveLayoutResource("TopBarStatusPadding", new Thickness(12, 8, 12, 8));
            SetResponsiveLayoutResource("TopBarStatusMinWidth", 180d);
            SetResponsiveLayoutResource("TopBarStatusLabelFontSize", 10d);
            SetResponsiveLayoutResource("TopBarStatusValueFontSize", 14d);
            SetResponsiveLayoutResource("TopBarMetricCardPadding", new Thickness(10, 8, 10, 8));
            SetResponsiveLayoutResource("TopBarMetricCardMargin", new Thickness(0, 0, 8, 0));
            SetResponsiveLayoutResource("TopBarMetricLabelFontSize", 10d);
            SetResponsiveLayoutResource("TopBarMetricValueFontSize", 14d);

            SetResponsiveLayoutResource("NavigationMinWidth", 220d);
            SetResponsiveLayoutResource("NavigationHeaderMargin", new Thickness(10, 10, 10, 8));
            SetResponsiveLayoutResource("NavigationHeaderPadding", new Thickness(10));
            SetResponsiveLayoutResource("NavigationLogoHeight", 34d);
            SetResponsiveLayoutResource("NavigationTitleFontSize", 14d);
            SetResponsiveLayoutResource("NavigationSummaryFontSize", 11d);
            SetResponsiveLayoutResource("NavigationDateFontSize", 11d);
            SetResponsiveLayoutResource("NavigationSectionHeaderFontSize", 10d);
            SetResponsiveLayoutResource("NavigationSectionHeaderMargin", new Thickness(4, 10, 0, 4));
            SetResponsiveLayoutResource("NavigationMenuHeaderFontSize", 12d);
            SetResponsiveLayoutResource("NavigationSubMenuHeaderFontSize", 11d);
            SetResponsiveLayoutResource("NavigationButtonPadding", new Thickness(10, 8, 10, 8));
            SetResponsiveLayoutResource("NavigationMenuStackMargin", new Thickness(10, 0, 10, 8));
            SetResponsiveLayoutResource("NavigationFooterMargin", new Thickness(10, 0, 10, 10));
            SetResponsiveLayoutResource("NavigationFooterPadding", new Thickness(8));

            SetResponsiveLayoutResource("CameraInfoPadding", new Thickness(10, 8, 10, 8));
            SetResponsiveLayoutResource("CameraInfoFontSize", 12d);
            SetResponsiveLayoutResource("CameraDisplayHostHeight", 265d);
            SetResponsiveLayoutResource("CameraDisplayHostMinHeight", 225d);
            SetResponsiveLayoutResource("CameraFeatureLabelFontSize", 12d);
            SetResponsiveLayoutResource("CameraFeatureIconSize", 40d);
            SetResponsiveLayoutResource("CameraFeatureIconMargin", new Thickness(5));
        }

        private void SetResponsiveLayoutResource(string key, object value)
        {
            if (Application.Current != null && Application.Current.Resources.Contains(key))
            {
                Application.Current.Resources[key] = value;
                return;
            }

            Resources[key] = value;
        }

        private void ApplyShellGridMetrics(double outerCornerRadius, double sidebarWidth, double sidebarGapWidth)
        {
            if (ShellOuterBorder != null)
            {
                ShellOuterBorder.CornerRadius = new CornerRadius(outerCornerRadius);
            }

            if (ShellGapRow != null)
            {
                ShellGapRow.Height = new GridLength(sidebarGapWidth);
            }

            if (ShellSidebarColumn != null)
            {
                ShellSidebarColumn.Width = new GridLength(sidebarWidth);
            }

            if (ShellSidebarGapColumn != null)
            {
                ShellSidebarGapColumn.Width = new GridLength(sidebarGapWidth);
            }
        }
        private void OnPanelUserInteraction(object sender, InputEventArgs e)
        {
            ServiceLocator.OperatorInactivityService.RegisterActivity(e?.RoutedEvent?.Name ?? "panel-input");
        }

        private void OnOpcUaStartRequested(object sender, OpcUaCommandEventArgs e)
        {
            // OPC UA commands are marshalled onto the UI dispatcher because the
            // runtime start path still touches WPF-bound state and MainWindow
            // orchestration. The command acknowledgement is written only after
            // the local runtime path has accepted or rejected the command.
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                var ok = false;
                string ackMessage;
                try
                {
                    ServiceLocator.MachineRuntimeService.ReleaseContinuousRunHold(MachineRuntimeService.HoldReasonManualStop, "opc-ua-start");
                    if (_continuousRunCts == null || _continuousRunCts.IsCancellationRequested)
                    {
                        _continuousRunCts = new CancellationTokenSource();
                    }

                    await ServiceLocator.MachineRuntimeService.StartContinuousRunAsync(_continuousRunCts.Token);
                    _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                    ok = _isContinuousRunActive;
                    ackMessage = ok
                        ? "Start command completed: continuous run active."
                        : $"Start command processed but continuous run is not active. {ServiceLocator.MachineRuntimeService.LastVisionIssue}";
                }
                catch (Exception ex)
                {
                    ackMessage = $"Start command failed: {ex.Message}";
                    logger.Warn($"OPC UA start request failed: {ex.Message}");
                    _applicationEventLogger.LogException("OPCUA_START_REQUEST_FAILED", ex, nameof(OnOpcUaStartRequested), "OPC UA start request failed");
                }
                await ServiceLocator.OpcUaClientService.WriteCommandAcknowledgementAsync(e?.CommandId ?? 0, ok, ackMessage);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnOpcUaStopRequested(object sender, OpcUaCommandEventArgs e)
        {
            // A remote stop is treated like an intentional manual stop: add the
            // same hold reason so automatic recovery does not restart the line
            // without a later explicit Start command.
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                var ok = false;
                string ackMessage;
                try
                {
                    ServiceLocator.MachineRuntimeService.AddContinuousRunHold(MachineRuntimeService.HoldReasonManualStop, "opc-ua-stop");
                    await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync();
                    _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                    ok = !_isContinuousRunActive;
                    ackMessage = ok
                        ? "Stop command completed: continuous run inactive."
                        : "Stop command processed but continuous run is still active.";
                }
                catch (Exception ex)
                {
                    ackMessage = $"Stop command failed: {ex.Message}";
                    logger.Warn($"OPC UA stop request failed: {ex.Message}");
                    _applicationEventLogger.LogException("OPCUA_STOP_REQUEST_FAILED", ex, nameof(OnOpcUaStopRequested), "OPC UA stop request failed");
                }
                await ServiceLocator.OpcUaClientService.WriteCommandAcknowledgementAsync(e?.CommandId ?? 0, ok, ackMessage);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnOpcUaRecipeChangeRequested(object sender, OpcUaCommandEventArgs e)
        {
            // Recipe changes requested by MES/OPC UA must use the same guarded
            // transition service as the HMI, then publish a handshake result for
            // the external command owner.
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                var result = await ChangeRecipeFromOpcUaAsync(e);
                try
                {
                    await ServiceLocator.OpcUaClientService.WriteCommandAcknowledgementAsync(e?.CommandId ?? 0, result.Success, result.Message);
                }
                catch (Exception ackEx)
                {
                    logger.Warn(ackEx, $"OPCUA_RECIPE_CHANGE_ACK_WRITE_FAILED|commandId={e?.CommandId ?? 0}|success={result.Success}");
                    _applicationEventLogger.LogException(
                        "OPCUA_RECIPE_CHANGE_ACK_WRITE_FAILED",
                        ackEx,
                        nameof(OnOpcUaRecipeChangeRequested),
                        $"Unable to write OPC UA recipe change acknowledgement for command {e?.CommandId ?? 0}");
                }

                if (result.Success && result.RecipeChanged)
                {
                    ShowOpcUaRecipeChangeNotification(result.RecipeName, result.SourceLabel);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task<OpcUaCommandResult> ChangeRecipeFromOpcUaAsync(OpcUaCommandEventArgs command)
        {
            if (command == null)
            {
                return new OpcUaCommandResult(false, "Recipe command rejected: missing OPC UA command payload.");
            }

            if (string.Equals(command.CommandName, "RecipeId", StringComparison.OrdinalIgnoreCase))
            {
                long recipeId;
                if (!long.TryParse(command.Value, out recipeId) || recipeId <= 0)
                {
                    return new OpcUaCommandResult(false, $"Recipe ID command rejected: invalid ID '{command.Value}'.");
                }

                var recipeFromDb = await ResolveRecipeNameByIdForOpcUaAsync(recipeId);
                if (string.IsNullOrWhiteSpace(recipeFromDb))
                {
                    return new OpcUaCommandResult(false, $"Recipe ID {recipeId} not found in tblproduzione.");
                }

                var result = await ChangeRecipeFromOpcUaAsync(recipeFromDb);
                if (!result.Success)
                {
                    return result;
                }

                return new OpcUaCommandResult(true, $"Recipe ID {recipeId} resolved to {recipeFromDb}. {result.Message}",
                    result.RecipeChanged, result.RecipeName, "MES / OPC UA (RecipeId)");
            }

            return await ChangeRecipeFromOpcUaAsync(command.Value);
        }

        /// <summary>
        /// Checks that the recipe XML at <paramref name="recipeXmlPath"/> is compatible
        /// with the jobs currently loaded in VisionPro.
        ///
        /// Returns null when the recipe is valid; otherwise returns a human-readable
        /// rejection reason that is forwarded to OPC UA <c>CommandAckMessage</c>.
        ///
        /// Parse errors are treated as "pass" to avoid blocking production on malformed
        /// files — the normal load path will surface those errors at apply time.
        /// </summary>
        private string ValidateRecipeForMachine(string recipeXmlPath)
        {
            if (!System.IO.File.Exists(recipeXmlPath))
                return $"Recipe XML not found: {System.IO.Path.GetFileName(recipeXmlPath)}";

            try
            {
                var serializer = new XmlSerializer(typeof(Cls_Config.Calss_structure.RecipeParameters.RecipeData));
                Cls_Config.Calss_structure.RecipeParameters.RecipeData recipe;
                using (var stream = System.IO.File.OpenRead(recipeXmlPath))
                    recipe = (Cls_Config.Calss_structure.RecipeParameters.RecipeData)serializer.Deserialize(stream);

                var status = recipe?.inspectionStatus;
                if (status == null) return null;

                if ((status.BottomSealing || status.TrappedPaper) && _bottomJob == null)
                    return "Recipe enables Bottom camera checks but no Bottom job is loaded in VisionPro";
            }
            catch (Exception ex)
            {
                // Non-blocking: let the normal recipe load path surface parse errors.
                logger.Warn($"RECIPE_VALIDATION_PARSE_ERROR|path={recipeXmlPath}|{ex.Message}");
            }

            return null;
        }

        private async Task<OpcUaCommandResult> ChangeRecipeFromOpcUaAsync(string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                return new OpcUaCommandResult(false, "Recipe command rejected: empty recipe name.");
            }

            var normalizedRecipe = NormalizeOpcUaRecipeName(recipeName);
            var previousRecipe = configManager?.Config?.Configuration?.LastRecipe;

            try
            {
                if (string.Equals(previousRecipe, normalizedRecipe, StringComparison.OrdinalIgnoreCase))
                {
                    await PublishCurrentRecipeIdentityToOpcUaAsync(normalizedRecipe);
                    await RefreshRecipeManagerAfterExternalRecipeChangeAsync("opc-ua-recipe-already-active");
                    return new OpcUaCommandResult(true, $"Recipe already active: {normalizedRecipe}", false, normalizedRecipe, "MES / OPC UA");
                }

                // F3-B: validate recipe compatibility before touching machine state.
                // Prevents loading a recipe that references cameras not present in the current VPP.
                var recipeFolder = configManager?.Config?.Configuration?.Recipe_Folder ?? string.Empty;
                var recipeXmlPath = System.IO.Path.ChangeExtension(System.IO.Path.Combine(recipeFolder, normalizedRecipe), ".xml");
                var validationError = ValidateRecipeForMachine(recipeXmlPath);
                if (validationError != null)
                {
                    logger.Warn($"OPCUA_RECIPE_VALIDATION_FAILED|recipe={normalizedRecipe}|reason={validationError}");
                    return new OpcUaCommandResult(false, $"Recipe validation failed: {validationError}");
                }

                logger.Info($"OPCUA_RECIPE_CHANGE_REQUESTED|from={previousRecipe}|to={normalizedRecipe}");
                _applicationEventLogger.LogRecipeEvent("OPCUA_RECIPE_CHANGE_REQUESTED", normalizedRecipe, nameof(ChangeRecipeFromOpcUaAsync), "OPC UA recipe change requested");

                configManager.Config.Configuration.LastRecipe = normalizedRecipe;
                await configManager.SaveConfigAsync();

                await InitializeRecipeAsync(normalizedRecipe);
                await InitializeComponentforChangeRecipe();
                await PublishCurrentRecipeIdentityToOpcUaAsync(normalizedRecipe);
                await RefreshRecipeManagerAfterExternalRecipeChangeAsync("opc-ua-recipe-change-completed");

                _applicationEventLogger.LogRecipeEvent("OPCUA_RECIPE_CHANGE_COMPLETED", normalizedRecipe, nameof(ChangeRecipeFromOpcUaAsync), "OPC UA recipe change completed");
                return new OpcUaCommandResult(true, $"Recipe change completed: {normalizedRecipe}", true, normalizedRecipe, "MES / OPC UA");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"OPCUA_RECIPE_CHANGE_FAILED|target={normalizedRecipe}|rollback={previousRecipe}");
                _applicationEventLogger.LogException("OPCUA_RECIPE_CHANGE_FAILED", ex, nameof(ChangeRecipeFromOpcUaAsync), $"OPC UA recipe change failed: {normalizedRecipe}");

                if (!string.IsNullOrWhiteSpace(previousRecipe))
                {
                    try
                    {
                        configManager.Config.Configuration.LastRecipe = previousRecipe;
                        await configManager.SaveConfigAsync();
                        await InitializeRecipeAsync(previousRecipe);
                        await InitializeComponentforChangeRecipe();
                        await PublishCurrentRecipeIdentityToOpcUaAsync(previousRecipe);
                        await RefreshRecipeManagerAfterExternalRecipeChangeAsync("opc-ua-recipe-rollback");
                    }
                    catch (Exception rollbackEx)
                    {
                        logger.Fatal(rollbackEx, $"OPCUA_RECIPE_ROLLBACK_FAILED|target={previousRecipe}");
                        _applicationEventLogger.LogException("OPCUA_RECIPE_ROLLBACK_FAILED", rollbackEx, nameof(ChangeRecipeFromOpcUaAsync), "OPC UA recipe rollback failed");
                    }
                }

                return new OpcUaCommandResult(false, $"Recipe change failed for {normalizedRecipe}: {ex.Message}");
            }
        }

        private async Task RefreshRecipeManagerAfterExternalRecipeChangeAsync(string reason)
        {
            try
            {
                var recipeManager = App.ViewModelCache.RecipeManagerVM;
                if (recipeManager == null)
                {
                    return;
                }

                await recipeManager.RefreshProductionRecipeOrderAsync();
                logger.Info($"RECIPE_MANAGER_PRODUCTION_ORDER_REFRESHED|reason={reason}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"RECIPE_MANAGER_PRODUCTION_ORDER_REFRESH_FAILED|reason={reason}");
            }
        }

        private void ShowOpcUaRecipeChangeNotification(string recipeName, string sourceLabel)
        {
            try
            {
                var recipeDisplayName = System.IO.Path.GetFileNameWithoutExtension(recipeName);
                if (string.IsNullOrWhiteSpace(recipeDisplayName))
                {
                    recipeDisplayName = recipeName;
                }

                var source = string.IsNullOrWhiteSpace(sourceLabel)
                    ? "MES / OPC UA"
                    : sourceLabel;
                var changedAt = DateTime.Now.ToString("HH:mm:ss");
                var title = GetServerMessage(
                    "Sub_entry_OpcUaRecipeChangePopupTitle",
                    "Recipe changed by MES / OPC UA");
                var message = FormatServerMessage(
                    "Sub_entry_OpcUaRecipeChangePopupMessageFormat",
                    "The recipe in production has been changed by MES or another OPC UA server.\n\nRecipe: {0}\nSource: {1}\nTime: {2}\n\nAcknowledge to close this notification. If no operator acknowledges it, the popup closes automatically after 1 minute.",
                    recipeDisplayName,
                    source,
                    changedAt);
                var bannerText = FormatServerMessage(
                    "Sub_entry_OpcUaRecipeChangeBannerFormat",
                    "Recipe in production changed by MES / OPC UA: {0} at {1}.",
                    recipeDisplayName,
                    changedAt);
                var acknowledgeText = GetServerMessage("Sub_entry_Acknowledge", "ACKNOWLEDGE");

                if (MainView?.DataContext is MainViewModel mainVm)
                {
                    mainVm.ShowOperatorNotification(bannerText);
                }

                if (_opcUaRecipeNotificationWindow != null)
                {
                    try { _opcUaRecipeNotificationWindow.Close(); } catch { }
                    _opcUaRecipeNotificationWindow = null;
                }

                var popup = new SystemNotificationWindow(
                    title,
                    message,
                    NotificationSeverity.Info,
                    false,
                    TimeSpan.FromMinutes(1),
                    acknowledgeText)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                _opcUaRecipeNotificationWindow = popup;
                popup.Closed += (sender, args) =>
                {
                    if (ReferenceEquals(_opcUaRecipeNotificationWindow, popup))
                    {
                        _opcUaRecipeNotificationWindow = null;
                    }

                    var logId = popup.Confirmed
                        ? "OPCUA_RECIPE_CHANGE_OPERATOR_ACKNOWLEDGED"
                        : "OPCUA_RECIPE_CHANGE_OPERATOR_NOTIFICATION_TIMEOUT";
                    _applicationEventLogger.LogOperationalEvent(
                        LogLevel.Info,
                        logId,
                        "OPC UA",
                        $"OPC UA recipe change notification for {recipeDisplayName}",
                        nameof(ShowOpcUaRecipeChangeNotification),
                        $"source={source}|time={changedAt}");
                };

                popup.Show();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"OPCUA_RECIPE_CHANGE_NOTIFICATION_FAILED|recipe={recipeName}");
                _applicationEventLogger.LogException(
                    "OPCUA_RECIPE_CHANGE_NOTIFICATION_FAILED",
                    ex,
                    nameof(ShowOpcUaRecipeChangeNotification),
                    $"Unable to show OPC UA recipe change notification for {recipeName}");
            }
        }

        private static string GetServerMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        private static string FormatServerMessage(string key, string fallback, params object[] args)
        {
            var template = GetServerMessage(key, fallback);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return string.Format(fallback, args);
            }
        }

        private sealed class OpcUaCommandResult
        {
            public OpcUaCommandResult(bool success, string message, bool recipeChanged = false, string recipeName = null, string sourceLabel = null)
            {
                Success = success;
                Message = message;
                RecipeChanged = recipeChanged;
                RecipeName = recipeName;
                SourceLabel = sourceLabel;
            }

            public bool Success { get; }
            public string Message { get; }
            public bool RecipeChanged { get; }
            public string RecipeName { get; }
            public string SourceLabel { get; }
        }

        private static string NormalizeOpcUaRecipeName(string recipeName)
        {
            var trimmed = recipeName.Trim();
            return trimmed.EndsWith(".vpp", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + ".vpp";
        }

        private async Task<string> ResolveRecipeNameByIdForOpcUaAsync(long recipeId)
        {
            try
            {
                if (_insertDataInDb == null)
                {
                    _insertDataInDb = new InserdataInDb();
                }

                return await _insertDataInDb.GetRicettaNameByIdAsync(recipeId);
            }
            catch (Exception ex)
            {
                logger.Warn($"OPC UA recipe ID resolve failed: id={recipeId}; error={ex.Message}");
                return null;
            }
        }

        private async Task<long> ResolveRecipeIdForOpcUaAsync(string recipeName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(recipeName))
                {
                    return 0;
                }

                if (_insertDataInDb == null)
                {
                    _insertDataInDb = new InserdataInDb();
                }

                var recipeNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(recipeName);
                var recipeId = await _insertDataInDb.GetRicettaIdByNameAsync(recipeNameWithoutExtension);
                return recipeId ?? 0;
            }
            catch (Exception ex)
            {
                logger.Warn($"OPC UA recipe ID publish resolve failed: recipe={recipeName}; error={ex.Message}");
                return 0;
            }
        }

        private async Task PublishCurrentRecipeIdentityToOpcUaAsync(string recipeName)
        {
            var recipeNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(recipeName ?? string.Empty);
            var recipeId = await ResolveRecipeIdForOpcUaAsync(recipeNameWithoutExtension);

            await ServiceLocator.OpcUaClientService.WriteCurrentRecipeAsync(recipeNameWithoutExtension);
            await ServiceLocator.OpcUaClientService.WriteCurrentRecipeIdAsync(recipeId);
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_disposed || IsShuttingDown)
            {
                e.Cancel = false;
                return;
            }

            try
            {
                IsShuttingDown = true;
                e.Cancel = true;  // Annulla chiusura immediata

                var shutdownWindow = new ShutdownProgressWindow();
                shutdownWindow.Show();

                // Forza aggiornamento UI prima di procedere
                await Task.Delay(100);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                await ShutdownApplicationAsync(shutdownWindow);

                // Chiudi finestra progresso in modo sicuro
                try
                {
                    if (shutdownWindow.IsVisible)
                        shutdownWindow.Close();
                }
                catch (Exception ex) { logger.Warn(ex, "SHUTDOWN_WINDOW_CLOSE_FAILED â€” non-critical"); }

                _disposed = true;

                // Shutdown pulito di WPF
                await Task.Delay(100);  // Lascia che i messaggi UI si processino
                Application.Current.Shutdown(0);
            }
            catch (ObjectDisposedException ex)
            {
                logger?.Warn($"Oggetto giÃ  dispose-ato durante la chiusura: {ex.Message}");
                _disposed = true;
                Application.Current.Shutdown(0);
            }
            catch (Exception ex)
            {
                logger?.Error($"Errore in Window_Closing: {ex.Message}");
                _disposed = true;
                Application.Current.Shutdown(IsShuttingDown ? 0 : 1);  // Evita exit code di errore su chiusure gia' avviate
            }
        }
        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            this.DataContext = App.ViewModelCache.MainVM ?? new MainViewModel();

        }
        private void OnAlarmTriggered(object sender, AlarmTriggeredEventArgs e)
        {
            logger.Warn($"EJECTION ALARM: {e.Message}");
            _applicationEventLogger.LogAlarmEvent(e.Message, nameof(OnAlarmTriggered), "Ejection alarm triggered", true);

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                // 1. Turn the physical output ON.
                // Alarm output is runtime behavior: use the configured alarm
                // channel and keep the popup/log path independent from I/O
                // failures so the operator still sees the alarm.
                var ioManager = ServiceLocator.IoManager;
                int outputChannel = -1;

                if (ioManager == null)
                {
                    logger.Error("ALARM_IO_UNAVAILABLE|alarm={0}|channel={1} - IoManager not initialized, physical output skipped",
                        e.Alarm?.Name, e.Alarm?.OutputChannelId);
                    ServiceLocator.DialogService.ShowWarning(
                        $"Uscita fisica non disponibile per allarme '{e.Alarm?.Name}'\n" +
                        $"Scheda I/O non inizializzata - verificare hardware Advantech.",
                        "Hardware I/O non disponibile");
                }
                else if (e?.Alarm != null)
                {
                    var channelId = e.Alarm.OutputChannelId;

                    if (!TryParseOutputChannel(channelId, out int ch))
                    {
                        logger.Warn("ALARM_IO_SKIPPED_INVALID_CHANNEL|alarm={0}|signalType={1}|channel={2}",
                            e.Alarm.Name,
                            e.Alarm.SignalType,
                            channelId);
                    }
                    else
                    {
                        outputChannel = ch;
                        try
                        {
                            await ioManager.WriteOutputAsync(outputChannel, true);
                            ServiceLocator.OutputWatchdog?.Track(outputChannel, expectedHigh: true);
                            logger.Info("IO_OUTPUT_HIGH|channel=DO{0}|alarm={1}|signalType={2}|source=Alarm.OutputChannelId",
                                outputChannel,
                                e.Alarm.Name,
                                e.Alarm.SignalType);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "ALARM_IO_WRITE_FAILED|channel=DO{0}|alarm={1}", outputChannel, e.Alarm.Name);
                        }
                    }
                }

                // 2. Auto-turn-off after PulseDurationMs if > 0.
                // PulseDurationMs == 0 intentionally leaves the output latched
                // until the alarm/line logic resets it.
                if (outputChannel >= 0 && ioManager != null && e?.Alarm != null && e.Alarm.PulseDurationMs > 0)
                {
                    int duration = e.Alarm.PulseDurationMs;
                    int capturedChannel = outputChannel;
                    Task.Run(async () =>
                    {
                        await Task.Delay(duration);
                        await ioManager.WriteOutputAsync(capturedChannel, false);
                        ServiceLocator.OutputWatchdog?.Track(capturedChannel, expectedHigh: false);
                    }).SafeFireAndForget(logger, $"ALARM_IO_AUTO_RESET_FAILED|channel=DO{outputChannel}");
                }

                // 3. Show notification popup.
                // The popup is still shown even if physical output activation
                // fails, because operator acknowledgement and alarm diagnostics
                // remain necessary.
                try
                {
                    var popup = new AlarmNotificationWindow(e, ioManager) { Owner = this };
                    popup.ShowDialog();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "ALARM_POPUP_FAILED");
                }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        private static bool TryParseOutputChannel(string channelId, out int channel)
        {
            channel = -1;
            var normalized = NormalizeOutputChannelId(channelId);
            if (string.IsNullOrWhiteSpace(normalized) ||
                !normalized.StartsWith("DO", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(normalized.Substring(2), out var parsedChannel))
            {
                return false;
            }

            if (!Pcie1756ChannelMap.IsValidDoChannel(parsedChannel))
            {
                return false;
            }

            channel = parsedChannel;
            return true;
        }

        private static string NormalizeOutputChannelId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim().ToUpperInvariant();
            if (!value.StartsWith("DO", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return int.TryParse(value.Substring(2), out var channel)
                ? $"DO{channel}"
                : value;
        }

        // NUOVO: Metodo chiamato quando scade il timer di debounce


        // Metodi di inizializzazione
        public static void loadVsionPro()
        {
            var configuration = configManager?.Config?.Configuration;
            if (configuration == null)
            {
                throw new InvalidOperationException("Configuration is not loaded.");
            }

            string recipeToLoad = configuration.LastRecipe;
            string recipeFolder = configuration.Recipe_Folder;

            if (string.IsNullOrWhiteSpace(recipeFolder))
            {
                throw new InvalidOperationException("Recipe_Folder is not configured in Config.xml.");
            }

            if (string.IsNullOrWhiteSpace(recipeToLoad))
            {
                throw new InvalidOperationException("LastRecipe is not configured in Config.xml.");
            }

            string vppPath = System.IO.Path.Combine(
                   recipeFolder,
                   recipeToLoad);
            // Inizializza il manager Cognex con la nuova ricetta
            if (_cognexManager != null)
            {
                _cognexManager.Dispose();
            }
            _cognexManager = new CognexJobManager();
            ServiceLocator.MachineRuntimeService.AttachCognexManager(_cognexManager);
            _cognexManager.Initialize(vppPath);
            ApplyVisionProStitchingParametersForLeftJobs(nameof(loadVsionPro));
        }
        public async Task InitializeConfigAsync()
        {
            try
            {
                await configManager.EnsureLoadedAsync();
                logger.Info("Configurazione caricata");
                LogConfigurationSecurityPosture();
                // Aggiungi un ritardo per stabilizzare il sistema
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                logger.Error($"Errore nel caricamento della configurazione: {ex.Message}");
                throw;
            }
        }

        private void LogConfigurationSecurityPosture()
        {
            try
            {
                var mysql = configManager?.Config?.MySqlConnection;
                if (mysql == null)
                {
                    return;
                }

                if (string.Equals(mysql.User, "root", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(mysql.Password, "root", StringComparison.Ordinal))
                {
                    const string details = "MySQL configuration uses the known root/root default credential. Change DB user/password before networked production.";
                    _applicationEventLogger.LogOperationalEvent(
                        LogLevel.Warn,
                        "MYSQL_DEFAULT_CREDENTIALS_CONFIGURED",
                        "Security",
                        "Default MySQL credentials configured",
                        nameof(LogConfigurationSecurityPosture),
                        details,
                        new Dictionary<string, object>
                        {
                            { "host", mysql.Host },
                            { "database", mysql.Db },
                            { "user", mysql.User }
                        });
                    ServiceLocator.AuditLogService?.LogAsync(
                        "MYSQL_DEFAULT_CREDENTIALS_CONFIGURED",
                        "system",
                        $"host={mysql.Host}; database={mysql.Db}; user={mysql.User}").SafeFireAndForget(logger, "AUDIT_MYSQL_DEFAULT_CREDENTIALS_FAILED");
                }

                if (mysql.SslDisabled && !IsLocalDatabaseHost(mysql.Host))
                {
                    string details = $"MySQL TLS is disabled for non-local host '{mysql.Host}'. Enable TLS when DB traffic leaves the local machine.";
                    _applicationEventLogger.LogOperationalEvent(
                        LogLevel.Warn,
                        "MYSQL_TLS_DISABLED_REMOTE_HOST",
                        "Security",
                        "MySQL TLS disabled for a non-local host",
                        nameof(LogConfigurationSecurityPosture),
                        details,
                        new Dictionary<string, object>
                        {
                            { "host", mysql.Host },
                            { "database", mysql.Db }
                        });
                    ServiceLocator.AuditLogService?.LogAsync(
                        "MYSQL_TLS_DISABLED_REMOTE_HOST",
                        "system",
                        details).SafeFireAndForget(logger, "AUDIT_MYSQL_TLS_DISABLED_FAILED");
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "SECURITY_POSTURE_AUDIT_FAILED");
            }
        }

        private static bool IsLocalDatabaseHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return true;
            }

            var normalized = host.Trim();
            return string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, ".", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "::1", StringComparison.OrdinalIgnoreCase);
        }

        public async Task InitializeRecipeAsync(string recipeName = null)
        {
            try
            {
                string recipeToLoad = recipeName ?? configManager.Config.Configuration.LastRecipe;
                string vppPath = System.IO.Path.Combine(
                    configManager.Config.Configuration.Recipe_Folder,
                    recipeToLoad);

                recipePath = System.IO.Path.ChangeExtension(vppPath, ".xml");
                // Fase 0 stabilita': AsyncRecipeParam legge il file XML con using (nessun handle
                // trattenuto), quindi il GC.Collect forzato qui compensava nulla e congelava la UI
                // a ogni cambio ricetta. Basta rilasciare il riferimento.
                ConfigRecipeParam = null;
                ConfigRecipeParam = new AsyncRecipeParam(recipePath);
                await ConfigRecipeParam.EnsureLoadedAsync();

                await Task.Delay(500);
                double topTriggerDelay = 0;
                double sideTriggerDelay = 0;
                double frontTriggerDelay = 0;

                if (ConfigRecipeParam.Config?.cameraSetting != null)
                {
                    topTriggerDelay = ConfigRecipeParam.Config.cameraSetting.TopCameraTriggerDelay;
                    sideTriggerDelay = ConfigRecipeParam.Config.cameraSetting.SideCameraTriggerDelay;
                    frontTriggerDelay = ConfigRecipeParam.Config.cameraSetting.FrontCameraTriggerDelay;
                    MainWindow.logger?.Info($"Trigger delay caricati da ricetta - TOP: {topTriggerDelay}Î¼s, SIDE: {sideTriggerDelay}Î¼s, FRONT: {frontTriggerDelay}Î¼s");
                }
                else
                {
                    MainWindow.logger?.Warn("CameraSetting non trovato nella ricetta, uso valori default 0Î¼s");
                }
                counterUpdater = new CounterUpdater(ConfigRecipeParam);

                if (_cts == null || _cts.IsCancellationRequested)
                {
                    try
                    {
                        _cts?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Errore nel ripristino del token di elaborazione: {ex.Message}");
                    }

                    _cts = new CancellationTokenSource();
                }

                // Ricrea il validatore con i parametri della ricetta appena caricata.
                var validator = new ToolBlockValidator(ConfigRecipeParam);
                _inspectionProcessor = new InspectionProcessor(validator, logger);

                await ApplyRecipeTriggerDelaysAsync(
                    topTriggerDelay,
                    ConfigRecipeParam.Config?.cameraSetting?.ResolveLeftCameraTriggerDelay() ?? sideTriggerDelay,
                    frontTriggerDelay,
                    ConfigRecipeParam.Config?.cameraSetting?.RightCameraTriggerDelay ?? 0,
                    ConfigRecipeParam.Config?.cameraSetting?.BottomCameraTriggerDelay ?? 0,
                    nameof(InitializeRecipeAsync),
                    ConfigRecipeParam.Config?.cameraSetting?.RearCameraTriggerDelay ?? 0);
                if (App.ViewModelCache.DigitalIOVM != null)
                {
                    await App.ViewModelCache.DigitalIOVM.ApplyActiveRecipeRuntimeAdjustmentsAsync(
                        ConfigRecipeParam.Config,
                        nameof(InitializeRecipeAsync));
                }
                RefreshMultiShotConfiguredState();
                ApplyVisionProStitchingParametersForLeftJobs(nameof(InitializeRecipeAsync));
                ApplyTop3DDetectionSensitivityFromRecipe(nameof(InitializeRecipeAsync));
                RefreshRuntimeInspectionFeatures();
                await ActivateCameraIoFromActiveVppAsync(nameof(InitializeRecipeAsync));
                await Dispatcher.InvokeAsync(() =>
                    App.ViewModelCache.RecipeManagerVM?.RefreshCameraConfigurationFromActiveVpp());
                await Dispatcher.InvokeAsync(() =>
                    CameraContainer.Instance?.RefreshFromRuntimeConfiguration(),
                    System.Windows.Threading.DispatcherPriority.Background);

                logger.Info($"Ricetta '{recipeToLoad}' caricata con successo");

                if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
                {
                    mainVm.TopMenuBarVM.CurrentRecipeName = System.IO.Path.GetFileNameWithoutExtension(recipeToLoad);
                }

                await PublishCurrentRecipeIdentityToOpcUaAsync(recipeToLoad);
            }
            catch (Exception ex)
            {
                logger.Error($"Errore nel caricamento della ricetta: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Applies the active recipe trigger delays to the DALSA acquisition jobs
        /// in a single coordinated step and verifies the hardware readback.
        /// Keeping this logic centralized avoids stale top/side asymmetries when
        /// a recipe is reloaded while the machine transitions between stop/run.
        /// </summary>
        private async Task ApplyRecipeTriggerDelaysAsync(
            double topTriggerDelay, double leftTriggerDelay, double frontTriggerDelay,
            double rightTriggerDelay, double bottomTriggerDelay, string source, double rearTriggerDelay = 0)
        {
            if (!ApplyCameraTriggerDelayToHardwareAtStartup)
            {
                logger.Info(
                    $"RECIPE_TRIGGER_DELAY_SKIPPED|source={source}|mode=io-driven|top_recipe={topTriggerDelay}" +
                    $"|left_recipe={leftTriggerDelay}|right_recipe={rightTriggerDelay}|bottom_recipe={bottomTriggerDelay}|front_recipe={frontTriggerDelay}");
                return;
            }

            ApplyTriggerDelayIfAvailable(_topJob, topTriggerDelay, source, "Top");
            ApplyTriggerDelayIfAvailable(_sideJob, leftTriggerDelay, source, "Left");
            ApplyTriggerDelayIfAvailable(_frontJob, frontTriggerDelay, source, "Front");
            ApplyTriggerDelayIfAvailable(_rightJob, rightTriggerDelay, source, "Right");
            double effectiveRearDelay = _rightJob == null && Math.Abs(rearTriggerDelay) < 0.000001
                ? rightTriggerDelay : rearTriggerDelay;
            ApplyTriggerDelayIfAvailable(_rearJob, effectiveRearDelay, source, "Rear");
            ApplyTriggerDelayIfAvailable(_bottomJob, bottomTriggerDelay, source, "Bottom");

            await Task.Delay(150);

            double? topAppliedDelay = _topJob != null ? IgigaCameraAccess.ReadTriggerDelayFromCamera(_topJob) : null;
            double? leftAppliedDelay = _sideJob != null ? IgigaCameraAccess.ReadTriggerDelayFromCamera(_sideJob) : null;
            double? frontAppliedDelay = _frontJob != null ? IgigaCameraAccess.ReadTriggerDelayFromCamera(_frontJob) : null;
            double? rightAppliedDelay = _rightJob != null ? IgigaCameraAccess.ReadTriggerDelayFromCamera(_rightJob) : null;
            double? rearAppliedDelay = _rearJob != null ? IgigaCameraAccess.ReadTriggerDelayFromCamera(_rearJob) : null;
            double? bottomAppliedDelay = _bottomJob != null ? IgigaCameraAccess.ReadTriggerDelayFromCamera(_bottomJob) : null;

            logger.Info(
                $"RECIPE_TRIGGER_DELAY_APPLIED|source={source}|top_requested={topTriggerDelay}|top_applied={topAppliedDelay?.ToString() ?? "n/a"}" +
                $"|left_requested={leftTriggerDelay}|left_applied={leftAppliedDelay?.ToString() ?? "n/a"}" +
                $"|right_requested={rightTriggerDelay}|right_applied={rightAppliedDelay?.ToString() ?? "n/a"}" +
                $"|rear_requested={rearTriggerDelay}|rear_applied={rearAppliedDelay?.ToString() ?? "n/a"}" +
                $"|bottom_requested={bottomTriggerDelay}|bottom_applied={bottomAppliedDelay?.ToString() ?? "n/a"}" +
                $"|front_requested={frontTriggerDelay}|front_applied={frontAppliedDelay?.ToString() ?? "n/a"}");
        }

        private void ApplyTriggerDelayIfAvailable(CogJob job, double triggerDelay, string source, string role)
        {
            if (job != null)
            {
                IgigaCameraAccess.Initialize(job, triggerDelay);
            }
            else
            {
                logger.Warn($"RECIPE_TRIGGER_DELAY_SKIPPED|source={source}|job={role}|reason=job-null");
            }
        }

        /// <summary>
        /// Applica alla testa 3D (job "top3d") la detection sensitivity salvata nella ricetta corrente.
        /// No-op se: il job top non e' una testa 3D, il valore non e' configurato (&lt;= 0), o l'accesso
        /// GigE non e' disponibile. Additivo e non bloccante: un errore non ferma il caricamento ricetta.
        /// </summary>
        private void ApplyTop3DDetectionSensitivityFromRecipe(string source)
        {
            try
            {
                if (_topJob == null)
                {
                    return;
                }

                // Solo se la testa top e' effettivamente 3D.
                string topRole = ResolveCameraRoleForJobName(_topJob.Name);
                if (!string.Equals(topRole, "top3d", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                double sensitivity = ConfigRecipeParam?.Config?.recipeParamTop3D?.Top3DDetectionSensitivity ?? 0;
                if (sensitivity <= 0)
                {
                    logger.Debug($"TOP3D_DETECTION_SENSITIVITY_SKIPPED|source={source}|reason=not-configured");
                    return;
                }

                string featureName = configManager?.Config?.Configuration?.Top3DDetectionSensitivityFeature;
                bool applied = IgigaCameraAccess.ApplyDetectionSensitivity(_topJob, featureName, sensitivity);
                logger.Info($"TOP3D_DETECTION_SENSITIVITY_APPLIED|source={source}|value={sensitivity:0.###}|feature={featureName}|applied={applied}");
            }
            catch (Exception ex)
            {
                logger.Warn($"TOP3D_DETECTION_SENSITIVITY_FAILED|source={source}|error={ex.Message}");
            }
        }

        /// <summary>
        /// Tears down the active VisionPro runtime before a recipe reload.
        /// The goal is to fully release the current job manager, queues and
        /// display state so the next recipe starts from a clean machine state.
        /// </summary>
        private async Task PrepareVisionSystemForRecipeReloadAsync()
        {
            BeginVisionStateTransition();

            try
            {
                logger.Info("Preparazione del sistema di visione per il cambio ricetta...");
                StopVisionHealthMonitoring();

                try
                {
                    _continuousRunCts?.Cancel();
                }
                catch (Exception ex)
                {
                    logger.Warn($"Errore annullamento continuous run prima del cambio ricetta: {ex.Message}");
                }

                if (ServiceLocator.MachineRuntimeService.IsContinuousRunActive ||
                    ServiceLocator.MachineRuntimeService.IsContinuousRunRequested)
                {
                    await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync();
                    _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                }

                if (App.ViewModelCache.DigitalIOVM != null)
                {
                    await App.ViewModelCache.DigitalIOVM.ApplyActiveVppCameraRolesAsync(
                        Array.Empty<string>(), "recipe reload");
                }

                if (_eventManager != null && _cognexManager != null)
                {
                    try
                    {
                        _eventManager.UnregisterEvents(MyJobManager_UserResultAvailableAsync, CognexManager_JobStopped);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Errore deregistrazione eventi Cognex durante cambio ricetta: {ex.Message}");
                    }
                }

                _eventManager = null;

                if (_cognexManager != null)
                {
                    try
                    {
                        _cognexManager.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Errore dispose Cognex manager durante cambio ricetta: {ex.Message}");
                    }
                    finally
                    {
                        _cognexManager = null;
                        ServiceLocator.MachineRuntimeService.AttachCognexManager(null);
                    }
                }

                ResetVisionRuntimeStateForRecipeReload();
                await ResetVisionDisplaysForRecipeReloadAsync();
                await Task.Delay(300);
            }
            finally
            {
                EndVisionStateTransition();
            }
        }

        private void ResetVisionRuntimeStateForRecipeReload()
        {
            ClearPendingVisionResults("recipe reload", clearCachedArtifacts: true, forceLog: false);

            _topToolGroup = null;
            _sideToolGroup = null;
            _BottomToolGroup = null;
            _RearToolGroup = null;
            _FrontToolGroup = null;

            _topJob = null;
            _sideJob = null;
            _frontJob = null;
            _rearJob = null;
            _rightJob = null;
            _bottomJob = null;

            JobMapping.Clear();
            _isContinuousRunActive = false;
            ServiceLocator.MachineRuntimeService.UpdateContinuousRunState(false);
            ServiceLocator.MachineRuntimeService.UpdateContinuousRunRequested(false);
            ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
        }

        /// <summary>
        /// Returns true only when the machine runtime is allowed to consume and
        /// apply VisionPro results.
        ///
        /// This gate protects the HMI from a stale-results scenario where the
        /// machine status has already moved to Stopped while pending VisionPro
        /// events are still arriving from queues drained after the stop.
        /// </summary>
        private bool ShouldAcceptVisionResults()
        {
            if (IsShuttingDown || _cts.IsCancellationRequested || _cognexManager == null)
            {
                return false;
            }

            var runtimeService = ServiceLocator.MachineRuntimeService;
            if (runtimeService == null)
            {
                return false;
            }

            if (runtimeService.IsContinuousRunCommandInProgress)
            {
                return false;
            }

            return runtimeService.IsContinuousRunActive;
        }

        /// <summary>
        /// Clears queued/staged VisionPro results so that the UI, counters and
        /// DB pipeline stop consuming stale frames when the runtime is no longer
        /// in continuous mode.
        /// </summary>
        private void ClearPendingVisionResults(string reason, bool clearCachedArtifacts = false, bool forceLog = false)
        {
            CameraTriggerCorrelationTracker.Clear();
            while (_topQueue.TryDequeue(out CameraResult topResult)) topResult?.ReleaseOutputSnapshot();
            while (_leftQueue.TryDequeue(out CameraResult sideResult)) sideResult?.ReleaseOutputSnapshot();
            while (_frontQueue.TryDequeue(out CameraResult frontResult)) frontResult?.ReleaseOutputSnapshot();
            while (_rightQueue.TryDequeue(out CameraResult rearResult)) rearResult?.ReleaseOutputSnapshot();
            while (_rearQueue.TryDequeue(out CameraResult separateRearResult)) separateRearResult?.ReleaseOutputSnapshot();
            while (_bottomQueue.TryDequeue(out CameraResult bottomResult)) bottomResult?.ReleaseOutputSnapshot();

            if (clearCachedArtifacts)
            {
                _topRecord = null;
                _sideRecord = null;
                _frontRecord = null;
                _rearRecord = null;
                _rightRecord = null;
                _bottomRecord = null;

                _topSaveRecord = null;
                _sideSaveRecord = null;
                _frontSaveRecord = null;
                _rearSaveRecord = null;
                _rightSaveRecord = null;
                _bottomSaveRecord = null;
                _topDisplaySaveRecord = null;
                _sideDisplaySaveRecord = null;
                _frontDisplaySaveRecord = null;
                _rearDisplaySaveRecord = null;
                _rightDisplaySaveRecord = null;
                _bottomDisplaySaveRecord = null;

                _topToolBlockReults = null;
                _sideToolBlockResults = null;
                _frontTBResults = null;
                _RearTBResults = null;
                _RightTBResults = null;
                _bottomTBResults = null;
                _topToolBlockMmpx = null;
                _sideToolBlockMmpx = null;
                _FrontTBMmpx = null;
                _RearTBMmpx = null;
                _BottomTBMmpx = null;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (forceLog || nowUtc - _lastDiscardedVisionResultLogAtUtc >= TimeSpan.FromSeconds(2))
            {
                logger.Info($"Pending VisionPro results cleared because {reason}.");
                _lastDiscardedVisionResultLogAtUtc = nowUtc;
            }
        }

        private async Task ResetVisionDisplaysForRecipeReloadAsync()
        {
            await Dispatcher.InvokeAsync(() =>
            {
                ClearDisplayForRecipeReload(TopCameraView.Instance?.CogRecordsDisplay1);
                ClearDisplayForRecipeReload(SideCameraView.Instance?.CogRecordsDisplay1);
                ClearDisplayForRecipeReload(LeftCameraView.Instance?.CogRecordsDisplay1);
                ClearDisplayForRecipeReload(FrontCameraView.Instance?.CogRecordsDisplay1);
                ClearDisplayForRecipeReload(RearCameraView.Instance?.CogRecordsDisplay1);
                ClearDisplayForRecipeReload(RightCameraView.Instance?.CogRecordsDisplay1);
                ClearDisplayForRecipeReload(BottomCameraView.Instance?.CogRecordsDisplay1);

                TopCameraView.Instance?.ResetAllStatusIndicators();
                SideCameraView.Instance?.ResetAllStatusIndicators();
                LeftCameraView.Instance?.ResetAllStatusIndicators();
                RightCameraView.Instance?.ResetAllStatusIndicators();
                FrontCameraView.Instance?.ResetTraceabilityStatus();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ClearDisplayForRecipeReload(CogRecordDisplay display)
        {
            if (display == null)
            {
                return;
            }

            try
            {
                if (display.LiveDisplayRunning)
                {
                    display.StopLiveDisplay();
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"Errore stop live display durante cambio ricetta: {ex.Message}");
            }

            try { display.StaticGraphics?.Clear(); } catch (Exception ex) { logger.Warn(ex, "DISPLAY_CLEAR_STATIC_FAILED"); }
            try { display.InteractiveGraphics?.Clear(); } catch (Exception ex) { logger.Warn(ex, "DISPLAY_CLEAR_INTERACTIVE_FAILED"); }
            try { display.Image = null; } catch (Exception ex) { logger.Warn(ex, "DISPLAY_CLEAR_IMAGE_FAILED"); }
            try { display.Record = null; } catch (Exception ex) { logger.Warn(ex, "DISPLAY_CLEAR_RECORD_FAILED"); }
        }

        public async Task InitializeComponentforChangeRecipe(OperationProgressWindow progressWindow = null)
        {
            try
            {
                progressWindow?.UpdateStatus(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationPreparing", "Preparing operation..."),
                    "Preparing vision system for recipe reload.",
                    55);
                await PrepareVisionSystemForRecipeReloadAsync();
                progressWindow?.UpdateStatus(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationUpdatingConfiguration", "Updating configuration..."),
                    "Reloading runtime configuration and shared services.",
                    65);
                await InitializeConfigAsync();
                ServiceLocator.Initialize();
                progressWindow?.UpdateStatus(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationReadingRuntime", "Reading runtime resources..."),
                    "Loading inspection configuration.",
                    75);
                await LoadInspectionConfigurationAsync();
                progressWindow?.UpdateStatus(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationReloadingRuntime", "Reloading runtime..."),
                    "Initializing VisionPro jobs and displays.",
                    85);
                await InitializeVisionSystem();
                progressWindow?.UpdateStatus(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationRestoringRun", "Restoring continuous run..."),
                    "Restarting continuous acquisition.",
                    94);
                await runVisionPro();
                if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
                {
                    mainVm.UpdateControlButtonsState();
                    mainVm.MarkSystemAsReady();
                    mainVm.TopMenuBarVM?.MarkSystemAsReady();
                }
                progressWindow?.UpdateStatus(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperationCompleted", "Completed"),
                    "Recipe runtime aligned successfully.",
                    100);
               

                logger.Info("Componenti inizializzati dopo il cambio ricetta.");
            }
            catch (Exception ex)
            {
                logger.Error($"Errore nell'inizializzazione del database: {ex.Message}");
                throw;
            }
        }
        public async Task LoadInspectionConfigurationAsync()
        {
            try
            {
                string machineType = MainWindow.configManager.Config.Configuration.MachineType;
                // Carica le feature nel servizio
                await ServiceLocator.InspectionConfigService.LoadFeaturesAsync(machineType);
                EnabledInspectionFeatures = ServiceLocator.InspectionConfigService.GetEnabledFeatures();
                MainWindow.logger.Info($"Caricate feature per tipo macchina: {machineType}");
            }
            catch (Exception ex)
            {
                MainWindow.logger.Warn($"Impossibile caricare la configurazione delle ispezioni: {ex.Message}");
            }
        }

        public async Task runVisionPro()
        {
            // A manual or recipe-reload start clears the "intervention required" latch
            // so the recovery watchdog can resume normal operation if needed.
            _visionManualInterventionRequired = false;
            _visionRecoveryAttemptCount = 0;

            try
            {
                _applicationEventLogger.LogCommand("VisionProRunContinuous", "Requested", nameof(runVisionPro));

                // avvio la classe di gestione eventi cognex 
                _eventManager = new CognexEventManager(_cognexManager);
                _eventManager.RegisterEvents(MyJobManager_UserResultAvailableAsync, CognexManager_JobStopped);

                // controllo  se esiste l'id produzione nel db
                await checkIdProd();
                // Avvio esecuzione continua
                _continuousRunCts = new CancellationTokenSource();
                await Task.Delay(1000);
                await ServiceLocator.MachineRuntimeService.StartContinuousRunAsync(_continuousRunCts.Token);
                _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                await Task.Delay(2000);
                ServiceLocator.MachineRuntimeService.SyncStateFromManager();
                _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                RefreshMultiShotConfiguredState();
                StartVisionHealthMonitoring();
                _applicationEventLogger.LogVisionProStatus(_isContinuousRunActive, nameof(runVisionPro), "VisionPro startup sequence completed");


            }
            catch (OperationCanceledException)
            {
                logger.Warn("Inizializzazione configurazione annullata.");
                _applicationEventLogger.LogOperationalEvent(LogLevel.Warn, "VISIONPRO_RUN_CANCELLED", "VisionPro", "VisionPro startup cancelled", nameof(runVisionPro));
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "Errore durante l'inizializzazione della configurazione.");
                _applicationEventLogger.LogException("VISIONPRO_RUN_FAILED", ex, nameof(runVisionPro), "Errore durante l'avvio di VisionPro");
            }
           
        }

        /// <summary>
        /// Initializes the watchdog timestamps and starts the periodic health monitor.
        /// This watchdog is the main protection layer that detects stalled VisionPro
        /// pipelines and decides when automatic recovery is allowed to run.
        /// </summary>
        private void StartVisionHealthMonitoring()
        {
            RefreshVisionAcquisitionModeState();

            lock (_visionHealthLock)
            {
                var now = DateTime.UtcNow;
                _lastVisionResultAtUtc = now;
                _lastTopResultAtUtc = now;
                _lastSideResultAtUtc = now;
                _lastFrontResultAtUtc = now;
                _lastVisionResultByRole.Clear();
                foreach (string role in DataManage.CameraInspectionMatrix.SupportedRoles)
                {
                    _lastVisionResultByRole[role] = now;
                }
                _lastProcessedPairAtUtc = now;
                _pendingPairSinceUtc = DateTime.MinValue;
                _consecutiveMissingUserResults = 0;
                _consecutiveRunStatusErrors = 0;
                _lastVisionIssueDetails = null;
                _lastContinuousRunStartedAtUtc = now;
                _hasReceivedVisionHeartbeatSinceRunStart = false;
            }

            ServiceLocator.MachineRuntimeService.MarkVisionHealthy();

            _visionHealthTimer.Stop();
            _visionHealthTimer.AutoReset = true;
            _visionHealthTimer.Elapsed -= VisionHealthTimer_Elapsed;
            _visionHealthTimer.Elapsed += VisionHealthTimer_Elapsed;
            _visionHealthTimer.Start();
        }

        private void RefreshVisionAcquisitionModeState()
        {
            CogJob[] activeJobs = { _topJob, _sideJob, _frontJob, _rightJob, _rearJob, _bottomJob };
            CogJob[] configuredJobs = activeJobs.Where(job => job != null).ToArray();

            _isSyntheticVisionRuntime = configuredJobs.Length > 0 &&
                configuredJobs.All(IsSyntheticAcquisitionJob);

            TimeSpan timeout = GetActiveVisionResultTimeout();
            logger.Info(
                $"VISION_WATCHDOG_MODE|synthetic={_isSyntheticVisionRuntime}" +
                $"|jobs={configuredJobs.Length}|result_timeout_ms={timeout.TotalMilliseconds:0}");
        }

        /// <summary>
        /// Stops the watchdog and clears transient heartbeat fault indicators.
        /// Called during explicit stop and application shutdown.
        /// </summary>
        private void StopVisionHealthMonitoring()
        {
            _visionHealthTimer.Stop();
            _visionHealthTimer.Elapsed -= VisionHealthTimer_Elapsed;
            lock (_visionHealthLock)
            {
                _hasReceivedVisionHeartbeatSinceRunStart = false;
                _lastVisionIssueDetails = null;
            }
            ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
        }

        private void RegisterVisionHeartbeat(string jobName)
        {
            lock (_visionHealthLock)
            {
                var now = DateTime.UtcNow;
                _lastVisionResultAtUtc = now;
                _consecutiveMissingUserResults = 0;
                _lastVisionIssueDetails = null;
                _hasReceivedVisionHeartbeatSinceRunStart = true;

                // Avvia il cronometro dell'attesa solo sul primo risultato di una serie:
                // i risultati successivi appartengono alla stessa coppia in formazione.
                if (_pendingPairSinceUtc == DateTime.MinValue)
                {
                    _pendingPairSinceUtc = now;
                }

                string cameraRole = ResolveCameraRoleForJobName(jobName);
                if (!string.IsNullOrWhiteSpace(cameraRole))
                {
                    _lastVisionResultByRole[cameraRole] = now;
                }

                switch (cameraRole)
                {
                    case "top":
                    case "top3d":
                        _lastTopResultAtUtc = now;
                        break;
                    case "side":
                    case "left":
                        _lastSideResultAtUtc = now;
                        break;
                    case "rear":
                    case "bottom":
                        _lastSideResultAtUtc = now;
                        break;
                    case "front":
                        _lastFrontResultAtUtc = now;
                        break;
                }
            }

            ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
        }

        private string ResolveCameraRoleForJobName(string jobName)
        {
            if (string.IsNullOrWhiteSpace(jobName))
            {
                return string.Empty;
            }

            if (JobMapping != null)
            {
                foreach (var job in JobMapping)
                {
                    if (string.Equals(job.Value, jobName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ResolveCameraRole(job.Key);
                    }
                }
            }

            return CameraConfigurationHelper.NormalizeCameraType(jobName);
        }

        private CameraDisplayManager EnsureCameraDisplayManagerInitialized()
        {
            if (_cameraDisplayManager != null)
            {
                return _cameraDisplayManager;
            }

            _cameraDisplayManager = new Services.CameraDisplayManager(
                Dispatcher,
                ShouldAcceptVisionResults,
                RegisterRunStatusIssue,
                GetJobIdForJob);

            return _cameraDisplayManager;
        }

        private InspectionOrchestrator EnsureInspectionOrchestratorInitialized()
        {
            if (_inspectionOrchestrator != null)
                return _inspectionOrchestrator;

            _inspectionOrchestrator = new Services.InspectionOrchestrator(
                _processSemaphore,
                _topQueue,
                _leftQueue,
                _frontQueue,
                _rightQueue,
                _rearQueue,
                _bottomQueue,
                ShouldAcceptVisionResults,
                (reason, clearArtifacts) => ClearPendingVisionResults(reason, clearArtifacts),
                GetExpectedCompanionInspectionRoles,
                GetInspectionGroupTimeout,
                GetCompanionSameProductLagMs,
                ProcessInspectionGroupAsync);

            _inspectionOrchestrator.CompanionTimeoutOccurred += OnCompanionTimeoutOccurred;

            return _inspectionOrchestrator;
        }

        private void OnCompanionTimeoutOccurred(IReadOnlyList<string> missingRoles, double waitedMs)
        {
            if ((DateTime.Now - _lastCompanionTimeoutNotificationAt).TotalSeconds < 30)
                return;
            _lastCompanionTimeoutNotificationAt = DateTime.Now;

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                var roleList = string.Join(", ", missingRoles).ToUpperInvariant();
                var msg = $"Camera timeout: {roleList}\n" +
                          $"Risposta non ricevuta dopo {waitedMs / 1000.0:F0}s.\n" +
                          $"L'ispezione è stata processata senza questa camera.\n" +
                          $"Verificare la connessione della camera e i log per COMPANION_TIMEOUT.";
                var win = new Views.SystemNotificationWindow(
                    "ATTENZIONE — Camera timeout",
                    msg,
                    Views.NotificationSeverity.Warning,
                    false,
                    TimeSpan.FromSeconds(10));
                win.Show();
            }));
        }

        private string ResolveCameraRole(int jobId) => EnsureCameraDisplayManagerInitialized().ResolveCameraRole(jobId);

        private static string ResolveSideCameraDisplayRole()
        {
            try
            {
                if (CameraConfigurationHelper.HasCameraRole(JobRoleMapping, "left"))
                {
                    return "left";
                }

                var machineConfiguration = ResolveActiveRecipeMachineRuntimeConfiguration();
                var sideOptions = machineConfiguration?.MachineMultiShotTrigger?.Side;
                if (sideOptions?.Enabled == true &&
                    string.Equals(sideOptions.DisplayName, "Left", StringComparison.OrdinalIgnoreCase))
                {
                    return "left";
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "SIDE_LEFT_DISPLAY_ROLE_RESOLVE_FAILED");
            }

            return "left";
        }

        private static bool ToolBlockContainsOutput(CogToolBlock toolBlock, string outputName)
        {
            return toolBlock != null &&
                   !string.IsNullOrWhiteSpace(outputName) &&
                   toolBlock.Outputs != null &&
                   toolBlock.Outputs.Contains(outputName);
        }

        private static string ResolveRuntimeFrontTraceabilityPresenceOutputName()
        {
            return string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.FrontTraceabilityPresenceOutput)
                ? "TraceabilityPresent"
                : configManager.Config.Configuration.FrontTraceabilityPresenceOutput;
        }

        private static string ResolveRuntimeFrontTraceabilityCodeOutputName()
        {
            return string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.FrontTraceabilityCodeOutput)
                ? "TraceabilityCode"
                : configManager.Config.Configuration.FrontTraceabilityCodeOutput;
        }

        private static string ResolveRuntimeLeftSideSealingOutputName()
        {
            return string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.LeftSideSealingOutput)
                ? "LeftSideSealingOk"
                : configManager.Config.Configuration.LeftSideSealingOutput;
        }

        private static string ResolveRuntimeLeftRollCountOutputName()
        {
            return string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.LeftRollCountOutput)
                ? "LeftRollCountOk"
                : configManager.Config.Configuration.LeftRollCountOutput;
        }

        private static IEnumerable<string> ResolveRuntimeBottomSealingOutputNames()
        {
            yield return "BottomSealingOk";
            yield return "BottomSealOk";
            yield return "SaldaturaInferioreOk";
            yield return "LowerSealingOk";
            yield return "BottomSealing";
        }

        private static IEnumerable<string> ResolveRuntimeTrappedPaperOutputNames()
        {
            yield return "NoTrappedPaper";
            yield return "TrappedPaperOk";
            yield return "PaperClear";
            yield return "CartaIntrappolataOk";
            yield return "CartaInSaldaturaOk";
            yield return "TrappedPaperDetected";
            yield return "CartaIntrappolata";
            yield return "PaperTrapped";
        }

        private static string NormalizeSideDisplayRole(string role)
        {
            return string.Equals(CameraConfigurationHelper.NormalizeCameraType(role), "left", StringComparison.OrdinalIgnoreCase)
                ? "left"
                : ResolveSideCameraDisplayRole();
        }

        private static string ResolveRuntimeThreeDHeightOutputName()
        {
            return string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.ThreeDHeightOutput)
                ? "ThreeDHeight"
                : configManager.Config.Configuration.ThreeDHeightOutput;
        }

        private static string ResolveRuntimeThreeDWidthOutputName()
        {
            return string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.ThreeDWidthOutput)
                ? "ThreeDWidth"
                : configManager.Config.Configuration.ThreeDWidthOutput;
        }

        private static string ResolveRuntimeThreeDLengthOutputName()
        {
            return string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.ThreeDLengthOutput)
                ? "ThreeDLength"
                : configManager.Config.Configuration.ThreeDLengthOutput;
        }

        private Dictionary<string, IReadOnlyCollection<string>> BuildRuntimeSupportedFeaturesByRole()
        {
            var supported = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

            AddSupportedFeaturesForRole(
                supported,
                _topJob != null ? ResolveCameraRole(GetJobIdForJob(_topJob)) : string.Empty,
                _topToolBlockReults,
                _topToolBlockMmpx);

            AddSupportedFeaturesForRole(
                supported,
                _sideJob != null ? ResolveCameraRole(GetJobIdForJob(_sideJob)) : string.Empty,
                _sideToolBlockResults,
                _sideToolBlockMmpx,
                _sideJob?.Name);

            AddSupportedFeaturesForRole(
                supported,
                _frontJob != null ? ResolveCameraRole(GetJobIdForJob(_frontJob)) : string.Empty,
                _frontTBResults,
                null);

            AddSupportedFeaturesForRole(
                supported,
                _rearJob != null ? ResolveCameraRole(GetJobIdForJob(_rearJob)) : string.Empty,
                _RearTBResults,
                null);

            AddSupportedFeaturesForRole(
                supported,
                _rightJob != null ? ResolveCameraRole(GetJobIdForJob(_rightJob)) : string.Empty,
                _RightTBResults,
                null);

            AddSupportedFeaturesForRole(
                supported,
                _bottomJob != null ? ResolveCameraRole(GetJobIdForJob(_bottomJob)) : string.Empty,
                _bottomTBResults,
                _BottomTBMmpx);

            return supported;
        }

        private void AddSupportedFeaturesForRole(
            Dictionary<string, IReadOnlyCollection<string>> supported,
            string role,
            CogToolBlock resultsToolBlock,
            CogToolBlock shapeToolBlock,
            string jobName = null)
        {
            string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(role);
            if (!CameraConfigurationHelper.IsSupportedCameraRole(normalizedRole))
            {
                return;
            }

            // Un job "Side" dei VPP storici viene risolto a runtime come camera fisica "left".
            // Le sue uscite (Heigth, SealingArea, Shape) vanno rilevate comunque: altrimenti
            // la vista laterale perde Height, SideSealing e ShapeSide al caricamento del VPP.
            bool legacySideJob = string.Equals(
                CameraConfigurationHelper.NormalizeCameraType(jobName), "side", StringComparison.OrdinalIgnoreCase);

            var featureHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            switch (normalizedRole)
            {
                case "top":
                    if (ToolBlockContainsOutput(resultsToolBlock, "LogoPosition")) featureHints.Add("Logo");
                    if (ToolBlockContainsOutput(resultsToolBlock, "PrintCentering")) featureHints.Add("PrintCentering");
                    if (ToolBlockContainsOutput(resultsToolBlock, "OpenFlapsArea")) featureHints.Add("OpenFlaps");
                    if (shapeToolBlock != null || resultsToolBlock != null)
                    {
                        featureHints.Add("ShapeTop");
                    }
                    break;

                case "top3d":
                    if (ToolBlockContainsOutput(resultsToolBlock, ResolveRuntimeThreeDHeightOutputName())) featureHints.Add("ThreeDHeight");
                    if (ToolBlockContainsOutput(resultsToolBlock, ResolveRuntimeThreeDWidthOutputName())) featureHints.Add("ThreeDWidth");
                    if (ToolBlockContainsOutput(resultsToolBlock, ResolveRuntimeThreeDLengthOutputName())) featureHints.Add("ThreeDLength");
                    break;

                case "side":
                case "left":
                    // Uscite della vista laterale storica (job Side).
                    if (ToolBlockContainsOutput(resultsToolBlock, "Heigth")) featureHints.Add("Height");
                    if (ToolBlockContainsOutput(resultsToolBlock, "SealingArea")) featureHints.Add("SideSealing");
                    if (shapeToolBlock != null ||
                        (legacySideJob || normalizedRole == "side") && resultsToolBlock != null ||
                        ToolBlockContainsOutput(resultsToolBlock, "A_T_L") ||
                        ToolBlockContainsOutput(resultsToolBlock, "A_T_R") ||
                        ToolBlockContainsOutput(resultsToolBlock, "A_B_L") ||
                        ToolBlockContainsOutput(resultsToolBlock, "A_B_R"))
                    {
                        featureHints.Add("ShapeSide");
                    }

                    // Uscite dedicate della camera Left (sigillatura e conteggio rotoli).
                    if (ToolBlockContainsOutput(resultsToolBlock, ResolveRuntimeLeftSideSealingOutputName()) ||
                        ToolBlockContainsOutput(resultsToolBlock, "SideSealingOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "SealingOk"))
                    {
                        featureHints.Add("SideSealing");
                    }

                    if (ToolBlockContainsOutput(resultsToolBlock, ResolveRuntimeLeftRollCountOutputName()) ||
                        ToolBlockContainsOutput(resultsToolBlock, "RollCountOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "RollCount"))
                    {
                        featureHints.Add("SideRollCount");
                    }
                    break;

                case "front":
                    if (ToolBlockContainsOutput(resultsToolBlock, ResolveRuntimeFrontTraceabilityPresenceOutputName()) ||
                        ToolBlockContainsOutput(resultsToolBlock, ResolveRuntimeFrontTraceabilityCodeOutputName()))
                    {
                        featureHints.Add("FrontTraceability");
                    }
                    break;

                case "rear":
                    if (ToolBlockContainsOutput(resultsToolBlock, "RightSideSealingOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "RearSideSealingOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "RightSealingOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "RearSealingOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "SideSealingOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "SealingOk") ||
                        ToolBlockContainsOutput(resultsToolBlock, "SealingArea"))
                    {
                        featureHints.Add("SideSealing");
                    }

                    if (ToolBlockContainsOutput(resultsToolBlock, "A_T_L") ||
                        ToolBlockContainsOutput(resultsToolBlock, "A_T_R") ||
                        ToolBlockContainsOutput(resultsToolBlock, "A_B_L") ||
                        ToolBlockContainsOutput(resultsToolBlock, "A_B_R"))
                    {
                        featureHints.Add("ShapeSide");
                    }
                    break;

                case "bottom":
                    if (ResolveRuntimeBottomSealingOutputNames().Any(output => ToolBlockContainsOutput(resultsToolBlock, output)))
                    {
                        featureHints.Add("BottomSealing");
                    }

                    if (ResolveRuntimeTrappedPaperOutputNames().Any(output => ToolBlockContainsOutput(resultsToolBlock, output)))
                    {
                        featureHints.Add("TrappedPaper");
                    }
                    break;
            }

            // Classification is a camera-independent inspection. It is exposed
            // as its own feature and must never inherit SurfaceCheck or sealing
            // enablement merely because EL_Classify is present in a job.
            if (VisionClassificationResultReader.HasClassificationOutput(resultsToolBlock))
            {
                featureHints.Add("AIClassification");
            }

            supported[normalizedRole] = featureHints.ToList();
        }

        private void RefreshRuntimeInspectionFeatures()
        {
            try
            {
                ServiceLocator.InspectionConfigService.ApplyRuntimeRecipeFeatures(
                    ConfigRecipeParam?.Config,
                    JobRoleMapping,
                    BuildRuntimeSupportedFeaturesByRole());

                EnabledInspectionFeatures = ServiceLocator.InspectionConfigService.GetEnabledFeatures();
                RefreshCameraFeatureVisibility();
            }
            catch (Exception ex)
            {
                logger.Warn($"Unable to refresh runtime inspection feature map: {ex.Message}");
            }
        }

        /// <summary>
        /// Predispone l'I/O della pagina Digital I/O per le camere del VPP appena caricato:
        /// uscita trigger e punto intervento per ogni camera, allarmi e scarto OPEN/CLOSE.
        /// Aggiunge solo cio' che manca, senza canale e con i punti disabilitati.
        /// </summary>
        private async Task ActivateCameraIoFromActiveVppAsync(string source)
        {
            try
            {
                var digitalIo = App.ViewModelCache.DigitalIOVM;
                if (digitalIo == null)
                {
                    return;
                }

                if (JobMapping == null || JobMapping.Count == 0)
                {
                    await digitalIo.ApplyActiveVppCameraRolesAsync(Array.Empty<string>(), source);
                    logger.Warn($"VPP_CAMERA_ROLE_AUDIT|source={source}|jobs=0|roles=none");
                    return;
                }

                var cameraRoles = JobMapping.Keys
                    .Select(jobId => ResolveCameraRole(jobId))
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .ToList();
                logger.Info($"VPP_CAMERA_ROLE_AUDIT|source={source}|jobs={JobMapping.Count}|roles={string.Join(",", cameraRoles)}");
                await digitalIo.ProvisionCameraIoAsync(cameraRoles, source);
                await digitalIo.ApplyActiveVppCameraRolesAsync(cameraRoles, source);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"VPP_CAMERA_ROLE_AUDIT_FAILED|source={source}");
                throw;
            }
        }

        public void RefreshActiveRecipeInspectionConfiguration(string source)
        {
            ClearPendingVisionResults(
                $"active recipe inspection configuration changed ({source ?? "unspecified"})",
                clearCachedArtifacts: false,
                forceLog: true);
            RefreshRuntimeInspectionFeatures();
            CameraContainer.Instance?.RefreshFromRuntimeConfiguration();
            logger.Info(
                $"RECIPE_INSPECTION_RUNTIME_REFRESHED|source={source ?? "unspecified"}" +
                $"|recipe={ConfigRecipeParam?.Config?.general_Info?.RecipeName ?? "-"}");
        }

        private void RefreshCameraFeatureVisibility() => EnsureCameraDisplayManagerInitialized().RefreshCameraFeatureVisibility();

        public static void ApplyVisionProStitchingParametersForLeftJobs(string source, MachineRuntimeConfiguration machineConfiguration = null)
        {
            ApplyVisionProStitchingParametersForMultiShotJobs(source, machineConfiguration);
        }

        private static MachineRuntimeConfiguration ResolveActiveRecipeMachineRuntimeConfiguration(
            MachineRuntimeConfiguration machineConfiguration = null)
        {
            if (machineConfiguration == null)
            {
                MachineRuntimeConfiguration configuredRuntime = App.ViewModelCache.DigitalIOVM?.EffectiveRuntimeConfiguration;
                if (configuredRuntime != null)
                {
                    return configuredRuntime;
                }

                machineConfiguration = new MachineConfigurationService().Load();
            }

            RecipeMachineRuntimeResolution resolution = RecipeMachineRuntimeResolver.Resolve(
                machineConfiguration,
                ConfigRecipeParam?.Config);
            if (resolution.Errors.Count > 0)
            {
                logger?.Warn(
                    "RECIPE_MACHINE_RUNTIME_RESOLVE_FALLBACK|errors={0}",
                    string.Join(" | ", resolution.Errors));
            }

            return resolution.Configuration ?? machineConfiguration;
        }

        public static void ApplyVisionProStitchingParametersForMultiShotJobs(string source, MachineRuntimeConfiguration machineConfiguration = null)
        {
            try
            {
                if (_cognexManager == null || JobMapping == null || JobMapping.Count == 0)
                {
                    return;
                }

                machineConfiguration = ResolveActiveRecipeMachineRuntimeConfiguration(machineConfiguration);
                var multiShotConfiguration = machineConfiguration?.MachineMultiShotTrigger;
                if (multiShotConfiguration == null ||
                    !multiShotConfiguration.EnumerateProfiles().Any(profile => profile.Value?.Enabled == true))
                {
                    return;
                }

                var applier = new VisionProStitchingParameterApplier(ServiceLocator.ApplicationEventLogger);
                foreach (var mapping in JobMapping.OrderBy(entry => entry.Key))
                {
                    string runtimeRole = ResolveCameraRoleForLoadedJob(mapping.Key, mapping.Value);
                    var profileOptions = multiShotConfiguration.ResolveOptionsForRuntimeRole(runtimeRole);
                    if (profileOptions?.Enabled != true)
                    {
                        continue;
                    }

                    CogJob job = null;
                    try
                    {
                        job = _cognexManager.GetJob(mapping.Key);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, $"Unable to resolve VisionPro job {mapping.Key} while applying stitching parameters from {source}.");
                    }

                    bool stitchingApplied = applier.ApplyIfRequired(
                        job,
                        runtimeRole,
                        multiShotConfiguration,
                        mapping.Value,
                        mapping.Key);

                    string profileKey = ResolveMultiShotProfileKey(runtimeRole);
                    if (profileOptions.DualIllumination?.Enabled == true && !stitchingApplied)
                    {
                        _dualIlluminationPhaseCoordinator.Value.BlockProfile(
                            profileKey,
                            job,
                            "VisionPro ImageStitching inputs are missing or invalid");
                        continue;
                    }

                    _dualIlluminationPhaseCoordinator.Value.ConfigureAfterJobLoad(
                        profileKey,
                        job,
                        profileOptions,
                        source);
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "VISIONPRO_STITCHING_CONFIG_INVALID");
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    LogLevel.Error,
                    "VISIONPRO_STITCHING_CONFIG_INVALID",
                    "VisionPro",
                    $"VisionPro stitching parameters could not be applied: {ex.Message}",
                    nameof(ApplyVisionProStitchingParametersForLeftJobs),
                    source);
            }
        }

        private static string ResolveCameraRoleForLoadedJob(int jobId, string jobName)
        {
            string roleFromJobName = CameraConfigurationHelper.NormalizeCameraType(jobName);
            if (CameraConfigurationHelper.IsSupportedCameraRole(roleFromJobName))
            {
                return roleFromJobName;
            }

            string roleFromSerial = CameraConfigurationHelper.ResolveCameraRoleFromRuntimeSerial(jobId);
            if (CameraConfigurationHelper.IsSupportedCameraRole(roleFromSerial))
            {
                return roleFromSerial;
            }

            if (JobRoleMapping != null && JobRoleMapping.TryGetValue(jobId, out string configuredRole))
            {
                return CameraConfigurationHelper.NormalizeCameraType(configuredRole);
            }

            return roleFromJobName;
        }

        public static bool PrepareMultiShotCameraPhase(string profileKey, MultiShotTriggerOptions options)
        {
            CogJob job = ResolveMultiShotJob(profileKey);
            return _dualIlluminationPhaseCoordinator.Value.PrepareSession(profileKey, job, options);
        }

        public static void HandleMultiShotCameraPhaseEvent(MultiShotTriggerEventArgs eventArgs)
        {
            _dualIlluminationPhaseCoordinator.Value.HandleTriggerEvent(eventArgs);
        }

        public static void MarkMultiShotCameraPhasesForRealignment(string reason)
        {
            _dualIlluminationPhaseCoordinator.Value.MarkAllForRealignment(reason);
        }

        private static CogJob ResolveMultiShotJob(string profileKey)
        {
            if (string.Equals(profileKey, "Right", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profileKey, "Rear", StringComparison.OrdinalIgnoreCase))
            {
                var config = ResolveActiveRecipeMachineRuntimeConfiguration()?.MachineMultiShotTrigger;
                var options = string.Equals(profileKey, "Rear", StringComparison.OrdinalIgnoreCase)
                    ? config?.Rear : config?.Right;
                return string.Equals(CameraConfigurationHelper.NormalizeCameraType(options?.CameraRole), "right", StringComparison.OrdinalIgnoreCase)
                    ? _rightJob : _rearJob;
            }

            if (string.Equals(profileKey, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return _bottomJob;
            }

            return _sideJob;
        }

        private static string ResolveMultiShotProfileKey(string runtimeRole)
        {
            string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(runtimeRole);
            var config = ResolveActiveRecipeMachineRuntimeConfiguration()?.MachineMultiShotTrigger;
            var options = config?.ResolveOptionsForRuntimeRole(normalizedRole);
            var matched = config?.EnumerateProfiles()
                .FirstOrDefault(profile => ReferenceEquals(profile.Value, options));
            if (matched.HasValue && !string.IsNullOrWhiteSpace(matched.Value.Key))
            {
                return matched.Value.Key;
            }
            if (string.Equals(normalizedRole, "right", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedRole, "rear", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedRole == "right" ? "Right" : "Rear";
            }

            if (string.Equals(normalizedRole, "bottom", StringComparison.OrdinalIgnoreCase))
            {
                return "Bottom";
            }

            return "Side";
        }

        private string GetActiveSecondaryInspectionRole()
        {
            if (CameraConfigurationHelper.HasCameraRole(JobRoleMapping, "left") &&
                _sideJob != null &&
                ServiceLocator.InspectionConfigService.IsCameraRoleEnabled("left"))
            {
                return "left";
            }

            if (CameraConfigurationHelper.HasCameraRole(JobRoleMapping, "side") &&
                _sideJob != null &&
                ServiceLocator.InspectionConfigService.IsCameraRoleEnabled("side"))
            {
                return "side";
            }

            if (CameraConfigurationHelper.HasCameraRole(JobRoleMapping, "front") &&
                _frontJob != null &&
                ServiceLocator.InspectionConfigService.IsCameraRoleEnabled("front"))
            {
                return "front";
            }

            if (_sideJob != null && ServiceLocator.InspectionConfigService.IsCameraRoleEnabled("side"))
            {
                return "side";
            }

            if (_frontJob != null && ServiceLocator.InspectionConfigService.IsCameraRoleEnabled("front"))
            {
                return "front";
            }

            return string.Empty;
        }

        private int GetJobIdForJob(CogJob job)
        {
            if (job == null || JobMapping == null)
            {
                return -1;
            }

            foreach (var mapping in JobMapping)
            {
                if (string.Equals(mapping.Value, job.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.Key;
                }
            }

            return -1;
        }

        private static bool IsThreeDCheckMachineType()
        {
            return string.Equals(
                configManager?.Config?.Configuration?.MachineType,
                "3DCheck",
                StringComparison.OrdinalIgnoreCase);
        }

        private ConcurrentQueue<CameraResult> GetQueueForRole(string role)
        {
            switch (CameraConfigurationHelper.NormalizePhysicalCameraRole(role))
            {
                case "top":
                    return _topQueue;
                case "left":
                    return _leftQueue;
                case "front":
                    return _frontQueue;
                case "right":
                    return _rightQueue;
                case "rear":
                    return _rearQueue;
                case "bottom":
                    return _bottomQueue;
                default:
                    return null;
            }
        }

        private IReadOnlyList<string> GetExpectedCompanionInspectionRoles()
        {
            var roles = new List<string>();

            AddExpectedRole(roles, _sideJob != null ? ResolveCameraRole(GetJobIdForJob(_sideJob)) : string.Empty);
            AddExpectedRole(roles, _frontJob != null ? "front" : string.Empty);
            AddExpectedRole(roles, _rearJob != null ? ResolveCameraRole(GetJobIdForJob(_rearJob)) : string.Empty);
            AddExpectedRole(roles, _rightJob != null ? ResolveCameraRole(GetJobIdForJob(_rightJob)) : string.Empty);
            AddExpectedRole(roles, _bottomJob != null ? "bottom" : string.Empty);

            return roles;
        }

        private static void AddExpectedRole(ICollection<string> roles, string role)
        {
            string normalizedRole = CameraConfigurationHelper.NormalizePhysicalCameraRole(role);
            if (string.Equals(normalizedRole, "top", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(normalizedRole) ||
                !CameraConfigurationHelper.IsSupportedCameraRole(normalizedRole))
            {
                return;
            }

            if (ServiceLocator.InspectionConfigService?.IsCameraRoleEnabled(normalizedRole) == false)
            {
                return;
            }

            if (!roles.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
            {
                roles.Add(normalizedRole);
            }
        }

        private CogRecordDisplay GetDisplayForRole(string role) => EnsureCameraDisplayManagerInitialized().GetDisplayForRole(role);

        private string GetLastRunViewForRole(string role) => EnsureCameraDisplayManagerInitialized().GetLastRunViewForRole(role);

        private void RegisterProcessedInspectionPair()
        {
            lock (_visionHealthLock)
            {
                _lastProcessedPairAtUtc = DateTime.UtcNow;
                _pendingPairSinceUtc = DateTime.MinValue; // nulla piu' in attesa
                _consecutiveRunStatusErrors = 0;
                _lastVisionIssueDetails = null;
            }

            ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
        }

        private void RegisterMissingUserResult()
        {
            int missingCount;

            lock (_visionHealthLock)
            {
                if (!_hasReceivedVisionHeartbeatSinceRunStart)
                {
                    _lastVisionIssueDetails = null;
                    return;
                }

                _consecutiveMissingUserResults++;
                missingCount = _consecutiveMissingUserResults;
                _lastVisionIssueDetails = "UserResult queue is empty while VisionPro reports an available result.";
            }

            if (missingCount == 1 || missingCount % 10 == 0)
            {
                logger.Warn("No UserResult available.");
                _applicationEventLogger.LogOperationalEvent(
                    LogLevel.Warn,
                    "VISIONPRO_USER_RESULT_MISSING",
                    "VisionPro",
                    "No UserResult available from VisionPro queue",
                    nameof(MyJobManager_UserResultAvailableAsync),
                    "VisionPro ha notificato un risultato disponibile ma la UserQueue era vuota.",
                    new Dictionary<string, object>
                    {
                        { "consecutive_missing_results", missingCount }
                    });
            }
        }

        private void RegisterRunStatusIssue(CogJob job, ICogRunStatus runStatus)
        {
            int runStatusErrorCount;

            lock (_visionHealthLock)
            {
                if (!_hasReceivedVisionHeartbeatSinceRunStart)
                {
                    _lastVisionIssueDetails = null;
                    return;
                }

                _consecutiveRunStatusErrors++;
                runStatusErrorCount = _consecutiveRunStatusErrors;
                _lastVisionIssueDetails = runStatus != null ? runStatus.Message : "Unknown VisionPro run status error";
            }

            var jobName = job != null ? job.Name : "Unknown";
            var runStatusMessage = runStatus != null ? runStatus.Message : "Unknown VisionPro run status error";

            if (runStatusErrorCount != 1 && runStatusErrorCount % 5 != 0)
            {
                return;
            }

            logger.Error($"RunStatus Error: {runStatusMessage}");
            _applicationEventLogger.LogOperationalEvent(
                LogLevel.Error,
                "USER_RESULT_AVAILABLE_ERROR",
                "VisionPro",
                "RunStatus error detected during display update",
                nameof(UpdateDisplayParallelAsync),
                runStatusMessage,
                new Dictionary<string, object>
                {
                    { "job_name", jobName },
                    { "runstatus_error_count", runStatusErrorCount },
                    { "continuous_run_active", ServiceLocator.MachineRuntimeService.IsContinuousRunActive },
                    { "runstatus_result", runStatus != null ? runStatus.Result.ToString() : "Unknown" }
                });
        }

        private async void VisionHealthTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (Interlocked.Exchange(ref _visionHealthMonitorRunning, 1) == 1)
            {
                return;
            }

            try
            {
                await MonitorVisionHealthAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unhandled error in vision health monitor timer.");
            }
            finally
            {
                Interlocked.Exchange(ref _visionHealthMonitorRunning, 0);
            }
        }

        /// <summary>
        /// Opens a suppression window so watchdog/recovery logic does not react to
        /// expected gaps while the machine is intentionally switching state.
        /// </summary>
        private void BeginVisionStateTransition()
        {
            lock (_visionHealthLock)
            {
                _isVisionStateChangeInProgress = true;
                _suppressVisionAutoRecoveryUntilUtc = DateTime.UtcNow.Add(VisionStateTransitionCooldown);
                _consecutiveMissingUserResults = 0;
                _consecutiveRunStatusErrors = 0;
                _lastVisionIssueDetails = null;
            }
        }

        /// <summary>
        /// Closes the guarded transition and keeps a short cooldown to prevent
        /// immediate re-entry into recovery after a manual state change.
        /// </summary>
        private void EndVisionStateTransition()
        {
            lock (_visionHealthLock)
            {
                _isVisionStateChangeInProgress = false;
                _suppressVisionAutoRecoveryUntilUtc = DateTime.UtcNow.Add(VisionStateTransitionCooldown);
            }
        }

        private bool IsVisionAutoRecoverySuppressed()
        {
            lock (_visionHealthLock)
            {
                return _isVisionStateChangeInProgress
                    || DateTime.UtcNow < _suppressVisionAutoRecoveryUntilUtc
                    || ServiceLocator.MachineRuntimeService.IsContinuousRunCommandInProgress;
            }
        }

        /// <summary>
        /// Core watchdog routine.
        /// It compares the desired runtime state with the actual VisionPro state
        /// and uses heartbeat/result timestamps to decide whether a recovery is
        /// truly needed or whether the machine is simply idle.
        /// </summary>
        private async Task MonitorVisionHealthAsync()
        {
            if (IsShuttingDown || _cognexManager == null)
            {
                return;
            }

            if (IsVisionAutoRecoverySuppressed())
            {
                ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
                return;
            }

            ServiceLocator.MachineRuntimeService.SyncStateFromManager();
            _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
            bool isContinuousRunRequested = ServiceLocator.MachineRuntimeService.IsContinuousRunRequested;
            DateTime nowUtc = DateTime.UtcNow;

            if (!_isContinuousRunActive && !isContinuousRunRequested)
            {
                ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
                return;
            }

            if (!_isContinuousRunActive && isContinuousRunRequested)
            {
                string stoppedWhileExpectedDetails = "Continuous run is requested but one or more VisionPro jobs are no longer running.";
                ServiceLocator.MachineRuntimeService.MarkVisionFault(stoppedWhileExpectedDetails);

                if (nowUtc - _lastVisionRecoveryAttemptAtUtc >= VisionRecoveryCooldown)
                {
                    await AttemptVisionAutoRecoveryAsync(stoppedWhileExpectedDetails);
                }

                return;
            }
            DateTime lastVisionResultAtUtc;
            Dictionary<string, DateTime> lastVisionResultByRole;
            DateTime lastProcessedPairAtUtc;
            DateTime pendingPairSinceUtc;
            DateTime lastRecoveryAttemptAtUtc;
            DateTime lastContinuousRunStartedAtUtc;
            int consecutiveMissingUserResults;
            int consecutiveRunStatusErrors;
            string issueDetails;
            bool hasReceivedVisionHeartbeatSinceRunStart;

            lock (_visionHealthLock)
            {
                lastVisionResultAtUtc = _lastVisionResultAtUtc;
                lastVisionResultByRole = new Dictionary<string, DateTime>(
                    _lastVisionResultByRole,
                    StringComparer.OrdinalIgnoreCase);
                lastProcessedPairAtUtc = _lastProcessedPairAtUtc;
                pendingPairSinceUtc = _pendingPairSinceUtc;
                lastRecoveryAttemptAtUtc = _lastVisionRecoveryAttemptAtUtc;
                lastContinuousRunStartedAtUtc = _lastContinuousRunStartedAtUtc;
                consecutiveMissingUserResults = _consecutiveMissingUserResults;
                consecutiveRunStatusErrors = _consecutiveRunStatusErrors;
                issueDetails = _lastVisionIssueDetails;
                hasReceivedVisionHeartbeatSinceRunStart = _hasReceivedVisionHeartbeatSinceRunStart;
            }

            bool startupGracePeriodActive = !hasReceivedVisionHeartbeatSinceRunStart &&
                nowUtc - lastContinuousRunStartedAtUtc < VisionStartupGracePeriod;

            if (startupGracePeriodActive)
            {
                ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
                return;
            }

            if (!hasReceivedVisionHeartbeatSinceRunStart)
            {
                ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
                return;
            }

            bool sideLeftMultiShotActive = IsAnyMultiShotRuntimeActive();
            TimeSpan resultTimeout = GetActiveVisionResultTimeout();

            bool anyResultTimedOut = nowUtc - lastVisionResultAtUtc > resultTimeout;
            var expectedRoles = new[] { _topJob, _sideJob, _frontJob, _rightJob, _rearJob, _bottomJob }
                .Where(job => job != null)
                .Select(job => ResolveCameraRole(GetJobIdForJob(job)))
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Where(role => ServiceLocator.InspectionConfigService.IsCameraRoleEnabled(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var timedOutRoles = expectedRoles
                .Where(role =>
                {
                    // Quando la correlazione trigger e' attiva, l'unico indizio affidabile di
                    // camera che non risponde e' un trigger emesso e rimasto senza risultato.
                    // Il criterio precedente guardava solo da quanto tempo la camera non
                    // produce nulla: dopo l'ultimo prodotto quel silenzio e' normale, ma se
                    // un'altra camera restava fresca la differenza veniva letta come desync e
                    // faceva partire il recovery, che ferma la run e scarta i risultati.
                    if (CameraTriggerCorrelationTracker.IsTriggerTrackingActive(role))
                    {
                        return CameraTriggerCorrelationTracker.IsAwaitingResult(role, resultTimeout);
                    }

                    // Senza correlazione trigger (trigger non gestiti dall'HMI) resta valido
                    // il criterio storico basato sull'eta' dell'ultimo risultato.
                    DateTime lastRoleResult = lastVisionResultByRole.TryGetValue(role, out DateTime value)
                        ? value
                        : lastContinuousRunStartedAtUtc;
                    return nowUtc - lastRoleResult > resultTimeout;
                })
                .ToList();
            // Companion waiting is allowed to consume the full result timeout. Give the
            // final validation/counter stage a short margin before declaring the pipeline
            // stalled, otherwise the watchdog can race the legitimate timeout path.
            TimeSpan processingStallTimeout = resultTimeout + VisionProcessingStallGrace;
            // Si misura da quando un risultato e' in attesa di diventare coppia elaborata.
            // Senza risultati pendenti la pipeline e' semplicemente ferma perche' non
            // passano prodotti: non e' uno stallo e non deve innescare il recovery, che
            // scarterebbe i risultati in volo del prodotto successivo.
            bool pairTimedOut = pendingPairSinceUtc != DateTime.MinValue &&
                                nowUtc - pendingPairSinceUtc > processingStallTimeout;
            bool tooManyMissingResults = consecutiveMissingUserResults >= 5;
            bool repeatedRunStatusErrors = consecutiveRunStatusErrors >= 3 && pairTimedOut;
            bool cameraDesyncDetected = timedOutRoles.Count > 0 && timedOutRoles.Count < expectedRoles.Count;
            bool processingPipelineStalled = pairTimedOut && !anyResultTimedOut;

            if (sideLeftMultiShotActive)
            {
                processingPipelineStalled = false;
            }

            bool meaningfulVisionFault = cameraDesyncDetected || processingPipelineStalled || tooManyMissingResults || repeatedRunStatusErrors;
            bool recoveryCoolingDown = nowUtc - lastRecoveryAttemptAtUtc < VisionRecoveryCooldown;

            if (!meaningfulVisionFault)
            {
                ServiceLocator.MachineRuntimeService.MarkVisionHealthy();
                return;
            }

            var stallDetails = BuildVisionStallDetails(
                anyResultTimedOut,
                timedOutRoles,
                pairTimedOut,
                tooManyMissingResults,
                repeatedRunStatusErrors,
                issueDetails,
                sideLeftMultiShotActive,
                resultTimeout);

            ServiceLocator.MachineRuntimeService.MarkVisionFault(stallDetails);

            if (recoveryCoolingDown)
            {
                return;
            }

            await AttemptVisionAutoRecoveryAsync(stallDetails);
        }

        private bool IsAnyMultiShotRuntimeActive() => _isMultiShotConfigured;

        private TimeSpan GetActiveVisionResultTimeout()
        {
            if (_isSyntheticVisionRuntime)
            {
                return VisionSyntheticResultTimeout;
            }

            return IsAnyMultiShotRuntimeActive()
                ? VisionMultiShotResultTimeout
                : VisionResultTimeout;
        }

        /// <summary>
        /// Reads MultiShot config from disk ONCE and caches the result in <see cref="_isMultiShotConfigured"/>.
        /// Call at machine start (runVisionPro) so that the 20ms polling loop never touches the disk
        /// and never returns false during an atomic config-file rename.
        /// </summary>
        private void RefreshMultiShotConfiguredState()
        {
            try
            {
                var config = ResolveActiveRecipeMachineRuntimeConfiguration();
                var multiShot = config?.MachineMultiShotTrigger;
                _isMultiShotConfigured = multiShot != null &&
                    multiShot.EnumerateProfiles().Any(p =>
                        p.Value?.Enabled == true && p.Value.ShotCount > 1);

                int lag = config?.RuntimeBindings?.MultiShotCompanionMaxLagMs ?? 0;
                if (lag > 0)
                    _multiShotCompanionMaxLagMs = lag;

                logger.Info($"MULTISHOT_CONFIG|configured={_isMultiShotConfigured}|companion_lag_ms={_multiShotCompanionMaxLagMs}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "MULTISHOT_CONFIG|Config read failed; keeping previous cached state.");
            }
        }

        private string BuildVisionStallDetails(
            bool anyResultTimedOut,
            IReadOnlyCollection<string> timedOutRoles,
            bool pairTimedOut,
            bool tooManyMissingResults,
            bool repeatedRunStatusErrors,
            string issueDetails,
            bool sideLeftMultiShotActive,
            TimeSpan resultTimeout)
        {
            var detailParts = new List<string>();

            if (anyResultTimedOut)
                detailParts.Add($"No VisionPro results received within timeout ({resultTimeout.TotalSeconds:0.#} s).");
            if (timedOutRoles != null && timedOutRoles.Count > 0)
            {
                detailParts.Add(
                    $"Camera heartbeat missing: {string.Join(", ", timedOutRoles.Select(role => role.ToUpperInvariant()))}.");
            }
            if (pairTimedOut)
                detailParts.Add("Inspection pair processing stalled.");
            if (tooManyMissingResults)
                detailParts.Add("UserResult queue repeatedly returned empty.");
            if (repeatedRunStatusErrors)
                detailParts.Add("Repeated VisionPro RunStatus errors detected.");
            if (sideLeftMultiShotActive)
                detailParts.Add("MultiShot watchdog grace active.");
            if (!string.IsNullOrWhiteSpace(issueDetails))
                detailParts.Add(issueDetails);

            return string.Join(" ", detailParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        /// <summary>
        /// Executes the automatic VisionPro recovery path in single-flight mode.
        /// The routine is deliberately serialized because multiple overlapping
        /// stop/start cycles are a common cause of frozen industrial HMIs.
        /// </summary>
        private async Task AttemptVisionAutoRecoveryAsync(string reason)
        {
            if (ServiceLocator.MachineRuntimeService.IsContinuousRunCommandInProgress)
            {
                return;
            }

            // Operator has already been notified via modal dialog — do not show it again.
            // The top-bar hold-reason indicator remains visible as the ongoing status.
            if (_visionManualInterventionRequired)
            {
                logger.Debug("VISIONPRO_RECOVERY_SUPPRESSED|reason=manual_intervention_required");
                return;
            }

            if (!await _visionRecoverySemaphore.WaitAsync(0))
            {
                return;
            }

            try
            {
                BeginVisionStateTransition();
                _visionRecoveryAttemptCount++;

                lock (_visionHealthLock)
                {
                    _lastVisionRecoveryAttemptAtUtc = DateTime.UtcNow;
                }

                logger.Warn($"VisionPro appears stalled. Starting automatic recovery. {reason}");
                _applicationEventLogger.LogOperationalEvent(
                    LogLevel.Warn,
                    "VISIONPRO_STALL_DETECTED",
                    "VisionPro",
                    "VisionPro appears stalled while continuous run is active",
                    nameof(AttemptVisionAutoRecoveryAsync),
                    reason);

                if (_continuousRunCts != null)
                {
                    _continuousRunCts.Cancel();
                    _continuousRunCts.Dispose();
                }

                _continuousRunCts = new CancellationTokenSource();
                App.ViewModelCache.DigitalIOVM?.HandleVisionProRecoveryStarted(reason);
                MarkMultiShotCameraPhasesForRealignment("VisionPro automatic recovery: " + reason);
                ClearPendingVisionResults("automatic VisionPro recovery started", clearCachedArtifacts: false, forceLog: true);

                bool recovered = await ServiceLocator.MachineRuntimeService.RecoverContinuousRunAsync(
                    _continuousRunCts.Token,
                    nameof(AttemptVisionAutoRecoveryAsync),
                    reason);

                ServiceLocator.MachineRuntimeService.SyncStateFromManager();
                _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;

                lock (_visionHealthLock)
                {
                    var now = DateTime.UtcNow;
                    _lastVisionResultAtUtc = now;
                    _lastTopResultAtUtc = now;
                    _lastSideResultAtUtc = now;
                    _lastFrontResultAtUtc = now;
                    _lastVisionResultByRole.Clear();
                    foreach (string role in DataManage.CameraInspectionMatrix.SupportedRoles)
                    {
                        _lastVisionResultByRole[role] = now;
                    }
                    _lastProcessedPairAtUtc = now;
                    _pendingPairSinceUtc = DateTime.MinValue;
                    _consecutiveMissingUserResults = 0;
                    _consecutiveRunStatusErrors = 0;
                    _lastVisionIssueDetails = recovered ? null : reason;
                    _lastContinuousRunStartedAtUtc = now;
                    _hasReceivedVisionHeartbeatSinceRunStart = false;
                }

                if (recovered)
                {
                    _visionRecoveryAttemptCount = 0;
                    _visionManualInterventionRequired = false;
                    logger.Info("Automatic VisionPro recovery completed successfully.");
                }
                else
                {
                    logger.Error("Automatic VisionPro recovery completed but VisionPro is still not running continuously.");

                    // Show the modal popup exactly once (when the attempt counter reaches 3).
                    // After that, _visionManualInterventionRequired prevents further retries and
                    // further popups — the top-bar hold-reason indicator serves as ongoing status.
                    if (_visionRecoveryAttemptCount >= 3 && !_visionManualInterventionRequired)
                    {
                        _visionManualInterventionRequired = true;
                        int attempts = _visionRecoveryAttemptCount;
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ServiceLocator.DialogService.ShowWarning(
                                string.Format(ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_VisionProRecoveryFailedMessage", "VisionPro did not respond after {0} automatic recovery attempts.\nManual intervention required: check cameras and restart the vision system."), attempts),
                                ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_VisionProRecoveryFailedTitle", "VisionPro Recovery Failed"));
                        }), System.Windows.Threading.DispatcherPriority.Normal);
                    }
                }

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (DataContext is MainViewModel mainVm)
                    {
                        mainVm.UpdateControlButtonsState();
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Automatic VisionPro recovery failed");
                _applicationEventLogger.LogException(
                    "VISIONPRO_AUTO_RECOVERY_EXCEPTION",
                    ex,
                    nameof(AttemptVisionAutoRecoveryAsync),
                    "Errore durante il recupero automatico di VisionPro");
            }
            finally
            {
                EndVisionStateTransition();
                _visionRecoverySemaphore.Release();
            }
        }
        public async Task ResetAllCountersAsync()
        {
            try
            {
                var dlgResetCounters = new SystemNotificationWindow("Conferma reset",
                    "Vuoi resettare tutti i contatori?\nQuesta operazione non puÃ² essere annullata.",
                    NotificationSeverity.Warning, true);
                dlgResetCounters.ShowDialog();
                if (dlgResetCounters.Confirmed)
                {
                    await CounterManager.ResetAllCountersAsync();
                    await (ServiceLocator.AuditLogService?.LogAsync(
                        "COUNTER_RESET_ALL", UserSession.CurrentUser, "source=MainWindow") ?? Task.CompletedTask);

                    // Aggiorna UI
                    if (Views.UserControls.StatisticsView.Instance != null)
                    {
                        Views.UserControls.StatisticsView.Instance.UpdateCountersFromManager();
                    }

                    new SystemNotificationWindow("Successo", "Contatori resettati con successo!", NotificationSeverity.Info).ShowDialog();
                    logger.Info("Contatori resettati");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Errore nel reset dei contatori: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nel reset dei contatori: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        public async Task InitializeComponentConfigAsync()
        {
            bool lockAcquired = false;

            try
            {
                await _operationLock.WaitAsync(_cts.Token);
                lockAcquired = true;

                await configManager.EnsureLoadedAsync();

                await InitializeVisionSystem();

                language = configManager.Config.Configuration.Language;

                ServerMessagePersonalize.LoadMessages();
                logger.Info("Configurazione componenti completata.");
            }
            catch (OperationCanceledException)
            {
                logger.Warn("Inizializzazione configurazione annullata.");
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "Errore durante l'inizializzazione della configurazione.");
            }
            finally
            {
                if (lockAcquired)
                    _operationLock.Release();
            }

        }
        public async Task InitializeUIComponentsAsync()
        {
            try
            {
                _statusTimer.Elapsed += UpdateDateTimeDisplay;
                _statusTimer.Start();
                await runVisionPro();

                if (DataContext is MainViewModel mainVm)
                {
                    mainVm.UpdateControlButtonsState();
                    mainVm.MarkSystemAsReady();
                }


            }
            catch (OperationCanceledException)
            {
                logger.Warn("Inizializzazione UI annullata.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Errore durante l'inizializzazione dei componenti UI.");
                throw;
            }
           

        }
       
        public async Task InitializeVisionSystem()
        {

            try
            {
                _applicationEventLogger.LogLifecycle("VISION_SYSTEM_INITIALIZING", "Vision system initialization started", nameof(InitializeVisionSystem));
                // Risolve VPP e XML dalla configurazione macchina senza introdurre path fissi.
                string vppPath = System.IO.Path.Combine(
                  configManager.Config.Configuration.Recipe_Folder,
                  configManager.Config.Configuration.LastRecipe);
                recipePath = System.IO.Path.ChangeExtension(vppPath, ".xml");
                ConfigRecipeParam = new AsyncRecipeParam(recipePath);
                await ConfigRecipeParam.EnsureLoadedAsync();

                // DigitalIOViewModel is preloaded before MainWindow and can therefore still
                // expose machine defaults here. Apply the active recipe before validating
                // VisionPro MultiShot so recipe-level Disabled/offset values are authoritative
                // from the first startup, not only after a later recipe change.
                if (App.ViewModelCache.DigitalIOVM != null)
                {
                    await App.ViewModelCache.DigitalIOVM.ApplyActiveRecipeRuntimeAdjustmentsAsync(
                        ConfigRecipeParam.Config,
                        nameof(InitializeVisionSystem));
                }

                RefreshMultiShotConfiguredState();
                await InitializeCognexManagerWithGigERecoveryAsync(vppPath);
                ApplyVisionProStitchingParametersForLeftJobs(nameof(InitializeVisionSystem));
                RefreshRuntimeInspectionFeatures();
                await ActivateCameraIoFromActiveVppAsync(nameof(InitializeVisionSystem));

                await Dispatcher.InvokeAsync(() =>
                {
                    App.ViewModelCache.RecipeManagerVM?.RefreshCameraConfigurationFromActiveVpp();
                    CameraContainer.Instance?.RefreshFromRuntimeConfiguration();
                    TopCameraView.Instance?.ApplyRuntimeRoleLabel(_topJob != null ? ResolveCameraRole(GetJobIdForJob(_topJob)) : "top");
                    SideCameraView.Instance?.ApplyRuntimeRoleLabel(ResolveSideCameraDisplayRole());
                }, System.Windows.Threading.DispatcherPriority.Background);

                // Gli oggetti dipendenti dalla ricetta vengono costruiti fuori dal dispatcher UI.
                await Task.Run(() =>
                {
                    counterUpdater = new CounterUpdater(ConfigRecipeParam);
                    _inspectionProcessor = new InspectionProcessor(
                       new ToolBlockValidator(ConfigRecipeParam), logger);
                });
                double topTriggerDelay = ConfigRecipeParam.Config?.cameraSetting?.TopCameraTriggerDelay ?? 700000;
                double sideTriggerDelay = ConfigRecipeParam.Config?.cameraSetting?.SideCameraTriggerDelay ?? 700000;
                double frontTriggerDelay = ConfigRecipeParam.Config?.cameraSetting?.FrontCameraTriggerDelay ?? 700000;

                await ApplyRecipeTriggerDelaysAsync(
                    topTriggerDelay,
                    ConfigRecipeParam.Config?.cameraSetting?.ResolveLeftCameraTriggerDelay() ?? sideTriggerDelay,
                    frontTriggerDelay,
                    ConfigRecipeParam.Config?.cameraSetting?.RightCameraTriggerDelay ?? 0,
                    ConfigRecipeParam.Config?.cameraSetting?.BottomCameraTriggerDelay ?? 0,
                    nameof(InitializeVisionSystem),
                    ConfigRecipeParam.Config?.cameraSetting?.RearCameraTriggerDelay ?? 0);
                logger.Info("Vision system initialized");
                _applicationEventLogger.LogLifecycle("VISION_SYSTEM_INITIALIZED", "Vision system initialized", nameof(InitializeVisionSystem));
            }
            catch (Exception ex)
            {
                logger.Error(ex.StackTrace, "Vision system initialization failed");
                _applicationEventLogger.LogException("VISION_SYSTEM_INIT_FAILED", ex, nameof(InitializeVisionSystem), "Vision system initialization failed");
                throw;
            }
        }

        private async Task InitializeCognexManagerWithGigERecoveryAsync(string vppPath)
        {
            CreateAndInitializeCognexManager(vppPath);

            // Right after deserialization a GigE job's AcqFifo can still be null for a
            // moment before VisionPro attaches the concrete CogAcqFifoGigE instance.
            // IsGigEAcquisitionJob (and therefore GetInvalidGigEAcquisitionJobs) only
            // recognizes a job once its FIFO type resolves, so checking immediately can
            // undercount Rear as "not a GigE job" and skip the whole recovery path below -
            // the job then reaches TryStartJobContinuous with AcqFifoState still Invalid
            // and no bounded reload ever ran. Give the FIFO the same settle window used
            // later in this method before the first classification.
            await Task.Delay(GigEStartupBindingSettleMs);
            IReadOnlyList<string> invalidJobs = _cognexManager.GetInvalidGigEAcquisitionJobs();
            if (invalidJobs.Count == 0)
            {
                return;
            }

            int expectedGigEJobs = _cognexManager.GetGigEAcquisitionJobCount();
            logger.Warn(
                $"VISIONPRO_GIGE_DISCOVERY_WAIT|expected={expectedGigEJobs}|invalid={string.Join(", ", invalidJobs)}");

            bool inventoryReady = await WaitForGigEFrameGrabberInventoryAsync(expectedGigEJobs);
            if (!inventoryReady)
            {
                logger.Error(
                    $"VISIONPRO_GIGE_DISCOVERY_TIMEOUT|expected={expectedGigEJobs}|invalid={string.Join(", ", invalidJobs)}");
                return;
            }

            // Device enumeration can complete before VisionPro can bind every
            // deserialized FIFO to its specific grabber. Give the first manager
            // one short settle window before deciding that a full VPP reload is
            // required.
            await Task.Delay(GigEStartupBindingSettleMs);
            invalidJobs = _cognexManager.GetInvalidGigEAcquisitionJobs();
            if (invalidJobs.Count == 0)
            {
                logger.Info(
                    $"VISIONPRO_GIGE_BINDING_READY|source=initial-manager|jobs={_cognexManager.JobCount}");
                return;
            }

            logger.Warn(
                $"VISIONPRO_GIGE_BINDING_PENDING_AFTER_DISCOVERY|invalid={string.Join(", ", invalidJobs)}");

            Exception lastReloadException = null;
            for (int attempt = 1; attempt <= GigEStartupVppReloadMaxAttempts; attempt++)
            {
                logger.Info(
                    $"VISIONPRO_VPP_RELOAD_AFTER_GIGE_DISCOVERY|path={vppPath}|attempt={attempt}/{GigEStartupVppReloadMaxAttempts}|reason=invalid-acquisition-after-device-discovery");

                await ReleaseCognexManagerForGigEReloadAsync(attempt);
                try
                {
                    CreateAndInitializeCognexManager(vppPath);
                    lastReloadException = null;
                }
                catch (Exception ex)
                {
                    lastReloadException = ex;
                    logger.Warn(
                        $"VISIONPRO_VPP_RELOAD_RESULT|status=load-failed|attempt={attempt}|error={ex.Message}");
                    continue;
                }

                // A freshly deserialized FGGigE FIFO may report Invalid for a
                // short period even though its FrameGrabber is now present.
                await Task.Delay(GigEStartupBindingSettleMs);

                IReadOnlyList<string> remainingInvalidJobs = _cognexManager.GetInvalidGigEAcquisitionJobs();
                if (remainingInvalidJobs.Count == 0)
                {
                    logger.Info(
                        $"VISIONPRO_VPP_RELOAD_RESULT|status=ready|attempt={attempt}|jobs={_cognexManager.JobCount}");
                    return;
                }

                logger.Warn(
                    $"VISIONPRO_VPP_RELOAD_RESULT|status=invalid|attempt={attempt}|invalid={string.Join(", ", remainingInvalidJobs)}");
            }

            if (_cognexManager == null)
            {
                string loadError = lastReloadException?.Message ?? "VisionPro manager was not created.";
                logger.Error(
                    $"VISIONPRO_VPP_RELOAD_EXHAUSTED|attempts={GigEStartupVppReloadMaxAttempts}|error={loadError}");
                throw new InvalidOperationException(
                    $"Unable to load VisionPro VPP after {GigEStartupVppReloadMaxAttempts} GigE recovery attempts.",
                    lastReloadException);
            }

            IReadOnlyList<string> exhaustedInvalidJobs = _cognexManager.GetInvalidGigEAcquisitionJobs();
            logger.Error(
                $"VISIONPRO_VPP_RELOAD_EXHAUSTED|attempts={GigEStartupVppReloadMaxAttempts}|invalid={string.Join(", ", exhaustedInvalidJobs)}");
        }

        private async Task ReleaseCognexManagerForGigEReloadAsync(int attempt)
        {
            ICognexJobManager managerToRelease = _cognexManager;
            _cognexManager = null;
            ServiceLocator.MachineRuntimeService.AttachCognexManager(null);

            try
            {
                managerToRelease?.Dispose();
            }
            catch (Exception ex)
            {
                logger.Warn(
                    $"VISIONPRO_GIGE_MANAGER_RELEASE_FAILED|attempt={attempt}|error={ex.Message}");
            }
            finally
            {
                // The static job/tool references otherwise keep the old FIFO
                // graph reachable while the next VPP is being deserialized.
                ResetVisionRuntimeStateForRecipeReload();
                JobRoleMapping.Clear();
            }

            logger.Info(
                $"VISIONPRO_GIGE_MANAGER_RELEASED|attempt={attempt}|settleMs={GigEStartupManagerReleaseDelayMs}");
            await Task.Delay(GigEStartupManagerReleaseDelayMs);
        }

        private static void CreateAndInitializeCognexManager(string vppPath)
        {
            var manager = new CognexJobManager();
            _cognexManager = manager;
            ServiceLocator.MachineRuntimeService.AttachCognexManager(manager);

            try
            {
                manager.Initialize(vppPath);
                if (manager.JobCount <= 0)
                {
                    throw new InvalidOperationException($"No VisionPro jobs were loaded from '{vppPath}'.");
                }
            }
            catch
            {
                ServiceLocator.MachineRuntimeService.AttachCognexManager(null);
                _cognexManager = null;
                manager.Dispose();
                throw;
            }
        }

        private async Task<bool> WaitForGigEFrameGrabberInventoryAsync(int expectedCount)
        {
            if (expectedCount <= 0)
            {
                return true;
            }

            DateTime deadlineUtc = DateTime.UtcNow.Add(GigEStartupDiscoveryTimeout);
            int stableSamples = 0;
            int previousCount = -1;
            string previousDescription = null;

            while (DateTime.UtcNow < deadlineUtc)
            {
                int availableCount = IgigaCameraAccess.GetFrameGrabberInventory(out string description);
                bool inventoryChanged = availableCount != previousCount ||
                    !string.Equals(description, previousDescription, StringComparison.Ordinal);
                if (inventoryChanged)
                {
                    logger.Info(
                        $"VISIONPRO_GIGE_INVENTORY|available={availableCount}|expected={expectedCount}|devices={description}");
                    previousCount = availableCount;
                    previousDescription = description;
                }

                stableSamples = availableCount >= expectedCount
                    ? (inventoryChanged ? 1 : stableSamples + 1)
                    : 0;

                if (stableSamples >= GigEStartupStableSamples)
                {
                    logger.Info(
                        $"VISIONPRO_GIGE_DISCOVERY_READY|available={availableCount}|expected={expectedCount}|devices={description}");
                    return true;
                }

                await Task.Delay(GigEStartupDiscoveryPollMs);
            }

            return false;
        }
        #region VisionPro Event Handlers
        private static string[] _updateDisplayStrings = new string[] { "ShowLastRunRecordForUserQueue", "LastRun" };
        static public ICogRecord TraverseSubRecords(ICogRecord r, string[] subs)
        {
            // Utility function to walk down to a specific subrecord
            if (r == null)
                return r;

            foreach (string s in subs)
            {
                if (r.SubRecords.ContainsKey(s))
                    r = r.SubRecords[s];
                else
                    return null;
            }

            return r;
        }

        private async void MyJobManager_UserResultAvailableAsync(object sender, CogJobManagerActionEventArgs e)
        {
            try
            {
                logger.Debug("VISION_EVENT|UserResultAvailable fired");
                if (IsJobEditorLivePreviewActive)
                {
                    logger.Debug("VISION_EVENT|UserResultAvailable ignored by production flow during Job Editor Live Preview.");
                    return;
                }

                ICogRecord result = _cognexManager.GetJobManager().UserResult();
                if (result == null)
                {
                    if (!ShouldAcceptVisionResults())
                    {
                        return;
                    }

                    logger.Debug("VISION_EVENT|UserResult=null — RegisterMissingUserResult");
                    RegisterMissingUserResult();
                    return;
                }

                if (!ShouldAcceptVisionResults())
                {
                    ClearPendingVisionResults("machine runtime is stopped or transitioning", clearCachedArtifacts: false);
                    return;
                }

                string jobName = (string)result.SubRecords["JobName"].Content;
                int? resolvedJobId = null;
                foreach (var job in JobMapping)
                {
                    if (string.Equals(job.Value, jobName, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedJobId = job.Key;
                        break;
                    }
                }

                if (!resolvedJobId.HasValue)
                {
                    logger.Warn($"Job '{jobName}' not found in JobMapping.");
                    return;
                }

                int jobId = resolvedJobId.Value;
                string cameraRole = ResolveCameraRole(jobId);
                if (string.IsNullOrWhiteSpace(cameraRole))
                {
                    logger.Warn($"Unable to resolve camera role for job {jobName} (ID:{jobId}).");
                    return;
                }

                if (ServiceLocator.InspectionConfigService?.IsCameraRoleEnabled(cameraRole) == false)
                {
                    logger.Debug(
                        $"VISION_RESULT_IGNORED_RECIPE_CAMERA_DISABLED|job={jobName}|role={cameraRole}");
                    return;
                }

                RegisterVisionHeartbeat(jobName);

                // Ottieni il toolblock corrispondente
                var toolBlock = GetToolBlockForJob(jobId, cameraRole, jobName);
                if (toolBlock == null)
                {
                    LogToolBlockResolutionWarning(
                        $"missing:{jobId}:{cameraRole}",
                        $"ToolBlock not found for job {jobName} (ID:{jobId}, role:{cameraRole})");
                    return;
                }

                CogJob sourceJob = _cognexManager?.GetJob(jobId);
                string userResultTag = result.SubRecords.ContainsKey("UserResultTag")
                    ? result.SubRecords["UserResultTag"]?.Content?.ToString()
                    : null;
                ICogRunStatus capturedRunStatus = sourceJob?.VisionTool?.RunStatus;
                // Reject is a valid completed inspection and must remain NOK (0).
                // Only an execution error means that the piece cannot be classified.
                bool hasProcessingError = capturedRunStatus?.Result == CogToolResultConstants.Error;
                var cameraResult = new CameraResult
                {
                    ToolBlock = toolBlock,
                    CameraRole = cameraRole,
                    JobName = jobName,
                    Job = sourceJob,
                    IsSyntheticAcquisition = IsSyntheticAcquisitionJob(sourceJob),
                    ResultSequence = Interlocked.Increment(ref _visionResultSequence),
                    UserResultTag = userResultTag,
                    HasProcessingError = hasProcessingError,
                    RunStatusResult = capturedRunStatus?.Result.ToString(),
                    RunStatusMessage = capturedRunStatus?.Message,
                    RunTotalTimeMs = capturedRunStatus != null ? (double?)capturedRunStatus.TotalTime : null,
                    RunProcessingTimeMs = capturedRunStatus != null ? (double?)capturedRunStatus.ProcessingTime : null
                };

                if (hasProcessingError)
                {
                    logger.Error(
                        $"VISION_RESULT_UNCLASSIFIED_CANDIDATE|sequence={cameraResult.ResultSequence}" +
                        $"|job={jobName}|role={cameraRole}|runStatus={cameraResult.RunStatusResult}" +
                        $"|message={cameraResult.RunStatusMessage ?? "VisionPro processing error"}");
                }

                logger.Debug($"VISION_EVENT|job={jobName} role={cameraRole} — evaluating multishot filter");
                if (ShouldIgnoreIntermediateSideLeftMultiShotResult(cameraResult, jobId))
                {
                    return;
                }

                if (!cameraResult.IsSyntheticAcquisition)
                {
                    CameraTriggerClaimStatus claimStatus =
                        CameraTriggerCorrelationTracker.TryClaim(
                            cameraRole, cameraResult.UserResultTag, out CameraTriggerTicket triggerTicket);
                    if (claimStatus == CameraTriggerClaimStatus.Matched)
                    {
                        cameraResult.TriggeredProductId = triggerTicket.ProductId;
                        cameraResult.TriggerPointCode = triggerTicket.PointCode;
                        cameraResult.TriggerIssuedAtUtc = triggerTicket.IssuedAtUtc;
                        double triggerToResultMs = (DateTime.UtcNow - triggerTicket.IssuedAtUtc).TotalMilliseconds;
                        logger.Debug(
                            $"VISION_RESULT_CORRELATED|sequence={cameraResult.ResultSequence}" +
                            $"|role={cameraRole}|tag={cameraResult.UserResultTag ?? "-"}" +
                            $"|product={triggerTicket.ProductId}|point={triggerTicket.PointCode}" +
                            $"|trigger_to_result_ms={triggerToResultMs:F1}");
                        CameraDiagnostics.ResultClaimed(
                            cameraRole,
                            cameraResult.ResultSequence,
                            jobName,
                            triggerTicket.ProductId,
                            triggerTicket.PointCode,
                            triggerToResultMs,
                            cameraResult.RunTotalTimeMs,
                            cameraResult.RunProcessingTimeMs,
                            cameraResult.HasProcessingError,
                            cameraResult.UserResultTag);
                    }
                    else if (claimStatus == CameraTriggerClaimStatus.DuplicateUserResult)
                    {
                        logger.Warn(
                            $"VISION_RESULT_DUPLICATE_TAG_DROPPED|sequence={cameraResult.ResultSequence}" +
                            $"|job={jobName}|role={cameraRole}|tag={cameraResult.UserResultTag}");
                        CameraDiagnostics.ResultDuplicateTag(
                            cameraRole, cameraResult.ResultSequence, jobName, cameraResult.UserResultTag);
                        return;
                    }
                    else if (claimStatus == CameraTriggerClaimStatus.Unsolicited)
                    {
                        logger.Warn(
                            $"VISION_RESULT_UNSOLICITED_DROPPED|sequence={cameraResult.ResultSequence}" +
                            $"|job={jobName}|role={cameraRole}|tag={cameraResult.UserResultTag ?? "-"}" +
                            "|reason=no pending HMI trigger ticket; probable duplicate or delayed result");
                        CameraDiagnostics.ResultUnsolicited(
                            cameraRole, cameraResult.ResultSequence, jobName, cameraResult.UserResultTag);
                        return;
                    }
                }

                // Capture scalar outputs before traversing the graphical record. Record
                // construction can be relatively expensive, while VisionPro may already
                // begin the next run and reuse the live ToolBlock.
                try
                {
                    cameraResult.OutputSnapshot = VisionToolBlockOutputSnapshot.Capture(toolBlock);
                    logger.Debug(
                        $"VISION_RESULT_SNAPSHOT|sequence={cameraResult.ResultSequence}" +
                        $"|job={jobName}|role={cameraRole}" +
                        $"|outputs={cameraResult.OutputSnapshot.Outputs.Count}");
                }
                catch (Exception snapshotException)
                {
                    cameraResult.HasProcessingError = true;
                    cameraResult.RunStatusResult = CogToolResultConstants.Error.ToString();
                    cameraResult.RunStatusMessage =
                        $"VisionPro output snapshot failed: {snapshotException.GetBaseException().Message}";
                    logger.Error(
                        snapshotException,
                        $"VISION_RESULT_SNAPSHOT_FAILED|sequence={cameraResult.ResultSequence}" +
                        $"|job={jobName}|role={cameraRole}");
                }

                ICogRecord addrec = TraverseSubRecords(result, _updateDisplayStrings);
                if (addrec == null)
                {
                    cameraResult.HasProcessingError = true;
                    cameraResult.RunStatusResult = CogToolResultConstants.Error.ToString();
                    cameraResult.RunStatusMessage = string.IsNullOrWhiteSpace(cameraResult.RunStatusMessage)
                        ? "VisionPro display record could not be extracted."
                        : $"{cameraResult.RunStatusMessage} VisionPro display record could not be extracted.";
                    // Preserve the UserResult record as a diagnostic fallback. This lets
                    // the unclassified piece reach counters, DB and image saving instead
                    // of being converted into an unrelated companion timeout.
                    addrec = result;
                    logger.Error(
                        $"VISION_RESULT_RECORD_FALLBACK|sequence={cameraResult.ResultSequence}" +
                        $"|job={jobName}|role={cameraRole}|outcome=3");
                }

                cameraResult.Record = addrec;
                cameraResult.EnqueuedAtUtc = DateTime.UtcNow;
                switch (cameraRole)
                {
                    case "top":
                    case "top3d":
                        _topQueue.Enqueue(cameraResult);
                        logger.Info($"{cameraRole.ToUpperInvariant()} result queued. Sequence: {cameraResult.ResultSequence}. Queue size: {_topQueue.Count}");
                        break;
                    case "side":
                    case "left":
                        _leftQueue.Enqueue(cameraResult);
                        logger.Info($"LEFT result queued. Sequence: {cameraResult.ResultSequence}. Queue size: {_leftQueue.Count}");
                        break;
                    case "front":
                        _frontQueue.Enqueue(cameraResult);
                        logger.Info($"Front result queued. Sequence: {cameraResult.ResultSequence}. Queue size: {_frontQueue.Count}");
                        break;
                    case "rear":
                        _rearQueue.Enqueue(cameraResult);
                        logger.Info($"REAR result queued. Sequence: {cameraResult.ResultSequence}. Queue size: {_rearQueue.Count}");
                        break;
                    case "right":
                        _rightQueue.Enqueue(cameraResult);
                        logger.Info($"RIGHT result queued. Sequence: {cameraResult.ResultSequence}. Queue size: {_rightQueue.Count}");
                        break;
                    case "bottom":
                        _bottomQueue.Enqueue(cameraResult);
                        logger.Info($"Bottom result queued. Sequence: {cameraResult.ResultSequence}. Queue size: {_bottomQueue.Count}");
                        break;
                    default:
                        cameraResult.ReleaseOutputSnapshot();
                        logger.Warn($"Unhandled camera role '{cameraRole}' for job ID: {jobId}");
                        return;
                }

                // Tenta di processare una coppia
                await Task.Run(() => ProcessQueuedPairAsync());
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error processing UserResultAvailable event");
            }
            finally
            {
                _lastProcessingTime = DateTime.Now;
            }

        }
        private static bool IsSyntheticAcquisitionJob(CogJob job)
        {
            try
            {
                ICogAcqFifo fifo = job?.AcqFifo;
                string fifoType = fifo?.GetType().FullName ?? fifo?.GetType().Name ?? string.Empty;
                string grabberName = fifo?.FrameGrabber?.Name ?? string.Empty;

                return fifoType.IndexOf("Synthetic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       grabberName.IndexOf("Synthetic", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private CogToolBlock GetToolBlockForJob(int jobId, string cameraRole = null, string jobName = null)
        {
            try
            {
                var job = _cognexManager?.GetJob(jobId);
                if (job == null)
                {
                    return null;
                }

                if (job.VisionTool is CogToolBlock directToolBlock)
                {
                    return directToolBlock;
                }

                if (job.VisionTool is CogToolGroup group)
                {
                    if (group.Tools.Contains("Results") && group.Tools["Results"] is CogToolBlock resultsToolBlock)
                    {
                        return resultsToolBlock;
                    }

                    var fallbackToolBlock = ResolveFallbackToolBlockForRole(group, cameraRole);
                    if (fallbackToolBlock != null)
                    {
                        LogToolBlockResolutionWarning(
                            $"fallback:{jobId}:{cameraRole}:{fallbackToolBlock.Name}",
                            $"Results toolblock missing for job {jobName ?? job.Name} (ID:{jobId}, role:{cameraRole}). Using fallback toolblock '{fallbackToolBlock.Name}'.");
                        return fallbackToolBlock;
                    }

                    LogToolBlockResolutionWarning(
                        $"inventory:{jobId}:{cameraRole}",
                        $"Results toolblock missing for job {jobName ?? job.Name} (ID:{jobId}, role:{cameraRole}). Available tools: {string.Join(", ", group.Tools.OfType<ICogTool>().Select(tool => tool?.Name).Where(name => !string.IsNullOrWhiteSpace(name)))}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error getting toolblock for job {jobId}: {ex.Message}");
            }
            return null;
        }

        private bool ShouldIgnoreIntermediateSideLeftMultiShotResult(CameraResult cameraResult, int jobId)
        {
            if (cameraResult?.Job == null)
            {
                return false;
            }

            string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(cameraResult.CameraRole);
            var multiShotConfiguration = ResolveActiveRecipeMachineRuntimeConfiguration()?.MachineMultiShotTrigger;
            var multiShotProfile = multiShotConfiguration?.ResolveOptionsForRuntimeRole(normalizedRole);
            if (multiShotProfile?.Enabled != true)
            {
                return false;
            }

            var stitching = multiShotProfile.VisionProStitching;
            string toolBlockName = string.IsNullOrWhiteSpace(stitching?.ToolBlockName)
                ? "ImageStitching"
                : stitching.ToolBlockName.Trim();

            if (!TryReadStitchingReady(
                    cameraResult.Job,
                    toolBlockName,
                    out bool isReady,
                    out double? frameIndex,
                    out string status,
                    out string reason))
                {
                    LogToolBlockResolutionWarning(
                        $"multishot-stitching-ready:{jobId}:{toolBlockName}",
                        $"{ResolveMultiShotDisplayName(multiShotProfile)} MultiShot could not read {toolBlockName}.isReady for job {cameraResult.JobName}: {reason}. The result will be accepted to avoid blocking production.");
                    return false;
                }

                if (isReady)
                {
                    logger.Info($"{ResolveMultiShotDisplayName(multiShotProfile).ToUpperInvariant()} MultiShot final stitching result accepted. frameIndex={frameIndex?.ToString("0.###") ?? "-"}, status={status ?? "-"}.");
                    return false;
                }

            logger.Info($"{ResolveMultiShotDisplayName(multiShotProfile).ToUpperInvariant()} MultiShot intermediate VisionPro result ignored. Waiting for final stitched image. frameIndex={frameIndex?.ToString("0.###") ?? "-"}, status={status ?? "-"}.");
            return true;
        }

        private static string ResolveMultiShotDisplayName(MultiShotTriggerOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options?.DisplayName))
            {
                return options.DisplayName;
            }

            return string.IsNullOrWhiteSpace(options?.CameraRole) ? "Camera" : options.CameraRole;
        }

        private static bool TryReadStitchingReady(
            CogJob job,
            string toolBlockName,
            out bool isReady,
            out double? frameIndex,
            out string status,
            out string reason)
        {
            isReady = false;
            frameIndex = null;
            status = null;
            reason = null;

            var toolBlock = FindToolBlock(job, toolBlockName);
            if (toolBlock == null)
            {
                reason = $"ToolBlock '{toolBlockName}' not found";
                return false;
            }

            if (toolBlock.Outputs == null || !toolBlock.Outputs.Contains("isReady"))
            {
                reason = $"Output '{toolBlockName}.isReady' not found";
                return false;
            }

            object readyValue = toolBlock.Outputs["isReady"].Value;
            if (readyValue is bool readyBool)
            {
                isReady = readyBool;
            }
            else if (readyValue != null && bool.TryParse(readyValue.ToString(), out bool parsedReady))
            {
                isReady = parsedReady;
            }
            else
            {
                reason = $"Output '{toolBlockName}.isReady' is not boolean";
                return false;
            }

            if (toolBlock.Outputs.Contains("frameIndex"))
            {
                object frameValue = toolBlock.Outputs["frameIndex"].Value;
                if (frameValue is IConvertible)
                {
                    try
                    {
                        frameIndex = Convert.ToDouble(frameValue, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        frameIndex = null;
                    }
                }
            }

            if (toolBlock.Outputs.Contains("status"))
            {
                status = toolBlock.Outputs["status"].Value?.ToString();
            }

            return true;
        }

        private static CogToolBlock FindToolBlock(CogJob job, string toolBlockName)
        {
            string resolvedName = string.IsNullOrWhiteSpace(toolBlockName)
                ? "ImageStitching"
                : toolBlockName.Trim();

            return FindToolBlockRecursive(job?.VisionTool, resolvedName, 0);
        }

        private static CogToolBlock FindToolBlockRecursive(object toolOrGroup, string toolBlockName, int depth)
        {
            if (toolOrGroup == null || depth > 16)
            {
                return null;
            }

            if (toolOrGroup is CogToolBlock toolBlock &&
                string.Equals(toolBlock.Name, toolBlockName, StringComparison.OrdinalIgnoreCase))
            {
                return toolBlock;
            }

            var tools = ResolveNestedTools(toolOrGroup);
            if (tools == null)
            {
                return null;
            }

            foreach (var child in tools)
            {
                var nestedMatch = FindToolBlockRecursive(child, toolBlockName, depth + 1);
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }

            return null;
        }

        private static System.Collections.IEnumerable ResolveNestedTools(object toolOrGroup)
        {
            try
            {
                return toolOrGroup?.GetType().GetProperty("Tools")?.GetValue(toolOrGroup, null) as System.Collections.IEnumerable;
            }
            catch
            {
                return null;
            }
        }

        private CogToolBlock ResolveFallbackToolBlockForRole(CogToolGroup group, string cameraRole)
        {
            if (group == null)
            {
                return null;
            }

            var toolBlocks = group.Tools
                .OfType<CogToolBlock>()
                .ToList();

            if (toolBlocks.Count == 0)
            {
                return null;
            }

            string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(cameraRole);

            if (string.Equals(normalizedRole, "top3d", StringComparison.OrdinalIgnoreCase))
            {
                string heightOutput = ResolveConfiguredOutputName(
                    configManager?.Config?.Configuration?.ThreeDHeightOutput,
                    "ThreeDHeight");
                string widthOutput = ResolveConfiguredOutputName(
                    configManager?.Config?.Configuration?.ThreeDWidthOutput,
                    "ThreeDWidth");
                string lengthOutput = ResolveConfiguredOutputName(
                    configManager?.Config?.Configuration?.ThreeDLengthOutput,
                    "ThreeDLength");
                string rerenderOutput = ResolveConfiguredOutputName(
                    configManager?.Config?.Configuration?.Top3DRerenderResultOutput,
                    "Top3DRerenderResult");
                string pointCloudOutput = ResolveConfiguredOutputName(
                    configManager?.Config?.Configuration?.Top3DPointCloudOutput,
                    "Top3DPointCloud");

                var bestTop3DMatch = toolBlocks
                    .Select(toolBlock => new
                    {
                        ToolBlock = toolBlock,
                        Score =
                            CountMatchingOutputs(toolBlock, heightOutput, widthOutput, lengthOutput) * 2 +
                            CountMatchingOutputs(toolBlock, rerenderOutput, pointCloudOutput)
                    })
                    .OrderByDescending(candidate => candidate.Score)
                    .FirstOrDefault();

                if (bestTop3DMatch != null && bestTop3DMatch.Score > 0)
                {
                    return bestTop3DMatch.ToolBlock;
                }
            }

            if (toolBlocks.Count == 1)
            {
                return toolBlocks[0];
            }

            return toolBlocks.FirstOrDefault(tool =>
                tool != null &&
                !string.IsNullOrWhiteSpace(tool.Name) &&
                tool.Name.IndexOf("result", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static int CountMatchingOutputs(CogToolBlock toolBlock, params string[] outputNames)
        {
            if (toolBlock?.Outputs == null || outputNames == null || outputNames.Length == 0)
            {
                return 0;
            }

            int matchCount = 0;
            foreach (string outputName in outputNames)
            {
                if (!string.IsNullOrWhiteSpace(outputName) && toolBlock.Outputs.Contains(outputName))
                {
                    matchCount++;
                }
            }

            return matchCount;
        }

        private static string ResolveConfiguredOutputName(string configuredName, string fallbackName)
        {
            return string.IsNullOrWhiteSpace(configuredName)
                ? fallbackName
                : configuredName;
        }

        private void LogToolBlockResolutionWarning(string key, string message)
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime lastLoggedUtc = _toolBlockResolutionWarningTimes.GetOrAdd(key, DateTime.MinValue);
            if ((nowUtc - lastLoggedUtc) < TimeSpan.FromSeconds(10))
            {
                return;
            }

            _toolBlockResolutionWarningTimes[key] = nowUtc;
            logger.Warn(message);
        }
      
        private async Task ProcessInspectionGroupAsync(
            CameraResult topResult,
            IReadOnlyDictionary<string, CameraResult> companionResults,
            IEnumerable<string> missingCameraRoles)
        {
            if (_cts.IsCancellationRequested || !ShouldAcceptVisionResults())
            {
                ClearPendingVisionResults("inspection group arrived while machine runtime is not active", clearCachedArtifacts: false);
                logger.Info("Processing cancelled");
                return;
            }

            try
            {
                _produzioneRecord = CreateBaseInspectionRecord();
                var companions = (companionResults ?? new Dictionary<string, CameraResult>())
                    .Where(item => item.Value != null)
                    .ToDictionary(
                        item => CameraConfigurationHelper.NormalizePhysicalCameraRole(item.Key),
                        item => item.Value,
                        StringComparer.OrdinalIgnoreCase);

                CameraResult sideResult = FindCompanionResult(companions, "side", "left");
                CameraResult frontResult = FindCompanionResult(companions, "front");
                CameraResult rightResult = FindCompanionResult(companions, "right");
                CameraResult rearResult = FindCompanionResult(companions, "rear");
                CameraResult bottomResult = FindCompanionResult(companions, "bottom");
                var processingErrorResults = new[] { topResult, sideResult, frontResult, rightResult, rearResult, bottomResult }
                    .Where(result => result?.HasProcessingError == true)
                    .ToList();
                bool isSidePair = sideResult != null;
                bool isFrontPair = frontResult != null;
                bool isTop3DInspection = string.Equals(topResult?.CameraRole, "top3d", StringComparison.OrdinalIgnoreCase);
                var missingRoles = (missingCameraRoles ?? Enumerable.Empty<string>())
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Select(role => CameraConfigurationHelper.NormalizePhysicalCameraRole(role))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                long performanceStartTicks = Stopwatch.GetTimestamp();
                double displayMs = 0.0;
                double validationMs = 0.0;
                double countersMs = 0.0;
                double saveDecisionMs = 0.0;
                double advisoryMs = 0.0;
                double dbQueueMs = 0.0;

                CacheLatestInspectionArtifacts(
                    topResult,
                    sideResult,
                    frontResult,
                    rearResult,
                    bottomResult,
                    rightResult);

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    TopCameraView.Instance?.ApplyRuntimeRoleLabel(isTop3DInspection ? "top3d" : "top");
                    SideCameraView.Instance?.ApplyRuntimeRoleLabel(isTop3DInspection ? "top2d" : NormalizeSideDisplayRole(sideResult?.CameraRole));
                }), System.Windows.Threading.DispatcherPriority.Background);

                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeoutCts.CancelAfter(GetInspectionGroupTimeout());
                // Aggiorna i display delle telecamere

                long stageTicks = Stopwatch.GetTimestamp();
                long displayStartTicks = stageTicks;
                string sideDisplayRole = isTop3DInspection
                    ? "top2d"
                    : NormalizeSideDisplayRole(sideResult?.CameraRole);
                var displayTasks = new List<Task>
                {
                    ProcessToolResultsAsyncTop(
                        topResult.Record,
                        GetDisplayForRole(isTop3DInspection ? "top3d" : "top"),
                        GetLastRunViewForRole(isTop3DInspection ? "top3d" : "top"),
                        isTop3DInspection ? "top3d" : "top",
                        timeoutCts.Token,
                        topResult)
                };

                if (isTop3DInspection)
                {
                    displayTasks.Add(ProcessToolResultsAsyncTopSecondary(
                        topResult.Record,
                        GetDisplayForRole("top2d"),
                        GetLastRunViewForRole("top2d"),
                        timeoutCts.Token,
                        topResult));
                }
                else if (isSidePair)
                {
                    displayTasks.Add(ProcessToolResultsAsyncside(
                        sideResult.Record,
                        GetDisplayForRole(sideDisplayRole),
                        GetLastRunViewForRole(sideDisplayRole),
                        timeoutCts.Token,
                        sideDisplayRole,
                        sideResult));
                }

                if (isFrontPair)
                {
                    displayTasks.Add(ProcessToolResultsAsyncFront(
                        frontResult.Record,
                        FrontCameraView.Instance?.CogRecordsDisplay1,
                        GetLastRunViewForRole("front"),
                        timeoutCts.Token,
                        frontResult));
                }

                if (rearResult != null)
                {
                    displayTasks.Add(ProcessToolResultsAsyncRear(
                        rearResult.Record,
                        GetDisplayForRole(rearResult.JobName),
                        GetLastRunViewForRole(rearResult.JobName),
                        timeoutCts.Token,
                        rearResult));
                }
                if (rightResult != null)
                {
                    displayTasks.Add(ProcessToolResultsAsyncRear(
                        rightResult.Record,
                        GetDisplayForRole("right"),
                        GetLastRunViewForRole("right"),
                        timeoutCts.Token,
                        rightResult));
                }

                if (bottomResult != null)
                {
                    displayTasks.Add(ProcessToolResultsAsyncBottom(
                        bottomResult.Record,
                        BottomCameraView.Instance?.CogRecordsDisplay1,
                        GetLastRunViewForRole("bottom"),
                        timeoutCts.Token,
                        bottomResult));
                }

                Task displayCompletionTask = Task.WhenAll(displayTasks);

                if (!ShouldAcceptVisionResults())
                {
                    ClearPendingVisionResults("machine runtime stopped during display update", clearCachedArtifacts: false);
                    return;
                }

                CounterManager?.BeginInspectionCycle();

                // Esegui la validazione
                stageTicks = Stopwatch.GetTimestamp();
                InspectionResult inspectionResult;
                if (processingErrorResults.Count > 0)
                {
                    inspectionResult = CreateUnclassifiedInspectionResult(processingErrorResults);
                }
                else
                {
                    inspectionResult = await _inspectionProcessor.ProcessInspectionAsync(
                        topResult.OutputSnapshot,
                        sideResult?.OutputSnapshot,
                        frontResult?.OutputSnapshot,
                        rearResult?.OutputSnapshot,
                        bottomResult?.OutputSnapshot,
                        missingRoles,
                        isTop3DInspection,
                        timeoutCts.Token,
                        rightResult?.OutputSnapshot);
                }
                ApplyInspectionCameraRoles(
                    inspectionResult,
                    topResult,
                    sideResult,
                    frontResult,
                    rearResult,
                    bottomResult,
                    isTop3DInspection,
                    rightResult);
                validationMs = ElapsedMillisecondsSince(stageTicks);
          
                // Aggiorna contatori e gestisci allarmi (versione modificata che riceve i CameraResult)
                stageTicks = Stopwatch.GetTimestamp();
                await ProcessInspectionWithCountersAsync(
                    inspectionResult,
                    topResult,
                    sideResult,
                    frontResult);
                countersMs = ElapsedMillisecondsSince(stageTicks);
                RegisterProcessedInspectionPair();

                // Display rendering is best-effort and must never hold the serial product
                // pipeline. Captured records are immutable for this cycle, so the UI can
                // finish at Background priority while the next result group is validated.
                displayCompletionTask.SafeFireAndForget(ex => logger.Error(
                    ex,
                    $"DISPLAY_UPDATE_BACKGROUND_FAILED|topSequence={topResult?.ResultSequence ?? 0}"));
                displayMs = ElapsedMillisecondsSince(displayStartTicks);

                // Fondazione dati AI (Fase 0): cattura non bloccante delle misure strutturate
                // di ispezione (tbl_inspection_measurements). Additiva, degrada da sola se DB assente.
                stageTicks = Stopwatch.GetTimestamp();
                ServiceLocator.MeasurementCaptureService?.CaptureInspection(
                    inspectionResult,
                    configManager?.Config?.Configuration?.LastRecipe,
                    _produzioneRecord?.IdProduzione ?? 0,
                    isTop3DInspection ? "top3d" : "top",
                    sideResult?.CameraRole,
                    frontResult?.CameraRole,
                    rearResult?.CameraRole,
                    bottomResult?.CameraRole,
                    rightResult?.CameraRole);
                advisoryMs += ElapsedMillisecondsSince(stageTicks);


                if (_saveImage != null)
                {
                    stageTicks = Stopwatch.GetTimestamp();
                    if (inspectionResult.IsUnclassified)
                    {
                        logger.Warn(
                            $"UNCLASSIFIED_DIAGNOSTIC_SAVE|roles={string.Join(",", inspectionResult.UnclassifiedCameraRoles)}" +
                            "|policy=forced-all-active-views");
                        _saveImage.ForceDiagnosticSave();
                    }
                    else if (!inspectionResult.HasDefects)
                    {
                        _saveImage.CheckOkPercentageSave();
                    }
                    else
                    {
                        _saveImage.CheckFailPercentageSave();
                    }
                    saveDecisionMs = ElapsedMillisecondsSince(stageTicks);
                }

                // Fase 3a: raccolta dati etichettati per il training vision (solo se le immagini
                // del pezzo sono state salvate in questo ciclo). Additiva, fire-and-forget.
                stageTicks = Stopwatch.GetTimestamp();
                ServiceLocator.TrainingDataCollectionService?.CaptureSample(
                    inspectionResult,
                    configManager?.Config?.Configuration?.LastRecipe,
                    _produzioneRecord?.IdProduzione ?? 0,
                    _produzioneRecord?.PieceData);

                // Fase 3: classificatore difetti ONNX in shadow-mode (advisory). No-op se il modello
                // non e' caricato o non ci sono immagini salvate; non incide su ispezione/scarto.
                // Un'istanza indipendente per camera (TOP, SIDE, REAR, FRONT, BOTTOM): ognuna legge
                // la propria immagine e si confronta col risultato-regole della propria camera.
                // Stesso pezzo, stessa cartella immagini; senza modello abilitato e' un no-op.
                string shadowPieceFolder = _produzioneRecord?.PieceData;
                foreach (var defectClassifier in ServiceLocator.AllDefectClassifiers)
                {
                    defectClassifier?.RunShadowComparison(inspectionResult, shadowPieceFolder);
                }

                // Fase 5: ottimizzatore timing IO/encoder (advisory). Osserva in-memory il lag dei
                // risultati companion rispetto al TOP; non rilegge il DB e non incide sul loop di controllo.
                ServiceLocator.IoTimingOptimizer?.ObserveInspectionTiming(topResult, companions, missingRoles);

                var recipeAdvisor = ServiceLocator.RecipeProductAdvisor;
                recipeAdvisor?.ObserveInspection(
                    inspectionResult,
                    configManager?.Config?.Configuration?.LastRecipe,
                    _produzioneRecord?.IdProduzione ?? 0,
                    recipeAdvisor.Enabled ? GetCurrentTopCameraImage() : null);
                advisoryMs += ElapsedMillisecondsSince(stageTicks);

                var recordSnapshot = CreateInspectionRecordSnapshot(inspectionResult);
                stageTicks = Stopwatch.GetTimestamp();
                addRecordIndb(recordSnapshot).SafeFireAndForget();
                dbQueueMs = ElapsedMillisecondsSince(stageTicks);

                var performanceMonitor = ServiceLocator.AiPerformanceMonitor;
                if (performanceMonitor?.Enabled == true)
                {
                    performanceMonitor.Observe(new AiInspectionPerformanceSample
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Recipe = configManager?.Config?.Configuration?.LastRecipe,
                        ProductId = _produzioneRecord?.IdProduzione ?? 0,
                        IsValid = inspectionResult.IsValid,
                        CameraRoles = string.Join(",", new[]
                        {
                            isTop3DInspection ? "top3d" : "top",
                            sideResult?.CameraRole,
                            frontResult?.CameraRole,
                            rearResult?.CameraRole,
                            rightResult?.CameraRole,
                            bottomResult?.CameraRole
                        }.Where(role => !string.IsNullOrWhiteSpace(role))),
                        TotalMs = ElapsedMillisecondsSince(performanceStartTicks),
                        DisplayMs = displayMs,
                        ValidationMs = validationMs,
                        CountersMs = countersMs,
                        SaveDecisionMs = saveDecisionMs,
                        AdvisoryMs = advisoryMs,
                        DbQueueMs = dbQueueMs
                    });
                }

                logger.Info("Processing completed successfully");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error processing inspection pair");
            }
            finally
            {
                topResult?.ReleaseOutputSnapshot();
                if (companionResults != null)
                {
                    foreach (CameraResult companion in companionResults.Values.Where(value => value != null).Distinct())
                    {
                        companion.ReleaseOutputSnapshot();
                    }
                }
            }
        }

        private static CameraResult FindCompanionResult(
            IReadOnlyDictionary<string, CameraResult> companionResults,
            params string[] roles)
        {
            if (companionResults == null || roles == null)
            {
                return null;
            }

            foreach (string role in roles)
            {
                // Le chiavi arrivano dall'orchestratore gia' normalizzate come camera fisica
                // (left/right/rear). La normalizzazione mantiene separati Right e Rear.
                string normalizedRole = CameraConfigurationHelper.NormalizePhysicalCameraRole(role);
                if (companionResults.TryGetValue(normalizedRole, out CameraResult result))
                {
                    return result;
                }
            }

            return null;
        }

        private static void ApplyInspectionCameraRoles(
            InspectionResult inspectionResult,
            CameraResult topResult,
            CameraResult sideResult,
            CameraResult frontResult,
            CameraResult rearResult,
            CameraResult bottomResult,
            bool isTop3DInspection,
            CameraResult rightResult = null)
        {
            if (inspectionResult == null)
            {
                return;
            }

            inspectionResult.TopCameraRole = isTop3DInspection
                ? "top3d"
                : NormalizeClassificationCameraRole(topResult?.CameraRole, "top");
            inspectionResult.SideCameraRole = NormalizeClassificationCameraRole(sideResult?.CameraRole, "side");
            inspectionResult.FrontCameraRole = NormalizeClassificationCameraRole(frontResult?.CameraRole, "front");
            inspectionResult.RearCameraRole = NormalizeClassificationCameraRole(rearResult?.CameraRole, "rear");
            inspectionResult.RightCameraRole = NormalizeClassificationCameraRole(rightResult?.CameraRole, "right");
            inspectionResult.BottomCameraRole = NormalizeClassificationCameraRole(bottomResult?.CameraRole, "bottom");
        }

        private static string NormalizeClassificationCameraRole(string cameraRole, string fallback)
        {
            if (string.IsNullOrWhiteSpace(cameraRole))
            {
                return fallback;
            }

            string normalized = CameraConfigurationHelper.NormalizeCameraType(cameraRole);
            return string.IsNullOrWhiteSpace(normalized)
                ? CameraConfigurationHelper.NormalizeCameraDisplayType(fallback)
                : CameraConfigurationHelper.NormalizeCameraDisplayType(normalized);
        }

        private static InspectionResult CreateUnclassifiedInspectionResult(
            IEnumerable<CameraResult> processingErrorResults)
        {
            var affectedResults = (processingErrorResults ?? Enumerable.Empty<CameraResult>())
                .Where(result => result != null)
                .ToList();
            var errors = affectedResults
                .Select(result =>
                    $"{result.CameraRole ?? result.JobName ?? "camera"}: " +
                    $"{(string.IsNullOrWhiteSpace(result.RunStatusMessage) ? "VisionPro processing error" : result.RunStatusMessage)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var inspectionResult = new InspectionResult
            {
                Outcome = InspectionOutcomeCode.Unclassified,
                IsValid = false,
                HasDefects = true,
                RejectRequired = true,
                Message = "Inspection not classified because one or more VisionPro tools failed.",
                ProcessingErrors = errors,
                UnclassifiedCameraRoles = affectedResults
                    .Select(result => CameraConfigurationHelper.NormalizeCameraType(result.CameraRole))
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                DetectedDefects = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Unclassified"] = true
                },
                RejectingDefects = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Unclassified"] = true
                }
            };

            foreach (CameraResult result in affectedResults)
            {
                var validation = new ValidationResult
                {
                    IsValid = false,
                    Measurements = new Dictionary<string, (double Value, double Tolerance, bool Passed)>(),
                    ErrorMessages = new List<string>
                    {
                        string.IsNullOrWhiteSpace(result.RunStatusMessage)
                            ? "VisionPro processing error"
                            : result.RunStatusMessage
                    }
                };

                switch (CameraConfigurationHelper.NormalizeCameraType(result.CameraRole))
                {
                    case "top":
                    case "top3d":
                        inspectionResult.TopResult = validation;
                        break;
                    case "side":
                    case "left":
                        inspectionResult.SideResult = validation;
                        break;
                    case "front":
                        inspectionResult.FrontResult = validation;
                        break;
                    case "rear":
                        inspectionResult.RearResult = validation;
                        break;
                    case "right":
                        inspectionResult.RightResult = validation;
                        break;
                    case "bottom":
                        inspectionResult.BottomResult = validation;
                        break;
                }
            }

            logger.Error(
                $"INSPECTION_UNCLASSIFIED|outcome=3|roles={string.Join(",", inspectionResult.UnclassifiedCameraRoles)}" +
                $"|errors={string.Join(" || ", errors)}");
            return inspectionResult;
        }

        private void CacheLatestInspectionArtifacts(
            CameraResult topResult,
            CameraResult sideResult,
            CameraResult frontResult,
            CameraResult rearResult,
            CameraResult bottomResult,
            CameraResult rightResult = null)
        {
            SetRecord(ref _topRecord,   topResult?.Record);
            SetRecord(ref _sideRecord,  sideResult?.Record);
            SetRecord(ref _frontRecord, frontResult?.Record);
            SetRecord(ref _rearRecord, rearResult?.Record);
            SetRecord(ref _rightRecord, rightResult?.Record);
            SetRecord(ref _bottomRecord, bottomResult?.Record);
            _topSaveRecord = _topRecord;
            _sideSaveRecord = _sideRecord;
            _frontSaveRecord = _frontRecord;
            _rearSaveRecord = _rearRecord;
            _rightSaveRecord = _rightRecord;
            _bottomSaveRecord = _bottomRecord;
            _topDisplaySaveRecord = null;
            _sideDisplaySaveRecord = null;
            _frontDisplaySaveRecord = null;
            _rearDisplaySaveRecord = null;
            _rightDisplaySaveRecord = null;
            _bottomDisplaySaveRecord = null;

            _topToolBlockReults = topResult != null ? topResult.ToolBlock : null;
            _sideToolBlockResults = sideResult != null ? sideResult.ToolBlock : null;
            _frontTBResults = frontResult != null ? frontResult.ToolBlock : null;
            _RearTBResults = rearResult != null ? rearResult.ToolBlock : null;
            _RightTBResults = rightResult != null ? rightResult.ToolBlock : null;
            _bottomTBResults = bottomResult != null ? bottomResult.ToolBlock : null;
        }
        private async Task ProcessQueuedPairAsync()
        {
            await EnsureInspectionOrchestratorInitialized().ProcessQueuedPairsAsync(_cts.Token);
        }

        private TimeSpan GetInspectionGroupTimeout()
        {
            return GetActiveVisionResultTimeout();
        }

        /// <summary>
        /// Lag massimo (ms) entro cui un companion e' considerato dello STESSO prodotto del TOP.
        /// Concettualmente distinto da <see cref="GetInspectionGroupTimeout"/> (quanto attendo il
        /// risultato): qui conta la durata FISICA plausibile della cattura. MultiShot -> valore da
        /// config cachato; single-shot -> floor fisso 5s.
        /// </summary>
        private double GetCompanionSameProductLagMs()
        {
            return IsAnyMultiShotRuntimeActive()
                ? _multiShotCompanionMaxLagMs
                : SingleShotCompanionMaxLagMs;
        }

        private static double ElapsedMillisecondsSince(long startTicks)
        {
            return (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        }

        private async Task ProcessInspectionWithCountersAsync(InspectionResult inspectionResult, CameraResult topResult, CameraResult sideResult, CameraResult frontResult)
        {
            try
            {
                if (inspectionResult?.IsUnclassified == true)
                {
                    inspectionResult.IsValid = false;
                    inspectionResult.HasDefects = true;
                    inspectionResult.RejectRequired = true;
                    await CounterManager.ProcessInspectionAsync((int)InspectionOutcomeCode.Unclassified, inspectionResult.DetectedDefects);
                    UpdateCameraStatusIndicators(inspectionResult);
                    await PublishInspectionResultToOpcUaAsync(inspectionResult);
                    await ServiceLocator.AlarmService.ProcessInspectionResultAsync(inspectionResult);
                    return;
                }

                var defects = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var nonRejectingDefects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                MergeProcessMonitoringDefects(inspectionResult.TopResult, defects, nonRejectingDefects);
                MergeProcessMonitoringDefects(inspectionResult.SideResult, defects, nonRejectingDefects);
                MergeProcessMonitoringDefects(inspectionResult.FrontResult, defects, nonRejectingDefects);
                MergeProcessMonitoringDefects(inspectionResult.RearResult, defects, nonRejectingDefects);
                MergeProcessMonitoringDefects(inspectionResult.RightResult, defects, nonRejectingDefects);
                MergeProcessMonitoringDefects(inspectionResult.BottomResult, defects, nonRejectingDefects);

                // Analizza i risultati per determinare i difetti
                if (inspectionResult.TopResult != null && !inspectionResult.TopResult.IsValid)
                {
                    AnalyzeTopDefects(inspectionResult.TopResult, defects);
                }

                if (inspectionResult.SideResult != null && !inspectionResult.SideResult.IsValid)
                {
                    AnalyzeSideDefects(inspectionResult.SideResult, defects);
                }

                if (inspectionResult.FrontResult != null && !inspectionResult.FrontResult.IsValid)
                {
                    AnalyzeFrontDefects(inspectionResult.FrontResult, defects);
                }

                if (inspectionResult.RearResult != null && !inspectionResult.RearResult.IsValid)
                {
                    AnalyzeSideDefects(inspectionResult.RearResult, defects);
                }
                if (inspectionResult.RightResult != null && !inspectionResult.RightResult.IsValid)
                {
                    AnalyzeSideDefects(inspectionResult.RightResult, defects);
                }

                if (inspectionResult.BottomResult != null && !inspectionResult.BottomResult.IsValid)
                {
                    AnalyzeBottomDefects(inspectionResult.BottomResult, defects);
                }

                if (inspectionResult.MissingCameraResults != null)
                {
                    foreach (var missingResult in inspectionResult.MissingCameraResults.Where(result => result?.IsValid == false))
                    {
                        AnalyzeMissingCameraDefects(missingResult, defects);
                    }
                }

                inspectionResult.DetectedDefects = new Dictionary<string, bool>(defects, StringComparer.OrdinalIgnoreCase);
                inspectionResult.HasDefects = !inspectionResult.IsValid || defects.Any(d => d.Value);
                inspectionResult.RejectingDefects = BuildRejectingDefectMap(defects, nonRejectingDefects);
                inspectionResult.RejectRequired =
                    inspectionResult.RejectingDefects.Any(d => d.Value) ||
                    (!inspectionResult.IsValid && !defects.Any(d => d.Value));
                inspectionResult.Outcome = inspectionResult.HasDefects
                    ? InspectionOutcomeCode.NoGood
                    : InspectionOutcomeCode.Good;

                if (inspectionResult.RejectRequired)
                {
                    bool isCriticalRejectDefect = inspectionResult.RejectingDefects.Any(d =>
                        d.Value && (d.Key.Equals("Logo", StringComparison.OrdinalIgnoreCase) ||
                                    d.Key.Equals("PrintCentering", StringComparison.OrdinalIgnoreCase)));

                    if (isCriticalRejectDefect)
                    {
                        ICogImage currentImage = GetCurrentTopCameraImage();
                        if (currentImage != null)
                        {
                            OnInspectionResult("Fail", currentImage);
                        }
                    }
                }

                await CounterManager.ProcessInspectionAsync((int)inspectionResult.Outcome, defects);

                UpdateCameraStatusIndicators(inspectionResult);
                await PublishInspectionResultToOpcUaAsync(inspectionResult);

                await ServiceLocator.AlarmService.ProcessInspectionResultAsync(inspectionResult);
            }
            catch (Exception ex)
            {
                logger.Error($"Errore nell'aggiornamento dei contatori: {ex.Message}");
            }
        }

        private static void MergeProcessMonitoringDefects(
            ValidationResult validationResult,
            IDictionary<string, bool> detectedDefects,
            ISet<string> nonRejectingDefects)
        {
            if (validationResult?.MonitoringResults == null)
            {
                return;
            }

            foreach (ProcessMonitoringResult monitoring in validationResult.MonitoringResults.Values)
            {
                if (monitoring == null || !monitoring.IsDefect || string.IsNullOrWhiteSpace(monitoring.FeatureKey))
                {
                    continue;
                }

                detectedDefects[monitoring.FeatureKey] = true;
                if (!monitoring.RejectEnabled)
                {
                    nonRejectingDefects.Add(monitoring.FeatureKey);
                }
            }
        }
        private async Task PublishInspectionResultToOpcUaAsync(InspectionResult inspectionResult)
        {
            try
            {
                // Publish a compact snapshot after local counters are updated.
                // The external handshake uses the OPC UA service; this method
                // only prepares production values and current recipe identity.
                var counters = CounterManager.GetAllCounters();
                long total = counters.ContainsKey("Total") ? counters["Total"] : 0;
                long good = counters.ContainsKey("Good") ? counters["Good"] : 0;
                long bad = counters.ContainsKey("NoGood") ? counters["NoGood"] : 0;

                var currentRecipeName = System.IO.Path.GetFileNameWithoutExtension(configManager?.Config?.Configuration?.LastRecipe ?? string.Empty);
                var currentRecipeId = await ResolveRecipeIdForOpcUaAsync(currentRecipeName);

                await ServiceLocator.OpcUaClientService.WriteInspectionSnapshotAsync(
                    !inspectionResult.HasDefects,
                    total,
                    good,
                    bad,
                    currentRecipeName,
                    currentRecipeId);

                // Health nodes — read-only by SCADA; updated every cycle so the MES
                // can detect degraded production without relying on inspection events alone.
                var rt = ServiceLocator.MachineRuntimeService;
                string healthStatus = (rt != null && !rt.IsVisionHealthy) ? "CAMERA_MISSING" : "OK";
                string lastAlarmCode = ServiceLocator.AlarmService?.GetFirstTriggeredAlarmCode() ?? string.Empty;
                string holdReasons = rt?.ContinuousRunHoldSummary ?? string.Empty;
                await ServiceLocator.OpcUaClientService.WriteHealthSnapshotAsync(healthStatus, lastAlarmCode, holdReasons);
            }
            catch (Exception ex)
            {
                logger.Warn($"OPC UA inspection publish failed: {ex.Message}");
            }
        }
        private ICogImage GetCurrentTopCameraImage() => EnsureCameraDisplayManagerInitialized().GetCurrentTopCameraImage();
        private void UpdateCameraStatusIndicators(InspectionResult inspectionResult)
            => EnsureCameraDisplayManagerInitialized().UpdateCameraStatusIndicators(inspectionResult);
        private void AnalyzeTopDefects(ValidationResult topResult, Dictionary<string, bool> defects)
        {
            AnalyzeAiClassificationDefect(topResult, defects);

            foreach (var error in topResult.ErrorMessages)
            {
                if (IsAiClassificationError(error))
                {
                    continue;
                }

                string errorLower = error.ToLowerInvariant();

                if (errorLower.Contains("logo"))
                {
                    defects["Logo"] = true;
                }
                else if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                    (errorLower.Contains("height") || errorLower.Contains("altezza")))
                {
                    defects["ThreeDHeight"] = true;
                }
                else if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                    (errorLower.Contains("width") || errorLower.Contains("larghezza")))
                {
                    defects["ThreeDWidth"] = true;
                }
                else if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                    (errorLower.Contains("length") || errorLower.Contains("lunghezza")))
                {
                    defects["ThreeDLength"] = true;
                }
                else if (errorLower.Contains("print centering"))
                {
                    defects["PrintCentering"] = true;
                }
                else if (errorLower.Contains("open flaps"))
                {
                    defects["OpenFlaps"] = true;
                }
                else if (errorLower.Contains("surface"))
                {
                    defects["SurfaceCheck"] = true;
                }
                else if (errorLower.Contains("shape") && errorLower.Contains("top"))
                {
                    defects["ShapeTop"] = true;
                }
            }
        }

        private void AnalyzeSideDefects(ValidationResult sideResult, Dictionary<string, bool> defects)
        {
            AnalyzeAiClassificationDefect(sideResult, defects);

            foreach (var error in sideResult.ErrorMessages)
            {
                if (IsAiClassificationError(error))
                {
                    continue;
                }

                string errorLower = error.ToLowerInvariant();

                if (errorLower.Contains("height"))
                {
                    defects["Height"] = true;
                }
                else if (errorLower.Contains("sealing"))
                {
                    defects["SideSealing"] = true;
                }
                else if (errorLower.Contains("roll count") || errorLower.Contains("rollcount") || errorLower.Contains("rotoli"))
                {
                    defects["SideRollCount"] = true;
                }
                else if (errorLower.Contains("shape") && errorLower.Contains("side"))
                {
                    defects["ShapeSide"] = true;
                }
            }
        }

        private void AnalyzeFrontDefects(ValidationResult frontResult, Dictionary<string, bool> defects)
        {
            AnalyzeAiClassificationDefect(frontResult, defects);

            foreach (var error in frontResult.ErrorMessages)
            {
                if (IsAiClassificationError(error))
                {
                    continue;
                }

                string errorLower = error.ToLowerInvariant();
                if (errorLower.Contains("traceability") || errorLower.Contains("code"))
                {
                    defects["FrontTraceability"] = true;
                }
                if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                    (errorLower.Contains("height") || errorLower.Contains("altezza")))
                {
                    defects["ThreeDHeight"] = true;
                }
                if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                    (errorLower.Contains("width") || errorLower.Contains("larghezza")))
                {
                    defects["ThreeDWidth"] = true;
                }
                if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                    (errorLower.Contains("length") || errorLower.Contains("lunghezza")))
                {
                    defects["ThreeDLength"] = true;
                }
            }
        }

        private void AnalyzeBottomDefects(ValidationResult bottomResult, Dictionary<string, bool> defects)
        {
            AnalyzeAiClassificationDefect(bottomResult, defects);

            foreach (var error in bottomResult.ErrorMessages)
            {
                if (IsAiClassificationError(error))
                {
                    continue;
                }

                string errorLower = error.ToLowerInvariant();

                if (errorLower.Contains("trapped paper") ||
                    errorLower.Contains("paper trapped") ||
                    errorLower.Contains("carta intrappolata") ||
                    errorLower.Contains("carta in saldatura"))
                {
                    defects["TrappedPaper"] = true;
                }
                else if (errorLower.Contains("bottom sealing") ||
                         errorLower.Contains("bottom seal") ||
                         errorLower.Contains("lower sealing") ||
                         errorLower.Contains("saldatura inferiore"))
                {
                    defects["BottomSealing"] = true;
                }
            }
        }

        private static void AnalyzeAiClassificationDefect(
            ValidationResult validationResult,
            Dictionary<string, bool> defects)
        {
            if (validationResult?.ErrorMessages == null || defects == null)
            {
                return;
            }

            if (validationResult.ErrorMessages.Any(IsAiClassificationError))
            {
                defects["AIClassification"] = true;
            }
        }

        private static bool IsAiClassificationError(string error)
        {
            return !string.IsNullOrWhiteSpace(error) &&
                   error.IndexOf("AI classification", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AnalyzeMissingCameraDefects(ValidationResult missingResult, Dictionary<string, bool> defects)
        {
            defects["CameraMissing"] = true;

            foreach (var error in missingResult.ErrorMessages ?? Enumerable.Empty<string>())
            {
                string errorLower = error.ToLowerInvariant();
                if (errorLower.Contains("front"))
                {
                    defects["FrontTraceability"] = true;
                }
                else if (errorLower.Contains("rear") || errorLower.Contains("right"))
                {
                    defects["SideSealing"] = true;
                }
                else if (errorLower.Contains("bottom"))
                {
                    defects["BottomSealing"] = true;
                    defects["TrappedPaper"] = true;
                }
                else if (errorLower.Contains("side") || errorLower.Contains("left"))
                {
                    defects["SideSealing"] = true;
                }
            }
        }

        private Dictionary<string, bool> BuildRejectingDefectMap(
            Dictionary<string, bool> detectedDefects,
            ISet<string> nonRejectingDefects = null)
        {
            var rejectingDefects = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (detectedDefects == null)
            {
                return rejectingDefects;
            }

            foreach (var defect in detectedDefects.Where(current => current.Value))
            {
                if (nonRejectingDefects?.Contains(defect.Key) == true)
                {
                    continue;
                }

                if (ShouldRejectDefect(defect.Key))
                {
                    rejectingDefects[defect.Key] = true;
                }
            }

            return rejectingDefects;
        }

        private bool ShouldRejectDefect(string featureKey)
        {
            var status = ConfigRecipeParam?.Config?.ejectionStatus;
            if (status == null)
            {
                // Backward-compatible fallback: if the recipe still has no explicit
                // reject section loaded, preserve the historical behaviour.
                return true;
            }

            switch (featureKey?.Trim())
            {
                case "Logo":
                    return status.logo;
                case "PrintCentering":
                    return status.Print_centering;
                case "OpenFlaps":
                    return status.OpenFlaps;
                case "SurfaceCheck":
                    return status.SurfaceCheck;
                case "Height":
                    return status.Height;
                case "SideSealing":
                    return status.Side_sealing;
                case "SideRollCount":
                    return true;
                case "ShapeTop":
                    return status.ShapeTop;
                case "ShapeSide":
                    return status.ShapeSide;
                case "FrontTraceability":
                    return status.FrontTraceability;
                case "ThreeDHeight":
                    return status.ThreeDHeight;
                case "ThreeDWidth":
                    return status.ThreeDWidth;
                case "ThreeDLength":
                    return status.ThreeDLength;
                case "BottomSealing":
                    return status.BottomSealing;
                case "TrappedPaper":
                    return status.TrappedPaper;
                case "AIClassification":
                    return status.AIClassification;
                default:
                    return true;
            }
        }

        private Task ProcessToolResultsAsyncside(ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, string displayRole = "side", CameraResult capturedResult = null)
            => EnsureCameraDisplayManagerInitialized().ProcessToolResultsAsyncside(record, view, subRecordKey, token, displayRole, capturedResult);

        private Task ProcessToolResultsAsyncTop(ICogRecord record, CogRecordDisplay view, string subRecordKey, string displayRole, CancellationToken token, CameraResult capturedResult = null)
            => EnsureCameraDisplayManagerInitialized().ProcessToolResultsAsyncTop(record, view, subRecordKey, displayRole, token, capturedResult);

        private Task ProcessToolResultsAsyncTopSecondary(ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
            => EnsureCameraDisplayManagerInitialized().ProcessToolResultsAsyncTopSecondary(record, view, subRecordKey, token, capturedResult);

        private Task ProcessToolResultsAsyncFront(ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
            => EnsureCameraDisplayManagerInitialized().ProcessToolResultsAsyncFront(record, view, subRecordKey, token, capturedResult);

        private Task ProcessToolResultsAsyncRear(ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
            => EnsureCameraDisplayManagerInitialized().ProcessToolResultsAsyncRear(record, view, subRecordKey, token, capturedResult);

        private Task ProcessToolResultsAsyncBottom(ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
            => EnsureCameraDisplayManagerInitialized().ProcessToolResultsAsyncBottom(record, view, subRecordKey, token, capturedResult);

        private Task UpdateDisplayParallelAsync(ICogRecord record, CogRecordDisplay display, string subRecordKey, CogJob job, ICogRunStatus runStatus, string displayRole, CancellationToken token)
            => EnsureCameraDisplayManagerInitialized().UpdateDisplayParallelAsync(record, display, subRecordKey, job, runStatus, displayRole, token);
        /// <summary>
        /// Handles unexpected VisionPro stop notifications.
        /// The handler updates cached runtime state immediately and queues recovery
        /// without forcing expensive manager reads on the UI thread.
        /// </summary>
        private void CognexManager_JobStopped(object sender, CogJobManagerActionEventArgs e)
        {
            bool shouldBeRunning = ServiceLocator.MachineRuntimeService.IsContinuousRunRequested;
            string stopReason = e != null ? e.Action.ToString() : "Unknown";

            if (IsJobEditorLivePreviewActive)
            {
                logger.Debug($"CognexManager_JobStopped ignored during Job Editor Live Preview (reason: {stopReason}).");
                return;
            }

            // If we are within the state-transition suppression window and the machine is
            // expected to keep running, this is a spurious delayed stop event â€” e.g. the
            // job-editor single-run preview completing after continuous run was already
            // restarted (StoppedSingle ~500 ms after navigation).  Ignore it completely;
            // flipping state to Stopped here would cause an immediate unwanted stop.
            if (shouldBeRunning && IsVisionAutoRecoverySuppressed())
            {
                logger.Debug($"CognexManager_JobStopped suppressed during state transition (reason: {stopReason}). Machine is still expected to run.");
                return;
            }

            ServiceLocator.MachineRuntimeService.UpdateContinuousRunState(false);
            _isContinuousRunActive = false;
            ClearPendingVisionResults("VisionPro reported a stopped job", clearCachedArtifacts: false, forceLog: true);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DataContext is MainViewModel mainVm)
                {
                    mainVm.ControlButtonsVM.IsRunning = false;
                    mainVm.ControlButtonsVM.IsSystemReady = true;
                    logger.Debug($"Stato aggiornato: IsRunning = {mainVm.ControlButtonsVM.IsRunning}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);

            _applicationEventLogger.LogOperationalEvent(
                shouldBeRunning ? LogLevel.Warn : LogLevel.Info,
                "VISIONPRO_JOB_STOPPED",
                "VisionPro",
                "VisionPro job stopped",
                nameof(CognexManager_JobStopped),
                stopReason);

            if (shouldBeRunning && !IsVisionAutoRecoverySuppressed())
            {
                string issueDetails = $"A VisionPro job stopped unexpectedly while continuous run is still expected. Reason: {stopReason}.";
                ServiceLocator.MachineRuntimeService.MarkVisionFault(issueDetails);
                QueueVisionAutoRecovery(issueDetails);
            }
        }

        private void QueueVisionAutoRecovery(string reason)
        {
            if (Interlocked.Exchange(ref _visionRecoveryQueued, 1) == 1)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await AttemptVisionAutoRecoveryAsync(reason).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _visionRecoveryQueued, 0);
                }
            }).SafeFireAndForget(false);
        }
        #endregion
        #region update date time
        private void UpdateDateTimeDisplay(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Controlla se NavigationMenu.Instance esiste
                    if (NavigationMenu.Instance != null && NavigationMenu.Instance.lbdate != null)
                    {
                        NavigationMenu.Instance.lbdate.FontWeight = FontWeights.Bold;
                        NavigationMenu.Instance.lbdate.FontSize = 16;
                        NavigationMenu.Instance.lbdate.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                    }

                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error updating date/time display");
            }
        } 
        public (CogJob, CogJob) GetJobsSafe()
        {
            try
            {
                var topJob = _cognexManager.JobCount > 0 ? _cognexManager.GetJob(0) : null;
                var sideJob = _cognexManager.JobCount > 1 ? _cognexManager.GetJob(1) : null;

                CogJobResultHistoryCollection jobs = new CogJobResultHistoryCollection();
                bool changed = false;
                for (int j = 0; j < _cognexManager.GetJobManager().JobCount; j++)
                {
                    int i = mHistoryCollection.IndexOf(_cognexManager.GetJobManager().Job(j).Name);
                    if (i != -1)
                    {
                        if (i != jobs.Count)
                        {
                            jobs.Add(mHistoryCollection[i]);
                            changed = true;
                        }
                        else
                        {
                            jobs.Add(new CogJobResultHistoryGated(_cognexManager.GetJobManager().Job(j).Name));
                            changed = true;

                        }

                    }
                    if (jobs.Count != mHistoryCollection.Count)
                        changed = true;
                    if (changed)
                    {
                        mHistoryCollection = jobs;

                    }

                }

                return (topJob, sideJob);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting jobs");
                return (null, null);
            }
        }
        public (CogToolGroup, CogToolGroup) GetToolGroups()
        {
            try
            {
                var topGroup = _topJob?.VisionTool as CogToolGroup;
                var sideGroup = _sideJob?.VisionTool as CogToolGroup;
                return (topGroup, sideGroup);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting tool groups");
                return (null, null);
            }
        }
        #endregion

        #region database manager
        public async Task InitializeDatabaseAsync()
        {
            try
            {
                _applicationEventLogger.LogLifecycle("DATABASE_INITIALIZING", "Database initialization started", nameof(InitializeDatabaseAsync));

                // Inizializza il database per i contatori globali
                _initializzeDb = new Cls_InitializzeDb();
               await Task.Delay(500);
                await _initializzeDb.Initialize();

                await Task.Delay(300);
                await _initializzeDb.AddNewColumnsToTblGenerale();
                await Task.Delay(100);
                // 1. INIZIALIZZA PRIMA _insertDataInDb
                if (_insertDataInDb == null)
                {
                    _insertDataInDb = new InserdataInDb();
                }

                _alarmCardRepository=new Database.AlarmCardRepository();
                await Task.Delay(100);
                await _alarmCardRepository.CreateTableIfNotExistsAsync();
                await Task.Delay(100);
                await _alarmCardRepository.InsertDefaultAlarmsIfEmptyAsync();

                if (MainWindow._produzioneRecord == null)
                {
                    MainWindow._produzioneRecord = new ProduzioneRecord();
                }

                MainWindow._produzioneRecord.Operatore = ResolveCurrentOperator();

                await ServiceLocator.OpcUaClientService.InitializeAsync(_cts?.Token ?? CancellationToken.None);

                _applicationEventLogger.LogLifecycle("DATABASE_INITIALIZED", "Database initialization completed", nameof(InitializeDatabaseAsync));

            }
            catch (Exception ex)
            {
                logger.Error($"Database initialization failed: {ex.Message}");
                _applicationEventLogger.LogException("DATABASE_INIT_FAILED", ex, nameof(InitializeDatabaseAsync), "Database initialization failed");
                new SystemNotificationWindow("Error", $"Database initialization failed: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        public string SerializeToXml<T>(T obj)
        {
            try
            {
                return JsonConvert.SerializeObject(obj, Formatting.None);
            }
            catch (Exception ex)
            {
                logger.Error($"Error serializing object to JSON: {ex.Message}");
                return "{}";
            }
        }
        public async Task checkIdProd()
        {
            try
            {
                string jsonTop = SerializeToXml(ConfigRecipeParam.Config.recipeParamTop);
                string jsonSide = SerializeToXml(ConfigRecipeParam.Config.recipeParamSide);
                string jsonFront = SerializeToXml(ConfigRecipeParam.Config.recipeParamFront);
                string jsonTop3D = SerializeToXml(ConfigRecipeParam.Config.recipeParamTop3D);
                string jsonLast = SerializeToXml(ConfigRecipeParam.Config);
                string jsonParam = SerializeToXml(configManager.Config);

                var inserIdProd = new InserdataInDb();
                string ricetta = System.IO.Path.GetFileName(configManager.Config.Configuration.LastRecipe ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(ricetta) &&
                    !ricetta.EndsWith(".vpp", StringComparison.OrdinalIgnoreCase))
                {
                    ricetta += ".vpp";
                }

                if (_produzioneRecord == null)
                {
                    _produzioneRecord = new ProduzioneRecord();
                }

                // The production identity belongs to the active recipe, not to a
                // previous piece. Clear it before resolving the new recipe so a DB
                // failure cannot associate inspections with the preceding recipe.
                _produzioneRecord.IdProduzione = 0;

                if (string.IsNullOrWhiteSpace(ricetta))
                {
                    logger.Warn("PRODUCTION_ID_UNRESOLVED|recipe=<empty>|active recipe name is not configured");
                    return;
                }

                bool exists = await inserIdProd.ExistsRicettaAsync(ricetta);

                if (!exists)
                {
                    await Task.Run(() => inserIdProd.InsertProduzione(
                        configManager.Config.Configuration.Compagny,
                        "145",
                        "1",
                        "Stato 1",
                        ricetta,
                        ricetta,
                        jsonTop,
                        jsonSide,
                        jsonFront,
                        jsonTop3D,
                        jsonLast,
                        jsonParam,
                        DateTime.Now,
                        DateTime.Now,
                        1
                    ));
                    exists = await inserIdProd.ExistsRicettaAsync(ricetta);
                }
                else
                {

                    await inserIdProd.UpdateStartTimeAsync(ricetta, DateTime.Now);
                    await inserIdProd.udpdatePameterRecipe(ricetta, jsonTop, jsonSide, jsonFront, jsonTop3D, jsonLast, jsonParam);

                    // MessageBox.Show($"La ricetta '{ricetta}' Ã¨ giÃ  presente nel database.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (_produzioneRecord.IdProduzione > 0)
                {
                    logger.Info($"PRODUCTION_ID_RESOLVED|recipe={ricetta}|id={_produzioneRecord.IdProduzione}");
                }
                else
                {
                    logger.Warn($"PRODUCTION_ID_UNRESOLVED|recipe={ricetta}|tblgenerale records will use id 0 until the recipe identity is resolved");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"ID Prod check failed: {ex.Message}");
                //  MessageBox.Show($"ID Prod check failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private ProduzioneRecord CreateBaseInspectionRecord()
        {
            return new ProduzioneRecord
            {
                IdProduzione = _produzioneRecord?.IdProduzione ?? 0,
                Operatore = ResolveCurrentOperator(),
                Prodotto = configManager.Config.Configuration.LastRecipe,
                Ricetta = configManager.Config.Configuration.LastRecipe
            };
        }

        private ProduzioneRecord CreateInspectionRecordSnapshot(InspectionResult inspectionResult)
        {
            var snapshot = (_produzioneRecord ?? new ProduzioneRecord()).Clone();
            snapshot.DataEOra = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            snapshot.Operatore = string.IsNullOrWhiteSpace(snapshot.Operatore) ? ResolveCurrentOperator() : snapshot.Operatore;
            snapshot.Prodotto = string.IsNullOrWhiteSpace(snapshot.Prodotto) ? configManager.Config.Configuration.LastRecipe : snapshot.Prodotto;
            snapshot.Ricetta = string.IsNullOrWhiteSpace(snapshot.Ricetta) ? configManager.Config.Configuration.LastRecipe : snapshot.Ricetta;
            snapshot.EsitoClassificazione = inspectionResult != null
                ? (int)inspectionResult.Outcome
                : (int)InspectionOutcomeCode.Unclassified;
            snapshot.EspulsioneComandata = inspectionResult?.RejectRequired == true ? 1 : 0;
            snapshot.InspectionStatusDetails = BuildInspectionStatusDetails(inspectionResult);
            ApplyVisionClassifications(snapshot, inspectionResult);
            NormalizeInspectionFlagsForDatabase(snapshot, inspectionResult);
            return snapshot;
        }

        private static void ApplyVisionClassifications(ProduzioneRecord snapshot, InspectionResult inspectionResult)
        {
            if (snapshot == null || inspectionResult == null)
            {
                return;
            }

            ApplyVisionClassification(
                inspectionResult.TopClassification,
                out string topLabel,
                out double? topScore);
            snapshot.TopClassificationLabel = topLabel;
            snapshot.TopClassificationScore = topScore;
            snapshot.NCTopAiClassification = ResolveAiClassificationOutcome(
                IsAiClassificationEnabledForView(inspectionResult.TopCameraRole, "top"),
                inspectionResult.TopClassification,
                inspectionResult.TopResult,
                inspectionResult,
                "top");

            ApplyVisionClassification(
                inspectionResult.SideClassification,
                out string sideLabel,
                out double? sideScore);
            snapshot.SideClassificationLabel = sideLabel;
            snapshot.SideClassificationScore = sideScore;
            snapshot.NCSideAiClassification = ResolveAiClassificationOutcome(
                IsAiClassificationEnabledForView(inspectionResult.SideCameraRole, "side"),
                inspectionResult.SideClassification,
                inspectionResult.SideResult,
                inspectionResult,
                "side");

            ApplyVisionClassification(
                inspectionResult.FrontClassification,
                out string frontLabel,
                out double? frontScore);
            snapshot.FrontClassificationLabel = frontLabel;
            snapshot.FrontClassificationScore = frontScore;
            snapshot.NCFrontAiClassification = ResolveAiClassificationOutcome(
                IsAiClassificationEnabledForView(inspectionResult.FrontCameraRole, "front"),
                inspectionResult.FrontClassification,
                inspectionResult.FrontResult,
                inspectionResult,
                "front");

            ApplyVisionClassification(
                inspectionResult.RearClassification,
                out string rearLabel,
                out double? rearScore);
            snapshot.RearClassificationLabel = rearLabel;
            snapshot.RearClassificationScore = rearScore;
            snapshot.NCRearAiClassification = ResolveAiClassificationOutcome(
                IsAiClassificationEnabledForView(inspectionResult.RearCameraRole, "rear"),
                inspectionResult.RearClassification,
                inspectionResult.RearResult,
                inspectionResult,
                "rear");

            ApplyVisionClassification(
                inspectionResult.RightClassification,
                out string rightLabel,
                out double? rightScore);
            snapshot.RightClassificationLabel = rightLabel;
            snapshot.RightClassificationScore = rightScore;
            snapshot.NCRightAiClassification = ResolveAiClassificationOutcome(
                IsAiClassificationEnabledForView(inspectionResult.RightCameraRole, "right"),
                inspectionResult.RightClassification,
                inspectionResult.RightResult,
                inspectionResult,
                "right");

            ApplyVisionClassification(
                inspectionResult.BottomClassification,
                out string bottomLabel,
                out double? bottomScore);
            snapshot.BottomClassificationLabel = bottomLabel;
            snapshot.BottomClassificationScore = bottomScore;
            snapshot.NCBottomAiClassification = ResolveAiClassificationOutcome(
                IsAiClassificationEnabledForView(inspectionResult.BottomCameraRole, "bottom"),
                inspectionResult.BottomClassification,
                inspectionResult.BottomResult,
                inspectionResult,
                "bottom");
        }

        private static bool IsAiClassificationEnabledForView(string cameraRole, string fallbackRole)
        {
            string role = string.IsNullOrWhiteSpace(cameraRole) ? fallbackRole : cameraRole;
            return ServiceLocator.InspectionConfigService.IsFeatureEnabled(role, "AIClassification");
        }

        private static int ResolveAiClassificationOutcome(
            bool inspectionEnabled,
            CameraClassificationResult classification,
            ValidationResult validationResult,
            InspectionResult inspectionResult,
            string cameraRole)
        {
            // NC inspection convention: 0=NOK, 1=GOOD, 3=processing unclassified,
            // 4=not enabled/not executed. Una confidenza insufficiente e' un controllo
            // eseguito ma non affidabile: il pezzo e' NoGood (0), non un errore runtime (3).
            if (!inspectionEnabled)
            {
                return 4;
            }

            if (inspectionResult?.IsUnclassified == true &&
                IsUnclassifiedCameraRole(inspectionResult.UnclassifiedCameraRoles, cameraRole))
            {
                return 3;
            }

            if (classification != null && !string.IsNullOrWhiteSpace(classification.ClassName))
            {
                return classification.State == CameraClassificationState.Passed ? 1 : 0;
            }

            bool classificationFailed = validationResult?.ErrorMessages?.Any(message =>
                !string.IsNullOrWhiteSpace(message) &&
                message.StartsWith("AI classification failed", StringComparison.OrdinalIgnoreCase)) == true;

            return classificationFailed ? 0 : 4;
        }

        private static bool IsUnclassifiedCameraRole(IEnumerable<string> affectedRoles, string cameraRole)
        {
            string requested = CameraConfigurationHelper.NormalizeCameraType(cameraRole);
            foreach (string affectedRole in affectedRoles ?? Enumerable.Empty<string>())
            {
                string affected = CameraConfigurationHelper.NormalizeCameraType(affectedRole);
                if (string.Equals(affected, requested, StringComparison.OrdinalIgnoreCase) ||
                    (requested == "top" && affected == "top3d") ||
                    (requested == "side" && affected == "left"))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyVisionClassification(
            CameraClassificationResult classification,
            out string label,
            out double? score)
        {
            label = classification?.ClassName?.Trim();
            if (label?.Length > 255)
            {
                label = label.Substring(0, 255);
            }

            score = classification?.Score;
        }

        private string ResolveCurrentOperator()
        {
            if (!string.IsNullOrWhiteSpace(_produzioneRecord?.Operatore))
            {
                return _produzioneRecord.Operatore;
            }

            if (!string.IsNullOrWhiteSpace(UserSession.CurrentUser))
            {
                return UserSession.CurrentUser;
            }

            if (!string.IsNullOrWhiteSpace(configManager?.Config?.Configuration?.User))
            {
                return configManager.Config.Configuration.User;
            }

            return "Guest";
        }

        private static string BuildInspectionStatusDetails(InspectionResult inspectionResult)
        {
            if (inspectionResult == null)
            {
                return null;
            }

            var reasons = new List<string>();

            if (inspectionResult.IsUnclassified)
            {
                reasons.Add("Outcome=3 (UNCLASSIFIED)");
                reasons.AddRange((inspectionResult.ProcessingErrors ?? new List<string>())
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => $"VisionPro: {message}"));
            }

            AppendMonitoringDetails(reasons, "Top", inspectionResult.TopResult);
            AppendMonitoringDetails(reasons, "Side", inspectionResult.SideResult);
            AppendMonitoringDetails(reasons, "Front", inspectionResult.FrontResult);
            AppendMonitoringDetails(reasons, "Rear", inspectionResult.RearResult);
            AppendMonitoringDetails(reasons, "Right", inspectionResult.RightResult);
            AppendMonitoringDetails(reasons, "Bottom", inspectionResult.BottomResult);

            if (inspectionResult.TopResult?.ErrorMessages != null)
            {
                reasons.AddRange(inspectionResult.TopResult.ErrorMessages
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => $"Top: {message}"));
            }

            if (inspectionResult.SideResult?.ErrorMessages != null)
            {
                reasons.AddRange(inspectionResult.SideResult.ErrorMessages
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => $"Side: {message}"));
            }

            if (inspectionResult.FrontResult?.ErrorMessages != null)
            {
                reasons.AddRange(inspectionResult.FrontResult.ErrorMessages
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => $"Front: {message}"));
            }

            if (inspectionResult.RearResult?.ErrorMessages != null)
            {
                reasons.AddRange(inspectionResult.RearResult.ErrorMessages
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => $"Rear: {message}"));
            }
            if (inspectionResult.RightResult?.ErrorMessages != null)
            {
                reasons.AddRange(inspectionResult.RightResult.ErrorMessages
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => $"Right: {message}"));
            }

            if (inspectionResult.BottomResult?.ErrorMessages != null)
            {
                reasons.AddRange(inspectionResult.BottomResult.ErrorMessages
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Select(message => $"Bottom: {message}"));
            }

            if (inspectionResult.MissingCameraResults != null)
            {
                foreach (var missingResult in inspectionResult.MissingCameraResults)
                {
                    if (missingResult?.ErrorMessages == null)
                    {
                        continue;
                    }

                    reasons.AddRange(missingResult.ErrorMessages
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .Select(message => $"Camera: {message}"));
                }
            }

            var summary = reasons.Count > 0
                ? string.Join(" | ", reasons.Distinct())
                : (inspectionResult.HasDefects ? inspectionResult.Message : "OK");

            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = inspectionResult.HasDefects ? "Inspection failed" : "OK";
            }

            if (inspectionResult.RejectRequired)
            {
                summary = $"{summary} | Reject=ON";
            }

            return summary.Length <= 1000 ? summary : $"{summary.Substring(0, 997)}...";
        }

        private static void AppendMonitoringDetails(
            ICollection<string> destination,
            string cameraLabel,
            ValidationResult validationResult)
        {
            if (destination == null || validationResult?.MonitoringResults == null)
            {
                return;
            }

            foreach (ProcessMonitoringResult monitoring in validationResult.MonitoringResults.Values)
            {
                if (monitoring == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(monitoring.Summary))
                {
                    destination.Add($"{cameraLabel}: {monitoring.Summary}");
                }

                foreach (string message in monitoring.Messages.Where(message => !string.IsNullOrWhiteSpace(message)))
                {
                    destination.Add($"{cameraLabel}: {message}");
                }
            }
        }

        private static void NormalizeInspectionFlagsForDatabase(ProduzioneRecord snapshot, InspectionResult inspectionResult)
        {
            if (snapshot == null || inspectionResult?.HasDefects != true || inspectionResult.IsUnclassified)
            {
                return;
            }

            if (!snapshot.NcLoghiImmagini.HasValue) snapshot.NcLoghiImmagini = 0;
            if (!snapshot.NcCentraturaLogo.HasValue) snapshot.NcCentraturaLogo = 0;
            if (!snapshot.NcAletteAperte.HasValue) snapshot.NcAletteAperte = 0;
            if (!snapshot.NcHeigth.HasValue) snapshot.NcHeigth = 0;
            if (!snapshot.NcSaldaturaLaterale.HasValue) snapshot.NcSaldaturaLaterale = 0;
            if (!snapshot.NcShapeTop.HasValue) snapshot.NcShapeTop = 0;
            if (!snapshot.NcShapeBottom.HasValue) snapshot.NcShapeBottom = 0;
            if (!snapshot.NCSurfaceCheck.HasValue) snapshot.NCSurfaceCheck = 0;
            if (!snapshot.NCTraceability.HasValue) snapshot.NCTraceability = 0;
            if (!snapshot.NCThreeDHeight.HasValue) snapshot.NCThreeDHeight = 0;
            if (!snapshot.NCThreeDWidth.HasValue) snapshot.NCThreeDWidth = 0;
            if (!snapshot.NCThreeDLength.HasValue) snapshot.NCThreeDLength = 0;
            if (!snapshot.NCBottomSealing.HasValue) snapshot.NCBottomSealing = 0;
            if (!snapshot.NCTrappedPaper.HasValue) snapshot.NCTrappedPaper = 0;
        }

        public async Task addRecordIndb(ProduzioneRecord recordSnapshot)
        {
            if (recordSnapshot == null)
            {
                return;
            }

            try
            {
                var inserIdProd = new InserdataInDb();
                await Task.Run(() => inserIdProd.InsertDataConforme(recordSnapshot));
            }
            catch (Exception ex)
            {
                logger.Error($"Error adding record to database: {ex.Message}");
                new SystemNotificationWindow("Error", $"Error adding record to database: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        public async Task UserdbInitialiseAsync()
        {
            _userManager = new Cls_CheckUser(
                configManager.Config.MySqlConnection.Host,
                "pulsarsdk_auth",
                configManager.Config.MySqlConnection.User,
                configManager.Config.MySqlConnection.Password,
                configManager.Config.MySqlConnection.port,
                configManager.Config.MySqlConnection.SslDisabled
                );
            string defaultRole = configManager.Config.Roles.LastOrDefault() ?? "Viewer";
            await _userManager.InitializeUserAsync("NoUser", "NoUser", defaultRole);
           
        }
        #endregion
        public async Task ManageJobStateAsync(bool start)
        {
            try
            {
                _applicationEventLogger.LogCommand(start ? "StartVision" : "StopVision", "ManageJobStateRequested", nameof(ManageJobStateAsync));
                if (_cognexManager == null)
                {
                    logger.Warn("CognexManager is not initialized");
                    _applicationEventLogger.LogOperationalEvent(LogLevel.Warn, "VISION_MANAGER_NOT_READY", "VisionPro", "Cognex manager is not initialized", nameof(ManageJobStateAsync));
                    return;
                }

                BeginVisionStateTransition();

                await Dispatcher.InvokeAsync(() =>
                {
                    if (DataContext is MainViewModel mainVm)
                    {
                        mainVm.ControlButtonsVM.IsRunning = start;
                    }
                });

                if (start)
                {
                    _continuousRunCts = new CancellationTokenSource();
                    ClearPendingVisionResults("continuous run start requested", clearCachedArtifacts: false, forceLog: false);
                    await ServiceLocator.MachineRuntimeService.StartContinuousRunAsync(_continuousRunCts.Token);
                    _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                    StartVisionHealthMonitoring();

                    logger.Info("Sistema di visione avviato");
                    _applicationEventLogger.LogVisionProStatus(_isContinuousRunActive, nameof(ManageJobStateAsync), "Vision system started from control buttons");
                }
                else
                {
                    bool wasRunning = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                    await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync();
                    ClearPendingVisionResults("continuous run stopped from control buttons", clearCachedArtifacts: false, forceLog: true);

                    if (wasRunning)
                    {
                        logger.Debug("Sistema di visione fermato");
                    }

                    StopVisionHealthMonitoring();
                    _isContinuousRunActive = false;
                    _applicationEventLogger.LogVisionProStatus(false, nameof(ManageJobStateAsync), "Vision system stopped from control buttons");
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (DataContext is MainViewModel mainVm)
                    {
                        mainVm.ControlButtonsVM.IsRunning = start && _isContinuousRunActive;
                        mainVm.UpdateControlButtonsState();
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Job state change error");
                _applicationEventLogger.LogException("MANAGE_JOB_STATE_FAILED", ex, nameof(ManageJobStateAsync), "Job state change error");
                new SystemNotificationWindow("Errore", $"Operazione fallita: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
            finally
            {
                _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                EndVisionStateTransition();
            }
        }

       
        public async Task ShutdownApplicationAsync(ShutdownProgressWindow progressWindow = null)
        {
            try
            {
                logger.Info("Inizio procedura di chiusura applicazione...");
                progressWindow?.UpdateStatus("Inizializzazione chiusura...", 0);

                // 1. Flag di shutdown PRIMA di tutto
                IsShuttingDown = true;
                App.ScheduleForcedProcessTermination(nameof(ShutdownApplicationAsync));

                // 2. Ferma timer (non dipende da altri)
                progressWindow?.UpdateStatus("Fermo timer...", 10);
                _statusTimer?.Stop();
                _statusTimer?.Dispose();
                StopVisionHealthMonitoring();
                _visionHealthTimer?.Dispose();

                try
                {
                    ServiceLocator.AutoRecipeSwitchService.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Warn($"Errore stop auto-switch service: {ex.Message}");
                }

                try
                {
                    ServiceLocator.AlarmService.AlarmTriggered -= OnAlarmTriggered;
                }
                catch (Exception ex)
                {
                    logger.Warn($"Errore dispose alarm manager: {ex.Message}");
                }

                // 3. Ferma sistema di visione (CRITICO: prima di Dispose ViewModel)
                progressWindow?.UpdateStatus("Fermo sistema di visione...", 20);
                if (_isContinuousRunActive && _cognexManager != null)
                {
                    _continuousRunCts?.Cancel();
                    try
                    {
                        await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync();
                        _isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"Errore stop visione: {ex.Message}");
                    }
                    await Task.Delay(1000);  // Attendi fermata completa
                }

                // 4. Chiudi servizi (prima di Dispose risorse)
                progressWindow?.UpdateStatus("Chiusura servizi...", 40);
                try { await ServiceLocator.ShutdownAllServicesAsync(); }
                catch (Exception ex) { logger.Warn($"Errore servizi: {ex.Message}"); }

                // 5. Unregister events (prima di Dispose _cognexManager)
                progressWindow?.UpdateStatus("Pulizia eventi...", 50);
                if (_eventManager != null)
                {
                    try
                    {
                        _eventManager.UnregisterEvents(MyJobManager_UserResultAvailableAsync, CognexManager_JobStopped);
                    }
                    catch (Exception ex) { logger.Warn($"Errore unregister: {ex.Message}"); }
                }

                // 6. Dispose risorse Cognex in ORDINE CORRETTO
                progressWindow?.UpdateStatus("Pulizia risorse Cognex...", 60);

                // Prima il manager (ferma live/job, rilascia risorse native)
                if (_cognexManager != null)
                {
                    try { _cognexManager.Dispose(); }
                    catch (Exception ex) { logger.Warn($"Errore dispose Cognex: {ex.Message}"); }
                    _cognexManager = null;
                    ServiceLocator.MachineRuntimeService.AttachCognexManager(null);
                }

                // Poi rilascia i riferimenti UI dei display senza distruggere i controlli ActiveX.
                SafeDisposeViews();

                // Poi pulisci riferimenti statici
                SafeDisposeCognexResources();

                // 7. Altri componenti
                progressWindow?.UpdateStatus("Pulizia componenti...", 70);
                if (_inspectionProcessor != null)
                {
                    try { await _inspectionProcessor.ShutdownAsync(); }
                    catch (Exception ex) { logger.Warn($"Errore inspection: {ex.Message}"); }
                }

                // 8. Token di cancellazione
                try
                {
                    SafeCancelTokenSource(_cts, nameof(_cts));
                    SafeCancelTokenSource(_continuousRunCts, nameof(_continuousRunCts));
                }
                catch (Exception ex) { logger.Warn(ex, "CANCELLATION_TOKEN_CANCEL_FAILED â€” non-critical"); }
                finally
                {
                    SafeDisposeTokenSource(_cts, nameof(_cts));
                    SafeDisposeTokenSource(_continuousRunCts, nameof(_continuousRunCts));
                    _cts = null;
                    _continuousRunCts = null;
                }

                // 9. Salva dati
                progressWindow?.UpdateStatus("Salvataggio dati...", 80);
                if (_saveImage != null)
                {
                    try { await _saveImage.FlushAsync(); }
                    catch (Exception ex) { logger.Warn($"Errore flush immagini: {ex.Message}"); }
                    try { (_saveImage as IDisposable)?.Dispose(); }
                    catch (Exception ex) { logger.Warn($"Errore dispose immagini: {ex.Message}"); }
                    _saveImage = null;
                }
                try { CounterManager?.SaveCountersToBackup(); }
                catch (Exception ex) { logger.Warn($"Errore contatori: {ex.Message}"); }

                try { await configManager?.SaveConfigAsync(); }
                catch (Exception ex) { logger.Warn($"Errore config: {ex.Message}"); }

                // 10. Chiudi finestre secondarie e ViewModel (dopo aver fermato tutto)
                progressWindow?.UpdateStatus("Chiusura finestre...", 85);
                CloseSecondaryWindows(progressWindow);

                progressWindow?.UpdateStatus("Pulizia ViewModel...", 90);
                if (DataContext is MainViewModel mainVm)
                {
                    try { mainVm.Dispose(); }
                    catch (Exception ex) { logger.Warn($"Errore VM: {ex.Message}"); }
                }

                try
                {
                    ServiceLocator.Reset();
                }
                catch (Exception ex)
                {
                    logger.Warn($"Errore reset servizi: {ex.Message}");
                }

                try
                {
                    Cognex.Vision.Startup.Shutdown();
                }
                catch (Exception ex)
                {
                    logger.Warn($"Errore shutdown runtime Cognex: {ex.Message}");
                }

                // 11. GC finale
                progressWindow?.UpdateStatus("Pulizia memoria...", 95);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(100);

                logger.Info("Applicazione pronta per la chiusura");
                progressWindow?.UpdateStatus("Completato!", 100);
                await Task.Delay(300);
            }
            catch (ObjectDisposedException ex)
            {
                logger.Warn($"Oggetto giÃ  dispose-ato durante la chiusura applicativa: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.Error($"Errore durante la chiusura: {ex.Message}");
            }
        }

        private static void SafeCancelTokenSource(CancellationTokenSource source, string name)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                logger?.Debug("TOKEN_CANCEL_SKIPPED_ALREADY_DISPOSED|name={0}", name);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "TOKEN_CANCEL_FAILED|name={0}", name);
            }
        }

        private static void SafeDisposeTokenSource(CancellationTokenSource source, string name)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Dispose();
            }
            catch (ObjectDisposedException)
            {
                logger?.Debug("TOKEN_DISPOSE_SKIPPED_ALREADY_DISPOSED|name={0}", name);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "TOKEN_DISPOSE_FAILED|name={0}", name);
            }
        }

        private void CloseSecondaryWindows(ShutdownProgressWindow progressWindow)
        {
            try
            {
                if (Application.Current?.Dispatcher == null)
                {
                    return;
                }

                Action closeAction = () =>
                {
                    var windowsToClose = Application.Current.Windows
                        .OfType<Window>()
                        .Where(window =>
                            window != null &&
                            window != this &&
                            window != progressWindow)
                        .ToList();

                    foreach (var window in windowsToClose)
                    {
                        try
                        {
                            if (window.IsVisible)
                            {
                                window.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Warn($"Errore chiusura finestra secondaria '{window.GetType().Name}': {ex.Message}");
                        }
                    }
                };

                if (Application.Current.Dispatcher.CheckAccess())
                {
                    closeAction();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(closeAction);
                }
            }
            catch (Exception ex)
            {
                logger.Warn($"Errore chiusura finestre secondarie: {ex.Message}");
            }
        }

        private void SafeDisposeViews()
        {
            try
            {
                // Usa Dispatcher per operazioni UI
                Dispatcher.Invoke(() =>
                {
                    TopCameraView.Instance?.dispose();
                    SideCameraView.Instance?.dispose();
                    LeftCameraView.Instance?.dispose();
                    FrontCameraView.Instance?.dispose();
                    RearCameraView.Instance?.dispose();
                    RightCameraView.Instance?.dispose();
                    BottomCameraView.Instance?.dispose();
                });
            }
            catch (Exception ex)
            {
                logger.Warn($"Errore dispose views: {ex.Message}");
            }
        }
    
        // Metodo specifico per Cognex - ORDINE CRITICO!
        private void SafeDisposeCognexResources()
        {
            try
            {
                // 1. Svuota le code (operazioni thread-safe)
                while (_topQueue?.TryDequeue(out _) == true) { }
                while (_leftQueue?.TryDequeue(out _) == true) { }
                while (_frontQueue?.TryDequeue(out _) == true) { }
                while (_rightQueue?.TryDequeue(out _) == true) { }
                while (_rearQueue?.TryDequeue(out _) == true) { }
                while (_bottomQueue?.TryDequeue(out _) == true) { }

                // 2. Rilascia riferimenti (NO dispose qui!)
                _topRecord = null;
                _sideRecord = null;
                _frontRecord = null;
                _rearRecord = null;
                _rightRecord = null;
                _bottomRecord = null;
                _topSaveRecord = null;
                _sideSaveRecord = null;
                _frontSaveRecord = null;
                _rearSaveRecord = null;
                _rightSaveRecord = null;
                _bottomSaveRecord = null;
                _topDisplaySaveRecord = null;
                _sideDisplaySaveRecord = null;
                _frontDisplaySaveRecord = null;
                _rearDisplaySaveRecord = null;
                _rightDisplaySaveRecord = null;
                _bottomDisplaySaveRecord = null;

                _topToolBlockReults = null;
                _sideToolBlockResults = null;
                _frontTBResults = null;
                _RearTBResults = null;
                _RightTBResults = null;
                _bottomTBResults = null;
                _topToolBlockMmpx = null;
                _sideToolBlockMmpx = null;
                _FrontTBMmpx = null;
                _RearTBMmpx = null;
                _BottomTBMmpx = null;

                _topToolGroup = null;
                _sideToolGroup = null;
                _BottomToolGroup = null;
                _RearToolGroup = null;
                _FrontToolGroup = null;

                // 3. Job references - NON dispose qui, giÃ  fatto da _cognexManager.Dispose()
                _topJob = null;
                _sideJob = null;
                _frontJob = null;
                _rearJob = null;
            _rightJob = null;
                _bottomJob = null;
            }
            catch (Exception ex)
            {
                logger?.Warn($"Errore in SafeDisposeCognexResources: {ex.Message}");
            }
        }
        #region User control autorization
        private void NotifyRoleChange()
        {
            
            // Notifica tutti i componenti che il ruolo Ã¨ cambiato
            UserSession.NotifyRoleChanged();

           
        }
        public void UpdateUIForCurrentUser()
        {
            if (DataContext is MainViewModel mainVm)
            {
                mainVm.TopMenuBarVM.CurrentUser = UserSession.CurrentUser;
                mainVm.TopMenuBarVM.CurrentUserRole = UserSession.CurrentRole;
                mainVm.TopMenuBarVM.IsLoggedIn = UserSession.IsLoggedIn; // Importante!

                // Aggiorna lo stato dei pulsanti di controllo
                mainVm.UpdateControlButtonsState();
                mainVm.OnNavigateRequested("Channel1");
                // Aggiorna il menu di navigazione
                NotifyRoleChange();
                MainWindow.logger?.Info($"UI aggiornata per utente: {UserSession.CurrentUser} ({UserSession.CurrentRole})");
            }
        }

        #endregion

        #region implementazione cambio ricetta automatico
        // Metodo chiamato quando arriva un risultato da VisionPro
        public void OnInspectionResult(string status, ICogImage currentImage)
        {
            ServiceLocator.AutoRecipeSwitchService.OnInspectionResult(status, currentImage);
        }
        #endregion
    }
}
