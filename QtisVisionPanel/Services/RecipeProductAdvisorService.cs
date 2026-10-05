using Cognex.VisionPro;
using NLog;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.RecipeSwitch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Advisory service that detects likely recipe/product mismatch patterns.
    ///
    /// It observes completed inspection results only. It does not decide OK/NOK,
    /// does not reject products, and does not change the active recipe. When the
    /// pattern looks suspicious, it logs a controlled advisory and can search a
    /// candidate recipe in background by reusing the existing auto-switch matcher.
    /// </summary>
    public class RecipeProductAdvisorService
    {
        private const int MaxWindow = 20;
        private const int MinWindow = 5;
        private const int ConsecutiveFailureThreshold = 4;
        private const double FailureRateWarnRatio = 0.65;
        private const double DominantFeatureWarnRatio = 0.55;

        private static readonly TimeSpan AdvisoryCooldown = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CandidateSearchCooldown = TimeSpan.FromMinutes(10);

        private readonly object _lock = new object();
        private readonly LinkedList<ProductObservation> _window = new LinkedList<ProductObservation>();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private volatile bool _enabled;
        private int _consecutiveFailures;
        private DateTime _lastAdvisoryUtc = DateTime.MinValue;
        private DateTime _lastCandidateSearchUtc = DateTime.MinValue;
        private bool _candidateSearchRunning;
        private string _lastRecipe;

        public RecipeProductAdvisorService()
        {
            _enabled = ReadEnabledFlag();
            _logger.Info($"AI_RECIPE_ADVISOR_INIT|enabled={_enabled}");
        }

        public bool Enabled => _enabled;

        public void RefreshConfiguration()
        {
            _enabled = ReadEnabledFlag();
        }

        public void ObserveInspection(InspectionResult result, string recipe, long productId, ICogImage topImage)
        {
            if (!_enabled || result == null)
            {
                return;
            }

            recipe = NormalizeRecipe(recipe);
            var observation = BuildObservation(result, recipe, productId);
            RecipeMismatchAdvisory advisory = null;

            lock (_lock)
            {
                if (!string.Equals(_lastRecipe, recipe, StringComparison.OrdinalIgnoreCase))
                {
                    _window.Clear();
                    _consecutiveFailures = 0;
                    _lastRecipe = recipe;
                }

                _window.AddLast(observation);
                while (_window.Count > MaxWindow)
                {
                    _window.RemoveFirst();
                }

                _consecutiveFailures = observation.IsFail ? _consecutiveFailures + 1 : 0;
                advisory = TryBuildAdvisoryLocked(recipe);
            }

            if (advisory == null)
            {
                return;
            }

            EmitAdvisory(advisory);
            TryStartCandidateSearch(topImage, recipe, productId, advisory);
        }

        private RecipeMismatchAdvisory TryBuildAdvisoryLocked(string recipe)
        {
            if (_window.Count < MinWindow)
            {
                return null;
            }

            DateTime now = DateTime.UtcNow;
            if (now - _lastAdvisoryUtc < AdvisoryCooldown)
            {
                return null;
            }

            var observations = _window.ToList();
            int failCount = observations.Count(o => o.IsFail);
            double failRate = observations.Count == 0 ? 0.0 : (double)failCount / observations.Count;

            var featureGroups = observations
                .Where(o => o.IsFail)
                .SelectMany(o => o.Features)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Feature = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            string dominantFeature = featureGroups.FirstOrDefault()?.Feature ?? "GenericNok";
            double dominantRatio = failCount == 0 ? 0.0 : (double)(featureGroups.FirstOrDefault()?.Count ?? 0) / failCount;

            bool sustainedFailures = _consecutiveFailures >= ConsecutiveFailureThreshold;
            bool highFailRate = failRate >= FailureRateWarnRatio && dominantRatio >= DominantFeatureWarnRatio;

            if (!sustainedFailures && !highFailRate)
            {
                return null;
            }

            _lastAdvisoryUtc = now;
            return new RecipeMismatchAdvisory
            {
                Recipe = recipe,
                WindowSize = observations.Count,
                FailCount = failCount,
                FailRate = failRate,
                ConsecutiveFailures = _consecutiveFailures,
                DominantFeature = dominantFeature,
                DominantFeatureRatio = dominantRatio,
                Features = featureGroups.Take(5).Select(g => $"{g.Feature}:{g.Count}").ToList()
            };
        }

        private void EmitAdvisory(RecipeMismatchAdvisory advisory)
        {
            string message = string.Format(
                "Possibile incoerenza prodotto/ricetta: ricetta={0}, scarti={1}/{2} ({3:0}%), consecutivi={4}, difetto dominante={5} ({6:0}%). Verificare formato o ricetta attiva.",
                advisory.Recipe,
                advisory.FailCount,
                advisory.WindowSize,
                advisory.FailRate * 100.0,
                advisory.ConsecutiveFailures,
                advisory.DominantFeature,
                advisory.DominantFeatureRatio * 100.0);

            _logger.Warn($"AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED|recipe={advisory.Recipe}|fail_rate={advisory.FailRate:0.00}|consecutive={advisory.ConsecutiveFailures}|dominant={advisory.DominantFeature}");

            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                LogLevel.Warn,
                "AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED",
                "AI Recipe Advisor",
                message,
                nameof(RecipeProductAdvisorService),
                null,
                new Dictionary<string, object>
                {
                    { "recipe", advisory.Recipe },
                    { "fail_count", advisory.FailCount },
                    { "window_size", advisory.WindowSize },
                    { "fail_rate", advisory.FailRate.ToString("0.00") },
                    { "consecutive_failures", advisory.ConsecutiveFailures },
                    { "dominant_feature", advisory.DominantFeature },
                    { "dominant_feature_ratio", advisory.DominantFeatureRatio.ToString("0.00") },
                    { "features", string.Join(",", advisory.Features ?? new List<string>()) }
                });
        }

        private void TryStartCandidateSearch(ICogImage topImage, string currentRecipe, long productId, RecipeMismatchAdvisory advisory)
        {
            if (topImage == null)
            {
                return;
            }

            string recipeFolder = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder;
            if (string.IsNullOrWhiteSpace(recipeFolder) || !Directory.Exists(recipeFolder))
            {
                return;
            }

            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                if (_candidateSearchRunning || now - _lastCandidateSearchUtc < CandidateSearchCooldown)
                {
                    return;
                }

                _candidateSearchRunning = true;
                _lastCandidateSearchUtc = now;
            }

            Task.Run(() =>
            {
                try
                {
                    var switcher = new AdvancedRecipeAutoSwitcher();
                    string foundRecipe = switcher.FindMatchingRecipeWithFallback(topImage, recipeFolder);
                    foundRecipe = NormalizeRecipe(foundRecipe);

                    if (string.IsNullOrWhiteSpace(foundRecipe) ||
                        string.Equals(foundRecipe, currentRecipe, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Info($"AI_RECIPE_CANDIDATE_NOT_FOUND|current={currentRecipe}|product={productId}");
                        return;
                    }

                    string message = string.Format(
                        "Advisor ricetta: il prodotto corrente sembra compatibile con '{0}' mentre la ricetta attiva e' '{1}'. Nessun cambio automatico eseguito.",
                        foundRecipe,
                        currentRecipe);

                    _logger.Warn($"AI_RECIPE_CANDIDATE_FOUND|current={currentRecipe}|candidate={foundRecipe}|product={productId}");
                    ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                        LogLevel.Warn,
                        "AI_RECIPE_CANDIDATE_FOUND",
                        "AI Recipe Advisor",
                        message,
                        nameof(RecipeProductAdvisorService),
                        null,
                        new Dictionary<string, object>
                        {
                            { "current_recipe", currentRecipe },
                            { "candidate_recipe", foundRecipe },
                            { "product_id", productId },
                            { "dominant_feature", advisory?.DominantFeature ?? string.Empty }
                        });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "AI_RECIPE_CANDIDATE_SEARCH_FAILED");
                }
                finally
                {
                    lock (_lock)
                    {
                        _candidateSearchRunning = false;
                    }
                }
            }).SafeFireAndForget(_logger, "AI_RECIPE_CANDIDATE_SEARCH_TASK_FAILED");
        }

        private static ProductObservation BuildObservation(InspectionResult result, string recipe, long productId)
        {
            bool fail = result.HasDefects || !result.IsValid;
            var features = ExtractFailFeatures(result);
            if (fail && features.Count == 0)
            {
                features.Add("GenericNok");
            }

            return new ProductObservation
            {
                TimestampUtc = DateTime.UtcNow,
                Recipe = recipe,
                ProductId = productId,
                IsFail = fail,
                Features = features
            };
        }

        private static List<string> ExtractFailFeatures(InspectionResult result)
        {
            var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (result.DetectedDefects != null)
            {
                foreach (var kv in result.DetectedDefects)
                {
                    if (kv.Value && !string.IsNullOrWhiteSpace(kv.Key))
                    {
                        features.Add(kv.Key);
                    }
                }
            }

            AddFailedMeasurements(features, "top", result.TopResult);
            AddFailedMeasurements(features, "side", result.SideResult);
            AddFailedMeasurements(features, "front", result.FrontResult);
            AddFailedMeasurements(features, "rear", result.RearResult);
            AddFailedMeasurements(features, "right", result.RightResult);
            AddFailedMeasurements(features, "bottom", result.BottomResult);

            if (result.MissingCameraResults != null)
            {
                foreach (var missing in result.MissingCameraResults)
                {
                    AddFailedMeasurements(features, "missing-camera", missing);
                }
            }

            return features.ToList();
        }

        private static void AddFailedMeasurements(HashSet<string> features, string role, ValidationResult validation)
        {
            if (validation?.Measurements == null)
            {
                return;
            }

            foreach (var kv in validation.Measurements)
            {
                if (!kv.Value.Passed)
                {
                    features.Add(string.Concat(role, ".", kv.Key));
                }
            }
        }

        private static string NormalizeRecipe(string recipe)
        {
            if (string.IsNullOrWhiteSpace(recipe))
            {
                return string.Empty;
            }

            return Path.GetFileNameWithoutExtension(recipe.Trim());
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.RecipeProductAdvisorEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ProductObservation
        {
            public DateTime TimestampUtc { get; set; }
            public string Recipe { get; set; }
            public long ProductId { get; set; }
            public bool IsFail { get; set; }
            public List<string> Features { get; set; }
        }

        private sealed class RecipeMismatchAdvisory
        {
            public string Recipe { get; set; }
            public int WindowSize { get; set; }
            public int FailCount { get; set; }
            public double FailRate { get; set; }
            public int ConsecutiveFailures { get; set; }
            public string DominantFeature { get; set; }
            public double DominantFeatureRatio { get; set; }
            public List<string> Features { get; set; }
        }
    }
}
