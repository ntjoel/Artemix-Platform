using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.Advantech;

namespace QtisVisionPanel.Models
{
    public interface IIODeviceManager : IDisposable
    {
        bool IsInitialized { get; }
        bool IsSimulationMode { get; }

        Task<bool> InitializeAsync();
        Task<bool> InitializeDeviceAsync(AdvantechDeviceConfig config);

        /// <summary>
        /// Legge tutti gli ingressi PCIE-1756 in un buffer legacy da 8 byte
        /// (<see cref="Pcie1756ChannelMap.LegacyPortBufferLength"/>): byte 0..3 = porte DI 0..3
        /// (DI0-DI31); byte 4..7 non sono porte hardware e valgono sempre 0.
        /// </summary>
        Task<byte[]> ReadAllInputsAsync();
        Task<bool> ReadInputAsync(int channel);
        Task WriteOutputAsync(int channel, bool value);
        /// <summary>
        /// Genera un impulso su un worker I/O dedicato. La discesa viene schedulata con
        /// Stopwatch monotono e non dipende dal ThreadPool o da Task.Delay.
        /// Il Task termina con <see cref="OutputPulseException"/> se la scheda rifiuta la
        /// scrittura della salita o, dopo i tentativi previsti, della discesa.
        /// </summary>
        Task<OutputPulseResult> PulseOutputAsync(int channel, bool activeElectricalState, bool inactiveElectricalState, int pulseMilliseconds);
        /// <summary>
        /// Scrive tutte le uscite PCIE-1756. Richiede esattamente 8 byte
        /// (<see cref="Pcie1756ChannelMap.LegacyPortBufferLength"/>): byte 0..3 = porte DO 0..3
        /// (DO0-DO31); byte 4..7 sono ignorati e azzerati nella cache delle uscite.
        /// </summary>
        Task WriteOutputsAsync(byte[] values);
        /// <summary>
        /// Restituisce una copia della cache uscite PCIE-1756 in un buffer legacy da 8 byte
        /// (<see cref="Pcie1756ChannelMap.LegacyPortBufferLength"/>): byte 0..3 = porte DO 0..3
        /// (DO0-DO31); byte 4..7 valgono sempre 0.
        /// </summary>
        Task<byte[]> ReadAllOutputsAsync();

        Task<long> ReadEncoderAsync(int channel);
        Task ResetEncoderAsync(int channel);
        Task SetEncoderPresetAsync(int channel, long preset);
        bool SupportsHardwareEncoderCompare { get; }
        /// <summary>
        /// Prepara o arma la tabella compare della PCIE-1884. Con arm=false esegue
        /// soltanto validazione e diagnostica; non modifica l'hardware.
        /// </summary>
        Task ConfigureEncoderCompareTableAsync(int channel, IReadOnlyList<int> targetCounts, bool arm);
        Task StartEncoderAsync(int channel);
        Task StopEncoderAsync(int channel);
        void ConfigureActiveEncoderChannels(IEnumerable<int> activeChannels);
        void ConfigureSimulationEncoder(double speedMmPerSecond, double countsPerMillimeter, bool isRunning);
        Task SimulateEncoderAdvanceAsync(double millimeters);
        Task SetSimulationInputStateAsync(int channel, bool value);
        Task PulseSimulationInputAsync(int channel, int pulseMs);

        Task UpdateAllInputsAsync(ObservableCollection<IOChannel> inputChannels);
        Task UpdateAllEncodersAsync(ObservableCollection<EncoderChannel> encoderChannels);

        void AddInputAction(int inputChannel, Action<IIODeviceManager, bool> action);
        void AddCounterTrigger(int counterChannel, long triggerValue, Action<IIODeviceManager> action);

        event EventHandler<IOEvent> InputChanged;
        /// <summary>
        /// Flusso encoder riservato a tracking e trigger. Viene pubblicato da un worker
        /// dedicato ad alta priorita', separato dagli aggiornamenti UI.
        /// </summary>
        event EventHandler<EncoderEvent> CriticalCounterChanged;
        event EventHandler<EncoderEvent> CounterChanged;
        event EventHandler<string> LogMessage;
    }

    public class IOEvent : EventArgs
    {
        public int Channel { get; set; }
        public bool NewState { get; set; }
        public DateTime Timestamp { get; set; }
        public string DeviceType { get; set; } = "Unknown";
        public long[] EncoderCountsSnapshot { get; set; } = Array.Empty<long>();
        public DateTime EncoderSnapshotTimestamp { get; set; }

        public bool TryGetEncoderCount(int channel, out long encoderCount)
        {
            encoderCount = 0;
            if (channel < 0 || EncoderCountsSnapshot == null || channel >= EncoderCountsSnapshot.Length)
            {
                return false;
            }

            encoderCount = EncoderCountsSnapshot[channel];
            return true;
        }
    }

    public class EncoderEvent : EventArgs
    {
        public int Channel { get; set; }
        public long NewValue { get; set; }
        public long Delta { get; set; }
        public double RawFrequencyHz { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class OutputPulseResult
    {
        public long RaisedStopwatchTicks { get; set; }
        public long LoweredStopwatchTicks { get; set; }
        public double ActualHighMilliseconds =>
            (LoweredStopwatchTicks - RaisedStopwatchTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    public enum OutputPulsePhase
    {
        Rise,
        Fall
    }

    /// <summary>
    /// La scheda non ha accettato uno dei due fronti di un impulso.
    /// Con <see cref="OutputPulsePhase.Rise"/> la camera non ha ricevuto alcun fronte;
    /// con <see cref="OutputPulsePhase.Fall"/> il fronte di salita e' partito ma la linea
    /// potrebbe essere rimasta alta.
    /// </summary>
    public sealed class OutputPulseException : Exception
    {
        public OutputPulseException(int channel, OutputPulsePhase phase, string detail)
            : base($"IO_PULSE_{(phase == OutputPulsePhase.Rise ? "RISE" : "FALL")}_FAILED|channel={channel}|detail={detail}")
        {
            Channel = channel;
            Phase = phase;
            Detail = detail;
        }

        public int Channel { get; }

        public OutputPulsePhase Phase { get; }

        public string Detail { get; }

        public bool RisingEdgeGenerated => Phase == OutputPulsePhase.Fall;
    }
}

