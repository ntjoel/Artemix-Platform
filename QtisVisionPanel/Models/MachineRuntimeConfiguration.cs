using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using QtisVisionPanel.Models.MultiShotTrigger;

namespace QtisVisionPanel.Models
{
    public class MachineRuntimeConfiguration
    {
        public string ConfigurationName { get; set; } = "Default Machine Runtime";

        public string ConfigurationVersion { get; set; } = "1.0";

        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

        public string DigitalIoBoard { get; set; } = "PCIE-1756-BE";

        public string EncoderBoard { get; set; } = "PCIE-1884-AE";

        public string TriggerMode { get; set; } = "Hybrid";

        public string RejectMode { get; set; } = "EncoderTracked";

        public string Notes { get; set; } =
            "Base runtime for product tracking, camera trigger, and encoder-synchronized reject.";

        public List<MachineSignalDefinition> MachineInputs { get; set; } = new List<MachineSignalDefinition>();

        public List<MachineSignalDefinition> MachineOutputs { get; set; } = new List<MachineSignalDefinition>();

        public List<EncoderConfigurationTemplate> EncoderTemplates { get; set; } = new List<EncoderConfigurationTemplate>();

        public List<MachineInterventionPoint> InterventionPoints { get; set; } = new List<MachineInterventionPoint>();

        public MachineRuntimeBindings RuntimeBindings { get; set; } = new MachineRuntimeBindings();

        public MachineMultiShotTriggerConfiguration MachineMultiShotTrigger { get; set; } = new MachineMultiShotTriggerConfiguration();

        public List<AdditionalRuntimeBindingDefinition> AdditionalRuntimeBindings { get; set; } = new List<AdditionalRuntimeBindingDefinition>();
    }

    public class MachineRuntimeBindings
    {
        public bool UseEncoderTrigger { get; set; } = true;

        [System.Xml.Serialization.XmlIgnore]
        public bool UseEncoderTriggerSpecified { get; set; }

        public string TriggerSchedulingMode { get; set; } = "VirtualConveyor";

        public string ProductPhotocellSignalCode { get; set; } = "IN_PRODUCT_PHOTOCELL";

        public string ExternalTriggerEnableSignalCode { get; set; } = "IN_EXTERNAL_TRIGGER_ENABLE";

        public string CameraTriggerSignalCode { get; set; } = "OUT_CAMERA_TOP_TRIGGER";

        public string RejectSignalCode { get; set; } = "OUT_REJECT_SOLENOID";

        public string GeneralAlarmSignalCode { get; set; } = "OUT_GENERAL_ALARM";

        public string MainEncoderAxisCode { get; set; } = "ENC_CONVEYOR_MAIN";

        public string HeartbeatOutputSignalCode { get; set; } = "OUT_HEARTBEAT";

        public bool HeartbeatEnabled { get; set; } = true;

        public bool HeartbeatAutoStartOnRealHardware { get; set; } = true;

        public int HeartbeatIntervalMs { get; set; } = 500;

        public int ProductQueueCapacity { get; set; } = 200;

        public int RejectPulseMs { get; set; } = 80;

        /// <summary>AirBlast and Monostable use one timed output. Bistable requires position sensors.</summary>
        public string RejectActuatorType { get; set; }

        /// <summary>
        /// Campi storici riservati al futuro deviatore bistabile. Le uscite OPEN/CLOSE
        /// non sono attivate finche' non saranno disponibili i sensori di posizione.
        /// AirBlast e Monostable usano un solo impulso su <see cref="RejectSignalCode"/>.
        /// </summary>
        public string RejectOpenSignalCode { get; set; } = "OUT_REJECT_OPEN";

        public string RejectCloseSignalCode { get; set; } = "OUT_REJECT_CLOSE";

        public int RejectCloseDelayMs { get; set; } = 300;

        public int PhotocellDebounceMs { get; set; } = 80;

        public int MinimumRetriggerGapMs { get; set; } = 120;

        // Lag massimo (ms) tra il risultato TOP e un companion perche' siano considerati LO STESSO
        // prodotto quando il MultiShot e' attivo. E' un valore FISICO (durata plausibile della
        // scansione multishot + stitching), DISTINTO dal timeout di attesa risultato (30s, tetto di
        // sicurezza). Per il single-shot resta usato il floor interno dell'orchestrator (5s).
        public int MultiShotCompanionMaxLagMs { get; set; } = 15000;

        public int TopTriggerBaseDelayMs { get; set; } = 0;

        public int SideTriggerBaseDelayMs { get; set; } = 0;

        public int TopTriggerPulseMs { get; set; } = 40;

        public int SideTriggerPulseMs { get; set; } = 40;

        public int LivePreviewIntervalMs { get; set; } = 1000;

        public int LivePreviewPulseMs { get; set; } = 20;

        public double LivePreviewExposureUs { get; set; } = 0.0;

        // Fondazione dati AI (Fase 0): abilita la cattura delle misure di ispezione
        // (tbl_inspection_measurements) e degli snapshot salute PC (tbl_health_snapshots).
        // Additiva e non bloccante; disabilitata di default sulle nuove configurazioni
        // finche' il commissioning non ha definito DB, retention e uso operativo.
        public bool DataFoundationCaptureEnabled { get; set; } = false;

        // Integrazione AI (Fase 1): abilita il controllo statistico di processo (SPC)
        // e il rilevamento deriva sulle misure catturate. Advisory, non bloccante.
        public bool ProcessControlEnabled { get; set; } = false;

        // Integrazione AI (Fase 2): abilita la manutenzione predittiva (baseline statistica)
        // sugli snapshot salute PC. Advisory, non bloccante.
        public bool PredictiveMaintenanceEnabled { get; set; } = false;

        // Integrazione AI (Fase 3a): abilita la raccolta dati etichettati (label.json + indice DB)
        // per il futuro training del classificatore vision. Solo indicizzazione, non addestra nulla.
        public bool TrainingDataCollectionEnabled { get; set; } = false;

        // Integrazione AI (Fase 3): classificatore difetti ONNX TOP in shadow-mode (advisory).
        // InputWidth/InputHeight sono il resize richiesto dal modello ONNX, non la risoluzione
        // nativa della camera. Il profilo SIDE/LEFT parallelo e' definito subito sotto.
        // Disabilitato di default: si attiva fornendo un modello .onnx e impostando Enabled=true.
        public bool DefectClassifierEnabled { get; set; } = false;
        public string DefectClassifierModelPath { get; set; } = string.Empty;
        public string DefectClassifierModelVersion { get; set; } = string.Empty;
        public string DefectClassifierModelNotes { get; set; } = string.Empty;
        public int DefectClassifierInputWidth { get; set; } = 224;
        public int DefectClassifierInputHeight { get; set; } = 224;
        public bool DefectClassifierGrayscale { get; set; } = false;
        public double DefectClassifierNormalizeMean { get; set; } = 0.0;
        public double DefectClassifierNormalizeStd { get; set; } = 255.0;
        // Confidenza minima (0..1) per dichiarare NOK: un NOK sotto soglia diventa "incerto" e non
        // conta come difetto (taglia i falsi allarmi a bassa confidenza). 0 = filtro disattivato.
        public double DefectClassifierMinConfidence { get; set; } = 0.0;

        // Classificatore ONNX della camera SIDE (immagine *_F_A.bmp): set di campi parallelo al TOP,
        // cosi' la Side ha modello e preprocessing propri. Shadow-mode advisory, disabilitato di default.
        public bool SideDefectClassifierEnabled { get; set; } = false;
        public string SideDefectClassifierModelPath { get; set; } = string.Empty;
        public string SideDefectClassifierModelVersion { get; set; } = string.Empty;
        public string SideDefectClassifierModelNotes { get; set; } = string.Empty;
        public int SideDefectClassifierInputWidth { get; set; } = 224;
        public int SideDefectClassifierInputHeight { get; set; } = 224;
        public bool SideDefectClassifierGrayscale { get; set; } = false;
        public double SideDefectClassifierNormalizeMean { get; set; } = 0.0;
        public double SideDefectClassifierNormalizeStd { get; set; } = 255.0;
        public double SideDefectClassifierMinConfidence { get; set; } = 0.0;

        // Classificatori ONNX delle altre camere gestite dalla piattaforma (REAR/RIGHT, FRONT,
        // BOTTOM): un profilo per camera con modello e preprocessing propri. TOP e SIDE restano
        // sui campi dedicati sopra, per compatibilita' con le configurazioni esistenti. La lista
        // nasce vuota: MachineConfigurationService la completa con un profilo per ruolo (evita
        // che la deserializzazione XML accodi voci duplicate ai valori di default).
        public List<DefectClassifierCameraProfile> AdditionalDefectClassifierProfiles { get; set; } =
            new List<DefectClassifierCameraProfile>();

        /// <summary>
        /// Preprocessing e modello del classificatore della camera indicata, in forma comune.
        /// Per TOP e SIDE e' una copia dei campi dedicati (modificarla non cambia la
        /// configurazione); per le altre camere e' il profilo in lista. Null se il ruolo non
        /// ha un profilo.
        /// </summary>
        public DefectClassifierCameraProfile ResolveDefectClassifierProfile(string cameraRole)
        {
            string role = DefectClassifierCameraProfile.NormalizeRole(cameraRole);
            if (role == "top")
            {
                return new DefectClassifierCameraProfile
                {
                    CameraRole = "top",
                    Enabled = DefectClassifierEnabled,
                    ModelPath = DefectClassifierModelPath,
                    ModelVersion = DefectClassifierModelVersion,
                    ModelNotes = DefectClassifierModelNotes,
                    InputWidth = DefectClassifierInputWidth,
                    InputHeight = DefectClassifierInputHeight,
                    Grayscale = DefectClassifierGrayscale,
                    NormalizeMean = DefectClassifierNormalizeMean,
                    NormalizeStd = DefectClassifierNormalizeStd,
                    MinConfidence = DefectClassifierMinConfidence
                };
            }

            if (role == "side")
            {
                return new DefectClassifierCameraProfile
                {
                    CameraRole = "side",
                    Enabled = SideDefectClassifierEnabled,
                    ModelPath = SideDefectClassifierModelPath,
                    ModelVersion = SideDefectClassifierModelVersion,
                    ModelNotes = SideDefectClassifierModelNotes,
                    InputWidth = SideDefectClassifierInputWidth,
                    InputHeight = SideDefectClassifierInputHeight,
                    Grayscale = SideDefectClassifierGrayscale,
                    NormalizeMean = SideDefectClassifierNormalizeMean,
                    NormalizeStd = SideDefectClassifierNormalizeStd,
                    MinConfidence = SideDefectClassifierMinConfidence
                };
            }

            return (AdditionalDefectClassifierProfiles ?? new List<DefectClassifierCameraProfile>())
                .FirstOrDefault(profile => string.Equals(
                    DefectClassifierCameraProfile.NormalizeRole(profile?.CameraRole),
                    role,
                    StringComparison.OrdinalIgnoreCase));
        }

        // Integrazione AI (Fase 3b): training on-demand del classificatore dal pannello
        // (pulsante "Addestra modello"). Il training gira in uno script Python ESTERNO
        // (Scripts/AI/train_defect_classifier.py) — vedi DefectClassifierTrainingService.
        // PythonPath: eseguibile python (default "python" dal PATH; accetta percorso completo).
        // OutputDir: cartella dei modelli versionati defect_classifier_vN.onnx.
        public string DefectClassifierTrainingPythonPath { get; set; } = "python";
        public string DefectClassifierTrainingOutputDir { get; set; } = @"C:\QtisVision\AI\Models";
        public int DefectClassifierTrainingEpochs { get; set; } = 20;

        // Integrazione AI (Fase 5): ottimizzatore timing IO/encoder. Analisi/advisory sempre
        // non bloccante; l'applicazione dei parametri avviene solo su conferma operatore.
        public bool IoTimingOptimizerEnabled { get; set; } = false;

        // Integrazione AI (Fase 6): monitor prestazioni ciclo ispezione. Misura tempi
        // aggregati e produce solo advisory; non modifica il runtime macchina.
        public bool AiPerformanceMonitorEnabled { get; set; } = false;

        // Integrazione AI (Fase 7): advisor ricetta/prodotto. Rileva possibili incoerenze
        // tra prodotto ispezionato e ricetta in produzione; suggerisce soltanto.
        public bool RecipeProductAdvisorEnabled { get; set; } = false;

        // Integrazione AI (Fase 8): monitoraggio derive (ispezioni + salute PC) con notifica
        // preallarmi al cliente. Aggrega i segnali advisory esistenti e li instrada su uno o piu'
        // canali (locale/Events Monitor sempre attivo; MES-OPC UA ed Email/SMTP additivi).
        // Advisory e non bloccante: default disabilitato sulle nuove configurazioni.
        public bool MachineHealthNotificationsEnabled { get; set; } = false;
        // Intervallo del digest periodico dei preallarmi non-critici (minuti). Default ~1 turno.
        public int MachineHealthDigestIntervalMinutes { get; set; } = 480;
        // Deriva ispezione: stima pezzi al fuori-tolleranza <= questo valore => preallarme CRITICO (invio immediato).
        public int MachineHealthCriticalEtaProducts { get; set; } = 50;
        // Salute PC: proiezione disco che raggiunge la soglia entro queste ore => preallarme CRITICO.
        public double MachineHealthDiskCriticalHours { get; set; } = 24.0;

        // Integrazione AI (Fase 9): canale Email/SMTP per Services.MachineHealthNotificationService.
        // Additivo: se disabilitato o non configurato, il canale locale (Events Monitor) resta
        // comunque attivo. Password salvata in chiaro come la password DB in ConfigClassStructure,
        // stessa convenzione gia' in uso in questo file di configurazione.
        public bool EmailNotificationsEnabled { get; set; } = false;
        public string EmailSmtpHost { get; set; } = string.Empty;
        public int EmailSmtpPort { get; set; } = 587;
        public bool EmailUseSsl { get; set; } = true;
        public string EmailFromAddress { get; set; } = string.Empty;
        // Uno o piu' destinatari separati da ';' o ','.
        public string EmailToAddresses { get; set; } = string.Empty;
        public string EmailSmtpUsername { get; set; } = string.Empty;
        public string EmailSmtpPassword { get; set; } = string.Empty;

        // Retention dati AI. 0 = conserva senza pulizia automatica.
        public int AiInspectionMeasurementRetentionDays { get; set; } = 180;
        public int AiHealthSnapshotRetentionDays { get; set; } = 90;
        public int AiTrainingSampleRetentionDays { get; set; } = 0;

        public bool VirtualConveyorEnabled { get; set; } = true;

        public double VirtualConveyorSpeedMetersPerMinute { get; set; } = 18.0;

        public bool VirtualConveyorUsePiecesPerMinute { get; set; } = false;

        public double VirtualConveyorPiecesPerMinute { get; set; } = 120.0;

        public double VirtualConveyorProductPitchMm { get; set; } = 150.0;

        public double EncoderCalibrationTachometerSpeedMetersPerMinute { get; set; } = 40.0;
    }

    /// <summary>
    /// Classificatore difetti ONNX (shadow-mode) di una camera: modello, preprocessing e soglia
    /// propri. Il ruolo usa le stesse chiavi di label.json (top, side, rear, front, bottom).
    /// Notifica le modifiche perche' il pannello AI lega i campi direttamente al profilo.
    /// </summary>
    public class DefectClassifierCameraProfile : INotifyPropertyChanged
    {
        /// <summary>Ruoli con profilo in lista (TOP e SIDE usano i campi dedicati).</summary>
        public static readonly string[] AdditionalRoles = { "right", "rear", "front", "bottom" };

        /// <summary>Tutti i ruoli per cui la piattaforma puo' addestrare un classificatore.</summary>
        public static readonly string[] AllRoles = { "top", "side", "right", "rear", "front", "bottom" };

        private string _cameraRole = string.Empty;
        private bool _enabled;
        private string _modelPath = string.Empty;
        private string _modelVersion = string.Empty;
        private string _modelNotes = string.Empty;
        private int _inputWidth = 224;
        private int _inputHeight = 224;
        private bool _grayscale;
        private double _normalizeMean;
        private double _normalizeStd = 255.0;
        private double _minConfidence;

        public event PropertyChangedEventHandler PropertyChanged;

        [XmlAttribute]
        public string CameraRole { get => _cameraRole; set => Set(ref _cameraRole, value ?? string.Empty); }
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public string ModelPath { get => _modelPath; set => Set(ref _modelPath, value ?? string.Empty); }
        public string ModelVersion { get => _modelVersion; set => Set(ref _modelVersion, value ?? string.Empty); }
        public string ModelNotes { get => _modelNotes; set => Set(ref _modelNotes, value ?? string.Empty); }
        public int InputWidth { get => _inputWidth; set => Set(ref _inputWidth, value); }
        public int InputHeight { get => _inputHeight; set => Set(ref _inputHeight, value); }
        public bool Grayscale { get => _grayscale; set => Set(ref _grayscale, value); }
        public double NormalizeMean { get => _normalizeMean; set => Set(ref _normalizeMean, value); }
        public double NormalizeStd { get => _normalizeStd; set => Set(ref _normalizeStd, value); }
        public double MinConfidence { get => _minConfidence; set => Set(ref _minConfidence, value); }

        /// <summary>
        /// Chiave di ruolo del classificatore: stessa di label.json e dei risultati di ispezione.
        /// Side/Left -> side; Right e Rear hanno modelli separati.
        /// </summary>
        public static string NormalizeRole(string cameraRole)
        {
            switch ((cameraRole ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "top":
                case "top3d":
                    return "top";
                case "side":
                case "left":
                    return "side";
                case "right":
                    return "right";
                case "rear":
                    return "rear";
                case "front":
                    return "front";
                case "bottom":
                    return "bottom";
                default:
                    return string.Empty;
            }
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
