using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// AI advisory performance monitor.
    ///
    /// It observes already-completed inspection cycle timings and emits throttled
    /// advisory warnings when a stage becomes a likely bottleneck. It never blocks,
    /// changes machine state, changes recipe, or writes configuration.
    /// </summary>
    public class AiPerformanceMonitorService
    {
        private const int MaxWindow = 200;
        private const int MinSamples = 30;
        private const int EvaluateEverySamples = 100;
        private const double TotalP95WarnMs = 1200.0;
        private const double DisplayP95WarnMs = 450.0;
        private const double ValidationP95WarnMs = 500.0;
        private const double CountersP95WarnMs = 250.0;
        private const double AdvisoryP95WarnMs = 250.0;
        private static readonly TimeSpan AdvisoryCooldown = TimeSpan.FromMinutes(10);

        private readonly object _lock = new object();
        private readonly LinkedList<AiInspectionPerformanceSample> _samples =
            new LinkedList<AiInspectionPerformanceSample>();
        private readonly Dictionary<string, DateTime> _lastAdvisoryUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private volatile bool _enabled;
        private long _observed;
        private long _lastEvaluatedAt;

        public AiPerformanceMonitorService()
        {
            _enabled = ReadEnabledFlag();
            _logger.Info($"AI_PERFORMANCE_MONITOR_INIT|enabled={_enabled}");
        }

        public bool Enabled => _enabled;

        public void RefreshConfiguration()
        {
            _enabled = ReadEnabledFlag();
        }

        public void Observe(AiInspectionPerformanceSample sample)
        {
            if (!_enabled || sample == null)
            {
                return;
            }

            bool shouldEvaluate = false;

            lock (_lock)
            {
                _samples.AddLast(sample);
                while (_samples.Count > MaxWindow)
                {
                    _samples.RemoveFirst();
                }

                _observed++;
                if (_samples.Count >= MinSamples && _observed - _lastEvaluatedAt >= EvaluateEverySamples)
                {
                    _lastEvaluatedAt = _observed;
                    shouldEvaluate = true;
                }
            }

            if (shouldEvaluate)
            {
                Evaluate();
            }
        }

        private void Evaluate()
        {
            AiInspectionPerformanceSample[] snapshot;
            lock (_lock)
            {
                snapshot = _samples.ToArray();
            }

            if (snapshot.Length < MinSamples)
            {
                return;
            }

            var stages = new[]
            {
                BuildStage("total", "ciclo completo post-acquisizione", snapshot.Select(s => s.TotalMs), TotalP95WarnMs),
                BuildStage("display", "aggiornamento display camere", snapshot.Select(s => s.DisplayMs), DisplayP95WarnMs),
                BuildStage("validation", "validazione VisionPro/risultati", snapshot.Select(s => s.ValidationMs), ValidationP95WarnMs),
                BuildStage("counters", "contatori/allarmi/OPC UA", snapshot.Select(s => s.CountersMs), CountersP95WarnMs),
                BuildStage("advisory", "servizi AI/advisory post-esito", snapshot.Select(s => s.AdvisoryMs), AdvisoryP95WarnMs)
            };

            foreach (var stage in stages.Where(s => s.P95Ms >= s.WarnThresholdMs).OrderByDescending(s => s.P95Ms))
            {
                EmitAdvisoryIfDue(stage, snapshot.Length);
            }
        }

        private static StageSnapshot BuildStage(string key, string label, IEnumerable<double> values, double warnThresholdMs)
        {
            var data = values
                .Where(v => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0.0)
                .ToArray();

            Array.Sort(data);
            return new StageSnapshot
            {
                Key = key,
                Label = label,
                P50Ms = Percentile(data, 50),
                P95Ms = Percentile(data, 95),
                WarnThresholdMs = warnThresholdMs
            };
        }

        private void EmitAdvisoryIfDue(StageSnapshot stage, int sampleCount)
        {
            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                if (_lastAdvisoryUtc.TryGetValue(stage.Key, out var last) &&
                    now - last < AdvisoryCooldown)
                {
                    return;
                }

                _lastAdvisoryUtc[stage.Key] = now;
            }

            string message = string.Format(
                "Prestazioni ciclo: {0} p95={1:0} ms, mediana={2:0} ms su {3} campioni. Possibile collo di bottiglia da analizzare.",
                stage.Label,
                stage.P95Ms,
                stage.P50Ms,
                sampleCount);

            _logger.Warn($"AI_PERFORMANCE_ADVISORY|stage={stage.Key}|p95_ms={stage.P95Ms:0}|p50_ms={stage.P50Ms:0}|samples={sampleCount}");

            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                LogLevel.Warn,
                "AI_PERFORMANCE_ADVISORY",
                "AI Performance",
                message,
                nameof(AiPerformanceMonitorService),
                null,
                new Dictionary<string, object>
                {
                    { "stage", stage.Key },
                    { "p95_ms", stage.P95Ms.ToString("0") },
                    { "p50_ms", stage.P50Ms.ToString("0") },
                    { "samples", sampleCount }
                });
        }

        private static double Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Length == 0)
            {
                return 0.0;
            }

            if (sortedValues.Length == 1)
            {
                return sortedValues[0];
            }

            double position = (percentile / 100.0) * (sortedValues.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return sortedValues[lower];
            }

            double weight = position - lower;
            return sortedValues[lower] * (1.0 - weight) + sortedValues[upper] * weight;
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.AiPerformanceMonitorEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }

        private sealed class StageSnapshot
        {
            public string Key { get; set; }
            public string Label { get; set; }
            public double P50Ms { get; set; }
            public double P95Ms { get; set; }
            public double WarnThresholdMs { get; set; }
        }
    }

    public sealed class AiInspectionPerformanceSample
    {
        public DateTime TimestampUtc { get; set; }
        public string Recipe { get; set; }
        public long ProductId { get; set; }
        public bool IsValid { get; set; }
        public string CameraRoles { get; set; }
        public double TotalMs { get; set; }
        public double DisplayMs { get; set; }
        public double ValidationMs { get; set; }
        public double CountersMs { get; set; }
        public double SaveDecisionMs { get; set; }
        public double AdvisoryMs { get; set; }
        public double DbQueueMs { get; set; }
    }
}
