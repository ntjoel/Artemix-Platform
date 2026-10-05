using System;

namespace QtisVisionPanel.Models
{
    /// <summary>
    /// Singola misura strutturata di ispezione per un prodotto.
    ///
    /// Fondazione dati AI (Fase 0): a differenza dei contatori (pass/fail aggregati),
    /// questa riga conserva il valore numerico misurato e la relativa tolleranza per
    /// ogni feature di ogni camera, abilitando analisi statistica e rilevamento deriva.
    ///
    /// Una riga per (prodotto, camera, feature). Persistita in `tbl_inspection_measurements`
    /// da <see cref="QtisVisionPanel.Database.InspectionMeasurementRepository"/>.
    /// </summary>
    public sealed class InspectionMeasurementEntry
    {
        public DateTime Timestamp { get; set; }

        /// <summary>Id prodotto (IdProduzione della produzione corrente); 0 se non disponibile.</summary>
        public long ProductId { get; set; }

        public string Recipe { get; set; }

        /// <summary>Ruolo camera normalizzato: top, top3d, side/left, front, rear, bottom.</summary>
        public string CameraRole { get; set; }

        /// <summary>Nome feature (chiave del dizionario Measurements): Logo, Height, PrintCentering, ...</summary>
        public string Feature { get; set; }

        public double MeasuredValue { get; set; }

        public double Tolerance { get; set; }

        /// <summary>true se la feature e' fuori tolleranza (difetto) per questo prodotto.</summary>
        public bool IsDefect { get; set; }
    }
}
