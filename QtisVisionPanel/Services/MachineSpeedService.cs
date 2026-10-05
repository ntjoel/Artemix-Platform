using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QtisVisionPanel.Services
{
    public sealed class MachineSpeedService : INotifyPropertyChanged
    {
        private double _metersPerMinute;
        private string _source = "None";
        private DateTime _lastUpdateUtc = DateTime.MinValue;

        public event PropertyChangedEventHandler PropertyChanged;

        public double MetersPerMinute
        {
            get => _metersPerMinute;
            private set
            {
                if (Math.Abs(_metersPerMinute - value) < 0.0001)
                {
                    return;
                }

                _metersPerMinute = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MetersPerMinuteText));
                OnPropertyChanged(nameof(HasRecentValue));
            }
        }

        public string Source
        {
            get => _source;
            private set
            {
                if (string.Equals(_source, value, StringComparison.Ordinal))
                {
                    return;
                }

                _source = value;
                OnPropertyChanged();
            }
        }

        public DateTime LastUpdateUtc
        {
            get => _lastUpdateUtc;
            private set
            {
                _lastUpdateUtc = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRecentValue));
            }
        }

        public string MetersPerMinuteText => $"{MetersPerMinute:0.00}";

        public bool HasRecentValue => LastUpdateUtc != DateTime.MinValue &&
                                      (DateTime.UtcNow - LastUpdateUtc).TotalSeconds <= 5.0;

        public void Publish(double metersPerMinute, string source)
        {
            MetersPerMinute = Math.Max(0.0, metersPerMinute);
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            LastUpdateUtc = DateTime.UtcNow;
        }

        public void Clear(string source)
        {
            if (!string.IsNullOrWhiteSpace(source) &&
                !string.Equals(Source, source, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            MetersPerMinute = 0.0;
            Source = "None";
            LastUpdateUtc = DateTime.MinValue;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
