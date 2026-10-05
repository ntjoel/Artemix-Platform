param(
    [string]$SourcePath = "D:\Pulsar\Developer\NewPanel\QtisVisionPanel",
    [string]$TargetPath = "C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\lastRelease\QtisVisionPanel",
    [switch]$Apply,
    [switch]$Mirror,
    [switch]$VerifyCopiedFiles
)

$ErrorActionPreference = "Stop"

function Resolve-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path).TrimEnd('\')
}

function Assert-RepoRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label folder not found: $Path"
    }

    $projectPath = Join-Path $Path "QtisVisionPanel.csproj"
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "$Label folder does not look like a QtisVisionPanel root: $Path"
    }
}

Assert-RepoRoot -Path $SourcePath -Label "Source"
Assert-RepoRoot -Path $TargetPath -Label "Target"

$sourceFullPath = Resolve-NormalizedPath -Path $SourcePath
$targetFullPath = Resolve-NormalizedPath -Path $TargetPath

if ($sourceFullPath -eq $targetFullPath) {
    throw "Source and target cannot be the same folder."
}

$excludedDirectories = @(
    ".git",
    ".vs",
    ".vscode",
    ".claude",
    ".codex",
    ".codex-temp",
    ".backups",
    ".tmp",
    "__pycache__",
    "_buildcheck",
    "_buildcheck2",
    "_reviewbuild",
    "_audit_build",
    "bin",
    "obj",
    "packages"
)

$excludedFiles = @(
    "*.suo",
    "*.user",
    "*.cache",
    "*.pdb",
    "*.pyc",
    "*.exe",
    "*.dll",
    "*.baml",
    "*.g.cs",
    "*.g.i.cs",
    "*.resources",
    "*.tmp",
    "*.bak",
    "*.log"
)

$commonArguments = @(
    $sourceFullPath,
    $targetFullPath,
    "/E",
    "/R:0",
    "/W:0",
    "/NP",
    "/XD"
) + $excludedDirectories + @(
    "/XF"
) + $excludedFiles

function Test-ExcludedRelativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $parts = $RelativePath -split '[\\/]'
    foreach ($directory in $excludedDirectories) {
        if ($parts -contains $directory) {
            return $true
        }
    }

    $fileName = Split-Path -Leaf $RelativePath
    foreach ($pattern in $excludedFiles) {
        if ($fileName -like $pattern) {
            return $true
        }
    }

    return $false
}

function Assert-RobocopySuccess {
    param([int]$ExitCode)

    # Robocopy codes 0..7 are successful states. 1 means files were copied.
    if ($ExitCode -ge 8) {
        throw "Robocopy failed with exit code $ExitCode."
    }
}

function Test-SharedBaselineAlignment {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$TargetRoot
    )

    $mismatches = New-Object System.Collections.Generic.List[string]
    $sourceRootWithSeparator = $SourceRoot.TrimEnd('\') + '\'

    Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($sourceRootWithSeparator.Length)
        if (Test-ExcludedRelativePath -RelativePath $relativePath) {
            return
        }

        $targetFile = Join-Path $TargetRoot $relativePath
        if (-not (Test-Path -LiteralPath $targetFile -PathType Leaf)) {
            $mismatches.Add("missing:$relativePath")
            return
        }

        $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
        $targetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetFile).Hash
        if ($sourceHash -ne $targetHash) {
            $mismatches.Add("different:$relativePath")
        }
    }

    if ($mismatches.Count -gt 0) {
        $preview = $mismatches | Select-Object -First 20
        throw "Shared baseline verification failed. First mismatches:`n$($preview -join "`n")"
    }

    Write-Host "Shared baseline verification OK." -ForegroundColor Green
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "Preview mode: no file will be copied." -ForegroundColor Cyan
    Write-Host "Source: $sourceFullPath"
    Write-Host "Target: $targetFullPath"
    Write-Host ""

    & robocopy @commonArguments "/L" "/NJH" "/NJS"
    $robocopyExitCode = $LASTEXITCODE
    Assert-RobocopySuccess -ExitCode $robocopyExitCode
    exit 0
}

$modeLabel = if ($Mirror) { "APPLY + MIRROR" } else { "APPLY (copy/update only)" }
Write-Host ""
Write-Host "$modeLabel" -ForegroundColor Yellow
Write-Host "Source: $sourceFullPath"
Write-Host "Target: $targetFullPath"
Write-Host ""

$applyArguments = @($commonArguments)
if ($Mirror) {
    $applyArguments += "/MIR"
}

& robocopy @applyArguments
$robocopyExitCode = $LASTEXITCODE
Assert-RobocopySuccess -ExitCode $robocopyExitCode

if ($VerifyCopiedFiles) {
    Test-SharedBaselineAlignment -SourceRoot $sourceFullPath -TargetRoot $targetFullPath
}

exit 0
