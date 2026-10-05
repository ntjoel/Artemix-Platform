# Developer And Tester Onboarding 2026-03-27

## Audience

This guide is for:

- new developers
- commissioning engineers
- testers and validators
- AI copilots used on the project

## First-day reading order

Read in this order:

1. `README.md`
2. `AGENTS.md`
3. `TASK_REQUEST_TEMPLATE.md`
4. `Documentation\MachineHardware\README.md`
5. `Documentation\MachineHardware\current-unified-baseline-2026-03-27.md`
6. `Documentation\MachineHardware\repo-alignment-execution-plan-2026-03-27.md`

## Mental model of the system

The project must always be reasoned in three layers:

### Machine

- physical I/O
- encoders
- camera triggers
- serials
- rejection timing
- machine-specific hardware behavior

### Recipe

- product dimensions
- tolerances
- defect enablement
- product-specific trigger delay
- product-specific inspection thresholds

### Runtime

- UI state
- alarms
- active session
- live inspection results
- buffered counters
- watchdog and recovery behavior

Confusing these layers is the fastest way to create regressions.

## High-risk modules

Touch carefully:

- `MainWindow.xaml.cs`
- `RecipeManagerViewModel.cs`
- `ICognexJobManager.cs`
- `Cls_Config\*`
- `Database\Cls_InitializzeDb.cs`
- `Services\MachineRuntimeService.cs`
- `Services\ConfigurationRecoveryService.cs`
- `Views\UserControls\DataInspectorView.xaml`
- `Inspector\*`

## Minimum validation checklist

After relevant changes, validate:

1. application startup
2. recipe load/save/duplicate
3. continuous run start/stop
4. camera display refresh
5. image saving
6. alarm/event logging
7. `DataInspector` native view
8. preferences/config save
9. shutdown path

## Documentation rule

If you change any of these areas:

- runtime behavior
- DB bootstrap
- recipe semantics
- config file semantics
- native inspector

you must update at least one technical document in `Documentation\MachineHardware`.

If operator behavior changes, update user-facing documentation under `Docs\Manual`.

## Testing posture

This is an industrial application.

Prefer:

- conservative changes
- observable logs
- reversible migrations
- config-driven behavior
- incremental integration over big-bang rewrites

Avoid:

- hidden runtime assumptions
- hardcoded local paths
- silent DB contract changes
- unlogged automatic recovery behavior
