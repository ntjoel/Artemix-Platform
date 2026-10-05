using Cognex.Vision;
using Cognex.VisionPro;
using Cognex.VisionPro.Exceptions;
using Cognex.VisionPro.Interop;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using NLog;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views.UserControls.DisplayRecord;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Cognex.VisionPro.QuickBuild.CogJobManager;

namespace QtisVisionPanel.Cls_Vpro
{
    public interface ICognexJobManager
    {
        void Initialize(string vppPath);
        // Metodi LiveView esistenti...
        void StartLiveDisplay(CogRecordDisplay display, int jobIndex);
        void StopLiveDisplay(CogRecordDisplay display);
        bool IsLiveDisplayRunning(CogRecordDisplay display);
        void RestoreHardwareMode(int jobIndex);

        void StartJobs();
        void StopJobs();
        CogJob GetJob(int index);
        // MODIFICATO: Aggiunto parametro triggerId

        void RunContinuous(CancellationToken cancellationToken);
        void RunOnce(int jobIndex, CancellationToken cancellationToken);
        void StopContinuousRunAsync();
        void Dispose();

        bool IsRunningContinuously { get; }
        string LastContinuousRunFailure { get; }
        CogJobManager GetJobManager();
        int GetJobIndexFromName(CogJobManager mgr, string name);
        int JobCount { get; }
        int GetGigEAcquisitionJobCount();
        IReadOnlyList<string> GetInvalidGigEAcquisitionJobs();

        event CogUserResultAvailableEventHandler UserResultAvailable;
        event EventHandler<CogJobManagerActionEventArgs> JobStopped;
      
    }
    public class CognexJobManager : ICognexJobManager, IDisposable
    {
        private const int ContinuousRunStartMaxAttempts = 3;
        private const int ContinuousRunAcquisitionReadyWaitMs = 1500;
        private const int ContinuousRunStateWaitMs = 1500;
        private const int ContinuousRunRetryDelayMs = 500;
        private const int ContinuousRunInterJobDelayMs = 500;
        private const int ContinuousRunPollIntervalMs = 100;

        private CogJobManager jobManager;
        private readonly List<CogJob> jobs = new List<CogJob>();
        private CancellationTokenSource _continuousRunCts;
        private bool _isRunningContinuously;
        private string _lastContinuousRunFailure;
        private readonly object _runLock = new object();
        private readonly object _liveDisplayLock = new object();
        private CogUserResultAvailableEventHandler _jobManagerUserResultHandler;
        private CogJobManagerStoppedEventHandler _jobManagerStoppedHandler;
        private readonly ConcurrentDictionary<CogRecordDisplay, Cognex.VisionPro.ICogAcqFifo> _liveDisplayFifos =
    new ConcurrentDictionary<CogRecordDisplay, Cognex.VisionPro.ICogAcqFifo>();

        public bool IsRunningContinuously
        {
            get
            {
                lock (_runLock)
                {
                    return _isRunningContinuously;
                }
            }
        }
        public int JobCount => jobs.Count;

        public int GetGigEAcquisitionJobCount()
        {
            return jobs.Count(IsGigEAcquisitionJob);
        }

        public IReadOnlyList<string> GetInvalidGigEAcquisitionJobs()
        {
            return jobs
                .Where(IsGigEAcquisitionJob)
                .Where(job => !IsAcquisitionReady(job))
                .Select(job => $"{job.Name} ({DescribeAcquisition(job)})")
                .ToList();
        }

        public string LastContinuousRunFailure
        {
            get
            {
                lock (_runLock)
                {
                    return _lastContinuousRunFailure;
                }
            }
        }

        public event CogUserResultAvailableEventHandler UserResultAvailable;
        public event EventHandler<CogJobManagerActionEventArgs> JobStopped;

        public void Initialize(string vppPath)
        {
            try
            {
                if (!ValidateVppFile(vppPath))
                {
                    MainWindow.logger.Error($"Invalid VPP file: {vppPath}");
                    return;
                }
                // Try to load with enhanced error handling
                jobManager = LoadVppFileSafely(vppPath);


                // jobManager = (CogJobManager)CogSerializer.LoadObjectFromFile(vppPath);
                if (jobManager == null)
                {
                    MainWindow.logger.Error($"Failed to load VPP file: {vppPath}");
                    return;
                }
                jobManager.UserQueueResultCreation = CogUserQueueResultCreationConstants.Always;
                jobManager.FailureQueueFlush();
                jobManager.UserQueueFlush();
                jobs.Clear();

                // PULISCI E POPOLA LA DICTIONARY
                MainWindow.JobMapping.Clear();
                MainWindow.JobRoleMapping.Clear();


                for (int i = 0; i < jobManager.JobCount; i++)
                {


                    var job = jobManager.Job(i);
                    job.OwnedIndependent.RealTimeQueueFlush(); // Imposta OwnedIndependent a false
                    job.ImageQueueFlush();

                    // AGGIUNGI ALLA DICTIONARY - Job ID -> Job Name
                    MainWindow.JobMapping[i] = job.Name;

                    if (job.AcqFifoState == CogJobAcqFifoStateConstants.Valid)
                    {
                        MainWindow.logger.Info($"Job {job.Name} has valid AcqFifoState.");
                    }
                    else
                    {
                        MainWindow.logger.Warn($"Job {job.Name} has invalid AcqFifoState: {job.AcqFifoState}");
                    }
                    job.ThroughputAlgorithm = CogJobThroughputAlgorithmConstants.MovingAverage;

                    jobs.Add(job);
                }

                string cameraConfigPath = Path.Combine(
                    MainWindow.configManager.Config.Configuration.Recipe_Folder,
                    "CameraConfig.xml");

                MainWindow.JobRoleMapping = CameraConfigurationHelper.BuildRoleMapping(cameraConfigPath, MainWindow.JobMapping);
                if (MainWindow.JobRoleMapping.Count == 0)
                {
                    MainWindow.logger.Warn($"Job role mapping non disponibile da CameraConfig.xml ({cameraConfigPath}). Verranno usati fallback sui nomi job.");
                }

                foreach (var job in jobs)
                {
                    loadjobs(job);
                }

                _jobManagerUserResultHandler = OnJobManagerUserResultAvailable;
                jobManager.UserResultAvailable -= _jobManagerUserResultHandler;
                jobManager.UserResultAvailable += _jobManagerUserResultHandler;

                _jobManagerStoppedHandler = OnJobManagerStopped;
                jobManager.Stopped -= _jobManagerStoppedHandler;
                jobManager.Stopped += _jobManagerStoppedHandler;
                // Log della dictionary creata
                MainWindow.logger.Info($"Job Mapping created with {MainWindow.JobMapping.Count} entries");
                foreach (var mapping in MainWindow.JobMapping)
                {
                    MainWindow.logger.Info($"Job ID: {mapping.Key} -> Job Name: {mapping.Value}");
                }

                foreach (var mapping in MainWindow.JobRoleMapping)
                {
                    MainWindow.logger.Info($"Job ID: {mapping.Key} -> Camera Role: {mapping.Value}");
                }
            }
            catch (CogException ex)
            {
                MainWindow.logger.Error($"cogjob cannot initialize {ex.Message} \n {ex.StackTrace}");
            }



        }

        public void loadjobs(CogJob job)
        {
            try
            {
                if (MainWindow.JobMapping.Count == 0) return;
                if (MainWindow.JobMapping.Values.Contains(job.Name))
                {
                    int jobId = MainWindow.JobMapping.FirstOrDefault(x => x.Value == job.Name).Key;
                    string cameraRoleFromJobName = CameraConfigurationHelper.NormalizeCameraType(job.Name);
                    string cameraRole = CameraConfigurationHelper.IsSupportedCameraRole(cameraRoleFromJobName)
                        ? cameraRoleFromJobName
                        : (MainWindow.JobRoleMapping.TryGetValue(jobId, out string configuredRole)
                            ? CameraConfigurationHelper.NormalizeCameraType(configuredRole)
                            : cameraRoleFromJobName);

                    switch (cameraRole)
                    {
                        case "top":
                        case "top3d":
                            MainWindow._topJob = job;
                            MainWindow._topToolGroup = MainWindow._topJob?.VisionTool as CogToolGroup;

                            MainWindow._topToolBlockMmpx = GetToolBlockIfExists(MainWindow._topToolGroup, "Shape");
                            MainWindow._topToolBlockReults = GetToolBlockIfExists(MainWindow._topToolGroup, "Results");
                            

                            break;
                        case "side":
                        case "left":
                            MainWindow. _sideJob = job;
                            MainWindow. _sideToolGroup = MainWindow._sideJob?.VisionTool as CogToolGroup;
                            MainWindow. _sideToolBlockMmpx = GetToolBlockIfExists(MainWindow._sideToolGroup, "Shape");
                            MainWindow. _sideToolBlockResults = GetToolBlockIfExists(MainWindow._sideToolGroup, "Results");

                            break;
                        case "front":
                            MainWindow._frontJob = job;
                            MainWindow. _FrontToolGroup = MainWindow._frontJob?.VisionTool as CogToolGroup;
                            MainWindow. _frontTBResults = GetToolBlockIfExists(MainWindow._FrontToolGroup, "Results");

                            break;
                        case "rear":
                            MainWindow. _rearJob = job;
                            MainWindow. _RearToolGroup = MainWindow._rearJob?.VisionTool as CogToolGroup;
                            MainWindow._RearTBResults = GetToolBlockIfExists(MainWindow._RearToolGroup, "Results");
                            break;
                        case "right":
                            MainWindow._rightJob = job;
                            MainWindow._RightTBResults = GetToolBlockIfExists(job.VisionTool as CogToolGroup, "Results");
                            break;
                        case "bottom":
                            MainWindow. _bottomJob = job;
                            MainWindow._BottomToolGroup = MainWindow._bottomJob?.VisionTool as CogToolGroup;
                            MainWindow._bottomTBResults = GetToolBlockIfExists(MainWindow._BottomToolGroup, "Results");
                            break;
                        default:
                            MainWindow.logger.Warn($"Unknown camera role '{cameraRole}' for job ID: {jobId} and job name: {job.Name}");
                            break;
                    }
                }

            }
            catch (Exception ex)
            {
                MainWindow.logger.Error(ex, "Error loading job");
            }

        }
        private static CogToolBlock GetToolBlockIfExists(CogToolGroup toolGroup, string toolName)
        {
            if (toolGroup == null || string.IsNullOrWhiteSpace(toolName) || !toolGroup.Tools.Contains(toolName))
            {
                return null;
            }

            return toolGroup.Tools[toolName] as CogToolBlock;
        }
        private bool ValidateVppFile(string vppPath)
        {
            try
            {
                if (string.IsNullOrEmpty(vppPath))
                {
                    MainWindow.logger.Error("VPP path is null or empty");
                    return false;
                }

                if (!File.Exists(vppPath))
                {
                    MainWindow.logger.Error($"VPP file does not exist: {vppPath}");
                    return false;
                }

                var fileInfo = new FileInfo(vppPath);
                if (fileInfo.Length == 0)
                {
                    MainWindow.logger.Error($"VPP file is empty: {vppPath}");
                    return false;
                }

                // Check if file is accessible
                using (var stream = File.Open(vppPath, FileMode.Open, FileAccess.Read))
                {
                    return stream.Length > 0;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error validating VPP file: {ex.Message}");
                return false;
            }
        }

        private CogJobManager LoadVppFileSafely(string vppPath)
        {
            try
            {
                VisionProRuntimeBootstrap.EnsureInitialized();
                // Primo tentativo: caricamento standard
                return (CogJobManager)CogSerializer.LoadObjectFromFile(vppPath);
            }
            catch (SerializationException ex)
            {
                MainWindow.logger.Warn($"Caricamento standard fallito, tentativo con approccio alternativo... {ex.Message}");

                // Secondo tentativo: prova con diversi parametri di serializzazione (solo 3 argomenti supportati)
                try
                {
                    return (CogJobManager)CogSerializer.LoadObjectFromFile(
                        vppPath,
                        typeof(System.Runtime.Serialization.Formatters.Binary.BinaryFormatter),
                        CogSerializationOptionsConstants.All);
                }
                catch (Exception secondEx)
                {
                    MainWindow.logger.Error(
                        "Secondo tentativo di caricamento VPP fallito: " +
                        VisionProRuntimeBootstrap.DescribeException(secondEx));
                    return null;
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                MainWindow.logger.Error($"Errore imprevisto durante il caricamento del VPP: {ex.Message}");
                if (ex.LoaderExceptions != null)
                {
                    foreach (var loaderException in ex.LoaderExceptions.Where(x => x != null))
                    {
                        MainWindow.logger.Error($"VPP LoaderException: {loaderException.Message}");
                    }
                }
                return null;
            }
            catch (CogException ex)
            {
                MainWindow.logger.Error(
                    "CogException durante il caricamento del VPP: " +
                    VisionProRuntimeBootstrap.DescribeException(ex));
                return null;
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error(
                    "Errore imprevisto durante il caricamento del VPP: " +
                    VisionProRuntimeBootstrap.DescribeException(ex) +
                    " | " + VisionProRuntimeBootstrap.DescribeRuntimeLocations());
                return null;
            }
        }


        public void StartJobs()
        {
            foreach (var job in jobs)
            {
                job.Run();
                // Imposta la modalità di esecuzione su Manuale

            }
        }

        public void StopJobs()
        {
            foreach (var job in jobs)
            {
                try
                {
                    if (job.RunningSyncBoolean.Value)
                    {
                        job.Stop();

                    }
                }
                catch { /* Ignore stop errors */ }
            }
        }
        public void RunContinuous(CancellationToken cancellationToken)
        {
            CancellationToken runToken;

            lock (_runLock)
            {
                RefreshContinuousRunState();
                if (_isRunningContinuously)
                {
                    MainWindow.logger.Debug("RunContinuous già in esecuzione.");
                    return;
                }

                _continuousRunCts?.Cancel();
                _continuousRunCts?.Dispose();
                _continuousRunCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _isRunningContinuously = false;
                _lastContinuousRunFailure = null;
                MainWindow._isContinuousRunActive = false;
                runToken = _continuousRunCts.Token;
            }

            try
            {
                StopAllLiveDisplays();
                ForceStopAllJobs(); // Assicura che tutti i job siano davvero fermi prima di ripartire

                var failedJobs = new List<string>();
                for (int jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
                {
                    // Preserve the established staggered startup so several GigE
                    // cameras do not all transition acquisition state together.
                    WaitWithCancellation(ContinuousRunInterJobDelayMs, runToken);

                    var job = jobs[jobIndex];
                    if (!TryStartJobContinuous(job, jobIndex, runToken, out string failureDetails))
                    {
                        failedJobs.Add(failureDetails);
                    }
                }

                RefreshContinuousRunState();

                if (failedJobs.Count > 0 || !IsRunningContinuously)
                {
                    string details = failedJobs.Count > 0
                        ? string.Join(" | ", failedJobs)
                        : "One or more VisionPro jobs did not report continuous running state.";

                    SetContinuousRunFailure(details);
                    MainWindow.logger.Error($"VISIONPRO_CONTINUOUS_START_INCOMPLETE|{details}");

                    // A partial run is unsafe: no product may be inspected with only
                    // a subset of the cameras required by the active VPP.
                    StopContinuousRunAsync();
                    return;
                }

                MainWindow.logger.Info($"VISIONPRO_CONTINUOUS_START_COMPLETE|jobs={jobs.Count}");
            }
            catch (OperationCanceledException)
            {
                SetContinuousRunFailure("VisionPro continuous start was cancelled.");
                StopContinuousRunAsync();
                throw;
            }
            catch (Exception ex)
            {
                SetContinuousRunFailure(ex.Message);
                MainWindow.logger.Error(ex, "VISIONPRO_CONTINUOUS_START_EXCEPTION");
                StopContinuousRunAsync(); // fallback in caso di errore
            }
        }

        public void RunOnce(int jobIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (jobManager == null)
            {
                throw new System.InvalidOperationException("VisionPro job manager non inizializzato.");
            }

            if (jobIndex < 0 || jobIndex >= jobs.Count)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(jobIndex),
                    jobIndex,
                    $"Indice job VisionPro non valido. Job disponibili: {jobs.Count}.");
            }

            CogJob selectedJob = jobs[jobIndex];
            string jobName = selectedJob?.Name ?? $"Job {jobIndex}";

            try
            {
                // The editor owns a single selected job. Running the whole manager would
                // also arm unrelated cameras and lets one invalid job fail this command.
                StopAllLiveDisplays();
                ForceStopAllJobs();
                cancellationToken.ThrowIfCancellationRequested();

                if (selectedJob == null)
                {
                    throw new System.InvalidOperationException($"Job VisionPro {jobIndex} non disponibile.");
                }

                if (IsGigEAcquisitionJob(selectedJob))
                {
                    WaitForAcquisitionReadiness(selectedJob, cancellationToken);
                    if (!IsAcquisitionReady(selectedJob))
                    {
                        throw new System.InvalidOperationException(
                            $"Acquisizione GigE non pronta per il job '{jobName}': {DescribeAcquisition(selectedJob)}");
                    }
                }

                MainWindow.logger.Info(
                    $"VISIONPRO_RUN_ONCE_START|jobIndex={jobIndex}|job={jobName}|{DescribeAcquisition(selectedJob)}");

                selectedJob.Run();
                RefreshContinuousRunState();

                MainWindow.logger.Info(
                    $"VISIONPRO_RUN_ONCE_STARTED|jobIndex={jobIndex}|job={jobName}|{DescribeAcquisition(selectedJob)}");
            }
            catch (Exception ex)
            {
                string details = VisionProRuntimeBootstrap.DescribeException(ex);
                MainWindow.logger.Error(
                    ex,
                    $"VISIONPRO_RUN_ONCE_FAILED|jobIndex={jobIndex}|job={jobName}|{DescribeAcquisition(selectedJob)}|{details}");

                TryStopJob(selectedJob);
                RefreshContinuousRunState();

                if (ex is OperationCanceledException)
                {
                    throw;
                }

                Exception rootCause = ex.GetBaseException();
                throw new System.InvalidOperationException(
                    $"RunOnce non riuscito per il job '{jobName}': {rootCause.Message}",
                    ex);
            }
        }

        public  void StopContinuousRunAsync()
        {
            CancellationTokenSource ctsToDispose = null;
            bool shouldStop;

            lock (_runLock)
            {
                bool hasAnyRunningJobs = AreAnyJobsRunning();
                shouldStop = _isRunningContinuously || hasAnyRunningJobs;
                if (!shouldStop)
                {
                    MainWindow.logger.Debug($"StopContinuousRun ignorato: nessuna esecuzione continua attiva.");
                    return;
                }

                ctsToDispose = _continuousRunCts;
                _continuousRunCts = null;
                _isRunningContinuously = false;
                MainWindow._isContinuousRunActive = false;
            }

            try
            {
                ctsToDispose?.Cancel();
            }
            catch
            {
            }

            try
            {
                if (jobManager != null)
                    jobManager.Stop();

                Thread.Sleep(100);
                ForceStopAllJobs();
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"errore generato dal metodo StopContinuousRun :{ex.Message}");
            }
            finally
            {
                try
                {
                    ctsToDispose?.Dispose();
                }
                catch
                {
                }

                RefreshContinuousRunState();
            }
        }

        public CogJob GetJob(int index) =>
            (index >= 0 && index < jobs.Count) ? jobs[index] : null;





        public CogJobManager GetJobManager() => jobManager;
        public int GetJobIndexFromName(CogJobManager mgr, string name)
        {
            if (mgr != null)
            {
                for (int i = 0; i < mgr.JobCount; ++i)
                    if (mgr.Job(i).Name == name)
                        return i;
            }
            return -1;
        }
        // Aggiungi questi metodi per il LiveView
        public void StartLiveDisplay(CogRecordDisplay display, int jobIndex)
        {
            try
            {
                if (display == null)
                {
                    MainWindow.logger.Error("Impossibile avviare il live: display nullo.");
                    return;
                }

                var job = GetJob(jobIndex);
                if (job == null || job.AcqFifo == null)
                {
                    MainWindow.logger.Warn($"Impossibile avviare il live: job {jobIndex} o FIFO non disponibile.");
                    return;
                }

                var liveFifo = PrepareLiveAcqFifo(job, jobIndex);
                if (liveFifo == null)
                {
                    throw new System.InvalidOperationException(
                        $"Live preview non disponibile per il job '{job.Name}': FIFO di acquisizione non inizializzata.");
                }

                lock (_liveDisplayLock)
                {
                    StopJobs();

                    if (display.LiveDisplayRunning)
                    {
                        display.StopLiveDisplay();
                    }

                    display.Image = null;
                    display.Record = null;
                    display.StaticGraphics?.Clear();
                    display.InteractiveGraphics?.Clear();
                    display.StartLiveDisplay(liveFifo, true);
                    _liveDisplayFifos[display] = liveFifo;
                }

                MainWindow.logger.Info($"Live display iniziato per Job {jobIndex} ({job.Name}).");
            }
            catch (System.InvalidCastException ex)
            {
                MainWindow.logger.Error($"Live display non compatibile con il controllo corrente: {ex.Message}");
                throw new System.InvalidOperationException(
                    "La Live Preview richiede una camera hardware associata al job. Il job corrente sta usando una FIFO sintetica di VisionPro/QuickBuild.",
                    ex);
            }
            catch (CogAcqCannotCreateFifoException ex)
            {
                MainWindow.logger.Error($"Cannot create FIFO – maybe camera port already in use with hardware trigger? {ex.Message}");
            }
            catch (System.InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error starting live display: {ex.Message} \n {ex.StackTrace}");
            }
        }

        public void StopLiveDisplay(CogRecordDisplay display)
        {
            try
            {
                if (display == null)
                {
                    MainWindow.logger.Error("CogRecordDisplay is null");
                    return;
                }

                var control = display as System.Windows.Forms.Control;
                if (control != null && (control.IsDisposed || !control.IsHandleCreated))
                {
                    _liveDisplayFifos.TryRemove(display, out _);
                    MainWindow.logger.Warn("Live display gia rilasciato dal controllo UI. Stop saltato.");
                    return;
                }

                try
                {
                    if (display.LiveDisplayRunning)
                    {
                        display.StopLiveDisplay();
                    }
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException ex)
                {
                    _liveDisplayFifos.TryRemove(display, out _);
                    MainWindow.logger.Warn($"Live display gia rilasciato (RCW): {ex.Message}");
                    return;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message.IndexOf("RCW", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _liveDisplayFifos.TryRemove(display, out _);
                        MainWindow.logger.Warn($"Live display COM gia rilasciato: {ex.Message}");
                        return;
                    }

                    throw;
                }

                try { display.StaticGraphics?.Clear(); } catch { }
                try { display.InteractiveGraphics?.Clear(); } catch { }
                try { display.Image = null; } catch { }
                try { display.Record = null; } catch { }
                _liveDisplayFifos.TryRemove(display, out _);
                MainWindow.logger.Info("Live display stopped");
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error stopping live display: {ex.Message}");
            }
        }

        public bool IsLiveDisplayRunning(CogRecordDisplay display)
        {
            try
            {
                return display?.LiveDisplayRunning ?? false;
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error checking live display status: {ex.Message}");
                return false;
            }
        }

        public void RestoreHardwareMode(int jobIndex)
        {
            try
            {
                var job = GetJob(jobIndex);
                if (job == null)
                {
                    MainWindow.logger?.Warn($"RestoreHardwareMode: job {jobIndex} non trovato.");
                    return;
                }

                double triggerDelay = GetTriggerDelayForJob(jobIndex);
                if (!MainWindow.ApplyCameraTriggerDelayToHardwareAtStartup)
                {
                    MainWindow.logger?.Info(
                        $"RestoreHardwareMode: scrittura TriggerDelay saltata per {job.Name}. Modalita I/O attiva, valore ricetta={triggerDelay}.");
                    return;
                }

                IgigaCameraAccess.Initialize(job, triggerDelay);
                MainWindow.logger?.Info(
                    $"Job {job.Name}: configurazione hardware ripristinata dopo Live Preview (trigger delay {triggerDelay}).");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(
                    $"Errore nel ripristino della modalità hardware per il job {jobIndex}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (jobManager != null)
            {
                if (_jobManagerUserResultHandler != null)
                {
                    jobManager.UserResultAvailable -= _jobManagerUserResultHandler;
                }

                if (_jobManagerStoppedHandler != null)
                {
                    jobManager.Stopped -= _jobManagerStoppedHandler;
                }
            }

            try
            {
                StopAllLiveDisplays();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore durante lo stop dei live display in chiusura: {ex.Message}");
            }

            StopContinuousRunAsync();
            StopJobs();

            try
            {
                jobManager?.Shutdown();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore durante il rilascio del job manager Cognex: {ex.Message}");
            }
            finally
            {
                _liveDisplayFifos.Clear();
                jobs.Clear();
                jobManager = null;
            }
        }

        private void OnJobManagerUserResultAvailable(object sender, CogJobManagerActionEventArgs e)
        {
            UserResultAvailable?.Invoke(sender, e);
        }

        private void OnJobManagerStopped(object sender, CogJobManagerActionEventArgs e)
        {
            lock (_runLock)
            {
                _isRunningContinuously = false;
                MainWindow._isContinuousRunActive = false;
            }

            JobStopped?.Invoke(sender, e);
        }

        private bool TryStartJobContinuous(
            CogJob job,
            int jobIndex,
            CancellationToken cancellationToken,
            out string failureDetails)
        {
            failureDetails = null;
            if (job == null)
            {
                failureDetails = $"jobIndex={jobIndex}; error=job is null";
                return false;
            }

            for (int attempt = 1; attempt <= ContinuousRunStartMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsJobRunning(job))
                {
                    MainWindow.logger.Info(
                        $"VISIONPRO_JOB_RUNNING|jobIndex={jobIndex}|job={job.Name}|attempt={attempt}|{DescribeAcquisition(job)}");
                    return true;
                }

                WaitForAcquisitionReadiness(job, cancellationToken);
                string acquisitionState = DescribeAcquisition(job);

                try
                {
                    MainWindow.logger.Info(
                        $"VISIONPRO_JOB_START_ATTEMPT|jobIndex={jobIndex}|job={job.Name}|attempt={attempt}/{ContinuousRunStartMaxAttempts}|{acquisitionState}");

                    job.RunContinuous();
                    if (WaitForJobRunning(job, ContinuousRunStateWaitMs, cancellationToken))
                    {
                        MainWindow.logger.Info(
                            $"VISIONPRO_JOB_RUNNING|jobIndex={jobIndex}|job={job.Name}|attempt={attempt}|{DescribeAcquisition(job)}");
                        return true;
                    }

                    failureDetails =
                        $"jobIndex={jobIndex}; job={job.Name}; attempt={attempt}; error=continuous state not reached; {DescribeAcquisition(job)}";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failureDetails =
                        $"jobIndex={jobIndex}; job={job.Name}; attempt={attempt}; error={ex.Message}; {DescribeAcquisition(job)}";
                }

                MainWindow.logger.Warn($"VISIONPRO_JOB_START_RETRY|{failureDetails}");

                if (attempt < ContinuousRunStartMaxAttempts)
                {
                    TryStopJob(job);
                    WaitWithCancellation(ContinuousRunRetryDelayMs, cancellationToken);
                }
            }

            failureDetails = failureDetails ??
                $"jobIndex={jobIndex}; job={job.Name}; error=continuous start failed; {DescribeAcquisition(job)}";
            return false;
        }

        private static void WaitForAcquisitionReadiness(CogJob job, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ContinuousRunAcquisitionReadyWaitMs);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (job?.AcqFifoState == CogJobAcqFifoStateConstants.Valid)
                    {
                        return;
                    }
                }
                catch
                {
                }

                WaitWithCancellation(ContinuousRunPollIntervalMs, cancellationToken);
            }
        }

        private static bool WaitForJobRunning(CogJob job, int timeoutMs, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsJobRunning(job))
                {
                    return true;
                }

                WaitWithCancellation(ContinuousRunPollIntervalMs, cancellationToken);
            }

            return IsJobRunning(job);
        }

        private static bool IsJobRunning(CogJob job)
        {
            try
            {
                return job?.RunningSyncBoolean?.Value == true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryStopJob(CogJob job)
        {
            try
            {
                job?.Stop();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"VISIONPRO_JOB_RETRY_STOP_FAILED|job={job?.Name}|error={ex.Message}");
            }
        }

        private static void WaitWithCancellation(int delayMs, CancellationToken cancellationToken)
        {
            if (delayMs <= 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            if (cancellationToken.WaitHandle.WaitOne(delayMs))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private static string DescribeAcquisition(CogJob job)
        {
            string fifoState = "unknown";
            string fifoType = "null";
            string grabberName = "pending";
            string serial = "pending";

            try
            {
                fifoState = job?.AcqFifoState.ToString() ?? "null";
                var fifo = job?.AcqFifo;
                fifoType = GetFifoTypeName(fifo);
                grabberName = fifo?.FrameGrabber?.Name ?? "pending";
                serial = fifo?.FrameGrabber?.SerialNumber ?? "pending";
            }
            catch (Exception ex)
            {
                return $"acqState={fifoState}; fifo={fifoType}; grabber={grabberName}; serial={serial}; diagnosticError={ex.Message}";
            }

            return $"acqState={fifoState}; fifo={fifoType}; grabber={grabberName}; serial={serial}";
        }

        private static bool IsGigEAcquisitionJob(CogJob job)
        {
            try
            {
                string fifoType = GetFifoTypeName(job?.AcqFifo);
                return fifoType.IndexOf("FGGigE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       fifoType.IndexOf("AcqFifoGigE", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAcquisitionReady(CogJob job)
        {
            try
            {
                return job != null &&
                       job.AcqFifoState == CogJobAcqFifoStateConstants.Valid &&
                       job.AcqFifo?.FrameGrabber != null;
            }
            catch
            {
                return false;
            }
        }

        private void SetContinuousRunFailure(string details)
        {
            lock (_runLock)
            {
                _lastContinuousRunFailure = details;
                _isRunningContinuously = false;
                MainWindow._isContinuousRunActive = false;
            }
        }

        private void RefreshContinuousRunState()
        {
            lock (_runLock)
            {
                _isRunningContinuously = jobs.Count > 0 && jobs.All(job =>
                {
                    try
                    {
                        return job?.RunningSyncBoolean?.Value == true;
                    }
                    catch
                    {
                        return false;
                    }
                });
                MainWindow._isContinuousRunActive = _isRunningContinuously;
            }
        }

        private bool AreAnyJobsRunning()
        {
            return jobs.Any(job =>
            {
                try
                {
                    return job?.RunningSyncBoolean?.Value == true;
                }
                catch
                {
                    return false;
                }
            });
        }

        private void ForceStopAllJobs()
        {
            try
            {
                if (jobManager != null)
                {
                    jobManager.Stop();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore durante lo stop globale del job manager: {ex.Message}");
            }

            foreach (var job in jobs)
            {
                try
                {
                    if (job?.RunningSyncBoolean?.Value == true)
                    {
                        job.Stop();
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn($"Errore durante lo stop del job {job?.Name}: {ex.Message}");
                }
            }

            Thread.Sleep(150);
            RefreshContinuousRunState();
        }

        private void StopAllLiveDisplays()
        {
            foreach (var display in _liveDisplayFifos.Keys.ToList())
            {
                try
                {
                    StopLiveDisplay(display);
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn($"Errore stop live display durante switch run mode: {ex.Message}");
                }
            }
        }

        private Cognex.VisionPro.ICogAcqFifo PrepareLiveAcqFifo(CogJob job, int jobIndex)
        {
            var currentFifo = job?.AcqFifo;
            if (currentFifo == null)
            {
                MainWindow.logger.Warn(
                    $"Job {job?.Name}: AcqFifo nulla prima della Live Preview. Tentativo di reinizializzazione hardware.");

                TryReinitializeJobAcqFifo(job, jobIndex);
                currentFifo = job?.AcqFifo;

                if (currentFifo == null)
                {
                    MainWindow.logger.Error(
                        $"Job {job?.Name}: AcqFifo ancora nulla dopo la reinizializzazione hardware.");
                    return null;
                }
            }

            if (IsCompatibleLiveFifo(currentFifo))
            {
                PrepareExistingFifoForLivePreview(currentFifo, jobIndex, job?.Name);
                MainWindow.logger.Info(
                    $"Job {job?.Name}: Live Preview usera la FIFO corrente {GetFifoTypeName(currentFifo)}.");
                return currentFifo;
            }

            MainWindow.logger.Warn(
                $"Job {job?.Name} usa una FIFO non compatibile con la Live Preview: {GetFifoTypeName(currentFifo)}. Tentativo di reinizializzazione hardware.");

            TryReinitializeJobAcqFifo(job, jobIndex);
            currentFifo = job?.AcqFifo;

            if (currentFifo != null && IsCompatibleLiveFifo(currentFifo))
            {
                PrepareExistingFifoForLivePreview(currentFifo, jobIndex, job?.Name);
                MainWindow.logger.Info($"Job {job?.Name}: FIFO live compatibile ripristinata ({GetFifoTypeName(currentFifo)}).");
                return currentFifo;
            }

            string currentFifoType = GetFifoTypeName(currentFifo);
            MainWindow.logger.Error(
                $"Job {job?.Name}: impossibile ottenere una FIFO compatibile per la Live Preview. Tipo corrente: {currentFifoType}");
            return null;
        }

        private static bool IsCompatibleLiveFifo(Cognex.VisionPro.ICogAcqFifo fifo)
        {
            if (fifo == null)
            {
                return false;
            }

            string fifoTypeName = GetFifoTypeName(fifo);

            if (fifoTypeName.IndexOf("Synthetic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (fifoTypeName.IndexOf("FGGigE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fifoTypeName.IndexOf("AcqFifoGigE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fifoTypeName.IndexOf("CogAcqFifo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            try
            {
                if (fifo.FrameGrabber != null)
                {
                    return true;
                }

                if (fifo.OwnedTriggerParams != null)
                {
                    return true;
                }
            }
            catch
            {
            }

            return !string.IsNullOrWhiteSpace(fifoTypeName);
        }

        private static string GetFifoTypeName(Cognex.VisionPro.ICogAcqFifo fifo)
        {
            if (fifo == null)
            {
                return "null";
            }

            try
            {
                return fifo.GetType().FullName ?? fifo.GetType().Name ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private void PrepareExistingFifoForLivePreview(Cognex.VisionPro.ICogAcqFifo fifo, int jobIndex, string jobName)
        {
            if (fifo == null)
            {
                return;
            }

            try
            {
                double triggerDelay = GetTriggerDelayForJob(jobIndex);
                IgigaCameraAccess.EnableLivePreviewMode(fifo, triggerDelay);

                MainWindow.logger?.Info(
                    $"Job {jobName}: FIFO live pronta ({fifo.GetType().FullName}, Grabber: {fifo.FrameGrabber?.Name}).");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(
                    $"Job {jobName}: impossibile preparare la FIFO corrente per la Live Preview: {ex.Message}");
            }
        }

        private void TryReinitializeJobAcqFifo(CogJob job, int jobIndex)
        {
            try
            {
                double triggerDelay = GetTriggerDelayForJob(jobIndex);
                if (!MainWindow.ApplyCameraTriggerDelayToHardwareAtStartup)
                {
                    MainWindow.logger?.Info(
                        $"Job {job?.Name}: reinizializzazione AcqFifo senza scrittura TriggerDelay. Modalita I/O attiva, valore ricetta={triggerDelay}.");
                    return;
                }

                IgigaCameraAccess.Initialize(job, triggerDelay);
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Errore durante la reinizializzazione della FIFO live per il job {job?.Name}: {ex.Message}");
            }
        }

        private static double GetTriggerDelayForJob(int jobIndex)
        {
            try
            {
                var cameraSetting = MainWindow.ConfigRecipeParam?.Config?.cameraSetting;
                if (cameraSetting == null)
                {
                    return 0;
                }

                switch (CameraConfigurationHelper.NormalizePhysicalCameraRole(GetCameraRoleForJob(jobIndex)))
                {
                    case "top":
                        return cameraSetting.TopCameraTriggerDelay;
                    case "left":
                        return cameraSetting.ResolveLeftCameraTriggerDelay();
                    case "front":
                        return cameraSetting.FrontCameraTriggerDelay;
                    case "right":
                        return cameraSetting.RightCameraTriggerDelay;
                    case "bottom":
                        return cameraSetting.BottomCameraTriggerDelay;
                    default:
                        return 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static string GetCameraRoleForJob(int jobIndex)
        {
            if (MainWindow.JobRoleMapping != null && MainWindow.JobRoleMapping.TryGetValue(jobIndex, out string role))
            {
                return CameraConfigurationHelper.NormalizeCameraType(role);
            }

            if (MainWindow.JobMapping != null && MainWindow.JobMapping.TryGetValue(jobIndex, out string jobName))
            {
                return CameraConfigurationHelper.NormalizeCameraType(jobName);
            }

            return string.Empty;
        }
    }
}
