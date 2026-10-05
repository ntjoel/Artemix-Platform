using MySql.Data.MySqlClient;
using QtisVisionPanel.Cls_Config.Calss_structure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    public sealed class OpcUaConfigurationRepository
    {
        public async Task CreateTableIfNotExistsAsync()
        {
            using (var conn = new MySqlConnection(GetConnectionString()))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await CreateTableIfNotExistsAsync(conn).ConfigureAwait(false);
            }
        }

        public async Task EnsureTableAndSeedAsync(OpcUaConfig config)
        {
            if (config == null)
            {
                return;
            }

            using (var conn = new MySqlConnection(GetConnectionString()))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await CreateTableIfNotExistsAsync(conn).ConfigureAwait(false);
                await SeedSettingsAsync(conn, config).ConfigureAwait(false);
                await SeedNodesAsync(conn, config).ConfigureAwait(false);
            }
        }

        public async Task<OpcUaConfig> LoadMergedAsync(OpcUaConfig xmlConfig)
        {
            var config = xmlConfig ?? new OpcUaConfig();

            using (var conn = new MySqlConnection(GetConnectionString()))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await CreateTableIfNotExistsAsync(conn).ConfigureAwait(false);

                using (var cmd = new MySqlCommand("SELECT ItemType, ItemKey, NodeId, Direction, DataType, Value, Enabled, `Required`, Description FROM cfg_opcua_configuration ORDER BY SortOrder, Id;", conn))
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    var nodes = new Dictionary<string, OpcUaNodeConfig>(StringComparer.OrdinalIgnoreCase);
                    foreach (var node in config.Nodes ?? OpcUaConfig.CreateDefaultNodes())
                    {
                        if (!string.IsNullOrWhiteSpace(node.Key))
                        {
                            nodes[node.Key] = node;
                        }
                    }

                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        var itemType = Convert.ToString(reader["ItemType"]);
                        var itemKey = Convert.ToString(reader["ItemKey"]);

                        if (string.Equals(itemType, "Setting", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplySetting(config, itemKey, Convert.ToString(reader["Value"]));
                            continue;
                        }

                        if (string.Equals(itemType, "Node", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(itemKey))
                        {
                            OpcUaNodeConfig node;
                            if (!nodes.TryGetValue(itemKey, out node))
                            {
                                node = new OpcUaNodeConfig { Key = itemKey };
                                nodes[itemKey] = node;
                            }

                            node.NodeId = Convert.ToString(reader["NodeId"]);
                            node.Direction = Convert.ToString(reader["Direction"]);
                            node.DataType = Convert.ToString(reader["DataType"]);
                            node.Enabled = Convert.ToBoolean(reader["Enabled"]);
                            node.Required = Convert.ToBoolean(reader["Required"]);
                            node.Description = Convert.ToString(reader["Description"]);
                        }
                    }

                    config.Nodes = new List<OpcUaNodeConfig>(nodes.Values);
                }
            }

            return config;
        }

        public async Task SaveAsync(OpcUaConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            using (var conn = new MySqlConnection(GetConnectionString()))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await CreateTableIfNotExistsAsync(conn).ConfigureAwait(false);
                await SaveSettingsAsync(conn, config).ConfigureAwait(false);
                await SaveNodesAsync(conn, config).ConfigureAwait(false);
            }
        }

        public static async Task CreateTableIfNotExistsAsync(MySqlConnection conn)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS `cfg_opcua_configuration` (
                    `Id` INT NOT NULL AUTO_INCREMENT,
                    `ItemType` VARCHAR(20) NOT NULL,
                    `ItemKey` VARCHAR(100) NOT NULL,
                    `NodeId` VARCHAR(255) NULL,
                    `Direction` VARCHAR(30) NULL,
                    `DataType` VARCHAR(50) NULL,
                    `Value` TEXT NULL,
                    `Enabled` BOOLEAN NOT NULL DEFAULT TRUE,
                    `Required` BOOLEAN NOT NULL DEFAULT FALSE,
                    `Description` VARCHAR(255) NULL,
                    `SortOrder` INT NOT NULL DEFAULT 0,
                    `UpdatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    PRIMARY KEY (`Id`),
                    UNIQUE KEY `ux_cfg_opcua_item` (`ItemType`, `ItemKey`),
                    KEY `idx_cfg_opcua_type` (`ItemType`)
                ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static async Task SeedSettingsAsync(MySqlConnection conn, OpcUaConfig config)
        {
            var settings = new[]
            {
                new KeyValuePair<string, string>("Enabled", config.Enabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RequiredForMachineRun", config.RequiredForMachineRun.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("UseDatabaseConfiguration", config.UseDatabaseConfiguration.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("ServerUrl", config.ServerUrl),
                new KeyValuePair<string, string>("ApplicationName", config.ApplicationName),
                new KeyValuePair<string, string>("UseSecurity", config.UseSecurity.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("AutoAcceptUntrustedCertificates", config.AutoAcceptUntrustedCertificates.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RemoteCommandsEnabled", config.RemoteCommandsEnabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RemoteStartStopEnabled", config.RemoteStartStopEnabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RemoteRecipeChangeEnabled", config.RemoteRecipeChangeEnabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("AuditRemoteCommandsToDatabase", config.AuditRemoteCommandsToDatabase.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("ReconnectIntervalMs", config.ReconnectIntervalMs.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("SubscriptionIntervalMs", config.SubscriptionIntervalMs.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("SessionTimeoutMs", config.SessionTimeoutMs.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("OperationTimeoutMs", config.OperationTimeoutMs.ToString(CultureInfo.InvariantCulture))
            };

            var sortOrder = 0;
            foreach (var setting in settings)
            {
                await InsertIgnoreAsync(conn, "Setting", setting.Key, null, null, null, setting.Value, true, false, "OPC UA runtime setting.", sortOrder++).ConfigureAwait(false);
            }
        }

        private static async Task SeedNodesAsync(MySqlConnection conn, OpcUaConfig config)
        {
            var sortOrder = 100;
            foreach (var node in config.Nodes ?? OpcUaConfig.CreateDefaultNodes())
            {
                await InsertIgnoreAsync(
                    conn,
                    "Node",
                    node.Key,
                    node.NodeId,
                    node.Direction,
                    node.DataType,
                    null,
                    node.Enabled,
                    node.Required,
                    node.Description,
                    sortOrder++).ConfigureAwait(false);
            }
        }

        private static async Task SaveSettingsAsync(MySqlConnection conn, OpcUaConfig config)
        {
            var settings = new[]
            {
                new KeyValuePair<string, string>("Enabled", config.Enabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RequiredForMachineRun", config.RequiredForMachineRun.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("UseDatabaseConfiguration", config.UseDatabaseConfiguration.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("ServerUrl", config.ServerUrl),
                new KeyValuePair<string, string>("ApplicationName", config.ApplicationName),
                new KeyValuePair<string, string>("UseSecurity", config.UseSecurity.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("AutoAcceptUntrustedCertificates", config.AutoAcceptUntrustedCertificates.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RemoteCommandsEnabled", config.RemoteCommandsEnabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RemoteStartStopEnabled", config.RemoteStartStopEnabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("RemoteRecipeChangeEnabled", config.RemoteRecipeChangeEnabled.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("AuditRemoteCommandsToDatabase", config.AuditRemoteCommandsToDatabase.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("ReconnectIntervalMs", config.ReconnectIntervalMs.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("SubscriptionIntervalMs", config.SubscriptionIntervalMs.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("SessionTimeoutMs", config.SessionTimeoutMs.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("OperationTimeoutMs", config.OperationTimeoutMs.ToString(CultureInfo.InvariantCulture))
            };

            var sortOrder = 0;
            foreach (var setting in settings)
            {
                await UpsertAsync(conn, "Setting", setting.Key, null, null, null, setting.Value, true, false, "OPC UA runtime setting.", sortOrder++).ConfigureAwait(false);
            }
        }

        private static async Task SaveNodesAsync(MySqlConnection conn, OpcUaConfig config)
        {
            var sortOrder = 100;
            foreach (var node in config.Nodes ?? OpcUaConfig.CreateDefaultNodes())
            {
                await UpsertAsync(
                    conn,
                    "Node",
                    node.Key,
                    node.NodeId,
                    node.Direction,
                    node.DataType,
                    null,
                    node.Enabled,
                    node.Required,
                    node.Description,
                    sortOrder++).ConfigureAwait(false);
            }
        }

        private static async Task UpsertAsync(
            MySqlConnection conn,
            string itemType,
            string itemKey,
            string nodeId,
            string direction,
            string dataType,
            string value,
            bool enabled,
            bool required,
            string description,
            int sortOrder)
        {
            const string sql = @"
                INSERT INTO cfg_opcua_configuration
                    (ItemType, ItemKey, NodeId, Direction, DataType, Value, Enabled, `Required`, Description, SortOrder)
                VALUES
                    (@itemType, @itemKey, @nodeId, @direction, @dataType, @value, @enabled, @required, @description, @sortOrder)
                ON DUPLICATE KEY UPDATE
                    NodeId = VALUES(NodeId),
                    Direction = VALUES(Direction),
                    DataType = VALUES(DataType),
                    Value = VALUES(Value),
                    Enabled = VALUES(Enabled),
                    `Required` = VALUES(`Required`),
                    Description = VALUES(Description),
                    SortOrder = VALUES(SortOrder);";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@itemType", itemType);
                cmd.Parameters.AddWithValue("@itemKey", itemKey);
                cmd.Parameters.AddWithValue("@nodeId", (object)nodeId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@direction", (object)direction ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dataType", (object)dataType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@value", (object)value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@enabled", enabled);
                cmd.Parameters.AddWithValue("@required", required);
                cmd.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static async Task InsertIgnoreAsync(
            MySqlConnection conn,
            string itemType,
            string itemKey,
            string nodeId,
            string direction,
            string dataType,
            string value,
            bool enabled,
            bool required,
            string description,
            int sortOrder)
        {
            const string sql = @"
                INSERT IGNORE INTO cfg_opcua_configuration
                    (ItemType, ItemKey, NodeId, Direction, DataType, Value, Enabled, `Required`, Description, SortOrder)
                VALUES
                    (@itemType, @itemKey, @nodeId, @direction, @dataType, @value, @enabled, @required, @description, @sortOrder);";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@itemType", itemType);
                cmd.Parameters.AddWithValue("@itemKey", itemKey);
                cmd.Parameters.AddWithValue("@nodeId", (object)nodeId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@direction", (object)direction ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dataType", (object)dataType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@value", (object)value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@enabled", enabled);
                cmd.Parameters.AddWithValue("@required", required);
                cmd.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static void ApplySetting(OpcUaConfig config, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            switch (key.Trim())
            {
                case "Enabled":
                    config.Enabled = ParseBool(value, config.Enabled);
                    break;
                case "RequiredForMachineRun":
                    config.RequiredForMachineRun = ParseBool(value, config.RequiredForMachineRun);
                    break;
                case "UseDatabaseConfiguration":
                    config.UseDatabaseConfiguration = ParseBool(value, config.UseDatabaseConfiguration);
                    break;
                case "ServerUrl":
                    config.ServerUrl = value;
                    break;
                case "ApplicationName":
                    config.ApplicationName = value;
                    break;
                case "UseSecurity":
                    config.UseSecurity = ParseBool(value, config.UseSecurity);
                    break;
                case "AutoAcceptUntrustedCertificates":
                    config.AutoAcceptUntrustedCertificates = ParseBool(value, config.AutoAcceptUntrustedCertificates);
                    break;
                case "RemoteCommandsEnabled":
                    config.RemoteCommandsEnabled = ParseBool(value, config.RemoteCommandsEnabled);
                    break;
                case "RemoteStartStopEnabled":
                    config.RemoteStartStopEnabled = ParseBool(value, config.RemoteStartStopEnabled);
                    break;
                case "RemoteRecipeChangeEnabled":
                    config.RemoteRecipeChangeEnabled = ParseBool(value, config.RemoteRecipeChangeEnabled);
                    break;
                case "AuditRemoteCommandsToDatabase":
                    config.AuditRemoteCommandsToDatabase = ParseBool(value, config.AuditRemoteCommandsToDatabase);
                    break;
                case "ReconnectIntervalMs":
                    config.ReconnectIntervalMs = ParseInt(value, config.ReconnectIntervalMs);
                    break;
                case "SubscriptionIntervalMs":
                    config.SubscriptionIntervalMs = ParseInt(value, config.SubscriptionIntervalMs);
                    break;
                case "SessionTimeoutMs":
                    config.SessionTimeoutMs = ParseInt(value, config.SessionTimeoutMs);
                    break;
                case "OperationTimeoutMs":
                    config.OperationTimeoutMs = ParseInt(value, config.OperationTimeoutMs);
                    break;
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static string GetConnectionString()
        {
            var mysql = MainWindow.configManager?.Config?.MySqlConnection;
            if (mysql == null)
            {
                throw new InvalidOperationException("MySQL configuration is not loaded.");
            }

            return DbConnectionStringHelper.Build(mysql.Host, mysql.Db, mysql.User, mysql.Password, mysql.port, mysql.SslDisabled);
        }
    }
}
