using NLog;
using Newtonsoft.Json;
using QtisVisionPanel.Database;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Integrazione AI (Fase 3a) — Raccolta dati etichettati per il futuro classificatore vision.
    ///
    /// Quando un'ispezione viene finalizzata E le immagini del pezzo sono state salvate su disco,
    /// registra un campione etichettato:
    /// - un sidecar <c>label.json</c> dentro la cartella del pezzo (dataset auto-descrittivo e
    ///   portabile: immagini + etichetta insieme, pronto per l'addestramento offline);
    /// - una riga di indice in <c>tbl_training_samples</c> per query/conteggi.
    ///
    /// Non addestra nulla e non influenza l'ispezione: accumula soltanto il dataset nel tempo.
    /// Additivo e non bloccante. Attivazione via
    /// <c>MachineRuntimeBindings.TrainingDataCollectionEnabled</c> (default disabilitato sulle nuove configurazioni).
    /// </summary>
    public class TrainingDataCollectionService
    {
        private const string SidecarFileName = "label.json";

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly TrainingSampleRepository _repository = new TrainingSampleRepository();
        private volatile bool _enabled;

        public TrainingDataCollectionService()
        {
            _enabled = ReadEnabledFlag();
            _logger.Info($"TRAINING_DATA_COLLECTION_INIT|enabled={_enabled}");
        }

        public bool Enabled => _enabled;

        public void RefreshConfiguration() => _enabled = ReadEnabledFlag();

        /// <summary>
        /// Registra (fire-and-forget) il campione etichettato di un'ispezione, solo se le immagini
        /// del pezzo sono state salvate (pieceFolder valorizzato).
        /// </summary>
        public void CaptureSample(InspectionResult result, string recipe, long productId, string pieceFolder)
        {
            if (!_enabled || result == null)
                return;

            // Nessuna immagine salvata in questo ciclo (salvataggio a percentuale): niente da etichettare.
            if (string.IsNullOrWhiteSpace(pieceFolder))
                return;

            try
            {
                var defects = (result.DetectedDefects ?? new Dictionary<string, bool>())
                    .Where(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();

                var entry = new TrainingSampleEntry
                {
                    Timestamp = DateTime.Now,
                    ProductId = productId,
                    Recipe = recipe,
                    Label = result.IsValid ? "OK" : "NOK",
                    PieceFolder = pieceFolder,
                    Defects = defects,
                    CameraLabels = BuildCameraLabels(result)
                };

                // Sidecar sul disco (dataset portabile) + indice DB, entrambi non bloccanti.
                Task.Run(() => WriteSidecar(entry))
                    .SafeFireAndForget(_logger, "TRAINING_SAMPLE_SIDECAR_FAILED");

                _repository.InsertAsync(entry)
                    .SafeFireAndForget(_logger, "TRAINING_SAMPLE_DB_FAILED");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "TRAINING_SAMPLE_CAPTURE_FAILED");
            }
        }

        // Etichetta per-camera dal risultato-regole della singola camera (solo quelle ispezionate:
        // se una camera non ha ToolBlock il suo ValidationResult e' null e viene omessa).
        private static Dictionary<string, string> BuildCameraLabels(InspectionResult result)
        {
            var labels = new Dictionary<string, string>();
            AddCameraLabel(labels, "top", result.TopResult?.IsValid);
            AddCameraLabel(labels, "side", result.SideResult?.IsValid);
            AddCameraLabel(labels, "front", result.FrontResult?.IsValid);
            AddCameraLabel(labels, "rear", result.RearResult?.IsValid);
            AddCameraLabel(labels, "right", result.RightResult?.IsValid);
            AddCameraLabel(labels, "bottom", result.BottomResult?.IsValid);
            return labels;
        }

        private static void AddCameraLabel(Dictionary<string, string> labels, string camera, bool? isValid)
        {
            if (isValid.HasValue)
                labels[camera] = isValid.Value ? "OK" : "NOK";
        }

        private void WriteSidecar(TrainingSampleEntry entry)
        {
            try
            {
                if (!Directory.Exists(entry.PieceFolder))
                    return;

                string path = Path.Combine(entry.PieceFolder, SidecarFileName);
                // UTF-8 SENZA BOM: il trainer Python legge comunque con 'utf-8-sig' (compatibile con i
                // sidecar gia' esistenti scritti con BOM), ma i nuovi file restano puliti e portabili.
                File.WriteAllText(path, BuildJson(entry), new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "TRAINING_SAMPLE_SIDECAR_WRITE_FAILED");
            }
        }

        private static string BuildJson(TrainingSampleEntry entry)
        {
            var payload = new
            {
                timestamp = entry.Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff"),
                productId = entry.ProductId,
                recipe = entry.Recipe,
                label = entry.Label,
                labels = entry.CameraLabels ?? new Dictionary<string, string>(),
                labelSource = "VisionProRules",
                softwareVersion = typeof(TrainingDataCollectionService).Assembly.GetName().Version?.ToString(),
                pieceFolder = entry.PieceFolder,
                defects = entry.Defects ?? new List<string>()
            };

            return JsonConvert.SerializeObject(payload, Formatting.Indented);
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.TrainingDataCollectionEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
