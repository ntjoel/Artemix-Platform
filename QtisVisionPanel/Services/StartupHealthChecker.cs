using MySql.Data.MySqlClient;
using NLog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public enum HealthStatus
    {
        OK,
        Warning,
        Degraded,
        Critical,
        Unavailable,
        Unknown
    }

    public class HealthCheckReport
    {
        public HealthStatus Database  { get; set; } = HealthStatus.Unknown;
        public HealthStatus VppFile   { get; set; } = HealthStatus.Unknown;
        public HealthStatus DiskSpace { get; set; } = HealthStatus.Unknown;
        public HealthStatus IoBoard   { get; set; } = HealthStatus.Unknown;

        public bool HasCritical =>
            Database  == HealthStatus.Critical ||
            VppFile   == HealthStatus.Critical ||
            DiskSpace == HealthStatus.Critical;

        public string SummaryText
        {
            get
            {
                if (DiskSpace == HealthStatus.Critical)
                    return "CRITICO: Spazio disco quasi esaurito — avviare pulizia immediata";
                if (Database == HealthStatus.Unavailable || Database == HealthStatus.Degraded)
                    return "Database non raggiungibile — avvio in modalità offline";
                if (VppFile == HealthStatus.Unavailable)
                    return "File ricetta VisionPro non trovato — selezionare ricetta manualmente";
                if (DiskSpace == HealthStatus.Warning)
                    return "Avviso: Spazio disco limitato (< 20 GB)";
                if (VppFile == HealthStatus.Warning)
                    return "Nessuna ricetta configurata — selezionare una ricetta";
                return "Tutti i sottosistemi verificati OK";
            }
        }
    }

    /// <summary>
    /// Esegue un check rapido dei sottosistemi durante lo splash screen.
    /// Non blocca l'avvio — logga i risultati e restituisce il report.
    /// </summary>
    public sealed class StartupHealthChecker
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public async Task<HealthCheckReport> RunAsync(IProgress<string> progress = null)
        {
            var report = new HealthCheckReport();

            progress?.Report("Verifica connessione database...");
            report.Database = await CheckDatabaseAsync();

            progress?.Report("Verifica file ricetta VisionPro...");
            report.VppFile = CheckVppFile();

            progress?.Report("Verifica spazio disco...");
            report.DiskSpace = CheckDiskSpace();

            report.IoBoard = CheckIoBoard();

            _log.Info(
                "STARTUP_HEALTH_CHECK|db={0}|vpp={1}|disk={2}|io={3}|summary={4}",
                report.Database, report.VppFile, report.DiskSpace, report.IoBoard,
                report.SummaryText);

            return report;
        }

        private static async Task<HealthStatus> CheckDatabaseAsync()
        {
            try
            {
                var cfg = MainWindow.configManager?.Config?.MySqlConnection;
                if (cfg == null) return HealthStatus.Unknown;

                string cs = $"Server={cfg.Host};Uid={cfg.User};Pwd={cfg.Password};Port={cfg.port};Connection Timeout=3;";
                using (var conn = new MySqlConnection(cs))
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                {
                    await conn.OpenAsync(cts.Token);
                    return HealthStatus.OK;
                }
            }
            catch (OperationCanceledException)
            {
                _log.Warn("HEALTH_DB_TIMEOUT — MySQL not responding within 3 s");
                return HealthStatus.Degraded;
            }
            catch (Exception ex)
            {
                _log.Warn(ex, "HEALTH_DB_UNAVAILABLE");
                return HealthStatus.Unavailable;
            }
        }

        private static HealthStatus CheckVppFile()
        {
            try
            {
                var cfg = MainWindow.configManager?.Config?.Configuration;
                if (cfg == null) return HealthStatus.Unknown;

                var lastRecipe = cfg.LastRecipe;
                if (string.IsNullOrWhiteSpace(lastRecipe))
                {
                    _log.Warn("HEALTH_VPP_NOT_CONFIGURED — no LastRecipe in Config.xml");
                    return HealthStatus.Warning;
                }

                if (File.Exists(lastRecipe)) return HealthStatus.OK;

                if (!string.IsNullOrWhiteSpace(cfg.Recipe_Folder))
                {
                    var combined = Path.Combine(cfg.Recipe_Folder, Path.GetFileName(lastRecipe));
                    if (File.Exists(combined)) return HealthStatus.OK;
                }

                _log.Warn("HEALTH_VPP_NOT_FOUND|lastRecipe={0}", lastRecipe);
                return HealthStatus.Unavailable;
            }
            catch (Exception ex)
            {
                _log.Warn(ex, "HEALTH_VPP_CHECK_FAILED");
                return HealthStatus.Unknown;
            }
        }

        private static HealthStatus CheckDiskSpace()
        {
            try
            {
                var imageDir = MainWindow.configManager?.Config?.Configuration?.ImageDir ?? @"D:\";
                var root = Path.GetPathRoot(imageDir);
                if (string.IsNullOrEmpty(root)) return HealthStatus.Unknown;

                if (!Directory.Exists(root)) return HealthStatus.Unknown;

                var drive = new DriveInfo(root);
                double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

                if (freeGb < 5)
                {
                    _log.Error("HEALTH_DISK_CRITICAL|drive={0}|freeGb={1:F1}", root, freeGb);
                    return HealthStatus.Critical;
                }
                if (freeGb < 20)
                {
                    _log.Warn("HEALTH_DISK_WARNING|drive={0}|freeGb={1:F1}", root, freeGb);
                    return HealthStatus.Warning;
                }
                _log.Info("HEALTH_DISK_OK|drive={0}|freeGb={1:F1}", root, freeGb);
                return HealthStatus.OK;
            }
            catch (Exception ex)
            {
                _log.Warn(ex, "HEALTH_DISK_CHECK_FAILED");
                return HealthStatus.Unknown;
            }
        }

        private static HealthStatus CheckIoBoard()
        {
            var io = ServiceLocator.IoManager;
            if (io == null) return HealthStatus.Unavailable;
            if (io.IsSimulationMode) return HealthStatus.Warning;
            return io.IsInitialized ? HealthStatus.OK : HealthStatus.Degraded;
        }
    }
}
