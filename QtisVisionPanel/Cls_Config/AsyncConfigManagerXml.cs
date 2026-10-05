using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Services;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace QtisVisionPanel.Cls_Config
{
    /// <summary>
    /// Asynchronous loader/saver for the main application configuration file.
    ///
    /// Responsibilities:
    /// - load and normalize Config.xml
    /// - provide atomic save operations
    /// - trigger MySQL-backed snapshot backup and recovery
    /// - inject safe runtime defaults when legacy fields are missing
    /// </summary>
    public class AsyncConfigManagerXml
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Regex Utf16DeclarationRegex = new Regex("encoding\\s*=\\s*[\"']utf-16(?:le|be)?[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _configFilePath;
        // Fase 0 stabilita': timeout sull'acquisizione dei lock config. Un lock mai rilasciato
        // (bug o hang I/O) non deve bloccare per sempre load/save config in silenzio: dopo il
        // timeout l'operazione fallisce in modo VISIBILE (log + eccezione) invece di appendersi.
        private static readonly TimeSpan LockAcquireTimeout = TimeSpan.FromSeconds(30);
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly ConfigurationRecoveryService _recoveryService = new ConfigurationRecoveryService();

        private AppConfig _config;
        private bool _isLoaded;
        private static readonly string[] DefaultRoles = { "Administrator", "Installer", "Expert", "Operator", "Viewer" };
        private static readonly string[] DefaultEjectionStatuses = { "OK", "Rejected", "Pending" };

        /// <summary>
        /// Creates the manager for the supplied runtime configuration path.
        /// </summary>
        public AsyncConfigManagerXml(string configFilePath)
        {
            _configFilePath = configFilePath;
        }

        /// <summary>
        /// True once a usable in-memory configuration has been loaded or created.
        /// </summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// Absolute path of the main machine configuration file.
        /// Companion configuration files can use the same directory without
        /// adding unrelated sections to Config.xml.
        /// </summary>
        public string ConfigFilePath => _configFilePath;

        /// <summary>
        /// Loads the configuration once, normalizes it and prepares snapshot recovery.
        /// Safe to call repeatedly.
        /// </summary>
        public async Task EnsureLoadedAsync()
        {
            if (_isLoaded)
            {
                return;
            }

            if (!await _loadLock.WaitAsync(LockAcquireTimeout).ConfigureAwait(false))
            {
                MainWindow.logger?.Error("CONFIG_LOCK_TIMEOUT|operation=EnsureLoadedAsync|timeout_s=30");
                throw new TimeoutException("Timeout acquisizione lock caricamento configurazione (EnsureLoadedAsync).");
            }
            try
            {
                if (_isLoaded)
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
                        MainWindow.logger?.Warn($"Errore lettura Config.xml, tentativo recovery MySQL: {ex.Message}");
                        _config = await TryRestoreFromRecoveryAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    _config = await TryRestoreFromRecoveryAsync().ConfigureAwait(false);
                }

                if (_config == null)
                {
                    _config = new AppConfig();
                }

                if (_config.Roles == null || _config.Roles.Count == 0)
                {
                    MainWindow.logger?.Warn("Config.xml loaded without Roles entries. Applying default runtime roles.");
                }

                if (_config.Ejection_status == null || _config.Ejection_status.Count == 0)
                {
                    MainWindow.logger?.Warn("Config.xml loaded without Ejection_status entries. Applying default runtime statuses.");
                }

                _config = NormalizeConfig(_config);

                _isLoaded = true;

                if (_config?.MySqlConnection != null)
                {
                    _ = Task.Run(() => _recoveryService.SaveBootstrapAsync(_config.MySqlConnection));

                    var serialized = SerializeConfig(_config);
                    _ = Task.Run(() => _recoveryService.BackupSerializedAsync(
                        ConfigurationRecoveryService.AppConfigScope,
                        _configFilePath,
                        serialized,
                        _config.MySqlConnection,
                        updateBootstrap: true));
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento della configurazione: {ex.Message}", ex);
                _config = new AppConfig();
                _isLoaded = true;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// Current normalized in-memory configuration.
        /// </summary>
        public AppConfig Config
        {
            get
            {
                if (!_isLoaded || _config == null)
                {
                    return null;
                }

                return _config;
            }
        }

        /// <summary>
        /// Saves the current configuration atomically to disk and asynchronously
        /// backs it up to the MySQL recovery store.
        /// </summary>
        public async Task SaveConfigAsync()
        {
            if (!await _saveLock.WaitAsync(LockAcquireTimeout).ConfigureAwait(false))
            {
                MainWindow.logger?.Error("CONFIG_LOCK_TIMEOUT|operation=SaveConfigAsync|timeout_s=30");
                throw new TimeoutException("Timeout acquisizione lock salvataggio configurazione (SaveConfigAsync).");
            }
            try
            {
                if (_config == null || string.IsNullOrWhiteSpace(_configFilePath))
                {
                    return;
                }

                // Versioned filesystem backup before overwriting (30 snapshots, then oldest pruned)
                if (File.Exists(_configFilePath))
                {
                    try
                    {
                        var configDir = Path.GetDirectoryName(_configFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                        var backupDir = Path.Combine(configDir, "cfg_backups");
                        Directory.CreateDirectory(backupDir);
                        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        File.Copy(_configFilePath, Path.Combine(backupDir, $"Config_{stamp}.xml"), overwrite: true);

                        foreach (var old in Directory.GetFiles(backupDir, "Config_*.xml")
                                                     .OrderByDescending(f => f)
                                                     .Skip(30))
                        {
                            try { File.Delete(old); } catch { /* best-effort */ }
                        }
                    }
                    catch (Exception ex)
                    {
                        MainWindow.logger?.Warn($"CONFIG_BACKUP_FAILED|{ex.Message}");
                    }
                }

                var serialized = SerializeConfig(_config);
                await WriteTextAtomicallyInternalAsync(_configFilePath, serialized).ConfigureAwait(false);

                _ = Task.Run(() => _recoveryService.BackupSerializedAsync(
                    ConfigurationRecoveryService.AppConfigScope,
                    _configFilePath,
                    serialized,
                    _config.MySqlConnection,
                    updateBootstrap: true));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel salvataggio della configurazione: {ex.Message}", ex);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// Replaces the MySQL section and persists the config immediately.
        /// </summary>
        public async Task UpdateMySqlConnectionAsync(MySqlConnectionSettings newConnection)
        {
            _config.MySqlConnection = newConnection;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Replaces the general runtime section and persists the config immediately.
        /// </summary>
        public async Task UpdateConfigurationAsync(Configuration newConfig)
        {
            _config.Configuration = newConfig;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads Config.xml from disk using shared-read access so machine-side
        /// monitoring or backup tools do not block the HMI.
        /// </summary>
        private async Task<AppConfig> LoadFromDiskAsync()
        {
            var ext = Path.GetExtension(_configFilePath).ToLowerInvariant();
            if (ext != ".xml")
            {
                throw new NotSupportedException("Formato di configurazione non supportato.");
            }

            try
            {
                using (var stream = new FileStream(_configFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var serializer = new XmlSerializer(typeof(AppConfig));
                    return await Task.Run(() => (AppConfig)serializer.Deserialize(stream)).ConfigureAwait(false);
                }
            }
            catch (Exception directReadException) when (CanAttemptTextRecovery(directReadException))
            {
                var xmlText = await ReadXmlTextWithFallbackAsync(_configFilePath).ConfigureAwait(false);
                var config = DeserializeConfig(xmlText);
                await TryRewriteCanonicalXmlAsync(_configFilePath, SerializeConfig(config), "Config.xml").ConfigureAwait(false);
                return config;
            }
        }

        /// <summary>
        /// Restores Config.xml from the MySQL snapshot store and writes it back
        /// locally so the runtime can continue to use the usual file-based flow.
        /// </summary>
        private async Task<AppConfig> TryRestoreFromRecoveryAsync()
        {
            var payload = await _recoveryService.TryRecoverSerializedAsync(
                ConfigurationRecoveryService.AppConfigScope,
                _configFilePath).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var restoredConfig = DeserializeConfig(payload);
            var canonicalPayload = SerializeConfig(restoredConfig);

            if (!await _saveLock.WaitAsync(LockAcquireTimeout).ConfigureAwait(false))
            {
                MainWindow.logger?.Error("CONFIG_LOCK_TIMEOUT|operation=RestoreFromRecovery|timeout_s=30");
                throw new TimeoutException("Timeout acquisizione lock scrittura configurazione (recovery).");
            }
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

        private static AppConfig DeserializeConfig(string xml)
        {
            var serializer = new XmlSerializer(typeof(AppConfig));
            using (var reader = new StringReader(xml))
            {
                return NormalizeConfig((AppConfig)serializer.Deserialize(reader));
            }
        }

        private static string SerializeConfig(AppConfig config)
        {
            config = NormalizeConfig(config);
            var serializer = new XmlSerializer(typeof(AppConfig));
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

        /// <summary>
        /// Fills legacy or missing sections with runtime-safe defaults.
        /// This method is intentionally centralized so every load/save path
        /// applies the same normalization rules.
        /// </summary>
        private static AppConfig NormalizeConfig(AppConfig config)
        {
            config = config ?? new AppConfig();
            config.MySqlConnection = config.MySqlConnection ?? new MySqlConnectionSettings();
            config.Configuration = config.Configuration ?? new Configuration();
            config.Machine = config.Machine ?? new Machine();
            config.IO = config.IO ?? new IO();
            config.LasRunParam = config.LasRunParam ?? new LasRunParam();
            config._SaveImagePercentage = config._SaveImagePercentage ?? new SaveImagePercentage();
            config.AutoSwitchSettings = config.AutoSwitchSettings ?? new AutoSwitchSettings();
            config.SystemDiagnostics = config.SystemDiagnostics ?? new SystemDiagnosticsSettings();
            config.PowerFlex525 = config.PowerFlex525 ?? new PowerFlex525Settings();
            config.AnalyticsDashboard = config.AnalyticsDashboard ?? new AnalyticsDashboardSettings();
            config.AnalyticsDashboard.Cards = config.AnalyticsDashboard.Cards ?? new System.Collections.Generic.List<AnalyticsCardConfig>();

            if (config.AnalyticsDashboard.Cards.Count == 0)
            {
                config.AnalyticsDashboard.Cards.AddRange(new[]
                {
                    new AnalyticsCardConfig { CardType = "ProductionOverview", IsVisible = true },
                    new AnalyticsCardConfig { CardType = "DefectPie", IsVisible = true },
                    new AnalyticsCardConfig { CardType = "HeightTrend", IsVisible = true },
                    new AnalyticsCardConfig { CardType = "ThreeDHeightTrend", IsVisible = true },
                    new AnalyticsCardConfig { CardType = "WidthTrend", IsVisible = true },
                    new AnalyticsCardConfig { CardType = "LengthTrend", IsVisible = true }
                });
            }

            if (config.Roles == null)
            {
                config.Roles = new System.Collections.Generic.List<string>();
            }

            if (config.Roles.Count == 0)
            {
                config.Roles.AddRange(DefaultRoles);
            }

            if (config.Ejection_status == null)
            {
                config.Ejection_status = new System.Collections.Generic.List<string>();
            }

            if (config.Ejection_status.Count == 0)
            {
                config.Ejection_status.AddRange(DefaultEjectionStatuses);
            }

            if (string.IsNullOrWhiteSpace(config.Configuration.User))
            {
                config.Configuration.User = "Guest";
            }

            if (string.IsNullOrWhiteSpace(config.Configuration.CurrentUserRole))
            {
                config.Configuration.CurrentUserRole = "Viewer";
            }

            if (config.Machine.machine_type == null)
            {
                config.Machine.machine_type = new System.Collections.Generic.List<string>();
            }

            return config;
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

                throw new IOException($"Impossibile salvare il file di configurazione '{filePath}' dopo multipli tentativi.", lastException);
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
                    throw new InvalidDataException($"Il file XML '{filePath}' risulta vuoto.");
                }

                if (!xml.TrimStart().StartsWith("<", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Il file XML '{filePath}' non contiene un documento XML valido.");
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

            // Auto-heal best-effort: su timeout del lock si rinuncia senza propagare errori.
            if (!await _saveLock.WaitAsync(LockAcquireTimeout).ConfigureAwait(false))
            {
                MainWindow.logger?.Warn($"CONFIG_LOCK_TIMEOUT|operation=AutoHeal_{fileLabel}|timeout_s=30 — auto-heal saltato");
                return;
            }
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
