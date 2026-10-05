namespace QtisVisionPanel.Inspector.Models
{
    public sealed class PieceImageArtifact
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string ViewCode { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public bool Exists { get; set; }
    }
}
