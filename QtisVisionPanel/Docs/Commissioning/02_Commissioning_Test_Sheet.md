# Commissioning Test Sheet

Questo file serve per annotare in modo ordinato i test reali eseguiti sulla macchina.

Uso consigliato:

1. duplicare il file per ogni sessione di test oppure compilarlo direttamente
2. non cancellare gli esiti precedenti
3. segnare sempre data, operatore, ricetta e configurazione usata

---

## Session Information

| Field | Value |
|---|---|
| Date |  |
| Technician |  |
| Machine |  |
| Software version |  |
| Active recipe |  |
| I/O cable | `PCL-10250-2E` |
| I/O terminal board | `ADAM-3951-BE` |
| Number of I/O terminal boards used |  |
| Encoder cable | `PCL-10137H-3E` |
| Encoder terminal board | `ADAM-3937-BE` |
| Encoder type | `Single-ended / Differential / Unknown` |
| Encoder supply required by datasheet |  |
| Encoder supply actually used |  |
| I/O input reference wiring | `0V -> ECOM` |
| I/O output reference wiring | `+24V -> PCOM`, `0V -> IGND` |
| Encoder reference wiring | `Encoder powered externally, signals wired to PCIE-1884, GND referenced to board GND` |
| Config file checked |  |
| `io_mapping.xml` checked | Yes / No |
| Notes |  |

---

## Hardware Detection

| Check | Expected | Result | Notes |
|---|---|---|---|
| `PCIE-1756` detected | Yes |  |  |
| `PCIE-1884` detected | Yes / Optional |  |  |
| Software starts without crash | Yes |  |  |
| Machine status readable | Yes |  |  |
| Events Monitor readable | Yes |  |  |
| Heartbeat stable at startup | Yes |  |  |

---

## Configuration References

Annotare i valori reali trovati nel sistema.

| Parameter | Expected / Default | Actual value | Notes |
|---|---|---|---|
| Blocking alarm output | per alarm card (`DO00`-`DO31`); `DO40` in Config.xml = legacy |  |  |
| Non-blocking alarm output | per alarm card (`DO00`-`DO31`); `DO41` in Config.xml = legacy |  |  |
| Top trigger delay | from recipe |  |  |
| Side trigger delay | from recipe |  |  |
| `Recipe_Folder` | configured |  |  |
| `io_mapping.xml` path | `Recipe_Folder\\cfg\\io_mapping.xml` |  |  |

---

## Digital Inputs - PCIE-1756

Range software previsto:

- `DI00` - `DI31`

Compilare solo i canali realmente cablati.

| Channel | Terminal | Function | Rest state | Active state | Observed | Result | Notes |
|---|---|---|---|---|---|---|---|
| `DI00` |  |  |  |  |  |  |  |
| `DI01` |  |  |  |  |  |  |  |
| `DI02` |  |  |  |  |  |  |  |
| `DI03` |  |  |  |  |  |  |  |
| `DI04` |  |  |  |  |  |  |  |
| `DI05` |  |  |  |  |  |  |  |
| `DI06` |  |  |  |  |  |  |  |
| `DI07` |  |  |  |  |  |  |  |
| `...` |  |  |  |  |  |  |  |

---

## Digital Inputs - PCIE-1884

Range software previsto:

- `DI100` - `DI103`

| Channel | Terminal | Function | Rest state | Active state | Observed | Result | Notes |
|---|---|---|---|---|---|---|---|
| `DI100` |  |  |  |  |  |  |  |
| `DI101` |  |  |  |  |  |  |  |
| `DI102` |  |  |  |  |  |  |  |
| `DI103` |  |  |  |  |  |  |  |

---

## Digital Outputs - PCIE-1756

Range software previsto:

- `DO00` - `DO31`

Annotare almeno:

- uscite realmente cablate
- canale della alarm card bloccante
- canale della alarm card non bloccante

| Channel | Terminal | Function | Safe to test | Observed ON | Observed OFF | Result | Notes |
|---|---|---|---|---|---|---|---|
| `DO00` |  |  |  |  |  |  |  |
| `DO01` |  |  |  |  |  |  |  |
| `DO02` |  |  |  |  |  |  |  |
| `DO03` |  |  |  |  |  |  |  |
| `DO..` |  | Blocking alarm (canale alarm card) |  |  |  |  |  |
| `DO..` |  | Non-blocking alarm (canale alarm card) |  |  |  |  |  |
| `...` |  |  |  |  |  |  |  |

---

## Digital Outputs - PCIE-1884

Range software previsto:

- `DO100` - `DO103`

| Channel | Terminal | Function | Safe to test | Observed ON | Observed OFF | Result | Notes |
|---|---|---|---|---|---|---|---|
| `DO100` |  |  |  |  |  |  |  |
| `DO101` |  |  |  |  |  |  |  |
| `DO102` |  |  |  |  |  |  |  |
| `DO103` |  |  |  |  |  |  |  |

---

## Encoder Channels

Range software previsto:

- `ENC1` - `ENC4`
- contatori `0` - `3`
- modalità prevista: `Quadrature`

| Encoder | Counter channel | Direction OK | Stable at rest | Counts in motion | Reset OK | Preset OK | Result | Notes |
|---|---|---|---|---|---|---|---|---|
| `ENC1` | `0` |  |  |  |  |  |  |  |
| `ENC2` | `1` |  |  |  |  |  |  |  |
| `ENC3` | `2` |  |  |  |  |  |  |  |
| `ENC4` | `3` |  |  |  |  |  |  |  |

---

Primo test consigliato per il banco encoder:

- `ENC1 / Counter0`
- single-ended:
  - `A -> CNT0_CLK+/A+`
  - `B -> CNT0_AUX+/B+`
  - `Z -> CNT0_GATE+/Z+` opzionale
  - `GND -> GND`
- differenziale:
  - `A+/A-`, `B+/B-`, `Z+/Z-` sui pin dedicati

---

## Runtime Stability

| Check | Expected | Result | Notes |
|---|---|---|---|
| Heartbeat stable for 10-15 min | Yes |  |  |
| No random input toggles | Yes |  |  |
| No encoder drift at rest | Yes |  |  |
| No device disconnects | Yes |  |  |
| Events Monitor clean or understandable | Yes |  |  |

---

## Trigger Validation

| Check | Expected | Result | Notes |
|---|---|---|---|
| Top trigger delay loaded from recipe | Yes |  |  |
| Side trigger delay loaded from recipe | Yes |  |  |
| Trigger happens at expected distance | Yes |  |  |
| No double trigger | Yes |  |  |
| No missed trigger | Yes |  |  |

---

## Reject / Ejection Validation

| Check | Expected | Result | Notes |
|---|---|---|---|
| KO piece generates reject event | Yes |  |  |
| OK piece does not reject | Yes |  |  |
| Reject output fires at correct point | Yes |  |  |
| No double ejection | Yes |  |  |
| Alarm output channel matches config | Yes |  |  |

---

## Open Anomalies

| ID | Area | Description | Severity | Temporary workaround | Next action |
|---|---|---|---|---|---|
| 1 |  |  |  |  |  |
| 2 |  |  |  |  |  |
| 3 |  |  |  |  |  |

---

## Final Outcome

| Item | Status |
|---|---|
| Electrical checks completed |  |
| Inputs validated |  |
| Outputs validated |  |
| Encoder validated |  |
| Trigger validated |  |
| Reject validated |  |
| Configuration saved |  |
| Evidence package collected |  |

Final notes:

-
