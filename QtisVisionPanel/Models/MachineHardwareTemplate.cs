using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QtisVisionPanel.Models
{
    public enum MachineSignalDirection
    {
        Input,
        Output,
        Derived
    }

    public enum MachineSignalCategory
    {
        PresenceSensor,
        Lighting,
        CameraTrigger,
        Reject,
        Alarm,
        Encoder,
        Safety,
        Spare
    }

    public enum MachineSignalPolarity
    {
        ActiveHigh,
        ActiveLow,
        NotApplicable
    }

    public class MachineSignalDefinition : INotifyPropertyChanged
    {
        private string _signalCode;
        private string _description;
        private string _board;
        private string _channel;
        private string _notes;
        private MachineSignalDirection _direction;
        private MachineSignalCategory _category;
        private MachineSignalPolarity _polarity;
        private bool _reservedForRealSignal;
        private bool _isRuntimeBound;
        private bool _isAdditionalBinding;
        private string _runtimeUsageLabel;
        private string _signalModeLabel;

        public string SignalCode
        {
            get => _signalCode;
            set { _signalCode = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public MachineSignalDirection Direction
        {
            get => _direction;
            set
            {
                if (_direction == value) return;
                _direction = value;
                OnPropertyChanged();
            }
        }

        public MachineSignalCategory Category
        {
            get => _category;
            set
            {
                if (_category == value) return;
                _category = value;
                OnPropertyChanged();
            }
        }

        public string Board
        {
            get => _board;
            set { _board = value; OnPropertyChanged(); }
        }

        public string Channel
        {
            get => _channel;
            set { _channel = value; OnPropertyChanged(); }
        }

        public MachineSignalPolarity Polarity
        {
            get => _polarity;
            set
            {
                if (_polarity == value) return;
                _polarity = value;
                OnPropertyChanged();
            }
        }

        public bool ReservedForRealSignal
        {
            get => _reservedForRealSignal;
            set { _reservedForRealSignal = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        public bool IsRuntimeBound
        {
            get => _isRuntimeBound;
            set { _isRuntimeBound = value; OnPropertyChanged(); }
        }

        public bool IsAdditionalBinding
        {
            get => _isAdditionalBinding;
            set { _isAdditionalBinding = value; OnPropertyChanged(); }
        }

        public string RuntimeUsageLabel
        {
            get => _runtimeUsageLabel;
            set { _runtimeUsageLabel = value; OnPropertyChanged(); }
        }

        public string SignalModeLabel
        {
            get => _signalModeLabel;
            set { _signalModeLabel = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    public class AdditionalRuntimeBindingDefinition : INotifyPropertyChanged
    {
        private string _bindingCode;
        private string _description;
        private string _signalCode;
        private string _notes;

        public string BindingCode
        {
            get => _bindingCode;
            set { _bindingCode = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string SignalCode
        {
            get => _signalCode;
            set { _signalCode = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class EncoderConfigurationTemplate : INotifyPropertyChanged
    {
        private string _axisName;
        private string _board;
        private string _channel;
        private int _pulsesPerRevolution;
        private double _millimetersPerRevolution;
        private string _machineZeroLabel = "CONVEYOR_START";
        private double _photocellMachineOffsetMm;
        private double _triggerOffsetMm;
        private double _rejectOffsetMm;
        private string _notes;

        public string AxisName
        {
            get => _axisName;
            set { _axisName = value; OnPropertyChanged(); }
        }

        public string Board
        {
            get => _board;
            set { _board = value; OnPropertyChanged(); }
        }

        public string Channel
        {
            get => _channel;
            set { _channel = value; OnPropertyChanged(); }
        }

        public int PulsesPerRevolution
        {
            get => _pulsesPerRevolution;
            set { _pulsesPerRevolution = value; OnPropertyChanged(); }
        }

        public double MillimetersPerRevolution
        {
            get => _millimetersPerRevolution;
            set { _millimetersPerRevolution = value; OnPropertyChanged(); }
        }

        public string MachineZeroLabel
        {
            get => _machineZeroLabel;
            set { _machineZeroLabel = value; OnPropertyChanged(); }
        }

        public double PhotocellMachineOffsetMm
        {
            get => _photocellMachineOffsetMm;
            set { _photocellMachineOffsetMm = value; OnPropertyChanged(); }
        }

        public string TrackingMode { get; set; }

        public double TriggerOffsetMm
        {
            get => _triggerOffsetMm;
            set { _triggerOffsetMm = value; OnPropertyChanged(); }
        }

        public double RejectOffsetMm
        {
            get => _rejectOffsetMm;
            set { _rejectOffsetMm = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MachineInterventionPoint : INotifyPropertyChanged
    {
        private string _pointCode;
        private string _description;
        private bool _enabled = true;
        private string _referenceCode = "PRODUCT_ZERO";
        private double _baseOffsetMm;
        private double _trimOffsetMm;
        private string _actionType = "OutputPulse";
        private string _signalCode;
        private int _pulseMs = 40;
        private string _notes;
        private bool _isStandard;

        public string PointCode
        {
            get => _pointCode;
            set { _pointCode = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public string ReferenceCode
        {
            get => _referenceCode;
            set { _referenceCode = value; OnPropertyChanged(); }
        }

        public double BaseOffsetMm
        {
            get => _baseOffsetMm;
            set { _baseOffsetMm = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveOffsetMm)); }
        }

        public double TrimOffsetMm
        {
            get => _trimOffsetMm;
            set { _trimOffsetMm = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveOffsetMm)); }
        }

        public double EffectiveOffsetMm => BaseOffsetMm + TrimOffsetMm;

        public string ActionType
        {
            get => _actionType;
            set { _actionType = value; OnPropertyChanged(); }
        }

        public string SignalCode
        {
            get => _signalCode;
            set { _signalCode = value; OnPropertyChanged(); }
        }

        public int PulseMs
        {
            get => _pulseMs;
            set { _pulseMs = value; OnPropertyChanged(); }
        }

        public bool IsStandard
        {
            get => _isStandard;
            set { _isStandard = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class InterventionPointDisplayItem
    {
        public int OrderIndex { get; set; }

        public bool Enabled { get; set; }

        public bool IsStandard { get; set; }

        public string PointCode { get; set; }

        public string Description { get; set; }

        public string ActionType { get; set; }

        public string SignalCode { get; set; }

        public double RelativeQuotaMm { get; set; }

        public double AbsoluteMachineQuotaMm { get; set; }

        public string Notes { get; set; }

        public string RelativeQuotaDisplay => $"{RelativeQuotaMm:0.0} mm from PRODUCT_ZERO";

        public string AbsoluteQuotaDisplay => $"{AbsoluteMachineQuotaMm:0.0} mm machine";
    }

    public class InterventionSignalAssignment
    {
        public int OrderIndex { get; set; }

        public bool Enabled { get; set; }

        public string PointCode { get; set; }

        public string Description { get; set; }

        public string ActionType { get; set; }

        public double RelativeQuotaMm { get; set; }

        public double AbsoluteMachineQuotaMm { get; set; }

        public string SignalCode { get; set; }

        public string Board { get; set; }

        public string Channel { get; set; }

        public string Polarity { get; set; }

        public int PulseMs { get; set; }

        public string SignalStatus { get; set; }

        public string AbsoluteQuotaDisplay => $"{AbsoluteMachineQuotaMm:0.0} mm";

        public string RelativeQuotaDisplay => $"{RelativeQuotaMm:0.0} mm";
    }

    public class InterventionRuntimeEvent
    {
        public DateTime Timestamp { get; set; }

        public long ProductId { get; set; }

        public string PointCode { get; set; }

        public string Description { get; set; }

        public string ActionType { get; set; }

        public string SignalCode { get; set; }

        public double OffsetMm { get; set; }

        public string Status { get; set; }

        public string Notes { get; set; }
    }

    public class MachineOperationalEvent
    {
        public DateTime Timestamp { get; set; }

        public string Category { get; set; }

        public string FusionCategory { get; set; }

        public string SourceArea { get; set; }

        public long? ProductId { get; set; }

        public string EventCode { get; set; }

        public string Title { get; set; }

        public string Detail { get; set; }

        public string SignalCode { get; set; }

        public double? MachineQuotaMm { get; set; }

        public string Status { get; set; }

        public string Severity { get; set; }

        public string MainProjectCategory { get; set; }

        public string MainProjectMachineStatus { get; set; }

        public string MainProjectVisionStatus { get; set; }

        public string MainProjectLogId { get; set; }

        public string TimeDisplay => Timestamp.ToString("HH:mm:ss.fff");

        public string ProductDisplay => ProductId.HasValue ? ProductId.Value.ToString() : "-";

        public string QuotaDisplay => MachineQuotaMm.HasValue ? $"{MachineQuotaMm.Value:0.0} mm" : "-";

        public string MainProjectCompatibilityDisplay
        {
            get
            {
                var category = string.IsNullOrWhiteSpace(MainProjectCategory) ? "-" : MainProjectCategory;
                var status = string.IsNullOrWhiteSpace(MainProjectMachineStatus) ? "-" : MainProjectMachineStatus;
                return $"{category} | {status}";
            }
        }
    }

    public class MachineSequenceStep
    {
        public int StepOrder { get; set; }

        public string EventName { get; set; }

        public string Source { get; set; }

        public string Action { get; set; }

        public string Target { get; set; }

        public string Timing { get; set; }

        public string Notes { get; set; }
    }
}


