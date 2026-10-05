using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Json;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;

namespace QtisVisionPanel.Services
{
    public sealed class ToolLocalizationService : INotifyPropertyChanged
    {
        private const string DefaultLanguageCode = "ENG";
        private static readonly Lazy<ToolLocalizationService> LazyInstance =
            new Lazy<ToolLocalizationService>(() => new ToolLocalizationService());

        private readonly Dictionary<string, string> _messages =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _availableLanguageCodes =
            new List<string>();

        private string _languageCode = DefaultLanguageCode;
        private string _currentPackPath;

        public static ToolLocalizationService Current => LazyInstance.Value;

        private ToolLocalizationService()
        {
            LoadDefaultMessages();
            ServerMessagePersonalize.MessagesUpdated += OnServerMessagesUpdated;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string LanguageCode
        {
            get => _languageCode;
            private set
            {
                if (string.Equals(_languageCode, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _languageCode = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<string> AvailableLanguageCodes => _availableLanguageCodes.AsReadOnly();

        public string CurrentPackPath
        {
            get => _currentPackPath;
            private set
            {
                if (string.Equals(_currentPackPath, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _currentPackPath = value;
                OnPropertyChanged();
            }
        }

        // WPF may attempt a TwoWay binding in a few host-property scenarios
        // even though localization values are effectively read-only for the tool.
        // A no-op setter keeps the indexer binding-safe without changing behavior.
        public string this[string key]
        {
            get => Get(key);
            set { }
        }

        public string Get(string key, string fallback = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var serverMessage = ServerMessagePersonalize.GetMessageOrDefault(key, null);
            if (!string.IsNullOrWhiteSpace(serverMessage))
            {
                return serverMessage;
            }

            if (_messages.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback ?? key;
        }

        private void OnServerMessagesUpdated(object sender, EventArgs e)
        {
            OnPropertyChanged("Item[]");
        }

        public void LoadDefaultMessages()
        {
            LoadLanguage(DefaultLanguageCode);
        }

        public bool LoadLanguage(string languageCode)
        {
            RefreshAvailableLanguages();

            var requestedLanguage = NormalizeLanguageCode(languageCode);
            var englishPath = GetLanguagePackPath(DefaultLanguageCode);
            var requestedPath = GetLanguagePackPath(requestedLanguage);

            if (!File.Exists(englishPath))
            {
                _messages.Clear();
                LanguageCode = DefaultLanguageCode;
                CurrentPackPath = null;
                OnPropertyChanged("Item[]");
                return false;
            }

            try
            {
                var mergedMessages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!TryLoadCatalog(englishPath, out var englishCatalog))
                {
                    _messages.Clear();
                    LanguageCode = DefaultLanguageCode;
                    CurrentPackPath = null;
                    OnPropertyChanged("Item[]");
                    return false;
                }

                MergeMessages(mergedMessages, englishCatalog?.messages);

                var effectiveLanguage = DefaultLanguageCode;
                var effectivePath = englishPath;

                if (!string.Equals(requestedLanguage, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(requestedPath)
                    && TryLoadCatalog(requestedPath, out var requestedCatalog))
                {
                    MergeMessages(mergedMessages, requestedCatalog?.messages);
                    effectiveLanguage = string.IsNullOrWhiteSpace(requestedCatalog?.key) ? requestedLanguage : requestedCatalog.key;
                    effectivePath = requestedPath;
                }

                _messages.Clear();
                foreach (var item in mergedMessages)
                {
                    _messages[item.Key] = item.Value;
                }

                LanguageCode = effectiveLanguage;
                CurrentPackPath = effectivePath;
                OnPropertyChanged(nameof(AvailableLanguageCodes));
                OnPropertyChanged("Item[]");
                return true;
            }
            catch
            {
                _messages.Clear();
                LanguageCode = DefaultLanguageCode;
                CurrentPackPath = null;
                OnPropertyChanged("Item[]");
                return false;
            }
        }

        private void RefreshAvailableLanguages()
        {
            var localizationFolder = GetLocalizationFolderPath();
            _availableLanguageCodes.Clear();
            _availableLanguageCodes.Add(DefaultLanguageCode);

            if (!Directory.Exists(localizationFolder))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(localizationFolder, "messages_*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name) || name.Length <= "messages_".Length)
                {
                    continue;
                }

                var code = name.Substring("messages_".Length).ToUpperInvariant();
                if (!_availableLanguageCodes.Any(existing => string.Equals(existing, code, StringComparison.OrdinalIgnoreCase)))
                {
                    _availableLanguageCodes.Add(code);
                }
            }

            _availableLanguageCodes.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            return string.IsNullOrWhiteSpace(languageCode)
                ? DefaultLanguageCode
                : languageCode.Trim().ToUpperInvariant();
        }

        private static string GetLocalizationFolderPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localization");
        }

        private static string GetLanguagePackPath(string languageCode)
        {
            return Path.Combine(GetLocalizationFolderPath(), $"messages_{languageCode.ToLowerInvariant()}.json");
        }

        private static bool TryLoadCatalog(string path, out ToolMessageCatalog catalog)
        {
            catalog = null;

            if (!File.Exists(path))
            {
                return false;
            }

            using (var stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(ToolMessageCatalog),
                    new DataContractJsonSerializerSettings
                    {
                        UseSimpleDictionaryFormat = true
                    });
                catalog = serializer.ReadObject(stream) as ToolMessageCatalog;
            }

            return catalog != null;
        }

        private static void MergeMessages(IDictionary<string, string> target, IDictionary<string, string> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                target[item.Key] = item.Value;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
