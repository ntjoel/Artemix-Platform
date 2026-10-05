using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace QtisVisionPanel.Models
{
    public class EventLogPayload
    {
        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("details")]
        public string Details { get; set; }

        [JsonProperty("machine_status")]
        public string MachineStatus { get; set; }

        [JsonProperty("database_status")]
        public string DatabaseStatus { get; set; }

        [JsonProperty("visionpro_status")]
        public string VisionProStatus { get; set; }

        [JsonProperty("user")]
        public string User { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("recipe")]
        public string Recipe { get; set; }

        [JsonProperty("exception_type")]
        public string ExceptionType { get; set; }

        [JsonProperty("stack_trace")]
        public string StackTrace { get; set; }

        [JsonProperty("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public class EventLogEntry
    {
        public long Id { get; set; }
        public string Application { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string LogId { get; set; }
        public string InfoJson { get; set; }
        public int? ExportType { get; set; }
        public EventLogPayload Payload { get; set; }

        public string Category => !string.IsNullOrWhiteSpace(Payload?.Category) ? Payload.Category : "General";
        public string Message => !string.IsNullOrWhiteSpace(Payload?.Message) ? Payload.Message : LogId;
        public string Source => !string.IsNullOrWhiteSpace(Payload?.Source) ? Payload.Source : Application;
        public string Details => Payload?.Details;
        public string MachineStatus => Payload?.MachineStatus;
        public string DatabaseStatus => Payload?.DatabaseStatus;
        public string VisionProStatus => Payload?.VisionProStatus;
        public string User => Payload?.User;
        public string Role => Payload?.Role;
        public string Recipe => Payload?.Recipe;
        public string ExceptionType => Payload?.ExceptionType;
        public string StackTrace => Payload?.StackTrace;

        public string LevelText
        {
            get
            {
                switch ((Level ?? string.Empty).Trim().ToUpperInvariant())
                {
                    case "D":
                        return "Debug";
                    case "W":
                        return "Warning";
                    case "E":
                        return "Error";
                    case "C":
                        return "Critical";
                    default:
                        return "Info";
                }
            }
        }

        public Brush LevelBrush => CreateBrush(GetLevelColorHex());

        public string FullDetails
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(Details))
                    parts.Add(Details);

                if (!string.IsNullOrWhiteSpace(ExceptionType))
                    parts.Add($"Exception: {ExceptionType}");

                if (!string.IsNullOrWhiteSpace(StackTrace))
                    parts.Add(StackTrace);

                return string.Join(Environment.NewLine + Environment.NewLine, parts);
            }
        }

        public string PrettyInfoJson
        {
            get
            {
                if (string.IsNullOrWhiteSpace(InfoJson))
                    return string.Empty;

                try
                {
                    return JToken.Parse(InfoJson).ToString(Formatting.Indented);
                }
                catch
                {
                    return InfoJson;
                }
            }
        }

        public static EventLogPayload ParsePayload(string infoJson, string fallbackMessage = null, string fallbackSource = null)
        {
            if (string.IsNullOrWhiteSpace(infoJson))
            {
                return CreateFallbackPayload(fallbackMessage, fallbackSource);
            }

            try
            {
                var trimmed = infoJson.Trim();

                if (trimmed.StartsWith("{"))
                {
                    var payload = JsonConvert.DeserializeObject<EventLogPayload>(trimmed);
                    if (payload != null)
                    {
                        if (string.IsNullOrWhiteSpace(payload.Message))
                            payload.Message = fallbackMessage;

                        if (string.IsNullOrWhiteSpace(payload.Source))
                            payload.Source = fallbackSource;

                        payload.Metadata = payload.Metadata ?? new Dictionary<string, string>();
                        return payload;
                    }
                }

                if (trimmed.StartsWith("\""))
                {
                    var message = JsonConvert.DeserializeObject<string>(trimmed);
                    return CreateFallbackPayload(message ?? fallbackMessage, fallbackSource);
                }
            }
            catch (Exception ex)
            {
                // Payload 'info' non parsabile: fallback al testo grezzo, ma non in silenzio.
                System.Diagnostics.Debug.WriteLine($"EventLogEntry: parse JSON del campo info fallito — {ex.Message}");
            }

            return CreateFallbackPayload(infoJson, fallbackSource);
        }

        private static EventLogPayload CreateFallbackPayload(string message, string source)
        {
            return new EventLogPayload
            {
                Category = "General",
                Message = message,
                Source = source,
                Metadata = new Dictionary<string, string>()
            };
        }

        private string GetLevelColorHex()
        {
            switch ((Level ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "D":
                    return "#6A7F95";
                case "W":
                    return "#D9942B";
                case "E":
                    return "#D94B4B";
                case "C":
                    return "#7A1D1D";
                default:
                    return "#2C79B8";
            }
        }

        private static Brush CreateBrush(string colorHex)
        {
            return (Brush)new BrushConverter().ConvertFromString(colorHex);
        }
    }
}
