using MySql.Data.MySqlClient;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace QtisVisionPanel.ViewModels
{
    public class InspectionConfigViewModel : INotifyPropertyChanged
    {
        private string _machineType;
        private bool _isBusy;
        private bool _hasChanges;
        private bool _usesActiveRecipeProfile;
        private bool _usesRecipeViewConfiguration;

        public ObservableCollection<InspectionFeature> Features { get; set; }
        public List<string> MachineTypes { get; } = new List<string> { "Packs", "Rolls", "Bags", "3DCheck" };

        public ICommand SaveCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand ResetToDefaultCommand { get; }
        public IReadOnlyList<RecipeMultiShotModeOption> CameraModes { get; }

        public string MachineType
        {
            get => _machineType;
            set
            {
                if (_machineType != value)
                {
                    _machineType = value;
                    OnPropertyChanged();
                    LoadFeaturesAsync().SafeFireAndForget();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            set
            {
                _hasChanges = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusMessage));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public int EnabledFeatureCount => Features?.Count(f => f.IsEnabled) ?? 0;
        public int DisabledFeatureCount => Math.Max(0, TotalFeatureCount - EnabledFeatureCount);
        public int TotalFeatureCount => Features?.Count ?? 0;
        public bool UsesActiveRecipeProfile
        {
            get => _usesActiveRecipeProfile;
            set
            {
                if (_usesActiveRecipeProfile != value)
                {
                    _usesActiveRecipeProfile = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        public bool UsesRecipeViewConfiguration
        {
            get => _usesRecipeViewConfiguration;
            private set
            {
                if (_usesRecipeViewConfiguration == value)
                {
                    return;
                }

                _usesRecipeViewConfiguration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public string ActiveRecipeName =>
            MainWindow.ConfigRecipeParam?.Config?.general_Info?.RecipeName ?? "-";

        public string ActiveRecipeDisplay => string.Format(
            ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_Inspection_activeRecipeFormat",
                "Active recipe: {0}"),
            ActiveRecipeName);

        public string CameraUsageLabel => ServerMessagePersonalize.GetMessageOrDefault(
            "Sub_entry_Inspection_cameraUsage",
            "Camera usage in this recipe");

        public string AcceptedClassesLabel => ServerMessagePersonalize.GetMessageOrDefault(
            "Sub_entry_Inspection_acceptedClasses",
            "Accepted Classify classes (comma separated)");

        public string AcceptedClassesToolTip => ServerMessagePersonalize.GetMessageOrDefault(
            "Sub_entry_Inspection_acceptedClassesToolTip",
            "Example: OK, Compliant, Good sealing. Leave empty to use the standard labels (OK/GOOD/PASS/PASSED/COMPLIANT).");

        public string MinimumScoreLabel => ServerMessagePersonalize.GetMessageOrDefault(
            "Sub_entry_Inspection_minimumScore",
            "Minimum confidence (0 = disabled)");

        public string MinimumScoreToolTip => ServerMessagePersonalize.GetMessageOrDefault(
            "Sub_entry_Inspection_minimumScoreToolTip",
            "Minimum score required for an accepted class to be GOOD, using the same scale as the job Score output (typically 0..1). Below the threshold the product is Unclassify and rejected. 0 disables this check.");

        public string StatusMessage
        {
            get
            {
                if (HasChanges)
                {
                    return UsesActiveRecipeProfile
                        ? ServerMessagePersonalize.GetMessageOrDefault(
                            "Sub_entry_Inspection_statusRecipeChanges",
                            "Changes to save in the active recipe")
                        : ServerMessagePersonalize.GetMessageOrDefault(
                            "Sub_entry_Inspection_statusMachineChanges",
                            "Changes to save");
                }

                return UsesRecipeViewConfiguration
                    ? ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_Inspection_statusRecipeProfile",
                        "Active recipe camera-view profile")
                    : UsesActiveRecipeProfile
                    ? ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_Inspection_statusMachineDefaults",
                        "Active recipe using machine camera-view defaults")
                    : ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_Inspection_statusMachineUpdated",
                        "Machine configuration is up to date");
            }
        }

        public InspectionConfigViewModel()
        {
            Features = new ObservableCollection<InspectionFeature>();
            Features.CollectionChanged += OnFeaturesCollectionChanged;
            CameraModes = new List<RecipeMultiShotModeOption>
            {
                new RecipeMultiShotModeOption(
                    Services.RecipeMachineRuntimeResolver.MultiShotModeMachine,
                    ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RecipeMultiShotModeMachine",
                        "Use machine setting")),
                new RecipeMultiShotModeOption(
                    Services.RecipeMachineRuntimeResolver.MultiShotModeEnabled,
                    ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RecipeMultiShotModeEnabled",
                        "Enabled for this recipe")),
                new RecipeMultiShotModeOption(
                    Services.RecipeMachineRuntimeResolver.MultiShotModeDisabled,
                    ServerMessagePersonalize.GetMessageOrDefault(
                        "Sub_entry_RecipeMultiShotModeDisabled",
                        "Disabled for this recipe"))
            }.AsReadOnly();

            // Carica il tipo di macchina dalla configurazione
            _machineType = MainWindow.configManager.Config.Configuration.MachineType;

            // Inizializza i comandi
            SaveCommand = new RelayCommand(async _ => await SaveFeaturesAsync(), _ => !IsBusy && HasChanges);
            ReloadCommand = new RelayCommand(async _ => await LoadFeaturesAsync(), _ => !IsBusy);
            ResetToDefaultCommand = new RelayCommand(async _ => await ResetToDefaultAsync(), _ => !IsBusy);

            // Sottoscrivi agli eventi di modifica
            Features.CollectionChanged += (s, e) => HasChanges = true;

            // Carica le feature
            LoadFeaturesAsync().SafeFireAndForget();
        }

        /// <summary>
        /// Matrice vista x ispezione mostrata nella pagina: una riga per vista camera, con
        /// le sole ispezioni che quella vista puo' meccanicamente verificare.
        /// </summary>
        public ObservableCollection<CameraViewInspectionGroup> ViewMatrix { get; } =
            new ObservableCollection<CameraViewInspectionGroup>();

        private bool _hasViewMatrixChanges;
        public bool HasViewMatrixChanges
        {
            get => _hasViewMatrixChanges;
            private set
            {
                if (_hasViewMatrixChanges == value)
                {
                    return;
                }

                _hasViewMatrixChanges = value;
                OnPropertyChanged(nameof(HasViewMatrixChanges));
                if (value)
                {
                    HasChanges = true;
                }
            }
        }

        /// <summary>
        /// Costruisce la matrice per le sole viste presenti a runtime e applica gli override
        /// salvati. Le celle mai configurate partono dal default storico, quindi aprire la
        /// pagina senza salvare non cambia nulla.
        /// </summary>
        private async Task LoadViewMatrixAsync()
        {
            foreach (var existing in ViewMatrix)
            {
                existing.Detach(OnViewMatrixCellChanged);
            }
            ViewMatrix.Clear();

            RecipeParameters.RecipeData activeRecipe = MainWindow.ConfigRecipeParam?.Config;
            DataManage.CameraInspectionMatrix sourceMatrix;
            DataManage.CameraClassificationPolicy sourcePolicy;
            UsesRecipeViewConfiguration =
                DataManage.RecipeInspectionViewConfigurationMapper.TryBuild(
                    activeRecipe,
                    out sourceMatrix,
                    out sourcePolicy,
                    !HasRuntimeRole("right") && HasRuntimeRole("rear"));

            if (!UsesRecipeViewConfiguration)
            {
                try
                {
                    List<DataManage.CameraInspectionCell> machineCells =
                        await InspectionDataService.LoadViewMatrixAsync(MachineType);
                    List<DataManage.CameraClassificationRule> machineRules =
                        await InspectionDataService.LoadClassificationRulesAsync(MachineType);
                    sourceMatrix = new DataManage.CameraInspectionMatrix(machineCells);
                    sourcePolicy = new DataManage.CameraClassificationPolicy(machineRules);
                }
                catch (Exception ex)
                {
                    sourceMatrix = ServiceLocator.InspectionConfigService.MachineViewMatrix;
                    sourcePolicy = ServiceLocator.InspectionConfigService.MachineClassificationPolicy;
                    MainWindow.logger?.Warn(ex,
                        $"INSPECTION_VIEW_MATRIX_UI_USING_LAST_SNAPSHOT|machineType={MachineType}");
                }
            }

            foreach (string role in DataManage.CameraInspectionMatrix.SupportedRoles)
            {
                // Solo le viste realmente presenti sulla macchina: proporre la Bottom su un
                // impianto a tre camere confonderebbe e basta.
                if (!HasRuntimeRole(role))
                {
                    continue;
                }

                var group = new CameraViewInspectionGroup(role);
                // Solo le ispezioni che questa camera puo' eseguire, piu' quelle abilitate
                // esplicitamente. Il salvataggio scrive una cella per ogni camera e ogni feature:
                // mostrare anche le celle salvate a "no" riempiva ogni camera con le feature di
                // tutte le altre (Logo sulla Left, 3D sulla Top 2D...). Le celle nascoste
                // conservano il valore salvato perche' il salvataggio parte dalla baseline.
                foreach (string feature in DataManage.InspectionConfigService.AllKnownFeatures
                             .Where(item =>
                                 DataManage.CameraInspectionMatrix.DefaultAppliesTo(role, item) ||
                                 (sourceMatrix.HasExplicitCell(role, item) && sourceMatrix.IsApplicable(role, item))))
                {
                    group.Inspections.Add(new CameraViewInspectionCell
                    {
                        CameraRole = role,
                        Feature = feature,
                        IsEnabled = sourceMatrix.IsApplicable(role, feature)
                    });
                }

                if (group.Inspections.Count == 0)
                {
                    continue;
                }

                group.CameraMode = GetRecipeCameraMode(activeRecipe, role);
                group.AcceptedClasses = sourcePolicy.GetAcceptedClassesText(role);
                group.MinimumClassificationScore = sourcePolicy.GetMinimumScore(role);
                group.AcceptedClassesChanged += (s, e) => HasViewMatrixChanges = true;
                group.CameraModeChanged += (s, e) => HasViewMatrixChanges = true;
                group.Attach(OnViewMatrixCellChanged);
                ViewMatrix.Add(group);
            }

            HasViewMatrixChanges = false;
            OnPropertyChanged(nameof(HasViewMatrix));
            OnPropertyChanged(nameof(ActiveRecipeName));
            OnPropertyChanged(nameof(ActiveRecipeDisplay));
            MainWindow.logger?.Info(
                $"INSPECTION_VIEW_MATRIX_LOADED|machineType={MachineType}|scope={(UsesRecipeViewConfiguration ? "recipe" : "machine-default")}|views={ViewMatrix.Count}|cells={sourceMatrix.ExplicitCellCount}");
        }

        public bool HasViewMatrix => ViewMatrix.Count > 0;

        private void OnViewMatrixCellChanged(object sender, PropertyChangedEventArgs e)
        {
            if (IsBusy)
            {
                return;
            }

            if (e.PropertyName == nameof(CameraViewInspectionCell.IsEnabled))
            {
                HasViewMatrixChanges = true;
            }
        }

        private async Task SaveViewMatrixAsync()
        {
            // Opening and saving the page must not silently convert a legacy recipe into
            // a strict profile. Persist the matrix only after an operator changed a cell,
            // a camera mode or a classification policy.
            if (!HasViewMatrixChanges)
            {
                return;
            }

            RecipeParameters.RecipeData activeRecipe = MainWindow.ConfigRecipeParam?.Config;
            DataManage.CameraInspectionMatrix baselineMatrix;
            DataManage.CameraClassificationPolicy baselinePolicy;
            if (!DataManage.RecipeInspectionViewConfigurationMapper.TryBuild(
                    activeRecipe,
                    out baselineMatrix,
                    out baselinePolicy,
                    !HasRuntimeRole("right") && HasRuntimeRole("rear")))
            {
                baselineMatrix = ServiceLocator.InspectionConfigService.MachineViewMatrix;
                baselinePolicy = ServiceLocator.InspectionConfigService.MachineClassificationPolicy;
            }

            var visibleCells = ViewMatrix
                .SelectMany(group => group.Inspections)
                .Select(cell => new DataManage.CameraInspectionCell
                {
                    CameraRole = cell.CameraRole,
                    Feature = cell.Feature,
                    Enabled = cell.IsEnabled
                })
                .ToList();

            var visibleCellMap = visibleCells.ToDictionary(
                cell => BuildViewFeatureKey(cell.CameraRole, cell.Feature),
                cell => cell.Enabled,
                StringComparer.OrdinalIgnoreCase);

            // A recipe profile is strict by design. Seed every supported role and feature
            // from the resolved baseline before overlaying the controls currently rendered
            // by this machine. Otherwise an unrendered Rear/Bottom column would disappear
            // permanently at the first save.
            var cells = new List<DataManage.CameraInspectionCell>();
            foreach (string role in DataManage.CameraInspectionMatrix.SupportedRoles)
            {
                foreach (string feature in DataManage.InspectionConfigService.AllKnownFeatures)
                {
                    string key = BuildViewFeatureKey(role, feature);
                    cells.Add(new DataManage.CameraInspectionCell
                    {
                        CameraRole = role,
                        Feature = feature,
                        Enabled = visibleCellMap.TryGetValue(key, out bool visibleValue)
                            ? visibleValue
                            : baselineMatrix.IsApplicable(role, feature)
                    });
                }
            }

            var visibleRuleMap = ViewMatrix.ToDictionary(
                group => DataManage.CameraInspectionMatrix.NormalizeRole(group.CameraRole),
                group => NormalizeAcceptedClasses(group.AcceptedClasses),
                StringComparer.OrdinalIgnoreCase);
            var visibleScoreMap = ViewMatrix.ToDictionary(
                group => DataManage.CameraInspectionMatrix.NormalizeRole(group.CameraRole),
                group => group.MinimumClassificationScore,
                StringComparer.OrdinalIgnoreCase);
            var rules = DataManage.CameraInspectionMatrix.SupportedRoles
                .Select(role => new DataManage.CameraClassificationRule
                {
                    CameraRole = role,
                    AcceptedClasses = visibleRuleMap.TryGetValue(role, out string visibleClasses)
                        ? visibleClasses
                        : baselinePolicy.GetAcceptedClassesText(role),
                    // Le viste non mostrate conservano la soglia di partenza invece di
                    // azzerarla: salvare da una schermata filtrata non deve disattivare
                    // silenziosamente il controllo sulle altre camere.
                    MinimumScore = visibleScoreMap.TryGetValue(role, out double visibleScore)
                        ? visibleScore
                        : baselinePolicy.GetMinimumScore(role)
                })
                .ToList();

            if (activeRecipe != null)
            {
                DataManage.RecipeInspectionViewConfigurationMapper.Apply(activeRecipe, cells, rules);
                foreach (CameraViewInspectionGroup group in ViewMatrix)
                {
                    SetRecipeCameraMode(activeRecipe, group.CameraRole, group.CameraMode);
                }
                UsesRecipeViewConfiguration = true;
            }
            else
            {
                await InspectionDataService.SaveViewMatrixAsync(cells, MachineType);
                await InspectionDataService.SaveClassificationRulesAsync(rules, MachineType);
                ServiceLocator.InspectionConfigService.ApplyViewMatrix(
                    new DataManage.CameraInspectionMatrix(cells));
                ServiceLocator.InspectionConfigService.ApplyClassificationPolicy(
                    new DataManage.CameraClassificationPolicy(rules));
            }

            HasViewMatrixChanges = false;
            MainWindow.logger?.Info(
                $"INSPECTION_VIEW_MATRIX_SAVED|machineType={MachineType}|scope={(activeRecipe != null ? "recipe" : "machine-default")}|cells={cells.Count}");
        }

        private async Task LoadFeaturesAsync()
        {
            try
            {
                IsBusy = true;
                UnsubscribeFromFeatureChanges();
                Features.Clear();

                var features = await InspectionDataService.LoadFeaturesAsync(MachineType);
                features = FilterFeaturesForActiveRuntime(features);
                UsesActiveRecipeProfile = ApplyActiveRecipeState(features);

                foreach (var feature in features.OrderBy(f => f.Feature))
                {
                    feature.PropertyChanged += OnFeaturePropertyChanged;
                    Features.Add(feature);
                }

                await LoadViewMatrixAsync();

                RefreshFeatureSummary();
                HasChanges = false;
                MainWindow.logger?.Info($"Caricate {features.Count} feature per tipo macchina: {MachineType}");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento delle feature: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveFeaturesAsync()
        {
            if (!CanModifyInspectionConfiguration("salvare la configurazione"))
            {
                return;
            }

            if (HasViewMatrixChanges && HasDisabledPrimaryCamera())
            {
                MainWindow.logger?.Warn("INSPECTION_CONFIGURATION_SAVE_BLOCKED|reason=primary-camera-disabled");
                new SystemNotificationWindow(
                    "Configurazione non valida",
                    "La vista TOP/TOP 3D e' la vista primaria e non puo' essere disabilitata per ricetta.",
                    NotificationSeverity.Warning).ShowDialog();
                return;
            }

            const string runtimeHold = "InspectionConfigurationSave";
            ServiceLocator.MachineRuntimeService?.AddContinuousRunHold(runtimeHold, nameof(SaveFeaturesAsync));
            try
            {
                IsBusy = true;
                // await InspectionDataService.SaveFeaturesAsync(Features, MachineType);
                // Usa il servizio per salvare
                await ServiceLocator.InspectionConfigService.SaveFeaturesAsync(Features, MachineType);
                await SaveViewMatrixAsync();
                await UpdateActiveRecipeAsync(Features);
                // Aggiorna anche il file di configurazione
                await UpdateConfigFileAsync();

                HasChanges = false;

                // Notifica il sistema che la configurazione è cambiata
                OnConfigurationChanged();

                MainWindow.logger?.Info($"Feature salvate per tipo macchina: {MachineType}");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel salvataggio delle feature: {ex.Message}");
                throw;
            }
            finally
            {
                ServiceLocator.MachineRuntimeService?.ReleaseContinuousRunHold(runtimeHold, nameof(SaveFeaturesAsync));
                IsBusy = false;
            }
        }

        private async Task ResetToDefaultAsync()
        {
            if (!CanModifyInspectionConfiguration("ripristinare la configurazione"))
            {
                return;
            }

            const string runtimeHold = "InspectionConfigurationReset";
            ServiceLocator.MachineRuntimeService?.AddContinuousRunHold(runtimeHold, nameof(ResetToDefaultAsync));
            try
            {
                IsBusy = true;
                await InspectionDataService.ResetToDefaultAsync(MachineType);
                var defaultFeatures = await InspectionDataService.LoadFeaturesAsync(MachineType);
                RecipeParameters.RecipeData activeRecipe = MainWindow.ConfigRecipeParam?.Config;
                if (activeRecipe != null)
                {
                    DataManage.RecipeInspectionViewConfigurationMapper.Reset(activeRecipe);
                    ResetRecipeCameraModes(activeRecipe);
                }
                else
                {
                    await InspectionDataService.ResetViewConfigurationAsync(MachineType);
                    ServiceLocator.InspectionConfigService.ApplyViewMatrix(
                        new DataManage.CameraInspectionMatrix());
                    ServiceLocator.InspectionConfigService.ApplyClassificationPolicy(
                        new DataManage.CameraClassificationPolicy());
                }
                await UpdateActiveRecipeAsync(FilterFeaturesForActiveRuntime(defaultFeatures));
                await LoadFeaturesAsync();
                MainWindow.logger?.Info($"Feature resettate ai valori predefiniti per: {MachineType}");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel reset delle feature: {ex.Message}");
            }
            finally
            {
                ServiceLocator.MachineRuntimeService?.ReleaseContinuousRunHold(runtimeHold, nameof(ResetToDefaultAsync));
                IsBusy = false;
            }
        }

        private async Task UpdateConfigFileAsync()
        {
            try
            {
                // Aggiorna il tipo macchina nel file di configurazione
                MainWindow.configManager.Config.Configuration.MachineType = MachineType;
                await MainWindow.configManager.SaveConfigAsync();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Impossibile aggiornare il file di configurazione: {ex.Message}");
            }
        }

        private void OnConfigurationChanged()
        {
            // Notifica gli altri componenti che la configurazione è cambiata
            // Questo può essere usato da ToolBlockValidator per aggiornare le feature abilitate
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnFeaturesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshFeatureSummary();
            if (!IsBusy)
            {
                HasChanges = true;
            }
        }

        private void OnFeaturePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InspectionFeature.IsEnabled))
            {
                RefreshFeatureSummary();
            }

            if (!IsBusy)
            {
                HasChanges = true;
            }
        }

        private void RefreshFeatureSummary()
        {
            OnPropertyChanged(nameof(EnabledFeatureCount));
            OnPropertyChanged(nameof(DisabledFeatureCount));
            OnPropertyChanged(nameof(TotalFeatureCount));
        }

        private List<InspectionFeature> FilterFeaturesForActiveRuntime(IEnumerable<InspectionFeature> features)
        {
            if (features == null)
            {
                return new List<InspectionFeature>();
            }

            bool hasAnyRuntimeRole = MainWindow.JobRoleMapping != null && MainWindow.JobRoleMapping.Count > 0;
            if (!hasAnyRuntimeRole)
            {
                return features.ToList();
            }

            return features.Where(feature => ShouldIncludeFeatureForCurrentRuntime(feature?.Feature)).ToList();
        }

        private bool ShouldIncludeFeatureForCurrentRuntime(string featureKey)
        {
            string normalizedFeature = NormalizeFeatureKey(featureKey);
            if (string.IsNullOrWhiteSpace(normalizedFeature))
            {
                return false;
            }

            switch (normalizedFeature)
            {
                case "Logo":
                case "PrintCentering":
                case "OpenFlaps":
                case "SurfaceCheck":
                case "ShapeTop":
                    return HasRuntimeRole("top");
                case "Height":
                    return HasRuntimeRole("side");
                case "SideSealing":
                case "ShapeSide":
                    return HasRuntimeRole("side") ||
                           HasRuntimeRole("left") ||
                           HasRuntimeRole("rear") ||
                           HasRuntimeRole("right");
                case "FrontTraceability":
                    return HasRuntimeRole("front");
                case "ThreeDHeight":
                case "ThreeDWidth":
                case "ThreeDLength":
                    return HasRuntimeRole("top3d");
                case "BottomSealing":
                case "TrappedPaper":
                    return HasRuntimeRole("bottom");
                case "AIClassification":
                    return HasRuntimeRole("top") ||
                           HasRuntimeRole("top3d") ||
                           HasRuntimeRole("side") ||
                           HasRuntimeRole("left") ||
                           HasRuntimeRole("front") ||
                           HasRuntimeRole("rear") ||
                           HasRuntimeRole("right") ||
                           HasRuntimeRole("bottom");
                default:
                    return true;
            }
        }

        private static bool HasRuntimeRole(string role)
        {
            if (MainWindow.JobRoleMapping != null &&
                CameraConfigurationHelper.HasCameraRole(MainWindow.JobRoleMapping, role))
            {
                return true;
            }

            // JobRoleMapping can be only partially populated while a VPP is loading. Union
            // it with CameraConfig instead of using CameraConfig only when the whole map is
            // empty, otherwise the last physical view disappears from this page.
            try
            {
                string recipeFolder = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder;
                if (string.IsNullOrWhiteSpace(recipeFolder))
                {
                    return false;
                }

                string cameraConfigPath = Path.Combine(recipeFolder, "CameraConfig.xml");
                // CameraConfig.xml puo' contenere righe obsolete (per esempio Front
                // nell'impianto Top/Left/Right). Incrociare i job VPP realmente
                // caricati evita di mostrare una camera che non esiste.
                var fallbackRoles = CameraConfigurationHelper.BuildRoleMapping(
                    cameraConfigPath,
                    MainWindow.JobMapping?.Count > 0 ? MainWindow.JobMapping : null);
                return CameraConfigurationHelper.HasCameraRole(fallbackRoles, role);
            }
            catch
            {
                return false;
            }
        }

        private static bool ApplyActiveRecipeState(IEnumerable<InspectionFeature> features)
        {
            var recipeData = MainWindow.ConfigRecipeParam?.Config;
            if (recipeData?.ejectionStatus == null || features == null)
            {
                return false;
            }

            foreach (var feature in features)
            {
                if (TryGetRecipeFeatureState(recipeData, feature?.Feature, out bool isEnabled))
                {
                    feature.IsEnabled = isEnabled;
                }
            }

            return true;
        }

        private static bool TryGetRecipeFeatureState(RecipeParameters.RecipeData recipeData, string featureKey, out bool isEnabled)
        {
            isEnabled = false;
            if (recipeData?.inspectionStatus == null && recipeData?.ejectionStatus == null)
            {
                return false;
            }

            var inspectionStatus = recipeData.inspectionStatus ?? RecipeParameters.InspectionStatus.FromEjectionStatus(recipeData.ejectionStatus);

            switch (NormalizeFeatureKey(featureKey))
            {
                case "Logo":
                    isEnabled = inspectionStatus.logo;
                    return true;
                case "PrintCentering":
                    isEnabled = inspectionStatus.Print_centering;
                    return true;
                case "OpenFlaps":
                    isEnabled = inspectionStatus.OpenFlaps;
                    return true;
                case "SurfaceCheck":
                    isEnabled = inspectionStatus.SurfaceCheck;
                    return true;
                case "Height":
                    isEnabled = inspectionStatus.Height;
                    return true;
                case "SideSealing":
                    isEnabled = inspectionStatus.Side_sealing;
                    return true;
                case "ShapeTop":
                    isEnabled = inspectionStatus.ShapeTop;
                    return true;
                case "ShapeSide":
                    isEnabled = inspectionStatus.ShapeSide;
                    return true;
                case "FrontTraceability":
                    isEnabled = inspectionStatus.FrontTraceability;
                    return true;
                case "ThreeDHeight":
                    isEnabled = inspectionStatus.ThreeDHeight;
                    return true;
                case "ThreeDWidth":
                    isEnabled = inspectionStatus.ThreeDWidth;
                    return true;
                case "ThreeDLength":
                    isEnabled = inspectionStatus.ThreeDLength;
                    return true;
                case "BottomSealing":
                    isEnabled = inspectionStatus.BottomSealing;
                    return true;
                case "TrappedPaper":
                    isEnabled = inspectionStatus.TrappedPaper;
                    return true;
                case "AIClassification":
                    isEnabled = inspectionStatus.AIClassification;
                    return true;
                default:
                    return false;
            }
        }

        private static void SetRecipeFeatureState(RecipeParameters.RecipeData recipeData, string featureKey, bool isEnabled)
        {
            if (recipeData == null)
            {
                return;
            }

            if (recipeData.inspectionStatus == null)
            {
                recipeData.inspectionStatus = RecipeParameters.InspectionStatus.FromEjectionStatus(recipeData.ejectionStatus);
            }

            switch (NormalizeFeatureKey(featureKey))
            {
                case "Logo":
                    recipeData.inspectionStatus.logo = isEnabled;
                    break;
                case "PrintCentering":
                    recipeData.inspectionStatus.Print_centering = isEnabled;
                    break;
                case "OpenFlaps":
                    recipeData.inspectionStatus.OpenFlaps = isEnabled;
                    break;
                case "SurfaceCheck":
                    recipeData.inspectionStatus.SurfaceCheck = isEnabled;
                    break;
                case "Height":
                    recipeData.inspectionStatus.Height = isEnabled;
                    break;
                case "SideSealing":
                    recipeData.inspectionStatus.Side_sealing = isEnabled;
                    break;
                case "ShapeTop":
                    recipeData.inspectionStatus.ShapeTop = isEnabled;
                    break;
                case "ShapeSide":
                    recipeData.inspectionStatus.ShapeSide = isEnabled;
                    break;
                case "FrontTraceability":
                    recipeData.inspectionStatus.FrontTraceability = isEnabled;
                    break;
                case "ThreeDHeight":
                    recipeData.inspectionStatus.ThreeDHeight = isEnabled;
                    break;
                case "ThreeDWidth":
                    recipeData.inspectionStatus.ThreeDWidth = isEnabled;
                    break;
                case "ThreeDLength":
                    recipeData.inspectionStatus.ThreeDLength = isEnabled;
                    break;
                case "BottomSealing":
                    recipeData.inspectionStatus.BottomSealing = isEnabled;
                    break;
                case "TrappedPaper":
                    recipeData.inspectionStatus.TrappedPaper = isEnabled;
                    break;
                case "AIClassification":
                    recipeData.inspectionStatus.AIClassification = isEnabled;
                    break;
            }
        }

        private static async Task UpdateActiveRecipeAsync(IEnumerable<InspectionFeature> features)
        {
            var recipeManager = MainWindow.ConfigRecipeParam;
            var recipeData = recipeManager?.Config;
            if (recipeManager == null || features == null)
            {
                return;
            }

            if (recipeData.inspectionStatus == null)
            {
                recipeData.inspectionStatus = RecipeParameters.InspectionStatus.FromEjectionStatus(recipeData.ejectionStatus);
            }

            foreach (var feature in features)
            {
                SetRecipeFeatureState(recipeData, feature?.Feature, feature?.IsEnabled ?? false);
            }

            await recipeManager.SaveConfigAsync();

            MainWindow.MainView?.RefreshActiveRecipeInspectionConfiguration(
                nameof(InspectionConfigViewModel));

            if (App.ViewModelCache.DigitalIOVM != null)
            {
                await App.ViewModelCache.DigitalIOVM.ApplyActiveRecipeRuntimeAdjustmentsAsync(
                    recipeData,
                    nameof(InspectionConfigViewModel));
            }
        }

        private static string NormalizeAcceptedClasses(string value)
        {
            return string.Join(", ",
                DataManage.CameraClassificationPolicy.ParseClasses(value)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        }

        private static string BuildViewFeatureKey(string cameraRole, string feature)
        {
            return DataManage.CameraInspectionMatrix.NormalizeRole(cameraRole) + "|" +
                   DataManage.CameraInspectionMatrix.NormalizeFeature(feature);
        }

        private bool HasDisabledPrimaryCamera()
        {
            return ViewMatrix.Any(group =>
                (string.Equals(DataManage.CameraInspectionMatrix.NormalizeRole(group.CameraRole), "top", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(DataManage.CameraInspectionMatrix.NormalizeRole(group.CameraRole), "top3d", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(
                    group.CameraMode,
                    Services.RecipeMachineRuntimeResolver.MultiShotModeDisabled,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool CanModifyInspectionConfiguration(string operation)
        {
            if (ServiceLocator.MachineRuntimeService?.IsContinuousRunActive == true)
            {
                MainWindow.logger?.Warn($"INSPECTION_CONFIGURATION_CHANGE_BLOCKED|reason=continuous-run|operation={operation}");
                new SystemNotificationWindow(
                    "Macchina in produzione",
                    $"Arrestare la produzione prima di {operation}.",
                    NotificationSeverity.Warning).ShowDialog();
                return false;
            }

            int inFlightProducts = App.ViewModelCache.DigitalIOVM?.TrackedProducts.Count(product =>
                product != null &&
                product.State != TrackedProductState.Completed &&
                product.State != TrackedProductState.RejectedExecuted) ?? 0;
            if (inFlightProducts > 0)
            {
                MainWindow.logger?.Warn(
                    $"INSPECTION_CONFIGURATION_CHANGE_BLOCKED|reason=products-in-flight|count={inFlightProducts}|operation={operation}");
                new SystemNotificationWindow(
                    "Prodotti ancora tracciati",
                    $"Attendere il completamento di {inFlightProducts} prodotto/i prima di {operation}.",
                    NotificationSeverity.Warning).ShowDialog();
                return false;
            }

            return true;
        }

        private static string GetRecipeCameraMode(
            RecipeParameters.RecipeData recipe,
            string cameraRole)
        {
            RecipeParameters.RecipeCameraTriggerAdjustments triggers =
                recipe?.machineRuntimeAdjustments?.CameraTriggers;
            if (triggers == null)
            {
                return Services.RecipeMachineRuntimeResolver.MultiShotModeMachine;
            }

            // SIDE resta compatibile con LEFT. REAR e RIGHT sono distinti quando
            // entrambi i job sono presenti; su un VPP solo-REAR vale il fallback storico.
            switch (DataManage.CameraInspectionMatrix.NormalizeRole(cameraRole))
            {
                case "top":
                case "top3d": return triggers.Top;
                case "left": return ResolvePhysicalCameraMode(triggers.Left, triggers.Side);
                case "front": return triggers.Front;
                case "right": return triggers.Right;
                case "rear": return !HasRuntimeRole("right")
                    ? ResolvePhysicalCameraMode(triggers.Rear, triggers.Right)
                    : triggers.Rear;
                case "bottom": return triggers.Bottom;
                default: return Services.RecipeMachineRuntimeResolver.MultiShotModeMachine;
            }
        }

        private static string ResolvePhysicalCameraMode(string physicalMode, string legacyMode)
        {
            string machine = Services.RecipeMachineRuntimeResolver.MultiShotModeMachine;
            return string.IsNullOrWhiteSpace(physicalMode) ||
                   string.Equals(physicalMode.Trim(), machine, StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(legacyMode) ? machine : legacyMode)
                : physicalMode;
        }

        private static void SetRecipeCameraMode(
            RecipeParameters.RecipeData recipe,
            string cameraRole,
            string mode)
        {
            if (recipe == null)
            {
                return;
            }

            if (recipe.machineRuntimeAdjustments == null)
            {
                recipe.machineRuntimeAdjustments = new RecipeParameters.RecipeMachineRuntimeAdjustments();
            }
            if (recipe.machineRuntimeAdjustments.CameraTriggers == null)
            {
                recipe.machineRuntimeAdjustments.CameraTriggers = new RecipeParameters.RecipeCameraTriggerAdjustments();
            }

            string normalizedMode = string.IsNullOrWhiteSpace(mode)
                ? Services.RecipeMachineRuntimeResolver.MultiShotModeMachine
                : mode;
            RecipeParameters.RecipeCameraTriggerAdjustments triggers =
                recipe.machineRuntimeAdjustments.CameraTriggers;
            switch (DataManage.CameraInspectionMatrix.NormalizeRole(cameraRole))
            {
                case "top":
                case "top3d": triggers.Top = normalizedMode; break;
                case "left":
                    // Si salva il campo fisico e si neutralizza lo storico, come la pagina ricetta.
                    triggers.Left = normalizedMode;
                    triggers.Side = Services.RecipeMachineRuntimeResolver.MultiShotModeMachine;
                    break;
                case "front": triggers.Front = normalizedMode; break;
                case "right":
                    triggers.Right = normalizedMode;
                    break;
                case "rear":
                    triggers.Rear = normalizedMode;
                    if (!HasRuntimeRole("right")) triggers.Right = Services.RecipeMachineRuntimeResolver.MultiShotModeMachine;
                    break;
                case "bottom": triggers.Bottom = normalizedMode; break;
            }
        }

        private static void ResetRecipeCameraModes(RecipeParameters.RecipeData recipe)
        {
            if (recipe == null)
            {
                return;
            }

            if (recipe.machineRuntimeAdjustments == null)
            {
                recipe.machineRuntimeAdjustments = new RecipeParameters.RecipeMachineRuntimeAdjustments();
            }
            recipe.machineRuntimeAdjustments.CameraTriggers =
                new RecipeParameters.RecipeCameraTriggerAdjustments();
        }

        private static string NormalizeFeatureKey(string feature)
        {
            switch (feature?.Trim())
            {
                case "Print_centering":
                    return "PrintCentering";
                case "Side_sealing":
                    return "SideSealing";
                case "Bottom_sealing":
                case "Bottom sealing":
                case "SaldaturaInferiore":
                case "Saldatura inferiore":
                    return "BottomSealing";
                case "Trapped paper":
                case "CartaIntrappolata":
                case "Carta intrappolata":
                case "Carta in saldatura":
                    return "TrappedPaper";
                case "AI Classify":
                case "AI classification":
                case "Classification":
                case "Classify":
                    return "AIClassification";
                default:
                    return feature?.Trim();
            }
        }

        private void UnsubscribeFromFeatureChanges()
        {
            foreach (var feature in Features)
            {
                feature.PropertyChanged -= OnFeaturePropertyChanged;
            }
        }

        public event EventHandler ConfigurationChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Una vista camera con le ispezioni che puo' verificare.</summary>
    public class CameraViewInspectionGroup : INotifyPropertyChanged
    {
        public CameraViewInspectionGroup(string cameraRole)
        {
            CameraRole = cameraRole;
            Inspections = new ObservableCollection<CameraViewInspectionCell>();
        }

        public string CameraRole { get; }

        private string _acceptedClasses;
        private double _minimumClassificationScore;
        private string _cameraMode = Services.RecipeMachineRuntimeResolver.MultiShotModeMachine;

        public string CameraMode
        {
            get => _cameraMode;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value)
                    ? Services.RecipeMachineRuntimeResolver.MultiShotModeMachine
                    : value;
                if (string.Equals(_cameraMode, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _cameraMode = normalized;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CameraMode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCameraEnabled)));
                CameraModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsCameraEnabled =>
            !string.Equals(
                CameraMode,
                Services.RecipeMachineRuntimeResolver.MultiShotModeDisabled,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Classi Classify (ViDi EL) che NON generano scarto su questa vista, separate da
        /// virgola. Vuoto = nessuna policy per la vista, vale il comportamento storico
        /// (etichette standard OK/GOOD/PASS/PASSED/COMPLIANT).
        /// </summary>
        public string AcceptedClasses
        {
            get => _acceptedClasses;
            set
            {
                if (_acceptedClasses == value)
                {
                    return;
                }

                _acceptedClasses = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AcceptedClasses)));
                AcceptedClassesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Confidenza minima perche' una classe accettata valga come OK su questa vista.
        /// 0 = soglia disattivata. Vedi CameraClassificationPolicy.GetMinimumScore.
        /// </summary>
        public double MinimumClassificationScore
        {
            get => _minimumClassificationScore;
            set
            {
                // Una soglia negativa non ha significato e disattiverebbe il controllo in
                // modo non evidente: si normalizza a 0, che e' gia' il valore di "spenta".
                double sanitized = value < 0d ? 0d : value;
                if (_minimumClassificationScore.Equals(sanitized))
                {
                    return;
                }

                _minimumClassificationScore = sanitized;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinimumClassificationScore)));
                AcceptedClassesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Notifica ogni modifica alla policy di classificazione della vista: classi
        /// accettate o soglia di confidenza. Serve solo a marcare la configurazione come
        /// modificata, quindi un solo evento per entrambe.
        /// </summary>
        public event EventHandler AcceptedClassesChanged;
        public event EventHandler CameraModeChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Etichetta della vista con il nome del job VPP (TOP, SIDE, REAR, LEFT, ...): la chiave
        /// interna resta il ruolo fisico (un job Side e' la camera "left").
        /// </summary>
        public string DisplayName => Models.CameraConfigurationHelper.GetCameraRoleDisplayLabel(CameraRole);

        public ObservableCollection<CameraViewInspectionCell> Inspections { get; }

        public void Attach(PropertyChangedEventHandler handler)
        {
            foreach (var cell in Inspections)
            {
                cell.PropertyChanged += handler;
            }
        }

        public void Detach(PropertyChangedEventHandler handler)
        {
            foreach (var cell in Inspections)
            {
                cell.PropertyChanged -= handler;
            }
        }
    }

    /// <summary>Una cella della matrice: questa vista verifica questa ispezione?</summary>
    public class CameraViewInspectionCell : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string CameraRole { get; set; }
        public string Feature { get; set; }
        public string DisplayName => InspectionFeatureLocalization.GetDisplayName(Feature);

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }

                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class InspectionFeature : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Feature { get; set; }
        public string Description { get; set; }
        public string DisplayName => InspectionFeatureLocalization.GetDisplayName(Feature);
        public string LocalizedDescription => InspectionFeatureLocalization.GetDescription(Feature, Description);

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

      
        public string Icon
        {
            get
            {
                var iconMap = new Dictionary<string, string>
        {
            // Icone vettoriali (Resources/InspectionIcons.xaml). Prima questa mappa duplicava
            // quella di InspectionCounterViewModel e riportava la stessa inversione:
            // Logo -> presenza-marker (che raffigura numeri in posizioni diverse, cioe' la
            // CENTRATURA) e PrintCentering -> centratura-logo (che raffigura un singolo blocco
            // logo, cioe' la PRESENZA). Ora ogni ispezione ha il proprio disegno.
            { "Logo", "InspIcon_Logo" },
            { "ShapeTop", "InspIcon_ShapeTop" },
            { "OpenFlaps", "InspIcon_OpenFlaps" },
            { "SurfaceCheck", "InspIcon_SurfaceCheck" },
            { "PrintCentering", "InspIcon_PrintCentering" },
            { "SideSealing", "InspIcon_SideSealing" },
            { "ShapeSide", "InspIcon_ShapeSide" },
            { "Height", "InspIcon_Height" },
            { "FrontTraceability", "InspIcon_FrontTraceability" },
            { "ThreeDHeight", "InspIcon_ThreeDHeight" },
            { "ThreeDWidth", "InspIcon_ThreeDWidth" },
            { "ThreeDLength", "InspIcon_ThreeDLength" },
            { "BottomSealing", "InspIcon_BottomSealing" },
            { "TrappedPaper", "InspIcon_TrappedPaper" },
            { "AIClassification", "InspIcon_AIClassification" }
        };

                if (iconMap.TryGetValue(Feature, out string icon))
                {
                    return icon;
                }
                else
                {
                    return "COMPLIANT-OK.png";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal static class InspectionFeatureLocalization
    {
        private static readonly Dictionary<string, string> NameKeys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Logo", "Sub_entry_Logo_Isp" },
                { "ShapeTop", "Sub_entry_ShapeTop" },
                { "OpenFlaps", "Sub_entry_OpenFlaps" },
                { "SurfaceCheck", "Sub_entry_SurfaceCheck" },
                { "PrintCentering", "Sub_entry_PrintCentering" },
                { "SideSealing", "Sub_entry_SideSealing" },
                { "SideRollCount", "Sub_entry_SideRollCount" },
                { "ShapeSide", "Sub_entry_ShapeSide" },
                { "Height", "Sub_entry_Height_isp" },
                { "FrontTraceability", "Sub_entry_FrontTraceability_Isp" },
                { "ThreeDHeight", "Sub_entry_ThreeDHeight_Isp" },
                { "ThreeDWidth", "Sub_entry_ThreeDWidth_Isp" },
                { "ThreeDLength", "Sub_entry_ThreeDLength_Isp" },
                { "BottomSealing", "Sub_entry_BottomSealing" },
                { "TrappedPaper", "Sub_entry_TrappedPaper" },
                { "AIClassification", "Sub_entry_AiClassification" }
            };

        private static readonly Dictionary<string, string> DescriptionKeys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Logo", "Sub_entry_Logo_Isp_Descr" },
                { "ShapeTop", "Sub_entry_ShapeTop_descr" },
                { "OpenFlaps", "Sub_entry_OpenFlaps_descr" },
                { "SurfaceCheck", "Sub_entry_SurfaceCheck_descr" },
                { "PrintCentering", "Sub_entry_PrintCentering_descr" },
                { "SideSealing", "Sub_entry_SideSealing_descr" },
                { "SideRollCount", "Sub_entry_SideRollCount_descr" },
                { "ShapeSide", "Sub_entry_ShapeSide_descr" },
                { "Height", "Sub_entry_Height_isp_descr" },
                { "FrontTraceability", "Sub_entry_FrontTraceability_Isp_descr" },
                { "ThreeDHeight", "Sub_entry_ThreeDHeight_Isp_descr" },
                { "ThreeDWidth", "Sub_entry_ThreeDWidth_Isp_descr" },
                { "ThreeDLength", "Sub_entry_ThreeDLength_Isp_descr" },
                { "BottomSealing", "Sub_entry_BottomSealing_descr" },
                { "TrappedPaper", "Sub_entry_TrappedPaper_descr" },
                { "AIClassification", "Sub_entry_AiClassification_descr" }
            };

        public static string GetDisplayName(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature) || !NameKeys.TryGetValue(feature, out string key))
            {
                return feature ?? string.Empty;
            }

            return ServerMessagePersonalize.GetMessageOrDefault(key, feature);
        }

        public static string GetDescription(string feature, string fallback)
        {
            if (string.IsNullOrWhiteSpace(feature) || !DescriptionKeys.TryGetValue(feature, out string key))
            {
                return fallback ?? string.Empty;
            }

            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback ?? feature);
        }
    }

    public static class InspectionDataService
    {
        public static async Task<List<InspectionFeature>> LoadFeaturesAsync(string machineType)
        {
            var features = new List<InspectionFeature>();

            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"SELECT Feature, Description, IsEnabled 
                                     FROM cfg_inspection 
                                     WHERE MachineType = @MachineType 
                                     ORDER BY Feature";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@MachineType", machineType);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                features.Add(new InspectionFeature
                                {
                                    Feature = reader.GetString(0),
                                    Description = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                    IsEnabled = reader.GetBoolean(2)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento delle feature: {ex.Message}");
                throw;
            }

            return features;
        }

        public static async Task SaveFeaturesAsync(IEnumerable<InspectionFeature> features, string machineType)
        {
            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        foreach (var feature in features)
                        {
                            string query = @"INSERT INTO cfg_inspection (Feature, Description, IsEnabled, MachineType) 
                                             VALUES (@Feature, @Description, @IsEnabled, @MachineType)
                                             ON DUPLICATE KEY UPDATE 
                                             Description = VALUES(Description), 
                                             IsEnabled = VALUES(IsEnabled),
                                             LastUpdated = CURRENT_TIMESTAMP";

                            using (var cmd = new MySqlCommand(query, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Feature", feature.Feature);
                                cmd.Parameters.AddWithValue("@Description", feature.Description ?? string.Empty);
                                cmd.Parameters.AddWithValue("@IsEnabled", feature.IsEnabled);
                                cmd.Parameters.AddWithValue("@MachineType", machineType);

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel salvataggio delle feature: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Carica la matrice vista x ispezione da `cfg_inspection_view`.
        /// Una tabella vuota (impianto mai configurato) restituisce una lista vuota, che a
        /// valle significa "nessun override": vale la tabella di applicabilita' storica.
        /// Gli errori DB vengono propagati al servizio, che conserva l'ultimo snapshot
        /// valido invece di scambiarli per una configurazione vuota.
        /// </summary>
        public static async Task<List<DataManage.CameraInspectionCell>> LoadViewMatrixAsync(string machineType)
        {
            var cells = new List<DataManage.CameraInspectionCell>();

            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"SELECT Feature, CameraRole, IsEnabled
                                     FROM cfg_inspection_view
                                     WHERE MachineType = @MachineType";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@MachineType", machineType);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                cells.Add(new DataManage.CameraInspectionCell
                                {
                                    Feature = reader.GetString(0),
                                    CameraRole = reader.GetString(1),
                                    Enabled = reader.GetBoolean(2)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, $"INSPECTION_VIEW_MATRIX_LOAD_FAILED|machineType={machineType}");
                throw;
            }

            return cells;
        }

        /// <summary>
        /// Salva la matrice vista x ispezione. Un payload vuoto non equivale a un reset:
        /// il reset intenzionale usa <see cref="ResetViewConfigurationAsync"/>.
        /// </summary>
        public static async Task SaveViewMatrixAsync(
            IEnumerable<DataManage.CameraInspectionCell> cells,
            string machineType)
        {
            if (cells == null)
            {
                return;
            }

            var payload = cells
                .Where(cell => cell != null &&
                               !string.IsNullOrWhiteSpace(cell.Feature) &&
                               !string.IsNullOrWhiteSpace(cell.CameraRole))
                .ToList();
            if (payload.Count == 0)
            {
                MainWindow.logger?.Warn(
                    $"INSPECTION_VIEW_MATRIX_SAVE_SKIPPED|machineType={machineType}|reason=empty-payload");
                return;
            }

            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        using (var delete = new MySqlCommand(
                            "DELETE FROM cfg_inspection_view WHERE MachineType = @MachineType",
                            conn,
                            transaction))
                        {
                            delete.Parameters.AddWithValue("@MachineType", machineType);
                            await delete.ExecuteNonQueryAsync();
                        }

                        foreach (var cell in payload)
                        {
                            string query = @"INSERT INTO cfg_inspection_view (Feature, CameraRole, MachineType, IsEnabled)
                                             VALUES (@Feature, @CameraRole, @MachineType, @IsEnabled)
                                             ON DUPLICATE KEY UPDATE
                                             IsEnabled = VALUES(IsEnabled),
                                             LastUpdated = CURRENT_TIMESTAMP";

                            using (var cmd = new MySqlCommand(query, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Feature", cell.Feature);
                                cmd.Parameters.AddWithValue("@CameraRole", cell.CameraRole);
                                cmd.Parameters.AddWithValue("@MachineType", machineType);
                                cmd.Parameters.AddWithValue("@IsEnabled", cell.Enabled);

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, $"INSPECTION_VIEW_MATRIX_SAVE_FAILED|machineType={machineType}");
                throw;
            }
        }

        /// <summary>Classi Classify accettate per vista. Tabella vuota = comportamento storico.</summary>
        public static async Task<List<DataManage.CameraClassificationRule>> LoadClassificationRulesAsync(string machineType)
        {
            var rules = new List<DataManage.CameraClassificationRule>();

            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"SELECT CameraRole, AcceptedClasses, MinimumScore
                                     FROM cfg_view_classification
                                     WHERE MachineType = @MachineType";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@MachineType", machineType);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                rules.Add(new DataManage.CameraClassificationRule
                                {
                                    CameraRole = reader.GetString(0),
                                    AcceptedClasses = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                    MinimumScore = reader.IsDBNull(2) ? 0d : reader.GetDouble(2)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, $"CLASSIFICATION_RULES_LOAD_FAILED|machineType={machineType}");
                throw;
            }

            return rules;
        }

        public static async Task SaveClassificationRulesAsync(
            IEnumerable<DataManage.CameraClassificationRule> rules,
            string machineType)
        {
            if (rules == null)
            {
                return;
            }

            var payload = rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.CameraRole)).ToList();
            if (payload.Count == 0)
            {
                MainWindow.logger?.Warn(
                    $"CLASSIFICATION_RULES_SAVE_SKIPPED|machineType={machineType}|reason=empty-payload");
                return;
            }

            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        using (var delete = new MySqlCommand(
                            "DELETE FROM cfg_view_classification WHERE MachineType = @MachineType",
                            conn,
                            transaction))
                        {
                            delete.Parameters.AddWithValue("@MachineType", machineType);
                            await delete.ExecuteNonQueryAsync();
                        }

                        foreach (var rule in payload)
                        {
                            string query = @"INSERT INTO cfg_view_classification (CameraRole, MachineType, AcceptedClasses, MinimumScore)
                                             VALUES (@CameraRole, @MachineType, @AcceptedClasses, @MinimumScore)
                                             ON DUPLICATE KEY UPDATE
                                             AcceptedClasses = VALUES(AcceptedClasses),
                                             MinimumScore = VALUES(MinimumScore),
                                             LastUpdated = CURRENT_TIMESTAMP";

                            using (var cmd = new MySqlCommand(query, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@CameraRole", rule.CameraRole);
                                cmd.Parameters.AddWithValue("@MachineType", machineType);
                                cmd.Parameters.AddWithValue("@AcceptedClasses", rule.AcceptedClasses ?? string.Empty);
                                cmd.Parameters.AddWithValue("@MinimumScore", rule.MinimumScore);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, $"CLASSIFICATION_RULES_SAVE_FAILED|machineType={machineType}");
                throw;
            }
        }

        public static async Task ResetToDefaultAsync(string machineType)
        {
            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"UPDATE cfg_inspection 
                                     SET IsEnabled = TRUE,
                                         LastUpdated = CURRENT_TIMESTAMP
                                     WHERE MachineType = @MachineType";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@MachineType", machineType);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel reset delle feature: {ex.Message}");
                throw;
            }
        }

        public static async Task ResetViewConfigurationAsync(string machineType)
        {
            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        foreach (string table in new[] { "cfg_inspection_view", "cfg_view_classification" })
                        {
                            using (var cmd = new MySqlCommand(
                                $"DELETE FROM {table} WHERE MachineType = @MachineType",
                                conn,
                                transaction))
                            {
                                cmd.Parameters.AddWithValue("@MachineType", machineType);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex,
                    $"INSPECTION_VIEW_CONFIGURATION_RESET_FAILED|machineType={machineType}");
                throw;
            }
        }

        public static async Task<Dictionary<string, bool>> GetEnabledFeaturesAsync(string machineType)
        {
            var enabledFeatures = new Dictionary<string, bool>();

            try
            {
                var features = await LoadFeaturesAsync(machineType);
                foreach (var feature in features)
                {
                    enabledFeatures[feature.Feature] = feature.IsEnabled;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Impossibile caricare le feature abilitate: {ex.Message}");
                // Ritorna tutte le feature abilitate come fallback
                enabledFeatures = machineType == "3DCheck"
                    ? new Dictionary<string, bool>
                    {
                        { "ThreeDHeight", true },
                        { "ThreeDWidth", true },
                        { "ThreeDLength", true },
                        { "FrontTraceability", false }
                    }
                    : new Dictionary<string, bool>
                    {
                        { "Logo", true },
                        { "ShapeTop", true },
                        { "OpenFlaps", true },
                        { "SurfaceCheck", true },
                        { "PrintCentering", true },
                        { "SideSealing", true },
                        { "ShapeSide", true },
                        { "Height", true },
                        { "FrontTraceability", true },
                        { "BottomSealing", true },
                        { "TrappedPaper", true },
                        { "AIClassification", false }
                    };
            }

            return enabledFeatures;
        }
    }
}
