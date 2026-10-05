using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// In-memory cache for analytics dashboard snapshots.
    ///
    /// The dashboard is read-only from the machine-process point of view, so we
    /// keep a lightweight memory cache to avoid blocking the WPF thread every
    /// time the operator opens the page. The snapshot is refreshed on demand and
    /// by the page timer, but opening the page can immediately reuse the last
    /// available state.
    /// </summary>
    public class DataAnalysisSnapshotCacheService
    {
        private readonly ConcurrentDictionary<string, AnalyticsDashboardSnapshot> _snapshots =
            new ConcurrentDictionary<string, AnalyticsDashboardSnapshot>(StringComparer.OrdinalIgnoreCase);

        private AnalyticsDashboardSnapshot _latestSnapshot;

        public AnalyticsDashboardSnapshot TryGetSnapshot(DateTime start, DateTime end, string recipeName)
        {
            _snapshots.TryGetValue(CreateKey(start, end, recipeName), out var snapshot);
            return snapshot;
        }

        public AnalyticsDashboardSnapshot TryGetLatestSnapshot()
        {
            return _latestSnapshot;
        }

        public void StoreSnapshot(AnalyticsDashboardSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _snapshots[CreateKey(snapshot.Start, snapshot.End, snapshot.RecipeName)] = snapshot;
            _latestSnapshot = snapshot;
        }

        private static string CreateKey(DateTime start, DateTime end, string recipeName)
        {
            string normalizedRecipe = string.IsNullOrWhiteSpace(recipeName)
                ? "*"
                : recipeName.Trim().ToLowerInvariant();

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:O}|{1:O}|{2}",
                start,
                end,
                normalizedRecipe);
        }
    }

    public class AnalyticsDashboardSnapshot
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string RecipeName { get; set; }
        public string ResolvedRecipePath { get; set; }
        public DateTime CreatedAt { get; set; }
        public ProductionOverviewSnapshot ProductionOverview { get; set; } = new ProductionOverviewSnapshot();
        public Dictionary<string, int> Defects { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public RecipeParameters.InspectionStatus InspectionStatus { get; set; }
        public Dictionary<DataAnalysisCardType, AnalyticsTrendSnapshot> Trends { get; set; } =
            new Dictionary<DataAnalysisCardType, AnalyticsTrendSnapshot>();
    }

    public class AnalyticsTrendSnapshot
    {
        public bool IsSupported { get; set; }
        public string Subtitle { get; set; }
        public string ResolvedColumn { get; set; }
        public string SummaryText { get; set; }
        public List<DataAnalysisTrendBucket> Buckets { get; set; } = new List<DataAnalysisTrendBucket>();
        public double? NominalValue { get; set; }
        public double? LowerToleranceValue { get; set; }
        public double? UpperToleranceValue { get; set; }
    }
}
