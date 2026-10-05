using Automation.BDaq;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Serialization;
using System.Reflection;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.Advantech;
using Action = QtisVisionPanel.Models.Advantech.Action;
using System.Collections.Generic;

namespace QtisVisionPanel.Models
{
    public class AdvantechDeviceManager : IIODeviceManager
    {
        // Strutture per BDaq
        private DeviceInformation _di1756;      // PCIE-1756-BE
        private DeviceInformation _di1884;      // PCIE-1884-AE
        private InstantDiCtrl _diCtrl1756;      // Digital Input Control (1756)
        private InstantDoCtrl _doCtrl1756;      // Digital Output Control (1756)
        private InstantDiCtrl _diCtrl1884;      // Digital Input Control (1884)
        private InstantDoCtrl _doCtrl1884;      // Digital Output Control (1884)
        private UdCounterCtrl _udCounterCtrl;   // Up/Down Counter Control (1884) - per encoder
        private FreqMeterCtrl _freqMeterCtrl;   // Frequency meter control (1884) - diagnostica raw encoder
        private TimerPulseCtrl _timerPulseCtrl; // Timer Pulse Control (1884)

        // Ultimo canale selezionato sui controlli BDaq. I setter Channel/ChannelStart
        // marshallano nel driver e possono riconfigurare il dispositivo: con un solo
        // canale encoder attivo venivano eseguiti ~500 volte al secondo dal thread di
        // polling senza alcun effetto utile. Riassegniamo solo a canale realmente
        // cambiato. Protetti da _lock come tutti gli accessi ai controlli encoder.
        private int _selectedCounterChannel = -1;
        private int _selectedFreqMeterChannel = -1;

        // Configurazioni
        private IOMappingConfig _mappingConfig;

        // Stato e simulazione
        private bool _isInitialized = false;
        private bool _isSimulationMode = false;
        private volatile bool _isDisposing = false;
        private readonly object _lock = new object();
        private readonly object _outputWriteLock = new object();
        private readonly bool _forceSimulationMode;
        private CancellationTokenSource _pollingCts;
        private Task _pollingTask;

        // Il polling hardware non invoca piu' callback applicative. Due ring buffer fissi
        // separano il percorso critico tracking/trigger dalla telemetria UI senza allocare
        // nodi di coda ad ogni campione encoder.
        private const int EncoderDispatchCapacity = 1024;
        private readonly EncoderDispatchSample[] _criticalEncoderQueue = new EncoderDispatchSample[EncoderDispatchCapacity];
        private readonly EncoderDispatchSample[] _telemetryEncoderQueue = new EncoderDispatchSample[EncoderDispatchCapacity];
        private readonly object _criticalEncoderQueueLock = new object();
        private readonly object _telemetryEncoderQueueLock = new object();
        private readonly AutoResetEvent _criticalEncoderSignal = new AutoResetEvent(false);
        private readonly AutoResetEvent _telemetryEncoderSignal = new AutoResetEvent(false);
        private int _criticalEncoderHead, _criticalEncoderTail, _criticalEncoderCount;
        private int _telemetryEncoderHead, _telemetryEncoderTail, _telemetryEncoderCount;
        private Task _criticalEncoderDispatchTask;
        private Task _telemetryEncoderDispatchTask;
        private long _criticalEncoderDropped;
        private long _telemetryEncoderDropped;

        // Un solo worker possiede la temporizzazione degli impulsi. La coda e' un array
        // preallocato; il thread non dipende da ThreadPool e Dispatcher WPF, ma resta un
        // thread gestito: una GC bloccante lo sospende e sposta i fronti (collaudo 2026-09-24).
        private const int OutputPulseQueueCapacity = 128;
        // Tentativi immediati di scrittura della discesa prima di dichiarare l'impulso fallito.
        private const int OutputPulseFallAttempts = 3;
        private readonly OutputPulseRequest[] _outputPulseQueue = new OutputPulseRequest[OutputPulseQueueCapacity];
        private readonly object _outputPulseQueueLock = new object();
        private readonly AutoResetEvent _outputPulseSignal = new AutoResetEvent(false);
        private int _outputPulseHead, _outputPulseTail, _outputPulseCount;
        private Task _outputPulseTask;

        // Buffer per PCIE-1756: 4 porte DI + 4 porte DO da 8 bit (32 DI + 32 DO).
        // I buffer restano da 8 byte (Pcie1756ChannelMap.LegacyPortBufferLength) per il
        // contratto byte[] di IIODeviceManager e per io_simulation_state.xml: solo i
        // byte 0..3 sono porte hardware, i byte 4..7 restano sempre a 0.
        private byte[] _lastInputs1756 = new byte[Pcie1756ChannelMap.LegacyPortBufferLength];
        private byte[] _currentOutputs1756 = new byte[Pcie1756ChannelMap.LegacyPortBufferLength];

        // Buffer per PCIE-1884 (4 bit per porta, 1 porta)
        private byte _lastInputs1884 = 0x00;
        private byte _currentOutputs1884 = 0x00;

        // Buffer encoder (4 contatori a 32 bit)
        private long[] _encoderValues = new long[4];
        private readonly double[] _encoderFrequencyHz = new double[4];
        private readonly int[] _lastEncoderRawSamples = new int[4];
        private readonly bool[] _encoderReadAsDeltaMode = new bool[4];
        private readonly bool[] _activeEncoderChannels = { true, false, false, false };
        private readonly bool[] _encoderSamplingStarted = new bool[4];
        private readonly DateTime[] _lastFrequencyReadUtc = new DateTime[4];
        private readonly DateTime[] _lastFrequencyBusyLogUtc = new DateTime[4];
        private readonly long[] _lastSoftwareFrequencySample = new long[4];
        private readonly long[] _lastSoftwareFrequencyCount = new long[4];
        private const int EncoderFrequencyMinimumReadIntervalMs = 250;
        private const int EncoderFrequencyBusyLogThrottleMs = 5000;
        private const int EncoderSoftwareFrequencySampleIntervalMs = 50;

        // Stato simulazione
        private SimulationState _simState = new SimulationState();
        private string _simulationFilePath;
        private double _simulationEncoderSpeedMmPerSecond = 300.0;
        private double _simulationCountsPerMillimeter = 20.48;
        private bool _simulationEncoderRunning = true;
        private DateTime _simulationEncoderLastUpdateUtc = DateTime.UtcNow;
        private DateTime _lastSimulationStateSaveUtc = DateTime.MinValue;
        private DateTime _lastPollingOverrunLogUtc = DateTime.MinValue;

        // Logica personalizzata
        private ConcurrentDictionary<int, System.Action<IIODeviceManager, bool>> _inputActions = new ConcurrentDictionary<int, System.Action<IIODeviceManager, bool>>();
        private ConcurrentDictionary<int, ConcurrentDictionary<long, System.Action<IIODeviceManager>>> _counterTriggers = new ConcurrentDictionary<int, ConcurrentDictionary<long, System.Action<IIODeviceManager>>>();

        // Eventi
        public event EventHandler<IOEvent> InputChanged;
        public event EventHandler<EncoderEvent> CriticalCounterChanged;
        public event EventHandler<EncoderEvent> CounterChanged;
        public event EventHandler<string> LogMessage;

        // Logger
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public bool IsInitialized => _isInitialized;
        public bool IsSimulationMode => _isSimulationMode;
        public bool SupportsHardwareEncoderCompare => !_isSimulationMode && _udCounterCtrl?.Initialized == true;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint periodMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint periodMilliseconds);

        private static bool? _bioDaqAvailable;

        public static bool IsBioDaqNativeAvailable()
        {
            if (_bioDaqAvailable.HasValue)
                return _bioDaqAvailable.Value;
            try
            {
                IntPtr hModule = LoadLibrary("biodaq.dll");
                if (hModule == IntPtr.Zero)
                {
                    _bioDaqAvailable = false;
                    return false;
                }
                FreeLibrary(hModule);
                _bioDaqAvailable = true;
                return true;
            }
            catch
            {
                _bioDaqAvailable = false;
                return false;
            }
        }

        public AdvantechDeviceManager(bool forceSimulationMode = false)
        {
            bool daqAbsent = !IsBioDaqNativeAvailable();
            if (daqAbsent)
                _logger.Warn("biodaq.dll non trovata: DAQNavi non installato. Modalita simulazione forzata.");

            _forceSimulationMode = forceSimulationMode || daqAbsent;
            _simulationFilePath = Path.Combine(
                MainWindow.configManager.Config.Configuration.Recipe_Folder,
                "cfg",
                "io_simulation_state.xml");

            _mappingConfig = LoadIOMappingConfig();
            StartDeterministicWorkers();
        }

        private struct EncoderDispatchSample
        {
            public int Channel;
            public long NewValue;
            public long Delta;
            public double RawFrequencyHz;
            public DateTime Timestamp;
        }

        private sealed class OutputPulseRequest
        {
            public int Channel;
            public bool ActiveState;
            public bool InactiveState;
            public int PulseMilliseconds;
            public TaskCompletionSource<OutputPulseResult> Completion;
        }

        private void StartDeterministicWorkers()
        {
            _criticalEncoderDispatchTask = Task.Factory.StartNew(
                () => EncoderDispatchLoop(true), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _telemetryEncoderDispatchTask = Task.Factory.StartNew(
                () => EncoderDispatchLoop(false), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _outputPulseTask = Task.Factory.StartNew(
                OutputPulseLoop, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _logger.Info(
                $"IO_DETERMINISTIC_WORKERS_STARTED|encoderCriticalCapacity={EncoderDispatchCapacity}" +
                $"|encoderTelemetryCapacity={EncoderDispatchCapacity}|pulseCapacity={OutputPulseQueueCapacity}" +
                "|criticalPriority=Highest|telemetryPriority=BelowNormal|pulsePriority=Highest");
        }

        private void EnqueueEncoderSample(int channel, long newValue, long delta, double frequency, DateTime timestamp)
        {
            var sample = new EncoderDispatchSample
            {
                Channel = channel,
                NewValue = newValue,
                Delta = delta,
                RawFrequencyHz = frequency,
                Timestamp = timestamp
            };
            EnqueueEncoderSample(_criticalEncoderQueue, _criticalEncoderQueueLock, ref _criticalEncoderHead,
                ref _criticalEncoderTail, ref _criticalEncoderCount, ref _criticalEncoderDropped,
                _criticalEncoderSignal, sample, "critical");
            EnqueueEncoderSample(_telemetryEncoderQueue, _telemetryEncoderQueueLock, ref _telemetryEncoderHead,
                ref _telemetryEncoderTail, ref _telemetryEncoderCount, ref _telemetryEncoderDropped,
                _telemetryEncoderSignal, sample, "telemetry");
        }

        private void EnqueueEncoderSample(EncoderDispatchSample[] queue, object sync, ref int head, ref int tail,
            ref int count, ref long dropped, AutoResetEvent signal, EncoderDispatchSample sample, string queueName)
        {
            lock (sync)
            {
                if (count == queue.Length)
                {
                    head = (head + 1) % queue.Length;
                    count--;
                    dropped++;
                    if (dropped == 1 || dropped % 100 == 0)
                        _logger.Error($"ENCODER_DISPATCH_QUEUE_OVERFLOW|queue={queueName}|dropped={dropped}");
                }
                queue[tail] = sample;
                tail = (tail + 1) % queue.Length;
                count++;
            }
            signal.Set();
        }

        private void EncoderDispatchLoop(bool critical)
        {
            try
            {
                Thread.CurrentThread.Name = critical ? "Qtis.EncoderCritical" : "Qtis.EncoderTelemetry";
                Thread.CurrentThread.Priority = critical ? ThreadPriority.Highest : ThreadPriority.BelowNormal;
            }
            catch { }

            var signal = critical ? _criticalEncoderSignal : _telemetryEncoderSignal;
            while (!_isDisposing)
            {
                EncoderDispatchSample sample;
                bool found = false;
                object sync = critical ? _criticalEncoderQueueLock : _telemetryEncoderQueueLock;
                lock (sync)
                {
                    if (critical ? _criticalEncoderCount > 0 : _telemetryEncoderCount > 0)
                    {
                        if (critical)
                        {
                            sample = _criticalEncoderQueue[_criticalEncoderHead];
                            _criticalEncoderHead = (_criticalEncoderHead + 1) % EncoderDispatchCapacity;
                            _criticalEncoderCount--;
                        }
                        else
                        {
                            sample = _telemetryEncoderQueue[_telemetryEncoderHead];
                            _telemetryEncoderHead = (_telemetryEncoderHead + 1) % EncoderDispatchCapacity;
                            _telemetryEncoderCount--;
                        }
                        found = true;
                    }
                    else sample = default(EncoderDispatchSample);
                }
                if (!found)
                {
                    signal.WaitOne(20);
                    continue;
                }

                var args = new EncoderEvent
                {
                    Channel = sample.Channel,
                    NewValue = sample.NewValue,
                    Delta = sample.Delta,
                    RawFrequencyHz = sample.RawFrequencyHz,
                    Timestamp = sample.Timestamp
                };
                try
                {
                    if (critical) CriticalCounterChanged?.Invoke(this, args);
                    else CounterChanged?.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, critical ? "ENCODER_CRITICAL_DISPATCH_FAILED" : "ENCODER_TELEMETRY_DISPATCH_FAILED");
                }
            }
        }

        #region Inizializzazione

        private static bool IsPhysicalTargetDevice(string description, string expectedToken)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return false;
            }

            var normalized = description.Trim();
            if (normalized.IndexOf("demo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return normalized.IndexOf(expectedToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private async Task<bool> CheckForHardwareDevicesAsync()
        {
            return await Task.Run(() =>
            {
                bool foundTargetBoard = false;
                try
                {
                    _logger.Info("Scansione dispositivi Advantech (Slot 0-15)..."); LogMessage?.Invoke(this, "[HW] Scansione dispositivi Advantech avviata (slot 0-15)");

                    using (var tempCtrl = new InstantDiCtrl())
                    {
                        // Proviamo i Device Number da 0 a 15 (limite ragionevole)
                        for (int i = 0; i < 16; i++)
                        {
                            try
                            {
                                // Creiamo la struct usando il costruttore (int deviceNumber)
                                // ATTENZIONE: Questo risolve l'errore di conversione.
                                var devInfo = new DeviceInformation(i);

                                // Tentativo di assegnazione
                                tempCtrl.SelectedDevice = devInfo;

                                // Se il driver carica il dispositivo, Initialized diventa true
                                if (tempCtrl.Initialized)
                                {
                                    // In alcune versioni vecchie la descrizione è vuota nella struct finché non si connette
                                    // Usiamo tempCtrl.SelectedDevice.Description che dovrebbe essere popolata
                                    string desc = tempCtrl.SelectedDevice.Description;
                                    _logger.Info($"  - Trovato Device {i}: {desc}");
                                    if (IsPhysicalTargetDevice(desc, "1756") || IsPhysicalTargetDevice(desc, "1884"))
                                    {
                                        foundTargetBoard = true;
                                    }
                                }
                            }
                            catch
                            {
                                // Ignora errori su slot vuoti
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Errore durante la scansione: {ex.Message}"); LogMessage?.Invoke(this, $"[HW] Errore scansione dispositivi: {ex.Message}");
                }

                return foundTargetBoard;
            });
        }
        private async Task<bool> InitializeRealDevicesAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    _diCtrl1756 = new InstantDiCtrl();
                    _doCtrl1756 = new InstantDoCtrl();

                    bool found1756 = false;

                    // --- RICERCA PCIE-1756 (Digital I/O) ---
                    for (int i = 0; i < 16; i++)
                    {
                        try
                        {
                            // Usa il costruttore corretto mostrato nel decompilato
                            var devInfo = new DeviceInformation(i);
                            _diCtrl1756.SelectedDevice = devInfo;

                            if (_diCtrl1756.Initialized)
                            {
                                string desc = _diCtrl1756.SelectedDevice.Description;
                                if (IsPhysicalTargetDevice(desc, "1756"))
                                {
                                    _di1756 = _diCtrl1756.SelectedDevice; // Salva la struct
                                    _logger.Info($"Scheda I/O collegata: {desc} (DeviceID: {i})"); LogMessage?.Invoke(this, $"[HW] Scheda I/O rilevata: {desc} (DeviceID {i})");

                                    // Collega anche l'output control allo stesso device
                                    _doCtrl1756.SelectedDevice = devInfo;

                                    found1756 = true;
                                    break; // Trovata, esci dal loop
                                }
                            }
                        }
                        catch { /* Slot vuoto, continua */ }
                    }

                    if (!found1756)
                    {
                        _logger.Warn("Nessuna scheda PCIE-1756 trovata durante la scansione."); LogMessage?.Invoke(this, "[HW] Nessuna scheda PCIE-1756 rilevata");
                        return false;
                    }

                    LogPcie1756PortCounts();

                    // --- RICERCA PCIE-1884 (Encoder) ---
                    // Nota: usiamo un controllo temporaneo per la scansione e poi
                    // inizializziamo i controller reali una volta trovato il device corretto.
                    _timerPulseCtrl = new TimerPulseCtrl();
                    bool found1884 = false;

                    for (int i = 0; i < 16; i++)
                    {
                        try
                        {
                            // Saltiamo il deviceID già usato dalla 1756 (opzionale, ma pulito)
                            if (found1756 && i == _di1756.DeviceNumber) continue;

                            var devInfo = new DeviceInformation(i);
                            _timerPulseCtrl.SelectedDevice = devInfo;

                            if (_timerPulseCtrl.Initialized)
                            {
                                string desc = _timerPulseCtrl.SelectedDevice.Description;
                                if (IsPhysicalTargetDevice(desc, "1884"))
                                {
                                    _di1884 = _timerPulseCtrl.SelectedDevice;
                                    _logger.Info($"Scheda Encoder collegata: {desc} (DeviceID: {i})"); LogMessage?.Invoke(this, $"[HW] Scheda encoder rilevata: {desc} (DeviceID {i})");

                                    // Inizializza i controller reali della 1884.
                                    _diCtrl1884 = new InstantDiCtrl { SelectedDevice = devInfo };
                                    _doCtrl1884 = new InstantDoCtrl { SelectedDevice = devInfo };
                                    _udCounterCtrl = new UdCounterCtrl { SelectedDevice = devInfo };
                                    _freqMeterCtrl = new FreqMeterCtrl { SelectedDevice = devInfo };
                                    // Controlli nuovi: la selezione canale memorizzata non vale piu'.
                                    _selectedCounterChannel = -1;
                                    _selectedFreqMeterChannel = -1;

                                    _logger.Info($"Controller PCIE-1884 inizializzati: DI={_diCtrl1884?.Initialized}, DO={_doCtrl1884?.Initialized}, Counter={_udCounterCtrl?.Initialized}, Freq={_freqMeterCtrl?.Initialized}");
                                    LogMessage?.Invoke(this, $"[HW] Controller PCIE-1884 inizializzati: DI={_diCtrl1884?.Initialized}, DO={_doCtrl1884?.Initialized}, Counter={_udCounterCtrl?.Initialized}, Freq={_freqMeterCtrl?.Initialized}");

                                    // Configura i canali encoder
                                    for (int ch = 0; ch < 4; ch++)
                                    {
                                        ConfigureEncoderChannel(ch);
                                        ConfigureEncoderFrequencyChannel(ch);
                                    }

                                    found1884 = true;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "HW_SCAN_1884_ENUM_ERROR — errore durante rilevamento scheda encoder PCIE-1884");
                        }
                    }

                    if (!found1884)
                    {
                        _logger.Info("Nessuna scheda encoder (1884) trovata."); LogMessage?.Invoke(this, "[HW] Nessuna scheda encoder PCIE-1884 rilevata");
                        _timerPulseCtrl.Dispose();
                        _timerPulseCtrl = null;
                    }

                    // Lettura iniziale per conferma
                    await ReadAllInputsAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Errore init hardware: {ex.Message}"); LogMessage?.Invoke(this, $"[HW] Errore inizializzazione hardware: {ex.Message}");
                    return false;
                }
            });
        }
        // Diagnostica una tantum: confronta il numero di porte riportato dal driver
        // (DioCtrlBase.PortCount) con la mappa attesa della PCIE-1756 (4 DI + 4 DO).
        // Solo log: non dimensiona i buffer e non influisce sull'esito dell'init.
        private void LogPcie1756PortCounts()
        {
            try
            {
                int diPortCount = _diCtrl1756.PortCount;
                int doPortCount = _doCtrl1756.PortCount;

                if (diPortCount != Pcie1756ChannelMap.DiPortCount || doPortCount != Pcie1756ChannelMap.DoPortCount)
                {
                    _logger.Warn(
                        $"PCIE1756_PORTCOUNT_MISMATCH|diPorts={diPortCount}|doPorts={doPortCount}" +
                        $"|expectedDiPorts={Pcie1756ChannelMap.DiPortCount}|expectedDoPorts={Pcie1756ChannelMap.DoPortCount}");
                }
                else
                {
                    _logger.Info($"PCIE1756_PORTCOUNT|diPorts={diPortCount}|doPorts={doPortCount}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "PCIE1756_PORTCOUNT_PROBE_FAILED");
            }
        }

        private void ConfigureEncoderChannel(int channel)
        {
            try
            {
                if (_udCounterCtrl != null && channel < _udCounterCtrl.Channels.Length)
                {
                    var channelConfig = _udCounterCtrl.Channels[channel];

                    // Modalita encoder quadrature 4X (ABPhaseX4).
                    channelConfig.CountingType = CountingType.AbPhaseX4;

                    // Imposta la quota iniziale senza resettare un canale gia attivo.
                    _udCounterCtrl.Channels[channel].InitialValue = 0;

                    _logger.Debug($"Encoder channel {channel} configured");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Errore nella configurazione dell'encoder {channel}: {ex.Message}");
            }
        }

        private void ConfigureEncoderFrequencyChannel(int channel)
        {
            try
            {
                if (_freqMeterCtrl != null && channel < _freqMeterCtrl.Channels.Length)
                {
                    var channelConfig = _freqMeterCtrl.Channels[channel];
                    channelConfig.FmMethod = FreqMeasureMethod.AutoAdaptive;
                    channelConfig.CollectionPeriod = 0.2;
                    channelConfig.NoiseFiltered = false;
                    _logger.Debug($"Encoder frequency channel {channel} configured");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Errore nella configurazione frequency meter encoder {channel}: {ex.Message}");
            }
        }

        // Da chiamare sempre con _lock gia' acquisito.
        private void SelectCounterChannel(int channel)
        {
            if (_selectedCounterChannel == channel)
            {
                return;
            }

            _udCounterCtrl.Channel = channel;
            _udCounterCtrl.ChannelStart = channel;
            _selectedCounterChannel = channel;
        }

        // Da chiamare sempre con _lock gia' acquisito.
        private void SelectFreqMeterChannel(int channel)
        {
            if (_selectedFreqMeterChannel == channel)
            {
                return;
            }

            _freqMeterCtrl.Channel = channel;
            _freqMeterCtrl.ChannelStart = channel;
            _selectedFreqMeterChannel = channel;
        }

        private void SetEncoderChannelActive(int channel, bool isActive)
        {
            if (_udCounterCtrl == null || channel < 0 || channel >= _udCounterCtrl.Channels.Length)
            {
                return;
            }

            lock (_lock)
            {
                SelectCounterChannel(channel);
                _udCounterCtrl.Enabled = isActive;
                if (_freqMeterCtrl != null && channel < _freqMeterCtrl.Channels.Length)
                {
                    SelectFreqMeterChannel(channel);
                    _freqMeterCtrl.Enabled = isActive;
                }
            }
        }

        private void SetEncoderChannelActiveInMemory(int channel, bool isActive)
        {
            if (channel < 0 || channel >= _activeEncoderChannels.Length)
            {
                return;
            }

            lock (_lock)
            {
                _activeEncoderChannels[channel] = isActive;
                if (!isActive)
                {
                    _encoderSamplingStarted[channel] = false;
                    _encoderFrequencyHz[channel] = 0.0;
                }
            }
        }

        private bool TrySetEncoderChannelActive(int channel, bool isActive, out bool functionBusy)
        {
            functionBusy = false;

            try
            {
                SetEncoderChannelActive(channel, isActive);
                return true;
            }
            catch (Exception ex) when (IsAdvantechFunctionBusy(ex))
            {
                functionBusy = true;
                return false;
            }
        }

        private static bool IsAdvantechFunctionBusy(Exception ex)
        {
            return ex != null &&
                   ex.Message != null &&
                   ex.Message.IndexOf("ErrorFuncBusy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<bool> InitializeSimulationAsync()
        {
            try
            {
                _logger.Info("Inizializzazione modalità simulazione...");

                // Carica stato simulazione da file
                LoadSimulationState();

                // Inizializza buffer per simulazione (8 byte legacy, porte reali 0..3)
                _lastInputs1756 = new byte[Pcie1756ChannelMap.LegacyPortBufferLength];
                _currentOutputs1756 = new byte[Pcie1756ChannelMap.LegacyPortBufferLength];
                _lastInputs1884 = 0x00;
                _currentOutputs1884 = 0x00;
                _encoderValues = new long[4];

                // Copia valori dal file di simulazione
                if (_simState != null)
                {
                    // Copia tollerante: un file con array piu' corti (o vuoti) non deve
                    // far fallire l'avvio della simulazione.
                    var savedInputs1756 = _simState.PCIE1756_Inputs ?? Array.Empty<byte>();
                    var savedOutputs1756 = _simState.PCIE1756_Outputs ?? Array.Empty<byte>();
                    Array.Copy(savedInputs1756, _lastInputs1756, Math.Min(savedInputs1756.Length, _lastInputs1756.Length));
                    Array.Copy(savedOutputs1756, _currentOutputs1756, Math.Min(savedOutputs1756.Length, _currentOutputs1756.Length));

                    // I byte 4..7 non corrispondono a porte reali della PCIE-1756: azzerati dopo il caricamento.
                    Array.Clear(_lastInputs1756, Pcie1756ChannelMap.DiPortCount, _lastInputs1756.Length - Pcie1756ChannelMap.DiPortCount);
                    Array.Clear(_currentOutputs1756, Pcie1756ChannelMap.DoPortCount, _currentOutputs1756.Length - Pcie1756ChannelMap.DoPortCount);

                    _lastInputs1884 = _simState.PCIE1884_DigitalInput;
                    _currentOutputs1884 = _simState.PCIE1884_DigitalOutput;
                    Array.Copy(_simState.PCIE1884_Counters, _encoderValues, 4);
                }

                _isInitialized = true;
                StartPolling();

                _logger.Info("Modalità simulazione inizializzata.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inizializzazione della simulazione: {ex.Message}"); LogMessage?.Invoke(this, $"[HW] Errore inizializzazione simulazione: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Sistema di simulazione basato su XML

        [XmlRoot("SimulationState")]
        public class SimulationState
        {
            [XmlArray("PCIE1756_Inputs")]
            [XmlArrayItem("Byte")]
            public byte[] PCIE1756_Inputs { get; set; } = new byte[Pcie1756ChannelMap.LegacyPortBufferLength];

            [XmlArray("PCIE1756_Outputs")]
            [XmlArrayItem("Byte")]
            public byte[] PCIE1756_Outputs { get; set; } = new byte[Pcie1756ChannelMap.LegacyPortBufferLength];

            [XmlArray("PCIE1884_Counters")]
            [XmlArrayItem("Counter")]
            public long[] PCIE1884_Counters { get; set; } = new long[4];

            [XmlElement("PCIE1884_DigitalInput")]
            public byte PCIE1884_DigitalInput { get; set; }

            [XmlElement("PCIE1884_DigitalOutput")]
            public byte PCIE1884_DigitalOutput { get; set; }

            [XmlElement("LastUpdate")]
            public DateTime LastUpdate { get; set; } = DateTime.Now;
        }

        private void LoadSimulationState()
        {
            try
            {
                if (File.Exists(_simulationFilePath))
                {
                    var serializer = new XmlSerializer(typeof(SimulationState));
                    using (var reader = new StreamReader(_simulationFilePath))
                    {
                        _simState = (SimulationState)serializer.Deserialize(reader);
                        _logger.Info("Stato simulazione caricato da file");
                    }
                }
                else
                {
                    _simState = new SimulationState();
                    _logger.Info("Creato nuovo stato di simulazione");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Impossibile caricare lo stato di simulazione: {ex.Message}");
                _simState = new SimulationState();
            }
        }

        private void SaveSimulationState()
        {
            try
            {
                if (_simState != null)
                {
                    _simState.LastUpdate = DateTime.Now;

                    // Aggiorna stato corrente nel file
                    _simState.PCIE1756_Inputs = _lastInputs1756;
                    _simState.PCIE1756_Outputs = _currentOutputs1756;
                    _simState.PCIE1884_Counters = _encoderValues;
                    _simState.PCIE1884_DigitalInput = _lastInputs1884;
                    _simState.PCIE1884_DigitalOutput = _currentOutputs1884;

                    var serializer = new XmlSerializer(typeof(SimulationState));
                    using (var writer = new StreamWriter(_simulationFilePath))
                    {
                        serializer.Serialize(writer, _simState);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Impossibile salvare lo stato di simulazione: {ex.Message}");
            }
        }

        #endregion

        #region Polling e gestione eventi

        private void StartPolling()
        {
            _pollingCts = new CancellationTokenSource();
            _pollingTask = Task.Factory.StartNew(
                () => PollingLoop(_pollingCts.Token),
                _pollingCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private void PollingLoop(CancellationToken token)
        {
            // Encoder-trigger precision is bounded by this sampling period. At 40 m/min,
            // 10 ms represents 6.7 mm before software and board-write latency. A 2 ms
            // target keeps the software contribution near 1.3 mm. This worker must not
            // use Task.Delay: on production PCs its continuation was observed every
            // 15-17 ms, with occasional 100+ ms stalls caused by thread-pool scheduling.
            const int pollIntervalMs = 2;
            const int overrunWarningMs = 10;
            long previousLoopStartTimestamp = 0;
            bool highResolutionTimerActive = false;

            // Conteggi GC all'inizio del ciclo precedente. Se un buco di 'cycle' coincide
            // con un incremento, la pausa e' una sospensione dell'intero processo per
            // garbage collection: nessuna priorita' di thread e nessuno spin la evita, e
            // spiega perche' nello stesso istante sballa anche la durata degli impulsi,
            // che vive su un thread diverso.
            int previousGen0 = GC.CollectionCount(0);
            int previousGen1 = GC.CollectionCount(1);
            int previousGen2 = GC.CollectionCount(2);

            try
            {
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                }
                catch (Exception ex)
                {
                    _logger.Debug($"IO_POLL_PRIORITY_DEFAULT|reason={ex.Message}");
                }

                highResolutionTimerActive = TimeBeginPeriod(1) == 0;
                _logger.Info(
                    $"IO_POLL_STARTED|worker=dedicated|priority={Thread.CurrentThread.Priority}" +
                    $"|timer_resolution={(highResolutionTimerActive ? "1ms" : "default")}|target={pollIntervalMs}ms" +
                    $"|gcServer={System.Runtime.GCSettings.IsServerGC}" +
                    $"|gcLatency={System.Runtime.GCSettings.LatencyMode}");

                while (!token.IsCancellationRequested && !_isDisposing)
                {
                    try
                    {
                        long loopStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                        double cycleTimeMs = previousLoopStartTimestamp == 0
                            ? 0.0
                            : (loopStartTimestamp - previousLoopStartTimestamp) * 1000.0 /
                              System.Diagnostics.Stopwatch.Frequency;
                        previousLoopStartTimestamp = loopStartTimestamp;

                        int gen0 = GC.CollectionCount(0);
                        int gen1 = GC.CollectionCount(1);
                        int gen2 = GC.CollectionCount(2);
                        int gen0Delta = gen0 - previousGen0;
                        int gen1Delta = gen1 - previousGen1;
                        int gen2Delta = gen2 - previousGen2;
                        previousGen0 = gen0;
                        previousGen1 = gen1;
                        previousGen2 = gen2;

                        var pollWatch = System.Diagnostics.Stopwatch.StartNew();
                        long phaseStartedTimestamp = loopStartTimestamp;
                        string slowestPhase = "none";
                        double slowestPhaseMs = 0.0;

                    // 1. Leggi prima gli encoder: ogni edge input usera' questo snapshot,
                    // evitando che la UI legga un count gia' avanzato dopo il fronte fotocellula.
                    long[] encoderSnapshot = null;
                    DateTime encoderSnapshotTimestamp = DateTime.Now;
                    if (_udCounterCtrl != null || _isSimulationMode)
                    {
                        ReadAllEncodersAsync().GetAwaiter().GetResult();
                        encoderSnapshot = CreateEncoderSnapshot();
                        encoderSnapshotTimestamp = DateTime.Now;
                    }
                    phaseStartedTimestamp = CaptureSlowestPollingPhase(
                        "encoder-read", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);

                    // 2. Leggi tutti gli input
                    var currentInputs1756 = ReadAllInputsAsync().GetAwaiter().GetResult();
                    phaseStartedTimestamp = CaptureSlowestPollingPhase(
                        "input-1756-read", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);
                    var currentInput1884 = ReadDigitalInput1884Async().GetAwaiter().GetResult();
                    phaseStartedTimestamp = CaptureSlowestPollingPhase(
                        "input-1884-read", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);

                    // 3. Rileva cambiamenti negli input PCIE-1756
                    DetectInputChanges1756(currentInputs1756, encoderSnapshot, encoderSnapshotTimestamp);
                    phaseStartedTimestamp = CaptureSlowestPollingPhase(
                        "input-1756-events", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);

                    // 4. Rileva cambiamenti negli input PCIE-1884
                    DetectInputChanges1884(currentInput1884, encoderSnapshot, encoderSnapshotTimestamp);
                    phaseStartedTimestamp = CaptureSlowestPollingPhase(
                        "input-1884-events", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);

                    // 5. Esegui logica personalizzata
                    ExecuteCustomLogic();
                    phaseStartedTimestamp = CaptureSlowestPollingPhase(
                        "custom-logic", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);

                    // 6. Gestisci trigger dei contatori
                    CheckCounterTriggersAsync().GetAwaiter().GetResult();
                    phaseStartedTimestamp = CaptureSlowestPollingPhase(
                        "counter-triggers", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);

                    // 7. Salva stato simulazione (solo in modalità simulazione)
                    if (_isSimulationMode &&
                        (DateTime.UtcNow - _lastSimulationStateSaveUtc).TotalMilliseconds >= 1000.0)
                    {
                        SaveSimulationState();
                        _lastSimulationStateSaveUtc = DateTime.UtcNow;
                    }
                    CaptureSlowestPollingPhase(
                        "simulation-save", phaseStartedTimestamp, ref slowestPhase, ref slowestPhaseMs);

                    // 8. Calcola il tempo di esecuzione e attesa
                    pollWatch.Stop();
                    var pollTime = pollWatch.Elapsed.TotalMilliseconds;
                    if ((pollTime >= overrunWarningMs || cycleTimeMs >= overrunWarningMs) &&
                        (DateTime.UtcNow - _lastPollingOverrunLogUtc).TotalSeconds >= 5.0)
                    {
                        _lastPollingOverrunLogUtc = DateTime.UtcNow;
                        // encHw = lettura hardware BDaq; encDispatch = solo copia nei ring buffer.
                        // Tracking e UI sono eseguiti dai worker separati e non fanno piu'
                        // parte del tempo di lavoro del polling.
                        _logger.Warn(
                            $"IO_POLL_OVERRUN|cycle={cycleTimeMs:0.###}ms|work={pollTime:0.###}ms" +
                            $"|target={pollIntervalMs}ms|slowest={slowestPhase}|slowestMs={slowestPhaseMs:0.###}" +
                            $"|encHwMs={_lastEncoderHardwareMs:0.###}|encDispatchMs={_lastEncoderDispatchMs:0.###}" +
                            $"|gc0={gen0Delta}|gc1={gen1Delta}|gc2={gen2Delta}");
                    }

                        WaitUntilNextPoll(loopStartTimestamp, pollIntervalMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Errore nel polling loop I/O; nuovo tentativo tra 1 secondo");
                        if (token.WaitHandle.WaitOne(1000))
                        {
                            break;
                        }

                        previousLoopStartTimestamp = 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Errore non recuperabile nel polling loop I/O");
            }
            finally
            {
                if (highResolutionTimerActive)
                {
                    TimeEndPeriod(1);
                }

                _logger.Info("IO_POLL_STOPPED|worker=dedicated");
            }
        }

        private static long CaptureSlowestPollingPhase(
            string phase,
            long phaseStartedTimestamp,
            ref string slowestPhase,
            ref double slowestPhaseMs)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double elapsedMs = (now - phaseStartedTimestamp) * 1000.0 /
                               System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs > slowestPhaseMs)
            {
                slowestPhaseMs = elapsedMs;
                slowestPhase = phase;
            }

            return now;
        }

        // Intervalli fino a questa soglia vengono attesi senza mai dormire: Thread.Sleep(1)
        // non si risveglia dopo 1 ms ma al primo tick del timer di sistema. Su Windows 11
        // timeBeginPeriod(1) ritorna successo ma non garantisce piu' la granularita' a 1 ms
        // per il processo, quindi il risveglio cadeva sul tick di default (~15,6 ms): i
        // cicli misurati erano infatti multipli di quel tick (12-16 ms, 55 ms, 65 ms) con
        // corpo del poll a ~1,5 ms. Restando runnable il thread mantiene la cadenza a 2 ms
        // e i punti di intervento vengono valutati alla quota giusta.
        private const int MaxSpinOnlyPollIntervalMs = 5;

        private static void WaitUntilNextPoll(long loopStartTimestamp, int pollIntervalMs, CancellationToken token)
        {
            long targetTimestamp = loopStartTimestamp +
                (long)Math.Ceiling(System.Diagnostics.Stopwatch.Frequency * pollIntervalMs / 1000.0);
            bool spinOnly = pollIntervalMs <= MaxSpinOnlyPollIntervalMs;

            while (!token.IsCancellationRequested)
            {
                long remainingTicks = targetTimestamp - System.Diagnostics.Stopwatch.GetTimestamp();
                if (remainingTicks <= 0)
                {
                    return;
                }

                double remainingMs = remainingTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

                // Sopra la soglia (poll lento configurato a mano) conviene ancora dormire:
                // bruciare un core per decine di ms non avrebbe senso.
                if (!spinOnly && remainingMs >= 1.25)
                {
                    Thread.Sleep(1);
                    continue;
                }

                // Solo SpinWait (istruzioni PAUSE), senza Yield: cedere il core a un thread
                // pronto significa riaverlo indietro non prima della fine del suo quanto,
                // cioe' gli stessi 10-15 ms del vecchio Sleep(1). Con VisionPro, salvataggio
                // immagini e ThreadPool a 24 thread minimi c'e' quasi sempre qualcuno pronto,
                // quindi lo Yield in spin stretto reintroduceva il buco che deve eliminare.
                // Il thread resta sul core per l'attesa (2 ms su 24 processori logici).
                Thread.SpinWait(64);
            }

            token.ThrowIfCancellationRequested();
        }

        private long[] CreateEncoderSnapshot()
        {
            var snapshot = new long[_encoderValues.Length];
            Array.Copy(_encoderValues, snapshot, _encoderValues.Length);
            return snapshot;
        }

        private void DetectInputChanges1756(byte[] currentInputs, long[] encoderSnapshot, DateTime encoderSnapshotTimestamp)
        {
            for (int port = 0; port < Pcie1756ChannelMap.DiPortCount; port++)
            {
                byte oldValue = _lastInputs1756[port];
                byte newValue = currentInputs[port];

                if (oldValue != newValue)
                {
                    byte changedBits = (byte)(oldValue ^ newValue);

                    for (int bit = 0; bit < 8; bit++)
                    {
                        if ((changedBits & (1 << bit)) != 0)
                        {
                            int channel = port * 8 + bit;
                            bool newState = (newValue & (1 << bit)) != 0;

                            // Aggiorna cache
                            if (newState)
                                _lastInputs1756[port] |= (byte)(1 << bit);
                            else
                                _lastInputs1756[port] &= (byte)~(1 << bit);

                            // Solleva evento solo per i canali DI reali (0-31)
                            if (channel < Pcie1756ChannelMap.DiChannelCount)
                            {
                                InputChanged?.Invoke(this, new IOEvent
                                {
                                    Channel = channel,
                                    NewState = newState,
                                    Timestamp = DateTime.Now,
                                    DeviceType = "PCIE-1756",
                                    EncoderCountsSnapshot = encoderSnapshot,
                                    EncoderSnapshotTimestamp = encoderSnapshotTimestamp
                                });

                                // Esegui azioni associate
                                ExecuteInputActions(channel, newState);
                            }
                        }
                    }
                }
            }
        }

        private void DetectInputChanges1884(byte currentInput, long[] encoderSnapshot, DateTime encoderSnapshotTimestamp)
        {
            byte oldValue = _lastInputs1884;
            byte newValue = currentInput;

            if (oldValue != newValue)
            {
                byte changedBits = (byte)(oldValue ^ newValue);

                for (int bit = 0; bit < 4; bit++) // Solo 4 bit per PCIE-1884
                {
                    if ((changedBits & (1 << bit)) != 0)
                    {
                        int channel = bit; // Canali 0-3
                        bool newState = (newValue & (1 << bit)) != 0;

                        // Aggiorna cache
                        if (newState)
                            _lastInputs1884 |= (byte)(1 << bit);
                        else
                            _lastInputs1884 &= (byte)~(1 << bit);

                        // Solleva evento con offset 100 per distinguere da PCIE-1756
                        InputChanged?.Invoke(this, new IOEvent
                        {
                            Channel = 100 + channel,
                            NewState = newState,
                            Timestamp = DateTime.Now,
                            DeviceType = "PCIE-1884",
                            EncoderCountsSnapshot = encoderSnapshot,
                            EncoderSnapshotTimestamp = encoderSnapshotTimestamp
                        });

                        // Esegui azioni associate (se configurate per questi canali)
                        ExecuteInputActions(100 + channel, newState);
                    }
                }
            }
        }

        #endregion

        #region Implementazione IIODeviceManager
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.Info("Inizializzazione dispositivi Advantech...");

                // Verifica hardware reale o forza simulazione
                bool hardwareFound = !_forceSimulationMode && await CheckForHardwareDevicesAsync();

                // Se non trovo hardware, vado in simulazione
                _isSimulationMode = !hardwareFound;

                if (_isSimulationMode)
                {
                    _logger.Warn(_forceSimulationMode ? "Modalita simulazione forzata dall'applicazione." : "Nessun dispositivo Advantech trovato. Modalita simulazione attivata."); LogMessage?.Invoke(this, _forceSimulationMode ? "[HW] Simulazione forzata dall'applicazione" : "[HW] Nessun dispositivo Advantech trovato, fallback a simulazione");
                    return await InitializeSimulationAsync();
                }

                // Inizializza dispositivi reali
                bool success = await InitializeRealDevicesAsync();

                if (success)
                {
                    _isInitialized = true;
                    StartPolling();
                    _logger.Info("Dispositivi Advantech inizializzati con successo."); LogMessage?.Invoke(this, "[HW] Dispositivi Advantech inizializzati con successo");
                    return true;
                }

                _logger.Warn("Inizializzazione hardware reale non riuscita. Possibile board occupata da DAQNavi Navigator o da un altro software."); 
                LogMessage?.Invoke(this, "[HW] Inizializzazione hardware reale fallita. Possibile board occupata da DAQNavi Navigator o da un altro software.");
                _isSimulationMode = true;
                return await InitializeSimulationAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inizializzazione dei dispositivi: {ex.Message}");
                _isSimulationMode = true;
                return await InitializeSimulationAsync();
            }
        }
        private List<DeviceInformation> GetDevices(string searchDescription)
        {
            var foundDevices = new List<DeviceInformation>();

            // Usiamo un controllo temporaneo per sondare i driver
            using (var tempCtrl = new InstantDiCtrl())
            {
                // Scansione "Brute Force" degli ID dispositivo da 0 a 15
                for (int i = 0; i < 16; i++)
                {
                    try
                    {
                        var devInfo = new DeviceInformation(i);
                        tempCtrl.SelectedDevice = devInfo;

                        // Se il driver si inizializza, il dispositivo esiste
                        if (tempCtrl.Initialized)
                        {
                            string currentDesc = tempCtrl.SelectedDevice.Description;

                            // Se la descrizione contiene la stringa cercata (es. "1756")
                            if (!string.IsNullOrEmpty(currentDesc) &&
                                currentDesc.IndexOf(searchDescription, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Aggiungiamo alla lista usando il DeviceNumber corretto
                                foundDevices.Add(new DeviceInformation(i, currentDesc, AccessMode.ModeWrite, 0));
                            }
                        }
                    }
                    catch
                    {
                        // Ignora slot vuoti o errori di driver
                    }
                }
            }

            return foundDevices;
        }
        public async Task<bool> InitializeDeviceAsync(AdvantechDeviceConfig config)
        {
            try
            {
                // Cerca dispositivo specifico
                var devices = GetDevices(config.DeviceDescription);
                if (devices.Count == 0)
                {
                    _logger.Warn($"Dispositivo {config.DeviceDescription} non trovato");
                    return false;
                }

                var device = devices[0];

                if (config.Type == AdvantechDeviceConfig.DeviceType.PCIE1756)
                {
                    _di1756 = device;
                    _diCtrl1756 = new InstantDiCtrl { SelectedDevice = device };
                    _doCtrl1756 = new InstantDoCtrl { SelectedDevice = device };
                    _logger.Info($"Dispositivo PCIE-1756 configurato: {config.DeviceName}");
                }
                else if (config.Type == AdvantechDeviceConfig.DeviceType.PCIE1884)
                {
                    _di1884 = device;
                    _diCtrl1884 = new InstantDiCtrl { SelectedDevice = device };
                    _doCtrl1884 = new InstantDoCtrl { SelectedDevice = device };
                    _udCounterCtrl = new UdCounterCtrl { SelectedDevice = device };
                    // Controllo nuovo: la selezione canale memorizzata non vale piu'.
                    _selectedCounterChannel = -1;
                    _logger.Info($"Dispositivo PCIE-1884 configurato: {config.DeviceName}");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inizializzazione del dispositivo {config.DeviceName}: {ex.Message}");
                return false;
            }
        }

        public async Task<byte[]> ReadAllInputsAsync()
        {
            if (_disposed || _isDisposing)
            {
                return _lastInputs1756;
            }

            if (_isSimulationMode)
            {
                // In simulazione, genera valori casuali ma mantieni un pattern realistico
                var random = new Random();
                for (int i = 0; i < Pcie1756ChannelMap.DiPortCount; i++)
                {
                    // Mantieni alcuni bit fissi per simulare un pattern reale
                    if (i % 2 == 0)
                        _lastInputs1756[i] = (byte)random.Next(0, 256);
                }
                return _lastInputs1756;
            }

            try
            {
                if (_diCtrl1756 == null)
                    return new byte[Pcie1756ChannelMap.LegacyPortBufferLength];

                byte[] buffer = new byte[Pcie1756ChannelMap.LegacyPortBufferLength];

                // Legge 4 porte da 8 bit (DI0-DI31) nei byte 0..3; i byte 4..7 restano 0
                ErrorCode error = _diCtrl1756.Read(0, Pcie1756ChannelMap.DiPortCount, buffer);

                if (error != ErrorCode.Success)
                {
                    _logger.Warn($"Errore nella lettura degli input PCIE-1756: {error}");
                }

                return buffer;
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nella lettura degli input PCIE-1756: {ex.Message}");
                return new byte[Pcie1756ChannelMap.LegacyPortBufferLength];
            }
        }

        private async Task<byte> ReadDigitalInput1884Async()
        {
            if (_isSimulationMode)
            {
                // Simula 4 bit di input
                var random = new Random();
                _lastInputs1884 = (byte)(random.Next(0, 16) & 0x0F);
                return _lastInputs1884;
            }

            try
            {
                if (_diCtrl1884 == null)
                    return 0x00;

                byte[] buffer = new byte[1];
                ErrorCode error = _diCtrl1884.Read(0, 1, buffer); // Legge 1 porta da 4 bit

                if (error != ErrorCode.Success)
                {
                    _logger.Warn($"Errore nella lettura degli input PCIE-1884: {error}");
                }

                return (byte)(buffer[0] & 0x0F); // Maschera solo i 4 bit validi
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nella lettura degli input PCIE-1884: {ex.Message}");
                return 0x00;
            }
        }

        public async Task<bool> ReadInputAsync(int channel)
        {
            // Distingue tra PCIE-1756 (0-31) e PCIE-1884 (100-103)
            if (channel >= 100 && channel <= 103)
            {
                // Input PCIE-1884 (4 bit)
                int bit = channel - 100;
                if (bit < 0 || bit >= 4)
                    throw new ArgumentOutOfRangeException(nameof(channel));

                var input = await ReadDigitalInput1884Async();
                return (input & (1 << bit)) != 0;
            }
            else
            {
                // Input PCIE-1756 (32 bit, DI0-DI31)
                if (!Pcie1756ChannelMap.IsValidDiChannel(channel))
                    throw new ArgumentOutOfRangeException(nameof(channel));

                int port = channel / 8;
                int bit = channel % 8;

                var inputs = await ReadAllInputsAsync();
                return (inputs[port] & (1 << bit)) != 0;
            }
        }

        public async Task WriteOutputAsync(int channel, bool value)
        {
            // Contratto invariato per heartbeat, allarmi e comandi manuali: un rifiuto della
            // scheda viene registrato nel log ma non propagato. Il worker impulsi usa invece
            // l'esito di TryWriteOutputCore per non dichiarare riuscito un fronte mai partito.
            TryWriteOutputCore(channel, value, out _);
        }

        /// <summary>
        /// Aggiorna la cache e scrive la porta sotto <c>_outputWriteLock</c>. Restituisce false
        /// se la scheda rifiuta la scrittura (ErrorCode diverso da Success, eccezione BDaq o
        /// controller non inizializzato fuori simulazione); in simulazione restituisce true.
        /// </summary>
        private bool TryWriteOutputCore(int channel, bool value, out string failure)
        {
            failure = null;

            // Distingue tra PCIE-1756 (0-31) e PCIE-1884 (100-103)
            if (channel >= 100 && channel <= 103)
            {
                // Output PCIE-1884 (4 bit)
                int bit = channel - 100;
                if (bit < 0 || bit >= 4)
                    throw new ArgumentOutOfRangeException(nameof(channel));

                long writeRequested = Stopwatch.GetTimestamp();
                double lockWaitMs = 0.0;
                double hardwareWriteMs = 0.0;
                lock (_outputWriteLock)
                {
                    lockWaitMs = (Stopwatch.GetTimestamp() - writeRequested) * 1000.0 / Stopwatch.Frequency;
                    if (value)
                        _currentOutputs1884 |= (byte)(1 << bit);
                    else
                        _currentOutputs1884 &= (byte)~(1 << bit);

                    if (!_isSimulationMode)
                    {
                        if (_doCtrl1884 == null)
                        {
                            failure = "controller-not-initialized";
                        }
                        else
                        {
                            try
                            {
                                long hardwareWriteStarted = Stopwatch.GetTimestamp();
                                byte[] buffer = new byte[] { _currentOutputs1884 };
                                ErrorCode error = _doCtrl1884.Write(0, 1, buffer);
                                hardwareWriteMs = (Stopwatch.GetTimestamp() - hardwareWriteStarted) * 1000.0 /
                                                  Stopwatch.Frequency;

                                if (error != ErrorCode.Success)
                                {
                                    failure = error.ToString();
                                    _logger.Warn($"IO_OUTPUT_WRITE_FAILED|board=PCIE-1884|channel={channel}|error={error}");
                                }
                            }
                            catch (Exception ex)
                            {
                                failure = ex.GetType().Name;
                                _logger.Error(ex, $"IO_OUTPUT_WRITE_FAILED|board=PCIE-1884|channel={channel}");
                            }
                        }
                    }
                }

                double totalWriteMs = (Stopwatch.GetTimestamp() - writeRequested) * 1000.0 / Stopwatch.Frequency;
                if (totalWriteMs > 20.0)
                {
                    _logger.Warn(
                        $"IO_OUTPUT_WRITE_SLOW|board=PCIE-1884|channel={channel}|lockWaitMs={lockWaitMs:0.###}" +
                        $"|hardwareMs={hardwareWriteMs:0.###}|totalMs={totalWriteMs:0.###}");
                }

                _logger.Debug($"Output PCIE-1884 {channel} set to {(value ? "HIGH" : "LOW")}");
                return failure == null;
            }
            else
            {
                // Output PCIE-1756 (32 bit, DO0-DO31)
                if (!Pcie1756ChannelMap.IsValidDoChannel(channel))
                    throw new ArgumentOutOfRangeException(nameof(channel));

                int port = channel / 8;
                int bit = channel % 8;

                long writeRequested = Stopwatch.GetTimestamp();
                double lockWaitMs = 0.0;
                double hardwareWriteMs = 0.0;
                lock (_outputWriteLock)
                {
                    lockWaitMs = (Stopwatch.GetTimestamp() - writeRequested) * 1000.0 / Stopwatch.Frequency;
                    if (value)
                        _currentOutputs1756[port] |= (byte)(1 << bit);
                    else
                        _currentOutputs1756[port] &= (byte)~(1 << bit);

                    if (!_isSimulationMode)
                    {
                        if (_doCtrl1756 == null)
                        {
                            failure = "controller-not-initialized";
                        }
                        else
                        {
                            try
                            {
                                long writeStarted = Stopwatch.GetTimestamp();
                                byte[] portValue = { _currentOutputs1756[port] };
                                ErrorCode error = _doCtrl1756.Write(port, 1, portValue);
                                hardwareWriteMs = (Stopwatch.GetTimestamp() - writeStarted) * 1000.0 / Stopwatch.Frequency;

                                if (error != ErrorCode.Success)
                                {
                                    failure = error.ToString();
                                    _logger.Warn($"IO_OUTPUT_WRITE_FAILED|board=PCIE-1756|channel={channel}|port={port}|error={error}");
                                }
                            }
                            catch (Exception ex)
                            {
                                failure = ex.GetType().Name;
                                _logger.Error(ex, $"IO_OUTPUT_WRITE_FAILED|board=PCIE-1756|channel={channel}|port={port}");
                            }
                        }
                    }
                }

                double totalWriteMs = (Stopwatch.GetTimestamp() - writeRequested) * 1000.0 / Stopwatch.Frequency;
                if (totalWriteMs > 20.0)
                {
                    _logger.Warn(
                        $"IO_OUTPUT_WRITE_SLOW|board=PCIE-1756|channel={channel}|port={port}" +
                        $"|lockWaitMs={lockWaitMs:0.###}|hardwareMs={hardwareWriteMs:0.###}|totalMs={totalWriteMs:0.###}");
                }

                _logger.Debug($"Output PCIE-1756 {channel} set to {(value ? "HIGH" : "LOW")}");
                return failure == null;
            }
        }

        public Task<OutputPulseResult> PulseOutputAsync(int channel, bool activeElectricalState, bool inactiveElectricalState, int pulseMilliseconds)
        {
            if (!Pcie1756ChannelMap.IsValidOutputChannel(channel))
                throw new ArgumentOutOfRangeException(nameof(channel));

            var request = new OutputPulseRequest
            {
                Channel = channel,
                ActiveState = activeElectricalState,
                InactiveState = inactiveElectricalState,
                PulseMilliseconds = Math.Max(1, pulseMilliseconds),
                Completion = new TaskCompletionSource<OutputPulseResult>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            lock (_outputPulseQueueLock)
            {
                if (_isDisposing)
                    throw new ObjectDisposedException(nameof(AdvantechDeviceManager));
                if (_outputPulseCount == _outputPulseQueue.Length)
                    throw new InvalidOperationException("Deterministic output pulse queue is full.");
                _outputPulseQueue[_outputPulseTail] = request;
                _outputPulseTail = (_outputPulseTail + 1) % _outputPulseQueue.Length;
                _outputPulseCount++;
            }
            _outputPulseSignal.Set();
            return request.Completion.Task;
        }

        private void OutputPulseLoop()
        {
            try
            {
                Thread.CurrentThread.Name = "Qtis.OutputPulse";
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch { }

            var activeRequests = new OutputPulseRequest[OutputPulseQueueCapacity];
            var raisedTimestamps = new long[OutputPulseQueueCapacity];
            var deadlines = new long[OutputPulseQueueCapacity];
            var channelsNeedingLowReset = new HashSet<int>();
            int activeCount = 0;

            while (!_isDisposing)
            {
                // Abbassa prima gli impulsi scaduti. La rimozione swap-with-last evita
                // compattazioni e mantiene il percorso privo di allocazioni.
                long now = Stopwatch.GetTimestamp();
                for (int i = activeCount - 1; i >= 0; i--)
                {
                    if (now < deadlines[i])
                    {
                        continue;
                    }

                    var completed = activeRequests[i];
                    try
                    {
                        // La discesa deve arrivare in scheda: ogni tentativo riscrive la porta
                        // dalla cache. Se tutti falliscono la linea puo' essere rimasta alta.
                        string fallFailure = null;
                        bool lowered = false;
                        for (int attempt = 0; attempt < OutputPulseFallAttempts && !lowered; attempt++)
                        {
                            lowered = TryWriteOutputCore(completed.Channel, completed.InactiveState, out fallFailure);
                        }

                        if (lowered)
                        {
                            channelsNeedingLowReset.Remove(completed.Channel);
                            completed.Completion.TrySetResult(new OutputPulseResult
                            {
                                RaisedStopwatchTicks = raisedTimestamps[i],
                                LoweredStopwatchTicks = Stopwatch.GetTimestamp()
                            });
                        }
                        else
                        {
                            channelsNeedingLowReset.Add(completed.Channel);
                            completed.Completion.TrySetException(
                                new OutputPulseException(completed.Channel, OutputPulsePhase.Fall, fallFailure));
                        }
                    }
                    catch (Exception ex)
                    {
                        channelsNeedingLowReset.Add(completed.Channel);
                        completed.Completion.TrySetException(ex);
                    }

                    activeCount--;
                    activeRequests[i] = activeRequests[activeCount];
                    raisedTimestamps[i] = raisedTimestamps[activeCount];
                    deadlines[i] = deadlines[activeCount];
                    activeRequests[activeCount] = null;
                }

                // Avvia la prima richiesta pronta in ordine FIFO per ciascun canale.
                // Una richiesta per un canale ancora HIGH non deve fermare quelle
                // successive per altri canali: le quote delle camere sono indipendenti.
                bool startedAny = false;
                while (activeCount < activeRequests.Length)
                {
                    OutputPulseRequest request = null;
                    lock (_outputPulseQueueLock)
                    {
                        for (int offset = 0; offset < _outputPulseCount; offset++)
                        {
                            int candidateIndex = (_outputPulseHead + offset) % _outputPulseQueue.Length;
                            var candidate = _outputPulseQueue[candidateIndex];
                            bool channelBusy = false;
                            for (int i = 0; i < activeCount; i++)
                            {
                                if (activeRequests[i].Channel == candidate.Channel)
                                {
                                    channelBusy = true;
                                    break;
                                }
                            }
                            if (!channelBusy)
                            {
                                request = candidate;
                                // Rimuove l'elemento selezionato mantenendo l'ordine delle
                                // altre richieste, incluso l'ordine sullo stesso DO.
                                for (int shift = offset; shift < _outputPulseCount - 1; shift++)
                                {
                                    int to = (_outputPulseHead + shift) % _outputPulseQueue.Length;
                                    int from = (_outputPulseHead + shift + 1) % _outputPulseQueue.Length;
                                    _outputPulseQueue[to] = _outputPulseQueue[from];
                                }
                                _outputPulseTail = (_outputPulseTail - 1 + _outputPulseQueue.Length) % _outputPulseQueue.Length;
                                _outputPulseQueue[_outputPulseTail] = null;
                                _outputPulseCount--;
                                break;
                            }
                        }
                    }
                    if (request == null) break;

                    try
                    {
                        if (channelsNeedingLowReset.Contains(request.Channel))
                        {
                            if (!TryWriteOutputCore(request.Channel, request.InactiveState, out string resetFailure))
                            {
                                request.Completion.TrySetException(
                                    new OutputPulseException(request.Channel, OutputPulsePhase.Rise,
                                        "previous-fall-not-confirmed; low-reset=" + resetFailure));
                                continue;
                            }
                            channelsNeedingLowReset.Remove(request.Channel);
                            _logger.Warn($"IO_PULSE_CHANNEL_LOW_RESET|channel={request.Channel}|reason=previous fall failed");
                        }

                        if (!TryWriteOutputCore(request.Channel, request.ActiveState, out string riseFailure))
                        {
                            // Nessun fronte in scheda: riporta cache e linea allo stato inattivo
                            // e segnala al chiamante che la camera non ha ricevuto il trigger.
                            if (!TryWriteOutputCore(request.Channel, request.InactiveState, out string resetFailure))
                            {
                                channelsNeedingLowReset.Add(request.Channel);
                                _logger.Error($"IO_PULSE_FAILSAFE_LOW_FAILED|channel={request.Channel}|detail={resetFailure}");
                            }
                            request.Completion.TrySetException(
                                new OutputPulseException(request.Channel, OutputPulsePhase.Rise, riseFailure));
                            continue;
                        }

                        long raised = Stopwatch.GetTimestamp();
                        activeRequests[activeCount] = request;
                        raisedTimestamps[activeCount] = raised;
                        deadlines[activeCount] = raised +
                            (long)Math.Ceiling(request.PulseMilliseconds * (double)Stopwatch.Frequency / 1000.0);
                        activeCount++;
                        startedAny = true;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            if (!TryWriteOutputCore(request.Channel, request.InactiveState, out string resetFailure))
                            {
                                channelsNeedingLowReset.Add(request.Channel);
                                _logger.Error($"IO_PULSE_FAILSAFE_LOW_FAILED|channel={request.Channel}|detail={resetFailure}");
                            }
                        }
                        catch (Exception resetEx) { _logger.Error(resetEx, $"IO_PULSE_FAILSAFE_LOW_FAILED|channel={request.Channel}"); }
                        request.Completion.TrySetException(ex);
                    }
                }

                if (activeCount == 0)
                {
                    _outputPulseSignal.WaitOne(20);
                    continue;
                }

                long earliestDeadline = deadlines[0];
                for (int i = 1; i < activeCount; i++)
                    if (deadlines[i] < earliestDeadline) earliestDeadline = deadlines[i];
                long remainingTicks = earliestDeadline - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0 || startedAny) continue;
                double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
                if (remainingMs > 3.0)
                    _outputPulseSignal.WaitOne(Math.Max(1, Math.Min(20, (int)remainingMs - 2)));
                else
                    Thread.SpinWait(64);
            }

            // Arresto: riporta LOW ogni canale attivo prima di annullare le richieste.
            for (int i = 0; i < activeCount; i++)
            {
                var active = activeRequests[i];
                try
                {
                    if (!TryWriteOutputCore(active.Channel, active.InactiveState, out string resetFailure))
                    {
                        _logger.Error($"IO_PULSE_FAILSAFE_LOW_FAILED|channel={active.Channel}|detail={resetFailure}");
                    }
                }
                catch (Exception ex) { _logger.Error(ex, $"IO_PULSE_FAILSAFE_LOW_FAILED|channel={active.Channel}"); }
                active.Completion.TrySetCanceled();
            }

            lock (_outputPulseQueueLock)
            {
                while (_outputPulseCount > 0)
                {
                    var pending = _outputPulseQueue[_outputPulseHead];
                    _outputPulseQueue[_outputPulseHead] = null;
                    _outputPulseHead = (_outputPulseHead + 1) % _outputPulseQueue.Length;
                    _outputPulseCount--;
                    pending?.Completion.TrySetCanceled();
                }
            }
        }

        public async Task WriteOutputsAsync(byte[] values)
        {
            // Scrive su PCIE-1756: contratto legacy da 8 byte; porte 0..3 = DO0-DO31, byte 4..7 ignorati
            if (values.Length != Pcie1756ChannelMap.LegacyPortBufferLength)
                throw new ArgumentException("Array deve contenere 8 byte per PCIE-1756 (porte 0..3 = DO0-DO31; byte 4..7 ignorati)");

            lock (_outputWriteLock)
            {
                Array.Copy(values, _currentOutputs1756, Pcie1756ChannelMap.LegacyPortBufferLength);

                // I byte 4..7 non corrispondono a porte reali: la cache resta a 0
                Array.Clear(_currentOutputs1756, Pcie1756ChannelMap.DoPortCount, _currentOutputs1756.Length - Pcie1756ChannelMap.DoPortCount);

                if (!_isSimulationMode && _doCtrl1756 != null)
                {
                    try
                    {
                        ErrorCode error = _doCtrl1756.Write(0, Pcie1756ChannelMap.DoPortCount, _currentOutputs1756);

                        if (error != ErrorCode.Success)
                        {
                            _logger.Warn($"Errore nella scrittura degli output PCIE-1756: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Errore nella scrittura degli output PCIE-1756: {ex.Message}");
                    }
                }
            }

            _logger.Info("Tutti gli output PCIE-1756 aggiornati");
        }

        public Task ConfigureEncoderCompareTableAsync(int channel, IReadOnlyList<int> targetCounts, bool arm)
        {
            if (channel < 0 || channel >= _activeEncoderChannels.Length)
                throw new ArgumentOutOfRangeException(nameof(channel));
            if (targetCounts == null || targetCounts.Count == 0)
                throw new ArgumentException("At least one encoder compare target is required.", nameof(targetCounts));
            if (targetCounts.Count > 1024)
                throw new ArgumentException("Encoder compare table exceeds the guarded 1024-target limit.", nameof(targetCounts));

            var orderedTargets = targetCounts.ToArray();
            for (int i = 1; i < orderedTargets.Length; i++)
            {
                if (orderedTargets[i] <= orderedTargets[i - 1])
                    throw new ArgumentException("Encoder compare targets must be strictly increasing.", nameof(targetCounts));
            }

            if (!arm)
            {
                _logger.Info($"PCIE1884_COMPARE_PLAN_VALID|channel={channel}|targets={orderedTargets.Length}" +
                    $"|first={orderedTargets[0]}|last={orderedTargets[orderedTargets.Length - 1]}|armed=False");
                return Task.CompletedTask;
            }
            if (!SupportsHardwareEncoderCompare)
                throw new InvalidOperationException("PCIE-1884 hardware compare is not available on the active connection.");

            lock (_lock)
            {
                ErrorCode clearResult = _udCounterCtrl.CompareClear(channel);
                if (clearResult != ErrorCode.Success)
                    throw new InvalidOperationException($"PCIE-1884 CompareClear failed: {clearResult}");
                ErrorCode setResult = _udCounterCtrl.CompareSetTable(channel, orderedTargets.Length, orderedTargets);
                if (setResult != ErrorCode.Success)
                    throw new InvalidOperationException($"PCIE-1884 CompareSetTable failed: {setResult}");
            }

            _logger.Warn($"PCIE1884_COMPARE_ARMED|channel={channel}|targets={orderedTargets.Length}" +
                $"|first={orderedTargets[0]}|last={orderedTargets[orderedTargets.Length - 1]}" +
                "|wiring=verify-COUT-to-camera-trigger");
            return Task.CompletedTask;
        }

        public async Task<byte[]> ReadAllOutputsAsync()
        {
            lock (_outputWriteLock)
            {
                return (byte[])_currentOutputs1756.Clone();
            }
        }

        public void ConfigureActiveEncoderChannels(IEnumerable<int> activeChannels)
        {
            var selected = new bool[4];

            if (activeChannels != null)
            {
                foreach (var channel in activeChannels)
                {
                    if (channel >= 0 && channel < selected.Length)
                    {
                        selected[channel] = true;
                    }
                }
            }

            if (!selected.Any(value => value))
            {
                selected[0] = true;
            }

            lock (_lock)
            {
                for (int i = 0; i < selected.Length; i++)
                {
                    _activeEncoderChannels[i] = selected[i];
                    if (!selected[i])
                    {
                        _encoderSamplingStarted[i] = false;
                        _encoderFrequencyHz[i] = 0.0;
                        _lastSoftwareFrequencySample[i] = 0;
                        _lastSoftwareFrequencyCount[i] = 0;
                    }
                }
            }

            _logger.Info($"Active encoder channels configured: {string.Join(",", selected.Select((active, index) => active ? index.ToString() : null).Where(item => item != null))}");
        }

        public async Task<long> ReadEncoderAsync(int channel)
        {
            if (channel < 0 || channel >= 4)
                throw new ArgumentOutOfRangeException(nameof(channel));

            if (!_activeEncoderChannels[channel])
                return _encoderValues[channel];

            if (_isSimulationMode)
            {
                UpdateSimulationEncoderState();
                return _encoderValues[channel];
            }

            try
            {
                if (_udCounterCtrl != null)
                {
                    ErrorCode error = ErrorCode.Success;
                    int count = 0;
                    lock (_lock)
                    {
                        SelectCounterChannel(channel);
                        try
                        {
                            error = _udCounterCtrl.Read(out count);
                            if (error != ErrorCode.Success)
                            {
                                count = _udCounterCtrl.Value;
                                error = ErrorCode.Success;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (IsAdvantechFunctionBusy(ex))
                            {
                                _logger.Debug($"Encoder read busy on channel {channel}; keeping last count {_encoderValues[channel]}.");
                                return _encoderValues[channel];
                            }

                            _logger.Warn($"Encoder read failed for channel {channel}: {ex.Message}");
                            error = ErrorCode.ErrorHandleNotValid;
                        }
                    }

                    if (error == ErrorCode.Success)
                    {
                        if (count == 0 && _lastEncoderRawSamples[channel] != 0)
                        {
                            // Board transient: returns 0 after a non-zero value during init or
                            // VisionPro startup. Keep the last known good value.
                            // Do NOT accumulate (the old delta-mode logic caused exponential growth).
                            _logger.Debug($"ENCODER_TRANSIENT_ZERO|ch={channel}|keeping={_encoderValues[channel]}");
                        }
                        else if (count != 0)
                        {
                            _encoderValues[channel] = (long)count;
                        }

                        _lastEncoderRawSamples[channel] = count;
                        return _encoderValues[channel];
                    }
                    else
                    {
                        _logger.Warn($"Errore nella lettura dell'encoder {channel}: {error}");
                    }
                }

                return _encoderValues[channel];
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nella lettura dell'encoder {channel}: {ex.Message}");
                return _encoderValues[channel];
            }
        }

        private async Task<double> ReadEncoderFrequencyAsync(int channel)
        {
            if (channel < 0 || channel >= 4)
                throw new ArgumentOutOfRangeException(nameof(channel));

            if (!_activeEncoderChannels[channel])
            {
                _encoderFrequencyHz[channel] = 0.0;
                return 0.0;
            }

            if (_isSimulationMode)
            {
                if (channel != 0 || !_simulationEncoderRunning)
                {
                    _encoderFrequencyHz[channel] = 0.0;
                    return 0.0;
                }

                var countsPerSecond = Math.Abs(_simulationEncoderSpeedMmPerSecond * _simulationCountsPerMillimeter);
                _encoderFrequencyHz[channel] = countsPerSecond;
                return countsPerSecond;
            }

            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastFrequencyReadUtc[channel]).TotalMilliseconds < EncoderFrequencyMinimumReadIntervalMs)
                {
                    return _encoderFrequencyHz[channel];
                }

                if (_freqMeterCtrl != null)
                {
                    ErrorCode error;
                    double value = 0.0;
                    lock (_lock)
                    {
                        SelectFreqMeterChannel(channel);
                        error = _freqMeterCtrl.Read(out value);
                    }

                    if (error == ErrorCode.Success)
                    {
                        _lastFrequencyReadUtc[channel] = now;
                        _encoderFrequencyHz[channel] = Math.Max(0.0, value);
                        return _encoderFrequencyHz[channel];
                    }

                    if (error == ErrorCode.ErrorFuncBusy)
                    {
                        if ((now - _lastFrequencyBusyLogUtc[channel]).TotalMilliseconds >= EncoderFrequencyBusyLogThrottleMs)
                        {
                            _lastFrequencyBusyLogUtc[channel] = now;
                            _logger.Debug($"Frequency meter busy on encoder {channel}; keeping last frequency value {_encoderFrequencyHz[channel]:0.###} Hz");
                        }

                        return _encoderFrequencyHz[channel];
                    }

                    _lastFrequencyReadUtc[channel] = now;
                    _logger.Warn($"Errore nella lettura frequency encoder {channel}: {error}");
                }

                return _encoderFrequencyHz[channel];
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nella lettura frequency encoder {channel}: {ex.Message}");
                return _encoderFrequencyHz[channel];
            }
        }

        public async Task ResetEncoderAsync(int channel)
        {
            if (_isSimulationMode)
            {
                _encoderValues[channel] = 0;
                return;
            }

            try
            {
                if (_udCounterCtrl != null)
                {
                    ErrorCode error;
                    lock (_lock)
                    {
                        SelectCounterChannel(channel);
                        // Usa il metodo ValueReset() per resettare il contatore
                        error = _udCounterCtrl.ValueReset();
                    }
                   // ErrorCode error = _udCounterCtrl.Channels[channel].Reset();

                    if (error != ErrorCode.Success)
                    {
                        _logger.Warn($"Errore nel reset dell'encoder {channel}: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel reset dell'encoder {channel}: {ex.Message}");
            }

            _encoderValues[channel] = 0;
            _encoderFrequencyHz[channel] = 0.0;
            _lastSoftwareFrequencySample[channel] = 0;
            _lastSoftwareFrequencyCount[channel] = 0;
            _lastEncoderRawSamples[channel] = 0;
            _encoderReadAsDeltaMode[channel] = false;
        }

        public async Task SetEncoderPresetAsync(int channel, long preset)
        {
            if (_isSimulationMode)
            {
                _encoderValues[channel] = preset;
                return;
            }

            try
            {
                // Per impostare un preset, usiamo CompareSet
                if (_udCounterCtrl != null)
                {
                    ErrorCode error;
                    lock (_lock)
                    {
                        SelectCounterChannel(channel);
                        // Imposta il valore di comparazione
                        _udCounterCtrl.Channels[channel].InitialValue = (int)preset;
                        error = _udCounterCtrl.ValueReset();
                    }

                    if (error != ErrorCode.Success)
                    {
                        _logger.Warn($"Errore nell'impostazione del preset per l'encoder {channel}: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'impostazione del preset per l'encoder {channel}: {ex.Message}");
            }
        }

    
        public async Task StartEncoderAsync(int channel)
        {
            if (_isSimulationMode)
                return;

            if (channel < 0 || channel >= 4)
                throw new ArgumentOutOfRangeException(nameof(channel));

            try
            {
                if (_udCounterCtrl != null)
                {
                    if (_encoderSamplingStarted[channel])
                    {
                        SetEncoderChannelActiveInMemory(channel, true);
                        _logger.Debug($"Encoder {channel} startup skipped: already armed for continuous read.");
                        return;
                    }

                    bool armed = false;
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        armed = TrySetEncoderChannelActive(channel, true, out bool functionBusy);
                        if (armed)
                        {
                            break;
                        }

                        if (!functionBusy)
                        {
                            break;
                        }

                        _logger.Debug($"Encoder {channel} hardware busy during startup attempt {attempt}; retrying without raising an event.");
                        await Task.Delay(150).ConfigureAwait(false);
                    }

                    if (!armed)
                    {
                        _logger.Debug($"Encoder {channel} startup left unchanged because Advantech counter is busy. Polling will keep the last configured state.");
                        SetEncoderChannelActiveInMemory(channel, true);
                        return;
                    }

                    _encoderSamplingStarted[channel] = true;
                    _lastEncoderRawSamples[channel] = 0;
                    _encoderReadAsDeltaMode[channel] = false;
                    await ReadEncoderAsync(channel);
                    _logger.Info($"Encoder {channel} armed for continuous read. Enabled={_udCounterCtrl.Enabled}, Running={_udCounterCtrl.Running}, Value={_udCounterCtrl.Value}");
                }
            }
            catch (Exception ex) when (IsAdvantechFunctionBusy(ex))
            {
                SetEncoderChannelActiveInMemory(channel, true);
                _logger.Debug($"Encoder {channel} startup ignored because Advantech returned ErrorFuncBusy. The channel remains configured for polling.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'avvio dell'encoder {channel}: {ex.Message}");
            }
        }

        public async Task StopEncoderAsync(int channel)
        {
            if (_isSimulationMode)
            {
                _simulationEncoderRunning = false;
                return;
            }

            try
            {
                if (_udCounterCtrl != null)
                {
                    if (TrySetEncoderChannelActive(channel, false, out bool functionBusy))
                    {
                        _encoderSamplingStarted[channel] = false;
                    }
                    else if (functionBusy)
                    {
                        _logger.Debug($"Encoder {channel} stop ignored because Advantech returned ErrorFuncBusy.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nello stop dell'encoder {channel}: {ex.Message}");
            }
        }

        public void ConfigureSimulationEncoder(double speedMmPerSecond, double countsPerMillimeter, bool isRunning)
        {
            _simulationEncoderSpeedMmPerSecond = Math.Max(0.0, speedMmPerSecond);
            _simulationCountsPerMillimeter = countsPerMillimeter > 0 ? countsPerMillimeter : 20.48;
            _simulationEncoderRunning = isRunning;
            _simulationEncoderLastUpdateUtc = DateTime.UtcNow;
            LogMessage?.Invoke(this, $"Sim encoder configured: speed={_simulationEncoderSpeedMmPerSecond:0.0} mm/s, counts/mm={_simulationCountsPerMillimeter:0.000}, running={_simulationEncoderRunning}");
        }

        public async Task SimulateEncoderAdvanceAsync(double millimeters)
        {
            if (!_isSimulationMode)
            {
                return;
            }

            var oldValue = _encoderValues[0];
            var deltaCounts = (long)Math.Round(millimeters * _simulationCountsPerMillimeter, MidpointRounding.AwayFromZero);
            _encoderValues[0] += deltaCounts;
            _simulationEncoderLastUpdateUtc = DateTime.UtcNow;

            EnqueueEncoderSample(0, _encoderValues[0], _encoderValues[0] - oldValue,
                _simulationEncoderRunning ? Math.Abs(_simulationEncoderSpeedMmPerSecond * _simulationCountsPerMillimeter) : 0.0,
                DateTime.Now);

            await Task.CompletedTask;
        }

        public async Task SetSimulationInputStateAsync(int channel, bool value)
        {
            if (!_isSimulationMode)
            {
                return;
            }

            if (channel >= 100 && channel <= 103)
            {
                var bit = channel - 100;
                if (value)
                {
                    _lastInputs1884 |= (byte)(1 << bit);
                }
                else
                {
                    _lastInputs1884 &= (byte)~(1 << bit);
                }
            }
            else
            {
                if (!Pcie1756ChannelMap.IsValidDiChannel(channel))
                {
                    throw new ArgumentOutOfRangeException(nameof(channel));
                }

                var port = channel / 8;
                var bit = channel % 8;
                if (value)
                {
                    _lastInputs1756[port] |= (byte)(1 << bit);
                }
                else
                {
                    _lastInputs1756[port] &= (byte)~(1 << bit);
                }
            }

            InputChanged?.Invoke(this, new IOEvent
            {
                Channel = channel,
                NewState = value,
                Timestamp = DateTime.Now,
                DeviceType = channel >= 100 ? "PCIE-1884" : "PCIE-1756"
            });

            SaveSimulationState();
            await Task.CompletedTask;
        }

        public async Task PulseSimulationInputAsync(int channel, int pulseMs)
        {
            if (!_isSimulationMode)
            {
                return;
            }

            await SetSimulationInputStateAsync(channel, true);
            await Task.Delay(Math.Max(20, pulseMs));
            await SetSimulationInputStateAsync(channel, false);
        }

        private void UpdateSimulationEncoderState()
        {
            var now = DateTime.UtcNow;
            var deltaSeconds = (now - _simulationEncoderLastUpdateUtc).TotalSeconds;
            _simulationEncoderLastUpdateUtc = now;

            if (!_simulationEncoderRunning || deltaSeconds <= 0)
            {
                return;
            }

            var deltaCounts = (long)Math.Round(_simulationEncoderSpeedMmPerSecond * deltaSeconds * _simulationCountsPerMillimeter, MidpointRounding.AwayFromZero);
            if (deltaCounts == 0)
            {
                return;
            }

            var oldValue = _encoderValues[0];
            _encoderValues[0] += deltaCounts;

            EnqueueEncoderSample(0, _encoderValues[0], _encoderValues[0] - oldValue,
                _simulationEncoderRunning ? Math.Abs(_simulationEncoderSpeedMmPerSecond * _simulationCountsPerMillimeter) : 0.0,
                DateTime.Now);
        }

        public async Task UpdateAllInputsAsync(ObservableCollection<IOChannel> inputChannels)
        {
            var inputs = await ReadAllInputsAsync();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < Math.Min(inputChannels.Count, Pcie1756ChannelMap.DiChannelCount); i++)
                {
                    int port = i / 8;
                    int bit = i % 8;
                    bool isActive = (inputs[port] & (1 << bit)) != 0;

                    inputChannels[i].IsActive = isActive;
                    inputChannels[i].State = isActive ? "ON" : "OFF";
                }
            });
        }

        public async Task UpdateAllEncodersAsync(ObservableCollection<EncoderChannel> encoderChannels)
        {
            for (int i = 0; i < Math.Min(encoderChannels.Count, 4); i++)
            {
                if (!_activeEncoderChannels[i])
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        encoderChannels[i].CounterValue = _encoderValues[i];
                        encoderChannels[i].RawPulseFrequencyHz = 0.0;
                        encoderChannels[i].Status = "Not configured";
                    });
                    continue;
                }

                long value = await ReadEncoderAsync(i);
                // Frequency is diagnostic only. Reuse the software estimate maintained by
                // the real-time poll instead of issuing a second BDaq call under _lock.
                double rawFrequency = _encoderFrequencyHz[i];

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    encoderChannels[i].CounterValue = value;
                    encoderChannels[i].RawPulseFrequencyHz = rawFrequency;
                    encoderChannels[i].Status = $"Count: {value}";

                    // Aggiorna colore in base allo stato
                    if (value >= encoderChannels[i].PresetValue && encoderChannels[i].PresetValue > 0)
                    {
                        encoderChannels[i].Status = $"Count: {value} (Preset raggiunto!)";
                    }
                });
            }
        }

        #endregion

        #region Logica personalizzata e gestione eventi

        private IOMappingConfig LoadIOMappingConfig()
        {
            string configPath = System.IO.Path.Combine(
                MainWindow.configManager.Config.Configuration.Recipe_Folder,
                "cfg",
                "io_mapping.xml");

            try
            {
                if (System.IO.File.Exists(configPath))
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(IOMappingConfig));
                    using (var reader = new System.IO.StreamReader(configPath))
                    {
                        var loaded = serializer.Deserialize(reader) as IOMappingConfig;
                        var normalized = NormalizeIOMappingConfig(loaded);
                        _logger.Info(
                            $"IO_MAPPING_LOADED|path={configPath}" +
                            $"|input_actions={normalized.InputActions.Count}" +
                            $"|output_sequences={normalized.OutputSequences.Count}" +
                            $"|counter_triggers={normalized.CounterTriggers.Count}");
                        return normalized;
                    }
                }

                _logger.Info(
                    $"IO_MAPPING_NOT_CONFIGURED|path={configPath}" +
                    "|legacy automatic input actions disabled");
            }
            catch (Exception ex)
            {
                _logger.Warn(
                    ex,
                    $"IO_MAPPING_LOAD_FAILED|path={configPath}" +
                    "|legacy automatic input actions disabled");
            }

            // io_mapping.xml is an optional legacy extension. Machine I/O routing is
            // configured elsewhere; an absent file must never energize physical outputs.
            return CreateEmptyIOMappingConfig();
        }

        private static IOMappingConfig NormalizeIOMappingConfig(IOMappingConfig config)
        {
            if (config == null)
            {
                return CreateEmptyIOMappingConfig();
            }

            if (config.InputActions == null)
            {
                config.InputActions = new Dictionary<int, IOLogic>();
            }

            if (config.OutputSequences == null)
            {
                config.OutputSequences = new Dictionary<int, OutputSequence>();
            }

            if (config.CounterTriggers == null)
            {
                config.CounterTriggers = new List<CounterTrigger>();
            }

            return config;
        }

        private static IOMappingConfig CreateEmptyIOMappingConfig()
        {
            return new IOMappingConfig
            {
                InputActions = new Dictionary<int, IOLogic>(),
                OutputSequences = new Dictionary<int, OutputSequence>(),
                CounterTriggers = new List<CounterTrigger>()
            };
        }

        private void ExecuteInputActions(int channel, bool state)
        {
            try
            {
                if (_inputActions.TryGetValue(channel, out var action))
                {
                    action(this, state);
                }

                if (_mappingConfig.InputActions != null &&
                    _mappingConfig.InputActions.TryGetValue(channel, out var logic))
                {
                    Task.Run(async () => await ExecuteLogicAsync(logic, state));
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'esecuzione delle azioni per input {channel}: {ex.Message}");
            }
        }

        private async Task ExecuteLogicAsync(IOLogic logic, bool inputState)
        {
            if (logic?.Actions == null)
            {
                return;
            }

            foreach (var action in logic.Actions)
            {
                if (action == null)
                {
                    continue;
                }

                if (ShouldExecuteAction(action, inputState))
                {
                    await Task.Delay(action.DelayMs);

                    switch (action.Type)
                    {
                        case ActionType.SetOutput:
                            await WriteOutputAsync(action.TargetChannel, action.Value);
                            break;

                        case ActionType.PulseOutput:
                            await WriteOutputAsync(action.TargetChannel, true);
                            await Task.Delay(100);
                            await WriteOutputAsync(action.TargetChannel, false);
                            break;

                        case ActionType.ToggleOutput:
                            var current = await ReadInputAsync(action.TargetChannel);
                            await WriteOutputAsync(action.TargetChannel, !current);
                            break;

                        case ActionType.ResetCounter:
                            await ResetEncoderAsync(action.TargetChannel);
                            break;
                    }
                }
            }
        }

        private bool ShouldExecuteAction(Action action, bool inputState)
        {
            if (action.Condition == null)
                return true;

            switch (action.Condition.Type)
            {
                case ConditionType.RisingEdge:
                    return inputState;
                case ConditionType.FallingEdge:
                    return !inputState;
                default:
                    return true;
            }
        }

        private async Task CheckCounterTriggersAsync()
        {
            for (int i = 0; i < _encoderValues.Length; i++)
            {
                long currentValue = _encoderValues[i];

                if (_counterTriggers.TryGetValue(i, out var triggers))
                {
                    foreach (var trigger in triggers)
                    {
                        if (currentValue >= trigger.Key)
                        {
                            trigger.Value(this);
                            triggers.TryRemove(trigger.Key, out _);
                        }
                    }
                }
            }
        }

        private void ExecuteCustomLogic()
        {
            if (_mappingConfig.OutputSequences != null)
            {
                foreach (var sequence in _mappingConfig.OutputSequences.Values)
                {
                    // Implementa sequenze di output
                }
            }
        }

        // La fase encoder-read misura separatamente il driver BDaq e l'accodamento nei due
        // ring buffer. Le callback applicative non vengono mai eseguite sul thread di polling.
        private double _lastEncoderHardwareMs;
        private double _lastEncoderDispatchMs;

        private async Task ReadAllEncodersAsync()
        {
            _lastEncoderHardwareMs = 0.0;
            _lastEncoderDispatchMs = 0.0;

            for (int i = 0; i < 4; i++)
            {
                if (!_activeEncoderChannels[i])
                {
                    continue;
                }

                long oldValue = _encoderValues[i];

                long hardwareStarted = Stopwatch.GetTimestamp();
                long newValue = await ReadEncoderAsync(i);
                _lastEncoderHardwareMs +=
                    (Stopwatch.GetTimestamp() - hardwareStarted) * 1000.0 / Stopwatch.Frequency;

                double oldFrequency = _encoderFrequencyHz[i];
                double newFrequency = UpdateSoftwareEncoderFrequency(i, newValue);

                if (oldValue != newValue || Math.Abs(newFrequency - oldFrequency) >= 0.1)
                {
                    long dispatchStarted = Stopwatch.GetTimestamp();
                    EnqueueEncoderSample(i, newValue, newValue - oldValue, newFrequency, DateTime.Now);
                    _lastEncoderDispatchMs +=
                        (Stopwatch.GetTimestamp() - dispatchStarted) * 1000.0 / Stopwatch.Frequency;
                }
            }
        }

        private double UpdateSoftwareEncoderFrequency(int channel, long currentCount)
        {
            long now = Stopwatch.GetTimestamp();
            long previousTimestamp = _lastSoftwareFrequencySample[channel];
            if (previousTimestamp == 0)
            {
                _lastSoftwareFrequencySample[channel] = now;
                _lastSoftwareFrequencyCount[channel] = currentCount;
                return _encoderFrequencyHz[channel];
            }

            double elapsedMs = (now - previousTimestamp) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < EncoderSoftwareFrequencySampleIntervalMs)
            {
                return _encoderFrequencyHz[channel];
            }

            long delta = currentCount - _lastSoftwareFrequencyCount[channel];
            double instantaneousHz = Math.Abs(delta) * 1000.0 / elapsedMs;
            double previousHz = _encoderFrequencyHz[channel];
            _encoderFrequencyHz[channel] = previousHz <= 0.0
                ? instantaneousHz
                : (previousHz * 0.70) + (instantaneousHz * 0.30);
            if (delta == 0 && elapsedMs >= EncoderFrequencyMinimumReadIntervalMs)
            {
                _encoderFrequencyHz[channel] = 0.0;
            }

            _lastSoftwareFrequencySample[channel] = now;
            _lastSoftwareFrequencyCount[channel] = currentCount;
            return _encoderFrequencyHz[channel];
        }

        public void AddInputAction(int inputChannel, System.Action<IIODeviceManager, bool> action)
        {
            _inputActions.AddOrUpdate(inputChannel, action, (k, v) => action);
        }

        public void AddCounterTrigger(int counterChannel, long triggerValue, System.Action<IIODeviceManager> action)
        {
            var triggers = _counterTriggers.GetOrAdd(counterChannel,
                k => new ConcurrentDictionary<long, System.Action<IIODeviceManager>>());

            triggers[triggerValue] = action;
        }

        #endregion

        #region IDisposable Support

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _isDisposing = true;
            _criticalEncoderSignal.Set();
            _telemetryEncoderSignal.Set();
            _outputPulseSignal.Set();

            if (disposing)
            {
                try
                {
                    _pollingCts?.Cancel();

                    if (_pollingTask != null && !_pollingTask.IsCompleted)
                    {
                        _pollingTask.Wait(1000);
                    }
                }
                catch (Exception ex) { _logger.Warn(ex, "DISPOSE_POLLING_WAIT_FAILED — non-critical"); }

                try { _criticalEncoderDispatchTask?.Wait(1000); } catch { }
                try { _telemetryEncoderDispatchTask?.Wait(1000); } catch { }
                try { _outputPulseTask?.Wait(1000); } catch { }

                try { _diCtrl1756?.Dispose(); } catch { }
                try { _doCtrl1756?.Dispose(); } catch { }
                try { _diCtrl1884?.Dispose(); } catch { }
                try { _doCtrl1884?.Dispose(); } catch { }
                try { _udCounterCtrl?.Dispose(); } catch { }
                try { _freqMeterCtrl?.Dispose(); } catch { }
                try { _timerPulseCtrl?.Dispose(); } catch { }

                if (_isSimulationMode)
                {
                    try { SaveSimulationState(); } catch { }
                }

                try { _pollingCts?.Dispose(); } catch { }
                try { _criticalEncoderSignal.Dispose(); } catch { }
                try { _telemetryEncoderSignal.Dispose(); } catch { }
                try { _outputPulseSignal.Dispose(); } catch { }
            }

            _isInitialized = false;
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

    }
}














