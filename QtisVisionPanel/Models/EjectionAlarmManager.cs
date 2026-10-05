using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace QtisVisionPanel.Models
{
    public class EjectionAlarmManager : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly EjectionAlarmsConfig _config;
        private readonly string _configFilePath;
        private readonly Dictionary<DefectType, Queue<bool>> _defectBuffers = new Dictionary<DefectType, Queue<bool>>();
        private readonly Dictionary<DefectType, int> _consecutiveCounters = new Dictionary<DefectType, int>();
        private readonly object _lock = new object();

        public event EventHandler<AlarmTriggeredEventArgs> AlarmTriggered;
        public event EventHandler<string> LogMessage;

        public EjectionAlarmManager(string configFilePath = null)
        {
            _configFilePath = configFilePath ?? Path.Combine(
         MainWindow.configManager.Config.Configuration.Recipe_Folder,
         "cfg",
         "ejection_alarms.xml");

            _logger.Info($"Percorso configurazione allarmi: {_configFilePath}");

            // Prima prova a riparare/creare il file
            //RepairOrCreateConfigFile();

            // Poi carica la configurazione
            _config = LoadConfig();
            InitializeBuffers();
        }
        public void RepairOrCreateConfigFile()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    // Prova a leggere e validare il file esistente
                    string content = File.ReadAllText(_configFilePath, Encoding.UTF8);

                    // Cerca di riparare problemi comuni
                    content = content.TrimStart('\uFEFF', '\u200B', '\u00A0');

                    // Verifica se inizia con dichiarazione XML valida
                    if (!content.StartsWith("<?xml"))
                    {
                        _logger.Warn("Il file XML non inizia con dichiarazione XML valida, sarà rigenerato");
                        File.Delete(_configFilePath);
                    }
                }

                // Se il file non esiste o è stato eliminato, creane uno nuovo
                if (!File.Exists(_configFilePath))
                {
                    _logger.Info($"Creazione nuovo file di configurazione: {_configFilePath}");

                    // Crea una configurazione di default
                    var defaultConfig = new EjectionAlarmsConfig();

                    // Salva la configurazione
                    SaveConfigAsync().Wait(1000);

                    _logger.Info("File di configurazione rigenerato con successo");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore durante la riparazione del file di configurazione: {ex.Message}");
            }
        }
        private EjectionAlarmsConfig LoadConfig()
        {
            try
            {
                _logger.Info($"Caricamento configurazione da: {_configFilePath}");

                if (File.Exists(_configFilePath))
                {
                    // Leggi il file come byte array per vedere esattamente cosa c'è
                    byte[] fileBytes = File.ReadAllBytes(_configFilePath);
                    _logger.Info($"Dimensione file: {fileBytes.Length} byte");

                    // Verifica i primi 10 byte per BOM o caratteri strani
                    string hexStart = BitConverter.ToString(fileBytes, 0, Math.Min(20, fileBytes.Length));
                    _logger.Info($"Primi byte (esadecimale): {hexStart}");

                    // Converti in stringa gestendo vari encoding
                    string xmlContent;

                    // Controlla se inizia con BOM UTF-8
                    if (fileBytes.Length >= 3 && fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF)
                    {
                        _logger.Info("File ha BOM UTF-8");
                        xmlContent = Encoding.UTF8.GetString(fileBytes, 3, fileBytes.Length - 3);
                    }
                    else
                    {
                        // Prova con UTF-8 senza BOM
                        xmlContent = Encoding.UTF8.GetString(fileBytes);
                    }

                    // Rimuovi eventuali caratteri di controllo all'inizio
                    xmlContent = xmlContent.TrimStart('\uFEFF', '\u200B', '\u00A0');

                    // Log il contenuto completo per debug
                    _logger.Debug($"Contenuto XML completo ({xmlContent.Length} caratteri):");
                    _logger.Debug(xmlContent);

                    // Crea un XmlReader con impostazioni più permissive per il debug
                    var settings = new XmlReaderSettings
                    {
                        IgnoreWhitespace = true,
                        IgnoreComments = true,
                        CheckCharacters = false, // Disabilita controllo caratteri per debug
                        DtdProcessing = DtdProcessing.Ignore
                    };

                    using (var stringReader = new StringReader(xmlContent))
                    using (var xmlReader = XmlReader.Create(stringReader, settings))
                    {
                        var serializer = new XmlSerializer(typeof(EjectionAlarmsConfig));
                        var config = (EjectionAlarmsConfig)serializer.Deserialize(xmlReader);
                        _logger.Info("Configurazione caricata con successo");
                        return config;
                    }
                }
                else
                {
                    _logger.Warn($"File di configurazione non trovato: {_configFilePath}");
                    _logger.Info("Creazione configurazione predefinita");
                }
            }
            catch (XmlException xmlEx)
            {
                _logger.Error($"Errore XML specifico: {xmlEx.Message}");
                _logger.Error($"Linea: {xmlEx.LineNumber}, Posizione: {xmlEx.LinePosition}");

                // Per debugging, cerca il carattere problematico
                if (File.Exists(_configFilePath))
                {
                    string content = File.ReadAllText(_configFilePath, Encoding.UTF8);
                    if (xmlEx.LineNumber > 0 && xmlEx.LinePosition > 0)
                    {
                        string[] lines = content.Split('\n');
                        if (xmlEx.LineNumber - 1 < lines.Length)
                        {
                            string problemLine = lines[xmlEx.LineNumber - 1];
                            _logger.Error($"Linea problematica: {problemLine}");
                            _logger.Error($"Carattere nella posizione {xmlEx.LinePosition}: '{problemLine[xmlEx.LinePosition - 1]}' (codice: {(int)problemLine[xmlEx.LinePosition - 1]})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore generico nel caricamento configurazione: {ex.Message}");
                _logger.Error($"Stack trace: {ex.StackTrace}");

                // Se c'è un problema con il file XML, creane uno nuovo di default
                _logger.Info("Creazione nuova configurazione di default");
            }

            // Se arriva qui, restituisci una configurazione vuota
            return new EjectionAlarmsConfig();
        }
        public void CreateTestXmlFile()
        {
            try
            {
                string testPath = Path.Combine(Path.GetDirectoryName(_configFilePath), "test_config.xml");

                var testConfig = new EjectionAlarmsConfig();

                var serializer = new XmlSerializer(typeof(EjectionAlarmsConfig));
                var settings = new XmlWriterSettings
                {
                    Encoding = Encoding.UTF8,
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\n"
                };

                using (var writer = XmlWriter.Create(testPath, settings))
                {
                    serializer.Serialize(writer, testConfig);
                }

                _logger.Info($"File di test creato: {testPath}");

                // Prova a caricare il file di test
                using (var reader = XmlReader.Create(testPath))
                {
                    var loadedConfig = (EjectionAlarmsConfig)serializer.Deserialize(reader);
                    _logger.Info("File di test caricato con successo!");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nella creazione del file di test: {ex.Message}");
            }
        }
        public async Task SaveConfigAsync()
        {
            try
            {
                // Crea la directory se non esiste
                var directory = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Info($"Creata directory: {directory}");
                }

                var serializer = new XmlSerializer(typeof(EjectionAlarmsConfig));
                var settings = new XmlWriterSettings
                {
                    Encoding = Encoding.UTF8,
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\n"
                };

                // Usa XmlWriter.Create con le impostazioni
                using (var writer = XmlWriter.Create(_configFilePath, settings))
                {
                    serializer.Serialize(writer, _config);
                }

                _logger.Info($"Configurazione salvata in: {_configFilePath}");
                LogMessage?.Invoke(this, "Configurazione allarmi salvata");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel salvataggio configurazione: {ex.Message}");
            }
        }


        private void InitializeBuffers()
        {
            // Non serve più assegnare i dizionari qui, solo svuotarli e reinizializzarli
            _defectBuffers.Clear();
            _consecutiveCounters.Clear();

            foreach (DefectType defect in Enum.GetValues(typeof(DefectType)))
            {
                _defectBuffers[defect] = new Queue<bool>();
                _consecutiveCounters[defect] = 0;
            }
        }

        public void ProcessInspectionResult(bool hasDefects, Dictionary<DefectType, bool> defectMap)
        {
            lock (_lock)
            {
                // Aggiorna buffer e contatori per ogni difetto
                foreach (var defect in defectMap)
                {
                    UpdateDefectBuffer(defect.Key, defect.Value);
                    UpdateConsecutiveCounter(defect.Key, defect.Value);
                }

                // Verifica tutti gli allarmi configurati
                foreach (var alarm in _config.Alarms.Where(a => a.Enabled))
                {
                    CheckAlarm(alarm, defectMap);
                }
            }
        }

        private void UpdateDefectBuffer(DefectType defect, bool hasDefect)
        {
            var buffer = _defectBuffers[defect];

            // Trova la dimensione massima del buffer richiesta dagli allarmi
            var relevantAlarms = _config.Alarms
                .Where(a => a.Enabled &&
                           a.AlarmType == AlarmType.PercentageInBuffer &&
                           a.MonitoredDefects.Contains(defect))
                .ToList();

            // Se non ci sono allarmi rilevanti, esci
            if (!relevantAlarms.Any())
                return;

            int maxBufferSize = relevantAlarms.Max(a => a.BufferSize);

            if (maxBufferSize > 0)
            {
                buffer.Enqueue(hasDefect);

                // Mantieni la dimensione del buffer
                while (buffer.Count > maxBufferSize)
                {
                    buffer.Dequeue();
                }
            }
        }

        private void UpdateConsecutiveCounter(DefectType defect, bool hasDefect)
        {
            if (hasDefect)
            {
                _consecutiveCounters[defect]++;
            }
            else
            {
                _consecutiveCounters[defect] = 0;
            }
        }

        private void CheckAlarm(EjectionAlarmConfig alarm, Dictionary<DefectType, bool> defectMap)
        {
            // Consecutive alarms and percentage-buffer alarms use the same
            // monitored defect list but different memory: consecutive counters
            // reset on GOOD, buffers keep the last N product outcomes.
            bool shouldTrigger = false;
            string reason = string.Empty;
            Dictionary<DefectType, int> defectCounts = new Dictionary<DefectType, int>();

            switch (alarm.AlarmType)
            {
                case AlarmType.ConsecutiveEvents:
                    foreach (var defect in alarm.MonitoredDefects)
                    {
                        if (_consecutiveCounters.ContainsKey(defect) &&
                            _consecutiveCounters[defect] >= alarm.Threshold)
                        {
                            shouldTrigger = true;
                            defectCounts[defect] = _consecutiveCounters[defect];
                            reason = $"{defect} consecutive defects: {_consecutiveCounters[defect]}";
                            break;
                        }
                    }
                    break;

                case AlarmType.ConsecutiveEventsAlt:
                    // Stessa logica del primo ma con threshold diverso
                    foreach (var defect in alarm.MonitoredDefects)
                    {
                        if (_consecutiveCounters.ContainsKey(defect) &&
                            _consecutiveCounters[defect] >= alarm.Threshold)
                        {
                            shouldTrigger = true;
                            defectCounts[defect] = _consecutiveCounters[defect];
                            reason = $"{defect} consecutive defects (alt): {_consecutiveCounters[defect]}";
                            break;
                        }
                    }
                    break;

                case AlarmType.PercentageInBuffer:
                    foreach (var defect in alarm.MonitoredDefects)
                    {
                        var buffer = _defectBuffers[defect];
                        if (buffer.Count >= alarm.BufferSize)
                        {
                            int defectCount = buffer.Count(x => x);
                            double percentage = (defectCount * 100.0) / alarm.BufferSize;

                            if (percentage >= alarm.Threshold)
                            {
                                shouldTrigger = true;
                                defectCounts[defect] = defectCount;
                                reason = $"{defect} defect percentage: {percentage:F1}% ({defectCount}/{alarm.BufferSize})";
                                break;
                            }
                        }
                    }
                    break;
            }

            if (shouldTrigger)
            {
                TriggerAlarm(alarm, defectCounts, reason);
            }
        }

        private void TriggerAlarm(EjectionAlarmConfig alarm, Dictionary<DefectType, int> defectCounts, string reason)
        {
            AlarmTriggeredEventArgs eventArgs;
            lock (_lock)
            {
                // Guard atomico: non triggerare se già attivo o se il precedente trigger è avvenuto
                // meno di 500ms fa (previene doppi impulsi per risultati in rapida successione)
                if (alarm.IsTriggered) return;
                if ((DateTime.Now - alarm.LastTriggered).TotalMilliseconds < 500) return;

                alarm.IsTriggered = true;
                alarm.LastTriggered = DateTime.Now;
                alarm.TriggerCount++;

                string message = $"Ejection Alarm Triggered: {alarm.Name} - {reason}";
                _logger.Warn("ALARM_TRIGGERED|name={0}|reason={1}|count={2}", alarm.Name, reason, alarm.TriggerCount);
                LogMessage?.Invoke(this, message);

                eventArgs = new AlarmTriggeredEventArgs
                {
                    Alarm = alarm,
                    DefectCounts = defectCounts,
                    TriggerTime = alarm.LastTriggered,
                    Message = message
                };
            }

            // Solleva evento fuori dal lock per evitare deadlock nei subscriber
            AlarmTriggered?.Invoke(this, eventArgs);

            // TODO: Attivare output digitale se configurato
            // if (!string.IsNullOrEmpty(alarm.SignalID) && alarm.SignalID != "Blocking")
            // {
            //     // Attiva output digitale tramite IIODeviceManager
            // }
        }

        public void ResetAlarm(EjectionAlarmConfig alarm)
        {
            if (alarm == null)
            {
                return;
            }

            lock (_lock)
            {
                var managedAlarm = _config.Alarms.FirstOrDefault(a => string.Equals(a.Name, alarm.Name, StringComparison.OrdinalIgnoreCase));
                if (managedAlarm != null)
                {
                    ResetAlarmRuntimeState(managedAlarm);
                }

                if (!ReferenceEquals(alarm, managedAlarm))
                {
                    alarm.IsTriggered = false;
                    alarm.TriggerCount = managedAlarm?.TriggerCount ?? 0;
                    alarm.LastTriggered = managedAlarm?.LastTriggered ?? DateTime.MinValue;
                }
            }
        }

        public EjectionAlarmConfig ResetAlarm(string alarmName)
        {
            if (string.IsNullOrWhiteSpace(alarmName))
            {
                return null;
            }

            lock (_lock)
            {
                var alarm = _config.Alarms.FirstOrDefault(a => string.Equals(a.Name, alarmName, StringComparison.OrdinalIgnoreCase));
                if (alarm == null)
                {
                    return null;
                }

                ResetAlarmRuntimeState(alarm);
                return alarm;
            }
        }

        private void ResetAlarmRuntimeState(EjectionAlarmConfig alarm)
        {
            // ACK must reset both the visible alarm flag and the internal
            // evidence that caused the trigger. Without clearing these counters
            // the same alarm can reappear immediately on the next defect.
            alarm.IsTriggered = false;
            alarm.TriggerCount = 0;
            alarm.LastTriggered = DateTime.MinValue;

            var defectsToReset = alarm.MonitoredDefects != null && alarm.MonitoredDefects.Count > 0
                ? alarm.MonitoredDefects.Distinct().ToList()
                : Enum.GetValues(typeof(DefectType)).Cast<DefectType>().ToList();

            foreach (var defect in defectsToReset)
            {
                if (_consecutiveCounters.ContainsKey(defect))
                {
                    _consecutiveCounters[defect] = 0;
                }

                if (_defectBuffers.ContainsKey(defect))
                {
                    _defectBuffers[defect].Clear();
                }
            }

            _logger.Info(
                "ALARM_RUNTIME_STATE_RESET|name={0}|type={1}|defects={2}",
                alarm.Name,
                alarm.AlarmType,
                string.Join(",", defectsToReset));
        }

        public void ResetAllAlarms()
        {
            lock (_lock)
            {
                // Full runtime reset: all alarm latches and all defect memories.
                foreach (var alarm in _config.Alarms)
                {
                    alarm.IsTriggered = false;
                    alarm.TriggerCount = 0;
                }

                // Resetta anche i contatori
                foreach (DefectType defect in Enum.GetValues(typeof(DefectType)))
                {
                    _consecutiveCounters[defect] = 0;
                    _defectBuffers[defect].Clear();
                }
            }
        }

        public List<EjectionAlarmConfig> GetAlarms()
        {
            return _config.Alarms;
        }

        public void AddAlarm(EjectionAlarmConfig alarm)
        {
            lock (_lock)
            {
                _config.Alarms.Add(alarm);
            }
        }

        public void RemoveAlarm(EjectionAlarmConfig alarm)
        {
            lock (_lock)
            {
                _config.Alarms.Remove(alarm);
            }
        }

        public void UpdateAlarm(EjectionAlarmConfig original, EjectionAlarmConfig updated)
        {
            lock (_lock)
            {
                int index = _config.Alarms.IndexOf(original);
                if (index >= 0)
                {
                    _config.Alarms[index] = updated;
                }
            }
        }

        public Dictionary<DefectType, AlarmStatistics> GetDefectStatistics()
        {
            var stats = new Dictionary<DefectType, AlarmStatistics>();

            lock (_lock)
            {
                foreach (DefectType defect in Enum.GetValues(typeof(DefectType)))
                {
                    stats[defect] = new AlarmStatistics
                    {
                        ConsecutiveCount = _consecutiveCounters[defect],
                        BufferSize = _defectBuffers[defect].Count,
                        DefectPercentage = _defectBuffers[defect].Count > 0 ?
                            (_defectBuffers[defect].Count(x => x) * 100.0 / _defectBuffers[defect].Count) : 0
                    };
                }
            }

            return stats;
        }

        public void Dispose()
        {
            SaveConfigAsync().Wait(1000);
        }
    }

    public class AlarmStatistics
    {
        public int ConsecutiveCount { get; set; }
        public int BufferSize { get; set; }
        public double DefectPercentage { get; set; }
    }
}
