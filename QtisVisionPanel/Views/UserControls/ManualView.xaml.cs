using QtisVisionPanel.ViewModels;
using System.Windows.Controls;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;

namespace QtisVisionPanel.Views.UserControls
{
    public partial class ManualView : UserControl
    {
        public ManualView()
        {
            InitializeComponent();
            LoadServerMessages();

            if (!DesignModeHelper.IsInDesignMode)
            {
                DataContext = new ManualViewModel();
            }
        }

        private void LoadServerMessages()
        {
            Sub_entry_ManualTitleText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualTitle", "Operator Manual");
            Sub_entry_ManualSubtitleText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualSubtitle", "Practical guide to navigate the panel and manage daily operations");
            Sub_entry_ManualRefreshButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualRefresh", "Refresh");
            Sub_entry_ManualOpenFolderButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualOpenFolder", "Open folder");
            Sub_entry_ManualExportPdfButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualExportPdf", "Export PDF");
            Sub_entry_ManualDocumentsHeader.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualDocumentsHeader", "Available documents");
            Sub_entry_ManualDocumentsHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualDocumentsHint", "Quick procedures, startup, recipes and alarm handling");
            Sub_entry_ManualPreviewUnavailableTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualPreviewUnavailableTitle", "Document preview not available");
            Sub_entry_ManualPreviewUnavailableHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ManualPreviewUnavailableHint", "Add an image in the .md file or in Docs\\Manual\\Images");
        }
    }
}
