using Newtonsoft.Json.Linq;
using NLog;
using QtisVisionPanel.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Integrazione AI (Fase 3b) — Training on-demand del classificatore difetti ONNX.
    ///
    /// Orchestrazione del pulsante "Addestra modello" (PC Diagnostics → Controlli AI):
    /// il training vero avviene in uno script Python lanciato come PROCESSO ESTERNO
    /// (<c>Scripts/AI/train_defect_classifier.py</c>) — isolamento totale dall'HMI 24/7:
    /// un crash o un out-of-memory del training non tocca il processo macchina.
    ///
    /// Il servizio: valida il dataset (sidecar label.json raccolti da
    /// TrainingDataCollectionService), calcola il nome versionato (defect_classifier_vN.onnx),
    /// lancia lo script passando il preprocessing corrente della config HMI (dimensioni,
    /// grayscale, mean/std — cosi' il modello esce COERENTE col contratto di
    /// OnnxDefectClassifier), riporta il progresso riga-per-riga e a fine corsa verifica che
    /// l'ONNX prodotto sia caricabile. Il modello NON viene mai abilitato automaticamente:
    /// resta un'azione supervisionata dell'operatore (principio "training offline" della
    /// architettura AI).
    /// </summary>
    public class DefectClassifierTrainingService
    {
        private const int MinSamplesPerClass = 10;
        private const int RecommendedValidationSamplesPerClass = 10;
        private static readonly TimeSpan TrainingTimeout = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan PythonCheckTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TrainingHeartbeatInterval = TimeSpan.FromSeconds(15);

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        // Usato dagli helper statici di risoluzione dell'eseguibile Python.
        private static readonly Logger StaticLogger = LogManager.GetCurrentClassLogger();

        public sealed class TrainingOutcome
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string ModelPath { get; set; }
            public string ModelVersion { get; set; }
            public double? ValAccuracy { get; set; }
            public double? ValNokRecall { get; set; }
            public int OkCount { get; set; }
            public int NokCount { get; set; }
            public string ValidationStrategy { get; set; }
            public string ValidationStatus { get; set; }
            public bool ValidationQualified { get; set; }
            public int ValidationOkCount { get; set; }
            public int ValidationNokCount { get; set; }
            public int RecommendedValidationPerClass { get; set; }
        }

        public sealed class PythonCheckOutcome
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        private sealed class TrainingSettings
        {
            public string CameraRole { get; set; }
            public string PythonPath { get; set; }
            public string OutputDir { get; set; }
            public int Epochs { get; set; }
            public int InputWidth { get; set; }
            public int InputHeight { get; set; }
            public bool Grayscale { get; set; }
            public double NormalizeMean { get; set; }
            public double NormalizeStd { get; set; }

            // Il preprocessing e' PER-CAMERA (ogni camera ha il proprio profilo); python path,
            // cartella modelli ed epoche sono invece proprieta' di macchina condivise.
            public static TrainingSettings From(MachineRuntimeBindings bindings, string cameraRole)
            {
                if (bindings == null)
                    return null;

                string role = DefectClassifierCameraProfile.NormalizeRole(cameraRole);
                DefectClassifierCameraProfile profile = bindings.ResolveDefectClassifierProfile(role);
                if (profile == null)
                    return null;

                int width = profile.InputWidth;
                int height = profile.InputHeight;
                bool grayscale = profile.Grayscale;
                double mean = profile.NormalizeMean;
                double std = profile.NormalizeStd;

                return new TrainingSettings
                {
                    CameraRole = role,
                    PythonPath = NormalizeExecutable(bindings.DefectClassifierTrainingPythonPath),
                    OutputDir = NormalizePath(bindings.DefectClassifierTrainingOutputDir, @"C:\QtisVision\AI\Models"),
                    Epochs = bindings.DefectClassifierTrainingEpochs > 0 ? bindings.DefectClassifierTrainingEpochs : 20,
                    InputWidth = width > 0 ? width : 224,
                    InputHeight = height > 0 ? height : 224,
                    Grayscale = grayscale,
                    NormalizeMean = mean,
                    NormalizeStd = Math.Abs(std) < 1e-9 ? 255.0 : std
                };
            }
        }

        private sealed class ProcessRunResult
        {
            public bool Started { get; set; }
            public bool TimedOut { get; set; }
            public bool Cancelled { get; set; }
            public int ExitCode { get; set; }
            public string StartError { get; set; }
            public string StderrTail { get; set; }
            public JObject ResultJson { get; set; }
        }

        private sealed class DatasetScanSummary
        {
            public int OkCount { get; set; }
            public int NokCount { get; set; }
            public int MissingCameraLabelCount { get; set; }
            public int MissingImageCount { get; set; }
            public int InvalidSidecarCount { get; set; }
        }

        /// <summary>
        /// Esegue il training end-to-end. Non lancia mai eccezioni: ogni errore torna
        /// come <see cref="TrainingOutcome"/> con <c>Success=false</c> e messaggio chiaro.
        /// </summary>
        public async Task<TrainingOutcome> TrainAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            var bindings = new MachineConfigurationService().Load()?.RuntimeBindings;
            return await TrainAsync(bindings, "top", progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Esegue il training della camera indicata (top, side, rear, front, bottom) usando lo snapshot corrente
        /// della UI. Questo evita che modifiche non ancora salvate su machine_runtime_config.xml
        /// vengano ignorate dal training.
        /// </summary>
        public async Task<TrainingOutcome> TrainAsync(MachineRuntimeBindings bindings, string cameraRole, IProgress<string> progress, CancellationToken cancellationToken)
        {
            try
            {
                var settings = TrainingSettings.From(bindings, cameraRole);
                return await TrainCoreAsync(settings, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new TrainingOutcome { Success = false, Message = "Training annullato dall'operatore." };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "AI_TRAINING_RUN_FAILED");
                return new TrainingOutcome { Success = false, Message = $"Errore training: {ex.Message}" };
            }
        }

        public async Task<PythonCheckOutcome> TestPythonAsync(MachineRuntimeBindings bindings, IProgress<string> progress, CancellationToken cancellationToken)
        {
            try
            {
                var settings = TrainingSettings.From(bindings, "top");
                if (settings == null)
                    return new PythonCheckOutcome { Success = false, Message = "Configurazione runtime non disponibile." };

                string scriptPath = GetTrainingScriptPath();
                if (!File.Exists(scriptPath))
                    return new PythonCheckOutcome { Success = false, Message = $"Script di training non trovato: {scriptPath}" };

                progress?.Report("Verifica Python e dipendenze AI...");
                _logger.Info($"AI_TRAINING_PYTHON_TEST_START|python={settings.PythonPath}");

                string arguments = JoinArguments(scriptPath, "--self-test");
                var run = await RunPythonProcessAsync(settings.PythonPath, arguments, PythonCheckTimeout, progress, "AI_TRAINING_PYTHON_TEST", cancellationToken)
                    .ConfigureAwait(false);

                if (!run.Started)
                    return new PythonCheckOutcome { Success = false, Message = $"Python non avviabile ('{settings.PythonPath}'): {run.StartError}" };

                if (run.Cancelled)
                    return new PythonCheckOutcome { Success = false, Message = "Verifica Python annullata." };

                if (run.TimedOut)
                    return new PythonCheckOutcome { Success = false, Message = "Verifica Python interrotta per timeout." };

                if (run.ExitCode != 0 || run.ResultJson == null || run.ResultJson.Value<bool?>("ok") != true)
                {
                    string detail = run.ResultJson?.Value<string>("error");
                    if (string.IsNullOrWhiteSpace(detail))
                        detail = !string.IsNullOrWhiteSpace(run.StderrTail) ? run.StderrTail : $"exit code {run.ExitCode}";

                    _logger.Warn($"AI_TRAINING_PYTHON_TEST_FAILED|detail={detail}");
                    return new PythonCheckOutcome { Success = false, Message = $"Python non pronto: {detail}" };
                }

                string versions = run.ResultJson["versions"]?.ToString(Newtonsoft.Json.Formatting.None);
                string message = string.IsNullOrWhiteSpace(versions)
                    ? "Python pronto per training AI."
                    : $"Python pronto per training AI. Versioni: {versions}";
                _logger.Info($"AI_TRAINING_PYTHON_TEST_OK|{versions}");
                return new PythonCheckOutcome { Success = true, Message = message };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "AI_TRAINING_PYTHON_TEST_EXCEPTION");
                return new PythonCheckOutcome { Success = false, Message = $"Verifica Python fallita: {ex.Message}" };
            }
        }

        // Serializza i training nel processo: chiude la corsa TOCTOU sul nome versionato
        // (defect_classifier_vN.onnx) quando due training partono quasi insieme — possibile
        // via re-entrancy del dialog modale di conferma (doppio click su "Addestra").
        private static readonly SemaphoreSlim TrainingGate = new SemaphoreSlim(1, 1);

        private async Task<TrainingOutcome> TrainCoreAsync(TrainingSettings settings, IProgress<string> progress, CancellationToken ct)
        {
            if (settings == null)
                return new TrainingOutcome { Success = false, Message = "Configurazione runtime non disponibile." };

            if (!TrainingGate.Wait(0))
                return new TrainingOutcome { Success = false, Message = "Un training è già in corso: attendere il completamento." };

            try
            {
                return await TrainCoreLockedAsync(settings, progress, ct).ConfigureAwait(false);
            }
            finally
            {
                TrainingGate.Release();
            }
        }

        private async Task<TrainingOutcome> TrainCoreLockedAsync(TrainingSettings settings, IProgress<string> progress, CancellationToken ct)
        {
            string datasetRoot = MainWindow.configManager?.Config?.Configuration?.ImageDir;
            if (string.IsNullOrWhiteSpace(datasetRoot) || !Directory.Exists(datasetRoot))
            {
                return new TrainingOutcome
                {
                    Success = false,
                    Message = $"Cartella immagini non trovata: '{datasetRoot}'. Il dataset viene raccolto li' dal servizio di raccolta training."
                };
            }

            // Pre-scan veloce del dataset: evita di lanciare Python per poi scoprire che mancano campioni.
            ReportProgress(progress, $"stage=scan|camera={settings.CameraRole}|message=Scansione dataset label.json");
            var counts = await Task.Run(
                () => CountLabeledSamples(datasetRoot, settings.CameraRole, ct), ct).ConfigureAwait(false);
            var okCount = counts.OkCount;
            var nokCount = counts.NokCount;
            if (okCount < MinSamplesPerClass || nokCount < MinSamplesPerClass)
            {
                return new TrainingOutcome
                {
                    Success = false,
                    OkCount = okCount,
                    NokCount = nokCount,
                    Message = $"Dataset {settings.CameraRole.ToUpperInvariant()} insufficiente: servono almeno {MinSamplesPerClass} campioni per classe " +
                              $"(trovati OK={okCount}, NOK={nokCount}; senza etichetta camera={counts.MissingCameraLabelCount}; " +
                              $"senza immagine={counts.MissingImageCount}; sidecar non validi={counts.InvalidSidecarCount}). " +
                              "Abilitare la raccolta dati training e produrre pezzi etichettati."
                };
            }

            string scriptPath = GetTrainingScriptPath();
            if (!File.Exists(scriptPath))
            {
                return new TrainingOutcome { Success = false, Message = $"Script di training non trovato: {scriptPath}" };
            }

            string outputDir = settings.OutputDir;
            Directory.CreateDirectory(outputDir);

            var (outputPath, version) = ResolveNextVersionedPath(outputDir, settings.CameraRole);

            var arguments = new StringBuilder();
            AppendArgument(arguments, scriptPath);
            AppendNamedArgument(arguments, "--camera", settings.CameraRole);
            AppendNamedArgument(arguments, "--dataset-root", datasetRoot.TrimEnd('\\'));
            AppendNamedArgument(arguments, "--output", outputPath);
            AppendNamedArgument(arguments, "--input-width", settings.InputWidth.ToString(CultureInfo.InvariantCulture));
            AppendNamedArgument(arguments, "--input-height", settings.InputHeight.ToString(CultureInfo.InvariantCulture));
            AppendNamedArgument(arguments, "--norm-mean", settings.NormalizeMean.ToString(CultureInfo.InvariantCulture));
            AppendNamedArgument(arguments, "--norm-std", settings.NormalizeStd.ToString(CultureInfo.InvariantCulture));
            AppendNamedArgument(arguments, "--epochs", settings.Epochs.ToString(CultureInfo.InvariantCulture));
            AppendNamedArgument(arguments, "--min-per-class", MinSamplesPerClass.ToString(CultureInfo.InvariantCulture));
            AppendNamedArgument(arguments, "--recommended-val-per-class", RecommendedValidationSamplesPerClass.ToString(CultureInfo.InvariantCulture));
            if (settings.Grayscale)
            {
                AppendArgument(arguments, "--grayscale");
            }

            _logger.Info($"AI_TRAINING_START|camera={settings.CameraRole}|python={settings.PythonPath}|output={outputPath}|ok={okCount}|nok={nokCount}|epochs={settings.Epochs}|input={settings.InputWidth}x{settings.InputHeight}|gray={settings.Grayscale}");
            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                LogLevel.Info,
                "AI_TRAINING_START",
                "AI Vision",
                $"Avvio training modello ONNX: {outputPath}",
                nameof(DefectClassifierTrainingService),
                $"camera={settings.CameraRole}; ok={okCount}; nok={nokCount}; epochs={settings.Epochs}; input={settings.InputWidth}x{settings.InputHeight}; gray={settings.Grayscale}",
                null);
            ReportProgress(progress, $"stage=start|camera={settings.CameraRole}|message=Avvio training OK={okCount} NOK={nokCount} epoche={settings.Epochs}");

            var run = await RunPythonProcessAsync(settings.PythonPath, arguments.ToString(), TrainingTimeout, progress, "AI_TRAINING", ct)
                .ConfigureAwait(false);

            if (!run.Started)
            {
                return new TrainingOutcome
                {
                    Success = false,
                    OkCount = okCount,
                    NokCount = nokCount,
                    Message = $"Python non avviabile ('{settings.PythonPath}'): {run.StartError}. Verificare l'installazione o il campo DefectClassifierTrainingPythonPath."
                };
            }

            if (run.Cancelled)
            {
                _logger.Warn($"AI_TRAINING_CANCELLED|camera={settings.CameraRole}");
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(LogLevel.Warn, "AI_TRAINING_CANCELLED", "AI Vision",
                    $"Training modello ONNX {settings.CameraRole} annullato dall'operatore", nameof(DefectClassifierTrainingService), null, null);
                return new TrainingOutcome { Success = false, OkCount = okCount, NokCount = nokCount, Message = "Training annullato dall'operatore." };
            }

            if (run.TimedOut)
            {
                _logger.Warn($"AI_TRAINING_TIMEOUT|camera={settings.CameraRole}|minutes={TrainingTimeout.TotalMinutes:0}");
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(LogLevel.Warn, "AI_TRAINING_TIMEOUT", "AI Vision",
                    $"Training modello ONNX {settings.CameraRole} interrotto per timeout ({TrainingTimeout.TotalMinutes:0} min)", nameof(DefectClassifierTrainingService), outputPath, null);
                return new TrainingOutcome { Success = false, OkCount = okCount, NokCount = nokCount, Message = $"Training interrotto per timeout ({TrainingTimeout.TotalMinutes:0} minuti)." };
            }

            if (run.ExitCode != 0 || run.ResultJson == null || run.ResultJson.Value<bool?>("ok") != true)
            {
                string detail = run.ResultJson?.Value<string>("error");
                if (string.IsNullOrWhiteSpace(detail))
                    detail = !string.IsNullOrWhiteSpace(run.StderrTail) ? run.StderrTail : $"exit code {run.ExitCode}";

                _logger.Warn($"AI_TRAINING_FAILED|camera={settings.CameraRole}|exit={run.ExitCode}|detail={detail}");
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(LogLevel.Warn, "AI_TRAINING_FAILED", "AI Vision",
                    $"Training modello ONNX {settings.CameraRole} fallito: {detail}", nameof(DefectClassifierTrainingService), outputPath, null);
                return new TrainingOutcome { Success = false, OkCount = okCount, NokCount = nokCount, Message = $"Training fallito: {detail}" };
            }

            if (!File.Exists(outputPath))
            {
                return new TrainingOutcome { Success = false, Message = $"Il file modello non risulta creato: {outputPath}" };
            }

            // Verifica finale: il modello deve essere caricabile dallo stesso runtime ONNX dell'HMI.
            string loadError = TryValidateOnnx(outputPath);
            if (loadError != null)
            {
                return new TrainingOutcome { Success = false, Message = $"Modello creato ma NON caricabile da ONNX Runtime: {loadError}" };
            }

            var metrics = run.ResultJson["metrics"] as JObject;
            var validation = run.ResultJson["validation"] as JObject;
            var outcome = new TrainingOutcome
            {
                Success = true,
                ModelPath = outputPath,
                ModelVersion = version,
                OkCount = okCount,
                NokCount = nokCount,
                ValAccuracy = metrics?.Value<double?>("val_accuracy"),
                ValNokRecall = metrics?.Value<double?>("val_nok_recall"),
                ValidationStrategy = validation?.Value<string>("strategy") ?? "unknown",
                ValidationStatus = validation?.Value<string>("status") ?? "provisional",
                ValidationQualified = validation?.Value<bool?>("qualified") == true,
                ValidationOkCount = validation?.Value<int?>("ok") ?? 0,
                ValidationNokCount = validation?.Value<int?>("nok") ?? 0,
                RecommendedValidationPerClass = validation?.Value<int?>("recommended_per_class") ?? RecommendedValidationSamplesPerClass
            };
            outcome.Message = outcome.ValidationQualified
                ? "Training completato con holdout di validazione sufficiente."
                : $"Training completato, ma validazione provvisoria (holdout OK={outcome.ValidationOkCount}, NOK={outcome.ValidationNokCount}; consigliati almeno {outcome.RecommendedValidationPerClass} per classe).";

            string completedMessage = $"AI_TRAINING_COMPLETED|camera={settings.CameraRole}|model={outputPath}|val_acc={outcome.ValAccuracy:0.###}|nok_recall={outcome.ValNokRecall:0.###}|validation={outcome.ValidationStatus}|val_ok={outcome.ValidationOkCount}|val_nok={outcome.ValidationNokCount}|strategy={outcome.ValidationStrategy}";
            if (outcome.ValidationQualified)
                _logger.Info(completedMessage);
            else
                _logger.Warn(completedMessage);

            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                outcome.ValidationQualified ? LogLevel.Info : LogLevel.Warn,
                outcome.ValidationQualified ? "AI_TRAINING_COMPLETED" : "AI_TRAINING_COMPLETED_PROVISIONAL",
                "AI Vision",
                outcome.ValidationQualified
                    ? $"Training modello ONNX completato: {outputPath}"
                    : $"Training modello ONNX completato con validazione provvisoria: {outputPath}",
                nameof(DefectClassifierTrainingService),
                $"camera={settings.CameraRole}; val_acc={outcome.ValAccuracy:0.###}; nok_recall={outcome.ValNokRecall:0.###}; ok={okCount}; nok={nokCount}; validation={outcome.ValidationStatus}; val_ok={outcome.ValidationOkCount}; val_nok={outcome.ValidationNokCount}; strategy={outcome.ValidationStrategy}",
                null);
            return outcome;
        }

        private async Task<ProcessRunResult> RunPythonProcessAsync(
            string pythonExe,
            string arguments,
            TimeSpan timeout,
            IProgress<string> progress,
            string logPrefix,
            CancellationToken ct)
        {
            var result = new ProcessRunResult();
            JObject resultJson = null;
            var stderrTail = new StringBuilder();
            long lastOutputTicks = DateTime.UtcNow.Ticks;

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                process.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) => exited.TrySetResult(true);

                process.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data))
                        return;

                    Interlocked.Exchange(ref lastOutputTicks, DateTime.UtcNow.Ticks);
                    if (e.Data.StartsWith("PROGRESS|", StringComparison.Ordinal))
                    {
                        string payload = e.Data.Substring("PROGRESS|".Length);
                        ReportProgress(progress, payload);
                    }
                    else if (e.Data.StartsWith("RESULT|", StringComparison.Ordinal))
                    {
                        try { resultJson = JObject.Parse(e.Data.Substring("RESULT|".Length)); }
                        catch (Exception ex) { _logger.Warn(ex, $"{logPrefix}_RESULT_PARSE_FAILED"); }
                    }
                    else
                    {
                        _logger.Info($"{logPrefix}_STDOUT|{e.Data}");
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data))
                        return;
                    Interlocked.Exchange(ref lastOutputTicks, DateTime.UtcNow.Ticks);
                    // Trim DOPO l'append: il buffer resta sempre <= 4000 caratteri (coda dello stderr).
                    stderrTail.AppendLine(e.Data);
                    if (stderrTail.Length > 4000)
                        stderrTail.Remove(0, stderrTail.Length - 4000);
                    _logger.Warn($"{logPrefix}_STDERR|{e.Data}");
                };

                try
                {
                    if (!process.Start())
                    {
                        result.Started = false;
                        result.StartError = "Process.Start ha restituito false.";
                        return result;
                    }
                    result.Started = true;
                }
                catch (Exception ex)
                {
                    result.Started = false;
                    result.StartError = ex.Message;
                    return result;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                ReportProgress(progress, "stage=process|message=Processo Python avviato, attesa output training");

                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(timeout);
                    var heartbeatTask = Task.Run(async () =>
                    {
                        while (!timeoutCts.IsCancellationRequested)
                        {
                            try
                            {
                                await Task.Delay(TrainingHeartbeatInterval, timeoutCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }

                            if (timeoutCts.IsCancellationRequested || process.HasExited)
                                break;

                            var quietFor = DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastOutputTicks), DateTimeKind.Utc);
                            ReportProgress(progress,
                                $"stage=running|message=Training Python in esecuzione, nessun nuovo output da {quietFor.TotalSeconds:0}s");
                        }
                    }, CancellationToken.None);

                    bool watchdogTriggered = false;
                    var killRegistration = timeoutCts.Token.Register(() =>
                    {
                        watchdogTriggered = true;
                        TryKill(process);
                    });
                    try
                    {
                        await exited.Task.ConfigureAwait(false);
                    }
                    finally
                    {
                        killRegistration.Dispose();
                        if (!timeoutCts.IsCancellationRequested)
                            timeoutCts.Cancel();
                        try { await heartbeatTask.ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                    }

                    process.WaitForExit();
                    result.Cancelled = ct.IsCancellationRequested;
                    result.TimedOut = watchdogTriggered && !result.Cancelled;
                }

                result.ExitCode = process.ExitCode;
                result.ResultJson = resultJson;
                result.StderrTail = stderrTail.ToString().Trim();
                return result;
            }
        }

        private void ReportProgress(IProgress<string> progress, string payload)
        {
            string message = string.IsNullOrWhiteSpace(payload)
                ? "Training AI in corso..."
                : payload.Replace("|", "  ");
            _logger.Info($"AI_TRAINING_PROGRESS|{payload}");
            progress?.Report(message);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception)
            {
                // Il processo puo' essere gia' uscito tra il check e il Kill: non-critico.
            }
        }

        private static string GetTrainingScriptPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "AI", "train_defect_classifier.py");
        }

        /// <summary>
        /// Conta solo i campioni realmente utilizzabili dalla camera richiesta. Il TOP mantiene il
        /// fallback alla label globale per i dataset storici; le altre camere richiedono la propria
        /// etichetta (labels.side, labels.rear, ...) per evitare che un difetto di un'altra camera
        /// etichetti come NOK un'immagine buona.
        /// </summary>
        private DatasetScanSummary CountLabeledSamples(string datasetRoot, string cameraRole, CancellationToken ct)
        {
            var summary = new DatasetScanSummary();
            string role = DefectClassifierCameraProfile.NormalizeRole(cameraRole);
            bool top = role == "top";
            try
            {
                foreach (var file in Directory.EnumerateFiles(datasetRoot, "label.json", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var sidecar = JObject.Parse(File.ReadAllText(file));
                        var cameraLabels = sidecar["labels"] as JObject;
                        bool hasCameraLabelsBlock = cameraLabels != null && cameraLabels.Properties().Any();
                        string label = cameraLabels?.Value<string>(role);
                        // Stessa regola del trainer Python: il fallback alla label globale vale
                        // solo per i sidecar TOP legacy privi di un blocco labels non vuoto.
                        // Se labels esiste ma top manca/e' vuoto, quella camera non va inventata.
                        if (top && label == null && !hasCameraLabelsBlock)
                            label = sidecar.Value<string>("label");

                        label = label?.Trim().ToUpperInvariant();
                        if (label != "OK" && label != "NOK")
                        {
                            summary.MissingCameraLabelCount++;
                            continue;
                        }

                        if (!HasCameraImage(Path.GetDirectoryName(file), role))
                        {
                            summary.MissingImageCount++;
                            continue;
                        }

                        if (label == "OK") summary.OkCount++;
                        else summary.NokCount++;
                    }
                    catch (Exception)
                    {
                        summary.InvalidSidecarCount++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "AI_TRAINING_DATASET_SCAN_FAILED");
            }

            return summary;
        }

        private static bool HasCameraImage(string pieceFolder, string cameraRole)
        {
            if (string.IsNullOrWhiteSpace(pieceFolder) || !Directory.Exists(pieceFolder))
                return false;

            return OnnxDefectClassifier.GetImageTags(cameraRole).Any(tag =>
                Directory.EnumerateFiles(pieceFolder, "*" + tag + "A.bmp", SearchOption.TopDirectoryOnly).Any() ||
                Directory.EnumerateFiles(pieceFolder, "*" + tag + "Z.jpg", SearchOption.TopDirectoryOnly).Any());
        }

        /// <summary>
        /// Nome versionato per camera: TOP -> defect_classifier_v1.onnx; le altre camere
        /// -> defect_classifier_{camera}_v1.onnx (side, rear, front, bottom).
        /// Primo numero libero nella cartella (i modelli delle diverse camere non collidono).
        /// </summary>
        private static (string path, string version) ResolveNextVersionedPath(string outputDir, string cameraRole)
        {
            string role = DefectClassifierCameraProfile.NormalizeRole(cameraRole);
            string suffix = string.IsNullOrEmpty(role) || role == "top" ? string.Empty : role + "_";
            string prefix = $"defect_classifier_{suffix}v";

            for (int n = 1; n < 1000; n++)
            {
                string candidate = Path.Combine(outputDir, $"{prefix}{n}.onnx");
                if (!File.Exists(candidate))
                    return (candidate, $"v{n}");
            }

            string fallback = Path.Combine(outputDir, $"defect_classifier_{suffix}{DateTime.Now:yyyyMMdd_HHmmss}.onnx");
            return (fallback, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        }

        private static string NormalizeExecutable(string value)
        {
            value = NormalizePath(value, "python");
            if (string.IsNullOrWhiteSpace(value))
                value = "python";
            return ResolvePythonExecutable(value);
        }

        private static string NormalizePath(string value, string fallback)
        {
            value = Environment.ExpandEnvironmentVariables(value ?? string.Empty).Trim().Trim('"');
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        /// <summary>
        /// Risolve l'eseguibile Python a un percorso ASSOLUTO.
        ///
        /// Con <c>UseShellExecute=false</c>, <c>Process.Start</c> cerca l'eseguibile nel PATH del
        /// PROCESSO dell'app, che spesso NON contiene l'installazione per-utente di Python (PATH
        /// utente aggiornato dopo l'avvio dell'app, oppure app avviata in un contesto diverso): da
        /// qui l'errore "Impossibile trovare il file specificato". Inoltre l'alias dello Store
        /// (<c>...\Microsoft\WindowsApps\python.exe</c>) e' uno stub che fa fallire CreateProcess.
        ///
        /// Strategia: un percorso esplicito viene rispettato; un nome nudo (python/python.exe/py)
        /// viene cercato nel PATH (saltando WindowsApps) e nelle cartelle d'installazione tipiche.
        /// Se nulla viene trovato si restituisce il valore originale (l'errore di avvio restera'
        /// esplicito e mostrera' il nome cercato).
        /// </summary>
        private static string ResolvePythonExecutable(string configured)
        {
            try
            {
                // Percorso esplicito (contiene separatori): rispettalo com'e'.
                if (configured.IndexOf('\\') >= 0 || configured.IndexOf('/') >= 0)
                    return configured;

                string exeName = configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? configured
                    : configured + ".exe";

                string fromPath = TryFindOnPath(exeName);
                if (fromPath != null)
                {
                    StaticLogger.Info($"AI_TRAINING_PYTHON_RESOLVED|source=PATH|path={fromPath}");
                    return fromPath;
                }

                string fromCommon = TryProbeCommonPythonDirs(exeName);
                if (fromCommon != null)
                {
                    StaticLogger.Info($"AI_TRAINING_PYTHON_RESOLVED|source=probe|path={fromCommon}");
                    return fromCommon;
                }

                // py launcher (installato con python.org, in C:\Windows): forwarda gli argomenti allo script.
                string launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe");
                if (File.Exists(launcher))
                {
                    StaticLogger.Info($"AI_TRAINING_PYTHON_RESOLVED|source=py-launcher|path={launcher}");
                    return launcher;
                }

                StaticLogger.Warn($"AI_TRAINING_PYTHON_UNRESOLVED|configured={configured} — uso il nome cosi' com'e'");
                return configured;
            }
            catch (Exception ex)
            {
                StaticLogger.Warn(ex, "AI_TRAINING_PYTHON_RESOLVE_FAILED");
                return configured;
            }
        }

        private static string TryFindOnPath(string exeName)
        {
            // Unisce il PATH del PROCESSO con quello UTENTE e MACCHINA letti dal registro: cosi' si
            // trova Python anche se il PATH utente e' stato aggiornato DOPO l'avvio dell'app (il
            // processo mantiene un PATH "vecchio" finche' non riparte).
            var pathSources = new StringBuilder();
            pathSources.Append(Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
            foreach (var target in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                try
                {
                    string extra = Environment.GetEnvironmentVariable("PATH", target);
                    if (!string.IsNullOrWhiteSpace(extra))
                        pathSources.Append(';').Append(extra);
                }
                catch (Exception)
                {
                    // Lettura registro non disponibile in questo contesto: usa solo il PATH di processo.
                }
            }

            foreach (var rawDir in pathSources.ToString().Split(';'))
            {
                if (string.IsNullOrWhiteSpace(rawDir))
                    continue;

                // Salta l'alias Store: stub 0-byte che apre il Microsoft Store e fa fallire CreateProcess.
                if (rawDir.IndexOf(@"\Microsoft\WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                try
                {
                    string candidate = Path.Combine(Environment.ExpandEnvironmentVariables(rawDir.Trim().Trim('"')), exeName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Voce PATH con caratteri non validi: ignora e prosegui.
                }
            }

            return null;
        }

        private static string TryProbeCommonPythonDirs(string exeName)
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] baseDirs =
            {
                Path.Combine(localApp, "Programs", "Python"),                          // python.org "install for current user"
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),     // install di sistema
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"C:\"                                                                  // C:\Python3xx classico
            };

            foreach (var baseDir in baseDirs)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                        continue;

                    var pythonDirs = Directory.GetDirectories(baseDir, "Python3*");
                    Array.Sort(pythonDirs, StringComparer.OrdinalIgnoreCase); // piu' recente per ultimo
                    for (int i = pythonDirs.Length - 1; i >= 0; i--)
                    {
                        string candidate = Path.Combine(pythonDirs[i], exeName);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
                catch (Exception)
                {
                    // Cartella non accessibile: passa alla successiva.
                }
            }

            return null;
        }

        private static string JoinArguments(params string[] args)
        {
            var builder = new StringBuilder();
            foreach (var arg in args)
                AppendArgument(builder, arg);
            return builder.ToString();
        }

        private static void AppendNamedArgument(StringBuilder builder, string name, string value)
        {
            AppendArgument(builder, name);
            AppendArgument(builder, value);
        }

        private static void AppendArgument(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(QuoteArgument(value ?? string.Empty));
        }

        private static string QuoteArgument(string value)
        {
            if (value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"'))
                return value;

            var builder = new StringBuilder();
            builder.Append('"');
            int slashCount = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    slashCount++;
                    continue;
                }

                if (c == '"')
                {
                    builder.Append('\\', slashCount * 2 + 1);
                    builder.Append('"');
                    slashCount = 0;
                    continue;
                }

                if (slashCount > 0)
                {
                    builder.Append('\\', slashCount);
                    slashCount = 0;
                }
                builder.Append(c);
            }

            if (slashCount > 0)
                builder.Append('\\', slashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// Verifica il modello nel worker ONNX esterno. L'HMI non deve caricare
        /// Microsoft ONNX Runtime nello stesso processo di VisionPro ViDi EL.
        /// </summary>
        private string TryValidateOnnx(string modelPath)
        {
            return OnnxInferenceWorkerClient.Shared.TryValidateModel(modelPath, out string error)
                ? null
                : error;
        }
    }
}
