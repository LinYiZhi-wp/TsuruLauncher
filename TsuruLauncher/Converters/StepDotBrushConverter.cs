using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TsuruLauncher.Converters
{
    /// <summary>
    /// 安装步骤指示点：进度达到 ConverterParameter（0~1）时点亮成强调色，否则是中性底色。
    /// 用于「准备 → 版本 JSON → 运行库 → 资源文件 → 加载器」这种步骤条。
    /// </summary>
    public class StepDotBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double progress = 0;
            if (value is double d) progress = d;
            else if (value is float f) progress = f;
            else if (value is int i) progress = i;

            double threshold = 0;
            if (parameter is string s) double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out threshold);
            else if (parameter is double pd) threshold = pd;

            bool done = progress >= threshold - 0.0001;
            string key = done ? "AccentBrush" : "Surface5Brush";
            var brush = Application.Current == null ? null : Application.Current.TryFindResource(key) as Brush;
            return brush ?? (done ? Brushes.MediumSeaGreen : Brushes.Gainsboro);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
