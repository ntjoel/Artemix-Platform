# DataInspector UX Refinement 2026-03-30

> Nota storica: questo documento descrive la slice del 30/03/2026. La classe
> `InspectionResultBadgeViewModel` citata sotto non aveva consumer nella baseline corrente ed e'
> stata rimossa nell'audit del 21/07/2026. Per lo stato attuale usare
> `architecture-and-modules.md` e `full-application-audit-2026-07-21.md`.

## Goal

Refine the native `DataInspector` inside the main HMI so that:

- images become the primary focus of the page
- reject reasons are easier to read at a glance
- repeated detail text is reduced
- the layout feels clearer for operators and customer-side users, not only technical staff

## Files changed

- [DataInspectorView.xaml](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml)
- [DataInspectorView.xaml.cs](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/DataInspectorView.xaml.cs)
- [DataInspectorViewModel.cs](C:/Users/chouikha/QtisVisionPanel/Inspector/ViewModels/DataInspectorViewModel.cs)
- [ImageArtifactViewModel.cs](C:/Users/chouikha/QtisVisionPanel/Inspector/ViewModels/ImageArtifactViewModel.cs)
- [InspectionResultBadgeViewModel.cs](C:/Users/chouikha/QtisVisionPanel/Inspector/ViewModels/InspectionResultBadgeViewModel.cs)
- [MainWindow.xaml](C:/Users/chouikha/QtisVisionPanel/MainWindow.xaml)
- [NavigationMenu.xaml](C:/Users/chouikha/QtisVisionPanel/Views/NavigationMenu.xaml)
- [ControlButtons.xaml](C:/Users/chouikha/QtisVisionPanel/Views/UserControls/ControlButtons.xaml)
- [QtisVisionPanel.csproj](C:/Users/chouikha/QtisVisionPanel/QtisVisionPanel.csproj)
- [ServerMessageStructure.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)
- `C:\QtisVision\Language\messages_eng.json`
- `C:\QtisVision\Language\messages_ita.json`

## UX changes

### 1. Header made more useful

The blue top header now exposes:

- selected image full path

This keeps the selected-image path in the top information strip and removes the redundant lower path card, freeing space for the actual review surface.

### 2. More space given to image review

The image panels were rebalanced so that:

- the left list column is slightly narrower
- image preview panels are taller
- image preview areas are individually scrollable when zoomed
- source and processed previews remain visible as the main comparison surface
- the default opened image fits the available preview area instead of appearing already enlarged
- the upper summary/detail bands consume less vertical space, leaving more room for the actual review images

### 3. Detail cards simplified

The lower information cards previously repeated the same status and defect information in multiple places.

The new structure keeps:

- item details:
  - reject reasons as colored badges
  - compact timestamp / batch / traceability boxes
  - one short free-text note

Removed from the visible layout:

- the separate lower path/storage card that duplicated the selected image path already shown in the top header
- multiple repeated text lines that duplicated the reject reason already visible elsewhere
- extra detail separation that consumed vertical space without helping operator understanding

### 4. Reject reasons highlighted visually

Reject reasons now use a dedicated viewmodel:

- `InspectionResultBadgeViewModel`

The view maps inspection results into badges with:

- red emphasis for rejected / failed reasons
- amber emphasis for reported / softer conditions
- blue fallback styling for neutral items

This is intended to let an operator immediately understand why the piece was rejected before even reading the full note.

### 5. Zoom support

Zoom support is intentionally simpler now:

- the image opens at the normal fitted scale
- zoom is adjusted only with the mouse wheel while the pointer is over the preview
- source and processed previews now keep independent zoom factors

The goal is to keep the interaction natural for operators: open, inspect, wheel-zoom only when needed, without unexpectedly changing the opposite image.

### 6. Sidebar and shell spacing tuned

The main shell was also adjusted so the page feels less cramped:

- left navigation column widened slightly
- navigation header spacing and clock card padding improved
- menu buttons use more balanced padding and spacing
- bottom `Start / Stop / Close` buttons now use a uniform three-column layout so labels are less likely to look compressed or clipped

## Language alignment

The UX refactor continues to use the main-project shared language system.

New keys added in the previous refinement and still used by the current page:

- `Sub_entry_DataInspectorRejectReasons`
- `Sub_entry_DataInspectorSelectedImagePath`
- `Sub_entry_DataInspectorLiveDataReadFailed`
- `Sub_entry_DataInspectorEvidenceLoadFailed`

Runtime packs updated:

- `C:\QtisVision\Language\messages_eng.json`
- `C:\QtisVision\Language\messages_ita.json`

## Build verification

Verified with:

- `MSBuild QtisVisionPanel.csproj /p:Configuration=Debug /p:Platform=x64`

Result:

- build succeeded
- no new blocking compile issues introduced
- remaining warnings are legacy warnings already present in the project baseline

## Fusion impact

This refinement stays compatible with the current fused architecture:

- no DB schema change
- no repository contract change
- no runtime-path contract change
- no change to machine or recipe flows

It is a presentation-layer and interaction improvement only, built on top of the already fused native `DataInspector`.
