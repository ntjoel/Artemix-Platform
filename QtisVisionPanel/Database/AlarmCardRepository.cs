using MySql.Data.MySqlClient;
using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace QtisVisionPanel.Database
{
    /// <summary>
    /// Dual-storage repository for ejection alarm card configuration.
    ///
    /// Every write goes to both MySQL (<c>cfg_alarm_cards</c>) and an XML
    /// backup file under the recipe folder.  Reads try MySQL first and fall
    /// back to the XML copy so the alarm configuration survives a DB outage.
    ///
    /// Physical output channels are stored per-alarm but are authoritative only
    /// when they match <c>Config.xml BlockingAlarmOutput / NonBlockingAlarmOutput</c>.
    /// The runtime (<see cref="Services.IntegratedAlarmCardService"/>) resolves
    /// the real channel at trigger time and overwrites stale rows on save.
    /// </summary>
    public class AlarmCardRepository
    {
        private static readonly Logger _logger = MainWindow.logger;
        private readonly string _backupFilePath;
        private readonly string _connectionString;

        public AlarmCardRepository()
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            _connectionString = DbConnectionStringHelper.Build(config.Host, config.Db, config.User, config.Password, config.port);

            // Percorso del file XML di backup
            _backupFilePath = Path.Combine(
                MainWindow.configManager.Config.Configuration.Recipe_Folder,
                "cfg",
                "alarm_cards_backup.xml"
            );

            // Assicura che la directory esista
            Directory.CreateDirectory(Path.GetDirectoryName(_backupFilePath));
        }

        #region Database Operations

        /// <summary>
        /// Crea la tabella per gli allarmi se non esiste
        /// </summary>
        public async Task CreateTableIfNotExistsAsync()
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string createTableQuery = @"
                        CREATE TABLE IF NOT EXISTS `cfg_alarm_cards` (
                            `Id` INT NOT NULL AUTO_INCREMENT,
                            `Name` VARCHAR(100) NOT NULL,
                            `AlarmType` VARCHAR(50) NOT NULL,
                            `Enabled` BOOLEAN DEFAULT FALSE,
                            `Threshold` INT DEFAULT 5,
                            `BufferSize` INT DEFAULT 10,
                            `SignalType` VARCHAR(50) DEFAULT 'Blocking',
                            `SignalID` VARCHAR(50) DEFAULT 'DO0',
                            `DurationMs` INT DEFAULT 0,
                            `MonitoredDefects` JSON DEFAULT NULL,
                            `SortOrder` INT DEFAULT 0,
                            `IsDeleted` BOOLEAN DEFAULT FALSE,
                            `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
                            `UpdatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                            PRIMARY KEY (`Id`),
                            UNIQUE KEY `uk_Name` (`Name`),
                            KEY `idx_SortOrder` (`SortOrder`),
                            KEY `idx_IsDeleted` (`IsDeleted`)
                        ) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4;";

                    using (var cmd = new MySqlCommand(createTableQuery, conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }

                    _logger.Info("Tabella cfg_alarm_cards creata/verificata con successo");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nella creazione della tabella cfg_alarm_cards: {ex.Message}");
            }
        }

        /// <summary>
        /// Inserisce gli allarmi di default se la tabella è vuota
        /// </summary>
        public async Task InsertDefaultAlarmsIfEmptyAsync()
        {
            try
            {
                var existingAlarms = await GetAllAlarmsAsync();
                if (existingAlarms.Any())
                {
                    _logger.Info("Allarmi già presenti nel database, skip inserimento default");
                    return;
                }

                var defaultAlarms = new List<EjectionAlarmConfig>
                {
                    new EjectionAlarmConfig
                    {
                        Name = "Alarm 1 - Consecutive (N)",
                        AlarmType = AlarmType.ConsecutiveEvents,
                        Threshold = 5,
                        SignalType = SignalType.Blocking,
                        SignalID = "DO0",
                        OutputChannelId = "DO0",
                        Enabled = false
                    },
                    new EjectionAlarmConfig
                    {
                        Name = "Alarm 2 - Consecutive (M)",
                        AlarmType = AlarmType.ConsecutiveEventsAlt,
                        Threshold = 10,
                        SignalType = SignalType.Blocking,
                        SignalID = "DO0",
                        OutputChannelId = "DO0",
                        Enabled = false
                    },
                    new EjectionAlarmConfig
                    {
                        Name = "Alarm 3 - Percentage",
                        AlarmType = AlarmType.PercentageInBuffer,
                        Threshold = 50,
                        BufferSize = 10,
                        SignalType = SignalType.Blocking,
                        SignalID = "DO0",
                        OutputChannelId = "DO0",
                        Enabled = false
                    },
                    // Allarmi NC (Non Conforming)
                    new EjectionAlarmConfig
                    {
                        Name = "NC - Consecutive (N)",
                        AlarmType = AlarmType.ConsecutiveEvents,
                        Threshold = 5,
                        SignalType = SignalType.NotBlocking,
                        SignalID = "DO0",
                        OutputChannelId = "DO0",
                        Enabled = false
                    },
                    new EjectionAlarmConfig
                    {
                        Name = "NC - Consecutive (M)",
                        AlarmType = AlarmType.ConsecutiveEventsAlt,
                        Threshold = 10,
                        SignalType = SignalType.NotBlocking,
                        SignalID = "DO0",
                        OutputChannelId = "DO0",
                        Enabled = false
                    },
                    new EjectionAlarmConfig
                    {
                        Name = "NC - Percentage",
                        AlarmType = AlarmType.PercentageInBuffer,
                        Threshold = 50,
                        BufferSize = 10,
                        SignalType = SignalType.NotBlocking,
                        SignalID = "DO0",
                        OutputChannelId = "DO0",
                        Enabled = false
                    }
                };

                for (int i = 0; i < defaultAlarms.Count; i++)
                {
                    await InsertAlarmAsync(defaultAlarms[i], i);
                }

                _logger.Info($"Inseriti {defaultAlarms.Count} allarmi di default");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inserimento degli allarmi di default: {ex.Message}");
            }
        }

        /// <summary>
        /// Recupera tutti gli allarmi dal database
        /// </summary>
        public async Task<List<EjectionAlarmConfig>> GetAllAlarmsAsync()
        {
            var alarms = new List<EjectionAlarmConfig>();

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"
                        SELECT Id, Name, AlarmType, Enabled, Threshold, BufferSize, 
                               SignalType, SignalID, DurationMs, MonitoredDefects, SortOrder
                        FROM cfg_alarm_cards 
                        WHERE IsDeleted = FALSE 
                        ORDER BY SortOrder ASC, Id ASC";

                    using (var cmd = new MySqlCommand(query, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string signalIdValue = reader.IsDBNull(7) ? "DO0" : reader.GetString(7);
                            var alarm = new EjectionAlarmConfig
                            {
                                Name = reader.GetString(1),
                                Enabled = reader.GetBoolean(3),
                                Threshold = reader.GetInt32(4),
                                BufferSize = reader.GetInt32(5),
                                SignalID = signalIdValue,
                                OutputChannelId = signalIdValue,
                                DurationMs = reader.GetInt32(8),
                                MonitoredDefects = ParseMonitoredDefects(reader.IsDBNull(9) ? null : reader.GetString(9))
                            };

                            // Parse AlarmType
                            if (Enum.TryParse<AlarmType>(reader.GetString(2), out var alarmType))
                            {
                                alarm.AlarmType = alarmType;
                            }

                            // Parse SignalType
                            if (Enum.TryParse<SignalType>(reader.GetString(6), out var signalType))
                            {
                                alarm.SignalType = signalType;
                            }

                            alarms.Add(alarm);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel recupero degli allarmi dal database: {ex.Message}");
            }

            return alarms;
        }

        /// <summary>
        /// Inserisce un nuovo allarme
        /// </summary>
        public async Task<bool> InsertAlarmAsync(EjectionAlarmConfig alarm, int sortOrder = 0)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"
                        INSERT INTO cfg_alarm_cards 
                        (Name, AlarmType, Enabled, Threshold, BufferSize, SignalType, SignalID, DurationMs, MonitoredDefects, SortOrder)
                        VALUES 
                        (@Name, @AlarmType, @Enabled, @Threshold, @BufferSize, @SignalType, @SignalID, @DurationMs, @MonitoredDefects, @SortOrder)";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Name", alarm.Name);
                        cmd.Parameters.AddWithValue("@AlarmType", alarm.AlarmType.ToString());
                        cmd.Parameters.AddWithValue("@Enabled", alarm.Enabled);
                        cmd.Parameters.AddWithValue("@Threshold", alarm.Threshold);
                        cmd.Parameters.AddWithValue("@BufferSize", alarm.BufferSize);
                        cmd.Parameters.AddWithValue("@SignalType", alarm.SignalType.ToString());
                        cmd.Parameters.AddWithValue("@SignalID", alarm.OutputChannelId);
                        cmd.Parameters.AddWithValue("@DurationMs", alarm.DurationMs);
                        cmd.Parameters.AddWithValue("@MonitoredDefects", SerializeMonitoredDefects(alarm.MonitoredDefects));
                        cmd.Parameters.AddWithValue("@SortOrder", sortOrder);

                        await cmd.ExecuteNonQueryAsync();
                    }

                    _logger.Info($"Allarme '{alarm.Name}' inserito nel database");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inserimento dell'allarme: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Aggiorna un allarme esistente
        /// </summary>
        public async Task<bool> UpdateAlarmAsync(string originalName, EjectionAlarmConfig alarm)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"
                        UPDATE cfg_alarm_cards 
                        SET Name = @Name, 
                            AlarmType = @AlarmType, 
                            Enabled = @Enabled, 
                            Threshold = @Threshold, 
                            BufferSize = @BufferSize, 
                            SignalType = @SignalType, 
                            SignalID = @SignalID, 
                            DurationMs = @DurationMs, 
                            MonitoredDefects = @MonitoredDefects,
                            UpdatedAt = CURRENT_TIMESTAMP
                        WHERE Name = @OriginalName AND IsDeleted = FALSE";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@OriginalName", originalName);
                        cmd.Parameters.AddWithValue("@Name", alarm.Name);
                        cmd.Parameters.AddWithValue("@AlarmType", alarm.AlarmType.ToString());
                        cmd.Parameters.AddWithValue("@Enabled", alarm.Enabled);
                        cmd.Parameters.AddWithValue("@Threshold", alarm.Threshold);
                        cmd.Parameters.AddWithValue("@BufferSize", alarm.BufferSize);
                        cmd.Parameters.AddWithValue("@SignalType", alarm.SignalType.ToString());
                        cmd.Parameters.AddWithValue("@SignalID", alarm.OutputChannelId);
                        cmd.Parameters.AddWithValue("@DurationMs", alarm.DurationMs);
                        cmd.Parameters.AddWithValue("@MonitoredDefects", SerializeMonitoredDefects(alarm.MonitoredDefects));

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            _logger.Info($"Allarme '{originalName}' aggiornato nel database");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'aggiornamento dell'allarme: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Elimina (soft delete) un allarme
        /// </summary>
        public async Task<bool> DeleteAlarmAsync(string name)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"
                        UPDATE cfg_alarm_cards 
                        SET IsDeleted = TRUE, UpdatedAt = CURRENT_TIMESTAMP
                        WHERE Name = @Name";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Name", name);
                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            _logger.Info($"Allarme '{name}' eliminato dal database");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'eliminazione dell'allarme: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Verifica se il database è raggiungibile
        /// </summary>
        public async Task<bool> IsDatabaseAvailableAsync()
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region XML Backup Operations

        /// <summary>
        /// Salva tutti gli allarmi nel file XML di backup
        /// </summary>
        public async Task SaveToXmlBackupAsync(List<EjectionAlarmConfig> alarms)
        {
            try
            {
                var backupConfig = new AlarmCardsBackupConfig
                {
                    LastBackupDate = DateTime.Now,
                    Alarms = alarms
                };

                var serializer = new XmlSerializer(typeof(AlarmCardsBackupConfig));
                var settings = new XmlWriterSettings
                {
                    Encoding = System.Text.Encoding.UTF8,
                    Indent = true,
                    IndentChars = "  "
                };

                using (var writer = XmlWriter.Create(_backupFilePath, settings))
                {
                    serializer.Serialize(writer, backupConfig);
                }

                //_logger.Info($"Backup XML salvato in: {_backupFilePath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel salvataggio del backup XML: {ex.Message}");
            }
        }

        /// <summary>
        /// Carica gli allarmi dal file XML di backup
        /// </summary>
        public async Task<List<EjectionAlarmConfig>> LoadFromXmlBackupAsync()
        {
            var alarms = new List<EjectionAlarmConfig>();

            try
            {
                if (!File.Exists(_backupFilePath))
                {
                    _logger.Warn($"File di backup XML non trovato: {_backupFilePath}");
                    return alarms;
                }

                var serializer = new XmlSerializer(typeof(AlarmCardsBackupConfig));

                using (var stream = new FileStream(_backupFilePath, FileMode.Open))
                {
                    var backupConfig = (AlarmCardsBackupConfig)serializer.Deserialize(stream);
                    alarms = backupConfig.Alarms ?? new List<EjectionAlarmConfig>();
                }

                _logger.Info($"Caricati {alarms.Count} allarmi dal backup XML");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel caricamento del backup XML: {ex.Message}");
            }

            return alarms;
        }

        /// <summary>
        /// Sincronizza il database con il file XML di backup
        /// </summary>
        public async Task SyncToBackupAsync()
        {
            var alarms = await GetAllAlarmsAsync();
            await SaveToXmlBackupAsync(alarms);
        }

        #endregion

        #region Helper Methods

        private List<DefectType> ParseMonitoredDefects(string json)
        {
            var defects = new List<DefectType>();

            if (string.IsNullOrEmpty(json))
                return defects;

            try
            {
                // Rimuovi le parentesi quadre e le virgolette
                var cleaned = json.Trim('[', ']').Replace("\"", "");
                var items = cleaned.Split(',');

                foreach (var item in items)
                {
                    if (Enum.TryParse<DefectType>(item.Trim(), out var defect))
                    {
                        defects.Add(defect);
                    }
                }
            }
            catch
            {
                // In caso di errore, usa i difetti di default
                defects = new List<DefectType>
                {
                    DefectType.Logo, DefectType.PrintCentering, DefectType.OpenFlaps,
                    DefectType.SurfaceCheck, DefectType.Height, DefectType.SideSealing,
                    DefectType.SideRollCount, DefectType.ShapeTop, DefectType.ShapeSide, DefectType.FrontTraceability,
                    DefectType.ThreeDHeight, DefectType.ThreeDWidth, DefectType.ThreeDLength,
                    DefectType.BottomSealing, DefectType.TrappedPaper, DefectType.AIClassification
                };
            }

            return defects;
        }

        private string SerializeMonitoredDefects(List<DefectType> defects)
        {
            if (defects == null || defects.Count == 0)
                return "[]";

            var items = defects.Select(d => $"\"{d}\"");
            return $"[{string.Join(",", items)}]";
        }

        #endregion

        #region Alarm Events

        /// <summary>
        /// Persists an alarm trigger event to the alarm_events table. Best-effort — never throws.
        /// </summary>
        public async Task LogAlarmEventAsync(AlarmTriggeredEventArgs e)
        {
            try
            {
                string defectsJson = null;
                if (e.DefectCounts != null && e.DefectCounts.Count > 0)
                {
                    var sb = new StringBuilder("{");
                    bool first = true;
                    foreach (var kv in e.DefectCounts)
                    {
                        if (!first) sb.Append(',');
                        sb.Append($"\"{kv.Key}\":{kv.Value}");
                        first = false;
                    }
                    sb.Append('}');
                    defectsJson = sb.ToString();
                }

                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new MySqlCommand(null, conn))
                    {
                        cmd.CommandText = @"
                            INSERT INTO `alarm_events`
                                (`AlarmName`, `TriggerTime`, `DefectsJson`, `OutputChannel`, `DurationMs`)
                            VALUES
                                (@name, @ts, @defects, @channel, @duration)";

                        cmd.Parameters.AddWithValue("@name",     e.Alarm?.Name     ?? string.Empty);
                        cmd.Parameters.AddWithValue("@ts",       e.TriggerTime);
                        cmd.Parameters.AddWithValue("@defects",  (object)defectsJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@channel",  (object)e.Alarm?.OutputChannelId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@duration", e.Alarm?.PulseDurationMs ?? 0);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ALARM_EVENT_LOG_FAILED|alarm={0}", e.Alarm?.Name);
            }
        }

        #endregion
    }

    /// <summary>
    /// Classe per la serializzazione XML del backup
    /// </summary>
    [Serializable]
    [XmlRoot("AlarmCardsBackup")]
    public class AlarmCardsBackupConfig
    {
        [XmlElement("LastBackupDate")]
        public DateTime LastBackupDate { get; set; }

        [XmlArray("Alarms")]
        [XmlArrayItem("Alarm")]
        public List<EjectionAlarmConfig> Alarms { get; set; } = new List<EjectionAlarmConfig>();
    }
}
