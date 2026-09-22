using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Timers;

namespace TsuruLauncher.Services
{
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class NotificationMessage
    {
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public NotificationType Type { get; set; } = NotificationType.Info;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3);
        public Action? OnClick { get; set; }
    }

    public class NotificationService : INotifyPropertyChanged
    {
        public event Action<NotificationMessage>? OnShowNotification;
#pragma warning disable CS0067
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

        public void Show(string title, string message, NotificationType type = NotificationType.Info, int durationSeconds = 3, Action? onClick = null)
        {
            OnShowNotification?.Invoke(new NotificationMessage
            {
                Title = title,
                Message = message,
                Type = type,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                OnClick = onClick
            });
        }

        public void ShowError(string title, string message, int durationSeconds = 5)
        {
            Show(title, message, NotificationType.Error, durationSeconds);
        }

        public void ShowSuccess(string title, string message)
        {
            Show(title, message, NotificationType.Success);
        }

        public void ShowWarning(string title, string message)
        {
            Show(title, message, NotificationType.Warning);
        }
    }
}