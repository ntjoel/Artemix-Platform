using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace QtisVision.Setup;

internal sealed class CognexGigENetworkConfigurator
{
    private const string ScriptFileName = "Configure-CognexGigENetwork.ps1";
    private readonly Action<string> _log;
    private readonly Action<int, string> _progress;

    public CognexGigENetworkConfigurator(Action<string> log, Action<int, string> progress)
    {
        _log = log;
        _progress = progress;
    }

    public async Task ConfigureAsync(string installRoot, CancellationToken cancellationToken)
    {
        string programDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pulsar",
            "QtisVision",
            "Installer");
        string scriptDirectory = Path.Combine(programDataRoot, "Scripts");
        string resultDirectory = Path.Combine(programDataRoot, "NetworkResults");
        string backupDirectory = Path.Combine(programDataRoot, "NetworkBackups");
        Directory.CreateDirectory(scriptDirectory);
        Directory.CreateDirectory(resultDirectory);
        Directory.CreateDirectory(backupDirectory);

        string scriptPath = Path.Combine(scriptDirectory, ScriptFileName);
        ExtractScript(scriptPath);
        string resultPath = Path.Combine(
            resultDirectory,
            $"cognex-gige-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powerShell))
        {
            throw new FileNotFoundException(
                "Windows PowerShell 5.1 non trovato; impossibile configurare le schede Cognex GigE.",
                powerShell);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = powerShell,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
                 {
                     "-NoLogo",
                     "-NoProfile",
                     "-NonInteractive",
                     "-ExecutionPolicy",
                     "Bypass",
                     "-File",
                     scriptPath,
                     "-InstallRoot",
                     Path.GetFullPath(installRoot),
                     "-ResultPath",
                     resultPath,
                     "-BackupRoot",
                     backupDirectory,
                     "-Apply"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        _progress(5, "Rilevamento schede camera Cognex GigE");
        _log($"COGNEX_GIGE_CONFIG_START|installRoot={Path.GetFullPath(installRoot)}|result={resultPath}");
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Avvio configuratore rete Cognex GigE fallito.");
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;

        LogProcessText("COGNEX_GIGE_SCRIPT_OUT", standardOutput);
        LogProcessText("COGNEX_GIGE_SCRIPT_ERR", standardError);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Configurazione rete Cognex GigE fallita con codice {process.ExitCode}. Risultato: {resultPath}");
        }
        if (!File.Exists(resultPath))
        {
            throw new InvalidDataException(
                $"Il configuratore Cognex GigE non ha prodotto il report atteso: {resultPath}.");
        }

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(resultPath, Encoding.UTF8, cancellationToken));
        JsonElement root = document.RootElement;
        string status = root.TryGetProperty("status", out JsonElement statusElement)
            ? statusElement.GetString() ?? "Unknown"
            : "Unknown";
        int configuredAdapters = root.TryGetProperty("configuredAdapters", out JsonElement adaptersElement) &&
                                 adaptersElement.ValueKind == JsonValueKind.Array
            ? adaptersElement.GetArrayLength()
            : 0;
        int warningCount = root.TryGetProperty("warnings", out JsonElement warningsElement) &&
                           warningsElement.ValueKind == JsonValueKind.Array
            ? warningsElement.GetArrayLength()
            : 0;
        string? backupPath = root.TryGetProperty("backupPath", out JsonElement backupElement) &&
                             backupElement.ValueKind == JsonValueKind.String
            ? backupElement.GetString()
            : null;

        _progress(100, "Configurazione Cognex GigE completata");
        _log(
            $"COGNEX_GIGE_CONFIG_COMPLETE|status={status}|adapters={configuredAdapters}|" +
            $"warnings={warningCount}|backup={backupPath ?? "none"}|result={resultPath}");
    }

    private static void ExtractScript(string destinationPath)
    {
        Assembly assembly = typeof(CognexGigENetworkConfigurator).Assembly;
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

    private void LogProcessText(string eventName, string text)
    {
        foreach (string line in text.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _log($"{eventName}|{line}");
        }
    }
}
