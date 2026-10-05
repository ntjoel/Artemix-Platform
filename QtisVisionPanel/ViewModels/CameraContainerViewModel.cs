using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// View-model that exposes the camera views shown in the main overview.
    ///
    /// The current production baseline now gives priority to the active VPP job
    /// names when they already expose a semantic role (Top, Front, ...).
    ///
    /// CameraConfig.xml still matters as a fallback semantic contract, but it
    /// should no longer hide a valid runtime camera panel just because its
    /// numeric IDs were not updated after a QuickBuild change.
    /// </summary>
    public class CameraContainerViewModel : INotifyPropertyChanged
    {
        private static readonly HashSet<string> SupportedCameraTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "top",
            "top3d",
            "side",
            "left",
            "front",
            "rear",
            "bottom",
            "right"
        };

        public ObservableCollection<CameraViewModel> CameraViews { get; set; }

        private FileSystemWatcher _watcher;
        private readonly string _configPath;
        private string _logMessage;
        private int _cameraColumnCount = 2;
        private Thickness _cameraSectionPadding = new Thickness(6);
        private bool _isCompactViewport;
        private double _lastViewportWidth;
        private double _lastViewportHeight;

        public string LogMessage
        {
            get => _logMessage;
            set
            {
                _logMessage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogMessage)));
            }
        }

        public int CameraColumnCount
        {
            get => _cameraColumnCount;
            private set
            {
                if (_cameraColumnCount == value)
                {
                    return;
                }

                _cameraColumnCount = value;
                OnPropertyChanged();
            }
        }

        public Thickness CameraSectionPadding
        {
            get => _cameraSectionPadding;
            private set
            {
                if (_cameraSectionPadding == value)
                {
                    return;
                }

                _cameraSectionPadding = value;
                OnPropertyChanged();
            }
        }

        public bool IsCompactViewport
        {
            get => _isCompactViewport;
            private set
            {
                if (_isCompactViewport == value)
                {
                    return;
                }

                _isCompactViewport = value;
                OnPropertyChanged();
            }
        }

        public CameraContainerViewModel()
        {
            CameraViews = new ObservableCollection<CameraViewModel>();

            try
            {
                _configPath = Path.Combine(MainWindow.configManager.Config.Configuration.Recipe_Folder, "CameraConfig.xml");

                if (File.Exists(_configPath))
                {
                    RefreshCameraViews(_configPath);
                    SetupFileWatcher(_configPath);
                }
                else
                {
                    LogMessage = "File di configurazione non trovato.";
                }
            }
            catch (Exception ex)
            {
                LogMessage = $"Errore: {ex.Message}";
            }
        }

        private void UpdateLogMessage()
        {
            var jobCount = MainWindow.JobMapping?.Count ?? 0;
            var cameraCount = CameraViews.Count;
            var configSource = MainWindow.JobMapping != null && MainWindow.JobMapping.Count > 0 ? "JobMapping" : "XML";

            LogMessage = $"{ServerMessagePersonalize.CurrentMessages.messages.lb105} {cameraCount}, Job VPP: {jobCount} (Fonte: {configSource})";
        }

        private void SetupFileWatcher(string xmlPath)
        {
            var directory = Path.GetDirectoryName(xmlPath);
            var fileName = Path.GetFileName(xmlPath);

            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _watcher.Changed += async (s, e) =>
            {
                await System.Threading.Tasks.Task.Delay(100);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshCameraViews(xmlPath);
                }, DispatcherPriority.Background);
            };

            _watcher.EnableRaisingEvents = true;
        }

        public void RefreshCameraViews(string xmlPath)
        {
            CameraViews.Clear();
            LoadCameraConfiguration(xmlPath);
            ApplyResponsiveLayout();
            UpdateLogMessage();
            LogMessage = $"Configurazione aggiornata. Telecamere: {CameraViews.Count}";
        }

        /// <summary>
        /// Rebuilds the camera view list using the latest runtime job metadata.
        /// This is used after VisionPro initialization or recipe reload, so the
        /// overview reflects only the camera jobs really exposed by the active VPP.
        /// </summary>
        public void RefreshFromRuntimeConfiguration()
        {
            if (!string.IsNullOrWhiteSpace(_configPath))
            {
                RefreshCameraViews(_configPath);
            }
        }

        public void UpdateLayoutProfile(double availableWidth, double availableHeight)
        {
            if (availableWidth > 0)
            {
                _lastViewportWidth = availableWidth;
            }

            if (availableHeight > 0)
            {
                _lastViewportHeight = availableHeight;
            }

            ApplyResponsiveLayout();
        }

        private void ApplyResponsiveLayout()
        {
            var cameraCount = Math.Max(CameraViews.Count, 1);
            var effectiveWidth = _lastViewportWidth > 0 ? _lastViewportWidth : SystemParameters.PrimaryScreenWidth;
            var effectiveHeight = _lastViewportHeight > 0 ? _lastViewportHeight : SystemParameters.PrimaryScreenHeight;

            int desiredColumns;
            if (effectiveWidth < 620)
            {
                desiredColumns = 1;
            }
            else if (effectiveWidth < 900)
            {
                desiredColumns = Math.Min(2, cameraCount);
            }
            else if (cameraCount == 3)
            {
                desiredColumns = 3;
            }
            else if (cameraCount >= 4)
            {
                // Up to four cameras are kept on the first row. Additional
                // cameras automatically continue on the following rows.
                desiredColumns = 4;
            }
            else
            {
                desiredColumns = Math.Min(2, cameraCount);
            }

            CameraColumnCount = Math.Max(1, Math.Min(desiredColumns, cameraCount));
            IsCompactViewport = effectiveWidth < 1280 || effectiveHeight < 900;
            CameraSectionPadding = IsCompactViewport ? new Thickness(3) : new Thickness(6);

            double horizontalChrome = CameraSectionPadding.Left + CameraSectionPadding.Right + (CameraColumnCount * 14.0);
            double tileWidth = Math.Max(180.0, (effectiveWidth - horizontalChrome) / CameraColumnCount);
            double maximumDisplayHeight = cameraCount <= 2 ? 265.0 : cameraCount <= 4 ? 220.0 : 195.0;
            double displayHeight = Math.Max(145.0, Math.Min(maximumDisplayHeight, tileWidth * 0.58));

            foreach (CameraViewModel cameraView in CameraViews)
            {
                cameraView.DisplayHostHeight = displayHeight;
                cameraView.DisplayHostMinHeight = Math.Min(145.0, displayHeight);
            }
        }

        private void LoadCameraConfiguration(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var configuredCameras = CameraConfigurationHelper.LoadConfiguredCameras(path);
                var camerasToLoad = ResolveRuntimeCameraViews(configuredCameras);

                foreach (var camera in camerasToLoad)
                {
                    if (string.IsNullOrWhiteSpace(camera.CameraType) || !SupportedCameraTypes.Contains(camera.CameraType))
                    {
                        MainWindow.logger.Warn($"Camera ID {camera.CameraId} ignorata nella vista: tipo non supportato '{camera.CameraType}'.");
                        continue;
                    }

                    CameraViews.Add(camera);
                }

                MainWindow.logger.Info($"Viste telecamere caricate: {CameraViews.Count}");
                ApplyResponsiveLayout();
            }
            catch (Exception ex)
            {
                LogMessage = $"Errore caricamento configurazione: {ex.Message}";
                MainWindow.logger.Error($"Errore nel LoadCameraConfiguration: {ex.Message}");
            }
        }

        private static List<CameraViewModel> ResolveRuntimeCameraViews(IReadOnlyList<CameraModel> configuredCameras)
        {
            var resolved = new List<CameraViewModel>();
            var configuredByRole = configuredCameras
                .GroupBy(camera => CameraConfigurationHelper.NormalizeCameraType(camera.Type))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var configuredSerialsByRole = CameraConfigurationHelper.BuildConfiguredSerialMapping();
            var configuredSerialRoleCounts = configuredSerialsByRole
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .GroupBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var runtimeJobSerials = new Dictionary<int, string>();

            if (MainWindow.JobMapping == null || MainWindow.JobMapping.Count == 0)
            {
                foreach (var camera in configuredCameras.OrderBy(camera => camera.Id))
                {
                    resolved.Add(new CameraViewModel
                    {
                        CameraId = camera.Id,
                        CameraType = ResolveConfiguredDisplayRole(camera),
                        JobName = string.Empty,
                        IsJobAvailable = false
                    });
                }

                EnsureVirtualTop2DView(resolved);
                MainWindow.logger.Info($"JobMapping non disponibile - Caricate tutte le {resolved.Count} telecamere dal file XML");
                return resolved;
            }

            foreach (var job in MainWindow.JobMapping.OrderBy(entry => entry.Key))
            {
                string role = ResolveCameraRole(job.Key, job.Value);
                string runtimeSerial = CameraConfigurationHelper.GetRuntimeJobSerial(job.Key);
                runtimeJobSerials[job.Key] = runtimeSerial;

                if (!SupportedCameraTypes.Contains(role))
                {
                    MainWindow.logger.Warn($"Job ID {job.Key} ({job.Value}) ignorato nella vista: ruolo '{role}' non supportato.");
                    continue;
                }

                if (ServiceLocator.InspectionConfigService?.IsCameraRoleEnabled(role) == false)
                {
                    MainWindow.logger.Info(
                        $"CAMERA_VIEW_DISABLED_BY_RECIPE|jobId={job.Key}|job={job.Value}|role={role}");
                    continue;
                }

                bool hasHardwareCamera = HasHardwareCamera(job.Key);
                if (!hasHardwareCamera)
                {
                    MainWindow.logger.Warn(
                        $"CAMERA_VIEW_HARDWARE_PENDING|jobId={job.Key}|job={job.Value}|role={role}|The camera panel remains visible while the FrameGrabber becomes ready.");
                    // Keep the semantic VPP job visible while a GigE camera is
                    // still being enumerated during the first startup refresh.
                }

                bool hasConfigMatch = configuredByRole.TryGetValue(role, out CameraModel configuredCamera);
                if (!hasConfigMatch)
                {
                    if (configuredSerialsByRole.TryGetValue(role, out string configuredSerial) &&
                        !string.IsNullOrWhiteSpace(configuredSerial) &&
                        string.Equals(configuredSerial, runtimeSerial, StringComparison.OrdinalIgnoreCase))
                    {
                        MainWindow.logger.Info(
                            $"Job ID {job.Key} ({job.Value}) usa il ruolo '{role}' dal runtime e combacia col seriale macchina '{runtimeSerial}', anche senza match esplicito in CameraConfig.xml.");
                    }
                    else
                    {
                        MainWindow.logger.Warn(
                            $"Job ID {job.Key} ({job.Value}) usa il ruolo '{role}' dal runtime senza match esplicito in CameraConfig.xml. Seriale runtime: {runtimeSerial ?? "n/a"}.");
                    }
                }

                resolved.Add(new CameraViewModel
                {
                    CameraId = job.Key,
                    CameraType = ResolveRuntimeDisplayRole(job.Value, role, configuredCamera),
                    JobName = job.Value,
                    IsJobAvailable = true
                });

                if (hasConfigMatch)
                {
                    configuredByRole.Remove(role);
                }
            }

            var activeRuntimeSerials = new HashSet<string>(
                runtimeJobSerials.Values.Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var leftoverCamera in configuredByRole.Values.OrderBy(camera => camera.Id))
            {
                string normalizedRole = CameraConfigurationHelper.NormalizeCameraType(leftoverCamera.Type);
                configuredSerialsByRole.TryGetValue(normalizedRole, out string configuredSerial);

                if (!string.IsNullOrWhiteSpace(configuredSerial) &&
                    activeRuntimeSerials.Contains(configuredSerial))
                {
                    MainWindow.logger.Info(
                        $"Telecamera configurata '{leftoverCamera.Type}' (ID {leftoverCamera.Id}) non esposta come vista dedicata, ma il seriale macchina '{configuredSerial}' risulta gia' presente in un job runtime.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(configuredSerial) &&
                    configuredSerialRoleCounts.TryGetValue(configuredSerial, out int roleCount) &&
                    roleCount > 1)
                {
                    MainWindow.logger.Info(
                        $"Telecamera configurata '{leftoverCamera.Type}' (ID {leftoverCamera.Id}) non trovata nei job attivi, ma il seriale '{configuredSerial}' e' condiviso tra piu' ruoli macchina: warning declassato per evitare falsi positivi.");
                    continue;
                }

                MainWindow.logger.Warn(
                    $"Telecamera configurata '{leftoverCamera.Type}' (ID {leftoverCamera.Id}) non trovata nei job attivi del VPP. Seriale atteso: {configuredSerial ?? "n/a"}.");
            }

            EnsureVirtualTop2DView(resolved);
            MainWindow.logger.Info($"Telecamere runtime caricate: {resolved.Count}");
            return resolved;
        }

        private static string ResolveConfiguredDisplayRole(CameraModel camera)
        {
            if (camera == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(camera.DisplayType)
                ? CameraConfigurationHelper.NormalizeCameraDisplayType(camera.Type)
                : camera.DisplayType;
        }

        private static string ResolveRuntimeDisplayRole(string jobName, string runtimeRole, CameraModel configuredCamera)
        {
            string roleFromJobName = CameraConfigurationHelper.NormalizeCameraDisplayType(jobName);
            if (SupportedCameraTypes.Contains(roleFromJobName))
            {
                return roleFromJobName;
            }

            string configuredDisplayRole = ResolveConfiguredDisplayRole(configuredCamera);
            return SupportedCameraTypes.Contains(configuredDisplayRole)
                ? configuredDisplayRole
                : CameraConfigurationHelper.NormalizeCameraDisplayType(runtimeRole);
        }

        private static void EnsureVirtualTop2DView(List<CameraViewModel> resolved)
        {
            if (resolved == null || resolved.Count != 1)
            {
                return;
            }

            var top3DView = resolved.FirstOrDefault(camera =>
                string.Equals(camera.CameraType, "top3d", StringComparison.OrdinalIgnoreCase));

            if (top3DView == null)
            {
                return;
            }

            resolved.Add(new CameraViewModel
            {
                CameraId = top3DView.CameraId,
                CameraType = "side",
                JobName = $"{top3DView.JobName}_Top2DVirtual",
                IsJobAvailable = top3DView.IsJobAvailable
            });

            MainWindow.logger.Info("Added virtual Top2D operator view alongside the single Top3D runtime job.");
        }

        private static bool HasHardwareCamera(int jobId)
        {
            try
            {
                return MainWindow._cognexManager?.GetJob(jobId)?.AcqFifo?.FrameGrabber != null;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveCameraRole(int jobId, string jobName)
        {
            string roleFromJobName = CameraConfigurationHelper.NormalizeCameraType(jobName);
            if (CameraConfigurationHelper.IsSupportedCameraRole(roleFromJobName))
            {
                return roleFromJobName;
            }

            string roleFromSerial = CameraConfigurationHelper.ResolveCameraRoleFromRuntimeSerial(jobId);
            if (CameraConfigurationHelper.IsSupportedCameraRole(roleFromSerial))
            {
                return roleFromSerial;
            }

            if (MainWindow.JobRoleMapping != null && MainWindow.JobRoleMapping.TryGetValue(jobId, out string configuredRole))
            {
                return CameraConfigurationHelper.NormalizeCameraType(configuredRole);
            }

            return roleFromJobName;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Lightweight descriptor bound by the camera ItemsControl.
    /// </summary>
    public class CameraViewModel : INotifyPropertyChanged
    {
        private int _cameraId;
        private string _cameraType;
        private string _jobName;
        private bool _isJobAvailable;
        private double _displayHostHeight = 230;
        private double _displayHostMinHeight = 145;

        public int CameraId
        {
            get => _cameraId;
            set
            {
                _cameraId = value;
                OnPropertyChanged();
            }
        }

        public string CameraType
        {
            get => _cameraType;
            set
            {
                _cameraType = value;
                OnPropertyChanged();
            }
        }

        public string JobName
        {
            get => _jobName;
            set
            {
                _jobName = value;
                OnPropertyChanged();
            }
        }

        public bool IsJobAvailable
        {
            get => _isJobAvailable;
            set
            {
                _isJobAvailable = value;
                OnPropertyChanged();
            }
        }

        public double DisplayHostHeight
        {
            get => _displayHostHeight;
            set
            {
                if (Math.Abs(_displayHostHeight - value) < 0.1)
                {
                    return;
                }

                _displayHostHeight = value;
                OnPropertyChanged();
            }
        }

        public double DisplayHostMinHeight
        {
            get => _displayHostMinHeight;
            set
            {
                if (Math.Abs(_displayHostMinHeight - value) < 0.1)
                {
                    return;
                }

                _displayHostMinHeight = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
