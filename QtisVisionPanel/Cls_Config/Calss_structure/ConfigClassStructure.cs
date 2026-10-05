using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace QtisVisionPanel.Cls_Config.Calss_structure
{
    public class MySqlConnectionSettings
    {
        public string Host { get; set; } = "localhost";
        public string User { get; set; }="root";
        public string Password { get; set; }="CHANGE_ME";
        public string Db { get; set; }="quatisv";
        public string port { get; set; }="3306";
        public string AuthPlugin { get; set; }="mysql_native_password";
        public bool SslDisabled { get; set; }= true;
    }

    public class Configuration
    {
        public string Language { get; set; }="eng";
        public string Machine_type { get; set; }="Facial";
        public string Lang1 { get; set; }="ENG";
        public string Lang2 { get; set; }="ITA";
        public string Recipe_Folder { get; set; } = "C:\\QtisVision\\Programs\\";
        public string ImageDir { get; set; } = "D:\\QtisVision\\Pieces\\";
        public string LastRecipe { get; set; }
        public string NumCamera { get; set; }
        public string Compagny { get; set; }
        public string BackupRecipe { get; set; } = "D:\\QtisVision\\JopBackup";
        public string LastReset { get; set; }
        public string User { get; set; }
        public string DataHostnames { get; set; }="localhost";
        public string Dashboard { get; set; }
        public string LogshowImage { get; set; }
        public int DayToCleanData { get; set; }
        public string MachineType { get; set; } = "Packs";
        public string ExternalAppPath { get; set; }
        public string CurrentUserRole { get; set; }
        public string externalApp_automation { get; set; }= string.Empty;
        public string externalApp_tools { get; set; } = string.Empty;
        public string externalApp_reports { get; set; } = string.Empty;
        // LEGACY: uscite allarmi non piu' lette dal runtime dalla 3.0.7.8; l'uscita fisica
        // si imposta per ogni scheda allarme (OutputChannelId). Default vuoto: la PCIE-1756
        // ha solo DO00-DO31 (i vecchi default DO40/DO41 non esistono sulla scheda).
        // I Config.xml esistenti mantengono il valore salvato (non viene riscritto).
        public string BlockingAlarmOutput { get; set; } = string.Empty;
        public string NonBlockingAlarmOutput { get; set; } = string.Empty;
        public string TopCameraSerial { get; set; }= "S1214730";
        public string SideCameraSerial { get; set; }= "S1232080";
        public string FrontCameraSerial { get; set; } = "S1232080";
        public string RearCameraSerial { get; set; } = "S1232080";
        public string RightCameraSerial { get; set; } = string.Empty;
        public string BottomCameraSerial { get; set; } = "S1232080";
        public string FrontTraceabilityPresenceOutput { get; set; } = "TraceabilityPresent";
        public string FrontTraceabilityCodeOutput { get; set; } = "TraceabilityCode";
        public string LeftSideSealingOutput { get; set; } = "LeftSideSealingOk";
        public string LeftRollCountOutput { get; set; } = "LeftRollCountOk";
        public string ThreeDHeightOutput { get; set; } = "ThreeDHeight";
        public string ThreeDHeightMedianOutput { get; set; } = "HeightMedian";
        public string ThreeDHeightHighTailOutput { get; set; } = "HeightHighTail";
        public string ThreeDHeightValidPixelRatioOutput { get; set; } = "HeightValidPixelRatio";
        public string ThreeDHeightBulgeOutput { get; set; } = "HeightBulge";
        public string ThreeDHeightMaximumOutput { get; set; } = "ThreeDHeight_MAX";
        public string ThreeDWidthOutput { get; set; } = "ThreeDWidth";
        public string ThreeDLengthOutput { get; set; } = "ThreeDLength";
        // Global calibration correction in millimetres. The runtime validates
        // VisionProRaw + Offset against the nominal/tolerance stored in the recipe.
        // Missing fields in existing Config.xml files deserialize as zero.
        public double Top3DHeightMeasurementOffsetMm { get; set; } = 0;
        public double Top3DWidthMeasurementOffsetMm { get; set; } = 0;
        public double Top3DLengthMeasurementOffsetMm { get; set; } = 0;
        public string Top3DRerenderResultOutput { get; set; } = "Top3DRerenderResult";
        // Nome della feature GigE/GenICam per la "Detection Sensitivity" della testa 3D (es. L38).
        // Proprieta' hardware (uguale per tutte le ricette) -> vive in Config, non in ricetta.
        // Il valore per-prodotto sta invece in recipeParamTop3D.Top3DDetectionSensitivity.
        // Default indicativo: verificare/correggere col nome reale dall'albero feature GigE del sensore.
        public string Top3DDetectionSensitivityFeature { get; set; } = "DetectionSensitivity";
        public string Top3DPointCloudOutput { get; set; } = "Top3DPointCloud";
        public bool SaveTop3DRendered2DImage { get; set; } = true;
        public bool SaveTop3DRangeImage { get; set; } = true;
        public bool SaveTop3DPointCloudCsv { get; set; } = true;
        public bool AutoResumeVisionWhenIdle { get; set; } = true;
        public int AutoResumeVisionIdleSeconds { get; set; } = 45;
        public int AutoResumeVisionRetryCooldownSeconds { get; set; } = 30;
        public bool AutoLogoutWhenIdle { get; set; } = true;
        public int AutoLogoutIdleMinutes { get; set; } = 15;
        public bool AutoLoginAdministratorForDemo { get; set; } = false;
        public bool ApplyCameraTriggerDelayToHardwareAtStartup { get; set; } = false;

        /// <summary>
        /// UNC or local path of the shared folder where each saved piece-folder is mirrored
        /// after every image save. Leave empty to disable sync.
        /// Example: \\192.168.1.100\QtisShared\Pieces
        /// </summary>
        public string SharedImageDir { get; set; } = "";

    }

    public class IO
    {
        public int ExpulsionOffset { get; set; } 
        public int ExpWidth { get; set; }
        public int delay { get; set; }
    }
    public class Inspections
    {
        public string LogoPosition { get; }
        public string OpenFlapsArea { get; }
        public string Heigth { get; }
        public string SealingArea { get; }
        public string EL_Classify { get; }
        public string EL_Score { get; }

    }
    public class SaveImagePercentage
    {
        public int PercSaveDefect { get; set; }
        public int PercSaveOk { get; set; }
        public int session { get; set; }
        public int batch { get; set; }
        public int Incremento_imagini { get; set; }
        public int pezzo { get; set; }
        public int Terms_OK { get; set; }
        public int Terms_Fail { get; set; }
    }

    public class LasRunParam
    {
        public string LastRunView1 { get; set; } = "Fixture.CogFixtureTool1.OutputImage";
        public string LastRunView2 { get; set; }= "Fixture.CogFixtureTool1.OutputImage";
        public string LastRunView3 { get; set; } = "Fixture.CogFixtureTool1.OutputImage";
        public string LastRunView4 { get; set; } = "Fixture.CogFixtureTool1.OutputImage";
    }
    public class AutoSwitchSettings
    {
        public double SizeTolerancePercent { get; set; } = 0.20;
        public double AspectRatioTolerance { get; set; } = 0.10;
        public int MinImageWidth { get; set; } = 100;
        public int MinImageHeight { get; set; } = 100;
        public int MaxConsecutiveFailures { get; set; } = 5;
        public double MinimumMatchScore { get; set; } = 0.7;
        public bool RequireSizeCheck { get; set; } = true;
    }

    public class SystemDiagnosticsSettings
    {
        public bool Enabled { get; set; } = true;
        public int RefreshIntervalSeconds { get; set; } = 10;
        public double CpuWarningPercent { get; set; } = 80;
        public double CpuCriticalPercent { get; set; } = 92;
        public double RamWarningPercent { get; set; } = 80;
        public double RamCriticalPercent { get; set; } = 90;
        public double DiskWarningPercent { get; set; } = 85;
        public double DiskCriticalPercent { get; set; } = 93;
        public bool AutoCleanupEnabled { get; set; } = true;
        public double DiskCleanupStartPercent { get; set; } = 94;
        public double DiskCleanupTargetPercent { get; set; } = 88;
        public int CleanupCooldownMinutes { get; set; } = 15;
        public int MaxDeletedFoldersPerCycle { get; set; } = 40;
        public string AdditionalCleanupFolders { get; set; } = string.Empty;
        public bool DatabaseArchiveMonitoringEnabled { get; set; } = true;
        public bool DatabaseArchiveAutoCleanupEnabled { get; set; } = true;
        public string DatabaseArchiveTableName { get; set; } = "tblgenerale";
        public string DatabaseArchiveTimestampColumn { get; set; } = "DataeOra";
        public double DatabaseArchiveCleanupStartMb { get; set; } = 2048;
        public double DatabaseArchiveCleanupTargetMb { get; set; } = 1536;
        public int DatabaseArchiveRetentionDays { get; set; } = 90;
        public int DatabaseArchiveDeleteBatchSize { get; set; } = 5000;
        public int DatabaseArchiveCleanupCooldownMinutes { get; set; } = 60;
        public bool TemperatureMonitoringEnabled { get; set; } = true;
        public string HardwareTemperatureProvider { get; set; } = "Auto";
        public int HardwareTemperaturePollingSeconds { get; set; } = 10;
        public double CpuTemperatureWarningC { get; set; } = 80;
        public double CpuTemperatureCriticalC { get; set; } = 90;
        public double DiskTemperatureWarningC { get; set; } = 52;
        public double DiskTemperatureCriticalC { get; set; } = 60;
        public double MemoryTemperatureWarningC { get; set; } = 65;
        public double MemoryTemperatureCriticalC { get; set; } = 75;
        public double MotherboardTemperatureWarningC { get; set; } = 70;
        public double MotherboardTemperatureCriticalC { get; set; } = 80;
    }

    public class PowerFlex525Settings
    {
        public bool Enabled { get; set; } = true;
        public string IpAddress { get; set; } = string.Empty;
        public int TimeoutMs { get; set; } = 1000;
        public int PollIntervalMs { get; set; } = 3000;
        public string HistoryFilePath { get; set; } = @"D:\QtisVision\Logs\PowerFlex525ChangeHistory.json";
        public double MetersPerMinutePerHz { get; set; } = 0;
        public double DrivePulleyDiameterMm { get; set; } = 0;
        public double GearRatio { get; set; } = 1;
    }

    public class AnalyticsCardConfig
    {
        public string CardType { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
    }

    public class AnalyticsDashboardSettings
    {
        public int RefreshMinutes { get; set; } = 5;
        public string TotalBarColor { get; set; } = "#2563EB";
        public string GoodBarColor { get; set; } = "#16A34A";
        public string NoGoodBarColor { get; set; } = "#DC2626";

        [XmlArray("Cards")]
        [XmlArrayItem("Card")]
        public List<AnalyticsCardConfig> Cards { get; set; } = new List<AnalyticsCardConfig>
        {
            new AnalyticsCardConfig { CardType = "ProductionOverview", IsVisible = true },
            new AnalyticsCardConfig { CardType = "DefectPie", IsVisible = true },
            new AnalyticsCardConfig { CardType = "HeightTrend", IsVisible = true },
            new AnalyticsCardConfig { CardType = "ThreeDHeightTrend", IsVisible = true },
            new AnalyticsCardConfig { CardType = "WidthTrend", IsVisible = true },
            new AnalyticsCardConfig { CardType = "LengthTrend", IsVisible = true }
        };
    }

    public class Machine
    {
        public List<string> machine_type { get; set; }
    }

    [Serializable]

    [XmlRoot("AppConfig")]

    public class AppConfig
    {
        public MySqlConnectionSettings MySqlConnection { get; set; } = new MySqlConnectionSettings();
        public Configuration Configuration { get; set; } = new Configuration();
        public Machine Machine { get; set; } = new Machine();
        public IO IO { get; set; } = new IO();


        [XmlArray("Roles")]
        [XmlArrayItem("Item")]

        public List<string> Roles { get; set; } = new List<string>();


        [XmlArray("Ejection_status")]
        [XmlArrayItem("Item")]

        public List<string> Ejection_status { get; set; } = new List<string>();
        public LasRunParam LasRunParam { get; set; } = new LasRunParam();
        public SaveImagePercentage _SaveImagePercentage { get; set; } = new SaveImagePercentage();
        public Inspections _Inspection { get; set; } = new Inspections();
        // ... proprietà esistenti ...
        public AutoSwitchSettings AutoSwitchSettings { get; set; } = new AutoSwitchSettings();
        public SystemDiagnosticsSettings SystemDiagnostics { get; set; } = new SystemDiagnosticsSettings();
        public PowerFlex525Settings PowerFlex525 { get; set; } = new PowerFlex525Settings();
        public AnalyticsDashboardSettings AnalyticsDashboard { get; set; } = new AnalyticsDashboardSettings();
    }

    [Serializable]
    [XmlRoot("PreferenceViewConfig")]
    public class PreferenceViewConfig
    {
        public LanguageSettings LanguageSettings { get; set; } = new LanguageSettings();
        public SystemSettings SystemSettings { get; set; } = new SystemSettings();
        public UserManagementSettings UserManagementSettings { get; set; } = new UserManagementSettings();
    }
    [Serializable]
    public class LanguageSettings
    {
        public string CurrentLanguage { get; set; } = "eng";

        [XmlArray("AvailableLanguages")]
        [XmlArrayItem("Language")]
        public List<LanguageInfo> AvailableLanguages { get; set; } = new List<LanguageInfo>();

        public LanguagePaths Paths { get; set; } = new LanguagePaths();
    }
    [Serializable]
    public class LanguageInfo
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public string FlagImage { get; set; }
        public string ResourceFile { get; set; }

        [XmlIgnore]
        public string FullFlagImagePath
        {
            get
            {
                // Correzione: non si può accedere a LanguageSettings come tipo statico.
                // Occorre passare il percorso tramite una proprietà o metodo, oppure
                // calcolare il percorso altrove.
                // Qui si assume che FlagImagesPath sia sempre costante.
                string defaultPath = @"C:\QtisVision\Language\img\icons\langs\";
                return System.IO.Path.Combine(defaultPath, FlagImage);
            }
        }
    }

    [Serializable]
    public class LanguagePaths
    {
        public string FlagImagesPath { get; set; } = @"C:\QtisVision\Language\img\icons\langs\";
        public string LanguageFilesPath { get; set; } = @"C:\QtisVision\Language\";
    }

    [Serializable]
    public class SystemSettings
    {
        public string SavePath { get; set; } = @"E:\MVS\pieces";
        public bool AutoSaveEnabled { get; set; } = true;
        public int AutoSaveIntervalMinutes { get; set; } = 5;
    }

    [Serializable]
    public class UserManagementSettings
    {
        public string DefaultRole { get; set; } = "Viewer";

        [XmlArray("AvailableRoles")]
        [XmlArrayItem("Role")]
        public List<string> AvailableRoles { get; set; } = new List<string>
    {
        "Administrator", "Installer", "Expert", "Operator", "Viewer"
    };
    }

}
