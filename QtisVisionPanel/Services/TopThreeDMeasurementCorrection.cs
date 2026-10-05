using System;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Applies the machine-level Top3D calibration correction before recipe validation.
    /// Recipe nominal values and tolerances remain product-specific.
    /// </summary>
    public static class TopThreeDMeasurementCorrection
    {
        public const double MaximumAbsoluteOffsetMm = 1000.0;

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static bool IsValidOffset(double value)
        {
            return IsFinite(value) && Math.Abs(value) <= MaximumAbsoluteOffsetMm;
        }

        public static double NormalizeOffset(double configuredOffsetMm)
        {
            if (!IsFinite(configuredOffsetMm))
            {
                return 0;
            }

            return Math.Max(
                -MaximumAbsoluteOffsetMm,
                Math.Min(MaximumAbsoluteOffsetMm, configuredOffsetMm));
        }

        public static double Apply(double rawMeasurementMm, double configuredOffsetMm, out double appliedOffsetMm)
        {
            appliedOffsetMm = NormalizeOffset(configuredOffsetMm);
            return rawMeasurementMm + appliedOffsetMm;
        }
    }
}
