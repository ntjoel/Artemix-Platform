using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using QtisVisionPanel.Models.MultiShotTrigger;

namespace QtisVisionPanel.Database
{
    internal static class MultiShotParamHistoryRepository
    {
        private const string TableName = "tbl_multishot_param_history";

        public static async Task CreateTableIfNotExistsAsync(MySqlConnection conn)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS `tbl_multishot_param_history` (
                    `profile`     VARCHAR(20)   NOT NULL,
                    `param_name`  VARCHAR(100)  NOT NULL,
                    `prev_value`  TEXT          NOT NULL,
                    `changed_at`  DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
                    `changed_by`  VARCHAR(100)  NOT NULL DEFAULT '',
                    PRIMARY KEY (`profile`, `param_name`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            using (var cmd = new MySqlCommand(sql, conn))
            {
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Salva lo snapshot dei parametri (valori precedenti) per un profilo.
        /// UPSERT: sovrascrive se esiste già una riga per (profile, param_name).
        /// </summary>
        public static async Task SaveSnapshotAsync(
            MySqlConnection conn,
            string profile,
            Dictionary<string, string> paramValues,
            string changedBy)
        {
            if (paramValues == null || paramValues.Count == 0) return;

            const string sql = @"
                INSERT INTO `tbl_multishot_param_history`
                    (`profile`, `param_name`, `prev_value`, `changed_at`, `changed_by`)
                VALUES
                    (@profile, @param, @value, CURRENT_TIMESTAMP(3), @actor)
                ON DUPLICATE KEY UPDATE
                    `prev_value` = VALUES(`prev_value`),
                    `changed_at` = VALUES(`changed_at`),
                    `changed_by` = VALUES(`changed_by`)";

            foreach (var kv in paramValues)
            {
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@profile", profile ?? "");
                    cmd.Parameters.AddWithValue("@param",   kv.Key ?? "");
                    cmd.Parameters.AddWithValue("@value",   kv.Value ?? "");
                    cmd.Parameters.AddWithValue("@actor",   changedBy ?? "");
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Carica tutti gli snapshot salvati.
        /// Ritorna Dictionary[profile → Dictionary[param_name → prev_value]].
        /// </summary>
        public static async Task<Dictionary<string, Dictionary<string, string>>> LoadAllAsync(MySqlConnection conn)
        {
            const string sql = "SELECT `profile`, `param_name`, `prev_value` FROM `tbl_multishot_param_history`";
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new MySqlCommand(sql, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var profile = reader.GetString(0);
                    var param   = reader.GetString(1);
                    var value   = reader.GetString(2);

                    if (!result.ContainsKey(profile))
                        result[profile] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    result[profile][param] = value;
                }
            }

            return result;
        }

        /// <summary>
        /// Apre una connessione MySQL con i parametri dalla configurazione globale e ritorna la connessione aperta.
        /// Il chiamante è responsabile del Dispose.
        /// </summary>
        public static async Task<MySqlConnection> OpenConnectionAsync()
        {
            var config = MainWindow.configManager?.Config?.MySqlConnection;
            if (config == null) throw new InvalidOperationException("MySQL configuration not available");

            var connStr = DbConnectionStringHelper.Build(
                config.Host, config.Db, config.User, config.Password, config.port);
            var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            return conn;
        }
    }
}
