# Top3D L38-300 VisionPro And Panel Integration

## Scope

This note documents the recommended implementation pattern for a `Top3D` machine
using the Cognex `L38-300` sensor, with the current `QtisVisionPanel` production
baseline.

The goal is to keep a clean separation between:

- machine/runtime configuration in `Config.xml`
- product thresholds in the recipe XML
- runtime evidence capture in the existing piece-save pipeline

while making the `Top3D` flow industrially useful for:

- operator visibility
- commissioning
- simulation / offline analysis
- defect traceability

## Baseline rule

The panel does not create a separate storage root for `Top3D`.

When image saving is requested by the existing save-percentage logic, `Top3D`
artifacts are saved inside the same current piece folder already used by the
standard system:

- `ImageDir\yyyy-MM-dd\SESSION_xx\BATCH_xx\Piece_xxxxxxxx`

This preserves the canonical runtime contract already driven by `Config.xml`.

## Recommended VisionPro structure

For the `L38-300`, use a dedicated semantic job named:

- `Top3D`

or another job name that normalizes to `top3d`, for example:

- `Top_3D`
- `Top-3D`
- names containing both `Top` and `3D`
- names containing `profilomet...`

Inside that job, keep the flow conceptually split into three blocks:

1. acquisition / dataset preparation
2. measurement extraction
3. operator-facing rerender + export outputs

### 1. Acquisition / dataset preparation

Build the 3D dataset once and keep that object as the canonical source for:

- height
- width
- length
- visual rerender
- point export

The key industrial rule is to avoid measuring from one branch and rendering from
another unrelated branch, because offline analysis then becomes much harder.

### 2. Measurement extraction

Expose the final scalar measurements from the `Results` toolblock with stable,
semantic names:

- `ThreeDHeight`
- `ThreeDWidth`
- `ThreeDLength`

These names are already part of the current panel/runtime contract and are read
by the Top3D validator.

### 3. Operator-facing rerender + export outputs

Expose these final outputs from the `Results` toolblock:

- `Top3DRerenderResult`
- `Top3DPointCloud`

Recommended output types:

- `Top3DRerenderResult`
  - `Cog3DVisionDataRerenderResult`
  - or `Cog3DVisionDataStitchResult` when the pipeline uses stitch aggregation
- `Top3DPointCloud`
  - preferably `Cog3DVect3Collection`
  - or another enumerable point collection exposing `X`, `Y`, `Z`

If the current VPP already computes the measurements but does not yet expose a
final rerender object, add one final rerender stage and publish that result from
`Results` instead of wiring the panel against intermediate images.

This is the cleanest contract for the panel because:

- one output gives the rendered 2D image and the rendered 3D/range image
- one output gives a reusable point set for CSV export

## What the panel now saves

When the active top job is `Top3D` and the outputs above are available, the piece
save pipeline stores these sidecar files in the current piece folder:

- `CH1_<piece>_T3D_2D_A.bmp`
- `CH1_<piece>_T3D_3D_Range_A.bmp`
- `CH1_<piece>_T3D_PointCloud.csv`

The standard piece images continue to be saved too, according to the current
runtime rules and percentages.

## New Config.xml keys

Under `AppConfig -> Configuration`, the baseline now supports these optional keys:

- `Top3DRerenderResultOutput`
- `Top3DPointCloudOutput`
- `SaveTop3DRendered2DImage`
- `SaveTop3DRangeImage`
- `SaveTop3DPointCloudCsv`

Default values:

- `Top3DRerenderResultOutput = Top3DRerenderResult`
- `Top3DPointCloudOutput = Top3DPointCloud`
- `SaveTop3DRendered2DImage = true`
- `SaveTop3DRangeImage = true`
- `SaveTop3DPointCloudCsv = true`

If the keys are missing, the runtime uses these defaults and stays backward
compatible with existing `Config.xml` files.

## Suggested operator/engineering semantics

### Rendered 2D image

Use the 2D render to give a familiar, easy-to-read view of the product footprint
or aligned grey projection.

This is the file the operator and commissioning technician will open first.

### Rendered 3D/range image

Use the 3D/range render as a fast visual representation of the height map.

This is useful for:

- understanding whether the scan actually contains valid topography
- correlating dimensional defects with the measured region
- spotting sensor-side issues such as missing height data or unstable exposure

### Point cloud CSV

Use the CSV export for:

- simulation
- offline debugging
- comparison across products / recipes
- sending evidence outside the HMI without shipping the whole runtime folder

The CSV header is:

- `X;Y;Z`

Values are exported with invariant numeric formatting.

## Recommended VisionPro design choices

### Keep the semantic outputs final

Do not make the panel depend on intermediate tool names such as:

- `Tool1.Output`
- `ResultA`
- generic hidden outputs

Instead, always publish the final semantic contract from `Results`.

This makes the VPP maintainable and makes the HMI resilient when the internal tool
graph changes.

### Reuse the same alignment reference

If the Top3D flow uses fixturing, use the same fixture reference for:

- numeric measurements
- rerender used for operator evidence
- point-cloud export branch

That way, when a product is investigated later, the saved evidence matches the
measured coordinates.

### Keep point-cloud export bounded

If the raw point set is too dense for practical CSV handling, create a dedicated
export branch in VisionPro that publishes the engineering subset you really need,
for example:

- ROI-limited point set
- downsampled point set
- final aligned point set only

The panel will export what the VPP exposes, so this optimization belongs on the
VisionPro side.

## Failure behavior

The panel intentionally behaves defensively:

- if `Top3DRerenderResult` is missing or not of the expected type, the standard piece
  save flow continues and the runtime writes a warning
- if `Top3DPointCloud` is missing or not enumerable as `X/Y/Z`, the panel skips the
  CSV export and writes a warning
- production is not blocked by missing Top3D sidecar outputs

This keeps the machine stable while still making commissioning gaps visible.

## Commissioning checklist

1. Verify the active QuickBuild job name normalizes to `top3d`.
2. Verify the `Results` toolblock exports:
   - `ThreeDHeight`
   - `ThreeDWidth`
   - `ThreeDLength`
   - `Top3DRerenderResult`
   - `Top3DPointCloud`
3. Confirm `Config.xml` output names match the VPP names if custom names are used.
4. Run one product with image saving enabled by the current save-percentage logic.
5. Check the generated piece folder.
6. Confirm the presence and readability of:
   - standard top image files
   - `CH1_<piece>_T3D_2D_A.bmp`
   - `CH1_<piece>_T3D_3D_Range_A.bmp`
   - `CH1_<piece>_T3D_PointCloud.csv`
7. Confirm `tblgenerale` still receives the 3D measurement values and NC flags.

## Notes for future extensions

If later the project needs richer 3D evidence handling such as:

- native serialized 3D dataset storage
- point-cloud visualization inside the HMI
- 3D artifact browsing from `DataInspector`

those should be added as progressive extensions on top of this same contract,
without moving the current baseline away from the existing piece-folder and
`Config.xml`-driven runtime structure.
