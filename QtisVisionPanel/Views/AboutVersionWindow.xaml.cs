using Cognex.VisionPro;
using FontAwesome.WPF;
using GalaSoft.MvvmLight;
using Microsoft.Web.WebView2.Wpf;
using NLog;
using QtisVisionPanel.ServerMessage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;

namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Read-only service window that exposes the official software version and the
    /// versions of the main runtime modules used by the industrial HMI.
    /// </summary>
    public partial class AboutVersionWindow : Window
    {
        private const string VersionArchiveRelativePath = @"Documentation\MachineHardware\software-version-archive.md";

        public AboutVersionWindow()
        {
            InitializeComponent();

            Modules = new ObservableCollection<ModuleVersionInfo>(BuildModuleList());

            var executingAssembly = Assembly.GetExecutingAssembly();
            var productName = GetAssemblyAttribute<AssemblyProductAttribute>(executingAssembly)?.Product ?? "QtisVisionPanel";
            var companyName = GetAssemblyAttribute<AssemblyCompanyAttribute>(executingAssembly)?.Company ?? "Pulsar Engineering";
            var version = executingAssembly.GetName().Version?.ToString() ?? "1.1.0.0";
            var versionArchivePath = ResolveVersionArchivePath();

            ProductName = productName;
            CompanyName = companyName;
            SoftwareVersion = version;
            AssemblyVersion = FileVersionInfo.GetVersionInfo(executingAssembly.Location).FileVersion ?? version;
            DisplayVersionBadge = $"Baseline {version}";
            RuntimeFramework = $".NET {Environment.Version}";
            ConfigFilePath = @"C:\QtisVision\cfg\Config.xml";
            DatabaseTarget = BuildDatabaseTarget();
            RecipeFolder = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder ?? "(not configured)";
            ImageRoot = MainWindow.configManager?.Config?.Configuration?.ImageDir ?? "(not configured)";
            ActiveRecipe = MainWindow.configManager?.Config?.Configuration?.LastRecipe ?? "(not configured)";
            MachineTypeDisplay = MainWindow.configManager?.Config?.Configuration?.MachineType
                ?? MainWindow.configManager?.Config?.Configuration?.Machine_type
                ?? "(not configured)";
            VersionArchivePath = versionArchivePath;
            ReleaseDateDisplay = ResolveReleaseDate(version, versionArchivePath);
            LastUpdateDisplay = ResolveLastUpdate(executingAssembly);

            DataContext = this;
            LoadServerMessages();
        }

        public string ProductName { get; }
        public string CompanyName { get; }
        public string SoftwareVersion { get; }
        public string AssemblyVersion { get; }
        public string DisplayVersionBadge { get; }
        public string ReleaseDateDisplay { get; }
        public string LastUpdateDisplay { get; }
        public string RuntimeFramework { get; }
        public string ConfigFilePath { get; }
        public string DatabaseTarget { get; }
        public string RecipeFolder { get; }
        public string ImageRoot { get; }
        public string ActiveRecipe { get; }
        public string MachineTypeDisplay { get; }
        public string VersionArchivePath { get; }
        public ObservableCollection<ModuleVersionInfo> Modules { get; }

        private void LoadServerMessages()
        {
            Title = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutWindowTitle", "Software Version");
            Sub_entry_AboutWindowTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutWindowTitle", "Software Version");
            Sub_entry_AboutWindowSubtitle.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_AboutWindowSubtitle",
                "Unified industrial baseline, runtime modules and current configuration contract");
            Sub_entry_AboutCompanyLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutCompanyLabel", "Company");
            Sub_entry_AboutMachineLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutMachineLabel", "Machine");
            Sub_entry_AboutSoftwareSectionTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutSoftwareSectionTitle", "Software Identity");
            Sub_entry_AboutSoftwareSectionHint.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_AboutSoftwareSectionHint",
                "Official versioning and product metadata exposed by the current build.");
            Sub_entry_AboutReleaseDateLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutReleaseDateLabel", "Release date");
            Sub_entry_AboutLastUpdateLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutLastUpdateLabel", "Last update");
            Sub_entry_AboutRuntimeSectionTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutRuntimeSectionTitle", "Runtime Contract");
            Sub_entry_AboutRuntimeSectionHint.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_AboutRuntimeSectionHint",
                "Config-driven runtime paths and live machine targets currently in use.");
            Sub_entry_AboutModulesSectionTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutModulesSectionTitle", "Integrated Modules");
            Sub_entry_AboutModulesSectionHint.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_AboutModulesSectionHint",
                "Versions resolved from the assemblies currently loaded by the application.");
            Sub_entry_AboutNotesSectionTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutNotesSectionTitle", "Version Archive");
            Sub_entry_AboutNotesSectionHint.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_AboutNotesSectionHint",
                "The project now tracks official versions, functional changes, configuration additions and database notes in a dedicated archive.");
            Sub_entry_AboutArchivePathLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AboutArchivePathLabel", "Archive file");
            Sub_entry_OpenVersionArchiveButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OpenVersionArchiveButton", "Open version archive");
            Sub_entry_CloseAboutButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CloseAboutButton", "Close");
        }

        private static string BuildDatabaseTarget()
        {
            var settings = MainWindow.configManager?.Config?.MySqlConnection;
            if (settings == null)
            {
                return "(not configured)";
            }

            return $"{settings.Host}:{settings.port} / {settings.Db}";
        }

        private static string ResolveVersionArchivePath()
        {
            var deployedPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, VersionArchiveRelativePath));
            if (File.Exists(deployedPath))
            {
                return deployedPath;
            }

            var developerPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..", VersionArchiveRelativePath));
            return File.Exists(developerPath) ? developerPath : deployedPath;
        }

        private static string ResolveReleaseDate(string version, string archivePath)
        {
            try
            {
                if (!File.Exists(archivePath))
                {
                    return "not available";
                }

                var content = File.ReadAllText(archivePath);
                var sectionMatch = Regex.Match(
                    content,
                    $@"## Version {Regex.Escape(version)}(?<body>.*?)(?:\r?\n## |\z)",
                    RegexOptions.Singleline);

                if (sectionMatch.Success)
                {
                    var dateMatch = Regex.Match(sectionMatch.Groups["body"].Value, @"- release date:\s*`(?<date>[^`]+)`");
                    if (dateMatch.Success)
                    {
                        return dateMatch.Groups["date"].Value;
                    }
                }

                var currentVersionMatch = Regex.Match(
                    content,
                    @"## Current Official Version\s*\r?\n\r?\n- `(?<version>\d+\.\d+\.\d+\.\d+)`\r?\n- release baseline date: `(?<date>[^`]+)`",
                    RegexOptions.Multiline);

                if (currentVersionMatch.Success && string.Equals(currentVersionMatch.Groups["version"].Value, version, StringComparison.OrdinalIgnoreCase))
                {
                    return currentVersionMatch.Groups["date"].Value;
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Unable to resolve software release date from archive: {ex.Message}");
            }

            return "not available";
        }

        private static string ResolveLastUpdate(Assembly assembly)
        {
            try
            {
                if (assembly == null || string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location))
                {
                    return "not available";
                }

                return File.GetLastWriteTime(assembly.Location).ToString("dd/MM/yyyy HH:mm:ss");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Unable to resolve assembly last update timestamp: {ex.Message}");
                return "not available";
            }
        }

        private static IReadOnlyList<ModuleVersionInfo> BuildModuleList()
        {
            return new List<ModuleVersionInfo>
            {
                CreateModule("VisionPro Core", "Main machine vision runtime and serialization layer.", typeof(CogSerializer).Assembly),
                CreateModule("VisionPro Controls", "Display and operator-facing controls used by camera views.", typeof(CogRecordDisplay).Assembly),
                CreateModule("VisionPro QuickBuild", "Job manager runtime used to execute production jobs.", typeof(Cognex.VisionPro.QuickBuild.CogJobManager).Assembly),
                CreateModule("WebView2", "Embedded browser runtime used by dashboard and legacy web surfaces.", typeof(WebView2).Assembly),
                CreateModule("MySQL Driver", "Persistence layer for recipes, events and recovery snapshots.", typeof(MySql.Data.MySqlClient.MySqlConnection).Assembly),
                CreateModule("NLog", "Structured machine-side application logging bridge.", typeof(LogManager).Assembly),
                CreateModule("MvvmLight", "MVVM infrastructure used by the WPF application.", typeof(ViewModelBase).Assembly),
                CreateModule("FontAwesome.WPF", "Icon pack used across HMI navigation and top bar.", typeof(ImageAwesome).Assembly)
            };
        }

        private static ModuleVersionInfo CreateModule(string name, string description, Assembly assembly)
        {
            var version = assembly?.GetName().Version?.ToString();
            if (string.IsNullOrWhiteSpace(version) && assembly != null)
            {
                version = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
            }

            return new ModuleVersionInfo
            {
                Name = name,
                Description = description,
                Version = string.IsNullOrWhiteSpace(version) ? "not available" : version
            };
        }

        private static TAttribute GetAssemblyAttribute<TAttribute>(Assembly assembly) where TAttribute : Attribute
        {
            var attributes = assembly.GetCustomAttributes(typeof(TAttribute), false);
            return attributes.Length > 0 ? (TAttribute)attributes[0] : null;
        }

        private void OpenVersionArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(VersionArchivePath))
                {
                    new SystemNotificationWindow("Version Archive",
                        $"Archive file not found:{Environment.NewLine}{VersionArchivePath}",
                        NotificationSeverity.Info).ShowDialog();
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = VersionArchivePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Unable to open version archive: {ex.Message}");
                new SystemNotificationWindow("Version Archive",
                    $"Unable to open version archive:{Environment.NewLine}{ex.Message}",
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private void CloseAboutButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public sealed class ModuleVersionInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Version { get; set; }
        }
    }
}
