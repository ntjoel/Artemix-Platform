using NLog;
using QtisVisionPanel.Cls_Vpro;
using QtisVisionPanel.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Central runtime coordinator for VisionPro continuous execution.
    /// This service is the single source of truth for:
    /// - requested run state
    /// - actual cached run state
    /// - vision health / fault state
    /// - serialized start/stop/recovery operations
    ///
    /// The implementation intentionally avoids frequent direct reads from the
    /// Cognex manager on the UI thread because those reads can freeze the HMI
    /// while COM/hardware state transitions are still in progress.
    /// </summary>
    public class MachineRuntimeService
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public const string HoldReasonManualStop = "manual-stop";
        public const string HoldReasonRecipeSave = "recipe-save";
        public const string HoldReasonJobEditor = "job-editor";
        public const string HoldReasonShutdown = "shutdown";

        private readonly object _syncRoot = new object();
        private readonly SemaphoreSlim _continuousRunCommandSemaphore = new SemaphoreSlim(1, 1);
        private readonly HashSet<string> _continuousRunHoldReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _isContinuousRunActive;
        private bool _isContinuousRunRequested;
        private bool _isVisionHealthy = true;
        private string _lastVisionIssue;
        private int _continuousRunCommandInProgress;
        private static readonly TimeSpan StartContinuousRunTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan StopContinuousRunTimeout = TimeSpan.FromSeconds(8);

        public event EventHandler<bool> ContinuousRunStateChanged;

        /// <summary>
        /// Back-reference to the main shell used for delegated operations that
        /// still belong to <see cref="MainWindow"/> (job state orchestration, shutdown).
        /// </summary>
        public MainWindow MainWindow { get; private set; }

        /// <summary>
        /// Attached Cognex job manager instance currently serving the machine.
        /// </summary>
        public ICognexJobManager CognexManager { get; private set; }

        /// <summary>
        /// Cached runtime state used by UI, watchdog and services to know if
        /// VisionPro is currently running in continuous mode.
        /// </summary>
        public bool IsContinuousRunActive
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isContinuousRunActive;
                }
            }
        }

        /// <summary>
        /// Cached health flag set by the watchdog and recovery pipeline.
        /// </summary>
        public bool IsVisionHealthy
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isVisionHealthy;
                }
            }
        }

        /// <summary>
        /// Indicates whether the machine logic expects VisionPro to be in
        /// continuous mode, even if the actual job state is temporarily degraded.
        /// </summary>
        public bool IsContinuousRunRequested
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isContinuousRunRequested;
                }
            }
        }

        /// <summary>
        /// Returns true when one or more intentional runtime holds are active.
        /// Holds are used to distinguish manual/maintenance pauses from
        /// unexpected runtime stops that may be auto-resumed safely.
        /// </summary>
        public bool HasContinuousRunHold
        {
            get
            {
                lock (_syncRoot)
                {
                    return _continuousRunHoldReasons.Count > 0;
                }
            }
        }

        /// <summary>
        /// Human-readable summary of active runtime hold reasons.
        /// </summary>
        public string ContinuousRunHoldSummary
        {
            get
            {
                lock (_syncRoot)
                {
                    return string.Join(",", _continuousRunHoldReasons.OrderBy(reason => reason));
                }
            }
        }

        /// <summary>
        /// Last human-readable vision issue registered by the watchdog.
        /// </summary>
        public string LastVisionIssue
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastVisionIssue;
                }
            }
        }

        /// <summary>
        /// Attaches the main shell after startup. The service stays decoupled
        /// from WPF construction and receives the window only when available.
        /// </summary>
        public void AttachMainWindow(MainWindow mainWindow)
        {
            lock (_syncRoot)
            {
                MainWindow = mainWindow;
            }
        }

        /// <summary>
        /// Attaches the active Cognex manager instance used by runtime commands.
        /// </summary>
        public void AttachCognexManager(ICognexJobManager cognexManager)
        {
            lock (_syncRoot)
            {
                CognexManager = cognexManager;
            }
        }

        /// <summary>
        /// Updates the cached actual run state.
        /// Used after start/stop commands and after stop notifications coming
        /// from VisionPro events.
        /// </summary>
        public void UpdateContinuousRunState(bool isActive)
        {
            bool changed;
            lock (_syncRoot)
            {
                changed = _isContinuousRunActive != isActive;
                _isContinuousRunActive = isActive;
            }

            if (changed)
            {
                ContinuousRunStateChanged?.Invoke(this, isActive);
            }
        }

        /// <summary>
        /// Updates the desired run state tracked by watchdog and auto-recovery.
        /// </summary>
        public void UpdateContinuousRunRequested(bool isRequested)
        {
            lock (_syncRoot)
            {
                _isContinuousRunRequested = isRequested;
            }
        }

        /// <summary>
        /// Pulls the current continuous-run state from the Cognex manager and
        /// writes it into the local cache.
        ///
        /// This method intentionally refuses to query the manager from the UI
        /// thread and while a start/stop command is already in flight.
        /// </summary>
        public void SyncStateFromManager()
        {
            if (IsContinuousRunCommandInProgress)
            {
                return;
            }

            try
            {
                if (Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    return;
                }
            }
            catch
            {
            }

            bool currentState = false;

            try
            {
                currentState = CognexManager?.IsRunningContinuously ?? false;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Unable to read VisionPro running state from manager: {ex.Message}");
            }

            lock (_syncRoot)
            {
                _isContinuousRunActive = currentState;
            }
        }

        /// <summary>
        /// Returns true while a serialized start/stop command is still executing.
        /// UI/watchdog logic uses this to avoid overlapping transitions.
        /// </summary>
        public bool IsContinuousRunCommandInProgress
        {
            get { return Interlocked.CompareExchange(ref _continuousRunCommandInProgress, 0, 0) == 1; }
        }

        /// <summary>
        /// Registers an intentional runtime hold. While a hold is active the
        /// machine is allowed to stay stopped without auto-resume forcing it
        /// back into continuous execution.
        /// </summary>
        public void AddContinuousRunHold(string reason, string source = null)
        {
            string normalizedReason = NormalizeHoldReason(reason);
            bool added;
            string summary;

            lock (_syncRoot)
            {
                added = _continuousRunHoldReasons.Add(normalizedReason);
                _isContinuousRunRequested = false;
                summary = string.Join(",", _continuousRunHoldReasons.OrderBy(item => item));
            }

            if (added)
            {
                MainWindow.logger?.Info(
                    $"VISIONPRO_RUN_HOLD_ADDED|reason={normalizedReason}|source={source ?? "runtime"}|active_holds={summary}");
            }
        }

        /// <summary>
        /// Removes a previously registered intentional runtime hold.
        /// </summary>
        public bool ReleaseContinuousRunHold(string reason, string source = null)
        {
            string normalizedReason = NormalizeHoldReason(reason);
            bool removed;
            string summary;

            lock (_syncRoot)
            {
                removed = _continuousRunHoldReasons.Remove(normalizedReason);
                summary = string.Join(",", _continuousRunHoldReasons.OrderBy(item => item));
            }

            if (removed)
            {
                MainWindow.logger?.Info(
                    $"VISIONPRO_RUN_HOLD_RELEASED|reason={normalizedReason}|source={source ?? "runtime"}|active_holds={(string.IsNullOrWhiteSpace(summary) ? "none" : summary)}");
            }

            return removed;
        }

        /// <summary>
        /// Tells callers whether automatic resume is currently allowed.
        /// </summary>
        public bool CanAutoResumeContinuousRun()
        {
            lock (_syncRoot)
            {
                return _continuousRunHoldReasons.Count == 0;
            }
        }

        public bool IsContinuousRunHoldActive(string reason)
        {
            string normalizedReason = NormalizeHoldReason(reason);
            lock (_syncRoot)
            {
                return _continuousRunHoldReasons.Contains(normalizedReason);
            }
        }

        /// <summary>
        /// Clears the current fault and marks the vision subsystem as healthy.
        /// </summary>
        public void MarkVisionHealthy()
        {
            lock (_syncRoot)
            {
                _isVisionHealthy = true;
                _lastVisionIssue = null;
            }
        }

        /// <summary>
        /// Marks the vision subsystem as unhealthy and records a short reason.
        /// </summary>
        public void MarkVisionFault(string issueDetails)
        {
            lock (_syncRoot)
            {
                _isVisionHealthy = false;
                _lastVisionIssue = issueDetails;
            }
        }

        /// <summary>
        /// Delegates the user-driven start/stop request to the main window flow.
        /// </summary>
        public async Task ManageJobStateAsync(bool start)
        {
            var mainWindow = MainWindow ?? throw new InvalidOperationException("Main window is not attached.");
            await mainWindow.ManageJobStateAsync(start);
        }

        /// <summary>
        /// Delegates full application shutdown to the main window sequence.
        /// </summary>
        public async Task ShutdownApplicationAsync(ShutdownProgressWindow progressWindow)
        {
            var mainWindow = MainWindow ?? throw new InvalidOperationException("Main window is not attached.");
            await mainWindow.ShutdownApplicationAsync(progressWindow);
        }

        /// <summary>
        /// Fire-and-forget wrapper kept for backward compatibility with older
        /// call sites. New code should prefer <see cref="StartContinuousRunAsync"/>.
        /// </summary>
        public void StartContinuousRun(CancellationToken token)
        {
            _ = StartContinuousRunAsync(token);
        }

        /// <summary>
        /// Starts VisionPro continuous execution in a serialized, timeout-guarded
        /// way. The method updates the desired state before starting, because in
        /// an industrial HMI the operator intent matters even while hardware is
        /// transitioning.
        /// </summary>
        public async Task StartContinuousRunAsync(CancellationToken token)
        {
            await _continuousRunCommandSemaphore.WaitAsync().ConfigureAwait(false);
            Interlocked.Exchange(ref _continuousRunCommandInProgress, 1);

            try
            {
                string holdSummary = ContinuousRunHoldSummary;
                if (!string.IsNullOrWhiteSpace(holdSummary))
                {
                    UpdateContinuousRunRequested(false);
                    UpdateContinuousRunState(false);
                    MainWindow.logger?.Info($"Continuous run start skipped because runtime hold(s) are active: {holdSummary}");
                    return;
                }

                UpdateContinuousRunRequested(true);

                string opcRunBlockReason;
                if (!ServiceLocator.OpcUaClientService.CanMachineRun(out opcRunBlockReason))
                {
                    UpdateContinuousRunRequested(false);
                    UpdateContinuousRunState(false);
                    MarkVisionFault(opcRunBlockReason);
                    MainWindow.logger?.Warn($"OPCUA_RUN_BLOCKED|reason={opcRunBlockReason}");
                    ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                        LogLevel.Warn,
                        "OPCUA_RUN_BLOCKED",
                        "OPC UA",
                        "Continuous run blocked because OPC UA is required but not connected",
                        nameof(StartContinuousRunAsync),
                        opcRunBlockReason);
                    return;
                }

                var manager = CognexManager;
                if (manager != null && !IsContinuousRunActive)
                {
                    bool completed = await ExecuteManagerOperationWithTimeoutAsync(
                        () => manager.RunContinuous(token),
                        StartContinuousRunTimeout,
                        nameof(StartContinuousRunAsync)).ConfigureAwait(false);

                    if (!completed)
                    {
                        UpdateContinuousRunState(false);
                        MarkVisionFault("Timed out while starting VisionPro continuous run.");
                        return;
                    }
                }

                // Read state directly: SyncStateFromManager() guards against running
                // while IsContinuousRunCommandInProgress is set, which it still is here.
                bool isNowRunning = false;
                try { isNowRunning = CognexManager?.IsRunningContinuously ?? false; }
                catch (Exception ex) { _log.Warn(ex, "COGNEX_STATE_READ_FAILED - assuming not running"); }
                UpdateContinuousRunState(isNowRunning);

                if (isNowRunning)
                {
                    MarkVisionHealthy();
                }
                else
                {
                    string failureDetails = CognexManager?.LastContinuousRunFailure;
                    if (string.IsNullOrWhiteSpace(failureDetails))
                    {
                        failureDetails = "Continuous run is requested but one or more VisionPro jobs did not enter running state.";
                    }

                    MarkVisionFault(failureDetails);
                    ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                        LogLevel.Warn,
                        "VISIONPRO_CONTINUOUS_START_INCOMPLETE",
                        "VisionPro",
                        "Continuous run did not start for every VisionPro job",
                        nameof(StartContinuousRunAsync),
                        failureDetails);
                }
                ServiceLocator.ApplicationEventLogger.LogVisionProStatus(IsContinuousRunActive, nameof(StartContinuousRunAsync), "Continuous run requested");
                ServiceLocator.OpcUaClientService.WriteIsRunningAsync(IsContinuousRunActive).SafeFireAndForget(_log, "OPCUA_WRITE_RUNNING_STATE_FAILED");
            }
            finally
            {
                Interlocked.Exchange(ref _continuousRunCommandInProgress, 0);
                _continuousRunCommandSemaphore.Release();
            }
        }

        /// <summary>
        /// Lightweight helper used by navigation/startup paths to request
        /// continuous mode only when it is not already active or transitioning.
        /// </summary>
        public void EnsureContinuousRun()
        {
            if (IsContinuousRunActive || IsContinuousRunCommandInProgress || HasContinuousRunHold)
            {
                return;
            }

            var token = MainWindow._continuousRunCts?.Token ?? CancellationToken.None;
            _ = StartContinuousRunAsync(token);
        }

        /// <summary>
        /// Ensures VisionPro is running when the operator opens the All Channel
        /// overview. This path reads the real Cognex state off the UI thread and
        /// starts continuous mode only when the manager reports it is stopped.
        /// </summary>
        public async Task EnsureContinuousRunForAllChannelAsync(CancellationToken token, string source = null)
        {
            var caller = string.IsNullOrWhiteSpace(source) ? nameof(EnsureContinuousRunForAllChannelAsync) : source;

            if (IsContinuousRunCommandInProgress)
            {
                MainWindow.logger?.Info($"VISIONPRO_ALLCHANNEL_ENSURE_SKIPPED|source={caller}|reason=command-in-progress");
                return;
            }

            ReleaseContinuousRunHold(HoldReasonManualStop, caller);

            var actualRunning = await ReadContinuousRunStateFromManagerAsync(caller).ConfigureAwait(false);
            if (actualRunning)
            {
                UpdateContinuousRunState(true);
                MarkVisionHealthy();
                MainWindow.logger?.Info($"VISIONPRO_ALLCHANNEL_ALREADY_RUNNING|source={caller}");
                return;
            }

            var holdSummary = ContinuousRunHoldSummary;
            if (!string.IsNullOrWhiteSpace(holdSummary))
            {
                MainWindow.logger?.Info($"VISIONPRO_ALLCHANNEL_START_SKIPPED|source={caller}|active_holds={holdSummary}");
                return;
            }

            MainWindow.logger?.Info($"VISIONPRO_ALLCHANNEL_AUTO_START|source={caller}|reason=not-running");
            await StartContinuousRunAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Synchronous stop request kept for legacy call sites.
        /// When <paramref name="preserveRequestedState"/> is true, the watchdog
        /// can still consider continuous mode as desired during auto-recovery.
        /// </summary>
        public void StopContinuousRun(bool preserveRequestedState = false)
        {
            if (!preserveRequestedState)
            {
                UpdateContinuousRunRequested(false);
            }

            CognexManager?.StopContinuousRunAsync();
            UpdateContinuousRunState(false);
            MarkVisionHealthy();
            ServiceLocator.ApplicationEventLogger.LogVisionProStatus(false, nameof(StopContinuousRun), "Continuous run stop requested");
            ServiceLocator.OpcUaClientService.WriteIsRunningAsync(false).SafeFireAndForget(_log, "OPCUA_WRITE_RUNNING_STATE_FAILED");
        }

        /// <summary>
        /// Stops VisionPro continuous execution in a serialized, timeout-guarded
        /// way. This is the preferred API for manual stop and auto-recovery.
        /// </summary>
        public async Task StopContinuousRunAsync(bool preserveRequestedState = false)
        {
            await _continuousRunCommandSemaphore.WaitAsync().ConfigureAwait(false);
            Interlocked.Exchange(ref _continuousRunCommandInProgress, 1);

            try
            {
                if (!preserveRequestedState)
                {
                    UpdateContinuousRunRequested(false);
                }

                var manager = CognexManager;
                if (manager != null)
                {
                    bool completed = await ExecuteManagerOperationWithTimeoutAsync(
                        manager.StopContinuousRunAsync,
                        StopContinuousRunTimeout,
                        nameof(StopContinuousRunAsync)).ConfigureAwait(false);

                    if (!completed)
                    {
                        UpdateContinuousRunState(false);
                        MarkVisionFault("Timed out while stopping VisionPro continuous run.");
                        return;
                    }
                }

                UpdateContinuousRunState(false);
                MarkVisionHealthy();
                SyncStateFromManager();
                ServiceLocator.ApplicationEventLogger.LogVisionProStatus(false, nameof(StopContinuousRunAsync), "Continuous run stopped");
                ServiceLocator.OpcUaClientService.WriteIsRunningAsync(false).SafeFireAndForget(_log, "OPCUA_WRITE_RUNNING_STATE_FAILED");
            }
            finally
            {
                Interlocked.Exchange(ref _continuousRunCommandInProgress, 0);
                _continuousRunCommandSemaphore.Release();
            }
        }

        /// <summary>
        /// Executes the standard recovery recipe:
        /// stop continuous mode while preserving the desired run state,
        /// wait a short cooldown, then start again.
        /// </summary>
        public async Task<bool> RecoverContinuousRunAsync(CancellationToken token, string source, string details = null)
        {
            UpdateContinuousRunRequested(true);
            MarkVisionFault(details ?? "VisionPro health issue detected");
            ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                LogLevel.Warn,
                "VISIONPRO_AUTO_RECOVERY_START",
                "VisionPro",
                "Automatic VisionPro recovery started",
                source,
                details);

            await StopContinuousRunAsync(true);
            await Task.Delay(500, token).ConfigureAwait(false);

            await StartContinuousRunAsync(token).ConfigureAwait(false);
            SyncStateFromManager();

            var recovered = IsContinuousRunActive;
            string recoveryDetails = details;
            if (!recovered && !string.IsNullOrWhiteSpace(CognexManager?.LastContinuousRunFailure))
            {
                recoveryDetails = string.IsNullOrWhiteSpace(details)
                    ? CognexManager.LastContinuousRunFailure
                    : $"{details} Latest start failure: {CognexManager.LastContinuousRunFailure}";
            }

            if (recovered)
            {
                ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                    LogLevel.Info,
                    "VISIONPRO_AUTO_RECOVERY_COMPLETE",
                    "VisionPro",
                    "Automatic VisionPro recovery completed",
                    source,
                    recoveryDetails);
            }
            else
            {
                ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                    LogLevel.Error,
                    "VISIONPRO_AUTO_RECOVERY_FAILED",
                    "VisionPro",
                    "Automatic VisionPro recovery failed",
                    source,
                    recoveryDetails);
            }

            return recovered;
        }

        /// <summary>
        /// Runs a potentially blocking Cognex operation outside the UI thread and
        /// protects the caller with a finite timeout. This is one of the key
        /// safeguards against frozen HMIs during driver/COM stalls.
        /// </summary>
        private async Task<bool> ExecuteManagerOperationWithTimeoutAsync(Action operation, TimeSpan timeout, string operationName)
        {
            if (operation == null)
            {
                return true;
            }

            var task = Task.Run(operation);
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completedTask != task)
            {
                MainWindow.logger?.Error($"{operationName} timed out after {timeout.TotalSeconds:0} seconds.");
                ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                    LogLevel.Error,
                    "VISIONPRO_OPERATION_TIMEOUT",
                    "VisionPro",
                    "VisionPro operation timed out",
                    operationName,
                    $"Timeout after {timeout.TotalSeconds:0} seconds.",
                    null);
                return false;
            }

            await task.ConfigureAwait(false);
            return true;
        }

        private async Task<bool> ReadContinuousRunStateFromManagerAsync(string source)
        {
            var manager = CognexManager;
            if (manager == null)
            {
                UpdateContinuousRunState(false);
                MainWindow.logger?.Warn($"VISIONPRO_STATE_READ_UNAVAILABLE|source={source}|reason=no-cognex-manager");
                return false;
            }

            var readTask = Task.Run(() =>
            {
                try
                {
                    return manager.IsRunningContinuously;
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn($"VISIONPRO_STATE_READ_FAILED|source={source}|error={ex.Message}");
                    return false;
                }
            });

            var completedTask = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            if (completedTask != readTask)
            {
                MainWindow.logger?.Warn($"VISIONPRO_STATE_READ_TIMEOUT|source={source}");
                return IsContinuousRunActive;
            }

            var isRunning = await readTask.ConfigureAwait(false);
            UpdateContinuousRunState(isRunning);
            return isRunning;
        }

        private static string NormalizeHoldReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? "runtime-hold"
                : reason.Trim().ToLowerInvariant();
        }
    }
}
