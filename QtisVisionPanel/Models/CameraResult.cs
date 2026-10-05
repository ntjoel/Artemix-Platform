using Cognex.VisionPro;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using System;

namespace QtisVisionPanel.Models
{
    /// <summary>
    /// Carries the raw output of one camera inspection cycle from the VisionPro callback
    /// into the inspection queue pipeline.
    /// </summary>
    internal sealed class CameraResult
    {
        public ICogRecord Record { get; set; }

        /// <summary>
        /// Live job ToolBlock. Retained only for existing image/artifact fallbacks;
        /// it must not be used to validate a queued inspection result.
        /// </summary>
        public CogToolBlock ToolBlock { get; set; }

        /// <summary>
        /// Output-only snapshot captured synchronously in UserResultAvailable.
        /// Validation and operator classification use this instance so they cannot
        /// observe outputs from a later VisionPro run.
        /// </summary>
        public CogToolBlock OutputSnapshot { get; set; }

        public string CameraRole { get; set; }
        public string JobName { get; set; }
        public CogJob Job { get; set; }

        /// <summary>Monotonic HMI sequence assigned when the result is captured.</summary>
        public long ResultSequence { get; set; }

        /// <summary>VisionPro packet sequence from the UserResultTag subrecord.</summary>
        public string UserResultTag { get; set; }

        /// <summary>
        /// Product identity captured when the HMI emitted the physical camera trigger.
        /// Null is retained for synthetic, external-trigger and legacy acquisition paths.
        /// </summary>
        public long? TriggeredProductId { get; set; }

        public string TriggerPointCode { get; set; }
        public DateTime? TriggerIssuedAtUtc { get; set; }

        /// <summary>
        /// True when QuickBuild produced the result through a synthetic acquisition
        /// FIFO. Synthetic jobs run independently and have no shared product timestamp.
        /// </summary>
        public bool IsSyntheticAcquisition { get; set; }

        /// <summary>UTC timestamp set immediately before enqueueing into the role queue.</summary>
        public DateTime EnqueuedAtUtc { get; set; }

        /// <summary>
        /// Immutable processing status captured in UserResultAvailable. Reading the
        /// live CogJob RunStatus later can return the status of a subsequent run.
        /// </summary>
        public bool HasProcessingError { get; set; }
        public string RunStatusResult { get; set; }
        public string RunStatusMessage { get; set; }
        public double? RunTotalTimeMs { get; set; }
        public double? RunProcessingTimeMs { get; set; }

        public void ReleaseOutputSnapshot()
        {
            CogToolBlock snapshot = OutputSnapshot;
            OutputSnapshot = null;
            snapshot?.Dispose();
        }
    }
}
