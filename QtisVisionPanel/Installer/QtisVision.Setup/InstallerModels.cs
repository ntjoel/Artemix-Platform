using System.Text.Json.Serialization;

namespace QtisVision.Setup;

internal sealed class InstallerManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string ProductName { get; set; } = "Qtis Vision Panel";
    public string ProductVersion { get; set; } = "0.0.0.0";
    public int MediaRevision { get; set; } = 1;
    public string PackageType { get; set; } = "Full";
    public string InstallRoot { get; set; } = @"C:\QtisVision";
    public long RequiredFreeSpaceBytes { get; set; }
    public bool ContainsMachineSpecificConfiguration { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<InstallerComponent> Components { get; set; } = [];

    [JsonIgnore]
    public bool IsUpdateOnly => string.Equals(
        PackageType,
        "UpdateOnly",
        StringComparison.OrdinalIgnoreCase);
}

internal sealed class InstallerComponent
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool DefaultSelected { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public bool Interactive { get; set; }
    public bool ConfigureWhenInstalled { get; set; }
    public string? BlockingReason { get; set; }
    public string? DetectionPath { get; set; }
    public string? DetectionRegistryKey { get; set; }
    public string? DetectionRegistryValueName { get; set; } = "DisplayVersion";
    public string? DetectionRegistryPathValueName { get; set; }
    public string? DetectionSearchFileName { get; set; }
    public List<string> DetectionSearchRoots { get; set; } = [];
    public string? MinimumVersion { get; set; }
    public string? EntryPoint { get; set; }
    public string? InstallArguments { get; set; }
    public List<string> DependsOn { get; set; } = [];
    public List<InstallerPayload> Payloads { get; set; } = [];

    [JsonIgnore]
    public ComponentDetection Detection { get; set; } = ComponentDetection.Unknown;
}

internal sealed class InstallerPayload
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

internal enum ComponentDetection
{
    Unknown,
    Missing,
    Installed,
    InstalledOlder,
    Invalid
}

internal sealed class ValidationFinding
{
    public string ComponentId { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info";
    public string Message { get; init; } = string.Empty;
    public bool BlocksInstallation { get; init; }
}

internal sealed class ValidationReport
{
    public string ProductVersion { get; init; } = string.Empty;
    public bool IsValid => Findings.All(item => !item.BlocksInstallation);
    public List<ValidationFinding> Findings { get; init; } = [];
}

internal sealed class InstallState
{
    public string ProductVersion { get; set; } = string.Empty;
    public int MediaRevision { get; set; }
    public DateTime InstalledAtUtc { get; set; }
    public string InstallerMediaPath { get; set; } = string.Empty;
    public string? PreviousBinBackup { get; set; }
    public string? VisionProDependenciesTarget { get; set; }
    public List<string> InstalledComponents { get; set; } = [];
    public string InstallMode { get; set; } = "Fresh";
    public string PackageType { get; set; } = "Full";
    public string? PreviousProductVersion { get; set; }
    public int ApplicationFilesAdded { get; set; }
    public int ApplicationFilesUpdated { get; set; }
    public int ApplicationFilesRemoved { get; set; }
    public int ApplicationFilesUnchanged { get; set; }
}

internal sealed class CoreInstallationInfo
{
    public string TargetVersion { get; init; } = string.Empty;
    public string? InstalledVersion { get; init; }
    public string Mode { get; init; } = "Fresh";
    public bool IsDowngradeBlocked { get; init; }

    public string DisplayText => Mode switch
    {
        "Upgrade" => $"Aggiorna {InstalledVersion} -> {TargetVersion}",
        "Repair" => $"Ripara {TargetVersion}",
        "DowngradeBlocked" => $"Bloccato: {InstalledVersion} > {TargetVersion}",
        _ => $"Nuova installazione {TargetVersion}"
    };
}

internal sealed class ManagedApplicationManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string ProductVersion { get; set; } = string.Empty;
    public int MediaRevision { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<ManagedApplicationFile> Files { get; set; } = [];
}

internal sealed class ManagedApplicationFile
{
    public string RelativePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class CoreDifferentialPlan
{
    public ManagedApplicationManifest TargetManifest { get; init; } = new();
    public bool PreviousManifestTrusted { get; init; }
    public List<string> AddedFiles { get; init; } = [];
    public List<string> UpdatedFiles { get; init; } = [];
    public List<string> RemovedFiles { get; init; } = [];
    public int UnchangedFiles { get; init; }
}

internal sealed class CoreUpdateJournal
{
    public int SchemaVersion { get; set; } = 1;
    public string Status { get; set; } = "Prepared";
    public DateTime CreatedAtUtc { get; set; }
    public string InstallRoot { get; set; } = string.Empty;
    public string DestinationBin { get; set; } = string.Empty;
    public string BackupRoot { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public string? InstalledVersion { get; set; }
    public bool HadInstallState { get; set; }
    public bool HadApplicationManifest { get; set; }
    public List<string> AddedFiles { get; set; } = [];
    public List<string> UpdatedFiles { get; set; } = [];
    public List<string> RemovedFiles { get; set; } = [];
}

internal sealed class HardwareInventory
{
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BaseBoardManufacturer { get; set; } = string.Empty;
    public string BaseBoardProduct { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public string ProcessorName { get; set; } = string.Empty;
    public bool ModelIsGeneric { get; set; }
    public string Source { get; set; } = "Windows SMBIOS registry";
}
