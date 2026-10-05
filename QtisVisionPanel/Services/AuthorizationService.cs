using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Central authorization gate for pages and feature-level commands.
    ///
    /// The UI must not decide permissions by itself: views ask this service so
    /// Viewer, Operator, Expert, Installer and Administrator stay consistent
    /// across navigation, recipe actions and dangerous maintenance commands.
    /// </summary>
    public class AuthorizationService
    {
        private Dictionary<string, List<string>> _roleFeaturesCache; // ruolo -> lista feature
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
        private readonly SemaphoreSlim _permissionLoadLock = new SemaphoreSlim(1, 1);
        // Stable mapping between navigation view names and authorization
        // feature keys stored in pulsarsdk_auth.roles_authorizations.
        private static readonly IReadOnlyDictionary<string, string> ViewToFeatureMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Channel1", "cameraView" },
                { "Statistics", "statisticsView" },
                { "Counters", "countersView" },
                { "RecipeManager", "viewRecipeDetails" },
                { "JobToolEditor", "jobToolEditor" },
                { "Setting", "viewSettingJob" },
                { "InspectionConfig", "inspectionConfig" },
                { "EjectionAlarms", "alarmManagement" },
                { "Preferences", "preferencesView" },
                { "Alarmsview", "alarmsView" },
                { "DataInspector", "dataInspectorView" },
                { "SystemDiagnostics", "systemDiagnosticsView" },
                { "PowerFlex525", "powerFlex525View" },
                { "OpcUaConfiguration", "opcUaConfigurationView" },
                { "Manual", "manualView" },
                { "Assistance", "assistanceView" },
                { "Automation", "automationView" }
            };
        // Production safety override: even if a permissive DB row exists, these
        // features stay hidden from Operator because they can alter recipe,
        // machine setup, diagnostics or physical outputs.
        private static readonly HashSet<string> OperatorRestrictedFeatures =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "recipeManagement",
                "saveRecipe",
                "createRecipe",
                "deleteRecipe",
                "editRecipeTolerances",
                "editProductInfo",
                "editCameraTriggerDelay",
                "jobToolEditor",
                "viewSettingJob",
                "inspectionConfig",
                "alarmManagement",
                "preferencesView",
                "dataInspectorView",
                "systemDiagnosticsView",
                "powerFlex525View",
                "opcUaConfigurationView",
                "automationView"
            };
        public AuthorizationService()
        {
            _roleFeaturesCache = new Dictionary<string, List<string>>();
        }

        private static string GetConnectionString()
        {
            var config = MainWindow.configManager?.Config?.MySqlConnection;
            if (config == null)
            {
                throw new InvalidOperationException("MySqlConnection config is not loaded.");
            }

            return $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
        }

        // Per le viste, manteniamo il mapping vista -> feature
        public async Task<bool> CanAccessView(string viewName)
        {
            if (IsRecipeManagerView(viewName))
            {
                return await CanAccessRecipeManagerViewAsync().ConfigureAwait(false);
            }

            if (!ViewToFeatureMap.TryGetValue(viewName ?? string.Empty, out var feature))
                return false; // vista non mappata

            return await CanAccessFeature(feature).ConfigureAwait(false);
        }

        public bool CanAccessViewCached(string viewName)
        {
            if (IsRecipeManagerView(viewName))
            {
                return CanAccessRecipeManagerViewCached();
            }

            if (!ViewToFeatureMap.TryGetValue(viewName ?? string.Empty, out var feature))
            {
                return false;
            }

            return CanAccessFeatureCached(feature);
        }
        // Applica prima le restrizioni operative e poi i permessi caricati dal database.
        public async Task<bool> CanAccessFeature(string featureName)
        {
            if (IsRestrictedForOperatorRole(featureName))
            {
                return false;
            }

            if (CanAccessFeatureCached(featureName))
            {
                if ((DateTime.Now - _lastCacheUpdate) > _cacheDuration &&
                    !UserSession.IsAdministrator &&
                    !UserSession.IsInstaller)
                {
                    _ = LoadAllPermissionsAsync();
                }

                return true;
            }

            // Se la cache è scaduta o vuota, prova a ricaricare
            if ((DateTime.Now - _lastCacheUpdate) > _cacheDuration || !_roleFeaturesCache.Any())
            {
                bool loaded = await LoadAllPermissionsAsync().ConfigureAwait(false);
                if (!loaded && !_roleFeaturesCache.Any())
                {
                    // Database non disponibile e nessuna cache: nessun permesso
                    return false;
                }
            }

            return CanAccessFeatureCached(featureName);
        }

        public async Task EnsurePermissionsLoadedAsync(bool forceRefresh = false)
        {
            if (UserSession.IsAdministrator || UserSession.IsInstaller)
            {
                return;
            }

            if (!forceRefresh &&
                _roleFeaturesCache.Any() &&
                (DateTime.Now - _lastCacheUpdate) <= _cacheDuration)
            {
                return;
            }

            await LoadAllPermissionsAsync().ConfigureAwait(false);
        }

        private bool CanAccessFeatureCached(string featureName)
        {
            if (IsRestrictedForOperatorRole(featureName))
            {
                return false;
            }

            if (UserSession.IsAdministrator || UserSession.IsInstaller)
            {
                return true;
            }

            if (_roleFeaturesCache.TryGetValue(UserSession.CurrentRole, out var features))
            {
                if (features.Contains(featureName))
                {
                    return true;
                }

                if (string.Equals(featureName, "systemDiagnosticsView", StringComparison.OrdinalIgnoreCase) &&
                    _fallbackPermissions.TryGetValue(UserSession.CurrentRole, out var fallbackFeatures))
                {
                    return fallbackFeatures.Contains(featureName);
                }

                return false;
            }

            if (!_roleFeaturesCache.Any() &&
                _fallbackPermissions.TryGetValue(UserSession.CurrentRole, out var fallbackFeaturesOnly))
            {
                return fallbackFeaturesOnly.Contains(featureName);
            }

            return false;
        }

        private async Task<bool> CanAccessRecipeManagerViewAsync()
        {
            if (CanAccessRecipeManagerViewCached())
            {
                return true;
            }

            return await CanAccessFeature("viewRecipeDetails").ConfigureAwait(false) ||
                   await CanAccessFeature("loadRecipeToProduction").ConfigureAwait(false) ||
                   await CanAccessFeature("recipeManagement").ConfigureAwait(false);
        }

        private bool CanAccessRecipeManagerViewCached()
        {
            if (UserSession.IsAdministrator || UserSession.IsInstaller || UserSession.IsExpert || UserSession.IsOperator)
            {
                return true;
            }

            return CanAccessFeatureCached("viewRecipeDetails") ||
                   CanAccessFeatureCached("loadRecipeToProduction") ||
                   CanAccessFeatureCached("recipeManagement");
        }

        private static bool IsRecipeManagerView(string viewName)
        {
            return string.Equals(viewName, "RecipeManager", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRestrictedForOperatorRole(string featureName)
        {
            if (string.IsNullOrWhiteSpace(featureName))
            {
                return true;
            }

            if (UserSession.IsAdministrator || UserSession.IsInstaller || UserSession.IsExpert)
            {
                return false;
            }

            return OperatorRestrictedFeatures.Contains(featureName);
        }

        private Dictionary<string, List<string>> _fallbackPermissions = new Dictionary<string, List<string>>
        {
            ["Expert"] = new List<string> { "cameraView", "statisticsView", "countersView", "recipeManagement", "loadRecipeToProduction", "viewRecipeDetails", "jobToolEditor", "viewSettingJob", "inspectionConfig", "alarmManagement", "preferencesView", "alarmsView", "dataInspectorView", "systemDiagnosticsView", "powerFlex525View", "opcUaConfigurationView", "manualView", "assistanceView", "automationView" },
            ["Viewer"] = new List<string> { "cameraView", "statisticsView", "countersView", "alarmsView", "manualView" },
            ["Operator"] = new List<string> { "cameraView", "statisticsView", "countersView", "loadRecipeToProduction", "viewRecipeDetails", "alarmsView", "manualView" },
            // ... altri ruoli
        };

        // Metodi specifici per features comuni
        public async Task<bool> CanViewSettingJob()
        {
            return await CanAccessFeature("viewSettingJob");
        }

        public async Task<bool> CanViewDigitalIO()
        {
            return await CanAccessFeature("digitalIOView");
        }

        public async Task<bool> CanEditTolerances()
        {
            return await CanAccessFeature("toleranceSetting");
        }

        public async Task<bool> CanEditRecipe()
        {
            return await CanAccessFeature("recipeManagement");
            
        }

        public async Task<bool> CanEditJobTools()
        {
            return await CanAccessFeature("jobToolEditor");
        }

        public async Task<bool> CanEditInspectionConfig()
        {
            return await CanAccessFeature("inspectionConfig");
        }
        public async Task<bool> CanViewStatistics()
        {

            return await CanAccessView("Statistics").ConfigureAwait(false);
        }

        // Ottiene tutte le feature disponibili
        // Carica tutte le autorizzazioni per tutti i ruoli (chiamato all'avvio e periodicamente)
        private async Task<bool> LoadAllPermissionsAsync()
        {
            await _permissionLoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if ((DateTime.Now - _lastCacheUpdate) <= _cacheDuration && _roleFeaturesCache.Any())
                {
                    return true;
                }

                using (var conn = new MySqlConnection(GetConnectionString()))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    string query = "SELECT rolename, Features FROM pulsarsdk_auth.roles_authorizations";
                    using (var cmd = new MySqlCommand(query, conn))
                    using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        var newCache = new Dictionary<string, List<string>>();
                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            string role = reader.GetString(0);
                            string feature = reader.GetString(1);
                            if (!newCache.ContainsKey(role))
                                newCache[role] = new List<string>();
                            newCache[role].Add(feature);
                        }
                        _roleFeaturesCache = newCache;
                        _lastCacheUpdate = DateTime.Now;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Impossibile caricare permessi dal database: {ex.Message}");
                return false;
            }
            finally
            {
                _permissionLoadLock.Release();
            }
        }
        public async Task<List<string>> GetFeaturesForRole(string role)
        {
            var features = new List<string>();

            try
            {
                using (var conn = new MySqlConnection(GetConnectionString()))
                {
                    await conn.OpenAsync();

                    string query = "SELECT Features FROM pulsarsdk_auth.roles_authorizations WHERE rolename = @role";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@role", role);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                features.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento features: {ex.Message}");
            }

            return features;
        }

        /// <summary>
        /// Forces the next permission check to reload from DB instead of using the timed cache.
        /// Call after any role or permission modification so the change takes effect immediately.
        /// </summary>
        public void InvalidateCache()
        {
            _roleFeaturesCache = new Dictionary<string, List<string>>();
            _lastCacheUpdate = DateTime.MinValue;
        }
    }
}
