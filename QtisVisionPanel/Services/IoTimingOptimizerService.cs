using NLog;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Integrazione AI (Fase 5) — Ottimizzatore timing IO/encoder (advisory + apply con conferma).
    ///
    /// Osserva in-memory la telemetria temporale della catena multi-camera (lag di arrivo dei
    /// risultati companion rispetto al TOP, frequenza di companion mancanti) senza toccare il
    /// codice real-time: e' alimentato dal punto di finalizzazione ispezione in MainWindow, dove
    /// i <see cref="CameraResult"/> con <c>EnqueuedAtUtc</c> sono gia' disponibili. Si adatta
    /// automaticamente al numero di ruoli camera presenti nel progetto VisionPro.
    ///
    /// Produce raccomandazioni ADVISORY per ruolo (distribuzione lag, jitter, missing-rate,
    /// margine rispetto ai timeout risultato). L'APPLICAZIONE dei parametri di trigger avviene
    /// SOLO su chiamata esplicita (<see cref="ApplyTimingParameter"/>) con: whitelist dei
    /// parametri, limiti di range, backup del valore precedente e audit. Nessuna modifica
    /// automatica del loop di controllo. Attivazione via
    /// <c>MachineRuntimeBindings.IoTimingOptimizerEnabled</c> (default disabilitato sulle nuove configurazioni).
    /// </summary>
    public class IoTimingOptimizerService
    {
        private const int MaxWindow = 500;
        private const int MinLagSamples = 50;
        private const int EvaluateEveryGroups = 200;
        private const double MissingRateWarn = 0.02; // 2%

        // Timeout risultato: sono costanti di codice in MainWindow (non scrivibili da config).
        // Servono solo per valutare il MARGINE come advisory.
        private const double SingleResultTimeoutMs = 5000.0;
        private const double MultiShotResultTimeoutMs = 30000.0;
        private const double TimeoutMarginWarnRatio = 0.7;

        // Whitelist dei parametri applicabili (campi int di RuntimeBindings) con range di sicurezza.
        private static readonly Dictionary<string, (int Min, int Max)> TunableParameters =
            new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
            {
                { "TopTriggerBaseDelayMs", (0, 2000) },
                { "SideTriggerBaseDelayMs", (0, 2000) },
                { "TopTriggerPulseMs", (1, 500) },
                { "SideTriggerPulseMs", (1, 500) },
                { "PhotocellDebounceMs", (1, 1000) },
                { "MinimumRetriggerGapMs", (0, 2000) }
            };

        public sealed class ApplyResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int OldValue { get; set; }
            public int NewValue { get; set; }
            public string BackupPath { get; set; }
        }

        private sealed class RoleTimingState
        {
            public readonly LinkedList<double> Lags = new LinkedList<double>();
            public long Missing;
            public long Expected;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, RoleTimingState> _roles =
            new Dictionary<string, RoleTimingState>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IoTimingRecommendation> _recommendations = new List<IoTimingRecommendation>();

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private volatile bool _enabled;
        private long _groupsObserved;
        private long _lastEvaluatedAtGroup;

        public IoTimingOptimizerService()
        {
            _enabled = ReadEnabledFlag();
            _logger.Info($"IO_TIMING_OPTIMIZER_INIT|enabled={_enabled}|tunables={TunableParameters.Count}");
        }

        public bool Enabled => _enabled;

        public void RefreshConfiguration() => _enabled = ReadEnabledFlag();

        public IReadOnlyList<IoTimingRecommendation> GetRecommendations()
        {
            lock (_lock)
            {
                return _recommendations.ToList();
            }
        }

        /// <summary>Elenco dei parametri applicabili con i relativi range di sicurezza.</summary>
        public static IReadOnlyDictionary<string, (int Min, int Max)> ApplicableParameters => TunableParameters;

        /// <summary>
        /// Osserva il timing di un gruppo di ispezione. Chiamato in-memory da MainWindow.
        /// Sincrono, veloce, non rilegge il DB.
        /// </summary>
        // internal: CameraResult e' internal all'assembly; il chiamante (MainWindow) e' nello stesso assembly.
        internal void ObserveInspectionTiming(
            CameraResult top,
            IReadOnlyDictionary<string, CameraResult> companions,
            IEnumerable<string> missingRoles)
        {
            if (!_enabled || top == null)
                return;

            bool shouldEvaluate = false;

            lock (_lock)
            {
                DateTime topTs = top.EnqueuedAtUtc;

                if (companions != null && topTs != default(DateTime))
                {
                    foreach (var kv in companions)
                    {
                        if (kv.Value == null || string.IsNullOrWhiteSpace(kv.Key))
                            continue;

                        var state = GetOrCreate(kv.Key);
                        state.Expected++;

                        if (kv.Value.EnqueuedAtUtc != default(DateTime))
                        {
                            double lag = (kv.Value.EnqueuedAtUtc - topTs).TotalMilliseconds;
                            state.Lags.AddLast(lag);
                            while (state.Lags.Count > MaxWindow)
                                state.Lags.RemoveFirst();
                        }
                    }
                }

                if (missingRoles != null)
                {
                    foreach (var role in missingRoles)
                    {
                        if (string.IsNullOrWhiteSpace(role))
                            continue;

                        var state = GetOrCreate(role);
                        state.Expected++;
                        state.Missing++;
                    }
                }

                _groupsObserved++;
                if (_groupsObserved - _lastEvaluatedAtGroup >= EvaluateEveryGroups)
                {
                    _lastEvaluatedAtGroup = _groupsObserved;
                    shouldEvaluate = true;
                }
            }

            if (shouldEvaluate)
                Evaluate();
        }

        private RoleTimingState GetOrCreate(string role)
        {
            if (!_roles.TryGetValue(role, out var state))
            {
                state = new RoleTimingState();
                _roles[role] = state;
            }
            return state;
        }

        private void Evaluate()
        {
            List<IoTimingRecommendation> results = new List<IoTimingRecommendation>();

            lock (_lock)
            {
                foreach (var kv in _roles)
                {
                    string role = kv.Key;
                    RoleTimingState state = kv.Value;

                    double missingRate = state.Expected > 0 ? (double)state.Missing / state.Expected : 0.0;

                    if (missingRate > MissingRateWarn)
                    {
                        results.Add(new IoTimingRecommendation
                        {
                            CameraRole = role,
                            Metric = "missing-rate",
                            Severity = "warning",
                            SampleCount = (int)Math.Min(int.MaxValue, state.Expected),
                            ComputedAtUtc = DateTime.UtcNow,
                            Message = string.Format(
                                "Ruolo {0}: risultati mancanti {1:0.0}% ({2}/{3}). Verificare timeout risultato o affidabilita' trigger.",
                                role, missingRate * 100.0, state.Missing, state.Expected)
                        });
                    }

                    if (state.Lags.Count >= MinLagSamples)
                    {
                        double[] lags = state.Lags.ToArray();
                        Array.Sort(lags);
                        double median = Percentile(lags, 50);
                        double p99 = Percentile(lags, 99);
                        double jitter = StdDev(lags);

                        results.Add(new IoTimingRecommendation
                        {
                            CameraRole = role,
                            Metric = "companion-lag",
                            Severity = "info",
                            SampleCount = lags.Length,
                            ComputedAtUtc = DateTime.UtcNow,
                            Message = string.Format(
                                "Ruolo {0}: lag risultato mediano {1:0} ms, p99 {2:0} ms, jitter {3:0} ms (n={4}).",
                                role, median, p99, jitter, lags.Length)
                        });

                        // Margine rispetto al timeout risultato (costante di codice): solo advisory.
                        double timeout = string.Equals(role, "left", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(role, "side", StringComparison.OrdinalIgnoreCase)
                            ? MultiShotResultTimeoutMs
                            : SingleResultTimeoutMs;

                        if (p99 > timeout * TimeoutMarginWarnRatio)
                        {
                            results.Add(new IoTimingRecommendation
                            {
                                CameraRole = role,
                                Metric = "timeout-margin",
                                Severity = "warning",
                                SampleCount = lags.Length,
                                ComputedAtUtc = DateTime.UtcNow,
                                Message = string.Format(
                                    "Ruolo {0}: p99 lag {1:0} ms vicino al timeout risultato {2:0} ms. Margine risicato: rischio di risultati saltati sotto carico.",
                                    role, p99, timeout)
                            });
                        }
                    }
                }

                _recommendations.Clear();
                _recommendations.AddRange(results);
            }

            foreach (var rec in results)
            {
                try
                {
                    if (string.Equals(rec.Severity, "warning", StringComparison.OrdinalIgnoreCase))
                        _logger.Warn($"IO_TIMING_ADVISORY|role={rec.CameraRole}|metric={rec.Metric}|{rec.Message}");
                    else
                        _logger.Info($"IO_TIMING_ADVISORY|role={rec.CameraRole}|metric={rec.Metric}|{rec.Message}");

                    ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                        string.Equals(rec.Severity, "warning", StringComparison.OrdinalIgnoreCase) ? LogLevel.Warn : LogLevel.Info,
                        "IO_TIMING_ADVISORY",
                        "IoTimingOptimizer",
                        rec.Message,
                        nameof(IoTimingOptimizerService),
                        null,
                        new Dictionary<string, object>
                        {
                            { "role", rec.CameraRole },
                            { "metric", rec.Metric },
                            { "samples", rec.SampleCount }
                        });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "IO_TIMING_ADVISORY_LOG_FAILED");
                }
            }
        }

        /// <summary>
        /// Applica in modo guardrailato un parametro di timing (SOLO su conferma operatore).
        /// Valida whitelist e range, salva il backup del valore precedente, scrive la config e
        /// registra l'audit. Nessuna applicazione automatica: questo metodo va invocato da
        /// un'azione utente esplicita e autorizzata.
        /// </summary>
        public ApplyResult ApplyTimingParameter(string parameterName, int newValue, string actor)
        {
            var result = new ApplyResult { Success = false };

            if (string.IsNullOrWhiteSpace(parameterName) || !TunableParameters.TryGetValue(parameterName, out var range))
            {
                result.Message = $"Parametro non applicabile: {parameterName}.";
                return result;
            }

            if (newValue < range.Min || newValue > range.Max)
            {
                result.Message = $"Valore {newValue} fuori dal range di sicurezza [{range.Min},{range.Max}] per {parameterName}.";
                return result;
            }

            try
            {
                var service = new MachineConfigurationService();
                var config = service.Load();
                if (config?.RuntimeBindings == null)
                {
                    result.Message = "Configurazione runtime non disponibile.";
                    return result;
                }

                var bindings = config.RuntimeBindings;
                int oldValue;

                switch (parameterName)
                {
                    case "TopTriggerBaseDelayMs": oldValue = bindings.TopTriggerBaseDelayMs; bindings.TopTriggerBaseDelayMs = newValue; break;
                    case "SideTriggerBaseDelayMs": oldValue = bindings.SideTriggerBaseDelayMs; bindings.SideTriggerBaseDelayMs = newValue; break;
                    case "TopTriggerPulseMs": oldValue = bindings.TopTriggerPulseMs; bindings.TopTriggerPulseMs = newValue; break;
                    case "SideTriggerPulseMs": oldValue = bindings.SideTriggerPulseMs; bindings.SideTriggerPulseMs = newValue; break;
                    case "PhotocellDebounceMs": oldValue = bindings.PhotocellDebounceMs; bindings.PhotocellDebounceMs = newValue; break;
                    case "MinimumRetriggerGapMs": oldValue = bindings.MinimumRetriggerGapMs; bindings.MinimumRetriggerGapMs = newValue; break;
                    default:
                        result.Message = $"Parametro non gestito: {parameterName}.";
                        return result;
                }

                string backupPath = service.CreateBackupFromActiveConfiguration("before-ai-timing-apply");
                service.Save(config);

                result.Success = true;
                result.OldValue = oldValue;
                result.NewValue = newValue;
                result.BackupPath = backupPath;
                result.Message = $"{parameterName}: {oldValue} -> {newValue}. Backup file: {backupPath ?? "non disponibile"}. " +
                                 "Il valore e' salvato nel file macchina; diventa effettivo dopo il refresh/reload runtime previsto dalla pagina configurazione.";

                _logger.Info($"IO_TIMING_APPLY|param={parameterName}|old={oldValue}|new={newValue}|actor={actor}");

                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    LogLevel.Warn,
                    "IO_TIMING_APPLY",
                    "IoTimingOptimizer",
                    result.Message,
                    nameof(IoTimingOptimizerService),
                    $"actor={actor}",
                    new Dictionary<string, object>
                    {
                        { "parameter", parameterName },
                        { "old_value", oldValue },
                        { "new_value", newValue },
                        { "backup_path", backupPath ?? string.Empty },
                        { "actor", actor ?? "unknown" }
                    });

                ServiceLocator.AuditLogService?.LogAsync(
                    "IO_TIMING_APPLY",
                    actor ?? "unknown",
                    $"parameter={parameterName}; backup={backupPath ?? string.Empty}",
                    Convert.ToString(oldValue, CultureInfo.InvariantCulture),
                    Convert.ToString(newValue, CultureInfo.InvariantCulture)).SafeFireAndForget(_logger, "AUDIT_IO_TIMING_APPLY_FAILED");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "IO_TIMING_APPLY_FAILED");
                result.Success = false;
                result.Message = $"Errore applicazione: {ex.Message}";
            }

            return result;
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 0)
                return 0.0;
            if (sorted.Length == 1)
                return sorted[0];

            double rank = (percentile / 100.0) * (sorted.Length - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            double weight = rank - lower;
            return sorted[lower] * (1.0 - weight) + sorted[upper] * weight;
        }

        private static double StdDev(double[] values)
        {
            if (values.Length < 2)
                return 0.0;
            double mean = values.Average();
            double sumSq = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSq / (values.Length - 1));
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.IoTimingOptimizerEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
