using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QtisVisionPanel.Converters
{

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }
    public class BooleanToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Enable : 
                ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Disable;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? "#FF9800" : "#4CAF50";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BusyStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b
                ? ServerMessagePersonalize.GetMessageOrDefault(
                    "Sub_entry_Inspection_busyStatus",
                    "Processing...")
                : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ImagePathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string imageName && !string.IsNullOrEmpty(imageName))
            {
                try
                {
                    // Icone vettoriali delle ispezioni: chiave "InspIcon_<Feature>" risolta dal
                    // ResourceDictionary Resources/InspectionIcons.xaml. Nitide a qualunque
                    // scala, una per ispezione. Se la chiave non esiste si prosegue con la
                    // risoluzione PNG storica, quindi la sostituzione puo' avvenire icona per
                    // icona senza rompere nulla.
                    if (imageName.StartsWith("InspIcon_", StringComparison.Ordinal))
                    {
                        object vectorIcon = System.Windows.Application.Current?.TryFindResource(imageName);
                        if (vectorIcon != null)
                        {
                            return vectorIcon;
                        }

                        return null;
                    }

                    if (Path.IsPathRooted(imageName) && File.Exists(imageName))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(imageName, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }

                    // Se è già un URI completo (caso dei file system)
                    if (imageName.StartsWith("file://") || imageName.StartsWith("pack://"))
                    {
                        return new BitmapImage(new Uri(imageName, UriKind.RelativeOrAbsolute));
                    }

                    // Altrimenti cerca nelle risorse del progetto
                    string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                    Uri uri = new Uri($"pack://application:,,,/{assemblyName};component/Resources/{imageName}", UriKind.Absolute);
                    return new BitmapImage(uri);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class BoolToYesNoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? "SI" : "NO";
            return "NO";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToDbIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? "✓ In Database" : "✗ Solo File";
            return "✗ Solo File";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class RecipeTypeAbbreviationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string recipeType)
            {
                if (recipeType.Contains("rotoli"))
                    return "Recipa...";
                else if (recipeType.Contains("pacchi"))
                    return "Recipa...";
                else if (recipeType.Contains("templato"))
                    return "Templa...";
                else
                    return recipeType.Length > 8 ? recipeType.Substring(0, 5) + "..." : recipeType;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MeasurementLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string recipeType)
            {
                // Per rotoli mostra "Diametro", per altri mostra "Larghezza"
                if (recipeType.Contains("rotoli") ||
                    recipeType.Contains("roll"))
                    return "Diametro";
                else
                    return "Larghezza";
            }
            return "Larghezza";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isInverted = parameter as string == "inverted";

            if (isInverted)
            {
                return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class RecipeStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecipeStatus status)
            {
                switch (status)
                {
                    case RecipeStatus.InProduction:
                        return new SolidColorBrush(Color.FromArgb(255, 227, 242, 253)); // Blue light
                    case RecipeStatus.Selected:
                        return new SolidColorBrush(Color.FromArgb(255, 255, 235, 238)); // Red light
                    default:
                        return new SolidColorBrush(Color.FromArgb(255, 245, 245, 245)); // Gray
                }
            }
            return new SolidColorBrush(Colors.LightGray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class RecipeStatusToBorderColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecipeStatus status)
            {
                switch (status)
                {
                    case RecipeStatus.InProduction:
                        return new SolidColorBrush(Color.FromArgb(255, 33, 150, 243)); // Blue
                    case RecipeStatus.Selected:
                        return new SolidColorBrush(Color.FromArgb(255, 244, 67, 54)); // Red
                    default:
                        return new SolidColorBrush(Color.FromArgb(255, 224, 224, 224)); // Gray
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class RecipeStatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecipeStatus status)
            {
                switch (status)
                {
                    case RecipeStatus.InProduction:
                        return "▶"; // Play icon
                    case RecipeStatus.Selected:
                        return "✓"; // Check icon
                    default:
                        return "";
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class RecipeStatusToIconColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecipeStatus status)
            {
                switch (status)
                {
                    case RecipeStatus.InProduction:
                        return new SolidColorBrush(Color.FromArgb(255, 33, 150, 243)); // Blue
                    case RecipeStatus.Selected:
                        return new SolidColorBrush(Color.FromArgb(255, 244, 67, 54)); // Red
                    default:
                        return new SolidColorBrush(Colors.Gray);
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RecipeImagePathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return null;

            string imagePath = value.ToString();

            // Se è già un percorso completo
            if (Path.IsPathRooted(imagePath) && File.Exists(imagePath))
            {
                // CARICA L'IMMAGINE IN MEMORIA senza bloccare il file
             
                try
                {
                    // CARICA L'IMMAGINE IN MEMORIA senza bloccare il file
                    byte[] imageData = File.ReadAllBytes(imagePath);

                    using (var stream = new MemoryStream(imageData))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Importante: rende l'immagine thread-safe e rilascia lo stream
                        return bitmap;
                    }
                }
                catch (Exception ex)
                {
                    // Immagine corrotta/illeggibile: la vista mostra il placeholder, ma non in silenzio.
                    System.Diagnostics.Debug.WriteLine($"RecipeImagePathConverter: caricamento immagine fallito '{imagePath}' — {ex.Message}");
                    return null;
                }
            }

            return null;
        }

     

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToIconVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecipeStatus status)
            {
                return status == RecipeStatus.Normal ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ProductionStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecipeStatus status)
            {
                switch (status)
                {
                    case RecipeStatus.InProduction:
                        return "✓ IN PRODUZIONE";
                    case RecipeStatus.Selected:
                        return "✓ SELEZIONATA";
                    default:
                        return "";
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RecipeTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string recipeType && parameter is string targetTypeParam)
            {
                if (targetTypeParam == "Other")
                {
                    return recipeType != "Ricetta rotoli" && recipeType != "Ricetta pacchi"
                        ? Visibility.Visible : Visibility.Collapsed;
                }

                return recipeType == targetTypeParam ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MachineTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string machineType && parameter is string expectedType)
            {
                string normalizedMachineType = machineType.Trim();
                string normalizedExpectedType = expectedType.Trim();

                if (normalizedExpectedType.Equals("Other", StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedMachineType.Equals("Packs", StringComparison.OrdinalIgnoreCase) ||
                           normalizedMachineType.Equals("Rolls", StringComparison.OrdinalIgnoreCase) ||
                           normalizedMachineType.Equals("3DCheck", StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }

                return normalizedMachineType.Equals(normalizedExpectedType, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BooleanToOpacityConverter : IValueConverter
    {
        public double TrueValue { get; set; } = 1.0;
        public double FalseValue { get; set; } = 0.5;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }
            return TrueValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MachinestatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TopMenuBarViewModel.MachineStatus status)
            {
                switch (status)
                {
                    case TopMenuBarViewModel.MachineStatus.Connecting:
                        return "Refresh";
                    case TopMenuBarViewModel.MachineStatus.Running:
                        return "RotateRight";
                    case TopMenuBarViewModel.MachineStatus.Stopped:
                        return "Stop";
                    case TopMenuBarViewModel.MachineStatus.Error:
                        return "ExclamationTriangle";
                    case TopMenuBarViewModel.MachineStatus.Maintenance:
                        return "Wrench";
                    default:
                        return "Question";
                }
               
            }
            return "Question";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MachinestatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TopMenuBarViewModel.MachineStatus status)
            {
                switch(status)
                {
                    case TopMenuBarViewModel.MachineStatus.Connecting:
                        return Brushes.Orange; // Yellow
                    case TopMenuBarViewModel.MachineStatus.Running:
                        return Brushes.LimeGreen; // Green
                    case TopMenuBarViewModel.MachineStatus.Stopped:
                        return Brushes.Gray; // Gray
                    case TopMenuBarViewModel.MachineStatus.Error:
                        return Brushes.Red; // Red
                    case TopMenuBarViewModel.MachineStatus.Maintenance:
                        return Brushes.Yellow; // Orange
                    default:
                        return Brushes.White; // White
                }
               
            }
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AlarmLevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AlarmLevel level)
            {
                switch(level)
                {
                    case AlarmLevel.Info:
                        return Brushes.Blue; // Blue
                    case AlarmLevel.Warning:
                        return Brushes.Orange; // Orange
                    case AlarmLevel.Error:
                        return Brushes.Red; // Red
                    case AlarmLevel.Critical:
                        return Brushes.DarkRed; // Dark Red
                    default:
                        return Brushes.Gray; // Gray
                }
                
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AlarmLevelToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AlarmLevel level)
            {
                switch(level)
                {
                    case AlarmLevel.Info:
                        return "InfoCircle";
                    case AlarmLevel.Warning:
                        return "ExclamationTriangle";
                    case AlarmLevel.Error:
                        return "ExclamationCircle";
                    case AlarmLevel.Critical:
                        return "Ban";
                    default:
                        return "Bell";
                }
               
            }
            return "Bell";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BooleanToVisibilityInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }


    public class MachineStatusToSpinBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TopMenuBarViewModel.MachineStatus status)
                return status == TopMenuBarViewModel.MachineStatus.Connecting
                    || status == TopMenuBarViewModel.MachineStatus.Running;

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Brushes.LimeGreen : Brushes.Red;
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class BooleanToAlarmStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isTriggered)
            {
                return isTriggered ? "TRIGGERED" : "Normal";
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }


    public class ListContainsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] == null || values[1] == null)
                return false;

            var list = values[0] as System.Collections.IEnumerable;
            var item = values[1];

            if (list != null)
            {
                foreach (var obj in list)
                {
                    if (Equals(obj, item))
                        return true;
                }
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                return value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AlarmTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AlarmType alarmType)
            {
                switch (alarmType)
                {
                    case AlarmType.ConsecutiveEvents:
                        return "Fires after N consecutive events";
                    case AlarmType.ConsecutiveEventsAlt:
                        return "Fires after M consecutive events";
                    case AlarmType.PercentageInBuffer:
                        return "Fires when percentage exceeds threshold";
                    default:
                        return alarmType.ToString();
                }
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                switch (stringValue)
                {
                    case "Consecutive Events (N)":
                        return AlarmType.ConsecutiveEvents;
                    case "Consecutive Events (M)":
                        return AlarmType.ConsecutiveEventsAlt;
                    case "Percentage in Buffer":
                        return AlarmType.PercentageInBuffer;
                    default:
                        return AlarmType.ConsecutiveEvents;
                }
            }
            return AlarmType.ConsecutiveEvents;
        }
    }
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PercentageToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percentage && parameter is string maxWidthStr && double.TryParse(maxWidthStr, out double maxWidth))
            {
                // Calcola la larghezza basata sulla percentuale e sulla larghezza massima
                return percentage * maxWidth / 100.0;
            }

            if (value is double percentage2)
            {
                // Default: restituisce la percentuale come moltiplicatore (es. 50% -> 0.5)
                return percentage2 / 100.0;
            }

            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MultiPercentageToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return 0.0;

            // Primo valore: percentuale (double)
            // Secondo valore: larghezza del contenitore (double)
            if (values[0] is double percentage && values[1] is double containerWidth)
            {
                // Calcola la larghezza in base alla percentuale
                double width = (percentage / 100.0) * containerWidth;
                return Math.Max(0, Math.Min(width, containerWidth)); // Limita alla larghezza del contenitore
            }

            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleLessThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null)
            {
                return false;
            }

            double threshold;
            if (!double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out threshold) &&
                !double.TryParse(parameter.ToString(), NumberStyles.Any, culture, out threshold))
            {
                return false;
            }

            double currentValue;
            if (value is double doubleValue)
            {
                currentValue = doubleValue;
            }
            else if (value is int intValue)
            {
                currentValue = intValue;
            }
            else if (value is float floatValue)
            {
                currentValue = floatValue;
            }
            else if (value is string stringValue &&
                     (double.TryParse(stringValue, NumberStyles.Any, culture, out currentValue) ||
                      double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out currentValue)))
            {
                return currentValue < threshold;
            }
            else
            {
                return false;
            }

            return currentValue < threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return false;
        }
    }

    public class IntToVisibilityConverters : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                bool hasItems = count > 0;
                bool inverse = parameter?.ToString()?.ToLower() == "inverse";
                if (inverse)
                    //    return count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    //else
                    //    return count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    return hasItems ? Visibility.Collapsed : Visibility.Visible;
                else
                    return hasItems ? Visibility.Visible : Visibility.Collapsed;

            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
