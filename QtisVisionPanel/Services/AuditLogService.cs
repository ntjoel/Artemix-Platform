using MySql.Data.MySqlClient;
using NLog;
using QtisVisionPanel.Database;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Writes security-relevant events to the audit_log table.
    /// All writes are best-effort: exceptions are logged via NLog and never propagate to callers.
    /// </summary>
    public sealed class AuditLogService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private string BuildConnectionString()
        {
            var cfg = MainWindow.configManager?.Config?.MySqlConnection;
            if (cfg == null) return null;
            return DbConnectionStringHelper.Build(cfg.Host, cfg.Db, cfg.User, cfg.Password, cfg.port, cfg.SslDisabled);
        }

        public async Task LogAsync(
            string eventType,
            string actor,
            string details,
            string oldValue = null,
            string newValue = null,
            CancellationToken ct = default)
        {
            string connStr = BuildConnectionString();
            if (connStr == null)
            {
                _logger.Warn("AUDIT_LOG_SKIP|reason=no_config|event={0}|actor={1}", eventType, actor);
                return;
            }

            try
            {
                using (var conn = new MySqlConnection(connStr))
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    await conn.OpenAsync(cts.Token);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT INTO `audit_log`
                                (`Timestamp`, `EventType`, `Actor`, `Details`, `OldValue`, `NewValue`)
                            VALUES
                                (NOW(3), @evt, @actor, @details, @old, @new)";

                        cmd.Parameters.AddWithValue("@evt",     eventType ?? string.Empty);
                        cmd.Parameters.AddWithValue("@actor",   actor     ?? string.Empty);
                        cmd.Parameters.AddWithValue("@details", details   ?? string.Empty);
                        cmd.Parameters.AddWithValue("@old",     (object)oldValue ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@new",     (object)newValue ?? DBNull.Value);

                        await cmd.ExecuteNonQueryAsync(cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("AUDIT_LOG_TIMEOUT|event={0}|actor={1}", eventType, actor);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "AUDIT_LOG_ERROR|event={0}|actor={1}", eventType, actor);
            }
        }
    }
}
