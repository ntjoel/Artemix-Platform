using Cognex.Vision;
using Cognex.VisionPro;
using Cognex.VisionPro.ImageFile;
using Cognex.VisionPro.ImageProcessing;
using Cognex.VisionPro.PMAlign;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace QtisVisionPanel.RecipeSwitch
{
    public class RecipeAutoSwitcher
    {
        private CogPMAlignTool _matcherTool;
        // Lazy access: avoids NullRef if RecipeAutoSwitcher is constructed before MainWindow.logger is assigned.
        private NLog.Logger _logger => MainWindow.logger;
        // Tolleranza dimensioni (in percentuale)
        private const double SIZE_TOLERANCE_PERCENT = 0.20; // 20% di differenza massima
        private const int MIN_IMAGE_SIZE = 100; // Larghezza/altezza minima in pixel
        public RecipeAutoSwitcher()
        {
            InitializeMatcherTool();
        }
        private void InitializeMatcherTool()
        {
            try
            {
                _matcherTool = new CogPMAlignTool();

                // Configurazione avanzata per PMAlign
                _matcherTool.RunParams.AcceptThreshold = 0.2; // 70% di similarità minima
                _matcherTool.RunParams.ZoneAngle.Configuration = CogPMAlignZoneConstants.LowHigh;
                _matcherTool.RunParams.ZoneAngle.Low = -10.0; // Aumenta il range di rotazione
                _matcherTool.RunParams.ZoneAngle.High = 10.0;
                _matcherTool.RunParams.RunMode= CogPMAlignRunModeConstants.SearchImage;
                _matcherTool.RunParams.RunAlgorithm= CogPMAlignRunAlgorithmConstants.BestTrained;
                // Configurazione per tollerare variazioni di illuminazione
                _matcherTool.RunParams.ContrastThreshold = 5.0; // Abbassa la soglia di contrasto
                _matcherTool.Pattern.IgnorePolarity = true; // Ignora la polarità

                // Configurazione per gestire immagini con poche features
              //  _matcherTool.RunParams.CoverageThreshold = 0.1; // Riduci la copertura richiesta
                _matcherTool.RunParams.ApproximateNumberToFind = 1; // Cerca solo un pattern

                _logger.Info("RecipeAutoSwitcher inizializzato con parametri ottimizzati");
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'inizializzazione del PMAlignTool: {ex.Message}");
                throw;
            }
        }

        public string FindMatchingRecipe(ICogImage currentImage, string recipeImageFolder)
        {
            if (currentImage == null)
            {
                _logger.Warn("Immagine corrente nulla");
                return null;
            }

            if (!Directory.Exists(recipeImageFolder))
            {
                _logger.Error($"Cartella ricette non trovata: {recipeImageFolder}");
                return null;
            }

         

            _logger.Info($"Ricerca ricetta in: {recipeImageFolder}");

            // Estensione per includere anche .vpp se non ci sono immagini
            var imageFiles = GetRecipeImageFiles(recipeImageFolder);

            if (imageFiles.Count == 0)
            {
                _logger.Warn($"Nessuna immagine trovata nella cartella: {recipeImageFolder}");
                return null;
            }
            // 1. Ottieni dimensioni dell'immagine corrente
            var currentSize = GetImageDimensions(currentImage);
            if (currentSize.Width < MIN_IMAGE_SIZE || currentSize.Height < MIN_IMAGE_SIZE)
            {
                _logger.Warn($"Immagine corrente troppo piccola: {currentSize.Width}x{currentSize.Height}");
                return null;
            }

            _logger.Info($"Dimensioni immagine corrente: {currentSize.Width}x{currentSize.Height}");

            // Pre-processa l'immagine corrente
            ICogImage processedCurrentImage = convertImage(currentImage);
            // 2. Trova tutti i file immagine nella cartella ricette
           // var imageFiles = GetRecipeImageFiles(recipeImageFolder);
            if (imageFiles.Count == 0)
            {
                _logger.Warn($"Nessuna immagine trovata nella cartella: {recipeImageFolder}");
                return null;
            }

            _logger.Info($"Trovate {imageFiles.Count} immagini di riferimento");

            // 3. Filtra per dimensioni compatibili PRIMA del PMAlign
            var compatibleImages = FilterByDimensions(imageFiles, currentSize);

            if (compatibleImages.Count == 0)
            {
                _logger.Warn($"Nessuna immagine con dimensioni compatibili. " +
                           $"Cercando tra tutte le immagini...");
                // Se nessuna compatibile, prova comunque con tutte
                compatibleImages = imageFiles;
            }
            else
            {
                _logger.Info($"{compatibleImages.Count}/{imageFiles.Count} immagini hanno dimensioni compatibili");
            }
            // 4. Esegui PMAlign solo sulle immagini compatibili
            return FindBestMatchWithPMAlign(currentImage, compatibleImages, currentSize);

         
        }
        private (int Width, int Height) GetImageDimensions(ICogImage image)
        {
            try
            {
                return (image.Width, image.Height);
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore ottenendo dimensioni immagine: {ex.Message}");
                return (0, 0);
            }
        }
        private List<string> FilterByDimensions(List<string> imagePaths, (int Width, int Height) targetSize)
        {
            var compatibleImages = new List<string>();

            foreach (var imagePath in imagePaths)
            {
                try
                {
                    // Ottieni dimensioni SENZA caricare l'intera immagine in memoria
                    var refSize = GetImageDimensionsFast(imagePath);

                    if (refSize.Width == 0 || refSize.Height == 0)
                    {
                        _logger.Debug($"Impossibile ottenere dimensioni per: {Path.GetFileName(imagePath)}");
                        continue;
                    }

                    // Calcola la differenza percentuale
                    double widthDiff = Math.Abs(refSize.Width - targetSize.Width) / (double)targetSize.Width;
                    double heightDiff = Math.Abs(refSize.Height - targetSize.Height) / (double)targetSize.Height;

                    // Verifica se le dimensioni sono compatibili
                    if (widthDiff <= SIZE_TOLERANCE_PERCENT && heightDiff <= SIZE_TOLERANCE_PERCENT)
                    {
                        compatibleImages.Add(imagePath);
                        _logger.Debug($"  ✓ Compatibile: {Path.GetFileName(imagePath)} " +
                                    $"{refSize.Width}x{refSize.Height} " +
                                    $"(diff: {widthDiff:P0}, {heightDiff:P0})");
                    }
                    else
                    {
                        _logger.Debug($"  ✗ Scartata: {Path.GetFileName(imagePath)} " +
                                    $"{refSize.Width}x{refSize.Height} " +
                                    $"(diff: {widthDiff:P0}, {heightDiff:P0})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Errore verificando dimensioni {Path.GetFileName(imagePath)}: {ex.Message}");
                }
            }

            return compatibleImages;
        }
        private List<string> GetRecipeImageFiles(string folderPath)
        {
            var imageFiles = new List<string>();

            // Cerca nelle sottocartelle
            var extensions = new[] { "*.bmp", "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff" };

            foreach (var extension in extensions)
            {
                try
                {
                    imageFiles.AddRange(Directory.GetFiles(folderPath, extension));
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Errore cercando files {extension}: {ex.Message}");
                }
            }

            // Filtra per escludere immagini troppo piccole
            imageFiles = imageFiles.Where(file =>
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    return fileInfo.Length > 1024; // Almeno 1KB
                }
                catch
                {
                    return false;
                }
            }).ToList();

            return imageFiles;
        }

        private string FindBestMatchWithPMAlign(ICogImage currentImage, List<string> compatibleImages,
                                                (int Width, int Height) currentSize)
        {
            double bestScore = 0;
            string bestRecipeName = null;
            int imagesProcessed = 0;
            int imagesTrained = 0;

            // Pre-processa l'immagine corrente una sola volta
            CogImage8Grey currentImageGrey = currentImage as CogImage8Grey;
            if (currentImageGrey == null)
            {
                currentImageGrey =convertImage (currentImage);
            }

            // Aumenta la tolleranza se ci sono poche immagini compatibili
            double requiredScore = compatibleImages.Count <= 3 ? 0.65 : 0.75;

            foreach (var imagePath in compatibleImages)
            {
                try
                {
                    imagesProcessed++;
                    string fileName = Path.GetFileNameWithoutExtension(imagePath);

                    // Ottieni dimensioni di riferimento per logging
                    var refSize = GetImageDimensionsFast(imagePath);

                    _logger.Debug($"Matching {imagesProcessed}/{compatibleImages.Count}: {fileName} " +
                                $"({refSize.Width}x{refSize.Height})");

                    // Carica l'immagine di riferimento
                    using (var loader = new CogImageFileTool())
                    {
                        loader.Operator.Open(imagePath, CogImageFileModeConstants.Read);
                        loader.Run();

                        if (loader.OutputImage == null)
                        {
                            _logger.Debug($"  Immagine non caricata: {fileName}");
                            continue;
                        }

                        // Converti in scala di grigi
                        CogImage8Grey refImage = loader.OutputImage as CogImage8Grey;
                        if (refImage == null)
                        {
                            refImage = convertImage(loader.OutputImage);
                        }

                        // Controllo dimensioni aggiuntivo dopo il caricamento
                        if (Math.Abs(refImage.Width - currentSize.Width) > currentSize.Width * SIZE_TOLERANCE_PERCENT ||
                            Math.Abs(refImage.Height - currentSize.Height) > currentSize.Height * SIZE_TOLERANCE_PERCENT)
                        {
                            _logger.Debug($"  Dimensioni non compatibili dopo caricamento: " +
                                        $"{refImage.Width}x{refImage.Height}");
                            continue;
                        }

                        // Configura e addestra PMAlign
                        _matcherTool.Pattern.TrainImage = refImage;
                        _matcherTool.Pattern.TrainRegionMode= CogRegionModeConstants.PixelAlignedBoundingBox;
                        CogRectangle rect = new CogRectangle();
                        rect.SetXYWidthHeight(
                            0,  // X (coordinata in alto a sinistra)
                            0,  // Y (coordinata in alto a sinistra)
                            refImage.Width,  // Larghezza dell'immagine
                            refImage.Height  // Altezza dell'immagine
                        );
                        _matcherTool.Pattern.TrainRegion = rect;

                        try
                        {
                            _matcherTool.Pattern.Train();

                            if (!_matcherTool.Pattern.Trained)
                            {
                                _logger.Debug($"  Training fallito per {fileName}");
                                continue;
                            }

                            imagesTrained++;
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"  Errore training {fileName}: {ex.Message}");
                            continue;
                        }

                        // Esegui matching
                        _matcherTool.InputImage = currentImageGrey;
                       
                        _matcherTool.Run();

                        if (_matcherTool.Results != null && _matcherTool.Results.Count > 0)
                        {
                            double score = _matcherTool.Results[0].Score;
                            _logger.Debug($"  Score: {score:F2}");

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestRecipeName = fileName;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Errore processando {Path.GetFileName(imagePath)}: {ex.Message}");
                }
            }

            _logger.Info($"PMAlign completato: {imagesTrained}/{imagesProcessed} immagini addestrate");
            _logger.Info($"Miglior score: {bestScore:F2} per {bestRecipeName}");

            if (bestScore >= requiredScore)
            {
                return bestRecipeName;
            }
            else if (bestScore >= 0.6 && compatibleImages.Count == 1)
            {
                // Se c'è solo un'immagine compatibile e lo score è decente, suggeriscila
                _logger.Info($"Score basso ma unica opzione: {bestRecipeName}");
                return bestRecipeName + "?"; // Aggiungi punto interrogativo per indicare bassa confidenza
            }

            return null;
        }

        // Metodo aggiuntivo per ottenere anche il punteggio
        public CogImage8Grey convertImage(ICogImage cogImage)
        {
            if (cogImage != null && cogImage is CogImage24PlanarColor colorImg)
            {
                // Conversione semplice se arriva a colori
                CogImageConvertTool converter = new CogImageConvertTool();
                converter.InputImage = colorImg;
                converter.Run();
                cogImage = converter.OutputImage as CogImage8Grey;
                return (CogImage8Grey)cogImage;
            }
            else { return (CogImage8Grey)cogImage; }
        }
      
        private (int Width, int Height) GetImageDimensionsFast(string imagePath)
        {
            try
            {
                // Metodo 1: Usa System.Drawing (veloce)
                using (var bitmap = Image.FromFile(imagePath))
                {
                    return (bitmap.Width, bitmap.Height);
                }
            }
            catch
            {
                try
                {
                    // Metodo 2: Usa Cognex (più lento ma più affidabile)
                    using (var loader = new CogImageFileTool())
                    {
                        loader.Operator.Open(imagePath, CogImageFileModeConstants.Read);
                        loader.Run();
                        if (loader.OutputImage != null)
                        {
                            return (loader.OutputImage.Width, loader.OutputImage.Height);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Errore ottenendo dimensioni per {Path.GetFileName(imagePath)}: {ex.Message}");
                }

                return (0, 0);
            }
        }

 
        public (string RecipeName, double Score) FindMatchingRecipeWithScore(ICogImage currentImage, string recipeImageFolder)
        {
            string recipeName = FindMatchingRecipe(currentImage, recipeImageFolder);
            return (recipeName, 0.0); // Puoi modificare per restituire anche il punteggio
        }
    }

    public class AdvancedRecipeAutoSwitcher
    {
        private readonly Logger _logger = MainWindow.logger;

        public string FindMatchingRecipeWithFallback(ICogImage currentImage, string recipeImageFolder)
        {
            // 1. Primo tentativo: PMAlign con parametri ottimizzati
            var result = TryPMAlignMatching(currentImage, recipeImageFolder);
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

           
            // 3. Terzo tentativo: Confronto basato su hash dell'immagine
            result = TryHashBasedMatching(currentImage, recipeImageFolder);

            return result;
        }

        private string TryPMAlignMatching(ICogImage currentImage, string recipeImageFolder)
        {
            try
            {
                var switcher = new RecipeAutoSwitcher();
                return switcher.FindMatchingRecipe(currentImage, recipeImageFolder);
            }
            catch (Exception ex)
            {
                _logger.Warn($"PMAlign matching failed: {ex.Message}");
                return null;
            }
        }


        private string TryHashBasedMatching(ICogImage currentImage, string recipeImageFolder)
        {
            // Implementa un semplice hash basato sui pixel
            // Questo è un esempio semplificato
            return null;
        }
    }
}