using QtisVisionPanel.Views;
using System.Windows;

namespace QtisVisionPanel.Services
{
    public class DialogService
    {
        public void ShowInfo(string message, string title)
        {
            new SystemNotificationWindow(title, message, NotificationSeverity.Info).ShowDialog();
        }

        public void ShowWarning(string message, string title)
        {
            new SystemNotificationWindow(title, message, NotificationSeverity.Warning).ShowDialog();
        }

        public void ShowError(string message, string title)
        {
            new SystemNotificationWindow(title, message, NotificationSeverity.Error).ShowDialog();
        }

        public ShutdownProgressWindow CreateShutdownProgressWindow()
        {
            return new ShutdownProgressWindow();
        }

        public OperationProgressWindow CreateOperationProgressWindow()
        {
            var window = new OperationProgressWindow();
            var owner = Application.Current?.MainWindow;

            if (owner != null && owner.IsVisible && owner != window)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            return window;
        }
    }
}
