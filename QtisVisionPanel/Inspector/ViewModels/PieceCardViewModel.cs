using QtisVisionPanel.Inspector.Models;
using QtisVisionPanel.ServerMessage;
using System.Windows.Media;

namespace QtisVisionPanel.Inspector.ViewModels
{
    public sealed class PieceCardViewModel
    {
        public PieceCardViewModel(PieceRecordSummary piece)
        {
            Piece = piece;
            Title = L("Sub_entry_DataInspectorPieceLabel", "Piece") + " " + piece.PieceId;
            Subtitle = piece.ProductName + " | " + piece.RecipeName;
            DetailLine = piece.InspectionStatusDetails;
            bool isReportedNoGood = !piece.IsRejected && piece.ClassificationOutcome == 0;
            bool isUnclassified = piece.ClassificationOutcome == 3;
            StatusText = isUnclassified
                ? L("Sub_entry_Unclassified", "Non classificato")
                : piece.IsRejected
                ? L("Sub_entry_DataInspectorRejected", "Rejected")
                : isReportedNoGood
                    ? L("Sub_entry_DataInspectorReported", "Reported")
                    : L("Sub_entry_DataInspectorAccepted", "Accepted");
            StatusBrush = new SolidColorBrush(
                isUnclassified
                    ? Color.FromRgb(190, 125, 0)
                    : piece.IsRejected
                    ? Color.FromRgb(206, 64, 64)
                    : isReportedNoGood
                        ? Color.FromRgb(190, 125, 0)
                        : Color.FromRgb(40, 148, 92));
            TimestampText = L("Sub_entry_DataInspectorCheckedOn", "Checked on:") + " " + piece.Timestamp.ToString("G");
            InspectionSummary = L("Sub_entry_DataInspectorResultNote", "Result note:") + " " + piece.InspectionStatusDetails;
            OperatorText = string.IsNullOrWhiteSpace(piece.OperatorName) ? L("Sub_entry_DataInspectorOperatorUnavailable", "Operator not available") : piece.OperatorName;
            ProductionText = L("Sub_entry_DataInspectorProductionBatch", "Production batch:") + " " + piece.ProductionId;
            PieceFolderPath = L("Sub_entry_DataInspectorSavedFolder", "Saved folder:") + " " + (string.IsNullOrWhiteSpace(piece.PieceDataPath) ? L("Sub_entry_DataInspectorNotAvailable", "not available") : piece.PieceDataPath);
            TraceabilityNote = L("Sub_entry_DataInspectorStorageSource", "Storage source:") + " " + (string.IsNullOrWhiteSpace(piece.DataHostnames) ? L("Sub_entry_DataInspectorNotAvailable", "not available") : piece.DataHostnames);
            piece.AdditionalFields.TryGetValue("Top3DProfileSummary", out string topThreeDProfileSummary);
            HasTopThreeDProfileDetails = !string.IsNullOrWhiteSpace(topThreeDProfileSummary);
            TopThreeDProfileDetails = HasTopThreeDProfileDetails
                ? L("Sub_entry_DataInspectorTop3DProfileDetails", "Top3D profile details") + ": " + topThreeDProfileSummary
                : string.Empty;
        }

        public PieceRecordSummary Piece { get; private set; }
        public string Title { get; private set; }
        public string Subtitle { get; private set; }
        public string DetailLine { get; private set; }
        public string StatusText { get; private set; }
        public Brush StatusBrush { get; private set; }
        public string RecipeName { get { return Piece.RecipeName; } }
        public string TimestampText { get; private set; }
        public string InspectionSummary { get; private set; }
        public string OperatorText { get; private set; }
        public string ProductionText { get; private set; }
        public string PieceFolderPath { get; private set; }
        public string TraceabilityNote { get; private set; }
        public bool HasTopThreeDProfileDetails { get; private set; }
        public string TopThreeDProfileDetails { get; private set; }

        public void UpdateTraceability(PieceEvidenceBundle bundle)
        {
            if (bundle == null)
                return;

            PieceFolderPath = L("Sub_entry_DataInspectorSavedFolder", "Saved folder:") + " " + (string.IsNullOrWhiteSpace(bundle.PieceFolderPath) ? L("Sub_entry_DataInspectorNotAvailable", "not available") : bundle.PieceFolderPath);
            TraceabilityNote = L("Sub_entry_DataInspectorImageCheck", "Image check:") + " " + bundle.ResolutionNotes;
        }

        private static string L(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }
    }
}
