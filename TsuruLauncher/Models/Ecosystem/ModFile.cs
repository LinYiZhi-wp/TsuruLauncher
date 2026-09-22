using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TsuruLauncher.Models.Ecosystem
{
    public class ModFile : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public Dictionary<string, string> Hashes { get; set; } = new Dictionary<string, string>();
        public List<string> Loaders { get; set; } = new List<string>();
        public List<string> GameVersions { get; set; } = new List<string>();
        public long Size { get; set; }

        /// <summary>该版本的下载量（Axolotl 版本表格的一列）。Modrinth 返回 downloads，CurseForge 没有。</summary>
        public long Downloads { get; set; }

        /// <summary>下载量的紧凑显示（228.1M / 35.4K）。</summary>
        public string DownloadsText => Downloads <= 0 ? "—"
            : Downloads >= 1_000_000 ? (Downloads / 1_000_000.0).ToString("0.#") + "M"
            : Downloads >= 1_000 ? (Downloads / 1_000.0).ToString("0.#") + "K"
            : Downloads.ToString();
        public string ReleaseDate { get; set; } = string.Empty;
        public List<ModDependency> Dependencies { get; set; } = new List<ModDependency>();

        /// <summary>
        /// 「发布时间」列显示用的相对时间（美西螈显示的是 <c>11小时前</c> / <c>6天前</c>，
        /// 不是完整时间戳 —— 之前直接绑 <see cref="ReleaseDate"/>，一列
        /// <c>2026/9/15 15:57:23</c> 把其它列全挤扁了）。
        /// </summary>
        public string ReleaseDateText
        {
            get
            {
                if (!TryParseUtc(ReleaseDate, out var utc)) return ReleaseDate;
                var span = DateTime.UtcNow - utc;

                if (span.TotalSeconds < 0) return "刚刚";
                if (span.TotalMinutes < 1) return "刚刚";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}分钟前";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours}小时前";
                if (span.TotalDays < 7) return $"{(int)span.TotalDays}天前";
                if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}周前";
                if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}个月前";
                return $"{(int)(span.TotalDays / 365)}年前";
            }
        }

        private static bool TryParseUtc(string? s, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal, out utc);
        }

        // ── 游戏版本列的 chip（美西螈只铺前两个 + 「+N」）──────────────
        public List<string> GameVersionChips =>
            (GameVersions ?? new List<string>()).Take(2).ToList();

        public int ExtraGameVersionCount => Math.Max(0, (GameVersions?.Count ?? 0) - 2);
        public bool HasExtraGameVersions => ExtraGameVersionCount > 0;
        public string ExtraGameVersionText => "+" + ExtraGameVersionCount;

        /// <summary>平台列的 chip（同样只铺前两个，避免一列被 WrapPanel 撑成好几行）。</summary>
        public List<string> LoaderChips => (Loaders ?? new List<string>()).Take(2).ToList();

        /// <summary>
        /// 发布通道。Modrinth 的 <c>version_type</c>：release / beta / alpha；
        /// CurseForge 的 <c>releaseType</c>：1=release 2=beta 3=alpha（已在服务层归一化成同样的字符串）。
        /// Axolotl 版本表第一列那颗通道圆点 + 「Project channels」筛选都吃这个字段。
        /// </summary>
        public string VersionType { get; set; } = "release";

        /// <summary>通道中文标签（正式版 / 测试版 / 内测版）。</summary>
        public string ChannelLabel => VersionType switch
        {
            "beta" => "测试版",
            "alpha" => "内测版",
            _ => "正式版"
        };

        /// <summary>通道色 key（XAML 里映射到绿 / 橙 / 红）。</summary>
        public string ChannelColorKey => VersionType switch
        {
            "beta" => "Warning",
            "alpha" => "Danger",
            _ => "Success"
        };

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>版本依赖项。</summary>
    public class ModDependency
    {
        public string ProjectId { get; set; } = string.Empty;
        public string VersionId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

        /// <summary>Modrinth: required / optional / embedded / incompatible。</summary>
        public string DependencyType { get; set; } = "required";

        /// <summary>
        /// 列表里显示的标题。
        /// ⚠ Modrinth 对 <c>embedded</c> 类依赖返回 <c>file_name: null</c>（实测），
        /// 这时要退回 project_id —— 之前直接绑 <c>FileName</c>，界面上一整列全是空的/串了类型。
        /// </summary>
        public string DisplayName =>
            !string.IsNullOrWhiteSpace(FileName) ? FileName :
            !string.IsNullOrWhiteSpace(ProjectId) ? ProjectId :
            !string.IsNullOrWhiteSpace(VersionId) ? VersionId : "未知依赖";

        /// <summary>中文类型标签。</summary>
        public string TypeLabel => DependencyType switch
        {
            "required" => "必需",
            "optional" => "可选",
            "embedded" => "内置",
            "incompatible" => "不兼容",
            _ => DependencyType
        };

        /// <summary>徽标底色 key（XAML 里按类型上色，不再是清一色"必需"）。</summary>
        public bool IsRequired => DependencyType == "required";
        public bool IsIncompatible => DependencyType == "incompatible";
    }
}
