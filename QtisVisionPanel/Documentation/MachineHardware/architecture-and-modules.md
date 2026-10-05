# Architettura e Moduli — QtisVisionPanel

Documento vivente. Aggiornare dopo ogni modifica architetturale.
Ultima revisione: **2026-09-25**

Nota 2026-09-28 - classificatore ONNX per camera:
- ogni camera ha un `OnnxDefectClassifier` proprio. TOP (`ServiceLocator.DefectClassifier`) e
  SIDE (`SideDefectClassifier`) sono invariati. REAR/RIGHT, FRONT e BOTTOM stanno in
  `ServiceLocator.AdditionalDefectClassifiers`; `AllDefectClassifiers` li elenca tutti;
- configurazione: TOP e SIDE usano i campi dedicati di `MachineRuntimeBindings`, le altre camere
  `AdditionalDefectClassifierProfiles` (`DefectClassifierCameraProfile`). Il punto di lettura
  comune è `ResolveDefectClassifierProfile(role)`;
- chiavi di ruolo del classificatore = chiavi di `label.json` (top, side, rear, front, bottom).
  Le sigle immagine sono in `OnnxDefectClassifier.GetImageTags`, allineate a `CAMERA_IMAGE_TAGS`
  del trainer Python. Una nuova camera va aggiunta in tutti e due i punti.

Nota 2026-09-28 - nome camera in UI:
- il ruolo interno resta quello fisico (`left` = job Side o Left, `right` = job Rear o Right);
  le etichette operatore passano tutte da `CameraConfigurationHelper.GetCameraRoleDisplayLabel`,
  che restituisce il nome del job VPP (SIDE, REAR, ...). Non reintrodurre switch locali
  role -> "LEFT"/"RIGHT" nelle ViewModel o nelle view.

Nota 2026-09-25 - I/O predisposto dal VPP e scarto OPEN/CLOSE:
- `MainWindow.ProvisionCameraIoFromActiveVpp` -> `DigitalIOViewModel.ProvisionCameraIoAsync`:
  il VPP (JobMapping) è la fonte del numero e del ruolo delle camere. La configurazione
  I/O viene completata con le uscite trigger, i punti `CAMERA_TRIGGER_<ROLE>` (disabilitati),
  le uscite allarme bloccante/non bloccante e scarto OPEN/CLOSE, tutti con canale vuoto;
  l'operatore sceglie solo il canale (`OutputChannelOptions` / `InputChannelOptions`);
- `MachineRuntimeBindings` ha `RejectOpenSignalCode`, `RejectCloseSignalCode`,
  `RejectCloseDelayMs`: con entrambe le uscite assegnate lo scarto è a due comandi, altrimenti
  resta l'impulso singolo su `RejectSignalCode`.

Nota 2026-09-25 - spazio canali PCIE-1756, worker impulsi ed errori BDaq:
- `Models/Pcie1756ChannelMap` e' la fonte unica dello spazio canali della scheda digitale:
  32 DI e 32 DO su 4+4 porte da 8 bit, buffer legacy da 8 byte (byte 4..7 sempre 0), canali
  PCIE-1884 100..103. La usano `AdvantechDeviceManager`, `DigitalIOViewModel`, `MainWindow`,
  `AlarmNotificationWindow` ed `EjectionAlarmCardViewModel`; `MachineRuntimeIo.ParseChannelNumber`
  resta un estrattore di cifre indipendente dalla scheda;
- `AdvantechDeviceManager` separa encoder critici e telemetria su worker dedicati
  (`Qtis.EncoderCritical`, `Qtis.EncoderTelemetry`) e genera gli impulsi sul worker
  `Qtis.OutputPulse` con scadenze `Stopwatch`: piu' canali possono essere alti insieme, la stessa
  uscita resta serializzata. Tutti i thread restano gestiti, quindi una GC bloccante li sospende;
- contratto `IIODeviceManager.PulseOutputAsync`: il Task termina con `OutputPulseException`
  (`Phase` = `Rise` o `Fall`) se la scheda rifiuta un fronte. `WriteOutputAsync` mantiene invece
  il contratto storico: registra il rifiuto ma non lo propaga;
- `CameraTriggerCorrelationTracker.Withdraw(role, productId, pointCode)` ritira il ticket
  visione di un trigger senza fronte; `DigitalIOViewModel.PulseMappedOutputAsync` restituisce
  se il fronte di salita e' partito;
- identita' camere fisiche: `CameraConfigurationHelper.NormalizePhysicalCameraRole` e
  `CameraInspectionMatrix.NormalizeRole` riducono Side/Left a `left` e Rear/Right a `right`.
  E' l'unica chiave per stato runtime, ticket visione e code risultati; Side/Rear restano
  alias accettati in VPP, ricette e XML storici, e il campo fisico ha la precedenza;
- dettagli e collaudo in `code-changes-log.md` (2026-09-25) e in
  `encoder-trigger-deterministic-workers-and-pcie1884-compare-2026-09-24.md`.

Nota 2026-09-14 - localizzazione configurazione ispezioni e hardening cattura:
- le label dinamiche della matrice vista x ispezione, i modi camera, classi
  accettate, soglia, tooltip e messaggi di stato passano da
  `ServerMessagePersonalize`; i codici tecnici e XML restano invariati;
- `InspectionFeatureLocalization` risolve nome e descrizione delle feature note
  usando le chiavi lingua esistenti, con fallback ai dati DB;
- `WindowScreenshotHelper` verifica handle e risultato di `PrintWindow` e
  rilascia sempre l'HDC; `CameraDisplayManager` rifiuta record superati e usa
  `CreateContentBitmap` come fallback;
- contratto e collaudo completi in
  `visionpro-annotated-image-save-2026-09-14.md`.

Nota 2026-09-11 - salvataggio immagini annotate, rendezvous display/salvataggio:
- `Services/WindowScreenshotHelper.cs` e' un nuovo helper statico che cattura un
  `CogRecordDisplay` con `user32!PrintWindow` (`PW_CLIENTONLY`): legge la superficie
  realmente dipinta dal controllo ActiveX, aggirando il rettangolo nero restituito da una
  cattura GDI ordinaria attraverso il `WindowsFormsHost` WPF;
- `AnnotatedImageCache` non e' piu' una cache a sola lettura ma un punto di incontro fra
  produttore e consumatore: `CreateRequest` prenota, `Store` completa le richieste pendenti
  dello stesso record, `Request.TryWait` attende con timeout. Serve perche' il salvataggio
  parte circa 8 ms dopo l'esito mentre la cattura avviene all'aggiornamento della vista:
  l'ordine dei due eventi non e' garantito in nessuna delle due direzioni;
- la chiave condivisa fra `CameraDisplayManager` e `SaveImage` e' il record RADICE del
  risultato, non il sottorecord mostrato a display;
- l'attesa dell'immagine annotata vive esclusivamente sul worker immagini, con scadenza
  condivisa fra tutte le viste di un pezzo (1500 ms): ne' il ciclo d'ispezione ne' il thread
  UI attendono mai il rendering;
- `PendingRecordSave` trasporta `AnnotatedImageRequests` invece di
  `ViewsWithAnnotatedImage`; le richieste vengono sempre rilasciate in `finally`, anche
  quando la coda record e' piena;
- `SaveImage.MapViewNameToDisplayRole` e `GetResultRecord` tengono conto di Top3D attivo
  (`T` -> `top3d`, `F`/`S` -> `top2d`);
- dettaglio completo, log e correzione sull'ordine alias della vista side in
  `code-changes-log.md`, sezione [2026-09-11] "Immagini annotate: cattura via PrintWindow".

Nota 2026-09-03 - profilo camera/ispezione per ricetta, release 3.1.5.1:
- `inspectionViewConfiguration` salva nel file ricetta la matrice vista x ispezione e le
  classi Classify accettate per ciascuna vista;
- le ricette legacy continuano a ereditare i default macchina dalle tabelle
  `cfg_inspection_view` e `cfg_view_classification` finche' il profilo non viene salvato;
- l'abilitazione camera usa il contratto esistente `CameraTriggers`, senza duplicare mapping
  fisici o introdurre un secondo flag in conflitto;
- camere disabilitate sono escluse da trigger effettivi, attesa companion, watchdog risultati,
  code di elaborazione e container display;
- il profilo strict viene materializzato per tutti i ruoli, TOP-only e' valido e il watchdog
  mantiene un heartbeat distinto per ogni camera;
- il polling encoder non legge piu' il frequency meter BDaq: usa una stima dai delta e pubblica
  la fase piu' lenta negli overrun; dettagli in `trigger-timing-hardening-2026-09-03.md`;
- dettagli del profilo in `recipe-camera-inspection-profile-2026-09-03.md`.

Nota 2026-09-02 - correlazione trigger/prodotto e pipeline UI disaccoppiata, release 3.1.2.0:
- `DigitalIOViewModel` inoltra i punti raggiunti a un worker output dedicato, lasciando il polling
  Advantech libero da scritture DO e pubblicazione UI;
- `CameraTriggerCorrelationTracker` assegna un ticket `ProductId` a ogni trigger HMI single-shot;
- `InspectionOrchestrator` usa l'identita' prodotto quando disponibile e mantiene il fallback
  timestamp/FIFO per synthetic, trigger esterni e percorsi legacy;
- `CameraDisplayManager` scarta completion grafiche fuori ordine e lavora a priorita' Background;
- `SaveImage` cattura record immutabili in una coda bounded e non usa piu'
  `CogRecordDisplay.CreateContentBitmap` sul Dispatcher durante la produzione.

Nota 2026-08-26 - Classify come ispezione autonoma, release 3.1.1.0:
- `AIClassification` e' una feature ricetta indipendente con abilitazione
  ispezione e abilitazione scarto separate;
- `IToolBlockValidator` applica un solo contratto Classify a tutti i ruoli
  camera e incrementa il contatore dedicato, deduplicato per ciclo pezzo;
- `MainWindow`, `CameraDisplayManager` e il layer allarmi trattano gli errori AI
  separatamente da `SurfaceCheck`, `SideSealing` e dalle altre feature;
- le ricette storiche mantengono il comportamento precedente perche' i nuovi
  flag XML hanno default `false`;
- dettagli e collaudo sono in
  `ai-classification-standalone-inspection-2026-08-26.md`.

Nota 2026-08-24 - snapshot output VisionPro per ciclo, release 3.1.0.7:
- `UserResultAvailable` assegna una `ResultSequence` e crea un ToolBlock
  output-only con copie dei valori scalari del ciclo appena completato;
- `CameraResult.ToolBlock` resta il riferimento live usato dai fallback
  immagine, mentre `CameraResult.OutputSnapshot` e' l'unica sorgente di
  validazione e card classificazione;
- gli snapshot sono rilasciati dopo il gruppo, allo svuotamento code e durante
  il coalescing sintetico, evitando crescita memoria nel funzionamento 24/7;
- il log del gruppo espone sequenza TOP e sequenze companion per il collaudo;
- lo stato cromatico della classificazione usa `ValidationResult.Measurements`
  quando contiene `Classification`, evitando che classi libere come
  `Sealing Open` vengano mostrate come neutrali o conformi.

Nota 2026-08-24 - classificazione VisionPro comune nelle viste camera, release 3.1.0.6:
- `VisionClassificationResultReader` legge classe e score dai terminali radice
  gia' calcolati dal job, con alias compatibili e senza eseguire inferenza;
- `InspectionResult` conserva un risultato informativo distinto per Top, Side,
  Front, Rear e Bottom; la proiezione Side/Left e Rear/Right resta responsabilita'
  di `CameraDisplayManager`;
- `ClassificationFeatureCard` e' un controllo XAML unico riusato da tutte le
  sette viste, con icona vettoriale, testo localizzato e stato verde/rosso/blu;
- la card scompare se la camera non pubblica una classe, evitando risultati
  obsoleti dopo un cambio job o prodotto;
- invariante: il mapping visuale delle classi non modifica validator, esito,
  contatori, scarto, ricetta, XML, DB, trigger o pairing multi-camera.

Nota 2026-08-05 - display multi-camera responsive, release 3.0.8.6:
- `CameraModel` separa il ruolo runtime dal ruolo visuale: `Right` continua a
  usare il percorso compatibile `rear`, ma seleziona `RightCameraView`;
- `CameraContainer` espone pannelli dedicati `LeftCameraView` e
  `RightCameraView`, con saldatura laterale e controllo rotolo;
- la griglia usa al massimo quattro colonne; la quinta camera apre la seconda
  riga e l'altezza dei `CogRecordDisplay` viene calcolata dal viewport;
- cambio ricetta, aggiornamento record, indicatori e dispose sono allineati
  alle nuove viste senza aggiungere configurazioni macchina o ricetta.

Nota 2026-07-21 - audit completo e consolidamento manuale:
- rimossi file, converter, modelli, handler e metodi privati dimostrabilmente senza consumer;
- rimosso l'esito GOOD artificiale eseguito durante il caricamento ricetta;
- il manuale HMI carica solo i capitoli operatore numerati e usa il Markdown come sorgente unica;
- inventario, verifiche, rischi residui e criteri di collaudo sono in
  `full-application-audit-2026-07-21.md`;
- il piano corrente e' `Documentation/improvement-plan-2026-07.md`; le sequenze storiche in questo
  documento restano contesto, non sostituiscono il piano operativo aggiornato.

Nota 2026-07-14 - workspace Recipe Management responsive, release 3.0.7.7:
- `RecipeManagerView` usa un solo `ScrollViewer` per l'intera area dati/parametri, evitando due
  viewport verticali concorrenti nella stessa procedura operativa;
- `RecipeWorkspaceGrid` conserva due pannelli logici: dati principali ricetta e parametri tecnici.
  Non sono stati spostati binding o responsabilita nel ViewModel;
- `RecipeManagerView.ApplyRecipeWorkspaceLayout` cambia solo `Grid.Row`, `Grid.Column`, larghezze e
  margini: a partire da `1040 px` utili usa due colonne, sotto la soglia impila i parametri;
- il layout e una proiezione UI runtime. Non legge o scrive nuove configurazioni e non modifica
  ricette, DB, VisionPro, I/O o ciclo di produzione;
- panoramica ricetta e diagnostica database sono property grid compatte; le sezioni mantengono i
  nomi XAML usati dal server messaggi e tutti i gate autorizzativi esistenti.

Nota 2026-07-14 - anteprima globale/effettiva offset ricetta, release 3.0.7.6:
- `RecipeMachineRuntimeOverview` e un projection model read-only costruito dal file macchina salvato
  e dalla ricetta selezionata; non conserva una seconda configurazione e non scrive XML;
- la proiezione invoca `RecipeMachineRuntimeResolver`, quindi quote, alias camera, clamp e fallback
  coincidono con quelli applicati al runtime produttivo;
- `RecipeManagerViewModel` ricostruisce la proiezione solo su eventi UI a bassa frequenza (cambio
  ricetta, apertura pagina, fine modifica campo): nessun lavoro viene aggiunto al percorso caldo di
  acquisizione/ispezione;
- le quote MultiShot in impulsi sono presentate in mm usando il main encoder globale. Una
  calibrazione non valida produce `non disponibile`, mai una stima;
- invariante: questa UI e informativa e non puo modificare mapping I/O, quote globali, calibrazione o
  stato runtime.

Nota 2026-07-14 - offset camera e MultiShot per ricetta, release 3.0.7.5:
- `RecipeParameters.RecipeData.machineRuntimeAdjustments` contiene esclusivamente delta
  prodotto-specifici: offset posizione per ruolo camera e profili `SideLeft`, `RightRear`,
  `Bottom` con modo `Machine/Enabled/Disabled`, delta scatti, delta step e delta origine sequenza;
- `RecipeMachineRuntimeResolver` crea una copia runtime di punti intervento e profili MultiShot.
  I/O, encoder, base/trim commissioning, pulse timing, calibrazione e limiti restano nella
  configurazione macchina e non vengono mai riscritti dal cambio ricetta;
- formula quota: `base macchina + trim commissioning + offset ricetta`. Numero scatti e step sono
  ottenuti aggiungendo i delta ricetta e applicando i limiti macchina;
- `DigitalIOViewModel.EffectiveRuntimeConfiguration` e' la sorgente in memoria per tracking e
  MultiShot; `MainWindow` usa la stessa istanza per scrivere `expectedFrames`, `stepMm` e
  `mmPerPixel` in VisionPro. Nessuna lettura config e nessuna allocazione aggiuntiva per prodotto;
- tutti i cambi ricetta, inclusi autoswitch e OPC UA, convergono su `InitializeRecipeAsync` e
  riapplicano le correzioni prima del RunContinuous;
- ricette storiche senza il nuovo nodo XML hanno offset zero e modo `Machine`: comportamento
  precedente invariato. Il flusso `TimedFromPhotocell` continua a usare i trigger delay ricetta;
  gli offset in mm restano propri del tracking encoder;
- invariante: la ricetta non puo' cambiare board, channel, polarity, counts/mm, timeout, intervallo
  minimo, durata impulso, calibrazione camera o limiti massimi del MultiShot.

Nota 2026-07-13 - isolamento risorse ONNX, release 3.0.7.2:
- il punto di chiamata da `MainWindow` resta non bloccante. Se il profilo camera e' disabilitato o
  la sessione non e' pronta, `RunShadowComparison` termina con una sola lettura volatile: non crea
  task, non cerca file e non alloca immagini o tensor;
- ogni istanza TOP/SIDE mantiene il proprio gate single-flight e le due istanze condividono un gate
  globale. La memoria e' quindi bounded: al massimo una inferenza usa bitmap/tensor, mentre
  l'eventuale seconda camera attende in modo asincrono senza averli ancora allocati;
- ONNX Runtime usa esecuzione sequenziale, un thread intra-op, un thread inter-op, arena CPU e
  memory pattern disabilitati. Il worker prova a usare priorita' `BelowNormal`; un host che non
  consente il cambio priorita' conserva comunque i limiti 1x1;
- il resize usa `LockBits` BGR anziche' `Bitmap.GetPixel`. L'input del modello resta quello del
  training (tipicamente 224x224), non la risoluzione nativa della camera;
- gli accordi ordinari `VISION_ML_SHADOW` sono a livello DEBUG. Disaccordi, timeout ed errori
  restano WARN/audit. Le statistiche espongono media/massimo inferenza e attesa del gate globale;
- non viene usata memoria virtuale come strategia prestazionale: il paging renderebbe la latenza
  meno deterministica. Il contratto e' limitare concorrenza e allocazioni RAM;
- invariante: ONNX resta advisory e non entra nella decisione VisionPro, nel pairing camere, nei
  contatori, nel trigger o nello scarto.

Nota 2026-07-13 - AI shadow/training hardening, release 3.0.7.1:
- `OnnxDefectClassifier` non assume piu' che il file raw esista gia' subito dopo la decisione di
  salvataggio: il worker advisory attende fino a 10 s, verifica l'accesso esclusivo al file e poi
  esegue decode/inferenza. L'attesa resta fuori dal thread ispezione e il gate single-flight evita
  code di inferenze.
- Ogni reload assegna una generazione. Un task nato con una generazione precedente non puo'
  registrare un campione dopo il cambio configurazione. Le statistiche vengono azzerate solo quando
  cambia il fingerprint del contesto (camera, file/versione modello, timestamp/size file, resize,
  grayscale, mean/std o soglia NOK); un semplice reload dello stesso contesto le preserva.
- Il fingerprint produce anche un identificatore `set` stabile, riportato nella UI e in ogni evento
  `VISION_ML_SHADOW` insieme a versione e soglia. Gli aggregati live e le analisi log restano quindi
  separabili con la stessa chiave tecnica.
- `ShadowModeStats` espone il contesto attivo e i contatori diagnostici `SkippedBusy` e
  `ImageWaitTimeouts`. Il classificatore resta advisory e non modifica esito, scarto o contatori.
- `DefectClassifierTrainingService` e il trainer Python condividono la stessa regola label: la label
  globale vale solo per TOP legacy senza un blocco `labels` non vuoto; le camere moderne richiedono
  la propria label esplicita.
- La validation usa un holdout cronologico stratificato sui campioni piu' recenti di ogni classe.
  Le metriche sono `qualified` con almeno 10 OK e 10 NOK nel solo holdout; altrimenti il modello e'
  comunque esportato, ma UI, note modello, log e audit lo marcano `provisional`.
- Invariante: il modello non viene abilitato automaticamente. La decisione di salvarlo/attivarlo in
  shadow-mode resta supervisionata da Installer/Administrator.

Nota 2026-07-13 - AI: classificatore ONNX esteso alla camera SIDE, release 3.0.6.8:
- `OnnxDefectClassifier` e' ora PARAMETRICO per camera (`new OnnxDefectClassifier("top"|"side")`): il ruolo determina suffisso immagine (`_T_`/`_F_`), campi config letti (Top vs Side*) e risultato-regole confrontato (`TopResult`/`SideResult`). Ogni istanza ha stato proprio (gate single-flight, sessione ONNX, statistiche shadow).
- `ServiceLocator` espone due singleton: `DefectClassifier` (top) e `SideDefectClassifier` (side); entrambi creati in Initialize() e disposti in Reset(). MainWindow chiama `RunShadowComparison` una volta per istanza dopo l'ispezione.
- Config: set di campi `SideDefectClassifier*` parallelo al Top in `MachineRuntimeBindings` (modello/preprocessing per camera). Python path/cartella/epoche restano condivisi (proprieta' di macchina).
- Dataset: `TrainingDataCollectionService` scrive in `label.json` le etichette PER-CAMERA (`labels`) da `InspectionResult.{Top,Side,...}Result.IsValid`. Il trainer (`--camera`) usa l'etichetta della camera; il TOP ha fallback alla label globale per i dataset storici, la SIDE no (evita contaminazione).
- Invariante: aggiungere una camera al classificatore = nuova istanza parametrica + campi config paralleli + hook shadow + (per il training) suffisso immagine ed etichetta per-camera. Chi cambia il contratto ONNX (canali/normalizzazione/classi) aggiorna sia OnnxDefectClassifier sia lo script Python.

Nota 2026-07-10 - AI training progress heartbeat, release 3.0.6.4:
- `DefectClassifierTrainingService` forza `PYTHONUNBUFFERED=1` e pubblica un heartbeat ogni 15 secondi mentre il processo Python e' vivo ma non emette output. Questo evita che il pannello sembri bloccato durante import `torch`, resize immagini o calcolo CPU.
- `Scripts/AI/train_defect_classifier.py` emette progressi piu' granulari: import dipendenze, split dataset, modello inizializzato, epoca, batch e validazione. Nessun cambio al contratto ONNX o alla logica decisionale.
- Invariante: cambia solo osservabilita'/diagnostica del training. VisionPro, trigger, scarto, contatori, ricette e shadow-mode AI restano invariati.

Nota 2026-07-10 - AI Fase 3b hardening, release 3.0.6.3:
- `SystemDiagnosticsView` espone direttamente i parametri macchina del trainer (`DefectClassifierTrainingPythonPath`, `DefectClassifierTrainingOutputDir`, `DefectClassifierTrainingEpochs`) e un comando `Test Python`. Non serve piu' editare manualmente XML per configurare una venv o una cartella modelli diversa.
- `DefectClassifierTrainingService.TrainAsync` riceve lo snapshot corrente dei binding UI: il training usa i valori visibili in pagina (resize, grayscale, normalizzazione, Python path, output dir, epoche) e non una rilettura potenzialmente vecchia da disco.
- Il pre-scan dataset gira fuori dal thread UI; lo stato viene pubblicato come `AI_TRAINING_PROGRESS` e gli eventi start/completed/failed/cancel/timeout entrano nell'audit operativo.
- Lo script Python supporta `--self-test` e produce il file ONNX in modo atomico (`.tmp` + `os.replace`) dopo validazione `onnx.checker`. Un crash/annullamento non deve lasciare un modello definitivo parziale.
- Invariante: Python e' richiesto solo per il training integrato. L'inferenza shadow-mode di un modello `.onnx` gia' presente resta gestita dalla HMI tramite ONNX Runtime e non richiede Python installato.

Nota 2026-07-10 - AI Fase 3b: training on-demand del classificatore, release 3.0.6.2:
- Nuovo `DefectClassifierTrainingService` (NON in ServiceLocator: istanziato dal ViewModel, il training e' un'operazione una-tantum su richiesta, non un servizio runtime). Orchestrazione del processo esterno Python `Scripts/AI/train_defect_classifier.py` con protocollo a righe `PROGRESS|`/`RESULT|{json}` su stdout.
- Confine architetturale: il training gira SEMPRE fuori processo (crash isolation per l'HMI 24/7) e il preprocessing di training arriva dalla config HMI (`DefectClassifierInput*`, mean/std) cosi' il modello e' coerente col contratto di `OnnxDefectClassifier` per costruzione — chi cambia il contratto (canali/normalizzazione/classi) deve aggiornare ENTRAMBI i lati.
- Principio "training offline supervisionato" mantenuto: il modello prodotto non viene mai abilitato automaticamente; la UI precompila percorso/versione/note e l'attivazione passa dal flusso Salva esistente (role-gated, validazione, reload a caldo).
- Invariante: i nuovi campi config `DefectClassifierTraining*` sono proprieta' macchina (Python path, cartella modelli, epoche), non per-ricetta.

Nota 2026-07-10 - ONNX TOP-only: resize modello chiarito, release 3.0.6.1:
- `SystemDiagnosticsView` espone il classificatore come `TOP shadow-mode`: la baseline corrente usa solo l'immagine TOP salvata (`*_T_A.bmp`, fallback `*_T_Z.jpg`).
- `DefectClassifierInputWidth` e `DefectClassifierInputHeight` sono dimensioni di resize del modello ONNX, non risoluzioni native delle camere. Il limite runtime e' `16..1024 px` per lato.
- Non esiste ancora un classificatore per-camera: Side/Left/Right/Bottom richiedono estensione esplicita con profili/modelli/dataset per ruolo camera.
- Invariante: nessun cambio a scarto, contatori, trigger o pairing VisionPro; ONNX resta advisory.

Nota 2026-07-09 - AI Fase 8 hardening, release 3.0.6.0:
- `SystemDiagnosticsView` localizza tutta la nuova UI AI/preallarmi tramite `ServerMessage` e JSON lingua; i testi hardcoded della sezione Fase 8 non sono piu' il contratto operativo.
- La UI espone in modo esplicito la dipendenza funzionale: `MachineHealthNotificationsEnabled` riceve segnali solo se SPC (`ProcessControlEnabled`) e/o manutenzione predittiva (`PredictiveMaintenanceEnabled`) sono attivi.
- `SystemDiagnosticsViewModel` valida i parametri AI prima del salvataggio; `MachineConfigurationService` applica comunque clamp lato config per proteggere `machine_runtime_config.xml` da valori fuori range.
- `OpcUaClientService.WriteEarlyWarningAsync` resta advisory ma ora e' single-flight, con timeout 5s e messaggi limitati a 1000 caratteri. Nodi OPC UA mancanti restano no-op sicuro.
- Invariante: nessuna modifica a ricette, DB, trigger, scarto o ciclo VisionPro; i preallarmi AI non comandano la macchina.

Nota 2026-07-09 - AI Fase 8: UI abilitazione + pannello preallarmi, release 3.0.5.9:
- `MachineHealthNotificationService` espone `GetRecentWarnings()` (ring buffer ultimi 100, snapshot difensivo, piu' recente per primo). `EarlyWarning` ha helper di sola lettura per il binding (SeverityText/CategoryText/DetectedAtLocalText/EtaText) — nessuna dipendenza WPF nel modello.
- `SystemDiagnosticsViewModel`: `EarlyWarnings` (ObservableCollection) + `RefreshEarlyWarningsCommand`; i flag/soglie Fase 8 si bindano a `AiSettings` (RuntimeBindings) e si salvano col flusso Controlli AI esistente.
- `SystemDiagnosticsView.xaml`: Controlli AI estesi (checkbox + 3 soglie) e nuova card preallarmi (Grid.Row 7) con severita' colorata.
- Invariante: la UI e' sola lettura/abilitazione sopra il servizio; rilevamento, aggregazione e canali non cambiano.

Nota 2026-07-09 - AI Fase 8: canale notifiche MES via OPC UA, release 3.0.5.8:
- Primo `INotificationChannel` esterno: `OpcUaNotificationChannel` (nel file `MachineHealthNotificationService.cs`), registrato nel costruttore accanto al canale locale. Risolve `OpcUaClientService` a runtime (nessuna dipendenza sull'ordine di init; riflette stato live enabled/connesso).
- `IOpcUaClientService.WriteEarlyWarningAsync(severity, code, message, etaValue, etaUnit, count)`: pubblica i nodi read-only `EarlyWarning*` + `EarlyWarningSequence` monotona. Guardato da IsEnabled/IsConnected; ogni singolo nodo pubblica solo se mappato (`WriteValue` no-op per chiavi assenti).
- Nodi di default `EarlyWarning*` in `OpcUaConfig.CreateDefaultNodes` (ClientToServer). Le config esistenti DB-backed vanno integrate dal pannello OPC UA.
- Invariante: l'aggregatore (`MachineHealthNotificationService`) e i rilevatori di deriva NON cambiano quando si aggiunge un canale; un nuovo canale = nuova implementazione `INotificationChannel` + `RegisterChannel`.

Nota 2026-07-09 - AI Fase 8: monitor derive + notifica preallarmi (fondazione), release 3.0.5.7:
- Nuovo `MachineHealthNotificationService` in `ServiceLocator` (singleton, `Start()` dopo ProcessControl/PredictiveMaintenance in `Initialize()`, `SafeDispose` in `Reset()`).
- Punto di aggancio: gli eventi `ProcessControlService.DriftDetected` e `PredictiveMaintenanceService.MaintenancePredicted` — prima privi di consumatori — sono ora aggregati qui. La logica di rilevamento nei due servizi NON cambia.
- Astrazione `INotificationChannel`: le implementazioni devono essere non bloccanti e degradation-safe. Fondazione: `LocalEventLogNotificationChannel` (Events Monitor) sempre attivo. I canali esterni (OPC UA estendendo `OpcUaClientService`; Email/SMTP con `System.Net.Mail`) si aggiungono via `RegisterChannel` senza toccare l'aggregatore.
- Modello dati canale-agnostico `EarlyWarning` (severita', categoria Inspection/PcHealth, stima al limite in pezzi/ore, causa a monte suggerita). Cadenza: critico immediato, resto in digest su timer.
- Config: `MachineHealthNotificationsEnabled` (+ digest/soglie) in `MachineRuntimeConfiguration`, marker in `MachineConfigurationService`, refresh a caldo in `SystemDiagnosticsViewModel.RefreshAiServicesConfiguration`.

Nota 2026-07-09 - Top3D Detection Sensitivity: localizzazione completa, release 3.0.5.6:
- La sezione `Detection sensitivity` del Job Tool Editor segue ora il contratto standard
  HMI: label, pulsanti e stati passano da `ServerMessageStructure`,
  `ServerMessagePersonalize` e dai JSON lingua aggiornati tramite
  `scripts/UpdateRuntimeLanguageFiles.ps1`.
- Nessuna modifica alla logica di acquisizione, trigger, validazione o scarto: e' una
  patch di completezza UI/localizzazione della release `3.0.5.5`.
- Invariante da mantenere: ogni nuovo testo operatore/tecnico aggiunto a XAML o ViewModel
  deve avere una chiave `Sub_entry_*` e deve essere aggiunto allo script lingua.

Nota 2026-07-09 - Top3D: Detection Sensitivity per-ricetta (branch, da collaudare), release 3.0.5.5:
- `IgigaCameraAccess`: nuove `ReadDetectionSensitivity`/`ApplyDetectionSensitivity` (feature double GigE col nome preso da Config `Top3DDetectionSensitivityFeature`). `ExposureAuto` ora impostata via `TrySetOptionalStringFeature`: feature assente = Debug, non Warn (teste 3D L38).
- Separazione dati: il VALORE detection sensitivity e' per-ricetta (`recipeParamTop3D.Top3DDetectionSensitivity`); il NOME feature GigE e' proprieta' hardware globale (Config). Persistenza ricetta via `AsyncRecipeParam.UpdateRecipeParamTop3DAsync`.
- UI: `JobToolEditorViewModel.IsTop3DPanelSelected` governa la visibilita' della sezione "Detection sensitivity" (Read/Apply), presente solo per il pannello Top3D. Apply = scrittura testa + salvataggio per-ricetta.
- Runtime: `MainWindow.ApplyTop3DDetectionSensitivityFromRecipe` applica il valore di ricetta alla testa 3D al load ricetta (accanto a trigger delay e stitching), guardato (solo se top e' "top3d" e valore > 0).
- Stato: branch `feature/top3d-detection-sensitivity`, non in master finche' non validato su macchina (nome feature GigE da confermare sull'hardware reale).

Nota 2026-07-08 - Fase 0: attese bloccanti ridotte in Live Preview e diagnostica, release 3.0.5.4:
- `JobToolEditorViewModel` non usa piu' `Thread.Sleep` nel wait loop della Live Preview:
  il polling del completamento frame VisionPro resta fuori dal thread UI e ora usa
  `Task.Delay(..., ct)` con lo stesso timeout cancellabile.
- `SystemDiagnosticsService` non blocca piu' il refresh per il priming del
  PerformanceCounter CPU; il primo snapshot puo' mostrare `0%`, poi il valore si
  stabilizza dal refresh successivo.
- Restano volutamente fuori dal batch i tre `Thread.Sleep` di `ICognexJobManager`,
  perche' sono nei percorsi start/stop job Cognex e vanno sostituiti con test macchina
  dedicato. Nessuna modifica a trigger, scarto, ricette, DB o mapping I/O.

Nota 2026-07-08 - Top3D display resolver per path VisionPro annidati, release 3.0.5.3:
- `CameraDisplayManager` risolve i record immagine di produzione con un resolver robusto:
  chiave esatta, path annidato, path senza prefisso wrapper/toolblock e ricerca ricorsiva
  nel record `LastRun`.
- Invariante Top3D: i path ricetta possono essere salvati come path QuickBuild completi
  (`Measure.Height.InputImage`) oppure come path relativi al LastRun (`Height.InputImage`).
  Il display HMI deve accettare entrambi senza cambiare ricetta o job VisionPro.
- `GetCurrentTopCameraImage()` non deve usare indicizzazione posizionale (`SubRecords[0]`)
  per i record Top3D: il layout del record 3D non e' garantito uguale alle camere 2D.
- Questa logica riguarda solo la selezione del record da visualizzare; validazione,
  contatori, scarto e salvataggio DB restano invariati.

Nota 2026-07-07 - Fase 0: chiuso GC.Collect residuo, release 3.0.5.2:
- `RecipeManagerViewModel.ReleaseImageLock` non forza piu' il GC: verificato che tutte le immagini ricetta sono caricate esclusivamente in-memory via `RecipeImagePathConverter` (`ReadAllBytes` → `MemoryStream` → `BitmapCacheOption.OnLoad` → `Freeze`) e dal preload di `App.xaml.cs` con lo stesso pattern — la UI non trattiene mai handle sui file immagine.
- Invariante da mantenere: qualsiasi nuovo caricamento di immagini ricetta DEVE passare dal converter (o replicare il pattern in-memory); un `new BitmapImage(Uri)` diretto reintrodurrebbe il file-lock WPF.
- Metrica Fase 0 "0 GC.Collect su percorsi runtime" raggiunta (resta solo il GC di shutdown, deliberato).

Nota 2026-07-07 - Fase 0: async command wrappers e piano flaggato, release 3.0.5.1:
- I command handler non-event-handler piu' evidenti sono stati portati al pattern
  wrapper `void` + core `async Task` con `SafeFireAndForget()`: `MainViewModel`
  (Start/Stop/Restart/Navigate), `EjectionAlarmCardViewModel`, `JobToolEditorViewModel`
  (Live View) e `DbQuatis`.
- Il criterio resta conservativo: event handler WPF, timer e callback Cognex/SDK
  non vengono convertiti in blocco; ogni punto va valutato in base al thread
  chiamante, al try/catch presente e al rischio reale sul runtime macchina.
- `Documentation/improvement-plan-2026-07.md` ora contiene lo stato Fase 0 con
  punti completati, parziali e aperti. Restano da trattare in batch dedicati:
  lifecycle VisionPro/Cognex, code risultati ispezione senza drop silenzioso e
  rimozione del `GC.Collect()` residuo dopo fix file-lock immagini WPF.

Nota 2026-07-07 - Fase 0: salvataggio immagini con slot pre-capture, release 3.0.5.0:
- `SaveImage`: la coda immagini bounded resta a 32 elementi, ma ora uno slot non bloccante viene prenotato prima di creare `Bitmap`/`byte[]` per JPEG/BMP. Se la coda e' piena, il drop best-effort avviene subito con `IMAGE_QUEUE_FULL|stage=pre-capture`.
- Il writer rilascia lo slot solo dopo la scrittura su disco o l'errore di scrittura; il limite copre quindi sia gli elementi accodati sia quello eventualmente in scrittura.
- Il salvataggio immagini resta best-effort e campionato: nessun risultato ispezione viene scartato da questa logica. Le code `CameraResult` rimangono fuori da questa modifica e saranno trattate solo con una strategia senza drop silenzioso.
- Shutdown: se il writer immagini non termina entro il timeout, il dispose non libera risorse sotto un thread ancora attivo e logga `IMAGE_QUEUE_SHUTDOWN_TIMEOUT`.

Nota 2026-07-07 - Fase 0: code bounded per immagini e diagnostica, release 3.0.4.9:
- `SaveImage`: coda scrittura immagini `BlockingCollection` limitata (32) con `TryAdd` non bloccante; a coda piena drop esplicito con contatore e warning `IMAGE_QUEUE_FULL`. Il produttore (thread UI) non viene mai bloccato.
- `ApplicationEventLogger`: pattern producer/consumer bounded per il mirror DB eventi — coda limitata (500) + unico drainer `LongRunning` al posto di un `Task.Run` per evento; drop esplicito throttled `EVENTLOG_DB_QUEUE_FULL` (l'evento resta nei file NLog). Ora `IDisposable` con drain finale (3s) e `SafeDispose` in `ServiceLocator.Reset()`.
- Politica code (emendamento revisione): drop esplicito e tracciato SOLO su dati best-effort; le code risultati ispezione di MainWindow restano invariate fino al batch Cognex/VisionPro (nessun drop silenzioso di ispezioni).

Nota 2026-07-07 - Fase 0 stabilita' 24/7 (primo batch), release 3.0.4.8:
- Pattern consolidato per gli `async void` non-event-handler: wrapper `void` che delega a un core `async Task` via `SafeFireAndForget()` (risolto dal `TaskExtensions` nel namespace radice `QtisVisionPanel` — logga sempre l'eccezione col nome del chiamante). Applicato a StatisticsView, NavigationViewModel, RecipeManagerViewModel; `Cls_CheckUser.InitializeUserAsync` e' ora `async Task` attesa dal chiamante.
- `AsyncConfigManagerXml`: acquisizione lock load/save con timeout 30s (`CONFIG_LOCK_TIMEOUT` + `TimeoutException`); l'auto-heal degrada senza propagare.
- `Cls_InitializzeDb`: rimosso il metodo morto `Initialize1()`; `CommandTimeout` espliciti su init (60s) e ALTER (120s).
- GC forzati rimossi dai percorsi ricetta (AsyncRecipeParam non trattiene handle); il GC di `ReleaseImageLock` resta finche' il caricamento immagini ricetta non passa a `BitmapCacheOption.OnLoad`+`Freeze` (Fase 1).

Nota 2026-07-07 - Snapshot encoder sull'edge fotocellula, release 3.0.4.7:
- `IOEvent` trasporta lo snapshot dei contatori encoder letto dal polling I/O nello stesso ciclo del fronte input.
- `AdvantechDeviceManager.PollingLoopAsync` legge prima gli encoder e poi gli input; il fronte fotocellula non dipende piu' da una lettura successiva del count dopo il dispatcher UI.
- `DigitalIOViewModel.OnInputChanged` risolve subito il main encoder count dallo snapshot evento e lo passa al core UI. La nascita del prodotto (`PRODUCT_ZERO`) resta compatibile con il flusso esistente ma non eredita un encoder gia' avanzato dal primo prodotto.
- Il log fotocellula include `snapshotAge`; insieme a `late=... mm` dei trigger camera permette di capire se il problema residuo e' polling, distanza camera o necessita' di latch hardware.

Nota 2026-07-06 - Diagnostica ritardo reale trigger encoder camera, release 3.0.4.6:
- `MachineController.UpdateEncoderPosition` ora salva sul singolo `ProductInterventionState` il target encoder, l'encoder reale ricevuto quando il punto viene processato, il ritardo in count e il ritardo convertito in mm.
- `DigitalIOViewModel.ExecuteInterventionPointAsync` riporta queste misure nel log `TRACK` prima del pulse fisico. Oltre 5 mm il trigger camera viene loggato come warning diagnostico.
- Il comportamento macchina non cambia: quote intervento, pulse, mapping I/O e trigger mode restano quelli configurati. La misura serve a distinguere ritardo software/polling da errore di quota o problema VisionPro.
- Nota commissioning: quote camera molto vicine, ad esempio TOP/SIDE separate da circa 12 mm, a velocita' linea alte lasciano pochi millisecondi tra due trigger. Se il ritardo reale e' superiore alla separazione fisica, usare quote coincidenti quando lo scatto deve essere simultaneo, correggere la distanza macchina reale o valutare un trigger hardware deterministico.

Nota 2026-07-06 - Trigger encoder/IO non bloccati dalla UI, release 3.0.4.5:
- `DigitalIOViewModel` non usa piu' `Dispatcher.Invoke()` nel percorso caldo encoder -> intervento -> pulse camera. Gli aggiornamenti UI di conteggio encoder, prodotti tracciati, eventi intervento, eventi macchina e log pannello vengono accodati con `BeginInvoke`.
- Il principio architetturale da mantenere e': prima il comando fisico/real-time, poi la diagnostica visuale. La UI non deve mai essere una barriera sincrona tra callback encoder e uscita camera/reject.
- La modifica non cambia configurazioni macchina, quote, mapping IO o ricette; riduce solo il jitter introdotto dalla UI quando passano prodotti ravvicinati o quando VisionPro/log/salvataggio immagini impegnano il pannello.

Nota 2026-07-06 - Controlli AI: validazione ONNX e reload immediato, release 3.0.4.4:
- `OnnxDefectClassifier` espone `RefreshConfiguration()`: dispone la sessione ONNX corrente, rilegge `MachineRuntimeBindings.DefectClassifier*` e prova a caricare il nuovo modello senza riavviare la HMI.
- `SystemDiagnosticsViewModel.SaveAiSettings` forza l'aggiornamento dei binding WPF prima di salvare e valida il classificatore quando e' abilitato: percorso non vuoto, file esistente, estensione `.onnx`, dimensioni input > 0 e normalizzazione con dev.std non zero.
- Il pannello `Controlli AI` resta advisory e role-gated: solo Installer/Administrator possono salvare; il modello ONNX non entra nel percorso decisionale di scarto.
- Nuova guida `Documentation/AI/ONNX_Defect_Classifier_Guide.md`: contratto modello `NCHW`, classe `0 = OK`, classi `>0 = NOK`, dataset da `label.json`, export e collaudo.

Nota 2026-07-03 - Pannello "Controlli AI" in PC Diagnostics, release 3.0.4.3:
- `SystemDiagnosticsViewModel` espone `AiSettings` (l'oggetto `MachineRuntimeBindings` caricato da `MachineConfigurationService`) direttamente al binding della vista: nessuna duplicazione delle ~19 proprieta' AI nel VM. `SaveAiSettings` = gate ruolo (Installer/Administrator, riuso di `CanApplyTimingParameters`) → conferma dialog → `MachineConfigurationService.Save` (atomico, clamp) → `RefreshConfiguration()` su tutti i servizi AI (MeasurementCapture, ProcessControl, PredictiveMaintenance, TrainingDataCollection, IoTimingOptimizer, AiPerformanceMonitor, RecipeProductAdvisor) → evento `AI_SETTINGS_APPLY` → reload per mostrare i valori post-clamp.
- Nota storica 3.0.4.3: il classificatore ONNX richiedeva riavvio per cambiare modello/preprocessing; limite superato in 3.0.4.4 con `OnnxDefectClassifier.RefreshConfiguration()`.
- `SystemDiagnosticsView.xaml`: nuova riga (Grid.Row 6) con la sezione a tre colonne (Servizi / Classificatore ONNX / Retention); l'intera area editor e' `IsEnabled={Binding CanApplyTimingParameters}` per i ruoli non autorizzati.

Nota 2026-07-03 - Sicurezza e audit OPC UA / credenziali, release 3.0.4.2:
- `OpcUaClientService` separa ora "OPC UA usato per scambio dati" da "OPC UA autorizzato a comandare la macchina": i flag `RemoteCommandsEnabled`, `RemoteStartStopEnabled` e `RemoteRecipeChangeEnabled` governano Start/Stop/cambio ricetta remoti senza cambiare il flusso HMI.
- I comandi OPC UA disabilitati vengono respinti con ACK negativo (`CommandAckOk=false`) e audit `OPCUA_COMMAND_REJECTED_BY_POLICY`; i comandi accettati restano loggati come `OPCUA_COMMAND_RECEIVED`.
- `OpcUaConfig.AutoAcceptUntrustedCertificates` parte da `false` per nuove configurazioni. I valori gia' salvati in XML/DB restano rispettati per compatibilita' di campo.
- `Cls_CheckUser` non ripopola piu' utenti default se esiste gia' almeno un utente interattivo; credenziali default presenti o usate vengono loggate come warning/audit, senza bloccare login o avvio.
- Confine di sicurezza: warning MySQL root/root, TLS disabilitato verso host non locale e credenziali default sono diagnostica/audit non bloccante. La decisione di fermare la macchina resta nel runtime macchina/config esistente, non nel nuovo audit.

Nota 2026-07-03 - MultiShot: soglia "stesso-prodotto" separata dal timeout, release 3.0.4.1:
- `InspectionOrchestrator` riceve un nuovo delegato `Func<double> getCompanionSameProductLagMs` (12° parametro del costruttore): la soglia "stesso-prodotto" e' ora un valore FISICO iniettato, distinto dal timeout di attesa risultato (in 3.0.4.0 erano fusi in `max(5000, timeout)`).
- MainWindow fornisce il valore: single-shot 5s (costante), MultiShot da `MachineRuntimeBindings.MultiShotCompanionMaxLagMs` (default 15s), cachato in `_multiShotCompanionMaxLagMs` durante `RefreshMultiShotConfiguredState` (nessuna lettura config per-prodotto).
- `MaxCompanionLagMs` (5s) nell'orchestrator resta solo come floor/fallback single-shot.

Nota 2026-07-03 - Pacchetto affidabilita' runtime, release 3.0.4.0:
- `InspectionOrchestrator`: la soglia di accettazione companion non e' piu' una costante fissa (5s) ma `max(5000ms, timeout di gruppo)`, cosi' la finestra di accettazione coincide con quella di attesa (fix del companion MultiShot lento marcato `missing`).
- `OnnxDefectClassifier`: `RunShadowComparison` ora esegue l'inferenza off-thread (`Task.Run` + `SafeFireAndForget`) con single-flight gate (`SemaphoreSlim`), togliendo il carico dal thread real-time di ispezione.
- `CounterManager.Dispose`: da `async void` a sincrono con flush finale atteso a timeout + dispose del flusher.
- `MachineConfigurationService.Save`: scrittura atomica (temp file + `File.Replace`) sotto lock.

Nota 2026-07-03 - AI performance monitor e advisor prodotto/ricetta, release 3.0.3.9:
- Nuovo `AiPerformanceMonitorService` registrato in `ServiceLocator` come `AiPerformanceMonitor`. Osserva i tempi gia' disponibili nel ciclo `MainWindow.ProcessInspectionGroupAsync`: display camere, validazione, contatori/allarmi/OPC UA, salvataggio decisione, advisory AI e queue DB.
- Il monitor produce solo `AI_PERFORMANCE_ADVISORY` su finestra mobile con cooldown; non modifica timeout, trigger, scarto, contatori o configurazioni.
- Nuovo `RecipeProductAdvisorService` registrato in `ServiceLocator` come `RecipeProductAdvisor`. Osserva esiti, difetti e misure fallite della ricetta corrente per rilevare pattern compatibili con formato/ricetta errata.
- L'advisor produce `AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED` quando vede fail consecutivi o fail-rate alto con difetto dominante. Se disponibile un'immagine TOP, puo' cercare in background una ricetta candidata riusando `AdvancedRecipeAutoSwitcher`, ma logga solo `AI_RECIPE_CANDIDATE_FOUND`: nessun caricamento automatico.
- Nuovi flag macchina, entrambi default `false`: `AiPerformanceMonitorEnabled`, `RecipeProductAdvisorEnabled`.
- Confine di sicurezza: Fase 6/7 sono advisory in-memory. Il runtime autorevole resta VisionPro + logica esistente per OK/NOK, scarto, OPC UA, allarmi e ricette.

Nota 2026-07-02 - Hardening AI advisory e retention dati, release 3.0.3.8:
- Le funzioni AI restano additive e non intervengono su esito, scarto, contatori, pairing VisionPro o uscite fisiche.
- I default delle nuove configurazioni sono ora conservativi: `DataFoundationCaptureEnabled`, `ProcessControlEnabled`, `PredictiveMaintenanceEnabled`, `TrainingDataCollectionEnabled` e `IoTimingOptimizerEnabled` partono da `false`. I file macchina esistenti mantengono il valore gia' salvato.
- In caso di errore lettura `machine_runtime_config.xml`, i servizi AI tornano disabilitati invece di auto-abilitarsi.
- Nuovo helper interno `AiDataRepositorySupport`: connessione MySQL condivisa per repository AI, warning throttled ogni 10 minuti per errori DB/config, retention tentata al massimo ogni 12 ore.
- Nuovi parametri retention macchina: `AiInspectionMeasurementRetentionDays` (default 180), `AiHealthSnapshotRetentionDays` (default 90), `AiTrainingSampleRetentionDays` (default 0 = conserva).
- `IoTimingOptimizer.ApplyTimingParameter` crea un backup reale del file macchina prima del salvataggio e segnala che il valore diventa effettivo dopo refresh/reload runtime previsto dalla pagina configurazione.
- `ProcessControlService` calcola media/sigma sulla finestra precedente al campione corrente, evitando che il punto appena misurato sposti subito i limiti usati per valutarlo.
- `TrainingDataCollectionService` serializza `label.json` con Newtonsoft.Json e include `labelSource` + versione software; `OnnxDefectClassifier` aggiunge metadati modello e logga in `tbllogevent` solo i disaccordi shadow-mode.

Nota 2026-07-02 - UI conferma Fase 5 in SystemDiagnostics, release 3.0.3.6:
- `SystemDiagnosticsViewModel` esteso con la sezione timing optimizer: `TimingAdvisories`, `TimingParameters`, `CanApplyTimingParameters` (gate ruolo Installer/Administrator via `UserSession.CurrentRole`), `RefreshTimingCommand`, `ApplyTimingParameterCommand`. L'apply passa da conferma `SystemNotificationWindow` e chiama `ServiceLocator.IoTimingOptimizer.ApplyTimingParameter` (guardrail: whitelist+range+backup+audit). Nuove classi di supporto `TimingAdvisoryItem`, `TimingParameterItem`.
- `SystemDiagnosticsView.xaml`: nuova riga (Grid.Row 5) con la sezione "Ottimizzazione timing IO/encoder (Fase 5)" — raccomandazioni advisory a sinistra, parametri applicabili (valore corrente/range/target/Applica) a destra; il bottone Applica e' abilitato solo se il ruolo e' autorizzato.
- Nessuna nuova dipendenza. La collocazione in SystemDiagnostics evita di toccare il God-class Digital I/O.

Nota 2026-07-02 - Ottimizzatore timing IO/encoder (Fase 5), release 3.0.3.5:
- Nuovo servizio `IoTimingOptimizerService` registrato in `ServiceLocator` come `IoTimingOptimizer` (singleton double-check), creato in `Initialize()` e ripulito in `Reset()`. Solo stato in-memory.
- Alimentato da `MainWindow.ProcessInspectionGroupAsync` con una sola riga (`ObserveInspectionTiming(topResult, companions, missingRoles)`): riusa il punto hook AI esistente e i `CameraResult.EnqueuedAtUtc` gia' disponibili — NESSUNA modifica a `InspectionOrchestrator`/`MultiShotTriggerController` (codice real-time intatto).
- Analisi per ruolo camera (scala col numero di camere): finestra mobile del lag risultato companion vs TOP, percentili/jitter, missing-rate; advisory sul margine rispetto ai timeout risultato (costanti di codice in MainWindow, non applicabili).
- Confine di sicurezza: advisory automatiche (`IO_TIMING_ADVISORY` su `tbllogevent`); l'applicazione dei parametri trigger di `RuntimeBindings` avviene SOLO via `ApplyTimingParameter(param, value, actor)` con whitelist, range di sicurezza, backup del valore precedente e audit (`IO_TIMING_APPLY`). Mai automatica.
- Flag `MachineRuntimeBindings.IoTimingOptimizerEnabled` (default `true`).
- Da completare: UI di conferma operatore che invoca `ApplyTimingParameter` (il metodo e i guardrail sono pronti).

Nota 2026-07-02 - Classificatore difetti ONNX shadow-mode (Fase 3), release 3.0.3.4:
- Nuova interfaccia `DataManage/IDefectClassifier` + implementazione `Services/OnnxDefectClassifier` (ONNX Runtime), registrata in `ServiceLocator` come `DefectClassifier` (singleton, `IDisposable`).
- Nuova dipendenza `Microsoft.ML.OnnxRuntime` (PackageReference, DLL nativa x64 copiata in output).
- `OnnxDefectClassifier` carica il modello dal percorso di config (se `DefectClassifierEnabled` e file presenti), altrimenti resta no-op. `RunShadowComparison(InspectionResult, pieceFolder)` localizza l'immagine TOP salvata, la classifica e logga l'accordo/disaccordo con l'esito a regole come advisory (`VISION_ML_SHADOW`), senza incidere su ispezione/scarto.
- Invocato da `MainWindow.ProcessInspectionGroupAsync` con una sola riga dopo la cattura training.
- Preprocessing configurabile (dimensione, grayscale, normalizzazione mean/std). Assunzioni scaffold: layout NCHW, classe 0 = OK — da riadattare al modello reale.
- Flag e parametri in `MachineRuntimeBindings.DefectClassifier*` (default: disabilitato).

Nota 2026-07-02 - Raccolta dati etichettati per training vision (Fase 3a), release 3.0.3.3:
- Nuovo servizio `TrainingDataCollectionService` registrato in `ServiceLocator` (singleton double-check), creato in `Initialize()` e ripulito in `Reset()`.
- Invocato da `MainWindow.ProcessInspectionGroupAsync` con una sola riga, dopo il blocco di salvataggio immagini: cattura un campione etichettato SOLO se `_produzioneRecord.PieceData` e' valorizzato (immagini del pezzo effettivamente salvate).
- Scrive due cose fire-and-forget: un sidecar `label.json` dentro la cartella `Piece_XXXXXXXX` (dataset auto-descrittivo e portabile per training offline) e una riga indice in `tbl_training_samples` via `TrainingSampleRepository`.
- L'etichetta e' derivata da `InspectionResult` (IsValid -> OK/NOK, DetectedDefects -> elenco difetti). JSON del sidecar costruito a mano (nessuna dipendenza di serializzazione).
- NON addestra nulla: prepara solo il dataset per la futura Fase 3 (classificatore ONNX). Flag `MachineRuntimeBindings.TrainingDataCollectionEnabled` (default `true`).

Nota 2026-07-02 - Manutenzione predittiva (Fase 2), release 3.0.3.2:
- Nuovo servizio `PredictiveMaintenanceService` registrato in `ServiceLocator` (singleton double-check), creato in `Initialize()` e ripulito in `Reset()`. Non `IDisposable` (solo stato in-memory).
- Alimentato in-memory da `HealthSnapshotService` (che dopo aver costruito lo snapshot chiama `PredictiveMaintenanceService.Observe`) — nessuna lettura DB.
- Statistica su finestre mobili (~12h a 60s/campione) di CPU/RAM/disco: proiezione lineare del riempimento disco verso il 95% (ore/giorni stimati, avvisa solo entro orizzonte ~14gg), EWMA per CPU (>85%) e RAM (>90%) sostenute.
- Avvisi ADVISORY: `MaintenancePredicted` (evento) + `GetActiveAlerts()` + log Warning su `tbllogevent` (`PREDICTIVE_MAINTENANCE`). Non attivano il badge errori.
- Flag `MachineRuntimeBindings.PredictiveMaintenanceEnabled` (default `true`).

Nota 2026-07-02 - SPC e rilevamento deriva (Fase 1), release 3.0.3.1:
- Nuovo servizio `ProcessControlService` registrato in `ServiceLocator` (singleton double-check), creato in `Initialize()` e ripulito in `Reset()`. Non e' `IDisposable` (solo stato in-memory, nessun timer).
- Riceve le misure in-memory da `MeasurementCaptureService.CaptureInspection` (che dopo aver costruito la lista chiama `ProcessControlService.ObserveBatch`) — nessuna lettura DB nel percorso caldo dell'ispezione.
- Statistica SPC per chiave (ricetta|camera|feature): finestra mobile (60 campioni, min 15), limiti I-MR (media +/- 3 sigma da moving range/1.128), regole di Nelson (1 oltre 3 sigma, 2 di 3 oltre 2 sigma, 8 punti stesso lato, 6 monotoni), carta EWMA (lambda 0.2), stima "pezzi al limite" via pendenza lineare. Debounce per regola e recupero dopo 8 punti in-control.
- Le derive sono ADVISORY: `DriftDetected` (evento) + `GetActiveDrifts()` (snapshot per futura UI) + log su `tbllogevent` a livello Warning via `ApplicationEventLogger` (compaiono nell'Events Monitor, non attivano il badge errori).
- Flag `MachineRuntimeBindings.ProcessControlEnabled` (default `true`).

Nota 2026-07-02 - Fondazione dati AI (Fase 0), release 3.0.3.0:
- Due nuovi servizi registrati in `ServiceLocator` (singleton double-check): `MeasurementCaptureService` e `HealthSnapshotService`. Entrambi creati/avviati in `ServiceLocator.Initialize()` e ripuliti in `Reset()`.
- `MeasurementCaptureService` estrae dai `ValidationResult` di un'ispezione (dizionario `Measurements`) i valori misurati per feature/camera e li persiste in modo non bloccante via `InspectionMeasurementRepository` → tabella `tbl_inspection_measurements`. E' invocato con una singola riga in `MainWindow.ProcessInspectionGroupAsync`, dopo `RegisterProcessedInspectionPair()`, senza alterare il flusso ispezione/scarto/contatori.
- `HealthSnapshotService` legge periodicamente (60s, primo tick +45s) le metriche di `SystemDiagnosticsService` (CPU/RAM/disco) e le persiste via `HealthSnapshotRepository` → tabella `tbl_health_snapshots`. Implementa `IDisposable` (timer).
- I due repository seguono il pattern di `EventLogRepository`: gate a semaforo, coppia async/sync, connection-string per chiamata, degradazione silenziosa quando MySQL e' assente.
- Nuovo flag di config `MachineRuntimeBindings.DataFoundationCaptureEnabled` (default `true`) abilita/disabilita l'intera cattura; letto una volta all'avvio dei servizi.
- Schema DB esteso in `Cls_InitializzeDb.Initialize()` con `CreateInspectionMeasurementsTable` e `CreateHealthSnapshotsTable` (`CREATE TABLE IF NOT EXISTS`).

Nota 2026-06-22 - Consolidamento Claude 3.0.1.1:
- `DataInspectorViewModel` espone comandi di zoom e download evidenza per immagini source/processed: l'operatore puo aprire un overlay fullscreen, usare rotella mouse/pulsanti zoom e creare un pacchetto con immagini + `detail.txt`.
- `DataInspectorView.xaml` contiene l'overlay zoom e il pulsante `Scarica evidenza` nel pannello storage; il code-behind gestisce solo l'input wheel per lo zoom, lasciando la logica nel ViewModel.
- `CameraContainer.xaml.cs` mantiene `_hasCompletedInitialLoad`: il rebuild automatico delle `CameraViews` avviene solo al primo `Loaded`; ai ritorni da navigazione viene aggiornato il layout senza perdere gli ultimi `CogRecordDisplay`.
- Il refresh completo dai job VisionPro resta esplicito e viene richiamato da `MainWindow` dopo inizializzazione VisionPro o cambio ricetta.

Nota 2026-06-18 - Consolidamento Claude 3.0.1.0:
- `InspectionOrchestrator` pubblica l'evento `CompanionTimeoutOccurred` quando una camera companion prevista non arriva entro timeout; `MainWindow` lo traduce in popup warning non modale con auto-close e rate limit.
- `CounterManager` notifica `CountersUpdated` tramite `Dispatcher.BeginInvoke`, evitando il blocco del thread orchestrator su aggiornamenti UI sincroni durante burst di ispezioni.
- `TopMenuBarViewModel` ascolta `CountersUpdated` e aggiorna subito produzione/qualita, oltre al timer periodico.
- `StatisticsView` e `InspectionCounterViewModel` ripristinano le subscription dopo ogni ciclo `Loaded` / `Unloaded`, cosi i contatori Overview restano live dopo navigazione.
- `CounterData.Key` separa la chiave logica del contatore dal testo visualizzato/localizzato, rendendo robusto l'aggiornamento di `Total`, `Good` e `No Good`.

Nota 2026-06-17 - Consolidamento Claude 3.0.0.9:
- `InspectionOrchestrator` e' ora parte della baseline corrente: gestisce il pairing multi-camera con timestamp enqueue, timeout companion e guardia contro mismatch cross-product.
- `CameraContainer` e' cache singleton lato `ViewFactoryService` e si riallinea ai job VPP runtime quando la vista torna visibile.
- La diagnostica `MULTISHOT_FRAME_ACK` non e' piu collegata al job VisionPro per evitare accessi COM fuori thread STA; la diagnostica operativa usa `COMPANION_*`, `CAMERA_TIMING` e i log MultiShot encoder.
- `PowerFlex525EtherNetIpClient` tiene i frame raw EtherNet/IP a livello Debug e limita il log di monitor completato per ridurre rumore in produzione.

Nota 2026-06-15 — Feature: storico parametri MultiShot:
- `MultiShotProfileSnapshot` (new class, `Models/MultiShotTrigger/MultiShotProfileSnapshot.cs`): POCO con tutti i parametri editor MultiShot come stringhe; usato da `DigitalIOViewModel.CurrentMultiShotPreviousSnapshot` per il binding XAML "Prec.: X".
- `MultiShotParamHistoryRepository` (new class, `Database/MultiShotParamHistoryRepository.cs`): tabella `tbl_multishot_param_history` (PK composite `profile`+`param_name`), UPSERT al salvataggio, SELECT all al caricamento.
- `DigitalIOViewModel`: aggiunta property pubblica `CurrentMultiShotPreviousSnapshot` (sostituita come oggetto intero, nessun INPC interno). Cambia profilo → `OnPropertyChanged(nameof(CurrentMultiShotPreviousSnapshot))`. Salvataggio → snapshot vecchio config a DB (fire-and-forget) e aggiornamento in-memory. Caricamento → load da DB (fire-and-forget). Failure DB non blocca il flusso XML.
- `DigitalIOControl.xaml`: ogni campo editabile MultiShot avvolto in StackPanel con TextBlock "Prec.: X" sotto, visibile solo quando `HasValues = true`.

Nota 2026-06-15 — Fix encoder + salvataggio immagini Rear/Bottom:
- `AdvantechDeviceManager.ReadEncoderAsync`: rimossa logica `_encoderReadAsDeltaMode`. Il ritorno a `count=0` dalla scheda durante lo startup di VisionPro (transiente ~12s) ora preserva l'ultimo valore noto invece di attivare l'accumulo cumulativo — causa del fenomeno "250 m/min all'avvio". Il flag e la modalità delta non esistono più.
- `RearCameraView` e `BottomCameraView`: aggiunta property pubblica `ImageDisplay` (tipo `Cognex.VisionPro.CogRecordDisplay`) che espone `CogRecordsDisplay1`. MainWindow usa `.ImageDisplay` al posto dell'accesso diretto al campo generato da XAML.
- `CameraDisplayManager.UpdateDisplayParallelAsync`: aggiunti gli assignment `MainWindow._rearDisplaySaveRecord = tmpRecord` e `MainWindow._bottomDisplaySaveRecord = tmpRecord` nei branch "rear" e "bottom". In precedenza questi record restavano sempre `null` — `ISaveImage.GetSavedRecord("R"/"B")` cadeva sempre sul record raw invece del display record.
- `ISaveImage.GetSavedRecord` e `TryGetImageFromToolBlock`: aggiunti casi "R" e "B" per supportare il salvataggio immagini sulle viste Rear e Bottom.
- `MainWindow.SyncInspectionRecordsForSave` e `ClearPendingVisionResults`: sincronizzazione e pulizia di `_rearSaveRecord`, `_bottomSaveRecord`, `_rearDisplaySaveRecord`, `_bottomDisplaySaveRecord` allineate agli altri ruoli camera.

Nota di consolidamento finale 2026-06-11 (F2-A + F3-A + F3-B):
- `Services/InspectionOrchestrator.cs` estratto da MainWindow: gestisce le code multi-camera con pattern delegate-injection; `ProcessQueuedPairAsync` in MainWindow delega con una singola riga
- OPC UA: 3 nodi read-only aggiunti (`SystemHealthStatus`, `LastAlarmCode`, `ActiveHoldReasons`) tramite `WriteHealthSnapshotAsync` chiamato ogni ciclo ispezione
- Validazione ricetta pre-apply OPC UA: `ValidateRecipeForMachine()` controlla compatibilità job caricati vs feature abilitate in ricetta; `CommandAckOk=false` se ricetta richiede camera non presente

Nota di consolidamento 2026-06-11:
- `MainWindow._isContinuousRunActive` (public static field) convertito a property che delega a `MachineRuntimeService.IsContinuousRunActive` — race condition C3 chiusa
- `Models/CameraResult.cs` estratto da class privata annidata in MainWindow a `internal sealed class` standalone — prerequisito per `InspectionOrchestrator`
- `AuthorizationService.InvalidateCache()` aggiunto come metodo pubblico; viene invocato da `TopMenuBarViewModel` dopo login e logout — cache permessi ora invalidata immediatamente invece di aspettare il timeout da 5 minuti
- Audit trail completo su azioni operative: `RECIPE_SAVE`, `RECIPE_LOAD_PRODUCTION`, `RECIPE_DELETE`, `COUNTER_RESET_ALL`, `COUNTER_RESET_SHIFT`, `ALARM_CARD_SAVE`, `MACHINE_CONFIG_SAVE`
- 20+ metodi `async void` nei ViewModel convertiti a `async Task` + `.SafeFireAndForget()`

Nota di integrazione 2026-04-27:
- la baseline `lastRelease` include ora il primo sottosistema commissioning completo derivato dal tool standalone I/O/encoder
- il blocco non e' piu' esterno al full project: modelli runtime, controller macchina, localizzazione tool, template config e `DigitalIOControl` fanno parte della baseline condivisa
- la sorgente di verita' per la configurazione commissioning resta il file `machine_runtime_config.xml` derivato dal template `ConfigurationTemplates\machine_runtime_config.template.xml`
- il runtime macchina continua a usare il conteggio encoder cumulativo come base per quota prodotto/trigger/reject; la velocita' e' una grandezza derivata da `deltaCount / deltaTime`
- la diagnostica raw del segnale encoder resta un supporto commissioning e non sostituisce il modello macchina basato su count cumulativo

Nota di bootstrap 2026-04-14:
- `CameraDisplayManager` viene inizializzato in modo lazy da `MainWindow` perche' il refresh runtime di ricetta/visione puo' partire prima che `Window_Loaded` completi la costruzione degli helper UI.
- Questo evita `NullReferenceException` nei percorsi di startup che ricostruiscono la mappa feature supportate prima del completamento del caricamento finestra.

---

## Stack Tecnico

| Layer | Tecnologia | Note |
|---|---|---|
| Runtime | .NET Framework 4.8, WPF, x64 | AllowUnsafeBlocks per image processing |
| UI Pattern | MVVM (parziale) | 19 ViewModel, MVVM bypassato in MainWindow |
| Vision | Cognex VisionPro | CogJobManager, CogToolBlock, CogRecordDisplay |
| Camera | DALSA GigE | IgigaCameraAccess, FIFO hardware trigger |
| I/O | Advantech BDaq4 | AdvantechDeviceManager, IIODeviceManager, Pcie1756ChannelMap (32 DI / 32 DO) |
| Database | MySQL 8 (MySql.Data 9.5.0) | Raw SQL, SemaphoreSlim gating |
| Config | XML + MySQL snapshot | Atomic write + recovery fallback |
| Logging | NLog 6.0.6 | ApplicationEventLogger, NLogEventBridge |
| UI Styles | MaterialDesignThemes 5.3.0 | MVVM Light 5.4.1.1 |

---

## Struttura Cartelle e Dimensioni

| Cartella | File principali | Righe stimate |
|---|---|---|
| `/` (root) | MainWindow.xaml.cs, App.xaml.cs, ServiceLocator.cs | ~4.200 |
| `ViewModels/` | 19 classi ViewModel | ~10.731 |
| `Services/` | 15 classi servizio | ~5.240 |
| `Database/` | 9 classi repository | ~4.602 |
| `Cls_Vpro/` | CognexJobManager, IgigaCameraAccess | ~2.671 |
| `DataManage/` | InspectionProcessor, IToolBlockValidator | ~1.682 |
| `Models/` | 16 classi modello | ~3.611 |
| `Inspector/` | sottosistema DataInspector | ~1.400 |
| `Views/` | 26 XAML + code-behind | ~5.000 est. |
| `Cls_Config/` | AsyncConfigManagerXml, RecipeParam | ~1.258 |
| `Converters/` | 8 converter WPF | ~1.723 |
| `ServerMessage/` | ServerMessageStructure | ~659 |
| `RecipeSwitch/` | RecipeAutoSwitcher | ~443 |
| `Support/` | helper condivisi commissioning/runtime | ~1 |

## Nuovo sottosistema commissioning nel full project

La baseline `lastRelease` ora contiene in-process il blocco commissioning nato nello standalone:

- `ViewModels/DigitalIOViewModel.cs`
- `Views/UserControls/DigitalIOControl.xaml`
- `Models/MachineRuntimeConfiguration.cs`
- `Models/MachineHardwareTemplate.cs`
- `Models/TrackedProduct.cs`
- `Services/MachineConfigurationService.cs`
- `Services/MachineController.cs`
- `Services/ToolLocalizationService.cs`
- `Services/ToolFusionSnapshotService.cs`
- `Services/ToolMainProjectEventMapper.cs`

Scopo del blocco:

- configurazione persistente I/O ed encoder
- diagnostica runtime di input, output e contatori
- tracking prodotto da fotocellula + encoder
- base per commissioning macchina reale prima dei test di passaggio prodotto

Confine architetturale:

- questo sottosistema non sostituisce `MainWindow` o il runtime VisionPro
- si integra al full project come layer operativo di configurazione/diagnostica macchina
- le funzionalita' camera/visione, analytics, PowerFlex e DataInspector restano sottosistemi distinti

---

## Architettura a Livelli (stato reale)

```
┌─────────────────────────────────────────────────────────────┐
│  PRESENTATION                                                │
│  MainWindow.xaml.cs  (God Class — ~3500 righe, in riduzione) │
│  Services/CameraDisplayManager.cs  (Fase 3 Step 1 — estratto)│
│  Views/ (26 XAML)    ViewModels/ (19 classi)                │
│  Converters/ (8)     NavigationMenu / TopMenuBar            │
└────────────────────────────┬────────────────────────────────┘
                             │ accoppiamento diretto via static
┌────────────────────────────▼────────────────────────────────┐
│  SERVICE REGISTRY                                            │
│  ServiceLocator.cs  (10 singleton, double-check locking)    │
│  UserSession.cs     (static — ruolo/utente corrente)        │
└────┬──────────────┬──────────────┬──────────────────────────┘
     │              │              │
┌────▼────┐  ┌──────▼──────┐  ┌───▼──────────────────────────┐
│SERVICES │  │  DOMAIN      │  │  HARDWARE LAYER               │
│15 classi│  │  DataManage/ │  │  Cls_Vpro/ (VisionPro)        │
│         │  │  Inspector/  │  │  Models/Advantech (I/O)       │
│         │  │  Cls_Config/ │  │  SaveImage/                   │
└────┬────┘  └──────┬───────┘  └───────────────────────────────┘
     │              │
┌────▼──────────────▼─────────────────────────────────────────┐
│  DATABASE                                                    │
│  Database/ (9 classi)  Raw SQL + MySql.Data                 │
└─────────────────────────────────────────────────────────────┘
```

**Nota critica:** il layering è perforato. MainWindow accede direttamente a DB, Hardware,
Config e Services, creando cicli nel grafo delle dipendenze.

---

## Flusso di Avvio

```
App.Startup
  ├─ Mutex singola istanza
  ├─ 3 global exception handlers (Dispatcher, AppDomain, TaskScheduler)
  ├─ ScheduleForcedProcessTermination (watchdog 30s)
  └─ SplashWindow (12 step lineari)
       ├─ InitCognex / LoadVPP
       ├─ LoadConfig  →  AsyncConfigManagerXml
       ├─ ServiceLocator.Initialize (10 servizi)
       ├─ InitializeDatabase  →  Cls_InitializzeDb
       ├─ InitializeRecipe
       └─ MainWindow.Show() → StartContinuousRun
```

---

## Flusso Runtime Ispezione

```
Camera trigger
  → CognexJobManager.UserResultAvailable
  → MyJobManager_UserResultAvailableAsync  (MainWindow)
       ├─ ResolveCameraRole(jobId)
       │    └─ CameraConfigurationHelper.NormalizeCameraType()
       │         → "top" | "top3d" | "side" | "front" | "rear" | "bottom"
       ├─ Capture OutputSnapshot + ResultSequence
       ├─ Enqueue  →  _topQueue / _sideQueue / _frontQueue / _rearQueue / _bottomQueue
       └─ ProcessQueuedPairAsync()
            ├─ GetActiveSecondaryInspectionRole()  → "side" | "front" | ""
            ├─ se secondaryQueue == null
            │    └─ drena _topQueue → ProcessInspectionPairAsync(top, null)
            └─ se secondaryQueue != null
                 └─ drena coppia  → ProcessInspectionPairAsync(top, secondary)
                      ├─ isTop3DInspection = (CameraRole == "top3d")   ← chiave routing
                      ├─ DisplayTasks  →  CogRecordsDisplay aggiornati
                      ├─ InspectionProcessor.ProcessInspectionAsync(OutputSnapshot...)
                      │    ├─ isTop3DInspection=true  → GetDetailedTop3DValidationAsync
                      │    └─ isTop3DInspection=false → GetDetailedTopValidationAsync
                      ├─ ProcessInspectionWithCountersAsync
                      │    └─ CounterManager.IncrementDefectAsync
                      ├─ AlarmService.ProcessInspectionResultAsync
                      └─ addRecordIndb  →  ProduzioneRecord → tblgenerale
```

Dal runtime `3.1.0.9`, lo snapshot `ProduzioneRecord` conserva anche
`IdProduzione` della ricetta attiva e label/score Classify separati per
Top, Side/Left, Front, Rear/Right e Bottom. I valori Classify provengono dagli
stessi `OutputSnapshot` immutabili usati dalla validazione del ciclo.

**Regola routing Top/Top3D (implementata 2026-04-13):**
Il routing usa `CameraResult.CameraRole` (derivato dal nome job nel VPP),
**non** `Config.MachineType`. Non modificare questa logica.

---

## Flusso Cambio Ricetta

```
RecipeManagerViewModel.SaveCommand
  → RecipeManagerViewModel → MainWindow
  → PrepareVisionSystemForRecipeReloadAsync
       ├─ MachineRuntimeService.AddContinuousRunHold(HoldReasonRecipeSave)
       ├─ ManageJobStateAsync(false)  →  StopJobs
       ├─ salva XML ricetta  →  AsyncRecipeParam
       ├─ InitializeRecipeAsync
       └─ runVisionPro  →  MachineRuntimeService.ReleaseContinuousRunHold
```

---

## Flusso Shutdown

```
ControlButtons.RestartRequested
  → MachineRuntimeService.StopContinuousRunAsync
  → InspectionProcessor.ShutdownAsync
  → ServiceLocator.ShutdownAllServicesAsync
  → Application.Shutdown()
  → ScheduleForcedProcessTermination (safety net — termina il processo dopo 30s)
```

---

## Moduli — Dettaglio

### MainWindow.xaml.cs (God Class)

- **Righe:** ~4.360 (cresciuta di ~700 con 3.0.x multi-camera)
- **Problema:** 26 `public static` fields, logica business, orchestrazione hardware, UI — tutto nello stesso file
- **Static fields critici:** `configManager`, `IsShuttingDown`, `MainView`, `_cognexManager`, `_cts`, `logger`, tutti i job/toolblock
- **Metodi chiave:** `ProcessQueuedPairAsync`, `ProcessInspectionPairAsync`, `ResolveCameraRole`, `GetActiveSecondaryInspectionRole`, `IsThreeDCheckMachineType` (usato solo per config display, non più per routing ispettivo)
- **Refactoring completato:** `CameraResult` estratto a `Models/CameraResult.cs`; `InspectionOrchestrator` estratto in `Services/InspectionOrchestrator.cs` e collegato da MainWindow tramite delegate injection

### ServiceLocator.cs

- **Pattern:** Double-check locking, 10 singleton lazy-init
- **Servizi registrati:** AlarmService, MachineRuntimeService, DialogService, ViewFactoryService, ApplicationEventLogger, SystemDiagnosticsService, OperatorInactivityService, CounterManager, InspectionConfigService, MachineStatusService
- **Problema noto:** `ShutdownAllServicesAsync()` dipende da `MainWindow.logger` — ciclo di dipendenza

### MachineRuntimeService.cs ★ (modello di riferimento)

- `SemaphoreSlim _continuousRunCommandSemaphore` — serializza start/stop
- Hold-reason HashSet: `HoldReasonManualStop`, `HoldReasonRecipeSave`, `HoldReasonJobEditor`, `HoldReasonShutdown`
- `ExecuteManagerOperationWithTimeoutAsync` — timeout 12s start, 8s stop
- `RecoverContinuousRunAsync` — stop → 500ms → start

### AsyncConfigManagerXml.cs ★ (modello di riferimento)

- Scrittura atomica: file temp + GUID + `File.Replace` + retry (5 tentativi, backoff esponenziale)
- Recovery da snapshot MySQL su config corrotto/mancante
- `NormalizeConfig` centralizza i valori default

### InspectionProcessor.cs

- **Costruttore:** `(IToolBlockValidator validator, ILogger logger)`
- **Metodo principale:** `ProcessInspectionAsync(topTB, sideTB, frontTB, isTop3DInspection, ct)`
- **Routing validator:** `isTop3DInspection` → Top3D, `!isTop3DInspection` → Top standard
- **Guard:** senza camera secondaria e senza Top3D → `IsValid = false`
- Notifica allarmi via `ServiceLocator.AlarmService.ProcessInspectionResultAsync`

### IToolBlockValidator.cs (interfaccia + implementazione)

Cinque metodi per ruolo camera (aggiornato 3.0.0.3):
- `GetDetailedTopValidationAsync` → Logo, PrintCentering, ShapeTop, OpenFlaps, SurfaceCheck
- `GetDetailedTop3DValidationAsync` → ThreeDHeight, ThreeDWidth, ThreeDLength
- `GetDetailedSideValidationAsync` → Height, SideSealing, ShapeSide
- `GetDetailedFrontValidationAsync` → FrontTraceability
- `GetDetailedBottomValidationAsync` → BottomSealing, TrappedPaper (solo output booleani VisionPro Bottom, non top-like)

Tutti usano `IsFeatureEnabled(feature)` da `InspectionConfigService` e `CounterManager.IncrementDefectAsync`.

### CognexJobManager (Cls_Vpro/ICognexJobManager.cs)

- `loadjobs(CogJob job)` mappa il job per ruolo su static fields
- "top" e "top3d" → stesso `_topJob`/`_topToolGroup` — distinti solo da `CameraRole`
- `NormalizeCameraType` → "top" per job "Top", "top3d" per job "Top3D"
- `BuildRoleMapping` (CameraConfigurationHelper) → nome job VPP vince su CameraConfig.xml

### Inspector/ (sottosistema DataInspector) ★

Il sottosistema più pulito del progetto. Usa dependency injection reale:
- `IPieceHistoryRepository` → `MySqlPieceHistoryRepository`
- `IImageEvidenceResolver` → `LocalPieceEvidenceResolver`
- `IDataSourceHealthProvider` → implementato da MySqlPieceHistoryRepository

### CounterManager (Database/CounterManager.cs)

- In-memory + flush MySQL
- Tipo difetto → chiave stringa → colonna DB
- Event `CounterManager.CountersUpdated`, pubblicato direttamente dal manager e sottoscritto dalle
  viste/VM che proiettano i contatori. Il vecchio event bus `CounterEvents.cs` non aveva consumer ed
  e' stato rimosso.

### MachineStatusService.cs

- Il timer determina lo stato in background, ma `CurrentStatus`, `StatusHistory` e
  `PropertyChanged` vengono trasferiti sul Dispatcher UI prima di toccare la
  `ObservableCollection`.
- Resta un singleton legacy esposto tramite `Instance`; evitare di creare un secondo owner durante
  la migrazione verso dipendenze esplicite.

### clsConnection.cs

- La configurazione MySQL viene letta dentro `ConnectToDatabaseAsync`; non esiste piu' una stringa
  host inizializzata staticamente prima del caricamento config.
- `DbConnectionStringHelper` centralizza pooling e timeout. Restano diversi consumer diretti di
  `MainWindow.configManager`, da migrare progressivamente dietro una factory.

---

## Grafo Dipendenze (accoppiamento reale)

```
MainWindow ──────────────────────────────────► TUTTO
    ├──► CognexJobManager
    ├──► IgigaCameraAccess
    ├──► InspectionProcessor
    ├──► AsyncConfigManagerXml
    ├──► clsConnection
    ├──► CounterManager
    ├──► ISaveImage
    └──► RecipeAutoSwitcher

ServiceLocator ──► MainWindow.logger          (CICLO — da risolvere)

ViewModel ──────► ServiceLocator
         ──────► MainWindow.* static          (VIOLAZIONE MVVM)

InspectionProcessor ──► ServiceLocator.AlarmService
                    ──► MainWindow._cts        (VIOLAZIONE — da rimuovere)
```

---

## Issue Aperte (da risolvere)

| # | Priorità | Issue | File | Stato |
|---|---|---|---|---|
| C1 | CRITICO | `StatusHistory.Add()` da background thread → crash ObservableCollection | `MachineStatusService.cs` | **Chiusa 2026-04-13** |
| C2 | CRITICO | `clsConnection` static init prima del config load → NullRef | `Database/clsConnection.cs` | **Chiusa 2026-04-13** |
| C3 | CRITICO | `_isContinuousRunActive` duplicato → race condition | `MainWindow.cs` + `MachineRuntimeService.cs` | **Chiusa 2026-06-11** |
| A1 | ALTO | `async void OnUserChanged` senza try/catch → eccezione silenziate | `MainViewModel.cs` | **Chiusa 2026-04-13** |
| A2 | ALTO | `ProcessorAffinity = 0x0003` hardcoded → 2 core only | `MainWindow.xaml.cs` | **Chiusa 2026-08-24: affinity non piu' forzata, log scheduler e benchmark ViDi EL 3.1.0.5** |
| A3 | ALTO | `AuthorizationService` per-istanza → cache separata per ogni VM; `InvalidateCache()` aggiunto per reset esplicito | `AuthorizationService.cs` | Parziale — cache reset da login/logout, ma istanze multiple non ancora unificate |
| M1 | MEDIO | `RelayCommand` definita 3 volte | `Models/`, `Inspector/`, `MainViewModel` | **Chiusa: una sola implementazione in `Models/RelayCommand.cs`** |
| M2 | MEDIO | `AlarmCardRepository` duplicata | `Database/` + `IntegratedAlarmCardService` | Aperta |
| M3 | MEDIO | Codice morto esteso con commenti | progetto | **Parziale: audit 2026-07-21 riduce la baseline di 2.048 righe nette e archivia 1.894 righe documentali; restano catch vuoti e God-class da trattare per pacchetti** |
| M4 | MEDIO | Dipendenza ciclica ServiceLocator ↔ MainWindow | `ServiceLocator.cs` | Aperta |
| F1 | RISOLTO | Routing Top/Top3D usava `MachineType` config invece del nome job VPP | `InspectionProcessor`, `MainWindow` | **Chiusa 2026-04-13** |

---

## Metriche Qualità per Modulo

| Modulo | Testabilità | Coesione | Accoppiamento |
|---|---|---|---|
| MachineRuntimeService | ★★★★☆ | Alta | Basso |
| AsyncConfigManagerXml | ★★★★☆ | Alta | Basso |
| EventLogRepository | ★★★★☆ | Alta | Basso |
| Inspector/ (sottosistema) | ★★★★☆ | Alta | Basso |
| IToolBlockValidator | ★★★☆☆ | Media | Medio |
| InspectionProcessor | ★★★☆☆ | Media | Medio |
| CounterManager | ★★☆☆☆ | Bassa | Alto |
| ServiceLocator | ★★☆☆☆ | Bassa | Alto |
| MainWindow.xaml.cs | ★☆☆☆☆ | Nulla | Totale |

---

## Piano di refactoring corrente

Il piano operativo completo, con priorita', rischio, criteri di accettazione e rollback, e' in:

`Documentation/improvement-plan-2026-07.md`

Ordine sintetico: test di caratterizzazione, warning e dipendenze, code risultati senza drop,
lifecycle Cognex con test hardware, resilienza DB/OPC UA, riduzione progressiva delle God-class e
soak test 72h. Sono gia' completati `CameraDisplayManager`, modello `CameraResult`,
`InspectionOrchestrator`, config atomica, code bounded per dati best-effort, AI advisory fuori dal
percorso caldo e audit del codice morto.

## Piano storico iniziale (superato dal piano corrente)

```
Fase 1 — Fix critici (zero regressioni attese)
  ├─ C1: StatusHistory.Add() → Dispatcher.BeginInvoke
  ├─ C2: clsConnection → lazy property invece di static init
  └─ A1: async void → SafeFireAndForget nei command handlers

Fase 2 — Consolidamento
  ├─ RelayCommand unica in Models/
  ├─ AlarmCardRepository unica
  └─ CounterManager: separare in-memory da flush DB

Fase 3 — Riduzione MainWindow
  ├─ [DONE] Estrarre CameraDisplayManager (ProcessToolResultsAsync*, UpdateCameraStatusIndicators, ResolveCameraRole, GetDisplayForRole, GetLastRunViewForRole, RefreshCameraFeatureVisibility, GetCurrentTopCameraImage)
  ├─ [DONE] CameraResult estratto a Models/CameraResult.cs - class privata rimossa da MainWindow
  ├─ [DONE] InspectionOrchestrator estratto (ProcessQueuedPairAsync + ProcessInspectionPairAsync)
  └─ Estrarre RecipeLifecycleManager (PrepareVisionSystemForRecipeReloadAsync)

Fase 4 — DI reale
  ├─ IServiceProvider al posto di ServiceLocator (opzionale)
  └─ MainWindow riceve dipendenze via costruttore, elimina static fields
```
