# Post-Switch Language Hardening 2026-03-30

## Purpose

After promoting the colleague-aligned baseline to the active local repo, a focused language-system audit was run to reduce the risk of:

- raw keys appearing in the UI
- fragile refresh behavior after language changes
- regressions caused by older legacy lookup patterns

This note records what was verified and what was hardened.

## Scope Checked

The audit covered:

- [ServerMessageStructure.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)
- runtime language packs:
  - `C:\QtisVision\Language\messages_eng.json`
  - `C:\QtisVision\Language\messages_ita.json`
- main navigation and settings refresh flow
- fused Inspector wording and fallback behavior

## Contract Check

The runtime packs were compared against the current string properties declared in:

- [ServerMessageStructure.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)

Result:

- no main-project string property is missing from the runtime packs
- the only extra name found by the mechanical comparison is `key`, which comes from the JSON wrapper structure and is not a real UI message contract issue

## Residual Structural Observation

Many `Sub_entry_*` names still exist directly in XAML but are not represented as explicit string properties inside `ServerMessageStructure.cs`.

This is a legacy pattern used especially in areas such as:

- preferences
- assistance/manual
- diagnostics/detail views

These are not immediate blockers because some views still resolve them dynamically through the runtime language pack, but they are a maintenance risk because:

- they bypass the strongly-typed message contract
- they are easier to miss during future audits

## Hardening Applied

### 1. Navigation refresh fallback was made safer

Updated:

- [NavigationViewModel.cs](C:/Users/chouikha/QtisVisionPanel/ViewModels/NavigationViewModel.cs)

Change:

- menu-item text refresh now uses `GetMessageOrDefault(...)`
- if a localized key is missing, the menu keeps its current visible label instead of relying on raw-key comparison logic

### 2. Preference view runtime refresh now uses safe fallback

Updated:

- [PreferenceViewModel.cs](C:/Users/chouikha/QtisVisionPanel/ViewModels/PreferenceViewModel.cs)

Change:

- recursive UI refresh now uses `GetMessageOrDefault(...)` for `TextBlock`, `Button` and `Label`
- this avoids fragile direct `GetMessage(...)` behavior during language refresh
- if a translation is missing, the current visible text is preserved

## Risk Assessment After Hardening

Current status:

- build remains green
- the main language contract is structurally covered
- fused `DataInspector` remains aligned with the shared main-project language system
- navigation/settings are less likely to expose raw keys during runtime refresh

Remaining medium-risk area:

- legacy `Sub_entry_*` names in XAML that still depend on dynamic lookup instead of explicit typed contract entries

These should be cleaned progressively, not with a broad disruptive refactor.

## Recommended Next Step

If more language hardening is needed later, the safest next slice is:

1. preferences
2. system diagnostics
3. assistance/manual

The goal should be:

- move the most important remaining legacy `Sub_entry_*` names into `ServerMessageStructure.cs`
- keep runtime JSON packs synchronized
- avoid mass migration without testing the affected views
