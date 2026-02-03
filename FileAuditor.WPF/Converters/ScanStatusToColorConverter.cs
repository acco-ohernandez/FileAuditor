using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using FileAuditor.Core.Enums;

namespace FileAuditor.WPF.Converters
{
    public class ScanStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ScanStatus status)
            {
                return status switch
                {
                    ScanStatus.Pending => new SolidColorBrush(Colors.Gray),
                    ScanStatus.Running => new SolidColorBrush(Colors.Blue),
                    ScanStatus.Completed => new SolidColorBrush(Colors.Green),
                    ScanStatus.Failed => new SolidColorBrush(Colors.Red),
                    ScanStatus.Cancelled => new SolidColorBrush(Colors.Orange),
                    _ => new SolidColorBrush(Colors.Black)
                };
            }
            return new SolidColorBrush(Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}