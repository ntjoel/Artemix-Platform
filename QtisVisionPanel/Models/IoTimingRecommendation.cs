using System;

namespace QtisVisionPanel.Models
{
    /// <summary>
    /// Raccomandazione/diagnostica di timing IO/encoder prodotta dall'ottimizzatore (Fase 5).
    ///
    /// E' ADVISORY: descrive il comportamento temporale osservato per un ruolo camera e, dove
    /// difendibile dai dati, suggerisce un parametro/valore. L'applicazione avviene solo su
    /// conferma esplicita dell'operatore tramite il percorso guardrailato del servizio.
    /// </summary>
    public sealed class IoTimingRecommendation
    {
        public string CameraRole { get; set; }

        /// <summary>Metrica di riferimento: "companion-lag", "missing-rate", "timeout-margin".</summary>
        public string Metric { get; set; }

        public string Message { get; set; }

        /// <summary>"info" oppure "warning".</summary>
        public string Severity { get; set; }

        /// <summary>Parametro di config suggerito (se applicabile), es. "SideTriggerBaseDelayMs".</summary>
        public string SuggestedParameter { get; set; }

        /// <summary>Valore suggerito (se difendibile dai dati); null se puramente diagnostico.</summary>
        public double? SuggestedValue { get; set; }

        public int SampleCount { get; set; }

        public DateTime ComputedAtUtc { get; set; }
    }
}
