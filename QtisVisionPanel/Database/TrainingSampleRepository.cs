using MySql.Data.MySqlClient;
using QtisVisionPanel.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    /// <summary>
    /// Indice queryable dei campioni etichettati per il training vision (Fase 3a),
    /// tabella <c>tbl_training_samples</c>. Stesso pattern resiliente di
    /// <see cref="EventLogRepository"/>: gate a semaforo, coppia async/sync,
    /// degradazione silenziosa quando MySQL non e' disponibile.
    ///
    /// Il dataset autorevole per l'addestramento resta il sidecar label.json accanto
    /// alle immagini; questa tabella serve solo a interrogare/contare i campioni.
    /// </summary>
    public class TrainingSampleRepository
    {
        private static readonly SemaphoreSlim RepositoryGate = new SemaphoreSlim(1, 1);

        public async Task<bool> InsertAsync(TrainingSampleEntry entry)
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

        private bool InsertCore(TrainingSampleEntry entry)
        {
            var connectionString = AiDataRepositorySupport.BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                AiDataRepositorySupport.LogThrottledWarning(
                    "AI_TRAINING_DB_NOT_CONFIGURED",
                    "Connessione MySQL non disponibile per l'indice training AI. La produzione non viene bloccata.");
                return false;
            }

            try
            {
                string defects = entry.Defects != null ? string.Join(";", entry.Defects) : string.Empty;

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    if (AiDataRepositorySupport.ShouldRunRetention("tbl_training_samples"))
                    {
                        int retentionDays = AiDataRepositorySupport.ReadRetentionDays(
                            bindings => bindings.AiTrainingSampleRetentionDays,
                            0);
                        AiDataRepositorySupport.ApplyRetention(connection, "tbl_training_samples", retentionDays);
                    }

                    const string query = @"
INSERT INTO tbl_training_samples
    (Timestamp, ProductId, Recipe, Label, Defects, PieceFolder)
VALUES
    (@Timestamp, @ProductId, @Recipe, @Label, @Defects, @PieceFolder);";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Timestamp", entry.Timestamp);
                        command.Parameters.AddWithValue("@ProductId", entry.ProductId);
                        command.Parameters.AddWithValue("@Recipe", entry.Recipe ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Label", entry.Label ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Defects", defects);
                        command.Parameters.AddWithValue("@PieceFolder", entry.PieceFolder ?? (object)DBNull.Value);
                        command.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                AiDataRepositorySupport.LogThrottledWarning(
                    "AI_TRAINING_WRITE_FAILED",
                    "Scrittura indice training AI fallita. La produzione non viene bloccata.",
                    ex);
                return false;
            }
        }
    }
}
