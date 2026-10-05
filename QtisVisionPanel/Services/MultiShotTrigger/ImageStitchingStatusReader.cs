using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using System;
using System.Collections;
using System.Globalization;

namespace QtisVisionPanel.Services.MultiShotTrigger
{
    /// <summary>
    /// Snapshot of the outputs written by the VisionPro ImageStitching ToolBlock script
    /// after each acquired frame.
    /// </summary>
    public readonly struct ImageStitchingStatus
    {
        /// <summary>False when the job is null or the ToolBlock was not found.</summary>
        public bool IsAvailable { get; }
        public bool IsReady    { get; }
        public int  FrameIndex { get; }
        public string Status       { get; }
        public string ErrorMessage { get; }

        public ImageStitchingStatus(bool isReady, int frameIndex, string status, string errorMessage)
        {
            IsAvailable  = true;
            IsReady      = isReady;
            FrameIndex   = frameIndex;
            Status       = status       ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }
    }

    /// <summary>
    /// Reads the current stitching status from a VisionPro ImageStitching ToolBlock.
    /// The job script (PostAcquisitionRef) writes isReady / frameIndex / status / errorMessage
    /// to the ToolBlock Outputs after every acquired frame.
    ///
    /// Accepts <see cref="object"/> for the job parameter so callers (e.g. ViewModels)
    /// do not need a direct Cognex assembly reference in their compilation unit.
    /// </summary>
    public static class ImageStitchingStatusReader
    {
        private const string DefaultToolBlockName = "ImageStitching";

        public static ImageStitchingStatus ReadFromJob(
            object jobObject,
            string toolBlockName = DefaultToolBlockName)
        {
            if (jobObject == null) return default;

            var job = jobObject as CogJob;
            if (job == null) return default;

            string resolvedName = string.IsNullOrWhiteSpace(toolBlockName)
                ? DefaultToolBlockName
                : toolBlockName.Trim();

            var toolBlock = FindToolBlock(job.VisionTool, resolvedName, 0);
            if (toolBlock == null) return default;

            try
            {
                bool   isReady      = ReadBool(toolBlock,   "isReady");
                int    frameIndex   = ReadInt(toolBlock,    "frameIndex");
                string status       = ReadString(toolBlock, "status");
                string errorMessage = ReadString(toolBlock, "errorMessage");
                return new ImageStitchingStatus(isReady, frameIndex, status, errorMessage);
            }
            catch
            {
                return default;
            }
        }

        private static bool ReadBool(CogToolBlock tb, string name)
        {
            if (tb?.Outputs == null || !tb.Outputs.Contains(name)) return false;
            var v = tb.Outputs[name].Value;
            if (v is bool b) return b;
            if (v != null && bool.TryParse(v.ToString(), out bool p)) return p;
            return false;
        }

        private static int ReadInt(CogToolBlock tb, string name)
        {
            if (tb?.Outputs == null || !tb.Outputs.Contains(name)) return 0;
            var v = tb.Outputs[name].Value;
            if (v is IConvertible)
            {
                try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
                catch { }
            }
            return 0;
        }

        private static string ReadString(CogToolBlock tb, string name)
        {
            if (tb?.Outputs == null || !tb.Outputs.Contains(name)) return string.Empty;
            return tb.Outputs[name].Value?.ToString() ?? string.Empty;
        }

        private static CogToolBlock FindToolBlock(object toolOrGroup, string name, int depth)
        {
            if (toolOrGroup == null || depth > 16) return null;

            if (toolOrGroup is CogToolBlock tb &&
                string.Equals(tb.Name, name, StringComparison.OrdinalIgnoreCase))
                return tb;

            try
            {
                var tools = toolOrGroup.GetType()
                                       .GetProperty("Tools")
                                       ?.GetValue(toolOrGroup, null) as IEnumerable;
                if (tools == null) return null;
                foreach (var child in tools)
                {
                    var found = FindToolBlock(child, name, depth + 1);
                    if (found != null) return found;
                }
            }
            catch { }

            return null;
        }
    }
}
