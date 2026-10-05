using Cognex.VisionPro;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Rendezvous dei JPEG annotati tra l'aggiornamento display e il worker immagini.
    ///
    /// Perche' esiste: catturare l'immagine annotata AL MOMENTO DEL SALVATAGGIO obbligava a
    /// forzare il ridisegno del CogRecordDisplay bloccando il thread UI. Da li' nascevano tre
    /// difetti distinti — immagine di solo sfondo, immagine del pezzo sbagliato, e pannello
    /// che su pezzo NOK restava fermo perche' il suo aggiornamento veniva scartato come stale
    /// mentre tenevamo occupato il dispatcher. Catturando invece dove e quando la vista viene
    /// aggiornata, il controllo dipinge nel suo ciclo normale e nessuno di quei problemi si
    /// presenta.
    ///
    /// L'associazione pezzo-immagine e' garantita dal record radice del risultato, non da un
    /// timestamp. Request chiude entrambe le corse possibili: se il render e' gia' avvenuto
    /// restituisce il JPEG subito; se deve ancora avvenire, Store completa la richiesta dello
    /// stesso record. Il worker applica comunque un timeout e ripiega sul grezzo.
    /// </summary>
    internal static class AnnotatedImageCache
    {
        internal sealed class RoleState
        {
            public readonly object SyncRoot = new object();
            public readonly List<Request> PendingRequests = new List<Request>();
            public ICogRecord LastRecord;
            public byte[] LastJpeg;
        }

        internal sealed class Request : IDisposable
        {
            private readonly RoleState _state;
            private readonly TaskCompletionSource<byte[]> _completion;
            private int _disposed;

            internal Request(RoleState state, ICogRecord record)
            {
                _state = state;
                Record = record;
                _completion = new TaskCompletionSource<byte[]>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal ICogRecord Record { get; }

            internal void Complete(byte[] jpeg)
            {
                _completion.TrySetResult(jpeg);
            }

            public bool TryWait(int timeoutMs, out byte[] jpeg)
            {
                jpeg = null;
                if (timeoutMs < 0)
                {
                    timeoutMs = 0;
                }

                try
                {
                    if (!_completion.Task.Wait(timeoutMs) ||
                        _completion.Task.Status != TaskStatus.RanToCompletion)
                    {
                        return false;
                    }

                    jpeg = _completion.Task.Result;
                    return jpeg != null && jpeg.Length > 0;
                }
                catch (AggregateException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                lock (_state.SyncRoot)
                {
                    _state.PendingRequests.Remove(this);
                }

                _completion.TrySetCanceled();
            }
        }

        private static readonly ConcurrentDictionary<string, RoleState> ByRole =
            new ConcurrentDictionary<string, RoleState>(StringComparer.OrdinalIgnoreCase);

        public static Request CreateRequest(string role, ICogRecord record)
        {
            if (string.IsNullOrWhiteSpace(role) || record == null)
            {
                return null;
            }

            RoleState state = ByRole.GetOrAdd(role, _ => new RoleState());
            var request = new Request(state, record);

            lock (state.SyncRoot)
            {
                if (object.ReferenceEquals(state.LastRecord, record) &&
                    state.LastJpeg != null &&
                    state.LastJpeg.Length > 0)
                {
                    request.Complete(state.LastJpeg);
                }
                else
                {
                    state.PendingRequests.Add(request);
                }
            }

            return request;
        }

        public static int Store(string role, ICogRecord record, byte[] jpeg)
        {
            if (string.IsNullOrWhiteSpace(role) || record == null || jpeg == null || jpeg.Length == 0)
            {
                return 0;
            }

            RoleState state = ByRole.GetOrAdd(role, _ => new RoleState());
            var completed = new List<Request>();

            lock (state.SyncRoot)
            {
                // Una sola immagine recente per ruolo basta per il caso producer-first. Le
                // richieste gia' registrate conservano invece il proprio JPEG fino al worker.
                state.LastRecord = record;
                state.LastJpeg = jpeg;

                for (int index = state.PendingRequests.Count - 1; index >= 0; index--)
                {
                    Request request = state.PendingRequests[index];
                    if (!object.ReferenceEquals(request.Record, record))
                    {
                        continue;
                    }

                    state.PendingRequests.RemoveAt(index);
                    completed.Add(request);
                }
            }

            foreach (Request request in completed)
            {
                request.Complete(jpeg);
            }

            return completed.Count;
        }

        public static void Clear()
        {
            foreach (RoleState state in ByRole.Values)
            {
                Request[] pending;
                lock (state.SyncRoot)
                {
                    pending = state.PendingRequests.ToArray();
                    state.PendingRequests.Clear();
                    state.LastRecord = null;
                    state.LastJpeg = null;
                }

                foreach (Request request in pending)
                {
                    request.Dispose();
                }
            }

            ByRole.Clear();
        }
    }

    /// <summary>
    /// Produce il bitmap annotato (immagine + grafica VisionPro) da un CogRecordDisplay.
    ///
    /// La documentazione VisionPro distingue chiaramente i contenuti: Display replica il
    /// viewport visibile, Image restituisce l'immagine completa non scalata con le annotazioni,
    /// Custom applica rettangolo e dimensione richiesti. Per il file diagnostico si usa quindi
    /// Image; Custom e Display restano solo fallback compatibili.
    /// </summary>
    internal static class AnnotatedImageRenderer
    {
        public static System.Drawing.Bitmap CreateContentBitmap(
            CogRecordDisplay display, out string failureReason)
        {
            failureReason = null;

            if (display == null)
            {
                failureReason = "display=null";
                return null;
            }

            try
            {
                var rendered = display.CreateContentBitmap(
                    Cognex.VisionPro.Display.CogDisplayContentBitmapConstants.Image,
                    null,
                    0) as System.Drawing.Bitmap;

                if (rendered != null)
                {
                    return rendered;
                }

                failureReason = "render-image=null";
            }
            catch (Exception ex)
            {
                failureReason = "render-image:" + ex.Message;
            }

            ICogImage image = null;
            try
            {
                image = display.Image;
            }
            catch (Exception ex)
            {
                failureReason = (failureReason ?? string.Empty) + "|display.Image:" + ex.Message;
            }

            if (image != null && image.Width > 0 && image.Height > 0)
            {
                try
                {
                    var contentRect = new CogRectangle();
                    contentRect.SetXYWidthHeight(0, 0, image.Width, image.Height);

                    var rendered = display.CreateContentBitmap(
                        Cognex.VisionPro.Display.CogDisplayContentBitmapConstants.Custom,
                        contentRect,
                        Math.Max(image.Width, image.Height)) as System.Drawing.Bitmap;

                    if (rendered != null)
                    {
                        return rendered;
                    }

                    failureReason = (failureReason ?? string.Empty) + "|render-custom=null";
                }
                catch (Exception ex)
                {
                    failureReason = (failureReason ?? string.Empty) + "|render-custom:" + ex.Message;
                }
            }
            else
            {
                failureReason = (failureReason ?? string.Empty) + "|display.Image=null";
            }

            // Ultimo ripiego sul viewport visibile per mantenere compatibilita' con runtime
            // Cognex che non supportano correttamente Image/Custom.
            try
            {
                var fallback = display.CreateContentBitmap(
                    Cognex.VisionPro.Display.CogDisplayContentBitmapConstants.Display)
                    as System.Drawing.Bitmap;

                if (fallback != null)
                {
                    return fallback;
                }
            }
            catch (Exception ex)
            {
                failureReason = (failureReason ?? string.Empty) + "|render-viewport:" + ex.Message;
            }

            return null;
        }
    }
}
