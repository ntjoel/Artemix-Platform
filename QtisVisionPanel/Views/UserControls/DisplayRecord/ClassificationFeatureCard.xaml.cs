using QtisVisionPanel.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    public partial class ClassificationFeatureCard : UserControl
    {
        public ClassificationFeatureCard()
        {
            InitializeComponent();
            RefreshLocalizedLabels();
        }

        public void UpdateResult(CameraClassificationResult result)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(() => UpdateResult(result)),
                    DispatcherPriority.Background);
                return;
            }

            if (result == null || string.IsNullOrWhiteSpace(result.ClassName))
            {
                SummaryText.Text = string.Empty;
                ToolTip = null;
                Visibility = Visibility.Collapsed;
                return;
            }

            RefreshLocalizedLabels();
            SummaryText.Text = BuildSummary(result);
            ToolTip = SummaryText.Text;
            ApplyState(result.State);
            Visibility = Visibility.Visible;
        }

        private void RefreshLocalizedLabels()
        {
            TitleText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_AiClassification",
                "AI classification");
        }

        private static string BuildSummary(CameraClassificationResult result)
        {
            string classLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ClassificationClass",
                "Class");
            string scoreLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ClassificationScore",
                "Score");
            string scoreText = FormatScore(result.Score);

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}: {1}   |   {2}: {3}",
                classLabel,
                result.ClassName,
                scoreLabel,
                scoreText);
        }

        private static string FormatScore(double? score)
        {
            if (!score.HasValue)
            {
                return "--";
            }

            double value = score.Value;
            if (value >= 0 && value <= 1)
            {
                return value.ToString("P1", CultureInfo.CurrentCulture);
            }

            if (value > 1 && value <= 100)
            {
                return value.ToString("0.0", CultureInfo.CurrentCulture) + "%";
            }

            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        private void ApplyState(CameraClassificationState state)
        {
            switch (state)
            {
                case CameraClassificationState.Passed:
                    ApplyPalette(
                        Color.FromRgb(232, 247, 236),
                        Color.FromRgb(111, 180, 126),
                        Color.FromRgb(75, 175, 80),
                        Color.FromRgb(38, 122, 66),
                        ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusGood", "GOOD"));
                    break;

                case CameraClassificationState.Failed:
                    ApplyPalette(
                        Color.FromRgb(252, 235, 235),
                        Color.FromRgb(220, 95, 95),
                        Color.FromRgb(211, 47, 47),
                        Color.FromRgb(174, 37, 37),
                        ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusNoGood", "NO GOOD"));
                    break;

                case CameraClassificationState.Unclassified:
                    ApplyPalette(
                        Color.FromRgb(255, 246, 224),
                        Color.FromRgb(217, 160, 48),
                        Color.FromRgb(191, 124, 0),
                        Color.FromRgb(157, 96, 0),
                        ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                            "Sub_entry_Unclassified",
                            "UNCLASSIFIED"));
                    break;

                default:
                    ApplyPalette(
                        Color.FromRgb(240, 246, 251),
                        Color.FromRgb(117, 169, 209),
                        Color.FromRgb(47, 128, 195),
                        Color.FromRgb(31, 95, 145),
                        ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StatusResult", "RESULT"));
                    break;
            }
        }

        private void ApplyPalette(
            Color cardBackground,
            Color cardBorder,
            Color badgeBackground,
            Color iconAccent,
            string status)
        {
            CardBorder.Background = new SolidColorBrush(cardBackground);
            CardBorder.BorderBrush = new SolidColorBrush(cardBorder);
            StatusBadge.Background = new SolidColorBrush(badgeBackground);
            StatusText.Text = status;
            IconFrame.Background = new SolidColorBrush(cardBackground);
            IconFrame.BorderBrush = new SolidColorBrush(iconAccent);
            IconText.Foreground = new SolidColorBrush(iconAccent);
        }
    }
}
