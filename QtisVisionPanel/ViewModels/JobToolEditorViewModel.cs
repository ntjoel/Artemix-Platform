// JobToolEditorViewModel.cs
using Cognex.VisionPro;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.QuickBuild.Implementation.Internal;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using Cognex.VisionProUI.Controls.Internal.Utils;
using QtisVisionPanel.Cls_Vpro;
using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// Describes one editable VisionPro job inside the Job Tool Editor.
    ///
    /// The production baseline no longer assumes a fixed Top/Side pair:
    /// the editor now exposes the real job list loaded from the active VPP.
    /// </summary>
    public sealed class JobToolEditorPanelItem : INotifyPropertyChanged
    {
        private string _selectedTool;

        public JobToolEditorPanelItem(int jobIndex, string jobName, string cameraRole)
        {
            JobIndex = jobIndex;
            JobName = string.IsNullOrWhiteSpace(jobName) ? $"Job {jobIndex}" : jobName.Trim();
            CameraRole = CameraConfigurationHelper.NormalizeCameraDisplayType(cameraRole);
        }

        public int JobIndex { get; }
        public string JobName { get; }
        public string CameraRole { get; }
        public ObservableCollection<string> Tools { get; } = new ObservableCollection<string>();

        /// <summary>
        /// The tab header mirrors the active QuickBuild job name so the operator
        /// sees the same semantic identifier both in HMI and in VisionPro.
        /// </summary>
        public string Header => JobName.ToUpperInvariant();

        public string ListTitle => Header;

        public string LiveDisplayTitle
        {
            get
            {
                if (CameraConfigurationHelper.IsSupportedCameraRole(CameraRole))
                {
                    return $"Live Preview - {CameraRole.ToUpperInvariant()} Camera";
                }

                return $"Live Preview - {JobName}";
            }
        }

        public string SelectedTool
        {
            get => _selectedTool;
            set
            {
                if (_selectedTool != value)
                {
                    _selectedTool = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Runtime-driven tool editor viewmodel.
    ///
    /// This editor now discovers editable jobs directly from the active QuickBuild
    /// manager instead of exposing only the historical Top/Side pair.
    /// </summary>
    public class JobToolEditorViewModel : INotifyPropertyChanged
    {
        private bool _isLoading;
        private bool _hasChanges;
        private LiveDisplayWindow _liveDisplayWindow;
        private bool _liveVisionProDisplayStarted;
        private bool _resumeContinuousRunAfterLive;
        private int _liveJobIndex = -1;
        private CancellationTokenSource _liveIoCts;
        private LiveCameraTriggerBinding _liveTriggerBinding;
        private int _livePreviewIntervalMs = 1000;
        private int _livePreviewPulseMs = 20;
        private string _livePreviewExposureUsText = string.Empty;
        private string _livePreviewExposureStatus = string.Empty;
        private string _livePreviewCurrentExposureText = string.Empty;
        private string _top3DDetectionSensitivityText = string.Empty;
        private string _top3DDetectionSensitivityStatus = string.Empty;
        private string _top3DCurrentDetectionSensitivityText = string.Empty;
        private readonly SemaphoreSlim _livePreviewRunLock = new SemaphoreSlim(1, 1);
        private bool _isLoadingLivePreviewSettings;
        private bool _isRestoringAfterLive;
        private string _livePreviewDisplaySource;
        private bool _livePreviewRawSourceWarningLogged;
        private bool _isToolLoading;
        private bool _isSaveEnabled = true;
        private int _selectedTabIndex;
        private bool _isSyncingSelectedTabIndex;
        private JobToolEditorPanelItem _selectedJobPanel;
        private string _selectToolPromptText;
        private string _editorHoldBannerTitle;
        private string _editorHoldBannerMessage;
        private string _loadingStatusTitle;
        private string _loadingStatusMessage;
        private string _loadingStatusDetail;
        private int _loadingProgressValue;
        private bool _isLoadingIndeterminate;
        private bool _jobEditorRuntimeHoldActive;
        private bool _resumeContinuousRunAfterEditor;
        private bool _editorEnteredWithManualStop;
        private bool _isEditorViewUnloading;

        private sealed class LiveCameraTriggerBinding
        {
            public int Channel { get; set; }

            public bool ActiveElectricalState { get; set; }

            public bool InactiveElectricalState { get; set; }

            public string SignalCode { get; set; }

            public string Board { get; set; }

            public string PhysicalChannel { get; set; }
        }

        private sealed class LivePreviewDisplayFrame
        {
            public ICogRecord Record { get; set; }

            public ICogImage Image { get; set; }

            public bool IsRawAcquisition { get; set; }

            public string Source { get; set; }
        }

        public bool IsAdministrator => UserSession.IsAdministrator;

        public bool IsTechnicalUser => UserSession.IsAdministrator || UserSession.IsInstaller || UserSession.IsExpert;

        public ObservableCollection<JobToolEditorPanelItem> JobPanels { get; } = new ObservableCollection<JobToolEditorPanelItem>();

        public JobToolEditorPanelItem SelectedJobPanel
        {
            get => _selectedJobPanel;
            set
            {
                if (!ReferenceEquals(_selectedJobPanel, value))
                {
                    _selectedJobPanel = value;
                    SyncSelectedTabIndexFromPanel(value);
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();

                    if (IsLiveRunning)
                    {
                        StopLiveDisplay();
                    }

                    RefreshLivePreviewExposureFromSelectedJob();
                    OnPropertyChanged(nameof(IsTop3DPanelSelected));
                    RefreshTop3DDetectionSensitivityFromSelectedJob();
                }
            }
        }

        public string SelectToolPromptText
        {
            get => _selectToolPromptText;
            private set
            {
                if (_selectToolPromptText != value)
                {
                    _selectToolPromptText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string EditorHoldBannerTitle
        {
            get => _editorHoldBannerTitle;
            private set
            {
                if (_editorHoldBannerTitle != value)
                {
                    _editorHoldBannerTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        public string EditorHoldBannerMessage
        {
            get => _editorHoldBannerMessage;
            private set
            {
                if (_editorHoldBannerMessage != value)
                {
                    _editorHoldBannerMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LoadingStatusTitle
        {
            get => _loadingStatusTitle;
            private set
            {
                if (_loadingStatusTitle != value)
                {
                    _loadingStatusTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LoadingStatusMessage
        {
            get => _loadingStatusMessage;
            private set
            {
                if (_loadingStatusMessage != value)
                {
                    _loadingStatusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LoadingStatusDetail
        {
            get => _loadingStatusDetail;
            private set
            {
                if (_loadingStatusDetail != value)
                {
                    _loadingStatusDetail = value;
                    OnPropertyChanged();
                }
            }
        }

        public int LoadingProgressValue
        {
            get => _loadingProgressValue;
            set
            {
                if (_loadingProgressValue != value)
                {
                    _loadingProgressValue = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LoadingProgressText));
                }
            }
        }

        public bool IsLoadingIndeterminate
        {
            get => _isLoadingIndeterminate;
            private set
            {
                if (_isLoadingIndeterminate != value)
                {
                    _isLoadingIndeterminate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LoadingProgressText));
                }
            }
        }

        public string LoadingProgressText =>
            IsLoadingIndeterminate
                ? GetMessage("Sub_entry_OperationPleaseWait", "Please wait...")
                : $"{Math.Max(0, Math.Min(100, LoadingProgressValue))}%";

        public bool IsToolLoading
        {
            get => _isToolLoading;
            set
            {
                _isToolLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSaveEnabled));
            }
        }

        public bool IsSaveEnabled
        {
            get => _isSaveEnabled && !_isToolLoading;
            set
            {
                _isSaveEnabled = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            set
            {
                _hasChanges = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
            }
        }

        public bool CanSave => HasChanges && IsTechnicalUser;

        /// <summary>
        /// Exposes whether the editor entered its protected stopped state from
        /// an originally running machine and should therefore try to restore
        /// continuous execution when the operator leaves the editor.
        /// </summary>
        public bool ShouldResumeContinuousRunAfterEditor => _resumeContinuousRunAfterEditor;

        private bool _isLiveRunning;
        public bool IsLiveRunning
        {
            get => _isLiveRunning;
            set
            {
                _isLiveRunning = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public int LivePreviewIntervalMs
        {
            get => _livePreviewIntervalMs;
            set
            {
                int clamped = Math.Max(100, Math.Min(30000, value));
                if (_livePreviewIntervalMs != clamped)
                {
                    _livePreviewIntervalMs = clamped;
                    OnPropertyChanged();
                    PersistLivePreviewSettingsToRuntimeConfig("interval changed");
                }
            }
        }

        public int LivePreviewPulseMs
        {
            get => _livePreviewPulseMs;
            set
            {
                int clamped = Math.Max(1, Math.Min(5000, value));
                if (_livePreviewPulseMs != clamped)
                {
                    _livePreviewPulseMs = clamped;
                    OnPropertyChanged();
                    PersistLivePreviewSettingsToRuntimeConfig("pulse changed");
                }
            }
        }

        public string LivePreviewExposureUsText
        {
            get => _livePreviewExposureUsText;
            set
            {
                if (_livePreviewExposureUsText != value)
                {
                    _livePreviewExposureUsText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LivePreviewExposureStatus
        {
            get => _livePreviewExposureStatus;
            private set
            {
                if (_livePreviewExposureStatus != value)
                {
                    _livePreviewExposureStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LivePreviewCurrentExposureText
        {
            get => _livePreviewCurrentExposureText;
            private set
            {
                if (_livePreviewCurrentExposureText != value)
                {
                    _livePreviewCurrentExposureText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// True quando il pannello selezionato e' la testa 3D (ruolo "top3d"): la sezione
        /// "Sensore 3D" della UI (detection sensitivity) e' visibile solo in questo caso.
        /// </summary>
        public bool IsTop3DPanelSelected =>
            SelectedJobPanel != null &&
            string.Equals(
                CameraConfigurationHelper.NormalizeCameraType(SelectedJobPanel.CameraRole),
                "top3d",
                StringComparison.OrdinalIgnoreCase);

        public string Top3DDetectionSensitivityText
        {
            get => _top3DDetectionSensitivityText;
            set
            {
                if (_top3DDetectionSensitivityText != value)
                {
                    _top3DDetectionSensitivityText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Top3DDetectionSensitivityStatus
        {
            get => _top3DDetectionSensitivityStatus;
            private set
            {
                if (_top3DDetectionSensitivityStatus != value)
                {
                    _top3DDetectionSensitivityStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Top3DCurrentDetectionSensitivityText
        {
            get => _top3DCurrentDetectionSensitivityText;
            private set
            {
                if (_top3DCurrentDetectionSensitivityText != value)
                {
                    _top3DCurrentDetectionSensitivityText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Preserved for compatibility with the current view flow.
        /// The selected index is now synchronized with the selected runtime panel.
        /// </summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();

                    if (!_isSyncingSelectedTabIndex &&
                        value >= 0 &&
                        value < JobPanels.Count)
                    {
                        _isSyncingSelectedTabIndex = true;
                        SelectedJobPanel = JobPanels[value];
                        _isSyncingSelectedTabIndex = false;
                    }
                }
            }
        }

        public ICommand OpenInQuickBuildCommand { get; private set; }
        public ICommand SaveCommand { get; set; }
        public ICommand RunOnceCommand { get; set; }
        public ICommand LiveViewCommand { get; set; }
        public ICommand ApplyLivePreviewExposureCommand { get; private set; }
        public ICommand RefreshLivePreviewExposureCommand { get; private set; }
        public ICommand ApplyTop3DDetectionSensitivityCommand { get; private set; }
        public ICommand RefreshTop3DDetectionSensitivityCommand { get; private set; }
        public ICommand RefreshToolsCommand { get; set; }
        public ICommand StopLiveDisplayCommand { get; private set; }

        public event Action<JobToolEditorPanelItem, string, CogToolBlock> ToolBlockLoaded;

        public JobToolEditorViewModel()
        {
            RefreshLocalizedTexts();
            LoadLivePreviewSettingsFromRuntimeConfig();
            InitializeCommands();
            LoadToolLists();
        }

        public void RefreshLocalizedTexts()
        {
            SelectToolPromptText = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_SelectToolFromList",
                "Select a tool from the list on the right");
            EditorHoldBannerTitle = GetMessage("Sub_entry_JobEditorHoldBannerTitle", "Machine stopped for job editing");
            EditorHoldBannerMessage = GetMessage("Sub_entry_JobEditorHoldBannerMessage", "The VisionPro runtime is held while tools are edited. Save the changes or leave the page to restore production run when allowed.");
            LoadingStatusTitle = GetMessage("Sub_entry_OperationLoadingToolTitle", "Loading tool");
            LoadingStatusMessage = GetMessage("Sub_entry_OperationPreparing", "Preparing operation...");
            LoadingStatusDetail = GetMessage("Sub_entry_JobEditorLoadingTool", "Preparing the VisionPro tool editor.");
            LoadingProgressValue = 0;
            IsLoadingIndeterminate = false;
        }

        private void InitializeCommands()
        {
            SaveCommand = new RelayCommand(async _ => await SaveJobAsync(), _ => CanSave);
            RunOnceCommand = new RelayCommand(
                _ => RunOnce(),
                _ => SelectedJobPanel != null && IsTechnicalUser && !IsLiveRunning);
            LiveViewCommand = new RelayCommand(_ => LiveView(), _ => !IsLiveRunning && SelectedJobPanel != null);
            ApplyLivePreviewExposureCommand = new RelayCommand(_ => ApplyLivePreviewExposure(), _ => SelectedJobPanel != null && IsTechnicalUser);
            RefreshLivePreviewExposureCommand = new RelayCommand(_ => RefreshLivePreviewExposureFromSelectedJob(), _ => SelectedJobPanel != null && IsTechnicalUser);
            ApplyTop3DDetectionSensitivityCommand = new RelayCommand(_ => ApplyTop3DDetectionSensitivity(), _ => IsTop3DPanelSelected && IsTechnicalUser);
            RefreshTop3DDetectionSensitivityCommand = new RelayCommand(_ => RefreshTop3DDetectionSensitivityFromSelectedJob(), _ => IsTop3DPanelSelected && IsTechnicalUser);
            RefreshToolsCommand = new RelayCommand(_ => LoadToolLists());
            OpenInQuickBuildCommand = new RelayCommand(_ => OpenInQuickBuild(), _ => CanOpenInQuickBuild());
            StopLiveDisplayCommand = new RelayCommand(_ => StopLiveDisplay(), _ => IsLiveRunning);
        }

        private void LoadLivePreviewSettingsFromRuntimeConfig()
        {
            _isLoadingLivePreviewSettings = true;
            try
            {
                var configuration = new MachineConfigurationService().Load();
                var bindings = configuration?.RuntimeBindings;
                if (bindings == null)
                {
                    return;
                }

                LivePreviewIntervalMs = bindings.LivePreviewIntervalMs > 0
                    ? bindings.LivePreviewIntervalMs
                    : 1000;
                LivePreviewPulseMs = bindings.LivePreviewPulseMs > 0
                    ? bindings.LivePreviewPulseMs
                    : 20;
                if (bindings.LivePreviewExposureUs > 0.0)
                {
                    LivePreviewExposureUsText = bindings.LivePreviewExposureUs.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Unable to load live preview settings: {ex.Message}");
            }
            finally
            {
                _isLoadingLivePreviewSettings = false;
            }
        }

        private void PersistLivePreviewSettingsToRuntimeConfig(string reason)
        {
            if (_isLoadingLivePreviewSettings)
            {
                return;
            }

            try
            {
                var service = new MachineConfigurationService();
                var configuration = service.Load();
                if (configuration?.RuntimeBindings == null)
                {
                    return;
                }

                configuration.RuntimeBindings.LivePreviewIntervalMs = _livePreviewIntervalMs;
                configuration.RuntimeBindings.LivePreviewPulseMs = _livePreviewPulseMs;
                if (TryParseLivePreviewExposure(out double exposureUs))
                {
                    configuration.RuntimeBindings.LivePreviewExposureUs = exposureUs;
                }
                service.Save(configuration);

                MainWindow.logger?.Info(
                    $"LIVE_IO|Live preview settings saved ({reason}): interval={_livePreviewIntervalMs}ms pulse={_livePreviewPulseMs}ms.");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Unable to save live preview settings ({reason}): {ex.Message}");
            }
        }

        /// <summary>
        /// Rebuilds the editor tabs from the active QuickBuild job manager.
        /// Each editable job becomes one runtime panel regardless of whether the
        /// role is Top, Side, Front, or another supported semantic camera role.
        /// </summary>
        public void LoadToolLists()
        {
            try
            {
                Dictionary<int, string> previousSelections = JobPanels.ToDictionary(
                    panel => panel.JobIndex,
                    panel => panel.SelectedTool);

                int previouslySelectedJobIndex = SelectedJobPanel?.JobIndex ?? -1;

                foreach (var panel in JobPanels)
                {
                    panel.PropertyChanged -= JobPanel_PropertyChanged;
                }

                JobPanels.Clear();
                SelectedJobPanel = null;

                if (MainWindow._cognexManager == null || MainWindow._cognexManager.JobCount <= 0)
                {
                    MainWindow.logger?.Warn("Job Tool Editor: nessun job manager attivo disponibile.");
                    return;
                }

                for (int jobIndex = 0; jobIndex < MainWindow._cognexManager.JobCount; jobIndex++)
                {
                    var panel = BuildPanelForJob(jobIndex, previousSelections);
                    if (panel == null)
                    {
                        continue;
                    }

                    panel.PropertyChanged += JobPanel_PropertyChanged;
                    JobPanels.Add(panel);
                }

                if (JobPanels.Count == 0)
                {
                    MainWindow.logger?.Warn("Job Tool Editor: nessun job editabile trovato nel QuickBuild attivo.");
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }

                SelectedJobPanel = previouslySelectedJobIndex >= 0
                    ? JobPanels.FirstOrDefault(panel => panel.JobIndex == previouslySelectedJobIndex) ?? JobPanels.First()
                    : JobPanels.First();

                MainWindow.logger?.Info(
                    $"Job Tool Editor: caricati {JobPanels.Count} job runtime -> {string.Join(", ", JobPanels.Select(panel => panel.JobName))}");
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento dinamico dei job tool editor: {ex.Message}");
            }
        }

        private JobToolEditorPanelItem BuildPanelForJob(int jobIndex, IDictionary<int, string> previousSelections)
        {
            try
            {
                var job = MainWindow._cognexManager?.GetJob(jobIndex);
                var toolGroup = job?.VisionTool as CogToolGroup;
                if (toolGroup == null)
                {
                    MainWindow.logger?.Warn($"Job Tool Editor: job index {jobIndex} non espone un CogToolGroup editabile.");
                    return null;
                }

                string jobName = ResolveJobName(jobIndex, job);
                string role = ResolveJobRole(jobIndex, jobName);
                var panel = new JobToolEditorPanelItem(jobIndex, jobName, role);

                foreach (CogToolBlock tool in toolGroup.Tools.OfType<CogToolBlock>().OrderBy(tool => tool.Name))
                {
                    panel.Tools.Add(tool.Name);
                }

                if (previousSelections != null &&
                    previousSelections.TryGetValue(jobIndex, out string previousTool) &&
                    panel.Tools.Contains(previousTool))
                {
                    panel.SelectedTool = previousTool;
                }

                return panel;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore creazione panel tool editor per job {jobIndex}: {ex.Message}");
                return null;
            }
        }

        private static string ResolveJobName(int jobIndex, CogJob job)
        {
            if (!string.IsNullOrWhiteSpace(job?.Name))
            {
                return job.Name;
            }

            if (MainWindow.JobMapping != null &&
                MainWindow.JobMapping.TryGetValue(jobIndex, out string mappedName) &&
                !string.IsNullOrWhiteSpace(mappedName))
            {
                return mappedName;
            }

            return $"Job {jobIndex}";
        }

        private static string ResolveJobRole(int jobIndex, string jobName)
        {
            string roleFromJobName = CameraConfigurationHelper.NormalizeCameraDisplayType(jobName);
            if (CameraConfigurationHelper.IsSupportedCameraRole(roleFromJobName))
            {
                return roleFromJobName;
            }

            if (MainWindow.JobRoleMapping != null &&
                MainWindow.JobRoleMapping.TryGetValue(jobIndex, out string configuredRole) &&
                !string.IsNullOrWhiteSpace(configuredRole))
            {
                return CameraConfigurationHelper.NormalizeCameraDisplayType(configuredRole);
            }

            return roleFromJobName;
        }

        private void JobPanel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(JobToolEditorPanelItem.SelectedTool))
            {
                return;
            }

            var panel = sender as JobToolEditorPanelItem;
            if (panel == null ||
                !ReferenceEquals(panel, SelectedJobPanel) ||
                string.IsNullOrWhiteSpace(panel.SelectedTool))
            {
                return;
            }

            LoadJobToolEditorAsync(panel, panel.SelectedTool).SafeFireAndForget();
        }

        private void SyncSelectedTabIndexFromPanel(JobToolEditorPanelItem panel)
        {
            if (panel == null)
            {
                return;
            }

            int index = JobPanels.IndexOf(panel);
            if (index < 0 || index == _selectedTabIndex)
            {
                return;
            }

            _isSyncingSelectedTabIndex = true;
            _selectedTabIndex = index;
            OnPropertyChanged(nameof(SelectedTabIndex));
            _isSyncingSelectedTabIndex = false;
        }

        public Task ReloadSelectedToolAsync()
        {
            if (SelectedJobPanel == null || string.IsNullOrWhiteSpace(SelectedJobPanel.SelectedTool))
            {
                return Task.CompletedTask;
            }

            return LoadJobToolEditorAsync(SelectedJobPanel, SelectedJobPanel.SelectedTool);
        }

        public async Task LoadJobToolEditorAsync(JobToolEditorPanelItem panel, string toolName)
        {
            if (panel == null || string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            try
            {
                IsToolLoading = true;
                IsLoading = true;
                SetLoadingState(
                    GetMessage("Sub_entry_OperationLoadingToolTitle", "Loading tool"),
                    string.Format(
                        GetMessage("Sub_entry_OperationLoadingToolFormat", "Opening {0} on {1}"),
                        toolName,
                        panel.Header),
                    GetMessage("Sub_entry_OperationPreparing", "Preparing operation..."),
                    10);

                await EnterEditorPauseAsync();
                SetLoadingState(
                    GetMessage("Sub_entry_OperationLoadingToolTitle", "Loading tool"),
                    string.Format(
                        GetMessage("Sub_entry_OperationLoadingToolFormat", "Opening {0} on {1}"),
                        toolName,
                        panel.Header),
                    GetMessage("Sub_entry_OperationStoppingRuntime", "Stopping continuous run..."),
                    35);
                await StopContinuousRunAsync();
                SetLoadingState(
                    GetMessage("Sub_entry_OperationLoadingToolTitle", "Loading tool"),
                    string.Format(
                        GetMessage("Sub_entry_OperationLoadingToolFormat", "Opening {0} on {1}"),
                        toolName,
                        panel.Header),
                    GetMessage("Sub_entry_OperationReadingRuntime", "Reading runtime resources..."),
                    65);

                var toolBlock = await GetToolBlockByNameAsync(toolName, panel.JobIndex);
                if (toolBlock != null)
                {
                    SetLoadingState(
                        GetMessage("Sub_entry_OperationLoadingToolTitle", "Loading tool"),
                        string.Format(
                            GetMessage("Sub_entry_OperationLoadingToolFormat", "Opening {0} on {1}"),
                            toolName,
                            panel.Header),
                        GetMessage("Sub_entry_OperationOpeningEditor", "Opening editor..."),
                        85);
                    ToolBlockLoaded?.Invoke(panel, toolName, toolBlock);
                    SetLoadingState(
                        GetMessage("Sub_entry_OperationLoadingToolTitle", "Loading tool"),
                        string.Format(
                            GetMessage("Sub_entry_OperationLoadingToolFormat", "Opening {0} on {1}"),
                            toolName,
                            panel.Header),
                        GetMessage("Sub_entry_OperationCompleted", "Completed"),
                        100);
                    await Task.Delay(120);
                    MainWindow.logger?.Info($"Job Tool Editor: tool '{toolName}' caricato dal job '{panel.JobName}' (index {panel.JobIndex}).");
                }
                else
                {
                    new SystemNotificationWindow("Errore",
                        $"Tool '{toolName}' non trovato nel job '{panel.JobName}'",
                        NotificationSeverity.Error).ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento del tool '{toolName}' dal job '{panel.JobName}': {ex.Message}");
            }
            finally
            {
                IsToolLoading = false;
                IsLoading = false;
                ResetLoadingState();
            }
        }

        /// <summary>
        /// Entering the job editor is an intentional maintenance/editing state:
        /// continuous run must stay stopped until the operator saves or leaves
        /// the editor.
        /// </summary>
        public async Task EnterEditorPauseAsync()
        {
            _isEditorViewUnloading = false;

            if (_jobEditorRuntimeHoldActive)
            {
                return;
            }

            _resumeContinuousRunAfterEditor =
                ServiceLocator.MachineRuntimeService.IsContinuousRunActive ||
                ServiceLocator.MachineRuntimeService.IsContinuousRunRequested;
            _editorEnteredWithManualStop = ServiceLocator.MachineRuntimeService.IsContinuousRunHoldActive(
                MachineRuntimeService.HoldReasonManualStop);

            ServiceLocator.MachineRuntimeService.AddContinuousRunHold(
                MachineRuntimeService.HoldReasonJobEditor,
                nameof(JobToolEditorViewModel));

            _jobEditorRuntimeHoldActive = true;

            if (ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
            {
                await StopContinuousRunAsync(preserveRequestedState: true);
            }
        }

        /// <summary>
        /// Leaving the editor or completing a save releases the maintenance hold.
        /// If the machine was previously expected to run, it is re-armed
        /// automatically unless another intentional hold is still active.
        /// </summary>
        public async Task ExitEditorPauseAsync(bool resumeIfPossible)
        {
            bool releasedGlobalHold = ServiceLocator.MachineRuntimeService.ReleaseContinuousRunHold(
                MachineRuntimeService.HoldReasonJobEditor,
                nameof(JobToolEditorViewModel));

            bool hadLocalEditorPause = _jobEditorRuntimeHoldActive;
            bool manualStopActive = ServiceLocator.MachineRuntimeService.IsContinuousRunHoldActive(
                MachineRuntimeService.HoldReasonManualStop);
            bool shouldResume = resumeIfPossible &&
                                !manualStopActive &&
                                !_editorEnteredWithManualStop;

            _jobEditorRuntimeHoldActive = false;
            _resumeContinuousRunAfterEditor = false;
            _editorEnteredWithManualStop = false;

            if (!hadLocalEditorPause && !releasedGlobalHold)
            {
                return;
            }

            if (!shouldResume ||
                MainWindow._cognexManager == null ||
                ServiceLocator.MachineRuntimeService.HasContinuousRunHold ||
                ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
            {
                MainWindow.logger?.Info(
                    $"JOBEDITOR_EXIT_NO_AUTO_RESUME|shouldResume={shouldResume}|manualStopActive={manualStopActive}|hasHold={ServiceLocator.MachineRuntimeService.HasContinuousRunHold}|isRunning={ServiceLocator.MachineRuntimeService.IsContinuousRunActive}");
                return;
            }

            if (MainWindow._continuousRunCts == null || MainWindow._continuousRunCts.IsCancellationRequested)
            {
                MainWindow._continuousRunCts = new CancellationTokenSource();
            }

            MainWindow.logger?.Info("JOBEDITOR_EXIT_AUTO_RESUME|reason=editor-hold-released");
            await ServiceLocator.MachineRuntimeService.ManageJobStateAsync(true);
            MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
        }

        private async Task<CogToolBlock> GetToolBlockByNameAsync(string name, int jobIndex)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var job = MainWindow._cognexManager?.GetJob(jobIndex);
                    return job?.VisionTool is CogToolGroup group
                        ? group.Tools[name] as CogToolBlock
                        : null;
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Error($"Errore nell'accesso al tool '{name}' del job {jobIndex}: {ex.Message}");
                    return null;
                }
            });
        }

        private async Task StopContinuousRunAsync(bool preserveRequestedState = false)
        {
            if (ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
            {
                try
                {
                    await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync(preserveRequestedState);
                    MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;

                    int maxWaitTime = 2000;
                    int elapsed = 0;
                    while (ServiceLocator.MachineRuntimeService.IsContinuousRunActive && elapsed < maxWaitTime)
                    {
                        await Task.Delay(100);
                        elapsed += 100;
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn($"Errore nell'arresto dell'esecuzione continua: {ex.Message}");
                }
            }
        }

        private async Task SaveJobAsync()
        {
            QtisVisionPanel.OperationProgressWindow progressWindow = null;

            try
            {
                string recipeFolder = MainWindow.configManager.Config.Configuration.Recipe_Folder;
                string lastRecipe = MainWindow.configManager.Config.Configuration.LastRecipe;
                string filePath = System.IO.Path.Combine(recipeFolder, lastRecipe);

                if (string.IsNullOrEmpty(lastRecipe) || !System.IO.File.Exists(filePath))
                {
                    new SystemNotificationWindow("Errore", "Seleziona prima un job da salvare.", NotificationSeverity.Error).ShowDialog();
                    return;
                }

                progressWindow = CreateProgressWindow(
                    GetMessage("Sub_entry_OperationSavingToolTitle", "Saving tool changes"),
                    GetMessage("Sub_entry_OperationPreparing", "Preparing operation..."),
                    lastRecipe,
                    10);

                string backupFolder = MainWindow.configManager.Config.Configuration.BackupRecipe;
                if (!System.IO.Directory.Exists(backupFolder))
                {
                    System.IO.Directory.CreateDirectory(backupFolder);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFileName = System.IO.Path.GetFileNameWithoutExtension(lastRecipe) +
                                       "_" + timestamp + System.IO.Path.GetExtension(lastRecipe);
                string backupFilePath = System.IO.Path.Combine(backupFolder, backupFileName);

                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationSavingToolTitle", "Saving tool changes"),
                    GetMessage("Sub_entry_OperationCreatingBackup", "Creating backup..."),
                    backupFilePath,
                    35);
                System.IO.File.Copy(filePath, backupFilePath, overwrite: true);

                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationSavingToolTitle", "Saving tool changes"),
                    GetMessage("Sub_entry_OperationWritingFiles", "Writing files..."),
                    filePath,
                    70);
                await SaveJobManagerAsync(filePath);

                HasChanges = false;
                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationSavingToolTitle", "Saving tool changes"),
                    GetMessage("Sub_entry_OperationRestoringRun", "Restoring continuous run..."),
                    null,
                    90);
                await ExitEditorPauseAsync(true);
                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationSavingToolTitle", "Saving tool changes"),
                    GetMessage("Sub_entry_OperationCompleted", "Completed"),
                    null,
                    100);
                await Task.Delay(180);
                CloseProgressWindow(progressWindow);
                progressWindow = null;

                new SystemNotificationWindow("Successo",
                    $"Job salvato con successo!\nBackup creato in: {backupFilePath}",
                    NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                new SystemNotificationWindow("Errore",
                    $"Errore nel salvataggio: {ex.Message}",
                    NotificationSeverity.Error).ShowDialog();
            }
            finally
            {
                CloseProgressWindow(progressWindow);
            }
        }

        private async Task SaveJobManagerAsync(string filePath)
        {
            var jobManager = MainWindow._cognexManager?.GetJobManager();
            if (jobManager == null)
            {
                throw new InvalidOperationException("Job manager non disponibile");
            }

            bool wasRunning = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;

            if (wasRunning)
            {
                await StopContinuousRunAsync();
            }

            if (!ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
            {
                await Task.Run(() =>
                {
                    Cognex.VisionPro.CogSerializer.SaveObjectToFile(jobManager, filePath);
                });
                MainWindow.logger?.Info($"Job manager salvato in: {filePath}");
            }
            else
            {
                throw new InvalidOperationException("Impossibile salvare mentre il job è in esecuzione");
            }
        }

        private void RunOnce()
        {
            try
            {
                JobToolEditorPanelItem selectedPanel = SelectedJobPanel;
                if (selectedPanel == null)
                {
                    throw new InvalidOperationException("Selezionare un job VisionPro prima di eseguire RunOnce.");
                }

                ICognexJobManager manager = MainWindow._cognexManager;
                if (manager == null)
                {
                    throw new InvalidOperationException("VisionPro job manager non inizializzato.");
                }

                manager.RunOnce(selectedPanel.JobIndex, CancellationToken.None);
                MainWindow.logger?.Info(
                    $"VISIONPRO_RUN_ONCE_UI_COMPLETE|jobIndex={selectedPanel.JobIndex}|job={selectedPanel.JobName}");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, $"Errore nell'esecuzione singola: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nell'esecuzione: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        private void RefreshLivePreviewExposureFromSelectedJob()
        {
            try
            {
                var job = ResolveSelectedLivePreviewJob();
                if (job == null)
                {
                    LivePreviewExposureStatus = string.Empty;
                    LivePreviewCurrentExposureText = string.Empty;
                    return;
                }

                var exposureUs = IgigaCameraAccess.ReadExposureTimeUs(job, false);
                if (exposureUs.HasValue && exposureUs.Value > 0.0)
                {
                    LivePreviewExposureUsText = exposureUs.Value.ToString("0.###", CultureInfo.InvariantCulture);
                    LivePreviewCurrentExposureText = FormatLivePreviewCurrentExposure(exposureUs.Value);
                    LivePreviewExposureStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ExposureReadOk", "Exposure read from camera");
                }
                else if (string.IsNullOrWhiteSpace(LivePreviewExposureUsText))
                {
                    LivePreviewCurrentExposureText = FormatLivePreviewCurrentExposure(null);
                    LivePreviewExposureStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ExposureReadUnavailable", "Exposure read unavailable");
                }
            }
            catch (Exception ex)
            {
                LivePreviewCurrentExposureText = FormatLivePreviewCurrentExposure(null);
                LivePreviewExposureStatus = $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ExposureReadFailed", "Exposure read failed")}: {ex.Message}";
                MainWindow.logger?.Warn($"LIVE_IO|Exposure read failed: {ex.Message}");
            }
        }

        private void ApplyLivePreviewExposure()
        {
            try
            {
                var job = ResolveSelectedLivePreviewJob();
                if (job == null)
                {
                    LivePreviewExposureStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_NoSelectedVisionJob", "No selected VisionPro job");
                    return;
                }

                if (!TryParseLivePreviewExposure(out double exposureUs) || exposureUs <= 0.0)
                {
                    LivePreviewExposureStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_InvalidExposureValue", "Invalid exposure value");
                    return;
                }

                bool applied = IgigaCameraAccess.ApplyExposureTimeUs(job, exposureUs);
                if (applied)
                {
                    var appliedExposureUs = IgigaCameraAccess.ReadExposureTimeUs(job, false) ?? exposureUs;
                    LivePreviewExposureUsText = appliedExposureUs.ToString("0.###", CultureInfo.InvariantCulture);
                    LivePreviewCurrentExposureText = FormatLivePreviewCurrentExposure(appliedExposureUs);
                    LivePreviewExposureStatus = $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ExposureApplied", "Exposure applied")}: {appliedExposureUs:0.###} us";
                    PersistLivePreviewSettingsToRuntimeConfig("exposure changed");
                }
                else
                {
                    LivePreviewExposureStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ExposureApplyFailed", "Exposure apply failed");
                }
            }
            catch (Exception ex)
            {
                LivePreviewExposureStatus = $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ExposureApplyFailed", "Exposure apply failed")}: {ex.Message}";
                MainWindow.logger?.Warn($"LIVE_IO|Exposure apply failed: {ex.Message}");
            }
        }

        private static string FormatLivePreviewCurrentExposure(double? exposureUs)
        {
            string currentLabel = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CurrentExposure", "Current");
            return exposureUs.HasValue && exposureUs.Value > 0.0
                ? $"{currentLabel}: {exposureUs.Value:0.###} us"
                : $"{currentLabel}: --";
        }

        private bool TryParseLivePreviewExposure(out double exposureUs)
        {
            exposureUs = 0.0;
            string raw = LivePreviewExposureUsText;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            raw = raw.Trim().Replace(',', '.');
            return double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out exposureUs);
        }

        private CogJob ResolveSelectedLivePreviewJob()
        {
            if (MainWindow._cognexManager == null || SelectedJobPanel == null)
            {
                return null;
            }

            return MainWindow._cognexManager.GetJob(SelectedJobPanel.JobIndex);
        }

        // ---- Sensore 3D: Detection Sensitivity (solo pannello top3d) ----

        private static string ResolveDetectionSensitivityFeatureName()
        {
            var configured = MainWindow.configManager?.Config?.Configuration?.Top3DDetectionSensitivityFeature;
            return string.IsNullOrWhiteSpace(configured) ? "DetectionSensitivity" : configured;
        }

        private void RefreshTop3DDetectionSensitivityFromSelectedJob()
        {
            if (!IsTop3DPanelSelected)
            {
                Top3DDetectionSensitivityStatus = string.Empty;
                Top3DCurrentDetectionSensitivityText = string.Empty;
                return;
            }

            try
            {
                // Valore per-ricetta gia' salvato (se presente) mostrato nel campo editabile.
                double recipeValue = MainWindow.ConfigRecipeParam?.Config?.recipeParamTop3D?.Top3DDetectionSensitivity ?? 0;
                if (recipeValue > 0)
                {
                    Top3DDetectionSensitivityText = recipeValue.ToString("0.###", CultureInfo.InvariantCulture);
                }

                var job = ResolveSelectedLivePreviewJob();
                if (job == null)
                {
                    Top3DDetectionSensitivityStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_NoSelectedVisionJob", "No selected VisionPro job");
                    Top3DCurrentDetectionSensitivityText = FormatCurrentDetectionSensitivity(null);
                    return;
                }

                var current = IgigaCameraAccess.ReadDetectionSensitivity(job, ResolveDetectionSensitivityFeatureName());
                Top3DCurrentDetectionSensitivityText = FormatCurrentDetectionSensitivity(current);
                if (current.HasValue)
                {
                    if (string.IsNullOrWhiteSpace(Top3DDetectionSensitivityText))
                    {
                        Top3DDetectionSensitivityText = current.Value.ToString("0.###", CultureInfo.InvariantCulture);
                    }
                    Top3DDetectionSensitivityStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DetectionSensitivityReadOk", "Detection sensitivity read from camera");
                }
                else
                {
                    Top3DDetectionSensitivityStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DetectionSensitivityReadUnavailable", "Detection sensitivity read unavailable");
                }
            }
            catch (Exception ex)
            {
                Top3DCurrentDetectionSensitivityText = FormatCurrentDetectionSensitivity(null);
                Top3DDetectionSensitivityStatus = $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DetectionSensitivityReadFailed", "Detection sensitivity read failed")}: {ex.Message}";
                MainWindow.logger?.Warn($"LIVE_IO|Detection sensitivity read failed: {ex.Message}");
            }
        }

        private void ApplyTop3DDetectionSensitivity()
        {
            if (!IsTop3DPanelSelected)
            {
                return;
            }

            try
            {
                var job = ResolveSelectedLivePreviewJob();
                if (job == null)
                {
                    Top3DDetectionSensitivityStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_NoSelectedVisionJob", "No selected VisionPro job");
                    return;
                }

                if (!TryParseDetectionSensitivity(out double sensitivity) || sensitivity <= 0.0)
                {
                    Top3DDetectionSensitivityStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_InvalidDetectionSensitivity", "Invalid detection sensitivity value");
                    return;
                }

                bool applied = IgigaCameraAccess.ApplyDetectionSensitivity(job, ResolveDetectionSensitivityFeatureName(), sensitivity);
                if (!applied)
                {
                    Top3DDetectionSensitivityStatus = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DetectionSensitivityApplyFailed", "Detection sensitivity apply failed");
                    return;
                }

                var appliedValue = IgigaCameraAccess.ReadDetectionSensitivity(job, ResolveDetectionSensitivityFeatureName()) ?? sensitivity;
                Top3DDetectionSensitivityText = appliedValue.ToString("0.###", CultureInfo.InvariantCulture);
                Top3DCurrentDetectionSensitivityText = FormatCurrentDetectionSensitivity(appliedValue);
                Top3DDetectionSensitivityStatus = $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DetectionSensitivityApplied", "Detection sensitivity applied")}: {appliedValue:0.###}";

                PersistDetectionSensitivityToRecipe(appliedValue);
            }
            catch (Exception ex)
            {
                Top3DDetectionSensitivityStatus = $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DetectionSensitivityApplyFailed", "Detection sensitivity apply failed")}: {ex.Message}";
                MainWindow.logger?.Warn($"LIVE_IO|Detection sensitivity apply failed: {ex.Message}");
            }
        }

        private void PersistDetectionSensitivityToRecipe(double value)
        {
            var recipeParam = MainWindow.ConfigRecipeParam;
            var top3D = recipeParam?.Config?.recipeParamTop3D;
            if (recipeParam == null || top3D == null)
            {
                MainWindow.logger?.Warn("LIVE_IO|Detection sensitivity non salvata: ricetta 3D non disponibile.");
                return;
            }

            top3D.Top3DDetectionSensitivity = value;
            recipeParam.UpdateRecipeParamTop3DAsync(top3D).SafeFireAndForget();
        }

        private static string FormatCurrentDetectionSensitivity(double? value)
        {
            string currentLabel = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CurrentDetectionSensitivity", "Current");
            return value.HasValue
                ? $"{currentLabel}: {value.Value:0.###}"
                : $"{currentLabel}: --";
        }

        private bool TryParseDetectionSensitivity(out double sensitivity)
        {
            sensitivity = 0.0;
            string raw = Top3DDetectionSensitivityText;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            raw = raw.Trim().Replace(',', '.');
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out sensitivity);
        }

        private void LiveView()
        {
            LiveViewCoreAsync().SafeFireAndForget();
        }

        private async Task LiveViewCoreAsync()
        {
            try
            {
                if (MainWindow._cognexManager == null || SelectedJobPanel == null)
                {
                    return;
                }

                _resumeContinuousRunAfterLive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                if (ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
                {
                    await StopContinuousRunAsync();
                }

                _liveJobIndex = SelectedJobPanel.JobIndex;
                var triggerBinding = ResolveCameraTriggerBinding(SelectedJobPanel.CameraRole);
                if (triggerBinding == null)
                {
                    MainWindow.logger?.Warn(
                        $"LIVE_IO|Camera trigger output not found for role='{SelectedJobPanel.CameraRole}'. Live preview not started.");
                    RestoreNormalModeAfterLiveAsync().SafeFireAndForget();
                    return;
                }

                EnsureLiveDisplayWindow(SelectedJobPanel);
                StopLiveTriggerLoop(true);
                _liveTriggerBinding = triggerBinding;
                _liveIoCts = new CancellationTokenSource();
                _liveVisionProDisplayStarted = false;
                _livePreviewDisplaySource = null;
                _livePreviewRawSourceWarningLogged = false;
                MainWindow.IsJobEditorLivePreviewActive = true;
                IsLiveRunning = true;
                ApplyConfiguredExposureForLivePreview();
                StartIoTriggerLoopAsync(_liveIoCts.Token, SelectedJobPanel.CameraRole, triggerBinding).SafeFireAndForget();

                MainWindow.logger?.Info(
                    $"LIVE_IO|Live preview hardware-trigger avviata sul job '{SelectedJobPanel.JobName}' (index {SelectedJobPanel.JobIndex}), IO trigger={triggerBinding.SignalCode} interval={_livePreviewIntervalMs}ms pulse={_livePreviewPulseMs}ms. VisionPro job run-once viene armato prima del pulse IO; FIFO live non usata.");
            }
            catch (Exception ex)
            {
                StopLiveTriggerLoop(true);
                IsLiveRunning = false;
                _liveVisionProDisplayStarted = false;
                MainWindow.IsJobEditorLivePreviewActive = false;
                RestoreNormalModeAfterLiveAsync().SafeFireAndForget();
                MainWindow.logger?.Error($"Errore nella Live View: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nella Live View: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        private void ApplyConfiguredExposureForLivePreview()
        {
            if (!TryParseLivePreviewExposure(out double exposureUs) || exposureUs <= 0.0)
            {
                return;
            }

            try
            {
                var job = ResolveSelectedLivePreviewJob();
                if (job != null && IgigaCameraAccess.ApplyExposureTimeUs(job, exposureUs))
                {
                    var appliedExposureUs = IgigaCameraAccess.ReadExposureTimeUs(job, false) ?? exposureUs;
                    LivePreviewExposureUsText = appliedExposureUs.ToString("0.###", CultureInfo.InvariantCulture);
                    LivePreviewCurrentExposureText = FormatLivePreviewCurrentExposure(appliedExposureUs);
                    LivePreviewExposureStatus = $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ExposureApplied", "Exposure applied")}: {appliedExposureUs:0.###} us";
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Unable to apply live preview exposure at start: {ex.Message}");
            }
        }

        public void OnToolBlockModified()
        {
            if (_isEditorViewUnloading)
            {
                MainWindow.logger?.Info("JOBEDITOR_TOOL_CHANGED_IGNORED|reason=view-unloading");
                return;
            }

            HasChanges = true;

            if (!_jobEditorRuntimeHoldActive)
            {
                EnterEditorPauseAsync().SafeFireAndForget();
            }
        }

        private bool CanOpenInQuickBuild()
        {
            try
            {
                string recipeFolder = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder;
                string lastRecipe = MainWindow.configManager?.Config?.Configuration?.LastRecipe;
                if (string.IsNullOrEmpty(recipeFolder) || string.IsNullOrEmpty(lastRecipe))
                {
                    return false;
                }

                string filePath = System.IO.Path.Combine(recipeFolder, lastRecipe);
                return System.IO.File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }

        private void OpenInQuickBuild()
        {
            try
            {
                string recipeFolder = MainWindow.configManager.Config.Configuration.Recipe_Folder;
                string lastRecipe = MainWindow.configManager.Config.Configuration.LastRecipe;
                string filePath = System.IO.Path.Combine(recipeFolder, lastRecipe);

                if (!System.IO.File.Exists(filePath))
                {
                    new SystemNotificationWindow("Error", "Job file not found.", NotificationSeverity.Error).ShowDialog();
                    return;
                }

                MainWindow._cognexManager?.StopJobs();

                string vproDir = Environment.GetEnvironmentVariable("VPRO_ROOT");
                string quickBuildPath = !string.IsNullOrEmpty(vproDir)
                    ? System.IO.Path.Combine(vproDir, "bin", "Cognex.VisionPro.QuickBuild.exe")
                    : @"C:\Program Files\Cognex\VisionPro\bin\Cognex.VisionPro.QuickBuild.exe";

                if (!System.IO.File.Exists(quickBuildPath))
                {
                    new SystemNotificationWindow("Errore", $"QuickBuild non trovato in: {quickBuildPath}", NotificationSeverity.Error).ShowDialog();
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = quickBuildPath,
                    Arguments = $"\"{filePath}\"",
                    WorkingDirectory = System.IO.Path.GetDirectoryName(quickBuildPath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                MainWindow.logger?.Info($"Tentativo apertura QuickBuild per: {filePath}");
                Process qbProcess = Process.Start(startInfo);

                qbProcess.EnableRaisingEvents = true;
                qbProcess.Exited += (s, e) =>
                {
                    _ = MainWindow.MainView.InitializeRecipeAsync();
                    MainWindow.logger?.Info("QuickBuild è stato chiuso.");
                };

                MainWindow.logger?.Info($"Opened {filePath} in QuickBuild");
            }
            catch (Exception ex)
            {
                new SystemNotificationWindow("Error", $"Failed to open QuickBuild: {ex.Message}", NotificationSeverity.Error).ShowDialog();
                MainWindow.logger?.Error($"Error opening QuickBuild: {ex.Message}");
            }
        }

        private void StopLiveDisplay()
        {
            try
            {
                StopLiveTriggerLoop(true);

                if (_liveVisionProDisplayStarted && _liveDisplayWindow?.LiveDisplay != null)
                {
                    MainWindow._cognexManager?.StopLiveDisplay(_liveDisplayWindow.LiveDisplay);
                }

                if (_liveDisplayWindow != null)
                {
                    _liveDisplayWindow.Closed -= LiveDisplayWindow_Closed;
                    _liveDisplayWindow.Close();
                    _liveDisplayWindow = null;
                }

                IsLiveRunning = false;
                _liveVisionProDisplayStarted = false;
                MainWindow.IsJobEditorLivePreviewActive = false;
                MainWindow.logger?.Info("Live fermato.");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nello stop live: {ex.Message}");
            }
            finally
            {
                MainWindow.IsJobEditorLivePreviewActive = false;
                RestoreNormalModeAfterLiveAsync().SafeFireAndForget();
            }
        }

        private async Task StartIoTriggerLoopAsync(CancellationToken ct, string cameraRole)
        {
            const int TriggerPulseMs = 20;

            int channel;
            bool activeState;
            ResolveCameraTriggerBinding(cameraRole, out channel, out activeState);
            bool inactiveState = !activeState;

            if (channel < 0)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Channel trigger non trovato per role='{cameraRole}'. Nessun impulso verrà generato.");
                return;
            }

            MainWindow.logger?.Info($"LIVE_IO|Avvio loop trigger IO channel={channel} interval={_livePreviewIntervalMs}ms pulse={TriggerPulseMs}ms role={cameraRole}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_livePreviewIntervalMs, ct).ConfigureAwait(false);

                    if (ct.IsCancellationRequested) break;

                    var ioManager = ServiceLocator.IoManager;
                    if (ioManager == null) continue;

                    try
                    {
                        await ioManager.WriteOutputAsync(channel, activeState).ConfigureAwait(false);
                        await Task.Delay(TriggerPulseMs, ct).ConfigureAwait(false);
                        await ioManager.WriteOutputAsync(channel, inactiveState).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        MainWindow.logger?.Warn($"LIVE_IO|Errore impulso trigger: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal loop cancellation on StopLiveDisplay
            }
            finally
            {
                MainWindow.logger?.Info($"LIVE_IO|Loop trigger IO terminato per role='{cameraRole}'");
            }
        }

        private void ResolveCameraTriggerBinding(string cameraRole, out int channel, out bool activeElectricalState)
        {
            channel = -1;
            activeElectricalState = true;
            try
            {
                var runtimeConfig = new MachineConfigurationService().Load();
                if (runtimeConfig?.MachineOutputs == null) return;

                string signalCode = runtimeConfig.RuntimeBindings?.CameraTriggerSignalCode
                                    ?? "OUT_CAMERA_TOP_TRIGGER";

                if (!string.IsNullOrWhiteSpace(cameraRole) &&
                    !cameraRole.Equals("top", StringComparison.OrdinalIgnoreCase))
                {
                    string roleUpper = cameraRole.ToUpperInvariant();
                    var roleSignal = runtimeConfig.MachineOutputs.FirstOrDefault(o =>
                        o.Category == MachineSignalCategory.CameraTrigger &&
                        o.SignalCode != null &&
                        o.SignalCode.IndexOf(roleUpper, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (roleSignal != null)
                        signalCode = roleSignal.SignalCode;
                }

                var signal = runtimeConfig.MachineOutputs.FirstOrDefault(o =>
                    string.Equals(o.SignalCode, signalCode, StringComparison.OrdinalIgnoreCase));

                if (signal == null) return;

                channel = MachineRuntimeIo.ParseChannelNumber(signal.Channel);
                activeElectricalState = MachineRuntimeIo.ToElectricalOutputState(signal, true);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Impossibile risolvere channel trigger: {ex.Message}");
            }
        }

        private async Task StartIoTriggerLoopAsync(
            CancellationToken ct,
            string cameraRole,
            LiveCameraTriggerBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            MainWindow.logger?.Info(
                $"LIVE_IO|Start IO trigger loop signal={binding.SignalCode} channel={binding.Channel} interval={_livePreviewIntervalMs}ms pulse={_livePreviewPulseMs}ms role={cameraRole}");

            try
            {
                await Task.Delay(50, ct).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    var cycleWatch = Stopwatch.StartNew();
                    await RunLivePreviewFrameAsync(binding, ct).ConfigureAwait(false);

                    var remainingIntervalMs = Math.Max(0, _livePreviewIntervalMs - (int)cycleWatch.ElapsedMilliseconds);
                    if (remainingIntervalMs > 0)
                    {
                        await Task.Delay(remainingIntervalMs, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal loop cancellation on StopLiveDisplay or window close.
            }
            finally
            {
                await ResetLiveTriggerOutputAsync(binding).ConfigureAwait(false);
                MainWindow.logger?.Info($"LIVE_IO|IO trigger loop stopped for role='{cameraRole}' signal={binding.SignalCode}");
            }
        }

        private async Task RunLivePreviewFrameAsync(
            LiveCameraTriggerBinding binding,
            CancellationToken ct)
        {
            if (binding == null)
            {
                return;
            }

            await _livePreviewRunLock.WaitAsync(ct).ConfigureAwait(false);

            CogJob job = null;
            Task<LivePreviewDisplayFrame> runTask = null;

            try
            {
                var manager = MainWindow._cognexManager;
                if (manager == null || _liveJobIndex < 0)
                {
                    return;
                }

                job = manager.GetJob(_liveJobIndex);
                if (job == null)
                {
                    MainWindow.logger?.Warn($"LIVE_IO|Live preview job not found at index {_liveJobIndex}.");
                    return;
                }

                runTask = Task.Run(async () =>
                {
                    job.Run();
                    await WaitForLivePreviewJobCompletionAsync(job, ct, ResolveLivePreviewRunTimeoutMs())
                        .ConfigureAwait(false);
                    return CreateLivePreviewDisplayFrame(job, manager.GetJobManager(), SelectedJobPanel?.CameraRole);
                }, ct);

                await Task.Delay(ResolveLivePreviewArmDelayMs(), ct).ConfigureAwait(false);
                await PulseLiveTriggerAsync(binding, _livePreviewPulseMs, ct).ConfigureAwait(false);

                var timeoutTask = Task.Delay(ResolveLivePreviewRunTimeoutMs(), ct);
                var completedTask = await Task.WhenAny(runTask, timeoutTask).ConfigureAwait(false);
                if (completedTask != runTask)
                {
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }

                    TryStopLivePreviewJob(job, "run timeout");
                    MainWindow.logger?.Warn(
                        $"LIVE_IO|VisionPro live preview run timeout on job '{job.Name}'. Check camera trigger, exposure and acquisition FIFO.");
                    return;
                }

                var displayFrame = await runTask.ConfigureAwait(false);
                await UpdateLivePreviewDisplayAsync(job, displayFrame, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryStopLivePreviewJob(job, "live preview cancelled");
                throw;
            }
            catch (Exception ex)
            {
                TryStopLivePreviewJob(job, "live preview error");
                MainWindow.logger?.Warn($"LIVE_IO|VisionPro live preview frame failed: {ex.Message}");
            }
            finally
            {
                _livePreviewRunLock.Release();
            }
        }

        private int ResolveLivePreviewArmDelayMs()
        {
            return Math.Max(20, Math.Min(100, _livePreviewPulseMs * 2));
        }

        private int ResolveLivePreviewRunTimeoutMs()
        {
            return Math.Max(2000, Math.Min(15000, _livePreviewIntervalMs + _livePreviewPulseMs + 1500));
        }

        private static async Task WaitForLivePreviewJobCompletionAsync(
            CogJob job,
            CancellationToken ct,
            int timeoutMs)
        {
            if (job == null)
            {
                return;
            }

            var watch = Stopwatch.StartNew();
            bool observedRunning = false;

            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();

                bool isRunning;
                try
                {
                    isRunning = job.RunningSyncBoolean.Value;
                }
                catch
                {
                    return;
                }

                if (isRunning)
                {
                    observedRunning = true;
                }
                else if (observedRunning || watch.ElapsedMilliseconds >= 80)
                {
                    return;
                }

                await Task.Delay(10, ct).ConfigureAwait(false);
            }

            throw new TimeoutException($"VisionPro live preview job '{job.Name}' did not complete within {timeoutMs} ms.");
        }

        private LivePreviewDisplayFrame CreateLivePreviewDisplayFrame(
            CogJob job,
            CogJobManager jobManager,
            string cameraRole)
        {
            var rawImage = TryGetLivePreviewAcquisitionInputImage(job);

            // Drain the technical UserResult even when the display uses the raw
            // acquisition image. This keeps the QuickBuild queue bounded during live preview.
            var displayRootRecord = TryCreateLivePreviewDisplayRecordFromUserResult(job, jobManager);
            if (rawImage != null)
            {
                LogLivePreviewDisplaySourceOnce(job, "CogInputImageTool.InputImage");
                return new LivePreviewDisplayFrame
                {
                    Record = null,
                    Image = rawImage,
                    IsRawAcquisition = true,
                    Source = "CogInputImageTool.InputImage"
                };
            }

            var rootRecord = job?.VisionTool?.CreateLastRunRecord();
            if (displayRootRecord == null && rootRecord != null)
            {
                displayRootRecord = TryTraverseLiveRecord(rootRecord, "ShowLastRunRecordForUserQueue", "LastRun")
                                    ?? TryTraverseLiveRecord(rootRecord, "LastRun")
                                    ?? TryFindLiveRecordByKey(rootRecord, "LastRun")
                                    ?? rootRecord;
            }

            var fallbackRecord = ResolveLivePreviewDisplayRecord(
                                     displayRootRecord,
                                     ResolveLivePreviewSubRecordKey(cameraRole))
                                 ?? displayRootRecord;
            var fallbackImage = TryFindLivePreviewImage(fallbackRecord);
            if (fallbackRecord != null)
            {
                LogLivePreviewDisplaySourceOnce(job, "LastRunFallback");
            }

            return fallbackRecord == null
                ? null
                : new LivePreviewDisplayFrame
                {
                    Record = fallbackRecord,
                    Image = fallbackImage,
                    IsRawAcquisition = false,
                    Source = "LastRunFallback"
                };
        }

        private ICogImage TryGetLivePreviewAcquisitionInputImage(CogJob job)
        {
            var visionTool = job?.VisionTool;
            var toolGroup = visionTool as CogToolGroup;
            if (toolGroup == null)
            {
                LogLivePreviewRawSourceUnavailableOnce(
                    job,
                    $"VisionTool is not CogToolGroup; actualType={visionTool?.GetType().FullName ?? "null"}");
                return null;
            }

            try
            {
                CogInputImageTool imageSourceTool = null;
                int imageSourceIndex = -1;

                if (toolGroup.Tools.Count > 0)
                {
                    imageSourceTool = toolGroup.Tools[0] as CogInputImageTool;
                    imageSourceIndex = imageSourceTool == null ? -1 : 0;
                }

                if (imageSourceTool == null)
                {
                    for (int i = 1; i < toolGroup.Tools.Count; i++)
                    {
                        imageSourceTool = toolGroup.Tools[i] as CogInputImageTool;
                        if (imageSourceTool != null)
                        {
                            imageSourceIndex = i;
                            break;
                        }
                    }
                }

                if (imageSourceTool == null)
                {
                    LogLivePreviewRawSourceUnavailableOnce(
                        job,
                        "CogInputImageTool was not found in CogToolGroup.Tools");
                    return null;
                }

                var inputImage = imageSourceTool.InputImage;
                if (inputImage == null)
                {
                    LogLivePreviewRawSourceUnavailableOnce(
                        job,
                        $"CogInputImageTool.InputImage is null; toolIndex={imageSourceIndex}; " +
                        $"toolName={imageSourceTool.Name}");
                    return null;
                }

                return inputImage;
            }
            catch (Exception ex)
            {
                LogLivePreviewRawSourceUnavailableOnce(
                    job,
                    $"acquisition InputImage read failed: {ex.Message}");
                return null;
            }
        }

        private void LogLivePreviewRawSourceUnavailableOnce(CogJob job, string reason)
        {
            if (_livePreviewRawSourceWarningLogged)
            {
                return;
            }

            _livePreviewRawSourceWarningLogged = true;
            MainWindow.logger?.Warn(
                $"LIVE_IO|Raw live preview source unavailable: job={job?.Name ?? "unknown"}; " +
                $"reason={reason}; fallback=LastRun");
        }

        private void LogLivePreviewDisplaySourceOnce(CogJob job, string source)
        {
            if (string.Equals(_livePreviewDisplaySource, source, StringComparison.Ordinal))
            {
                return;
            }

            _livePreviewDisplaySource = source;
            MainWindow.logger?.Info(
                $"LIVE_IO|Live preview display source selected: job={job?.Name ?? "unknown"}; source={source}");
        }

        private static string ResolveLivePreviewSubRecordKey(string cameraRole)
        {
            var normalizedRole = CameraConfigurationHelper.NormalizeCameraType(cameraRole);
            var recipeTop3D = MainWindow.ConfigRecipeParam?.Config?.recipeParamTop3D;
            var lastRun = MainWindow.configManager?.Config?.LasRunParam;

            switch (normalizedRole)
            {
                case "top3d":
                    return string.IsNullOrWhiteSpace(recipeTop3D?.Top3DPrimaryLastRunView)
                        ? lastRun?.LastRunView1
                        : recipeTop3D.Top3DPrimaryLastRunView;
                case "top2d":
                    return string.IsNullOrWhiteSpace(recipeTop3D?.Top3DSecondaryLastRunView)
                        ? lastRun?.LastRunView2
                        : recipeTop3D.Top3DSecondaryLastRunView;
                case "side":
                case "left":
                    return lastRun?.LastRunView2;
                case "front":
                case "rear":
                case "right":
                    return lastRun?.LastRunView3;
                case "bottom":
                    return "LastRun";
                case "top":
                default:
                    return lastRun?.LastRunView1;
            }
        }

        private static ICogRecord ResolveLivePreviewDisplayRecord(ICogRecord root, string subRecordKey)
        {
            if (root == null || string.IsNullOrWhiteSpace(subRecordKey))
            {
                return root;
            }

            try
            {
                if (root.SubRecords != null && root.SubRecords.ContainsKey(subRecordKey))
                {
                    return root.SubRecords[subRecordKey];
                }

                var byKey = TryFindLiveRecordByKey(root, subRecordKey);
                if (byKey != null)
                {
                    return byKey;
                }

                var current = root;
                foreach (var segment in subRecordKey.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (current?.SubRecords == null || !current.SubRecords.ContainsKey(segment))
                    {
                        return null;
                    }

                    current = current.SubRecords[segment];
                }

                return current;
            }
            catch
            {
                return null;
            }
        }

        private static ICogRecord TryCreateLivePreviewDisplayRecordFromUserResult(
            CogJob job,
            CogJobManager jobManager)
        {
            if (job == null || jobManager == null)
            {
                return null;
            }

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var result = jobManager.UserResult();
                    if (result == null)
                    {
                        return null;
                    }

                    string resultJobName = TryReadLivePreviewJobName(result);
                    if (!string.IsNullOrWhiteSpace(resultJobName) &&
                        !string.Equals(resultJobName, job.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        MainWindow.logger?.Debug($"LIVE_IO|Discarded queued UserResult for job '{resultJobName}' while waiting for live job '{job.Name}'.");
                        continue;
                    }

                    return TryTraverseLiveRecord(result, "ShowLastRunRecordForUserQueue", "LastRun")
                           ?? TryTraverseLiveRecord(result, "LastRun")
                           ?? TryFindLiveRecordByKey(result, "LastRun")
                           ?? result;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Unable to read live preview UserResult: {ex.Message}");
            }

            return null;
        }

        private static string TryReadLivePreviewJobName(ICogRecord result)
        {
            try
            {
                if (result?.SubRecords != null &&
                    result.SubRecords.ContainsKey("JobName"))
                {
                    return result.SubRecords["JobName"]?.Content as string;
                }
            }
            catch
            {
            }

            return null;
        }

        private static ICogImage TryFindLivePreviewImage(ICogRecord record, int depth = 0)
        {
            if (record == null || depth > 10)
            {
                return null;
            }

            try
            {
                if (record.Content is ICogImage image)
                {
                    return image;
                }
            }
            catch
            {
            }

            if (record.SubRecords == null)
            {
                return null;
            }

            foreach (ICogRecord child in record.SubRecords)
            {
                var image = TryFindLivePreviewImage(child, depth + 1);
                if (image != null)
                {
                    return image;
                }
            }

            return null;
        }

        private static ICogRecord TryTraverseLiveRecord(ICogRecord root, params string[] keys)
        {
            var current = root;
            foreach (var key in keys)
            {
                if (current?.SubRecords == null ||
                    !current.SubRecords.ContainsKey(key))
                {
                    return null;
                }

                current = current.SubRecords[key];
            }

            return current;
        }

        private static ICogRecord TryFindLiveRecordByKey(ICogRecord root, string key, int depth = 0)
        {
            if (root?.SubRecords == null ||
                string.IsNullOrWhiteSpace(key) ||
                depth > 8)
            {
                return null;
            }

            if (root.SubRecords.ContainsKey(key))
            {
                return root.SubRecords[key];
            }

            foreach (ICogRecord child in root.SubRecords)
            {
                var match = TryFindLiveRecordByKey(child, key, depth + 1);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private async Task UpdateLivePreviewDisplayAsync(
            CogJob job,
            LivePreviewDisplayFrame displayFrame,
            CancellationToken ct)
        {
            if (displayFrame?.Image == null)
            {
                MainWindow.logger?.Warn($"LIVE_IO|No display image available for live preview job '{job?.Name}'.");
                return;
            }

            var liveWindow = _liveDisplayWindow;
            if (liveWindow?.LiveDisplay == null)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested ||
                    _liveDisplayWindow == null ||
                    !ReferenceEquals(_liveDisplayWindow, liveWindow))
                {
                    return;
                }

                var display = liveWindow.LiveDisplay;
                display.AutoFit = true;
                if (displayFrame.IsRawAcquisition)
                {
                    display.Record = null;
                    display.StaticGraphics?.Clear();
                    display.InteractiveGraphics?.Clear();
                    display.Image = displayFrame.Image;
                }
                else
                {
                    display.Record = displayFrame.Record;
                    display.Image = displayFrame.Image;
                }
                display.Fit(true);
                display.Refresh();
            });
        }

        private static void TryStopLivePreviewJob(CogJob job, string reason)
        {
            if (job == null)
            {
                return;
            }

            try
            {
                if (job.RunningSyncBoolean.Value)
                {
                    job.Stop();
                    MainWindow.logger?.Warn($"LIVE_IO|Stopped live preview job '{job.Name}' after {reason}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Unable to stop live preview job '{job.Name}' after {reason}: {ex.Message}");
            }
        }

        private async Task PulseLiveTriggerAsync(
            LiveCameraTriggerBinding binding,
            int pulseMs,
            CancellationToken ct)
        {
            var ioManager = ServiceLocator.IoManager;
            if (ioManager == null || binding == null)
            {
                return;
            }

            bool outputRaised = false;
            try
            {
                await ioManager.WriteOutputAsync(binding.Channel, binding.ActiveElectricalState).ConfigureAwait(false);
                outputRaised = true;
                await Task.Delay(Math.Max(1, pulseMs), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Trigger pulse failed on {binding.SignalCode}: {ex.Message}");
            }
            finally
            {
                if (outputRaised)
                {
                    await ResetLiveTriggerOutputAsync(binding).ConfigureAwait(false);
                }
            }
        }

        private async Task ResetLiveTriggerOutputAsync(LiveCameraTriggerBinding binding)
        {
            try
            {
                var ioManager = ServiceLocator.IoManager;
                if (ioManager == null || binding == null || binding.Channel < 0)
                {
                    return;
                }

                await ioManager.WriteOutputAsync(binding.Channel, binding.InactiveElectricalState).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"LIVE_IO|Unable to reset trigger output {binding?.SignalCode}: {ex.Message}");
            }
        }

        private void StopLiveTriggerLoop(bool resetOutput)
        {
            MainWindow.IsJobEditorLivePreviewActive = false;

            var binding = _liveTriggerBinding;
            var cts = _liveIoCts;
            _liveIoCts = null;
            _liveTriggerBinding = null;

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    cts.Dispose();
                }
            }

            if (resetOutput && binding != null)
            {
                ResetLiveTriggerOutputAsync(binding).SafeFireAndForget();
            }
        }

        private LiveCameraTriggerBinding ResolveCameraTriggerBinding(string cameraRole)
        {
            try
            {
                var runtimeConfig = new MachineConfigurationService().Load();
                if (runtimeConfig?.MachineOutputs == null)
                {
                    return null;
                }

                var signal = ResolveCameraTriggerSignal(runtimeConfig, cameraRole);
                var channel = MachineRuntimeIo.ParseChannelNumber(signal?.Channel);
                if (signal == null || channel < 0)
                {
                    return null;
                }

                return new LiveCameraTriggerBinding
                {
                    Channel = channel,
                    ActiveElectricalState = MachineRuntimeIo.ToElectricalOutputState(signal, true),
                    InactiveElectricalState = MachineRuntimeIo.ToElectricalOutputState(signal, false),
                    SignalCode = signal.SignalCode,
                    Board = signal.Board,
                    PhysicalChannel = signal.Channel
                };
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"LIVE_IO|Unable to resolve camera trigger binding: {ex.Message}");
                return null;
            }
        }

        private static MachineSignalDefinition ResolveCameraTriggerSignal(
            MachineRuntimeConfiguration runtimeConfig,
            string cameraRole)
        {
            var candidates = ResolveCameraTriggerSignalCandidates(runtimeConfig, cameraRole).ToList();
            foreach (var candidate in candidates)
            {
                var signal = FindBestOutputSignal(runtimeConfig.MachineOutputs, candidate);
                if (signal != null)
                {
                    return signal;
                }
            }

            var normalizedRole = CameraConfigurationHelper.NormalizeCameraType(cameraRole);
            var roleTokens = ResolveCameraRoleTokens(normalizedRole);
            return runtimeConfig.MachineOutputs
                .Where(signal => signal != null && signal.Category == MachineSignalCategory.CameraTrigger)
                .Where(signal => ContainsAny(signal.SignalCode, roleTokens) || ContainsAny(signal.Description, roleTokens))
                .OrderByDescending(HasPhysicalChannel)
                .ThenBy(signal => signal.SignalCode)
                .FirstOrDefault();
        }

        private static IEnumerable<string> ResolveCameraTriggerSignalCandidates(
            MachineRuntimeConfiguration runtimeConfig,
            string cameraRole)
        {
            var normalizedRole = CameraConfigurationHelper.NormalizeCameraType(cameraRole);
            foreach (var fromPoint in ResolveCameraTriggerSignalsFromInterventionPoints(runtimeConfig, normalizedRole))
            {
                yield return fromPoint;
            }

            if (string.Equals(normalizedRole, "top", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(runtimeConfig.RuntimeBindings?.CameraTriggerSignalCode))
            {
                yield return runtimeConfig.RuntimeBindings.CameraTriggerSignalCode;
            }

            foreach (var fromProfile in ResolveCameraTriggerSignalsFromMultiShot(runtimeConfig, normalizedRole))
            {
                yield return fromProfile;
            }

            foreach (var fallback in ResolveDefaultTriggerSignalCodes(normalizedRole))
            {
                yield return fallback;
            }
        }

        private static IEnumerable<string> ResolveCameraTriggerSignalsFromInterventionPoints(
            MachineRuntimeConfiguration runtimeConfig,
            string normalizedRole)
        {
            var tokens = ResolveCameraRoleTokens(normalizedRole);
            return (runtimeConfig.InterventionPoints ?? new List<MachineInterventionPoint>())
                .Where(point => point != null && point.Enabled)
                .Where(point => string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase))
                .Where(point => ContainsAny($"{point.PointCode} {point.SignalCode}", tokens))
                .Select(point => point.SignalCode)
                .Where(signalCode => !string.IsNullOrWhiteSpace(signalCode));
        }

        private static IEnumerable<string> ResolveCameraTriggerSignalsFromMultiShot(
            MachineRuntimeConfiguration runtimeConfig,
            string normalizedRole)
        {
            if (runtimeConfig?.MachineMultiShotTrigger == null)
            {
                yield break;
            }

            if (string.Equals(normalizedRole, "left", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedRole, "side", StringComparison.OrdinalIgnoreCase))
            {
                yield return runtimeConfig.MachineMultiShotTrigger.Side?.TriggerOutputName;
                yield break;
            }

            if (string.Equals(normalizedRole, "rear", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedRole, "right", StringComparison.OrdinalIgnoreCase))
            {
                var profile = runtimeConfig.MachineMultiShotTrigger.ResolveOptionsForRuntimeRole(normalizedRole);
                if (profile != null && Models.MultiShotTrigger.MachineMultiShotTriggerConfiguration.IsCompatibleRuntimeRole(profile, normalizedRole))
                {
                    yield return profile.TriggerOutputName;
                }
                yield break;
            }

            if (string.Equals(normalizedRole, "bottom", StringComparison.OrdinalIgnoreCase))
            {
                yield return runtimeConfig.MachineMultiShotTrigger.Bottom?.TriggerOutputName;
            }
        }

        private static IEnumerable<string> ResolveDefaultTriggerSignalCodes(string normalizedRole)
        {
            if (string.Equals(normalizedRole, "left", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedRole, "side", StringComparison.OrdinalIgnoreCase))
            {
                yield return "OUT_CAMERA_LEFT_TRIGGER";
                yield return "OUT_CAMERA_SIDE_TRIGGER";
                yield break;
            }

            if (string.Equals(normalizedRole, "rear", StringComparison.OrdinalIgnoreCase))
            {
                yield return "OUT_CAMERA_REAR_TRIGGER";
                yield break;
            }
            if (string.Equals(normalizedRole, "right", StringComparison.OrdinalIgnoreCase))
            {
                yield return "OUT_CAMERA_RIGHT_TRIGGER";
                yield break;
            }

            if (string.Equals(normalizedRole, "bottom", StringComparison.OrdinalIgnoreCase))
            {
                yield return "OUT_CAMERA_BOTTOM_TRIGGER";
                yield break;
            }

            if (string.Equals(normalizedRole, "front", StringComparison.OrdinalIgnoreCase))
            {
                yield return "OUT_CAMERA_FRONT_TRIGGER";
                yield break;
            }

            yield return "OUT_CAMERA_TOP_TRIGGER";
        }

        private static string[] ResolveCameraRoleTokens(string normalizedRole)
        {
            if (string.Equals(normalizedRole, "left", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedRole, "side", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "LEFT", "SIDE" };
            }

            if (string.Equals(normalizedRole, "rear", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "REAR" };
            }
            if (string.Equals(normalizedRole, "right", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "RIGHT" };
            }

            if (string.Equals(normalizedRole, "bottom", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "BOTTOM" };
            }

            if (string.Equals(normalizedRole, "front", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "FRONT" };
            }

            return new[] { "TOP" };
        }

        private static MachineSignalDefinition FindBestOutputSignal(
            IEnumerable<MachineSignalDefinition> outputs,
            string signalCode)
        {
            if (string.IsNullOrWhiteSpace(signalCode))
            {
                return null;
            }

            var normalizedSignalCode = NormalizeSignalCode(signalCode);
            return (outputs ?? Enumerable.Empty<MachineSignalDefinition>())
                .Where(signal => string.Equals(NormalizeSignalCode(signal?.SignalCode), normalizedSignalCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(HasPhysicalChannel)
                .ThenByDescending(signal => signal?.ReservedForRealSignal == true)
                .FirstOrDefault();
        }

        private static bool HasPhysicalChannel(MachineSignalDefinition signal)
        {
            return signal != null &&
                   !string.IsNullOrWhiteSpace(signal.Board) &&
                   MachineRuntimeIo.ParseChannelNumber(signal.Channel) >= 0;
        }

        private static bool ContainsAny(string value, IEnumerable<string> tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return tokens.Any(token =>
                !string.IsNullOrWhiteSpace(token) &&
                value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string NormalizeSignalCode(string signalCode)
        {
            return string.IsNullOrWhiteSpace(signalCode)
                ? string.Empty
                : signalCode.Trim();
        }

        private LiveDisplayWindow EnsureLiveDisplayWindow(JobToolEditorPanelItem panel)
        {
            if (_liveDisplayWindow == null || !_liveDisplayWindow.IsLoaded)
            {
                var liveDisplayWindow = new LiveDisplayWindow
                {
                    LiveTitle = panel.LiveDisplayTitle
                };

                var ownerWindow = ResolveLiveWindowOwner(liveDisplayWindow);
                if (ownerWindow != null)
                {
                    liveDisplayWindow.Owner = ownerWindow;
                }

                _liveDisplayWindow = liveDisplayWindow;
                _liveDisplayWindow.Closed += LiveDisplayWindow_Closed;
                _liveDisplayWindow.Show();
            }
            else
            {
                _liveDisplayWindow.LiveTitle = panel.LiveDisplayTitle;
                if (!_liveDisplayWindow.IsVisible)
                {
                    _liveDisplayWindow.Show();
                }
            }

            _liveDisplayWindow.Activate();
            return _liveDisplayWindow;
        }

        private Window ResolveLiveWindowOwner(Window liveDisplayWindow)
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null &&
                !ReferenceEquals(mainWindow, liveDisplayWindow) &&
                mainWindow.IsLoaded)
            {
                return mainWindow;
            }

            return Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window =>
                    !ReferenceEquals(window, liveDisplayWindow) &&
                    window.IsLoaded &&
                    window.IsVisible);
        }

        private void LiveDisplayWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                StopLiveTriggerLoop(true);

                if (_liveVisionProDisplayStarted && _liveDisplayWindow?.LiveDisplay != null)
                {
                    MainWindow._cognexManager?.StopLiveDisplay(_liveDisplayWindow.LiveDisplay);
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore chiusura live preview: {ex.Message}");
            }
            finally
            {
                _liveDisplayWindow = null;
                IsLiveRunning = false;
                _liveVisionProDisplayStarted = false;
                MainWindow.IsJobEditorLivePreviewActive = false;
                RestoreNormalModeAfterLiveAsync().SafeFireAndForget();
            }
        }

        private async Task RestoreNormalModeAfterLiveAsync()
        {
            if (_isRestoringAfterLive)
            {
                return;
            }

            _isRestoringAfterLive = true;

            try
            {
                if (_liveJobIndex >= 0)
                {
                    MainWindow._cognexManager?.RestoreHardwareMode(_liveJobIndex);
                }

                if (_resumeContinuousRunAfterLive)
                {
                    await ServiceLocator.MachineRuntimeService.ManageJobStateAsync(true);
                }

                MainWindow.logger?.Info("Modalita normale ripristinata dopo la chiusura della Live Preview.");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel ripristino della modalita normale dopo il live: {ex.Message}");
            }
            finally
            {
                _resumeContinuousRunAfterLive = false;
                _liveJobIndex = -1;
                _isRestoringAfterLive = false;
            }
        }

        public Task OnEditorViewLoadedAsync()
        {
            return EnterEditorPauseAsync();
        }

        public Task OnEditorViewUnloadedAsync()
        {
            _isEditorViewUnloading = true;
            return ExitEditorPauseAsync(true);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        private void SetLoadingState(string title, string message, string detail, int progress, bool indeterminate = false)
        {
            LoadingStatusTitle = title;
            LoadingStatusMessage = message;
            LoadingStatusDetail = detail;
            LoadingProgressValue = progress;
            IsLoadingIndeterminate = indeterminate;
        }

        private void ResetLoadingState()
        {
            LoadingStatusTitle = GetMessage("Sub_entry_OperationLoadingToolTitle", "Loading tool");
            LoadingStatusMessage = GetMessage("Sub_entry_OperationPreparing", "Preparing operation...");
            LoadingStatusDetail = GetMessage("Sub_entry_JobEditorLoadingTool", "Preparing the VisionPro tool editor.");
            LoadingProgressValue = 0;
            IsLoadingIndeterminate = false;
        }

        private static QtisVisionPanel.OperationProgressWindow CreateProgressWindow(string title, string status, string detail, int progress)
        {
            var window = ServiceLocator.DialogService.CreateOperationProgressWindow();
            window.UpdateStatus(title, status, detail, progress);
            window.Show();
            return window;
        }

        private static void UpdateProgressWindow(QtisVisionPanel.OperationProgressWindow window, string title, string status, string detail, int progress)
        {
            window?.UpdateStatus(title, status, detail, progress);
        }

        private static void CloseProgressWindow(QtisVisionPanel.OperationProgressWindow window)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                if (window.Dispatcher.CheckAccess())
                {
                    window.Close();
                }
                else
                {
                    window.Dispatcher.Invoke(window.Close);
                }
            }
            catch
            {
            }
        }
    }
}
