using NLog;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.MultiShotTrigger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services.MultiShotTrigger
{
    public sealed class MultiShotTriggerController : IMultiShotTriggerController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly object _sync = new object();

        // Milliseconds to wait after each IO pulse before reading the VisionPro
        // ImageStitching ToolBlock outputs.  Allows time for camera acquisition
        // + GigE transfer + PostAcquisitionRef to complete before the read.
        private const int FrameAckReadDelayMs = 150;

        private volatile Func<string, ImageStitchingStatus> _stitchingStatusProvider;
        private volatile Func<string, MultiShotTriggerOptions, bool> _sessionPreparationHandler;

        public Func<string, ImageStitchingStatus> StitchingStatusProvider
        {
            get => _stitchingStatusProvider;
            set => _stitchingStatusProvider = value;
        }

        public Func<string, MultiShotTriggerOptions, bool> SessionPreparationHandler
        {
            get => _sessionPreparationHandler;
            set => _sessionPreparationHandler = value;
        }
        private readonly IIODeviceManager _ioManager;
        private readonly IMultiShotTriggerPlanBuilder _planBuilder;
        private readonly Dictionary<int, SemaphoreSlim> _channelLocks = new Dictionary<int, SemaphoreSlim>();
        private readonly Dictionary<string, MultiShotTriggerOptions> _optionsByProfile = new Dictionary<string, MultiShotTriggerOptions>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MultiShotTriggerSession> _sessionsByProfile = new Dictionary<string, MultiShotTriggerSession>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MultiShotOutputBinding> _outputBindingsByProfile = new Dictionary<string, MultiShotOutputBinding>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CancellationTokenSource> _sessionCtsByProfile = new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _timeoutTimer;

        private MachineRuntimeConfiguration _configuration;
        private Func<bool> _isMachineRunningAccessor;
        private int _mainEncoderChannel = -1;

        private sealed class PendingPulse
        {
            public string ProfileKey { get; set; }
            public MultiShotTriggerOptions Options { get; set; }
            public MultiShotTriggerSession Session { get; set; }
            public MultiShotOutputBinding OutputBinding { get; set; }
            public MultiShotTriggerTarget Target { get; set; }
            public long CurrentEncoderCount { get; set; }
            public long PulseStartedEncoderCount { get; set; }
            public CancellationToken Token { get; set; }
        }

        public MultiShotTriggerController(IIODeviceManager ioManager, IMultiShotTriggerPlanBuilder planBuilder = null)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _planBuilder = planBuilder ?? new MultiShotTriggerPlanBuilder();
            _timeoutTimer = new Timer(_ => CheckTimeout(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler<MultiShotTriggerEventArgs> EventRaised;

        public bool IsEnabled
        {
            get
            {
                lock (_sync)
                {
                    return _optionsByProfile.Values.Any(options => options?.Enabled == true);
                }
            }
        }

        public void Configure(MachineRuntimeConfiguration configuration, Func<bool> isMachineRunningAccessor)
        {
            CancelActiveSession("configuration reload");

            _configuration = configuration;
            _isMachineRunningAccessor = isMachineRunningAccessor;
            _mainEncoderChannel = ResolveMainEncoderChannel(configuration);

            var profiles = NormalizeProfiles(configuration?.MachineMultiShotTrigger);
            var rightOptions = profiles["Right"];
            var rearOptions = profiles["Rear"];
            if (rightOptions.Enabled && rearOptions.Enabled &&
                (string.Equals(rightOptions.TriggerOutputName, rearOptions.TriggerOutputName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(CameraConfigurationHelper.NormalizeCameraType(rightOptions.CameraRole),
                     CameraConfigurationHelper.NormalizeCameraType(rearOptions.CameraRole), StringComparison.OrdinalIgnoreCase)))
            {
                rearOptions.Enabled = false;
                Logger.Error("MULTISHOT_PROFILE_COLLISION|Right and Rear share a camera role or output; Rear profile disabled until the mapping is corrected.");
            }
            lock (_sync)
            {
                _optionsByProfile.Clear();
                foreach (var profile in profiles)
                {
                    _optionsByProfile[profile.Key] = profile.Value;
                }
            }

            foreach (var profile in profiles)
            {
                var options = profile.Value;
                LogEvent(
                    MultiShotTriggerEventKind.ConfigLoaded,
                    BuildLogId(profile.Key, "CONFIG_LOADED"),
                    $"{DescribeProfile(options)} MultiShot config loaded: enabled={options.Enabled}, output={options.TriggerOutputName}, shots={options.ShotCount}, step={options.StepPulses}, minInterval={options.MinimumInterShotIntervalMs} ms, lateTolerance={options.TargetLateTolerancePulses}.",
                    profile.Key,
                    null,
                    options,
                    null,
                    null);

                if (options.Enabled)
                {
                    LogResolvedOutputMapping(profile.Key, options);
                }
            }
        }

        public bool OnProductTriggerReceived(long currentEncoderCount)
        {
            List<KeyValuePair<string, MultiShotTriggerOptions>> profiles;
            lock (_sync)
            {
                profiles = _optionsByProfile
                    .Where(profile => profile.Value?.Enabled == true)
                    .ToList();
            }

            if (profiles.Count == 0)
            {
                return false;
            }

            foreach (var profile in profiles)
            {
                string profileKey = profile.Key;
                MultiShotTriggerOptions options = profile.Value;

                // ArmProfile can block on real camera I/O when Dual Illumination is enabled:
                // DualIlluminationPhaseCoordinator.PrepareSession executes synchronous GenICam
                // round-trips (cycling-preset reset) before every product. This method is called
                // inline from the SAME hardware polling thread that also raises OnEncoderChanged
                // for every profile (see DigitalIOViewModel.OnCounterChanged/OnInputChanged): a
                // slow or delayed camera round-trip stalled delivery of ALL subsequent encoder
                // ticks machine-wide, so only the first MultiShot pulse of the session was ever
                // issued (confirmed physically: the PCIe trigger output only rose once). Arming
                // off-thread keeps the shared polling loop responsive regardless of camera
                // response time. Safe under overlapping triggers: DualIlluminationPhaseCoordinator
                // already rejects a second PrepareSession while SessionActive is true, and
                // ArmProfile itself cancels/replaces any stale session under its own lock.
                Task.Run(() =>
                {
                    try
                    {
                        ArmProfile(profileKey, options, currentEncoderCount);
                        ProcessCurrentEncoder(currentEncoderCount);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, BuildLogId(profileKey, "ARM_PROFILE_FAILED"));
                    }
                });
            }

            return true;
        }

        public void OnEncoderChanged(int encoderChannel, long currentEncoderCount)
        {
            if (!IsEnabled || (_mainEncoderChannel >= 0 && encoderChannel != _mainEncoderChannel))
            {
                return;
            }

            ProcessCurrentEncoder(currentEncoderCount);
        }

        public void CancelActiveSession(string reason)
        {
            List<KeyValuePair<string, MultiShotTriggerSession>> sessions;
            List<MultiShotOutputBinding> outputBindings;
            List<CancellationTokenSource> cancellationTokens;

            lock (_sync)
            {
                sessions = _sessionsByProfile.ToList();
                outputBindings = _outputBindingsByProfile.Values.Where(binding => binding != null).ToList();
                cancellationTokens = _sessionCtsByProfile.Values.Where(cts => cts != null).ToList();

                _sessionsByProfile.Clear();
                _outputBindingsByProfile.Clear();
                _sessionCtsByProfile.Clear();
                _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            foreach (var cts in cancellationTokens)
            {
                cts.Cancel();
                cts.Dispose();
            }

            foreach (var outputBinding in outputBindings)
            {
                _ = ForceOutputInactiveAsync(outputBinding);
            }

            foreach (var session in sessions)
            {
                var options = ResolveOptions(session.Key);
                LogEvent(
                    MultiShotTriggerEventKind.Cancelled,
                    BuildLogId(session.Key, "CANCELLED"),
                    $"{DescribeProfile(options, session.Value)} MultiShot cancelled: {reason}.",
                    session.Key,
                    session.Value,
                    options,
                    null,
                    null);
            }
        }

        private void ArmProfile(string profileKey, MultiShotTriggerOptions options, long currentEncoderCount)
        {
            if (options.RequireMachineRunning && !IsMachineRunning())
            {
                LogEvent(
                    MultiShotTriggerEventKind.Skipped,
                    BuildLogId(profileKey, "CANCELLED"),
                    $"{DescribeProfile(options)} MultiShot skipped because the machine is not running.",
                    profileKey,
                    null,
                    options,
                    currentEncoderCount,
                    null);
                return;
            }

            MultiShotTriggerPlan plan;
            MultiShotOutputBinding outputBinding;
            try
            {
                plan = _planBuilder.Build(currentEncoderCount, options);
                outputBinding = ResolveOutputBinding(plan.TriggerOutputName, options);
                if (outputBinding == null || outputBinding.ChannelNumber < 0)
                {
                    throw new InvalidOperationException($"{DescribeProfile(options)} MultiShot trigger output is not mapped to a valid digital output.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, BuildLogId(profileKey, "CONFIG_ERROR"));
                LogEvent(
                    MultiShotTriggerEventKind.ConfigError,
                    BuildLogId(profileKey, "CONFIG_ERROR"),
                    ex.Message,
                    profileKey,
                    null,
                    options,
                    currentEncoderCount,
                    null);
                return;
            }


            var sessionPreparationHandler = _sessionPreparationHandler;
            if (sessionPreparationHandler != null)
            {
                try
                {
                    if (!sessionPreparationHandler(profileKey, options))
                    {
                        LogEvent(
                            MultiShotTriggerEventKind.Skipped,
                            BuildLogId(profileKey, "SESSION_PREPARATION_FAILED"),
                            $"{DescribeProfile(options)} MultiShot skipped because camera phase preparation did not complete.",
                            profileKey,
                            null,
                            options,
                            currentEncoderCount,
                            null);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, BuildLogId(profileKey, "SESSION_PREPARATION_FAILED"));
                    LogEvent(
                        MultiShotTriggerEventKind.ConfigError,
                        BuildLogId(profileKey, "SESSION_PREPARATION_FAILED"),
                        $"{DescribeProfile(options)} MultiShot camera phase preparation failed: {ex.Message}",
                        profileKey,
                        null,
                        options,
                        currentEncoderCount,
                        null);
                    return;
                }
            }

            CancellationTokenSource oldCts = null;
            lock (_sync)
            {
                if (_sessionCtsByProfile.TryGetValue(profileKey, out oldCts))
                {
                    _sessionCtsByProfile.Remove(profileKey);
                }

                var sessionCts = new CancellationTokenSource();
                _sessionCtsByProfile[profileKey] = sessionCts;
                _sessionsByProfile[profileKey] = new MultiShotTriggerSession
                {
                    CameraRole = plan.CameraRole,
                    DisplayName = plan.DisplayName,
                    EncoderReference = currentEncoderCount,
                    Plan = plan,
                    NextTargetIndex = 0,
                    StartedAt = DateTime.Now,
                    LastObservedEncoderCount = currentEncoderCount,
                    LastPulseStartedAt = null,
                    PulseInFlight = false
                };
                _outputBindingsByProfile[profileKey] = outputBinding;
                _timeoutTimer.Change(100, 100);
            }

            oldCts?.Cancel();
            oldCts?.Dispose();

            MultiShotTriggerSession createdSession;
            lock (_sync)
            {
                _sessionsByProfile.TryGetValue(profileKey, out createdSession);
            }

            LogEvent(
                MultiShotTriggerEventKind.PlanCreated,
                BuildLogId(profileKey, "PLAN_CREATED"),
                $"{DescribeProfile(options, createdSession)} MultiShot plan created with {plan.Targets.Count} target(s).",
                profileKey,
                createdSession,
                options,
                currentEncoderCount,
                null);
            LogEvent(
                MultiShotTriggerEventKind.SessionStarted,
                BuildLogId(profileKey, "SESSION_STARTED"),
                $"{DescribeProfile(options, createdSession)} MultiShot session started at encoder {currentEncoderCount}.",
                profileKey,
                createdSession,
                options,
                currentEncoderCount,
                null);
        }

        private void ProcessCurrentEncoder(long currentEncoderCount)
        {
            var pendingPulses = new List<PendingPulse>();
            var sessionsToCancel = new List<KeyValuePair<string, string>>();

            lock (_sync)
            {
                foreach (var entry in _sessionsByProfile.ToList())
                {
                    string profileKey = entry.Key;
                    var session = entry.Value;
                    if (session == null || session.IsComplete)
                    {
                        continue;
                    }

                    if (!_outputBindingsByProfile.TryGetValue(profileKey, out var outputBinding) || outputBinding == null)
                    {
                        continue;
                    }

                    var options = ResolveOptions(profileKey);
                    session.LastObservedEncoderCount = currentEncoderCount;
                    if (session.PulseInFlight)
                    {
                        continue;
                    }

                    var target = session.Plan.Targets[session.NextTargetIndex];
                    if (!HasReachedTarget(session.Plan, currentEncoderCount, target.TargetPulses))
                    {
                        continue;
                    }

                    if (!HasMinimumInterShotIntervalElapsed(session, out var remainingDelayMs))
                    {
                        if (!session.RetryScheduled)
                        {
                            session.RetryScheduled = true;
                            var token = _sessionCtsByProfile.TryGetValue(profileKey, out var retryCts)
                                ? retryCts.Token
                                : CancellationToken.None;
                            ScheduleInterShotRetry(profileKey, session, remainingDelayMs, token);
                        }

                        continue;
                    }

                    if (IsTargetTooLate(session.Plan, currentEncoderCount, target.TargetPulses))
                    {
                        var allowedOvershoot = ResolveTargetLateTolerancePulses(session.Plan);
                        var overshoot = Math.Abs(currentEncoderCount - target.TargetPulses);
                        LogEvent(
                            MultiShotTriggerEventKind.TargetTooLate,
                            BuildLogId(profileKey, "TARGET_TOO_LATE"),
                            $"{DescribeProfile(options, session)} MultiShot target late: shot {target.Index + 1}/{session.Plan.Targets.Count}, current={currentEncoderCount}, target={target.TargetPulses}, overshoot={overshoot}, allowed={allowedOvershoot}. Continuing sequence; check encoder polling/line speed if this warning is frequent.",
                            profileKey,
                            session,
                            options,
                            currentEncoderCount,
                            target.Index + 1);
                    }

                    if (options?.RequireMachineRunning == true && !IsMachineRunning())
                    {
                        sessionsToCancel.Add(new KeyValuePair<string, string>(profileKey, "machine stopped before next shot"));
                        continue;
                    }

                    session.PulseInFlight = true;
                    session.RetryScheduled = false;
                    session.LastPulseStartedAt = DateTime.Now;
                    session.NextTargetIndex++;

                    pendingPulses.Add(new PendingPulse
                    {
                        ProfileKey = profileKey,
                        Options = options,
                        Session = session,
                        OutputBinding = outputBinding,
                        Target = target,
                        CurrentEncoderCount = currentEncoderCount,
                        PulseStartedEncoderCount = currentEncoderCount,
                        Token = _sessionCtsByProfile.TryGetValue(profileKey, out var pulseCts)
                            ? pulseCts.Token
                            : CancellationToken.None
                    });
                }
            }

            foreach (var cancel in sessionsToCancel)
            {
                CancelSession(cancel.Key, cancel.Value);
            }

            foreach (var pulse in pendingPulses)
            {
                var shotIndex = pulse.Target.Index + 1;
                LogEvent(
                    MultiShotTriggerEventKind.TriggerPulse,
                    BuildLogId(pulse.ProfileKey, "TRIGGER_PULSE"),
                    $"{DescribeProfile(pulse.Options, pulse.Session)} MultiShot pulse {shotIndex}/{pulse.Session.Plan.Targets.Count} on {pulse.OutputBinding.SignalCode} at target {pulse.Target.TargetPulses}.",
                    pulse.ProfileKey,
                    pulse.Session,
                    pulse.Options,
                    pulse.CurrentEncoderCount,
                    shotIndex);

                PulseOutputAndContinueAsync(pulse)
                    .ContinueWith(task =>
                    {
                        if (task.Exception != null)
                        {
                            Logger.Error(task.Exception.Flatten(), BuildLogId(pulse.ProfileKey, "TRIGGER_PULSE_FAILED"));
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private void CancelSession(string profileKey, string reason)
        {
            MultiShotTriggerSession session = null;
            MultiShotOutputBinding outputBinding = null;
            CancellationTokenSource cts = null;

            lock (_sync)
            {
                _sessionsByProfile.TryGetValue(profileKey, out session);
                _outputBindingsByProfile.TryGetValue(profileKey, out outputBinding);
                _sessionCtsByProfile.TryGetValue(profileKey, out cts);

                _sessionsByProfile.Remove(profileKey);
                _outputBindingsByProfile.Remove(profileKey);
                _sessionCtsByProfile.Remove(profileKey);

                if (_sessionsByProfile.Count == 0)
                {
                    _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }

            cts?.Cancel();
            cts?.Dispose();

            if (outputBinding != null)
            {
                _ = ForceOutputInactiveAsync(outputBinding);
            }

            if (session != null)
            {
                var options = ResolveOptions(profileKey);
                LogEvent(
                    MultiShotTriggerEventKind.Cancelled,
                    BuildLogId(profileKey, "CANCELLED"),
                    $"{DescribeProfile(options, session)} MultiShot cancelled: {reason}.",
                    profileKey,
                    session,
                    options,
                    null,
                    null);
            }
        }

        private async Task PulseOutputAndContinueAsync(PendingPulse pulse)
        {
            try
            {
                await PulseOutputAsync(pulse.OutputBinding, pulse.Session.Plan.TriggerPulseMs, pulse.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, BuildLogId(pulse.ProfileKey, "TRIGGER_PULSE_FAILED"));
                LogEvent(
                    MultiShotTriggerEventKind.TriggerFailed,
                    BuildLogId(pulse.ProfileKey, "TRIGGER_PULSE_FAILED"),
                    $"{DescribeProfile(pulse.Options, pulse.Session)} MultiShot trigger pulse failed at shot {pulse.Target.Index + 1}/{pulse.Session.Plan.Targets.Count}: {ex.Message}",
                    pulse.ProfileKey,
                    pulse.Session,
                    pulse.Options,
                    pulse.CurrentEncoderCount,
                    pulse.Target.Index + 1);
                CancelSession(pulse.ProfileKey, "trigger pulse failed");
                return;
            }

            // Capture diagnostic context before entering the session lock so the
            // async read below can run without holding any lock or pulse reference.
            var statusProvider = _stitchingStatusProvider;
            int frameAckShotIdx    = pulse.Target.Index + 1;
            int frameAckTotalShots = pulse.Session.Plan.Targets.Count;
            string frameAckProfile = pulse.ProfileKey;
            CancellationToken frameAckToken = pulse.Token;

            bool completed = false;
            bool reevaluate = false;
            long lastObservedEncoder = 0;

            lock (_sync)
            {
                if (!_sessionsByProfile.TryGetValue(pulse.ProfileKey, out var activeSession) ||
                    !ReferenceEquals(activeSession, pulse.Session))
                {
                    return;
                }

                pulse.Session.PulseInFlight = false;
                pulse.Session.RetryScheduled = false;
                lastObservedEncoder = pulse.Session.LastObservedEncoderCount;

                if (pulse.Session.IsComplete)
                {
                    completed = true;
                    _sessionsByProfile.Remove(pulse.ProfileKey);
                    _outputBindingsByProfile.Remove(pulse.ProfileKey);
                    if (_sessionCtsByProfile.TryGetValue(pulse.ProfileKey, out var cts))
                    {
                        cts.Dispose();
                        _sessionCtsByProfile.Remove(pulse.ProfileKey);
                    }

                    if (_sessionsByProfile.Count == 0)
                    {
                        _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                }
                else
                {
                    reevaluate = lastObservedEncoder != pulse.PulseStartedEncoderCount;
                }
            }

            // Diagnostic: read VisionPro stitching status asynchronously so the 150 ms
            // wait does NOT delay PulseInFlight=false or the next encoder evaluation.
            if (statusProvider != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(FrameAckReadDelayMs, frameAckToken).ConfigureAwait(false);
                        var st = statusProvider(frameAckProfile);
                        if (st.IsAvailable)
                        {
                            bool match = st.FrameIndex == frameAckShotIdx;
                            Logger.Info(
                                $"MULTISHOT_FRAME_ACK|{(match ? "OK" : "MISMATCH")}|profile={frameAckProfile}" +
                                $" shot={frameAckShotIdx}/{frameAckTotalShots} vp_frame={st.FrameIndex}" +
                                $" ready={st.IsReady} status=[{st.Status}]" +
                                (st.ErrorMessage.Length > 0 ? $" error=[{st.ErrorMessage}]" : ""));
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Logger.Debug(ex, BuildLogId(frameAckProfile, "FRAME_ACK_READ_FAILED")); }
                });
            }

            if (completed)
            {
                LogEvent(
                    MultiShotTriggerEventKind.SessionCompleted,
                    BuildLogId(pulse.ProfileKey, "SESSION_COMPLETED"),
                    $"{DescribeProfile(pulse.Options, pulse.Session)} MultiShot session completed.",
                    pulse.ProfileKey,
                    pulse.Session,
                    pulse.Options,
                    lastObservedEncoder,
                    null);
                return;
            }

            if (reevaluate)
            {
                ProcessCurrentEncoder(lastObservedEncoder);
            }
        }

        private async Task PulseOutputAsync(MultiShotOutputBinding binding, int pulseMs, CancellationToken token)
        {
            SemaphoreSlim channelLock = GetChannelLock(binding.ChannelNumber);
            await channelLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                // Una volta alzata l'uscita il worker I/O deve completare sempre la
                // discesa, anche se la sessione MultiShot viene annullata nel frattempo.
                await _ioManager.PulseOutputAsync(
                    binding.ChannelNumber,
                    binding.ActiveElectricalState,
                    binding.InactiveElectricalState,
                    Math.Max(1, pulseMs)).ConfigureAwait(false);
            }
            finally
            {
                channelLock.Release();
            }
        }

        private async Task ForceOutputInactiveAsync(MultiShotOutputBinding binding)
        {
            try
            {
                await _ioManager.WriteOutputAsync(binding.ChannelNumber, binding.InactiveElectricalState).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, BuildLogId(binding?.RequestedOutputName, "FORCE_LOW_FAILED"));
            }
        }

        private SemaphoreSlim GetChannelLock(int channel)
        {
            lock (_channelLocks)
            {
                if (!_channelLocks.TryGetValue(channel, out var channelLock))
                {
                    channelLock = new SemaphoreSlim(1, 1);
                    _channelLocks[channel] = channelLock;
                }

                return channelLock;
            }
        }

        private void CheckTimeout()
        {
            List<string> timeoutCandidates;
            lock (_sync)
            {
                var now = DateTime.Now;
                timeoutCandidates = _sessionsByProfile
                    .Where(entry =>
                    {
                        var options = ResolveOptions(entry.Key);
                        var timeoutAnchor = ResolveTimeoutAnchor(entry.Value);
                        return entry.Value != null &&
                               options != null &&
                               options.SessionTimeoutMs > 0 &&
                               (now - timeoutAnchor).TotalMilliseconds > options.SessionTimeoutMs;
                    })
                    .Select(entry => entry.Key)
                    .ToList();
            }

            foreach (var profileKey in timeoutCandidates)
            {
                CancelSessionIfStillTimedOut(profileKey);
            }
        }

        private void CancelSessionIfStillTimedOut(string profileKey)
        {
            MultiShotTriggerSession session;
            MultiShotTriggerOptions options;
            MultiShotOutputBinding outputBinding;
            CancellationTokenSource cts;
            DateTime timeoutAnchor;
            double elapsedMs;

            lock (_sync)
            {
                if (!_sessionsByProfile.TryGetValue(profileKey, out session) || session == null)
                {
                    return;
                }

                options = ResolveOptions(profileKey);
                if (options == null || options.SessionTimeoutMs <= 0)
                {
                    return;
                }

                timeoutAnchor = ResolveTimeoutAnchor(session);
                elapsedMs = (DateTime.Now - timeoutAnchor).TotalMilliseconds;
                if (elapsedMs <= options.SessionTimeoutMs)
                {
                    // A pulse progressed while the timer candidate was being processed.
                    return;
                }

                _outputBindingsByProfile.TryGetValue(profileKey, out outputBinding);
                _sessionCtsByProfile.TryGetValue(profileKey, out cts);
                _sessionsByProfile.Remove(profileKey);
                _outputBindingsByProfile.Remove(profileKey);
                _sessionCtsByProfile.Remove(profileKey);

                if (_sessionsByProfile.Count == 0)
                {
                    _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }

            cts?.Cancel();
            cts?.Dispose();
            if (outputBinding != null)
            {
                _ = ForceOutputInactiveAsync(outputBinding);
            }

            string phase = session.LastPulseStartedAt.HasValue
                ? $"after shot {session.NextTargetIndex}/{session.Plan?.Targets?.Count ?? options.ShotCount}"
                : "before first shot";
            string details =
                $"{DescribeProfile(options, session)} MultiShot timeout {phase}: " +
                $"no session progress for {elapsedMs:0} ms (limit={options.SessionTimeoutMs} ms), " +
                $"lastEncoder={session.LastObservedEncoderCount}.";

            LogEvent(
                MultiShotTriggerEventKind.SessionTimeout,
                BuildLogId(profileKey, "SESSION_TIMEOUT"),
                details,
                profileKey,
                session,
                options,
                session.LastObservedEncoderCount,
                session.NextTargetIndex > 0 ? (int?)session.NextTargetIndex : null);
            LogEvent(
                MultiShotTriggerEventKind.Cancelled,
                BuildLogId(profileKey, "CANCELLED"),
                $"{DescribeProfile(options, session)} MultiShot cancelled: timeout without session progress.",
                profileKey,
                session,
                options,
                session.LastObservedEncoderCount,
                null);
        }

        private static DateTime ResolveTimeoutAnchor(MultiShotTriggerSession session)
        {
            if (session == null)
            {
                return DateTime.Now;
            }

            // Before the first frame, SessionTimeoutMs still protects a missing camera
            // target. After every successful trigger start, it becomes an inactivity
            // timeout so travel time from the photocell cannot consume the whole budget.
            return session.LastPulseStartedAt ?? session.StartedAt;
        }

        private MultiShotOutputBinding ResolveOutputBinding(string outputName, MultiShotTriggerOptions options)
        {
            var output = ResolveOutputSignal(outputName, options);
            if (output == null)
            {
                return null;
            }

            return new MultiShotOutputBinding
            {
                RequestedOutputName = outputName,
                SignalCode = output.SignalCode,
                Board = output.Board,
                PhysicalChannel = output.Channel,
                ChannelNumber = MachineRuntimeIo.ParseChannelNumber(output.Channel),
                ActiveElectricalState = MachineRuntimeIo.ToElectricalOutputState(output, true),
                InactiveElectricalState = MachineRuntimeIo.ToElectricalOutputState(output, false)
            };
        }

        private MachineSignalDefinition ResolveOutputSignal(string outputName, MultiShotTriggerOptions options)
        {
            var outputs = _configuration?.MachineOutputs ?? new List<MachineSignalDefinition>();
            if (!string.IsNullOrWhiteSpace(outputName))
            {
                var binding = _configuration?.AdditionalRuntimeBindings?.FirstOrDefault(item =>
                    string.Equals(item.BindingCode, outputName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Description, outputName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(binding?.SignalCode))
                {
                    outputName = binding.SignalCode;
                }

                var exactMatches = outputs
                    .Where(output => string.Equals(output?.SignalCode, outputName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var validExactMatches = exactMatches
                    .Where(output => MachineRuntimeIo.IsMappedPhysicalOutput(output, MachineSignalCategory.CameraTrigger))
                    .OrderByDescending(output => output.ReservedForRealSignal)
                    .ThenByDescending(output => output.IsRuntimeBound)
                    .ToList();

                if (validExactMatches.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"{DescribeProfile(options)} MultiShot trigger output '{outputName}' has multiple valid physical mappings: {DescribeOutputMappings(validExactMatches)}.");
                }

                if (validExactMatches.Count == 1)
                {
                    if (exactMatches.Count > 1)
                    {
                        Logger.Warn(
                            $"{BuildLogId(ResolveProfileKey(options), "OUTPUT_MAPPING_DUPLICATE_IGNORED")}|" +
                            $"Trigger output '{outputName}' contains {exactMatches.Count} rows; using the only valid CameraTrigger Output mapping {DescribeOutputMappings(validExactMatches)}.");
                    }

                    return validExactMatches[0];
                }

                if (exactMatches.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{DescribeProfile(options)} MultiShot trigger output '{outputName}' exists but is not a valid CameraTrigger Output with a mapped DO channel: {DescribeOutputMappings(exactMatches)}.");
                }
            }

            var tokens = ResolveOutputRoleTokens(options, outputName);
            var roleMatch = outputs.FirstOrDefault(output =>
                MachineRuntimeIo.IsMappedPhysicalOutput(output, MachineSignalCategory.CameraTrigger) &&
                ContainsAny(output.SignalCode, tokens));
            if (roleMatch != null)
            {
                return roleMatch;
            }

            string fallback = ResolveDefaultOutputName(options);
            return MachineRuntimeIo.ResolveMappedPhysicalOutput(
                outputs,
                fallback,
                MachineSignalCategory.CameraTrigger);
        }

        private void LogResolvedOutputMapping(string profileKey, MultiShotTriggerOptions options)
        {
            try
            {
                MultiShotOutputBinding binding = ResolveOutputBinding(options.TriggerOutputName, options);
                if (binding == null || binding.ChannelNumber < 0)
                {
                    throw new InvalidOperationException(
                        $"{DescribeProfile(options)} MultiShot trigger output is not mapped to a valid CameraTrigger Output.");
                }

                LogEvent(
                    MultiShotTriggerEventKind.ConfigLoaded,
                    BuildLogId(profileKey, "OUTPUT_MAPPING_RESOLVED"),
                    $"{DescribeProfile(options)} MultiShot trigger mapping resolved: signal={binding.SignalCode}, board={binding.Board}, channel={binding.PhysicalChannel}, activeState={binding.ActiveElectricalState}.",
                    profileKey,
                    null,
                    options,
                    null,
                    null);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, BuildLogId(profileKey, "OUTPUT_MAPPING_INVALID"));
                LogEvent(
                    MultiShotTriggerEventKind.ConfigError,
                    BuildLogId(profileKey, "OUTPUT_MAPPING_INVALID"),
                    ex.Message,
                    profileKey,
                    null,
                    options,
                    null,
                    null);
            }
        }

        private static string DescribeOutputMappings(IEnumerable<MachineSignalDefinition> signals)
        {
            return string.Join(
                ", ",
                (signals ?? Enumerable.Empty<MachineSignalDefinition>()).Select(signal =>
                    $"{signal?.Direction}/{signal?.Category}/{signal?.Board ?? "-"}/{signal?.Channel ?? "-"}"));
        }

        private string ResolveProfileKey(MultiShotTriggerOptions options)
        {
            lock (_sync)
            {
                return _optionsByProfile
                           .FirstOrDefault(entry => ReferenceEquals(entry.Value, options))
                           .Key ?? "SIDE";
            }
        }

        private static string[] ResolveOutputRoleTokens(MultiShotTriggerOptions options, string outputName)
        {
            var source = string.IsNullOrWhiteSpace(options?.CameraRole)
                ? $"{options?.DisplayName} {outputName}" : options.CameraRole;
            if (ContainsAny(source, "LEFT", "SIDE"))
            {
                return new[] { "LEFT", "SIDE" };
            }

            if (ContainsAny(source, "RIGHT"))
            {
                return new[] { "RIGHT" };
            }
            if (ContainsAny(source, "REAR"))
            {
                return new[] { "REAR" };
            }

            if (ContainsAny(source, "BOTTOM"))
            {
                return new[] { "BOTTOM" };
            }

            return new[] { "SIDE", "LEFT" };
        }

        private static string ResolveDefaultOutputName(MultiShotTriggerOptions options)
        {
            var source = string.IsNullOrWhiteSpace(options?.CameraRole)
                ? $"{options?.DisplayName} {options?.TriggerOutputName}" : options.CameraRole;
            if (ContainsAny(source, "RIGHT"))
            {
                return "OUT_CAMERA_RIGHT_TRIGGER";
            }
            if (ContainsAny(source, "REAR"))
            {
                return "OUT_CAMERA_REAR_TRIGGER";
            }

            if (ContainsAny(source, "BOTTOM"))
            {
                return "OUT_CAMERA_BOTTOM_TRIGGER";
            }

            return "OUT_CAMERA_SIDE_TRIGGER";
        }

        private MultiShotTriggerOptions ResolveOptions(string profileKey)
        {
            lock (_sync)
            {
                return _optionsByProfile.TryGetValue(profileKey ?? string.Empty, out var options)
                    ? options
                    : null;
            }
        }

        private static Dictionary<string, MultiShotTriggerOptions> NormalizeProfiles(MachineMultiShotTriggerConfiguration configuration)
        {
            configuration = configuration ?? new MachineMultiShotTriggerConfiguration();
            return new Dictionary<string, MultiShotTriggerOptions>(StringComparer.OrdinalIgnoreCase)
            {
                { "Side", NormalizeOptions("Side", configuration.Side) },
                { "Right", NormalizeOptions("Right", configuration.Right) },
                { "Rear", NormalizeOptions("Rear", configuration.Rear) },
                { "Bottom", NormalizeOptions("Bottom", configuration.Bottom) }
            };
        }

        private static MultiShotTriggerOptions NormalizeOptions(string profileKey, MultiShotTriggerOptions options)
        {
            var result = options ?? CreateDefaultOptions(profileKey);
            var defaults = CreateDefaultOptions(profileKey);

            if (string.IsNullOrWhiteSpace(result.CameraRole))
            {
                result.CameraRole = defaults.CameraRole;
            }

            if (string.IsNullOrWhiteSpace(result.DisplayName))
            {
                result.DisplayName = defaults.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(result.TriggerOutputName))
            {
                result.TriggerOutputName = defaults.TriggerOutputName;
            }

            if (result.MaxShotCount <= 0)
            {
                result.MaxShotCount = 12;
            }

            if (result.SessionTimeoutMs <= 0)
            {
                result.SessionTimeoutMs = 3000;
            }

            if (result.TriggerPulseMs <= 0)
            {
                result.TriggerPulseMs = 10;
            }

            if (result.MinimumInterShotIntervalMs < 0)
            {
                result.MinimumInterShotIntervalMs = 0;
            }

            if (result.TargetLateTolerancePulses <= 0)
            {
                result.TargetLateTolerancePulses = 500;
            }

            if (result.VisionProStitching == null)
            {
                result.VisionProStitching = new MultiShotVisionProStitchingOptions();
            }

            if (result.DualIllumination == null)
            {
                result.DualIllumination = new MultiShotDualIlluminationOptions();
            }

            return result;
        }

        private static MultiShotTriggerOptions CreateDefaultOptions(string profileKey)
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

        private static int ResolveMainEncoderChannel(MachineRuntimeConfiguration configuration)
        {
            var axisCode = configuration?.RuntimeBindings?.MainEncoderAxisCode;
            var encoder = configuration?.EncoderTemplates?.FirstOrDefault(item =>
                              string.Equals(item.AxisName, axisCode, StringComparison.OrdinalIgnoreCase))
                          ?? configuration?.EncoderTemplates?.FirstOrDefault();
            return MachineRuntimeIo.ParseChannelNumber(encoder?.Channel);
        }

        private static bool HasReachedTarget(MultiShotTriggerPlan plan, long currentEncoderCount, long targetPulses)
        {
            return plan.PositiveDirection
                ? currentEncoderCount >= targetPulses
                : currentEncoderCount <= targetPulses;
        }

        private static bool HasMinimumInterShotIntervalElapsed(MultiShotTriggerSession session, out int remainingDelayMs)
        {
            remainingDelayMs = 0;
            if (session?.LastPulseStartedAt == null)
            {
                return true;
            }

            int minimumIntervalMs = Math.Max(0, session.Plan?.MinimumInterShotIntervalMs ?? 0);
            if (minimumIntervalMs <= 0)
            {
                return true;
            }

            var elapsedMs = (DateTime.Now - session.LastPulseStartedAt.Value).TotalMilliseconds;
            if (elapsedMs >= minimumIntervalMs)
            {
                return true;
            }

            remainingDelayMs = Math.Max(1, (int)Math.Ceiling(minimumIntervalMs - elapsedMs));
            return false;
        }

        private void ScheduleInterShotRetry(string profileKey, MultiShotTriggerSession session, int delayMs, CancellationToken token)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(1, delayMs), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                long encoderCount;
                lock (_sync)
                {
                    if (!_sessionsByProfile.TryGetValue(profileKey, out var activeSession) ||
                        !ReferenceEquals(activeSession, session))
                    {
                        return;
                    }

                    session.RetryScheduled = false;
                    encoderCount = session.LastObservedEncoderCount;
                }

                ProcessCurrentEncoder(encoderCount);
            });
        }

        private static bool IsTargetTooLate(MultiShotTriggerPlan plan, long currentEncoderCount, long targetPulses)
        {
            long allowedOvershoot = ResolveTargetLateTolerancePulses(plan);
            if (allowedOvershoot <= 0)
            {
                return false;
            }

            long overshoot = Math.Abs(currentEncoderCount - targetPulses);
            return overshoot > allowedOvershoot;
        }

        private static long ResolveTargetLateTolerancePulses(MultiShotTriggerPlan plan)
        {
            long step = ResolveTargetStepPulses(plan);
            long configuredTolerance = Math.Max(0, plan?.TargetLateTolerancePulses ?? 0);
            if (step <= 0)
            {
                return configuredTolerance;
            }

            return Math.Max(step, configuredTolerance);
        }

        private static long ResolveTargetStepPulses(MultiShotTriggerPlan plan)
        {
            var targets = plan?.Targets;
            if (targets == null || targets.Count < 2)
            {
                return 0;
            }

            for (int i = 1; i < targets.Count; i++)
            {
                long step = Math.Abs(targets[i].TargetPulses - targets[i - 1].TargetPulses);
                if (step > 0)
                {
                    return step;
                }
            }

            return 0;
        }

        private bool IsMachineRunning()
        {
            try
            {
                return _isMachineRunningAccessor == null || _isMachineRunningAccessor();
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value) || tokens == null)
            {
                return false;
            }

            return tokens.Any(token => !string.IsNullOrWhiteSpace(token) &&
                                       value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string DescribeProfile(MultiShotTriggerOptions options, MultiShotTriggerSession session = null)
        {
            string displayName = session?.DisplayName ?? options?.DisplayName;
            string cameraRole = session?.CameraRole ?? options?.CameraRole;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            return string.IsNullOrWhiteSpace(cameraRole) ? "Camera" : cameraRole;
        }

        private static string BuildLogId(string profileKey, string suffix)
        {
            var normalizedProfile = string.IsNullOrWhiteSpace(profileKey)
                ? "CAMERA"
                : new string(profileKey.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return $"MULTISHOT_{normalizedProfile}_{suffix}";
        }

        private void LogEvent(
            MultiShotTriggerEventKind kind,
            string logId,
            string message,
            string profileKey,
            MultiShotTriggerSession session,
            MultiShotTriggerOptions options,
            long? currentEncoder,
            int? shotIndex)
        {
            var plan = session?.Plan;
            options = options ?? ResolveOptions(profileKey) ?? new MultiShotTriggerOptions();
            var targets = plan?.Targets?.Select(target => target.TargetPulses).ToList() ?? new List<long>();
            var metadata = new Dictionary<string, object>
            {
                { "profile_key", profileKey },
                { "camera_role", plan?.CameraRole ?? options.CameraRole },
                { "display_name", plan?.DisplayName ?? options.DisplayName },
                { "trigger_output", plan?.TriggerOutputName ?? options.TriggerOutputName },
                { "encoder_reference", session?.EncoderReference },
                { "current_encoder", currentEncoder },
                { "shot_index", shotIndex },
                { "shot_count", plan?.Targets?.Count ?? options.ShotCount },
                { "initial_offset_pulses", options.InitialOffsetPulses },
                { "step_pulses", options.StepPulses },
                { "minimum_inter_shot_interval_ms", plan?.MinimumInterShotIntervalMs ?? options.MinimumInterShotIntervalMs },
                { "target_late_tolerance_pulses", plan?.TargetLateTolerancePulses ?? options.TargetLateTolerancePulses },
                { "target_pulses", targets }
            };

            if (kind == MultiShotTriggerEventKind.ConfigError ||
                kind == MultiShotTriggerEventKind.SessionTimeout ||
                kind == MultiShotTriggerEventKind.TriggerFailed ||
                kind == MultiShotTriggerEventKind.TargetTooLate)
            {
                Logger.Warn($"{logId}|{message}");
            }
            else
            {
                Logger.Info($"{logId}|{message}");
            }

            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                kind == MultiShotTriggerEventKind.ConfigError ||
                kind == MultiShotTriggerEventKind.SessionTimeout ||
                kind == MultiShotTriggerEventKind.TriggerFailed ||
                kind == MultiShotTriggerEventKind.TargetTooLate
                    ? LogLevel.Warn
                    : LogLevel.Info,
                logId,
                "Trigger",
                message,
                nameof(MultiShotTriggerController),
                null,
                metadata);

            EventRaised?.Invoke(this, new MultiShotTriggerEventArgs
            {
                Kind = kind,
                LogId = logId,
                ProfileKey = profileKey,
                Message = message,
                CameraRole = plan?.CameraRole ?? options.CameraRole,
                DisplayName = plan?.DisplayName ?? options.DisplayName,
                TriggerOutput = plan?.TriggerOutputName ?? options.TriggerOutputName,
                EncoderReference = session?.EncoderReference,
                CurrentEncoderCount = currentEncoder,
                ShotIndex = shotIndex,
                ShotCount = plan?.Targets?.Count ?? options.ShotCount,
                TargetPulses = targets
            });
        }

        public void Dispose()
        {
            CancelActiveSession("controller dispose");
            _timeoutTimer.Dispose();
            foreach (var channelLock in _channelLocks.Values)
            {
                channelLock.Dispose();
            }
        }
    }
}
