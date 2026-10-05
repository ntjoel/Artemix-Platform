using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Integrazione AI (Fase 1) — Controllo Statistico di Processo (SPC) e rilevamento deriva.
    ///
    /// Riceve in-memory le misure catturate ad ogni ispezione (NON rilegge il DB per prodotto),
    /// mantiene una finestra mobile per chiave (ricetta|camera|feature) e applica statistica
    /// trasparente — nessuna scatola nera:
    /// - limiti di controllo I-MR: media +/- 3*sigma, con sigma stimato dal moving range (avgMR/d2);
    /// - sottoinsieme delle regole di Nelson (1 punto oltre 3 sigma, 2 di 3 oltre 2 sigma,
    ///   8 punti dallo stesso lato, 6 punti monotoni);
    /// - carta EWMA per piccole derive persistenti;
    /// - stima lineare dei "pezzi al limite" dalla pendenza recente.
    ///
    /// Le derive sono ADVISORY: vengono loggate nell'Events Monitor a livello Warning ed esposte
    /// tramite l'evento <see cref="DriftDetected"/> e <see cref="GetActiveDrifts"/>. Non incidono
    /// in alcun modo su ispezione, scarto o contatori. Attivazione via
    /// <c>MachineRuntimeBindings.ProcessControlEnabled</c> (default disabilitato sulle nuove configurazioni).
    /// </summary>
    public class ProcessControlService
    {
        private const int MaxWindow = 60;      // ampiezza finestra mobile per chiave
        private const int MinSamples = 15;     // campioni minimi prima di valutare i limiti
        private const double EwmaLambda = 0.2; // fattore di smoothing EWMA
        private const double D2 = 1.128;       // costante I-MR per moving range n=2
        private const int RecoveryPoints = 8;  // punti consecutivi in-control per chiudere una deriva

        public sealed class DriftInfo
        {
            public string Recipe { get; set; }
            public string CameraRole { get; set; }
            public string Feature { get; set; }
            public string Rule { get; set; }
            public string Direction { get; set; } // "up" | "down"
            public double CurrentValue { get; set; }
            public double Mean { get; set; }
            public double Sigma { get; set; }
            public double SlopePerProduct { get; set; }
            public int EstimatedProductsToLimit { get; set; } // -1 se non stimabile
            public DateTime DetectedAtUtc { get; set; }

            public string Key => string.Concat(Recipe, "|", CameraRole, "|", Feature);
        }

        private sealed class FeatureState
        {
            public readonly LinkedList<double> Window = new LinkedList<double>();
            public double? Ewma;
            public bool DriftActive;
            public string ActiveRule;
            public int InControlStreak;
        }

        public event Action<DriftInfo> DriftDetected;

        private readonly object _lock = new object();
        private readonly Dictionary<string, FeatureState> _states =
            new Dictionary<string, FeatureState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DriftInfo> _activeDrifts =
            new Dictionary<string, DriftInfo>(StringComparer.OrdinalIgnoreCase);

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private volatile bool _enabled;

        public ProcessControlService()
        {
            _enabled = ReadEnabledFlag();
            _logger.Info($"PROCESS_CONTROL_INIT|enabled={_enabled}");
        }

        public bool Enabled => _enabled;

        public void RefreshConfiguration() => _enabled = ReadEnabledFlag();

        /// <summary>Snapshot delle derive attualmente attive (per una futura UI di trend).</summary>
        public IReadOnlyList<DriftInfo> GetActiveDrifts()
        {
            lock (_lock)
            {
                return _activeDrifts.Values.ToList();
            }
        }

        /// <summary>
        /// Osserva le misure di un'ispezione. Chiamato in-memory da MeasurementCaptureService.
        /// Sincrono e veloce; l'eventuale logging/evento avviene fuori dal lock.
        /// </summary>
        public void ObserveBatch(IReadOnlyList<InspectionMeasurementEntry> entries)
        {
            if (!_enabled || entries == null || entries.Count == 0)
                return;

            List<DriftInfo> newDrifts = null;

            lock (_lock)
            {
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Feature))
                        continue;

                    var drift = ObserveOne(entry);
                    if (drift != null)
                        (newDrifts ?? (newDrifts = new List<DriftInfo>())).Add(drift);
                }
            }

            if (newDrifts == null)
                return;

            foreach (var drift in newDrifts)
                RaiseAndLog(drift);
        }

        // ---- Nucleo statistico (eseguito sotto lock) ----

        private DriftInfo ObserveOne(InspectionMeasurementEntry entry)
        {
            string key = string.Concat(entry.Recipe, "|", entry.CameraRole, "|", entry.Feature);
            if (!_states.TryGetValue(key, out var state))
            {
                state = new FeatureState();
                _states[key] = state;
            }

            double x = entry.MeasuredValue;
            double[] baseline = state.Window.ToArray();

            state.Ewma = state.Ewma.HasValue
                ? EwmaLambda * x + (1.0 - EwmaLambda) * state.Ewma.Value
                : x;

            state.Window.AddLast(x);
            while (state.Window.Count > MaxWindow)
                state.Window.RemoveFirst();

            if (baseline.Length < MinSamples)
                return null;

            double[] data = state.Window.ToArray();
            double mean = baseline.Average();
            double sigma = EstimateSigma(baseline);

            if (sigma <= 0.0 || double.IsNaN(sigma) || double.IsInfinity(sigma))
            {
                // Processo piatto (nessuna variabilita'): nessuna deriva statistica.
                MarkInControl(state, key);
                return null;
            }

            string rule = EvaluateRules(data, mean, sigma, state.Ewma.Value, out string direction);

            if (rule == null)
            {
                MarkInControl(state, key);
                return null;
            }

            state.InControlStreak = 0;

            // Debounce: se la stessa regola e' gia' attiva per questa chiave, non ri-segnalare.
            if (state.DriftActive && string.Equals(state.ActiveRule, rule, StringComparison.OrdinalIgnoreCase))
                return null;

            state.DriftActive = true;
            state.ActiveRule = rule;

            double slope = LinearSlope(data);
            int eta = EstimateProductsToLimit(x, slope, mean, sigma, direction);

            var info = new DriftInfo
            {
                Recipe = entry.Recipe,
                CameraRole = entry.CameraRole,
                Feature = entry.Feature,
                Rule = rule,
                Direction = direction,
                CurrentValue = x,
                Mean = mean,
                Sigma = sigma,
                SlopePerProduct = slope,
                EstimatedProductsToLimit = eta,
                DetectedAtUtc = DateTime.UtcNow
            };

            _activeDrifts[key] = info;
            return info;
        }

        private void MarkInControl(FeatureState state, string key)
        {
            if (!state.DriftActive)
                return;

            state.InControlStreak++;
            if (state.InControlStreak >= RecoveryPoints)
            {
                state.DriftActive = false;
                state.ActiveRule = null;
                state.InControlStreak = 0;
                _activeDrifts.Remove(key);
            }
        }

        private static double EstimateSigma(double[] data)
        {
            if (data.Length < 2)
                return 0.0;

            double sumMovingRange = 0.0;
            for (int i = 1; i < data.Length; i++)
                sumMovingRange += Math.Abs(data[i] - data[i - 1]);

            double avgMovingRange = sumMovingRange / (data.Length - 1);
            return avgMovingRange / D2;
        }

        // Restituisce il codice della prima regola violata (priorita' decrescente), altrimenti null.
        private static string EvaluateRules(double[] data, double mean, double sigma, double ewma, out string direction)
        {
            direction = null;
            int n = data.Length;
            double last = data[n - 1];

            double ucl3 = mean + 3.0 * sigma;
            double lcl3 = mean - 3.0 * sigma;
            double ucl2 = mean + 2.0 * sigma;
            double lcl2 = mean - 2.0 * sigma;

            // Regola 1: un punto oltre 3 sigma.
            if (last > ucl3) { direction = "up"; return "1:oltre-3sigma"; }
            if (last < lcl3) { direction = "down"; return "1:oltre-3sigma"; }

            // Carta EWMA: piccola deriva persistente.
            double ewmaHalfWidth = 3.0 * sigma * Math.Sqrt(EwmaLambda / (2.0 - EwmaLambda));
            if (ewma > mean + ewmaHalfWidth) { direction = "up"; return "EWMA:deriva-media"; }
            if (ewma < mean - ewmaHalfWidth) { direction = "down"; return "EWMA:deriva-media"; }

            // Regola 5: 2 di 3 punti consecutivi oltre 2 sigma dallo stesso lato.
            if (n >= 3)
            {
                int upCount = 0, downCount = 0;
                for (int i = n - 3; i < n; i++)
                {
                    if (data[i] > ucl2) upCount++;
                    else if (data[i] < lcl2) downCount++;
                }
                if (upCount >= 2) { direction = "up"; return "5:2di3-oltre-2sigma"; }
                if (downCount >= 2) { direction = "down"; return "5:2di3-oltre-2sigma"; }
            }

            // Regola 2: 8 punti consecutivi dallo stesso lato della media.
            if (n >= 8)
            {
                bool allAbove = true, allBelow = true;
                for (int i = n - 8; i < n; i++)
                {
                    if (data[i] <= mean) allAbove = false;
                    if (data[i] >= mean) allBelow = false;
                }
                if (allAbove) { direction = "up"; return "2:8punti-stesso-lato"; }
                if (allBelow) { direction = "down"; return "2:8punti-stesso-lato"; }
            }

            // Regola 3: 6 punti consecutivi in aumento o in diminuzione.
            if (n >= 6)
            {
                bool increasing = true, decreasing = true;
                for (int i = n - 5; i < n; i++)
                {
                    if (data[i] <= data[i - 1]) increasing = false;
                    if (data[i] >= data[i - 1]) decreasing = false;
                }
                if (increasing) { direction = "up"; return "3:6punti-crescenti"; }
                if (decreasing) { direction = "down"; return "3:6punti-decrescenti"; }
            }

            return null;
        }

        private static double LinearSlope(double[] data)
        {
            int n = data.Length;
            if (n < 2)
                return 0.0;

            double meanX = (n - 1) / 2.0;
            double meanY = data.Average();
            double num = 0.0, den = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = i - meanX;
                num += dx * (data[i] - meanY);
                den += dx * dx;
            }

            return den == 0.0 ? 0.0 : num / den;
        }

        // Stima quanti pezzi mancano prima che il valore raggiunga il limite di controllo (3 sigma)
        // nella direzione della deriva. -1 se non stimabile (pendenza nulla o in direzione opposta).
        private static int EstimateProductsToLimit(double current, double slope, double mean, double sigma, string direction)
        {
            double limit = direction == "up" ? mean + 3.0 * sigma : mean - 3.0 * sigma;
            double distance = limit - current;

            // Deve muoversi verso il limite (stesso segno di distance e slope).
            if (Math.Abs(slope) < 1e-9)
                return -1;
            if (Math.Sign(distance) != Math.Sign(slope))
                return -1;

            double products = distance / slope;
            if (double.IsNaN(products) || double.IsInfinity(products) || products < 0.0)
                return -1;

            return (int)Math.Ceiling(products);
        }

        private void RaiseAndLog(DriftInfo drift)
        {
            try
            {
                string etaText = drift.EstimatedProductsToLimit >= 0
                    ? $"{drift.EstimatedProductsToLimit} pezzi al limite"
                    : "trend non proiettabile";

                string message = string.Format(
                    "Deriva processo: {0} ({1}) - regola {2}, direzione {3}, valore {4:0.###}, media {5:0.###}, {6}",
                    drift.Feature, drift.CameraRole, drift.Rule, drift.Direction,
                    drift.CurrentValue, drift.Mean, etaText);

                _logger.Warn($"PROCESS_DRIFT|recipe={drift.Recipe}|role={drift.CameraRole}|feature={drift.Feature}" +
                             $"|rule={drift.Rule}|dir={drift.Direction}|value={drift.CurrentValue:0.###}" +
                             $"|mean={drift.Mean:0.###}|sigma={drift.Sigma:0.###}|eta={drift.EstimatedProductsToLimit}");

                var metadata = new Dictionary<string, object>
                {
                    { "recipe", drift.Recipe },
                    { "camera_role", drift.CameraRole },
                    { "feature", drift.Feature },
                    { "rule", drift.Rule },
                    { "direction", drift.Direction },
                    { "value", drift.CurrentValue.ToString("0.###") },
                    { "mean", drift.Mean.ToString("0.###") },
                    { "sigma", drift.Sigma.ToString("0.###") },
                    { "eta_products", drift.EstimatedProductsToLimit }
                };

                // Advisory: livello Warning -> compare nell'Events Monitor senza attivare il badge errori.
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    LogLevel.Warn,
                    "PROCESS_DRIFT",
                    "ProcessControl",
                    message,
                    nameof(ProcessControlService),
                    null,
                    metadata);

                DriftDetected?.Invoke(drift);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "PROCESS_DRIFT_NOTIFY_FAILED");
            }
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.ProcessControlEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
