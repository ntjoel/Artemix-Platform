# Repo Alignment Execution Plan 2026-03-27

## Purpose

This document defines how to keep the two main repositories aligned while preserving the current production runtime logic as the canonical baseline.

Repositories involved:

- current canonical repo:
  - `D:\Pulsar\Developer\NewPanel\QtisVisionPanel`
- colleague repo to keep aligned:
  - `D:\Pulsar\Developer\QtisVisionPanel`

## Canonical rule

The source of truth is the current main project baseline, not a neutral middle ground.

That means:

- current runtime logic remains canonical
- current `Config.xml` contract remains canonical
- current DB semantics remain canonical unless a mature improvement is explicitly absorbed
- colleague improvements are imported only when they raise quality without breaking this baseline

## Slices already selected for unification

### Keep from current baseline

- runtime logic and machine flow
- config-driven path resolution
- configuration recovery stack
- current operator/diagnostic improvements already validated on the machine
- current preferences/config editing direction

### Import from colleague baseline

- native `DataInspector`
- stronger technical documentation flow
- DB compatibility fix on `tblproduzione.TimeFine`
- build/baseline hygiene improvements that do not alter runtime semantics

## Mandatory mirrored assets

These assets must stay aligned between the two repos after each approved integration:

- `QtisVisionPanel.csproj`
- `Database\Cls_InitializzeDb.cs`
- `Views\UserControls\DataInspectorView.xaml`
- `Views\UserControls\DataInspectorView.xaml.cs`
- `Inspector\...`
- `ServerMessage\ServerMessageStructure.cs`
- `scripts\UpdateRuntimeLanguageFiles.ps1`
- `README.md`
- `AGENTS.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation\MachineHardware\...`

## Merge rule for future work

When new work is produced in either repo:

1. classify it as:
   - runtime-safe improvement
   - config/DB contract change
   - documentation-only change
2. validate against the current canonical baseline
3. update the canonical repo first
4. mirror the same validated slice into the colleague repo
5. update documentation in the same turn

## What must not diverge

The two repos must not diverge on:

- `DataInspector` implementation
- DB bootstrap logic
- shared language/message structure
- build assumptions
- technical documentation used by AI or human maintainers

## What can remain machine-specific

These remain machine/runtime specific and must stay driven by external config instead of being normalized in repo:

- image root paths
- backup paths
- dashboard links
- automation links
- local deployment folders
- machine I/O mappings

## Validation before mirroring

Before copying changes into the colleague repo, validate at minimum:

1. no intentional break of current runtime logic
2. no forced folder standardization
3. no breaking DB change without migration path
4. no new hardcoded runtime path replacing config-driven values
5. documentation updated for the modified slice

## AI-oriented alignment note

If the colleague loads the aligned repo in AI tools, the first files to read are:

- `README.md`
- `AGENTS.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation\MachineHardware\current-unified-baseline-2026-03-27.md`
- this document

This prevents the aligned repo from drifting back to an older baseline assumption.
