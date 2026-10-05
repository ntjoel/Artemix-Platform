using QtisVisionPanel.Models;
using QtisVisionPanel.Models.MultiShotTrigger;
using System;

namespace QtisVisionPanel.Services.MultiShotTrigger
{
    public interface IMultiShotTriggerController : IDisposable
    {
        event EventHandler<MultiShotTriggerEventArgs> EventRaised;

        bool IsEnabled { get; }

        void Configure(MachineRuntimeConfiguration configuration, Func<bool> isMachineRunningAccessor);

        bool OnProductTriggerReceived(long currentEncoderCount);

        void OnEncoderChanged(int encoderChannel, long currentEncoderCount);

        void CancelActiveSession(string reason);

        /// <summary>
        /// Optional callback invoked after each IO trigger pulse fires.
        /// Receives the profile key (e.g. "left") and returns the current
        /// VisionPro ImageStitching ToolBlock status for diagnostic logging.
        /// Set to null to disable per-pulse stitching feedback.
        /// </summary>
        Func<string, ImageStitchingStatus> StitchingStatusProvider { get; set; }

        /// <summary>
        /// Optional synchronous gate executed after plan/output validation and before
        /// a new session is stored. It is used to align camera-owned state without
        /// changing the trigger plan itself.
        /// </summary>
        Func<string, MultiShotTriggerOptions, bool> SessionPreparationHandler { get; set; }
    }
}
