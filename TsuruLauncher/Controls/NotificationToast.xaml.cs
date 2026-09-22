using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TsuruLauncher.Services;

namespace TsuruLauncher.Controls
{
    public partial class NotificationToast : UserControl
    {
        private DispatcherTimer? _timer;
        private Action _onClose;

        public NotificationToast(NotificationMessage message, Action onClose)
        {
            InitializeComponent();
            _onClose = onClose;

            TitleText.Text = message.Title;
            MessageText.Text = message.Message;

            // Style based on type
            switch (message.Type)
            {
                case NotificationType.Success:
                    IconText.Text = "✓";
                    IconBorder.Background = Utilities.ThemeBrush.Success;
                    break;
                case NotificationType.Warning:
                    IconText.Text = "⚠";
                    IconBorder.Background = Utilities.ThemeBrush.Warning;
                    break;
                case NotificationType.Error:
                    IconText.Text = "✕";
                    IconBorder.Background = Utilities.ThemeBrush.Danger;
                    break;
                case NotificationType.Info:
                default:
                    IconText.Text = "ℹ";
                    IconBorder.Background = Utilities.ThemeBrush.Info;
                    break;
            }

            // Click action
            if (message.OnClick != null)
            {
                this.Cursor = System.Windows.Input.Cursors.Hand;
                this.MouseLeftButtonUp += (s, e) => { message.OnClick(); Close(); };
            }

            // Auto close timer
            if (message.Duration.TotalSeconds > 0)
            {
                _timer = new DispatcherTimer { Interval = message.Duration };
                _timer.Tick += (s, e) => Close();
                _timer.Start();
            }

            // Animate In
            Loaded += (s, e) => 
            {
                var sb = FindResource("SlideIn") as Storyboard;
                sb?.Begin(this);
            };
        }

        public void Close()
        {
            _timer?.Stop();
            var sb = FindResource("SlideOut") as Storyboard;
            if (sb != null)
            {
                sb.Completed += (s, e) => _onClose?.Invoke();
                sb.Begin(this);
            }
            else
            {
                _onClose?.Invoke();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}