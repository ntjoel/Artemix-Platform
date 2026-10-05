using QtisVisionPanel.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public interface IPowerFlex525Client
    {
        Task<PowerFlex525DriveSnapshot> ReadDriveSnapshotAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<PowerFlex525ParameterValue>> ReadParametersAsync(IEnumerable<int> parameterNumbers, CancellationToken cancellationToken);
        Task<bool> WriteParameterAsync(int parameterNumber, double value, CancellationToken cancellationToken);
        Task<IReadOnlyList<PowerFlex525ParameterValue>> ReadModifiedParametersAsync(CancellationToken cancellationToken);
        Task<bool> ResetFaultAsync(CancellationToken cancellationToken);
        Task<bool> ClearFaultHistoryAsync(CancellationToken cancellationToken);
    }
}
