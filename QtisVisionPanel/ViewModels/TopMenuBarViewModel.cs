using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QtisVisionPanel.ViewModels
{
    public class TopMenuBarViewModel : INotifyPropertyChanged
    {
        private readonly MachineStatusService _statusService = MachineStatusService.Instance;
        private readonly ApplicationEventLogger _applicationEventLogger = ServiceLocator.ApplicationEventLogger;
        private readonly EventLogRepository _eventLogRepository = new EventLogRepository();
        private readonly AuthorizationService _authorizationService = ServiceLocator.Authorization;
        private readonly SystemDiagnosticsService _systemDiagnosticsService = ServiceLocator.SystemDiagnosticsService;
        private readonly MachineHealthNotificationService _machineHealthNotificationService = ServiceLocator.MachineHealthNotificationService;
        private readonly PowerFlex525Service _powerFlex525Service = ServiceLocator.PowerFlex525Service;
        private readonly MachineSpeedService _machineSpeedService = ServiceLocator.MachineSpeedService;
        private AlarmInfo _lastAlarm;
        private string _currentUser = "Guest";
        private string _currentUserRole = "Viewer";
        private string _currentRecipeName;
        private string _transportSpeed = "0";
        private string _productionCounter = "0";
        private string _qualityIndexText = "0.0%";
        private string _qualityIndexDetailText = "0 / 0";
        private int _activeAlarmCount = 0;
        private bool _isLoggedIn = false;
        private bool _hasRuntimeHold;
        private string _runtimeHoldDisplayText = string.Empty;
        private string _runtimeHoldTooltip = string.Empty;
        private readonly DispatcherTimer _refreshTimer;
        private DateTime _lastAlarmRefreshAt = DateTime.MinValue;
        private bool _isAlarmRefreshInProgress;
        // Aggiungi questo evento
        public event EventHandler<UserChangedEventArgs> UserChanged;
        public enum MachineStatus
        {
            Connecting,
            Running,
            Stopped,
            Error,
            Maintenance
        }

        // Property che si collega al servizio
        public MachineStatus CurrentMachineStatus => _statusService.CurrentStatus;
        public string MachineStatusText => _statusService.StatusText;
        public bool IsSystemInitializing => _statusService.IsSystemInitializing;
        public bool IsSystemReady => _statusService.IsSystemReady;

        public AlarmInfo LastAlarm
        {
            get => _lastAlarm;
            set
            {
                _lastAlarm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastAlarmMessage));
                OnPropertyChanged(nameof(CurrentAlarmLevel));
            }
        }

        public string LastAlarmMessage => _lastAlarm?.Message ?? "No alarms";
        public AlarmLevel CurrentAlarmLevel => _lastAlarm?.Level ?? AlarmLevel.None;

        public int ActiveAlarmCount
        {
            get => _activeAlarmCount;
            set
            {
                _activeAlarmCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveAlarms));
            }
        }

        public bool HasActiveAlarms => _activeAlarmCount > 0;

        public string CurrentUser
        {
            get => _currentUser;
            set
            {
                _currentUser = value;
                OnPropertyChanged();
            }
        }

        public string CurrentUserRole
        {
            get => _currentUserRole;
            set
            {
                _currentUserRole = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set
            {
                if (_isLoggedIn != value)
                {
                    _isLoggedIn = value;
                    MainWindow.logger?.Info($"IsLoggedIn changed to: {value}");
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentUser)); // Assicura che CurrentUser venga notificato
                }
            }
        }

        public string CurrentRecipeName
        {
            get => _currentRecipeName;
            set
            {
                _currentRecipeName = value;
                OnPropertyChanged();
            }
        }

        public string TransportSpeed
        {
            get => _transportSpeed;
            set
            {
                _transportSpeed = value;
                OnPropertyChanged();
            }
        }

        public string ProductionCounter
        {
            get => _productionCounter;
            set
            {
                _productionCounter = value;
                OnPropertyChanged();
            }
        }

        public string QualityIndexText
        {
            get => _qualityIndexText;
            set
            {
                _qualityIndexText = value;
                OnPropertyChanged();
            }
        }

        public string QualityIndexDetailText
        {
            get => _qualityIndexDetailText;
            set
            {
                _qualityIndexDetailText = value;
                OnPropertyChanged();
            }
        }

        public string CurrentRecipePath
        {
            get
            {
                if (MainWindow.configManager?.Config?.Configuration != null)
                {
                    var config = MainWindow.configManager.Config.Configuration;
                    var recipeFolder = config.Recipe_Folder;
                    var lastRecipe = config.LastRecipe;
                    return System.IO.Path.Combine(recipeFolder, lastRecipe);
                }
                return string.Empty;
            }
        }

        public DiagnosticsSeverity DiagnosticsSeverity => _systemDiagnosticsService.OverallSeverity;
        public Brush DiagnosticsStatusBrush => _systemDiagnosticsService.OverallStatusBrush;
        public string DiagnosticsStatusText => _systemDiagnosticsService.OverallStatusText;
        // Il badge deve notificare solo gli errori: conta e mostra solo le
        // diagnostiche Critical, escludendo i warning (es. RAM/CPU sopra soglia).
        public int DiagnosticsAlertCount => _systemDiagnosticsService.CriticalAlertCount;
        public bool HasDiagnosticsAlerts => _systemDiagnosticsService.CriticalAlertCount > 0;
        public bool CanOpenSystemDiagnostics => _authorizationService.CanAccessViewCached("SystemDiagnostics");
        public string DiagnosticsTooltip
        {
            get
            {
                if (!CanOpenSystemDiagnostics)
                {
                    return ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_SystemDiagnosticsPermissionRequired",
                        "PC diagnostics are reserved to Expert, Installer or Administrator.");
                }

                return string.Format("PC diagnostics: {0}. {1}", DiagnosticsStatusText, _systemDiagnosticsService.ActiveAlertSummary);
            }
        }

        // Badge proattivo Fase 9: preallarmi AI (SPC/salute PC) non ancora visti, distinto dal
        // badge diagnostico sopra (che mostra solo Critical di sistema). Si azzera aprendo/
        // aggiornando il pannello "Controlli AI" in PC Diagnostics.
        public int AiAdvisoryBadgeCount => _machineHealthNotificationService?.UnseenCount ?? 0;
        public bool HasAiAdvisories => AiAdvisoryBadgeCount > 0;
        public string AiAdvisoryTooltip
        {
            get
            {
                if (!CanOpenSystemDiagnostics)
                {
                    return ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_SystemDiagnosticsPermissionRequired",
                        "PC diagnostics are reserved to Expert, Installer or Administrator.");
                }

                string template = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                    "Sub_entry_AiAdvisoryBadgeTooltipFormat",
                    "{0} unseen AI early warning(s) (inspection drift / PC health).");
                return string.Format(template, AiAdvisoryBadgeCount);
            }
        }

        public bool HasRuntimeHold
        {
            get => _hasRuntimeHold;
            private set
            {
                if (_hasRuntimeHold != value)
                {
                    _hasRuntimeHold = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RuntimeHoldDisplayText
        {
            get => _runtimeHoldDisplayText;
            private set
            {
                if (_runtimeHoldDisplayText != value)
                {
                    _runtimeHoldDisplayText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RuntimeHoldTooltip
        {
            get => _runtimeHoldTooltip;
            private set
            {
                if (_runtimeHoldTooltip != value)
                {
                    _runtimeHoldTooltip = value;
                    OnPropertyChanged();
                }
            }
        }

        public Brush RuntimeHoldBrush => HasRuntimeHold
            ? new SolidColorBrush(Color.FromRgb(240, 164, 59))
            : Brushes.Transparent;

        public ICommand ShowAlarmDetailsCommand { get; }
        public ICommand ShowSystemDiagnosticsCommand { get; }
        public ICommand ShowLoginCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ShowStatusHistoryCommand { get; }

        public TopMenuBarViewModel()
        {
            MainWindow.logger?.Info("TopMenuBarViewModel constructor called");
            // Inizializza i comandi
            ShowAlarmDetailsCommand = new RelayCommand(ShowAlarmDetails);
            ShowSystemDiagnosticsCommand = new RelayCommand(ShowSystemDiagnostics, _ => CanOpenSystemDiagnostics);
            ShowLoginCommand = new RelayCommand(ShowLoginDialog);
            LogoutCommand = new RelayCommand(Logout);

            ShowStatusHistoryCommand = new RelayCommand(ShowStatusHistory);

            // Sottoscrivi agli eventi di cambio stato
            _statusService.StatusChanged += OnMachineStatusChanged;
            _statusService.PropertyChanged += OnStatusServicePropertyChanged;
            _systemDiagnosticsService.PropertyChanged += OnDiagnosticsServicePropertyChanged;
            _machineHealthNotificationService.WarningStateChanged += OnAiWarningStateChanged;
            _systemDiagnosticsService.Start();
            _powerFlex525Service.PropertyChanged += OnPowerFlexServicePropertyChanged;
            _powerFlex525Service.Start();
            _machineSpeedService.PropertyChanged += OnMachineSpeedServicePropertyChanged;
            UserSession.OnRoleChanged += OnUserRoleChanged;

            // Aggiorna il contatore nel top bar per ogni ispezione completata,
            // senza dipendere solo dal timer 1s (che causa il "salto" dopo burst).
            var counterManager = ServiceLocator.CounterManager;
            if (counterManager != null)
                counterManager.CountersUpdated += OnCountersUpdated;

            // Avvia timer per aggiornamenti periodici
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();

            // Inizializza con lo stato attuale
            InitializeFromSystem();
            MainWindow.logger?.Info($"TopMenuBarViewModel initialized - IsLoggedIn: {IsLoggedIn}");
        }
        private void OnMachineStatusChanged(object sender, StatusChangedEventArgs e)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnMachineStatusChanged(sender, e)));
                return;
            }

            // Questo viene chiamato quando lo stato cambia
            OnPropertyChanged(nameof(CurrentMachineStatus));
            OnPropertyChanged(nameof(MachineStatusText));

            // Ricalcola la velocità quando lo stato cambia
            CalculateTransportSpeed();

            // Log del cambio di stato
            MainWindow.logger?.Info($"Machine status changed: {e.OldStatus} → {e.NewStatus}");
        }
        private void OnStatusServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnStatusServicePropertyChanged(sender, e)));
                return;
            }

            if (e.PropertyName == nameof(MachineStatusService.StatusText))
            {
                OnPropertyChanged(nameof(MachineStatusText));
            }
            else if (e.PropertyName == nameof(MachineStatusService.IsSystemInitializing))
            {
                OnPropertyChanged(nameof(IsSystemInitializing));
                OnPropertyChanged(nameof(IsSystemReady));
            }
        }

        private void OnCountersUpdated()
        {
            // CountersUpdated fires via Dispatcher.BeginInvoke, so we are on the UI thread.
            UpdateCounters();
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            // Aggiorna i contatori periodicamente (fallback se CountersUpdated non disponibile)
            UpdateCounters();
            RefreshRuntimeHoldState();

            if ((DateTime.Now - _lastAlarmRefreshAt).TotalSeconds >= 2)
            {
                RefreshAlarmSummaryAsync().SafeFireAndForget();
            }
        }

        private void OnDiagnosticsServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnDiagnosticsServicePropertyChanged(sender, e)));
                return;
            }

            OnPropertyChanged(nameof(DiagnosticsSeverity));
            OnPropertyChanged(nameof(DiagnosticsStatusBrush));
            OnPropertyChanged(nameof(DiagnosticsStatusText));
            OnPropertyChanged(nameof(DiagnosticsAlertCount));
            OnPropertyChanged(nameof(HasDiagnosticsAlerts));
            OnPropertyChanged(nameof(CanOpenSystemDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsTooltip));
            OnPropertyChanged(nameof(AiAdvisoryBadgeCount));
            OnPropertyChanged(nameof(HasAiAdvisories));
            OnPropertyChanged(nameof(AiAdvisoryTooltip));
        }

        private void OnAiWarningStateChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(AiAdvisoryBadgeCount));
            OnPropertyChanged(nameof(HasAiAdvisories));
            OnPropertyChanged(nameof(AiAdvisoryTooltip));
        }

        private void OnUserRoleChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(CanOpenSystemDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsTooltip));
            OnPropertyChanged(nameof(AiAdvisoryTooltip));
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnPowerFlexServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PowerFlex525Service.MetersPerMinuteText) ||
                e.PropertyName == nameof(PowerFlex525Service.MetersPerMinute))
            {
                CalculateTransportSpeed();
            }
        }

        private void OnMachineSpeedServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnMachineSpeedServicePropertyChanged(sender, e)));
                return;
            }

            if (e.PropertyName == nameof(MachineSpeedService.MetersPerMinute) ||
                e.PropertyName == nameof(MachineSpeedService.MetersPerMinuteText) ||
                e.PropertyName == nameof(MachineSpeedService.LastUpdateUtc) ||
                e.PropertyName == nameof(MachineSpeedService.Source))
            {
                CalculateTransportSpeed();
            }
        }

        public void RefreshTexts()
        {
            OnPropertyChanged(nameof(IsLoggedIn));
            RefreshRuntimeHoldState();
            MainWindow.logger?.Info("Testi TopMenuBar aggiornati");
        }

        private void RefreshRuntimeHoldState()
        {
            var runtimeService = ServiceLocator.MachineRuntimeService;
            if (runtimeService == null)
            {
                HasRuntimeHold = false;
                RuntimeHoldDisplayText = string.Empty;
                RuntimeHoldTooltip = string.Empty;
                OnPropertyChanged(nameof(RuntimeHoldBrush));
                return;
            }

            string rawSummary = runtimeService.ContinuousRunHoldSummary;
            bool hasHold = !string.IsNullOrWhiteSpace(rawSummary);
            string displayText = hasHold
                ? string.Join(", ", rawSummary
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => TranslateRuntimeHoldReason(item.Trim()))
                    .Where(item => !string.IsNullOrWhiteSpace(item)))
                : string.Empty;

            HasRuntimeHold = hasHold;
            RuntimeHoldDisplayText = displayText;
            RuntimeHoldTooltip = hasHold
                ? string.Format(
                    ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RuntimeHoldTooltipFormat",
                        "Protected stop active: {0}"),
                    displayText)
                : string.Empty;

            OnPropertyChanged(nameof(RuntimeHoldBrush));
        }

        private static string TranslateRuntimeHoldReason(string reason)
        {
            switch (reason?.Trim().ToLowerInvariant())
            {
                case MachineRuntimeService.HoldReasonManualStop:
                    return ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RuntimeHoldReasonManualStop",
                        "Manual stop");
                case MachineRuntimeService.HoldReasonRecipeSave:
                    return ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RuntimeHoldReasonRecipeSave",
                        "Recipe save");
                case MachineRuntimeService.HoldReasonJobEditor:
                    return ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RuntimeHoldReasonJobEditor",
                        "Job editor");
                case MachineRuntimeService.HoldReasonShutdown:
                    return ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RuntimeHoldReasonShutdown",
                        "Shutdown");
                default:
                    return string.IsNullOrWhiteSpace(reason)
                        ? ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                            "Sub_entry_RuntimeHoldReasonUnknown",
                            "Unknown hold")
                        : reason;
            }
        }
        private void InitializeFromSystem()
        {
            

            // Carica l'ultimo allarme
            LoadLastAlarm();

            // Carica la ricetta corrente
            LoadCurrentRecipe();
            // Verifica se c'è un utente già loggato (es. da sessione precedente)
            CheckExistingUserSession();

            // Aggiorna i contatori
            UpdateCounters();
            RefreshRuntimeHoldState();
            RefreshAlarmSummaryAsync().SafeFireAndForget();
        }

        private void CheckExistingUserSession()
        {
            try
            {
                UserSession.ApplyStartupSessionFromConfig();

                // Aggiorna le proprietà del ViewModel
                CurrentUser = UserSession.CurrentUser;
                CurrentUserRole = UserSession.CurrentRole;
                IsLoggedIn = UserSession.IsLoggedIn;

                if (MainWindow.configManager?.Config?.Configuration != null)
                {
                    MainWindow.configManager.Config.Configuration.User = UserSession.CurrentUser;
                    MainWindow.configManager.Config.Configuration.CurrentUserRole = UserSession.CurrentRole;
                }

                MainWindow.logger?.Info($"User session bootstrap - {UserSession.CurrentUser}/{UserSession.CurrentRole} - IsLoggedIn: {IsLoggedIn}");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error checking user session: {ex.Message}");
                // Fallback sicuro
                UserSession.CurrentUser = "Guest";
                UserSession.CurrentRole = "Viewer";
            }
        }

        private void CheckForSystemErrors()
        {
            // Implementa la logica per controllare errori nel sistema
            // Potresti controllare i log o lo stato delle periferiche
        }

        private void LoadLastAlarm()
        {
            RefreshAlarmSummaryAsync().SafeFireAndForget();
        }

        private void LoadCurrentRecipe()
        {
            try
            {
                if (MainWindow.configManager?.Config?.Configuration != null)
                {
                    var recipe = MainWindow.configManager.Config.Configuration.LastRecipe;
                    CurrentRecipeName = System.IO.Path.GetFileNameWithoutExtension(recipe);
                }
                else
                {
                    CurrentRecipeName = "No recipe loaded";
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error loading current recipe: {ex.Message}");
                CurrentRecipeName = "Error";
            }
        }

        private void UpdateCounters()
        {
            try
            {
                // Ottieni il contatore di produzione dal CounterManager
                if (MainWindow.CounterManager != null)
                {
                    var counters = MainWindow.CounterManager.GetAllCounters();
                    counters.TryGetValue("Total", out long total);
                    counters.TryGetValue("Good", out long good);

                    ProductionCounter = total.ToString();
                    UpdateQualityIndex(good, total);

                    // Calcola la velocità (pezzi al minuto)
                    CalculateTransportSpeed();
                    return;
                }

                ProductionCounter = "0";
                UpdateQualityIndex(0, 0);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error updating counters: {ex.Message}");
            }
        }

        private void UpdateQualityIndex(long good, long total)
        {
            if (total <= 0)
            {
                QualityIndexText = "0.0%";
                QualityIndexDetailText = "0 / 0";
                return;
            }

            var percentage = Math.Round((double)good / total * 100, 1);
            QualityIndexText = $"{percentage:0.0}%";
            QualityIndexDetailText = $"{good} / {total}";
        }

        private void CalculateTransportSpeed()
        {
            if (_machineSpeedService.HasRecentValue &&
                string.Equals(_machineSpeedService.Source, "Encoder", StringComparison.OrdinalIgnoreCase))
            {
                TransportSpeed = $"{_machineSpeedService.MetersPerMinuteText} m/min";
                return;
            }

            // The conveyor is driven by the PowerFlex 525 at constant speed.
            // Do not simulate speed in production: show the last available
            // inverter-derived m/min value, or 0 when the drive is unavailable.
            var powerFlex = ServiceLocator.PowerFlex525Service;
            TransportSpeed = powerFlex?.MetersPerMinuteText == "--"
                ? "0 m/min"
                : $"{powerFlex?.MetersPerMinuteText ?? "0"} m/min";
        }

        private void ShowAlarmDetails(object parameter)
        {
            if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
            {
                mainVm.OnNavigateRequested("Alarmsview");
                return;
            }

            new SystemNotificationWindow("Alarm Details", "Vista allarmi non disponibile.", NotificationSeverity.Info).ShowDialog();
        }

        private void ShowSystemDiagnostics(object parameter)
        {
            if (!CanOpenSystemDiagnostics)
            {
                new SystemNotificationWindow(
                    ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_AuthorizationRequiredTitle",
                        "Authorization required"),
                    ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_SystemDiagnosticsPermissionRequired",
                        "PC diagnostics are reserved to Expert, Installer or Administrator."),
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
            {
                mainVm.OnNavigateRequested("SystemDiagnostics");
                return;
            }

            new SystemNotificationWindow("PC Diagnostics", "Vista diagnostica PC non disponibile.", NotificationSeverity.Info).ShowDialog();
        }
        private void ShowStatusHistory(object parameter)
        {
            var history = _statusService.GetStatusHistoryAsText();
            new SystemNotificationWindow("Status History", history, NotificationSeverity.Info).ShowDialog();
        }


        private void ShowLoginDialog(object parameter)
        {
            MainWindow.logger?.Info("ShowLoginDialog command executed");
            try
            {
                var loginWindow = new LoginWindow();

                // Trova owner corretto: prima la finestra attiva, poi MainWindow
                var owner = Application.Current.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow;

                // Imposta Owner solo se è davvero visibile
                if (owner != null && owner.IsVisible)
                {
                    loginWindow.Owner = owner;
                    loginWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    loginWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                var result = loginWindow.ShowDialog();

                if (result == true && loginWindow.LoggedInUser != null)
                {
                    // **LOGIN EFFETTIVO**
                    CurrentUser = loginWindow.LoggedInUser?.Username;
                    CurrentUserRole = loginWindow.LoggedInUser?.Role;


                    CurrentUser = UserSession.CurrentUser;
                    CurrentUserRole = UserSession.CurrentRole;
                    IsLoggedIn = true;

                    // Salva nel config (opzionale, per ricordare l'ultimo login)
                    if (MainWindow.configManager != null)
                    {
                        MainWindow.configManager.Config.Configuration.User = UserSession.CurrentUser;
                        MainWindow.configManager.Config.Configuration.CurrentUserRole = UserSession.CurrentRole;
                        MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
                    }

                    MainWindow.logger?.Info($"Utente loggato: {UserSession.CurrentUser} ({UserSession.CurrentRole})");
                    _applicationEventLogger.LogUserEvent("USER_LOGIN", UserSession.CurrentUser, UserSession.CurrentRole, nameof(ShowLoginDialog), "Login eseguito con successo");

                    if (MainWindow._produzioneRecord != null)
                    {
                        MainWindow._produzioneRecord.Operatore = ResolveOperatorForProductionRecord();
                        MainWindow.logger?.Info($"Produzione record operator updated to: {MainWindow._produzioneRecord.Operatore}");
                    }

                    // Notifica il cambio ruolo per aggiornare i permessi
                    UserSession.NotifyRoleChanged();
                    ServiceLocator.Authorization?.InvalidateCache();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error in ShowLoginDialog: {ex.Message}");
                _applicationEventLogger.LogException("USER_LOGIN_FAILED", ex, nameof(ShowLoginDialog), "Errore durante il login");
                new SystemNotificationWindow("Errore", $"Errore durante il login: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        private void Logout(object parameter)
        {
            ExecuteLogout(true, nameof(Logout), "Logout eseguito con successo", true);
        }

        public void ExecuteAutomaticLogout(string source, string details)
        {
            ExecuteLogout(false, source, details, false);
        }

        private void ExecuteLogout(bool askConfirmation, string source, string details, bool showSuccessMessage)
        {
            MainWindow.logger?.Info("Logout command executed");

            var result = MessageBoxResult.Yes;
            if (askConfirmation)
            {
                var dlgLogout = new SystemNotificationWindow("Logout", "Sei sicuro di voler uscire?", NotificationSeverity.Warning, true);
                dlgLogout.ShowDialog();
                result = dlgLogout.Confirmed ? MessageBoxResult.Yes : MessageBoxResult.No;
            }

            if (result == MessageBoxResult.Yes)
            {
                var oldUser = CurrentUser;
                var oldRole = CurrentUserRole;
                // TORNA A GUEST/VIEWER
                UserSession.CurrentUser = "Guest";
                UserSession.CurrentRole = "Viewer";
                ServiceLocator.Authorization?.InvalidateCache();

                // Aggiorna le proprietà locali
                CurrentUser = UserSession.CurrentUser;
                CurrentUserRole = UserSession.CurrentRole;
                IsLoggedIn = false;

                MainWindow.logger?.Info($"User logged out: {oldUser} ->Guest");
                var logoutLogId = askConfirmation ? "USER_LOGOUT" : "USER_AUTO_LOGOUT";
                _applicationEventLogger.LogUserEvent(logoutLogId, oldUser, oldRole, source, details);

                // Resetta l'operatore nel record di produzione
                if (MainWindow._produzioneRecord != null)
                {
                    MainWindow._produzioneRecord.Operatore = ResolveOperatorForProductionRecord();
                    MainWindow.logger?.Info($"Reset produzione record operator to: {MainWindow._produzioneRecord.Operatore}");
                }
                // Salva nel config
                if (MainWindow.configManager != null)
                {
                    MainWindow.configManager.Config.Configuration.User = "Guest";
                    MainWindow.configManager.Config.Configuration.CurrentUserRole = "Viewer";
                    MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
                }
                // **Notifica il cambio utente (importante per il redirect)**
                UserChanged?.Invoke(this, new UserChangedEventArgs
                {
                    OldUser = oldUser,
                    OldRole = oldRole,
                    NewUser = "Guest",
                    NewRole = "Viewer"
                });
                // Notifica il cambio ruolo per aggiornare i permessi
                // Aggiorna UI
               MainWindow.MainView.UpdateUIForCurrentUser();
                UserSession.NotifyRoleChanged();


                if (showSuccessMessage)
                {
                    new SystemNotificationWindow("Logout", "Logout effettuato con successo", NotificationSeverity.Info).ShowDialog();
                }
            }
        }

        private static string ResolveOperatorForProductionRecord()
        {
            if (!string.IsNullOrWhiteSpace(UserSession.CurrentUser))
            {
                return UserSession.CurrentUser;
            }

            if (!string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.User))
            {
                return MainWindow.configManager.Config.Configuration.User;
            }

            return "Guest";
        }

        // Metodo per aggiornare lo stato della macchina (chiamato da altri componenti)

        // Metodo per aggiungere un nuovo allarme
        public void AddAlarm(string message, AlarmLevel level)
        {
            LastAlarm = new AlarmInfo
            {
                Message = message,
                Level = level,
                Timestamp = DateTime.Now
            };

            if (level == AlarmLevel.Error || level == AlarmLevel.Critical)
            {
                ActiveAlarmCount++;
            }

            // Log dell'allarme
            MainWindow.logger?.Warn($"New alarm: {message} (Level: {level})");
            _applicationEventLogger.LogAlarmEvent(message, nameof(AddAlarm), $"Alarm level: {level}", level == AlarmLevel.Critical || level == AlarmLevel.Error);
            _lastAlarmRefreshAt = DateTime.MinValue;
        }

        private async Task RefreshAlarmSummaryAsync()
        {
            if (_isAlarmRefreshInProgress)
            {
                return;
            }

            try
            {
                _isAlarmRefreshInProgress = true;
                var summary = await _eventLogRepository.GetAlertSummaryAsync();
                ActiveAlarmCount = summary.AlertCount;

                var latest = summary.LatestAlert;
                if (latest != null)
                {
                    LastAlarm = new AlarmInfo
                    {
                        Message = latest.Message,
                        Level = ToAlarmLevel(latest.LevelText),
                        Timestamp = latest.Timestamp
                    };
                }
                else
                {
                    LastAlarm = new AlarmInfo
                    {
                        Message = "No alarms",
                        Level = AlarmLevel.None,
                        Timestamp = DateTime.Now
                    };
                }

                _lastAlarmRefreshAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error refreshing alarm summary: {ex.Message}");
            }
            finally
            {
                _isAlarmRefreshInProgress = false;
            }
        }

        private static AlarmLevel ToAlarmLevel(string levelText)
        {
            if (string.Equals(levelText, "Critical", StringComparison.OrdinalIgnoreCase))
                return AlarmLevel.Critical;
            if (string.Equals(levelText, "Error", StringComparison.OrdinalIgnoreCase))
                return AlarmLevel.Error;
            if (string.Equals(levelText, "Warning", StringComparison.OrdinalIgnoreCase))
                return AlarmLevel.Warning;
            if (string.Equals(levelText, "Info", StringComparison.OrdinalIgnoreCase))
                return AlarmLevel.Info;

            return AlarmLevel.None;
        }

        // Metodo per aggiornare i contatori
        public void RefreshCounters()
        {
            UpdateCounters();
        }
        public void MarkSystemAsReady()
        {
            _statusService.MarkSystemAsInitialized();
        }

        // Metodo per aggiornare la ricetta corrente
        public void RefreshRecipeInfo()
        {
            LoadCurrentRecipe();
            OnPropertyChanged(nameof(CurrentRecipePath));
        }
        public void Dispose()
        {
            _refreshTimer?.Stop();
            _statusService.StatusChanged -= OnMachineStatusChanged;
            _statusService.PropertyChanged -= OnStatusServicePropertyChanged;
            _systemDiagnosticsService.PropertyChanged -= OnDiagnosticsServicePropertyChanged;
            _machineHealthNotificationService.WarningStateChanged -= OnAiWarningStateChanged;
            _powerFlex525Service.PropertyChanged -= OnPowerFlexServicePropertyChanged;
            _machineSpeedService.PropertyChanged -= OnMachineSpeedServicePropertyChanged;
            UserSession.OnRoleChanged -= OnUserRoleChanged;
            var counterManager = ServiceLocator.CounterManager;
            if (counterManager != null)
                counterManager.CountersUpdated -= OnCountersUpdated;

            // Ferma eventuali timer o processi in background
           // _refreshTimer?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(propertyName)));
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    // Aggiungi questa classe per gli event arguments
    public class UserChangedEventArgs : EventArgs
    {
        public string OldUser { get; set; }
        public string OldRole { get; set; }
        public string NewUser { get; set; }
        public string NewRole { get; set; }
    }
    public class AlarmInfo
    {
        public string Message { get; set; }
        public AlarmLevel Level { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum AlarmLevel
    {
        None,
        Info,
        Warning,
        Error,
        Critical
    }

    public class UserInfo
    {
        public string Username { get; set; }
        public string Role { get; set; }
        public DateTime LoginTime { get; set; }
    }
}
