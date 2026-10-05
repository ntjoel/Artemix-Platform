using QtisVisionPanel.Cls_Config;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QtisVisionPanel.Cls_Vpro
{
    public interface ICounterUpdater
    {
        Task IncrementTotalAsync();
        Task IncrementCompliantAsync();
        Task IncrementNotCompliantAsync();
        Task IncrementLogoDefectsAsync();
        Task IncrementPrintCenteringDefectsAsync();
        Task IncrementOpenFlapsDefectsAsync();
        Task IncrementSurfaceCheckDefectsAsync();
        Task IncrementHeightDefectsAsync();
        Task IncrementSideSealingDefectsAsync();
        Task IncrementShapeDefectsAsync();
        Task IncrementShapeDefectsSideAsync();
        Task ResetAllCountersAsync();
        void MarkDirty();
        void ResetDirtyFlag();
        Task<RecipeParameters.Counter> GetCurrentCountersAsync();
        bool IsDirty { get; }
    }
    public class CounterUpdater : ICounterUpdater
    {
        private readonly AsyncRecipeParam _recipeParam;
        private readonly GlobalCounterService _globalCounterService;
        private bool _isDirty = false;

        public bool IsDirty => _isDirty;

        public CounterUpdater(AsyncRecipeParam recipeParam)
        {
            _recipeParam = recipeParam ?? throw new ArgumentNullException(nameof(recipeParam));
            _globalCounterService = new GlobalCounterService();
        }

        public async Task<RecipeParameters.Counter> GetCurrentCountersAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            return _recipeParam.Config.Counter;
        }

        public async Task IncrementTotalAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.Total++;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("TOTAL", 1);
            MarkDirty();
        }

        public async Task IncrementCompliantAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
             MainWindow._produzioneRecord.EsitoClassificazione = 1;
            _recipeParam.Config.Counter.Compliant++;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("GOOD", 1);
            MarkDirty();
        }

        public async Task IncrementNotCompliantAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.Not_Compliant++;
             MainWindow._produzioneRecord.EsitoClassificazione = 0;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("NOGOOD", 1);
            MarkDirty();
        }

        public async Task IncrementLogoDefectsAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.logo++;
            MainWindow._produzioneRecord.NcLoghiImmagini = 0;

            // Reset the logo defect counter in the production record
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("LOGO", 1);
            MarkDirty();
        }

        public async Task IncrementPrintCenteringDefectsAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.Print_centering++;
            MainWindow._produzioneRecord.NcCentraturaLogo = 0;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);

            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("PRINT_CENTERING", 1);
            MarkDirty();
        }

        public async Task IncrementOpenFlapsDefectsAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.OpenFlaps++;
            MainWindow._produzioneRecord.NcAletteAperte = 0;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);

            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("OPEN_FLAPS", 1);
            MarkDirty();
        }

        public async Task IncrementSurfaceCheckDefectsAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.SurfaceCheck++;
            MainWindow._produzioneRecord.NCSurfaceCheck = 0;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("SURFACE_CHECK", 1);
            MarkDirty();
        }

        public async Task IncrementHeightDefectsAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.Height++;
            MainWindow._produzioneRecord.NcHeigth = 0;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("HEIGHT", 1);
            MarkDirty();
        }

        public async Task IncrementSideSealingDefectsAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.Side_sealing++;
            MainWindow._produzioneRecord.NcSaldaturaLaterale = 0;
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);

            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("SIDE_SEALING", 1);
            MarkDirty();
        }
        public async Task IncrementShapeDefectsAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.ShapeTop++;
            MainWindow._produzioneRecord.NcShapeTop = 0;

            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("SHAPE_TOP", 1);
            MarkDirty();
        }
        public async Task IncrementShapeDefectsSideAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter.ShapeSide++;
            MainWindow._produzioneRecord.NcShapeBottom = 0;

            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);
            // Aggiorna anche il contatore globale
            await _globalCounterService.IncrementCounterAsync("SHAPE_SIDE", 1);
            MarkDirty();
        }

        public async Task ResetAllCountersAsync()
        {
            await _recipeParam.EnsureLoadedAsync();
            _recipeParam.Config.Counter = new RecipeParameters.Counter();
            await _recipeParam.UpdateCounterAsync(_recipeParam.Config.Counter);

            // Resetta anche i contatori globali
            await _globalCounterService.ResetAllCountersAsync();

            MarkDirty();
        }
        public void MarkDirty()
        {
            _isDirty = true;
        }

        public void ResetDirtyFlag()
        {
            _isDirty = false;
        }
    }
}
