param(
    [Parameter(Mandatory = $false)]
    [string]$ScenePath,

    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ScenePath)) {
    $ScenePath = Join-Path $scriptDirectory '..\ProductionLine.factoryio'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $scriptDirectory '..\Docs'
}
$scenePathResolved = (Resolve-Path $ScenePath).Path
[xml]$scene = Get-Content -Raw -Encoding UTF8 $scenePathResolved
$drivers = $scene.FactoryIO.Drivers

$signalsByKey = @{}
$signalNodes = $scene.SelectNodes('//*[self::BinaryInput or self::BinaryOutput or self::IntInput or self::IntOutput or self::AnalogueInput or self::AnalogueOutput]')
foreach ($signal in $signalNodes) {
    if (-not [string]::IsNullOrWhiteSpace($signal.Key)) {
        $signalsByKey[$signal.Key] = $signal
    }
}

$rows = foreach ($driver in ($drivers.ChildNodes | Where-Object NodeType -eq Element)) {
    foreach ($mapping in ($driver.ChildNodes | Where-Object { $_.Name -match '^(Bit|Numeric|Float|Int)(Input|Output)\d+$' })) {
        $signal = $signalsByKey[$mapping.PointIOKey]
        if ($null -eq $signal) { continue }

        $channel = [regex]::Match($mapping.Name, '\d+$').Value
        [pscustomobject]@{
            Driver       = $driver.Name
            ChannelType  = $mapping.Name -replace '\d+$', ''
            Channel      = [int]$channel
            SignalType   = $signal.LocalName
            SceneAddress = [int]$signal.Address
            Name         = [string]$signal.Name
            Key          = [string]$signal.Key
        }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$csvPath = Join-Path $OutputDirectory 'factoryio-tag-map.csv'
$rows | Sort-Object Driver, ChannelType, Channel | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8

$configuredDrivers = $rows | Group-Object Driver | Sort-Object Name
$signalCounts = $signalNodes | Group-Object LocalName | Sort-Object Name
$summaryPath = Join-Path $OutputDirectory 'factoryio-tag-summary.md'
$summary = @(
    '# Factory I/O tag export'
    ''
    ('- Source scene: `{0}`' -f [IO.Path]::GetFileName($scenePathResolved))
    "- Scene saved: $($scene.FactoryIO.Year)-$($scene.FactoryIO.Month)-$($scene.FactoryIO.Day)"
    ('- CurrentDriver value: `{0}`' -f $drivers.CurrentDriver)
    "- Total scene signals: $($signalNodes.Count)"
    ''
    '## Scene signal counts'
    ''
    '| Type | Count |'
    '|---|---:|'
)
$summary += @($signalCounts | ForEach-Object { "| $($_.Name) | $($_.Count) |" })
$summary += @(
    ''
    '## Drivers with saved channel mappings'
    ''
    '| Driver | Mapped signals |'
    '|---|---:|'
)
$summary += @($configuredDrivers | ForEach-Object { "| $($_.Name) | $($_.Count) |" })
$summary += @(
    ''
    'The scene contains channel mappings for `SiemensS7PLCSIM`. The `ModbusTCPServer` configuration has no channel mappings yet. To control the scene directly from WinForms over Modbus TCP, switch the Factory I/O driver and configure its I/O point counts and tag mappings first.'
    ''
    'See [factoryio-tag-map.csv](factoryio-tag-map.csv) for all mapped tags. `SceneAddress` is the internal scene address; `Channel` is the actual channel number used by the configured driver.'
)
$summary | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "Exported $($rows.Count) mapped tags to $csvPath"
Write-Host "Summary written to $summaryPath"
