using Newtonsoft.Json;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NLog;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Service for communicating with an Allen-Bradley PowerFlex 525 variable-frequency drive.
    ///
    /// The service polls drive parameters on a configurable interval and exposes
    /// them as bindable collections so the <c>PowerFlex525ViewModel</c> can surface
    /// current frequency, fault codes and change history to the operator.
    ///
    /// Connectivity is treated as best-effort: if the drive does not respond at
    /// startup, the service schedules a single retry after 10 minutes and then
    /// marks the endpoint unreachable to avoid flooding the event log with
    /// continuous poll failures.  This prevents unnecessary noise when the drive
    /// is not wired or not powered on a bench that does not include the inverter.
    /// </summary>
    public class PowerFlex525Service : INotifyPropertyChanged, IDisposable
    {
        private readonly ObservableCollection<PowerFlex525ParameterItem> _parameters;
        private readonly ObservableCollection<PowerFlex525ChangeHistoryEntry> _history = new ObservableCollection<PowerFlex525ChangeHistoryEntry>();
        private readonly ObservableCollection<PowerFlex525ParameterItem> _modifiedParameters = new ObservableCollection<PowerFlex525ParameterItem>();
        private readonly PowerFlex525ChangeHistoryRepository _historyRepository = new PowerFlex525ChangeHistoryRepository();
        private readonly DispatcherTimer _pollTimer;
        private IPowerFlex525Client _client;
        private CancellationTokenSource _refreshCts;
        private bool _isRefreshing;
        private bool _isAvailable;
        private bool _isRunning;
        private string _statusText = "PowerFlex 525 non inizializzato";
        private string _connectionStatusText = "Non inizializzato";
        private string _lastLoggedConnectionStatus;
        private string _driveStatusText = "STOP";
        private string _faultStatusText = "Nessun fault";
        private int? _lastFaultAlarmCode;
        private double? _metersPerMinute;
        private DateTime? _lastRefresh;
        private bool _speedFallbackLogged;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<PowerFlex525ParameterItem> Parameters => _parameters;
        public ObservableCollection<PowerFlex525ChangeHistoryEntry> History => _history;
        public ObservableCollection<PowerFlex525ParameterItem> ModifiedParameters => _modifiedParameters;

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set { _isRefreshing = value; OnPropertyChanged(); }
        }

        public bool IsAvailable
        {
            get => _isAvailable;
            private set { _isAvailable = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DriveStateText));
            }
        }

        public string DriveStateText => _driveStatusText;

        public string FaultStatusText
        {
            get => _faultStatusText;
            private set { _faultStatusText = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            private set { _connectionStatusText = value; OnPropertyChanged(); }
        }

        public double? MetersPerMinute
        {
            get => _metersPerMinute;
            private set
            {
                _metersPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MetersPerMinuteText));
            }
        }

        public string MetersPerMinuteText => MetersPerMinute.HasValue ? MetersPerMinute.Value.ToString("0.##") : "--";

        public DateTime? LastRefresh
        {
            get => _lastRefresh;
            private set
            {
                _lastRefresh = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastRefreshText));
            }
        }

        public string LastRefreshText => LastRefresh.HasValue ? LastRefresh.Value.ToString("dd/MM/yyyy HH:mm:ss") : "--";

        public PowerFlex525Service()
        {
            _parameters = new ObservableCollection<PowerFlex525ParameterItem>(
                PowerFlex525ParameterCatalog.All.Select(definition => new PowerFlex525ParameterItem { Definition = definition }));

            _pollTimer = new DispatcherTimer();
            _pollTimer.Tick += async (s, e) => await RefreshAsync();
            RebuildClientFromConfig();
            LoadLocalHistory();
            _ = _historyRepository.EnsureTableAsync();
        }

        public void Start()
        {
            var settings = MainWindow.configManager?.Config?.PowerFlex525 ?? new Cls_Config.Calss_structure.PowerFlex525Settings();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, settings.PollIntervalMs));
            _pollTimer.Start();
        }

        public void Stop()
        {
            _pollTimer.Stop();
            _refreshCts?.Cancel();
        }

        public void RebuildClientFromConfig()
        {
            var settings = MainWindow.configManager?.Config?.PowerFlex525 ?? new Cls_Config.Calss_structure.PowerFlex525Settings();
            _client = settings.Enabled
                ? new PowerFlex525EtherNetIpClient(settings.IpAddress, settings.TimeoutMs)
                : null;

            if (!settings.Enabled)
            {
                StatusText = "PowerFlex 525 disabilitato da Config.xml";
                ConnectionStatusText = "Disabilitato";
            }
        }

        public async Task<bool> UpdateSpeedFactorAsync(double metersPerMinutePerHz)
        {
            if (MainWindow.configManager?.Config == null)
            {
                MainWindow.logger?.Warn("PowerFlex525 speed factor save skipped: config manager not available.");
                return false;
            }

            if (metersPerMinutePerHz < 0)
            {
                MainWindow.logger?.Warn(
                    $"PowerFlex525 speed factor rejected: factor={metersPerMinutePerHz}.");
                return false;
            }

            var settings = MainWindow.configManager.Config.PowerFlex525 ?? new Cls_Config.Calss_structure.PowerFlex525Settings();
            settings.MetersPerMinutePerHz = metersPerMinutePerHz;
            MainWindow.configManager.Config.PowerFlex525 = settings;

            await MainWindow.configManager.SaveConfigAsync();

            _speedFallbackLogged = false;
            CalculateMetersPerMinute();
            MainWindow.logger?.Info(
                $"PowerFlex525 speed factor saved: MetersPerMinutePerHz={metersPerMinutePerHz:0.###}.");
            ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                LogLevel.Info,
                "POWERFLEX525_SPEED_FACTOR_SAVED",
                "PowerFlex525",
                "PowerFlex speed factor saved",
                nameof(PowerFlex525Service),
                $"MetersPerMinutePerHz={metersPerMinutePerHz:0.###}");
            return true;
        }

        public async Task RefreshAsync()
        {
            if (IsRefreshing)
                return;

            IsRefreshing = true;
            _refreshCts?.Cancel();
            _refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                if (_client == null)
                {
                    MarkUnavailable("PowerFlex 525 disabilitato");
                    return;
                }

                var snapshot = await _client.ReadDriveSnapshotAsync(_refreshCts.Token);
                IsAvailable = snapshot.IsAvailable;
                StatusText = snapshot.StatusMessage ?? (snapshot.IsAvailable ? "Drive disponibile" : "Drive non disponibile");
                ConnectionStatusText = snapshot.IsAvailable ? "EtherNet/IP OK" : "Non raggiungibile";
                if (!snapshot.IsAvailable)
                {
                    MarkUnavailable(StatusText);
                    LastRefresh = DateTime.Now;
                    LogConnectionStatus();
                    return;
                }

                var values = await _client.ReadParametersAsync(PowerFlex525ParameterCatalog.TrackedParameterNumbers, _refreshCts.Token);
                UpdateParameterValues(values);
                if (values != null && values.Any(v => string.Equals(v.Error, "CIP adapter parametri non collegato", StringComparison.OrdinalIgnoreCase)))
                {
                    ConnectionStatusText = "EtherNet/IP OK - CIP adapter non collegato";
                }

                var modifiedValues = await _client.ReadModifiedParametersAsync(_refreshCts.Token);
                UpdateModifiedParameterValues(modifiedValues);
                CalculateMetersPerMinute();
                UpdateDriveDiagnostics();
                LastRefresh = DateTime.Now;
                LogConnectionStatus();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 refresh non bloccante fallito: {ex.Message}");
                MarkUnavailable(ex.Message);
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        public async Task<IReadOnlyList<PowerFlex525ChangeHistoryEntry>> ApplyChangesAsync(IEnumerable<PowerFlex525ParameterItem> changedItems)
        {
            var applied = new List<PowerFlex525ChangeHistoryEntry>();
            if (_client == null)
                return applied;

            foreach (var item in changedItems.Where(p => p.HasPendingChange))
            {
                if (item.RequiresStop && IsRunning)
                {
                    MainWindow.logger?.Warn($"PowerFlex525 parameter {item.Code} requires stopped drive. Apply blocked.");
                    continue;
                }

                if (!double.TryParse(item.EditValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var newValue))
                    continue;

                bool ok = await _client.WriteParameterAsync(item.Number, newValue, CancellationToken.None);
                var entry = CreateHistoryEntry(item, newValue, ok ? "Applied" : "Rejected");
                applied.Add(entry);
                AddHistory(entry);
                _ = _historyRepository.InsertAsync(entry);

                if (ok)
                {
                    item.CurrentValue = newValue;
                    item.AcceptCurrentAsEditValue();
                }
            }

            return applied;
        }

        public void CancelPendingChanges()
        {
            foreach (var item in Parameters)
            {
                item.AcceptCurrentAsEditValue();
            }
        }

        public void RestoreDefaultEditableValues()
        {
            foreach (var item in Parameters.Where(p => p.IsEditable))
            {
                item.EditValue = string.Empty;
            }
        }

        public async Task<bool> ResetFaultAsync()
        {
            return await ExecuteFaultCommandAsync(
                command: () => _client?.ResetFaultAsync(CancellationToken.None) ?? Task.FromResult(false),
                commandValue: 1,
                resultText: "Fault reset");
        }

        public async Task<bool> ClearFaultHistoryAsync()
        {
            return await ExecuteFaultCommandAsync(
                command: () => _client?.ClearFaultHistoryAsync(CancellationToken.None) ?? Task.FromResult(false),
                commandValue: 2,
                resultText: "Fault history clear");
        }

        public void AddHistory(PowerFlex525ChangeHistoryEntry entry)
        {
            if (entry == null)
                return;

            _history.Insert(0, entry);
            while (_history.Count > 200)
                _history.RemoveAt(_history.Count - 1);

            SaveLocalHistory();
        }

        private void UpdateParameterValues(IReadOnlyList<PowerFlex525ParameterValue> values)
        {
            var map = values?.ToDictionary(v => v.Number) ?? new Dictionary<int, PowerFlex525ParameterValue>();
            foreach (var item in Parameters)
            {
                if (!map.TryGetValue(item.Number, out var value))
                    continue;

                item.CurrentValue = value.Value;
                item.IsAvailable = value.IsAvailable;
                item.Error = value.Error;
                if (!item.HasPendingChange)
                    item.AcceptCurrentAsEditValue();
            }
        }

        private void UpdateDriveDiagnostics()
        {
            var frequency = Parameters.FirstOrDefault(p => p.Number == 1)?.CurrentValue;
            var driveStatus = Parameters.FirstOrDefault(p => p.Number == 6)?.CurrentValue;
            var fault1 = Parameters.FirstOrDefault(p => p.Number == 7)?.CurrentValue;
            var fault2 = Parameters.FirstOrDefault(p => p.Number == 8)?.CurrentValue;
            var fault3 = Parameters.FirstOrDefault(p => p.Number == 9)?.CurrentValue;

            IsRunning = frequency.GetValueOrDefault() > 0.1;
            _driveStatusText = IsRunning ? "RUN" : "STOP";
            if (driveStatus.HasValue)
            {
                _driveStatusText += $" (b006={driveStatus.Value:0})";
            }

            int fault1Code = Convert.ToInt32(fault1.GetValueOrDefault());
            int fault2Code = Convert.ToInt32(fault2.GetValueOrDefault());
            int fault3Code = Convert.ToInt32(fault3.GetValueOrDefault());
            FaultStatusText = fault1Code > 0
                ? $"FAULT F{fault1Code:000} (storico: F{fault2Code:000}, F{fault3Code:000})"
                : "Nessun fault attivo/storico recente";

            OnPropertyChanged(nameof(DriveStateText));
            if (fault1Code > 0 && _lastFaultAlarmCode != fault1Code)
            {
                _lastFaultAlarmCode = fault1Code;
                string message = $"PowerFlex 525 fault F{fault1Code:000}";
                string details = $"DriveIp={MainWindow.configManager?.Config?.PowerFlex525?.IpAddress}; b006={driveStatus?.ToString("0") ?? "N/D"}; b007={fault1Code}; b008={fault2Code}; b009={fault3Code}";
                MainWindow.logger?.Warn($"{message}. {details}");
                ServiceLocator.ApplicationEventLogger.LogAlarmEvent(message, nameof(PowerFlex525Service), details, false);
            }
            else if (fault1Code == 0 && _lastFaultAlarmCode.HasValue)
            {
                MainWindow.logger?.Info($"PowerFlex525 fault cleared. Previous fault F{_lastFaultAlarmCode.Value:000}.");
                _lastFaultAlarmCode = null;
            }
        }

        private void UpdateModifiedParameterValues(IReadOnlyList<PowerFlex525ParameterValue> values)
        {
            _modifiedParameters.Clear();
            foreach (var value in values ?? new List<PowerFlex525ParameterValue>())
            {
                var definition = PowerFlex525ParameterCatalog.Find(value.Number) ??
                    new PowerFlex525ParameterDefinition
                    {
                        Number = value.Number,
                        Code = $"M{value.Number:000}",
                        DisplayName = "Modified parameter",
                        Group = PowerFlex525ParameterGroup.Modified
                    };

                _modifiedParameters.Add(new PowerFlex525ParameterItem
                {
                    Definition = definition,
                    CurrentValue = value.Value,
                    IsAvailable = value.IsAvailable,
                    Error = value.Error
                });
            }

            OnPropertyChanged(nameof(ModifiedParameters));
        }

        private void CalculateMetersPerMinute()
        {
            var settings = MainWindow.configManager?.Config?.PowerFlex525 ?? new Cls_Config.Calss_structure.PowerFlex525Settings();
            var frequency = Parameters.FirstOrDefault(p => p.Number == 1)?.CurrentValue;

            if (!frequency.HasValue)
            {
                MetersPerMinute = null;
                return;
            }

            if (settings.MetersPerMinutePerHz > 0)
            {
                MetersPerMinute = frequency.Value * settings.MetersPerMinutePerHz;
                _speedFallbackLogged = false;
                return;
            }
            MetersPerMinute = null;
            if (!_speedFallbackLogged)
            {
                _speedFallbackLogged = true;
                MainWindow.logger?.Warn(
                    "PowerFlex525 speed unavailable: configure only PowerFlex525.MetersPerMinutePerHz (factor K) to calculate m/min.");
            }
        }

        private void MarkUnavailable(string message)
        {
            IsAvailable = false;
            IsRunning = false;
            StatusText = message;
            ConnectionStatusText = message;
            MetersPerMinute = null;
            _speedFallbackLogged = false;
            foreach (var item in Parameters)
            {
                item.IsAvailable = false;
                item.Error = message;
            }
        }

        private void LogConnectionStatus()
        {
            var status = $"{ConnectionStatusText} | {StatusText}";
            if (string.Equals(_lastLoggedConnectionStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            _lastLoggedConnectionStatus = status;
            MainWindow.logger?.Info($"PowerFlex525 connection status: {status}");
        }

        private PowerFlex525ChangeHistoryEntry CreateHistoryEntry(PowerFlex525ParameterItem item, double newValue, string result)
        {
            return new PowerFlex525ChangeHistoryEntry
            {
                Timestamp = DateTime.Now,
                User = UserSession.CurrentUser,
                Role = UserSession.CurrentRole,
                DriveIp = MainWindow.configManager?.Config?.PowerFlex525?.IpAddress,
                ParameterNumber = item.Number,
                ParameterCode = item.Code,
                ParameterName = item.DisplayName,
                OldValue = item.CurrentValue,
                NewValue = newValue,
                Unit = item.Unit,
                Source = "PowerFlex525View",
                Result = result
            };
        }

        private string ResolveHistoryPath()
        {
            var configured = MainWindow.configManager?.Config?.PowerFlex525?.HistoryFilePath;
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerFlex525ChangeHistory.json");
        }

        private void LoadLocalHistory()
        {
            try
            {
                var path = ResolveHistoryPath();
                if (!File.Exists(path))
                    return;

                var entries = JsonConvert.DeserializeObject<List<PowerFlex525ChangeHistoryEntry>>(File.ReadAllText(path)) ?? new List<PowerFlex525ChangeHistoryEntry>();
                foreach (var entry in entries.OrderByDescending(e => e.Timestamp).Take(200))
                    _history.Add(entry);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 history load failed: {ex.Message}");
            }
        }

        private void SaveLocalHistory()
        {
            try
            {
                var path = ResolveHistoryPath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonConvert.SerializeObject(_history.Take(200).ToList(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 history save failed: {ex.Message}");
            }
        }

        private async Task<bool> ExecuteFaultCommandAsync(Func<Task<bool>> command, int commandValue, string resultText)
        {
            if (_client == null)
            {
                MainWindow.logger?.Warn($"PowerFlex525 {resultText} skipped: service disabled.");
                return false;
            }

            bool ok = await command();
            var entry = CreateCommandHistoryEntry(commandValue, resultText, ok ? "Applied" : "Rejected");
            AddHistory(entry);
            _ = _historyRepository.InsertAsync(entry);

            string driveIp = MainWindow.configManager?.Config?.PowerFlex525?.IpAddress ?? "N/D";
            if (ok)
            {
                MainWindow.logger?.Info($"PowerFlex525 {resultText} applied on drive {driveIp}.");
                ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                    LogLevel.Info,
                    $"POWERFLEX525_{resultText.Replace(" ", string.Empty).ToUpperInvariant()}",
                    "PowerFlex525",
                    $"{resultText} applied",
                    nameof(PowerFlex525Service),
                    $"DriveIp={driveIp}; Command=A551={commandValue}");
                await RefreshAsync();
            }
            else
            {
                MainWindow.logger?.Warn($"PowerFlex525 {resultText} rejected on drive {driveIp}.");
                ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                    LogLevel.Warn,
                    $"POWERFLEX525_{resultText.Replace(" ", string.Empty).ToUpperInvariant()}_FAILED",
                    "PowerFlex525",
                    $"{resultText} rejected",
                    nameof(PowerFlex525Service),
                    $"DriveIp={driveIp}; Command=A551={commandValue}");
            }

            return ok;
        }

        private PowerFlex525ChangeHistoryEntry CreateCommandHistoryEntry(int commandValue, string resultText, string result)
        {
            return new PowerFlex525ChangeHistoryEntry
            {
                Timestamp = DateTime.Now,
                User = UserSession.CurrentUser,
                Role = UserSession.CurrentRole,
                DriveIp = MainWindow.configManager?.Config?.PowerFlex525?.IpAddress,
                ParameterNumber = 551,
                ParameterCode = "A551",
                ParameterName = resultText,
                OldValue = null,
                NewValue = commandValue,
                Unit = string.Empty,
                Source = "PowerFlex525View",
                Result = result
            };
        }

        public void Dispose()
        {
            Stop();
            _refreshCts?.Dispose();
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
