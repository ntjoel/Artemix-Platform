using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Inspector.Models
{
    public sealed class PieceHistoryQuery
    {
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public bool OnlyRejectedPieces { get; set; }
        public bool RequireSavedImages { get; set; }
        public int MaxResults { get; set; } = 100;
        public IReadOnlyList<string> SelectedInspectionCodes { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> SelectedVisibleFields { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> SelectedLines { get; set; } = Array.Empty<string>();
    }
}
