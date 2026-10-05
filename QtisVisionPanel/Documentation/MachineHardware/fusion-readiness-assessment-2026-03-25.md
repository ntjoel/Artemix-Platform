# Fusion Readiness Assessment 2026-03-25

## Purpose

This note records the current readiness of the refreshed local `QtisVisionPanel` baseline to receive future fusion work from the standalone companions:
- `QuatisVisionPanelIO`
- `QuatisVisionInspector`

The goal of this note was to confirm whether the main project was prepared as a stable reference point before starting real fusion work.

That first real fusion work has now started for the Inspector area and is recorded in:
- [inspector-native-fusion-slice-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-native-fusion-slice-2026-03-26.md)

## Current Local Reference

Active references:
- repo: `C:\Users\chouikha\QtisVisionPanel`
- runtime assets: `C:\QtisVision`
- image evidence root: `C:\QtisVision\Pieces\`

## What Is Ready

### Governance and Process

The refreshed repo now contains the local working layer again:
- `AGENTS.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation\MachineHardware\*`

This means future work can continue with the same boundaries, risk model, and documentation discipline already used in the standalone tools.

### Runtime Context for Future Fusion

The project already exposes useful seams that make future fusion realistic:
- `ServiceLocator.cs`
- `Services\MachineRuntimeService.cs`
- `Services\ApplicationEventLogger.cs`
- `Services\ViewFactoryService.cs`

These are not the final fusion layer by themselves, but they are the correct kind of application seams to connect future machine and inspector functionality without placing everything inside `MainWindow`.

### Recipe and Configuration Boundaries

The project still preserves the right conceptual split:
- recipe data remains centered in `Cls_Config\Calss_structure\RecipeParameters.cs`
- machine save-image/runtime settings remain in the external runtime config
- at the time of this assessment, the legacy DataInspector flow was still only a wrapper bridge and not a native internal subsystem

This is good for fusion because it means the standalone tools do not need to be "unwound" from an already-overbuilt competing module.

### Image-Save Baseline

The image-save root is now aligned across:
- `C:\QtisVision\cfg\Config.xml`
- `C:\QtisVision\cfg\cfg\QtisPanel.ini`
- `C:\QtisVision\cfg\cfg\QtisFolderPanel.ini`
- `C:\QtisVision\cfg\PreferenceViewConfig.xml`
- `Cls_Config\AsyncPreferenceConfigManager.cs`
- `Cls_Config\Calss_structure\ConfigClassStructure.cs`

This gives `QuatisVisionInspector` a stable local target for DB-to-disk validation.

## What Is Not Yet Ready

### The Repo Is Not Build-Clean

The refreshed baseline is not currently in a compile-clean state.

Observed issues during local build include:
- missing generated XAML symbols
- many `InitializeComponent`-related failures
- missing named controls referenced from code-behind
- entry-point/build-generation problems

This does **not** come from the documentation/governance refresh work.
It appears to be part of the delivered state of the refreshed project.

This means:
- the repo is valid as a structural and architectural reference
- but it is not yet safe to treat it as a fully verified build baseline

### MainWindow Is Still Too Central

`MainWindow.xaml.cs` still carries too much responsibility:
- initialization
- runtime wiring
- camera/display references
- save-image orchestration
- broader system flow decisions

This does not block future fusion planning, but it means the actual integration work will need discipline to avoid adding more weight there.

### Legacy Mixed Persistence Layer

The database area still mixes:
- schema initialization
- inserts
- read-side behavior
- application logic assumptions

This is especially visible around:
- `Database\Cls_InitializzeDb.cs`
- older DB insert patterns

The future inspector fusion should continue to treat the DB as a contract boundary, not as a place to mirror old coupling.

## Readiness By Fusion Area

### I/O / Machine Runtime Fusion

Readiness: **medium**

Why:
- boundaries are documented
- standalone I/O contracts are prepared
- the main project has service seams
- but hardware fusion should still wait for real-machine validation of `QuatisVisionPanelIO`

### Inspector / Data Review Fusion

Readiness: **medium-high**

Why:
- at assessment time, the main project only wrapped an external DataInspector page
- the standalone inspector already exists as a native replacement path
- DB and image-folder seams are known
- local image storage is now standardized

This area is structurally easier to fuse than the machine runtime area.

### Localization Fusion

Readiness: **medium**

Why:
- the main project has an existing message-key model
- the standalone I/O tool has already been aligned semantically
- but the current main-project language system still has legacy inconsistencies and external-file coupling

## Recommended Next Order

1. continue validating `QuatisVisionInspector` against the real DB and `C:\QtisVision\Pieces`
2. continue real hardware validation of `QuatisVisionPanelIO`
3. keep using `QtisVisionPanel` as the architecture and contract reference
4. do not start real fusion until:
   - inspector live validation is solid
   - I/O hardware validation is solid
   - the main project baseline is either made build-clean or at least understood well enough not to hide regressions

## Conclusion

The refreshed `QtisVisionPanel` is now ready to serve as the documented local reference baseline for future fusion.

It is **not** yet ready to be called a clean integration baseline from a build perspective, but it is ready from a:
- documentation
- process
- structure
- seam-identification
- runtime-path-consistency

point of view.
