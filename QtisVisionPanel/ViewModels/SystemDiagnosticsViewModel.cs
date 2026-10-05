using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace QtisVisionPanel.ViewModels
{
    public class SystemDiagnosticsViewModel : INotifyPropertyChanged, IDisposable
    {
        private const int MaxDefectClassifierInputSize = 1024;
        private readonly SystemDiagnosticsService _diagnosticsService = ServiceLocator.SystemDiagnosticsService;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }
        public ICommand OpenEventsCommand { get; }

        public ObservableCollection<SystemDriveStatusItem> Drives => _diagnosticsService.Drives;
        public ObservableCollection<SystemDiagnosticsAlertItem> ActiveAlerts => _diagnosticsService.ActiveAlerts;
        public ObservableCollection<TemperatureSensorStatusItem> TemperatureSensors => _diagnosticsService.TemperatureSensors;
        public TemperatureSensorStatusItem HottestCpuTemperatureSensor => _diagnosticsService.HottestCpuTemperatureSensor;
        public TemperatureSensorStatusItem HottestDiskTemperatureSensor => _diagnosticsService.HottestDiskTemperatureSensor;
        public TemperatureSensorStatusItem MemoryTemperatureSensor => _diagnosticsService.MemoryTemperatureSensor;
        public DatabaseArchiveStatusItem DatabaseArchiveStatus => _diagnosticsService.DatabaseArchiveStatus;

        public double CpuUsagePercent => _diagnosticsService.CpuUsagePercent;
        public string CpuUsageText => _diagnosticsService.CpuUsageText;
        public double MemoryUsagePercent => _diagnosticsService.MemoryUsagePercent;
        public string MemoryUsageText => _diagnosticsService.MemoryUsageText;
        public DiagnosticsSeverity OverallSeverity => _diagnosticsService.OverallSeverity;
        public string OverallStatusText => _diagnosticsService.OverallStatusText;
        public Brush OverallStatusBrush => _diagnosticsService.OverallStatusBrush;
        public string WorstDiskText => _diagnosticsService.WorstDiskText;
        public string CleanupRootsText => _diagnosticsService.CleanupRootsText;
        public string CleanupPolicyText => _diagnosticsService.CleanupPolicyText;
        public string LastCleanupText => _diagnosticsService.LastCleanupText;
        public string LastUpdatedText => _diagnosticsService.LastUpdatedText;
        public string LastUpdatedDisplayText => string.Format("{0}: {1}", GetMessage("Sub_entry_LastUpdate", "Last update"), LastUpdatedText);
        public int ActiveAlertCount => _diagnosticsService.ActiveAlertCount;
        public int CriticalAlertCount => _diagnosticsService.CriticalAlertCount;
        public int WarningAlertCount => _diagnosticsService.WarningAlertCount;
        public bool HasAlerts => _diagnosticsService.HasActiveAlerts;
        public string ActiveAlertSummary => _diagnosticsService.ActiveAlertSummary;

        // --- Runtime machine metrics ---
        public double CurrentPpm => ServiceLocator.CounterManager?.GetCurrentPpm() ?? 0.0;
        public string CurrentPpmText => $"{CurrentPpm:F0} ppm";

        // --- System warning banner data (for MainWindow banner) ---
        public bool HasSystemWarning => OverallSeverity != DiagnosticsSeverity.Healthy;
        public string SystemWarningText
        {
            get
            {
                if (!HasSystemWarning) return null;
                return OverallSeverity == DiagnosticsSeverity.Critical
                    ? $"CRITICO: {ActiveAlertSummary}"
                    : $"Avviso: {ActiveAlertSummary}";
            }
        }

        // --- Fase 5: ottimizzatore timing IO/encoder (advisory + apply con conferma) ---
        private readonly IoTimingOptimizerService _ioTimingOptimizer = ServiceLocator.IoTimingOptimizer;
        private string _timingApplyStatus;

        public ObservableCollection<TimingAdvisoryItem> TimingAdvisories { get; } = new ObservableCollection<TimingAdvisoryItem>();
        public ObservableCollection<TimingParameterItem> TimingParameters { get; } = new ObservableCollection<TimingParameterItem>();
        public bool CanApplyTimingParameters { get; private set; }
        public bool HasTimingAdvisories => TimingAdvisories.Count > 0;
        public string TimingApplyStatus
        {
            get => _timingApplyStatus;
            private set { _timingApplyStatus = value; OnPropertyChanged(); }
        }
        public ICommand RefreshTimingCommand { get; }
        public ICommand ApplyTimingParameterCommand { get; }

        // --- Controlli AI: abilitazione/configurazione da pannello dei servizi AI ---
        // La UI binda direttamente su AiSettings (RuntimeBindings): il salvataggio passa dal
        // Save atomico di MachineConfigurationService e riapplica i flag/modello a caldo via
        // RefreshConfiguration() dei servizi.
        private MachineRuntimeConfiguration _aiConfig;
        private string _aiSettingsStatus;

        public MachineRuntimeBindings AiSettings => _aiConfig?.RuntimeBindings;
        public string AiSettingsStatus
        {
            get => _aiSettingsStatus;
            private set { _aiSettingsStatus = value; OnPropertyChanged(); }
        }
        public ICommand ReloadAiSettingsCommand { get; }
        public ICommand SaveAiSettingsCommand { get; }

        // --- Fase 8: pannello preallarmi salute macchina ---
        public ObservableCollection<EarlyWarning> EarlyWarnings { get; } = new ObservableCollection<EarlyWarning>();
        public bool HasEarlyWarnings => EarlyWarnings.Count > 0;
        public ICommand RefreshEarlyWarningsCommand { get; }

        // --- Fase 3: statistiche shadow-mode del classificatore ONNX ---
        private ShadowModeStats _shadowStats;
        public ShadowModeStats ShadowStats
        {
            get => _shadowStats;
            private set
            {
                _shadowStats = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasShadowStats));
                OnPropertyChanged(nameof(HasAnyShadowStats));
            }
        }
        public bool HasShadowStats => _shadowStats?.HasData == true;

        private ShadowModeStats _sideShadowStats;
        public ShadowModeStats SideShadowStats
        {
            get => _sideShadowStats;
            private set
            {
                _sideShadowStats = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSideShadowStats));
                OnPropertyChanged(nameof(HasAnyShadowStats));
            }
        }
        public bool HasSideShadowStats => _sideShadowStats?.HasData == true;
        public bool HasAnyShadowStats =>
            HasShadowStats || HasSideShadowStats || AdditionalClassifierProfiles.Any(item => item.HasStats);

        /// <summary>
        /// Classificatori delle altre camere (REAR/RIGHT, FRONT, BOTTOM): una card per camera con
        /// profilo, training e statistiche. Ricostruita a ogni caricamento delle impostazioni AI.
        /// </summary>
        public ObservableCollection<DefectClassifierProfileItem> AdditionalClassifierProfiles { get; } =
            new ObservableCollection<DefectClassifierProfileItem>();

        // Card TOP e SIDE: forniscono solo intestazione, presenza nel VPP e stato del menu.
        // I campi restano legati ai campi dedicati di AiSettings (compatibilita' configurazione).
        private DefectClassifierProfileItem _topClassifierCard;
        private DefectClassifierProfileItem _sideClassifierCard;

        public DefectClassifierProfileItem TopClassifierCard
        {
            get => _topClassifierCard;
            private set { _topClassifierCard = value; OnPropertyChanged(); }
        }

        public DefectClassifierProfileItem SideClassifierCard
        {
            get => _sideClassifierCard;
            private set { _sideClassifierCard = value; OnPropertyChanged(); }
        }

        // Stato aperto/chiuso scelto dall'operatore per camera: sopravvive a Salva/Ricarica,
        // che ricostruiscono le card dalla configurazione.
        private readonly System.Collections.Generic.Dictionary<string, bool> _classifierExpandedByRole =
            new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public OnnxFieldLabels OnnxLabels { get; } = new OnnxFieldLabels();

        public ICommand RefreshShadowStatsCommand { get; }
        public ICommand ResetShadowStatsCommand { get; }

        // --- Fase 3b: training on-demand del classificatore ONNX (script Python esterno) ---
        private readonly DefectClassifierTrainingService _trainingService = new DefectClassifierTrainingService();
        private CancellationTokenSource _trainingCts;
        private string _trainingStatus;
        private bool _isTrainingRunning;
        private bool _isPythonCheckRunning;

        public string TrainingStatus
        {
            get => _trainingStatus;
            private set { _trainingStatus = value; OnPropertyChanged(); }
        }

        public bool IsTrainingRunning
        {
            get => _isTrainingRunning;
            private set
            {
                _isTrainingRunning = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsPythonCheckRunning
        {
            get => _isPythonCheckRunning;
            private set
            {
                _isPythonCheckRunning = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // --- Fase 9: test invio email (canale Email/SMTP dei preallarmi) ---
        private bool _isEmailTestRunning;
        public bool IsEmailTestRunning
        {
            get => _isEmailTestRunning;
            private set
            {
                _isEmailTestRunning = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ICommand TrainDefectClassifierCommand { get; }
        public ICommand TrainSideDefectClassifierCommand { get; }
        /// <summary>Training della camera passata come CommandParameter (rear, front, bottom).</summary>
        public ICommand TrainCameraDefectClassifierCommand { get; }
        public ICommand TestPythonCommand { get; }
        public ICommand TestEmailCommand { get; }
        public ICommand CancelTrainingCommand { get; }

        public SystemDiagnosticsViewModel()
        {
            RefreshCommand = new RelayCommand(async => _diagnosticsService.RefreshNowAsync().SafeFireAndForget());
            OpenEventsCommand = new RelayCommand(OpenEvents);

            CanApplyTimingParameters = IsTimingApplyAuthorized();
            RefreshTimingCommand = new RelayCommand(_ => RefreshTiming());
            ApplyTimingParameterCommand = new RelayCommand(ApplyTimingParameter);
            RefreshTiming();

            // Ricarica/Salva bloccati durante il training: LoadAiSettings sostituirebbe _aiConfig
            // sotto i piedi del training in corso (il writeback del modello finirebbe su un'istanza
            // diversa da quella usata per addestrare, con preprocessing potenzialmente incoerente).
            ReloadAiSettingsCommand = new RelayCommand(_ => LoadAiSettings(), _ => !IsTrainingRunning && !IsPythonCheckRunning);
            SaveAiSettingsCommand = new RelayCommand(_ => SaveAiSettings(), _ => !IsTrainingRunning && !IsPythonCheckRunning);
            RefreshEarlyWarningsCommand = new RelayCommand(_ => RefreshEarlyWarnings());
            TrainDefectClassifierCommand = new RelayCommand(_ => TrainDefectClassifier("top"), _ => !IsTrainingRunning && !IsPythonCheckRunning && CanApplyTimingParameters);
            TrainSideDefectClassifierCommand = new RelayCommand(_ => TrainDefectClassifier("side"), _ => !IsTrainingRunning && !IsPythonCheckRunning && CanApplyTimingParameters);
            // Il ruolo arriva come CommandParameter; un ruolo non valido viene scartato
            // all'avvio del training (non in CanExecute, che WPF puo' valutare prima del parametro).
            TrainCameraDefectClassifierCommand = new RelayCommand(
                role => TrainDefectClassifier(role as string),
                _ => !IsTrainingRunning && !IsPythonCheckRunning && CanApplyTimingParameters);
            TestPythonCommand = new RelayCommand(_ => TestPython(), _ => !IsTrainingRunning && !IsPythonCheckRunning && CanApplyTimingParameters);
            TestEmailCommand = new RelayCommand(_ => TestEmail(), _ => !IsEmailTestRunning && CanApplyTimingParameters);
            CancelTrainingCommand = new RelayCommand(_ => CancelTraining(), _ => IsTrainingRunning);
            RefreshShadowStatsCommand = new RelayCommand(_ => RefreshShadowStats());
            ResetShadowStatsCommand = new RelayCommand(_ => ResetShadowStats(), _ => CanApplyTimingParameters);
            LoadAiSettings();
            RefreshEarlyWarnings();
            RefreshShadowStats();

            _diagnosticsService.PropertyChanged += DiagnosticsService_PropertyChanged;
            _diagnosticsService.Start();
        }

        private void OpenEvents(object p)
        {
            if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
            {
                mainVm.OnNavigateRequested("Alarmsview");
            }
        }

        // --- Fase 5: timing optimizer ---

        private static bool IsTimingApplyAuthorized()
        {
            string role = UserSession.CurrentRole ?? string.Empty;
            return role.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Installer", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshTiming()
        {
            TimingAdvisories.Clear();
            if (_ioTimingOptimizer != null)
            {
                foreach (var rec in _ioTimingOptimizer.GetRecommendations())
                {
                    TimingAdvisories.Add(new TimingAdvisoryItem
                    {
                        Message = rec.Message,
                        Severity = rec.Severity
                    });
                }
            }
            OnPropertyChanged(nameof(HasTimingAdvisories));

            MachineRuntimeBindings bindings = null;
            try { bindings = new MachineConfigurationService().Load()?.RuntimeBindings; }
            catch { bindings = null; }

            TimingParameters.Clear();
            foreach (var kv in IoTimingOptimizerService.ApplicableParameters)
            {
                int current = GetCurrentTimingValue(bindings, kv.Key);
                TimingParameters.Add(new TimingParameterItem
                {
                    Name = kv.Key,
                    Min = kv.Value.Min,
                    Max = kv.Value.Max,
                    CurrentValue = current,
                    TargetValue = current.ToString()
                });
            }
        }

        private static int GetCurrentTimingValue(MachineRuntimeBindings b, string name)
        {
            if (b == null) return 0;
            switch (name)
            {
                case "TopTriggerBaseDelayMs": return b.TopTriggerBaseDelayMs;
                case "SideTriggerBaseDelayMs": return b.SideTriggerBaseDelayMs;
                case "TopTriggerPulseMs": return b.TopTriggerPulseMs;
                case "SideTriggerPulseMs": return b.SideTriggerPulseMs;
                case "PhotocellDebounceMs": return b.PhotocellDebounceMs;
                case "MinimumRetriggerGapMs": return b.MinimumRetriggerGapMs;
                default: return 0;
            }
        }

        private void ApplyTimingParameter(object parameter)
        {
            if (!(parameter is TimingParameterItem item) || _ioTimingOptimizer == null)
                return;

            if (!CanApplyTimingParameters)
            {
                new SystemNotificationWindow("Autorizzazione richiesta",
                    "Solo Installer o Administrator possono applicare i parametri di timing.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            if (!int.TryParse(item.TargetValue, out int value))
            {
                new SystemNotificationWindow("Valore non valido",
                    $"Inserire un intero valido per {item.Name}.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            var confirm = new SystemNotificationWindow("Applica parametro timing",
                $"Confermi la modifica di {item.Name}?\n{item.CurrentValue} -> {value}  (range consentito {item.Min}-{item.Max})\nIl valore precedente viene salvato come backup.",
                NotificationSeverity.Warning, true);
            confirm.ShowDialog();
            if (!confirm.Confirmed)
                return;

            var result = _ioTimingOptimizer.ApplyTimingParameter(item.Name, value, UserSession.CurrentUser);
            TimingApplyStatus = result.Message;

            new SystemNotificationWindow(result.Success ? "Parametro applicato" : "Applicazione non riuscita",
                result.Message,
                result.Success ? NotificationSeverity.Info : NotificationSeverity.Warning).ShowDialog();

            if (result.Success)
            {
                item.CurrentValue = result.NewValue;
                item.TargetValue = result.NewValue.ToString();
            }
        }

        // --- Controlli AI ---

        private void LoadAiSettings()
        {
            try
            {
                var service = new MachineConfigurationService();
                _aiConfig = service.Load();
                AiSettingsStatus = _aiConfig == null
                    ? "Configurazione runtime non disponibile."
                    : $"File macchina attivo: {service.GetConfigurationFilePath()}";
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "AI_SETTINGS_LOAD_FAILED");
                _aiConfig = null;
                AiSettingsStatus = "Errore lettura configurazione.";
            }

            OnPropertyChanged(nameof(AiSettings));
            RebuildAdditionalClassifierProfiles();
        }

        // Le card legano i campi al profilo della configurazione appena caricata: salvataggio e
        // precompilazione dopo il training scrivono quindi direttamente su _aiConfig.
        private void RebuildAdditionalClassifierProfiles()
        {
            var bindings = _aiConfig?.RuntimeBindings;
            TopClassifierCard = CreateClassifierCard(
                bindings?.ResolveDefectClassifierProfile("top") ?? new DefectClassifierCameraProfile { CameraRole = "top" });
            SideClassifierCard = CreateClassifierCard(
                bindings?.ResolveDefectClassifierProfile("side") ?? new DefectClassifierCameraProfile { CameraRole = "side" });

            AdditionalClassifierProfiles.Clear();
            var profiles = bindings?.AdditionalDefectClassifierProfiles;
            if (profiles != null)
            {
                foreach (DefectClassifierCameraProfile profile in profiles)
                {
                    DefectClassifierProfileItem item = CreateClassifierCard(profile);
                    item.Stats = ServiceLocator.GetDefectClassifier(profile.CameraRole)?.GetShadowStats();
                    AdditionalClassifierProfiles.Add(item);
                }
            }

            OnPropertyChanged(nameof(HasAnyShadowStats));
        }

        private DefectClassifierProfileItem CreateClassifierCard(DefectClassifierCameraProfile profile)
        {
            var item = new DefectClassifierProfileItem(profile);
            if (_classifierExpandedByRole.TryGetValue(item.CameraRole, out bool expanded))
            {
                item.IsExpanded = expanded;
            }

            item.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(DefectClassifierProfileItem.IsExpanded))
                {
                    _classifierExpandedByRole[item.CameraRole] = item.IsExpanded;
                }
            };
            return item;
        }

        /// <summary>Ricarica lo snapshot dei preallarmi salute macchina (Fase 8) per la UI.</summary>
        private void RefreshEarlyWarnings()
        {
            try
            {
                EarlyWarnings.Clear();
                var recent = ServiceLocator.MachineHealthNotificationService?.GetRecentWarnings();
                if (recent != null)
                {
                    foreach (var w in recent)
                        EarlyWarnings.Add(w);
                }

                // Aprire/aggiornare questo pannello azzera il badge proattivo in TopMenuBar.
                ServiceLocator.MachineHealthNotificationService?.MarkWarningsSeen();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "EARLY_WARNINGS_REFRESH_FAILED");
            }

            OnPropertyChanged(nameof(HasEarlyWarnings));
        }

        /// <summary>Ricarica lo snapshot delle statistiche shadow-mode del classificatore ONNX.</summary>
        private void RefreshShadowStats()
        {
            try
            {
                ShadowStats = ServiceLocator.DefectClassifier?.GetShadowStats();
                SideShadowStats = ServiceLocator.SideDefectClassifier?.GetShadowStats();
                foreach (DefectClassifierProfileItem item in AdditionalClassifierProfiles)
                {
                    item.Stats = ServiceLocator.GetDefectClassifier(item.CameraRole)?.GetShadowStats();
                }

                OnPropertyChanged(nameof(HasAnyShadowStats));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "SHADOW_STATS_REFRESH_FAILED");
            }
        }

        private void ResetShadowStats()
        {
            try
            {
                foreach (OnnxDefectClassifier classifier in ServiceLocator.AllDefectClassifiers)
                {
                    classifier?.ResetShadowStats();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "SHADOW_STATS_RESET_FAILED");
            }
            RefreshShadowStats();
        }

        // --- Fase 3b: training on-demand del classificatore ONNX ---

        private void TrainDefectClassifier(string cameraRole)
        {
            TrainDefectClassifierCoreAsync(cameraRole).SafeFireAndForget();
        }

        private void TestPython()
        {
            TestPythonCoreAsync().SafeFireAndForget();
        }

        private void CancelTraining()
        {
            try
            {
                _trainingCts?.Cancel();
                TrainingStatus = "Annullamento training in corso...";
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task TrainDefectClassifierCoreAsync(string cameraRole)
        {
            cameraRole = DefectClassifierCameraProfile.NormalizeRole(cameraRole);
            if (string.IsNullOrEmpty(cameraRole))
                return;

            // Nome della camera come nel VPP (TOP, SIDE, REAR, ...), non la chiave interna.
            string cameraLabel = CameraConfigurationHelper.GetCameraRoleDisplayLabel(cameraRole);

            if (IsTrainingRunning)
                return;

            Keyboard.ClearFocus();

            if (!CanApplyTimingParameters)
            {
                new SystemNotificationWindow("Autorizzazione richiesta",
                    "Solo Installer o Administrator possono addestrare il modello.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            if (_aiConfig?.RuntimeBindings == null)
            {
                LoadAiSettings();
                if (_aiConfig?.RuntimeBindings == null)
                    return;
            }

            if (!ValidateTrainingSettingsBeforeRun(cameraRole, out var trainingValidationMessage))
            {
                TrainingStatus = trainingValidationMessage;
                new SystemNotificationWindow("Training AI non valido",
                    trainingValidationMessage,
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            // Il training e' CPU-intensivo: non deve competere con l'ispezione in produzione.
            if (ServiceLocator.MachineRuntimeService?.IsContinuousRunActive == true)
            {
                TrainingStatus = "Training non avviato: fermare la produzione prima di addestrare il modello.";
                new SystemNotificationWindow("Macchina in produzione",
                    "Il training del modello è CPU-intensivo: fermare la produzione e riprovare.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            var confirm = new SystemNotificationWindow($"Addestra modello difetti — {cameraLabel}",
                $"Avvio del training del classificatore difetti camera {cameraLabel} dal dataset raccolto (label.json).\n" +
                "Il training gira in un processo Python separato e può durare diversi minuti.\n" +
                "Il modello prodotto NON viene abilitato automaticamente: andrà verificato e salvato.",
                NotificationSeverity.Warning, true);
            confirm.ShowDialog();
            if (!confirm.Confirmed)
                return;

            // Ricontrollo DOPO il dialog: ShowDialog pompa messaggi (re-entrancy WPF), quindi un
            // secondo click su "Addestra" puo' essere arrivato fin qui mentre questo era bloccato
            // nel dialog. Senza questo check partirebbero due training in parallelo.
            if (IsTrainingRunning)
            {
                TrainingStatus = "Un training è già in corso: attendere il completamento.";
                return;
            }

            IsTrainingRunning = true;
            _trainingCts = new CancellationTokenSource();
            var progress = new Progress<string>(message =>
            {
                TrainingStatus = message;
                AiSettingsStatus = message;
            });

            try
            {
                var outcome = await _trainingService.TrainAsync(AiSettings, cameraRole, progress, _trainingCts.Token);

                if (outcome.Success)
                {
                    // Precompila il pannello ONNX della camera addestrata (NON salvato finche' l'operatore
                    // non preme "Salva"): il modello resta un'azione supervisionata.
                    ApplyTrainedModelToConfig(cameraRole, outcome);

                    TrainingStatus = string.Format(
                        "Modello {0} creato: {1} (val_acc={2:0.###}, NOK recall={3:0.###}, validation={4}, holdout OK={5}/NOK={6}). {7} Verificare i campi ONNX e premere \"Salva\" per attivarlo in shadow-mode.",
                        cameraLabel, outcome.ModelPath, outcome.ValAccuracy ?? 0, outcome.ValNokRecall ?? 0,
                        outcome.ValidationStatus, outcome.ValidationOkCount, outcome.ValidationNokCount, outcome.Message);
                    AiSettingsStatus = TrainingStatus;

                    ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                        outcome.ValidationQualified ? NLog.LogLevel.Info : NLog.LogLevel.Warn,
                        "AI_MODEL_TRAINED",
                        "AI Vision",
                        $"Modello classificatore addestrato: {outcome.ModelPath}",
                        nameof(SystemDiagnosticsViewModel),
                        $"actor={UserSession.CurrentUser}; val_acc={outcome.ValAccuracy:0.###}; nok_recall={outcome.ValNokRecall:0.###}; ok={outcome.OkCount}; nok={outcome.NokCount}; validation={outcome.ValidationStatus}; val_ok={outcome.ValidationOkCount}; val_nok={outcome.ValidationNokCount}; strategy={outcome.ValidationStrategy}",
                        null);

                    new SystemNotificationWindow(
                        outcome.ValidationQualified ? "Training AI completato" : "Training AI completato - validazione provvisoria",
                        TrainingStatus,
                        outcome.ValidationQualified ? NotificationSeverity.Info : NotificationSeverity.Warning).ShowDialog();
                }
                else
                {
                    TrainingStatus = outcome.Message;
                    AiSettingsStatus = outcome.Message;
                    new SystemNotificationWindow("Training AI non completato",
                        outcome.Message,
                        NotificationSeverity.Warning).ShowDialog();
                }
            }
            finally
            {
                DisposeTrainingCancellation();
                IsTrainingRunning = false;
            }
        }

        // Scrive il modello appena addestrato nei campi ONNX della camera giusta (non salva su file:
        // l'attivazione richiede il pulsante "Salva").
        private void ApplyTrainedModelToConfig(string cameraRole, DefectClassifierTrainingService.TrainingOutcome outcome)
        {
            var b = _aiConfig?.RuntimeBindings;
            if (b == null)
                return;

            string notes = string.Format(
                "Training {0:yyyy-MM-dd HH:mm} | camera={1} | dataset OK={2} NOK={3} | val_acc={4:0.###} | NOK recall={5:0.###} | validation={6} ({7}, holdout OK={8} NOK={9})",
                DateTime.Now, cameraRole, outcome.OkCount, outcome.NokCount, outcome.ValAccuracy ?? 0,
                outcome.ValNokRecall ?? 0, outcome.ValidationStatus, outcome.ValidationStrategy,
                outcome.ValidationOkCount, outcome.ValidationNokCount);

            string role = DefectClassifierCameraProfile.NormalizeRole(cameraRole);
            if (role == "side")
            {
                b.SideDefectClassifierModelPath = outcome.ModelPath;
                b.SideDefectClassifierModelVersion = outcome.ModelVersion;
                b.SideDefectClassifierModelNotes = notes;
            }
            else if (role == "top")
            {
                b.DefectClassifierModelPath = outcome.ModelPath;
                b.DefectClassifierModelVersion = outcome.ModelVersion;
                b.DefectClassifierModelNotes = notes;
            }
            else
            {
                // Profilo in lista: e' lo stesso oggetto legato alla card, che si aggiorna da sola.
                DefectClassifierCameraProfile profile = b.ResolveDefectClassifierProfile(role);
                if (profile != null)
                {
                    profile.ModelPath = outcome.ModelPath;
                    profile.ModelVersion = outcome.ModelVersion;
                    profile.ModelNotes = notes;
                }
            }

            OnPropertyChanged(nameof(AiSettings));
        }

        private async Task TestPythonCoreAsync()
        {
            if (IsPythonCheckRunning || IsTrainingRunning)
                return;

            Keyboard.ClearFocus();

            if (!CanApplyTimingParameters)
            {
                new SystemNotificationWindow("Autorizzazione richiesta",
                    "Solo Installer o Administrator possono verificare Python.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            if (_aiConfig?.RuntimeBindings == null)
            {
                LoadAiSettings();
                if (_aiConfig?.RuntimeBindings == null)
                    return;
            }

            if (!ValidateTrainingSettingsBeforeRun("top", out var validationMessage, validateOutputDirectory: false))
            {
                TrainingStatus = validationMessage;
                new SystemNotificationWindow("Configurazione Python non valida",
                    validationMessage,
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            IsPythonCheckRunning = true;
            var progress = new Progress<string>(message =>
            {
                TrainingStatus = message;
                AiSettingsStatus = message;
            });

            try
            {
                var outcome = await _trainingService.TestPythonAsync(AiSettings, progress, CancellationToken.None);
                TrainingStatus = outcome.Message;
                AiSettingsStatus = outcome.Message;

                new SystemNotificationWindow(outcome.Success ? "Python pronto" : "Python non pronto",
                    outcome.Message,
                    outcome.Success ? NotificationSeverity.Info : NotificationSeverity.Warning).ShowDialog();
            }
            finally
            {
                IsPythonCheckRunning = false;
            }
        }

        private void TestEmail()
        {
            TestEmailCoreAsync().SafeFireAndForget();
        }

        /// <summary>
        /// Invia un'email di prova usando esattamente il percorso reale di invio
        /// (<see cref="EmailNotificationChannel"/>), cosi' il test verifica l'intera catena
        /// (SMTP/host/porta/credenziali/destinatari), non solo la connettivita' a parte.
        /// </summary>
        private async Task TestEmailCoreAsync()
        {
            if (IsEmailTestRunning)
                return;

            Keyboard.ClearFocus();

            if (!CanApplyTimingParameters)
            {
                new SystemNotificationWindow("Autorizzazione richiesta",
                    "Solo Installer o Administrator possono inviare un'email di prova.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            if (_aiConfig?.RuntimeBindings == null)
            {
                LoadAiSettings();
                if (_aiConfig?.RuntimeBindings == null)
                    return;
            }

            var bindings = _aiConfig.RuntimeBindings;
            if (string.IsNullOrWhiteSpace(bindings.EmailSmtpHost) ||
                string.IsNullOrWhiteSpace(bindings.EmailFromAddress) ||
                string.IsNullOrWhiteSpace(bindings.EmailToAddresses))
            {
                string message = "Configurazione email incompleta: compilare host SMTP, mittente e almeno un destinatario prima di inviare la prova.";
                AiSettingsStatus = message;
                new SystemNotificationWindow("Configurazione email non valida", message,
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            IsEmailTestRunning = true;
            AiSettingsStatus = "Invio email di prova in corso...";

            try
            {
                var testWarning = new EarlyWarning
                {
                    DetectedAtUtc = DateTime.UtcNow,
                    Severity = EarlyWarningSeverity.Info,
                    Category = "Test",
                    Key = "email-test",
                    Title = "Email di prova",
                    Detail = string.Format("Prova inviata da {0} alle {1:HH:mm:ss}.", UserSession.CurrentUser, DateTime.Now)
                };

                // Usa i valori correnti del pannello (anche se non ancora salvati): il canale non
                // rilegge da disco in questo percorso, cosi' la prova riflette esattamente cio' che
                // l'operatore ha appena digitato.
                var channel = new EmailNotificationChannel();
                await channel.SendTestAsync(bindings, testWarning);

                string successMessage = string.Format(
                    "Email di prova inviata alle {0:HH:mm:ss} a: {1}.", DateTime.Now, bindings.EmailToAddresses);
                AiSettingsStatus = successMessage;
                new SystemNotificationWindow("Email inviata", successMessage, NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "AI_EMAIL_TEST_FAILED");
                string failureMessage = $"Invio email di prova non riuscito: {ex.Message}";
                AiSettingsStatus = failureMessage;
                new SystemNotificationWindow("Invio non riuscito", failureMessage, NotificationSeverity.Warning).ShowDialog();
            }
            finally
            {
                IsEmailTestRunning = false;
            }
        }

        private void SaveAiSettings()
        {
            Keyboard.ClearFocus();

            // Difesa oltre al CanExecute: salvare durante un training ricaricherebbe _aiConfig e
            // il modello a fine training verrebbe registrato su una configurazione diversa.
            if (IsTrainingRunning || IsPythonCheckRunning)
            {
                AiSettingsStatus = "Salvataggio non disponibile durante il training: attendere il completamento.";
                return;
            }

            if (_aiConfig?.RuntimeBindings == null)
            {
                LoadAiSettings();
                if (_aiConfig?.RuntimeBindings == null)
                    return;
            }

            if (!CanApplyTimingParameters)
            {
                new SystemNotificationWindow("Autorizzazione richiesta",
                    "Solo Installer o Administrator possono modificare i controlli AI.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            if (!ValidateAiSettingsBeforeSave(out var validationMessage))
            {
                AiSettingsStatus = validationMessage;
                new SystemNotificationWindow("Controlli AI non validi",
                    validationMessage,
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            var confirm = new SystemNotificationWindow("Salva controlli AI",
                "Confermi il salvataggio delle impostazioni AI in machine_runtime_config.xml?\n" +
                "I flag di abilitazione e il classificatore ONNX vengono ricaricati subito dai servizi.",
                NotificationSeverity.Warning, true);
            confirm.ShowDialog();
            if (!confirm.Confirmed)
                return;

            try
            {
                // Save atomico (temp + File.Replace, con clamp dei default) di MachineConfigurationService.
                new MachineConfigurationService().Save(_aiConfig);

                RefreshAiServicesConfiguration();

                AiSettingsStatus = string.Format(
                    "Impostazioni AI salvate alle {0:HH:mm:ss} da {1}. Servizi e classificatore ONNX ricaricati.",
                    DateTime.Now, UserSession.CurrentUser);

                MainWindow.logger?.Info($"AI_SETTINGS_APPLY|actor={UserSession.CurrentUser}");
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    NLog.LogLevel.Warn,
                    "AI_SETTINGS_APPLY",
                    "AI Controls",
                    "Impostazioni servizi AI modificate da pannello diagnostica",
                    nameof(SystemDiagnosticsViewModel),
                    $"actor={UserSession.CurrentUser}",
                    null);

                // Ricarica dal file per mostrare i valori effettivi post-clamp.
                LoadAiSettings();
                RefreshShadowStats();
                AiSettingsStatus = string.Format(
                    "Impostazioni AI salvate alle {0:HH:mm:ss}. Servizi e classificatore ONNX ricaricati.",
                    DateTime.Now);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, "AI_SETTINGS_SAVE_FAILED");
                AiSettingsStatus = $"Errore salvataggio: {ex.Message}";
                new SystemNotificationWindow("Salvataggio non riuscito", ex.Message,
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private bool ValidateAiSettingsBeforeSave(out string message)
        {
            message = null;
            var bindings = _aiConfig?.RuntimeBindings;
            if (bindings == null)
            {
                message = "Configurazione AI non disponibile.";
                return false;
            }

            if (!IsInRange(bindings.MachineHealthDigestIntervalMinutes, 1, 1440))
            {
                message = "Digest preallarmi non valido: usare un valore tra 1 e 1440 minuti.";
                return false;
            }

            if (!IsInRange(bindings.MachineHealthCriticalEtaProducts, 1, 1000000))
            {
                message = "Soglia ETA critica non valida: usare un valore tra 1 e 1000000 pezzi.";
                return false;
            }

            if (!IsInRange(bindings.MachineHealthDiskCriticalHours, 0.5, 8760.0))
            {
                message = "Soglia disco critica non valida: usare un valore tra 0,5 e 8760 ore.";
                return false;
            }

            if (!IsRetentionInRange(bindings.AiInspectionMeasurementRetentionDays) ||
                !IsRetentionInRange(bindings.AiHealthSnapshotRetentionDays) ||
                !IsRetentionInRange(bindings.AiTrainingSampleRetentionDays))
            {
                message = "Retention AI non valida: usare 0 per illimitata oppure un valore tra 1 e 3650 giorni.";
                return false;
            }

            // Soglie di confidenza NOK: range esplicito [0,1] con messaggio chiaro, invece del
            // clamp silenzioso a valle (che correggerebbe il valore senza avvisare l'operatore).
            if (!IsInRange(bindings.DefectClassifierMinConfidence, 0.0, 1.0))
            {
                message = "Confidenza min NOK (TOP) non valida: usare un valore tra 0 e 1 (0 = filtro disattivato).";
                return false;
            }

            if (!IsInRange(bindings.SideDefectClassifierMinConfidence, 0.0, 1.0))
            {
                message = "Confidenza min NOK (Side) non valida: usare un valore tra 0 e 1 (0 = filtro disattivato).";
                return false;
            }

            if (!ValidateTrainingSettingsBeforeRun("top", out message))
            {
                return false;
            }

            if (!ValidateTrainingSettingsBeforeRun("side", out message, validateOutputDirectory: false))
            {
                return false;
            }

            if (!ValidateEnabledClassifierModel(
                    bindings.DefectClassifierEnabled,
                    bindings.DefectClassifierModelPath,
                    "TOP",
                    out var normalizedTopPath,
                    out message))
            {
                return false;
            }
            if (bindings.DefectClassifierEnabled)
                bindings.DefectClassifierModelPath = normalizedTopPath;

            if (!ValidateEnabledClassifierModel(
                    bindings.SideDefectClassifierEnabled,
                    bindings.SideDefectClassifierModelPath,
                    "SIDE / LEFT",
                    out var normalizedSidePath,
                    out message))
            {
                return false;
            }
            if (bindings.SideDefectClassifierEnabled)
                bindings.SideDefectClassifierModelPath = normalizedSidePath;

            // Stesse regole di TOP e SIDE per le camere REAR/RIGHT, FRONT e BOTTOM.
            foreach (DefectClassifierCameraProfile profile in bindings.AdditionalDefectClassifierProfiles ?? new System.Collections.Generic.List<DefectClassifierCameraProfile>())
            {
                string cameraLabel = CameraConfigurationHelper.GetCameraRoleDisplayLabel(profile.CameraRole);
                if (!IsInRange(profile.MinConfidence, 0.0, 1.0))
                {
                    message = $"Confidenza min NOK ({cameraLabel}) non valida: usare un valore tra 0 e 1 (0 = filtro disattivato).";
                    return false;
                }

                if (!ValidateTrainingSettingsBeforeRun(profile.CameraRole, out message, validateOutputDirectory: false))
                {
                    return false;
                }

                if (!ValidateEnabledClassifierModel(
                        profile.Enabled,
                        profile.ModelPath,
                        cameraLabel,
                        out var normalizedProfilePath,
                        out message))
                {
                    return false;
                }
                if (profile.Enabled)
                    profile.ModelPath = normalizedProfilePath;
            }

            if (bindings.EmailNotificationsEnabled)
            {
                if (string.IsNullOrWhiteSpace(bindings.EmailSmtpHost))
                {
                    message = "Email/SMTP abilitato ma host SMTP mancante.";
                    return false;
                }

                if (!IsInRange(bindings.EmailSmtpPort, 1, 65535))
                {
                    message = "Porta SMTP non valida: usare un valore tra 1 e 65535.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(bindings.EmailFromAddress) ||
                    bindings.EmailFromAddress.IndexOf('@') <= 0)
                {
                    message = "Email/SMTP abilitato ma indirizzo mittente mancante o non valido.";
                    return false;
                }

                var recipients = (bindings.EmailToAddresses ?? string.Empty)
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0)
                    .ToList();
                if (recipients.Count == 0 || recipients.Any(a => a.IndexOf('@') <= 0))
                {
                    message = "Email/SMTP abilitato ma nessun destinatario valido (separare piu' indirizzi con ';' o ',').";
                    return false;
                }
            }

            return true;
        }

        private bool ValidateTrainingSettingsBeforeRun(
            string cameraRole,
            out string message,
            bool validateOutputDirectory = true)
        {
            message = null;
            var bindings = _aiConfig?.RuntimeBindings;
            if (bindings == null)
            {
                message = "Configurazione AI non disponibile.";
                return false;
            }

            DefectClassifierCameraProfile profile = bindings.ResolveDefectClassifierProfile(cameraRole);
            if (profile == null)
            {
                message = $"Nessun profilo classificatore per la camera '{cameraRole}'.";
                return false;
            }

            string cameraLabel = CameraConfigurationHelper.GetCameraRoleDisplayLabel(profile.CameraRole);
            int inputWidth = profile.InputWidth;
            int inputHeight = profile.InputHeight;
            double normalizeStd = profile.NormalizeStd;

            if (inputWidth < 16 || inputHeight < 16)
            {
                message = $"Larghezza e altezza input del classificatore {cameraLabel} devono essere almeno 16 px.";
                return false;
            }

            if (inputWidth > MaxDefectClassifierInputSize || inputHeight > MaxDefectClassifierInputSize)
            {
                message = $"La dimensione input ONNX {cameraLabel} e' il resize del modello, non la risoluzione camera. " +
                          $"Usare valori tra 16 e {MaxDefectClassifierInputSize} px per lato, ad esempio 224x224, 320x320 o 640x640.";
                return false;
            }

            if (double.IsNaN(normalizeStd) || double.IsInfinity(normalizeStd) || Math.Abs(normalizeStd) < 1e-9)
            {
                message = $"La deviazione standard di normalizzazione ONNX {cameraLabel} non puo' essere zero o non valida.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(bindings.DefectClassifierTrainingPythonPath))
            {
                bindings.DefectClassifierTrainingPythonPath = "python";
            }

            if (string.IsNullOrWhiteSpace(bindings.DefectClassifierTrainingOutputDir))
            {
                bindings.DefectClassifierTrainingOutputDir = @"C:\QtisVision\AI\Models";
            }

            if (!IsInRange(bindings.DefectClassifierTrainingEpochs, 1, 500))
            {
                message = "Epoche training non valide: usare un valore tra 1 e 500.";
                return false;
            }

            if (validateOutputDirectory)
            {
                try
                {
                    Path.GetFullPath(Environment.ExpandEnvironmentVariables(bindings.DefectClassifierTrainingOutputDir));
                }
                catch (Exception ex)
                {
                    message = $"Cartella modelli AI non valida: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateEnabledClassifierModel(
            bool enabled,
            string configuredPath,
            string cameraLabel,
            out string normalizedPath,
            out string message)
        {
            normalizedPath = Environment.ExpandEnvironmentVariables(configuredPath ?? string.Empty).Trim().Trim('"');
            message = null;
            if (!enabled)
                return true;

            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                message = $"Il classificatore ONNX {cameraLabel} e' abilitato, ma il percorso modello e' vuoto. " +
                          "Selezionare un file .onnx valido oppure disabilitare il classificatore.";
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                message = $"Il file modello ONNX {cameraLabel} non esiste: {normalizedPath}";
                return false;
            }

            if (!string.Equals(Path.GetExtension(normalizedPath), ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                message = $"Il file modello {cameraLabel} deve avere estensione .onnx: {normalizedPath}";
                return false;
            }

            return true;
        }

        private static bool IsInRange(int value, int min, int max)
        {
            return value >= min && value <= max;
        }

        private static bool IsInRange(double value, double min, double max)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;
        }

        private static bool IsRetentionInRange(int value)
        {
            return value == 0 || (value >= 1 && value <= 3650);
        }

        private static void RefreshAiServicesConfiguration()
        {
            // Riapplica i flag a caldo. HealthSnapshotService legge il flag ad ogni tick.
            ServiceLocator.MeasurementCaptureService?.RefreshConfiguration();
            ServiceLocator.ProcessControlService?.RefreshConfiguration();
            ServiceLocator.PredictiveMaintenanceService?.RefreshConfiguration();
            ServiceLocator.TrainingDataCollectionService?.RefreshConfiguration();
            foreach (OnnxDefectClassifier classifier in ServiceLocator.AllDefectClassifiers)
            {
                classifier?.RefreshConfiguration();
            }
            ServiceLocator.IoTimingOptimizer?.RefreshConfiguration();
            ServiceLocator.AiPerformanceMonitor?.RefreshConfiguration();
            ServiceLocator.RecipeProductAdvisor?.RefreshConfiguration();
            ServiceLocator.MachineHealthNotificationService?.RefreshConfiguration();
        }

        private void DiagnosticsService_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);

            // Refresh machine-rate metrics on every diagnostics tick
            OnPropertyChanged(nameof(CurrentPpm));
            OnPropertyChanged(nameof(CurrentPpmText));

            if (e.PropertyName == nameof(SystemDiagnosticsService.OverallSeverity))
            {
                OnPropertyChanged(nameof(HasSystemWarning));
                OnPropertyChanged(nameof(SystemWarningText));
            }

            if (e.PropertyName == nameof(SystemDiagnosticsService.ActiveAlertCount))
            {
                OnPropertyChanged(nameof(HasAlerts));
                OnPropertyChanged(nameof(ActiveAlertSummary));
                OnPropertyChanged(nameof(SystemWarningText));
                OnPropertyChanged(nameof(CriticalAlertCount));
                OnPropertyChanged(nameof(WarningAlertCount));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.ActiveAlerts))
            {
                OnPropertyChanged(nameof(ActiveAlerts));
                OnPropertyChanged(nameof(HasAlerts));
                OnPropertyChanged(nameof(ActiveAlertSummary));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.Drives))
            {
                OnPropertyChanged(nameof(Drives));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.TemperatureSensors))
            {
                OnPropertyChanged(nameof(TemperatureSensors));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.HottestDiskTemperatureSensor))
            {
                OnPropertyChanged(nameof(HottestDiskTemperatureSensor));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.HottestCpuTemperatureSensor))
            {
                OnPropertyChanged(nameof(HottestCpuTemperatureSensor));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.MemoryTemperatureSensor))
            {
                OnPropertyChanged(nameof(MemoryTemperatureSensor));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.DatabaseArchiveStatus))
            {
                OnPropertyChanged(nameof(DatabaseArchiveStatus));
            }
            else if (e.PropertyName == nameof(SystemDiagnosticsService.LastUpdatedText) ||
                     e.PropertyName == nameof(SystemDiagnosticsService.LastUpdatedAt))
            {
                OnPropertyChanged(nameof(LastUpdatedDisplayText));
            }
        }

        public void Dispose()
        {
            _diagnosticsService.PropertyChanged -= DiagnosticsService_PropertyChanged;
            DisposeTrainingCancellation(cancelFirst: true);
        }

        private void DisposeTrainingCancellation(bool cancelFirst = false)
        {
            var cts = _trainingCts;
            _trainingCts = null;
            if (cts == null)
                return;

            try
            {
                if (cancelFirst)
                    cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }
    }

    /// <summary>Riga advisory dell'ottimizzatore timing (Fase 5).</summary>
    public sealed class TimingAdvisoryItem
    {
        public string Message { get; set; }
        public string Severity { get; set; }
        public bool IsWarning => string.Equals(Severity, "warning", StringComparison.OrdinalIgnoreCase);
        public Brush SeverityBrush => IsWarning
            ? new SolidColorBrush(Color.FromRgb(0xD9, 0x8E, 0x2F))
            : new SolidColorBrush(Color.FromRgb(0x2C, 0x79, 0xB8));
    }

    /// <summary>Parametro di timing applicabile con valore corrente e target editabile (Fase 5).</summary>
    public sealed class TimingParameterItem : INotifyPropertyChanged
    {
        private int _currentValue;
        private string _targetValue;

        public string Name { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public string RangeText => $"{Min} - {Max}";

        /// <summary>Riepilogo di sola lettura per la UI (binding OneWay su un unico TextBlock).</summary>
        public string CurrentSummary => $"attuale {CurrentValue} ms  ·  range {RangeText}";

        public int CurrentValue
        {
            get => _currentValue;
            set { _currentValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentSummary)); }
        }

        public string TargetValue
        {
            get => _targetValue;
            set { _targetValue = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
