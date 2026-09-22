using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TsuruLauncher.Converters
{
    /// <summary>
    /// bool → HorizontalAlignment，两态都给值（不能返回 DoNothing，那样目标会落回默认 Stretch）。
    ///   * true  → ConverterParameter 指定的对齐
    ///   * false → Fallback 对齐（通过 ConverterParameter 之后的第二个参数传，或用 TrueAlignment/FalseAlignment 属性）
    /// 目前用于资源页侧栏顶部 chevron 折叠按钮：
    ///   * 折叠态（IsSidebarCollapsed=true）→ Center（用户要「显示在中间」）
    ///   * 展开态（false）→ Right（用户标红框的位置）
    /// </summary>
    public class BoolToHorizontalAlignmentConverter : IValueConverter
    {
        /// <summary>true 时用的对齐，默认 Center。</summary>
        public HorizontalAlignment TrueAlignment { get; set; } = HorizontalAlignment.Center;

        /// <summary>false 时用的对齐，默认 Right。</summary>
        public HorizontalAlignment FalseAlignment { get; set; } = HorizontalAlignment.Right;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 允许用 ConverterParameter 临时覆盖 true 分支（"Center" / "Right" / ...）
            var trueAlign = TrueAlignment;
            if (parameter is HorizontalAlignment ha) trueAlign = ha;
            else if (parameter is string s && Enum.TryParse<HorizontalAlignment>(s, true, out var parsed)) trueAlign = parsed;

            return value is bool b && b ? trueAlign : FalseAlignment;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
