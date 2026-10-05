using System;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using NLog;

namespace QtisVisionPanel.Database
{
    public class GlobalCounterService : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _backupFilePath;
        private bool _useBackupMode = false;
        private readonly string _connectionString;
        private readonly Dictionary<string, int> _counters = new Dictionary<string, int>();
        private bool _disposed = false;

        // Evento che viene sollevato quando un contatore cambia
        public event Action<CounterUpdate> CounterUpdated;

        public class CounterUpdate
        {
            public string CounterName { get; set; }
            public int NewValue { get; set; }
            public Dictionary<string, int> AllCounters { get; set; }
        }

        public GlobalCounterService()
        {
            // Percorso per il file di backup locale
            var appDataPath = Path.Combine(
                MainWindow.configManager.Config.Configuration.Recipe_Folder,
                "QtisVisionPanel");

            Directory.CreateDirectory(appDataPath);

            _backupFilePath = Path.Combine(appDataPath, "counters_backup.json");

            // Stringa di connessione MySQL
            var config = MainWindow.configManager?.Config?.MySqlConnection;
            if (config != null)
            {
                _connectionString = DbConnectionStringHelper.Build(config.Host, config.Db, config.User, config.Password, config.port);
                _logger.Info($"MySQL connection string configurata");
            }
            else
            {
                _useBackupMode = true;
                _logger.Warn($"Configurazione MySQL non trovata, uso modalità backup");
            }

            // Carica i contatori all'avvio
            Task.Run(async () => await LoadCountersAsync());
        }

        // Metodo principale per incrementare qualsiasi contatore
        public async Task IncrementCounterAsync(string counterName, int increment = 1)
        {
            try
            {
                // Blocco per evitare race conditions
                lock (_counters)
                {
                    if (!_counters.ContainsKey(counterName))
                    {
                        _counters[counterName] = 0;
                    }
                    _counters[counterName] += increment;
                }

                _logger.Debug($"Counter '{counterName}' incremented to {_counters[counterName]}");

                // Salva in MySQL (se disponibile)
                if (!_useBackupMode)
                {
                    try
                    {
                        await SaveCounterToMySQLAsync(counterName, _counters[counterName]);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"MySQL non disponibile, uso backup: {ex.Message}");
                        _useBackupMode = true;
                    }
                }
                // Salva in background
                _ = Task.Run(async () => await SaveCountersAsync());

                // Notifica l'UI
                NotifyCounterUpdate(counterName, _counters[counterName]);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error incrementing counter '{counterName}': {ex.Message}");
            }
        }
        // Salva contatore in MySQL
        private async Task SaveCounterToMySQLAsync(string feature, int value)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"
                    INSERT INTO tblglobalcounters (Feature, Counter) 
                    VALUES (@Feature, @Counter)
                    ON DUPLICATE KEY UPDATE 
                    Counter = @Counter,
                    LastUpdated = NOW()";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Feature", feature);
                        cmd.Parameters.AddWithValue("@Counter", value);

                        await cmd.ExecuteNonQueryAsync();
                        _logger.Debug($"Counter '{feature}' salvato in MySQL: {value}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore salvataggio MySQL per '{feature}': {ex.Message}");
                throw;
            }
        }

        // Metodo per ottenere tutti i contatori
        public Dictionary<string, int> GetAllCounters()
        {
            lock (_counters)
            {
                return new Dictionary<string, int>(_counters);
            }
        }

        // Metodo per ottenere contatori in tempo reale
        public async Task<Dictionary<string, int>> GetRealTimeCountersAsync()
        {
            var counters = GetAllCounters();

            // Notifica UI con tutti i contatori
            NotifyAllCounters(counters);

            return await Task.FromResult(counters);
        }

        // Metodo per caricare tutti i contatori
        public async Task<Dictionary<string, int>> LoadAllCountersAsync()
        {

            await LoadCountersAsync();
            return GetAllCounters();
        }

        // Metodo per resettare tutti i contatori
        public async Task ResetAllCountersAsync()
        {
            try
            {
                lock (_counters)
                {
                    _counters.Clear();
                }

                await SaveCountersAsync();

                // Notifica UI che tutti i contatori sono stati resettati
                NotifyAllCounters(new Dictionary<string, int>());

                _logger.Info("All counters reset");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error resetting counters: {ex.Message}");
            }
        }

        // Carica i contatori dal file di backup
        private async Task LoadCountersAsync()
        {
            try
            {
                if (File.Exists(_backupFilePath))
                {
                    var json = await Task.Run(()=> File.ReadAllText(_backupFilePath));
                    var loadedCounters = JsonConvert.DeserializeObject<Dictionary<string, int>>(json)
                        ?? new Dictionary<string, int>();

                    lock (_counters)
                    {
                        _counters.Clear();
                        foreach (var kvp in loadedCounters)
                        {
                            _counters[kvp.Key] = kvp.Value;
                        }
                    }

                    _logger.Info($"Loaded {loadedCounters.Count} counters from backup");

                    // Notifica UI dopo il caricamento
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        NotifyAllCounters(loadedCounters);
                    });
                }
                else
                {
                    _logger.Info("No backup file found, starting with empty counters");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error loading counters: {ex.Message}");
            }
        }

        // Salva tutti i contatori nel file di backup
        private async Task SaveCountersAsync()
        {
            try
            {
                Dictionary<string, int> countersToSave;
                lock (_counters)
                {
                    countersToSave = new Dictionary<string, int>(_counters);
                }

                var json = JsonConvert.SerializeObject(countersToSave, Formatting.Indented);
                await Task.Run(()=> File.WriteAllText(_backupFilePath, json));

                _logger.Debug($"Saved {countersToSave.Count} counters to backup");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error saving counters to backup: {ex.Message}");
            }
        }

        // Notifica aggiornamento di un singolo contatore all'UI
        private void NotifyCounterUpdate(string counterName, int value)
        {
            _logger.Debug($"NotifyCounterUpdate: {counterName} = {value}");

            var update = new CounterUpdate
            {
                CounterName = counterName,
                NewValue = value,
                AllCounters = GetAllCounters()
            };
            _logger.Debug($"Invocazione evento CounterUpdated con {update.AllCounters.Count} contatori");
            // Esegui sul thread UI
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _logger.Debug($"Evento CounterUpdated invocato sul thread UI");
                CounterUpdated?.Invoke(update);
            });
        }

        // Notifica tutti i contatori all'UI
        private void NotifyAllCounters(Dictionary<string, int> counters)
        {
            var update = new CounterUpdate
            {
                CounterName = "ALL",
                NewValue = 0,
                AllCounters = counters
            };

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CounterUpdated?.Invoke(update);
            });
        }

        // Metodo per provare a riconnettere al database (sempre in modalità backup)
        public async Task<bool> TryReconnectToDatabaseAsync()
        {
            // Per ora usa sempre backup locale
            return await Task.FromResult(true);
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Salva prima di chiudere
            if (_counters.Count > 0)
            {
                Task.Run(async () => await SaveCountersAsync()).Wait(2000);
            }

            _disposed = true;
        }
    }
}