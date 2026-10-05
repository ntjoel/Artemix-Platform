using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace QtisVisionPanel.ViewModels
{
    public class RecipeItem : INotifyPropertyChanged
    {
        private string _name;
        private string _description;
        private string _productImage;
        private DateTime _lastModified;
        private bool _isSelected;
        private double _productLength;
        private double _productWidth;
        private int _id;
        private string _recipeType;
        private double _diameter;
        private bool _isInProduction;
        private RecipeStatus _status;
        private string _dimensions;
        private string _imagePath;
        private double _productHeight;

        public double ProductHeight
        {
            get => _productHeight;
            set
            {
                _productHeight = value;
                OnPropertyChanged();
            }
        }
        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }
        public RecipeStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string ProductImage
        {
            get => _productImage;
            set { _productImage = value; OnPropertyChanged(); }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set { _lastModified = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public double ProductLength
        {
            get => _productLength;
            set { _productLength = value; OnPropertyChanged(); }
        }

        public double ProductWidth
        {
            get => _productWidth;
            set { _productWidth = value; OnPropertyChanged(); }
        }

        public double Diameter
        {
            get => _diameter;
            set { _diameter = value; OnPropertyChanged(); }
        }

        public string RecipeType
        {
            get => _recipeType;
            set { _recipeType = value; OnPropertyChanged(); }
        }
        public bool IsInProduction
        {
            get => _isInProduction;
            set { _isInProduction = value; OnPropertyChanged(); }
        }
        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(); }
        }
        public string Dimensions
        {
            get => _dimensions;
            set { _dimensions = value; OnPropertyChanged(); }
        }
        public string FilePath { get; set; }
        public string XmlFilePath { get; set; }
        public bool ExistsInDatabase { get; set; }
        public long? DatabaseId { get; set; }
        // NUOVO: Proprietà per l'immagine cached
        private BitmapImage _cachedImage;
        public BitmapImage CachedImage
        {
            get => _cachedImage;
            set
            {
                _cachedImage = value;
                OnPropertyChanged();
            }
        }

        // Propr
        // NUOVO: Proprietà attached per l'immagine cached
        public static readonly DependencyProperty CachedImageProperty =
            DependencyProperty.RegisterAttached("CachedImage", typeof(BitmapImage),
                typeof(RecipeItem));
      
        // NUOVO: Flag per indicare se è già stato precaricato
        public bool IsPreloaded { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public enum RecipeStatus
    {
        Normal,
        InProduction,
        Selected
    }
}