# Post-Fusion Main Project Health Check

## Scope

This note records the complete health check performed on `QtisVisionPanel` after the native `DataInspector` fusion slice.

Goals:

- confirm that the main project builds again in the active local baseline
- review the runtime logs produced by the application
- identify concrete blockers that can break runtime behavior
- fix safe, high-signal issues without destabilizing the production flow
- record what is still a code issue versus what is a configuration or tool-block issue

## Build Verification

The following builds were executed successfully with Visual Studio 18 MSBuild:

- `Debug | x64`
- `Release | x64`

Command used:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
  "C:\Users\chouikha\QtisVisionPanel\QtisVisionPanel.csproj" `
  /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo /m
```

and:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
  "C:\Users\chouikha\QtisVisionPanel\QtisVisionPanel.csproj" `
  /t:Build /p:Configuration=Release /p:Platform=x64 /nologo /m
```

Result:

- `0 errors`
- warnings remain, but they do not currently prevent compilation

## Log Sources Checked

The main runtime logs available in the local project are:

- [errors.log](C:/Users/chouikha/QtisVisionPanel/bin/x64/Debug/logs/errors.log)
- [event-audit.log](C:/Users/chouikha/QtisVisionPanel/bin/x64/Debug/logs/event-audit.log)

No dedicated `USER_LOGIN` or `USER_LOGOUT` entries were present in the currently available log window that was reviewed. The application session recorded there remained effectively in `Guest / Viewer` context.

This means:

- the application log pipeline is active
- machine, database and navigation events are being recorded
- a recent explicit user-login flow was not present in the inspected log slice

## High-Signal Runtime Problems Found

### 1. Top menu machine status cross-thread access

Observed in logs:

- `MACHINE_STATUS_CHECK_ERROR`
- `System.InvalidOperationException`
- message equivalent to: caller thread cannot access a UI-owned object

Stack trace pointed to:

- [TopMenuBar.xaml.cs](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/TopMenuBar.xaml.cs)
- [TopMenuBarViewModel.cs](C:/Users/chouikha/QtisVisionPanel/ViewModels/TopMenuBarViewModel.cs)
- [MachineStatusService.cs](C:/Users/chouikha/QtisVisionPanel/Services/MachineStatusService.cs)

### 2. Defect counter normalization mismatch

Observed in logs:

- `Tipo difetto non riconosciuto: shapeside`
- `Tipo difetto non riconosciuto: Logo`
- `Tipo difetto non riconosciuto: print_centering`

This meant the counter layer was receiving valid defect identifiers from the runtime, but one code path normalized them to canonical names while another switch still expected legacy lowercase names.

## Fixes Applied

### Top menu cross-thread fix

Files:

- [TopMenuBar.xaml.cs](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/TopMenuBar.xaml.cs)
- [TopMenuBarViewModel.cs](C:/Users/chouikha/QtisVisionPanel/ViewModels/TopMenuBarViewModel.cs)

What changed:

- `TopMenuBar` now stores and unsubscribes from a dedicated bound view model reference instead of re-reading `DataContext` inside the status-change callback.
- the callback now reads the view model from `sender`, which avoids touching a UI `DependencyObject` from the wrong thread path
- `TopMenuBarViewModel` now exposes `CurrentMachineStatus` from its own cached field
- the cached status is updated on machine-status change before firing `PropertyChanged`
- initialization now aligns the cached field with the status service on startup

Why this matters:

- the view is no longer dependent on live `DataContext` reads during background-originated notifications
- machine status animation and status text updates are safer against dispatcher timing issues

### Defect counter normalization fix

File:

- [CounterManager.cs](C:/Users/chouikha/QtisVisionPanel/Database/CounterManager.cs)

What changed:

- `MapDefectType` now delegates to the canonical normalizer
- `NormalizeDefectType` now guards against null or whitespace
- `IncrementDefectAsync` now switches on canonical defect names:
  - `Logo`
  - `PrintCentering`
  - `OpenFlaps`
  - `SurfaceCheck`
  - `Height`
  - `SideSealing`
  - `ShapeTop`
  - `ShapeSide`

Why this matters:

- valid defect names coming from inspections are no longer rejected by the counter layer simply because they arrive in a different legacy format
- counter totals and defect statistics are now more trustworthy

## User Log Result

The inspected `event-audit` slice did **not** contain explicit user actions such as:

- `USER_LOGIN`
- `USER_LOGOUT`
- `USER_AUTO_LOGOUT`

The current visible context in the reviewed logs remained:

- user: `Guest`
- role: `Viewer`

So the practical conclusion is:

- logging works
- there is no recent user-auth event captured in the reviewed runtime window
- if user-auth validation is needed, that should be tested with an explicit login/logout roundtrip in the running HMI

## Remaining Warnings And Risks

### Code warnings that remain but do not block build

Main categories still present:

- `RecipeManagerViewModel` hides inherited members
- unused fields and events in some viewmodels/views
- obsolete Advantech API usage
- one unreachable-code warning in legacy DB/user code

These are worth cleaning later, but they are not the current blockers for basic runtime operation.

### Runtime warnings still visible in the available logs

Still present in the reviewed log history:

- `OpenFlapsArea output not found in tool block.`
- `Classification outputs not found in tool block.`
- `No UserResult available.`

These do **not** currently look like fusion regressions.
They look more like one of the following:

- incomplete or mismatched VisionPro tool-block outputs
- recipe/runtime alignment issues
- expected warnings while a result object is not yet available during acquisition flow

These should be validated with the actual job, recipe and runtime configuration loaded on the target machine.

## Practical Readiness Conclusion

The main project is now in a materially better state after the check:

- main project builds again in both `Debug x64` and `Release x64`
- the `DataInspector` fusion slice is no longer blocked by baseline compilation debt
- the top-menu machine-status cross-thread issue has been corrected
- defect counter normalization has been corrected
- the remaining runtime warnings are now mostly configuration/runtime-validation topics rather than obvious compile-time or fusion-time breakages

## Recommended Next Validation

1. Start `QtisVisionPanel` in `Debug x64`.
2. Open the native `DataInspector` page inside the HMI.
3. Confirm:
   - recent rejects list loads
   - image resolution works from `C:\QtisVision\Pieces\`
   - no new `MACHINE_STATUS_CHECK_ERROR` appears in fresh logs
4. Perform one explicit login/logout cycle so `USER_LOGIN` and `USER_LOGOUT` can be verified in `event-audit.log`.
5. Validate one real or simulated inspection cycle and watch whether:
   - `No UserResult available`
   - `OpenFlapsArea output not found`
   - `Classification outputs not found`
   remain expected warnings or reveal a true tool-block mismatch
