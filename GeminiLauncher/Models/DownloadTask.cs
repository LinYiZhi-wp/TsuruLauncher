using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GeminiLauncher.Models
{
    public partial class DownloadTask : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _status = "Waiting...";

        [ObservableProperty]
        private double _progress; // 0.0 to 1.0

        [ObservableProperty]
        private long _totalBytes;

        [ObservableProperty]
        private long _downloadedBytes;

        [ObservableProperty]
        private string _speedText = "0 KB/s";

        [ObservableProperty]
        private string _sizeText = "0 / 0 MB";

        [ObservableProperty]
        private bool _isCompleted;

        [ObservableProperty]
        private bool _isFailed;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        // Sub-task Progress (for the new UI)
        [ObservableProperty] private string _jsonStatus = "Waiting...";
        [ObservableProperty] private string _jsonStatusText = "";
        [ObservableProperty] private double _jsonProgress; // 0 or 1
        
        [ObservableProperty] private string _librariesStatus = "Waiting...";
        [ObservableProperty] private string _librariesStatusText = "";
        [ObservableProperty] private double _librariesProgress;
        
        [ObservableProperty] private string _assetsStatus = "Waiting...";
        [ObservableProperty] private string _assetsStatusText = "";
        [ObservableProperty] private double _assetsProgress;
        
        [ObservableProperty] private string _componentsStatus = "Waiting...";
        [ObservableProperty] private string _componentsStatusText = "";
        [ObservableProperty] private double _componentsProgress;

        [ObservableProperty] private int _remainingFiles;

        public long LastDownloadedBytes { get; set; }
        public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();

        // Retry metadata (populated by DownloadManagerService so RetryFailed can
        // rebuild the correct download instead of guessing from the display name)
        public string VersionId { get; set; } = string.Empty;
        public string LoaderChoice { get; set; } = "Vanilla";
        public string LoaderVersion { get; set; } = string.Empty;
        public string Source { get; set; } = "Official";
        public string DownloadUrl { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        
        public string ProgressPercentageText => $"{(Progress * 100):F0}%"; // Using field is fine here for read, but Property is preferred.
        // Actually, ObservableProperty generates "Progress", so let's use that.
        // public string ProgressPercentageText => $"{(Progress * 100):F0}%"; 
        // Wait, I cannot change the lambda body easily without reading context again or knowing if I can just swap.
        // The warning said "should not be directly referenced".
        
        // Let's suppress the warning for the field access in `IncrementBytes` as we need Interlocked.
        public void IncrementBytes(long bytes)
        {
#pragma warning disable MVVMTK0034
            Interlocked.Add(ref _downloadedBytes, bytes);
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(DownloadedBytes));
        }
    }
}
