using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace QtisVisionPanel.Models
{
    public class IOChannel : INotifyPropertyChanged
    {
        private int _channelNumber;
        public int ChannelNumber
        {
            get => _channelNumber;
            set { _channelNumber = value; OnPropertyChanged(); }
        }

        private string _channelName;
        public string ChannelName
        {
            get => _channelName;
            set { _channelName = value; OnPropertyChanged(); }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveBrush)); }
        }

        private string _state;
        public string State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(); }
        }

        private string _stateText;
        public string StateText
        {
            get => _stateText;
            set { _stateText = value; OnPropertyChanged(); }
        }

        private Brush _activeBrush = Brushes.Red;
        public Brush ActiveBrush
        {
            get => IsActive ? Brushes.LimeGreen : Brushes.Red;
            set { _activeBrush = value; OnPropertyChanged(); }
        }

        private IOType _type;
        public IOType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum IOType
    {
        DigitalInput,
        DigitalOutput,
        EncoderInput
    }

    public class EncoderChannel : INotifyPropertyChanged
    {
        private int _channelNumber;
        public int ChannelNumber
        {
            get => _channelNumber;
            set { _channelNumber = value; OnPropertyChanged(); }
        }

        private string _channelName;
        public string ChannelName
        {
            get => _channelName;
            set { _channelName = value; OnPropertyChanged(); }
        }

        private long _counterValue;
        public long CounterValue
        {
            get => _counterValue;
            set { _counterValue = value; OnPropertyChanged(); }
        }

        private string _status;
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private EncoderMode _mode;
        public EncoderMode Mode
        {
            get => _mode;
            set { _mode = value; OnPropertyChanged(); }
        }

        private int _presetValue;
        public int PresetValue
        {
            get => _presetValue;
            set { _presetValue = value; OnPropertyChanged(); }
        }

        private bool _isCounting;
        public bool IsCounting
        {
            get => _isCounting;
            set { _isCounting = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusBrush)); }
        }

        private double _speedMetersPerMinute;
        public double SpeedMetersPerMinute
        {
            get => _speedMetersPerMinute;
            set
            {
                _speedMetersPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedDisplay));
            }
        }

        private double _rawSpeedMetersPerMinute;
        public double RawSpeedMetersPerMinute
        {
            get => _rawSpeedMetersPerMinute;
            set
            {
                _rawSpeedMetersPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RawSpeedDisplay));
            }
        }

        private long _lastDeltaCounts;
        public long LastDeltaCounts
        {
            get => _lastDeltaCounts;
            set
            {
                _lastDeltaCounts = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DeltaCountsDisplay));
            }
        }

        private double _lastSampleMilliseconds;
        public double LastSampleMilliseconds
        {
            get => _lastSampleMilliseconds;
            set
            {
                _lastSampleMilliseconds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SamplePeriodDisplay));
            }
        }

        private double _countsPerMillimeter;
        public double CountsPerMillimeter
        {
            get => _countsPerMillimeter;
            set
            {
                _countsPerMillimeter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CountsPerMillimeterDisplay));
            }
        }

        private double _estimatedPiecesPerMinute;
        public double EstimatedPiecesPerMinute
        {
            get => _estimatedPiecesPerMinute;
            set
            {
                _estimatedPiecesPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PiecesPerMinuteDisplay));
            }
        }

        private double _rawPulseFrequencyHz;
        public double RawPulseFrequencyHz
        {
            get => _rawPulseFrequencyHz;
            set
            {
                _rawPulseFrequencyHz = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PulseFrequencyDisplay));
            }
        }

        private string _diagnosticState = "Idle";
        public string DiagnosticState
        {
            get => _diagnosticState;
            set { _diagnosticState = value; OnPropertyChanged(); }
        }

        public string SpeedDisplay => $"{SpeedMetersPerMinute:0.00} m/min";
        public string RawSpeedDisplay => $"{RawSpeedMetersPerMinute:0.000} m/min raw";
        public string DeltaCountsDisplay => $"{LastDeltaCounts:+#;-#;0} counts";
        public string SamplePeriodDisplay => LastSampleMilliseconds <= 0.0 ? "Campione: n/d" : $"Campione: {LastSampleMilliseconds:0} ms";
        public string CountsPerMillimeterDisplay => CountsPerMillimeter <= 0.0 ? "Counts/mm: n/d" : $"Counts/mm: {CountsPerMillimeter:0.###}";
        public string PiecesPerMinuteDisplay => $"{EstimatedPiecesPerMinute:0.0} pz/min";
        public string PulseFrequencyDisplay => $"{RawPulseFrequencyHz:0.###} Hz raw";

        public Brush StatusBrush => IsCounting ? Brushes.LimeGreen : Brushes.Gray;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum EncoderMode
    {
        PulseDirection,
        UpDown,
        Quadrature,
        CW_CCW
    }

    public class IOLogEntry : INotifyPropertyChanged
    {
        private DateTime _timestamp;
        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(); }
        }

        private string _message;
        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }

        private IOLogLevel _level;
        public IOLogLevel Level
        {
            get => _level;
            set { _level = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelBrush)); }
        }

        public Brush LevelBrush
        {
            get
            {

                switch (Level)
                {
                    case IOLogLevel.Info:
                        return Brushes.Black;
                    case IOLogLevel.Warning:
                        return Brushes.Orange;
                    case IOLogLevel.Error:
                        return Brushes.Red;
                    case IOLogLevel.Success:
                        return Brushes.Green;
                    default:
                        return Brushes.Gray;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum IOLogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }
}


