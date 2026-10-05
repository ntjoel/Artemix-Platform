using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QtisVisionPanel.Services
{
    public static class VisualPdfExportService
    {
        private const double RenderScale = 2.4;

        public static void ExportFrameworkElement(FrameworkElement visual, string outputPath, string title, string subtitle, string logoPath)
        {
            ExportFrameworkElements(new[] { visual }, outputPath, title, subtitle, logoPath);
        }

        public static void ExportFrameworkElements(IReadOnlyList<FrameworkElement> visuals, string outputPath, string title, string subtitle, string logoPath)
        {
            if (visuals == null)
            {
                throw new ArgumentNullException(nameof(visuals));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            }

            var captures = new List<VisualCapture>();
            foreach (FrameworkElement visual in visuals)
            {
                if (visual == null || visual.Visibility != Visibility.Visible)
                {
                    continue;
                }

                visual.UpdateLayout();
                if (visual.ActualWidth <= 20 || visual.ActualHeight <= 20)
                {
                    continue;
                }

                VisualCapture capture = CaptureVisual(visual);
                if (capture != null)
                {
                    captures.Add(capture);
                }
            }

            if (captures.Count == 0)
            {
                throw new InvalidOperationException("No visible chart is available to export.");
            }

            var builder = new PdfBuilder();
            var layout = new VisualPdfLayout(builder, title, subtitle, logoPath);
            layout.AddCards(captures);
            builder.Save(outputPath);
        }

        private static VisualCapture CaptureVisual(FrameworkElement visual)
        {
            double sourceWidth = Math.Ceiling(visual.ActualWidth);
            double sourceHeight = Math.Ceiling(visual.ActualHeight);
            if (sourceWidth <= 1 || sourceHeight <= 1)
            {
                return null;
            }

            int pixelWidth = Math.Max(1, (int)Math.Ceiling(sourceWidth * RenderScale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(sourceHeight * RenderScale));

            var drawingVisual = new DrawingVisual();
            using (DrawingContext context = drawingVisual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, sourceWidth, sourceHeight));
                var brush = new VisualBrush(visual)
                {
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                context.DrawRectangle(brush, null, new Rect(0, 0, sourceWidth, sourceHeight));
            }

            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
            bitmap.Render(drawingVisual);
            bitmap.Freeze();

            PdfImage image = PdfImage.FromBitmapJpeg(bitmap, 96);
            if (image == null)
            {
                return null;
            }

            return new VisualCapture
            {
                Image = image,
                LogicalWidth = sourceWidth,
                LogicalHeight = sourceHeight
            };
        }

        private sealed class VisualPdfLayout
        {
            private const double PageWidth = PdfBuilder.PageWidth;
            private const double PageHeight = PdfBuilder.PageHeight;
            private const double Margin = 30;
            private const double HeaderTop = 22;
            private const double HeaderHeight = 66;
            private const double ContentTop = 94;
            private const double ContentWidth = PageWidth - (Margin * 2);
            private const double ContentHeight = PageHeight - ContentTop - 40;
            private const double CardGap = 14;

            private readonly PdfBuilder _pdf;
            private readonly string _title;
            private readonly string _subtitle;
            private readonly string _logoPath;
            private int _pageNumber;
            private int _totalPages;

            public VisualPdfLayout(PdfBuilder pdf, string title, string subtitle, string logoPath)
            {
                _pdf = pdf;
                _title = string.IsNullOrWhiteSpace(title) ? "Data Analysis" : title;
                _subtitle = subtitle ?? string.Empty;
                _logoPath = logoPath;
            }

            public void AddCards(IReadOnlyList<VisualCapture> captures)
            {
                _totalPages = (captures.Count + 1) / 2;
                for (int i = 0; i < captures.Count; i += 2)
                {
                    int cardsOnPage = Math.Min(2, captures.Count - i);
                    StartPage(i + 1, cardsOnPage, captures.Count);

                    double slotHeight = cardsOnPage == 1
                        ? ContentHeight
                        : (ContentHeight - CardGap) / 2;

                    for (int slotIndex = 0; slotIndex < cardsOnPage; slotIndex++)
                    {
                        double slotTop = ContentTop + (slotIndex * (slotHeight + CardGap));
                        AddCard(captures[i + slotIndex], slotTop, slotHeight);
                    }
                }
            }

            private void AddCard(VisualCapture capture, double slotTop, double slotHeight)
            {
                double scale = Math.Min(ContentWidth / capture.LogicalWidth, slotHeight / capture.LogicalHeight);
                double drawWidth = capture.LogicalWidth * scale;
                double drawHeight = capture.LogicalHeight * scale;
                double x = Margin + ((ContentWidth - drawWidth) / 2);
                double top = slotTop + ((slotHeight - drawHeight) / 2);

                _pdf.DrawRectangle(x - 8, top - 8, drawWidth + 16, drawHeight + 16, 0xFFFFFF, 0xD6E2F0);
                _pdf.DrawImage(capture.Image, x, top, drawWidth, drawHeight);
            }

            private void StartPage(int firstCardNumber, int cardsOnPage, int totalCards)
            {
                _pdf.AddPage();
                _pageNumber++;
                _pdf.DrawRectangle(0, 0, PageWidth, PageHeight, 0xF7FAFD, 0xF7FAFD);

                if (!string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath))
                {
                    _pdf.DrawImage(_logoPath, Margin, HeaderTop, 128, 42);
                }

                _pdf.DrawText(178, 25, _title, 16, true, PageWidth - 265);
                _pdf.DrawText(178, 48, _subtitle, 9, false, PageWidth - 265);
                _pdf.DrawText(PageWidth - 146, 27, DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), 8, false, 116);
                int lastCardNumber = firstCardNumber + cardsOnPage - 1;
                string cardRange = cardsOnPage == 1
                    ? "Grafico " + firstCardNumber.ToString(CultureInfo.InvariantCulture)
                    : "Grafici " + firstCardNumber.ToString(CultureInfo.InvariantCulture) + "-" + lastCardNumber.ToString(CultureInfo.InvariantCulture);
                _pdf.DrawText(PageWidth - 146, 47, cardRange + " / " + totalCards.ToString(CultureInfo.InvariantCulture), 8, true, 116);
                _pdf.DrawLine(Margin, HeaderHeight + 8, PageWidth - Margin, HeaderHeight + 8, 0.8, 0xCAD8E6);
                _pdf.DrawText(
                    PageWidth - 92,
                    PageHeight - 24,
                    "Pag. " + _pageNumber.ToString(CultureInfo.InvariantCulture) + " / " + _totalPages.ToString(CultureInfo.InvariantCulture),
                    8,
                    false,
                    64);
            }
        }

        private sealed class VisualCapture
        {
            public PdfImage Image { get; set; }
            public double LogicalWidth { get; set; }
            public double LogicalHeight { get; set; }
        }

        private sealed class PdfBuilder
        {
            public const double PageWidth = 842;
            public const double PageHeight = 595;

            private readonly List<PdfPage> _pages = new List<PdfPage>();
            private readonly Dictionary<string, PdfImage> _fileImages = new Dictionary<string, PdfImage>(StringComparer.OrdinalIgnoreCase);
            private readonly List<PdfImage> _images = new List<PdfImage>();
            private PdfPage _currentPage;

            public void AddPage()
            {
                _currentPage = new PdfPage();
                _pages.Add(_currentPage);
            }

            public PdfImage GetImage(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                if (!_fileImages.TryGetValue(path, out PdfImage image))
                {
                    image = PdfImage.FromFile(path);
                    if (image != null)
                    {
                        RegisterImage(image);
                        _fileImages[path] = image;
                    }
                }

                return image;
            }

            public void DrawText(double x, double top, string text, int size, bool bold, double maxWidth)
            {
                if (_currentPage == null || string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                string clipped = ClipText(text, maxWidth, size);
                string font = bold ? "F2" : "F1";
                double y = PageHeight - top - size;
                _currentPage.Content.Append("0.063 0.165 0.263 rg\n");
                _currentPage.Content.AppendFormat(CultureInfo.InvariantCulture, "BT /{0} {1} Tf {2:0.##} {3:0.##} Td ({4}) Tj ET\n",
                    font, size, x, y, EscapePdfText(clipped));
            }

            public void DrawImage(string path, double x, double top, double width, double height)
            {
                DrawImage(GetImage(path), x, top, width, height);
            }

            public void DrawImage(PdfImage image, double x, double top, double width, double height)
            {
                if (_currentPage == null || image == null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(image.Name))
                {
                    RegisterImage(image);
                }

                _currentPage.Images.Add(image);
                double y = PageHeight - top - height;
                _currentPage.Content.AppendFormat(CultureInfo.InvariantCulture, "q {0:0.##} 0 0 {1:0.##} {2:0.##} {3:0.##} cm /{4} Do Q\n",
                    width, height, x, y, image.Name);
            }

            public void DrawLine(double x1, double top1, double x2, double top2, double width, int color)
            {
                if (_currentPage == null)
                {
                    return;
                }

                AppendStrokeColor(color);
                _currentPage.Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} w {1:0.##} {2:0.##} m {3:0.##} {4:0.##} l S\n",
                    width, x1, PageHeight - top1, x2, PageHeight - top2);
            }

            public void DrawRectangle(double x, double top, double width, double height, int fillColor, int strokeColor)
            {
                if (_currentPage == null)
                {
                    return;
                }

                AppendFillColor(fillColor);
                AppendStrokeColor(strokeColor);
                _currentPage.Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##} {3:0.##} re B\n",
                    x, PageHeight - top - height, width, height);
            }

            public void Save(string outputPath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

                var objects = new List<PdfObject>();
                int catalogId = AddObject(objects, string.Empty);
                int pagesId = AddObject(objects, string.Empty);
                int fontRegularId = AddObject(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
                int fontBoldId = AddObject(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

                foreach (PdfImage image in _images)
                {
                    image.ObjectId = objects.Count + 1;
                    objects.Add(PdfObject.StreamObject(BuildImageDictionary(image), image.Data));
                }

                var pageIds = new List<int>();
                foreach (PdfPage page in _pages)
                {
                    byte[] contentBytes = Encoding.ASCII.GetBytes(page.Content.ToString());
                    int contentId = objects.Count + 1;
                    objects.Add(PdfObject.StreamObject("<< /Length " + contentBytes.Length.ToString(CultureInfo.InvariantCulture) + " >>", contentBytes));

                    int pageId = objects.Count + 1;
                    pageIds.Add(pageId);
                    objects.Add(new PdfObject(BuildPageDictionary(pagesId, fontRegularId, fontBoldId, contentId, page.Images)));
                }

                objects[catalogId - 1] = new PdfObject("<< /Type /Catalog /Pages " + pagesId.ToString(CultureInfo.InvariantCulture) + " 0 R >>");
                objects[pagesId - 1] = new PdfObject(BuildPagesDictionary(pageIds));

                WritePdf(outputPath, objects);
            }

            private void RegisterImage(PdfImage image)
            {
                image.Name = "Im" + (_images.Count + 1).ToString(CultureInfo.InvariantCulture);
                _images.Add(image);
            }

            private static int AddObject(List<PdfObject> objects, string content)
            {
                objects.Add(new PdfObject(content));
                return objects.Count;
            }

            private static string BuildPagesDictionary(List<int> pageIds)
            {
                string kids = string.Join(" ", pageIds.Select(id => id.ToString(CultureInfo.InvariantCulture) + " 0 R"));
                return "<< /Type /Pages /Count " + pageIds.Count.ToString(CultureInfo.InvariantCulture) + " /Kids [ " + kids + " ] >>";
            }

            private static string BuildPageDictionary(int pagesId, int fontRegularId, int fontBoldId, int contentId, HashSet<PdfImage> images)
            {
                var imageResources = new StringBuilder();
                foreach (PdfImage image in images)
                {
                    imageResources.AppendFormat(CultureInfo.InvariantCulture, "/{0} {1} 0 R ", image.Name, image.ObjectId);
                }

                return string.Format(CultureInfo.InvariantCulture,
                    "<< /Type /Page /Parent {0} 0 R /MediaBox [0 0 {1:0} {2:0}] /Resources << /Font << /F1 {3} 0 R /F2 {4} 0 R >> /XObject << {5} >> >> /Contents {6} 0 R >>",
                    pagesId, PageWidth, PageHeight, fontRegularId, fontBoldId, imageResources, contentId);
            }

            private static string BuildImageDictionary(PdfImage image)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>",
                    image.Width, image.Height, image.Data.Length);
            }

            private static void WritePdf(string outputPath, List<PdfObject> objects)
            {
                using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(stream, Encoding.ASCII))
                {
                    writer.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n"));
                    var offsets = new List<long> { 0 };

                    for (int i = 0; i < objects.Count; i++)
                    {
                        offsets.Add(stream.Position);
                        writer.Write(Encoding.ASCII.GetBytes((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
                        objects[i].Write(writer);
                        writer.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
                    }

                    long xrefPosition = stream.Position;
                    writer.Write(Encoding.ASCII.GetBytes("xref\n0 " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n"));
                    writer.Write(Encoding.ASCII.GetBytes("0000000000 65535 f \n"));
                    for (int i = 1; i < offsets.Count; i++)
                    {
                        writer.Write(Encoding.ASCII.GetBytes(offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n"));
                    }

                    writer.Write(Encoding.ASCII.GetBytes("trailer\n<< /Size " + (objects.Count + 1).ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R >>\n"));
                    writer.Write(Encoding.ASCII.GetBytes("startxref\n" + xrefPosition.ToString(CultureInfo.InvariantCulture) + "\n%%EOF"));
                }
            }

            private void AppendFillColor(int color)
            {
                double r = ((color >> 16) & 0xFF) / 255.0;
                double g = ((color >> 8) & 0xFF) / 255.0;
                double b = (color & 0xFF) / 255.0;
                _currentPage.Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} rg\n", r, g, b);
            }

            private void AppendStrokeColor(int color)
            {
                double r = ((color >> 16) & 0xFF) / 255.0;
                double g = ((color >> 8) & 0xFF) / 255.0;
                double b = (color & 0xFF) / 255.0;
                _currentPage.Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} RG\n", r, g, b);
            }

            private static string ClipText(string text, double maxWidth, int fontSize)
            {
                if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0)
                {
                    return text;
                }

                int maxChars = Math.Max(12, (int)(maxWidth / (fontSize * 0.48)));
                return text.Length <= maxChars ? text : text.Substring(0, Math.Max(0, maxChars - 3)) + "...";
            }

            private static string EscapePdfText(string text)
            {
                string normalized = (text ?? string.Empty)
                    .Replace("à", "a'")
                    .Replace("è", "e'")
                    .Replace("é", "e'")
                    .Replace("ì", "i'")
                    .Replace("ò", "o'")
                    .Replace("ù", "u'")
                    .Replace("À", "A'")
                    .Replace("È", "E'")
                    .Replace("É", "E'")
                    .Replace("Ì", "I'")
                    .Replace("Ò", "O'")
                    .Replace("Ù", "U'");

                var builder = new StringBuilder();
                foreach (char ch in normalized)
                {
                    if (ch == '\\' || ch == '(' || ch == ')')
                    {
                        builder.Append('\\');
                        builder.Append(ch);
                    }
                    else if (ch >= 32 && ch <= 126)
                    {
                        builder.Append(ch);
                    }
                    else
                    {
                        builder.Append(' ');
                    }
                }

                return builder.ToString();
            }
        }

        private sealed class PdfPage
        {
            public StringBuilder Content { get; } = new StringBuilder();
            public HashSet<PdfImage> Images { get; } = new HashSet<PdfImage>();
        }

        private sealed class PdfImage
        {
            public string Name { get; set; }
            public int ObjectId { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public byte[] Data { get; set; }
            public static PdfImage FromFile(string path)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    using (var memoryStream = new MemoryStream())
                    {
                        encoder.Save(memoryStream);
                        return new PdfImage
                        {
                            Width = bitmap.PixelWidth,
                            Height = bitmap.PixelHeight,
                            Data = memoryStream.ToArray()
                        };
                    }
                }
                catch
                {
                    return null;
                }
            }

            public static PdfImage FromBitmapJpeg(BitmapSource bitmap, int qualityLevel)
            {
                if (bitmap == null)
                {
                    return null;
                }

                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
                converted.Freeze();

                var encoder = new JpegBitmapEncoder { QualityLevel = Math.Max(80, Math.Min(100, qualityLevel)) };
                encoder.Frames.Add(BitmapFrame.Create(converted));

                using (var memoryStream = new MemoryStream())
                {
                    encoder.Save(memoryStream);
                    return new PdfImage
                    {
                        Width = converted.PixelWidth,
                        Height = converted.PixelHeight,
                        Data = memoryStream.ToArray()
                    };
                }
            }
        }

        private sealed class PdfObject
        {
            private readonly string _content;
            private readonly byte[] _stream;

            public PdfObject(string content)
            {
                _content = content;
            }

            private PdfObject(string dictionary, byte[] stream)
            {
                _content = dictionary;
                _stream = stream;
            }

            public static PdfObject StreamObject(string dictionary, byte[] stream)
            {
                return new PdfObject(dictionary, stream);
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(Encoding.ASCII.GetBytes(_content));
                if (_stream == null)
                {
                    return;
                }

                writer.Write(Encoding.ASCII.GetBytes("\nstream\n"));
                writer.Write(_stream);
                writer.Write(Encoding.ASCII.GetBytes("\nendstream"));
            }
        }
    }
}
