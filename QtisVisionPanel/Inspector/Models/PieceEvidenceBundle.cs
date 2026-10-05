using System.Collections.Generic;

namespace QtisVisionPanel.Inspector.Models
{
    public sealed class PieceEvidenceBundle
    {
        public string PieceKey { get; set; } = string.Empty;
        public string PieceFolderPath { get; set; } = string.Empty;
        public bool FolderExists { get; set; }
        public bool HasAnyImage { get; set; }
        public string ResolutionNotes { get; set; } = string.Empty;
        public List<PieceImageArtifact> SourceImages { get; } = new List<PieceImageArtifact>();
        public List<PieceImageArtifact> ProcessedImages { get; } = new List<PieceImageArtifact>();
        public List<PieceImageArtifact> OtherImages { get; } = new List<PieceImageArtifact>();
    }
}
