using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro3D;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Models;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.SaveImage
{
    public interface ISaveImage
    {
        int[] saving_array_definition(int value_perc);
        string GetImageDirectory(); // ✅ nome corretto
        void CreateImageDirectory();
        /// <param name="renderAnnotatedFromDisplay">
        /// true solo per i pezzi non conformi: prenota il JPG con la grafica VisionPro.
        /// CameraDisplayManager lo renderizza sul thread UI e il solo worker immagini ne
        /// attende l'arrivo; la pipeline d'ispezione non attende il completamento.
        /// </param>
        void _SaveImage(Dictionary<string, CogRecordDisplay> recordDisplays, bool renderAnnotatedFromDisplay = false);

        void CheckOkPercentageSave();    // ✅ nuovo metodo
        void CheckFailPercentageSave();  // ✅ nuovo metodo
        void ForceDiagnosticSave();

    }
    public class SaveImage : ISaveImage, IDisposable
    {
        private sealed class PendingImageWrite
        {
            public string Path { get; set; }
            public byte[] Data { get; set; }
        }

        private sealed class PendingRecordSave
        {
            public string PieceFolder { get; set; }
            public int PieceIndex { get; set; }
            public Dictionary<string, ICogRecord> Records { get; set; }
            public string SharedImageDirectory { get; set; }
            public bool IncludeTop3DArtifacts { get; set; }
            // Richieste correlate al record radice del risultato. Il worker attende soltanto
            // queste richieste; il ciclo d'ispezione non attende mai il rendering UI.
            public Dictionary<string, Services.AnnotatedImageCache.Request> AnnotatedImageRequests { get; set; }
            public object Top3DRerenderValue { get; set; }
            public object Top3DPointCloudValue { get; set; }
        }

        private readonly object _saveLock = new object();

        // Fase 0 stabilita' (bounded queues): la coda di scrittura immagini e' LIMITATA.
        // Ogni elemento contiene l'intera immagine in byte[] (anche molti MB): con disco lento
        // o share di rete in stallo la coda illimitata cresceva senza limite in memoria.
        // A coda piena la scrittura viene scartata in modo ESPLICITO (contatore + warning):
        // il salvataggio immagini e' gia' campionato a percentuale, la produzione non e' toccata.
        private const int WriteQueueCapacity = 32;
        private const int RecordSaveQueueCapacity = 8;
        private long _droppedImageWrites;
        private long _droppedRecordSaves;
        private int _recordCaptureActive;
        private readonly SemaphoreSlim _writeQueueSlots = new SemaphoreSlim(WriteQueueCapacity, WriteQueueCapacity);
        private readonly BlockingCollection<PendingImageWrite> _writeQueue =
            new BlockingCollection<PendingImageWrite>(new ConcurrentQueue<PendingImageWrite>(), WriteQueueCapacity);
        private readonly CancellationTokenSource _writerCts = new CancellationTokenSource();
        private readonly Task _writerTask;
        private readonly BlockingCollection<PendingRecordSave> _recordSaveQueue =
            new BlockingCollection<PendingRecordSave>(new ConcurrentQueue<PendingRecordSave>(), RecordSaveQueueCapacity);
        private readonly CancellationTokenSource _recordSaveCts = new CancellationTokenSource();
        private readonly Task _recordSaveTask;
        private bool _disposed;

        private SaveImagePercentage SaveConfig => MainWindow.configManager.Config._SaveImagePercentage;
        private Configuration RuntimeConfiguration => MainWindow.configManager?.Config?.Configuration;

        public SaveImage()
        {
            _recordSaveTask = Task.Factory.StartNew(
                ProcessRecordSaveQueue,
                _recordSaveCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            _writerTask = Task.Factory.StartNew(
                ProcessWriteQueue,
                _writerCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public async Task FlushAsync(int timeoutMs = 5000)
        {
            var startedAt = DateTime.UtcNow;
            while ((_recordSaveQueue.Count > 0 || Volatile.Read(ref _recordCaptureActive) != 0 || _writeQueue.Count > 0) &&
                   (DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(50);
            }
        }

        public int[] saving_array_definition(int percentage)
        {
            if (percentage < 0 || percentage > 100)
                throw new ArgumentOutOfRangeException(nameof(percentage), "Percentage must be between 0 and 100.");

            int totalImages = 100;
            int imagesToSave = (int)Math.Round(percentage * totalImages / 100.0);

            int[] result = new int[totalImages];

            if (imagesToSave > 0)
            {
                double step = (double)totalImages / imagesToSave;
                for (int i = 0; i < imagesToSave; i++)
                {
                    int index = (int)Math.Round(i * step);
                    if (index >= totalImages) index = totalImages - 1;
                    result[index] = 1;
                }
            }

            return result;
        }


       


        public string GetImageDirectory()
        {
            string baseDir = MainWindow.configManager.Config.Configuration.ImageDir;
            DateTime localDate = DateTime.Now;
            int sessionIndex = SaveConfig.session;
            int batchIndex = SaveConfig.batch;

            int batch = (sessionIndex == 0 && batchIndex == 0) ? sessionIndex : batchIndex;

            string path = Path.Combine(
                baseDir,
                localDate.ToString("yyyy-MM-dd"),
                $"SESSION_{sessionIndex:D2}",
                $"BATCH_{batch:D2}"
            );

            return path + Path.DirectorySeparatorChar;
        }

        public void CreateImageDirectory()
        {
            string newDirectory = GetImageDirectory();

            try
            {
                if (!Directory.Exists(newDirectory))
                {
                    Directory.CreateDirectory(newDirectory);

                    var di = new DirectoryInfo(newDirectory);
                    var ds = di.GetAccessControl();
                    var fsar = new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                        FileSystemRights.FullControl,
                        AccessControlType.Allow
                    );

                    ds.AddAccessRule(fsar);
                    di.SetAccessControl(ds);

                    MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error creating image directory: {ex.Message}\n{ex.StackTrace}");
                // MessageBox.Show($"The process failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
        public void _SaveImage(Dictionary<string, CogRecordDisplay> recordDisplays, bool renderAnnotatedFromDisplay = false)
        {
            try
            {
                if (recordDisplays == null || recordDisplays.Count == 0)
                {
                    MainWindow.logger.Warn("Nessun display disponibile per il salvataggio");
                    return;
                }

                lock (_saveLock)
                {
                    CreateImageDirectory();

                    int imageIndex = SaveConfig.Incremento_imagini;
                    int pieceIndex = SaveConfig.pezzo;
                    int batchIndex = SaveConfig.batch;

                    string basePath = GetImageDirectory();
                    string pieceFolder = Path.Combine(basePath, $"Piece_{pieceIndex:D8}");
                    Directory.CreateDirectory(pieceFolder);

                    var recordSnapshot = new Dictionary<string, ICogRecord>(StringComparer.OrdinalIgnoreCase);
                    foreach (string viewName in recordDisplays.Keys)
                    {
                        ICogRecord record = GetSavedRecord(viewName);
                        if (record != null)
                        {
                            recordSnapshot[viewName] = record;
                        }
                    }

                    if (recordSnapshot.Count == 0)
                    {
                        MainWindow._produzioneRecord.PieceData = null;
                        MainWindow._produzioneRecord.DataHostnames = null;
                        MainWindow.logger.Warn("SAVE_IMAGE_SKIPPED|reason=no-records-for-current-piece");
                        return;
                    }

                    bool includeTop3DArtifacts = IsTop3DRuntimeActive();
                    CogToolBlock top3DToolBlock = includeTop3DArtifacts ? MainWindow._topToolBlockReults : null;
                    // Prenota l'immagine annotata del record radice corrente. CameraDisplayManager
                    // completa la richiesta quando il sottorecord e' stato assegnato al display;
                    // il worker immagini applica il timeout senza fermare ispezione o UI.
                    var annotatedRequests = new Dictionary<string, Services.AnnotatedImageCache.Request>(
                        StringComparer.OrdinalIgnoreCase);
                    if (renderAnnotatedFromDisplay)
                    {
                        foreach (string viewName in recordSnapshot.Keys)
                        {
                            string role = MapViewNameToDisplayRole(viewName);
                            ICogRecord resultRecord = GetResultRecord(viewName);
                            Services.AnnotatedImageCache.Request request =
                                Services.AnnotatedImageCache.CreateRequest(role, resultRecord);
                            if (request != null)
                            {
                                annotatedRequests[viewName] = request;
                            }
                        }
                    }

                    var pendingSave = new PendingRecordSave
                    {
                        AnnotatedImageRequests = annotatedRequests,
                        PieceFolder = pieceFolder,
                        PieceIndex = pieceIndex,
                        Records = recordSnapshot,
                        SharedImageDirectory = RuntimeConfiguration?.SharedImageDir,
                        IncludeTop3DArtifacts = includeTop3DArtifacts,
                        Top3DRerenderValue = includeTop3DArtifacts
                            ? TryGetToolBlockOutputValue(top3DToolBlock, ResolveTop3DRerenderResultOutputName())
                            : null,
                        Top3DPointCloudValue = includeTop3DArtifacts
                            ? TryGetToolBlockOutputValue(top3DToolBlock, ResolveTop3DPointCloudOutputName())
                            : null
                    };

                    if (_disposed || !_recordSaveQueue.TryAdd(pendingSave))
                    {
                        DisposeAnnotatedRequests(annotatedRequests);
                        MainWindow._produzioneRecord.PieceData = null;
                        MainWindow._produzioneRecord.DataHostnames = null;
                        long dropped = Interlocked.Increment(ref _droppedRecordSaves);
                        MainWindow.logger.Warn(
                            $"IMAGE_CAPTURE_QUEUE_FULL|capacity={RecordSaveQueueCapacity}|dropped_total={dropped}" +
                            $"|piece={pieceFolder}|Production continues without blocking the UI.");
                        return;
                    }

                    MainWindow._produzioneRecord.PieceData = pieceFolder;
                    MainWindow._produzioneRecord.DataHostnames = MainWindow.configManager.Config.Configuration.DataHostnames;

                    imageIndex++;
                    pieceIndex++;
                    SaveConfig.Incremento_imagini = imageIndex;

                    if (pieceIndex >= 100)
                    {
                        batchIndex++;
                        SaveConfig.pezzo = 0;
                        SaveConfig.batch = batchIndex;
                    }
                    else
                    {
                        SaveConfig.pezzo = pieceIndex;
                    }

                    MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error in _SaveImage: {ex.Message}\n{ex.StackTrace}");
            }
        }
        public void CheckOkPercentageSave()
        {
            int termsOk = SaveConfig.Terms_OK;
            termsOk++;
            SaveConfig.Terms_OK = termsOk;

            try
            {
                if (MainWindow.when_save_OK[MainWindow.CountImage_OK % 100] == 1)
                {
                    if (MainWindow._recordDisplays == null || MainWindow._recordDisplays.Count == 0)
                    {
                        MainWindow.logger.Warn("No record displays available to save.");
                        return;
                    }

                    _SaveImage(MainWindow._recordDisplays);

                    SaveConfig.Terms_OK = 0;
                    MainWindow.CountImage_OK++;
                }
                else
                {
                    // Reset filePath and DataHostnames if not saving
                    MainWindow._produzioneRecord.PieceData = null;
                    MainWindow._produzioneRecord.DataHostnames = null;
                    MainWindow.CountImage_OK++;
                }

                if (MainWindow.CountImage_OK >= 100)
                {
                    MainWindow.CountImage_OK = 0;
                }

                MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error in CheckOkPercentageSave: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void CheckFailPercentageSave()
        {
            int termsFail = SaveConfig.Terms_Fail;
            termsFail++;
            SaveConfig.Terms_Fail = termsFail;

            try
            {
                if (MainWindow._recordDisplays == null || MainWindow._recordDisplays.Count == 0)
                {
                    MainWindow.logger.Warn("No record displays available to save.");
                    return;
                }

                if (MainWindow.when_save_FAIL[MainWindow.CountImage_FAIL % 100] == 1)
                {
                    // Pezzo non conforme: qui la grafica serve, e' cio' che spiega lo scarto.
                    _SaveImage(MainWindow._recordDisplays, renderAnnotatedFromDisplay: true);

                    SaveConfig.Terms_Fail = 0;
                    MainWindow.CountImage_FAIL++;
                }
                else
                {
                    MainWindow._produzioneRecord.PieceData = null;
                    MainWindow._produzioneRecord.DataHostnames = null;
                    MainWindow.CountImage_FAIL++;
                }

                if (MainWindow.CountImage_FAIL >= 100)
                {
                    MainWindow.CountImage_FAIL = 0;
                }

                MainWindow.configManager.SaveConfigAsync().SafeFireAndForget();
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Error in CheckFailPercentageSave: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void ForceDiagnosticSave()
        {
            if (MainWindow._recordDisplays == null || MainWindow._recordDisplays.Count == 0)
            {
                MainWindow.logger.Warn("UNCLASSIFIED_DIAGNOSTIC_SAVE_SKIPPED|reason=no-record-displays");
                return;
            }

            var activeDisplays = new Dictionary<string, CogRecordDisplay>(StringComparer.OrdinalIgnoreCase);
            AddDiagnosticDisplay(activeDisplays, "T");

            bool sideJobIsLeft = (MainWindow._sideJob?.Name ?? string.Empty)
                .IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0;
            AddDiagnosticDisplay(activeDisplays, sideJobIsLeft ? "L" : "F");
            AddDiagnosticDisplay(activeDisplays, "FR");

            bool rearJobIsRight = (MainWindow._rearJob?.Name ?? string.Empty)
                .IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0;
            AddDiagnosticDisplay(activeDisplays, rearJobIsRight ? "RI" : "R");
            AddDiagnosticDisplay(activeDisplays, "B");

            // Salvataggio diagnostico di un pezzo non classificabile: e' il caso in cui la
            // grafica serve di piu', perche' mostra dove il tool si e' fermato.
            _SaveImage(activeDisplays, renderAnnotatedFromDisplay: true);
        }

        private static void AddDiagnosticDisplay(
            IDictionary<string, CogRecordDisplay> destination,
            string viewName)
        {
            if (MainWindow._recordDisplays.TryGetValue(viewName, out CogRecordDisplay display) &&
                display != null)
            {
                destination[viewName] = display;
            }
        }
        public void SaveImageFromRecordDisplay(CogRecordDisplay display, string path, ImageFormat format)
        {
            // Verifica che siamo sul thread UI
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                throw new InvalidOperationException("SaveImageFromRecordDisplay deve essere chiamato sul thread UI");
            }
            try
            {
                if (display == null)
                {
                    MainWindow.logger.Warn($"Display nullo per: {path}");
                    return;
                }

                if (display.Record == null && display.Image == null)
                {
                    MainWindow.logger.Warn($"Nessun record o immagine nel display per: {path}");
                    return;
                }

                if (!TryReserveImageWriteSlot(path))
                {
                    return;
                }

                bool handedToQueue = false;
                try
                {
                    var data = CaptureDisplayImageBytes(display, format);
                    handedToQueue = true;
                    EnqueueReservedImageWrite(path, data);
                }
                catch
                {
                    if (!handedToQueue)
                    {
                        ReleaseImageWriteSlot();
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Errore in SaveImageFromRecordDisplay: {ex.Message}");
                throw;
            }
        }

        public void SaveRawImage(ICogImage image, string path, ImageFormat format)
        {
            if (image == null)
            {
                MainWindow.logger.Warn($"Immagine null per: {path}");
                return;
            }
            try
            {
                if (!TryReserveImageWriteSlot(path))
                {
                    return;
                }

                bool handedToQueue = false;
                try
                {
                    var data = CaptureRawImageBytes(image, format);
                    handedToQueue = true;
                    EnqueueReservedImageWrite(path, data);
                }
                catch
                {
                    if (!handedToQueue)
                    {
                        ReleaseImageWriteSlot();
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Errore in SaveRawImage: {ex.Message}");
                throw;
            }
        }

        private bool TrySaveAnnotatedFromDisplayRecord(CogRecordDisplay display, string path)
        {
            if (display == null || display.Record == null)
            {
                return false;
            }

            try
            {
                SaveImageFromRecordDisplay(display, path, ImageFormat.Jpeg);
                return true;
            }
            catch (Exception ex)
            {
                MainWindow.logger.Warn($"Errore nel salvataggio annotato da display.Record: {ex.Message}");
                return false;
            }
        }



        private byte[] CaptureDisplayImageBytes(CogRecordDisplay display, ImageFormat format)
        {
            string renderFailure;
            using (var bmp = Services.AnnotatedImageRenderer.CreateContentBitmap(display, out renderFailure))
            using (var stream = new MemoryStream())
            {
                if (bmp == null)
                {
                    throw new InvalidOperationException(
                        "CreateContentBitmap non ha prodotto un bitmap: " + (renderFailure ?? "motivo ignoto"));
                }

                // Un display non ancora dipinto restituisce un bitmap della dimensione giusta
                // ma del solo colore di fondo. Salvarlo produce un file inutile senza che
                // nessuno se ne accorga: meglio rifiutarlo e lasciare che il chiamante
                // ripieghi sull'immagine grezza, che almeno mostra il pezzo.
                if (IsUniformBitmap(bmp))
                {
                    throw new InvalidOperationException(
                        "CreateContentBitmap ha restituito un'immagine di colore uniforme (display non dipinto)");
                }

                bmp.Save(stream, format);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// True se il bitmap e' di un solo colore. Non si confronta con un colore atteso: il
        /// fondo del CogRecordDisplay e' configurabile (qui #0A0B60, non blu puro) e un
        /// controllo su una costante sbagliata non scatterebbe mai. Conta solo l'uniformita'.
        /// Si usa LockBits: GetPixel per pixel bloccherebbe e sbloccherebbe il bitmap ogni volta.
        /// </summary>
        private static bool IsUniformBitmap(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 2 || bitmap.Height < 2)
            {
                return false; // non decidibile: si lascia passare
            }

            System.Drawing.Imaging.BitmapData data = null;
            try
            {
                data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                unsafe
                {
                    byte* scan0 = (byte*)data.Scan0;
                    int first = *((int*)scan0);

                    // Campionamento a griglia: un'immagine reale differisce dal fondo entro
                    // pochi campioni, quindi non serve leggere tutti i pixel.
                    int stepY = Math.Max(1, bitmap.Height / 64);
                    int stepX = Math.Max(1, bitmap.Width / 64);

                    for (int y = 0; y < bitmap.Height; y += stepY)
                    {
                        int* row = (int*)(scan0 + (long)y * data.Stride);
                        for (int x = 0; x < bitmap.Width; x += stepX)
                        {
                            if (row[x] != first)
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false; // in dubbio si salva comunque
            }
            finally
            {
                if (data != null)
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        // Oltre questo tempo si rinuncia alla grafica e si tiene il grezzo: bloccare la
        // pipeline di ispezione in attesa della UI sarebbe peggio di un'immagine senza
        // annotazioni.
        private const int AnnotatedRenderTimeoutMs = 1500;


        /// <summary>
        /// Attende sul solo worker immagini il JPEG prodotto dalla vista per lo stesso record.
        /// Il timeout e' calcolato dal chiamante su una scadenza condivisa fra tutte le viste,
        /// quindi una camera non disponibile non moltiplica l'attesa per il loro numero.
        /// </summary>
        private bool TryQueueAnnotatedForView(
            string viewName,
            Services.AnnotatedImageCache.Request request,
            string path,
            int timeoutMs)
        {
            if (request == null)
            {
                return false;
            }

            string role = MapViewNameToDisplayRole(viewName);
            if (!request.TryWait(timeoutMs, out byte[] jpeg))
            {
                MainWindow.logger.Warn(
                    $"SAVE_IMAGE_ANNOTATED_TIMEOUT|view={viewName}|role={role}" +
                    $"|timeoutMs={timeoutMs}|si salva il grezzo");
                return false;
            }

            if (!TryReserveImageWriteSlot(path))
            {
                return false;
            }

            EnqueueReservedImageWrite(path, jpeg);
            MainWindow.logger.Info(
                $"SAVE_IMAGE_ANNOTATED_READY|file={Path.GetFileName(path)}|role={role}|bytes={jpeg.Length}");
            return true;
        }

        /// <summary>
        /// Stessa mappa vista -> ruolo usata da GetDisplayForView, cosi' cache e display
        /// restano allineati.
        /// </summary>
        private static string MapViewNameToDisplayRole(string viewName)
        {
            switch ((viewName ?? string.Empty).ToUpperInvariant())
            {
                case "T": return IsTop3DRuntimeActive() ? "top3d" : "top";
                case "F":
                case "S": return IsTop3DRuntimeActive() ? "top2d" : "side";
                case "L": return "left";
                case "FR": return "front";
                case "R": return "rear";
                case "RI": return "right";
                case "B": return "bottom";
                default: return string.Empty;
            }
        }

        private static ICogRecord GetResultRecord(string viewName)
        {
            switch ((viewName ?? string.Empty).ToUpperInvariant())
            {
                case "T":
                    return MainWindow._topSaveRecord;
                case "F":
                case "S":
                    return IsTop3DRuntimeActive()
                        ? MainWindow._topSaveRecord
                        : MainWindow._sideSaveRecord;
                case "L":
                    return MainWindow._sideSaveRecord;
                case "FR":
                    return MainWindow._frontSaveRecord;
                case "R":
                case "RI":
                    return MainWindow._rearSaveRecord;
                case "B":
                    return MainWindow._bottomSaveRecord;
                default:
                    return null;
            }
        }

        private static void DisposeAnnotatedRequests(
            IDictionary<string, Services.AnnotatedImageCache.Request> requests)
        {
            if (requests == null)
            {
                return;
            }

            foreach (Services.AnnotatedImageCache.Request request in requests.Values)
            {
                request?.Dispose();
            }
        }

        private void SaveImageFromRecord(ICogRecord record, CogRecordDisplay display, string path, ImageFormat format)
        {
            if (record == null || display == null)
            {
                return;
            }

            var previousRecord = display.Record;
            bool mustRestore = !object.ReferenceEquals(previousRecord, record);
            bool handedToQueue = false;

            try
            {
                if (!TryReserveImageWriteSlot(path))
                {
                    return;
                }

                if (mustRestore)
                {
                    display.Record = record;
                    display.AutoFit = true;
                    display.Fit(true);
                }

                display.Refresh();

                if (mustRestore)
                {
                    // Refresh() da solo non basta quando il record viene assegnato ora: il
                    // ridisegno del controllo VisionPro si completa sulla coda del dispatcher,
                    // ferma finche' occupiamo il thread UI. Questa Invoke a priorita' inferiore
                    // a Render svuota la coda fino al render completato.
                    //
                    // Effetto collaterale noto e accettato: i refresh accodati da
                    // CameraDisplayManager a DispatcherPriority.Background vengono eseguiti qui,
                    // quindi durante il salvataggio di uno scarto la vista TOP si aggiorna due
                    // volte a schermo. E' il prezzo per catturare dal controllo condiviso con la
                    // HMI, ed e' la configurazione che in campo salvava correttamente tutte le
                    // viste (log 2026-09-09 14:25). Ogni variante provata dopo — display fuori
                    // schermo, rendering sul worker, ciclo di tentativi — ha peggiorato il
                    // risultato: non reintrodurle senza una prova che risolvano.
                }

                var data = CaptureDisplayImageBytes(display, format);
                handedToQueue = true;
                EnqueueReservedImageWrite(path, data);
            }
            catch
            {
                if (!handedToQueue)
                {
                    ReleaseImageWriteSlot();
                }
                throw;
            }
            finally
            {
                if (mustRestore)
                {
                    display.Record = previousRecord;
                }
            }
        }

        private byte[] CaptureRawImageBytes(ICogImage image, ImageFormat format)
        {
            using (Bitmap bmp = image.ToBitmap())
            using (Bitmap normalized = PrepareRawBitmapForExport(image, bmp))
            using (var stream = new MemoryStream())
            {
                normalized.Save(stream, format);
                return stream.ToArray();
            }
        }

        private static Bitmap PrepareRawBitmapForExport(ICogImage image, Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return null;
            }

            if (ShouldSaveAsGrayscale(image, bitmap))
            {
                return MakeGrayscale(bitmap);
            }

            return CloneBitmap(bitmap);
        }

        private static bool ShouldSaveAsGrayscale(ICogImage image, Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return false;
            }

            var typeName = image?.GetType().Name ?? string.Empty;
            if (typeName.IndexOf("Grey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Gray", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return bitmap.PixelFormat == PixelFormat.Format8bppIndexed ||
                   bitmap.PixelFormat == PixelFormat.Format16bppGrayScale;
        }

        private static Bitmap CloneBitmap(Bitmap source)
        {
            var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(clone))
            {
                graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }

            return clone;
        }

        private void EnqueueImageWrite(string path, byte[] data)
        {
            if (!TryReserveImageWriteSlot(path))
            {
                return;
            }

            EnqueueReservedImageWrite(path, data);
        }

        private bool TryReserveImageWriteSlot(string path)
        {
            if (_disposed)
            {
                MainWindow.logger.Warn($"Tentativo di accodare un'immagine durante lo shutdown: {path}");
                return false;
            }

            // Non blocca il chiamante e non crea bitmap/byte[] pesanti se la coda e' gia' satura.
            try
            {
                if (!_writeQueueSlots.Wait(0))
                {
                    LogImageQueueFull(path, "pre-capture");
                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            return true;
        }

        private void EnqueueReservedImageWrite(string path, byte[] data)
        {
            bool queued = false;

            if (data == null || data.Length == 0)
            {
                MainWindow.logger.Warn($"Nessun dato immagine da accodare per: {path}");
                ReleaseImageWriteSlot();
                return;
            }

            try
            {
                // TryAdd resta non bloccante: la prenotazione sopra evita quasi sempre
                // conversioni pesanti quando la coda e' piena; questo ramo resta come guardia.
                if (!_writeQueue.TryAdd(new PendingImageWrite
                {
                    Path = path,
                    Data = data
                }))
                {
                    LogImageQueueFull(path, "post-capture");
                    return;
                }

                queued = true;
            }
            catch (ObjectDisposedException)
            {
                MainWindow.logger.Warn($"Coda immagini già rilasciata durante il salvataggio di: {path}");
            }
            catch (InvalidOperationException)
            {
                MainWindow.logger.Warn($"Coda immagini già chiusa durante il salvataggio di: {path}");
            }
            finally
            {
                if (!queued)
                {
                    ReleaseImageWriteSlot();
                }
            }
        }

        private void LogImageQueueFull(string path, string stage)
        {
            long dropped = System.Threading.Interlocked.Increment(ref _droppedImageWrites);
            if (dropped == 1 || dropped % 25 == 0)
            {
                MainWindow.logger.Warn(
                    $"IMAGE_QUEUE_FULL|capacity={WriteQueueCapacity}|dropped_total={dropped}|stage={stage}|path={path} — scrittura immagine scartata (disco lento o in stallo); produzione non impattata");
            }
        }

        private void ReleaseImageWriteSlot()
        {
            try
            {
                _writeQueueSlots.Release();
            }
            catch (SemaphoreFullException ex)
            {
                MainWindow.logger.Warn(ex, "IMAGE_QUEUE_SLOT_RELEASE_SKIPPED|semaphore already full");
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ProcessRecordSaveQueue()
        {
            try
            {
                Thread.CurrentThread.Name = "Qtis.ImageCapture";
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            }
            catch
            {
            }

            try
            {
                foreach (PendingRecordSave item in _recordSaveQueue.GetConsumingEnumerable(_recordSaveCts.Token))
                {
                    Interlocked.Exchange(ref _recordCaptureActive, 1);
                    try
                    {
                        DateTime annotatedDeadlineUtc = DateTime.UtcNow.AddMilliseconds(AnnotatedRenderTimeoutMs);
                        foreach (KeyValuePair<string, ICogRecord> entry in item.Records)
                        {
                            string imageWithGraphics = Path.Combine(
                                item.PieceFolder,
                                $"CH1_{item.PieceIndex:D8}_{entry.Key}_Z.jpg");
                            string imageRaw = Path.Combine(
                                item.PieceFolder,
                                $"CH1_{item.PieceIndex:D8}_{entry.Key}_A.bmp");

                            bool annotatedAlreadySaved = false;
                            if (item.AnnotatedImageRequests != null &&
                                item.AnnotatedImageRequests.TryGetValue(
                                    entry.Key,
                                    out Services.AnnotatedImageCache.Request annotatedRequest))
                            {
                                try
                                {
                                    int remainingMs = Math.Max(
                                        0,
                                        (int)Math.Ceiling((annotatedDeadlineUtc - DateTime.UtcNow).TotalMilliseconds));
                                    annotatedAlreadySaved = TryQueueAnnotatedForView(
                                        entry.Key,
                                        annotatedRequest,
                                        imageWithGraphics,
                                        remainingMs);
                                }
                                finally
                                {
                                    annotatedRequest.Dispose();
                                }
                            }

                            ICogImage image = TryExtractImageFromRecord(entry.Value, 0);
                            if (image == null)
                            {
                                MainWindow.logger.Warn(
                                    $"SAVE_IMAGE_VIEW_SKIPPED|view={entry.Key}|piece={item.PieceIndex}|reason=no-image-in-captured-record");
                                continue;
                            }

                            if (!annotatedAlreadySaved)
                            {
                                SaveRawImage(image, imageWithGraphics, ImageFormat.Jpeg);
                            }

                            SaveRawImage(image, imageRaw, ImageFormat.Bmp);
                            LogSaveImageOk(
                                entry.Key,
                                imageWithGraphics,
                                imageRaw,
                                annotatedAlreadySaved ? "display-record-graphics" : "captured-record-raw",
                                "captured-record");
                        }

                        if (item.IncludeTop3DArtifacts)
                        {
                            TrySaveTop3DArtifacts(
                                item.PieceFolder,
                                item.PieceIndex,
                                item.Top3DRerenderValue,
                                item.Top3DPointCloudValue);
                        }

                        if (!string.IsNullOrWhiteSpace(item.SharedImageDirectory))
                        {
                            string capturedPieceFolder = item.PieceFolder;
                            string sharedDirectory = item.SharedImageDirectory;
                            Task.Run(async () =>
                            {
                                await Task.Delay(500).ConfigureAwait(false);
                                SyncPieceFolderToShared(capturedPieceFolder, sharedDirectory);
                            }).SafeFireAndForget(ex => MainWindow.logger.Warn(
                                ex,
                                "SHARED_SYNC|Failed to sync piece folder to shared directory."));
                        }
                    }
                    catch (Exception ex)
                    {
                        MainWindow.logger.Error(ex, $"IMAGE_CAPTURE_FAILED|piece={item?.PieceFolder}");
                    }
                    finally
                    {
                        DisposeAnnotatedRequests(item?.AnnotatedImageRequests);
                        Interlocked.Exchange(ref _recordCaptureActive, 0);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                MainWindow.logger.Debug("Coda cattura record immagini arrestata");
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error(ex, "Errore nella coda di cattura record immagini");
            }
        }

        private void ProcessWriteQueue()
        {
            try
            {
                foreach (var item in _writeQueue.GetConsumingEnumerable(_writerCts.Token))
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(item.Path);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllBytes(item.Path, item.Data);
                        MainWindow.logger.Debug($"Immagine accodata salvata: {item.Path}");
                    }
                    catch (Exception ex)
                    {
                        MainWindow.logger.Error($"Errore scrittura immagine in coda '{item.Path}': {ex.Message}");
                    }
                    finally
                    {
                        ReleaseImageWriteSlot();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                MainWindow.logger.Debug("Coda salvataggio immagini arrestata");
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Errore nella coda di salvataggio immagini: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            bool recordSaveStopped = false;
            bool writerStopped = false;

            try
            {
                _recordSaveQueue.CompleteAdding();
                recordSaveStopped = _recordSaveTask.Wait(3000);
                if (!recordSaveStopped)
                {
                    _recordSaveCts.Cancel();
                    recordSaveStopped = _recordSaveTask.Wait(1000);
                }

                _writeQueue.CompleteAdding();
            }
            catch
            {
            }

            try
            {
                writerStopped = _writerTask.Wait(2000);
                if (!writerStopped)
                {
                    _writerCts.Cancel();
                    writerStopped = _writerTask.Wait(1000);
                }
            }
            catch (AggregateException)
            {
                writerStopped = _writerTask.IsCompleted;
            }
            catch (ObjectDisposedException)
            {
                writerStopped = true;
            }
            finally
            {
                try
                {
                    _recordSaveCts.Cancel();
                    _writerCts.Cancel();
                }
                catch
                {
                }

                if (writerStopped)
                {
                    if (recordSaveStopped)
                    {
                        _recordSaveCts.Dispose();
                        _recordSaveQueue.Dispose();
                    }
                    _writerCts.Dispose();
                    _writeQueue.Dispose();
                    _writeQueueSlots.Dispose();
                }
                else
                {
                    MainWindow.logger.Warn("IMAGE_QUEUE_SHUTDOWN_TIMEOUT|writer did not stop within dispose timeout; resources left for process teardown");
                }

                if (!recordSaveStopped)
                {
                    MainWindow.logger.Warn("IMAGE_CAPTURE_QUEUE_SHUTDOWN_TIMEOUT|capture worker did not stop within dispose timeout");
                }
            }
        }

        private ICogImage TryResolveRawImage(CogRecordDisplay display, string viewName, out string source)
        {
            source = null;

            var savedRecordImage = TryGetImageFromSavedRecord(viewName);
            if (savedRecordImage != null)
            {
                source = "record";
                return savedRecordImage;
            }

            if (display != null)
            {
                var displayRecordImage = TryExtractImageFromRecord(display.Record, 0);
                if (displayRecordImage != null)
                {
                    source = "display-record";
                    return displayRecordImage;
                }

                if (display.Image != null)
                {
                    source = "display-image";
                    return display.Image;
                }
            }

            return TryResolveFallbackImage(viewName, out source);
        }

        private ICogImage TryResolveFallbackImage(string viewName, out string source)
        {
            source = null;

            var recordImage = TryGetImageFromSavedRecord(viewName);
            if (recordImage != null)
            {
                source = "record";
                return recordImage;
            }

            var toolBlockImage = TryGetImageFromToolBlock(viewName);
            if (toolBlockImage != null)
            {
                source = "toolblock";
                return toolBlockImage;
            }

            return null;
        }

        private void LogSaveImageOk(string viewName, string annotatedPath, string rawPath, string annotatedSource, string rawSource)
        {
            MainWindow.logger.Info(
                $"SAVE_IMAGE_OK|view={viewName}|annotated={annotatedPath}|raw={rawPath}|annotated_source={annotatedSource}|raw_source={rawSource}");
        }

        /// <summary>
        /// Copies all files in <paramref name="localPieceFolder"/> to the mirrored path under
        /// <paramref name="sharedBaseDir"/>, preserving the date/session/batch/piece sub-structure.
        /// Runs on a background thread; errors are logged but never bubble to the caller.
        /// </summary>
        private void SyncPieceFolderToShared(string localPieceFolder, string sharedBaseDir)
        {
            try
            {
                string baseImageDir = RuntimeConfiguration?.ImageDir ?? string.Empty;
                string relativePath = !string.IsNullOrEmpty(baseImageDir) &&
                                      localPieceFolder.StartsWith(baseImageDir, StringComparison.OrdinalIgnoreCase)
                    ? localPieceFolder.Substring(baseImageDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : Path.GetFileName(localPieceFolder);

                string targetFolder = Path.Combine(sharedBaseDir, relativePath);
                Directory.CreateDirectory(targetFolder);

                int copied = 0;
                foreach (string file in Directory.GetFiles(localPieceFolder))
                {
                    string targetFile = Path.Combine(targetFolder, Path.GetFileName(file));
                    File.Copy(file, targetFile, overwrite: true);
                    copied++;
                }

                MainWindow.logger.Info($"SHARED_SYNC|files={copied} target={targetFolder}");
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error(ex, $"SHARED_SYNC|Error syncing {localPieceFolder} to shared folder.");
            }
        }

        private ICogImage TryGetImageFromSavedRecord(string viewName)
        {
            ICogRecord record = GetSavedRecord(viewName);

            return TryExtractImageFromRecord(record, 0);
        }

        private ICogRecord GetSavedRecord(string viewName)
        {
            ICogRecord record = null;

            if (string.Equals(viewName, "T", StringComparison.OrdinalIgnoreCase))
            {
                record = MainWindow._topDisplaySaveRecord ?? MainWindow._topSaveRecord;
            }
            else if (string.Equals(viewName, "F", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(viewName, "S", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(viewName, "L", StringComparison.OrdinalIgnoreCase))
            {
                record = MainWindow._sideDisplaySaveRecord ??
                         (IsTop3DRuntimeActive()
                             ? MainWindow._topSaveRecord
                             : MainWindow._sideSaveRecord);
            }
            else if (string.Equals(viewName, "FR", StringComparison.OrdinalIgnoreCase))
            {
                record = MainWindow._frontDisplaySaveRecord ?? MainWindow._frontSaveRecord;
            }
            else if (string.Equals(viewName, "R", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(viewName, "RI", StringComparison.OrdinalIgnoreCase))
            {
                record = MainWindow._rearDisplaySaveRecord ?? MainWindow._rearSaveRecord;
            }
            else if (string.Equals(viewName, "B", StringComparison.OrdinalIgnoreCase))
            {
                record = MainWindow._bottomDisplaySaveRecord ?? MainWindow._bottomSaveRecord;
            }

            return record;
        }

        private CogRecordDisplay GetDisplayForView(string viewName)
        {
            // Si risolve dalle istanze VIVE delle viste, non da MainWindow._recordDisplays.
            // Quel dizionario viene popolato in Window_Loaded, ma le viste camera vengono
            // ricaricate subito dopo ("Viste telecamere caricate"): i controlli a schermo
            // sono nuovi e il dizionario resta a puntare quelli vecchi, orfani. Restano
            // visible=True e handleCreated=True perche' furono realizzati una volta, ma
            // nessuno ci scrive piu': Record e Image sono null e la cattura restituisce il
            // solo colore di fondo. La mappa vista -> controllo e' la stessa di Window_Loaded.
            CogRecordDisplay live = null;
            switch ((viewName ?? string.Empty).ToUpperInvariant())
            {
                case "T":
                    live = Views.UserControls.DisplayRecord.TopCameraView.Instance?.CogRecordsDisplay1;
                    break;
                case "F":
                case "S":
                    live = Views.UserControls.DisplayRecord.SideCameraView.Instance?.CogRecordsDisplay1;
                    break;
                case "L":
                    live = Views.UserControls.DisplayRecord.LeftCameraView.Instance?.ImageDisplay;
                    break;
                case "FR":
                    live = Views.UserControls.DisplayRecord.FrontCameraView.Instance?.CogRecordsDisplay1;
                    break;
                case "R":
                    live = Views.UserControls.DisplayRecord.RearCameraView.Instance?.ImageDisplay;
                    break;
                case "RI":
                    live = Views.UserControls.DisplayRecord.RightCameraView.Instance?.ImageDisplay;
                    break;
                case "B":
                    live = Views.UserControls.DisplayRecord.BottomCameraView.Instance?.ImageDisplay;
                    break;
            }

            if (live != null)
            {
                return live;
            }

            if (MainWindow._recordDisplays == null)
            {
                return null;
            }

            CogRecordDisplay display;
            return MainWindow._recordDisplays.TryGetValue(viewName, out display) ? display : null;
        }

        private ICogImage TryGetImageFromToolBlock(string viewName)
        {
            try
            {
                Cognex.VisionPro.ToolBlock.CogToolBlock toolBlock = null;

                if (string.Equals(viewName, "T", StringComparison.OrdinalIgnoreCase))
                {
                    toolBlock = MainWindow._topToolBlockReults;
                }
                else if (string.Equals(viewName, "F", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(viewName, "S", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(viewName, "L", StringComparison.OrdinalIgnoreCase))
                {
                    toolBlock = MainWindow._sideToolBlockResults;
                }
                else if (string.Equals(viewName, "FR", StringComparison.OrdinalIgnoreCase))
                {
                    toolBlock = MainWindow._frontTBResults;
                }
                else if (string.Equals(viewName, "R", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(viewName, "RI", StringComparison.OrdinalIgnoreCase))
                {
                    toolBlock = MainWindow._RearTBResults;
                }
                else if (string.Equals(viewName, "B", StringComparison.OrdinalIgnoreCase))
                {
                    toolBlock = MainWindow._bottomTBResults;
                }

                if (toolBlock == null || !toolBlock.Inputs.Contains("InputImage"))
                {
                    return null;
                }

                var input = toolBlock.Inputs["InputImage"];
                return input != null ? input.Value as ICogImage : null;
            }
            catch (Exception ex)
            {
                MainWindow.logger.Warn($"Errore recupero immagine da toolblock per {viewName}: {ex.Message}");
                return null;
            }
        }

        private ICogImage TryExtractImageFromRecord(ICogRecord record, int depth)
        {
            if (record == null || depth > 8)
            {
                return null;
            }

            if (record.Content is ICogImage directImage)
            {
                return directImage;
            }

            try
            {
                var subRecords = record.SubRecords;
                if (subRecords == null)
                {
                    return null;
                }

                for (int i = 0; i < subRecords.Count; i++)
                {
                    var nestedImage = TryExtractImageFromRecord(subRecords[i], depth + 1);
                    if (nestedImage != null)
                    {
                        return nestedImage;
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger.Warn($"Errore lettura record immagine: {ex.Message}");
            }

            return null;
        }

        private bool TrySaveFromSavedRecord(string viewName, string imageWithGraphics, string imageRaw)
        {
            try
            {
                var record = GetSavedRecord(viewName);
                if (record == null)
                {
                    return false;
                }

                var display = GetDisplayForView(viewName);
                if (display == null)
                {
                    return false;
                }

                SaveImageFromRecord(record, display, imageWithGraphics, ImageFormat.Jpeg);
                return true;
            }
            catch (Exception ex)
            {
                MainWindow.logger.Warn($"Errore nel salvataggio da record per {viewName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves Top3D-specific artifacts alongside the standard piece images.
        ///
        /// The runtime keeps the same piece-folder contract and adds sidecar files
        /// only when the active top job is a semantic Top3D job and the configured
        /// VisionPro outputs are available.
        /// </summary>
        private void TrySaveTop3DArtifacts(
            string pieceFolder,
            int pieceIndex,
            object rerenderValue,
            object pointCloudValue)
        {
            try
            {
                string rerenderOutputName = ResolveTop3DRerenderResultOutputName();
                if (!TryResolveTop3DRenderArtifacts(
                    rerenderValue,
                    out ICogImage rendered2DImage,
                    out ICogImage rangeImage))
                {
                    MainWindow.logger.Warn(
                        $"Top3D artifact export skipped: output '{rerenderOutputName}' is missing or not a supported rerender/stitch result.");
                    return;
                }

                bool rendered2DSaved = false;
                bool rangeSaved = false;
                bool pointCloudSaved = false;
                int pointCount = 0;

                if (IsTop3DRendered2DImageSaveEnabled() && rendered2DImage != null)
                {
                    string rendered2DPath = Path.Combine(pieceFolder, $"CH1_{pieceIndex:D8}_T3D_2D_A.bmp");
                    SaveRawImage(rendered2DImage, rendered2DPath, ImageFormat.Bmp);
                    rendered2DSaved = true;
                }

                if (IsTop3DRangeImageSaveEnabled() && rangeImage != null)
                {
                    string rangePath = Path.Combine(pieceFolder, $"CH1_{pieceIndex:D8}_T3D_3D_Range_A.bmp");
                    SaveRawImage(rangeImage, rangePath, ImageFormat.Bmp);
                    rangeSaved = true;
                }

                if (IsTop3DPointCloudCsvSaveEnabled())
                {
                    string pointCloudOutputName = ResolveTop3DPointCloudOutputName();
                    string pointCloudPath = Path.Combine(pieceFolder, $"CH1_{pieceIndex:D8}_T3D_PointCloud.csv");

                    if (TrySavePointCloudCsv(pointCloudValue, pointCloudPath, out pointCount))
                    {
                        pointCloudSaved = true;
                    }
                    else
                    {
                        MainWindow.logger.Warn(
                            $"Top3D point-cloud export skipped: output '{pointCloudOutputName}' is missing or not enumerable as X/Y/Z points.");
                    }
                }

                MainWindow.logger.Info(
                    $"SAVE_TOP3D_ARTIFACTS|piece={pieceFolder}|render2d_saved={rendered2DSaved}|range_saved={rangeSaved}|pointcloud_saved={pointCloudSaved}|point_count={pointCount}");
            }
            catch (Exception ex)
            {
                MainWindow.logger.Error($"Top3D artifact export error: {ex.Message}");
            }
        }

        private static bool IsTop3DRuntimeActive()
        {
            string topJobName = MainWindow._topJob?.Name;
            string resolvedRole = CameraConfigurationHelper.NormalizeCameraType(topJobName);

            if (string.Equals(resolvedRole, "top3d", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (MainWindow.JobRoleMapping != null)
            {
                foreach (var mappedRole in MainWindow.JobRoleMapping.Values)
                {
                    if (string.Equals(CameraConfigurationHelper.NormalizeCameraType(mappedRole), "top3d", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private object TryGetToolBlockOutputValue(CogToolBlock toolBlock, string outputName)
        {
            try
            {
                if (toolBlock == null ||
                    string.IsNullOrWhiteSpace(outputName) ||
                    toolBlock.Outputs == null ||
                    !toolBlock.Outputs.Contains(outputName))
                {
                    return null;
                }

                return toolBlock.Outputs[outputName]?.Value;
            }
            catch (Exception ex)
            {
                MainWindow.logger.Warn($"Errore recupero output Top3D '{outputName}': {ex.Message}");
                return null;
            }
        }

        private static bool TryResolveTop3DRenderArtifacts(
            object rerenderValue,
            out ICogImage rendered2DImage,
            out ICogImage rangeImage)
        {
            rendered2DImage = null;
            rangeImage = null;

            if (rerenderValue is Cog3DVisionDataRerenderResult rerenderResult)
            {
                rendered2DImage = rerenderResult.GreyImage;
                rangeImage = rerenderResult.RangeImage;
                return rendered2DImage != null || rangeImage != null;
            }

            if (rerenderValue is Cog3DVisionDataStitchResult stitchResult)
            {
                rendered2DImage = stitchResult.GreyImage;
                rangeImage = stitchResult.RangeImage;
                return rendered2DImage != null || rangeImage != null;
            }

            return false;
        }

        private bool TrySavePointCloudCsv(object pointCloudValue, string path, out int pointCount)
        {
            pointCount = 0;

            if (!TryExtractPointCloudRows(pointCloudValue, out List<PointCloudRow> rows) || rows.Count == 0)
            {
                return false;
            }

            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("X;Y;Z");

            foreach (var row in rows)
            {
                csvBuilder.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0};{1};{2}",
                        row.X,
                        row.Y,
                        row.Z));
            }

            pointCount = rows.Count;
            EnqueueBinaryWrite(path, Encoding.UTF8.GetBytes(csvBuilder.ToString()));
            return true;
        }

        private static bool TryExtractPointCloudRows(object pointCloudValue, out List<PointCloudRow> rows)
        {
            rows = new List<PointCloudRow>();

            if (pointCloudValue == null)
            {
                return false;
            }

            if (pointCloudValue is Cog3DVect3Collection pointCollection)
            {
                foreach (Cog3DVect3 point in pointCollection)
                {
                    rows.Add(new PointCloudRow(point.X, point.Y, point.Z));
                }

                return rows.Count > 0;
            }

            if (pointCloudValue is IEnumerable enumerable)
            {
                foreach (object point in enumerable)
                {
                    if (TryExtractPointCloudRow(point, out PointCloudRow row))
                    {
                        rows.Add(row);
                    }
                }

                return rows.Count > 0;
            }

            return false;
        }

        private static bool TryExtractPointCloudRow(object point, out PointCloudRow row)
        {
            row = default(PointCloudRow);

            if (point == null)
            {
                return false;
            }

            Type pointType = point.GetType();
            var xProperty = pointType.GetProperty("X");
            var yProperty = pointType.GetProperty("Y");
            var zProperty = pointType.GetProperty("Z");

            if (xProperty == null || yProperty == null || zProperty == null)
            {
                return false;
            }

            try
            {
                row = new PointCloudRow(
                    Convert.ToDouble(xProperty.GetValue(point), CultureInfo.InvariantCulture),
                    Convert.ToDouble(yProperty.GetValue(point), CultureInfo.InvariantCulture),
                    Convert.ToDouble(zProperty.GetValue(point), CultureInfo.InvariantCulture));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnqueueBinaryWrite(string path, byte[] data)
        {
            EnqueueImageWrite(path, data);
        }

        private string ResolveTop3DRerenderResultOutputName()
        {
            string configuredName = RuntimeConfiguration?.Top3DRerenderResultOutput;
            return string.IsNullOrWhiteSpace(configuredName) ? "Top3DRerenderResult" : configuredName;
        }

        private string ResolveTop3DPointCloudOutputName()
        {
            string configuredName = RuntimeConfiguration?.Top3DPointCloudOutput;
            return string.IsNullOrWhiteSpace(configuredName) ? "Top3DPointCloud" : configuredName;
        }

        private bool IsTop3DRendered2DImageSaveEnabled()
        {
            return RuntimeConfiguration?.SaveTop3DRendered2DImage ?? true;
        }

        private bool IsTop3DRangeImageSaveEnabled()
        {
            return RuntimeConfiguration?.SaveTop3DRangeImage ?? true;
        }

        private bool IsTop3DPointCloudCsvSaveEnabled()
        {
            return RuntimeConfiguration?.SaveTop3DPointCloudCsv ?? true;
        }

        private readonly struct PointCloudRow
        {
            public PointCloudRow(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
        }

        public static Bitmap MakeGrayscale(Bitmap original)
        {
            if (original == null)
            {
                throw new ArgumentNullException(nameof(original));
            }

            using (Bitmap sourceBitmap = CloneBitmap(original))
            unsafe
            {
                Bitmap newBitmap = new Bitmap(sourceBitmap.Width, sourceBitmap.Height, PixelFormat.Format24bppRgb);

                BitmapData originalData = sourceBitmap.LockBits(
                    new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                BitmapData newData = newBitmap.LockBits(
                    new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);

                int pixelSize = 3;

                for (int y = 0; y < sourceBitmap.Height; y++)
                {
                    byte* oRow = (byte*)originalData.Scan0 + (y * originalData.Stride);
                    byte* nRow = (byte*)newData.Scan0 + (y * newData.Stride);

                    for (int x = 0; x < sourceBitmap.Width; x++)
                    {
                        byte grayScale =
                            (byte)((oRow[x * pixelSize] * .11) +
                                   (oRow[x * pixelSize + 1] * .59) +
                                   (oRow[x * pixelSize + 2] * .3));

                        nRow[x * pixelSize] = grayScale;
                        nRow[x * pixelSize + 1] = grayScale;
                        nRow[x * pixelSize + 2] = grayScale;
                    }
                }

                newBitmap.UnlockBits(newData);
                sourceBitmap.UnlockBits(originalData);

                return newBitmap;
            }
        }



    }

}
