using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Inspector.Models
{
    public sealed class PieceRecordSummary
    {
        public long PieceId { get; set; }
        public long ProductionId { get; set; }
        public DateTime Timestamp { get; set; }
        public string OperatorName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string RecipeName { get; set; } = string.Empty;
        public int? ClassificationOutcome { get; set; }
        public bool IsRejected { get; set; }
        public string PieceDataPath { get; set; } = string.Empty;
        public string DataHostnames { get; set; } = string.Empty;
        public string InspectionStatusDetails { get; set; } = string.Empty;
        public string SourceTable { get; set; } = string.Empty;
        public List<InspectionResultDescriptor> InspectionResults { get; } = new List<InspectionResultDescriptor>();
        public Dictionary<string, string> AdditionalFields { get; } = new Dictionary<string, string>();
    }
}
