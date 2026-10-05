using System.Runtime.InteropServices;

namespace QtisVision.Setup;

internal static class MachineEnvironmentConfigurator
{
    private const uint HwndBroadcast = 0xffff;
    private const uint WmSettingChange = 0x001a;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint hWnd,
        uint message,
        nuint wParam,
        string lParam,
        uint flags,
        uint timeout,
        out nuint result);

    public static void ConfigurePython(string pythonRoot, Action<string> log)
    {
        string root = Path.GetFullPath(pythonRoot);
        string pythonExe = Path.Combine(root, "python.exe");
        if (!File.Exists(pythonExe))
        {
            throw new FileNotFoundException(
                $"Python non trovato durante la configurazione delle variabili ambiente: {pythonExe}.",
                pythonExe);
        }

        List<string> requestedPaths = [root];
        string scripts = Path.Combine(root, "Scripts");
        if (Directory.Exists(scripts))
        {
            requestedPaths.Add(scripts);
        }

        string machinePath = Environment.GetEnvironmentVariable(
                                 "Path",
                                 EnvironmentVariableTarget.Machine)
                             ?? string.Empty;
        List<string> entries = machinePath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        List<string> added = [];
        foreach (string requested in requestedPaths)
        {
            if (entries.Any(existing => PathsEqual(existing, requested)))
            {
                continue;
            }

            entries.Add(requested);
            added.Add(requested);
        }

        string updatedPath = string.Join(';', entries);
        if (updatedPath.Length > 30_000)
        {
            throw new InvalidOperationException(
                "La variabile PATH macchina e' troppo lunga per aggiungere in sicurezza Python Qtis.");
        }

        if (added.Count > 0)
        {
            Environment.SetEnvironmentVariable(
                "Path",
                updatedPath,
                EnvironmentVariableTarget.Machine);
        }

        Environment.SetEnvironmentVariable(
            "QTIS_PYTHON_ROOT",
            root,
            EnvironmentVariableTarget.Machine);
        Environment.SetEnvironmentVariable(
            "QTIS_PYTHON_EXE",
            pythonExe,
            EnvironmentVariableTarget.Machine);

        string processPath = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
        List<string> processEntries = processPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        foreach (string requested in requestedPaths)
        {
            if (!processEntries.Any(existing => PathsEqual(existing, requested)))
            {
                processEntries.Add(requested);
            }
        }
        Environment.SetEnvironmentVariable("Path", string.Join(';', processEntries));
        Environment.SetEnvironmentVariable("QTIS_PYTHON_ROOT", root);
        Environment.SetEnvironmentVariable("QTIS_PYTHON_EXE", pythonExe);

        _ = SendMessageTimeout(
            (nint)HwndBroadcast,
            WmSettingChange,
            0,
            "Environment",
            SmtoAbortIfHung,
            5_000,
            out _);

        log(
            $"PYTHON_MACHINE_ENVIRONMENT_CONFIGURED|root={root}|exe={pythonExe}|" +
            $"pathEntriesAdded={added.Count}|entries={string.Join(',', added)}");
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            string normalizedLeft = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(left.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(right.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return left.Trim().Trim('"')
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(
                    right.Trim().Trim('"')
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
