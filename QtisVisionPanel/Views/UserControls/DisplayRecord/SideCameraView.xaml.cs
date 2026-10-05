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
    public partial class SideCameraView : UserControl
    {
        private static SideCameraView _instance;
        public static SideCameraView Instance => _instance;
        public enum InspectionStatus { NotChecked, Ok, Nok }
        private Dictionary<string, (Border Overlay, TextBlock Text)> _statusOverlays;
        private Dictionary<string, FrameworkElement> _featureContainers;
        public SideCameraView()
        {
            InitializeComponent();
            _instance = this;
            CogDisplayStatusBarV21.Display = CogRecordsDisplay1;

            ApplyRuntimeRoleLabel("side");
            InitializeOverlays();
            ResetAllStatusIndicators();
        }
        public void ApplyRuntimeRoleLabel(string cameraRole)
        {
            string normalizedRole = string.Equals(cameraRole?.Trim(), "top2d", StringComparison.OrdinalIgnoreCase)
                ? "top2d"
                : Models.CameraConfigurationHelper.NormalizeCameraType(cameraRole);

            if (string.Equals(normalizedRole, "top2d", StringComparison.OrdinalIgnoreCase))
            {
                lbSideCamera.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("lbTop2DCamera", "Top2D Planar Features");
                FeatureSummaryBorder.Visibility = Visibility.Collapsed;
                return;
            }

            if (string.Equals(cameraRole?.Trim(), "left", StringComparison.OrdinalIgnoreCase))
            {
                lbSideCamera.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("lbLeftCamera", "Left Camera Features");
                FeatureSummaryBorder.Visibility = Visibility.Visible;
                ClearThreeDMeasurementSummary();
                return;
            }

            lbSideCamera.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.lbSideCamera;
            FeatureSummaryBorder.Visibility = Visibility.Visible;
            ClearThreeDMeasurementSummary();
        }
        private void InitializeOverlays()
        {
            _statusOverlays = new Dictionary<string, (Border, TextBlock)>
        {
            { "Height", (OpenFlapsSideStatusOverlay, OpenFlapsSideStatusText) },
            { "SealingSide", (OpenSealingSideStatusOverlay, OpenSealingSideStatusText) },
            { "ShapeSide", (ShapeSideStatusOverlay, ShapeSideStatusText) },
            { "SideRollCount", (SideRollCountStatusOverlay, SideRollCountStatusText) }
        };

            _featureContainers = new Dictionary<string, FrameworkElement>
        {
            { "Height", HeightFeatureContainer },
            { "SealingSide", SealingFeatureContainer },
            { "ShapeSide", ShapeSideFeatureContainer },
            { "SideRollCount", SideRollCountFeatureContainer }
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
            if (!_statusOverlays.ContainsKey(featureName)) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var (overlay, textBlock) = _statusOverlays[featureName];

                switch (status)
                {
                    case InspectionStatus.NotChecked:
                        textBlock.Text = "?";
                        textBlock.Foreground = System.Windows.Media.Brushes.Yellow;
                        overlay.Visibility = Visibility.Visible;
                        break;
                    case InspectionStatus.Ok:
                        textBlock.Text = "✓";
                        textBlock.Foreground = System.Windows.Media.Brushes.Green;
                        overlay.Visibility = Visibility.Visible;
                        break;
                    case InspectionStatus.Nok:
                        textBlock.Text = "✗";
                        textBlock.Foreground = System.Windows.Media.Brushes.Red;
                        overlay.Visibility = Visibility.Visible;
                        break;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void UpdateStatusIndicators(Dictionary<string, bool?> results)
        {
            if (results == null) return;

            foreach (var result in results)
            {
                if (result.Value.HasValue)
                {
                    SetStatusIndicator(result.Key,
                        result.Value.Value ? InspectionStatus.Ok : InspectionStatus.Nok);
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

        public void UpdateThreeDMeasurementSummary(
            (string Label, string Summary, bool? IsWithinTolerance)? first,
            (string Label, string Summary, bool? IsWithinTolerance)? second)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!first.HasValue && !second.HasValue)
                {
                    ClearThreeDMeasurementSummary();
                    return;
                }

                ThreeDMeasurementPanel.Visibility = Visibility.Visible;
                ApplyMeasurementCard(ThreeDSecondaryCard1, ThreeDSecondaryLabelText1, ThreeDSecondarySummaryText1, ThreeDSecondaryStatusBadge1, ThreeDSecondaryStatusText1, first);
                ApplyMeasurementCard(ThreeDSecondaryCard2, ThreeDSecondaryLabelText2, ThreeDSecondarySummaryText2, ThreeDSecondaryStatusBadge2, ThreeDSecondaryStatusText2, second);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void ClearThreeDMeasurementSummary()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThreeDMeasurementPanel.Visibility = Visibility.Collapsed;
                ApplyMeasurementCard(ThreeDSecondaryCard1, ThreeDSecondaryLabelText1, ThreeDSecondarySummaryText1, ThreeDSecondaryStatusBadge1, ThreeDSecondaryStatusText1, null);
                ApplyMeasurementCard(ThreeDSecondaryCard2, ThreeDSecondaryLabelText2, ThreeDSecondarySummaryText2, ThreeDSecondaryStatusBadge2, ThreeDSecondaryStatusText2, null);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static void ApplyMeasurementCard(
            Border card,
            TextBlock label,
            TextBlock summary,
            Border badge,
            TextBlock badgeText,
            (string Label, string Summary, bool? IsWithinTolerance)? item)
        {
            if (card == null || label == null || summary == null || badge == null || badgeText == null)
            {
                return;
            }

            if (!item.HasValue)
            {
                card.Visibility = Visibility.Collapsed;
                label.Text = string.Empty;
                summary.Text = string.Empty;
                TopCameraView.ApplyMeasurementStyle(card, badge, badgeText, null);
                return;
            }

            card.Visibility = Visibility.Visible;
            label.Text = item.Value.Label ?? string.Empty;
            summary.Text = item.Value.Summary ?? string.Empty;
            TopCameraView.ApplyMeasurementStyle(card, badge, badgeText, item.Value.IsWithinTolerance);
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
            catch (Exception ex) { MainWindow.logger?.Warn(ex, "SIDE_LIVE_DISPLAY_STOP_FAILED — non-critical"); }
            try { CogRecordsDisplay1.StaticGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.InteractiveGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.Image = null; } catch { }
            try { CogRecordsDisplay1.Record = null; } catch { }
            _instance = null;
        }
    }
}
