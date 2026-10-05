using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Models;
using QtisVisionPanel.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QtisVisionPanel.DataManage
{
    /// <summary>
    /// Keeps the active inspection-feature set aligned with both historical
    /// machine defaults and the currently loaded production recipe/runtime.
    ///
    /// The DB-backed configuration remains available as a baseline, but the
    /// effective runtime set can be overridden by the active recipe + VPP jobs
    /// + supported toolblock outputs. This is what validators, counters and
    /// camera panels should use during production.
    /// </summary>
    public class InspectionConfigService
    {
        private static readonly string[] KnownFeatures =
        {
            "Logo",
            "PrintCentering",
            "OpenFlaps",
            "SurfaceCheck",
            "Height",
            "SideSealing",
            "SideRollCount",
            "ShapeTop",
            "ShapeSide",
            "FrontTraceability",
            "ThreeDHeight",
            "ThreeDWidth",
            "ThreeDLength",
            "BottomSealing",
            "TrappedPaper",
            "AIClassification"
        };

        private Dictionary<string, bool> _configuredFeatures;
        private Dictionary<string, bool> _runtimeFeatures;
        private string _currentMachineType;

        // Matrice vista x ispezione. Sostituita per intero ad ogni ricarica (mai mutata in
        // place) perche' viene letta dal thread di ispezione mentre la UI puo' salvarla.
        private volatile CameraInspectionMatrix _viewMatrix = new CameraInspectionMatrix();
        private volatile CameraInspectionMatrix _machineViewMatrix = new CameraInspectionMatrix();
        private volatile CameraClassificationPolicy _machineClassificationPolicy = new CameraClassificationPolicy();
        private volatile HashSet<string> _disabledCameraRoles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile HashSet<string> _activeCameraRoles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile bool _runtimeUsesRecipeViewConfiguration;

        public event EventHandler FeaturesChanged;

        public InspectionConfigService()
        {
            _configuredFeatures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _runtimeFeatures = null;
        }

        /// <summary>Elenco delle ispezioni note, per la UI della matrice.</summary>
        public static IReadOnlyList<string> AllKnownFeatures => KnownFeatures;

        /// <summary>Matrice corrente vista x ispezione (mai null).</summary>
        public CameraInspectionMatrix ViewMatrix => _viewMatrix ?? new CameraInspectionMatrix();

        /// <summary>Default macchina usato solamente dalle ricette legacy.</summary>
        public CameraInspectionMatrix MachineViewMatrix => _machineViewMatrix ?? new CameraInspectionMatrix();

        private volatile CameraClassificationPolicy _classificationPolicy = new CameraClassificationPolicy();

        /// <summary>Classi Classify accettate per vista (mai null).</summary>
        public CameraClassificationPolicy ClassificationPolicy =>
            _classificationPolicy ?? new CameraClassificationPolicy();

        public CameraClassificationPolicy MachineClassificationPolicy =>
            _machineClassificationPolicy ?? new CameraClassificationPolicy();

        public void ApplyClassificationPolicy(CameraClassificationPolicy policy)
        {
            _machineClassificationPolicy = policy ?? new CameraClassificationPolicy();
            if (!_runtimeUsesRecipeViewConfiguration)
            {
                _classificationPolicy = _machineClassificationPolicy;
            }
            OnFeaturesChanged();
        }

        /// <summary>
        /// Applica la matrice configurata dall'operatore. Una matrice vuota o null riporta
        /// al comportamento storico (solo default di applicabilita').
        /// </summary>
        public void ApplyViewMatrix(CameraInspectionMatrix matrix)
        {
            _machineViewMatrix = matrix ?? new CameraInspectionMatrix();
            if (!_runtimeUsesRecipeViewConfiguration)
            {
                _viewMatrix = _machineViewMatrix;
            }
            OnFeaturesChanged();
        }

        public async Task LoadFeaturesAsync(string machineType)
        {
            _currentMachineType = machineType;
            _configuredFeatures = await InspectionDataService.GetEnabledFeaturesAsync(machineType);

            // La matrice vista x ispezione va caricata anche all'avvio, non solo quando
            // l'operatore apre la pagina Inspection Configuration: altrimenti il validatore
            // ignorerebbe gli override salvati fino alla prima visita di quella pagina.
            try
            {
                var cells = await InspectionDataService.LoadViewMatrixAsync(machineType);
                _machineViewMatrix = new CameraInspectionMatrix(cells);
                if (!_runtimeUsesRecipeViewConfiguration)
                {
                    _viewMatrix = _machineViewMatrix;
                }
                if (cells.Count > 0)
                {
                    MainWindow.logger?.Info(
                        $"INSPECTION_VIEW_MATRIX_ACTIVE|machineType={machineType}|cells={cells.Count}");
                }
            }
            catch (Exception ex)
            {
                // Mantiene l'ultimo snapshot valido: un guasto DB non deve cambiare
                // improvvisamente le ispezioni richieste durante la produzione.
                MainWindow.logger?.Warn(ex, $"INSPECTION_VIEW_MATRIX_UNAVAILABLE|machineType={machineType}");
            }

            try
            {
                var rules = await InspectionDataService.LoadClassificationRulesAsync(machineType);
                _machineClassificationPolicy = new CameraClassificationPolicy(rules);
                if (!_runtimeUsesRecipeViewConfiguration)
                {
                    _classificationPolicy = _machineClassificationPolicy;
                }
                if (rules.Count > 0)
                {
                    MainWindow.logger?.Info(
                        $"CLASSIFICATION_POLICY_ACTIVE|machineType={machineType}|views={rules.Count}");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, $"CLASSIFICATION_POLICY_UNAVAILABLE|machineType={machineType}");
            }

            OnFeaturesChanged();
        }

        public async Task SaveFeaturesAsync(IEnumerable<InspectionFeature> features, string machineType)
        {
            await InspectionDataService.SaveFeaturesAsync(features, machineType);
            await LoadFeaturesAsync(machineType);
        }

        public void ApplyRuntimeRecipeFeatures(
            RecipeParameters.RecipeData recipeData,
            IDictionary<int, string> roleMapping,
            IDictionary<string, IReadOnlyCollection<string>> supportedFeaturesByRole)
        {
            _disabledCameraRoles = BuildDisabledCameraRoleSet(recipeData,
                !CameraConfigurationHelper.HasCameraRole(roleMapping, "right") &&
                CameraConfigurationHelper.HasCameraRole(roleMapping, "rear"));
            _activeCameraRoles = BuildActiveCameraRoleSet(roleMapping, supportedFeaturesByRole, _disabledCameraRoles);
            _runtimeFeatures = BuildRuntimeFeatureMap(recipeData, roleMapping, supportedFeaturesByRole);

            if (RecipeInspectionViewConfigurationMapper.TryBuild(
                    recipeData,
                    out CameraInspectionMatrix recipeMatrix,
                    out CameraClassificationPolicy recipePolicy,
                    !CameraConfigurationHelper.HasCameraRole(roleMapping, "right") &&
                    CameraConfigurationHelper.HasCameraRole(roleMapping, "rear")))
            {
                _viewMatrix = recipeMatrix;
                _classificationPolicy = recipePolicy;
                _runtimeUsesRecipeViewConfiguration = true;
                MainWindow.logger?.Info(
                    $"RECIPE_INSPECTION_VIEW_PROFILE_ACTIVE|recipe={recipeData?.general_Info?.RecipeName ?? "-"}" +
                    $"|cells={recipeMatrix.ExplicitCellCount}" +
                    $"|disabledCameras={string.Join(",", _disabledCameraRoles.OrderBy(role => role))}");
            }
            else
            {
                _viewMatrix = MachineViewMatrix;
                _classificationPolicy = MachineClassificationPolicy;
                _runtimeUsesRecipeViewConfiguration = false;
                MainWindow.logger?.Info(
                    $"RECIPE_INSPECTION_VIEW_PROFILE_LEGACY_FALLBACK|recipe={recipeData?.general_Info?.RecipeName ?? "-"}" +
                    $"|machineType={_currentMachineType ?? "-"}");
            }

            OnFeaturesChanged();
        }

        public void ClearRuntimeRecipeFeatures()
        {
            _runtimeFeatures = null;
            _disabledCameraRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _activeCameraRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _runtimeUsesRecipeViewConfiguration = false;
            _viewMatrix = MachineViewMatrix;
            _classificationPolicy = MachineClassificationPolicy;
            OnFeaturesChanged();
        }

        /// <summary>
        /// Camera attiva per il prodotto corrente. Il flag deriva dallo stesso
        /// CameraTriggers della ricetta che abilita il punto fisico di trigger.
        /// </summary>
        public bool IsCameraRoleEnabled(string cameraRole)
        {
            string normalizedRole = CameraInspectionMatrix.NormalizeRole(cameraRole);
            return string.IsNullOrWhiteSpace(normalizedRole) ||
                   !_disabledCameraRoles.Contains(normalizedRole);
        }

        public bool IsFeatureEnabled(string feature)
        {
            string normalizedFeature = NormalizeFeatureKey(feature);

            if (!IsFeatureEnabledAtGlobalLevel(normalizedFeature))
            {
                return false;
            }

            HashSet<string> activeRoles = _activeCameraRoles;
            return activeRoles == null || activeRoles.Count == 0 ||
                   activeRoles.Any(role => ViewMatrix.IsApplicable(role, normalizedFeature));
        }

        private bool IsFeatureEnabledAtGlobalLevel(string normalizedFeature)
        {

            if (_runtimeFeatures != null && _runtimeFeatures.TryGetValue(normalizedFeature, out bool runtimeEnabled))
            {
                return runtimeEnabled;
            }

            if (_configuredFeatures != null && _configuredFeatures.TryGetValue(normalizedFeature, out bool configuredEnabled))
            {
                return configuredEnabled;
            }

            return true;
        }

        /// <summary>
        /// Variante consapevole della vista: l'ispezione e' attiva solo se lo e' a livello
        /// globale (ricetta/profilo macchina, invariato) E la matrice dice che QUESTA vista
        /// la verifica.
        ///
        /// E' il punto che elimina i falsi scarti: prima la Rear ereditava
        /// SideSealing dal flag globale e il validatore ne pretendeva l'output anche quando
        /// il ToolBlock Rear non lo espone. Senza configurazione esplicita il default
        /// riproduce la tabella di applicabilita' storica, quindi nulla cambia finche'
        /// l'operatore non interviene dalla pagina Inspection Configuration.
        /// </summary>
        public bool IsFeatureEnabled(string cameraRole, string feature)
        {
            string normalizedFeature = NormalizeFeatureKey(feature);
            if (!IsCameraRoleEnabled(cameraRole) || !IsFeatureEnabledAtGlobalLevel(normalizedFeature))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(cameraRole))
            {
                return true;
            }

            return ViewMatrix.IsApplicable(cameraRole, normalizedFeature);
        }

        /// <summary>Normalizzazione condivisa con <see cref="CameraInspectionMatrix"/>.</summary>
        public static string NormalizeFeatureKeyPublic(string feature)
        {
            return NormalizeFeatureKey(feature);
        }

        public Dictionary<string, bool> GetEnabledFeatures()
        {
            if (_runtimeFeatures != null)
            {
                return new Dictionary<string, bool>(_runtimeFeatures, StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, bool>(_configuredFeatures, StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, bool> BuildRuntimeFeatureMap(
            RecipeParameters.RecipeData recipeData,
            IDictionary<int, string> roleMapping,
            IDictionary<string, IReadOnlyCollection<string>> supportedFeaturesByRole)
        {
            var runtimeFeatures = KnownFeatures.ToDictionary(
                feature => feature,
                _ => false,
                StringComparer.OrdinalIgnoreCase);

            var status = recipeData?.inspectionStatus ?? RecipeParameters.InspectionStatus.FromEjectionStatus(recipeData?.ejectionStatus);

            bool hasTop = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "top");
            bool hasTop3D = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "top3d");
            bool hasSide = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "side");
            bool hasLeft = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "left");
            bool hasFront = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "front");
            bool hasRear = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "rear");
            bool hasRight = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "right");
            bool hasBottom = RoleHasFeatureScope(roleMapping, supportedFeaturesByRole, "bottom");

            // Side e Left sono la stessa camera fisica: un job "Side" viene risolto a runtime
            // come "left" (NormalizeCameraDisplayType), quindi le ispezioni storiche della vista
            // laterale devono restare disponibili qualunque dei due nomi abbia il job.
            bool hasLeftCamera = hasSide || hasLeft;

            // The active recipe remains the source of truth for which inspections
            // are enabled. Job/output detection is used to understand which camera
            // roles exist at runtime, but it must not silently hide recipe-enabled
            // inspections from counters and operator panels.
            runtimeFeatures["Logo"] = hasTop && status.logo;
            runtimeFeatures["PrintCentering"] = hasTop && status.Print_centering;
            runtimeFeatures["OpenFlaps"] = hasTop && status.OpenFlaps;
            runtimeFeatures["SurfaceCheck"] = hasTop && status.SurfaceCheck;
            runtimeFeatures["ShapeTop"] = hasTop && status.ShapeTop;

            runtimeFeatures["Height"] = hasLeftCamera && status.Height;
            runtimeFeatures["SideSealing"] = (hasLeftCamera || hasRight || hasRear) && status.Side_sealing;
            runtimeFeatures["SideRollCount"] = hasLeftCamera &&
                (SupportedRoleHasFeature(supportedFeaturesByRole, "left", "SideRollCount") ||
                 SupportedRoleHasFeature(supportedFeaturesByRole, "side", "SideRollCount"));
            runtimeFeatures["ShapeSide"] = (hasLeftCamera || hasRight || hasRear) && status.ShapeSide;

            runtimeFeatures["FrontTraceability"] = hasFront && status.FrontTraceability;

            runtimeFeatures["ThreeDHeight"] = hasTop3D && status.ThreeDHeight;
            runtimeFeatures["ThreeDWidth"] = hasTop3D && status.ThreeDWidth;
            runtimeFeatures["ThreeDLength"] = hasTop3D && status.ThreeDLength;
            runtimeFeatures["BottomSealing"] = hasBottom && status.BottomSealing;
            runtimeFeatures["TrappedPaper"] = hasBottom && status.TrappedPaper;
            runtimeFeatures["AIClassification"] =
                (hasTop || hasTop3D || hasLeftCamera || hasFront || hasRight || hasRear || hasBottom) &&
                status.AIClassification;

            return runtimeFeatures;
        }

        private static HashSet<string> BuildDisabledCameraRoleSet(RecipeParameters.RecipeData recipeData, bool legacyRightIsRear)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RecipeParameters.RecipeCameraTriggerAdjustments triggers =
                recipeData?.machineRuntimeAdjustments?.CameraTriggers;
            if (triggers == null)
            {
                return result;
            }

            if (string.Equals(triggers.Top?.Trim(), "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                MainWindow.logger?.Warn(
                    "RECIPE_PRIMARY_CAMERA_DISABLE_IGNORED|role=top/top3d|reason=primary-orchestrator-anchor");
            }
            AddDisabledRole(result, "left", ResolvePhysicalCameraMode(triggers.Left, triggers.Side));
            AddDisabledRole(result, "front", triggers.Front);
            AddDisabledRole(result, "right", legacyRightIsRear ? null : triggers.Right);
            AddDisabledRole(result, "rear", legacyRightIsRear
                ? ResolvePhysicalCameraMode(triggers.Rear, triggers.Right)
                : triggers.Rear);
            AddDisabledRole(result, "bottom", triggers.Bottom);
            return result;
        }

        private static string ResolvePhysicalCameraMode(string physicalMode, string legacyMode)
        {
            return string.IsNullOrWhiteSpace(physicalMode) ||
                   string.Equals(physicalMode.Trim(), "Machine", StringComparison.OrdinalIgnoreCase)
                ? legacyMode
                : physicalMode;
        }

        private static HashSet<string> BuildActiveCameraRoleSet(
            IDictionary<int, string> roleMapping,
            IDictionary<string, IReadOnlyCollection<string>> supportedFeaturesByRole,
            ISet<string> disabledRoles)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string role in roleMapping?.Values ?? Enumerable.Empty<string>())
            {
                string normalized = CameraInspectionMatrix.NormalizeRole(role);
                if (!string.IsNullOrWhiteSpace(normalized) && disabledRoles?.Contains(normalized) != true)
                {
                    result.Add(normalized);
                }
            }

            foreach (string role in supportedFeaturesByRole?.Keys ?? Enumerable.Empty<string>())
            {
                string normalized = CameraInspectionMatrix.NormalizeRole(role);
                if (!string.IsNullOrWhiteSpace(normalized) && disabledRoles?.Contains(normalized) != true)
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        private static void AddDisabledRole(ISet<string> roles, string role, string mode)
        {
            if (string.Equals(mode?.Trim(), "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add(CameraInspectionMatrix.NormalizeRole(role));
            }
        }

        private bool RoleHasFeatureScope(
            IDictionary<int, string> roleMapping,
            IDictionary<string, IReadOnlyCollection<string>> supportedFeaturesByRole,
            string role)
        {
            string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(role);
            if (!IsCameraRoleEnabled(normalizedRole))
            {
                return false;
            }

            bool roleConfigured = CameraConfigurationHelper.HasCameraRole(roleMapping, normalizedRole);
            bool roleDetected = supportedFeaturesByRole != null &&
                supportedFeaturesByRole.Keys.Any(key =>
                    string.Equals(CameraConfigurationHelper.NormalizeCameraType(key), normalizedRole, StringComparison.OrdinalIgnoreCase));

            return roleConfigured || roleDetected;
        }

        private static bool SupportedRoleHasFeature(
            IDictionary<string, IReadOnlyCollection<string>> supportedFeaturesByRole,
            string role,
            string feature)
        {
            if (supportedFeaturesByRole == null || string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(feature))
            {
                return false;
            }

            string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(role);
            foreach (var entry in supportedFeaturesByRole)
            {
                if (!string.Equals(CameraConfigurationHelper.NormalizeCameraType(entry.Key), normalizedRole, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return entry.Value != null &&
                       entry.Value.Any(current => string.Equals(current, feature, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        private static string NormalizeFeatureKey(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature))
            {
                return string.Empty;
            }

            switch (feature.Trim())
            {
                case "Print_centering":
                    return "PrintCentering";
                case "Side_sealing":
                    return "SideSealing";
                case "Bottom_sealing":
                case "Bottom seal":
                case "Bottom sealing":
                case "SaldaturaInferiore":
                case "Saldatura inferiore":
                    return "BottomSealing";
                case "Trapped paper":
                case "Paper trapped":
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
                    return feature.Trim();
            }
        }

        protected virtual void OnFeaturesChanged()
        {
            FeaturesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
