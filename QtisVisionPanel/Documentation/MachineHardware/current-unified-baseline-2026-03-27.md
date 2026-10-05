# Current Unified Baseline 2026-03-27

## Goal

Document the current merged baseline of `QtisVisionPanel` after aligning:

- the existing strong runtime logic of the current main project
- the mature `DataInspector` and documentation improvements coming from the colleague baseline
- the current configuration recovery services and runtime safety work already present in this branch

## Canonical baseline rule

The canonical baseline is the current main-project structure of this repo.

This means:

- preserve the current runtime logic
- preserve the current `Config.xml` contract
- keep path resolution config-driven
- do not introduce forced folder standardization as architectural policy

Any imported improvement must adapt to this rule.

## Kept from the current branch

- current shell structure and runtime logic
- current recipe save / reload flow
- current config recovery stack:
  - `Services\ConfigurationRecoveryService.cs`
  - `Database\ConfigSnapshotRepository.cs`
- current diagnostics, inactivity handling, alarms, save-image and runtime improvements
- current `Preferences` direction and localized additions already present in this branch

## Imported from the colleague baseline

### Native DataInspector

Imported into this branch:

- `Inspector\Abstractions`
- `Inspector\Models`
- `Inspector\Services`
- `Inspector\ViewModels`
- native `Views\UserControls\DataInspectorView.xaml`
- native `Views\UserControls\DataInspectorView.xaml.cs`

The imported native inspector is valid for this baseline because it already reads:

- database connection from `MainWindow.configManager.Config.MySqlConnection`
- image path from `MainWindow.configManager.Config.Configuration.ImageDir`
- `DataHostnames` from the same main configuration

So it does not require a different runtime root.

### DB compatibility improvement

Imported concept:

- `tblproduzione.TimeFine` must be nullable

Applied in this branch:

- create table definition now uses `NULL DEFAULT NULL`
- insert path now writes `DBNull.Value`
- migration path updates legacy `0000-00-00 00:00:00` to `NULL`

This keeps the current DB logic but removes an incompatibility that blocks more mature read paths.

### Documentation flow

Imported into this branch:

- `README.md`
- `AGENTS.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation\MachineHardware\...`

Adapted to this branch:

- docs now explicitly state that runtime logic remains config-driven
- current repo assumptions are canonical
- imported documents are considered supporting/historical unless explicitly referenced by the current canonical docs

## Non-canonical imported assumptions

Some imported documents may mention:

- `C:\QtisVision\Pieces\` as a standardized root
- other local repo paths
- intermediate baseline decisions

Those references are not automatically canonical for this branch.

The canonical rule remains:

- follow the actual values configured in the current `Config.xml`

## Current integration seam

The current mature integration path is:

1. keep production HMI runtime stable
2. import only mature, already-validated slices
3. document every merged slice
4. mirror the same merged baseline into the teammate repo after validation

## Developer guidance

When adding future features:

- use the current branch runtime logic as baseline
- treat imported colleague work as source of mature slices, not as automatic source of truth
- update docs in the same turn when changing DB, XML, runtime or operator behavior

## Tester guidance

When validating this merged baseline, verify at minimum:

1. recipe load/save/duplicate
2. VisionPro run/stop/recovery
3. alarm/event logging
4. image save pipeline
5. native `DataInspector`
6. config recovery and config save behavior

## Files to read first

- `README.md`
- `AGENTS.md`
- `Documentation/MachineHardware/main-project-datainspector-configuration-and-usage.md`
- `Documentation/MachineHardware/baseline-build-restoration-2026-03-26.md`
