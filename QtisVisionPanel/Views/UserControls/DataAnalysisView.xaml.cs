using QtisVisionPanel.ViewModels;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace QtisVisionPanel.Views.UserControls
{
    public partial class DataAnalysisView : UserControl, IDisposable
    {
        public DataAnalysisView()
        {
            InitializeComponent();
            DataContext = new DataAnalysisViewModel();
        }

        public void Dispose()
        {
            (DataContext as IDisposable)?.Dispose();
        }

        private void ExportPdfButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                var viewModel = DataContext as DataAnalysisViewModel;
                string defaultName = "DataAnalysis_Grafici_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".pdf";

                var dialog = new SaveFileDialog
                {
                    Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataAnalysisExportPdf", "Export PDF"),
                    FileName = defaultName,
                    Filter = "PDF (*.pdf)|*.pdf",
                    AddExtension = true,
                    DefaultExt = ".pdf"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                VisualPdfExportService.ExportFrameworkElements(
                    CollectVisibleChartCards(),
                    dialog.FileName,
                    viewModel?.Title ?? "Data Analysis",
                    viewModel?.ExportSubtitle ?? string.Empty,
                    ResolveLogoPath());

                string message = ServerMessagePersonalize.GetMessageOrDefault(
                    "Sub_entry_DataAnalysisPdfExportSuccess",
                    "Data Analysis PDF exported successfully.");
                new SystemNotificationWindow(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataAnalysisTitle", "Data Analysis"),
                    message + Environment.NewLine + dialog.FileName,
                    NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                string message = ServerMessagePersonalize.GetMessageOrDefault(
                    "Sub_entry_DataAnalysisPdfExportError",
                    "Error while exporting Data Analysis PDF:");
                new SystemNotificationWindow(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataAnalysisTitle", "Data Analysis"),
                    message + Environment.NewLine + ex.Message,
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private static string ResolveLogoPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "Docs", "Manual", "Images", "Pulsar_Engineering_logo.png"),
                Path.Combine(baseDirectory, "resources", "logo_pulsar.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "Docs", "Manual", "Images", "Pulsar_Engineering_logo.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "resources", "logo_pulsar.png")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private IReadOnlyList<FrameworkElement> CollectVisibleChartCards()
        {
            UpdateLayout();

            var cards = new List<FrameworkElement>();
            AddIfVisible(cards, ProductionOverviewCard);
            AddIfVisible(cards, DefectDistributionCard);

            TrendCardsItemsControl.UpdateLayout();
            foreach (object item in TrendCardsItemsControl.Items)
            {
                var container = TrendCardsItemsControl.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
                if (container == null)
                {
                    continue;
                }

                AddIfVisible(cards, FindVisibleChartBorder(container));
            }

            return cards;
        }

        private static void AddIfVisible(ICollection<FrameworkElement> cards, FrameworkElement element)
        {
            if (element != null && element.IsVisible && element.ActualWidth > 20 && element.ActualHeight > 20)
            {
                cards.Add(element);
            }
        }

        private static FrameworkElement FindVisibleChartBorder(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is Border border && border.IsVisible && border.ActualWidth > 20 && border.ActualHeight > 20)
                {
                    return border;
                }

                FrameworkElement result = FindVisibleChartBorder(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
