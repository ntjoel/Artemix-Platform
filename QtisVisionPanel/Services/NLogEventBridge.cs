using Newtonsoft.Json;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Services
{
    public static class NLogEventBridge
    {
        [ThreadStatic]
        private static bool _isWriting;

        public static void Write(string loggerName, string level, string logId, string message, string exception, string payloadJson)
        {
            if (_isWriting)
                return;

            // Routine inspection outcomes and expected cancellations belong to the
            // diagnostic file log, not to the operator event history in tbllogevent.
            if (ShouldSkipRoutineEvent(message))
                return;

            try
            {
                _isWriting = true;

                var repository = new EventLogRepository();
                var normalizedLogId = string.IsNullOrWhiteSpace(logId)
                    ? "APP_LOG"
                    : logId.Trim();

                var payload = BuildPayload(loggerName, normalizedLogId, message, exception, payloadJson);
                var entry = new EventLogEntry
                {
                    Application = string.IsNullOrWhiteSpace(loggerName) ? "QtisVisionPanel" : loggerName,
                    Timestamp = DateTime.Now,
                    Level = NormalizeLevel(level),
                    LogId = normalizedLogId,
                    InfoJson = JsonConvert.SerializeObject(payload),
                    ExportType = 3,
                    Payload = payload
                };

                repository.Insert(entry);
            }
            catch
            {
            }
            finally
            {
                _isWriting = false;
            }
        }

        private static bool ShouldSkipRoutineEvent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var value = message.Trim();
            return value.StartsWith("Validation errors:", StringComparison.OrdinalIgnoreCase) ||
                   value.IndexOf(" validation was cancelled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.StartsWith("Processing cancelled for ", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("Display update cancelled", StringComparison.OrdinalIgnoreCase) ||
                   value.IndexOf("non-critical", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static EventLogPayload BuildPayload(string loggerName, string logId, string message, string exception, string payloadJson)
        {
            var payload = EventLogEntry.ParsePayload(payloadJson, message ?? logId, loggerName);

            if (string.IsNullOrWhiteSpace(payload.Category))
            {
                payload.Category = InferCategory(logId, loggerName);
            }

            if (string.IsNullOrWhiteSpace(payload.Message))
            {
                payload.Message = message ?? logId;
            }

            if (string.IsNullOrWhiteSpace(payload.Source))
            {
                payload.Source = loggerName;
            }

            if (!string.IsNullOrWhiteSpace(exception))
            {
                if (string.IsNullOrWhiteSpace(payload.ExceptionType))
                {
                    payload.ExceptionType = "UnhandledException";
                }

                if (string.IsNullOrWhiteSpace(payload.StackTrace))
                {
                    payload.StackTrace = exception;
                }

                if (string.IsNullOrWhiteSpace(payload.Details))
                {
                    payload.Details = exception;
                }
            }

            payload.Metadata = payload.Metadata ?? new Dictionary<string, string>();
            return payload;
        }

        private static string InferCategory(string logId, string loggerName)
        {
            var value = $"{logId} {loggerName}".ToUpperInvariant();

            if (value.Contains("DB") || value.Contains("DATABASE"))
                return "Database";

            if (value.Contains("VISION") || value.Contains("COGNEX"))
                return "VisionPro";

            if (value.Contains("ALARM"))
                return "Alarm";

            if (value.Contains("COMMAND"))
                return "Command";

            if (value.Contains("RECIPE"))
                return "Recipe";

            return "General";
        }

        private static string NormalizeLevel(string level)
        {
            switch ((level ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "TRACE":
                case "DEBUG":
                    return "D";
                case "WARN":
                case "WARNING":
                    return "W";
                case "ERROR":
                    return "E";
                case "FATAL":
                case "CRITICAL":
                    return "C";
                default:
                    return "I";
            }
        }
    }
}
