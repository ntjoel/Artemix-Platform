# Integration Architecture And Code Guide 2026-03-27

## Purpose

This guide explains the main integration slices of the unified `QtisVisionPanel` baseline so a new developer, tester or AI agent can open the codebase and quickly understand:

- what each integration is responsible for
- which class orchestrates it
- which methods are the main operational entry points
- where to look first when behavior changes on machine

This document complements inline code comments and XML summaries inside the source files.

## 1. VisionPro Runtime And Recovery

### Main classes

- `Services/MachineRuntimeService.cs`
- `MainWindow.xaml.cs`
- `Cls_Vpro/ICognexJobManager.cs`
- `Services/MachineStatusService.cs`
- `ViewModels/ControlButtonsViewModel.cs`

### Responsibility split

- `MachineRuntimeService`
  - central runtime coordinator for continuous run
  - stores cached run state, requested run state and vision health
  - serializes start/stop/recovery commands
  - protects Cognex operations with timeouts
- `MainWindow`
  - owns watchdog timestamps and health-monitoring logic
  - reacts to Cognex events
  - decides when auto-recovery should be triggered
- `CognexJobManager`
  - direct adapter around VisionPro `CogJobManager`
  - start/stop jobs, live display, hardware mode restore
- `MachineStatusService`
  - translates runtime state into HMI machine status (`Running`, `Stopped`, `Error`, etc.)
- `ControlButtonsViewModel`
  - updates start/stop button state from the cached runtime service state

### Key methods

- `MachineRuntimeService.StartContinuousRunAsync`
- `MachineRuntimeService.StopContinuousRunAsync`
- `MachineRuntimeService.RecoverContinuousRunAsync`
- `MachineRuntimeService.SyncStateFromManager`
- `MainWindow.StartVisionHealthMonitoring`
- `MainWindow.MonitorVisionHealthAsync`
- `MainWindow.AttemptVisionAutoRecoveryAsync`
- `MainWindow.CognexManager_JobStopped`
- `ICognexJobManager.RunContinuous`
- `ICognexJobManager.StopContinuousRunAsync`

### Important design rule

The UI must not repeatedly query the Cognex manager directly during start/stop/recovery transitions. The runtime service cache exists specifically to avoid UI freezes while COM/hardware operations are still settling.

## 2. Operator Inactivity Automation

### Main class

- `Services/OperatorInactivityService.cs`

### Responsibilities

- track mouse, touch, stylus and keyboard activity coming from the main window
- auto-resume `RunContinuous` if the panel stays idle and the machine is stopped
- auto-logout the current operator after prolonged inactivity
- suspend automation if secondary windows/dialogs are visible

### Key methods

- `AttachMainWindow`
- `RegisterActivity`
- `Start`
- `EvaluateAsync`
- `ResumeVisionAsync`
- `PerformAutoLogoutAsync`
- `ShutdownAsync`

## 3. Configuration Load, Save And Recovery

### Main classes

- `Cls_Config/AsyncConfigManagerXml.cs`
- `Cls_Config/AsyncRecipeParam.cs`
- `Cls_Config/AsyncPreferenceConfigManager.cs`
- `Services/ConfigurationRecoveryService.cs`
- `Database/ConfigSnapshotRepository.cs`

### Responsibility split

- `AsyncConfigManagerXml`
  - loads/saves `Config.xml`
  - normalizes missing legacy fields
  - performs atomic save
  - triggers recovery snapshots
- `AsyncRecipeParam`
  - loads/saves the recipe XML paired with the active VPP
  - exposes runtime recipe data used by inspection logic
- `AsyncPreferenceConfigManager`
  - same pattern for preference-specific XML payloads
- `ConfigurationRecoveryService`
  - scope-based backup/restore orchestration using MySQL
- `ConfigSnapshotRepository`
  - low-level snapshot table access

### Key methods

- `EnsureLoadedAsync`
- `SaveConfigAsync`
- `TryRestoreFromRecoveryAsync`
- `BackupSerializedAsync`
- `TryRecoverSerializedAsync`
- `SaveSnapshotAsync`
- `TryGetSnapshotAsync`

### Recovery model

The project remains file-driven at runtime, but MySQL acts as a recovery plane:

1. file is loaded from disk when available
2. if file read fails, recovery tries MySQL snapshot storage
3. recovered payload is written back to disk atomically
4. runtime continues to work with the usual file-based contract

## 4. DataInspector Native Integration

### Main classes

- `Views/UserControls/DataInspectorView.xaml`
- `Views/UserControls/DataInspectorView.xaml.cs`
- `Inspector/ViewModels/DataInspectorViewModel.cs`
- `Inspector/Services/MySqlPieceHistoryRepository.cs`
- `Inspector/Services/LocalPieceEvidenceResolver.cs`
- `Inspector/ViewModels/ImageArtifactViewModel.cs`

### Responsibility split

- `DataInspectorView`
  - host control
  - creates the native inspector profile/services/view model
  - intentionally owns its own view model
- `DataInspectorViewModel`
  - loads rejected pieces
  - selects the active piece
  - loads evidence and traceability
  - exposes localized labels and summaries
- `MySqlPieceHistoryRepository`
  - reads rejected pieces from the database
  - remaps/normalizes `PieceData` paths when needed
- `LocalPieceEvidenceResolver`
  - turns a `PieceData` path into source and processed image lists
- `ImageArtifactViewModel`
  - wraps a file artifact into a preview-ready item for the UI

### Important design rule

`DataInspectorView` must not inherit `MainViewModel` bindings for inspector data. The inspector owns `InspectorVm` explicitly so XAML bindings stay isolated from the shell view model.

## 5. Main Shell Orchestration

### Main classes

- `MainWindow.xaml.cs`
- `ViewModels/MainViewModel.cs`
- `ServiceLocator.cs`
- `Services/ViewFactoryService.cs`

### Responsibility split

- `MainWindow`
  - machine runtime shell
  - Cognex events, watchdog, inspection flow, shutdown sequence
- `MainViewModel`
  - navigation and main shell commands
  - connects start/stop/shutdown buttons to runtime service
- `ServiceLocator`
  - lightweight singleton registry for cross-cutting services
- `ViewFactoryService`
  - creates the views requested by navigation

### Key methods

- `MainViewModel.OnNavigateRequested`
- `MainViewModel.OnStartRequested`
- `MainViewModel.OnStopRequested`
- `MainViewModel.OnRestartRequested`
- `MainWindow.ManageJobStateAsync`
- `MainWindow.ShutdownApplicationAsync`
- `ServiceLocator.Initialize`
- `ServiceLocator.ShutdownAllServicesAsync`

## 6. Logging And Event Audit

### Main classes

- `Services/ApplicationEventLogger.cs`
- `Database/EventLogRepository.cs`

### Responsibilities

- write structured application/runtime/vision events
- log machine state transitions
- log operator events
- log recovery and diagnostics events

### Usage rule

Operational code should prefer structured event logging when:

- a machine state changes
- a recovery starts/completes/fails
- configuration or runtime data is reloaded
- an automatic action happens without direct operator input

## 7. How To Read The Code Quickly

For a new developer, the recommended reading order is:

1. `README.md`
2. `AGENTS.md`
3. this file
4. `MainWindow.xaml.cs`
5. `Services/MachineRuntimeService.cs`
6. `Cls_Vpro/ICognexJobManager.cs`
7. `Services/OperatorInactivityService.cs`
8. `Cls_Config/AsyncConfigManagerXml.cs`
9. `Inspector/ViewModels/DataInspectorViewModel.cs`

## 8. Practical Debug Map

If the issue is:

- VisionPro does not stay in `RunContinuous`
  - start from `MachineRuntimeService` and `MainWindow.MonitorVisionHealthAsync`
- UI freeze during recovery
  - inspect `AttemptVisionAutoRecoveryAsync`, `RecoverContinuousRunAsync`, and any direct manager reads from UI paths
- recipe changes do not reach runtime
  - inspect `RecipeManagerViewModel.SaveRecipeAsync` and `MainWindow.InitializeRecipeAsync`
- images are saved but not visible in `DataInspector`
  - inspect `tblgenerale.PieceData`, `MySqlPieceHistoryRepository`, `LocalPieceEvidenceResolver`
- config or recipe XML disappears/corrupts
  - inspect `ConfigurationRecoveryService` and `ConfigSnapshotRepository`

## 9. Documentation Policy Going Forward

When a new integration slice is added, update all three layers:

1. inline code comments / XML docs in the responsible classes
2. this architecture guide
3. baseline change log in `Documentation/MachineHardware/local-baseline-change-log-*.md`

This keeps the project understandable for:

- current developers
- the colleague repo mirror
- future maintainers
- AI-assisted analysis and onboarding
