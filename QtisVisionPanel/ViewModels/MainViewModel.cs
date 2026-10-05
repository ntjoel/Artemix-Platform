
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views;
using QtisVisionPanel.Views.UserControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;


namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// Main shell view model for the HMI.
    ///
    /// Responsibilities:
    /// - host current view navigation
    /// - bridge control buttons to the runtime service
    /// - coordinate logout/user-change side effects on navigation
    /// - route application shutdown through the controlled shutdown pipeline
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        public ControlButtonsViewModel ControlButtonsVM { get; }
        private object _currentView;
        private string _currentViewName;
        private readonly AuthorizationService _authService;
        private readonly MachineRuntimeService _machineRuntimeService;
        private readonly DialogService _dialogService;
        private readonly ViewFactoryService _viewFactoryService;
        private readonly ApplicationEventLogger _applicationEventLogger;
        private bool _shutdownRequested;
        private string _operatorNotificationText;
        public event EventHandler LanguageChanged;
        private bool _disposed = false;
        public object CurrentView
        {
            get => _currentView;
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        public string CurrentViewName
        {
            get => _currentViewName;
            set
            {
                //_currentView = $"{ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_View_info} {value}";
                _currentViewName = $"{ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_View_info} {value}";
                OnPropertyChanged();
            }
        }

        public NavigationViewModel NavigationVM { get;  set; }
        public TopMenuBarViewModel TopMenuBarVM { get;  }
        public SystemDiagnosticsViewModel DiagnosticsVM { get; }
        public bool HasShellNotification => HasOperatorNotification || (DiagnosticsVM?.HasSystemWarning ?? false);
        public bool HasOperatorNotification => !string.IsNullOrWhiteSpace(OperatorNotificationText);
        public bool HasDiagnosticsNotification => DiagnosticsVM?.HasSystemWarning ?? false;
        public bool HasBothShellNotifications => HasOperatorNotification && HasDiagnosticsNotification;
        public bool HasOnlyOperatorNotification => HasOperatorNotification && !HasDiagnosticsNotification;
        public bool HasOnlyDiagnosticsNotification => !HasOperatorNotification && HasDiagnosticsNotification;
        public string OperatorNotificationText
        {
            get => _operatorNotificationText;
            private set
            {
                if (_operatorNotificationText != value)
                {
                    _operatorNotificationText = value;
                    OnPropertyChanged();
                    NotifyShellNotificationStateChanged();
                }
            }
        }



        /// <summary>
        /// Builds the shell view model and wires navigation, top bar and control buttons.
        /// </summary>
        public MainViewModel()
        {
            _authService = ServiceLocator.Authorization;
            _machineRuntimeService = ServiceLocator.MachineRuntimeService;
            _dialogService = ServiceLocator.DialogService;
            _viewFactoryService = ServiceLocator.ViewFactoryService;
            _applicationEventLogger = ServiceLocator.ApplicationEventLogger;
            ServiceLocator.SystemDiagnosticsService.Start();
            DiagnosticsVM = new SystemDiagnosticsViewModel();
            DiagnosticsVM.PropertyChanged += OnDiagnosticsPropertyChanged;
            NavigationVM = new NavigationViewModel();
            TopMenuBarVM = new TopMenuBarViewModel();
           
            // Inizializza il ViewModel per i pulsanti di controllo
            ControlButtonsVM = NavigationVM.ControlButtonsVM;

            // Imposta lo stato iniziale (non pronto finché il sistema non è inizializzato)
            ControlButtonsVM.IsSystemReady = false;


            // Sottoscrivi agli eventi
            ControlButtonsVM.StartRequested += OnStartRequested;
            ControlButtonsVM.StopRequested += OnStopRequested;
            ControlButtonsVM.RestartRequested += OnRestartRequested;
            // ControlButtonsVM.ExternalAppRequested += OnExternalAppRequested;
            // Set initial view
            // Sottoscrivi all'evento di cambio utente
            TopMenuBarVM.UserChanged += OnUserChanged;
            var initialTarget = _viewFactoryService.CreateAsync("Channel1").GetAwaiter().GetResult();
            CurrentView = initialTarget.View;
            CurrentViewName = initialTarget.Title;

            // Subscribe to navigation events
            NavigationVM.NavigateRequested += OnNavigateRequested;
        }

        private void OnDiagnosticsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemDiagnosticsViewModel.HasSystemWarning) ||
                e.PropertyName == nameof(SystemDiagnosticsViewModel.SystemWarningText) ||
                e.PropertyName == nameof(SystemDiagnosticsViewModel.OverallSeverity))
            {
                NotifyShellNotificationStateChanged();
            }
        }

        private void NotifyShellNotificationStateChanged()
        {
            OnPropertyChanged(nameof(HasOperatorNotification));
            OnPropertyChanged(nameof(HasDiagnosticsNotification));
            OnPropertyChanged(nameof(HasBothShellNotifications));
            OnPropertyChanged(nameof(HasOnlyOperatorNotification));
            OnPropertyChanged(nameof(HasOnlyDiagnosticsNotification));
            OnPropertyChanged(nameof(HasShellNotification));
        }

        public void ShowOperatorNotification(string message)
        {
            OperatorNotificationText = message;
        }

        public void ClearOperatorNotification()
        {
            OperatorNotificationText = null;
        }

        private async void OnUserChanged(object sender, UserChangedEventArgs e)
        {
            try
            {
                // Controlla se la vista corrente è accessibile per il nuovo utente
                if (!await CanAccessView(CurrentViewName))
                {
                    // Se non è accessibile, torna alla vista Overview
                    OnNavigateRequested("Channel1");

                    MainWindow.logger?.Info($"Redirect alla vista principale dopo logout. " +
                                           $"Vecchia vista '{CurrentViewName}' non accessibile per {e.NewUser}");
                }
                else
                {
                    MainWindow.logger?.Info($"Utente cambiato: {e.OldUser} -> {e.NewUser}. " +
                                           $"Vista corrente '{CurrentViewName}' rimane accessibile.");
                }
                // Se siamo in RecipeManager, notifica il ViewModel di aggiornare i permessi
                if (CurrentViewName == "RecipeManager" && CurrentView is RecipeManagerView recipeView)
                {
                    if (recipeView.DataContext is RecipeManagerViewModel recipeVm)
                    {
                        recipeVm.UpdatePermissionsAsync().SafeFireAndForget();
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, $"USER_CHANGED_HANDLER_ERROR|user={e?.NewUser}");
                _applicationEventLogger?.LogException("USER_CHANGED_HANDLER_ERROR", ex, nameof(OnUserChanged), "Errore durante il cambio utente");
            }
        }
        // Metodo per segnalare che il sistema è pronto
        /// <summary>
        /// Called once startup initialization has completed and the controls may be used.
        /// </summary>
        public void MarkSystemAsReady()
        {
            ControlButtonsVM.IsSystemReady = true;
        }

        public void NotifyLanguageChanged()
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);

            // Aggiorna il nome della vista corrente
            if (CurrentViewName != null)
            {
                // Estrai il nome base senza il prefisso
                var parts = CurrentViewName.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    string viewName = string.Join(" ", parts.Skip(1));
                    CurrentViewName = viewName; // Forza refresh con nuova lingua
                }
            }
        }
        // Metodo per aggiornare lo stato dai pulsanti
        /// <summary>
        /// Refreshes start/stop button state from the runtime cache.
        /// </summary>
        public void UpdateControlButtonsState()
        {
            ControlButtonsVM.UpdateFromSystem();
        }
        /// <summary>
        /// Handles operator start requests and delegates the real runtime work to
        /// the machine runtime service.
        /// </summary>
        private void OnStartRequested()
        {
            OnStartRequestedCoreAsync().SafeFireAndForget();
        }

        private async Task OnStartRequestedCoreAsync()
        {
            try
            {
                _applicationEventLogger.LogCommand("StartVision", "Requested", nameof(OnStartRequested));
                _machineRuntimeService.ReleaseContinuousRunHold(MachineRuntimeService.HoldReasonManualStop, nameof(OnStartRequested));
                _machineRuntimeService.ReleaseContinuousRunHold(MachineRuntimeService.HoldReasonRecipeSave, nameof(OnStartRequested));

                // If the operator has already left Job Tool Editor, a stale
                // editor hold must never keep production blocked on other pages.
                if (!(CurrentView is JobToolEditorView))
                {
                    string activeHoldsBeforeStartCleanup = _machineRuntimeService.ContinuousRunHoldSummary;
                    bool releasedJobEditorHold = _machineRuntimeService.ReleaseContinuousRunHold(
                        MachineRuntimeService.HoldReasonJobEditor,
                        nameof(OnStartRequested));

                    if (releasedJobEditorHold)
                    {
                        var metadata = new Dictionary<string, object>
                        {
                            { "current_view", CurrentView?.GetType().Name ?? "Unknown" },
                            { "active_holds_before", string.IsNullOrWhiteSpace(activeHoldsBeforeStartCleanup) ? "none" : activeHoldsBeforeStartCleanup },
                            { "active_holds_after", string.IsNullOrWhiteSpace(_machineRuntimeService.ContinuousRunHoldSummary) ? "none" : _machineRuntimeService.ContinuousRunHoldSummary },
                            { "released_reason", MachineRuntimeService.HoldReasonJobEditor }
                        };

                        _applicationEventLogger.LogOperationalEvent(
                            NLog.LogLevel.Info,
                            "JOBEDITOR_HOLD_AUTO_RELEASED_ON_START",
                            "VisionPro",
                            "Stale Job Tool Editor hold released automatically before start",
                            nameof(OnStartRequested),
                            $"Current view: {CurrentView?.GetType().Name ?? "Unknown"}",
                            metadata);
                    }
                }

                // Disabilita temporaneamente i pulsanti durante l'operazione
                ControlButtonsVM.IsSystemReady = false;
                await _machineRuntimeService.ManageJobStateAsync(true);

                if (_machineRuntimeService.IsContinuousRunActive)
                {
                    _applicationEventLogger.LogCommand("StartVision", "Completed", nameof(OnStartRequested));
                }
                else
                {
                    string holdSummary = _machineRuntimeService.ContinuousRunHoldSummary;
                    string details = string.IsNullOrWhiteSpace(holdSummary)
                        ? "Start request completed without entering continuous run."
                        : $"Start request blocked by active hold(s): {holdSummary}.";

                    MainWindow.logger?.Warn($"STARTVISION_NOT_RUNNING|details={details}");
                    _applicationEventLogger.LogOperationalEvent(
                        NLog.LogLevel.Warn,
                        "STARTVISION_NOT_RUNNING",
                        "VisionPro",
                        "Start request completed but VisionPro is still stopped",
                        nameof(OnStartRequested),
                        details);
                }

                // Forza un aggiornamento immediato
                ControlButtonsVM.UpdateFromSystem(); // Aggiorna lo stato
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'avvio: {ex.Message}");
                _applicationEventLogger.LogException("START_COMMAND_FAILED", ex, nameof(OnStartRequested), "Errore nell'avvio del sistema di visione");
                // In caso di errore, ripristina lo stato
                ControlButtonsVM.UpdateFromSystem();
            }
        }
        /// <summary>
        /// Handles operator stop requests.
        /// </summary>
        private void OnStopRequested()
        {
            OnStopRequestedCoreAsync().SafeFireAndForget();
        }

        private async Task OnStopRequestedCoreAsync()
        {
            try
            {
                _applicationEventLogger.LogCommand("StopVision", "Requested", nameof(OnStopRequested));
                _machineRuntimeService.AddContinuousRunHold(MachineRuntimeService.HoldReasonManualStop, nameof(OnStopRequested));
                // Disabilita temporaneamente i pulsanti durante l'operazione
                ControlButtonsVM.IsSystemReady = false;
                await _machineRuntimeService.ManageJobStateAsync(false);
                _applicationEventLogger.LogCommand("StopVision", "Completed", nameof(OnStopRequested));
                // Forza un aggiornamento immediato
                ControlButtonsVM.UpdateFromSystem(); // Aggiorna lo stato
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'arresto: {ex.Message}");
                _applicationEventLogger.LogException("STOP_COMMAND_FAILED", ex, nameof(OnStopRequested), "Errore nell'arresto del sistema di visione");
                // In caso di errore, ripristina lo stato
                ControlButtonsVM.UpdateFromSystem();
            }
        }
        /// <summary>
        /// Handles the controlled application shutdown command from the UI.
        /// VisionPro is stopped immediately after confirmation, then the full
        /// shutdown sequence continues through the runtime service.
        /// </summary>
        private void OnRestartRequested()
        {
            OnRestartRequestedCoreAsync().SafeFireAndForget();
        }

        private async Task OnRestartRequestedCoreAsync()
        {
            if (_shutdownRequested || MainWindow.IsShuttingDown)
            {
                return;
            }

            try
            {
                _shutdownRequested = true;
                MainWindow.IsShuttingDown = true;
                _applicationEventLogger.LogCommand("ShutdownApplication", "Requested", nameof(OnRestartRequested));
                
                // Disabilita tutti i pulsanti durante la chiusura
                ControlButtonsVM.IsSystemReady = false;
                var shutdownWindow = _dialogService.CreateShutdownProgressWindow();
                shutdownWindow.Show();

                // Lascia renderizzare subito la finestra di stato prima di avviare la sequenza pesante.
                await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                await Task.Delay(100);

                shutdownWindow.UpdateStatus("Fermo VisionPro...", 5);
                try
                {
                    await _machineRuntimeService.StopContinuousRunAsync();
                    ControlButtonsVM.IsRunning = false;
                    ControlButtonsVM.UpdateFromSystem();
                    _applicationEventLogger.LogVisionProStatus(false, nameof(OnRestartRequested), "Continuous run stopped immediately after shutdown confirmation");
                }
                catch (Exception stopEx)
                {
                    MainWindow.logger?.Warn($"Errore nello stop immediato di VisionPro durante shutdown: {stopEx.Message}");
                }

                await _machineRuntimeService.ShutdownApplicationAsync(shutdownWindow);

                // Chiudi la finestra di progresso
                if (shutdownWindow.IsVisible)
                {
                    shutdownWindow.Close();
                }


                // Dopo la chiusura completa, termina l'applicazione
                _applicationEventLogger.LogCommand("ShutdownApplication", "Completed", nameof(OnRestartRequested));
                Application.Current.Shutdown(0); // Codice di successo 0
            }
            catch (ObjectDisposedException ex)
            {
                MainWindow.logger?.Warn($"Chiusura completata con oggetti già dispose-ati: {ex.Message}");
                Application.Current.Shutdown(0);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nella chiusura dell'applicazione: {ex.Message}");
                _applicationEventLogger.LogException("SHUTDOWN_COMMAND_FAILED", ex, nameof(OnRestartRequested), "Errore durante la chiusura dell'applicazione");

                if (!MainWindow.IsShuttingDown)
                {
                    _dialogService.ShowError($"Errore nella chiusura: {ex.Message}\nForzando la chiusura...",
                        "Errore");
                }

                Application.Current.Shutdown(MainWindow.IsShuttingDown ? 0 : 1);
            }
        }
   
        /// <summary>
        /// Central navigation entry point used by the side menu.
        /// It performs authorization checks, ensures continuous run when needed
        /// and creates the requested view through the view factory.
        /// </summary>
        public void OnNavigateRequested(string viewName)
        {
            OnNavigateRequestedCoreAsync(viewName).SafeFireAndForget();
        }

        private async Task OnNavigateRequestedCoreAsync(string viewName)
        {
            // Il NavigationViewModel ha già controllato le autorizzazioni,
            // ma facciamo un doppio controllo per sicurezza
            if (! await CanAccessView(viewName))
            {
                _applicationEventLogger.LogCommand(viewName, "AccessDenied", nameof(OnNavigateRequested));
                _dialogService.ShowWarning("Non hai i permessi per accedere a questa vista",
                    "Accesso negato");
                return;
            }

            try
            {
                _applicationEventLogger.LogCommand(viewName, "NavigationRequested", nameof(OnNavigateRequested));

                if (!await PrepareCurrentViewForNavigationAsync(viewName))
                {
                    _applicationEventLogger.LogCommand(viewName, "NavigationCancelled", nameof(OnNavigateRequested));
                    return;
                }

                if (string.Equals(viewName, "Channel1", StringComparison.OrdinalIgnoreCase))
                {
                    var token = MainWindow._continuousRunCts?.Token ?? CancellationToken.None;
                    await _machineRuntimeService.EnsureContinuousRunForAllChannelAsync(token, nameof(OnNavigateRequested));
                }

                var target = await _viewFactoryService.CreateAsync(viewName);
                CurrentView = target.View;
                CurrentViewName = target.Title;
                _applicationEventLogger.LogCommand(viewName, "NavigationCompleted", nameof(OnNavigateRequested));
            }
            catch (Exception ex)
            {
                _applicationEventLogger.LogException("NAVIGATION_FAILED", ex, nameof(OnNavigateRequested), $"Errore durante la navigazione verso {viewName}");
                _dialogService.ShowError($"Impossibile aprire la vista richiesta.\n{ex.Message}", "Errore navigazione");
            }
        }

        /// <summary>
        /// Executes view-specific exit logic before replacing the current content.
        /// This is especially important for maintenance views such as the
        /// Job Tool Editor, which hold the machine in a protected stopped state
        /// while the operator is editing VisionPro tools.
        /// </summary>
        private async Task<bool> PrepareCurrentViewForNavigationAsync(string targetViewName)
        {
            if (CurrentView == null)
            {
                return true;
            }

            if (CurrentView is JobToolEditorView jobEditorView &&
                !string.Equals(targetViewName, "JobToolEditor", StringComparison.OrdinalIgnoreCase))
            {
                if (jobEditorView.DataContext is JobToolEditorViewModel jobEditorVm)
                {
                    if (jobEditorVm.HasChanges)
                    {
                        var confirmExit = new SystemNotificationWindow(
                            ServerMessagePersonalize.GetMessageOrDefault(
                                "Sub_entry_JobEditorUnsavedExitTitle",
                                "Unsaved job changes"),
                            ServerMessagePersonalize.GetMessageOrDefault(
                                "Sub_entry_JobEditorUnsavedExitMessage",
                                "Leave the Job Tool Editor without saving? Unsaved VisionPro tool changes will be lost."),
                            NotificationSeverity.Warning,
                            true);
                        confirmExit.ShowDialog();

                        if (!confirmExit.Confirmed)
                        {
                            _applicationEventLogger.LogOperationalEvent(
                                NLog.LogLevel.Warn,
                                "JOBEDITOR_NAVIGATION_CANCELLED_UNSAVED_CHANGES",
                                "VisionPro",
                                "Navigation cancelled because Job Tool Editor has unsaved changes",
                                nameof(PrepareCurrentViewForNavigationAsync),
                                $"Target view: {targetViewName}.",
                                new Dictionary<string, object>
                                {
                                    { "target_view", targetViewName },
                                    { "active_holds", string.IsNullOrWhiteSpace(_machineRuntimeService.ContinuousRunHoldSummary) ? "none" : _machineRuntimeService.ContinuousRunHoldSummary }
                                });
                            return false;
                        }
                    }

                    string activeHoldsBeforeNavigation = _machineRuntimeService.ContinuousRunHoldSummary;
                    bool hadEditorHoldBeforeExit = IsRuntimeHoldActive(MachineRuntimeService.HoldReasonJobEditor);
                    bool shouldResumeAfterExit = jobEditorVm.ShouldResumeContinuousRunAfterEditor;

                    if (jobEditorVm.StopLiveDisplayCommand?.CanExecute(null) == true)
                    {
                        jobEditorVm.StopLiveDisplayCommand.Execute(null);
                    }

                    MainWindow.logger?.Info(
                        $"JOBEDITOR_NAVIGATION_EXIT|target={targetViewName}|action=release-editor-hold");
                    await jobEditorVm.OnEditorViewUnloadedAsync();

                    // Defensive release: in production we must not rely on the
                    // WPF editor lifecycle alone to free a protected stop state.
                    bool defensiveRelease = _machineRuntimeService.ReleaseContinuousRunHold(
                        MachineRuntimeService.HoldReasonJobEditor,
                        nameof(PrepareCurrentViewForNavigationAsync));

                    bool holdClearedByNavigation = hadEditorHoldBeforeExit &&
                                                   !IsRuntimeHoldActive(MachineRuntimeService.HoldReasonJobEditor);

                    if (holdClearedByNavigation || defensiveRelease)
                    {
                        var metadata = new Dictionary<string, object>
                        {
                            { "target_view", targetViewName },
                            { "resume_requested", shouldResumeAfterExit },
                            { "defensive_release", defensiveRelease },
                            { "had_editor_hold_before_exit", hadEditorHoldBeforeExit },
                            { "hold_cleared_by_navigation", holdClearedByNavigation },
                            { "active_holds_before", string.IsNullOrWhiteSpace(activeHoldsBeforeNavigation) ? "none" : activeHoldsBeforeNavigation },
                            { "active_holds_after", string.IsNullOrWhiteSpace(_machineRuntimeService.ContinuousRunHoldSummary) ? "none" : _machineRuntimeService.ContinuousRunHoldSummary }
                        };

                        _applicationEventLogger.LogOperationalEvent(
                            NLog.LogLevel.Info,
                            "JOBEDITOR_HOLD_AUTO_RELEASED_ON_NAVIGATION",
                            "VisionPro",
                            "Job Tool Editor hold released automatically during navigation",
                            nameof(PrepareCurrentViewForNavigationAsync),
                            $"Target view: {targetViewName}. Resume requested: {shouldResumeAfterExit}. Defensive release: {defensiveRelease}.",
                            metadata);
                    }
                }
            }

            return true;
        }

        private bool IsRuntimeHoldActive(string reason)
        {
            string holdSummary = _machineRuntimeService.ContinuousRunHoldSummary;
            if (string.IsNullOrWhiteSpace(holdSummary))
            {
                return false;
            }

            return holdSummary
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(item => string.Equals(item.Trim(), reason, StringComparison.OrdinalIgnoreCase));
        }
        /// <summary>
        /// Double-checks view permissions using the authorization cache/service.
        /// </summary>
        private async Task<bool> CanAccessView(string viewName)
        {
            // Controllo tramite database per tutti gli utenti loggati
            if (_authService.CanAccessViewCached(viewName))
            {
                return true;
            }

            return await _authService.CanAccessView(viewName).ConfigureAwait(false);
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        // Metodo per cleanup
        /// <summary>
        /// Releases view-model level resources during application shutdown.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                ControlButtonsVM.StartRequested -= OnStartRequested;
                ControlButtonsVM.StopRequested -= OnStopRequested;
                ControlButtonsVM.RestartRequested -= OnRestartRequested;
            }
            catch
            {
            }

            try
            {
                if (TopMenuBarVM != null)
                {
                    TopMenuBarVM.UserChanged -= OnUserChanged;
                }
            }
            catch
            {
            }

            try
            {
                if (NavigationVM != null)
                {
                    NavigationVM.NavigateRequested -= OnNavigateRequested;
                }
            }
            catch
            {
            }

            try
            {
                if (DiagnosticsVM != null)
                {
                    DiagnosticsVM.PropertyChanged -= OnDiagnosticsPropertyChanged;
                    DiagnosticsVM.Dispose();
                }
            }
            catch (ObjectDisposedException) { MainWindow.logger?.Debug("DiagnosticsVM already disposed — non-critical"); }

            DisposeCurrentView();

            try
            {
                TopMenuBarVM?.Dispose();
            }
            catch (ObjectDisposedException) { MainWindow.logger?.Debug("TopMenuBarVM already disposed — non-critical"); }

            try
            {
                ControlButtonsVM?.Dispose();

            }
            catch (ObjectDisposedException) { MainWindow.logger?.Debug("ControlButtonsVM already disposed — non-critical"); }
        }

        private void DisposeCurrentView()
        {
            try
            {
                if (CurrentView is IDisposable disposableView)
                {
                    disposableView.Dispose();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore dispose view corrente: {ex.Message}");
            }

            try
            {
                if (CurrentView is FrameworkElement element && element.DataContext is IDisposable disposableViewModel)
                {
                    disposableViewModel.Dispose();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore dispose viewmodel corrente: {ex.Message}");
            }

            CurrentView = null;
        }
    }

}
