# Right / Bottom MultiShot Expansion 2026-06-09

## Scope

Questo documento apre la fase `3.0.0.0` per estendere la piattaforma multi-camera senza rompere la baseline `2.0.2.5`.

> Stato asset al 3 agosto 2026: questo documento descrive la capacita'
> software e il contratto previsto, non certifica che il VPP attivo sia gia' a
> quattro camere. L'inventario read-only della macchina e dei VPP reali e' in
> `four-camera-read-only-inventory-2026-08-03.md`.

Backup baseline precedente:

```text
C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\SoftwareArchives\QtisVisionPanel_2.0.2.5_pre_right_bottom_multishot_20260609_134704.zip
```

## Obiettivo

La stessa applicazione deve poter lavorare su due famiglie macchina:

| Famiglia | Numero camere | Uso previsto |
|---|---:|---|
| Vision | 1..3 | Macchine visione compatte con configurazione camera ridotta. |
| Quatis | 3..5 | Macchine complete con Top/Left/Right/Bottom e camere complementari. |

La selezione camere deve restare guidata da configurazione macchina e da cio' che il job VisionPro espone, non da codice hardcoded per un solo impianto.

## Task 1 - Camera Right

Requisiti iniziali:

- introdurre ruolo runtime `Right`
- usare la vista esistente `RearCameraView` come vista complementare, senza creare una vista nuova se non necessario
- supportare MultiShot come per Left, con configurazione macchina dedicata
- ispezioni attese: controllo saldatura laterale con output booleano VisionPro
- VisionPro deve acquisire e fare eventuale stitching; la HMI non deve costruire immagini composite

## Task 2 - Camera Bottom

Requisiti iniziali:

- usare la vista `BottomCameraView` gia esistente
- supportare sia scatto singolo sia MultiShot in base alla tipologia prodotto/configurazione macchina
- non duplicare parametri macchina in ricetta
- mantenere compatibilita con ricette esistenti che non usano Bottom

## Confini macchina / ricetta / runtime

Deve restare macchina:

- numero massimo camere installate
- mapping ruolo camera -> vista/display HMI
- mapping I/O trigger
- configurazione MultiShot per ruolo camera
- quote intervento fisiche e tarature encoder

Deve restare ricetta:

- abilitazione controlli prodotto
- tolleranze prodotto
- scelta ispezioni richieste dal prodotto
- eventuali parametri VisionPro specifici del prodotto

Deve restare runtime:

- camere realmente caricate dal job corrente
- stato running/hold
- risultati ispezione correnti
- sessioni MultiShot attive

## Principio di implementazione

Estendere l'attuale modello Side/Left verso una configurazione per ruolo camera, evitando duplicazioni rigide come `LeftOnly`, `RightOnly`, `BottomOnly`.

La direzione preferita e':

```text
MachineMultiShotTrigger
  Left/SideLeft
  Right/RearRight
  Bottom
```

Ogni ruolo deve poter definire:

- abilitazione MultiShot
- output trigger
- numero scatti
- durata impulso
- tempo minimo tra scatti
- offset primo scatto da intervention point
- step da encoder/stitching
- parametri VisionPro ImageStitching se richiesti

## Da verificare prima del codice

- nomi job VisionPro attesi per Right e Bottom
- output booleani VisionPro per saldatura laterale Right
- se Bottom usa un ToolBlock `ImageStitching` separato o condiviso
- mapping fisico I/O dei trigger Right e Bottom
- se i profili `Vision` e `Quatis` devono essere un campo esplicito nel file macchina oppure derivati dalle camere presenti

## Implementazione 3.0.0.1 - Fondazione multi-profilo

La prima implementazione operativa introduce tre profili MultiShot macchina dentro `MachineMultiShotTrigger`:

| Profilo HMI | Ruolo runtime compatibile | Vista usata | Uscita default | Punto intervento usato per primo scatto |
|---|---|---|---|---|
| `Left / Side` | `left`, `side`, `sideleft` | `SideCameraView` | `OUT_CAMERA_SIDE_TRIGGER` | `CAMERA_TRIGGER_LEFT`, fallback `CAMERA_TRIGGER_SIDE` |
| `Right / Rear` | `right`, `rear`, `sideright` | `RearCameraView` | `OUT_CAMERA_REAR_TRIGGER` | `CAMERA_TRIGGER_RIGHT`, fallback `CAMERA_TRIGGER_REAR` |
| `Bottom` | `bottom` | `BottomCameraView` | `OUT_CAMERA_BOTTOM_TRIGGER` | `CAMERA_TRIGGER_BOTTOM` |

### Cosa cambia

- `MachineMultiShotTrigger` mantiene `Side` per compatibilita e aggiunge `Right` e `Bottom`.
- Il controller MultiShot puo armare piu sessioni sullo stesso fronte fotocellula, una per ogni profilo abilitato.
- Ogni profilo ha uscita trigger, numero scatti, durata impulso, intervallo minimo, timeout, direzione encoder e parametri VisionPro indipendenti.
- La pagina `I/O Diagnostics -> Machine Configuration` mostra un selettore profilo, poi riusa gli stessi campi per modificare il profilo scelto.
- VisionPro riceve i parametri `expectedFrames`, `stepMm`, `mmPerPixel` per ogni job runtime compatibile con un profilo MultiShot abilitato.
- I risultati intermedi di `ImageStitching` vengono ignorati per ogni profilo abilitato finche `isReady=true`.
- Le viste `RearCameraView` e `BottomCameraView` vengono aggiornate quando i job `Rear/Right` e `Bottom` producono risultati.

### Cosa non cambia ancora

- La logica di scarto principale resta sulla baseline corrente: Top + secondaria attiva (`Side/Left` o `Front`) oppure Top3D standalone.
- I risultati `Rear/Right` e `Bottom` vengono visualizzati e possono essere triggerati/stitchati, ma non sono ancora inseriti nel calcolo finale NOK multi-camera.
- Non viene fatto stitching in HMI.
- Non vengono aggiunti campi ricetta.

Questa separazione serve a collaudare prima I/O, acquisizione e stitching delle camere aggiuntive, poi passare alla fase successiva: sincronizzazione risultati multi-camera 3..5 e regole di reject complete.

## Implementazione 3.0.0.2 - Esito finale multi-camera

La seconda implementazione collega le camere aggiuntive alla decisione finale del pezzo.

### Nomi job VisionPro canonici

Per questa linea macchina i nomi job VisionPro attesi sono:

| Job VisionPro | Ruolo runtime HMI | Vista HMI | Note |
|---|---|---|---|
| `Top` | `top` | `TopCameraView` | Camera superiore principale. |
| `Left` | `left` | `SideCameraView` | Left viene trattata come camera laterale sinistra e validata dentro il ramo `SideResult`. |
| `Right` | `rear` | `RearCameraView` | Right viene normalizzata internamente come `rear` per riusare la vista complementare esistente. |
| `Bottom` | `bottom` | `BottomCameraView` | Camera inferiore opzionale, single-shot o MultiShot. |
| `Front` | `front` | `FrontCameraView` | Camera tracciabilita opzionale. |

Gli alias `Side` e `Rear` restano supportati per compatibilita con configurazioni precedenti, ma i job QuickBuild nuovi devono preferire `Left` e `Right`.

### Cosa cambia

- Il flusso risultati non e' piu limitato a `Top + una camera secondaria`.
- `Top` resta l'ancora del pezzo, ma il runtime attende tutte le camere companion caricate:
  - `Side` o `Left`
  - `Front`
  - `Rear` usata come `Right`
  - `Bottom`
- `InspectionResult` ora contiene:
  - `TopResult`
  - `SideResult`
  - `FrontResult`
  - `RearResult`
  - `BottomResult`
  - `MissingCameraResults`
- `Right/Rear` viene validata come camera laterale destra.
- `Bottom` viene validata come camera dedicata ai controlli inferiori.
- Se una camera prevista non produce risultato entro timeout, viene generato un difetto tecnico `CameraMissing` che forza NOK/scarto.
- Il timeout usa `VisionMultiShotResultTimeout` quando almeno un profilo MultiShot e' abilitato.

### Validator VisionPro

| Ruolo | Validator | Output accettati |
|---|---|---|
| `Left/Side` | `GetDetailedSideValidationAsync` con ramo Left quando trova output Left | `LeftSideSealingOk`, `LeftRollCountOk`, alias sealing/roll count |
| `Right/Rear` | `GetDetailedRearValidationAsync` | `RightSideSealingOk`, `RearSideSealingOk`, `RightSealingOk`, `RearSealingOk`, `SideSealingOk`, `SealingOk`, `SealingArea` |
| `Bottom` | `GetDetailedBottomValidationAsync` | `BottomSealingOk`, `NoTrappedPaper`, alias pass/fail compatibili |

## Implementazione 3.0.0.3 - Controlli Bottom reali

La terza implementazione corregge il perimetro della camera Bottom.

### Cosa cambia

- Bottom non eredita piu controlli della Top.
- Le feature dedicate sono:
  - `BottomSealing`: saldatura inferiore / bottom sealing
  - `TrappedPaper`: carta intrappolata nella saldatura inferiore
- I due difetti sono separati in:
  - abilitazione ricetta
  - contatori operatore
  - allarmi scarto
  - popup allarme
  - Data Analysis
  - colonne DB storico
- `BottomCameraView` mostra due indicatori dedicati.

### Output VisionPro Bottom

Output consigliati:

| Controllo | Output consigliato | Semantica |
|---|---|---|
| Saldatura inferiore | `BottomSealingOk` | `true` = controllo OK |
| Carta intrappolata | `NoTrappedPaper` | `true` = nessuna carta intrappolata |

Alias accettati:

| Controllo | Output pass accettati | Output difetto accettati |
|---|---|---|
| `BottomSealing` | `BottomSealingOk`, `BottomSealOk`, `LowerSealingOk`, `SaldaturaInferioreOk`, `BottomSealing` | `BottomSealingDefect`, `BottomSealDefect`, `BottomSealingNg`, `BottomSealingNok`, `SaldaturaInferioreKo`, `SaldaturaInferioreNok` |
| `TrappedPaper` | `NoTrappedPaper`, `TrappedPaperOk`, `PaperClear`, `CartaIntrappolataOk`, `CartaInSaldaturaOk` | `TrappedPaperDetected`, `TrappedPaper`, `PaperTrapped`, `CartaIntrappolata`, `CartaInSaldatura` |

Se una feature Bottom e abilitata in ricetta ma l'output VisionPro non esiste, il pezzo viene marcato NOK. Questa scelta evita falsi OK durante commissioning o dopo una modifica job incompleta.

## Implementazione 3.0.0.9 - Stabilita pairing MultiShot e diagnostica sicura

La release `3.0.0.9` consolida i fix Claude del 16-17 giugno sul flusso multi-camera.

### Cosa cambia

- `InspectionOrchestrator` usa `CameraResult.EnqueuedAtUtc` per distinguere risultati companion validi da risultati arrivati troppo tardi e appartenenti al prodotto successivo.
- In caso di mismatch viene loggato `COMPANION_PRODUCT_MISMATCH` e il risultato companion resta in coda per il Top corretto.
- `COMPANION_ARRIVED`, `COMPANION_TIMEOUT` e `CAMERA_TIMING` diventano i log principali per capire i tempi di arrivo tra Top e camere companion.
- Il provider `StitchingStatusProvider` verso VisionPro non viene collegato dal ViewModel: la lettura diretta di `ImageStitching.frameIndex/isReady` da thread background non e sicura con gli oggetti COM VisionPro.
- `MULTISHOT_FRAME_ACK` resta una diagnostica disponibile a livello interfaccia/controller, ma non e prodotta dalla baseline HMI corrente.

### Impatto commissioning

- Se il risultato Left/Right/Bottom arriva dopo il prodotto successivo, non viene piu consumato dal gruppo sbagliato.
- Per analizzare un timeout usare prima `COMPANION_TIMEOUT` e `CAMERA_TIMING`, poi verificare nel job VisionPro che `ImageStitching.isReady=true` venga pubblicato solo sull'ultimo frame.
- Non considerare assente `MULTISHOT_FRAME_ACK` come errore: e' stato disabilitato apposta per evitare accessi COM non sicuri.

### Note operative

- Le camere attese sono derivate dai job caricati e dai ruoli runtime, non da un campo ricetta.
- Se `Right/Rear` o `Bottom` devono essere solo visualizzate e non usate per lo scarto, non devono essere caricate come job produttivo oppure i relativi controlli devono essere disabilitati/coerenti nella ricetta.
- Per il commissioning, testare prima ogni camera singolarmente, poi eseguire un prodotto con tutte le camere attive e verificare che nei dettagli DB compaiano tutti i motivi NOK.

## Configurazione commissioning

1. Verificare in `CameraConfig.xml` o nei nomi job QuickBuild che i job siano riconoscibili:
   - `Top`
   - `Left` oppure `Side`
   - `Right` oppure `Rear`
   - `Bottom`
   - opzionale `Front`
2. In `I/O Diagnostics -> Machine Configuration -> Intervention Points` controllare i punti:
   - `CAMERA_TRIGGER_TOP`
   - `CAMERA_TRIGGER_LEFT` o `CAMERA_TRIGGER_SIDE`
   - `CAMERA_TRIGGER_RIGHT` o `CAMERA_TRIGGER_REAR`
   - `CAMERA_TRIGGER_BOTTOM`
3. Ogni punto deve avere:
   - `Action = TriggerCamera`
   - `SignalCode` coerente con l'uscita fisica
   - `Base offset mm + Trim +/- mm` uguale alla posizione reale da `PRODUCT_ZERO`
4. In `Camera MultiShot` selezionare il profilo:
   - `Left / Side`
   - `Right / Rear`
   - `Bottom`
5. Per ogni profilo che richiede MultiShot:
   - abilitare `Enable`
   - selezionare l'uscita trigger reale
   - impostare `Shots`
   - impostare `Pulse duration`
   - impostare `Minimum time between shots`
   - impostare `Stitching step`
   - impostare `Camera calibration`
6. Salvare la configurazione macchina.
7. Ricaricare la ricetta/VPP.
8. Provare a bassa velocita e verificare:
   - log `MULTISHOT_*_PLAN_CREATED`
   - log `MULTISHOT_*_TRIGGER_PULSE`
   - VisionPro `ImageStitching.expectedFrames`, `stepMm`, `mmPerPixel`
   - conteggio frame camera uguale a `Shots`

## Step successivo consigliato

La fase successiva puo raffinare il commissioning per famiglie macchina:

- rendere esplicito in HMI il profilo macchina `Vision` / `Quatis`
- aggiungere una card allarme dedicata `CameraMissing`
- mostrare nella UI un riepilogo per pezzo con tutte le camere ricevute/mancanti
- valutare watchdog heartbeat dedicati anche per `Rear` e `Bottom`, non solo aggregati sul canale side
