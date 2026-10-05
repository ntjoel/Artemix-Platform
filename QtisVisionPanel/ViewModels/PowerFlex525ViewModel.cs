using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace QtisVisionPanel.ViewModels
{
    public class PowerFlex525ViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly PowerFlex525Service _service = ServiceLocator.PowerFlex525Service;
        private string _operatorMessage;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand ResetFaultCommand { get; }
        public ICommand ClearFaultHistoryCommand { get; }
        public ICommand SaveMachineSettingsCommand { get; }

        public string Title => GetMessage("Sub_entry_PowerFlex525Title", "PowerFlex 525");
        public string Subtitle => GetMessage("Sub_entry_PowerFlex525Subtitle", "Conveyor inverter monitor, Rockwell parameters, m/min speed and change history");
        public string RefreshLabel => GetMessage("Sub_entry_PowerFlex525Refresh", "Refresh");
        public string ApplyLabel => GetMessage("Sub_entry_PowerFlex525Apply", "Apply");
        public string CancelLabel => GetMessage("Sub_entry_PowerFlex525Cancel", "Cancel");
        public string RestoreLabel => GetMessage("Sub_entry_PowerFlex525Restore", "Restore");
        public string ResetFaultLabel => GetMessage("Sub_entry_PowerFlex525ResetFault", "Reset fault");
        public string ClearFaultHistoryLabel => GetMessage("Sub_entry_PowerFlex525ClearFaultHistory", "Clear fault history");
        public string DriveIpLabel => GetMessage("Sub_entry_PowerFlex525DriveIp", "Drive IP");
        public string DriveStateLabel => GetMessage("Sub_entry_PowerFlex525DriveState", "Drive state");
        public string FaultStatusLabel => GetMessage("Sub_entry_PowerFlex525FaultStatus", "Fault status");
        public string ConnectionStatusLabel => GetMessage("Sub_entry_PowerFlex525ConnectionStatus", "Connection");
        public string BeltSpeedLabel => GetMessage("Sub_entry_PowerFlex525BeltSpeed", "Belt speed");
        public string LastRefreshLabel => GetMessage("Sub_entry_PowerFlex525LastRefresh", "Last refresh");
        public string MonitorLabel => GetMessage("Sub_entry_PowerFlex525Monitor", "Monitor");
        public string MotorLabel => GetMessage("Sub_entry_PowerFlex525Motor", "Motor");
        public string RampLimitLabel => GetMessage("Sub_entry_PowerFlex525RampLimit", "Ramps and limits");
        public string PendingChangesLabel => GetMessage("Sub_entry_PowerFlex525PendingChanges", "Pending changes");
        public string ModifiedParametersLabel => GetMessage("Sub_entry_PowerFlex525ModifiedParameters", "Modified parameters");
        public string HistoryLabel => GetMessage("Sub_entry_PowerFlex525History", "Change history");
        public string StopRuleLabel => GetMessage("Sub_entry_PowerFlex525StopRule", "If a parameter requires STOP and the drive is in RUN, Apply remains disabled.");
        public string MachineSettingsLabel => GetMessage("Sub_entry_PowerFlex525MachineSettings", "Speed factor K");
        public string SaveMachineSettingsLabel => GetMessage("Sub_entry_PowerFlex525SaveMachineSettings", "Save speed factor");
        public string MetersPerMinutePerHzLabel => GetMessage("Sub_entry_PowerFlex525MetersPerMinutePerHz", "Factor K");
        public string MachineSettingsHint => GetMessage("Sub_entry_PowerFlex525MachineSettingsHint", "Speed is calculated only as b001 x K.");

        public ObservableCollection<PowerFlex525ParameterItem> Parameters => _service.Parameters;
        public ObservableCollection<PowerFlex525ChangeHistoryEntry> History => _service.History;
        public ObservableCollection<PowerFlex525ParameterItem> ModifiedParameters => _service.ModifiedParameters;

        public ObservableCollection<PowerFlex525ParameterItem> MonitorParameters { get; }
        public ObservableCollection<PowerFlex525ParameterItem> MotorParameters { get; }
        public ObservableCollection<PowerFlex525ParameterItem> RampLimitParameters { get; }
        public ObservableCollection<PowerFlex525ParameterItem> EditableParameters { get; }

        public bool IsAvailable => _service.IsAvailable;
        public bool IsRunning => _service.IsRunning;
        public bool IsRefreshing => _service.IsRefreshing;
        public string StatusText => _service.StatusText;
        public string ConnectionStatusText => _service.ConnectionStatusText;
        public string DriveStateText => _service.DriveStateText;
        public string FaultStatusText => _service.FaultStatusText;
        public string LastRefreshText => _service.LastRefreshText;
        public string MetersPerMinuteText => _service.MetersPerMinuteText;
        public string DriveIp => MainWindow.configManager?.Config?.PowerFlex525?.IpAddress ?? string.Empty;
        public string MachineMetersPerMinutePerHz { get; set; }

        public string OperatorMessage
        {
            get => _operatorMessage;
            set { _operatorMessage = value; OnPropertyChanged(); }
        }

        public bool HasPendingChanges => Parameters.Any(p => p.HasPendingChange);

        public bool HasStopBlockedChanges => IsRunning && Parameters.Any(p => p.HasPendingChange && p.RequiresStop);

        public bool CanApply => IsAvailable && HasPendingChanges && !HasStopBlockedChanges && !IsRefreshing;

        public PowerFlex525ViewModel()
        {
            MonitorParameters = new ObservableCollection<PowerFlex525ParameterItem>(
                Parameters.Where(p => p.Group == PowerFlex525ParameterGroup.Monitor));
            MotorParameters = new ObservableCollection<PowerFlex525ParameterItem>(
                Parameters.Where(p => p.Group == PowerFlex525ParameterGroup.Motor));
            RampLimitParameters = new ObservableCollection<PowerFlex525ParameterItem>(
                Parameters.Where(p => p.Group == PowerFlex525ParameterGroup.RampLimit));
            EditableParameters = new ObservableCollection<PowerFlex525ParameterItem>(
                Parameters.Where(p => p.IsEditable));

            RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
            ApplyCommand = new RelayCommand(async _ => await ApplyAsync(), _ => CanApply);
            CancelCommand = new RelayCommand(_ => CancelPendingChanges());
            RestoreCommand = new RelayCommand(_ => RestoreDefaults());
            ResetFaultCommand = new RelayCommand(async _ => await ResetFaultAsync(), _ => IsAvailable && !IsRefreshing);
            ClearFaultHistoryCommand = new RelayCommand(async _ => await ClearFaultHistoryAsync(), _ => IsAvailable && !IsRefreshing);
            SaveMachineSettingsCommand = new RelayCommand(async _ => await SaveMachineSettingsAsync(), _ => !IsRefreshing);

            LoadMachineSettingsFromConfig();

            foreach (var item in Parameters)
                item.PropertyChanged += Parameter_PropertyChanged;

            _service.PropertyChanged += Service_PropertyChanged;
            _service.Start();
        }

        private async Task RefreshAsync()
        {
            await _service.RefreshAsync();
            OperatorMessage = GetMessage("Sub_entry_PowerFlex525RefreshDone", "PowerFlex 525 refresh completed");
        }

        private async Task ApplyAsync()
        {
            if (!CanApply)
            {
                OperatorMessage = HasStopBlockedChanges
                    ? GetMessage("Sub_entry_PowerFlex525StopRequired", "Stop drive before applying selected parameters.")
                    : GetMessage("Sub_entry_PowerFlex525ApplyUnavailable", "Apply unavailable.");
                return;
            }

            var changes = Parameters.Where(p => p.HasPendingChange).ToList();
            if (!ConfirmPrivilegedOperation(
                GetMessage("Sub_entry_PowerFlex525ConfirmApplyTitle", "Confirm drive parameter apply"),
                BuildApplyConfirmationMessage(changes)))
            {
                OperatorMessage = GetMessage("Sub_entry_PowerFlex525ApplyCancelled", "Apply cancelled.");
                return;
            }

            var applied = await _service.ApplyChangesAsync(changes);
            OperatorMessage = string.Format(
                GetMessage("Sub_entry_PowerFlex525ApplyResult", "{0} pending changes processed."),
                applied.Count);

            RaiseStateChanged();
        }

        private void CancelPendingChanges()
        {
            _service.CancelPendingChanges();
            OperatorMessage = GetMessage("Sub_entry_PowerFlex525CancelDone", "Pending changes cancelled.");
            RaiseStateChanged();
        }

        private void RestoreDefaults()
        {
            _service.RestoreDefaultEditableValues();
            OperatorMessage = GetMessage("Sub_entry_PowerFlex525RestoreHint", "Editable fields cleared. Apply only after verifying machine data.");
            RaiseStateChanged();
        }

        private async Task ResetFaultAsync()
        {
            if (!ConfirmPrivilegedOperation(
                GetMessage("Sub_entry_PowerFlex525ConfirmResetFaultTitle", "Confirm drive fault reset"),
                GetMessage("Sub_entry_PowerFlex525ConfirmResetFaultMessage", "Send a fault reset command to the PowerFlex 525 drive? Verify the fault cause has been removed before continuing.")))
            {
                return;
            }

            bool ok = await _service.ResetFaultAsync();
            OperatorMessage = ok
                ? GetMessage("Sub_entry_PowerFlex525ResetFaultDone", "Fault reset command sent.")
                : GetMessage("Sub_entry_PowerFlex525ResetFaultFailed", "Fault reset failed.");
            RaiseStateChanged();
        }

        private async Task ClearFaultHistoryAsync()
        {
            if (!ConfirmPrivilegedOperation(
                GetMessage("Sub_entry_PowerFlex525ConfirmClearHistoryTitle", "Confirm clear fault history"),
                GetMessage("Sub_entry_PowerFlex525ConfirmClearHistoryMessage", "Clear the PowerFlex 525 fault history buffer? This removes diagnostic history from the drive.")))
            {
                return;
            }

            bool ok = await _service.ClearFaultHistoryAsync();
            OperatorMessage = ok
                ? GetMessage("Sub_entry_PowerFlex525ClearFaultHistoryDone", "Fault history clear command sent.")
                : GetMessage("Sub_entry_PowerFlex525ClearFaultHistoryFailed", "Fault history clear failed.");
            RaiseStateChanged();
        }

        private static bool ConfirmPrivilegedOperation(string title, string message)
        {
            if (!(UserSession.IsAdministrator || UserSession.IsInstaller || UserSession.IsExpert))
            {
                new SystemNotificationWindow(
                    GetMessage("Sub_entry_AuthorizationRequiredTitle", "Authorization required"),
                    GetMessage("Sub_entry_TechnicalRoleRequired", "This action is reserved to Expert, Installer or Administrator."),
                    NotificationSeverity.Warning).ShowDialog();
                return false;
            }

            var dialog = new SystemNotificationWindow(title, message, NotificationSeverity.Warning, true);
            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        private static string BuildApplyConfirmationMessage(System.Collections.Generic.IEnumerable<PowerFlex525ParameterItem> changes)
        {
            var changeList = changes.ToList();
            var lines = changeList
                .Take(8)
                .Select(item => $"{item.Code} - {item.DisplayName}: {item.CurrentValueText} -> {item.EditValue}")
                .ToList();

            if (changeList.Count > lines.Count)
            {
                lines.Add($"... altri {changeList.Count - lines.Count} parametri");
            }

            return "Stai per scrivere parametri sul drive PowerFlex 525:\n\n" +
                   string.Join("\n", lines) +
                   "\n\nContinuare?";
        }

        private async Task SaveMachineSettingsAsync()
        {
            if (!TryParseInvariant(MachineMetersPerMinutePerHz, out double metersPerMinutePerHz) ||
                metersPerMinutePerHz < 0)
            {
                OperatorMessage = GetMessage("Sub_entry_PowerFlex525MachineSettingsInvalid", "Invalid machine settings values.");
                return;
            }

            bool ok = await _service.UpdateSpeedFactorAsync(metersPerMinutePerHz);

            OperatorMessage = ok
                ? GetMessage("Sub_entry_PowerFlex525MachineSettingsSaved", "Machine settings saved.")
                : GetMessage("Sub_entry_PowerFlex525MachineSettingsSaveFailed", "Machine settings save failed.");

            if (ok)
            {
                LoadMachineSettingsFromConfig();
            }

            RaiseStateChanged();
        }

        private void Parameter_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PowerFlex525ParameterItem.HasPendingChange) ||
                e.PropertyName == nameof(PowerFlex525ParameterItem.EditValue))
            {
                RaiseStateChanged();
            }
        }

        private void Service_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsRefreshing));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(DriveStateText));
            OnPropertyChanged(nameof(FaultStatusText));
            OnPropertyChanged(nameof(LastRefreshText));
            OnPropertyChanged(nameof(MetersPerMinuteText));
            OnPropertyChanged(nameof(ModifiedParameters));
            OnPropertyChanged(nameof(HasPendingChanges));
            OnPropertyChanged(nameof(HasStopBlockedChanges));
            OnPropertyChanged(nameof(CanApply));
            CommandManager.InvalidateRequerySuggested();
        }

        public void Dispose()
        {
            foreach (var item in Parameters)
                item.PropertyChanged -= Parameter_PropertyChanged;

            _service.PropertyChanged -= Service_PropertyChanged;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        private void LoadMachineSettingsFromConfig()
        {
            var settings = MainWindow.configManager?.Config?.PowerFlex525 ?? new Cls_Config.Calss_structure.PowerFlex525Settings();
            MachineMetersPerMinutePerHz = settings.MetersPerMinutePerHz.ToString("0.###", CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(MachineMetersPerMinutePerHz));
        }

        private static bool TryParseInvariant(string text, out double value)
        {
            return double.TryParse(
                (text ?? string.Empty).Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
