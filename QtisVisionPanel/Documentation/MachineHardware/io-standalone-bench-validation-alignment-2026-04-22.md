# I/O Standalone Bench Validation Alignment - 2026-04-22

## Purpose

This document records what has been validated on the standalone I/O tool and what the main project must consider aligned from now on.

It exists to prevent loss of context between:

- bench commissioning on `QuatisVisionPanelIO`
- future machine-runtime work inside `QtisVisionPanel`

## Confirmed on real hardware

The following chain has been validated successfully on the standalone tool with real hardware:

- photocell input
- Advantech input acquisition
- standalone software reaction
- digital output command
- illuminator enable

Validated behavior:

- photocell occupied -> `DI00 ON` -> `DO00 ON` -> illuminator ON
- photocell free -> `DI00 OFF` -> `DO00 OFF` -> illuminator OFF

## Reference mapping used in the bench

Input:

- `IN_PRODUCT_PHOTOCELL -> DI00`

Output:

- `OUT_LIGHT_TOP -> DO00`

Hardware path:

- `PCIE-1756`
- `PCL-10250-2E`
- `ADAM-3951-BE` for input
- `ADAM-3951-BE` for output

## Electrical behavior confirmed

### Inputs

- `0V -> ECOM`
- `+24V or sensor output -> IDIx`

### Isolated sink outputs on `PCIE-1756`

- `+24V -> PCOM`
- `0V -> IGND`
- load between `PCOM/+24V` and `IDOx`

This must now be considered the validated working reference for the family machine bench used in this project.

## Important software conclusion

The standalone tool required a dedicated commissioning mode:

- `PhotocellLightingBenchModeEnabled`

That mode is correct for:

- first automatic hardware validation
- proving input -> software -> output chain
- bench work before encoder-based runtime is commissioned

But it is **not** the final machine logic for the main project.

## What the main project should align to

The main project must align to the following decisions:

1. keep the validated signal mapping vocabulary
2. keep the validated electrical interpretation of `PCIE-1756` sink outputs
3. preserve the distinction between:
   - commissioning bench logic
   - production machine runtime
4. reuse the validated naming and intent of:
   - `IN_PRODUCT_PHOTOCELL`
   - `OUT_LIGHT_TOP`

## What should not be copied blindly

The following should **not** be imported as final production logic:

- direct permanent mirror `photocell -> light output`
- bench-only shortcuts implemented for diagnostics
- UI-specific commissioning controls from the standalone tool

## What should be ported conceptually

The main project should carry over:

- the fact that the photocell event is reliable on real hardware
- the fact that the lighting output command path is reliable on real hardware
- the fact that the first software-driven automatic reaction has already been validated
- the root-cause fix learned during bench work:
  - input transitions must be handled from a correct previous-state cache
  - UI state alone is not enough to prove runtime events are firing

## Correct production-oriented next step

Inside `QtisVisionPanel`, the professional machine flow remains:

1. photocell creates product detection
2. encoder tracks product motion
3. intervention scheduler executes:
   - `LIGHTS_ON`
   - `CAMERA_TRIGGER`
   - `LIGHTS_OFF`
   - `REJECT`

So the standalone bench validation should be considered:

- completed and useful
- a commissioning milestone
- not the endpoint of the machine runtime

## Cross-reference

For the full technical history of the standalone validation, read:

- `C:\Users\chouikha\QuatisVisionPanelIO\Documentation\MachineHardware\bench-validation-photocell-lighting-chain-2026-04-22.md`

For the future architecture direction, keep aligned with:

- `future-integration-main-project.md` in the standalone repo
- `current-unified-baseline-2026-03-27.md`
- `integration-architecture-and-code-guide-2026-03-27.md`
