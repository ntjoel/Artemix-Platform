using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QtisVisionPanel.ViewModels
{
    public class AlarmsViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly EventLogRepository _repository = new EventLogRepository();
        private readonly DispatcherTimer _refreshTimer;
        private readonly MachineRuntimeService _machineRuntimeService = ServiceLocator.MachineRuntimeService;
        private readonly MachineStatusService _machineStatusService = MachineStatusService.Instance;
        private readonly SystemDiagnosticsService _systemDiagnosticsService = ServiceLocator.SystemDiagnosticsService;
        private readonly AuthorizationService _authorizationService = ServiceLocator.Authorization;
        private bool _isInitialized;
        private bool _isBusy;
        private bool _autoRefresh = true;
        private string _searchText;
        private string _selectedLevel = "All";
        private EventLogEntry _selectedEvent;
        private string _databaseStatus = "Unknown";
        private string _visionProStatus = "Unknown";
        private string _machineStatus = "Unknown";
        private string _statusMessage = "Ready";
        private string _lastRefreshText = "Never";
        private string _selectedMachineStatus = "-";
        private string _selectedDatabaseStatus = "-";
        private string _selectedVisionProStatus = "-";
        private string _selectedUser = "-";
        private string _selectedRole = "-";

        public ObservableCollection<EventLogEntry> Events { get; } = new ObservableCollection<EventLogEntry>();
        public ObservableCollection<string> LevelOptions { get; } = new ObservableCollection<string>
        {
            "All",
            "Info",
            "Warning",
            "Error",
            "Critical",
            "Debug"
        };

        public ICollectionView EventsView { get; }

        public ICommand RefreshCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand ClearEventsCommand { get; }
        public ICommand ClearWarningInfoCommand { get; }
        public ICommand ClearOlderThanOneDayCommand { get; }
        public ICommand OpenSystemDiagnosticsCommand { get; }

        public AlarmsViewModel()
        {
            EventsView = CollectionViewSource.GetDefaultView(Events);
            EventsView.Filter = FilterEvents;

            RefreshCommand = new QtisVisionPanel.Models.RelayCommand(() => LoadEventsAsync().SafeFireAndForget());
            ClearFiltersCommand = new QtisVisionPanel.Models.RelayCommand(ClearFilters);
            ClearEventsCommand = new QtisVisionPanel.Models.RelayCommand(() => ClearEventsAsync().SafeFireAndForget());
            ClearWarningInfoCommand = new QtisVisionPanel.Models.RelayCommand(() => ClearWarningInfoAsync().SafeFireAndForget());
            ClearOlderThanOneDayCommand = new QtisVisionPanel.Models.RelayCommand(() => ClearOlderThanOneDayAsync().SafeFireAndForget());
            OpenSystemDiagnosticsCommand = new QtisVisionPanel.Models.RelayCommand(OpenSystemDiagnostics, () => CanOpenSystemDiagnostics);

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _systemDiagnosticsService.PropertyChanged += DiagnosticsService_PropertyChanged;
            UserSession.OnRoleChanged += UserSession_OnRoleChanged;
            _systemDiagnosticsService.Start();
        }

        public string DiagnosticsStatusText => _systemDiagnosticsService.OverallStatusText;
        public Brush DiagnosticsStatusBrush => _systemDiagnosticsService.OverallStatusBrush;
        public string DiagnosticsCpuText => _systemDiagnosticsService.CpuUsageText;
        public string DiagnosticsRamText => _systemDiagnosticsService.MemoryUsageText;
        public string DiagnosticsDiskText => _systemDiagnosticsService.WorstDiskText;
        public string DiagnosticsCpuDisplay => string.Format(GetMessage("Sub_entry_DiagnosticsCpuInlineFormat", "CPU {0}"), DiagnosticsCpuText);
        public string DiagnosticsRamDisplay => string.Format(GetMessage("Sub_entry_DiagnosticsRamInlineFormat", "RAM {0}"), DiagnosticsRamText);
        public string DiagnosticsDiskDisplay => string.Format(GetMessage("Sub_entry_DiagnosticsDiskInlineFormat", "Disk {0}"), DiagnosticsDiskText);
        public int DiagnosticsAlertCount => _systemDiagnosticsService.ActiveAlertCount;
        public string DiagnosticsAlertCountText => string.Format(GetMessage("Sub_entry_DiagnosticsAlertCountFormat", "{0} alert"), DiagnosticsAlertCount);
        public string DiagnosticsCleanupText => _systemDiagnosticsService.LastCleanupText;
        public bool CanManageEventMaintenance => UserSession.IsAdministrator || UserSession.IsInstaller;
        public bool CanOpenSystemDiagnostics => _authorizationService.CanAccessViewCached("SystemDiagnostics");
        public string SystemDiagnosticsPermissionToolTip => GetMessage(
            "Sub_entry_SystemDiagnosticsPermissionRequired",
            "PC diagnostics are reserved to Expert, Installer or Administrator.");

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool AutoRefresh
        {
            get => _autoRefresh;
            set
            {
                if (_autoRefresh != value)
                {
                    _autoRefresh = value;
                    OnPropertyChanged();
                    UpdateTimerState();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    EventsView.Refresh();
                }
            }
        }

        public string SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (_selectedLevel != value)
                {
                    _selectedLevel = value;
                    OnPropertyChanged();
                    EventsView.Refresh();
                }
            }
        }

        public EventLogEntry SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                if (_selectedEvent != value)
                {
                    _selectedEvent = value;
                    OnPropertyChanged();
                    UpdateSelectedEventDetails();
                }
            }
        }

        public int TotalEvents => Events.Count;
        public int WarningEvents => Events.Count(x => string.Equals(x.LevelText, "Warning", StringComparison.OrdinalIgnoreCase));
        public int ErrorEvents => Events.Count(x =>
            string.Equals(x.LevelText, "Error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.LevelText, "Critical", StringComparison.OrdinalIgnoreCase));

        public string DatabaseStatus
        {
            get => _databaseStatus;
            private set
            {
                if (_databaseStatus != value)
                {
                    _databaseStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string VisionProStatus
        {
            get => _visionProStatus;
            private set
            {
                if (_visionProStatus != value)
                {
                    _visionProStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MachineStatus
        {
            get => _machineStatus;
            private set
            {
                if (_machineStatus != value)
                {
                    _machineStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LastRefreshText
        {
            get => _lastRefreshText;
            private set
            {
                if (_lastRefreshText != value)
                {
                    _lastRefreshText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LastRefreshDisplayText));
                }
            }
        }

        public string LastRefreshDisplayText
        {
            get
            {
                return string.Format("{0}: {1}",
                    GetMessage("Sub_entry_LastRefresh", "Last refresh"),
                    LastRefreshText);
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedMachineStatus
        {
            get => _selectedMachineStatus;
            private set
            {
                if (_selectedMachineStatus != value)
                {
                    _selectedMachineStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedDatabaseStatus
        {
            get => _selectedDatabaseStatus;
            private set
            {
                if (_selectedDatabaseStatus != value)
                {
                    _selectedDatabaseStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedVisionProStatus
        {
            get => _selectedVisionProStatus;
            private set
            {
                if (_selectedVisionProStatus != value)
                {
                    _selectedVisionProStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedUser
        {
            get => _selectedUser;
            private set
            {
                if (_selectedUser != value)
                {
                    _selectedUser = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedRole
        {
            get => _selectedRole;
            private set
            {
                if (_selectedRole != value)
                {
                    _selectedRole = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedEventFullDetailsDisplay
        {
            get
            {
                if (_selectedEvent != null && !string.IsNullOrWhiteSpace(_selectedEvent.FullDetails))
                {
                    return _selectedEvent.FullDetails;
                }

                return GetMessage("Sub_entry_NoDetailsAvailable", "No details available");
            }
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                UpdateTimerState();
                return;
            }

            _isInitialized = true;
            UpdateTimerState();
            await LoadEventsAsync();
        }

        public async Task LoadEventsAsync()
        {
            if (IsBusy)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = GetMessage("Sub_entry_LoadingEvents", "Loading events...");

                var items = await _repository.GetRecentAsync(300);

                Events.Clear();
                foreach (var item in items)
                {
                    Events.Add(item);
                }

                SelectedEvent = Events.FirstOrDefault();
                UpdateStatusCards(items);
                UpdateSelectedEventDetails();
                LastRefreshText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                StatusMessage = $"{Events.Count} {GetMessage("Sub_entry_EventsLoaded", "events loaded")}";

                OnPropertyChanged(nameof(TotalEvents));
                OnPropertyChanged(nameof(WarningEvents));
                OnPropertyChanged(nameof(ErrorEvents));
                EventsView.Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"{GetMessage("Sub_entry_LoadFailed", "Load failed")}: {ex.Message}";
                MainWindow.logger?.Error($"Errore caricamento eventi: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Dispose()
        {
            _refreshTimer.Stop();
            _systemDiagnosticsService.PropertyChanged -= DiagnosticsService_PropertyChanged;
            UserSession.OnRoleChanged -= UserSession_OnRoleChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void UpdateStatusCards(System.Collections.Generic.IReadOnlyList<EventLogEntry> items)
        {
            var databaseStatus = items
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.DatabaseStatus))
                ?.DatabaseStatus;

            DatabaseStatus = !string.IsNullOrWhiteSpace(databaseStatus)
                ? databaseStatus
                : (items.Count > 0 ? "Connected" : "Unknown");

            var visionProStatus = items
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.VisionProStatus))
                ?.VisionProStatus;

            VisionProStatus = !string.IsNullOrWhiteSpace(visionProStatus)
                ? visionProStatus
                : (_machineRuntimeService.IsContinuousRunActive ? "Running" : "Stopped");

            var machineStatus = items
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.MachineStatus))
                ?.MachineStatus;

            MachineStatus = !string.IsNullOrWhiteSpace(machineStatus)
                ? machineStatus
                : _machineStatusService.CurrentStatus.ToString();
        }

        private void UpdateSelectedEventDetails()
        {
            SelectedMachineStatus = GetPreferredValue(_selectedEvent != null ? _selectedEvent.MachineStatus : null, MachineStatus);
            SelectedDatabaseStatus = GetPreferredValue(_selectedEvent != null ? _selectedEvent.DatabaseStatus : null, DatabaseStatus);
            SelectedVisionProStatus = GetPreferredValue(_selectedEvent != null ? _selectedEvent.VisionProStatus : null, VisionProStatus);
            SelectedUser = GetPreferredValue(_selectedEvent != null ? _selectedEvent.User : null, UserSession.CurrentUser);
            SelectedRole = GetPreferredValue(_selectedEvent != null ? _selectedEvent.Role : null, UserSession.CurrentRole);
            OnPropertyChanged(nameof(SelectedEventFullDetailsDisplay));
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (AutoRefresh)
            {
                LoadEventsAsync().SafeFireAndForget();
            }
        }

        private void DiagnosticsService_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(DiagnosticsStatusText));
            OnPropertyChanged(nameof(DiagnosticsStatusBrush));
            OnPropertyChanged(nameof(DiagnosticsCpuText));
            OnPropertyChanged(nameof(DiagnosticsRamText));
            OnPropertyChanged(nameof(DiagnosticsDiskText));
            OnPropertyChanged(nameof(DiagnosticsCpuDisplay));
            OnPropertyChanged(nameof(DiagnosticsRamDisplay));
            OnPropertyChanged(nameof(DiagnosticsDiskDisplay));
            OnPropertyChanged(nameof(DiagnosticsAlertCount));
            OnPropertyChanged(nameof(DiagnosticsAlertCountText));
            OnPropertyChanged(nameof(DiagnosticsCleanupText));
        }

        private void UserSession_OnRoleChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(CanManageEventMaintenance));
            OnPropertyChanged(nameof(CanOpenSystemDiagnostics));
            OnPropertyChanged(nameof(SystemDiagnosticsPermissionToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateTimerState()
        {
            if (AutoRefresh)
            {
                if (!_refreshTimer.IsEnabled)
                {
                    _refreshTimer.Start();
                }
            }
            else
            {
                _refreshTimer.Stop();
            }
        }

        private void OpenSystemDiagnostics()
        {
            if (!CanOpenSystemDiagnostics)
            {
                new SystemNotificationWindow(
                    GetMessage("Sub_entry_AuthorizationRequiredTitle", "Authorization required"),
                    GetMessage("Sub_entry_SystemDiagnosticsPermissionRequired", "PC diagnostics are reserved to Expert, Installer or Administrator."),
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
            {
                mainVm.OnNavigateRequested("SystemDiagnostics");
            }
        }

        private bool FilterEvents(object obj)
        {
            var item = obj as EventLogEntry;
            if (item == null)
                return false;

            if (!string.IsNullOrWhiteSpace(SelectedLevel) &&
                !string.Equals(SelectedLevel, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.LevelText, SelectedLevel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            var search = SearchText.Trim();
            return Contains(item.Message, search) ||
                   Contains(item.Category, search) ||
                   Contains(item.Source, search) ||
                   Contains(item.LogId, search) ||
                   Contains(item.Details, search) ||
                   Contains(item.User, search) ||
                   Contains(item.Recipe, search);
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            SelectedLevel = "All";
        }

        private async Task ClearEventsAsync()
        {
            if (IsBusy || !CanManageEventMaintenance)
                return;

            var dlgClearEvents = new SystemNotificationWindow(
                GetMessage("Sub_entry_ClearEventsTitle", "Clear events"),
                GetMessage("Sub_entry_ClearEventsConfirm", "Do you really want to clear all logged events?"),
                NotificationSeverity.Warning, true);
            dlgClearEvents.ShowDialog();
            if (!dlgClearEvents.Confirmed)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = GetMessage("Sub_entry_ClearEventsInProgress", "Clearing event table...");

                bool cleared = await _repository.ClearAllAsync();
                if (!cleared)
                {
                    StatusMessage = GetMessage("Sub_entry_ClearEventsFailed", "Unable to clear event table");
                    MainWindow.logger?.Warn("Clear events table failed.");
                    return;
                }

                Events.Clear();
                SelectedEvent = null;

                DatabaseStatus = "Connected";
                VisionProStatus = _machineRuntimeService.IsContinuousRunActive ? "Running" : "Stopped";
                MachineStatus = _machineStatusService.CurrentStatus.ToString();
                LastRefreshText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                StatusMessage = GetMessage("Sub_entry_ClearEventsDone", "Event table cleared");

                OnPropertyChanged(nameof(TotalEvents));
                OnPropertyChanged(nameof(WarningEvents));
                OnPropertyChanged(nameof(ErrorEvents));
                OnPropertyChanged(nameof(SelectedEventFullDetailsDisplay));
                EventsView.Refresh();

                MainWindow.logger?.Info("Event table tbllogevent cleared from AlarmsView.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore svuotamento eventi: {ex.Message}";
                MainWindow.logger?.Error($"Errore durante lo svuotamento di tbllogevent: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ClearWarningInfoAsync()
        {
            if (IsBusy || !CanManageEventMaintenance)
                return;

            var dlgClearWarning = new SystemNotificationWindow(
                GetMessage("Sub_entry_ClearWarningInfo", "Delete Warning / Info"),
                GetMessage("Sub_entry_ClearWarningInfoConfirm", "Do you really want to delete only Warning and Info events?"),
                NotificationSeverity.Warning, true);
            dlgClearWarning.ShowDialog();
            if (!dlgClearWarning.Confirmed)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = GetMessage("Sub_entry_ClearWarningInfoInProgress", "Deleting Warning / Info events...");

                bool cleared = await _repository.ClearWarningInfoAsync();
                if (!cleared)
                {
                    StatusMessage = GetMessage("Sub_entry_ClearWarningInfoFailed", "Unable to delete Warning / Info events");
                    MainWindow.logger?.Warn("Clear Warning/Info events failed.");
                    return;
                }

                await LoadEventsAfterMaintenanceAsync(GetMessage("Sub_entry_ClearWarningInfoDone", "Warning / Info events deleted"));
                MainWindow.logger?.Info("Warning/Info events deleted from tbllogevent.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore pulizia Warning/Info: {ex.Message}";
                MainWindow.logger?.Error($"Errore durante la pulizia Warning/Info di tbllogevent: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ClearOlderThanOneDayAsync()
        {
            if (IsBusy || !CanManageEventMaintenance)
                return;

            var dlgClearOlder = new SystemNotificationWindow(
                GetMessage("Sub_entry_ClearOlder", "Delete events older than 1 day"),
                GetMessage("Sub_entry_ClearOlderConfirm", "Do you really want to delete events older than 1 day?"),
                NotificationSeverity.Warning, true);
            dlgClearOlder.ShowDialog();
            if (!dlgClearOlder.Confirmed)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = GetMessage("Sub_entry_ClearOlderInProgress", "Deleting events older than 1 day...");

                bool cleared = await _repository.ClearOlderThanAsync(DateTime.Now.AddDays(-1));
                if (!cleared)
                {
                    StatusMessage = GetMessage("Sub_entry_ClearOlderFailed", "Unable to delete events older than 1 day");
                    MainWindow.logger?.Warn("Clear events older than one day failed.");
                    return;
                }

                await LoadEventsAfterMaintenanceAsync(GetMessage("Sub_entry_ClearOlderDone", "Events older than 1 day deleted"));
                MainWindow.logger?.Info("Events older than one day deleted from tbllogevent.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore pulizia eventi > 1 giorno: {ex.Message}";
                MainWindow.logger?.Error($"Errore durante la pulizia eventi > 1 giorno di tbllogevent: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadEventsAfterMaintenanceAsync(string completedMessage)
        {
            var items = await _repository.GetRecentAsync(300);

            Events.Clear();
            foreach (var item in items)
            {
                Events.Add(item);
            }

            SelectedEvent = Events.FirstOrDefault();
            UpdateStatusCards(items);
            UpdateSelectedEventDetails();
            LastRefreshText = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            StatusMessage = completedMessage;

            OnPropertyChanged(nameof(TotalEvents));
            OnPropertyChanged(nameof(WarningEvents));
            OnPropertyChanged(nameof(ErrorEvents));
            OnPropertyChanged(nameof(SelectedEventFullDetailsDisplay));
            EventsView.Refresh();
        }

        private static bool Contains(string source, string search)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetPreferredValue(string primaryValue, string fallbackValue)
        {
            if (!string.IsNullOrWhiteSpace(primaryValue))
            {
                return primaryValue;
            }

            if (!string.IsNullOrWhiteSpace(fallbackValue))
            {
                return fallbackValue;
            }

            return "-";
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
