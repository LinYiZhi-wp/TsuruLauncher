using System;
using System.Windows;
using System.Windows.Controls;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 资源详情的右侧信息栏内容（对齐 Axolotl 的右栏）：
    /// 兼容性（游戏版本 chips / 平台 / 支持环境）、相关链接、标签、作者、信息。
    ///
    /// <para>放在**外壳最右边那条信息栏**里（`InfoPanelResourceDetailHost`），
    /// 不是塞进页面自己的列里 —— 之前就是塞在页面里，观感完全不对。
    /// DataContext 由 MainWindow 指向该页的 ResourceDetailViewModel。</para>
    /// </summary>
    public partial class ResourceDetailSidePanel : UserControl
    {
        public ResourceDetailSidePanel()
        {
            InitializeComponent();
        }

        /// <summary>相关链接按钮：Tag 里挂 URL，用系统默认浏览器打开。</summary>
        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "ResourceDetailSidePanel.OpenLink");
            }
        }
    }
}
