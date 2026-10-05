# tblgenerale: Production ID And VisionPro Classification

## Scope

This note describes the archive contract introduced in software `3.1.0.9` for:

- the active recipe `IdProduzione` stored on every new inspection row;
- the optional VisionPro classification label and score stored per camera view.

The change is additive. It does not change validation, reject, counters, camera
triggering, recipe XML or machine configuration.

## Production identity

Before VisionPro enters RunContinuous, `checkIdProd()` resolves the active recipe
in `tblproduzione`. The resolved ID remains part of the active inspection record
and is copied into every per-piece snapshot written to `tblgenerale`.

Recipe names are normalized to one file name with one `.vpp` extension. Both
`RecipeName` and `RecipeName.vpp` therefore resolve the same database row.

Expected startup log:

```text
PRODUCTION_ID_RESOLVED|recipe=<recipe>.vpp|id=<positive id>
```

If the database or recipe row cannot be resolved, the HMI remains operational
and emits `PRODUCTION_ID_UNRESOLVED`. In that degraded condition new archive rows
may still use ID `0`; they are never associated silently with the previous
recipe. Historical rows already saved with ID `0` are not backfilled because
their original recipe identity may be ambiguous.

## Classification columns

At database initialization the HMI creates the following nullable columns when
they are missing:

| Runtime view | Label column | Score column | NC outcome column |
|---|---|---|---|
| Top / Top3D | `TopClassificationLabel` | `TopClassificationScore` | `NC_TopAiClassification` |
| Side / Left | `SideClassificationLabel` | `SideClassificationScore` | `NC_SideAiClassification` |
| Front | `FrontClassificationLabel` | `FrontClassificationScore` | `NC_FrontAiClassification` |
| Rear / Right | `RearClassificationLabel` | `RearClassificationScore` | `NC_RearAiClassification` |
| Bottom | `BottomClassificationLabel` | `BottomClassificationScore` | `NC_BottomAiClassification` |

Labels are stored as `VARCHAR(255)` and scores as nullable `DOUBLE`. The score is
stored exactly in the scale exported by the VisionPro job; the database layer
does not convert `0..1` into percentages.

The values come from the immutable ToolBlock output snapshot correlated with the
same inspection cycle. If a view has no Classify output, label and score remain
`NULL`.

From release `3.1.1.1`, every new row also stores one numeric outcome per view:

- `0`: AI classification NOK or enabled classification error;
- `1`: AI classification GOOD;
- `4`: AI inspection disabled or not executed on that view.

Rows written before the migration keep `NULL` in these five outcome columns.

## VisionPro output contract

Recommended root ToolBlock outputs:

- class: `EL_Classify`;
- score: `EL_Score`.

Supported class aliases are `Classify`, `Classification` and `Class`. Supported
score aliases are `Score`, `ClassificationScore` and `Confidence`.

## Commissioning checks

1. Start the HMI with MySQL available and load the production recipe.
2. Confirm one `PRODUCTION_ID_RESOLVED` log with an ID greater than zero.
3. Inspect one product with known Classify outputs on each available camera.
4. Run the query below and compare the row with the HMI cards and VisionPro.
5. Load a job without Classify on one view and confirm that only that view stores
   `NULL` label and score.

```sql
SELECT
    Id,
    IdProduzione,
    DataeOra,
    Ricetta,
    TopClassificationLabel,
    TopClassificationScore,
    NC_TopAiClassification,
    SideClassificationLabel,
    SideClassificationScore,
    NC_SideAiClassification,
    FrontClassificationLabel,
    FrontClassificationScore,
    NC_FrontAiClassification,
    RearClassificationLabel,
    RearClassificationScore,
    NC_RearAiClassification,
    BottomClassificationLabel,
    BottomClassificationScore,
    NC_BottomAiClassification
FROM tblgenerale
ORDER BY Id DESC
LIMIT 20;
```

Expected commissioning matrix:

| Recipe/job condition | Outcome |
|---|---|
| `AIClassification` disabled | `4` on every view |
| AI enabled, view absent or no Classify output | `4` on that view |
| AI enabled, passing class | `1` on that view |
| AI enabled, failing/free defect class | `0` on that view |
| AI enabled, class output present but empty/unreadable | `0` on that view |

Cross-check the recipe relation with:

```sql
SELECT g.Id, g.DataeOra, g.IdProduzione, g.Ricetta, p.Ricetta AS ProductionRecipe
FROM tblgenerale g
LEFT JOIN tblproduzione p ON p.IdProduzione = g.IdProduzione
ORDER BY g.Id DESC
LIMIT 20;
```

## Performance note

`InsertDataConforme` reads the `tblgenerale` column set with one
`information_schema.COLUMNS` query per insert. This replaces the previous series
of individual existence queries and keeps optional-schema compatibility without
adding one query for every classification column.
