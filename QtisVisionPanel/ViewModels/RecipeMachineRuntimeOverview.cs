using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.MultiShotTrigger;
using QtisVisionPanel.Services;
using System;
using System.Globalization;
using System.Linq;

namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// Read-only preview of the machine values and the recipe-adjusted runtime values.
    /// It is rebuilt from the saved machine configuration and never persists data.
    /// </summary>
    public sealed class RecipeMachineRuntimeOverview
    {
        public string TopPositionSummary { get; private set; }
        public string SidePositionSummary { get; private set; }
        public string LeftPositionSummary { get; private set; }
        public string FrontPositionSummary { get; private set; }
        public string RightPositionSummary { get; private set; }
        public string RearPositionSummary { get; private set; }
        public string BottomPositionSummary { get; private set; }

        public string SideLeftModeSummary { get; private set; }
        public string SideLeftShotSummary { get; private set; }
        public string SideLeftStepSummary { get; private set; }
        public string SideLeftFirstShotSummary { get; private set; }

        public string RightRearModeSummary { get; private set; }
        public string RightRearShotSummary { get; private set; }
        public string RightRearStepSummary { get; private set; }
        public string RightRearFirstShotSummary { get; private set; }
        public string RearModeSummary { get; private set; }
        public string RearShotSummary { get; private set; }
        public string RearStepSummary { get; private set; }
        public string RearFirstShotSummary { get; private set; }

        public string BottomModeSummary { get; private set; }
        public string BottomShotSummary { get; private set; }
        public string BottomStepSummary { get; private set; }
        public string BottomFirstShotSummary { get; private set; }

        public bool HasValidationErrors { get; private set; }
        public string ValidationSummary { get; private set; }

        public static RecipeMachineRuntimeOverview Create(
            MachineRuntimeConfiguration machineConfiguration,
            RecipeParameters.RecipeData recipe,
            Func<string, string, string> localize)
        {
            Func<string, string, string> message = localize ?? ((key, fallback) => fallback);
            string unavailable = message(
                "Sub_entry_RecipeGlobalValueUnavailable",
                "Global machine value not available");

            var overview = CreateUnavailable(unavailable);
            if (machineConfiguration == null)
            {
                return overview;
            }

            RecipeMachineRuntimeResolution resolution =
                RecipeMachineRuntimeResolver.Resolve(machineConfiguration, recipe);
            MachineRuntimeConfiguration effectiveConfiguration = resolution.Configuration;
            if (effectiveConfiguration == null)
            {
                overview.HasValidationErrors = true;
                overview.ValidationSummary = message(
                    "Sub_entry_RecipeRuntimePreviewInvalid",
                    "Some values are invalid. The preview shows the safe runtime fallback.");
                return overview;
            }

            overview.TopPositionSummary = BuildPositionSummary(machineConfiguration, effectiveConfiguration, "Top", message, unavailable);
            overview.SidePositionSummary = BuildPositionSummary(machineConfiguration, effectiveConfiguration, "Side", message, unavailable);
            overview.LeftPositionSummary = BuildPositionSummary(machineConfiguration, effectiveConfiguration, "Left", message, unavailable);
            overview.FrontPositionSummary = BuildPositionSummary(machineConfiguration, effectiveConfiguration, "Front", message, unavailable);
            overview.RightPositionSummary = BuildPositionSummary(machineConfiguration, effectiveConfiguration, "Right", message, unavailable);
            overview.RearPositionSummary = BuildPositionSummary(machineConfiguration, effectiveConfiguration, "Rear", message, unavailable);
            overview.BottomPositionSummary = BuildPositionSummary(machineConfiguration, effectiveConfiguration, "Bottom", message, unavailable);

            double countsPerMillimeter = ResolveCountsPerMillimeter(machineConfiguration);
            MultiShotOverview sideLeft = BuildMultiShotOverview(
                machineConfiguration.MachineMultiShotTrigger?.Side,
                effectiveConfiguration.MachineMultiShotTrigger?.Side,
                countsPerMillimeter,
                message,
                unavailable);
            overview.SideLeftModeSummary = sideLeft.Mode;
            overview.SideLeftShotSummary = sideLeft.Shots;
            overview.SideLeftStepSummary = sideLeft.Step;
            overview.SideLeftFirstShotSummary = sideLeft.FirstShot;

            MultiShotOverview rightRear = BuildMultiShotOverview(
                machineConfiguration.MachineMultiShotTrigger?.Right,
                effectiveConfiguration.MachineMultiShotTrigger?.Right,
                countsPerMillimeter,
                message,
                unavailable);
            overview.RightRearModeSummary = rightRear.Mode;
            overview.RightRearShotSummary = rightRear.Shots;
            overview.RightRearStepSummary = rightRear.Step;
            overview.RightRearFirstShotSummary = rightRear.FirstShot;

            MultiShotOverview rear = BuildMultiShotOverview(
                machineConfiguration.MachineMultiShotTrigger?.Rear,
                effectiveConfiguration.MachineMultiShotTrigger?.Rear,
                countsPerMillimeter, message, unavailable);
            overview.RearModeSummary = rear.Mode;
            overview.RearShotSummary = rear.Shots;
            overview.RearStepSummary = rear.Step;
            overview.RearFirstShotSummary = rear.FirstShot;

            MultiShotOverview bottom = BuildMultiShotOverview(
                machineConfiguration.MachineMultiShotTrigger?.Bottom,
                effectiveConfiguration.MachineMultiShotTrigger?.Bottom,
                countsPerMillimeter,
                message,
                unavailable);
            overview.BottomModeSummary = bottom.Mode;
            overview.BottomShotSummary = bottom.Shots;
            overview.BottomStepSummary = bottom.Step;
            overview.BottomFirstShotSummary = bottom.FirstShot;

            overview.HasValidationErrors = resolution.Errors.Count > 0;
            overview.ValidationSummary = overview.HasValidationErrors
                ? message(
                    "Sub_entry_RecipeRuntimePreviewInvalid",
                    "Some values are invalid. The preview shows the safe runtime fallback.")
                : string.Empty;
            return overview;
        }

        private static RecipeMachineRuntimeOverview CreateUnavailable(string unavailable)
        {
            return new RecipeMachineRuntimeOverview
            {
                TopPositionSummary = unavailable,
                SidePositionSummary = unavailable,
                LeftPositionSummary = unavailable,
                FrontPositionSummary = unavailable,
                RightPositionSummary = unavailable,
                RearPositionSummary = unavailable,
                BottomPositionSummary = unavailable,
                SideLeftModeSummary = unavailable,
                SideLeftShotSummary = unavailable,
                SideLeftStepSummary = unavailable,
                SideLeftFirstShotSummary = unavailable,
                RightRearModeSummary = unavailable,
                RightRearShotSummary = unavailable,
                RightRearStepSummary = unavailable,
                RightRearFirstShotSummary = unavailable,
                BottomModeSummary = unavailable,
                BottomShotSummary = unavailable,
                BottomStepSummary = unavailable,
                BottomFirstShotSummary = unavailable,
                ValidationSummary = string.Empty
            };
        }

        private static string BuildPositionSummary(
            MachineRuntimeConfiguration machine,
            MachineRuntimeConfiguration effective,
            string role,
            Func<string, string, string> message,
            string unavailable)
        {
            MachineInterventionPoint globalPoint =
                RecipeMachineRuntimeResolver.ResolveCameraInterventionPoint(machine, role);
            MachineInterventionPoint effectivePoint =
                RecipeMachineRuntimeResolver.ResolveCameraInterventionPoint(effective, role);
            if (globalPoint == null || effectivePoint == null)
            {
                return unavailable;
            }

            return FormatMessage(
                message,
                "Sub_entry_RecipeGlobalEffectivePositionFormat",
                "Global: {0} mm | Effective: {1} mm",
                FormatNumber(globalPoint.EffectiveOffsetMm),
                FormatNumber(effectivePoint.EffectiveOffsetMm));
        }

        private static MultiShotOverview BuildMultiShotOverview(
            MultiShotTriggerOptions global,
            MultiShotTriggerOptions effective,
            double countsPerMillimeter,
            Func<string, string, string> message,
            string unavailable)
        {
            if (global == null || effective == null)
            {
                return MultiShotOverview.Unavailable(unavailable);
            }

            string enabled = message("Sub_entry_RecipeStateEnabled", "Enabled");
            string disabled = message("Sub_entry_RecipeStateDisabled", "Disabled");
            string mode = FormatMessage(
                message,
                "Sub_entry_RecipeGlobalEffectiveModeFormat",
                "Global: {0} | Effective: {1}",
                global.Enabled ? enabled : disabled,
                effective.Enabled ? enabled : disabled);

            string shots = FormatMessage(
                message,
                "Sub_entry_RecipeGlobalEffectiveShotsFormat",
                "Global: {0} | Effective: {1} | Limit: {2}",
                global.ShotCount,
                effective.ShotCount,
                Math.Max(1, global.MaxShotCount));

            string step = FormatMessage(
                message,
                "Sub_entry_RecipeGlobalEffectiveStepFormat",
                "Global: {0} mm | Effective: {1} mm",
                FormatNumber(global.VisionProStitching?.StepMm ?? 0.0),
                FormatNumber(effective.VisionProStitching?.StepMm ?? 0.0));

            string firstShot = unavailable;
            if (countsPerMillimeter > 0.0)
            {
                firstShot = FormatMessage(
                    message,
                    "Sub_entry_RecipeGlobalEffectiveFirstShotFormat",
                    "Global origin: {0} mm | Effective: {1} mm",
                    FormatNumber(global.InitialOffsetPulses / countsPerMillimeter),
                    FormatNumber(effective.InitialOffsetPulses / countsPerMillimeter));
            }

            return new MultiShotOverview(mode, shots, step, firstShot);
        }

        private static double ResolveCountsPerMillimeter(MachineRuntimeConfiguration configuration)
        {
            string axisCode = configuration?.RuntimeBindings?.MainEncoderAxisCode;
            EncoderConfigurationTemplate encoder = configuration?.EncoderTemplates?.FirstOrDefault(item =>
                string.Equals(item.AxisName, axisCode, StringComparison.OrdinalIgnoreCase))
                ?? configuration?.EncoderTemplates?.FirstOrDefault();
            return MachineRuntimeIo.CalculateCountsPerMillimeter(encoder);
        }

        private static string FormatMessage(
            Func<string, string, string> message,
            string key,
            string fallback,
            params object[] values)
        {
            string format = message(key, fallback) ?? fallback;
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, values);
            }
            catch (FormatException)
            {
                return string.Format(CultureInfo.CurrentCulture, fallback, values);
            }
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        private sealed class MultiShotOverview
        {
            public MultiShotOverview(string mode, string shots, string step, string firstShot)
            {
                Mode = mode;
                Shots = shots;
                Step = step;
                FirstShot = firstShot;
            }

            public string Mode { get; }
            public string Shots { get; }
            public string Step { get; }
            public string FirstShot { get; }

            public static MultiShotOverview Unavailable(string unavailable)
            {
                return new MultiShotOverview(unavailable, unavailable, unavailable, unavailable);
            }
        }
    }
}
