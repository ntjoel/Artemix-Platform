# New Baseline Fusion Preparation Plan

## Scope

This document defines the preparation plan for the future fusion between:
- the refreshed main project `QtisVisionPanel`
- the standalone machine/commissioning tool `QuatisVisionPanelIO`

This plan is intentionally based on the **new consolidated baseline**:
- main project repo: `C:\Users\chouikha\QtisVisionPanel`
- external runtime assets: `C:\QtisVision`
- standalone tool repo: `C:\Users\chouikha\QuatisVisionPanelIO`

## Goal

Prepare the next UI and structural changes in the standalone tool so that, when fusion time comes, the move into the main project will be:
- semantically aligned
- architecturally safer
- less invasive
- less confusing for future maintainers

This is a preparation plan, not a direct fusion plan.

## Non-negotiable constraints

1. machine configuration must remain distinct from recipe persistence
2. the main project must not copy the standalone tool UI one-to-one
3. the standalone tool must not import legacy coupling from the main project just to look similar
4. runtime machine logic must not be pushed into `MainWindow`
5. fusion must happen by bringing models and runtime controllers, not by cloning pages

## What the refreshed baseline changes in the plan

Compared to the earlier working assumption, the refreshed baseline adds three important realities:

### 1. The main project now has a stronger runtime/service surface

Relevant seams now exist through:
- `MachineRuntimeService`
- `MachineStatusService`
- `ApplicationEventLogger`
- `ServiceLocator`

This means the future fusion should target those seams, not try to invent a completely parallel runtime shell.

### 2. The main project now has a stronger operator-facing documentation layer

The new `Docs\Manual` content means the main application already has an operator-oriented information surface.

So the standalone tool should remain an engineering/commissioning companion, not drift into becoming a second operator HMI.

### 3. The main project has a richer but messier language/runtime asset model

The existence of `C:\QtisVision\Language`, the update script, and the current message model means the tool must be prepared for convergence, but not coupled prematurely.

## Preparation workstream A - Tool UI alignment

### Objective

Adapt the standalone tool UI so that its concepts align with the refreshed main project without turning the tool into a copy of the HMI.

### Recommended direction

The tool should continue to feel like an engineering companion application.

Before fusion, its UI should be adjusted to emphasize:
- commissioning
- machine configuration
- encoder/position tuning
- event diagnostics
- board and signal verification

It should **not** try to mimic:
- operator dashboard flows
- production navigation patterns
- recipe-operating screens already owned by the main project

### Practical UI adaptations to plan next

1. Normalize page names and section wording against the main project vocabulary where meaning truly overlaps.
2. Keep machine-specific pages clearly machine-specific:
   - `Signals and Config`
   - `Encoder`
   - `Commissioning`
   - `Machine Events`
3. Reduce labels that sound like full production HMI features when they are actually engineering diagnostics.
4. Keep the tool shell compact and technical rather than operator-facing.

## Preparation workstream B - Tool structural alignment

### Objective

Prepare the tool data/contracts so that future fusion can move models and runtime behavior, not UI fragments.

### Recommended direction

The tool should keep strengthening these boundaries:
- machine configuration
- runtime state
- diagnostics/events
- localization/messages

### Practical structural changes to plan next in the tool

1. Stabilize the machine-configuration contract
   - versioned config format
   - explicit schema ownership
   - clearer field grouping by machine/runtime purpose

2. Keep machine values canonical and language-neutral
   - signal codes
   - action codes
   - runtime modes
   - intervention point identifiers

3. Prepare export/import clarity
   - make it easier to say which fields belong later to `MachineConfiguration`
   - which ones remain tool-only diagnostics
   - which ones may become runtime-only in the main project

4. Keep event taxonomy consistent
   - the tool event categories should be made easy to map onto `ApplicationEventLogger`

## Preparation workstream C - Language convergence

### Objective

Prepare the tool so that future convergence with the main project language system is possible without importing current main-project debt.

### Recommended tool changes before fusion

1. Keep the tool localization loader local.
2. Add support for multiple local language packs with English fallback.
3. Continue maintaining a clean machine-runtime key inventory.
4. Split keys into:
   - shared generic keys
   - machine-runtime shared keys to be promoted later
   - tool-only keys
5. Do not adopt the main project's giant typed message class.

### Recommended main-project awareness

When future convergence starts, the target should be:
- shared vocabulary
- shared pack schema
- cleaner service behavior

The target should **not** be:
- direct reuse of the current `ServerMessagePersonalize` implementation inside the tool

## Preparation workstream D - Main-project runtime insertion points

### Objective

Identify where future machine logic should enter the refreshed main project.

### Recommended insertion points

Future fusion should target new dedicated layers around these existing seams:
- `MachineRuntimeService`
- `ApplicationEventLogger`
- `MachineStatusService`
- recipe-loading boundary in `AsyncRecipeParam` / `RecipeParameters`

### Required future controllers

When fusion actually starts, the likely dedicated controllers should be introduced as separate services/layers such as:
- `MachineConfigurationService`
- `MachineRuntimeContext`
- `MachineCycleController`
- `TrackedProductRuntime`
- `MachineInterventionScheduler`
- `VisionResultMatcher`

These should not be implemented inside `MainWindow`.

## Preparation workstream E - Recipe boundary

### Objective

Keep the future bridge between tool and main project compatible with the existing recipe model.

### Rule

Only product-specific overrides belong later in recipe data.

Still-valid minimal direction:
- optional `MachineRuntimeOverrides`

Allowed examples:
- `TriggerOffsetMm`
- `LightsOnOffsetMm`
- `LightsOffOffsetMm`
- `RejectOffsetMm`
- custom event offsets

Not allowed in recipe:
- board/channel mapping
- encoder geometry
- machine zero
- photocell absolute machine position
- signal ownership

## Preparation workstream F - Logging and events

### Objective

Align future machine runtime diagnostics with the refreshed main-project event/logging model.

### Direction

The standalone tool should prepare event names and categories so they can later map naturally to:
- application operational events
- runtime health events
- machine status transitions
- recovery/fault traces

This avoids a future situation where the tool speaks one diagnostics language and the main project speaks another.

## Recommended implementation order before fusion

### Phase 1 - documentation and contracts
- complete the updated baseline analysis
- keep the machine/recipe/runtime split explicit
- refine the machine-config contract in the tool

### Phase 2 - standalone tool preparation
- improve tool localization readiness
- align vocabulary with the refreshed main project where appropriate
- refine event naming and config ownership in the tool

### Phase 3 - main project technical prep
- identify where dedicated runtime controllers will sit
- document required recipe-extension points without implementing them yet
- reduce ambiguity in runtime initialization paths

### Phase 4 - actual fusion planning
- choose the minimum first integration slice
- define validation strategy
- only then start coding the integration

## Best candidate for the first real fusion slice later

The safest first slice is likely:
- machine configuration read model
- optional recipe overrides read model
- runtime context assembly
- no operator UI fusion yet

That keeps the first integration mostly structural and minimizes regressions.

## Bottom line

The refreshed baseline changes the preparation strategy in a good way:
- the main project is now richer in services, docs, and runtime surfaces
- the standalone tool can be adapted more intelligently before fusion
- the future integration can target real seams instead of forcing everything through `MainWindow`

The correct next move is **not** immediate fusion.

The correct next move is to evolve the tool so it matches the refreshed baseline semantically and structurally, while staying autonomous until the machine/runtime merge is truly ready.
