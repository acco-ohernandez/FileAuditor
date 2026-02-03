using System.Globalization;
using System.Windows.Data;

namespace FileAuditor.WPF.Converters
{
    public class BytesToSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                const long KB = 1024;
                const long MB = KB * 1024;
                const long GB = MB * 1024;
                const long TB = GB * 1024;

                if (bytes >= TB)
                    return $"{bytes / (double)TB:F2} TB";
                if (bytes >= GB)
                    return $"{bytes / (double)GB:F2} GB";
                if (bytes >= MB)
                    return $"{bytes / (double)MB:F2} MB";
                if (bytes >= KB)
                    return $"{bytes / (double)KB:F2} KB";

                return $"{bytes} bytes";
            }
            return "0 bytes";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}