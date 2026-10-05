using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QtisVisionPanel.Models
{
    public enum PowerFlex525ParameterGroup
    {
        Monitor,
        Diagnostics,
        Motor,
        RampLimit,
        Modified
    }

    public class PowerFlex525ParameterDefinition
    {
        public int Number { get; set; }
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public string Unit { get; set; }
        public bool IsEditable { get; set; }
        public bool RequiresStop { get; set; }
        public PowerFlex525ParameterGroup Group { get; set; }
    }

    public class PowerFlex525ParameterValue
    {
        public int Number { get; set; }
        public double? Value { get; set; }
        public string RawValue { get; set; }
        public bool IsAvailable { get; set; }
        public string Error { get; set; }
    }

    public class PowerFlex525ParameterItem : INotifyPropertyChanged
    {
        private double? _currentValue;
        private string _editValue;
        private bool _isAvailable;
        private string _error;

        public event PropertyChangedEventHandler PropertyChanged;

        public PowerFlex525ParameterDefinition Definition { get; set; }

        public int Number => Definition?.Number ?? 0;
        public string Code => Definition?.Code;
        public string DisplayName => Definition?.DisplayName;
        public string Unit => Definition?.Unit;
        public bool IsEditable => Definition?.IsEditable == true;
        public bool RequiresStop => Definition?.RequiresStop == true;
        public PowerFlex525ParameterGroup Group => Definition?.Group ?? PowerFlex525ParameterGroup.Monitor;

        public double? CurrentValue
        {
            get => _currentValue;
            set
            {
                if (_currentValue == value)
                    return;

                _currentValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentValueText));
                OnPropertyChanged(nameof(HasPendingChange));
                OnPropertyChanged(nameof(PendingChangeText));
            }
        }

        public string EditValue
        {
            get => _editValue;
            set
            {
                if (_editValue == value)
                    return;

                _editValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPendingChange));
                OnPropertyChanged(nameof(PendingChangeText));
            }
        }

        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (_isAvailable == value)
                    return;

                _isAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string Error
        {
            get => _error;
            set
            {
                if (_error == value)
                    return;

                _error = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string CurrentValueText => CurrentValue.HasValue ? CurrentValue.Value.ToString("0.###") : "--";

        public bool HasPendingChange
        {
            get
            {
                if (!IsEditable || string.IsNullOrWhiteSpace(EditValue))
                    return false;

                if (!double.TryParse(EditValue.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var edited))
                    return false;

                return !CurrentValue.HasValue || Math.Abs(CurrentValue.Value - edited) > 0.0001;
            }
        }

        public string PendingChangeText => HasPendingChange
            ? $"{CurrentValueText} -> {EditValue} {Unit}".Trim()
            : string.Empty;

        public string StatusText
        {
            get
            {
                if (!IsAvailable)
                    return string.IsNullOrWhiteSpace(Error) ? "N/D" : Error;

                return RequiresStop ? "Stop richiesto per modifica" : "OK";
            }
        }

        public void AcceptCurrentAsEditValue()
        {
            EditValue = CurrentValue.HasValue ? CurrentValue.Value.ToString("0.###") : string.Empty;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PowerFlex525ChangeHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string User { get; set; }
        public string Role { get; set; }
        public string DriveIp { get; set; }
        public int ParameterNumber { get; set; }
        public string ParameterCode { get; set; }
        public string ParameterName { get; set; }
        public double? OldValue { get; set; }
        public double? NewValue { get; set; }
        public string Unit { get; set; }
        public string Source { get; set; }
        public string Result { get; set; }
    }

    public class PowerFlex525DriveSnapshot
    {
        public bool IsAvailable { get; set; }
        public bool IsRunning { get; set; }
        public string StatusMessage { get; set; }
        public double? DriveStatusCode { get; set; }
        public int? Fault1Code { get; set; }
        public int? Fault2Code { get; set; }
        public int? Fault3Code { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
