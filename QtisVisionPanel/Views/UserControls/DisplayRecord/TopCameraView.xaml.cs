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
using QtisVisionPanel.DataManage;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    /// <summary>
    /// Logica di interazione per ImageView.xaml
    /// </summary>
    public partial class TopCameraView : UserControl
    {
        private static TopCameraView _instance;
        public static TopCameraView Instance => _instance;
        // Enum per stati
        public enum InspectionStatus { NotChecked, Ok, Nok }
        // Riferimenti agli overlay
        private Dictionary<string, (Border Overlay, TextBlock Text)> _statusOverlays;
        private Dictionary<string, FrameworkElement> _featureContainers;
        public TopCameraView()
        {
            InitializeComponent();
            _instance = this;
            CogDisplayStatusBarV21.Display = CogRecordsDisplay1;
            ApplyRuntimeRoleLabel("top");
            // Inizializza tutte le icone a "non controllato" (giallo)
            InitializeOverlays();
            ResetAllStatusIndicators();
        }
        public void ApplyRuntimeRoleLabel(string cameraRole)
        {
            string normalizedRole = Models.CameraConfigurationHelper.NormalizeCameraType(cameraRole);
            if (string.Equals(normalizedRole, "top3d", StringComparison.OrdinalIgnoreCase))
            {
                lbTopCamera.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("lbTop3DCamera", "Top3D Sensor Features");
                FeatureSummaryBorder.Visibility = Visibility.Collapsed;
                return;
            }

            lbTopCamera.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.lbTopCamera;
            FeatureSummaryBorder.Visibility = Visibility.Visible;
            ClearThreeDMeasurementSummary();
        }
        private void InitializeOverlays()
        {
            _statusOverlays = new Dictionary<string, (Border, TextBlock)>
        {
            { "Logo", (LogoStatusOverlay, LogoStatusText) },
            { "PrintCentering", (PrincenteruingStatusOverlay, PrincenteruingStatusText) },
            { "OpenFlaps", (OpenFlapsStatusOverlay, OpenFlapsStatusText) },
            { "ShapeTop", (ShapeTopStatusOverlay, ShapeTopStatusText) },
            { "SurfaceCheck", (OpenPerforationStatusOverlay, OpenPerforationStatusText) }
        };

            _featureContainers = new Dictionary<string, FrameworkElement>
        {
            { "Logo", LogoFeatureContainer },
            { "PrintCentering", PrintCenteringFeatureContainer },
            { "OpenFlaps", OpenFlapsFeatureContainer },
            { "ShapeTop", ShapeTopFeatureContainer },
            { "SurfaceCheck", SurfaceCheckFeatureContainer }
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
        // Aggiorna tutti gli stati in base ai risultati
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

        public void UpdateThreeDMeasurementSummary(string label, string summary, bool? isWithinTolerance)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThreeDMeasurementPanel.Visibility = Visibility.Visible;
                ThreeDPrimaryLabelText.Text = label ?? string.Empty;
                ThreeDPrimarySummaryText.Text = summary ?? string.Empty;
                ApplyMeasurementStyle(ThreeDPrimaryCard, ThreeDPrimaryStatusBadge, ThreeDPrimaryStatusText, isWithinTolerance);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void UpdateThreeDProfileSummary(string label, string summary, ProcessMonitoringState state)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThreeDMeasurementPanel.Visibility = Visibility.Visible;
                ThreeDProfileCard.Visibility = Visibility.Visible;
                ThreeDProfileLabelText.Text = label ?? string.Empty;
                ThreeDProfileSummaryText.Text = summary ?? string.Empty;
                ApplyMonitoringStyle(
                    ThreeDProfileCard,
                    ThreeDProfileStatusBadge,
                    ThreeDProfileStatusText,
                    state);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void ClearThreeDProfileSummary()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThreeDProfileCard.Visibility = Visibility.Collapsed;
                ThreeDProfileLabelText.Text = string.Empty;
                ThreeDProfileSummaryText.Text = string.Empty;
                ApplyMonitoringStyle(
                    ThreeDProfileCard,
                    ThreeDProfileStatusBadge,
                    ThreeDProfileStatusText,
                    ProcessMonitoringState.Wait);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void ClearThreeDMeasurementSummary()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ThreeDMeasurementPanel.Visibility = Visibility.Collapsed;
                ThreeDPrimaryLabelText.Text = string.Empty;
                ThreeDPrimarySummaryText.Text = string.Empty;
                ApplyMeasurementStyle(ThreeDPrimaryCard, ThreeDPrimaryStatusBadge, ThreeDPrimaryStatusText, null);
                ThreeDProfileCard.Visibility = Visibility.Collapsed;
                ThreeDProfileLabelText.Text = string.Empty;
                ThreeDProfileSummaryText.Text = string.Empty;
                ApplyMonitoringStyle(
                    ThreeDProfileCard,
                    ThreeDProfileStatusBadge,
                    ThreeDProfileStatusText,
                    ProcessMonitoringState.Wait);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal static void ApplyMeasurementStyle(Border card, Border badge, TextBlock badgeText, bool? isWithinTolerance)
        {
            if (card == null || badge == null || badgeText == null)
            {
                return;
            }

            if (!isWithinTolerance.HasValue)
            {
                card.Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(215, 226, 236));
                badge.Background = new SolidColorBrush(Color.FromRgb(217, 227, 236));
                badgeText.Foreground = new SolidColorBrush(Color.FromRgb(31, 59, 87));
                badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusPending", "WAIT");
                return;
            }

            if (isWithinTolerance.Value)
            {
                card.Background = new SolidColorBrush(Color.FromRgb(232, 247, 236));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(111, 180, 126));
                badge.Background = new SolidColorBrush(Color.FromRgb(75, 175, 80));
                badgeText.Foreground = Brushes.White;
                badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusGood", "GOOD");
                return;
            }

            card.Background = new SolidColorBrush(Color.FromRgb(252, 235, 235));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 95, 95));
            badge.Background = new SolidColorBrush(Color.FromRgb(211, 47, 47));
            badgeText.Foreground = Brushes.White;
            badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusNoGood", "NO GOOD");
        }

        private static void ApplyMonitoringStyle(
            Border card,
            Border badge,
            TextBlock badgeText,
            ProcessMonitoringState state)
        {
            switch (state)
            {
                case ProcessMonitoringState.Good:
                    card.Background = new SolidColorBrush(Color.FromRgb(232, 247, 236));
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(111, 180, 126));
                    badge.Background = new SolidColorBrush(Color.FromRgb(75, 175, 80));
                    badgeText.Foreground = Brushes.White;
                    badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusGood", "GOOD");
                    break;

                case ProcessMonitoringState.NoGood:
                    card.Background = new SolidColorBrush(Color.FromRgb(252, 235, 235));
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 95, 95));
                    badge.Background = new SolidColorBrush(Color.FromRgb(211, 47, 47));
                    badgeText.Foreground = Brushes.White;
                    badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusNoGood", "NO GOOD");
                    break;

                case ProcessMonitoringState.Invalid:
                    card.Background = new SolidColorBrush(Color.FromRgb(255, 247, 225));
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(218, 159, 25));
                    badge.Background = new SolidColorBrush(Color.FromRgb(190, 125, 0));
                    badgeText.Foreground = Brushes.White;
                    badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusInvalid", "INVALID");
                    break;

                case ProcessMonitoringState.Monitor:
                    card.Background = new SolidColorBrush(Color.FromRgb(234, 244, 252));
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(91, 153, 202));
                    badge.Background = new SolidColorBrush(Color.FromRgb(47, 128, 195));
                    badgeText.Foreground = Brushes.White;
                    badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusMonitor", "MONITOR");
                    break;

                default:
                    card.Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));
                    card.BorderBrush = new SolidColorBrush(Color.FromRgb(215, 226, 236));
                    badge.Background = new SolidColorBrush(Color.FromRgb(217, 227, 236));
                    badgeText.Foreground = new SolidColorBrush(Color.FromRgb(31, 59, 87));
                    badgeText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusPending", "WAIT");
                    break;
            }
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
            catch (Exception ex) { MainWindow.logger?.Warn(ex, "TOP_LIVE_DISPLAY_STOP_FAILED — non-critical"); }
            try { CogRecordsDisplay1.StaticGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.InteractiveGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.Image = null; } catch { }
            try { CogRecordsDisplay1.Record = null; } catch { }
            _instance = null;
        }
    }
}
