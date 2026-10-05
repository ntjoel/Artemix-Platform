using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// ViewModel completo per la gestione Card Allarmi con:
    /// - Integrazione InspectionProcessor
    /// - Permessi Administrator
    /// - Database MySQL + Backup XML
    /// </summary>
    public class EjectionAlarmCardViewModel : INotifyPropertyChanged
    {
        private readonly IntegratedAlarmCardService _alarmService;
        private readonly IIODeviceManager _ioManager; // NUOVO: Manager I/O fisico
        private EjectionAlarmConfig _selectedAlarm;
        private string _statusMessage;
        private bool _isLoading;
        private bool _isAdministrator;
        // NUOVO: Flag per prevenire loop
        private bool _isProcessing = false;
        private bool _isSaving = false;
        #region Proprietà per nuovo allarme

        private AlarmType _newAlarmType = AlarmType.ConsecutiveEvents;
        public AlarmType NewAlarmType
        {
            get => _newAlarmType;
            set { _newAlarmType = value; OnPropertyChanged(); }
        }

        private SignalType _newSignalType = SignalType.Blocking;
        public SignalType NewSignalType
        {
            get => _newSignalType;
            set { _newSignalType = value; OnPropertyChanged(); }
        }
        #endregion

        #region Properties

        public ObservableCollection<EjectionAlarmConfig> Alarms { get; } = new ObservableCollection<EjectionAlarmConfig>();
        public event EventHandler<string> LogMessage;
        public ObservableCollection<MachineSignalDefinition> AvailableOutputSignals { get; } = new ObservableCollection<MachineSignalDefinition>();


        public EjectionAlarmConfig SelectedAlarm
        {
            get => _selectedAlarm;
            set
            {
                _selectedAlarm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAlarmSelected));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsAlarmSelected => SelectedAlarm != null;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool IsAdministrator
        {
            get => _isAdministrator;
            private set
            {
                _isAdministrator = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AdminButtonsVisibility));
                OnPropertyChanged(nameof(IsReadOnly));
            }
        }

        public Visibility AdminButtonsVisibility =>
            IsAdministrator ? Visibility.Visible : Visibility.Collapsed;

        public bool IsReadOnly => !IsAdministrator;

        #endregion

        #region Commands

        public ICommand AddAlarmCommand { get; }
        public ICommand RemoveAlarmCommand { get; }
        public ICommand DuplicateAlarmCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ResetAllCommand { get; }
        public ICommand TestAlarmCommand { get; }
        public ICommand ToggleEnabledCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand AddAlarmNCommand { get; }
        public ICommand AddAlarmMCommand { get; }
        public ICommand AddAlarmPercentCommand { get; }
        #endregion

        #region Constructor

        public EjectionAlarmCardViewModel()
        {
            _alarmService = new IntegratedAlarmCardService();
            _alarmService.AlarmTriggered += OnAlarmTriggered;
            _alarmService.StatusMessageChanged += OnStatusMessageChanged;
            // NUOVO: Inizializza I/O manager
            _ioManager = new AdvantechDeviceManager();
            // Verifica permessi
            CheckUserPermissions();
            LoadAvailableOutputSignals();

            // Inizializza comandi
            AddAlarmCommand = new RelayCommand(param => AddNewAlarm(param), _ => IsAdministrator);
            RemoveAlarmCommand = new RelayCommand(_ => RemoveSelectedAlarm(), _ => IsAdministrator && IsAlarmSelected);
            DuplicateAlarmCommand = new RelayCommand(_ => DuplicateSelectedAlarm(), _ => IsAdministrator && IsAlarmSelected);
            SaveCommand = new RelayCommand(_ => SaveConfigurationAsync().SafeFireAndForget(), _ => IsAdministrator);
            ResetAllCommand = new RelayCommand(_ => ResetAllAlarms());
            TestAlarmCommand = new RelayCommand(_ => TestSelectedAlarm(), _ => IsAlarmSelected);
            ToggleEnabledCommand = new RelayCommand(param => ToggleAlarmEnabled(param));
            RefreshCommand = new RelayCommand(_ => RefreshAlarmsAsync().SafeFireAndForget());
            // Nel costruttore:
            AddAlarmNCommand = new RelayCommand(_ => AddNewAlarmOfType(AlarmType.ConsecutiveEvents), _ => IsAdministrator);
            AddAlarmMCommand = new RelayCommand(_ => AddNewAlarmOfType(AlarmType.ConsecutiveEventsAlt), _ => IsAdministrator);
            AddAlarmPercentCommand = new RelayCommand(_ => AddNewAlarmOfType(AlarmType.PercentageInBuffer), _ => IsAdministrator);
            // Canali output disponibili: PCIE-1756 DO00-DO31 (32 canali), elenco da LoadAvailableOutputSignals
           
            // Carica allarmi
            _ = InitializeAsync();
        }

        #endregion

        #region Initialization

        private void CheckUserPermissions()
        {
            try
            {
                var currentRole = MainWindow.configManager?.Config?.Configuration?.CurrentUserRole;
                IsAdministrator = !string.IsNullOrEmpty(currentRole) &&
                                  currentRole.Equals("Administrator", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                IsAdministrator = false;
            }
        }

        private async Task InitializeAsync()
        {
            IsLoading = true;
            StatusMessage = "Caricamento configurazione allarmi...";

            try
            {
                await _alarmService.InitializeAsync();
                await RefreshAlarmsAsync();
                StatusMessage = $"Caricati {Alarms.Count} allarmi";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore caricamento: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RefreshAlarmsAsync()
        {
            try
            {
                var alarms = _alarmService.GetAllAlarms();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    string previousName = _selectedAlarm?.Name;
                    Alarms.Clear();
                    foreach (var alarm in alarms)
                        Alarms.Add(alarm);

                    if (previousName != null)
                    {
                        var restored = Alarms.FirstOrDefault(a => a.Name == previousName);
                        SelectedAlarm = restored ?? (Alarms.Count > 0 ? Alarms[0] : null);
                    }
                    else if (Alarms.Count > 0)
                    {
                        SelectedAlarm = Alarms[0];
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore refresh: {ex.Message}";
            }
        }

        #endregion

        #region Command Handlers

        private void AddNewAlarm(object parameter)
        {
            AddNewAlarmCoreAsync(parameter).SafeFireAndForget();
        }

        private async Task AddNewAlarmCoreAsync(object parameter)
        {
            if (!IsAdministrator) return;
            try
            {
                AlarmType alarmTypeToCreate = _newAlarmType;
                if (parameter is AlarmType type)
                {
                    alarmTypeToCreate = type;
                }
                string typePrefix = null;
                switch(alarmTypeToCreate)
                {
                    case AlarmType.ConsecutiveEvents:
                        typePrefix = "N";
                        break;
                    case AlarmType.ConsecutiveEventsAlt:
                        typePrefix = "M";
                        break;
                    case AlarmType.PercentageInBuffer:
                        typePrefix = "%";
                        break;
                    default:
                        typePrefix = "Alarm";
                        break;
                }

                var newAlarm = new EjectionAlarmConfig
                {
                    Name = $"Alarm {Alarms.Count + 1} ({typePrefix})",
                    AlarmType = alarmTypeToCreate,
                    Threshold = alarmTypeToCreate == AlarmType.PercentageInBuffer ? 50 : 5,
                    BufferSize = alarmTypeToCreate == AlarmType.PercentageInBuffer ? 10 : 0,
                    SignalType = _newSignalType,
                    SignalID = "DO0",
                    OutputChannelId = "DO0",
                    DurationMs = 0,
                    Enabled = true,
                    MonitoredDefects = Enum.GetValues(typeof(DefectType)).Cast<DefectType>().ToList()
                };

                if (await _alarmService.AddAlarmAsync(newAlarm, Alarms.Count))
                {
                    Alarms.Add(newAlarm);
                    SelectedAlarm = newAlarm;
                    StatusMessage = $"Aggiunto allarme: {newAlarm.Name} (Tipo: {alarmTypeToCreate})";
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, "ALARM_ADD_FAILED|type={0}", parameter);
                StatusMessage = $"Errore aggiunta allarme: {ex.Message}";
            }
        }

        private void RemoveSelectedAlarm()
        {
            RemoveSelectedAlarmCoreAsync().SafeFireAndForget();
        }

        private async Task RemoveSelectedAlarmCoreAsync()
        {
            if (!IsAdministrator || SelectedAlarm == null)
            {
                if (SelectedAlarm == null)
                {
                    new SystemNotificationWindow("Nessuna selezione",
                        "Seleziona prima un allarme da eliminare",
                        NotificationSeverity.Info).ShowDialog();
                }
                return;
            }
            try
            {
                var dlgDelete = new SystemNotificationWindow("Conferma eliminazione",
                    $"Sei sicuro di voler eliminare l'allarme '{SelectedAlarm.Name}'?",
                    NotificationSeverity.Warning, true);
                dlgDelete.ShowDialog();
                if (dlgDelete.Confirmed)
                {
                    var name = SelectedAlarm.Name;
                    if (await _alarmService.DeleteAlarmAsync(name))
                    {
                        Alarms.Remove(SelectedAlarm);
                        SelectedAlarm = Alarms.Count > 0 ? Alarms[0] : null;
                        StatusMessage = $"Eliminato allarme: {name}";
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, "ALARM_REMOVE_FAILED|alarm={0}", SelectedAlarm?.Name);
                StatusMessage = $"Errore eliminazione allarme: {ex.Message}";
            }
        }

        private void DuplicateSelectedAlarm()
        {
            DuplicateSelectedAlarmCoreAsync().SafeFireAndForget();
        }

        private async Task DuplicateSelectedAlarmCoreAsync()
        {
            if (!IsAdministrator || SelectedAlarm == null) return;
            try
            {
                var duplicate = SelectedAlarm.Clone();
                duplicate.Name = $"{SelectedAlarm.Name} (Copy)";
                duplicate.TriggerCount = 0;
                duplicate.IsTriggered = false;

                if (await _alarmService.AddAlarmAsync(duplicate, Alarms.Count))
                {
                    Alarms.Add(duplicate);
                    SelectedAlarm = duplicate;
                    StatusMessage = $"Duplicato allarme: {duplicate.Name}";
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, "ALARM_DUPLICATE_FAILED|alarm={0}", SelectedAlarm?.Name);
                StatusMessage = $"Errore duplicazione allarme: {ex.Message}";
            }
        }

        private async Task SaveConfigurationAsync()
        {
            if (!IsAdministrator || _isSaving) return;
            _isSaving = true;
            IsLoading = true;
            try
            {
                foreach (var alarm in Alarms)
                    await _alarmService.UpdateAlarmAsync(alarm.Name, alarm);

                await _alarmService.SaveConfigurationAsync();

                // Sync the singleton used by DigitalIOViewModel so it has the
                // updated OutputChannelId / PulseDurationMs before the next trigger.
                var singleton = ServiceLocator.AlarmService;
                if (singleton != null)
                    await singleton.ReloadAlarmsAsync();

                StatusMessage = "Configurazione salvata con successo";
                await (ServiceLocator.AuditLogService?.LogAsync(
                    "ALARM_CARD_SAVE", UserSession.CurrentUser,
                    $"alarms={Alarms.Count}") ?? Task.CompletedTask);
                await RefreshAlarmsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore salvataggio: {ex.Message}";
            }
            finally
            {
                _isSaving = false;
                IsLoading = false;
            }
        }
        // Aggiungi questo metodo per salvare una singola modifica
        public async Task SaveAlarmChangesAsync(EjectionAlarmConfig alarm)
        {
            if (alarm == null || !IsAdministrator) return;

            try
            {
                IsLoading = true;
                await _alarmService.UpdateAlarmAsync(alarm.Name, alarm);
                StatusMessage = $"Allarme '{alarm.Name}' aggiornato";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore aggiornamento: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        private void ResetAllAlarms()
        {
            _alarmService.ResetAllAlarms();
            OnPropertyChanged(nameof(Alarms));
            StatusMessage = "Tutti gli allarmi sono stati resettati";
        }

        private void TestSelectedAlarm()
        {
            if (SelectedAlarm == null) return;

            // Simula difetti per test
            var testDefects = new System.Collections.Generic.Dictionary<DefectType, bool>();
            foreach (var defect in SelectedAlarm.MonitoredDefects)
            {
                testDefects[defect] = true;
            }

            // Processa con il manager
            var alarmManager = new EjectionAlarmManager();
            alarmManager.ProcessInspectionResult(true, testDefects);

            StatusMessage = $"Test allarme: {SelectedAlarm.Name}";
            OnPropertyChanged(nameof(Alarms));
        }

        private void ToggleAlarmEnabled(object parameter)
        {
            if (parameter is EjectionAlarmConfig alarm)
            {
                alarm.Enabled = !alarm.Enabled;
                OnPropertyChanged(nameof(Alarms));
                _ = _alarmService.UpdateAlarmAsync(alarm.Name, alarm);
                StatusMessage = $"Allarme '{alarm.Name}' {(alarm.Enabled ? "abilitato" : "disabilitato")}";
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Elabora il risultato dell'ispezione e verifica gli allarmi
        /// </summary>
        public async Task ProcessInspectionResultAsync(InspectionResult result)
        {
            await _alarmService.ProcessInspectionResultAsync(result);
            await RefreshAlarmsAsync();
        }
        // Metodo per attivare output fisico con durata
        public async Task TriggerPhysicalOutputAsync(EjectionAlarmConfig alarm)
        {
            try
            {
                var outputChannel = NormalizeOutputChannel(alarm.OutputChannelId);

                if (!IsValidOutputChannel(outputChannel))
                {
                    LogMessage?.Invoke(this, $"ERRORE: canale output {alarm.OutputChannelId} non valido");
                    return;
                }

                // Estrai numero canale da "DO0", "DO1", ecc.
                if (int.TryParse(outputChannel.Replace("DO", ""), out int channel))
                {
                    // Attiva output
                    await _ioManager.WriteOutputAsync(channel, true);

                    LogMessage?.Invoke(this, $"Output {outputChannel} ATTIVATO per allarme {alarm.Name}");

                    // Se c'è una durata specificata, spegni dopo il tempo
                    if (alarm.PulseDurationMs > 0)
                    {
                        await Task.Delay(alarm.PulseDurationMs);
                        await _ioManager.WriteOutputAsync(channel, false);

                        LogMessage?.Invoke(this, $"Output {outputChannel} DISATTIVATO dopo {alarm.PulseDurationMs}ms");
                    }
                }
            }
            catch (Exception ex)
            {
               MainWindow.logger.Error($"Errore nell'attivazione output fisico: {ex.Message}");
            }
        }

        // Metodo per processare ispezione con controllo ejection
        public async Task ProcessInspectionResultWithEjectionCheckAsync(InspectionResult result)
        {
            if (_isProcessing) return;
            try
            {
                _isProcessing = true;
                // 1. Verifica se l'ispezione è fallita
                bool inspectionFailed = !result.IsValid;

                // 2. Se fallita, verifica se l'ejection era abilitato per i difetti rilevati
                if (inspectionFailed)
                {
                    // Estrai i difetti dai risultati di validazione (usa ErrorMessages come nel tuo codice esistente)
                    var defectMap = ExtractDefectsFromInspection(result);

                    // Per ogni allarme attivo, verifica se i difetti rilevati matchano
                    foreach (var alarm in Alarms.Where(a => a.Enabled))
                    {
                        bool shouldTrigger = false;

                        // Se l'allarme ha difetti specifici configurati, controlla solo quelli
                        if (alarm.MonitoredDefects != null && alarm.MonitoredDefects.Count > 0)
                        {
                            // Verifica se almeno uno dei difetti monitorati è presente
                            shouldTrigger = alarm.MonitoredDefects.Any(d =>
                                defectMap.ContainsKey(d) && defectMap[d]);
                        }
                        else
                        {
                            // Nessun filtro specifico, controlla tutti i difetti con ejection
                            shouldTrigger = defectMap.Any(d => d.Value && IsEjectionEnabledForDefect(d.Key));
                        }

                        if (shouldTrigger)
                        {
                            // Processa l'allarme
                            // await ProcessSingleAlarmAsync(alarm, defectMap);
                            //Invece, aggiorna direttamente lo stato dell'allarme
                        await UpdateAlarmStateAsync(alarm);
                        }
                    }
                }
                else
                {
                    // Ispezione OK - processa normalmente (per allarmi consecutivi)
                   // await ProcessInspectionResultAsync(result);
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel controllo ejection: {ex.Message}");
            }
        }
        // NUOVO: Metodo sicuro per aggiornare stato allarme
        private async Task UpdateAlarmStateAsync(EjectionAlarmConfig alarm)
        {
            try
            {
                alarm.TriggerCount++;
                alarm.IsTriggered = true;
                alarm.LastTriggered = DateTime.Now;

                // Notifica UI
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(Alarms));
                });

                // Salva nel service senza triggerare eventi
                await _alarmService.UpdateAlarmAsync(alarm.Name, alarm);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore aggiornamento allarme: {ex.Message}";
            }
        }
        // Metodo helper per estrarre difetti (copia dal tuo IntegratedAlarmCardService)
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

        // Metodo helper per mappare errori a difetti (copia dal tuo IntegratedAlarmCardService)
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

        // Metodo helper per controllare se l'ejection è abilitato per un difetto
        private bool IsEjectionEnabledForDefect(DefectType defect)
        {
            try
            {
                // Ottieni la configurazione ejection dalla ricetta corrente
                var currentRecipeData = GetCurrentRecipeData();
                if (currentRecipeData?.ejectionStatus == null)
                    return false;

                // Sostituisci lo switch expression con uno statement compatibile con C# 7.3
                switch (defect)
                {
                    case DefectType.Logo:
                        return currentRecipeData.ejectionStatus.logo;
                    case DefectType.PrintCentering:
                        return currentRecipeData.ejectionStatus.Print_centering;
                    case DefectType.OpenFlaps:
                        return currentRecipeData.ejectionStatus.OpenFlaps;
                    case DefectType.SurfaceCheck:
                        return currentRecipeData.ejectionStatus.SurfaceCheck;
                    case DefectType.Height:
                        return currentRecipeData.ejectionStatus.Height;
                    case DefectType.SideSealing:
                        return currentRecipeData.ejectionStatus.Side_sealing;
                    case DefectType.SideRollCount:
                        return true;
                    case DefectType.ShapeTop:
                        return currentRecipeData.ejectionStatus.ShapeTop;
                    case DefectType.ShapeSide:
                        return currentRecipeData.ejectionStatus.ShapeSide;
                    case DefectType.FrontTraceability:
                        return currentRecipeData.ejectionStatus.FrontTraceability;
                    case DefectType.ThreeDHeight:
                        return currentRecipeData.ejectionStatus.ThreeDHeight;
                    case DefectType.ThreeDWidth:
                        return currentRecipeData.ejectionStatus.ThreeDWidth;
                    case DefectType.ThreeDLength:
                        return currentRecipeData.ejectionStatus.ThreeDLength;
                    case DefectType.BottomSealing:
                        return currentRecipeData.ejectionStatus.BottomSealing;
                    case DefectType.TrappedPaper:
                        return currentRecipeData.ejectionStatus.TrappedPaper;
                    case DefectType.AIClassification:
                        return currentRecipeData.ejectionStatus.AIClassification;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }
        // Metodo helper per ottenere i dati della ricetta corrente
        private RecipeParameters.RecipeData GetCurrentRecipeData()
        {
            try
            {
                //// Prova a ottenere dalla MainWindow
                //if (MainWindow.CurrentRecipeData != null)
                //    return MainWindow.CurrentRecipeData;

                // Oppure carica dal file XML della ricetta corrente
                string recipeFolder = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder;
                string lastRecipe = MainWindow.configManager?.Config?.Configuration?.LastRecipe;

                if (!string.IsNullOrEmpty(recipeFolder) && !string.IsNullOrEmpty(lastRecipe))
                {
                    string xmlPath = System.IO.Path.Combine(recipeFolder,
                        System.IO.Path.ChangeExtension(lastRecipe, ".xml"));

                    if (File.Exists(xmlPath))
                    {
                        using (var stream = new FileStream(xmlPath, FileMode.Open))
                        {
                            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RecipeParameters.RecipeData));
                            return (RecipeParameters.RecipeData)serializer.Deserialize(stream);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento recipe data: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Aggiorna un allarme specifico
        /// </summary>
        public async Task UpdateAlarmAsync(EjectionAlarmConfig alarm)
        {
            if (alarm == null) return;
            await _alarmService.UpdateAlarmAsync(alarm.Name, alarm);
        }

        // Metodo semplificato:
        private void AddNewAlarmOfType(AlarmType type)
        {
            AddNewAlarmOfTypeCoreAsync(type).SafeFireAndForget();
        }

        private async Task AddNewAlarmOfTypeCoreAsync(AlarmType type)
        {
            if (!IsAdministrator) return;
            try
            {
                string typePrefix;
                switch (type)
                {
                    case AlarmType.ConsecutiveEvents:
                        typePrefix = "N";
                        break;
                    case AlarmType.ConsecutiveEventsAlt:
                        typePrefix = "M";
                        break;
                    case AlarmType.PercentageInBuffer:
                        typePrefix = "%";
                        break;
                    default:
                        typePrefix = "?";
                        break;
                }

                var signalResult = MessageBox.Show(
                    $"Creare allarme tipo {typePrefix}?\n\nSì = Blocking (Bloccante)\nNo = Not-Blocking (Non bloccante)",
                    "Seleziona tipo segnale",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (signalResult == MessageBoxResult.Cancel) return;

                SignalType signalType = signalResult == MessageBoxResult.Yes
                    ? SignalType.Blocking
                    : SignalType.NotBlocking;
                var selectedDefects = GetEjectionEnabledDefects();

                var newAlarm = new EjectionAlarmConfig
                {
                    Name = $"Alarm {Alarms.Count + 1} ({typePrefix})",
                    AlarmType = type,
                    Threshold = type == AlarmType.PercentageInBuffer ? 50 : 5,
                    BufferSize = type == AlarmType.PercentageInBuffer ? 10 : 0,
                    SignalType = signalType,
                    SignalID = "DO0",
                    OutputChannelId = "DO0",
                    DurationMs = 0,
                    Enabled = true,
                    MonitoredDefects = selectedDefects
                };

                if (await _alarmService.AddAlarmAsync(newAlarm, Alarms.Count))
                {
                    Alarms.Add(newAlarm);
                    SelectedAlarm = newAlarm;
                    StatusMessage = $"Aggiunto allarme: {newAlarm.Name}";
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, "ALARM_ADD_BY_TYPE_FAILED|type={0}", type);
                StatusMessage = $"Errore creazione allarme: {ex.Message}";
            }
        }
        // Helper per ottenere difetti con ejection abilitato
        private List<DefectType> GetEjectionEnabledDefects()
        {
            var result = new List<DefectType>();
            var recipeData = GetCurrentRecipeData();

            if (recipeData?.ejectionStatus == null) return result;

            if (recipeData.ejectionStatus.logo) result.Add(DefectType.Logo);
            if (recipeData.ejectionStatus.Print_centering) result.Add(DefectType.PrintCentering);
            if (recipeData.ejectionStatus.OpenFlaps) result.Add(DefectType.OpenFlaps);
            if (recipeData.ejectionStatus.SurfaceCheck) result.Add(DefectType.SurfaceCheck);
            if (recipeData.ejectionStatus.Height) result.Add(DefectType.Height);
            if (recipeData.ejectionStatus.Side_sealing) result.Add(DefectType.SideSealing);
            result.Add(DefectType.SideRollCount);
            if (recipeData.ejectionStatus.ShapeTop) result.Add(DefectType.ShapeTop);
            if (recipeData.ejectionStatus.ShapeSide) result.Add(DefectType.ShapeSide);
            if (recipeData.ejectionStatus.FrontTraceability) result.Add(DefectType.FrontTraceability);
            if (recipeData.ejectionStatus.ThreeDHeight) result.Add(DefectType.ThreeDHeight);
            if (recipeData.ejectionStatus.ThreeDWidth) result.Add(DefectType.ThreeDWidth);
            if (recipeData.ejectionStatus.ThreeDLength) result.Add(DefectType.ThreeDLength);
            if (recipeData.ejectionStatus.BottomSealing) result.Add(DefectType.BottomSealing);
            if (recipeData.ejectionStatus.TrappedPaper) result.Add(DefectType.TrappedPaper);
            if (recipeData.ejectionStatus.AIClassification) result.Add(DefectType.AIClassification);

            return result;
        }
        /// <summary>
        /// Verifica se il canale è valido per la scheda PCIE-1756 (DO0-DO31).
        /// L'uscita operativa è quella impostata sulla scheda allarme (OutputChannelId);
        /// i campi Config.xml BlockingAlarmOutput/NonBlockingAlarmOutput sono legacy e non usati.
        /// </summary>
        private bool IsValidOutputChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return false;
            channel = NormalizeOutputChannel(channel);
            if (!channel.StartsWith("DO", StringComparison.OrdinalIgnoreCase)) return false;

            if (int.TryParse(channel.Substring(2), out int channelNum))
            {
                return Pcie1756ChannelMap.IsValidDoChannel(channelNum);
            }
            return false;
        }

        private string NormalizeOutputChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
            {
                return string.Empty;
            }

            channel = channel.Trim().ToUpperInvariant();
            if (!channel.StartsWith("DO", StringComparison.OrdinalIgnoreCase))
            {
                return channel;
            }

            return int.TryParse(channel.Substring(2), out int channelNum)
                ? $"DO{channelNum}"
                : channel;
        }
        
     

        #endregion

        #region Event Handlers

        private void OnAlarmTriggered(object sender, AlarmTriggeredEventArgs e)
        {
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await RefreshAlarmsAsync();
                StatusMessage = $"ALLARME: {e.Alarm.Name} - {e.Message}";
            });
        }

        private void OnStatusMessageChanged(object sender, string message)
        {
            StatusMessage = message;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Machine Output Signals

        private void LoadAvailableOutputSignals()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                if (config?.MachineOutputs == null) return;
                foreach (var signal in config.MachineOutputs
                    .Where(s => !string.IsNullOrEmpty(s.Channel))
                    .OrderBy(s => s.SignalCode))
                {
                    AvailableOutputSignals.Add(signal);
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"[IO] Impossibile caricare i segnali di uscita: {ex.Message}");
            }
        }

        #endregion
    }
}
