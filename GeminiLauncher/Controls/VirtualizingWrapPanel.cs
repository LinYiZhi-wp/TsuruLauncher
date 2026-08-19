using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GeminiLauncher.Controls
{
    /// <summary>
    /// A wrap panel that virtualizes items: only the containers that fall inside the
    /// visible viewport are generated/measured/arranged, so large collections
    /// (e.g. thousands of mods) stay responsive.
    /// </summary>
    public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        private double _extentWidth;
        private double _extentHeight;
        private double _viewportWidth;
        private double _viewportHeight;
        private double _offsetX;
        private double _offsetY;
        private double _rowHeight = 250;

        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(220.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        private int Columns => _viewportWidth > 0 && ItemWidth > 0 ? Math.Max(1, (int)Math.Floor(_viewportWidth / ItemWidth)) : 1;

        private int ItemCount => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

        protected override Size MeasureOverride(Size availableSize)
        {
            _viewportWidth = availableSize.Width;
            _viewportHeight = availableSize.Height;

            var generator = ItemContainerGenerator;
            int count = ItemCount;
            int cols = Columns;
            double itemWidth = Math.Max(1, ItemWidth);

            if (count == 0 || generator == null)
            {
                RemoveAllChildren();
                _extentWidth = _extentHeight = 0;
                return new Size(0, 0);
            }

            double rh = double.IsNaN(ItemHeight) ? _rowHeight : ItemHeight;

            // Two passes: the first pass measures with the previous row height,
            // the second refines the window with the measured row height.
            for (int pass = 0; pass < 2; pass++)
            {
                int rows = (count + cols - 1) / cols;
                _extentWidth = cols * itemWidth;
                _extentHeight = rows * rh;

                if (_offsetY > _extentHeight - _viewportHeight)
                    _offsetY = Math.Max(0, _extentHeight - _viewportHeight);
                if (_offsetY < 0) _offsetY = 0;

                int firstRow = _viewportHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(_offsetY / Math.Max(1, rh)));
                int visibleRows = _viewportHeight <= 0 ? 1 : (int)Math.Ceiling(_viewportHeight / Math.Max(1, rh)) + 1;
                int firstIndex = Math.Min(count - 1, firstRow * cols);
                int lastIndex = Math.Min(count - 1, (firstRow + visibleRows) * cols + cols - 1);

                RealizeRange(firstIndex, lastIndex, itemWidth, generator);

                if (!double.IsNaN(ItemHeight)) break;

                double maxRowHeight = 0;
                for (int i = 0; i < InternalChildren.Count; i++)
                    maxRowHeight = Math.Max(maxRowHeight, InternalChildren[i].DesiredSize.Height);
                if (maxRowHeight <= 0 || Math.Abs(maxRowHeight - rh) < 1) break;
                rh = maxRowHeight;
                _rowHeight = maxRowHeight;
            }

            return new Size(Math.Min(_extentWidth, availableSize.Width), Math.Min(_extentHeight, availableSize.Height));
        }

        /// <summary>
        /// Keeps exactly the containers [firstIndex..lastIndex] realized, in item order.
        /// </summary>
        private void RealizeRange(int firstIndex, int lastIndex, double itemWidth, IItemContainerGenerator generator)
        {
            // Remove realized containers that left the window
            for (int i = InternalChildren.Count - 1; i >= 0; i--)
            {
                int itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                if (itemIndex < firstIndex || itemIndex > lastIndex)
                {
                    generator.Remove(new GeneratorPosition(i, 0), 1);
                    RemoveInternalChildRange(i, 1);
                }
            }

            // Realize missing containers in ascending item order, inserting them at the
            // correct position so InternalChildren stays sorted (required for the
            // GeneratorPosition mapping above to stay valid).
            var startPos = generator.GeneratorPositionFromIndex(firstIndex);
            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++)
                {
                    bool isNew = false;
                    var child = generator.GenerateNext(out isNew) as UIElement;
                    if (child == null) continue;

                    if (isNew)
                    {
                        int insertAt = 0;
                        while (insertAt < InternalChildren.Count &&
                               generator.IndexFromGeneratorPosition(new GeneratorPosition(insertAt, 0)) < itemIndex)
                            insertAt++;
                        InsertInternalChild(insertAt, child);
                        generator.PrepareItemContainer(child);
                    }

                    child.Measure(new Size(itemWidth, double.PositiveInfinity));
                }
            }
        }

        private void RemoveAllChildren()
        {
            for (int i = InternalChildren.Count - 1; i >= 0; i--)
                RemoveInternalChildRange(i, 1);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var generator = ItemContainerGenerator;
            int cols = Math.Max(1, Columns);
            double rh = Math.Max(1, _rowHeight);

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                int index = generator != null
                    ? generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0))
                    : i;
                int row = index / cols;
                int col = index % cols;
                double x = col * ItemWidth;
                double y = row * rh - _offsetY;
                child.Arrange(new Rect(x, y, ItemWidth, child.DesiredSize.Height));
            }

            return finalSize;
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }

        #region IScrollInfo

        public ScrollViewer? ScrollOwner { get; set; }
        public bool CanHorizontallyScroll { get; set; } = false;
        public bool CanVerticallyScroll { get; set; } = true;
        public double ExtentWidth => _extentWidth;
        public double ExtentHeight => _extentHeight;
        public double ViewportWidth => _viewportWidth;
        public double ViewportHeight => _viewportHeight;
        public double HorizontalOffset => _offsetX;
        public double VerticalOffset => _offsetY;

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            var owner = ItemsControl.GetItemsOwner(this);
            int index = -1;
            if (owner?.ItemContainerGenerator != null && visual is DependencyObject container)
                index = owner.ItemContainerGenerator.IndexFromContainer(container);
            if (index >= 0)
            {
                int cols = Math.Max(1, Columns);
                double rh = Math.Max(1, _rowHeight);
                int row = index / cols;
                double targetY = row * rh;
                if (targetY < _offsetY)
                    SetVerticalOffset(targetY);
                else if (targetY + rh > _offsetY + _viewportHeight)
                    SetVerticalOffset(targetY + rh - _viewportHeight);
                return new Rect((index % cols) * ItemWidth, targetY - _offsetY, ItemWidth, rh);
            }
            return rectangle;
        }

        public void LineUp() => SetVerticalOffset(_offsetY - 30);
        public void LineDown() => SetVerticalOffset(_offsetY + 30);
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() => SetVerticalOffset(_offsetY - _viewportHeight);
        public void PageDown() => SetVerticalOffset(_offsetY + _viewportHeight);
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelUp() => SetVerticalOffset(_offsetY - 60);
        public void MouseWheelDown() => SetVerticalOffset(_offsetY + 60);
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }

        public void SetHorizontalOffset(double offset)
        {
            _offsetX = Math.Max(0, Math.Min(offset, _extentWidth - _viewportWidth));
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }

        public void SetVerticalOffset(double offset)
        {
            _offsetY = Math.Max(0, Math.Min(offset, _extentHeight - _viewportHeight));
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }

        #endregion
    }
}
