[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][string]$SettingsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$stateRoot = Join-Path $env:ProgramData 'Pulsar\QtisVision\Startup'
$logRoot = Join-Path $stateRoot 'Logs'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$logPath = Join-Path $logRoot 'startup.log'
if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt 2MB) {
    $archive = Join-Path $logRoot 'startup.previous.log'
    Move-Item -LiteralPath $logPath -Destination $archive -Force
}

function Write-StartupLog {
    param([Parameter(Mandatory = $true)][string]$Message)
    $line = '{0:yyyy-MM-dd HH:mm:ss.fff}|{1}' -f (Get-Date), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Limit-Integer {
    param(
        [Parameter(Mandatory = $true)][int]$Value,
        [Parameter(Mandatory = $true)][int]$Minimum,
        [Parameter(Mandatory = $true)][int]$Maximum
    )
    return [Math]::Min($Maximum, [Math]::Max($Minimum, $Value))
}

function Get-CameraAdapterNames {
    $networkResults = Join-Path $env:ProgramData 'Pulsar\QtisVision\Installer\NetworkResults'
    if (-not (Test-Path -LiteralPath $networkResults -PathType Container)) {
        return @()
    }

    $latest = Get-ChildItem -LiteralPath $networkResults -Filter 'cognex-gige-*.json' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latest) {
        return @()
    }

    try {
        $report = Get-Content -LiteralPath $latest.FullName -Raw | ConvertFrom-Json
        return @($report.configuredAdapters |
            Where-Object { $_.Status -in @('Configured', 'ConfiguredWithWarnings', 'Candidate') } |
            ForEach-Object { [string]$_.Adapter } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique)
    }
    catch {
        Write-StartupLog "STARTUP_NETWORK_REPORT_INVALID|path=$($latest.FullName)|error=$($_.Exception.Message)"
        return @()
    }
}

function Get-PendingDependencies {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$CameraAdapterNames,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$ServiceNamePatterns
    )

    $pending = [System.Collections.Generic.List[string]]::new()
    foreach ($servicePattern in $ServiceNamePatterns) {
        foreach ($service in @(Get-Service -Name $servicePattern -ErrorAction SilentlyContinue)) {
            $service.Refresh()
            if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
                $pending.Add("service:$($service.Name):$($service.Status)")
            }
        }
    }

    if ($CameraAdapterNames.Count -gt 0 -and $null -ne (Get-Command Get-NetAdapter -ErrorAction SilentlyContinue)) {
        foreach ($adapterName in $CameraAdapterNames) {
            $adapter = Get-NetAdapter -Name $adapterName -ErrorAction SilentlyContinue
            if ($null -eq $adapter -or $adapter.Status -ne 'Up') {
                $status = if ($null -eq $adapter) { 'Missing' } else { [string]$adapter.Status }
                $pending.Add("camera-nic:${adapterName}:${status}")
            }
        }
    }

    return @($pending)
}

try {
    $settings = [pscustomobject]@{
        initialDelaySeconds = 45
        dependencyWaitSeconds = 120
        pollIntervalSeconds = 2
        launchWhenDependenciesTimeout = $true
        serviceNamePatterns = @('MySQL*')
    }
    if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
        $loaded = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
        if ($loaded.PSObject.Properties.Name -contains 'initialDelaySeconds') { $settings.initialDelaySeconds = [int]$loaded.initialDelaySeconds }
        if ($loaded.PSObject.Properties.Name -contains 'dependencyWaitSeconds') { $settings.dependencyWaitSeconds = [int]$loaded.dependencyWaitSeconds }
        if ($loaded.PSObject.Properties.Name -contains 'pollIntervalSeconds') { $settings.pollIntervalSeconds = [int]$loaded.pollIntervalSeconds }
        if ($loaded.PSObject.Properties.Name -contains 'launchWhenDependenciesTimeout') { $settings.launchWhenDependenciesTimeout = [bool]$loaded.launchWhenDependenciesTimeout }
        if ($loaded.PSObject.Properties.Name -contains 'serviceNamePatterns') {
            $settings.serviceNamePatterns = @($loaded.serviceNamePatterns |
                ForEach-Object { [string]$_ } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -Unique)
        }
    }

    $initialDelay = Limit-Integer -Value $settings.initialDelaySeconds -Minimum 0 -Maximum 600
    $dependencyWait = Limit-Integer -Value $settings.dependencyWaitSeconds -Minimum 0 -Maximum 600
    $pollInterval = Limit-Integer -Value $settings.pollIntervalSeconds -Minimum 1 -Maximum 30
    $servicePatterns = @($settings.serviceNamePatterns)
    $executable = Join-Path ([System.IO.Path]::GetFullPath($InstallRoot)) 'bin\QtisVisionPanel.exe'
    $workingDirectory = Split-Path -Parent $executable

    Write-StartupLog "STARTUP_BEGIN|initialDelay=${initialDelay}s|dependencyWait=${dependencyWait}s|exe=$executable"
    if (@(Get-Process -Name 'QtisVisionPanel' -ErrorAction SilentlyContinue).Count -gt 0) {
        Write-StartupLog 'STARTUP_SKIPPED_ALREADY_RUNNING'
        exit 0
    }

    if ($initialDelay -gt 0) {
        Start-Sleep -Seconds $initialDelay
    }

    $cameraAdapters = @(Get-CameraAdapterNames)
    $deadline = [DateTime]::UtcNow.AddSeconds($dependencyWait)
    $pending = @(Get-PendingDependencies -CameraAdapterNames $cameraAdapters -ServiceNamePatterns $servicePatterns)
    while ($pending.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds $pollInterval
        $pending = @(Get-PendingDependencies -CameraAdapterNames $cameraAdapters -ServiceNamePatterns $servicePatterns)
    }

    if ($pending.Count -gt 0) {
        Write-StartupLog "STARTUP_DEPENDENCY_TIMEOUT|pending=$($pending -join ',')"
        if (-not $settings.launchWhenDependenciesTimeout) {
            Write-StartupLog 'STARTUP_ABORTED_BY_POLICY'
            exit 3
        }
    }
    else {
        Write-StartupLog "STARTUP_DEPENDENCIES_READY|cameraAdapters=$($cameraAdapters -join ',')"
    }

    if (@(Get-Process -Name 'QtisVisionPanel' -ErrorAction SilentlyContinue).Count -gt 0) {
        Write-StartupLog 'STARTUP_SKIPPED_ALREADY_RUNNING_AFTER_WAIT'
        exit 0
    }
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        Write-StartupLog "STARTUP_EXECUTABLE_MISSING|path=$executable"
        exit 2
    }

    $process = Start-Process -FilePath $executable -WorkingDirectory $workingDirectory -PassThru
    Write-StartupLog "STARTUP_LAUNCHED|pid=$($process.Id)|dependencyTimeout=$($pending.Count -gt 0)"
    exit 0
}
catch {
    Write-StartupLog "STARTUP_FAILED|error=$($_.Exception.ToString())"
    exit 1
}
