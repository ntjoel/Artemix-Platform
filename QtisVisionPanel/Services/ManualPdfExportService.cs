using QtisVisionPanel.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;

namespace QtisVisionPanel.Services
{
    public static class ManualPdfExportService
    {
        public static void Export(ManualDocumentItem document, string outputPath, string logoPath)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var builder = new PdfBuilder();
            var layout = new ManualPdfLayout(builder, document.Title, logoPath);
            layout.AddDocument(document);
            builder.Save(outputPath);
        }

        private sealed class ManualPdfLayout
        {
            private const double ContentWidth = 515;
            private readonly PdfBuilder _pdf;
            private readonly string _documentTitle;
            private readonly string _logoPath;
            private double _y;

            public ManualPdfLayout(PdfBuilder pdf, string documentTitle, string logoPath)
            {
                _pdf = pdf;
                _documentTitle = string.IsNullOrWhiteSpace(documentTitle) ? "Manuale Operatore" : documentTitle;
                _logoPath = logoPath;
                StartPage();
            }

            public void AddDocument(ManualDocumentItem document)
            {
                AddHeading(document.Title, 20);
                AddWrappedText(document.Description, 11, false, 14);
                _y += 6;

                AddImage(document.PreviewImagePath, 260);
                AddWrappedText(document.PreviewImageCaption, 9, false, 11);
                _y += 8;

                foreach (ManualDocumentSectionItem section in document.Sections)
                {
                    AddSection(section);
                }
            }

            private void AddSection(ManualDocumentSectionItem section)
            {
                EnsureSpace(110);
                AddHeading(section.Heading, 15);
                AddImage(section.ImagePath, 220);
                AddWrappedText(section.ImageCaption, 9, false, 11);

                if (section.HasFunctionRows)
                {
                    AddFunctionTable(section.FunctionRows);
                }

                AddWrappedText(section.Body, 10, false, 13);
                _y += 8;
            }

            private void StartPage()
            {
                _pdf.AddPage();
                _y = 34;

                if (!string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath))
                {
                    _pdf.DrawImage(_logoPath, 40, _y, 132, 43);
                }

                _pdf.DrawText(190, _y + 8, _documentTitle, 13, true, 365);
                _pdf.DrawText(190, _y + 27, DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), 8, false, 365);
                _pdf.DrawLine(40, 84, 555, 84, 0.7, 0xD6E2F0);
                _y = 102;
            }

            private void EnsureSpace(double requiredHeight)
            {
                if (_y + requiredHeight > PdfBuilder.PageHeight - 45)
                {
                    StartPage();
                }
            }

            private void AddHeading(string text, int size)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                EnsureSpace(size + 18);
                _pdf.DrawText(40, _y, text, size, true, ContentWidth);
                _y += size + 10;
            }

            private void AddWrappedText(string text, int size, bool bold, double lineHeight)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
                {
                    string trimmed = paragraph.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        _y += lineHeight * 0.5;
                        continue;
                    }

                    foreach (string line in Wrap(trimmed, ContentWidth, size))
                    {
                        EnsureSpace(lineHeight + 4);
                        _pdf.DrawText(40, _y, line, size, bold, ContentWidth);
                        _y += lineHeight;
                    }
                }
            }

            private void AddImage(string imagePath, double maxHeight)
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    return;
                }

                PdfImage image = _pdf.GetImage(imagePath);
                if (image == null || image.Width <= 0 || image.Height <= 0)
                {
                    return;
                }

                double width = Math.Min(ContentWidth, image.Width);
                double height = width * image.Height / image.Width;
                if (height > maxHeight)
                {
                    height = maxHeight;
                    width = height * image.Width / image.Height;
                }

                EnsureSpace(height + 18);
                _pdf.DrawRectangle(40, _y, ContentWidth, height + 10, 0xF8FBFF, 0xD6E2F0);
                _pdf.DrawImage(imagePath, 40 + (ContentWidth - width) / 2, _y + 5, width, height);
                _y += height + 18;
            }

            private void AddFunctionTable(IEnumerable<ManualFunctionRowItem> rows)
            {
                var rowList = rows.Where(row => row != null).ToList();
                if (rowList.Count == 0)
                {
                    return;
                }

                double[] widths = { 32, 98, 130, 135, 120 };
                string[] headers = { "ID", "Funzione", "Cosa significa", "Cosa fare", "Quando chiamare supporto" };
                EnsureSpace(34);
                DrawTableRow(headers, widths, true, 28, 0xEAF2FB);

                foreach (ManualFunctionRowItem row in rowList)
                {
                    string[] values =
                    {
                        row.Id,
                        row.Function,
                        row.Meaning,
                        row.OperatorAction,
                        row.SupportCondition
                    };

                    int maxLines = 1;
                    for (int i = 0; i < values.Length; i++)
                    {
                        maxLines = Math.Max(maxLines, Wrap(values[i], widths[i] - 10, 8).Count);
                    }

                    double rowHeight = Math.Max(24, maxLines * 10 + 12);
                    EnsureSpace(rowHeight + 4);
                    DrawTableRow(values, widths, false, rowHeight, 0xFFFFFF);
                }

                _y += 10;
            }

            private void DrawTableRow(string[] values, double[] widths, bool header, double height, int backgroundColor)
            {
                double x = 40;
                for (int i = 0; i < widths.Length; i++)
                {
                    _pdf.DrawRectangle(x, _y, widths[i], height, backgroundColor, 0xD6E2F0);
                    double textY = _y + 7;
                    foreach (string line in Wrap(values[i], widths[i] - 10, header ? 8 : 8))
                    {
                        if (textY + 9 > _y + height)
                        {
                            break;
                        }

                        _pdf.DrawText(x + 5, textY, line, 8, header, widths[i] - 10);
                        textY += 10;
                    }

                    x += widths[i];
                }

                _y += height;
            }

            private static List<string> Wrap(string text, double width, int fontSize)
            {
                var lines = new List<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(string.Empty);
                    return lines;
                }

                int maxChars = Math.Max(12, (int)(width / (fontSize * 0.48)));
                var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var line = new StringBuilder();

                foreach (string word in words)
                {
                    if (line.Length == 0)
                    {
                        line.Append(word);
                        continue;
                    }

                    if (line.Length + word.Length + 1 > maxChars)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }

                    if (line.Length > 0)
                    {
                        line.Append(' ');
                    }
                    line.Append(word);
                }

                if (line.Length > 0)
                {
                    lines.Add(line.ToString());
                }

                return lines;
            }
        }

        private sealed class PdfBuilder
        {
            public const double PageWidth = 595;
            public const double PageHeight = 842;

            private readonly List<PdfPage> _pages = new List<PdfPage>();
            private readonly Dictionary<string, PdfImage> _images = new Dictionary<string, PdfImage>(StringComparer.OrdinalIgnoreCase);
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

                if (!_images.TryGetValue(path, out PdfImage image))
                {
                    image = PdfImage.FromFile(path);
                    if (image != null)
                    {
                        image.Name = "Im" + (_images.Count + 1).ToString(CultureInfo.InvariantCulture);
                        _images[path] = image;
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

                string font = bold ? "F2" : "F1";
                double y = PageHeight - top - size;
                _currentPage.Content.Append("0.063 0.165 0.263 rg\n");
                _currentPage.Content.AppendFormat(CultureInfo.InvariantCulture, "BT /{0} {1} Tf {2:0.##} {3:0.##} Td ({4}) Tj ET\n",
                    font, size, x, y, EscapePdfText(text));
            }

            public void DrawImage(string path, double x, double top, double width, double height)
            {
                if (_currentPage == null)
                {
                    return;
                }

                PdfImage image = GetImage(path);
                if (image == null)
                {
                    return;
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
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    throw new ArgumentException("Output path is required.", nameof(outputPath));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

                var objects = new List<PdfObject>();
                int catalogId = AddObject(objects, string.Empty);
                int pagesId = AddObject(objects, string.Empty);
                int fontRegularId = AddObject(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
                int fontBoldId = AddObject(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

                foreach (PdfImage image in _images.Values)
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

            private static string EscapePdfText(string text)
            {
                string normalized = text
                    .Replace("à", "a'")
                    .Replace("è", "e'")
                    .Replace("é", "e'")
                    .Replace("ì", "i'")
                    .Replace("ò", "o'")
                    .Replace("ù", "u'");

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

                    var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
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
