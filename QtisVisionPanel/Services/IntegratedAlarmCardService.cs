using MySql.Data.MySqlClient;
using NLog;
using QtisVisionPanel.Database;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Servizio integrato per la gestione degli allarmi con:
    /// - Database MySQL come sorgente primaria
    /// - File XML come backup
    /// - Integrazione con InspectionProcessor
    /// - Gestione contatori difetti
    ///
    /// Questa classe e' il ponte tra configurazione persistente e runtime:
    /// il manager in memoria valuta i difetti, mentre repository e backup
    /// mantengono la configurazione recuperabile anche senza database.
    /// </summary>
    public class IntegratedAlarmCardService : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly AlarmCardRepository _repository;
        private readonly EjectionAlarmManager _alarmManager;
        private List<EjectionAlarmConfig> _alarms;
        private readonly object _lock = new object();

        // Eventi
        public event EventHandler<AlarmTriggeredEventArgs> AlarmTriggered;
        public event EventHandler<AlarmAcknowledgedEventArgs> AlarmAcknowledged;
        public event EventHandler<string> StatusMessageChanged;

        public IntegratedAlarmCardService()
        {
            _repository = new AlarmCardRepository();
            _alarmManager = new EjectionAlarmManager();
            _alarmManager.AlarmTriggered += OnAlarmTriggered;
            _alarmManager.LogMessage += OnLogMessage;
            _alarms = new List<EjectionAlarmConfig>();
        }

        #region Inizializzazione

        /// <summary>
        /// Inizializza il servizio caricando gli allarmi
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger.Info("Inizializzazione IntegratedAlarmCardService...");

                // Crea tabella se non esiste
                await _repository.CreateTableIfNotExistsAsync();

                // Inserisci allarmi di default se vuoto
                await _repository.InsertDefaultAlarmsIfEmptyAsync();

                // Carica allarmi
                await ReloadAlarmsAsync();

                _logger.Info($"Servizio allarmi inizializzato con {_alarms.Count} allarmi");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inizializzazione del servizio allarmi: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Ricarica gli allarmi dal database o dal backup XML
        /// </summary>
        public async Task ReloadAlarmsAsync()
        {
            try
            {
                bool dbAvailable = await _repository.IsDatabaseAvailableAsync();

                if (dbAvailable)
                {
                    _alarms = await _repository.GetAllAlarmsAsync();
                    _logger.Info($"Caricati {_alarms.Count} allarmi dal database");
                }
                else
                {
                    _alarms = await _repository.LoadFromXmlBackupAsync();
                    _logger.Info($"Database non disponibile. Caricati {_alarms.Count} allarmi dal backup XML");
                }

                // Sincronizza con l'alarm manager
                SyncWithAlarmManager();
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel caricamento degli allarmi: {ex.Message}");
            }
        }

        private void SyncWithAlarmManager()
        {
            lock (_lock)
            {
                // Pulisci e ricarica gli allarmi nel manager
                var existingAlarms = _alarmManager.GetAlarms();
                foreach (var alarm in existingAlarms.ToList())
                {
                    _alarmManager.RemoveAlarm(alarm);
                }

                foreach (var alarm in _alarms)
                {
                    _alarmManager.AddAlarm(alarm);
                }
            }
        }

        #endregion

        #region Gestione Allarmi CRUD

        public List<EjectionAlarmConfig> GetAllAlarms()
        {
            lock (_lock)
            {
                return new List<EjectionAlarmConfig>(_alarms);
            }
        }

        public async Task<bool> AddAlarmAsync(EjectionAlarmConfig alarm, int sortOrder = 0)
        {
            try
            {
                bool dbAvailable = await _repository.IsDatabaseAvailableAsync();

                if (dbAvailable)
                {
                    await _repository.InsertAlarmAsync(alarm, sortOrder);
                }

                lock (_lock)
                {
                    _alarms.Add(alarm);
                    _alarmManager.AddAlarm(alarm);
                }

                await _repository.SaveToXmlBackupAsync(_alarms);
                _logger.Info($"Allarme '{alarm.Name}' aggiunto");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'aggiunta dell'allarme: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateAlarmAsync(string originalName, EjectionAlarmConfig alarm)
        {
            try
            {
                bool dbAvailable = await _repository.IsDatabaseAvailableAsync();

                if (dbAvailable)
                {
                    await _repository.UpdateAlarmAsync(originalName, alarm);
                }

                lock (_lock)
                {
                    var existing = _alarms.FirstOrDefault(a => a.Name == originalName);
                    if (existing != null)
                    {
                        int index = _alarms.IndexOf(existing);
                        _alarms[index] = alarm;
                        _alarmManager.UpdateAlarm(existing, alarm);
                    }
                }

                await _repository.SaveToXmlBackupAsync(_alarms);
                _logger.Info($"Allarme '{originalName}' aggiornato");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'aggiornamento dell'allarme: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteAlarmAsync(string name)
        {
            try
            {
                bool dbAvailable = await _repository.IsDatabaseAvailableAsync();

                if (dbAvailable)
                {
                    await _repository.DeleteAlarmAsync(name);
                }

                lock (_lock)
                {
                    var alarm = _alarms.FirstOrDefault(a => a.Name == name);
                    if (alarm != null)
                    {
                        _alarms.Remove(alarm);
                        _alarmManager.RemoveAlarm(alarm);
                    }
                }

                await _repository.SaveToXmlBackupAsync(_alarms);
                _logger.Info($"Allarme '{name}' eliminato");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'eliminazione dell'allarme: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Elaborazione Ispezione

        /// <summary>
        /// Elabora il risultato dell'ispezione e verifica gli allarmi
        /// </summary>
        public async Task ProcessInspectionResultAsync(InspectionResult inspectionResult)
        {
            try
            {
                if (inspectionResult == null) return;

                // Non elaborare allarmi quando la macchina non è in produzione attiva
                // (evita trigger spurii durante avvio, spegnimento o cambio ricetta)
                if (!ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
                {
                    _logger.Debug("ALARM_PROCESSING_SKIPPED — machine not in continuous run");
                    return;
                }

                // If the caller already resolved which defects must generate a
                // reject/alarm, trust that runtime decision. Otherwise fall back
                // to the legacy "any defect" behaviour for backward compatibility.
                var defectMap = inspectionResult.RejectingDefects != null && inspectionResult.RejectingDefects.Count > 0
                    ? ExtractRejectingDefects(inspectionResult.RejectingDefects)
                    : ExtractDefectsFromInspection(inspectionResult);
                bool hasStructuredDetectedDefects =
                    inspectionResult.DetectedDefects != null &&
                    inspectionResult.DetectedDefects.Any(defect => defect.Value);
                bool hasDefects =
                    inspectionResult.RejectRequired ||
                    (!inspectionResult.IsValid && !hasStructuredDetectedDefects);

                // Processa con l'alarm manager
                _alarmManager.ProcessInspectionResult(hasDefects, defectMap);

                // Log per debug
                if (hasDefects && defectMap.Any(d => d.Value))
                {
                    var triggeredDefects = defectMap.Where(d => d.Value).Select(d => d.Key.ToString());
                    _logger.Debug($"Difetti rilevati: {string.Join(", ", triggeredDefects)}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'elaborazione del risultato ispezione: {ex.Message}");
            }
        }

        /// <summary>
        /// Estrae i difetti dal risultato dell'ispezione
        /// </summary>
        private Dictionary<DefectType, bool> ExtractDefectsFromInspection(InspectionResult result)
        {
            var defectMap = new Dictionary<DefectType, bool>();

            // Inizializza tutti i difetti a false
            foreach (DefectType defect in Enum.GetValues(typeof(DefectType)))
            {
                defectMap[defect] = false;
            }

            // Estrai difetti dai risultati di validazione
            if (result.TopResult?.ErrorMessages != null)
            {
                foreach (var error in result.TopResult.ErrorMessages)
                {
                    MapErrorToDefect(error, defectMap);
                }
            }

            if (result.SideResult?.ErrorMessages != null)
            {
                foreach (var error in result.SideResult.ErrorMessages)
                {
                    MapErrorToDefect(error, defectMap);
                }
            }

            if (result.FrontResult?.ErrorMessages != null)
            {
                foreach (var error in result.FrontResult.ErrorMessages)
                {
                    MapErrorToDefect(error, defectMap);
                }
            }

            if (result.RearResult?.ErrorMessages != null)
            {
                foreach (var error in result.RearResult.ErrorMessages)
                {
                    MapErrorToDefect(error, defectMap);
                }
            }
            if (result.RightResult?.ErrorMessages != null)
            {
                foreach (var error in result.RightResult.ErrorMessages)
                {
                    MapErrorToDefect(error, defectMap);
                }
            }

            if (result.BottomResult?.ErrorMessages != null)
            {
                foreach (var error in result.BottomResult.ErrorMessages)
                {
                    MapErrorToDefect(error, defectMap);
                }
            }

            if (result.MissingCameraResults != null)
            {
                foreach (var missingResult in result.MissingCameraResults)
                {
                    if (missingResult?.ErrorMessages == null)
                    {
                        continue;
                    }

                    foreach (var error in missingResult.ErrorMessages)
                    {
                        MapErrorToDefect(error, defectMap);
                    }
                }
            }

            return defectMap;
        }

        private Dictionary<DefectType, bool> ExtractRejectingDefects(Dictionary<string, bool> rejectingDefects)
        {
            var defectMap = new Dictionary<DefectType, bool>();

            foreach (DefectType defect in Enum.GetValues(typeof(DefectType)))
            {
                defectMap[defect] = false;
            }

            if (rejectingDefects == null)
            {
                return defectMap;
            }

            foreach (var entry in rejectingDefects.Where(current => current.Value))
            {
                if (TryMapFeatureKeyToDefectType(entry.Key, out DefectType defectType))
                {
                    defectMap[defectType] = true;
                }
            }

            return defectMap;
        }

        private static bool TryMapFeatureKeyToDefectType(string featureKey, out DefectType defectType)
        {
            defectType = default;
            switch (featureKey?.Trim())
            {
                case "Logo":
                    defectType = DefectType.Logo;
                    return true;
                case "PrintCentering":
                    defectType = DefectType.PrintCentering;
                    return true;
                case "OpenFlaps":
                    defectType = DefectType.OpenFlaps;
                    return true;
                case "SurfaceCheck":
                    defectType = DefectType.SurfaceCheck;
                    return true;
                case "Height":
                    defectType = DefectType.Height;
                    return true;
                case "SideSealing":
                    defectType = DefectType.SideSealing;
                    return true;
                case "SideRollCount":
                    defectType = DefectType.SideRollCount;
                    return true;
                case "ShapeTop":
                    defectType = DefectType.ShapeTop;
                    return true;
                case "ShapeSide":
                    defectType = DefectType.ShapeSide;
                    return true;
                case "FrontTraceability":
                    defectType = DefectType.FrontTraceability;
                    return true;
                case "ThreeDHeight":
                    defectType = DefectType.ThreeDHeight;
                    return true;
                case "ThreeDWidth":
                    defectType = DefectType.ThreeDWidth;
                    return true;
                case "ThreeDLength":
                    defectType = DefectType.ThreeDLength;
                    return true;
                case "BottomSealing":
                    defectType = DefectType.BottomSealing;
                    return true;
                case "TrappedPaper":
                    defectType = DefectType.TrappedPaper;
                    return true;
                case "AIClassification":
                    defectType = DefectType.AIClassification;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Mappa un messaggio di errore al tipo di difetto corrispondente
        /// </summary>
        private void MapErrorToDefect(string errorMessage, Dictionary<DefectType, bool> defectMap)
        {
            if (string.IsNullOrEmpty(errorMessage)) return;

            string errorLower = errorMessage.ToLowerInvariant();

            if (errorLower.Contains("ai classification") ||
                errorLower.Contains("classificazione ai"))
                defectMap[DefectType.AIClassification] = true;
            else if (errorLower.Contains("trapped paper") ||
                errorLower.Contains("paper trapped") ||
                errorLower.Contains("carta intrappolata") ||
                errorLower.Contains("carta in saldatura"))
                defectMap[DefectType.TrappedPaper] = true;
            else if (errorLower.Contains("bottom sealing") ||
                     errorLower.Contains("bottom seal") ||
                     errorLower.Contains("lower sealing") ||
                     errorLower.Contains("saldatura inferiore"))
                defectMap[DefectType.BottomSealing] = true;
            else if (errorLower.Contains("logo"))
                defectMap[DefectType.Logo] = true;
            else if (errorLower.Contains("print centering") || errorLower.Contains("centratura"))
                defectMap[DefectType.PrintCentering] = true;
            else if (errorLower.Contains("open flaps") || errorLower.Contains("alette"))
                defectMap[DefectType.OpenFlaps] = true;
            else if (errorLower.Contains("surface") || errorLower.Contains("superficie"))
                defectMap[DefectType.SurfaceCheck] = true;
            else if (errorLower.Contains("shape") && (errorLower.Contains("top") || errorLower.Contains("bottom")))
                defectMap[DefectType.ShapeTop] = true;
            else if (errorLower.Contains("shape") && errorLower.Contains("side"))
                defectMap[DefectType.ShapeSide] = true;
            else if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                     (errorLower.Contains("height") || errorLower.Contains("altezza")))
                defectMap[DefectType.ThreeDHeight] = true;
            else if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                     (errorLower.Contains("width") || errorLower.Contains("larghezza")))
                defectMap[DefectType.ThreeDWidth] = true;
            else if ((errorLower.Contains("3d") || errorLower.Contains("profilomet")) &&
                     (errorLower.Contains("length") || errorLower.Contains("lunghezza")))
                defectMap[DefectType.ThreeDLength] = true;
            else if (errorLower.Contains("height") || errorLower.Contains("altezza"))
                defectMap[DefectType.Height] = true;
            else if (errorLower.Contains("roll count") || errorLower.Contains("rollcount") || errorLower.Contains("rotoli"))
                defectMap[DefectType.SideRollCount] = true;
            else if (errorLower.Contains("sealing") || errorLower.Contains("saldatura"))
                defectMap[DefectType.SideSealing] = true;
            else if (errorLower.Contains("traceability") || errorLower.Contains("tracciabil"))
                defectMap[DefectType.FrontTraceability] = true;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Returns the name of the first alarm currently in triggered state,
        /// or an empty string when no alarm is active.
        /// Used by OPC UA health snapshot to surface the active alarm code to SCADA.
        /// </summary>
        public string GetFirstTriggeredAlarmCode()
        {
            lock (_lock)
            {
                return _alarms?.FirstOrDefault(a => a.IsTriggered)?.Name ?? string.Empty;
            }
        }

        public void ResetAllAlarms()
        {
            // Reset globale usato da manutenzione/configurazione: azzera stato
            // runtime e statistiche evento in memoria, poi lascia il salvataggio
            // esplicito alle chiamate che modificano configurazione persistente.
            _alarmManager.ResetAllAlarms();
            foreach (var alarm in _alarms)
            {
                alarm.IsTriggered = false;
                alarm.TriggerCount = 0;
            }
            _logger.Info("Tutti gli allarmi resettati");
        }

        public bool AcknowledgeAlarm(string alarmName, string acknowledgedBy = null)
        {
            EjectionAlarmConfig alarm;
            lock (_lock)
            {
                alarm = _alarms.FirstOrDefault(a => string.Equals(a.Name, alarmName, StringComparison.OrdinalIgnoreCase));
                if (alarm == null)
                {
                    _logger.Warn("ALARM_ACKNOWLEDGE_NOT_FOUND|alarm={0}", alarmName);
                    return false;
                }

                // ACK is intentionally more than closing the popup: it resets
                // the alarm runtime state inside EjectionAlarmManager so the
                // next product starts counting consecutive/buffer events again.
                var resetAlarm = _alarmManager.ResetAlarm(alarm.Name);
                alarm.IsTriggered = false;
                alarm.TriggerCount = resetAlarm?.TriggerCount ?? 0;
                alarm.LastTriggered = resetAlarm?.LastTriggered ?? DateTime.MinValue;
            }

            _logger.Info("ALARM_ACKNOWLEDGED|alarm={0}|user={1}", alarm.Name, acknowledgedBy ?? "unknown");
            StatusMessageChanged?.Invoke(this, $"Allarme '{alarm.Name}' riconosciuto");
            AlarmAcknowledged?.Invoke(this, new AlarmAcknowledgedEventArgs
            {
                Alarm = alarm,
                AcknowledgedAt = DateTime.Now,
                AcknowledgedBy = acknowledgedBy
            });
            return true;
        }

        public async Task SaveConfigurationAsync()
        {
            await _alarmManager.SaveConfigAsync();
            await _repository.SaveToXmlBackupAsync(_alarms);
            _logger.Info("Configurazione allarmi salvata");
        }

        public Dictionary<DefectType, AlarmStatistics> GetDefectStatistics()
        {
            return _alarmManager.GetDefectStatistics();
        }

        #endregion

        #region Event Handlers

        private void OnAlarmTriggered(object sender, AlarmTriggeredEventArgs e)
        {
            // Fan out the runtime alarm to MainWindow for physical output/popup
            // and also persist the event for later production diagnostics.
            AlarmTriggered?.Invoke(this, e);
            _logger.Warn($"ALLARME TRIGGERED: {e.Alarm.Name} - {e.Message}");
            _repository.LogAlarmEventAsync(e).SafeFireAndForget();
        }

        private void OnLogMessage(object sender, string message)
        {
            StatusMessageChanged?.Invoke(this, message);
        }

        #endregion

        public void Dispose()
        {
            try
            {
                _alarmManager.AlarmTriggered -= OnAlarmTriggered;
                _alarmManager.LogMessage -= OnLogMessage;
            }
            catch
            {
            }

            try
            {
                _alarmManager.Dispose();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Repository per la gestione degli allarmi su DB e XML
    /// </summary>
    public class AlarmCardRepository
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _backupFilePath;
        private readonly string _connectionString;

        public AlarmCardRepository()
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            _connectionString = DbConnectionStringHelper.Build(config.Host, config.Db, config.User, config.Password, config.port);

            _backupFilePath = Path.Combine(
                MainWindow.configManager.Config.Configuration.Recipe_Folder,
                "cfg",
                "alarm_cards_backup.xml"
            );

            Directory.CreateDirectory(Path.GetDirectoryName(_backupFilePath));
        }

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
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nella creazione tabella: {ex.Message}");
            }
        }

        public async Task InsertDefaultAlarmsIfEmptyAsync()
        {
            try
            {
                var existing = await GetAllAlarmsAsync();
                if (existing.Any()) return;

                var defaultAlarms = new List<EjectionAlarmConfig>
                {
                    new EjectionAlarmConfig { Name = "Alarm 1 - Consecutive (N)", AlarmType = AlarmType.ConsecutiveEvents, Threshold = 5, SignalType = SignalType.Blocking, SignalID = "DO0", OutputChannelId = "DO0", Enabled = false },
                    new EjectionAlarmConfig { Name = "Alarm 2 - Consecutive (M)", AlarmType = AlarmType.ConsecutiveEventsAlt, Threshold = 10, SignalType = SignalType.Blocking, SignalID = "DO0", OutputChannelId = "DO0", Enabled = false },
                    new EjectionAlarmConfig { Name = "Alarm 3 - Percentage", AlarmType = AlarmType.PercentageInBuffer, Threshold = 50, BufferSize = 10, SignalType = SignalType.Blocking, SignalID = "DO0", OutputChannelId = "DO0", Enabled = false },
                    new EjectionAlarmConfig { Name = "NC - Consecutive (N)", AlarmType = AlarmType.ConsecutiveEvents, Threshold = 5, SignalType = SignalType.NotBlocking, SignalID = "DO0", OutputChannelId = "DO0", Enabled = false },
                    new EjectionAlarmConfig { Name = "NC - Consecutive (M)", AlarmType = AlarmType.ConsecutiveEventsAlt, Threshold = 10, SignalType = SignalType.NotBlocking, SignalID = "DO0", OutputChannelId = "DO0", Enabled = false },
                    new EjectionAlarmConfig { Name = "NC - Percentage", AlarmType = AlarmType.PercentageInBuffer, Threshold = 50, BufferSize = 10, SignalType = SignalType.NotBlocking, SignalID = "DO0", OutputChannelId = "DO0", Enabled = false }
                };

                for (int i = 0; i < defaultAlarms.Count; i++)
                {
                    await InsertAlarmAsync(defaultAlarms[i], i);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inserimento allarmi default: {ex.Message}");
            }
        }

        public async Task<List<EjectionAlarmConfig>> GetAllAlarmsAsync()
        {
            var alarms = new List<EjectionAlarmConfig>();

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"
                        SELECT Name, AlarmType, Enabled, Threshold, BufferSize, 
                               SignalType, SignalID, DurationMs, MonitoredDefects
                        FROM cfg_alarm_cards 
                        WHERE IsDeleted = FALSE 
                        ORDER BY SortOrder ASC, Id ASC";

                    using (var cmd = new MySqlCommand(query, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string signalId = reader.IsDBNull(6) ? "" : reader.GetString(6);
                            var alarm = new EjectionAlarmConfig
                            {
                                Name = reader.GetString(0),
                                Enabled = reader.GetBoolean(2),
                                Threshold = reader.GetInt32(3),
                                BufferSize = reader.GetInt32(4),
                                SignalID = signalId,
                                OutputChannelId = signalId,
                                DurationMs = reader.GetInt32(7),
                                MonitoredDefects = ParseMonitoredDefects(reader.IsDBNull(8) ? null : reader.GetString(8))
                            };

                            if (Enum.TryParse<AlarmType>(reader.GetString(1), out var alarmType))
                                alarm.AlarmType = alarmType;

                            if (Enum.TryParse<SignalType>(reader.GetString(5), out var signalType))
                                alarm.SignalType = signalType;

                            alarms.Add(alarm);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel recupero allarmi: {ex.Message}");
            }

            return alarms;
        }

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
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inserimento: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateAlarmAsync(string originalName, EjectionAlarmConfig alarm)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"
                        UPDATE cfg_alarm_cards 
                        SET Name = @Name, AlarmType = @AlarmType, Enabled = @Enabled, 
                            Threshold = @Threshold, BufferSize = @BufferSize, 
                            SignalType = @SignalType, SignalID = @SignalID, 
                            DurationMs = @DurationMs, MonitoredDefects = @MonitoredDefects
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

                        return await cmd.ExecuteNonQueryAsync() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'aggiornamento: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteAlarmAsync(string name)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string query = "UPDATE cfg_alarm_cards SET IsDeleted = TRUE WHERE Name = @Name";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Name", name);
                        return await cmd.ExecuteNonQueryAsync() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'eliminazione: {ex.Message}");
                return false;
            }
        }

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
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel backup XML: {ex.Message}");
            }
        }

        public async Task<List<EjectionAlarmConfig>> LoadFromXmlBackupAsync()
        {
            var alarms = new List<EjectionAlarmConfig>();

            try
            {
                if (!File.Exists(_backupFilePath)) return alarms;

                var serializer = new XmlSerializer(typeof(AlarmCardsBackupConfig));
                using (var stream = new FileStream(_backupFilePath, FileMode.Open))
                {
                    var backupConfig = (AlarmCardsBackupConfig)serializer.Deserialize(stream);
                    alarms = backupConfig.Alarms ?? new List<EjectionAlarmConfig>();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel caricamento backup XML: {ex.Message}");
            }

            return alarms;
        }

        private List<DefectType> ParseMonitoredDefects(string json)
        {
            var defects = new List<DefectType>();
            if (string.IsNullOrEmpty(json)) return defects;

            try
            {
                var cleaned = json.Trim('[', ']').Replace("\"", "");
                var items = cleaned.Split(',');
                foreach (var item in items)
                {
                    if (Enum.TryParse<DefectType>(item.Trim(), out var defect))
                        defects.Add(defect);
                }
            }
            catch
            {
                // Fallback ai difetti di default
                defects = Enum.GetValues(typeof(DefectType)).Cast<DefectType>().ToList();
            }

            return defects;
        }

        private string SerializeMonitoredDefects(List<DefectType> defects)
        {
            if (defects == null || defects.Count == 0) return "[]";
            var items = defects.Select(d => $"\"{d}\"");
            return $"[{string.Join(",", items)}]";
        }

        public async Task LogAlarmEventAsync(AlarmTriggeredEventArgs e)
        {
            try
            {
                string defectsJson = null;
                if (e.DefectCounts != null && e.DefectCounts.Count > 0)
                {
                    var sb = new System.Text.StringBuilder("{");
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
                    string query = @"
                        INSERT INTO `alarm_events`
                            (`AlarmName`, `TriggerTime`, `DefectsJson`, `OutputChannel`, `DurationMs`)
                        VALUES
                            (@name, @ts, @defects, @channel, @duration)";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@name",    e.Alarm?.Name     ?? string.Empty);
                        cmd.Parameters.AddWithValue("@ts",      e.TriggerTime);
                        cmd.Parameters.AddWithValue("@defects", (object)defectsJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@channel", (object)e.Alarm?.OutputChannelId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@duration", e.Alarm?.PulseDurationMs ?? 0);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"ALARM_EVENT_LOG_FAILED|alarm={e.Alarm?.Name}|err={ex.Message}");
            }
        }
    }

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
