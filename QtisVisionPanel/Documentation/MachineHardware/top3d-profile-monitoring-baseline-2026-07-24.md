# Top3D Profile Monitoring Baseline

## Scope

This note defines the first production baseline for the Essity Top3D height-profile metrics.
It adds process monitoring without enabling physical reject for the new profile result.

The existing dimensional checks remain authoritative:

- height;
- width;
- length.

The added profile metrics distinguish surface irregularity and acquisition quality from the
dimensional height decision.

## Ownership

### Machine configuration

`Config.xml -> Configuration` owns the VisionPro output names:

| Property | Default VisionPro output |
|---|---|
| `ThreeDHeightMedianOutput` | `HeightMedian` |
| `ThreeDHeightHighTailOutput` | `HeightHighTail` |
| `ThreeDHeightValidPixelRatioOutput` | `HeightValidPixelRatio` |
| `ThreeDHeightBulgeOutput` | `HeightBulge` |
| `ThreeDHeightMaximumOutput` | `ThreeDHeight_MAX` |

The legacy `ThreeDHeightOutput = ThreeDHeight` remains supported.

### Recipe

`RecipeParamTop3D` owns product-specific thresholds:

| Property | Default | Meaning |
|---|---:|---|
| `ThreeDProfileEnabled` | `false` | Enables evaluation of the profile thresholds |
| `ThreeDHeightMinimumValidPixelRatio` | `0.75` | Minimum valid acquisition ratio, stored as `0..1` |
| `ThreeDHeightMaximumBulge` | `0` | Maximum `HighTail - Median` in mm |

An absent field in an older XML recipe receives these safe defaults. A zero or negative maximum
bulge means the threshold is not configured and the profile remains monitor-only.

### Runtime

`ProcessMonitoringResult` represents a non-rejecting process check. It keeps state, summary,
diagnostic messages and a general `RejectEnabled` flag. The Top3D profile sets
`RejectEnabled=false`; this is not an Essity-specific exception in the reject code.

## VisionPro contract

The Top3D results ToolBlock can expose:

| Output | Type | Use |
|---|---|---|
| `HeightMedian` | `Double` | Preferred dimensional height and profile baseline |
| `HeightHighTail` | `Double` | High-tail profile measurement |
| `HeightValidPixelRatio` | `Double`, range `0..1` | Acquisition-quality indicator |
| `HeightBulge` | `Double`, mm | `HeightHighTail - HeightMedian` |
| `ThreeDHeight_MAX` | `Double` | Diagnostic maximum only |
| `ThreeDHeight` | `Double` | Legacy height fallback |
| `ThreeDWidth` | `Double` | Existing dimensional width |
| `ThreeDLength` | `Double` | Existing dimensional length |

VisionPro must perform floating-point division when calculating the valid-pixel ratio.

## Runtime data flow

1. `IToolBlockValidator` reads every optional output through `TryGetOutputValue`.
2. Values are accepted only when finite.
3. Official dimensional height resolves in this order:
   - finite `HeightMedian`;
   - finite legacy `ThreeDHeight`;
   - otherwise `INVALID`.
4. `Top3DHeightMeasurementOffsetMm` is applied only to the resolved dimensional height.
5. High Tail, diagnostic maximum, bulge and valid-pixel ratio are archived raw.
6. Profile state is evaluated only when:
   - `ThreeDProfileEnabled=true`;
   - `ThreeDHeightMaximumBulge > 0`.
7. The structured monitoring result enters the defect/history flow but is excluded from the
   reject-enabled map.
8. `ProduzioneRecord` is persisted through the existing dynamic MySQL insert.
9. Overview and Data Inspector render the same archived state and values.

## State contract

| State | Condition | `NCThreeDProfile` | Physical reject |
|---|---|---:|---|
| `GOOD` | Ratio valid and bulge within threshold | `1` | Off |
| `NO GOOD` | Ratio valid and bulge above threshold | `0` | Off |
| `INVALID` | Missing/invalid output or insufficient valid pixels | `0` | Off |
| `MONITOR` | Values available but check/threshold disabled | `4` | Off |
| `WAIT` | No profile value available while disabled/not applicable | `4` | Off |

`HasDefects` can be true for a profile issue so counters, image evidence and classification
history remain honest. `RejectRequired` remains false when the profile is the only issue.

## Visual classification contract

The Top3D operator views use dedicated inspection icons instead of the generic compliant-product
asset:

| Resource | Inspection | Runtime card |
|---|---|---|
| `resources/inspection_3d_height.png` | Z-axis height | Top3D primary card |
| `resources/inspection_3d_profile.png` | 3D surface profile | Top3D profile card |
| `resources/inspection_3d_width.png` | X-axis width | Top2D first dimensional card |
| `resources/inspection_3d_length.png` | Y-axis length | Top2D second dimensional card |

The same height, width and length assets are exposed by `InspectionConfigViewModel` and
`InspectionCounterViewModel`, keeping recipe configuration, runtime cards and defect counters
visually consistent. The icon classifies the inspection only. Existing `WAIT`, `GOOD`,
`NO GOOD`, `INVALID` and `MONITOR` state badges remain authoritative.

This is a presentation-only contract. It does not alter VisionPro output reading, dimensional
validation, counters, physical reject, recipe/configuration schemas or database persistence.

## MySQL schema

Incremental nullable columns in `tblgenerale`:

```sql
ThreeDHeightMedianValue       FLOAT(10,3) NULL DEFAULT NULL
ThreeDHeightHighTailValue     FLOAT(10,3) NULL DEFAULT NULL
ThreeDHeightMaximumValue      FLOAT(10,3) NULL DEFAULT NULL
ThreeDHeightBulgeValue        FLOAT(10,3) NULL DEFAULT NULL
ThreeDHeightValidPixelRatio   FLOAT(10,5) NULL DEFAULT NULL
NCThreeDProfile               INT         NULL DEFAULT NULL
```

Startup uses the existing `EnsureColumnAsync` migration pattern. Runtime inserts first inspect
column availability, so a database account without migration rights continues to store all older
fields. Data Inspector projects missing optional columns as `NULL`.

## Compatibility

### Old recipe

- XML deserialization keeps safe property defaults.
- Profile is disabled.
- Existing height/width/length checks are unchanged.

### Old VPP

- Missing new outputs are tolerated while the profile is disabled.
- Height falls back to `ThreeDHeight`.
- Enabling the profile with missing outputs produces `INVALID`, not a runtime exception.

### Old database

- Incremental migration is attempted.
- Dynamic insert excludes unavailable columns.
- Data Inspector checks the live schema before selecting optional fields.

## Operator surfaces

### Recipe Management

The Top3D section exposes:

- enable profile monitoring;
- minimum valid pixels as percentage;
- maximum bulge index in mm;
- an explicit explanation that the index covers the full ROI and does not locate the defect.

Point and comma decimal separators are accepted. Save rejects ratios outside `0..100%`,
non-finite values and negative bulge thresholds.

### Overview

A separate profile card shows:

- height median;
- High Tail;
- diagnostic maximum;
- bulge index and limit;
- valid-pixel percentage and minimum;
- `GOOD / NO GOOD / INVALID / MONITOR / WAIT`.

The existing height, width and length cards are not replaced.

### Data Inspector

Profile incidents use a `Reported`/`Segnalato` badge when classification is NOK but physical
expulsion was not commanded. Details include all profile values, thresholds and state from
`InspectionStatusDetails`.

## Dashboard decision

The existing `ThreeDHeightTrend -> ThreeDHeightMeasureValue` remains unchanged. A dedicated
`ThreeDProfileTrend -> ThreeDHeightBulgeValue` card is deferred: the current chart architecture
does not carry recipe-specific threshold history cleanly, and adding a misleading present-time
limit to historical points would be unsafe. Persistence and Data Inspector provide the first
baseline analysis path.

## Logs

- `TOP3D_HEIGHT_LEGACY_FALLBACK`: median unavailable; legacy height used.
- `TOP3D_PROFILE_MONITOR`: profile `NO GOOD` or `INVALID`; includes values and confirms
  `rejectEnabled=false`.

## Commissioning matrix

| Test | Inputs | Expected |
|---|---|---|
| Profile disabled, old VPP | Legacy height only | Existing dimensions work; profile `WAIT`; no defect |
| Monitor mode | New outputs, threshold `0` | Values archived; state `MONITOR`; no defect/reject |
| Good profile | ratio `0.8277`, bulge `8.571`, max `10` | `GOOD`, `NC=1`, no reject |
| Bulge high | ratio `0.8277`, bulge `12`, max `10` | `NO GOOD`, `NC=0`, evidence stored, no reject |
| Acquisition low | ratio `0.50`, min `0.75` | `INVALID`, `NC=0`, no reject |
| Missing output enabled | ratio or bulge absent | `INVALID` with configured output name and available list |
| Numeric invalid | NaN/Infinity/out-of-range ratio/negative bulge | `INVALID`, no exception/reject |
| Height fallback | median missing, legacy finite | Corrected legacy height used |
| Height offset | median finite, height offset non-zero | Offset affects official height only, never bulge |

Machine acceptance still requires the real VPP, Top3D sensor and MySQL instance. Build-only
verification cannot prove the VisionPro output values or physical no-reject wiring.

## Rollback

Disable `ThreeDProfileEnabled` in the recipe to return immediately to legacy dimensional behavior.
The new nullable DB columns and XML fields can remain present; older software ignores them.
