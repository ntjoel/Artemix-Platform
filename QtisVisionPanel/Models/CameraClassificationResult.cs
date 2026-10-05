namespace QtisVisionPanel.Models
{
    public enum CameraClassificationState
    {
        Informational,
        Passed,
        Failed,
        Unclassified
    }

    /// <summary>
    /// Classification captured from one VisionPro camera result. Recipe settings
    /// decide whether it is advisory or participates in validation and reject.
    /// </summary>
    public sealed class CameraClassificationResult
    {
        /// <summary>
        /// Classe scritta al posto di quella riconosciuta quando il punteggio e' sotto la
        /// soglia della vista.
        ///
        /// Serve perche' un modello Edge Learning puo' essere addestrato sulla sola classe
        /// OK: in quel caso non esistono altre classi in uscita e un punteggio basso non
        /// significa "ha vinto un'altra classe" ma "il modello non riconosce questo pezzo".
        /// Registrare a database OK con confidenza 0,064 sarebbe fuorviante in analisi: il
        /// punteggio resta quello rilevato, la classe diventa esplicitamente non classificata.
        /// </summary>
        public const string UnclassifiedClassName = "Unclassify";

        public string ClassName { get; set; }
        public double? Score { get; set; }
        public string ClassOutputName { get; set; }
        public string ScoreOutputName { get; set; }
        public CameraClassificationState State { get; set; } = CameraClassificationState.Informational;
    }
}
