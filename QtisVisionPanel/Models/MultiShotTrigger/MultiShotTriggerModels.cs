using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Models.MultiShotTrigger
{
    public sealed class MachineMultiShotTriggerConfiguration
    {
        public MultiShotTriggerOptions Side { get; set; } = MultiShotTriggerOptions.CreateDefault(
            "Side",
            "Left",
            "OUT_CAMERA_SIDE_TRIGGER");

        public MultiShotTriggerOptions Right { get; set; } = MultiShotTriggerOptions.CreateDefault(
            "Rear",
            "Right",
            "OUT_CAMERA_REAR_TRIGGER");

        public MultiShotTriggerOptions Rear { get; set; } = MultiShotTriggerOptions.CreateDefault(
            "Rear", "Rear", "OUT_CAMERA_REAR_TRIGGER");

        public MultiShotTriggerOptions Bottom { get; set; } = MultiShotTriggerOptions.CreateDefault(
            "Bottom",
            "Bottom",
            "OUT_CAMERA_BOTTOM_TRIGGER");

        public IEnumerable<KeyValuePair<string, MultiShotTriggerOptions>> EnumerateProfiles()
        {
            yield return new KeyValuePair<string, MultiShotTriggerOptions>("Side", Side);
            yield return new KeyValuePair<string, MultiShotTriggerOptions>("Right", Right);
            yield return new KeyValuePair<string, MultiShotTriggerOptions>("Rear", Rear);
            yield return new KeyValuePair<string, MultiShotTriggerOptions>("Bottom", Bottom);
        }

        public MultiShotTriggerOptions ResolveOptionsForRuntimeRole(string runtimeRole)
        {
            string normalized = NormalizeRuntimeRole(runtimeRole);
            if (normalized == "left" || normalized == "side")
            {
                return Side;
            }

            if (normalized == "bottom")
            {
                return Bottom;
            }

            return EnumerateProfiles()
                .Select(profile => profile.Value)
                .FirstOrDefault(options => options?.Enabled == true && IsCompatibleRuntimeRole(options, normalized))
                ?? EnumerateProfiles().Select(profile => profile.Value)
                    .FirstOrDefault(options => IsCompatibleRuntimeRole(options, normalized));
        }

        public static bool IsCompatibleRuntimeRole(MultiShotTriggerOptions options, string runtimeRole)
        {
            if (options == null)
            {
                return false;
            }

            string normalizedRuntime = NormalizeRuntimeRole(runtimeRole);
            string normalizedCameraRole = NormalizeRuntimeRole(options.CameraRole);
            string normalizedDisplayName = NormalizeRuntimeRole(options.DisplayName);

            return !string.IsNullOrWhiteSpace(normalizedRuntime) &&
                   (string.Equals(normalizedRuntime, normalizedCameraRole, StringComparison.OrdinalIgnoreCase) ||
                    IsSamePhysicalRole(normalizedRuntime, normalizedCameraRole) ||
                    (string.IsNullOrWhiteSpace(normalizedCameraRole) &&
                     (string.Equals(normalizedRuntime, normalizedDisplayName, StringComparison.OrdinalIgnoreCase) ||
                      IsSamePhysicalRole(normalizedRuntime, normalizedDisplayName))));
        }

        private static bool IsSamePhysicalRole(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if ((left == "left" || left == "side") &&
                (right == "left" || right == "side"))
            {
                return true;
            }

            return false;
        }

        private static string NormalizeRuntimeRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return string.Empty;
            }

            string normalized = role.Trim().ToLowerInvariant();
            if (normalized.Contains("left") ||
                normalized.Contains("sideleft") ||
                normalized.Contains("side_left") ||
                normalized.Contains("side-left") ||
                normalized.Contains("sinistra"))
            {
                return "left";
            }

            if (normalized.Contains("right") ||
                normalized.Contains("sideright") ||
                normalized.Contains("side_right") ||
                normalized.Contains("side-right") ||
                normalized.Contains("destra"))
            {
                return "right";
            }

            if (normalized.Contains("rear")) return "rear";

            if (normalized.Contains("bottom"))
            {
                return "bottom";
            }

            if (normalized.Contains("side"))
            {
                return "side";
            }

            return normalized;
        }
    }

    public sealed class MultiShotTriggerOptions
    {
        public bool Enabled { get; set; } = false;

        public string CameraRole { get; set; } = "Side";

        public string DisplayName { get; set; } = "Left";

        public string TriggerOutputName { get; set; } = "OUT_CAMERA_SIDE_TRIGGER";

        public int TriggerPulseMs { get; set; } = 10;

        public int MinimumInterShotIntervalMs { get; set; } = 30;

        public int ShotCount { get; set; } = 1;

        public long InitialOffsetPulses { get; set; } = 0;

        public long StepPulses { get; set; } = 0;

        public int MaxShotCount { get; set; } = 12;

        public int SessionTimeoutMs { get; set; } = 3000;

        public long TargetLateTolerancePulses { get; set; } = 500;

        public bool RequireMachineRunning { get; set; } = true;

        public bool PositiveDirection { get; set; } = true;

        public MultiShotVisionProStitchingOptions VisionProStitching { get; set; } = new MultiShotVisionProStitchingOptions();

        /// <summary>
        /// Machine-level camera cycling configuration used when one physical camera
        /// alternates front and back illumination on consecutive MultiShot frames.
        /// Recipe-specific geometry remains outside this block.
        /// </summary>
        public MultiShotDualIlluminationOptions DualIllumination { get; set; } = new MultiShotDualIlluminationOptions();

        public static MultiShotTriggerOptions CreateDefault(string cameraRole, string displayName, string triggerOutputName)
        {
            return new MultiShotTriggerOptions
            {
                CameraRole = cameraRole,
                DisplayName = displayName,
                TriggerOutputName = triggerOutputName
            };
        }
    }

    public sealed class MultiShotVisionProStitchingOptions
    {
        public string ToolBlockName { get; set; } = "ImageStitching";

        public string ExpectedFramesInputName { get; set; } = "expectedFrames";

        public string StepMmInputName { get; set; } = "stepMm";

        public string MmPerPixelInputName { get; set; } = "mmPerPixel";

        public double StepMm { get; set; } = 0.0;

        public double MmPerPixel { get; set; } = 0.0;
    }

    public sealed class MultiShotDualIlluminationOptions
    {
        public bool Enabled { get; set; } = false;

        public bool FrontFirst { get; set; } = true;

        public double ExposureTimeUs { get; set; } = 500.0;

        public string FrontOutputLine { get; set; } = "Line3";

        public string BackOutputLine { get; set; } = "Line4";

        public string ActiveOutputSource { get; set; } = "ExposureActive";

        public string DualIlluminationEnabledInputName { get; set; } = "dualIlluminationEnabled";

        public string FrontFirstInputName { get; set; } = "frontFirst";
    }

    public sealed class MultiShotTriggerTarget
    {
        public int Index { get; set; }

        public long TargetPulses { get; set; }
    }

    public sealed class MultiShotTriggerPlan
    {
        public string CameraRole { get; set; } = "Side";

        public string DisplayName { get; set; } = "Left";

        public string TriggerOutputName { get; set; }

        public int TriggerPulseMs { get; set; }

        public int MinimumInterShotIntervalMs { get; set; }

        public bool PositiveDirection { get; set; } = true;

        public long TargetLateTolerancePulses { get; set; } = 500;

        public IReadOnlyList<MultiShotTriggerTarget> Targets { get; set; } = Array.Empty<MultiShotTriggerTarget>();
    }

    public sealed class MultiShotTriggerSession
    {
        public string CameraRole { get; set; } = "Side";

        public string DisplayName { get; set; } = "Left";

        public long EncoderReference { get; set; }

        public MultiShotTriggerPlan Plan { get; set; }

        public int NextTargetIndex { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.Now;

        public long LastObservedEncoderCount { get; set; }

        public DateTime? LastPulseStartedAt { get; set; }

        public bool PulseInFlight { get; set; }

        public bool RetryScheduled { get; set; }

        public bool IsComplete
        {
            get { return Plan != null && NextTargetIndex >= Plan.Targets.Count; }
        }
    }

    public sealed class MultiShotOutputBinding
    {
        public string RequestedOutputName { get; set; }

        public string SignalCode { get; set; }

        public string Board { get; set; }

        public string PhysicalChannel { get; set; }

        public int ChannelNumber { get; set; } = -1;

        public bool ActiveElectricalState { get; set; } = true;

        public bool InactiveElectricalState { get; set; } = false;
    }

    public enum MultiShotTriggerEventKind
    {
        ConfigLoaded,
        PlanCreated,
        SessionStarted,
        TriggerPulse,
        TriggerFailed,
        SessionCompleted,
        SessionTimeout,
        Cancelled,
        ConfigError,
        Skipped,
        TargetTooLate
    }

    public sealed class MultiShotTriggerEventArgs : EventArgs
    {
        public MultiShotTriggerEventKind Kind { get; set; }

        public string LogId { get; set; }

        public string ProfileKey { get; set; }

        public string Message { get; set; }

        public string CameraRole { get; set; }

        public string DisplayName { get; set; }

        public string TriggerOutput { get; set; }

        public long? EncoderReference { get; set; }

        public long? CurrentEncoderCount { get; set; }

        public int? ShotIndex { get; set; }

        public int? ShotCount { get; set; }

        public IReadOnlyList<long> TargetPulses { get; set; } = Array.Empty<long>();
    }
}
