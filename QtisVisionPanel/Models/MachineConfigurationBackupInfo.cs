using System;

namespace QtisVisionPanel.Models
{
    public class MachineConfigurationBackupInfo
    {
        public string FilePath { get; set; }

        public string FileName { get; set; }

        public DateTime CreatedLocalTime { get; set; }

        public long SizeBytes { get; set; }

        public string ConfigurationName { get; set; }

        public string ConfigurationVersion { get; set; }

        public DateTime? ConfigurationUpdatedUtc { get; set; }

        public string DisplayTitle => string.IsNullOrWhiteSpace(ConfigurationName)
            ? FileName
            : $"{ConfigurationName} ({ConfigurationVersion})";

        public string DisplayTimestamp => CreatedLocalTime.ToString("dd/MM/yyyy HH:mm:ss");

        public string DisplaySize => $"{SizeBytes / 1024.0:0.0} KB";

        public string DisplaySubtitle => ConfigurationUpdatedUtc.HasValue
            ? $"Runtime updated: {ConfigurationUpdatedUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}"
            : "Runtime timestamp not available";
    }
}
