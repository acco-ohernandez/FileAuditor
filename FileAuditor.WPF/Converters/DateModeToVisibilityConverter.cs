using System.Globalization;
using System.Windows;
using System.Windows.Data;

using FileAuditor.Core.Enums;

namespace FileAuditor.WPF.Converters
{
    public class DateModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CleanupDateMode dateMode && parameter is string param)
            {
                var modes = param.Split(',');
                foreach (var mode in modes)
                {
                    if (Enum.TryParse<CleanupDateMode>(mode.Trim(), out var targetMode))
                    {
                        if (dateMode == targetMode)
                            return Visibility.Visible;
                    }
                }
                return Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}