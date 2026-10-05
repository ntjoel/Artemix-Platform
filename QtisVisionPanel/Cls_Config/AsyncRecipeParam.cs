using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Services;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace QtisVisionPanel.Cls_Config
{
    /// <summary>
    /// Asynchronous manager for the recipe XML paired with the active VPP file.
    ///
    /// Responsibilities:
    /// - load the recipe XML on demand
    /// - provide atomic save/update operations
    /// - trigger MySQL-backed recovery snapshots for recipe data
    /// - expose strongly typed access to recipe parameters used at runtime
    /// </summary>
    public class AsyncRecipeParam
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Regex Utf16DeclarationRegex = new Regex("encoding\\s*=\\s*[\"']utf-16(?:le|be)?[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _configFilePath;
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly ConfigurationRecoveryService _recoveryService = new ConfigurationRecoveryService();

        private RecipeParameters.RecipeData _config;
        private bool _isLoaded;

        /// <summary>
        /// Creates the manager for the supplied recipe XML path.
        /// </summary>
        public AsyncRecipeParam(string configFilePath)
        {
            _configFilePath = configFilePath;
        }

        /// <summary>
        /// True once the recipe has been loaded successfully.
        /// </summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// Loads the recipe XML once. If the file is unreadable or missing,
        /// the service tries the MySQL recovery snapshot before failing.
        /// </summary>
        public async Task EnsureLoadedAsync()
        {
            if (_isLoaded)
            {
                return;
            }

            await _loadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isLoaded || MainWindow.IsShuttingDown)
                {
                    return;
                }

                if (File.Exists(_configFilePath))
                {
                    try
                    {
                        _config = await LoadFromDiskAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        MainWindow.logger?.Warn($"Errore lettura recipe XML, tentativo recovery MySQL: {ex.Message}");
                        _config = await TryRestoreFromRecoveryAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    MainWindow.logger?.Warn("Recipe parameters file not found. Attempting MySQL recovery.");
                    _config = await TryRestoreFromRecoveryAsync().ConfigureAwait(false);
                    if (_config == null)
                    {
                        _config = new RecipeParameters.RecipeData();
                    }
                }

                if (_config == null)
                {
                    throw new InvalidOperationException("Recipe configuration is not available.");
                }

                _isLoaded = true;

                var serialized = SerializeConfig(_config);
                _ = Task.Run(() => _recoveryService.BackupSerializedAsync(
                    ConfigurationRecoveryService.RecipeConfigScope,
                    _configFilePath,
                    serialized));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error loading recipe parameters: {ex.Message}", ex);
                throw new Exception($"Error loading recipe parameters: {ex.Message}", ex);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// Current in-memory recipe object.
        /// </summary>
        public RecipeParameters.RecipeData Config
        {
            get
            {
                if (_config == null)
                {
                    MainWindow.logger?.Error("Configuration is not loaded yet. Please call EnsureLoadedAsync() first.");
                }

                return _config;
            }
        }

        /// <summary>
        /// Saves the current recipe atomically and mirrors it to the recovery store.
        /// </summary>
        public async Task SaveConfigAsync()
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (MainWindow.IsShuttingDown || _config == null || string.IsNullOrWhiteSpace(_configFilePath))
                {
                    return;
                }

                var serialized = SerializeConfig(_config);
                await WriteTextAtomicallyInternalAsync(_configFilePath, serialized).ConfigureAwait(false);

                _ = Task.Run(() => _recoveryService.BackupSerializedAsync(
                    ConfigurationRecoveryService.RecipeConfigScope,
                    _configFilePath,
                    serialized));
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error saving configuration: {ex.Message}", ex);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// Updates the top-camera recipe parameters and persists the recipe.
        /// </summary>
        public async Task UpdateRecipeParamTopAsync(RecipeParameters.RecipeParamTop newConfig)
        {
            _config.recipeParamTop = newConfig;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the side-camera recipe parameters and persists the recipe.
        /// </summary>
        public async Task UpdateRecipeParamSideAsync(RecipeParameters.RecipeParamSide newConfig)
        {
            _config.recipeParamSide = newConfig;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the Top3D recipe parameters (quote 3D + detection sensitivity) and persists the recipe.
        /// </summary>
        public async Task UpdateRecipeParamTop3DAsync(RecipeParameters.RecipeParamTop3D newConfig)
        {
            _config.recipeParamTop3D = newConfig;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Updates recipe-linked counters and persists the recipe.
        /// </summary>
        public async Task UpdateCounterAsync(RecipeParameters.Counter newConfig)
        {
            _config.Counter = newConfig;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Helper used by UI/runtime when only the counter section is needed.
        /// </summary>
        public async Task<RecipeParameters.Counter> GetCurrentCountersAsync()
        {
            try
            {
                await EnsureLoadedAsync().ConfigureAwait(false);
                return _config.Counter ?? new RecipeParameters.Counter();
            }
            catch
            {
                return new RecipeParameters.Counter();
            }
        }

        private async Task<RecipeParameters.RecipeData> LoadFromDiskAsync()
        {
            var ext = Path.GetExtension(_configFilePath).ToLowerInvariant();
            if (ext != ".xml")
            {
                throw new NotSupportedException("Unsupported configuration format.");
            }

            try
            {
                using (var stream = new FileStream(_configFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var serializer = new XmlSerializer(typeof(RecipeParameters.RecipeData));
                    return await Task.Run(() => (RecipeParameters.RecipeData)serializer.Deserialize(stream)).ConfigureAwait(false);
                }
            }
            catch (Exception directReadException) when (CanAttemptTextRecovery(directReadException))
            {
                var xmlText = await ReadXmlTextWithFallbackAsync(_configFilePath).ConfigureAwait(false);
                var config = DeserializeConfig(xmlText);
                await TryRewriteCanonicalXmlAsync(_configFilePath, SerializeConfig(config), "RecipeXml").ConfigureAwait(false);
                return config;
            }
        }

        /// <summary>
        /// Restores a recipe XML from MySQL snapshots and writes it back locally.
        /// </summary>
        private async Task<RecipeParameters.RecipeData> TryRestoreFromRecoveryAsync()
        {
            var payload = await _recoveryService.TryRecoverSerializedAsync(
                ConfigurationRecoveryService.RecipeConfigScope,
                _configFilePath).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var restoredConfig = DeserializeConfig(payload);
            var canonicalPayload = SerializeConfig(restoredConfig);

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await WriteTextAtomicallyInternalAsync(_configFilePath, canonicalPayload).ConfigureAwait(false);
            }
            finally
            {
                _saveLock.Release();
            }

            return restoredConfig;
        }

        private static RecipeParameters.RecipeData DeserializeConfig(string xml)
        {
            var serializer = new XmlSerializer(typeof(RecipeParameters.RecipeData));
            using (var reader = new StringReader(xml))
            {
                return (RecipeParameters.RecipeData)serializer.Deserialize(reader);
            }
        }

        private static string SerializeConfig(RecipeParameters.RecipeData config)
        {
            var serializer = new XmlSerializer(typeof(RecipeParameters.RecipeData));
            var settings = new XmlWriterSettings
            {
                Encoding = Utf8NoBom,
                Indent = true,
                OmitXmlDeclaration = false
            };

            using (var memoryStream = new MemoryStream())
            {
                using (var xmlWriter = XmlWriter.Create(memoryStream, settings))
                {
                    serializer.Serialize(xmlWriter, config);
                }

                return Utf8NoBom.GetString(memoryStream.ToArray());
            }
        }

        private static async Task WriteTextAtomicallyInternalAsync(string filePath, string content)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempDirectory = string.IsNullOrWhiteSpace(directory) ? AppDomain.CurrentDomain.BaseDirectory : directory;
            var tempFilePath = Path.Combine(
                tempDirectory,
                $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await Task.Run(() =>
                {
                    using (var stream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, Utf8NoBom))
                    {
                        writer.Write(content);
                    }
                }).ConfigureAwait(false);

                Exception lastException = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Replace(tempFilePath, filePath, null, true);
                        }
                        else
                        {
                            File.Move(tempFilePath, filePath);
                        }

                        tempFilePath = null;
                        return;
                    }
                    catch (IOException ex)
                    {
                        lastException = ex;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        lastException = ex;
                    }

                    await Task.Delay(75 * (attempt + 1)).ConfigureAwait(false);
                }

                throw new IOException($"Impossibile salvare il file ricetta '{filePath}' dopo multipli tentativi.", lastException);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempFilePath) && File.Exists(tempFilePath))
                {
                    try
                    {
                        File.Delete(tempFilePath);
                    }
                    catch
                    {
                        // Ignora cleanup best-effort del file temporaneo.
                    }
                }
            }
        }

        private static bool CanAttemptTextRecovery(Exception exception)
        {
            return exception is InvalidOperationException ||
                   exception is IOException ||
                   exception is XmlException;
        }

        private static async Task<string> ReadXmlTextWithFallbackAsync(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var xml = await reader.ReadToEndAsync().ConfigureAwait(false);
                xml = NormalizeXmlText(xml);

                if (string.IsNullOrWhiteSpace(xml))
                {
                    throw new InvalidDataException($"The recipe XML file '{filePath}' is empty.");
                }

                if (!xml.TrimStart().StartsWith("<", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"The recipe XML file '{filePath}' does not contain valid XML content.");
                }

                return xml;
            }
        }

        private static string NormalizeXmlText(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return xml;
            }

            var normalized = xml.TrimStart('\uFEFF', '\0', ' ', '\t', '\r', '\n');
            return Utf16DeclarationRegex.Replace(normalized, "encoding=\"utf-8\"");
        }

        private async Task TryRewriteCanonicalXmlAsync(string filePath, string canonicalXml, string fileLabel)
        {
            if (string.IsNullOrWhiteSpace(canonicalXml))
            {
                return;
            }

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await WriteTextAtomicallyInternalAsync(filePath, canonicalXml).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Debug($"AUTO_HEAL_{fileLabel}_SKIPPED|file={filePath}|error={ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
