namespace QtisVisionPanel.Inspector.Models
{
    public sealed class InspectorDataSourceProfile
    {
        public string ProfileName { get; set; } = string.Empty;
        public bool UseMockData { get; set; }
        public string DatabaseHost { get; set; } = string.Empty;
        public int DatabasePort { get; set; } = 3306;
        public string DatabaseName { get; set; } = string.Empty;
        public string DatabaseUser { get; set; } = string.Empty;
        public string DatabasePassword { get; set; } = string.Empty;
        public bool DisableSsl { get; set; } = true;
        public string RejectedPieceMode { get; set; } = "ClassificationRejected";
        public string PieceTableName { get; set; } = "tblgenerale";
        public string ProductionTableName { get; set; } = "tblproduzione";
        public string ImageRootPath { get; set; } = string.Empty;
        public string DataHostnames { get; set; } = string.Empty;
        public bool ResolveRelativePiecePathsFromImageRoot { get; set; } = true;
    }
}
