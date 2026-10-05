using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QtisVision.Setup;

internal sealed class InstallerEngine : IDisposable
{
    private const string CoreComponentId = "qtis-core";
    private const string UltraVncServiceName = "uvnc_service";
    private const string UltraVncIniFileName = "ultravnc.ini";
    private readonly string _manifestPath;
    private readonly string _mediaRoot;
    private readonly StreamWriter _logWriter;
    private readonly CoreDifferentialUpdater _coreUpdater;
    private bool _disposed;

    public InstallerEngine(string manifestPath)
    {
        _manifestPath = Path.GetFullPath(manifestPath);
        _mediaRoot = Path.GetDirectoryName(_manifestPath)
            ?? throw new InvalidOperationException("Cartella media installer non risolta.");

        string json = File.ReadAllText(_manifestPath, Encoding.UTF8);
        Manifest = JsonSerializer.Deserialize<InstallerManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Manifest installer vuoto o non valido.");

        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pulsar",
            "QtisVision",
            "Installer",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        LogPath = Path.Combine(
            logDirectory,
            $"setup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-p{Environment.ProcessId}.log");
        _logWriter = new StreamWriter(LogPath, append: false, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        _coreUpdater = new CoreDifferentialUpdater(Log, ReportProgress);

        Log(
            $"SETUP_START|version={Manifest.ProductVersion}|mediaRevision={Manifest.MediaRevision}|" +
            $"packageType={Manifest.PackageType}|media={_mediaRoot}");
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public InstallerManifest Manifest { get; }
    public string LogPath { get; }

    public event Action<string>? LogMessage;
    public event Action<int, string>? ProgressChanged;

    public CoreInstallationInfo GetCoreInstallationInfo()
    {
        if (!TryNormalizeVersion(Manifest.ProductVersion, out Version targetVersion))
        {
            throw new InvalidDataException(
                $"Versione HMI target non valida nel manifest: {Manifest.ProductVersion}.");
        }

        string executable = Path.Combine(
            Path.GetFullPath(Manifest.InstallRoot),
            "bin",
            "QtisVisionPanel.exe");
        if (!File.Exists(executable))
        {
            return new CoreInstallationInfo
            {
                TargetVersion = targetVersion.ToString(4),
                Mode = "Fresh"
            };
        }

        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executable);
        string? rawInstalledVersion = versionInfo.ProductVersion ?? versionInfo.FileVersion;
        if (!TryNormalizeVersion(rawInstalledVersion, out Version installedVersion))
        {
            return new CoreInstallationInfo
            {
                TargetVersion = targetVersion.ToString(4),
                InstalledVersion = rawInstalledVersion ?? "sconosciuta",
                Mode = "Repair"
            };
        }

        int comparison = installedVersion.CompareTo(targetVersion);
        return new CoreInstallationInfo
        {
            TargetVersion = targetVersion.ToString(4),
            InstalledVersion = installedVersion.ToString(4),
            Mode = comparison < 0
                ? "Upgrade"
                : comparison == 0
                    ? "Repair"
                    : "DowngradeBlocked",
            IsDowngradeBlocked = comparison > 0
        };
    }

    public ComponentDetection Detect(InstallerComponent component)
    {
        if (!component.Enabled)
        {
            component.Detection = ComponentDetection.Invalid;
            return component.Detection;
        }

        List<ComponentDetection> evidence = [];
        if (!string.IsNullOrWhiteSpace(component.DetectionRegistryKey))
        {
            evidence.Add(DetectFromRegistry(component));
        }

        if (!string.IsNullOrWhiteSpace(component.DetectionPath))
        {
            string path = Environment.ExpandEnvironmentVariables(component.DetectionPath);
            evidence.Add(DetectFromPath(path, component.MinimumVersion));
        }

        if (!string.IsNullOrWhiteSpace(component.DetectionSearchFileName) &&
            component.DetectionSearchRoots.Count > 0)
        {
            evidence.Add(DetectFromSearchRoots(component));
        }

        component.Detection = evidence.Count == 0
            ? ComponentDetection.Unknown
            : evidence.Contains(ComponentDetection.Installed)
                ? ComponentDetection.Installed
                : evidence.Contains(ComponentDetection.InstalledOlder)
                    ? ComponentDetection.InstalledOlder
                    : ComponentDetection.Missing;
        return component.Detection;
    }

    private static ComponentDetection DetectFromPath(string path, string? minimumVersion)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return ComponentDetection.Missing;
        }

        if (File.Exists(path) && Version.TryParse(minimumVersion, out Version? minimum))
        {
            string? rawVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (TryNormalizeVersion(rawVersion, out Version? installed) && installed < minimum)
            {
                return ComponentDetection.InstalledOlder;
            }
        }

        return ComponentDetection.Installed;
    }

    private static ComponentDetection DetectFromSearchRoots(InstallerComponent component)
    {
        bool olderVersionFound = false;
        foreach (string configuredRoot in component.DetectionSearchRoots
                     .Where(root => !string.IsNullOrWhiteSpace(root))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string root = Environment.ExpandEnvironmentVariables(configuredRoot);
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (string candidate in Directory.EnumerateFiles(
                             root,
                             component.DetectionSearchFileName!,
                             SearchOption.AllDirectories))
                {
                    ComponentDetection result = DetectFromPath(candidate, component.MinimumVersion);
                    if (result == ComponentDetection.Installed)
                    {
                        return result;
                    }

                    olderVersionFound |= result == ComponentDetection.InstalledOlder;
                }
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // A restricted vendor subfolder must not prevent the remaining known roots from being checked.
            }
        }

        return olderVersionFound
            ? ComponentDetection.InstalledOlder
            : ComponentDetection.Missing;
    }

    public async Task<ValidationReport> ValidateAsync(
        IEnumerable<string> componentIds,
        CancellationToken cancellationToken)
    {
        HashSet<string> selected = componentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<ValidationFinding> findings = [];

        if (Manifest.SchemaVersion != 1)
        {
            findings.Add(Block("manifest", $"Schema manifest non supportato: {Manifest.SchemaVersion}."));
        }

        ValidatePackageContract(findings);

        if (!Environment.Is64BitOperatingSystem)
        {
            findings.Add(Block("system", "QtisVisionPanel richiede Windows x64."));
        }

        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                findings.Add(Block("system", "L'installer deve essere eseguito come amministratore."));
            }
        }

        string installRoot = Path.GetFullPath(Manifest.InstallRoot);
        string? driveRoot = Path.GetPathRoot(installRoot);
        if (!string.IsNullOrWhiteSpace(driveRoot))
        {
            DriveInfo drive = new(driveRoot);
            if (drive.IsReady && drive.AvailableFreeSpace < Manifest.RequiredFreeSpaceBytes)
            {
                findings.Add(Block(
                    "system",
                    $"Spazio insufficiente su {drive.Name}: disponibili {FormatBytes(drive.AvailableFreeSpace)}, richiesti almeno {FormatBytes(Manifest.RequiredFreeSpaceBytes)}."));
            }
        }

        if (Process.GetProcessesByName("QtisVisionPanel").Length > 0)
        {
            findings.Add(Block("qtis-core", "QtisVisionPanel e' in esecuzione. Chiuderlo prima dell'installazione."));
        }

        if (selected.Contains(CoreComponentId))
        {
            CoreInstallationInfo coreInfo = GetCoreInstallationInfo();
            if (Manifest.IsUpdateOnly && coreInfo.Mode == "Fresh")
            {
                findings.Add(Block(
                    CoreComponentId,
                    "Il pacchetto di aggiornamento richiede una Qtis Vision Panel gia installata. Per un PC nuovo usare l'installer completo."));
            }

            if (coreInfo.IsDowngradeBlocked)
            {
                findings.Add(Block(
                    CoreComponentId,
                    $"Downgrade HMI bloccato: installata {coreInfo.InstalledVersion}, media {coreInfo.TargetVersion}. Usare un supporto piu recente."));
            }

            if (_coreUpdater.HasPendingTransaction)
            {
                findings.Add(new ValidationFinding
                {
                    ComponentId = CoreComponentId,
                    Severity = "Warning",
                    Message = "E' presente un aggiornamento HMI interrotto. Premendo Installa / aggiorna verra eseguito prima il rollback automatico.",
                    BlocksInstallation = false
                });
            }
        }

        int totalPayloads = Manifest.Components
            .Where(component => selected.Contains(component.Id))
            .Sum(component => component.Payloads.Count);
        int checkedPayloads = 0;

        foreach (InstallerComponent component in Manifest.Components.Where(component => selected.Contains(component.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!component.Enabled)
            {
                findings.Add(new ValidationFinding
                {
                    ComponentId = component.Id,
                    Severity = component.Required ? "Error" : "Warning",
                    Message = component.BlockingReason ?? $"{component.DisplayName}: componente disabilitato.",
                    BlocksInstallation = component.Required
                });
                continue;
            }

            foreach (string dependencyId in component.DependsOn)
            {
                InstallerComponent? dependency = Manifest.Components.FirstOrDefault(candidate =>
                    candidate.Id.Equals(dependencyId, StringComparison.OrdinalIgnoreCase));
                if (dependency is null)
                {
                    findings.Add(Block(
                        component.Id,
                        $"{component.DisplayName}: dipendenza non definita nel manifest: {dependencyId}."));
                    continue;
                }

                if (!selected.Contains(dependency.Id) &&
                    Detect(dependency) != ComponentDetection.Installed)
                {
                    findings.Add(Block(
                        component.Id,
                        $"{component.DisplayName} richiede {dependency.DisplayName}. Selezionarlo oppure installarlo prima."));
                }
            }

            foreach (InstallerPayload payload in component.Payloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath;
                try
                {
                    fullPath = ResolveMediaPath(payload.RelativePath);
                }
                catch (Exception ex)
                {
                    findings.Add(Block(component.Id, ex.Message));
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    findings.Add(Block(component.Id, $"Payload mancante: {payload.RelativePath}."));
                    continue;
                }

                FileInfo info = new(fullPath);
                if (payload.SizeBytes > 0 && info.Length != payload.SizeBytes)
                {
                    findings.Add(Block(
                        component.Id,
                        $"Dimensione payload non valida: {payload.RelativePath} ({info.Length} invece di {payload.SizeBytes} byte)."));
                    continue;
                }

                ReportProgress(
                    totalPayloads == 0 ? 0 : checkedPayloads * 100 / totalPayloads,
                    $"Verifica SHA-256: {payload.RelativePath}");

                string actualHash = await ComputeSha256Async(fullPath, cancellationToken);
                if (!actualHash.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(Block(component.Id, $"SHA-256 non valido: {payload.RelativePath}."));
                }
                else
                {
                    findings.Add(new ValidationFinding
                    {
                        ComponentId = component.Id,
                        Severity = "Info",
                        Message = $"Payload verificato: {payload.RelativePath}.",
                        BlocksInstallation = false
                    });
                }

                checkedPayloads++;
            }
        }

        foreach (string warning in Manifest.Warnings)
        {
            findings.Add(new ValidationFinding
            {
                ComponentId = "manifest",
                Severity = "Warning",
                Message = warning,
                BlocksInstallation = false
            });
        }

        ReportProgress(100, "Verifica payload completata");
        ValidationReport report = new()
        {
            ProductVersion = Manifest.ProductVersion,
            Findings = findings
        };
        Log($"VALIDATION_COMPLETE|valid={report.IsValid}|findings={findings.Count}");
        return report;
    }

    private void ValidatePackageContract(List<ValidationFinding> findings)
    {
        bool isFull = string.Equals(
            Manifest.PackageType,
            "Full",
            StringComparison.OrdinalIgnoreCase);
        if (!isFull && !Manifest.IsUpdateOnly)
        {
            findings.Add(Block(
                "manifest",
                $"Tipo pacchetto non supportato: {Manifest.PackageType}."));
            return;
        }

        List<InstallerComponent> coreComponents = Manifest.Components.Where(component =>
            component.Id.Equals(CoreComponentId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (coreComponents.Count != 1)
        {
            findings.Add(Block(
                "manifest",
                "Il manifest deve contenere esattamente un componente Qtis core."));
            return;
        }

        InstallerComponent core = coreComponents[0];

        int applicationArchives = core.Payloads.Count(payload =>
            payload.RelativePath.Contains("ApplicationBin", StringComparison.OrdinalIgnoreCase));
        int runtimeArchives = core.Payloads.Count(payload =>
            payload.RelativePath.Contains("RuntimeSeed", StringComparison.OrdinalIgnoreCase));
        if (applicationArchives != 1)
        {
            findings.Add(Block(
                "manifest",
                "Il componente Qtis core deve contenere esattamente un archivio ApplicationBin."));
        }

        if (Manifest.IsUpdateOnly)
        {
            if (Manifest.Components.Count != 1 || core.Payloads.Count != 1 ||
                runtimeArchives != 0 || Manifest.ContainsMachineSpecificConfiguration ||
                !core.Required || !core.Enabled)
            {
                findings.Add(Block(
                    "manifest",
                    "Pacchetto UpdateOnly non valido: e' ammesso un solo payload ApplicationBin nel componente core obbligatorio, senza RuntimeSeed, prerequisiti o configurazioni macchina."));
            }
        }
        else if (runtimeArchives != 1)
        {
            findings.Add(Block(
                "manifest",
                "L'installer completo deve contenere esattamente un archivio RuntimeSeed."));
        }
    }

    public async Task InstallAsync(
        IEnumerable<string> componentIds,
        CancellationToken cancellationToken)
    {
        HashSet<string> selected = componentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Contains(CoreComponentId) && _coreUpdater.HasPendingTransaction)
        {
            if (Process.GetProcessesByName("QtisVisionPanel").Length > 0)
            {
                throw new InvalidOperationException(
                    "QtisVisionPanel e' in esecuzione: impossibile ripristinare l'aggiornamento interrotto.");
            }

            await _coreUpdater.RecoverPendingTransactionAsync(
                Path.GetFullPath(Manifest.InstallRoot),
                cancellationToken);
        }

        ValidationReport validation = await ValidateAsync(selected, cancellationToken);
        if (!validation.IsValid)
        {
            string errors = string.Join(
                Environment.NewLine,
                validation.Findings.Where(item => item.BlocksInstallation).Select(item => item.Message));
            throw new InvalidOperationException($"Verifica installer fallita:{Environment.NewLine}{errors}");
        }

        List<InstallerComponent> components = Manifest.Components
            .Where(component => selected.Contains(component.Id))
            .ToList();

        int index = 0;
        foreach (InstallerComponent component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            ReportProgress((index - 1) * 100 / Math.Max(components.Count, 1), $"Preparazione {component.DisplayName}");

            ComponentDetection detection = Detect(component);
            bool isCore = component.Id.Equals(CoreComponentId, StringComparison.OrdinalIgnoreCase);
            bool mustRepairOfflineEnvironment =
                component.Kind.Equals("python-offline", StringComparison.OrdinalIgnoreCase);
            if (!isCore &&
                !mustRepairOfflineEnvironment &&
                !component.ConfigureWhenInstalled &&
                detection == ComponentDetection.Installed)
            {
                Log($"COMPONENT_SKIP_INSTALLED|id={component.Id}|name={component.DisplayName}");
                continue;
            }

            Log($"COMPONENT_INSTALL_START|id={component.Id}|kind={component.Kind}");
            switch (component.Kind.ToLowerInvariant())
            {
                case "visionpro-zip":
                    await InstallFromZipAsync(component, cancellationToken);
                    break;
                case "interactive-exe":
                    await InstallInteractiveExeAsync(component, cancellationToken);
                    break;
                case "interactive-msi":
                    await InstallInteractiveMsiAsync(component, cancellationToken);
                    break;
                case "ultravnc":
                    await InstallUltraVncAsync(component, detection, cancellationToken);
                    break;
                case "python-offline":
                    await InstallPythonOfflineAsync(component, cancellationToken);
                    break;
                case "qtis-core":
                    await InstallCoreAsync(component, selected, cancellationToken);
                    break;
                case "cognex-gige-network":
                    await new CognexGigENetworkConfigurator(Log, ReportProgress)
                        .ConfigureAsync(Manifest.InstallRoot, cancellationToken);
                    break;
                case "qtis-autostart":
                    new MachineStartupConfigurator(Log).Configure(Manifest.InstallRoot);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Tipo componente non supportato '{component.Kind}' per {component.DisplayName}.");
            }

            Log($"COMPONENT_INSTALL_COMPLETE|id={component.Id}|name={component.DisplayName}");
            ReportProgress(index * 100 / Math.Max(components.Count, 1), $"{component.DisplayName} completato");
        }

        Log("SETUP_COMPLETE");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Log("SETUP_END");
        _logWriter.Dispose();
    }

    private async Task InstallFromZipAsync(InstallerComponent component, CancellationToken cancellationToken)
    {
        InstallerPayload archive = component.Payloads.FirstOrDefault()
            ?? throw new InvalidDataException($"{component.DisplayName}: archivio non configurato.");
        string archivePath = ResolveMediaPath(archive.RelativePath);
        string cacheRoot = GetCacheRoot(component.Id, archive.Sha256);
        string entryPoint = Path.Combine(cacheRoot, component.EntryPoint ?? "setup.exe");

        if (!File.Exists(entryPoint))
        {
            Directory.CreateDirectory(cacheRoot);
            ReportProgress(0, $"Estrazione {component.DisplayName}");
            await Task.Run(
                () => ZipFile.ExtractToDirectory(archivePath, cacheRoot, overwriteFiles: true),
                cancellationToken);
        }

        if (!File.Exists(entryPoint))
        {
            throw new FileNotFoundException(
                $"Setup interno non trovato dopo l'estrazione: {entryPoint}.",
                entryPoint);
        }

        int exitCode = await RunProcessAsync(
            entryPoint,
            SplitArguments(component.InstallArguments),
            captureOutput: false,
            cancellationToken);
        EnsureSuccessfulExit(component, exitCode);

        string cacheParent = Path.GetDirectoryName(cacheRoot)
            ?? throw new InvalidOperationException("Root cache installer non risolta.");
        DeleteDirectoryUnder(cacheRoot, cacheParent);
        Log($"COMPONENT_CACHE_REMOVED|id={component.Id}|path={cacheRoot}");
    }

    private async Task InstallInteractiveExeAsync(
        InstallerComponent component,
        CancellationToken cancellationToken)
    {
        InstallerPayload payload = component.Payloads.FirstOrDefault()
            ?? throw new InvalidDataException($"{component.DisplayName}: eseguibile non configurato.");
        int exitCode = await RunProcessAsync(
            ResolveMediaPath(payload.RelativePath),
            SplitArguments(component.InstallArguments),
            captureOutput: false,
            cancellationToken);
        EnsureSuccessfulExit(component, exitCode);
    }

    private async Task InstallInteractiveMsiAsync(
        InstallerComponent component,
        CancellationToken cancellationToken)
    {
        InstallerPayload payload = component.Payloads.FirstOrDefault()
            ?? throw new InvalidDataException($"{component.DisplayName}: MSI non configurato.");
        List<string> arguments =
        [
            "/i",
            ResolveMediaPath(payload.RelativePath),
            "/norestart"
        ];
        arguments.AddRange(SplitArguments(component.InstallArguments));

        int exitCode = await RunProcessAsync(
            Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
            arguments,
            captureOutput: false,
            cancellationToken);
        EnsureSuccessfulExit(component, exitCode);
    }

    private async Task InstallUltraVncAsync(
        InstallerComponent component,
        ComponentDetection detection,
        CancellationToken cancellationToken)
    {
        if (detection != ComponentDetection.Installed)
        {
            InstallerPayload payload = component.Payloads.FirstOrDefault()
                ?? throw new InvalidDataException($"{component.DisplayName}: eseguibile non configurato.");
            int installExitCode = await RunProcessAsync(
                ResolveMediaPath(payload.RelativePath),
                SplitArguments(component.InstallArguments),
                captureOutput: false,
                cancellationToken);
            EnsureSuccessfulExit(component, installExitCode);
        }
        else
        {
            Log("ULTRAVNC_INSTALL_SKIPPED|reason=compatible version already installed");
        }

        await ConfigureUltraVncAsync(component, cancellationToken);
    }

    private async Task ConfigureUltraVncAsync(
        InstallerComponent component,
        CancellationToken cancellationToken)
    {
        string installRoot = ResolveUltraVncInstallRoot(component);
        string winVncPath = Path.Combine(installRoot, "winvnc.exe");
        string setPasswordPath = Path.Combine(installRoot, "setpasswd.exe");
        if (!File.Exists(winVncPath) || !File.Exists(setPasswordPath))
        {
            throw new FileNotFoundException(
                $"Installazione UltraVNC incompleta in '{installRoot}': winvnc.exe o setpasswd.exe mancante.");
        }

        string programDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "UltraVNC");
        Directory.CreateDirectory(programDataRoot);
        string programDataIni = Path.Combine(programDataRoot, UltraVncIniFileName);
        string installIni = Path.Combine(installRoot, UltraVncIniFileName);
        EnsureUltraVncConfigurationSeed(programDataIni, installIni);

        bool serviceExisted = await UltraVncServiceExistsAsync(cancellationToken);
        if (serviceExisted)
        {
            await StopUltraVncServiceAsync(cancellationToken);
        }

        string provisioningBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pulsar",
            "QtisVision",
            "Installer",
            "UltraVncProvisioning");
        string provisioningRoot = Path.Combine(provisioningBase, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(provisioningRoot);

        Exception? provisioningFailure = null;
        try
        {
            string stagedHelper = Path.Combine(provisioningRoot, "setpasswd.exe");
            string stagedIni = Path.Combine(provisioningRoot, UltraVncIniFileName);
            File.Copy(setPasswordPath, stagedHelper, overwrite: true);
            File.Copy(programDataIni, stagedIni, overwrite: true);

            string accessPassword = BuildUltraVncAccessPassword();
            int passwordExitCode;
            try
            {
                passwordExitCode = await RunSensitiveProcessAsync(
                    stagedHelper,
                    [accessPassword],
                    provisioningRoot,
                    cancellationToken);
            }
            finally
            {
                accessPassword = string.Empty;
            }

            if (passwordExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"UltraVNC setpasswd ha restituito il codice {passwordExitCode}.");
            }

            Version installedVersion = ReadFileVersion(winVncPath);
            string generatedIni = installedVersion >= new Version(1, 8, 0, 6)
                ? programDataIni
                : stagedIni;
            string encryptedPassword = ReadUltraVncPasswordValue(generatedIni);
            if (string.IsNullOrWhiteSpace(encryptedPassword))
            {
                throw new InvalidDataException(
                    "UltraVNC non ha generato un valore passwd valido in ultravnc.ini.");
            }

            ApplyUltraVncPasswordAtomically(programDataIni, encryptedPassword);
            if (File.Exists(installIni))
            {
                ApplyUltraVncPasswordAtomically(installIni, encryptedPassword);
            }

            if (string.IsNullOrWhiteSpace(ReadUltraVncPasswordValue(programDataIni)))
            {
                throw new InvalidDataException(
                    "Verifica password UltraVNC fallita dopo il salvataggio in ProgramData.");
            }

            Log(
                $"ULTRAVNC_PASSWORD_CONFIGURED|config={programDataIni}|version={installedVersion}|secret=redacted");
        }
        catch (Exception ex)
        {
            provisioningFailure = ex;
        }
        finally
        {
            try
            {
                if (Directory.Exists(provisioningRoot))
                {
                    DeleteDirectoryUnder(provisioningRoot, provisioningBase);
                }
            }
            catch (Exception cleanupException)
            {
                Log($"ULTRAVNC_PROVISIONING_CLEANUP_FAILED|error={cleanupException.Message}");
            }

            try
            {
                await EnsureUltraVncServiceRunningAsync(winVncPath, CancellationToken.None);
            }
            catch (Exception serviceException)
            {
                if (provisioningFailure is null)
                {
                    provisioningFailure = serviceException;
                }
                else
                {
                    Log($"ULTRAVNC_SERVICE_RECOVERY_FAILED|error={serviceException.Message}");
                }
            }
        }

        if (provisioningFailure is not null)
        {
            throw new InvalidOperationException(
                "Configurazione automatica UltraVNC non completata.",
                provisioningFailure);
        }
    }

    private static string ResolveUltraVncInstallRoot(InstallerComponent component)
    {
        if (!string.IsNullOrWhiteSpace(component.DetectionRegistryKey))
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(
                    component.DetectionRegistryKey,
                    writable: false);
                string? registeredRoot = key?.GetValue("InstallLocation")?.ToString();
                if (!string.IsNullOrWhiteSpace(registeredRoot) &&
                    File.Exists(Path.Combine(registeredRoot, "winvnc.exe")))
                {
                    return Path.GetFullPath(registeredRoot);
                }
            }
        }

        string[] candidates =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "uvnc bvba",
                "UltraVNC"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "UltraVNC"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "uvnc bvba",
                "UltraVNC"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "UltraVNC")
        ];
        string? root = candidates.FirstOrDefault(candidate =>
            File.Exists(Path.Combine(candidate, "winvnc.exe")));
        return root
            ?? throw new DirectoryNotFoundException(
                "Cartella UltraVNC non trovata dopo l'installazione.");
    }

    private static void EnsureUltraVncConfigurationSeed(
        string programDataIni,
        string installIni)
    {
        if (File.Exists(programDataIni))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(programDataIni)!);
        if (File.Exists(installIni))
        {
            File.Copy(installIni, programDataIni, overwrite: false);
            return;
        }

        File.WriteAllText(
            programDataIni,
            $"[ultravnc]{Environment.NewLine}",
            new UTF8Encoding(false));
    }

    private static Version ReadFileVersion(string path)
    {
        string? rawVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
        return TryNormalizeVersion(rawVersion, out Version version)
            ? version
            : new Version(0, 0);
    }

    private static string ReadUltraVncPasswordValue(string iniPath)
    {
        if (!File.Exists(iniPath))
        {
            return string.Empty;
        }

        bool inUltraVncSection = false;
        foreach (string rawLine in File.ReadLines(iniPath))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) &&
                line.EndsWith("]", StringComparison.Ordinal))
            {
                inUltraVncSection = line.Equals(
                    "[ultravnc]",
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inUltraVncSection)
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0 ||
                !line[..separator].Trim().Equals("passwd", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(separator + 1)..].Trim();
        }

        return string.Empty;
    }

    private static void ApplyUltraVncPasswordAtomically(
        string iniPath,
        string encryptedPassword)
    {
        List<string> lines = File.Exists(iniPath)
            ? File.ReadAllLines(iniPath).ToList()
            : [];
        int sectionIndex = lines.FindIndex(line =>
            line.Trim().Equals("[ultravnc]", StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }
            sectionIndex = lines.Count;
            lines.Add("[ultravnc]");
        }

        int nextSectionIndex = lines.FindIndex(
            sectionIndex + 1,
            line =>
            {
                string trimmed = line.Trim();
                return trimmed.StartsWith("[", StringComparison.Ordinal) &&
                       trimmed.EndsWith("]", StringComparison.Ordinal);
            });
        int sectionEnd = nextSectionIndex < 0 ? lines.Count : nextSectionIndex;
        int passwordIndex = -1;
        for (int index = sectionIndex + 1; index < sectionEnd; index++)
        {
            string line = lines[index];
            int separator = line.IndexOf('=');
            if (separator > 0 &&
                line[..separator].Trim().Equals("passwd", StringComparison.OrdinalIgnoreCase))
            {
                passwordIndex = index;
                break;
            }
        }

        string passwordLine = $"passwd={encryptedPassword}";
        if (passwordIndex >= 0)
        {
            lines[passwordIndex] = passwordLine;
        }
        else
        {
            lines.Insert(sectionIndex + 1, passwordLine);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        string temporaryPath = iniPath + ".qtis.tmp";
        string backupPath = iniPath + ".before-qtis-password";
        File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(false));
        if (File.Exists(iniPath))
        {
            File.Replace(temporaryPath, iniPath, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, iniPath);
        }
    }

    private async Task<bool> UltraVncServiceExistsAsync(CancellationToken cancellationToken)
    {
        return await QueryUltraVncServiceStateAsync(cancellationToken) is not null;
    }

    private async Task StopUltraVncServiceAsync(CancellationToken cancellationToken)
    {
        string? state = await QueryUltraVncServiceStateAsync(cancellationToken);
        if (state is null || state.Equals("STOPPED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string serviceController = Path.Combine(Environment.SystemDirectory, "sc.exe");
        await RunProcessAsync(
            serviceController,
            ["stop", UltraVncServiceName],
            captureOutput: true,
            cancellationToken);
        string? stoppedState = await WaitForUltraVncServiceStateAsync(
            "STOPPED",
            TimeSpan.FromSeconds(15),
            cancellationToken);
        if (!string.Equals(stoppedState, "STOPPED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Il servizio UltraVNC non si e' arrestato. Stato corrente: {stoppedState ?? "non rilevato"}.");
        }

        Log("ULTRAVNC_SERVICE_STOPPED");
    }

    private async Task EnsureUltraVncServiceRunningAsync(
        string winVncPath,
        CancellationToken cancellationToken)
    {
        string serviceController = Path.Combine(Environment.SystemDirectory, "sc.exe");
        if (!await UltraVncServiceExistsAsync(cancellationToken))
        {
            int installExitCode = await RunProcessAsync(
                winVncPath,
                ["-install"],
                captureOutput: true,
                cancellationToken);
            if (installExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Registrazione servizio UltraVNC fallita con codice {installExitCode}.");
            }
        }

        int configExitCode = await RunProcessAsync(
            serviceController,
            ["config", UltraVncServiceName, "start=", "auto"],
            captureOutput: true,
            cancellationToken);
        if (configExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Configurazione avvio automatico UltraVNC fallita con codice {configExitCode}.");
        }

        string? state = await QueryUltraVncServiceStateAsync(cancellationToken);
        if (!string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            int startExitCode = await RunProcessAsync(
                serviceController,
                ["start", UltraVncServiceName],
                captureOutput: true,
                cancellationToken);
            if (startExitCode is not (0 or 1056))
            {
                throw new InvalidOperationException(
                    $"Avvio servizio UltraVNC fallito con codice {startExitCode}.");
            }
        }

        string? runningState = await WaitForUltraVncServiceStateAsync(
            "RUNNING",
            TimeSpan.FromSeconds(20),
            cancellationToken);
        if (!string.Equals(runningState, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Il servizio UltraVNC non e' entrato in RUNNING. Stato corrente: {runningState ?? "non rilevato"}.");
        }

        Log("ULTRAVNC_SERVICE_READY|startup=Automatic|state=RUNNING");
    }

    private async Task<string?> WaitForUltraVncServiceStateAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string? state;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            state = await QueryUltraVncServiceStateAsync(cancellationToken);
            if (string.Equals(state, expectedState, StringComparison.OrdinalIgnoreCase))
            {
                return state;
            }

            await Task.Delay(250, cancellationToken);
        }
        while (stopwatch.Elapsed < timeout);

        return state;
    }

    private static async Task<string?> QueryUltraVncServiceStateAsync(
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("query");
        startInfo.ArgumentList.Add(UltraVncServiceName);

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            return null;
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output = await stdout;
        _ = await stderr;
        if (process.ExitCode != 0)
        {
            return null;
        }

        string? stateLine = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line =>
                line.Contains("STATE", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("STATO", StringComparison.OrdinalIgnoreCase));
        if (stateLine is null)
        {
            return "UNKNOWN";
        }

        Match match = Regex.Match(
            stateLine,
            @":\s*\d+\s+([A-Z_]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "UNKNOWN";
    }

    private static string BuildUltraVncAccessPassword()
    {
        string password = Environment.GetEnvironmentVariable("QTIS_ULTRAVNC_PASSWORD") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set QTIS_ULTRAVNC_PASSWORD before configuring remote assistance.");
        }
        return password;
    }

    private async Task InstallPythonOfflineAsync(
        InstallerComponent component,
        CancellationToken cancellationToken)
    {
        InstallerPayload installerPayload = component.Payloads.FirstOrDefault(payload =>
            payload.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Installer Python non configurato.");

        string pythonRoot = Path.Combine(Manifest.InstallRoot, "AI", "Python");
        string pythonExe = Path.Combine(pythonRoot, "python.exe");
        bool installPython = !File.Exists(pythonExe);
        if (!installPython && Version.TryParse(component.MinimumVersion, out Version? minimumVersion))
        {
            string? rawVersion = FileVersionInfo.GetVersionInfo(pythonExe).FileVersion;
            installPython =
                !TryNormalizeVersion(rawVersion, out Version installedVersion) ||
                installedVersion < minimumVersion;
        }

        if (installPython)
        {
            string arguments = (component.InstallArguments ?? string.Empty)
                .Replace("{PythonRoot}", pythonRoot, StringComparison.OrdinalIgnoreCase)
                .Replace("{InstallRoot}", Manifest.InstallRoot, StringComparison.OrdinalIgnoreCase);

            int installExit = await RunProcessAsync(
                ResolveMediaPath(installerPayload.RelativePath),
                SplitArguments(arguments),
                captureOutput: true,
                cancellationToken);
            EnsureSuccessfulExit(component, installExit);
        }

        if (!File.Exists(pythonExe))
        {
            throw new FileNotFoundException($"Python non trovato dopo l'installazione: {pythonExe}.", pythonExe);
        }

        int ensurePipExit = await RunProcessAsync(
            pythonExe,
            ["-m", "ensurepip", "--upgrade", "--default-pip"],
            captureOutput: true,
            cancellationToken);
        EnsureSuccessfulExit(component, ensurePipExit);

        InstallerPayload requirementsPayload = component.Payloads.FirstOrDefault(payload =>
            payload.RelativePath.EndsWith("requirements-ai.txt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("requirements-ai.txt non configurato.");
        string requirementsPath = ResolveMediaPath(requirementsPayload.RelativePath);
        string wheelhouse = Path.GetDirectoryName(
            ResolveMediaPath(component.Payloads.First(payload =>
                payload.RelativePath.EndsWith(".whl", StringComparison.OrdinalIgnoreCase)).RelativePath))
            ?? throw new InvalidDataException("Cartella wheelhouse non risolta.");

        int pipExit = await RunProcessAsync(
            pythonExe,
            [
                "-m", "pip", "install",
                "--no-index",
                "--disable-pip-version-check",
                "--find-links", wheelhouse,
                "-r", requirementsPath
            ],
            captureOutput: true,
            cancellationToken);
        EnsureSuccessfulExit(component, pipExit);

        InstallerPayload smokeTestPayload = component.Payloads.FirstOrDefault(payload =>
            payload.RelativePath.EndsWith("verify_ai_environment.py", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Smoke test Python non configurato.");
        int testExit = await RunProcessAsync(
            pythonExe,
            [ResolveMediaPath(smokeTestPayload.RelativePath)],
            captureOutput: true,
            cancellationToken);
        EnsureSuccessfulExit(component, testExit);

        MachineEnvironmentConfigurator.ConfigurePython(pythonRoot, Log);
    }

    private async Task InstallCoreAsync(
        InstallerComponent component,
        HashSet<string> selectedComponents,
        CancellationToken cancellationToken)
    {
        InstallerPayload binPayload = component.Payloads.FirstOrDefault(payload =>
            payload.RelativePath.Contains("ApplicationBin", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Archivio ApplicationBin non configurato.");
        InstallerPayload? runtimePayload = component.Payloads.FirstOrDefault(payload =>
            payload.RelativePath.Contains("RuntimeSeed", StringComparison.OrdinalIgnoreCase));
        if (!Manifest.IsUpdateOnly && runtimePayload is null)
        {
            throw new InvalidDataException("Archivio RuntimeSeed non configurato.");
        }

        if (Process.GetProcessesByName("QtisVisionPanel").Length > 0)
        {
            throw new InvalidOperationException("QtisVisionPanel e' ancora in esecuzione.");
        }

        CoreInstallationInfo installationInfo = GetCoreInstallationInfo();
        if (Manifest.IsUpdateOnly && installationInfo.Mode == "Fresh")
        {
            throw new InvalidOperationException(
                "Aggiornamento rifiutato: Qtis Vision Panel non risulta installata. Usare l'installer completo.");
        }

        string installRoot = Path.GetFullPath(Manifest.InstallRoot);
        if (Manifest.IsUpdateOnly && !Directory.Exists(installRoot))
        {
            throw new DirectoryNotFoundException(
                $"Aggiornamento rifiutato: cartella di installazione non trovata: {installRoot}.");
        }

        Directory.CreateDirectory(installRoot);

        string stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pulsar",
            "QtisVision",
            "Installer",
            "Staging",
            Guid.NewGuid().ToString("N"));
        string stagedBin = Path.Combine(stagingRoot, "bin");
        string stagedRuntime = Path.Combine(stagingRoot, "runtime");
        Directory.CreateDirectory(stagedBin);
        if (!Manifest.IsUpdateOnly)
        {
            Directory.CreateDirectory(stagedRuntime);
        }

        ReportProgress(0, "Estrazione applicazione");
        await Task.Run(
            () => ZipFile.ExtractToDirectory(ResolveMediaPath(binPayload.RelativePath), stagedBin),
            cancellationToken);
        if (runtimePayload is not null)
        {
            await Task.Run(
                () => ZipFile.ExtractToDirectory(ResolveMediaPath(runtimePayload.RelativePath), stagedRuntime),
                cancellationToken);
        }

        string expectedExe = Path.Combine(stagedBin, "QtisVisionPanel.exe");
        if (!File.Exists(expectedExe))
        {
            throw new InvalidDataException($"Archivio bin non valido: manca {expectedExe}.");
        }

        if (installationInfo.IsDowngradeBlocked)
        {
            throw new InvalidOperationException(
                $"Downgrade HMI bloccato: installata {installationInfo.InstalledVersion}, media {installationInfo.TargetVersion}.");
        }

        ManagedApplicationManifest targetManifest =
            await _coreUpdater.BuildTargetManifestAsync(
                stagedBin,
                Manifest.ProductVersion,
                Manifest.MediaRevision,
                cancellationToken);

        string backupRoot = Path.Combine(
            installRoot,
            "InstallerBackups",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupRoot);

        string destinationBin = Path.Combine(installRoot, "bin");
        try
        {
            if (Directory.Exists(destinationBin))
            {
                await InstallCoreDifferentialAsync(
                    stagedBin,
                    stagedRuntime,
                    destinationBin,
                    installRoot,
                    backupRoot,
                    installationInfo,
                    targetManifest,
                    selectedComponents,
                    cancellationToken);
            }
            else if (!Manifest.IsUpdateOnly)
            {
                await InstallCoreFreshAsync(
                    stagedBin,
                    stagedRuntime,
                    destinationBin,
                    installRoot,
                    backupRoot,
                    installationInfo,
                    targetManifest,
                    selectedComponents,
                    cancellationToken);
            }
            else
            {
                throw new DirectoryNotFoundException(
                    $"Aggiornamento rifiutato: cartella applicativa non trovata: {destinationBin}.");
            }
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                DeleteDirectoryUnder(
                    stagingRoot,
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Pulsar",
                        "QtisVision",
                        "Installer",
                        "Staging"));
            }
        }
    }

    private async Task InstallCoreDifferentialAsync(
        string stagedBin,
        string stagedRuntime,
        string destinationBin,
        string installRoot,
        string backupRoot,
        CoreInstallationInfo installationInfo,
        ManagedApplicationManifest targetManifest,
        HashSet<string> selectedComponents,
        CancellationToken cancellationToken)
    {
        ManagedApplicationManifest? previousManifest =
            _coreUpdater.LoadTrustedPreviousManifest(installationInfo.InstalledVersion);
        CoreDifferentialPlan plan = await _coreUpdater.BuildPlanAsync(
            stagedBin,
            destinationBin,
            targetManifest,
            previousManifest,
            cancellationToken);
        CoreUpdateJournal journal = await _coreUpdater.PrepareTransactionAsync(
            installRoot,
            destinationBin,
            backupRoot,
            installationInfo,
            plan,
            cancellationToken);

        try
        {
            await _coreUpdater.ApplyAsync(
                stagedBin,
                destinationBin,
                plan,
                cancellationToken);
            string? visionProDependenciesTarget;
            if (Manifest.IsUpdateOnly)
            {
                visionProDependenciesTarget = ResolveExistingVisionProDependenciesTarget(destinationBin);
                Log(
                    "CORE_UPDATE_ONLY_RUNTIME_PRESERVED|" +
                    "runtimeSeed=skipped|pythonConfig=skipped|shortcuts=skipped|machineConfiguration=preserved");
            }
            else
            {
                visionProDependenciesTarget =
                    await EnsureVisionProDependenciesJunctionAsync(destinationBin, cancellationToken);
                await CopyRuntimeSeedAsync(stagedRuntime, installRoot, backupRoot, cancellationToken);
                ApplyPythonConfigurationIfAvailable(
                    destinationBin,
                    installRoot,
                    backupRoot);
                CreateShortcuts(Path.Combine(destinationBin, "QtisVisionPanel.exe"));
            }

            _coreUpdater.WriteTargetManifest(targetManifest);
            WriteInstallState(
                installRoot,
                plan.UpdatedFiles.Count + plan.RemovedFiles.Count > 0
                    ? Path.Combine(backupRoot, "ApplicationChanged")
                    : null,
                visionProDependenciesTarget,
                selectedComponents,
                installationInfo,
                plan.AddedFiles.Count,
                plan.UpdatedFiles.Count,
                plan.RemovedFiles.Count,
                plan.UnchangedFiles);
            _coreUpdater.CommitTransaction(journal);
        }
        catch (Exception installException)
        {
            Log($"CORE_DIFFERENTIAL_ROLLBACK_START|error={installException.Message}");
            try
            {
                await _coreUpdater.RollbackAsync(journal, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Aggiornamento differenziale fallito e rollback non completato. Consultare il log installer.",
                    installException,
                    rollbackException);
            }

            throw;
        }
    }

    private async Task InstallCoreFreshAsync(
        string stagedBin,
        string stagedRuntime,
        string destinationBin,
        string installRoot,
        string backupRoot,
        CoreInstallationInfo installationInfo,
        ManagedApplicationManifest targetManifest,
        HashSet<string> selectedComponents,
        CancellationToken cancellationToken)
    {
        bool newBinInstalled = false;
        try
        {
            Directory.Move(stagedBin, destinationBin);
            newBinInstalled = true;
            Log($"CORE_BIN_INSTALLED|mode=fresh|path={destinationBin}");

            string visionProDependenciesTarget =
                await EnsureVisionProDependenciesJunctionAsync(destinationBin, cancellationToken);
            await CopyRuntimeSeedAsync(stagedRuntime, installRoot, backupRoot, cancellationToken);
            ApplyPythonConfigurationIfAvailable(
                destinationBin,
                installRoot,
                backupRoot);
            CreateShortcuts(Path.Combine(destinationBin, "QtisVisionPanel.exe"));

            _coreUpdater.WriteTargetManifest(targetManifest);
            WriteInstallState(
                installRoot,
                previousBinBackup: null,
                visionProDependenciesTarget,
                selectedComponents,
                installationInfo,
                targetManifest.Files.Count,
                filesUpdated: 0,
                filesRemoved: 0,
                filesUnchanged: 0);
        }
        catch (Exception installException)
        {
            Log("CORE_INSTALL_ROLLBACK_START|mode=fresh");
            if (newBinInstalled && Directory.Exists(destinationBin))
            {
                try
                {
                    RemoveVisionProDependenciesJunction(destinationBin);
                    DeleteDirectoryUnder(destinationBin, installRoot);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Installazione Qtis fallita e rollback non completato. Consultare il log installer.",
                        installException,
                        rollbackException);
                }
            }

            Log("CORE_INSTALL_ROLLBACK_COMPLETE|mode=fresh");
            throw;
        }
    }

    private void ApplyPythonConfigurationIfAvailable(
        string destinationBin,
        string installRoot,
        string backupRoot)
    {
        string pythonRoot = Path.Combine(installRoot, "AI", "Python");
        string pythonExe = Path.Combine(pythonRoot, "python.exe");
        if (!File.Exists(pythonExe))
        {
            Log($"PYTHON_CONFIGURATION_SKIPPED_NOT_INSTALLED|path={pythonExe}");
            return;
        }

        UpdatePythonPathInMachineConfiguration(
            Path.Combine(destinationBin, "cfg", "machine_runtime_config.xml"),
            pythonExe,
            backupRoot);
        MachineEnvironmentConfigurator.ConfigurePython(pythonRoot, Log);
    }

    private async Task<string> EnsureVisionProDependenciesJunctionAsync(
        string destinationBin,
        CancellationToken cancellationToken)
    {
        string target = ResolveVisionProBinDirectory();
        string linkPath = Path.Combine(destinationBin, "VisionProDependencies");

        if (Directory.Exists(linkPath))
        {
            FileAttributes existingAttributes = File.GetAttributes(linkPath);
            if ((existingAttributes & FileAttributes.ReparsePoint) == 0)
            {
                throw new InvalidOperationException(
                    $"{linkPath} esiste ma non e' una junction o un link simbolico.");
            }

            FileSystemInfo? resolvedLink = new DirectoryInfo(linkPath).ResolveLinkTarget(returnFinalTarget: true);
            string existingSentinel = Path.Combine(linkPath, "Cognex.Vision.Startup.Net.dll");
            if (resolvedLink is not null &&
                Path.GetFullPath(resolvedLink.FullName).Equals(
                    Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(existingSentinel))
            {
                Log($"CORE_VISIONPRO_DEPENDENCIES_REUSED|link={linkPath}|target={target}");
                return target;
            }
        }

        RemoveVisionProDependenciesJunction(destinationBin);

        string commandInterpreter = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (linkPath.IndexOfAny(['"', '\r', '\n']) >= 0 ||
            target.IndexOfAny(['"', '\r', '\n']) >= 0)
        {
            throw new InvalidOperationException(
                "Percorso VisionProDependencies non valido per la creazione della junction.");
        }

        string createJunctionArguments =
            $"/D /C mklink /J \"{linkPath}\" \"{target}\"";
        int exitCode = await RunProcessWithRawArgumentsAsync(
            commandInterpreter,
            createJunctionArguments,
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Creazione della junction VisionProDependencies fallita con codice {exitCode}.");
        }

        FileAttributes attributes = File.GetAttributes(linkPath);
        string sentinel = Path.Combine(linkPath, "Cognex.Vision.Startup.Net.dll");
        if ((attributes & FileAttributes.ReparsePoint) == 0 || !File.Exists(sentinel))
        {
            throw new InvalidOperationException(
                $"VisionProDependencies non e' valida o non espone la DLL Cognex richiesta: {sentinel}.");
        }

        Log($"CORE_VISIONPRO_DEPENDENCIES_READY|link={linkPath}|target={target}|sentinel={sentinel}");
        return target;
    }

    private void RemoveVisionProDependenciesJunction(string destinationBin)
    {
        string linkPath = Path.Combine(destinationBin, "VisionProDependencies");
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(linkPath);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            throw new InvalidOperationException(
                $"Rimozione rifiutata: {linkPath} esiste ma non e' una junction o un link simbolico.");
        }

        Directory.Delete(linkPath, recursive: false);
        Log($"CORE_VISIONPRO_DEPENDENCIES_LINK_REMOVED|path={linkPath}");
    }

    private string? ResolveExistingVisionProDependenciesTarget(string destinationBin)
    {
        string linkPath = Path.Combine(destinationBin, "VisionProDependencies");
        string sentinel = Path.Combine(linkPath, "Cognex.Vision.Startup.Net.dll");
        if (!File.Exists(sentinel))
        {
            Log(
                $"CORE_UPDATE_ONLY_VISIONPRO_DEPENDENCIES_UNAVAILABLE|path={linkPath}|" +
                "action=preserved");
            return null;
        }

        try
        {
            string? target = Directory.ResolveLinkTarget(linkPath, returnFinalTarget: true)?.FullName;
            Log(
                $"CORE_UPDATE_ONLY_VISIONPRO_DEPENDENCIES_PRESERVED|link={linkPath}|" +
                $"target={target ?? "unresolved"}");
            return target;
        }
        catch (IOException ex)
        {
            Log(
                $"CORE_UPDATE_ONLY_VISIONPRO_DEPENDENCIES_TARGET_UNRESOLVED|path={linkPath}|" +
                $"error={ex.Message}|action=preserved");
            return null;
        }
    }

    private static string ResolveVisionProBinDirectory()
    {
        string?[] roots =
        [
            Environment.GetEnvironmentVariable("VPRO_ROOT"),
            Environment.GetEnvironmentVariable("VPRO_ROOT", EnvironmentVariableTarget.Machine),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Cognex",
                "VisionPro")
        ];

        foreach (string root in roots
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value!.Trim().Trim('"'))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar))
                .Equals("bin", StringComparison.OrdinalIgnoreCase)
                ? root
                : Path.Combine(root, "bin");
            string sentinel = Path.Combine(candidate, "Cognex.Vision.Startup.Net.dll");
            if (File.Exists(sentinel))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new DirectoryNotFoundException(
            "Runtime VisionPro non trovato. Installare VisionPro 9.25 e verificare VPRO_ROOT prima della HMI.");
    }

    private async Task CopyRuntimeSeedAsync(
        string sourceRoot,
        string installRoot,
        string backupRoot,
        CancellationToken cancellationToken)
    {
        string[] files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
        int copied = 0;
        foreach (string source in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(sourceRoot, source);
            string destination = Path.Combine(installRoot, relative);
            bool softwareOwned = relative.StartsWith($"Language{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

            if (File.Exists(destination) && !softwareOwned)
            {
                continue;
            }

            if (File.Exists(destination))
            {
                if (await FilesMatchAsync(source, destination, cancellationToken))
                {
                    continue;
                }

                string backup = Path.Combine(backupRoot, "RuntimeOverwritten", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, overwrite: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(source, destination, cancellationToken);
            copied++;
            if (copied % 25 == 0)
            {
                ReportProgress(
                    files.Length == 0 ? 0 : copied * 100 / files.Length,
                    $"Installazione file runtime {copied}/{files.Length}");
            }
        }

        Log($"RUNTIME_SEED_APPLIED|filesCopied={copied}|filesTotal={files.Length}");
    }

    private void UpdatePythonPathInMachineConfiguration(
        string configPath,
        string pythonPath,
        string backupRoot)
    {
        if (!File.Exists(configPath) || !File.Exists(pythonPath))
        {
            return;
        }

        XDocument document = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        XElement? node = document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals(
                    "DefectClassifierTrainingPythonPath",
                    StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            XElement? runtimeBindings = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName.Equals("RuntimeBindings", StringComparison.OrdinalIgnoreCase));
            if (runtimeBindings is null)
            {
                Log($"PYTHON_CONFIG_PATH_SKIPPED|reason=RuntimeBindings missing|path={configPath}");
                return;
            }

            node = new XElement("DefectClassifierTrainingPythonPath");
            runtimeBindings.Add(node);
        }

        if (node.Value.Equals(pythonPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string backup = Path.Combine(backupRoot, "Config.xml.before-python-path");
        File.Copy(configPath, backup, overwrite: true);
        node.Value = pythonPath;

        string temporary = configPath + ".installer.tmp";
        document.Save(temporary, SaveOptions.DisableFormatting);
        File.Replace(temporary, configPath, destinationBackupFileName: null);
        Log($"PYTHON_CONFIG_PATH_APPLIED|path={pythonPath}");
    }

    private void CreateShortcuts(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            return;
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        string programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            "Pulsar Engineering");
        Directory.CreateDirectory(programs);

        CreateShortcut(Path.Combine(desktop, "Qtis Vision Panel.lnk"), executablePath);
        CreateShortcut(Path.Combine(programs, "Qtis Vision Panel.lnk"), executablePath);
    }

    private static void CreateShortcut(string shortcutPath, string executablePath)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host non disponibile.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Impossibile creare WScript.Shell.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = executablePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
        shortcut.Description = "Pulsar Engineering - Qtis Vision Panel";
        shortcut.IconLocation = executablePath;
        shortcut.Save();
    }

    private void WriteInstallState(
        string installRoot,
        string? previousBinBackup,
        string? visionProDependenciesTarget,
        HashSet<string> installedComponents,
        CoreInstallationInfo installationInfo,
        int filesAdded,
        int filesUpdated,
        int filesRemoved,
        int filesUnchanged)
    {
        InstallState? previousState = Manifest.IsUpdateOnly
            ? TryReadPreviousInstallState()
            : null;
        List<string> recordedComponents = installedComponents
            .Concat(previousState?.InstalledComponents ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        InstallState state = new()
        {
            ProductVersion = Manifest.ProductVersion,
            MediaRevision = Manifest.MediaRevision,
            InstalledAtUtc = DateTime.UtcNow,
            InstallerMediaPath = _mediaRoot,
            PreviousBinBackup = previousBinBackup,
            VisionProDependenciesTarget =
                visionProDependenciesTarget ?? previousState?.VisionProDependenciesTarget,
            InstalledComponents = recordedComponents,
            InstallMode = installationInfo.Mode,
            PackageType = Manifest.PackageType,
            PreviousProductVersion = installationInfo.InstalledVersion,
            ApplicationFilesAdded = filesAdded,
            ApplicationFilesUpdated = filesUpdated,
            ApplicationFilesRemoved = filesRemoved,
            ApplicationFilesUnchanged = filesUnchanged
        };
        CoreDifferentialUpdater.WriteJsonAtomically(
            CoreDifferentialUpdater.InstallStatePath,
            state);
        Log(
            $"INSTALL_STATE_WRITTEN|path={CoreDifferentialUpdater.InstallStatePath}|" +
            $"mode={installationInfo.Mode}|added={filesAdded}|updated={filesUpdated}|" +
            $"removed={filesRemoved}|unchanged={filesUnchanged}");
    }

    private InstallState? TryReadPreviousInstallState()
    {
        if (!File.Exists(CoreDifferentialUpdater.InstallStatePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallState>(
                File.ReadAllText(CoreDifferentialUpdater.InstallStatePath, Encoding.UTF8),
                JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log(
                $"INSTALL_STATE_PREVIOUS_READ_FAILED|path={CoreDifferentialUpdater.InstallStatePath}|" +
                $"error={ex.Message}|action=continue");
            return null;
        }
    }

    private static async Task<bool> FilesMatchAsync(
        string leftPath,
        string rightPath,
        CancellationToken cancellationToken)
    {
        FileInfo left = new(leftPath);
        FileInfo right = new(rightPath);
        if (left.Length != right.Length)
        {
            return false;
        }

        byte[] leftHash;
        byte[] rightHash;
        await using (FileStream stream = new(
                         leftPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         1024 * 1024,
                         useAsync: true))
        {
            leftHash = await SHA256.HashDataAsync(stream, cancellationToken);
        }

        await using (FileStream stream = new(
                         rightPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         1024 * 1024,
                         useAsync: true))
        {
            rightHash = await SHA256.HashDataAsync(stream, cancellationToken);
        }

        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private async Task<int> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        bool captureOutput,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            UseShellExecute = !captureOutput,
            CreateNoWindow = captureOutput,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? _mediaRoot
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Log($"PROCESS_START|file={fileName}|arguments={string.Join(" ", startInfo.ArgumentList.Select(RedactArgument))}");
        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Avvio processo fallito: {fileName}.");
        }

        Task<string>? stdout = captureOutput ? process.StandardOutput.ReadToEndAsync(cancellationToken) : null;
        Task<string>? stderr = captureOutput ? process.StandardError.ReadToEndAsync(cancellationToken) : null;
        await process.WaitForExitAsync(cancellationToken);

        if (stdout is not null)
        {
            LogMultiline("PROCESS_STDOUT", await stdout);
        }
        if (stderr is not null)
        {
            LogMultiline("PROCESS_STDERR", await stderr);
        }

        Log($"PROCESS_EXIT|file={fileName}|exitCode={process.ExitCode}");
        return process.ExitCode;
    }

    private async Task<int> RunSensitiveProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Log($"PROCESS_START|file={fileName}|arguments=***|sensitive=true");
        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Avvio processo fallito: {fileName}.");
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string capturedStdout = await stdout;
        string capturedStderr = await stderr;
        Log(
            $"PROCESS_SENSITIVE_OUTPUT_SUPPRESSED|stdoutChars={capturedStdout.Length}|stderrChars={capturedStderr.Length}");
        Log($"PROCESS_EXIT|file={fileName}|exitCode={process.ExitCode}");
        return process.ExitCode;
    }

    private async Task<int> RunProcessWithRawArgumentsAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? _mediaRoot
        };

        Log($"PROCESS_START|file={fileName}|arguments={arguments}");
        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Avvio processo fallito: {fileName}.");
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        LogMultiline("PROCESS_STDOUT", await stdout);
        LogMultiline("PROCESS_STDERR", await stderr);
        Log($"PROCESS_EXIT|file={fileName}|exitCode={process.ExitCode}");
        return process.ExitCode;
    }

    private static void EnsureSuccessfulExit(InstallerComponent component, int exitCode)
    {
        if (exitCode is 0 or 1641 or 3010)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{component.DisplayName} ha restituito il codice {exitCode}. Consultare il log installer.");
    }

    private string ResolveMediaPath(string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_mediaRoot, relativePath));
        string normalizedRoot = _mediaRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Percorso payload fuori dal media installer: {relativePath}.");
        }

        return fullPath;
    }

    private string GetCacheRoot(string componentId, string hash)
    {
        string shortHash = hash.Length >= 12 ? hash[..12] : hash;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pulsar",
            "QtisVision",
            "Installer",
            "Cache",
            $"{componentId}-{shortHash}");
    }

    private void Log(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}|{message}";
        _logWriter.WriteLine(line);
        LogMessage?.Invoke(line);
    }

    private void LogMultiline(string prefix, string value)
    {
        foreach (string line in value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            Log($"{prefix}|{line}");
        }
    }

    private void ReportProgress(int percent, string message)
    {
        ProgressChanged?.Invoke(Math.Clamp(percent, 0, 100), message);
        Log($"PROGRESS|percent={Math.Clamp(percent, 0, 100)}|message={message}");
    }

    private static ValidationFinding Block(string componentId, string message) => new()
    {
        ComponentId = componentId,
        Severity = "Error",
        Message = message,
        BlocksInstallation = true
    };

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        await using FileStream output = new(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            useAsync: true);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
    }

    private static void DeleteDirectoryUnder(string target, string allowedRoot)
    {
        string fullTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!fullTarget.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !fullTarget.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Rimozione rifiutata fuori dalla root consentita: {fullTarget}.");
        }

        Directory.Delete(fullTarget, recursive: true);
    }

    private static bool TryNormalizeVersion(string? raw, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string normalized = raw.Split([' ', '-', '+'], StringSplitOptions.RemoveEmptyEntries)[0];
        return Version.TryParse(normalized, out version!);
    }

    private static ComponentDetection DetectFromRegistry(InstallerComponent component)
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = baseKey.OpenSubKey(component.DetectionRegistryKey!, writable: false);
            if (key is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(component.DetectionRegistryPathValueName))
            {
                string? registeredPath = key
                    .GetValue(component.DetectionRegistryPathValueName)
                    ?.ToString();
                if (string.IsNullOrWhiteSpace(registeredPath) ||
                    (!File.Exists(Environment.ExpandEnvironmentVariables(registeredPath)) &&
                     !Directory.Exists(Environment.ExpandEnvironmentVariables(registeredPath))))
                {
                    continue;
                }
            }

            string valueName = string.IsNullOrWhiteSpace(component.DetectionRegistryValueName)
                ? "DisplayVersion"
                : component.DetectionRegistryValueName;
            string? rawVersion = key.GetValue(valueName)?.ToString();
            if (Version.TryParse(component.MinimumVersion, out Version? minimum) &&
                (!TryNormalizeVersion(rawVersion, out Version installed) || installed < minimum))
            {
                return ComponentDetection.InstalledOlder;
            }

            return ComponentDetection.Installed;
        }

        return ComponentDetection.Missing;
    }

    private static string FormatBytes(long bytes)
    {
        double gb = bytes / 1024d / 1024d / 1024d;
        return $"{gb:0.0} GB";
    }

    private static List<string> SplitArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        List<string> result = [];
        StringBuilder current = new();
        bool inQuotes = false;
        foreach (char character in arguments)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string RedactArgument(string value)
    {
        if (value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("passwd", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("pwd=", StringComparison.OrdinalIgnoreCase))
        {
            return "***";
        }

        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
