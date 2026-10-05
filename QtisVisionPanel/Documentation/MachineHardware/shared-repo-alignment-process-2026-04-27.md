# Shared Repo Alignment Process

## Scope

This note defines the canonical process used to realign the local development baseline:

- `D:\Pulsar\Developer\NewPanel\QtisVisionPanel`

with the shared colleague folder:

- `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\lastRelease\QtisVisionPanel`

The shared folder is not treated as a git working tree. It is aligned at file-system level after a local update has been verified.

## Why a dedicated process is needed

- the local repository is the active engineering baseline
- the shared OneDrive folder is a distribution/alignment target used with a colleague
- syncing blindly would also move transient files that must stay local:
  - `.vs`
  - `.vscode`
  - `.claude`
  - `bin`
  - `obj`
  - `packages`
  - generated WPF artifacts
  - local logs

## Canonical script

Use:

- [scripts/SyncSharedBaseline.ps1](/D:/Pulsar/Developer/NewPanel/QtisVisionPanel/scripts/SyncSharedBaseline.ps1)

## Default behavior

The script starts in preview mode unless `-Apply` is passed.

Preview mode:

- compares local source and shared target
- shows `Nuovo file`, `Più recente`, `Extra file`
- does not copy anything

Apply mode:

- copies new and updated source files from local baseline to shared folder
- keeps extra target files unless `-Mirror` is explicitly requested

Mirror mode:

- should be used only when the shared folder must become a strict mirror of the local baseline
- can delete target-only files not excluded by the script

## Exclusions

The script excludes:

- directories:
  - `.git`
  - `.vs`
  - `.vscode`
  - `.claude`
  - `bin`
  - `obj`
  - `packages`
- files:
  - `*.suo`
  - `*.user`
  - `*.cache`
  - `*.pdb`
  - `*.exe`
  - `*.dll`
  - `*.baml`
  - `*.g.cs`
  - `*.g.i.cs`
  - `*.resources`
  - `*.tmp`
  - `*.bak`
  - `*.log`

## Recommended operating flow

1. complete the local change in the git-backed engineering baseline
2. verify the change locally
3. confirm that runtime/UI/config behavior is acceptable
4. run preview:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\SyncSharedBaseline.ps1
```

5. review the file list shown by `robocopy`
6. if the update is confirmed, run apply:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\SyncSharedBaseline.ps1 -Apply
```

7. use mirror only when agreed:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\SyncSharedBaseline.ps1 -Apply -Mirror
```

## Guardrails

- do not mirror until the update has been confirmed good
- do not sync build outputs or generated files
- keep the local repo as the authoritative engineering source unless the team explicitly decides otherwise
- if the shared folder is manually edited by the colleague, run preview first and inspect differences before any apply/mirror action
