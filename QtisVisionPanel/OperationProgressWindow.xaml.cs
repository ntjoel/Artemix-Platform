using System;
using System.Threading.Tasks;
using System.Windows;

namespace QtisVisionPanel
{
    /// <summary>
    /// Lightweight non-modal operation progress popup used for long-running
    /// operator actions such as recipe load/save and VisionPro editor save.
    /// It intentionally exposes imperative update methods because the current
    /// production baseline still coordinates these flows from view models and
    /// runtime orchestration code rather than from a full dialog viewmodel stack.
    /// </summary>
    public partial class OperationProgressWindow : Window
    {
        private bool _isClosing;

        public OperationProgressWindow()
        {
            InitializeComponent();
            Closing += (_, __) => _isClosing = true;
        }

        public void UpdateStatus(string title, string status, string detail = null, int progress = -1, string phase = null)
        {
            try
            {
                if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    if (_isClosing)
                    {
                        return;
                    }

                    OperationTitleText.Text = string.IsNullOrWhiteSpace(title)
                        ? "Operation in progress"
                        : title;
                    OperationStatusText.Text = string.IsNullOrWhiteSpace(status)
                        ? "Preparing operation..."
                        : status;
                    OperationDetailText.Text = string.IsNullOrWhiteSpace(detail)
                        ? "Please wait while the system completes the requested task."
                        : detail;
                    OperationPhaseText.Text = string.IsNullOrWhiteSpace(phase)
                        ? OperationStatusText.Text
                        : phase;

                    if (progress < 0)
                    {
                        OperationProgressBar.IsIndeterminate = true;
                        OperationProgressText.Text = "In progress";
                    }
                    else
                    {
                        int clampedProgress = Math.Max(0, Math.Min(100, progress));
                        OperationProgressBar.IsIndeterminate = false;
                        OperationProgressBar.Value = clampedProgress;
                        OperationProgressText.Text = $"{clampedProgress}%";
                    }
                });
            }
            catch (InvalidOperationException)
            {
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}
