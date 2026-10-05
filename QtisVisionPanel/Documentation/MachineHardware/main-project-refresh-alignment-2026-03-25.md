# Main Project Refresh Alignment 2026-03-25

## Purpose

This note records the second local baseline refresh of `QtisVisionPanel` after receiving a newer full project drop and replacing the previous working copy.

The goal of this pass was:
- keep the new production project as the active source of truth
- restore the local governance and fusion-preparation documentation that existed in the previous working copy
- standardize the image-save root to `C:\QtisVision\Pieces\`
- preserve the context needed to continue the future fusion of:
  - `QuatisVisionPanelIO`
  - `QuatisVisionInspector`

## Active Local Baseline

From this point forward, the active local references are:
- repo: `C:\Users\chouikha\QtisVisionPanel`
- runtime assets: `C:\QtisVision`

The previous local working copy was retained only as a temporary recovery source:
- `C:\Users\chouikha\QtisVisionPanel_`

## What Was Restored Into The New Repo

The refreshed repo did not contain the local process/governance layer that had been built around the previous baseline.

The following items were restored into the new repo without changing the operational runtime code:
- `AGENTS.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation\MachineHardware\*`

This keeps the new baseline aligned with the standalone-tool preparation work already completed.

## Structural Differences Worth Noting

Compared to the previous working copy, the refreshed baseline already includes:
- `app.manifest`
- `QTisPanel.ico`

These are now part of the project contract through `QtisVisionPanel.csproj`.

Operationally important implication:
- the manifest requests administrator privileges at launch

This should be treated as part of deployment/runtime behavior, not as an incidental file.

## Image Storage Alignment

The machine image-save root was standardized to:
- `C:\QtisVision\Pieces\`

The alignment was applied conservatively in the places that govern runtime behavior and local defaults:
- `C:\QtisVision\cfg\Config.xml`
- `C:\QtisVision\cfg\cfg\QtisPanel.ini`
- `C:\QtisVision\cfg\cfg\QtisFolderPanel.ini`
- `Cls_Config\AsyncPreferenceConfigManager.cs`
- `Cls_Config\Calss_structure\ConfigClassStructure.cs`

The actual save pipeline still uses:
- `Configuration.ImageDir`

through:
- `SaveImage\ISaveImage.cs`

This means the redirect was achieved without changing the save workflow itself.

## Why This Matters For Inspector Preparation

`QuatisVisionInspector` depends on:
- the real database schema
- the real `PieceData` conventions
- the real saved-image folder structure

Standardizing image storage to `C:\QtisVision\Pieces\` gives a stable local baseline for:
- validating piece-to-folder resolution
- testing source/processed image pairing
- preparing the future native replacement of the legacy external DataInspector workflow

## Result

At the end of this refresh:
- the new `QtisVisionPanel` repo remains the active code baseline
- the local governance/process/docs layer has been restored
- the main runtime image path is aligned with the manually prepared local `Pieces` folder
- the standalone tools can continue to evolve against a stable, documented main-project reference
