# Baseline Promotion 2026-03-30

## Purpose

This note records the local promotion of the refreshed `QtisVisionPanel_` baseline to become the only active main-project repo on the workstation.

The goal of this promotion is simple:

- keep the local project aligned with the colleague baseline
- preserve the stronger unified documentation set
- avoid continuing development on two diverging local copies

## Starting Point

Before the promotion, two local repos were present:

- old active repo:
  - `C:\Users\chouikha\QtisVisionPanel`
- refreshed colleague-aligned repo:
  - `C:\Users\chouikha\QtisVisionPanel`

There is only one runtime support root currently used with both:

- `C:\QtisVision`

No second runtime root had to be merged during this step.

## Comparison Result

The refreshed baseline already contained:

- the native `DataInspector`
- the `TimeFine` DB compatibility fix
- the stronger unified documentation set
- repo-alignment and onboarding notes dated `2026-03-27`
- additional recovery/configuration components not present in the older local copy

Examples of files present only in the refreshed repo:

- `Database\ConfigSnapshotRepository.cs`
- `Services\ConfigurationRecoveryService.cs`
- `Documentation\MachineHardware\current-unified-baseline-2026-03-27.md`
- `Documentation\MachineHardware\developer-and-tester-onboarding-2026-03-27.md`
- `Documentation\MachineHardware\integration-architecture-and-code-guide-2026-03-27.md`
- `Documentation\MachineHardware\local-baseline-change-log-2026-03-27.md`
- `Documentation\MachineHardware\repo-alignment-execution-plan-2026-03-27.md`

## Local Fix Applied Before Promotion

One local blocker was found while validating the refreshed repo:

- `MainViewModel` called `AuthorizationService.CanAccessViewCached(...)`
- `AuthorizationService` in the refreshed repo did not expose that method yet

Applied fix:

- added `CanAccessViewCached(string viewName)` in:
  - [AuthorizationService.cs](C:/Users/chouikha/QtisVisionPanel_/Services/AuthorizationService.cs)

The implementation is conservative:

- respects administrator / installer bypass
- checks the in-memory role-feature cache first
- falls back to the existing hardcoded fallback permissions
- does not change DB permission semantics

## Validation After Fix

Validated locally with:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' QtisVisionPanel.csproj /t:Build /p:Configuration=Release /p:Platform=x64 /nologo
```

Result:

- build succeeded
- remaining output contains warnings only
- no promotion blocker remained after the authorization fix

## Promotion Decision

The refreshed repo is the correct baseline to keep because it is:

- aligned with the colleague version
- richer in documentation and onboarding material
- already carrying the fused Inspector path
- already carrying the DB compatibility work needed by the local environment

The old local repo should not continue as a parallel active baseline after this step.

## Runtime Support Notes

The support root to keep using is:

- `C:\QtisVision`

Important implication:

- the promoted repo must continue to work against the current config-driven runtime assets
- no forced folder standardization should be reintroduced unless `Config.xml` changes explicitly

## Recommended Reading After Promotion

For a developer or another AI resuming from the promoted baseline:

1. [README.md](C:/Users/chouikha/QtisVisionPanel_/README.md)
2. [AGENTS.md](C:/Users/chouikha/QtisVisionPanel_/AGENTS.md)
3. [README.md](C:/Users/chouikha/QtisVisionPanel_/Documentation/MachineHardware/README.md)
4. [current-unified-baseline-2026-03-27.md](C:/Users/chouikha/QtisVisionPanel_/Documentation/MachineHardware/current-unified-baseline-2026-03-27.md)
5. [repo-alignment-execution-plan-2026-03-27.md](C:/Users/chouikha/QtisVisionPanel_/Documentation/MachineHardware/repo-alignment-execution-plan-2026-03-27.md)
6. [developer-and-tester-onboarding-2026-03-27.md](C:/Users/chouikha/QtisVisionPanel_/Documentation/MachineHardware/developer-and-tester-onboarding-2026-03-27.md)
7. [baseline-promotion-2026-03-30.md](C:/Users/chouikha/QtisVisionPanel_/Documentation/MachineHardware/baseline-promotion-2026-03-30.md)

