param(
    [ValidateSet("Major", "Minor", "Build", "Revision")]
    [string]$Part = "Revision",

    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$assemblyInfoPath = Join-Path $repoRoot "Properties\AssemblyInfo.cs"
$versionArchivePath = Join-Path $repoRoot "Documentation\MachineHardware\software-version-archive.md"

if (-not (Test-Path $assemblyInfoPath)) {
    throw "AssemblyInfo not found: $assemblyInfoPath"
}

if (-not (Test-Path $versionArchivePath)) {
    throw "Version archive not found: $versionArchivePath"
}

function Get-CurrentVersion {
    param([string]$AssemblyInfoContent)

    $match = [regex]::Match($AssemblyInfoContent, 'AssemblyVersion\("(?<version>\d+\.\d+\.\d+\.\d+)"\)')
    if (-not $match.Success) {
        throw "Unable to read AssemblyVersion from AssemblyInfo.cs"
    }

    return [Version]$match.Groups["version"].Value
}

function Get-NextVersion {
    param(
        [Version]$CurrentVersion,
        [string]$RequestedPart,
        [string]$ExplicitVersion
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitVersion)) {
        return [Version]$ExplicitVersion
    }

    function Normalize-Version {
        param(
            [int]$Major,
            [int]$Minor,
            [int]$Build,
            [int]$Revision
        )

        while ($Revision -ge 10) {
            $Revision -= 10
            $Build += 1
        }

        while ($Build -ge 10) {
            $Build -= 10
            $Minor += 1
        }

        while ($Minor -ge 10) {
            $Minor -= 10
            $Major += 1
        }

        return [Version]::new($Major, $Minor, $Build, $Revision)
    }

    $major = $CurrentVersion.Major
    $minor = $CurrentVersion.Minor
    $build = $CurrentVersion.Build
    $revision = $CurrentVersion.Revision

    switch ($RequestedPart) {
        "Major" {
            $major += 1
            $minor = 0
            $build = 0
            $revision = 0
        }
        "Minor" {
            $minor += 1
            $build = 0
            $revision = 0
        }
        "Build" {
            $build += 1
            $revision = 0
        }
        "Revision" {
            $revision += 1
        }
        default    { throw "Unsupported increment part: $RequestedPart" }
    }

    return Normalize-Version -Major $major -Minor $minor -Build $build -Revision $revision
}

function Update-AssemblyInfo {
    param(
        [string]$Path,
        [string]$CurrentVersion,
        [string]$NewVersion
    )

    $content = Get-Content $Path -Raw
    $content = $content.Replace("AssemblyVersion(`"$CurrentVersion`")", "AssemblyVersion(`"$NewVersion`")")
    $content = $content.Replace("AssemblyFileVersion(`"$CurrentVersion`")", "AssemblyFileVersion(`"$NewVersion`")")
    $content = $content.Replace("AssemblyInformationalVersion(`"$CurrentVersion`")", "AssemblyInformationalVersion(`"$NewVersion`")")
    Set-Content -Path $Path -Value $content -Encoding UTF8
}

function Update-VersionArchive {
    param(
        [string]$Path,
        [string]$CurrentVersion,
        [string]$NewVersion
    )

    $today = (Get-Date).ToString("yyyy-MM-dd")
    $content = Get-Content $Path -Raw
    $replacement = '## Current Official Version' +
        "`r`n`r`n" +
        '- `' + $NewVersion + '`' +
        "`r`n" +
        '- release baseline date: `' + $today + '`'

    $content = [regex]::Replace(
        $content,
        '## Current Official Version\s*\r?\n\r?\n- `(?<version>\d+\.\d+\.\d+\.\d+)`\r?\n- release baseline date: `(?<date>[^`]+)`(\r?\n- baseline type: `(?<baseline>[^`]+)`)?',
        $replacement,
        1)

    if ($content -notmatch [regex]::Escape("## Version $NewVersion")) {
        $template = @"

## Version $NewVersion

### Release metadata

- release date: ``$today``

### Release intent

Incremental release generated after a software update on $today.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed `Config.xml` keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers
"@

        $legacyIndex = $content.IndexOf("## Legacy Baseline")
        if ($legacyIndex -ge 0) {
            $content = $content.Insert($legacyIndex, $template + "`r`n")
        }
        else {
            $content += $template
        }
    }

    Set-Content -Path $Path -Value $content -Encoding UTF8
}

$assemblyInfoContent = Get-Content $assemblyInfoPath -Raw
$currentVersion = Get-CurrentVersion -AssemblyInfoContent $assemblyInfoContent
$newVersion = Get-NextVersion -CurrentVersion $currentVersion -RequestedPart $Part -ExplicitVersion $Version

Update-AssemblyInfo -Path $assemblyInfoPath -CurrentVersion $currentVersion.ToString() -NewVersion $newVersion.ToString()
Update-VersionArchive -Path $versionArchivePath -CurrentVersion $currentVersion.ToString() -NewVersion $newVersion.ToString()

Write-Host "Software release updated: $($currentVersion.ToString()) -> $($newVersion.ToString())"
