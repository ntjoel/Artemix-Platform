using Newtonsoft.Json;
using QtisVisionPanel.Cls_Config;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Cls_Vpro;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters;

namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// View model for recipe selection, editing and production loading.
    ///
    /// Recipes belong to product configuration, not machine wiring. This view
    /// model therefore edits product data and delegates runtime changes through
    /// the same services used by operator, OPC UA and recovery flows.
    /// </summary>
    public class RecipeManagerViewModel : AuthorizedViewModel
    {
        private RecipeItem _selectedRecipe;
        private RecipeParameters.RecipeData _currentRecipeData;
        private bool _isModified;
        private bool _isLoaded = false;
        private RecipeItem _productionRecipe;
        private string _originalRecipeName;

        // Aggiungi queste proprietà
        public ObservableCollection<InspectionThresholdItem> TopThresholdItems { get; set; } = new ObservableCollection<InspectionThresholdItem>();
        public ObservableCollection<InspectionThresholdItem> SideThresholdItems { get; set; } = new ObservableCollection<InspectionThresholdItem>();
        public ObservableCollection<RecipeItem> Recipes { get; set; } = new ObservableCollection<RecipeItem>();
        public ObservableCollection<InspectionItem> InspectionItems { get; set; } = new ObservableCollection<InspectionItem>();
        public ObservableCollection<InspectionItem> EjectionItems { get; set; } = new ObservableCollection<InspectionItem>();
        public IReadOnlyList<RecipeMultiShotModeOption> RecipeMultiShotModes { get; private set; }

        private RecipeMachineRuntimeOverview _machineRuntimeAdjustmentOverview;
        public RecipeMachineRuntimeOverview MachineRuntimeAdjustmentOverview
        {
            get => _machineRuntimeAdjustmentOverview;
            private set
            {
                _machineRuntimeAdjustmentOverview = value;
                OnPropertyChanged();
            }
        }

        // Variabili private per i permessi
        private bool _canEditTolerances = false;
        private bool _canEditProductInfo = false;
        private bool _canEditRecipe = false;

        // PROPRIETÀ pubbliche per i permessi
        public bool CanEditTolerances
        {
            get => _canEditTolerances;
            set
            {
                _canEditTolerances = value;
                OnPropertyChanged();
            }
        }
        private bool _canEditTriggerDelay = false;
        public bool CanEditTriggerDelay
        {
            get => _canEditTriggerDelay;
            set
            {
                _canEditTriggerDelay = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanShowTriggerDelaySection));
            }
        }

        /// <summary>
        /// I ritardi di trigger della ricetta vengono applicati alla camera SOLO quando
        /// <c>ApplyCameraTriggerDelayToHardwareAtStartup</c> e' attivo nelle Preferenze.
        /// In modalita' io-driven (default) il ritardo lo determina il punto di intervento
        /// sull'encoder e questi campi non hanno alcun effetto: il runtime li salta e logga
        /// RECIPE_TRIGGER_DELAY_SKIPPED. Mostrarli comunque induce l'operatore a tarare un
        /// valore che non fa nulla, quindi la sezione compare solo dove serve davvero.
        /// </summary>
        public bool CanShowTriggerDelaySection =>
            CanEditTriggerDelay && MainWindow.ApplyCameraTriggerDelayToHardwareAtStartup;
        public bool CanEditProductInfo
        {
            get => _canEditProductInfo;
            set
            {
                _canEditProductInfo = value;
                OnPropertyChanged();
            }
        }

        public bool CanEditRecipe
        {
            get => _canEditRecipe;
            set
            {
                _canEditRecipe = value;
                OnPropertyChanged();
            }
        }

        // Aggiungi questa proprietà per mostrare il banner informativo
        public bool ShowReadOnlyWarning => !CanEditRecipe;

        public bool CanLoadRecipeToProduction => _canLoadRecipeToProduction;
        public bool CanDeleteRecipe => _canDeleteRecipe;
        public bool CanCreateRecipe => _canCreateRecipe;
        public bool CanSaveRecipePermission => _canSaveRecipe;

        public string RecipeReadOnlyWarningText
        {
            get
            {
                if (CanEditRecipe)
                {
                    return string.Empty;
                }

                if (_canLoadRecipeToProduction)
                {
                    return GetMessage(
                        "Sub_entry_RecipeProductionModeWarning",
                        "Modalita produzione: puoi consultare e caricare ricette autorizzate. La modifica e' riservata a Expert, Installer o Administrator.");
                }

                return GetMessage(
                    "Sub_entry_RecipeReadOnlyWarning",
                    "Modalita sola lettura: puoi consultare le ricette. Per caricare o modificare serve un ruolo autorizzato.");
            }
        }

        public string RecipeLoadToolTip
        {
            get
            {
                if (SelectedRecipe == null)
                {
                    return GetMessage("Sub_entry_RecipeSelectBeforeLoad", "Seleziona una ricetta da caricare.");
                }

                if (SelectedRecipe.Status == RecipeStatus.InProduction)
                {
                    return GetMessage("Sub_entry_RecipeAlreadyInProduction", "Questa e' gia' la ricetta in produzione.");
                }

                if (!_canLoadRecipeToProduction)
                {
                    return GetMessage("Sub_entry_RecipeLoadPermissionRequired", "Caricamento in produzione non autorizzato per questo ruolo.");
                }

                return GetMessage("Sub_entry_RecipeLoadTooltip", "Carica ricetta in produzione.");
            }
        }

        public string RecipeDeleteToolTip => _canDeleteRecipe
            ? GetMessage("Sub_entry_RecipeDeleteTooltip", "Delete recipe")
            : GetMessage("Sub_entry_RecipeDeletePermissionRequired", "Eliminazione ricetta riservata a ruoli tecnici.");

        public string RecipeSaveToolTip
        {
            get
            {
                if (!_canSaveRecipe)
                {
                    return GetMessage("Sub_entry_RecipeSavePermissionRequired", "Salvataggio ricetta riservato a ruoli tecnici.");
                }

                if (!IsModified)
                {
                    return GetMessage("Sub_entry_RecipeSaveNoChanges", "Nessuna modifica da salvare.");
                }

                return GetMessage("Sub_entry_RecipeSaveTooltip", "Save changes");
            }
        }

        public string RecipeDuplicateToolTip => _canCreateRecipe
            ? GetMessage("Sub_entry_RecipeDuplicateTooltip", "Copy recipe")
            : GetMessage("Sub_entry_RecipeDuplicatePermissionRequired", "Duplicazione ricetta riservata a ruoli tecnici.");

        public string Configurazione
        {
            get
            {
                if (SelectedRecipe == null) return "";

                // Determina la configurazione in base al RecipeType
                switch (SelectedRecipe.RecipeType)
                {
                    case "Ricetta pacchi":
                        return "1";
                    case "Ricetta rotoli":
                        return "2";
                    case "Templato":
                        return "3";
                    default:
                        return "3"; // Generico
                }
            }
        }

        public string MachineType
        {
            get => MainWindow.configManager?.Config?.Configuration?.MachineType ?? "Packs";
        }

        public bool IsThreeDCheckMachineType =>
            string.Equals(MachineType, "3DCheck", StringComparison.OrdinalIgnoreCase);

        public bool CanShowFrontTraceabilitySection =>
            HasFrontCameraConfigured && !IsThreeDCheckMachineType;

        public bool CanShowThreeDSection =>
            HasTop3DConfigured && IsThreeDCheckMachineType;

        /// <summary>
        /// The recipe editor exposes only the camera-specific sections that are
        /// actually configured in CameraConfig.xml / active job-role mapping.
        /// This keeps the same recipe page usable across Top+Side and Top+Front
        /// machine variants without branching the whole view.
        /// </summary>
        public bool HasTopCameraConfigured => HasConfiguredCameraRole("top");
        public bool HasSideCameraConfigured => HasConfiguredCameraRole("side");
        public bool HasLeftCameraConfigured => HasConfiguredCameraRole("left");
        public bool HasFrontCameraConfigured => HasConfiguredCameraRole("front");
        public bool HasTop3DConfigured => HasConfiguredCameraRole("top3d");
        public bool HasBottomCameraConfigured => HasConfiguredCameraRole("bottom");
        public bool HasRightCameraConfigured => HasConfiguredCameraRole("right");
        public bool HasRearCameraConfigured => HasConfiguredCameraRole("rear");
        public string LeftCameraTriggerLabel =>
            CameraConfigurationHelper.GetCameraRoleDisplayLabel("left").ToUpperInvariant() + " trigger";
        public bool CanShowTopPositionAdjustment => HasTopCameraConfigured || HasTop3DConfigured;
        public bool CanShowSideLeftPositionAdjustments => HasSideCameraConfigured || HasLeftCameraConfigured;
        public bool CanShowRightRearPositionAdjustments => HasRightCameraConfigured || HasRearCameraConfigured;
        public bool CanShowSideInspectionThresholds =>
            HasSideCameraConfigured || HasLeftCameraConfigured ||
            HasRightCameraConfigured || HasRearCameraConfigured;
        public bool CanShowSideLeftMultiShotAdjustment => CanShowSideLeftPositionAdjustments;
        public bool CanShowRightRearMultiShotAdjustment => CanShowRightRearPositionAdjustments;
        public bool CanShowBottomMultiShotAdjustment => HasBottomCameraConfigured;

        public RecipeItem SelectedRecipe
        {
            get => _selectedRecipe;
            set
            {
                // Reset previous selection status if it was only selected (not production)
                if (_selectedRecipe != null && _selectedRecipe != _productionRecipe)
                {
                    if (_selectedRecipe.Status == RecipeStatus.Selected)
                    {
                        _selectedRecipe.Status = RecipeStatus.Normal;
                    }
                }

                _selectedRecipe = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Configurazione));
                OnPropertyChanged(nameof(MachineType));
                OnPropertyChanged(nameof(IsThreeDCheckMachineType));
                OnPropertyChanged(nameof(CanShowFrontTraceabilitySection));
                OnPropertyChanged(nameof(CanShowThreeDSection));
                OnPropertyChanged(nameof(HasBottomCameraConfigured));
                NotifyRecipeMachineAdjustmentVisibility();
                OnPropertyChanged(nameof(RecipeLoadToolTip));
                CommandManager.InvalidateRequerySuggested();

                if (value != null)
                {
                    // Salva il nome originale per confronto
                    _originalRecipeName = value.Name;
                    // If the recipe is already in production, don't change its status
                    if (value != _productionRecipe)
                    {
                        value.Status = RecipeStatus.Selected;
                    }

                    LoadRecipeDetails(value);
                }
                else
                {
                    CurrentRecipeData = null;
                    InspectionItems.Clear();
                    EjectionItems.Clear();
                }
            }
        }

        public void UpdateConfiguration()
        {
            OnPropertyChanged(nameof(Configurazione));
        }

        public RecipeItem ProductionRecipe
        {
            get => _productionRecipe;
            set
            {
                _productionRecipe = value;
                OnPropertyChanged();
            }
        }

        private string GetCurrentProductionRecipe()
        {
            return MainWindow.configManager?.Config?.Configuration?.LastRecipe;
        }

        public RecipeParameters.RecipeData CurrentRecipeData
        {
            get => _currentRecipeData;
            set
            {
                _currentRecipeData = value;
                EnsureRecipeRuntimeExtensions(_currentRecipeData);
                OnPropertyChanged();
                OnPropertyChanged(nameof(LeftCameraTriggerMode));
                OnPropertyChanged(nameof(RightCameraTriggerMode));
                OnPropertyChanged(nameof(RearCameraTriggerMode));
                OnPropertyChanged(nameof(LeftCameraOffsetMm));
                OnPropertyChanged(nameof(RightCameraOffsetMm));
                OnPropertyChanged(nameof(RearCameraOffsetMm));
                OnPropertyChanged(nameof(LeftCameraHardwareDelayUs));
                OnPropertyChanged(nameof(IsThreeDCheckMachineType));
                OnPropertyChanged(nameof(CanShowFrontTraceabilitySection));
                OnPropertyChanged(nameof(CanShowThreeDSection));
                OnPropertyChanged(nameof(HasBottomCameraConfigured));
                OnPropertyChanged(nameof(ThreeDProfileMinimumValidPixelsPercent));
                NotifyRecipeMachineAdjustmentVisibility();
                UpdateInspectionItems();
                RefreshRecipeMachineRuntimeOverview();
            }
        }

        // Side/Rear remain readable in existing XML recipes. Editing a physical camera
        // writes one authoritative value and clears its historical alias.
        public string LeftCameraTriggerMode
        {
            get
            {
                var modes = CurrentRecipeData?.machineRuntimeAdjustments?.CameraTriggers;
                return ResolvePhysicalCameraMode(modes?.Left, modes?.Side);
            }
            set
            {
                EnsureRecipeRuntimeExtensions(CurrentRecipeData);
                var modes = CurrentRecipeData?.machineRuntimeAdjustments?.CameraTriggers;
                if (modes == null) return;
                modes.Left = value;
                modes.Side = "Machine";
                IsModified = true;
                OnPropertyChanged();
            }
        }

        public string RightCameraTriggerMode
        {
            get
            {
                var modes = CurrentRecipeData?.machineRuntimeAdjustments?.CameraTriggers;
                return modes?.Right ?? "Machine";
            }
            set
            {
                EnsureRecipeRuntimeExtensions(CurrentRecipeData);
                var modes = CurrentRecipeData?.machineRuntimeAdjustments?.CameraTriggers;
                if (modes == null) return;
                modes.Right = value;
                IsModified = true;
                OnPropertyChanged();
            }
        }

        public string RearCameraTriggerMode
        {
            get
            {
                var modes = CurrentRecipeData?.machineRuntimeAdjustments?.CameraTriggers;
                return !HasRightCameraConfigured
                    ? ResolvePhysicalCameraMode(modes?.Rear, modes?.Right)
                    : modes?.Rear ?? "Machine";
            }
            set
            {
                EnsureRecipeRuntimeExtensions(CurrentRecipeData);
                var modes = CurrentRecipeData?.machineRuntimeAdjustments?.CameraTriggers;
                if (modes == null) return;
                modes.Rear = value;
                if (!HasRightCameraConfigured) modes.Right = "Machine";
                IsModified = true;
                OnPropertyChanged();
            }
        }

        public double LeftCameraOffsetMm
        {
            get
            {
                var positions = CurrentRecipeData?.machineRuntimeAdjustments?.CameraPositions;
                return Math.Abs(positions?.LeftOffsetMm ?? 0d) > 0.0000001
                    ? positions.LeftOffsetMm
                    : positions?.SideOffsetMm ?? 0d;
            }
            set
            {
                EnsureRecipeRuntimeExtensions(CurrentRecipeData);
                var positions = CurrentRecipeData?.machineRuntimeAdjustments?.CameraPositions;
                if (positions == null) return;
                positions.LeftOffsetMm = value;
                positions.SideOffsetMm = 0d;
                IsModified = true;
                OnPropertyChanged();
            }
        }

        public double RightCameraOffsetMm
        {
            get
            {
                var positions = CurrentRecipeData?.machineRuntimeAdjustments?.CameraPositions;
                return positions?.RightOffsetMm ?? 0d;
            }
            set
            {
                EnsureRecipeRuntimeExtensions(CurrentRecipeData);
                var positions = CurrentRecipeData?.machineRuntimeAdjustments?.CameraPositions;
                if (positions == null) return;
                positions.RightOffsetMm = value;
                IsModified = true;
                OnPropertyChanged();
            }
        }

        public double RearCameraOffsetMm
        {
            get
            {
                var positions = CurrentRecipeData?.machineRuntimeAdjustments?.CameraPositions;
                return !HasRightCameraConfigured && Math.Abs(positions?.RearOffsetMm ?? 0d) < 0.0000001
                    ? positions?.RightOffsetMm ?? 0d
                    : positions?.RearOffsetMm ?? 0d;
            }
            set
            {
                EnsureRecipeRuntimeExtensions(CurrentRecipeData);
                var positions = CurrentRecipeData?.machineRuntimeAdjustments?.CameraPositions;
                if (positions == null) return;
                positions.RearOffsetMm = value;
                if (!HasRightCameraConfigured) positions.RightOffsetMm = 0d;
                IsModified = true;
                OnPropertyChanged();
            }
        }

        private static string ResolvePhysicalCameraMode(string physicalMode, string legacyMode)
        {
            return string.IsNullOrWhiteSpace(physicalMode) ||
                   string.Equals(physicalMode.Trim(), "Machine", StringComparison.OrdinalIgnoreCase)
                ? legacyMode ?? "Machine"
                : physicalMode;
        }

        public double LeftCameraHardwareDelayUs
        {
            get => CurrentRecipeData?.cameraSetting?.ResolveLeftCameraTriggerDelay() ?? 0d;
            set
            {
                if (CurrentRecipeData?.cameraSetting == null) return;
                if (Math.Abs(LeftCameraHardwareDelayUs - value) < 0.0000001) return;
                CurrentRecipeData.cameraSetting.LeftCameraTriggerDelay = value;
                CurrentRecipeData.cameraSetting.SideCameraTriggerDelay = 0d;
                IsModified = true;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Operator-facing percentage for the recipe ratio stored as a normalized 0..1 value.
        /// </summary>
        public double ThreeDProfileMinimumValidPixelsPercent
        {
            get => (CurrentRecipeData?.recipeParamTop3D?.ThreeDHeightMinimumValidPixelRatio ?? 0.75) * 100.0;
            set
            {
                EnsureRecipeRuntimeExtensions(CurrentRecipeData);
                if (CurrentRecipeData?.recipeParamTop3D == null)
                {
                    return;
                }

                double normalizedValue = value / 100.0;
                if (Math.Abs(CurrentRecipeData.recipeParamTop3D.ThreeDHeightMinimumValidPixelRatio - normalizedValue) < 0.0000001)
                {
                    return;
                }

                CurrentRecipeData.recipeParamTop3D.ThreeDHeightMinimumValidPixelRatio = normalizedValue;
                IsModified = true;
                OnPropertyChanged();
            }
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                _isModified = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveRecipeCommand));
                OnPropertyChanged(nameof(RecipeSaveToolTip));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // PROPRIETÀ per il binding del comando Save
        public bool CanSaveRecipeCommand => SelectedRecipe != null && IsModified && _canSaveRecipe;

        public ICommand LoadRecipeCommand { get; }
        public ICommand DeleteRecipeCommand { get; }
        public ICommand SaveRecipeCommand { get; }
        public ICommand CopyRecipeCommand { get; }
        public ICommand RefreshRecipesCommand { get; }
        public ICommand SelectImageCommand { get; }
        // Aggiungi comando per login
        public ICommand LoginCommand { get; }

        private readonly string _recipeFolder;
        private readonly InserdataInDb _dbManager;
        private readonly RecipeAuthorizationService _recipeAuthService;

        // VARIABILI PRIVATE PER I PERMESSI DAL DATABASE
        private bool _canLoadRecipeToProduction = false;
        private bool _canDeleteRecipe = false;
        private bool _canSaveRecipe = false;
        private bool _canCreateRecipe = false;
        private bool _canViewRecipeDetails = false;

        public RecipeManagerViewModel()
        {
            _recipeFolder = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder ?? @"C:\QtisVision\Recipes";
            _dbManager = new InserdataInDb();
            _recipeAuthService = new RecipeAuthorizationService();

            // Inizializza collezioni vuote (non null)
            Recipes = new ObservableCollection<RecipeItem>();
            InspectionItems = new ObservableCollection<InspectionItem>();
            EjectionItems = new ObservableCollection<InspectionItem>();
            TopThresholdItems = new ObservableCollection<InspectionThresholdItem>();
            SideThresholdItems = new ObservableCollection<InspectionThresholdItem>();
            RecipeMultiShotModes = new List<RecipeMultiShotModeOption>
            {
                new RecipeMultiShotModeOption(
                    RecipeMachineRuntimeResolver.MultiShotModeMachine,
                    GetMessage("Sub_entry_RecipeMultiShotModeMachine", "Use machine setting")),
                new RecipeMultiShotModeOption(
                    RecipeMachineRuntimeResolver.MultiShotModeEnabled,
                    GetMessage("Sub_entry_RecipeMultiShotModeEnabled", "Enabled for this recipe")),
                new RecipeMultiShotModeOption(
                    RecipeMachineRuntimeResolver.MultiShotModeDisabled,
                    GetMessage("Sub_entry_RecipeMultiShotModeDisabled", "Disabled for this recipe"))
            }.AsReadOnly();
            RefreshRecipeMachineRuntimeOverview();

            // Inizializzazione dei comandi - usare le proprietà invece di chiamare metodi async
            LoadRecipeCommand = new RelayCommand(async _ => await LoadSelectedRecipeAsync(),
                _ => SelectedRecipe != null && _canLoadRecipeToProduction);

            DeleteRecipeCommand = new RelayCommand(async _ => await DeleteSelectedRecipeAsync(),
                _ => SelectedRecipe != null && _canDeleteRecipe);

            SaveRecipeCommand = new RelayCommand(async _ => await SaveRecipeAsync(),
                _ => CanSaveRecipeCommand);

            CopyRecipeCommand = new RelayCommand(async _ => await CopyRecipeAsync(),
                _ => SelectedRecipe != null && _canCreateRecipe);

            RefreshRecipesCommand = new RelayCommand(async _ => await LoadRecipesAsync());

            SelectImageCommand = new RelayCommand(async _ => await SelectImageAsync(),
                _ => SelectedRecipe != null && CanEditProductInfo);

            // Comando per login
            LoginCommand = new RelayCommand(_ => ShowLoginDialog());

            // Aggiorna i permessi iniziali
         // UpdatePermissionsAsync().SafeFireAndForget();
           // InitializeCommands();
        }
        // NUOVO: Metodo di inizializzazione asincrona ottimizzata
    

        // Implementazione del metodo astratto di AuthorizedViewModel
        public override async Task UpdatePermissionsAsync()
        {
            try
            {
                // Usa AuthorizationService per leggere i permessi dal database
                CanEditTolerances = await CanAccessFeatureAsync("editRecipeTolerances");
                CanEditProductInfo = await CanAccessFeatureAsync("editProductInfo");
                CanEditRecipe = await CanAccessFeatureAsync("recipeManagement");
                _canSaveRecipe = await CanAccessFeatureAsync("saveRecipe");
                _canCreateRecipe = await CanAccessFeatureAsync("createRecipe");
                _canDeleteRecipe = await CanAccessFeatureAsync("deleteRecipe");
                _canLoadRecipeToProduction = await CanAccessFeatureAsync("loadRecipeToProduction");
                _canViewRecipeDetails = await CanAccessFeatureAsync("viewRecipeDetails");
                CanEditTriggerDelay = await CanAccessFeatureAsync("editCameraTriggerDelay");
                // Imposta i permessi di base
                CanEdit = CanEditRecipe;
                CanSave = _canSaveRecipe;

                MainWindow.logger?.Info($"RecipeManager permessi aggiornati dal DB: " +
                                      $"EditTolerances={CanEditTolerances}, " +
                                      $"EditProductInfo={CanEditProductInfo}, " +
                                      $"CanEdit={CanEdit}");

                // Notifica il cambio delle proprietà
                OnPropertyChanged(nameof(CanEditTolerances));
                OnPropertyChanged(nameof(CanEditProductInfo));
                OnPropertyChanged(nameof(CanEditRecipe));
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(ShowReadOnlyWarning));
                OnPropertyChanged(nameof(CanLoadRecipeToProduction));
                OnPropertyChanged(nameof(CanDeleteRecipe));
                OnPropertyChanged(nameof(CanCreateRecipe));
                OnPropertyChanged(nameof(CanSaveRecipePermission));
                OnPropertyChanged(nameof(CanSaveRecipeCommand));
                OnPropertyChanged(nameof(RecipeReadOnlyWarningText));
                OnPropertyChanged(nameof(RecipeLoadToolTip));
                OnPropertyChanged(nameof(RecipeDeleteToolTip));
                OnPropertyChanged(nameof(RecipeSaveToolTip));
                OnPropertyChanged(nameof(RecipeDuplicateToolTip));

                // Aggiorna anche i comandi
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'aggiornamento dei permessi: {ex.Message}");
                // Fallback ai permessi basati su ruoli statici
                UpdatePermissionsFromStaticRoles();
            }
        }

        // Helper method per accedere alle feature dal database
        private async Task<bool> CanAccessFeatureAsync(string featureName)
        {
            try
            {
                return await _authService.CanAccessFeature(featureName);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel controllo permessi per '{featureName}': {ex.Message}");
                return false;
            }
        }

        // Fallback per quando il database non è disponibile.
        // Fase 0 stabilita': wrapper void + core async Task (niente async void).
        private void UpdatePermissionsFromStaticRoles()
        {
            UpdatePermissionsFromStaticRolesCoreAsync().SafeFireAndForget();
        }

        private async Task UpdatePermissionsFromStaticRolesCoreAsync()
        {
            try
            {
                CanEditTolerances = await _recipeAuthService.CanEditTolerances();
                CanEditProductInfo = await _recipeAuthService.CanEditProductInfo();
                CanEditRecipe = await _recipeAuthService.CanEditRecipe();
                _canSaveRecipe = await _recipeAuthService.CanSaveRecipe();
                _canCreateRecipe = await _recipeAuthService.CanCreateNewRecipe();
                _canDeleteRecipe = await _recipeAuthService.CanDeleteRecipe();
                _canLoadRecipeToProduction = await _recipeAuthService.CanLoadRecipeToProduction();
                _canEditTriggerDelay = await _recipeAuthService.CanEditTriggerDelay();
                _canViewRecipeDetails = true;

                CanEdit = CanEditRecipe;
                CanSave = _canSaveRecipe;

                OnPropertyChanged(nameof(CanEditTolerances));
                OnPropertyChanged(nameof(CanEditProductInfo));
                OnPropertyChanged(nameof(CanEditRecipe));
                OnPropertyChanged(nameof(ShowReadOnlyWarning));
                OnPropertyChanged(nameof(CanLoadRecipeToProduction));
                OnPropertyChanged(nameof(CanDeleteRecipe));
                OnPropertyChanged(nameof(CanCreateRecipe));
                OnPropertyChanged(nameof(CanSaveRecipePermission));
                OnPropertyChanged(nameof(CanSaveRecipeCommand));
                OnPropertyChanged(nameof(RecipeReadOnlyWarningText));
                OnPropertyChanged(nameof(RecipeLoadToolTip));
                OnPropertyChanged(nameof(RecipeDeleteToolTip));
                OnPropertyChanged(nameof(RecipeSaveToolTip));
                OnPropertyChanged(nameof(RecipeDuplicateToolTip));
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "PERMISSIONS_FALLBACK_FAILED|user={0}", UserSession.CurrentUser);
            }
        }

        // METODO per mostrare errore di autorizzazione
        private void ShowEditPermissionError(string action)
        {
            string message = $"Per {action} è necessario avere l'autorizzazione appropriata. " +
                           $"Utente attuale: {UserSession.CurrentUser} ({UserSession.CurrentRole})";

            new SystemNotificationWindow("Autorizzazione insufficiente", message, NotificationSeverity.Warning).ShowDialog();
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        /// <summary>
        /// Rebuilds the read-only global/effective preview from the saved machine file.
        /// The preview never changes machine configuration or production runtime state.
        /// </summary>
        public void RefreshRecipeMachineRuntimeOverview()
        {
            MachineRuntimeConfiguration machineConfiguration = null;
            try
            {
                machineConfiguration = new MachineConfigurationService().Load();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, "RECIPE_MACHINE_OVERVIEW_LOAD_FAILED");
            }

            LegacyRightMultiShotRoleLabel = string.Equals(
                machineConfiguration?.MachineMultiShotTrigger?.Right?.CameraRole,
                "Rear", StringComparison.OrdinalIgnoreCase)
                ? GetMessage("Sub_entry_RecipeMultiShotLegacyRear", "REAR (legacy Right profile: check output)")
                : "RIGHT";

            MachineRuntimeAdjustmentOverview = RecipeMachineRuntimeOverview.Create(
                machineConfiguration,
                CurrentRecipeData,
                GetMessage);
        }

        private string _legacyRightMultiShotRoleLabel = "RIGHT";
        public string LegacyRightMultiShotRoleLabel
        {
            get => _legacyRightMultiShotRoleLabel;
            private set
            {
                if (_legacyRightMultiShotRoleLabel == value) return;
                _legacyRightMultiShotRoleLabel = value;
                OnPropertyChanged();
            }
        }

        public void RefreshRecipeViewAssignments()
        {
            UpdateInspectionItems();
            OnPropertyChanged(nameof(TopThresholdsViewLabel));
            OnPropertyChanged(nameof(SideThresholdsViewLabel));
        }

        private bool ValidateRecipeMachineRuntimeAdjustments()
        {
            EnsureRecipeRuntimeExtensions(CurrentRecipeData);
            var machineConfiguration = new MachineConfigurationService().Load();
            var resolution = RecipeMachineRuntimeResolver.Resolve(machineConfiguration, CurrentRecipeData);

            if (resolution.Warnings.Count > 0)
            {
                MainWindow.logger?.Warn(
                    "RECIPE_MACHINE_ADJUSTMENT_WARNING|recipe={0}|details={1}",
                    SelectedRecipe?.Name,
                    string.Join(" | ", resolution.Warnings));
            }

            if (resolution.IsValid)
            {
                return true;
            }

            string details = string.Join(Environment.NewLine, resolution.Errors.Select(error => "- " + error));
            MainWindow.logger?.Error(
                "RECIPE_MACHINE_ADJUSTMENT_INVALID|recipe={0}|details={1}",
                SelectedRecipe?.Name,
                string.Join(" | ", resolution.Errors));
            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                NLog.LogLevel.Error,
                "RECIPE_MACHINE_ADJUSTMENT_INVALID",
                "Recipe",
                "Recipe camera position or MultiShot corrections are invalid",
                nameof(RecipeManagerViewModel),
                $"Recipe={SelectedRecipe?.Name}; {string.Join(" | ", resolution.Errors)}");

            new SystemNotificationWindow(
                GetMessage("Sub_entry_RecipeRuntimeAdjustmentsInvalidTitle", "Invalid recipe camera corrections"),
                GetMessage(
                    "Sub_entry_RecipeRuntimeAdjustmentsInvalidMessage",
                    "Correct the following camera position or MultiShot values before saving:") +
                Environment.NewLine + Environment.NewLine + details,
                NotificationSeverity.Error).ShowDialog();
            return false;
        }

        private bool ValidateTopThreeDProfileRecipeSettings()
        {
            EnsureRecipeRuntimeExtensions(CurrentRecipeData);
            RecipeParamTop3D profile = CurrentRecipeData?.recipeParamTop3D;
            if (profile == null)
            {
                return true;
            }

            double minimumValidRatio = profile.ThreeDHeightMinimumValidPixelRatio;
            if (double.IsNaN(minimumValidRatio) ||
                double.IsInfinity(minimumValidRatio) ||
                minimumValidRatio < 0 ||
                minimumValidRatio > 1)
            {
                new SystemNotificationWindow(
                    GetMessage("Sub_entry_Top3DProfileValidationTitle", "Invalid Top3D profile settings"),
                    GetMessage(
                        "Sub_entry_Top3DProfileValidationRatio",
                        "Minimum valid pixels must be between 0 and 100 percent."),
                    NotificationSeverity.Error).ShowDialog();
                return false;
            }

            double maximumBulge = profile.ThreeDHeightMaximumBulge;
            if (double.IsNaN(maximumBulge) ||
                double.IsInfinity(maximumBulge) ||
                maximumBulge < 0)
            {
                new SystemNotificationWindow(
                    GetMessage("Sub_entry_Top3DProfileValidationTitle", "Invalid Top3D profile settings"),
                    GetMessage(
                        "Sub_entry_Top3DProfileValidationBulge",
                        "Maximum profile bulge must be a finite value greater than or equal to zero."),
                    NotificationSeverity.Error).ShowDialog();
                return false;
            }

            return true;
        }

        private static QtisVisionPanel.OperationProgressWindow CreateProgressWindow(string title, string status, string detail, int progress)
        {
            var window = ServiceLocator.DialogService.CreateOperationProgressWindow();
            window.UpdateStatus(title, status, detail, progress);
            window.Show();
            return window;
        }

        private static void UpdateProgressWindow(QtisVisionPanel.OperationProgressWindow window, string title, string status, string detail, int progress)
        {
            window?.UpdateStatus(title, status, detail, progress);
        }

        private static async Task CloseProgressWindowAsync(QtisVisionPanel.OperationProgressWindow window, int delayMs = 120)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                if (window.Dispatcher.CheckAccess())
                {
                    window.Close();
                }
                else
                {
                    await window.Dispatcher.InvokeAsync(window.Close);
                }
            }
            catch
            {
            }
        }

        // METODO modificato per selezionare immagine
        private async Task SelectImageAsync()
        {
            if (SelectedRecipe == null) return;

            // Controlla i permessi prima di procedere
            if (!CanEditProductInfo)
            {
                ShowEditPermissionError("modificare l'immagine del prodotto");
                return;
            }

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Immagini|*.jpg;*.jpeg;*.png;*.bmp|Tutti i file|*.*",
                    Title = "Seleziona immagine della ricetta"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string selectedImagePath = openFileDialog.FileName;
                    string recipeFolder = Path.GetDirectoryName(SelectedRecipe.XmlFilePath);
                    string originalExtension = Path.GetExtension(selectedImagePath);
                    string recipeImageFileName = $"{SelectedRecipe.Name}{originalExtension}";
                    string destinationPath = Path.Combine(recipeFolder, recipeImageFileName);

                    // 1. RILASCIA ESPLICITAMENTE L'IMMAGINE CORRENTE
                    ReleaseImageLock();

                    // 2. Gestisci il vecchio file SOLO se esiste ed è diverso dal nuovo
                    if (!string.IsNullOrEmpty(CurrentRecipeData.general_Info.Product_Image))
                    {
                        string oldImageFileName = CurrentRecipeData.general_Info.Product_Image;
                        string oldImagePath = Path.Combine(recipeFolder, oldImageFileName);

                        // Se il vecchio file è diverso dal nuovo e esiste
                        if (oldImageFileName != recipeImageFileName && File.Exists(oldImagePath))
                        {
                            try
                            {
                                // Attendi un momento per permettere al sistema di rilasciare il file
                                await Task.Delay(100);

                                // Prova a cancellare con più tentativi
                                bool deleted = await TryDeleteFileAsync(oldImagePath, 3);
                                if (!deleted)
                                {
                                    // Se non riesci a cancellare, rinomina invece di cancellare
                                    string renamedPath = oldImagePath + ".old";
                                    if (File.Exists(renamedPath))
                                        File.Delete(renamedPath);
                                    File.Move(oldImagePath, renamedPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                MainWindow.logger?.Warn($"Non è stato possibile eliminare il file vecchio: {ex.Message}");
                                // Continua comunque, non bloccare l'operazione
                            }
                        }
                    }

                    // 3. Copia la nuova immagine
                    File.Copy(selectedImagePath, destinationPath, true);

                    // 4. Aggiorna le proprietà
                    CurrentRecipeData.general_Info.Product_Image = recipeImageFileName;
                    SelectedRecipe.ImagePath = destinationPath;

                    // 5. Notifica i cambiamenti in modo esplicito
                    OnPropertyChanged(nameof(CurrentRecipeData));
                    OnPropertyChanged(nameof(SelectedRecipe));

                    // 6. Forza il refresh dell'interfaccia
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Aggiorna i binding
                        CommandManager.InvalidateRequerySuggested();
                    });

                    IsModified = true;

                    MainWindow.logger?.Info($"Immagine salvata: {recipeImageFileName}");

                    new SystemNotificationWindow("Successo", $"Immagine salvata come: {recipeImageFileName}", NotificationSeverity.Info).ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nella selezione dell'immagine: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nella selezione dell'immagine: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        // Metodo per rilasciare il lock sull'immagine
        private void ReleaseImageLock()
        {
            try
            {
                // 1. Imposta le proprietà a null
                SelectedRecipe.ImagePath = null;

                // 2. Notifica il cambio
                OnPropertyChanged(nameof(SelectedRecipe));

                // Fase 0 (step image-locking): il GC.Collect che era qui e' stato RIMOSSO.
                // Verificato che tutte le immagini ricetta sono caricate esclusivamente via
                // RecipeImagePathConverter (ReadAllBytes -> MemoryStream -> BitmapCacheOption.OnLoad
                // -> Freeze) e dal preload in App.xaml.cs con lo stesso pattern: la UI non tiene
                // MAI un handle sul file, quindi non c'era alcun lock da rilasciare via GC.
                // I retry di TryDeleteFileAsync restano come rete per lock transitori (es. antivirus).
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore nel rilascio dell'immagine: {ex.Message}");
            }
        }

        // Metodo per provare a cancellare un file con ritentativi
        private async Task<bool> TryDeleteFileAsync(string filePath, int maxRetries)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        return true;
                    }
                    return false;
                }
                catch
                {
                    if (i < maxRetries - 1)
                    {
                        await Task.Delay(100 * (i + 1)); // Attendi progressivamente di più
                    }
                }
            }
            return false;
        }

        private string ResolveImagePath(string imagePath, string xmlFilePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            // Se il percorso è assoluto e il file esiste
            if (Path.IsPathRooted(imagePath) && File.Exists(imagePath))
                return imagePath;

            // Altrimenti prova come percorso relativo alla cartella della ricetta
            var recipeFolder = Path.GetDirectoryName(xmlFilePath);
            var relativePath = Path.Combine(recipeFolder, imagePath);

            if (File.Exists(relativePath))
                return relativePath;

            // Se non trovata con il percorso fornito, prova con il nome della ricetta
            var recipeName = Path.GetFileNameWithoutExtension(xmlFilePath);
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

            foreach (var ext in extensions)
            {
                var recipeNamedPath = Path.Combine(recipeFolder, $"{recipeName}{ext}");
                if (File.Exists(recipeNamedPath))
                    return recipeNamedPath;
            }

            // Se non trovata, cerca nella cartella immagini globale
            var config = MainWindow.configManager.Config.Configuration;
            var globalImagePath = Path.Combine(config.Recipe_Folder, Path.GetFileName(imagePath));

            if (File.Exists(globalImagePath))
                return globalImagePath;

            return null;
        }

        // Aggiungi questo metodo per caricare le ricette quando la view viene aperta
        public async Task InitializeAsync()
        {
            if (_isLoaded) return;

            try
            {
                // Carica permessi prima (veloce)
                await UpdatePermissionsAsync();

                // Carica ricette in background
                await LoadRecipesAsync();

                _isLoaded = true;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore inizializzazione: {ex.Message}");
            }
        }

        public async Task RefreshProductionRecipeOrderAsync()
        {
            // Called after external recipe changes, including OPC UA/MES. The
            // operator list must immediately promote the active production
            // recipe without requiring a manual page reload.
            try
            {
                if (Recipes == null || Recipes.Count == 0)
                {
                    await LoadRecipesAsync();
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(UpdateProductionRecipeStatusAndOrder);
                }
                else
                {
                    UpdateProductionRecipeStatusAndOrder();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Recipe manager production refresh failed: {ex.Message}");
            }
        }

        private void UpdateProductionRecipeStatusAndOrder()
        {
            // Re-read the runtime recipe name from Config.xml-backed state, then
            // mark exactly one list item as InProduction.
            string currentProductionRecipeName = GetCurrentProductionRecipe();
            RecipeItem productionRecipe = null;

            foreach (var recipe in Recipes)
            {
                if (IsProductionRecipe(recipe.Name, currentProductionRecipeName))
                {
                    recipe.Status = RecipeStatus.InProduction;
                    productionRecipe = recipe;
                }
                else if (recipe.Status == RecipeStatus.InProduction)
                {
                    recipe.Status = RecipeStatus.Normal;
                }
            }

            ProductionRecipe = productionRecipe;
            ReorderRecipesForOperator();
            OnPropertyChanged(nameof(RecipeLoadToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Ispezioni abilitabili raggruppate per vista camera che le esegue, secondo la
        /// matrice configurata in Inspection Configuration.
        /// </summary>
        public ObservableCollection<RecipeInspectionViewGroup> InspectionViewGroups { get; } =
            new ObservableCollection<RecipeInspectionViewGroup>();

        /// <summary>Stessa proiezione per gli scarti.</summary>
        public ObservableCollection<RecipeInspectionViewGroup> EjectionViewGroups { get; } =
            new ObservableCollection<RecipeInspectionViewGroup>();

        public bool HasInspectionViewGroups => InspectionViewGroups.Count > 0;

        /// <summary>
        /// Viste che applicano le tolleranze della sezione TOP (logo, alette, forma top).
        /// Mostrata come sottotitolo della sezione: la tolleranza resta un valore unico di
        /// ricetta, ma l'operatore vede da quale camera viene effettivamente usata.
        /// </summary>
        public string TopThresholdsViewLabel =>
            DescribeViewsForFeatures("logo", "OpenFlaps", "ShapeTop", "SurfaceCheck");

        /// <summary>Viste che applicano le tolleranze della sezione SIDE (altezza, sigillatura, forma side).</summary>
        public string SideThresholdsViewLabel =>
            DescribeViewsForFeatures("Height", "Side_sealing", "ShapeSide");

        /// <summary>
        /// Unione ordinata delle viste che eseguono almeno una delle ispezioni indicate.
        /// </summary>
        private string DescribeViewsForFeatures(params string[] propertyNames)
        {
            var matrix = ResolveCurrentRecipeViewMatrix();
            if (matrix == null || propertyNames == null || propertyNames.Length == 0)
            {
                return UnassignedViewsLabel;
            }

            var performing = new List<string>();
            foreach (string role in DataManage.CameraInspectionMatrix.SupportedRoles)
            {
                if (!HasConfiguredCameraRole(role) || !IsCameraEnabledForCurrentRecipe(role))
                {
                    continue;
                }

                if (propertyNames.Any(name => matrix.IsApplicable(role, name)))
                {
                    performing.Add(DescribeViewRole(role));
                }
            }

            return performing.Count == 0
                ? UnassignedViewsLabel
                : string.Join(" / ", performing);
        }

        private void UpdateInspectionItems()
        {
            InspectionItems.Clear();
            EjectionItems.Clear();

            if (CurrentRecipeData == null)
                return;

            if (CurrentRecipeData.inspectionStatus == null)
            {
                CurrentRecipeData.inspectionStatus = InspectionStatus.FromEjectionStatus(CurrentRecipeData.ejectionStatus);
            }

            if (CurrentRecipeData.ejectionStatus == null)
            {
                CurrentRecipeData.ejectionStatus = new EjectionStatus();
            }

            var inspectionStatus = CurrentRecipeData.inspectionStatus;
            var ejectionStatus = CurrentRecipeData.ejectionStatus;

            if (HasTopCameraConfigured)
            {
                AddInspectionPair("Logo", "logo", inspectionStatus.logo, ejectionStatus.logo);
                AddInspectionPair("Centratura Stampa", "Print_centering", inspectionStatus.Print_centering, ejectionStatus.Print_centering);
                AddInspectionPair("Alette Aperte", "OpenFlaps", inspectionStatus.OpenFlaps, ejectionStatus.OpenFlaps);
                AddInspectionPair("Controllo Superficie", "SurfaceCheck", inspectionStatus.SurfaceCheck, ejectionStatus.SurfaceCheck);
                AddInspectionPair("Forma TOP", "ShapeTop", inspectionStatus.ShapeTop, ejectionStatus.ShapeTop);
            }

            if (HasSideCameraConfigured || HasLeftCameraConfigured ||
                HasRightCameraConfigured || HasRearCameraConfigured)
            {
                if (HasSideCameraConfigured)
                {
                    AddInspectionPair("Altezza", "Height", inspectionStatus.Height, ejectionStatus.Height);
                }

                AddInspectionPair("Sigillatura Laterale", "Side_sealing", inspectionStatus.Side_sealing, ejectionStatus.Side_sealing);

                if (HasSideCameraConfigured || HasRightCameraConfigured || HasRearCameraConfigured)
                {
                    AddInspectionPair("Forma SIDE", "ShapeSide", inspectionStatus.ShapeSide, ejectionStatus.ShapeSide);
                }
            }

            if (CanShowFrontTraceabilitySection)
            {
                AddInspectionPair(
                    ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_FrontTraceability", "Front traceability"),
                    "FrontTraceability",
                    inspectionStatus.FrontTraceability,
                    ejectionStatus.FrontTraceability);
            }

            if (CanShowThreeDSection)
            {
                AddInspectionPair(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDHeight", "3D height"),
                    "ThreeDHeight",
                    inspectionStatus.ThreeDHeight,
                    ejectionStatus.ThreeDHeight);
                AddInspectionPair(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDWidth", "3D width"),
                    "ThreeDWidth",
                    inspectionStatus.ThreeDWidth,
                    ejectionStatus.ThreeDWidth);
                AddInspectionPair(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDLength", "3D length"),
                    "ThreeDLength",
                    inspectionStatus.ThreeDLength,
                    ejectionStatus.ThreeDLength);
            }

            if (HasBottomCameraConfigured)
            {
                AddInspectionPair(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_BottomSealing", "Bottom sealing"),
                    "BottomSealing",
                    inspectionStatus.BottomSealing,
                    ejectionStatus.BottomSealing);
                AddInspectionPair(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_TrappedPaper", "Trapped paper"),
                    "TrappedPaper",
                    inspectionStatus.TrappedPaper,
                    ejectionStatus.TrappedPaper);
            }

            if (HasTopCameraConfigured || HasTop3DConfigured ||
                HasSideCameraConfigured || HasLeftCameraConfigured ||
                HasFrontCameraConfigured || HasRightCameraConfigured ||
                HasRearCameraConfigured || HasBottomCameraConfigured)
            {
                AddInspectionPair(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiClassification", "AI classification"),
                    "AIClassification",
                    inspectionStatus.AIClassification,
                    ejectionStatus.AIClassification);
            }

            foreach (var item in InspectionItems)
            {
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(InspectionItem.IsEnabled))
                    {
                        UpdateInspectionStatusFromInspectionItem(item);
                    }
                };
            }

            foreach (var item in EjectionItems)
            {
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(InspectionItem.IsEnabled))
                    {
                        UpdateEjectionStatusFromInspectionItem(item);
                    }
                };
            }

            // Viste raggruppate per la UI. Le collezioni piatte sopra restano la sorgente di
            // verita' per write-back e salvataggio: qui si costruisce solo una proiezione.
            RebuildViewGroups(InspectionItems, InspectionViewGroups);
            RebuildViewGroups(EjectionItems, EjectionViewGroups);
            OnPropertyChanged(nameof(HasInspectionViewGroups));
        }

        private void AddInspectionPair(string name, string propertyName, bool inspectionEnabled, bool rejectEnabled)
        {
            string views = DescribePerformingViews(propertyName);

            InspectionItems.Add(new InspectionItem
            {
                Name = name,
                IsEnabled = inspectionEnabled,
                PropertyName = propertyName,
                CameraViews = views
            });

            EjectionItems.Add(new InspectionItem
            {
                Name = name,
                IsEnabled = rejectEnabled,
                PropertyName = propertyName,
                CameraViews = views
            });
        }

        /// <summary>
        /// Viste realmente presenti sulla macchina che, secondo la matrice configurata in
        /// Inspection Configuration, eseguono questa ispezione. Restituisce
        /// <see cref="UnassignedViewsLabel"/> quando nessuna vista la esegue: in quel caso la
        /// spunta resta visibile ma raggruppata a parte, cosi' l'operatore vede subito che il
        /// controllo e' abilitato in ricetta ma nessuna camera lo applica.
        /// </summary>
        private string DescribePerformingViews(string propertyName)
        {
            var matrix = ResolveCurrentRecipeViewMatrix();
            if (matrix == null)
            {
                return UnassignedViewsLabel;
            }

            var performing = new List<string>();
            foreach (string role in DataManage.CameraInspectionMatrix.SupportedRoles)
            {
                if (!HasConfiguredCameraRole(role) || !IsCameraEnabledForCurrentRecipe(role))
                {
                    continue;
                }

                if (matrix.IsApplicable(role, propertyName))
                {
                    performing.Add(DescribeViewRole(role));
                }
            }

            return performing.Count == 0
                ? UnassignedViewsLabel
                : string.Join(" / ", performing);
        }

        private const string UnassignedViewsLabel =
            "Nessuna camera la esegue (disattivata per tutte le camere in Inspection Configuration)";

        // Nome del job VPP (SIDE, REAR, ...), non il ruolo fisico interno left/right.
        private static string DescribeViewRole(string role)
        {
            return CameraConfigurationHelper.GetCameraRoleDisplayLabel(role);
        }

        private DataManage.CameraInspectionMatrix ResolveCurrentRecipeViewMatrix()
        {
            if (DataManage.RecipeInspectionViewConfigurationMapper.TryBuild(
                    CurrentRecipeData,
                    out DataManage.CameraInspectionMatrix recipeMatrix,
                    out DataManage.CameraClassificationPolicy _,
                    !HasRightCameraConfigured && HasRearCameraConfigured))
            {
                return recipeMatrix;
            }

            return ServiceLocator.InspectionConfigService?.MachineViewMatrix ??
                   new DataManage.CameraInspectionMatrix();
        }

        private bool IsCameraEnabledForCurrentRecipe(string role)
        {
            RecipeParameters.RecipeCameraTriggerAdjustments triggers =
                CurrentRecipeData?.machineRuntimeAdjustments?.CameraTriggers;
            if (triggers == null)
            {
                return true;
            }

            string mode;
            switch (DataManage.CameraInspectionMatrix.NormalizeRole(role))
            {
                case "top":
                case "top3d": mode = triggers.Top; break;
                case "left": mode = ResolvePhysicalCameraMode(triggers.Left, triggers.Side); break;
                case "front": mode = triggers.Front; break;
                case "right": mode = triggers.Right; break;
                case "rear": mode = HasRightCameraConfigured
                    ? triggers.Rear
                    : ResolvePhysicalCameraMode(triggers.Rear, triggers.Right); break;
                case "bottom": mode = triggers.Bottom; break;
                default: return true;
            }

            return !string.Equals(
                mode,
                RecipeMachineRuntimeResolver.MultiShotModeDisabled,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Raggruppa le spunte per vista camera, preservando l'ordine di inserimento delle
        /// ispezioni. Le collezioni piatte restano invariate per tutti i consumatori
        /// esistenti (write-back, salvataggio, statistiche).
        /// </summary>
        private static void RebuildViewGroups(
            IEnumerable<InspectionItem> items,
            ObservableCollection<RecipeInspectionViewGroup> target)
        {
            target.Clear();

            foreach (var item in items)
            {
                string key = string.IsNullOrWhiteSpace(item.CameraViews)
                    ? UnassignedViewsLabel
                    : item.CameraViews;

                var group = target.FirstOrDefault(g =>
                    string.Equals(g.ViewName, key, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    group = new RecipeInspectionViewGroup(key);
                    target.Add(group);
                }

                group.Items.Add(item);
            }
        }

        private void UpdateInspectionStatusFromInspectionItem(InspectionItem item)
        {
            if (CurrentRecipeData?.inspectionStatus == null)
                return;

            bool isEnabled = item.IsEnabled;
            switch (item.PropertyName)
            {
                case "logo":
                    CurrentRecipeData.inspectionStatus.logo = isEnabled;
                    UpdateThresholdEnabled("Logo", isEnabled);
                    break;
                case "Print_centering":
                    CurrentRecipeData.inspectionStatus.Print_centering = isEnabled;
                    UpdateThresholdEnabled("Print Centering", isEnabled);
                    break;
                case "OpenFlaps":
                    CurrentRecipeData.inspectionStatus.OpenFlaps = isEnabled;
                    UpdateThresholdEnabled("Open Flaps", isEnabled);
                    break;
                case "SurfaceCheck":
                    CurrentRecipeData.inspectionStatus.SurfaceCheck = isEnabled;
                    break;
                case "Height":
                    CurrentRecipeData.inspectionStatus.Height = isEnabled;
                    UpdateThresholdEnabled("Height", isEnabled);
                    break;
                case "Side_sealing":
                    CurrentRecipeData.inspectionStatus.Side_sealing = isEnabled;
                    UpdateThresholdEnabled("Sealing", isEnabled);
                    break;
                case "ShapeTop":
                    CurrentRecipeData.inspectionStatus.ShapeTop = isEnabled;
                    UpdateThresholdEnabled("Shape", isEnabled);
                    break;
                case "ShapeSide":
                    CurrentRecipeData.inspectionStatus.ShapeSide = isEnabled;
                    break;
                case "FrontTraceability":
                    CurrentRecipeData.inspectionStatus.FrontTraceability = isEnabled;
                    break;
                case "ThreeDHeight":
                    CurrentRecipeData.inspectionStatus.ThreeDHeight = isEnabled;
                    break;
                case "ThreeDWidth":
                    CurrentRecipeData.inspectionStatus.ThreeDWidth = isEnabled;
                    break;
                case "ThreeDLength":
                    CurrentRecipeData.inspectionStatus.ThreeDLength = isEnabled;
                    break;
                case "BottomSealing":
                    CurrentRecipeData.inspectionStatus.BottomSealing = isEnabled;
                    break;
                case "TrappedPaper":
                    CurrentRecipeData.inspectionStatus.TrappedPaper = isEnabled;
                    break;
                case "AIClassification":
                    CurrentRecipeData.inspectionStatus.AIClassification = isEnabled;
                    break;
            }

            // If an inspection is disabled entirely, it cannot stay enabled as a
            // reject trigger. Keep the two recipe sections coherent automatically.
            if (!isEnabled)
            {
                var linkedEjectionItem = EjectionItems.FirstOrDefault(current => current.PropertyName == item.PropertyName);
                if (linkedEjectionItem != null && linkedEjectionItem.IsEnabled)
                {
                    linkedEjectionItem.IsEnabled = false;
                }
            }

            IsModified = true;
            UpdateThresholdItems();
        }

        private void UpdateEjectionStatusFromInspectionItem(InspectionItem item)
        {
            if (CurrentRecipeData?.ejectionStatus == null)
                return;

            if (item.IsEnabled && CurrentRecipeData.inspectionStatus != null)
            {
                var linkedInspectionItem = InspectionItems.FirstOrDefault(current => current.PropertyName == item.PropertyName);
                if (linkedInspectionItem != null && !linkedInspectionItem.IsEnabled)
                {
                    linkedInspectionItem.IsEnabled = true;
                }
            }

            bool isEnabled = item.IsEnabled;
            switch (item.PropertyName)
            {
                case "logo":
                    CurrentRecipeData.ejectionStatus.logo = item.IsEnabled;
                    UpdateThresholdEnabled("Logo", isEnabled);
                    break;
                case "Print_centering":
                    CurrentRecipeData.ejectionStatus.Print_centering = item.IsEnabled;
                    UpdateThresholdEnabled("Print Centering", isEnabled);
                    break;
                case "OpenFlaps":
                    CurrentRecipeData.ejectionStatus.OpenFlaps = item.IsEnabled;
                    UpdateThresholdEnabled("Open Flaps", isEnabled);
                    break;
                case "SurfaceCheck":
                    CurrentRecipeData.ejectionStatus.SurfaceCheck = item.IsEnabled;
                    break;
                case "Height":
                    CurrentRecipeData.ejectionStatus.Height = item.IsEnabled;
                    UpdateThresholdEnabled("Height", isEnabled);
                    break;
                case "Side_sealing":
                    CurrentRecipeData.ejectionStatus.Side_sealing = item.IsEnabled;
                    UpdateThresholdEnabled("Sealing", isEnabled);
                    break;
                case "ShapeTop":
                    CurrentRecipeData.ejectionStatus.ShapeTop = item.IsEnabled;
                    UpdateThresholdEnabled("Shape", isEnabled);
                    break;
                case "ShapeSide":
                    CurrentRecipeData.ejectionStatus.ShapeSide = item.IsEnabled;
                    break;
                case "FrontTraceability":
                    CurrentRecipeData.ejectionStatus.FrontTraceability = item.IsEnabled;
                    break;
                case "ThreeDHeight":
                    CurrentRecipeData.ejectionStatus.ThreeDHeight = item.IsEnabled;
                    break;
                case "ThreeDWidth":
                    CurrentRecipeData.ejectionStatus.ThreeDWidth = item.IsEnabled;
                    break;
                case "ThreeDLength":
                    CurrentRecipeData.ejectionStatus.ThreeDLength = item.IsEnabled;
                    break;
                case "BottomSealing":
                    CurrentRecipeData.ejectionStatus.BottomSealing = item.IsEnabled;
                    break;
                case "TrappedPaper":
                    CurrentRecipeData.ejectionStatus.TrappedPaper = item.IsEnabled;
                    break;
                case "AIClassification":
                    CurrentRecipeData.ejectionStatus.AIClassification = item.IsEnabled;
                    break;
            }

            IsModified = true;
            UpdateThresholdItems();
        }

        private bool HasConfiguredCameraRole(string role)
        {
            try
            {
                // The selected recipe can differ from the VPP currently running in the
                // machine. Its explicit view profile must remain editable in that case.
                var recipeViews = CurrentRecipeData?.inspectionViewConfiguration?.Views;
                if (CurrentRecipeData?.inspectionViewConfiguration?.IsConfigured == true &&
                    recipeViews != null && recipeViews.Any(view => view != null &&
                        string.Equals(
                            CameraConfigurationHelper.NormalizePhysicalCameraRole(view.CameraRole),
                            CameraConfigurationHelper.NormalizePhysicalCameraRole(role),
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (MainWindow.JobRoleMapping != null && MainWindow.JobRoleMapping.Count > 0)
                {
                    return CameraConfigurationHelper.HasCameraRole(MainWindow.JobRoleMapping, role);
                }

                string cameraConfigPath = Path.Combine(
                    MainWindow.configManager.Config.Configuration.Recipe_Folder,
                    "CameraConfig.xml");

                var roles = CameraConfigurationHelper.BuildRoleMapping(cameraConfigPath, MainWindow.JobMapping);
                return CameraConfigurationHelper.HasCameraRole(roles, role);
            }
            catch
            {
                return false;
            }
        }

        private void NotifyRecipeMachineAdjustmentVisibility()
        {
            OnPropertyChanged(nameof(HasTopCameraConfigured));
            OnPropertyChanged(nameof(HasSideCameraConfigured));
            OnPropertyChanged(nameof(HasLeftCameraConfigured));
            OnPropertyChanged(nameof(HasFrontCameraConfigured));
            OnPropertyChanged(nameof(HasRightCameraConfigured));
            OnPropertyChanged(nameof(HasRearCameraConfigured));
            OnPropertyChanged(nameof(LeftCameraTriggerLabel));
            OnPropertyChanged(nameof(HasBottomCameraConfigured));
            OnPropertyChanged(nameof(CanShowTopPositionAdjustment));
            OnPropertyChanged(nameof(CanShowSideLeftPositionAdjustments));
            OnPropertyChanged(nameof(CanShowRightRearPositionAdjustments));
            OnPropertyChanged(nameof(CanShowSideLeftMultiShotAdjustment));
            OnPropertyChanged(nameof(CanShowRightRearMultiShotAdjustment));
            OnPropertyChanged(nameof(CanShowBottomMultiShotAdjustment));
            OnPropertyChanged(nameof(CanShowSideInspectionThresholds));

            // Etichette delle viste che applicano le tolleranze: dipendono dagli stessi
            // predicati di presenza camera, quindi vanno rinfrescate insieme a loro.
            OnPropertyChanged(nameof(TopThresholdsViewLabel));
            OnPropertyChanged(nameof(SideThresholdsViewLabel));
        }

        public void RefreshCameraConfigurationFromActiveVpp()
        {
            NotifyRecipeMachineAdjustmentVisibility();
            OnPropertyChanged(nameof(RightCameraTriggerMode));
            OnPropertyChanged(nameof(RearCameraTriggerMode));
            OnPropertyChanged(nameof(RightCameraOffsetMm));
            OnPropertyChanged(nameof(RearCameraOffsetMm));
            RefreshRecipeMachineRuntimeOverview();
        }

        private static void EnsureRecipeRuntimeExtensions(RecipeParameters.RecipeData recipeData)
        {
            if (recipeData == null)
            {
                return;
            }

            if (recipeData.cameraSetting == null)
            {
                recipeData.cameraSetting = new CameraSetting();
            }

            if (recipeData.machineRuntimeAdjustments == null)
            {
                recipeData.machineRuntimeAdjustments = new RecipeParameters.RecipeMachineRuntimeAdjustments();
            }

            if (recipeData.machineRuntimeAdjustments.CameraPositions == null)
            {
                recipeData.machineRuntimeAdjustments.CameraPositions = new RecipeParameters.RecipeCameraPositionAdjustments();
            }

            if (recipeData.machineRuntimeAdjustments.CameraTriggers == null)
            {
                recipeData.machineRuntimeAdjustments.CameraTriggers = new RecipeParameters.RecipeCameraTriggerAdjustments();
            }

            if (recipeData.machineRuntimeAdjustments.MultiShot == null)
            {
                recipeData.machineRuntimeAdjustments.MultiShot = new RecipeParameters.RecipeMultiShotAdjustments();
            }

            if (recipeData.machineRuntimeAdjustments.MultiShot.SideLeft == null)
            {
                recipeData.machineRuntimeAdjustments.MultiShot.SideLeft = new RecipeParameters.RecipeMultiShotAdjustment();
            }

            if (recipeData.machineRuntimeAdjustments.MultiShot.RightRear == null)
            {
                recipeData.machineRuntimeAdjustments.MultiShot.RightRear = new RecipeParameters.RecipeMultiShotAdjustment();
            }
            if (recipeData.machineRuntimeAdjustments.MultiShot.Rear == null)
            {
                recipeData.machineRuntimeAdjustments.MultiShot.Rear = new RecipeParameters.RecipeMultiShotAdjustment();
            }

            if (recipeData.machineRuntimeAdjustments.MultiShot.Bottom == null)
            {
                recipeData.machineRuntimeAdjustments.MultiShot.Bottom = new RecipeParameters.RecipeMultiShotAdjustment();
            }

            if (recipeData.inspectionViewConfiguration == null)
            {
                recipeData.inspectionViewConfiguration = new RecipeParameters.RecipeInspectionViewConfiguration();
            }

            if (recipeData.recipeParamFront == null)
            {
                recipeData.recipeParamFront = new RecipeParameters.RecipeParamFront();
            }

            if (recipeData.recipeParamTop3D == null)
            {
                recipeData.recipeParamTop3D = new RecipeParameters.RecipeParamTop3D();
            }

            MigrateLegacyThreeDParameters();

            if (recipeData.ejectionStatus == null)
            {
                recipeData.ejectionStatus = new EjectionStatus();
            }

            if (recipeData.inspectionStatus == null)
            {
                recipeData.inspectionStatus = InspectionStatus.FromEjectionStatus(recipeData.ejectionStatus);
            }

            if (recipeData.InspectionThresholds == null)
            {
                recipeData.InspectionThresholds = new InspectionThresholds();
            }

            if (recipeData.InspectionThresholds.FrontInspections == null)
            {
                recipeData.InspectionThresholds.FrontInspections = new List<InspectionThresholdItem>();
            }
        }

        private static void MigrateLegacyThreeDParameters()
        {
            // RecipeParamFront no longer carries 3D fields — they live exclusively in
            // recipeParamTop3D. Nothing to migrate.
        }

        private void UpdateThresholdEnabled(string thresholdName, bool isEnabled)
        {
            if (CurrentRecipeData?.InspectionThresholds == null)
                return;

            // Cerca nelle soglie TOP
            var topItem = CurrentRecipeData.InspectionThresholds.TopInspections
                .FirstOrDefault(i => i.Name.Equals(thresholdName, StringComparison.OrdinalIgnoreCase));
            if (topItem != null)
            {
                topItem.IsEnabled = isEnabled;
            }

            // Cerca nelle soglie SIDE
            var sideItem = CurrentRecipeData.InspectionThresholds.SideInspections
                .FirstOrDefault(i => i.Name.Equals(thresholdName, StringComparison.OrdinalIgnoreCase));
            if (sideItem != null)
            {
                sideItem.IsEnabled = isEnabled;
            }
        }
        // Loads recipe cards off the UI thread, then updates the observable
        // collection on the dispatcher so the HMI stays responsive.
        private async Task LoadRecipesAsync()
        {
            try
            {
                // Usa Task.Run per non bloccare l'UI thread
                var loadedRecipes = await Task.Run(() => LoadRecipesFromDisk());

                // Aggiorna l'ObservableCollection sul thread UI
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Recipes.Clear();
                    foreach (var recipe in SortRecipesForOperator(loadedRecipes))
                    {
                        Recipes.Add(recipe);
                    }

                    ProductionRecipe = Recipes.FirstOrDefault(recipe => recipe.Status == RecipeStatus.InProduction);
                });

                MainWindow.logger?.Info($"Caricate {loadedRecipes.Count} ricette");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore caricamento ricette: {ex.Message}");
            }
        }

        private static IEnumerable<RecipeItem> SortRecipesForOperator(IEnumerable<RecipeItem> recipes)
        {
            // Production recipe always first: this is an operator safety choice,
            // not a cosmetic sort. Remaining items keep the existing newest-id
            // order used by the recipe page.
            return (recipes ?? Enumerable.Empty<RecipeItem>())
                .OrderByDescending(recipe => recipe.Status == RecipeStatus.InProduction)
                .ThenByDescending(recipe => recipe.Id);
        }

        private void ReorderRecipesForOperator()
        {
            // Preserve current selection while rebuilding the ObservableCollection;
            // replacing the collection object would break existing XAML bindings.
            var selected = _selectedRecipe;
            var sortedRecipes = SortRecipesForOperator(Recipes).ToList();

            Recipes.Clear();
            foreach (var recipe in sortedRecipes)
            {
                Recipes.Add(recipe);
            }

            if (selected != null && Recipes.Contains(selected))
            {
                _selectedRecipe = selected;
                OnPropertyChanged(nameof(SelectedRecipe));
            }
        }

        private static bool IsProductionRecipe(string recipeName, string currentProductionRecipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName) || string.IsNullOrWhiteSpace(currentProductionRecipeName))
            {
                return false;
            }

            string candidateFileName = NormalizeRecipeFileName(recipeName);
            string productionFileName = NormalizeRecipeFileName(currentProductionRecipeName);

            return string.Equals(candidateFileName, productionFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRecipeFileName(string recipeNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(recipeNameOrPath))
            {
                return string.Empty;
            }

            string fileName = recipeNameOrPath.Trim();
            try
            {
                string extractedFileName = Path.GetFileName(fileName);
                if (!string.IsNullOrWhiteSpace(extractedFileName))
                {
                    fileName = extractedFileName;
                }
            }
            catch
            {
            }

            return Path.HasExtension(fileName)
                ? fileName
                : fileName + ".vpp";
        }

        // NUOVO: Caricamento sincrono da usare in background
        private List<RecipeItem> LoadRecipesFromDisk()
        {
            var recipes = new List<RecipeItem>();

            if (!Directory.Exists(_recipeFolder))
            {
                Directory.CreateDirectory(_recipeFolder);
                return recipes;
            }

            var vppFiles = Directory.GetFiles(_recipeFolder, "*.vpp");
            var currentProductionRecipeName = GetCurrentProductionRecipe();
            int idCounter = 1;

            foreach (var vppFile in vppFiles)
            {
                try
                {
                    var recipeItem = CreateRecipeItemOptimized(vppFile, idCounter++, currentProductionRecipeName);
                    if (recipeItem != null)
                        recipes.Add(recipeItem);
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn($"Errore caricamento ricetta {vppFile}: {ex.Message}");
                }
            }

            return recipes;
        }

        // Metodo ottimizzato per creare RecipeItem
        private RecipeItem CreateRecipeItemOptimized(string vppFile, int id, string currentProductionRecipeName)
        {
            var recipeName = Path.GetFileNameWithoutExtension(vppFile);
            var xmlFile = Path.ChangeExtension(vppFile, ".xml");

            var recipeItem = new RecipeItem
            {
                Id = id,
                Name = recipeName,
                FilePath = vppFile,
                XmlFilePath = xmlFile,
                LastModified = File.GetLastWriteTime(vppFile),
                Status = RecipeStatus.Normal,
                RecipeType = DetermineRecipeType(recipeName)
            };
            // Set initial status to Normal
            recipeItem.Status = RecipeStatus.Normal;

            // Imposta stato produzione
            if (IsProductionRecipe(recipeName, currentProductionRecipeName))
            {
                recipeItem.Status = RecipeStatus.InProduction;
            }
            // Imposta RecipeType in base al nome del file
            recipeItem.RecipeType = DetermineRecipeType(recipeName);
            // Carica XML se esiste
            if (File.Exists(xmlFile))
            {
                LoadXmlDataIntoRecipeItem(recipeItem, xmlFile, recipeName);
            }

            // Database check in background
            CheckDatabaseAsync(recipeItem, recipeName);

            return recipeItem;
        }
        // Caricamento dati XML separato per pulizia
        private void LoadXmlDataIntoRecipeItem(RecipeItem recipeItem, string xmlFile, string recipeName)
        {
            try
            {
                using (var stream = new FileStream(xmlFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream))
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RecipeParameters.RecipeData));
                    var recipeData = (RecipeParameters.RecipeData)serializer.Deserialize(reader);

                    var generalInfo = recipeData?.general_Info;
                    if (generalInfo != null)
                    {
                        recipeItem.Description = generalInfo.RecipeDescription ?? "";
                        recipeItem.ProductLength = generalInfo.Product_Length;
                        recipeItem.ProductWidth = generalInfo.Product_Width;
                        recipeItem.ProductHeight = generalInfo.Product_Height;
                        recipeItem.Dimensions = $"{recipeItem.ProductWidth}x{recipeItem.ProductLength}";

                        // Gestione immagine
                        recipeItem.ImagePath = ResolveImagePathFromData(generalInfo.Product_Image, xmlFile, recipeName, recipeData, serializer);
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"XML error for {recipeName}: {ex.Message}");
            }
        }

        // Risoluzione percorso immagine
        private string ResolveImagePathFromData(string productImage, string xmlFile, string recipeName,
            RecipeParameters.RecipeData recipeData, System.Xml.Serialization.XmlSerializer serializer)
        {
            var recipeFolder = Path.GetDirectoryName(xmlFile);

            // Prova percorso dal XML
            if (!string.IsNullOrEmpty(productImage))
            {
                var resolved = ResolveImagePath(productImage, xmlFile);
                if (resolved != null) return resolved;
            }

            // Cerca automaticamente
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            foreach (var ext in extensions)
            {
                var autoPath = Path.Combine(recipeFolder, $"{recipeName}{ext}");
                if (File.Exists(autoPath))
                {
                    // Salva nel XML per prossima volta
                    try
                    {
                        recipeData.general_Info.Product_Image = $"{recipeName}{ext}";
                        using (var writeStream = new FileStream(xmlFile, FileMode.Create))
                        {
                            serializer.Serialize(writeStream, recipeData);
                        }
                    }
                    catch { /* Ignora errore salvataggio */ }

                    return autoPath;
                }
            }

            return null;
        }

        // Check database asincrono.
        // Fase 0 stabilita': wrapper void + core async Task (niente async void).
        private void CheckDatabaseAsync(RecipeItem recipeItem, string recipeName)
        {
            CheckDatabaseCoreAsync(recipeItem, recipeName).SafeFireAndForget();
        }

        private async Task CheckDatabaseCoreAsync(RecipeItem recipeItem, string recipeName)
        {
            try
            {
                var dbId = await _dbManager.GetRicettaIdByNameAsync(recipeName);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    recipeItem.ExistsInDatabase = dbId.HasValue;
                    recipeItem.DatabaseId = dbId;
                });
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Debug(ex, "RECIPE_DB_CHECK_FAILED|recipe={0}", recipeName);
            }
        }
        private string DetermineRecipeType(string recipeName)
        {
            // Prima controlla il MachineType dalla configurazione
            var configMachineType = MainWindow.configManager?.Config?.Configuration?.MachineType ?? "Packs";
            switch (configMachineType.ToLower())
            {
                case "packs":
                    return "Ricetta pacchi";
                case "rolls":
                    return "Ricetta rotoli";
                case "bags":
                    return "Ricetta sacchi";
                case "template":
                    return "Templato";
                default:
                    // Fallback basato sul nome della ricetta
                    if (recipeName.Contains("rotoli") || recipeName.Contains("roll"))
                    {
                        return "Ricetta rotoli";
                    }
                    else if (recipeName.Contains("pacchi") || recipeName.Contains("packs"))
                    {
                        return "Ricetta pacchi";
                    }
                    else if (recipeName.Contains("templato") || recipeName.Contains("template"))
                    {
                        return "Templato";
                    }
                    else
                    {
                        return "Ricetta";
                    }
            }
        }

        private void LoadRecipeDetails(RecipeItem recipe)
        {
            try
            {
                if (File.Exists(recipe.XmlFilePath))
                {
                    using (var stream = new FileStream(recipe.XmlFilePath, FileMode.Open))
                    {
                        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RecipeParameters.RecipeData));
                        CurrentRecipeData = (RecipeParameters.RecipeData)serializer.Deserialize(stream);

                        // AGGIUNGI: Sincronizza l'altezza
                        if (CurrentRecipeData?.general_Info != null && CurrentRecipeData?.recipeParamSide != null)
                        {
                            CurrentRecipeData.recipeParamSide.Min_Heigth_value = CurrentRecipeData.general_Info.Product_Height;
                        }

                        // Inizializza le soglie se non esistono
                        InitializeThresholds();
                    }
                }
                else
                {
                    CurrentRecipeData = new RecipeParameters.RecipeData();
                    InitializeThresholds();
                }

                // Aggiorna le proprietà della ricetta selezionata
                if (recipe != null)
                {
                    recipe.ProductHeight = CurrentRecipeData.general_Info?.Product_Height ?? 0;
                }

                IsModified = false;
                UpdateThresholdItems();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error loading recipe details: {ex.Message}");
                CurrentRecipeData = new RecipeParameters.RecipeData();
                InitializeThresholds();
                UpdateThresholdItems();
            }
        }

        private void UpdateThresholdItems()
        {
            
            // Notifica che le proprietà relative alle soglie sono cambiate
            OnPropertyChanged(nameof(CurrentRecipeData));

            // Se vuoi ancora mantenere l'aggiornamento delle soglie, fai così:
            if (CurrentRecipeData?.InspectionThresholds != null)
            {
                UpdateInspectionThresholdsFromRecipeParams();
            }
        }

        private void InitializeThresholds()
        {
            // Inizializza le soglie delle dimensioni dai dati generali
            if (CurrentRecipeData.DimensionThresholds == null)
            {
                CurrentRecipeData.DimensionThresholds = new RecipeParameters.DimensionThresholds();
            }

            CurrentRecipeData.DimensionThresholds.LengthValue = CurrentRecipeData.general_Info.Product_Length;
            CurrentRecipeData.DimensionThresholds.WidthValue = CurrentRecipeData.general_Info.Product_Width;
            CurrentRecipeData.DimensionThresholds.DiameterValue = CurrentRecipeData.general_Info.Roll_Diameter;

            // Inizializza le soglie di ispezione se non esistono
            if (CurrentRecipeData.InspectionThresholds == null)
            {
                CurrentRecipeData.InspectionThresholds = new RecipeParameters.InspectionThresholds();
                CreateDefaultThresholds();
            }
        }

        private void CreateDefaultThresholds()
        {
            if (CurrentRecipeData.InspectionThresholds == null)
            {
                CurrentRecipeData.InspectionThresholds = new RecipeParameters.InspectionThresholds();
            }

            // TOP inspections - solo per compatibilità, ma non verranno usati nella UI aggiornata
            var topItems = new List<RecipeParameters.InspectionThresholdItem>();
            var sideItems = new List<RecipeParameters.InspectionThresholdItem>();
        }

        private async Task LoadSelectedRecipeAsync()
        {
            if (SelectedRecipe == null || SelectedRecipe == ProductionRecipe) return;
            // Controlla i permessi
            if (!_canLoadRecipeToProduction)
            {
                ShowEditPermissionError("caricare una nuova ricetta");
                return;
            }

            QtisVisionPanel.OperationProgressWindow progressWindow = null;

            try
            {
                progressWindow = CreateProgressWindow(
                    GetMessage("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    GetMessage("Sub_entry_OperationPreparing", "Preparing operation..."),
                    SelectedRecipe.Name,
                    10);

                var config = MainWindow.configManager.Config.Configuration;

                // Update configuration
                var previousProductionRecipe = ProductionRecipe;
                config.LastRecipe = SelectedRecipe.Name + ".vpp";
                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    GetMessage("Sub_entry_OperationUpdatingConfiguration", "Updating configuration..."),
                    config.LastRecipe,
                    25);
                await MainWindow.configManager.SaveConfigAsync();

                // Update status for all recipes
                if (previousProductionRecipe != null)
                {
                    previousProductionRecipe.Status = RecipeStatus.Normal;
                }

                // Set new production recipe
                SelectedRecipe.Status = RecipeStatus.InProduction;
                ProductionRecipe = SelectedRecipe;
                ReorderRecipesForOperator();


                if (MainWindow._isContinuousRunActive && MainWindow._cognexManager != null)
                {
                    UpdateProgressWindow(
                        progressWindow,
                        GetMessage("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                        GetMessage("Sub_entry_OperationStoppingRuntime", "Stopping continuous run..."),
                        SelectedRecipe.Name,
                        40);
                    await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync();
                    MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                }

                // Reload the new recipe in the vision system
                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    GetMessage("Sub_entry_OperationReloadingRuntime", "Reloading runtime..."),
                    SelectedRecipe.Name,
                    60);
                await MainWindow.MainView.InitializeComponentforChangeRecipe(progressWindow);

                // Reset selection status
                if (SelectedRecipe != null)
                {
                    if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
                    {
                        mainVm.TopMenuBarVM.CurrentRecipeName = SelectedRecipe.Name;
                    }
                    SelectedRecipe = null;
                }

                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationLoadingRecipeTitle", "Loading production recipe"),
                    GetMessage("Sub_entry_OperationCompleted", "Completed"),
                    config.LastRecipe,
                    100);
                await CloseProgressWindowAsync(progressWindow);
                progressWindow = null;
                MainWindow.logger?.Info($"Recipe '{config.LastRecipe}' loaded as production recipe");
                await (ServiceLocator.AuditLogService?.LogAsync(
                    "RECIPE_LOAD_PRODUCTION", UserSession.CurrentUser,
                    $"recipe={config.LastRecipe}",
                    oldValue: previousProductionRecipe?.Name) ?? Task.CompletedTask);

                new SystemNotificationWindow("Successo", $"Ricetta '{config.LastRecipe}' caricata come ricetta in produzione!", NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error loading recipe: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nel caricare la ricetta: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
            finally
            {
                await CloseProgressWindowAsync(progressWindow);
            }
        }

        private async Task DeleteSelectedRecipeAsync()
        {
            if (SelectedRecipe == null) return;

            // Controlla i permessi
            if (!_canDeleteRecipe)
            {
                ShowEditPermissionError("eliminare la ricetta");
                return;
            }

            var dlgDelete = new SystemNotificationWindow("Conferma cancellazione",
                $"Sei sicuro di voler cancellare la ricetta '{SelectedRecipe.Name}'?\n\n" +
                "Questa operazione cancellerà:\n" +
                "- Il file .vpp della ricetta\n" +
                "- Il file .xml della ricetta\n" +
                "- I record associati nel database\n\n" +
                "Questa operazione non può essere annullata!",
                NotificationSeverity.Warning, true);
            dlgDelete.ShowDialog();
            if (!dlgDelete.Confirmed)
                return;

            try
            {
                // Cancella file .vpp
                if (File.Exists(SelectedRecipe.FilePath))
                {
                    File.Delete(SelectedRecipe.FilePath);
                }

                // Cancella file .xml
                if (File.Exists(SelectedRecipe.XmlFilePath))
                {
                    File.Delete(SelectedRecipe.XmlFilePath);
                }

                // Cancella dal database se esiste
                if (SelectedRecipe.DatabaseId.HasValue)
                {
                    await Task.Run(() => _dbManager.SoftDeleteRicetta((int)SelectedRecipe.DatabaseId.Value));
                }

                // Rimuovi dalla lista
                var recipeToRemove = SelectedRecipe;
                Recipes.Remove(recipeToRemove);
                SelectedRecipe = null;
                CurrentRecipeData = null;
                InspectionItems.Clear();
                EjectionItems.Clear();

                MainWindow.logger?.Info($"Recipe '{recipeToRemove.Name}' deleted successfully");
                await (ServiceLocator.AuditLogService?.LogAsync(
                    "RECIPE_DELETE", UserSession.CurrentUser,
                    $"recipe={recipeToRemove.Name}") ?? Task.CompletedTask);

                new SystemNotificationWindow("Successo", $"Ricetta '{recipeToRemove.Name}' cancellata con successo!", NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error deleting recipe: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nel cancellare la ricetta: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        // Modifica il metodo SaveRecipeAsync per gestire la rinomina
        private async Task SaveRecipeAsync()
        {
            if (SelectedRecipe == null || CurrentRecipeData == null) return;

            // Controlla i permessi
            if (!_canSaveRecipe)
            {
                ShowEditPermissionError("salvare la ricetta");
                return;
            }

            if (!ValidateTopThreeDProfileRecipeSettings())
            {
                return;
            }

            if (!ValidateRecipeMachineRuntimeAdjustments())
            {
                return;
            }
            // Controlla se il sistema è in esecuzione
            bool wasRunning = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
            QtisVisionPanel.OperationProgressWindow progressWindow = null;
            ServiceLocator.MachineRuntimeService.AddContinuousRunHold(
                MachineRuntimeService.HoldReasonRecipeSave,
                nameof(SaveRecipeAsync));
            try
            {
                progressWindow = CreateProgressWindow(
                    GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                    GetMessage("Sub_entry_OperationPreparing", "Preparing operation..."),
                    SelectedRecipe.Name,
                    10);

                // *** FERMA L'ACQUISIZIONE SE IN CORSO ***
                if (wasRunning)
                {
                    UpdateProgressWindow(
                        progressWindow,
                        GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                        GetMessage("Sub_entry_OperationStoppingRuntime", "Stopping continuous run..."),
                        SelectedRecipe.Name,
                        20);
                    MainWindow.logger?.Info("Fermata acquisizione continua per salvataggio...");
                    await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync();
                    MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;

                    // Attendi che si fermi completamente
                    await Task.Delay(800);

                    MainWindow.logger?.Info("Acquisizione fermata, procedo con il salvataggio");
                }
                bool renamed = false;
                string newName = SelectedRecipe.Name;
                var config = MainWindow.configManager.Config.Configuration;

                // Controlla se il nome è cambiato
                if (_originalRecipeName != newName && !string.IsNullOrEmpty(newName))
                {
                    var dlgRename = new SystemNotificationWindow("Conferma rinomina",
                        $"Vuoi rinominare la ricetta da '{_originalRecipeName}' a '{newName}'?\n\n" +
                        "Questa operazione:\n" +
                        "- Rinominerà i file .vpp e .xml\n" +
                        "- Aggiornerà il nome nel database\n" +
                        "- Se questa è la ricetta in produzione, aggiornerà la configurazione",
                        NotificationSeverity.Warning, true);
                    dlgRename.ShowDialog();
                    if (dlgRename.Confirmed)
                    {
                        await RenameRecipeFilesAsync(_originalRecipeName, newName);
                        renamed = true;
                    }
                    else
                    {
                        // Ripristina il nome originale
                        SelectedRecipe.Name = _originalRecipeName;
                        OnPropertyChanged(nameof(SelectedRecipe));
                        OnPropertyChanged(nameof(Configurazione));
                        return;
                    }
                }

                
                // Sincronizza tutti i parametri prima di salvare
                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                    GetMessage("Sub_entry_OperationWritingFiles", "Writing files..."),
                    SelectedRecipe.Name,
                    40);
                SynchronizeRecipeParameters();
                // Salva i dati XML
                string xmlFilePath = renamed
                    ? Path.Combine(_recipeFolder, $"{newName}.xml")
                    : SelectedRecipe.XmlFilePath;
                // Fase 0 stabilita': AsyncRecipeParam non trattiene handle sul file XML (letture
                // con using). Il GC.Collect + delay forzati qui congelavano la UI al salvataggio
                // ricetta senza rilasciare alcun lock reale.
                MainWindow.ConfigRecipeParam = null;

                using (var stream = new FileStream(xmlFilePath, FileMode.Create))
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RecipeParameters.RecipeData));
                    serializer.Serialize(stream, CurrentRecipeData);
                }
                // *** NUOVO: Applica i trigger delay alle telecamere ***
              

                // Aggiorna le proprietà della ricetta
                SelectedRecipe.Description = CurrentRecipeData.general_Info?.RecipeDescription ?? "";
                SelectedRecipe.ProductLength = CurrentRecipeData.general_Info?.Product_Length ?? 0;
                SelectedRecipe.ProductWidth = CurrentRecipeData.general_Info?.Product_Width ?? 0;
                SelectedRecipe.ProductHeight = CurrentRecipeData.general_Info?.Product_Height ?? 0;
                SelectedRecipe.Dimensions = $"{SelectedRecipe.ProductWidth}×{SelectedRecipe.ProductLength}";
                SelectedRecipe.LastModified = DateTime.Now;

                // Aggiorna il percorso dell'immagine
                if (!string.IsNullOrEmpty(CurrentRecipeData.general_Info?.Product_Image))
                {
                    SelectedRecipe.ImagePath = ResolveImagePath(
                        CurrentRecipeData.general_Info.Product_Image,
                        SelectedRecipe.XmlFilePath);
                }

                // VERIFICA SE LA RICETTA ESISTE GIÀ NEL DATABASE
                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                    GetMessage("Sub_entry_OperationSavingDatabase", "Updating database..."),
                    newName,
                    60);
                if (!SelectedRecipe.DatabaseId.HasValue)
                {
                    await InsertNewRecipeToDatabaseAsync(newName);
                }
                else
                {
                   await UpdateExistingRecipeInDatabaseAsync(newName, renamed);
                }

                // Se la ricetta è in produzione, ricaricala
                if (SelectedRecipe.Status == RecipeStatus.InProduction)
                {
                    config.LastRecipe = $"{newName}.vpp";
                    UpdateProgressWindow(
                        progressWindow,
                        GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                        GetMessage("Sub_entry_OperationUpdatingConfiguration", "Updating configuration..."),
                        config.LastRecipe,
                        72);
                    await MainWindow.configManager.SaveConfigAsync();

                    // Ricarica subito i parametri runtime della ricetta in produzione senza rifare un cambio ricetta completo
                    if (MainWindow.MainView != null)
                    {
                        UpdateProgressWindow(
                            progressWindow,
                            GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                            GetMessage("Sub_entry_OperationReloadingRuntime", "Reloading runtime..."),
                            config.LastRecipe,
                            84);
                        await MainWindow.MainView.InitializeRecipeAsync(config.LastRecipe);
                        MainWindow.logger?.Info(
                            $"RECIPE_RUNTIME_REFRESH_OK|recipe={newName}|vpp={config.LastRecipe}|xml={xmlFilePath}");
                        ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                            NLog.LogLevel.Info,
                            "RECIPE_RUNTIME_REFRESH_OK",
                            "Recipe",
                            "Production recipe runtime parameters refreshed successfully",
                            nameof(SaveRecipeAsync),
                            $"Recipe '{newName}' reloaded after save",
                            new Dictionary<string, object>
                            {
                                { "recipe_name", newName },
                                { "vpp_file", config.LastRecipe },
                                { "xml_file", xmlFilePath }
                            });
                    }
                }

                IsModified = false;
                _originalRecipeName = newName;

                // Ricarica la lista delle ricette per mostrare le modifiche
                await LoadRecipesAsync();

                UpdateProgressWindow(
                    progressWindow,
                    GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                    GetMessage("Sub_entry_OperationCompleted", "Completed"),
                    newName,
                    100);
                await CloseProgressWindowAsync(progressWindow);
                progressWindow = null;

                MainWindow.logger?.Info($"Recipe '{newName}' saved successfully");
                await (ServiceLocator.AuditLogService?.LogAsync(
                    "RECIPE_SAVE", UserSession.CurrentUser,
                    $"recipe={newName}|in_production={SelectedRecipe.Status == RecipeStatus.InProduction}") ?? Task.CompletedTask);

                new SystemNotificationWindow("Successo",
                    $"Ricetta '{newName}' salvata con successo!" +
                    (SelectedRecipe.Status == RecipeStatus.InProduction ?
                     "\n\nLa ricetta è in produzione ed è stata ricaricata con i nuovi parametri." : ""),
                    NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error saving recipe: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nel salvare la ricetta: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
            finally
            {
                ServiceLocator.MachineRuntimeService.ReleaseContinuousRunHold(
                    MachineRuntimeService.HoldReasonRecipeSave,
                    nameof(SaveRecipeAsync));

                // *** RIAVVIA L'ACQUISIZIONE DOPO IL SALVATAGGIO SE NON CI SONO HOLD ATTIVI ***
                if (MainWindow._cognexManager != null &&
                    !ServiceLocator.MachineRuntimeService.HasContinuousRunHold &&
                    !ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
                {
                    try
                    {
                        UpdateProgressWindow(
                            progressWindow,
                            GetMessage("Sub_entry_OperationSavingRecipeTitle", "Saving recipe"),
                            GetMessage("Sub_entry_OperationRestoringRun", "Restoring continuous run..."),
                            SelectedRecipe?.Name,
                            92);
                        MainWindow.logger?.Info("Riavvio acquisizione continua dopo salvataggio...");

                        // Crea nuovo token se necessario
                        if (MainWindow._continuousRunCts == null || MainWindow._continuousRunCts.IsCancellationRequested)
                        {
                            MainWindow._continuousRunCts = new CancellationTokenSource();
                        }

                        await ServiceLocator.MachineRuntimeService.ManageJobStateAsync(true);
                        MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;

                        // Aggiorna lo stato UI
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
                            {
                                mainVm.ControlButtonsVM.IsRunning = MainWindow._isContinuousRunActive;
                                mainVm.UpdateControlButtonsState();
                            }
                        });

                        if (MainWindow._isContinuousRunActive)
                        {
                            MainWindow.logger?.Info("Acquisizione riavviata con successo");
                        }
                        else
                        {
                            string holdSummary = ServiceLocator.MachineRuntimeService.ContinuousRunHoldSummary;
                            string details = string.IsNullOrWhiteSpace(holdSummary)
                                ? "Continuous run restart after recipe save did not become active."
                                : $"Continuous run restart after recipe save blocked by hold(s): {holdSummary}.";

                            MainWindow.logger?.Warn($"RECIPE_SAVE_AUTORESTART_NOT_RUNNING|recipe={SelectedRecipe?.Name}|details={details}");
                            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                                NLog.LogLevel.Warn,
                                "RECIPE_SAVE_AUTORESTART_NOT_RUNNING",
                                "Recipe",
                                "Recipe save completed but continuous run is still stopped",
                                nameof(SaveRecipeAsync),
                                details,
                                new Dictionary<string, object>
                                {
                                    { "recipe_name", SelectedRecipe?.Name ?? string.Empty }
                                });
                        }
                    }
                    catch (Exception restartEx)
                    {
                        MainWindow.logger?.Error($"Errore nel riavvio dell'acquisizione: {restartEx.Message}");
                        new SystemNotificationWindow("Warning", $"Attenzione: Ricetta salvata ma impossibile riavviare l'acquisizione.\n{restartEx.Message}", NotificationSeverity.Warning).ShowDialog();
                    }
                }
                else if (wasRunning)
                {
                    MainWindow.logger?.Info("Salvataggio ricetta completato senza riavvio automatico: esiste ancora un hold attivo sul run continuo.");
                }
                await CloseProgressWindowAsync(progressWindow);
            }
        }
        private async Task InsertNewRecipeToDatabaseAsync(string newName) 
        {
            // LA RICETTA NON ESISTE NEL DATABASE - CREA NUOVA ENTRY
            MainWindow.logger?.Info($"Creazione nuova entry nel database per la ricetta: {newName}");

            try
            {
                // Prepara i parametri per l'inserimento
                string jsonTop = SerializeToJson(CurrentRecipeData.recipeParamTop);
                string jsonSide = SerializeToJson(CurrentRecipeData.recipeParamSide);
                string jsonFront = SerializeToJson(CurrentRecipeData.recipeParamFront);
                string jsonTop3D = SerializeToJson(CurrentRecipeData.recipeParamTop3D);
                string jsonLast = SerializeToJson(CurrentRecipeData);
                string jsonParam = SerializeToJson(MainWindow.configManager.Config);

                // Inserisci la nuova ricetta nel database
                await Task.Run(() =>
                {
                    _dbManager.InsertProduzione(
                        impianto:MainWindow.configManager.Config.Configuration.LastRecipe ?? "Default",
                        lotto: "Default",
                        turno: "1",
                        operatore: "System",
                        prodotto: CurrentRecipeData.general_Info?.RecipeName ?? newName,
                        ricetta: newName + ".vpp",
                        recipeParameterTopJson: jsonTop,
                        recipeParameterSideJson: jsonSide,
                        recipeParameterFrontJson: jsonFront,
                        recipeParameterTop3DJson: jsonTop3D,
                        lastParameterJson: jsonLast,
                        ConfigParameterJson: jsonParam,
                        timeCreationIdpro: DateTime.Now,
                        timeInizio: DateTime.Now,
                        type: 1
                    );
                });

                // Ottieni l'ID della ricetta appena creata
                var newDbId = await _dbManager.GetRicettaIdByNameAsync(newName);

                if (newDbId.HasValue)
                {
                    SelectedRecipe.DatabaseId = newDbId;
                    SelectedRecipe.ExistsInDatabase = true;
                    MainWindow.logger?.Info($"Nuova ricetta creata nel database con ID: {newDbId.Value}");
                }

            }
            catch (Exception dbEx)
            {
                MainWindow.logger?.Error($"Errore nella creazione della ricetta nel database: {dbEx.Message}");
                new SystemNotificationWindow("Avviso", $"Ricetta salvata ma errore nel database: {dbEx.Message}", NotificationSeverity.Warning).ShowDialog();
            }
        }
        private async Task UpdateExistingRecipeInDatabaseAsync(string newName,bool renamed)
        {
            // LA RICETTA ESISTE GIÀ NEL DATABASE - AGGIORNA
            MainWindow.logger?.Info($"Aggiornamento ricetta esistente nel database: {newName}");
            try
            {

                // LA RICETTA ESISTE GIÀ NEL DATABASE - AGGIORNA
                if (renamed)
                {
                    await Task.Run(() => _dbManager.RenameRicetta((int)SelectedRecipe.DatabaseId.Value, newName + ".vpp"));
                }

                // Aggiorna i parametri della ricetta nel database
                string jsonTop = SerializeToJson(CurrentRecipeData.recipeParamTop);
                string jsonSide = SerializeToJson(CurrentRecipeData.recipeParamSide);
                string jsonFront = SerializeToJson(CurrentRecipeData.recipeParamFront);
                string jsonTop3D = SerializeToJson(CurrentRecipeData.recipeParamTop3D);
                string jsonLast = SerializeToJson(CurrentRecipeData);
                string jsonParam = SerializeToJson(MainWindow.configManager.Config);

                await _dbManager.udpdatePameterRecipe(newName + ".vpp", jsonTop, jsonSide, jsonFront, jsonTop3D, jsonLast, jsonParam);
            }
            catch (Exception dbEx)
            {
                MainWindow.logger?.Error($"Errore nell'aggiornamento della ricetta nel database: {dbEx.Message}");
              
            }
        }
        private void SynchronizeRecipeParameters()
        {
            if (CurrentRecipeData?.general_Info == null || CurrentRecipeData?.recipeParamSide == null)
                return;

            // Sincronizza l'altezza del prodotto con Min_Heigth_value
            CurrentRecipeData.recipeParamSide.Min_Heigth_value = CurrentRecipeData.general_Info.Product_Height;

            // Per le macchine 3DCheck le dimensioni prodotto sono il riferimento nominale
            // autorevole. Le tolleranze restano indipendenti e specifiche della ricetta Top3D.
            if (IsThreeDCheckMachineType && CurrentRecipeData.recipeParamTop3D != null)
            {
                CurrentRecipeData.recipeParamTop3D.ThreeDHeightNominalValue = CurrentRecipeData.general_Info.Product_Height;
                CurrentRecipeData.recipeParamTop3D.ThreeDWidthNominalValue = CurrentRecipeData.general_Info.Product_Width;
                CurrentRecipeData.recipeParamTop3D.ThreeDLengthNominalValue = CurrentRecipeData.general_Info.Product_Length;
            }

            // Notifica che le proprietà sono cambiate
            OnPropertyChanged(nameof(CurrentRecipeData));
        }

        private void UpdateInspectionThresholdsFromRecipeParams()
        {
            if (CurrentRecipeData?.recipeParamTop == null || CurrentRecipeData?.recipeParamSide == null)
                return;

            // Aggiorna l'altezza del prodotto
            if (CurrentRecipeData.general_Info != null)
            {
                CurrentRecipeData.general_Info.Product_Height = CurrentRecipeData.recipeParamSide.Min_Heigth_value;
            }

            // Notifica che le proprietà sono cambiate
            OnPropertyChanged(nameof(CurrentRecipeData));
            OnPropertyChanged(nameof(SelectedRecipe));
        }

        private string SerializeToJson<T>(T obj)
        {
            if (obj == null)
            {
                return null;
            }

            return JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });
        }

        private async Task RenameRecipeFilesAsync(string oldName, string newName)
        {
            try
            {
                string oldVppPath = Path.Combine(_recipeFolder, $"{oldName}.vpp");
                string oldXmlPath = Path.Combine(_recipeFolder, $"{oldName}.xml");
                string newVppPath = Path.Combine(_recipeFolder, $"{newName}.vpp");
                string newXmlPath = Path.Combine(_recipeFolder, $"{newName}.xml");

                // Controlla se i nuovi file esistono già
                if (File.Exists(newVppPath) || File.Exists(newXmlPath))
                {
                    throw new IOException($"Esiste già una ricetta con il nome '{newName}'");
                }

                // Rinomina i file
                if (File.Exists(oldVppPath))
                {
                    File.Move(oldVppPath, newVppPath);
                    SelectedRecipe.FilePath = newVppPath;
                }

                if (File.Exists(oldXmlPath))
                {
                    File.Move(oldXmlPath, newXmlPath);
                    SelectedRecipe.XmlFilePath = newXmlPath;
                }

                // Se questa è la ricetta in produzione, aggiorna la configurazione
                if (SelectedRecipe == ProductionRecipe)
                {
                    MainWindow.configManager.Config.Configuration.LastRecipe = $"{newName}.vpp";
                    await MainWindow.configManager.SaveConfigAsync();
                }

                MainWindow.logger?.Info($"Recipe renamed from '{oldName}' to '{newName}'");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error renaming recipe: {ex.Message}");
                throw;
            }
        }

        private async Task CopyRecipeAsync()
        {
            if (SelectedRecipe == null) return;

            // Controlla i permessi
            if (!_canCreateRecipe)
            {
                ShowEditPermissionError("copiare la ricetta");
                return;
            }

            try
            {
                var baseName = SelectedRecipe.Name;
                var copyNumber = 1;
                string newName;

                do
                {
                    newName = $"{baseName}_Copy{copyNumber}";
                    copyNumber++;
                } while (Recipes.Any(r => r.Name == newName));

                var newVppPath = Path.Combine(_recipeFolder, $"{newName}.vpp");
                var newXmlPath = Path.Combine(_recipeFolder, $"{newName}.xml");

                // Copia file VPP
                File.Copy(SelectedRecipe.FilePath, newVppPath);

                // Copia e modifica file XML
                RecipeParameters.RecipeData copiedRecipeData = null;

                if (File.Exists(SelectedRecipe.XmlFilePath))
                {
                    File.Copy(SelectedRecipe.XmlFilePath, newXmlPath);

                    using (var stream = new FileStream(newXmlPath, FileMode.Open))
                    {
                        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(RecipeParameters.RecipeData));
                        copiedRecipeData = (RecipeParameters.RecipeData)serializer.Deserialize(stream);

                        if (copiedRecipeData.general_Info != null)
                        {
                            copiedRecipeData.general_Info.RecipeDescription = $"{copiedRecipeData.general_Info.RecipeDescription} (Copia)";
                        }

                        stream.Close();

                        using (var writeStream = new FileStream(newXmlPath, FileMode.Create))
                        {
                            serializer.Serialize(writeStream, copiedRecipeData);
                        }
                    }
                }

                try
                {
                    await InsertRecipeToDatabaseAsync(newName, copiedRecipeData);
                    MainWindow.logger?.Info($"Ricetta copiata '{newName}' inserita nel database");
                }
                catch (Exception dbEx)
                {
                    MainWindow.logger?.Error($"Errore nell'inserimento nel database: {dbEx.Message}");
                    // Non bloccare l'operazione se il database fallisce, ma avvisa l'utente
                    new SystemNotificationWindow("Avviso", $"Ricetta copiata ma errore database: {dbEx.Message}", NotificationSeverity.Warning).ShowDialog();
                }

                await LoadRecipesAsync();

                var copiedRecipe = Recipes.FirstOrDefault(r => r.Name == newName);
                if (copiedRecipe != null)
                {
                    SelectedRecipe = copiedRecipe;
                }

                MainWindow.logger?.Info($"Recipe '{SelectedRecipe.Name}' copied to '{newName}'");

                new SystemNotificationWindow("Successo", $"Ricetta '{SelectedRecipe.Name}' copiata come '{newName}'", NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error copying recipe: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nel copiare la ricetta: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        private void ShowLoginDialog()
        {
            try
            {
                var loginWindow = new LoginWindow();
                loginWindow.Owner = Application.Current.MainWindow;
                loginWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var result = loginWindow.ShowDialog();

                if (result == true)
                {
                    // Aggiorna i permessi dopo il login
                    UpdatePermissionsAsync().SafeFireAndForget();

                    // Aggiorna la UI
                    OnPropertyChanged(nameof(ShowReadOnlyWarning));

                    new SystemNotificationWindow("Login effettuato", $"Benvenuto {UserSession.CurrentUser} ({UserSession.CurrentRole})", NotificationSeverity.Info).ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nella finestra di login: {ex.Message}");
            }
        }

        private async Task InsertRecipeToDatabaseAsync(string recipeName, RecipeParameters.RecipeData recipeData)
        {
            try
            {
                var config = MainWindow.configManager.Config.Configuration;

                string jsonTop = SerializeToJson(recipeData?.recipeParamTop);
                string jsonSide = SerializeToJson(recipeData?.recipeParamSide);
                string jsonFront = SerializeToJson(recipeData?.recipeParamFront);
                string jsonTop3D = SerializeToJson(recipeData?.recipeParamTop3D);
                string jsonLast = SerializeToJson(recipeData);
                string jsonParam = SerializeToJson(MainWindow.configManager.Config);

                // Usa il metodo InsertProduzione esistente
                await Task.Run(() =>
                {
                    _dbManager.InsertProduzione(
                        impianto: config.LastRecipe ?? "Default",
                        lotto: "Default",
                        turno: "1",
                        operatore: "System",
                        prodotto: recipeData?.general_Info?.RecipeName ?? recipeName,
                        ricetta: recipeName + ".vpp",
                        recipeParameterTopJson: jsonTop,
                        recipeParameterSideJson: jsonSide,
                        recipeParameterFrontJson: jsonFront,
                        recipeParameterTop3DJson: jsonTop3D,
                        lastParameterJson: jsonLast,
                        ConfigParameterJson: jsonParam,
                        timeCreationIdpro: DateTime.Now,
                        timeInizio: DateTime.Now,
                        type: 1
                    );
                });

                // Ottieni l'ID della ricetta appena creata
                var dbId = await _dbManager.GetRicettaIdByNameAsync(recipeName);

                if (dbId.HasValue)
                {
                    MainWindow.logger?.Info($"Ricetta '{recipeName}' inserita nel database con ID: {dbId.Value}");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'inserimento della ricetta nel database: {ex.Message}");
                throw;
            }
        }
    }

    public sealed class RecipeMultiShotModeOption
    {
        public RecipeMultiShotModeOption(string value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public string Value { get; }

        public string DisplayName { get; }
    }

    /// <summary>
    /// Un gruppo di spunte della pagina ricetta, etichettato con le viste camera che
    /// eseguono quelle ispezioni (es. "SIDE / LEFT"). E' una proiezione di sola lettura
    /// sulle collezioni piatte InspectionItems / EjectionItems: gli item sono gli stessi
    /// oggetti, quindi spuntare qui aggiorna la ricetta esattamente come prima.
    /// </summary>
    public class RecipeInspectionViewGroup
    {
        public RecipeInspectionViewGroup(string viewName)
        {
            ViewName = viewName;
            Items = new ObservableCollection<InspectionItem>();
        }

        public string ViewName { get; }

        public ObservableCollection<InspectionItem> Items { get; }
    }
}



