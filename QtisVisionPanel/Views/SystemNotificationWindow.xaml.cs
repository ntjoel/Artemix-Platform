using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace QtisVisionPanel.Views
{
    public enum NotificationSeverity { Info, Warning, Error }

    public partial class SystemNotificationWindow : Window
    {
        private DispatcherTimer _autoCloseTimer;

        public bool Confirmed { get; private set; }

        public SystemNotificationWindow(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
            : this(title, message, severity, false) { }

        public SystemNotificationWindow(string title, string message, NotificationSeverity severity, bool isConfirmation,
            TimeSpan? autoCloseAfter = null, string closeButtonText = null)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;
            TimestampText.Text = DateTime.Now.ToString("HH:mm:ss  -  dd/MM/yyyy");
            Tag = closeButtonText ?? (isConfirmation ? "Si" : "OK");

            ApplyTheme(severity);

            if (isConfirmation)
            {
                NoButton.Visibility = Visibility.Visible;
            }

            if (autoCloseAfter.HasValue && autoCloseAfter.Value > TimeSpan.Zero)
            {
                _autoCloseTimer = new DispatcherTimer
                {
                    Interval = autoCloseAfter.Value
                };
                _autoCloseTimer.Tick += AutoCloseTimer_Tick;
                _autoCloseTimer.Start();
            }

            Closed += SystemNotificationWindow_Closed;
            Owner = Application.Current?.MainWindow;
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void SystemNotificationWindow_Closed(object sender, EventArgs e)
        {
            if (_autoCloseTimer != null)
            {
                _autoCloseTimer.Stop();
                _autoCloseTimer.Tick -= AutoCloseTimer_Tick;
                _autoCloseTimer = null;
            }

            Closed -= SystemNotificationWindow_Closed;
        }

        private void ApplyTheme(NotificationSeverity severity)
        {
            SolidColorBrush headerBrush, borderBrush, iconCircleBrush, subtitleFg, buttonBrush;
            string subtitleLabel, iconGlyph;

            switch (severity)
            {
                case NotificationSeverity.Error:
                    headerBrush = Brush(0xBF, 0x20, 0x20);
                    borderBrush = Brush(0xBF, 0x30, 0x30);
                    iconCircleBrush = Brush(0xFF, 0x44, 0x44);
                    subtitleFg = Brush(0xFF, 0xCC, 0xCC);
                    buttonBrush = Brush(0xBF, 0x20, 0x20);
                    subtitleLabel = "ERRORE SISTEMA";
                    iconGlyph = "";
                    break;

                case NotificationSeverity.Warning:
                    headerBrush = Brush(0x7A, 0x55, 0x00);
                    borderBrush = Brush(0xCC, 0x88, 0x00);
                    iconCircleBrush = Brush(0xFF, 0xA7, 0x26);
                    subtitleFg = Brush(0xFF, 0xE0, 0xB2);
                    buttonBrush = Brush(0xE6, 0x51, 0x00);
                    subtitleLabel = "AVVISO SISTEMA";
                    iconGlyph = "";
                    break;

                default:
                    headerBrush = Brush(0x0D, 0x47, 0xA1);
                    borderBrush = Brush(0x15, 0x65, 0xC0);
                    iconCircleBrush = Brush(0x19, 0x76, 0xD2);
                    subtitleFg = Brush(0xBB, 0xDE, 0xFB);
                    buttonBrush = Brush(0x15, 0x65, 0xC0);
                    subtitleLabel = "INFORMAZIONE";
                    iconGlyph = "";
                    break;
            }

            RootBorder.BorderBrush = borderBrush;
            HeaderBorder.Background = headerBrush;
            IconCircle.Background = iconCircleBrush;
            SubtitleText.Foreground = subtitleFg;
            SubtitleText.Text = subtitleLabel;
            IconText.Text = iconGlyph;
            CloseButton.Tag = buttonBrush;
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromRgb(r, g, b));

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
