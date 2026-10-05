using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QtisVisionPanel.Inspector.Models;

namespace QtisVisionPanel.Inspector.Abstractions
{
    public interface IPieceHistoryRepository
    {
        Task<IReadOnlyList<PieceRecordSummary>> GetRecentRejectedPiecesAsync(int count, CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyList<PieceRecordSummary>> QueryPiecesAsync(PieceHistoryQuery query, CancellationToken cancellationToken = default(CancellationToken));
    }
}
