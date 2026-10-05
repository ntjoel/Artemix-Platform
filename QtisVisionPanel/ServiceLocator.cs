using QtisVisionPanel.Database;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QtisVisionPanel
{
    /// <summary>
    /// Minimal application-wide service registry.
    ///
    /// The project does not use a full dependency injection container, so this
    /// locator acts as the central bootstrap point for long-lived services that
    /// must be shared between WPF views, runtime orchestration and background tasks.
    /// </summary>
    public static class ServiceLocator
    {
        private static CounterManager _counterManager;
        private static InspectionConfigService _inspectionConfigService;
        private static readonly object _lock = new object();
        private static IntegratedAlarmCardService _alarmService;
        private static MachineRuntimeService _machineRuntimeService;
        private static DialogService _dialogService;
        private static ViewFactoryService _viewFactoryService;
        private static ApplicationEventLogger _applicationEventLogger;
        private static SystemDiagnosticsService _systemDiagnosticsService;
        private static OperatorInactivityService _operatorInactivityService;
        private static AuthorizationService _authorizationService;
        private static PowerFlex525Service _powerFlex525Service;
        private static IIODeviceManager _ioManager;
        private static OutputWatchdogService _outputWatchdog;
        private static AuditLogService _auditLogService;
        private static RecipeTransitionService _recipeTransitionService;
        private static AutoRecipeSwitchService _autoRecipeSwitchService;
        private static MachineSpeedService _machineSpeedService;
        private static IOpcUaClientService _opcUaClientService;
        private static MeasurementCaptureService _measurementCaptureService;
        private static HealthSnapshotService _healthSnapshotService;
        private static ProcessControlService _processControlService;
        private static PredictiveMaintenanceService _predictiveMaintenanceService;
        private static TrainingDataCollectionService _trainingDataCollectionService;
        private static OnnxDefectClassifier _defectClassifier;
        private static OnnxDefectClassifier _sideDefectClassifier;
        private static IReadOnlyList<OnnxDefectClassifier> _additionalDefectClassifiers;
        private static IoTimingOptimizerService _ioTimingOptimizer;
        private static AiPerformanceMonitorService _aiPerformanceMonitor;
        private static RecipeProductAdvisorService _recipeProductAdvisor;
        private static MachineHealthNotificationService _machineHealthNotificationService;

        /// <summary>
        /// The shared IO device manager. Set by DigitalIOViewModel on first initialization.
        /// Used by MainWindow to write physical alarm outputs regardless of which tab is open.
        /// Setting this property also (re-)creates the OutputWatchdogService.
        /// </summary>
        public static IIODeviceManager IoManager
        {
            get => _ioManager;
            set
            {
                _ioManager = value;
                // Ricrea il watchdog ogni volta che il device manager viene (ri)inizializzato
                _outputWatchdog?.Dispose();
                _outputWatchdog = value != null ? new OutputWatchdogService(value) : null;
            }
        }

        /// <summary>
        /// Watchdog che monitora uscite fisiche bloccate HIGH.
        /// Null finché IoManager non è inizializzato.
        /// </summary>
        public static OutputWatchdogService OutputWatchdog => _outputWatchdog;

        public static AuditLogService AuditLogService
        {
            get
            {
                if (_auditLogService == null)
                {
                    lock (_lock)
                    {
                        if (_auditLogService == null)
                            _auditLogService = new AuditLogService();
                    }
                }
                return _auditLogService;
            }
        }

        public static RecipeTransitionService RecipeTransitionService
        {
            get
            {
                if (_recipeTransitionService == null)
                {
                    lock (_lock)
                    {
                        if (_recipeTransitionService == null)
                            _recipeTransitionService = new RecipeTransitionService();
                    }
                }
                return _recipeTransitionService;
            }
        }

        public static AutoRecipeSwitchService AutoRecipeSwitchService
        {
            get
            {
                if (_autoRecipeSwitchService == null)
                {
                    lock (_lock)
                    {
                        if (_autoRecipeSwitchService == null)
                            _autoRecipeSwitchService = new AutoRecipeSwitchService();
                    }
                }
                return _autoRecipeSwitchService;
            }
        }

        public static MachineSpeedService MachineSpeedService
        {
            get
            {
                if (_machineSpeedService == null)
                {
                    lock (_lock)
                    {
                        if (_machineSpeedService == null)
                            _machineSpeedService = new MachineSpeedService();
                    }
                }
                return _machineSpeedService;
            }
        }

        public static IOpcUaClientService OpcUaClientService
        {
            get
            {
                if (_opcUaClientService == null)
                {
                    lock (_lock)
                    {
                        if (_opcUaClientService == null)
                            _opcUaClientService = new OpcUaClientService();
                    }
                }

                return _opcUaClientService;
            }
        }

        public static IntegratedAlarmCardService AlarmService
        {
            get
            {
                if (_alarmService == null)
                {
                    lock (_lock)
                    {
                        if (_alarmService == null)
                        {
                            _alarmService = new IntegratedAlarmCardService();
                        }
                    }
                }
                return _alarmService;
            }
        }
        public static MachineRuntimeService MachineRuntimeService
        {
            get
            {
                if (_machineRuntimeService == null)
                {
                    lock (_lock)
                    {
                        if (_machineRuntimeService == null)
                        {
                            _machineRuntimeService = new MachineRuntimeService();
                        }
                    }
                }
                return _machineRuntimeService;
            }
        }
        public static DialogService DialogService
        {
            get
            {
                if (_dialogService == null)
                {
                    lock (_lock)
                    {
                        if (_dialogService == null)
                        {
                            _dialogService = new DialogService();
                        }
                    }
                }
                return _dialogService;
            }
        }
        public static ViewFactoryService ViewFactoryService
        {
            get
            {
                if (_viewFactoryService == null)
                {
                    lock (_lock)
                    {
                        if (_viewFactoryService == null)
                        {
                            _viewFactoryService = new ViewFactoryService();
                        }
                    }
                }
                return _viewFactoryService;
            }
        }
        public static ApplicationEventLogger ApplicationEventLogger
        {
            get
            {
                if (_applicationEventLogger == null)
                {
                    lock (_lock)
                    {
                        if (_applicationEventLogger == null)
                        {
                            _applicationEventLogger = new ApplicationEventLogger();
                        }
                    }
                }
                return _applicationEventLogger;
            }
        }
        public static SystemDiagnosticsService SystemDiagnosticsService
        {
            get
            {
                if (_systemDiagnosticsService == null)
                {
                    lock (_lock)
                    {
                        if (_systemDiagnosticsService == null)
                        {
                            _systemDiagnosticsService = new SystemDiagnosticsService();
                        }
                    }
                }
                return _systemDiagnosticsService;
            }
        }
        public static OperatorInactivityService OperatorInactivityService
        {
            get
            {
                if (_operatorInactivityService == null)
                {
                    lock (_lock)
                    {
                        if (_operatorInactivityService == null)
                        {
                            _operatorInactivityService = new OperatorInactivityService();
                        }
                    }
                }
                return _operatorInactivityService;
            }
        }

        public static CounterManager CounterManager
        {
            get
            {
                if (_counterManager == null)
                {
                    lock (_lock)
                    {
                        if (_counterManager == null)
                        {
                            _counterManager = new CounterManager();
                        }
                    }
                }
                return _counterManager;
            }
        }
        public static InspectionConfigService InspectionConfigService
        {
            get
            {
                if (_inspectionConfigService == null)
                {
                    lock (_lock)
                    {
                        if (_inspectionConfigService == null)
                        {
                            _inspectionConfigService = new InspectionConfigService();
                        }
                    }
                }
                return _inspectionConfigService;
            }
        }

        public static AuthorizationService Authorization
        {
            get
            {
                if (_authorizationService == null)
                {
                    lock (_lock)
                    {
                        if (_authorizationService == null)
                        {
                            _authorizationService = new AuthorizationService();
                        }
                    }
                }
                return _authorizationService;
            }
        }

        public static PowerFlex525Service PowerFlex525Service
        {
            get
            {
                if (_powerFlex525Service == null)
                {
                    lock (_lock)
                    {
                        if (_powerFlex525Service == null)
                        {
                            _powerFlex525Service = new PowerFlex525Service();
                        }
                    }
                }
                return _powerFlex525Service;
            }
        }

        /// <summary>
        /// Fondazione dati AI (Fase 0): cattura misure strutturate di ispezione.
        /// </summary>
        public static MeasurementCaptureService MeasurementCaptureService
        {
            get
            {
                if (_measurementCaptureService == null)
                {
                    lock (_lock)
                    {
                        if (_measurementCaptureService == null)
                        {
                            _measurementCaptureService = new MeasurementCaptureService();
                        }
                    }
                }
                return _measurementCaptureService;
            }
        }

        /// <summary>
        /// Fondazione dati AI (Fase 0): snapshot periodici di salute PC.
        /// Avviare con <see cref="HealthSnapshotService.Start"/> dopo l'init del DB.
        /// </summary>
        public static HealthSnapshotService HealthSnapshotService
        {
            get
            {
                if (_healthSnapshotService == null)
                {
                    lock (_lock)
                    {
                        if (_healthSnapshotService == null)
                        {
                            _healthSnapshotService = new HealthSnapshotService();
                        }
                    }
                }
                return _healthSnapshotService;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 1): controllo statistico di processo e rilevamento deriva.
        /// </summary>
        public static ProcessControlService ProcessControlService
        {
            get
            {
                if (_processControlService == null)
                {
                    lock (_lock)
                    {
                        if (_processControlService == null)
                        {
                            _processControlService = new ProcessControlService();
                        }
                    }
                }
                return _processControlService;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 2): manutenzione predittiva sugli snapshot salute PC.
        /// </summary>
        public static PredictiveMaintenanceService PredictiveMaintenanceService
        {
            get
            {
                if (_predictiveMaintenanceService == null)
                {
                    lock (_lock)
                    {
                        if (_predictiveMaintenanceService == null)
                        {
                            _predictiveMaintenanceService = new PredictiveMaintenanceService();
                        }
                    }
                }
                return _predictiveMaintenanceService;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 3a): raccolta dati etichettati per il training vision.
        /// </summary>
        public static TrainingDataCollectionService TrainingDataCollectionService
        {
            get
            {
                if (_trainingDataCollectionService == null)
                {
                    lock (_lock)
                    {
                        if (_trainingDataCollectionService == null)
                        {
                            _trainingDataCollectionService = new TrainingDataCollectionService();
                        }
                    }
                }
                return _trainingDataCollectionService;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 3): classificatore difetti ONNX in shadow-mode.
        /// </summary>
        public static OnnxDefectClassifier DefectClassifier
        {
            get
            {
                if (_defectClassifier == null)
                {
                    lock (_lock)
                    {
                        if (_defectClassifier == null)
                        {
                            _defectClassifier = new OnnxDefectClassifier("top");
                        }
                    }
                }
                return _defectClassifier;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 3): classificatore difetti ONNX della camera SIDE (shadow-mode).
        /// </summary>
        public static OnnxDefectClassifier SideDefectClassifier
        {
            get
            {
                if (_sideDefectClassifier == null)
                {
                    lock (_lock)
                    {
                        if (_sideDefectClassifier == null)
                        {
                            _sideDefectClassifier = new OnnxDefectClassifier("side");
                        }
                    }
                }
                return _sideDefectClassifier;
            }
        }

        /// <summary>
        /// Classificatori ONNX delle altre camere (REAR/RIGHT, FRONT, BOTTOM), uno per ruolo
        /// in <see cref="Models.DefectClassifierCameraProfile.AdditionalRoles"/>. Senza modello
        /// abilitato ogni istanza e' un no-op.
        /// </summary>
        public static IReadOnlyList<OnnxDefectClassifier> AdditionalDefectClassifiers
        {
            get
            {
                if (_additionalDefectClassifiers == null)
                {
                    lock (_lock)
                    {
                        if (_additionalDefectClassifiers == null)
                        {
                            _additionalDefectClassifiers = Models.DefectClassifierCameraProfile.AdditionalRoles
                                .Select(role => new OnnxDefectClassifier(role))
                                .ToList()
                                .AsReadOnly();
                        }
                    }
                }
                return _additionalDefectClassifiers;
            }
        }

        /// <summary>Tutti i classificatori ONNX per camera: TOP, SIDE e le camere aggiuntive.</summary>
        public static IEnumerable<OnnxDefectClassifier> AllDefectClassifiers
        {
            get
            {
                yield return DefectClassifier;
                yield return SideDefectClassifier;
                foreach (OnnxDefectClassifier classifier in AdditionalDefectClassifiers)
                {
                    yield return classifier;
                }
            }
        }

        /// <summary>Classificatore della camera indicata (side/left, rear/right, ...), o null.</summary>
        public static OnnxDefectClassifier GetDefectClassifier(string cameraRole)
        {
            string role = Models.DefectClassifierCameraProfile.NormalizeRole(cameraRole);
            return AllDefectClassifiers.FirstOrDefault(classifier =>
                string.Equals(classifier?.CameraRole, role, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Integrazione AI (Fase 5): ottimizzatore timing IO/encoder (advisory + apply con conferma).
        /// </summary>
        public static IoTimingOptimizerService IoTimingOptimizer
        {
            get
            {
                if (_ioTimingOptimizer == null)
                {
                    lock (_lock)
                    {
                        if (_ioTimingOptimizer == null)
                        {
                            _ioTimingOptimizer = new IoTimingOptimizerService();
                        }
                    }
                }
                return _ioTimingOptimizer;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 6): monitor prestazioni ciclo ispezione.
        /// </summary>
        public static AiPerformanceMonitorService AiPerformanceMonitor
        {
            get
            {
                if (_aiPerformanceMonitor == null)
                {
                    lock (_lock)
                    {
                        if (_aiPerformanceMonitor == null)
                        {
                            _aiPerformanceMonitor = new AiPerformanceMonitorService();
                        }
                    }
                }
                return _aiPerformanceMonitor;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 7): advisor prodotto/ricetta solo advisory.
        /// </summary>
        public static RecipeProductAdvisorService RecipeProductAdvisor
        {
            get
            {
                if (_recipeProductAdvisor == null)
                {
                    lock (_lock)
                    {
                        if (_recipeProductAdvisor == null)
                        {
                            _recipeProductAdvisor = new RecipeProductAdvisorService();
                        }
                    }
                }
                return _recipeProductAdvisor;
            }
        }

        /// <summary>
        /// Integrazione AI (Fase 8): monitoraggio derive + notifica preallarmi al cliente.
        /// </summary>
        public static MachineHealthNotificationService MachineHealthNotificationService
        {
            get
            {
                if (_machineHealthNotificationService == null)
                {
                    lock (_lock)
                    {
                        if (_machineHealthNotificationService == null)
                        {
                            _machineHealthNotificationService = new MachineHealthNotificationService();
                        }
                    }
                }
                return _machineHealthNotificationService;
            }
        }

        /// <summary>
        /// Forces creation of the most important singleton services during startup.
        /// This reduces first-use latency on machine-side workflows.
        /// </summary>
        public static void Initialize()
        {
            // Forza la creazione del CounterManager
            var manager = CounterManager;
            var inspectionConfigService = InspectionConfigService;
            var runtimeService = MachineRuntimeService;
            var dialogService = DialogService;
            var viewFactoryService = ViewFactoryService;
            var applicationEventLogger = ApplicationEventLogger;
            var systemDiagnosticsService = SystemDiagnosticsService;
            var operatorInactivityService = OperatorInactivityService;
            var authorization = Authorization;
            var auditLog = AuditLogService;
            var autoRecipeSwitchService = AutoRecipeSwitchService;
            var machineSpeedService = MachineSpeedService;
            var opcUaClientService = OpcUaClientService;
            // Fondazione dati AI (Fase 0): crea il servizio di cattura misure e avvia
            // gli snapshot periodici di salute PC (primo tick differito ~45s, dopo l'init DB).
            var measurementCaptureService = MeasurementCaptureService;
            var processControlService = ProcessControlService;
            var predictiveMaintenanceService = PredictiveMaintenanceService;
            var trainingDataCollectionService = TrainingDataCollectionService;
            var defectClassifier = DefectClassifier;
            var sideDefectClassifier = SideDefectClassifier;
            var additionalDefectClassifiers = AdditionalDefectClassifiers;
            var ioTimingOptimizer = IoTimingOptimizer;
            var aiPerformanceMonitor = AiPerformanceMonitor;
            var recipeProductAdvisor = RecipeProductAdvisor;
            // Fase 8: avvia il monitor derive + notifiche DOPO che ProcessControl e
            // PredictiveMaintenance esistono, cosi' puo' sottoscriverne gli eventi.
            MachineHealthNotificationService.Start();
            HealthSnapshotService.Start();
            // PowerFlex 525 is an optional machine integration. Do not create or
            // poll it during application bootstrap, otherwise a development PC or
            // a machine without inverter network access could make startup fragile.
        }

        /// <summary>
        /// Drops all singleton references. Used mainly by shutdown/test scenarios.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                SafeDispose(_counterManager, nameof(_counterManager));
                _counterManager = null;
                _inspectionConfigService = null;
                _machineRuntimeService = null;
                _dialogService = null;
                _viewFactoryService = null;
                SafeDispose(_alarmService, nameof(_alarmService));
                _alarmService = null;
                SafeDispose(_systemDiagnosticsService, nameof(_systemDiagnosticsService));
                _systemDiagnosticsService = null;
                SafeDispose(_operatorInactivityService, nameof(_operatorInactivityService));
                _operatorInactivityService = null;
                SafeDispose(_applicationEventLogger, nameof(_applicationEventLogger));
                _applicationEventLogger = null;
                _authorizationService = null;
                _auditLogService = null;
                _recipeTransitionService = null;
                SafeDispose(_autoRecipeSwitchService, nameof(_autoRecipeSwitchService));
                _autoRecipeSwitchService = null;
                _machineSpeedService = null;
                SafeDispose(_opcUaClientService, nameof(_opcUaClientService));
                _opcUaClientService = null;
                SafeDispose(_powerFlex525Service, nameof(_powerFlex525Service));
                _powerFlex525Service = null;
                _measurementCaptureService = null;
                SafeDispose(_healthSnapshotService, nameof(_healthSnapshotService));
                _healthSnapshotService = null;
                _processControlService = null;
                _predictiveMaintenanceService = null;
                _trainingDataCollectionService = null;
                SafeDispose(_defectClassifier, nameof(_defectClassifier));
                _defectClassifier = null;
                SafeDispose(_sideDefectClassifier, nameof(_sideDefectClassifier));
                _sideDefectClassifier = null;
                foreach (OnnxDefectClassifier classifier in _additionalDefectClassifiers ?? new List<OnnxDefectClassifier>())
                {
                    SafeDispose(classifier, "_additionalDefectClassifiers:" + classifier?.CameraRole);
                }
                _additionalDefectClassifiers = null;
                _ioTimingOptimizer = null;
                _aiPerformanceMonitor = null;
                _recipeProductAdvisor = null;
                SafeDispose(_machineHealthNotificationService, nameof(_machineHealthNotificationService));
                _machineHealthNotificationService = null;
            }
        }

        private static void SafeDispose(object service, string name)
        {
            if (!(service is IDisposable disposable))
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (ObjectDisposedException)
            {
                MainWindow.logger?.Debug("SERVICE_RESET_DISPOSE_SKIPPED_ALREADY_DISPOSED|service={0}", name);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "SERVICE_RESET_DISPOSE_FAILED|service={0}", name);
            }
        }

        /// <summary>
        /// Gracefully shuts down services that participate in coordinated
        /// application stop, especially timers/background workers.
        /// </summary>
        public static async Task ShutdownAllServicesAsync()
        {
            try
            {
                MainWindow.logger?.Info("Shutdown di tutti i servizi...");
                ApplicationEventLogger.LogLifecycle("SERVICES_SHUTDOWN_START", "Shutdown di tutti i servizi", nameof(ShutdownAllServicesAsync));

                var counterManager = _counterManager;
                var diagnosticsService = _systemDiagnosticsService;
                var inactivityService = _operatorInactivityService;
                var opcUaClientService = _opcUaClientService;

                if (counterManager is IDisposableService disposableService)
                {
                    await disposableService.ShutdownAsync();
                }
                else
                {
                    SafeDispose(counterManager, nameof(_counterManager));
                }

                if (diagnosticsService is IDisposableService diagnosticsDisposable)
                {
                    await diagnosticsDisposable.ShutdownAsync();
                }

                if (inactivityService is IDisposableService inactivityDisposable)
                {
                    await inactivityDisposable.ShutdownAsync();
                }

                if (opcUaClientService != null)
                {
                    await opcUaClientService.DisconnectAsync();
                    SafeDispose(opcUaClientService, nameof(_opcUaClientService));
                }

                lock (_lock)
                {
                    if (ReferenceEquals(_counterManager, counterManager))
                    {
                        _counterManager = null;
                    }

                    if (ReferenceEquals(_systemDiagnosticsService, diagnosticsService))
                    {
                        _systemDiagnosticsService = null;
                    }

                    if (ReferenceEquals(_operatorInactivityService, inactivityService))
                    {
                        _operatorInactivityService = null;
                    }

                    if (ReferenceEquals(_opcUaClientService, opcUaClientService))
                    {
                        _opcUaClientService = null;
                    }
                }

                MainWindow.logger?.Info("Tutti i servizi sono stati chiusi");
                ApplicationEventLogger.LogLifecycle("SERVICES_SHUTDOWN_COMPLETE", "Tutti i servizi sono stati chiusi", nameof(ShutdownAllServicesAsync));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nello shutdown dei servizi: {ex.Message}");
                ApplicationEventLogger.LogException("SERVICES_SHUTDOWN_FAILED", ex, nameof(ShutdownAllServicesAsync), "Errore nello shutdown dei servizi");
            }
        }
    }
}
