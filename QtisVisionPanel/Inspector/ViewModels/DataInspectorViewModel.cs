using QtisVisionPanel.Inspector.Abstractions;
using QtisVisionPanel.Inspector.Models;
using QtisVisionPanel.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QtisVisionPanel.ServerMessage;

namespace QtisVisionPanel.Inspector.ViewModels
{
    /// <summary>
    /// Presentation model for the native DataInspector page.
    ///
    /// Responsibilities:
    /// - query recent rejected pieces from MySQL
    /// - fall back to rejected pieces with saved evidence when needed
    /// - resolve source/processed images from PieceData paths
    /// - expose localized labels and summaries to the WPF view
    /// </summary>
    public sealed class DataInspectorViewModel : INotifyPropertyChanged
    {
        private readonly IPieceHistoryRepository _pieceHistoryRepository;
        private readonly IImageEvidenceResolver _imageEvidenceResolver;
        private readonly IDataSourceHealthProvider _dataSourceHealthProvider;
        private PieceCardViewModel _selectedPiece;
        private ImageArtifactViewModel _selectedSourceImage;
        private ImageArtifactViewModel _selectedProcessedImage;
        private int _recentRejectCount = 10;
        private readonly string _dataSourceMode;
        private readonly string _basePrototypeNote;
        private string _prototypeNote;
        private string _activeReviewMode = "recent";
        private string _connectionStatusText;
        private Brush _connectionStatusBrush = new SolidColorBrush(Color.FromRgb(218, 159, 25));
        private bool _isZoomOverlayVisible;
        private BitmapImage _zoomedImage;
        private double _zoomLevel = 1.0;

        /// <summary>
        /// Creates the inspector view model from repository/resolver services
        /// already prepared by the hosting view.
        /// </summary>
        public DataInspectorViewModel(
            IPieceHistoryRepository pieceHistoryRepository,
            IDataSourceHealthProvider dataSourceHealthProvider,
            IImageEvidenceResolver imageEvidenceResolver,
            string dataSourceMode,
            string prototypeNote)
        {
            _pieceHistoryRepository = pieceHistoryRepository;
            _dataSourceHealthProvider = dataSourceHealthProvider;
            _imageEvidenceResolver = imageEvidenceResolver;
            _dataSourceMode = dataSourceMode;
            _basePrototypeNote = prototypeNote;
            _prototypeNote = prototypeNote;
            _connectionStatusText = L("Sub_entry_DataInspectorChecking", "Checking");

            RefreshCommand = new RelayCommand(() => { var _ = LoadAsync(); });
            SelectRecentRejectWindowCommand = new RelayCommand<string>(count =>
            {
                int parsedCount;
                if (int.TryParse(count, out parsedCount) && parsedCount > 0)
                    RecentRejectCount = parsedCount;
            });
            OpenSourceZoomCommand = new RelayCommand(
                () => OpenZoom(SelectedSourceImage?.Image),
                () => SelectedSourceImage?.Image != null);
            OpenProcessedZoomCommand = new RelayCommand(
                () => OpenZoom(SelectedProcessedImage?.Image),
                () => SelectedProcessedImage?.Image != null);
            CloseZoomCommand = new RelayCommand(() => { IsZoomOverlayVisible = false; });
            ZoomInCommand = new RelayCommand(() => { ZoomLevel = Math.Min(8.0, ZoomLevel + 0.25); });
            ZoomOutCommand = new RelayCommand(() => { ZoomLevel = Math.Max(0.25, ZoomLevel - 0.25); });
            ResetZoomCommand = new RelayCommand(() => { ZoomLevel = 1.0; });
            DownloadEvidenceCommand = new RelayCommand(
                () => { var _ = DownloadEvidenceAsync(); },
                () => CanDownloadEvidence);

            var __ = LoadAsync();
        }

        public ObservableCollection<PieceCardViewModel> RecentRejectedPieces { get; } = new ObservableCollection<PieceCardViewModel>();
        public ObservableCollection<ImageArtifactViewModel> SourceImages { get; } = new ObservableCollection<ImageArtifactViewModel>();
        public ObservableCollection<ImageArtifactViewModel> ProcessedImages { get; } = new ObservableCollection<ImageArtifactViewModel>();
        public ObservableCollection<string> SelectedInspectionDescriptors { get; } = new ObservableCollection<string>();

        public string InspectorTitle { get { return L("Sub_entry_DataInspectorNativeTitle", "QuatisVision Inspector"); } }
        public string ViewModeLabel { get { return L("Sub_entry_DataInspectorViewMode", "View mode"); } }
        public string DatabaseLabel { get { return L("Sub_entry_DatabaseStatus", "Database"); } }
        public string RecentRejectsTitle { get { return L("Sub_entry_DataInspectorRecentRejects", "Recent Rejects"); } }
        public string RefreshLabel { get { return L("Sub_entry_Refresh", "Refresh"); } }
        public string Last1Label { get { return string.Format(L("Sub_entry_DataInspectorLastN", "Last {0}"), 1); } }
        public string Last5Label { get { return string.Format(L("Sub_entry_DataInspectorLastN", "Last {0}"), 5); } }
        public string Last10Label { get { return string.Format(L("Sub_entry_DataInspectorLastN", "Last {0}"), 10); } }
        public string Last20Label { get { return string.Format(L("Sub_entry_DataInspectorLastN", "Last {0}"), 20); } }
        public string ReviewWindowLabel { get { return L("Sub_entry_DataInspectorReviewWindow", "Review window"); } }
        public string ItemsOnScreenLabel { get { return L("Sub_entry_DataInspectorItemsOnScreen", "Items on screen"); } }
        public string SelectedItemLabel { get { return L("Sub_entry_DataInspectorSelectedItem", "Selected item"); } }
        public string ImagesAvailableLabel { get { return L("Sub_entry_DataInspectorImagesAvailable", "Images available"); } }
        public string ImagesMissingLabel { get { return L("Sub_entry_DataInspectorImagesMissing", "Images missing"); } }
        public string RecipeLabel { get { return L("Sub_entry_RecipeLabel", "Recipe"); } }
        public string ImageStatusLabel { get { return L("Sub_entry_DataInspectorImageStatus", "Image status"); } }
        public string OperatorLabel { get { return L("Sub_entry_DataInspectorOperator", "Operator"); } }
        public string ItemDetailsLabel { get { return L("Sub_entry_DataInspectorItemDetails", "Item details"); } }
        public string StorageDetailsLabel { get { return L("Sub_entry_DataInspectorStorageDetails", "Storage details"); } }
        public string SourceImagesLabel { get { return L("Sub_entry_DataInspectorSourceImages", "Source images"); } }
        public string ProcessedImagesLabel { get { return L("Sub_entry_DataInspectorProcessedImages", "Processed images"); } }
        public string ChooseImageHint { get { return L("Sub_entry_DataInspectorChooseImage", "Choose an image below"); } }
        public string NoImageSelectedLabel { get { return L("Sub_entry_DataInspectorNoImageSelected", "No image selected"); } }
        public string NoPreviewLabel { get { return L("Sub_entry_DataInspectorNoPreview", "No preview"); } }
        public string SelectedSourceImageHint { get { return SelectedSourceImage?.SecondaryLabel ?? ChooseImageHint; } }
        public string SelectedProcessedImageHint { get { return SelectedProcessedImage?.SecondaryLabel ?? ChooseImageHint; } }

        public string DataSourceMode { get { return _dataSourceMode; } }
        public string PieceCountSummary { get { return string.Format(L("Sub_entry_DataInspectorItemsReady", "{0} items ready to review"), RecentRejectedPieces.Count); } }
        public string RejectWindowSummary { get { return string.Format(L("Sub_entry_DataInspectorShowingLatestRejected", "Showing the latest {0} rejected items"), RecentRejectCount); } }
        public string CurrentFilterLabel
        {
            get
            {
                string baseLabel = string.Format(L("Sub_entry_DataInspectorLastN", "Last {0}"), RecentRejectCount);
                return _activeReviewMode == "with-images"
                    ? baseLabel + " • " + L("Sub_entry_DataInspectorImagesAvailable", "Images available")
                    : baseLabel;
            }
        }
        public string EvidenceCoverageSummary { get { return string.Format(L("Sub_entry_DataInspectorSavedImagesSummary", "{0} with saved images"), RecentRejectedPieces.Count(p => !string.IsNullOrWhiteSpace(p.Piece.PieceDataPath))); } }
        public string MissingEvidenceSummary { get { return string.Format(L("Sub_entry_DataInspectorMissingImagesSummary", "{0} waiting for images"), RecentRejectedPieces.Count(p => string.IsNullOrWhiteSpace(p.Piece.PieceDataPath))); } }
        public string PrototypeNote { get { return _prototypeNote; } }
        public string ConnectionStatusText { get { return _connectionStatusText; } }
        public Brush ConnectionStatusBrush { get { return _connectionStatusBrush; } }

        public string EvidenceSummary
        {
            get
            {
                return SelectedPiece == null
                    ? L("Sub_entry_DataInspectorSelectItemToReview", "Select an item to review")
                    : string.Format(L("Sub_entry_DataInspectorEvidenceSummary", "{0} original / {1} inspected"), SourceImages.Count, ProcessedImages.Count);
            }
        }

        public bool IsSourceImageMissing { get { return SelectedSourceImage == null || SelectedSourceImage.Image == null; } }
        public bool IsProcessedImageMissing { get { return SelectedProcessedImage == null || SelectedProcessedImage.Image == null; } }

        public bool IsZoomOverlayVisible
        {
            get { return _isZoomOverlayVisible; }
            private set { SetProperty(ref _isZoomOverlayVisible, value); }
        }

        public BitmapImage ZoomedImage
        {
            get { return _zoomedImage; }
            private set { SetProperty(ref _zoomedImage, value); }
        }

        public double ZoomLevel
        {
            get { return _zoomLevel; }
            set
            {
                if (SetProperty(ref _zoomLevel, value))
                    OnPropertyChanged(nameof(ZoomPercent));
            }
        }

        public string ZoomPercent { get { return string.Format("{0}%", (int)(_zoomLevel * 100)); } }
        public bool CanDownloadEvidence { get { return SelectedPiece != null && (SourceImages.Count > 0 || ProcessedImages.Count > 0); } }
        public string ZoomOverlayTitleLabel { get { return L("Sub_entry_DataInspectorZoomTitle", "Image Detail"); } }
        public string ZoomClickHint { get { return L("Sub_entry_DataInspectorZoomHint", "Click to zoom"); } }
        public string DownloadEvidenceLabel { get { return L("Sub_entry_DataInspectorDownload", "Scarica evidenza"); } }

        public int RecentRejectCount
        {
            get { return _recentRejectCount; }
            private set
            {
                if (SetProperty(ref _recentRejectCount, value))
                {
                    OnPropertyChanged(nameof(CurrentFilterLabel));
                    OnPropertyChanged(nameof(RejectWindowSummary));
                    var _ = LoadAsync();
                }
            }
        }

        public PieceCardViewModel SelectedPiece
        {
            get { return _selectedPiece; }
            set
            {
                if (SetProperty(ref _selectedPiece, value))
                {
                    OnPropertyChanged(nameof(CanDownloadEvidence));
                    _ = LoadEvidenceAsync();
                }
            }
        }

        public ImageArtifactViewModel SelectedSourceImage
        {
            get { return _selectedSourceImage; }
            set
            {
                if (SetProperty(ref _selectedSourceImage, value))
                {
                    OnPropertyChanged(nameof(IsSourceImageMissing));
                    OnPropertyChanged(nameof(SelectedSourceImageHint));
                }
            }
        }

        public ImageArtifactViewModel SelectedProcessedImage
        {
            get { return _selectedProcessedImage; }
            set
            {
                if (SetProperty(ref _selectedProcessedImage, value))
                {
                    OnPropertyChanged(nameof(IsProcessedImageMissing));
                    OnPropertyChanged(nameof(SelectedProcessedImageHint));
                }
            }
        }

        public ICommand RefreshCommand { get; private set; }
        public ICommand SelectRecentRejectWindowCommand { get; private set; }
        public ICommand OpenSourceZoomCommand { get; private set; }
        public ICommand OpenProcessedZoomCommand { get; private set; }
        public ICommand CloseZoomCommand { get; private set; }
        public ICommand ZoomInCommand { get; private set; }
        public ICommand ZoomOutCommand { get; private set; }
        public ICommand ResetZoomCommand { get; private set; }
        public ICommand DownloadEvidenceCommand { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OpenZoom(BitmapImage image)
        {
            if (image == null) return;
            _zoomedImage = image;
            _zoomLevel = 1.0;
            OnPropertyChanged(nameof(ZoomedImage));
            OnPropertyChanged(nameof(ZoomLevel));
            OnPropertyChanged(nameof(ZoomPercent));
            IsZoomOverlayVisible = true;
        }

        private async Task DownloadEvidenceAsync()
        {
            if (SelectedPiece == null) return;

            string destFolder;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = L("Sub_entry_DataInspectorDownloadChooseFolder", "Selezionare la cartella di destinazione");
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                destFolder = Path.Combine(
                    dialog.SelectedPath,
                    string.Format("{0:yyyyMMdd_HHmmss}_{1}", SelectedPiece.Piece.Timestamp, SelectedPiece.Piece.PieceId));
            }

            try
            {
                Directory.CreateDirectory(destFolder);

                foreach (var artifact in SourceImages)
                {
                    if (artifact.Artifact?.FullPath != null && File.Exists(artifact.Artifact.FullPath))
                    {
                        string dest = Path.Combine(destFolder, "src_" + Path.GetFileName(artifact.Artifact.FullPath));
                        await Task.Run(() => File.Copy(artifact.Artifact.FullPath, dest, overwrite: true));
                    }
                }

                foreach (var artifact in ProcessedImages)
                {
                    if (artifact.Artifact?.FullPath != null && File.Exists(artifact.Artifact.FullPath))
                    {
                        string dest = Path.Combine(destFolder, "proc_" + Path.GetFileName(artifact.Artifact.FullPath));
                        await Task.Run(() => File.Copy(artifact.Artifact.FullPath, dest, overwrite: true));
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("=== QtisVision Evidence Export ===");
                sb.AppendLine(string.Format("Timestamp:    {0:yyyy-MM-dd HH:mm:ss}", SelectedPiece.Piece.Timestamp));
                sb.AppendLine(string.Format("Piece ID:     {0}", SelectedPiece.Piece.PieceId));
                sb.AppendLine(string.Format("Recipe:       {0}", SelectedPiece.Piece.RecipeName));
                sb.AppendLine(string.Format("Operator:     {0}", SelectedPiece.Piece.OperatorName));
                sb.AppendLine(string.Format("Result:       {0}", SelectedPiece.Piece.IsRejected ? "REJECTED" : "OK"));
                sb.AppendLine(string.Format("Data path:    {0}", SelectedPiece.Piece.PieceDataPath));
                sb.AppendLine();
                sb.AppendLine("=== Inspection Results ===");
                foreach (var result in SelectedPiece.Piece.InspectionResults)
                    sb.AppendLine(string.Format("  {0}: {1}", result.DisplayName, result.Status));

                string detailPath = Path.Combine(destFolder, "detail.txt");
                await Task.Run(() => File.WriteAllText(detailPath, sb.ToString(), Encoding.UTF8));

                MainWindow.logger?.Info(
                    "DATAINSPECTOR_DOWNLOAD|pieceId={0}|dest={1}",
                    SelectedPiece.Piece.PieceId, destFolder);

                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + destFolder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error(ex, "DATAINSPECTOR_DOWNLOAD_FAILED|pieceId={0}", SelectedPiece?.Piece?.PieceId);
                System.Windows.MessageBox.Show(
                    L("Sub_entry_DataInspectorDownloadErrorMessage", "Esportazione fallita:") + "\n" + ex.Message,
                    L("Sub_entry_DataInspectorDownloadErrorTitle", "Errore esportazione"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Reloads the piece list and selects the first meaningful item.
        /// If the latest rejected window has no saved evidence, the method falls
        /// back to the most recent rejected items that do contain image evidence.
        /// </summary>
        private async Task LoadAsync()
        {
            await RefreshConnectionStatusAsync();

            try
            {
                var pieces = await _pieceHistoryRepository.GetRecentRejectedPiecesAsync(RecentRejectCount);
                _activeReviewMode = "recent";

                if (pieces.Count > 0 && pieces.All(p => string.IsNullOrWhiteSpace(p.PieceDataPath)))
                {
                    var fallbackPieces = await _pieceHistoryRepository.QueryPiecesAsync(new PieceHistoryQuery
                    {
                        OnlyRejectedPieces = true,
                        RequireSavedImages = true,
                        MaxResults = RecentRejectCount
                    });

                    if (fallbackPieces.Count > 0)
                    {
                        pieces = fallbackPieces;
                        _activeReviewMode = "with-images";
                        _prototypeNote = _basePrototypeNote + " " +
                            L("Sub_entry_DataInspectorSavedImagesModeNote", "No saved evidence was found in the latest rejected pieces. Showing the most recent rejected items with saved images.");
                    }
                    else
                    {
                        _prototypeNote = _basePrototypeNote + " " +
                            L("Sub_entry_DataInspectorNoSavedImagesInWindow", "The current rejected window has no saved image evidence.");
                    }
                }
                else
                {
                    _prototypeNote = _basePrototypeNote;
                }

                OnPropertyChanged(nameof(PrototypeNote));

                RecentRejectedPieces.Clear();
                foreach (var piece in pieces.Select(p => new PieceCardViewModel(p)))
                    RecentRejectedPieces.Add(piece);

                SelectedPiece = RecentRejectedPieces.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Piece.PieceDataPath))
                    ?? RecentRejectedPieces.FirstOrDefault();
            }
            catch (System.Exception ex)
            {
                RecentRejectedPieces.Clear();
                SourceImages.Clear();
                ProcessedImages.Clear();
                SelectedInspectionDescriptors.Clear();
                SelectedPiece = null;
                SelectedSourceImage = null;
                SelectedProcessedImage = null;
                _activeReviewMode = "recent";

                _prototypeNote = _basePrototypeNote + " Live data read failed: " + ex.Message;
                OnPropertyChanged(nameof(PrototypeNote));
            }

            OnPropertyChanged(nameof(PieceCountSummary));
            OnPropertyChanged(nameof(RejectWindowSummary));
            OnPropertyChanged(nameof(CurrentFilterLabel));
            OnPropertyChanged(nameof(EvidenceCoverageSummary));
            OnPropertyChanged(nameof(MissingEvidenceSummary));
            OnPropertyChanged(nameof(SelectedSourceImageHint));
            OnPropertyChanged(nameof(SelectedProcessedImageHint));
        }

        /// <summary>
        /// Updates the small connection-status indicator shown by the inspector.
        /// </summary>
        private async Task RefreshConnectionStatusAsync()
        {
            bool isConnected = await _dataSourceHealthProvider.CheckConnectionAsync();

            if (isConnected)
            {
                _connectionStatusText = L("Sub_entry_DataInspectorConnected", "Connected");
                _connectionStatusBrush = new SolidColorBrush(Color.FromRgb(40, 148, 92));
            }
            else
            {
                _connectionStatusText = L("Sub_entry_DataInspectorNotConnected", "Not connected");
                _connectionStatusBrush = new SolidColorBrush(Color.FromRgb(206, 64, 64));
            }

            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(ConnectionStatusBrush));
        }

        /// <summary>
        /// Loads source and processed image evidence for the currently selected piece.
        /// The method also refreshes traceability and inspection descriptor details.
        /// </summary>
        private async Task LoadEvidenceAsync()
        {
            SourceImages.Clear();
            ProcessedImages.Clear();
            SelectedInspectionDescriptors.Clear();
            SelectedSourceImage = null;
            SelectedProcessedImage = null;

            if (SelectedPiece == null)
            {
                OnPropertyChanged(nameof(EvidenceSummary));
                return;
            }

            try
            {
                PieceEvidenceBundle bundle = await _imageEvidenceResolver.ResolveAsync(SelectedPiece.Piece);
                SelectedPiece.UpdateTraceability(bundle);

                foreach (var inspection in SelectedPiece.Piece.InspectionResults)
                    SelectedInspectionDescriptors.Add(inspection.DisplayName + ": " + inspection.Status);

                foreach (var artifact in bundle.SourceImages.Select(a => new ImageArtifactViewModel(a)))
                    SourceImages.Add(artifact);

                foreach (var artifact in bundle.ProcessedImages.Select(a => new ImageArtifactViewModel(a)))
                    ProcessedImages.Add(artifact);

                SelectedSourceImage = SourceImages.FirstOrDefault();
                SelectedProcessedImage = ProcessedImages.FirstOrDefault();

                MainWindow.logger?.Info(
                    "DATAINSPECTOR_EVIDENCE_RESOLVED|pieceId={0}|folder={1}|folder_exists={2}|source={3}|processed={4}|other={5}|notes={6}",
                    SelectedPiece.Piece.PieceId,
                    bundle.PieceFolderPath,
                    bundle.FolderExists,
                    bundle.SourceImages.Count,
                    bundle.ProcessedImages.Count,
                    bundle.OtherImages.Count,
                    bundle.ResolutionNotes);
            }
            catch (System.Exception ex)
            {
                string pieceId = SelectedPiece != null ? SelectedPiece.Piece.PieceId.ToString() : "n/a";
                string piecePath = SelectedPiece != null ? SelectedPiece.Piece.PieceDataPath : string.Empty;

                MainWindow.logger?.Error(
                    ex,
                    "DATAINSPECTOR_EVIDENCE_FAILED|pieceId={0}|path={1}",
                    pieceId,
                    piecePath);

                SelectedPiece.UpdateTraceability(new PieceEvidenceBundle
                {
                    PieceKey = pieceId,
                    PieceFolderPath = piecePath ?? string.Empty,
                    FolderExists = false,
                    ResolutionNotes = "Image evidence could not be loaded."
                });
            }

            OnPropertyChanged(nameof(EvidenceSummary));
            OnPropertyChanged(nameof(PieceCountSummary));
            OnPropertyChanged(nameof(SelectedPiece));
            OnPropertyChanged(nameof(EvidenceCoverageSummary));
            OnPropertyChanged(nameof(MissingEvidenceSummary));
            OnPropertyChanged(nameof(SelectedSourceImageHint));
            OnPropertyChanged(nameof(SelectedProcessedImageHint));
            OnPropertyChanged(nameof(CanDownloadEvidence));
        }

        /// <summary>
        /// Small MVVM helper used by the inspector state properties.
        /// </summary>
        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Localized string lookup shortcut used throughout the inspector UI.
        /// </summary>
        private static string L(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }
    }
}
