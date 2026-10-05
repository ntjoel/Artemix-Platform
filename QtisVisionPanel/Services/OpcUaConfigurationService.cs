using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Database;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Loads and saves the dedicated OPC UA configuration.
    ///
    /// The OPC UA config intentionally lives beside the main Config.xml but is
    /// not merged into it. This keeps machine wiring/runtime configuration
    /// separate from MES/SCADA communication setup while still allowing the DB
    /// table to parameterize endpoint, security and tag nodes.
    /// </summary>
    public sealed class OpcUaConfigurationService
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly OpcUaConfigurationRepository _repository = new OpcUaConfigurationRepository();

        public string ConfigPath
        {
            get
            {
                var mainConfigPath = MainWindow.configManager?.ConfigFilePath;
                var configDirectory = !string.IsNullOrWhiteSpace(mainConfigPath)
                    ? Path.GetDirectoryName(mainConfigPath)
                    : null;

                if (string.IsNullOrWhiteSpace(configDirectory))
                {
                    // Fallback only for early startup/recovery: normal runtime
                    // follows the directory of the loaded main Config.xml.
                    configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "QtisVision", "cfg");
                }

                return Path.Combine(configDirectory, "OpcUaConfig.xml");
            }
        }

        public async Task<OpcUaConfig> LoadAsync()
        {
            var config = await LoadOrCreateXmlAsync().ConfigureAwait(false);

            try
            {
                // DB values can override or enrich XML values when enabled, but
                // the XML remains the local bootstrap file for offline startup.
                await _repository.EnsureTableAndSeedAsync(config).ConfigureAwait(false);
                if (config.UseDatabaseConfiguration)
                {
                    config = await _repository.LoadMergedAsync(config).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"OPC UA DB configuration unavailable; XML configuration will be used. Error: {ex.Message}");
            }

            return config;
        }

        public async Task SaveAsync(OpcUaConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Nodes == null || config.Nodes.Count == 0)
            {
                config.Nodes = OpcUaConfig.CreateDefaultNodes();
            }
            else
            {
                MergeMissingDefaultNodes(config);
            }

            await SaveXmlAsync(config, ConfigPath).ConfigureAwait(false);
            await _repository.SaveAsync(config).ConfigureAwait(false);
        }

        private async Task<OpcUaConfig> LoadOrCreateXmlAsync()
        {
            var path = ConfigPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path))
            {
                var defaultConfig = new OpcUaConfig();
                await SaveXmlAsync(defaultConfig, path).ConfigureAwait(false);
                return defaultConfig;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var serializer = new XmlSerializer(typeof(OpcUaConfig));
                    var config = serializer.Deserialize(stream) as OpcUaConfig;
                    if (config == null)
                    {
                        config = new OpcUaConfig();
                    }

                    if (config.Nodes == null || config.Nodes.Count == 0)
                    {
                        config.Nodes = OpcUaConfig.CreateDefaultNodes();
                    }
                    else
                    {
                        MergeMissingDefaultNodes(config);
                    }

                    return config;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"OPC UA config load failed from {path}: {ex.Message}. Default file will be recreated.");
                var defaultConfig = new OpcUaConfig();
                await SaveXmlAsync(defaultConfig, path).ConfigureAwait(false);
                return defaultConfig;
            }
        }

        private static void MergeMissingDefaultNodes(OpcUaConfig config)
        {
            // Keep old configuration files compatible when new standard tags
            // are added by later software releases.
            foreach (var defaultNode in OpcUaConfig.CreateDefaultNodes())
            {
                var exists = config.Nodes.Exists(node =>
                    string.Equals(node.Key, defaultNode.Key, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    config.Nodes.Add(defaultNode);
                }
            }
        }

        private static async Task SaveXmlAsync(OpcUaConfig config, string path)
        {
            var serializer = new XmlSerializer(typeof(OpcUaConfig));
            string xml;
            using (var writer = new Utf8StringWriter())
            {
                serializer.Serialize(writer, config);
                xml = writer.ToString();
            }

            await WriteTextAtomicallyAsync(path, xml).ConfigureAwait(false);
        }

        private static async Task WriteTextAtomicallyAsync(string path, string text)
        {
            var tempPath = path + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(text);
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Utf8NoBom;
        }
    }
}
