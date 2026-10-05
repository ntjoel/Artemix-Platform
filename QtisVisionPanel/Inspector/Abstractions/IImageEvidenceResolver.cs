using System.Threading;
using System.Threading.Tasks;
using QtisVisionPanel.Inspector.Models;

namespace QtisVisionPanel.Inspector.Abstractions
{
    public interface IImageEvidenceResolver
    {
        Task<PieceEvidenceBundle> ResolveAsync(PieceRecordSummary piece, CancellationToken cancellationToken = default(CancellationToken));
    }
}
