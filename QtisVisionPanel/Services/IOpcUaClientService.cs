using QtisVisionPanel.Cls_Config.Calss_structure;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    public interface IOpcUaClientService : IDisposable
    {
        event EventHandler<OpcUaCommandEventArgs> OpcStartRequested;
        event EventHandler<OpcUaCommandEventArgs> OpcStopRequested;
        event EventHandler<OpcUaCommandEventArgs> OpcRecipeChangeRequested;

        bool IsEnabled { get; }
        bool IsConnected { get; }
        bool IsRequiredForMachineRun { get; }
        string LastStatusMessage { get; }
        OpcUaConfig CurrentConfig { get; }

        Task InitializeAsync(CancellationToken cancellationToken);
        Task ConnectAsync(CancellationToken cancellationToken);
        Task DisconnectAsync();
        bool CanMachineRun(out string reason);
        Task WriteCountersAsync(long total, long good, long bad);
        Task WriteLastResultAsync(bool isGood);
        Task WriteIsRunningAsync(bool isRunning);
        Task WriteCurrentRecipeAsync(string recipeName);
        Task WriteCurrentRecipeIdAsync(long recipeId);
        Task WriteHeartbeatAsync();
        Task WriteInspectionSnapshotAsync(bool isGood, long total, long good, long bad, string recipeName, long recipeId);
        Task WriteHealthSnapshotAsync(string systemHealthStatus, string lastAlarmCode, string activeHoldReasons);
        Task WriteEarlyWarningAsync(string severity, string code, string message, double? etaValue, string etaUnit, int count);
        Task WriteCommandAcknowledgementAsync(long commandId, bool ok, string message);
    }

    public sealed class OpcUaCommandEventArgs : EventArgs
    {
        public OpcUaCommandEventArgs(long commandId, string commandName, string value)
        {
            CommandId = commandId;
            CommandName = commandName;
            Value = value;
        }

        public long CommandId { get; }
        public string CommandName { get; }
        public string Value { get; }
    }
}
