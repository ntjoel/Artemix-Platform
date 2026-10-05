using FontAwesome.WPF;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace QtisVisionPanel.Models
{
    public class MenuItem : INotifyPropertyChanged
    {
        private string _header;
        private FontAwesomeIcon _icon;
        private bool _isExpanded;
        private string _viewName;
        private bool _isSelected;
        private ObservableCollection<MenuItem> _children;
        private ICommand _command;
        private object _commandParameter;
        // public bool HasChildren => Children != null && Children.Count > 0;
        public bool HasChildren
        {
            get { return Children != null && Children.Count > 0; }
        }
        public bool IsExternalCommand { get; set; } = false;
        public string ExternalAppPath { get; set; }
        // NUOVO: Indica se è un header di sezione (non cliccabile)

        public bool IsSectionHeader { get; set; }
        // NUOVA PROPRIETÀ: indica se mostrare il contenuto (expander/button)
        // Per gli header di sezione, deve essere false
        public bool ShowContent => !IsSectionHeader;
        // NUOVO: Colore per l'header di sezione (opzionale, default rosso)
        public string SectionColor { get; set; } = "#FF0000";
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

        public FontAwesomeIcon Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }
        public string ViewName
        {
            get => _viewName;
            set
            {
                if (_viewName != value)
                {
                    _viewName = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<MenuItem> Children
        {
            get
            {
                if (_children == null)
                {
                    _children = new ObservableCollection<MenuItem>();
                }
                return _children;
            }
            set
            {
                if (_children != value)
                {
                    _children = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand Command
        {
            get => _command;
            set
            {
                if (_command != value)
                {
                    _command = value;
                    OnPropertyChanged();
                }
            }
        }

        public object CommandParameter
        {
            get => _commandParameter;
            set
            {
                if (_commandParameter != value)
                {
                    _commandParameter = value;
                    OnPropertyChanged();
                }
            }
        }

        public MenuItem()
        {
            Children = new ObservableCollection<MenuItem>();
            Children.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(HasChildren));
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}