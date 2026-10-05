# Main Project Language System vs Standalone Tool Localization

## Scope

This document compares:
- the refreshed `QtisVisionPanel` language system
- the current `QuatisVisionPanelIO` localization model

Goal:
- understand what is already compatible
- identify what must change in the tool before future fusion
- avoid copying legacy constraints from the main project into the standalone tool

## Current architecture in the main project

The main project currently uses an external runtime asset model.

Key components:
- `Cls_Config\AsyncPreferenceConfigManager.cs`
- `ServerMessage\ServerMessagePersonalize.cs`
- `ServerMessage\ServerMessageStructure.cs`
- `C:\QtisVision\Language\messages_*.json`

### Current behavior

The project stores language-related settings in `PreferenceViewConfig`, including available languages and language-folder paths.

However, the active runtime loader still reads the legacy application language setting and then scans:
- `C:\QtisVision\Language\*.json`

So the refreshed baseline still has two overlapping models:
- language settings in preference configuration
- actual runtime loading through the legacy message loader

## Current architecture in the standalone tool

The standalone tool currently uses a local, packaged localization model.

Key components:
- `Services\ToolLocalizationService.cs`
- `Models\ToolMessageCatalog.cs`
- `Localization\messages_eng.json`

### Current behavior

The tool:
- loads its own local pack from the application folder
- uses dictionary-based lookup
- supports fallback to the key or provided fallback text
- has no external dependency on `C:\QtisVision\Language`

This makes the tool simpler and cleaner for engineering use.

## Shared compatibility already present

Both systems already share an important common point:

```json
{
  "key": "ENG",
  "messages": { ... }
}
```

This means the JSON envelope itself is compatible.

That is useful.

The incompatibility is not the file wrapper. The incompatibility is the service/model behind it.

## Main differences

### 1. Strongly typed vs dictionary-driven messages

Main project:
- deserializes into a strongly typed `Messages` class
- adding many new keys usually implies code/model maintenance

Tool:
- deserializes into `Dictionary<string,string>`
- new keys are data-driven and do not require expanding a giant property class

Practical reading:
- the tool model is cleaner for future growth
- the main-project model is more rigid and carries more maintenance debt

### 2. External runtime dependency vs local autonomy

Main project:
- language files live in `C:\QtisVision\Language`
- runtime depends on external file deployment and path correctness

Tool:
- localization pack is shipped locally with the executable
- no external language-folder dependency exists today

Practical reading:
- keep the tool autonomous for now
- do not point the tool to `C:\QtisVision\Language` before a proper merge plan exists

### 3. Language-setting duplication in the main project

The main project currently has:
- preference-layer language information
- legacy runtime loader logic

This is a debt in the main project, not something the tool should copy.

### 4. Key-space overlap is still low

The refreshed baseline and the current tool are not close enough for direct pack reuse.

The tool already has its own machine-runtime vocabulary covering:
- commissioning
- encoder diagnostics
- signal mapping
- heartbeat
- intervention points
- machine events

That vocabulary does not exist cleanly in the main project yet.

## What must change in the tool before future fusion

### Keep the loader local

The standalone tool should remain locally packaged and independent.

Do **not** replace its localization service with the main-project loader.

### Add multi-pack readiness locally

The next evolution of the tool should be:
- local `Load(languageCode)` support
- English fallback
- support for more than one local pack inside `Localization\`

This should happen without external coupling.

### Freeze the shared-key policy

The tool should classify keys into three groups:
- reusable shared generic keys
- reusable after wording cleanup
- new shared machine-runtime keys

This classification is already conceptually started in:
- `tool-message-key-compatibility-matrix.md`

### Keep runtime values language-neutral

These must remain canonical internal values, not translated runtime identifiers:
- `Internal`
- `External`
- `Hybrid`
- `EncoderTracked`
- `Immediate`
- signal codes
- action codes
- event identifiers

If they need UI translation later, use labels, not translated runtime values.

### Do not adopt the giant typed-message class from the main project

If there is future convergence, it should move toward:
- dictionary/schema validation
- generated accessors if needed
- centralized service behavior

The direction should **not** be to force the tool into a manually maintained typed `Messages` class.

## What must eventually change in the main project

This is not for immediate implementation, but it matters for planning.

The refreshed main project still carries localization debt:
- dual language-setting sources
- hard dependency on `C:\QtisVision\Language`
- typed message coupling
- partial/manual UI refresh model
- incomplete key parity between language packs

This means the main project should eventually move toward:
- one canonical localization service
- one canonical language selection source
- better fallback policy
- less reflection-based access
- less manual UI refresh logic

## Practical pre-fusion guidance

Recommended order:

1. keep the tool independent
2. treat the tool English pack as the clean machine-runtime reference
3. continue aligning naming semantics, not by copying the main-project implementation blindly
4. promote a shared key inventory only for concepts that are truly common
5. keep machine/runtime semantics language-neutral in both systems
6. only later, refactor the main project toward a cleaner service model

## Bottom line

The tool is already closer to the architecture we want long-term.

The main project is richer as a multilingual runtime baseline, but also carries more localization debt.

Therefore the correct convergence path is:
- do not copy the current main-project loader into the tool
- adapt the tool to be locally multilingual-ready
- define a shared vocabulary gradually
- later modernize the main-project localization layer instead of inheriting its current rigidity into the tool
