using NLog;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Integrazione AI (Fase 3) — Classificatore difetti ONNX in SHADOW-MODE.
    ///
    /// Carica un modello .onnx (percorso da configurazione) e, ad ogni ispezione con immagini
    /// salvate, esegue l'inferenza sull'immagine della camera configurata e CONFRONTA la predizione
    /// col risultato a regole gia' calcolato, loggando l'accordo/disaccordo come advisory. NON incide su
    /// ispezione, scarto o contatori: e' una fase di validazione parallela ("shadow") in attesa
    /// che il modello dimostri affidabilita' sul campo.
    ///
    /// Scaffold generico: preprocessing (dimensione input, grayscale/RGB, normalizzazione) da
    /// configurazione. Assunzioni da riadattare al modello reale: layout NCHW, classe 0 = OK.
    /// Disabilitato di default (<c>DefectClassifierEnabled=false</c>): senza modello e' un no-op.
    /// </summary>
    public class OnnxDefectClassifier : IDefectClassifier, IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly object _lock = new object();
        // Tutte le istanze camera condividono una sola risorsa di inferenza. TOP e SIDE non devono
        // mai competere tra loro o con VisionPro usando contemporaneamente tutti i core della CPU.
        // L'attesa e' asincrona e trattiene soltanto path/label, non bitmap o tensor.
        private static readonly SemaphoreSlim GlobalInferenceGate = new SemaphoreSlim(1, 1);
        // Single-flight: al massimo un confronto shadow in esecuzione alla volta. Evita accumulo
        // di task in background se l'inferenza e' piu' lenta della cadenza di ispezione.
        private readonly SemaphoreSlim _shadowGate = new SemaphoreSlim(1, 1);

        // Statistiche shadow-mode su finestra mobile (per la UI di PC Diagnostics). Le regole
        // VisionPro sono la verita' di riferimento: "difetto" = classe positiva.
        private const int MaxShadowSamples = 2000;
        private const int ImageReadyTimeoutMs = 10000;
        private const int ImageReadyPollMs = 50;
        private readonly object _statsLock = new object();
        private readonly List<ShadowSample> _shadowSamples = new List<ShadowSample>();
        private long _configurationGeneration;
        private long _statsGeneration;
        private string _statsContextKey = string.Empty;
        private string _statsContextId = string.Empty;
        private string _statsModelVersion = string.Empty;
        private string _statsModelPath = string.Empty;
        private int _statsInputWidth = 224;
        private int _statsInputHeight = 224;
        private bool _statsGrayscale;
        private double _statsMinConfidence;
        private DateTime _statsStartedUtc = DateTime.UtcNow;
        private int _shadowSkippedBusy;
        private int _shadowImageWaitTimeouts;

        private struct ShadowSample
        {
            public bool RuleDefective;
            public bool MlDefective;
            public bool Uncertain;   // NOK grezzo sotto la soglia di confidenza (declassato a non-difetto)
            public double Confidence;
            public double InferenceMs;
            public double ResourceWaitMs;
        }

        private struct ShadowContextSnapshot
        {
            public string ContextId;
            public string ModelPath;
            public string ModelVersion;
            public double MinConfidence;
        }

        private OnnxWorkerModelDefinition _workerModel;
        private string _modelPath;
        private string _modelVersion;
        private bool _disposed;
        // Fast gate del percorso caldo: false quando disabilitato, modello assente/non valido,
        // durante un reload o in shutdown. Volatile evita lock e accessi disco per ogni ispezione.
        private volatile bool _isOperational;

        // Confidenza minima per dichiarare NOK: un NOK sotto soglia diventa "incerto" e NON viene
        // dichiarato difetto (riduce i falsi allarmi). 0 = filtro disattivato (comportamento invariato).
        private double _minConfidence;

        // Camera servita da questa istanza (top, side, rear, front, bottom: stesse chiavi di
        // label.json). Determina le sigle immagine (GetImageTags), il profilo di configurazione
        // letto e il risultato-regole confrontato.
        private readonly string _cameraRole;

        public bool IsReady => _isOperational;

        public string CameraRole => _cameraRole;

        public OnnxDefectClassifier() : this("top")
        {
        }

        public OnnxDefectClassifier(string cameraRole)
        {
            string role = DefectClassifierCameraProfile.NormalizeRole(cameraRole);
            _cameraRole = string.IsNullOrEmpty(role) ? "top" : role;
            ReloadModelConfiguration();
        }

        public void RefreshConfiguration()
        {
            if (_disposed)
            {
                return;
            }

            ReloadModelConfiguration();
        }

        private void ReloadModelConfiguration()
        {
            // Ferma immediatamente nuove richieste mentre viene applicato il nuovo contesto.
            _isOperational = false;
            CameraModelConfig cfg = null;
            OnnxWorkerModelDefinition nextWorkerModel = null;
            Exception loadException = null;

            try
            {
                cfg = ResolveCameraConfig(new MachineConfigurationService().Load()?.RuntimeBindings, _cameraRole);
            }
            catch (Exception ex)
            {
                loadException = ex;
            }

            var context = BuildModelContext(cfg);

            if (context.Enabled && File.Exists(context.ModelPath))
            {
                try
                {
                    nextWorkerModel = CreateWorkerModelDefinition(context, _cameraRole);
                    if (!OnnxInferenceWorkerClient.Shared.TryConfigureModel(
                        nextWorkerModel,
                        out string workerError))
                    {
                        throw new InvalidOperationException(workerError);
                    }

                    context.Ready = true;
                }
                catch (Exception ex)
                {
                    loadException = ex;
                    nextWorkerModel = null;
                    context.Ready = false;
                }
            }

            OnnxWorkerModelDefinition previousWorkerModel;
            long generation;
            lock (_lock)
            {
                if (_disposed)
                {
                    if (nextWorkerModel != null)
                    {
                        OnnxInferenceWorkerClient.Shared.RemoveModel(nextWorkerModel.Key);
                    }
                    return;
                }

                previousWorkerModel = _workerModel;
                _workerModel = nextWorkerModel;
                _modelPath = context.ModelPath;
                _modelVersion = context.ModelVersion;
                _minConfidence = context.MinConfidence;
                _isOperational = context.Enabled && context.Ready && nextWorkerModel != null;
                generation = Interlocked.Increment(ref _configurationGeneration);
            }

            if (previousWorkerModel != null &&
                (nextWorkerModel == null ||
                 !string.Equals(previousWorkerModel.Key, nextWorkerModel.Key, StringComparison.Ordinal)))
            {
                OnnxInferenceWorkerClient.Shared.RemoveModel(previousWorkerModel.Key);
            }
            ApplyShadowStatsContext(context, generation);

            if (loadException != null)
            {
                _logger.Warn(loadException, $"VISION_ML_INIT_FAILED|camera={_cameraRole}|path={context.ModelPath}");
            }
            else if (!context.Enabled)
            {
                _logger.Info($"VISION_ML_INIT|camera={_cameraRole}|enabled=false");
            }
            else if (!context.Ready)
            {
                _logger.Warn($"VISION_ML_INIT|camera={_cameraRole}|enabled=true|model_missing|path={context.ModelPath}");
            }
            else
            {
                _logger.Info($"VISION_ML_INIT|camera={_cameraRole}|loaded_out_of_process|path={context.ModelPath}|version={context.ModelVersion}|w={context.InputWidth}|h={context.InputHeight}|gray={context.Grayscale}|min_conf={context.MinConfidence:0.###}|generation={generation}|global_parallel=1|worker_memory_limit_mb=512");
            }
        }

        private static ModelContext BuildModelContext(CameraModelConfig cfg)
        {
            var context = new ModelContext
            {
                Enabled = cfg?.Enabled == true,
                ModelPath = cfg?.ModelPath?.Trim() ?? string.Empty,
                ModelVersion = cfg?.ModelVersion?.Trim() ?? string.Empty,
                InputWidth = cfg != null && cfg.InputWidth > 0 ? cfg.InputWidth : 224,
                InputHeight = cfg != null && cfg.InputHeight > 0 ? cfg.InputHeight : 224,
                Grayscale = cfg?.Grayscale == true,
                NormalizeMean = cfg?.NormalizeMean ?? 0.0,
                NormalizeStd = cfg == null || Math.Abs(cfg.NormalizeStd) < 1e-9 ? 255.0 : cfg.NormalizeStd,
                MinConfidence = cfg != null && cfg.MinConfidence > 0.0 && cfg.MinConfidence <= 1.0
                    ? cfg.MinConfidence
                    : 0.0
            };

            try
            {
                if (context.Enabled && File.Exists(context.ModelPath))
                {
                    var info = new FileInfo(context.ModelPath);
                    context.ModelLength = info.Length;
                    context.ModelLastWriteTicks = info.LastWriteTimeUtc.Ticks;
                }
            }
            catch
            {
                context.ModelLength = 0;
                context.ModelLastWriteTicks = 0;
            }

            return context;
        }

        private static OnnxWorkerModelDefinition CreateWorkerModelDefinition(
            ModelContext context,
            string cameraRole)
        {
            return new OnnxWorkerModelDefinition
            {
                Key = BuildContextId("worker|" + context.BuildKey(cameraRole)),
                ModelPath = context.ModelPath,
                InputWidth = context.InputWidth,
                InputHeight = context.InputHeight,
                Grayscale = context.Grayscale,
                NormalizeMean = context.NormalizeMean,
                NormalizeStd = context.NormalizeStd
            };
        }

        private void ApplyShadowStatsContext(ModelContext context, long generation)
        {
            string nextKey = context.BuildKey(_cameraRole);
            string contextId = BuildContextId(nextKey);
            string previousKey;
            bool changed;

            lock (_statsLock)
            {
                previousKey = _statsContextKey;
                changed = !string.Equals(previousKey, nextKey, StringComparison.Ordinal);

                if (changed)
                {
                    _shadowSamples.Clear();
                    _shadowSkippedBusy = 0;
                    _shadowImageWaitTimeouts = 0;
                    _statsStartedUtc = DateTime.UtcNow;
                }

                _statsGeneration = generation;
                _statsContextKey = nextKey;
                _statsContextId = contextId;
                _statsModelVersion = context.ModelVersion ?? string.Empty;
                _statsModelPath = context.ModelPath ?? string.Empty;
                _statsInputWidth = context.InputWidth;
                _statsInputHeight = context.InputHeight;
                _statsGrayscale = context.Grayscale;
                _statsMinConfidence = context.MinConfidence;
            }

            if (!changed)
            {
                return;
            }

            string message = $"VISION_ML_SHADOW_STATS_CONTEXT_CHANGED|camera={_cameraRole}|context={contextId}|generation={generation}|version={context.ModelVersion}|min_conf={context.MinConfidence:0.###}|input={context.InputWidth}x{context.InputHeight}|gray={context.Grayscale}|ready={context.Ready}";
            _logger.Info(message);

            if (!string.IsNullOrEmpty(previousKey))
            {
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    LogLevel.Info,
                    "VISION_ML_SHADOW_STATS_CONTEXT_CHANGED",
                    "AI Vision",
                    message,
                    nameof(OnnxDefectClassifier),
                    context.ModelPath,
                    new Dictionary<string, object>
                    {
                        { "camera", _cameraRole },
                        { "context", contextId },
                        { "model_version", context.ModelVersion ?? string.Empty },
                        { "min_confidence", context.MinConfidence.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) },
                        { "input_width", context.InputWidth },
                        { "input_height", context.InputHeight },
                        { "grayscale", context.Grayscale },
                        { "generation", generation }
                    });
            }
        }

        private static string BuildContextId(string contextKey)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(contextKey ?? string.Empty));
                return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private sealed class CameraModelConfig
        {
            public bool Enabled;
            public string ModelPath;
            public string ModelVersion;
            public int InputWidth;
            public int InputHeight;
            public bool Grayscale;
            public double NormalizeMean;
            public double NormalizeStd;
            public double MinConfidence;
        }

        private sealed class ModelContext
        {
            public bool Enabled;
            public bool Ready;
            public string ModelPath;
            public string ModelVersion;
            public int InputWidth;
            public int InputHeight;
            public bool Grayscale;
            public double NormalizeMean;
            public double NormalizeStd;
            public double MinConfidence;
            public long ModelLength;
            public long ModelLastWriteTicks;

            public string BuildKey(string cameraRole)
            {
                return string.Join("|", new[]
                {
                    cameraRole ?? string.Empty,
                    Enabled ? "enabled" : "disabled",
                    Ready ? "ready" : "not-ready",
                    (ModelPath ?? string.Empty).Trim().ToUpperInvariant(),
                    ModelVersion ?? string.Empty,
                    InputWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    InputHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Grayscale ? "gray" : "rgb",
                    NormalizeMean.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    NormalizeStd.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    MinConfidence.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    ModelLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ModelLastWriteTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            }
        }

        // Seleziona il profilo della camera servita: ogni camera ha modello e preprocessing propri
        // (TOP e SIDE sui campi dedicati, le altre sui profili in lista).
        private static CameraModelConfig ResolveCameraConfig(MachineRuntimeBindings b, string role)
        {
            DefectClassifierCameraProfile profile = b?.ResolveDefectClassifierProfile(role);
            if (profile == null)
                return null;

            return new CameraModelConfig
            {
                Enabled = profile.Enabled,
                ModelPath = profile.ModelPath,
                ModelVersion = profile.ModelVersion,
                InputWidth = profile.InputWidth,
                InputHeight = profile.InputHeight,
                Grayscale = profile.Grayscale,
                NormalizeMean = profile.NormalizeMean,
                NormalizeStd = profile.NormalizeStd,
                MinConfidence = profile.MinConfidence
            };
        }

        public DefectClassificationResult Classify(string imagePath)
        {
            var result = new DefectClassificationResult { IsAvailable = false };

            if (!_isOperational || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return result;

            try
            {
                OnnxWorkerModelDefinition workerModel;
                double minConfidence;
                lock (_lock)
                {
                    if (!_isOperational || _workerModel == null || _disposed)
                        return result;

                    workerModel = _workerModel;
                    minConfidence = _minConfidence;
                }

                if (!OnnxInferenceWorkerClient.Shared.TryClassify(
                    workerModel,
                    imagePath,
                    out OnnxWorkerPrediction prediction,
                    out string workerError))
                {
                    throw new InvalidOperationException(workerError);
                }

                result.IsAvailable = true;
                result.PredictedClass = prediction.ClassIndex;
                result.Confidence = prediction.Confidence;

                bool rawDefective = prediction.ClassIndex != 0; // convenzione: classe 0 = OK
                // La soglia resta applicata dall'HMI dopo l'inferenza isolata.
                result.IsUncertain = rawDefective && minConfidence > 0.0 &&
                    result.Confidence < minConfidence;
                result.PredictedDefective = rawDefective && !result.IsUncertain;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "VISION_ML_CLASSIFY_FAILED");
                result.IsAvailable = false;
            }

            return result;
        }

        /// <summary>
        /// Esegue l'inferenza sull'immagine della camera servita e logga il confronto (shadow-mode)
        /// col risultato a regole. Chiamato da MainWindow con una sola riga.
        ///
        /// L'inferenza (decode immagine + ONNX) e' pesante e viene eseguita OFF-THREAD: non deve
        /// mai bloccare il thread di ispezione real-time. Single-flight: se un confronto e' gia'
        /// in corso questo viene saltato (advisory, non bloccante). No-op se il modello non e'
        /// pronto o non ci sono immagini salvate.
        /// </summary>
        public void RunShadowComparison(InspectionResult ruleResult, string pieceFolder)
        {
            // Percorso disabilitato/mancante: una sola lettura volatile, nessun lock, task o I/O.
            if (!_isOperational || ruleResult == null || string.IsNullOrWhiteSpace(pieceFolder))
                return;

            // Confronto PER-CAMERA: il modello di questa camera va valutato contro il risultato-regole
            // della SUA camera (top->TopResult, side->SideResult), non contro l'esito globale del pezzo.
            bool? ruleValid = ResolveRuleValidity(ruleResult);
            if (ruleValid == null)
                return; // nessuna ispezione per questa camera in questo ciclo: niente da confrontare

            long generation;
            lock (_statsLock)
            {
                generation = _statsGeneration;
            }

            // Acquisizione non bloccante sul thread di ispezione: se un confronto shadow e' gia'
            // in esecuzione, salta questo campione senza attendere.
            try
            {
                if (!_shadowGate.Wait(0))
                {
                    RecordSkippedBusy(generation);
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            // Cattura sincrona del solo dato necessario (bool), poi tutto il resto off-thread.
            bool ruleDefective = !ruleValid.Value;

            Task.Run(async () =>
            {
                try { await RunShadowComparisonCoreAsync(ruleDefective, pieceFolder, generation).ConfigureAwait(false); }
                finally
                {
                    // Se l'app viene chiusa mentre un confronto e' in volo, Dispose() puo' aver gia'
                    // rilasciato il semaforo: ignora l'ObjectDisposedException (shutdown, non un errore).
                    try { _shadowGate.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }).SafeFireAndForget(_logger, "VISION_ML_SHADOW_FAILED");
        }

        private bool? ResolveRuleValidity(InspectionResult ruleResult)
        {
            // Confronto per-camera COERENTE con l'etichetta di training: si usa il risultato-regole
            // DELLA camera servita. Se assente (camera non ispezionata in questo ciclo) si salta il
            // campione — NON si ricade sull'esito globale, che mescolerebbe i difetti di altre camere
            // e romperebbe il contratto con il training (che etichetta per-camera).
            switch (_cameraRole)
            {
                case "side": return ruleResult.SideResult?.IsValid;
                case "rear": return ruleResult.RearResult?.IsValid;
                case "right": return ruleResult.RightResult?.IsValid;
                case "front": return ruleResult.FrontResult?.IsValid;
                case "bottom": return ruleResult.BottomResult?.IsValid;
                default: return ruleResult.TopResult?.IsValid;
            }
        }

        private async Task RunShadowComparisonCoreAsync(bool ruleDefective, string pieceFolder, long generation)
        {
            try
            {
                string image = await FindRawImageAsync(pieceFolder, generation).ConfigureAwait(false);
                if (image == null)
                {
                    RecordImageWaitTimeout(generation, pieceFolder);
                    return;
                }

                if (!IsStatsGenerationCurrent(generation))
                    return;

                // TOP e SIDE condividono una sola inferenza globale. Chi attende non ha ancora
                // allocato bitmap/tensor e l'attesa asincrona non occupa thread del pool.
                var resourceWait = Stopwatch.StartNew();
                await GlobalInferenceGate.WaitAsync().ConfigureAwait(false);
                resourceWait.Stop();

                DefectClassificationResult prediction;
                var inferenceWatch = Stopwatch.StartNew();
                try
                {
                    if (!_isOperational || !IsStatsGenerationCurrent(generation))
                        return;

                    prediction = ClassifyAtLowPriority(image);
                }
                finally
                {
                    inferenceWatch.Stop();
                    GlobalInferenceGate.Release();
                }

                if (!prediction.IsAvailable)
                    return;

                // Un salvataggio impostazioni puo' sostituire modello/preprocessing/soglia mentre
                // il worker era in attesa del file. Il campione della vecchia generazione non deve
                // mai entrare nelle statistiche della nuova configurazione.
                if (!IsStatsGenerationCurrent(generation))
                    return;

                bool agree = prediction.PredictedDefective == ruleDefective;

                // Aggrega per le statistiche shadow-mode (finestra mobile, mostrate in PC Diagnostics).
                ShadowContextSnapshot shadowContext;
                if (!RecordShadowSample(
                    generation,
                    ruleDefective,
                    prediction.PredictedDefective,
                    prediction.IsUncertain,
                    prediction.Confidence,
                    inferenceWatch.Elapsed.TotalMilliseconds,
                    resourceWait.Elapsed.TotalMilliseconds,
                    out shadowContext))
                    return;

                string message = string.Format(
                    "VISION_ML_SHADOW|camera={0}|context={1}|model_version={2}|min_conf={3:0.###}|agree={4}|ml_defective={5}|rule_defective={6}|uncertain={7}|class={8}|confidence={9:0.###}|inference_ms={10:0.0}|resource_wait_ms={11:0.0}",
                    _cameraRole, shadowContext.ContextId, shadowContext.ModelVersion, shadowContext.MinConfidence,
                    agree, prediction.PredictedDefective, ruleDefective, prediction.IsUncertain,
                    prediction.PredictedClass, prediction.Confidence,
                    inferenceWatch.Elapsed.TotalMilliseconds, resourceWait.Elapsed.TotalMilliseconds);

                if (agree)
                    _logger.Debug(message);
                else
                {
                    _logger.Warn(message); // disaccordo: utile per valutare il modello, ancora solo advisory
                    ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                        LogLevel.Warn,
                        "VISION_ML_SHADOW_DISAGREE",
                        "AI Vision",
                        message,
                        nameof(OnnxDefectClassifier),
                        image,
                        new Dictionary<string, object>
                        {
                            { "camera", _cameraRole },
                            { "context", shadowContext.ContextId ?? string.Empty },
                            { "model_path", shadowContext.ModelPath ?? string.Empty },
                            { "model_version", shadowContext.ModelVersion ?? string.Empty },
                            { "min_confidence", shadowContext.MinConfidence.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) },
                            { "predicted_class", prediction.PredictedClass },
                            { "confidence", prediction.Confidence.ToString("0.###") },
                            { "inference_ms", inferenceWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) },
                            { "resource_wait_ms", resourceWait.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) },
                            { "rule_defective", ruleDefective },
                            { "ml_defective", prediction.PredictedDefective }
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "VISION_ML_SHADOW_FAILED");
            }
        }

        private DefectClassificationResult ClassifyAtLowPriority(string imagePath)
        {
            Thread currentThread = Thread.CurrentThread;
            ThreadPriority previousPriority = ThreadPriority.Normal;
            bool priorityChanged = false;

            try
            {
                previousPriority = currentThread.Priority;
                if ((int)previousPriority > (int)ThreadPriority.BelowNormal)
                {
                    currentThread.Priority = ThreadPriority.BelowNormal;
                    priorityChanged = true;
                }
            }
            catch
            {
                // Alcuni host possono vietare il cambio priorita'. I limiti ONNX 1x1 restano attivi.
            }

            try
            {
                return Classify(imagePath);
            }
            finally
            {
                if (priorityChanged)
                {
                    try { currentThread.Priority = previousPriority; }
                    catch { }
                }
            }
        }

        private async Task<string> FindRawImageAsync(string pieceFolder, long generation)
        {
            var startedAt = DateTime.UtcNow;
            while ((DateTime.UtcNow - startedAt).TotalMilliseconds < ImageReadyTimeoutMs)
            {
                if (_disposed || !IsStatsGenerationCurrent(generation))
                    return null;

                string image = FindReadableImage(pieceFolder);
                if (image != null)
                    return image;

                await Task.Delay(ImageReadyPollMs).ConfigureAwait(false);
            }

            return null;
        }

        private string FindReadableImage(string pieceFolder)
        {
            try
            {
                if (!Directory.Exists(pieceFolder))
                    return null;

                // Suffisso per camera (convenzione nomi file dell'HMI, vedi GetImageTags).
                // Preferita la raw *_A.bmp (senza grafica); fallback all'annotata *_Z.jpg.
                string[] tags = GetImageTags(_cameraRole);
                string raw = tags
                    .SelectMany(tag => Directory.GetFiles(pieceFolder, "*" + tag + "A.bmp").OrderBy(path => path))
                    .FirstOrDefault();
                if (IsFileReady(raw))
                    return raw;

                string annotated = tags
                    .SelectMany(tag => Directory.GetFiles(pieceFolder, "*" + tag + "Z.jpg").OrderBy(path => path))
                    .FirstOrDefault();
                return IsFileReady(annotated) ? annotated : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sigle file immagine della camera nel pezzo (chiave vista usata da ISaveImage).
        /// Una camera ha due sigle quando puo' comparire in due viste con lo stesso record:
        /// SIDE -> _F_ (vista Side storica) o _L_ (vista Left); REAR -> _RI_ (vista Right) o _R_
        /// (vista Rear storica). Condiviso con training, verifica dataset e trainer Python.
        /// </summary>
        internal static string[] GetImageTags(string cameraRole)
        {
            switch (DefectClassifierCameraProfile.NormalizeRole(cameraRole))
            {
                case "side": return new[] { "_F_", "_L_" };
                case "rear": return new[] { "_R_" };
                case "right": return new[] { "_RI_" };
                case "front": return new[] { "_FR_" };
                case "bottom": return new[] { "_B_" };
                default: return new[] { "_T_" };
            }
        }

        private static bool IsFileReady(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // FileShare.None garantisce che File.WriteAllBytes abbia chiuso il file prima
                // che Bitmap/ONNX provino a leggerlo. Evita immagini parziali o decode casuali.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return stream.Length > 0;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private bool IsStatsGenerationCurrent(long generation)
        {
            lock (_statsLock)
            {
                return _statsGeneration == generation;
            }
        }

        private void RecordSkippedBusy(long generation)
        {
            lock (_statsLock)
            {
                if (_statsGeneration == generation)
                    _shadowSkippedBusy++;
            }
        }

        private void RecordImageWaitTimeout(long generation, string pieceFolder)
        {
            int timeoutCount;
            lock (_statsLock)
            {
                if (_statsGeneration != generation)
                    return;

                timeoutCount = ++_shadowImageWaitTimeouts;
            }

            if (timeoutCount == 1 || timeoutCount % 25 == 0)
            {
                _logger.Warn($"VISION_ML_SHADOW_IMAGE_TIMEOUT|camera={_cameraRole}|wait_ms={ImageReadyTimeoutMs}|count={timeoutCount}|piece={pieceFolder}");
            }
        }

        private bool RecordShadowSample(
            long generation,
            bool ruleDefective,
            bool mlDefective,
            bool uncertain,
            double confidence,
            double inferenceMs,
            double resourceWaitMs,
            out ShadowContextSnapshot context)
        {
            context = new ShadowContextSnapshot();
            lock (_statsLock)
            {
                if (_statsGeneration != generation)
                    return false;

                _shadowSamples.Add(new ShadowSample
                {
                    RuleDefective = ruleDefective,
                    MlDefective = mlDefective,
                    Uncertain = uncertain,
                    Confidence = confidence,
                    InferenceMs = inferenceMs,
                    ResourceWaitMs = resourceWaitMs
                });
                if (_shadowSamples.Count > MaxShadowSamples)
                    _shadowSamples.RemoveRange(0, _shadowSamples.Count - MaxShadowSamples);

                context.ContextId = _statsContextId;
                context.ModelPath = _statsModelPath;
                context.ModelVersion = _statsModelVersion;
                context.MinConfidence = _statsMinConfidence;
                return true;
            }
        }

        /// <summary>
        /// Snapshot delle statistiche shadow-mode sulla finestra mobile corrente. Verita' di
        /// riferimento = regole VisionPro; positivo = "difetto".
        /// </summary>
        public ShadowModeStats GetShadowStats()
        {
            var stats = new ShadowModeStats();
            double confidenceSum = 0.0;
            double inferenceTotal = 0.0;
            double resourceWaitTotal = 0.0;

            lock (_statsLock)
            {
                stats.CameraRole = _cameraRole;
                stats.ContextId = _statsContextId;
                stats.ModelVersion = _statsModelVersion;
                stats.ModelFileName = string.IsNullOrWhiteSpace(_statsModelPath) ? string.Empty : Path.GetFileName(_statsModelPath);
                stats.InputWidth = _statsInputWidth;
                stats.InputHeight = _statsInputHeight;
                stats.Grayscale = _statsGrayscale;
                stats.MinConfidence = _statsMinConfidence;
                stats.StartedUtc = _statsStartedUtc;
                stats.SkippedBusy = _shadowSkippedBusy;
                stats.ImageWaitTimeouts = _shadowImageWaitTimeouts;

                foreach (var sample in _shadowSamples)
                {
                    confidenceSum += sample.Confidence;
                    inferenceTotal += sample.InferenceMs;
                    resourceWaitTotal += sample.ResourceWaitMs;
                    if (sample.InferenceMs > stats.MaxInferenceMs)
                        stats.MaxInferenceMs = sample.InferenceMs;
                    if (sample.Uncertain) stats.Uncertain++;
                    if (sample.RuleDefective && sample.MlDefective) stats.TruePositive++;
                    else if (sample.RuleDefective && !sample.MlDefective) stats.FalseNegative++;
                    else if (!sample.RuleDefective && sample.MlDefective) stats.FalsePositive++;
                    else stats.TrueNegative++;
                }
                stats.Total = _shadowSamples.Count;
            }

            if (stats.Total > 0)
            {
                stats.AgreementRate = (stats.TruePositive + stats.TrueNegative) / (double)stats.Total;
                stats.MeanConfidence = confidenceSum / stats.Total;
                stats.AverageInferenceMs = inferenceTotal / stats.Total;
                stats.AverageResourceWaitMs = resourceWaitTotal / stats.Total;
            }

            int ruleNok = stats.TruePositive + stats.FalseNegative;
            int mlNok = stats.TruePositive + stats.FalsePositive;
            int ruleOk = stats.TrueNegative + stats.FalsePositive;
            stats.NokRecall = ruleNok > 0 ? stats.TruePositive / (double)ruleNok : 0.0;
            stats.NokPrecision = mlNok > 0 ? stats.TruePositive / (double)mlNok : 0.0;
            stats.FalseAlarmRate = ruleOk > 0 ? stats.FalsePositive / (double)ruleOk : 0.0;
            return stats;
        }

        /// <summary>Azzera la finestra delle statistiche shadow-mode (ripartenza pulita della valutazione).</summary>
        public void ResetShadowStats()
        {
            lock (_statsLock)
            {
                _statsGeneration = Interlocked.Increment(ref _configurationGeneration);
                _shadowSamples.Clear();
                _shadowSkippedBusy = 0;
                _shadowImageWaitTimeouts = 0;
                _statsStartedUtc = DateTime.UtcNow;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _isOperational = false;
            lock (_statsLock)
            {
                _statsGeneration = Interlocked.Increment(ref _configurationGeneration);
            }
            OnnxWorkerModelDefinition workerModel;
            lock (_lock)
            {
                workerModel = _workerModel;
                _workerModel = null;
            }
            if (workerModel != null)
                OnnxInferenceWorkerClient.Shared.RemoveModel(workerModel.Key);
            _shadowGate.Dispose();
        }
    }

    /// <summary>
    /// Statistiche aggregate del confronto shadow-mode ML vs regole VisionPro (finestra mobile).
    /// Verita' di riferimento = regole; "difetto" = classe positiva. Solo lettura per la UI.
    /// </summary>
    public sealed class ShadowModeStats
    {
        public string CameraRole { get; set; }
        public string ContextId { get; set; }
        public string ModelVersion { get; set; }
        public string ModelFileName { get; set; }
        public int InputWidth { get; set; }
        public int InputHeight { get; set; }
        public bool Grayscale { get; set; }
        public double MinConfidence { get; set; }
        public DateTime StartedUtc { get; set; }

        public int Total { get; set; }
        public int TruePositive { get; set; }    // regola NOK & ML NOK  (difetto colto)
        public int FalseNegative { get; set; }    // regola NOK & ML OK   (difetto MANCATO)
        public int FalsePositive { get; set; }    // regola OK  & ML NOK  (falso allarme)
        public int TrueNegative { get; set; }     // regola OK  & ML OK   (buono confermato)
        public int Uncertain { get; set; }         // NOK grezzo declassato dalla soglia di confidenza
        public int SkippedBusy { get; set; }
        public int ImageWaitTimeouts { get; set; }

        public double AgreementRate { get; set; }
        public double NokRecall { get; set; }
        public double NokPrecision { get; set; }
        public double FalseAlarmRate { get; set; }
        public double MeanConfidence { get; set; }
        public double AverageInferenceMs { get; set; }
        public double MaxInferenceMs { get; set; }
        public double AverageResourceWaitMs { get; set; }

        public int RuleNokCount => TruePositive + FalseNegative;
        public int MlNokCount => TruePositive + FalsePositive;
        public bool HasData => Total > 0 || SkippedBusy > 0 || ImageWaitTimeouts > 0;

        public string AgreementText => Total > 0 ? Pct(AgreementRate) : "n/d";
        public string NokRecallText => RuleNokCount > 0 ? Pct(NokRecall) : "n/d";
        public string NokPrecisionText => MlNokCount > 0 ? Pct(NokPrecision) : "n/d";
        public string FalseAlarmText => Total > 0 ? Pct(FalseAlarmRate) : "n/d";
        public string MeanConfidenceText => Total > 0 ? Pct(MeanConfidence) : "n/d";
        public string ContextText
        {
            get
            {
                string version = string.IsNullOrWhiteSpace(ModelVersion) ? "n/d" : ModelVersion;
                string file = string.IsNullOrWhiteSpace(ModelFileName) ? "model n/d" : ModelFileName;
                string threshold = MinConfidence > 0.0
                    ? "NOK conf>=" + MinConfidence.ToString("0.###")
                    : "NOK conf=off";
                string color = Grayscale ? "GRAY" : "RGB";
                string started = StartedUtc == default(DateTime)
                    ? "n/d"
                    : StartedUtc.ToLocalTime().ToString("dd/MM HH:mm");
                string context = string.IsNullOrWhiteSpace(ContextId) ? "n/d" : ContextId;
                return $"{(CameraRole ?? string.Empty).ToUpperInvariant()} | set={context} | {file} | v={version} | {threshold} | {InputWidth}x{InputHeight} {color} | start={started}";
            }
        }
        public string SummaryText =>
            $"Campioni: {Total} | difetti reali: {RuleNokCount} | mancati: {FalseNegative} | falsi allarmi: {FalsePositive} | incerti: {Uncertain} | AI avg/max: {AverageInferenceMs:0}/{MaxInferenceMs:0} ms | attesa risorsa avg: {AverageResourceWaitMs:0} ms | saltati busy: {SkippedBusy} | timeout immagine: {ImageWaitTimeouts}";

        private static string Pct(double value)
        {
            return (value * 100.0).ToString("0.#") + "%";
        }
    }
}
