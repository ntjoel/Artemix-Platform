using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Views;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace QtisVisionPanel.ViewModels
{
    public class ControlButtonsViewModel : INotifyPropertyChanged
    {
        private bool _isRunning;
        private bool _isSystemReady;
        private System.Windows.Threading.DispatcherTimer _statusCheckTimer;

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StartButtonEnabled));
                    OnPropertyChanged(nameof(StopButtonEnabled));
                    OnPropertyChanged(nameof(StartButtonColor));
                    OnPropertyChanged(nameof(StopButtonColor));
                    OnPropertyChanged(nameof(StartButtonText));
                    OnPropertyChanged(nameof(StopButtonText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsSystemReady
        {
            get => _isSystemReady;
            set
            {
                if (_isSystemReady != value)
                {
                    _isSystemReady = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StartButtonEnabled));
                    OnPropertyChanged(nameof(StopButtonEnabled));
                    OnPropertyChanged(nameof(StartButtonColor));
                    OnPropertyChanged(nameof(StopButtonColor));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public Brush StartButtonColor
        {
            get
            {
                if (IsRunning)
                {
                    return Brushes.Green;
                }

                if (StartButtonEnabled)
                {
                    return Brushes.LightGreen;
                }

                return Brushes.LightGray;
            }
        }

        public Brush StopButtonColor
        {
            get
            {
                if (!IsRunning)
                {
                    return Brushes.Red;
                }

                if (StopButtonEnabled)
                {
                    return Brushes.LightCoral;
                }

                return Brushes.LightGray;
            }
        }

        public string StartButtonText => IsRunning
            ? GetMessage("Sub_entry_Running", "Running")
            : GetMessage("Sub_entry_Start", "Start");
        public string StopButtonText => IsRunning
            ? GetMessage("Sub_entry_Stop", "Stop")
            : GetMessage("Sub_entry_Stopped", "Stopped");
        public string CloseButtonText => GetMessage("Sub_entry_Close", "Chiudi HMI");
        public string CloseButtonToolTip => CanCloseApplication
            ? GetMessage("Sub_entry_CloseApplicationTooltip", "Close the application completely")
            : GetMessage("Sub_entry_CloseApplicationRestrictedTooltip", "Only Expert, Installer or Administrator can close the HMI from this button.");
        public bool StartButtonEnabled => !IsRunning && IsSystemReady;
        public bool StopButtonEnabled => IsRunning && IsSystemReady;
        public bool CanCloseApplication => UserSession.IsAdministrator || UserSession.IsInstaller || UserSession.IsExpert;

        public event Action StartRequested;
        public event Action StopRequested;
        public event Action RestartRequested;

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RestartCommand { get; }

        public ControlButtonsViewModel()
        {
            StartCommand = new RelayCommand(OnStart, _ => StartButtonEnabled);
            StopCommand = new RelayCommand(OnStop, _ => StopButtonEnabled);
            RestartCommand = new RelayCommand(OnRestart, _ => CanCloseApplication);
            UserSession.OnRoleChanged += OnUserRoleChanged;
            InitializeStatusTimer();
        }

        private void OnUserRoleChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(CanCloseApplication));
            OnPropertyChanged(nameof(CloseButtonToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        private void InitializeStatusTimer()
        {
            _statusCheckTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _statusCheckTimer.Tick += (s, e) => CheckSystemStatus();
            _statusCheckTimer.Start();
        }

        private void CheckSystemStatus()
        {
            try
            {
                bool currentRunningStatus = ReadRunningStateFromVisionPro();
                if (IsRunning != currentRunningStatus)
                {
                    if (Application.Current?.Dispatcher != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            IsRunning = currentRunningStatus;
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    else
                    {
                        IsRunning = currentRunningStatus;
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore nel controllo dello stato: {ex.Message}");
            }
        }

        private void OnStart(object parameter)
        {
            IsSystemReady = false;
            IsRunning = ReadRunningStateFromVisionPro();
            StartRequested?.Invoke();
        }

        private void OnStop(object parameter)
        {
            IsSystemReady = false;
            IsRunning = ReadRunningStateFromVisionPro();
            StopRequested?.Invoke();
        }

        private void OnRestart(object parameter)
        {
            var dlgRestart = new SystemNotificationWindow(
                GetMessage("Sub_entry_CloseAppConfirmTitle", "Confirm application shutdown"),
                GetMessage("Sub_entry_CloseAppConfirmMessage", "Are you sure you want to close the application completely?\nAll processes and threads will be stopped."),
                NotificationSeverity.Warning, true);
            dlgRestart.ShowDialog();
            if (dlgRestart.Confirmed)
            {
                RestartRequested?.Invoke();
            }
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void UpdateFromSystem()
        {
            try
            {
                bool currentStatus = ReadRunningStateFromVisionPro();
                bool isSystemAvailable = MainWindow._cognexManager != null;

                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsRunning = currentStatus;
                        IsSystemReady = isSystemAvailable;
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    IsRunning = currentStatus;
                    IsSystemReady = isSystemAvailable;
                }

                MainWindow.logger?.Debug($"Stato pulsanti aggiornato da VisionPro: IsRunning = {currentStatus}");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'aggiornamento dello stato: {ex.Message}");
            }
        }

        private bool ReadRunningStateFromVisionPro()
        {
            try
            {
                var runtimeService = ServiceLocator.MachineRuntimeService;
                if (runtimeService != null)
                {
                    return runtimeService.IsContinuousRunActive;
                }

                return MainWindow._isContinuousRunActive;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore lettura stato VisionPro: {ex.Message}");
                return MainWindow._isContinuousRunActive;
            }
        }

        public void SetInitialState(bool isRunning)
        {
            IsRunning = isRunning;
            IsSystemReady = true;
        }

        public void Dispose()
        {
            UserSession.OnRoleChanged -= OnUserRoleChanged;
            _statusCheckTimer?.Stop();
            _statusCheckTimer = null;
        }
    }
}
