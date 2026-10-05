using MySql.Data.MySqlClient;
using QtisVisionPanel.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    /// <summary>
    /// Persistenza degli snapshot di salute PC in `tbl_health_snapshots`.
    ///
    /// Stesso pattern di <see cref="EventLogRepository"/>: gate a semaforo, coppia
    /// async/sync, degradazione silenziosa quando MySQL non e' disponibile.
    /// </summary>
    public class HealthSnapshotRepository
    {
        private static readonly SemaphoreSlim RepositoryGate = new SemaphoreSlim(1, 1);

        public async Task<bool> InsertAsync(HealthSnapshotEntry entry)
        {
            if (entry == null)
                return false;

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

        private bool InsertCore(HealthSnapshotEntry entry)
        {
            var connectionString = AiDataRepositorySupport.BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                AiDataRepositorySupport.LogThrottledWarning(
                    "AI_HEALTH_DB_NOT_CONFIGURED",
                    "Connessione MySQL non disponibile per gli snapshot salute AI. La produzione non viene bloccata.");
                return false;
            }

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    if (AiDataRepositorySupport.ShouldRunRetention("tbl_health_snapshots"))
                    {
                        int retentionDays = AiDataRepositorySupport.ReadRetentionDays(
                            bindings => bindings.AiHealthSnapshotRetentionDays,
                            90);
                        AiDataRepositorySupport.ApplyRetention(connection, "tbl_health_snapshots", retentionDays);
                    }

                    const string query = @"
INSERT INTO tbl_health_snapshots
    (Timestamp, CpuPercent, MemoryPercent, MemoryUsedGb, MemoryTotalGb, DiskMaxUsedPercent)
VALUES
    (@Timestamp, @CpuPercent, @MemoryPercent, @MemoryUsedGb, @MemoryTotalGb, @DiskMaxUsedPercent);";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Timestamp", entry.Timestamp);
                        command.Parameters.AddWithValue("@CpuPercent", entry.CpuPercent);
                        command.Parameters.AddWithValue("@MemoryPercent", entry.MemoryPercent);
                        command.Parameters.AddWithValue("@MemoryUsedGb", entry.MemoryUsedGb);
                        command.Parameters.AddWithValue("@MemoryTotalGb", entry.MemoryTotalGb);
                        command.Parameters.AddWithValue("@DiskMaxUsedPercent", entry.DiskMaxUsedPercent);
                        command.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                AiDataRepositorySupport.LogThrottledWarning(
                    "AI_HEALTH_WRITE_FAILED",
                    "Scrittura snapshot salute AI fallita. La produzione non viene bloccata.",
                    ex);
                return false;
            }
        }
    }
}
