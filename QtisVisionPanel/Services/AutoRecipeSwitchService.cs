using Cognex.VisionPro;
using NLog;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.RecipeSwitch;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Threading;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Handles automatic recipe switch attempts after repeated inspection failures.
    /// MainWindow keeps only the inspection event hook; this service owns counters,
    /// timeout, visual matching, operator confirmation and guarded recipe reload.
    /// </summary>
    public sealed class AutoRecipeSwitchService : IDisposable
    {
        private const int FallbackMaxConsecutiveFailures = 5;
        private const int AutoSwitchTimeoutMs = 30000;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly System.Timers.Timer _autoSwitchTimer;
        private readonly ApplicationEventLogger _applicationEventLogger;
        private MainWindow _mainWindow;
        private int _consecutiveFailures;
        private bool _isAutoSwitching;
        private bool _disposed;

        public AutoRecipeSwitchService()
        {
            _applicationEventLogger = ServiceLocator.ApplicationEventLogger;
            _autoSwitchTimer = new System.Timers.Timer(AutoSwitchTimeoutMs)
            {
                AutoReset = false
            };
            _autoSwitchTimer.Elapsed += OnAutoSwitchTimeout;
        }

        public void AttachMainWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        public void OnInspectionResult(string status, ICogImage currentImage)
        {
            if (_disposed)
            {
                return;
            }

            if (_isAutoSwitching)
            {
                _autoSwitchTimer.Stop();
                _autoSwitchTimer.Start();
                return;
            }

            if (string.Equals(status, "Fail", StringComparison.OrdinalIgnoreCase))
            {
                var maxConsecutiveFailures = ResolveMaxConsecutiveFailures();
                _consecutiveFailures++;
                Logger.Info("AUTO_RECIPE_SWITCH_FAILURE_COUNT|count={0}|threshold={1}",
                    _consecutiveFailures,
                    maxConsecutiveFailures);

                if (_consecutiveFailures >= maxConsecutiveFailures)
                {
                    _autoSwitchTimer.Start();
                    _isAutoSwitching = true;
                    Task.Run(() => AttemptAutoRecipeSwitchAsync(currentImage)).SafeFireAndForget(
                        ex => Logger.Error(ex, "AUTO_RECIPE_SWITCH_TASK_FAILED"));
                    _consecutiveFailures = 0;
                }

                return;
            }

            _consecutiveFailures = 0;
            _autoSwitchTimer.Stop();
        }

        private static int ResolveMaxConsecutiveFailures()
        {
            var configuredValue = MainWindow.configManager?.Config?.AutoSwitchSettings?.MaxConsecutiveFailures
                ?? FallbackMaxConsecutiveFailures;

            if (configuredValue > 0)
            {
                return configuredValue;
            }

            Logger.Warn("AUTO_RECIPE_SWITCH_INVALID_MAX_FAILURES|configured={0}|fallback={1}",
                configuredValue,
                FallbackMaxConsecutiveFailures);
            return FallbackMaxConsecutiveFailures;
        }

        private void OnAutoSwitchTimeout(object sender, ElapsedEventArgs e)
        {
            var dispatcher = ResolveDispatcher();
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isAutoSwitching)
                {
                    return;
                }

                Logger.Warn("AUTO_RECIPE_SWITCH_TIMEOUT");
                _isAutoSwitching = false;
                _consecutiveFailures = 0;

                if (!ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
                {
                    MainWindow._continuousRunCts = new CancellationTokenSource();
                    ServiceLocator.MachineRuntimeService
                        .StartContinuousRunAsync(MainWindow._continuousRunCts.Token)
                        .SafeFireAndForget(false);
                }
            }), DispatcherPriority.Background);
        }

        private async Task AttemptAutoRecipeSwitchAsync(ICogImage badImage)
        {
            try
            {
                await StopContinuousRunIfNeededAsync().ConfigureAwait(false);

                var imageDir = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder;
                if (string.IsNullOrWhiteSpace(imageDir))
                {
                    Logger.Error("AUTO_RECIPE_SWITCH_ABORTED|reason=recipe_folder_missing");
                    return;
                }

                Logger.Info("AUTO_RECIPE_SWITCH_SEARCH_START|folder={0}", imageDir);
                var advancedSwitcher = new AdvancedRecipeAutoSwitcher();
                var foundRecipeName = await Task.Run(() =>
                    advancedSwitcher.FindMatchingRecipeWithFallback(badImage, imageDir)).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(foundRecipeName))
                {
                    Logger.Warn("AUTO_RECIPE_SWITCH_NO_MATCH");
                    return;
                }

                var currentRecipe = Path.GetFileNameWithoutExtension(
                    MainWindow.configManager?.Config?.Configuration?.LastRecipe ?? string.Empty);
                var normalizedFoundRecipe = Path.GetFileNameWithoutExtension(foundRecipeName);
                if (string.Equals(currentRecipe, normalizedFoundRecipe, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("AUTO_RECIPE_SWITCH_MATCH_IS_CURRENT|recipe={0}", normalizedFoundRecipe);
                    return;
                }

                Logger.Info("AUTO_RECIPE_SWITCH_MATCH_FOUND|current={0}|suggested={1}",
                    currentRecipe,
                    normalizedFoundRecipe);

                var confirmed = await ConfirmRecipeSwitchAsync(normalizedFoundRecipe).ConfigureAwait(false);
                if (!confirmed)
                {
                    Logger.Info("AUTO_RECIPE_SWITCH_REJECTED_BY_OPERATOR|recipe={0}", normalizedFoundRecipe);
                    return;
                }

                await ChangeRecipeAndInitializeAsync(normalizedFoundRecipe).ConfigureAwait(false);
                _consecutiveFailures = 0;
                Logger.Info("AUTO_RECIPE_SWITCH_COMPLETED|recipe={0}", normalizedFoundRecipe);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "AUTO_RECIPE_SWITCH_FAILED");
                await ShowErrorAsync(ex).ConfigureAwait(false);
            }
            finally
            {
                _isAutoSwitching = false;
                await RestartContinuousRunIfNeededAsync().ConfigureAwait(false);
            }
        }

        private async Task StopContinuousRunIfNeededAsync()
        {
            if (!ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
            {
                return;
            }

            await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync().ConfigureAwait(false);
            MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
            await Task.Delay(500).ConfigureAwait(false);
        }

        private async Task RestartContinuousRunIfNeededAsync()
        {
            if (ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
            {
                return;
            }

            MainWindow._continuousRunCts = new CancellationTokenSource();
            await ServiceLocator.MachineRuntimeService
                .StartContinuousRunAsync(MainWindow._continuousRunCts.Token)
                .ConfigureAwait(false);
            MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
            await Task.Delay(1000).ConfigureAwait(false);
        }

        private Task<bool> ConfirmRecipeSwitchAsync(string foundRecipeName)
        {
            var dispatcher = ResolveDispatcher();
            return dispatcher.InvokeAsync(() =>
            {
                var dlg = new SystemNotificationWindow(
                    "Conferma cambio ricetta",
                    $"Rilevati scarti consecutivi.\n\nLa ricetta '{foundRecipeName}' sembra corrispondere al prodotto.\nVuoi caricare questa ricetta?",
                    NotificationSeverity.Warning,
                    true);
                dlg.ShowDialog();
                return dlg.Confirmed;
            }).Task;
        }

        private async Task ChangeRecipeAndInitializeAsync(string recipeName)
        {
            var dispatcher = ResolveDispatcher();
            await dispatcher.InvokeAsync(() => ChangeRecipeAndInitializeOnUiAsync(recipeName)).Task.Unwrap();
        }

        private async Task ChangeRecipeAndInitializeOnUiAsync(string recipeName)
        {
            var mainWindow = ResolveMainWindow();
            var recipeWithExtension = EnsureVppExtension(recipeName);

            _applicationEventLogger.LogRecipeEvent(
                "AUTO_RECIPE_CHANGE_REQUESTED",
                recipeWithExtension,
                nameof(AutoRecipeSwitchService),
                "Automatic recipe change requested after repeated inspection failures");

            if (ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
            {
                await ServiceLocator.MachineRuntimeService.StopContinuousRunAsync();
                MainWindow._isContinuousRunActive = ServiceLocator.MachineRuntimeService.IsContinuousRunActive;
                await Task.Delay(1000);
            }

            MainWindow.configManager.Config.Configuration.LastRecipe = recipeWithExtension;
            await MainWindow.configManager.SaveConfigAsync();

            await mainWindow.InitializeRecipeAsync(recipeWithExtension);
            await mainWindow.InitializeComponentforChangeRecipe();

            if (mainWindow.DataContext is MainViewModel mainVm)
            {
                mainVm.UpdateControlButtonsState();
                mainVm.MarkSystemAsReady();
                mainVm.TopMenuBarVM?.MarkSystemAsReady();
            }

            _applicationEventLogger.LogRecipeEvent(
                "AUTO_RECIPE_CHANGE_COMPLETED",
                recipeWithExtension,
                nameof(AutoRecipeSwitchService),
                "Automatic recipe change completed");
        }

        private Task ShowErrorAsync(Exception ex)
        {
            var dispatcher = ResolveDispatcher();
            return dispatcher.InvokeAsync(() =>
            {
                new SystemNotificationWindow(
                    "Errore",
                    $"Errore nel cambio ricetta: {ex.Message}",
                    NotificationSeverity.Error).ShowDialog();
            }).Task;
        }

        private MainWindow ResolveMainWindow()
        {
            return _mainWindow ?? MainWindow.MainView
                ?? throw new InvalidOperationException("MainWindow not available for automatic recipe switch.");
        }

        private Dispatcher ResolveDispatcher()
        {
            return ResolveMainWindow().Dispatcher;
        }

        private static string EnsureVppExtension(string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                return recipeName;
            }

            return string.Equals(Path.GetExtension(recipeName), ".vpp", StringComparison.OrdinalIgnoreCase)
                ? recipeName
                : recipeName + ".vpp";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _autoSwitchTimer.Stop();
            _autoSwitchTimer.Elapsed -= OnAutoSwitchTimeout;
            _autoSwitchTimer.Dispose();
        }
    }
}
