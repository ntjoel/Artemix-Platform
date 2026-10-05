using QtisVisionPanel.Models.MultiShotTrigger;
using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Services.MultiShotTrigger
{
    public sealed class MultiShotTriggerPlanBuilder : IMultiShotTriggerPlanBuilder
    {
        public MultiShotTriggerPlan Build(long encoderReference, MultiShotTriggerOptions options)
        {
            if (options == null || !options.Enabled)
            {
                return null;
            }

            if (options.ShotCount <= 0)
            {
                throw new InvalidOperationException($"{DescribeProfile(options)} multishot ShotCount must be > 0.");
            }

            if (options.MaxShotCount <= 0)
            {
                throw new InvalidOperationException($"{DescribeProfile(options)} multishot MaxShotCount must be > 0.");
            }

            if (options.ShotCount > options.MaxShotCount)
            {
                throw new InvalidOperationException($"{DescribeProfile(options)} multishot ShotCount exceeds MaxShotCount.");
            }

            if (options.ShotCount > 1 && options.StepPulses <= 0)
            {
                throw new InvalidOperationException($"{DescribeProfile(options)} multishot StepPulses must be > 0 when ShotCount > 1.");
            }

            if (string.IsNullOrWhiteSpace(options.TriggerOutputName))
            {
                throw new InvalidOperationException($"{DescribeProfile(options)} multishot TriggerOutputName is missing.");
            }

            if (options.TriggerPulseMs <= 0)
            {
                throw new InvalidOperationException($"{DescribeProfile(options)} multishot TriggerPulseMs must be > 0.");
            }

            var targets = new List<MultiShotTriggerTarget>();
            for (int i = 0; i < options.ShotCount; i++)
            {
                long relativeOffset = options.InitialOffsetPulses + (i * options.StepPulses);
                long target = options.PositiveDirection
                    ? encoderReference + relativeOffset
                    : encoderReference - relativeOffset;

                targets.Add(new MultiShotTriggerTarget
                {
                    Index = i,
                    TargetPulses = target
                });
            }

            return new MultiShotTriggerPlan
            {
                CameraRole = string.IsNullOrWhiteSpace(options.CameraRole) ? "Side" : options.CameraRole,
                DisplayName = string.IsNullOrWhiteSpace(options.DisplayName) ? "Left" : options.DisplayName,
                TriggerOutputName = options.TriggerOutputName,
                TriggerPulseMs = options.TriggerPulseMs,
                MinimumInterShotIntervalMs = Math.Max(0, options.MinimumInterShotIntervalMs),
                PositiveDirection = options.PositiveDirection,
                TargetLateTolerancePulses = Math.Max(0, options.TargetLateTolerancePulses),
                Targets = targets
            };
        }

        private static string DescribeProfile(MultiShotTriggerOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options?.DisplayName))
            {
                return options.DisplayName;
            }

            return string.IsNullOrWhiteSpace(options?.CameraRole) ? "Camera" : options.CameraRole;
        }
    }
}
