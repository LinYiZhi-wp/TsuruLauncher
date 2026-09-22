using System;
using System.Globalization;
using System.Windows.Data;

namespace TsuruLauncher.Converters
{
    public class StringsMatchConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return false;
            return values[0]?.ToString() == values[1]?.ToString();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}