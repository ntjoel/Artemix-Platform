using FontAwesome.WPF;
using QtisVisionPanel.Cls_Config;
using QtisVisionPanel.Views;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QtisVisionPanel.ViewModels
{
    public class PreferenceViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AsyncPreferenceConfigManager _configManager;
        private Cls_CheckUser _userManager;

        // Language
        private ObservableCollection<LanguageInfo> _availableLanguages;
        private LanguageInfo _selectedLanguage;
        private string _flagImagesPath;
        private string _languageFilesPath;

        // System
        private string _savePath;
        private bool _autoSaveEnabled;
        private int _autoSaveInterval;

        // User Management
        private ObservableCollection<UserViewModel> _existingUsers;
        private string _newUsername;
        private string _newPassword;
        private string _confirmPassword;
        private ObservableCollection<string> _availableRoles;
        private string _selectedRole;
        private bool _isAdministrator;

        // UI State
        private bool _isLoading;
        private string _statusMessage;
        private Brush _statusColor;

        public PreferenceViewModel()
        {
            _configManager = new AsyncPreferenceConfigManager();

            // Inizializza UserManager con parametri da config principale
            var mysqlConfig = MainWindow.configManager?.Config?.MySqlConnection;
            if (mysqlConfig != null)
            {
                _userManager = new Cls_CheckUser(
                    mysqlConfig.Host,
                    mysqlConfig.Db,
                    mysqlConfig.User,
                    mysqlConfig.Password,
                    mysqlConfig.port,
                    mysqlConfig.SslDisabled);
            }

            AvailableLanguages = new ObservableCollection<LanguageInfo>();
            ExistingUsers = new ObservableCollection<UserViewModel>();
            AvailableRoles = new ObservableCollection<string>();

            // Commands
            SaveLanguageCommand = new RelayCommand(async _ => await SaveLanguageSettingsAsync());
            SaveSystemCommand = new RelayCommand(async _ => await SaveSystemSettingsAsync());
            BrowsePathCommand = new RelayCommand(_ => BrowseSavePath());
            AddUserCommand = new RelayCommand(async _ => await AddNewUserAsync(), _ => CanAddUser());
            RefreshUsersCommand = new RelayCommand(async _ => await RefreshUserManagementAsync());
            DeleteUserCommand = new RelayCommand<UserViewModel>(async user => await DeleteUserAsync(user));
            TestPathsCommand = new RelayCommand(_ => TestLanguagePaths());
            ReloadConfigFileCommand = new RelayCommand(async _ => await LoadMainConfigFileAsync(), _ => CanManageConfigFile);
            SaveConfigFileCommand = new RelayCommand(async _ => await SaveMainConfigFileAsync(), _ => CanManageConfigFile && RuntimeConfiguration != null && RuntimeMySqlConnection != null);
            UserSession.OnRoleChanged += OnUserRoleChanged;

            _ = InitializeAsync();
        }

        #region Properties

        public ObservableCollection<LanguageInfo> AvailableLanguages
        {
            get => _availableLanguages;
            set { _availableLanguages = value; OnPropertyChanged(); }
        }

        public LanguageInfo SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                _selectedLanguage = value;
                OnPropertyChanged();
                UpdateLanguagePreview();
            }
        }

        public string FlagImagesPath
        {
            get => _flagImagesPath;
            set { _flagImagesPath = value; OnPropertyChanged(); }
        }

        public string LanguageFilesPath
        {
            get => _languageFilesPath;
            set { _languageFilesPath = value; OnPropertyChanged(); }
        }

        public string SavePath
        {
            get => _savePath;
            set { _savePath = value; OnPropertyChanged(); }
        }

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set { _autoSaveEnabled = value; OnPropertyChanged(); }
        }

        public int AutoSaveInterval
        {
            get => _autoSaveInterval;
            set { _autoSaveInterval = value; OnPropertyChanged(); }
        }

        public ObservableCollection<UserViewModel> ExistingUsers
        {
            get => _existingUsers;
            set { _existingUsers = value; OnPropertyChanged(); }
        }

        public string NewUsername
        {
            get => _newUsername;
            set
            {
                _newUsername = value;
                OnPropertyChanged();
                (AddUserCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string NewPassword
        {
            get => _newPassword;
            set
            {
                _newPassword = value;
                OnPropertyChanged();
                (AddUserCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set
            {
                _confirmPassword = value;
                OnPropertyChanged();
                (AddUserCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<string> AvailableRoles
        {
            get => _availableRoles;
            set { _availableRoles = value; OnPropertyChanged(); }
        }

        public string SelectedRole
        {
            get => _selectedRole;
            set
            {
                _selectedRole = value;
                OnPropertyChanged();
                (AddUserCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsAdministrator
        {
            get => _isAdministrator;
            private set
            {
                _isAdministrator = value;
                OnPropertyChanged();
            }
        }

        public bool CanManageConfigFile => UserSession.IsAdministrator || UserSession.IsInstaller;

        public string MainConfigFilePath => @"C:\QtisVision\cfg\Config.xml";

        public Configuration RuntimeConfiguration => MainWindow.configManager?.Config?.Configuration;

        public MySqlConnectionSettings RuntimeMySqlConnection    => MainWindow.configManager?.Config?.MySqlConnection;
        public IO RuntimeIO                                        => MainWindow.configManager?.Config?.IO;
        public SaveImagePercentage RuntimeSaveImage                => MainWindow.configManager?.Config?._SaveImagePercentage;
        // Percorsi del sub-record VisionPro mostrato a display per ciascun canale. Erano
        // modificabili solo da una copia del progetto rimasta fuori dal tracciamento a luglio
        // (43f865b): da allora il valore esisteva in Config.xml ma nessuna schermata lo
        // esponeva. E' lo stesso valore che governa DISPLAY_RECORD_PATH_NOT_FOUND e quindi
        // cosa finisce nell'immagine salvata.
        public LasRunParam RuntimeLastRunViews                     => MainWindow.configManager?.Config?.LasRunParam;
        public AutoSwitchSettings RuntimeAutoSwitch                => MainWindow.configManager?.Config?.AutoSwitchSettings;
        public SystemDiagnosticsSettings RuntimeDiagnostics        => MainWindow.configManager?.Config?.SystemDiagnostics;
        public string[] TemperatureProviderOptions                 => new[] { "Auto", "LibreHardwareMonitor", "WindowsWmi" };
        public PowerFlex525Settings RuntimePowerFlex               => MainWindow.configManager?.Config?.PowerFlex525;
        public AnalyticsDashboardSettings RuntimeAnalytics         => MainWindow.configManager?.Config?.AnalyticsDashboard;

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public ImageSource CurrentFlagImage => GetFlagImage(SelectedLanguage?.FlagImage);

        #endregion

        #region Commands

        public ICommand SaveLanguageCommand { get; }
        public ICommand SaveSystemCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand AddUserCommand { get; }
        public ICommand RefreshUsersCommand { get; }
        public ICommand DeleteUserCommand { get; }
        public ICommand TestPathsCommand { get; }
        public ICommand ReloadConfigFileCommand { get; }
        public ICommand SaveConfigFileCommand { get; }

        #endregion

        private async Task InitializeAsync()
        {
            IsLoading = true;
            StatusMessage = "Caricamento preferenze...";
            StatusColor = Brushes.Orange;

            try
            {
                await _configManager.EnsureLoadedAsync();
                var config = _configManager.Config;

                // Load Language Settings
                FlagImagesPath = config.LanguageSettings.Paths.FlagImagesPath;
                LanguageFilesPath = config.LanguageSettings.Paths.LanguageFilesPath;

                AvailableLanguages.Clear();
                foreach (var lang in config.LanguageSettings.AvailableLanguages)
                {
                    // Imposta il riferimento al parent per il calcolo del path completo
                    AvailableLanguages.Add(lang);
                }

                SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == config.LanguageSettings.CurrentLanguage)
                    ?? AvailableLanguages.FirstOrDefault();

                // Load System Settings
                SavePath = config.SystemSettings.SavePath;
                AutoSaveEnabled = config.SystemSettings.AutoSaveEnabled;
                AutoSaveInterval = config.SystemSettings.AutoSaveIntervalMinutes;

                RefreshAdministratorState();

                if (CanManageConfigFile)
                {
                    await LoadMainConfigFileAsync();
                }

                if (IsAdministrator)
                {
                    await RefreshUserManagementAsync();
                }

                StatusMessage = "Preferenze caricate con successo";
                StatusColor = Brushes.Green;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore: {ex.Message}";
                StatusColor = Brushes.Red;
                MainWindow.logger?.Error($"Errore inizializzazione PreferenceViewModel: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadExistingUsersAsync()
        {
            try
            {
                ExistingUsers.Clear();
                if (!IsAdministrator || _userManager == null)
                {
                    return;
                }

                var users = await _userManager.GetUsersAsync();
                foreach (var user in users)
                {
                    ExistingUsers.Add(new UserViewModel
                    {
                        Username = user.Username,
                        Role = user.Role,
                        CreatedDate = DateTime.Now,
                        IsActive = true
                    });
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore caricamento utenti: {ex.Message}");
            }
        }

        private async Task LoadAvailableRolesAsync()
        {
            AvailableRoles.Clear();

            var roles = Enumerable.Empty<string>();

            if (_userManager != null)
            {
                try
                {
                    roles = await _userManager.GetAvailableRolesAsync();
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn($"Errore caricamento ruoli da database: {ex.Message}");
                }
            }

            if (!roles.Any())
            {
                roles = _configManager.Config != null
                    ? (_configManager.Config.UserManagementSettings.AvailableRoles ?? Enumerable.Empty<string>())
                    : Enumerable.Empty<string>();
            }

            foreach (var role in roles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AvailableRoles.Add(role);
            }

            var preferredRole = _configManager.Config != null
                ? _configManager.Config.UserManagementSettings.DefaultRole
                : null;
            SelectedRole = AvailableRoles.FirstOrDefault(role => string.Equals(role, preferredRole, StringComparison.OrdinalIgnoreCase))
                ?? AvailableRoles.FirstOrDefault();
        }

        private async Task RefreshUserManagementAsync()
        {
            if (!IsAdministrator)
            {
                return;
            }

            await LoadAvailableRolesAsync();
            await LoadExistingUsersAsync();
        }

        private void RefreshAdministratorState()
        {
            IsAdministrator = UserSession.IsAdministrator;
            OnPropertyChanged(nameof(CanManageConfigFile));
            OnPropertyChanged(nameof(MainConfigFilePath));
            OnPropertyChanged(nameof(RuntimeConfiguration));
            OnPropertyChanged(nameof(RuntimeMySqlConnection));
            OnPropertyChanged(nameof(RuntimeIO));
            OnPropertyChanged(nameof(RuntimeSaveImage));
            OnPropertyChanged(nameof(RuntimeAutoSwitch));
            OnPropertyChanged(nameof(RuntimeDiagnostics));
            OnPropertyChanged(nameof(RuntimePowerFlex));
            OnPropertyChanged(nameof(RuntimeAnalytics));
            (AddUserCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ReloadConfigFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SaveConfigFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void RebuildUserManager()
        {
            var mysqlConfig = MainWindow.configManager?.Config?.MySqlConnection;
            if (mysqlConfig == null)
            {
                _userManager = null;
                return;
            }

            _userManager = new Cls_CheckUser(
                mysqlConfig.Host,
                mysqlConfig.Db,
                mysqlConfig.User,
                mysqlConfig.Password,
                mysqlConfig.port,
                mysqlConfig.SslDisabled);
        }

        private async void OnUserRoleChanged(object sender, EventArgs e)
        {
            RefreshAdministratorState();

            if (CanManageConfigFile)
            {
                await LoadMainConfigFileAsync();
            }
            else
            {
                OnPropertyChanged(nameof(RuntimeConfiguration));
                OnPropertyChanged(nameof(RuntimeMySqlConnection));
            }

            if (IsAdministrator)
            {
                await RefreshUserManagementAsync();
            }
            else
            {
                ExistingUsers.Clear();
                AvailableRoles.Clear();
                SelectedRole = null;
                ClearNewUserFields();
            }
        }

        private async Task LoadMainConfigFileAsync()
        {
            if (!CanManageConfigFile)
            {
                return;
            }

            try
            {
                if (MainWindow.configManager == null)
                {
                    MainWindow.configManager = new AsyncConfigManagerXml(MainConfigFilePath);
                }

                await MainWindow.configManager.EnsureLoadedAsync();
                RebuildUserManager();
                OnPropertyChanged(nameof(RuntimeConfiguration));
                OnPropertyChanged(nameof(RuntimeMySqlConnection));
                OnPropertyChanged(nameof(RuntimeIO));
                OnPropertyChanged(nameof(RuntimeSaveImage));
                OnPropertyChanged(nameof(RuntimeAutoSwitch));
                OnPropertyChanged(nameof(RuntimeDiagnostics));
                OnPropertyChanged(nameof(RuntimePowerFlex));
                OnPropertyChanged(nameof(RuntimeAnalytics));
                (SaveConfigFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore lettura config: {ex.Message}";
                StatusColor = Brushes.Red;
                MainWindow.logger?.Error($"Errore lettura Config.xml: {ex.Message}");
            }
        }

        private async Task SaveMainConfigFileAsync()
        {
            if (!CanManageConfigFile)
            {
                StatusMessage = "Solo installer e administrator possono modificare il Config.xml";
                StatusColor = Brushes.OrangeRed;
                return;
            }

            if (!ValidateTopThreeDMeasurementOffsets())
            {
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Salvataggio configurazione applicativa...";
                StatusColor = Brushes.Orange;

                if (MainWindow.configManager == null)
                {
                    MainWindow.configManager = new AsyncConfigManagerXml(MainConfigFilePath);
                    await MainWindow.configManager.EnsureLoadedAsync();
                }

                await MainWindow.configManager.SaveConfigAsync();
                MainWindow.configManager = new AsyncConfigManagerXml(MainConfigFilePath);
                await MainWindow.configManager.EnsureLoadedAsync();
                RebuildUserManager();
                OnPropertyChanged(nameof(RuntimeConfiguration));
                OnPropertyChanged(nameof(RuntimeMySqlConnection));

                if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
                {
                    mainVm.NavigationVM?.ReloadMenu();
                    mainVm.TopMenuBarVM?.RefreshTexts();
                }

                StatusMessage = "Configurazione applicativa salvata e ricaricata";
                StatusColor = Brushes.Green;
                MainWindow.logger?.Info($"Config.xml aggiornato da Preferences: {MainConfigFilePath}");

                var savedConfiguration = RuntimeConfiguration;
                string offsetDetails =
                    $"height={(savedConfiguration?.Top3DHeightMeasurementOffsetMm ?? 0):0.###}; " +
                    $"width={(savedConfiguration?.Top3DWidthMeasurementOffsetMm ?? 0):0.###}; " +
                    $"length={(savedConfiguration?.Top3DLengthMeasurementOffsetMm ?? 0):0.###}; " +
                    $"actor={UserSession.CurrentUser}";
                MainWindow.logger?.Info($"TOP3D_MEASUREMENT_OFFSETS_SAVED|{offsetDetails}");
                ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                    NLog.LogLevel.Info,
                    "TOP3D_MEASUREMENT_OFFSETS_SAVED",
                    "Configuration",
                    "Top3D machine measurement corrections saved",
                    nameof(PreferenceViewModel),
                    offsetDetails);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore salvataggio config: {ex.Message}";
                StatusColor = Brushes.Red;
                MainWindow.logger?.Error($"Errore salvataggio Config.xml: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool ValidateTopThreeDMeasurementOffsets()
        {
            var configuration = RuntimeConfiguration;
            if (configuration == null)
            {
                return true;
            }

            bool offsetsAreValid =
                TopThreeDMeasurementCorrection.IsValidOffset(configuration.Top3DHeightMeasurementOffsetMm) &&
                TopThreeDMeasurementCorrection.IsValidOffset(configuration.Top3DWidthMeasurementOffsetMm) &&
                TopThreeDMeasurementCorrection.IsValidOffset(configuration.Top3DLengthMeasurementOffsetMm);

            if (offsetsAreValid)
            {
                return true;
            }

            string format = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ConfigTop3DMeasurementOffsetValidation",
                "Each Top3D measurement offset must be a finite value between -{0} and +{0} mm.");
            StatusMessage = string.Format(format, TopThreeDMeasurementCorrection.MaximumAbsoluteOffsetMm);
            StatusColor = Brushes.Red;
            MainWindow.logger?.Warn(
                "TOP3D_MEASUREMENT_OFFSETS_INVALID|height={0}|width={1}|length={2}|maxAbs={3}",
                configuration.Top3DHeightMeasurementOffsetMm,
                configuration.Top3DWidthMeasurementOffsetMm,
                configuration.Top3DLengthMeasurementOffsetMm,
                TopThreeDMeasurementCorrection.MaximumAbsoluteOffsetMm);
            return false;
        }

        private async Task SaveLanguageSettingsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Salvataggio lingua...";

                // 1. Salva nel PreferenceViewConfig
                _configManager.Config.LanguageSettings.CurrentLanguage = SelectedLanguage?.Code;
                _configManager.Config.LanguageSettings.Paths.FlagImagesPath = FlagImagesPath;
                _configManager.Config.LanguageSettings.Paths.LanguageFilesPath = LanguageFilesPath;

                await _configManager.SaveConfigAsync();

                // 2. Aggiorna anche il config principale
                MainWindow.configManager.Config.Configuration.Language = SelectedLanguage?.Code;
                await MainWindow.configManager.SaveConfigAsync();



                // 3. Ricarica i messaggi del server (questo aggiorna tutte le UI)
                await ServerMessagePersonalize.UpdateMessagesAsync();

                // 4. Notifica il cambio di lingua a tutto il pannello
                await RefreshAllPanelTextsAsync();

                StatusMessage = $"Lingua cambiata in {SelectedLanguage?.DisplayName}";
                StatusColor = Brushes.Green;

                MainWindow.logger?.Info($"Lingua cambiata in: {SelectedLanguage?.Code}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore salvataggio: {ex.Message}";
                StatusColor = Brushes.Red;
                MainWindow.logger?.Error($"Errore cambio lingua: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Aggiorna tutti i testi del pannello dopo il cambio lingua
        /// </summary>
        private async Task RefreshAllPanelTextsAsync()
        {
            try
            {
                // Esegui sul thread UI
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 1. Aggiorna il ViewModel della barra superiore
                    if (MainWindow.MainView?.DataContext is MainViewModel mainVm)
                    {
                        // Aggiorna il testo della vista corrente
                      //  mainVm.CurrentViewName = mainVm.CurrentViewName; // Forza refresh

                        // Aggiorna la barra superiore
                        mainVm.TopMenuBarVM?.RefreshTexts();

                        // Aggiorna il menu di navigazione
                        mainVm.NavigationVM?.ReloadMenu();
                    }

                    // 2. Aggiorna eventuali altre view aperte
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window is MainWindow mainWindow)
                        {
                            // Aggiorna titoli e testi statici
                            UpdateWindowTexts(mainWindow);
                        }
                    }

                    MainWindow.logger?.Info("Testi del pannello aggiornati con la nuova lingua");
                });
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore aggiornamento testi pannello: {ex.Message}");
            }
        }

        /// <summary>
        /// Aggiorna ricorsivamente tutti i testi di una finestra
        /// </summary>
        private void UpdateWindowTexts(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Aggiorna TextBlock
                if (child is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Name))
                {
                    // Prova a trovare il messaggio localizzato
                    string localizedText = ServerMessagePersonalize.GetMessage(textBlock.Name);
                    if (localizedText != textBlock.Name) // Se trovato
                    {
                        textBlock.Text = localizedText;
                    }
                }

                // Aggiorna Button
                if (child is Button button && button.Content is string content)
                {
                    string localizedContent = ServerMessagePersonalize.GetMessage(content);
                    if (localizedContent != content)
                    {
                        button.Content = localizedContent;
                    }
                }

                // Aggiorna Label
                if (child is Label label)
                {
                    string localizedContent = ServerMessagePersonalize.GetMessage(label.Content?.ToString());
                    if (localizedContent != label.Content?.ToString())
                    {
                        label.Content = localizedContent;
                    }
                }

                // Ricorsione per i figli
                if (VisualTreeHelper.GetChildrenCount(child) > 0)
                {
                    UpdateWindowTexts(child);
                }
            }
        }

        private async Task SaveSystemSettingsAsync()
        {
            try
            {
                IsLoading = true;

                _configManager.Config.SystemSettings.SavePath = SavePath;
                _configManager.Config.SystemSettings.AutoSaveEnabled = AutoSaveEnabled;
                _configManager.Config.SystemSettings.AutoSaveIntervalMinutes = AutoSaveInterval;

                await _configManager.SaveConfigAsync();

                StatusMessage = "Impostazioni sistema salvate";
                StatusColor = Brushes.Green;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore salvataggio: {ex.Message}";
                StatusColor = Brushes.Red;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void BrowseSavePath()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = SavePath,
                Description = "Seleziona cartella di salvataggio"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SavePath = dialog.SelectedPath;
            }
        }

        private bool CanAddUser()
        {
            return IsAdministrator
                && _userManager != null
                && !string.IsNullOrWhiteSpace(NewUsername)
                && !string.IsNullOrWhiteSpace(NewPassword)
                && NewPassword == ConfirmPassword
                && !string.IsNullOrWhiteSpace(SelectedRole);
        }

        private async Task AddNewUserAsync()
        {
            try
            {
                IsLoading = true;
                if (!IsAdministrator)
                {
                    StatusMessage = "Solo l'amministratore puo gestire gli utenti";
                    StatusColor = Brushes.OrangeRed;
                    return;
                }

                if (_userManager == null)
                {
                    StatusMessage = "Configurazione database utenti non disponibile";
                    StatusColor = Brushes.Red;
                    return;
                }

                string username = (NewUsername ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(username))
                {
                    StatusMessage = "Inserire uno username valido";
                    StatusColor = Brushes.OrangeRed;
                    return;
                }

                if (NewPassword != ConfirmPassword)
                {
                    StatusMessage = "Le password non coincidono";
                    StatusColor = Brushes.OrangeRed;
                    return;
                }

                if (await _userManager.UserExistsAsync(username))
                {
                    StatusMessage = $"L'utente {username} esiste gia";
                    StatusColor = Brushes.OrangeRed;
                    return;
                }

                bool inserted = await _userManager.InsertNewUsersAsync(username, NewPassword, SelectedRole);
                if (!inserted)
                {
                    StatusMessage = $"L'utente {username} esiste gia";
                    StatusColor = Brushes.OrangeRed;
                    return;
                }

                await RefreshUserManagementAsync();

                ClearNewUserFields();

                StatusMessage = $"Utente {username} creato con ruolo {SelectedRole}";
                StatusColor = Brushes.Green;
                MainWindow.logger?.Info($"Creato nuovo utente {username} con ruolo {SelectedRole}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore creazione utente: {ex.Message}";
                StatusColor = Brushes.Red;
                MainWindow.logger?.Error($"Errore creazione utente: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DeleteUserAsync(UserViewModel user)
        {
            if (user == null)
            {
                return;
            }

            if (!IsAdministrator)
            {
                StatusMessage = "Solo l'amministratore puo eliminare utenti";
                StatusColor = Brushes.OrangeRed;
                return;
            }

            if (_userManager == null)
            {
                StatusMessage = "Configurazione database utenti non disponibile";
                StatusColor = Brushes.Red;
                return;
            }

            if (string.Equals(user.Username, UserSession.CurrentUser, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Non e possibile eliminare l'utente attualmente loggato";
                StatusColor = Brushes.OrangeRed;
                return;
            }

            var dlgDelUser = new SystemNotificationWindow("Conferma",
                $"Eliminare l'utente {user.Username}?", NotificationSeverity.Warning, true);
            dlgDelUser.ShowDialog();
            if (dlgDelUser.Confirmed)
            {
                try
                {
                    IsLoading = true;
                    bool deleted = await _userManager.DeleteUserAsync(user.Username);

                    if (!deleted)
                    {
                        StatusMessage = $"Utente {user.Username} non trovato nel database";
                        StatusColor = Brushes.OrangeRed;
                        return;
                    }

                    await LoadExistingUsersAsync();
                    StatusMessage = $"Utente {user.Username} eliminato";
                    StatusColor = Brushes.Green;
                    MainWindow.logger?.Info($"Eliminato utente {user.Username}");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Errore eliminazione: {ex.Message}";
                    StatusColor = Brushes.Red;
                    MainWindow.logger?.Error($"Errore eliminazione utente {user.Username}: {ex.Message}");
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        public void UpdateNewPassword(string password)
        {
            NewPassword = password ?? string.Empty;
        }

        public void UpdateConfirmPassword(string password)
        {
            ConfirmPassword = password ?? string.Empty;
        }

        private void ClearNewUserFields()
        {
            NewUsername = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
        }

        private void TestLanguagePaths()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Test percorsi:");

            bool flagPathExists = Directory.Exists(FlagImagesPath);
            bool langPathExists = Directory.Exists(LanguageFilesPath);

            sb.AppendLine($"Flag Images: {(flagPathExists ? "✓" : "✗")} {FlagImagesPath}");
            sb.AppendLine($"Language Files: {(langPathExists ? "✓" : "✗")} {LanguageFilesPath}");

            if (flagPathExists)
            {
                var files = Directory.GetFiles(FlagImagesPath, "*.png");
                sb.AppendLine($"  Immagini trovate: {files.Length}");
                foreach (var f in files.Take(5))
                {
                    sb.AppendLine($"    - {Path.GetFileName(f)}");
                }
            }

            new SystemNotificationWindow("Test Percorsi", sb.ToString(),
                flagPathExists && langPathExists ? NotificationSeverity.Info : NotificationSeverity.Warning).ShowDialog();
        }

        private ImageSource GetFlagImage(string flagFileName)
        {
            if (string.IsNullOrEmpty(flagFileName)) return null;

            try
            {
                string fullPath = Path.Combine(FlagImagesPath, flagFileName);
                if (!File.Exists(fullPath)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(fullPath);
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateLanguagePreview()
        {
            OnPropertyChanged(nameof(CurrentFlagImage));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            UserSession.OnRoleChanged -= OnUserRoleChanged;
        }
    }

    public class UserViewModel
    {
        public string Username { get; set; }
        public string Role { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsActive { get; set; }

        public string RoleIcon
        {
            get
            {
                switch (Role)
                {
                    case "Administrator": return "UserSecret";
                    case "Installer": return "Wrench";
                    case "Expert": return "GraduationCap";
                    case "Operator": return "User";
                    case "Viewer": return "Eye";
                    default: return "User";
                }
            }
        }

        public Brush RoleColor
        {
            get
            {
                switch (Role)
                {
                    case "Administrator": return Brushes.DarkRed;
                    case "Installer": return Brushes.DarkBlue;
                    case "Expert": return Brushes.Purple;
                    case "Operator": return Brushes.DarkGreen;
                    case "Viewer": return Brushes.Gray;
                    default: return Brushes.Black;
                }
            }
        }
    }
}
