using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using NLog;
using QtisVisionPanel.Database.ProductionRecord;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace QtisVisionPanel.Database
{
    /// <summary>
    /// Runtime counter aggregator for production totals and defect totals.
    ///
    /// The numerical counters are historical for the current session/shift,
    /// while _defectErrorMessages represents only the latest active defect
    /// reasons shown in the operator UI. This distinction is important: a GOOD
    /// piece must clear old red reason text without resetting accumulated
    /// counters.
    /// </summary>
    public class CounterManager : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _backupFilePath;
        private readonly InserdataInDb _dbService;
        private readonly object _lock = new object();
        private bool _disposed = false;
        private Dictionary<string, string> _defectErrorMessages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _defectsCountedThisCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _shiftStartTime = DateTime.Now;
        private string _currentShiftName = string.Empty;
        private string _currentShiftOperator = string.Empty;

        private readonly Dictionary<string, string> _defectTypeMapping = new Dictionary<string, string>
    {
        { "Logo", "Logo" },
        { "PrintCentering", "PrintCentering" },
        { "OpenFlaps", "OpenFlaps" },
        { "SurfaceCheck", "SurfaceCheck" },
        { "Height", "Height" },
        { "SideSealing", "SideSealing" },
        { "SideRollCount", "SideRollCount" },
        { "ShapeTop", "ShapeTop" },
        { "ShapeSide", "ShapeSide" },
        { "FrontTraceability", "FrontTraceability" },
        { "ThreeDHeight", "ThreeDHeight" },
        { "ThreeDWidth", "ThreeDWidth" },
        { "ThreeDLength", "ThreeDLength" },
        { "BottomSealing", "BottomSealing" },
        { "TrappedPaper", "TrappedPaper" },
        { "AIClassification", "AIClassification" }
    };
        public readonly CounterDbFlusher _flusher;
        // Evento per aggiornamento UI
        public event Action CountersUpdated;

        // Contatori principali - accessibili direttamente
        private long _total = 0;
        private long _good = 0;
        private long _noGood = 0;
        private long _unclassified = 0;
        private long _logoDefects = 0;
        private long _printCenteringDefects = 0;
        private long _openFlapsDefects = 0;
        private long _surfaceCheckDefects = 0;
        private long _heightDefects = 0;
        private long _sideSealingDefects = 0;
        private long _sideRollCountDefects = 0;
        private long _shapeTopDefects = 0;
        private long _shapeSideDefects = 0;
        private long _frontTraceabilityDefects = 0;
        private long _threeDHeightDefects = 0;
        private long _threeDWidthDefects = 0;
        private long _threeDLengthDefects = 0;
        private long _bottomSealingDefects = 0;
        private long _trappedPaperDefects = 0;
        private long _aiClassificationDefects = 0;
        private int _backupWriteRequested;
        private int _backupWriteRunning;
        private int _uiUpdateQueued;

        /// <summary>
        /// Stores the last operator-visible reason for a defect type.
        /// Validators may use different naming conventions, so the key is
        /// normalized before it reaches the UI.
        /// </summary>
        public void SetErrorMessage(string defectType, string errorMessage)
        {
            lock (_lock)
            {
                string uiDefectType = NormalizeDefectType(defectType) ?? MapDefectType(defectType);
                if (!string.IsNullOrEmpty(uiDefectType))
                {
                    _defectErrorMessages[uiDefectType] = errorMessage;
                    _logger.Debug($"Impostato messaggio di errore per {uiDefectType}: {errorMessage}");
                }
            }
        }

        public string GetLastErrorMessage(string defectType)
        {
            lock (_lock)
            {
                string uiDefectType = NormalizeDefectType(defectType) ?? MapDefectType(defectType) ?? defectType;
                if (!string.IsNullOrWhiteSpace(uiDefectType) &&
                    _defectErrorMessages.TryGetValue(uiDefectType, out string message))
                {
                    return message;
                }

                return null;
            }
        }

        public void BeginInspectionCycle()
        {
            // Clear stale reason text before validators evaluate the new piece.
            // If the current piece is defective, validators will write fresh
            // messages during the same cycle.
            bool cleared;
            lock (_lock)
            {
                _defectsCountedThisCycle.Clear();
                cleared = ClearDefectMessagesInternal("inizio nuovo ciclo ispezione");
            }

            if (cleared)
            {
                QueueCountersUpdated();
            }
        }

        private bool ClearDefectMessagesInternal(string reason)
        {
            if (_defectErrorMessages.Count == 0)
            {
                return false;
            }

            _defectErrorMessages.Clear();
            _logger.Debug($"Messaggi ultimo difetto cancellati: {reason}.");
            return true;
        }

        public long Total
        {
            get => _total;
            private set
            {
                _total = value;
                OnPropertyChanged("Total");
            }
        }

        public long Good
        {
            get => _good;
            private set
            {
                _good = value;
                OnPropertyChanged("Good");
            }
        }

        public long NoGood
        {
            get => _noGood;
            private set
            {
                _noGood = value;
                OnPropertyChanged("NoGood");
            }
        }

        public long Unclassified
        {
            get => _unclassified;
            private set
            {
                _unclassified = value;
                OnPropertyChanged("Unclassified");
            }
        }

        public long LogoDefects
        {
            get => _logoDefects;
            private set
            {
                _logoDefects = value;
                OnPropertyChanged("LogoDefects");
            }
        }

        public long PrintCenteringDefects
        {
            get => _printCenteringDefects;
            private set
            {
                _printCenteringDefects = value;
                OnPropertyChanged("PrintCenteringDefects");
            }
        }

        public long OpenFlapsDefects
        {
            get => _openFlapsDefects;
            private set
            {
                _openFlapsDefects = value;
                OnPropertyChanged("OpenFlapsDefects");
            }
        }

        public long SurfaceCheckDefects
        {
            get => _surfaceCheckDefects;
            private set
            {
                _surfaceCheckDefects = value;
                OnPropertyChanged("SurfaceCheckDefects");
            }
        }

        public long AIClassificationDefects
        {
            get => _aiClassificationDefects;
            private set
            {
                _aiClassificationDefects = value;
                OnPropertyChanged("AIClassificationDefects");
            }
        }

        public long HeightDefects
        {
            get => _heightDefects;
            private set
            {
                _heightDefects = value;
                OnPropertyChanged("HeightDefects");
            }
        }

        public long SideSealingDefects
        {
            get => _sideSealingDefects;
            private set
            {
                _sideSealingDefects = value;
                OnPropertyChanged("SideSealingDefects");
            }
        }

        public long SideRollCountDefects
        {
            get => _sideRollCountDefects;
            private set
            {
                _sideRollCountDefects = value;
                OnPropertyChanged("SideRollCountDefects");
            }
        }

        public long ShapeTopDefects
        {
            get => _shapeTopDefects;
            private set
            {
                _shapeTopDefects = value;
                OnPropertyChanged("ShapeTopDefects");
            }
        }

        public long ShapeSideDefects
        {
            get => _shapeSideDefects;
            private set
            {
                _shapeSideDefects = value;
                OnPropertyChanged("ShapeSideDefects");
            }
        }

        public long FrontTraceabilityDefects
        {
            get => _frontTraceabilityDefects;
            private set
            {
                _frontTraceabilityDefects = value;
                OnPropertyChanged("FrontTraceabilityDefects");
            }
        }

        public long ThreeDHeightDefects
        {
            get => _threeDHeightDefects;
            private set
            {
                _threeDHeightDefects = value;
                OnPropertyChanged("ThreeDHeightDefects");
            }
        }

        public long ThreeDWidthDefects
        {
            get => _threeDWidthDefects;
            private set
            {
                _threeDWidthDefects = value;
                OnPropertyChanged("ThreeDWidthDefects");
            }
        }

        public long ThreeDLengthDefects
        {
            get => _threeDLengthDefects;
            private set
            {
                _threeDLengthDefects = value;
                OnPropertyChanged("ThreeDLengthDefects");
            }
        }

        public long BottomSealingDefects
        {
            get => _bottomSealingDefects;
            private set
            {
                _bottomSealingDefects = value;
                OnPropertyChanged("BottomSealingDefects");
            }
        }

        public long TrappedPaperDefects
        {
            get => _trappedPaperDefects;
            private set
            {
                _trappedPaperDefects = value;
                OnPropertyChanged("TrappedPaperDefects");
            }
        }

        private static string ResolveBackupRootPath()
        {
            try
            {
                var configuredPath = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder;
                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    return configuredPath;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Recipe_Folder non disponibile durante l'avvio: {ex.Message}");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return localAppData;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public CounterManager()
        {
            // Percorso per il file di backup locale
            string appDataPath;
            try
            {
                var backupRootPath = ResolveBackupRootPath();
                appDataPath = Path.Combine(backupRootPath, "QtisVisionPanel");
                Directory.CreateDirectory(appDataPath);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Percorso backup non valido, uso fallback locale: {ex.Message}");
                appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QtisVisionPanel");
                Directory.CreateDirectory(appDataPath);
            }

            _backupFilePath = Path.Combine(appDataPath, "counters_backup.json");

            // Inizializza servizio database
            _dbService = new InserdataInDb();

            _flusher = new CounterDbFlusher(
                    TimeSpan.FromSeconds(1),     // <-- per 180 ppm consiglio 1s
                    snapshotProvider: GetAllCounters,
                    saveFunc: counters => _dbService.SaveCountersAsync(counters)
                );


            _logger.Info($"CounterManager inizializzato. Backup file: {_backupFilePath}");

            // Carica contatori all'avvio
            LoadCounters();
        }

        #region Metodi Pubblici

        // Metodo principale per processare un'ispezione
        // Aggiungi questo metodo helper per mappare i tipi di difetto
        private string MapDefectType(string defectType)
        {
            switch (defectType?.Trim().ToLowerInvariant())
            {
                case "logo":
                    return "Logo";
                case "print_centering":
                case "printcentering":
                    return "PrintCentering";
                case "openflaps":
                    return "OpenFlaps";
                case "surfacecheck":
                    return "SurfaceCheck";
                case "aiclassification":
                case "ai_classification":
                case "classification":
                case "classify":
                    return "AIClassification";
                case "height":
                    return "Height";
                case "side_sealing":
                case "sidesealing":
                    return "SideSealing";
                case "side_roll_count":
                case "siderollcount":
                case "rollcount":
                case "rotoli":
                    return "SideRollCount";
                case "shapetop":
                    return "ShapeTop";
                case "shapeside":
                    return "ShapeSide";
                case "fronttraceability":
                case "traceability":
                    return "FrontTraceability";
                case "threedheight":
                case "3dheight":
                    return "ThreeDHeight";
                case "threedwidth":
                case "3dwidth":
                    return "ThreeDWidth";
                case "threedlength":
                case "3dlength":
                    return "ThreeDLength";
                case "bottom_sealing":
                case "bottomsealing":
                case "bottom seal":
                case "saldaturainferiore":
                case "saldatura inferiore":
                    return "BottomSealing";
                case "trapped_paper":
                case "trappedpaper":
                case "papertrapped":
                case "carta_intrappolata":
                case "cartaintrappolata":
                case "carta intrappolata":
                case "carta in saldatura":
                    return "TrappedPaper";
                default:
                    return null;
            }
        }
        public Task ProcessInspectionAsync(bool isCompliant, Dictionary<string, bool> defects = null)
        {
            return ProcessInspectionAsync(isCompliant ? 1 : 0, defects);
        }

        public Task ProcessInspectionAsync(int outcomeCode, Dictionary<string, bool> defects = null)
        {
            _logger.Debug($"ProcessInspectionAsync chiamato - Outcome: {outcomeCode}");

            try
            {
                lock (_lock)
                {
                    // Incrementa contatori principali
                    Total++;

                    if (outcomeCode == 1)
                    {
                        Good++;
                        // A conforming piece proves previous defect reasons are
                        // stale. Keep historical counts, clear only message text.
                        ClearDefectMessagesInternal("ispezione conforme");

                        _logger.Info($"Inspection passed - Total: {Total}, Good: {Good}");
                    }
                    else if (outcomeCode == 3)
                    {
                        Unclassified++;
                        _logger.Warn($"Inspection unclassified - Total: {Total}, Unclassified: {Unclassified}");
                    }
                    else
                    {
                        NoGood++;
                        _logger.Info($"Inspection failed - Total: {Total}, NoGood: {NoGood}");
                    }

                    // I contatori per-difetto sono gia incrementati da IToolBlockValidator
                    // via IncrementDefectAsync(). Non chiamare UpdateDefectCounters qui
                    // per evitare il doppio conteggio.
                }

                // Coalesce backup writes. The old per-piece synchronous disk write
                // delayed result publication and serial inspection queue draining.
                _flusher.MarkDirty();
                QueueBackupSave();

                // Notifica UI (BeginInvoke: non blocca il thread orchestrator durante burst)
                QueueCountersUpdated();

                _logger.Debug($"Contatori aggiornati: Total={Total}, Good={Good}, NoGood={NoGood}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'aggiornamento dei contatori: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        // Metodo per incrementare un difetto specifico
        public async Task IncrementDefectAsync(string defectType, string errorMessage = null)
        {
            MainWindow.logger.Debug($"CounterManager.IncrementDefectAsync chiamato - Difetto: {defectType}");

            try
            {
                string uiType = NormalizeDefectType(defectType);
                if (uiType == null)
                {
                    _logger.Warn($"Tipo difetto non riconosciuto: {defectType}");
                    return;
                }
                lock (_lock)
                {
                    bool countThisDefect = _defectsCountedThisCycle.Add(uiType);
                    if (countThisDefect)
                    {
                        switch (uiType)
                        {
                            case "Logo":
                                LogoDefects++;
                                MainWindow._produzioneRecord.NcLoghiImmagini = 0;
                                _logger.Debug($"LogoDefects incrementato a {LogoDefects}");
                                break;
                            case "PrintCentering":
                                PrintCenteringDefects++;
                                MainWindow._produzioneRecord.NcCentraturaLogo = 0;
                                break;
                            case "OpenFlaps":
                                OpenFlapsDefects++;
                                MainWindow._produzioneRecord.NcAletteAperte = 0;
                                break;
                            case "SurfaceCheck":
                                SurfaceCheckDefects++;
                                MainWindow._produzioneRecord.NCSurfaceCheck = 0;
                                break;
                            case "AIClassification":
                                AIClassificationDefects++;
                                break;
                            case "Height":
                                HeightDefects++;
                                MainWindow._produzioneRecord.NcHeigth = 0;
                                break;
                            case "SideSealing":
                                SideSealingDefects++;
                                MainWindow._produzioneRecord.NcSaldaturaLaterale = 0;
                                break;
                            case "SideRollCount":
                                SideRollCountDefects++;
                                break;
                            case "ShapeTop":
                                ShapeTopDefects++;
                                MainWindow._produzioneRecord.NcShapeTop = 0;
                                break;
                            case "ShapeSide":
                                ShapeSideDefects++;
                                MainWindow._produzioneRecord.NcShapeBottom = 0;
                                break;
                            case "FrontTraceability":
                                FrontTraceabilityDefects++;
                                MainWindow._produzioneRecord.NCTraceability = 0;
                                break;
                            case "ThreeDHeight":
                                ThreeDHeightDefects++;
                                MainWindow._produzioneRecord.NCThreeDHeight = 0;
                                break;
                            case "ThreeDWidth":
                                ThreeDWidthDefects++;
                                MainWindow._produzioneRecord.NCThreeDWidth = 0;
                                break;
                            case "ThreeDLength":
                                ThreeDLengthDefects++;
                                MainWindow._produzioneRecord.NCThreeDLength = 0;
                                break;
                            case "BottomSealing":
                                BottomSealingDefects++;
                                MainWindow._produzioneRecord.NCBottomSealing = 0;
                                break;
                            case "TrappedPaper":
                                TrappedPaperDefects++;
                                MainWindow._produzioneRecord.NCTrappedPaper = 0;
                                break;
                            default:
                                _logger.Warn($"Tipo difetto non riconosciuto: {defectType}");
                                break;
                        }
                    }
                    else
                    {
                        _logger.Debug($"Difetto {uiType} gia conteggiato nel ciclo ispezione corrente; aggiorno solo il messaggio.");
                    }

                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        // Piu' viste possono rilevare lo STESSO tipo di difetto sullo stesso
                        // pezzo: con AIClassification, ad esempio, Top puo' dare "pieghe top" e
                        // Rear "bad Trasversal sealing". Il contatore resta giustamente a 1 (il
                        // pezzo difettoso e' uno), ma prima il messaggio veniva sovrascritto e
                        // in descrizione sopravviveva solo l'ultima vista, nascondendo le altre.
                        // Qui i messaggi del ciclo vengono accodati, saltando i duplicati esatti.
                        if (_defectErrorMessages.TryGetValue(uiType, out string existingMessage) &&
                            !string.IsNullOrEmpty(existingMessage) &&
                            !countThisDefect)
                        {
                            bool alreadyPresent = false;
                            foreach (string part in existingMessage.Split(
                                         new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (string.Equals(part.Trim(), errorMessage.Trim(),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    alreadyPresent = true;
                                    break;
                                }
                            }

                            _defectErrorMessages[uiType] = alreadyPresent
                                ? existingMessage
                                : existingMessage + " | " + errorMessage;
                        }
                        else
                        {
                            _defectErrorMessages[uiType] = errorMessage;
                        }
                    }
                }
                _flusher.MarkDirty();     // DB lo fara il timer
                // await SaveCountersToDatabase();
                QueueBackupSave();

                QueueCountersUpdated();
               
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'incremento del difetto {defectType}: {ex.Message}");
            }
        }

        // Metodo per resettare tutti i contatori
        public async Task ResetAllCountersAsync()
        {
            _logger.Info("ResetAllCountersAsync chiamato");

            try
            {
                lock (_lock)
                {
                    Total = 0;
                    Good = 0;
                    NoGood = 0;
                    Unclassified = 0;
                    LogoDefects = 0;
                    PrintCenteringDefects = 0;
                    OpenFlapsDefects = 0;
                    SurfaceCheckDefects = 0;
                    HeightDefects = 0;
                    SideSealingDefects = 0;
                    SideRollCountDefects = 0;
                    ShapeTopDefects = 0;
                    ShapeSideDefects = 0;
                    FrontTraceabilityDefects = 0;
                    ThreeDHeightDefects = 0;
                    ThreeDWidthDefects = 0;
                    ThreeDLengthDefects = 0;
                    BottomSealingDefects = 0;
                    TrappedPaperDefects = 0;
                    AIClassificationDefects = 0;

                    // Resetta anche i messaggi di errore
                    _defectErrorMessages.Clear();
                    _defectsCountedThisCycle.Clear();
                }
               
                // await SaveCountersToDatabase();
                _flusher.MarkDirty();     // DB lo fara il timer
                SaveCountersToBackup();

                QueueCountersUpdated();

                _logger.Info("Tutti i contatori sono stati resettati");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel reset dei contatori: {ex.Message}");
            }
        }

        /// <summary>
        /// Pieces per minute based on elapsed shift time and total counter.
        /// Returns 0 until at least 10 seconds have elapsed to avoid spikes.
        /// </summary>
        /// <summary>Name of the active production shift (e.g. "Mattino", "Pomeriggio", "Notte").</summary>
        public string CurrentShiftName => _currentShiftName;

        /// <summary>Operator who started the current shift, if recorded.</summary>
        public string CurrentShiftOperator => _currentShiftOperator;

        /// <summary>Time the current shift (or application start) began.</summary>
        public DateTime ShiftStartTime => _shiftStartTime;

        public double GetCurrentPpm()
        {
            double elapsedMinutes = (DateTime.Now - _shiftStartTime).TotalMinutes;
            return elapsedMinutes < (10.0 / 60.0) ? 0.0 : Math.Round(_total / elapsedMinutes, 1);
        }

        /// <summary>
        /// Returns a snapshot of the current shift for reporting.
        /// </summary>
        public ShiftSummary GetShiftSummary()
        {
            lock (_lock)
            {
                return new ShiftSummary
                {
                    ShiftName       = _currentShiftName,
                    Operator        = _currentShiftOperator,
                    StartTime       = _shiftStartTime,
                    Total           = _total,
                    Good            = _good,
                    NoGood          = _noGood,
                    AveragePpm      = GetCurrentPpm()
                };
            }
        }

        /// <summary>
        /// Logs a shift-end snapshot, resets all counters, and restarts the shift clock.
        /// </summary>
        public async Task ResetForShiftAsync(string shiftName, string operatorName = null)
        {
            _logger.Info("SHIFT_RESET_START|shift={0}|operator={1}|total={2}|good={3}|noGood={4}|ppm={5:F1}",
                shiftName, operatorName ?? "-", _total, _good, _noGood, GetCurrentPpm());

            await ResetAllCountersAsync();
            _shiftStartTime = DateTime.Now;
            _currentShiftName     = shiftName     ?? string.Empty;
            _currentShiftOperator = operatorName  ?? string.Empty;

            _ = ServiceLocator.AuditLogService?.LogAsync(
                "COUNTER_RESET_SHIFT", operatorName ?? "system",
                $"shift={shiftName ?? "-"}|startTime={_shiftStartTime:HH:mm:ss}");

            _logger.Info("SHIFT_RESET_DONE|shift={0}|operator={1}|startTime={2:HH:mm:ss}",
                _currentShiftName, _currentShiftOperator, _shiftStartTime);
        }

        // Metodo per ottenere tutti i contatori come dizionario
        public Dictionary<string, long> GetAllCounters()
        {
            return new Dictionary<string, long>
            {
                { "Total", Total },
                { "Good", Good },
                { "NoGood", NoGood },
                { "Unclassified", Unclassified },
                { "Logo", LogoDefects },
                { "PrintCentering", PrintCenteringDefects },
                { "OpenFlaps", OpenFlapsDefects },
                { "SurfaceCheck", SurfaceCheckDefects },
                { "Height", HeightDefects },
                { "SideSealing", SideSealingDefects },
                { "SideRollCount", SideRollCountDefects },
                { "ShapeTop", ShapeTopDefects },
                { "ShapeSide", ShapeSideDefects },
                { "FrontTraceability", FrontTraceabilityDefects },
                { "ThreeDHeight", ThreeDHeightDefects },
                { "ThreeDWidth", ThreeDWidthDefects },
                { "ThreeDLength", ThreeDLengthDefects },
                { "BottomSealing", BottomSealingDefects },
                { "TrappedPaper", TrappedPaperDefects },
                { "AIClassification", AIClassificationDefects }
            };
        }

        // Metodo per sincronizzare l'UI
        public void NotifyUIUpdate()
        {
            QueueCountersUpdated();
        }

        // Metodo per testare il sistema
        public void TestIncrement()
        {
            _logger.Info("TestIncrement chiamato - Incremento contatori di test");
            Total++;
            Good++;
            LogoDefects++;

            SaveCountersToBackup();
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                CountersUpdated?.Invoke();
            }));
        }

        #endregion

        #region Metodi Privati

        private void LoadCounters()
        {
            try
            {
                if (File.Exists(_backupFilePath))
                {
                    _logger.Info($"Carico contatori da: {_backupFilePath}");
                    var json = File.ReadAllText(_backupFilePath);
                    var loadedCounters = JsonConvert.DeserializeObject<Dictionary<string, long>>(json);

                    if (loadedCounters != null)
                    {
                        lock (_lock)
                        {
                            Total = loadedCounters.ContainsKey("Total") ? loadedCounters["Total"] : 0;
                            Good = loadedCounters.ContainsKey("Good") ? loadedCounters["Good"] : 0;
                            NoGood = loadedCounters.ContainsKey("NoGood") ? loadedCounters["NoGood"] : 0;
                            Unclassified = loadedCounters.ContainsKey("Unclassified") ? loadedCounters["Unclassified"] : 0;
                            LogoDefects = loadedCounters.ContainsKey("Logo") ? loadedCounters["Logo"] : 0;
                            PrintCenteringDefects = loadedCounters.ContainsKey("PrintCentering") ? loadedCounters["PrintCentering"] : 0;
                            OpenFlapsDefects = loadedCounters.ContainsKey("OpenFlaps") ? loadedCounters["OpenFlaps"] : 0;
                            SurfaceCheckDefects = loadedCounters.ContainsKey("SurfaceCheck") ? loadedCounters["SurfaceCheck"] : 0;
                            HeightDefects = loadedCounters.ContainsKey("Height") ? loadedCounters["Height"] : 0;
                            SideSealingDefects = loadedCounters.ContainsKey("SideSealing") ? loadedCounters["SideSealing"] : 0;
                            SideRollCountDefects = loadedCounters.ContainsKey("SideRollCount") ? loadedCounters["SideRollCount"] : 0;
                            ShapeTopDefects = loadedCounters.ContainsKey("ShapeTop") ? loadedCounters["ShapeTop"] : 0;
                            ShapeSideDefects = loadedCounters.ContainsKey("ShapeSide") ? loadedCounters["ShapeSide"] : 0;
                            FrontTraceabilityDefects = loadedCounters.ContainsKey("FrontTraceability") ? loadedCounters["FrontTraceability"] : 0;
                            ThreeDHeightDefects = loadedCounters.ContainsKey("ThreeDHeight") ? loadedCounters["ThreeDHeight"] : 0;
                            ThreeDWidthDefects = loadedCounters.ContainsKey("ThreeDWidth") ? loadedCounters["ThreeDWidth"] : 0;
                            ThreeDLengthDefects = loadedCounters.ContainsKey("ThreeDLength") ? loadedCounters["ThreeDLength"] : 0;
                            BottomSealingDefects = loadedCounters.ContainsKey("BottomSealing") ? loadedCounters["BottomSealing"] : 0;
                            TrappedPaperDefects = loadedCounters.ContainsKey("TrappedPaper") ? loadedCounters["TrappedPaper"] : 0;
                            AIClassificationDefects = loadedCounters.ContainsKey("AIClassification") ? loadedCounters["AIClassification"] : 0;
                        }

                        _logger.Info($"Caricati {loadedCounters.Count} contatori dal backup");
                        _logger.Info($"Total: {Total}, Good: {Good}, NoGood: {NoGood}");

                        // Notifica dopo il caricamento iniziale
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            CountersUpdated?.Invoke();
                        }));
                    }
                }
                else
                {
                    _logger.Info("Nessun file di backup trovato, contatori inizializzati a zero");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel caricamento dei contatori: {ex.Message}");
            }
        }

        public void SaveCountersToBackup()
        {
            try
            {
                var countersToSave = GetAllCounters();
                var json = JsonConvert.SerializeObject(countersToSave, Formatting.Indented);
                File.WriteAllText(_backupFilePath, json);

                _logger.Debug($"Salvati {countersToSave.Count} contatori su backup: {_backupFilePath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel salvataggio dei contatori: {ex.Message}");
            }
        }

        private void QueueBackupSave()
        {
            Interlocked.Exchange(ref _backupWriteRequested, 1);
            if (Interlocked.CompareExchange(ref _backupWriteRunning, 1, 0) != 0)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    do
                    {
                        Interlocked.Exchange(ref _backupWriteRequested, 0);
                        SaveCountersToBackup();
                    }
                    while (Volatile.Read(ref _backupWriteRequested) != 0 && !_disposed);
                }
                finally
                {
                    Interlocked.Exchange(ref _backupWriteRunning, 0);
                    if (Volatile.Read(ref _backupWriteRequested) != 0 && !_disposed)
                    {
                        QueueBackupSave();
                    }
                }
            });
        }

        private void QueueCountersUpdated()
        {
            if (Interlocked.Exchange(ref _uiUpdateQueued, 1) != 0)
            {
                return;
            }

            Action publish = () =>
            {
                Interlocked.Exchange(ref _uiUpdateQueued, 0);
                CountersUpdated?.Invoke();
            };

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                publish();
                return;
            }

            dispatcher.BeginInvoke(publish, System.Windows.Threading.DispatcherPriority.DataBind);
        }
        // Dentro la classe CounterManager
        private string NormalizeDefectType(string defectType)
        {
            switch (defectType?.Trim().ToLowerInvariant())
            {
                case "logo": return "Logo";
                case "print_centering":
                case "printcentering": return "PrintCentering";
                case "openflaps": return "OpenFlaps";
                case "surfacecheck": return "SurfaceCheck";
                case "aiclassification":
                case "ai_classification":
                case "classification":
                case "classify": return "AIClassification";
                case "height": return "Height";
                case "side_sealing":
                case "sidesealing": return "SideSealing";
                case "side_roll_count":
                case "siderollcount":
                case "rollcount":
                case "rotoli": return "SideRollCount";
                case "shapetop": return "ShapeTop";
                case "shapeside": return "ShapeSide";
                case "fronttraceability":
                case "traceability": return "FrontTraceability";
                case "threedheight":
                case "3dheight": return "ThreeDHeight";
                case "threedwidth":
                case "3dwidth": return "ThreeDWidth";
                case "threedlength":
                case "3dlength": return "ThreeDLength";
                case "bottom_sealing":
                case "bottomsealing":
                case "bottom seal":
                case "saldaturainferiore":
                case "saldatura inferiore": return "BottomSealing";
                case "trapped_paper":
                case "trappedpaper":
                case "papertrapped":
                case "carta_intrappolata":
                case "cartaintrappolata":
                case "carta intrappolata":
                case "carta in saldatura": return "TrappedPaper";
                default: return null;
            }
        }

        public async Task ClearDefectMessage(string defectType)
        {
            string uiType = NormalizeDefectType(defectType);
            if (!string.IsNullOrEmpty(uiType))
            {
                lock (_lock)
                {
                    if (_defectErrorMessages.Remove(uiType))
                    {
                        _logger.Debug($"Cancellato messaggio di errore per {uiType}");
                    }
                }
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            // Metodo per notificare cambiamenti delle proprietà (se necessario)
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Backup su file (sincrono, disponibile anche senza DB): rete di sicurezza sui contatori.
            SaveCountersToBackup();

            // Flush finale su DB atteso in modo SINCRONO con timeout, cosi' non si perdono i contatori.
            // Task.Run stacca da un eventuale SynchronizationContext (evita deadlock su .Wait); il
            // timeout evita di bloccare lo shutdown se il DB non risponde (il backup file e' gia' fatto).
            try
            {
                if (!Task.Run(() => _flusher.FlushNowAsync()).Wait(TimeSpan.FromSeconds(5)))
                    _logger.Warn("COUNTER_DISPOSE_FLUSH_TIMEOUT|Flush finale contatori non completato entro 5s (backup file gia' effettuato)");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "COUNTER_DISPOSE_FLUSH_FAILED");
            }

            _flusher?.Dispose();
            _logger.Info("CounterManager disposed");
        }
    }

    public sealed class CounterDbFlusher : IDisposable
    {
        private static readonly Logger _flusherLogger = LogManager.GetCurrentClassLogger();

        private const int DisposeDrainTimeoutMs = 5000;

        // Deliberatamente sotto i 5 s con cui CounterManager.Dispose attende FlushNowAsync:
        // l'attesa del semaforo deve lasciare tempo alla scrittura vera e propria.
        private const int FinalFlushWaitMs = 3000;

        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _interval;
        private readonly Func<Dictionary<string, long>> _snapshotProvider;
        private readonly Func<Dictionary<string, long>, Task> _saveFunc;

        private readonly System.Threading.Timer _timer;
        private volatile bool _dirty;
        private volatile bool _disposed;

        public CounterDbFlusher(
            TimeSpan interval,
            Func<Dictionary<string, long>> snapshotProvider,
            Func<Dictionary<string, long>, Task> saveFunc)
        {
            _interval = interval;
            _snapshotProvider = snapshotProvider;
            _saveFunc = saveFunc;

            _timer = new System.Threading.Timer(OnTimerTick, null, _interval, _interval);
        }

        public void MarkDirty() => _dirty = true;

        // TimerCallback ha firma void: e' quindi obbligatoriamente async void e qualunque
        // eccezione che sfugga da qui viene rilanciata sul ThreadPool e termina il processo.
        // Tutto deve essere assorbito prima di uscire.
        private async void OnTimerTick(object state)
        {
            try
            {
                await FlushAsync(0).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _flusherLogger.Warn(ex, "COUNTER_FLUSH_TICK_FAILED");
            }
        }

        /// <param name="waitMs">
        /// 0 per il flush periodico: se ne e' gia' in corso uno si salta il giro.
        /// Valore positivo per il flush finale, che invece deve attendere il suo turno
        /// per non perdere i contatori.
        /// </param>
        private async Task FlushAsync(int waitMs)
        {
            if (_disposed || !_dirty)
            {
                return;
            }

            bool lockAcquired;
            try
            {
                lockAcquired = await _writeLock.WaitAsync(waitMs).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Dispose concorrente: non c'e' piu' nulla da scrivere.
                return;
            }

            if (!lockAcquired)
            {
                return;
            }

            try
            {
                _dirty = false; // reset "ottimistico"
                var snapshot = _snapshotProvider();
                await _saveFunc(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _dirty = true; // rimetti dirty cosi' ritenta al prossimo giro
                _flusherLogger.Warn(ex, "COUNTER_FLUSH_FAILED");
            }
            finally
            {
                // Difesa contro un Dispose che arriva mentre il salvataggio e' in volo:
                // senza questa guardia la Release su un semaforo gia' distrutto risaliva
                // fino alla callback async void, terminando l'applicazione.
                try
                {
                    _writeLock.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        public async Task FlushNowAsync()
        {
            _dirty = true;
            await FlushAsync(FinalFlushWaitMs).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();

            // Timer.Dispose non attende le callback in volo, e con una callback async void
            // non lo farebbe comunque l'overload con WaitHandle (il timer la considera
            // conclusa al primo await). Il semaforo stesso e' il tracciante: acquisirlo
            // significa che nessun flush e' piu' in corso.
            try
            {
                if (_writeLock.Wait(DisposeDrainTimeoutMs))
                {
                    _writeLock.Release();
                }
                else
                {
                    _flusherLogger.Warn(
                        $"COUNTER_FLUSH_DRAIN_TIMEOUT|flush ancora in corso dopo {DisposeDrainTimeoutMs} ms");
                }
            }
            catch (ObjectDisposedException)
            {
            }

            _writeLock.Dispose();
        }
    }

    public sealed class ShiftSummary
    {
        public string   ShiftName   { get; set; }
        public string   Operator    { get; set; }
        public DateTime StartTime   { get; set; }
        public long     Total       { get; set; }
        public long     Good        { get; set; }
        public long     NoGood      { get; set; }
        public double   AveragePpm  { get; set; }
        public TimeSpan Duration    => DateTime.Now - StartTime;
    }

}
