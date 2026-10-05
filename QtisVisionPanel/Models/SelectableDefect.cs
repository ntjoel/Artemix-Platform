using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QtisVisionPanel.Models
{
    public class SelectableDefect : INotifyPropertyChanged
    {
        private DefectType _defectType;
        public DefectType DefectType
        {
            get => _defectType;
            set { _defectType = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string DisplayName
        {
            get
            {
                switch (DefectType)
                {
                    case DefectType.Logo:              return "Logo";
                    case DefectType.PrintCentering:    return "Centratura Stampa";
                    case DefectType.OpenFlaps:         return "Alette Aperte";
                    case DefectType.SurfaceCheck:      return "Controllo Superficie";
                    case DefectType.Height:            return "Altezza";
                    case DefectType.SideSealing:       return "Sigillatura Laterale";
                    case DefectType.SideRollCount:     return "Conteggio Rotoli";
                    case DefectType.ShapeTop:          return "Forma Superiore";
                    case DefectType.ShapeSide:         return "Forma Laterale";
                    case DefectType.FrontTraceability: return "Tracciabilita Front";
                    case DefectType.ThreeDHeight:      return "Altezza 3D";
                    case DefectType.ThreeDWidth:       return "Larghezza 3D";
                    case DefectType.ThreeDLength:      return "Lunghezza 3D";
                    case DefectType.BottomSealing:     return "Saldatura Inferiore";
                    case DefectType.TrappedPaper:      return "Carta Intrappolata";
                    case DefectType.AIClassification:  return "Classificazione AI";
                    default:                           return DefectType.ToString();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
