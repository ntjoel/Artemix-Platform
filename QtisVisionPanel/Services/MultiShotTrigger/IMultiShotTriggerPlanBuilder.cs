using QtisVisionPanel.Models.MultiShotTrigger;

namespace QtisVisionPanel.Services.MultiShotTrigger
{
    public interface IMultiShotTriggerPlanBuilder
    {
        MultiShotTriggerPlan Build(long encoderReference, MultiShotTriggerOptions options);
    }
}
