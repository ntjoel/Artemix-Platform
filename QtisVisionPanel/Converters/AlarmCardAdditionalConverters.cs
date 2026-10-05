using QtisVisionPanel.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace QtisVisionPanel.Converters
{
    public class AlarmTypeToDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is AlarmType alarmType))
            {
                return "Tipo di allarme sconosciuto";
            }

            switch (alarmType)
            {
                case AlarmType.ConsecutiveEvents:
                    return "Attiva dopo N (soglia) eventi consecutivi";
                case AlarmType.ConsecutiveEventsAlt:
                    return "Attiva dopo M (soglia) eventi consecutivi";
                case AlarmType.PercentageInBuffer:
                    return "Attiva quando la condizione raggiunge la soglia percentuale nel buffer configurato.";
                default:
                    return "Tipo di allarme sconosciuto";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("Converter usato solo in lettura.");
    }

    public class AlarmTypeToThresholdLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is AlarmType alarmType))
            {
                return "Soglia";
            }

            switch (alarmType)
            {
                case AlarmType.ConsecutiveEvents:
                    return "Soglia N";
                case AlarmType.ConsecutiveEventsAlt:
                    return "Soglia M";
                case AlarmType.PercentageInBuffer:
                    return "Soglia %";
                default:
                    return "Soglia";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("Converter usato solo in lettura.");
    }

    public class BoolToEnabledTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool enabled && enabled ? "Abilitato" : "Disabilitato";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("Converter usato solo in lettura.");
    }

    public class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = Brushes.Red;
        public Brush FalseBrush { get; set; } = Brushes.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool enabled && enabled ? TrueBrush : FalseBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("Converter usato solo in lettura.");
    }

    public class BoolToDbSourceTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool useDatabase && useDatabase
                ? "Sorgente: Database MySQL"
                : "Sorgente: File XML (Backup)";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("Converter usato solo in lettura.");
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool enabled && enabled
                ? new SolidColorBrush(Colors.Green)
                : new SolidColorBrush(Colors.Red);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("Converter usato solo in lettura.");
    }
}
