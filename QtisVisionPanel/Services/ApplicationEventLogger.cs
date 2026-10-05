using NLog;
using Newtonsoft.Json;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public class ApplicationEventLogger : IDisposable
    {
        private readonly Logger _auditLogger = LogManager.GetLogger("EventAudit");
        private readonly EventLogRepository _eventLogRepository = new EventLogRepository();
        private readonly object _syncRoot = new object();

        // Fase 0 stabilita' (bounded queues): coda LIMITATA verso il mirror DB degli eventi,
        // svuotata da un unico drainer in background. Prima ogni evento lanciava un
        // Task.Run(InsertAsync): con MySQL in stallo i task si accumulavano senza limite
        // dietro il gate del repository (pressione su memoria e threadpool). A coda piena
        // il SOLO mirror DB dell'evento viene scartato in modo esplicito (contatore +
        // warning throttled): l'evento resta comunque nei file di log NLog.
        private const int DbQueueCapacity = 500;
        private readonly System.Collections.Concurrent.BlockingCollection<EventLogEntry> _dbWriteQueue =
            new System.Collections.Concurrent.BlockingCollection<EventLogEntry>(
                new System.Collections.Concurrent.ConcurrentQueue<EventLogEntry>(), DbQueueCapacity);
        private readonly Task _dbWriterTask;
        private long _droppedDbEvents;
        private DateTime _lastDropWarningUtc = DateTime.MinValue;
        private volatile bool _disposed;

        public ApplicationEventLogger()
        {
            _dbWriterTask = Task.Factory.StartNew(
                DrainDbQueueAsync,
                System.Threading.CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

        private async Task DrainDbQueueAsync()
        {
            foreach (var entry in _dbWriteQueue.GetConsumingEnumerable())
            {
                try
                {
                    await _eventLogRepository.InsertAsync(entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Il repository degrada gia' da solo; qui interessa solo non fermare il drainer.
                    _auditLogger.Warn(ex, "EVENTLOG_DB_WRITE_FAILED");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                _dbWriteQueue.CompleteAdding();
                // Breve drain finale: gli eventi gia' accodati vengono scritti, poi si chiude.
                _dbWriterTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                _auditLogger.Debug(ex, "EVENTLOG_QUEUE_DISPOSE_INCOMPLETE");
            }
            finally
            {
                _dbWriteQueue.Dispose();
            }
        }
        private bool? _lastDatabaseConnected;
        private bool? _lastVisionRunning;
        private bool? _lastOpcUaEnabled;
        private bool? _lastOpcUaRequired;
        private bool? _lastOpcUaConnected;
        private string _lastMachineStatus;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        public void LogLifecycle(string logId, string message, string source, string details = null, IDictionary<string, object> metadata = null)
        {
            Write(LogLevel.Info, logId, "Application", message, source, details, metadata, null, null, null, null);
        }

        public void LogOperationalEvent(LogLevel level, string logId, string category, string message, string source,
            string details = null, IDictionary<string, object> metadata = null)
        {
            Write(level, logId, category, message, source, details, metadata, null, null, null, null);
        }

        public void LogDatabaseStatus(bool isConnected, string source, string details = null)
        {
            lock (_syncRoot)
            {
                if (_lastDatabaseConnected.HasValue && _lastDatabaseConnected.Value == isConnected)
                    return;

                _lastDatabaseConnected = isConnected;
            }

            Write(
                isConnected ? LogLevel.Info : LogLevel.Warn,
                isConnected ? "DB_CONNECTED" : "DB_DISCONNECTED",
                "Database",
                isConnected ? "Database connection available" : "Database connection unavailable",
                source,
                details,
                null,
                null,
                isConnected ? "Connected" : "Disconnected",
                null,
                null);
        }

        public void LogOpcUaStatus(bool enabled, bool requiredForRun, bool connected, string source, string details = null,
            IDictionary<string, object> metadata = null, bool force = false, LogLevel level = null, string logId = null)
        {
            lock (_syncRoot)
            {
                if (!force &&
                    _lastOpcUaEnabled.HasValue && _lastOpcUaEnabled.Value == enabled &&
                    _lastOpcUaRequired.HasValue && _lastOpcUaRequired.Value == requiredForRun &&
                    _lastOpcUaConnected.HasValue && _lastOpcUaConnected.Value == connected)
                {
                    return;
                }

                _lastOpcUaEnabled = enabled;
                _lastOpcUaRequired = requiredForRun;
                _lastOpcUaConnected = connected;
            }

            var eventMetadata = metadata != null
                ? new Dictionary<string, object>(metadata)
                : new Dictionary<string, object>();
            eventMetadata["enabled"] = enabled;
            eventMetadata["required_for_machine_run"] = requiredForRun;
            eventMetadata["connected"] = connected;

            var resolvedLogId = logId;
            if (string.IsNullOrWhiteSpace(resolvedLogId))
            {
                resolvedLogId = !enabled
                    ? "OPCUA_DISABLED"
                    : connected
                        ? "OPCUA_CONNECTED"
                        : "OPCUA_DISCONNECTED";
            }

            var resolvedLevel = level ?? (!enabled || connected ? LogLevel.Info : LogLevel.Warn);
            var message = !enabled
                ? "OPC UA communication disabled"
                : connected
                    ? "OPC UA communication connected"
                    : requiredForRun
                        ? "OPC UA communication disconnected and required for machine run"
                        : "OPC UA communication disconnected but not required for machine run";

            Write(
                resolvedLevel,
                resolvedLogId,
                "OPC UA",
                message,
                source,
                details,
                eventMetadata,
                null,
                null,
                null,
                null);
        }

        public void LogVisionProStatus(bool isRunning, string source, string details = null)
        {
            lock (_syncRoot)
            {
                if (_lastVisionRunning.HasValue && _lastVisionRunning.Value == isRunning)
                    return;

                _lastVisionRunning = isRunning;
            }

            Write(
                LogLevel.Info,
                isRunning ? "VISIONPRO_RUNNING" : "VISIONPRO_STOPPED",
                "VisionPro",
                isRunning ? "VisionPro continuous run active" : "VisionPro continuous run stopped",
                source,
                details,
                null,
                null,
                null,
                isRunning ? "Running" : "Stopped",
                null);
        }

        public void LogMachineStatusChange(string oldStatus, string newStatus, bool isContinuousRunActive)
        {
            lock (_syncRoot)
            {
                if (string.Equals(_lastMachineStatus, newStatus, StringComparison.OrdinalIgnoreCase))
                    return;

                _lastMachineStatus = newStatus;
            }

            var metadata = new Dictionary<string, object>
            {
                { "old_status", oldStatus ?? "Unknown" },
                { "continuous_run_active", isContinuousRunActive }
            };

            Write(
                LogLevel.Info,
                "MACHINE_STATUS_CHANGED",
                "Machine",
                $"Machine status changed to {newStatus}",
                nameof(MachineStatusService),
                $"Previous status: {oldStatus}",
                metadata,
                newStatus,
                null,
                isContinuousRunActive ? "Running" : "Stopped",
                null);
        }

        public void LogCommand(string commandName, string phase, string source, string details = null, IDictionary<string, object> metadata = null)
        {
            var normalizedCommand = (commandName ?? "COMMAND").Trim().ToUpperInvariant().Replace(" ", "_");
            var normalizedPhase = (phase ?? "EXECUTED").Trim().ToUpperInvariant().Replace(" ", "_");
            var message = $"{commandName} - {phase}";

            Write(LogLevel.Info, $"{normalizedCommand}_{normalizedPhase}", "Command", message, source, details, metadata, null, null, null, null);
        }

        public void LogRecipeEvent(string logId, string recipeName, string source, string details = null)
        {
            var metadata = new Dictionary<string, object>
            {
                { "recipe_name", recipeName }
            };

            Write(LogLevel.Info, logId, "Recipe", $"Recipe event for {recipeName}", source, details, metadata, null, null, null, null);
        }

        public void LogUserEvent(string logId, string username, string role, string source, string details = null)
        {
            var metadata = new Dictionary<string, object>
            {
                { "username", username },
                { "role", role }
            };

            Write(LogLevel.Info, logId, "User", $"{username} ({role})", source, details, metadata, null, null, null, null);
        }

        public void LogAlarmEvent(string message, string source, string details = null, bool isCritical = false)
        {
            Write(
                isCritical ? LogLevel.Error : LogLevel.Warn,
                "ALARM_TRIGGERED",
                "Alarm",
                message,
                source,
                details,
                null,
                null,
                null,
                null,
                null);
        }

        public void LogException(string logId, Exception exception, string source, string message = null, IDictionary<string, object> metadata = null)
        {
            Write(
                LogLevel.Error,
                logId,
                "Exception",
                message ?? exception?.Message ?? logId,
                source,
                exception?.Message,
                metadata,
                null,
                null,
                null,
                exception);
        }

        private void Write(LogLevel level, string logId, string category, string message, string source, string details,
            IDictionary<string, object> metadata, string machineStatus, string databaseStatus, string visionProStatus, Exception exception)
        {
            var payload = new EventLogPayload
            {
                Category = category,
                Message = message,
                Source = source,
                Details = details,
                MachineStatus = ResolveMachineStatus(machineStatus),
                DatabaseStatus = ResolveDatabaseStatus(databaseStatus),
                VisionProStatus = ResolveVisionProStatus(visionProStatus),
                User = UserSession.CurrentUser,
                Role = UserSession.CurrentRole,
                Recipe = GetCurrentRecipeName(),
                Metadata = NormalizeMetadata(metadata)
            };

            if (exception != null)
            {
                payload.ExceptionType = exception.GetType().FullName;
                payload.StackTrace = exception.ToString();

                if (string.IsNullOrWhiteSpace(payload.Details))
                    payload.Details = exception.Message;
            }

            var logEvent = new LogEventInfo(level, _auditLogger.Name, message ?? logId ?? "Application event");
            var normalizedLogId = string.IsNullOrWhiteSpace(logId) ? "APP_EVENT" : logId;
            var payloadJson = JsonConvert.SerializeObject(payload, JsonSettings);

            logEvent.Properties["log_id"] = normalizedLogId;
            logEvent.Properties["payload_json"] = payloadJson;

            _auditLogger.Log(logEvent);

            var dbEntry = new EventLogEntry
            {
                Application = "QtisVisionPanel",
                Timestamp = DateTime.Now,
                Level = NormalizeLevel(level),
                LogId = normalizedLogId,
                InfoJson = payloadJson,
                ExportType = 3,
                Payload = payload
            };

            try
            {
                if (!_disposed && !_dbWriteQueue.TryAdd(dbEntry))
                {
                    long dropped = System.Threading.Interlocked.Increment(ref _droppedDbEvents);
                    var nowUtc = DateTime.UtcNow;
                    if ((nowUtc - _lastDropWarningUtc).TotalSeconds >= 60)
                    {
                        _lastDropWarningUtc = nowUtc;
                        _auditLogger.Warn(
                            $"EVENTLOG_DB_QUEUE_FULL|capacity={DbQueueCapacity}|dropped_total={dropped} — mirror DB evento scartato (DB lento/assente); evento comunque nei file di log");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Coda gia' completata durante lo shutdown: l'evento resta nei log NLog.
            }
        }

        private string ResolveMachineStatus(string machineStatus)
        {
            if (!string.IsNullOrWhiteSpace(machineStatus))
                return machineStatus;

            if (!string.IsNullOrWhiteSpace(_lastMachineStatus))
                return _lastMachineStatus;

            try
            {
                return MachineStatusService.Instance.CurrentStatus.ToString();
            }
            catch
            {
                return null;
            }
        }

        private string ResolveDatabaseStatus(string databaseStatus)
        {
            if (!string.IsNullOrWhiteSpace(databaseStatus))
                return databaseStatus;

            lock (_syncRoot)
            {
                if (_lastDatabaseConnected.HasValue)
                    return _lastDatabaseConnected.Value ? "Connected" : "Disconnected";
            }

            return null;
        }

        private string ResolveVisionProStatus(string visionProStatus)
        {
            if (!string.IsNullOrWhiteSpace(visionProStatus))
                return visionProStatus;

            try
            {
                var runtimeService = ServiceLocator.MachineRuntimeService;
                if (runtimeService != null)
                    return runtimeService.IsContinuousRunActive ? "Running" : "Stopped";
            }
            catch
            {
            }

            lock (_syncRoot)
            {
                if (_lastVisionRunning.HasValue)
                    return _lastVisionRunning.Value ? "Running" : "Stopped";
            }

            return null;
        }

        private Dictionary<string, string> NormalizeMetadata(IDictionary<string, object> metadata)
        {
            var normalized = new Dictionary<string, string>();

            if (metadata == null)
                return normalized;

            foreach (var item in metadata)
            {
                normalized[item.Key] = item.Value == null ? string.Empty : item.Value.ToString();
            }

            return normalized;
        }

        private string GetCurrentRecipeName()
        {
            var recipe = MainWindow.configManager?.Config?.Configuration?.LastRecipe;
            return string.IsNullOrWhiteSpace(recipe)
                ? null
                : Path.GetFileNameWithoutExtension(recipe);
        }

        private string NormalizeLevel(LogLevel level)
        {
            if (level == null)
                return "I";

            if (level == LogLevel.Debug || level == LogLevel.Trace)
                return "D";

            if (level == LogLevel.Warn)
                return "W";

            if (level == LogLevel.Error)
                return "E";

            if (level == LogLevel.Fatal)
                return "C";

            return "I";
        }
    }
}
