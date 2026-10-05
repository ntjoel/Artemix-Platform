using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QtisVisionPanel.Inspector.Abstractions;
using QtisVisionPanel.Inspector.Models;

namespace QtisVisionPanel.Inspector.Services
{
    public sealed class LocalPieceEvidenceResolver : IImageEvidenceResolver
    {
        public Task<PieceEvidenceBundle> ResolveAsync(PieceRecordSummary piece, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (piece == null)
                throw new ArgumentNullException(nameof(piece));

            string resolvedPieceFolder = NormalizePieceFolderPath(piece.PieceDataPath);
            var bundle = new PieceEvidenceBundle
            {
                PieceKey = piece.PieceId.ToString(),
                PieceFolderPath = resolvedPieceFolder
            };

            if (string.IsNullOrWhiteSpace(bundle.PieceFolderPath) || !Directory.Exists(bundle.PieceFolderPath))
            {
                bundle.FolderExists = false;
                bundle.ResolutionNotes = "Piece folder is missing or not available.";
                return Task.FromResult(bundle);
            }

            bundle.FolderExists = true;

            foreach (var path in Directory.EnumerateFiles(bundle.PieceFolderPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(path);
                var artifact = new PieceImageArtifact
                {
                    FileName = fileName,
                    FullPath = path,
                    Exists = true,
                    ViewCode = TryExtractViewCode(fileName),
                    Variant = ClassifyVariant(fileName)
                };

                switch (artifact.Variant)
                {
                    case "Source":
                        bundle.SourceImages.Add(artifact);
                        break;
                    case "Processed":
                        bundle.ProcessedImages.Add(artifact);
                        break;
                    default:
                        bundle.OtherImages.Add(artifact);
                        break;
                }
            }

            bundle.HasAnyImage = bundle.SourceImages.Count > 0 || bundle.ProcessedImages.Count > 0 || bundle.OtherImages.Count > 0;
            bundle.ResolutionNotes = bundle.HasAnyImage
                ? "Local piece folder resolved successfully."
                : "Piece folder exists but no images were found.";

            return Task.FromResult(bundle);
        }

        private static string NormalizePieceFolderPath(string pieceDataPath)
        {
            if (string.IsNullOrWhiteSpace(pieceDataPath))
                return string.Empty;

            string normalized = pieceDataPath.Trim().Trim('"');
            if (Directory.Exists(normalized))
                return normalized;

            if (File.Exists(normalized))
                return Path.GetDirectoryName(normalized) ?? string.Empty;

            return normalized;
        }

        private static string ClassifyVariant(string fileName)
        {
            if (fileName.IndexOf("_A.", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Source";

            if (fileName.IndexOf("_Z.", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Processed";

            return "Other";
        }

        private static string TryExtractViewCode(string fileName)
        {
            string[] parts = fileName.Split('_');
            return parts.Length >= 3 ? parts[2] : string.Empty;
        }
    }
}
