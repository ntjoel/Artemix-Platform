using Cognex.VisionPro;
using Cognex.VisionPro.QuickBuild;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CogAcqFifoPixelFormatConstants = Cognex.VisionPro.CogAcqFifoPixelFormatConstants;
using CogFrameGrabbers = Cognex.VisionPro.CogFrameGrabbers;

namespace QtisVisionPanel.Cls_Vpro
{
    public sealed class CameraCyclingPresetResult
    {
        public bool Success { get; set; }

        public bool ResetCommandExecuted { get; set; }

        public uint? CurrentActiveSet { get; set; }

        public string CameraName { get; set; }

        public string SerialNumber { get; set; }

        public string ErrorMessage { get; set; }
    }

    public class IgigaCameraAccess
    {
        private const int JobReadyRetryCount = 8;
        private const int JobReadyRetryDelayMs = 125;
        private const int TriggerApplySettleMs = 75;
        public static Cognex.VisionPro.ICogAcqTrigger mTriggerTop, mTriggerSide;
        private static readonly Dictionary<string, string> _cameraSerialMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static int GetFrameGrabberInventory(out string description)
        {
            var devices = new List<string>();

            try
            {
                var frameGrabbers = new CogFrameGrabbers();
                foreach (ICogFrameGrabber frameGrabber in frameGrabbers)
                {
                    string name;
                    string serial;

                    try { name = frameGrabber?.Name ?? "unknown"; }
                    catch { name = "unavailable"; }

                    try { serial = frameGrabber?.SerialNumber ?? "unknown"; }
                    catch { serial = "unavailable"; }

                    devices.Add($"{name} [{serial}]");
                }

                devices.Sort(StringComparer.OrdinalIgnoreCase);
                description = devices.Count > 0
                    ? string.Join(", ", devices)
                    : "none";
                return frameGrabbers.Count;
            }
            catch (Exception ex)
            {
                description = $"inventory-error={ex.Message}";
                return 0;
            }
        }

        public static void AcqFifoConstruction(Cognex.VisionPro.ICogAcqFifo fifo, double triggerDelay)
        {
            Cognex.VisionPro.ICogGigEAccess mCameraAccess = fifo.FrameGrabber.OwnedGigEAccess;

            if (mCameraAccess != null && IsCorrectCamera(fifo.FrameGrabber.Name))
            {
                try
                {
                    ConfigureTrigger(mCameraAccess, triggerDelay);
                    MainWindow.logger?.Info("TriggerActivation feature set successfully.\n" + fifo.FrameGrabber.Name + " " + fifo.FrameGrabber.SerialNumber);
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Error("Access failure: " + ex.Message);
                }
            }
            else
            {
                string cameraName = fifo?.FrameGrabber?.Name ?? "camera sconosciuta";
                MainWindow.logger?.Warn(
                    $"Impossibile applicare TriggerActivation al job: interfaccia GigE non disponibile o camera non identificata correttamente ({cameraName}).");
            }
        }

        public static void Initialize(CogJob jobParam, double TriggerDelay)
        {
            try
            {
                if (jobParam == null)
                {
                    MainWindow.logger?.Error("Job parameter is null.");
                    return;
                }

                if (jobParam.AcqFifo != null)
                {
                    string jobName = jobParam.Name;

                    LoadCameraSerialMapping();
                    string expectedSerial = GetExpectedSerialForJob(jobName);
                    string currentSerial = jobParam.AcqFifo.FrameGrabber?.SerialNumber;
                    string currentFifoType = jobParam.AcqFifo.GetType().FullName;

                    MainWindow.logger?.Info($"Job {jobName}: Seriale atteso={expectedSerial}, Seriale attuale={currentSerial}, FIFO={currentFifoType}");

                    if (!string.IsNullOrEmpty(expectedSerial) && expectedSerial != currentSerial)
                    {
                        MainWindow.logger?.Info($"Seriale non corrispondente per job {jobName}. Ricerca telecamera corretta...");

                        if (!AssignCameraToJobBySerial(jobParam, expectedSerial))
                        {
                            MainWindow.logger?.Error($"Impossibile assegnare telecamera con seriale {expectedSerial} al job {jobName}");
                        }
                    }

                    if (!WaitForJobReady(jobParam))
                    {
                        MainWindow.logger?.Warn($"Job {jobName}: acquisizione non pronta per aggiornare TriggerDelay. Operazione rinviata.");
                        return;
                    }

                    PrepareJobForTriggerUpdate(jobParam);
                    SetHardwareTriggerMode(jobParam.AcqFifo);
                    AcqFifoConstruction(jobParam.AcqFifo, TriggerDelay);
                    FinalizeJobTriggerUpdate(jobParam);
                    VerifyAppliedTriggerDelay(jobParam, TriggerDelay);

                    MainWindow.logger?.Info($"Telecamera per job {jobName} inizializzata. " +
                        $"Seriale: {jobParam.AcqFifo.FrameGrabber?.SerialNumber}, " +
                        $"Trigger: Hardware, TriggerDelay richiesto: {TriggerDelay} μs");
                }
                else
                {
                    MainWindow.logger?.Info("This job does not contain an acquisition FIFO.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Errore nell'inizializzazione telecamera per job {jobParam?.Name}: {ex.Message} \n {ex.StackTrace}");
            }
        }

        private static bool WaitForJobReady(CogJob jobParam)
        {
            if (jobParam == null)
            {
                return false;
            }

            for (int attempt = 0; attempt < JobReadyRetryCount; attempt++)
            {
                try
                {
                    if (jobParam.AcqFifo != null &&
                        jobParam.AcqFifo.FrameGrabber != null &&
                        jobParam.AcqFifo.FrameGrabber.OwnedGigEAccess != null)
                    {
                        return true;
                    }
                }
                catch
                {
                }

                Task.Delay(JobReadyRetryDelayMs).GetAwaiter().GetResult();
            }

            return false;
        }

        private static void PrepareJobForTriggerUpdate(CogJob jobParam)
        {
            FlushPendingJobData(jobParam, "before-trigger-update");
        }

        private static void FinalizeJobTriggerUpdate(CogJob jobParam)
        {
            if (jobParam == null)
            {
                return;
            }

            FlushPendingJobData(jobParam, "after-trigger-update");
        }

        private static void FlushPendingJobData(CogJob jobParam, string stage)
        {
            try
            {
                jobParam?.ImageQueueFlush();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"ImageQueueFlush fallita per {jobParam?.Name} ({stage}): {ex.Message}");
            }

            try
            {
                jobParam?.OwnedIndependent?.RealTimeQueueFlush();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"RealTimeQueueFlush fallita per {jobParam?.Name} ({stage}): {ex.Message}");
            }

            try
            {
                var manager = MainWindow._cognexManager?.GetJobManager();
                manager?.FailureQueueFlush();
                manager?.UserQueueFlush();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Queue flush del job manager fallito per {jobParam?.Name} ({stage}): {ex.Message}");
            }

            Task.Delay(TriggerApplySettleMs).GetAwaiter().GetResult();
        }

        private static void VerifyAppliedTriggerDelay(CogJob jobParam, double expectedTriggerDelay)
        {
            var appliedTriggerDelay = ReadTriggerDelayFromCamera(jobParam, false);
            if (!appliedTriggerDelay.HasValue)
            {
                return;
            }

            double delta = Math.Abs(appliedTriggerDelay.Value - expectedTriggerDelay);
            if (delta > 0.5d)
            {
                MainWindow.logger?.Warn(
                    $"TriggerDelay verificato con scostamento per {jobParam?.Name}: richiesto={expectedTriggerDelay} μs, letto={appliedTriggerDelay.Value} μs");
                return;
            }

            MainWindow.logger?.Info(
                $"TriggerDelay verificato per {jobParam?.Name}: richiesto={expectedTriggerDelay} μs, letto={appliedTriggerDelay.Value} μs");
        }

        public static void EnableLivePreviewMode(Cognex.VisionPro.ICogAcqFifo acqFifo, double triggerDelay)
        {
            try
            {
                if (acqFifo == null)
                {
                    MainWindow.logger?.Warn("EnableLivePreviewMode: AcqFifo nulla.");
                    return;
                }

                var triggerParams = acqFifo.OwnedTriggerParams;
                if (triggerParams != null)
                {
                    triggerParams.TriggerEnabled = false;
                    triggerParams.TriggerModel = Cognex.VisionPro.CogAcqTriggerModelConstants.Auto;
                    triggerParams.TriggerEnabled = true;
                }

                var gigEAccess = acqFifo.FrameGrabber?.OwnedGigEAccess;
                if (gigEAccess != null)
                {
                    TrySetStringFeature(gigEAccess, "AcquisitionMode", "Continuous", acqFifo.FrameGrabber?.Name);
                    TrySetStringFeature(gigEAccess, "TriggerSelector", "FrameStart", acqFifo.FrameGrabber?.Name);
                    // TriggerMode is intentionally NOT overridden here. The camera (e.g. Teledyne DALSA Nano-M2020)
                    // rejects writes to this node while acquisition is active ("Node is not writable").
                    // VisionPro TriggerModel=Auto already provides continuous acquisition for LiveView
                    // without needing to switch the GigE camera to free-run at the hardware level.
                }

                MainWindow.logger?.Info(
                    $"Live Preview abilitata per {acqFifo.FrameGrabber?.Name ?? "camera sconosciuta"} (trigger hardware mantenuto, VisionPro Auto mode).");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore nell'abilitazione della Live Preview free-run: {ex.Message}");
            }
        }

        private static void LoadCameraSerialMapping()
        {
            try
            {
                _cameraSerialMapping.Clear();
                var config = MainWindow.configManager?.Config;

                if (config?.Configuration == null)
                {
                    MainWindow.logger?.Warn("Configurazione non disponibile per caricare i seriali delle telecamere");
                    return;
                }

                AddSerialToMapping("Top", GetConfigValue(() => config.Configuration.TopCameraSerial));
                AddSerialToMapping("Side", GetConfigValue(() => config.Configuration.SideCameraSerial));
                AddSerialToMapping("Front", GetConfigValue(() => config.Configuration.FrontCameraSerial));
                AddSerialToMapping("Rear", GetConfigValue(() => config.Configuration.RearCameraSerial));
                AddSerialToMapping("Bottom", GetConfigValue(() => config.Configuration.BottomCameraSerial));

                MainWindow.logger?.Info($"Mappatura telecamere caricata: {_cameraSerialMapping.Count} entries");
                foreach (var kvp in _cameraSerialMapping)
                {
                    MainWindow.logger?.Info($"  Job: {kvp.Key} -> Seriale: {kvp.Value}");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento mappatura telecamere: {ex.Message}");
            }
        }

        private static string GetConfigValue(Func<string> getter)
        {
            try { return getter(); } catch { return null; }
        }

        private static void AddSerialToMapping(string jobName, string serial)
        {
            if (!string.IsNullOrWhiteSpace(serial))
                _cameraSerialMapping[jobName] = serial;
        }

        private static string GetExpectedSerialForJob(string jobName)
        {
            if (string.IsNullOrEmpty(jobName)) return null;

            if (_cameraSerialMapping.TryGetValue(jobName, out string serial))
                return serial;

            foreach (var kvp in _cameraSerialMapping)
            {
                if (jobName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kvp.Value;
            }

            return null;
        }

        /// <summary>
        /// Assegna una telecamera specifica a un job in base al seriale
        /// CORREZIONE: Usa ICogFrameGrabber invece di CogFrameGrabber
        /// </summary>
        /// <summary>
        /// Assegna una telecamera specifica a un job in base al seriale
        /// <summary>
        /// Assegna una telecamera specifica a un job in base al seriale
        /// </summary>
        private static bool AssignCameraToJobBySerial(CogJob jobParam, string targetSerial)
        {
            try
            {
                if (string.IsNullOrEmpty(targetSerial))
                {
                    MainWindow.logger?.Warn("Seriale target non specificato");
                    return false;
                }

                CogFrameGrabbers frameGrabbers = new CogFrameGrabbers();

                if (frameGrabbers == null || frameGrabbers.Count == 0)
                {
                    MainWindow.logger?.Error("Nessun FrameGrabber disponibile");
                    return false;
                }

                MainWindow.logger?.Info($"Ricerca telecamera con seriale {targetSerial} tra {frameGrabbers.Count} dispositivi...");

                ICogFrameGrabber targetGrabber = null;

                foreach (ICogFrameGrabber grabber in frameGrabbers)
                {
                    string grabberSerial = grabber.SerialNumber;
                    MainWindow.logger?.Debug($"  Verificato: {grabber.Name} - Seriale: {grabberSerial}");

                    if (grabberSerial == targetSerial)
                    {
                        targetGrabber = grabber;
                        break;
                    }
                }

                if (targetGrabber == null)
                {
                    MainWindow.logger?.Error($"Nessuna telecamera trovata con seriale {targetSerial}");
                    return false;
                }

                MainWindow.logger?.Info($"Telecamera trovata: {targetGrabber.Name} (Seriale: {targetGrabber.SerialNumber})");

                var currentFifo = jobParam.AcqFifo;
                if (currentFifo?.FrameGrabber?.SerialNumber == targetSerial)
                {
                    MainWindow.logger?.Info("Il job ha già il FrameGrabber corretto assegnato");
                    return true;
                }

                // CORREZIONE: Per telecamere DALSA GigE Vision, usa il formato corretto
                string videoFormat = GetCorrectVideoFormat(targetGrabber, currentFifo);
                MainWindow.logger?.Info($"Usando formato video: {videoFormat}");

                try
                {
                    ICogAcqFifo newFifo = null;

                    // CORREZIONE: Usa reflection per chiamare CreateAcqFifo con i tipi corretti
                    // o usa dynamic ma assicurati che il formato sia string
                    dynamic dynamicGrabber = targetGrabber;

                    // Prova a creare l'AcqFifo
                    newFifo = dynamicGrabber.CreateAcqFifo(
                        videoFormat,
                        CogAcqFifoPixelFormatConstants.Format8Grey,
                        0,
                        false
                    );

                    if (newFifo != null)
                    {
                        // CORREZIONE CRITICA: Invece di usare reflection per sostituire AcqFifo
                        // (che potrebbe non funzionare), dobbiamo aggiornare il job in modo diverso
                        // Poiché AcqFifo è probabilmente read-only, logghiamo l'errore e suggeriamo
                        // la configurazione manuale, OPPURE usiamo un approccio alternativo

                        MainWindow.logger?.Info($"AcqFifo creato con successo per {targetGrabber.Name}");

                        // Tentativo di sostituzione via reflection
                        var acqFifoProperty = jobParam.GetType().GetProperty("AcqFifo");
                        if (acqFifoProperty != null && acqFifoProperty.CanWrite)
                        {
                            acqFifoProperty.SetValue(jobParam, newFifo);
                            MainWindow.logger?.Info($"AcqFifo sostituito con successo per il job {jobParam.Name}");
                            return true;
                        }
                        else
                        {
                            // Se non possiamo sostituire, dobbiamo ricreare il job o usare un altro metodo
                            MainWindow.logger?.Warn($"Proprietà AcqFifo è read-only per il job {jobParam.Name}. " +
                                $"Tentativo approccio alternativo...");

                            // Approccio alternativo: modifica il FrameGrabber esistente se possibile
                            return TryUpdateJobCamera(jobParam, newFifo, targetGrabber);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Error($"Errore nella creazione del nuovo AcqFifo: {ex.Message}");

                    // Se il formato non è supportato, prova con formati alternativi
                    if (ex.Message.Contains("video format is not supported"))
                    {
                        return TryAlternativeVideoFormats(jobParam, targetGrabber, targetSerial);
                    }

                    return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'assegnazione telecamera: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tenta di aggiornare la telecamera del job usando un approccio alternativo
        /// </summary>
        private static bool TryUpdateJobCamera(CogJob jobParam, ICogAcqFifo newFifo, ICogFrameGrabber targetGrabber)
        {
            try
            {
                // Opzione 1: Prova a usare un metodo interno del job per aggiornare l'AcqFifo
                var updateMethod = jobParam.GetType().GetMethod("UpdateAcqFifo",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (updateMethod != null)
                {
                    updateMethod.Invoke(jobParam, new object[] { newFifo });
                    MainWindow.logger?.Info($"AcqFifo aggiornato via metodo interno per il job {jobParam.Name}");
                    return true;
                }

                // Opzione 2: Se il job ha un metodo per ricaricare la configurazione
                var reloadMethod = jobParam.GetType().GetMethod("Reload",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (reloadMethod != null)
                {
                    // Questo potrebbe richiedere parametri aggiuntivi
                    MainWindow.logger?.Info($"Tentativo reload job {jobParam.Name}...");
                }

                // Se nessuna opzione funziona, logga istruzioni per configurazione manuale
                MainWindow.logger?.Error($"IMPOSSIBILE aggiornare automaticamente la telecamera per il job {jobParam.Name}. " +
                    $"CONFIGURAZIONE MANUALE RICHIESTA: Aprire VisionPro QuickBuild, selezionare il job '{jobParam.Name}', " +
                    $"e assegnare manualmente la telecamera con seriale {targetGrabber.SerialNumber}");

                return false;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'aggiornamento alternativo: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Prova formati video alternativi se il principale fallisce
        /// </summary>
        private static bool TryAlternativeVideoFormats(CogJob jobParam, ICogFrameGrabber targetGrabber, string targetSerial)
        {
            // Lista di formati comuni per telecamere DALSA GigE Vision
            string[] alternativeFormats = new string[]
            {
        "Generic GigEVision (Mono)",
        "Generic GigEVision (Mono12)",
        "Mono 8",
        "Mono 12",
        "Bayer GR 8",
        "Bayer RG 8",
        "YUV 422 (YUYV) Packed",
        "RGB 8"
            };

            MainWindow.logger?.Info($"Tentativo formati video alternativi per {targetGrabber.Name}...");

            foreach (string format in alternativeFormats)
            {
                try
                {
                    MainWindow.logger?.Debug($"  Provo formato: {format}");

                    dynamic dynamicGrabber = targetGrabber;
                    var newFifo = dynamicGrabber.CreateAcqFifo(
                        format,
                        CogAcqFifoPixelFormatConstants.Format8Grey,
                        0,
                        false
                    );

                    if (newFifo != null)
                    {
                        MainWindow.logger?.Info($"Formato '{format}' funzionante!");

                        // Tenta sostituzione
                        var acqFifoProperty = jobParam.GetType().GetProperty("AcqFifo");
                        if (acqFifoProperty != null && acqFifoProperty.CanWrite)
                        {
                            acqFifoProperty.SetValue(jobParam, newFifo);
                            MainWindow.logger?.Info($"Telecamera assegnata con formato alternativo: {format}");
                            return true;
                        }
                        else
                        {
                            return TryUpdateJobCamera(jobParam, newFifo, targetGrabber);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Debug($"  Formato '{format}' fallito: {ex.Message}");
                    continue;
                }
            }

            MainWindow.logger?.Error($"Nessun formato video funzionante trovato per {targetGrabber.Name}");
            return false;
        }

        /// <summary>
        /// Ottiene il formato video corretto per il frame grabber
        /// </summary>
        private static string GetCorrectVideoFormat(ICogFrameGrabber grabber, ICogAcqFifo currentFifo)
        {
            try
            {
                // Per telecamere DALSA GigE Vision, usa il formato standard
                if (grabber.Name.Contains("DALSA") || grabber.Name.Contains("GigE"))
                {
                    // Il formato corretto per DALSA è "Generic GigEVision (Mono)"
                    // come mostrato nello screenshot
                    return "Generic GigEVision (Mono)";
                }

                // Se c'è già un formato valido nel fifo corrente, usa quello
                if (currentFifo != null && !string.IsNullOrEmpty(currentFifo.VideoFormat))
                {
                    return currentFifo.VideoFormat;
                }

                // Fallback generico
                return "Generic GigEVision (Mono)";
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel determinare il formato video: {ex.Message}");
                return "Generic GigEVision (Mono)"; // Fallback sicuro per DALSA
            }
        }

        private static void SetHardwareTriggerMode(Cognex.VisionPro.ICogAcqFifo acqFifo)
        {
            try
            {
                if (acqFifo == null)
                {
                    MainWindow.logger?.Error("AcqFifo è null, impossibile impostare trigger hardware");
                    return;
                }

                var triggerParams = acqFifo.OwnedTriggerParams;
                if (triggerParams == null)
                {
                    MainWindow.logger?.Error("OwnedTriggerParams è null");
                    return;
                }

                triggerParams.TriggerEnabled = false;
                triggerParams.TriggerModel = Cognex.VisionPro.CogAcqTriggerModelConstants.Auto;
                triggerParams.TriggerEnabled = true;

                MainWindow.logger?.Info($"Trigger mode impostato su {triggerParams.TriggerModel} per {acqFifo.FrameGrabber?.Name}");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nell'impostazione del trigger hardware: {ex.Message}");
            }
        }

        private static void TrySetStringFeature(ICogGigEAccess gigEAccess, string featureName, string value, string cameraName)
        {
            try
            {
             
                gigEAccess.SetFeature(featureName, value);
                MainWindow.logger?.Info(
                    $"{cameraName ?? "Camera"}: feature {featureName} impostata a {value}.");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(
                    $"{cameraName ?? "Camera"}: impossibile impostare {featureName} a {value}: {ex.Message}");
            }
        }

        private static void ConfigureTrigger(ICogGigEAccess GigEAccess, double LineDebouncerTime)
        {
            const int maxAttempts = 3;
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    GigEAccess.SetDoubleFeature("TriggerDelay", LineDebouncerTime);
                    MainWindow.logger?.Info($"TriggerDelay impostato a {LineDebouncerTime} μs");
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    if (attempt < maxAttempts)
                    {
                        MainWindow.logger?.Warn(
                            $"Tentativo {attempt}/{maxAttempts} fallito nell'impostazione TriggerDelay={LineDebouncerTime} μs: {ex.Message}. Nuovo tentativo in corso...");
                        Task.Delay(JobReadyRetryDelayMs).GetAwaiter().GetResult();
                    }
                }
            }

            string mText = "The TriggerActivation feature could not be set." + "\n" +
                "This may be because the selected camera does not support this feature." + "\n" +
                "Error message: " + lastException?.Message;
            MainWindow.logger?.Error(mText);
        }

        public static void getConfigureTrigger(CogJob jobParam)
        {
            ReadTriggerDelayFromCamera(jobParam, true);
        }

        public static double? ReadTriggerDelayFromCamera(CogJob jobParam, bool logDetails = true)
        {
            if (jobParam == null)
            {
                MainWindow.logger?.Error("Job parameter is null.");
                return null;
            }

            Cognex.VisionPro.ICogAcqFifo fifo = jobParam.AcqFifo;
            Cognex.VisionPro.ICogGigEAccess mCameraAccess = null;

            if (fifo != null && fifo.FrameGrabber != null)
            {
                mCameraAccess = fifo.FrameGrabber.OwnedGigEAccess;
            }

            if (mCameraAccess != null && IsCorrectCamera(fifo.FrameGrabber.Name))
            {
                try
                {
                    bool isTopJob = string.Equals(jobParam.Name, MainWindow._topJob?.Name, StringComparison.OrdinalIgnoreCase);
                    bool isSideJob = string.Equals(jobParam.Name, MainWindow._sideJob?.Name, StringComparison.OrdinalIgnoreCase);

                    if (isTopJob)
                    {
                        mTriggerTop = fifo.OwnedTriggerParams;

                        if (mTriggerTop == null)
                        {
                            MainWindow.logger?.Error("Trigger parameters for top job are null.");
                            return null;
                        }
                    }
                    else if (isSideJob)
                    {
                        mTriggerSide = fifo.OwnedTriggerParams;

                        if (mTriggerSide == null)
                        {
                            MainWindow.logger?.Error("Trigger parameters for side job are null.");
                            return null;
                        }
                    }

                    double triggerDelay = mCameraAccess.GetDoubleFeature("TriggerDelay");
                    if (logDetails)
                    {
                        var trigger = isTopJob ? mTriggerTop : mTriggerSide;
                        string triggerLabel = isTopJob ? "top" : isSideJob ? "side" : jobParam.Name;
                        string triggerModel = trigger?.TriggerModel.ToString() ?? "unknown";
                        string triggerEnabled = trigger?.TriggerEnabled.ToString() ?? "unknown";

                        MainWindow.logger?.Info(
                            $"Camera trigger configuration read for {triggerLabel}: TriggerDelay={triggerDelay} μs, TriggerEnabled={triggerEnabled}, TriggerModel={triggerModel}, Camera={fifo.FrameGrabber.Name}, Serial={fifo.FrameGrabber.SerialNumber}");
                    }

                    return triggerDelay;
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Error("Failed to get trigger configuration: " + ex.Message);
                    return null;
                }
            }
            else
            {
                MainWindow.logger?.Warn($"No Frame grabber o camera detected on the getConfigureTrigger methode for job {jobParam?.Name ?? "unknown"}");
                return null;
            }
        }

        public static double? ReadExposureTimeUs(CogJob jobParam, bool logDetails = true)
        {
            var gigEAccess = ResolveGigEAccess(jobParam, out string cameraName, out string serialNumber);
            if (gigEAccess == null)
            {
                MainWindow.logger?.Warn($"Exposure read unavailable for job {jobParam?.Name ?? "unknown"}: GigE access not available.");
                return null;
            }

            foreach (var featureName in ResolveExposureFeatureNames())
            {
                try
                {
                    double value = gigEAccess.GetDoubleFeature(featureName);
                    if (logDetails)
                    {
                        MainWindow.logger?.Info(
                            $"Camera exposure read for {jobParam?.Name}: {featureName}={value:0.###} us, Camera={cameraName}, Serial={serialNumber}");
                    }

                    return value;
                }
                catch
                {
                    // Try the next common GenICam exposure node name.
                }
            }

            MainWindow.logger?.Warn($"Exposure read failed for job {jobParam?.Name}: no supported ExposureTime feature found.");
            return null;
        }

        public static bool ApplyExposureTimeUs(CogJob jobParam, double exposureUs)
        {
            if (exposureUs <= 0.0 || double.IsNaN(exposureUs) || double.IsInfinity(exposureUs))
            {
                MainWindow.logger?.Warn($"Exposure write skipped for job {jobParam?.Name}: invalid value {exposureUs} us.");
                return false;
            }

            var gigEAccess = ResolveGigEAccess(jobParam, out string cameraName, out string serialNumber);
            if (gigEAccess == null)
            {
                MainWindow.logger?.Warn($"Exposure write unavailable for job {jobParam?.Name ?? "unknown"}: GigE access not available.");
                return false;
            }

            // ExposureAuto non esiste su tutte le teste (es. sensori di spostamento 3D L38):
            // se la feature e' assente NON e' un errore, quindi non va loggato come warning.
            TrySetOptionalStringFeature(gigEAccess, "ExposureAuto", "Off", cameraName);

            foreach (var featureName in ResolveExposureFeatureNames())
            {
                try
                {
                    gigEAccess.SetDoubleFeature(featureName, exposureUs);
                    MainWindow.logger?.Info(
                        $"Camera exposure applied for {jobParam?.Name}: {featureName}={exposureUs:0.###} us, Camera={cameraName}, Serial={serialNumber}");
                    return true;
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Debug($"Exposure feature {featureName} not writable for {jobParam?.Name}: {ex.Message}");
                }
            }

            MainWindow.logger?.Warn($"Exposure write failed for job {jobParam?.Name}: no supported writable ExposureTime feature found.");
            return false;
        }

        private static IEnumerable<string> ResolveExposureFeatureNames()
        {
            yield return "ExposureTime";
            yield return "ExposureTimeAbs";
            yield return "ExposureTimeRaw";
        }

        /// <summary>
        /// Legge la "detection sensitivity" (o altra feature double indicata) dalla testa 3D.
        /// featureName arriva da Config (Top3DDetectionSensitivityFeature): permette di
        /// adattare il nome reale della feature GigE senza ricompilare.
        /// </summary>
        public static double? ReadDetectionSensitivity(CogJob jobParam, string featureName)
        {
            if (string.IsNullOrWhiteSpace(featureName))
            {
                return null;
            }

            var gigEAccess = ResolveGigEAccess(jobParam, out string cameraName, out string serialNumber);
            if (gigEAccess == null)
            {
                MainWindow.logger?.Warn($"Detection sensitivity read unavailable for job {jobParam?.Name ?? "unknown"}: GigE access not available.");
                return null;
            }

            try
            {
                double value = gigEAccess.GetDoubleFeature(featureName);
                MainWindow.logger?.Info(
                    $"Detection sensitivity read for {jobParam?.Name}: {featureName}={value:0.###}, Camera={cameraName}, Serial={serialNumber}");
                return value;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(
                    $"Detection sensitivity read failed for job {jobParam?.Name}: feature '{featureName}' not readable: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Scrive la "detection sensitivity" (o altra feature double indicata) sulla testa 3D.
        /// Non blocca il ciclo: in caso di errore logga e restituisce false.
        /// </summary>
        public static bool ApplyDetectionSensitivity(CogJob jobParam, string featureName, double value)
        {
            if (string.IsNullOrWhiteSpace(featureName))
            {
                MainWindow.logger?.Warn($"Detection sensitivity write skipped for job {jobParam?.Name}: feature name not configured.");
                return false;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                MainWindow.logger?.Warn($"Detection sensitivity write skipped for job {jobParam?.Name}: invalid value {value}.");
                return false;
            }

            var gigEAccess = ResolveGigEAccess(jobParam, out string cameraName, out string serialNumber);
            if (gigEAccess == null)
            {
                MainWindow.logger?.Warn($"Detection sensitivity write unavailable for job {jobParam?.Name ?? "unknown"}: GigE access not available.");
                return false;
            }

            try
            {
                gigEAccess.SetDoubleFeature(featureName, value);
                MainWindow.logger?.Info(
                    $"Detection sensitivity applied for {jobParam?.Name}: {featureName}={value:0.###}, Camera={cameraName}, Serial={serialNumber}");
                return true;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(
                    $"Detection sensitivity write failed for job {jobParam?.Name}: feature '{featureName}' not writable: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Come <see cref="TrySetStringFeature"/> ma tratta l'assenza della feature come condizione
        /// ATTESA (log Debug), non come warning. Utile per feature che esistono solo su alcune teste
        /// (es. ExposureAuto assente sui sensori 3D L38).
        /// </summary>
        private static void TrySetOptionalStringFeature(ICogGigEAccess gigEAccess, string featureName, string value, string cameraName)
        {
            try
            {
                gigEAccess.SetFeature(featureName, value);
                MainWindow.logger?.Info(
                    $"{cameraName ?? "Camera"}: feature {featureName} impostata a {value}.");
            }
            catch (Exception ex)
            {
                bool featureAbsent = ex.Message?.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message?.IndexOf("non trovat", StringComparison.OrdinalIgnoreCase) >= 0;

                if (featureAbsent)
                {
                    MainWindow.logger?.Debug(
                        $"{cameraName ?? "Camera"}: feature opzionale {featureName} non presente su questa testa: {ex.Message}");
                }
                else
                {
                    MainWindow.logger?.Warn(
                        $"{cameraName ?? "Camera"}: impossibile impostare {featureName} a {value}: {ex.Message}");
                }
            }
        }

        private static ICogGigEAccess ResolveGigEAccess(CogJob jobParam, out string cameraName, out string serialNumber)
        {
            cameraName = jobParam?.AcqFifo?.FrameGrabber?.Name;
            serialNumber = jobParam?.AcqFifo?.FrameGrabber?.SerialNumber;

            var gigEAccess = jobParam?.AcqFifo?.FrameGrabber?.OwnedGigEAccess;
            if (gigEAccess == null || !IsCorrectCamera(cameraName))
            {
                return null;
            }

            return gigEAccess;
        }

        /// <summary>
        /// Configures two DALSA Cycling Presets for alternating front/back illumination.
        /// One external FrameStart pulse still produces one image; StartOfFrame only
        /// advances the camera-side preset used by the next frame.
        /// </summary>
        public static CameraCyclingPresetResult ConfigureDualIlluminationCyclingPresets(
            CogJob jobParam,
            Models.MultiShotTrigger.MultiShotDualIlluminationOptions options,
            bool executeResetCommand)
        {
            var result = CreateCyclingResult(jobParam);
            if (!ValidateDualIlluminationOptions(options, out string validationError))
            {
                result.ErrorMessage = validationError;
                return result;
            }

            var gigEAccess = ResolveGigEAccess(jobParam, out string cameraName, out string serialNumber);
            result.CameraName = cameraName;
            result.SerialNumber = serialNumber;
            if (gigEAccess == null)
            {
                result.ErrorMessage = "OwnedGigEAccess is not available for the loaded VisionPro job.";
                return result;
            }

            try
            {
                // Cycling features must be programmed while the module is disabled.
                gigEAccess.SetFeature("cyclingPresetMode", "Off");
                gigEAccess.SetIntegerFeature("cyclingPresetCount", 2u);
                gigEAccess.SetFeature("cyclingPresetIncrementalSource", "StartOfFrame");
                gigEAccess.SetIntegerFeature("cyclingPresetRepeater", 1u);
                gigEAccess.SetFeature("cyclingPresetResetSource", "Software");

                // Per Teledyne's documented Cycling Preset sequence (Multi-Exposure Cycling
                // Example Setup), cP_PresetConfigurationSelector must already point at a valid
                // preset BEFORE cP_FeaturesActivationSelector/cP_FeaturesActivationMode are
                // written, otherwise the per-preset value nodes (cP_ExposureTime, etc.) stay
                // read-only/not-found for the activated feature.
                gigEAccess.SetIntegerFeature("cP_PresetConfigurationSelector", 1u);
                ActivateCyclingFeature(gigEAccess, "ExposureTime");
                ActivateCyclingFeature(gigEAccess, ToOutputControlFeature(options.FrontOutputLine));
                ActivateCyclingFeature(gigEAccess, ToOutputControlFeature(options.BackOutputLine));

                string preset1ActiveLine = options.FrontFirst ? options.FrontOutputLine : options.BackOutputLine;
                string preset2ActiveLine = options.FrontFirst ? options.BackOutputLine : options.FrontOutputLine;
                ConfigureCyclingPreset(gigEAccess, 1u, options, preset1ActiveLine);
                ConfigureCyclingPreset(gigEAccess, 2u, options, preset2ActiveLine);

                gigEAccess.SetFeature("cyclingPresetMode", "Active");
                if (executeResetCommand)
                {
                    gigEAccess.ExecuteCommand("cyclingPresetResetCmd");
                    result.ResetCommandExecuted = true;
                }

                result.CurrentActiveSet = TryReadCyclingPresetActiveSet(gigEAccess);
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public static CameraCyclingPresetResult ResetDualIlluminationCyclingPreset(CogJob jobParam)
        {
            var result = CreateCyclingResult(jobParam);
            var gigEAccess = ResolveGigEAccess(jobParam, out string cameraName, out string serialNumber);
            result.CameraName = cameraName;
            result.SerialNumber = serialNumber;
            if (gigEAccess == null)
            {
                result.ErrorMessage = "OwnedGigEAccess is not available for the loaded VisionPro job.";
                return result;
            }

            try
            {
                gigEAccess.ExecuteCommand("cyclingPresetResetCmd");
                result.ResetCommandExecuted = true;
                result.CurrentActiveSet = TryReadCyclingPresetActiveSet(gigEAccess);
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public static uint? ReadCyclingPresetCurrentActiveSet(CogJob jobParam)
        {
            var gigEAccess = ResolveGigEAccess(jobParam, out _, out _);
            return TryReadCyclingPresetActiveSet(gigEAccess);
        }

        private static CameraCyclingPresetResult CreateCyclingResult(CogJob jobParam)
        {
            return new CameraCyclingPresetResult
            {
                CameraName = jobParam?.AcqFifo?.FrameGrabber?.Name,
                SerialNumber = jobParam?.AcqFifo?.FrameGrabber?.SerialNumber
            };
        }

        private static bool ValidateDualIlluminationOptions(
            Models.MultiShotTrigger.MultiShotDualIlluminationOptions options,
            out string error)
        {
            error = null;
            if (options == null)
            {
                error = "Dual illumination configuration is missing.";
                return false;
            }

            if (options.ExposureTimeUs <= 0.0 ||
                double.IsNaN(options.ExposureTimeUs) ||
                double.IsInfinity(options.ExposureTimeUs))
            {
                error = "Dual illumination exposure must be greater than zero microseconds.";
                return false;
            }

            if (!IsSupportedCyclingOutputLine(options.FrontOutputLine) ||
                !IsSupportedCyclingOutputLine(options.BackOutputLine) ||
                string.Equals(options.FrontOutputLine, options.BackOutputLine, StringComparison.OrdinalIgnoreCase))
            {
                error = "Front and back illumination must use different DALSA output lines (Line3 and Line4).";
                return false;
            }

            if (!string.Equals(options.ActiveOutputSource, "ExposureActive", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(options.ActiveOutputSource, "PulseOnStartofExposure", StringComparison.OrdinalIgnoreCase))
            {
                error = "ActiveOutputSource must be ExposureActive or PulseOnStartofExposure.";
                return false;
            }

            return true;
        }

        private static void ActivateCyclingFeature(ICogGigEAccess gigEAccess, string selector)
        {
            gigEAccess.SetFeature("cP_FeaturesActivationSelector", selector);
            gigEAccess.SetFeature("cP_FeaturesActivationMode", "Active");
        }

        private static void ConfigureCyclingPreset(
            ICogGigEAccess gigEAccess,
            uint presetIndex,
            Models.MultiShotTrigger.MultiShotDualIlluminationOptions options,
            string activeLine)
        {
            gigEAccess.SetIntegerFeature("cP_PresetConfigurationSelector", presetIndex);
            gigEAccess.SetDoubleFeature("cP_ExposureTime", options.ExposureTimeUs);

            ConfigureCyclingOutputLine(
                gigEAccess,
                options.FrontOutputLine,
                string.Equals(activeLine, options.FrontOutputLine, StringComparison.OrdinalIgnoreCase)
                    ? options.ActiveOutputSource
                    : "Off");
            ConfigureCyclingOutputLine(
                gigEAccess,
                options.BackOutputLine,
                string.Equals(activeLine, options.BackOutputLine, StringComparison.OrdinalIgnoreCase)
                    ? options.ActiveOutputSource
                    : "Off");
        }

        private static void ConfigureCyclingOutputLine(ICogGigEAccess gigEAccess, string line, string source)
        {
            gigEAccess.SetFeature("cP_LineSelector", NormalizeCyclingOutputLine(line));
            gigEAccess.SetFeature("cP_OutputLineSource", source);
        }

        private static uint? TryReadCyclingPresetActiveSet(ICogGigEAccess gigEAccess)
        {
            if (gigEAccess == null)
            {
                return null;
            }

            try
            {
                if (!gigEAccess.IsReadable("cyclingPresetCurrentActiveSet"))
                {
                    return null;
                }

                return gigEAccess.GetIntegerFeature("cyclingPresetCurrentActiveSet");
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSupportedCyclingOutputLine(string line)
        {
            string normalized = NormalizeCyclingOutputLine(line);
            return string.Equals(normalized, "Line3", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Line4", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCyclingOutputLine(string line)
        {
            return string.IsNullOrWhiteSpace(line) ? string.Empty : line.Replace(" ", string.Empty).Trim();
        }

        private static string ToOutputControlFeature(string line)
        {
            return "Output" + NormalizeCyclingOutputLine(line) + "Control";
        }

        private static bool IsCorrectCamera(string GigECameraName)
        {
            if (string.IsNullOrWhiteSpace(GigECameraName))
            {
                MainWindow.logger?.Warn("Frame grabber senza nome camera: impossibile validare l'interfaccia GigE.");
                return false;
            }

            if (GigECameraName.IndexOf("Teledyne DALSA", StringComparison.OrdinalIgnoreCase) != -1)
            {
                MainWindow.logger?.Info("GigE Vision DALSA camera detected: " + GigECameraName);
            }
            else
            {
                MainWindow.logger?.Info("GigE Vision camera detected: " + GigECameraName);
            }

            return true;
        }
    }
}
