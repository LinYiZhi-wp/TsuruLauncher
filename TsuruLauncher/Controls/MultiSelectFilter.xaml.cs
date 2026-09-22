using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Models;
using TsuruLauncher.Services.Animation;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 可多选的下拉筛选器（美西螈版本页那三颗 Platforms / Game versions / Project channels）。
    ///
    /// 用法：把 <see cref="Options"/> 绑到 ViewModel 里的 ObservableCollection&lt;FilterOption&gt;，
    /// 勾选变化时抛 <see cref="SelectionChanged"/>，ViewModel 收到后重新过滤即可 ——
    /// 控件本身不认识任何业务字段。
    /// </summary>
    public partial class MultiSelectFilter : UserControl
    {
        public MultiSelectFilter()
        {
            InitializeComponent();
            Loaded += (_, __) => RefreshBadge();
        }

        // ── Label ──────────────────────────────────────────────────────────
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(MultiSelectFilter),
                new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        // ── Options ────────────────────────────────────────────────────────
        public static readonly DependencyProperty OptionsProperty =
            DependencyProperty.Register(nameof(Options), typeof(IEnumerable), typeof(MultiSelectFilter),
                new PropertyMetadata(null, OnOptionsChanged));

        public IEnumerable? Options
        {
            get => (IEnumerable?)GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
        }

        // ── IsOpen ─────────────────────────────────────────────────────────
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(MultiSelectFilter),
                new PropertyMetadata(false, OnIsOpenChanged));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        // ── Footer（下拉底部的附加内容，可为空）──────────────────────────────
        public static readonly DependencyProperty FooterProperty =
            DependencyProperty.Register(nameof(Footer), typeof(object), typeof(MultiSelectFilter),
                new PropertyMetadata(null, OnFooterChanged));

        public object? Footer
        {
            get => GetValue(FooterProperty);
            set => SetValue(FooterProperty, value);
        }

        private static void OnFooterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (MultiSelectFilter)d;
            if (ctl.FooterWrap != null)
                ctl.FooterWrap.Visibility = e.NewValue != null ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>任意一项的勾选状态变了。</summary>
        public event EventHandler? SelectionChanged;

        // ── Searchable ─────────────────────────────────────────────────────
        /// <summary>是否显示搜索框。默认「候选超过 <see cref="SearchThreshold"/> 项时自动出现」。</summary>
        public static readonly DependencyProperty SearchableProperty =
            DependencyProperty.Register(nameof(Searchable), typeof(bool), typeof(MultiSelectFilter),
                new PropertyMetadata(false, OnSearchableChanged));

        public bool Searchable
        {
            get => (bool)GetValue(SearchableProperty);
            set => SetValue(SearchableProperty, value);
        }

        /// <summary>候选超过这个数量就自动加搜索框（避免每次都要显式设 Searchable）。</summary>
        public static readonly DependencyProperty SearchThresholdProperty =
            DependencyProperty.Register(nameof(SearchThreshold), typeof(int), typeof(MultiSelectFilter),
                new PropertyMetadata(12, OnSearchableChanged));

        public int SearchThreshold
        {
            get => (int)GetValue(SearchThresholdProperty);
            set => SetValue(SearchThresholdProperty, value);
        }

        private static void OnSearchableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((MultiSelectFilter)d).RefreshSearchVisibility();

        private void RefreshSearchVisibility()
        {
            if (SearchWrap == null) return;
            int count = Options == null ? 0 : Options.Cast<object>().Count();
            bool show = Searchable || count > SearchThreshold;
            SearchWrap.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchHint != null)
                SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;

            _view?.Refresh();
        }

        private bool MatchSearch(object item)
        {
            string q = SearchBox?.Text?.Trim() ?? string.Empty;
            if (q.Length == 0) return true;
            if (item is not FilterOption fo) return true;
            return (fo.Label ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)
                || (fo.Value ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>清掉所有勾选（外部「清除全部筛选」用）。</summary>
        public void ClearSelection()
        {
            if (Options == null) return;
            bool changed = false;
            foreach (var o in Options)
            {
                if (o is FilterOption fo && fo.IsSelected)
                {
                    fo.IsSelected = false;
                    changed = true;
                }
            }
            if (changed) { RefreshBadge(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
        }

        // ── 内部 ───────────────────────────────────────────────────────────
        /// <summary>内部：搜索用的视图（给 ItemsControl 提供过滤后的候选）。</summary>
        private ICollectionView? _view;

        private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (MultiSelectFilter)d;

            if (e.OldValue is INotifyCollectionChanged oldColl)
                oldColl.CollectionChanged -= ctl.OnCollectionChanged;
            if (e.OldValue is IEnumerable oldItems)
                foreach (var o in oldItems) ctl.Unhook(o);

            if (e.NewValue is INotifyCollectionChanged newColl)
                newColl.CollectionChanged += ctl.OnCollectionChanged;
            if (e.NewValue is IEnumerable newItems)
                foreach (var o in newItems) ctl.Hook(o);

            ctl.RebuildView(e.NewValue as IEnumerable);
            ctl.RefreshBadge();
            ctl.RefreshSearchVisibility();
        }

        /// <summary>给 ItemsControl 换一个新的（带搜索过滤的）视图，不污染原集合的默认视图。</summary>
        private void RebuildView(IEnumerable? source)
        {
            if (OptionList == null) return;

            if (source is System.Collections.IList list)
            {
                _view = new ListCollectionView(list) { Filter = MatchSearch };
                OptionList.ItemsSource = _view;
            }
            else
            {
                _view = null;
                OptionList.ItemsSource = source;
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (var o in e.OldItems) Unhook(o);
            if (e.NewItems != null)
                foreach (var o in e.NewItems) Hook(o);
            RefreshBadge();
            RefreshSearchVisibility();
        }

        private void Hook(object? item)
        {
            if (item is FilterOption fo) fo.PropertyChanged += OnOptionPropertyChanged;
        }

        private void Unhook(object? item)
        {
            if (item is FilterOption fo) fo.PropertyChanged -= OnOptionPropertyChanged;
        }

        private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FilterOption.IsSelected)) return;
            RefreshBadge();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>角标数字 + 空状态提示。</summary>
        private void RefreshBadge()
        {
            int n = 0;
            int total = 0;
            if (Options != null)
            {
                foreach (var o in Options)
                {
                    total++;
                    if (o is FilterOption fo && fo.IsSelected) n++;
                }
            }

            if (CountBadge != null)
            {
                CountText.Text = n.ToString();
                CountBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (EmptyHint != null)
                EmptyHint.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (MultiSelectFilter)d;
            ctl.AnimateChevron((bool)e.NewValue);

            // 关掉下拉时把搜索词清掉，下次打开是完整列表（否则会莫名只剩几项）
            if (!(bool)e.NewValue && ctl.SearchBox != null)
                ctl.SearchBox.Text = string.Empty;
        }

        /// <summary>箭头翻转（Axolotl：<c>duration-150</c>，打开时 rotate-90 的方向）。</summary>
        private void AnimateChevron(bool open)
        {
            if (Chevron?.RenderTransform is not RotateTransform rt) return;
            var anim = new DoubleAnimation(open ? 180 : 0, AxolotlMotion.Ms(180))
            {
                EasingFunction = AxolotlMotion.EaseOut
            };
            rt.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
    }
}
