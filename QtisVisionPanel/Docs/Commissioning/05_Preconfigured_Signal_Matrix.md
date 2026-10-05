# Preconfigured Signal Matrix

Questo documento raccoglie i segnali che risultano gia' predisposti nel progetto e che possono essere usati come base pratica per il commissioning.

Non sostituisce la verifica elettrica in campo.

Serve per:

- partire dai segnali gia' previsti dal software
- evitare di reinventare il flusso macchina
- annotare rapidamente morsetti e verifica reale
- mantenere il commissioning coerente con questa famiglia di macchine

---

## 2026-04-28 - Test macchina senza encoder - mapping due trigger camera

Per il test macchina attuale la scheda encoder `PCIE-1884` non viene usata. La `PCIE-1756` resta in hardware reale per input/output, mentre la posizione nastro viene avanzata dal pannello con una velocita' virtuale impostata manualmente in `m/min`.

Cablaggio da usare:

| Funzione | Segnale software | Scheda | Canale | Morsetto ADAM | Note |
|---|---|---|---|---|---|
| Fotocellula prodotto | `IN_PRODUCT_PHOTOCELL` | `PCIE-1756-BE` | `DI00` | CON1 `IDI0` terminale `1` | `0V` su `ECOM0` terminale `17` |
| Spare / non usato | `OUT_SPARE_DO00` | `PCIE-1756-BE` | `DO00` | CON2 `IDO0` terminale `1` | lasciare libero nel flusso attuale; usare solo diagnostica esplicita |
| Trigger camera side | `OUT_CAMERA_SIDE_TRIGGER` | `PCIE-1756-BE` | `DO01` | CON2 `IDO1` terminale `2` | impulso trigger camera side e catena enable illuminatore associata |
| Trigger camera top | `OUT_CAMERA_TOP_TRIGGER` | `PCIE-1756-BE` | `DO02` | CON2 `IDO2` terminale `3` | impulso trigger camera top e catena enable illuminatore associata |

Note runtime:

- abilitare `Virtual conveyor in HW` nella pagina I/O quando non e' installato l'encoder
- impostare la velocita' reale stimata del nastro in `m/min`
- configurare i punti intervento in millimetri rispetto a `PRODUCT_ZERO`
- usare due punti intervento `TriggerCamera` separati:
  - `CAMERA_TRIGGER_TOP` su `OUT_CAMERA_TOP_TRIGGER`
  - `CAMERA_TRIGGER_SIDE` su `OUT_CAMERA_SIDE_TRIGGER`
- non usare `LIGHTS_ON` / `LIGHTS_OFF` per questo test: se restano attivi con segnale vuoto possono comandare i binding top/side come uscite statiche invece di impulsi camera
- la checkbox `Photocell -> lighting bench mode` e' solo per test banco semplice; con punti camera attivi viene ignorata dal runtime e non deve sostituire il tracking
- se l'ingresso della camera richiede un comando PNP/source `+24V`, non collegarlo direttamente all'uscita sink della `PCIE-1756`; usare rele', optoisolatore o convertitore NPN->PNP

---

## Regola di utilizzo

Usare questa matrice cosi':

1. prendere questi segnali come baseline software
2. verificare in campo se il cablaggio macchina corrisponde davvero
3. compilare morsetto, presenza reale e note
4. se la macchina differisce, annotare la deviazione invece di cambiare il significato dei canali a memoria

---

## Stato attuale trovato nel progetto locale

Riferimenti confermati nella repo locale:

- scheda digital I/O prevista:
  - `PCIE-1756`
- scheda encoder / I/O dedicati prevista:
  - `PCIE-1884`
- input standard:
  - `DI00` - `DI31`
- input dedicati scheda encoder:
  - `DI100` - `DI103`
- output standard:
  - `DO00` - `DO31`
- output dedicati scheda encoder:
  - `DO100` - `DO103`
- encoder:
  - `ENC1` - `ENC4`
  - contatori `0` - `3`
- campi legacy in `Config.xml` (non letti dal runtime, fuori range PCIE-1756):
  - `BlockingAlarmOutput = DO40`
  - `NonBlockingAlarmOutput = DO41`
- uscite allarme reali: canale assegnato a ciascuna alarm card
- trigger delay ricetta:
  - `TopCameraTriggerDelay`
  - `SideCameraTriggerDelay`

Kit fisico confermato per questa sessione:

- cavo I/O:
  - `PCL-10250-2E`
- terminal board I/O:
  - `ADAM-3951-BE`
  - quantita' disponibile: `2`
- cavo encoder:
  - `PCL-10137H-3E`
- terminal board encoder:
  - `ADAM-3937-BE`

Nota pratica importante:

- nel runtime locale non e' stato trovato un file `io_mapping.xml` sotto `C:\QtisVision`
- quindi, fino a prova contraria, il riferimento operativo di partenza e':
  - configurazione attuale del software
  - `Config.xml`
  - template segnali del progetto

---

## Runtime values currently active

Valori letti dal runtime locale:

| Item | Current value |
|---|---|
| Recipe folder | `C:\QtisVision\Programs\` |
| Image dir | `C:\QtisVision\Pieces\` |
| Last recipe | `PanoliniFiera.vpp` |
| NumCamera | `2` |
| Blocking alarm output | `DO40` (legacy non letto dal runtime, fuori range PCIE-1756) |
| Non-blocking alarm output | `DO41` (legacy non letto dal runtime, fuori range PCIE-1756) |
| Top camera serial output | `DO41` |
| Side camera serial output | `DO41` |
| Expulsion offset | `10` |
| Expulsion width | `5` |
| Generic delay | `100` |

Questi valori vanno considerati parte del contesto di test attuale.

---

## Bench validation already confirmed

Validazione gia' riuscita in banco sulla `PCIE-1756`:

- input digitali:
  - `0V` su `ECOM`
  - `+24V` applicato su `IDIx`
  - LED ADAM acceso
  - stato visibile correttamente in Navigator
- output digitali:
  - `+24V` su `PCOM`
  - `0V` su `IGND`
  - carico `24V` collegato tra `PCOM / +24V` e `IDOx`
  - uscita pilotata correttamente in Navigator

Nota importante:

- per gli output isolati della `PCIE-1756`, il riferimento corretto e':
  - `PCOM -> +24V`
  - `IGND -> 0V`
  - `IDOx` come ritorno commutato del carico

---

## Physical Connection Topology To Validate

Percorso fisico da verificare durante il collaudo:

### I/O digitali

- PC / scheda Advantech I/O
- cavo `PCL-10250-2E`
- terminal board `ADAM-3951-BE`
- morsetti macchina / sensori / attuatori

Per il primo test pratico della `PCIE-1756` usare:

- `CON1` su ADAM dedicato agli input
- `CON2` su ADAM dedicato agli output

Riferimento operativo:

- `06_PCIE1756_2xADAM3951_Navigator_Test.md`

### Encoder

- PC / scheda Advantech encoder
- cavo `PCL-10137H-3E`
- terminal board `ADAM-3937-BE`
- encoder / segnali dedicati

Regola pratica:

- alimentare l'encoder secondo il suo datasheet
- usare `ADAM-3937-BE` come terminal board del ramo segnali
- non portare tensioni di alimentazione sui pin segnale della `PCIE-1884`

Controlli minimi per ogni linea:

- cavo corretto sul ramo corretto
- terminal board corretta
- morsetto corretto
- canale software coerente

Primo canale da validare:

- `ENC1 -> Counter0`

Pin `Counter0` gia' confermati dal manuale:

- `CNT0_CLK+/A+` -> pin `2`
- `CNT0_CLK-/A-` -> pin `20`
- `CNT0_AUX+/B+` -> pin `3`
- `CNT0_AUX-/B-` -> pin `21`
- `CNT0_GATE+/Z+` -> pin `4`
- `CNT0_GATE-/Z-` -> pin `22`
- `GND` -> pin `1`, `14`, `17`, `32`, `35`

---

## Preconfigured signal baseline for this machine family

Questa tabella deriva dal template macchina gia' presente nel progetto:

- `Documentation\MachineHardware\signal-mapping-template.csv`

e dai riferimenti runtime/codice della baseline attuale.

### Inputs

| Area | Signal code | Board | Channel | Expected role | Field confirmation | Notes |
|---|---|---|---|---|---|---|
| Feeding | `IN_PRODUCT_PHOTOCELL` | `PCIE-1756` | `DI00` | presenza prodotto in ingresso |  | apre tracking prodotto |
| Vision | `IN_EXTERNAL_TRIGGER_ENABLE` | `PCIE-1756` | `DI01` | consenso trigger esterno |  | usare se PLC governa il trigger |
| Reject | `IN_REJECT_OPEN_FB` | `PCIE-1756` | `DI02` | feedback scarto aperto |  | opzionale |
| Reject | `IN_REJECT_CLOSED_FB` | `PCIE-1756` | `DI03` | feedback scarto chiuso |  | opzionale |
| Encoder | `IN_ENCODER_Z` | `PCIE-1884` | `DI100` | zero encoder / referenziazione |  | opzionale |

### Outputs

| Area | Signal code | Board | Channel | Expected role | Field confirmation | Notes |
|---|---|---|---|---|---|---|
| Spare | `OUT_SPARE_DO00` | `PCIE-1756` | `DO00` | libero |  | non usare nel flusso camera attuale |
| Vision | `OUT_CAMERA_SIDE_TRIGGER` | `PCIE-1756` | `DO01` | trigger hardware camera side |  | impulso 40 ms default |
| Vision | `OUT_CAMERA_TOP_TRIGGER` | `PCIE-1756` | `DO02` | trigger hardware camera top |  | impulso 40 ms default |
| Reject | `OUT_REJECT_SOLENOID` | `PCIE-1756` | `DO03` | scarto monoattuatore |  | default mono |
| Reject | `OUT_REJECT_OPEN` | `PCIE-1756` | `DO04` | apertura scarto |  | usare solo se bi-stabile presente |
| Reject | `OUT_REJECT_CLOSE` | `PCIE-1756` | `DO05` | chiusura scarto |  | usare solo se bi-stabile presente |
| Alarm | `OUT_GENERAL_ALARM` | `PCIE-1756` | `DO06` | allarme generale |  | fault bloccanti |
| Alarm | `OUT_WARNING_ALARM` | `PCIE-1756` | `DO07` | warning non bloccante |  | opzionale |
| Alarm | `OUT_BLOCKING_ALARM_CONFIG` | `Config.xml` | `DO40` | uscita allarme bloccante configurata |  | legacy non letto dal runtime, fuori range PCIE-1756; usare il canale della alarm card |
| Alarm | `OUT_NONBLOCKING_ALARM_CONFIG` | `Config.xml` | `DO41` | uscita allarme non bloccante configurata |  | legacy non letto dal runtime, fuori range PCIE-1756; usare il canale della alarm card |

### Encoder

| Area | Signal code | Board | Channel | Expected role | Field confirmation | Notes |
|---|---|---|---|---|---|---|
| Encoder | `ENC_CONVEYOR_MAIN` | `PCIE-1884` | `Counter0 / ENC1` | encoder principale trasporto |  | quadrature |
| Encoder | `ENC_AUX_2` | `PCIE-1884` | `Counter1 / ENC2` | encoder ausiliario 2 se presente |  | da confermare |
| Encoder | `ENC_AUX_3` | `PCIE-1884` | `Counter2 / ENC3` | encoder ausiliario 3 se presente |  | da confermare |
| Encoder | `ENC_AUX_4` | `PCIE-1884` | `Counter3 / ENC4` | encoder ausiliario 4 se presente |  | da confermare |

---

## Comportamento sicuro del mapping legacy

`Recipe_Folder\cfg\io_mapping.xml` e' un'estensione legacy opzionale e non e'
il mapping principale mostrato in Machine I/O Setup.

Dalla release `3.1.0.8`:

- se il file non esiste compare `IO_MAPPING_NOT_CONFIGURED` una sola volta
  all'avvio del device manager;
- se il file e' illeggibile compare `IO_MAPPING_LOAD_FAILED`;
- in entrambi i casi la configurazione legacy resta vuota e nessuna uscita
  fisica viene comandata;
- solo un file valido ed esplicito puo' registrare azioni aggiuntive, confermate
  dal log `IO_MAPPING_LOADED` con il numero di azioni caricate.

Non creare `io_mapping.xml` per correggere il normale ciclo camera/scarto: usare
la tabella Machine outputs, i runtime binding e i punti intervento. In
particolare, il passaggio su `DI00` non deve attivare `DO00` o `DO01` se quei
canali non sono referenziati esplicitamente.

---

## Come usarla durante il test

Ordine consigliato:

1. aprire `04_Bench_Quick_Checklist.md`
2. usare questa matrice per sapere quali canali provare prima
3. compilare `02_Commissioning_Test_Sheet.md`
4. se emerge un mapping reale diverso, annotarlo qui o nel report di commissioning

---

## Campi da completare in macchina

Per ogni segnale effettivamente usato sulla macchina annotare:

- morsetto fisico
- presenza reale si/no
- stato a riposo
- stato attivo
- polarita' confermata
- note di cablaggio
- eventuale differenza rispetto alla baseline software

---

## Regola finale

Se il comportamento reale macchina e' diverso da questa baseline:

- non correggere mentalmente
- non affidarti solo alla memoria
- annota la differenza
- aggiorna poi il documento o il mapping ufficiale solo dopo conferma in campo
