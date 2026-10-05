# Main Project Refresh Alignment - 2026-03-23

## Scope

This note records the alignment performed after receiving a newer baseline of the main project.

Reference folders:
- previous working copy with our governance/docs additions: `C:\Users\chouikha\QtisVisionPanel_`
- new upstream copy to be used as the reference from now on: `C:\Users\chouikha\QtisVisionPanel`
- previous external runtime assets: `C:\QtisVision_`
- new external runtime assets to be used as the reference from now on: `C:\QtisVision`

## Goal

Keep the new updated main project as the source of truth while preserving the non-invasive governance and documentation work created on the previous local copy.

This alignment intentionally avoids carrying over structural or operational code changes from the old copy.

## What was checked

### Main project repo

Compared:
- root-level files
- documentation presence
- governance files
- high-level folder structure

Key findings:
- the new repo contains updated application/runtime files and additional folders such as `Docs`, `scripts`, and `Nlog.config`
- the old repo contained our added governance files:
  - `AGENTS.md`
  - `TASK_REQUEST_TEMPLATE.md`
- the old repo also contained the technical folder:
  - `Documentation\MachineHardware`
- the new repo did not contain these carried-over governance/docs artifacts yet

### External runtime assets (`C:\QtisVision`)

Compared:
- folder structure for `cfg`, `Programs`, `Language`
- file-name presence in `cfg`, `Programs`, `Language`
- message JSON hashes for `messages_eng.json` and `messages_ita.json`

Key findings:
- `cfg` and `Programs` show the same file-name set between old and new
- `Language` shows the same file-name set between old and new
- language file contents changed in the new baseline and therefore must be treated as updated upstream assets
- no old-only runtime asset files were found that need to be copied into the new `C:\QtisVision`

## Alignment performed

The following files/folders were copied from `QtisVisionPanel_` into the new `QtisVisionPanel`:
- `AGENTS.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation\MachineHardware\README.md`
- `Documentation\MachineHardware\encoder-integration-template.md`
- `Documentation\MachineHardware\runtime-foundation.md`
- `Documentation\MachineHardware\signal-mapping-template.csv`

No application code files were copied from the old repo into the new repo.

No files were copied from `C:\QtisVision_` into `C:\QtisVision`, because the updated external asset tree is now the reference and no old-only files were detected.

## Resulting reference policy

From this point forward, the reference locations are:
- main project repo: `C:\Users\chouikha\QtisVisionPanel`
- external runtime assets: `C:\QtisVision`

The folders with suffix `_` should be kept only until final human validation is complete, then they can be removed.

## Recommended validation before deleting old copies

1. Confirm the new repo now contains:
   - `AGENTS.md`
   - `TASK_REQUEST_TEMPLATE.md`
   - `Documentation\MachineHardware\...`
2. Confirm the application code remains the new upstream version.
3. Confirm `C:\QtisVision\Language` is the intended updated asset set.
4. Confirm no local-only files are still needed from the `_` folders.

## Notes for future integration with `QuatisVisionPanelIO`

This alignment restores the documentation and governance layer needed for the later machine/tool fusion work, without polluting the new upstream codebase.

When integration work starts, use the updated repo and updated `C:\QtisVision` assets as the new baseline.
