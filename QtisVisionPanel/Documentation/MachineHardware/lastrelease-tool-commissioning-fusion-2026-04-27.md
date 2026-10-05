# lastRelease Tool Commissioning Fusion - 2026-04-27

## Scope

This note records the first complete commissioning/tool fusion performed on the correct colleague baseline:

- source baseline: `S:\Sorgenti e software\VISIONQAI\lastRelease\QtisVisionPanel`
- external mature source: `C:\Users\chouikha\QuatisVisionPanelIO`

The goal of this session was not just to copy a view, but to promote the standalone I/O and encoder commissioning subsystem into the full project without regressing the newer `lastRelease` work.

## What Was Added

The following runtime/tool foundation files were added to the full project:

- `Models\MachineConfigurationBackupInfo.cs`
- `Models\MachineHardwareTemplate.cs`
- `Models\MachineRuntimeConfiguration.cs`
- `Models\ToolFusionSnapshot.cs`
- `Models\ToolMessageCatalog.cs`
- `Models\TrackedProduct.cs`
- `Services\MachineConfigurationService.cs`
- `Services\MachineController.cs`
- `Services\ToolFusionSnapshotService.cs`
- `Services\ToolLocalizationService.cs`
- `Services\ToolMainProjectEventMapper.cs`

The commissioning UI slice was promoted:

- `ViewModels\DigitalIOViewModel.cs`
- `Views\UserControls\DigitalIOControl.xaml`
- `Views\UserControls\DigitalIOControl.xaml.cs`

Follow-up layout hardening:

- the `HW/SIM` connection switch and active connection status were moved into `DigitalIOControl.xaml`
- this keeps the mode bar visible when the commissioning view is hosted inside the full QtisVisionPanel shell, where the standalone tool window chrome is not present
- the bar is compact and sits above the internal commissioning tabs, so the existing full-project top menu and navigation layout remain unchanged

The required runtime assets were added:

- `ConfigurationTemplates\machine_runtime_config.template.xml`
- `Localization\messages_eng.json`
- `Localization\messages_ita.json`
- `Docs\Commissioning\01..07`

The low-level Advantech layer was aligned with the commissioning runtime expected by the promoted UI:

- `Models\AdvantechDeviceManager.cs`
- `Models\IIODeviceManager.cs`
- `Models\IOChannel.cs`
- `Support\TaskExtensions.cs`

## Build Hardening

Two build-specific fixes were necessary on the `lastRelease` baseline:

1. `QtisVisionPanel.csproj`

- the project file was updated to compile the promoted models/services/support files
- the new content assets were included in the project
- the `PostBuildEvent` was hardened so the share build does not fail when the VisionPro junction cannot be created on a network path

2. `MainWindow.xaml.cs`

- an older inline `TaskExtensions` helper already existed at the bottom of the file
- it conflicted with the canonical shared helper required by the promoted commissioning subsystem
- the inline copy was removed and `Support\TaskExtensions.cs` became the single implementation

## Validation

Validation command:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'S:\Sorgenti e software\VISIONQAI\lastRelease\QtisVisionPanel\QtisVisionPanel.sln' `
  /t:Build /p:Configuration=Debug /p:Platform=x64 /m
```

Result:

- `Debug|x64` build green
- output remains warning-heavy, but there are no blocking compile errors after the fusion

## Important Architectural Constraint

The commissioning subsystem is now inside the full project, but it should still be treated as a bounded operational layer:

- configuration and machine diagnostics
- encoder and I/O commissioning
- product tracking foundation for real machine tests

It must not be allowed to silently fork away from:

- `MainWindow` runtime behavior
- VisionPro startup rules
- the newer `lastRelease` modules such as `DataAnalysis` and `PowerFlex525`

## Next Step

Use this `lastRelease` baseline as the source of truth, then sync the local full project to it so both the machine PC and the local workstation continue from the same project state.
