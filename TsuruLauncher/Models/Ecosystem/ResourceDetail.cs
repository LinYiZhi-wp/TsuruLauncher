using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TsuruLauncher.Models.Ecosystem
{
    public enum DownloadSource
    {
        Official,
        Modrinth,
        BMCLAPI,
        FastMirror
    }

    /// <summary>
    /// 图库里的一张图。
    /// ⚠ 不能直接把 URL 字符串绑到 <c>Image.Source</c> —— WPF 会**在 UI 线程同步下载**，
    /// 结果就是部分图片卡住不显示（实测 4 张里有 2 张是空白）而且会拖慢整个页面。
    /// 改成「URL + 异步加载好的位图」两段式，跟图标 / 头像走同一套 ImageCache。
    /// </summary>
    public class GalleryImage : ObservableObject
    {
        public string Url { get; set; } = string.Empty;

        private System.Windows.Media.Imaging.BitmapImage? _image;
        public System.Windows.Media.Imaging.BitmapImage? Image
        {
            get => _image;
            set { if (SetProperty(ref _image, value)) OnPropertyChanged(nameof(HasImage)); }
        }

        public bool HasImage => _image != null;
    }

    /// <summary>作者条目（Axolotl 右栏「作者」那一块：头像 + 名字 + 角色）。</summary>
    public class ResourceAuthor : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsOrganization { get; set; }

        private System.Windows.Media.Imaging.BitmapImage? _avatarImage;
        /// <summary>头像。**必须能通知** —— 它是异步加载回来后才有值的，普通属性绑定不会刷新。</summary>
        public System.Windows.Media.Imaging.BitmapImage? AvatarImage
        {
            get => _avatarImage;
            set { if (SetProperty(ref _avatarImage, value)) OnPropertyChanged(nameof(HasAvatar)); }
        }
        public bool HasAvatar => _avatarImage != null;

        /// <summary>没头像时的占位首字母。</summary>
        public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
    }

    public class ResourceDetail : ObservableObject
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _summary = string.Empty;
        private string _description = string.Empty;
        private string _iconUrl = string.Empty;
        private System.Windows.Media.Imaging.BitmapImage? _iconImage;
        private string _author = string.Empty;
        private long _downloads;
        private int _followers;
        private DateTime _dateCreated;
        private DateTime _dateModified;
        private string _license = string.Empty;
        private ProjectPlatform _platform;
        private ProjectType _type;
        private string _webUrl = string.Empty;
        private List<string> _categories = new();
        private List<string> _gameVersions = new();
        private List<string> _loaders = new();
        private List<ModFile> _versions = new();
        private List<GalleryImage> _galleryImages = new();
        private bool _isLoadingDetails;
        private bool _isDownloading;
        private double _downloadProgress;
        private string _downloadStatus = "Ready";
        private string _downloadSpeedText = "";
        private long _downloadedBytes;
        private long _totalBytes;
        private ModFile? _selectedVersion;
        private DownloadSource _preferredSource = DownloadSource.Modrinth;
        private bool _isClientSideOnly;
        private bool _isServerSideOnly;
        private List<ResourceAuthor> _authors = new();
        private string _sourceUrl = string.Empty;
        private string _issuesUrl = string.Empty;
        private string _wikiUrl = string.Empty;
        private string _discordUrl = string.Empty;
        private string _donationUrl = string.Empty;

        public string Id { get => _id; set => SetProperty(ref _id, value); }
        public string Name { get => _name; set { if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(Initial)); } }
        public string Summary { get => _summary; set => SetProperty(ref _summary, value); }
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        public string IconUrl { get => _iconUrl; set => SetProperty(ref _iconUrl, value); }
        public System.Windows.Media.Imaging.BitmapImage? IconImage
        {
            get => _iconImage;
            set { if (SetProperty(ref _iconImage, value)) OnPropertyChanged(nameof(HasIcon)); }
        }

        /// <summary>图标还没到位时占位用的首字母（否则 hero 左上角是一块空方块）。</summary>
        public string Initial => string.IsNullOrWhiteSpace(_name) ? "?" : _name.Substring(0, 1).ToUpperInvariant();

        public bool HasIcon => _iconImage != null;
        public string Author { get => _author; set => SetProperty(ref _author, value); }
        public long Downloads { get => _downloads; set => SetProperty(ref _downloads, value); }
        public int Followers { get => _followers; set => SetProperty(ref _followers, value); }
        public DateTime DateCreated { get => _dateCreated; set => SetProperty(ref _dateCreated, value); }
        public DateTime DateModified { get => _dateModified; set => SetProperty(ref _dateModified, value); }
        public string License { get => _license; set => SetProperty(ref _license, value); }
        public ProjectPlatform Platform { get => _platform; set => SetProperty(ref _platform, value); }
        public ProjectType Type { get => _type; set => SetProperty(ref _type, value); }
        public string WebUrl { get => _webUrl; set => SetProperty(ref _webUrl, value); }
        public List<string> Categories { get => _categories; set => SetProperty(ref _categories, value); }
        public List<string> GameVersions { get => _gameVersions; set => SetProperty(ref _gameVersions, value); }
        public List<string> Loaders { get => _loaders; set => SetProperty(ref _loaders, value); }
        public List<ModFile> Versions { get => _versions; set => SetProperty(ref _versions, value); }
        public List<GalleryImage> GalleryImages { get => _galleryImages; set => SetProperty(ref _galleryImages, value); }
        public bool IsLoadingDetails { get => _isLoadingDetails; set => SetProperty(ref _isLoadingDetails, value); }
        public bool IsDownloading { get => _isDownloading; set => SetProperty(ref _isDownloading, value); }
        public double DownloadProgress { get => _downloadProgress; set => SetProperty(ref _downloadProgress, value); }
        public string DownloadStatus { get => _downloadStatus; set => SetProperty(ref _downloadStatus, value); }
        public string DownloadSpeedText { get => _downloadSpeedText; set => SetProperty(ref _downloadSpeedText, value); }
        public long DownloadedBytes { get => _downloadedBytes; set => SetProperty(ref _downloadedBytes, value); }
        public long TotalBytes { get => _totalBytes; set => SetProperty(ref _totalBytes, value); }
        public ModFile? SelectedVersion { get => _selectedVersion; set => SetProperty(ref _selectedVersion, value); }
        public DownloadSource PreferredSource { get => _preferredSource; set => SetProperty(ref _preferredSource, value); }
        public bool IsClientSideOnly { get => _isClientSideOnly; set => SetProperty(ref _isClientSideOnly, value); }

        /// <summary>作者/团队（Axolotl 右栏「作者」区）。Modrinth 走 team members，CurseForge 走 authors。</summary>
        public List<ResourceAuthor> Authors
        {
            get => _authors;
            set { if (SetProperty(ref _authors, value)) OnPropertyChanged(nameof(HasAuthors)); }
        }

        // ── 相关链接（Axolotl 右栏「相关链接」区）──
        public string SourceUrl { get => _sourceUrl; set { if (SetProperty(ref _sourceUrl, value)) OnPropertyChanged(nameof(HasSource)); } }
        public string IssuesUrl { get => _issuesUrl; set { if (SetProperty(ref _issuesUrl, value)) OnPropertyChanged(nameof(HasIssues)); } }
        public string WikiUrl { get => _wikiUrl; set { if (SetProperty(ref _wikiUrl, value)) OnPropertyChanged(nameof(HasWiki)); } }
        public string DiscordUrl { get => _discordUrl; set { if (SetProperty(ref _discordUrl, value)) OnPropertyChanged(nameof(HasDiscord)); } }
        public string DonationUrl { get => _donationUrl; set { if (SetProperty(ref _donationUrl, value)) OnPropertyChanged(nameof(HasDonation)); } }

        public bool HasSource => !string.IsNullOrWhiteSpace(SourceUrl);
        public bool HasIssues => !string.IsNullOrWhiteSpace(IssuesUrl);
        public bool HasWiki => !string.IsNullOrWhiteSpace(WikiUrl);
        public bool HasDiscord => !string.IsNullOrWhiteSpace(DiscordUrl);
        public bool HasDonation => !string.IsNullOrWhiteSpace(DonationUrl);
        public bool HasAnyLink => HasSource || HasIssues || HasWiki || HasDiscord || HasDonation;
        public bool HasAuthors => Authors != null && Authors.Count > 0;

        /// <summary>
        /// 右栏「兼容性」里显示的游戏版本 chips。
        /// ⚠ 直接绑 <see cref="GameVersions"/> 会被淹掉 —— Fabric API 有 600+ 个版本
        /// （18w49a / 19w03a … 一路铺满整个右栏，实测）。Axolotl 是**归并 + 截断**：
        ///   * 快照（<c>24w14a</c> 这种）合成一个「快照」chip
        ///   * 补丁号归到次版本（<c>1.21.4</c> → <c>1.21.x</c>）
        ///   * 最多 12 个
        /// </summary>
        public List<string> GameVersionChips
        {
            get
            {
                var all = GameVersions ?? new List<string>();

                // ① 优先只列**正式版**（不含 -pre / -rc / 快照）；正式版够 6 个就不用预发布凑数
                var releases = all.Where(v => !string.IsNullOrWhiteSpace(v) && !v.Contains('-') && !IsSnapshot(v)).ToList();
                var source = releases.Count >= 6 ? releases : all;

                // ② 从**新到旧**（Modrinth 的 game_versions 是旧→新，直接取前 N 个会全是远古版本）
                var list = new List<string>();
                var seen = new HashSet<string>();
                for (int i = source.Count - 1; i >= 0; i--)
                {
                    string label = GroupGameVersion(source[i]);
                    if (string.IsNullOrEmpty(label) || !seen.Add(label)) continue;
                    list.Add(label);
                    if (list.Count >= 12) break;
                }
                return list;
            }
        }

        /// <summary>快照判定：<c>24w14a</c>，以及 <c>25w14craftmine</c> / <c>24w14potato</c> 这类愚人节版。</summary>
        private static bool IsSnapshot(string v)
            => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{2}w\d{2}");

        private static string GroupGameVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return string.Empty;
            if (IsSnapshot(v)) return "快照";
            var parts = v.Split('.');
            if (parts.Length >= 3 && int.TryParse(parts[0], out _)) return parts[0] + "." + parts[1] + ".x";
            return v;
        }
        public bool IsServerSideOnly { get => _isServerSideOnly; set => SetProperty(ref _isServerSideOnly, value); }

        public string DownloadsFormatted
        {
            get
            {
                if (_downloads >= 1_000_000) return $"{_downloads / 1_000_000.0:F1}M";
                if (_downloads >= 1_000) return $"{_downloads / 1000.0:F1}K";
                return _downloads.ToString();
            }
        }

        public string FollowersFormatted
        {
            get
            {
                if (_followers >= 1000) return $"{_followers / 1000.0:F1}K";
                return _followers.ToString();
            }
        }

        public string SizeDisplay
        {
            get
            {
                if (_totalBytes < 1024) return $"{_totalBytes} B";
                if (_totalBytes < 1024 * 1024) return $"{_totalBytes / 1024.0:F1} KB";
                return $"{_totalBytes / (1024.0 * 1024.0):F1} MB";
            }
        }

        public string DownloadProgressText => $"{(_downloadProgress * 100):F0}%";

        public string TimeRemainingDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(_downloadSpeedText) || _downloadProgress <= 0 || _downloadProgress >= 1.0) return "";

                var speedMatch = System.Text.RegularExpressions.Regex.Match(_downloadSpeedText, @"[\d.]+");
                if (!speedMatch.Success) return "";

                if (!double.TryParse(speedMatch.Value, out double speedVal)) return "";
                double speedMBs = speedVal < 100 ? speedVal / 1024.0 : speedVal / (1024.0 * 1024.0);
                if (_downloadSpeedText.Contains("KB")) speedMBs = speedVal / 1024.0;
                else if (_downloadSpeedText.Contains("MB")) speedMBs = speedVal;
                else if (_downloadSpeedText.Contains("B/s") && !_downloadSpeedText.Contains("K") && !_downloadSpeedText.Contains("M")) speedMBs = speedVal / (1024.0 * 1024.0);

                if (speedMBs <= 0) return "--:--";

                long remainingBytes = (long)(_totalBytes * (1 - _downloadProgress));
                double secondsRemaining = remainingBytes / (1024.0 * 1024.0) / speedMBs;

                if (secondsRemaining < 60) return $"{(int)secondsRemaining}s";
                if (secondsRemaining < 3600) return $"{(int)(secondsRemaining / 60)}m {(int)(secondsRemaining % 60)}s";

                int hours = (int)(secondsRemaining / 3600);
                int mins = (int)((secondsRemaining % 3600) / 60);
                return $"{hours}h {mins}m";
            }
        }
    }
}