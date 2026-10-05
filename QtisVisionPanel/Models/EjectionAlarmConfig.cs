
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace QtisVisionPanel.Models
{
    public enum SignalType
    {
        Blocking,
        NotBlocking,
        None
    }

    public enum AlarmType
    {
        ConsecutiveEvents,
        ConsecutiveEventsAlt,
        PercentageInBuffer
    }

    public enum DefectType
    {
        Logo = 0,
        PrintCentering = 1,
        OpenFlaps = 2,
        SurfaceCheck = 3,
        Height = 4,
        SideSealing = 5,
        ShapeTop = 6,
        ShapeSide = 7,
        FrontTraceability = 8,
        ThreeDHeight = 9,
        ThreeDWidth = 10,
        ThreeDLength = 11,
        SideRollCount = 12,
        BottomSealing = 13,
        TrappedPaper = 14,
        AIClassification = 15
    }

    [Serializable]
    public class EjectionAlarmConfig : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Persistent properties ──────────────────────────────────────────────

        private string _name = "New Alarm";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private bool _enabled;
        [XmlElement("Enabled")]
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
        }

        private int _threshold = 5;
        [XmlElement("Threshold")]
        public int Threshold
        {
            get => _threshold;
            set { if (_threshold != value) { _threshold = value; OnPropertyChanged(); } }
        }

        private int _bufferSize = 10;
        [XmlElement("BufferSize")]
        public int BufferSize
        {
            get => _bufferSize;
            set { if (_bufferSize != value) { _bufferSize = value; OnPropertyChanged(); } }
        }

        private SignalType _signalType = SignalType.Blocking;
        [XmlElement("SignalType")]
        public SignalType SignalType
        {
            get => _signalType;
            set { if (_signalType != value) { _signalType = value; OnPropertyChanged(); } }
        }

        [XmlElement("SignalID")]
        public string SignalID { get; set; } = "DO0";

        private int _pulseDurationMs = 0;
        public int PulseDurationMs
        {
            get => _pulseDurationMs;
            set
            {
                if (_pulseDurationMs != value)
                {
                    _pulseDurationMs = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DurationMs));
                }
            }
        }

        public int DurationMs
        {
            get => PulseDurationMs;
            set => PulseDurationMs = value;
        }

        private AlarmType _alarmType = AlarmType.ConsecutiveEvents;
        [XmlElement("AlarmType")]
        public AlarmType AlarmType
        {
            get => _alarmType;
            set { if (_alarmType != value) { _alarmType = value; OnPropertyChanged(); } }
        }

        private List<DefectType> _monitoredDefects = new List<DefectType>();
        [XmlArray("MonitoredDefects")]
        [XmlArrayItem("Defect")]
        public List<DefectType> MonitoredDefects
        {
            get => _monitoredDefects;
            set
            {
                _monitoredDefects = value ?? new List<DefectType>();
                OnPropertyChanged();
                SyncSelectableDefects();
            }
        }

        [XmlIgnore]
        public ObservableCollection<SelectableDefect> SelectableDefects { get; } = new ObservableCollection<SelectableDefect>();

        private bool _syncingDefects;

        private void SyncSelectableDefects()
        {
            _syncingDefects = true;
            foreach (var sd in SelectableDefects)
                sd.PropertyChanged -= OnSelectableDefectChanged;

            SelectableDefects.Clear();
            foreach (DefectType defect in Enum.GetValues(typeof(DefectType)))
            {
                var sd = new SelectableDefect
                {
                    DefectType = defect,
                    IsSelected = _monitoredDefects.Contains(defect)
                };
                SelectableDefects.Add(sd);
            }

            foreach (var sd in SelectableDefects)
                sd.PropertyChanged += OnSelectableDefectChanged;
            _syncingDefects = false;
        }

        private void OnSelectableDefectChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_syncingDefects || e.PropertyName != nameof(SelectableDefect.IsSelected)) return;
            _monitoredDefects = SelectableDefects
                .Where(sd => sd.IsSelected)
                .Select(sd => sd.DefectType)
                .ToList();
            OnPropertyChanged(nameof(MonitoredDefects));
        }

        private string _outputChannelId = "DO0";
        public string OutputChannelId
        {
            get => _outputChannelId;
            set { if (_outputChannelId != value) { _outputChannelId = value; OnPropertyChanged(); } }
        }

        // ── Runtime-only properties (not serialized) ───────────────────────────

        private bool _isTriggered;
        [XmlIgnore]
        public bool IsTriggered
        {
            get => _isTriggered;
            set { if (_isTriggered != value) { _isTriggered = value; OnPropertyChanged(); } }
        }

        [XmlIgnore]
        public DateTime LastTriggered { get; set; }

        private int _triggerCount;
        [XmlIgnore]
        public int TriggerCount
        {
            get => _triggerCount;
            set { if (_triggerCount != value) { _triggerCount = value; OnPropertyChanged(); } }
        }

        // ── Constructor ────────────────────────────────────────────────────────

        public EjectionAlarmConfig()
        {
            _monitoredDefects = new List<DefectType>
            {
                DefectType.Logo,
                DefectType.PrintCentering,
                DefectType.OpenFlaps,
                DefectType.SurfaceCheck,
                DefectType.Height,
                DefectType.SideSealing,
                DefectType.SideRollCount,
                DefectType.ShapeTop,
                DefectType.ShapeSide,
                DefectType.FrontTraceability,
                DefectType.ThreeDHeight,
                DefectType.ThreeDWidth,
                DefectType.ThreeDLength,
                DefectType.BottomSealing,
                DefectType.TrappedPaper,
                DefectType.AIClassification
            };
            SyncSelectableDefects();
        }

        public EjectionAlarmConfig Clone()
        {
            return new EjectionAlarmConfig
            {
                Name = this.Name,
                Enabled = this.Enabled,
                Threshold = this.Threshold,
                BufferSize = this.BufferSize,
                SignalType = this.SignalType,
                SignalID = this.SignalID,
                OutputChannelId = this.OutputChannelId,
                DurationMs = this.DurationMs,
                AlarmType = this.AlarmType,
                MonitoredDefects = new List<DefectType>(this.MonitoredDefects)
            };
        }
    }

    [Serializable]
    [XmlRoot("EjectionAlarmsConfig")]
    public class EjectionAlarmsConfig
    {
        [XmlArray("Alarms")]
        [XmlArrayItem("EjectionAlarmConfig")]
        public List<EjectionAlarmConfig> Alarms { get; set; } = new List<EjectionAlarmConfig>();

        public EjectionAlarmsConfig()
        {
            Alarms.Add(new EjectionAlarmConfig
            {
                Name = "Alarm 1 - Consecutive (N)",
                AlarmType = AlarmType.ConsecutiveEvents,
                Threshold = 5,
                SignalType = SignalType.Blocking,
                SignalID = "DO0",
                OutputChannelId = "DO0",
                Enabled = false
            });
            Alarms.Add(new EjectionAlarmConfig
            {
                Name = "Alarm 2 - Consecutive (M)",
                AlarmType = AlarmType.ConsecutiveEventsAlt,
                Threshold = 10,
                SignalType = SignalType.Blocking,
                SignalID = "DO0",
                OutputChannelId = "DO0",
                Enabled = false
            });
            Alarms.Add(new EjectionAlarmConfig
            {
                Name = "Alarm 3 - Percentage",
                AlarmType = AlarmType.PercentageInBuffer,
                Threshold = 50,
                BufferSize = 10,
                SignalType = SignalType.Blocking,
                SignalID = "DO0",
                OutputChannelId = "DO0",
                Enabled = false
            });
        }
    }

    public class AlarmTriggeredEventArgs : EventArgs
    {
        public EjectionAlarmConfig Alarm { get; set; }
        public Dictionary<DefectType, int> DefectCounts { get; set; }
        public DateTime TriggerTime { get; set; }
        public string Message { get; set; }
    }

    public class AlarmAcknowledgedEventArgs : EventArgs
    {
        public EjectionAlarmConfig Alarm { get; set; }
        public DateTime AcknowledgedAt { get; set; }
        public string AcknowledgedBy { get; set; }
    }
}
