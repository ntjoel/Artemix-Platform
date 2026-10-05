using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace QtisVisionPanel.Services
{
    public class MachineConfigurationService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string ConfigurationFileName = "machine_runtime_config.xml";
        private const string TemplateFileName = "machine_runtime_config.template.xml";
        private static readonly string[] RequiredRuntimeBindingMarkers =
        {
            "<TriggerSchedulingMode>",
            "<PhotocellDebounceMs>",
            "<MinimumRetriggerGapMs>",
            "<MultiShotCompanionMaxLagMs>",
            "<TopTriggerBaseDelayMs>",
            "<SideTriggerBaseDelayMs>",
            "<TopTriggerPulseMs>",
            "<SideTriggerPulseMs>",
            "<LivePreviewIntervalMs>",
            "<LivePreviewPulseMs>",
            "<LivePreviewExposureUs>",
            "<DataFoundationCaptureEnabled>",
            "<ProcessControlEnabled>",
            "<PredictiveMaintenanceEnabled>",
            "<TrainingDataCollectionEnabled>",
            "<DefectClassifierEnabled>",
            "<DefectClassifierModelPath>",
            "<DefectClassifierModelVersion>",
            "<DefectClassifierModelNotes>",
            "<DefectClassifierInputWidth>",
            "<DefectClassifierInputHeight>",
            "<DefectClassifierGrayscale>",
            "<DefectClassifierNormalizeMean>",
            "<DefectClassifierNormalizeStd>",
            "<DefectClassifierMinConfidence>",
            "<DefectClassifierTrainingPythonPath>",
            "<DefectClassifierTrainingOutputDir>",
            "<DefectClassifierTrainingEpochs>",
            "<SideDefectClassifierEnabled>",
            "<SideDefectClassifierModelPath>",
            "<SideDefectClassifierModelVersion>",
            "<SideDefectClassifierModelNotes>",
            "<SideDefectClassifierInputWidth>",
            "<SideDefectClassifierInputHeight>",
            "<SideDefectClassifierGrayscale>",
            "<SideDefectClassifierNormalizeMean>",
            "<SideDefectClassifierNormalizeStd>",
            "<SideDefectClassifierMinConfidence>",
            "<AdditionalDefectClassifierProfiles>",
            "<IoTimingOptimizerEnabled>",
            "<AiPerformanceMonitorEnabled>",
            "<RecipeProductAdvisorEnabled>",
            "<MachineHealthNotificationsEnabled>",
            "<MachineHealthDigestIntervalMinutes>",
            "<MachineHealthCriticalEtaProducts>",
            "<MachineHealthDiskCriticalHours>",
            "<EmailNotificationsEnabled>",
            "<EmailSmtpHost>",
            "<EmailSmtpPort>",
            "<EmailUseSsl>",
            "<EmailFromAddress>",
            "<EmailToAddresses>",
            "<EmailSmtpUsername>",
            "<EmailSmtpPassword>",
            "<AiInspectionMeasurementRetentionDays>",
            "<AiHealthSnapshotRetentionDays>",
            "<AiTrainingSampleRetentionDays>",
            "<EncoderCalibrationTachometerSpeedMetersPerMinute>",
            "<MachineMultiShotTrigger>",
            "<Right>",
            "<Bottom>",
            "<DualIllumination>"
        };

        private static string ResolveStandaloneRootPath()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && current != null; i++)
            {
                if (File.Exists(Path.Combine(current.FullName, "QuatisVisionPanelIO.sln")) ||
                    File.Exists(Path.Combine(current.FullName, "QuatisVisionPanelIO.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        }

        public string GetConfigurationRootPath()
        {
            var cfgPath = Path.Combine(ResolveStandaloneRootPath(), "cfg");
            Directory.CreateDirectory(cfgPath);
            return cfgPath;
        }

        public string GetConfigurationFilePath()
        {
            return Path.Combine(GetConfigurationRootPath(), ConfigurationFileName);
        }

        public string GetBackupFolderPath()
        {
            var backupPath = Path.Combine(GetConfigurationRootPath(), "backups");
            Directory.CreateDirectory(backupPath);
            return backupPath;
        }

        public string GetTemplateFolderPath()
        {
            var templatePath = Path.Combine(ResolveStandaloneRootPath(), "ConfigurationTemplates");
            Directory.CreateDirectory(templatePath);
            return templatePath;
        }

        public string GetTemplateConfigurationFilePath()
        {
            return Path.Combine(GetTemplateFolderPath(), TemplateFileName);
        }

        public void EnsureTemplateExists(
            IEnumerable<MachineSignalDefinition> defaultInputs,
            IEnumerable<MachineSignalDefinition> defaultOutputs,
            IEnumerable<EncoderConfigurationTemplate> defaultEncoders,
            IEnumerable<MachineInterventionPoint> defaultInterventionPoints)
        {
            var templatePath = GetTemplateConfigurationFilePath();
            if (File.Exists(templatePath))
            {
                return;
            }

            var template = BuildDefault(defaultInputs, defaultOutputs, defaultEncoders, defaultInterventionPoints);
            template.ConfigurationName = "Standalone IO Template";
            Save(template, templatePath);
        }

        public MachineRuntimeConfiguration LoadOrCreateDefault(
            IEnumerable<MachineSignalDefinition> defaultInputs,
            IEnumerable<MachineSignalDefinition> defaultOutputs,
            IEnumerable<EncoderConfigurationTemplate> defaultEncoders,
            IEnumerable<MachineInterventionPoint> defaultInterventionPoints)
        {
            EnsureTemplateExists(defaultInputs, defaultOutputs, defaultEncoders, defaultInterventionPoints);

            var path = GetConfigurationFilePath();
            if (!File.Exists(path))
            {
                var configuration = BuildDefault(defaultInputs, defaultOutputs, defaultEncoders, defaultInterventionPoints);
                Save(configuration);
                return configuration;
            }

            return Load(path) ?? BuildDefault(defaultInputs, defaultOutputs, defaultEncoders, defaultInterventionPoints);
        }

        public MachineRuntimeConfiguration Load(string path = null)
        {
            var filePath = path ?? GetConfigurationFilePath();
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var serializer = new XmlSerializer(typeof(MachineRuntimeConfiguration));
                using (var reader = new StreamReader(filePath))
                {
                    var configuration = (MachineRuntimeConfiguration)serializer.Deserialize(reader);
                    // Un file precedente ai profili per camera non li contiene: si completano
                    // subito, cosi' pannello AI e classificatori vedono sempre REAR/FRONT/BOTTOM.
                    if (configuration?.RuntimeBindings != null)
                    {
                        EnsureAdditionalDefectClassifierProfiles(configuration.RuntimeBindings);
                    }

                    return configuration;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Errore nel caricamento configurazione macchina: {filePath}");
                return null;
            }
        }

        // Serializza le scritture concorrenti (UI + upgrade schema di avvio) sullo stesso file.
        private static readonly object SaveLock = new object();

        public void Save(MachineRuntimeConfiguration configuration, string path = null)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var filePath = path ?? GetConfigurationFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            configuration.LastUpdatedUtc = DateTime.UtcNow;
            EnsureRuntimeBindingsDefaults(configuration);

            var serializer = new XmlSerializer(typeof(MachineRuntimeConfiguration));

            // Scrittura ATOMICA: serializza su file temporaneo e poi sostituisci il target in un solo
            // passo (File.Replace su NTFS). Se il processo si interrompe a meta' scrittura (macchina 24/7)
            // il file di config resta integro (o vecchio o nuovo, mai troncato). Il lock evita che due
            // Save concorrenti (es. salvataggio UI + EnsureLatestRuntimeBindingsSchema all'avvio) si
            // sovrappongano corrompendo il file.
            lock (SaveLock)
            {
                string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var writer = new StreamWriter(tempPath))
                    {
                        serializer.Serialize(writer, configuration);
                        writer.Flush();
                    }

                    if (File.Exists(filePath))
                    {
                        File.Replace(tempPath, filePath, null);
                    }
                    else
                    {
                        File.Move(tempPath, filePath);
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        Logger.Warn(cleanupEx, "MACHINE_CONFIG_SAVE|Cleanup file temporaneo non riuscito");
                    }
                }
            }
        }

        public bool EnsureLatestRuntimeBindingsSchema(MachineRuntimeConfiguration configuration, string path = null)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var filePath = path ?? GetConfigurationFilePath();
            EnsureRuntimeBindingsDefaults(configuration);

            if (!NeedsRuntimeBindingSchemaUpgrade(filePath))
            {
                return false;
            }

            Save(configuration, filePath);
            return true;
        }

        public string CreateBackupFromActiveConfiguration(string backupReason = null)
        {
            var activePath = GetConfigurationFilePath();
            if (!File.Exists(activePath))
            {
                return null;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var suffix = string.IsNullOrWhiteSpace(backupReason) ? string.Empty : "-" + SanitizeForFileName(backupReason);
            var backupFileName = $"machine_runtime_config-backup-{timestamp}{suffix}.xml";
            var backupPath = Path.Combine(GetBackupFolderPath(), backupFileName);
            File.Copy(activePath, backupPath, overwrite: false);
            return backupPath;
        }

        public void RestoreBackupToActiveConfiguration(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new ArgumentNullException(nameof(backupPath));
            }

            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException("Backup configurazione non trovato.", backupPath);
            }

            var activePath = GetConfigurationFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(activePath));
            File.Copy(backupPath, activePath, overwrite: true);
        }

        public List<MachineConfigurationBackupInfo> GetAvailableBackups()
        {
            var backupFolder = GetBackupFolderPath();
            return Directory
                .EnumerateFiles(backupFolder, "*.xml", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var fileInfo = new FileInfo(path);
                    var configuration = Load(path);

                    return new MachineConfigurationBackupInfo
                    {
                        FilePath = path,
                        FileName = fileInfo.Name,
                        CreatedLocalTime = fileInfo.LastWriteTime,
                        SizeBytes = fileInfo.Length,
                        ConfigurationName = configuration?.ConfigurationName,
                        ConfigurationVersion = configuration?.ConfigurationVersion,
                        ConfigurationUpdatedUtc = configuration?.LastUpdatedUtc
                    };
                })
                .OrderByDescending(item => item.CreatedLocalTime)
                .ToList();
        }

        public MachineRuntimeConfiguration BuildDefault(
            IEnumerable<MachineSignalDefinition> defaultInputs,
            IEnumerable<MachineSignalDefinition> defaultOutputs,
            IEnumerable<EncoderConfigurationTemplate> defaultEncoders,
            IEnumerable<MachineInterventionPoint> defaultInterventionPoints)
        {
            return new MachineRuntimeConfiguration
            {
                MachineInputs = CloneSignals(defaultInputs),
                MachineOutputs = CloneSignals(defaultOutputs),
                EncoderTemplates = CloneEncoders(defaultEncoders),
                InterventionPoints = CloneInterventionPoints(defaultInterventionPoints)
            };
        }

        private static bool NeedsRuntimeBindingSchemaUpgrade(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                return RequiredRuntimeBindingMarkers.Any(marker =>
                    content.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Impossibile verificare lo schema runtime bindings di {filePath}");
                return false;
            }
        }

        private static void EnsureRuntimeBindingsDefaults(MachineRuntimeConfiguration configuration)
        {
            if (configuration.RuntimeBindings == null)
            {
                configuration.RuntimeBindings = new MachineRuntimeBindings();
            }

            if (configuration.MachineMultiShotTrigger == null)
            {
                configuration.MachineMultiShotTrigger = new Models.MultiShotTrigger.MachineMultiShotTriggerConfiguration();
            }

            if (configuration.MachineMultiShotTrigger.Side == null)
            {
                configuration.MachineMultiShotTrigger.Side = new Models.MultiShotTrigger.MultiShotTriggerOptions();
            }

            if (configuration.MachineMultiShotTrigger.Right == null)
            {
                configuration.MachineMultiShotTrigger.Right = Models.MultiShotTrigger.MultiShotTriggerOptions.CreateDefault(
                    "Rear",
                    "Right",
                    "OUT_CAMERA_REAR_TRIGGER");
            }

            if (configuration.MachineMultiShotTrigger.Rear == null)
            {
                configuration.MachineMultiShotTrigger.Rear = Models.MultiShotTrigger.MultiShotTriggerOptions.CreateDefault(
                    "Rear", "Rear", "OUT_CAMERA_REAR_TRIGGER");
            }

            if (configuration.MachineMultiShotTrigger.Bottom == null)
            {
                configuration.MachineMultiShotTrigger.Bottom = Models.MultiShotTrigger.MultiShotTriggerOptions.CreateDefault(
                    "Bottom",
                    "Bottom",
                    "OUT_CAMERA_BOTTOM_TRIGGER");
            }

            if (configuration.MachineMultiShotTrigger.Side.VisionProStitching == null)
            {
                configuration.MachineMultiShotTrigger.Side.VisionProStitching = new Models.MultiShotTrigger.MultiShotVisionProStitchingOptions();
            }

            if (configuration.MachineMultiShotTrigger.Right.VisionProStitching == null)
            {
                configuration.MachineMultiShotTrigger.Right.VisionProStitching = new Models.MultiShotTrigger.MultiShotVisionProStitchingOptions();
            }
            if (configuration.MachineMultiShotTrigger.Rear.VisionProStitching == null)
            {
                configuration.MachineMultiShotTrigger.Rear.VisionProStitching = new Models.MultiShotTrigger.MultiShotVisionProStitchingOptions();
            }

            if (configuration.MachineMultiShotTrigger.Bottom.VisionProStitching == null)
            {
                configuration.MachineMultiShotTrigger.Bottom.VisionProStitching = new Models.MultiShotTrigger.MultiShotVisionProStitchingOptions();
            }

            EnsureDualIlluminationDefaults(configuration.MachineMultiShotTrigger.Side);
            EnsureDualIlluminationDefaults(configuration.MachineMultiShotTrigger.Right);
            EnsureDualIlluminationDefaults(configuration.MachineMultiShotTrigger.Rear);
            EnsureDualIlluminationDefaults(configuration.MachineMultiShotTrigger.Bottom);

            if (configuration.MachineMultiShotTrigger.Side.TargetLateTolerancePulses <= 0)
            {
                configuration.MachineMultiShotTrigger.Side.TargetLateTolerancePulses = 500;
            }

            if (configuration.MachineMultiShotTrigger.Right.TargetLateTolerancePulses <= 0)
            {
                configuration.MachineMultiShotTrigger.Right.TargetLateTolerancePulses = 500;
            }
            if (configuration.MachineMultiShotTrigger.Rear.TargetLateTolerancePulses <= 0)
            {
                configuration.MachineMultiShotTrigger.Rear.TargetLateTolerancePulses = 500;
            }

            if (configuration.MachineMultiShotTrigger.Bottom.TargetLateTolerancePulses <= 0)
            {
                configuration.MachineMultiShotTrigger.Bottom.TargetLateTolerancePulses = 500;
            }

            var bindings = configuration.RuntimeBindings;
            if (string.IsNullOrWhiteSpace(bindings.TriggerSchedulingMode))
            {
                bindings.TriggerSchedulingMode = "VirtualConveyor";
            }

            if (bindings.PhotocellDebounceMs <= 0)
            {
                bindings.PhotocellDebounceMs = 80;
            }

            if (bindings.MinimumRetriggerGapMs <= 0)
            {
                bindings.MinimumRetriggerGapMs = 120;
            }

            bindings.MultiShotCompanionMaxLagMs = Clamp(bindings.MultiShotCompanionMaxLagMs, 1000, 120000, 15000);

            if (bindings.TopTriggerPulseMs <= 0)
            {
                bindings.TopTriggerPulseMs = 40;
            }

            if (bindings.SideTriggerPulseMs <= 0)
            {
                bindings.SideTriggerPulseMs = 40;
            }

            bindings.LivePreviewIntervalMs = Clamp(bindings.LivePreviewIntervalMs, 100, 30000, 1000);
            bindings.LivePreviewPulseMs = Clamp(bindings.LivePreviewPulseMs, 1, 5000, 20);
            bindings.LivePreviewExposureUs = Clamp(bindings.LivePreviewExposureUs, 0.0, 1000000.0, 0.0);

            bindings.DefectClassifierInputWidth = Clamp(bindings.DefectClassifierInputWidth, 16, 1024, 224);
            bindings.DefectClassifierInputHeight = Clamp(bindings.DefectClassifierInputHeight, 16, 1024, 224);
            bindings.DefectClassifierNormalizeMean = Clamp(bindings.DefectClassifierNormalizeMean, -1000000.0, 1000000.0, 0.0);
            bindings.DefectClassifierNormalizeStd = ClampPositive(bindings.DefectClassifierNormalizeStd, 0.000001, 1000000.0, 255.0);
            bindings.DefectClassifierMinConfidence = Clamp(bindings.DefectClassifierMinConfidence, 0.0, 1.0, 0.0);
            if (string.IsNullOrWhiteSpace(bindings.DefectClassifierTrainingPythonPath))
            {
                bindings.DefectClassifierTrainingPythonPath = "python";
            }
            if (string.IsNullOrWhiteSpace(bindings.DefectClassifierTrainingOutputDir))
            {
                bindings.DefectClassifierTrainingOutputDir = @"C:\QtisVision\AI\Models";
            }
            bindings.DefectClassifierTrainingEpochs = Clamp(bindings.DefectClassifierTrainingEpochs, 1, 500, 20);

            bindings.SideDefectClassifierInputWidth = Clamp(bindings.SideDefectClassifierInputWidth, 16, 1024, 224);
            bindings.SideDefectClassifierInputHeight = Clamp(bindings.SideDefectClassifierInputHeight, 16, 1024, 224);
            bindings.SideDefectClassifierNormalizeMean = Clamp(bindings.SideDefectClassifierNormalizeMean, -1000000.0, 1000000.0, 0.0);
            bindings.SideDefectClassifierNormalizeStd = ClampPositive(bindings.SideDefectClassifierNormalizeStd, 0.000001, 1000000.0, 255.0);
            bindings.SideDefectClassifierMinConfidence = Clamp(bindings.SideDefectClassifierMinConfidence, 0.0, 1.0, 0.0);
            EnsureAdditionalDefectClassifierProfiles(bindings);

            bindings.MachineHealthDigestIntervalMinutes = Clamp(bindings.MachineHealthDigestIntervalMinutes, 1, 1440, 480);
            bindings.MachineHealthCriticalEtaProducts = Clamp(bindings.MachineHealthCriticalEtaProducts, 1, 1000000, 50);
            bindings.MachineHealthDiskCriticalHours = ClampPositive(bindings.MachineHealthDiskCriticalHours, 0.5, 8760.0, 24.0);

            bindings.EmailSmtpPort = Clamp(bindings.EmailSmtpPort, 1, 65535, 587);

            bindings.AiInspectionMeasurementRetentionDays = ClampZeroAllowed(bindings.AiInspectionMeasurementRetentionDays, 3650, 180);
            bindings.AiHealthSnapshotRetentionDays = ClampZeroAllowed(bindings.AiHealthSnapshotRetentionDays, 3650, 90);
            bindings.AiTrainingSampleRetentionDays = ClampZeroAllowed(bindings.AiTrainingSampleRetentionDays, 3650, 0);

            if (bindings.EncoderCalibrationTachometerSpeedMetersPerMinute <= 0.0)
            {
                bindings.EncoderCalibrationTachometerSpeedMetersPerMinute = 40.0;
            }
        }

        /// <summary>
        /// Un solo profilo classificatore per ciascuna camera REAR/RIGHT, FRONT e BOTTOM, con i
        /// valori riportati nei limiti. Ruoli sconosciuti o duplicati vengono scartati (vince il
        /// primo), cosi' un file modificato a mano non crea due modelli per la stessa camera.
        /// </summary>
        private static void EnsureAdditionalDefectClassifierProfiles(MachineRuntimeBindings bindings)
        {
            var source = bindings.AdditionalDefectClassifierProfiles ?? new List<DefectClassifierCameraProfile>();
            var normalized = new List<DefectClassifierCameraProfile>();

            foreach (string role in DefectClassifierCameraProfile.AdditionalRoles)
            {
                DefectClassifierCameraProfile profile = source.FirstOrDefault(item =>
                    string.Equals(DefectClassifierCameraProfile.NormalizeRole(item?.CameraRole), role, StringComparison.OrdinalIgnoreCase))
                    ?? new DefectClassifierCameraProfile();

                profile.CameraRole = role;
                profile.InputWidth = Clamp(profile.InputWidth, 16, 1024, 224);
                profile.InputHeight = Clamp(profile.InputHeight, 16, 1024, 224);
                profile.NormalizeMean = Clamp(profile.NormalizeMean, -1000000.0, 1000000.0, 0.0);
                profile.NormalizeStd = ClampPositive(profile.NormalizeStd, 0.000001, 1000000.0, 255.0);
                profile.MinConfidence = Clamp(profile.MinConfidence, 0.0, 1.0, 0.0);
                normalized.Add(profile);
            }

            int discarded = source.Count - normalized.Count(profile => source.Contains(profile));
            if (discarded > 0)
            {
                Logger.Warn($"AI_CLASSIFIER_PROFILES_NORMALIZED|discarded={discarded}|reason=unknown-or-duplicate-role");
            }

            bindings.AdditionalDefectClassifierProfiles = normalized;
        }

        private static void EnsureDualIlluminationDefaults(Models.MultiShotTrigger.MultiShotTriggerOptions options)
        {
            if (options == null)
            {
                return;
            }

            if (options.DualIllumination == null)
            {
                options.DualIllumination = new Models.MultiShotTrigger.MultiShotDualIlluminationOptions();
            }

            var dual = options.DualIllumination;
            dual.ExposureTimeUs = ClampPositive(dual.ExposureTimeUs, 1.0, 1000000.0, 500.0);
            if (string.IsNullOrWhiteSpace(dual.FrontOutputLine))
            {
                dual.FrontOutputLine = "Line3";
            }

            if (string.IsNullOrWhiteSpace(dual.BackOutputLine))
            {
                dual.BackOutputLine = "Line4";
            }

            if (string.IsNullOrWhiteSpace(dual.ActiveOutputSource))
            {
                dual.ActiveOutputSource = "ExposureActive";
            }

            if (string.IsNullOrWhiteSpace(dual.DualIlluminationEnabledInputName))
            {
                dual.DualIlluminationEnabledInputName = "dualIlluminationEnabled";
            }

            if (string.IsNullOrWhiteSpace(dual.FrontFirstInputName))
            {
                dual.FrontFirstInputName = "frontFirst";
            }
        }

        private static int Clamp(int value, int min, int max, int fallback)
        {
            if (value <= 0)
            {
                value = fallback;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static int ClampZeroAllowed(int value, int max, int fallback)
        {
            if (value < 0)
            {
                value = fallback;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = fallback;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static double ClampPositive(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
            {
                value = fallback;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static List<MachineSignalDefinition> CloneSignals(IEnumerable<MachineSignalDefinition> signals)
        {
            return (signals ?? Enumerable.Empty<MachineSignalDefinition>())
                .Where(signal => signal != null &&
                    (!string.IsNullOrWhiteSpace(signal.SignalCode) || !string.IsNullOrWhiteSpace(signal.Channel)))
                .Select(signal => new MachineSignalDefinition
                {
                    SignalCode = signal.SignalCode,
                    Description = signal.Description,
                    Direction = signal.Direction,
                    Category = signal.Category,
                    Board = signal.Board,
                    Channel = signal.Channel,
                    Polarity = signal.Polarity,
                    ReservedForRealSignal = signal.ReservedForRealSignal,
                    Notes = signal.Notes
                })
                .ToList();
        }

        private static List<EncoderConfigurationTemplate> CloneEncoders(IEnumerable<EncoderConfigurationTemplate> encoders)
        {
            return (encoders ?? Enumerable.Empty<EncoderConfigurationTemplate>())
                .Select(encoder => new EncoderConfigurationTemplate
                {
                    AxisName = encoder.AxisName,
                    Board = encoder.Board,
                    Channel = encoder.Channel,
                    PulsesPerRevolution = encoder.PulsesPerRevolution,
                    MillimetersPerRevolution = encoder.MillimetersPerRevolution,
                    MachineZeroLabel = encoder.MachineZeroLabel,
                    PhotocellMachineOffsetMm = encoder.PhotocellMachineOffsetMm,
                    TrackingMode = encoder.TrackingMode,
                    TriggerOffsetMm = encoder.TriggerOffsetMm,
                    RejectOffsetMm = encoder.RejectOffsetMm,
                    Notes = encoder.Notes
                })
                .ToList();
        }

        private static List<MachineInterventionPoint> CloneInterventionPoints(IEnumerable<MachineInterventionPoint> points)
        {
            return (points ?? Enumerable.Empty<MachineInterventionPoint>())
                .Where(point => point != null &&
                    (!string.IsNullOrWhiteSpace(point.PointCode) || !string.IsNullOrWhiteSpace(point.ActionType)))
                .Select(point => new MachineInterventionPoint
                {
                    PointCode = point.PointCode,
                    Description = point.Description,
                    Enabled = point.Enabled,
                    ReferenceCode = point.ReferenceCode,
                    BaseOffsetMm = point.BaseOffsetMm,
                    TrimOffsetMm = point.TrimOffsetMm,
                    ActionType = point.ActionType,
                    SignalCode = point.SignalCode,
                    PulseMs = point.PulseMs,
                    IsStandard = point.IsStandard,
                    Notes = point.Notes
                })
                .ToList();
        }

        private static string SanitizeForFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string((value ?? string.Empty)
                .Trim()
                .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
                .ToArray());
        }
    }
}

