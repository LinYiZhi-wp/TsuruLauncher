using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TsuruLauncher.Converters
{
    /// <summary>
    /// 元素宽度小于阈值 → <see cref="Visibility.Collapsed"/>，否则 Visible。
    ///
    /// 用途：网格卡片在「模组 → 5 列」这种窄格子下，底部一行塞不下
    /// 「下载量 + 来源徽标 + 安装按钮」，来源徽标属于次要信息，窄了就藏掉。
    ///
    /// 用法：
    ///   Visibility="{Binding ActualWidth, ElementName=CardRoot,
    ///                Converter={StaticResource NarrowToCollapsed}, ConverterParameter=190}"
    /// 阈值取 ConverterParameter；不传则用 <see cref="DefaultThreshold"/>。
    /// </summary>
    public class NarrowToCollapsedConverter : IValueConverter
    {
        public double DefaultThreshold { get; set; } = 190;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double threshold = DefaultThreshold;
            if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                threshold = p;
            else if (parameter is double d)
                threshold = d;

            // 还没量出宽度时（NaN / 0）先当「不窄」，避免首帧闪一下
            if (value is not double w || double.IsNaN(w) || w <= 0)
                return Visibility.Visible;

            return w < threshold ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
