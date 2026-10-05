using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace QtisVisionPanel.ViewModels
{
    public abstract class AuthorizedViewModel : INotifyPropertyChanged
    {
        protected AuthorizationService _authService;

        private bool _isAuthorized = true;
        private bool _canEdit = false;
        private bool _canSave = false;

        public bool IsAuthorized
        {
            get => _isAuthorized;
            protected set
            {
                if (_isAuthorized != value)
                {
                    _isAuthorized = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanEdit
        {
            get => _canEdit;
            protected set
            {
                if (_canEdit != value)
                {
                    _canEdit = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanSave
        {
            get => _canSave;
            protected set
            {
                if (_canSave != value)
                {
                    _canSave = value;
                    OnPropertyChanged();
                }
            }
        }

        public AuthorizedViewModel()
        {
            _authService = ServiceLocator.Authorization;

            // Sottoscrivi agli eventi di cambio ruolo
            UserSession.OnRoleChanged += async (s, e) => await UpdatePermissionsAsync();

            // Aggiorna autorizzazioni iniziali
            Task.Run(async () => await UpdatePermissionsAsync());
        }

        // Metodo astratto che ogni ViewModel deve implementare
        public abstract Task UpdatePermissionsAsync();

        // Metodo helper per mostrare errori di autorizzazione
        protected void ShowAuthorizationError(string action)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                new SystemNotificationWindow("Autorizzazione negata",
                    $"Non hai i permessi per {action}.",
                    NotificationSeverity.Warning).ShowDialog();
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}