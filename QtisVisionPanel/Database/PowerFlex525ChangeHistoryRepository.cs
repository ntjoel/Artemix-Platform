using MySql.Data.MySqlClient;
using QtisVisionPanel.Models;
using System;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    public class PowerFlex525ChangeHistoryRepository
    {
        private static string GetConnectionString()
        {
            var config = MainWindow.configManager?.Config?.MySqlConnection;
            if (config == null)
                return null;

            return $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
        }

        public async Task EnsureTableAsync()
        {
            var connectionString = GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return;

            using (var conn = new MySqlConnection(connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS `tblpowerflex525_history` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT,
    `Timestamp` DATETIME NOT NULL,
    `User` VARCHAR(100) NULL,
    `Role` VARCHAR(100) NULL,
    `DriveIp` VARCHAR(64) NULL,
    `ParameterNumber` INT NOT NULL,
    `ParameterCode` VARCHAR(16) NULL,
    `ParameterName` VARCHAR(255) NULL,
    `OldValue` DOUBLE NULL,
    `NewValue` DOUBLE NULL,
    `Unit` VARCHAR(32) NULL,
    `Source` VARCHAR(100) NULL,
    `Result` VARCHAR(50) NULL,
    PRIMARY KEY (`Id`),
    KEY `idx_powerflex525_timestamp` (`Timestamp`),
    KEY `idx_powerflex525_parameter` (`ParameterNumber`)
) ENGINE=InnoDB AUTO_INCREMENT=1;";
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task InsertAsync(PowerFlex525ChangeHistoryEntry entry)
        {
            if (entry == null)
                return;

            var connectionString = GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return;

            try
            {
                await EnsureTableAsync().ConfigureAwait(false);
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO `tblpowerflex525_history`
(`Timestamp`, `User`, `Role`, `DriveIp`, `ParameterNumber`, `ParameterCode`, `ParameterName`, `OldValue`, `NewValue`, `Unit`, `Source`, `Result`)
VALUES
(@timestamp, @user, @role, @driveIp, @parameterNumber, @parameterCode, @parameterName, @oldValue, @newValue, @unit, @source, @result);";

                        cmd.Parameters.AddWithValue("@timestamp", entry.Timestamp);
                        cmd.Parameters.AddWithValue("@user", (object)entry.User ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@role", (object)entry.Role ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@driveIp", (object)entry.DriveIp ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@parameterNumber", entry.ParameterNumber);
                        cmd.Parameters.AddWithValue("@parameterCode", (object)entry.ParameterCode ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@parameterName", (object)entry.ParameterName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@oldValue", (object)entry.OldValue ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@newValue", (object)entry.NewValue ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@unit", (object)entry.Unit ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@source", (object)entry.Source ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@result", (object)entry.Result ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 history DB insert skipped: {ex.Message}");
            }
        }
    }
}
