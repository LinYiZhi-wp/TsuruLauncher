using System.Windows.Controls;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 资源筛选卡片。由外壳右信息面板在「资源」页承载（<c>MainWindow.InfoPanelResourcesFilterHost</c>），
    /// DataContext 是 <c>ResourcesViewModel</c>（MainWindow 在导航时注入）。
    /// 原本内嵌在 ResourcesPage 里，搬到右栏后资源内容区腾出 240px，卡片得以排成两列。
    /// </summary>
    public partial class ResourcesFilterPanel : UserControl
    {
        public ResourcesFilterPanel() => InitializeComponent();
    }
}
