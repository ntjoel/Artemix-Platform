# Local Baseline Change Log 2026-03-27

## Scope

This note records the baseline fixes and alignment work applied on 2026-03-27 after the canonical-merge step.

## 22. Top3D artifact capture for L38-300 piece folders

### Scope

The `Top3D` flow already validated and persisted 3D measurements (`ThreeDHeight`,
`ThreeDWidth`, `ThreeDLength`), but the image-save pipeline still behaved mainly like
the classic top-camera flow.

For the Cognex `L38-300` machine variant we also need engineering evidence that can be
reused after production:

- rendered 2D view
- rendered 3D/range view
- point-cloud export

without creating a different runtime storage root than the current `Config.xml`
contract.

### Main runtime / build changes

- `SaveImage` now detects semantic `Top3D` runtime on the active top job
- the standard piece-folder export adds Top3D sidecar files when the configured outputs exist
- the runtime reads new optional config keys:
  - `Top3DRerenderResultOutput`
  - `Top3DPointCloudOutput`
  - `SaveTop3DRendered2DImage`
  - `SaveTop3DRangeImage`
  - `SaveTop3DPointCloudCsv`
- exported artifacts are written into the existing `Piece_xxxxxxxx` folder:
  - `CH1_<piece>_T3D_2D_A.bmp`
  - `CH1_<piece>_T3D_3D_Range_A.bmp`
  - `CH1_<piece>_T3D_PointCloud.csv`

### Configuration and database impact

- no new runtime root folder
- no recipe XML schema changes
- no MySQL schema changes
- the existing `PieceData` folder path in `tblgenerale` remains the anchor for the new artifacts

### Operator / maintainer impact

- Top3D evidence is now collected in the same place as the normal piece images
- if the VPP does not expose the configured outputs, production is not blocked; the runtime writes warnings and keeps the standard save flow alive
- commissioning is now clearer because the expected VisionPro outputs are documented explicitly for the `L38-300`

## 21. VisionPro reference alignment to installed 9.25 baseline

### Scope

The project already used the correct Cognex installation folders, but the `.csproj`
still declared old `Cognex.VisionPro*` assembly metadata.

This mismatch did not redefine the runtime paths, but it left the project baseline less
clear for developers and maintainers working across multiple machines.

### Main runtime / build changes

- `QtisVisionPanel.csproj` now declares `Cognex.VisionPro*` references with version `93.0.0.0`
- the Cognex `.Net` support assemblies remain referenced through the existing `bin` paths
- no runtime path or machine logic has been changed

### Verification context

- Cognex official documentation portal currently exposes `VisionPro 9.25 SR1`
- local installation folder includes `VPro925x64_installed_productcode.txt`
- local `ReferencedAssemblies\\Cognex.VisionPro*.dll` resolve to assembly version `93.0.0.0`
- local `bin\\Cognex.Vision.*.Net.dll` resolve to assembly version `9.25.0.0`

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- no operator workflow change
- developers and testers now have a clearer Cognex compatibility baseline for this repo

## 20. Structured metadata for automatic Job Tool Editor hold release audit events

### Scope

The explicit audit events for stale `job-editor` hold recovery were already present, but
their diagnostic value was still partly embedded in free text.

For production troubleshooting we also need structured payload information that can be:

- filtered in DB queries
- read quickly in JSON payload viewers
- consumed by AI or future reporting tools

### Main runtime / UI changes

- `JOBEDITOR_HOLD_AUTO_RELEASED_ON_NAVIGATION` now writes:
  - `target_view`
  - `resume_requested`
  - `defensive_release`
  - `had_editor_hold_before_exit`
  - `hold_cleared_by_navigation`
  - `active_holds_before`
  - `active_holds_after`
- `JOBEDITOR_HOLD_AUTO_RELEASED_ON_START` now writes:
  - `current_view`
  - `active_holds_before`
  - `active_holds_after`
  - `released_reason`

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL schema changes

### Operator / maintainer impact

- hold-release troubleshooting is now much clearer directly from the audit JSON payload
- the maintainer can distinguish with less ambiguity whether recovery happened during page exit or during a later start request

## 19. Explicit audit trace for automatic Job Tool Editor hold release

### Scope

The runtime behavior around stale `job-editor` holds was already hardened, but the
event trail still did not show clearly when the recovery had happened.

This made it harder to distinguish:

- a normal editor exit and resume
- a stale editor hold recovered during navigation
- a stale editor hold cleared only when the operator later pressed `Start`

### Main runtime / UI changes

- leaving `Job Tool Editor` can now produce `JOBEDITOR_HOLD_AUTO_RELEASED_ON_NAVIGATION`
- manual `Start` outside the editor can now produce `JOBEDITOR_HOLD_AUTO_RELEASED_ON_START`
- both events are written into the standard audit pipeline and DB-backed event history

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- maintainers can now verify directly from the audit trail whether the editor hold was released during navigation or during a later start request
- troubleshooting no longer requires inferring hold release only from the resulting machine state

## 18. Hotfix for stale Job Tool Editor hold release and restart recovery

### Scope

Even after the first deterministic navigation-exit fix, production logs still showed
cases where:

- the operator entered `Job Tool Editor`
- left the page correctly
- later `Start` remained blocked by `job-editor`

This meant the runtime could still retain a stale editor hold even though the operator
was no longer in the VisionPro editing workflow.

### Main runtime / UI changes

- `JobToolEditorViewModel.ExitEditorPauseAsync(...)` now always attempts to release the global `job-editor` hold
- editor auto-resume now uses `ManageJobStateAsync(true)` instead of a lower-level direct start helper
- `MainViewModel` now releases `job-editor` defensively:
  - when leaving `Job Tool Editor`
  - when the operator presses `Start` from another page

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- after leaving `Job Tool Editor`, the machine should return to `RunContinuous` more reliably
- if the operator is already on another page, `Start` should no longer be blocked by a stale editor hold

## 17. Operator-visible runtime hold indicator in top bar

### Scope

The runtime already distinguished protected stopped states such as:

- manual stop
- recipe save
- job editor
- shutdown

However, that information was only visible in logs and internal diagnostics.
Operators could therefore see `Stopped` while the machine was behaving correctly, without immediately understanding why `Start` might still be blocked.

### Main runtime / UI changes

- `TopMenuBarViewModel` now reads `MachineRuntimeService.ContinuousRunHoldSummary`
- the top status card shows a dedicated runtime-hold pill only when one or more holds are active
- the pill exposes translated hold reasons both in compact text and in tooltip form
- the language system now contains dedicated keys for protected-stop reasons

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- the reason for an intentional protected stop is now visible directly in the top bar
- the operator can distinguish faster between:
  - a normal maintenance/editing stop
  - an abnormal unexpected stopped condition

## 16. Hotfix for deterministic Job Tool Editor hold release on navigation

### Scope

The protected stopped state used by `Job Tool Editor` was originally released through the view
`Unloaded` path only. In practice this was not deterministic enough for production navigation:

- the operator could leave `Job Tool Editor`
- another page such as `RecipeManager` could open correctly
- but the runtime could still carry the `job-editor` hold
- later `Start` requests would then be blocked with `blocked by active hold(s): job-editor`

### Main runtime / UI changes

- `MainViewModel.OnNavigateRequested(...)` now prepares the outgoing view before replacing it
- when leaving `Job Tool Editor`, navigation explicitly:
  - stops live preview if active
  - runs the editor exit path
  - releases the `job-editor` hold before opening the next page

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- switching from `Job Tool Editor` to other pages should no longer leave the machine stuck in the editor maintenance hold
- the protected stopped state remains valid while editing, but it no longer leaks into normal production navigation

## 15. Hotfix for recipe-save runtime holds and verified start completion

### Scope

Production recipe editing exposed a fragile path where:

- `SaveRecipeAsync` could stop continuous run correctly
- the automatic restart could fail or be skipped
- a later manual `Start` could still find a stale `recipe-save` hold
- logs could still show `StartVision - Completed` even while the runtime stayed stopped

### Main runtime / UI changes

- the manual start command now releases the `recipe-save` hold defensively before requesting continuous run
- automatic post-save resume now goes through `ManageJobStateAsync(true)` instead of a low-level direct start call
- start completion is now validated against the real runtime state before writing the final success log
- explicit warning logs are produced when start or post-save resume do not actually reactivate VisionPro

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- production recipe save should return the machine to continuous run more reliably
- if the machine stays stopped, the operator and maintainer now get clearer traces of the blocked/non-effective restart path

## 14. Hotfix for operator progress binding and VisionPro status telemetry

### Scope

Two operator-facing issues were identified after the introduction of the progress-feedback layer:

- the `Job Tool Editor` progress overlay could throw a WPF binding exception because the progress bar was binding with a write-capable mode against read-only viewmodel properties
- event/audit payloads could continue to expose `visionpro_status = Stopped` after VisionPro had already resumed, because the logger preferred a stale cached value instead of the live runtime state

### Main runtime / UI changes

- `Views/JobToolEditorView.xaml` now binds the loading progress bar in explicit `OneWay` mode
- `Services/ApplicationEventLogger.cs` now resolves `visionpro_status` from `MachineRuntimeService` before falling back to the internal logger cache

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- tool load/save feedback in the `Job Tool Editor` no longer risks raising the `TwoWay / read-only property` exception
- event log payloads and diagnostics now reflect the real VisionPro runtime state more reliably after stop/start transitions

## 12. CameraDisplayManager bootstrap hardened after extraction

### Scope

The display-service extraction introduced a startup-order dependency between:

- `Window_Loaded`
- runtime recipe refresh
- vision-system initialization

Some runtime paths rebuilt supported inspection features before `Window_Loaded` had created
`CameraDisplayManager`, causing a `NullReferenceException` in `MainWindow.BuildRuntimeSupportedFeaturesByRole()`.

### Main runtime changes

- `MainWindow.xaml.cs` now owns a single helper `EnsureCameraDisplayManagerInitialized()`
- all thin delegator methods created during the display extraction now use the same lazy bootstrap
- `Window_Loaded` still warms the display service, but it is no longer the only place where the helper can be created

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- startup no longer depends on the exact timing between UI loading and runtime feature-map refresh
- recipe reload and vision reinitialization keep the same functional behavior, but they no longer risk a null-reference crash if the display helper is requested early

### Hotfix 2026-04-14

After the hardening step, the lazy bootstrap helper was corrected once more because a recursive
call had accidentally replaced the real constructor path.

- fixed `MainWindow.EnsureCameraDisplayManagerInitialized()`
- removed infinite self-call
- restored actual `CameraDisplayManager` creation

This hotfix removes the `System.StackOverflowException` seen during startup and preserves the
same lazy-bootstrap contract introduced by the previous step.

## 13. Operator feedback for long-running tool and recipe actions

### Scope

The baseline now provides explicit operator feedback during the three operations that were
previously perceived as "silent waiting states":

- loading a tool inside `Job Tool Editor`
- saving tool changes from the VisionPro editor
- loading or saving a production recipe

### Main runtime / UI changes

- a new `OperationProgressWindow` popup was introduced for long-running operator actions
- `DialogService` can now create and center this popup on the active main shell
- `Job Tool Editor` now exposes a richer in-view overlay with:
  - current action title
  - current step description
  - detail text
  - progress bar / percentage
- recipe load/save now report their stages explicitly, including runtime stop/reload/restart
- the internal recipe reload path (`InitializeComponentforChangeRecipe`) can now update the operator popup while runtime services are being rebuilt

### Configuration and database impact

- no `Config.xml` changes
- no recipe XML changes
- no MySQL changes

### Operator / maintainer impact

- the panel now communicates that work is in progress instead of leaving blank or static content areas
- repeated clicks during long operations should be reduced because the operator sees what the HMI is doing
- visible texts are tracked in `ServerMessage` and runtime language files, so the feedback layer remains part of the localized baseline

## 11. Camera-role runtime extended with Front traceability

### Scope

The machine runtime is no longer limited to the historical `Top + Side` assumption.

The baseline now supports a second validated configuration:

- `Top + Front`

where the `Front` camera is responsible for traceability presence/code validation driven by VisionPro.

### Main runtime changes

- `MainWindow.xaml.cs` now resolves the active secondary inspection role from the runtime job-role mapping
- queue pairing, watchdog heartbeat checks, display update and inspection processing now switch automatically between `Side` and `Front`
- the front camera can update its own runtime panel with:
  - traceability detected / missing
  - code returned by VisionPro
  - expected prefix coming from recipe XML

### Configuration and recipe contract

The front integration stays compatible with the baseline rule:

- machine logic remains machine-side
- product-specific traceability behavior stays in recipe
- runtime state stays runtime-only

New/used recipe nodes:

- `cameraSetting.FrontCameraTriggerDelay`
- `recipeParamFront.RequireTraceability`
- `recipeParamFront.ExpectedCodePrefix`
- `ejectionStatus.FrontTraceability`
- `Counter.FrontTraceability`

New/used config nodes:

- `FrontTraceabilityPresenceOutput`
- `FrontTraceabilityCodeOutput`

### Database alignment

The production/event snapshot already uses:

- `TraceabilityDetected`
- `TraceabilityCode`
- `TraceabilityExpectedPrefix`
- `NC_Traceability`

The recipe metadata archive now also stores:

- `tblproduzione.RecipeParamerterFront`

so the DB copy of the recipe remains aligned with the XML model.

### Structural improvement

`CameraContainerViewModel` now rebuilds the visible camera panels from:

1. `CameraConfig.xml` semantic roles
2. active VPP jobs actually loaded at runtime

This is the first step toward a camera-role-driven backend that makes future integrations less fragile than the historical fixed `Top/Side` branching.

### Supporting maintenance fix

`scripts/IncrementSoftwareRelease.ps1` had a PowerShell parameter-binding issue because `param(...)` was declared after an executable statement.

That was corrected so the canonical release-bump script is now directly usable again.

## 12. QuickBuild job names now take priority over CameraConfig IDs

### Symptom

When a machine loaded a QuickBuild with jobs like:

- `Top`
- `Front`

the HMI could still render:

- `Top`
- `Side`

if `CameraConfig.xml` still contained the historical numeric mapping:

- `0 -> Top`
- `1 -> Side`

### Root cause

The camera-role resolver was still giving too much weight to the `CameraConfig.xml` ID order.

So a valid runtime job on position `1` could inherit the semantic role `Side` from the XML file even if the active VPP job name was already explicit and said `Front`.

### Fix

Role resolution now follows this order:

1. semantic role inferred from the active VPP job name
2. fallback semantic role from `CameraConfig.xml`

Practical consequence:

- if QuickBuild exposes `Top` and `Front`, those roles win
- `CameraConfig.xml` remains a fallback, not the first authority when the job name is already descriptive

### UI consequence

The camera overview now renders the active runtime roles coming from QuickBuild even if `CameraConfig.xml` is stale or incomplete for that role.

The mismatch is logged, but the view is no longer hidden silently.

## 13. Job Tool Editor now follows the active QuickBuild job set

### Symptom

After extending the runtime with `Front`, the `Job Tool Editor` still showed only:

- `TOP Tools`
- `SIDE Tools`

so a valid `Front` job loaded by the active `.vpp` could not be edited from the HMI.

### Root cause

The editor was still hardcoded around the historical two-camera assumption:

- fixed `TopTools` collection
- fixed `SideTools` collection
- fixed `Top` / `Side` tab containers
- live preview and tool lookup driven by a fixed tab index

This was no longer coherent with the runtime baseline, which already supports job-role discovery from the active QuickBuild.

### Fix

The editor now discovers its editable jobs directly from the active `CogJobManager`.

For each active runtime job that exposes a `CogToolGroup`, the HMI creates:

- one tab
- one tool list
- one runtime binding for tool loading and live preview

The tab header now mirrors the real QuickBuild job name, so the operator sees the same job identity in both VisionPro and the HMI.

### Practical consequence

- `Top + Front` machines can edit the `Front` job without any extra hardcoded UI work
- future supported multi-job layouts are easier to integrate because the editor no longer assumes a fixed `Top/Side` pair
- `CameraConfig.xml` remains useful for semantic role context, but it no longer decides which job tabs exist inside the editor

## 1. Startup failure during database initialization fixed

### Symptom

The application could show:

- `Database initialization failed: Index was out of range...`

### Root cause

`MainWindow.InitializeDatabaseAsync()` was assigning:

- `MainWindow._produzioneRecord.Operatore = MainWindow.configManager.Config.Roles[3];`

This assumes that:

- `Roles` exists
- it contains at least 4 items

That assumption is not guaranteed by the current canonical baseline, because the runtime must follow the active `Config.xml`, not a hardcoded role-array contract.

### Fix

The production operator is now resolved safely from:

1. current runtime user session
2. current configured user
3. fallback `Guest`

Files:

- `MainWindow.xaml.cs`
- `ViewModels/TopMenuBarViewModel.cs`

## 2. Operator semantics corrected

The `Operatore` field was being reused inconsistently as if it were a role label.

Examples of wrong historical behavior:

- using role-array entries like `Viewer`
- resetting production operator to a role instead of the active user

The baseline now treats:

- `Operatore` as operator username
- `CurrentUserRole` as role

This avoids silent data pollution in production and event history.

## 3. Repo alignment completed

The following slices were aligned between:

- `D:\Pulsar\Developer\NewPanel\QtisVisionPanel`
- `D:\Pulsar\Developer\QtisVisionPanel`

Aligned areas:

- `DataInspector`
- `Inspector`
- DB bootstrap changes
- message structure
- language update script
- canonical docs

## 4. Duplicate Inspector structure removed in colleague repo

The colleague repo contained a duplicate historical tree:

- `Inspector\Inspector\...`

That duplicate was removed so both repos now expose the same `Inspector\...` layout.

## 5. Canonical documentation made path-neutral

The most important documents now use repo-neutral references instead of hardwiring the current developer path.

This improves:

- portability between the two repos
- AI onboarding
- human onboarding

## 6. Build note

The canonical merge and startup fixes are independent from the long-standing WPF/XAML compile debt still present in the project.

At the moment:

- the startup `Roles[3]` fault is fixed
- the canonical docs and repo alignment are fixed
- some historical WPF/code-behind build issues still require a dedicated cleanup pass

Those remaining issues should be treated as a separate hardening milestone, not as a regression introduced by the 2026-03-27 alignment work.

## 7. DataInspector baseline hardened

### Symptoms

The native `DataInspector` could show two related classes of problems:

- startup/runtime binding noise against `MainViewModel`
- rejected pieces loaded from MySQL, but image evidence not displayed reliably

### Root causes

The baseline had three weak points:

1. `Views/UserControls/DataInspectorView.xaml` inherited the parent `MainViewModel` first and only replaced the view model in `Loaded`, so WPF tried to resolve inspector bindings against `MainViewModel`
2. image-path resolution assumed that `PieceData` was always already in the final local directory format
3. evidence loading and preview creation had no explicit diagnostics when image resolution failed

### Fix

The baseline now:

- binds the view to an internal `InspectorVm` owned by `DataInspectorView`
- initializes that inspector view model without relying on the inherited parent `DataContext`
- normalizes `PieceData` more defensively, including rooted-file to directory and remap-to-current-image-root scenarios
- logs evidence resolution with `DATAINSPECTOR_EVIDENCE_RESOLVED`
- logs preview failures with `DATAINSPECTOR_IMAGE_PREVIEW_FAILED`
- logs evidence-load failures with `DATAINSPECTOR_EVIDENCE_FAILED`
- snapshots the inspection record for `tblgenerale` only after the image-save branch has populated `PieceData`
- falls back to the most recent rejected items with saved images when the latest rejected window contains no evidence

Files:

- `Views/UserControls/DataInspectorView.xaml`
- `Views/UserControls/DataInspectorView.xaml.cs`
- `Inspector/ViewModels/DataInspectorViewModel.cs`
- `Inspector/ViewModels/ImageArtifactViewModel.cs`
- `Inspector/Services/MySqlPieceHistoryRepository.cs`
- `Inspector/Services/LocalPieceEvidenceResolver.cs`
- `Inspector/Models/PieceHistoryQuery.cs`
- `MainWindow.xaml.cs`

## 8. VisionPro auto-recovery and UI freeze hardening

### Symptoms

On machine, when a VisionPro job stopped unexpectedly while continuous mode was still requested, the application could:

- log `VISIONPRO_STALL_DETECTED`
- log `VISIONPRO_AUTO_RECOVERY_START`
- remain visually frozen with no further UI interaction possible

### Root causes

The baseline still had a few high-risk runtime patterns:

1. `CognexManager_JobStopped(...)` synchronously re-read VisionPro run state during the stop event path
2. `ICognexJobManager.IsRunningContinuously` refreshed the Cognex job state on every property read instead of serving a cached state
3. several UI/runtime helpers still fell back to direct `IsRunningContinuously` reads or synchronous `Dispatcher.Invoke(...)`
4. an auto-recipe-switch path executed a stop/search/start sequence inside `Dispatcher.InvokeAsync(async ...)`, forcing long operations onto the UI thread

### Fix

The runtime was hardened as follows:

- `ICognexJobManager.IsRunningContinuously` is now a cached state getter
- the Cognex stopped-event path now marks continuous run inactive immediately without re-querying VisionPro synchronously
- `MachineRuntimeService.SyncStateFromManager()` now skips direct manager reads from the UI thread
- control-button and recipe-management flows now use cached runtime state instead of direct manager reads
- the auto recipe switch no longer performs heavy work inside a dispatcher-owned async lambda
- machine status now avoids oscillating while a continuous-run command is already in progress
- a few remaining UI-thread `Sleep/Wait/Invoke` patterns in runtime-adjacent flows were reduced or removed

### Expected result

With these changes, if a VisionPro job stops unexpectedly:

- recovery stays single-flight
- UI state is updated from cached runtime state
- the auto-recovery can attempt stop/start without freezing the HMI thread
- status transitions are less noisy while recovery is in progress

Files:

- `Services/MachineRuntimeService.cs`
- `Cls_Vpro/ICognexJobManager.cs`
- `MainWindow.xaml.cs`
- `Services/MachineStatusService.cs`
- `ViewModels/ControlButtonsViewModel.cs`
- `ViewModels/RecipeManagerViewModel.cs`
- `ViewModels/JobToolEditorViewModel.cs`

## 9. Integration code documentation reinforced

### Goal

The unified baseline now starts documenting integration logic directly in the source files, so a developer opening the project can understand not only what a class does, but also why the integration was implemented in that way.

### What was added

- XML summaries on core orchestration classes and their key methods
- explicit responsibility notes for runtime, recovery, config and inspector flows
- a dedicated technical guide:
  - `Documentation/MachineHardware/integration-architecture-and-code-guide-2026-03-27.md`

### Main documented classes

- `MainWindow.xaml.cs`
- `ViewModels/MainViewModel.cs`
- `Services/MachineRuntimeService.cs`
- `Services/OperatorInactivityService.cs`
- `Services/ConfigurationRecoveryService.cs`
- `Database/ConfigSnapshotRepository.cs`
- `Cls_Config/AsyncConfigManagerXml.cs`
- `Cls_Config/AsyncRecipeParam.cs`
- `ServiceLocator.cs`
- `Views/UserControls/DataInspectorView.xaml.cs`
- `Inspector/ViewModels/DataInspectorViewModel.cs`

### Documentation rule

Going forward, every new integration slice should update:

1. inline code comments/XML docs
2. this change log
3. the integration architecture guide

## 10. Official software versioning and About popup introduced

### Goal

The baseline did not expose a formal software version to operators and maintainers, and there was no canonical archive file that linked a released version to:

- implemented features
- configuration additions
- database notes

### Fix

The baseline now introduces:

- official version `1.1.0.0`
- assembly/file/informational version alignment in `Properties/AssemblyInfo.cs`
- a new `AboutVersionWindow`
- direct access to that window by clicking the company logo in `NavigationMenu`
- a canonical version archive:
  - `Documentation/MachineHardware/software-version-archive.md`

### Expected result

Operators, testers and maintainers can now:

- identify the currently deployed software version immediately
- review the loaded runtime-module versions such as VisionPro, WebView2, MySQL driver and NLog
- verify the current config/database targets used by the application
- open the version archive and read configuration / DB notes linked to the release

Files:

- `Properties/AssemblyInfo.cs`
- `Views/AboutVersionWindow.xaml`
- `Views/AboutVersionWindow.xaml.cs`
- `Views/NavigationMenu.xaml`
- `Views/NavigationMenu.xaml.cs`
- `ServerMessage/ServerMessageStructure.cs`
- `scripts/UpdateRuntimeLanguageFiles.ps1`
- `Documentation/MachineHardware/software-version-archive.md`
- `README.md`

## 11. Raw image export normalized for mono simulation use

### Goal

Raw images saved for evidence and simulation were not always convenient to reuse directly when the Cognex source image was monochrome.

### Fix

The raw export path in `SaveImage` now:

- detects mono / grey Cognex sources
- saves those raw exports as normalized grayscale bitmaps
- keeps RGB sources unchanged

This keeps simulation-friendly grayscale output for mono images without altering color evidence when the source really is RGB.

Files:

- `SaveImage/ISaveImage.cs`

## 12. Incremental release workflow enforced

### Goal

The unified baseline needed an explicit rule so every software update generates a visible incremental release instead of silently changing behavior under the same version number.

### Fix

The baseline now enforces:

- assembly version bump for each software update
- release tracking in the canonical version archive
- a reusable bump script:
  - `scripts/IncrementSoftwareRelease.ps1`

This update also advances the official version from:

- `1.1.0.0`
- to `1.1.0.1`

Files:

- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `scripts/IncrementSoftwareRelease.ps1`
- `AGENTS.md`
- `README.md`

## 13. About popup now exposes release date and last software update

### Goal

Operators and maintainers could already see the software version, but the popup did not yet expose when that release was created or when the currently deployed executable was last updated.

### Fix

The `AboutVersionWindow` now shows:

- the official release date resolved from `software-version-archive.md`
- the last update timestamp of the deployed software executable

The release archive and the release-bump script were also updated so future versions keep the release-date field in a consistent format.

This update advances the official version from:

- `1.1.0.1`
- to `1.1.0.2`

Files:

- `Views/AboutVersionWindow.xaml`
- `Views/AboutVersionWindow.xaml.cs`
- `ServerMessage/ServerMessageStructure.cs`
- `scripts/UpdateRuntimeLanguageFiles.ps1`
- `scripts/IncrementSoftwareRelease.ps1`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `README.md`

## 14. XML startup recovery hardened for Config.xml and recipe XML files

### Goal

After software updates, startup could log warnings such as:

- `Errore lettura Config.xml, tentativo recovery MySQL: Errore nel documento XML (0, 0).`
- `Errore lettura recipe XML, tentativo recovery MySQL: Errore nel documento XML (0, 0).`

The root cause was an encoding mismatch in the XML write path: some files were serialized with an XML declaration that claimed `utf-16`, but then written to disk with a different encoding.

### Fix

The XML managers now:

- serialize configuration and recipe XML using canonical UTF-8 output
- normalize legacy `utf-16` declarations when a file can still be recovered by text read
- rewrite repaired XML back to disk automatically
- rewrite restored MySQL recovery payloads using the canonical XML format

This update advances the official version from:

- `1.1.0.2`
- to `1.1.0.3`

Files:

- `Cls_Config/AsyncConfigManagerXml.cs`
- `Cls_Config/AsyncRecipeParam.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

## 15. PC diagnostics extended with database archive retention and thermal monitoring

### Goal

The machine needed a more complete local-health view:

- monitor not only disk usage, but also archive-table growth inside MySQL
- automatically trim old archive rows while preserving roughly 3 months of statistical history
- expose disk and memory temperature conditions with configurable thresholds

### Fix

The `SystemDiagnostics` feature set now includes:

- MySQL archive-size monitoring based on the configured table and timestamp column
- automatic batched cleanup of rows older than the configured retention window
- configurable archive size thresholds:
  - cleanup start size
  - cleanup target size
  - cleanup cooldown
  - delete batch size
- disk temperature monitoring through Windows storage reliability counters
- memory/system thermal-zone monitoring through ACPI WMI
- new diagnostics UI cards for:
  - database archive size
  - hottest disk temperature
  - memory temperature
  - archive cleanup policy and result

This update advances the official version from:

- `1.1.0.3`
- to `1.1.0.4`

Files:

- `Services/SystemDiagnosticsService.cs`
- `Models/SystemDiagnosticsModels.cs`
- `ViewModels/SystemDiagnosticsViewModel.cs`
- `Views/UserControls/SystemDiagnosticsView.xaml`
- `Views/UserControls/SystemDiagnosticsView.xaml.cs`
- `ServerMessage/ServerMessageStructure.cs`
- `scripts/UpdateRuntimeLanguageFiles.ps1`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `README.md`

## 16. Immediate trigger-delay application hardened for DALSA direct hardware trigger

### Goal

In production, changing `TriggerDelay` could become effective only after the third or fourth product.

The same family of symptoms could appear:

- after application startup
- after recipe change
- after editing and saving the active production recipe

The issue was more visible on machines where the trigger line goes directly to the Teledyne DALSA camera.

### Root causes

Multiple fragile points overlapped:

- `InitializeVisionSystem()` was reloading the recipe XML with `EnsureLoadedAsync().SafeFireAndForget()` and then immediately reading trigger-delay values, so the runtime could fall back to defaults or stale values
- startup/reload defaults were inconsistent (`70000` vs `700000`)
- `getConfigureTrigger()` had a hidden side effect that could save runtime state back into the recipe/config path
- the save flow was trying to touch trigger-delay application before persisting the edited XML
- no explicit queue flush / hardware readback verification existed around DALSA trigger-delay updates

### Fix

The runtime now:

- awaits recipe XML loading before reading camera settings during vision-system initialization
- uses the same fallback trigger-delay baseline in startup and reload paths
- applies trigger delay through a coordinated hardware sequence:
  - stop job
  - flush job and manager queues
  - arm hardware trigger mode
  - write `TriggerDelay`
  - flush again
  - read back the applied delay for verification
- centralizes the top/side application in `MainWindow.ApplyRecipeTriggerDelaysAsync(...)`
- removes the implicit save side effect from `IgigaCameraAccess.getConfigureTrigger(...)`
- keeps the recipe XML as the source of truth for trigger delay

This update advances the official version from:

- `1.1.0.4`
- to `1.1.0.5`

Files:

- `Cls_Vpro/IgigaCameraAccess.cs`
- `MainWindow.xaml.cs`
- `ViewModels/RecipeManagerViewModel.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `README.md`

## 17. DALSA trigger-delay follow-up stabilization after version 1.1.0.5

### Goal

After deploying `1.1.0.5`, some machines still logged:

- `No Frame grabber o camera detected on the getConfigureTrigger methode`
- `Node is not writable. (feature: TriggerDelay)`

especially during startup or immediately after recipe reload.

### Root cause

The first hardening pass improved queue draining and verification, but it still contained two aggressive behaviors for some DALSA states:

- forcing `job.Stop()` directly before writing the trigger delay
- treating transient `AcqFifo` / `OwnedGigEAccess` unavailability as a hard error instead of waiting for the job to finish settling

### Fix

The DALSA path now:

- waits briefly for `AcqFifo`, `FrameGrabber` and `OwnedGigEAccess` readiness before applying the delay
- removes the direct `job.Stop()` from the low-level trigger update
- retries `TriggerDelay` write automatically before declaring the node not writable
- logs missing camera/framegrabber during readback as warning rather than hard error

This update advances the official version from:

- `1.1.0.5`
- to `1.1.0.6`

Files:

- `Cls_Vpro/IgigaCameraAccess.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

## 18. Recipe change runtime rebuilt with a single clean VisionPro lifecycle

### Goal

In production, after recipe change the machine could show unstable behavior such as:

- updates becoming visible only every few products
- `VISIONPRO_JOB_STOPPED` with `StoppedSingle`
- side tool failures immediately after the reload
- stale top/side camera state surviving the previous recipe

### Root cause

The recipe-change path was initializing VisionPro twice:

1. `loadVsionPro()`
2. `InitializeVisionSystem()`

inside the same reload flow.

That meant the old runtime was not torn down cleanly before the new one started, and the same recipe change could leave:

- stale event subscriptions
- stale queues
- stale display records
- cameras/jobs in partially released state

### Fix

The recipe-change flow now:

- performs a dedicated pre-reload teardown
- stops vision watchdog and continuous run
- deregisters Cognex events
- disposes the current `CogJobManager`
- clears queues, records and job references
- clears camera-display state
- rebuilds the new recipe runtime only once

This update advances the official version from:

- `1.1.0.6`
- to `1.1.0.7`

Files:

- `MainWindow.xaml.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

## 19. Full application shutdown hardened for native acquisition/runtime release

### Goal

On some production PCs the application window could close, but:

- `QtisVisionPanel.exe` remained alive in Task Manager
- native acquisition modules such as `ACQ_MODULE_AIK_2_3_0_14` remained loaded
- auxiliary workers or secondary windows could leave the HMI process in a ghost state

### Root causes addressed

The baseline had multiple shutdown weak points:

1. the global Cognex runtime was initialized but never shut down explicitly
2. the asynchronous image writer had no explicit dispose path
3. singleton services were shut down, but not fully reset/disposed in a single canonical step
4. the active view and its disposable view model were not explicitly released during shell shutdown
5. WPF `MainWindow` semantics still started from the splash window instead of the real shell

### Fix

The shutdown baseline now:

- assigns the production shell to `Application.MainWindow`
- schedules a forced-process exit watchdog for shutdown timeout scenarios
- closes secondary windows during teardown
- disposes the image-save writer worker explicitly
- resets singleton services explicitly
- calls `Cognex.Vision.Startup.Shutdown()` during exit
- disposes the current active view and its disposable view model from the main shell

This update advances the official version from:

- `1.1.0.7`
- to `1.1.0.8`

Files:

- `App.xaml.cs`
- `SplashWindow.xaml.cs`
- `MainWindow.xaml.cs`
- `ViewModels/MainViewModel.cs`
- `SaveImage/ISaveImage.cs`
- `ServiceLocator.cs`
- `Services/IntegratedAlarmCardService.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

## 20. Cognex startup/shutdown name collision fixed in App.xaml.cs

### Goal

After the shutdown hardening update, the project could fail to compile with errors such as:

- `È necessario un riferimento all'oggetto per la proprietà, il metodo o il campo non statico 'Application.Startup'`
- `L'evento 'Application.Startup' può essere specificato solo sul lato sinistro di += o di -=`

### Root cause

Inside `App : Application`, the identifier `Startup` became ambiguous:

- WPF resolves `Startup` as the `Application.Startup` event in instance context
- the intended target was `Cognex.Vision.Startup`

### Fix

The baseline now uses an explicit alias for the Cognex runtime startup/shutdown class inside `App.xaml.cs`.

This update advances the official version from:

- `1.1.0.8`
- to `1.1.0.9`

Files:

- `App.xaml.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

## 21. Decimal rollover added to the canonical software versioning policy

### Goal

The release numbering must not continue with two digits on the rightmost segment.

Expected operator-facing behavior:

- `1.1.0.9`
- next release becomes `1.1.1.0`

### Root cause

The canonical script `scripts\IncrementSoftwareRelease.ps1` was incrementing the requested segment linearly, so the revision would continue as:

- `1.1.0.9`
- `1.1.0.10`

That no longer matches the desired industrial versioning rule.

### Fix

The canonical release-bump script now applies decimal carry logic:

- revision `10` carries into build
- build `10` carries into minor
- minor `10` carries into major

This update advances the official version from:

- `1.1.0.9`
- to `1.1.1.0`

Files:

- `scripts/IncrementSoftwareRelease.ps1`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

## 22. Camera overview made adaptive for compact HMIs and multi-camera runtime layouts

### Goal

When the machine is configured with more than two cameras, the camera overview on 15" industrial screens could become visually cramped and lower panels were hard to reach.

The required behavior is:

- preserve the existing camera/runtime logic
- keep the same camera templates and statistics view
- make the page usable on compact screens with a vertical scrollbar
- adapt the number of visible columns automatically to the actual viewport width

### Root cause

The baseline camera container used:

- a fixed `UniformGrid Columns="2"`
- no vertical outer scrolling for the full camera overview
- fixed padding regardless of viewport size

With four cameras this could push lower content below the visible area of compact HMIs.

### Fix

The camera overview baseline now:

- wraps the full camera/statistics area in an outer vertical `ScrollViewer`
- computes the camera grid column count at runtime from the available width
- keeps one column on narrow layouts, two columns on standard widths and three columns only on very wide layouts with enough cameras
- applies a compact padding profile automatically on smaller viewports

This update advances the official version from:

- `1.1.1.0`
- to `1.1.1.1`

Files:

- `Views/UserControls/DisplayRecord/CameraContainer.xaml`
- `Views/UserControls/DisplayRecord/CameraContainer.xaml.cs`
- `ViewModels/CameraContainerViewModel.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

## 23. Stopped machine state now rejects stale queued VisionPro results

### Goal

The machine must not continue to look active after runtime has already transitioned to `Stopped`.

Observed field behavior:

- machine status shown as `Stopped`
- operator returns from inspection configuration or another page
- buffered VisionPro results still continue to update image panels and inspection-related UI
- pressing `Start` restores coherence, which indicates stale queued runtime data rather than a true running state

### Root cause

The baseline could still consume already queued Top/Side/Front VisionPro results after continuous run had gone inactive.

This produced a temporary mismatch between:

- machine/runtime state
- pending display/counter processing

### Fix

The runtime baseline now:

- accepts VisionPro results only when continuous run is truly active and not transitioning
- clears pending result queues centrally on stop, job-stopped, auto-recovery start and recipe/runtime reset
- stops display update and pair-processing paths from consuming stale queued results when the machine is no longer active

This update advances the official version from:

- `1.1.1.4`
- to `1.1.1.5`

Files:

- `MainWindow.xaml.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/local-baseline-change-log-2026-03-27.md`

## 24. Continuous-run supervision hardened with explicit intentional hold reasons

### Goal

The industrial baseline must keep VisionPro in `RunContinuous` almost all the time, while still respecting the few states where staying stopped is intentional:

- manual stop from the Stop button
- recipe save / runtime refresh
- Job Tool Editor maintenance/editing activity

Unexpected stops should remain eligible for automatic resume after inactivity, but intentional stops must not be auto-resumed behind the operator's back.

### Root cause

The baseline previously tracked whether VisionPro was active, but it did not model clearly why it was stopped.

That meant:

- idle auto-resume could try to restart VisionPro even during editing scenarios
- recipe save and job editing used stop/start flows without a shared semantic contract
- the machine could drift into ambiguous runtime behavior around maintenance and resume

### Fix

The runtime baseline now introduces explicit continuous-run hold reasons in `MachineRuntimeService`:

- `manual-stop`
- `recipe-save`
- `job-editor`

The updated behavior is:

- auto-resume after inactivity runs only when there is no active intentional hold
- the Start button releases only the manual-stop hold before requesting continuous mode
- recipe save acquires a temporary save hold, then releases it and re-arms continuous mode if no other hold remains
- the Job Tool Editor acquires an editing hold while tools are being edited and releases it on successful save or clean editor exit
- if a tool is modified again after a save, the editor re-enters the protected stop state automatically

This update advances the official version from:

- `1.1.1.5`
- to `1.1.1.6`

Files:

- `Services/MachineRuntimeService.cs`
- `Services/OperatorInactivityService.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/RecipeManagerViewModel.cs`
- `ViewModels/JobToolEditorViewModel.cs`
- `Views/JobToolEditorView.xaml.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/local-baseline-change-log-2026-03-27.md`

## 25. Counters page now shows only enabled inspections

### Goal

The counters page must stay clear for the operator and avoid showing defect counters for inspections that are currently disabled.

Required behavior:

- keep the main production counters always visible
- hide defect counters for disabled inspections
- refresh immediately after an inspection enable/disable action
- refresh again when recipe/runtime reload re-applies the inspection configuration

### Root cause

The counters page always rendered the full theoretical defect list, regardless of the active inspection configuration cached in `InspectionConfigService`.

This created operator confusion because disabled inspections still appeared in the counters page as if they were active.

### Fix

The counters baseline now:

- stores a canonical feature key for each defect counter item
- filters the operator-facing defects collection using the currently enabled features from `InspectionConfigService`
- subscribes the counters viewmodel to `FeaturesChanged` so the panel refreshes immediately when inspections are enabled or disabled
- recalculates the visible defect total using only the currently displayed inspections

This update advances the official version from:

- `1.1.1.6`
- to `1.1.1.7`

Files:

- `Models/CounterData.cs`
- `ViewModels/InspectionCounterViewModel.cs`
- `ViewModels/InspectionConfigViewModel.cs`
- `Views/UserControls/StatisticsView.xaml`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/local-baseline-change-log-2026-03-27.md`

## 26. Top3D profiling contract separated from Front traceability

### Goal

The 3D / profilometer machine variant must stop reusing the historical `Front` recipe section and behave like a dedicated `Top3D` inspection role that renders on the top display surface.

Required behavior:

- semantic runtime role `Top3D`
- display on `TopCameraView`
- dedicated recipe section for 3D thresholds
- dedicated recipe archive JSON in MySQL
- backward compatibility for older recipes that still stored 3D values under `recipeParamFront`

### Root cause

The first 3D integration reused `Front` recipe fields and the front validation path to move quickly. That worked functionally, but it mixed two different machine concepts:

- front traceability camera logic
- top-mounted profilometer logic

This made configuration harder to understand and increased the risk of operator or maintainer confusion.

### Fix

The baseline now:

- introduces `recipeParamTop3D` as the dedicated recipe section for 3D nominal/tolerance values
- keeps legacy `recipeParamFront.ThreeD*` fields only for deserialization compatibility
- migrates legacy 3D values into `recipeParamTop3D` in memory when needed
- recognizes `Top3D` as a runtime camera role and routes it through the top queue/display pipeline
- validates 3D outputs through a dedicated `GetDetailedTop3DValidationAsync(...)` path
- allows `3DCheck` machines to process a single top-like inspection job without waiting for a secondary queue
- archives the dedicated recipe section in `tblproduzione.RecipeParamerterTop3D`
- documents the commissioning workflow in `top3d-machine-configuration-guide-2026-04-13.md`

This update advances the official version from:

- `1.1.1.7`
- to `1.1.1.8`

Files:

- `Cls_Config/Calss_structure/RecipeParameters.cs`
- `Models/CameraModel.cs`
- `ViewModels/CameraContainerViewModel.cs`
- `ViewModels/CameraTemplateSelector.cs`
- `Views/UserControls/DisplayRecord/TopCameraView.xaml.cs`
- `ViewModels/RecipeManagerViewModel.cs`
- `Views/RecipeManagerView.xaml`
- `Views/RecipeManagerView.xaml.cs`
- `DataManage/IToolBlockValidator.cs`
- `DataManage/InspectionProcessor.cs`
- `Cls_Vpro/ICognexJobManager.cs`
- `Database/Cls_InitializzeDb.cs`
- `MainWindow.xaml.cs`
- `Services/IntegratedAlarmCardService.cs`
- `ViewModels/EjectionAlarmCardViewModel.cs`
- `ServerMessage/ServerMessageStructure.cs`
- `scripts/UpdateRuntimeLanguageFiles.ps1`
- `Documentation/MachineHardware/top3d-machine-configuration-guide-2026-04-13.md`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/local-baseline-change-log-2026-03-27.md`
- `Documentation/MachineHardware/README.md`
- `README.md`

## 27. Runtime feature set now follows recipe + VPP job roles + toolblock outputs

### Goal

Keep the production runtime unambiguous when the same `TopView` surface can host either:

- a standard `Top` inspection job
- a dedicated `Top3D` profilometer job

At the same time, the operator-facing counters and camera feature panels must show only the inspections that are really active for the current machine/recipe/runtime combination.

### Root cause

After introducing `Top3D`, the runtime still had a few mixed assumptions:

- some top visibility decisions still leaned on machine-level context instead of the real semantic job loaded from the VPP
- the counters/filtering logic was not yet driven by the recipe `ejectionStatus`
- the camera feature panels could still show inspections that were not active for the current recipe or not supported by the loaded toolblock outputs

This made normal non-3D machines look inconsistent and risked hiding or misrouting standard top inspections.

### Fix

The baseline now:

- gives explicit runtime priority to the semantic job name loaded from the VPP when resolving `Top` vs `Top3D`
- computes an effective inspection map from:
  - recipe `ejectionStatus`
  - active semantic job roles
  - supported toolblock outputs detected at runtime
- uses that effective map for `InspectionConfigService.IsFeatureEnabled(...)`
- keeps counters aligned with the effective runtime feature set
- hides camera-panel feature icons that are not active for the current runtime
- limits the recipe inspection checklist to the inspection families that match the active camera roles

This update advances the official version from:

- `1.1.1.8`
- to `1.1.1.9`

Files:

- `DataManage/InspectionConfigService.cs`
- `MainWindow.xaml.cs`
- `Cls_Vpro/ICognexJobManager.cs`
- `ViewModels/CameraContainerViewModel.cs`
- `ViewModels/RecipeManagerViewModel.cs`
- `Views/UserControls/DisplayRecord/TopCameraView.xaml`
- `Views/UserControls/DisplayRecord/TopCameraView.xaml.cs`
- `Views/UserControls/DisplayRecord/SideCameraView.xaml`
- `Views/UserControls/DisplayRecord/SideCameraView.xaml.cs`
- `Views/UserControls/DisplayRecord/FrontCameraView.xaml`
- `Views/UserControls/DisplayRecord/FrontCameraView.xaml.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/local-baseline-change-log-2026-03-27.md`
- `README.md`

## 28. Inspection configuration now mirrors the active recipe/runtime

### Goal

Remove the operator confusion caused by two different inspection states being shown at the same time:

- machine-profile defaults from `cfg_inspection`
- effective runtime inspections from the active recipe XML

### Root cause

The counters and camera panels were already following the active recipe `ejectionStatus`, but the `Inspection Configuration` page still loaded the raw machine-profile rows from `cfg_inspection`.

This produced cases like:

- `Inspection Configuration` showing `7` enabled inspections
- runtime counters showing only `Logo`

even though the active recipe had only `logo=true` and all other `ejectionStatus` flags set to `false`.

### Fix

The baseline now:

- keeps the recipe as the authoritative runtime source for enabled inspections
- stops hiding recipe-enabled inspections only because a toolblock-output hint is missing
- filters `Inspection Configuration` cards by the active camera roles
- overlays the current active recipe state on the `Inspection Configuration` page
- saves/resets inspection changes to both:
  - machine profile (`cfg_inspection`)
  - active recipe XML (`ejectionStatus`)

This update advances the official version from:

- `1.1.1.9`
- to `1.1.2.0`

Files:

- `DataManage/InspectionConfigService.cs`
- `ViewModels/InspectionConfigViewModel.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/local-baseline-change-log-2026-03-27.md`
- `README.md`

## 29. Recipe inspections are now separated from reject-trigger inspections

### Goal

Allow the recipe to say independently:

- which inspections are active
- which defects must command product reject

### Root cause

The historical `ejectionStatus` node was doing two jobs at once:

- enabling/disabling inspections
- deciding if the detected defect must generate reject

That forced every enabled inspection to behave as a reject inspection.

### Fix

The baseline now introduces:

- `inspectionStatus` in the recipe XML for active inspections
- `ejectionStatus` kept as the reject-decision section

Runtime effects:

- validators, counters and feature visibility follow `inspectionStatus`
- reject decisions and ejection alarms follow `ejectionStatus`
- recipe UI shows two separate operator groups
- old recipes remain compatible because `inspectionStatus` is auto-derived from `ejectionStatus` when missing

This update advances the official version from:

- `1.1.2.0`
- to `1.1.2.1`

Files:

- `Cls_Config/Calss_structure/RecipeParameters.cs`
- `DataManage/InspectionConfigService.cs`
- `ViewModels/InspectionConfigViewModel.cs`
- `ViewModels/RecipeManagerViewModel.cs`
- `Views/RecipeManagerView.xaml`
- `Views/RecipeManagerView.xaml.cs`
- `DataManage/InspectionProcessor.cs`
- `MainWindow.xaml.cs`
- `Services/IntegratedAlarmCardService.cs`
- `ServerMessage/ServerMessageStructure.cs`
- `scripts/UpdateRuntimeLanguageFiles.ps1`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/local-baseline-change-log-2026-03-27.md`
- `README.md`
