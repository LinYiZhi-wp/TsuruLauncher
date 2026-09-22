using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// Mac风格窗口控制按钮（红黄绿三色圆点）
    /// </summary>
    public partial class MacWindowButtons : UserControl
    {
        public MacWindowButtons()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.WindowState = window.WindowState == WindowState.Maximized 
                    ? WindowState.Normal 
                    : WindowState.Maximized;
            }
        }
    }
}