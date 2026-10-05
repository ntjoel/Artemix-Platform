using MySql.Data.MySqlClient;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Views;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace QtisVisionPanel.ViewModels
{
    public class InspectionCounterViewModel : INotifyPropertyChanged
    {
        private readonly CounterManager _counterManager;
        private readonly InspectionConfigService _inspectionConfigService;
        private bool _showPercentageValues;
        private string _totalDefectsDisplayValue = "0";

        public InspectionData InspectionData { get; } = new InspectionData();

        /// <summary>
        /// Operator-facing defect counters filtered against the currently enabled
        /// inspection configuration. This keeps the panel clean and aligned with
        /// the active machine/recipe context.
        /// </summary>
        public ObservableCollection<DefectItem> VisibleDefectCounters { get; } = new ObservableCollection<DefectItem>();

        public double GoodPercentage { get; private set; }
        public double NoGoodPercentage { get; private set; }
        public double QualityPercentage
        {
            get
            {
                var totalCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Header == "Total");
                var goodCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Header == "Good");

                if (totalCounter != null && goodCounter != null && totalCounter.Value > 0)
                {
                    return Math.Round((double)goodCounter.Value / totalCounter.Value * 100, 2);
                }
                return 0;
            }
        }

        public string QualityPercentageColor
        {
            get
            {
                if (QualityPercentage >= 95) return "#008000"; // Verde
                if (QualityPercentage >= 80) return "#FFA500"; // Arancione
                return "#FF0000"; // Rosso
            }
        }
        public ICommand ResetCountersCommand { get; }
        public ICommand ToggleCounterDisplayModeCommand { get; }

        public bool ShowPercentageValues
        {
            get => _showPercentageValues;
            set
            {
                if (_showPercentageValues != value)
                {
                    _showPercentageValues = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CounterDisplayModeLabel));
                    OnPropertyChanged(nameof(CounterDisplayModeToolTip));
                    UpdateDisplayModeValues();
                }
            }
        }

        public string CounterDisplayModeLabel => ShowPercentageValues ? "#" : "%";

        public string CounterDisplayModeToolTip => ShowPercentageValues
            ? ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ShowNumericCounters", "Show numeric counters")
            : ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ShowPercentageCounters", "Show counters in percentage");

        public string TotalDefectsDisplayValue
        {
            get => _totalDefectsDisplayValue;
            private set
            {
                if (_totalDefectsDisplayValue != value)
                {
                    _totalDefectsDisplayValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public InspectionCounterViewModel()
        {
            // Usa il nuovo CounterManager
            _counterManager = ServiceLocator.CounterManager;
            _inspectionConfigService = ServiceLocator.InspectionConfigService;

            // Inizializza contatori
            InitializeCounters();

            // Inizializza i comandi richiesti dalla vista
            ResetCountersCommand = new RelayCommand(async _ => await ResetCountersAsync());
            ToggleCounterDisplayModeCommand = new RelayCommand(_ => ToggleCounterDisplayMode());

            // Sottoscrivi aggiornamenti
            _counterManager.CountersUpdated += OnCountersUpdated;
            if (_inspectionConfigService != null)
            {
                _inspectionConfigService.FeaturesChanged += OnInspectionFeaturesChanged;
            }

            // Carica contatori esistenti
            LoadExistingCounters();
            RefreshVisibleDefectCounters();
        }
        
        private void InitializeCounters()
        {
            // Contatori di selezione
            InspectionData.SelectionCounters.Clear();
            InspectionData.SelectionCounters.Add(new CounterData
            {
                Key = "Total",
                Header = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Total,
                Description = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_TotalDesc,
                Value = 0,
                DisplayValue = "0"
            });
            InspectionData.SelectionCounters.Add(new CounterData
            {
                Key = "Good",
                Header = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_OK,
                Description = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_OKDescr,
                Value = 0,
                DisplayValue = "0"
            });
            InspectionData.SelectionCounters.Add(new CounterData
            {
                Key = "No Good",
                Header = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Defects,
                Description = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_DefectsDesc,
                Value = 0,
                DisplayValue = "0"
            });
            InspectionData.SelectionCounters.Add(new CounterData
            {
                Key = "Unclassified",
                Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Unclassified", "Non classificato"),
                Description = ServerMessagePersonalize.GetMessageOrDefault(
                    "Sub_entry_UnclassifiedDescr",
                    "Errore di elaborazione VisionPro; immagini salvate per diagnosi"),
                Value = 0,
                DisplayValue = "0"
            });

            // Contatori difetti
            InspectionData.DefectsCounters.Clear();
            var defectTypes = new Dictionary<string, (string DisplayName, string Icon, string ToolTip)>
    {
        { "Logo", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Logo_Isp, "InspIcon_Logo",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Logo_Isp_Descr) },
        { "ShapeTop", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ShapeTop, "InspIcon_ShapeTop",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ShapeTop_descr) },
        { "OpenFlaps", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_OpenFlaps, "InspIcon_OpenFlaps",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_OpenFlaps_descr) },
        { "SurfaceCheck", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SurfaceCheck, "InspIcon_SurfaceCheck",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SurfaceCheck_descr) },
        { "PrintCentering", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_PrintCentering, "InspIcon_PrintCentering",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_PrintCentering_descr) },
        { "SideSealing", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SideSealing, "InspIcon_SideSealing",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SideSealing_descr) },
        { "SideRollCount", (ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_SideRollCount", "Roll count"), "InspIcon_SideRollCount",
        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_SideRollCount_descr", "Roll count inside bag validation")) },
        { "ShapeSide", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ShapeSide, "InspIcon_ShapeSide",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ShapeSide_descr) },
        { "Height", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Height_isp, "InspIcon_Height",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Height_isp_descr) },
        { "FrontTraceability", (ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_FrontTraceability_Isp, "InspIcon_FrontTraceability",
        ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_FrontTraceability_Isp_descr) },
        { "ThreeDHeight", (ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDHeight_Isp", "3D Height"), "InspIcon_ThreeDHeight",
        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDHeight_Isp_descr", "3D profilometer height validation")) },
        { "ThreeDWidth", (ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDWidth_Isp", "3D Width"), "InspIcon_ThreeDWidth",
        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDWidth_Isp_descr", "3D profilometer width validation")) },
        { "ThreeDLength", (ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDLength_Isp", "3D Length"), "InspIcon_ThreeDLength",
        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDLength_Isp_descr", "3D profilometer length validation")) },
        { "BottomSealing", (ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_BottomSealing", "Bottom sealing"), "InspIcon_BottomSealing",
        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_BottomSealing_descr", "Bottom sealing validation")) },
        { "TrappedPaper", (ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_TrappedPaper", "Trapped paper"), "InspIcon_TrappedPaper",
        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_TrappedPaper_descr", "Paper trapped in bottom sealing validation")) },
        { "AIClassification", (ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiClassification", "AI classification"), "InspIcon_AIClassification",
        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiClassification_descr", "Standalone VisionPro AI classification validation")) }
    };

            foreach (var defect in defectTypes)
            {
                InspectionData.DefectsCounters.Add(new DefectItem
                {
                    FeatureKey = defect.Key,
                    Feature = defect.Value.DisplayName,
                    IconPath = defect.Value.Icon,
                    ToolTip = defect.Value.ToolTip,
                    LastErrorMessage = null,
                    Count = 0,
                    DisplayValue = "0"
                });
            }
        }

        private  void LoadExistingCounters()
        {
            try
            {
                var counters =  _counterManager.GetAllCounters();
                UpdateCountersFromDictionary(counters);
                CalculatePercentages();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error loading counters: {ex.Message}");
            }
        }

        private void OnCountersUpdated()
        {
            // Aggiorna UI con i nuovi contatori
            var counters = _counterManager.GetAllCounters();
            UpdateCountersFromDictionary(counters);
            CalculatePercentages();
            // Forza l'aggiornamento delle proprietà calcolate
            OnPropertyChanged(nameof(QualityPercentage));
            OnPropertyChanged(nameof(QualityPercentageColor));
        }

        private void OnInspectionFeaturesChanged(object sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                RefreshVisibleDefectCounters();
                UpdateDisplayModeValues();
                MainWindow.logger?.Info("Inspection counters visibility refreshed after inspection configuration change.");
            }));
        }
        public async Task<Dictionary<string, long>> LoadCountersFromDatabaseAsync()
        {
            var counters = new Dictionary<string, long>();

            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port};Connection Timeout=5";

                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT Feature, Counter 
                FROM tblglobalcounters 
                WHERE Feature IN (
                    'TOTAL', 'GOOD', 'NOGOOD', 'LOGO', 'SHAPE_TOP', 
                    'OPEN_FLAPS', 'SURFACE_CHECK', 'PRINT_CENTERING', 
                    'SIDE_SEALING', 'SHAPE_SIDE', 'HEIGHT', 'TRACEABILITY',
                    'THREED_HEIGHT', 'THREED_WIDTH', 'THREED_LENGTH',
                    'BOTTOM_SEALING', 'TRAPPED_PAPER', 'AI_CLASSIFICATION', 'UNCLASSIFIED'
                )";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string feature = reader.GetString(0);
                                long counterValue = reader.GetInt64(1);
                                counters[feature] = counterValue;
                            }
                        }
                    }
                }

                MainWindow.logger?.Debug($"Caricati {counters.Count} contatori dal database");
            }
            catch (MySqlException mysqlEx)
            {
                if (mysqlEx.Number == 1146) // Table doesn't exist
                {
                    MainWindow.logger?.Warn("Tabella non trovata, la creerò al prossimo salvataggio");
                    // Non è un errore critico, restituisci vuoto
                }
                else
                {
                    MainWindow.logger?.Error($"Errore MySQL nel caricamento: {mysqlEx.Message}");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento dei contatori: {ex.Message}");
            }

            return counters;
        }
        private void UpdateCountersFromDictionary(Dictionary<string, long> counters)
        {
            // Mappatura tra feature del database e UI
            var featureMapping = new Dictionary<string, (string Type, string Header)>
{
    { "Total", ("SELECTION", "Total") },
    { "Good", ("SELECTION", "Good") },
    { "NoGood", ("SELECTION", "No Good") },
    { "Unclassified", ("SELECTION", "Unclassified") },
    { "UNCLASSIFIED", ("SELECTION", "Unclassified") },
    { "Logo", ("DEFECT", "Logo") },
    { "PrintCentering", ("DEFECT", "PrintCentering") },
    { "OpenFlaps", ("DEFECT", "OpenFlaps") },
    { "SurfaceCheck", ("DEFECT", "SurfaceCheck") },
    { "Height", ("DEFECT", "Height") },
    { "SideSealing", ("DEFECT", "SideSealing") },
    { "SideRollCount", ("DEFECT", "SideRollCount") },
    { "ShapeTop", ("DEFECT", "ShapeTop") },
    { "ShapeSide", ("DEFECT", "ShapeSide") },
    { "FrontTraceability", ("DEFECT", "FrontTraceability") },
    { "TRACEABILITY", ("DEFECT", "FrontTraceability") },
    { "ThreeDHeight", ("DEFECT", "ThreeDHeight") },
    { "ThreeDWidth", ("DEFECT", "ThreeDWidth") },
    { "ThreeDLength", ("DEFECT", "ThreeDLength") },
    { "THREED_HEIGHT", ("DEFECT", "ThreeDHeight") },
    { "THREED_WIDTH", ("DEFECT", "ThreeDWidth") },
    { "THREED_LENGTH", ("DEFECT", "ThreeDLength") },
    { "BottomSealing", ("DEFECT", "BottomSealing") },
    { "BOTTOM_SEALING", ("DEFECT", "BottomSealing") },
    { "TrappedPaper", ("DEFECT", "TrappedPaper") },
    { "TRAPPED_PAPER", ("DEFECT", "TrappedPaper") },
    { "AIClassification", ("DEFECT", "AIClassification") },
    { "AI_CLASSIFICATION", ("DEFECT", "AIClassification") }
};

            foreach (var counter in counters)
            {
                if (featureMapping.TryGetValue(counter.Key, out var mapping))
                {
                    if (mapping.Type == "SELECTION")
                    {
                        string errorMessage = _counterManager.GetLastErrorMessage(mapping.Header);
                        UpdateSelectionCounter(mapping.Header, counter.Value);
                        UpdateDefectCounter(mapping.Header, counter.Value, errorMessage);
                    }
                    else if (mapping.Type == "DEFECT")
                    {
                        string errorMessage = ResolveLastErrorMessage(counter.Key, mapping.Header);
                        UpdateDefectCounter(mapping.Header, counter.Value, errorMessage);
                    }
                }
            }
        }

        // Metodo per aggiornare i contatori da dati globali (usato da StatisticsView)
        public void UpdateFromGlobalCounters(Dictionary<string, long> globalCounters)
        {
            UpdateCountersFromDictionary(globalCounters);
            CalculatePercentages();
            RefreshVisibleDefectCounters();
        }

        // Metodo per aggiornare un contatore di selezione
        public void UpdateSelectionCounter(string header, long newValue)
        {
            var counter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Key == header)
                       ?? InspectionData.SelectionCounters.FirstOrDefault(c => c.Header == header);
            if (counter != null)
            {
                counter.Value = newValue;
                OnPropertyChanged(nameof(InspectionData));
            }
        }

        // Metodo per aggiornare un contatore di difetto
        public void UpdateDefectCounter(string feature, long newCount, string errorMessage = null)
        {
            string normalizedFeature = NormalizeFeatureKey(feature);
            string displayFeature = ResolveDefectDisplayName(normalizedFeature);
            var defect = InspectionData.DefectsCounters.FirstOrDefault(d =>
                string.Equals(d.FeatureKey, normalizedFeature, StringComparison.OrdinalIgnoreCase) ||
                d.Feature == feature || d.Feature == displayFeature);
            if (defect != null)
            {
                defect.Count = newCount;
                if (newCount == 0 || string.IsNullOrWhiteSpace(errorMessage))
                {
                    defect.LastErrorMessage = null;
                }
                else
                {
                    defect.LastErrorMessage = errorMessage;
                }

                OnPropertyChanged(nameof(InspectionData));
            }
            RefreshVisibleDefectCounters();
        }

        private string ResolveLastErrorMessage(params string[] featureKeys)
        {
            foreach (string featureKey in featureKeys ?? Array.Empty<string>())
            {
                string normalizedFeature = NormalizeFeatureKey(featureKey);
                string message = _counterManager.GetLastErrorMessage(normalizedFeature);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }

                message = _counterManager.GetLastErrorMessage(featureKey);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }

            return null;
        }

        private string ResolveDefectDisplayName(string feature)
        {
            switch (feature)
            {
                case "Logo":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Logo_Isp;
                case "PrintCentering":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_PrintCentering;
                case "OpenFlaps":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_OpenFlaps;
                case "SurfaceCheck":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SurfaceCheck;
                case "Height":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Height_isp;
                case "SideSealing":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SideSealing;
                case "SideRollCount":
                    return ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_SideRollCount", "Roll count");
                case "ShapeTop":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ShapeTop;
                case "ShapeSide":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ShapeSide;
                case "FrontTraceability":
                    return ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_FrontTraceability_Isp;
                case "ThreeDHeight":
                    return ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDHeight_Isp", "3D Height");
                case "ThreeDWidth":
                    return ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDWidth_Isp", "3D Width");
                case "ThreeDLength":
                    return ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDLength_Isp", "3D Length");
                case "BottomSealing":
                    return ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_BottomSealing", "Bottom sealing");
                case "TrappedPaper":
                    return ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_TrappedPaper", "Trapped paper");
                case "AIClassification":
                    return ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiClassification", "AI classification");
                default:
                    return feature;
            }
        }
        // Metodo helper per creare un messaggio di errore di default
      
        private void CalculatePercentages()
        {
            var totalCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Key == "Total" || c.Header == "Total");
            var goodCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Key == "Good" || c.Header == "Good");
            var noGoodCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Key == "No Good" || c.Header == "No Good");

            if (totalCounter != null && totalCounter.Value > 0)
            {
                GoodPercentage = Math.Round((double)goodCounter.Value / totalCounter.Value * 100, 2);
                NoGoodPercentage = Math.Round((double)noGoodCounter.Value / totalCounter.Value * 100, 2);
            }
            else
            {
                GoodPercentage = 0;
                NoGoodPercentage = 0;
            }

            OnPropertyChanged(nameof(GoodPercentage));
            OnPropertyChanged(nameof(NoGoodPercentage));
            OnPropertyChanged(nameof(QualityPercentage)); // Aggiungi questa linea
            OnPropertyChanged(nameof(QualityPercentageColor)); // Aggiungi questa linea
            UpdateDisplayModeValues();
        }

        // Metodo principale per incrementare contatori (chiamato dal sistema di ispezione)
        // Metodo principale per incrementare contatori
        public async Task IncrementCountersAsync(bool isCompliant, Dictionary<string, bool> defects = null)
        {
            try
            {
                await _counterManager.ProcessInspectionAsync(isCompliant, defects);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error incrementing counters: {ex.Message}");
            }
        }

        // Comando: Reset contatori
        private async Task ResetCountersAsync()
        {
            try
            {
                var dlgReset = new SystemNotificationWindow(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ResetCountersConfirmTitle", "Reset counters"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ResetCountersConfirmMessage", "Do you want to reset all counters?\nThis operation cannot be undone."),
                    NotificationSeverity.Warning, true);
                dlgReset.ShowDialog();
                if (dlgReset.Confirmed)
                {
                    await _counterManager.ResetAllCountersAsync();
                    await (ServiceLocator.AuditLogService?.LogAsync(
                        "COUNTER_RESET_ALL", UserSession.CurrentUser, "source=InspectionCounterView") ?? Task.CompletedTask);
                    new SystemNotificationWindow(
                        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ResetCountersSuccessTitle", "Success"),
                        ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ResetCountersSuccess", "Counters reset successfully!"),
                        NotificationSeverity.Info).ShowDialog();
                }
            }
            catch (Exception ex)
            {
                new SystemNotificationWindow(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ResetCountersConfirmTitle", "Reset counters"),
                    $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ResetCountersError", "Error while resetting counters")}: {ex.Message}",
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private void ToggleCounterDisplayMode()
        {
            ShowPercentageValues = !ShowPercentageValues;
        }

        private long GetSelectionCounterValueByIndex(int index)
        {
            if (InspectionData.SelectionCounters.Count > index)
            {
                return InspectionData.SelectionCounters[index].Value;
            }

            return 0;
        }

        private void UpdateDisplayModeValues()
        {
            long totalInspections = GetSelectionCounterValueByIndex(0);
            long goodInspections = GetSelectionCounterValueByIndex(1);
            long noGoodInspections = GetSelectionCounterValueByIndex(2);
            long totalDefectEvents = VisibleDefectCounters.Sum(defect => defect.Count);

            for (int i = 0; i < InspectionData.SelectionCounters.Count; i++)
            {
                var counter = InspectionData.SelectionCounters[i];
                if (counter == null)
                {
                    continue;
                }

                if (!ShowPercentageValues)
                {
                    counter.DisplayValue = counter.Value.ToString();
                    continue;
                }

                if (i == 0)
                {
                    counter.DisplayValue = totalInspections > 0 ? "100%" : "0%";
                }
                else if (i == 1)
                {
                    counter.DisplayValue = FormatPercentage(goodInspections, totalInspections);
                }
                else if (i == 2)
                {
                    counter.DisplayValue = FormatPercentage(noGoodInspections, totalInspections);
                }
                else
                {
                    counter.DisplayValue = counter.Value.ToString();
                }
            }

            foreach (var defect in InspectionData.DefectsCounters)
            {
                if (defect == null)
                {
                    continue;
                }

                bool defectVisible = VisibleDefectCounters.Contains(defect);
                defect.DisplayValue = ShowPercentageValues
                    ? FormatPercentage(defect.Count, defectVisible ? totalDefectEvents : 0)
                    : defect.Count.ToString();
            }

            TotalDefectsDisplayValue = ShowPercentageValues
                ? (totalDefectEvents > 0 ? "100%" : "0%")
                : totalDefectEvents.ToString();
        }

        private string FormatPercentage(long partialValue, long totalValue)
        {
            if (totalValue <= 0)
            {
                return "0%";
            }

            double percentage = Math.Round((double)partialValue / totalValue * 100, 1);
            return string.Format("{0:0.#}%", percentage);
        }

        public void Dispose()
        {
            _counterManager.CountersUpdated -= OnCountersUpdated;
            if (_inspectionConfigService != null)
            {
                _inspectionConfigService.FeaturesChanged -= OnInspectionFeaturesChanged;
            }
        }

        public void Resubscribe()
        {
            _counterManager.CountersUpdated -= OnCountersUpdated;
            _counterManager.CountersUpdated += OnCountersUpdated;
            if (_inspectionConfigService != null)
            {
                _inspectionConfigService.FeaturesChanged -= OnInspectionFeaturesChanged;
                _inspectionConfigService.FeaturesChanged += OnInspectionFeaturesChanged;
            }
        }

        private void RefreshVisibleDefectCounters()
        {
            VisibleDefectCounters.Clear();

            // Keep the original ordering but expose only the inspections that
            // are currently enabled in InspectionConfigService.
            foreach (var defect in InspectionData.DefectsCounters.Where(IsDefectEnabledForDisplay))
            {
                VisibleDefectCounters.Add(defect);
            }

            OnPropertyChanged(nameof(VisibleDefectCounters));
            UpdateDisplayModeValues();
        }

        private bool IsDefectEnabledForDisplay(DefectItem defect)
        {
            if (defect == null)
            {
                return false;
            }

            string featureKey = NormalizeFeatureKey(defect.FeatureKey ?? defect.Feature);
            if (string.IsNullOrWhiteSpace(featureKey) || _inspectionConfigService == null)
            {
                return true;
            }

            return _inspectionConfigService.IsFeatureEnabled(featureKey);
        }

        private string NormalizeFeatureKey(string feature)
        {
            switch (feature?.Trim().ToLowerInvariant())
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
                case "aiclassification":
                case "ai_classification":
                case "ai classification":
                case "classify":
                case "classification":
                    return "AIClassification";
                default:
                    return feature;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
