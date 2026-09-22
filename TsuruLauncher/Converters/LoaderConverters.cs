using System;
using System.Globalization;
using System.Windows.Data;

namespace TsuruLauncher.Converters
{
    public class ExpandIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool expanded)
                return expanded ? "▼" : "▶";
            return "▶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class BoolToAngleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool expanded && expanded)
                return 180.0;
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}