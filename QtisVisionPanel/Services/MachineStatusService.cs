using QtisVisionPanel.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Threading;
using System.Windows;

namespace QtisVisionPanel.Services
{
    public class MachineStatusService : INotifyPropertyChanged
    {
        private static readonly Lazy<MachineStatusService> _instance =
            new Lazy<MachineStatusService>(() => new MachineStatusService());

        public static MachineStatusService Instance => _instance.Value;

        private TopMenuBarViewModel.MachineStatus _currentStatus;
        private bool _isSystemInitializing = true;
        private Timer _statusCheckTimer;
        private readonly object _lock = new object();
        private readonly MachineRuntimeService _machineRuntimeService;
        private readonly ApplicationEventLogger _applicationEventLogger;

        public ObservableCollection<StatusChangeEvent> StatusHistory { get; } = new ObservableCollection<StatusChangeEvent>();

        public TopMenuBarViewModel.MachineStatus CurrentStatus
        {
            get => _currentStatus;
            private set
            {
                // ObservableCollection is not thread-safe — dispatch to UI thread
                // before touching StatusHistory or raising PropertyChanged.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => CurrentStatus = value));
                    return;
                }

                if (_currentStatus != value)
                {
                    var oldStatus = _currentStatus;
                    _currentStatus = value;

                    // Registra il cambio di stato nella storia
                    StatusHistory.Add(new StatusChangeEvent
                    {
                        Timestamp = DateTime.Now,
                        OldStatus = oldStatus,
                        NewStatus = value,
                        IsContinuousRunActive = _machineRuntimeService.IsContinuousRunActive
                    });

                    // Mantieni solo gli ultimi 100 eventi
                    if (StatusHistory.Count > 100)
                    {
                        StatusHistory.RemoveAt(0);
                    }

                    OnPropertyChanged(nameof(CurrentStatus));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(IsSystemReady));

                    _applicationEventLogger.LogMachineStatusChange(
                        oldStatus.ToString(),
                        value.ToString(),
                        _machineRuntimeService.IsContinuousRunActive);

                    // Notifica gli osservatori
                    StatusChanged?.Invoke(this, new StatusChangedEventArgs(oldStatus, value));
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (_isSystemInitializing)
                    return "INITIALIZING...";

                switch (CurrentStatus)
                {
                    case TopMenuBarViewModel.MachineStatus.Connecting:
                        return "CONNECTING...";
                        case TopMenuBarViewModel.MachineStatus.Running:
                        return "RUNNING";
                        case TopMenuBarViewModel.MachineStatus.Stopped:
                        return "STOPPED";
                        case TopMenuBarViewModel.MachineStatus.Error:
                        return "ERROR";
                        case TopMenuBarViewModel.MachineStatus.Maintenance:
                        return "MAINTENANCE";
                        default:
                        return "UNKNOWN";
                }
               
            }
        }

        public bool IsSystemReady => !_isSystemInitializing &&
            (CurrentStatus == TopMenuBarViewModel.MachineStatus.Running ||
             CurrentStatus == TopMenuBarViewModel.MachineStatus.Stopped);

        public bool IsSystemInitializing
        {
            get => _isSystemInitializing;
            set
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => IsSystemInitializing = value));
                    return;
                }

                if (_isSystemInitializing != value)
                {
                    _isSystemInitializing = value;
                    OnPropertyChanged(nameof(IsSystemInitializing));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(IsSystemReady));
                }
            }
        }

        public event EventHandler<StatusChangedEventArgs> StatusChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        private MachineStatusService()
        {
            _currentStatus = TopMenuBarViewModel.MachineStatus.Connecting;
            _machineRuntimeService = ServiceLocator.MachineRuntimeService;
            _applicationEventLogger = ServiceLocator.ApplicationEventLogger;

            // Avvia il timer per controllare lo stato ogni 500ms
            _statusCheckTimer = new Timer(CheckStatusCallback, null, 1000, 500);

            // Registra l'evento di shutdown per fermare il timer
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop();
            Application.Current.Exit += (s, e) => Stop();
        }

        private void CheckStatusCallback(object state)
        {
            try
            {
                // Controlla se l'applicazione si sta chiudendo
                if (MainWindow.IsShuttingDown)
                {
                    Stop();
                    return;
                }

                // Determina lo stato corrente
                var newStatus = DetermineCurrentStatus();

                UpdateCurrentStatus(newStatus);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error in status check: {ex.Message}");
                _applicationEventLogger.LogException("MACHINE_STATUS_CHECK_ERROR", ex, nameof(CheckStatusCallback), "Errore durante il controllo dello stato macchina");
            }
        }

        private void UpdateCurrentStatus(TopMenuBarViewModel.MachineStatus newStatus)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => UpdateCurrentStatus(newStatus)));
                return;
            }

            lock (_lock)
            {
                if (_currentStatus != newStatus)
                {
                    CurrentStatus = newStatus;
                }
            }
        }

        private TopMenuBarViewModel.MachineStatus DetermineCurrentStatus()
        {
            try
            {
                if (_machineRuntimeService.IsContinuousRunCommandInProgress)
                {
                    return _currentStatus;
                }

                // Se il sistema è in fase di inizializzazione
                if (_isSystemInitializing)
                {
                    return TopMenuBarViewModel.MachineStatus.Connecting;
                }

                // Controlla VisionPro
                if (_machineRuntimeService.IsContinuousRunActive)
                {
                    if (!_machineRuntimeService.IsVisionHealthy)
                    {
                        return TopMenuBarViewModel.MachineStatus.Error;
                    }

                    return TopMenuBarViewModel.MachineStatus.Running;
                }

                // Controlla se ci sono errori
                if (CheckForSystemErrors())
                {
                    return TopMenuBarViewModel.MachineStatus.Error;
                }

                // Controlla se il sistema è in manutenzione
                if (IsInMaintenanceMode())
                {
                    return TopMenuBarViewModel.MachineStatus.Maintenance;
                }

                // Altrimenti è fermo
                return TopMenuBarViewModel.MachineStatus.Stopped;
            }
            catch (Exception)
            {
                return TopMenuBarViewModel.MachineStatus.Error;
            }
        }

        private bool CheckForSystemErrors()
        {
            try
            {
                // Implementa la logica per rilevare errori di sistema
                // Ad esempio: controlla se ci sono eccezioni non gestite, 
                // connessioni perse, dispositivi non rispondenti, etc.

                // Controlla se VisionPro ha errori
                if (_machineRuntimeService.CognexManager != null && !_machineRuntimeService.IsVisionHealthy)
                {
                    return true;
                }

                // Controlla se il database è raggiungibile
                // if (!CheckDatabaseConnection()) return true;

                return false;
            }
            catch
            {
                return true;
            }
        }

        private bool IsInMaintenanceMode()
        {
            // Implementa la logica per rilevare la modalità manutenzione
            // Potrebbe essere basata su un file flag, configurazione, etc.
            return false;
        }

        public void MarkSystemAsInitialized()
        {
            IsSystemInitializing = false;
            MainWindow.logger?.Info("System marked as initialized");
            _applicationEventLogger.LogLifecycle("SYSTEM_INITIALIZED", "System marked as initialized", nameof(MarkSystemAsInitialized));
        }

        public void SetMaintenanceMode(bool enable)
        {
            if (enable)
            {
                CurrentStatus = TopMenuBarViewModel.MachineStatus.Maintenance;
            }
            else
            {
                // Ritorna allo stato precedente
                CheckStatusCallback(null);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _statusCheckTimer?.Dispose();
                _statusCheckTimer = null;
            }
        }

        public TopMenuBarViewModel.MachineStatus GetLastKnownStatus()
        {
            return StatusHistory.Count > 0
                ? StatusHistory[StatusHistory.Count - 1].NewStatus
                : TopMenuBarViewModel.MachineStatus.Connecting;
        }

        public string GetStatusHistoryAsText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Status History:");
            sb.AppendLine("================");

            foreach (var eventItem in StatusHistory)
            {
                sb.AppendLine($"{eventItem.Timestamp:HH:mm:ss.fff} - {eventItem.OldStatus} → {eventItem.NewStatus} " +
                             $"(ContinuousRun: {eventItem.IsContinuousRunActive})");
            }

            return sb.ToString();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class StatusChangeEvent
    {
        public DateTime Timestamp { get; set; }
        public TopMenuBarViewModel.MachineStatus OldStatus { get; set; }
        public TopMenuBarViewModel.MachineStatus NewStatus { get; set; }
        public bool IsContinuousRunActive { get; set; }
    }

    public class StatusChangedEventArgs : EventArgs
    {
        public TopMenuBarViewModel.MachineStatus OldStatus { get; }
        public TopMenuBarViewModel.MachineStatus NewStatus { get; }

        public StatusChangedEventArgs(TopMenuBarViewModel.MachineStatus oldStatus, TopMenuBarViewModel.MachineStatus newStatus)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
        }
    }
}
