using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Inspector.Abstractions
{
    public interface IDataSourceHealthProvider
    {
        Task<bool> CheckConnectionAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}
