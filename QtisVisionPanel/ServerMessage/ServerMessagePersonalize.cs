using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QtisVisionPanel.ServerMessage
{
    public static class ServerMessagePersonalize
    {
        private static string _fileMessagesPath = @"C:\QtisVision\Language";
        private static  ServerMessageConfig _currentMessages;
        public static event EventHandler MessagesUpdated;

        public static ServerMessageConfig CurrentMessages
        {
            get
            {
                if (_currentMessages == null)
                {
                    LoadMessages();
                }
                return _currentMessages;
            }
        }
        public static void LoadMessages()
        {
            try
            {
                string language = MainWindow.configManager.Config.Configuration.Language;
                string messageFile = FindMessageFile(language);

                if (!string.IsNullOrEmpty(messageFile) && File.Exists(messageFile))
                {
                    string jsonContent = File.ReadAllText(messageFile);
                    _currentMessages = DeserializeWithBundledFallback(jsonContent, language);
                }
                else
                {
                    // Fallback ai valori di default
                    _currentMessages = new ServerMessageConfig();
                    MainWindow.logger.Warn($"Language file not found for {language}, using default messages");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error loading messages: {ex.Message} \n {ex.StackTrace}");
                _currentMessages = new ServerMessageConfig(); // Fallback
            }
        }

        /// <summary>
        /// Conserva i testi runtime personalizzati e completa soltanto le chiavi mancanti
        /// usando il catalogo distribuito accanto all'applicazione. L'updater puo' quindi
        /// aggiungere nuove label senza modificare C:\QtisVision\Language.
        /// </summary>
        private static ServerMessageConfig DeserializeWithBundledFallback(
            string runtimeJson,
            string language)
        {
            JObject runtimeDocument = JObject.Parse(runtimeJson);
            JObject runtimeMessages = runtimeDocument["messages"] as JObject;
            if (runtimeMessages == null)
            {
                runtimeMessages = new JObject();
                runtimeDocument["messages"] = runtimeMessages;
            }

            string normalizedLanguage = string.IsNullOrWhiteSpace(language)
                ? "eng"
                : language.Trim().ToLowerInvariant();
            string bundledPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Localization",
                $"messages_{normalizedLanguage}.json");

            if (File.Exists(bundledPath))
            {
                JObject bundledDocument = JObject.Parse(File.ReadAllText(bundledPath));
                JObject bundledMessages = bundledDocument["messages"] as JObject;
                if (bundledMessages != null)
                {
                    foreach (JProperty property in bundledMessages.Properties())
                    {
                        if (runtimeMessages.Property(property.Name, StringComparison.OrdinalIgnoreCase) == null)
                        {
                            runtimeMessages[property.Name] = property.Value.DeepClone();
                        }
                    }
                }
            }

            return runtimeDocument.ToObject<ServerMessageConfig>() ?? new ServerMessageConfig();
        }

        private static string FindMessageFile(string language)
        {
            foreach (string txtName in Directory.GetFiles(_fileMessagesPath, "*.json"))
            {
                try
                {
                    string jsonContent = File.ReadAllText(txtName);
                    var config = JsonConvert.DeserializeObject<ServerMessageConfig>(jsonContent);

                    // Confronto case-insensitive (ignora maiuscole/minuscole)
                    if (config?.key != null &&
                        string.Equals(config.key, language, StringComparison.OrdinalIgnoreCase))
                    {
                        return txtName;
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.logger.Error($"Error reading language file {txtName}: {ex.Message}");
                }
            }
            return null;
        }

        // Metodo per aggiornare i messaggi in runtime
        public static async Task UpdateMessagesAsync()
        {
            await Task.Run(() => LoadMessages());

            await MainWindow.MainView.Dispatcher.InvokeAsync(() =>
            {
                MessagesUpdated?.Invoke(null, EventArgs.Empty);
                // Notifica tutte le view che i messaggi sono stati aggiornati
                // Puoi implementare un evento qui se necessario
                RefreshAllUI();
                // Aggiorna il titolo della finestra principale
                if (MainWindow.MainView != null)
                {
                    MainWindow.MainView.Title = CurrentMessages.messages.lbVisionSystem ;
                }
            });
            MainWindow.logger?.Info($"Messaggi aggiornati per lingua: {CurrentMessages.key}");
        }

        private static void RefreshAllUI()
        {
            // Aggiorna tutte le UI che utilizzano i messaggi
            // Puoi implementare un sistema di eventi o chiamate dirette
        }

        // Metodo helper per ottenere un messaggio specifico
        public static string GetMessage(string messageKey)
        {
            var property = CurrentMessages.messages.GetType().GetProperty(messageKey);
            var typedValue = property?.GetValue(CurrentMessages.messages)?.ToString();
            if (!string.IsNullOrWhiteSpace(typedValue))
            {
                return typedValue;
            }

            return CurrentMessages.messages.GetExtendedMessage(messageKey) ?? messageKey;
        }

        public static string GetMessageOrDefault(string messageKey, string fallback)
        {
            string value = GetMessage(messageKey);
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, messageKey, StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            return value;
        }
    }
}
