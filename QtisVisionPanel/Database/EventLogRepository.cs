using MySql.Data.MySqlClient;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    public class EventAlertSummary
    {
        public int AlertCount { get; set; }
        public EventLogEntry LatestAlert { get; set; }
    }

    public class EventLogRepository
    {
        private static readonly SemaphoreSlim RepositoryGate = new SemaphoreSlim(1, 1);

        public async Task<bool> InsertAsync(EventLogEntry entry)
        {
            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return InsertCore(entry);
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        public bool Insert(EventLogEntry entry)
        {
            RepositoryGate.Wait();
            try
            {
                return InsertCore(entry);
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        public async Task<IReadOnlyList<EventLogEntry>> GetRecentAsync(int limit = 250)
        {
            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return GetRecentCore(limit);
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        public async Task<EventAlertSummary> GetAlertSummaryAsync()
        {
            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return GetAlertSummaryCore();
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        public async Task<bool> ClearAllAsync()
        {
            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return ClearAllCore();
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        public async Task<bool> ClearWarningInfoAsync()
        {
            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return ClearWarningInfoCore();
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        public async Task<bool> ClearOlderThanAsync(DateTime cutoff)
        {
            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return ClearOlderThanCore(cutoff);
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        private bool InsertCore(EventLogEntry entry)
        {
            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString) || entry == null)
                return false;

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    const string query = @"
INSERT INTO tbllogevent (application, timestamp, level, log_id, info, export_type)
VALUES (@Application, @Timestamp, @Level, @LogId, @Info, @ExportType);";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Application", entry.Application ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Timestamp", entry.Timestamp);
                        command.Parameters.AddWithValue("@Level", entry.Level ?? "I");
                        command.Parameters.AddWithValue("@LogId", entry.LogId ?? "APP_EVENT");
                        command.Parameters.AddWithValue("@Info", entry.InfoJson ?? "{}");
                        command.Parameters.AddWithValue("@ExportType", entry.ExportType ?? (object)DBNull.Value);
                        command.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventLogRepository.Insert failed: {ex.Message}");
                return false;
            }
        }

        private IReadOnlyList<EventLogEntry> GetRecentCore(int limit)
        {
            var items = new List<EventLogEntry>();
            var connectionString = BuildConnectionString();

            if (string.IsNullOrWhiteSpace(connectionString))
                return items;

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    const string query = @"
SELECT Id, application, timestamp, level, log_id, info, export_type
FROM tbllogevent
ORDER BY timestamp DESC
LIMIT @Limit;";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Limit", limit);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                items.Add(Map(reader));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventLogRepository.GetRecent failed: {ex.Message}");
            }

            return items;
        }

        private EventAlertSummary GetAlertSummaryCore()
        {
            var summary = new EventAlertSummary();
            var connectionString = BuildConnectionString();

            if (string.IsNullOrWhiteSpace(connectionString))
                return summary;

            try
            {
                // Conta e mostra solo gli errori (Error/Critical) delle ultime 24 ore,
                // non l'intero storico: il badge deve riflettere gli errori "attuali".
                var cutoff = DateTime.Now.AddHours(-24);

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    const string countQuery = @"
SELECT COUNT(*)
FROM tbllogevent
WHERE UPPER(level) IN ('E', 'C')
  AND timestamp >= @Cutoff;";

                    using (var countCommand = new MySqlCommand(countQuery, connection))
                    {
                        countCommand.Parameters.AddWithValue("@Cutoff", cutoff);
                        var scalar = countCommand.ExecuteScalar();
                        summary.AlertCount = scalar == null || scalar == DBNull.Value
                            ? 0
                            : Convert.ToInt32(scalar);
                    }

                    const string latestQuery = @"
SELECT Id, application, timestamp, level, log_id, info, export_type
FROM tbllogevent
WHERE UPPER(level) IN ('E', 'C')
  AND timestamp >= @Cutoff
ORDER BY timestamp DESC
LIMIT 1;";

                    using (var latestCommand = new MySqlCommand(latestQuery, connection))
                    {
                        latestCommand.Parameters.AddWithValue("@Cutoff", cutoff);
                        using (var reader = latestCommand.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                summary.LatestAlert = Map(reader);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventLogRepository.GetAlertSummary failed: {ex.Message}");
            }

            return summary;
        }

        private bool ClearAllCore()
        {
            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    const string deleteQuery = @"DELETE FROM tbllogevent;";
                    using (var deleteCommand = new MySqlCommand(deleteQuery, connection))
                    {
                        deleteCommand.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventLogRepository.ClearAll failed: {ex.Message}");
                return false;
            }
        }

        private bool ClearWarningInfoCore()
        {
            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    const string deleteQuery = @"
DELETE FROM tbllogevent
WHERE UPPER(level) IN ('I', 'W', 'INFO', 'WARNING');";

                    using (var deleteCommand = new MySqlCommand(deleteQuery, connection))
                    {
                        deleteCommand.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventLogRepository.ClearWarningInfo failed: {ex.Message}");
                return false;
            }
        }

        private bool ClearOlderThanCore(DateTime cutoff)
        {
            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    const string deleteQuery = @"
DELETE FROM tbllogevent
WHERE timestamp < @Cutoff;";

                    using (var deleteCommand = new MySqlCommand(deleteQuery, connection))
                    {
                        deleteCommand.Parameters.AddWithValue("@Cutoff", cutoff);
                        deleteCommand.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventLogRepository.ClearOlderThan failed: {ex.Message}");
                return false;
            }
        }

        private EventLogEntry Map(System.Data.Common.DbDataReader reader)
        {
            var infoValue = reader["info"] == DBNull.Value ? null : reader["info"].ToString();
            var application = reader["application"] == DBNull.Value ? null : reader["application"].ToString();
            var logId = reader["log_id"] == DBNull.Value ? null : reader["log_id"].ToString();

            return new EventLogEntry
            {
                Id = Convert.ToInt64(reader["Id"]),
                Application = application,
                Timestamp = Convert.ToDateTime(reader["timestamp"]),
                Level = reader["level"] == DBNull.Value ? "I" : reader["level"].ToString(),
                LogId = logId,
                InfoJson = infoValue,
                ExportType = reader["export_type"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["export_type"]),
                Payload = EventLogEntry.ParsePayload(infoValue, logId, application)
            };
        }

        private string BuildConnectionString()
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

            return $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port};Connection Timeout=5;Default Command Timeout=10;";
        }
    }
}
