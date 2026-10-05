using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Models
{
    public class ToolFusionSnapshot
    {
        public string SnapshotVersion { get; set; } = "1.0";

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public ToolFusionConfigReference ConfigReference { get; set; } = new ToolFusionConfigReference();

        public ToolFusionRuntimeSummary RuntimeSummary { get; set; } = new ToolFusionRuntimeSummary();

        public List<ToolFusionRuntimeEventRecord> RuntimeEvents { get; set; } = new List<ToolFusionRuntimeEventRecord>();
    }

    public class ToolFusionConfigReference
    {
        public string ActiveConfigurationPath { get; set; }

        public string TemplateConfigurationPath { get; set; }

        public string BackupFolderPath { get; set; }

        public string ExportFolderPath { get; set; }

        public string ConfigurationName { get; set; }

        public string ConfigurationVersion { get; set; }
    }

    public class ToolFusionRuntimeSummary
    {
        public string ActiveConnectionMode { get; set; }

        public string MachineControllerStatus { get; set; }

        public string TriggerMode { get; set; }

        public string RejectMode { get; set; }

        public string MainEncoderAxisCode { get; set; }

        public string MainProjectCompatibleMachineStatus { get; set; }

        public int ActiveTrackedProducts { get; set; }

        public int RuntimeEventCount { get; set; }
    }

    public class ToolFusionRuntimeEventRecord
    {
        public DateTime TimestampUtc { get; set; }

        public string EventCode { get; set; }

        public string Category { get; set; }

        public string Status { get; set; }

        public long? ProductId { get; set; }

        public string SignalCode { get; set; }

        public double? MachineQuotaMm { get; set; }

        public string Detail { get; set; }

        public string SourceArea { get; set; }

        public string Severity { get; set; }

        public string MainProjectCategory { get; set; }

        public string MainProjectMachineStatus { get; set; }

        public string MainProjectVisionStatus { get; set; }

        public string MainProjectLogId { get; set; }
    }
}
