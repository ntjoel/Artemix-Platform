using NLog;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// OPC UA client used by the HMI to exchange production commands and
    /// inspection data with MES/SCADA/PLC layers.
    ///
    /// Important runtime rule:
    /// - Enabled means the client should try to communicate.
    /// - RequiredForMachineRun decides whether communication failure blocks
    ///   RunContinuous. The default configuration keeps this false so the
    ///   machine can continue producing when the external server is offline.
    ///
    /// The service also owns the command/data handshake tags so each external
    /// command can be acknowledged with command id, success flag and message.
    /// </summary>
    public sealed class OpcUaClientService : IOpcUaClientService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly OpcUaConfigurationService _configurationService = new OpcUaConfigurationService();
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _earlyWarningWriteGate = new SemaphoreSlim(1, 1);
        private readonly object _syncRoot = new object();

        private ApplicationConfiguration _applicationConfiguration;
        private Session _session;
        private Subscription _subscription;
        private Timer _reconnectTimer;
        private long _heartbeat;
        private long _dataSequence;
        private long _earlyWarningSequence;
        private long _lastCommandId;
        private long _lastProcessedCommandId;
        private long _lastDataAckSequence;
        private bool _pendingStart;
        private bool _pendingStop;
        private string _pendingRecipeName;
        private long _pendingRecipeId;
        private bool _lastDataAckOk;
        private string _lastDataAckMessage;
        private DateTime _lastConnectRequestAuditUtc = DateTime.MinValue;
        private bool _disposed;

        // Commands are raised back to MainWindow because start/stop/recipe
        // changes must pass through the same runtime gates used by the HMI.
        public event EventHandler<OpcUaCommandEventArgs> OpcStartRequested;
        public event EventHandler<OpcUaCommandEventArgs> OpcStopRequested;
        public event EventHandler<OpcUaCommandEventArgs> OpcRecipeChangeRequested;

        public bool IsEnabled => CurrentConfig?.Enabled == true;
        public bool IsRequiredForMachineRun => CurrentConfig?.Enabled == true && CurrentConfig.RequiredForMachineRun;

        public bool IsConnected
        {
            get
            {
                lock (_syncRoot)
                {
                    return _session != null && _session.Connected;
                }
            }
        }

        public string LastStatusMessage { get; private set; } = "OPC UA not initialized.";
        public OpcUaConfig CurrentConfig { get; private set; }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            CurrentConfig = await _configurationService.LoadAsync().ConfigureAwait(false);
            LastStatusMessage = CurrentConfig.Enabled
                ? "OPC UA configuration loaded."
                : "OPC UA disabled.";

            LogOpcUaStatus(
                CurrentConfig.Enabled,
                CurrentConfig.RequiredForMachineRun,
                false,
                nameof(InitializeAsync),
                LastStatusMessage,
                "OPCUA_CONFIGURATION_LOADED",
                LogLevel.Info,
                true);
            LogOpcUaSecurityPosture();

            if (!CurrentConfig.Enabled)
            {
                // Disabled communication is an intentional machine state, not
                // a fault. Ensure stale sessions are closed and report disabled.
                await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await DisconnectInternalAsync().ConfigureAwait(false);
                }
                finally
                {
                    _connectionLock.Release();
                }
                return;
            }

            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            StartReconnectTimer();
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (!IsEnabled || _disposed)
            {
                return;
            }

            if (ShouldLogConnectRequestAudit())
            {
                LogOpcUaEvent(
                    LogLevel.Info,
                    "OPCUA_CONNECT_REQUESTED",
                    "OPC UA connection requested",
                    nameof(ConnectAsync),
                    $"ServerUrl={CurrentConfig?.ServerUrl}; RequiredForMachineRun={CurrentConfig?.RequiredForMachineRun}");
            }

            await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    return;
                }

                await DisconnectInternalAsync().ConfigureAwait(false);
                _applicationConfiguration = await BuildApplicationConfigurationAsync(CurrentConfig).ConfigureAwait(false);

                // Endpoint selection honors the XML/DB security settings. This
                // is where Ignition/SCADA certificate and security mismatches
                // surface as configuration errors in the event log.
                var selectedEndpoint = CoreClientUtils.SelectEndpoint(
                    _applicationConfiguration,
                    CurrentConfig.ServerUrl,
                    CurrentConfig.UseSecurity,
                    CurrentConfig.OperationTimeoutMs);

                var endpointConfiguration = EndpointConfiguration.Create(_applicationConfiguration);
                var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);

                var session = await Session.Create(
                    _applicationConfiguration,
                    endpoint,
                    false,
                    CurrentConfig.ApplicationName,
                    (uint)Math.Max(CurrentConfig.SessionTimeoutMs, 10000),
                    null,
                    null).ConfigureAwait(false);

                session.KeepAlive += OnKeepAlive;

                lock (_syncRoot)
                {
                    _session = session;
                }

                CreateSubscriptions(session);
                LastStatusMessage = $"OPC UA connected to {CurrentConfig.ServerUrl}.";
                _logger.Info(LastStatusMessage);
                LogOpcUaStatus(
                    true,
                    CurrentConfig.RequiredForMachineRun,
                    true,
                    nameof(ConnectAsync),
                    LastStatusMessage,
                    "OPCUA_CONNECTED",
                    LogLevel.Info,
                    true);
            }
            catch (Exception ex)
            {
                LastStatusMessage = $"OPC UA connection failed: {ex.Message}";
                _logger.Warn(ex, "OPCUA_CONNECT_FAILED");
                LogOpcUaStatus(
                    true,
                    CurrentConfig?.RequiredForMachineRun == true,
                    false,
                    nameof(ConnectAsync),
                    LastStatusMessage,
                    "OPCUA_CONNECT_FAILED",
                    CurrentConfig?.RequiredForMachineRun == true ? LogLevel.Error : LogLevel.Warn,
                    false,
                    new Dictionary<string, object>
                    {
                        { "server_url", CurrentConfig?.ServerUrl },
                        { "exception_type", ex.GetType().FullName }
                    });
                await DisconnectInternalAsync().ConfigureAwait(false);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            await _connectionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await DisconnectInternalAsync().ConfigureAwait(false);
                LastStatusMessage = "OPC UA disconnected.";
                LogOpcUaStatus(
                    IsEnabled,
                    CurrentConfig?.RequiredForMachineRun == true,
                    false,
                    nameof(DisconnectAsync),
                    LastStatusMessage,
                    "OPCUA_DISCONNECTED",
                    LogLevel.Info,
                    true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public bool CanMachineRun(out string reason)
        {
            if (!IsRequiredForMachineRun)
            {
                // OPC UA can be monitored without being a production interlock.
                // Keep this path permissive unless the commissioning parameter
                // explicitly requires communication for RunContinuous.
                reason = null;
                return true;
            }

            if (IsConnected)
            {
                reason = null;
                return true;
            }

            reason = "OPC UA communication is enabled and required, but the client is not connected.";
            return false;
        }

        public Task WriteCountersAsync(long total, long good, long bad)
        {
            if (!IsEnabled || !IsConnected)
            {
                // Publishing counters is best-effort when OPC UA is optional.
                // Required communication is enforced by CanMachineRun instead.
                return Task.CompletedTask;
            }

            WriteValue("TotalCount", total);
            WriteValue("GoodCount", good);
            WriteValue("BadCount", bad);
            return WriteHeartbeatAsync();
        }

        public Task WriteLastResultAsync(bool isGood)
        {
            if (!IsEnabled || !IsConnected)
            {
                return Task.CompletedTask;
            }

            WriteValue("LastResult", isGood);
            return Task.CompletedTask;
        }

        public Task WriteInspectionSnapshotAsync(bool isGood, long total, long good, long bad, string recipeName, long recipeId)
        {
            if (!IsEnabled || !IsConnected)
            {
                return Task.CompletedTask;
            }

            var ok = true;
            ok &= WriteValue("LastResult", isGood);
            ok &= WriteValue("TotalCount", total);
            ok &= WriteValue("GoodCount", good);
            ok &= WriteValue("BadCount", bad);
            ok &= WriteValue("CurrentRecipe", recipeName ?? string.Empty);
            ok &= WriteValue("CurrentRecipeId", recipeId);
            ok &= WriteValue("Heartbeat", Interlocked.Increment(ref _heartbeat));

            var sequence = Interlocked.Increment(ref _dataSequence);
            var message = ok
                ? "HMI data snapshot written successfully."
                : "HMI data snapshot completed with one or more OPC UA write errors.";

            WriteValue("DataPublishOk", ok);
            WriteValue("DataPublishMessage", message);
            WriteValue("DataPublishTimestamp", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            WriteValue("DataSequence", sequence);

            if (!ok)
            {
                LogOpcUaEvent(
                    LogLevel.Warn,
                    "OPCUA_DATA_SNAPSHOT_FAILED",
                    "OPC UA data snapshot completed with write errors",
                    nameof(WriteInspectionSnapshotAsync),
                    message,
                    new Dictionary<string, object>
                    {
                        { "sequence", sequence },
                        { "total", total },
                        { "good", good },
                        { "bad", bad },
                        { "recipe_name", recipeName },
                        { "recipe_id", recipeId }
                    });
            }

            return Task.CompletedTask;
        }

        public Task WriteCommandAcknowledgementAsync(long commandId, bool ok, string message)
        {
            if (!IsEnabled || !IsConnected)
            {
                return Task.CompletedTask;
            }

            WriteValue("CommandAckOk", ok);
            WriteValue("CommandAckMessage", message ?? string.Empty);
            WriteValue("CommandAckTimestamp", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            WriteValue("CommandAckId", commandId);
            LogOpcUaEvent(
                ok ? LogLevel.Info : LogLevel.Warn,
                ok ? "OPCUA_COMMAND_ACK_OK" : "OPCUA_COMMAND_ACK_FAILED",
                ok ? "OPC UA command acknowledged successfully" : "OPC UA command acknowledged with failure",
                nameof(WriteCommandAcknowledgementAsync),
                message,
                new Dictionary<string, object>
                {
                    { "command_id", commandId },
                    { "ack_ok", ok }
                });
            return Task.CompletedTask;
        }

        /// <summary>
        /// Publishes read-only machine health nodes to OPC UA.
        /// Called once per inspection cycle alongside <see cref="WriteInspectionSnapshotAsync"/>.
        ///
        /// <paramref name="systemHealthStatus"/> uses the conventional codes:
        /// <c>OK</c>, <c>CAMERA_MISSING</c>, <c>DB_DEGRADED</c>, <c>ENCODER_UNCALIBRATED</c>.
        /// SCADA/MES can monitor this node to detect degraded-mode production without
        /// relying on the absence of new inspection events.
        /// </summary>
        public Task WriteHealthSnapshotAsync(string systemHealthStatus, string lastAlarmCode, string activeHoldReasons)
        {
            if (!IsEnabled || !IsConnected)
                return Task.CompletedTask;

            WriteValue("SystemHealthStatus", systemHealthStatus ?? "OK");
            WriteValue("LastAlarmCode", lastAlarmCode ?? string.Empty);
            WriteValue("ActiveHoldReasons", activeHoldReasons ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task WriteIsRunningAsync(bool isRunning)
        {
            if (!IsEnabled || !IsConnected)
            {
                return Task.CompletedTask;
            }

            WriteValue("IsRunning", isRunning);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Publishes a machine-health EARLY WARNING to OPC UA so MES/SCADA can anticipate
        /// upstream problems (inspection drift, PC health degradation) before they cause scrap.
        ///
        /// Read-only ClientToServer nodes; each node publishes ONLY if the customer has mapped it
        /// in the OPC UA node configuration (unmapped keys are a safe no-op). The monotonic
        /// <c>EarlyWarningSequence</c> lets the MES detect a new warning even when the text repeats.
        /// Called by <c>OpcUaNotificationChannel</c>; no-op if OPC UA is disabled or disconnected.
        /// </summary>
        public async Task WriteEarlyWarningAsync(string severity, string code, string message, double? etaValue, string etaUnit, int count)
        {
            if (!IsEnabled || !IsConnected)
            {
                return;
            }

            if (!_earlyWarningWriteGate.Wait(0))
            {
                _logger.Debug("OPCUA_EARLY_WARNING_WRITE_SKIPPED_BUSY");
                return;
            }

            Task writeTask = Task.Run(() =>
            {
                bool active = !string.Equals(severity, "None", StringComparison.OrdinalIgnoreCase) && count > 0;

                WriteValue("EarlyWarningActive", active);
                WriteValue("EarlyWarningSeverity", severity ?? "None");
                WriteValue("EarlyWarningCode", code ?? string.Empty);
                WriteValue("EarlyWarningMessage", TrimOpcText(message, 1000));
                WriteValue("EarlyWarningEtaValue", etaValue ?? 0.0);
                WriteValue("EarlyWarningEtaUnit", etaUnit ?? string.Empty);
                WriteValue("EarlyWarningCount", (long)count);
                WriteValue("EarlyWarningTimestamp", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                WriteValue("EarlyWarningSequence", Interlocked.Increment(ref _earlyWarningSequence));
            });

            Task completedTask = await Task.WhenAny(writeTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (completedTask != writeTask)
            {
                _logger.Warn("OPCUA_EARLY_WARNING_WRITE_TIMEOUT|timeout_ms=5000");
                _ = writeTask.ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        _logger.Warn(t.Exception.GetBaseException(), "OPCUA_EARLY_WARNING_WRITE_LATE_FAILED");
                    }

                    ReleaseEarlyWarningWriteGate();
                }, TaskContinuationOptions.ExecuteSynchronously);
                return;
            }

            try
            {
                await writeTask.ConfigureAwait(false);
            }
            finally
            {
                ReleaseEarlyWarningWriteGate();
            }
        }

        public Task WriteCurrentRecipeAsync(string recipeName)
        {
            if (!IsEnabled || !IsConnected)
            {
                return Task.CompletedTask;
            }

            WriteValue("CurrentRecipe", recipeName ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task WriteCurrentRecipeIdAsync(long recipeId)
        {
            if (!IsEnabled || !IsConnected)
            {
                return Task.CompletedTask;
            }

            WriteValue("CurrentRecipeId", recipeId);
            return Task.CompletedTask;
        }

        public Task WriteHeartbeatAsync()
        {
            if (!IsEnabled || !IsConnected)
            {
                return Task.CompletedTask;
            }

            WriteValue("Heartbeat", Interlocked.Increment(ref _heartbeat));
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _disposed = true;
            try
            {
                _reconnectTimer?.Dispose();
            }
            catch
            {
            }

            DisconnectAsync().GetAwaiter().GetResult();
            _connectionLock.Dispose();
            _earlyWarningWriteGate.Dispose();
        }

        private static async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync(OpcUaConfig config)
        {
            var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OPC", "pki");
            var ownPath = Path.Combine(basePath, "own");
            var trustedPath = Path.Combine(basePath, "trusted");
            var issuerPath = Path.Combine(basePath, "issuers");
            var rejectedPath = Path.Combine(basePath, "rejected");
            var trustedUserPath = Path.Combine(basePath, "trusted_user");
            var userIssuerPath = Path.Combine(basePath, "user_issuers");

            Directory.CreateDirectory(ownPath);
            Directory.CreateDirectory(trustedPath);
            Directory.CreateDirectory(issuerPath);
            Directory.CreateDirectory(rejectedPath);
            Directory.CreateDirectory(trustedUserPath);
            Directory.CreateDirectory(userIssuerPath);

            var applicationConfig = new ApplicationConfiguration
            {
                ApplicationName = config.ApplicationName,
                ApplicationUri = $"urn:{Utils.GetHostName()}:{config.ApplicationName}",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = "Directory",
                        StorePath = ownPath,
                        SubjectName = config.ApplicationName
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = issuerPath
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = trustedPath
                    },
                    TrustedUserCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = trustedUserPath
                    },
                    UserIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = userIssuerPath
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = rejectedPath
                    },
                    AutoAcceptUntrustedCertificates = config.AutoAcceptUntrustedCertificates,
                    AddAppCertToTrustedStore = true
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = Math.Max(config.OperationTimeoutMs, 1000)
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = Math.Max(config.SessionTimeoutMs, 10000)
                },
                DisableHiResClock = false
            };

            await applicationConfig.Validate(ApplicationType.Client).ConfigureAwait(false);

            var applicationInstance = new ApplicationInstance(applicationConfig)
            {
                ApplicationName = config.ApplicationName,
                ApplicationType = ApplicationType.Client,
                ApplicationConfiguration = applicationConfig
            };

            var certificateReady = await applicationInstance
                .CheckApplicationInstanceCertificatesAsync(true, 60, CancellationToken.None)
                .ConfigureAwait(false);

            if (!certificateReady)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    $"OPC UA application certificate could not be created or loaded from '{ownPath}'.");
            }

            if (config.AutoAcceptUntrustedCertificates)
            {
                applicationConfig.CertificateValidator.CertificateValidation += (sender, e) =>
                {
                    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
                    {
                        e.Accept = true;
                    }
                };
            }

            return applicationConfig;
        }

        private void CreateSubscriptions(Session session)
        {
            var inboundNodes = GetNodes("ServerToClient").Where(node => node.Enabled).ToList();
            if (inboundNodes.Count == 0)
            {
                return;
            }

            var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = Math.Max(CurrentConfig.SubscriptionIntervalMs, 100)
            };

            foreach (var node in inboundNodes)
            {
                try
                {
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        DisplayName = node.Key,
                        StartNodeId = NodeId.Parse(node.NodeId),
                        AttributeId = Attributes.Value,
                        SamplingInterval = Math.Max(CurrentConfig.SubscriptionIntervalMs, 100),
                        QueueSize = 1,
                        DiscardOldest = true
                    };
                    item.Notification += OnMonitoredItemNotification;
                    subscription.AddItem(item);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"OPCUA_SUBSCRIPTION_NODE_SKIPPED|key={node.Key}|nodeId={node.NodeId}");
                    LogOpcUaEvent(
                        LogLevel.Warn,
                        "OPCUA_SUBSCRIPTION_NODE_SKIPPED",
                        "OPC UA subscription node skipped",
                        nameof(CreateSubscriptions),
                        ex.Message,
                        new Dictionary<string, object>
                        {
                            { "node_key", node.Key },
                            { "node_id", node.NodeId },
                            { "exception_type", ex.GetType().FullName }
                        });
                }
            }

            if (subscription.MonitoredItemCount == 0)
            {
                LogOpcUaEvent(
                    LogLevel.Warn,
                    "OPCUA_SUBSCRIPTION_EMPTY",
                    "OPC UA subscription has no monitored items",
                    nameof(CreateSubscriptions),
                    "No enabled server-to-client node could be subscribed.");
                return;
            }

            session.AddSubscription(subscription);
            subscription.Create();
            _subscription = subscription;
            LogOpcUaEvent(
                LogLevel.Info,
                "OPCUA_SUBSCRIPTION_CREATED",
                "OPC UA subscription created",
                nameof(CreateSubscriptions),
                $"Monitored items: {subscription.MonitoredItemCount}",
                new Dictionary<string, object>
                {
                    { "monitored_item_count", subscription.MonitoredItemCount }
                });
        }

        private void OnMonitoredItemNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                foreach (var value in item.DequeueValues())
                {
                    ProcessInboundValue(item.DisplayName, value.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"OPCUA_MONITORED_ITEM_FAILED|key={item?.DisplayName}");
                LogOpcUaEvent(
                    LogLevel.Warn,
                    "OPCUA_MONITORED_ITEM_FAILED",
                    "OPC UA monitored item processing failed",
                    nameof(OnMonitoredItemNotification),
                    ex.Message,
                    new Dictionary<string, object>
                    {
                        { "node_key", item?.DisplayName },
                        { "exception_type", ex.GetType().FullName }
                    });
            }
        }

        private void ProcessInboundValue(string key, object value)
        {
            if (string.Equals(key, "CommandId", StringComparison.OrdinalIgnoreCase))
            {
                _lastCommandId = ConvertToInt64(value);
                LogOpcUaEvent(
                    LogLevel.Info,
                    "OPCUA_COMMAND_ID_RECEIVED",
                    "OPC UA command sequence received",
                    nameof(ProcessInboundValue),
                    $"CommandId={_lastCommandId}",
                    new Dictionary<string, object>
                    {
                        { "command_id", _lastCommandId }
                    });
                ProcessSequencedCommandIfReady();
                return;
            }

            if (string.Equals(key, "Start", StringComparison.OrdinalIgnoreCase) && ConvertToBoolean(value))
            {
                _pendingStart = true;
                if (_lastCommandId > 0)
                {
                    ProcessSequencedCommandIfReady();
                }
                else
                {
                    DispatchRemoteCommand(0, "Start", "true", false, OpcStartRequested);
                }
                return;
            }

            if (string.Equals(key, "Stop", StringComparison.OrdinalIgnoreCase) && ConvertToBoolean(value))
            {
                _pendingStop = true;
                if (_lastCommandId > 0)
                {
                    ProcessSequencedCommandIfReady();
                }
                else
                {
                    DispatchRemoteCommand(0, "Stop", "true", false, OpcStopRequested);
                }
                return;
            }

            if (string.Equals(key, "RecipeName", StringComparison.OrdinalIgnoreCase))
            {
                var recipeName = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(recipeName))
                {
                    _pendingRecipeName = recipeName.Trim();
                    if (_lastCommandId > 0)
                    {
                        ProcessSequencedCommandIfReady();
                    }
                    else
                    {
                        DispatchRemoteCommand(0, "RecipeName", _pendingRecipeName, false, OpcRecipeChangeRequested);
                    }
                }
                return;
            }

            if (string.Equals(key, "RecipeId", StringComparison.OrdinalIgnoreCase))
            {
                var recipeId = ConvertToInt64(value);
                if (recipeId > 0)
                {
                    _pendingRecipeId = recipeId;
                    if (_lastCommandId > 0)
                    {
                        ProcessSequencedCommandIfReady();
                    }
                    else
                    {
                        DispatchRemoteCommand(0, "RecipeId", recipeId.ToString(CultureInfo.InvariantCulture), false, OpcRecipeChangeRequested);
                    }
                }
                return;
            }

            if (string.Equals(key, "DataAckSequence", StringComparison.OrdinalIgnoreCase))
            {
                var ackSequence = ConvertToInt64(value);
                if (ackSequence > 0 && ackSequence != _lastDataAckSequence)
                {
                    _lastDataAckSequence = ackSequence;
                    _logger.Info($"OPCUA_DATA_ACK_RECEIVED|sequence={ackSequence}|ok={_lastDataAckOk}|message={_lastDataAckMessage}");
                    LogOpcUaEvent(
                        _lastDataAckOk ? LogLevel.Info : LogLevel.Warn,
                        _lastDataAckOk ? "OPCUA_DATA_ACK_RECEIVED" : "OPCUA_DATA_ACK_FAILED",
                        "OPC UA data snapshot acknowledgement received",
                        nameof(ProcessInboundValue),
                        _lastDataAckMessage,
                        new Dictionary<string, object>
                        {
                            { "sequence", ackSequence },
                            { "ack_ok", _lastDataAckOk }
                        });
                }
                return;
            }

            if (string.Equals(key, "DataAckOk", StringComparison.OrdinalIgnoreCase))
            {
                _lastDataAckOk = ConvertToBoolean(value);
                return;
            }

            if (string.Equals(key, "DataAckMessage", StringComparison.OrdinalIgnoreCase))
            {
                _lastDataAckMessage = Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private void ProcessSequencedCommandIfReady()
        {
            if (_lastCommandId <= 0 || _lastCommandId == _lastProcessedCommandId)
            {
                return;
            }

            if (_pendingStop)
            {
                _lastProcessedCommandId = _lastCommandId;
                _pendingStop = false;
                _pendingStart = false;
                DispatchRemoteCommand(_lastCommandId, "Stop", "true", true, OpcStopRequested);
                return;
            }

            if (_pendingStart)
            {
                _lastProcessedCommandId = _lastCommandId;
                _pendingStart = false;
                DispatchRemoteCommand(_lastCommandId, "Start", "true", true, OpcStartRequested);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_pendingRecipeName))
            {
                var recipeName = _pendingRecipeName;
                _lastProcessedCommandId = _lastCommandId;
                _pendingRecipeName = null;
                DispatchRemoteCommand(_lastCommandId, "RecipeName", recipeName, true, OpcRecipeChangeRequested);
                return;
            }

            if (_pendingRecipeId > 0)
            {
                var recipeId = _pendingRecipeId;
                _lastProcessedCommandId = _lastCommandId;
                _pendingRecipeId = 0;
                DispatchRemoteCommand(_lastCommandId, "RecipeId", recipeId.ToString(CultureInfo.InvariantCulture), true, OpcRecipeChangeRequested);
            }
        }

        private void ReleaseEarlyWarningWriteGate()
        {
            try
            {
                _earlyWarningWriteGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SemaphoreFullException)
            {
            }
        }

        private static string TrimOpcText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private bool WriteValue(string key, object value)
        {
            var session = _session;
            if (session == null || !session.Connected)
            {
                return false;
            }

            var node = FindNode(key);
            if (node == null || !node.Enabled || string.IsNullOrWhiteSpace(node.NodeId))
            {
                return true;
            }

            try
            {
                var convertedValue = ConvertForNode(value, node.DataType);
                var writeValue = new WriteValue
                {
                    NodeId = NodeId.Parse(node.NodeId),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(convertedValue))
                };

                StatusCodeCollection results;
                DiagnosticInfoCollection diagnostics;
                session.Write(null, new WriteValueCollection { writeValue }, out results, out diagnostics);

                if (results == null || results.Count == 0 || StatusCode.IsBad(results[0]))
                {
                    _logger.Warn($"OPCUA_WRITE_FAILED|key={key}|nodeId={node.NodeId}|status={(results != null && results.Count > 0 ? results[0].ToString() : "missing")}");
                    LogOpcUaEvent(
                        LogLevel.Warn,
                        "OPCUA_WRITE_FAILED",
                        "OPC UA node write failed",
                        nameof(WriteValue),
                        $"Key={key}; NodeId={node.NodeId}",
                        new Dictionary<string, object>
                        {
                            { "node_key", key },
                            { "node_id", node.NodeId },
                            { "status", results != null && results.Count > 0 ? results[0].ToString() : "missing" }
                        });
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"OPCUA_WRITE_EXCEPTION|key={key}|nodeId={node.NodeId}");
                LogOpcUaEvent(
                    LogLevel.Warn,
                    "OPCUA_WRITE_EXCEPTION",
                    "OPC UA node write raised an exception",
                    nameof(WriteValue),
                    ex.Message,
                    new Dictionary<string, object>
                    {
                        { "node_key", key },
                        { "node_id", node.NodeId },
                        { "exception_type", ex.GetType().FullName }
                    });
                return false;
            }
        }

        private void OnKeepAlive(ISession sender, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsBad(e.Status))
            {
                LastStatusMessage = $"OPC UA keepalive failed: {e.Status}";
                _logger.Warn(LastStatusMessage);
                LogOpcUaStatus(
                    true,
                    CurrentConfig?.RequiredForMachineRun == true,
                    false,
                    nameof(OnKeepAlive),
                    LastStatusMessage,
                    "OPCUA_KEEPALIVE_FAILED",
                    CurrentConfig?.RequiredForMachineRun == true ? LogLevel.Error : LogLevel.Warn,
                    true,
                    new Dictionary<string, object>
                    {
                        { "status", e.Status?.ToString() }
                    });
            }
        }

        private void StartReconnectTimer()
        {
            var interval = Math.Max(CurrentConfig?.ReconnectIntervalMs ?? 5000, 1000);
            _reconnectTimer?.Dispose();
            _reconnectTimer = new Timer(async _ =>
            {
                if (_disposed || !IsEnabled || IsConnected)
                {
                    return;
                }

                try
                {
                    await ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "OPCUA_RECONNECT_FAILED");
                    LogOpcUaStatus(
                        true,
                        CurrentConfig?.RequiredForMachineRun == true,
                        false,
                        nameof(StartReconnectTimer),
                        $"OPC UA reconnect failed: {ex.Message}",
                        "OPCUA_RECONNECT_FAILED",
                        CurrentConfig?.RequiredForMachineRun == true ? LogLevel.Error : LogLevel.Warn,
                        true,
                        new Dictionary<string, object>
                        {
                            { "exception_type", ex.GetType().FullName }
                        });
                }
            }, null, interval, interval);
        }

        private async Task DisconnectInternalAsync()
        {
            var session = _session;
            var subscription = _subscription;

            lock (_syncRoot)
            {
                _session = null;
                _subscription = null;
            }

            if (subscription != null)
            {
                try
                {
                    subscription.Delete(true);
                    subscription.Dispose();
                }
                catch
                {
                }
            }

            if (session != null)
            {
                try
                {
                    session.KeepAlive -= OnKeepAlive;
                    session.Close(1000);
                    session.Dispose();
                }
                catch
                {
                }
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private OpcUaNodeConfig FindNode(string key)
        {
            return CurrentConfig?.Nodes?.FirstOrDefault(node =>
                string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerable<OpcUaNodeConfig> GetNodes(string direction)
        {
            return CurrentConfig?.Nodes?.Where(node =>
                string.Equals(node.Direction, direction, StringComparison.OrdinalIgnoreCase)) ??
                Enumerable.Empty<OpcUaNodeConfig>();
        }

        private void DispatchRemoteCommand(long commandId, string commandName, string value, bool sequenced, EventHandler<OpcUaCommandEventArgs> handler)
        {
            if (!IsRemoteCommandAllowed(commandName, out var rejectionReason))
            {
                LogOpcUaCommandRejected(commandId, commandName, value, sequenced, rejectionReason);
                WriteCommandAcknowledgementAsync(commandId, false, rejectionReason)
                    .SafeFireAndForget(_logger, "OPCUA_COMMAND_REJECT_ACK_FAILED");
                return;
            }

            LogOpcUaCommandReceived(commandId, commandName, value, sequenced);
            handler?.Invoke(this, new OpcUaCommandEventArgs(commandId, commandName, value));
        }

        private bool IsRemoteCommandAllowed(string commandName, out string rejectionReason)
        {
            rejectionReason = null;
            var config = CurrentConfig;
            if (config == null || !config.RemoteCommandsEnabled)
            {
                rejectionReason = "OPC UA remote commands are disabled by configuration.";
                return false;
            }

            if ((string.Equals(commandName, "Start", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(commandName, "Stop", StringComparison.OrdinalIgnoreCase)) &&
                !config.RemoteStartStopEnabled)
            {
                rejectionReason = "OPC UA remote Start/Stop commands are disabled by configuration.";
                return false;
            }

            if ((string.Equals(commandName, "RecipeName", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(commandName, "RecipeId", StringComparison.OrdinalIgnoreCase)) &&
                !config.RemoteRecipeChangeEnabled)
            {
                rejectionReason = "OPC UA remote recipe change commands are disabled by configuration.";
                return false;
            }

            return true;
        }

        private void LogOpcUaCommandReceived(long commandId, string commandName, string value, bool sequenced)
        {
            LogOpcUaEvent(
                LogLevel.Info,
                "OPCUA_COMMAND_RECEIVED",
                $"OPC UA command received: {commandName}",
                nameof(ProcessInboundValue),
                value,
                new Dictionary<string, object>
                {
                    { "command_id", commandId },
                    { "command_name", commandName },
                    { "value", value },
                    { "sequenced", sequenced }
                });

            if (CurrentConfig?.AuditRemoteCommandsToDatabase == true)
            {
                ServiceLocator.AuditLogService?.LogAsync(
                    "OPCUA_COMMAND_RECEIVED",
                    "OPC UA",
                    $"command={commandName}; commandId={commandId}; sequenced={sequenced}; value={value}").SafeFireAndForget(_logger, "AUDIT_OPCUA_COMMAND_RECEIVED_FAILED");
            }
        }

        private void LogOpcUaCommandRejected(long commandId, string commandName, string value, bool sequenced, string reason)
        {
            LogOpcUaEvent(
                LogLevel.Warn,
                "OPCUA_COMMAND_REJECTED_BY_POLICY",
                $"OPC UA command rejected by policy: {commandName}",
                nameof(ProcessInboundValue),
                reason,
                new Dictionary<string, object>
                {
                    { "command_id", commandId },
                    { "command_name", commandName },
                    { "value", value },
                    { "sequenced", sequenced },
                    { "reason", reason }
                });

            if (CurrentConfig?.AuditRemoteCommandsToDatabase == true)
            {
                ServiceLocator.AuditLogService?.LogAsync(
                    "OPCUA_COMMAND_REJECTED_BY_POLICY",
                    "OPC UA",
                    $"command={commandName}; commandId={commandId}; sequenced={sequenced}; reason={reason}",
                    null,
                    value).SafeFireAndForget(_logger, "AUDIT_OPCUA_COMMAND_REJECT_FAILED");
            }
        }

        private void LogOpcUaSecurityPosture()
        {
            var config = CurrentConfig;
            if (config == null || !config.Enabled)
            {
                return;
            }

            var warnings = new List<string>();
            if (config.AutoAcceptUntrustedCertificates)
            {
                warnings.Add("auto_accept_untrusted_certificates=true");
            }

            if (config.RemoteCommandsEnabled && !config.UseSecurity)
            {
                warnings.Add("remote_commands_enabled_without_opcua_security");
            }

            if (warnings.Count == 0)
            {
                return;
            }

            string details = string.Join("; ", warnings);
            LogOpcUaEvent(
                LogLevel.Warn,
                "OPCUA_SECURITY_POSTURE_WARNING",
                "OPC UA security posture requires review",
                nameof(LogOpcUaSecurityPosture),
                details,
                new Dictionary<string, object>
                {
                    { "use_security", config.UseSecurity },
                    { "auto_accept_untrusted_certificates", config.AutoAcceptUntrustedCertificates },
                    { "remote_commands_enabled", config.RemoteCommandsEnabled },
                    { "remote_start_stop_enabled", config.RemoteStartStopEnabled },
                    { "remote_recipe_change_enabled", config.RemoteRecipeChangeEnabled }
                });

            ServiceLocator.AuditLogService?.LogAsync(
                "OPCUA_SECURITY_POSTURE_WARNING",
                "system",
                $"server={config.ServerUrl}; {details}").SafeFireAndForget(_logger, "AUDIT_OPCUA_SECURITY_POSTURE_FAILED");
        }

        private void LogOpcUaStatus(bool enabled, bool requiredForRun, bool connected, string source, string details,
            string logId, LogLevel level, bool force, IDictionary<string, object> metadata = null)
        {
            try
            {
                var eventLogger = ServiceLocator.ApplicationEventLogger;
                if (eventLogger == null)
                {
                    return;
                }

                var eventMetadata = metadata != null
                    ? new Dictionary<string, object>(metadata)
                    : new Dictionary<string, object>();
                eventMetadata["server_url"] = CurrentConfig?.ServerUrl;
                eventMetadata["last_status_message"] = LastStatusMessage;

                eventLogger.LogOpcUaStatus(
                    enabled,
                    requiredForRun,
                    connected,
                    source,
                    details,
                    eventMetadata,
                    force,
                    level,
                    logId);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "OPCUA_AUDIT_STATUS_LOG_FAILED");
            }
        }

        private void LogOpcUaEvent(LogLevel level, string logId, string message, string source, string details = null,
            IDictionary<string, object> metadata = null)
        {
            try
            {
                var eventLogger = ServiceLocator.ApplicationEventLogger;
                if (eventLogger == null)
                {
                    return;
                }

                var eventMetadata = metadata != null
                    ? new Dictionary<string, object>(metadata)
                    : new Dictionary<string, object>();
                eventMetadata["enabled"] = IsEnabled;
                eventMetadata["required_for_machine_run"] = IsRequiredForMachineRun;
                eventMetadata["connected"] = IsConnected;
                eventMetadata["server_url"] = CurrentConfig?.ServerUrl;

                eventLogger.LogOperationalEvent(
                    level,
                    logId,
                    "OPC UA",
                    message,
                    source,
                    details,
                    eventMetadata);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "OPCUA_AUDIT_EVENT_LOG_FAILED");
            }
        }

        private bool ShouldLogConnectRequestAudit()
        {
            lock (_syncRoot)
            {
                var now = DateTime.UtcNow;
                if (now - _lastConnectRequestAuditUtc < TimeSpan.FromMinutes(1))
                {
                    return false;
                }

                _lastConnectRequestAuditUtc = now;
                return true;
            }
        }

        private static object ConvertForNode(object value, string dataType)
        {
            if (string.Equals(dataType, "Boolean", StringComparison.OrdinalIgnoreCase))
            {
                return ConvertToBoolean(value);
            }

            if (string.Equals(dataType, "Int64", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            if (string.Equals(dataType, "Int32", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }

            if (string.Equals(dataType, "Double", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static bool ConvertToBoolean(object value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            if (value is IConvertible)
            {
                try
                {
                    return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                }
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "start", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "stop", StringComparison.OrdinalIgnoreCase);
        }

        private static long ConvertToInt64(object value)
        {
            if (value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }
    }
}
