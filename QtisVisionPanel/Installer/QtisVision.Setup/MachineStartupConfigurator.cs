using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace QtisVision.Setup;

internal sealed class MachineStartupConfigurator
{
    private const string ScriptFileName = "Start-QtisVisionPanel.ps1";
    private const string ShortcutFileName = "Qtis Vision Panel (Delayed).lnk";
    private readonly Action<string> _log;

    public MachineStartupConfigurator(Action<string> log)
    {
        _log = log;
    }

    public void Configure(string installRoot)
    {
        string root = Path.GetFullPath(installRoot);
        string executable = Path.Combine(root, "bin", "QtisVisionPanel.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                $"HMI non trovata durante la configurazione dell'avvio automatico: {executable}.",
                executable);
        }

        string startupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pulsar",
            "QtisVision",
            "Startup");
        Directory.CreateDirectory(startupRoot);

        string scriptPath = Path.Combine(startupRoot, ScriptFileName);
        string settingsPath = Path.Combine(startupRoot, "startup-settings.json");
        string statePath = Path.Combine(startupRoot, "startup-install-state.json");
        ExtractScript(scriptPath);
        EnsureSettings(settingsPath);

        string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        if (string.IsNullOrWhiteSpace(commonStartup))
        {
            throw new DirectoryNotFoundException(
                "Cartella Startup comune di Windows non risolta.");
        }
        Directory.CreateDirectory(commonStartup);

        string shortcutPath = Path.Combine(commonStartup, ShortcutFileName);
        RemoveLegacyImmediateStartupLinks(commonStartup, shortcutPath);
        CreateStartupShortcut(shortcutPath, scriptPath, settingsPath, root);

        CoreDifferentialUpdater.WriteJsonAtomically(
            statePath,
            new StartupInstallState
            {
                ConfiguredAtUtc = DateTime.UtcNow,
                InstallRoot = root,
                ExecutablePath = executable,
                ScriptPath = scriptPath,
                SettingsPath = settingsPath,
                ShortcutPath = shortcutPath
            });

        _log(
            $"MACHINE_AUTOSTART_CONFIGURED|shortcut={shortcutPath}|script={scriptPath}|" +
            $"settings={settingsPath}|installRoot={root}");
    }

    private static void EnsureSettings(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    File.ReadAllText(path, Encoding.UTF8));
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("schemaVersion", out JsonElement schema) &&
                    schema.TryGetInt32(out int schemaVersion) &&
                    schemaVersion == 1)
                {
                    return;
                }
            }
            catch (JsonException)
            {
                // Preserve the invalid operator-edited file before restoring safe defaults.
            }

            string invalidBackup = path + $".invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            File.Move(path, invalidBackup, overwrite: false);
        }

        CoreDifferentialUpdater.WriteJsonAtomically(
            path,
            new StartupSettings());
    }

    private static void ExtractScript(string destinationPath)
    {
        Assembly assembly = typeof(MachineStartupConfigurator).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith($".Scripts.{ScriptFileName}", StringComparison.OrdinalIgnoreCase));
        using Stream source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Risorsa installer non trovata: {resourceName}.");
        using MemoryStream buffer = new();
        source.CopyTo(buffer);
        byte[] scriptBytes = buffer.ToArray();

        if (File.Exists(destinationPath) &&
            File.ReadAllBytes(destinationPath).AsSpan().SequenceEqual(scriptBytes))
        {
            return;
        }

        string temporary = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, scriptBytes);
            File.Move(temporary, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void CreateStartupShortcut(
        string shortcutPath,
        string scriptPath,
        string settingsPath,
        string installRoot)
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powerShell))
        {
            throw new FileNotFoundException(
                "Windows PowerShell 5.1 non trovato per l'avvio automatico Qtis.",
                powerShell);
        }

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host non disponibile.");
        object shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Impossibile creare WScript.Shell.");
        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            Type shortcutType = shortcut?.GetType()
                ?? throw new InvalidOperationException("Impossibile creare il collegamento Startup.");
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [powerShell]);
            shortcutType.InvokeMember(
                "Arguments",
                BindingFlags.SetProperty,
                null,
                shortcut,
                [$"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" -InstallRoot \"{installRoot}\" -SettingsPath \"{settingsPath}\""]);
            shortcutType.InvokeMember(
                "WorkingDirectory",
                BindingFlags.SetProperty,
                null,
                shortcut,
                [Path.Combine(installRoot, "bin")]);
            shortcutType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, [7]);
            shortcutType.InvokeMember(
                "Description",
                BindingFlags.SetProperty,
                null,
                shortcut,
                ["Avvio ritardato e controllato Qtis Vision Panel"]);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
            if (Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private void RemoveLegacyImmediateStartupLinks(string commonStartup, string currentShortcut)
    {
        foreach (string legacyName in new[]
                 {
                     "Qtis Vision Panel.lnk",
                     "QtisVisionPanel.lnk",
                     "Qtis Vision Panel.cmd",
                     "QtisVisionPanel.cmd"
                 })
        {
            string candidate = Path.Combine(commonStartup, legacyName);
            if (candidate.Equals(currentShortcut, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(candidate))
            {
                continue;
            }

            File.Delete(candidate);
            _log($"MACHINE_AUTOSTART_LEGACY_ENTRY_REMOVED|path={candidate}");
        }
    }

    private sealed class StartupSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public int InitialDelaySeconds { get; set; } = 45;
        public int DependencyWaitSeconds { get; set; } = 120;
        public int PollIntervalSeconds { get; set; } = 2;
        public bool LaunchWhenDependenciesTimeout { get; set; } = true;
        public string[] ServiceNamePatterns { get; set; } = ["MySQL*"];
    }

    private sealed class StartupInstallState
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTime ConfiguredAtUtc { get; set; }
        public string InstallRoot { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string ScriptPath { get; set; } = string.Empty;
        public string SettingsPath { get; set; } = string.Empty;
        public string ShortcutPath { get; set; } = string.Empty;
    }
}
