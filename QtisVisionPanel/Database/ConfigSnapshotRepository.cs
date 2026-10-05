using MySql.Data.MySqlClient;
using QtisVisionPanel.Cls_Config.Calss_structure;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    /// <summary>
    /// Low-level MySQL persistence used by <see cref="Services.ConfigurationRecoveryService"/>.
    /// Stores serialized XML snapshots keyed by a logical scope plus a stable path hash.
    /// </summary>
    public class ConfigSnapshotRepository
    {
        private static readonly SemaphoreSlim RepositoryGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Inserts or updates the latest snapshot for the requested file/scope.
        /// </summary>
        public async Task SaveSnapshotAsync(string scope, string filePath, string payloadXml, MySqlConnectionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(scope) ||
                string.IsNullOrWhiteSpace(filePath) ||
                string.IsNullOrWhiteSpace(payloadXml))
            {
                return;
            }

            var connectionString = BuildConnectionString(settings);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    await EnsureTableAsync(connection).ConfigureAwait(false);

                    const string query = @"
INSERT INTO tblconfigsnapshot (scope, file_path, path_hash, file_name, machine_name, payload_xml, updated_at)
VALUES (@Scope, @FilePath, @PathHash, @FileName, @MachineName, @PayloadXml, @UpdatedAt)
ON DUPLICATE KEY UPDATE
    payload_xml = VALUES(payload_xml),
    file_name = VALUES(file_name),
    machine_name = VALUES(machine_name),
    updated_at = VALUES(updated_at);";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Scope", scope.Trim());
                        command.Parameters.AddWithValue("@FilePath", filePath.Trim());
                        command.Parameters.AddWithValue("@PathHash", ComputeHash(filePath));
                        command.Parameters.AddWithValue("@FileName", System.IO.Path.GetFileName(filePath) ?? string.Empty);
                        command.Parameters.AddWithValue("@MachineName", Environment.MachineName);
                        command.Parameters.AddWithValue("@PayloadXml", payloadXml);
                        command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        /// <summary>
        /// Reads a previously stored snapshot for the requested file/scope.
        /// </summary>
        public async Task<string> TryGetSnapshotAsync(string scope, string filePath, MySqlConnectionSettings settings)
        {
            if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var connectionString = BuildConnectionString(settings);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return null;
            }

            await RepositoryGate.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    await EnsureTableAsync(connection).ConfigureAwait(false);

                    const string query = @"
SELECT payload_xml
FROM tblconfigsnapshot
WHERE scope = @Scope AND path_hash = @PathHash
LIMIT 1;";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Scope", scope.Trim());
                        command.Parameters.AddWithValue("@PathHash", ComputeHash(filePath));

                        var scalar = await command.ExecuteScalarAsync().ConfigureAwait(false);
                        return scalar == null || scalar == DBNull.Value
                            ? null
                            : scalar.ToString();
                    }
                }
            }
            finally
            {
                RepositoryGate.Release();
            }
        }

        /// <summary>
        /// Creates the snapshot table on demand so the recovery feature can be
        /// enabled on existing machines without a separate migration step.
        /// </summary>
        private static async Task EnsureTableAsync(MySqlConnection connection)
        {
            const string query = @"
CREATE TABLE IF NOT EXISTS tblconfigsnapshot (
    id BIGINT NOT NULL AUTO_INCREMENT,
    scope VARCHAR(64) NOT NULL,
    file_path VARCHAR(512) NOT NULL,
    path_hash CHAR(64) NOT NULL,
    file_name VARCHAR(255) NOT NULL,
    machine_name VARCHAR(128) NULL,
    payload_xml LONGTEXT NOT NULL,
    updated_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_tblconfigsnapshot_scope_hash (scope, path_hash)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            using (var command = new MySqlCommand(query, connection))
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds a minimal and short-timeout connection string for recovery tasks.
        /// </summary>
        private static string BuildConnectionString(MySqlConnectionSettings settings)
        {
            if (settings == null ||
                string.IsNullOrWhiteSpace(settings.Host) ||
                string.IsNullOrWhiteSpace(settings.Db) ||
                string.IsNullOrWhiteSpace(settings.User) ||
                string.IsNullOrWhiteSpace(settings.port))
            {
                return null;
            }

            if (!uint.TryParse(settings.port, out var port))
            {
                return null;
            }

            var builder = new MySqlConnectionStringBuilder
            {
                Server = settings.Host,
                Database = settings.Db,
                UserID = settings.User,
                Password = settings.Password ?? string.Empty,
                Port = port,
                ConnectionTimeout = 5,
                DefaultCommandTimeout = 10,
                Pooling = true
            };

            if (settings.SslDisabled)
            {
                builder.SslMode = MySqlSslMode.Disabled;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Generates a normalized stable hash so path matching is resilient to
        /// casing differences and avoids overlong unique keys in MySQL.
        /// </summary>
        private static string ComputeHash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
