using CognexVisionStartup = Cognex.Vision.Startup;
using NLog;
using NLog.Config;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views;
using System;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QtisVisionPanel
{
    /// <summary>
    /// Logica di interazione per App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex _mutex;
        private const string mutexName = "QtisVisionPanelSingleInstanceMutex";
        private static int _forcedExitScheduled;

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static class ViewModelCache
        {
            public static RecipeManagerViewModel RecipeManagerVM { get; set; }
            public static MainViewModel MainVM { get; set; }
            public static DigitalIOViewModel DigitalIOVM { get; set; }
            public static bool IsInitialized { get; set; }
        }

        /// <summary>
        /// Industrial safety net for shutdown.
        ///
        /// If native acquisition/COM resources keep the process alive after the
        /// normal WPF shutdown sequence, this watchdog forces the process to
        /// terminate after a grace period.
        /// </summary>
        public static void ScheduleForcedProcessTermination(string reason, int exitCode = 0, int timeoutMs = 15000)
        {
            if (Interlocked.Exchange(ref _forcedExitScheduled, 1) == 1)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(timeoutMs).ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    ServiceLocator.ApplicationEventLogger.LogLifecycle(
                        "FORCED_PROCESS_EXIT",
                        $"Forced process termination after shutdown timeout. Reason: {reason}",
                        nameof(ScheduleForcedProcessTermination));
                }
                catch
                {
                }

                try
                {
                    CognexVisionStartup.Shutdown();
                }
                catch
                {
                }

                try
                {
                    LogManager.Flush();
                    LogManager.Shutdown();
                }
                catch
                {
                }

                Environment.Exit(exitCode);
            });
        }

        /// <summary>
        /// Riduce le pause del garbage collector, che sospendono l'intero processo e
        /// spostano la quota a cui vengono alzate le uscite di trigger.
        ///
        /// Server GC si attiva SOLO da QtisVisionPanel.exe.config e quel file va copiato
        /// insieme all'eseguibile: distribuendo il solo .exe la macchina resta su
        /// Workstation GC. Qui si verifica e si segnala, e si imposta comunque via codice
        /// SustainedLowLatency, che evita le gen2 bloccanti e viaggia con l'eseguibile.
        /// </summary>
        private void ConfigureGarbageCollectorLatency()
        {
            var logger = NLog.LogManager.GetCurrentClassLogger();

            try
            {
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            }
            catch (Exception ex)
            {
                // Non supportata in alcune configurazioni di hosting: non e' fatale.
                logger.Warn(ex, "GC_LATENCY_MODE_NOT_APPLIED");
            }

            if (!GCSettings.IsServerGC)
            {
                logger.Warn(
                    "GC_SERVER_DISABLED|Server GC non attivo: QtisVisionPanel.exe.config non e' stato " +
                    "distribuito insieme all'eseguibile. Le collection gen2 restano bloccanti e " +
                    "spostano la quota dei trigger camera. Copiare il file accanto all'exe.");
            }

            logger.Info($"GC_CONFIG|server={GCSettings.IsServerGC}|latency={GCSettings.LatencyMode}");
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            bool isNewInstance;

            ConfigureNLog();
            RegisterGlobalExceptionHandling();
            ConfigureGarbageCollectorLatency();
            ServiceLocator.ApplicationEventLogger.LogLifecycle("APP_STARTING", "Application startup requested", nameof(Application_Startup));

            _mutex = new Mutex(true, mutexName, out isNewInstance);
            try
            {
                if (!isNewInstance)
                {
                    IntPtr hWnd = FindWindow(null, "Qtis Vision Panel");
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                    else
                    {
                        new SystemNotificationWindow("Informazione", "L'applicazione è già in esecuzione.", NotificationSeverity.Info).ShowDialog();
                    }

                    Shutdown();
                    return;
                }

                // Cognex requires VProX initialization before any VisionPro type,
                // ToolBlock or ViDi EL template is used by the application.
                Services.VisionProRuntimeBootstrap.EnsureInitialized();

                var splash = new SplashWindow();
                splash.Show();
                await PreloadViewModelsAsync(splash);
                await splash.StartInitializationAsync();
            }
            catch (Exception ex)
            {
                ServiceLocator.ApplicationEventLogger.LogException("APP_STARTUP_FAILED", ex, nameof(Application_Startup), "Errore durante l'avvio dell'applicazione");
                new SystemNotificationWindow("Errore", "Errore durante l'avvio dell'applicazione: " + ex.Message, NotificationSeverity.Error).ShowDialog();
                Shutdown();
            }
        }

        private async Task PreloadViewModelsAsync(SplashWindow splash)
        {
            try
            {
                await splash.Dispatcher.InvokeAsync(() =>
                    splash.UpdateStatus("Precaricamento ViewModel...", 5));

                await SplashWindow.LoadConfigFileAsync();

                // DigitalIOViewModel must be created on the UI thread (uses DispatcherTimer).
                // Instantiation fires InitializeAsync internally which starts I/O hardware
                // in the background, so the board is ready before the first camera view.
                ViewModelCache.DigitalIOVM = new DigitalIOViewModel();

                ViewModelCache.MainVM = new MainViewModel();

                await splash.Dispatcher.InvokeAsync(() =>
                    splash.UpdateStatus("Caricamento ricette...", 10));

                ViewModelCache.RecipeManagerVM = new RecipeManagerViewModel();

                await Task.Run(async () =>
                {
                    await ViewModelCache.RecipeManagerVM.InitializeAsync();
                });

                await PreloadRecipeImagesAsync();

                ViewModelCache.IsInitialized = true;
            }
            catch (Exception ex)
            {
                ServiceLocator.ApplicationEventLogger.LogException("PRELOAD_VIEWMODELS_FAILED", ex, nameof(PreloadViewModelsAsync), "Errore durante il precaricamento dei ViewModel");
            }
        }

        private async Task PreloadRecipeImagesAsync()
        {
            var recipes = ViewModelCache.RecipeManagerVM.Recipes;
            if (recipes == null || recipes.Count == 0)
                return;

            var semaphore = new SemaphoreSlim(4);

            var loadTasks = recipes
                .Where(r => !string.IsNullOrEmpty(r.ImagePath) && File.Exists(r.ImagePath))
                .Select(async recipe =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await Task.Run(() =>
                        {
                            try
                            {
                                byte[] imageData = File.ReadAllBytes(recipe.ImagePath);
                                using (var stream = new MemoryStream(imageData))
                                {
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.StreamSource = stream;
                                    bitmap.EndInit();
                                    bitmap.Freeze();

                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        recipe.CachedImage = bitmap;
                                    });
                                }
                            }
                            catch
                            {
                            }
                        });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

            await Task.WhenAll(loadTasks);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                ServiceLocator.ApplicationEventLogger.LogLifecycle("APP_EXIT", "Application exit", nameof(OnExit));
            }
            catch
            {
            }

            try
            {
                ViewModelCache.MainVM?.Dispose();
            }
            catch
            {
            }

            try
            {
                ViewModelCache.DigitalIOVM?.Dispose();
            }
            catch
            {
            }

            ViewModelCache.MainVM = null;
            ViewModelCache.RecipeManagerVM = null;
            ViewModelCache.DigitalIOVM = null;
            ViewModelCache.IsInitialized = false;

            try
            {
                ServiceLocator.Reset();
            }
            catch
            {
            }

            try
            {
                CognexVisionStartup.Shutdown();
            }
            catch
            {
            }

            _mutex?.ReleaseMutex();
            _mutex = null;
            LogManager.Flush();
            LogManager.Shutdown();
            base.OnExit(e);
        }

        private void ConfigureNLog()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nlog.config");
                if (File.Exists(configPath))
                {
                    LogManager.Configuration = new XmlLoggingConfiguration(configPath);
                }
            }
            catch
            {
            }
        }

        private void RegisterGlobalExceptionHandling()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ServiceLocator.ApplicationEventLogger.LogException(
                "UI_UNHANDLED_EXCEPTION",
                e.Exception,
                nameof(OnDispatcherUnhandledException),
                "Unhandled UI exception");

            e.Handled = true;

            new SystemNotificationWindow("Errore applicazione",
                $"Si è verificato un errore non gestito:\n{e.Exception.Message}",
                NotificationSeverity.Error).ShowDialog();
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception ?? new Exception("Unknown AppDomain exception");
            ServiceLocator.ApplicationEventLogger.LogException(
                "APPDOMAIN_UNHANDLED_EXCEPTION",
                exception,
                nameof(OnCurrentDomainUnhandledException),
                "Unhandled AppDomain exception");
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            ServiceLocator.ApplicationEventLogger.LogException(
                "TASK_UNOBSERVED_EXCEPTION",
                e.Exception,
                nameof(OnUnobservedTaskException),
                "Unobserved task exception");

            e.SetObserved();
        }
    }
}
