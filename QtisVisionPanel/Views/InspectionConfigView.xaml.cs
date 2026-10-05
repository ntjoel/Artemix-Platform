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

namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Logica di interazione per InspectionConfigView.xaml
    /// </summary>
    public partial class InspectionConfigView : UserControl
    {
        public InspectionConfigView()
        {
            InitializeComponent();
            loadServerMessage();
        }

        public void loadServerMessage()
        {
            // Carica i messaggi personalizzati
            Sub_entry_Inspection_header.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_header", "Inspection Configuration");
            Sub_entry_Inspection_inf.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_inf", "Enable and manage inspections for the active machine profile");
            Sub_entry_Inspection_machineTypeLabel.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_machineTypeLabel", "Machine type");
            Sub_entry_Inspection_reloadButton.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_reloadButton", "Reload");
            Sub_entry_Inspection_resetButton.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_resetButton", "Reset Default");
            Sub_entry_Inspection_saveButton.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_saveButton", "Save");
            Sub_entry_Inspection_loadingText.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_loadingText", "Loading...");
            Sub_entry_Inspection_enabledSummary.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Enable", "Enabled");
            Sub_entry_Inspection_disabledSummary.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Disable", "Disabled");
            Sub_entry_Inspection_totalSummary.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Total", "Total");
            Sub_entry_Inspection_matrixTitle.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_Inspection_matrixTitle",
                "Inspections per camera view");
            Sub_entry_Inspection_matrixHint.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_Inspection_matrixHint",
                "Select which cameras this recipe uses, then enable only the inspections produced by each VisionPro view. Machine I/O mapping remains global.");

        }
    }
}
