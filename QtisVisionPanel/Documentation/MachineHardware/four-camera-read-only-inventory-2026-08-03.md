# Inventario read-only pre-integrazione macchina pacchi a quattro telecamere

## Scopo e stato

Questo documento chiude il controllo preliminare richiesto prima di estendere
la macchina pacchi ai job VisionPro:

- `Top`;
- `Left`;
- `Right`;
- `Bottom`.

L'inventario e' stato eseguito il 3 agosto 2026 sulla baseline software
`3.0.8.4`, commit `9bebd8f`, dopo il backup descritto in
`pre-four-camera-package-integration-backup-2026-08-03.md`.

Durante il controllo:

- i file XML sono stati soltanto letti;
- i VPP sono stati deserializzati con Cognex VisionPro senza eseguire `Run`,
  `RunOnce`, `RunContinuous` o acquisizioni;
- nessun VPP e' stato salvato;
- non sono stati modificati mapping I/O, quote, ricette, DB o runtime macchina.

La conclusione principale e': **il codice HMI contiene gia' buona parte della
struttura multi-camera, ma gli asset attivi sono ancora una baseline reale a
due job (`Top` + `Side`) e non costituiscono una macchina quattro camere gia'
commissionata**.

## Sorgenti lette

| Sorgente | Percorso | SHA-256 |
|---|---|---|
| Config applicazione | `C:\QtisVision\cfg\Config.xml` | `A988A4F477380A831F1B8DD088E1A889CFDD89D3415A0C90B20876F916B440EE` |
| Ruoli camera XML | `C:\QtisVision\Programs\CameraConfig.xml` | `F711E12980BEA504C1EB0A9F3498F89AE657C3856008C81597BD184DF60BF79A` |
| Ricetta attiva | `C:\QtisVision\Programs\Index_Fiera_sample.xml` | `5ACC1121C5D04F5C651D06DA8C48FEB3E73B2A58E87EB20A1713EC422644413F` |
| VPP attivo | `C:\QtisVision\Programs\Index_Fiera_sample.vpp` | `18C1913BBFDF6230902DEA2805CE62AC4CB92091DCA845EA447896A2DB1C8432` |
| VPP MultiShot candidato | `C:\QtisVision\Programs\Mutishot.vpp` | `984A4449C82CBACF95F0E2C030EE93A68C79CFDCE2B936E74118ADEC18CCE7F1` |
| VPP recente di confronto | `C:\QtisVision\Programs\Navigator.vpp` | `11CFB664D5BB0CA568DB6D9A2DD2105D72427F1E0B093B1431C72D7B19356C28` |
| VPP prototipo legacy | `C:\QtisVision\Programs\Mutishot__.vpp` | `011FE68FEF5C51A9AE713001EC5AAA509B655D8798C1E9749ABC7FDFA8EA8BCB` |
| Runtime macchina attivo accanto all'EXE | `bin\x64\Release\cfg\machine_runtime_config.xml` | `0DB5FD91B8D25BC5247CC46840657A6BE8E2CB52DFEE7856955099A3D842EB34` |

`Mutishot__.vpp` non ha completato la deserializzazione read-only entro 90
secondi. Non viene quindi assunto come baseline o come prova di un contratto
VisionPro valido. Deve essere aperto e verificato manualmente in QuickBuild se
si vuole recuperarlo.

## Config applicazione attiva

| Campo | Valore letto | Osservazione |
|---|---|---|
| `MachineType` | `Packs` | Tipo macchina canonico corrente. |
| `Machine_type` | `Facial` | Campo legacy ancora presente e non coerente con `MachineType`. |
| `NumCamera` | `2` | Coerente con il VPP attivo. |
| `LastRecipe` | `Index_Fiera_sample.vpp` | VPP realmente attivo. |
| `Recipe_Folder` | `C:\QtisVision\Programs\` | Root config-driven. |
| `TopCameraSerial` | `DO41` | Valore non simile a un seriale camera; da bonificare in commissioning. |
| `SideCameraSerial` | `DO41` | Come sopra. |
| `FrontCameraSerial` | `DO41` | Come sopra. |
| `RearCameraSerial` | `DO41` | Come sopra. |
| `BottomCameraSerial` | `S1232080` | Unico valore con forma coerente a un seriale camera. |
| `LeftSideSealingOutput` | `LeftSideSealingOk` | Nome output configurabile gia' previsto. |
| `LeftRollCountOutput` | `LeftRollCountOk` | Nome output configurabile gia' previsto. |

I nomi job espliciti di QuickBuild hanno priorita' sui seriali e sugli ID di
`CameraConfig.xml`. I seriali anomali non cambiano oggi il ruolo di `Top` e
`Side`, ma restano un rischio per job con nomi non riconoscibili.

## Ruoli camera

### Contratto HMI corrente

| Nome job VisionPro | Ruolo normalizzato | Job/queue HMI | Vista |
|---|---|---|---|
| `Top` | `top` | `_topJob` / top queue | `TopCameraView` |
| `Top3D` | `top3d` | `_topJob` / top queue | `TopCameraView` |
| `Side` | `side` | `_sideJob` / side queue | `SideCameraView` |
| `Left` | `left` | `_sideJob` / side queue | `SideCameraView` |
| `Front` | `front` | `_frontJob` / front queue | `FrontCameraView` |
| `Right` | `rear` | `_rearJob` / rear queue | `RearCameraView` |
| `Rear` | `rear` | `_rearJob` / rear queue | `RearCameraView` |
| `Bottom` | `bottom` | `_bottomJob` / bottom queue | `BottomCameraView` |

Per compatibilita', il nome esterno richiesto `Right` resta normalizzato
internamente come `rear`. Il nome esterno `Left` usa la queue e la vista Side,
senza creare una nuova `LeftCameraView`.

### CameraConfig.xml corrente

| ID | Ruolo configurato |
|---|---|
| 0 | `Top` |
| 1 | `Side` |
| 2 | `Front` |
| 3 | `Rear` |

Manca `Bottom` e la matrice non descrive il futuro VPP `Top/Left/Right/Bottom`.
I nomi job espliciti evitano oggi un mapping errato a runtime, ma il file deve
essere riallineato prima del commissioning a quattro camere per evitare viste
di fallback incoerenti durante l'avvio.

## Inventario VPP e ToolBlock

### VPP attivo Index_Fiera_sample.vpp

| Job | Ruolo | FIFO letta | ToolBlock `ImageStitching` | Stato |
|---|---|---|---|---|
| `Top` | `top` | `Valid` | assente | job produttivo attivo |
| `Side` | `side` | `Valid` | assente | job produttivo attivo |

Output del ToolBlock `Results` di `Top`:

`A_T_L`, `A_T_R`, `D_T_Logo`, `D_B_Logo`, `D_R_Logo`, `D_L_Logo`,
`A_B_L`, `A_B_R`, `LogoPosition`, `EL_Score`, `EL_Classify`.

Output del ToolBlock `Results` di `Side`:

`Heigth`, `A_T_R`, `A_T_L`, `A_B_L`, `A_B_R`.

Il job Side ha un input `SealingArea` nel ToolBlock `Results`, ma non pubblica
`SealingArea` tra gli output. Il validator legacy controlla la saldatura
laterale soltanto se l'output e' realmente presente.

### VPP Navigator.vpp

Contiene due job `Top` e `Side`, entrambi con FIFO `Valid`. Non contiene
`ImageStitching` e non aggiunge contratti Left, Right o Bottom. E' quindi una
seconda conferma della baseline due camere, non una base quattro camere.

### VPP candidato Mutishot.vpp

| Job | Ruolo | FIFO letta | Nota |
|---|---|---|---|
| `Top` | `top` | `Invalid` | contratto Top simile alla baseline corrente |
| `Left` | `left` | `Invalid` | contiene `ImageStitching`, ma non gli output finali Left richiesti |

Il ToolBlock `Left/ImageStitching` espone il contratto corretto per i parametri:

| Direzione | Nome | Tipo letto |
|---|---|---|
| input | `OutputImage` | `CogImage8Grey` |
| input | `expectedFrames` | `System.Int32` |
| input | `stepMm` | `System.Double` |
| input | `mmPerPixel` | `System.Double` |
| output | `OutputImage` | `CogImage8Grey` |
| output | `isReady` | `System.Boolean` |
| output | `frameIndex` | `System.Int32` |
| output | `status` | `System.String` |

L'output opzionale `errorMessage`, letto dalla diagnostica HMI quando esiste,
non e' presente.

Il ToolBlock `Left/Results` pubblica soltanto:

`Heigth`, `A_T_R`, `A_T_L`, `A_B_L`, `A_B_R`.

Non pubblica:

- `LeftSideSealingOk` o uno dei relativi alias;
- `LeftRollCountOk` o uno dei relativi alias;
- `SealingArea` come output.

Di conseguenza il VPP dimostra la parte di acquisizione/stitching, ma **non e'
ancora un job produttivo completo per saldatura laterale e controllo rotolo**.
Inoltre, senza gli output Left, il validator puo' ricadere nel ramo legacy
Side e non deve essere usato come prova di un esito Left corretto.

### Contratto ImageStitching implementato nell'HMI

`VisionProStitchingParameterApplier`:

- risolve i profili `Side/Left`, `Right/Rear` e `Bottom` dal ruolo runtime;
- agisce solo sul profilo effettivamente abilitato;
- cerca ricorsivamente `ImageStitching`;
- scrive `expectedFrames`, `stepMm`, `mmPerPixel`;
- rifiuta configurazioni con scatti, step o calibrazione non validi;
- registra `VISIONPRO_STITCHING_PARAMS_APPLIED`,
  `VISIONPRO_STITCHING_TOOLBLOCK_NOT_FOUND`,
  `VISIONPRO_STITCHING_INPUT_MISSING` e
  `VISIONPRO_STITCHING_CONFIG_INVALID`.

`ImageStitchingStatusReader` conosce `isReady`, `frameIndex`, `status` ed
`errorMessage`. Il relativo provider per letture frame-by-frame e' pero'
disconnesso intenzionalmente: accedere agli oggetti VisionPro da un worker
background non e' considerato sicuro. La baseline usa impulsi HMI, log del
controller e risultato finale VisionPro, non `MULTISHOT_FRAME_ACK`.

## Encoder e modo trigger attivi

| Campo | Valore |
|---|---|
| Asse | `ENC_CONVEYOR_MAIN` |
| Scheda/canale | `PCIE-1884-AE` / `Counter0` |
| PPR | `2048` |
| Conteggio | `Quadrature x4` |
| mm/rev | `100` |
| counts/mm calcolati | `81,92` |
| Zero macchina | `CONVEYOR_START` |
| Fotocellula | `0 mm` |
| `UseEncoderTrigger` | `false` |
| `TriggerSchedulingMode` | `TimedFromPhotocell` |
| `VirtualConveyorEnabled` | `false` |

Il runtime corrente e' quindi configurato per trigger camera a tempo dalla
fotocellula, non per punti camera encoder-driven. I profili MultiShot sono
tutti disabilitati, quindi questa configurazione non sta oggi eseguendo
sequenze MultiShot encoder.

Prima di abilitare MultiShot su Left, Right o Bottom serve una decisione di
commissioning esplicita sul modo encoder. Non e' corretto compensare il modo
trigger con offset ricetta.

## Quote e segnali correnti

| Punto | On | Quota effettiva | Segnale richiesto | Pulse | Mapping fisico letto | Esito inventario |
|---|---:|---:|---|---:|---|---|
| `CAMERA_TRIGGER_TOP` | si | 74 mm | `OUT_CAMERA_TOP_TRIGGER` | 40 ms | `PCIE-1756-BE/DO03`, Output, CameraTrigger | coerente |
| `CAMERA_TRIGGER_SIDE` | si | 76 mm | `OUT_CAMERA_SIDE_TRIGGER` | 40 ms | `PCIE-1756-BE/DO02`, Output, CameraTrigger | coerente per macchina Side legacy |
| `CAMERA_TRIGGER_LEFT` | si | 86 mm | `OUT_CAMERA_LEFT_TRIGGER` | 40 ms | `PCIE-1756-BE/DO10`, ma Direction=`Input` | definizione fisica non valida come output canonico |
| `CAMERA_TRIGGER_RIGHT` | si | 376 mm | `OUT_CAMERA_RIGHT_TRIGGER` | 40 ms | `PCIE-1756-BE/DO09`, Direction=`Input`, Category=`PresenceSensor` | definizione fisica non valida come output canonico |
| `CAMERA_TRIGGER_BOTTOM` | si | 476 mm | `OUT_CAMERA_BOTTOM_TRIGGER` | 40 ms | `PCIE-1756-BE/DO05`, Output, CameraTrigger | coerente |
| `REJECT` | no | 350 mm | `OUT_REJECT_SOLENOID` | 80 ms | `PCIE-1756-BE/DO17`, Output, Reject | fisico definito, punto disabilitato |

Alias fisici oggi disponibili:

- Left puo' ricadere su `OUT_CAMERA_SIDE_TRIGGER` / `DO02`;
- Right puo' ricadere su `OUT_CAMERA_REAR_TRIGGER` / `DO04`.

Il resolver della vista I/O privilegia un mapping fisico reale e puo' usare
questi alias. Il controller MultiShot, invece, usa prima il nome esatto del
profilo. I profili correnti sono sicuri perche' puntano direttamente a Side e
Rear; non si deve cambiare il nome del profilo verso gli alias Left/Right
finche' le relative definizioni non sono state rese veri `Output`.

Sono presenti anche duplicati/stati incoerenti su heartbeat e uscite allarme
(`Direction=Input` su canali DO). Non sono il blocco principale della futura
integrazione camera, ma vanno bonificati con una migrazione controllata senza
sovrascrivere i canali realmente commissionati.

## Profili MultiShot macchina

Tutti i profili letti sono disabilitati.

| Profilo XML | Ruolo/display | Output | Scatti | Primo scatto | Step | Pulse / minimo | VisionPro |
|---|---|---|---:|---:|---:|---|---|
| `Side` | `Side` / `Left` | `OUT_CAMERA_SIDE_TRIGGER` | 9 | 7045 pulse, circa 86 mm | 2728 pulse, circa 33,3 mm | 15 / 30 ms | step 33,3 mm, 0,1667 mm/px |
| `Right` | `Rear` / `Right` | `OUT_CAMERA_REAR_TRIGGER` | 9 | 30802 pulse, circa 376 mm | 1884 pulse, circa 23 mm | 10 / 30 ms | step 23 mm, 0,166 mm/px |
| `Bottom` | `Bottom` / `Bottom` | `OUT_CAMERA_BOTTOM_TRIGGER` | 1 | 38994 pulse, circa 476 mm | 0 pulse | 10 / 30 ms | step 0, calibrazione 0 |

Ogni profilo ha `MaxShotCount=12`, `SessionTimeoutMs=3000` e tolleranza target
late di 500 pulse.

Il profilo Bottom e' coerente solo come single-shot con MultiShot disabilitato.
Prima di abilitarlo per una ricetta MultiShot servono almeno:

- piu di uno scatto effettivo;
- step globale positivo;
- calibrazione `mmPerPixel` positiva;
- ToolBlock `ImageStitching` nel job Bottom.

## Correzioni ricetta attiva

La ricetta `Index_Fiera_sample.xml` contiene gia' la struttura compatibile:

- offset posizione `Top`, `Side`, `Left`, `Front`, `Right`, `Rear`, `Bottom`;
- profili ricetta `SideLeft`, `RightRear`, `Bottom`;
- `EnabledMode`, `ShotCountOffset`, `StepOffsetMm`,
  `FirstShotOffsetMm` per ogni profilo.

Tutti gli offset sono `0` e tutti i modi MultiShot sono `Machine`. Il risultato
effettivo coincide quindi con la configurazione macchina e resta disabilitato.

La ricetta abilita pero' `BottomSealing=true` e `TrappedPaper=true` sia per
ispezione sia per scarto, mentre il VPP attivo non contiene un job Bottom.
Oggi il cambio ricetta OPC UA blocca esplicitamente questa combinazione; il
caricamento manuale non applica lo stesso preflight e le feature vengono poi
nascoste per assenza del ruolo Bottom. Il comportamento deve essere unificato
prima della macchina quattro camere.

## Copertura software gia' disponibile

| Area | Copertura esistente | Stato |
|---|---|---|
| Ruoli e viste | Top, Left/Side, Right/Rear, Bottom, Front | disponibile |
| Queue risultati | top, side/left, front, rear/right, bottom | disponibile |
| Orchestrazione | Top ancora del prodotto, companion attesi dai job caricati, timeout e mismatch | disponibile |
| Offset camera ricetta | tutti i ruoli richiesti | disponibile |
| Correzioni MultiShot ricetta | SideLeft, RightRear, Bottom | disponibile |
| Parametri ImageStitching | tutti e tre i profili | disponibile |
| Validator Left | saldatura e conteggio rotoli tramite output booleani | disponibile ma dipende da output non presenti nel VPP candidato |
| Validator Right | saldatura laterale, alias e fallback `SealingArea` | disponibile; conteggio/rotolo Right non implementato |
| Validator Bottom | `BottomSealing` e `TrappedPaper`, fail-safe su output mancanti | disponibile; nessun VPP Bottom letto |
| Diagnostica frame per frame | interfaccia presente | disabilitata per sicurezza thread/COM |

## Gap bloccanti prima di sviluppare

### G1 - Nessun VPP quattro camere verificato

Nessuno dei VPP ispezionati espone insieme `Top`, `Left`, `Right`, `Bottom`.
Serve prima un VPP QuickBuild di commissioning con questi nomi esatti.

### G2 - Contratto Results Left incompleto

`Mutishot.vpp` ha lo stitching, ma non gli output finali Left. La prima
baseline VPP deve pubblicare almeno:

- `LeftSideSealingOk` (`bool`, `true=OK`);
- `LeftRollCountOk` (`bool`, `true=OK`).

Il validator HMI deve inoltre selezionare il ramo Left dal ruolo del job, non
solo dedurlo dalla presenza degli output. In questo modo un output mancante
diventa un NOK tecnico chiaro e non un fallback Side silenzioso.

### G3 - Contratto Right non completo per le ispezioni richieste

Il validator Right gestisce la saldatura ma non il controllo rotolo girato.
Va concordato un output canonico, consigliato `RightRollCountOk` oppure un nome
piu aderente alla semantica reale del job, sempre booleano con `true=OK`.

### G4 - Nessun contratto VPP Bottom reale letto

Il nuovo job Bottom deve pubblicare:

- `BottomSealingOk` (`bool`, `true=OK`);
- `NoTrappedPaper` (`bool`, `true=nessuna carta intrappolata`).

Se usa MultiShot deve aggiungere lo stesso ToolBlock `ImageStitching`; se usa
single-shot il profilo MultiShot deve restare disabilitato per la ricetta.

### G5 - Mapping Left/Right ambiguo

Prima del codice servono due decisioni elettriche documentate:

- canale fisico definitivo della camera Left: oggi DO02 alias Side oppure DO10
  definito in modo non valido;
- canale fisico definitivo della camera Right: oggi DO04 alias Rear oppure
  DO09 definito in modo non valido.

Non devono restare due uscite reali concorrenti per la stessa camera.

### G6 - Contatori e scarto per ruolo

Left e Right possono rilevare lo stesso tipo di difetto sullo stesso pezzo.
La futura logica deve deduplicare per `product + camera role + feature`,
produrre un solo esito pezzo e conservare il lato che ha generato il difetto.
Riutilizzare un unico flag mutabile senza il ruolo rischia sovrascritture o
doppi incrementi.

## Estensioni compatibili proposte

Le seguenti estensioni sono definite solo dopo il confronto. Non sono ancora
implementate da questo inventario.

### Fase A - Congelare il contratto VisionPro

1. Creare un VPP di commissioning con job esatti `Top`, `Left`, `Right`,
   `Bottom`.
2. Pubblicare nel `Results` di ogni companion output booleani canonici e
   tipizzati.
3. Inserire `ImageStitching` soltanto nei job che acquisiscono MultiShot.
4. Verificare che il job restituisca il risultato finale una sola volta, dopo
   l'ultimo frame.
5. Salvare inventario VPP con job, FIFO, input/output e hash.

### Fase B - Canonicalizzare config macchina senza rompere gli alias

1. Conservare le chiavi XML esistenti `Side`, `Right`, `Bottom` per
   compatibilita'.
2. Selezionare una sola uscita fisica reale per Left e una per Right.
3. Correggere `Direction=Output`, `Category=CameraTrigger`, board e channel
   delle due definizioni canoniche.
4. Mantenere Side/Rear solo come alias di compatibilita', non come seconda
   uscita attiva concorrente.
5. Riallineare `CameraConfig.xml` ai job reali e aggiungere Bottom.
6. Abilitare il trigger encoder soltanto dopo test counts/mm, direzione e
   quote con un prodotto singolo.

### Fase C - Riutilizzare la ricetta esistente e aggiungere solo cio' che manca

Gli offset camera e le correzioni MultiShot sono gia' sufficienti e non vanno
duplicati. Servono nuovi campi ricetta solo se si vuole abilitare o scartare in
modo indipendente:

- saldatura Left;
- rotolo Left;
- saldatura Right;
- rotolo Right.

I nuovi campi, se approvati, devono essere opzionali e avere un fallback
esplicito ai flag legacy `Side_sealing`, cosi' le ricette storiche continuano
a caricarsi. Bottom puo' continuare a usare `BottomSealing` e `TrappedPaper`.

### Fase D - Rendere l'orchestrazione role-aware

1. Passare il ruolo normalizzato al validator Side/Left.
2. Aggiungere la validazione del controllo rotolo Right.
3. Produrre difetti con almeno `ProductId`, `CameraRole`, `Feature`, `Message`.
4. Deduplicare contatori/scarto sullo stesso identificatore.
5. Conservare Top come ancora del prodotto e attendere solo i job realmente
   caricati.
6. Applicare lo stesso preflight ricetta sia a caricamento manuale sia OPC UA.

### Fase E - Diagnostica commissioning non invasiva

Aggiungere un preflight read-only visibile al tecnico che mostri:

- job caricati e ruolo risolto;
- presenza e tipo degli input/output obbligatori;
- profilo MultiShot globale, correzione ricetta e valore effettivo;
- punto macchina, quota effettiva, segnale logico, board e canale;
- counts/mm e conversione mm/pulse;
- numero impulsi richiesti/generati e risultato finale ricevuto;
- errori bloccanti prima del `RunContinuous` soltanto quando la configurazione
  quattro camere e' esplicitamente richiesta.

Non riattivare il polling COM frame-by-frame da thread background. Per il
commissioning usare log controller, contatore frame nel job VisionPro e output
finale, oppure introdurre in futuro una lettura serializzata sul thread Cognex.

## Ordine di implementazione raccomandato

| Ordine | Task | Motivo |
|---:|---|---|
| 1 | Contratto e VPP di commissioning | senza output reali il codice non e' collaudabile |
| 2 | Decisione canali Left/Right e bonifica mapping | evita impulsi sulla DO sbagliata |
| 3 | Preflight read-only HMI | rende visibili gli errori prima della produzione |
| 4 | Validator role-aware e Right roll check | chiude gli esiti richiesti |
| 5 | Deduplica ruolo/feature in orchestrazione e contatori | evita doppio conteggio e sovrascritture |
| 6 | Eventuali campi ricetta/DB opzionali | solo dopo avere deciso la granularita' richiesta |
| 7 | Collaudo camera singola, poi due, poi quattro | riduce il rischio di pairing errato |

## Criteri di ingresso alla fase di codice

La fase di modifica runtime puo' iniziare solo quando sono disponibili:

- VPP quattro camere apribile con FIFO coerenti;
- tabella firmata dei canali fisici Left e Right;
- output `Results` concordati e tipizzati;
- scelta esplicita sul controllo rotolo Right e sulla sua abilitazione/scarto
  per ricetta;
- counts/mm e quote camera verificati sulla macchina;
- decisione Bottom single-shot/MultiShot per almeno una ricetta campione.

Fino a quel momento la baseline `3.0.8.4` e il tag di backup restano il punto di
ripristino operativo.
