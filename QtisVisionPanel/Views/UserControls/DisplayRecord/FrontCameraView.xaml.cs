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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    /// <summary>
    /// Logica di interazione per ImageView.xaml
    /// </summary>
    public partial class FrontCameraView : UserControl
    {
        private static FrontCameraView _instance;
        public static FrontCameraView Instance => _instance;
        public FrontCameraView()
        {
            InitializeComponent();
            _instance = this;
            CogDisplayStatusBarV21.Display = CogRecordsDisplay1;
            lbfrontCamera.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.lbfrontCamera;
            TraceabilityStatusLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_TraceabilityStatus;
            TraceabilityCodeLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_TraceabilityCode;
            ExpectedPrefixLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ExpectedCodePrefix;
            ResetTraceabilityStatus();
        }
        public void ResetTraceabilityStatus()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TraceabilityStatusValueText.Text = "Waiting result";
                TraceabilityStatusValueText.Foreground = Brushes.Goldenrod;
                TraceabilityCodeValueText.Text = "-";
                ExpectedPrefixValueText.Text = "-";
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void UpdateTraceabilityStatus(bool? traceabilityDetected, string code, string expectedPrefix, bool? isValid)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExpectedPrefixValueText.Text = string.IsNullOrWhiteSpace(expectedPrefix) ? "-" : expectedPrefix;
                TraceabilityCodeValueText.Text = string.IsNullOrWhiteSpace(code) ? "-" : code;

                if (!traceabilityDetected.HasValue)
                {
                    TraceabilityStatusValueText.Text = "Not checked";
                    TraceabilityStatusValueText.Foreground = Brushes.Goldenrod;
                    return;
                }

                if (traceabilityDetected.Value && isValid == true)
                {
                    TraceabilityStatusValueText.Text = "Detected";
                    TraceabilityStatusValueText.Foreground = Brushes.Green;
                }
                else if (traceabilityDetected.Value)
                {
                    TraceabilityStatusValueText.Text = "Detected with mismatch";
                    TraceabilityStatusValueText.Foreground = Brushes.OrangeRed;
                }
                else
                {
                    TraceabilityStatusValueText.Text = "Missing";
                    TraceabilityStatusValueText.Foreground = Brushes.Red;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void UpdateTraceabilityVisibility(bool isVisible)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TraceabilityFeatureContainer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        public void dispose()
        {
            try { CogDisplayStatusBarV21.Display = null; } catch { }
            try
            {
                if (CogRecordsDisplay1.LiveDisplayRunning)
                {
                    CogRecordsDisplay1.StopLiveDisplay();
                }
            }
            catch (Exception ex) { MainWindow.logger?.Warn(ex, "FRONT_LIVE_DISPLAY_STOP_FAILED — non-critical"); }
            try { CogRecordsDisplay1.StaticGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.InteractiveGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.Image = null; } catch { }
            try { CogRecordsDisplay1.Record = null; } catch { }
            ResetTraceabilityStatus();
            _instance = null;
        }
    }
}
