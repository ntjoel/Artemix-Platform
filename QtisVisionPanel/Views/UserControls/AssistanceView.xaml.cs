using QtisVisionPanel.ViewModels;
using System.Windows.Controls;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;

namespace QtisVisionPanel.Views.UserControls
{
    public partial class AssistanceView : UserControl
    {
        public AssistanceView()
        {
            InitializeComponent();
            LoadServerMessages();

            if (!DesignModeHelper.IsInDesignMode)
            {
                DataContext = new AssistanceViewModel();
            }
        }

        private void LoadServerMessages()
        {
            Sub_entry_AssistanceTitleText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AssistanceTitle", "Assistance");
            Sub_entry_AssistanceOpenManualButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AssistanceOpenManual", "Open manual");
            Sub_entry_AssistanceOpenPortalButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AssistanceOpenPortal", "Company portal");
            Sub_entry_AssistanceProductionLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AssistanceProduction", "Production");
            Sub_entry_AssistanceTechnicalLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AssistanceTechnical", "Technical support");
            Sub_entry_AssistanceITLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AssistanceITSpare", "IT / Spare parts");
            Sub_entry_AssistanceChecklistTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AssistanceChecklistTitle", "Checklist before requesting support");
        }
    }
}
