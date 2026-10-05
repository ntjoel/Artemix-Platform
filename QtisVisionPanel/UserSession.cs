using System;

namespace QtisVisionPanel
{
    public static class UserSession
    {
        private static string _currentUser = "Guest";
        private static string _currentRole = "Viewer";

        public static string CurrentUser
        {
            get => _currentUser;
            set
            {
                _currentUser = value;
                OnUserChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static string CurrentRole
        {
            get => _currentRole;
            set
            {
                _currentRole = value;
                OnRoleChanged?.Invoke(null, EventArgs.Empty);
            }
        }
       // UserSession.OnRoleChanged?.Invoke(this, EventArgs.Empty);
        public static void NotifyRoleChanged()
        {
            OnRoleChanged?.Invoke(null, EventArgs.Empty);
        }
        public static event EventHandler OnUserChanged;
        public static event EventHandler OnRoleChanged;

        // Metodi helper per controlli rapidi
        public static bool IsAdministrator => CurrentRole == "Administrator";
        public static bool IsInstaller => CurrentRole == "Installer";
        public static bool IsExpert => CurrentRole == "Expert";
        public static bool IsOperator => CurrentRole == "Operator";
        public static bool IsViewer => CurrentRole == "Viewer";
        // **NUOVO: Controlla se è Guest (non loggato)**
        public static bool IsGuest => CurrentUser == "Guest" && CurrentRole == "Viewer";
        // **NUOVO: Controlla se è loggato (non Guest)**
        public static bool IsLoggedIn => !IsGuest;
        public static bool CanEdit => !IsViewer && !IsGuest;
        public static bool CanSave => !IsViewer && !IsGuest;

        public static bool ShouldAutoLoginAdministratorFromConfig()
        {
            return MainWindow.configManager?.Config?.Configuration?.AutoLoginAdministratorForDemo == true;
        }

        public static void ApplyStartupSessionFromConfig()
        {
            if (ShouldAutoLoginAdministratorFromConfig())
            {
                // Demo/fair mode intentionally bypasses the login dialog so the machine
                // can be shown continuously. Keep it explicit and config-driven.
                CurrentUser = "Administrator";
                CurrentRole = "Administrator";
                MainWindow.logger?.Warn("Demo auto-login enabled: startup session forced to Administrator.");
                return;
            }

            CurrentUser = "Guest";
            CurrentRole = "Viewer";
        }
    }
}
