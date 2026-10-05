using System;

namespace QtisVisionPanel.Services
{
    public sealed class MainProjectEventCompatibility
    {
        public string Category { get; set; }

        public string MachineStatus { get; set; }

        public string VisionStatus { get; set; }

        public string LogId { get; set; }
    }

    public static class ToolMainProjectEventMapper
    {
        public static MainProjectEventCompatibility Map(
            string category,
            string eventCode,
            string status,
            string fusionCategory,
            string sourceArea,
            string severity)
        {
            var normalizedCategory = category ?? string.Empty;
            var normalizedCode = (eventCode ?? string.Empty).ToUpperInvariant();
            var normalizedStatus = (status ?? string.Empty).ToUpperInvariant();
            var normalizedFusionCategory = fusionCategory ?? string.Empty;
            var normalizedSourceArea = sourceArea ?? string.Empty;
            var normalizedSeverity = (severity ?? string.Empty).ToUpperInvariant();

            var mainCategory = ResolveMainProjectCategory(normalizedCategory, normalizedCode, normalizedFusionCategory, normalizedSourceArea, normalizedSeverity);
            var machineStatus = ResolveMainProjectMachineStatus(normalizedCategory, normalizedCode, normalizedStatus, normalizedSourceArea, normalizedSeverity);
            var visionStatus = ResolveMainProjectVisionStatus(normalizedCode, normalizedStatus, normalizedSourceArea, normalizedSeverity);
            var logId = ResolveMainProjectLogId(normalizedCode, mainCategory, machineStatus, normalizedSeverity);

            return new MainProjectEventCompatibility
            {
                Category = mainCategory,
                MachineStatus = machineStatus,
                VisionStatus = visionStatus,
                LogId = logId
            };
        }

        private static string ResolveMainProjectCategory(string category, string eventCode, string fusionCategory, string sourceArea, string severity)
        {
            if (string.Equals(severity, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fusionCategory, "Alarm", StringComparison.OrdinalIgnoreCase) ||
                eventCode.Contains("FAILED") ||
                eventCode.Contains("ALARM"))
            {
                return "Alarm";
            }

            if (string.Equals(sourceArea, "Trigger", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "Reject", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fusionCategory, "Command", StringComparison.OrdinalIgnoreCase))
            {
                return "Command";
            }

            if (string.Equals(category, "Connection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "Connection", StringComparison.OrdinalIgnoreCase) ||
                eventCode.Contains("MODE_") ||
                eventCode.Contains("INIT_"))
            {
                return "Machine";
            }

            if (eventCode.Contains("EXCEPTION"))
            {
                return "Exception";
            }

            return "Machine";
        }

        private static string ResolveMainProjectMachineStatus(string category, string eventCode, string status, string sourceArea, string severity)
        {
            if (string.Equals(severity, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                eventCode.Contains("FAILED") ||
                status.Contains("ERROR") ||
                status.Contains("FAULT"))
            {
                return "Error";
            }

            if (status.Contains("MAINTENANCE") ||
                status.Contains("SERVICE") ||
                status.Contains("MANUAL"))
            {
                return "Maintenance";
            }

            if (string.Equals(category, "Connection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "Connection", StringComparison.OrdinalIgnoreCase) ||
                eventCode.Contains("INIT_") ||
                eventCode.Contains("CONNECT"))
            {
                return "Connecting";
            }

            if (string.Equals(sourceArea, "Trigger", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "Reject", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "Tracking", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "ProductTracking", StringComparison.OrdinalIgnoreCase))
            {
                return "Running";
            }

            return "Stopped";
        }

        private static string ResolveMainProjectVisionStatus(string eventCode, string status, string sourceArea, string severity)
        {
            if (string.Equals(severity, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                eventCode.Contains("TRIGGER_FAILED"))
            {
                return "Stopped";
            }

            if (string.Equals(sourceArea, "Trigger", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "Tracking", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceArea, "ProductTracking", StringComparison.OrdinalIgnoreCase) ||
                eventCode.Contains("TRIGGER") ||
                eventCode.Contains("RESULT"))
            {
                return "Running";
            }

            return "Stopped";
        }

        private static string ResolveMainProjectLogId(string eventCode, string category, string machineStatus, string severity)
        {
            if (string.Equals(category, "Alarm", StringComparison.OrdinalIgnoreCase))
            {
                return "ALARM_TRIGGERED";
            }

            if (string.Equals(category, "Machine", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(machineStatus, "Connecting", StringComparison.OrdinalIgnoreCase) ||
                 eventCode.Contains("INIT_") ||
                 eventCode.Contains("MODE_")))
            {
                return "MACHINE_STATUS_CHANGED";
            }

            if (string.Equals(category, "Command", StringComparison.OrdinalIgnoreCase))
            {
                return $"TOOL_{NormalizeLogId(eventCode)}";
            }

            if (string.Equals(severity, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return "TOOL_RUNTIME_ERROR";
            }

            return $"TOOL_{NormalizeLogId(eventCode)}";
        }

        private static string NormalizeLogId(string eventCode)
        {
            var normalized = string.IsNullOrWhiteSpace(eventCode) ? "EVENT" : eventCode.Trim().ToUpperInvariant().Replace(" ", "_");
            return normalized;
        }
    }
}
