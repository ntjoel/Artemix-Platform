using MySql.Data.MySqlClient;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    public class clsConnection
    {
        public static MySqlConnection oConn;
        public DateTime localDate = DateTime.Now;

        public clsConnection()
        {
        }
        public async Task<bool> ConnectToDatabaseAsync()
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            if (!int.TryParse(config.port, out int port))
            {
                MainWindow.logger?.Error("Porta MySQL non valida: " + config.port);
                ServiceLocator.ApplicationEventLogger.LogDatabaseStatus(false, nameof(clsConnection), "Porta MySQL non valida");
                return false;
            }
            using (var db = new DbConnectionManager(
                config.Host,
                //config.Db,
                config.User,
                config.Password,
                port
                ))
            {
                if (await db.OpenConnectionAsync())
                {
                    var connection = db.GetConnection();
                    // Use the connection
                    // Example: MySqlCommand cmd = new MySqlCommand("SELECT * FROM table", connection);

                    return true;

                }
            }
            return false;




        }
    }
    /// <summary>
    /// Builds a standardised connection string with pooling and timeout baked in.
    /// All callers that open their own MySqlConnection should use this instead of
    /// hand-crafting the string, so that pool size and timeout are consistent.
    /// </summary>
    public static class DbConnectionStringHelper
    {
        public static string Build(string host, string database, string user, string password, string port, bool sslDisabled = true)
        {
            int.TryParse(port, out int portInt);
            return Build(host, database, user, password, portInt, sslDisabled);
        }

        public static string Build(string host, string database, string user, string password, int port, bool sslDisabled = true)
        {
            var b = new MySqlConnectionStringBuilder
            {
                Server   = host,
                Database = database,
                UserID   = user,
                Password = password,
                Port     = (uint)(port > 0 ? port : 3306),
                Pooling             = true,
                MinimumPoolSize     = 2,
                MaximumPoolSize     = 10,
                ConnectionLifeTime  = 300,
                ConnectionTimeout   = 5,
                DefaultCommandTimeout = 30
            };
            b.SslMode = sslDisabled ? MySqlSslMode.Disabled : MySqlSslMode.Preferred;
            return b.ConnectionString;
        }
    }

    public class DbConnectionManager : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private const int ConnectTimeoutSeconds = 5;

        public static string _connectionString;
        private MySqlConnection _connection;

        public DbConnectionManager(string host, string user, string password, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host non può essere vuoto.", nameof(host));
            //if (string.IsNullOrWhiteSpace(database))
            //    throw new ArgumentException("Database non può essere vuoto.", nameof(database));
            if (string.IsNullOrWhiteSpace(user))
                throw new ArgumentException("User non può essere vuoto.", nameof(user));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password non può essere vuota.", nameof(password));
            if (port <= 0)
                throw new ArgumentException("Porta non valida.", nameof(port));

            var builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                //Database = database,
                UserID = user,
                Password = password,
                Port = (uint)port,
                Pooling = true,
                MinimumPoolSize = 2,
                MaximumPoolSize = 15,
                ConnectionTimeout = ConnectTimeoutSeconds,
                DefaultCommandTimeout = 30
            };

            _connectionString = builder.ConnectionString;
        }

        public async Task<bool> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds + 1));
                try
                {
                    _connection = new MySqlConnection(_connectionString);
                    await _connection.OpenAsync(cts.Token);
                    ServiceLocator.ApplicationEventLogger.LogDatabaseStatus(true, nameof(DbConnectionManager), "Connessione MySQL aperta con successo");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    _log.Error("DB_CONNECT_TIMEOUT|host — connessione non riuscita entro {0}s", ConnectTimeoutSeconds);
                    ServiceLocator.ApplicationEventLogger.LogDatabaseStatus(false, nameof(DbConnectionManager), $"Timeout connessione ({ConnectTimeoutSeconds}s)");
                    return false;
                }
                catch (MySqlException ex)
                {
                    LogSqlException(ex);
                    ServiceLocator.ApplicationEventLogger.LogDatabaseStatus(false, nameof(DbConnectionManager), ex.Message);
                    return false;
                }
            }
        }

        public void CloseConnection()
        {
            try
            {
                _connection?.Close();
                _connection?.Dispose();
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to close DB connection: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        public MySqlConnection GetConnection() => _connection;

        private void LogSqlException(MySqlException ex)
        {
            //string message = ex.Number switch
            //{
            //    0 => "Cannot connect to server. Contact administrator.",
            //    1045 => "Invalid username/password. Please try again.",
            //    _ => "MySQL error: " + ex.Message
            //};
            string message;

            switch (ex.Number)
            {
                case 0:
                    message = "Cannot connect to server. Contact administrator.";
                    break;
                case 1045:
                    message = "Invalid username/password. Please try again.";
                    break;
                default:
                    message = "MySQL error: " + ex.Message;
                    break;
            }

            MainWindow.logger?.Error($"{message}\n{ex.StackTrace}");
        }

        public void Dispose()
        {
            CloseConnection();
        }
    }
}
