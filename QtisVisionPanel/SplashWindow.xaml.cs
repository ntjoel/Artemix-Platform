using QtisVisionPanel.Cls_Config;
using QtisVisionPanel.Cls_Vpro;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Services;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QtisVisionPanel
{
    /// <summary>
    /// Logica di interazione per Window1.xaml
    /// </summary>
    public partial class SplashWindow : Window
    {
        public static SplashWindow splash;
        public SplashWindow()
        {
            InitializeComponent();
            splash = this;
        }

        public async Task StartInitializationAsync()
        {
            bool lockAcquired = false;
            try
            {

                await MainWindow._operationLock.WaitAsync(MainWindow._cts.Token);
                lockAcquired = true;

                int progress = 0;
               

                await UpdateStatusAsync("licence presence reading ...", progress += 5);
                await InitializeCognex();


                await UpdateStatusAsync("Loading configuration file...", progress += 10);
                await LoadConfigFileAsync();

                //await MainWindow.configManager.EnsureLoadedAsync();
                // **AUTO-LOGIN: Imposta Guest/Viewer di default**
                await UpdateStatusAsync("set default user...", progress += 5);
                SetupDefaultUser();

                // **1. INIZIALIZZA SERVICE LOCATOR PER PRIMO**
                await UpdateStatusAsync("Initialisation of locator services...", progress += 5);
                ServiceLocator.Initialize();
                await UpdateStatusAsync("Initialisation of the vision system library...", progress += 5);
                MainWindow.loadVsionPro();
                // Step 2: Crea l'istanza principale
                await UpdateStatusAsync("Main application creation...", progress += 5);
                MainWindow.MainView = new MainWindow();
                Application.Current.MainWindow = MainWindow.MainView;
                // Step 3: Carica la configurazione
              

                // Step 4: Inizializza il database
                await UpdateStatusAsync("Database initialisation...", progress += 15);
                await MainWindow.MainView.InitializeDatabaseAsync();

                // Health check — puramente informativo, non blocca l'avvio
                var healthReport = await new Services.StartupHealthChecker().RunAsync(
                    new Progress<string>(msg => UpdateStatus(msg, progress)));
                await UpdateStatusAsync(healthReport.SummaryText, progress);

                // Dopo aver caricato la configurazione, carica le feature abilitate
                await UpdateStatusAsync("Loading inspection configuration...", progress += 5);
                await MainWindow.MainView.LoadInspectionConfigurationAsync();

                // Step 5: Carica la ricetta predefinita
                await UpdateStatusAsync("Loading recipe...", progress += 15);
                await MainWindow.MainView.InitializeRecipeAsync();

              

                // Step 7: Inizializza l'accesso al database utenti
                await UpdateStatusAsync("User configuration...", progress += 15);
                await MainWindow.MainView.UserdbInitialiseAsync();

                // Step 8: Inizializza i componenti UI
                await UpdateStatusAsync("Interface initialisation...", progress += 15);
               

                // Step 9: Completamento
                await UpdateStatusAsync("Start-up complete", progress += 5);


                
                MainWindow.MainView.Show();
                // Marca il sistema come inizializzato
                if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
                {
                    mainVm.TopMenuBarVM?.MarkSystemAsReady();
                }
                MainWindow.logger.Info("Applicazione avviata con successo");
                ServiceLocator.ApplicationEventLogger.LogLifecycle("APP_STARTED", "Applicazione avviata con successo", nameof(StartInitializationAsync));
                await Task.Delay(50); // breve pausa per mostrare il completamento


            }
            catch (Exception ex)
            {
                MainWindow.logger.Error("Errore durante l'inizializzazione: " + ex.Message + "\n" + ex.StackTrace);
                ServiceLocator.ApplicationEventLogger.LogException("APP_INITIALIZATION_FAILED", ex, nameof(StartInitializationAsync), "Errore durante l'inizializzazione applicativa");
                new SystemNotificationWindow("Errore", "Errore durante l'inizializzazione: " + ex.InnerException, NotificationSeverity.Error).ShowDialog();
                Application.Current.Shutdown();
            }
            finally
            {
               // await MainWindow.MainView.InitializeUIComponentsAsync();
                if (lockAcquired)
                    MainWindow._operationLock.Release();
               // MainWindow.MainView.InitializeUIComponentsAsync().SafeFireAndForget();
                this.Close();
            }
        }
       


        private async Task UpdateStatusAsync(string message, int progress)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = message;
                ProgressBar.Value = progress;
            });
            await Task.Delay(100);
        }
        public Task InitializeCognex()
        {
            VisionProRuntimeBootstrap.EnsureInitialized();
            return Task.CompletedTask;
        }

        private void SetupDefaultUser()
        {
            try
            {
                UserSession.ApplyStartupSessionFromConfig();
                MainWindow.logger.Info($"Utente di default impostato: {UserSession.CurrentUser} ({UserSession.CurrentRole})");

                // Persist the chosen startup mode so the UI and recovery logic remain aligned.
                if (MainWindow.configManager?.Config?.Configuration != null)
                {
                    MainWindow.configManager.Config.Configuration.User = UserSession.CurrentUser;
                    MainWindow.configManager.Config.Configuration.CurrentUserRole = UserSession.CurrentRole;

                    MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Errore nel setup utente: {ex.Message}");
                UserSession.CurrentUser = "Guest";
                UserSession.CurrentRole = "Viewer";
            }
        }
       
        public void UpdateStatus(string message, int progress)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                ProgressBar.Value = progress;
            });
        }
        public static async Task LoadConfigFileAsync()
        {
            try
            {
                await MainWindow.configManager.EnsureLoadedAsync();
                // UpdateStatus("Configuration loaded", 20);

                if (MainWindow.configManager?.Config?.Configuration != null)
                {
                    MainWindow.language = MainWindow.configManager.Config.Configuration.Language;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error loading configuration: {ex.Message}");
               // UpdateStatus("Error loading configuration", 20);
            }
        }

        }
}
