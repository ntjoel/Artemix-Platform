using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.MultiShotTrigger;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Result of merging immutable machine geometry with product-specific recipe deltas.
    /// The returned configuration is an in-memory runtime copy and is never persisted as
    /// machine configuration.
    /// </summary>
    public sealed class RecipeMachineRuntimeResolution
    {
        public MachineRuntimeConfiguration Configuration { get; internal set; }

        public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();

        public IReadOnlyList<string> Warnings { get; internal set; } = Array.Empty<string>();

        public IReadOnlyList<string> AppliedAdjustments { get; internal set; } = Array.Empty<string>();

        public bool IsValid => Errors.Count == 0;

        public bool HasRecipeAdjustments => AppliedAdjustments.Count > 0;
    }

    /// <summary>
    /// Builds the effective runtime configuration used by tracking, camera triggering and
    /// VisionPro stitching. Physical I/O, encoder calibration and safety limits always come
    /// from the machine configuration; the recipe contributes only product geometry deltas.
    /// </summary>
    public static class RecipeMachineRuntimeResolver
    {
        public const string MultiShotModeMachine = "Machine";
        public const string MultiShotModeEnabled = "Enabled";
        public const string MultiShotModeDisabled = "Disabled";

        private const double MaximumAbsolutePositionOffsetMm = 5000.0;
        private const double MaximumAbsoluteStepOffsetMm = 5000.0;
        private const double MaximumAbsoluteFirstShotOffsetMm = 5000.0;

        public static RecipeMachineRuntimeResolution Resolve(
            MachineRuntimeConfiguration machineConfiguration,
            RecipeParameters.RecipeData recipe)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var applied = new List<string>();

            if (machineConfiguration == null)
            {
                errors.Add("Machine runtime configuration is not available.");
                return new RecipeMachineRuntimeResolution
                {
                    Configuration = null,
                    Errors = errors.AsReadOnly(),
                    Warnings = warnings.AsReadOnly(),
                    AppliedAdjustments = applied.AsReadOnly()
                };
            }

            MachineRuntimeConfiguration effective = CloneRuntimeConfiguration(machineConfiguration);
            RecipeParameters.RecipeMachineRuntimeAdjustments adjustments = recipe?.machineRuntimeAdjustments;
            if (adjustments == null)
            {
                return BuildResult(effective, errors, warnings, applied);
            }

            RecipeParameters.RecipeCameraPositionAdjustments positions =
                adjustments.CameraPositions ?? new RecipeParameters.RecipeCameraPositionAdjustments();
            var adjustedCameraPoints = new HashSet<MachineInterventionPoint>();

            ApplyCameraPositionOffset(effective.InterventionPoints, "Top", null, positions.TopOffsetMm, adjustedCameraPoints, errors, applied);
            bool leftPositionApplied = ApplyCameraPositionOffset(effective.InterventionPoints, "Left", "Side", ResolvePhysicalCameraOffset(positions.LeftOffsetMm, positions.SideOffsetMm), adjustedCameraPoints, errors, applied);
            ApplyCameraPositionOffset(effective.InterventionPoints, "Front", null, positions.FrontOffsetMm, adjustedCameraPoints, errors, applied);
            bool hasPhysicalRightPoint = effective.InterventionPoints.Any(point => IsCameraPointForRole(point, "Right"));
            bool rightPositionApplied = ApplyCameraPositionOffset(effective.InterventionPoints, "Right",
                hasPhysicalRightPoint ? null : "Rear",
                hasPhysicalRightPoint ? positions.RightOffsetMm : ResolvePhysicalCameraOffset(positions.RightOffsetMm, positions.RearOffsetMm),
                adjustedCameraPoints, errors, applied);
            bool rearPositionApplied = false;
            if (hasPhysicalRightPoint)
            {
                rearPositionApplied = ApplyCameraPositionOffset(effective.InterventionPoints, "Rear", null, positions.RearOffsetMm, adjustedCameraPoints, errors, applied);
            }
            bool bottomPositionApplied = ApplyCameraPositionOffset(effective.InterventionPoints, "Bottom", null, positions.BottomOffsetMm, adjustedCameraPoints, errors, applied);

            RecipeParameters.RecipeCameraTriggerAdjustments cameraTriggers =
                adjustments.CameraTriggers ?? new RecipeParameters.RecipeCameraTriggerAdjustments();
            var adjustedTriggerPoints = new Dictionary<MachineInterventionPoint, string>();
            ApplyCameraTriggerMode(effective.InterventionPoints, "Top", null, cameraTriggers.Top, adjustedTriggerPoints, errors, applied);
            ApplyCameraTriggerMode(effective.InterventionPoints, "Left", "Side", ResolvePhysicalCameraMode(cameraTriggers.Left, cameraTriggers.Side), adjustedTriggerPoints, errors, applied);
            ApplyCameraTriggerMode(effective.InterventionPoints, "Front", null, cameraTriggers.Front, adjustedTriggerPoints, errors, applied);
            ApplyCameraTriggerMode(effective.InterventionPoints, "Right", hasPhysicalRightPoint ? null : "Rear",
                hasPhysicalRightPoint ? cameraTriggers.Right : ResolvePhysicalCameraMode(cameraTriggers.Right, cameraTriggers.Rear),
                adjustedTriggerPoints, errors, applied);
            if (hasPhysicalRightPoint)
            {
                ApplyCameraTriggerMode(effective.InterventionPoints, "Rear", null, cameraTriggers.Rear, adjustedTriggerPoints, errors, applied);
            }
            ApplyCameraTriggerMode(effective.InterventionPoints, "Bottom", null, cameraTriggers.Bottom, adjustedTriggerPoints, errors, applied);

            RecipeParameters.RecipeMultiShotAdjustments multiShot =
                adjustments.MultiShot ?? new RecipeParameters.RecipeMultiShotAdjustments();

            EncoderConfigurationTemplate mainEncoder = ResolveMainEncoder(effective);
            double countsPerMillimeter = MachineRuntimeIo.CalculateCountsPerMillimeter(mainEncoder);

            ApplyMultiShotAdjustment(
                "SideLeft",
                effective.MachineMultiShotTrigger.Side,
                multiShot.SideLeft,
                effective.InterventionPoints,
                countsPerMillimeter,
                leftPositionApplied,
                errors,
                warnings,
                applied);

            ApplyMultiShotAdjustment(
                "RightRear",
                effective.MachineMultiShotTrigger.Right,
                multiShot.RightRear,
                effective.InterventionPoints,
                countsPerMillimeter,
                string.Equals(effective.MachineMultiShotTrigger.Right?.CameraRole, "Rear", StringComparison.OrdinalIgnoreCase)
                    ? (hasPhysicalRightPoint ? rearPositionApplied : rightPositionApplied)
                    : rightPositionApplied,
                errors,
                warnings,
                applied);

            ApplyMultiShotAdjustment(
                "Rear",
                effective.MachineMultiShotTrigger.Rear,
                multiShot.Rear,
                effective.InterventionPoints,
                countsPerMillimeter,
                rearPositionApplied,
                errors,
                warnings,
                applied);

            ApplyMultiShotAdjustment(
                "Bottom",
                effective.MachineMultiShotTrigger.Bottom,
                multiShot.Bottom,
                effective.InterventionPoints,
                countsPerMillimeter,
                bottomPositionApplied,
                errors,
                warnings,
                applied);

            DisableMultiShotWithoutCameraPoint("SideLeft", effective.MachineMultiShotTrigger.Side, effective.InterventionPoints, "Left", "Side", warnings, applied);
            DisableMultiShotWithoutCameraPoint("RightRear", effective.MachineMultiShotTrigger.Right, effective.InterventionPoints,
                string.Equals(effective.MachineMultiShotTrigger.Right?.CameraRole, "Right", StringComparison.OrdinalIgnoreCase) ? "Right" : "Rear",
                null, warnings, applied);
            DisableMultiShotWithoutCameraPoint("Rear", effective.MachineMultiShotTrigger.Rear, effective.InterventionPoints, "Rear", null, warnings, applied);
            DisableMultiShotWithoutCameraPoint("Bottom", effective.MachineMultiShotTrigger.Bottom, effective.InterventionPoints, "Bottom", null, warnings, applied);

            return BuildResult(effective, errors, warnings, applied);
        }

        /// <summary>
        /// Resolves the enabled machine intervention point used by a recipe camera role.
        /// Compatible physical aliases are used only when the requested point is absent.
        /// </summary>
        public static MachineInterventionPoint ResolveCameraInterventionPoint(
            MachineRuntimeConfiguration configuration,
            string role)
        {
            if (configuration?.InterventionPoints == null || string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            string normalizedRole = role.Trim()
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty);
            string primaryRole = role;
            string fallbackRole = null;

            if (string.Equals(normalizedRole, "SideLeft", StringComparison.OrdinalIgnoreCase))
            {
                primaryRole = "Left";
                fallbackRole = "Side";
            }
            else if (string.Equals(normalizedRole, "RightRear", StringComparison.OrdinalIgnoreCase))
            {
                primaryRole = "Right";
                fallbackRole = "Rear";
            }
            else if (string.Equals(normalizedRole, "Left", StringComparison.OrdinalIgnoreCase))
            {
                fallbackRole = "Side";
            }
            else if (string.Equals(normalizedRole, "Side", StringComparison.OrdinalIgnoreCase))
            {
                fallbackRole = "Left";
            }
            else if (string.Equals(normalizedRole, "Top3D", StringComparison.OrdinalIgnoreCase))
            {
                primaryRole = "Top";
                fallbackRole = "Top3D";
            }

            MachineInterventionPoint point = configuration.InterventionPoints.FirstOrDefault(item =>
                item.Enabled && IsCameraPointForRole(item, primaryRole));
            if (point == null && !string.IsNullOrWhiteSpace(fallbackRole))
            {
                point = configuration.InterventionPoints.FirstOrDefault(item =>
                    item.Enabled && IsCameraPointForRole(item, fallbackRole));
            }

            return point;
        }

        private static double ResolvePhysicalCameraOffset(double physicalOffsetMm, double legacyOffsetMm)
        {
            return Math.Abs(physicalOffsetMm) > 0.0000001 ? physicalOffsetMm : legacyOffsetMm;
        }

        private static string ResolvePhysicalCameraMode(string physicalMode, string legacyMode)
        {
            return string.IsNullOrWhiteSpace(physicalMode) ||
                   string.Equals(physicalMode.Trim(), MultiShotModeMachine, StringComparison.OrdinalIgnoreCase)
                ? legacyMode
                : physicalMode;
        }

        private static RecipeMachineRuntimeResolution BuildResult(
            MachineRuntimeConfiguration configuration,
            List<string> errors,
            List<string> warnings,
            List<string> applied)
        {
            return new RecipeMachineRuntimeResolution
            {
                Configuration = configuration,
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                AppliedAdjustments = applied.AsReadOnly()
            };
        }

        private static bool ApplyCameraPositionOffset(
            IEnumerable<MachineInterventionPoint> points,
            string role,
            string compatibleFallbackRole,
            double offsetMm,
            ISet<MachineInterventionPoint> adjustedPoints,
            List<string> errors,
            List<string> applied)
        {
            if (Math.Abs(offsetMm) < 0.0000001)
            {
                return false;
            }

            if (!IsFinite(offsetMm) || Math.Abs(offsetMm) > MaximumAbsolutePositionOffsetMm)
            {
                errors.Add($"{role} camera position offset must be between -{MaximumAbsolutePositionOffsetMm:0} and +{MaximumAbsolutePositionOffsetMm:0} mm.");
                return false;
            }

            var matchingPoints = (points ?? Enumerable.Empty<MachineInterventionPoint>())
                .Where(point => point.Enabled && IsCameraPointForRole(point, role))
                .ToList();

            string resolvedRole = role;
            if (matchingPoints.Count == 0 && !string.IsNullOrWhiteSpace(compatibleFallbackRole))
            {
                matchingPoints = (points ?? Enumerable.Empty<MachineInterventionPoint>())
                    .Where(point => point.Enabled && IsCameraPointForRole(point, compatibleFallbackRole))
                    .ToList();
                resolvedRole = compatibleFallbackRole;
            }

            if (matchingPoints.Count == 0)
            {
                errors.Add($"{role} camera position offset is configured, but no matching enabled machine intervention point exists.");
                return false;
            }

            if (matchingPoints.Any(point => adjustedPoints?.Contains(point) == true))
            {
                errors.Add($"{role} camera position offset resolves to a machine point already corrected by another recipe role. Keep only one compatible Side/Left or Right/Rear offset.");
                return false;
            }

            bool changed = false;
            foreach (MachineInterventionPoint point in matchingPoints)
            {
                double effectiveOffset = point.EffectiveOffsetMm + offsetMm;
                if (effectiveOffset < 0.0)
                {
                    errors.Add($"{role} camera effective position would be negative ({effectiveOffset:0.###} mm). The machine value was retained.");
                    continue;
                }

                point.TrimOffsetMm += offsetMm;
                adjustedPoints?.Add(point);
                changed = true;
                applied.Add($"camera={role}|resolved_role={resolvedRole}|point={point.PointCode}|recipe_offset_mm={offsetMm:0.###}|effective_mm={point.EffectiveOffsetMm:0.###}");
            }

            return changed;
        }

        private static void ApplyCameraTriggerMode(
            IEnumerable<MachineInterventionPoint> points,
            string role,
            string compatibleFallbackRole,
            string requestedMode,
            IDictionary<MachineInterventionPoint, string> adjustedPoints,
            List<string> errors,
            List<string> applied)
        {
            string mode = NormalizeCameraTriggerMode(requestedMode, errors, role);
            if (string.Equals(mode, MultiShotModeMachine, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // The current inspection orchestrator is anchored to the TOP/TOP 3D result.
            // Keep the physical machine setting when an old or manually edited recipe
            // requests TOP disabled; accepting it would leave companion queues without a
            // primary result and eventually trigger a false VisionPro stall recovery.
            if (string.Equals(role, "Top", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(mode, MultiShotModeDisabled, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Top/Top3D is the primary inspection role and cannot be disabled for a recipe. Machine mode was used.");
                return;
            }

            var matchingPoints = (points ?? Enumerable.Empty<MachineInterventionPoint>())
                .Where(point => IsCameraPointForRole(point, role))
                .ToList();
            string resolvedRole = role;
            if (matchingPoints.Count == 0 && !string.IsNullOrWhiteSpace(compatibleFallbackRole))
            {
                matchingPoints = (points ?? Enumerable.Empty<MachineInterventionPoint>())
                    .Where(point => IsCameraPointForRole(point, compatibleFallbackRole))
                    .ToList();
                resolvedRole = compatibleFallbackRole;
            }

            if (matchingPoints.Count == 0)
            {
                errors.Add($"{role} camera trigger mode is configured, but no matching machine intervention point exists.");
                return;
            }

            bool enabled = string.Equals(mode, MultiShotModeEnabled, StringComparison.OrdinalIgnoreCase);
            foreach (var point in matchingPoints)
            {
                if (adjustedPoints != null && adjustedPoints.TryGetValue(point, out string previousMode))
                {
                    if (!string.Equals(previousMode, mode, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"{role} camera trigger resolves to point '{point.PointCode}', already configured with mode {previousMode} through a compatible role. Keep Side/Left or Right/Rear aliases consistent.");
                    }
                    continue;
                }

                point.Enabled = enabled;
                if (adjustedPoints != null)
                {
                    adjustedPoints[point] = mode;
                }
                applied.Add($"camera_trigger={role}|resolved_role={resolvedRole}|point={point.PointCode}|mode={mode}|enabled={enabled}");
            }
        }

        private static string NormalizeCameraTriggerMode(string mode, List<string> errors, string role)
        {
            if (string.IsNullOrWhiteSpace(mode) ||
                string.Equals(mode, MultiShotModeMachine, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "Inherit", StringComparison.OrdinalIgnoreCase))
            {
                return MultiShotModeMachine;
            }

            if (string.Equals(mode, MultiShotModeEnabled, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, MultiShotModeDisabled, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }

            errors.Add($"{role} camera trigger mode '{mode}' is invalid. Machine mode was used.");
            return MultiShotModeMachine;
        }

        private static void DisableMultiShotWithoutCameraPoint(
            string profileKey,
            MultiShotTriggerOptions options,
            IEnumerable<MachineInterventionPoint> points,
            string primaryRole,
            string fallbackRole,
            List<string> warnings,
            List<string> applied)
        {
            if (options?.Enabled != true)
            {
                return;
            }

            bool hasEnabledPoint = (points ?? Enumerable.Empty<MachineInterventionPoint>())
                .Any(point => point.Enabled &&
                    (IsCameraPointForRole(point, primaryRole) ||
                     (!string.IsNullOrWhiteSpace(fallbackRole) && IsCameraPointForRole(point, fallbackRole))));
            if (hasEnabledPoint)
            {
                return;
            }

            options.Enabled = false;
            warnings.Add($"{profileKey} MultiShot was disabled at runtime because its camera trigger is disabled by the recipe.");
            applied.Add($"multishot={profileKey}|enabled=false|reason=recipe_camera_trigger_disabled");
        }

        private static void ApplyMultiShotAdjustment(
            string profileKey,
            MultiShotTriggerOptions options,
            RecipeParameters.RecipeMultiShotAdjustment adjustment,
            IReadOnlyList<MachineInterventionPoint> interventionPoints,
            double countsPerMillimeter,
            bool relatedCameraPositionChanged,
            List<string> errors,
            List<string> warnings,
            List<string> applied)
        {
            if (options == null)
            {
                errors.Add($"Machine MultiShot profile {profileKey} is missing.");
                return;
            }

            adjustment = adjustment ?? new RecipeParameters.RecipeMultiShotAdjustment();
            bool hasModeOverride = !string.IsNullOrWhiteSpace(adjustment.EnabledMode) &&
                                   !string.Equals(adjustment.EnabledMode, MultiShotModeMachine, StringComparison.OrdinalIgnoreCase) &&
                                   !string.Equals(adjustment.EnabledMode, "Inherit", StringComparison.OrdinalIgnoreCase);
            bool hasMultiShotOverride = hasModeOverride ||
                                        adjustment.ShotCountOffset != 0 ||
                                        Math.Abs(adjustment.StepOffsetMm) > 0.0000001 ||
                                        Math.Abs(adjustment.FirstShotOffsetMm) > 0.0000001;
            if (!hasMultiShotOverride && !relatedCameraPositionChanged)
            {
                return;
            }

            string mode = NormalizeMultiShotMode(adjustment.EnabledMode, errors, profileKey);
            bool machineEnabled = options.Enabled;
            options.Enabled = string.Equals(mode, MultiShotModeEnabled, StringComparison.OrdinalIgnoreCase)
                ? true
                : string.Equals(mode, MultiShotModeDisabled, StringComparison.OrdinalIgnoreCase)
                    ? false
                    : machineEnabled;

            int maxShotCount = options.MaxShotCount > 0 ? options.MaxShotCount : 1;
            long requestedShotCount = (long)options.ShotCount + adjustment.ShotCountOffset;
            if (adjustment.ShotCountOffset != 0 &&
                (requestedShotCount < 1 || requestedShotCount > maxShotCount))
            {
                errors.Add($"{profileKey} MultiShot effective shot count {requestedShotCount} is outside 1..{maxShotCount}. It was clamped for runtime safety.");
            }

            if (adjustment.ShotCountOffset != 0)
            {
                options.ShotCount = (int)Math.Max(1, Math.Min(maxShotCount, requestedShotCount));
            }

            MultiShotDualIlluminationOptions dualIllumination =
                options.DualIllumination ?? new MultiShotDualIlluminationOptions();
            options.DualIllumination = dualIllumination;
            if (options.Enabled && dualIllumination.Enabled &&
                (options.ShotCount < 2 || options.ShotCount % 2 != 0))
            {
                errors.Add(
                    $"{profileKey} dual-illumination MultiShot requires an even effective shot count of at least 2; current value is {options.ShotCount}. " +
                    "Adjust the recipe shot-count delta without changing the machine preset sequence.");
            }

            if (!IsFinite(adjustment.StepOffsetMm) ||
                Math.Abs(adjustment.StepOffsetMm) > MaximumAbsoluteStepOffsetMm)
            {
                errors.Add($"{profileKey} MultiShot step offset must be between -{MaximumAbsoluteStepOffsetMm:0} and +{MaximumAbsoluteStepOffsetMm:0} mm.");
                adjustment = CloneAdjustmentWithoutStep(adjustment);
            }

            if (!IsFinite(adjustment.FirstShotOffsetMm) ||
                Math.Abs(adjustment.FirstShotOffsetMm) > MaximumAbsoluteFirstShotOffsetMm)
            {
                errors.Add($"{profileKey} MultiShot first-shot offset must be between -{MaximumAbsoluteFirstShotOffsetMm:0} and +{MaximumAbsoluteFirstShotOffsetMm:0} mm.");
                adjustment = CloneAdjustmentWithoutFirstShot(adjustment);
            }

            MultiShotVisionProStitchingOptions stitching = options.VisionProStitching ?? new MultiShotVisionProStitchingOptions();
            options.VisionProStitching = stitching;
            double machineStepMm = stitching.StepMm;
            double effectiveStepMm = machineStepMm + adjustment.StepOffsetMm;

            bool requiresStepValidation = Math.Abs(adjustment.StepOffsetMm) > 0.0000001 ||
                                          adjustment.ShotCountOffset != 0 ||
                                          string.Equals(mode, MultiShotModeEnabled, StringComparison.OrdinalIgnoreCase);
            if (requiresStepValidation && options.ShotCount > 1 && effectiveStepMm <= 0.0)
            {
                errors.Add($"{profileKey} MultiShot effective step must be greater than zero when more than one shot is requested. The machine step was retained.");
                effectiveStepMm = machineStepMm;
                if (effectiveStepMm <= 0.0)
                {
                    options.Enabled = false;
                    warnings.Add($"{profileKey} MultiShot was disabled at runtime because no valid machine stitching step is available.");
                }
            }

            stitching.StepMm = effectiveStepMm;
            if (Math.Abs(adjustment.StepOffsetMm) > 0.0000001 && effectiveStepMm > 0.0 && countsPerMillimeter > 0.0)
            {
                options.StepPulses = ConvertMillimetersToPulses(effectiveStepMm, countsPerMillimeter, options.ShotCount > 1);
            }
            else if (Math.Abs(adjustment.StepOffsetMm) > 0.0000001 && countsPerMillimeter <= 0.0)
            {
                errors.Add($"{profileKey} MultiShot step offset cannot be converted because the main encoder calibration is invalid.");
            }

            MachineInterventionPoint cameraPoint = ResolveMultiShotCameraPoint(profileKey, interventionPoints, options.CameraRole);
            bool firstShotGeometryChanged = relatedCameraPositionChanged || Math.Abs(adjustment.FirstShotOffsetMm) > 0.0000001;
            if (firstShotGeometryChanged && cameraPoint != null && countsPerMillimeter > 0.0)
            {
                double effectiveFirstShotMm = cameraPoint.EffectiveOffsetMm + adjustment.FirstShotOffsetMm;
                if (effectiveFirstShotMm < 0.0)
                {
                    errors.Add($"{profileKey} MultiShot first-shot position would be negative ({effectiveFirstShotMm:0.###} mm). The camera intervention position was retained.");
                    effectiveFirstShotMm = cameraPoint.EffectiveOffsetMm;
                }

                options.InitialOffsetPulses = ConvertMillimetersToPulses(effectiveFirstShotMm, countsPerMillimeter, false);
            }
            else if (firstShotGeometryChanged && Math.Abs(adjustment.FirstShotOffsetMm) > 0.0000001)
            {
                if (countsPerMillimeter <= 0.0)
                {
                    errors.Add($"{profileKey} MultiShot first-shot offset cannot be converted because the main encoder calibration is invalid.");
                }
                else
                {
                    long deltaPulses = ConvertSignedMillimetersToPulses(adjustment.FirstShotOffsetMm, countsPerMillimeter);
                    long requestedInitialOffset = options.InitialOffsetPulses + deltaPulses;
                    if (requestedInitialOffset < 0)
                    {
                        errors.Add($"{profileKey} MultiShot first-shot pulse target would be negative. The machine value was retained.");
                    }
                    else
                    {
                        options.InitialOffsetPulses = requestedInitialOffset;
                        warnings.Add($"{profileKey} MultiShot first-shot delta was applied to the saved pulse offset because no camera intervention point was found.");
                    }
                }
            }

            if (hasMultiShotOverride || relatedCameraPositionChanged)
            {
                applied.Add(
                    $"multishot={profileKey}|mode={mode}|enabled={options.Enabled}|shots={options.ShotCount}|step_mm={stitching.StepMm:0.###}|first_shot_pulses={options.InitialOffsetPulses}");
            }
        }

        private static string NormalizeMultiShotMode(string mode, List<string> errors, string profileKey)
        {
            if (string.IsNullOrWhiteSpace(mode) ||
                string.Equals(mode, MultiShotModeMachine, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "Inherit", StringComparison.OrdinalIgnoreCase))
            {
                return MultiShotModeMachine;
            }

            if (string.Equals(mode, MultiShotModeEnabled, StringComparison.OrdinalIgnoreCase))
            {
                return MultiShotModeEnabled;
            }

            if (string.Equals(mode, MultiShotModeDisabled, StringComparison.OrdinalIgnoreCase))
            {
                return MultiShotModeDisabled;
            }

            errors.Add($"{profileKey} MultiShot mode '{mode}' is invalid. Machine mode was used.");
            return MultiShotModeMachine;
        }

        private static RecipeParameters.RecipeMultiShotAdjustment CloneAdjustmentWithoutStep(
            RecipeParameters.RecipeMultiShotAdjustment source)
        {
            return new RecipeParameters.RecipeMultiShotAdjustment
            {
                EnabledMode = source.EnabledMode,
                ShotCountOffset = source.ShotCountOffset,
                StepOffsetMm = 0.0,
                FirstShotOffsetMm = source.FirstShotOffsetMm
            };
        }

        private static RecipeParameters.RecipeMultiShotAdjustment CloneAdjustmentWithoutFirstShot(
            RecipeParameters.RecipeMultiShotAdjustment source)
        {
            return new RecipeParameters.RecipeMultiShotAdjustment
            {
                EnabledMode = source.EnabledMode,
                ShotCountOffset = source.ShotCountOffset,
                StepOffsetMm = source.StepOffsetMm,
                FirstShotOffsetMm = 0.0
            };
        }

        private static MachineInterventionPoint ResolveMultiShotCameraPoint(
            string profileKey,
            IEnumerable<MachineInterventionPoint> points,
            string configuredRole)
        {
            var enabledPoints = (points ?? Enumerable.Empty<MachineInterventionPoint>())
                .Where(point => point.Enabled)
                .ToList();

            if (string.Equals(profileKey, "RightRear", StringComparison.OrdinalIgnoreCase))
            {
                return enabledPoints.FirstOrDefault(point => IsCameraPointForRole(point,
                    string.Equals(configuredRole, "Right", StringComparison.OrdinalIgnoreCase) ? "Right" : "Rear"));
            }
            if (string.Equals(profileKey, "Rear", StringComparison.OrdinalIgnoreCase))
            {
                return enabledPoints.FirstOrDefault(point => IsCameraPointForRole(point, "Rear"));
            }

            if (string.Equals(profileKey, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                return enabledPoints.FirstOrDefault(point => IsCameraPointForRole(point, "Bottom"));
            }

            return enabledPoints.FirstOrDefault(point => IsCameraPointForRole(point, "Left"))
                ?? enabledPoints.FirstOrDefault(point => IsCameraPointForRole(point, "Side"));
        }

        private static bool IsCameraPointForRole(MachineInterventionPoint point, string role)
        {
            if (point == null || string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            return ContainsCameraRole(point.PointCode, role) ||
                   ContainsCameraRole(point.SignalCode, role);
        }

        private static bool ContainsCameraRole(string value, string role)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            string normalized = value.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
            string token = role.Trim().ToUpperInvariant();
            return normalized.Contains("CAMERA_TRIGGER_" + token) ||
                   normalized.Contains("CAMERA_" + token + "_TRIGGER") ||
                   normalized.EndsWith("_" + token, StringComparison.Ordinal);
        }

        private static EncoderConfigurationTemplate ResolveMainEncoder(MachineRuntimeConfiguration configuration)
        {
            string axisCode = configuration?.RuntimeBindings?.MainEncoderAxisCode;
            return configuration?.EncoderTemplates?.FirstOrDefault(encoder =>
                       string.Equals(encoder.AxisName, axisCode, StringComparison.OrdinalIgnoreCase))
                   ?? configuration?.EncoderTemplates?.FirstOrDefault();
        }

        private static long ConvertMillimetersToPulses(double millimeters, double countsPerMillimeter, bool requirePositive)
        {
            double raw = Math.Max(0.0, millimeters) * countsPerMillimeter;
            long pulses = raw >= long.MaxValue
                ? long.MaxValue
                : (long)Math.Round(raw, MidpointRounding.AwayFromZero);
            return requirePositive ? Math.Max(1L, pulses) : Math.Max(0L, pulses);
        }

        private static long ConvertSignedMillimetersToPulses(double millimeters, double countsPerMillimeter)
        {
            double raw = millimeters * countsPerMillimeter;
            if (raw >= long.MaxValue)
            {
                return long.MaxValue;
            }

            if (raw <= long.MinValue)
            {
                return long.MinValue;
            }

            return (long)Math.Round(raw, MidpointRounding.AwayFromZero);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static MachineRuntimeConfiguration CloneRuntimeConfiguration(MachineRuntimeConfiguration source)
        {
            return new MachineRuntimeConfiguration
            {
                ConfigurationName = source.ConfigurationName,
                ConfigurationVersion = source.ConfigurationVersion,
                LastUpdatedUtc = source.LastUpdatedUtc,
                DigitalIoBoard = source.DigitalIoBoard,
                EncoderBoard = source.EncoderBoard,
                TriggerMode = source.TriggerMode,
                RejectMode = source.RejectMode,
                Notes = source.Notes,
                MachineInputs = source.MachineInputs ?? new List<MachineSignalDefinition>(),
                MachineOutputs = source.MachineOutputs ?? new List<MachineSignalDefinition>(),
                EncoderTemplates = source.EncoderTemplates ?? new List<EncoderConfigurationTemplate>(),
                InterventionPoints = (source.InterventionPoints ?? new List<MachineInterventionPoint>())
                    .Select(CloneInterventionPoint)
                    .ToList(),
                RuntimeBindings = source.RuntimeBindings ?? new MachineRuntimeBindings(),
                MachineMultiShotTrigger = CloneMultiShotConfiguration(source.MachineMultiShotTrigger),
                AdditionalRuntimeBindings = source.AdditionalRuntimeBindings ?? new List<AdditionalRuntimeBindingDefinition>()
            };
        }

        private static MachineInterventionPoint CloneInterventionPoint(MachineInterventionPoint source)
        {
            return new MachineInterventionPoint
            {
                PointCode = source.PointCode,
                Description = source.Description,
                Enabled = source.Enabled,
                ReferenceCode = source.ReferenceCode,
                BaseOffsetMm = source.BaseOffsetMm,
                TrimOffsetMm = source.TrimOffsetMm,
                ActionType = source.ActionType,
                SignalCode = source.SignalCode,
                PulseMs = source.PulseMs,
                Notes = source.Notes,
                IsStandard = source.IsStandard
            };
        }

        private static MachineMultiShotTriggerConfiguration CloneMultiShotConfiguration(
            MachineMultiShotTriggerConfiguration source)
        {
            source = source ?? new MachineMultiShotTriggerConfiguration();
            return new MachineMultiShotTriggerConfiguration
            {
                Side = CloneMultiShotOptions(source.Side ?? MultiShotTriggerOptions.CreateDefault("Side", "Left", "OUT_CAMERA_SIDE_TRIGGER")),
                Right = CloneMultiShotOptions(source.Right ?? MultiShotTriggerOptions.CreateDefault("Rear", "Right", "OUT_CAMERA_REAR_TRIGGER")),
                Rear = CloneMultiShotOptions(source.Rear ?? MultiShotTriggerOptions.CreateDefault("Rear", "Rear", "OUT_CAMERA_REAR_TRIGGER")),
                Bottom = CloneMultiShotOptions(source.Bottom ?? MultiShotTriggerOptions.CreateDefault("Bottom", "Bottom", "OUT_CAMERA_BOTTOM_TRIGGER"))
            };
        }

        private static MultiShotTriggerOptions CloneMultiShotOptions(MultiShotTriggerOptions source)
        {
            MultiShotVisionProStitchingOptions stitching = source.VisionProStitching ?? new MultiShotVisionProStitchingOptions();
            MultiShotDualIlluminationOptions dualIllumination = source.DualIllumination ?? new MultiShotDualIlluminationOptions();
            return new MultiShotTriggerOptions
            {
                Enabled = source.Enabled,
                CameraRole = source.CameraRole,
                DisplayName = source.DisplayName,
                TriggerOutputName = source.TriggerOutputName,
                TriggerPulseMs = source.TriggerPulseMs,
                MinimumInterShotIntervalMs = source.MinimumInterShotIntervalMs,
                ShotCount = source.ShotCount,
                InitialOffsetPulses = source.InitialOffsetPulses,
                StepPulses = source.StepPulses,
                MaxShotCount = source.MaxShotCount,
                SessionTimeoutMs = source.SessionTimeoutMs,
                TargetLateTolerancePulses = source.TargetLateTolerancePulses,
                RequireMachineRunning = source.RequireMachineRunning,
                PositiveDirection = source.PositiveDirection,
                VisionProStitching = new MultiShotVisionProStitchingOptions
                {
                    ToolBlockName = stitching.ToolBlockName,
                    ExpectedFramesInputName = stitching.ExpectedFramesInputName,
                    StepMmInputName = stitching.StepMmInputName,
                    MmPerPixelInputName = stitching.MmPerPixelInputName,
                    StepMm = stitching.StepMm,
                    MmPerPixel = stitching.MmPerPixel
                },
                DualIllumination = new MultiShotDualIlluminationOptions
                {
                    Enabled = dualIllumination.Enabled,
                    FrontFirst = dualIllumination.FrontFirst,
                    ExposureTimeUs = dualIllumination.ExposureTimeUs,
                    FrontOutputLine = dualIllumination.FrontOutputLine,
                    BackOutputLine = dualIllumination.BackOutputLine,
                    ActiveOutputSource = dualIllumination.ActiveOutputSource,
                    DualIlluminationEnabledInputName = dualIllumination.DualIlluminationEnabledInputName,
                    FrontFirstInputName = dualIllumination.FrontFirstInputName
                }
            };
        }
    }
}
