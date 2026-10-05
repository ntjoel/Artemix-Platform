# Inspector Native Fusion Slice 2026-03-26

## Goal

Replace the legacy external `DataInspector` bridge with a first native in-HMI slice that reuses the validated standalone inspector contracts without pulling the whole standalone shell into the main project.

## What was fused in this slice

The main project now contains a first internal Inspector layer under:

- `Inspector\Abstractions`
- `Inspector\Models`
- `Inspector\Services`
- `Inspector\ViewModels`

The native `DataInspectorView` has been replaced from a `WebView2` wrapper to a direct WPF user control backed by:

- `Inspector\Services\InspectorProfileFactory`
- `Inspector\Services\MySqlPieceHistoryRepository`
- `Inspector\Services\LocalPieceEvidenceResolver`
- `Inspector\ViewModels\DataInspectorViewModel`

## Why this is the right first slice

This slice is intentionally conservative:

- it keeps the existing menu entry and navigation target name `DataInspector`
- it does not touch recipe flow
- it does not change DB schema
- it does not add coupling to machine runtime
- it stays read-only

This gives the project a real native review surface while preserving the validated standalone product as the richer pre-fusion source of truth.

## Data sources used

The native view reads from the same canonical machine/runtime configuration already used by the main project:

- MySQL settings from `AppConfig.MySqlConnection`
- image root from `AppConfig.Configuration.ImageDir`
- storage host metadata from `AppConfig.Configuration.DataHostnames`

The expected image-save root remains:

- `C:\QtisVision\Pieces\`

## Scope intentionally left out

This first slice does not yet port:

- advanced standalone filtering UI
- dedicated config file loader from the standalone app
- remote storage bridge options
- export/report workflows
- full operator/client wording review inside the main HMI language key system

Those remain part of the next fusion steps after the native read-side proves stable in the main project.

## Build and runtime status

The main-project baseline has now been restored to a successful local build in:

- `Debug | x64`

See:

- [baseline-build-restoration-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/baseline-build-restoration-2026-03-26.md)

This means the native Inspector fusion is no longer just a prepared slice. It is now:

- compiled into the main HMI
- hosted by the standard navigation flow
- built against the restored local baseline

A short executable smoke run also confirmed that the generated HMI starts and stays alive.

Full functional validation of the `DataInspector` navigation target remains the next manual step.

## Next steps after this slice

1. Validate the native inspector view against the local seeded `quatisv` database.
2. Align the native inspector wording with the main-project language key system.
3. Decide which standalone-only features are worth porting after internal testing.
4. Keep the main-project version read-only until production review confirms the UX and traceability flow.
