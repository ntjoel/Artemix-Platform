# I/O Main Project Alignment Update - 2026-04-23

## Purpose

This note records the alignment work performed on the local main project after the standalone I/O bench validation was completed.

The goal is to keep `QtisVisionPanel` technically aligned with the validated I/O behavior already proven on the standalone companion, before continuing with encoder-focused work.

## What was already validated outside the main project

Validated on `QuatisVisionPanelIO`:

- real photocell input acquisition on `PCIE-1756`
- real digital output command on `PCIE-1756`
- automatic software-driven reaction:
  - `DI00` photocell
  - `DO00` top lighting
  - illuminator enable chain
- confirmed sink-output electrical behavior:
  - `+24V -> PCOM`
  - `0V -> IGND`
  - load between `PCOM/+24V` and `IDOx`

Reference mapping used during the bench:

- `IN_PRODUCT_PHOTOCELL -> DI00`
- `OUT_LIGHT_TOP -> DO00`

## Why the main project needed alignment

During the standalone bench validation, a real issue was found in the Advantech input event path:

- input state could appear correct in UI
- but automatic logic depending on state transitions could fail

Root cause:

- the input read methods updated the cached last-state too early
- this could prevent correct transition detection in polling-based runtime logic

This is not only a standalone concern. It belongs to the shared I/O behavior of the Advantech integration and therefore must be aligned in the main project as well.

## Code alignment applied

Updated:

- `Models\\AdvantechDeviceManager.cs`

Change:

- `ReadAllInputsAsync()` now returns the freshly read 1756 buffer instead of overwriting `_lastInputs1756` prematurely
- `ReadDigitalInput1884Async()` now returns the freshly read masked 1884 input value instead of overwriting `_lastInputs1884` prematurely

Result:

- polling logic can compare previous cached values against newly read hardware values correctly
- transition-based logic is now aligned with the standalone fix

## Build note on the local main project

A local build check was executed after the alignment.

Result:

- the project is still not build-clean
- failures remain dominated by pre-existing baseline issues:
  - generated XAML / `InitializeComponent`
  - missing named controls in several views
  - view/code-behind inconsistencies already present in the current local baseline

So:

- the I/O alignment change itself is low-risk and localized
- the current build failure cannot be attributed to this specific I/O update

## What was intentionally not ported directly

The following was **not** copied into the main project as final runtime logic:

- `PhotocellLightingBenchModeEnabled`
- direct permanent mirror `photocell -> top lighting`
- standalone commissioning UI behavior

Reason:

- those belong to bench validation and commissioning support
- the main project must keep the production-oriented runtime model:
  - photocell -> product birth
  - encoder tracking
  - intervention points
  - `LIGHTS_ON / TRIGGER / LIGHTS_OFF / REJECT`

## Current aligned state of the main project

As of this update, the main project is aligned to the standalone work on these I/O points:

- validated electrical interpretation of `PCIE-1756` digital inputs and sink outputs
- validated naming and intent of:
  - `IN_PRODUCT_PHOTOCELL`
  - `OUT_LIGHT_TOP`
- validated knowledge that the first automatic I/O chain works on real hardware
- corrected shared low-level input transition handling in the Advantech manager

## Next recommended step

The correct next technical block is encoder-focused work:

1. validate `PCIE-1884` and encoder channel behavior
2. confirm machine zero and photocell machine position
3. move from bench mirror logic to:
   - encoder-based product tracking
   - intervention-point runtime
   - `LIGHTS_ON / TRIGGER / LIGHTS_OFF`

## Cross-reference

Related documents:

- `io-standalone-bench-validation-alignment-2026-04-22.md`
- `Docs/Commissioning/05_Preconfigured_Signal_Matrix.md`
- `Docs/Commissioning/06_PCIE1756_2xADAM3951_Navigator_Test.md`
- standalone repo:
  - `bench-validation-photocell-lighting-chain-2026-04-22.md`
