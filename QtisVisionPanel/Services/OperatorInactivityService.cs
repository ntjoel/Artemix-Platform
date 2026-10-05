using NLog;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Supervises panel inactivity to support two industrial behaviors:
    /// - auto-resume VisionPro continuous mode after the panel stays idle
    /// - auto-logout of the operator after prolonged inactivity
    ///
    /// The service listens to generic user activity coming from the main window
    /// and periodically evaluates whether the machine should resume or logout.
    /// </summary>
    public class OperatorInactivityService : IDisposableService
    {
        private readonly object _syncRoot = new object();
        private readonly SemaphoreSlim _evaluationLock = new SemaphoreSlim(1, 1);
        private readonly ApplicationEventLogger _eventLogger = ServiceLocator.ApplicationEventLogger;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private DispatcherTimer _timer;
        private MainWindow _mainWindow;
        private DateTime _lastActivityUtc = DateTime.UtcNow;
        private DateTime _lastAutoResumeAttemptUtc = DateTime.MinValue;
        private bool _isStarted;
        private bool _isDisposed;
        private bool _isShuttingDown;

        /// <summary>
        /// Required by <see cref="IDisposableService"/> so shutdown orchestration
        /// can know whether the service is already leaving its active state.
        /// </summary>
        public bool IsShuttingDown => _isShuttingDown;

        /// <summary>
        /// Attaches the main window used for panel/window presence checks and
        /// for auto-logout operations routed through the main view model.
        /// </summary>
        public void AttachMainWindow(MainWindow mainWindow)
        {
            lock (_syncRoot)
            {
                _mainWindow = mainWindow;
            }
        }

        /// <summary>
        /// Registers user activity coming from mouse, touch, stylus or keyboard.
        /// The small debounce prevents floods of UI events from generating
        /// unnecessary work.
        /// </summary>
        public void RegisterActivity(string source = null)
        {
            if (_isDisposed || _isShuttingDown || MainWindow.IsShuttingDown)
                return;

            var nowUtc = DateTime.UtcNow;

            lock (_syncRoot)
            {
                if (nowUtc - _lastActivityUtc < TimeSpan.FromMilliseconds(250))
                    return;

                _lastActivityUtc = nowUtc;
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                _logger.Debug($"Operator activity detected: {source}");
            }
        }

        /// <summary>
        /// Starts the inactivity polling timer on the WPF dispatcher.
        /// </summary>
        public void Start()
        {
            if (_isStarted || _isDisposed || Application.Current?.Dispatcher == null)
                return;

            _timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            _isStarted = true;
            RegisterActivity("service-start");
        }

        /// <summary>
        /// Dispatcher-timer bridge. The heavy logic stays in <see cref="EvaluateAsync"/>.
        /// </summary>
        private async void OnTimerTick(object sender, EventArgs e)
        {
            await EvaluateAsync();
        }

        /// <summary>
        /// Periodic supervisor routine.
        /// Order of evaluation:
        /// 1. skip if app is shutting down or a modal/secondary window is open
        /// 2. if operator is logged in and idle too long, auto-logout
        /// 3. if VisionPro is stopped and the panel is idle, auto-resume
        /// </summary>
        private async Task EvaluateAsync()
        {
            if (_isDisposed || _isShuttingDown || MainWindow.IsShuttingDown)
                return;

            if (!await _evaluationLock.WaitAsync(0))
                return;

            try
            {
                var configuration = MainWindow.configManager?.Config?.Configuration;
                if (configuration == null)
                    return;

                MainWindow mainWindow;
                DateTime lastActivityUtc;

                lock (_syncRoot)
                {
                    mainWindow = _mainWindow;
                    lastActivityUtc = _lastActivityUtc;
                }

                if (mainWindow == null)
                    return;

                if (HasVisibleSecondaryWindow(mainWindow))
                    return;

                var idleTime = DateTime.UtcNow - lastActivityUtc;

                if (UserSession.ShouldAutoLoginAdministratorFromConfig())
                {
                    return;
                }

                if (configuration.AutoLogoutWhenIdle &&
                    UserSession.IsLoggedIn &&
                    idleTime >= TimeSpan.FromMinutes(Math.Max(configuration.AutoLogoutIdleMinutes, 1)))
                {
                    await PerformAutoLogoutAsync(mainWindow, idleTime);
                    return;
                }

                if (!configuration.AutoResumeVisionWhenIdle)
                    return;

                if (MachineStatusService.Instance.IsSystemInitializing)
                    return;

                var runtimeService = ServiceLocator.MachineRuntimeService;
                var manager = MainWindow._cognexManager;
                if (manager == null || runtimeService == null || runtimeService.IsContinuousRunActive)
                    return;

                if (!runtimeService.CanAutoResumeContinuousRun())
                    return;

                if (idleTime < TimeSpan.FromSeconds(Math.Max(configuration.AutoResumeVisionIdleSeconds, 5)))
                    return;

                if (DateTime.UtcNow - _lastAutoResumeAttemptUtc <
                    TimeSpan.FromSeconds(Math.Max(configuration.AutoResumeVisionRetryCooldownSeconds, 10)))
                    return;

                await ResumeVisionAsync(idleTime);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Operator inactivity evaluation failed: {ex.Message}");
            }
            finally
            {
                _evaluationLock.Release();
            }
        }

        /// <summary>
        /// Guards automation so it does not interfere with explicit operator dialogs.
        /// </summary>
        private static bool HasVisibleSecondaryWindow(MainWindow mainWindow)
        {
            try
            {
                return Application.Current?.Windows
                    .OfType<Window>()
                    .Any(window => window != mainWindow && window.IsVisible) == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Requests VisionPro continuous mode again after the configured idle time.
        /// </summary>
        private async Task ResumeVisionAsync(TimeSpan idleTime)
        {
            _lastAutoResumeAttemptUtc = DateTime.UtcNow;

            try
            {
                if (MainWindow._continuousRunCts == null || MainWindow._continuousRunCts.IsCancellationRequested)
                {
                    try
                    {
                        MainWindow._continuousRunCts?.Dispose();
                    }
                    catch
                    {
                    }

                    MainWindow._continuousRunCts = new CancellationTokenSource();
                }

                _logger.Info($"AUTO_RUNCONTINUOUS_REQUEST|idle_seconds={idleTime.TotalSeconds:0}");
                _eventLogger.LogOperationalEvent(
                    LogLevel.Info,
                    "VISIONPRO_IDLE_AUTORESUME_REQUESTED",
                    "VisionPro",
                    "VisionPro auto-resume requested after panel inactivity",
                    nameof(OperatorInactivityService),
                    $"No panel activity for {idleTime.TotalSeconds:0} seconds while VisionPro was stopped.",
                    null);

                await ServiceLocator.MachineRuntimeService.StartContinuousRunAsync(MainWindow._continuousRunCts.Token).ConfigureAwait(false);
                MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;

                _eventLogger.LogOperationalEvent(
                    LogLevel.Info,
                    "VISIONPRO_IDLE_AUTORESUME_COMPLETED",
                    "VisionPro",
                    "VisionPro auto-resume completed after panel inactivity",
                    nameof(OperatorInactivityService),
                    $"Continuous run active: {ServiceLocator.MachineRuntimeService.IsContinuousRunActive}",
                    null);

                RegisterActivity("auto-runcontinuous");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Automatic VisionPro resume failed: {ex.Message}");
                _eventLogger.LogException(
                    "VISIONPRO_IDLE_AUTORESUME_FAILED",
                    ex,
                    nameof(OperatorInactivityService),
                    "VisionPro auto-resume failed after panel inactivity");
            }
        }

        /// <summary>
        /// Executes automatic logout through the same top-bar workflow used by
        /// the manual operator logout.
        /// </summary>
        private async Task PerformAutoLogoutAsync(MainWindow mainWindow, TimeSpan idleTime)
        {
            try
            {
                if (!(mainWindow.DataContext is ViewModels.MainViewModel mainVm) || mainVm.TopMenuBarVM == null)
                    return;

                _logger.Info($"AUTO_LOGOUT_REQUEST|idle_minutes={idleTime.TotalMinutes:0.0}|user={UserSession.CurrentUser}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    mainVm.TopMenuBarVM.ExecuteAutomaticLogout(
                        nameof(OperatorInactivityService),
                        $"Automatic logout after {idleTime.TotalMinutes:0.0} minutes of panel inactivity.");
                });

                RegisterActivity("auto-logout");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Automatic logout failed: {ex.Message}");
                _eventLogger.LogException(
                    "USER_AUTO_LOGOUT_FAILED",
                    ex,
                    nameof(OperatorInactivityService),
                    "Automatic logout failed after panel inactivity");
            }
        }

        /// <summary>
        /// Stops the timer as part of the coordinated application shutdown.
        /// </summary>
        public async Task ShutdownAsync()
        {
            _isShuttingDown = true;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Releases timer resources. Safe to call more than once.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isShuttingDown = true;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }

            _evaluationLock.Dispose();
        }
    }
}
