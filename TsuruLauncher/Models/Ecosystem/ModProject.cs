using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace TsuruLauncher.Models.Ecosystem
{
    public enum ProjectPlatform
    {
        Modrinth,
        CurseForge
    }

    public enum ProjectType
    {
        Mod,
        Modpack,
        ResourcePack,
        Shader,
        DataPack
    }

    public class ModProject : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;

        /// <summary>
        /// 缩略图。⚠ 它**必须**支持属性变更通知 —— 图片是在卡片已经加入集合**之后**才异步加载回来的
        /// （<c>LoadFeaturedContentAsync</c> 先填集合、再 <c>await PreloadImagesAsync</c>），
        /// 没有通知的话网格视图里 <c>&lt;Image Source="{Binding IconImage}"&gt;</c> 永远收不到值，
        /// 表现就是「所有缩略图都是空白灰块」（列表视图有 <c>SearchRows.NotifyIconChanged()</c> 所以正常）。
        /// </summary>
        private BitmapImage? _iconImage;
        public BitmapImage? IconImage
        {
            get => _iconImage;
            set
            {
                if (ReferenceEquals(_iconImage, value)) return;
                _iconImage = value;
                OnPropertyChanged();
            }
        }

        public string Author { get; set; } = string.Empty;
        public long Downloads { get; set; }
        public ProjectPlatform Platform { get; set; }
        public ProjectType Type { get; set; } = ProjectType.Mod;
        public string WebUrl { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}