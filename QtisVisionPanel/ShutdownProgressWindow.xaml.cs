using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QtisVisionPanel
{
    /// <summary>
    /// Logica di interazione per ShutdownProgressWindow.xaml
    /// </summary>
    public partial class ShutdownProgressWindow : Window
    {
        private bool _isClosing = false;
        public ShutdownProgressWindow()
        {
            InitializeComponent();
            this.Closing += (s, e) => _isClosing = true;
        }
        public void UpdateStatus(string message, int progress = 0)
        {
            // Proteggi da eccezioni se il Dispatcher sta chiudendo
            try
            {
                if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                    return;

                Dispatcher.Invoke(() =>
                {
                    if (_isClosing) return;  // Double-check
                    StatusText.Text = message;
                    ProgressBar.Value = progress;
                    ProgressText.Text = $"{progress}%";
                });
            }
            catch (InvalidOperationException)
            {
                // Dispatcher shutdown in progress, ignora silenziosamente
            }
            catch (TaskCanceledException)
            {
                // Operation canceled, ignora silenziosamente
            }
        }

        public void Complete()
        {
            try
            {
                if (_isClosing || Dispatcher.HasShutdownStarted) return;

                Dispatcher.Invoke(() =>
                {
                    if (_isClosing) return;
                    StatusText.Text = "Chiusura completata!";
                    ProgressBar.Value = 100;
                    ProgressText.Text = "100%";
                });
            }
            catch { /* Ignora eccezioni durante shutdown */ }
        }
    }
}
