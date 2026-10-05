[CmdletBinding()]
param(
    [string]$RuntimeRoot = 'C:\QtisVision',
    [string]$ReleaseDirectory,
    [string]$PayloadRoot = 'D:\QtisInstallerPayloads',
    [string]$OutputRoot = 'D:\QtisInstallerOutput',
    [int]$MediaRevision = 19,
    [switch]$RedactSecrets,
    [switch]$KeepStaging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $PSScriptRoot '..\bin\x64\Release'
}

function Resolve-ExistingDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description non trovata: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Resolve-ExistingFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description non trovato: $Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Remove-SafeDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Target)) {
        return
    }

    $fullTarget = [System.IO.Path]::GetFullPath($Target).TrimEnd('\')
    $fullRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    if ($fullTarget -eq $fullRoot -or
        -not $fullTarget.StartsWith($fullRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Rimozione rifiutata fuori dalla cartella di output: $fullTarget"
    }

    Remove-Item -LiteralPath $fullTarget -Recurse -Force
}

function Invoke-RobocopyChecked {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string[]]$ExtraArguments = @()
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $arguments = @(
        $Source,
        $Destination,
        '*.*',
        '/E',
        '/COPY:DAT',
        '/DCOPY:DAT',
        '/R:2',
        '/W:1',
        '/XJ',
        '/NFL',
        '/NDL',
        '/NJH',
        '/NJS',
        '/NP'
    ) + $ExtraArguments

    & robocopy.exe @arguments | Out-Host
    if ($LASTEXITCODE -gt 7) {
        throw "Robocopy fallito con codice $LASTEXITCODE. Sorgente: $Source"
    }
}

function Assert-ValidSignature {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "$Description non ha una firma Authenticode valida: $($signature.Status). File: $Path"
    }
}

function Assert-ExpectedSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not $actual.Equals($Expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description non corrisponde all'hash approvato. Atteso $Expected, trovato $actual."
    }
}

function Redact-XmlSecrets {
    param([Parameter(Mandatory = $true)][string]$Root)

    $secretPattern = '(?i)(password|passwd|pwd|secret|token|privatekey)'
    Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.xml' | ForEach-Object {
        $xmlPath = $_.FullName
        try {
            $document = New-Object System.Xml.XmlDocument
            $document.PreserveWhitespace = $true
            $document.Load($xmlPath)
            $changed = $false
            foreach ($node in $document.SelectNodes('//*')) {
                if ($node.Name -match $secretPattern -and $node.ChildNodes.Count -le 1) {
                    $node.InnerText = ''
                    $changed = $true
                }
            }
            if ($changed) {
                $document.Save($xmlPath)
            }
        }
        catch {
            Write-Warning "File XML non modificato durante la redazione: $xmlPath. $($_.Exception.Message)"
        }
    }
}

function New-ZipArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $Source,
        $Destination,
        [System.IO.Compression.CompressionLevel]::Fastest,
        $false)
}

function Get-MediaRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$MediaRoot,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $normalizedRoot = [System.IO.Path]::GetFullPath($MediaRoot).TrimEnd('\') + '\'
    $normalizedPath = [System.IO.Path]::GetFullPath($FullPath)
    if (-not $normalizedPath.StartsWith($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Il payload non e' sotto la root del media: $normalizedPath"
    }

    return $normalizedPath.Substring($normalizedRoot.Length)
}

function New-PayloadDescriptor {
    param(
        [Parameter(Mandatory = $true)][string]$MediaRoot,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $item = Get-Item -LiteralPath $FullPath
    return [ordered]@{
        relativePath = Get-MediaRelativePath -MediaRoot $MediaRoot -FullPath $item.FullName
        sha256      = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        sizeBytes   = $item.Length
    }
}

function Copy-Payload {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    return (Get-Item -LiteralPath $Destination).FullName
}

function Test-GeneratedManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$MediaRoot
    )

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.packageType -eq 'UpdateOnly') {
        $components = @($manifest.components)
        if ($components.Count -ne 1 -or [string]$components[0].id -ne 'qtis-core') {
            throw 'Manifest UpdateOnly non valido: deve contenere esclusivamente il componente qtis-core.'
        }

        $payloads = @($components[0].payloads)
        if ($payloads.Count -ne 1 -or
            ([string]$payloads[0].relativePath).IndexOf('ApplicationBin', [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
            @($payloads | Where-Object { ([string]$_.relativePath).IndexOf('RuntimeSeed', [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -ne 0 -or
            [bool]$manifest.containsMachineSpecificConfiguration) {
            throw 'Manifest UpdateOnly non valido: e consentito solo ApplicationBin, senza RuntimeSeed o configurazioni macchina.'
        }
    }

    $checked = 0
    foreach ($component in $manifest.components) {
        foreach ($payload in $component.payloads) {
            $path = Join-Path $MediaRoot $payload.relativePath
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Payload mancante dopo la generazione: $($payload.relativePath)"
            }

            $item = Get-Item -LiteralPath $path
            if ([Int64]$payload.sizeBytes -ne $item.Length) {
                throw "Dimensione payload errata: $($payload.relativePath)"
            }

            $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            if (-not $actualHash.Equals([string]$payload.sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Hash payload errato: $($payload.relativePath)"
            }
            $checked++
        }
    }

    return $checked
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runtimeRootResolved = Resolve-ExistingDirectory -Path $RuntimeRoot -Description 'Cartella runtime QtisVision'
$releaseResolved = Resolve-ExistingDirectory -Path $ReleaseDirectory -Description 'Cartella bin x64 Release'
$payloadRootResolved = Resolve-ExistingDirectory -Path $PayloadRoot -Description 'Cartella payload offline'

$applicationExe = Resolve-ExistingFile `
    -Path (Join-Path $releaseResolved 'QtisVisionPanel.exe') `
    -Description 'QtisVisionPanel.exe Release'
$onnxWorkerExe = Resolve-ExistingFile `
    -Path (Join-Path $releaseResolved 'AiRuntime\QtisVisionPanel.OnnxWorker.exe') `
    -Description 'Worker ONNX isolato'
$null = Resolve-ExistingFile `
    -Path (Join-Path $releaseResolved 'AiRuntime\Microsoft.ML.OnnxRuntime.dll') `
    -Description 'Runtime ONNX managed del worker'
$null = Resolve-ExistingFile `
    -Path (Join-Path $releaseResolved 'AiRuntime\onnxruntime.dll') `
    -Description 'Runtime ONNX nativo del worker'

$forbiddenRootOnnxFiles = @(
    @(
        'Microsoft.ML.OnnxRuntime.dll',
        'onnxruntime.dll',
        'onnxruntime_providers_shared.dll'
    ) | ForEach-Object { Join-Path $releaseResolved $_ } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
)

if ($forbiddenRootOnnxFiles.Count -gt 0) {
    throw "Runtime ONNX Microsoft trovato accanto alla HMI. Questo rompe la deserializzazione Cognex ViDi EL. I file devono restare solo in AiRuntime: $($forbiddenRootOnnxFiles -join ', ')"
}
$forbiddenLocalCognexStartup = Join-Path $releaseResolved 'Cognex.Vision.Startup.Net.dll'
if (Test-Path -LiteralPath $forbiddenLocalCognexStartup -PathType Leaf) {
    throw "Cognex.Vision.Startup.Net.dll non deve essere copiata accanto alla HMI. Deve essere risolta tramite VisionProDependencies insieme alle dipendenze native RBBT/ViDi EL: $forbiddenLocalCognexStartup"
}
$visionProDependenciesSource = Resolve-ExistingDirectory `
    -Path (Join-Path $releaseResolved 'VisionProDependencies') `
    -Description 'Collegamento VisionProDependencies della Release'
$null = Resolve-ExistingFile `
    -Path (Join-Path $visionProDependenciesSource 'Cognex.Vision.Startup.Net.dll') `
    -Description 'DLL sentinella VisionProDependencies'
$visionProDependenciesItem = Get-Item -LiteralPath (Join-Path $releaseResolved 'VisionProDependencies') -Force
if (($visionProDependenciesItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
    Write-Warning 'VisionProDependencies non e'' un reparse point. Il contenuto non sara'' duplicato nel media; il setup ricreera'' la junction sul target.'
}
else {
    Write-Host "VisionProDependencies: $($visionProDependenciesItem.Target -join ', ')" -ForegroundColor DarkGray
}
$productVersion = (Get-Item -LiteralPath $applicationExe).VersionInfo.ProductVersion
$parsedProductVersion = $null
if ([string]::IsNullOrWhiteSpace($productVersion) -or
    -not [Version]::TryParse($productVersion, [ref]$parsedProductVersion)) {
    throw "Versione prodotto non valida in ${applicationExe}: '$productVersion'"
}
if ($MediaRevision -lt 1) {
    throw 'MediaRevision deve essere maggiore o uguale a 1.'
}

$outputRootFull = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
$mediaRoot = Join-Path $outputRootFull "QtisVisionPanel_$productVersion-r$MediaRevision"
$updateMediaRoot = Join-Path $outputRootFull "QtisVisionPanel_Update_$productVersion-r$MediaRevision"
$stagingRoot = Join-Path $outputRootFull "_staging_$productVersion-r$MediaRevision"
Remove-SafeDirectory -Target $mediaRoot -AllowedRoot $outputRootFull
Remove-SafeDirectory -Target $updateMediaRoot -AllowedRoot $outputRootFull
Remove-SafeDirectory -Target $stagingRoot -AllowedRoot $outputRootFull
New-Item -ItemType Directory -Path $mediaRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

Write-Host "Qtis Vision installer media $productVersion-r$MediaRevision" -ForegroundColor Cyan
Write-Host "Runtime: $runtimeRootResolved"
Write-Host "Release: $releaseResolved"
Write-Host "Payload: $payloadRootResolved"
Write-Host "Output:  $mediaRoot"

try {
    $stagedRuntime = Join-Path $stagingRoot 'RuntimeSeed'
    $stagedBin = Join-Path $stagingRoot 'ApplicationBin'

    Write-Host '1/7 - Copia runtime seed...' -ForegroundColor Cyan
    Invoke-RobocopyChecked `
        -Source $runtimeRootResolved `
        -Destination $stagedRuntime `
        -ExtraArguments @('/XD', (Join-Path $runtimeRootResolved 'bin'), (Join-Path $runtimeRootResolved 'InstallerBackups'))

    if ($RedactSecrets) {
        Write-Host 'Redazione credenziali XML dal seed...' -ForegroundColor Yellow
        Redact-XmlSecrets -Root $stagedRuntime
    }

    Write-Host '2/7 - Copia binari Release...' -ForegroundColor Cyan
    Invoke-RobocopyChecked `
        -Source $releaseResolved `
        -Destination $stagedBin `
        -ExtraArguments @(
            '/XF', '*.pdb', '*.lib',
            '/XD',
            (Join-Path $releaseResolved 'VisionProDependencies'),
            (Join-Path $releaseResolved 'QtisVisionPanel.exe.WebView2')
        )

    $junctions = Get-ChildItem -LiteralPath $stagedBin -Recurse -Force |
        Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 }
    if ($junctions) {
        throw "Sono rimasti reparse point nel payload bin: $($junctions.FullName -join ', ')"
    }

    $stagedWebViewCache = Join-Path $stagedBin 'QtisVisionPanel.exe.WebView2'
    if (Test-Path -LiteralPath $stagedWebViewCache) {
        throw "Il payload applicativo contiene la cache WebView2 runtime, che deve restare macchina-specifica: $stagedWebViewCache"
    }

    $null = Resolve-ExistingFile `
        -Path (Join-Path $stagedBin 'AiRuntime\QtisVisionPanel.OnnxWorker.exe') `
        -Description 'Worker ONNX nel payload applicativo'

    $stagedRootOnnxFiles = @(
        @(
            'Microsoft.ML.OnnxRuntime.dll',
            'onnxruntime.dll',
            'onnxruntime_providers_shared.dll'
        ) | ForEach-Object { Join-Path $stagedBin $_ } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    )

    if ($stagedRootOnnxFiles.Count -gt 0) {
        throw "Il payload applicativo contiene runtime ONNX Microsoft accanto alla HMI: $($stagedRootOnnxFiles -join ', ')"
    }

    $stagedLocalCognexStartup = Join-Path $stagedBin 'Cognex.Vision.Startup.Net.dll'
    if (Test-Path -LiteralPath $stagedLocalCognexStartup -PathType Leaf) {
        throw "Il payload applicativo contiene Cognex.Vision.Startup.Net.dll accanto alla HMI. La DLL deve provenire soltanto dalla junction VisionProDependencies: $stagedLocalCognexStartup"
    }

    Write-Host '3/7 - Creazione archivi core...' -ForegroundColor Cyan
    $coreMedia = Join-Path $mediaRoot 'Payloads\Core'
    New-Item -ItemType Directory -Path $coreMedia -Force | Out-Null
    $applicationArchive = Join-Path $coreMedia "ApplicationBin-$productVersion.zip"
    $runtimeArchive = Join-Path $coreMedia "RuntimeSeed-$productVersion.zip"
    New-ZipArchive -Source $stagedBin -Destination $applicationArchive
    New-ZipArchive -Source $stagedRuntime -Destination $runtimeArchive

    Write-Host '4/7 - Verifica e copia prerequisiti offline...' -ForegroundColor Cyan
    $visionProSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'VisionPro\VisionPro_9_25_64-bit.zip') `
        -Description 'VisionPro 9.25 x64'
    Assert-ExpectedSha256 `
        -Path $visionProSource `
        -Expected '8A1B97CFEC84B4511412195E658CA0076C54B69B65A20632BE778A31BC9EB5A9' `
        -Description 'VisionPro 9.25 x64'
    $visionPatchZipSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'VisionPro\VisionPro WinUpdate KB5077181 Patch.zip') `
        -Description 'VisionPro Windows Update KB5077181 Patch'
    Assert-ExpectedSha256 `
        -Path $visionPatchZipSource `
        -Expected '1E766579810017201E8FC9A931C0D683CBD1DBA333284EA2D4E6E36BA00E2F13' `
        -Description 'VisionPro Windows Update KB5077181 Patch ZIP'

    $visionPatchExtractRoot = Join-Path $stagingRoot 'VisionProKB5077181'
    New-Item -ItemType Directory -Path $visionPatchExtractRoot -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($visionPatchZipSource, $visionPatchExtractRoot)
    $visionPatchExeSource = Resolve-ExistingFile `
        -Path (Join-Path $visionPatchExtractRoot 'Setup Cognex VisionPro Windows Update KB5077181 Patch.exe') `
        -Description 'Setup interno VisionPro KB5077181'
    Assert-ExpectedSha256 `
        -Path $visionPatchExeSource `
        -Expected '774BFA603A4ED4AB16E6C27B36D1853DB1C0A9528EDA39CC18D1BC0AEE0731F9' `
        -Description 'Setup interno VisionPro KB5077181'

    $daqSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'Advantech_DAQNavi\XNavi.exe') `
        -Description 'Advantech XNavi'
    $mysqlSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'MySQL\mysql-installer-community-8.0.46.0.msi') `
        -Description 'MySQL Installer Community 8.0.46'
    $heidiSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'MySQL\HeidiSQL_12.11.0.7065_Setup.exe') `
        -Description 'HeidiSQL'
    $pythonSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'Python\python-3.13.14-amd64.exe') `
        -Description 'Python 3.13.14 x64'
    $ultraVncSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'Tools\UltraVNC_1640_x64_Setup.exe') `
        -Description 'UltraVNC 1.6.4.0 x64'
    $notepadPlusPlusSource = Resolve-ExistingFile `
        -Path (Join-Path $payloadRootResolved 'Tools\npp.8.9.3.Installer.x64.exe') `
        -Description 'Notepad++ 8.9.3 x64'

    Assert-ExpectedSha256 `
        -Path $ultraVncSource `
        -Expected '434853E116EEB132CFDF47FDF6BA489D30C67A38147AFF6B9BD0EC2F4D0F1919' `
        -Description 'UltraVNC 1.6.4.0 x64'
    Assert-ExpectedSha256 `
        -Path $notepadPlusPlusSource `
        -Expected '8DB87A458551371E911BEA5243840C783B5E7D41B7F867A2A6F74971881FB1E4' `
        -Description 'Notepad++ 8.9.3 x64'

    Assert-ValidSignature -Path $daqSource -Description 'Advantech XNavi'
    Assert-ValidSignature -Path $mysqlSource -Description 'MySQL Installer Community 8.0.46'
    Assert-ValidSignature -Path $heidiSource -Description 'HeidiSQL'
    Assert-ValidSignature -Path $pythonSource -Description 'Python 3.13.14 x64'
    Assert-ValidSignature -Path $ultraVncSource -Description 'UltraVNC 1.6.4.0 x64'
    Assert-ValidSignature -Path $notepadPlusPlusSource -Description 'Notepad++ 8.9.3 x64'

    $visionProMedia = Copy-Payload `
        -Source $visionProSource `
        -Destination (Join-Path $mediaRoot 'Payloads\VisionPro\VisionPro_9_25_64-bit.zip')
    $visionPatchMedia = Copy-Payload `
        -Source $visionPatchExeSource `
        -Destination (Join-Path $mediaRoot 'Payloads\VisionPro\KB5077181\Setup Cognex VisionPro Windows Update KB5077181 Patch.exe')
    $daqMedia = Copy-Payload `
        -Source $daqSource `
        -Destination (Join-Path $mediaRoot 'Payloads\Advantech\XNavi.exe')
    $mysqlMedia = Copy-Payload `
        -Source $mysqlSource `
        -Destination (Join-Path $mediaRoot 'Payloads\MySQL\mysql-installer-community-8.0.46.0.msi')
    $heidiMedia = Copy-Payload `
        -Source $heidiSource `
        -Destination (Join-Path $mediaRoot 'Payloads\MySQL\HeidiSQL_12.11.0.7065_Setup.exe')
    $pythonMedia = Copy-Payload `
        -Source $pythonSource `
        -Destination (Join-Path $mediaRoot 'Payloads\Python\python-3.13.14-amd64.exe')
    $ultraVncMedia = Copy-Payload `
        -Source $ultraVncSource `
        -Destination (Join-Path $mediaRoot 'Payloads\Tools\UltraVNC_1640_x64_Setup.exe')
    $notepadPlusPlusMedia = Copy-Payload `
        -Source $notepadPlusPlusSource `
        -Destination (Join-Path $mediaRoot 'Payloads\Tools\npp.8.9.3.Installer.x64.exe')

    $requirementsMedia = Copy-Payload `
        -Source (Join-Path $PSScriptRoot 'requirements-ai.txt') `
        -Destination (Join-Path $mediaRoot 'Payloads\Python\requirements-ai.txt')
    $smokeTestMedia = Copy-Payload `
        -Source (Join-Path $PSScriptRoot 'Tools\verify_ai_environment.py') `
        -Destination (Join-Path $mediaRoot 'Payloads\Python\verify_ai_environment.py')

    $wheelhouseSource = Resolve-ExistingDirectory `
        -Path (Join-Path $payloadRootResolved 'Python\wheelhouse') `
        -Description 'Wheelhouse Python offline'
    $wheelhouseMedia = Join-Path $mediaRoot 'Payloads\Python\wheelhouse'
    Invoke-RobocopyChecked -Source $wheelhouseSource -Destination $wheelhouseMedia
    $wheelFiles = @(Get-ChildItem -LiteralPath $wheelhouseMedia -File -Filter '*.whl' | Sort-Object Name)
    if ($wheelFiles.Count -eq 0) {
        throw 'Il wheelhouse Python non contiene pacchetti .whl.'
    }

    Write-Host '5/7 - Generazione manifest SHA-256...' -ForegroundColor Cyan
    $pythonPayloads = @()
    $pythonPayloads += ,(New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $pythonMedia)
    $pythonPayloads += ,(New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $requirementsMedia)
    $pythonPayloads += ,(New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $smokeTestMedia)
    foreach ($wheel in $wheelFiles) {
        $pythonPayloads += ,(New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $wheel.FullName)
    }

    $warnings = @(
        'Prima del primo avvio verificare licenza VisionPro, seriali camera, mapping I/O e configurazione MySQL.',
        'XNavi.exe e'' firmato ma da solo puo'' richiedere rete. Per un target senza rete generare anche il pacchetto offline DAQNavi da XNavi.',
        'La configurazione MySQL resta una procedura guidata: definire credenziali e policy del sito durante il commissioning.',
        'UltraVNC viene installato come servizio e riceve la password operativa prevista dalla baseline. Il segreto non e'' scritto nel manifest o nei log; custodire comunque il supporto installer come materiale riservato.',
        'La patch VisionPro KB5077181 e'' inclusa ed eseguita dopo VisionPro 9.25. L''EXE Cognex interno non e'' firmato: il builder accetta esclusivamente gli hash ZIP ed EXE approvati.',
        'VisionProDependencies non viene duplicata nel payload bin: il setup crea e verifica la junction C:\QtisVision\bin\VisionProDependencies verso il runtime Cognex installato.',
        'Gli aggiornamenti HMI sono differenziali: vengono copiati, rimossi o sostituiti solo i file applicativi gestiti che risultano cambiati; cfg, OPC, cache WebView2 e VisionProDependencies restano protetti.',
        'La configurazione Cognex GigE modifica solo adattatori fisici associati senza ambiguita alle camere. In assenza di un match sicuro non tocca la rete e richiede il commissioning con il tool Cognex.',
        'Python Qtis viene aggiunto al PATH macchina senza impostare PYTHONHOME globale. L''avvio HMI ritardato attende i servizi noti, ma per continuita operativa puo avviare la HMI anche dopo timeout.',
        'Il runtime seed proviene dalla macchina sorgente. Trattare il supporto come materiale tecnico riservato.'
    )
    if ($RedactSecrets) {
        $warnings += 'Le credenziali XML del runtime seed sono state rimosse e devono essere configurate sul target.'
    }
    else {
        $warnings += 'Il supporto contiene la configurazione macchina corrente, incluse eventuali credenziali: custodirlo come dato sensibile.'
    }

    $manifest = [ordered]@{
        schemaVersion                       = 1
        productName                        = 'Qtis Vision Panel'
        productVersion                     = $productVersion
        mediaRevision                      = $MediaRevision
        packageType                        = 'Full'
        installRoot                        = 'C:\QtisVision'
        requiredFreeSpaceBytes             = 25GB
        containsMachineSpecificConfiguration = $true
        warnings                           = $warnings
        components                         = @(
            [ordered]@{
                id               = 'visionpro'
                displayName      = 'Cognex VisionPro 9.25 x64'
                description      = 'Runtime e driver Cognex richiesti dai job VPP.'
                kind             = 'visionpro-zip'
                required         = $false
                defaultSelected  = $true
                enabled          = $true
                interactive      = $true
                blockingReason   = $null
                detectionPath    = 'C:\Program Files\Cognex\VisionPro\bin\Cognex.Vision.Startup.Net.dll'
                minimumVersion   = '9.25.0.0'
                entryPoint       = 'setup.exe'
                installArguments = ''
                payloads         = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $visionProMedia)
                )
            },
            [ordered]@{
                id                         = 'visionpro-kb5077181'
                displayName                = 'VisionPro Windows Update KB5077181'
                description                = 'Patch Cognex richiesta per VisionPro 9.25; installazione guidata dopo il runtime base.'
                kind                       = 'interactive-exe'
                required                   = $false
                defaultSelected            = $true
                enabled                    = $true
                interactive                = $true
                blockingReason             = $null
                detectionPath              = $null
                detectionRegistryKey       = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{17F14A9A-E830-42B8-9E1C-049A4CA78758}'
                detectionRegistryValueName = 'DisplayVersion'
                minimumVersion             = '1.0'
                entryPoint                 = $null
                installArguments           = ''
                dependsOn                  = @('visionpro')
                payloads                   = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $visionPatchMedia)
                )
            },
            [ordered]@{
                id               = 'daqnavi'
                displayName      = 'Advantech DAQNavi'
                description      = 'Runtime/API Advantech. Le installazioni DAQNavi OEM compatibili vengono mantenute; verificare separatamente i driver PCIE-1756/PCIE-1884.'
                kind             = 'interactive-exe'
                required         = $false
                defaultSelected  = $true
                enabled          = $true
                interactive      = $true
                blockingReason   = $null
                detectionPath    = 'C:\Advantech\DAQNavi\Automation.BDaq\4.0.0.0\Automation.BDaq4.dll'
                detectionRegistryKey = 'SOFTWARE\Advantech\Components\cmp_daqnavi_runtime\Automation.BDaq4.dll'
                detectionRegistryValueName = 'FileVersion'
                detectionRegistryPathValueName = 'Path'
                detectionSearchFileName = 'Automation.BDaq4.dll'
                detectionSearchRoots = @(
                    '%SystemDrive%\Advantech\DAQNavi\Automation.BDaq',
                    '%ProgramFiles%\Advantech\DAQNavi\Automation.BDaq',
                    '%ProgramFiles(x86)%\Advantech\DAQNavi\Automation.BDaq'
                )
                minimumVersion   = '4.0.0.0'
                entryPoint       = $null
                installArguments = ''
                payloads         = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $daqMedia)
                )
            },
            [ordered]@{
                id               = 'mysql'
                displayName      = 'MySQL Server 8.0'
                description      = 'Database locale Qtis. La configurazione account resta guidata per sicurezza.'
                kind             = 'interactive-msi'
                required         = $false
                defaultSelected  = $true
                enabled          = $true
                interactive      = $true
                blockingReason   = $null
                detectionPath    = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqld.exe'
                minimumVersion   = '8.0.18.0'
                entryPoint       = $null
                installArguments = ''
                payloads         = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $mysqlMedia)
                )
            },
            [ordered]@{
                id               = 'heidisql'
                displayName      = 'HeidiSQL 12.11'
                description      = 'Strumento opzionale per commissioning e manutenzione del database.'
                kind             = 'interactive-exe'
                required         = $false
                defaultSelected  = $false
                enabled          = $true
                interactive      = $true
                blockingReason   = $null
                detectionPath    = 'C:\Program Files\HeidiSQL\heidisql.exe'
                minimumVersion   = '12.11.0.0'
                entryPoint       = $null
                installArguments = ''
                payloads         = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $heidiMedia)
                )
            },
            [ordered]@{
                id                        = 'ultravnc'
                displayName               = 'UltraVNC 1.6.4.0 x64'
                description               = 'Assistenza remota: installazione silenziosa Server/Viewer, servizio automatico e provisioning sicuro della password operativa.'
                kind                      = 'ultravnc'
                required                  = $false
                defaultSelected           = $true
                enabled                   = $true
                interactive               = $false
                configureWhenInstalled    = $true
                blockingReason            = $null
                detectionPath             = 'C:\Program Files\uvnc bvba\UltraVNC\winvnc.exe'
                detectionRegistryKey      = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Ultravnc2_is1'
                detectionRegistryValueName = 'DisplayVersion'
                detectionRegistryPathValueName = 'InstallLocation'
                minimumVersion            = '1.6.4.0'
                entryPoint                = $null
                installArguments          = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /COMPONENTS=UltraVNC_Server,UltraVNC_Viewer /TASKS=installservice,startservice'
                payloads                  = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $ultraVncMedia)
                )
            },
            [ordered]@{
                id               = 'notepad-plus-plus'
                displayName      = 'Notepad++ 8.9.3 x64'
                description      = 'Editor tecnico per XML, log e file di configurazione durante commissioning e manutenzione.'
                kind             = 'interactive-exe'
                required         = $false
                defaultSelected  = $true
                enabled          = $true
                interactive      = $false
                blockingReason   = $null
                detectionPath    = 'C:\Program Files\Notepad++\notepad++.exe'
                minimumVersion   = '8.9.3.0'
                entryPoint       = $null
                installArguments = '/S'
                payloads         = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $notepadPlusPlusMedia)
                )
            },
            [ordered]@{
                id               = 'python-ai'
                displayName      = 'Ambiente AI Python offline'
                description      = 'Python isolato, PyTorch CPU, ONNX, ONNX Script, Pillow e dipendenze per il training.'
                kind             = 'python-offline'
                required         = $false
                defaultSelected  = $true
                enabled          = $true
                interactive      = $false
                blockingReason   = $null
                detectionPath    = 'C:\QtisVision\AI\Python\python.exe'
                minimumVersion   = '3.13.14.0'
                entryPoint       = $null
                installArguments = '/quiet InstallAllUsers=1 TargetDir="{PythonRoot}" Include_launcher=0 Include_test=0 Include_doc=0 Shortcuts=0 PrependPath=0 Include_pip=1'
                payloads         = $pythonPayloads
            },
            [ordered]@{
                id               = 'qtis-core'
                displayName      = 'Qtis Vision Panel'
                description      = "HMI $productVersion, DLL x64, script e runtime seed C:\QtisVision."
                kind             = 'qtis-core'
                required         = $true
                defaultSelected  = $true
                enabled          = $true
                interactive      = $false
                blockingReason   = $null
                detectionPath    = 'C:\QtisVision\bin\QtisVisionPanel.exe'
                minimumVersion   = $productVersion
                entryPoint       = $null
                installArguments = ''
                dependsOn        = @('visionpro')
                payloads         = @(
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $applicationArchive),
                    (New-PayloadDescriptor -MediaRoot $mediaRoot -FullPath $runtimeArchive)
                )
            },
            [ordered]@{
                id                    = 'cognex-gige-network'
                displayName           = 'Ottimizzazione rete camere Cognex GigE'
                description           = 'Configura automaticamente solo le porte camera dedicate riconosciute tramite CameraConfig.xml o stato Cognex/eBUS; salva un backup prima delle modifiche.'
                kind                  = 'cognex-gige-network'
                required              = $false
                defaultSelected       = $true
                enabled               = $true
                interactive           = $false
                configureWhenInstalled = $true
                blockingReason        = $null
                detectionPath         = $null
                minimumVersion        = $null
                entryPoint            = $null
                installArguments      = ''
                dependsOn             = @('qtis-core')
                payloads              = @()
            },
            [ordered]@{
                id                    = 'qtis-autostart'
                displayName           = 'Avvio automatico HMI ritardato'
                description           = 'Crea nello Startup comune un avvio nascosto e configurabile, con attesa MySQL/NIC camera, anti-doppia istanza e log dedicato.'
                kind                  = 'qtis-autostart'
                required              = $false
                defaultSelected       = $true
                enabled               = $true
                interactive           = $false
                configureWhenInstalled = $true
                blockingReason        = $null
                detectionPath         = $null
                minimumVersion        = $null
                entryPoint            = $null
                installArguments      = ''
                dependsOn             = @('qtis-core')
                payloads              = @()
            }
        )
    }

    $manifestPath = Join-Path $mediaRoot 'installer-manifest.json'
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    $integrityLines = foreach ($component in $manifest.components) {
        foreach ($payload in $component.payloads) {
            "$($payload.sha256) *$($payload.relativePath)"
        }
    }

    Write-Host '6/7 - Compilazione bootstrapper self-contained...' -ForegroundColor Cyan
    $publishRoot = Join-Path $stagingRoot 'SetupPublish'
    $setupProject = Join-Path $PSScriptRoot 'QtisVision.Setup\QtisVision.Setup.csproj'
    & dotnet publish $setupProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $publishRoot `
        /p:Version=$productVersion `
        /p:FileVersion=$productVersion `
        /p:AssemblyVersion=$productVersion `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Compilazione bootstrapper fallita con codice $LASTEXITCODE."
    }

    $setupMedia = Join-Path $mediaRoot 'QtisVisionSetup.exe'
    Copy-Item `
        -LiteralPath (Join-Path $publishRoot 'QtisVisionSetup.exe') `
        -Destination $setupMedia `
        -Force
    $setupHash = (Get-FileHash -LiteralPath $setupMedia -Algorithm SHA256).Hash
    $integrityPath = Join-Path $mediaRoot 'payload-integrity.sha256'
    @($integrityLines) + @("$setupHash *QtisVisionSetup.exe") |
        Sort-Object -Unique |
        Set-Content -LiteralPath $integrityPath -Encoding ASCII

    $readme = @"
QTIS VISION PANEL - SUPPORTO INSTALLAZIONE OFFLINE
Versione: $productVersion-r$MediaRevision
Generato: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

AVVIO
1. Copiare l'intera cartella su un disco locale del PC macchina.
2. Avviare QtisVisionSetup.exe.
3. Premere Verifica prima di installare.
4. La riga HMI mostra Nuova installazione, Aggiorna o Ripara confrontando la versione installata.
5. I prerequisiti compatibili indicati Gia installato vengono deselezionati e mantenuti.
6. UltraVNC resta selezionato anche se presente: il setup verifica servizio e riapplica la configurazione di accesso senza mostrare la password nei log.
7. Selezionare i prerequisiti necessari e avviare Installa / aggiorna.
8. Completare le procedure guidate VisionPro, DAQNavi e MySQL.
9. Controllare il report Cognex GigE e poi riavviare Windows e seguire la checklist di commissioning.

INVENTARIO
- QtisVisionSetup.exe --inventory scrive installer-inventory.json senza modificare il PC.

AGGIORNAMENTO SICURO
- Il setup confronta SHA-256 e aggiorna solo i file applicativi aggiunti, cambiati o rimossi dalla nuova versione.
- Prima di ogni modifica salva i soli file coinvolti e usa un journal persistente per rollback e recovery dopo interruzione.
- Configurazioni, ricette, job VPP e modelli AI esistenti non vengono sovrascritti.
- cfg, OPC, cache WebView2 e VisionProDependencies sono protetti dal motore differenziale.
- I file Language sono aggiornati con backup.
- I log installer sono in C:\ProgramData\Pulsar\QtisVision\Installer\Logs.

RETE CAMERE COGNEX GIGE
- Il setup considera solo adattatori Ethernet fisici con binding Cognex eBUS attivo.
- Senza metadati espliciti, una porta viene riconosciuta solo con IPv4 privata statica e nessun default gateway.
- CameraConfig.xml puo specificare adapterName/adapterGuid/adapterMac, hostIpAddress, cameraIpAddress e prefixLength per un'associazione deterministica.
- Vengono applicati, se supportati dal driver OEM: Energy Efficient Ethernet Off, Interrupt Moderation On/Extreme, Jumbo Packet almeno 9000, buffer e code RSS al massimo.
- Restano attivi soltanto IPv4 ed eBUS sulla porta camera dedicata. Firewall e piano energia globale non vengono disabilitati automaticamente.
- Backup e risultati sono in C:\ProgramData\Pulsar\QtisVision\Installer\NetworkBackups e NetworkResults.

PYTHON E AVVIO AUTOMATICO
- Python Qtis e la relativa cartella Scripts vengono aggiunti al PATH macchina; QTIS_PYTHON_ROOT e QTIS_PYTHON_EXE espongono i percorsi completi senza impostare PYTHONHOME.
- Il collegamento Qtis Vision Panel (Delayed) nello Startup comune esegue un launcher PowerShell nascosto.
- Il launcher attende inizialmente 45 secondi, poi fino a 120 secondi i servizi configurati e le porte camera rilevate; impedisce doppie istanze.
- Impostazioni: C:\ProgramData\Pulsar\QtisVision\Startup\startup-settings.json.
- Log: C:\ProgramData\Pulsar\QtisVision\Startup\Logs\startup.log.
- Per continuita operativa, il default avvia comunque la HMI dopo timeout e lascia alla diagnostica applicativa la segnalazione delle dipendenze non disponibili.

INVENTARIO PC
- QtisVisionSetup.exe --inventory registra produttore, modello, baseboard, BIOS e CPU letti da SMBIOS, oltre allo stato dei componenti.
- Le temperature disco/RAM dipendono dai sensori esposti da firmware e driver. Un valore Sensor not available non viene sostituito con dati stimati.

NOTE
- Il supporto contiene una copia della configurazione della macchina sorgente.
- DAQNavi OEM viene mantenuto se Automation.BDaq4.dll e' presente in versione 4.0.0.0 o successiva.
- Verificare comunque in DAQNavi Navigator i driver delle schede PCIE-1756/PCIE-1884 montate.
- XNavi.exe puo richiedere rete se non e' stato generato il cache package offline DAQNavi.
- La licenza VisionPro e la configurazione MySQL restano operazioni di commissioning.
- UltraVNC viene installato come servizio automatico con regole firewall vendor; limitare l'accesso alla rete macchina/VPN e cambiare la credenziale secondo la policy del sito.
- Notepad++ viene installato o aggiornato in modalita silenziosa.
- La patch VisionPro KB5077181 e' installata dopo VisionPro 9.25 e verificata tramite hash ZIP ed EXE.
- Il setup crea C:\QtisVision\bin\VisionProDependencies come junction verso il bin di VisionPro e ne verifica la DLL sentinella.
- Il vecchio MSI MySQL con hash non valido e' escluso.
"@
    Set-Content -LiteralPath (Join-Path $mediaRoot 'README-INSTALLAZIONE.txt') -Value $readme -Encoding UTF8

    Write-Host '6b/7 - Generazione pacchetto aggiornamento applicativo...' -ForegroundColor Cyan
    New-Item -ItemType Directory -Path (Join-Path $updateMediaRoot 'Payloads\Core') -Force | Out-Null
    $stagedUpdateBin = Join-Path $stagingRoot 'ApplicationUpdate'
    Invoke-RobocopyChecked `
        -Source $stagedBin `
        -Destination $stagedUpdateBin `
        -ExtraArguments @(
            '/XD',
            (Join-Path $stagedBin 'cfg'),
            (Join-Path $stagedBin 'OPC'),
            (Join-Path $stagedBin 'QtisVisionPanel.exe.WebView2'),
            (Join-Path $stagedBin 'VisionProDependencies')
        )
    $protectedUpdateDirectories = @(
        @(
            'cfg',
            'OPC',
            'QtisVisionPanel.exe.WebView2',
            'VisionProDependencies'
        ) | ForEach-Object { Join-Path $stagedUpdateBin $_ } |
            Where-Object { Test-Path -LiteralPath $_ }
    )
    if ($protectedUpdateDirectories.Count -gt 0) {
        throw "Il payload UpdateOnly contiene cartelle protette macchina: $($protectedUpdateDirectories -join ', ')"
    }
    $updateApplicationArchive = Join-Path `
        $updateMediaRoot `
        "Payloads\Core\ApplicationBin-$productVersion.zip"
    New-ZipArchive -Source $stagedUpdateBin -Destination $updateApplicationArchive
    $updateSetupMedia = Copy-Payload `
        -Source $setupMedia `
        -Destination (Join-Path $updateMediaRoot 'QtisVisionUpdate.exe')

    $updateWarnings = @(
        'Questo supporto aggiorna esclusivamente una Qtis Vision Panel gia installata e rifiuta le installazioni nuove.',
        'Chiudere QtisVisionPanel prima della verifica e dell aggiornamento.',
        'Programs, ricette, job VPP, cfg, OPC, Language, immagini, modelli AI e configurazioni macchina non sono inclusi nel pacchetto e non vengono modificati.',
        'I file applicativi vengono confrontati tramite SHA-256; vengono scritti solo file nuovi o diversi e rimossi solo file appartenenti a un precedente manifest affidabile.',
        'Prima delle modifiche viene creato un backup dei soli file coinvolti e un journal persistente abilita rollback e recovery.'
    )
    $updateManifest = [ordered]@{
        schemaVersion                         = 1
        productName                          = 'Qtis Vision Panel Update'
        productVersion                       = $productVersion
        mediaRevision                        = $MediaRevision
        packageType                          = 'UpdateOnly'
        installRoot                          = 'C:\QtisVision'
        requiredFreeSpaceBytes               = 2GB
        containsMachineSpecificConfiguration = $false
        warnings                             = $updateWarnings
        components                           = @(
            [ordered]@{
                id               = 'qtis-core'
                displayName      = 'Qtis Vision Panel - aggiornamento applicativo'
                description      = "Aggiorna solo i file gestiti in C:\QtisVision\bin alla versione $productVersion."
                kind             = 'qtis-core'
                required         = $true
                defaultSelected  = $true
                enabled          = $true
                interactive      = $false
                blockingReason   = $null
                detectionPath    = 'C:\QtisVision\bin\QtisVisionPanel.exe'
                minimumVersion   = $productVersion
                entryPoint       = $null
                installArguments = ''
                dependsOn        = @()
                payloads         = @(
                    (New-PayloadDescriptor -MediaRoot $updateMediaRoot -FullPath $updateApplicationArchive)
                )
            }
        )
    }
    $updateManifestPath = Join-Path $updateMediaRoot 'installer-manifest.json'
    $updateManifest | ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $updateManifestPath -Encoding UTF8

    $updateSetupHash = (Get-FileHash -LiteralPath $updateSetupMedia -Algorithm SHA256).Hash
    $updatePayloadHash = (Get-FileHash -LiteralPath $updateApplicationArchive -Algorithm SHA256).Hash
    @(
        "$updatePayloadHash *Payloads\Core\ApplicationBin-$productVersion.zip",
        "$updateSetupHash *QtisVisionUpdate.exe"
    ) | Set-Content -LiteralPath (Join-Path $updateMediaRoot 'payload-integrity.sha256') -Encoding ASCII

    $updateReadme = @"
QTIS VISION PANEL - AGGIORNAMENTO APPLICATIVO PROTETTO
Versione: $productVersion-r$MediaRevision
Generato: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

USO
1. Usare questo pacchetto soltanto su una macchina con Qtis Vision Panel gia installata.
2. Copiare l intera cartella sul PC macchina e chiudere QtisVisionPanel.exe.
3. Avviare QtisVisionUpdate.exe come amministratore.
4. Premere Verifica, controllare la versione installata e avviare l aggiornamento.
5. Riavviare la HMI ed eseguire il collaudo della ricetta attiva.

PROTEZIONI
- Il pacchetto contiene soltanto ApplicationBin e non contiene RuntimeSeed o prerequisiti.
- Programs, VPP, ricette, cfg, OPC, Language, immagini, modelli AI e configurazioni macchina non vengono inclusi ne modificati.
- Il motore confronta SHA-256 e scrive solo file applicativi nuovi o diversi.
- I file software obsoleti vengono rimossi solo se appartenevano al precedente manifest affidabile.
- Backup e journal consentono rollback e recovery dopo una interruzione.
- Un PC senza installazione esistente viene rifiutato: per quello usare QtisVisionPanel_$productVersion-r$MediaRevision.

LOG
C:\ProgramData\Pulsar\QtisVision\Installer\Logs
"@
    Set-Content -LiteralPath (Join-Path $updateMediaRoot 'README-AGGIORNAMENTO.txt') `
        -Value $updateReadme `
        -Encoding UTF8

    Write-Host '7/7 - Validazione finale del media...' -ForegroundColor Cyan
    $checkedPayloads = Test-GeneratedManifest -ManifestPath $manifestPath -MediaRoot $mediaRoot
    $checkedUpdatePayloads = Test-GeneratedManifest `
        -ManifestPath $updateManifestPath `
        -MediaRoot $updateMediaRoot
    $mediaBytes = (Get-ChildItem -LiteralPath $mediaRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $updateMediaBytes = (Get-ChildItem -LiteralPath $updateMediaRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $summary = [ordered]@{
        productVersion = $productVersion
        mediaRevision  = $MediaRevision
        generatedAt    = (Get-Date).ToString('o')
        mediaRoot      = $mediaRoot
        payloadCount   = $checkedPayloads
        mediaBytes     = [Int64]$mediaBytes
        setupSha256    = $setupHash
        secretsRedacted = [bool]$RedactSecrets
        validation     = 'PASSED'
        updateMediaRoot = $updateMediaRoot
        updatePayloadCount = $checkedUpdatePayloads
        updateMediaBytes = [Int64]$updateMediaBytes
        updateSetupSha256 = $updateSetupHash
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mediaRoot 'build-summary.json') -Encoding UTF8

    $updateSummary = [ordered]@{
        productVersion = $productVersion
        mediaRevision  = $MediaRevision
        packageType    = 'UpdateOnly'
        generatedAt    = (Get-Date).ToString('o')
        mediaRoot      = $updateMediaRoot
        payloadCount   = $checkedUpdatePayloads
        mediaBytes     = [Int64]$updateMediaBytes
        setupSha256    = $updateSetupHash
        containsMachineSpecificConfiguration = $false
        validation     = 'PASSED'
    }
    $updateSummary | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $updateMediaRoot 'build-summary.json') -Encoding UTF8

    Write-Host ''
    Write-Host "Supporto creato e validato: $mediaRoot" -ForegroundColor Green
    Write-Host "Payload verificati: $checkedPayloads"
    Write-Host ("Dimensione: {0:N2} GB" -f ($mediaBytes / 1GB))
    Write-Host "Aggiornamento creato e validato: $updateMediaRoot" -ForegroundColor Green
    Write-Host "Payload update verificati: $checkedUpdatePayloads"
    Write-Host ("Dimensione update: {0:N2} MB" -f ($updateMediaBytes / 1MB))
}
finally {
    if (-not $KeepStaging -and (Test-Path -LiteralPath $stagingRoot)) {
        Remove-SafeDirectory -Target $stagingRoot -AllowedRoot $outputRootFull
    }
}
