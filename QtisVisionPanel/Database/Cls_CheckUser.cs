using MySql.Data.MySqlClient;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    public class UserDatabaseRecord
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Role { get; set; }
    }

    public class Cls_CheckUser
    {

        private readonly string _host;
        private readonly string _user;
        private readonly string _password;
        private readonly string _port;
        private readonly string _database;
        private readonly bool _sslDisabled;
        private readonly string _connectionString;
        MySqlConnection conn = null;

        private static readonly IReadOnlyList<(string Username, string Password, string Role)> DefaultUserSeeds =
            new List<(string Username, string Password, string Role)>
            {
                ("pulsar", "Pulsar.Quality", "Administrator"),
                ("operator", "operator123+", "Operator"),
                ("installer", "installer123+", "Installer"),
                ("expert", "expert123+", "Expert"),
                ("viewer", "viewer123+", "Viewer")
            };

        public Cls_CheckUser(string host, string database, string user, string password, string port, bool sslDisabled = true)
        {
            _host = host;
            _user = user;
            _password = password;
            _port = port;
            _database = database;
            _sslDisabled = sslDisabled;
            _connectionString = DbConnectionStringHelper.Build(host, database, user, password, port, sslDisabled);
        }
        private async Task EnsureDatabaseExistsAsync()
        {
            // No database name in this string — we're creating the DB itself.
            string connectionString = DbConnectionStringHelper.Build(_host, "", _user, _password, _port, _sslDisabled);
            using (var connection = new MySqlConnection(connectionString))
            using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await connection.OpenAsync(cts.Token);
                if (!System.Text.RegularExpressions.Regex.IsMatch(_database ?? "", @"^[a-zA-Z0-9_]+$"))
                    throw new InvalidOperationException($"INVALID_DB_NAME — nome database non valido: '{_database}'");

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_database}`;";
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        // Fase 0 stabilita': async Task (non piu' async void) — il chiamante puo' attendere
        // il completamento dell'init utenti e le eccezioni non finiscono sul ThreadPool.
        public async Task InitializeUserAsync(string username, string password, string roles)
        {
            try
            {
                await EnsureDatabaseExistsAsync();
                await CreateTablesAsync();
                bool seedDefaultUsers = !await AnyInteractiveUserExistsAsync();
                await InsertNewUsersAsync(username, password, roles);
                if (seedDefaultUsers)
                {
                    foreach (var seed in DefaultUserSeeds)
                    {
                        await InsertNewUsersAsync(seed.Username, seed.Password, seed.Role);
                    }
                }

                // Popola le autorizzazioni di default
                await InsertDefaultAuthorizationsAsync();
                await AuditDefaultCredentialPostureAsync();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error("Unhandled exception in InitializeUserAsync: " + ex.Message);
            }
        }
        private async Task CreateTablesAsync()
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var command = conn.CreateCommand())
                    {
                        command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS pulsarsdk_auth.users (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        username VARCHAR(255) UNIQUE NOT NULL,
                        password VARCHAR(255) NOT NULL,
                        Roles VARCHAR(255) NOT NULL
                    ) ENGINE = InnoDB AUTO_INCREMENT = 1;

                    CREATE TABLE IF NOT EXISTS pulsarsdk_auth.roles_authorizations (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        Features VARCHAR(255) NOT NULL,
                        rolename VARCHAR(255) NOT NULL,
                        UNIQUE KEY unique_feature_role (Features, rolename)
                    ) ENGINE = InnoDB AUTO_INCREMENT = 1;";

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to create tables: " + ex.Message);
            }
        }
        private async Task InsertDefaultAuthorizationsAsync()
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Definizione delle autorizzazioni di default
                    var defaultAuthorizations = new List<(string Feature, string Role)>
{
    // ViewSettingJob - visibile da Expert
    ("viewSettingJob", "Expert"),
    
    // DigitalIOView - stesso di viewSetting
    ("digitalIOView", "Expert"),
    
    // ToleranceSetting - per RecipeManager
    ("toleranceSetting", "Expert"),
    ("toleranceSetting", "Operator"),
    
    // RecipeManagement - per gestione ricette
    ("recipeManagement", "Expert"),
    ("recipeManagement", "Operator"),
      ("recipeManagement", "Viewer"),
    
    // JobToolEditor - per modifica tool
    ("jobToolEditor", "Expert"),
    
    // InspectionConfig - configurazione ispezione
    ("inspectionConfig", "Expert"),
    
    // CameraView - visibile da tutti i ruoli loggati (tranne Administrator e Installer che hanno già accesso)
    ("cameraView", "Expert"),
    ("cameraView", "Operator"),
    ("cameraView", "Viewer"),
    
    // StatisticsView - visibile da tutti i ruoli loggati
    ("statisticsView", "Expert"),
    ("statisticsView", "Operator"),
    ("statisticsView", "Viewer"),
    
    // CountersView - visibile da tutti i ruoli loggati
    ("countersView", "Expert"),
    ("countersView", "Operator"),
    ("countersView", "Viewer"),

    // Viste di supporto e diagnostica
    ("preferencesView", "Expert"),
    ("preferencesView", "Operator"),
    ("preferencesView", "Viewer"),
    ("alarmsView", "Expert"),
    ("alarmsView", "Operator"),
    ("alarmsView", "Viewer"),
    ("dataInspectorView", "Expert"),
    ("dataInspectorView", "Operator"),
    ("dataInspectorView", "Viewer"),
    ("manualView", "Expert"),
    ("manualView", "Operator"),
    ("manualView", "Viewer"),
    ("assistanceView", "Expert"),
    ("assistanceView", "Operator"),
    ("assistanceView", "Viewer"),
    ("automationView", "Expert"),
    ("automationView", "Operator"),
    ("automationView", "Viewer"),
    
    // AlarmManagement - per gestione allarmi
    ("alarmManagement", "Expert"),
    
    // Features specifiche per RecipeManager
    ("viewRecipeDetails", "Expert"),
    ("viewRecipeDetails", "Operator"),
    ("viewRecipeDetails", "Viewer"),

    ("editRecipeTolerances", "Expert"),
    ("editRecipeTolerances", "Operator"),

    ("editProductInfo", "Expert"),
    ("editProductInfo", "Operator"),

    ("saveRecipe", "Expert"),
    ("saveRecipe", "Operator"),

    ("createRecipe", "Expert"),

    ("deleteRecipe", "Expert"),

    ("loadRecipeToProduction", "Expert"),
    ("loadRecipeToProduction", "Operator"),
    ("editCameraTriggerDelay", "Expert"),
    ("editCameraTriggerDelay", "Operator"),
    ("editCameraTriggerDelay", "Installer"),

    // PowerFlex 525 - inverter nastro accessibile ai ruoli tecnici
    ("powerFlex525View", "Expert"),
    ("powerFlex525View", "Installer"),

    // OPC UA - configurazione comunicazione accessibile ai ruoli tecnici
    ("opcUaConfigurationView", "Expert"),
    ("opcUaConfigurationView", "Installer")
};

                    foreach (var auth in defaultAuthorizations)
                    {
                        using (var command = conn.CreateCommand())
                        {
                            command.CommandText = @"
                            INSERT IGNORE INTO pulsarsdk_auth.roles_authorizations (Features, rolename)
                            VALUES (@feature, @rolename)";

                            command.Parameters.AddWithValue("@feature", auth.Feature);
                            command.Parameters.AddWithValue("@rolename", auth.Role);

                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'inserimento autorizzazioni: {ex.Message}");
            }
        }

        private async Task<bool> AnyInteractiveUserExistsAsync()
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT COUNT(*)
                        FROM pulsarsdk_auth.users
                        WHERE username <> 'NoUser';";
                    return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
                }
            }
        }

        private async Task AuditDefaultCredentialPostureAsync()
        {
            var exposedUsers = new List<string>();

            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT username, password, Roles
                            FROM pulsarsdk_auth.users
                            WHERE username IN ('pulsar', 'operator', 'installer', 'expert', 'viewer');";

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string username = reader.GetString(0);
                                string storedHash = reader.GetString(1);
                                string role = reader.GetString(2);

                                var seed = DefaultUserSeeds.FirstOrDefault(s =>
                                    string.Equals(s.Username, username, StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrWhiteSpace(seed.Username) &&
                                    PasswordMatches(storedHash, seed.Password))
                                {
                                    exposedUsers.Add($"{username} ({role})");
                                }
                            }
                        }
                    }
                }

                if (exposedUsers.Count == 0)
                {
                    return;
                }

                string details = "Known default user passwords still active: " + string.Join(", ", exposedUsers);
                MainWindow.logger?.Warn("DEFAULT_CREDENTIALS_PRESENT|" + details);

                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    NLog.LogLevel.Warn,
                    "DEFAULT_CREDENTIALS_PRESENT",
                    "Security",
                    "Known default user passwords are still active",
                    nameof(Cls_CheckUser),
                    details,
                    new Dictionary<string, object>
                    {
                        { "default_user_count", exposedUsers.Count },
                        { "users", string.Join(", ", exposedUsers) }
                    });

                ServiceLocator.AuditLogService?.LogAsync(
                    "DEFAULT_CREDENTIALS_PRESENT",
                    "system",
                    details).SafeFireAndForget();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn("DEFAULT_CREDENTIAL_AUDIT_FAILED|" + ex.Message);
            }
        }

        private static bool PasswordMatches(string storedHash, string plainPassword)
        {
            if (string.IsNullOrEmpty(storedHash) || plainPassword == null)
            {
                return false;
            }

            if (LooksLikeBCryptHash(storedHash))
            {
                return VerifyBCrypt(plainPassword, storedHash);
            }

            if (LooksLikeSha1Hash(storedHash))
            {
                return string.Equals(storedHash, HashCode(plainPassword), StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(storedHash, plainPassword, StringComparison.Ordinal);
        }

        private static bool IsKnownDefaultCredential(string username, string password, out string role)
        {
            role = null;
            if (string.IsNullOrWhiteSpace(username) || password == null)
            {
                return false;
            }

            var seed = DefaultUserSeeds.FirstOrDefault(s =>
                string.Equals(s.Username, username.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Password, password, StringComparison.Ordinal));

            if (string.IsNullOrWhiteSpace(seed.Username))
            {
                return false;
            }

            role = seed.Role;
            return true;
        }

        public async Task<bool> InsertNewUsersAsync(string username, string password, string Roles)
        {

            try
            {
                using (conn = new MySqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var command = conn.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM pulsarsdk_auth.users WHERE username = @username";
                        command.Parameters.AddWithValue("@username", username);
                        var exists = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;

                        if (!exists)
                        {

                            // Secondo comando: inserisce l'utente
                            using (var insertCommand = conn.CreateCommand())
                            {
                                insertCommand.CommandText = "INSERT INTO pulsarsdk_auth.users (username, password, Roles) VALUES (@username, @password, @Roles)";
                                insertCommand.Parameters.AddWithValue("@username", username);
                                insertCommand.Parameters.AddWithValue("@password",
                                    LooksLikeBCryptHash(password) ? password :
                                    LooksLikeSha1Hash(password)   ? password.ToLowerInvariant() :
                                                                    HashPasswordBCrypt(password));
                                insertCommand.Parameters.AddWithValue("@Roles", Roles);
                                await insertCommand.ExecuteNonQueryAsync();
                            }

                            var actor = string.IsNullOrWhiteSpace(UserSession.CurrentUser) ? "system" : UserSession.CurrentUser;
                            ServiceLocator.AuditLogService?.LogAsync(
                                "USER_CREATED",
                                actor,
                                $"created_user={username}; role={Roles}",
                                null,
                                Roles).SafeFireAndForget();
                            return true;
                        }
                        else
                        {
                            MainWindow.logger?.Info($"User '{username}' already exists. Skipping insertion.");
                            return false;
                        }
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to initialize user: " + ex.Message + "\n" + ex.StackTrace);
                throw;
            }
            finally
            {
                if (conn != null)
                {
                    conn.Close();
                }
            }
        }
        public async Task<string> GetUserRoleAsync(string username, string password)
        {
            string role = null;
            bool upgradeToBCrypt = false;

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Roles, password FROM pulsarsdk_auth.users WHERE username = @username";
                    command.Parameters.AddWithValue("@username", username);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            string storedHash = reader.GetString(1);
                            bool authenticated;

                            if (LooksLikeBCryptHash(storedHash))
                            {
                                authenticated = VerifyBCrypt(password, storedHash);
                            }
                            else
                            {
                                // SHA1 or legacy
                                authenticated = storedHash == HashCode(password);
                                if (authenticated)
                                    upgradeToBCrypt = true;
                            }

                            if (authenticated)
                                role = reader.GetString(0);
                        }
                    }
                }

                // Silently upgrade SHA1 → BCrypt while connection is still open
                if (role != null && upgradeToBCrypt)
                {
                    try
                    {
                        string newHash = HashPasswordBCrypt(password);
                        using (var upgradeCmd = connection.CreateCommand())
                        {
                            upgradeCmd.CommandText = "UPDATE pulsarsdk_auth.users SET password = @hash WHERE username = @user";
                            upgradeCmd.Parameters.AddWithValue("@hash", newHash);
                            upgradeCmd.Parameters.AddWithValue("@user", username);
                            await upgradeCmd.ExecuteNonQueryAsync();
                            MainWindow.logger?.Info("BCRYPT_UPGRADE_OK|username={0}", username);
                        }
                    }
                    catch (Exception ex)
                    {
                        MainWindow.logger?.Warn("BCRYPT_UPGRADE_FAILED|username={0}|err={1}", username, ex.Message);
                    }
                }
            }

            if (role != null)
            {
                ServiceLocator.AuditLogService?.LogAsync("LOGIN_SUCCESS", username, $"role={role}").SafeFireAndForget();
                if (IsKnownDefaultCredential(username, password, out var defaultRole))
                {
                    var details = $"username={username}; role={defaultRole}; action=change_password_required";
                    ServiceLocator.AuditLogService?.LogAsync(
                        "DEFAULT_CREDENTIAL_LOGIN",
                        username,
                        details).SafeFireAndForget();
                    ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                        NLog.LogLevel.Warn,
                        "DEFAULT_CREDENTIAL_LOGIN",
                        "Security",
                        "User logged in with a known default password",
                        nameof(GetUserRoleAsync),
                        details,
                        new Dictionary<string, object>
                        {
                            { "username", username },
                            { "role", defaultRole }
                        });
                }
            }
            else
            {
                ServiceLocator.AuditLogService?.LogAsync("LOGIN_FAILURE", username, "invalid credentials").SafeFireAndForget();
            }

            return role;
        }
        public static string HashCode(string str)
        {
            string rethash = "";
            try
            {

                System.Security.Cryptography.SHA1 hash = System.Security.Cryptography.SHA1.Create();
                System.Text.ASCIIEncoding encoder = new System.Text.ASCIIEncoding();
                byte[] combined = encoder.GetBytes(str);
                hash.ComputeHash(combined);
                // rethash = Convert.ToBase64String(hash.Hash);
                rethash = BitConverter.ToString(hash.Hash).ToLower().Replace("-", "");




            }
            catch (Exception ex)
            {
                string strerr = "Error in HashCode : " + ex.Message;
            }
            return rethash;
        }
        public async Task<string> GetUserPasswordAsync(string username)
        {
            string password = null;

            using (var connection = new MySqlConnection(_connectionString))
            {
                try
                {
                    await connection.OpenAsync();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT password FROM pulsarsdk_auth.users WHERE username = @username";
                        command.Parameters.AddWithValue("@username", username);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                password = reader.GetString(0);
                            }
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    MainWindow.logger?.Error("Failed to close DB connection: " + ex.Message + "\n" + ex.StackTrace);
                }
            }

            return password;
        }

        public async Task<IReadOnlyList<string>> GetAvailableRolesAsync()
        {
            var roles = new List<string>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT role_name
                        FROM (
                            SELECT DISTINCT rolename AS role_name
                            FROM pulsarsdk_auth.roles_authorizations
                            UNION
                            SELECT DISTINCT Roles AS role_name
                            FROM pulsarsdk_auth.users
                        ) roles
                        WHERE role_name IS NOT NULL
                          AND role_name <> ''
                        ORDER BY
                          FIELD(role_name, 'Administrator', 'Installer', 'Expert', 'Operator', 'Viewer'),
                          role_name;";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            roles.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return roles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<IReadOnlyList<UserDatabaseRecord>> GetUsersAsync()
        {
            var users = new List<UserDatabaseRecord>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, username, Roles
                        FROM pulsarsdk_auth.users
                        ORDER BY username;";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            users.Add(new UserDatabaseRecord
                            {
                                Id = reader.GetInt32(0),
                                Username = reader.GetString(1),
                                Role = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return users;
        }

        public async Task<bool> UserExistsAsync(string username)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM pulsarsdk_auth.users WHERE username = @username";
                    command.Parameters.AddWithValue("@username", username);
                    return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
                }
            }
        }

        public async Task<bool> DeleteUserAsync(string username)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM pulsarsdk_auth.users WHERE username = @username";
                    command.Parameters.AddWithValue("@username", username);
                    bool deleted = await command.ExecuteNonQueryAsync() > 0;
                    if (deleted)
                    {
                        var actor = string.IsNullOrWhiteSpace(UserSession.CurrentUser) ? "system" : UserSession.CurrentUser;
                        ServiceLocator.AuditLogService?.LogAsync(
                            "USER_DELETED",
                            actor,
                            $"deleted_user={username}").SafeFireAndForget();
                    }

                    return deleted;
                }
            }
        }

        private static string HashPasswordBCrypt(string password)
        {
            var salt = new byte[16];
            new SecureRandom().NextBytes(salt);
            return OpenBsdBCrypt.Generate("2b", password.ToCharArray(), salt, 10);
        }

        private static bool VerifyBCrypt(string password, string hash)
        {
            try { return OpenBsdBCrypt.CheckPassword(hash, password.ToCharArray()); }
            catch { return false; }
        }

        private static bool LooksLikeBCryptHash(string value)
        {
            return value != null && value.Length == 60 && value.StartsWith("$2");
        }

        private static bool LooksLikeSha1Hash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 40)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                bool isHex =
                    (current >= '0' && current <= '9') ||
                    (current >= 'a' && current <= 'f') ||
                    (current >= 'A' && current <= 'F');

                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
