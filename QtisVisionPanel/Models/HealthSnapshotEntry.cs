using System;

namespace QtisVisionPanel.Models
{
    /// <summary>
    /// Snapshot periodico dello stato di salute del PC di controllo.
    ///
    /// Fondazione dati AI (Fase 0): serie storica per manutenzione predittiva
    /// (deriva CPU/RAM/disco nel tempo). Persistita in `tbl_health_snapshots`
    /// da <see cref="QtisVisionPanel.Database.HealthSnapshotRepository"/>.
    /// </summary>
    public sealed class HealthSnapshotEntry
    {
        public DateTime Timestamp { get; set; }

        public double CpuPercent { get; set; }

        public double MemoryPercent { get; set; }

        public double MemoryUsedGb { get; set; }

        public double MemoryTotalGb { get; set; }

        /// <summary>Percentuale di utilizzo del disco piu pieno tra quelli monitorati.</summary>
        public double DiskMaxUsedPercent { get; set; }
    }
}
