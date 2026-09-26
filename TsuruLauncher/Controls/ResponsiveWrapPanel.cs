using System;
using System.Windows;
using System.Windows.Controls;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 按「目标卡片宽度」自动决定列数、并把可用宽度**铺满**的换行面板。
    ///
    /// 解决两个问题：
    ///   ① 原来的 <see cref="WrapPanel"/> 写死 <c>ItemWidth="230"</c> —— 窗口一宽，
    ///      右侧就空出一大块（用户截图里那条大空白）。
    ///   ② 不同分类的卡片信息量差别很大（模组卡片小、整合包封面大），
    ///      统一宽度不好看。现在每列的实际宽度 = (可用宽度 - 间距总和) / 列数，
    ///      列数由 <see cref="TargetItemWidth"/> 反推，所以卡片会自动撑满、不留缝。
    ///
    /// 列数公式：<c>cols = floor((available + gap) / (target + gap))</c>，
    /// 再夹到 [<see cref="MinColumns"/>, <see cref="MaxColumns"/>]。
    /// 例：可用 680、间距 10、目标 132 → (690)/(142) = 4.86 → 4 列，每列宽 162.5。
    /// 想要 5 列就把目标调小（见 ResourcesViewModel.GridTargetItemWidth）。
    ///
    /// 同一行内所有卡片高度取该行最大值，所以长短不一的简介不会把网格打乱。
    /// </summary>
    public class ResponsiveWrapPanel : Panel
    {
        /// <summary>目标卡片宽度（决定列数）。越小列越多。</summary>
        public static readonly DependencyProperty TargetItemWidthProperty =
            DependencyProperty.Register(nameof(TargetItemWidth), typeof(double), typeof(ResponsiveWrapPanel),
                new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double TargetItemWidth
        {
            get => (double)GetValue(TargetItemWidthProperty);
            set => SetValue(TargetItemWidthProperty, value);
        }

        /// <summary>卡片之间的水平 / 垂直间距。</summary>
        public static readonly DependencyProperty ItemGapProperty =
            DependencyProperty.Register(nameof(ItemGap), typeof(double), typeof(ResponsiveWrapPanel),
                new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double ItemGap
        {
            get => (double)GetValue(ItemGapProperty);
            set => SetValue(ItemGapProperty, value);
        }

        public static readonly DependencyProperty MinColumnsProperty =
            DependencyProperty.Register(nameof(MinColumns), typeof(int), typeof(ResponsiveWrapPanel),
                new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int MinColumns
        {
            get => (int)GetValue(MinColumnsProperty);
            set => SetValue(MinColumnsProperty, value);
        }

        public static readonly DependencyProperty MaxColumnsProperty =
            DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(ResponsiveWrapPanel),
                new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int MaxColumns
        {
            get => (int)GetValue(MaxColumnsProperty);
            set => SetValue(MaxColumnsProperty, value);
        }

        /// <summary>当前实际列数（自检 / 调试可读）。</summary>
        public int ActualColumns { get; private set; } = 1;

        /// <summary>当前实际每列宽度。</summary>
        public double ActualItemWidth { get; private set; }

        private int ComputeColumns(double available)
        {
            if (available <= 0 || double.IsInfinity(available)) available = TargetItemWidth;

            double target = TargetItemWidth > 1 ? TargetItemWidth : 200;
            double gap = Math.Max(0, ItemGap);

            int cols = (int)Math.Floor((available + gap) / (target + gap));
            return Math.Max(Math.Max(1, MinColumns), Math.Min(Math.Max(1, MaxColumns), cols));
        }

        private double ComputeItemWidth(double available, int cols)
        {
            double gap = Math.Max(0, ItemGap);
            double w = (available - (cols - 1) * gap) / cols;
            return w > 1 ? w : 1;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int n = InternalChildren.Count;
            if (n == 0)
            {
                ActualColumns = ComputeColumns(availableSize.Width);
                ActualItemWidth = ComputeItemWidth(
                    double.IsInfinity(availableSize.Width) ? TargetItemWidth : availableSize.Width, ActualColumns);
                return new Size(0, 0);
            }

            double avail = double.IsInfinity(availableSize.Width) ? TargetItemWidth * n : availableSize.Width;
            ActualColumns = ComputeColumns(avail);
            ActualItemWidth = ComputeItemWidth(avail, ActualColumns);

            double totalH = 0;
            double rowMax = 0;
            for (int i = 0; i < n; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(ActualItemWidth, double.PositiveInfinity));
                rowMax = Math.Max(rowMax, child.DesiredSize.Height);

                bool rowEnd = (i + 1) % ActualColumns == 0 || i == n - 1;
                if (rowEnd)
                {
                    totalH += rowMax + ItemGap;
                    rowMax = 0;
                }
            }
            totalH -= ItemGap;   // 最后一行的间距不算

            return new Size(avail, Math.Max(0, totalH));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int n = InternalChildren.Count;
            if (n == 0) return finalSize;

            int cols = ComputeColumns(finalSize.Width);
            double itemW = ComputeItemWidth(finalSize.Width, cols);
            ActualColumns = cols;
            ActualItemWidth = itemW;

            // 先按行分组：同一行取最大高度，保证网格对齐
            double y = 0;
            for (int start = 0; start < n; start += cols)
            {
                int end = Math.Min(start + cols, n);

                double rowH = 0;
                for (int i = start; i < end; i++)
                    rowH = Math.Max(rowH, InternalChildren[i].DesiredSize.Height);

                for (int i = start; i < end; i++)
                {
                    int col = i - start;
                    InternalChildren[i].Arrange(new Rect(col * (itemW + ItemGap), y, itemW, rowH));
                }

                y += rowH + ItemGap;
            }

            return finalSize;
        }
    }
}
