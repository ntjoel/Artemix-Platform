# QtisVisionPanel

`QtisVisionPanel` is the unified main HMI / vision project baseline.

This repository preserves the current production runtime logic of the panel and progressively absorbs the strongest validated improvements coming from the companion workstreams, without forcing a different runtime layout than the one already driven by `Config.xml`.

## What This Repo Contains

- WPF HMI application
- recipe loading, editing, duplication, persistence and runtime refresh
- camera/runtime integration with VisionPro
- machine status, alarms, diagnostics and recovery services
- native `DataInspector` image-review surface integrated inside the HMI
- MySQL-backed configuration snapshot / recovery services
- operator manual, assistance and documentation flow for future developers and testers

## Runtime Rule

This baseline follows the current runtime configuration semantics already used by the project:

- the runtime asset root is whatever is configured in `C:\QtisVision\cfg\Config.xml`
- image, backup and dashboard paths must remain config-driven
- do not hardcode or standardize runtime folders unless the configuration contract changes explicitly
- if machine runtime is `Stopped` or transitioning, stale queued VisionPro results must be discarded instead of being rendered later into the HMI or counters
- intentional stopped states must be modeled explicitly so automatic resume restarts only unexpected stops, not maintenance or editing pauses
- intentional protected stops must also be visible to the operator in the top bar, so blocked `Start` requests can be understood without opening the event log
- maintenance holds created by `Job Tool Editor` must be released deterministically on page exit and must never remain active as stale blockers on other views
- automatic release of stale maintenance holds must be auditable in the event log so maintainers can verify whether recovery happened during navigation or during a later `Start`
- when automatic stale-hold recovery is logged, the payload JSON should include structured metadata so the event can be filtered and understood without relying only on free-text details
- the counters page must mirror the currently enabled inspections only, so disabled checks do not remain visible to the operator as active defect channels
- long-running operator actions such as recipe load/save and VisionPro tool editing must expose clear progress feedback instead of silent waiting states
- for semantic `Top3D` jobs, the piece-save pipeline should enrich the standard piece folder with 2D render, 3D/range render and point-cloud sidecar artifacts when the configured VisionPro outputs are available

In other words, this repo must respect the current machine configuration first, and only then improve structure, quality and maintainability.

## Current Strong Baseline

This merged baseline intentionally combines:

- the current project strengths:
  - runtime logic already validated on the machine
  - MySQL configuration recovery
  - current recipe/runtime integration
- the colleague baseline strengths:
  - native in-HMI `DataInspector`
  - richer documentation flow
  - improved database compatibility around `tblproduzione.TimeFine`
  - more mature project hygiene around build and fusion notes

## Read This First

Before changing the project, read:

- `AGENTS.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation/MachineHardware/README.md`
- `Documentation/MachineHardware/integration-architecture-and-code-guide-2026-03-27.md`
- `Documentation/MachineHardware/current-unified-baseline-2026-03-27.md`
- `Documentation/MachineHardware/repo-alignment-execution-plan-2026-03-27.md`
- `Documentation/MachineHardware/developer-and-tester-onboarding-2026-03-27.md`
- `Documentation/MachineHardware/software-version-archive.md`

## Key Areas

- Native Inspector:
  - `Views/UserControls/DataInspectorView.xaml`
  - `Views/UserControls/DataInspectorView.xaml.cs`
  - `Inspector`
- Configuration recovery:
  - `Services/ConfigurationRecoveryService.cs`
  - `Database/ConfigSnapshotRepository.cs`
  - `Cls_Config/AsyncConfigManagerXml.cs`
  - `Cls_Config/AsyncRecipeParam.cs`
- Database initialization:
  - `Database/Cls_InitializzeDb.cs`
- Top3D evidence capture:
  - `SaveImage/ISaveImage.cs`
  - `Documentation/MachineHardware/top3d-machine-configuration-guide-2026-04-13.md`
  - `Documentation/MachineHardware/top3d-l38-300-visionpro-panel-integration-2026-04-21.md`
- Shared language system:
  - `ServerMessage/ServerMessageStructure.cs`
  - `scripts/UpdateRuntimeLanguageFiles.ps1`

## Documentation Families

The project now keeps three parallel documentation tracks:

- AI-oriented guidance:
  - `AGENTS.md`
  - machine-hardware fusion notes
- Human-oriented operational documentation:
  - `Docs\Manual\00_Documentazione_Tecnica_Globale.md` as the first in-HMI guide for operators, maintainers and first-time users
  - `Docs\Manual\README.md` as the manual index by role and task
  - the remaining `Docs\Manual` pages for focused operator procedures
  - assistance / operator-facing view documentation
- Technical developer and commissioning documentation:
  - `Documentation\MachineHardware\README.md` as the technical entry point
  - fusion notes, build baseline notes, DB alignment notes and runtime assumptions

The in-application Manual page loads Markdown files from `Docs\Manual`, so operator-facing changes should be documented there first. Technical behavior changes should also be recorded in `Documentation\MachineHardware\code-changes-log.md`.

## Deployment Notes

This baseline includes:

- `app.manifest`
- `QTisPanel.ico`

The manifest requests administrator privileges at launch, so elevation remains part of the runtime/deployment contract of the application.

## VisionPro Compatibility Baseline

The current project baseline expects the Cognex VisionPro installation already configured through the standard local path:

- `C:\Program Files\Cognex\VisionPro`

As of the latest compatibility alignment performed on `2026-04-17`:

- Cognex official documentation shows `VisionPro 9.25 SR1` as the latest documentation baseline checked for this project
- the local `Cognex.VisionPro*.dll` referenced by the project resolve to assembly version `93.0.0.0`
- the local `Cognex.Vision.*.Net.dll` support assemblies resolve to assembly version `9.25.0.0`

The project file has been aligned to that installed Cognex baseline without changing runtime paths or introducing hardcoded machine-specific alternatives.

## Shutdown Reliability

The production baseline now treats application shutdown as an industrial runtime concern, not just a window-close event.

The shutdown sequence explicitly:

- stops the VisionPro runtime
- disposes long-lived workers and singleton services
- releases the global Cognex runtime
- applies a fail-safe forced-process termination only if native resources keep the process alive beyond the shutdown grace timeout

## Runtime XML Reliability

The main configuration XML and the recipe XML now use a canonical UTF-8 write path.

The runtime also attempts a local self-heal before falling back to MySQL recovery when it finds a legacy XML file that is readable as text but has an invalid encoding declaration after an update.

## Trigger Delay Runtime Semantics

For the production baseline, `TriggerDelay` remains recipe-driven:

- `cameraSetting.TopCameraTriggerDelay`
- `cameraSetting.SideCameraTriggerDelay`
- `cameraSetting.FrontCameraTriggerDelay`

When the active recipe is loaded or saved in production, the runtime now applies trigger delay through a controlled DALSA sequence:

- stop acquisition job state
- flush pending job and manager queues
- apply the new hardware delay
- read back the value from the camera for verification

The hardware readback is used for runtime verification only and no longer writes back into the recipe file implicitly.

For full recipe changes, the runtime now also tears down the active VisionPro manager and rebuilds it only once before returning to `RunContinuous`, to avoid stale camera/job state crossing the reload boundary.

## Camera Role-Driven Runtime

The baseline is no longer limited to a fixed `Top + Side` assumption.

The runtime now combines:

- `CameraConfig.xml` as the semantic role contract (`Top`, `Side`, `Front`, ...)
- the active `.vpp` job set as the real runtime availability contract

This means:

- only the camera panels backed by the active VPP are shown in the overview
- the secondary inspection role can now be `Side` or `Front`
- on `Top + Front` machines, the front camera performs traceability presence/code validation while the top camera continues the product inspection path
- when the QuickBuild job name is already explicit (`Top`, `Front`, `Side`, ...), that semantic role now has priority over the numeric ID order found in `CameraConfig.xml`
- `CameraConfig.xml` stays as a fallback semantic contract, but a stale camera ID assignment no longer silently forces the wrong runtime view

The same runtime-first rule now also drives the `Job Tool Editor`:

- editor tabs are generated from the active QuickBuild jobs, not from a fixed `Top/Side` assumption
- if the loaded `.vpp` exposes `Top + Front`, the editor shows both jobs directly
- live preview and tool loading now follow the selected runtime job instead of a fixed tab index contract

The first front-camera recipe/config contract now includes:

- `recipeParamFront.RequireTraceability`
- `recipeParamFront.ExpectedCodePrefix`
- `Configuration.FrontTraceabilityPresenceOutput`
- `Configuration.FrontTraceabilityCodeOutput`

For `3DCheck` machines, the baseline now also supports a dedicated `Top3D` profilometer contract:

- QuickBuild semantic job role `Top3D`
- display rendered on the standard `TopCameraView`
- recipe thresholds stored in `recipeParamTop3D`
- recipe archive stored in MySQL column `RecipeParamerterTop3D`
- 3D result persistence inside `tblgenerale`

The MySQL recipe archive therefore stores both:

- `RecipeParamerterFront` for front traceability recipes
- `RecipeParamerterTop3D` for the dedicated Top3D profilometer recipe section

The effective inspection set used by validators, counters and camera feature panels is resolved at runtime from four sources together:

- recipe XML `ejectionStatus`
- semantic job roles exposed by the active `.vpp`
- outputs really available in the active toolblocks
- recipe XML `inspectionViewConfiguration`, when explicitly configured

This keeps the runtime distinction explicit:

- if the active top job is `Top`, the standard top inspections are evaluated
- if the active top job is `Top3D`, only the 3D profilometer inspections are evaluated

The same runtime feature map is also used to keep the operator-facing counters and camera feature panels clean, so disabled or unsupported checks are not shown as if they were active.

When an active production recipe is loaded, the `Inspection Configuration` page edits the camera/inspection profile of that recipe instead of overwriting the broader machine defaults. Camera acquisition uses the existing recipe `machineRuntimeAdjustments.CameraTriggers` modes, so trigger points, expected VisionPro results and camera panels share one source of truth:

- if a defect counter is hidden, check the active recipe `ejectionStatus`
- if a defect counter is hidden, check the active recipe `inspectionStatus`
- `Machine` inherits the global camera trigger setting, `Enabled` forces it for the recipe and `Disabled` removes that camera from trigger, pairing, watchdog and display expectations
- TOP/TOP3D remains the required primary role; a TOP-only recipe is supported
- saving is allowed only while stopped and with no tracked product in flight, and stores a complete per-view matrix plus accepted Classify labels in the recipe XML
- resetting a recipe removes its explicit matrix and returns it to machine-default fallback

Legacy recipe XML files remain compatible: while `inspectionViewConfiguration.IsConfigured` is false, the runtime continues to use `cfg_inspection_view` and `cfg_view_classification` as machine defaults. Once a recipe profile is saved, missing cells are intentionally disabled so future inspections cannot become mandatory on an already qualified recipe.

The recipe contract now separates two concepts that were historically coupled:

- `inspectionStatus`: the inspection is active, validated and visible to the operator
- `ejectionStatus`: the defect is reject-enabled and may command product expulsion

This means the software can now manage informational/process-monitoring inspections without forcing every enabled inspection to reject the product.

## Extended PC Diagnostics

The PC diagnostics module now covers:

- CPU and RAM utilization
- mounted-disk usage and automatic image cleanup
- MySQL archive-table growth with configurable retention and batched cleanup
- CPU, motherboard/system, memory and disk temperatures through the optional
  `LibreHardwareMonitorLib` provider
- automatic fallback to the existing Windows WMI disk and ACPI thermal-zone
  readers when the provider or a specific sensor is unavailable

The runtime contract remains driven by `AppConfig.SystemDiagnostics` inside `Config.xml`.
Temperature scans run in the diagnostics background refresh, are cached and never
participate in the inspection, camera, encoder or reject path.

## Software Version Popup

The company logo in the left navigation panel now opens the software information popup.

That window is the operator-facing entry point for:

- official software version
- official release date of the current version
- last deployed software-update timestamp
- loaded module versions such as VisionPro and WebView2
- current runtime targets from `Config.xml`
- direct access to the version archive document

## Release Increment Rule

Every software update that changes runtime behavior, UI, integration, configuration or database expectations must generate a new incremental software release.

Canonical release-bump script:

- `scripts\IncrementSoftwareRelease.ps1`

Default policy:

- increment the last version segment (`Revision`)
- use decimal rollover instead of growing the segment to two digits
  - `1.1.0.9 -> 1.1.1.0`
  - `1.1.9.9 -> 1.2.0.0`
- update `Properties\AssemblyInfo.cs`
- update `Documentation\MachineHardware\software-version-archive.md`
