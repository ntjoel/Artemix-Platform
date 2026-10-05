# Job Editor Live Preview - Hardware Trigger

## Scopo

Questa procedura serve quando la camera e' configurata con trigger hardware esterno e la Live Preview standard di VisionPro non acquisisce immagini in modo continuo.

La HMI apre una finestra tecnica, arma un run singolo del job VisionPro selezionato e genera un impulso sulla stessa uscita fisica usata dalla macchina per far scattare la camera. Quando il run termina, ricava il `CogToolGroup` dal `VisionTool` del job e legge direttamente `CogInputImageTool.InputImage` dal tool QuickBuild `Image Source` (normalmente `Tools[0]`). In questo modo la Live Preview non mostra overlay o risultati elaborati dagli strumenti di ispezione.

Se il `VisionTool` non e' un `CogToolGroup`, il tool sorgente non esiste o `InputImage` e' nullo, la HMI scrive il motivo nel log e usa come fallback il record display configurato per la produzione (`Config.xml -> LasRunParam`). Il fallback mantiene la compatibilita' con i VPP precedenti.

Non viene avviata la live FIFO VisionPro e non viene tentata la reinizializzazione della FIFO del job. La produzione, il MultiShot, l'encoder e lo scarto non vengono modificati da questa funzione.

Durante questa modalita gli eventi globali VisionPro di produzione vengono ignorati: il run live e' tecnico, quindi non deve svuotare code produzione, cambiare stato macchina o generare audit `VisionPro job stopped`.

## Dove si configura

Aprire `System Editors -> Job Tool Editor`.

Nella barra dei comandi sono disponibili:

| Campo | Significato | Default |
| --- | --- | --- |
| `Interval (ms)` | Tempo tra l'inizio di uno scatto live e l'inizio dello scatto successivo. | `1000` |
| `Pulse (ms)` | Tempo per cui l'uscita trigger camera resta attiva. | `20` |
| `Exposure (us)` | Tempo esposizione camera in microsecondi. Premere `Apply exposure` per scriverlo sulla camera selezionata. | `0` = non forzare |
| `Read` | Legge dalla camera il tempo esposizione attualmente impostato e aggiorna l'indicazione `Current`. | - |
| `Current` | Valore esposizione effettivo letto dalla camera selezionata. | - |
| `Apply exposure` | Applica il valore esposizione alla camera del job selezionato e lo salva nella configurazione runtime. | - |
| `Run once` | Arma/esegue soltanto il job della tab selezionata. Non pulsa automaticamente l'uscita fisica. | - |
| `Live preview` | Avvia la finestra tecnica, il run singolo VisionPro e il loop di trigger hardware. | - |
| `Stop preview` | Ferma il loop e forza l'uscita trigger allo stato inattivo. | - |

I valori sono salvati in `machine_runtime_config.xml`, dentro `RuntimeBindings`:

```xml
<LivePreviewIntervalMs>1000</LivePreviewIntervalMs>
<LivePreviewPulseMs>20</LivePreviewPulseMs>
<LivePreviewExposureUs>0</LivePreviewExposureUs>
```

Il valore esposizione viene scritto tramite accesso GigE Vision sui nodi piu comuni (`ExposureTime`, `ExposureTimeAbs`, `ExposureTimeRaw`). Quando disponibile, la HMI imposta anche `ExposureAuto=Off` prima della scrittura.

## Sequenza operativa

1. Verificare che la macchina non debba produrre mentre si usa il Job Tool Editor.
2. Entrare con utente tecnico autorizzato.
3. Aprire `Job Tool Editor`.
4. Selezionare il job camera corretto, ad esempio `Top`, `Left`, `Right/Rear` o `Bottom`.
5. Impostare `Interval (ms)` e `Pulse (ms)`.
6. Se serve calibrare la luminosita, premere `Read` per leggere il valore corrente dalla camera.
7. Inserire il nuovo valore in `Exposure (us)` e premere `Apply exposure`.
8. Premere `Live preview`.
9. La HMI arma il job selezionato in `RunOnce`, poi pulsa l'uscita camera e infine aggiorna la finestra con `CogInputImageTool.InputImage`; se non disponibile usa il record LastRun configurato.
10. Controllare che la camera riceva gli impulsi e che le immagini arrivino nella finestra live.
11. Durante il live, variare l'esposizione e premere `Apply exposure` fino al valore ottimale; `Current` viene aggiornato con il valore effettivo riletto dalla camera.
12. Premere `Stop preview` prima di uscire, oppure chiudere la finestra live: in entrambi i casi la HMI forza il trigger allo stato inattivo.

## RunOnce tecnico

`Run once` e `Live preview` hanno scopi differenti:

- `Run once` coinvolge esclusivamente il job della tab selezionata e non prova ad
  avviare gli altri job presenti nel VPP;
- su una camera con trigger hardware, `Run once` arma il job ma lo scatto deve
  arrivare dal segnale esterno;
- `Live preview` completa invece la sequenza tecnica: arma il job selezionato,
  genera l'impulso sull'uscita configurata e aggiorna il display;
- un errore viene riportato con il nome del job e lo stato FIFO. Il log di
  successo viene scritto solo quando VisionPro ha accettato il comando.

Log utili:

- `VISIONPRO_RUN_ONCE_START`;
- `VISIONPRO_RUN_ONCE_STARTED`;
- `VISIONPRO_RUN_ONCE_FAILED`.
- `LIVE_IO|Live preview display source selected`: riporta
  `CogInputImageTool.InputImage` oppure `LastRunFallback` una volta per sessione.
- `LIVE_IO|Raw live preview source unavailable`: spiega perche' il cast del
  `VisionTool`, la ricerca del `CogInputImageTool` o la lettura di `InputImage`
  non sono riusciti prima del fallback.

## Coerenza MultiShot e job VisionPro

La configurazione effettiva e' data da profilo macchina piu' override ricetta.

- Se la ricetta imposta `EnabledMode=Disabled`, il job puo' funzionare in
  scatto singolo e non deve essere forzato a contenere `ImageStitching`.
- Se il profilo effettivo e' abilitato, il job del ruolo corrispondente deve
  contenere il ToolBlock `ImageStitching` e gli input configurati, inclusi
  `expectedFrames`, `stepMm` e `mmPerPixel`.
- Con doppia illuminazione attiva devono essere presenti anche gli input previsti
  per il profilo DALSA.

Il messaggio `VISIONPRO_STITCHING_TOOLBLOCK_NOT_FOUND` indica quindi una
incoerenza tra configurazione effettiva e VPP. Correggere il modo MultiShot della
ricetta oppure completare il job VisionPro; non aggirare il controllo modificando
il mapping I/O.

## Risoluzione uscita

La HMI risolve l'uscita camera partendo dalla configurazione macchina:

- prima cerca il punto `InterventionPoints` del ruolo camera selezionato
- poi considera il profilo `MachineMultiShotTrigger` del ruolo
- infine usa i nomi standard, con alias compatibili:
  - `Left` puo usare `OUT_CAMERA_LEFT_TRIGGER` o `OUT_CAMERA_SIDE_TRIGGER`
  - `Right` puo usare `OUT_CAMERA_RIGHT_TRIGGER` o `OUT_CAMERA_REAR_TRIGGER`
  - `Bottom` usa `OUT_CAMERA_BOTTOM_TRIGGER`
  - `Top` usa `OUT_CAMERA_TOP_TRIGGER`

## Se non arrivano immagini

Controllare in ordine:

1. Il job selezionato deve essere pronto ad acquisire da trigger hardware esterno; la HMI arma il job con un run singolo ma non forza la live FIFO VisionPro.
2. Il segnale camera deve esistere in `Machine outputs` con `Board` e `Channel` compilati.
3. Il cablaggio della camera deve essere sullo stesso output indicato dal riepilogo I/O.
4. `Pulse (ms)` deve essere sufficiente per il trigger della camera.
5. `Interval (ms)` deve essere piu lungo del tempo minimo che la camera richiede tra due acquisizioni.
6. La camera deve essere pronta e non occupata da QuickBuild o da un altro processo.
7. Se il tool editor VisionPro mostra immagini aggiornate ma il popup resta blu, verificare che il `VisionTool` del job sia un `CogToolGroup` e che contenga un `CogInputImageTool`, normalmente come primo tool `Image Source`.
8. Controllare il log `LIVE_IO|Live preview display source selected`: il percorso raw corretto e' `CogInputImageTool.InputImage`. Con `LastRunFallback`, leggere prima il warning `Raw live preview source unavailable` e verificare anche che `Config.xml -> LasRunParam.LastRunView1/2/3` punti a un sub-record immagine valido.
9. Se nel log compare `LIVE_IO|VisionPro live preview run timeout`, VisionPro non ha completato il run dopo il trigger: verificare acquisizione, exposure, trigger mode e cablaggio.
10. Se `Apply exposure` fallisce, verificare che il job usi una camera GigE reale e che il nodo esposizione sia scrivibile.
11. Se ricompaiono log ripetitivi `Pending VisionPro results cleared` durante Live Preview, verificare che la release in uso sia almeno `3.0.2.1`.

## Nota di sicurezza

Questa funzione e' per setup e diagnostica. Non sostituisce il ciclo produzione e non deve essere usata come logica di scatto in marcia automatica.
