using System;
using QtisVisionPanel.ServerMessage;
using System.Windows.Media;

namespace QtisVisionPanel.Models
{
    public enum DiagnosticsSeverity
    {
        Healthy = 0,
        Warning = 1,
        Critical = 2
    }

    public class SystemDriveStatusItem
    {
        public string DriveName { get; set; }
        public string RootPath { get; set; }
        public string VolumeLabel { get; set; }
        public string DriveType { get; set; }
        public double TotalSizeGb { get; set; }
        public double FreeSpaceGb { get; set; }
        public double UsedSpaceGb { get; set; }
        public double UsedPercent { get; set; }
        public double FreePercent { get; set; }
        public bool IsCleanupTarget { get; set; }
        public string CleanupFoldersDisplay { get; set; }
        public DiagnosticsSeverity Severity { get; set; }

        public string UsageText
        {
            get
            {
                return string.Format(GetMessage("Sub_entry_DiagnosticsDiskUsageFormat", "{0:0.0}% used"), UsedPercent);
            }
        }

        public string CapacityText
        {
            get
            {
                return string.Format(GetMessage("Sub_entry_DiagnosticsDiskCapacityFormat", "{0:0.0} GB free / {1:0.0} GB total"), FreeSpaceGb, TotalSizeGb);
            }
        }

        public string SeverityText
        {
            get
            {
                return GetSeverityText(Severity);
            }
        }

        public Brush SeverityBrush
        {
            get
            {
                return CreateBrush(GetSeverityHex());
            }
        }

        private string GetSeverityHex()
        {
            switch (Severity)
            {
                case DiagnosticsSeverity.Critical:
                    return "#D64545";
                case DiagnosticsSeverity.Warning:
                    return "#D9942B";
                default:
                    return "#2E9E75";
            }
        }

        private static Brush CreateBrush(string colorHex)
        {
            return (Brush)new BrushConverter().ConvertFromString(colorHex);
        }

        private static string GetSeverityText(DiagnosticsSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticsSeverity.Critical:
                    return GetMessage("Sub_entry_DiagnosticsCritical", "Critical");
                case DiagnosticsSeverity.Warning:
                    return GetMessage("Sub_entry_Warning", "Warning");
                default:
                    return GetMessage("Sub_entry_DiagnosticsHealthy", "Healthy");
            }
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }
    }

    public class SystemDiagnosticsAlertItem
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Category { get; set; }
        public DateTime Timestamp { get; set; }
        public DiagnosticsSeverity Severity { get; set; }

        public string SeverityText
        {
            get
            {
                return GetSeverityText(Severity);
            }
        }

        public string TimestampDisplay
        {
            get
            {
                return string.Format(GetMessage("Sub_entry_DiagnosticsDetectedAt", "Detected at {0:dd/MM/yyyy HH:mm:ss}"), Timestamp);
            }
        }

        public Brush SeverityBrush
        {
            get
            {
                return CreateBrush(GetSeverityHex());
            }
        }

        private string GetSeverityHex()
        {
            switch (Severity)
            {
                case DiagnosticsSeverity.Critical:
                    return "#D64545";
                case DiagnosticsSeverity.Warning:
                    return "#D9942B";
                default:
                    return "#2E9E75";
            }
        }

        private static Brush CreateBrush(string colorHex)
        {
            return (Brush)new BrushConverter().ConvertFromString(colorHex);
        }

        private static string GetSeverityText(DiagnosticsSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticsSeverity.Critical:
                    return GetMessage("Sub_entry_DiagnosticsCritical", "Critical");
                case DiagnosticsSeverity.Warning:
                    return GetMessage("Sub_entry_Warning", "Warning");
                default:
                    return GetMessage("Sub_entry_DiagnosticsHealthy", "Healthy");
            }
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }
    }

    public enum DiagnosticsTemperatureSensorType
    {
        Disk = 0,
        Memory = 1,
        Cpu = 2,
        Motherboard = 3
    }

    public class TemperatureSensorStatusItem
    {
        public string Name { get; set; }
        public DiagnosticsTemperatureSensorType SensorType { get; set; }
        public double? TemperatureC { get; set; }
        public DiagnosticsSeverity Severity { get; set; }
        public bool IsAvailable { get; set; }
        public string Details { get; set; }

        public string TemperatureText
        {
            get
            {
                if (!IsAvailable || !TemperatureC.HasValue)
                {
                    return GetMessage("Sub_entry_TemperatureUnavailable", "Sensor not available");
                }

                return string.Format(GetMessage("Sub_entry_TemperatureValueFormat", "{0:0.0} C"), TemperatureC.Value);
            }
        }

        public string SeverityText
        {
            get
            {
                if (!IsAvailable)
                {
                    return GetMessage("Sub_entry_Unknown", "Unknown");
                }

                return GetSeverityText(Severity);
            }
        }

        public Brush SeverityBrush
        {
            get
            {
                if (!IsAvailable)
                {
                    return CreateBrush("#7A8795");
                }

                return CreateBrush(GetSeverityHex());
            }
        }

        private string GetSeverityHex()
        {
            switch (Severity)
            {
                case DiagnosticsSeverity.Critical:
                    return "#D64545";
                case DiagnosticsSeverity.Warning:
                    return "#D9942B";
                default:
                    return "#2E9E75";
            }
        }

        private static Brush CreateBrush(string colorHex)
        {
            return (Brush)new BrushConverter().ConvertFromString(colorHex);
        }

        private static string GetSeverityText(DiagnosticsSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticsSeverity.Critical:
                    return GetMessage("Sub_entry_DiagnosticsCritical", "Critical");
                case DiagnosticsSeverity.Warning:
                    return GetMessage("Sub_entry_Warning", "Warning");
                default:
                    return GetMessage("Sub_entry_DiagnosticsHealthy", "Healthy");
            }
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }
    }

    public class DatabaseArchiveStatusItem
    {
        public string TableName { get; set; }
        public string TimestampColumn { get; set; }
        public double SizeMb { get; set; }
        public long EstimatedRowCount { get; set; }
        public DiagnosticsSeverity Severity { get; set; }
        public bool IsAvailable { get; set; }
        public bool CleanupExecuted { get; set; }
        public int DeletedRowsLastCleanup { get; set; }
        public string LastCleanupDetails { get; set; }
        public DateTime? LastCleanupAt { get; set; }
        public double CleanupStartMb { get; set; }
        public double CleanupTargetMb { get; set; }
        public int RetentionDays { get; set; }
        public int DeleteBatchSize { get; set; }

        public string SizeText
        {
            get
            {
                if (!IsAvailable)
                {
                    return GetMessage("Sub_entry_DatabaseArchiveUnavailable", "Archive status not available");
                }

                return string.Format(GetMessage("Sub_entry_DatabaseArchiveSizeFormat", "{0:0.0} MB"), SizeMb);
            }
        }

        public string SummaryText
        {
            get
            {
                if (!IsAvailable)
                {
                    return GetMessage("Sub_entry_DatabaseArchiveUnavailable", "Archive status not available");
                }

                return string.Format(
                    GetMessage("Sub_entry_DatabaseArchiveSummaryFormat", "Table {0} | approx. {1:N0} row(s)"),
                    TableName,
                    EstimatedRowCount);
            }
        }

        public string PolicyText
        {
            get
            {
                return string.Format(
                    GetMessage("Sub_entry_DatabaseArchivePolicyFormat", "Keep {0} day(s). Cleanup from {1:0.0} MB to {2:0.0} MB. Batch {3:N0} row(s)."),
                    RetentionDays,
                    CleanupStartMb,
                    CleanupTargetMb,
                    DeleteBatchSize);
            }
        }

        public string CleanupText
        {
            get
            {
                if (!CleanupExecuted)
                {
                    return GetMessage("Sub_entry_DatabaseArchiveNoCleanupExecuted", "No database cleanup executed");
                }

                return string.Format(
                    GetMessage("Sub_entry_DatabaseArchiveCleanupResultFormat", "Last DB cleanup {0:dd/MM/yyyy HH:mm:ss}: deleted {1:N0} row(s). {2}"),
                    LastCleanupAt ?? DateTime.Now,
                    DeletedRowsLastCleanup,
                    LastCleanupDetails ?? string.Empty);
            }
        }

        public Brush SeverityBrush
        {
            get
            {
                if (!IsAvailable)
                {
                    return CreateBrush("#7A8795");
                }

                return CreateBrush(GetSeverityHex());
            }
        }

        public string SeverityText
        {
            get
            {
                if (!IsAvailable)
                {
                    return GetMessage("Sub_entry_Unknown", "Unknown");
                }

                return GetSeverityText(Severity);
            }
        }

        private string GetSeverityHex()
        {
            switch (Severity)
            {
                case DiagnosticsSeverity.Critical:
                    return "#D64545";
                case DiagnosticsSeverity.Warning:
                    return "#D9942B";
                default:
                    return "#2E9E75";
            }
        }

        private static Brush CreateBrush(string colorHex)
        {
            return (Brush)new BrushConverter().ConvertFromString(colorHex);
        }

        private static string GetSeverityText(DiagnosticsSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticsSeverity.Critical:
                    return GetMessage("Sub_entry_DiagnosticsCritical", "Critical");
                case DiagnosticsSeverity.Warning:
                    return GetMessage("Sub_entry_Warning", "Warning");
                default:
                    return GetMessage("Sub_entry_DiagnosticsHealthy", "Healthy");
            }
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }
    }
}
