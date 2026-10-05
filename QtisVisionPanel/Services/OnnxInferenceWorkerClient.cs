using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    internal sealed class OnnxWorkerModelDefinition
    {
        public string Key { get; set; }
        public string ModelPath { get; set; }
        public int InputWidth { get; set; }
        public int InputHeight { get; set; }
        public bool Grayscale { get; set; }
        public double NormalizeMean { get; set; }
        public double NormalizeStd { get; set; }
    }

    internal struct OnnxWorkerPrediction
    {
        public int ClassIndex;
        public double Confidence;
    }

    /// <summary>
    /// Hosts Microsoft ONNX Runtime outside the HMI process. VisionPro ViDi EL
    /// ships a different native onnxruntime.dll; process isolation prevents the
    /// Windows loader from binding either product to the other product's DLL.
    /// </summary>
    internal sealed class OnnxInferenceWorkerClient
    {
        private const int RequestTimeoutMs = 30000;
        private const int ShutdownTimeoutMs = 2000;
        private const long MaxWorkerWorkingSetBytes = 512L * 1024L * 1024L;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Lazy<OnnxInferenceWorkerClient> Instance =
            new Lazy<OnnxInferenceWorkerClient>(() => new OnnxInferenceWorkerClient());

        private readonly object _sync = new object();
        private readonly Dictionary<string, OnnxWorkerModelDefinition> _models =
            new Dictionary<string, OnnxWorkerModelDefinition>(StringComparer.Ordinal);
        private readonly HashSet<string> _loadedModels =
            new HashSet<string>(StringComparer.Ordinal);

        private Process _process;
        private StreamWriter _input;
        private StreamReader _output;
        private int _requestSequence;

        private OnnxInferenceWorkerClient()
        {
        }

        public static OnnxInferenceWorkerClient Shared => Instance.Value;

        public bool TryConfigureModel(OnnxWorkerModelDefinition definition, out string error)
        {
            error = null;
            if (!ValidateDefinition(definition, out error))
            {
                return false;
            }

            lock (_sync)
            {
                _models[definition.Key] = Clone(definition);
                if (EnsureModelLoadedLocked(definition.Key, out error))
                {
                    return true;
                }

                _models.Remove(definition.Key);
                _loadedModels.Remove(definition.Key);
                if (_models.Count == 0)
                {
                    StopProcessLocked();
                }

                return false;
            }
        }

        public bool TryClassify(
            OnnxWorkerModelDefinition definition,
            string imagePath,
            out OnnxWorkerPrediction prediction,
            out string error)
        {
            prediction = new OnnxWorkerPrediction();
            error = null;

            if (!ValidateDefinition(definition, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                error = "Inspection image not found: " + (imagePath ?? string.Empty);
                return false;
            }

            lock (_sync)
            {
                _models[definition.Key] = Clone(definition);
                if (!EnsureModelLoadedLocked(definition.Key, out error))
                {
                    return false;
                }

                string response = SendRequestLocked(
                    "RUN",
                    new[] { Encode(definition.Key), Encode(imagePath) },
                    RequestTimeoutMs,
                    out error);

                if (response == null)
                {
                    return false;
                }

                string[] parts = response.Split('\t');
                if (parts.Length != 4 || !string.Equals(parts[0], "RESULT", StringComparison.Ordinal))
                {
                    error = "Unexpected ONNX worker response: " + response;
                    return false;
                }

                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out prediction.ClassIndex) ||
                    !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out prediction.Confidence) ||
                    double.IsNaN(prediction.Confidence) ||
                    double.IsInfinity(prediction.Confidence))
                {
                    error = "Invalid ONNX worker result: " + response;
                    return false;
                }

                return true;
            }
        }

        public bool TryValidateModel(string modelPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                error = "ONNX model not found: " + (modelPath ?? string.Empty);
                return false;
            }

            lock (_sync)
            {
                if (!EnsureProcessLocked(out error))
                {
                    return false;
                }

                string response = SendRequestLocked(
                    "VALIDATE",
                    new[] { Encode(modelPath) },
                    RequestTimeoutMs,
                    out error);

                bool valid = response != null &&
                    response.StartsWith("VALID\t", StringComparison.Ordinal);
                if (!valid && string.IsNullOrWhiteSpace(error))
                {
                    error = "Unexpected ONNX worker validation response: " +
                        (response ?? "<empty>");
                }

                if (_models.Count == 0)
                {
                    StopProcessLocked();
                }

                return valid;
            }
        }

        public void RemoveModel(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (_sync)
            {
                _models.Remove(key);
                _loadedModels.Remove(key);

                if (IsProcessRunningLocked())
                {
                    SendRequestLocked(
                        "REMOVE",
                        new[] { Encode(key) },
                        ShutdownTimeoutMs,
                        out _);
                }

                if (_models.Count == 0)
                {
                    StopProcessLocked();
                }
            }
        }

        private bool EnsureModelLoadedLocked(string key, out string error)
        {
            error = null;
            if (!_models.TryGetValue(key, out OnnxWorkerModelDefinition definition))
            {
                error = "ONNX worker model definition not registered: " + key;
                return false;
            }

            if (!EnsureProcessLocked(out error))
            {
                return false;
            }

            if (_loadedModels.Contains(key))
            {
                return true;
            }

            string response = SendRequestLocked(
                "LOAD",
                new[]
                {
                    Encode(definition.Key),
                    Encode(definition.ModelPath),
                    definition.InputWidth.ToString(CultureInfo.InvariantCulture),
                    definition.InputHeight.ToString(CultureInfo.InvariantCulture),
                    definition.Grayscale ? "1" : "0",
                    definition.NormalizeMean.ToString("R", CultureInfo.InvariantCulture),
                    definition.NormalizeStd.ToString("R", CultureInfo.InvariantCulture)
                },
                RequestTimeoutMs,
                out error);

            if (response == null || !response.StartsWith("READY\t", StringComparison.Ordinal))
            {
                if (error == null)
                {
                    error = "Unexpected ONNX worker load response: " + response;
                }

                return false;
            }

            _loadedModels.Add(key);
            return true;
        }

        private bool EnsureProcessLocked(out string error)
        {
            error = null;

            if (IsProcessRunningLocked())
            {
                try
                {
                    if (_process.WorkingSet64 <= MaxWorkerWorkingSetBytes)
                    {
                        return true;
                    }

                    Logger.Warn(
                        "VISION_ML_WORKER_MEMORY_LIMIT|pid={0}|working_set_mb={1:0.0}|limit_mb={2}",
                        _process.Id,
                        _process.WorkingSet64 / 1024.0 / 1024.0,
                        MaxWorkerWorkingSetBytes / 1024 / 1024);
                }
                catch
                {
                    return true;
                }

                StopProcessLocked();
            }
            else if (_process != null)
            {
                // Release redirected streams and the exited Process instance
                // before replacing a worker that stopped unexpectedly.
                StopProcessLocked();
            }

            string workerPath = ResolveWorkerPath();
            if (!File.Exists(workerPath))
            {
                error = "ONNX worker not installed: " + workerPath;
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = workerPath,
                    WorkingDirectory = Path.GetDirectoryName(workerPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                _process.ErrorDataReceived += OnWorkerErrorDataReceived;

                if (!_process.Start())
                {
                    throw new InvalidOperationException("Process.Start returned false.");
                }

                _input = _process.StandardInput;
                _input.AutoFlush = true;
                _output = _process.StandardOutput;
                _process.BeginErrorReadLine();

                try
                {
                    _process.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch
                {
                    // Priority is advisory; one-thread ONNX limits remain active.
                }

                _loadedModels.Clear();
                string pong = SendRequestLocked("PING", Array.Empty<string>(), 10000, out error);
                if (pong == null || !pong.StartsWith("PONG\t", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(error ?? "ONNX worker did not answer PING.");
                }

                Logger.Info(
                    "VISION_ML_WORKER_STARTED|pid={0}|path={1}|memory_limit_mb={2}",
                    _process.Id,
                    workerPath,
                    MaxWorkerWorkingSetBytes / 1024 / 1024);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.Warn(ex, "VISION_ML_WORKER_START_FAILED|path={0}", workerPath);
                StopProcessLocked();
                return false;
            }
        }

        private string SendRequestLocked(
            string command,
            string[] arguments,
            int timeoutMs,
            out string error)
        {
            error = null;
            if (!IsProcessRunningLocked() || _input == null || _output == null)
            {
                error = "ONNX worker is not running.";
                return null;
            }

            string requestId = Interlocked.Increment(ref _requestSequence)
                .ToString(CultureInfo.InvariantCulture);
            var request = new StringBuilder(command)
                .Append('\t')
                .Append(requestId);

            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    request.Append('\t').Append(argument ?? string.Empty);
                }
            }

            try
            {
                _input.WriteLine(request.ToString());
                Task<string> readTask = _output.ReadLineAsync();
                if (!readTask.Wait(timeoutMs))
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "ONNX worker timeout after {0} ms for {1}.",
                        timeoutMs,
                        command);
                    Logger.Warn("VISION_ML_WORKER_TIMEOUT|command={0}|timeout_ms={1}", command, timeoutMs);
                    StopProcessLocked();
                    return null;
                }

                string response = (readTask.Result ?? string.Empty).TrimStart('\uFEFF');
                string[] parts = response.Split('\t');
                if (parts.Length < 2 || !string.Equals(parts[1], requestId, StringComparison.Ordinal))
                {
                    error = "ONNX worker protocol mismatch: " + response;
                    StopProcessLocked();
                    return null;
                }

                if (string.Equals(parts[0], "ERROR", StringComparison.Ordinal))
                {
                    error = parts.Length > 2 ? Decode(parts[2]) : "Unknown ONNX worker error.";
                    return null;
                }

                return response;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                Logger.Warn(ex.GetBaseException(), "VISION_ML_WORKER_REQUEST_FAILED|command={0}", command);
                StopProcessLocked();
                return null;
            }
        }

        private bool IsProcessRunningLocked()
        {
            try
            {
                return _process != null && !_process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void StopProcessLocked()
        {
            Process process = _process;
            StreamWriter input = _input;
            StreamReader output = _output;

            _process = null;
            _input = null;
            _output = null;
            _loadedModels.Clear();

            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited && input != null)
                {
                    string requestId = Interlocked.Increment(ref _requestSequence)
                        .ToString(CultureInfo.InvariantCulture);
                    input.WriteLine("SHUTDOWN\t" + requestId);
                    input.Flush();
                    process.WaitForExit(ShutdownTimeoutMs);
                }
            }
            catch
            {
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(ShutdownTimeoutMs);
                }
            }
            catch
            {
            }

            try { process.CancelErrorRead(); } catch { }
            try { input?.Dispose(); } catch { }
            try { output?.Dispose(); } catch { }
            try { process.Dispose(); } catch { }
        }

        private static bool ValidateDefinition(OnnxWorkerModelDefinition definition, out string error)
        {
            error = null;
            if (definition == null || string.IsNullOrWhiteSpace(definition.Key))
            {
                error = "ONNX worker model key is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.ModelPath) || !File.Exists(definition.ModelPath))
            {
                error = "ONNX model not found: " + (definition.ModelPath ?? string.Empty);
                return false;
            }

            if (definition.InputWidth <= 0 || definition.InputHeight <= 0 ||
                Math.Abs(definition.NormalizeStd) < 1e-12)
            {
                error = "ONNX preprocessing parameters are invalid.";
                return false;
            }

            return true;
        }

        private static OnnxWorkerModelDefinition Clone(OnnxWorkerModelDefinition source)
        {
            return new OnnxWorkerModelDefinition
            {
                Key = source.Key,
                ModelPath = source.ModelPath,
                InputWidth = source.InputWidth,
                InputHeight = source.InputHeight,
                Grayscale = source.Grayscale,
                NormalizeMean = source.NormalizeMean,
                NormalizeStd = source.NormalizeStd
            };
        }

        private static string ResolveWorkerPath()
        {
            string assemblyDirectory = null;
            try
            {
                assemblyDirectory = Path.GetDirectoryName(
                    typeof(OnnxInferenceWorkerClient).Assembly.Location);
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                assemblyDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            return Path.Combine(
                assemblyDirectory,
                "AiRuntime",
                "QtisVisionPanel.OnnxWorker.exe");
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
            }
            catch
            {
                return value ?? string.Empty;
            }
        }

        private static void OnWorkerErrorDataReceived(object sender, DataReceivedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Logger.Warn("VISION_ML_WORKER_STDERR|{0}", args.Data);
            }
        }
    }
}
