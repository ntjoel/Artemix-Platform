[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][string]$ResultPath,
    [Parameter(Mandatory = $true)][string]$BackupRoot,
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$warnings = [System.Collections.Generic.List[string]]::new()
$configured = [System.Collections.Generic.List[object]]::new()
$skipped = [System.Collections.Generic.List[object]]::new()

function Add-Warning {
    param([Parameter(Mandatory = $true)][string]$Message)
    $warnings.Add($Message)
    Write-Warning $Message
}

function Get-XmlAttributeValue {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlNode]$Node,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    foreach ($name in $Names) {
        $attribute = @($Node.Attributes) |
            Where-Object { $_.Name.Equals($name, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($null -ne $attribute -and -not [string]::IsNullOrWhiteSpace($attribute.Value)) {
            return $attribute.Value.Trim()
        }
    }

    return $null
}

function Normalize-MacAddress {
    param([AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }

    return ($Value -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
}

function Convert-SubnetMaskToPrefixLength {
    param([AllowNull()][string]$SubnetMask)
    if ([string]::IsNullOrWhiteSpace($SubnetMask)) {
        return $null
    }

    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($SubnetMask, [ref]$parsed)) {
        return $null
    }

    $bits = ($parsed.GetAddressBytes() | ForEach-Object {
        [Convert]::ToString($_, 2).PadLeft(8, '0')
    }) -join ''
    if ($bits -notmatch '^1*0*$') {
        return $null
    }

    return ($bits.ToCharArray() | Where-Object { $_ -eq '1' }).Count
}

function Convert-IPv4ToUInt32 {
    param([Parameter(Mandatory = $true)][string]$Address)
    $parsed = [System.Net.IPAddress]::Parse($Address)
    $bytes = $parsed.GetAddressBytes()
    if ($bytes.Length -ne 4) {
        throw "Indirizzo non IPv4: $Address"
    }

    return ([uint32]$bytes[0] -shl 24) -bor
           ([uint32]$bytes[1] -shl 16) -bor
           ([uint32]$bytes[2] -shl 8) -bor
           [uint32]$bytes[3]
}

function Test-SameSubnet {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right,
        [Parameter(Mandatory = $true)][int]$PrefixLength
    )

    if ($PrefixLength -lt 1 -or $PrefixLength -gt 30) {
        return $false
    }

    $mask = [uint32]::MaxValue -shl (32 - $PrefixLength)
    return ((Convert-IPv4ToUInt32 $Left) -band $mask) -eq
           ((Convert-IPv4ToUInt32 $Right) -band $mask)
}

function Test-PrivateIPv4 {
    param([Parameter(Mandatory = $true)][string]$Address)
    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($Address, [ref]$parsed) -or
        $parsed.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        return $false
    }

    $bytes = $parsed.GetAddressBytes()
    return $bytes[0] -eq 10 -or
           ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
           ($bytes[0] -eq 192 -and $bytes[1] -eq 168)
}

function Get-CameraConfigPath {
    param([Parameter(Mandatory = $true)][string]$Root)

    $mainConfigPath = Join-Path $Root 'cfg\Config.xml'
    if (Test-Path -LiteralPath $mainConfigPath -PathType Leaf) {
        try {
            [xml]$mainConfig = Get-Content -LiteralPath $mainConfigPath -Raw
            $recipeFolderNode = $mainConfig.SelectSingleNode("//*[local-name()='Recipe_Folder']")
            if ($null -ne $recipeFolderNode -and
                -not [string]::IsNullOrWhiteSpace($recipeFolderNode.InnerText)) {
                $recipeFolder = [Environment]::ExpandEnvironmentVariables($recipeFolderNode.InnerText.Trim())
                if (-not [System.IO.Path]::IsPathRooted($recipeFolder)) {
                    $recipeFolder = Join-Path $Root $recipeFolder
                }

                $candidate = Join-Path $recipeFolder 'CameraConfig.xml'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return (Resolve-Path -LiteralPath $candidate).Path
                }
            }
        }
        catch {
            Add-Warning "Config.xml non leggibile per risolvere CameraConfig.xml: $($_.Exception.Message)"
        }
    }

    $fallback = Join-Path $Root 'Programs\CameraConfig.xml'
    if (Test-Path -LiteralPath $fallback -PathType Leaf) {
        return (Resolve-Path -LiteralPath $fallback).Path
    }

    return $null
}

function Get-AdapterIPv4 {
    param([Parameter(Mandatory = $true)][int]$InterfaceIndex)
    return @(Get-NetIPAddress -InterfaceIndex $InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object {
            $_.IPAddress -ne '127.0.0.1' -and
            -not $_.IPAddress.StartsWith('169.254.', [StringComparison]::OrdinalIgnoreCase)
        })
}

function Test-AdapterHasDefaultGateway {
    param([Parameter(Mandatory = $true)][int]$InterfaceIndex)
    return @(
        Get-NetRoute -InterfaceIndex $InterfaceIndex -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
            Where-Object { $_.NextHop -and $_.NextHop -ne '0.0.0.0' }
    ).Count -gt 0
}

function Test-EbusBindingEnabled {
    param([Parameter(Mandatory = $true)][string]$AdapterName)
    return @(
        Get-NetAdapterBinding -Name $AdapterName -AllBindings -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Enabled -and
                ($_.ComponentID -match '(?i)ebUniversal|eBUS|Pleora|Cognex')
            }
    ).Count -gt 0
}

function Convert-LinkSpeedToBitsPerSecond {
    param([AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch '(?i)^\s*([0-9]+(?:[\.,][0-9]+)?)\s*([KMGT])bps\s*$') {
        return 0L
    }

    $numericText = $Matches[1].Replace(',', '.')
    $numeric = [double]::Parse($numericText, [Globalization.CultureInfo]::InvariantCulture)
    $multiplier = switch ($Matches[2].ToUpperInvariant()) {
        'K' { 1KB }
        'M' { 1MB }
        'G' { 1GB }
        'T' { 1TB }
    }
    return [long]($numeric * $multiplier)
}

function Get-NetworkMetadata {
    param([Parameter(Mandatory = $true)][string]$CameraConfigPath)

    [xml]$cameraConfig = Get-Content -LiteralPath $CameraConfigPath -Raw
    $nodes = @($cameraConfig.SelectNodes("//*[local-name()='Camera']"))
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($node in $nodes) {
        $prefixText = Get-XmlAttributeValue -Node $node -Names @('prefixLength', 'hostPrefixLength')
        $prefixLength = 24
        if (-not [string]::IsNullOrWhiteSpace($prefixText)) {
            $parsedPrefix = 0
            if ([int]::TryParse($prefixText, [ref]$parsedPrefix)) {
                $prefixLength = $parsedPrefix
            }
        }
        else {
            $subnetMask = Get-XmlAttributeValue -Node $node -Names @('subnetMask', 'hostSubnetMask')
            $convertedPrefix = Convert-SubnetMaskToPrefixLength -SubnetMask $subnetMask
            if ($null -ne $convertedPrefix) {
                $prefixLength = $convertedPrefix
            }
        }

        $item = [pscustomobject]@{
            Role = Get-XmlAttributeValue -Node $node -Names @('type', 'role', 'name')
            AdapterName = Get-XmlAttributeValue -Node $node -Names @('adapterName', 'networkAdapter', 'interfaceAlias')
            AdapterGuid = Get-XmlAttributeValue -Node $node -Names @('adapterGuid', 'interfaceGuid')
            AdapterMac = Normalize-MacAddress (Get-XmlAttributeValue -Node $node -Names @('adapterMac', 'macAddress'))
            HostIp = Get-XmlAttributeValue -Node $node -Names @('hostIpAddress', 'hostIp', 'adapterIpAddress')
            CameraIp = Get-XmlAttributeValue -Node $node -Names @('cameraIpAddress', 'cameraIp', 'ipAddress')
            PrefixLength = $prefixLength
        }
        $items.Add($item)
    }

    return @($items)
}

function Test-ExplicitDescriptor {
    param([Parameter(Mandatory = $true)]$Descriptor)
    return -not [string]::IsNullOrWhiteSpace($Descriptor.AdapterName) -or
           -not [string]::IsNullOrWhiteSpace($Descriptor.AdapterGuid) -or
           -not [string]::IsNullOrWhiteSpace($Descriptor.AdapterMac) -or
           -not [string]::IsNullOrWhiteSpace($Descriptor.HostIp) -or
           -not [string]::IsNullOrWhiteSpace($Descriptor.CameraIp)
}

function Find-AdaptersForDescriptor {
    param(
        [Parameter(Mandatory = $true)]$Descriptor,
        [Parameter(Mandatory = $true)][object[]]$Adapters
    )

    $hasIdentity = -not [string]::IsNullOrWhiteSpace($Descriptor.AdapterName) -or
                   -not [string]::IsNullOrWhiteSpace($Descriptor.AdapterGuid) -or
                   -not [string]::IsNullOrWhiteSpace($Descriptor.AdapterMac)
    return @($Adapters | Where-Object {
        $adapter = $_
        if (-not [string]::IsNullOrWhiteSpace($Descriptor.AdapterName) -and
            -not $adapter.Name.Equals($Descriptor.AdapterName, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
        if (-not [string]::IsNullOrWhiteSpace($Descriptor.AdapterGuid) -and
            -not $adapter.InterfaceGuid.ToString().Trim('{}').Equals(
                $Descriptor.AdapterGuid.Trim('{}'),
                [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
        if (-not [string]::IsNullOrWhiteSpace($Descriptor.AdapterMac) -and
            (Normalize-MacAddress $adapter.MacAddress) -ne $Descriptor.AdapterMac) {
            return $false
        }

        $addresses = @(Get-AdapterIPv4 -InterfaceIndex $adapter.ifIndex)
        if (-not $hasIdentity -and -not [string]::IsNullOrWhiteSpace($Descriptor.HostIp)) {
            return @($addresses | Where-Object { $_.IPAddress -eq $Descriptor.HostIp }).Count -gt 0
        }
        if (-not $hasIdentity -and -not [string]::IsNullOrWhiteSpace($Descriptor.CameraIp)) {
            return @($addresses | Where-Object {
                Test-SameSubnet -Left $_.IPAddress -Right $Descriptor.CameraIp -PrefixLength $_.PrefixLength
            }).Count -gt 0
        }

        return $true
    })
}

function Select-AutomaticCameraAdapters {
    param([Parameter(Mandatory = $true)][object[]]$Adapters)

    return @($Adapters | Where-Object {
        $adapter = $_
        $description = "$($adapter.Name) $($adapter.InterfaceDescription)"
        if ($description -match '(?i)wi-?fi|wireless|wlan|bluetooth|virtual|hyper-v|vmware|vpn|tap|tunnel') {
            $skipped.Add([pscustomobject]@{ Adapter = $adapter.Name; Reason = 'not a dedicated physical Ethernet adapter' })
            return $false
        }

        $addresses = @(Get-AdapterIPv4 -InterfaceIndex $adapter.ifIndex)
        $privateAddresses = @($addresses | Where-Object { Test-PrivateIPv4 $_.IPAddress })
        $ipInterface = Get-NetIPInterface -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue
        $manualAddress = $null -ne $ipInterface -and $ipInterface.Dhcp -eq 'Disabled' -and $privateAddresses.Count -gt 0
        $dedicatedRoute = -not (Test-AdapterHasDefaultGateway -InterfaceIndex $adapter.ifIndex)
        $ebusEnabled = Test-EbusBindingEnabled -AdapterName $adapter.Name
        $linkSpeed = Convert-LinkSpeedToBitsPerSecond $adapter.LinkSpeed.ToString()
        $gigabitLink = $adapter.Status -ne 'Up' -or $linkSpeed -ge 1000000000
        if (-not ($manualAddress -and $dedicatedRoute -and $ebusEnabled -and $gigabitLink)) {
            $skipped.Add([pscustomobject]@{
                Adapter = $adapter.Name
                Reason = "auto-match rejected: manualPrivateIp=$manualAddress; noDefaultGateway=$dedicatedRoute; eBus=$ebusEnabled; gigabitLink=$gigabitLink"
            })
            return $false
        }

        return $true
    })
}

function Get-AdapterBackup {
    param([Parameter(Mandatory = $true)]$Adapter)

    $advanced = @(Get-NetAdapterAdvancedProperty -Name $Adapter.Name -AllProperties -ErrorAction SilentlyContinue |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.RegistryKeyword) } |
        Select-Object DisplayName, DisplayValue, RegistryKeyword, RegistryValue)
    $bindings = @(Get-NetAdapterBinding -Name $Adapter.Name -AllBindings -ErrorAction SilentlyContinue |
        Select-Object DisplayName, ComponentID, Enabled)
    $power = Get-NetAdapterPowerManagement -Name $Adapter.Name -ErrorAction SilentlyContinue |
        Select-Object ArpOffload, D0PacketCoalescing, DeviceSleepOnDisconnect, NSOffload,
            RsnRekeyOffload, SelectiveSuspend, WakeOnMagicPacket, WakeOnPattern
    $addresses = @(Get-AdapterIPv4 -InterfaceIndex $Adapter.ifIndex |
        Select-Object IPAddress, PrefixLength, PrefixOrigin, SuffixOrigin)

    return [pscustomobject]@{
        Name = $Adapter.Name
        InterfaceDescription = $Adapter.InterfaceDescription
        InterfaceGuid = $Adapter.InterfaceGuid.ToString()
        InterfaceIndex = $Adapter.ifIndex
        MacAddress = $Adapter.MacAddress
        Status = $Adapter.Status.ToString()
        LinkSpeed = $Adapter.LinkSpeed.ToString()
        IPv4 = $addresses
        Bindings = $bindings
        AdvancedProperties = $advanced
        PowerManagement = $power
    }
}

function Find-AdvancedProperty {
    param(
        [Parameter(Mandatory = $true)][object[]]$Properties,
        [Parameter(Mandatory = $true)][string]$RegistryPattern,
        [Parameter(Mandatory = $true)][string]$DisplayPattern
    )

    return @($Properties | Where-Object {
        ($_.RegistryKeyword -and $_.RegistryKeyword -match $RegistryPattern) -or
        ($_.DisplayName -and $_.DisplayName -match $DisplayPattern)
    } | Sort-Object @{ Expression = { if ($_.RegistryKeyword -match $RegistryPattern) { 0 } else { 1 } } }) |
        Select-Object -First 1
}

function Set-AdvancedPropertyChoice {
    param(
        [Parameter(Mandatory = $true)][string]$AdapterName,
        [Parameter(Mandatory = $true)][object[]]$Properties,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$RegistryPattern,
        [Parameter(Mandatory = $true)][string]$DisplayPattern,
        [Parameter(Mandatory = $true)][ValidateSet('Off', 'On', 'Extreme', 'Maximum')][string]$Choice,
        [int]$MinimumNumericValue = 0
    )

    $property = Find-AdvancedProperty -Properties $Properties -RegistryPattern $RegistryPattern -DisplayPattern $DisplayPattern
    if ($null -eq $property) {
        Add-Warning "${AdapterName}: proprieta '$Label' non esposta dal driver OEM."
        return $false
    }

    $validDisplay = @($property.ValidDisplayValues)
    $validRegistry = @($property.ValidRegistryValues)
    $targetRegistry = $null
    if ($Choice -eq 'Maximum') {
        $numericValues = [System.Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $validRegistry.Count; $index++) {
            $numeric = 0L
            if ([long]::TryParse([string]$validRegistry[$index], [ref]$numeric)) {
                $numericValues.Add([pscustomobject]@{ Numeric = $numeric; Registry = [string]$validRegistry[$index] })
            }
        }
        $selectedValue = @($numericValues | Sort-Object Numeric -Descending | Select-Object -First 1)
        if ($selectedValue.Count -gt 0 -and $selectedValue[0].Numeric -ge $MinimumNumericValue) {
            $targetRegistry = $selectedValue[0].Registry
        }
    }
    else {
        $displayPatternForChoice = switch ($Choice) {
            'Off' { '(?i)^off$|disable|disabil|spento|disattiv' }
            'On' { '(?i)^on$|enable|abilit|attiv' }
            'Extreme' { '(?i)extreme|estrem' }
        }
        for ($index = 0; $index -lt $validDisplay.Count -and $index -lt $validRegistry.Count; $index++) {
            if ([string]$validDisplay[$index] -match $displayPatternForChoice) {
                $targetRegistry = [string]$validRegistry[$index]
                break
            }
        }

        if ($null -eq $targetRegistry) {
            $fallback = if ($Choice -eq 'Off') { '0' } elseif ($Choice -eq 'On') { '1' } else { $null }
            if ($null -ne $fallback -and $validRegistry -contains $fallback) {
                $targetRegistry = $fallback
            }
        }
    }

    if ($null -eq $targetRegistry) {
        Add-Warning "${AdapterName}: nessun valore compatibile trovato per '$Label' ($Choice)."
        return $false
    }

    $currentRegistry = [string](@($property.RegistryValue) | Select-Object -First 1)
    if ($currentRegistry -eq $targetRegistry) {
        return $false
    }

    Set-NetAdapterAdvancedProperty `
        -Name $AdapterName `
        -RegistryKeyword $property.RegistryKeyword `
        -RegistryValue $targetRegistry `
        -NoRestart `
        -ErrorAction Stop
    return $true
}

function Set-DedicatedBindings {
    param([Parameter(Mandatory = $true)][string]$AdapterName)

    $changed = $false
    foreach ($binding in @(Get-NetAdapterBinding -Name $AdapterName -AllBindings -ErrorAction Stop)) {
        $isAllowed = $binding.ComponentID -eq 'ms_tcpip' -or
                     $binding.ComponentID -match '(?i)ebUniversal|eBUS|Pleora|Cognex'
        if ($isAllowed) {
            if (-not $binding.Enabled) {
                Enable-NetAdapterBinding -Name $AdapterName -ComponentID $binding.ComponentID -ErrorAction Stop
                $changed = $true
            }
        }
        elseif ($binding.Enabled) {
            Disable-NetAdapterBinding -Name $AdapterName -ComponentID $binding.ComponentID -ErrorAction Stop
            $changed = $true
        }
    }

    return $changed
}

function Set-AdapterPowerManagement {
    param([Parameter(Mandatory = $true)][string]$AdapterName)

    $parameters = @{
        Name = $AdapterName
        NoRestart = $true
        ErrorAction = 'Stop'
    }
    $commandParameters = (Get-Command Set-NetAdapterPowerManagement).Parameters
    $current = Get-NetAdapterPowerManagement -Name $AdapterName -ErrorAction SilentlyContinue
    foreach ($setting in @('SelectiveSuspend', 'DeviceSleepOnDisconnect', 'D0PacketCoalescing')) {
        $currentValue = if ($null -ne $current) { [string]$current.$setting } else { '' }
        if ($commandParameters.ContainsKey($setting) -and
            -not [string]::IsNullOrWhiteSpace($currentValue) -and
            $currentValue -ne 'Unsupported' -and
            $currentValue -ne 'Disabled') {
            $parameters[$setting] = 'Disabled'
        }
    }
    if ($parameters.Count -le 3) {
        return $false
    }

    Set-NetAdapterPowerManagement @parameters
    return $true
}

function Set-ExplicitHostAddress {
    param(
        [Parameter(Mandatory = $true)]$Adapter,
        [Parameter(Mandatory = $true)][object[]]$Descriptors
    )

    $requested = @($Descriptors | Where-Object { -not [string]::IsNullOrWhiteSpace($_.HostIp) } |
        Group-Object HostIp, PrefixLength)
    if ($requested.Count -eq 0) {
        return $false
    }
    if ($requested.Count -gt 1) {
        Add-Warning "$($Adapter.Name): CameraConfig.xml richiede piu indirizzi host diversi; IP non modificato."
        return $false
    }

    $descriptor = $requested[0].Group[0]
    if (-not (Test-PrivateIPv4 $descriptor.HostIp) -or
        $descriptor.PrefixLength -lt 8 -or $descriptor.PrefixLength -gt 30) {
        Add-Warning "$($Adapter.Name): host IP/prefix non sicuro o non valido in CameraConfig.xml; IP non modificato."
        return $false
    }

    foreach ($cameraDescriptor in @($Descriptors | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.CameraIp)
    })) {
        $cameraIp = $cameraDescriptor.CameraIp
        if (-not (Test-PrivateIPv4 $cameraIp) -or
            $cameraIp -eq $descriptor.HostIp -or
            -not (Test-SameSubnet -Left $descriptor.HostIp -Right $cameraIp -PrefixLength $descriptor.PrefixLength)) {
            Add-Warning "$($Adapter.Name): host IP $($descriptor.HostIp)/$($descriptor.PrefixLength) e camera IP $cameraIp non formano una coppia IPv4 privata valida; IP non modificato."
            return $false
        }
    }

    if (Test-AdapterHasDefaultGateway -InterfaceIndex $Adapter.ifIndex) {
        Add-Warning "$($Adapter.Name): presente un default gateway; IP non modificato per proteggere la rete aziendale."
        return $false
    }

    $changed = $false
    $ipInterface = Get-NetIPInterface `
        -InterfaceIndex $Adapter.ifIndex `
        -AddressFamily IPv4 `
        -ErrorAction SilentlyContinue
    if ($null -eq $ipInterface -or $ipInterface.Dhcp -ne 'Disabled') {
        Set-NetIPInterface -InterfaceIndex $Adapter.ifIndex -AddressFamily IPv4 -Dhcp Disabled -ErrorAction Stop
        $changed = $true
    }

    $existing = @(Get-AdapterIPv4 -InterfaceIndex $Adapter.ifIndex)
    if (@($existing | Where-Object {
        $_.IPAddress -eq $descriptor.HostIp -and $_.PrefixLength -eq $descriptor.PrefixLength
    }).Count -gt 0) {
        return $changed
    }

    New-NetIPAddress `
        -InterfaceIndex $Adapter.ifIndex `
        -IPAddress $descriptor.HostIp `
        -PrefixLength $descriptor.PrefixLength `
        -AddressFamily IPv4 `
        -ErrorAction Stop | Out-Null
    return $true
}

function Write-JsonAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary -Encoding UTF8
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

$result = [ordered]@{
    schemaVersion = 1
    status = 'Started'
    applyRequested = [bool]$Apply
    cameraConfigPath = $null
    configuredAdapters = @()
    skippedAdapters = @()
    warnings = @()
    backupPath = $null
    completedAtUtc = $null
}

try {
    $cameraConfigPath = Get-CameraConfigPath -Root ([System.IO.Path]::GetFullPath($InstallRoot))
    $result.cameraConfigPath = $cameraConfigPath
    $metadata = @()
    if ($null -ne $cameraConfigPath) {
        try {
            $metadata = @(Get-NetworkMetadata -CameraConfigPath $cameraConfigPath)
        }
        catch {
            Add-Warning "CameraConfig.xml non leggibile per i metadati rete: $($_.Exception.Message). Sara usato il riconoscimento conservativo."
        }
    }
    else {
        Add-Warning 'CameraConfig.xml non trovato. Sara usato solo il riconoscimento conservativo dello stato rete Cognex/eBUS.'
    }

    $physicalAdapters = @(Get-NetAdapter -Physical -ErrorAction Stop)
    $selectedByGuid = @{}
    $descriptorsByGuid = @{}
    $explicitDescriptors = @($metadata | Where-Object { Test-ExplicitDescriptor $_ })
    if ($explicitDescriptors.Count -gt 0) {
        foreach ($descriptor in $explicitDescriptors) {
            $matches = @(Find-AdaptersForDescriptor -Descriptor $descriptor -Adapters $physicalAdapters)
            if ($matches.Count -ne 1) {
                Add-Warning "Camera '$($descriptor.Role)': associazione scheda rete non univoca (match=$($matches.Count)); nessuna modifica per questa camera."
                continue
            }

            $key = $matches[0].InterfaceGuid.ToString()
            $selectedByGuid[$key] = $matches[0]
            if (-not $descriptorsByGuid.ContainsKey($key)) {
                $descriptorsByGuid[$key] = [System.Collections.Generic.List[object]]::new()
            }
            $descriptorsByGuid[$key].Add($descriptor)
        }
    }
    else {
        foreach ($adapter in @(Select-AutomaticCameraAdapters -Adapters $physicalAdapters)) {
            $selectedByGuid[$adapter.InterfaceGuid.ToString()] = $adapter
            $descriptorsByGuid[$adapter.InterfaceGuid.ToString()] = [System.Collections.Generic.List[object]]::new()
        }
    }

    $selectedAdapters = @($selectedByGuid.Values | Sort-Object Name)
    if ($selectedAdapters.Count -eq 0) {
        $result.status = 'NoCandidates'
        Add-Warning 'Nessuna scheda GigE camera identificata con sicurezza. La rete non e stata modificata; completare il commissioning con Cognex GigE Vision Configuration Tool.'
    }
    elseif (-not $Apply) {
        $result.status = 'AuditOnly'
        foreach ($adapter in $selectedAdapters) {
            $configured.Add([pscustomobject]@{
                Adapter = $adapter.Name
                InterfaceDescription = $adapter.InterfaceDescription
                Status = 'Candidate'
                Changed = $false
            })
        }
    }
    else {
        New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
        $backupPath = Join-Path $BackupRoot ("cognex-gige-network-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $backup = [ordered]@{
            schemaVersion = 1
            createdAtUtc = [DateTime]::UtcNow
            cameraConfigPath = $cameraConfigPath
            adapters = @($selectedAdapters | ForEach-Object { Get-AdapterBackup -Adapter $_ })
        }
        Write-JsonAtomically -Path $backupPath -Value $backup
        $result.backupPath = $backupPath

        foreach ($adapter in $selectedAdapters) {
            $changed = $false
            $adapterWarningsBefore = $warnings.Count
            try {
                if (-not (Test-EbusBindingEnabled -AdapterName $adapter.Name)) {
                    Add-Warning "$($adapter.Name): binding Cognex eBUS non attivo; scheda non modificata. Reinstallare i Cognex Drivers."
                    $configured.Add([pscustomobject]@{
                        Adapter = $adapter.Name
                        InterfaceDescription = $adapter.InterfaceDescription
                        Status = 'RejectedMissingEbus'
                        Changed = $false
                    })
                    continue
                }

                if (Test-AdapterHasDefaultGateway -InterfaceIndex $adapter.ifIndex) {
                    Add-Warning "$($adapter.Name): presente un default gateway; scheda esclusa per proteggere la rete aziendale."
                    $configured.Add([pscustomobject]@{
                        Adapter = $adapter.Name
                        InterfaceDescription = $adapter.InterfaceDescription
                        Status = 'RejectedDefaultGateway'
                        Changed = $false
                    })
                    continue
                }

                $linkSpeed = Convert-LinkSpeedToBitsPerSecond $adapter.LinkSpeed.ToString()
                if ($adapter.Status -eq 'Up' -and $linkSpeed -lt 1000000000) {
                    Add-Warning "$($adapter.Name): collegamento attivo sotto 1 Gbps ($($adapter.LinkSpeed)); scheda non modificata."
                    $configured.Add([pscustomobject]@{
                        Adapter = $adapter.Name
                        InterfaceDescription = $adapter.InterfaceDescription
                        Status = 'RejectedBelowGigabit'
                        Changed = $false
                    })
                    continue
                }

                $descriptors = if ($descriptorsByGuid.ContainsKey($adapter.InterfaceGuid.ToString())) {
                    @($descriptorsByGuid[$adapter.InterfaceGuid.ToString()])
                }
                else {
                    @()
                }

                $changed = (Set-ExplicitHostAddress -Adapter $adapter -Descriptors $descriptors) -or $changed
                $properties = @(Get-NetAdapterAdvancedProperty -Name $adapter.Name -AllProperties -ErrorAction Stop)
                $changed = (Set-AdvancedPropertyChoice -AdapterName $adapter.Name -Properties $properties -Label 'Energy Efficient Ethernet' -RegistryPattern '(?i)^\*?EEE$|EnergyEfficient|GreenEthernet|GigaLite' -DisplayPattern '(?i)energy.*efficient|green ethernet|gigalite' -Choice Off) -or $changed
                $changed = (Set-AdvancedPropertyChoice -AdapterName $adapter.Name -Properties $properties -Label 'Interrupt Moderation' -RegistryPattern '(?i)^\*?InterruptModeration$' -DisplayPattern '(?i)^interrupt moderation$|moderazione interrupt' -Choice On) -or $changed
                $changed = (Set-AdvancedPropertyChoice -AdapterName $adapter.Name -Properties $properties -Label 'Interrupt Moderation Rate' -RegistryPattern '(?i)InterruptModerationRate|^ITR$' -DisplayPattern '(?i)interrupt moderation rate|velocita.*moderazione' -Choice Extreme) -or $changed
                $changed = (Set-AdvancedPropertyChoice -AdapterName $adapter.Name -Properties $properties -Label 'Jumbo Packet' -RegistryPattern '(?i)^\*?Jumbo(Packet|Frame)' -DisplayPattern '(?i)jumbo' -Choice Maximum -MinimumNumericValue 9000) -or $changed
                $changed = (Set-AdvancedPropertyChoice -AdapterName $adapter.Name -Properties $properties -Label 'Receive Buffers' -RegistryPattern '(?i)^\*?ReceiveBuffers$|RxDescriptors' -DisplayPattern '(?i)receive buffers|buffer.*ricezione' -Choice Maximum) -or $changed
                $changed = (Set-AdvancedPropertyChoice -AdapterName $adapter.Name -Properties $properties -Label 'Transmit Buffers' -RegistryPattern '(?i)^\*?TransmitBuffers$|TxDescriptors' -DisplayPattern '(?i)transmit buffers|buffer.*trasmissione' -Choice Maximum) -or $changed
                $changed = (Set-AdvancedPropertyChoice -AdapterName $adapter.Name -Properties $properties -Label 'Maximum RSS Queues' -RegistryPattern '(?i)^\*?NumRssQueues$|MaximumNumberOfRSSQueues' -DisplayPattern '(?i)rss.*queue|code.*rss' -Choice Maximum) -or $changed
                $changed = (Set-DedicatedBindings -AdapterName $adapter.Name) -or $changed
                $changed = (Set-AdapterPowerManagement -AdapterName $adapter.Name) -or $changed

                if ($changed) {
                    Restart-NetAdapter -Name $adapter.Name -Confirm:$false -ErrorAction Stop
                }
                $configured.Add([pscustomobject]@{
                    Adapter = $adapter.Name
                    InterfaceDescription = $adapter.InterfaceDescription
                    Status = if ($warnings.Count -gt $adapterWarningsBefore) { 'ConfiguredWithWarnings' } else { 'Configured' }
                    Changed = $changed
                })
            }
            catch {
                Add-Warning "$($adapter.Name): configurazione incompleta: $($_.Exception.Message)"
                $configured.Add([pscustomobject]@{
                    Adapter = $adapter.Name
                    InterfaceDescription = $adapter.InterfaceDescription
                    Status = 'Failed'
                    Changed = $changed
                })
            }
        }

        $incompleteCount = @($configured | Where-Object {
            $_.Status -notin @('Configured', 'ConfiguredWithWarnings')
        }).Count
        $result.status = if ($incompleteCount -eq 0) { 'Configured' } else { 'Partial' }
        Add-Warning 'Windows Firewall e impostazioni globali del piano energia non sono stati disabilitati automaticamente. Verificarli con il Cognex GigE Vision Configuration Tool durante il commissioning.'
    }
}
catch {
    $result.status = 'Failed'
    Add-Warning "Configurazione rete Cognex fallita: $($_.Exception.Message)"
}
finally {
    $result.configuredAdapters = @($configured)
    $result.skippedAdapters = @($skipped)
    $result.warnings = @($warnings)
    $result.completedAtUtc = [DateTime]::UtcNow
    Write-JsonAtomically -Path $ResultPath -Value $result
}

if ($result.status -eq 'Failed') {
    exit 1
}

exit 0
