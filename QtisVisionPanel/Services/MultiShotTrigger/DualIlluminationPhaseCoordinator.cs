using Cognex.VisionPro.QuickBuild;
using NLog;
using QtisVisionPanel.Cls_Vpro;
using QtisVisionPanel.Models.MultiShotTrigger;
using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Services.MultiShotTrigger
{
    /// <summary>
    /// Owns the runtime phase of DALSA two-preset illumination cycling.
    /// Camera configuration is machine-level; this class only tracks transient
    /// alignment and never changes the encoder-driven trigger sequence.
    /// </summary>
    public sealed class DualIlluminationPhaseCoordinator
    {
        private const string SourceName = nameof(DualIlluminationPhaseCoordinator);
        private readonly object _sync = new object();
        private readonly Dictionary<string, ProfilePhaseState> _states =
            new Dictionary<string, ProfilePhaseState>(StringComparer.OrdinalIgnoreCase);
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ApplicationEventLogger _eventLogger;

        private sealed class ProfilePhaseState
        {
            public CogJob Job { get; set; }
            public bool Configured { get; set; }
            public bool ResetRequired { get; set; }
            public bool SessionActive { get; set; }
            public bool Blocked { get; set; }
        }

        public DualIlluminationPhaseCoordinator(ApplicationEventLogger eventLogger = null)
        {
            _eventLogger = eventLogger ?? ServiceLocator.ApplicationEventLogger;
        }

        public bool ConfigureAfterJobLoad(
            string profileKey,
            CogJob job,
            MultiShotTriggerOptions options,
            string source)
        {
            string key = NormalizeProfileKey(profileKey);
            var dual = options?.DualIllumination;
            if (dual?.Enabled != true)
            {
                lock (_sync)
                {
                    _states.Remove(key);
                }

                return true;
            }

            if (!ValidateProfile(options, out string validationError))
            {
                SetConfigurationFailed(key, job);
                Log(
                    LogLevel.Warn,
                    "CAMERA_CYCLING_PRESET_CONFIG_INVALID",
                    $"Dual illumination configuration is invalid for profile '{key}': {validationError}",
                    key,
                    job,
                    source,
                    null);
                return false;
            }

            lock (_sync)
            {
                if (_states.TryGetValue(key, out var activeState) && activeState.SessionActive)
                {
                    activeState.Job = job;
                    activeState.Configured = false;
                    activeState.ResetRequired = true;
                    Log(
                        LogLevel.Warn,
                        "CAMERA_CYCLING_PRESET_RESET_DEFERRED_ACTIVE_SESSION",
                        $"DALSA Cycling Preset configuration for profile '{key}' was deferred because a MultiShot session is active.",
                        key,
                        job,
                        source,
                        null);
                    return false;
                }
            }

            CameraCyclingPresetResult result = IgigaCameraAccess.ConfigureDualIlluminationCyclingPresets(
                job,
                dual,
                executeResetCommand: false);
            if (!result.Success)
            {
                SetConfigurationFailed(key, job);
                Log(
                    LogLevel.Warn,
                    "CAMERA_CYCLING_PRESET_CONFIG_FAILED",
                    $"DALSA Cycling Presets could not be configured for profile '{key}': {result.ErrorMessage}",
                    key,
                    job,
                    source,
                    result);
                return false;
            }

            lock (_sync)
            {
                _states[key] = new ProfilePhaseState
                {
                    Job = job,
                    Configured = true,
                    // Configuration and phase alignment are deliberately separate:
                    // the reset is armed immediately before the first product session.
                    ResetRequired = true,
                    SessionActive = false,
                    Blocked = false
                };
            }

            Log(
                LogLevel.Info,
                "CAMERA_CYCLING_PRESETS_CONFIGURED",
                $"DALSA Cycling Presets configured for profile '{key}': preset 1/2, exposure={dual.ExposureTimeUs:0.###} us, front={dual.FrontOutputLine}, back={dual.BackOutputLine}, frontFirst={dual.FrontFirst}.",
                key,
                job,
                source,
                result);
            return true;
        }

        public bool PrepareSession(string profileKey, CogJob job, MultiShotTriggerOptions options)
        {
            string key = NormalizeProfileKey(profileKey);
            var dual = options?.DualIllumination;
            if (dual?.Enabled != true)
            {
                return true;
            }

            if (!ValidateProfile(options, out string validationError))
            {
                Log(
                    LogLevel.Warn,
                    "CAMERA_CYCLING_PRESET_CONFIG_INVALID",
                    $"Dual illumination session for profile '{key}' was blocked: {validationError}",
                    key,
                    job,
                    "session preparation",
                    null);
                return false;
            }

            bool requiresConfiguration;
            bool requiresReset;
            lock (_sync)
            {
                if (!_states.TryGetValue(key, out var state))
                {
                    state = new ProfilePhaseState { Job = job, ResetRequired = true };
                    _states[key] = state;
                }

                if (state.SessionActive)
                {
                    Log(
                        LogLevel.Warn,
                        "CAMERA_CYCLING_PRESET_RESET_DEFERRED_ACTIVE_SESSION",
                        $"A new dual-illumination session for profile '{key}' was blocked because the previous session is still active.",
                        key,
                        job,
                        "session preparation",
                        null);
                    return false;
                }

                if (state.Blocked)
                {
                    Log(
                        LogLevel.Warn,
                        "CAMERA_CYCLING_PRESET_SESSION_BLOCKED",
                        $"Dual-illumination session for profile '{key}' was blocked because job/configuration validation failed during load.",
                        key,
                        job,
                        "session preparation",
                        null);
                    return false;
                }

                requiresConfiguration = !state.Configured || !ReferenceEquals(state.Job, job);
                requiresReset = state.ResetRequired;
            }

            if (requiresConfiguration)
            {
                if (!ConfigureAfterJobLoad(key, job, options, "session preparation"))
                {
                    return false;
                }

                requiresReset = true;
            }

            if (requiresReset)
            {
                CameraCyclingPresetResult resetResult = IgigaCameraAccess.ResetDualIlluminationCyclingPreset(job);
                if (!resetResult.Success)
                {
                    lock (_sync)
                    {
                        if (_states.TryGetValue(key, out var failedState))
                        {
                            failedState.ResetRequired = true;
                        }
                    }

                    Log(
                        LogLevel.Warn,
                        "CAMERA_CYCLING_PRESET_RESET_FAILED",
                        $"DALSA Cycling Preset phase could not be reset for profile '{key}': {resetResult.ErrorMessage}",
                        key,
                        job,
                        "session preparation",
                        resetResult);
                    return false;
                }

                lock (_sync)
                {
                    if (_states.TryGetValue(key, out var resetState))
                    {
                        resetState.ResetRequired = false;
                    }
                }

                LogResetArmed(key, job, "session preparation", resetResult);
            }
            else
            {
                uint? activeSet = IgigaCameraAccess.ReadCyclingPresetCurrentActiveSet(job);
                if (activeSet.HasValue)
                {
                    Log(
                        LogLevel.Info,
                        "CAMERA_CYCLING_PRESET_ACTIVE_SET",
                        $"DALSA Cycling Preset active set observed before profile '{key}' session: {activeSet.Value}. The next StartOfFrame determines the frame preset.",
                        key,
                        job,
                        "session preparation",
                        new CameraCyclingPresetResult { Success = true, CurrentActiveSet = activeSet });
                }
            }

            lock (_sync)
            {
                if (_states.TryGetValue(key, out var readyState))
                {
                    readyState.SessionActive = true;
                }
            }

            return true;
        }

        public void HandleTriggerEvent(MultiShotTriggerEventArgs eventArgs)
        {
            if (eventArgs == null || string.IsNullOrWhiteSpace(eventArgs.ProfileKey))
            {
                return;
            }

            string key = NormalizeProfileKey(eventArgs.ProfileKey);
            bool requiresRealignment = eventArgs.Kind == MultiShotTriggerEventKind.Cancelled ||
                                       eventArgs.Kind == MultiShotTriggerEventKind.SessionTimeout ||
                                       eventArgs.Kind == MultiShotTriggerEventKind.TriggerFailed ||
                                       eventArgs.Kind == MultiShotTriggerEventKind.ConfigError;

            lock (_sync)
            {
                if (!_states.TryGetValue(key, out var state))
                {
                    return;
                }

                if (eventArgs.Kind == MultiShotTriggerEventKind.SessionStarted)
                {
                    state.SessionActive = true;
                }
                else if (eventArgs.Kind == MultiShotTriggerEventKind.SessionCompleted || requiresRealignment)
                {
                    state.SessionActive = false;
                }

                if (requiresRealignment)
                {
                    state.ResetRequired = true;
                }
            }

            if (requiresRealignment)
            {
                Log(
                    LogLevel.Warn,
                    "CAMERA_CYCLING_PHASE_REALIGN_REQUIRED",
                    $"DALSA Cycling Preset phase for profile '{key}' will be reset before the next product because of {eventArgs.LogId ?? eventArgs.Kind.ToString()}.",
                    key,
                    null,
                    eventArgs.LogId,
                    null);
            }
        }

        public void MarkAllForRealignment(string reason)
        {
            var affectedProfiles = new List<string>();
            lock (_sync)
            {
                foreach (var entry in _states)
                {
                    entry.Value.ResetRequired = true;
                    affectedProfiles.Add(entry.Key);
                }
            }

            if (affectedProfiles.Count > 0)
            {
                Log(
                    LogLevel.Warn,
                    "CAMERA_CYCLING_PHASE_REALIGN_REQUIRED",
                    $"DALSA Cycling Preset phase marked for realignment on profiles [{string.Join(", ", affectedProfiles)}]: {reason}.",
                    string.Join(",", affectedProfiles),
                    null,
                    reason,
                    null);
            }
        }

        public void BlockProfile(string profileKey, CogJob job, string reason)
        {
            string key = NormalizeProfileKey(profileKey);
            lock (_sync)
            {
                _states[key] = new ProfilePhaseState
                {
                    Job = job,
                    Configured = false,
                    ResetRequired = true,
                    SessionActive = false,
                    Blocked = true
                };
            }

            Log(
                LogLevel.Warn,
                "CAMERA_CYCLING_PRESET_SESSION_BLOCKED",
                $"Dual-illumination profile '{key}' is blocked until the VisionPro job/configuration is reloaded successfully: {reason}.",
                key,
                job,
                reason,
                null);
        }

        private void SetConfigurationFailed(string profileKey, CogJob job)
        {
            lock (_sync)
            {
                _states[profileKey] = new ProfilePhaseState
                {
                    Job = job,
                    Configured = false,
                    ResetRequired = true,
                    SessionActive = false,
                    Blocked = false
                };
            }
        }

        private static bool ValidateProfile(MultiShotTriggerOptions options, out string error)
        {
            error = null;
            if (options == null)
            {
                error = "MultiShot profile is missing.";
                return false;
            }

            if (options.ShotCount < 2 || options.ShotCount % 2 != 0)
            {
                error = "dual illumination requires an even ShotCount of at least 2";
                return false;
            }

            return true;
        }

        private void LogResetArmed(
            string profileKey,
            CogJob job,
            string source,
            CameraCyclingPresetResult result)
        {
            string activeSetText = result?.CurrentActiveSet.HasValue == true
                ? result.CurrentActiveSet.Value.ToString()
                : "unavailable";
            Log(
                LogLevel.Info,
                "CAMERA_CYCLING_PRESET_RESET_ARMED",
                $"DALSA Cycling Preset reset armed for profile '{profileKey}'; currentActiveSet={activeSetText}. With StartOfFrame synchronization the reset is applied on the next frame event.",
                profileKey,
                job,
                source,
                result);
        }

        private void Log(
            LogLevel level,
            string logId,
            string message,
            string profileKey,
            CogJob job,
            string source,
            CameraCyclingPresetResult result)
        {
            var metadata = new Dictionary<string, object>
            {
                { "profile_key", profileKey },
                { "job_name", job?.Name },
                { "camera_name", result?.CameraName },
                { "camera_serial", result?.SerialNumber },
                { "current_active_set", result?.CurrentActiveSet },
                { "reset_command_executed", result?.ResetCommandExecuted },
                { "source", source }
            };

            _logger.Log(level, $"{logId}|{message}");
            _eventLogger?.LogOperationalEvent(level, logId, "VisionPro", message, SourceName, source, metadata);
        }

        private static string NormalizeProfileKey(string profileKey)
        {
            return string.IsNullOrWhiteSpace(profileKey) ? "Side" : profileKey.Trim();
        }
    }
}
