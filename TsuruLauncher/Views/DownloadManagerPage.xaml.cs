using System.IO;
using System.Windows;
using System.Windows.Controls;
using TsuruLauncher.Services.Network;

namespace TsuruLauncher.Views
{
    public partial class DownloadManagerPage : Page
    {
        public DownloadManagerService ViewModel => DownloadManagerService.Instance;

        public DownloadManagerPage()
        {
            this.DataContext = ViewModel;
            InitializeComponent();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (this.NavigationService.CanGoBack)
            {
                this.NavigationService.GoBack();
            }
            else
            {
                // Fallback to home if no history
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                mainWindow.RootFrame.Navigate(new HomePage());
            }
        }

        private void NewDownload_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RootFrame.Navigate(new DownloadPage());
            }
        }

        private void CancelAll_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CancelAll();
        }

        private void RemoveCompleted_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RemoveCompleted();
        }

        private void RetryFailed_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RetryFailed();
        }

        private void CancelTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TsuruLauncher.Models.DownloadTask task)
            {
                if (!task.IsCompleted && !task.IsFailed)
                {
                    task.Cts.Cancel();
                    task.Status = "已取消";
                    task.IsFailed = true;
                }
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var mainVM = Application.Current.MainWindow.DataContext as ViewModels.MainViewModel;
            string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
            string modsDir = Path.Combine(gamePath, "mods");
            
            if (!Directory.Exists(modsDir))
            {
                Directory.CreateDirectory(modsDir);
            }

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", modsDir);
            }
            catch { }
        }
    }
}