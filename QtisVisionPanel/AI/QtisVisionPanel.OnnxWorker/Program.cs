using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace QtisVisionPanel.OnnxWorker
{
    internal static class Program
    {
        private const int MaxLoadedModels = 4;
        private static readonly Dictionary<string, ModelSession> Sessions =
            new Dictionary<string, ModelSession>(StringComparer.Ordinal);
        private static readonly Queue<string> LoadOrder = new Queue<string>();

        private static int Main()
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = new UTF8Encoding(false);

            try
            {
                string request;
                while ((request = Console.ReadLine()) != null)
                {
                    if (!HandleRequest(request))
                    {
                        break;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WORKER_FATAL|" + ex);
                return 2;
            }
            finally
            {
                DisposeAllSessions();
            }
        }

        private static bool HandleRequest(string request)
        {
            string[] parts = (request ?? string.Empty).Split('\t');
            string command = parts.Length > 0 ? parts[0] : string.Empty;
            string requestId = parts.Length > 1 ? parts[1] : "0";

            try
            {
                switch (command)
                {
                    case "PING":
                        Reply("PONG", requestId);
                        return true;

                    case "LOAD":
                        LoadModel(parts, requestId);
                        return true;

                    case "RUN":
                        RunModel(parts, requestId);
                        return true;

                    case "VALIDATE":
                        ValidateModel(parts, requestId);
                        return true;

                    case "REMOVE":
                        RemoveModel(parts, requestId);
                        return true;

                    case "SHUTDOWN":
                        Reply("BYE", requestId);
                        return false;

                    default:
                        throw new InvalidOperationException("Unknown command: " + command);
                }
            }
            catch (Exception ex)
            {
                Reply("ERROR", requestId, Encode(ex.GetType().Name + ": " + ex.Message));
                return true;
            }
        }

        private static void LoadModel(string[] parts, string requestId)
        {
            RequireParts(parts, 9);

            string key = Decode(parts[2]);
            string modelPath = Decode(parts[3]);
            int width = ParseInt(parts[4], "width", 1, 8192);
            int height = ParseInt(parts[5], "height", 1, 8192);
            bool grayscale = parts[6] == "1";
            double normalizeMean = ParseDouble(parts[7], "normalizeMean");
            double normalizeStd = ParseDouble(parts[8], "normalizeStd");

            if (Math.Abs(normalizeStd) < 1e-12)
            {
                throw new InvalidOperationException("normalizeStd cannot be zero.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Model key is empty.");
            }

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("ONNX model not found.", modelPath);
            }

            if (Sessions.TryGetValue(key, out ModelSession current) &&
                current.Matches(modelPath, width, height, grayscale, normalizeMean, normalizeStd))
            {
                Reply("READY", requestId, Encode(current.InputName));
                return;
            }

            var replacement = new ModelSession(
                modelPath,
                width,
                height,
                grayscale,
                normalizeMean,
                normalizeStd);

            if (Sessions.TryGetValue(key, out ModelSession previous))
            {
                Sessions.Remove(key);
                previous.Dispose();
            }

            Sessions[key] = replacement;
            LoadOrder.Enqueue(key);
            TrimModelCache();
            Reply("READY", requestId, Encode(replacement.InputName));
        }

        private static void RunModel(string[] parts, string requestId)
        {
            RequireParts(parts, 4);
            string key = Decode(parts[2]);
            string imagePath = Decode(parts[3]);

            if (!Sessions.TryGetValue(key, out ModelSession model))
            {
                throw new InvalidOperationException("Model is not loaded: " + key);
            }

            Prediction prediction = model.Run(imagePath);
            Reply(
                "RESULT",
                requestId,
                prediction.ClassIndex.ToString(CultureInfo.InvariantCulture),
                prediction.Confidence.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void ValidateModel(string[] parts, string requestId)
        {
            RequireParts(parts, 3);
            string modelPath = Decode(parts[2]);
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("ONNX model not found.", modelPath);
            }

            using (var options = CreateSessionOptions())
            using (var session = new InferenceSession(modelPath, options))
            {
                string inputName = session.InputMetadata.Keys.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(inputName))
                {
                    throw new InvalidOperationException("The model exposes no input.");
                }

                Reply("VALID", requestId, Encode(inputName));
            }
        }

        private static void RemoveModel(string[] parts, string requestId)
        {
            RequireParts(parts, 3);
            string key = Decode(parts[2]);
            if (Sessions.TryGetValue(key, out ModelSession model))
            {
                Sessions.Remove(key);
                model.Dispose();
            }

            Reply("REMOVED", requestId);
        }

        private static SessionOptions CreateSessionOptions()
        {
            return new SessionOptions
            {
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                EnableCpuMemArena = false,
                EnableMemoryPattern = false,
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING
            };
        }

        private static void TrimModelCache()
        {
            while (Sessions.Count > MaxLoadedModels && LoadOrder.Count > 0)
            {
                string oldest = LoadOrder.Dequeue();
                if (!Sessions.TryGetValue(oldest, out ModelSession model))
                {
                    continue;
                }

                Sessions.Remove(oldest);
                model.Dispose();
            }
        }

        private static void DisposeAllSessions()
        {
            foreach (ModelSession session in Sessions.Values)
            {
                session.Dispose();
            }

            Sessions.Clear();
            LoadOrder.Clear();
        }

        private static void RequireParts(string[] parts, int required)
        {
            if (parts == null || parts.Length < required)
            {
                throw new InvalidOperationException("Malformed worker request.");
            }
        }

        private static int ParseInt(string value, string name, int minimum, int maximum)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
                parsed < minimum || parsed > maximum)
            {
                throw new InvalidOperationException(name + " is invalid.");
            }

            return parsed;
        }

        private static double ParseDouble(string value, string name)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                throw new InvalidOperationException(name + " is invalid.");
            }

            return parsed;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
        }

        private static void Reply(string response, string requestId, params string[] values)
        {
            var line = new StringBuilder(response)
                .Append('\t')
                .Append(requestId);

            if (values != null)
            {
                foreach (string value in values)
                {
                    line.Append('\t').Append(value ?? string.Empty);
                }
            }

            Console.WriteLine(line.ToString());
            Console.Out.Flush();
        }

        private sealed class ModelSession : IDisposable
        {
            private readonly InferenceSession _session;
            private readonly string _modelPath;
            private readonly int _width;
            private readonly int _height;
            private readonly bool _grayscale;
            private readonly double _normalizeMean;
            private readonly double _normalizeStd;

            public ModelSession(
                string modelPath,
                int width,
                int height,
                bool grayscale,
                double normalizeMean,
                double normalizeStd)
            {
                _modelPath = Path.GetFullPath(modelPath);
                _width = width;
                _height = height;
                _grayscale = grayscale;
                _normalizeMean = normalizeMean;
                _normalizeStd = normalizeStd;

                using (var options = CreateSessionOptions())
                {
                    _session = new InferenceSession(_modelPath, options);
                }

                InputName = _session.InputMetadata.Keys.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(InputName))
                {
                    _session.Dispose();
                    throw new InvalidOperationException("The model exposes no input.");
                }
            }

            public string InputName { get; }

            public bool Matches(
                string modelPath,
                int width,
                int height,
                bool grayscale,
                double normalizeMean,
                double normalizeStd)
            {
                return string.Equals(_modelPath, Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase) &&
                       _width == width &&
                       _height == height &&
                       _grayscale == grayscale &&
                       Math.Abs(_normalizeMean - normalizeMean) < 1e-12 &&
                       Math.Abs(_normalizeStd - normalizeStd) < 1e-12;
            }

            public Prediction Run(string imagePath)
            {
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException("Inspection image not found.", imagePath);
                }

                DenseTensor<float> tensor = Preprocess(imagePath);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(InputName, tensor)
                };

                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs))
                {
                    float[] scores = outputs.First().AsEnumerable<float>().ToArray();
                    if (scores.Length == 0)
                    {
                        throw new InvalidOperationException("The model returned no scores.");
                    }

                    int argmax = 0;
                    for (int i = 1; i < scores.Length; i++)
                    {
                        if (scores[i] > scores[argmax])
                        {
                            argmax = i;
                        }
                    }

                    return new Prediction
                    {
                        ClassIndex = argmax,
                        Confidence = Softmax(scores, argmax)
                    };
                }
            }

            private DenseTensor<float> Preprocess(string imagePath)
            {
                int channels = _grayscale ? 1 : 3;
                var tensor = new DenseTensor<float>(new[] { 1, channels, _height, _width });
                float mean = (float)_normalizeMean;
                float inverseStd = (float)(1.0 / _normalizeStd);

                using (var source = new Bitmap(imagePath))
                using (var resized = new Bitmap(_width, _height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics graphics = Graphics.FromImage(resized))
                    {
                        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                        graphics.DrawImage(source, 0, 0, _width, _height);
                    }

                    var bounds = new Rectangle(0, 0, _width, _height);
                    BitmapData data = resized.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        unsafe
                        {
                            byte* scan0 = (byte*)data.Scan0.ToPointer();
                            for (int y = 0; y < _height; y++)
                            {
                                byte* row = scan0 + (y * data.Stride);
                                for (int x = 0; x < _width; x++)
                                {
                                    int offset = x * 3;
                                    byte blue = row[offset];
                                    byte green = row[offset + 1];
                                    byte red = row[offset + 2];

                                    if (_grayscale)
                                    {
                                        float gray = (0.299f * red) + (0.587f * green) + (0.114f * blue);
                                        tensor[0, 0, y, x] = (gray - mean) * inverseStd;
                                    }
                                    else
                                    {
                                        tensor[0, 0, y, x] = (red - mean) * inverseStd;
                                        tensor[0, 1, y, x] = (green - mean) * inverseStd;
                                        tensor[0, 2, y, x] = (blue - mean) * inverseStd;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        resized.UnlockBits(data);
                    }
                }

                return tensor;
            }

            public void Dispose()
            {
                _session.Dispose();
            }
        }

        private struct Prediction
        {
            public int ClassIndex;
            public double Confidence;
        }

        private static double Softmax(float[] scores, int index)
        {
            double max = scores.Max();
            double sum = 0.0;
            for (int i = 0; i < scores.Length; i++)
            {
                sum += Math.Exp(scores[i] - max);
            }

            return sum <= 0.0 ? 0.0 : Math.Exp(scores[index] - max) / sum;
        }
    }
}
