using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    /// <summary>
    /// Logica di interazione per ImageView.xaml
    /// </summary>
    public partial class BottomCameraView : UserControl
    {
        private static BottomCameraView _instance;
        public static BottomCameraView Instance => _instance;
        public Cognex.VisionPro.CogRecordDisplay ImageDisplay => CogRecordsDisplay1;
        public enum InspectionStatus { NotChecked, Ok, Nok }
        private Dictionary<string, (Border Overlay, TextBlock Text)> _statusOverlays;
        private Dictionary<string, FrameworkElement> _featureContainers;

        public BottomCameraView()
        {
            InitializeComponent();
            _instance = this;
            CogDisplayStatusBarV21.Display = CogRecordsDisplay1;
            lbbottom.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.lbbottom;
            InitializeOverlays();
            ResetAllStatusIndicators();
        }

        private void InitializeOverlays()
        {
            _statusOverlays = new Dictionary<string, (Border, TextBlock)>
            {
                { "BottomSealing", (BottomSealingStatusOverlay, BottomSealingStatusText) },
                { "TrappedPaper", (TrappedPaperStatusOverlay, TrappedPaperStatusText) }
            };

            _featureContainers = new Dictionary<string, FrameworkElement>
            {
                { "BottomSealing", BottomSealingFeatureContainer },
                { "TrappedPaper", TrappedPaperFeatureContainer }
            };
        }

        public void ResetAllStatusIndicators()
        {
            foreach (var key in _statusOverlays.Keys)
            {
                SetStatusIndicator(key, InspectionStatus.NotChecked);
            }
        }

        public void SetStatusIndicator(string featureName, InspectionStatus status)
        {
            if (_statusOverlays == null || !_statusOverlays.ContainsKey(featureName))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var overlay = _statusOverlays[featureName].Overlay;
                var textBlock = _statusOverlays[featureName].Text;

                switch (status)
                {
                    case InspectionStatus.NotChecked:
                        textBlock.Text = "?";
                        textBlock.Foreground = Brushes.Yellow;
                        overlay.Visibility = Visibility.Visible;
                        break;
                    case InspectionStatus.Ok:
                        textBlock.Text = "✓";
                        textBlock.Foreground = Brushes.Green;
                        overlay.Visibility = Visibility.Visible;
                        break;
                    case InspectionStatus.Nok:
                        textBlock.Text = "✗";
                        textBlock.Foreground = Brushes.Red;
                        overlay.Visibility = Visibility.Visible;
                        break;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void UpdateStatusIndicators(Dictionary<string, bool?> results)
        {
            if (results == null)
            {
                return;
            }

            foreach (var result in results)
            {
                if (result.Value.HasValue)
                {
                    SetStatusIndicator(result.Key, result.Value.Value ? InspectionStatus.Ok : InspectionStatus.Nok);
                }
                else
                {
                    SetStatusIndicator(result.Key, InspectionStatus.NotChecked);
                }
            }
        }

        public void UpdateFeatureVisibility(Dictionary<string, bool> visibilityByFeature)
        {
            if (visibilityByFeature == null || _featureContainers == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var featureContainer in _featureContainers)
                {
                    bool isVisible = visibilityByFeature.TryGetValue(featureContainer.Key, out bool enabled) && enabled;
                    featureContainer.Value.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }
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
            catch (Exception ex) { MainWindow.logger?.Warn(ex, "BOTTOM_LIVE_DISPLAY_STOP_FAILED — non-critical"); }
            try { CogRecordsDisplay1.StaticGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.InteractiveGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.Image = null; } catch { }
            try { CogRecordsDisplay1.Record = null; } catch { }
            _instance = null;
        }
    }
}
