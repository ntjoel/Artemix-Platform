# Local Baseline Change Log

## Purpose

This document tracks what was added or changed in the local `QtisVisionPanel` baseline after the latest project drop was loaded on the workstation.

It is intended for:

- teammate handover
- AI-assisted continuation in another environment
- future fusion work with the standalone companions

The goal is to make the local evolution readable without asking someone to reconstruct it from scattered commits or chat history.

## Reference Baseline

The reference main project is:

- [QtisVisionPanel](C:/Users/chouikha/QtisVisionPanel)

The local runtime asset root used with this baseline is:

- `C:\QtisVision`

The image storage root standardized during the local alignment work is:

- `C:\QtisVision\Pieces\`

## High-Level Work Done After Loading The Latest Local Version

### 1. Governance and project working rules restored

The refreshed repo was aligned again with the local working process already established on the previous baseline.

Added or restored:

- [AGENTS.md](C:/Users/chouikha/QtisVisionPanel/AGENTS.md)
- [TASK_REQUEST_TEMPLATE.md](C:/Users/chouikha/QtisVisionPanel/TASK_REQUEST_TEMPLATE.md)

Why:

- to preserve the separation between main HMI, machine companion, and inspector companion
- to keep future work compatible with the documented machine/recipe/runtime boundaries

### 2. Technical documentation restored into the refreshed baseline

The refreshed main project now contains the technical documentation needed for:

- machine/runtime fusion planning
- inspector fusion planning
- post-refresh architectural reading
- build-baseline restoration

Relevant documents include:

- [main-project-refresh-alignment-2026-03-25.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/main-project-refresh-alignment-2026-03-25.md)
- [updated-main-project-technical-reconnaissance.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/updated-main-project-technical-reconnaissance.md)
- [new-baseline-fusion-preparation-plan.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/new-baseline-fusion-preparation-plan.md)
- [inspector-standalone-preparation.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-standalone-preparation.md)

### 3. Runtime image path was standardized

The local runtime configuration was aligned so the main project and the inspector work against the same image root:

- `C:\QtisVision\Pieces\`

This was reflected in:

- `C:\QtisVision\cfg\Config.xml`
- `C:\QtisVision\cfg\cfg\QtisPanel.ini`
- `C:\QtisVision\cfg\cfg\QtisFolderPanel.ini`
- `C:\QtisVision\cfg\PreferenceViewConfig.xml`

And in local defaults inside the project:

- [AsyncPreferenceConfigManager.cs](C:/Users/chouikha/QtisVisionPanel/Cls_Config/AsyncPreferenceConfigManager.cs)
- [ConfigClassStructure.cs](C:/Users/chouikha/QtisVisionPanel/Cls_Config/Calss_structure/ConfigClassStructure.cs)

### 4. Main-project build baseline was restored

The latest delivered baseline was not initially build-clean locally.

The local restoration work made the project compile again in:

- `Debug | x64`
- `Release | x64`

Main adjustments were conservative and limited to baseline/toolchain compatibility:

- project build-path cleanup in [QtisVisionPanel.csproj](C:/Users/chouikha/QtisVisionPanel/QtisVisionPanel.csproj)
- WebView2 legacy import path handling aligned to the current package/toolchain
- post-build dependency link switched to a local-compatible junction strategy

Reference note:

- [baseline-build-restoration-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/baseline-build-restoration-2026-03-26.md)

### 5. Native DataInspector fusion slice added

The old external bridge based on opening an external page was replaced with a first native in-HMI fusion slice.

Main entry points:

- [DataInspectorView.xaml](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml)
- [DataInspectorView.xaml.cs](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml.cs)

New inspector layer added under the main project:

- [Inspector](C:/Users/chouikha/QtisVisionPanel/Inspector)

This internal layer includes:

- abstractions for repository, health check and image resolution
- inspector models for piece history and image evidence
- MySQL read-only repository for rejected-piece history
- local image resolver for `PieceData` and `C:\QtisVision\Pieces\`
- native viewmodel for the in-HMI review surface

Reference note:

- [inspector-native-fusion-slice-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-native-fusion-slice-2026-03-26.md)

### 6. Native DataInspector configuration and usage were documented

Added:

- [main-project-datainspector-configuration-and-usage.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/main-project-datainspector-configuration-and-usage.md)

This documents:

- runtime config source
- DB assumptions
- image path assumptions
- internal structure
- operator usage
- maintenance constraints

### 7. MySQL zero-date issue was fixed

The latest local runtime raised:

- `Incorrect datetime value: '0000-00-00 00:00:00' for column 'TimeFine'`

This was fixed in the main project by:

- making `tblproduzione.TimeFine` nullable
- migrating legacy zero-date rows to `NULL`
- inserting `NULL` instead of fake zero dates
- protecting Inspector read-side queries against legacy zero-date values

Main code touched:

- [Cls_InitializzeDb.cs](C:/Users/chouikha/QtisVisionPanel/Database/Cls_InitializzeDb.cs)
- [MySqlPieceHistoryRepository.cs](C:/Users/chouikha/QtisVisionPanel/Inspector/Services/MySqlPieceHistoryRepository.cs)

### 8. Post-fusion health check was completed

A full post-fusion check was performed:

- build verification
- log analysis
- runtime blocker identification
- safe runtime fixes

Main blockers found and fixed:

- machine-status top-bar cross-thread access
- defect-counter normalization mismatch

Reference note:

- [post-fusion-main-project-health-check-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/post-fusion-main-project-health-check-2026-03-26.md)

### 9. DataInspector initialization bug in HMI context was fixed

After the native inspector fusion, the page could appear blank inside the main HMI even though the standalone inspector worked correctly.

Root cause:

- the view was inheriting an external `DataContext` from the host navigation/container
- the loaded handler exited too early whenever any inherited `DataContext` was present
- as a result, the inspector-specific viewmodel was never created

Fix applied in:

- [DataInspectorView.xaml.cs](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml.cs)

Behavior now:

- initialization is skipped only if the `DataContext` is already a `DataInspectorViewModel`
- inherited container viewmodels no longer block the native inspector bootstrap

### 10. DataInspector language handling was unified with the main HMI

After the native fusion, the Inspector still carried some standalone-style wording and hardcoded UI strings.

This was aligned so the fused page now uses the same language-management entity as the rest of the main project:

- [ServerMessagePersonalize.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessagePersonalize.cs)
- [ServerMessageStructure.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)

What was done:

- the native Inspector viewmodel now resolves user-facing labels through `GetMessageOrDefault(...)`
- the native Inspector host view uses localized initialization and error messages
- `PieceCardViewModel` now uses localized strings for status, timestamps, storage labels, and fallback wording
- the runtime language packs in `C:\QtisVision\Language` were extended with Inspector-specific keys in both English and Italian
- remaining hardcoded view text such as the thumbnail `No preview` placeholder was removed

Result:

- the fused `DataInspector` follows the active HMI language
- there is no parallel language service for the Inspector inside the main project
- future Inspector wording changes must now go through the single shared language system

## Login Credentials Seeded By The Current Baseline

The local codebase seeds these default users when the auth DB is initialized:

- `pulsar` / `Pulsar.Quality` -> `Administrator`
- `operator` / `operator123+` -> `Operator`
- `installer` / `installer123+` -> `Installer`
- `expert` / `expert123+` -> `Expert`
- `viewer` / `viewer123+` -> `Viewer`

Source:

- [Cls_CheckUser.cs](C:/Users/chouikha/QtisVisionPanel/Database/Cls_CheckUser.cs)

Important note:

- the panel still boots by default as `Guest / Viewer`
- these credentials depend on the auth database/table being initialized and available

## Current Functional Status

At the end of the local alignment work:

- main project builds in `Debug x64`
- main project builds in `Release x64`
- image root is aligned to `C:\QtisVision\Pieces\`
- native `DataInspector` is present inside the HMI
- post-fusion runtime blockers found in logs were corrected
- language handling for the fused `DataInspector` is unified with the main project language system

## Environment Snapshot For Handover

This local baseline assumes the following environment split:

- source repo: [QtisVisionPanel](C:/Users/chouikha/QtisVisionPanel)
- runtime asset root: `C:\QtisVision`
- language packs: `C:\QtisVision\Language`
- runtime config: `C:\QtisVision\cfg`
- saved piece images: `C:\QtisVision\Pieces`

Key operational assumption:

- the repo alone is not enough to reproduce the local behavior
- the runtime asset root under `C:\QtisVision` is part of the effective environment

## What Must Travel With The Repo

If this baseline is copied into a shared folder for a teammate or another AI-enabled workstation, copy:

1. the source repo:
   - [QtisVisionPanel](C:/Users/chouikha/QtisVisionPanel)
2. the runtime asset root:
   - `C:\QtisVision`

At minimum, the following runtime folders should be included:

- `C:\QtisVision\cfg`
- `C:\QtisVision\Language`
- `C:\QtisVision\Programs`
- `C:\QtisVision\Pieces`
- `C:\QtisVision\Recipes` if recipes are part of the test environment
- `C:\QtisVision\SampleImage` if image examples are used for validation

Recommended exclusions from the repo copy, unless a compiled handoff is explicitly needed:

- `bin`
- `obj`
- `.vs`

Reason:

- many runtime behaviors depend on external config, language JSON files, local programs, and saved piece-image folders
- copying only the repo would not recreate the working local baseline

## Build And Sanity-Check Flow

The main project was validated locally with:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' QtisVisionPanel.csproj /t:Build /p:Configuration=Release /p:Platform=x64 /nologo
```

Working directory:

- [QtisVisionPanel](C:/Users/chouikha/QtisVisionPanel)

Recommended quick sanity-check after restoring the environment:

1. verify `C:\QtisVision\cfg\Config.xml` points to the intended runtime DB and image root
2. verify `C:\QtisVision\Language\messages_eng.json` and `messages_ita.json` are present
3. build `Release | x64`
4. start the HMI
5. enter `DataInspector`
6. confirm:
   - the page initializes without blank bindings
   - recent rejected items load
   - images resolve from `C:\QtisVision\Pieces`
   - labels follow the active language

## Language-System Alignment Status

The current baseline includes a full local audit of the main-project language system.

Relevant reference:

- [main-project-language-audit-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/main-project-language-audit-2026-03-26.md)

Practical status:

- runtime language packs were re-synchronized with [ServerMessageStructure.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)
- `NavigationViewModel` now uses safe message fallback logic instead of exposing raw keys when possible
- `PreferenceViewModel` runtime messages were moved into the shared language system
- `DataInspector` fused text now resolves through the same language entity as the rest of the HMI

This means the main project should now be treated as having a single language-management source of truth:

- message structure in code
- runtime JSON packs under `C:\QtisVision\Language`

## Remaining Known Runtime Risks

These warnings still appear in historical logs and should be treated as runtime/config validation topics:

- `OpenFlapsArea output not found in tool block`
- `Classification outputs not found in tool block`
- `No UserResult available`

These are not currently tracked as fusion blockers, but as:

- tool-block output availability issues
- recipe/runtime alignment issues
- expected warnings while certain inspection outputs are not yet produced

## Companion Projects Still Relevant

This main-project baseline is designed to stay aligned with:

- [QuatisVisionPanelIO](C:/Users/chouikha/QuatisVisionPanelIO)
- [QuatisVisionInspector](C:/Users/chouikha/QuatisVisionInspector)

Those companion repos remain the pre-fusion truth sources for:

- machine I/O, commissioning, encoder and intervention logic
- rejected-piece history, image review workflow and inspector UX

## Recommended Handover Reading Order

For a teammate or another AI instance, the recommended reading order is:

1. [README.md](C:/Users/chouikha/QtisVisionPanel/README.md)
2. [AGENTS.md](C:/Users/chouikha/QtisVisionPanel/AGENTS.md)
3. [Documentation/MachineHardware/README.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/README.md)
4. [baseline-build-restoration-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/baseline-build-restoration-2026-03-26.md)
5. [inspector-native-fusion-slice-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-native-fusion-slice-2026-03-26.md)
6. [main-project-datainspector-configuration-and-usage.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/main-project-datainspector-configuration-and-usage.md)
7. [main-project-language-audit-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/main-project-language-audit-2026-03-26.md)
8. [post-fusion-main-project-health-check-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/post-fusion-main-project-health-check-2026-03-26.md)
9. this file: [local-baseline-change-log-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/local-baseline-change-log-2026-03-26.md)

## AI-Oriented Resume Guide

For another AI instance continuing from this baseline, the shortest correct mental model is:

- this repo is the main HMI baseline
- `C:\QtisVision` is part of the working system, not an optional extra
- native `DataInspector` is already fused and should not be reintroduced as an external web bridge
- machine/recipe/runtime boundaries must stay aligned with the existing documentation
- `QuatisVisionPanelIO` and `QuatisVisionInspector` remain useful companion truth sources for pre-fusion design intent

Before changing code, an AI should read:

1. [AGENTS.md](C:/Users/chouikha/QtisVisionPanel/AGENTS.md)
2. [README.md](C:/Users/chouikha/QtisVisionPanel/README.md)
3. [Documentation/MachineHardware/README.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/README.md)
4. [local-baseline-change-log-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/local-baseline-change-log-2026-03-26.md)

If the immediate goal is runtime validation, start from:

- [main-project-datainspector-configuration-and-usage.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/main-project-datainspector-configuration-and-usage.md)
- [post-fusion-main-project-health-check-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/post-fusion-main-project-health-check-2026-03-26.md)

If the immediate goal is future fusion planning with companions, also read:

- [new-baseline-fusion-preparation-plan.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/new-baseline-fusion-preparation-plan.md)
- [inspector-standalone-preparation.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-standalone-preparation.md)
