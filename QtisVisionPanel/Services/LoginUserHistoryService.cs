using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Stores only successful login usernames for operator convenience.
    /// Passwords are never persisted.
    /// </summary>
    public class LoginUserHistoryService
    {
        private const int MaxUsernames = 20;
        private readonly string _historyFilePath;

        public LoginUserHistoryService()
            : this(BuildDefaultHistoryFilePath())
        {
        }

        public LoginUserHistoryService(string historyFilePath)
        {
            _historyFilePath = historyFilePath;
        }

        public IReadOnlyList<string> LoadUsernames()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_historyFilePath) || !File.Exists(_historyFilePath))
                {
                    return Array.Empty<string>();
                }

                var json = File.ReadAllText(_historyFilePath);
                var usernames = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();

                return usernames
                    .Select(NormalizeUsername)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxUsernames)
                    .ToList();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Unable to load login username history: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public void RememberSuccessfulLogin(string username)
        {
            username = NormalizeUsername(username);
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            try
            {
                var usernames = new List<string> { username };
                usernames.AddRange(LoadUsernames().Where(item =>
                    !string.Equals(item, username, StringComparison.OrdinalIgnoreCase)));

                usernames = usernames
                    .Take(MaxUsernames)
                    .ToList();

                var directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_historyFilePath, JsonConvert.SerializeObject(usernames, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Unable to save login username history: {ex.Message}");
            }
        }

        private static string NormalizeUsername(string username)
        {
            return string.IsNullOrWhiteSpace(username)
                ? string.Empty
                : username.Trim();
        }

        private static string BuildDefaultHistoryFilePath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = AppDomain.CurrentDomain.BaseDirectory;
            }

            return Path.Combine(root, "Pulsar Engineering", "QtisVisionPanel", "login-user-history.json");
        }
    }
}
