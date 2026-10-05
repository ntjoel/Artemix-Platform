using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Inspector.Models;

namespace QtisVisionPanel.Inspector.Services
{
    public sealed class InspectorProfileFactory
    {
        public InspectorDataSourceProfile CreateFromMainConfiguration(AppConfig appConfig)
        {
            appConfig = appConfig ?? new AppConfig();

            int port = 3306;
            int.TryParse(appConfig.MySqlConnection.port, out port);
            if (port <= 0)
                port = 3306;

            return new InspectorDataSourceProfile
            {
                ProfileName = "Main project runtime",
                UseMockData = false,
                DatabaseHost = appConfig.MySqlConnection.Host ?? "localhost",
                DatabasePort = port,
                DatabaseName = appConfig.MySqlConnection.Db ?? string.Empty,
                DatabaseUser = appConfig.MySqlConnection.User ?? string.Empty,
                DatabasePassword = appConfig.MySqlConnection.Password ?? string.Empty,
                DisableSsl = appConfig.MySqlConnection.SslDisabled,
                RejectedPieceMode = "ClassificationRejected",
                PieceTableName = "tblgenerale",
                ProductionTableName = "tblproduzione",
                ImageRootPath = appConfig.Configuration.ImageDir ?? string.Empty,
                DataHostnames = appConfig.Configuration.DataHostnames ?? string.Empty,
                ResolveRelativePiecePathsFromImageRoot = true
            };
        }
    }
}
