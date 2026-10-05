using System;
using System.Collections.Generic;
using System.Linq;
using QtisVisionPanel.Models;

namespace QtisVisionPanel.Services
{
    public static class MachineRuntimeIo
    {
        public static bool IsSignalLogicallyActive(MachineSignalDefinition signal, bool electricalState)
        {
            return signal?.Polarity == MachineSignalPolarity.ActiveLow
                ? !electricalState
                : electricalState;
        }

        public static bool ToElectricalOutputState(MachineSignalDefinition signal, bool logicalState)
        {
            return signal?.Polarity == MachineSignalPolarity.ActiveLow
                ? !logicalState
                : logicalState;
        }

        public static int ParseChannelNumber(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
            {
                return -1;
            }

            var digits = new string(channel.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var parsed) ? parsed : -1;
        }

        public static bool IsMappedPhysicalOutput(
            MachineSignalDefinition signal,
            MachineSignalCategory? requiredCategory = null)
        {
            if (signal == null ||
                signal.Direction != MachineSignalDirection.Output ||
                ParseChannelNumber(signal.Channel) < 0)
            {
                return false;
            }

            return !requiredCategory.HasValue || signal.Category == requiredCategory.Value;
        }

        public static MachineSignalDefinition ResolveMappedPhysicalOutput(
            IEnumerable<MachineSignalDefinition> outputs,
            string signalCode,
            MachineSignalCategory? requiredCategory = null)
        {
            if (string.IsNullOrWhiteSpace(signalCode))
            {
                return null;
            }

            return (outputs ?? Enumerable.Empty<MachineSignalDefinition>())
                .Where(signal =>
                    string.Equals(signal?.SignalCode?.Trim(), signalCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    IsMappedPhysicalOutput(signal, requiredCategory))
                .OrderByDescending(signal => signal.ReservedForRealSignal)
                .ThenByDescending(signal => signal.IsRuntimeBound)
                .FirstOrDefault();
        }

        public static int GetEncoderChannelNumber(EncoderConfigurationTemplate template)
        {
            return ParseChannelNumber(template?.Channel);
        }

        public static double CalculateCountsPerMillimeter(EncoderConfigurationTemplate template)
        {
            if (template == null || template.MillimetersPerRevolution <= 0)
            {
                return 0.0;
            }

            return Math.Max(0, template.PulsesPerRevolution) *
                   ResolveEncoderTrackingMultiplier(template.TrackingMode) /
                   template.MillimetersPerRevolution;
        }

        public static int ConvertMillimetersToCounts(double millimeters, EncoderConfigurationTemplate encoder)
        {
            var countsPerMillimeter = CalculateCountsPerMillimeter(encoder);
            if (countsPerMillimeter <= 0.0)
            {
                return 0;
            }

            return (int)Math.Round(millimeters * countsPerMillimeter, MidpointRounding.AwayFromZero);
        }

        public static double ConvertCountsToMillimeters(long counts, EncoderConfigurationTemplate encoder)
        {
            var countsPerMillimeter = CalculateCountsPerMillimeter(encoder);
            return countsPerMillimeter <= 0.0 ? 0.0 : counts / countsPerMillimeter;
        }

        public static double ResolveEncoderTrackingMultiplier(string trackingMode)
        {
            if (string.IsNullOrWhiteSpace(trackingMode))
            {
                return 1.0;
            }

            var normalized = trackingMode.Trim().ToLowerInvariant();
            if (normalized.Contains("x4") || normalized.Contains("4x"))
            {
                return 4.0;
            }

            if (normalized.Contains("x2") || normalized.Contains("2x"))
            {
                return 2.0;
            }

            return 1.0;
        }
    }
}
