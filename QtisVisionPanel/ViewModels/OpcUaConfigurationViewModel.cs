using NLog;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Extensions;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace QtisVisionPanel.ViewModels
{
    public class OpcUaConfigurationViewModel : INotifyPropertyChanged, IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly OpcUaConfigurationService _configurationService = new OpcUaConfigurationService();
        private bool _isBusy;
        private bool _enabled;
        private bool _requiredForMachineRun;
        private bool _useDatabaseConfiguration;
        private bool _useSecurity;
        private bool _autoAcceptUntrustedCertificates;
        private bool _remoteCommandsEnabled;
        private bool _remoteStartStopEnabled;
        private bool _remoteRecipeChangeEnabled;
        private bool _auditRemoteCommandsToDatabase;
        private string _serverUrl;
        private string _applicationName;
        private string _reconnectIntervalMs;
        private string _subscriptionIntervalMs;
        private string _sessionTimeoutMs;
        private string _operationTimeoutMs;
        private string _statusText;
        private string _runtimeStatusText;
        private string _operatorMessage;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ReconnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public ObservableCollection<OpcUaNodeEditorItem> Nodes { get; } = new ObservableCollection<OpcUaNodeEditorItem>();
        public string[] DirectionOptions { get; } = { "ServerToClient", "ClientToServer" };
        public string[] DataTypeOptions { get; } = { "Boolean", "String", "Int64", "Int32", "Double" };

        public string Title => GetMessage("Sub_entry_OpcUaConfigurationTitle", "OPC UA Communication");
        public string Subtitle => GetMessage("Sub_entry_OpcUaConfigurationSubtitle", "Connection settings, exchanged tags and data-flow direction");
        public string RefreshLabel => GetMessage("Sub_entry_OpcUaRefresh", "Refresh");
        public string SaveLabel => GetMessage("Sub_entry_OpcUaSave", "Save and reload");
        public string ReconnectLabel => GetMessage("Sub_entry_OpcUaReconnect", "Reconnect");
        public string DisconnectLabel => GetMessage("Sub_entry_OpcUaDisconnect", "Disconnect");
        public string StatusLabel => GetMessage("Sub_entry_OpcUaStatus", "Status");
        public string RuntimeStatusLabel => GetMessage("Sub_entry_OpcUaRuntimeStatus", "Runtime");
        public string ServerSettingsLabel => GetMessage("Sub_entry_OpcUaServerSettings", "Server settings");
        public string TagsLabel => GetMessage("Sub_entry_OpcUaTags", "Exchanged tags");
        public string DirectionDiagramLabel => GetMessage("Sub_entry_OpcUaDirectionDiagram", "Exchange direction");
        public string ServerSideLabel => GetMessage("Sub_entry_OpcUaServerSide", "PLC / SCADA / OPC UA Server");
        public string ClientSideLabel => GetMessage("Sub_entry_OpcUaClientSide", "QtisVision HMI");
        public string ServerToClientLabel => GetMessage("Sub_entry_OpcUaServerToClient", "Server -> HMI commands");
        public string ClientToServerLabel => GetMessage("Sub_entry_OpcUaClientToServer", "HMI -> Server status");
        public string EnabledLabel => GetMessage("Sub_entry_OpcUaEnabled", "Enable OPC UA");
        public string RequiredLabel => GetMessage("Sub_entry_OpcUaRequired", "Required for RunContinuous");
        public string UseDatabaseLabel => GetMessage("Sub_entry_OpcUaUseDatabase", "Use database overrides");
        public string UseSecurityLabel => GetMessage("Sub_entry_OpcUaUseSecurity", "Use security");
        public string AutoAcceptLabel => GetMessage("Sub_entry_OpcUaAutoAccept", "Auto accept certificates");
        public string RemoteCommandsLabel => GetMessage("Sub_entry_OpcUaRemoteCommands", "Allow remote commands");
        public string RemoteStartStopLabel => GetMessage("Sub_entry_OpcUaRemoteStartStop", "Allow remote Start/Stop");
        public string RemoteRecipeChangeLabel => GetMessage("Sub_entry_OpcUaRemoteRecipeChange", "Allow remote recipe change");
        public string AuditRemoteCommandsLabel => GetMessage("Sub_entry_OpcUaAuditRemoteCommands", "Audit remote commands");
        public string ServerUrlLabel => GetMessage("Sub_entry_OpcUaServerUrl", "Server URL");
        public string ApplicationNameLabel => GetMessage("Sub_entry_OpcUaApplicationName", "Application name");
        public string ReconnectIntervalLabel => GetMessage("Sub_entry_OpcUaReconnectInterval", "Reconnect ms");
        public string SubscriptionIntervalLabel => GetMessage("Sub_entry_OpcUaSubscriptionInterval", "Subscription ms");
        public string SessionTimeoutLabel => GetMessage("Sub_entry_OpcUaSessionTimeout", "Session timeout ms");
        public string OperationTimeoutLabel => GetMessage("Sub_entry_OpcUaOperationTimeout", "Operation timeout ms");
        public string ConfigPathLabel => GetMessage("Sub_entry_OpcUaConfigPath", "Configuration file");
        public string ConfigPath => _configurationService.ConfigPath;
        public string TagKeyHeader => GetMessage("Sub_entry_OpcUaTagKey", "Key");
        public string NodeIdHeader => GetMessage("Sub_entry_OpcUaNodeId", "NodeId");
        public string DirectionHeader => GetMessage("Sub_entry_OpcUaDirection", "Direction");
        public string DataTypeHeader => GetMessage("Sub_entry_OpcUaDataType", "Type");
        public string EnabledHeader => GetMessage("Sub_entry_OpcUaTagEnabled", "Enabled");
        public string RequiredHeader => GetMessage("Sub_entry_OpcUaTagRequired", "Required");
        public string DescriptionHeader => GetMessage("Sub_entry_OpcUaDescription", "Description");

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanExecuteCommands));
            }
        }

        public bool CanExecuteCommands => !IsBusy;

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public bool RequiredForMachineRun
        {
            get => _requiredForMachineRun;
            set { _requiredForMachineRun = value; OnPropertyChanged(); }
        }

        public bool UseDatabaseConfiguration
        {
            get => _useDatabaseConfiguration;
            set { _useDatabaseConfiguration = value; OnPropertyChanged(); }
        }

        public bool UseSecurity
        {
            get => _useSecurity;
            set { _useSecurity = value; OnPropertyChanged(); }
        }

        public bool AutoAcceptUntrustedCertificates
        {
            get => _autoAcceptUntrustedCertificates;
            set { _autoAcceptUntrustedCertificates = value; OnPropertyChanged(); }
        }

        public bool RemoteCommandsEnabled
        {
            get => _remoteCommandsEnabled;
            set { _remoteCommandsEnabled = value; OnPropertyChanged(); }
        }

        public bool RemoteStartStopEnabled
        {
            get => _remoteStartStopEnabled;
            set { _remoteStartStopEnabled = value; OnPropertyChanged(); }
        }

        public bool RemoteRecipeChangeEnabled
        {
            get => _remoteRecipeChangeEnabled;
            set { _remoteRecipeChangeEnabled = value; OnPropertyChanged(); }
        }

        public bool AuditRemoteCommandsToDatabase
        {
            get => _auditRemoteCommandsToDatabase;
            set { _auditRemoteCommandsToDatabase = value; OnPropertyChanged(); }
        }

        public string ServerUrl
        {
            get => _serverUrl;
            set { _serverUrl = value; OnPropertyChanged(); }
        }

        public string ApplicationName
        {
            get => _applicationName;
            set { _applicationName = value; OnPropertyChanged(); }
        }

        public string ReconnectIntervalMs
        {
            get => _reconnectIntervalMs;
            set { _reconnectIntervalMs = value; OnPropertyChanged(); }
        }

        public string SubscriptionIntervalMs
        {
            get => _subscriptionIntervalMs;
            set { _subscriptionIntervalMs = value; OnPropertyChanged(); }
        }

        public string SessionTimeoutMs
        {
            get => _sessionTimeoutMs;
            set { _sessionTimeoutMs = value; OnPropertyChanged(); }
        }

        public string OperationTimeoutMs
        {
            get => _operationTimeoutMs;
            set { _operationTimeoutMs = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string RuntimeStatusText
        {
            get => _runtimeStatusText;
            set { _runtimeStatusText = value; OnPropertyChanged(); }
        }

        public string OperatorMessage
        {
            get => _operatorMessage;
            set { _operatorMessage = value; OnPropertyChanged(); }
        }

        public int ServerToClientCount => Nodes.Count(n => string.Equals(n.Direction, "ServerToClient", StringComparison.OrdinalIgnoreCase));
        public int ClientToServerCount => Nodes.Count(n => string.Equals(n.Direction, "ClientToServer", StringComparison.OrdinalIgnoreCase));
        public string ServerToClientCountText => string.Format(CultureInfo.InvariantCulture, "{0} tag", ServerToClientCount);
        public string ClientToServerCountText => string.Format(CultureInfo.InvariantCulture, "{0} tag", ClientToServerCount);

        public OpcUaConfigurationViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => CanExecuteCommands);
            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanExecuteCommands);
            ReconnectCommand = new RelayCommand(async _ => await ReconnectAsync(), _ => CanExecuteCommands);
            DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(), _ => CanExecuteCommands);

            Nodes.CollectionChanged += (s, e) => RaiseNodeCounts();
            _ = LoadAsync();
        }

        public void Dispose()
        {
        }

        private async Task LoadAsync()
        {
            await RunBusyAsync(async () =>
            {
                var config = await _configurationService.LoadAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ApplyConfig(config));

                OperatorMessage = GetMessage("Sub_entry_OpcUaLoaded", "OPC UA configuration loaded.");
                RefreshRuntimeStatus();
            });
        }

        private async Task SaveAsync()
        {
            if (!Validate(out var message))
            {
                OperatorMessage = message;
                return;
            }

            if (Enabled && RequiredForMachineRun && !ConfirmRequiredMode())
            {
                OperatorMessage = GetMessage("Sub_entry_OpcUaSaveCancelled", "Save cancelled.");
                return;
            }

            await RunBusyAsync(async () =>
            {
                var config = BuildConfig();
                await _configurationService.SaveAsync(config);

                await ServiceLocator.OpcUaClientService.DisconnectAsync();
                await ServiceLocator.OpcUaClientService.InitializeAsync(CancellationToken.None);

                OperatorMessage = GetMessage("Sub_entry_OpcUaSaved", "OPC UA configuration saved and runtime reloaded.");
                RefreshRuntimeStatus();
                ServiceLocator.ApplicationEventLogger.LogOperationalEvent(
                    LogLevel.Info,
                    "OPCUA_CONFIGURATION_SAVED_FROM_UI",
                    "OPC UA",
                    "OPC UA configuration saved from HMI",
                    nameof(OpcUaConfigurationViewModel),
                    null,
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "enabled", config.Enabled },
                        { "required_for_machine_run", config.RequiredForMachineRun },
                        { "server_url", config.ServerUrl },
                        { "use_security", config.UseSecurity },
                        { "auto_accept_untrusted_certificates", config.AutoAcceptUntrustedCertificates },
                        { "remote_commands_enabled", config.RemoteCommandsEnabled },
                        { "remote_start_stop_enabled", config.RemoteStartStopEnabled },
                        { "remote_recipe_change_enabled", config.RemoteRecipeChangeEnabled },
                        { "node_count", config.Nodes?.Count ?? 0 }
                    });
                ServiceLocator.AuditLogService?.LogAsync(
                    "OPCUA_CONFIGURATION_SAVED",
                    string.IsNullOrWhiteSpace(UserSession.CurrentUser) ? "unknown" : UserSession.CurrentUser,
                    $"enabled={config.Enabled}; required={config.RequiredForMachineRun}; server={config.ServerUrl}; remoteCommands={config.RemoteCommandsEnabled}; startStop={config.RemoteStartStopEnabled}; recipeChange={config.RemoteRecipeChangeEnabled}; autoAccept={config.AutoAcceptUntrustedCertificates}",
                    null,
                    config.ServerUrl).SafeFireAndForget(Logger, "AUDIT_OPCUA_CONFIG_SAVE_FAILED");
            });
        }

        private async Task ReconnectAsync()
        {
            await RunBusyAsync(async () =>
            {
                await ServiceLocator.OpcUaClientService.DisconnectAsync();
                await ServiceLocator.OpcUaClientService.InitializeAsync(CancellationToken.None);
                OperatorMessage = GetMessage("Sub_entry_OpcUaReconnectDone", "OPC UA reconnect requested.");
                RefreshRuntimeStatus();
            });
        }

        private async Task DisconnectAsync()
        {
            await RunBusyAsync(async () =>
            {
                await ServiceLocator.OpcUaClientService.DisconnectAsync();
                OperatorMessage = GetMessage("Sub_entry_OpcUaDisconnectDone", "OPC UA disconnected.");
                RefreshRuntimeStatus();
            });
        }

        private async Task RunBusyAsync(Func<Task> action)
        {
            if (IsBusy)
            {
                return;
            }

            try
            {
                IsBusy = true;
                await action();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "OPC UA configuration page operation failed.");
                OperatorMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1}",
                    GetMessage("Sub_entry_OpcUaOperationFailed", "OPC UA operation failed:"),
                    ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyConfig(OpcUaConfig config)
        {
            Enabled = config.Enabled;
            RequiredForMachineRun = config.RequiredForMachineRun;
            UseDatabaseConfiguration = config.UseDatabaseConfiguration;
            ServerUrl = config.ServerUrl;
            ApplicationName = config.ApplicationName;
            UseSecurity = config.UseSecurity;
            AutoAcceptUntrustedCertificates = config.AutoAcceptUntrustedCertificates;
            RemoteCommandsEnabled = config.RemoteCommandsEnabled;
            RemoteStartStopEnabled = config.RemoteStartStopEnabled;
            RemoteRecipeChangeEnabled = config.RemoteRecipeChangeEnabled;
            AuditRemoteCommandsToDatabase = config.AuditRemoteCommandsToDatabase;
            ReconnectIntervalMs = config.ReconnectIntervalMs.ToString(CultureInfo.InvariantCulture);
            SubscriptionIntervalMs = config.SubscriptionIntervalMs.ToString(CultureInfo.InvariantCulture);
            SessionTimeoutMs = config.SessionTimeoutMs.ToString(CultureInfo.InvariantCulture);
            OperationTimeoutMs = config.OperationTimeoutMs.ToString(CultureInfo.InvariantCulture);

            Nodes.Clear();
            foreach (var node in config.Nodes ?? OpcUaConfig.CreateDefaultNodes())
            {
                var item = new OpcUaNodeEditorItem(node);
                item.PropertyChanged += Node_PropertyChanged;
                Nodes.Add(item);
            }

            RaiseNodeCounts();
            RefreshRuntimeStatus();
            OnPropertyChanged(nameof(ConfigPath));
        }

        private OpcUaConfig BuildConfig()
        {
            return new OpcUaConfig
            {
                Enabled = Enabled,
                RequiredForMachineRun = RequiredForMachineRun,
                UseDatabaseConfiguration = UseDatabaseConfiguration,
                ServerUrl = ServerUrl?.Trim(),
                ApplicationName = ApplicationName?.Trim(),
                UseSecurity = UseSecurity,
                AutoAcceptUntrustedCertificates = AutoAcceptUntrustedCertificates,
                RemoteCommandsEnabled = RemoteCommandsEnabled,
                RemoteStartStopEnabled = RemoteStartStopEnabled,
                RemoteRecipeChangeEnabled = RemoteRecipeChangeEnabled,
                AuditRemoteCommandsToDatabase = AuditRemoteCommandsToDatabase,
                ReconnectIntervalMs = ParseInt(ReconnectIntervalMs, 5000),
                SubscriptionIntervalMs = ParseInt(SubscriptionIntervalMs, 250),
                SessionTimeoutMs = ParseInt(SessionTimeoutMs, 60000),
                OperationTimeoutMs = ParseInt(OperationTimeoutMs, 5000),
                Nodes = Nodes.Select(n => n.ToConfig()).ToList()
            };
        }

        private bool Validate(out string message)
        {
            if (string.IsNullOrWhiteSpace(ServerUrl))
            {
                message = GetMessage("Sub_entry_OpcUaInvalidServerUrl", "Server URL is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ApplicationName))
            {
                message = GetMessage("Sub_entry_OpcUaInvalidApplicationName", "Application name is required.");
                return false;
            }

            if (!ValidateInt(ReconnectIntervalMs, 1000, nameof(ReconnectIntervalMs), out message) ||
                !ValidateInt(SubscriptionIntervalMs, 100, nameof(SubscriptionIntervalMs), out message) ||
                !ValidateInt(SessionTimeoutMs, 10000, nameof(SessionTimeoutMs), out message) ||
                !ValidateInt(OperationTimeoutMs, 1000, nameof(OperationTimeoutMs), out message))
            {
                return false;
            }

            foreach (var node in Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Key) || string.IsNullOrWhiteSpace(node.NodeId))
                {
                    message = GetMessage("Sub_entry_OpcUaInvalidNode", "Each enabled tag needs a key and NodeId.");
                    return false;
                }
            }

            message = null;
            return true;
        }

        private bool ValidateInt(string text, int minimum, string field, out string message)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < minimum)
            {
                message = string.Format(
                    CultureInfo.InvariantCulture,
                    GetMessage("Sub_entry_OpcUaInvalidNumber", "{0} must be at least {1}."),
                    field,
                    minimum);
                return false;
            }

            message = null;
            return true;
        }

        private int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private bool ConfirmRequiredMode()
        {
            var dialog = new SystemNotificationWindow(
                GetMessage("Sub_entry_OpcUaConfirmRequiredTitle", "Confirm required OPC UA"),
                GetMessage("Sub_entry_OpcUaConfirmRequiredMessage", "Required mode can block RunContinuous when OPC UA is disconnected. Continue?"),
                NotificationSeverity.Warning,
                true);
            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        private void RefreshRuntimeStatus()
        {
            var service = ServiceLocator.OpcUaClientService;
            StatusText = service.LastStatusMessage;
            RuntimeStatusText = service.IsEnabled
                ? service.IsConnected
                    ? GetMessage("Sub_entry_OpcUaRuntimeConnected", "Connected")
                    : GetMessage("Sub_entry_OpcUaRuntimeDisconnected", "Disconnected")
                : GetMessage("Sub_entry_OpcUaRuntimeDisabled", "Disabled");
        }

        private void RaiseNodeCounts()
        {
            OnPropertyChanged(nameof(ServerToClientCount));
            OnPropertyChanged(nameof(ClientToServerCount));
            OnPropertyChanged(nameof(ServerToClientCountText));
            OnPropertyChanged(nameof(ClientToServerCountText));
        }

        private void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpcUaNodeEditorItem.Direction))
            {
                RaiseNodeCounts();
            }
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class OpcUaNodeEditorItem : INotifyPropertyChanged
    {
        private string _key;
        private string _nodeId;
        private string _direction;
        private string _dataType;
        private bool _enabled;
        private bool _required;
        private string _description;

        public event PropertyChangedEventHandler PropertyChanged;

        public OpcUaNodeEditorItem(OpcUaNodeConfig config)
        {
            _key = config?.Key;
            _nodeId = config?.NodeId;
            _direction = config?.Direction;
            _dataType = config?.DataType;
            _enabled = config?.Enabled ?? true;
            _required = config?.Required ?? false;
            _description = config?.Description;
        }

        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        public string NodeId
        {
            get => _nodeId;
            set { _nodeId = value; OnPropertyChanged(); }
        }

        public string Direction
        {
            get => _direction;
            set
            {
                _direction = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DirectionText));
                OnPropertyChanged(nameof(DirectionBrush));
            }
        }

        public string DataType
        {
            get => _dataType;
            set { _dataType = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public bool Required
        {
            get => _required;
            set { _required = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string DirectionText => string.Equals(Direction, "ServerToClient", StringComparison.OrdinalIgnoreCase)
            ? "Server -> HMI"
            : "HMI -> Server";

        public Brush DirectionBrush => string.Equals(Direction, "ServerToClient", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(35, 120, 183))
            : new SolidColorBrush(Color.FromRgb(46, 158, 117));

        public OpcUaNodeConfig ToConfig()
        {
            return new OpcUaNodeConfig(
                Key?.Trim(),
                NodeId?.Trim(),
                string.IsNullOrWhiteSpace(Direction) ? "ClientToServer" : Direction.Trim(),
                string.IsNullOrWhiteSpace(DataType) ? "String" : DataType.Trim(),
                Enabled,
                Required,
                Description?.Trim());
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
