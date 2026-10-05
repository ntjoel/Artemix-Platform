using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace QtisVisionPanel.ViewModels
{
    public class ManualFunctionRowItem
    {
        public string Id { get; set; }
        public string Function { get; set; }
        public string Meaning { get; set; }
        public string OperatorAction { get; set; }
        public string SupportCondition { get; set; }
    }

    public class ManualDocumentSectionItem
    {
        public string Heading { get; set; }
        public string Body { get; set; }
        public string ImagePath { get; set; }
        public string ImageCaption { get; set; }
        public ObservableCollection<ManualFunctionRowItem> FunctionRows { get; } = new ObservableCollection<ManualFunctionRowItem>();
        public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);
        public bool HasFunctionRows => FunctionRows.Count > 0;
    }

    public class ManualDocumentItem
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string FilePath { get; set; }
        public string PreviewImagePath { get; set; }
        public string PreviewImageCaption { get; set; }
        public ObservableCollection<ManualDocumentSectionItem> Sections { get; } = new ObservableCollection<ManualDocumentSectionItem>();
    }

    public class ManualViewModel : INotifyPropertyChanged
    {
        private ManualDocumentItem _selectedDocument;
        private string _manualFolderPath;
        private double _zoomLevel = 1.0;
        private readonly ObservableCollection<ManualDocumentSectionItem> _emptySections = new ObservableCollection<ManualDocumentSectionItem>();

        public ObservableCollection<ManualDocumentItem> Documents { get; } = new ObservableCollection<ManualDocumentItem>();

        public ICommand RefreshDocumentsCommand { get; }
        public ICommand OpenManualFolderCommand { get; }
        public ICommand SelectDocumentCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand ExportPdfCommand { get; }

        public ManualDocumentItem SelectedDocument
        {
            get => _selectedDocument;
            set
            {
                if (_selectedDocument != value)
                {
                    _selectedDocument = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentTitle));
                    OnPropertyChanged(nameof(CurrentDescription));
                    OnPropertyChanged(nameof(CurrentSections));
                    OnPropertyChanged(nameof(CurrentHeroImagePath));
                    OnPropertyChanged(nameof(CurrentHeroImageCaption));
                    OnPropertyChanged(nameof(HasCurrentHeroImage));
                }
            }
        }

        public string CurrentTitle => SelectedDocument != null ? SelectedDocument.Title : GetMessage("Sub_entry_ManualTitle", "Operator Manual");
        public string CurrentDescription => SelectedDocument != null ? SelectedDocument.Description : GetMessage("Sub_entry_ManualFallbackDescription", "Operator overview");
        public ObservableCollection<ManualDocumentSectionItem> CurrentSections => SelectedDocument != null ? SelectedDocument.Sections : _emptySections;
        public string CurrentHeroImagePath => SelectedDocument != null ? SelectedDocument.PreviewImagePath : null;
        public string CurrentHeroImageCaption => SelectedDocument != null ? SelectedDocument.PreviewImageCaption : null;
        public bool HasCurrentHeroImage => !string.IsNullOrWhiteSpace(CurrentHeroImagePath) && File.Exists(CurrentHeroImagePath);
        public string ManualTableHeaderId => GetMessage("Sub_entry_ManualTableHeaderId", "ID");
        public string ManualTableHeaderFunction => GetMessage("Sub_entry_ManualTableHeaderFunction", "Function");
        public string ManualTableHeaderMeaning => GetMessage("Sub_entry_ManualTableHeaderMeaning", "Meaning");
        public string ManualTableHeaderAction => GetMessage("Sub_entry_ManualTableHeaderAction", "Operator action");
        public string ManualTableHeaderSupport => GetMessage("Sub_entry_ManualTableHeaderSupport", "When to call support");
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                double boundedValue = Math.Max(0.8, Math.Min(1.8, value));
                if (Math.Abs(_zoomLevel - boundedValue) > 0.001)
                {
                    _zoomLevel = boundedValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ZoomPercentage));
                }
            }
        }

        public string ZoomPercentage => Math.Round(ZoomLevel * 100).ToString("0") + "%";

        public ManualViewModel()
        {
            RefreshDocumentsCommand = new QtisVisionPanel.Models.RelayCommand(LoadDocuments);
            OpenManualFolderCommand = new QtisVisionPanel.Models.RelayCommand(OpenManualFolder);
            SelectDocumentCommand = new QtisVisionPanel.Models.RelayCommand<ManualDocumentItem>(SelectDocument);
            ZoomInCommand = new QtisVisionPanel.Models.RelayCommand(() => ZoomLevel += 0.1);
            ZoomOutCommand = new QtisVisionPanel.Models.RelayCommand(() => ZoomLevel -= 0.1);
            ResetZoomCommand = new QtisVisionPanel.Models.RelayCommand(() => ZoomLevel = 1.0);
            ExportPdfCommand = new QtisVisionPanel.Models.RelayCommand(ExportSelectedDocumentToPdf);

            LoadDocuments();
        }

        private void LoadDocuments()
        {
            Documents.Clear();
            _manualFolderPath = ResolveManualFolderPath();

            if (!string.IsNullOrWhiteSpace(_manualFolderPath) && Directory.Exists(_manualFolderPath))
            {
                var markdownFiles = Directory
                    .GetFiles(_manualFolderPath, "*.md", SearchOption.TopDirectoryOnly)
                    .Where(IsNumberedOperatorManual)
                    .OrderBy(path => path)
                    .ToList();

                var plainFiles = Directory
                    .GetFiles(_manualFolderPath, "*.txt", SearchOption.TopDirectoryOnly)
                    .Where(IsNumberedOperatorManual)
                    .OrderBy(path => path)
                    .ToList();

                var files = markdownFiles.Any() ? markdownFiles : plainFiles;

                foreach (var file in files)
                {
                    Documents.Add(file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        ? LoadMarkdownDocument(file)
                        : LoadPlainTextDocument(file));
                }
            }

            if (Documents.Count == 0)
            {
                Documents.Add(CreateFallbackDocument());
            }

            SelectedDocument = Documents.FirstOrDefault();
        }

        private static bool IsNumberedOperatorManual(string path)
        {
            string fileName = Path.GetFileName(path);
            return fileName != null &&
                   fileName.Length > 3 &&
                   char.IsDigit(fileName[0]) &&
                   char.IsDigit(fileName[1]) &&
                   fileName[2] == '_';
        }

        private void SelectDocument(ManualDocumentItem document)
        {
            if (document != null)
            {
                SelectedDocument = document;
            }
        }

        private void OpenManualFolder()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_manualFolderPath) || !Directory.Exists(_manualFolderPath))
                {
                    new SystemNotificationWindow(
                        GetMessage("Sub_entry_ManualFolderCaption", "Manual"),
                        GetMessage("Sub_entry_ManualFolderUnavailable", "Manual folder not available."),
                        NotificationSeverity.Info).ShowDialog();
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = _manualFolderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error("Errore apertura cartella manuali: " + ex.Message);
                new SystemNotificationWindow(
                    GetMessage("Sub_entry_ManualFolderCaption", "Manual"),
                    GetMessage("Sub_entry_ManualFolderOpenError", "Error while opening manual folder:") + " " + ex.Message,
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private void ExportSelectedDocumentToPdf()
        {
            try
            {
                if (SelectedDocument == null)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = GetMessage("Sub_entry_ManualExportPdf", "Export PDF"),
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = BuildSafeFileName(SelectedDocument.Title) + ".pdf",
                    AddExtension = true,
                    DefaultExt = ".pdf",
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                ManualPdfExportService.Export(SelectedDocument, dialog.FileName, ResolveManualLogoPath());

                new SystemNotificationWindow(
                    GetMessage("Sub_entry_ManualFolderCaption", "Manual"),
                    GetMessage("Sub_entry_ManualPdfExportSuccess", "Manual PDF exported successfully."),
                    NotificationSeverity.Info).ShowDialog();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error("Errore esportazione PDF manuale: " + ex.Message);
                new SystemNotificationWindow(
                    GetMessage("Sub_entry_ManualFolderCaption", "Manual"),
                    GetMessage("Sub_entry_ManualPdfExportError", "Error while exporting manual PDF:") + " " + ex.Message,
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private string ResolveManualLogoPath()
        {
            if (!string.IsNullOrWhiteSpace(_manualFolderPath))
            {
                string manualLogoPath = Path.Combine(_manualFolderPath, "Images", "Pulsar_Engineering_logo.png");
                if (File.Exists(manualLogoPath))
                {
                    return manualLogoPath;
                }
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "resources", "logo_pulsar.png"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "resources", "logo_pulsar.png")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "resources", "logo_pulsar.png"))
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string BuildSafeFileName(string title)
        {
            string value = string.IsNullOrWhiteSpace(title) ? "Manuale_Operatore" : title;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value.Replace(' ', '_');
        }

        private string ResolveManualFolderPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "Docs", "Manual"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "Docs", "Manual")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "Docs", "Manual"))
            };

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private string BuildTitle(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath).Replace("_", " ");
        }

        private string BuildDescription(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

            if (fileName.Contains("overview") || fileName.Contains("panoramica"))
                return GetMessage("Sub_entry_ManualDocumentsHint", "Quick procedures, startup, recipes and alarm handling");
            if (fileName.Contains("start") || fileName.Contains("stop"))
                return GetMessage("Sub_entry_ManualStartStopDescription", "Startup, stop and production monitoring");
            if (fileName.Contains("alarm") || fileName.Contains("allarm"))
                return GetMessage("Sub_entry_ManualAlarmSupportDescription", "Reject handling, alarms and support");

            return GetMessage("Sub_entry_ManualGenericDescription", "Operator document");
        }

        private ManualDocumentItem LoadPlainTextDocument(string filePath)
        {
            var document = new ManualDocumentItem
            {
                Title = BuildTitle(filePath),
                Description = BuildDescription(filePath),
                FilePath = filePath
            };

            document.Sections.Add(new ManualDocumentSectionItem
            {
                Heading = document.Title,
                Body = File.ReadAllText(filePath)
            });

            return document;
        }

        private ManualDocumentItem LoadMarkdownDocument(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            int index = 0;
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (lines.Length > 0 && lines[0].Trim() == "---")
            {
                index = 1;
                while (index < lines.Length && lines[index].Trim() != "---")
                {
                    string line = lines[index];
                    int separatorIndex = line.IndexOf(':');
                    if (separatorIndex > 0)
                    {
                        string key = line.Substring(0, separatorIndex).Trim();
                        string value = line.Substring(separatorIndex + 1).Trim();
                        metadata[key] = value;
                    }

                    index++;
                }

                if (index < lines.Length && lines[index].Trim() == "---")
                {
                    index++;
                }
            }

            var document = new ManualDocumentItem
            {
                Title = metadata.ContainsKey("title") ? metadata["title"] : BuildTitle(filePath),
                Description = metadata.ContainsKey("description") ? metadata["description"] : BuildDescription(filePath),
                FilePath = filePath,
                PreviewImagePath = ResolveImagePath(filePath, metadata.ContainsKey("image") ? metadata["image"] : null),
                PreviewImageCaption = metadata.ContainsKey("image_caption") ? metadata["image_caption"] : null
            };

            var currentSection = new ManualDocumentSectionItem();
            var bodyBuilder = new StringBuilder();

            for (int i = index; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (trimmed.StartsWith("# "))
                {
                    if (string.IsNullOrWhiteSpace(document.Title))
                    {
                        document.Title = trimmed.Substring(2).Trim();
                    }
                    continue;
                }

                if (trimmed.StartsWith("## "))
                {
                    FlushSection(document, currentSection, bodyBuilder);
                    currentSection = new ManualDocumentSectionItem
                    {
                        Heading = trimmed.Substring(3).Trim()
                    };
                    continue;
                }

                string imageAltText;
                string imagePath;
                if (TryParseImageLine(trimmed, out imageAltText, out imagePath))
                {
                    string resolvedImagePath = ResolveImagePath(filePath, imagePath);
                    if (string.IsNullOrWhiteSpace(currentSection.Heading) &&
                        string.IsNullOrWhiteSpace(currentSection.Body) &&
                        string.IsNullOrWhiteSpace(currentSection.ImagePath) &&
                        string.IsNullOrWhiteSpace(document.PreviewImagePath))
                    {
                        document.PreviewImagePath = resolvedImagePath;
                        document.PreviewImageCaption = imageAltText;
                    }
                    else
                    {
                        currentSection.ImagePath = resolvedImagePath;
                        currentSection.ImageCaption = imageAltText;
                    }
                    continue;
                }

                bodyBuilder.AppendLine(NormalizeMarkdownLine(line));
            }

            FlushSection(document, currentSection, bodyBuilder);

            if (document.Sections.Count == 0)
            {
                document.Sections.Add(new ManualDocumentSectionItem
                {
                    Heading = document.Title,
                    Body = "Documento disponibile ma senza sezioni formattate."
                });
            }

            return document;
        }

        private static void FlushSection(ManualDocumentItem document, ManualDocumentSectionItem section, StringBuilder bodyBuilder)
        {
            string body = ExtractFunctionRows(section, bodyBuilder.ToString()).Trim();
            if (!string.IsNullOrWhiteSpace(body))
            {
                section.Body = body;
            }

            if (!string.IsNullOrWhiteSpace(section.Heading) ||
                !string.IsNullOrWhiteSpace(section.Body) ||
                !string.IsNullOrWhiteSpace(section.ImagePath))
            {
                if (string.IsNullOrWhiteSpace(section.Heading))
                {
                    section.Heading = document.Title;
                }

                document.Sections.Add(section);
            }

            bodyBuilder.Clear();
        }

        private static string ExtractFunctionRows(ManualDocumentSectionItem section, string rawBody)
        {
            var plainTextBuilder = new StringBuilder();

            foreach (string rawLine in rawBody.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string trimmed = rawLine.Trim();
                if (TryParseTableCells(trimmed, out var cells))
                {
                    if (IsMarkdownSeparatorRow(cells) || IsMarkdownHeaderRow(cells))
                    {
                        continue;
                    }

                    bool hasIdColumn = cells.Count >= 5 || IsIdHeaderCandidate(GetCell(cells, 0));
                    section.FunctionRows.Add(new ManualFunctionRowItem
                    {
                        Id = hasIdColumn ? GetCell(cells, 0) : string.Empty,
                        Function = GetCell(cells, hasIdColumn ? 1 : 0),
                        Meaning = GetCell(cells, hasIdColumn ? 2 : 1),
                        OperatorAction = GetCell(cells, hasIdColumn ? 3 : 2),
                        SupportCondition = GetCell(cells, hasIdColumn ? 4 : 3)
                    });
                    continue;
                }

                plainTextBuilder.AppendLine(rawLine);
            }

            return plainTextBuilder.ToString();
        }

        private static bool TryParseTableCells(string line, out List<string> cells)
        {
            cells = null;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("|") || !line.Contains("|"))
            {
                return false;
            }

            cells = line.Trim('|')
                .Split('|')
                .Select(cell => cell.Trim())
                .ToList();

            return cells.Count >= 2;
        }

        private static bool IsMarkdownHeaderRow(List<string> cells)
        {
            string first = GetCell(cells, 0).ToLowerInvariant();
            return first == "id" || first == "n" || first == "numero" ||
                first == "funzione" || first == "function" || first == "area" || first == "campo" || first == "field";
        }

        private static bool IsIdHeaderCandidate(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "id" || normalized == "n" || normalized == "numero";
        }

        private static bool IsMarkdownSeparatorRow(List<string> cells)
        {
            return cells.All(cell => cell.All(ch => ch == '-' || ch == ':' || char.IsWhiteSpace(ch)));
        }

        private static string GetCell(List<string> cells, int index)
        {
            return cells != null && index >= 0 && index < cells.Count ? cells[index] : string.Empty;
        }

        private static string NormalizeMarkdownLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("- "))
            {
                return "* " + trimmed.Substring(2);
            }

            return line;
        }

        private static bool TryParseImageLine(string line, out string altText, out string imagePath)
        {
            altText = null;
            imagePath = null;

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("![") || !line.Contains("](") || !line.EndsWith(")"))
            {
                return false;
            }

            int middleIndex = line.IndexOf("](");
            if (middleIndex <= 2)
            {
                return false;
            }

            altText = line.Substring(2, middleIndex - 2).Trim();
            imagePath = line.Substring(middleIndex + 2, line.Length - middleIndex - 3).Trim();
            return !string.IsNullOrWhiteSpace(imagePath);
        }

        private static string ResolveImagePath(string documentPath, string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            if (Path.IsPathRooted(imagePath))
            {
                return imagePath;
            }

            string baseFolder = Path.GetDirectoryName(documentPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(baseFolder, imagePath));
        }

        private static ManualDocumentItem CreateFallbackDocument()
        {
            var document = new ManualDocumentItem
            {
                Title = GetStaticMessage("Sub_entry_ManualFallbackTitle", "Quick guide"),
                Description = GetStaticMessage("Sub_entry_ManualFallbackDescription", "Operator overview")
            };

            document.Sections.Add(new ManualDocumentSectionItem
            {
                Heading = GetStaticMessage("Sub_entry_ManualFallbackSection", "Quick use"),
                Body = GetStaticMessage("Sub_entry_ManualFallbackBody",
@"1. Check machine status and active recipe.
2. Verify VisionPro is RUNNING.
3. Check camera images and inspection status.
4. In case of reject, read Defects Counters and Alarms.
5. Use Recipe Management only with adequate permissions.
6. Open Assistance view when support is needed.")
            });

            return document;
        }

        private string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        private static string GetStaticMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
