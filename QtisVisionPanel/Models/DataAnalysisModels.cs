using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace QtisVisionPanel.Models
{
    public enum DataAnalysisCardType
    {
        ProductionOverview,
        DefectPie,
        HeightTrend,
        ThreeDHeightTrend,
        WidthTrend,
        LengthTrend,
        RejectRateTrend,
        ProductionRateTrend
    }

    public class DataAnalysisBarItem
    {
        public string Label { get; set; }
        public int Value { get; set; }
        public double Percentage { get; set; }
        public double Width { get; set; }
        public Brush Fill { get; set; }
    }

    public class DataAnalysisDefectItem
    {
        public string Label { get; set; }
        public int Value { get; set; }
        public double Percentage { get; set; }
        public Geometry SliceGeometry { get; set; }
        public Brush Fill { get; set; }
    }

    public class DataAnalysisTrendBucket
    {
        public DateTime Timestamp { get; set; }
        public double Average { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public int SampleCount { get; set; }
    }

    public class DataAnalysisTrendCard : INotifyPropertyChanged
    {
        private string _title;
        private string _subtitle;
        private string _valueColumn;
        private string _nominalPolylinePoints = string.Empty;
        private string _lowerTolerancePolylinePoints = string.Empty;
        private string _upperTolerancePolylinePoints = string.Empty;
        private string _averagePolylinePoints = string.Empty;
        private string _minimumPolylinePoints = string.Empty;
        private string _maximumPolylinePoints = string.Empty;
        private Geometry _averagePathData;
        private Geometry _minimumPathData;
        private Geometry _maximumPathData;
        private Geometry _averageAreaPathData;
        private bool _showMinMax = true;
        private string _valueUnit = string.Empty;
        private string _summaryText = "N/D";
        private double _toleranceBandTop;
        private double _toleranceBandHeight;
        private bool _hasReferenceBand;
        private bool _isVisible;

        public event PropertyChangedEventHandler PropertyChanged;

        public DataAnalysisTrendCard()
        {
            XAxisLabels = new ObservableCollection<DataAnalysisAxisLabel>();
            YAxisLabels = new ObservableCollection<DataAnalysisAxisLabel>();
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title == value)
                    return;

                _title = value;
                OnPropertyChanged();
            }
        }

        public string Subtitle
        {
            get => _subtitle;
            set
            {
                if (_subtitle == value)
                    return;

                _subtitle = value;
                OnPropertyChanged();
            }
        }

        public string ValueColumn
        {
            get => _valueColumn;
            set
            {
                if (_valueColumn == value)
                    return;

                _valueColumn = value;
                OnPropertyChanged();
            }
        }

        public DataAnalysisCardType CardType { get; set; }

        public ObservableCollection<DataAnalysisAxisLabel> XAxisLabels { get; }

        public ObservableCollection<DataAnalysisAxisLabel> YAxisLabels { get; }

        public string NominalPolylinePoints
        {
            get => _nominalPolylinePoints;
            set
            {
                if (_nominalPolylinePoints == value)
                    return;

                _nominalPolylinePoints = value;
                OnPropertyChanged();
            }
        }

        public string LowerTolerancePolylinePoints
        {
            get => _lowerTolerancePolylinePoints;
            set
            {
                if (_lowerTolerancePolylinePoints == value)
                    return;

                _lowerTolerancePolylinePoints = value;
                OnPropertyChanged();
            }
        }

        public string UpperTolerancePolylinePoints
        {
            get => _upperTolerancePolylinePoints;
            set
            {
                if (_upperTolerancePolylinePoints == value)
                    return;

                _upperTolerancePolylinePoints = value;
                OnPropertyChanged();
            }
        }

        public string AveragePolylinePoints
        {
            get => _averagePolylinePoints;
            set
            {
                if (_averagePolylinePoints == value)
                    return;

                _averagePolylinePoints = value;
                OnPropertyChanged();
            }
        }

        public string MinimumPolylinePoints
        {
            get => _minimumPolylinePoints;
            set
            {
                if (_minimumPolylinePoints == value)
                    return;

                _minimumPolylinePoints = value;
                OnPropertyChanged();
            }
        }

        public string MaximumPolylinePoints
        {
            get => _maximumPolylinePoints;
            set
            {
                if (_maximumPolylinePoints == value)
                    return;

                _maximumPolylinePoints = value;
                OnPropertyChanged();
            }
        }

        public Geometry AveragePathData
        {
            get => _averagePathData;
            set { _averagePathData = value; OnPropertyChanged(); }
        }

        public Geometry MinimumPathData
        {
            get => _minimumPathData;
            set { _minimumPathData = value; OnPropertyChanged(); }
        }

        public Geometry MaximumPathData
        {
            get => _maximumPathData;
            set { _maximumPathData = value; OnPropertyChanged(); }
        }

        public Geometry AverageAreaPathData
        {
            get => _averageAreaPathData;
            set { _averageAreaPathData = value; OnPropertyChanged(); }
        }

        public bool ShowMinMax
        {
            get => _showMinMax;
            set { _showMinMax = value; OnPropertyChanged(); }
        }

        public string ValueUnit
        {
            get => _valueUnit;
            set { _valueUnit = value; OnPropertyChanged(); }
        }

        public string SummaryText
        {
            get => _summaryText;
            set
            {
                if (_summaryText == value)
                    return;

                _summaryText = value;
                OnPropertyChanged();
            }
        }

        public double ToleranceBandTop
        {
            get => _toleranceBandTop;
            set
            {
                if (Math.Abs(_toleranceBandTop - value) < 0.001)
                    return;

                _toleranceBandTop = value;
                OnPropertyChanged();
            }
        }

        public double ToleranceBandHeight
        {
            get => _toleranceBandHeight;
            set
            {
                if (Math.Abs(_toleranceBandHeight - value) < 0.001)
                    return;

                _toleranceBandHeight = value;
                OnPropertyChanged();
            }
        }

        public bool HasReferenceBand
        {
            get => _hasReferenceBand;
            set
            {
                if (_hasReferenceBand == value)
                    return;

                _hasReferenceBand = value;
                OnPropertyChanged();
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value)
                    return;

                _isVisible = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DataAnalysisAxisLabel
    {
        public string Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class DataAnalysisRecipeOption
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    public class DataAnalysisAvailableCardOption
    {
        public string Label { get; set; }
        public DataAnalysisCardType CardType { get; set; }
    }
}
