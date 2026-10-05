using QtisVisionPanel.Services;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.ServerMessage;
using System.Windows;
using System.Windows.Controls;

namespace QtisVisionPanel.Views.UserControls
{
    public partial class SystemDiagnosticsView : UserControl
    {
        private readonly SystemDiagnosticsViewModel _viewModel;

        public SystemDiagnosticsView()
        {
            InitializeComponent();
            LoadServerMessages();

            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }

            _viewModel = new SystemDiagnosticsViewModel();
            DataContext = _viewModel;
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Dispose();
        }

        private void LoadServerMessages()
        {
            Sub_entry_PCDiagnosticsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_PCDiagnosticsTitle", "PC Diagnostics");
            Sub_entry_PCDiagnosticsSubtitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_PCDiagnosticsSubtitle", "CPU, RAM, mounted disks, archive retention and automatic cleanup control");
            Sub_entry_EventMonitorButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_EventMonitorButton", "Event Monitor");
            Sub_entry_SystemDiagnosticsRefreshButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Refresh", "Refresh");
            Sub_entry_OverallHealthLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OverallHealth", "Overall health");
            Sub_entry_CpuLoadLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CpuLoad", "CPU load");
            Sub_entry_RamUsageLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RamUsage", "RAM usage");
            Sub_entry_ActiveAlertsLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ActiveAlerts", "Active alerts");
            Sub_entry_DatabaseArchiveTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DatabaseArchiveTitle", "Database archive");
            Sub_entry_HottestDiskTemperatureTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_HottestDiskTemperature", "Hottest disk temperature");
            Sub_entry_CpuTemperatureTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CpuTemperature", "CPU temperature");
            Sub_entry_MemoryTemperatureTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_SystemTemperature", "System / memory temperature");
            Sub_entry_MountedDisksTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_MountedDisks", "Mounted disks");
            Sub_entry_ActivePcNotificationsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ActivePcNotifications", "Active PC notifications");
            Sub_entry_ActivePcNotificationsHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ActivePcNotificationsHint", "Critical and warning diagnostics are logged into tbllogevent");
            Sub_entry_CleanupFoldersTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CleanupFolders", "Cleanup folders");
            Sub_entry_CleanupPolicyTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CleanupPolicy", "Cleanup policy");
            Sub_entry_DatabaseArchivePolicyTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DatabaseArchivePolicyTitle", "Database archive policy");
            Sub_entry_TemperatureSensorsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_TemperatureSensorsTitle", "Temperature sensors");

            Sub_entry_AiControlsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiControlsTitle", "AI controls");
            Sub_entry_AiControlsHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiControlsHint", "Enable and configure advisory AI services. Saving is reserved to Installer/Administrator.");
            Sub_entry_AiReloadSettingsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Reload", "Reload");
            Sub_entry_AiSaveSettingsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Save", "Save");
            Sub_entry_AiServicesTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiServicesTitle", "Services");
            Sub_entry_AiDataFoundationCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiDataFoundation", "Data foundation");
            Sub_entry_AiSpcDriftCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiSpcDrift", "SPC / drift detection");
            Sub_entry_AiPredictiveMaintenanceCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiPredictiveMaintenance", "Predictive maintenance");
            Sub_entry_AiTrainingCollectionCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainingCollection", "Training data collection");
            Sub_entry_AiTimingOptimizerCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTimingOptimizer", "I/O timing optimizer");
            Sub_entry_AiPerformanceMonitorCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiPerformanceMonitor", "AI performance monitor");
            Sub_entry_AiRecipeAdvisorCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiRecipeAdvisor", "Product/recipe advisor");
            Sub_entry_AiHealthNotificationsCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiHealthNotifications", "Machine health notifications");
            Sub_entry_AiHealthDependencyHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiHealthDependencyHint", "Early warnings need SPC and/or predictive maintenance enabled. Critical warnings are sent immediately; the rest is sent as a digest.");
            Sub_entry_AiHealthDigestMinutesLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiHealthDigestMinutes", "Digest (min)");
            Sub_entry_AiHealthCriticalEtaLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiHealthCriticalEta", "Critical ETA (pcs)");
            Sub_entry_AiHealthCriticalDiskLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiHealthCriticalDisk", "Critical disk (hours)");
            Sub_entry_AiEmailChannelTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailChannelTitle", "Email / SMTP");
            Sub_entry_AiEmailEnabledCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Enabled", "Enabled");
            Sub_entry_AiEmailHostLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailHost", "SMTP host");
            Sub_entry_AiEmailPortLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailPort", "Port");
            Sub_entry_AiEmailUseSslCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailUseSsl", "Use SSL/TLS");
            Sub_entry_AiEmailFromLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailFrom", "From address");
            Sub_entry_AiEmailToLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailTo", "To (';' or ',' separated)");
            Sub_entry_AiEmailUsernameLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailUsername", "SMTP username");
            Sub_entry_AiEmailPasswordLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailPassword", "SMTP password");
            Sub_entry_AiTestEmailButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTestEmail", "Invia email di prova");
            Sub_entry_AiEmailHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEmailHint", "Password saved in plain text in the machine configuration file, like other credentials already stored there. The test button uses the values currently typed even if not yet saved.");
            // Intestazioni dei singoli classificatori: vengono dalle card camera del ViewModel
            // (nome come nel VPP), qui resta solo il titolo della colonna.
            Sub_entry_AiOnnxClassifierTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxClassifierTitle", "ONNX classifiers per camera (shadow-mode)");
            Sub_entry_AiOnnxEnabledCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Enabled", "Enabled");
            Sub_entry_AiOnnxModelPathLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxModelPath", "Model path (.onnx)");
            Sub_entry_AiOnnxModelVersionLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxModelVersion", "Model version");
            Sub_entry_AiOnnxModelNotesLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxModelNotes", "Model notes");
            Sub_entry_AiOnnxInputWidthLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxInputWidth", "Resize width");
            Sub_entry_AiOnnxInputHeightLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxInputHeight", "Resize height");
            Sub_entry_AiOnnxNormalizeMeanLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxNormalizeMean", "Norm. mean");
            Sub_entry_AiOnnxNormalizeStdLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxNormalizeStd", "Norm. std.dev.");
            Sub_entry_AiOnnxGrayscaleCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxGrayscale", "Grayscale");
            Sub_entry_AiOnnxInputSizeHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxInputSizeHint", "These values are model resize dimensions, not native camera resolution. Each camera uses its own model and preprocessing.");
            Sub_entry_AiTopMinConfidenceLabel.Text = Sub_entry_AiSideMinConfidenceLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiMinNokConfidence", "Minimum NOK confidence (0-1, 0=off)");
            Sub_entry_AiMinConfidenceHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiMinNokConfidenceHint", "Above 0, a lower-confidence NOK becomes uncertain and is excluded from defects. Uncertain results remain visible in shadow statistics.");
            Sub_entry_AiTrainingRuntimeTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainingRuntimeTitle", "Training runtime");
            Sub_entry_AiTrainingPythonPathLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainingPythonPath", "Python executable");
            Sub_entry_AiTrainingOutputDirLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainingOutputDir", "Model output folder");
            Sub_entry_AiTrainingEpochsLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainingEpochs", "Epochs");
            Sub_entry_AiTestPythonButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTestPython", "Test Python");
            Sub_entry_AiTrainModelButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainModel", "Train model");
            Sub_entry_AiCancelTrainingButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Cancel", "Cancel");
            Sub_entry_AiTrainModelHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainModelHint", "Train from collected label.json dataset and create defect_classifier_vN.onnx in the model folder. Requires Python (torch, pillow, numpy, onnx) and stopped machine. The model is not enabled automatically: verify fields and press Save.");
            Sub_entry_AiSideEnabledCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Enabled", "Enabled");
            Sub_entry_AiSideModelPathLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiSideModelPath", "SIDE / LEFT model path (.onnx)");
            Sub_entry_AiSideModelVersionLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxModelVersion", "Model version");
            Sub_entry_AiSideModelNotesLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxModelNotes", "Model notes");
            Sub_entry_AiSideInputWidthLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxInputWidth", "Resize width");
            Sub_entry_AiSideInputHeightLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxInputHeight", "Resize height");
            Sub_entry_AiSideNormalizeMeanLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxNormalizeMean", "Norm. mean");
            Sub_entry_AiSideNormalizeStdLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxNormalizeStd", "Norm. std.dev.");
            Sub_entry_AiSideGrayscaleCheck.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxGrayscale", "Grayscale");
            Sub_entry_AiTrainSideModelButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainSideModel", "Train SIDE / LEFT model");
            Sub_entry_AiTrainSideModelHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTrainSideModelHint", "Creates defect_classifier_side_vN.onnx from image _F_ / _L_ and labels.side. Stop production first, then verify the model and press Save to enable shadow-mode.");
            Sub_entry_AiRetentionTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiRetentionTitle", "AI data retention (days, 0 = unlimited)");
            Sub_entry_AiRetentionInspectionLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiRetentionInspection", "Inspection measurements");
            Sub_entry_AiRetentionHealthLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiRetentionHealth", "PC health snapshots");
            Sub_entry_AiRetentionTrainingLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiRetentionTraining", "Training samples");
            Sub_entry_AiEarlyWarningsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEarlyWarningsTitle", "Machine health early warnings");
            Sub_entry_AiEarlyWarningsHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiEarlyWarningsHint", "Inspection and PC health drift detected before it becomes scrap or downtime. Advisory only: it does not stop the machine.");
            Sub_entry_AiRefreshEarlyWarningsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Refresh", "Refresh");
            Sub_entry_AiNoEarlyWarningsText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiNoEarlyWarnings", "No early warnings recorded.");
            Sub_entry_AiShadowStatsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowStatsTitle", "AI shadow-mode statistics");
            Sub_entry_AiShadowStatsHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowStatsHint", "ONNX comparison with VisionPro rules as reference. Moving window, advisory only.");
            Sub_entry_AiRefreshShadowStatsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Refresh", "Refresh");
            Sub_entry_AiResetShadowStatsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiResetShadowStats", "Reset");
            Sub_entry_AiNoShadowStatsText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiNoShadowStats", "No data: enable an ONNX classifier and inspect both OK and NOK products.");
            Sub_entry_AiTopShadowCameraLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiTopShadowCamera", "TOP camera (image _T_)");
            Sub_entry_AiSideShadowCameraLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiSideShadowCamera", "SIDE / LEFT camera (image _F_ / _L_)");
            Sub_entry_AiTopShadowAgreementLabel.Text = Sub_entry_AiSideShadowAgreementLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowAgreement", "Agreement");
            Sub_entry_AiTopShadowNokRecallLabel.Text = Sub_entry_AiSideShadowNokRecallLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowNokRecall", "Defect recall (NOK)");
            Sub_entry_AiTopShadowNokPrecisionLabel.Text = Sub_entry_AiSideShadowNokPrecisionLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowNokPrecision", "NOK precision");
            Sub_entry_AiTopShadowFalseAlarmsLabel.Text = Sub_entry_AiSideShadowFalseAlarmsLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowFalseAlarms", "False alarms");
            Sub_entry_AiTopShadowMeanConfidenceLabel.Text = Sub_entry_AiSideShadowMeanConfidenceLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowMeanConfidence", "Mean confidence");
            Sub_entry_AiShadowMetricHint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowMetricHint", "Defect recall shows how many real defects the model catches. NOK precision shows how often a NOK prediction is correct.");
        }
    }
}
