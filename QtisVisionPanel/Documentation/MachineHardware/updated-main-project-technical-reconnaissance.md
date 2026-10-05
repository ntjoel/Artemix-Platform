# Updated Main Project Technical Reconnaissance

## Scope

This document records the technical reconnaissance of the refreshed `QtisVisionPanel` baseline now located at:
- `C:\Users\chouikha\QtisVisionPanel`

The purpose is to understand the updated application structure before preparing UI and structural changes on `QuatisVisionPanelIO` and before planning the later machine/tool fusion.

This is not a migration document for code changes. It is a baseline-reading note.

## Baseline summary

The refreshed baseline remains a classic `.NET Framework 4.8` WPF application, but it is now broader than a pure HMI shell. It includes:
- application code
- operator documentation
- engineering documentation
- runtime asset update scripts
- explicit logging configuration

The application root now includes these relevant top-level areas:
- `Views`
- `ViewModels`
- `Models`
- `Services`
- `Cls_Config`
- `ServerMessage`
- `Docs`
- `Documentation`
- `scripts`
- `Nlog.config`

## Areas that matter for future integration

### 1. Operator documentation is now part of the delivered product surface

The folder:
- `Docs\Manual`

contains operator-facing manual content.

This matters because the project is no longer only an engineering runtime. It now has a formal operator-facing documentation layer that should stay separate from machine commissioning material.

Practical consequence:
- operator documentation belongs in `Docs`
- machine/tool integration guidance belongs in `Documentation\MachineHardware`

### 2. Machine integration guidance exists but is still documentation-only

The folder:
- `Documentation\MachineHardware`

now contains the technical baseline for machine-side integration.

This is good and should continue, because it preserves a safe boundary:
- the main project keeps recipe and production logic stable
- machine/runtime integration is prepared deliberately, not improvised inside views

Practical consequence:
- future machine/runtime work must keep using this folder as the technical landing zone before implementation

### 3. Runtime service composition has improved, but the composition root is still heavy

The refreshed baseline now exposes a clearer service layer through:
- `ServiceLocator.cs`
- `Services\MachineRuntimeService.cs`
- `Services\MachineStatusService.cs`
- `Services\ApplicationEventLogger.cs`

This is useful because the project now has existing seams where future machine/runtime logic can attach.

However, `MainWindow.xaml.cs` is still the real orchestration center for:
- Cognex manager wiring
- run/stop handling
- runtime health
- recovery paths
- camera and recipe initialization

Practical consequence:
- future machine/tool fusion must **not** push more logic into `MainWindow`
- any new machine-cycle behavior must land in dedicated runtime/controller services

### 4. The project now depends operationally on external runtime assets

The refreshed baseline includes:
- `scripts\UpdateRuntimeLanguageFiles.ps1`
- `ServerMessage\ServerMessagePersonalize.cs`

Both point to:
- `C:\QtisVision`

This means the application baseline is coupled not only to its own repo, but also to the external runtime asset tree.

Practical consequence:
- `C:\QtisVision` must now be treated as part of the effective runtime baseline
- future fusion work must always consider both the repo and the external runtime assets together

### 5. Logging has matured

The root file:
- `Nlog.config`

is now explicit and copied as part of the runtime setup.

Practical consequence:
- future machine/runtime diagnostics should integrate with `ApplicationEventLogger` and `NLog`
- avoid ad hoc diagnostics that bypass the new event/logging path

## Existing integration seams worth reusing

### MachineRuntimeService

`Services\MachineRuntimeService.cs` is currently VisionPro-centric, but it is the strongest existing runtime seam for:
- continuous-run state
- health/fault tracking
- recovery hooks
- coordination with `MainWindow`

It is **not yet** a real machine-cycle runtime.

This is important.

Future fusion should build beside or under this layer, not by overloading it with all machine semantics in one step.

### ApplicationEventLogger

`Services\ApplicationEventLogger.cs` is the correct destination for future machine-visible events such as:
- trigger scheduled/executed
- reject scheduled/executed
- board communication faults
- runtime recovery actions
- configuration mismatch or fallback events

This should become the canonical bridge between new machine logic and existing UI diagnostics.

### RecipeParameters

`Cls_Config\Calss_structure\RecipeParameters.cs` still respects the correct boundary fairly well:
- it carries product and inspection settings
- it carries camera trigger delays
- it does **not** carry board/channel mapping or machine geometry

This is good and must be preserved.

### InspectionConfigService and RecipeAutoSwitcher

These services show that the main project already has runtime behavior that reacts to product/machine context without storing it all inside recipe persistence.

That is a useful architectural pattern for future machine/runtime fusion.

## Risks and constraints observed in the refreshed baseline

### MainWindow remains too central

This is the main architectural constraint.

If future machine/runtime fusion is implemented directly in `MainWindow`, the project will become difficult to test, reason about, and stabilize.

### External path usage is inconsistent

The baseline still uses hardcoded external paths with mixed drive assumptions, for example:
- `C:\QtisVision\Language`
- `D:\QtisVision\...`
- package path assumptions in `scripts\UpdateRuntimeLanguageFiles.ps1`

This increases fragility for automation and deployment.

### Vision initialization is duplicated

The refreshed project still appears to initialize or attach runtime/Cognex state in more than one place.

This matters because future encoder/trigger/runtime fusion depends on deterministic initialization order.

### Trigger-delay fallback values are inconsistent

Different fallback values appear in different startup/initialization areas.

This is a real risk for future synchronization with:
- encoder positions
- trigger scheduling
- recipe-specific offsets

### Machine status is still mostly inferred from vision/app state

`MachineStatusService` is useful, but it is not yet a real machine-state model.

Future fusion must not mistake current app/vision health for a complete machine-state representation.

## What this means for the next phase

The refreshed baseline is usable for future fusion work, but only if we respect these rules:

1. keep recipe persistence stable
2. keep machine configuration outside recipe structure
3. extend runtime through dedicated services/controllers
4. reuse `ApplicationEventLogger` for machine-visible diagnostics
5. treat `C:\QtisVision` as part of the actual runtime baseline
6. do not let the tool UI dictate the main-project UI one-to-one

## Immediate preparation value for `QuatisVisionPanelIO`

This reconnaissance clarifies what the standalone tool should prepare **before** any actual fusion:
- shared machine/runtime terminology
- exportable machine-configuration concepts
- event naming compatible with main-project diagnostics
- localization strategy compatible with the refreshed main-project baseline
- documentation that maps tool concepts to the real seams already present in the main application

## Recommended next technical focus

Before any code fusion, the safest next preparatory work is:
1. compare localization architecture between tool and main project
2. define the updated fusion plan against the refreshed baseline
3. identify the UI and structural changes needed in the tool so it aligns semantically with the main project without being embedded into it yet
