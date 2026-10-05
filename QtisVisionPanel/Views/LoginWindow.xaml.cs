using QtisVisionPanel.ViewModels;
using System;
using System.Collections.ObjectModel;
using QtisVisionPanel.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Logica di interazione per LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly LoginUserHistoryService _userHistoryService = new LoginUserHistoryService();
        private readonly ObservableCollection<string> _recentUsernames = new ObservableCollection<string>();

        public UserInfo LoggedInUser { get; private set; }
        public LoginWindow()
        {
            InitializeComponent();
            SeedConfiguredUsername();
            LoadRecentUsernames();
            UsernameComboBox.Focus();
            servermessageloader();
            MainWindow.logger?.Info("LoginWindow created");
        }
        private void ShowError(string message)
        {
            ErrorMessage.Text = message;
            ErrorMessage.Visibility = Visibility.Visible;
        }
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameComboBox.Text?.Trim() ?? string.Empty;
            string password = PasswordBox.Password;

            // Permetti login come Guest senza password
            if (username.Equals("Guest", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(password))
            {
                // Guest login
                UserSession.CurrentUser = "Guest";
                UserSession.CurrentRole = "Viewer";
                LoggedInUser = new UserInfo
                {
                    Username = "Guest",
                    Role = "Viewer",
                    LoginTime = DateTime.Now
                };

                // Salva nel config (opzionale, Guest è default)
                if (MainWindow.configManager != null)
                {
                    MainWindow.configManager.Config.Configuration.User = "Guest";
                    MainWindow.configManager.Config.Configuration.CurrentUserRole = "Viewer";
                    await MainWindow.configManager.SaveConfigAsync();
                }

                RememberSuccessfulLogin("Guest");
                DialogResult = true;
                Close();
                return;
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowError("Please enter both username and password");
                return;
            }

            // Mostra indicatore di caricamento
            LoginButton.IsEnabled = false;
            LoginButton.Content = "Logging in...";

            try
            {
                // Usa il gestore utenti esistente
                var userManager = MainWindow._userManager;
                if (userManager == null)
                {
                    ShowError("User manager not initialized");
                    return;
                }

                // Verifica le credenziali
                string role = await userManager.GetUserRoleAsync(username, password);

                if (!string.IsNullOrEmpty(role))
                {
                    // IMPOSTA L'UTENTE NELLA SESSIONE GLOBALE
                    UserSession.CurrentUser = username;
                    UserSession.CurrentRole = role;
                    LoggedInUser = new UserInfo
                    {
                        Username = username,
                        Role = role,
                        LoginTime = DateTime.Now
                    };

                    // Salva nel config
                    MainWindow.configManager.Config.Configuration.User = username;
                    MainWindow.configManager.Config.Configuration.CurrentUserRole = role;
                    await MainWindow.configManager.SaveConfigAsync();

                    RememberSuccessfulLogin(username);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowError("Invalid username or password");
                    MainWindow.logger?.Warn($"Login failed for user: {username} - invalid credentials");
                    PasswordBox.Clear();
                    PasswordBox.Focus();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Login error: {ex.Message}");
                ShowError($"Login error: {ex.Message}");
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "Login";
            }
        }

        // Aggiungi un metodo per login automatico come Guest
        public void PerformGuestLogin()
        {
            UserSession.CurrentUser = "Guest";
            UserSession.CurrentRole = "Viewer";
            LoggedInUser = new UserInfo
            {
                Username = "Guest",
                Role = "Viewer",
                LoginTime = DateTime.Now
            };
            RememberSuccessfulLogin("Guest");
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.logger?.Info("Login cancelled by user");
            DialogResult = false;
            Close();
        }

      
        private void servermessageloader()
        {
            Sub_entry_loginHeader.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_loginHeader;
            Sub_entry_cancelbtn_login.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_cancelbtn_login;
            LoginButton.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_login;
        }
        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                LoginButton_Click(sender, e);
            }
        }

        private void UsernameComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PasswordBox.Focus();
            }
        }

        private void UsernameComboBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            OpenUsernameSuggestionList();
        }

        private void UsernameComboBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!UsernameComboBox.IsKeyboardFocusWithin)
            {
                UsernameComboBox.Focus();
                e.Handled = true;
            }

            OpenUsernameSuggestionList();
        }

        private void UsernameComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            UsernameComboBox.ApplyTemplate();
            ApplyUsernameComboBoxTextStyle();
            Dispatcher.BeginInvoke(new Action(ApplyUsernameComboBoxTextStyle));
        }

        private void LoadRecentUsernames()
        {
            _recentUsernames.Clear();
            foreach (var username in _userHistoryService.LoadUsernames())
            {
                _recentUsernames.Add(username);
            }

            UsernameComboBox.ItemsSource = _recentUsernames;
        }

        private void SeedConfiguredUsername()
        {
            var configuredUsername = MainWindow.configManager?.Config?.Configuration?.User;
            if (!string.IsNullOrWhiteSpace(configuredUsername))
            {
                _userHistoryService.RememberSuccessfulLogin(configuredUsername);
            }
        }

        private void RememberSuccessfulLogin(string username)
        {
            _userHistoryService.RememberSuccessfulLogin(username);
            LoadRecentUsernames();
        }

        private void OpenUsernameSuggestionList()
        {
            if (_recentUsernames.Count > 0)
            {
                UsernameComboBox.IsDropDownOpen = true;
            }
        }

        private void ApplyUsernameComboBoxTextStyle()
        {
            var editableTextBox = UsernameComboBox.Template.FindName("PART_EditableTextBox", UsernameComboBox) as TextBox;
            if (editableTextBox == null)
            {
                return;
            }

            editableTextBox.Background = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E));
            editableTextBox.Foreground = Brushes.White;
            editableTextBox.CaretBrush = Brushes.White;
            editableTextBox.BorderThickness = new Thickness(0);
        }
    }
}
