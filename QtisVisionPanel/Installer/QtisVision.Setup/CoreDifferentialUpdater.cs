using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QtisVision.Setup;

internal sealed class CoreDifferentialUpdater
{
    private const string ApplicationManifestFileName = "application-files.json";
    private const string PendingJournalFileName = "pending-core-update.json";
    private const string InstallStateFileName = "install-state.json";

    private static readonly HashSet<string> ProtectedTopLevelPaths = new(
        [
            "cfg",
            "OPC",
            "QtisVisionPanel.exe.WebView2",
            "VisionProDependencies"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] LegacyForbiddenRootFiles =
    [
        "Microsoft.ML.OnnxRuntime.dll",
        "onnxruntime.dll",
        "onnxruntime_providers_shared.dll",
        "Cognex.Vision.Startup.Net.dll"
    ];

    private readonly Action<string> _log;
    private readonly Action<int, string> _progress;

    public CoreDifferentialUpdater(Action<string> log, Action<int, string> progress)
    {
        _log = log;
        _progress = progress;
    }

    public static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Pulsar",
        "QtisVision");

    public static string ApplicationManifestPath =>
        Path.Combine(StateDirectory, ApplicationManifestFileName);

    public static string PendingJournalPath =>
        Path.Combine(StateDirectory, PendingJournalFileName);

    public static string InstallStatePath =>
        Path.Combine(StateDirectory, InstallStateFileName);

    public bool HasPendingTransaction => File.Exists(PendingJournalPath);

    public async Task<ManagedApplicationManifest> BuildTargetManifestAsync(
        string stagedBin,
        string productVersion,
        int mediaRevision,
        CancellationToken cancellationToken)
    {
        string sourceRoot = Path.GetFullPath(stagedBin);
        string[] files = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsProtectedRelativePath(Path.GetRelativePath(sourceRoot, path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<ManagedApplicationFile> entries = new(files.Length);
        for (int index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = files[index];
            string relative = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, path));
            FileInfo info = new(path);
            entries.Add(new ManagedApplicationFile
            {
                RelativePath = relative,
                SizeBytes = info.Length,
                Sha256 = await ComputeSha256Async(path, cancellationToken)
            });

            if ((index + 1) % 50 == 0 || index + 1 == files.Length)
            {
                _progress(
                    files.Length == 0 ? 100 : (index + 1) * 100 / files.Length,
                    $"Inventario applicazione {index + 1}/{files.Length}");
            }
        }

        return new ManagedApplicationManifest
        {
            ProductVersion = productVersion,
            MediaRevision = mediaRevision,
            GeneratedAtUtc = DateTime.UtcNow,
            Files = entries
        };
    }

    public ManagedApplicationManifest? LoadTrustedPreviousManifest(string? installedVersion)
    {
        if (!File.Exists(ApplicationManifestPath))
        {
            _log("CORE_FILE_MANIFEST_NOT_FOUND|legacy differential mode; obsolete unknown files will be preserved");
            return null;
        }

        try
        {
            ManagedApplicationManifest? manifest = JsonSerializer.Deserialize<ManagedApplicationManifest>(
                File.ReadAllText(ApplicationManifestPath, Encoding.UTF8),
                InstallerEngine.JsonOptions);
            if (manifest is null ||
                manifest.SchemaVersion != 1 ||
                manifest.Files is null ||
                string.IsNullOrWhiteSpace(installedVersion) ||
                !VersionsEqual(manifest.ProductVersion, installedVersion) ||
                !ValidateManifestEntries(manifest.Files))
            {
                _log(
                    $"CORE_FILE_MANIFEST_UNTRUSTED|path={ApplicationManifestPath}|" +
                    $"manifestVersion={manifest?.ProductVersion ?? "missing"}|installedVersion={installedVersion ?? "unknown"}");
                return null;
            }

            return manifest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log($"CORE_FILE_MANIFEST_READ_FAILED|path={ApplicationManifestPath}|error={ex.Message}");
            return null;
        }
    }

    public async Task<CoreDifferentialPlan> BuildPlanAsync(
        string stagedBin,
        string destinationBin,
        ManagedApplicationManifest targetManifest,
        ManagedApplicationManifest? previousManifest,
        CancellationToken cancellationToken)
    {
        string sourceRoot = Path.GetFullPath(stagedBin);
        string destinationRoot = Path.GetFullPath(destinationBin);
        Dictionary<string, ManagedApplicationFile> targetFiles = targetManifest.Files.ToDictionary(
            entry => NormalizeRelativePath(entry.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        List<string> added = [];
        List<string> updated = [];
        List<string> removed = [];
        int unchanged = 0;
        int inspected = 0;

        foreach (ManagedApplicationFile target in targetFiles.Values.OrderBy(
                     entry => entry.RelativePath,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = NormalizeRelativePath(target.RelativePath);
            string source = ResolveUnderRoot(sourceRoot, relative);
            string destination = ResolveUnderRoot(destinationRoot, relative);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"File dichiarato nell'inventario applicativo non trovato: {relative}.",
                    source);
            }

            if (!File.Exists(destination))
            {
                added.Add(relative);
            }
            else
            {
                FileInfo destinationInfo = new(destination);
                bool matches = destinationInfo.Length == target.SizeBytes;
                if (matches)
                {
                    string installedHash = await ComputeSha256Async(destination, cancellationToken);
                    matches = installedHash.Equals(target.Sha256, StringComparison.OrdinalIgnoreCase);
                }

                if (matches)
                {
                    unchanged++;
                }
                else
                {
                    updated.Add(relative);
                }
            }

            inspected++;
            if (inspected % 50 == 0 || inspected == targetFiles.Count)
            {
                _progress(
                    targetFiles.Count == 0 ? 100 : inspected * 100 / targetFiles.Count,
                    $"Confronto installazione {inspected}/{targetFiles.Count}");
            }
        }

        if (previousManifest is not null)
        {
            foreach (ManagedApplicationFile previous in previousManifest.Files)
            {
                string relative = NormalizeRelativePath(previous.RelativePath);
                if (IsProtectedRelativePath(relative) || targetFiles.ContainsKey(relative))
                {
                    continue;
                }

                string destination = ResolveUnderRoot(destinationRoot, relative);
                if (File.Exists(destination))
                {
                    removed.Add(relative);
                }
            }
        }

        foreach (string forbiddenRootFile in LegacyForbiddenRootFiles)
        {
            string relative = NormalizeRelativePath(forbiddenRootFile);
            if (targetFiles.ContainsKey(relative))
            {
                continue;
            }

            string destination = ResolveUnderRoot(destinationRoot, relative);
            if (File.Exists(destination) && !removed.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                removed.Add(relative);
            }
        }

        return new CoreDifferentialPlan
        {
            TargetManifest = targetManifest,
            PreviousManifestTrusted = previousManifest is not null,
            AddedFiles = added.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            UpdatedFiles = updated.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedFiles = removed.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            UnchangedFiles = unchanged
        };
    }

    public async Task<CoreUpdateJournal> PrepareTransactionAsync(
        string installRoot,
        string destinationBin,
        string backupRoot,
        CoreInstallationInfo installationInfo,
        CoreDifferentialPlan plan,
        CancellationToken cancellationToken)
    {
        if (HasPendingTransaction)
        {
            throw new InvalidOperationException(
                $"Esiste gia una transazione di aggiornamento pendente: {PendingJournalPath}.");
        }

        string destinationRoot = Path.GetFullPath(destinationBin);
        string backupApplicationRoot = Path.Combine(backupRoot, "ApplicationChanged");
        foreach (string relative in plan.UpdatedFiles.Concat(plan.RemovedFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = ResolveUnderRoot(destinationRoot, relative);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"File da aggiornare o rimuovere non trovato durante il backup: {relative}.",
                    source);
            }

            string backup = ResolveUnderRoot(backupApplicationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            await CopyFileAsync(source, backup, cancellationToken, atomicDestination: false);
        }

        Directory.CreateDirectory(StateDirectory);
        string stateBackupRoot = Path.Combine(backupRoot, "InstallerState");
        Directory.CreateDirectory(stateBackupRoot);
        bool hadInstallState = File.Exists(InstallStatePath);
        bool hadApplicationManifest = File.Exists(ApplicationManifestPath);
        if (hadInstallState)
        {
            File.Copy(
                InstallStatePath,
                Path.Combine(stateBackupRoot, InstallStateFileName),
                overwrite: true);
        }
        if (hadApplicationManifest)
        {
            File.Copy(
                ApplicationManifestPath,
                Path.Combine(stateBackupRoot, ApplicationManifestFileName),
                overwrite: true);
        }

        CoreUpdateJournal journal = new()
        {
            CreatedAtUtc = DateTime.UtcNow,
            InstallRoot = Path.GetFullPath(installRoot),
            DestinationBin = destinationRoot,
            BackupRoot = Path.GetFullPath(backupRoot),
            TargetVersion = installationInfo.TargetVersion,
            InstalledVersion = installationInfo.InstalledVersion,
            HadInstallState = hadInstallState,
            HadApplicationManifest = hadApplicationManifest,
            AddedFiles = [.. plan.AddedFiles],
            UpdatedFiles = [.. plan.UpdatedFiles],
            RemovedFiles = [.. plan.RemovedFiles]
        };
        WriteJsonAtomically(PendingJournalPath, journal);
        _log(
            $"CORE_DIFFERENTIAL_PREPARED|installed={installationInfo.InstalledVersion ?? "none"}|" +
            $"target={installationInfo.TargetVersion}|added={plan.AddedFiles.Count}|" +
            $"updated={plan.UpdatedFiles.Count}|removed={plan.RemovedFiles.Count}|" +
            $"unchanged={plan.UnchangedFiles}|previousManifestTrusted={plan.PreviousManifestTrusted}|" +
            $"backup={backupRoot}");
        return journal;
    }

    public async Task ApplyAsync(
        string stagedBin,
        string destinationBin,
        CoreDifferentialPlan plan,
        CancellationToken cancellationToken)
    {
        string sourceRoot = Path.GetFullPath(stagedBin);
        string destinationRoot = Path.GetFullPath(destinationBin);
        Dictionary<string, ManagedApplicationFile> targetFiles = plan.TargetManifest.Files.ToDictionary(
            entry => NormalizeRelativePath(entry.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        string[] writes = plan.UpdatedFiles
            .Concat(plan.AddedFiles)
            .OrderBy(relative => relative.Equals("QtisVisionPanel.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (int index = 0; index < writes.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = writes[index];
            string source = ResolveUnderRoot(sourceRoot, relative);
            string destination = ResolveUnderRoot(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(source, destination, cancellationToken, atomicDestination: true);

            string installedHash = await ComputeSha256Async(destination, cancellationToken);
            if (!installedHash.Equals(targetFiles[relative].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Verifica SHA-256 fallita dopo l'aggiornamento del file {relative}.");
            }

            _progress(
                writes.Length == 0 ? 100 : (index + 1) * 100 / writes.Length,
                $"Aggiornamento file {index + 1}/{writes.Length}");
        }

        foreach (string relative in plan.RemovedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = ResolveUnderRoot(destinationRoot, relative);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        _log(
            $"CORE_DIFFERENTIAL_APPLIED|added={plan.AddedFiles.Count}|updated={plan.UpdatedFiles.Count}|" +
            $"removed={plan.RemovedFiles.Count}|unchanged={plan.UnchangedFiles}");
    }

    public async Task RecoverPendingTransactionAsync(
        string expectedInstallRoot,
        CancellationToken cancellationToken)
    {
        if (!HasPendingTransaction)
        {
            return;
        }

        CoreUpdateJournal journal = ReadJournal();
        if (journal.Status.Equals("Committed", StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(PendingJournalPath);
            _log("CORE_DIFFERENTIAL_COMMITTED_JOURNAL_CLEANED");
            return;
        }

        if (!Path.GetFullPath(journal.InstallRoot).Equals(
                Path.GetFullPath(expectedInstallRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"La transazione pendente appartiene a una root diversa: {journal.InstallRoot}.");
        }

        string expectedRoot = Path.GetFullPath(expectedInstallRoot);
        string expectedBin = Path.Combine(expectedRoot, "bin");
        string expectedBackups = Path.Combine(expectedRoot, "InstallerBackups");
        if (!Path.GetFullPath(journal.DestinationBin).Equals(
                Path.GetFullPath(expectedBin),
                StringComparison.OrdinalIgnoreCase) ||
            !IsPathUnderRoot(journal.BackupRoot, expectedBackups))
        {
            throw new InvalidDataException(
                "La transazione pendente contiene percorsi non compatibili con la root installer attesa.");
        }

        _log(
            $"CORE_DIFFERENTIAL_RECOVERY_START|installed={journal.InstalledVersion ?? "unknown"}|" +
            $"target={journal.TargetVersion}|backup={journal.BackupRoot}");
        await RollbackAsync(journal, cancellationToken);
        _log("CORE_DIFFERENTIAL_RECOVERY_COMPLETE");
    }

    public async Task RollbackAsync(CoreUpdateJournal journal, CancellationToken cancellationToken)
    {
        List<Exception> errors = [];
        string destinationRoot = Path.GetFullPath(journal.DestinationBin);
        string backupApplicationRoot = Path.Combine(journal.BackupRoot, "ApplicationChanged");

        foreach (string relative in journal.AddedFiles.AsEnumerable().Reverse())
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = ResolveUnderRoot(destinationRoot, relative);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        foreach (string relative in journal.UpdatedFiles.Concat(journal.RemovedFiles).Reverse())
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string backup = ResolveUnderRoot(backupApplicationRoot, relative);
                string destination = ResolveUnderRoot(destinationRoot, relative);
                if (!File.Exists(backup))
                {
                    throw new FileNotFoundException(
                        $"Backup mancante durante il rollback: {relative}.",
                        backup);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(backup, destination, cancellationToken, atomicDestination: true);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        RestoreInstallerStateFile(
            InstallStatePath,
            Path.Combine(journal.BackupRoot, "InstallerState", InstallStateFileName),
            journal.HadInstallState,
            errors);
        RestoreInstallerStateFile(
            ApplicationManifestPath,
            Path.Combine(journal.BackupRoot, "InstallerState", ApplicationManifestFileName),
            journal.HadApplicationManifest,
            errors);

        if (errors.Count > 0)
        {
            _log($"CORE_DIFFERENTIAL_ROLLBACK_FAILED|errors={errors.Count}");
            throw new AggregateException(
                "Rollback differenziale non completato. Consultare il log installer.",
                errors);
        }

        if (File.Exists(PendingJournalPath))
        {
            File.Delete(PendingJournalPath);
        }
        _log("CORE_DIFFERENTIAL_ROLLBACK_COMPLETE");
    }

    public void WriteTargetManifest(ManagedApplicationManifest manifest)
    {
        Directory.CreateDirectory(StateDirectory);
        WriteJsonAtomically(ApplicationManifestPath, manifest);
        _log(
            $"CORE_FILE_MANIFEST_WRITTEN|path={ApplicationManifestPath}|" +
            $"version={manifest.ProductVersion}|files={manifest.Files.Count}");
    }

    public void CommitTransaction(CoreUpdateJournal journal)
    {
        journal.Status = "Committed";
        WriteJsonAtomically(PendingJournalPath, journal);
        File.Delete(PendingJournalPath);
        _log("CORE_DIFFERENTIAL_COMMIT_COMPLETE");
    }

    public static void WriteJsonAtomically<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(value, InstallerEngine.JsonOptions),
                new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private CoreUpdateJournal ReadJournal()
    {
        try
        {
            CoreUpdateJournal? journal = JsonSerializer.Deserialize<CoreUpdateJournal>(
                File.ReadAllText(PendingJournalPath, Encoding.UTF8),
                InstallerEngine.JsonOptions);
            if (journal is null ||
                journal.SchemaVersion != 1 ||
                string.IsNullOrWhiteSpace(journal.InstallRoot) ||
                string.IsNullOrWhiteSpace(journal.DestinationBin) ||
                string.IsNullOrWhiteSpace(journal.BackupRoot) ||
                journal.AddedFiles is null ||
                journal.UpdatedFiles is null ||
                journal.RemovedFiles is null ||
                !ValidateJournalEntries(journal))
            {
                throw new InvalidDataException("Journal aggiornamento vuoto o con schema non supportato.");
            }

            return journal;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Journal aggiornamento non leggibile: {PendingJournalPath}.",
                ex);
        }
    }

    private static void RestoreInstallerStateFile(
        string destination,
        string backup,
        bool existedBefore,
        List<Exception> errors)
    {
        try
        {
            if (existedBefore)
            {
                if (!File.Exists(backup))
                {
                    throw new FileNotFoundException(
                        $"Backup stato installer mancante: {backup}.",
                        backup);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backup, destination, overwrite: true);
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static bool ValidateManifestEntries(IEnumerable<ManagedApplicationFile> entries)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ManagedApplicationFile entry in entries)
        {
            try
            {
                string relative = NormalizeRelativePath(entry.RelativePath);
                if (IsProtectedRelativePath(relative) ||
                    entry.SizeBytes < 0 ||
                    entry.Sha256.Length != 64 ||
                    !paths.Add(relative))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateJournalEntries(CoreUpdateJournal journal)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in journal.AddedFiles
                     .Concat(journal.UpdatedFiles)
                     .Concat(journal.RemovedFiles))
        {
            try
            {
                string relative = NormalizeRelativePath(relativePath);
                if (IsProtectedRelativePath(relative) || !paths.Add(relative))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static bool VersionsEqual(string left, string right)
    {
        return Version.TryParse(left, out Version? leftVersion) &&
               Version.TryParse(right, out Version? rightVersion) &&
               leftVersion == rightVersion;
    }

    private static bool IsProtectedRelativePath(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        string topLevel = normalized.Split(Path.DirectorySeparatorChar, 2)[0];
        return ProtectedTopLevelPaths.Contains(topLevel);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Percorso relativo applicativo non valido: '{relativePath}'.");
        }

        string normalized = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        string[] segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Percorso relativo applicativo non valido: '{relativePath}'.");
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string normalizedRelative = NormalizeRelativePath(relativePath);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Percorso fuori dalla root consentita: {relativePath}.");
        }

        return fullPath;
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return fullPath.StartsWith(
            fullRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken,
        bool atomicDestination)
    {
        string outputPath = atomicDestination
            ? destination + $".{Guid.NewGuid():N}.tmp"
            : destination;
        try
        {
            await using (FileStream input = new(
                             source,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             1024 * 1024,
                             useAsync: true))
            await using (FileStream output = new(
                             outputPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             useAsync: true))
            {
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (!atomicDestination)
            {
                return;
            }

            if (File.Exists(destination))
            {
                File.Replace(outputPath, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(outputPath, destination);
            }
        }
        finally
        {
            if (atomicDestination && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}
