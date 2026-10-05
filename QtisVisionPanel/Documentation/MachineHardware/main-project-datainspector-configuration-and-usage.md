# Main-Project DataInspector Configuration And Usage

## Goal

Document the native `DataInspector` now hosted inside `QtisVisionPanel` so that a first-time maintainer can understand:

- what it does
- how it is configured
- which runtime assets it depends on
- how operators are expected to use it
- where future fusion work should continue

## Functional role

The `DataInspector` entry in the main HMI is now a native read-only review surface for:

- recent rejected pieces
- piece-to-image traceability
- raw/source image review
- processed/inspected image review
- inspection-result context linked to the selected piece

It replaces the old external-browser bridge approach and is intended to become the internal image-review page of the full HMI.

## Navigation and hosting

The feature is exposed through the existing navigation target:

- `DataInspector`

Main hosting points:

- [ViewFactoryService.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Services/ViewFactoryService.cs)
- [DataInspectorView.xaml](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml)
- [DataInspectorView.xaml.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml.cs)

Important behavior:

- the existing menu and authorization flow are preserved
- the view is initialized on load
- the view remains read-only
- it does not alter recipe, runtime machine logic, or DB schema

## Internal structure

The native Inspector layer inside the main project is organized under:

- `Inspector\Abstractions`
- `Inspector\Models`
- `Inspector\Services`
- `Inspector\ViewModels`

### Abstractions

Core contracts:

- `IPieceHistoryRepository`
- `IImageEvidenceResolver`
- `IDataSourceHealthProvider`

These keep:

- DB access
- image resolution
- data-source health

separate from the UI.

### Models

Core data objects:

- `InspectorDataSourceProfile`
- `PieceHistoryQuery`
- `PieceRecordSummary`
- `PieceEvidenceBundle`
- `PieceImageArtifact`
- `InspectionResultDescriptor`

These are the canonical seam between the standalone Inspector companion and the main-project implementation.

### Services

Current native services:

- [InspectorProfileFactory.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Inspector/Services/InspectorProfileFactory.cs)
- [MySqlPieceHistoryRepository.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Inspector/Services/MySqlPieceHistoryRepository.cs)
- [LocalPieceEvidenceResolver.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Inspector/Services/LocalPieceEvidenceResolver.cs)

Responsibilities:

- build the local runtime profile from `AppConfig`
- query recent rejected pieces from MySQL
- resolve the image bundle from `PieceData` and the image root

### ViewModels

Current native presentation layer:

- `DataInspectorViewModel`
- `PieceCardViewModel`
- `ImageArtifactViewModel`
- `RelayCommand`

Responsibilities:

- load recent rejected pieces
- expose connection status
- manage selected piece
- manage selected source/processed images
- expose user-facing summaries for the native view
- from release `3.0.1.1`, expose zoom commands and evidence download for the selected piece/images

## Runtime configuration source

The native Inspector does not use its own config file inside the main project.

It reads from the existing main-project runtime configuration through:

- `MainWindow.configManager.Config`

The profile conversion is done by:

- [InspectorProfileFactory.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Inspector/Services/InspectorProfileFactory.cs)

### Current configuration mapping

From `AppConfig.MySqlConnection`:

- `Host` -> `DatabaseHost`
- `port` -> `DatabasePort`
- `Db` -> `DatabaseName`
- `User` -> `DatabaseUser`
- `Password` -> `DatabasePassword`
- `SslDisabled` -> `DisableSsl`

From `AppConfig.Configuration`:

- `ImageDir` -> `ImageRootPath`
- `DataHostnames` -> `DataHostnames`

Canonical baseline rule for this repo:

- DB host, port and DB name are read from the current `Config.xml`
- image root is read from `Configuration.ImageDir`
- no hardcoded image root must replace the configured runtime path

## Language management

The fused `DataInspector` is now aligned to the same language-management path used by the rest of the main HMI.

It does not carry its own local language service anymore.

Instead, user-facing labels are resolved through:

- [ServerMessagePersonalize.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/ServerMessage/ServerMessagePersonalize.cs)
- [ServerMessageStructure.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)

Runtime language packs remain the shared main-project assets:

- `C:\QtisVision\Language\messages_eng.json`
- `C:\QtisVision\Language\messages_ita.json`

This means:

- the `DataInspector` now follows the active HMI language
- new Inspector labels must be added as message keys in the main-project message structure
- the runtime JSON packs must be updated together with the strongly typed message model

The preferred access pattern inside the code is:

- `ServerMessagePersonalize.GetMessageOrDefault(key, fallback)`

This keeps the fused Inspector consistent with newer main-project views and avoids reintroducing a parallel localization mechanism.

## Database assumptions

The current native implementation is aligned to:

- `tblgenerale`
- `tblproduzione`

Current read behavior:

- recent pieces are ordered by `DataeOra DESC`
- rejected-piece filtering uses the configured mode
- production metadata is joined by `IdProduzione`
- `PieceData` is treated as the canonical piece-to-image seam

Production-session compatibility note:

- `TimeFine` must be treated as nullable
- open production sessions are expected to keep `TimeFine = NULL`
- legacy zero-date placeholders such as `0000-00-00 00:00:00` are not the supported contract anymore
- the native repository is also defensive against legacy MySQL zero-date behavior by:
  - avoiding strict-mode-unsafe SQL such as `NULLIF(datetime_column, '0000-00-00 00:00:00')`
  - enabling connector-side zero-date conversion for read-only inspection queries

Current rejected-piece mode in the main project profile:

- `ClassificationRejected`

This means the default query path considers:

- `Esito_Classificazione = 0`

as rejected.

## Image resolution behavior

Image resolution happens through:

- [LocalPieceEvidenceResolver.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Inspector/Services/LocalPieceEvidenceResolver.cs)

Resolution logic:

1. start from `PieceData`
2. if the piece path is relative, combine it with `ImageRootPath`
3. inspect the resolved piece folder
4. classify source and processed images according to the naming rules already validated in the standalone companion

Current expected image root:

- the directory configured in `Configuration.ImageDir`

## Current user workflow

From the operator point of view, the current workflow is:

1. open the `DataInspector` page from the HMI navigation
2. review the recent rejected pieces list
3. select one piece
4. check:
   - item summary
   - inspection context
   - storage details
   - source images
   - processed images
5. compare the visual evidence and defect descriptors
6. from release `3.0.1.1`, click a source or processed preview to open a fullscreen zoom overlay
7. use mouse wheel or the `+` / `-` / `1:1` controls to inspect small defects
8. when external support or quality evidence is needed, use `Scarica evidenza` to export the available images and `detail.txt`

The current page is intentionally simple:

- no editing
- no machine-side actions
- no DB writes
- evidence download is a file-system export of already available images/details; it does not alter the database or machine runtime

This keeps the page safe for operators and support staff.

## Connection and failure behavior

The current native page exposes data-source status through the viewmodel.

Expected user-facing states:

- connected
- not connected

If initialization fails, the code-behind currently shows a warning message box:

- [DataInspectorView.xaml.cs](/d:/Pulsar/Developer/NewPanel/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml.cs)

This is acceptable for the current fusion stage, but later it should move to:

- clearer in-page user feedback
- language-key based messaging
- less code-behind responsibility

## What is already fused

Already fused into the main project:

- native host view
- DB read-side
- image resolution
- operator review surface
- local runtime-profile mapping
- fullscreen zoom for source and processed images
- evidence package download for support/quality review

Not yet fully fused:

- advanced filters from the standalone companion
- formal PDF/report actions beyond the `3.0.1.1` evidence package
- remote storage bridge options
- full parity with the standalone Inspector companion

## How to validate locally

### Build

Use the restored local build path:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' `
  'C:\Users\chouikha\QtisVisionPanel\QtisVisionPanel.csproj' `
  /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo /m
```

### Runtime

1. start the HMI in `Debug | x64`
2. navigate to `DataInspector`
3. verify that:
   - the page opens natively
   - rejected pieces load from `quatisv`
   - selecting a piece updates the right-side detail panels
   - images resolve from `C:\QtisVision\Pieces\`
   - clicking a source/processed image opens the zoom overlay
   - wheel zoom and `+` / `-` / `1:1` controls work
   - `Scarica evidenza` creates a folder containing available images and `detail.txt`

## Maintenance notes

When changing this area:

- do not introduce write behavior without an explicit design decision
- do not bypass `PieceData` with hardcoded disk assumptions
- do not duplicate config in a second main-project Inspector file
- keep the main-project version aligned with the validated standalone companion contracts

If a new feature is being considered, the order should remain:

1. validate it in `QuatisVisionInspector`
2. document the seam and migration reason
3. port only the needed slice into `QtisVisionPanel`

## Related documents

- [inspector-native-fusion-slice-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-native-fusion-slice-2026-03-26.md)
- [baseline-build-restoration-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/baseline-build-restoration-2026-03-26.md)
- [inspector-standalone-preparation.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-standalone-preparation.md)
- [phase-5-main-project-baseline-restored.md](C:/Users/chouikha/QuatisVisionInspector/Documentation/Inspector/phase-5-main-project-baseline-restored.md)
