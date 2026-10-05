using System;
using System.Globalization;
using System.Windows.Data;

namespace QtisVisionPanel.Converters
{
    public class DecimalTextBoxConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return string.Empty;
            }

            try
            {
                var number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return number.ToString("0.######", CultureInfo.InvariantCulture);
            }
            catch
            {
                return value.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value == null ? string.Empty : value.ToString().Trim();
            if (IsIntermediateDecimalText(text))
            {
                return Binding.DoNothing;
            }

            double parsed;
            if (TryParseFlexibleDecimal(text, out parsed))
            {
                return parsed;
            }

            return Binding.DoNothing;
        }

        private static bool IsIntermediateDecimalText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (text == "." || text == "," || text == "+" || text == "-" || text == "+." || text == "-." || text == "+," || text == "-,")
            {
                return true;
            }

            if (text.EndsWith(".", StringComparison.Ordinal) || text.EndsWith(",", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseFlexibleDecimal(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = NormalizeDecimalText(text);
            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static string NormalizeDecimalText(string text)
        {
            var trimmed = text.Trim();
            var lastDot = trimmed.LastIndexOf('.');
            var lastComma = trimmed.LastIndexOf(',');

            if (lastDot >= 0 && lastComma >= 0)
            {
                if (lastDot > lastComma)
                {
                    return trimmed.Replace(",", string.Empty);
                }

                return trimmed.Replace(".", string.Empty).Replace(',', '.');
            }

            return trimmed.Replace(',', '.');
        }
    }
}
