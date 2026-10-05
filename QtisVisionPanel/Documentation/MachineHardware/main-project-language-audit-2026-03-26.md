# Main Project Language Audit

## Goal

Record the post-fusion language audit executed on the refreshed local `QtisVisionPanel` baseline after the native `DataInspector` integration.

This note is intended for:

- teammate handover
- future AI-assisted continuation
- regression checks when new UI pages are added

## Scope

The audit covered:

- keys used through `ServerMessagePersonalize.GetMessage(...)`
- keys used through `ServerMessagePersonalize.GetMessageOrDefault(...)`
- direct usages of `ServerMessagePersonalize.CurrentMessages.messages.<Property>`
- the strongly typed message model in [ServerMessageStructure.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)
- the runtime packs:
  - `C:\QtisVision\Language\messages_eng.json`
  - `C:\QtisVision\Language\messages_ita.json`

## Findings

### 1. Runtime packs were not fully aligned with the strongly typed message model

The refreshed baseline contained a mismatch between:

- the message properties declared in `ServerMessageStructure`
- the keys physically present in the runtime JSON language files

This was especially visible in:

- `PreferenceView`
- diagnostics-related pages
- recently fused pages such as the native `DataInspector`

Because several views still use direct property access on `CurrentMessages.messages`, missing keys could surface as:

- raw key names
- incomplete labels
- mixed-language UI

### 2. The issue was broader than a single page

The problem was not limited to `Settings`.

The audit confirmed that the gap affected the main-project language system at a shared infrastructure level:

- model and packs were drifting
- newly added keys could exist in code but not in both runtime files
- some older values were present but had poor quality due to encoding artifacts

### 3. DataInspector is now aligned to the shared main-project language entity

The native `DataInspector` does not use a standalone language service anymore.

It is now fully aligned to the main-project language system via:

- [ServerMessagePersonalize.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessagePersonalize.cs)
- [ServerMessageStructure.cs](C:/Users/chouikha/QtisVisionPanel/ServerMessage/ServerMessageStructure.cs)

This keeps the fused Inspector inside the same runtime language flow as the rest of the HMI.

## Actions performed

### A. Full synchronization of runtime packs with the message model

Both runtime files were synchronized so that all string properties declared in `ServerMessageStructure` are now present in:

- `messages_eng.json`
- `messages_ita.json`

Result:

- no structural gaps remain between the strongly typed message model and the runtime packs
- newly used keys in current UI pages are no longer missing from one language file only

### B. Preference / settings area made safe from unmapped keys

The `PreferenceView` relies heavily on direct property access.

Because of that, the main practical priority was to ensure that all settings-related keys are physically present in the runtime packs.

This area is now covered.

### C. Low-quality / mojibake values cleaned up in the runtime packs

Several runtime values had encoding artifacts such as:

- `Ã`
- `â`
- `ð`

Visible cases were cleaned up in the current local runtime packs, especially around:

- button labels
- status messages
- Italian strings with accented characters

## Validation result

After synchronization:

- all string properties from `ServerMessageStructure` are present in `messages_eng.json`
- all string properties from `ServerMessageStructure` are present in `messages_ita.json`
- no currently used message key was left structurally unmapped in the runtime packs
- the `PreferenceViewModel` status, save/load, user-management, and path-test messages are now also routed through the shared language system instead of remaining hardcoded in Italian

## Remaining caution

This audit removes the structural “missing key” class of issues for the current baseline.

What can still remain in the future:

- hardcoded strings in legacy views not yet brought into the language system
- low-quality translations that are readable but not yet ideal
- newly added keys if developers update `ServerMessageStructure` without also updating the runtime packs

## Recommended rule going forward

Whenever a new localized label is introduced:

1. add the property to `ServerMessageStructure`
2. add the key to `messages_eng.json`
3. add the key to `messages_ita.json`
4. prefer `GetMessageOrDefault(...)` for new code paths unless there is a strong reason to use direct property access

This is the minimum rule needed to keep the main-project language system coherent.
