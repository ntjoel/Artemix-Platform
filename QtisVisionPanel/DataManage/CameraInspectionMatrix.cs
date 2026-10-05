using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.DataManage
{
    /// <summary>
    /// Matrice esplicita "vista camera x ispezione".
    ///
    /// Prima di questa classe l'insieme delle ispezioni attive era una mappa piatta
    /// feature -> bool, e il ruolo camera entrava solo come OR-gate
    /// (es. SideSealing = (side || left || rear) &amp;&amp; status.Side_sealing). Il risultato
    /// era che abilitare la sigillatura per la Side la rendeva obbligatoria anche sulla
    /// Rear, il cui ToolBlock non espone alcun output di sigillatura: ogni prodotto
    /// veniva scartato con "Right side sealing output missing".
    ///
    /// Qui la relazione diventa esplicita e per singola vista. Regole:
    ///
    /// - <see cref="IsApplicable"/> dice se una vista PUO' verificare quell'ispezione.
    ///   I profili macchina e le ricette legacy usano la tabella di applicabilita'
    ///   storica per le celle mancanti.
    /// - una ricetta salvata con profilo vista esplicito usa invece una matrice strict:
    ///   le celle mancanti restano disabilitate, cosi' una futura nuova ispezione non
    ///   diventa obbligatoria su ricette gia' qualificate.
    /// - <see cref="SetExplicit"/> registra la scelta dell'operatore per una cella.
    ///   Solo le celle configurate esplicitamente sovrascrivono il default: una matrice
    ///   vuota non disabilita nulla (le ricette esistenti continuano a funzionare).
    ///
    /// La classe e' immutabile dopo la costruzione dal punto di vista dei consumatori sul
    /// percorso di ispezione: <see cref="InspectionConfigService"/> la sostituisce
    /// interamente ad ogni ricarica invece di mutarla in place.
    /// </summary>
    public sealed class CameraInspectionMatrix
    {
        private readonly Dictionary<string, bool> _explicitCells;
        private readonly bool _useDefaultsForMissingCells;

        public CameraInspectionMatrix()
            : this(null, true)
        {
        }

        public CameraInspectionMatrix(IEnumerable<CameraInspectionCell> cells)
            : this(cells, true)
        {
        }

        public CameraInspectionMatrix(
            IEnumerable<CameraInspectionCell> cells,
            bool useDefaultsForMissingCells)
        {
            _useDefaultsForMissingCells = useDefaultsForMissingCells;
            _explicitCells = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (cells == null)
            {
                return;
            }

            // Le celle legacy Side/Rear sono lette per prime. Se la ricetta ha
            // anche una cella Left/Right esplicita, prevale la scelta della
            // camera fisica a prescindere dall'ordine delle righe XML.
            foreach (var cell in cells.OrderBy(cell => IsLegacyPhysicalAlias(cell?.CameraRole) ? 0 : 1))
            {
                if (cell == null)
                {
                    continue;
                }

                string key = BuildKey(cell.CameraRole, cell.Feature);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                _explicitCells[key] = cell.Enabled;
            }
        }

        /// <summary>Numero di celle configurate esplicitamente.</summary>
        public int ExplicitCellCount => _explicitCells.Count;

        public bool HasExplicitConfiguration => _explicitCells.Count > 0;

        /// <summary>
        /// True se questa vista ha una scelta esplicita registrata per l'ispezione indicata.
        /// </summary>
        public bool HasExplicitCell(string cameraRole, string feature)
        {
            string key = BuildKey(cameraRole, feature);
            return !string.IsNullOrEmpty(key) && _explicitCells.ContainsKey(key);
        }

        /// <summary>
        /// True se la vista deve verificare l'ispezione. Usa la scelta esplicita quando
        /// presente; per una matrice macchina usa il default storico, mentre per una
        /// matrice ricetta strict lascia disabilitata la cella mancante.
        /// </summary>
        public bool IsApplicable(string cameraRole, string feature)
        {
            string key = BuildKey(cameraRole, feature);
            if (string.IsNullOrEmpty(key))
            {
                return _useDefaultsForMissingCells;
            }

            if (_explicitCells.TryGetValue(key, out bool configured))
            {
                return configured;
            }

            return _useDefaultsForMissingCells && DefaultAppliesTo(cameraRole, feature);
        }

        public IReadOnlyDictionary<string, bool> GetExplicitCells()
        {
            return new Dictionary<string, bool>(_explicitCells, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Tabella di applicabilita' predefinita: quali viste possono, per costruzione
        /// meccanica della macchina, verificare quale ispezione. Riproduce la logica che
        /// prima era sparsa fra InspectionConfigService.BuildRuntimeFeatureMap e
        /// InspectionConfigViewModel.ShouldIncludeFeatureForCurrentRuntime, cosi' che
        /// l'assenza di configurazione esplicita non cambi il comportamento in campo.
        /// </summary>
        public static bool DefaultAppliesTo(string cameraRole, string feature)
        {
            string role = NormalizeRole(cameraRole);
            string key = NormalizeFeature(feature);
            if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(key))
            {
                return true;
            }

            switch (key)
            {
                case "Logo":
                case "PrintCentering":
                case "OpenFlaps":
                case "SurfaceCheck":
                case "ShapeTop":
                    return role == "top";

                case "Height":
                    return role == "left";

                case "SideSealing":
                    return role == "left" || role == "right" || role == "rear";

                case "ShapeSide":
                    return role == "left" || role == "right" || role == "rear";

                case "SideRollCount":
                    return role == "left";

                case "FrontTraceability":
                    return role == "front";

                case "ThreeDHeight":
                case "ThreeDWidth":
                case "ThreeDLength":
                    return role == "top3d";

                case "BottomSealing":
                case "TrappedPaper":
                    return role == "bottom";

                case "AIClassification":
                    // La classificazione AI e' un tool di ToolBlock: qualunque vista puo'
                    // esporla, quindi resta disponibile su tutti i ruoli supportati.
                    return true;

                default:
                    // Feature sconosciuta: coerente con InspectionConfigService.IsFeatureEnabled,
                    // che per una chiave non mappata restituisce true.
                    return true;
            }
        }

        /// <summary>Ruoli camera per cui ha senso costruire una colonna della matrice.</summary>
        public static IReadOnlyList<string> SupportedRoles { get; } = new[]
        {
            "top",
            "top3d",
            "left",
            "front",
            "right",
            "rear",
            "bottom"
        };

        /// <summary>
        /// Ispezioni che il ruolo indicato puo' verificare secondo il default meccanico.
        /// Usata dalla UI per non proporre celle prive di senso (es. Logo sulla Bottom).
        /// </summary>
        public static IReadOnlyList<string> ApplicableFeaturesForRole(string cameraRole, IEnumerable<string> knownFeatures)
        {
            if (knownFeatures == null)
            {
                return Array.Empty<string>();
            }

            return knownFeatures
                .Where(feature => DefaultAppliesTo(cameraRole, feature))
                .ToList();
        }

        public static string NormalizeRole(string cameraRole)
        {
            if (string.IsNullOrWhiteSpace(cameraRole))
            {
                return string.Empty;
            }

            string normalized = CameraConfigurationHelper.NormalizeCameraType(cameraRole);
            if (string.Equals(normalized, "side", StringComparison.OrdinalIgnoreCase)) normalized = "left";
            return string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : normalized.Trim().ToLowerInvariant();
        }

        internal static bool IsLegacyPhysicalAlias(string role) =>
            string.Equals(role?.Trim(), "side", StringComparison.OrdinalIgnoreCase);

        public static string NormalizeFeature(string feature)
        {
            return InspectionConfigService.NormalizeFeatureKeyPublic(feature);
        }

        private static string BuildKey(string cameraRole, string feature)
        {
            string role = NormalizeRole(cameraRole);
            string key = NormalizeFeature(feature);
            if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return role + "|" + key;
        }
    }

    /// <summary>Una cella configurata esplicitamente dall'operatore.</summary>
    public sealed class CameraInspectionCell
    {
        public string CameraRole { get; set; }
        public string Feature { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Classi accettate dal tool Classify (ViDi EL) per una singola vista.
    ///
    /// Serve perche' la tassonomia di un modello Edge Learning e' addestrata per camera:
    /// la Top puo' produrre "pieghe top", la Rear "bad Trasversal sealing", la Side
    /// "Paper Shift". Prima esisteva solo una lista fissa di etichette di OK
    /// (OK/GOOD/PASS/PASSED/COMPLIANT) in VisionClassificationResultReader: qualunque
    /// classe fuori da quella lista veniva risolta come "Informational" e il validatore
    /// trattava Informational come scarto. Con una tassonomia custom questo significa
    /// scartare ogni pezzo, che e' esattamente cio' che si vedeva nei log.
    /// </summary>
    public sealed class CameraClassificationPolicy
    {
        private readonly Dictionary<string, HashSet<string>> _acceptedByRole;
        private readonly Dictionary<string, double> _minimumScoreByRole;

        public CameraClassificationPolicy()
            : this(null)
        {
        }

        public CameraClassificationPolicy(IEnumerable<CameraClassificationRule> rules)
        {
            _acceptedByRole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            _minimumScoreByRole = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (rules == null)
            {
                return;
            }

            foreach (var rule in rules.OrderBy(rule =>
                string.Equals(rule?.CameraRole?.Trim(), "side", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule?.CameraRole?.Trim(), "rear", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                if (rule == null)
                {
                    continue;
                }

                string role = CameraInspectionMatrix.NormalizeRole(rule.CameraRole);
                if (string.IsNullOrEmpty(role))
                {
                    continue;
                }

                if (rule.MinimumScore > 0d)
                {
                    // Registrata separatamente dalla tassonomia: una vista puo' avere una
                    // soglia di confidenza pur affidandosi alle etichette standard.
                    _minimumScoreByRole[role] = rule.MinimumScore;
                }

                var accepted = ParseClasses(rule.AcceptedClasses);
                if (accepted.Count == 0)
                {
                    continue;
                }

                _acceptedByRole[role] = accepted;
            }
        }

        public bool HasPolicyFor(string cameraRole)
        {
            string role = CameraInspectionMatrix.NormalizeRole(cameraRole);
            return !string.IsNullOrEmpty(role) && _acceptedByRole.ContainsKey(role);
        }

        /// <summary>
        /// True se la classe e' dichiarata accettabile per quella vista. Da chiamare solo
        /// quando <see cref="HasPolicyFor"/> e' true: senza policy vale il comportamento
        /// storico di <c>VisionClassificationResultReader</c>.
        /// </summary>
        public bool IsAccepted(string cameraRole, string className)
        {
            string role = CameraInspectionMatrix.NormalizeRole(cameraRole);
            if (string.IsNullOrEmpty(role) || string.IsNullOrWhiteSpace(className))
            {
                return false;
            }

            return _acceptedByRole.TryGetValue(role, out var accepted) &&
                   accepted.Contains(className.Trim());
        }

        /// <summary>
        /// Confidenza minima configurata per la vista, oppure 0 se non impostata.
        ///
        /// Sotto questa soglia la classe non viene considerata riconosciuta: il pezzo non
        /// e' "OK con poca confidenza" ma un caso non verificato — puo' essere un prodotto
        /// diverso, o una condizione che il modello non ha mai visto in addestramento.
        /// </summary>
        public double GetMinimumScore(string cameraRole)
        {
            string role = CameraInspectionMatrix.NormalizeRole(cameraRole);
            if (string.IsNullOrEmpty(role) || _minimumScoreByRole == null)
            {
                return 0d;
            }

            return _minimumScoreByRole.TryGetValue(role, out double minimum) ? minimum : 0d;
        }

        public string GetAcceptedClassesText(string cameraRole)
        {
            string role = CameraInspectionMatrix.NormalizeRole(cameraRole);
            if (string.IsNullOrEmpty(role) || !_acceptedByRole.TryGetValue(role, out var accepted))
            {
                return string.Empty;
            }

            return string.Join(", ", accepted.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Separatori ammessi: virgola, punto e virgola, a capo.</summary>
        public static HashSet<string> ParseClasses(string text)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            foreach (string token in text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = token.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }
    }

    /// <summary>Classi accettate per una vista, come configurate dall'operatore.</summary>
    public sealed class CameraClassificationRule
    {
        public string CameraRole { get; set; }

        /// <summary>Elenco separato da virgole delle classi che NON generano scarto.</summary>
        public string AcceptedClasses { get; set; }

        /// <summary>
        /// Confidenza minima richiesta per accettare la classe. 0 = soglia disattivata.
        /// </summary>
        public double MinimumScore { get; set; }
    }

    /// <summary>
    /// Conversione tra il contratto XML di ricetta e gli snapshot immutabili letti
    /// dal percorso di ispezione. Una configurazione ricetta esplicita e' strict:
    /// una cella assente resta disabilitata invece di ereditare nuovi default.
    /// </summary>
    public static class RecipeInspectionViewConfigurationMapper
    {
        public static bool TryBuild(
            RecipeParameters.RecipeData recipe,
            out CameraInspectionMatrix matrix,
            out CameraClassificationPolicy classificationPolicy,
            bool legacyRightIsRear = false)
        {
            matrix = null;
            classificationPolicy = null;

            RecipeParameters.RecipeInspectionViewConfiguration configuration =
                recipe?.inspectionViewConfiguration;
            if (configuration?.IsConfigured != true)
            {
                return false;
            }

            var cells = new List<CameraInspectionCell>();
            var rules = new List<CameraClassificationRule>();
            foreach (RecipeParameters.RecipeCameraInspectionView view in
                     configuration.Views ?? new List<RecipeParameters.RecipeCameraInspectionView>())
            {
                string role = CameraInspectionMatrix.NormalizeRole(view?.CameraRole);
                if (legacyRightIsRear && string.Equals(role, "right", StringComparison.OrdinalIgnoreCase))
                {
                    role = "rear";
                }
                if (string.IsNullOrWhiteSpace(role))
                {
                    continue;
                }

                foreach (RecipeParameters.RecipeInspectionFeatureAssignment assignment in
                         view.Inspections ?? new List<RecipeParameters.RecipeInspectionFeatureAssignment>())
                {
                    string feature = CameraInspectionMatrix.NormalizeFeature(assignment?.Feature);
                    if (string.IsNullOrWhiteSpace(feature))
                    {
                        continue;
                    }

                    cells.Add(new CameraInspectionCell
                    {
                        CameraRole = role,
                        Feature = feature,
                        Enabled = assignment.Enabled
                    });
                }

                rules.Add(new CameraClassificationRule
                {
                    CameraRole = role,
                    AcceptedClasses = view.AcceptedClassificationClasses ?? string.Empty,
                    MinimumScore = view.MinimumClassificationScore
                });
            }

            matrix = new CameraInspectionMatrix(cells, false);
            classificationPolicy = new CameraClassificationPolicy(rules);
            return true;
        }

        public static void Apply(
            RecipeParameters.RecipeData recipe,
            IEnumerable<CameraInspectionCell> cells,
            IEnumerable<CameraClassificationRule> rules)
        {
            if (recipe == null)
            {
                return;
            }

            var cellPayload = (cells ?? Enumerable.Empty<CameraInspectionCell>())
                .Where(cell => cell != null)
                .OrderBy(cell => CameraInspectionMatrix.IsLegacyPhysicalAlias(cell.CameraRole) ? 0 : 1)
                .GroupBy(cell => CameraInspectionMatrix.NormalizeRole(cell.CameraRole))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .GroupBy(cell => CameraInspectionMatrix.NormalizeFeature(cell.Feature), StringComparer.OrdinalIgnoreCase)
                        .Select(featureGroup => featureGroup.Last())
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var rulePayload = (rules ?? Enumerable.Empty<CameraClassificationRule>())
                .Where(rule => rule != null)
                .OrderBy(rule => CameraInspectionMatrix.IsLegacyPhysicalAlias(rule.CameraRole) ? 0 : 1)
                .GroupBy(rule => CameraInspectionMatrix.NormalizeRole(rule.CameraRole))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            var roles = new HashSet<string>(cellPayload.Keys, StringComparer.OrdinalIgnoreCase);
            roles.UnionWith(rulePayload.Keys);

            var views = new List<RecipeParameters.RecipeCameraInspectionView>();
            foreach (string role in CameraInspectionMatrix.SupportedRoles.Where(roles.Contains))
            {
                cellPayload.TryGetValue(role, out List<CameraInspectionCell> roleCells);
                rulePayload.TryGetValue(role, out CameraClassificationRule roleRule);

                views.Add(new RecipeParameters.RecipeCameraInspectionView
                {
                    CameraRole = role,
                    AcceptedClassificationClasses = roleRule?.AcceptedClasses ?? string.Empty,
                    MinimumClassificationScore = roleRule?.MinimumScore ?? 0d,
                    Inspections = (roleCells ?? new List<CameraInspectionCell>())
                        .Select(cell => new RecipeParameters.RecipeInspectionFeatureAssignment
                        {
                            Feature = CameraInspectionMatrix.NormalizeFeature(cell.Feature),
                            Enabled = cell.Enabled
                        })
                        .Where(item => !string.IsNullOrWhiteSpace(item.Feature))
                        .OrderBy(item => item.Feature, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }

            recipe.inspectionViewConfiguration = new RecipeParameters.RecipeInspectionViewConfiguration
            {
                IsConfigured = true,
                Views = views
            };
        }

        public static void Reset(RecipeParameters.RecipeData recipe)
        {
            if (recipe == null)
            {
                return;
            }

            recipe.inspectionViewConfiguration = new RecipeParameters.RecipeInspectionViewConfiguration();
        }
    }
}
