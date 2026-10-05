using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public interface IDisposableService : IDisposable
    {
        Task ShutdownAsync();
        bool IsShuttingDown { get; }
    }
}
