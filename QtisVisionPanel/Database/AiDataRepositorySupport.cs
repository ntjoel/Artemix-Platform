using MySql.Data.MySqlClient;
using NLog;
using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Database
{
    internal static class AiDataRepositorySupport
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, DateTime> LastWarningUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastRetentionUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan WarningThrottle = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(12);

        public static string BuildConnectionString()
        {
            var config = MainWindow.configManager?.Config?.MySqlConnection;
            if (config == null ||
                string.IsNullOrWhiteSpace(config.Host) ||
                string.IsNullOrWhiteSpace(config.Db) ||
                string.IsNullOrWhiteSpace(config.User) ||
                string.IsNullOrWhiteSpace(config.port))
            {
                return null;
            }

            return $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};" +
                   $"Port={config.port};Connection Timeout=5;Default Command Timeout=10;";
        }

        public static void LogThrottledWarning(string logId, string message, Exception exception = null)
        {
            if (string.IsNullOrWhiteSpace(logId))
            {
                logId = "AI_DATA_WARNING";
            }

            if (!ShouldEmitWarning(logId))
            {
                return;
            }

            if (exception != null)
            {
                Logger.Warn(exception, $"{logId}|{message}");
            }
            else
            {
                Logger.Warn($"{logId}|{message}");
            }

            try
            {
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    LogLevel.Warn,
                    logId,
                    "AI Data",
                    message,
                    nameof(AiDataRepositorySupport),
                    exception?.Message,
                    new Dictionary<string, object>
                    {
                        { "throttled_minutes", WarningThrottle.TotalMinutes.ToString("0") }
                    });
            }
            catch (Exception logException)
            {
                Logger.Debug(logException, "AI_DATA_EVENT_LOG_FAILED");
            }
        }

        public static int ReadRetentionDays(Func<MachineRuntimeBindings, int> selector, int fallbackDays)
        {
            try
            {
                var bindings = new MachineConfigurationService().Load()?.RuntimeBindings;
                if (bindings == null || selector == null)
                {
                    return fallbackDays;
                }

                int days = selector(bindings);
                return days < 0 ? fallbackDays : days;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "AI_RETENTION_CONFIG_READ_FAILED");
                return fallbackDays;
            }
        }

        public static bool ShouldRunRetention(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return false;
            }

            DateTime now = DateTime.UtcNow;
            lock (SyncRoot)
            {
                if (LastRetentionUtc.TryGetValue(tableName, out DateTime last) &&
                    now - last < RetentionInterval)
                {
                    return false;
                }

                LastRetentionUtc[tableName] = now;
                return true;
            }
        }

        public static void ApplyRetention(MySqlConnection connection, string tableName, int retentionDays)
        {
            if (connection == null || string.IsNullOrWhiteSpace(tableName) || retentionDays <= 0)
            {
                return;
            }

            try
            {
                DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);
                string query = $"DELETE FROM `{tableName}` WHERE `Timestamp` < @Cutoff;";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Cutoff", cutoff);
                    int deleted = command.ExecuteNonQuery();
                    if (deleted > 0)
                    {
                        Logger.Info($"AI_RETENTION_APPLIED|table={tableName}|deleted={deleted}|retention_days={retentionDays}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottledWarning(
                    "AI_RETENTION_FAILED",
                    $"Pulizia retention fallita per {tableName}. La produzione non viene bloccata.",
                    ex);
            }
        }

        private static bool ShouldEmitWarning(string logId)
        {
            DateTime now = DateTime.UtcNow;
            lock (SyncRoot)
            {
                if (LastWarningUtc.TryGetValue(logId, out DateTime last) &&
                    now - last < WarningThrottle)
                {
                    return false;
                }

                LastWarningUtc[logId] = now;
                return true;
            }
        }
    }
}
