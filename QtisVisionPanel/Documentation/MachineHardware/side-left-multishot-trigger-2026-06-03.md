# Side/Left MultiShot Trigger 2026-06-03

## Scope

Questa integrazione introduce la prima versione conservativa del MultiShot hardware per la camera fisica Left, mantenendo il ruolo software esistente `Side`.

La HMI non acquisisce immagini multiple e non esegue stitching in C#.

Responsabilita':

- HMI / I/O / Encoder: generare N impulsi trigger hardware distanziati da encoder
- VisionPro: acquisire frame, gestire eventuale buffer/stitching nel VPP e produrre il risultato finale

## Ruolo runtime Left

Da release `2.0.0.1`, la stessa camera fisica Side puo' essere trattata come ruolo runtime `Left` quando il job QuickBuild espone un nome come `Left`, `SideLeft`, `Side_Left` o `Side-Left`.

Il path hardware/display resta quello Side, ma il ruolo semantico diventa `left`, con la stessa filosofia usata per `Top` / `Top3D`.

Per un job Left, il toolblock `Results` deve esporre output booleani:

- `LeftSideSealingOk`
- `LeftRollCountOk`

Alias supportati:

- saldatura laterale: `SideSealingOk`, `SealingOk`, `LeftSealingOk`
- conteggio rotoli: `RollCountOk`, `RollsCountOk`, `RollCount`

Quando viene rilevata la modalita' Left, la validazione Side standard numerica viene saltata: il pannello si aspetta solo il true/false finale prodotto da VisionPro.

## Parametri VisionPro ImageStitching

Da release `2.0.0.2`, quando il ruolo runtime e' `left` e `MachineMultiShotTrigger/Side/Enabled=true`, la HMI scrive automaticamente i parametri macchina nel ToolBlock VisionPro `ImageStitching` dopo il caricamento VPP e prima del `RunContinuous`.

Nodo macchina:

```xml
<VisionProStitching>
  <ToolBlockName>ImageStitching</ToolBlockName>
  <ExpectedFramesInputName>expectedFrames</ExpectedFramesInputName>
  <StepMmInputName>stepMm</StepMmInputName>
  <MmPerPixelInputName>mmPerPixel</MmPerPixelInputName>
  <StepMm>0</StepMm>
  <MmPerPixel>0</MmPerPixel>
</VisionProStitching>
```

Mapping applicato:

- `ShotCount` -> `ImageStitching.expectedFrames`
- `VisionProStitching.StepMm` -> `ImageStitching.stepMm`
- `VisionProStitching.MmPerPixel` -> `ImageStitching.mmPerPixel`

Se `StepMm` o `MmPerPixel` non sono maggiori di zero, viene loggato `VISIONPRO_STITCHING_CONFIG_INVALID`.

Se il ToolBlock o gli input non esistono, vengono loggati:

- `VISIONPRO_STITCHING_TOOLBLOCK_NOT_FOUND`
- `VISIONPRO_STITCHING_INPUT_MISSING`

La HMI non acquisisce frame, non stitcha immagini e non crea macro immagini. VisionPro deve gestire `PostAcquisitionRef`, buffer/stitching e pubblicare solo il risultato finale.

Da release `2.0.2.0`, la HMI applica anche una guardia runtime: se VisionPro emette comunque eventi intermedi durante il MultiShot, il risultato Left viene accettato solo quando `ImageStitching.isReady=true`.

Da release `2.0.2.1`, la ricerca del ToolBlock `ImageStitching` e' ricorsiva: il ToolBlock puo essere direttamente nel job o dentro ToolBlock/Gruppi annidati.

Output diagnostici richiesti/consigliati sul ToolBlock `ImageStitching`:

| Output | Obbligatorio | Uso HMI |
|---|---:|---|
| `isReady` | Si | Se `false`, il risultato Left viene ignorato; se `true`, entra nella coda Top/Left. |
| `frameIndex` | No | Log diagnostico per capire quale frame e' stato emesso. |
| `status` | No | Log diagnostico, per esempio `WaitingFrames` o `Ready`. |

Questa guardia evita che uno stitching parziale venga salvato o conteggiato come scarto macchina.

## Baseline preservata

Quando il MultiShot e' disabilitato:

- il flusso single-shot resta invariato
- TOP, SIDE, FRONT, Top3D, salvataggio immagini, contatori, allarmi, OPC UA e ricette non cambiano
- il nodo XML puo' essere presente senza generare impulsi extra

## Selezione trigger encoder / tempo da HMI

Da release `2.0.0.5`, nella pagina Digital I/O e' presente lo switch macchina `Usa trigger da encoder reale`.

Comportamento:

| Switch | Modo runtime | Uso previsto |
|---|---|---|
| `UseEncoderTrigger=true` | Encoder reale | La fotocellula crea lo zero prodotto; trigger camera, MultiShot e scarto avanzano su quota encoder. |
| `UseEncoderTrigger=false` | TimedFromPhotocell | La fotocellula genera impulsi TOP/SIDE dopo i delay in millisecondi configurati. |

Il MultiShot Side/Left richiede `UseEncoderTrigger=true`.

Se il MultiShot e' abilitato ma `UseEncoderTrigger=false`, il fronte fotocellula non arma la sessione MultiShot e viene scritto un warning nei log. Questo evita di generare sequenze incomplete quando la macchina sta lavorando in modalita' temporizzata.

## Configurazione macchina

La configurazione e' macchina, non ricetta. Il nodo viene salvato in `machine_runtime_config.xml`:

```xml
<MachineMultiShotTrigger>
  <Side>
    <Enabled>false</Enabled>
    <CameraRole>Side</CameraRole>
    <DisplayName>Left</DisplayName>
    <TriggerOutputName>OUT_CAMERA_SIDE_TRIGGER</TriggerOutputName>
    <TriggerPulseMs>10</TriggerPulseMs>
    <MinimumInterShotIntervalMs>30</MinimumInterShotIntervalMs>
    <ShotCount>5</ShotCount>
    <InitialOffsetPulses>0</InitialOffsetPulses>
    <StepPulses>4500</StepPulses>
    <MaxShotCount>12</MaxShotCount>
    <SessionTimeoutMs>3000</SessionTimeoutMs>
    <TargetLateTolerancePulses>500</TargetLateTolerancePulses>
    <RequireMachineRunning>true</RequireMachineRunning>
    <PositiveDirection>true</PositiveDirection>
  </Side>
</MachineMultiShotTrigger>
```

Default operativo: `Enabled=false`.

`TriggerOutputName` puo' puntare direttamente al signal code esistente `OUT_CAMERA_SIDE_TRIGGER`. Il controller supporta anche alias Side/Left, ma in commissioning e' consigliato usare il signal code reale.

## Configurazione da HMI

Da release `2.0.0.3` la configurazione principale non richiede piu' modifica manuale XML.

Percorso pannello:

```text
Diagnostics -> I/O Diagnostics -> Intervention Points / Commissioning -> Side/Left MultiShot
```

### Cosa imposta ogni campo

| Campo HMI | Valore XML | Cosa significa per l'operatore |
|---|---|---|
| Enable Side/Left MultiShot | `Enabled` | Abilita gli N impulsi hardware per la camera Left/SideLeft. Se disabilitato resta il flusso normale single-shot. |
| Trigger only when machine is running | `RequireMachineRunning` | Evita impulsi MultiShot quando la macchina non e' in produzione. |
| Encoder positive direction | `PositiveDirection` | Usa conteggio encoder crescente. Disabilitare solo se la macchina conta al contrario. |
| Max shots | `MaxShotCount` | Limite di sicurezza massimo per gli scatti configurabili. |
| Session timeout | `SessionTimeoutMs` | Prima del primo scatto: tempo massimo dalla fotocellula al primo target. Dopo ogni impulso: tempo massimo senza lo scatto successivo. |
| Late tolerance | `TargetLateTolerancePulses` | Tolleranza, in impulsi encoder, prima di annullare una sessione se un target e' gia stato superato. Default `500`. |
| Trigger output | `TriggerOutputName` | Uscita logica della camera Left/Side, normalmente `OUT_CAMERA_SIDE_TRIGGER`. |
| Shots | `ShotCount` | Numero immagini che VisionPro deve acquisire prima di restituire il risultato finale. |
| Pulse duration | `TriggerPulseMs` | Durata di ogni impulso digitale verso camera/trigger. |
| Minimum time between shots | `MinimumInterShotIntervalMs` | Tempo minimo tra due fronti trigger consecutivi. Serve quando la scheda genera tutti gli impulsi ma la camera ne conta meno. |
| First shot offset | `InitialOffsetPulses` | Valore calcolato dalla quota `CAMERA_TRIGGER_LEFT` se presente; in fallback usa `CAMERA_TRIGGER_SIDE`. Non va convertito a mano. |
| Distance between shots | `StepPulses` | Valore calcolato da `Stitching step` in mm e dalla taratura encoder. Non va convertito a mano. |
| Stitching step | `VisionProStitching.StepMm` | Distanza reale, in mm, usata dal ToolBlock `ImageStitching`. |
| Left calibration | `VisionProStitching.MmPerPixel` | Calibrazione camera Left in mm/pixel. |

### Procedura semplice per commissioning

1. Verificare nella tabella uscite che `OUT_CAMERA_SIDE_TRIGGER` punti al canale fisico corretto, per esempio `DO01` o quello cablato in macchina.
2. Nella sezione `Intervention Points`, impostare `CAMERA_TRIGGER_LEFT` alla posizione reale della camera Left/Side rispetto a `PRODUCT_ZERO`, in mm. Se il punto non esiste, viene usato `CAMERA_TRIGGER_SIDE` per compatibilita.
3. Abilitare `Enable Side/Left MultiShot`.
4. Impostare `Shots` uguale al numero di frame attesi dal job VisionPro.
5. Impostare `Pulse duration` con un valore accettato dall'ingresso camera. In macchina reale evitare impulsi troppo brevi: partire da `10..20 ms`.
6. Impostare `Minimum time between shots` con un valore compatibile con trigger input, esposizione e tempo busy della camera. Se la camera conta meno frame degli impulsi loggati dalla scheda, aumentare questo valore.
7. Impostare `Stitching step` in mm e `Left calibration` con i valori tarati in VisionPro.
8. Verificare nel riepilogo che gli impulsi calcolati siano coerenti con la taratura encoder.
9. Premere `Save configuration`.
10. Caricare o ricaricare la ricetta/job Left se il job non e' gia aperto.
11. Avviare produzione e verificare nei log:
   - `MULTISHOT_SIDELEFT_CONFIG_LOADED`
   - `MULTISHOT_SIDELEFT_PLAN_CREATED`
   - `MULTISHOT_SIDELEFT_TRIGGER_PULSE`
   - `VISIONPRO_STITCHING_PARAMS_APPLIED`

Se `StepMm` o `MmPerPixel` sono lasciati a zero, la HMI non crasha ma VisionPro non riceve parametri validi e viene loggato `VISIONPRO_STITCHING_CONFIG_INVALID`.

## Sequenza runtime

Quando `Enabled=true`:

1. arriva il fronte fotocellula prodotto
2. viene letto il riferimento encoder corrente
3. viene calcolata la quota encoder del primo scatto dalla posizione macchina `CAMERA_TRIGGER_LEFT`, oppure `CAMERA_TRIGGER_SIDE` se il punto Left non esiste
4. viene calcolato lo step encoder da `VisionProStitching.StepMm`
5. viene creato il piano target:

```text
InitialOffsetPulses = CAMERA_TRIGGER_LEFT_OR_SIDE.EffectiveOffsetMm * mainEncoderCountsPerMm
StepPulses = VisionProStitching.StepMm * mainEncoderCountsPerMm
Target[i] = EncoderReference + InitialOffsetPulses + (i * StepPulses)
```

6. quando l'encoder raggiunge ogni target viene generato un impulso sull'uscita Left/Side
7. se dal fronte precedente non e' ancora passato `MinimumInterShotIntervalMs`, il controller attende il tempo residuo prima di generare il nuovo fronte
8. durante l'impulso la sessione resta `PulseInFlight`, quindi non vengono generati altri impulsi
9. al termine dell'impulso il controller rivaluta solo se durante l'impulso e' arrivato un nuovo conteggio encoder reale
10. al completamento di `ShotCount` la sessione viene chiusa

Con:

```text
EncoderReference = 100000
CAMERA_TRIGGER_LEFT = 386 mm
mainEncoderCountsPerMm = 8.021
ShotCount = 10
VisionProStitching.StepMm = 10 mm
```

target attesi:

```text
InitialOffsetPulses = 386 * 8.021 = 3096 impulsi circa
StepPulses = 10 * 8.021 = 80 impulsi circa

103096
103176
103256
...
```

## Timeout Progressivo Della Sessione

Da release `3.0.9.5`, `SessionTimeoutMs` protegge la sequenza senza imporre una
durata totale fissa:

- prima del primo impulso e' misurato da `StartedAt`, cioe' dal fronte
  fotocellula;
- dopo il primo impulso e' misurato da `LastPulseStartedAt` e viene rinnovato a
  ogni scatto;
- la cancellazione ricontrolla atomicamente il riferimento temporale, per non
  eliminare una sessione che ha appena iniziato un nuovo impulso;
- il timeout continua a richiedere target encoder reali: non trasforma la
  sequenza in un generatore temporizzato.

Il log distingue `before first shot` da `after shot N/totale` e riporta
`lastEncoder`. Se `lastEncoder` non cambia, il problema e' nella progressione
encoder o nello stato macchina; se la sessione viene cancellata per stop, va
risolto prima il relativo arresto RunContinuous.

## Sicurezze

Il controller:

- non usa `Thread.Sleep`
- emette al massimo un impulso MultiShot alla volta
- non esegue catch-up burst se il polling encoder trova piu target gia superati
- se il target successivo e' troppo in ritardo rispetto a `max(StepPulses, TargetLateTolerancePulses)`, logga `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` ma continua la sessione
- dopo un impulso non rivaluta lo stesso identico sample encoder gia usato per avviare l'impulso; aspetta un nuovo conteggio encoder reale
- forza l'uscita bassa in `finally`
- cancella la sessione su reload configurazione, cambio modalita' IO, dispose viewmodel o stop `RunContinuous`
- applica `SessionTimeoutMs` come watchdog di mancato avanzamento e lo rinnova
  dopo ogni impulso
- valida `ShotCount`, `MaxShotCount`, `StepPulses`, `TriggerOutputName` e `TriggerPulseMs`
- non genera impulsi se `RequireMachineRunning=true` e la macchina non e' in `RunContinuous`

Quando MultiShot Side/Left e' attivo:

- il normale impulso singolo SIDE del batch TOP/SIDE viene disabilitato
- l'intervention point SIDE viene saltato
- TOP continua a usare il flusso single-shot esistente

## Eventi log

Eventi diagnostici aggiunti:

- `MULTISHOT_SIDELEFT_CONFIG_LOADED`
- `MULTISHOT_SIDELEFT_PLAN_CREATED`
- `MULTISHOT_SIDELEFT_SESSION_STARTED`
- `MULTISHOT_SIDELEFT_TRIGGER_PULSE`
- `MULTISHOT_SIDELEFT_SESSION_COMPLETED`
- `MULTISHOT_SIDELEFT_SESSION_TIMEOUT`
- `MULTISHOT_SIDELEFT_CANCELLED`
- `MULTISHOT_SIDELEFT_CONFIG_ERROR`

Gli eventi vengono scritti tramite `ApplicationEventLogger` con metadata strutturati:

- `camera_role`
- `display_name`
- `trigger_output`
- `encoder_reference`
- `shot_count`
- `initial_offset_pulses`
- `step_pulses`
- `minimum_inter_shot_interval_ms`
- `target_late_tolerance_pulses`
- `target_pulses`

## Note su quota primo scatto e Trim +/-

Da release `2.0.2.5`, il primo scatto MultiShot viene calcolato cosi:

1. cerca un punto intervento abilitato `CAMERA_TRIGGER_LEFT`
2. se non esiste, usa `CAMERA_TRIGGER_SIDE` per compatibilita con configurazioni precedenti
3. calcola `EffectiveOffsetMm = BaseOffsetMm + TrimOffsetMm`
4. converte in impulsi con la taratura del main encoder

Formula:

```text
FirstShotOffsetPulses = round((BaseOffsetMm + TrimOffsetMm) * CountsPerMillimeter)
```

Quindi il campo `Trim +/- mm` sposta il primo scatto solo se viene modificato sul punto intervento che il riepilogo MultiShot indica come sorgente. Per job Left usare preferibilmente `CAMERA_TRIGGER_LEFT`.

## Se la camera conta meno trigger

Il log HMI puo mostrare tutti gli impulsi generati, per esempio `pulse 1/10 ... pulse 10/10`, perche la scheda DO e' stata effettivamente comandata. Questo non garantisce che la camera li abbia acquisiti tutti: l'ingresso camera puo ignorare un fronte se:

- `Pulse duration` e' troppo breve
- due fronti sono troppo vicini
- la camera e' ancora in esposizione o busy
- il filtro/debounce dell'ingresso trigger camera e' maggiore della durata impulso
- lo step encoder e' troppo piccolo rispetto alla velocita linea

Regola pratica di commissioning:

1. portare `Pulse duration` almeno a `10..20 ms`
2. impostare `Minimum time between shots` almeno al tempo minimo richiesto dalla camera
3. verificare che `Distance between shots` in impulsi non obblighi la HMI a generare fronti piu rapidi del limite camera
4. se serve mantenere una distanza fisica precisa ma la camera non riesce a seguire, ridurre velocita linea o aumentare lo step di stitching

## Troubleshooting target late

Se compare:

```text
MULTISHOT_SIDELEFT_TARGET_TOO_LATE ... overshoot=661, allowed=600
```

da release `2.0.2.3` la sessione non viene annullata: l'evento e' una diagnostica di precisione encoder.

Lettura rapida:

- `overshoot`: quanti impulsi encoder sono stati superati rispetto al target atteso
- `allowed`: soglia ammessa, calcolata come `max(StepPulses, TargetLateTolerancePulses)`
- se `overshoot` e' poco sopra `allowed`, aumentare `TargetLateTolerancePulses` in commissioning
- se `overshoot` e' molto alto, controllare velocita linea, polling encoder, durata impulso e step reale
- se il problema capita sempre sul primo frame, controllare che `CAMERA_TRIGGER_LEFT` / `CAMERA_TRIGGER_SIDE` non sia troppo vicino alla fotocellula rispetto al tempo di polling

Nota encoder: `AdvantechDeviceManager` campiona I/O ed encoder ogni `10 ms` da release `2.0.2.3`. A `40 m/min`, un campione ogni 10 ms corrisponde comunque a circa `6,7 mm` di avanzamento linea. Se lo step di stitching e' molto piccolo, la precisione reale dipende dal jitter del polling Windows; per precisione deterministica serve una generazione impulsi hardware/counter compare.

## Watchdog VisionPro

Da release `2.0.2.4`, il watchdog VisionPro riconosce la modalita Left MultiShot.

Comportamento:

- timeout risultati esteso da `5 s` a `30 s` quando `MachineMultiShotTrigger/Side` e' abilitato in modalita Left/SideLeft
- la semplice attesa del risultato stitched finale non avvia recovery automatico
- desync temporaneo Top/Left e `Inspection pair processing stalled` vengono ignorati come causa di recovery durante MultiShot
- recovery resta attivo se VisionPro esce davvero da `RunContinuous`, se la UserResult queue e' ripetutamente vuota o se ci sono RunStatus error ripetuti

Se lo stitching reale richiede piu di `30 s`, verificare il VPP o aumentare la finestra `VisionMultiShotResultTimeout`.

## UI

La vista resta `SideCameraView`.

Se il nodo Side MultiShot e' abilitato e `DisplayName=Left`, la label operatore mostra:

```text
Left Camera Features
```

Il ruolo runtime resta `Side`.

## Test di accettazione

1. `Enabled=false`: verificare che non vengano generati impulsi extra e che il flusso single-shot resti identico.
2. `Enabled=true`, `ShotCount=5`, `InitialOffsetPulses` calcolato da `CAMERA_TRIGGER_LEFT`, `StepPulses=4500`: verificare 5 impulsi su `OUT_CAMERA_SIDE_TRIGGER`.
3. Modifica `Trim +/- mm` su `CAMERA_TRIGGER_LEFT`: verificare che il riepilogo `First shot offset` cambi.
4. Encoder fermo: verificare timeout e uscita bassa.
5. Stop macchina durante sessione: verificare cancellazione e nessun impulso residuo.
6. Cambio configurazione / ricetta / modo IO: verificare cancellazione sessione e rilettura configurazione.
7. UI: verificare che la vista Side venga mostrata come Left senza creare nuove viste.
