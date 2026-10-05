using NLog;
using QtisVisionPanel.Extensions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Orchestrates recipe changes with automatic rollback on failure.
    /// Delegates all vision and config work to existing MainWindow methods -
    /// this service only adds the snapshot/restore envelope and audit trail.
    /// </summary>
    public sealed class RecipeTransitionService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Switches to <paramref name="newRecipeName"/> with rollback on failure.
        /// Returns true on success, false if the change failed (rollback attempted).
        /// </summary>
        public async Task<bool> ChangeRecipeAsync(
            string newRecipeName,
            object progressWindow = null,
            CancellationToken ct = default)
        {
            var mw = MainWindow.MainView
                ?? throw new InvalidOperationException("MainWindow not available");

            string previousRecipe = MainWindow.configManager?.Config?.Configuration?.LastRecipe;

            _logger.Info("RECIPE_CHANGE_START|from={0}|to={1}|user={2}",
                previousRecipe, newRecipeName, UserSession.CurrentUser);

            ServiceLocator.AuditLogService?.LogAsync(
                "RECIPE_CHANGE",
                UserSession.CurrentUser ?? "unknown",
                $"from={previousRecipe}|to={newRecipeName}",
                oldValue: previousRecipe,
                newValue: newRecipeName).SafeFireAndForget();

            try
            {
                var pw = progressWindow as OperationProgressWindow;
                await mw.InitializeRecipeAsync(newRecipeName);
                await mw.InitializeComponentforChangeRecipe(pw);

                _logger.Info("RECIPE_CHANGE_SUCCESS|from={0}|to={1}", previousRecipe, newRecipeName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "RECIPE_CHANGE_FAILED|recipe={0}|rollback_to={1}",
                    newRecipeName, previousRecipe);

                if (!string.IsNullOrEmpty(previousRecipe))
                {
                    // A failed recipe reload can leave VisionPro, config and UI
                    // partially updated. Restore the previous production recipe
                    // through the same runtime path instead of only changing a
                    // config string.
                    _logger.Info("RECIPE_ROLLBACK_START|to={0}", previousRecipe);
                    try
                    {
                        await mw.InitializeRecipeAsync(previousRecipe);
                        await mw.InitializeComponentforChangeRecipe(null);
                        _logger.Info("RECIPE_ROLLBACK_OK|to={0}", previousRecipe);
                    }
                    catch (Exception rbEx)
                    {
                        _logger.Fatal(rbEx,
                            "RECIPE_ROLLBACK_FAILED - system in unknown state|target={0}", previousRecipe);
                    }
                }

                return false;
            }
        }
    }
}
