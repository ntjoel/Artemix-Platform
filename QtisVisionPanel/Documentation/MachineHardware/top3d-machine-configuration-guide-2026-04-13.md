# Top3D Machine Configuration Guide

## Scope

This guide documents the `3DCheck` machine variant where the active VisionPro job is a single `Top3D` profilometer job and the HMI reuses the standard `TopCameraView` surface to display the last run image / point-cloud projection.

The goal of this baseline is:

- keep the current production runtime contract driven by `Config.xml`
- separate `Top3D` recipe parameters from the historical `Front` traceability recipe section
- store `Top3D` recipe parameters and 3D inspection results in the database with the same persistence discipline already used by the other inspections

## Runtime model

### Machine type

Set the machine type in `Config.xml`:

- `Configuration.MachineType = 3DCheck`

The machine type stays in the machine/runtime configuration and must not be duplicated into the recipe XML.

### Active job naming

The active QuickBuild job for the profilometer must expose a semantic job name compatible with `Top3D`, for example:

- `Top3D`
- `Top_3D`
- `Top-3D`
- names containing both `Top` and `3D`
- names containing `profilomet...`

The runtime now gives priority to the QuickBuild job name over the numeric ID in `CameraConfig.xml`.

### Camera view

`Top3D` uses the standard `TopCameraView` surface:

- same top display host
- same top record display
- same `LastRunView1` slot
- same `cameraSetting.TopCameraTriggerDelay`

This means the 3D machine does not need a dedicated `FrontCameraView` to render the profilometer output.

### Display record paths

For operator display, the recipe may override the two LastRun views with:

- `recipeParamTop3D.Top3DPrimaryLastRunView`
- `recipeParamTop3D.Top3DSecondaryLastRunView`

The display resolver accepts both full QuickBuild-style paths and paths relative to the `LastRun` record.
These examples are valid when they point to the same VisionPro record:

- `Measure.Height.InputImage`
- `Height.InputImage`
- `LastRun.Height.InputImage`

The same rule applies to the secondary planar view, for example:

- `Measure.CogPixelMapTool1.OutputImage`
- `CogPixelMapTool1.OutputImage`

This display path contract is independent from the optional Top3D artifact outputs
(`Top3DRerenderResult`, `Top3DPointCloud`). Missing artifact outputs must not prevent the
operator Overview from showing the configured Top3D / Top2D images.

## Recipe XML contract

### Dedicated recipe section

The recipe XML now stores 3D thresholds in:

- `recipeParamTop3D`

Fields:

- `ThreeDHeightNominalValue`
- `ThreeDHeightTolerance`
- `ThreeDWidthNominalValue`
- `ThreeDWidthTolerance`
- `ThreeDLengthNominalValue`
- `ThreeDLengthTolerance`

### Legacy compatibility

Older recipes may still contain 3D values inside:

- `recipeParamFront`

The runtime migrates those legacy values into `recipeParamTop3D` in memory when the new section is still empty, so old recipes remain readable.

### Front section remains front-only

`recipeParamFront` is now considered front-traceability only:

- `RequireTraceability`
- `ExpectedCodePrefix`

3D settings must no longer be edited conceptually as part of the front section.

## VisionPro output contract

The `Top3D` validator reads the configured outputs from `Config.xml`:

- `Configuration.ThreeDHeightOutput`
- `Configuration.ThreeDWidthOutput`
- `Configuration.ThreeDLengthOutput`

Default output names:

- `ThreeDHeight`
- `ThreeDWidth`
- `ThreeDLength`

## Machine-level measurement correction

Calibration correction belongs to the machine, not to the recipe. `Config.xml -> Configuration`
provides three optional offsets in millimetres:

- `Top3DHeightMeasurementOffsetMm`
- `Top3DWidthMeasurementOffsetMm`
- `Top3DLengthMeasurementOffsetMm`

The runtime applies the same rule to all three dimensions:

`effective measurement = VisionPro raw output + machine offset`

The effective measurement is the single authoritative value used by:

- recipe nominal/tolerance validation
- GOOD / NO GOOD status
- defect messages and counters
- the Top3D / Top2D operator cards
- `tblgenerale` measurement columns
- Data Analysis trends

No database migration is required. Existing `Config.xml` files remain compatible because missing
offset fields deserialize as `0`. Runtime normalization protects the inspection path from
`NaN`, infinity and values outside the supported `-1000..+1000 mm` range; the Preferences editor
rejects invalid values before saving.

### Commissioning procedure

1. Stop production and measure a certified reference with each required 3D dimension.
2. Acquire enough VisionPro samples to obtain a stable raw average.
3. Calculate `offset = reference - raw average`.
4. Log in as Installer or Administrator.
5. Open `System Preferences -> Config.xml -> VisionPro outputs`.
6. Enter the three values under `Top3D measurement correction`.
7. Save configuration and repeat the reference test.
8. Verify `TOP3D_MEASUREMENT_OFFSETS_SAVED` in Events Monitor.

When an offset is active, the operator card labels the result as corrected and displays the applied
offset. Out-of-tolerance diagnostics include raw measurement, applied offset and corrected value.

For artifact capture, the panel now also supports these optional outputs:

- `Configuration.Top3DRerenderResultOutput`
- `Configuration.Top3DPointCloudOutput`

Default output names:

- `Top3DRerenderResult`
- `Top3DPointCloud`

The recommended VisionPro contract for `Top3DRerenderResult` is:

- `Cog3DVisionDataRerenderResult`

or, when the Top3D pipeline uses stitch aggregation:

- `Cog3DVisionDataStitchResult`

The recommended contract for `Top3DPointCloud` is:

- `Cog3DVect3Collection`

or another enumerable collection exposing `X`, `Y` and `Z`.

The validator compares measured values against `recipeParamTop3D` and writes:

- measured value
- nominal value
- min / max range
- NC flag

into the production record snapshot before database persistence.

## Runtime result lifecycle

The `Top3D` flow now follows the same end-to-end persistence model already used by the other inspection families.

For every processed product:

1. the `Top3D` validator reads the configured VisionPro outputs
2. the measured values and NC flags are written into `MainWindow._produzioneRecord`
3. the inspection snapshot is normalized and inserted into `tblgenerale`
4. the active recipe archive in `tblproduzione` stores the dedicated `RecipeParamerterTop3D` JSON section
5. the defect counters are updated through `CounterManager`
6. the alarm/ejection pipeline can monitor `ThreeDHeight`, `ThreeDWidth` and `ThreeDLength`
7. if image saving is requested, the standard top image-saving path is reused because `Top3D` renders on the top display surface
8. when `Top3DRerenderResult` and `Top3DPointCloud` are available, the same piece folder also stores:
   - `CH1_<piece>_T3D_2D_A.bmp`
   - `CH1_<piece>_T3D_3D_Range_A.bmp`
   - `CH1_<piece>_T3D_PointCloud.csv`

This means `Top3D` is not handled as a special standalone exception: it is now part of the same runtime/result contract used by the other inspections.

## Database contract

### tblgenerale

3D result columns stored together with the production record:

- `ThreeDHeightNominalValue`
- `ThreeDHeightMeasureValue`
- `ThreeDHeight_Minimum`
- `ThreeDHeight_Maximum`
- `NC_ThreeDHeight`
- `ThreeDWidthNominalValue`
- `ThreeDWidthMeasureValue`
- `ThreeDWidth_Minimum`
- `ThreeDWidth_Maximum`
- `NC_ThreeDWidth`
- `ThreeDLengthNominalValue`
- `ThreeDLengthMeasureValue`
- `ThreeDLength_Minimum`
- `ThreeDLength_Maximum`
- `NC_ThreeDLength`

### tblproduzione

The recipe archive now stores a dedicated JSON snapshot:

- `RecipeParamerterTop3D`

The historical columns remain:

- `RecipeParamerterTop`
- `RecipeParamerterSide`
- `RecipeParamerterFront`

### Global counters

The database initialization ensures these counters exist:

- `THREED_HEIGHT`
- `THREED_WIDTH`
- `THREED_LENGTH`

### Inspection configuration defaults

For `MachineType = 3DCheck`, the DB initialization ensures:

- `ThreeDHeight` enabled
- `ThreeDWidth` enabled
- `ThreeDLength` enabled
- `FrontTraceability` disabled

## Operator-facing behavior

- the recipe page shows a dedicated `Top3D` profilometer section
- the front traceability section is hidden on `3DCheck` machines
- the counters page shows only the enabled 3D counters when the machine is configured as `3DCheck`
- the top display continues to show the active `Top3D` last run image without introducing a second front-style camera panel
- when the save-image percentage logic requests a piece save, the Top3D machine can now archive 2D/3D/point-cloud evidence in the same piece folder used by the standard system

## Commissioning checklist

1. Set `Configuration.MachineType = 3DCheck` in `Config.xml`.
2. Ensure the active `.vpp` exposes the profilometer job with a semantic `Top3D`-style name.
3. Verify `CameraConfig.xml` is compatible, but remember the QuickBuild semantic name now has priority.
4. Check `Config.xml` 3D output names:
   - `ThreeDHeightOutput`
   - `ThreeDWidthOutput`
   - `ThreeDLengthOutput`
5. Check the Top3D artifact-capture output names:
   - `Top3DRerenderResultOutput`
   - `Top3DPointCloudOutput`
6. Check Top3D save flags:
   - `SaveTop3DRendered2DImage`
   - `SaveTop3DRangeImage`
   - `SaveTop3DPointCloudCsv`
7. Open `Recipe Management` and fill the `Top3D` nominal/tolerance values.
8. Save the recipe and verify `RECIPE_RUNTIME_REFRESH_OK` in the event log.
9. Run a product and verify:
   - `tblgenerale` receives 3D measured values
   - counters increment on 3D defects
   - `tblproduzione.RecipeParamerterTop3D` is populated for the recipe archive
   - the piece folder contains the expected Top3D sidecar files when the outputs are present

## VisionPro implementation suggestion for L38-300

For the `L38-300`, keep the `Top3D` job simple and deterministic:

1. Acquire and build the native 3D dataset from the sensor in the dedicated `Top3D` job.
2. Perform all geometric measurements (`height`, `width`, `length`) from the same aligned dataset used for the visual rerender.
3. In the `Results` toolblock, publish:
   - scalar outputs:
     - `ThreeDHeight`
     - `ThreeDWidth`
     - `ThreeDLength`
   - one render output:
     - `Top3DRerenderResult`
   - one point export output:
     - `Top3DPointCloud`
4. Reuse the same fixture / pose reference for both measurement and rerender, so the saved 2D and 3D evidence stays coherent with the measured values.
5. Avoid relying on generic unnamed outputs from intermediate tools; expose the final semantic outputs explicitly from `Results`, because the panel is intentionally driven by stable contracts instead of tool-internal names.

For the detailed L38-300 VisionPro ↔ panel integration notes, read:

- `Documentation/MachineHardware/top3d-l38-300-visionpro-panel-integration-2026-04-21.md`

## Notes for future extensions

If another machine variant introduces a second 3D sensor, do not overload `recipeParamFront` or `recipeParamTop3D`.

Create a dedicated semantic role and a dedicated recipe section matching the physical inspection purpose, then adapt:

- `CameraConfigurationHelper`
- `InspectionProcessor`
- `ToolBlockValidator`
- DB recipe archive columns
- operator documentation
