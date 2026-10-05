using MySql.Data.MySqlClient;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    /// <summary>
    /// Persistenza delle misure strutturate di ispezione in `tbl_inspection_measurements`.
    ///
    /// Segue lo stesso pattern di <see cref="EventLogRepository"/>: gate a semaforo,
    /// coppia metodo async/sync, connection-string costruita per chiamata e
    /// degradazione silenziosa (ritorna false, non lancia) quando MySQL non e' disponibile.
    /// La scrittura e' batch: tutte le misure di un prodotto in una sola transazione.
    /// </summary>
    public class InspectionMeasurementRepository
    {
        private static readonly SemaphoreSlim RepositoryGate = new SemaphoreSlim(1, 1);

        public async Task<bool> InsertBatchAsync(IReadOnlyList<InspectionMeasurementEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return true;

            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return InsertBatchCore(entries);
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        private bool InsertBatchCore(IReadOnlyList<InspectionMeasurementEntry> entries)
        {
            var connectionString = AiDataRepositorySupport.BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                AiDataRepositorySupport.LogThrottledWarning(
                    "AI_MEASUREMENTS_DB_NOT_CONFIGURED",
                    "Connessione MySQL non disponibile per la cattura misure AI. La produzione non viene bloccata.");
                return false;
            }

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    if (AiDataRepositorySupport.ShouldRunRetention("tbl_inspection_measurements"))
                    {
                        int retentionDays = AiDataRepositorySupport.ReadRetentionDays(
                            bindings => bindings.AiInspectionMeasurementRetentionDays,
                            180);
                        AiDataRepositorySupport.ApplyRetention(connection, "tbl_inspection_measurements", retentionDays);
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        const string query = @"
INSERT INTO tbl_inspection_measurements
    (Timestamp, ProductId, Recipe, CameraRole, Feature, MeasuredValue, Tolerance, IsDefect)
VALUES
    (@Timestamp, @ProductId, @Recipe, @CameraRole, @Feature, @MeasuredValue, @Tolerance, @IsDefect);";

                        foreach (var entry in entries)
                        {
                            if (entry == null)
                                continue;

                            using (var command = new MySqlCommand(query, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@Timestamp", entry.Timestamp);
                                command.Parameters.AddWithValue("@ProductId", entry.ProductId);
                                command.Parameters.AddWithValue("@Recipe", entry.Recipe ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@CameraRole", entry.CameraRole ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@Feature", entry.Feature ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@MeasuredValue", entry.MeasuredValue);
                                command.Parameters.AddWithValue("@Tolerance", entry.Tolerance);
                                command.Parameters.AddWithValue("@IsDefect", entry.IsDefect ? 1 : 0);
                                command.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                AiDataRepositorySupport.LogThrottledWarning(
                    "AI_MEASUREMENTS_WRITE_FAILED",
                    "Scrittura misure AI fallita. La produzione non viene bloccata.",
                    ex);
                return false;
            }
        }
    }
}
