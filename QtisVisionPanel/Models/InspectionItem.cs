using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QtisVisionPanel.Models
{
    public class InspectionItem : INotifyPropertyChanged
    {
        private string _name;
        private bool _isEnabled;
        private string _propertyName;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public string PropertyName
        {
            get => _propertyName;
            set { _propertyName = value; OnPropertyChanged(); }
        }

        private string _cameraViews;

        /// <summary>
        /// Viste camera che eseguono questa ispezione, come etichetta leggibile
        /// (es. "SIDE / LEFT"). Deriva dalla matrice vista x ispezione configurata in
        /// Inspection Configuration. Serve a raggruppare le spunte nella pagina ricetta e a
        /// rendere evidente QUALI camere applicano il controllo.
        ///
        /// Nota: il flag della ricetta resta uno solo per ispezione (e' una proprieta' del
        /// prodotto, non della camera). Per questo l'ispezione compare una volta sola, sotto
        /// l'etichetta di tutte le viste che la eseguono, invece di comparire una volta per
        /// vista con spunte che scriverebbero tutte lo stesso valore.
        /// </summary>
        public string CameraViews
        {
            get => _cameraViews;
            set { _cameraViews = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
