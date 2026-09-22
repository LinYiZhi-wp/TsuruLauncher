using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TsuruLauncher.Services.Network;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TsuruLauncher.Views
{
    public partial class DownloadManagerPanel : UserControl
    {
        public DownloadManagerService ViewModel => DownloadManagerService.Instance;

        public DownloadManagerPanel()
        {
            InitializeComponent();
            this.DataContext = this;
            
            // Periodically update active status
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => {
                OnPropertyChanged(nameof(HasActiveTasks));
            };
            timer.Start();
        }

        public bool HasActiveTasks => ViewModel.ActiveTasks.Any(t => !t.IsCompleted && !t.IsFailed);

        private void NavigateToManager_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RootFrame.Navigate(new DownloadManagerPage());
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}