using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Models
{
    /// <summary>
    /// Campione etichettato per il futuro training del classificatore difetti (Fase 3).
    ///
    /// Collega le immagini gia' salvate di un pezzo (cartella <see cref="PieceFolder"/>)
    /// alla sua etichetta di esito e ai difetti rilevati. Persistito come indice in
    /// <c>tbl_training_samples</c> e come sidecar <c>label.json</c> dentro la cartella del pezzo
    /// (dataset auto-descrittivo e portabile per l'addestramento offline).
    /// </summary>
    public sealed class TrainingSampleEntry
    {
        public DateTime Timestamp { get; set; }

        public long ProductId { get; set; }

        public string Recipe { get; set; }

        /// <summary>"OK" (conforme) oppure "NOK" (non conforme).</summary>
        public string Label { get; set; }

        /// <summary>Cartella su disco che contiene le immagini del pezzo (Piece_XXXXXXXX).</summary>
        public string PieceFolder { get; set; }

        /// <summary>Elenco dei difetti rilevati (chiavi normalizzate).</summary>
        public List<string> Defects { get; set; } = new List<string>();

        /// <summary>
        /// Etichette PER-CAMERA ("top","side",...) -> "OK"/"NOK", derivate dal risultato-regole della
        /// singola camera. Permettono di addestrare un classificatore per camera sull'etichetta giusta
        /// (l'etichetta <see cref="Label"/> globale sarebbe rumorosa per una camera specifica).
        /// Solo nel sidecar label.json; non persistito su DB.
        /// </summary>
        public Dictionary<string, string> CameraLabels { get; set; } = new Dictionary<string, string>();
    }
}
