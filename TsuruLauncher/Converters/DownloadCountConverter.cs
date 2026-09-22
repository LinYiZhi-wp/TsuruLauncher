using System;
using System.Globalization;
using System.Windows.Data;

namespace TsuruLauncher.Converters
{
    public class DownloadCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long count)
            {
                if (count >= 1_000_000_000)
                    return $"📥 {(double)count / 1_000_000_000:F1}B";
                if (count >= 1_000_000)
                    return $"📥 {(double)count / 1_000_000:F1}M";
                if (count >= 1_000)
                    return $"📥 {(double)count / 1_000:F1}K";
                return $"📥 {count}";
            }
            return "📥 0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}