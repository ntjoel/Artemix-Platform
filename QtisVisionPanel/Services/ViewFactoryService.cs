using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views;
using QtisVisionPanel.Views.UserControls;
using QtisVisionPanel.Views.UserControls.DisplayRecord;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public class NavigationTarget
    {
        public object View { get; set; }
        public string Title { get; set; }
    }

    public class ViewFactoryService
    {
        // CameraContainer is kept as a singleton across navigations so that the
        // CogRecordDisplay controls (and the last inspection images inside them)
        // survive when the user switches to another view and comes back.
        // The instance is created on first access and reused on every subsequent
        // "Channel1" navigation.
        private CameraContainer _cachedCameraContainer;

        private CameraContainer GetOrCreateCameraContainer()
        {
            if (_cachedCameraContainer == null)
                _cachedCameraContainer = new CameraContainer();
            return _cachedCameraContainer;
        }

        public async Task<NavigationTarget> CreateAsync(string viewName)
        {
            switch (viewName)
            {
                case "Channel1":
                    return new NavigationTarget
                    {
                        View = GetOrCreateCameraContainer(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CamerasViewTitle", "Cameras")
                    };
                case "Statistics":
                    return new NavigationTarget
                    {
                        View = new DataAnalysisView(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataAnalysisTitle", "Data Analysis")
                    };
                case "Counters":
                    return new NavigationTarget
                    {
                        View = new StatisticsView(),
                        Title = ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Counter_NAv
                    };
                case "Alarmsview":
                    return new NavigationTarget
                    {
                        View = new AlarmsView(),
                        Title = ServerMessagePersonalize.CurrentMessages.messages.sub_entry_alarms
                    };
                case "InspectionConfig":
                    return new NavigationTarget
                    {
                        View = new InspectionConfigView { DataContext = new InspectionConfigViewModel() },
                        Title = ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Ispection_Conf_NAv
                    };
                case "RecipeManager":
                    var recipeView = new RecipeManagerView();
                    if (App.ViewModelCache.RecipeManagerVM != null)
                    {
                        recipeView.DataContext = App.ViewModelCache.RecipeManagerVM;
                        await App.ViewModelCache.RecipeManagerVM.UpdatePermissionsAsync();
                    }
                    else
                    {
                        var recipeVm = new RecipeManagerViewModel();
                        await recipeVm.InitializeAsync();
                        recipeView.DataContext = recipeVm;
                    }

                    return new NavigationTarget
                    {
                        View = recipeView,
                        Title = ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_RecipeManager_NAv
                    };
                case "JobToolEditor":
                    var jobToolEditorView = new JobToolEditorView();
                    if (jobToolEditorView.DataContext is JobToolEditorViewModel jobVm)
                    {
                        jobVm.LoadToolLists();
                    }

                    return new NavigationTarget
                    {
                        View = jobToolEditorView,
                        Title = ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Tool_edit_NAv
                    };
                case "Setting":
                    var ioVm = App.ViewModelCache.DigitalIOVM ?? new DigitalIOViewModel();
                    var digitalIOControl = new DigitalIOControl { DataContext = ioVm };

                    return new NavigationTarget
                    {
                        View = digitalIOControl,
                        Title = ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Setting
                    };
                case "EjectionAlarms":
                    return new NavigationTarget
                    {
                        View = new EjectionAlarmControl(),
                        Title = ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Alarms_Rules
                    };
                case "Preferences":
                    return new NavigationTarget
                    {
                        View = new PreferenceView(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_PreferencesTitle", "General Preferences")
                    };
                case "DataInspector":
                    return new NavigationTarget
                    {
                        View = new DataInspectorView(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataInspectorTitle", "Data Inspector")
                    };
                case "SystemDiagnostics":
                    return new NavigationTarget
                    {
                        View = new SystemDiagnosticsView(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_PCDiagnosticsTitle", "PC Diagnostics")
                    };
                case "PowerFlex525":
                    return new NavigationTarget
                    {
                        View = new PowerFlex525View(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_PowerFlex525Title", "PowerFlex 525")
                    };
                case "OpcUaConfiguration":
                    return new NavigationTarget
                    {
                        View = new OpcUaConfigurationView(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OpcUaConfigurationTitle", "OPC UA Communication")
                    };
                case "Manual":
                    return new NavigationTarget
                    {
                        View = new ManualView(),
                        Title = ServerMessagePersonalize.CurrentMessages.messages.lbManual
                    };
                case "Assistance":
                    return new NavigationTarget
                    {
                        View = new AssistanceView(),
                        Title = ServerMessagePersonalize.CurrentMessages.messages.lbAssistance
                    };
                default:
                    return new NavigationTarget
                    {
                        View = GetOrCreateCameraContainer(),
                        Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CamerasViewTitle", "Cameras")
                    };
            }
        }
    }
}
