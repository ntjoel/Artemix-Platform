using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Monitora periodicamente le uscite digitali fisiche e rileva uscite bloccate HIGH.
    /// Un'uscita rimasta HIGH oltre STUCK_THRESHOLD_SECONDS quando l'applicazione
    /// non ha richiesto tale stato indica un guasto hardware (solenoide bloccato,
    /// cortocircuito) e viene forzatamente abbassata.
    /// </summary>
    public sealed class OutputWatchdogService : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private const int POLL_INTERVAL_SECONDS = 10;
        private const int STUCK_THRESHOLD_SECONDS = 60;

        private readonly IIODeviceManager _io;
        private readonly Timer _timer;
        private readonly ConcurrentDictionary<int, OutputEntry> _registry = new ConcurrentDictionary<int, OutputEntry>();
        private bool _disposed;

        private class OutputEntry
        {
            public bool ExpectedHigh { get; set; }
            public DateTime LastChangedAt { get; set; } = DateTime.Now;
        }

        public OutputWatchdogService(IIODeviceManager io)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
            _timer = new Timer(OnPollTick, null,
                TimeSpan.FromSeconds(POLL_INTERVAL_SECONDS),
                TimeSpan.FromSeconds(POLL_INTERVAL_SECONDS));
        }

        /// <summary>
        /// Registra un canale di uscita nel watchdog.
        /// Chiamare ogni volta che si scrive un output (HIGH o LOW).
        /// </summary>
        public void Track(int channel, bool expectedHigh)
        {
            _registry.AddOrUpdate(channel,
                _ => new OutputEntry { ExpectedHigh = expectedHigh, LastChangedAt = DateTime.Now },
                (_, entry) =>
                {
                    if (entry.ExpectedHigh != expectedHigh)
                        entry.LastChangedAt = DateTime.Now;
                    entry.ExpectedHigh = expectedHigh;
                    return entry;
                });
        }

        private async void OnPollTick(object state)
        {
            if (_disposed || _io == null) return;

            foreach (var kv in _registry)
            {
                int channel = kv.Key;
                OutputEntry entry = kv.Value;
                if (!entry.ExpectedHigh) continue;

                double secondsHigh = (DateTime.Now - entry.LastChangedAt).TotalSeconds;
                if (secondsHigh < STUCK_THRESHOLD_SECONDS) continue;

                // Uscita HIGH da troppo tempo senza reset — possibile guasto hardware
                _log.Error("IO_OUTPUT_STUCK_HIGH|channel=DO{0}|duration={1:F0}s — forcing reset", channel, secondsHigh);

                try
                {
                    await _io.WriteOutputAsync(channel, false);
                    entry.ExpectedHigh = false;
                    entry.LastChangedAt = DateTime.Now;
                    _log.Warn("IO_OUTPUT_FORCED_RESET|channel=DO{0}", channel);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "IO_OUTPUT_FORCED_RESET_FAILED|channel=DO{0}", channel);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
        }
    }
}
