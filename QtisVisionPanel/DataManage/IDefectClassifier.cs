namespace QtisVisionPanel.DataManage
{
    /// <summary>
    /// Esito della classificazione ML di un'immagine di ispezione (Fase 3).
    /// </summary>
    public sealed class DefectClassificationResult
    {
        /// <summary>true se il modello era caricato e l'inferenza e' andata a buon fine.</summary>
        public bool IsAvailable { get; set; }

        /// <summary>Indice della classe con probabilita' massima.</summary>
        public int PredictedClass { get; set; }

        /// <summary>Confidenza (softmax) della classe predetta, 0..1.</summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Interpretazione binaria: true se il modello ritiene il pezzo difettoso.
        /// Convenzione dello scaffold: la classe 0 rappresenta il pezzo conforme (OK),
        /// qualsiasi altra classe indica un difetto. Da riadattare al modello reale.
        /// Se <see cref="IsUncertain"/> e' true, un NOK grezzo e' stato declassato a non-difetto
        /// dalla soglia di confidenza: <c>PredictedDefective</c> risulta quindi false.
        /// </summary>
        public bool PredictedDefective { get; set; }

        /// <summary>
        /// true se la classe predetta era "difetto" ma con confidenza sotto la soglia minima
        /// configurata: la predizione e' stata declassata a non-difetto (NOK "incerto").
        /// </summary>
        public bool IsUncertain { get; set; }
    }

    /// <summary>
    /// Classificatore difetti basato su immagine (Fase 3). Implementazione ONNX in
    /// <see cref="QtisVisionPanel.Services.OnnxDefectClassifier"/>. In questa fase l'esito
    /// e' usato solo in SHADOW-MODE (advisory): confrontato e loggato accanto al risultato
    /// a regole, senza incidere su ispezione o scarto.
    /// </summary>
    public interface IDefectClassifier
    {
        /// <summary>true se un modello e' caricato e pronto all'inferenza.</summary>
        bool IsReady { get; }

        /// <summary>Classifica l'immagine al percorso indicato.</summary>
        DefectClassificationResult Classify(string imagePath);
    }
}
