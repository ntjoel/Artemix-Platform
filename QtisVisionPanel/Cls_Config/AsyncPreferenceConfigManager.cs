using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace QtisVisionPanel.Cls_Config
{
    public class AsyncPreferenceConfigManager
    {
        private readonly string _configFilePath;
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly ConfigurationRecoveryService _recoveryService = new ConfigurationRecoveryService();

        private PreferenceViewConfig _config;
        private bool _isLoaded;

        public AsyncPreferenceConfigManager(string configFilePath = null)
        {
            _configFilePath = configFilePath ?? Path.Combine(
                @"C:\QtisVision",
                "cfg",
                "PreferenceViewConfig.xml");
        }

        public bool IsLoaded => _isLoaded;

        public async Task EnsureLoadedAsync()
        {
            if (_isLoaded)
            {
                return;
            }

            await _loadLock.WaitAsync().ConfigureAwait(false);
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
                        MainWindow.logger?.Warn($"Errore lettura PreferenceViewConfig, tentativo recovery MySQL: {ex.Message}");
                        _config = await TryRestoreFromRecoveryAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    MainWindow.logger?.Warn("File PreferenceViewConfig non trovato. Tentativo recovery MySQL.");
                    _config = await TryRestoreFromRecoveryAsync().ConfigureAwait(false);
                }

                if (_config == null)
                {
                    _config = CreateDefaultConfig();
                    await SaveConfigAsync().ConfigureAwait(false);
                }

                _isLoaded = true;
                MainWindow.logger?.Info("PreferenceViewConfig caricata correttamente.");

                var serialized = SerializeConfig(_config);
                _ = Task.Run(() => _recoveryService.BackupSerializedAsync(
                    ConfigurationRecoveryService.PreferenceConfigScope,
                    _configFilePath,
                    serialized));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore caricamento PreferenceViewConfig: {ex.Message}");
                _config = CreateDefaultConfig();
                _isLoaded = true;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private PreferenceViewConfig CreateDefaultConfig()
        {
            return new PreferenceViewConfig
            {
                LanguageSettings = new LanguageSettings
                {
                    CurrentLanguage = "eng",
                    AvailableLanguages = new System.Collections.Generic.List<LanguageInfo>
                    {
                        new LanguageInfo { Code = "eng", DisplayName = "LANG_EN", FlagImage = "english.png", ResourceFile = "messages_eng.json" },
                        new LanguageInfo { Code = "ita", DisplayName = "LANG_IT", FlagImage = "italy.png", ResourceFile = "messages_ita.json" },
                    },
                    Paths = new LanguagePaths
                    {
                        FlagImagesPath = @"C:\QtisVision\Language\img\icons\langs\",
                        LanguageFilesPath = @"C:\QtisVision\Language\"
                    }
                },
                SystemSettings = new SystemSettings
                {
                    SavePath = @"D:\QtisVision\Pieces",
                    AutoSaveEnabled = true,
                    AutoSaveIntervalMinutes = 5
                },
                UserManagementSettings = new UserManagementSettings
                {
                    DefaultRole = "Viewer",
                    AvailableRoles = new System.Collections.Generic.List<string>
                    { "Administrator", "Installer", "Expert", "Operator", "Viewer" }
                }
            };
        }

        public PreferenceViewConfig Config
        {
            get
            {
                if (!_isLoaded)
                {
                    MainWindow.logger?.Warn("PreferenceViewConfig non caricata. Chiamare EnsureLoadedAsync() prima.");
                    return null;
                }

                return _config;
            }
        }

        public async Task SaveConfigAsync()
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_config == null || string.IsNullOrWhiteSpace(_configFilePath))
                {
                    return;
                }

                var serialized = SerializeConfig(_config);
                await WriteTextAtomicallyInternalAsync(_configFilePath, serialized).ConfigureAwait(false);

                _ = Task.Run(() => _recoveryService.BackupSerializedAsync(
                    ConfigurationRecoveryService.PreferenceConfigScope,
                    _configFilePath,
                    serialized));

                MainWindow.logger?.Info("PreferenceViewConfig salvata correttamente.");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore salvataggio PreferenceViewConfig: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async Task UpdateLanguageAsync(string languageCode)
        {
            _config.LanguageSettings.CurrentLanguage = languageCode;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        public async Task UpdateSystemSettingsAsync(SystemSettings settings)
        {
            _config.SystemSettings = settings;
            await SaveConfigAsync().ConfigureAwait(false);
        }

        public async Task AddUserAsync(string username, string password, string role)
        {
            await Task.CompletedTask;
        }

        private async Task<PreferenceViewConfig> LoadFromDiskAsync()
        {
            using (var stream = new FileStream(_configFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var serializer = new XmlSerializer(typeof(PreferenceViewConfig));
                return await Task.Run(() => (PreferenceViewConfig)serializer.Deserialize(stream)).ConfigureAwait(false);
            }
        }

        private async Task<PreferenceViewConfig> TryRestoreFromRecoveryAsync()
        {
            var payload = await _recoveryService.TryRecoverSerializedAsync(
                ConfigurationRecoveryService.PreferenceConfigScope,
                _configFilePath).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await WriteTextAtomicallyInternalAsync(_configFilePath, payload).ConfigureAwait(false);
            }
            finally
            {
                _saveLock.Release();
            }

            return DeserializeConfig(payload);
        }

        private static PreferenceViewConfig DeserializeConfig(string xml)
        {
            var serializer = new XmlSerializer(typeof(PreferenceViewConfig));
            using (var reader = new StringReader(xml))
            {
                return (PreferenceViewConfig)serializer.Deserialize(reader);
            }
        }

        private static string SerializeConfig(PreferenceViewConfig config)
        {
            var serializer = new XmlSerializer(typeof(PreferenceViewConfig));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, config);
                return writer.ToString();
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
                    using (var writer = new StreamWriter(stream))
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

                throw new IOException($"Impossibile salvare il file preferenze '{filePath}' dopo multipli tentativi.", lastException);
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
    }
}
