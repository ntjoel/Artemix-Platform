using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QtisVisionPanel.Models
{
    public class CounterData: INotifyPropertyChanged
    {
        private string _header;
        private string _description;
        private long _value;
        private string _displayValue;

        // Canonical lookup key — never localized. Used by UpdateSelectionCounter() to find the right slot.
        public string Key { get; set; }

        public string Header
        {
            get => _header;
            set
            {
                if (_header != value)
                {
                    _header = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
            }
        }
        public long Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayValue
        {
            get => _displayValue;
            set
            {
                if (_displayValue != value)
                {
                    _displayValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class DefectItem : INotifyPropertyChanged
    {
        private long _count;
        private string _lastErrorMessage;
        private string _displayValue;
        public string FeatureKey { get; set; }
        public string Feature { get; set; }
        public string IconPath { get; set; } // Percorso dell'icona
        public string ToolTip { get; set; }

        public long Count
        {
            get => _count;
            set
            {
                if (_count != value)
                {
                    _count = value;
                    OnPropertyChanged();
                }
            }
        }
        public string LastErrorMessage
        {
            get => _lastErrorMessage;
            set { _lastErrorMessage = value; OnPropertyChanged(); }
        }

        public string DisplayValue
        {
            get => _displayValue;
            set
            {
                if (_displayValue != value)
                {
                    _displayValue = value;
                    OnPropertyChanged();
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class InspectionData : INotifyPropertyChanged
    {
        public ObservableCollection<CounterData> _selectionCounters { get; set; } = new ObservableCollection<CounterData>();
        public ObservableCollection<DefectItem> _defectsCounters { get; set; } = new ObservableCollection<DefectItem>();
        public ObservableCollection<CounterData> SelectionCounters
        {
            get => _selectionCounters;
            set
            {
                _selectionCounters = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<DefectItem> DefectsCounters
        {
            get => _defectsCounters;
            set
            {
                _defectsCounters = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
