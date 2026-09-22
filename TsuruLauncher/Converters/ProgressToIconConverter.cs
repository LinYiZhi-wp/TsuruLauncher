using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace TsuruLauncher.Converters
{
    public class ProgressToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double progress)
            {
                if (progress >= 0.99)
                {
                    return new SymbolIcon { Symbol = SymbolRegular.Checkmark24, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DodgerBlue) };
                }
            }
            return new SymbolIcon { Symbol = SymbolRegular.Circle24, Opacity = 0.3 };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}