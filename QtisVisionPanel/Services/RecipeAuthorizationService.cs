using System;
using System.Threading.Tasks;
using System.Windows;
using QtisVisionPanel.Views;

namespace QtisVisionPanel.Services
{
    public class RecipeAuthorizationService
    {
        private readonly AuthorizationService _authService;

        public RecipeAuthorizationService()
        {
            _authService = ServiceLocator.Authorization;
        }

        // Tutti i metodi ora leggono dal database
        public async Task<bool> CanEditTolerances()
        {
            return await _authService.CanAccessFeature("editRecipeTolerances");
        }

        public async Task<bool> CanEditProductInfo()
        {
            return await _authService.CanAccessFeature("editProductInfo");
        }

        public async Task<bool> CanSaveRecipe()
        {
            return await _authService.CanAccessFeature("saveRecipe");
        }

        public async Task<bool> CanCreateNewRecipe()
        {
            return await _authService.CanAccessFeature("createRecipe");
        }

        public async Task<bool> CanDeleteRecipe()
        {
            return await _authService.CanAccessFeature("deleteRecipe");
        }

        public async Task<bool> CanLoadRecipeToProduction()
        {
            return await _authService.CanAccessFeature("loadRecipeToProduction");
        }

        public async Task<bool> CanViewRecipeDetails()
        {
            return await _authService.CanAccessFeature("viewRecipeDetails");
        }

        public async Task<bool> CanEditRecipe()
        {
            return await _authService.CanAccessFeature("recipeManagement");
        }
        public async Task<bool> CanEditTriggerDelay()
        {
            return await _authService.CanAccessFeature("editCameraTriggerDelay");
        }
        // Mostra messaggio di errore se non autorizzato
        public void ShowEditPermissionError(string action)
        {
            string message = $"Per {action} è necessario avere l'autorizzazione appropriata. " +
                           $"Utente attuale: {UserSession.CurrentUser} ({UserSession.CurrentRole})";

            new SystemNotificationWindow("Autorizzazione insufficiente", message, NotificationSeverity.Warning).ShowDialog();
        }
    }
}