# Inspector Standalone Preparation

## Purpose

This document records how the standalone `QuatisVisionInspector` was aligned to the refreshed `QtisVisionPanel` baseline before the first native fusion slice was prepared.

Related standalone repository:
- [QuatisVisionInspector](C:/Users/chouikha/QuatisVisionInspector)

## Original Main-Project State

At the start of this preparation work, the `DataInspector` entry in the main HMI was a `WebView2` wrapper for an external page configured through `Configuration.LogshowImage`.

Relevant files:
- [Views/UserControls/DataInspectorView.xaml](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml)
- [Views/UserControls/DataInspectorView.xaml.cs](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml.cs)
- [Services/ViewFactoryService.cs](C:/Users/chouikha/QtisVisionPanel/Services/ViewFactoryService.cs)

## Important Implication

There is no complex native inspector module to preserve in this repo.
The future integration problem is therefore:
- replace the external browser-style dependency
- keep compatibility with the production data schema
- integrate a native review experience into the HMI later

That first native step is now documented in:
- [inspector-native-fusion-slice-2026-03-26.md](C:/Users/chouikha/QtisVisionPanel/Documentation/MachineHardware/inspector-native-fusion-slice-2026-03-26.md)

## Stable Boundaries To Preserve

The main boundaries currently worth preserving are:
- `tblgenerale` production piece data
- `tblproduzione` production session context
- saved image folder structure from `SaveImage`
- language and navigation semantics from the current HMI

Current local image-root baseline:
- `C:\QtisVision\Pieces\`

## Standalone Alignment Strategy

The standalone `QuatisVisionInspector` is being prepared with:
- a portable core layer for piece-history and evidence contracts
- no dependency on Apache/PHP/FileZilla
- no dependency on `WebView2`
- no dependency on `MainWindow` runtime state

This keeps the future fusion path cleaner.

## Rule For Future Integration

The rule that guided the first fusion slice remains valid:
- move contracts and services first
- rebuild the internal HMI view over those contracts
- remove the external-page bridge only after the native module is validated
