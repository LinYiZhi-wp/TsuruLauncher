using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace TsuruLauncher.Models
{
    /// <summary>
    /// 一个可勾选的筛选项。
    ///
    /// 美西螈的版本筛选是三个 MultiSelect（Platforms / Game versions / Project channels），
    /// 下拉里每行一个 checkbox。WPF 没有现成的 MultiSelect，所以这里用
    /// <see cref="Controls.MultiSelectFilter"/> + 这个模型拼出来：
    /// 控件只负责把 <see cref="IsSelected"/> 勾上/取消，筛选逻辑全在 ViewModel 里。
    /// </summary>
    public partial class FilterOption : ObservableObject
    {
        /// <summary>原始值（用来跟数据比对，比如 "fabric" / "1.21.4" / "beta"）。</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>显示名（可以跟 Value 不同，比如 "1.21.4" → "1.21.4"，"neoforge" → "NeoForge"）。</summary>
        private string _label = string.Empty;
        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public FilterOption() { }

        public FilterOption(string value, string? label = null, bool isSelected = false)
        {
            Value = value;
            Label = label ?? value;
            IsSelected = isSelected;
        }
    }

    /// <summary>
    /// 已生效的筛选条件（美西螈筛选下拉下面那一排可点 × 删掉的胶囊）。
    /// 点 × 走 <see cref="RemoveCommand"/>，由 ViewModel 把对应 FilterOption 取消勾选。
    /// </summary>
    public class ActiveFilterChip
    {
        public string Label { get; set; } = string.Empty;

        /// <summary>platform / gameVersion / channel —— 决定胶囊配色。</summary>
        public string Group { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public ICommand? RemoveCommand { get; set; }
    }
}
