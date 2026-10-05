using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace QtisVisionPanel.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Se viene passato un parametro, inverti la logica (es: True = Collapsed)
                if (parameter != null && parameter.ToString().ToLower() == "inverse")
                {
                    return boolValue ? Visibility.Collapsed : Visibility.Visible;
                }

                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                if (parameter != null && parameter.ToString().ToLower() == "inverse")
                {
                    return visibility != Visibility.Visible;
                }

                return visibility == Visibility.Visible;
            }
            return false;
        }
    }
}

