using QtisVisionPanel.ViewModels;
using System.Windows;
using System.Windows.Controls;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;

namespace QtisVisionPanel.Views.UserControls
{
    /// <summary>
    /// Logica di interazione per AlarmsView.xaml
    /// </summary>
    public partial class AlarmsView : UserControl
    {
        private readonly AlarmsViewModel _viewModel;

        public AlarmsView()
        {
            InitializeComponent();
            LoadServerMessages();

            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }

            _viewModel = new AlarmsViewModel();
            DataContext = _viewModel;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.InitializeAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Dispose();
        }

        private void LoadServerMessages()
        {
            Sub_entry_EventsMonitorTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_EventsMonitorTitle", "Events Monitor");
            Sub_entry_EventsMonitorSubtitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_EventsMonitorSubtitle", "Application events history, machine states and diagnostic exceptions");
            Sub_entry_RefreshButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Refresh", "Refresh");
            Sub_entry_ClearEventsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ClearEvents", "Clear events");
            Sub_entry_ClearWarningInfoButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ClearWarningInfoShort", "Delete W/I");
            Sub_entry_ClearOlderButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ClearOlderShort", "Delete >1d");
            Sub_entry_AutoRefreshCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AutoRefresh", "Auto refresh");
            Sub_entry_TotalEventsLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_TotalEvents", "Total events");
            Sub_entry_WarningLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Warning", "Warning");
            Sub_entry_ErrorsCriticalLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ErrorsCritical", "Errors / Critical");
            Sub_entry_DatabaseStatusLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DatabaseStatus", "Database");
            Sub_entry_VisionProStatusLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_VisionProStatus", "VisionPro");
            Sub_entry_ClearFiltersButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ClearFilters", "Clear filters");
            Sub_entry_PCDiagnosticsCardTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_PCDiagnosticsTitle", "PC diagnostics");
            Sub_entry_OpenSystemDiagnosticsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OpenSystemDiagnostics", "Open PC diagnostics");
            Sub_entry_EventStreamTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_EventStreamTitle", "Event Stream");
            Sub_entry_EventStreamHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_EventStreamHint", "Recent history of events stored in `tbllogevent`");
            Sub_entry_LevelColumn.Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Level", "Level");
            Sub_entry_TimestampColumn.Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Timestamp", "Timestamp");
            Sub_entry_CategoryColumn.Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Category", "Category");
            Sub_entry_CodeColumn.Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Code", "Code");
            Sub_entry_MessageColumn.Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Message", "Message");
            Sub_entry_EventDetailsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_EventDetailsTitle", "Event Details");
            Sub_entry_EventDetailsHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_EventDetailsHint", "Full detail of the selected event");
            Sub_entry_MachineStatusDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_MachineStatusLabel", "Machine Status");
            Sub_entry_DatabaseStatusDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DatabaseStatus", "Database Status");
            Sub_entry_VisionProStatusDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_VisionProStatus", "VisionPro Status");
            Sub_entry_OperatorRoleLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OperatorRole", "Operator / Role");
            Sub_entry_TimestampDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Timestamp", "Timestamp");
            Sub_entry_CategoryDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Category", "Category");
            Sub_entry_SourceDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Source", "Source");
            Sub_entry_RecipeDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeLabel", "Recipe");
            Sub_entry_MessageDetailLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Message", "Message");
            Sub_entry_DetailsReasonLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DetailsReason", "Details / Reason");
            Sub_entry_PayloadJsonLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_PayloadJson", "Payload JSON");
        }
    }
}
