using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using NLog;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.MultiShotTrigger;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QtisVisionPanel.Services.MultiShotTrigger
{
    public sealed class VisionProStitchingParameterApplier
    {
        private const string SourceName = nameof(VisionProStitchingParameterApplier);
        private readonly ApplicationEventLogger _eventLogger;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public VisionProStitchingParameterApplier(ApplicationEventLogger eventLogger = null)
        {
            _eventLogger = eventLogger ?? ServiceLocator.ApplicationEventLogger;
        }

        public bool ApplyIfRequired(
            CogJob job,
            string runtimeRole,
            MachineMultiShotTriggerConfiguration multiShotConfiguration,
            string jobName = null,
            int jobId = -1)
        {
            string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(runtimeRole);
            var options = multiShotConfiguration?.ResolveOptionsForRuntimeRole(normalizedRole);
            if (options?.Enabled != true)
            {
                return false;
            }

            if (!MachineMultiShotTriggerConfiguration.IsCompatibleRuntimeRole(options, normalizedRole))
            {
                return false;
            }

            var stitching = options.VisionProStitching ?? new MultiShotVisionProStitchingOptions();
            if (!ValidateConfiguration(options, stitching, jobName, jobId))
            {
                return false;
            }

            var toolBlock = FindToolBlock(job, stitching.ToolBlockName);
            if (toolBlock == null)
            {
                Log(
                    LogLevel.Warn,
                    "VISIONPRO_STITCHING_TOOLBLOCK_NOT_FOUND",
                    $"VisionPro stitching ToolBlock '{ResolveToolBlockName(stitching)}' not found for job '{jobName ?? job?.Name ?? "<unknown>"}'.",
                    jobName,
                    jobId,
                    normalizedRole,
                    options,
                    stitching);
                return false;
            }

            if (!TryWriteInput(toolBlock, stitching.ExpectedFramesInputName, options.ShotCount, jobName, jobId, normalizedRole, options, stitching) ||
                !TryWriteInput(toolBlock, stitching.StepMmInputName, stitching.StepMm, jobName, jobId, normalizedRole, options, stitching) ||
                !TryWriteInput(toolBlock, stitching.MmPerPixelInputName, stitching.MmPerPixel, jobName, jobId, normalizedRole, options, stitching))
            {
                return false;
            }

            var dualIllumination = options.DualIllumination ?? new MultiShotDualIlluminationOptions();
            if (dualIllumination.Enabled)
            {
                if (!TryWriteInput(toolBlock, dualIllumination.DualIlluminationEnabledInputName, true, jobName, jobId, normalizedRole, options, stitching) ||
                    !TryWriteInput(toolBlock, dualIllumination.FrontFirstInputName, dualIllumination.FrontFirst, jobName, jobId, normalizedRole, options, stitching))
                {
                    return false;
                }
            }
            else
            {
                // Avoid leaving a VPP in dual mode after the machine profile is disabled.
                // Legacy ToolBlocks without the optional input remain fully compatible.
                TryWriteOptionalInput(toolBlock, dualIllumination.DualIlluminationEnabledInputName, false);
            }

            Log(
                LogLevel.Info,
                "VISIONPRO_STITCHING_PARAMS_APPLIED",
                $"VisionPro stitching parameters applied to '{toolBlock.Name}': expectedFrames={options.ShotCount}, stepMm={stitching.StepMm.ToString(CultureInfo.InvariantCulture)}, mmPerPixel={stitching.MmPerPixel.ToString(CultureInfo.InvariantCulture)}, dualIllumination={dualIllumination.Enabled}, frontFirst={dualIllumination.FrontFirst}.",
                jobName,
                jobId,
                normalizedRole,
                options,
                stitching,
                new Dictionary<string, object>
                {
                    { "toolblock_name", toolBlock.Name },
                    { "expected_frames", options.ShotCount },
                    { "step_mm", stitching.StepMm },
                    { "mm_per_pixel", stitching.MmPerPixel },
                    { "dual_illumination_enabled", dualIllumination.Enabled },
                    { "front_first", dualIllumination.FrontFirst }
                });

            return true;
        }

        private bool ValidateConfiguration(
            MultiShotTriggerOptions options,
            MultiShotVisionProStitchingOptions stitching,
            string jobName,
            int jobId)
        {
            var errors = new List<string>();
            if (options.ShotCount <= 0)
            {
                errors.Add("ShotCount must be > 0");
            }

            if (stitching.StepMm <= 0)
            {
                errors.Add("VisionProStitching.StepMm must be > 0");
            }

            if (stitching.MmPerPixel <= 0)
            {
                errors.Add("VisionProStitching.MmPerPixel must be > 0");
            }

            if (string.IsNullOrWhiteSpace(stitching.ToolBlockName))
            {
                errors.Add("VisionProStitching.ToolBlockName is missing");
            }

            if (string.IsNullOrWhiteSpace(stitching.ExpectedFramesInputName) ||
                string.IsNullOrWhiteSpace(stitching.StepMmInputName) ||
                string.IsNullOrWhiteSpace(stitching.MmPerPixelInputName))
            {
                errors.Add("VisionProStitching input names must not be empty");
            }

            var dualIllumination = options.DualIllumination ?? new MultiShotDualIlluminationOptions();
            if (dualIllumination.Enabled)
            {
                if (options.ShotCount < 2 || options.ShotCount % 2 != 0)
                {
                    errors.Add("Dual illumination requires an even ShotCount of at least 2");
                }

                if (string.IsNullOrWhiteSpace(dualIllumination.DualIlluminationEnabledInputName) ||
                    string.IsNullOrWhiteSpace(dualIllumination.FrontFirstInputName))
                {
                    errors.Add("Dual illumination VisionPro input names must not be empty");
                }
            }

            if (errors.Count == 0)
            {
                return true;
            }

            Log(
                LogLevel.Warn,
                "VISIONPRO_STITCHING_CONFIG_INVALID",
                "VisionPro stitching configuration invalid: " + string.Join("; ", errors),
                jobName,
                jobId,
                options?.CameraRole,
                options,
                stitching,
                new Dictionary<string, object> { { "errors", errors } });

            return false;
        }

        private bool TryWriteInput(
            CogToolBlock toolBlock,
            string inputName,
            object value,
            string jobName,
            int jobId,
            string runtimeRole,
            MultiShotTriggerOptions options,
            MultiShotVisionProStitchingOptions stitching)
        {
            if (toolBlock?.Inputs == null ||
                string.IsNullOrWhiteSpace(inputName) ||
                !toolBlock.Inputs.Contains(inputName))
            {
                Log(
                    LogLevel.Warn,
                    "VISIONPRO_STITCHING_INPUT_MISSING",
                    $"VisionPro stitching input '{inputName}' missing in ToolBlock '{toolBlock?.Name ?? ResolveToolBlockName(stitching)}'.",
                    jobName,
                    jobId,
                    runtimeRole,
                    options,
                    stitching,
                    new Dictionary<string, object>
                    {
                        { "toolblock_name", toolBlock?.Name ?? ResolveToolBlockName(stitching) },
                        { "missing_input", inputName },
                        { "available_inputs", DescribeInputs(toolBlock) }
                    });
                return false;
            }

            try
            {
                toolBlock.Inputs[inputName].Value = value;
                return true;
            }
            catch (Exception ex)
            {
                Log(
                    LogLevel.Error,
                    "VISIONPRO_STITCHING_INPUT_MISSING",
                    $"VisionPro stitching input '{inputName}' could not be written: {ex.Message}",
                    jobName,
                    jobId,
                    runtimeRole,
                    options,
                    stitching,
                    new Dictionary<string, object>
                    {
                        { "toolblock_name", toolBlock.Name },
                        { "input_name", inputName },
                        { "value", value },
                        { "exception", ex.Message }
                    });
                return false;
            }
        }

        private static void TryWriteOptionalInput(CogToolBlock toolBlock, string inputName, object value)
        {
            if (toolBlock?.Inputs == null ||
                string.IsNullOrWhiteSpace(inputName) ||
                !toolBlock.Inputs.Contains(inputName))
            {
                return;
            }

            try
            {
                toolBlock.Inputs[inputName].Value = value;
            }
            catch
            {
                // Optional compatibility write: required inputs still use TryWriteInput.
            }
        }

        private static CogToolBlock FindToolBlock(CogJob job, string toolBlockName)
        {
            string resolvedName = ResolveToolBlockName(toolBlockName);
            return FindToolBlockRecursive(job?.VisionTool, resolvedName, 0);
        }

        private static CogToolBlock FindToolBlockRecursive(object toolOrGroup, string toolBlockName, int depth)
        {
            if (toolOrGroup == null || depth > 16)
            {
                return null;
            }

            if (toolOrGroup is CogToolBlock toolBlock &&
                string.Equals(toolBlock.Name, toolBlockName, StringComparison.OrdinalIgnoreCase))
            {
                return toolBlock;
            }

            var tools = ResolveNestedTools(toolOrGroup);
            if (tools == null)
            {
                return null;
            }

            foreach (var child in tools)
            {
                var nestedMatch = FindToolBlockRecursive(child, toolBlockName, depth + 1);
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }

            return null;
        }

        private static System.Collections.IEnumerable ResolveNestedTools(object toolOrGroup)
        {
            try
            {
                return toolOrGroup?.GetType().GetProperty("Tools")?.GetValue(toolOrGroup, null) as System.Collections.IEnumerable;
            }
            catch
            {
                return null;
            }
        }

        private void Log(
            LogLevel level,
            string logId,
            string message,
            string jobName,
            int jobId,
            string runtimeRole,
            MultiShotTriggerOptions options,
            MultiShotVisionProStitchingOptions stitching,
            IDictionary<string, object> extraMetadata = null)
        {
            var metadata = new Dictionary<string, object>
            {
                { "job_name", jobName },
                { "job_id", jobId },
                { "runtime_role", runtimeRole },
                { "enabled", options?.Enabled },
                { "shot_count", options?.ShotCount },
                { "toolblock_name", ResolveToolBlockName(stitching) },
                { "expected_frames_input", stitching?.ExpectedFramesInputName },
                { "step_mm_input", stitching?.StepMmInputName },
                { "mm_per_pixel_input", stitching?.MmPerPixelInputName },
                { "step_mm", stitching?.StepMm },
                { "mm_per_pixel", stitching?.MmPerPixel }
            };

            if (extraMetadata != null)
            {
                foreach (var item in extraMetadata)
                {
                    metadata[item.Key] = item.Value;
                }
            }

            _logger.Log(level, $"{logId}|{message}");
            _eventLogger?.LogOperationalEvent(level, logId, "VisionPro", message, SourceName, null, metadata);
        }

        private static string DescribeInputs(CogToolBlock toolBlock)
        {
            try
            {
                if (toolBlock?.Inputs == null)
                {
                    return "<none>";
                }

                var names = new List<string>();
                for (int i = 0; i < toolBlock.Inputs.Count; i++)
                {
                    string name = toolBlock.Inputs[i]?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }

                return names.Count == 0 ? "<none>" : string.Join(", ", names);
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string ResolveToolBlockName(MultiShotVisionProStitchingOptions stitching)
        {
            return ResolveToolBlockName(stitching?.ToolBlockName);
        }

        private static string ResolveToolBlockName(string configuredName)
        {
            return string.IsNullOrWhiteSpace(configuredName)
                ? "ImageStitching"
                : configuredName.Trim();
        }
    }
}
