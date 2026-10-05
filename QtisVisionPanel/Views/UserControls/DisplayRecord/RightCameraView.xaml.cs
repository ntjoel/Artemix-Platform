using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    /// <summary>
    /// Dedicated operator display for a VisionPro job named Right. Acquisition
    /// and result pairing remain on the compatible Rear/Right runtime path.
    /// </summary>
    public partial class RightCameraView : UserControl
    {
        public enum InspectionStatus { NotChecked, Ok, Nok }

        private static RightCameraView _instance;
        private Dictionary<string, (Border Overlay, TextBlock Text)> _statusOverlays;
        private Dictionary<string, FrameworkElement> _featureContainers;

        public static RightCameraView Instance => _instance;
        public Cognex.VisionPro.CogRecordDisplay ImageDisplay => CogRecordsDisplay1;

        public RightCameraView()
        {
            InitializeComponent();
            _instance = this;
            CogDisplayStatusBarV21.Display = CogRecordsDisplay1;
            lbRightCamera.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "lbRightCamera",
                "Right Camera Features");
            InitializeIndicators();
            ResetAllStatusIndicators();
        }

        /// <summary>
        /// Titolo del pannello con il nome del job VPP che lo alimenta (es. "Rear"): la vista
        /// Right mostra anche i job Rear storici. Da chiamare sul thread UI.
        /// </summary>
        public void UpdateCameraTitle(string viewLabel)
        {
            lbRightCamera.Content = CameraFeaturesTitle.Format(viewLabel, "lbRightCamera", "Right Camera Features");
        }

        private void InitializeIndicators()
        {
            _statusOverlays = new Dictionary<string, (Border, TextBlock)>
            {
                { "SealingSide", (SealingStatusOverlay, SealingStatusText) },
                { "ShapeSide", (ShapeSideStatusOverlay, ShapeSideStatusText) },
                { "SideRollCount", (RollCountStatusOverlay, RollCountStatusText) }
            };

            _featureContainers = new Dictionary<string, FrameworkElement>
            {
                { "SealingSide", SealingFeatureContainer },
                { "ShapeSide", ShapeSideFeatureContainer },
                { "SideRollCount", RollCountFeatureContainer }
            };
        }

        public void ResetAllStatusIndicators()
        {
            foreach (string featureName in _statusOverlays.Keys)
            {
                SetStatusIndicator(featureName, InspectionStatus.NotChecked);
            }
        }

        public void SetStatusIndicator(string featureName, InspectionStatus status)
        {
            if (!_statusOverlays.TryGetValue(featureName, out var indicator))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (status)
                {
                    case InspectionStatus.Ok:
                        indicator.Text.Text = "\u2713";
                        indicator.Text.Foreground = Brushes.Green;
                        break;
                    case InspectionStatus.Nok:
                        indicator.Text.Text = "\u2717";
                        indicator.Text.Foreground = Brushes.Red;
                        break;
                    default:
                        indicator.Text.Text = "?";
                        indicator.Text.Foreground = Brushes.Goldenrod;
                        break;
                }

                indicator.Overlay.Visibility = Visibility.Visible;
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
                SetStatusIndicator(
                    result.Key,
                    !result.Value.HasValue
                        ? InspectionStatus.NotChecked
                        : result.Value.Value ? InspectionStatus.Ok : InspectionStatus.Nok);
            }
        }

        public void UpdateFeatureVisibility(Dictionary<string, bool> visibilityByFeature)
        {
            if (visibilityByFeature == null)
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
            catch (Exception ex) { MainWindow.logger?.Warn(ex, "RIGHT_LIVE_DISPLAY_STOP_FAILED - non-critical"); }

            try { CogRecordsDisplay1.StaticGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.InteractiveGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.Image = null; } catch { }
            try { CogRecordsDisplay1.Record = null; } catch { }
            _instance = null;
        }
    }
}
