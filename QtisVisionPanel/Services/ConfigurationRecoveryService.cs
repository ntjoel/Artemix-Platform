using Newtonsoft.Json;
using NLog;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Database;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Handles backup/restore of XML-based runtime files through MySQL snapshots.
    ///
    /// The service is intentionally scope-based so the same infrastructure can
    /// protect the main application config, recipe XML files and preference files
    /// without coupling the logic to a single file format.
    /// </summary>
    public class ConfigurationRecoveryService
    {
        public const string AppConfigScope = "app_config";
        public const string RecipeConfigScope = "recipe_config";
        public const string PreferenceConfigScope = "preference_config";

        private static readonly SemaphoreSlim BootstrapGate = new SemaphoreSlim(1, 1);
        private const string BootstrapFilePath = @"C:\QtisVision\cfg\ConfigRecovery.bootstrap.json";

        private readonly ConfigSnapshotRepository _repository = new ConfigSnapshotRepository();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Persists a serialized XML payload into MySQL snapshot storage.
        /// </summary>
        public async Task BackupSerializedAsync(string scope, string filePath, string serializedContent, MySqlConnectionSettings explicitSettings = null, bool updateBootstrap = false)
        {
            var settings = ResolveSettings(explicitSettings);
            if (settings == null || string.IsNullOrWhiteSpace(serializedContent))
            {
                return;
            }

            if (updateBootstrap)
            {
                await SaveBootstrapAsync(settings).ConfigureAwait(false);
            }

            try
            {
                await _repository.SaveSnapshotAsync(scope, filePath, serializedContent, settings).ConfigureAwait(false);
                _logger.Debug($"CONFIG_RECOVERY_BACKUP_OK|scope={scope}|file={filePath}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"CONFIG_RECOVERY_BACKUP_FAILED|scope={scope}|file={filePath}|error={ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to restore the latest known payload for a given scope/path pair.
        /// Returns null when no snapshot is available.
        /// </summary>
        public async Task<string> TryRecoverSerializedAsync(string scope, string filePath, MySqlConnectionSettings explicitSettings = null)
        {
            var settings = ResolveSettings(explicitSettings);
            if (settings == null)
            {
                return null;
            }

            try
            {
                var payload = await _repository.TryGetSnapshotAsync(scope, filePath, settings).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    _logger.Info($"CONFIG_RECOVERY_RESTORE_OK|scope={scope}|file={filePath}");
                }

                return payload;
            }
            catch (Exception ex)
            {
                _logger.Warn($"CONFIG_RECOVERY_RESTORE_FAILED|scope={scope}|file={filePath}|error={ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves a minimal connection bootstrap file locally so recovery can still
        /// access MySQL even if the main XML configuration becomes unreadable.
        /// </summary>
        public async Task SaveBootstrapAsync(MySqlConnectionSettings settings)
        {
            if (!HasRequiredConnectionSettings(settings))
            {
                return;
            }

            await BootstrapGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var directory = Path.GetDirectoryName(BootstrapFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                await Task.Run(() => File.WriteAllText(BootstrapFilePath, json)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Debug($"CONFIG_RECOVERY_BOOTSTRAP_SAVE_FAILED|error={ex.Message}");
            }
            finally
            {
                BootstrapGate.Release();
            }
        }

        private MySqlConnectionSettings ResolveSettings(MySqlConnectionSettings explicitSettings)
        {
            if (HasRequiredConnectionSettings(explicitSettings))
            {
                return Clone(explicitSettings);
            }

            var runtimeSettings = MainWindow.configManager?.Config?.MySqlConnection;
            if (HasRequiredConnectionSettings(runtimeSettings))
            {
                return Clone(runtimeSettings);
            }

            return LoadBootstrapSettings();
        }

        private MySqlConnectionSettings LoadBootstrapSettings()
        {
            try
            {
                if (!File.Exists(BootstrapFilePath))
                {
                    return null;
                }

                var json = File.ReadAllText(BootstrapFilePath);
                var settings = JsonConvert.DeserializeObject<MySqlConnectionSettings>(json);
                return HasRequiredConnectionSettings(settings) ? settings : null;
            }
            catch (Exception ex)
            {
                _logger.Debug($"CONFIG_RECOVERY_BOOTSTRAP_LOAD_FAILED|error={ex.Message}");
                return null;
            }
        }

        private static bool HasRequiredConnectionSettings(MySqlConnectionSettings settings)
        {
            return settings != null &&
                   !string.IsNullOrWhiteSpace(settings.Host) &&
                   !string.IsNullOrWhiteSpace(settings.Db) &&
                   !string.IsNullOrWhiteSpace(settings.User) &&
                   !string.IsNullOrWhiteSpace(settings.port);
        }

        private static MySqlConnectionSettings Clone(MySqlConnectionSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            return new MySqlConnectionSettings
            {
                Host = settings.Host,
                User = settings.User,
                Password = settings.Password,
                Db = settings.Db,
                port = settings.port,
                AuthPlugin = settings.AuthPlugin,
                SslDisabled = settings.SslDisabled
            };
        }
    }
}
