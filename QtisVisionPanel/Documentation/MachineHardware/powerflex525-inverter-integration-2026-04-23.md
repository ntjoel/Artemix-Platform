# PowerFlex 525 Inverter Integration - 2026-04-23

## Scope

This integration adds a guarded machine-level page for the conveyor inverter. It belongs to machine configuration and runtime diagnostics, not to recipe data.

The runtime remains non-blocking:

- missing IP address does not stop the HMI;
- unreachable inverter does not stop production;
- failed database history writes are logged and skipped;
- actual parameter communication is isolated behind `IPowerFlex525Client`.

## Rockwell Communication Direction

For PowerFlex 525, the reliable integration path is EtherNet/IP explicit messaging for parameter reads/writes. The implementation keeps this behind `PowerFlex525EtherNetIpClient` so the HMI can be validated safely before a plant-approved CIP adapter is enabled.

The current safe client checks the EtherNet/IP endpoint on TCP `44818` and reports parameter data as unavailable until the validated CIP parameter adapter is connected.

## Config.xml Additions

New optional section:

```xml
<PowerFlex525>
  <Enabled>true</Enabled>
  <IpAddress></IpAddress>
  <TimeoutMs>1000</TimeoutMs>
  <PollIntervalMs>3000</PollIntervalMs>
  <HistoryFilePath>D:\QtisVision\Logs\PowerFlex525ChangeHistory.json</HistoryFilePath>
  <MetersPerMinutePerHz>0</MetersPerMinutePerHz>
  <DrivePulleyDiameterMm>0</DrivePulleyDiameterMm>
  <GearRatio>1</GearRatio>
</PowerFlex525>
```

Related machine startup option in `Configuration`:

```xml
<Configuration>
  ...
  <AutoLoginAdministratorForDemo>false</AutoLoginAdministratorForDemo>
</Configuration>
```

When this flag is `true`, the panel starts directly as `Administrator`. This mode is intended for supervised demo/fair operation and should be disabled again for normal production.

Speed in `m/min` is calculated in this order:

- `b001 * MetersPerMinutePerHz`, when the direct collaudo factor is configured;
- otherwise from `b001`, `P032`, `P036`, `DrivePulleyDiameterMm` and `GearRatio`;
- otherwise from the live `b001` frequency as a temporary commissioning fallback so the top bar and PowerFlex page never stay frozen at `0` / `--` while conversion data is still missing.

The `PowerFlex 525` page now also lets the operator save the three machine conversion values directly into `Config.xml`:

- `MetersPerMinutePerHz`
- `DrivePulleyDiameterMm`
- `GearRatio`

## UI and Permissions

Navigation path:

- `Setting -> PowerFlex 525`

Allowed roles:

- `Administrator`
- `Installer`
- `Expert`

Feature key:

- `powerFlex525View`

## Tracked Parameters

Monitor:

- `b001` Output Frequency
- `b003` Output Current
- `b004` Output Voltage
- `b005` DC Bus Voltage
- `b017` Output Power

Motor:

- `P031` Motor NP Volts
- `P032` Motor NP Hertz
- `P033` Motor OL Current
- `P034` Motor NP FLA
- `P035` Motor NP Poles
- `P036` Motor NP RPM
- `P037` Motor NP Power

Ramps and limits:

- `P041` Accel Time 1
- `P042` Decel Time 1
- `P043` Minimum Freq
- `P044` Maximum Freq

Modified group:

- exposed in the UI through `ModifiedParameters`
- remains empty until the validated CIP adapter returns the drive's Modified M group

Stop-required parameters:

- `P031`
- `P032`
- `P036`
- `P043`
- `P044`

## Persistence

Local JSON:

- path from `PowerFlex525.HistoryFilePath`
- last 200 entries retained by the service

Database:

- table `tblpowerflex525_history`
- created by startup DB migration and by the repository as a best-effort fallback

## Implementation Notes

Main files:

- `Models/PowerFlex525Models.cs`
- `Services/IPowerFlex525Client.cs`
- `Services/PowerFlex525EtherNetIpClient.cs`
- `Services/PowerFlex525ParameterCatalog.cs`
- `Services/PowerFlex525Service.cs`
- `ViewModels/PowerFlex525ViewModel.cs`
- `Views/UserControls/PowerFlex525View.xaml`
- `Database/PowerFlex525ChangeHistoryRepository.cs`

The top bar speed display now reads `ServiceLocator.PowerFlex525Service.MetersPerMinuteText` instead of generating a random simulated speed.

## Diagnostics Added In 1.1.4.2

The PowerFlex view now shows a dedicated connection status:

- `EtherNet/IP OK` when TCP `44818` is reachable
- `Non raggiungibile` when the endpoint is not reachable
- `EtherNet/IP OK - CIP adapter non collegato` when the drive endpoint responds but the parameter adapter is not providing values

The same states are logged through NLog with `PowerFlex525` in the message text. Repeated states are throttled to avoid flooding the runtime logs while the page polls.

## CIP Read Prototype Added In 1.1.4.3

The first real explicit messaging path is implemented in `PowerFlex525EtherNetIpClient`:

- EtherNet/IP `RegisterSession`
- EtherNet/IP `SendRRData`
- CIP `Get Attribute Single`
- path `Class 0x0F / Instance parameterNumber / Attribute 0x01`
- EtherNet/IP `UnregisterSession`

The first enabled parameters are monitor-only:

- `b001`
- `b003`
- `b004`
- `b005`
- `b017`

Writes remain disabled. During commissioning, compare `b001` against the drive keypad/CCW and review raw `PowerFlex525 CIP request` / `PowerFlex525 CIP response` log lines.

## Full Read And Fault Events Added In 1.1.4.4

After monitor readback validation, the read path now covers all tracked parameters:

- monitor `b001`, `b003`, `b004`, `b005`, `b017`
- diagnostics `b006`, `b007`, `b008`, `b009`
- motor `P031..P037`
- ramps/limits `P041..P044`

Fault handling:

- `b007` is used as the most recent fault code
- `b008` and `b009` are shown as recent fault history
- non-zero `b007` generates a critical application alarm event through `ApplicationEventLogger`
- repeated events are suppressed until the fault code changes or clears

Writes remain disabled.

## Fault Reset Added In 1.1.4.6

The guarded write path now supports only the drive fault-clear command:

- `A551 = 1` -> reset active fault
- `A551 = 2` -> clear the fault history buffer

This is implemented as a dedicated command path, not as generic free writing of all drive parameters.

Technical notes:

- the CIP client now builds extended parameter paths for instances above `255`
- this is required because `A551 [Fault Clear]` cannot be addressed with the old 8-bit instance segment
- every reset/history-clear command is logged through:
  - NLog
  - local JSON history
  - MySQL `tblpowerflex525_history` when available
