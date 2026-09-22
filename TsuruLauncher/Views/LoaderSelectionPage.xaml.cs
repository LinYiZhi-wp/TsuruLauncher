using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TsuruLauncher.Services.Animation;
using TsuruLauncher.ViewModels;

namespace TsuruLauncher.Views
{
    public partial class LoaderSelectionPage : Page
    {
        public LoaderSelectionViewModel VM => (LoaderSelectionViewModel)DataContext;

        public LoaderSelectionPage(Models.DownloadableVersion version)
        {
            InitializeComponent();
            DataContext = new LoaderSelectionViewModel();
            VM.Initialize(version);
            VM.PropertyChanged += VM_PropertyChanged;
            Loaded += LoaderSelectionPage_Loaded;
        }

        private void LoaderSelectionPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (LoaderPanel != null)
            {
                PageTransition.PlayStaggeredIn(LoaderPanel, staggerMs: 60);
            }
        }

        private void VM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(LoaderSelectionViewModel.ForgeExpanded):
                    if (VM.ForgeExpanded && ForgeExpandPanel != null)
                        PageTransition.PlayExpandCollapse(ForgeExpandPanel, true);
                    break;
                case nameof(LoaderSelectionViewModel.FabricExpanded):
                    if (VM.FabricExpanded && FabricExpandPanel != null)
                        PageTransition.PlayExpandCollapse(FabricExpandPanel, true);
                    break;
                case nameof(LoaderSelectionViewModel.OptifineExpanded):
                    if (VM.OptifineExpanded && OptifineExpandPanel != null)
                        PageTransition.PlayExpandCollapse(OptifineExpandPanel, true);
                    break;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
            else if (Application.Current.MainWindow is MainWindow mw)
                mw.RootFrame.Navigate(new DownloadPage());
        }

        private void MinimizeDownload_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
                mw.RootFrame.Navigate(new DownloadManagerPage());
        }
    }
}