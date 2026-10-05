## [2026-09-28] Selettore Right e tipo attuatore scarto (3.1.5.7)

- La pagina I/O espone l'uscita trigger della camera REAR, associata alla camera fisica destra. Un punto nuovo resta disabilitato finche' l'operatore non lo abilita nel piano degli interventi. Le etichette operative delle ispezioni e dei classificatori mostrano TOP, SIDE, REAR secondo i nomi del job.
- Scarto: soffio e cilindro monostabile usano un solo impulso sull'uscita di scarto, della durata configurata, seguito da LOW. Le uscite storiche OPEN/CLOSE non vengono piu' attivate dalla sola presenza di due canali.
- Il bistabile resta non disponibile finche' non saranno implementati gli ingressi dei sensori di posizione OPEN/CLOSE. Un XML precedente con entrambe le uscite assegnate viene riconosciuto come bistabile e lo scarto viene bloccato con evento diagnostico.
- XML macchina: nuovo campo opzionale `RejectActuatorType` (`AirBlast`, `Monostable`, `Bistable`); nessuna migrazione DB o variazione di `Config.xml`. Verificare fisicamente polarita', durata e ritorno del monostabile prima dell'uso in produzione.

## [2026-09-28] Controlli AI: TOP e SIDE in menu espandibili come le altre camere

**File modificati:**
- `Views/UserControls/SystemDiagnosticsView.xaml` — nuova struttura della colonna ONNX:
  - in alto, sempre visibile, la parte comune a tutte le camere: Python, cartella modelli, epoche,
    `Test Python`, `Annulla` (spostato accanto a Test Python) e lo stato del training, seguiti dalle
    note su resize e confidenza;
  - sotto, un `Expander` per camera: TOP, SIDE, poi REAR/RIGHT, FRONT e BOTTOM. Ognuno contiene i
    propri campi e il proprio pulsante di training;
  - i campi TOP e SIDE restano legati ai campi dedicati di `AiSettings`; tutti gli `x:Name`
    localizzati sono conservati, tranne il titolo SIDE.
- `Views/UserControls/SystemDiagnosticsView.xaml.cs`:
  - il titolo della colonna diventa "ONNX classifiers per camera (shadow-mode)";
  - l'intestazione SIDE ora viene dalla card, con il nome della camera come nel VPP.
- `ViewModels/SystemDiagnosticsViewModel.cs`:
  - nuove card `TopClassifierCard` e `SideClassifierCard` (`DefectClassifierProfileItem`) per
    intestazione, presenza nel VPP e stato del menu;
  - lo stato aperto/chiuso di ogni camera resta anche dopo Salva o Ricarica.
- `Localization/messages_ita.json`, `messages_eng.json` — nuovo testo per
  `Sub_entry_AiOnnxClassifierTitle`.

**Motivo:** layout uniforme per tutte le camere, richiesto dall'utente.

**Impatto:**
- Config.xml: nessuno.
- Database: nessuno.
- API pubblica: `SystemDiagnosticsViewModel.TopClassifierCard` e `SideClassifierCard`.
- Comportamento runtime: solo layout del pannello. Ogni menu parte aperto se la camera è nel VPP
  attivo o ha un modello abilitato.

## [2026-09-28] Classificatore ONNX e training per tutte le camere (REAR/RIGHT, FRONT, BOTTOM)

Prima solo TOP e SIDE avevano modello, training e shadow-mode. Ora ogni camera gestita dalla
piattaforma ha un profilo proprio. `label.json` conteneva già `labels.rear`, `labels.front` e
`labels.bottom`, e le immagini venivano già salvate (`_RI_`/`_R_`, `_FR_`, `_B_`).

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs`:
  - nuova classe `DefectClassifierCameraProfile` (INotifyPropertyChanged). Campi: attributo
    `CameraRole`, Enabled, ModelPath, ModelVersion, ModelNotes, InputWidth/Height, Grayscale,
    NormalizeMean/Std, MinConfidence;
  - `NormalizeRole`: top/top3d → top, side/left → side, rear/right → rear, front, bottom;
  - `MachineRuntimeBindings.AdditionalDefectClassifierProfiles` (lista);
  - `MachineRuntimeBindings.ResolveDefectClassifierProfile(role)`: vista comune; per TOP e SIDE
    restituisce una copia dei campi dedicati, che restano invariati.
- `Services/MachineConfigurationService.cs`:
  - `EnsureAdditionalDefectClassifierProfiles` garantisce un profilo per rear, front e bottom,
    scarta duplicati e ruoli ignoti, e riporta i valori nei limiti. Viene chiamato sia al `Load`
    sia in `EnsureRuntimeBindingsDefaults`;
  - nuovo marker `<AdditionalDefectClassifierProfiles>`: i file esistenti vengono aggiornati
    all'avvio.
- `Services/OnnxDefectClassifier.cs`:
  - accetta qualsiasi ruolo;
  - la configurazione si legge da `ResolveDefectClassifierProfile`;
  - il confronto usa il risultato della propria camera: `RearResult`, `FrontResult` o
    `BottomResult`;
  - `GetImageTags`: rear → `_RI_`, `_R_`; front → `_FR_`; bottom → `_B_`.
- `ServiceLocator.cs`:
  - `AdditionalDefectClassifiers`: un'istanza per rear, front e bottom;
  - `AllDefectClassifiers`: TOP, SIDE e le camere aggiuntive;
  - `GetDefectClassifier(role)`;
  - dispose in `Reset`.
- `MainWindow.xaml.cs` — il confronto shadow gira su `AllDefectClassifiers`: ogni camera è
  indipendente e diventa no-op senza modello.
- `Services/DefectClassifierTrainingService.cs`:
  - impostazioni e conteggio del dataset per qualsiasi camera, con `labels.<camera>` e le sigle
    immagine della camera;
  - nome file `defect_classifier_<camera>_vN.onnx`; il TOP resta `defect_classifier_vN.onnx`.
- `Scripts/AI/train_defect_classifier.py`:
  - `CAMERA_IMAGE_TAGS` per top, side, rear, front e bottom;
  - `--camera` accetta tutti e cinque i valori.
- `ViewModels/DefectClassifierProfileItem.cs` (nuovo, nel csproj):
  - `DefectClassifierProfileItem` è la card camera. Contiene il nome come nel VPP, le sigle
    immagine, la presenza nel VPP attivo e le statistiche shadow;
  - `OnnxFieldLabels`: etichette localizzate.
- `ViewModels/SystemDiagnosticsViewModel.cs`:
  - nuove proprietà `AdditionalClassifierProfiles` e `OnnxLabels`;
  - nuovo comando `TrainCameraDefectClassifierCommand` (il parametro è il ruolo);
  - statistiche, reset e refresh servizi estesi a tutte le camere;
  - validazione al salvataggio e prima del training estesa a ogni profilo;
  - il training precompila il profilo della camera addestrata.
- `Views/UserControls/SystemDiagnosticsView.xaml(.cs)`:
  - sotto il blocco SIDE c'è una card espandibile per ogni camera aggiuntiva, con gli stessi
    campi e il pulsante di training. È aperta se la camera è nel VPP attivo o ha già un modello
    abilitato;
  - le statistiche shadow ora mostrano anche queste camere;
  - i testi SIDE indicano `_F_ / _L_`.
- `Localization/messages_ita.json`, `messages_eng.json` — nuove chiavi `Sub_entry_AiCamera*` e
  `Sub_entry_AiTrainCameraModel*`, testi SIDE e hint aggiornati.

**Motivo:** con tre camere (Top, Side, Rear) non era possibile creare un modello per la Rear.
Ogni camera che la piattaforma gestisce deve poter avere il proprio classificatore.

**Impatto:**
- Config.xml: nessuno.
- machine_runtime_config.xml:
  - nuovo blocco `RuntimeBindings/AdditionalDefectClassifierProfiles`, con un
    `DefectClassifierCameraProfile CameraRole="rear|front|bottom"` per camera;
  - aggiunto automaticamente all'avvio, disabilitato;
  - i campi TOP e SIDE non cambiano.
- Database: nessuno.
- API pubblica:
  - `ServiceLocator.AdditionalDefectClassifiers`, `AllDefectClassifiers`, `GetDefectClassifier`;
  - `MachineRuntimeBindings.ResolveDefectClassifierProfile`;
  - `SystemDiagnosticsViewModel.TrainCameraDefectClassifierCommand`.
- Comportamento runtime:
  - senza modello abilitato nulla cambia: le nuove istanze sono no-op;
  - resta un'unica inferenza globale alla volta per tutte le camere (`GlobalInferenceGate`);
  - il classificatore resta advisory (shadow-mode).

**Collaudo:**
1. Aprire PC Diagnostics → Controlli AI. Sotto SIDE deve comparire la card "Classificatore REAR
   (immagine _RI_ / _R_)" aperta; FRONT e BOTTOM devono essere chiuse.
2. Con dataset sufficiente (≥10 OK e ≥10 NOK con `labels.rear`) premere "Addestra modello REAR".
   Si deve ottenere `defect_classifier_rear_v1.onnx` precompilato nella card.
3. Abilitare, salvare e far passare pezzi. Nel log deve comparire
   `VISION_ML_INIT|camera=rear|loaded_out_of_process`, e nelle statistiche shadow la sezione
   REAR.

## [2026-09-28] Training AI Side: immagini salvate come `_L_` non trovate

Da quando un job Side viene mostrato nella vista Left (camera fisica `left`), le sue immagini
pezzo vengono salvate con la sigla della vista: `CH1_<pezzo>_L_A.bmp` / `_L_Z.jpg`. Il trainer,
il controllo dataset del pannello e il classificatore ONNX Side cercavano solo `_F_`. Tutti i
campioni Side risultavano quindi "senza immagine" e l'inferenza Side andava in timeout immagine.
`label.json` era comunque scritto correttamente, con `labels.side` preso dal risultato della
camera Left/Side.

**File modificati:**
- `Services/OnnxDefectClassifier.cs`:
  - nuovo `GetImageTags(cameraRole)`: TOP -> `_T_`, SIDE -> `_F_` poi `_L_`;
  - `FindReadableImage` prova tutte le sigle (raw `A.bmp` prima, annotata `Z.jpg` dopo).
- `Services/DefectClassifierTrainingService.cs` — `HasCameraImage` usa `GetImageTags`: il
  conteggio campioni prima del training vede le immagini `_L_`.
- `Scripts/AI/train_defect_classifier.py` — `find_camera_image` accetta più sigle; per side
  cerca `("_F_", "_L_")`.

**Motivo:** con il VPP Top/Side/Rear la raccolta dati Side non era utilizzabile per il training.

**Impatto:**
- Config.xml: nessuno.
- Database: nessuno.
- API pubblica: `OnnxDefectClassifier.GetImageTags` (internal).
- Comportamento runtime:
  - i dataset già raccolti con `_L_` diventano utilizzabili senza rinominare file;
  - i dataset storici `_F_` restano compatibili;
  - la camera Rear/Right (`_RI_` / `_R_`) non ha ancora un profilo classificatore dedicato.

**Collaudo:** con `Training data collection` attivo e salvataggio immagini configurato, passare
dei pezzi, poi premere `Addestra modello` sul profilo SIDE. Il log `AI_TRAINING_PROGRESS|stage=scan`
deve riportare `senza_immagine=0` per i pezzi con immagine `_L_`.

## [2026-09-28] Nomi camera dal VPP (Side/Rear) invece di Left/Right, ShapeSide sulla vista Right

Con un VPP a tre job (Top, Side, Rear), le pagine Inspection Configuration, Ricetta
("Enabled inspections") e Display mostravano LEFT e RIGHT. Le camere erano gestite
correttamente: dalla separazione per camera fisica un job Side è la camera interna `left` e un
job Rear è la camera interna `right`. Le etichette però erano fisse sul ruolo interno e non
riportavano il nome del job VPP.

**File modificati:**
- `Models/CameraModel.cs` (`CameraConfigurationHelper`):
  - nuovo `GetCameraRoleDisplayLabel(role)`. Restituisce il nome della camera come nel VPP (TOP,
    SIDE, REAR, LEFT, RIGHT, TOP 3D) leggendo `MainWindow.JobMapping` / `JobRoleMapping`;
  - il nome del job ha la precedenza sul ruolo risolto da seriale o `CameraConfig.xml`. Senza VPP
    restituisce il nome del ruolo richiesto;
  - l'elenco delle parole chiave "right" è estratto in `ContainsRightKeyword`, senza cambiare il
    comportamento delle normalizzazioni.
- `ViewModels/InspectionConfigViewModel.cs` — `CameraViewInspectionRow.DisplayName` usa il nome
  del job VPP.
- `ViewModels/RecipeManagerViewModel.cs`:
  - `DescribeViewRole` usa il nome del job VPP (gruppi "Enabled inspections" e sottotitoli delle
    tolleranze);
  - il gruppo "Nessuna vista assegnata" diventa "Nessuna camera la esegue (disattivata per tutte le
    camere in Inspection Configuration)".
- `Views/UserControls/DisplayRecord/CameraFeaturesTitle.cs` (nuovo, aggiunto al csproj) — formato
  del titolo pannello da `lbCameraFeaturesFormat`.
- `Views/UserControls/DisplayRecord/LeftCameraView.xaml.cs`, `RightCameraView.xaml.cs` — nuovo
  `UpdateCameraTitle(viewLabel)`.
- `Views/UserControls/DisplayRecord/RightCameraView.xaml(.cs)` — aggiunta l'icona ShapeSide
  (`ShapeSideFeatureContainer` / `ShapeSideStatusOverlay` / `ShapeSideStatusText`). La matrice la
  permette per questa camera, ma il pannello non la mostrava.
- `Services/CameraDisplayManager.cs`:
  - `RefreshCameraFeatureVisibility` imposta i titoli Left/Right con il nome del job e gestisce la
    visibilità ShapeSide della vista Right;
  - lo stato ShapeSide della vista Right usa gli errori del risultato Rear.
- `Localization/messages_ita.json`, `messages_eng.json` — nuova chiave `lbCameraFeaturesFormat`
  ("Funzioni camera {0}" / "{0} Camera Features").

**Motivo:** l'operatore deve ritrovare in pagina i nomi dei job del proprio VPP. Con Top/Side/Rear
vedeva LEFT/RIGHT senza sapere da dove arrivassero.

**Impatto:**
- Config.xml: nessuno.
- Database: nessuno.
- Ricette: nessuno. Le chiavi interne (`left`/`right`, `CameraTriggers.Left/Side/Right/Rear`,
  matrice vista × ispezione) non cambiano.
- API pubblica: `CameraConfigurationHelper.GetCameraRoleDisplayLabel`,
  `LeftCameraView.UpdateCameraTitle` e `RightCameraView.UpdateCameraTitle` aggiunti.
- Comportamento runtime:
  - solo etichette, più il nuovo indicatore ShapeSide sulla vista Right/Rear;
  - routing, gating e validazione invariati.

**Collaudo:** caricare il VPP Top/Side/Rear.
- Inspection Configuration deve mostrare le card TOP, SIDE e REAR.
- Ricetta → Enabled inspections deve mostrare i gruppi TOP, SIDE e "TOP / SIDE / REAR".
- Display deve mostrare "Funzioni camera Side" e "Funzioni camera Rear".
- Con ShapeSide abilitata per REAR deve comparire l'icona nel pannello Rear.

## [2026-09-28] Camere fisiche e scarto verificabile (3.1.5.6)

- Side/Rear nei vecchi VPP e XML sono alias di ingresso di Left/Right. Code e ticket runtime sono separati per camera fisica; `UserResultTag` ripetuti non consumano il ticket del prodotto successivo.
- La ricetta mostra un solo trigger, offset e profilo per Left e Right. I nuovi ritardi hardware Left, Right e Bottom sono opzionali; Left eredita `SideCameraTriggerDelay` finche' non viene impostato il campo nuovo.
- Lo scarto OPEN/CLOSE e quello a impulso singolo sono considerati eseguiti solo dopo il completamento dell'impulso. Un tentativo parziale viene segnalato e non ripetuto automaticamente; un canale DO con discesa fallita richiede che il driver BDaq accetti una nuova scrittura LOW. Verificare il livello elettrico sulla macchina.
- Nessuna migrazione DB o cambiamento al mapping macchina. Build x64 Release e prove isolate su alias e tag passate; verificare sulla macchina l'attuatore e le acquisizioni Right extra con tag distinti.

## [2026-09-25] I/O predisposto per camera, scarto OPEN/CLOSE, canali a tendina

Richiesta: con il numero di camere noto (job del VPP) la configurazione I/O deve essere già
predisposta. L'operatore assegna solo il canale da una lista (DO00..DO31 / DI00..DI31), senza
scrivere codici. Lo scarto deve poter usare due uscite (apertura e chiusura).

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`:
  - nuovo `ProvisionCameraIoAsync(cameraRoles, source)`. Per ogni camera del VPP (top, left, right,
    front, bottom) aggiunge, se mancano, l'uscita trigger e il punto `CAMERA_TRIGGER_<ROLE>`.
    Aggiunge sempre `OUT_BLOCKING_ALARMS`, `OUT_NON_BLOCKING_ALARMS`, `OUT_REJECT_OPEN` e
    `OUT_REJECT_CLOSE`. I nuovi elementi hanno il **canale vuoto** e i punti nascono
    **disabilitati**: nessun canale viene assegnato d'ufficio. Le righe esistenti non vengono mai
    modificate; gli alias Side/Left e Rear/Right contano come già presenti. Normalizza i nomi
    canale (`DO6` -> `DO06`). Salva solo se la pagina non era già in modifica, altrimenti lascia le
    modifiche da salvare (`IO_PROVISIONING_APPLIED_NOT_SAVED`). Log: `IO_PROVISIONING_APPLIED`,
    `IO_PROVISIONING_UP_TO_DATE`;
  - nuove liste `OutputChannelOptions` e `InputChannelOptions`: vuoto, `DO00..DO31`/`DI00..DI31`
    e i canali PCIE-1884 `DO100..DO103`/`DI100..DI103`;
  - scarto a due uscite: nuove proprietà `BoundRejectOpenSignalCode`, `BoundRejectCloseSignalCode`,
    `ConfiguredRejectCloseDelayMs` (default 300 ms) e `RejectActuatorSummary`. Se OPEN e CLOSE
    hanno entrambe un canale, `ExecuteRejectForProductAsync` invia l'impulso OPEN al punto di
    scarto e l'impulso CLOSE dopo il ritardo misurato dalla salita di OPEN (log
    `REJECT_OPEN_CLOSE` / `REJECT_OPEN_FAILED`). Altrimenti resta l'impulso singolo su
    `RejectSignalCode`;
  - `MigrateLegacyTwoCameraRuntimeConfiguration`: le uscite create dalla migrazione non ricevono
    più i canali fissi DO01/DO02/DO04/DO05, ma canale vuoto;
  - `ValidateTriggerExecutionConfiguration`: una riga è incompleta solo se manca il SignalCode, per
    cui un canale vuoto è ammesso. Nuovo avviso per un punto abilitato su un'uscita senza canale, e
    per lo stesso canale DO assegnato a segnali diversi (gli alias Side/Left e Rear/Right non
    contano come conflitto).
- `Models/MachineRuntimeConfiguration.cs` — `MachineRuntimeBindings`: nuovi campi
  `RejectOpenSignalCode`, `RejectCloseSignalCode`, `RejectCloseDelayMs`.
- `MainWindow.xaml.cs` — nuovo `ProvisionCameraIoFromActiveVpp(source)`: ricava i ruoli dal
  JobMapping (`ResolveCameraRole`) e chiama il provisioning dopo `RefreshRuntimeInspectionFeatures()`
  in `InitializeRecipeAsync` e `InitializeVisionSystem`.
- `Views/UserControls/DigitalIOControl.xaml`:
  - la colonna Canale delle griglie Uscite e Ingressi (vista estesa e vista compatta) è ora un
    `DataGridComboBoxColumn` sulle liste canali. Le griglie encoder restano a testo;
  - nei due pannelli "Runtime bindings" / "Reject" ci sono ora i campi Uscita OPEN, Uscita CLOSE,
    Ritardo CLOSE (ms) e il riepilogo dell'attuatore.

**Motivo:** rendere la configurazione I/O guidata. Le camere sono note dal VPP, la scheda ha 32 DO e
32 DI (31 usati), e l'operatore deve solo scegliere il canale. Serve poi un deviatore a due comandi
per l'espulsione.

**Impatto:**
- Config.xml: nessuno.
- machine_runtime_config.xml:
  - nuovi campi `RuntimeBindings/RejectOpenSignalCode`, `RejectCloseSignalCode`,
    `RejectCloseDelayMs`;
  - al caricamento di ricetta o VPP vengono aggiunte, con canale vuoto, le uscite e i punti
    mancanti per le camere presenti;
  - i file esistenti restano compatibili (valori di default).
- Database: nessuno.
- API pubblica: `DigitalIOViewModel.ProvisionCameraIoAsync`, nuove proprietà bindabili.
  `PulseMappedOutputAsync` invariata.
- Comportamento runtime:
  - finché OPEN/CLOSE non hanno un canale, lo scarto resta a impulso singolo, come prima;
  - un punto trigger predisposto resta disabilitato finché l'operatore non assegna il canale e lo
    abilita.

**Collaudo:**
1. Caricare un VPP Top+Side e verificare nel log `IO_PROVISIONING_APPLIED` e nelle griglie le
   nuove righe senza canale.
2. Assegnare i canali dalla tendina, salvare e riaprire la pagina: i valori restano.
3. Assegnare OPEN/CLOSE e verificare nel log `REJECT_OPEN_CLOSE` con il ritardo effettivo.
4. Assegnare lo stesso DO a due segnali e verificare l'avviso di validazione.

## [2026-09-25] Correzione: feature del job Side non caricate, risultato Right perso nel gruppo

Effetti della rinomina Side -> Left / Rear -> Right dello stesso giorno, trovati in simulazione
con un VPP a due job (Top e Side): la camera veniva mostrata come `left` ma le ispezioni del job
Side non venivano caricate.

**File modificati:**
- `MainWindow.xaml.cs` — `AddSupportedFeaturesForRole`: i casi `side` e `left` sono uniti.
  `ResolveCameraRole` restituisce ora `left` anche per un job `Side`, e il caso `left` cercava
  solo le uscite dedicate Left, quindi Height (`Heigth`), SideSealing (`SealingArea`) e ShapeSide
  sparivano al caricamento del VPP. L'euristica storica "ShapeSide se esiste il ToolBlock" vale
  ancora solo per i job con nome Side (nuovo parametro `jobName`); per un job Left moderno
  ShapeSide richiede il ToolBlock shape o le uscite `A_T_L/A_T_R/A_B_L/A_B_R`
- `DataManage/InspectionConfigService.cs` — `BuildRuntimeFeatureMap`: Side e Left sono la stessa
  camera fisica (`hasLeftCamera`) per Height, SideSealing, SideRollCount, ShapeSide e
  AIClassification
- `MainWindow.xaml.cs` — `FindCompanionResult` cerca con `NormalizePhysicalCameraRole`, la stessa
  funzione con cui l'orchestratore scrive le chiavi. Con `NormalizeCameraType`, "right" diventava
  "rear" e il risultato della camera Right non veniva mai trovato: nel gruppo `rearResult` era
  sempre nullo
- `Views/UserControls/DisplayRecord/LeftCameraView.xaml(.cs)` — la vista Left diventa una vista
  laterale completa: aggiunte le card Height e Shape side accanto a Sealing e RollCount, con le
  stesse chiavi di `SideCameraView`. Un job Side viene ora mostrato nella vista Left (template
  `left`), che prima aveva solo Sealing e RollCount: il pannello "Left Camera Features" restava
  vuoto anche con l'ispezione Side attiva
- `Services/CameraDisplayManager.cs` — visibilita' delle card Left per Height, SideSealing,
  ShapeSide e SideRollCount
- `ViewModels/InspectionConfigViewModel.cs` — la pagina Inspection Configuration mostra per ogni
  camera solo le ispezioni che puo' eseguire (`DefaultAppliesTo`) piu' quelle abilitate
  esplicitamente. Prima mostrava anche ogni cella salvata a "no": poiche' il salvataggio scrive
  tutte le combinazioni camera x feature, dopo il primo salvataggio ogni camera elencava le
  feature di tutte le altre (Logo sulla Left, 3D sulla Top 2D). Le celle nascoste conservano il
  valore salvato: il salvataggio parte dalla matrice della ricetta

**Motivo:** nel codice convivevano due normalizzazioni dei ruoli (`NormalizeCameraType` con
side/rear distinti, `NormalizePhysicalCameraRole` con left/right) usate su lati opposti dello
stesso confronto.

**Impatto:**
- Config.xml / ricette / database: nessuno
- API pubblica: `AddSupportedFeaturesForRole` (privato) accetta il nome del job
- Comportamento runtime: con un job Side le ispezioni della vista laterale tornano disponibili e
  il risultato Right entra nella validazione del pezzo. Nessun cambiamento per Top, Front, Bottom

**Verifica:** build Release x64 completa del 2026-09-25 09:01 senza errori. Da provare in
simulazione con il VPP Top + Side: la pagina Inspection Configuration deve mostrare Height,
Side sealing e Shape side nella colonna LEFT.

## [2026-09-25] Camere indipendenti, fase A: log e diagnostica per camera

Prima fase del piano "ogni camera lavora in modo indipendente". Non cambia trigger, ispezione
o scarto: rende leggibile la catena di una singola camera. La fase B (pipeline di ispezione per
camera con verdetto aggregato per `ProductId`) e' descritta in fondo.

**File modificati:**
- `Services/CameraDiagnostics.cs` — **nuovo**. Un logger NLog per camera fisica (`Camera.top`,
  `Camera.left`, `Camera.right`, `Camera.front`, `Camera.bottom`; Side/Rear confluiscono in
  Left/Right tramite `NormalizePhysicalCameraRole`) e contatori per camera. Eventi:
  `CAM_TRIGGER_POINT` (target, quota reale, ritardo count/mm), `CAM_TICKET_REGISTERED` /
  `CAM_TICKET_REALIGNED`, `CAM_PULSE` (raiseLatency, durata richiesta ed effettiva),
  `CAM_PULSE_FAILED`, `CAM_TRIGGER_NOT_EXECUTED`, `CAM_RESULT` (sequenza, job, prodotto,
  latenza trigger->risultato, tempi job VisionPro, errore di elaborazione),
  `CAM_RESULT_UNSOLICITED`, `CAM_RESULT_STALE_DROPPED`, `CAM_RESULT_MISSING`. Ogni 50 trigger
  per camera `CAM_SUMMARY` con totali e massimi della finestra
- `QtisVisionPanel.csproj` — `Compile Include` del nuovo file
- `Nlog.config` — target `camerafile` (`D:/QtisVision/Debug/logs/camera-<ruolo>.log`, rotazione
  giornaliera, 10 giorni) e regola `Camera.*` non `final`: le righe restano anche in `app.log`
  e, se WARN+, in `errors.log`
- `ViewModels/DigitalIOViewModel.cs` — il punto `TriggerCamera` registra quota, ticket, fronte o
  fallimento sul logger della camera; `PulseMappedOutputAsync` accetta ruolo e prodotto
  opzionali (gli altri chiamanti restano invariati)
- `MainWindow.xaml.cs` — al riscatto del ticket `CAM_RESULT` o `CAM_RESULT_UNSOLICITED`
- `Services/InspectionOrchestrator.cs` — `CAM_RESULT_STALE_DROPPED` e `CAM_RESULT_MISSING`
  per camera, accanto ai log esistenti

**Motivo:** nel collaudo del 2026-09-24 ricostruire i 23 doppioni Rear e i ritardi di una sola
camera richiedeva di correlare a mano righe di thread diversi, con nomi Side/Left e Rear/Right
mescolati.

**Impatto:**
- Config.xml / ricette / database: nessuno
- Nlog.config: nuovo target e nuova regola (file aggiuntivi nella cartella log esistente)
- API pubblica: nuova classe interna `CameraDiagnostics`
- Comportamento runtime: nessun cambiamento funzionale. Nessuna chiamata dal thread di polling;
  i log esistenti restano invariati

**Verifica:** build Release x64 completa senza errori; routing NLog provato con la configurazione
del progetto (`camera-left.log` e `camera-right.log` separati, stesse righe in `app.log`).

**Fase B (prossima, non ancora implementata), decisioni prese:**
- ogni camera viene validata appena arriva il suo risultato, senza attendere Top;
- errore tool su una camera: solo quella camera risulta non classificata, le altre vengono
  validate; il pezzo e' NOK se un'altra camera trova difetti, altrimenti non classificato;
- camera attesa che non risponde entro il timeout: pezzo non classificato per quella vista;
- il verdetto del pezzo (scarto, contatori, riga DB) si aggrega per `ProductId` senza bloccare
  l'elaborazione delle singole camere.

## [2026-09-25] Camere fisiche Top/Left/Right/Bottom separate e coda impulsi senza blocco tra canali

**Contesto:** il VPP del collaudo ha tre job fisici: Top (S1214730), Side = Left (S1232080) e
Rear = Right (S1225066). I file storici usano ancora Side/Rear, `CameraConfig.xml` elenca
Front e in `Config.xml` alcuni campi seriale contengono `DO41`. Il runtime identifica ora le
camere fisiche come Left e Right senza stato condiviso con altre viste, e i nomi Side/Rear
restano leggibili.

**File modificati:**
- `Models/CameraModel.cs` — nuovo `CameraConfigurationHelper.NormalizePhysicalCameraRole`
  (side/left → `left`, rear/right → `right`, top3d → `top`): chiave unica di stato runtime
- `DataManage/CameraInspectionMatrix.cs` — `NormalizeRole` restituisce `left`/`right`;
  `IsLegacyPhysicalAlias`; con una cella storica Side/Rear e una Left/Right esplicita prevale
  quella fisica
- `DataManage/InspectionConfigService.cs`, `Services/RecipeMachineRuntimeResolver.cs` —
  precedenza esplicita: il campo ricetta Left/Right vince, Side/Rear vale solo se il fisico e'
  vuoto o `Machine`. Nessuna somma di offset storici e fisici
- `Services/CameraTriggerCorrelationTracker.cs` — ruolo del ticket normalizzato con
  `NormalizePhysicalCameraRole` sia in `Register` sia in `TryClaim`
- `Services/CameraDisplayManager.cs`, `Services/InspectionOrchestrator.cs`, `MainWindow.xaml.cs`
  — ruolo `right` instradato sulla coda Rear; il risultato compagno si cerca come right o rear
- `ViewModels/RecipeManagerViewModel.cs`, `Views/RecipeManagerView.xaml(.cs)` — un solo
  controllo LEFT e uno RIGHT per modo trigger e offset. Leggono il valore storico se il fisico
  manca; in modifica scrivono il campo fisico e neutralizzano lo storico (`Machine` / 0 mm).
  Rimosse le etichette SIDE/REAR non piu' presenti nello XAML (errori CS0103)
- `ViewModels/InspectionConfigViewModel.cs` — lettura e scrittura del modo trigger per camera
  allineate a `NormalizeRole`: prima il caso `right` mancava (la pagina mostrava sempre
  `Machine` e non salvava la modifica) e `left` ignorava il valore storico Side
- `ViewModels/InspectionConfigViewModel.cs`, `ViewModels/RecipeManagerViewModel.cs` — etichette
  vista: SIDE diventa LEFT, REAR / RIGHT diventa RIGHT
- `Models/AdvantechDeviceManager.cs` — `OutputPulseLoop` avvia la prima richiesta pronta per
  ciascun canale invece della sola testa della coda: un impulso in attesa su un DO ancora alto
  non blocca piu' quelli pronti su altri DO. L'ordine FIFO sulla stessa uscita e' conservato

**Motivo:** con alias Side/Left e Rear/Right trattati come ruoli distinti, una ricetta con
entrambi i campi poteva applicare il valore sbagliato. Una configurazione ambigua poteva
inoltre unire due job. La coda con esame della sola testa ricreava, per richieste accodate
dietro un canale occupato, la serializzazione osservata nel collaudo del 2026-09-24.

**Impatto:**
- Config.xml: nessuno (i campi seriale con `DO41` restano da correggere a mano)
- Ricetta XML: nessuna nuova chiave. Le ricette esistenti si caricano invariate; il primo
  salvataggio dopo una modifica Left/Right sposta il valore nel campo fisico
- Database: nessuna modifica di colonne; le colonne storiche Side/Rear restano
- API pubblica: aggiunti `CameraConfigurationHelper.NormalizePhysicalCameraRole` e
  `CameraInspectionMatrix.IsLegacyPhysicalAlias`; `NormalizeRole` restituisce `left`/`right`
- Comportamento runtime: Left e Right hanno stato, ticket e code separati; nessun cambiamento
  per Top e Bottom

**Collaudo:** build Release x64 completa del 2026-09-25 08:33 senza errori. Non ancora provata
in macchina. Verificare nella pagina ricetta e in Inspection Configuration che i modi LEFT e
RIGHT letti da una ricetta storica coincidano con quelli applicati (log
`RECIPE_MACHINE_RUNTIME_APPLIED`), e che ogni risultato Left/Right trovi il suo ticket (nessun
`VISION_RESULT_UNSOLICITED_DROPPED` oltre ai doppioni Rear gia' noti).

## [2026-09-25] PCIE-1756 a 32/32 canali, pulizia configurazione uscite, errori BDaq propagati agli impulsi

La voce documenta tre interventi presenti nel working tree. Il primo e' il codice del
2026-09-24 (fase 1 del piano jitter: punti #6 e #8), gia' girato in macchina nel collaudo
a 100 prodotti delle 15:00. Il secondo e' la concorrenza per canale nel worker impulsi
(2026-09-24 15:20), che non era registrata. Il terzo e' la propagazione degli errori BDaq
(2026-09-25).

### A. Spazio canali PCIE-1756 32 DI / 32 DO (#6)

**File modificati:**
- `Models/Pcie1756ChannelMap.cs` — **nuovo**. Fonte unica dello spazio canali: 4 porte DI e
  4 porte DO da 8 bit (32+32 canali), buffer legacy da 8 byte (solo i byte 0..3 sono porte
  reali), canali PCIE-1884 100..103. Helper `IsValidDiChannel`, `IsValidDoChannel`,
  `IsPcie1884Channel`, `IsValidInputChannel`, `IsValidOutputChannel`
- `QtisVisionPanel.csproj` — `Compile Include` del nuovo file
- `Models/AdvantechDeviceManager.cs` — guardie `ReadInputAsync`, `WriteOutputAsync`,
  `SetSimulationInputStateAsync` a 0..31 (i rami PCIE-1884 restano prima e invariati); loop
  `DetectInputChanges1756` e riempimento simulazione limitati a 4 porte; ripristino
  `io_simulation_state.xml` con `Math.Min` e byte 4..7 azzerati; `WriteOutputsAsync` mantiene
  il contratto da 8 byte ma azzera i byte 4..7 e scrive `Write(0, DoPortCount, ...)`; corretto il
  commento su `Read(0, 4, ...)` (porte da 8 bit, non 16); all'avvio su hardware reale log
  `PCIE1756_PORTCOUNT` con le porte dichiarate dal driver (in macchina: `diPorts=4|doPorts=4`)
- `Models/IIODeviceManager.cs` — documentato il buffer legacy da 8 byte sui metodi bulk
- `ViewModels/DigitalIOViewModel.cs` — validazione preventiva dei canali in
  `ReadMappedInputStateAsync`, `SetMappedOutputAsync`, `PulseMappedOutputAsync`, avvio
  heartbeat, richieste timed, `SimulateProductPassageAsync`, allarmi; pagina Test IO a 32+32
  tile; reset heartbeat in `Dispose` protetto (`HEARTBEAT_SHUTDOWN_RESET_FAILED`)
- `MainWindow.xaml.cs`, `Views/AlarmNotificationWindow.xaml.cs` — parser `DOnn` degli allarmi
  limitato a DO00-DO31 (sostituito solo il letterale 64)
- `ViewModels/EjectionAlarmCardViewModel.cs` — testi e validatore (percorso non usato a runtime)
- `Cls_Config/Calss_structure/ConfigClassStructure.cs` — default `BlockingAlarmOutput` /
  `NonBlockingAlarmOutput` vuoti: campi legacy, non letti dal runtime dalla 3.0.7.8

**Motivo:** la scheda ha 32 DI e 32 DO, ma il codice accettava 0..63. Un canale DO32-63 (per
esempio i default DO40/DO41) superava la validazione, impostava un bit fantasma nella cache e
produceva una scrittura BDaq su porte inesistenti, mentre il log dichiarava l'uscita attiva.

### B. Pulizia configurazione uscite (#8)

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — `ValidateTriggerExecutionConfiguration` segnala
  all'avvio i `SignalCode` duplicati indicando quale riga usa `ResolveOutputSignalByCode`, i
  segnali PCIE-1756 mappati su canali 32..63 e i valori legacy fuori range di `Config.xml`.
  Solo log, nessuna rimozione automatica
- `bin/x64/Release/cfg/machine_runtime_config.xml` (PC di sviluppo) — rimosse la riga
  `OUT_HEARTBEAT` su DO20 e la riga vuota `OUT_CAMERA_TOP_REAR`. Backup:
  `cfg/backups/machine_runtime_config-backup-20260924-pre-phase1-cleanup.xml`
- file della macchina — consegnata una versione pulita da copiare a mano: rimosse
  `OUT_HEARTBEAT` su **DO07** (stesso canale del trigger REAR), l'alias
  `OUT_CAMERA_RIGHT_TRIGGER` su DO09, tre righe vuote e il punto intervento `HEARTBEAT`;
  heartbeat DO16 portato a categoria `Alarm`; `DO6` normalizzato in `DO06`

**Motivo:** con due righe per lo stesso segnale il canale effettivo dipende dai flag
`ReservedForRealSignal` di `ResolveOutputSignalByCode`. In macchina bastava cambiare un flag
per spostare l'heartbeat su DO07 e far scattare la camera Rear ogni secondo.

**Da decidere in macchina:** DO17 resta assegnato sia a `OUT_REJECT_SOLENOID` sia a
`OUT_NON_BLOCKING_ALARMS` (entrambi disabilitati). Va scelto il segnale cablato prima di
abilitare lo scarto.

### C. Worker impulsi: concorrenza per canale (2026-09-24 15:20)

**File modificati:**
- `Models/AdvantechDeviceManager.cs` — `OutputPulseLoop` mantiene piu' impulsi attivi su
  canali diversi; la stessa uscita resta serializzata fino alla discesa precedente

**Motivo:** nel collaudo delle 15:00 il worker serializzava anche canali diversi. A circa
60 m/min TOP aspettava la discesa di REAR (30 mm, circa 30 ms, contro 40 ms di impulso): 52
dei 53 `IO_PULSE_RAISE_LATE` hanno questa origine.

**Stato:** non ancora collaudata. Non corregge i 19 impulsi lunghi (50-63 ms) e gli 11
trigger in ritardo (fino a 28,5 mm) del collaudo: coincidono tutti con pause GC
(`IO_POLL_OVERRUN` con `gc0=gc1=gc2=1`, ciclo 19-37 ms).

### D. Errori di scrittura BDaq propagati agli impulsi (2026-09-25)

**File modificati:**
- `Models/AdvantechDeviceManager.cs` — nuovo `TryWriteOutputCore` restituisce l'esito della
  scrittura (ErrorCode diverso da Success, eccezione BDaq, controller assente fuori
  simulazione). `WriteOutputAsync` lo usa senza propagare, quindi heartbeat, allarmi e comandi
  manuali restano invariati. Il worker `Qtis.OutputPulse` invece: salita rifiutata = fail-safe
  basso e `OutputPulseException(Rise)`; discesa rifiutata = fino a 3 tentativi, poi
  `OutputPulseException(Fall)`. Log `IO_OUTPUT_WRITE_FAILED|board=|channel=|port=|error=` al
  posto dei vecchi messaggi con `ex.Message` inline
- `Models/IIODeviceManager.cs` — nuovi `OutputPulsePhase` e `OutputPulseException`
  (`RisingEdgeGenerated` vero solo per fallimento della discesa)
- `Services/CameraTriggerCorrelationTracker.cs` — nuovo `Withdraw(role, productId, pointCode)`
- `ViewModels/DigitalIOViewModel.cs` — `PulseMappedOutputAsync` restituisce `Task<bool>` (fronte
  di salita generato) e registra `IO_PULSE_FAILED` / `IO_PULSE_CANCELLED` invece di
  `Pulse START/END`; il punto `TriggerCamera` senza fronte ritira il ticket visione e registra
  `TRIGGER_NOT_EXECUTED` invece di "Trigger request executed"

**Motivo:** `WriteOutputAsync` intercettava ogni errore BDaq, quindi il worker non vedeva mai
un fallimento. Un fronte non partito veniva registrato come impulso da 40 ms riuscito, e
l'immagine mancava senza traccia. Il ticket restava inoltre in coda e poteva essere preso da
un'immagine non richiesta (per esempio un doppione Rear), attribuendola al prodotto sbagliato.
Il ramo `TRIGGER_PULSE_FAILED` del MultiShot, prima irraggiungibile, ora e' attivo.

**Impatto:**
- Config.xml: nessuna nuova chiave; i default dei due campi allarme legacy diventano vuoti solo
  per file nuovi o senza l'elemento. I valori esistenti restano e generano un warning d'avvio
- machine_runtime_config.xml: nessuna modifica di schema
- Database: nessuno
- API pubblica: nuovi `Pcie1756ChannelMap`, `OutputPulsePhase`, `OutputPulseException`,
  `CameraTriggerCorrelationTracker.Withdraw`; `IIODeviceManager.PulseOutputAsync` termina con
  `OutputPulseException` quando la scheda rifiuta un fronte (firma invariata)
- Comportamento runtime: canali 32..63 rifiutati con warning esplicito; nuovi warning d'avvio
  per duplicati e valori legacy; impulsi rifiutati dalla scheda visibili nel log come
  `IO_PULSE_FAILED` e `TRIGGER_NOT_EXECUTED`. Con scritture riuscite nessun cambiamento
  elettrico: canali, durate (compreso il pavimento a 20 ms, punto #7 ancora aperto) e ordine
  dei fronti restano quelli del 2026-09-24 15:20

**Collaudo:** build Release x64 del 2026-09-25 senza errori. In macchina verificare che non
compaiano `IO_OUTPUT_WRITE_FAILED` e `IO_PULSE_FAILED` e che ogni `Pulse START` abbia il suo
`Trigger request executed`. Con la nuova concorrenza per canale ricontare le immagini Rear
scartate (`VISION_RESULT_UNSOLICITED_DROPPED|job=Rear`, 23 su 100 nel collaudo del 24).

## [2026-09-14] Localizzazione Inspection Configuration e hardening cattura annotata

**File principali:** `Views/InspectionConfigView.xaml(.cs)`,
`ViewModels/InspectionConfigViewModel.cs`, `Converters/BooleanConverters.cs`,
`ServerMessage/ServerMessageStructure.cs`, `scripts/UpdateRuntimeLanguageFiles.ps1`,
`Services/WindowScreenshotHelper.cs`, `Services/CameraDisplayManager.cs`.

**Comportamento:** tutte le label introdotte per matrice camera, classi accettate
e soglia AI sono risolte dal server messaggi. Nomi tecnici e valori persistiti
non vengono tradotti. `PrintWindow` controlla ora esito e handle, il record
mostrato deve ancora coincidere con quello richiesto e il renderer Cognex resta
fallback quando la cattura Windows non e' disponibile.

**Compatibilita':** nessuna nuova chiave Config.xml o ricetta, nessuna modifica
DB. Le nuove chiavi lingua vengono aggiunte in modo idempotente ai cataloghi
ENG/ITA. Versione applicazione `3.1.5.4`, media installer `r18`.

## [2026-09-11] Sotto soglia la classe diventa Unclassify, il punteggio resta quello rilevato

**File modificati:**
- `Models/CameraClassificationResult.cs` — nuova costante `UnclassifiedClassName = "Unclassify"`
- `DataManage/InspectionProcessor.cs` — `ReadClassificationResult` sostituisce anche
  `ClassName`, non solo `State`, quando la vista risulta sotto soglia

**Motivo:** un modello Edge Learning puo' essere addestrato **sulla sola classe OK**. In quel
caso non esistono altre classi in uscita, quindi un punteggio basso non significa "ha vinto
un'altra classe" ma "il modello non riconosce questo pezzo". Prima a database finiva
`RearClassificationLabel = "OK"` con `RearClassificationScore = 0,064`: formalmente esatto,
in analisi fuorviante, perche' un pezzo mai riconosciuto risultava indistinguibile da un OK
a bassa confidenza.

**Comportamento:** sotto soglia la classe diventa `Unclassify`, **il punteggio resta quello
rilevato**. La classe originariamente riconosciuta non va persa: resta nel messaggio di
difetto prodotto dal validatore (`AI classification unclassified [vista]: Class=OK,
Score=0.064 below minimum 0.6`).

L'esito per vista `NC<Vista>AiClassification` resta **0 (NoGood)** e non 3: una confidenza
insufficiente e' un controllo eseguito e non affidabile, non un errore runtime di VisionPro.
Il codice 3 continua a indicare i soli fallimenti di elaborazione dei tool.

**Impatto:**
- Config.xml: nessuno
- Ricetta XML: nessuno
- Database: nessuna modifica di schema. Cambiano i valori scritti in
  `<Vista>ClassificationLabel` quando la soglia scatta; `<Vista>ClassificationScore` invariato
- API pubblica: aggiunta la costante `CameraClassificationResult.UnclassifiedClassName`
- Comportamento runtime: invariato con soglia a 0 su tutte le viste. La scheda vista in HMI
  mostra "Unclassify" con lo stato gia' previsto per `CameraClassificationState.Unclassified`

## [2026-09-11] Immagini annotate: cattura via PrintWindow e rendezvous salvataggio/display

> Supera la sezione "Immagini annotate: render del contenuto invece del viewport dipinto"
> dello stesso giorno. Quella correzione, da sola, non risolveva: il difetto non era solo
> nel COSA si cattura ma anche in CHI ASPETTA CHI.

**File modificati:**
- `Services/WindowScreenshotHelper.cs` — **nuovo**. P/Invoke di `user32!PrintWindow` con
  `PW_CLIENTONLY (0x2)` sull'handle del `CogRecordDisplay`
- `Services/AnnotatedImageCache.cs` — la cache diventa un rendezvous: `CreateRequest` /
  `Store` / `Request.TryWait` / `Request.Dispose` al posto di `TryGet`.
  `AnnotatedImageRenderer` ora prova `Image`, poi `Custom`, poi `Display`
- `Services/CameraDisplayManager.cs` — `CacheAnnotatedImage` cattura con `PrintWindow` e
  indicizza sul record RADICE; priorita' dispatcher da `ContextIdle` a `Background`
- `SaveImage/ISaveImage.cs` — `PendingRecordSave.ViewsWithAnnotatedImage` (HashSet) diventa
  `AnnotatedImageRequests` (Dictionary di Request); nuovi `GetResultRecord`,
  `TryQueueAnnotatedForView`, `DisposeAnnotatedRequests`; routing Top3D
- `MainWindow.xaml.cs` — ordine alias in `FindCompanionResult` per la vista side
- `QtisVisionPanel.csproj` — `<Compile Include="Services\WindowScreenshotHelper.cs" />`

**Motivo:** il JPG `_Z.jpg` continuava a essere salvato senza grafica. I log di campo hanno
mostrato che il salvataggio cercava l'immagine in cache circa 8 ms dopo l'esito
dell'ispezione, mentre la cattura era rinviata a `ContextIdle` dopo l'aggiornamento della
vista. La cache non poteva contenere quel record: conteneva al massimo il pezzo precedente.
Nei log non compariva nessun `ANNOTATED_CACHE_SKIPPED` ne' `ANNOTATED_CACHE_FAILED` — pur
essendo entrambi a Warn — mentre `ANNOTATED_CACHE_STORED` e' a Debug e `NLog.config` filtra
a `minlevel="Info"`: la cattura riusciva, semplicemente troppo tardi perche' qualcuno la
usasse. Era una corsa impossibile da vincere con quell'ordinamento.

**Le cinque modifiche sostanziali:**

1. **Cattura con `PrintWindow` invece di `CreateContentBitmap`.** Legge la superficie
   realmente dipinta dal controllo ActiveX, aggirando il rettangolo nero che una cattura
   GDI ordinaria restituisce attraverso il `WindowsFormsHost` WPF. Include quindi zoom,
   rotazione e grafica esattamente come li vede l'operatore.

2. **La cache diventa un rendezvous.** `CreateRequest(role, record)` restituisce una
   richiesta gia' completata se il render e' gia' avvenuto (caso producer-first), altrimenti
   la registra come pendente (caso consumer-first); `Store` completa tutte le pendenti per lo
   stesso record e ritorna quante ne ha completate. Chiude la corsa in entrambi i versi,
   invece di limitarsi a fallire quando arriva prima il consumatore.

3. **Chiave sul record RADICE, non sul sottorecord mostrato.** Prima `Store` indicizzava su
   `tmpRecord` (il sottorecord a display) mentre il salvataggio cercava per record radice:
   due chiavi diverse per lo stesso pezzo. Ora entrambe le parti usano il record radice,
   risolto lato salvataggio dal nuovo `GetResultRecord(viewName)`.

4. **L'attesa vive solo sul worker immagini.** Il ciclo d'ispezione prenota la richiesta e
   prosegue senza attendere. Il worker attende con una scadenza **condivisa fra tutte le
   viste** (`AnnotatedRenderTimeoutMs = 1500 ms`, calcolata una volta per pezzo): una camera
   non disponibile non moltiplica l'attesa per il numero di viste. Ne' l'ispezione ne' il
   thread UI vengono mai bloccati.

5. **Routing Top3D nel salvataggio.** Con Top3D attivo `T` mappa su `top3d` e `F`/`S` su
   `top2d`; `GetResultRecord` e `GetSavedRecord` per `F`/`S` ripiegano su `_topSaveRecord`.

**Correzione separata:** `FindCompanionResult(companions, "left", "side")` diventa
`("side", "left")`. L'ordine degli alias determina la priorita' di risoluzione, quindi la
vista side veniva risolta preferendo il ruolo "left".

**Nota sulle costanti di `CreateContentBitmap`:** la documentazione VisionPro distingue
`Display` (replica del viewport visibile), `Image` (immagine completa non scalata, **con**
le annotazioni) e `Custom` (rettangolo e dimensione richiesti). `Image` non e' quindi
l'immagine grezza senza grafica, come indicato per errore in precedenza.
`AnnotatedImageRenderer` prova ora nell'ordine `Image`, `Custom`, `Display` e resta in uso
per `CaptureDisplayImageBytes`, cioe' per i percorsi di salvataggio diversi da quello
annotato principale, che ora passa da `PrintWindow`.

**Nuovi codici di log:**
- `SAVE_IMAGE_ANNOTATED_READY|file=|role=|bytes=` — immagine annotata accodata (Info)
- `SAVE_IMAGE_ANNOTATED_TIMEOUT|view=|role=|timeoutMs=` — scaduta l'attesa, si salva il
  grezzo (Warn). Sostituisce `SAVE_IMAGE_ANNOTATED_CACHE_MISS`, che era a Info
- `ANNOTATED_CACHE_STORED|role=|w=|h=|completedRequests=` — quante richieste ha soddisfatto
  la cattura (Debug, non visibile con `minlevel="Info"`)

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `AnnotatedImageCache` cambia contratto (`TryGet` rimosso, aggiunti
  `CreateRequest`/`Store`/`Request`); `PendingRecordSave.ViewsWithAnnotatedImage` sostituito
  da `AnnotatedImageRequests`. Entrambi i tipi sono `internal`/privati al progetto
- Comportamento runtime: il `_Z.jpg` contiene la grafica VisionPro. In caso di ritardo oltre
  1500 ms si salva il grezzo, come prima, ma ora con un warning esplicito
- Dipendenze: nuova P/Invoke su `user32.dll`. Il ciclo di vita dell'handle del controllo e'
  quello del controllo stesso, non viene creato ne' distrutto nulla

## [2026-09-11] Soglia di confidenza AI per vista: sotto soglia il pezzo e' non verificato

**File modificati:**
- `Cls_Config/Calss_structure/RecipeParameters.cs` — nuovo campo
  `RecipeCameraInspectionView.MinimumClassificationScore`
- `DataManage/CameraInspectionMatrix.cs` — `CameraClassificationRule.MinimumScore`,
  `CameraClassificationPolicy.GetMinimumScore(role)`, mappatura ricetta <-> regole
- `DataManage/IToolBlockValidator.cs` — `ValidateAiClassificationAsync` applica la soglia
- `ViewModels/InspectionConfigViewModel.cs` — proprieta' di riga, caricamento, salvataggio
  ricetta e persistenza su `cfg_view_classification`
- `Views/InspectionConfigView.xaml` — campo "Confidenza minima" accanto alle classi accettate
- `Database/Cls_InitializzeDb.cs` — colonna `MinimumScore` + helper `EnsureColumnAsync`

**Motivo:** il tool Classify (ViDi EL) restituiva la classe senza che nessuno guardasse la
confidenza. Un pezzo classificato "OK" con punteggio basso passava come conforme, ma un
punteggio basso non e' un OK debole: puo' essere un prodotto diverso da quello di ricetta,
o una condizione che il modello non ha mai visto in addestramento. In entrambi i casi il
pezzo non e' verificato e non deve passare.

**Comportamento:** se la classe e' accettata ma il punteggio e' sotto la soglia della vista,
la classificazione viene declassata a non riconosciuta e il pezzo va a scarto con messaggio
`AI classification unclassified [<vista>]`, distinto da quello di classe non accettata.
Se la soglia e' impostata e il job non espone lo Score, il caso conta come non verificato:
trattarlo come conforme vanificherebbe la soglia proprio sui job privi di quell'output.

La soglia e' **per vista** perche' ogni modello Edge Learning e' addestrato per camera e la
separazione tra le classi non e' la stessa su tutte. **0 = soglia disattivata**, che e' il
default: un impianto che non la configura si comporta esattamente come prima.

Salvando da una schermata filtrata, le viste non mostrate conservano la soglia di partenza
invece di azzerarla, per non disattivare il controllo in modo silenzioso sulle altre camere.

**Impatto:**
- Config.xml: nessuno
- Ricetta XML: nuovo campo `MinimumClassificationScore` per vista (assente = 0 = disattivata)
- Database: nuova colonna `cfg_view_classification.MinimumScore` (DOUBLE NOT NULL DEFAULT 0).
  Migrazione idempotente via `EnsureColumnAsync`, che interroga `information_schema` da C#:
  la variante SQL con `SET @ddl` + `PREPARE` richiederebbe `AllowUserVariables` nella
  stringa di connessione — non impostata su quella usata all'avvio — e avrebbe bloccato
  l'inizializzazione del database. `ADD COLUMN IF NOT EXISTS` non e' utilizzabile: MySQL 5.7
  non lo supporta. Un fallimento della migrazione viene loggato
  (`DB_ENSURE_COLUMN_FAILED`) senza impedire l'avvio.
- API pubblica: aggiunti `CameraClassificationPolicy.GetMinimumScore` e
  `CameraClassificationRule.MinimumScore`; nessuna firma esistente modificata
- Comportamento runtime: invariato finche' la soglia resta 0 su tutte le viste

## [2026-09-11] Immagini annotate: render del contenuto invece del viewport dipinto

**File modificati:**
- `Services/AnnotatedImageCache.cs` — nuova classe `AnnotatedImageRenderer`: produce il
  bitmap annotato con l'overload `CreateContentBitmap(content, contentRect, bitmapSize)`,
  con ripiego sull'overload a un argomento
- `Services/CameraDisplayManager.cs` — `CacheAnnotatedImage` usa il renderer condiviso; i
  fallimenti passano da Debug a Warn e distinguono `record-superseded` da `exception`
- `SaveImage/ISaveImage.cs` — `CaptureDisplayImageBytes` usa il renderer condiviso

**Motivo:** il salvataggio annotato (`_Z.jpg`) falliva in modo persistente, producendo
immagini del solo colore di fondo o `SAVE_IMAGE_ANNOTATED_CACHE_MISS`. La causa non era
il *quando* si catturava — thread, priorita' del dispatcher, Refresh() forzati — ma il
*cosa*: l'overload a un argomento di `CreateContentBitmap` restituisce il viewport **cosi'
come e' dipinto**. Se il controllo ActiveX non ha ancora ridisegnato (fuori schermo, scheda
non attiva, ciclo di paint non ancora passato) il risultato e' un rettangolo uniforme del
colore di fondo. L'overload a tre argomenti chiede invece a VisionPro di renderizzare una
regione del contenuto in un bitmap nuovo: e' un render esplicito, indipendente dallo stato
di paint.

Verificato per riflessione su `Cognex.VisionPro.Display.Controls.dll` (VisionPro 9.3):
`CogDisplayContentBitmapConstants` espone solo `Image = 0`, `Display = 1`, `Custom = 2`.
I valori `InteractiveGraphics` / `StaticGraphics` suggeriti da fonti esterne **non esistono**
in questa versione e non compilano.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna modifica a firme esistenti; aggiunta `AnnotatedImageRenderer` interna
- Comportamento runtime: il JPG annotato viene reso alla risoluzione nativa dell'immagine
  (lato maggiore) invece che a quella della finestra a video; i fallimenti di cattura sono
  ora visibili a livello Warn con il motivo preciso

# Code Changes Log

## [2026-09-24] Worker encoder deterministici, impulsi monotoni e compare PCIE-1884

**File principali:**

- `Models/AdvantechDeviceManager.cs`
- `Models/IIODeviceManager.cs`
- `Services/MachineController.cs`
- `ViewModels/DigitalIOViewModel.cs`
- `Documentation/MachineHardware/encoder-trigger-deterministic-workers-and-pcie1884-compare-2026-09-24.md`

**Modifiche:**

- separati tracking/MultiShot e telemetria UI in due worker encoder dedicati;
- aggiunti ring buffer preallocati con diagnostica overflow;
- rimosse allocazioni LINQ dal percorso ordinario di `UpdateEncoderPosition`;
- aggiunto worker impulsi ad alta priorita con coda fissa e deadline monotona;
- eliminato `Task.Delay` dagli impulsi camera VirtualConveyor e TimedFromPhotocell;
- aggiunto contratto DAQNavi per validare e, dopo collaudo e cablaggio, armare la FIFO compare
  della PCIE-1884;
- nessuna nuova chiave XML, modifica ricetta o migrazione database.

**Da verificare in macchina:** almeno 100 prodotti con Top/Rear/Side e confronto dei log timing
secondo la guida dedicata.

## [2026-09-10] Ripristino immagini annotate senza bloccare l'ispezione

**File modificati:**
- `SaveImage/ISaveImage.cs`
  - corretto il ciclo di inizializzazione del `CogRecordDisplay` off-screen: `BeginInit()`
    precede ora ogni assegnazione di proprieta', `EndInit()` precede la creazione degli handle;
  - eliminata la causa di `System.Windows.Forms.AxHost+InvalidActiveXStateException` osservata
    su ogni vista dei pezzi NOK;
  - il rendering annotato e' stato spostato nella coda record bounded gia' esistente. Il thread
    d'ispezione cattura il record del pezzo e accoda il lavoro senza attendere il dispatcher UI;
  - il worker passa al renderer l'esatto `ICogRecord` dello snapshot, evitando che un refresh
    successivo della HMI associ al file `_Z.jpg` il pezzo seguente;
  - aggiunti `SAVE_IMAGE_CAPTURE_DISPLAY_READY` e il tipo completo dell'eccezione nei warning
    `SAVE_IMAGE_ANNOTATED_FAILED`;
  - invariato il fallback: se il render fallisce, `_Z.jpg` viene scritto dal raw e `_A.bmp` resta
    disponibile. La produzione non viene arrestata.
- `Docs/Manual/02_Avvio_Stop_Produzione.md`
  - chiarito il contratto `_A.bmp` / `_Z.jpg` e corretta la tabella della sequenza operativa.
- `Documentation/MachineHardware/vision-trigger-result-correlation-and-ui-decoupling-2026-09-02.md`
  - documentato il rendering annotato off-screen asincrono e il fallback raw.

**Impatto:**
- Config.xml: nessuno;
- ricette/XML: nessuno;
- database: nessuno;
- runtime macchina: nessuna modifica a trigger, tracking, scarto o risultato ispezione;
- diagnostica: sui primi NOK deve comparire una sola riga
  `SAVE_IMAGE_CAPTURE_DISPLAY_READY`, seguita da `SAVE_IMAGE_OK` con
  `annotated_source=display-record-graphics` per le viste renderizzate.

## [2026-09-04] Cadenza del thread di polling I/O e icone ispezione vettoriali

**File modificati:**
- `SaveImage/ISaveImage.cs` — nuovo `TryRenderAnnotatedFromRecord`: marshalla il
  rendering sul thread UI con attesa limitata a `AnnotatedRenderTimeoutMs` (1500 ms) e
  restituisce se il JPG annotato e' stato davvero prodotto. Usa `SaveImageFromRecord`
  con il record snapshottato del pezzo, non `display.Record`, perche' il pannello puo'
  gia' mostrare il prodotto successivo.
- `SaveImage/ISaveImage.cs` — `_SaveImage` accetta `renderAnnotatedFromDisplay`
  (default false). Se true, il JPG `_Z` viene renderizzato dal `CogRecordDisplay` sul
  thread UI con la grafica VisionPro; le viste gia' prodotte sono elencate in
  `PendingRecordSave.ViewsWithAnnotatedImage` e il thread di scrittura non le
  sovrascrive col grezzo. Passano `true` solo `CheckFailPercentageSave` e
  `ForceDiagnosticSave`. `annotated_source` nel log riporta ora
  `display-record-graphics` o `captured-record-raw` secondo il caso.
- `Services/CameraTriggerCorrelationTracker.cs` — la coda dei ticket per ruolo non viene
  piu' svuotata a ogni nuovo trigger: e' una FIFO con profondita' massima
  `MaxPendingTicketsPerRole = 4`. In eccedenza si scarta il ticket piu' vecchio, non
  quelli legittimi in volo. Il controllo MultiShot usa `LastRegisteredProductId` invece
  della testa della coda.
- `ViewModels/DigitalIOViewModel.cs` — `OnCounterChanged` aggiorna la UI del contatore
  encoder a 10 Hz invece che alla cadenza del poll (500 Hz), con passaggio sempre
  consentito a nastro fermo (`RawFrequencyHz < 0.1`) perche' il valore a riposo mostrato
  sia quello vero. Closure che cattura locali invece dell'oggetto evento, concatenazione
  al posto dell'interpolazione. Il percorso di controllo macchina
  (`UpdateEncoderPosition`, MultiShot) resta a piena cadenza.
- `DataManage/IToolBlockValidator.cs` — messaggio di difetto Logo riscritto: e' una
  soglia minima di corrispondenza, non uno scostamento massimo. Logica invariata.
- `App.config` — attivati `gcServer` e `gcConcurrent` (Background Server GC).
- `App.xaml.cs` — nuovo `ConfigureGarbageCollectorLatency()` chiamato a inizio
  `Application_Startup`: imposta `GCLatencyMode.SustainedLowLatency` via codice ed
  emette `GC_SERVER_DISABLED` se `GCSettings.IsServerGC` e' false, cioe' se
  `QtisVisionPanel.exe.config` non e' stato distribuito accanto all'eseguibile.
- `Models/AdvantechDeviceManager.cs` — `IO_POLL_OVERRUN` riporta `gc0/gc1/gc2`
  (collection avvenute nel ciclo) e `IO_POLL_STARTED` riporta `gcServer`/`gcLatency`.
- `Views/UserControls/DisplayRecord/CameraOutcomeBackground.cs` (nuovo) — tinte e
  applicazione dell'esito pezzo alla card feature di una vista.
- `Views/UserControls/DisplayRecord/*.xaml` (7 viste) — la Border che racchiude
  etichetta e icone prende `x:Name="FeatureCardBorder"` e `Background="Transparent"`.
- `Services/CameraDisplayManager.cs` — nuovi `ApplyOutcomeBackgroundToAllViews(bool?)`
  (reset a neutro) e `ApplyPerViewOutcomeBackground(InspectionResult)` con il resolver
  `ResolveViewOutcome`, invocati da `UpdateCameraStatusIndicators`. La card di ogni
  vista riflette l'esito di quella vista: feature valide e classificazione non Failed.
  Side/Left e Rear/Right condividono l'esito perche' sono viste alternative sullo
  stesso job. Vista non partecipante o ruolo non classificabile restano neutri.
- `MainWindow.xaml.cs` — `Window_Loaded` chiama `RefreshCameraFeatureVisibility()` e
  imposta le card a neutro, quando le viste camera esistono davvero.
- `Services/CameraTriggerCorrelationTracker.cs` — nuovi `IsTriggerTrackingActive` e
  `IsAwaitingResult`: espongono se a una camera sono stati inviati trigger di recente
  e se ha un trigger emesso rimasto senza risultato.
- `MainWindow.xaml.cs` — `timedOutRoles` usa quei due predicati quando la correlazione
  trigger e' attiva, con fallback al criterio storico (eta' dell'ultimo risultato)
  quando i trigger non sono gestiti dall'HMI.
- `MainWindow.xaml.cs` — nuovo campo `_pendingPairSinceUtc`. La condizione
  `pairTimedOut` del watchdog VisionPro non misura piu' l'eta' assoluta dell'ultima
  coppia elaborata ma da quanto un risultato e' effettivamente in attesa di essere
  appaiato. Azzerato in `RegisterProcessedInspectionPair` e ai due punti di reset
  (avvio run continua e fine recovery), armato in `RegisterVisionHeartbeat`.
- `Database/CounterManager.cs` — `CounterDbFlusher`: callback del timer estratta in
  `OnTimerTick` con try/catch totale, flag `_disposed`, `Release` protetta da
  `ObjectDisposedException`, `Dispose` che drena il flush in volo prima di
  distruggere il semaforo, e flush finale che attende il proprio turno invece di
  saltarlo.
- `Models/AdvantechDeviceManager.cs` — `WaitUntilNextPoll` non usa piu' `Thread.Sleep(1)`
  per intervalli di poll <= 5 ms (`MaxSpinOnlyPollIntervalMs`): attesa con
  `Thread.SpinWait` + `Thread.Yield`. Aggiunti `SelectCounterChannel` /
  `SelectFreqMeterChannel`: i setter `Channel`/`ChannelStart` dei controlli BDaq
  vengono ora invocati solo a canale realmente cambiato, non a ogni lettura.
  Cache invalidata (`-1`) dove i controlli vengono ricreati.
- `Resources/InspectionIcons.xaml` (nuovo) — 16 icone vettoriali `DrawingImage`,
  una per ispezione, nello stile esistente (pacco `#7F7F7F`, elemento `#E30613`).
- `Converters/BooleanConverters.cs` — `ImagePathConverter` risolve le chiavi
  `InspIcon_*` dal dizionario applicativo, con fallback sui PNG storici.
- `App.xaml`, `QtisVisionPanel.csproj` — dizionario icone incluso e compilato come Page.
- `ViewModels/InspectionCounterViewModel.cs`, `ViewModels/InspectionConfigViewModel.cs`,
  `Views/UserControls/DisplayRecord/*.xaml` — mappatura icone corretta e migrata ai vettoriali.

**Motivo:**
-26. *Ritorno alla configurazione funzionante del 2026-09-09 14:25.* Dopo quel log — T, F e
   R tutte con `display-record-graphics`, zero fallimenti — sono state provate tre varianti,
   tutte peggiorative:
   (a) display fuori schermo dedicato: impossibile, `CogRecordDisplay` e' ActiveX e istanziato
       da codice resta passivo (`InvalidActiveXStateException`);
   (b) rendering spostato sul worker immagini: avveniva secondi dopo la decisione sul pezzo,
       con display gia' passato ad altri prodotti — falliva anche la TOP;
   (c) ciclo di tentativi sulla cattura: la SIDE ha continuato a fallire in modo
       deterministico su ogni pezzo, quindi la ripetizione non aiutava.
   Ripristinata la cattura SINGOLA dal display vivo con scambio del Record, `Refresh()` e
   drain `ContextIdle`, e rimossi `AnnotatedCaptureAttempts` / `TryCaptureDisplayImageBytes`.
   Effetto collaterale accettato dall'utente: la vista TOP si aggiorna due volte durante il
   salvataggio di uno scarto.
   APERTO: nel log 2026-09-10 la SIDE produce un'immagine di solo sfondo. Non e' ancora
   accertato se dipenda dal controllo che non dipinge o dal contenuto, e le prove di quel
   giorno erano tutte in simulazione (`Synthetic FrameGrabber`) su una macchina a 12 core e
   2 camere, quindi non confrontabili con il campo.

-25. *Ripristinato il rendering annotato nel thread di elaborazione.* La causa dei
   fallimenti non era il criterio di cattura ma il MOMENTO: il rendering era stato spostato
   sul worker immagini (`PendingRecordSave.RenderAnnotatedFromDisplay`), quindi avveniva
   secondi dopo la decisione sul pezzo — con il display della vista gia' passato ad altri
   prodotti e la coda del dispatcher vuota, il controllo non ridisegnava e la cattura
   tornava uniforme. Nel log 2026-09-10 13:32 falliscono anche viste che prima riuscivano,
   e il ritardo fra `Processing completed` e `SAVE_IMAGE_ANNOTATED_RENDER` e' di secondi.
   Il rendering torna in `_SaveImage`, subito dopo la decisione sul pezzo, come nella
   versione che in campo produceva `display-record-graphics` su tutte le viste (log
   2026-09-09 14:25). Il worker riceve `ViewsWithAnnotatedImage` e si limita a non
   sovrascrivere con il grezzo cio' che e' gia' stato prodotto.
   Effetto collaterale noto e accettato dall'utente: la vista TOP si aggiorna due volte
   durante il salvataggio di uno scarto.

-24. *Solo la prima vista produceva l'immagine annotata.* Log 2026-09-10 12:22, pezzi 88 e
   89: view=T riesce (`display-record-graphics`), view=F fallisce con immagine uniforme —
   stesso stato del controllo in entrambi i casi (`referenceDisplay=True`, `size=780x265`,
   `savedRecord=True`). La discriminante e' l'ORDINE: il drain `ContextIdle` non attende il
   repaint, esegue cio' che e' in coda. La prima cattura del ciclo trova il refresh della
   vista appena aggiornata e quindi il controllo dipinge; le successive trovano la coda
   gia' svuotata dalla precedente e leggono una superficie non ridisegnata.
   Sostituita la cattura singola con un ciclo di massimo `AnnotatedCaptureAttempts` = 5
   tentativi che VERIFICA il risultato tramite `IsUniformBitmap` e ritenta, invece di
   affidarsi al fatto che un singolo drain cada al momento giusto. Aggiunto
   `TryCaptureDisplayImageBytes` (non solleva su bitmap uniforme) e la diagnostica
   `SAVE_IMAGE_ANNOTATED_RETRY`. Il ripiego sul grezzo resta se tutti i tentativi falliscono.

-23. *Display fuori schermo impraticabile: CogRecordDisplay e' ActiveX.* Il tentativo -22
   fallisce con `System.Windows.Forms.AxHost+InvalidActiveXStateException` su tutte le
   viste (log 2026-09-09 15:22, pezzi 37/38/39). `CogRecordDisplay` deriva da AxHost:
   istanziarlo da codice lo lascia in stato passivo e qualunque accesso alle sue
   proprieta' solleva l'eccezione. Ne' `ISupportInitialize.BeginInit/EndInit` ne'
   `CreateControl()` su una Form mostrata fuori schermo lo portano in stato attivo:
   l'attivazione OLE avviene solo per i controlli realizzati dal XAML delle viste camera.
   Ripristinata quindi la cattura dal display VIVO risolto da `GetDisplayForView`, con lo
   scambio temporaneo del Record e il drain `DispatcherPriority.ContextIdle` che ne
   completa il ridisegno — l'unica combinazione che in campo ha prodotto immagini
   annotate corrette (log 14:25, `annotated_source=display-record-graphics` su tre viste
   senza fallimenti). Rimosso il blocco del display fuori schermo e il relativo cleanup.
   COSTO ACCETTATO: il drain esegue i refresh accodati a `DispatcherPriority.Background`,
   quindi durante il salvataggio di uno scarto la vista si aggiorna a schermo. E' inerente
   al catturare dal controllo condiviso con la HMI.

-22. *Cattura annotata spostata su display fuori schermo.* La soluzione precedente
   catturava dal display dell'operatore, e per farlo dipingere svuotava la coda del
   dispatcher con `Invoke(..., DispatcherPriority.ContextIdle)`. `CameraDisplayManager`
   accoda i refresh delle viste a `DispatcherPriority.Background`, che e' PIU' ALTA di
   ContextIdle: quel drain li eseguiva quindi tutti prima di ritornare. Conseguenze: la
   vista TOP si aggiornava a vista durante il salvataggio, e — non osservato ma possibile
   — un refresh poteva riassegnare `display.Record` fra lo scambio e `CreateContentBitmap`,
   facendo catturare il pezzo sbagliato e lasciando poi il pannello su un record vecchio
   per via del ripristino nel `finally`.
   Ora la cattura usa un `CogRecordDisplay` dedicato dentro una `Form` posizionata a
   (-32000,-32000) e mostrata: mostrarla e' necessario, un controllo mai realizzato non ha
   superficie e `CreateContentBitmap` restituirebbe il colore di fondo. Il display
   dell'operatore non viene piu' toccato: niente scambio di Record, niente drain, niente
   rientranza. La Form viene chiusa dal thread UI in `Dispose`.
   Corretto inoltre in `CameraDisplayManager` l'ordine `Fit(true)` / `Record =`: era
   invertito e adattava la vista al record precedente.
-21. *Board/Channel/Polarity del punto di intervento non persistiti.* L'editor leggeva e
   scriveva tramite `ResolveOutputSignalByCode`, che espande il codice negli alias
   (REAR<->RIGHT, SIDE<->LEFT) e ordina per `HasPhysicalMapping` prima della corrispondenza
   esatta (`CandidateIndex` e' solo il quarto criterio). Con la riga
   `OUT_CAMERA_REAR_TRIGGER` priva di canale e l'alias `OUT_CAMERA_RIGHT_TRIGGER` mappato,
   l'editor operava sulla riga dell'ALIAS: il valore digitato finiva su un'altra camera e
   la riga del segnale realmente referenziato dal punto restava incompleta a ogni
   salvataggio. E' l'origine del warning `Machine outputs contain 3 incomplete row(s)`
   presente in tutti i log di avvio. Aggiunto `ResolveExactOutputSignalByCode`, usato da
   `ResolveSelectedInterventionOutput` e `GetOrCreateSelectedInterventionOutput`: l'editor
   agisce sulla riga con codice esattamente uguale e, se manca, la crea. Il resolver con
   alias resta invariato per il runtime, dove la tolleranza sui nomi e' voluta.
   VERIFICATO ED ESCLUSO: `BuildRuntimeConfigurationFromView` non perde le modifiche —
   `BuildDefault` clona le collezioni ricevute dalla vista.
-20. *Cattura riuscita solo a record gia' mostrato.* Log 2026-09-07 15:06-15:07: su quattro
   catture con `displayRecord=True|displayImage=True|savedRecord=True` una sola e' riuscita.
   La discriminante e' `mustRestore`: quando il record era gia' quello a display non si
   assegna nulla e si cattura una superficie gia' dipinta (riesce); quando lo si assegna
   ora, `CreateContentBitmap` restituisce un'immagine uniforme. Il ridisegno del controllo
   VisionPro si completa sulla coda del dispatcher, ferma finche' il thread UI e' occupato
   dalla nostra Invoke sincrona. Aggiunta, solo nel ramo `mustRestore`, una
   `Dispatcher.Invoke(() => {}, DispatcherPriority.ContextIdle)` che svuota la coda fino al
   render completato prima della cattura. NON e' `Application.DoEvents()`: non pompa i
   messaggi Win32 di input e non riapre la rientranza nei gestori dei risultati VisionPro.
   Se dovesse risultare insufficiente, l'alternativa e' catturare i byte JPEG in
   `CameraDisplayManager` a valle del render (priorita' ContextIdle) e tenerli in cache per
   ruolo, lasciando al salvataggio solo la scrittura.
-19. *Immagine annotata sfasata di un pezzo rispetto alla raw.* Con il display vivo la
   grafica veniva salvata, ma catturando il pannello "com'e'": `CameraDisplayManager`
   aggiorna il display con `InvokeAsync`, quindi al momento del salvataggio puo' mostrare
   ancora il pezzo precedente, mentre la raw usa `recordSnapshot` del pezzo corrente. Da
   qui la coppia disallineata. Ora si assegna il record del pezzo corrente prima della
   cattura (`SaveImageFromRecord`, che ripristina il record precedente nel `finally`).
   Il blu delle prove precedenti NON era causato da questa sostituzione ma dal display
   orfano risolto al punto -15: con il controllo vivo l'assegnazione renderizza. La
   cattura "com'e'" resta solo se manca il record del pezzo, con warning
   `SAVE_IMAGE_ANNOTATED_DISPLAY_AS_IS`.
-18. *REGRESSIONE INTRODOTTA E CORRETTA: offset camera e trigger per ricetta spariti.*
   Nascondendo i trigger delay avevo legato la Visibility del GroupBox
   `Sub_entry_Trigger_Config` (riga 1420) a `CanShowTriggerDelaySection`. Quel GroupBox
   pero' **non si chiude fino a riga ~1859** e racchiude anche l'Expander dei trigger
   camera per ricetta, gli offset posizione camera (`*OffsetMm`) e i parametri MultiShot:
   in modalita' io-driven spariva tutto. Il GroupBox torna alla Visibility di ruolo
   (`CanEditTriggerDelay`) e `CanShowTriggerDelaySection` e' applicata al solo blocco
   interno `Sub_entry_TriggerDelayFields`. Aggiunto commento in testa al GroupBox perche'
   l'errore non si ripeta: la sua estensione non e' evidente leggendo l'apertura.
   Annullata anche la modifica -17 sulla sezione Top3D, che torna visibile solo con
   ispezione 3D.
-17. *Percorsi record VisionPro non piu' modificabili.* `LasRunParam.LastRunView1..4`
   esiste in `Config.xml` ed e' letto da `CameraDisplayManager` per scegliere il
   sub-record da mostrare, ma nessuna schermata lo esponeva: l'editor stava in una copia
   del progetto uscita dal tracciamento con `43f865b` (2026-07-02). Aggiunta
   `PreferenceViewModel.RuntimeLastRunViews` e i quattro campi in `PreferenceView.xaml`.
   Inoltre `Top3DPrimaryLastRunView` / `Top3DSecondaryLastRunView` erano dentro il
   GroupBox `Sub_entry_ThreeDSection`, nascosto da `CanShowThreeDSection` su macchine
   senza Top3D: la condizione e' scesa dal GroupBox al solo blocco delle soglie
   profilometro (`Sub_entry_ThreeDThresholds`), cosi' i percorsi record restano visibili
   ovunque. Nessuna riga della griglia aggiunta: rows 0-9 erano gia' tutte occupate.
-16. *Aggiornamento schema configurazione mai persistito.* `EnsureLatestRuntimeBindingsSchema`
   aggiornava `_machineConfiguration` in memoria, poi `MarkConfigurationSaved()` marcava la
   configurazione come pulita senza averla scritta; l'unica `Save` presente puntava al
   template, non alla configurazione attiva. I campi mancanti venivano quindi ricreati a
   ogni avvio e mai persistiti in `machine_runtime_config.xml` — il messaggio
   `upgraded to include timed-trigger runtime bindings` compare infatti in TUTTI i log di
   avvio. Ora la configurazione attiva viene salvata quando lo schema cambia.
-15. *CAUSA RADICE dell'immagine annotata vuota: display orfano.* Il log ha chiuso la
   diagnosi: `displayRecord=False|displayImage=False|savedRecord=True` con
   `visible=True|handleCreated=True`. Il controllo catturato era vuoto pur essendo
   realizzato e visibile, mentre a schermo l'immagine annotata c'era: due oggetti
   diversi. `MainWindow._recordDisplays` viene popolato in `Window_Loaded`, ma le viste
   camera vengono ricaricate subito dopo ("Viste telecamere caricate", nel log ~200 ms
   piu' tardi). I `CogRecordDisplay` a schermo sono quindi nuove istanze e il dizionario
   continua a puntare quelle vecchie, orfane: mai piu' aggiornate da
   `CameraDisplayManager`, che scrive sui singleton `*CameraView.Instance`.
   `GetDisplayForView` risolve ora dalle istanze vive, con la stessa mappa vista ->
   controllo di `Window_Loaded`, e ripiega sul dizionario solo se il singleton e' nullo.
   NOTA: `MainWindow._recordDisplays` e' usato anche altrove; se altre funzioni mostrano
   comportamenti analoghi, la causa e' la stessa.
-14. *Nessuna verifica sull'immagine catturata.* `CaptureDisplayImageBytes` salvava
   qualunque cosa `CreateContentBitmap` restituisse, quindi un display non dipinto
   produceva un file del solo colore di fondo senza alcun segnale. Ora un bitmap di
   colore uniforme viene rifiutato con eccezione: lo slot di scrittura e' rilasciato dal
   `catch` gia' presente, il chiamante logga `SAVE_IMAGE_ANNOTATED_FAILED` e il thread di
   scrittura produce comunque il grezzo. `IsUniformBitmap` non confronta con un colore
   atteso — il fondo del display e' `#0A0B60`, non blu puro, e una costante sbagliata non
   scatterebbe mai — ma verifica solo l'uniformita', campionando a griglia via `LockBits`.
-13. *Immagine annotata blu con display visibile e dimensionato.* Il log ha escluso le
   ipotesi precedenti: `SAVE_IMAGE_ANNOTATED_RENDER|visible=True|handleCreated=True|
   size=780x265` e `annotated_source=display-record-graphics`, quindi il rendering
   avveniva su un controllo valido. `SaveImageFromRecord` e `CaptureDisplayImageBytes`
   risultano identici alla versione funzionante del 02/09 (`064fa9f`), cosi' come
   `GetSavedRecord`. La differenza introdotta da `834c6fb` e' in
   `CameraDisplayManager`: una guardia sulla sequenza
   (`DISPLAY_STALE_UPDATE_SKIPPED`) puo' uscire prima di assegnare
   `_<ruolo>DisplaySaveRecord`, che quindi ripiega su un record privo di immagine.
   Assegnarlo al display svuotava la vista e la cattura restituiva il solo sfondo.
   Ora, se il pannello ha gia' contenuto (`display.Record` o `display.Image` non nulli),
   si cattura la vista COSI' COM'E' con `SaveImageFromRecordDisplay`, senza sostituire
   il record; la sostituzione resta solo per il display mai mostrato. Il log riporta ora
   `displayRecord`, `displayImage` e `savedRecord` per distinguere i due rami.
-12. *Immagine annotata ancora vuota dopo il repaint.* Il solo `display.Refresh()` non e'
   bastato. Confronto con l'implementazione pre-`834c6fb` (`TrySaveFromSavedRecord`,
   invocata dentro `dispatcher.Invoke`): quella risolveva `GetSavedRecord(viewName)` e
   `GetDisplayForView(viewName)` **sul thread UI, nell'istante della cattura**, mentre la
   versione nuova passava un `ICogRecord` prelevato prima sul thread di ispezione. Un
   record VisionPro preso in anticipo puo' essere gia' stato riciclato quando la UI lo
   assegna al display, che quindi renderizza il solo sfondo. `TryRenderAnnotatedForView`
   ora replica la risoluzione tardiva dell'originale.
-11. *Immagine annotata salvata come solo sfondo.* Con il marshalling sulla UI il
   rendering partiva, ma il file conteneva unicamente il fondo blu del `CogRecordDisplay`
   (dimensione corretta, contenuto nullo). `SaveImageFromRecord` assegnava
   `display.Record` e chiamava `CreateContentBitmap` immediatamente dopo: il controllo
   VisionPro ridisegna in modo asincrono, quindi si catturava la superficie non ancora
   dipinta. Aggiunto `display.Refresh()` (Invalidate+Update sincrono) prima della
   cattura. Aggiunto `SAVE_IMAGE_ANNOTATED_RENDER` con `visible`, `handleCreated` e
   dimensioni del controllo, per distinguere questo caso da un display non realizzato o
   non visibile.
-10. *Grafica ancora assente dopo il primo tentativo.* Il primo intervento chiamava
   `SaveImageFromRecordDisplay` direttamente dal punto di salvataggio, che pero' gira sul
   thread di ispezione (`MainWindow.xaml.cs:4281`, pipeline disaccoppiata dalla UI).
   Quel metodo solleva `InvalidOperationException` se non e' sul thread UI, quindi il
   rendering falliva sempre e si ricadeva sul grezzo, con `SAVE_IMAGE_ANNOTATED_FAILED`
   nel log. Risolto marshallando sul Dispatcher con attesa limitata.
-9. *Immagini salvate tutte grezze.* Dal commit `834c6fb` il percorso di salvataggio in
   produzione scriveva sia `_Z.jpg` sia `_A.bmp` con `SaveRawImage` sulla stessa
   immagine: il file `_Z` prometteva la grafica ma non l'aveva, e i due file
   differivano solo per formato. Era una scelta deliberata contro lo stallo di
   `CreateContentBitmap` sul thread WPF (secondi per pezzo su contatori e coda
   VisionPro), ma applicata a tutti i pezzi. Ora la grafica torna sui soli pezzi che la
   giustificano — scarti e pezzi non classificabili — dove il costo si paga di rado ed e'
   l'immagine che spiega la decisione. Sui pezzi conformi il comportamento resta
   invariato. Il rendering fallito su una vista non fa perdere il pezzo: quella vista
   ricade sul grezzo e viene loggato `SAVE_IMAGE_ANNOTATED_FAILED`.
-8. *Prodotti ravvicinati non ispezionati.* Log 2026-09-04 14:18: due prodotti a 0,69 s
   di distanza producono UNA sola ispezione. `CameraTriggerCorrelationTracker.Register`
   eseguiva `state.Pending.Clear()` a ogni nuovo trigger, quindi la coda dei ticket aveva
   capacita' 1. Il giro trigger -> risultato VisionPro misura ~1,5 s (trigger 14:18:36.71,
   risultato 14:18:38.21): con prodotti ogni 0,7 s il ticket del prodotto precedente
   veniva distrutto mentre il suo risultato era ancora legittimamente in volo. Nel log:
   tre `VISION_TRIGGER_TICKET_REALIGNED` (rear/top/side, discardedProduct=1), poi 7
   risultati di cui 4 scartati come `VISION_RESULT_UNSOLICITED_DROPPED` e
   `Total: 1` per due pezzi passati. Il pezzo non ispezionato esce senza possibilita' di
   scarto e senza comparire nei contatori. Ora la coda tiene fino a 4 ticket e in
   eccedenza scarta il piu' vecchio, che e' il caso reale di acquisizione mancata.
-7. *UI encoder aggiornata alla cadenza del poll.* `OnCounterChanged` inoltrava alla UI
   ogni variazione del contatore, cioe' ~500 volte al secondo: una closure, una
   `DispatcherOperation` e una stringa interpolata per ogni tick. Sono oggetti che
   restano in coda sul thread UI, quindi con vita piu' lunga della gen0 e soggetti a
   promozione in gen1 — cioe' proprio la spazzatura che alimenta le collection
   responsabili degli shift. Ridotto a 10 Hz: la lettura umana non cambia, il traffico
   verso il Dispatcher cala di 50 volte. Riduce anche la contesa sul Dispatcher,
   candidata per gli `encDispatchMs` da ~10 ms osservati con `gc0=0`.
   NON e' stato messo in pool l'oggetto `EncoderEvent`: e' piccolo, muore in gen0 e
   l'unico sottoscrittore non lo trattiene, quindi il guadagno non giustifica uno stato
   mutabile condiviso.
-6. *Messaggio difetto Logo fuorviante.* `Logo position out of tolerance: 0,559 (Max: ±60)`.
   Il controllo e' `Math.Abs(logoPosition) >= recipe.LogoToll / 100`: `logoPosition` e' un
   punteggio di corrispondenza 0..1 (assegnato a `LogoMatchPerc`) e `LogoToll` la
   percentuale MINIMA accettata. 0,559 = 55,9% sotto il minimo del 60% e' quindi uno
   scarto corretto, ma il testo parlava di posizione e di un massimo: l'operatore leggeva
   un valore ampiamente dentro ±60 e interpretava lo scarto come guasto della macchina.
   Ora: `Logo match below minimum: 55,9% (Min: 60%)`. Logica di scarto invariata.
   Il messaggio conserva il prefisso "Logo" richiesto dai filtri in `CameraDisplayManager`.

**Verifica in campo del fix GC (log 2026-09-04 12:29):** `gcServer=True|
gcLatency=SustainedLowLatency`. Ritardi trigger su tre prodotti: REAR +11/+0/+3,
TOP +6/+10/+12, SIDE +10/+2/+5 counts, cioe' entro 1,5 mm contro i +299 counts
(37,7 mm) del log precedente. Pause gen2 scese da 46-72 ms a 21-27 ms. Nessun
`INSPECTION_UNCLASSIFIED` nella sessione.

-5. *Shift del trigger SIDE: causa individuata nel GC.* La telemetria aggiunta ha
   chiuso la diagnosi. Nel log del 2026-09-04 10:2x ogni `IO_POLL_OVERRUN` con
   `cycle` elevato riporta almeno una collection, e i tre buchi maggiori
   (46,7 / 50,1 / 72,6 ms) hanno tutti `gc2=1` con `work` fra 0,05 e 0,29 ms.
   `IO_POLL_STARTED` confermava `gcServer=False|gcLatency=Interactive`, cioe'
   Workstation GC con heap unico su 24 processori logici. Una collection bloccante
   sospende l'intero processo: nessuna priorita' di thread e nessuno spin la evita,
   ed e' anche la spiegazione del fatto che nello stesso istante sballasse la durata
   degli impulsi, che vive su un thread diverso (`IO_PULSE_TIMING_OVERRUN` fino a
   +51 ms). Le ipotesi precedenti (Sleep sul tick di sistema, Yield in spin) erano
   contributi reali ma minori: risolte quelle, restava il GC.
   Conseguenza a valle documentata dal log: il prodotto 2 ha avuto
   `CAMERA_TRIGGER_SIDE` a +299 counts (37,7 mm) durante una gen2, e il job Side ha
   poi prodotto `Tool "Shape" failed ... "LineB" argument is Nothing` con pezzo
   classificato Unclassified. Lo shift del trigger produce quindi scarti diagnostici,
   non solo imprecisione.
-4. *Icone non filtrate all'avvio.* All'apertura del pannello ogni vista mostrava tutte
   le icone di ispezione, anche quelle disabilitate, e si correggeva solo al primo
   prodotto. `RefreshCameraFeatureVisibility()` veniva chiamata al caricamento della
   configurazione ispezioni, che avviene prima della creazione delle viste camera:
   gli accessi `TopCameraView.Instance?.UpdateFeatureVisibility(...)` cadevano quindi
   su `null` e non applicavano nulla, lasciando le icone di default dello XAML. La
   correzione arrivava solo dal refresh dentro `UpdateCameraStatusIndicators`, cioe' a
   primo pezzo ispezionato.
-3. *Esito non leggibile a distanza.* L'operatore doveva distinguere le singole icone
   per capire l'esito. Ora la card feature di ogni vista diventa verde o rossa in base
   all'esito di quella vista, neutra quando il pezzo non e' classificabile o quando la
   vista non partecipa all'ispezione del pezzo. Su pezzo conforme tutte le card sono
   verdi, quindi la lettura a distanza resta immediata; su scarto la card rossa indica
   anche quale camera ha rilevato il difetto.
-2. *Recovery da falso desync camera.* `VISIONPRO_STALL_DETECTED|details=Camera heartbeat
   missing: REAR`. Ramo diverso dal precedente: `cameraDesyncDetected`, non
   `pairTimedOut`. Stessa classe di difetto pero': `timedOutRoles` misurava l'eta'
   assoluta dell'ultimo risultato per ruolo. Dopo l'ultimo prodotto il silenzio di una
   camera e' atteso; se un'altra restava fresca, `timedOutRoles.Count > 0 &&
   timedOutRoles.Count < expectedRoles.Count` diventava vero e il recovery fermava la
   run. Il criterio corretto e' il trigger pendente: se alla camera e' stato inviato un
   trigger e il risultato non arriva entro il timeout, allora manca davvero.
-1. *Pezzi non contati.* Su 9 passaggi il contatore ne registrava 7. Il watchdog
   dichiarava `Inspection pair processing stalled` e avviava il recovery automatico,
   che esegue `Pending VisionPro results cleared`: i risultati del prodotto in volo
   venivano scartati e il contatore non incrementava. Il falso positivo nasceva da
   `pairTimedOut = nowUtc - lastProcessedPairAtUtc > timeout`, cioe' l'eta' assoluta
   dell'ultima coppia elaborata. Durante una pausa di produzione quel valore invecchia
   liberamente; la guardia `hasReceivedVisionHeartbeatSinceRunStart` copriva solo
   l'attesa, ma **all'arrivo del primo risultato successivo** `anyResultTimedOut`
   tornava `false` mentre `pairTimedOut` era ancora `true`, e
   `processingPipelineStalled = pairTimedOut && !anyResultTimedOut` diventava vero
   proprio nella finestra fra "risultati arrivati" e "coppia elaborata".
   Nel log: risultati alle 09:23:00.72, watchdog alle 09:23:01.11, elaborazione
   completata alle 09:23:01.18. Il difetto si manifestava a intermittenza perche'
   `VisionRecoveryCooldown` sopprimeva il recovery su alcune pause.
0. *Crash in chiusura.* `ObjectDisposedException` da `SemaphoreSlim.Release()` in
   `CounterDbFlusher.TickAsync`. `Dispose()` distruggeva il semaforo subito dopo
   `_timer.Dispose()`, che non attende le callback gia' partite: un flush ancora
   fermo su `await _saveFunc(...)` trovava il semaforo distrutto al `finally`.
   L'eccezione nasceva nel `finally`, quindi non era coperta dal `catch` del metodo,
   e la callback del timer era `async void`: l'eccezione risaliva al ThreadPool e
   terminava il processo. Emerso anche un secondo difetto silenzioso: il flush finale
   usava `WaitAsync(0)` e veniva quindi **saltato** se un tick periodico era in corso,
   perdendo i contatori dell'ultima sessione.
1. *Uscite in ritardo.* Il log di campo mostrava `CAMERA_TRIGGER_SIDE` raggiunto con
   `+220/+224 counts (27,8/28,3 mm)` di ritardo, corrispondente al ritardo fisico
   osservato sull'uscita SIDE. La telemetria aggiunta in precedenza ha escluso la
   catena di scrittura (`raiseLatency=0 ms`, `channelWait=0,001 ms`) e ha isolato la
   causa nella cadenza del poll: `work` del corpo poll ~1,5 ms ma `cycle` di 12-65 ms.
   I valori di `cycle` risultavano multipli del tick di sistema (~15,6 ms), quindi
   `Thread.Sleep(1)` si stava risvegliando sul tick di default: su Windows 11
   `timeBeginPeriod(1)` ritorna successo senza piu' garantire la granularita' a 1 ms.
   La valutazione dei punti di intervento gira dentro il poll (dispatch sincrono di
   `CounterChanged` -> `MachineController.UpdateEncoderPosition`), quindi un buco di
   55 ms nel poll si traduce direttamente in un'uscita alzata alla quota sbagliata.
2. *`encHwMs` 10-30 ms.* Con un solo canale encoder attivo i setter
   `Channel`/`ChannelStart` venivano eseguiti ~500 volte al secondo sotto `_lock`,
   marshallando nel driver senza effetto utile.
3. *Icone invertite.* Logo e Centratura Stampa erano scambiate in tre punti distinti:
   i nomi dei file PNG storici descrivono il contenuto opposto a quello raffigurato
   (`presenza-marker-.png` mostra la misura di posizione, `centratura-logo.png` la
   presenza del logo). Diverse ispezioni condividevano inoltre la stessa icona
   segnaposto (`Height` e `SideRollCount` usavano il generico `COMPLIANT-OK.png`).

**Impatto:**
- Config.xml: nessuno.
- Database: nessuno.
- API pubblica: nessuna modifica di firma. Aggiunti due metodi privati in
  `AdvantechDeviceManager`.
- Comportamento runtime: il thread di polling I/O non cede piu' il core al timer di
  sistema con `pollIntervalMs` <= 5 ms. Consumo previsto: un core costantemente
  attivo (su 24 processori logici della macchina in campo). Intervalli di poll
  superiori a 5 ms mantengono il comportamento precedente con `Thread.Sleep(1)`.
  Le icone delle ispezioni sono vettoriali; la risoluzione ricade sui PNG se una
  chiave non e' presente.

**Da verificare in campo:** che `IO_POLL_OVERRUN` con `cycle` elevato e `work` basso
sparisca, e che il ritardo su `CAMERA_TRIGGER_SIDE` rientri nei pochi counts gia'
osservati su TOP/REAR.

## [2026-09-03] Versione 3.1.5.1 - Camere e ispezioni specifiche per ricetta

**Problema corretto:** la matrice vista x ispezione e la tassonomia Classify erano
indicizzate soltanto per `MachineType`. Di conseguenza due ricette della stessa macchina non
potevano usare insiemi diversi di camere o ToolBlock diversi senza aspettarsi output assenti.
Inoltre il selettore camera della pagina non governava ancora pairing, watchdog e display.

**Implementazione:**
- nuovo nodo XML opzionale `inspectionViewConfiguration` nel file ricetta, contenente viste,
  celle ispezione e classi AI accettate;
- uso del gia' esistente `machineRuntimeAdjustments.CameraTriggers` come unica abilitazione
  camera per ricetta: mapping I/O, board, canale, polarita' e quote restano globali macchina;
- matrice ricetta strict: una cella non salvata e' disabilitata, quindi nuove feature future
  non si attivano da sole su ricette gia' qualificate;
- al primo passaggio a strict vengono congelati tutti i ruoli e tutte le feature risolte,
  incluse le viste non renderizzate; anche le policy Classify macchina sono preservate;
- fallback compatibile per XML storici verso `cfg_inspection_view` e
  `cfg_view_classification` del tipo macchina;
- esclusione delle camere disabilitate dalle code risultato, dai companion attesi, dal
  watchdog e dal container display;
- pagina ricetta e pannello soglie SIDE allineati anche a Left e Rear/Right;
- errori DB in lettura non vengono piu' scambiati per matrice vuota: il servizio conserva
  l'ultimo snapshot macchina valido;
- reset della configurazione esteso a matrice e tassonomia; salvataggi default macchina
  eliminano anche righe obsolete, mentre un payload vuoto non puo' cancellare il fallback;
- una configurazione solo TOP e' valida; TOP/TOP3D non puo' essere disabilitata finche'
  rimane il risultato primario dell'orchestratore;
- salvataggio e reset Inspection Configuration sono bloccati durante la produzione o con
  prodotti ancora in volo e tengono un hold runtime per tutta l'operazione;
- watchdog heartbeat separato per ogni ruolo, compresi Rear e Bottom;
- confronto area sigillatura corretto in `misura >= soglia minima` per Side e Rear;
- messaggio e simbolo Open Flaps riallineati alla semantica reale di soglia massima;
- NLog mantiene asincrono il percorso chiamante con code separate, limitate e sempre
  `Discard`: nessun file, audit o bridge MySQL puo' bloccare il thread real-time;
- il polling non interroga piu' il frequency meter BDaq nel ciclo da 2 ms: la frequenza
  diagnostica deriva dai delta encoder; `IO_POLL_OVERRUN` riporta la fase piu' lenta;
- `IO_PULSE_TIMING_OVERRUN` riporta durata HIGH reale, attesa canale e ritardo, mentre
  `IO_OUTPUT_WRITE_SLOW` separa attesa lock e tempo della chiamata hardware.

**Compatibilita':**
- nessuna nuova chiave `Config.xml` o `machine_runtime_config.xml`;
- nessuna migrazione MySQL: le tabelle esistenti restano default macchina;
- le vecchie ricette deserializzano `IsConfigured=false` e mantengono il comportamento
  precedente;
- il nuovo profilo viene scritto soltanto quando un utente salva la configurazione per vista.

**Verifica:** build `Release|x64` exit 0; round-trip XML e test in memoria della matrice strict,
policy AI, camera Left disabilitata e fallback legacy superati. Resta obbligatorio il collaudo
su macchina reale: verificare `slowest`, `actualHighMs`, `hardwareMs` e gli scarti encoder su
almeno 30 prodotti prima di considerare chiuso lo shift residuo.

## [2026-09-02] Versione 3.1.5.0 - Logging asincrono (shift trigger) e soglie sigillatura per vista

### 1. Causa dello shift residuo dei trigger: logging sincrono sul thread di polling

**Evidenza dai log di campo:** ogni ritardo anomalo coincide esattamente con un
`IO_POLL_OVERRUN`. Prodotto 6: `work=65,4 ms` -> SIDE late `+306 counts` (38,6 mm);
prodotto 8: `work=61,7 ms` -> `+217`; prodotto 9: `work=41,5 ms` -> `+148`. A polling pulito
il ritardo e' di 1-10 count.

**Meccanismo:** `DigitalIOViewModel.AddLogEntry` e `OnLogMessage` vengono invocati dal
THREAD DI POLLING I/O (quello che legge encoder e input ogni 2 ms, via
`OnCounterChanged` -> `MachineController.UpdateEncoderPosition`). `AddLogEntry` chiamava
`Logger.Info/Warn/Error` in modo SINCRONO. In `NLog.config` i target erano tutti sincroni,
con `keepFileOpen="false"` (apertura e chiusura del file ad ogni scrittura) e
`concurrentWrites="true"` (mutex inter-processo ad ogni scrittura). La regola
`<logger name="*" minlevel="Warn" writeTo="errorfile,dbbridge,jsonfile" />` faceva inoltre
si' che ogni WARN eseguisse, in fila sul thread chiamante:

- `errorfile`: mutex + open + write + flush + close
- `jsonfile`: idem
- `dbbridge`: `MethodCall` verso `NLogEventBridge.Write`, che fa
  `JsonConvert.SerializeObject` e una **INSERT MySQL sincrona** (`repository.Insert(entry)`)

Con il disco gia' occupato dal salvataggio delle immagini pezzo, questo spiega interamente i
65 ms. Il caso era anche **auto-alimentante**: il messaggio
`[TRACK] Trigger request executed ... late=+306 counts` e' esso stesso un WARN, quindi il log
del ritardo bloccava il thread e faceva arrivare in ritardo il trigger successivo.

**Correzione:** `NLog.config` usa ora `<targets async="true">`, che avvolge OGNI target in un
`AsyncWrapper`: il thread chiamante si limita ad accodare l'evento. Aggiunto anche
`keepFileOpen="true"` per eliminare open/close ad ogni riga. Nessuna modifica al codice C#.

Compromesso accettato e documentato nel file: in caso di crash brutale gli eventi ancora in
coda vengono persi (`overflowAction=Discard` di default). Su una macchina in produzione
perdere le ultime righe di diagnostica e' preferibile a fermare il campionamento encoder.

Questa correzione dovrebbe agire anche sui ritardi di aggiornamento dei contatori segnalati
dall'operatore, perche' rimuove la stessa sorgente di blocco.

### 2. Soglia di area sigillatura separata per vista

**Motivo:** `recipeParamSide.MinAreaSealing` era un unico scalare letto sia dalla validazione
SIDE sia da quella REAR. Due camere con ottica, distanza e inquadratura diverse condividevano
quindi la stessa soglia in mm2, il che rende impossibile tarare correttamente entrambe.

**File modificati:**
- `Cls_Config/Calss_structure/RecipeParameters.cs` - nuovi `MinAreaSealingRear` e
  `MinAreaSealingLeft` in `RecipeParamSide`, piu' `ResolveMinAreaSealing(cameraRole)` che
  centralizza l'ereditarieta'. Zero significa "non configurata" e fa ereditare
  `MinAreaSealing`: stesso sentinella gia' usato per `Top3DDetectionSensitivity`, quindi le
  ricette esistenti mantengono esattamente il comportamento precedente.
- `DataManage/IToolBlockValidator.cs` - le validazioni SIDE e REAR chiamano ora
  `ResolveMinAreaSealing` con il proprio ruolo invece di leggere lo scalare condiviso; i
  messaggi di errore riportano la soglia effettivamente applicata.
- `Views/RecipeManagerView.xaml` - due campi "Sigillatura REAR" e "Sigillatura LEFT" nella
  sezione soglie SIDE, con indicazione esplicita "0 = usa SIDE".
- `Properties/AssemblyInfo.cs` - release `3.1.4.0` -> `3.1.5.0`.

**Verifica:** build Release x64 exit 0; `NLog.config` validato come XML ben formato.
`Nlog.config` e' gia' dichiarato nel csproj con `CopyToOutputDirectory=PreserveNewest`,
quindi la nuova configurazione arriva in output alla prima build.

Da verificare in campo: dopo l'aggiornamento gli `IO_POLL_OVERRUN` con `work` nell'ordine
delle decine di ms devono sparire, e con essi i ritardi `late=+150...+300 counts` sui trigger
camera. Se dovessero persistere con polling pulito, la causa non e' piu' il logging.

**Impatto:**
- Database, Config.xml: nessuna modifica.
- Ricette: due campi nuovi, entrambi opzionali con default 0 = comportamento precedente.
  `XmlSerializer` ignora gli elementi assenti, quindi le ricette esistenti si caricano
  invariate.
- API pubblica: solo aggiunte.

## [2026-09-02] Versione 3.1.4.0 - Tassonomia Classify (ViDi EL) per vista camera

**Motivo:** la decisione OK/NOK della classificazione AI passava da una lista FISSA di
etichette in `Services/VisionClassificationResultReader.cs`:

```csharp
PassingLabels = { "OK", "GOOD", "PASS", "PASSED", "COMPLIANT" }
FailingLabels = { "NOK", "NG", "KO", "FAIL", "FAILED", "BAD", "REJECT", "NOREAD" }
```

`ResolveState` restituisce `Informational` per qualunque classe fuori da quelle due liste, e
`ValidateAiClassificationAsync` trattava `Informational` come scarto. Un modello Edge
Learning e' pero' addestrato PER CAMERA, quindi la tassonomia cambia da vista a vista: nei
log di campo si vedono `Class=pieghe top` sulla Top, `Class=Paper Shift` sulla Side,
`Class=bad Trasversal sealing` sulla Rear. Con una tassonomia custom ogni classe finiva in
`Informational` e quindi in scarto, indipendentemente dal suo significato.

**File modificati:**
- `DataManage/CameraInspectionMatrix.cs` - nuove `CameraClassificationPolicy` e
  `CameraClassificationRule`: per ogni vista, l'elenco delle classi che NON generano scarto.
- `DataManage/InspectionConfigService.cs` - la policy viene caricata insieme alla matrice in
  `LoadFeaturesAsync`, quindi vale gia' all'avvio; `ApplyClassificationPolicy` la applica a
  caldo dopo un salvataggio.
- `DataManage/IToolBlockValidator.cs` - `ValidateAiClassificationAsync` usa la policy della
  vista quando esiste; il messaggio di errore elenca ora le classi accettate per quella
  vista invece della lista fissa.
- `Database/Cls_InitializzeDb.cs` - nuova tabella `cfg_view_classification`
  (CameraRole, MachineType, AcceptedClasses).
- `ViewModels/InspectionConfigViewModel.cs` - load/save della policy, esposta per vista.
- `Views/InspectionConfigView.xaml` - campo "Classi Classify accettate" nella card di ogni
  vista, accanto alle spunte delle ispezioni.
- `Properties/AssemblyInfo.cs` - release `3.1.3.1` -> `3.1.4.0`.

**Compatibilita':** campo vuoto o tabella vuota = nessuna policy per quella vista, e vale
integralmente il comportamento storico basato sulle etichette standard. Nessun seed.

**Verifica:** build Release x64 exit 0. Durante l'implementazione e' stato intercettato e
corretto un uso di `StaticResource CaptionStyle` non definita in quella view: il compilatore
non lo segnala, ma avrebbe fatto fallire il caricamento della pagina a runtime. Tutte le
`StaticResource` della view sono state poi verificate una per una.

Da verificare in campo: dichiarare per ogni vista le classi accettate del proprio modello EL
(es. sulla Rear le classi di saldatura trasversale conforme) e controllare che i pezzi buoni
non vengano piu' scartati; il messaggio di fallimento ora riporta le classi ammesse per la
vista, quindi un eventuale disallineamento e' leggibile direttamente dal log.

**Impatto:**
- Database: nuova tabella `cfg_view_classification`; nessuna modifica a tabelle esistenti.
- Ricette / Config.xml: nessuna modifica.
- API pubblica: solo aggiunte.
- Comportamento runtime: invariato finche' non viene dichiarata almeno una classe.

**Non ancora implementato (richiesto, da progettare insieme):** tolleranze NUMERICHE distinte
per vista. Oggi alcuni scalari sono condivisi fra piu' camere - il caso piu' evidente e'
`recipeParamSide.MinAreaSealing`, letto sia dalla validazione Side sia da quella Rear
(`IToolBlockValidator.cs:386` e `:500`) - e `ShapeValue`/`ShapeToll` di ShapeSide vivono in
`recipeParamTop`. Separarli richiede un contenitore per vista nel modello ricetta con
fallback allo scalare esistente quando non configurato, altrimenti le ricette gia' in campo
si troverebbero la tolleranza azzerata.

## [2026-09-02] Versione 3.1.3.1 - Pagina ricetta: ispezioni e tolleranze classificate per vista

Fase 2 della matrice vista x ispezione introdotta in `3.1.3.0`. Quella release ha reso il
validatore consapevole della vista; questa porta la stessa classificazione nella pagina
ricetta, dove finora ispezioni e tolleranze erano un elenco piatto senza alcun riferimento
alla camera che le esegue.

**File modificati:**
- `Models/InspectionItem.cs` - nuovo campo `CameraViews`: le viste che eseguono quella
  ispezione, come etichetta leggibile (es. `SIDE / LEFT`).
- `ViewModels/RecipeManagerViewModel.cs` - `DescribePerformingViews` interroga la matrice
  configurata in Inspection Configuration e riporta le sole viste realmente presenti sulla
  macchina che eseguono il controllo; nuove proiezioni raggruppate `InspectionViewGroups` /
  `EjectionViewGroups`; etichette `TopThresholdsViewLabel` / `SideThresholdsViewLabel` per
  le sezioni di tolleranza, rinfrescate insieme agli altri predicati di presenza camera.
- `Views/RecipeManagerView.xaml` - le spunte "Ispezioni Abilitate" e "Ispezioni che generano
  scarto" sono ora raggruppate per vista; le due sezioni di soglie mostrano un sottotitolo
  con le viste che applicano quelle tolleranze.
- `Properties/AssemblyInfo.cs` - release `3.1.3.0` -> `3.1.3.1`.

**Scelta di modello (importante):** un'ispezione compare UNA sola volta, sotto l'etichetta di
tutte le viste che la eseguono, invece di comparire una volta per vista. Il motivo e' che il
flag della ricetta e' uno solo per ispezione: e' una proprieta' del PRODOTTO, non della
camera. Mostrare tre spunte per `Side_sealing` (SIDE, LEFT, REAR) che scrivono tutte lo
stesso valore sarebbe una trappola per l'operatore. La ripartizione per camera resta dove
deve stare, cioe' nella matrice di Inspection Configuration (livello macchina), mentre la
ricetta continua a dire soltanto se quel controllo e' attivo per quel prodotto.

Conseguenza pratica: se dalla pagina Inspection Configuration si toglie `SideSealing` alla
vista REAR, la pagina ricetta smette di mostrare "Sigillatura Laterale" sotto
`SIDE / LEFT / REAR` e la mostra sotto `SIDE / LEFT`. Un'ispezione che nessuna vista esegue
finisce nel gruppo "Nessuna vista assegnata", cosi' resta visibile il caso in cui il
controllo e' acceso in ricetta ma nessuna camera lo applica.

**Cosa NON e' stato toccato, e perche':**
- Le collezioni piatte `InspectionItems` / `EjectionItems` restano invariate e sono ancora la
  sorgente di verita' per il write-back su `inspectionStatus` / `ejectionStatus`: i gruppi
  sono una proiezione sugli STESSI oggetti, quindi spuntare nella nuova UI aggiorna la
  ricetta esattamente come prima.
- La `Visibility` delle sezioni di tolleranza e' rimasta quella esistente
  (`MachineTypeToVisibilityConverter` / `HasSideCameraConfigured`): nasconderle in base alla
  matrice avrebbe potuto sottrarre campi a un operatore su impianti gia' in produzione. Le
  sezioni sono state solo etichettate.
- I `GroupBox` delle tolleranze conservano il proprio `x:Name` e il proprio `Header`, perche'
  `RecipeManagerView.xaml.cs::InitializeMessagelabel()` localizza circa 90 controlli per
  nome: spostarli dentro un `DataTemplate` li renderebbe irraggiungibili dal code-behind.
- La sezione "Centratura Stampa" non ha ricevuto il sottotitolo: il suo contenuto e' una
  griglia a righe fisse e inserire un elemento avrebbe richiesto di rinumerarle.

**Verifica:** build Release x64 exit 0. Da verificare in campo: aprire una ricetta e
controllare che le spunte compaiano raggruppate per vista coerentemente con la matrice, e che
togliendo `SideSealing` alla REAR in Inspection Configuration il raggruppamento si aggiorni.

**Impatto:**
- Database, ricette, Config.xml: nessuna modifica di formato.
- API pubblica: solo aggiunte.
- Comportamento runtime: nessun cambiamento nella validazione; questa release e' di sola
  presentazione e organizzazione della pagina ricetta.

## [2026-09-02] Versione 3.1.3.0 - Matrice esplicita vista camera x ispezione

**Motivo:** l'insieme delle ispezioni attive era una mappa piatta `feature -> bool`, con il
ruolo camera usato solo come OR-gate. In `InspectionConfigService.BuildRuntimeFeatureMap`:

```csharp
runtimeFeatures["SideSealing"] = (hasSide || hasLeft || hasRear) && status.Side_sealing;
runtimeFeatures["ShapeSide"]   = (hasSide || hasRear) && status.ShapeSide;
```

Abilitare la sigillatura per la Side la rendeva quindi obbligatoria anche sulla Rear, il cui
ToolBlock espone solo `EL_Classify` / `EL_SCore`. Risultato in produzione: ogni pezzo
scartato con `Right side sealing output missing. Expected one of: RightSideSealingOk,
RearSideSealingOk, ... Available outputs: EL_Classify, EL_SCore`. Non esisteva alcun modo di
dichiarare quali controlli ogni vista verifica davvero.

**File modificati:**
- `DataManage/CameraInspectionMatrix.cs` (nuovo) - matrice vista x ispezione. `IsApplicable`
  usa l'override esplicito quando presente, altrimenti `DefaultAppliesTo`, che riproduce la
  tabella di applicabilita' storica finora sparsa fra `BuildRuntimeFeatureMap` e
  `InspectionConfigViewModel.ShouldIncludeFeatureForCurrentRuntime`.
- `DataManage/InspectionConfigService.cs` - nuovo overload
  `IsFeatureEnabled(cameraRole, feature)` = flag globale AND cella di matrice. L'overload a
  un argomento resta invariato per tutti i consumatori esistenti (contatori, pannelli).
  La matrice viene caricata dentro `LoadFeaturesAsync`, quindi vale gia' all'avvio e non
  solo dopo aver aperto la pagina di configurazione.
- `DataManage/IToolBlockValidator.cs` - i 20 gate di ispezione passano ora il ruolo della
  propria vista (`RoleTop`, `RoleSide`, `RoleRear`, `RoleLeft`, `RoleFront`, `RoleBottom`,
  `RoleTop3D`); `ValidateAiClassificationAsync` usa il `cameraRole` che gia' riceve.
- `Database/Cls_InitializzeDb.cs` - nuova tabella `cfg_inspection_view`
  (Feature, CameraRole, MachineType, IsEnabled).
- `ViewModels/InspectionConfigViewModel.cs` - `LoadViewMatrixAsync`/`SaveViewMatrixAsync`
  sulla nuova tabella, e collezione `ViewMatrix` per la UI.
- `Views/InspectionConfigView.xaml` - sezione "Inspections per camera view": una scheda per
  vista realmente presente, con le sole ispezioni che quella vista puo' verificare.
- `QtisVisionPanel.csproj` - registrazione del nuovo file (progetto classico).
- `Properties/AssemblyInfo.cs` - release `3.1.2.0` -> `3.1.3.0`.

**Scelte di compatibilita' (deliberate):**
- Tabella NUOVA invece di una colonna su `cfg_inspection`: quest'ultima ha UNIQUE KEY
  (Feature, MachineType) ed e' creata con `CREATE TABLE IF NOT EXISTS`, quindi gli impianti
  gia' installati non riceverebbero mai una colonna aggiunta li'. Una tabella dedicata si
  crea sui vecchi impianti senza migrazione.
- NESSUN seed della nuova tabella. Seminare celle a `false` disabiliterebbe silenziosamente
  ispezioni oggi attive: `BottomSealing` e `TrappedPaper` hanno default `true` in
  `InspectionStatus`. Zero righe = nessun override = comportamento storico.
- Il default non "risolve da solo" il caso Rear: `SideSealing` resta applicabile a
  side/left/rear come oggi. E' l'operatore che, dalla nuova sezione, toglie la spunta a
  `SideSealing` sulla vista REAR. Questo era il senso della richiesta: rendere esplicito
  cio' che prima era dedotto.
- Ruolo passato in modo esplicito ai gate, non tramite stato implicito: i metodi di
  validazione sono `async` e una continuazione puo' riprendere su un thread diverso, quindi
  `[ThreadStatic]` sarebbe stato inaffidabile proprio sul percorso che decide gli scarti.

**Verifica:** build Release x64 exit 0. Da verificare in campo: aprire Inspection
Configuration, togliere `SideSealing` (ed eventualmente `ShapeSide`) dalla vista REAR,
salvare, e controllare che il messaggio `Right side sealing output missing` sparisca dai log
senza che le altre viste cambino comportamento. Il log `INSPECTION_VIEW_MATRIX_ACTIVE`
all'avvio conferma quante celle di override sono attive.

**Impatto:**
- Database: nuova tabella `cfg_inspection_view`; nessuna modifica a tabelle esistenti.
- Ricette / Config.xml: nessuna modifica.
- API pubblica: solo aggiunte (nuovo overload, nuova classe); nessuna firma cambiata.
- Comportamento runtime: invariato finche' non viene salvata una configurazione per vista.

**Non incluso (fase successiva concordata):** il raggruppamento per vista dei campi di
tolleranza nella pagina ricetta. La mappatura e' gia' fatta ma l'intervento e' delicato:
`RecipeManagerView.xaml.cs::InitializeMessagelabel()` localizza circa 90 controlli per
`x:Name`, e spostarli dentro un `DataTemplate` li renderebbe irraggiungibili dal code-behind.

## [2026-09-02] Versione 3.1.2.0 - Trigger isolati, risultati correlati e UI non bloccante

**Trigger:** gli eventi quota encoder vengono accodati a un worker `AboveNormal`; le scritture
DAQNavi e i log non bloccano piu' il polling encoder. La PCIE-1756 aggiorna il solo port byte del
canale modificato. Nuovi log: `INTERVENTION_DISPATCH_DELAY` e `IO_OUTPUT_WRITE_SLOW`.

**Sincronismo risultati:** ogni trigger HMI crea un ticket ruolo/prodotto. `CameraResult` conserva
il `ProductId` del trigger e `InspectionOrchestrator` abbina i companion per identita', scartando
duplicati non richiesti e risultati obsoleti. Synthetic, external trigger e legacy mantengono il
fallback compatibile con controllo del lag assoluto.

**UI e salvataggio:** i display sono best-effort a priorita' `Background`, ignorano completion
fuori ordine e non trattengono piu' la pipeline seriale. Le immagini vengono estratte da snapshot
record immutabili su una coda bounded e convertite fuori dal Dispatcher; contatori e difetti
mantengono priorita' `DataBind`.

**Compatibilita':** nessuna chiave XML e nessuna migrazione DB. Il JPEG `_Z` continuo e' ora una
copia diagnostica del record senza rendering overlay; il BMP `_A` resta raw. Guida:
`vision-trigger-result-correlation-and-ui-decoupling-2026-09-02.md`.

## [2026-09-01] Versione 3.1.1.8 - Output verificati, trigger per ricetta e tracking piu' preciso

**Persistenza I/O:** le nuove righe Machine Outputs nascono come uscite camera reali invece che
come input non assegnati. `Direction`, `Category` e `Polarity` notificano ora le modifiche WPF. Il
salvataggio chiude le transazioni DataGrid, normalizza i campi, rilegge il file macchina e verifica
che ogni mapping sia stato effettivamente persistito.

**Intervention Point Editor:** il punto selezionato espone scheda, canale, polarita' e flag segnale
fisico del relativo output globale. Se il signal code non ha ancora un mapping, l'editor crea la
riga macchina senza trasferire dettagli hardware nella ricetta.

**Ricetta:** aggiunto `machineRuntimeAdjustments.CameraTriggers` con modo `Machine`, `Enabled` o
`Disabled` per Top, Side, Left, Front, Right, Rear e Bottom. Le ricette legacy ereditano lo stato
macchina. Un MultiShot senza punto camera abilitato viene disattivato nel solo runtime.

**Timing:** polling encoder target da 10 a 2 ms, eliminata la lettura DI fisica nel punto caldo del
trigger, scrittura DO prima del log e serializzazione delle scritture concorrenti sulla scheda.
`IO_POLL_OVERRUN` segnala cicli di almeno 10 ms; il salvataggio dello stato simulato e' limitato a
una volta al secondo.

**Compatibilita':** nessun cambio DB. Il nuovo nodo XML ricetta e' additivo; mapping, quote base,
canali e polarita' restano globali nel file macchina. Guida e test:
`machine-output-recipe-trigger-and-tracking-commissioning-2026-09-01.md`.

**Verifica:** JSON lingua valido e build Release x64 completata con 0 errori. Resta obbligatorio il
collaudo hardware di 100 prodotti; il polling Windows non e' hard real-time.

## [2026-09-01] Versione 3.1.1.7 - Log transizioni output I/O ridotto

**Pulizia log:** le transizioni elettriche raw `Output PCIE-1756/1884 <canale>
set to HIGH/LOW` passano da `INFO` a `DEBUG`. Non vengono quindi piu' scritte
nel normale application log, configurato con soglia `Info`.

**Diagnostica preservata:** errori e codici di fallimento delle schede restano
`ERROR/WARN`; i log applicativi `Pulse START/END`, segnale, durata e motivo
restano `INFO`. In una sessione diagnostica con livello Debug e' ancora
possibile osservare ogni singolo cambio elettrico.

**Impatto:** nessuna modifica a I/O, temporizzazioni, XML macchina/ricetta o
database. Cambia soltanto il livello delle due righe ad alta frequenza.

## [2026-09-01] Versione 3.1.1.6 - Live Preview da CogInputImageTool

**Correzione:** la sorgente grezza non viene piu' cercata nel LastRun record.
Il `VisionTool` del job selezionato viene convertito in `CogToolGroup` e la HMI
legge direttamente `CogInputImageTool.InputImage` dal primo tool QuickBuild;
se un VPP inserisce un tool prima di `Image Source`, viene cercato il primo
`CogInputImageTool` successivo.

**Display e diagnostica:** l'immagine raw viene acquisita prima di consumare il
`UserResult`; record e graphics del display vengono azzerati prima
dell'assegnazione. Il log riporta `source=CogInputImageTool.InputImage` oppure
un warning unico con tipo/tool/motivo e `fallback=LastRun`.

**Compatibilita':** nessuna modifica a `Config.xml`, XML ricetta o database. I
VPP senza `CogInputImageTool` continuano a usare il LastRun configurato.

**Verifica:** build Release x64 completata con 0 errori. Il VPP di sviluppo e'
stato ispezionato e il tool `Image Source` espone la proprieta' pubblica
`InputImage`; resta obbligatorio il collaudo con camera hardware-trigger reale.

## [2026-09-01] Versione 3.1.1.5 - Live Preview raw e updater separato

**Live Preview:** `JobToolEditorViewModel` continua a consumare il `UserResult`
tecnico per mantenere limitata la coda QuickBuild, ma visualizza
prioritariamente l'immagine grezza trovata in
`[ToolGroup Inputs] -> Image Source -> OutputImage`. Il display viene ripulito
da record e graphics prima di assegnare la raw image. Se il ramo non esiste,
resta il fallback LastRun compatibile con i VPP precedenti.

**Installer:** il manifest espone `packageType=Full|UpdateOnly`. Il builder `r17`
genera due supporti autonomi: il media completo e
`QtisVisionPanel_Update_3.1.1.5-r17`, contenente un solo componente core e un
solo `ApplicationBin`. Il bootstrapper blocca update su installazione assente,
manifest con RuntimeSeed/prerequisiti/configurazioni e downgrade.

**Protezione macchina:** durante `UpdateOnly` non vengono eseguiti runtime seed,
configurazione Python, shortcut, rete o prerequisiti. `Programs`, ricette, VPP,
`cfg`, OPC, Language macchina, immagini e modelli AI non sono trasportati. Il
motore SHA-256, il backup selettivo, il journal persistente e il rollback restano
attivi sui soli file di `C:\QtisVision\bin` gestiti dal manifest.

**Hardening bootstrapper:** i log usano timestamp con millisecondi e PID per
evitare collisioni quando inventario/verifica di full e updater vengono avviati
nello stesso secondo. L'updater conserva inoltre nello stato installazione i
componenti e il target VisionProDependencies gia' registrati.

**Impatto:** nessuna modifica a XML macchina/ricetta o database. Release
`3.1.1.4 -> 3.1.1.5`; media `r16 -> r17`.

**Verifica:** build Release x64 completata; builder full/update `PASSED`; media
full con 30 payload e bootstrapper `--validate isValid=true`; updater con un
solo payload, `containsMachineSpecificConfiguration=false`, nessun RuntimeSeed
e zero entry `cfg`/`OPC`/Programs/Language/VisionProDependencies. Sul PC di
sviluppo senza HMI installata il test negativo dell'updater restituisce
correttamente il blocco `usare l'installer completo`.

## [2026-08-31] Media installer 3.1.1.4-r16 - provider temperature PC

**Generazione:** `Installer\Build-QtisInstallerMedia.ps1`, media in
`D:\QtisInstallerOutput\QtisVisionPanel_3.1.1.4-r16`.

**Verifica:** builder 7/7 `PASSED`; bootstrapper `--validate` con
`isValid=true`, zero errori e undici warning operativi attesi; 30 payload;
dimensione media circa `7,23 GiB`. SHA-256 setup:
`5B8393E0C972512A0095EFDBF8BD1312837AEB42E10F15AD59945E9530660C3E`.

**Contenuto aggiunto:** `LibreHardwareMonitorLib.dll`, `DiskInfoToolkit.dll`,
`HidSharp.dll`, `RAMSPDToolkit-NDD.dll` e
`ThirdPartyNotices\LibreHardwareMonitor.md` dentro
`ApplicationBin-3.1.1.4.zip`. L'update differenziale li tratta come nuovi file
applicativi senza modificare `cfg`, ricette, VPP, database o junction Cognex.

## [2026-08-31] Versione 3.1.1.4 - Temperature hardware PC integrate e non bloccanti

**File principali:**
- `Services/HardwareTemperatureProvider.cs` - provider LibreHardwareMonitor con
  cache, retry limitato, selezione dei sensori rappresentativi e dispose.
- `Services/SystemDiagnosticsService.cs` - integrazione background, fallback WMI,
  soglie e logging a transizione.
- `Views/UserControls/SystemDiagnosticsView.xaml` - card CPU dedicata e card
  sistema/memoria.
- `Views/PreferenceView.xaml` - provider, polling e soglie CPU/motherboard.
- `QtisVisionPanel.csproj` - NuGet `LibreHardwareMonitorLib` 0.9.6 e notice MPL.

**Motivo:** i provider WMI usati in precedenza non espongono spesso la
temperatura CPU, motherboard, RAM o NVMe sui PC industriali. La nuova sorgente
amplia la copertura senza installare un programma separato.

**Impatto:**
- `Config.xml`: nuovi campi opzionali sotto `SystemDiagnostics`; file precedenti
  compatibili grazie ai default del modello.
- Database e ricette: nessuna modifica.
- Runtime macchina: nessuna dipendenza. Scansione in background con cache;
  errore o sensore assente non blocca avvio, RunContinuous, I/O o scarto.
- Installer: le DLL NuGet e il notice di licenza vengono inclusi in
  `ApplicationBin` e gestiti anche dall'aggiornamento differenziale.

**Verifica:** restore e build Release x64 completati; provider caricato in un
probe locale. La shell di test non elevata rileva l'hardware ma non i valori
termici Intel, condizione gestita come sensore non disponibile; il collaudo dei
valori reali resta da eseguire sul PC AVS con HMI elevata.

## [2026-08-31] Media installer 3.1.1.3-r15 - rigenerazione con il fix dei binding runtime

**Obiettivo:** portare sul supporto di installazione la correzione `3.1.1.3`
(preservazione dei binding runtime non gestiti dalla vista I/O), senza modificare
la composizione del media.

**Generazione:** `Installer\Build-QtisInstallerMedia.ps1` con i parametri di
default (nessun `-RedactSecrets`, come per il media r15 precedente). La revisione
media resta `r15`: cambiano solo i binari HMI, non i prerequisiti offline ne' i
componenti opzionali.

**Verifica eseguita:**
- builder: 7 passi su 7, `Supporto creato e validato`, exit code `0`;
- payload verificati: `30`; dimensione `7.766.707.787` byte (`7,23 GiB`);
- SHA-256 setup:
  `81FC269AFEED360BAD992FE23E2AAFB1395BBFA015198A5C60D5FDB42A37B9B0`;
- `QtisVisionPanel.exe` dentro `ApplicationBin-3.1.1.3.zip` byte-identico alla
  Release x64 (`D64CAA35961142749D09E3C4F4C022D59892AC3AD417AB6C9355DB59A5227630`),
  versione file `3.1.1.3`.

**Non eseguito:** la validazione del bootstrapper (`QtisVisionSetup.exe --validate`)
richiede una shell elevata e non e' stata lanciata su questo media. Va eseguita sul
PC di destinazione prima della consegna, come per i media precedenti.

**Impatto:** nessuna modifica a composizione del supporto, prerequisiti, componenti
opzionali o procedura di installazione. Cambia solo la versione HMI trasportata.

## [2026-08-31] Versione 3.1.1.3 - Perdita silenziosa dei binding runtime non gestiti dalla vista I/O

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - `BuildRuntimeConfigurationFromView()` non ricostruisce
  piu' `RuntimeBindings` da zero: parte dall'istanza gia' caricata e sovrascrive solo i campi
  posseduti dalla vista I/O.
- `Properties/AssemblyInfo.cs` - release `3.1.1.2` -> `3.1.1.3`.

**Motivo:** `MachineRuntimeBindings` espone 76 proprieta', ma il pannello I/O ne scrive 27. Il
metodo faceva `configuration.RuntimeBindings = new MachineRuntimeBindings { ... }`, quindi le
restanti **49** tornavano al valore di default compilato e venivano poi persistite su disco.
Il metodo e' invocato ad ogni avvio (`InitializeMachineRuntimeAsync`), ad ogni salvataggio del
pannello I/O e ad ogni anteprima runtime: la perdita si ripeteva quindi ad ogni riavvio della
macchina, non una volta sola.

Campi azzerati (verificati per enumerazione, non a campione):

- Email/SMTP: `EmailNotificationsEnabled`, `EmailSmtpHost`, `EmailSmtpPort`, `EmailUseSsl`,
  `EmailFromAddress`, `EmailToAddresses`, `EmailSmtpUsername`, `EmailSmtpPassword` - il canale
  email dei preallarmi risultava configurato e collaudato, poi silenziosamente inattivo dal
  primo riavvio, senza alcuna segnalazione.
- Flag servizi AI: `DataFoundationCaptureEnabled`, `ProcessControlEnabled`,
  `PredictiveMaintenanceEnabled`, `TrainingDataCollectionEnabled`, `IoTimingOptimizerEnabled`,
  `AiPerformanceMonitorEnabled`, `RecipeProductAdvisorEnabled`,
  `MachineHealthNotificationsEnabled`.
- Classificatori ONNX TOP e SIDE: `*ModelPath`, `*ModelVersion`, `*ModelNotes`, `*InputWidth`,
  `*InputHeight`, `*Grayscale`, `*NormalizeMean`, `*NormalizeStd`, `*MinConfidence`, piu'
  `DefectClassifierTraining*` - un modello addestrato e attivato tornava non configurato.
- Soglie e retention: `MachineHealthDigestIntervalMinutes`, `MachineHealthCriticalEtaProducts`,
  `MachineHealthDiskCriticalHours`, `AiInspectionMeasurementRetentionDays`,
  `AiHealthSnapshotRetentionDays`, `AiTrainingSampleRetentionDays`.
- **Non advisory:** `MultiShotCompanionMaxLagMs` (tornava a 15000 ms, cambiando la finestra di
  correlazione tra risultato TOP e companion MultiShot) e `LivePreviewIntervalMs` /
  `LivePreviewPulseMs` / `LivePreviewExposureUs` (esposizione applicata alla camera).

La mappa I/O commissionata (`MachineInputs`, `MachineOutputs`, `EncoderTemplates`,
`InterventionPoints`, `AdditionalRuntimeBindings`) NON era coinvolta: quei rami vengono
ricostruiti dalla vista che era appena stata popolata dal file caricato.

**Correzione:** merge invece di ricostruzione. Aggiungendo in futuro un campo a
`MachineRuntimeBindings` non serve piu' toccare questo metodo perche' il valore sopravviva;
il modo di fallire peggiore possibile diventa "un nuovo campo della vista non viene salvato",
non "una configurazione commissionata viene cancellata".

**Verifica:** build Release x64 exit 0. Conteggio per enumerazione delle proprieta' di
`MachineRuntimeBindings` (76) contro quelle assegnate dalla vista (27) prima e dopo la
modifica; le 49 restanti ora conservano il valore caricato.

**Impatto:**
- Config.xml: nessuno.
- `machine_runtime_config.xml`: nessun campo nuovo o rimosso; cambia solo il fatto che i valori
  esistenti non vengono piu' sovrascritti con i default.
- Database: nessuno.
- API pubblica: nessuna firma cambiata.
- Comportamento runtime: le impostazioni AI/Email/anteprima e `MultiShotCompanionMaxLagMs`
  sopravvivono a riavvio e a salvataggio del pannello I/O.

**Nota / follow-up non incluso in questa modifica:** `NeedsRuntimeBindingSchemaUpgrade` in
`Services/MachineConfigurationService.cs` cerca i marker come sottostringa `<Tag>`, ma
`XmlSerializer` scrive i campi stringa vuoti come `<Tag />`. I marker relativi a campi vuoti
non corrispondono mai, quindi la riscrittura di schema viene eseguita ad ogni avvio invece che
una sola volta. Con questo fix la riscrittura e' innocua (preserva i valori), ma resta una
scrittura su disco non necessaria ad ogni avvio: da valutare separatamente.

## [2026-08-31] Installer 3.1.1.2-r15 - Python macchina, startup ritardato e inventario PC

**Obiettivo:** completare il provisioning del PC macchina senza richiedere
interventi manuali sul `PATH` Python e garantire un avvio HMI automatico ma
ordinato dopo il login Windows.

**Implementazione:**
- ambiente `C:\QtisVision\AI\Python` aggiunto in modo idempotente al `PATH`
  macchina insieme a `Scripts`; create `QTIS_PYTHON_ROOT` e
  `QTIS_PYTHON_EXE`, senza impostare il `PYTHONHOME` globale;
- riallineamento ambiente eseguito sia dopo installazione/riparazione Python,
  sia durante un update HMI quando il Python Qtis e' gia presente;
- nuovo componente `qtis-autostart`, selezionato di default, che installa uno
  script PowerShell incorporato e un collegamento nello Startup comune;
- ritardo iniziale e attesa dipendenze configurabili, pattern servizi default
  `MySQL*`, attesa delle NIC camera dall'ultimo report Cognex, doppio controllo
  istanza e log dedicato con rotazione;
- timeout non bloccante per default: la HMI parte e mantiene la propria
  diagnostica come sorgente autorevole sullo stato camere/DB;
- `--inventory` esteso con produttore, modello, baseboard, BIOS e CPU da SMBIOS,
  inclusa segnalazione dei modelli firmware generici;
- documentato il limite dei sensori: le temperature dipendono dai provider WMI
  storage/ACPI e non vengono stimate quando firmware, RAID, NVMe o DDR5 non le
  espongono;
- media builder incrementato a `r15` e README del supporto esteso con percorsi,
  policy e collaudo.

**Verifica:**
- bootstrapper .NET 8 Release: zero errori e zero warning;
- parser PowerShell superato per builder, rete Cognex e launcher Startup;
- media generato in
  `D:\QtisInstallerOutput\QtisVisionPanel_3.1.1.2-r15`;
- `--validate`: `isValid=true`, zero errori e undici warning operativi;
- `--inventory`: identita SMBIOS e stato HMI presenti;
- 30 payload, `7.766.696.289` byte, SHA-256 setup
  `422D82F170F1543CB9576A19B106D60B84DBB40422091175DF81D17CD4691359`.

**Impatto:** nessuna modifica al ciclo HMI, ricette, DB, I/O, encoder, trigger,
MultiShot o VisionPro. La versione assembly resta `3.1.1.2`; cambia solo la
revisione del media. Lo Startup parte dopo login Windows e puo essere escluso
deselezionando il componente o modificato nel JSON sotto `ProgramData`.

## [2026-08-28] Installer 3.1.1.2-r14 - Update differenziale e rete Cognex GigE

**Obiettivo:** permettere l'aggiornamento di una HMI gia installata toccando
solo i file software realmente cambiati e predisporre il commissioning
automatico delle sole porte Gigabit dedicate alle camere Cognex.

**Correzione:**
- nuovo motore `CoreDifferentialUpdater` con inventario SHA-256, modalita
  `Fresh` / `Upgrade` / `Repair`, blocco downgrade e conteggio dei file
  aggiunti, modificati, rimossi e invariati;
- backup limitato ai file coinvolti, scritture atomiche, journal persistente,
  rollback su errore e recovery automatico dopo interruzione;
- protezione esplicita di `cfg`, `OPC`, cache WebView2 e junction
  `VisionProDependencies`; rimozione controllata dei duplicati ONNX/Cognex
  incompatibili nella root;
- nuovo componente opzionale `cognex-gige-network`, selezionato di default,
  che legge gli attributi rete opzionali da `CameraConfig.xml` e usa un
  riconoscimento conservativo quando non sono presenti;
- associazione NIC tramite nome, GUID o MAC, validazione host/camera IP,
  esclusione assoluta delle porte con default gateway e nessuna modifica in
  caso di match assente o ambiguo;
- backup di IPv4, binding, proprieta OEM e power management prima di applicare
  EEE Off, Interrupt Moderation, Jumbo Packet, buffer, RSS e binding IPv4/eBUS;
- builder riallineato a media `r14` ed esclusione della cache runtime WebView2.

**Verifica:**
- build bootstrapper `.NET 8` Release: zero errori e zero warning;
- media creato in
  `D:\QtisInstallerOutput\QtisVisionPanel_3.1.1.2-r14`;
- 30 payload, `7.766.673.358` byte, builder `PASSED`;
- `--validate`: `isValid=true`, zero errori, dieci warning operativi e zero
  finding bloccanti;
- ZIP applicativo: worker ONNX presente, zero cache WebView2, zero
  `VisionProDependencies` e zero DLL vietate nella root;
- script rete provato in audit e `-Apply` sul PC sviluppo: `NoCandidates`,
  Wi-Fi escluso e nessuna modifica applicata.

**Impatto:** nessuna modifica a versione o comportamento HMI, ricette, DB,
I/O, encoder, trigger, MultiShot o runtime produzione. La versione applicativa
resta `3.1.1.2`; cambia solo la revisione media. Gli attributi rete nel
`CameraConfig.xml` sono facoltativi e retrocompatibili. Firewall, piano energia
globale e parametri VisionPro `Packet Size` / `Latency` restano controlli di
commissioning manuali.

## [2026-08-28] Installer 3.1.1.2-r13 - Supporto offline riallineato

**Obiettivo:** rigenerare il supporto di installazione con la Release x64
ufficiale `3.1.1.2` e il seed operativo corrente, mantenendo invariata la
composizione prerequisiti `r13`.

**Verifica:**
- media creato in
  `D:\QtisInstallerOutput\QtisVisionPanel_3.1.1.2-r13`;
- 30 payload verificati per dimensione e SHA-256;
- builder `PASSED` e bootstrapper `--validate` con `isValid=true`, zero errori
  e otto avvisi operativi attesi;
- l'EXE HMI nel pacchetto coincide con la Release x64, SHA-256
  `92A52EA233028C33BFB47BE9DB05A7C0F0EA2B0ACD8715E62396086AF554B7D4`;
- worker ONNX isolato presente, nessuna DLL ONNX Microsoft nella root e nessuna
  copia di `VisionProDependencies` nello ZIP applicativo;
- seed runtime verificato con `cfg`, `Programs`, `Language` e `AI`.

**Impatto:** nessuna modifica a codice applicativo, config macchina, ricette,
database, I/O, encoder, trigger, MultiShot o comportamento operatore. La
versione HMI resta `3.1.1.2`; `r13` identifica la composizione del media.

## [2026-08-28] Versione 3.1.1.2 - Badge AI reattivo e Data Analysis consolidato

**Obiettivo:** completare il pacchetto lasciato in lavorazione su diagnostica
proattiva, analisi dati ed export senza modificare il ciclo macchina.

**Correzione:**
- `MachineHealthNotificationService` espone un evento thread-safe quando cambia
  il numero di preallarmi non visti;
- la Top Menu aggiorna subito il badge arancione e lo azzera quando l'operatore
  apre o aggiorna il pannello preallarmi;
- tooltip badge aggiunto ai cataloghi ENG/ITA e allo script lingua canonico;
- Data Analysis distingue ricetta attiva e aggregazione di tutte le ricette,
  filtra le feature runtime della ricetta corrente e rende i trend adattivi;
- export PDF impagina fino a due grafici visibili per pagina A4 orizzontale;
- fallback salvataggio immagini riallineato agli alias Side/Left e Rear/Right;
- prerequisiti training aggiornati con il modulo Python `onnxscript`.

**Impatto:** nessuna modifica a config macchina, ricette, DB, I/O, encoder,
trigger, MultiShot, esito, scarto o RunContinuous.

**Verifica richiesta:** badge con warning nuovo/ack, filtri Data Analysis,
PDF con numero pari e dispari di grafici e salvataggio immagini Side/Left e
Rear/Right.

## [2026-08-26] Versione 3.1.1.1 - Esito AI NC per ogni vista camera

**Obiettivo:** archiviare la decisione della classificazione AI con la stessa
semantica `NC_*` delle altre ispezioni, mantenendo separati classe, score ed
esito di ogni vista.

**Correzione:**
- aggiunti a `ProduzioneRecord` gli esiti Top/Top3D, Side/Left, Front,
  Rear/Right e Bottom;
- lo snapshot immutabile del pezzo valorizza ogni esito con `0=NOK`, `1=GOOD`
  oppure `4=non abilitato/non eseguito`;
- un output classe presente ma vuoto o illeggibile resta un errore reale e
  viene archiviato come `0`;
- `tblgenerale` riceve cinque colonne `NC_*AiClassification` tramite migrazione
  idempotente;
- l'INSERT verifica le colonne disponibili e resta compatibile se lo schema non
  e' ancora stato aggiornato;
- DataInspector legge le nuove colonne con proiezione opzionale, quindi continua
  a funzionare anche sui database storici.

**Impatto:** nessuna modifica a ricette, `Config.xml`, configurazione macchina,
I/O, encoder, trigger, MultiShot, contatore AI o logica di scarto. Le righe
storiche non vengono reinterpretate e conservano `NULL` nelle nuove colonne.

**Verifica:** build Release x64 completata con zero errori. Query e matrice di
collaudo aggiornate in
`tblgenerale-production-id-and-classification-2026-08-26.md`.

## [2026-08-26] Versione 3.1.1.0 - Classificazione AI come ispezione autonoma

**Problema rilevato:** il risultato Classify VisionPro era visualizzato per
camera, ma nella validazione storica poteva ereditare `SurfaceCheck` o
`SideSealing`. Non esistevano una coppia abilitazione/scarto di ricetta e un
contatore difetto dedicati.

**Correzione:**
- aggiunti `inspectionStatus.AIClassification` ed
  `ejectionStatus.AIClassification`, entrambi `false` per compatibilita' con le
  ricette XML esistenti;
- validazione comune per Top, Top3D, Side, Left, Front, Rear/Right e Bottom,
  usando gli output Classify dello snapshot VisionPro dello stesso ciclo;
- contatore `AIClassification` dedicato, deduplicato una volta per pezzo e
  persistito come `AI_CLASSIFICATION`;
- card classificazione mostrata soltanto quando il controllo e' abilitato;
- regole di scarto e allarme integrate nella nuova famiglia
  `DefectType.AIClassification`;
- rimossi i mapping storici che associavano Classify a `SurfaceCheck` o
  `SideSealing`;
- testi italiano/inglese e manuale operatore aggiornati.

**Impatto:** nessuna modifica a `Config.xml`, configurazione macchina, I/O,
encoder, quote, trigger o MultiShot. `cfg_inspection` riceve la voce disabilitata
di default; `tblglobalcounters` usa la nuova chiave contatore. Non viene aggiunta
una colonna NC a `tblgenerale`: label e score per vista restano quelli della
release `3.1.0.9`.

**Verifica:** build Release x64 con zero errori. Matrice di collaudo in
`ai-classification-standalone-inspection-2026-08-26.md`.

## [2026-08-26] Versione 3.1.0.9 - Id produzione e Classify per vista in tblgenerale

**Problemi rilevati:**
- `checkIdProd()` risolveva correttamente l'ID della ricetta in
  `tblproduzione`, ma `ProcessInspectionGroupAsync()` sostituiva il record a
  ogni pezzo senza copiare `IdProduzione`; tutte le nuove righe archivio
  ricevevano quindi `0`;
- classe e score VisionPro erano gia' correlati e mostrati nelle card camera,
  ma non facevano parte dello snapshot persistito in `tblgenerale`.

**Correzione:**
- `MainWindow.xaml.cs` conserva l'ID risolto nel record base di ogni ispezione,
  azzera l'identita' precedente prima di risolvere una nuova ricetta e registra
  `PRODUCTION_ID_RESOLVED` / `PRODUCTION_ID_UNRESOLVED`;
- i nomi ricetta DB vengono normalizzati con una sola estensione `.vpp`;
- `ProduzioneRecord` archivia label e score per Top, Side/Left, Front,
  Rear/Right e Bottom usando i risultati Classify dello stesso
  `OutputSnapshot` immutabile validato per il pezzo;
- `Cls_InitializzeDb` applica una migrazione idempotente con dieci colonne
  nullable e mantiene compatibilita' con schemi non ancora aggiornati;
- la verifica delle colonne opzionali durante l'insert usa una sola lettura di
  `information_schema.COLUMNS`, al posto di una query per colonna.

**Impatto:** nessuna modifica a `Config.xml`, file ricetta, VPP, I/O, encoder,
trigger, MultiShot, validazione, contatori o scarto. Le righe storiche con
`IdProduzione=0` non vengono modificate automaticamente. Su viste senza output
Classify, label e score restano `NULL`.

**Verifica:** build Release x64 completata con zero errori. Procedura e query di
collaudo in
`tblgenerale-production-id-and-classification-2026-08-26.md`.

## [2026-08-25] Versione 3.1.0.8 - Fallback I/O sicuro e orchestratore senza lost wake-up

**Problemi rilevati:**
- quando `Recipe_Folder\cfg\io_mapping.xml` non esisteva, il device manager
  attivava una sequenza dimostrativa storica: ogni cambio di `DI00` scriveva
  `DO00` e impulsava `DO01`, anche se quei canali non erano presenti nel mapping
  macchina attivo;
- l'orchestratore multi-camera usava un gate non bloccante. Un risultato TOP
  arrivato durante la chiusura del consumer poteva trovare il gate occupato e
  restare in coda senza un successivo risveglio, causando uno stall/timeout
  intermittente pur con le camere operative.

**Correzione:**
- `Models/AdvantechDeviceManager.cs` tratta `io_mapping.xml` come estensione
  legacy opzionale: se assente, nullo o illeggibile usa collezioni vuote e non
  comanda alcuna uscita. I mapping esplicitamente presenti restano supportati;
- aggiunti i log `IO_MAPPING_LOADED`, `IO_MAPPING_NOT_CONFIGURED` e
  `IO_MAPPING_LOAD_FAILED`;
- `Services/InspectionOrchestrator.cs` registra le richieste di drain arrivate
  mentre il gate e' occupato e ricontrolla la coda TOP dopo il rilascio;
- `COMPANION_TIMEOUT` include ora `topSequence` e lo stato delle code companion
  (`count`, `headSequence`, `lagMs`, `valid`) per distinguere coda vuota,
  risultato futuro/non valido e mancato consumer.

**Impatto:** nessuna modifica a `Config.xml`, `machine_runtime_config.xml`,
ricette, DB, quote encoder, punti intervento, durata impulsi o mapping fisico
configurato. `DO00`/`DO01` vengono usati solo se referenziati esplicitamente da
una configurazione macchina o da un `io_mapping.xml` valido.

**Verifica:** build Release x64 completata con zero errori. In collaudo, senza
`io_mapping.xml`, deve comparire una sola volta `IO_MAPPING_NOT_CONFIGURED` e il
fronte della fotocellula non deve produrre scritture su DO non configurate.

## [2026-08-24] Versione 3.1.0.7 - Correlazione immutabile risultati VisionPro

**Problema:** `UserResultAvailable` metteva in coda il record grafico corretto,
ma conservava un riferimento al ToolBlock vivo del job. Con acquisizioni rapide
o code sintetiche, VisionPro poteva aggiornare quel ToolBlock prima della
validazione: immagine, classe/score e messaggi HMI potevano quindi descrivere
cicli differenti.

**Correzione:**
- `Services/VisionToolBlockOutputSnapshot.cs` copia nel callback tutti gli
  output scalari del ciclo in un ToolBlock leggero e indipendente;
- `Models/CameraResult.cs` separa il ToolBlock live dallo snapshot e assegna una
  sequenza progressiva al risultato;
- `MainWindow.xaml.cs` valida esclusivamente gli snapshot, li rilascia in modo
  deterministico e registra la sequenza nei log di enqueue;
- `Services/InspectionOrchestrator.cs` registra sequenza TOP e companion e
  rilascia anche gli snapshot eliminati dal coalescing simulazione;
- `VisionClassificationResultReader` usa l'esito `Classification` del validator
  per il colore della card: classi difetto libere, come `Sealing Open`, risultano
  NO GOOD se il validator le ha rifiutate;
- manuale, commissioning e architettura aggiornati.

**Impatto:** nessuna modifica a VPP, XML, ricetta, DB, mapping I/O, encoder,
trigger, MultiShot o regole di scarto. Il ToolBlock live rimane disponibile ai
fallback immagine/Top3D; solo la validazione usa la copia coerente con il record.

## [2026-08-24] Versione 3.1.0.6 - Card classificazione AI su tutte le viste camera

**Obiettivo:** mostrare in modo uniforme classe e score prodotti dai
classificatori VisionPro nelle feature delle camere, seguendo la presentazione
compatta delle misure Top3D e senza introdurre una seconda logica di scarto.

**File modificati:**
- `Models/CameraClassificationResult.cs` e
  `Services/VisionClassificationResultReader.cs` - contratto informativo comune
  e lettura case-insensitive degli output classe/score;
- `DataManage/InspectionProcessor.cs` - acquisizione del risultato per ogni
  ToolBlock gia' validato, senza inferenza o scansione dei tool interni;
- `Views/UserControls/DisplayRecord/ClassificationFeatureCard.*` - card
  responsive con icona vettoriale AI, classe, score e badge;
- viste `Top`, `Side`, `Left`, `Front`, `Rear`, `Right`, `Bottom` e
  `Services/CameraDisplayManager.cs` - integrazione e routing runtime;
- `ServerMessage`, JSON lingua e script aggiornamento lingue - testi inglesi e
  italiani;
- manuale operatore e guida commissioning - contratto ToolBlock e procedura di
  test;
- `Installer/Build-QtisInstallerMedia.ps1` e `Installer/README.md` - media
  offline `3.1.0.6-r11` validato con 30 payload.

**Contratto VisionPro:** classe consigliata `EL_Classify`, score consigliato
`EL_Score`; sono accettati anche `Classify`/`Classification`/`Class` e
`Score`/`ClassificationScore`/`Confidence`.

**Impatto:** nessuna nuova chiave XML, campo ricetta o migrazione DB. La card e'
informativa: non modifica validazione, NOK, contatori, scarto, I/O, encoder,
trigger, MultiShot o pairing multi-camera.

## [2026-08-24] Versione 3.1.0.5 - Prestazioni ViDi EL allineate a QuickBuild

**Problema:** sullo stesso PC e sullo stesso VPP, il `CogClassifyTool` richiedeva
18-25 ms in QuickBuild ma poteva arrivare a 6-8 s dentro la HMI. La baseline
storica impostava almeno 24 thread del ThreadPool e subito dopo limitava tutto
`QtisVisionPanel.exe` ai soli logical processor 0 e 1 con
`ProcessorAffinity=0x0003`. VisionPro, UI e servizi macchina si contendevano
quindi due soli processori dei 12 disponibili.

**File modificati:**
- `MainWindow.xaml.cs` - rimosso il vincolo hardcoded a due core; Windows puo'
  usare tutti i processori concessi al processo. Il minimo ThreadPool viene
  mantenuto almeno pari al numero di logical processor, senza raddoppio
  arbitrario. Aggiunto il log `VISION_RUNTIME_SCHEDULER` con affinity e minimi
  effettivi;
- `Docs/Commissioning/13_VisionPro_ViDi_Classify_Performance.md` - benchmark,
  procedura di collaudo Release senza debugger e diagnosi di warm-up, runtime e
  contesa;
- `Documentation/MachineHardware/architecture-and-modules.md` - issue A2 chiusa;
- `Properties/AssemblyInfo.cs` - release `3.1.0.4` -> `3.1.0.5`.

**Verifica controllata:** sul PC i7-1355U, caricando direttamente
`Lucart_Test_sample.vpp`, il classificatore caldo ha misurato 11-25 ms con
affinity completa (`0xFFF`) e 489-628 ms con il vecchio limite (`0x003`). Il job
Side sintetico completo e' passato da circa 560-790 ms a circa 279 ms. Il primo
ciclo, escluso dalle medie, ha richiesto 1-2 s per warm-up.

**Impatto:** nessuna modifica a VPP, modello Classify, soglie, esiti, XML,
ricette, DB, mapping I/O, encoder, trigger, MultiShot o scarto. Cambia solo la
disponibilita' CPU del processo HMI. Il collaudo finale va eseguito in Release
x64 senza debugger e con QuickBuild chiuso.

## [2026-08-24] Versione 3.1.0.4 - Pairing stabile dei job VisionPro sintetici multi-camera

**Problema:** in sviluppo, con job `Top` e `Side` basati su
`CogAcqFifoSynthetic`, le due acquisizioni continue avanzano in modo indipendente.
Se Top e' piu' veloce, la sua coda cresce mentre Side sta ancora elaborando. Il
runtime precedente applicava anche alla simulazione la finestra hardware di 5 s:
il companion poteva quindi essere dichiarato mancante e il watchdog avviava una
recovery, anche se entrambi i job erano ancora validi.

**File modificati:**
- `Models/CameraResult.cs` - ogni risultato conserva se proviene da una FIFO
  sintetica;
- `Services/InspectionOrchestrator.cs` - i job sintetici vengono accoppiati in
  ordine FIFO, senza usare timestamp indipendenti come identita' prodotto; il
  backlog sintetico viene limitato e i record scartati vengono rilasciati;
- `MainWindow.xaml.cs` - il tipo di runtime viene rilevato e cachato all'avvio;
  quando tutti i job attivi sono sintetici, attesa companion e watchdog usano 30
  s. Hardware single-shot resta a 5 s e hardware MultiShot resta a 30 s;
- `Docs/Commissioning/12_VisionPro_Synthetic_MultiCamera_Simulation.md` - nuova
  procedura di test e interpretazione dei log;
- `Installer/Build-QtisInstallerMedia.ps1` e `Installer/README.md` - media
  offline aggiornato e validato come `3.1.0.4-r9`;
- `Properties/AssemblyInfo.cs` - release `3.1.0.3` -> `3.1.0.4`.

**Verifica:** build Release x64 completata con 0 errori. Una prova runtime con
Top/Side sintetici ha confermato `VISION_SIMULATION_PAIRING_ACTIVE` e
`VISION_SIM_QUEUE_COALESCED`; la finestra finale di 30 s deve essere ricontrollata
avviando la produzione simulata con il VPP interessato. Media installer
`3.1.0.4-r9` creato e validato con 30 payload; l'EXE nello ZIP coincide con la
Release x64.

**Impatto:** nessuna modifica a XML, DB, ricette, mapping I/O, trigger, quote o
MultiShot. La nuova politica e' attiva solo se tutti i job VisionPro caricati
usano acquisizione sintetica; basta un job hardware per mantenere il contratto
temporale di produzione.

## [2026-08-21] Versione 3.1.0.3 - RunOnce per job e MultiShot ricetta valido dal primo avvio

**Problema:** dal Job Tool Editor il comando `RunOnce` chiamava
`CogJobManager.Run()`, quindi tentava di avviare tutti i job del VPP. Un job non
pronto o non coerente con la configurazione poteva far fallire anche il test della
sola camera selezionata. L'eccezione veniva inoltre assorbita e il log riportava
soltanto lo stack, seguito dal falso messaggio di esecuzione avviata.

Il log allegato mostrava anche `VISIONPRO_STITCHING_TOOLBLOCK_NOT_FOUND` per
`Side`: la ricetta `Lucart_Test_sample.xml` disabilita `SideLeft`, ma al primo
avvio il ViewModel I/O precaricato poteva ancora esporre i default macchina con
MultiShot attivo quando iniziava la validazione VisionPro.

**File modificati:**
- `Cls_Vpro/ICognexJobManager.cs` - `RunOnce` riceve l'indice job, ferma lo stato
  tecnico precedente, attende la FIFO GigE selezionata e chiama `CogJob.Run()`;
  log strutturati con eccezione Cognex completa e propagazione dell'errore alla UI;
- `ViewModels/JobToolEditorViewModel.cs` - usa la tab selezionata, abilita il
  comando solo per utente tecnico con job valido e registra successo solo dopo
  una chiamata riuscita;
- `MainWindow.xaml.cs` - applica gli override runtime della ricetta prima del
  bootstrap/controllo MultiShot VisionPro al primo avvio;
- `Docs/Commissioning/10_Job_Editor_Live_Preview_Hardware_Trigger.md` - procedura
  RunOnce e diagnosi coerenza MultiShot/ImageStitching;
- `Properties/AssemblyInfo.cs` - release `3.1.0.2` -> `3.1.0.3`.

**Verifica:** build Release x64 completata con 0 errori. Il collaudo fisico del
RunOnce e del trigger hardware resta da eseguire sulla macchina con la camera
selezionata.

**Impatto:** nessuna modifica a schema XML, DB, mapping I/O, quote, impulsi o
algoritmo MultiShot. Cambiano l'isolamento del test tecnico sul singolo job e
l'ordine con cui la configurazione ricetta gia' esistente viene applicata in
memoria durante il primo avvio.

## [2026-08-21] Versione 3.1.0.2 - Fix definitivo caricamento VPP ViDi EL Classify

**Problema:** anche dopo l'isolamento del runtime ONNX HMI, il VPP
`Lucart_Test_sample.vpp` continuava a fallire nell'applicazione con
`TargetInvocationException -> Cognex.Vision.SerializationException: Error loading
object`, pur aprendosi in QuickBuild e nella copia applicativa `3.0.2.4`.

**Causa verificata con confronto A/B:** `Cognex.Vision.Startup.Net.dll` era stata
marcata `Copy Local` e veniva caricata dalla directory di `QtisVisionPanel.exe`.
Questa posizione cambiava la risoluzione delle dipendenze native avviata da Cognex:
RBBT partiva, ma ViDi EL Classify e la `onnxruntime.dll` Cognex non venivano caricati.
Nella copia funzionante la stessa DLL era risolta attraverso la junction
`VisionProDependencies`. Eliminando soltanto la copia locale, lo stesso probe e lo
stesso VPP caricavano correttamente i job `Top,Side`.

**File modificati:**
- `QtisVisionPanel.csproj` - Startup Cognex riportata a `Private=False`; target di
  pulizia esteso alle copie Startup residue oltre alle DLL ONNX Microsoft;
- `Services/VisionProRuntimeBootstrap.cs` - guardia sulla posizione della DLL,
  diagnostica dei moduli nativi RBBT/ViDi/ONNX e log del runtime effettivo;
- `Cls_Vpro/ICognexJobManager.cs` - errore VPP arricchito con le posizioni runtime;
- `Installer/Build-QtisInstallerMedia.ps1` - media `r7` e blocco della DLL Startup
  locale nella Release e nello staging;
- documentazione installer/ViDi EL e archivio versioni aggiornati;
- `Properties/AssemblyInfo.cs` - release `3.1.0.1` -> `3.1.0.2`.

**Verifica:** confronto positivo con la copia `3.0.2.4`; build Release e Debug x64
con 0 errori; probe HMI-equivalente sul VPP reale riuscito in entrambe le configurazioni
con 2 job e moduli Classify/ONNX caricati da `VisionProDependencies`.

**Impatto:** nessuna modifica a VPP, ricette, `Config.xml`, machine runtime XML,
database, I/O, encoder, trigger o ciclo di ispezione. La correzione riguarda solo il
layout delle dipendenze Cognex e impedisce una configurazione di deploy non valida.

## [2026-08-21] Versione 3.1.0.1 - Isolamento ONNX per VPP ViDi EL Classify

**Problema:** nonostante il bootstrap anticipato `VProX`, i VPP con
`CogClassifyTool` continuavano a fallire solo nell'HMI con
`TargetInvocationException -> Cognex.Vision.SerializationException: Error loading
object`, mentre gli stessi file funzionavano in VisionPro QuickBuild.

**Causa verificata:** la directory dell'EXE conteneva la `onnxruntime.dll` Microsoft
1.19.2 usata dal classificatore shadow-mode dell'HMI. Il loader nativo Windows la
selezionava prima della versione Cognex in `VisionProDependencies`; ViDi EL caricava
quindi una ABI ONNX incompatibile durante la deserializzazione RBBT. Una vecchia build
senza ONNX Microsoft in root caricava lo stesso VPP correttamente.

**File modificati:**
- `AI/QtisVisionPanel.OnnxWorker/*` - nuovo worker x64 dedicato per caricamento,
  validazione e inferenza dei modelli HMI;
- `Services/OnnxInferenceWorkerClient.cs` - protocollo locale serializzato, recovery
  processo, limite memoria, timeout e cache modelli;
- `Services/OnnxDefectClassifier.cs` - shadow-mode delegato al worker, senza caricare
  Microsoft ONNX Runtime nel processo VisionPro;
- `Services/DefectClassifierTrainingService.cs` - validazione del modello generato nel
  worker isolato;
- `QtisVisionPanel.csproj` / `.sln` - progetto worker, rimozione package ONNX dal main e
  pulizia preventiva delle DLL ONNX Microsoft dalla root di output;
- `Installer/Build-QtisInstallerMedia.ps1` - media revision `r6`, presenza worker
  obbligatoria e build bloccata se una DLL ONNX Microsoft compare accanto all'HMI;
- `Properties/AssemblyInfo.cs` - release `3.1.0.0` -> `3.1.0.1`;
- documentazione tecnica e archivio release aggiornati.

**Verifica:** build Release x64 completata con 0 errori; il VPP reale
`C:\QtisVision\Programs\Lucart_Test_sample.vpp` viene deserializzato senza
`SerializationException`; il worker ha superato `PING`, load modello e inferenza reale
(`RESULT class=0 confidence=1`). Le DLL Microsoft ONNX risultano assenti dalla root
Release e presenti esclusivamente in `AiRuntime`.

**Impatto:** nessuna modifica a ricette, `Config.xml`, machine runtime XML, database,
I/O, encoder, trigger o logica esito. ONNX resta advisory e non bloccante. Se e'
disabilitato non viene avviato alcun processo aggiuntivo; se fallisce, VisionPro e la
produzione restano indipendenti.

## [2026-08-21] Versione 3.1.0.0 - CogClassifyTool ViDi EL nell'editor VisionPro integrato

**Problema:** il template `CogClassifyTool` poteva essere aggiunto correttamente in
VisionPro QuickBuild ma falliva nel `Job Tool Editor` incorporato con popup
`Failed to Load Tool Template` e catena di eccezioni `CogPathException`,
`Cognex.Vision.SerializationException` e `TargetInvocationException`. Anche i VPP
contenenti un classificatore persistito potevano fallire durante la deserializzazione.

**Causa:** VisionPro veniva inizializzato in piu punti e dopo il preload di componenti
dell'applicazione. Lo splash usava inoltre un valore `ProductKey` vuoto con security
disabilitata. Questa sequenza non rispettava il contratto del sample Cognex ViDi EL, che
richiede `Startup.Initialize(ProductKey.VProX)` prima di qualsiasi tipo VisionPro,
ToolBlock o template Classify.

**File modificati:**
- `Services/VisionProRuntimeBootstrap.cs` - nuovo bootstrap idempotente `VProX`, attesa
  security limitata a 30 secondi, caricamento delle estensioni editor Classify e formatter
  ricorsivo delle eccezioni Cognex/loader;
- `App.xaml.cs` - bootstrap Cognex anticipato prima dello splash e del preload ViewModel;
- `SplashWindow.xaml.cs` - rimossa l'inizializzazione duplicata/non corretta e il processo
  esterno `cogtool --print`;
- `Cls_Vpro/ICognexJobManager.cs` - caricamento VPP allineato al bootstrap unico e log con
  inner exception, HResult e LoaderExceptions;
- `Views/JobToolEditorView.xaml.cs` - estensioni ViDi EL preparate prima della costruzione
  del controllo editor;
- `QtisVisionPanel.csproj` - riferimento ufficiale a
  `Cognex.VisionPro.ViDiEL.Controls.dll`;
- `Properties/AssemblyInfo.cs` - release `3.0.9.9` -> `3.1.0.0`;
- documentazione tecnica e archivio release aggiornati.

**Verifica:** build `Release x64` completata con 0 errori. Con bootstrap `VProX`
anticipato sono stati deserializzati correttamente il template ufficiale
`Templates/Tools/ViDiEL/CogClassifyTool.vtt`, `Lucart_Test_sample.vpp` e
`Index_Fiera_sample.vpp` (2 job ciascuno), senza avviare acquisizione o RunContinuous.
Il collaudo finale resta da eseguire nella palette embedded con la licenza e il modello
della macchina, seguendo
`visionpro-vidiel-classify-embedded-editor-2026-08-21.md`.

**Impatto:** nessuna modifica a `Config.xml`, machine runtime XML, ricette, database,
mapping I/O, encoder, trigger o ciclo ispezione. L'inizializzazione Cognex viene eseguita
una sola volta all'avvio del processo e riutilizzata da splash, editor e caricatore VPP.

## [2026-08-20] Versione 3.0.9.9 - Corretta race di classificazione GigE all'avvio (job Rear/FLIR)

Causa aggiuntiva individuata sullo stesso meccanismo introdotto in 3.0.9.4
(`visionpro-three-camera-startup-hardening-2026-08-19.md`), riprodotta sul campo: il
job `Rear` (camera FLIR/Blackfly GigE) falliva l'avvio in `RunContinuous` con
`VISIONPRO_JOB_START_RETRY|...requires AcqFifo to run in AcquisitionAndImageProcessing
RunMode` in loop, SENZA che comparisse alcun log `VISIONPRO_GIGE_DISCOVERY_WAIT` /
`VISIONPRO_VPP_RELOAD_*` - cioe' l'intero meccanismo di recovery della 3.0.9.4 non
veniva mai eseguito.

**Causa:** `InitializeCognexManagerWithGigERecoveryAsync` chiamava
`GetInvalidGigEAcquisitionJobs()` subito dopo `CreateAndInitializeCognexManager`, senza
attesa. Questo metodo (e `GetGigEAcquisitionJobCount()`) filtrano tramite
`IsGigEAcquisitionJob`, che riconosce un job come GigE solo quando il suo `AcqFifo` e'
gia' risolto al tipo concreto `CogAcqFifoGigE`. Subito dopo la deserializzazione del VPP,
`job.AcqFifo` per Rear puo' essere ancora `null`: in quel caso `IsGigEAcquisitionJob`
restituisce `false` e il job viene escluso dalla lista degli invalidi - non perche'
pronto, ma perche' non ancora riconosciuto come GigE. Con `invalidJobs.Count == 0` il
metodo tornava subito, saltando l'intero meccanismo di recovery della 3.0.9.4. Quando
`TryStartJobContinuous` veniva poi chiamato per Rear (dopo altri passi di avvio), il FIFO
risultava gia' correttamente tipizzato `CogAcqFifoGigE`, ma con `AcqFifoState` ancora
`Invalid` - esattamente il caso che la recovery della 3.0.9.4 gestisce, raggiunto pero'
troppo tardi e senza alcun reload tentato.

**File modificati:**
- `MainWindow.xaml.cs` - `InitializeCognexManagerWithGigERecoveryAsync` attende ora
  `GigEStartupBindingSettleMs` (gia' usato piu' avanti nello stesso metodo) prima della
  primissima classificazione `GetInvalidGigEAcquisitionJobs()`, cosi' un FIFO Rear non
  ancora tipizzato non viene scambiato per "non e' un job GigE". Nessuna modifica a
  `IsGigEAcquisitionJob`, `IsAcquisitionReady`, al ciclo di reload o all'avvio di
  qualsiasi altra camera.
- `Documentation/MachineHardware/visionpro-three-camera-startup-hardening-2026-08-19.md` -
  aggiunta sezione con questa causa e la nuova evidenza di log attesa.
- `Properties/AssemblyInfo.cs` - release `3.0.9.8` -> `3.0.9.9`.

**Verifica:** compilazione Release x64 riuscita (exit 0, su OutDir separato). Da
confermare in produzione: dopo il riavvio, la riga `Job Rear has invalid AcqFifoState:
Invalid` deve essere seguita da `VISIONPRO_GIGE_DISCOVERY_WAIT` (o direttamente da
`VISIONPRO_GIGE_BINDING_READY`), non piu' da `VISIONPRO_JOB_START_RETRY` immediato;
infine tutti e tre i job (`Top`, `Left`, `Rear`) devono raggiungere
`VISIONPRO_JOB_RUNNING` e `VISIONPRO_CONTINUOUS_START_COMPLETE|jobs=3`.

**Impatto:**
- Config.xml: nessuno.
- Database: nessuno.
- API pubblica: nessuna firma cambiata.
- Comportamento runtime: la finestra di attesa GigE gia' esistente viene ora applicata
  anche prima della primissima verifica di avvio, non solo nei tentativi successivi.
  Nessun cambiamento per camere non-GigE o per VPP dove tutti i FIFO sono gia' validi al
  primo caricamento (percorso rapido invariato, solo posticipato di
  `GigEStartupBindingSettleMs`).

## [2026-08-20] Versione 3.0.9.8 - Corretto ordine configurazione GenICam Cycling Preset (Left dual illumination)

Causa aggiuntiva individuata per lo stesso sintomo di
`left-dual-multishot-no-trigger-diagnosis-2026-08-19.md`, verificata sul campo anche a
macchina ferma (quindi non spiegabile con il "Node is not writable" da acquisizione attiva
gia' documentato per il caso a macchina in marcia).

**Causa:** `IgigaCameraAccess.ConfigureDualIlluminationCyclingPresets` attivava
`cP_FeaturesActivationSelector`/`cP_FeaturesActivationMode` per ExposureTime e per le due
linee di output PRIMA di selezionare un preset con `cP_PresetConfigurationSelector`.
L'esempio ufficiale Teledyne per il Cycling Preset (Multi-Exposure Cycling Example Setup)
documenta l'ordine opposto: selezionare prima il preset, poi attivare la feature, poi
scrivere il valore. Con l'ordine invertito, il nodo per-preset `cP_ExposureTime` risultava
non scrivibile/non trovato. Confermato anche via CamExpert sulla camera Left reale
(Teledyne DALSA Nano-M1450, firmware `10CA18.0008`): `cP_ExposureTime` con
`access=RO` e `cP_FeaturesActivationMode=Off` per il selettore corrente.

**File modificati:**
- `Cls_Vpro/IgigaCameraAccess.cs` - `ConfigureDualIlluminationCyclingPresets` imposta ora
  `cP_PresetConfigurationSelector=1` prima delle chiamate `ActivateCyclingFeature` per
  ExposureTime e per le linee Front/Back, allineando la sequenza a quella documentata da
  Teledyne. Nessun'altra modifica alla logica esistente (contatore preset 2, sorgente
  incrementale StartOfFrame, reset software, mapping linee).
- `Documentation/MachineHardware/left-dual-multishot-no-trigger-diagnosis-2026-08-19.md` -
  aggiunta sezione con questa causa aggiuntiva, il riferimento CamExpert e il nuovo punto
  di verifica.
- `Properties/AssemblyInfo.cs` - release `3.0.9.7` -> `3.0.9.8`.

**Verifica:** compilazione Release x64 riuscita (exit 0, su OutDir separato). Non
riproducibile in laboratorio senza hardware DALSA Left/Dual: da confermare in produzione
ricaricando il job Left a macchina ferma e verificando `CAMERA_CYCLING_PRESETS_CONFIGURED`
al posto di `CAMERA_CYCLING_PRESET_CONFIG_FAILED|...feature not found: cP_ExposureTime`,
poi ripetendo la prova con 8 scatti fisici su DO02 descritta nel documento di diagnosi.

**Impatto:**
- Config.xml: nessuno.
- Database: nessuno.
- API pubblica: nessuna firma cambiata.
- Comportamento runtime: la configurazione dei Cycling Preset DALSA per il profilo Left
  dual illumination segue ora l'ordine documentato da Teledyne. Nessun cambiamento per
  camere/profili che non usano Dual Illumination.

## [2026-08-20] Versione 3.0.9.7 - Rimosso output spare DO00 generato di default all'avvio

**Causa:** su questa macchina DO00/DO01 non sono cablati fisicamente, ma l'utente osservava
il warning `[IO] Output signal not configured: OUT_SPARE_DO00` ricomparire ad ogni riavvio
anche dopo aver cancellato manualmente la voce dal file di configurazione. La causa erano
due generatori indipendenti, entrambi attivi indipendentemente dal contenuto del file XML:
1. `DigitalIOViewModel.InitializeMachineHardwareTemplate()` (eseguito incondizionatamente nel
   costruttore, ad ogni avvio) aggiungeva un `MachineSignalDefinition` hardcoded per
   `OUT_SPARE_DO00` alla collezione `MachineOutputs` in memoria, prima ancora che il file di
   configurazione venisse caricato — quindi la cancellazione manuale dal file non aveva
   effetto: la voce veniva ricreata in RAM ad ogni avvio indipendentemente dal contenuto su
   disco.
2. `ParkUnusedSpareOutputsLowAsync(reason)`, chiamato da 4 punti (init runtime macchina,
   caricamento/salvataggio configurazione, riapplicazione live della preview), forzava
   esplicitamente a livello basso l'output `OUT_SPARE_DO00` e generava il warning
   `[IO] Output signal not configured` ogni volta che non lo trovava configurato.

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`:
  - Rimossa la definizione hardcoded di `OUT_SPARE_DO00` da
    `InitializeMachineHardwareTemplate()`. `OUT_CAMERA_SIDE_TRIGGER` (segnale reale, DO01,
    usato altrove) non e' stato toccato.
  - `ParkUnusedSpareOutputsLowAsync` trasformato in no-op documentato (rimosso `async`,
    ritorna direttamente `Task.CompletedTask`); firma invariata cosi' i 4 punti di chiamata
    esistenti non richiedono modifiche.
  - Rimosso codice morto gia' commentato in `MigrateLegacyTwoCameraRuntimeConfiguration()`
    che referenziava `OUT_SPARE_DO00` (righe gia' disattivate da una sessione precedente).
- `Properties/AssemblyInfo.cs` - release `3.0.9.6` -> `3.0.9.7`.

**Verifica:** `grep -r "OUT_SPARE_DO00|OUT_SPARE"` su `*.cs/*.xaml/*.xml/*.json` non
restituisce piu' alcun riferimento funzionale (solo il commento esplicativo lasciato nel
metodo no-op). Compilazione Release x64 riuscita (exit 0, su OutDir separato
`_buildcheck`). Da verificare sul campo: riavviare l'applicazione e confermare che il
warning `Output signal not configured: OUT_SPARE_DO00` non compaia piu' nei log, e che una
cancellazione manuale di una voce dal file di configurazione non venga piu' ricreata al
riavvio successivo.

**Impatto:**
- Config.xml: nessuno (nessun campo nuovo o rimosso; il file operativo non conteneva gia'
  `OUT_SPARE_DO00` di default, era solo generato in RAM dal costruttore del ViewModel).
- Database: nessuno.
- API pubblica: nessuna firma cambiata (`ParkUnusedSpareOutputsLowAsync` mantiene lo stesso
  tipo di ritorno `Task`).
- Comportamento runtime: non viene piu' generato/riproposto l'output spare `OUT_SPARE_DO00`
  ne' il relativo warning; le cancellazioni manuali dal file di configurazione ora
  persistono tra un riavvio e l'altro. Nessun cambiamento per `OUT_CAMERA_SIDE_TRIGGER`
  (DO01) o per qualunque altro segnale reale.

## [2026-08-20] Versione 3.0.9.6 - Left dual MultiShot: armo del profilo fuori dal thread di polling

Causa aggiuntiva individuata per lo stesso sintomo di
`left-dual-multishot-no-trigger-diagnosis-2026-08-19.md` ("uscita fisica attiva una sola
volta", VisionPro fermo a `Dual frame 1/8`), verificata sul campo anche dopo il fix
timeout scorrevole della 3.0.9.5.

**Causa:** `MultiShotTriggerController.ArmProfile` invoca in modo SINCRONO
`DualIlluminationPhaseCoordinator.PrepareSession`, che quando `DualIllumination.Enabled=true`
esegue comandi GenICam reali verso la fotocamera (reset del cycling preset Front/Backlight)
prima di ogni prodotto. `ArmProfile` viene chiamato da `OnProductTriggerReceived`, che a sua
volta e' invocato inline dallo STESSO thread hardware che consegna ANCHE tutti gli eventi
encoder della macchina (`DigitalIOViewModel.OnCounterChanged` -> tutti i profili MultiShot,
non solo Left). Se il comando GenICam e' lento o si blocca, quel thread resta fermo e nessun
ulteriore evento encoder viene consegnato per NESSUN profilo finche' il comando non ritorna:
il primo scatto (gia' in coda) parte, ma i successivi non vengono mai processati finche' il
blocco persiste. In modalita' single-shot `PrepareSession` e' un no-op immediato e il problema
non si manifesta mai: coerente con "single fa tutti gli scatti, dual si ferma al primo".

**File modificati:**
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - `OnProductTriggerReceived` non
  chiama piu' `ArmProfile` in modo sincrono sul thread chiamante: l'armo del profilo (incluso
  l'eventuale reset camera Dual Illumination) e la prima valutazione encoder vengono ora
  eseguiti su un task in background, per profilo. Il thread di polling hardware condiviso
  torna quindi libero indipendentemente dai tempi di risposta della fotocamera. Nessuna
  modifica alla logica di calcolo target/encoder/timeout (gia' corretta e invariata dalla
  3.0.9.5); nessun cambiamento di comportamento in modalita' single-shot (il gate resta
  immediato). Sicuro sotto trigger ravvicinati: `DualIlluminationPhaseCoordinator` rifiuta
  gia' una seconda `PrepareSession` mentre `SessionActive=true`, e `ArmProfile` cancella/
  sostituisce sotto lock qualunque sessione precedente per lo stesso profilo.
- `Documentation/MachineHardware/left-dual-multishot-no-trigger-diagnosis-2026-08-19.md` -
  aggiunta sezione con questa causa aggiuntiva e il nuovo punto di verifica nei log.
- `Properties/AssemblyInfo.cs` - release `3.0.9.5` -> `3.0.9.6`.

**Verifica:** compilazione Release x64 riuscita (exit 0, su OutDir separato). Non
riproducibile in laboratorio senza hardware DALSA Left/Dual: da confermare in produzione
seguendo la sequenza log gia' descritta nel documento di diagnosi (8x `TRIGGER_PULSE` fino a
`SESSION_COMPLETED`) e verificando che il thread di polling non risulti piu' fermo durante il
reset camera.

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessuna modifica.
- Database: nessuna modifica.
- API pubblica: firma di `OnProductTriggerReceived` invariata; il valore di ritorno non e'
  comunque consumato dall'unico chiamante (`DigitalIOViewModel`).
- Comportamento runtime: nessun cambiamento in modalita' single-shot. In modalita' Dual
  Illumination, il thread di polling encoder/IO condiviso non puo' piu' essere bloccato da un
  comando fotocamera lento o bloccato.

## [2026-08-20] Versione 3.0.9.5 - Timeout progressivo Left dual MultiShot

**File modificati:**
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - il timeout viene
  calcolato dalla fotocellula solo prima del primo scatto e, dopo ogni impulso,
  dall'ultimo avanzamento della sequenza;
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - la cancellazione
  per timeout ricontrolla atomicamente la sessione per non annullare un impulso
  partito mentre il timer stava elaborando un candidato scaduto;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.9.5`;
- guide MultiShot, diagnosi Left e archivio versioni aggiornati.

**Motivo:** con un primo target vicino alla scadenza, il vecchio watchdog
continuava a misurare `SessionTimeoutMs` dal fronte fotocellula anche dopo il
primo impulso. La sessione poteva quindi essere annullata a `frameIndex=1`
nonostante l'encoder stesse proseguendo verso gli altri sette target. Esisteva
inoltre una finestra di race tra selezione della sessione scaduta e
cancellazione.

**Impatto:**
- nessuna modifica a mapping I/O, `DO02`, quote, step, numero scatti, Cycling
  Preset DALSA, ToolBlock VisionPro, ricette, `Config.xml` o database;
- `SessionTimeoutMs` resta il limite per raggiungere il primo target; dopo il
  primo scatto diventa il massimo intervallo senza un nuovo impulso;
- il timeout riporta ora fase, tempo senza avanzamento e ultimo conteggio
  encoder, rendendo distinguibili encoder fermo e arresto del runtime;
- test isolato con timeout `300 ms`: primo target a `230 ms`, 8 impulsi totali
  distribuiti oltre la vecchia scadenza, `8 HIGH`, una sessione completata e
  zero timeout.

**Verifica:** build `Release x64` completata con 0 errori. Resta obbligatorio
il collaudo hardware su `OUT_CAMERA_LEFT_TRIGGER`: otto eventi
`MULTISHOT_SIDE_TRIGGER_PULSE`, `frameIndex=8`, alternanza 4 Front / 4
Backlight e `isReady=true`.

## [2026-08-20] Versione 3.0.9.4 - Binding GigE Rear con reload VPP ripetibile

**File modificati:**
- `MainWindow.xaml.cs` - dopo la discovery stabile distingue l'inventario
  globale dal binding della FIFO del singolo job, attende il settle iniziale e,
  se Rear resta `Invalid`, esegue fino a tre reload completi del VPP;
- `MainWindow.xaml.cs` - prima di ogni reload rilascia il manager Cognex,
  scollega il runtime, azzera job/ToolBlock statici e attende il rilascio delle
  risorse GigE;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.9.4`;
- documentazione di bootstrap, collaudo e archivio versioni aggiornata.

**Motivo:** la `3.0.9.2` attendeva correttamente tre FrameGrabber nell'inventario
Cognex, ma ricaricava il VPP una sola volta e subito dopo la discovery. La
Blackfly poteva essere gia' enumerata mentre la FIFO FGGigE del job `Rear` era
ancora `Invalid` con `grabber=pending; serial=pending`. Il cambio ricetta
manuale riusciva perche' avveniva piu' tardi e rilasciava tutti i riferimenti
del manager precedente.

**Impatto:**
- nessuna modifica a VPP, ricette, `Config.xml`, configurazione macchina, I/O,
  encoder, MultiShot, database o risultati ispezione;
- nessun reload aggiuntivo quando le FIFO sono valide al primo caricamento;
- recovery limitata a tre reload, senza loop infinito e senza avvio parziale;
- nuovi log `VISIONPRO_GIGE_BINDING_READY`,
  `VISIONPRO_GIGE_BINDING_PENDING_AFTER_DISCOVERY`,
  `VISIONPRO_GIGE_MANAGER_RELEASED`, `VISIONPRO_GIGE_MANAGER_RELEASE_FAILED`
  e `VISIONPRO_VPP_RELOAD_EXHAUSTED`;
- `VISIONPRO_VPP_RELOAD_AFTER_GIGE_DISCOVERY` e
  `VISIONPRO_VPP_RELOAD_RESULT` riportano ora il numero di tentativo.

**Verifica:** build `Release x64` completata con 0 errori. Il collaudo hardware
deve eseguire almeno tre avvii a freddo con VPP `Top/Left/Rear` e confermare che
`VISIONPRO_VPP_RELOAD_RESULT|status=ready` preceda
`VISIONPRO_JOB_RUNNING|jobIndex=2|job=Rear`.

## [2026-08-19] Versione 3.0.9.3 - Preflight uscita Left dual MultiShot

**File modificati:**
- `Services/MachineRuntimeIo.cs` - aggiunti helper comuni per riconoscere e
  risolvere una vera uscita fisica con direzione, categoria e canale coerenti;
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - il profilo
  MultiShot abilitato verifica il mapping del trigger gia' al caricamento e
  accetta solo righe `Output / CameraTrigger` con canale DO valido;
- `ViewModels/DigitalIOViewModel.cs` - il salvataggio da PCIE settings viene
  bloccato quando l'uscita di un profilo abilitato e' duplicata o non valida;
- `Localization/messages_eng.json`, `Localization/messages_ita.json` e
  `scripts/UpdateRuntimeLanguageFiles.ps1` - messaggi localizzati della nuova
  validazione;
- documentazione MultiShot, guida di diagnosi e archivio versioni aggiornati;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.9.3`.

**Motivo:** la configurazione Left dual MultiShot usata nel collaudo era stata
preparata in un file template esterno, mentre il runtime continuava a leggere il
file sotto `cfg`. In piu', lo stesso `OUT_CAMERA_LEFT_TRIGGER` compariva sia
come uscita fisica sia come riga Input/test. Il profilo attivo risultava quindi
disabilitato oppure poteva risolvere una definizione non utilizzabile e la
camera Left non riceveva impulsi.

**Impatto:**
- nessun cambio alla sequenza encoder, alle quote, allo stitching VisionPro,
  ai Cycling Preset DALSA, alle ricette o al database;
- nessuna nuova chiave XML: i file esistenti restano compatibili;
- un mapping ambiguo non viene piu' usato silenziosamente;
- nuovi eventi `MULTISHOT_<PROFILE>_OUTPUT_MAPPING_RESOLVED`,
  `MULTISHOT_<PROFILE>_OUTPUT_MAPPING_INVALID` e, per compatibilita' con un
  solo mapping fisico valido, `...OUTPUT_MAPPING_DUPLICATE_IGNORED`;
- il file attivo resta `<cartella applicazione>\cfg\machine_runtime_config.xml`:
  un template conservato in Documenti non modifica il runtime finche' non
  viene caricato o copiato tramite la procedura di commissioning.

**Verifica:** build `Release x64` completata con 0 errori. Il file operativo
locale e' stato salvato in backup e riallineato al profilo Side/Left abilitato,
8 trigger totali, doppia illuminazione e mapping fisico univoco
`OUT_CAMERA_LEFT_TRIGGER -> PCIE-1756-BE/DO02`. Resta obbligatoria la prova su
hardware descritta in
`left-dual-multishot-no-trigger-diagnosis-2026-08-19.md`.

## [2026-08-19] Versione 3.0.9.2 - Reload VPP dopo discovery GigE

**File modificati:**
- `MainWindow.xaml.cs` - il primo caricamento VPP verifica i job GigE, attende
  fino a 20 secondi che l'inventario Cognex raggiunga il numero di camere
  richiesto e ricarica automaticamente lo stesso VPP una sola volta;
- `Cls_Vpro/ICognexJobManager.cs` - esposti conteggio dei job GigE e dettaglio
  dei job con FIFO non valida per guidare il bootstrap;
- `Cls_Vpro/IgigaCameraAccess.cs` - inventario diagnostico dei FrameGrabber con
  nome e seriale;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.9.2`;
- documentazione di collaudo e archivio versioni aggiornati.

**Motivo:** dopo la `3.0.9.1` le tre viste erano correttamente presenti, ma al
primo avvio il job `Rear` della Blackfly S poteva essere deserializzato con FIFO
GigE `Invalid` e `FrameGrabber` ancora pending. Il cambio ricetta funzionava
perche' eseguiva un secondo caricamento quando la discovery di rete era ormai
completa.

**Impatto:**
- nessuna modifica a `Config.xml`, XML ricetta, database, I/O, trigger o
  MultiShot;
- nessun ritardo quando tutte le FIFO sono valide al primo caricamento;
- il recupero e' limitato al bootstrap e ricarica al massimo una volta;
- se sono disponibili meno camere di quelle richieste, la macchina resta ferma
  e il log distingue discovery incompleta da binding VPP non valido;
- nuovi log: `VISIONPRO_GIGE_DISCOVERY_WAIT`, `VISIONPRO_GIGE_INVENTORY`,
  `VISIONPRO_GIGE_DISCOVERY_READY`, `VISIONPRO_GIGE_DISCOVERY_TIMEOUT`,
  `VISIONPRO_VPP_RELOAD_AFTER_GIGE_DISCOVERY` e
  `VISIONPRO_VPP_RELOAD_RESULT`.

**Verifica:** build `Release x64` completata con 0 errori. Il collaudo hardware
deve verificare inventario `3/3`, reload automatico `status=ready` e
`VISIONPRO_CONTINUOUS_START_COMPLETE|jobs=3`.

## [2026-08-19] Versione 3.0.9.1 - Avvio robusto VPP Top/Left/Rear

**File modificati:**
- `Cls_Vpro/ICognexJobManager.cs` - avvio continuo verificato per ogni job,
  attesa limitata della FIFO, tre tentativi e diagnostica con grabber/seriale;
- `Services/MachineRuntimeService.cs` - timeout multi-camera portato a 20 secondi,
  stato non marcato sano se un job resta fermo e dettaglio propagato al recovery;
- `ViewModels/CameraContainerViewModel.cs` - i job camera validi restano visibili
  anche se il `FrameGrabber` e' temporaneamente in enumerazione;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.9.1`;
- documentazione tecnica e archivio versioni aggiornati.

**Motivo:** al primo avvio della ricetta `Dual_Mutishot` con job `Top`, `Left` e
`Rear`, la Blackfly S del job Rear poteva non avere ancora il grabber disponibile
durante il primo refresh. La vista veniva esclusa e l'eccezione di avvio del job
veniva ignorata. Un successivo cambio ricetta funzionava perche' l'enumerazione
GigE era ormai completa.

**Impatto:**
- nessuna modifica a `Config.xml`, XML ricetta, database, I/O o MultiShot;
- la presenza del job nel VPP e il suo ruolo semantico determinano la vista;
- un avvio parziale viene rifiutato: se una camera non parte, tutti i job vengono
  fermati e la macchina non esegue ispezioni incomplete;
- i log `VISIONPRO_JOB_START_ATTEMPT`, `VISIONPRO_JOB_RUNNING`,
  `VISIONPRO_JOB_START_RETRY` e `VISIONPRO_CONTINUOUS_START_INCOMPLETE`
  identificano il job e lo stato della FIFO.

**Verifica:** build `Release x64` completata con 0 errori. Il collaudo hardware
richiede tre avvii a freddo con la ricetta a tre camere e almeno dieci prodotti;
procedura completa in
`visionpro-three-camera-startup-hardening-2026-08-19.md`.

## [2026-08-06] Versione 3.0.9.0 - Tipi custom disponibili al designer XAML

**File modificati:**
- `QtisVisionPanel.csproj` - `Cognex.Vision.Startup.Net` viene copiata accanto
  all'assembly applicativo;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.9.0`;
- documentazione release e installer riallineata alla risoluzione delle
  dipendenze VisionPro.

**Motivo:** il runtime HMI risolve le DLL Cognex tramite il probing path
`VisionProDependencies`, mentre il designer di Visual Studio riflette
`QtisVisionPanel.exe` senza applicare il probing di `App.config`. Il fallimento
di `Cognex.Vision.Startup.Net` faceva quindi apparire come inesistenti tutte le
view camera, `StatisticsView` e `CameraTemplateSelector`, anche se classi e
namespace erano corretti.

**Impatto:**
- la sola DLL bootstrap Cognex e' disponibile nella directory dell'assembly;
- le dipendenze VisionPro complete restano gestite dalla junction
  `VisionProDependencies`;
- nessuna modifica al routing camere, all'acquisizione, alle ispezioni, a I/O,
  ricette, configurazione macchina o database.

**Verifica:** rebuild `Debug | Any CPU`, `Debug x64` e `Release x64` completati
con 0 errori; tutti gli output sono `3.0.9.0`; riflessione completa riuscita su
1515 tipi e presenza verificata per le sette view camera, `StatisticsView` e
`CameraTemplateSelector`.

## [2026-08-06] Versione 3.0.8.9 - Caricamento designer del contenitore camera

**File modificati:**
- `App.xaml` - aggiunti nello scope condiviso anche `CameraInfoPadding` e
  `CameraInfoFontSize`;
- `MainWindow.xaml` - rimosse le due definizioni duplicate locali;
- `DataManage/InspectionProcessor.cs` - eliminata la collisione del nome locale
  `result` che bloccava la compilazione `Debug | Any CPU` usata dal designer;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.8.9`.

**Motivo:** `CameraContainer` usa le metriche informative e carica i template
Top, Side, Left, Front, Rear, Right e Bottom quando viene aperto autonomamente
dal designer. Lo scope limitato a `MainWindow` e output `Debug` non aggiornati
generavano errori a cascata su viste e `CameraTemplateSelector`.

**Impatto:**
- tutte le metriche condivise delle viste camera sono risolvibili da `App.xaml`;
- il ridimensionamento responsive continua ad aggiornarle a runtime;
- nessuna modifica ai template, al routing dei ruoli camera, a VisionPro o al
  ciclo macchina;
- nessuna modifica a configurazione, ricette o database.

**Verifica:** parsing XAML e build `Debug | Any CPU`, `Debug x64` e `Release x64`
completati con 0 errori; generate correttamente tutte le sette view camera e
`CameraContainer`; i tre output applicativi riportano la versione `3.0.8.9`.

## [2026-08-06] Versione 3.0.8.8 - Risorse responsive camera disponibili globalmente

**File modificati:**
- `App.xaml` - aggiunti i default applicativi condivisi
  `CameraFeatureLabelFontSize`, `CameraFeatureIconSize` e
  `CameraFeatureIconMargin`;
- `MainWindow.xaml` - rimosse le definizioni duplicate limitate allo scope
  della finestra;
- `MainWindow.xaml.cs` - gli aggiornamenti responsive scrivono nello scope
  applicativo quando la risorsa e' condivisa;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.8.8`.

**Motivo:** le viste camera possono essere caricate singolarmente dal designer
XAML. Le risorse definite soltanto in `MainWindow.Resources` erano disponibili
al runtime annidato, ma non durante la risoluzione autonoma di ogni UserControl.

**Impatto:**
- eliminati gli errori di risorsa non risolta per label, dimensione e margine
  delle icone camera;
- mantenuto il ridimensionamento responsive per pannelli piccoli, medi e grandi;
- nessuna modifica a VisionPro, ciclo macchina, I/O, ricette, config o database.

**Verifica:** parsing XAML completato; `Rebuild Release x64` completata con
0 errori e rigenerazione dei BAML delle viste camera.

## [2026-08-06] Versione 3.0.8.7 - Icone dedicate alle ispezioni 3D

**File principali modificati:**
- `resources/inspection_3d_height.png` - icona asse `Z` per altezza;
- `resources/inspection_3d_width.png` - icona asse `X` per larghezza;
- `resources/inspection_3d_length.png` - icona asse `Y` per lunghezza;
- `resources/inspection_3d_profile.png` - icona curva per il profilo 3D;
- `TopCameraView.xaml` e `SideCameraView.xaml` - icone inserite nelle schede
  Top3D e Top2D mantenendo badge e riepiloghi esistenti;
- `InspectionConfigViewModel.cs` e `InspectionCounterViewModel.cs` - mapping
  uniforme delle icone nelle configurazioni e nei contatori difetto;
- manuale operatore e baseline tecnica Top3D aggiornati;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.8.7`.

**Logica:** l'icona identifica la classe di misura: `Z` altezza, `X` larghezza,
`Y` lunghezza e curva `3D` profilo. Il badge `WAIT / GOOD / NO GOOD` resta la
sorgente autorevole dell'esito e non viene sostituito dall'icona.

**Impatto:**
- `Config.xml`, machine runtime config e ricette: nessuna modifica schema;
- database: nessuna modifica;
- lettura output VisionPro, validazione, contatori e scarto fisico: invariati;
- UI: classificazione visuale coerente tra monitor 3D, configurazione ispezioni
  e contatori difetto.

**Verifica:** risorse PNG e markup XAML validati; build `Release x64` completata
con 0 errori. Resta richiesto il controllo visuale sul pannello macchina.

## [2026-08-05] Versione 3.0.8.6 - Viste Left/Right e overview multi-camera responsive

**File principali modificati:**
- `Models/CameraModel.cs` - separati ruolo runtime compatibile e ruolo visuale;
- `Views/UserControls/DisplayRecord/LeftCameraView.*` e
  `RightCameraView.*` - nuovi pannelli espliciti con indicatori saldatura e
  controllo rotolo;
- `CameraContainer.xaml` / `CameraContainerViewModel.cs` - griglia responsive
  fino a quattro colonne e altezza display calcolata sul viewport;
- `CameraTemplateSelector.cs`, `CameraDisplayManager.cs` e
  `MainWindow.xaml.cs` - routing record, indicatori, reset ricetta e dispose;
- `JobToolEditorViewModel.cs` - titoli semantici Left/Right conservati nel Job
  Editor senza cambiare la risoluzione del trigger fisico;
- file lingua e script server messaggi - aggiunta `lbRightCamera`;
- manuale operatore e documentazione tecnica aggiornati;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.8.6`.

**Logica:** `Right` resta `rear` nell'orchestrazione per compatibilita' con code,
validator e cache gia' collaudati, ma viene mostrata in una vista Right dedicata.
`Left` continua a usare il percorso Side/Left con una vista autonoma. Su un
viewport di almeno 900 px vengono mostrate fino a quattro camere sulla prima
riga; la quinta apre la seconda riga. Su display stretti le colonne diminuiscono
automaticamente per mantenere leggibilita'.

**Impatto:**
- `Config.xml`, machine runtime config e ricette: nessuna modifica schema;
- `CameraConfig.xml`: nessuna migrazione, riconoscimento visuale dei tipi
  `Left` e `Right` esistenti;
- database: nessuna modifica;
- ciclo macchina, trigger, pairing, validatori e scarto: invariati;
- UI: nuove viste e ridimensionamento dinamico dei `CogRecordDisplay`.

**Verifica:** build `Release x64` completata con 0 errori. Resta richiesto il
collaudo su macchina con VPP a quattro/cinque job per verificare record Cognex,
indicatori e resa sul pannello reale.

## [2026-08-05] Linea guida completamento macchina pacchi quattro telecamere

**Documentazione aggiornata:**
- `four-camera-package-integration-guideline-2026-08-05.md` - roadmap a gate
  per VPP `Top/Left/Right/Bottom`, mapping fisico, encoder, MultiShot standard,
  doppia illuminazione DALSA, contratti `Results`, validatori, ricette,
  diagnostica, soak test e rollback;
- `README.md` - aggiunto il nuovo documento all'indice tecnico live.

**Decisioni registrate:**
- una sola camera tra Left e Right esegue il controllo rotolo con backlight;
- un impulso fisico continua a corrispondere a una sola immagine;
- stitching e separazione Front/Backlight restano in VisionPro;
- il prossimo prerequisito e' un VPP di commissioning con i quattro job e i
  terminali canonici, prima di estendere validator e contatori.

**Impatto:** sola documentazione. Nessuna modifica a release assembly, HMI,
VPP, `Config.xml`, machine runtime config, ricette, database, mapping I/O o
ciclo macchina. La versione resta `3.0.8.5`.

## [2026-08-04] Versione 3.0.8.5 - Sincronizzazione Cycling Preset DALSA Front/Backlight

**File principali modificati:**
- `Cls_Vpro/IgigaCameraAccess.cs` - configurazione e reset dei due Cycling Preset
  tramite `OwnedGigEAccess`, con lettura opzionale del set attivo;
- `Services/MultiShotTrigger/DualIlluminationPhaseCoordinator.cs` - stato per
  profilo, reset prima sessione, riallineamento dopo anomalie e blocco durante
  sessione attiva;
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - hook di
  preparazione sessione ed evento esplicito per impulso I/O fallito;
- `Services/MultiShotTrigger/VisionProStitchingParameterApplier.cs` - scrittura
  degli input `dualIlluminationEnabled` e `frontFirst`;
- `Models/MultiShotTrigger/MultiShotTriggerModels.cs` e
  `Services/MachineConfigurationService.cs` - nuovo blocco macchina
  `DualIllumination`, default disabilitato e compatibile con XML esistenti;
- `ViewModels/DigitalIOViewModel.cs` e
  `Views/UserControls/DigitalIOControl.xaml` - editor HMI, validazione e
  persistenza per Side/Left, Right/Rear e Bottom;
- `ConfigurationTemplates/machine_runtime_config.template.xml` - valori
  iniziali `500 us`, `Line3`, `Line4`, `ExposureActive`;
- file lingua e relativo script server messaggi aggiornati;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.8.5`.

**Logica:** il caricamento VPP configura i preset ma non considera ancora la
fase allineata. Prima della prima sessione viene eseguito
`cyclingPresetResetCmd`. Cancel, timeout, errore trigger e recovery VisionPro
marcano il profilo per un nuovo reset prima del prodotto seguente. Nessun reset
viene eseguito mentre la sessione precedente risulta attiva.

**Impatto:**
- `Config.xml`: nessuna modifica;
- ricette: nessun nuovo campo;
- machine runtime config: nuovo blocco opzionale `DualIllumination` per profilo,
  aggiunto con `Enabled=false`;
- database: nessuna migrazione;
- trigger: controller encoder-driven e rapporto un impulso/una immagine
  invariati;
- runtime: una configurazione dual non valida blocca solo la relativa sessione
  MultiShot e produce log diagnostici, senza chiudere la HMI.

**Verifica:** build `Release x64` completata con 0 errori. Il collaudo reale
DALSA Line3/Line4 e la prova di recovery sono descritti in
`dalsa-dual-illumination-cycling-presets-2026-08-04.md`.

## [2026-08-03] Inventario read-only pre-integrazione quattro telecamere

**Attivita':**
- letti senza modifica `Config.xml`, configurazione runtime macchina, ricetta
  attiva, `CameraConfig.xml` e profili MultiShot;
- deserializzati in sola lettura i VPP attivo e candidati, senza `Run`,
  acquisizione o salvataggio;
- verificati job, ruoli normalizzati, quote, segnali fisici, alias e contratti
  `Results` / `ImageStitching`;
- confrontata la struttura HMI gia' disponibile con gli asset realmente
  commissionati;
- definito l'ordine compatibile delle future estensioni a config, ricetta,
  orchestrazione e diagnostica.

**Esito principale:** la baseline attiva resta `Top + Side`. `Mutishot.vpp`
contiene `Top + Left` e il ToolBlock `ImageStitching`, ma il `Results` Left non
pubblica ancora gli output saldatura/rotolo richiesti. Non e' stato trovato un
VPP verificato con `Top + Left + Right + Bottom`. I mapping Left/Side e
Right/Rear richiedono inoltre una decisione fisica prima dell'implementazione.

**Impatto:** sola documentazione. Nessuna modifica a HMI, assembly, VPP,
`Config.xml`, machine runtime config, ricette, DB o runtime macchina. La
versione resta `3.0.8.4`.

Dettaglio: `four-camera-read-only-inventory-2026-08-03.md`.

## [2026-08-03] Backup verificato pre-integrazione macchina pacchi a quattro telecamere

**Attivita':**
- creato backup locale completo di sorgente, stato Git e runtime `C:\QtisVision`;
- creato tag Git
  `pre-four-camera-packages-multishot-3.0.8.4-20260803` sul commit
  `8ba46362f6658f2ea8c0fb547204bcb558e07c55`;
- creato snapshot indipendente della baseline condivisa OneDrive;
- generati inventari SHA-256, bundle Git, patch del working tree, log di copia
  e procedura di ripristino;
- documentato il perimetro futuro Top/Left/Right/Bottom con MultiShot Left e
  Right e acquisizione Bottom singola o MultiShot.

**Verifica:** sorgente `7115/7115` file e runtime `187/187` file coincidono con
gli inventari SHA-256; baseline condivisa `6557` file senza differenze nel
confronto a secco; bundle Git verificato come cronologia completa.

**Impatto:** nessuna modifica a HMI, `Config.xml`, mapping I/O, encoder,
ricette, database, job VisionPro o runtime macchina. La versione resta
`3.0.8.4`; questa voce registra un punto di ripristino operativo, non una
release software.

Dettaglio: `pre-four-camera-package-integration-backup-2026-08-03.md`.

## [2026-07-27] Installer 3.0.8.4-r5 - UltraVNC e Notepad++

**File modificati:**
- `Installer/Build-QtisInstallerMedia.ps1` - aggiunti payload firmati
  `UltraVNC_1640_x64_Setup.exe` e `npp.8.9.3.Installer.x64.exe`, SHA-256
  bloccati e media revision `r5`;
- `Installer/QtisVision.Setup/InstallerModels.cs` - flag generico
  `ConfigureWhenInstalled`;
- `Installer/QtisVision.Setup/MainForm.cs` - i componenti che richiedono
  provisioning restano selezionati anche se gia installati;
- `Installer/QtisVision.Setup/InstallerEngine.cs` - installazione UltraVNC
  Server/Viewer, servizio automatico, password tramite `setpasswd.exe`,
  scrittura atomica INI, output sensibile oscurato e verifica `RUNNING`;
- documentazione installer e commissioning aggiornata.

**Comportamento:** Notepad++ viene installato o aggiornato in modalita
silenziosa. UltraVNC viene installato come servizio; su versioni compatibili
gia presenti il setup evita il downgrade ma riapplica la configurazione di
accesso e verifica il servizio. La password richiesta dalla baseline non viene
scritta in chiaro nel manifest, nei README o nei log.

**Verifica:** generato
`D:\QtisInstallerOutput\QtisVisionPanel_3.0.8.4-r5`, 30 payload,
`7.780.492.018` byte, builder `PASSED`, bootstrapper `isValid=true`.
Inventario di prova: `ultravnc=Installed`,
`notepad-plus-plus=InstalledOlder`. SHA-256 setup:
`C428187BB48F3287B7E800FD8AED031227033B5806608B43821FEA112F8162E2`.

**Impatto:** nessuna modifica alla HMI, a `Config.xml`, mapping I/O, encoder,
ricette, database o runtime macchina. La versione software resta `3.0.8.4`;
`r5` identifica esclusivamente la revisione del supporto installer.

## [2026-07-24] Installer 3.0.8.4-r4 - Rilevamento DAQNavi preinstallato

**File modificati:**
- `Installer/QtisVision.Setup/InstallerModels.cs` - aggiunti marker registro,
  percorso registrato e root di ricerca opzionali per il rilevamento componenti;
- `Installer/QtisVision.Setup/InstallerEngine.cs` - rilevamento aggregato con
  verifica versione del runtime e fallback filesystem;
- `Installer/QtisVision.Setup/MainForm.cs` - i prerequisiti opzionali compatibili
  sono mostrati come `Gia installato` e deselezionati;
- `Installer/QtisVision.Setup/Program.cs` - comando non interattivo
  `--inventory`;
- `Installer/Build-QtisInstallerMedia.ps1` - media revision `r4` e marker
  ufficiale Advantech per `Automation.BDaq4.dll`;
- documentazione installer e commissioning aggiornata.

**Comportamento:** sui PC consegnati con DAQNavi gia installato, il bootstrapper
verifica il marker OEM, il file registrato e la sua versione minima
`4.0.0.0`. Se il registro non e' disponibile usa una ricerca limitata alle
root DAQNavi standard. Un runtime valido viene mantenuto e XNavi non viene
rilanciato. La sola presenza del Navigator non e' considerata sufficiente e i
driver PCIE-1756/PCIE-1884 restano una verifica di commissioning.

**Verifica:** generato
`D:\QtisInstallerOutput\QtisVisionPanel_3.0.8.4-r4`, 28 payload, circa
`7,23 GB`, builder `PASSED`, bootstrapper `isValid=true`, zero finding
bloccanti. L'inventario del PC di prova riporta `daqnavi=Installed`.

**Impatto:** nessuna modifica a HMI, `Config.xml`, mapping I/O, encoder,
ricette, database o runtime macchina. La versione software resta `3.0.8.4`;
`r4` identifica esclusivamente la revisione del supporto installer.

## [2026-07-24] Versione 3.0.8.4 - Guardia avvio polling I/O e MachineController

**File modificati:**
- `Services/MachineController.cs` - stato `IsInitialized` esposto tramite configurazione letta e
  pubblicata in modo thread-safe;
- `ViewModels/DigitalIOViewModel.cs` - gli eventi iniziali di input ed encoder vengono inoltrati
  alla logica macchina solo dopo il caricamento della configurazione runtime; vengono inoltre
  ignorati eventuali callback appartenenti a un precedente manager dopo un cambio HW/SIM;
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.8.4`.

**Causa:** `AdvantechDeviceManager` avvia il polling non appena termina l'inizializzazione della
scheda. `MachineController` riceve invece la configurazione macchina nella fase immediatamente
successiva. Un campione encoder o input arrivato in quella breve finestra chiamava
`UpdateEncoderPosition` o `RegisterProductDetection` prima dell'inizializzazione e produceva
`Errore nel polling loop: MachineController not initialized.`.

**Comportamento corretto:** i campioni elettrici ricevuti prima che il runtime sia pronto non
entrano nel tracking prodotto. Al termine dell'inizializzazione,
`ArmConfiguredEncoderChannelsAsync` legge il contatore reale e applica comunque uno snapshot
autorevole al controller, quindi conteggio, quota macchina, trigger e MultiShot partono dalla
posizione corrente senza richiedere un reset manuale.

**Impatto:** nessuna modifica a `Config.xml`, mapping I/O, encoder, quote, ricette o database.
Il flusso operativo dopo l'avvio resta invariato.

**Installer offline:** rigenerato e validato
`D:\QtisInstallerOutput\QtisVisionPanel_3.0.8.4-r3` con Release x64 corrente,
seed completo `C:\QtisVision`, VisionPro 9.25, patch KB5077181, DAQNavi, MySQL,
HeidiSQL e ambiente Python AI offline. Il bootstrapper ha verificato 28 payload
e ha prodotto `installer-validation.json` con `isValid=true`.

## [2026-07-24] Versione 3.0.8.3 - Monitoraggio profilo Top3D Essity

**File principali modificati:** configurazione macchina e ricetta, `IToolBlockValidator.cs`,
persistenza produzione/MySQL, flusso esito in `MainWindow.xaml.cs`, Overview Top3D, Recipe
Management, Data Inspector, server messaggi, manuale e documentazione tecnica.

**Comportamento:** il runtime acquisisce `HeightMedian`, `HeightHighTail`,
`HeightValidPixelRatio`, `HeightBulge` e `ThreeDHeight_MAX`. L'altezza dimensionale usa
`HeightMedian` con fallback compatibile a `ThreeDHeight`; il solo valore dimensionale riceve
l'offset globale macchina. Il profilo usa soglie per ricetta, espone gli stati
`GOOD`, `NO GOOD`, `INVALID`, `MONITOR` e `WAIT`, aggiorna classificazione, contatori e storico,
ma in questa baseline non abilita mai lo scarto fisico.

**Configurazione e compatibilita':**
- `Config.xml -> Configuration`: cinque nomi output VisionPro opzionali, con default coerenti al
  contratto del ToolBlock Top3D;
- XML ricetta: `ThreeDProfileEnabled=false`,
  `ThreeDHeightMinimumValidPixelRatio=0.75` e `ThreeDHeightMaximumBulge=0`;
- ricette e VPP precedenti restano validi: profilo disabilitato e fallback altezza legacy;
- nessuna modifica a mapping I/O, encoder, trigger, quote macchina o `ejectionStatus`.

**Database:** migrazione incrementale e non distruttiva di `tblgenerale` con sei colonne nullable:
`ThreeDHeightMedianValue`, `ThreeDHeightHighTailValue`, `ThreeDHeightMaximumValue`,
`ThreeDHeightBulgeValue`, `ThreeDHeightValidPixelRatio`, `NCThreeDProfile`. Il codice di
persistenza e Data Inspector tollera database non ancora migrati.

**UI e diagnostica:** Recipe Management permette di abilitare il controllo e impostare pixel
validi minimi e indice pancia massimo; Overview e Data Inspector mostrano valori, soglie e stato.
Eventi diagnostici principali: `TOP3D_PROFILE_MONITOR` e
`TOP3D_HEIGHT_LEGACY_FALLBACK`.

## [2026-07-23] Versione 3.0.8.2 - Offset globali misure Top3D

**File principali modificati:** `ConfigClassStructure.cs`, `IToolBlockValidator.cs`,
`TopThreeDMeasurementCorrection.cs`, `CameraDisplayManager.cs`, `PreferenceView*`, sistema lingua
condiviso, manuale e guida Top3D.

**Comportamento:** `Config.xml -> Configuration` espone tre correzioni globali in millimetri per
altezza, larghezza e lunghezza. Il validator applica
`misura corretta = misura grezza VisionPro + offset` prima del confronto con nominale/tolleranza.
La misura corretta resta l'unico valore usato da esito, contatori, Overview, record produzione e
Data Analysis. Se l'offset e diverso da zero, la card operatore mostra valore corretto e offset.

**Sicurezza:** l'editor riservato a Installer/Administrator accetta valori finiti tra `-1000` e
`+1000 mm` e supporta punto o virgola come separatore decimale; il runtime normalizza comunque
Config.xml modificati manualmente. Salvataggi tracciati con
`TOP3D_MEASUREMENT_OFFSETS_SAVED`.

**Compatibilita':** i tre campi sono opzionali e valgono zero sui Config.xml precedenti. Nessuna
modifica alle ricette e nessuna migrazione MySQL; nelle colonne misura esistenti viene archiviato il
valore corretto effettivamente validato.

**Verifica:** build x64 Release completata con zero errori; test positivo, negativo, zero legacy,
NaN e clamp completati.

## [2026-07-22] Versione 3.0.8.1 - Filtro ispezioni ricetta attiva in Data Analysis

**File principali modificati:** `ViewModels/DataAnalysisViewModel.cs`, sistema lingua condiviso e
archivio release.

**Comportamento:** la pagina seleziona per default `Solo ispezioni della ricetta attiva`. In questa
modalita' query, stato ispezioni ricetta e mappa feature runtime/VPP vengono applicati insieme; una
macchina Top3D non mostra difetti Bottom non disponibili. `Tutte le ricette` e' ora una vera
aggregazione globale distinta, mentre le singole ricette archiviate restano selezionabili.

**Compatibilita':** nessun impatto su XML ricetta, `Config.xml`, schema DB o ciclo macchina.

**Verifica:** build x64 Debug completata con 0 errori; file lingua repository e runtime riallineati.

## [2026-07-22] Versione 3.0.8.0 - Export PDF Data Analysis compatto

**File principale modificato:** `Services/VisualPdfExportService.cs`.

**Comportamento:** l'export A4 orizzontale dispone fino a due card per pagina, conserva le
proporzioni della cattura e indica nell'intestazione l'intervallo dei grafici presenti. Una
dashboard da sette card passa normalmente da sette a quattro pagine, riducendo lo spazio vuoto.

**Compatibilita':** nessun impatto su ricette, XML, `Config.xml`, database o runtime macchina.

**Verifica:** analizzate visivamente tutte le sette pagine del PDF campione; build x64 Debug
completata con 0 errori. Il collaudo finale richiede una nuova esportazione dalla HMI con dati reali.

## [2026-07-22] Versione 3.0.7.9 - Propagazione nominali Top3D e card trend statistiche

**File principali modificati:**
- `ViewModels/RecipeManagerViewModel.cs` e `Views/RecipeManagerView.xaml`
- `ViewModels/DataAnalysisViewModel.cs` e `Views/UserControls/DataAnalysisView.xaml`
- documentazione release e manuale Top3D

**Comportamento:** sulle macchine `3DCheck`, il salvataggio propaga altezza, larghezza e lunghezza
prodotto nei rispettivi nominali `recipeParamTop3D`. La UI mostra questi nominali in sola lettura e
mantiene indipendenti le tolleranze. Le card dei trend hanno area grafico responsiva, riepilogo
compatto e valori dimensionali espressi in millimetri.

**Compatibilita':** nessuna modifica a `Config.xml`, schema MySQL o struttura XML. I campi Top3D
esistenti vengono valorizzati dal normale salvataggio della ricetta; nessuna migrazione richiesta.

**Verifica:** build x64 Debug completata con 0 errori; restano 76 warning preesistenti.

## [2026-07-21] Versione 3.0.7.8 - Audit completo, pulizia conservativa e manuale operatore

**File principali modificati:**
- sorgenti C#/XAML e `QtisVisionPanel.csproj` - rimossi file, converter, modelli, handler, metodi
  privati e blocchi commentati dimostrabilmente senza consumer
- `MainWindow.xaml.cs` - eliminata la chiamata che registrava una falsa ispezione GOOD durante il
  caricamento ricetta; il load ora aggiorna soltanto la proiezione UI dei contatori
- `ViewModels/ManualViewModel.cs` - caricamento limitato ai capitoli operatore numerati `NN_*`
- `Docs/Manual/*.md` - manuale consolidato con immagini, tabelle ID, procedure e criteri di
  escalation; rimossi i duplicati `.txt`
- `Documentation/MachineHardware/full-application-audit-2026-07-21.md` - inventario, pulizia,
  warning, rischi residui e checklist collaudo
- `Documentation/improvement-plan-2026-07.md` - piano industriale riscritto con priorita',
  acceptance criteria e rollback
- documenti architetturali e indici aggiornati; piani/presentazioni non operativi spostati sotto
  `Documentation/MachineHardware/archive`
- `Properties/AssemblyInfo.cs` - release `3.0.7.8`

**Motivo:** la crescita progressiva dell'applicazione aveva lasciato classi non utilizzate,
implementazioni duplicate/commentate e documenti tecnici visibili insieme al manuale operatore.
L'audit crea una baseline piu' leggibile e misurabile senza un refactor invasivo del ciclo macchina.

**Comportamento:**
- caricare o ricaricare una ricetta non incrementa piu' `Total`/`Good` senza un prodotto
- un prodotto viene contabilizzato solo dalla finalizzazione reale del gruppo ispezione
- la pagina Manuale elenca otto capitoli operatore canonici e ignora README/piani tecnici
- trigger, pairing camere, VisionPro, scarto, OPC UA, AI advisory e autorizzazioni restano invariati

**Impatto:**
- Config/XML ricetta: nessuna modifica
- database: nessuna migrazione
- I/O/encoder/VisionPro: nessuna modifica di configurazione o sequenza
- server messaggi: nessuna nuova label UI

**Verifica:** tag di rollback creato; corrispondenza completa tra 212 sorgenti C# e progetto;
collegamenti immagini/manuale validi; build Release x64 con 0 errori; warning residui censiti nel
report di audit.

## [2026-07-14] Versione 3.0.7.7 - Recipe Management industriale e responsive

**File principali modificati:**
- `Views/RecipeManagerView.xaml` - dettagli ricetta e parametri riuniti in un workspace unico;
  panoramica/stato database compattati; stile sezioni, immagine e selettori ispezione riallineato
  a una UI tecnica industriale
- `Views/RecipeManagerView.xaml.cs` - breakpoint responsive che mantiene due colonne su schermi
  larghi e sposta il pannello parametri sotto ai dati principali su schermi compatti
- `Properties/AssemblyInfo.cs` - release `3.0.7.7`
- manuale operatore e documentazione architetturale aggiornati

**Motivo:** la precedente composizione usava due contenitori e due scrollbar indipendenti nella
stessa area operativa. Su pannelli compatti i campi risultavano compressi, mentre su schermi larghi
la pagina appariva frammentata e con troppo spazio verticale non informativo.

**Comportamento:**
- una sola superficie e una sola scrollbar per dati ricetta, tolleranze, trigger e diagnostica DB
- sopra `1040 px` utili: dati principali e parametri affiancati in proporzione `1.8:1`
- sotto `1040 px` utili: pannello parametri impilato sotto, a piena larghezza
- nessun controllo viene rimosso o duplicato; gli stessi binding e handler continuano a essere usati

**Impatto:**
- Config/XML ricetta/database: nessuna modifica
- runtime macchina, VisionPro, I/O e ciclo ispezione: nessuna modifica
- server messaggi: nessuna nuova label; riutilizzate tutte le chiavi localizzate esistenti

**Verifica:** parsing XAML, build Release x64 senza errori e misura layout WPF alle larghezze
`1600`, `1366`, `1280` e `1024 px`.

## [2026-07-14] Versione 3.0.7.6 - Anteprima quote globali ed effettive nell'editor ricetta

**File principali modificati:**
- `ViewModels/RecipeMachineRuntimeOverview.cs` - nuovo modello read-only che presenta valori
  macchina e valori ricetta effettivi senza modificare configurazione o runtime
- `Services/RecipeMachineRuntimeResolver.cs` - helper pubblico per risolvere il punto camera globale
  con gli stessi alias fisici usati dalla fusione runtime
- `ViewModels/RecipeManagerViewModel.cs`, `Views/RecipeManagerView.xaml(.cs)` - riepilogo sotto ogni
  offset camera e campo MultiShot, aggiornato al cambio ricetta e al termine della modifica
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`,
  `scripts/UpdateRuntimeLanguageFiles.ps1` - testi localizzati inglese/italiano
- manuali e documentazione tecnica aggiornati

**Motivo:** un valore offset isolato non permette al collaudatore di capire immediatamente la quota
finale. La pagina ora mostra il confronto con la taratura globale, riducendo tentativi e rischio di
inserire correzioni nella direzione o nell'ordine di grandezza sbagliati.

**Comportamento:**
- quote camera: `Globale = base + trim`; `Effettiva = Globale + offset ricetta`
- MultiShot: vengono mostrati stato, scatti, limite, step e origine primo scatto globali/effettivi
- il calcolo usa il resolver produttivo e la calibrazione encoder salvata, non formule UI parallele
- se un valore ricetta non e valido viene mostrato un avviso e l'anteprima espone il fallback sicuro

**Impatto:**
- `Config.xml` / `machine_runtime_config.xml`: sola lettura, nessun campo e nessuna riscrittura
- XML ricetta: invariato rispetto a `3.0.7.5`
- database: nessuna modifica
- runtime macchina: nessuna modifica; la nuova funzione e esclusivamente informativa

**Verifica:** build Release x64 senza errori, parsing XAML, parita messaggi ITA/ENG e test numerico
di quota Top, alias Left/Side, scatti, step e origine MultiShot.

## [2026-07-14] Versione 3.0.7.5 - Offset camera e MultiShot specifici della ricetta

**File principali modificati:**
- `Cls_Config/Calss_structure/RecipeParameters.cs` - nuovo nodo XML opzionale
  `machineRuntimeAdjustments` con offset camera e correzioni MultiShot per prodotto
- `Services/RecipeMachineRuntimeResolver.cs` - fusione in memoria tra configurazione macchina e
  delta ricetta, con clone difensivo, validazione e fallback sicuro
- `ViewModels/DigitalIOViewModel.cs` - configurazione runtime effettiva applicata a tracking e
  controller MultiShot senza modificare `machine_runtime_config.xml`
- `MainWindow.xaml.cs` - applicazione durante ogni cambio ricetta, incluso autoswitch/OPC UA, e
  uso degli stessi valori effettivi per i parametri `ImageStitching` VisionPro
- `ViewModels/RecipeManagerViewModel.cs`, `Views/RecipeManagerView.xaml(.cs)` - editor ricetta con
  offset camera per ruolo e profili Side/Left, Right/Rear e Bottom
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`,
  `scripts/UpdateRuntimeLanguageFiles.ps1` - testi HMI inglese/italiano e aggiornamento server
  messaggi
- documentazione tecnica, commissioning e manuale operatore aggiornata

**Motivo:** le quote nominali delle camere sono caratteristiche fisiche globali, ma prodotti con
dimensioni diverse possono richiedere una correzione positiva o negativa. Analogamente, alcuni
prodotti richiedono single-shot e altri MultiShot con geometria o numero frame differenti.

**Comportamento:**
- quota effettiva = base macchina + trim commissioning + offset ricetta
- scatti effettivi = scatti macchina + delta ricetta, limitati da `MaxShotCount`
- step effettivo = step macchina + delta ricetta; il primo scatto puo' traslare l'intera sequenza
- canale, impulso, intervallo minimo, encoder, calibrazione, timeout e limiti restano globali
- ricette storiche senza il nuovo nodo mantengono esattamente i valori macchina

**Impatto:**
- `Config.xml` / `machine_runtime_config.xml`: nessun nuovo campo e nessuna riscrittura da ricetta
- XML ricetta: nuovo nodo opzionale retrocompatibile; valori assenti = zero / `Machine`
- database: nessuna migrazione
- runtime: i prodotti gia' in tracking vengono puliti al cambio geometria; trigger, MultiShot e
  VisionPro ricevono un'unica configurazione effettiva coerente
- modalita `TimedFromPhotocell`: invariata, continua a usare i trigger delay temporali ricetta

**Verifica:** build Release x64, smoke test resolver, compatibilita XML ricette storiche, clamp
valori non validi e verifica che la configurazione macchina sorgente resti immutata.

## [2026-07-14] Versione 3.0.7.4 - AI: hardening da review avversariale completa dello stack

Esito della review avversariale completa dello stack AI (5 revisori specializzati + 2 verificatori
indipendenti per finding): 32 segnalazioni grezze → 23 respinte dalla doppia verifica → 6 confermate
+ 3 in disaccordo, rivalutate a mano sul codice. Questa release corregge le 5 reali; le restanti
sono documentate sotto come rischi accettati.

**File modificati:**
- `Services/DefectClassifierTrainingService.cs` — (1) TOCTOU sul nome versionato: due training
  lanciati quasi insieme risolvevano entrambi `defect_classifier_v1.onnx` e l'ultimo sovrascriveva
  il primo. Ora un gate statico (`TrainingGate`, SemaphoreSlim Wait(0)) serializza i training nel
  processo: il secondo riceve un esito esplicito "training già in corso". (2) Il buffer coda stderr
  ora trimma DOPO l'append: resta sempre ≤ 4000 caratteri come da contratto
- `ViewModels/SystemDiagnosticsViewModel.cs` — (1) ricontrollo `IsTrainingRunning` DOPO il dialog di
  conferma: `ShowDialog` pompa messaggi (re-entrancy WPF) e un secondo click su "Addestra" poteva
  attraversare il metodo mentre il primo era nel dialog. (2) "Salva"/"Ricarica" controlli AI bloccati
  durante il training (CanExecute + guardia interna): `LoadAiSettings` sostituiva `_aiConfig` sotto
  i piedi del training e il writeback del modello finiva su un'istanza di config diversa da quella
  usata per addestrare. (3) `ValidateAiSettingsBeforeSave` ora valida esplicitamente
  `DefectClassifierMinConfidence` e `SideDefectClassifierMinConfidence` in [0,1] con messaggio chiaro
  (prima: clamp silenzioso a valle senza avviso all'operatore)
- `Services/MachineConfigurationService.cs` — ripristinata la simmetria dei marker schema-upgrade:
  aggiunti i 6 marker TOP mancanti (`ModelPath`, `InputWidth`, `InputHeight`, `Grayscale`,
  `NormalizeMean`, `NormalizeStd`) che la Side aveva e il TOP no (config vecchie non venivano
  ri-salvate con i tag TOP; runtime comunque corretto per via dei default C#)
- `Properties/AssemblyInfo.cs` — release `3.0.7.3` → `3.0.7.4`

**Segnalazioni respinte / rischi accettati (documentati, nessuna modifica):**
- `File.ReadAllText` senza encoding: usa UTF-8 con rilevamento BOM in .NET Framework — falso positivo
- race Exited-vs-flush degli stream: `process.WaitForExit()` senza parametri (riga ~513) drena i
  reader asincroni prima della lettura di `ResultJson` — già corretto by-design
- doppia scansione dataset (pre-scan C# + scan Python): intenzionale (gate di validazione prima di
  lanciare Python); costo I/O accettabile
- matrice di confusione emessa dal trainer ma non estratta dal C#: il sidecar `.metrics.json` accanto
  al modello la conserva già su disco — telemetria disponibile, non persa
- mix Top 2D / Top3D nel dataset `labels.top` (stesso suffisso `_T_`): su una singola macchina il
  tipo e' omogeneo; rischio solo se la stessa macchina alterna job top e top3d nel tempo — da tenere
  presente se in futuro si mescolano le modalita' (eventuale campo `topRole` nel sidecar)

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessun campo nuovo (solo marker: le config vecchie vengono
  ri-salvate una volta all'avvio con i tag TOP espliciti — scrittura atomica gia' esistente)
- Database / API pubblica: nessuna modifica
- Comportamento runtime: identico nei percorsi normali; chiusi i percorsi di corsa (doppio training,
  save durante training) e la validazione UI e' piu' chiara

## [2026-07-13] Versione 3.0.7.3 - Training ONNX senza warning autograd/deprecation

**File modificati:**
- `scripts/AI/train_defect_classifier.py` - la loss di ogni batch viene estratta una sola volta con
  `loss.detach().item()` prima di alimentare progressi e media epoca; filtrato esclusivamente il
  messaggio PyTorch sul futuro cambio di exporter ONNX, mantenendo l'exporter TorchScript gia'
  collaudato
- `Properties/AssemblyInfo.cs` - release `3.0.7.3`
- documentazione AI, collaudo e archivio release aggiornati

**Motivo:** `float(loss)` su un tensor con `requires_grad=True` genera un `UserWarning` PyTorch che
la HMI pubblica come `AI_TRAINING_STDERR`, pur non essendo un errore. PyTorch 2.8 emette inoltre una
deprecazione informativa a fine export sul cambio di default previsto per una versione futura.

**Impatto:**
- algoritmo, backward, optimizer, metriche, formato ONNX e contratto classi: invariati
- `Config.xml`, configurazione macchina, ricette e database: nessuna modifica
- log: `AI_TRAINING_STDERR` torna riservato ad avvisi/errori effettivamente utili al manutentore

**Verifica:** `py_compile` e `--self-test` completati; smoke training reale con dataset sintetico
OK/NOK, un'epoca, validation ed export ONNX completato con exit code 0 e stderr vuoto.

## [2026-07-13] Versione 3.0.7.2 - AI ONNX isolata dal ciclo macchina e memoria limitata

**File principali modificati:**
- `Services/OnnxDefectClassifier.cs` - fast path volatile quando il profilo camera e' disabilitato
  o il modello non e' pronto; una sola inferenza ONNX globale tra TOP e SIDE; sessioni ONNX con
  esecuzione sequenziale, un thread intra-op e un thread inter-op; worker a priorita' bassa;
  arena CPU e memory pattern disabilitati; preprocessing `LockBits` al posto di `GetPixel`;
  statistiche con tempo inferenza e attesa risorsa; accordi shadow spostati da INFO a DEBUG
- `Properties/AssemblyInfo.cs` - release `3.0.7.2`
- `Documentation/AI/ONNX_Defect_Classifier_Guide.md`,
  `Documentation/MachineHardware/architecture-and-modules.md`,
  `Documentation/MachineHardware/new-implementations-test-guide-2026-07-10.md` - contratto
  zero-impact, limiti risorsa e collaudo comparativo AI OFF/ON

**Motivo:** nei log di simulazione TOP e SIDE potevano eseguire contemporaneamente due inferenze
native e registrare un evento INFO per ogni confronto. Il servizio era gia' asincrono, ma la
concorrenza CPU nativa e il preprocessing pixel-per-pixel potevano sottrarre tempo alla UI e ai
worker VisionPro su PC macchina con risorse limitate.

**Impatto:**
- `Config.xml`, `machine_runtime_config.xml`, ricette e database: nessuna modifica di schema o dati
- AI disabilitata: nessun model load, task, accesso cartella pezzo, bitmap o tensor nel ciclo
- modello mancante/non valido: warning al caricamento configurazione, poi percorso runtime no-op
- AI attiva: massimo una inferenza globale; al massimo un campione per camera puo' essere in volo,
  senza coda crescente e senza bitmap/tensor allocati durante l'attesa della risorsa
- runtime macchina: esito VisionPro, trigger, scarto, contatori e pairing camere restano invariati;
  l'AI continua a essere solo advisory
- logging: gli accordi ordinari sono disponibili a livello DEBUG; disaccordi, errori e timeout
  restano WARN e auditati

**Nota diagnostica:** il log analizzato mostra anche una coda SIDE con lag di piu' secondi rispetto
alla TOP. Questo e' un problema distinto di acquisizione/pairing e non viene mascherato dalla patch
prestazionale ONNX.

**Verifica:** build Release x64 completata; guida di test aggiornata con prova AI OFF, modello
mancante, TOP+SIDE attivi, stabilita' memoria e confronto di cadenza per almeno 15 minuti.

## [2026-07-13] Versione 3.0.7.1 - AI shadow: immagini sincronizzate, statistiche isolate e validation cronologica

**File principali modificati:**
- `Services/OnnxDefectClassifier.cs` - attesa asincrona e limitata del file immagine accodato;
  controllo di file chiuso prima del decode; generazione runtime per scartare inferenze stale;
  reset automatico delle statistiche quando cambia modello, versione, preprocessing o soglia;
  identificatore stabile `set` con versione/soglia scritto anche nei log shadow; contesto attivo e
  contatori `busy` / `image timeout` esposti in PC Diagnostics
- `Services/DefectClassifierTrainingService.cs` - pre-scan C# allineato alla regola label Python;
  lettura dello stato validation e notifica esplicita quando le metriche sono provvisorie
- `scripts/AI/train_defect_classifier.py` - holdout cronologico stratificato al posto dello split
  casuale; conteggio separato OK/NOK di validation; stato `qualified` solo con almeno 10 campioni
  per classe nel solo holdout
- `ViewModels/SystemDiagnosticsViewModel.cs`, `Views/UserControls/SystemDiagnosticsView.xaml` -
  contesto modello/soglia mostrato sopra ogni statistica e popup warning per validation provvisoria
- `ConfigurationTemplates/machine_runtime_config.template.xml`,
  `Services/MachineConfigurationService.cs` - campi soglia TOP e SIDE presenti nel template e nei
  marker di upgrade schema
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`,
  `scripts/UpdateRuntimeLanguageFiles.ps1` - localizzazione delle label soglia ONNX
- `scripts/SyncSharedBaseline.ps1`, `.gitignore` - `_buildcheck` esclusa dalla baseline sorgente
  condivisa e dal versionamento
- `Properties/AssemblyInfo.cs` - release `3.0.7.1`

**Motivo:** la scrittura immagini e' asincrona, quindi lo shadow classifier poteva cercare il file
prima del completamento del writer e perdere silenziosamente il campione. Inoltre le statistiche
restavano aggregate dopo un cambio modello/soglia e lo split casuale poteva produrre metriche troppo
ottimistiche su immagini consecutive e molto simili.

**Impatto:**
- `Config.xml`, ricette e database: nessuna modifica
- `machine_runtime_config.xml`: compatibile; i due campi `*MinConfidence` mancanti vengono aggiunti
  con default `0`
- runtime macchina: nessuna attesa nel percorso ispezione/scarto; tutte le attese e inferenze
  restano off-thread, single-flight e advisory
- operatore/manutentore: dopo un cambio contesto le statistiche ripartono automaticamente; un
  modello con holdout insufficiente viene creato ma indicato chiaramente come `provisional`

**Verifica:** build Release x64 completata; smoke test trainer ONNX con holdout cronologico e smoke
test regole label TOP legacy/SIDE per-camera completati con esito positivo.

## [2026-07-13] Versione 3.0.7.0 - AI: soglia di confidenza NOK per-camera (taglio falsi allarmi)

Risponde al problema visto nelle statistiche shadow-mode: la TOP aveva falsi allarmi (7,6%) con
confidenza media bassa. Ora si puo' impostare una CONFIDENZA MINIMA per dichiarare NOK, per camera:
un NOK sotto soglia diventa "incerto" e NON conta come difetto (taglia i falsi allarmi a bassa
confidenza), ma viene contato a parte per non nascondere potenziali difetti (sicurezza).

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs` — `DefectClassifierMinConfidence` e
  `SideDefectClassifierMinConfidence` (double 0..1, default 0 = filtro disattivato)
- `Services/MachineConfigurationService.cs` — clamp [0,1] per entrambe
- `DataManage/IDefectClassifier.cs` — `DefectClassificationResult.IsUncertain` (NOK grezzo declassato dalla soglia)
- `Services/OnnxDefectClassifier.cs` — legge la soglia per camera (`ResolveCameraConfig`/`TryLoadModel`);
  in `Classify` applica il gate: `PredictedDefective = argmax!=0 && confidenza>=soglia`, altrimenti
  `IsUncertain=true` e non-difetto. Nuovo contatore `Uncertain` nelle statistiche shadow
  (`ShadowSample`/`GetShadowStats`/`ShadowModeStats`), incluso in `SummaryText`. Tag `uncertain=`
  nel log `VISION_ML_SHADOW`
- `Views/UserControls/SystemDiagnosticsView.xaml` — campo "Confidenza min NOK" per TOP e per SIDE;
  gli incerti compaiono nel riepilogo statistiche (Top e Side)
- `Properties/AssemblyInfo.cs` — release `3.0.6.9` → `3.0.7.0`

**Motivo:** dare una leva diretta e sicura per ridurre i falsi scarti potenziali del modello senza
riaddestrare, mantenendo visibile quanto la soglia sta sopprimendo (contatore incerti).

**Impatto:**
- machine_runtime_config.xml: 2 nuovi campi `*DefectClassifierMinConfidence` (default 0 = comportamento invariato)
- Database / API pubblica: `DefectClassificationResult.IsUncertain` aggiunto; nessuna firma rimossa
- Comportamento runtime: con soglia 0 (default) nulla cambia. Sopra 0, i NOK a bassa confidenza non
  sono piu' dichiarati difetto nello shadow: meno falsi allarmi, e gli "incerti" sono tracciati.
  Resta advisory (nessun impatto su scarto/contatori). NB: la soglia puo' ridurre anche il recall se
  un difetto reale ha confidenza bassa -> valutare col contatore incerti + recall

## [2026-07-13] Versione 3.0.6.9 - AI Side: hardening da review avversariale (contratto etichette + race shutdown)

Correzioni ai difetti CONFERMATI dalla review avversariale della 3.0.6.8 (2 reali su 4 findings;
uno era falso positivo, uno risolto insieme al principale). Nessun cambiamento funzionale: solo
coerenza training/inference e robustezza allo shutdown.

**File modificati:**
- `scripts/AI/train_defect_classifier.py` — il fallback all'etichetta GLOBALE (per il TOP) ora scatta
  SOLO per i sidecar LEGACY (senza blocco `labels`). Se il blocco `labels` esiste ma non contiene la
  camera, quella camera NON e' stata ispezionata sul pezzo → il campione viene SALTATO invece di
  ereditare la label globale (che poteva contenere il difetto di un'ALTRA camera → contaminazione del
  dataset per-camera). Vale per top e side
- `Services/OnnxDefectClassifier.cs` — (1) `ResolveRuleValidity` reso simmetrico: sia top sia side
  usano il risultato-regole DELLA loro camera e saltano se assente; rimosso il fallback del top
  all'esito globale (rompeva il contratto con l'etichetta di training per-camera). (2) `_shadowGate.Release()`
  nel finally del task shadow ora e' protetto da try/catch(ObjectDisposedException): se l'app si chiude
  mentre un confronto e' in volo, il semaforo puo' essere gia' disposto → si ignora (shutdown, non errore)
- `Properties/AssemblyInfo.cs` — release `3.0.6.8` → `3.0.6.9`

**Falso positivo respinto:** la review segnalava `File.ReadAllText(file)` in
`DefectClassifierTrainingService.CountLabeledSamples` come bug di encoding. NON e' un bug:
`File.ReadAllText` in .NET Framework usa UTF-8 con rilevamento BOM (non la codepage di sistema) e
legge correttamente i sidecar UTF-8-no-BOM. Confermato empiricamente (il pre-scan C# conta i campioni
corretti). Nessuna modifica.

**Verifica:** compilazione Release x64 exit 0 (OutDir separato; exe in bin bloccato dall'app attiva).
Scan reale post-fix su `D:\QtisVision\Pieces`: TOP `ok=596 nok=187 senza_etichetta_camera=0`;
SIDE `ok=312 nok=36 senza_etichetta_camera=435` (i 435 legacy correttamente saltati per la Side, resi
visibili dal contatore). La Side ha ora abbastanza NOK per il training.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- API pubblica: nessuna firma cambiata
- Comportamento runtime: shadow-mode piu' corretto (confronto sempre per-camera; niente contaminazione
  dell'etichetta); shutdown piu' pulito. Nessun impatto su ispezione/scarto (resta advisory)

## [2026-07-13] Versione 3.0.6.8 - AI: classificatore ONNX esteso alla camera SIDE

Finora il classificatore difetti ONNX (shadow-mode) esisteva solo per la camera TOP. Ora c'e' una
seconda istanza indipendente per la camera SIDE, con modello, preprocessing, training e statistiche
propri — mirror completo del TOP.

**Fatto chiave (convenzione nomi verificata):** immagine Side = suffisso `_F_` (non `_S_`), Top = `_T_`.
Tutte le immagini di un pezzo stanno nella stessa cartella `Piece_XXXXXXXX`.

**File modificati:**
- `Services/OnnxDefectClassifier.cs` — reso PARAMETRICO per camera: ctor `OnnxDefectClassifier(role)`;
  `ResolveCameraConfig` seleziona i campi Top o Side; `FindRawImage` usa il suffisso `_T_`/`_F_`;
  `ResolveRuleValidity` confronta contro il risultato-regole DELLA camera (top→`TopResult`,
  side→`SideResult`) invece dell'esito globale — piu' corretto (una Side buona non risulta piu' NOK
  per un difetto Top); tag `camera=` nei log e nei metadata. Ogni istanza ha gate, sessione e
  statistiche shadow proprie
- `Models/MachineRuntimeConfiguration.cs` — 9 campi `SideDefectClassifier*` paralleli al Top
- `Services/MachineConfigurationService.cs` — marker schema-upgrade + clamp per i campi Side
- `ServiceLocator.cs` — singleton `SideDefectClassifier` (`new OnnxDefectClassifier("side")`) + init + dispose
- `MainWindow.xaml.cs` (~3745) — seconda chiamata `RunShadowComparison` per la Side (stesso pezzo/cartella)
- `Services/TrainingDataCollectionService.cs` + `Models/TrainingSampleEntry.cs` — il sidecar
  `label.json` ora include `labels` PER-CAMERA (top/side/front/rear/bottom) da `*Result.IsValid`,
  cosi' il modello Side impara sull'etichetta della SUA camera (l'etichetta globale sarebbe rumorosa)
- `scripts/AI/train_defect_classifier.py` — argomento `--camera top|side`; `find_camera_image(suffix)`;
  lettura etichetta per-camera. TOP mantiene il fallback alla label globale per i dataset storici;
  SIDE richiede `labels.side` (niente fallback: eviterebbe la contaminazione da difetti Top) con
  contatore diagnostico `senza_etichetta_camera`
- `Services/DefectClassifierTrainingService.cs` — `TrainAsync(bindings, cameraRole, ...)`;
  `TrainingSettings.From(bindings, role)` legge il preprocessing della camera; output versionato
  per camera (`defect_classifier_vN.onnx` / `defect_classifier_side_vN.onnx`); `--camera` allo script
- `ViewModels/SystemDiagnosticsViewModel.cs` — `TrainSideDefectClassifierCommand`;
  `TrainDefectClassifierCoreAsync(cameraRole)` + `ApplyTrainedModelToConfig(cameraRole,...)` scrive i
  campi della camera giusta; `SideShadowStats`; refresh/reset e `RefreshConfiguration` per entrambe
- `Views/UserControls/SystemDiagnosticsView.xaml` — sezione "Classificatore Side (_F_)" (abilita,
  percorso, versione/note, dimensioni, grayscale, mean/std, pulsante "Addestra Side") + blocco
  statistiche shadow Side (Top e Side etichettati)
- Localizzazione e template config aggiornati per i nuovi campi/label; `Properties/AssemblyInfo.cs` 3.0.6.7 → 3.0.6.8

**Verifica:** compilazione Release x64 exit 0 (su OutDir separato: l'exe in bin era bloccato dall'app in
esecuzione). Scan reale su `D:\QtisVision\Pieces`: TOP `ok=125 nok=40`, SIDE `ok=322 nok=112`
(immagini `_F_` trovate; un pezzo senza `_F_` gestito). Review avversariale eseguita: 0 bug confermati
(agenti di verifica interrotti dal limite di sessione: verifica non esaustiva).

**Impatto:**
- machine_runtime_config.xml: 9 nuovi campi `SideDefectClassifier*` (default sicuri; assenti su vecchi XML = default)
- Database: nessuna modifica
- label.json: nuovo oggetto `labels` per-camera (i sidecar vecchi restano validi: TOP via fallback)
- API pubblica: `OnnxDefectClassifier` nuovo ctor con ruolo; `TrainAsync` nuova firma con cameraRole; solo aggiunte
- Comportamento runtime: nessun cambiamento con Side disabilitata (default). Il TOP ora confronta lo
  shadow contro TopResult invece dell'esito globale: piu' accurato, cambia solo le statistiche advisory

## [2026-07-10] Versione 3.0.6.7 - AI: statistiche shadow-mode in PC Diagnostics

Rende leggibile a colpo d'occhio la qualita' del classificatore ONNX: invece di scorrere migliaia
di righe VISION_ML_SHADOW nei log, un riquadro in PC Diagnostics aggrega il confronto ML vs regole
VisionPro su finestra mobile.

**File modificati:**
- `Services/OnnxDefectClassifier.cs` — aggregatore in memoria (ring buffer ultimi 2000 confronti,
  thread-safe). Ogni confronto shadow registra (rule_defective, ml_defective, confidence). Nuovi
  `GetShadowStats()` (snapshot con matrice di confusione + metriche) e `ResetShadowStats()`.
  Nuova classe `ShadowModeStats` (solo lettura, senza dipendenze WPF): Total, TP/FN/FP/TN,
  AgreementRate, NokRecall, NokPrecision, FalseAlarmRate, MeanConfidence + stringhe % formattate.
  Convenzione: regole VisionPro = verita' di riferimento, "difetto" = classe positiva
- `ViewModels/SystemDiagnosticsViewModel.cs` — `ShadowStats` (+ `HasShadowStats`),
  `RefreshShadowStatsCommand`, `ResetShadowStatsCommand` (Azzera role-gated). Popolato all'avvio
  e su comando da `ServiceLocator.DefectClassifier`
- `Views/UserControls/SystemDiagnosticsView.xaml` — nuova card "Statistiche shadow-mode AI"
  (Grid.Row 8): 5 metriche (Accordo, Recall difetti NOK evidenziata, Precisione NOK, Falsi allarmi,
  Confidenza media) + riga riepilogo (campioni / difetti reali / mancati / falsi allarmi) + pulsanti
  Aggiorna/Azzera
- `Properties/AssemblyInfo.cs` — release `3.0.6.6` → `3.0.6.7`

**Motivo:** valutare il modello con numeri (soprattutto la RECALL sui difetti, la metrica chiave in
un dataset sbilanciato) invece di ispezionare i log a mano; decidere in modo informato quando/se
promuovere il modello oltre lo shadow-mode.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- API pubblica: `OnnxDefectClassifier` (classe concreta usata dal ServiceLocator) espone due nuovi
  metodi; nessuna firma esistente cambiata; `IDefectClassifier` invariata
- Comportamento runtime: nessun impatto sul ciclo (aggregazione in memoria, off-thread come il
  confronto shadow gia' esistente); azzerabile senza riavvio

## [2026-07-10] Versione 3.0.6.6 - AI training: fix BOM sidecar label.json (dataset "vuoto" fantasma)

Fix macchina: "Addestra modello" falliva con "Dataset insufficiente (trovati OK=0, NOK=0)" pur
essendoci 165 campioni etichettati su disco e 163 righe in tbl_training_samples.

**Causa (diagnosi verificata sul campo):** i sidecar `label.json` vengono scritti da
`TrainingDataCollectionService` con `Encoding.UTF8`, che aggiunge il **BOM UTF-8** (`EF BB BF`).
Lo script Python li apriva con `encoding="utf-8"`: `json.load` sul BOM iniziale solleva
`JSONDecodeError` ("Unexpected UTF-8 BOM, decode using utf-8-sig"), catturato dal
`except (OSError, ValueError): continue` → TUTTI i 165 file scartati in SILENZIO → `ok=0, nok=0`.
La pre-scansione C# non se ne accorgeva perche' `File.ReadAllText` rimuove il BOM in automatico
(da qui il conteggio corretto lato HMI ma zero lato Python). Nessun dato mancante: i dati e le
immagini `*_T_A.bmp` erano tutti presenti e corretti.

**File modificati:**
- `Scripts/AI/train_defect_classifier.py` — `scan_dataset` legge i sidecar con `encoding="utf-8-sig"`
  (gestisce sia file CON BOM, gia' esistenti, sia senza). Aggiunti contatori di scarto ESPLICITI
  nel progress di scan: `json_illeggibile` e `label_ignota` oltre a `senza_immagine`, cosi' un
  dataset vuoto mostra SUBITO il motivo invece di uno 0 muto. I contatori sono inclusi anche nel
  sidecar `.metrics.json`
- `Services/TrainingDataCollectionService.cs` — i nuovi sidecar vengono scritti UTF-8 SENZA BOM
  (`new UTF8Encoding(false)`): file puliti d'ora in poi (i vecchi con BOM restano leggibili grazie a utf-8-sig)
- `Properties/AssemblyInfo.cs` — release `3.0.6.5` → `3.0.6.6`

**Verifica:** rieseguito lo scan reale su `D:\QtisVision\Pieces` con lo script corretto →
`ok=125, nok=40, json_illeggibile=0` (prima `0/0`). Confermata anche con test byte-level del BOM
e confronto `utf-8` vs `utf-8-sig`.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- API pubblica: nessuna firma cambiata
- Comportamento runtime: il training ora vede il dataset gia' raccolto; nessun intervento manuale
  richiesto sui file esistenti. NB: la copia dell'exe in bin richiede la chiusura dell'app in
  esecuzione (lock), la compilazione e' comunque ok

## [2026-07-10] Versione 3.0.6.5 - AI training: risoluzione robusta dell'eseguibile Python

Fix del problema riportato su macchina: il pulsante "Addestra modello" / "Test Python" falliva con
"Python non avviabile ('python'): Impossibile trovare il file specificato" pur avendo Python 3.9.13
installato e funzionante dalla shell (`python --version`).

**Causa:** `Process.Start` con `UseShellExecute=false` cerca l'eseguibile nel PATH del PROCESSO
dell'app. Con un'installazione Python per-utente (python.org "install for current user", in
`%LOCALAPPDATA%\Programs\Python\Python39\`, aggiunta al PATH UTENTE), l'app non lo trova se e' stata
avviata prima dell'aggiornamento del PATH o in un contesto diverso. Inoltre l'alias dello Store
`...\Microsoft\WindowsApps\python.exe` e' uno stub che fa fallire CreateProcess con lo stesso errore.

**File modificati:**
- `Services/DefectClassifierTrainingService.cs` — `NormalizeExecutable` ora risolve il nome nudo
  ("python") a un PERCORSO ASSOLUTO tramite il nuovo `ResolvePythonExecutable`:
  1. percorso esplicito (con separatori) → rispettato com'e';
  2. ricerca nel PATH di PROCESSO + UTENTE + MACCHINA (letti dal registro, cosi' si trova Python
     anche se il PATH utente e' stato aggiornato dopo l'avvio dell'app), SALTANDO le voci
     `\Microsoft\WindowsApps` (alias Store);
  3. probe delle cartelle d'installazione tipiche: `%LOCALAPPDATA%\Programs\Python\Python3*`,
     `%ProgramFiles%`, `%ProgramFiles(x86)%`, `C:\Python3*` (piu' recente prima);
  4. fallback al py launcher `C:\Windows\py.exe` se presente;
  5. se nulla e' trovato, resta il nome originale (errore di avvio esplicito).
  Il percorso risolto viene loggato (`AI_TRAINING_PYTHON_RESOLVED|source=...|path=...`). Punto unico:
  vale sia per il training sia per il "Test Python". Il valore salvato in config NON cambia (la
  risoluzione avviene solo a runtime).

- `Properties/AssemblyInfo.cs` — release `3.0.6.4` → `3.0.6.5`

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- API pubblica: nessuna firma cambiata (soli metodi privati aggiunti)
- Comportamento runtime: nessun cambiamento per chi aveva gia' un percorso Python esplicito
  funzionante; ora "python" nudo viene risolto automaticamente all'installazione per-utente

## [2026-07-10] Versione 3.0.6.4 - AI training progress: heartbeat processo Python e step dettagliati

Rifinitura dopo prova su macchina: il training partiva correttamente ma, durante import
dipendenze o calcolo CPU, poteva restare senza messaggi per troppo tempo. Ora la UI e i log
mostrano chiaramente se il processo Python e' vivo e quale fase sta eseguendo.

**File modificati:**
- `Services/DefectClassifierTrainingService.cs` - aggiunto `PYTHONUNBUFFERED=1`, heartbeat ogni
  15 secondi mentre il processo Python e' vivo, log/stato `stage=process` e `stage=running` se non
  arriva nuovo output
- `Scripts/AI/train_defect_classifier.py` - aggiunti progressi prima/dopo import dipendenze AI,
  split train/validation, inizializzazione modello, inizio epoca, batch intermedi e validazione
  epoca
- `Documentation/MachineHardware/software-version-archive.md`,
  `Documentation/AI/ONNX_Defect_Classifier_Guide.md` - note operative aggiornate
- `Properties/AssemblyInfo.cs` - release `3.0.6.3` -> `3.0.6.4`

**Motivo:** rendere distinguibile un training realmente bloccato da un training CPU-bound ancora in
esecuzione, specialmente su PC industriali dove import `torch` e resize immagini possono richiedere
tempo.

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessuna modifica
- Database: nessuna modifica
- Runtime macchina: nessuna modifica a VisionPro, trigger, scarto, contatori o ricette
- AI: nessun cambio al modello prodotto; cambia solo la diagnostica/progress del training

## [2026-07-10] Versione 3.0.6.3 - AI training hardening: Test Python, parametri HMI e modello scritto in modo atomico

Rifinitura della Fase 3b dopo review tecnica: il training resta fuori processo e supervisionato,
ma ora e' piu' adatto al collaudo su macchina reale e a PC senza Python nel PATH.

**File modificati:**
- `Views/UserControls/SystemDiagnosticsView.xaml` / `.xaml.cs` - nella sezione ONNX aggiunti i
  campi `Python executable`, `Model output folder`, `Epochs` e il pulsante `Test Python`
- `ViewModels/SystemDiagnosticsViewModel.cs` - il training passa a `DefectClassifierTrainingService`
  i binding correnti della UI, quindi resize/grayscale/normalizzazione modificati in pagina non
  vengono persi; aggiunte notifiche finali di successo/fallimento e stato live anche in
  `AiSettingsStatus`
- `Services/DefectClassifierTrainingService.cs` - aggiunto self-test Python, pre-scan dataset in
  background, logging `AI_TRAINING_PROGRESS`, audit start/completed/failed/cancel/timeout e uso
  robusto degli argomenti processo
- `Scripts/AI/train_defect_classifier.py` - aggiunto `--self-test`, dipendenza `onnx`, validazione
  `onnx.checker`, export su file temporaneo e `os.replace` finale per evitare modelli parziali
- `Services/MachineConfigurationService.cs` - clamp/default per `DefectClassifierTrainingPythonPath`,
  `DefectClassifierTrainingOutputDir`, `DefectClassifierTrainingEpochs`
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_eng.json`,
  `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - nuove chiavi lingua
  per controlli training
- `Documentation/AI/ONNX_Defect_Classifier_Guide.md`,
  `Documentation/MachineHardware/software-version-archive.md`,
  `Documentation/MachineHardware/architecture-and-modules.md` - guida e release aggiornate
- `Properties/AssemblyInfo.cs` - release `3.0.6.2` -> `3.0.6.3`

**Motivo:** evitare training con parametri UI non salvati, rendere visibile lo stato del training,
diagnosticare subito macchine senza Python/dependencies e proteggere il file `.onnx` da scritture
incomplete.

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessuna nuova chiave rispetto a `3.0.6.2`; i 3 campi
  training diventano editabili da HMI
- Database: nessuna modifica
- Runtime macchina: nessuna modifica a VisionPro, trigger, scarto, contatori o ricette
- AI: Python serve solo per addestrare; per usare un modello `.onnx` gia' creato non serve Python
  installato sulla macchina

## [2026-07-10] Versione 3.0.6.2 - AI Fase 3b: training on-demand del classificatore (pulsante "Addestra modello")

Chiude il cerchio della pipeline dati AI: raccolta dataset (Fase 3a) → TRAINING dal pannello →
shadow-mode (Fase 3). Il pulsante in PC Diagnostics → Controlli AI → Classificatore ONNX addestra
il modello dal dataset `label.json` gia' raccolto e crea `defect_classifier_vN.onnx` (v1, v2, ...)
in `C:\QtisVision\AI\Models`.

Principio architetturale mantenuto: il training resta OFFLINE e SUPERVISIONATO. Il calcolo gira in
uno script Python lanciato come PROCESSO ESTERNO (isolamento totale: un crash/OOM del training non
tocca l'HMI 24/7) e il modello prodotto NON viene mai abilitato automaticamente — il pannello viene
precompilato (percorso/versione/note con metriche) e l'operatore conferma con "Salva".

**File aggiunti:**
- `Scripts/AI/train_defect_classifier.py` — script di training (copiato in output dal build):
  scandisce ricorsivamente i sidecar `label.json`, carica l'immagine TOP del pezzo (`*_T_A.bmp`,
  fallback `*_T_Z.jpg` — stessa convenzione di OnnxDefectClassifier), addestra una CNN leggera DA
  ZERO (nessun peso preaddestrato: funziona air-gapped, CPU-only), con split stratificato, pesi di
  classe contro lo sbilanciamento OK/NOK e selezione della migliore epoca su validazione; esporta
  ONNX (opset 12, input `NCHW [1,C,H,W]`, classe 0=OK/1=NOK) + sidecar `.metrics.json`.
  Protocollo verso l'HMI: righe `PROGRESS|...` e `RESULT|{json}`; exit code dedicati.
  Il preprocessing di training (resize, grayscale, (pixel-mean)/std) arriva DALLA CONFIG HMI:
  il modello esce coerente col contratto di `OnnxDefectClassifier` per costruzione.
  Dipendenze: `pip install torch pillow numpy onnx`
- `Services/DefectClassifierTrainingService.cs` — orchestrazione: pre-scan dataset (min 10
  campioni/classe), nome versionato automatico, lancio processo con stdout/stderr rediretti,
  progresso live, cancel (kill) e timeout 60 min, verifica finale che l'ONNX sia caricabile da
  ONNX Runtime. Non lancia mai eccezioni: ogni errore torna come esito con messaggio chiaro

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs` — `DefectClassifierTrainingPythonPath` (default `python`),
  `DefectClassifierTrainingOutputDir` (default `C:\QtisVision\AI\Models`),
  `DefectClassifierTrainingEpochs` (default 20)
- `Services/MachineConfigurationService.cs` — marker schema-upgrade dei 3 campi
- `ViewModels/SystemDiagnosticsViewModel.cs` — `TrainDefectClassifierCommand`/`CancelTrainingCommand`
  (pattern wrapper void + core async Task), `TrainingStatus`/`IsTrainingRunning`; guardie: ruolo
  Installer/Administrator, RIFIUTO se produzione attiva (training CPU-intensivo), dialog di conferma;
  a successo precompila i campi ONNX del pannello (non salvati) e logga `AI_MODEL_TRAINED`
- `Views/UserControls/SystemDiagnosticsView.xaml` — pulsanti "Addestra modello"/"Annulla" + stato
  live nella colonna Classificatore ONNX
- `QtisVisionPanel.csproj` — `Compile` del servizio + `Content` (PreserveNewest) dello script
- `Documentation/AI/ONNX_Defect_Classifier_Guide.md` — nuova sezione "Training Integrato Dal Pannello"
- `Properties/AssemblyInfo.cs` — release `3.0.6.1` → `3.0.6.2`

**Motivo:** richiesta cliente: generare il modello `defect_classifier_v1.onnx` con un click,
integrando script Python nell'applicazione. Scelta processo-esterno (vs Python in-process) per
isolamento su HMI 24/7.

**Impatto:**
- Config.xml / machine_runtime_config.xml: 3 nuovi campi (default sicuri)
- Database: nessuna modifica
- API pubblica: solo aggiunte (nuovo servizio, nuovi comandi VM)
- Comportamento runtime: nessun cambiamento finche' il pulsante non viene premuto; richiede Python
  con torch/pillow/numpy installati sulla macchina (errore chiaro se mancano)

## [2026-07-10] Versione 3.0.6.1 - ONNX TOP-only: resize modello chiarito e protetto

Correzione di chiarezza dopo feedback in collaudo: nel pannello `Controlli AI`, i campi
`Width/Height` potevano essere interpretati come risoluzione nativa camera. Non e' cosi':
il classificatore ONNX corrente lavora solo sull'immagine TOP salvata e ridimensiona l'immagine
alla dimensione richiesta dal modello.

**File modificati:**
- `Views/UserControls/SystemDiagnosticsView.xaml` / `.xaml.cs` - titolo ONNX aggiornato a
  `TOP shadow-mode`, label `Resize width/height` e messaggio esplicito: non usare la
  risoluzione nativa camera
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_eng.json`,
  `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - nuove/aggiornate
  chiavi lingua per il chiarimento ONNX
- `ViewModels/SystemDiagnosticsViewModel.cs` - validazione salvataggio: input ONNX massimo
  `1024 px` per lato quando il classificatore e' abilitato
- `Services/MachineConfigurationService.cs` - clamp lato config portato a `16..1024 px`
  per width/height ONNX
- `Models/MachineRuntimeConfiguration.cs` - commento esplicito: TOP-only e resize modello
- `Documentation/AI/*`, `Documentation/MachineHardware/*` - note guida e release aggiornate
- `Properties/AssemblyInfo.cs` - release `3.0.6.0` -> `3.0.6.1`

**Motivo:** evitare che un tecnico inserisca valori come `1456x1088` o `1800x1500` pensando
alla risoluzione fisica delle camere. L'input ONNX deve corrispondere al modello addestrato,
tipicamente `224x224`, `320x320`, `512x512` o `640x640`.

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessuna nuova chiave; valori ONNX width/height
  fuori range vengono normalizzati
- Database: nessuna modifica schema
- Runtime macchina: nessuna modifica a trigger, VisionPro, scarto, ricette o contatori
- AI: resta shadow-mode, TOP-only, non bloccante e non decisionale

## [2026-07-09] Versione 3.0.6.0 - AI Fase 8 hardening: localizzazione, guardrail config e OPC UA

Rifinitura della Fase 8 AI/preallarmi dopo review: nessuna modifica alla logica di ispezione,
trigger, scarto, ricette o ciclo macchina. Il lavoro rende piu' robusti UI, salvataggio parametri
e pubblicazione OPC UA dei preallarmi advisory.

**File modificati:**
- `Views/UserControls/SystemDiagnosticsView.xaml` / `.xaml.cs` - la sezione "Controlli AI" e
  la card "Preallarmi salute macchina" non usano piu' testi hardcoded: label, checkbox e pulsanti
  passano dal sistema lingua condiviso. Aggiunto messaggio operativo sulle dipendenze:
  i preallarmi richiedono SPC e/o manutenzione predittiva abilitati
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_eng.json`,
  `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - nuove chiavi
  `Sub_entry_Ai*` per localizzazione completa ENG/ITA della UI AI
- `ViewModels/SystemDiagnosticsViewModel.cs` - validazione prima del salvataggio per soglie
  preallarmi, retention AI e parametri ONNX; l'operatore riceve un messaggio chiaro se un valore
  e' fuori range
- `Services/MachineConfigurationService.cs` - clamp lato config per impedire valori persistenti
  non coerenti in `machine_runtime_config.xml`
- `Services/OpcUaClientService.cs` - pubblicazione `WriteEarlyWarningAsync` resa single-flight,
  con timeout 5s e messaggio OPC tagliato a 1000 caratteri; se una scrittura advisory e' ancora
  in corso, la successiva viene saltata a livello Debug invece di accumulare lavoro
- `Documentation/MachineHardware/new-implementations-test-guide-2026-07-10.md` e
  `Documentation/MachineHardware/README.md` - aggiunta guida di collaudo per validare le nuove
  implementazioni AI/preallarmi, OPC UA, Top3D e la regressione macchina essenziale
- `Properties/AssemblyInfo.cs` - release `3.0.5.9` -> `3.0.6.0`

**Motivo:** chiudere i punti di review emersi dopo l'integrazione Claude Fase 8: testi
operatore non localizzati, parametri AI senza guardrail espliciti, dipendenze preallarmi poco
chiare e pubblicazione OPC UA da proteggere contro server/rete lenti.

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessuna nuova chiave; i campi AI esistenti vengono
  validati e normalizzati su range sicuri al salvataggio/load
- Database: nessuna modifica schema
- Runtime macchina: nessun cambiamento con flag AI/preallarmi disabilitati; anche da abilitati,
  i preallarmi restano advisory e non bloccano RunContinuous, scarto, trigger o VisionPro
- OPC UA: nodi non mappati restano no-op; un server lento non deve creare accumulo di scritture
  preallarme

## [2026-07-09] Versione 3.0.5.9 - AI Fase 8: UI abilitazione + pannello preallarmi (PC Diagnostics)

Terzo step Fase 8: interfaccia in PC Diagnostics per abilitare/configurare le notifiche salute
macchina e visualizzare i preallarmi. Solo UI + esposizione dati: nessuna modifica alla logica
di rilevamento/aggregazione/canali.

**File modificati:**
- `Services/MachineHealthNotificationService.cs` — storico recente in memoria (ultimi 100
  preallarmi, ring buffer) aggiornato in `Ingest` indipendentemente dal throttle di invio;
  nuovo `GetRecentWarnings()` (snapshot piu' recente per primo, copia difensiva). Helper di
  sola lettura su `EarlyWarning` per il binding (`SeverityText`, `CategoryText`,
  `DetectedAtLocalText`, `EtaText`) senza dipendenze WPF nel modello
- `ViewModels/SystemDiagnosticsViewModel.cs` — `ObservableCollection<EarlyWarning> EarlyWarnings`
  + `HasEarlyWarnings` + `RefreshEarlyWarningsCommand`; popolamento da
  `ServiceLocator.MachineHealthNotificationService.GetRecentWarnings()` all'avvio e su comando
- `Views/UserControls/SystemDiagnosticsView.xaml` — nel pannello "Controlli AI": checkbox
  "Notifiche salute macchina (preallarmi)" (bind `AiSettings.MachineHealthNotificationsEnabled`)
  + soglie (digest minuti, ETA critica pezzi, disco ore critiche). Nuova card "Preallarmi salute
  macchina" (Grid.Row 7) con lista colorata per severita' (Critical rosso, Warning arancio),
  categoria, titolo, causa a monte suggerita, ETA e timestamp locale; badge/converter di
  visibilita' aggiunti
- `Properties/AssemblyInfo.cs` — release `3.0.5.8` → `3.0.5.9`

**Motivo:** dare all'operatore/tecnico l'accensione dal pannello e la visibilita' dei preallarmi
senza dover leggere i log. I flag/soglie si bindano direttamente a `RuntimeBindings` (gia' esposto
come `AiSettings`) e si salvano col flusso "Salva" esistente dei Controlli AI (role-gated
Installer/Administrator, `RefreshConfiguration` a caldo).

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica (i campi esistevano gia' da 3.0.5.7)
- API pubblica: aggiunto `MachineHealthNotificationService.GetRecentWarnings()` + helper display su `EarlyWarning`
- Comportamento runtime: nessun cambiamento con flag off. Da abilitato, i preallarmi compaiono
  nel pannello oltre che nell'Events Monitor / OPC UA. La lista e' uno snapshot (pulsante "Aggiorna")

**Prossimi step:** canale Email/SMTP (server/destinatari, segreti fuori repo).

## [2026-07-09] Versione 3.0.5.8 - AI Fase 8: canale notifiche MES via OPC UA

Secondo step della Fase 8 (dopo la fondazione 3.0.5.7): primo canale esterno per i preallarmi
salute macchina — pubblicazione verso MES/SCADA tramite OPC UA. Additivo: implementa
`INotificationChannel` senza toccare l'aggregatore ne' i rilevatori di deriva.

**File modificati:**
- `Services/MachineHealthNotificationService.cs` — nuovo `OpcUaNotificationChannel`
  (implementa `INotificationChannel`), registrato nel costruttore del servizio accanto al canale
  locale. Sceglie il preallarme "guida" (severita' max → ETA piu' vicino → piu' recente),
  calcola severita' massima e conteggio, e per il digest sintetizza "N preallarmi (X critici)".
  Risolve `OpcUaClientService` a runtime: nessun problema di ordine init, riflette lo stato live
  enabled/connesso
- `Services/IOpcUaClientService.cs` + `Services/OpcUaClientService.cs` — nuovo
  `WriteEarlyWarningAsync(severity, code, message, etaValue, etaUnit, count)`: scrive i nodi
  read-only `EarlyWarning*` + una sequenza monotona (`EarlyWarningSequence`) cosi' il MES rileva
  un nuovo preallarme anche se il testo si ripete. No-op se OPC UA disabilitato/disconnesso
- `Cls_Config/Calss_structure/OpcUaConfig.cs` — 9 nodi di default `EarlyWarning*` (ClientToServer):
  Active, Severity, Code, Message, EtaValue, EtaUnit, Count, Timestamp, Sequence
- `Properties/AssemblyInfo.cs` — release `3.0.5.7` → `3.0.5.8`

**Motivo:** canale MES scelto dal cliente (OPC UA), air-gap friendly, riusa la connessione
OPC UA gia' presente senza credenziali nuove.

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessuna modifica
- Config OPC UA: le nuove configurazioni includono i 9 nodi `EarlyWarning*`. Le configurazioni
  ESISTENTI (specie se `UseDatabaseConfiguration=true`) NON li hanno finche' non vengono aggiunti
  dal pannello di configurazione OPC UA / DB. Ogni nodo pubblica SOLO se mappato e abilitato:
  un nodo assente e' un no-op sicuro (nessun errore)
- API pubblica: aggiunto `IOpcUaClientService.WriteEarlyWarningAsync` (solo aggiunta)
- Comportamento runtime: nessun cambiamento con `MachineHealthNotificationsEnabled=false` (default)
  o con OPC UA disabilitato. Da abilitati entrambi, i preallarmi appaiono sui nodi OPC UA
  `EarlyWarning*` oltre che nell'Events Monitor (canale locale)

**Prossimi step:** Email/SMTP (richiede server/destinatari, segreti fuori repo) e UI di
abilitazione + pannello preallarmi in PC Diagnostics.

## [2026-07-09] Versione 3.0.5.7 - AI Fase 8: monitor derive + notifica preallarmi (fondazione)

Prima fase dell'idea "monitorare in tempo reale le derive (salute PC + ispezioni) e mandare
preallarmi al cliente per anticipare i problemi a monte". Questa release e' la FONDAZIONE
sicura: aggregazione + astrazione canale + canale locale. I canali esterni (MES via OPC UA,
Email/SMTP) sono gli step successivi (implementano `INotificationChannel` senza toccare questa logica).

**File aggiunti:**
- `Services/MachineHealthNotificationService.cs` — nuovo servizio (Fase 8). Si sottoscrive agli
  eventi advisory GIA' esistenti `ProcessControlService.DriftDetected` (deriva ispezioni/SPC) e
  `PredictiveMaintenanceService.MaintenancePredicted` (deriva salute PC), che finora NON avevano
  consumatori. Normalizza in `EarlyWarning` (severita' + stima "quanto manca al limite": pezzi
  per le ispezioni, ore per il disco), deduplica/throttla, e instrada sui canali. Cadenza:
  CRITICO subito, resto in DIGEST periodico (timer). Contiene anche l'interfaccia
  `INotificationChannel`, il modello `EarlyWarning`/`EarlyWarningSeverity` e il canale di default
  `LocalEventLogNotificationChannel` (scrive nell'Events Monitor: rete di sicurezza sempre attiva).
  Tutto off-thread, degradation-safe: un errore di canale non ferma la macchina.

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs` — nuovi flag: `MachineHealthNotificationsEnabled` (default
  false), `MachineHealthDigestIntervalMinutes` (480 = ~1 turno), `MachineHealthCriticalEtaProducts`
  (50), `MachineHealthDiskCriticalHours` (24)
- `Services/MachineConfigurationService.cs` — marker schema-upgrade per i 4 nuovi campi
- `ServiceLocator.cs` — singleton `MachineHealthNotificationService`; `Start()` chiamato in
  `Initialize()` DOPO ProcessControl/PredictiveMaintenance (per sottoscriverne gli eventi);
  `SafeDispose` in `Reset()`
- `ViewModels/SystemDiagnosticsViewModel.cs` — `RefreshConfiguration()` del nuovo servizio nel refresh AI a caldo
- `QtisVisionPanel.csproj` — `<Compile Include>` del nuovo file (csproj classico)
- `Properties/AssemblyInfo.cs` — release `3.0.5.6` → `3.0.5.7`

**Motivo:** dare la base per i preallarmi predittivi al cliente riusando i rilevatori di deriva
gia' presenti, senza duplicarli e senza introdurre rischi (default off, solo canale locale in
questa fase).

**Impatto:**
- Config.xml / machine_runtime_config.xml: nuovi 4 campi (default sicuri; assenti su vecchi XML = default)
- Database / API pubblica: nessuna firma rimossa/cambiata (solo aggiunte)
- Comportamento runtime: nessun cambiamento finche' `MachineHealthNotificationsEnabled=false`
  (default). Abilitandolo, i preallarmi compaiono nell'Events Monitor (`AI_EARLY_WARNING`,
  `AI_HEALTH_DIGEST`); richiede anche `ProcessControlEnabled`/`PredictiveMaintenanceEnabled` per
  avere segnali in ingresso

**Prossimi step (canali esterni, additivi):**
- MES via OPC UA: estendere `OpcUaClientService` con nodi salute/preallarme (air-gap friendly,
  nessuna credenziale nuova) implementando `INotificationChannel`
- Email/SMTP: `System.Net.Mail`, config server/destinatari (segreti fuori dal repo), non bloccante
- UI: pannello preallarmi + abilitazione flag in PC Diagnostics (pannello "Controlli AI")

## [2026-07-09] Versione 3.0.5.6 - Top3D Detection Sensitivity: messaggi lingua allineati

Patch di completamento della release `3.0.5.5`: la nuova sezione "Detection sensitivity"
del Job Tool Editor era funzionale, ma alcuni testi erano hardcoded e alcune chiavi usate
dal ViewModel non erano presenti nel server messaggi.

**File modificati:**
- `ServerMessage/ServerMessageStructure.cs` - aggiunte le chiavi
  `Sub_entry_Top3DDetectionSensitivityLabel`, `Sub_entry_ReadDetectionSensitivity`,
  `Sub_entry_ApplyDetectionSensitivity` e gli stati dedicati read/apply
- `Views/JobToolEditorView.xaml` / `.xaml.cs` - label e pulsanti Top3D Detection
  Sensitivity ora vengono localizzati tramite `ServerMessagePersonalize`
- `ViewModels/JobToolEditorViewModel.cs` - gli stati di detection sensitivity usano
  messaggi dedicati, non piu' le chiavi generiche dell'esposizione
- `scripts/UpdateRuntimeLanguageFiles.ps1`, `Localization/messages_eng.json`,
  `Localization/messages_ita.json` - cataloghi lingua ENG/ITA aggiornati; lo script e'
  stato eseguito anche sui file runtime in `C:\QtisVision\Language`
- `Properties/AssemblyInfo.cs` - release `3.0.5.5` -> `3.0.5.6`
- `Documentation/MachineHardware/software-version-archive.md` - archivio versione

**Motivo:** rispettare la regola della baseline: ogni label/testo nuovo visibile da HMI
deve passare dal server messaggi e dagli script di lingua, evitando testi hardcoded non
traducibili.

**Impatto:**
- Config.xml / ricette / Database: nessuna modifica aggiuntiva rispetto alla `3.0.5.5`
- Runtime produzione: nessun cambio a trigger, acquisizione, validazione o scarto
- UI: la sezione Top3D Detection Sensitivity mantiene lo stesso comportamento ma ora e'
  localizzabile in ENG/ITA

## [2026-07-09] Versione 3.0.5.5 - Top3D: Detection Sensitivity per-ricetta + fix rumore ExposureAuto (L38)

Richiesta macchina Top3D (testa Cognex L38-300R). Due interventi:
1. l'impostazione dell'esposizione loggava un warning `impossibile impostare ExposureAuto`
   perche' l'L38 (sensore di spostamento 3D) non ha quella feature GigE (l'esposizione veniva
   comunque applicata: era solo rumore);
2. mancava la possibilita' di regolare la "Detection Sensitivity" della testa 3D dall'HMI.

**ATTENZIONE — richiede validazione su macchina prima del merge in master:**
- il nome della feature GigE per la detection sensitivity e' un DEFAULT indicativo
  (`DetectionSensitivity`), da verificare/correggere con il nome reale dell'albero feature
  del sensore (campo `Top3DDetectionSensitivityFeature` in Config.xml, nessuna ricompilazione);
- la scrittura sulla testa va provata sull'hardware reale.

**File modificati:**
- `Cls_Vpro/IgigaCameraAccess.cs` — `ApplyExposureTimeUs` usa `TrySetOptionalStringFeature` per
  `ExposureAuto`: se la feature non esiste (teste 3D) il log scende a Debug, non piu' Warn.
  Nuovi `ReadDetectionSensitivity(job, featureName)` e `ApplyDetectionSensitivity(job, featureName, value)`
  (double via GigE, try/catch, non bloccanti)
- `Cls_Config/Calss_structure/RecipeParameters.cs` — `RecipeParamTop3D.Top3DDetectionSensitivity`
  (double, per-ricetta; `<= 0` = non configurata → non applicata, retrocompatibile)
- `Cls_Config/Calss_structure/ConfigClassStructure.cs` — `Top3DDetectionSensitivityFeature`
  (nome feature GigE, proprieta' hardware globale; default `DetectionSensitivity`)
- `Cls_Config/AsyncRecipeParam.cs` — `UpdateRecipeParamTop3DAsync` (persistenza atomica del blocco 3D)
- `ViewModels/JobToolEditorViewModel.cs` — proprieta' `IsTop3DPanelSelected` + campi/comandi
  `Top3DDetectionSensitivity*`; read dalla testa e apply (scrittura camera + salvataggio per-ricetta)
- `Views/JobToolEditorView.xaml` — sezione "Detection sensitivity" (Read/Apply) visibile SOLO quando
  il pannello selezionato e' Top3D (binding `IsTop3DPanelSelected`)
- `MainWindow.xaml.cs` — `ApplyTop3DDetectionSensitivityFromRecipe` al caricamento ricetta:
  applica alla testa 3D il valore salvato in ricetta (no-op se top non e' 3D o valore non configurato)
- `Properties/AssemblyInfo.cs` — release `3.0.5.4` → `3.0.5.5`

**Motivo:** dare all'operatore/tecnico la regolazione della detection sensitivity del sensore 3D
per-prodotto (dipende dalla superficie), e togliere il warning fuorviante sull'L38.

**Impatto:**
- Config.xml: nuovo campo `Top3DDetectionSensitivityFeature` (default `DetectionSensitivity`)
- Ricetta: nuovo campo `recipeParamTop3D/Top3DDetectionSensitivity` (default 0 = non applicata)
- Database / API pubblica: nessuna firma rimossa o cambiata (solo metodi aggiunti)
- Comportamento runtime: invariato finche' la sensitivity resta 0 (ricette esistenti). L'esposizione
  su L38 non cambia comportamento: sparisce solo il warning ExposureAuto. Il display 3D e l'errore
  `Index out of bounds` / `Measure ... Results.Item[0] is Nothing` NON sono toccati da questa release
  (problema a livello di job VPP, da affrontare separatamente)

## [2026-07-08] Versione 3.0.5.4 - Fase 0: attese bloccanti ridotte in Live Preview e diagnostica

Batch incrementale dell'improvement plan dopo verifica macchina del lavoro precedente.
La modifica evita attese bloccanti non critiche senza toccare il ciclo produttivo,
le ricette, il mapping I/O, il DB o il runtime di ispezione.

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs` - il wait loop della Live Preview non usa piu'
  `Thread.Sleep(10)`; l'attesa di completamento del frame VisionPro usa `Task.Delay`
  cancellabile mantenendo timeout e recovery esistenti
- `Services/SystemDiagnosticsService.cs` - il priming del PerformanceCounter CPU non
  blocca piu' 100 ms il refresh diagnostico; il primo snapshot puo' mostrare `0%`,
  il valore reale arriva dal refresh successivo
- `Documentation/improvement-plan-2026-07.md` - stato Fase 0 aggiornato:
  gli sleep non critici sono chiusi, restano i tre `Thread.Sleep` in
  `ICognexJobManager` da trattare con prova dedicata su macchina
- `Properties/AssemblyInfo.cs` - release `3.0.5.3` -> `3.0.5.4`
- `Documentation/MachineHardware/software-version-archive.md`,
  `Documentation/MachineHardware/architecture-and-modules.md` - documentazione release

**Motivo:** ridurre freeze e occupazione thread in viste di setup/diagnostica prima di
toccare il lifecycle Cognex vero e proprio. Il batch chiude un pezzo della Fase 0 a
basso rischio e lascia esplicito il confine del prossimo lavoro.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- Runtime produzione: nessun cambio nella logica trigger, acquisizione, validazione,
  contatori o scarto
- UI: Live Preview e PC Diagnostics mantengono lo stesso comportamento operativo; la
  diagnostica CPU puo' mostrare `0%` solo al primo refresh dopo inizializzazione
- Resta aperto: `ICognexJobManager` contiene ancora `Thread.Sleep(10/100/150ms)` nei
  percorsi start/stop job Cognex; da modificare solo con test macchina dedicato

## [2026-07-08] Versione 3.0.5.3 - Top3D display resolver per path VisionPro annidati

Correzione mirata per macchine `Top3D` dove VisionPro calcola correttamente le misure
ma il pannello resta blu/non visualizza l'immagine principale 3D quando la ricetta usa
path come `Measure.Height.InputImage`.

**File modificati:**
- `Services/CameraDisplayManager.cs` - il display produzione non usa piu' solo
  `record.SubRecords[subRecordKey]`; ora risolve chiave esatta, path annidati separati
  da `.`, path senza prefisso wrapper/toolblock (`Measure.Height.InputImage` ->
  `Height.InputImage`) e ricerca ricorsiva nel record `LastRun`
- `Services/CameraDisplayManager.cs` - se il path configurato non viene trovato, il
  display usa come fallback il primo record contenente `ICogImage` invece di lasciare
  il riquadro vuoto; se non esiste nessuna immagine viene loggato
  `DISPLAY_RECORD_PATH_NOT_FOUND`
- `Services/CameraDisplayManager.cs` - `GetCurrentTopCameraImage()` non usa piu'
  `SubRecords[0]`, evitando errori `Index was outside the bounds of the array` con
  record Top3D che non hanno layout 2D standard
- `Docs/Manual/04_Controllo_3D_Top3D.md` - chiarito che sono validi sia path completi
  QuickBuild sia path relativi al record `LastRun`
- `Documentation/MachineHardware/top3d-machine-configuration-guide-2026-04-13.md` -
  aggiunta la regola tecnica sui path display Top3D/Top2D e sulla separazione dagli
  output opzionali `Top3DRerenderResult` / `Top3DPointCloud`
- `Properties/AssemblyInfo.cs` - release `3.0.5.2` -> `3.0.5.3`
- `Documentation/MachineHardware/software-version-archive.md`,
  `Documentation/MachineHardware/architecture-and-modules.md` - documentazione release

**Motivo:** nelle applicazioni 3D il record VisionPro puo' essere esposto come
`LastRun.Height.InputImage`, mentre la ricetta puo' salvare il path completo del
toolblock (`Measure.Height.InputImage`). Il vecchio lookup diretto non gestiva questa
equivalenza e poteva fallire lasciando il pannello senza immagine.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- Ricette: nessuna migrazione; i path esistenti restano validi
- Runtime: cambia solo il modo in cui la HMI seleziona il record immagine da mostrare
  per il display; la validazione Top3D, i contatori e lo scarto non sono modificati
- Nota: il warning `Top3D artifact export skipped: output 'Top3DRerenderResult'...`
  riguarda solo il salvataggio opzionale di sidecar 3D e non impedisce la visualizzazione
  dell'immagine in Overview

## [2026-07-07] Versione 3.0.5.2 - Fase 0: chiuso il GC.Collect residuo (image locking gia' risolto)

Quinto batch della Fase 0. Lo step "WPF image locking" del piano prevedeva di caricare
le immagini ricetta con `BitmapCacheOption.OnLoad`+`Freeze` e POI rimuovere il
`GC.Collect()` residuo di `ReleaseImageLock`. La verifica ha mostrato che il caricamento
corretto era GIA' implementato ovunque: tutte le immagini ricetta passano dal
`RecipeImagePathConverter` (`File.ReadAllBytes` → `MemoryStream` → `OnLoad` → `Freeze`,
righe 470 e 910 di `RecipeManagerView.xaml`) e il preload in `App.xaml.cs` usa lo stesso
pattern. La UI non tiene MAI un handle sul file immagine: il GC era compensazione morta.

**File modificati:**
- `ViewModels/RecipeManagerViewModel.cs` — rimosso il `GC.Collect()+WaitForPendingFinalizers()`
  da `ReleaseImageLock` (con motivazione documentata nel codice); i retry di
  `TryDeleteFileAsync` restano come rete di sicurezza per lock transitori (es. antivirus)
- `Converters/BooleanConverters.cs` — il catch silenzioso di `RecipeImagePathConverter`
  ora logga a Debug il percorso dell'immagine illeggibile (igiene CLAUDE.md)
- `Documentation/improvement-plan-2026-07.md` — riga GC.Collect promossa a `[x]`
  (resta solo il GC di shutdown, deliberato e fuori dal ciclo produttivo); step
  "WPF image locking" rimosso dai prossimi batch con nota esplicativa
- `Properties/AssemblyInfo.cs` — release `3.0.5.1` → `3.0.5.2`

**Motivo:** completare la metrica del piano "0 `GC.Collect()` su percorsi runtime".
Con questo batch la metrica e' raggiunta.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- Comportamento runtime: la cancellazione/sostituzione dell'immagine ricetta non esegue
  piu' un full GC bloccante; nessun altro cambiamento funzionale
- Restano aperti (richiedono macchina): batch VisionPro/Cognex lifecycle e code
  risultati ispezione con back-pressure/allarme

## [2026-07-07] Versione 3.0.5.1 - Fase 0: async command wrappers e piano flaggato

Quarto batch leggero della Fase 0 dell'improvement plan. Nessun cambiamento a
ricette, DB, `Config.xml` o `machine_runtime_config.xml`; il lavoro riduce il
rischio di eccezioni async non osservate nei comandi UI e rende leggibile lo
stato di avanzamento del piano.

**File modificati:**
- `ViewModels/MainViewModel.cs` - `Start`, `Stop`, `Restart` e navigazione passano
  al pattern wrapper `void` + core `async Task` eseguito tramite `SafeFireAndForget`
- `ViewModels/EjectionAlarmCardViewModel.cs` - comandi salva/refresh e gestione
  allarmi scarto convertiti allo stesso pattern osservabile/loggato
- `ViewModels/JobToolEditorViewModel.cs` - Live View convertita da `async void` a
  wrapper + core async
- `Views/DbQuatis.xaml.cs` - inizializzazione della vista resa `Task` osservata
  tramite `SafeFireAndForget`
- `Documentation/improvement-plan-2026-07.md` - aggiunto stato Fase 0 con punti
  `[x]`, `[~]`, `[ ]` e prossimi batch consigliati
- `Properties/AssemblyInfo.cs` - release `3.0.5.0` -> `3.0.5.1`
- `Documentation/MachineHardware/software-version-archive.md`,
  `Documentation/MachineHardware/architecture-and-modules.md` - documentazione
  release

**Motivo:** continuare la pulizia del punto 0.3 del piano senza convertire alla
cieca timer, event handler WPF e callback SDK. I command handler convertiti non
richiedono firme `async void`; usando il core `async Task` le eccezioni restano
osservate e loggate dall'helper comune.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- Runtime macchina: nessun cambiamento di logica Start/Stop, navigazione,
  allarmi scarto o live editor; cambia solo il pattern di esecuzione async
- Piano improvement: restano aperti i batch VisionPro/Cognex, code risultati
  ispezione senza drop silenzioso e rimozione del `GC.Collect()` residuo dopo
  fix del file-lock immagini WPF

## [2026-07-07] Versione 3.0.5.0 - Fase 0: salvataggio immagini con slot pre-capture

Terzo batch leggero della Fase 0, costruito sopra la coda bounded immagini di `3.0.4.9`.
Nessun cambiamento nell'uso operatore e nessuna modifica a ricette, DB o file di configurazione.

**File modificati:**
- `SaveImage/ISaveImage.cs` - aggiunto uno slot guard non bloccante (`SemaphoreSlim`) davanti alla conversione immagini: se la coda da 32 scritture e' piena, il sistema logga `IMAGE_QUEUE_FULL|stage=pre-capture` e non crea `Bitmap`/`byte[]` pesanti destinati a essere scartati
- `SaveImage/ISaveImage.cs` - il writer rilascia lo slot solo dopo la scrittura o l'errore su disco; la capienza limita quindi la somma di elementi in coda e scrittura in corso
- `SaveImage/ISaveImage.cs` - shutdown della coda reso piu' prudente: se il writer non termina entro il timeout, le risorse non vengono disposte sotto un thread ancora attivo e viene loggato `IMAGE_QUEUE_SHUTDOWN_TIMEOUT`
- `Properties/AssemblyInfo.cs` - release `3.0.4.9` -> `3.0.5.0`
- `Documentation/MachineHardware/software-version-archive.md`, `Documentation/MachineHardware/architecture-and-modules.md` - documentazione release

**Motivo:** in `3.0.4.9` la coda immagini era bounded, ma la conversione del display/raw image in memoria avveniva comunque prima del `TryAdd`. Con disco o share in stallo, il thread chiamante poteva ancora spendere CPU e memoria temporanea per immagini che sarebbero poi state droppate. Ora il drop best-effort avviene prima della conversione quando la coda e' satura.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- Runtime: i risultati ispezione e i contatori non sono toccati; si limita solo il salvataggio immagini best-effort
- Diagnostica: `IMAGE_QUEUE_FULL|stage=pre-capture` indica storage lento/saturo prima della conversione immagine; `stage=post-capture` resta come guardia di race

## [2026-07-07] Versione 3.0.4.9 - Fase 0: code bounded per immagini e diagnostica

Secondo batch della Fase 0 dell'improvement plan, nell'ordine concordato: prima le code
bounded per immagini e diagnostica; la gestione Cognex/VisionPro (con test su macchina)
e la pulizia degli async void residui seguono nei prossimi batch. Le 5 code risultati
ispezione di MainWindow restano deliberatamente INVARIATE: il loro bounding rientra nel
batch Cognex/VisionPro per l'emendamento "nessun drop silenzioso sulle code ispezione".

**File modificati:**
- `SaveImage/ISaveImage.cs` — la coda di scrittura immagini (`BlockingCollection`) e' ora
  LIMITATA a 32 elementi (ogni elemento contiene l'intera immagine in byte[], anche molti MB:
  con disco lento o share di rete in stallo la coda illimitata cresceva senza limite in
  memoria). `EnqueueImageWrite` usa `TryAdd` non bloccante: a coda piena la scrittura viene
  scartata in modo ESPLICITO (contatore + warning `IMAGE_QUEUE_FULL`, loggato al primo drop
  e poi ogni 25) senza mai bloccare il thread chiamante. Il salvataggio immagini e' gia'
  campionato a percentuale: la produzione non e' impattata
- `Services/ApplicationEventLogger.cs` — il mirror DB degli eventi non usa piu' un
  `Task.Run(InsertAsync)` per evento (con MySQL in stallo i task si accumulavano senza
  limite dietro il gate del repository): ora una coda LIMITATA a 500 elementi svuotata da
  un unico drainer in background (`LongRunning`). A coda piena il solo mirror DB viene
  scartato con warning throttled `EVENTLOG_DB_QUEUE_FULL` (max 1/min) + contatore:
  l'evento resta comunque nei file di log NLog. La classe e' ora `IDisposable`
  (CompleteAdding + drain finale 3s allo shutdown)
- `ServiceLocator.cs` — `Reset()` ora fa `SafeDispose` di `ApplicationEventLogger`
  (drain finale della coda eventi allo shutdown)
- `Properties/AssemblyInfo.cs` — release `3.0.4.8` → `3.0.4.9`

**Motivo:** punto 0.4 dell'improvement plan (code senza limite di capienza → crescita
memoria illimitata sotto carico), applicato con la politica concordata: drop esplicito e
tracciato SOLO dove il dato e' best-effort (immagini campionate, mirror DB di eventi che
restano comunque su file); mai drop silenzioso.

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica
- API pubblica: `ApplicationEventLogger` implementa `IDisposable` (nessuna firma cambiata)
- Comportamento runtime: memoria bounded anche con disco o DB in stallo prolungato; in
  saturazione si perdono solo scritture best-effort, sempre con warning e contatore nei log.
  Nessun cambiamento nell'uso operatore

## [2026-07-07] Versione 3.0.4.8 - Fase 0 stabilita' 24/7 (improvement plan, primo batch)

Primo batch della Fase 0 di `Documentation/improvement-plan-2026-07.md` (con gli emendamenti
concordati in revisione). Nessun cambiamento nell'uso operatore.

**Freeze UI eliminati (GC.Collect forzati):**
- `MainWindow.xaml.cs` (`InitializeRecipeAsync`) e `ViewModels/RecipeManagerViewModel.cs` (salvataggio ricetta) — rimossi `GC.Collect()+WaitForPendingFinalizers()` (e il delay da 100ms al salvataggio): `AsyncRecipeParam` legge il file XML con `using` e non trattiene handle, quindi il GC forzato congelava la UI a ogni cambio/salvataggio ricetta senza rilasciare alcun lock reale
- `RecipeManagerViewModel.ReleaseImageLock` — il `GC.Collect()` e' **deliberatamente mantenuto** e documentato: compensa il file-lock delle `BitmapImage` WPF prima della cancellazione dell'immagine ricetta; la rimozione richiede prima il caricamento immagini con `BitmapCacheOption.OnLoad`+`Freeze` (follow-up Fase 1)
- Il `GC.Collect()` nello shutdown applicativo (fuori dal runtime) resta invariato

**Codice morto e catch silenziosi:**
- `Database/Cls_InitializzeDb.cs` — rimosso `Initialize1()` (177 righe): `async void`, mai invocato, creava `tblgenerale` senza indici (rischio schema-drift)
- `Models/EventLogEntry.cs` — il catch silenzioso sul parse JSON del campo `info` ora logga a Debug

**async void (niente piu' fuori dagli event handler, dove rischioso):**
- `Views/UserControls/StatisticsView.xaml.cs` — `ProcessInspectionResult` era `async void` SENZA try/catch chiamato dal flusso di ispezione (un'eccezione su thread di background poteva abbattere il processo); ora wrapper `void` + core `async Task` con `SafeFireAndForget`. Stessa conversione per `ResetCounters`
- `ViewModels/NavigationViewModel.cs` — `ReloadMenu` aveva il primo `await` fuori dal try/catch; convertito col wrapper. Stessa conversione per `OnExternalButtonClicked`
- `ViewModels/RecipeManagerViewModel.cs` — `UpdatePermissionsFromStaticRoles` e `CheckDatabaseAsync` convertiti col wrapper
- `Database/Cls_CheckUser.cs` — `InitializeUserAsync` da `async void` a `async Task`; il chiamante in `MainWindow` ora la attende direttamente (rimosso il `Task.Run` inutile)
- Verificato: gli handler globali (`DispatcherUnhandledException` con `Handled=true`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` con `SetObserved`) erano gia' completi in `App.xaml.cs` — il punto 0.3b del piano era gia' soddisfatto

**Timeout anti-hang:**
- `Cls_Config/AsyncConfigManagerXml.cs` — i 4 `SemaphoreSlim.WaitAsync()` (load/save/recovery/auto-heal) ora hanno timeout 30s: su mancata acquisizione l'operazione fallisce in modo visibile (log `CONFIG_LOCK_TIMEOUT` + `TimeoutException`; l'auto-heal best-effort si limita a loggare e rinunciare)
- `Database/Cls_InitializzeDb.cs` — `CommandTimeout` esplicito su `ExecuteQueryAsync` (60s) e sugli `ALTER TABLE` di `EnsureColumnAsync` (120s): l'avvio non puo' piu' restare appeso su un DB lento

**Rimandato (documentato):**
- `Thread.Sleep` in `ICognexJobManager` (10/100/150ms): sono nei path start/stop (non per-prodotto); la conversione richiede firme async sull'interfaccia → Fase 1 ("load VPP in background") come previsto dal piano stesso
- Code bounded (`Channel.CreateBounded`): per le code risultati ispezione serve un design senza drop silenzioso (emendamento) → batch successivo
- Restanti `async void` con try/catch completo su thread UI (MainViewModel Start/Stop/Restart/Navigate, EjectionAlarmCard ×4, JobToolEditor LiveView, DbQuatis): gia' coperti da try/catch + `DispatcherUnhandledException`; conversione di conformita' nel batch successivo

**Impatto:**
- Config.xml / machine_runtime_config.xml / Database: nessuna modifica di schema o chiavi
- API pubblica: `Cls_CheckUser.InitializeUserAsync` ora restituisce `Task` (unico chiamante aggiornato); `StatisticsView.ResetCounters/ProcessInspectionResult` e `NavigationViewModel.ReloadMenu` da `async void` a `void` (firme compatibili per i chiamanti)
- Comportamento runtime: cambio/salvataggio ricetta senza freeze da GC forzato; nessun hang silenzioso su lock config o DB lento; eccezioni dei percorsi convertiti sempre osservate e loggate

## [2026-07-07] Versione 3.0.4.7 - Snapshot encoder sull'edge fotocellula

**File modificati:**
- `Models/IIODeviceManager.cs` - `IOEvent` ora trasporta lo snapshot dei contatori encoder e il timestamp dello snapshot generato dal polling I/O
- `Models/AdvantechDeviceManager.cs` - il polling legge prima gli encoder, crea uno snapshot e poi legge/rileva gli input; gli eventi `InputChanged` ricevono lo stesso snapshot encoder del ciclo polling
- `ViewModels/DigitalIOViewModel.cs` - il flusso fotocellula usa il count encoder congelato nell'evento invece di rileggere `_machineController.GetLastMainEncoderCount()` dopo il passaggio sulla UI; il fast-path `TimedFromPhotocell` usa lo stesso snapshot; il log mostra anche `snapshotAge`
- `Properties/AssemblyInfo.cs` - release `3.0.4.6` -> `3.0.4.7`
- `Documentation/MachineHardware/code-changes-log.md`, `Documentation/MachineHardware/software-version-archive.md`, `Documentation/MachineHardware/architecture-and-modules.md` - documentazione release

**Motivo:** in macchina il primo prodotto veniva triggerato nella posizione corretta, mentre il secondo prodotto in fila veniva triggerato in ritardo. Il flusso precedente gestiva l'edge fotocellula sul dispatcher UI e leggeva il count encoder solo dopo; se la UI era occupata dal primo prodotto, il secondo `PRODUCT_ZERO` poteva nascere con un encoder gia' avanzato, spostando in avanti tutte le quote camera.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nessuna modifica schema o valori
- Database: nessuna modifica
- Runtime: la quota encoder del prodotto viene catturata dal polling I/O nello stesso ciclo dell'edge input e poi riusata anche se la UI aggiorna i log in ritardo. Quote macchina, pulse e mapping I/O restano invariati
- Limite tecnico: senza latch hardware encoder/fotocellula resta la granularita' del polling; per precisione assoluta su pezzi molto ravvicinati valutare latch/compare hardware o una logica trigger deterministica dedicata

## [2026-07-06] Versione 3.0.4.6 - Diagnostica ritardo reale trigger encoder camera

**File modificati:**
- `Models/TrackedProduct.cs` - aggiunti campi runtime sul punto intervento: encoder reale di raggiungimento, timestamp UTC, ritardo in count e ritardo in mm
- `Services/MachineController.cs` - quando un punto macchina viene raggiunto, il controller registra il valore encoder effettivo ricevuto dal polling/callback e logga target vs reale
- `ViewModels/DigitalIOViewModel.cs` - il log `Trigger request accepted` ora mostra target encoder, encoder reale e ritardo; se un trigger camera supera 5 mm di ritardo viene evidenziato come warning
- `Properties/AssemblyInfo.cs` - release `3.0.4.5` -> `3.0.4.6`
- `Documentation/MachineHardware/code-changes-log.md`, `Documentation/MachineHardware/software-version-archive.md`, `Documentation/MachineHardware/architecture-and-modules.md` - documentazione release

**Motivo:** con prodotti ravvicinati e immagini nere/fuori centro, il log precedente mostrava solo la quota target del punto intervento, non il valore encoder reale al momento in cui il runtime ha deciso di generare il pulse camera. Questo rendeva difficile distinguere tra configurazione quote errata, polling encoder arrivato tardi, distanza TOP/SIDE troppo ravvicinata o problema VisionPro.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nessuna modifica schema o valori
- Database: nessuna modifica
- Runtime: nessun cambio di logica trigger; vengono aggiunte solo misure diagnostiche nei log. Se appaiono warning con ritardi camera sopra 5 mm, verificare `MillimetersPerRevolution`, quote intervento, velocita' linea, distanza tra punti camera e possibilita' di usare punti coincidenti o trigger hardware per quote molto ravvicinate
- VisionPro: se il job mostra `CogBlobTool...Item[0] is Nothing`, aggiungere una guardia nel tool/script per gestire `blob count = 0` come NOK controllato; resta comunque necessario correggere il timing fisico se l'immagine arriva fuori prodotto

## [2026-07-06] Versione 3.0.4.5 - Trigger encoder/IO non bloccati dalla UI

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - rimosso il blocco sincrono `Dispatcher.Invoke()` dal percorso caldo encoder/trigger: aggiornamento live encoder, lista prodotti tracciati, eventi intervento, eventi macchina e log UI vengono accodati con `BeginInvoke`; l'esecuzione del punto intervento parte prima degli aggiornamenti UI
- `Properties/AssemblyInfo.cs` - release `3.0.4.4` -> `3.0.4.5`
- `Documentation/MachineHardware/code-changes-log.md`, `Documentation/MachineHardware/software-version-archive.md`, `Documentation/MachineHardware/architecture-and-modules.md` - documentazione release

**Motivo:** durante debug con prodotti ravvicinati sono state viste immagini nere/fuori prodotto. Nel percorso esistente il callback encoder poteva rimanere fermo in attesa della UI prima di generare il pulse fisico camera, perche' `Pulse START`, eventi tracking e liste diagnostiche passavano da `Dispatcher.Invoke()`. Con UI occupata da VisionPro, log o salvataggio immagini, il DO camera poteva quindi arrivare in ritardo.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nessuna modifica schema o valori
- Database: nessuna modifica
- Runtime: il tracking encoder e i pulse fisici camera non aspettano piu' l'aggiornamento UI; gli aggiornamenti visuali restano identici ma asincroni. Quote camera, `MillimetersPerRevolution`, pulse ms e mapping IO restano parametri macchina da verificare in commissioning
- Operativita': se l'immagine resta nera, controllare prima calibrazione encoder (`counts/mm`), quote intervento camera, camere realmente abilitate e durata pulse/luci

## [2026-07-06] Versione 3.0.4.4 - Controlli AI: validazione ONNX e reload immediato

**File aggiunti:**
- `Documentation/AI/ONNX_Defect_Classifier_Guide.md` - guida per generare e installare il modello `.onnx`: dataset da `label.json`, contratto input/output, export e collaudo shadow-mode

**File modificati:**
- `Services/OnnxDefectClassifier.cs` - aggiunto `RefreshConfiguration()`: scarica la sessione ONNX corrente e rilegge configurazione/modello dal file macchina senza riavviare l'app
- `ViewModels/SystemDiagnosticsViewModel.cs` - `SaveAiSettings` forza l'aggiornamento dei binding WPF prima del salvataggio, valida file `.onnx`/dimensioni/normalizzazione se il classificatore e' abilitato, mostra il path del file macchina attivo e ricarica anche `ServiceLocator.DefectClassifier`
- `Views/UserControls/SystemDiagnosticsView.xaml` - testo pannello aggiornato per indicare il reload immediato del classificatore
- `Properties/AssemblyInfo.cs` - release `3.0.4.3` -> `3.0.4.4`

**Motivo:** durante il collaudo il tecnico poteva abilitare il classificatore ONNX senza un modello valido o aspettarsi il reload dopo riavvio. Ora il pannello blocca configurazioni incoerenti e ricarica subito il classificatore dopo `Salva`.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nessuna nuova chiave; i campi `DefectClassifier*` esistenti vengono salvati e validati
- Database: nessuna modifica
- Runtime: il classificatore resta shadow-mode e non influenza scarto/contatori/esito; cambio modello/preprocessing da PC Diagnostics e' immediato dopo salvataggio se il file `.onnx` esiste

## [2026-07-03] Versione 3.0.4.3 - Pannello "Controlli AI" in PC Diagnostics

**File modificati:**
- `ViewModels/SystemDiagnosticsViewModel.cs` — nuova sezione Controlli AI: `AiSettings` (espone `RuntimeBindings` al binding), `AiSettingsStatus`, comandi `ReloadAiSettingsCommand`/`SaveAiSettingsCommand`; `SaveAiSettings` con gate ruolo (riusa `CanApplyTimingParameters`, Installer/Administrator), dialog di conferma, salvataggio via `MachineConfigurationService.Save` (atomico, con clamp), evento `AI_SETTINGS_APPLY` su Events Monitor e riapplicazione a caldo dei flag via `RefreshConfiguration()` di tutti i servizi AI
- `Views/UserControls/SystemDiagnosticsView.xaml` — nuova sezione "Controlli AI" (Grid.Row 6) in fondo alla pagina diagnostica, tre colonne: **Servizi** (7 toggle: fondazione dati, SPC, manutenzione predittiva, raccolta training, timing optimizer, performance monitor, advisor ricetta), **Classificatore ONNX** (abilitato, percorso/versione/note modello, dimensioni input, normalizzazione, scala di grigi), **Retention dati AI** (giorni per misure/salute/training, 0 = illimitata). Pulsanti Ricarica/Salva; editor disabilitati per ruoli non autorizzati
- `Properties/AssemblyInfo.cs` — release `3.0.4.2` → `3.0.4.3`

**Motivo:** i parametri AI di `machine_runtime_config.xml` erano modificabili solo a mano sul file XML. Ora il tecnico li abilita/configura da pannello nella pagina PC Diagnostics, con controllo ruolo e conferma.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nessuna nuova chiave (il pannello scrive le chiavi esistenti tramite il Save atomico)
- Database: nessuno
- API pubblica: nuove proprieta'/comandi su `SystemDiagnosticsViewModel`
- Comportamento runtime: i flag di abilitazione si riapplicano **a caldo** dopo il salvataggio (`RefreshConfiguration` dei servizi); le modifiche al classificatore ONNX (modello/preprocessing) richiedono il **riavvio** dell'applicazione (il modello e' caricato nel costruttore) — indicato chiaramente nel pannello. Ogni salvataggio e' tracciato con `AI_SETTINGS_APPLY`

## [2026-07-03] Versione 3.0.4.2 - Sicurezza e audit OPC UA / credenziali

Pacchetto sicurezza a basso impatto runtime: non cambia il ciclo macchina, ma rende configurabili
i comandi remoti OPC UA, aumenta l'audit delle azioni sensibili e segnala credenziali/default TLS
prima della messa in rete.

**File modificati:**
- `Cls_Config/Calss_structure/OpcUaConfig.cs` - default `AutoAcceptUntrustedCertificates=false` per nuove configurazioni; aggiunti flag `RemoteCommandsEnabled`, `RemoteStartStopEnabled`, `RemoteRecipeChangeEnabled`, `AuditRemoteCommandsToDatabase`
- `Database/OpcUaConfigurationRepository.cs` - persistenza DB dei nuovi setting OPC UA; connection string allineata a `SslDisabled`
- `Services/OpcUaClientService.cs` - policy sui comandi remoti Start/Stop/Ricetta; comando disabilitato rifiutato con ACK negativo e log/audit
- `Views/UserControls/OpcUaConfigurationView.xaml` + `ViewModels/OpcUaConfigurationViewModel.cs` - nuova sezione UI per abilitare/disabilitare comandi remoti e audit OPC UA
- `Database/Cls_CheckUser.cs` - seed utenti di default solo su primo bootstrap; warning/audit su credenziali default presenti o usate; audit create/delete utente
- `MainWindow.xaml.cs` - warning/audit non bloccanti su MySQL `root/root` e TLS disabilitato verso host non locale
- `Services/AuditLogService.cs`, `Database/clsConnection.cs`, `ViewModels/PreferenceViewModel.cs` - connection string centralizzata e rispetto di `SslDisabled`
- `Services/IoTimingOptimizerService.cs` - `IO_TIMING_APPLY` scritto anche in `audit_log`
- `Localization/messages_eng.json`, `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - label OPC UA aggiornate
- `Properties/AssemblyInfo.cs` - release `3.0.4.1` -> `3.0.4.2`

**Motivo:** prima di esporre OPC UA/DB in rete reale, Start/Stop/cambio ricetta da server esterno
devono essere esplicitamente governabili e tracciati. Le credenziali default devono restare visibili
come rischio operativo senza bloccare la macchina durante collaudo o recovery.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml / `cfg_opcua_configuration`: nuovi setting opzionali, compatibili con configurazioni esistenti; default remoti abilitati per non rompere impianti gia' in uso, ma ora disattivabili da HMI/DB
- Database: nessuna nuova tabella/colonna; nuove righe in `audit_log` e setting key/value in `cfg_opcua_configuration`
- Runtime: RunContinuous e ciclo ispezione invariati; un comando OPC UA disabilitato viene respinto con ACK negativo invece di essere eseguito
- Operativita': pagina OPC UA permette di separare "OPC connesso per dati" da "OPC autorizzato a comandare macchina"

## [2026-07-03] Versione 3.0.4.1 - MultiShot: soglia "stesso-prodotto" separata dal timeout

Rifinitura del fix MultiShot di 3.0.4.0 su consiglio di revisione: **non usare la stessa soglia**
per "quanto attendo il risultato" e per "quanto considero ancora lo stesso prodotto". In 3.0.4.0
la soglia di accettazione companion era stata allineata al timeout (`max(5000, timeout)`), che
risolveva il sintomo ma fondeva i due concetti (il lag "stesso-prodotto" diventava 30s in MultiShot).

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs` — nuovo `RuntimeBindings.MultiShotCompanionMaxLagMs` (default 15000ms): lag FISICO plausibile della scansione MultiShot, distinto dal timeout di attesa (30s, tetto di sicurezza)
- `ConfigurationTemplates/machine_runtime_config.template.xml` + `Services/MachineConfigurationService.cs` — nuova chiave `<MultiShotCompanionMaxLagMs>`, marker di schema e clamp [1000, 120000]
- `MainWindow.xaml.cs` — cache `_multiShotCompanionMaxLagMs` (letta una volta in `RefreshMultiShotConfiguredState`, nessuna lettura config per-prodotto); nuovo provider `GetCompanionSameProductLagMs()` (MultiShot → valore config; single-shot → 5s costante); iniettato nel costruttore dell'orchestrator
- `Services/InspectionOrchestrator.cs` — nuovo parametro `Func<double> getCompanionSameProductLagMs`; la soglia "stesso-prodotto" ora proviene dal provider (fisica) invece che da `max(5000, timeout)`. `MaxCompanionLagMs` (5s) resta solo come floor/fallback single-shot
- `Properties/AssemblyInfo.cs` — release `3.0.4.0` → `3.0.4.1`

**Motivo:** separare i due concetti (attesa vs identita'-prodotto) come da revisione. Il timeout MultiShot (30s) e' un tetto di sicurezza generoso; il lag "stesso-prodotto" deve riflettere la durata fisica della cattura (~15s di default, configurabile), evitando sia il falso `missing` sia il rischio teorico di mis-pairing cross-prodotto quando i prodotti si susseguono piu' veloci del timeout.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovo campo `MultiShotCompanionMaxLagMs` (default 15000); i file esistenti ricevono il default via upgrade schema; letto una volta all'avvio (cambiarlo richiede riavvio)
- Database: nessuna modifica
- API pubblica: nessuna (firma interna dell'orchestrator `internal` estesa)
- Comportamento runtime: identita'-prodotto MultiShot ora governata da una soglia fisica dedicata, non dal timeout; single-shot invariato (5s). Operatore invariato

## [2026-07-03] Versione 3.0.4.0 - Pacchetto affidabilita' runtime

Correzioni di affidabilita' vicine al ciclo reale di ispezione, dalla revisione architetturale.
Nessun cambiamento nel modo in cui l'operatore usa la macchina.

**File modificati:**
- `Services/InspectionOrchestrator.cs` (#3 — MultiShot) — la finestra di accettazione dei companion (`MaxCompanionLagMs`, fissa a 5s) e' ora **allineata al timeout di attesa**: `maxLagMs = max(5000ms, timeout)`. Prima un companion MultiShot legittimo (scansione a 9 scatti, che puo' richiedere >5s) veniva atteso fino a 30s ma poi scartato come "future product" e classificato `missing`. Il floor di 5s preserva la protezione cross-prodotto del single-shot. Firma di `WaitForExpectedCompanionResultsAsync`/`RoleQueueHasValidResult` estesa con `maxLagMs`.
- `Services/OnnxDefectClassifier.cs` (#4 — AI off-thread) — `RunShadowComparison` non esegue piu' l'inferenza (decode immagine + ONNX) sul thread di ispezione: cattura il solo bool necessario e lancia il lavoro pesante **off-thread** con `Task.Run(...).SafeFireAndForget`. Aggiunto un **single-flight gate** (`SemaphoreSlim`): se un confronto shadow e' gia' in corso, il campione viene saltato (advisory, evita accumulo di task se il modello e' lento). Gate rilasciato in `finally` e disposto in `Dispose`.
- `Database/CounterManager.cs` (#5) — `Dispose` non e' piu' `async void`: il flush finale dei contatori e' atteso in modo **sincrono con timeout 5s** (`Task.Run(FlushNowAsync).Wait(5s)`, stacca da SynchronizationContext per evitare deadlock), preceduto dal backup su file. Aggiunto `_flusher.Dispose()` (prima il timer del flusher restava attivo).
- `Services/MachineConfigurationService.cs` (#6) — `Save` ora e' **atomico** (serializza su file temporaneo poi `File.Replace`) e **serializzato con lock**: elimina il rischio di `machine_runtime_config.xml` troncato/corrotto per interruzione a meta' scrittura o save concorrenti.
- `Properties/AssemblyInfo.cs` — release `3.0.3.9` → `3.0.4.0`

**Motivo:** priorita' ai rischi piu' vicini al ciclo di ispezione (MultiShot e inferenza AI sul thread real-time), piu' due robustezze su shutdown contatori e scrittura config.

**Impatto:**
- Config.xml / DB: nessuna modifica di schema
- API pubblica: nessun cambiamento (firme interne di `InspectionOrchestrator`, classe `internal`, estese)
- Comportamento runtime: **#3** i companion MultiShot lenti non vengono piu' erroneamente marcati `missing`; **#4** l'inferenza AI (quando abilitata) non rallenta piu' il thread di ispezione; **#5** i contatori vengono flushati in modo affidabile allo shutdown senza crash da eccezione non osservata; **#6** la config runtime non puo' piu' corrompersi durante il salvataggio
- Nessun cambiamento nell'uso operatore

**Nota (cleanup futuro, non incluso):** `Cls_InitializzeDb.Initialize1()` e' codice morto (`async void`, mai invocato, crea `tblgenerale` senza indici). Zero rischio runtime perche' non chiamato; rimozione rimandata per evitare un edit ampio e rischioso in un file DB ad alto impatto in questo pacchetto.

## [2026-07-03] Versione 3.0.3.9 - AI performance monitor e advisor prodotto/ricetta

**File aggiunti:**
- `Services/AiPerformanceMonitorService.cs` - monitor in-memory dei tempi del ciclo ispezione post-acquisizione; produce advisory throttled `AI_PERFORMANCE_ADVISORY`
- `Services/RecipeProductAdvisorService.cs` - advisor in-memory per sospetta incoerenza prodotto/ricetta; produce `AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED` e, se possibile, suggerisce una ricetta candidata senza applicarla

**File modificati:**
- `MainWindow.xaml.cs` - misurazione `Stopwatch` attorno a display, validazione, contatori/allarmi/OPC UA, salvataggio decisione, advisory AI e queue DB; aggancio dei nuovi servizi dopo la finalizzazione ispezione
- `Models/MachineRuntimeConfiguration.cs` - nuovi flag `AiPerformanceMonitorEnabled` e `RecipeProductAdvisorEnabled`, entrambi default `false`
- `ConfigurationTemplates/machine_runtime_config.template.xml` - template aggiornato con i nuovi flag disabilitati
- `Services/MachineConfigurationService.cs` - marker schema runtime aggiornati per i nuovi campi
- `ServiceLocator.cs` - registrazione singleton dei due servizi e reset coerente
- `QtisVisionPanel.csproj` - inclusi i nuovi file servizio
- `Documentation/AI/AI_Integration_Architecture.md`, `Documentation/MachineHardware/architecture-and-modules.md`, `Documentation/MachineHardware/software-version-archive.md` - documentazione aggiornata
- `Properties/AssemblyInfo.cs` - release `3.0.3.8` -> `3.0.3.9`

**Motivo:** usare l'AI per migliorare diagnosi e performance senza inserirla nel percorso decisionale macchina. Il monitor identifica colli di bottiglia; l'advisor segnala possibili ricette/formati incoerenti prima che l'autoswitch arrivi alla soglia, ma senza comandare cambi automatici.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: due nuovi campi opzionali sotto `RuntimeBindings`, default `false`
- Database: nessuna nuova tabella/colonna; solo event log advisory se i flag sono abilitati
- Runtime: nessuna modifica a esito, scarto, contatori, uscite fisiche, RunContinuous o cambio ricetta; servizi in-memory e advisory

## [2026-07-02] Versione 3.0.3.8 - Hardening AI advisory, retention dati e default conservativi

**File aggiunti:**
- `Database/AiDataRepositorySupport.cs` - helper interno per repository AI: connection string MySQL condivisa, warning throttled, retention periodica

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs` - default AI conservativi (`false` per Fase 0/1/2/3a/5), nuovi campi retention AI e metadati ONNX modello
- `ConfigurationTemplates/machine_runtime_config.template.xml` - template allineato ai nuovi default e ai nuovi campi retention/metadati
- `Services/MachineConfigurationService.cs` - marker schema aggiornati per retention e metadati modello
- `Database/InspectionMeasurementRepository.cs`, `Database/HealthSnapshotRepository.cs`, `Database/TrainingSampleRepository.cs` - warning DB throttled e retention automatica non bloccante
- `Services/MeasurementCaptureService.cs`, `Services/HealthSnapshotService.cs`, `Services/ProcessControlService.cs`, `Services/PredictiveMaintenanceService.cs`, `Services/TrainingDataCollectionService.cs`, `Services/IoTimingOptimizerService.cs` - fail-safe: se la config non e' leggibile i servizi AI restano disabilitati
- `Services/ProcessControlService.cs` - media/sigma calcolate sulla finestra precedente al campione corrente
- `Services/PredictiveMaintenanceService.cs` - valutazione memoria basata sul conteggio campioni memoria
- `Services/TrainingDataCollectionService.cs` - `label.json` serializzato con Newtonsoft.Json e arricchito con `labelSource` + versione software
- `Services/OnnxDefectClassifier.cs` - metadati modello, log `VISION_ML_SHADOW_DISAGREE` in EventLog solo sui disaccordi shadow-mode
- `Services/IoTimingOptimizerService.cs` - backup reale prima del salvataggio parametri e messaggio piu' esplicito sul refresh/reload runtime
- `Documentation/AI/AI_Integration_Architecture.md` - riscritto come stato reale dell'implementazione AI
- `Documentation/MachineHardware/architecture-and-modules.md`, `Documentation/MachineHardware/software-version-archive.md` - documentazione release aggiornata
- `Properties/AssemblyInfo.cs` - release `3.0.3.7` -> `3.0.3.8`

**Motivo:** le funzioni AI erano gia' additive, ma per una baseline macchina matura non devono attivarsi per default o per errore di lettura config. Inoltre le nuove tabelle devono avere una strategia minima di retention e gli errori DB AI devono essere visibili senza generare rumore continuo.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovi campi opzionali `DefectClassifierModelVersion`, `DefectClassifierModelNotes`, `AiInspectionMeasurementRetentionDays`, `AiHealthSnapshotRetentionDays`, `AiTrainingSampleRetentionDays`; i file esistenti mantengono i flag gia' salvati
- Database: nessuna nuova tabella; retention sulle tabelle AI esistenti, tentata al massimo ogni 12 ore
- Runtime: AI resta advisory e non modifica esito, scarto, contatori, pairing VisionPro o uscite fisiche

## [2026-07-02] Versione 3.0.3.7 - Fix binding read-only sezione timing (Fase 5 UI)

**File modificati:**
- `ViewModels/SystemDiagnosticsViewModel.cs` — aggiunta proprieta' di sola lettura `TimingParameterItem.CurrentSummary` (notificata quando cambia `CurrentValue`)
- `Views/UserControls/SystemDiagnosticsView.xaml` — sostituiti i `<Run Text="{Binding ...}">` con un unico `TextBlock Text="{Binding CurrentSummary}"`
- `Properties/AssemblyInfo.cs` — release `3.0.3.6` → `3.0.3.7`

**Motivo:** all'apertura di PC Diagnostics veniva lanciata `XamlParseException / InvalidOperationException`: il binding su `Run.Text` viene trattato come TwoWay da WPF e la proprieta' sorgente `RangeText` (e in generale i Run bindings) e' di sola lettura. Un `TextBlock.Text` ha binding OneWay di default e non genera l'errore.

**Impatto:**
- Config.xml / DB: nessuno
- Comportamento runtime: la vista PC Diagnostics si apre correttamente; la sezione timing mostra "attuale N ms · range min-max" senza eccezioni. Nessun'altra modifica funzionale

## [2026-07-02] Versione 3.0.3.6 - UI conferma Fase 5 in SystemDiagnostics

**File modificati:**
- `ViewModels/SystemDiagnosticsViewModel.cs` — aggiunta sezione timing optimizer: collezioni `TimingAdvisories`/`TimingParameters`, `CanApplyTimingParameters` (gate ruolo), comandi `RefreshTimingCommand`/`ApplyTimingParameterCommand`, metodi `RefreshTiming`/`ApplyTimingParameter` (conferma via `SystemNotificationWindow`, apply via `IoTimingOptimizer.ApplyTimingParameter`); nuove classi `TimingAdvisoryItem` e `TimingParameterItem`
- `Views/UserControls/SystemDiagnosticsView.xaml` — nuova sezione "Ottimizzazione timing IO/encoder (Fase 5)" (Grid.Row 5): lista raccomandazioni advisory + parametri applicabili con valore corrente, range, target editabile e bottone "Applica"
- `Properties/AssemblyInfo.cs` — release `3.0.3.5` → `3.0.3.6`

**Motivo:** completare la Fase 5 con la superficie UI di conferma operatore, integrata nella vista SystemDiagnostics come richiesto. Il metodo di apply guardrailato esisteva gia' (3.0.3.5); qui si aggiunge il modo per invocarlo in sicurezza.

**Impatto:**
- Config.xml / machine_runtime_config.xml: nessuno
- Database: nessuno
- API pubblica: `SystemDiagnosticsViewModel` espone le nuove collezioni/comandi
- Comportamento runtime: la vista PC Diagnostics mostra ora le raccomandazioni di timing e permette l'applicazione dei parametri trigger **solo** con ruolo Installer/Administrator e **conferma esplicita** (dialog). Ogni apply passa dal percorso guardrailato (whitelist + range + backup + audit) della Fase 5. Nessuna modifica automatica

## [2026-07-02] Versione 3.0.3.5 - Ottimizzatore timing IO/encoder (Fase 5)

**File aggiunti:**
- `Models/IoTimingRecommendation.cs` — DTO raccomandazione/diagnostica di timing
- `Services/IoTimingOptimizerService.cs` — analisi in-memory del lag risultati companion per ruolo camera (percentili, jitter, missing-rate), advisory su margine timeout, e `ApplyTimingParameter` guardrailato (whitelist + range + backup + audit)

**File modificati:**
- `MainWindow.xaml.cs` — in `ProcessInspectionGroupAsync`, unica riga che alimenta `ObserveInspectionTiming(topResult, companions, missingRoles)` in-memory (nessuna modifica al codice real-time InspectionOrchestrator/MultiShot)
- `Models/MachineRuntimeConfiguration.cs` — nuovo flag `RuntimeBindings.IoTimingOptimizerEnabled` (default `true`)
- `Services/MachineConfigurationService.cs` — aggiunto `<IoTimingOptimizerEnabled>` ai marker di schema
- `ConfigurationTemplates/machine_runtime_config.template.xml` — aggiunta chiave `<IoTimingOptimizerEnabled>true</IoTimingOptimizerEnabled>`
- `ServiceLocator.cs` — registrato `IoTimingOptimizer` (singleton double-check), creato in `Initialize()`, ripulito in `Reset()`
- `Properties/AssemblyInfo.cs` — release `3.0.3.4` → `3.0.3.5`

**Motivo:** Fase 5 del piano AI. La gestione IO/encoder scala col numero di camere del progetto VisionPro: questo servizio ne osserva il comportamento temporale per ruolo e propone ottimizzazioni. Tocca il loop di controllo, quindi rispetta il confine di sicurezza: **analisi/advisory automatiche, applicazione solo su conferma esplicita**.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovo campo `IoTimingOptimizerEnabled` (default `true`); l'apply scrive i parametri trigger di `RuntimeBindings` solo tramite `ApplyTimingParameter` (mai in automatico)
- Database: nessuna nuova tabella; advisory loggate in `tbllogevent` (codice `IO_TIMING_ADVISORY`), apply audit come `IO_TIMING_APPLY`
- API pubblica: aggiunto `ServiceLocator.IoTimingOptimizer` con `GetRecommendations()`, `ApplyTimingParameter(param, value, actor)`, `ApplicableParameters`
- Comportamento runtime: l'analisi e' advisory e non bloccante (osservazione in-memory per gruppo ispezione). **Nessuna modifica automatica del loop di controllo**: l'apply e' guardrailato (whitelist parametri, range di sicurezza, backup del valore precedente, audit) e va invocato da un'azione utente esplicita e autorizzata
- Nota: i timeout risultato (`VisionResultTimeout`/`VisionMultiShotResultTimeout`) sono costanti di codice in MainWindow → per essi la Fase 5 produce solo advisory sul margine, non li applica

**Da completare (fuori da questa release):** la UI di conferma operatore che invoca `ApplyTimingParameter` (bottone "Applica" con controllo ruolo autorizzato). Il metodo di apply e i suoi guardrail sono pronti; manca solo la superficie UI.

## [2026-07-02] Versione 3.0.3.4 - Classificatore difetti ONNX in shadow-mode (Fase 3)

**File aggiunti:**
- `DataManage/IDefectClassifier.cs` — interfaccia + `DefectClassificationResult`
- `Services/OnnxDefectClassifier.cs` — inferenza ONNX Runtime: caricamento modello `.onnx`, preprocessing configurabile (dimensione/grayscale/normalizzazione), classificazione, e `RunShadowComparison` che confronta la predizione col risultato a regole

**File modificati:**
- `QtisVisionPanel.csproj` — aggiunto `PackageReference Microsoft.ML.OnnxRuntime 1.19.2` (dipendenza nativa x64); compile entries per i due nuovi file
- `Models/MachineRuntimeConfiguration.cs` — nuovi campi `DefectClassifier*` (Enabled default `false`, ModelPath, InputWidth/Height, Grayscale, NormalizeMean/Std)
- `ConfigurationTemplates/machine_runtime_config.template.xml` + `Services/MachineConfigurationService.cs` — nuove chiavi e marker schema
- `ServiceLocator.cs` — registrato `DefectClassifier` (singleton, `IDisposable` → dispose in `Reset()`)
- `MainWindow.xaml.cs` — in `ProcessInspectionGroupAsync`, unica riga di confronto shadow-mode dopo la cattura training
- `Properties/AssemblyInfo.cs` — release `3.0.3.3` → `3.0.3.4`

**Motivo:** Fase 3 del piano AI. Predispone l'inferenza ML sulle immagini in **shadow-mode**: il modello gira in parallelo a VisionPro, la sua predizione viene confrontata e loggata (accordo/disaccordo) senza incidere sullo scarto, in attesa che dimostri affidabilita' sul campo prima di un'eventuale promozione.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovi campi opzionali `DefectClassifier*` (default disabilitato); letti una volta all'avvio
- Database: nessuna nuova tabella
- Dipendenze: **nuova dipendenza nativa** `Microsoft.ML.OnnxRuntime` (DLL x64 copiata in output)
- API pubblica: aggiunto `ServiceLocator.DefectClassifier` (`IDefectClassifier`)
- Comportamento runtime: **disabilitato di default** (nessun modello → no-op, zero overhead). Se abilitato con un modello valido, gira in shadow-mode advisory (log `VISION_ML_SHADOW`), senza alcun effetto su ispezione/scarto/contatori
- **Assunzioni da riadattare al modello reale:** layout tensore NCHW, classe 0 = pezzo conforme. Il preprocessing (dimensione/grayscale/normalizzazione) e' configurabile

## [2026-07-02] Versione 3.0.3.3 - Raccolta dati etichettati per training vision (Fase 3a)

**File aggiunti:**
- `Models/TrainingSampleEntry.cs` — POCO campione etichettato (esito + difetti + cartella immagini)
- `Database/TrainingSampleRepository.cs` — indice queryable in `tbl_training_samples` (pattern EventLogRepository)
- `Services/TrainingDataCollectionService.cs` — a ogni ispezione con immagini salvate, scrive un sidecar `label.json` nella cartella del pezzo e una riga indice su DB

**File modificati:**
- `Database/Cls_InitializzeDb.cs` — aggiunto `CreateTrainingSamplesTable`, invocato in `Initialize()`
- `MainWindow.xaml.cs` — in `ProcessInspectionGroupAsync`, dopo il salvataggio immagini, unica riga di cattura campione etichettato (solo se le immagini del pezzo sono state salvate)
- `Models/MachineRuntimeConfiguration.cs` — nuovo flag `RuntimeBindings.TrainingDataCollectionEnabled` (default `true`)
- `Services/MachineConfigurationService.cs` — aggiunto `<TrainingDataCollectionEnabled>` ai marker di schema
- `ConfigurationTemplates/machine_runtime_config.template.xml` — aggiunta chiave `<TrainingDataCollectionEnabled>true</TrainingDataCollectionEnabled>`
- `ServiceLocator.cs` — registrato `TrainingDataCollectionService` (singleton double-check), creato in `Initialize()`, ripulito in `Reset()`
- `Properties/AssemblyInfo.cs` — release `3.0.3.2` → `3.0.3.3`

**Motivo:** Fase 3a del piano AI. Il classificatore vision (Fase 3 completa) richiede un modello addestrato su immagini etichettate: questo passo accumula il dataset nel tempo, agganciando l'etichetta (OK/NOK + difetti) alle immagini gia' salvate dal flusso esistente, senza addestrare nulla.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovo campo opzionale `TrainingDataCollectionEnabled` (default `true`); letto una volta all'avvio
- Database: **nuova tabella** `tbl_training_samples` (create automatica con `CREATE TABLE IF NOT EXISTS`)
- Filesystem: aggiunge un piccolo `label.json` in ogni cartella `Piece_XXXXXXXX` quando le immagini del pezzo vengono salvate (il salvataggio immagini resta invariato, a percentuale)
- API pubblica: aggiunto `ServiceLocator.TrainingDataCollectionService`
- Comportamento runtime: **additivo e non bloccante**. Indicizza solo cio' che il flusso immagini gia' salva; degrada silenziosamente se DB/disco non disponibili. Nessun impatto su ispezione/scarto/contatori

## [2026-07-02] Versione 3.0.3.2 - Manutenzione predittiva (Fase 2)

**File aggiunti:**
- `Services/PredictiveMaintenanceService.cs` — analisi statistica in-memory degli snapshot salute PC: proiezione lineare riempimento disco (ore-al-limite), EWMA per CPU/RAM sostenute sopra soglia. Espone evento `MaintenancePredicted` e `GetActiveAlerts()`

**File modificati:**
- `Services/HealthSnapshotService.cs` — dopo aver costruito lo snapshot, alimenta `PredictiveMaintenanceService.Observe(entry)` in-memory prima della scrittura DB
- `Models/MachineRuntimeConfiguration.cs` — nuovo flag `RuntimeBindings.PredictiveMaintenanceEnabled` (default `true`)
- `Services/MachineConfigurationService.cs` — aggiunto `<PredictiveMaintenanceEnabled>` ai marker di schema
- `ConfigurationTemplates/machine_runtime_config.template.xml` — aggiunta chiave `<PredictiveMaintenanceEnabled>true</PredictiveMaintenanceEnabled>`
- `ServiceLocator.cs` — registrato `PredictiveMaintenanceService` (singleton double-check), creato in `Initialize()`, ripulito in `Reset()`
- `Properties/AssemblyInfo.cs` — release `3.0.3.1` → `3.0.3.2`

**Motivo:** Fase 2 del piano AI (`Documentation/AI/AI_Integration_Architecture.md`). Usa la serie storica salute PC (Fase 0) per anticipare i degradi hardware — soprattutto il riempimento del disco — con statistica trasparente, prima che diventino guasti bloccanti.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovo campo opzionale `PredictiveMaintenanceEnabled` (default `true`); letto una volta all'avvio
- Database: nessuna nuova tabella; gli avvisi sono loggati in `tbllogevent` a livello Warning (codice `PREDICTIVE_MAINTENANCE`)
- API pubblica: aggiunto `ServiceLocator.PredictiveMaintenanceService` con evento `MaintenancePredicted` e `GetActiveAlerts()`
- Comportamento runtime: **advisory e non bloccante**. Gli avvisi compaiono nell'Events Monitor come Warning (non attivano il badge errori). Analisi in-memory ad ogni snapshot salute (~60s). Nessun impatto su ispezione/scarto/contatori

## [2026-07-02] Versione 3.0.3.1 - Controllo statistico di processo e rilevamento deriva (Fase 1)

**File aggiunti:**
- `Services/ProcessControlService.cs` — SPC in-memory: finestra mobile per chiave (ricetta|camera|feature), limiti di controllo I-MR (media +/- 3 sigma da moving range), sottoinsieme regole di Nelson + carta EWMA, stima lineare dei "pezzi al limite". Espone evento `DriftDetected` e `GetActiveDrifts()`

**File modificati:**
- `Services/MeasurementCaptureService.cs` — dopo aver costruito le misure di un'ispezione, alimenta `ProcessControlService.ObserveBatch(entries)` in-memory (prima della scrittura DB, sincrono, senza rileggere il database)
- `Models/MachineRuntimeConfiguration.cs` — nuovo flag `RuntimeBindings.ProcessControlEnabled` (default `true`)
- `Services/MachineConfigurationService.cs` — aggiunto `<ProcessControlEnabled>` ai marker di schema
- `ConfigurationTemplates/machine_runtime_config.template.xml` — aggiunta chiave `<ProcessControlEnabled>true</ProcessControlEnabled>`
- `ServiceLocator.cs` — registrato `ProcessControlService` (singleton double-check), creato in `Initialize()`, ripulito in `Reset()`
- `Properties/AssemblyInfo.cs` — release `3.0.3.0` → `3.0.3.1`

**Motivo:** Fase 1 del piano AI (`Documentation/AI/AI_Integration_Architecture.md`). Usando le misure strutturate della Fase 0, rileva le derive di processo (deriva media, trend, shift) *prima* che diventino scarto, con statistica trasparente (nessuna scatola nera). I limiti di controllo sono derivati dal processo stesso (non dalle tolleranze di specifica), quindi il metodo e' robusto e indipendente dalla semantica di ogni feature.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovo campo opzionale `ProcessControlEnabled` (default `true`); letto una volta all'avvio
- Database: nessuna nuova tabella; le derive sono loggate in `tbllogevent` (via `ApplicationEventLogger`) a livello Warning
- API pubblica: aggiunto `ServiceLocator.ProcessControlService` con evento `DriftDetected` e `GetActiveDrifts()`
- Comportamento runtime: **advisory e non bloccante**. Le derive compaiono nell'Events Monitor come Warning (NON attivano il badge errori, che resta sui soli Critical). Nessun impatto su ispezione, scarto o contatori. L'analisi e' in-memory per prodotto (nessuna lettura DB nel percorso caldo)

## [2026-07-02] Versione 3.0.3.0 - Fondazione dati AI (Fase 0)

**File aggiunti:**
- `Models/InspectionMeasurementEntry.cs` — POCO: misura strutturata di ispezione (valore, tolleranza, esito) per feature/camera/prodotto
- `Models/HealthSnapshotEntry.cs` — POCO: snapshot salute PC (CPU/RAM/disco)
- `Database/InspectionMeasurementRepository.cs` — scrittura batch in `tbl_inspection_measurements` (pattern EventLogRepository: semaforo, async/sync, degradazione silenziosa)
- `Database/HealthSnapshotRepository.cs` — scrittura in `tbl_health_snapshots`
- `Services/MeasurementCaptureService.cs` — estrae le misure dai `ValidationResult` di un'ispezione e le persiste in modo non bloccante; gated dal flag di config
- `Services/HealthSnapshotService.cs` — timer (60s, primo tick +45s) che legge `SystemDiagnosticsService` e scrive uno snapshot salute; gated dal flag di config

**File modificati:**
- `Database/Cls_InitializzeDb.cs` — aggiunti `CreateInspectionMeasurementsTable` e `CreateHealthSnapshotsTable`, invocati in `Initialize()`
- `Models/MachineRuntimeConfiguration.cs` — nuovo flag `RuntimeBindings.DataFoundationCaptureEnabled` (default `true`)
- `Services/MachineConfigurationService.cs` — aggiunto `<DataFoundationCaptureEnabled>` ai marker di schema runtime
- `ConfigurationTemplates/machine_runtime_config.template.xml` — aggiunta chiave `<DataFoundationCaptureEnabled>true</DataFoundationCaptureEnabled>`
- `ServiceLocator.cs` — registrati `MeasurementCaptureService` e `HealthSnapshotService` (singleton double-check), creati/avviati in `Initialize()`, ripuliti in `Reset()`
- `MainWindow.xaml.cs` — in `ProcessInspectionGroupAsync`, unica riga di cattura misure dopo `RegisterProcessedInspectionPair()`
- `Properties/AssemblyInfo.cs` — release `3.0.2.4` → `3.0.3.0`

**Motivo:** primo passo (Fase 0) del piano di integrazione AI (`Documentation/AI/AI_Integration_Architecture.md`). I valori misurati per prodotto oggi vivevano solo dentro stringhe di log non analizzabili. Strutturarli in due tabelle abilita i livelli successivi (SPC/rilevamento deriva, manutenzione predittiva) senza toccare la logica di ispezione.

**Impatto:**
- Config.xml: nessuno
- machine_runtime_config.xml: nuovo campo opzionale `DataFoundationCaptureEnabled` (default `true`; i file esistenti privi del campo restano abilitati grazie al default del modello). Il flag e' letto una volta all'avvio dai servizi: cambiarlo richiede il riavvio dell'applicazione
- Database: **due nuove tabelle** `tbl_inspection_measurements` e `tbl_health_snapshots` (create automaticamente all'avvio con `CREATE TABLE IF NOT EXISTS`). Nessuna modifica alle tabelle esistenti
- API pubblica: aggiunti `ServiceLocator.MeasurementCaptureService` e `ServiceLocator.HealthSnapshotService`
- Comportamento runtime: additivo e non bloccante. La cattura misure e' fire-and-forget dopo ogni ispezione; lo snapshot salute e' scritto ogni 60s. Se MySQL non e' disponibile, entrambi degradano silenziosamente senza impattare produzione, scarto o contatori

## [2026-07-01] Badge TopMenuBar: notificare solo gli errori

**File modificati:**
- `Database/EventLogRepository.cs` — `GetAlertSummaryCore()` ora filtra gli eventi Error/Critical alle ultime 24 ore (`timestamp >= @Cutoff`), sia per il conteggio sia per l'ultimo allarme mostrato
- `ViewModels/TopMenuBarViewModel.cs` — `DiagnosticsAlertCount` e `HasDiagnosticsAlerts` usano `CriticalAlertCount` invece di `ActiveAlertCount`/`HasActiveAlerts`

**Motivo:** i due badge nella barra di stato in alto non riflettevano "solo gli errori":
- Il badge allarmi (rosso) sommava tutti gli eventi Error/Critical dell'intero storico `tbllogevent` (es. 57), non gli errori attuali. Ora conta solo gli errori delle ultime 24 ore.
- Il badge diagnostica (icona monitor) sommava sia i Warning sia i Critical della diagnostica PC (es. 4). Ora mostra solo i Critical, così i warning (RAM/CPU sopra soglia) non generano più notifica.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna modifica di schema; la query di riepilogo allarmi ora è vincolata alle ultime 24 ore
- API pubblica: nessuna variazione di firma
- Comportamento runtime: il badge rosso riflette gli errori delle ultime 24h; il badge monitor notifica solo diagnostiche Critical. Le viste di dettaglio (Events Monitor, SystemDiagnostics) restano invariate

## [2026-07-01] Versione 3.0.2.4 - Contatori difetti: incremento singolo per ispezione

**File modificati:**
- `Database/CounterManager.cs` - `ProcessInspectionAsync` aggiorna solo `Total`, `Good` e `NoGood`; rimosso il secondo incremento dei contatori per-difetto da `UpdateDefectCounters(defects)`; aggiunta deduplica per ciclo con `_defectsCountedThisCycle`
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.2.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.2.4`
- `Documentation/MachineHardware/counter-defect-single-source-2026-07-01.md` - documentato il flusso autorevole dei contatori produzione/difetto
- `Docs/Manual/00_Documentazione_Tecnica_Globale.md`, `Docs/Manual/01_Panoramica_Pannello.md` - note operatore aggiornate sui contatori

**Motivo:** ogni difetto veniva conteggiato due volte. Path 1: `DataManage/IToolBlockValidator.cs` chiama `CounterManager.IncrementDefectAsync()` direttamente durante la validazione di ogni prodotto. Path 2 (rimosso): al termine della validazione, `MainWindow.ProcessInspectionWithCountersAsync()` chiamava `CounterManager.ProcessInspectionAsync(isCompliant, defects)`, che a sua volta invocava il metodo privato `UpdateDefectCounters(defects)` incrementando di nuovo gli stessi campi (`LogoDefects++`, `PrintCenteringDefects++`, `HeightDefects++`, `ShapeTopDefects++`, `ShapeSideDefects++`, ecc.). Il path 1 resta quello autorevole per i contatori difetto perche i validator conoscono il controllo realmente fallito; `ProcessInspectionAsync` resta responsabile solo dei contatori pezzo. In piu, se nello stesso pezzo due controlli della stessa famiglia difetto chiamano `IncrementDefectAsync` con la stessa chiave normalizzata, il contatore numerico aumenta una sola volta e viene aggiornato solo il messaggio operatore.

**Impatto:**
- Config.xml: nessuno
- Machine runtime config: nessuna modifica
- Database: nessuna nuova tabella o colonna; i contatori storici per-difetto gia salvati possono essere gonfiati del doppio e vanno azzerati manualmente se serve una ripartenza pulita
- API: `CounterManager.ProcessInspectionAsync` mantiene la firma esistente; il parametro `defects` resta opzionale per compatibilita ma non incrementa piu i contatori difetto
- Runtime produzione: contatori `Total`, `Good`, `NoGood` invariati; ogni contatore per-difetto ora aumenta al massimo una volta per ciclo pezzo

## [2026-07-01] Versione 3.0.2.3 - Ricette 15 pollici adattive e lettura esposizione live

**File modificati:**
- `Views/RecipeManagerView.xaml` - colonne principali rese proporzionali con limiti min/max; ridotti padding, margini, card ricette, preview immagine e vincoli rigidi per adattarsi meglio a display da 15"
- `Views/JobToolEditorView.xaml` - toolbar comandi convertita a `WrapPanel`; aggiunto pulsante `Read` e testo `Current: ... us` per esposizione corrente
- `Views/JobToolEditorView.xaml.cs` - label `Read` caricata dal server messaggi
- `ViewModels/JobToolEditorViewModel.cs` - aggiunti `RefreshLivePreviewExposureCommand`, `LivePreviewCurrentExposureText` e lettura/esposizione corrente separata dal valore da applicare
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_eng.json`, `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - nuove label EN/IT per lettura esposizione e stati applicazione
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.2.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.2.3`

**Motivo:** su un pannello 15" la pagina ricette risultava troppo compressa perche usava larghezze fisse e card con vincoli rigidi. In Live Preview l'operatore aveva bisogno di leggere il valore esposizione corrente della camera prima di modificarlo, senza procedere a tentativi.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna nuova chiave; resta valido `LivePreviewExposureUs`
- Database: nessuna modifica
- Runtime produzione: invariato
- UI: Recipe Management piu adattiva su display compatti; Job Tool Editor piu leggibile e con readback esposizione camera

## [2026-07-01] Versione 3.0.2.2 - Live Preview: record display produzione ed esposizione camera

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs` - la finestra Live Preview risolve il record immagine usando la stessa chiave `Config.xml -> LasRunParam` gia usata dai display produzione; fallback su record completo e primo `ICogImage` trovato
- `ViewModels/JobToolEditorViewModel.cs` - aggiunti lettura/applicazione esposizione camera in microsecondi e persistenza del valore in `machine_runtime_config.xml`
- `Cls_Vpro/IgigaCameraAccess.cs` - aggiunti helper `ReadExposureTimeUs` / `ApplyExposureTimeUs` su `OwnedGigEAccess`, con tentativo sui nodi `ExposureTime`, `ExposureTimeAbs`, `ExposureTimeRaw`
- `Views/JobToolEditorView.xaml` e `.xaml.cs` - aggiunti campo esposizione, pulsante `Apply exposure` e stato operazione nella toolbar del Job Editor
- `Models/MachineRuntimeConfiguration.cs`, `Services/MachineConfigurationService.cs`, `ConfigurationTemplates/machine_runtime_config.template.xml` - aggiunta chiave opzionale `RuntimeBindings.LivePreviewExposureUs`
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_eng.json`, `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - label EN/IT aggiornate
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.2.2`
- `Documentation/MachineHardware/software-version-archive.md`, `Docs/Commissioning/10_Job_Editor_Live_Preview_Hardware_Trigger.md` - documentazione aggiornata

**Motivo:** in macchina il run live e gli impulsi IO acquisivano correttamente, e il tool editor VisionPro mostrava immagini aggiornate, ma la finestra popup restava blu perche non selezionava lo stesso sub-record visualizzato dai display produzione. Durante il setup camera serve inoltre poter variare rapidamente l'esposizione senza uscire dal flusso live.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: aggiunta chiave opzionale `LivePreviewExposureUs` default `0`
- Database: nessuna modifica
- Runtime produzione: invariato
- Job Tool Editor: la Live Preview usa ancora `RunOnce -> pulse IO -> display result`, ma seleziona il record immagine corretto e permette la calibrazione esposizione

## [2026-07-01] Versione 3.0.2.1 - Live Preview: eventi VisionPro produzione soppressi e LastRun letto da UserResult

**File modificati:**
- `MainWindow.xaml.cs` - aggiunto flag `IsJobEditorLivePreviewActive` per distinguere i run tecnici della Live Preview dai run produzione
- `MainWindow.xaml.cs` - `MyJobManager_UserResultAvailableAsync` e `CognexManager_JobStopped` ignorano i run tecnici quando la Live Preview Job Editor e' attiva, evitando svuotamento code e audit `VisionPro job stopped`
- `ViewModels/JobToolEditorViewModel.cs` - il ciclo live attende il completamento del job VisionPro prima di leggere il record
- `ViewModels/JobToolEditorViewModel.cs` - la finestra live legge prima il `UserResult` del job selezionato e poi usa `CreateLastRunRecord()` come fallback
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.2.1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.2.1`

**Motivo:** il run singolo della Live Preview generava correttamente un risultato VisionPro, ma gli handler globali di produzione lo scartavano perche la macchina era in stato `Stopped`. Questo produceva log ripetitivi `Pending VisionPro results cleared` / `VisionPro job stopped` e lasciava la finestra live senza immagine.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema
- Database: nessuna modifica
- Runtime produzione: invariato
- Job Tool Editor: i run tecnici della Live Preview non modificano lo stato macchina e non alimentano contatori/queue produzione

## [2026-07-01] Versione 3.0.2.0 - Live Preview: RunOnce VisionPro armato prima del trigger IO

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs` - il loop `LIVE_IO` ora esegue un run singolo del job selezionato e poi pulsa l'uscita camera, evitando che il trigger fisico arrivi quando VisionPro e' fermo/non armato
- `ViewModels/JobToolEditorViewModel.cs` - la finestra live viene aggiornata direttamente dal record VisionPro `ShowLastRunRecordForUserQueue -> LastRun` o da un `LastRun` compatibile trovato nel record dell'ultimo run
- `ViewModels/JobToolEditorViewModel.cs` - aggiunti lock anti-overlap e timeout del run live; in caso di acquisizione bloccata il job viene fermato e il log riporta `LIVE_IO|VisionPro live preview run timeout`
- `Docs/Commissioning/10_Job_Editor_Live_Preview_Hardware_Trigger.md` - procedura aggiornata con la nuova sequenza `RunOnce -> pulse IO -> display LastRun`
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.2.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.2.0`

**Motivo:** il loop precedente generava correttamente gli impulsi fisici (`Output ... HIGH/LOW`) ma non avviava nessuna esecuzione VisionPro. Se il job era in stato `Stopped`, la camera poteva ricevere il trigger ma il pannello non riceveva record da visualizzare nella finestra Live Preview.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; restano usati `LivePreviewIntervalMs` e `LivePreviewPulseMs`
- Database: nessuna modifica
- Runtime produzione: invariato; la modifica riguarda solo la Live Preview tecnica del Job Tool Editor
- VisionPro: non viene usato `CogRecordDisplay.StartLiveDisplay()` e non viene reinizializzata la FIFO live; il job selezionato viene solo eseguito in singolo ciclo tecnico durante la preview

## [2026-07-01] Versione 3.0.1.9 - PCIE settings: Machine Setup unificato per encoder, I/O e punti intervento

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` - la scheda top-level `Encoder Setup` resta nel markup ma viene nascosta; i controlli encoder, simulazione, tachimetro, diagnostica e canali live vengono integrati nella pagina `Machine Setup`
- `Views/UserControls/DigitalIOControl.xaml` - la pagina `Machine Setup` mantiene anche riepilogo punto intervento -> uscita fisica, tabelle `Machine outputs`, `Machine inputs`, `Encoder and tracking` e `Backup / restore`
- `Localization/messages_eng.json`, `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - label aggiornate da `Machine I/O Setup` a `Machine Setup`
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.9`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.9`

**Motivo:** la configurazione PCIE aveva troppe schede separate per un utilizzo pratico in macchina. Encoder, I/O, binding runtime e punti intervento appartengono allo stesso flusso di commissioning, quindi ora sono consultabili e regolabili da una sola pagina piu chiara.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; si continuano a leggere e salvare gli stessi dati macchina
- Database: nessuna modifica
- Runtime produzione: invariato
- UI manutenzione: una sola scheda principale `Machine Setup` raccoglie encoder, I/O macchina e punti intervento; la vecchia scheda encoder e la vecchia vista completa segnali restano nascoste nel markup come fallback tecnico

## [2026-06-30] Versione 3.0.1.8 - Diagnostica PC: shutdown senza ObjectDisposedException sul semaforo

**File modificati:**
- `Services/SystemDiagnosticsService.cs` - `Start()`, `OnRefreshTimerTick()`, `ConfigureTimer()` e `RefreshNowAsync()` ora rispettano anche `_isDisposed`, oltre a `_isShuttingDown`
- `Services/SystemDiagnosticsService.cs` - gestione sicura di `ObjectDisposedException` durante shutdown del refresh diagnostico
- `Services/SystemDiagnosticsService.cs` - `_refreshLock` non viene piu disposto durante `Dispose()` per evitare race con callback timer asincrone ancora attive
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.8`

**Motivo:** durante la chiusura HMI poteva arrivare un callback timer `OnRefreshTimerTick` mentre `SystemDiagnosticsService` stava gia liberando le risorse. Il refresh entrava nel `finally` e chiamava `SemaphoreSlim.Release()` dopo il dispose del semaforo, generando un log `Unhandled exception in OnRefreshTimerTick`.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Database: nessuna modifica
- Runtime produzione: invariato
- Log: eliminato rumore non critico in fase di spegnimento applicazione

## [2026-06-30] Versione 3.0.1.7 - Live Preview senza inizializzazione FIFO VisionPro

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs` - `LiveView()` non chiama piu `StartLiveDisplay()` e non richiede piu `IsLiveDisplayRunning`; la preview tecnica si basa sul loop IO hardware-trigger
- `ViewModels/JobToolEditorViewModel.cs` - se l'uscita trigger camera non viene risolta, viene ripristinato il modo normale invece di lasciare il runtime in hold
- `ViewModels/JobToolEditorViewModel.cs` - `StopLiveDisplay()` viene chiamato solo se una live FIFO VisionPro era stata effettivamente avviata, evitando warning su controlli gia rilasciati
- `Docs/Commissioning/10_Job_Editor_Live_Preview_Hardware_Trigger.md` - procedura aggiornata: la Live Preview non tenta piu la reinizializzazione FIFO live VisionPro
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.7`

**Motivo:** con job QuickBuild basati su FIFO sintetica o hardware trigger esterno, l'avvio della live FIFO VisionPro poteva generare errori come `FIFO non compatibile`, `Tentativo di reinizializzazione hardware` e `FIFO di acquisizione non inizializzata`. In Live Preview la HMI deve simulare lo scatto tramite uscita IO, senza cambiare o reinizializzare la FIFO del job.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; restano usati `LivePreviewIntervalMs` e `LivePreviewPulseMs`
- Database: nessuna modifica
- Runtime produzione: invariato
- Job Tool Editor: la preview tecnica parte se viene risolta l'uscita trigger camera; non dipende piu dallo stato `LiveDisplayRunning` del controllo Cognex

## [2026-06-30] Versione 3.0.1.6 - Live Preview Job Editor via trigger IO hardware

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs` - Live Preview avviata con loop IO hardware solo se `StartLiveDisplay()` risulta attivo; aggiunti `LivePreviewIntervalMs`, `LivePreviewPulseMs`, reset sicuro dell'uscita trigger su stop/cancellazione/chiusura finestra e risoluzione camera `Left/Side` + `Right/Rear`
- `Views/JobToolEditorView.xaml` - aggiunti campi tecnici `Interval (ms)` e `Pulse (ms)` prima dell'avvio Live Preview
- `Views/JobToolEditorView.xaml.cs` - label live preview lette dal server messaggi
- `Models/MachineRuntimeConfiguration.cs` - aggiunti `RuntimeBindings.LivePreviewIntervalMs` e `RuntimeBindings.LivePreviewPulseMs`
- `Services/MachineConfigurationService.cs` - default/upgrade schema per i nuovi parametri live
- `ConfigurationTemplates/machine_runtime_config.template.xml` - default live preview aggiunti al template
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_eng.json`, `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - label lingua aggiornate
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.6`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.6`

**Motivo:** `CogRecordDisplay.StartLiveDisplay()` puo non produrre frame su camere rimaste in hardware trigger esterno. La Live Preview tecnica ora usa lo stesso concetto del ciclo macchina: mantiene il live VisionPro aperto e pulsa periodicamente l'uscita fisica della camera, con intervallo e durata impulso configurabili dal pannello.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: aggiunti campi opzionali `LivePreviewIntervalMs` default `1000` e `LivePreviewPulseMs` default `20` dentro `RuntimeBindings`
- Database: nessuna modifica
- Runtime produzione: invariato; la logica riguarda solo Live Preview del Job Tool Editor
- Sicurezza IO: l'uscita trigger viene riportata allo stato inattivo anche se l'operatore preme Stop Preview o chiude direttamente la finestra live

## [2026-06-30] Nota provvisoria superata dalla versione 3.0.1.6

Questa nota resta come traccia del primo tentativo; la descrizione valida e completa e' la release `3.0.1.6` sopra.

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs` — aggiunto campo `_liveIoCts`, `_livePreviewIntervalMs`; proprietà `LivePreviewIntervalMs`; metodi `StartIoTriggerLoopAsync`, `ResolveCameraTriggerBinding`; modificati `LiveView()` e `StopLiveDisplay()`
- `Views/JobToolEditorView.xaml` — aggiunto controllo "Interval (ms)" visibile solo prima di avviare il live

**Motivo:** `CogRecordDisplay.StartLiveDisplay()` con `TriggerModel=Auto` non produce frame sulle telecamere Teledyne DALSA Nano-M2020 perché restano in modalità hardware trigger (il nodo `TriggerMode` non è scrivibile a runtime). Pulsando periodicamente l'uscita IO del trigger camera si forza lo scatto fisico e il frame entra nel FIFO VisionPro, che è già in Auto mode, e viene mostrato nel live display.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: solo in live mode — loop background che pulsa l'uscita IO della camera (canale risolto da `machine_runtime_config.xml`) all'intervallo configurato (default 1000 ms, range 100–30000 ms); funzionamento normale produzione invariato

## [2026-06-29] Versione 3.0.1.5 - Riepilogo punto -> uscita: duplicati e alias Left/Right risolti

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - `ResolveOutputSignalByCode` normalizza i `SignalCode`, preferisce le righe reali con `Board`/`Channel` compilati quando esistono duplicati e supporta alias compatibili `Left/Side` e `Right/Rear`
- `ViewModels/DigitalIOViewModel.cs` - la tabella `Intervention point to physical output` usa lo stesso resolver robusto anche per righe con spazi nascosti o duplicati
- `App.xaml` - aggiunto stile globale `ComboBoxItem` per evitare warning binding WPF su `HorizontalContentAlignment` / `VerticalContentAlignment`
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.5`

**Motivo:** nella nuova tabella riepilogo alcuni campi `Board`/`Channel` potevano restare vuoti anche se la tabella `Machine outputs` mostrava una riga corretta. La causa era una combinazione di duplicati nascosti dai filtri (`Only real machine signals` mostrava solo la riga reale, mentre il resolver poteva prendere prima una riga test vuota), spazi nei codici e differenza semantica `RIGHT`/`REAR`.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema
- Database: nessuna modifica
- Runtime: risoluzione segnale piu robusta; quando un punto usa `OUT_CAMERA_RIGHT_TRIGGER` e la macchina ha `OUT_CAMERA_REAR_TRIGGER`, viene usato il mapping fisico compatibile
- UI: meno warning WPF sui ComboBox e riepilogo punto -> uscita piu coerente con la tabella `Machine outputs`

## [2026-06-29] Versione 3.0.1.4 - Machine I/O Setup: tabelle input/output complete e visibili

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` - la sezione `Complete machine configuration` nella pagina `Machine I/O Setup` ora si apre di default; le tabelle `Machine outputs` e `Machine inputs` restano editabili e mostrano anche la colonna `Signal type`; reintegrati `Additional runtime bindings` e filtri rapidi segnali
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiornate le label della sezione completa e aggiunta guida per modifica diretta delle tabelle
- `Localization/messages_eng.json` / `Localization/messages_ita.json` - rigenerati tramite script lingua
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.4`

**Motivo:** il primo layout unificato aveva integrato `MachineInputs`, `MachineOutputs`, encoder, runtime binding e backup, ma la sezione era chiusa dentro un expander avanzato. In macchina poteva sembrare che alcune schede della vecchia `Machine Configuration` fossero state eliminate. Ora la configurazione completa e' visibile subito nella pagina unificata, mantenendo una sola area operativa.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; input/output/encoder sono gli stessi blocchi gia esistenti
- Database: nessuna modifica
- Runtime: nessuna modifica comportamentale del giro segnali
- UI: `Machine inputs`, `Machine outputs` e `Additional runtime bindings` sono raggiungibili nella stessa pagina; input/output restano modificabili con aggiunta/cancellazione righe tramite DataGrid

## [2026-06-29] Versione 3.0.1.3 - PCIE settings unificato per I/O macchina e punti intervento

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` - tab `Intervention Points` rinominata in `Machine I/O Setup`, aggiunti comandi configurazione, tabella punto -> uscita fisica e sezione avanzata espandibile con binding runtime, I/O, encoder e backup
- `Models/MachineHardwareTemplate.cs` - aggiunto modello UI `InterventionSignalAssignment`
- `ViewModels/DigitalIOViewModel.cs` - aggiunta collezione calcolata `InterventionSignalAssignments`, aggiornata su modifiche a punti, segnali, encoder e mapping
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte chiavi lingua per la nuova vista e per lo stato segnale punto -> uscita
- `Localization/messages_eng.json` / `Localization/messages_ita.json` - rigenerati tramite script lingua
- `Docs/Commissioning/09_Machine_IO_Setup_Unified_View.md` - nuova guida pratica per manutentori/collaudatori
- `Documentation/MachineHardware/pci-io-unified-setup-2026-06-29.md` - nuova documentazione tecnica con differenza tra punti intervento e configurazione macchina
- `Docs/Commissioning/README.md`, `Docs/Manual/README.md`, `Documentation/MachineHardware/README.md` - indici aggiornati
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.3`

**Motivo:** ridurre le schede visibili in `PCIE settings` e rendere la configurazione piu leggibile per una macchina automatica industriale. Il manutentore ora vede nella stessa pagina la sequenza prodotto e l'uscita fisica usata da ogni punto, senza dover saltare mentalmente tra `Intervention Points` e `Machine Configuration`.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; vengono visualizzati e modificati gli stessi dati gia esistenti
- Database: nessuna modifica
- Runtime: nessuna modifica comportamentale; il giro segnali resta fotocellula -> PRODUCT_ZERO -> encoder -> intervention point -> SignalCode -> MachineOutputs -> canale fisico
- UI: la vecchia tab `Machine Configuration` non e' piu visibile come tab principale; le funzioni avanzate sono disponibili nella sezione espandibile della nuova pagina `Machine I/O Setup`
- Verifica: build `Debug|x64` completata con successo; restano warning preesistenti non legati alla modifica
- Backup pre-modifica locale: `D:\Pulsar\Backups\QtisVisionPanel\QtisVisionPanel_source_pre_pcie_unified_20260629_111149`
- Backup pre-modifica condiviso: `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\lastRelease_backups\QtisVisionPanel_shared_source_pre_pcie_unified_20260629_111149`

## [2026-06-29] Versione 3.0.1.2 - Pulizia conservativa gestione I/O ed encoder

**File modificati:**
- `Services/MachineRuntimeIo.cs` - nuovo helper condiviso per parsing canali, polarita' segnali e conversioni encoder
- `Services/MachineController.cs` - usa il nuovo helper per target encoder e posizione macchina
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - usa il nuovo helper per risolvere canali output e stati elettrici
- `ViewModels/DigitalIOViewModel.cs` - i wrapper locali di calcolo/polarita' delegano al nuovo helper condiviso
- `QtisVisionPanel.csproj` - incluso il nuovo file helper nel progetto
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.1.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.1.2`

**Motivo:** avviare la pulizia della gestione input/output riducendo duplicazioni tra ViewModel, controller macchina e controller MultiShot. Le formule encoder e la conversione logico/elettrico devono restare uniche per evitare divergenze future su trigger, scarto, velocita' e MultiShot.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Database: nessuna modifica
- Runtime: comportamento invariato; refactor interno conservativo
- Verifica: build `Debug|x64` completata con successo; restano warning preesistenti non legati alla modifica
- Backup pre-modifica: `D:\Pulsar\Backups\QtisVisionPanel\QtisVisionPanel_application_verified_20260629_092528`

## [2026-06-29] Integrazione messaggio "Recupero VisionPro fallito" nel server messaggi

**File modificati:**
- `ServerMessage/ServerMessageStructure.cs` — aggiunte property `Sub_entry_VisionProRecoveryFailedTitle` e `Sub_entry_VisionProRecoveryFailedMessage` (con placeholder `{0}` per il conteggio tentativi)
- `C:\QtisVision\Language\messages_ita.json` — aggiunte traduzioni italiane per le due nuove chiavi
- `C:\QtisVision\Language\messages_eng.json` — aggiunte traduzioni inglesi per le due nuove chiavi
- `MainWindow.xaml.cs` — sostituita la stringa hardcoded del dialogo `ShowWarning` con `ServerMessagePersonalize.GetMessageOrDefault(...)` + `string.Format` per il parametro `{0}`

**Motivo:** il dialogo "Recupero VisionPro fallito" (mostrato dopo 3 tentativi automatici falliti) usava stringhe hardcoded in italiano, non traducibili a runtime e non coerenti con il sistema di messaggi del pannello.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: il testo del dialogo viene ora caricato dal file JSON lingua attivo; fallback inglese incorporato in caso di chiave mancante; la logica di trigger (3 tentativi) rimane invariata

## [2026-06-25] Documentazione schema elettrico I/O ed encoder baseline corrente

**File modificati:**
- `Documentation/MachineHardware/io-encoder-electrical-schematic-current-baseline-2026-06-25.md` - nuovo schema operativo per `PCIE-1756-BE`, `ADAM-3951-BE`, `PCIE-1884-AE`, `ADAM-3937-BE`, fotocellula, trigger camera, scarto, heartbeat, allarmi e encoder `Counter0 / ENC1`
- `Docs/Commissioning/README.md` - aggiunto riferimento allo schema elettrico nel percorso consigliato di commissioning
- `Documentation/MachineHardware/README.md` - aggiunto il nuovo documento tra i template macchina/hardware

**Motivo:** fornire a collaudo e manutenzione uno schema elettrico coerente con il funzionamento attuale dell'applicazione, separando cablaggio fisico, mapping software e logica runtime encoder-driven.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Database: nessuna modifica
- Runtime: nessuna modifica codice
- Documentazione: schema aggiornato per I/O, encoder, MultiShot e sequenza fotocellula -> quota encoder -> trigger/scarto

## [2026-06-22] Versione 3.0.1.1 - Consolidamento Claude: DataInspector evidence UX e Overview persistence

**File modificati principali:**
- `Inspector/ViewModels/DataInspectorViewModel.cs` - comandi zoom fullscreen immagini source/processed, reset zoom e download evidenza
- `Views/UserControls/DataInspectorView.xaml` / `.xaml.cs` - overlay zoom con rotella mouse, pulsanti `+`, `-`, `1:1`, chiusura e pulsante `Scarica evidenza`
- `Views/UserControls/DisplayRecord/CameraContainer.xaml.cs` - il refresh runtime automatico viene limitato al primo `Loaded`, evitando rebuild delle camera view al ritorno pagina
- `Docs/Manual/*`, `Documentation/MachineHardware/*`, `Properties/AssemblyInfo.cs` - documentazione/manuali e release allineati a `3.0.1.1`

**Motivo:** consolidare gli aggiornamenti Claude non ancora inclusi nell'archivio ufficiale. L'operatore deve poter controllare un difetto salvato in DataInspector con zoom leggibile e scaricare un pacchetto evidenza; la Overview deve mantenere le immagini quando si naviga fuori e si rientra.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Ricette/VPP: nessuna modifica obbligatoria
- Database: nessuna modifica schema
- Runtime/UI: DataInspector piu utile per analisi difetti; Overview piu stabile dopo navigazione; refresh camera completo ancora gestito esplicitamente da MainWindow dopo init VisionPro/cambio ricetta
- Release software: `3.0.1.1`

## [2026-06-18] Feature: DataInspectorView — zoom immagini + download evidenza

**File modificati:**
- `Inspector/ViewModels/DataInspectorViewModel.cs` — aggiunti comandi `OpenSourceZoomCommand`, `OpenProcessedZoomCommand`, `CloseZoomCommand`, `ZoomInCommand`, `ZoomOutCommand`, `ResetZoomCommand`, `DownloadEvidenceCommand`; proprietà `IsZoomOverlayVisible`, `ZoomedImage`, `ZoomLevel`, `ZoomPercent`, `CanDownloadEvidence`; metodi privati `OpenZoom(BitmapImage)` e `DownloadEvidenceAsync()`
- `Views/UserControls/DataInspectorView.xaml` — aggiunto overlay fullscreen di zoom con ScrollViewer+ScaleTransform+controlli ±/1:1/✕; pulsante trasparente su entrambe le anteprime immagine (source + processed) per aprire lo zoom al click; pulsante "Scarica evidenza" nel pannello Storage Details
- `Views/UserControls/DataInspectorView.xaml.cs` — aggiunto handler `ZoomScrollViewer_PreviewMouseWheel` per zoom mouse wheel

**Motivo:** L'operatore ha bisogno di verificare i difetti sull'immagine ad alta risoluzione (zoom) e di esportare le immagini + un file di dettaglio per analisi esterna.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: click su anteprima immagine apre overlay fullscreen con zoom wheel/±/1:1; pulsante "Scarica evidenza" copia le immagini in una cartella scelta dall'operatore e scrive `detail.txt` con timestamp, PieceId, ricetta, operatore, risultati ispezione; al termine apre la cartella in Explorer

## [2026-06-18] Fix: CameraContainer si ricarica (CameraViews.Clear) al ritorno dalla navigazione

**File modificati:**
- `Views/UserControls/DisplayRecord/CameraContainer.xaml.cs` — aggiunto campo `_hasCompletedInitialLoad`; in `OnViewportLoaded` la chiamata a `RefreshFromRuntimeConfiguration()` avviene solo al PRIMO `Loaded` (prima visita o avvio). Sui `Loaded` successivi (ritorno da navigazione) viene aggiornato solo il layout profile. Il metodo pubblico `RefreshFromRuntimeConfiguration()` rimane invariato e continua ad essere chiamato esplicitamente da MainWindow dopo init VisionPro o cambio ricetta.

**Motivo:** `CameraContainer` è un singleton (ViewFactoryService già lo metteva in cache), ma `OnViewportLoaded` chiamava `viewModel.RefreshFromRuntimeConfiguration()` → `CameraViews.Clear()` + rebuild ad ogni `Loaded`, inclusi i ritorni dalla navigazione. Questo distruggeva i controlli CogRecordDisplay e le ultime immagini di ispezione.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: tornando all'overview le telecamere rimangono nella loro ultima visualizzazione; VisionPro continua a inviare nuovi risultati normalmente

## [2026-06-18] Versione 3.0.1.0 - Consolidamento Claude: contatori live e popup timeout companion

**File modificati principali:**
- `Services/InspectionOrchestrator.cs` - evento `CompanionTimeoutOccurred` quando una camera companion prevista non consegna il risultato entro timeout
- `MainWindow.xaml.cs` - popup warning non modale, auto-close 10s e rate-limit 30s per timeout companion
- `Database/CounterManager.cs` - aggiornamento eventi contatori tramite `Dispatcher.BeginInvoke` per non bloccare il thread ispezione
- `ViewModels/TopMenuBarViewModel.cs` - refresh immediato del top bar su `CountersUpdated`
- `Views/UserControls/StatisticsView.xaml.cs` / `ViewModels/InspectionCounterViewModel.cs` - subscription ripristinata a ogni rientro pagina
- `Models/CounterData.cs` - chiave canonica `Key` per aggiornare contatori anche con testi localizzati o header diversi
- `Docs/Manual/*`, `Documentation/MachineHardware/*`, `Properties/AssemblyInfo.cs` - documentazione e release allineate a `3.0.1.0`

**Motivo:** consolidare gli aggiornamenti Claude del 18 giugno 2026: contatori Overview/top bar congelati dopo navigazione o burst rapidi, timeout companion poco visibile all'operatore e rischio di blocco del thread orchestrator per eventi UI sincroni.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Ricette/VPP: nessuna modifica obbligatoria
- Database: nessuna modifica schema
- Runtime: contatori piu reattivi, popup visibile in caso di camera companion mancante, elaborazione ispezioni meno dipendente dal Dispatcher UI
- Release software: `3.0.1.0`

## [2026-06-18] Fix: burst counter freeze + COMPANION_TIMEOUT popup notification

**File modificati:**
- `Services/InspectionOrchestrator.cs` — aggiunto evento `CompanionTimeoutOccurred Action<IReadOnlyList<string>, double>`; fire in `WaitForExpectedCompanionResultsAsync` quando `missing.Count > 0`
- `MainWindow.xaml.cs` — aggiunto campo `_lastCompanionTimeoutNotificationAt`; sottoscrizione a `CompanionTimeoutOccurred` in `EnsureInspectionOrchestratorInitialized()`; handler `OnCompanionTimeoutOccurred` mostra `SystemNotificationWindow.Show()` non-modale con auto-close 10s, rate-limited a 1 popup ogni 30s
- `Database/CounterManager.cs` — tutti i `Dispatcher.Invoke(() => CountersUpdated?.Invoke())` cambiati in `Dispatcher.BeginInvoke(new Action(...))`: il thread orchestrator non viene più bloccato durante burst di ispezioni rapide
- `ViewModels/TopMenuBarViewModel.cs` — aggiunta sottoscrizione a `ServiceLocator.CounterManager.CountersUpdated`; handler `OnCountersUpdated()` chiama `UpdateCounters()` immediatamente; unsubscribe in `Dispose()`. Il top bar ora aggiorna il contatore per ogni ispezione completata invece di aspettare il timer 1s

**Motivo:** Durante burst post-`COMPANION_TIMEOUT` (4–5 prodotti processati in 0.4s), il top bar rimaneva congelato per 30s e poi saltava direttamente al valore finale. Causa 1: `Dispatcher.Invoke` (sincrono) bloccava il thread orchestrator ad ogni ispezione, serializzando l'elaborazione. Causa 2: `TopMenuBarViewModel` si aggiornava solo dal `DispatcherTimer` a 1Hz, non per ogni ispezione. Causa 3: timeout COMPANION silenzioso senza feedback visivo all'operatore.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: aggiunto `CompanionTimeoutOccurred` su `InspectionOrchestrator` (internal sealed)
- Comportamento runtime: contatori top bar si aggiornano per ogni prodotto; popup Warning appare quando camera companion non risponde entro timeout; orchestrator processa burst più velocemente (non più bloccato su Dispatcher)

## [2026-06-18] Fix: contatori overview non si aggiornano durante produzione (subscription persa su Unloaded)

**File modificati:**
- `Views/UserControls/StatisticsView.xaml.cs` — `SetupEventHandlers()` spostato dal costruttore all'evento `Loaded`; su ogni `Loaded` dedup+subscribe e `UpdateCountersFromManager()`; `Unloaded` rimuove solo subscription senza disporre il ViewModel prima di `Dispose()`
- `ViewModels/InspectionCounterViewModel.cs` — aggiunto `Resubscribe()`: ri-iscrive il ViewModel a `CounterManager.CountersUpdated` e `FeaturesChanged` dopo un `Dispose()` causato da Unloaded

**Motivo:** `StatisticsView.Unloaded` rimuoveva la subscription all'evento `CountersUpdated` e chiamava `ViewModel.Dispose()` (che a sua volta rimuoveva la subscription del ViewModel). Quando la vista tornava visibile (navigazione avanti/indietro), `Loaded` non chiamava `SetupEventHandlers()` di nuovo perché l'istanza era già costruita — quindi nessuna subscription era attiva e i contatori rimanevano congelati ai valori del momento della costruzione.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: aggiunto `Resubscribe()` su `InspectionCounterViewModel`
- Comportamento runtime: i contatori overview si aggiornano in tempo reale su ogni ispezione; la subscription è garantita su ogni ciclo Loaded/Unloaded

## [2026-06-18] Fix: contatori SELECTION non si aggiornano nella vista principale (header mismatch)

**File modificati:**
- `Models/CounterData.cs` — aggiunto campo `Key` (stringa canonica inglese, mai tradotta)
- `ViewModels/InspectionCounterViewModel.cs` — `InitializeCounters()` imposta `Key` per ogni contatore di selezione; `UpdateSelectionCounter()` cerca prima per `Key`, poi per `Header`
- `Views/UserControls/StatisticsView.xaml.cs` — `UpdateSelectionCounter()` adeguata con la stessa logica Key-first

**Motivo:** `InitializeCounters()` impostava `Header` con la stringa localizzata da ServerMessage (es. `messages.Sub_entry_Total`). Le chiavi `Sub_entry_Total`, `Sub_entry_OK`, `Sub_entry_Defects` non esistono in nessun file JSON di localizzazione, quindi `Header` rimaneva il nome grezzo della chiave (`"Sub_entry_Total"` ecc.). `UpdateCountersFromDictionary()` cercava `c.Header == "Total"` — non trovava mai nessun elemento → i contatori Total/Good/NoGood non si aggiornivano nella UI.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `CounterData` ha un nuovo campo `Key` (non breaking — nullable di fatto)
- Comportamento runtime: i contatori di selezione si aggiornano correttamente in ogni istanza di `StatisticsView`

## [2026-06-17] Versione 3.0.0.9 - Consolidamento Claude: MultiShot, Overview, LiveView e PowerFlex log cleanup

**File modificati principali:**
- `Services/InspectionOrchestrator.cs` / `Models/CameraResult.cs` - pairing multi-camera rafforzato con timestamp enqueue, trace arrivo companion, timeout espliciti e guardia contro risultati di prodotti successivi
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` / `Services/MultiShotTrigger/ImageStitchingStatusReader.cs` / `ViewModels/DigitalIOViewModel.cs` - diagnostica FrameAck resa non bloccante e provider VisionPro disabilitato per evitare accessi COM fuori thread STA
- `Views/UserControls/DisplayRecord/CameraContainer.xaml.cs` / `ViewModels/CameraContainerViewModel.cs` / `Services/ViewFactoryService.cs` - Overview persistente e riallineata ai job VPP runtime con hardware camera reale
- `Cls_Vpro/IgigaCameraAccess.cs` - LiveView non forza piu `TriggerMode=Off` su camere hardware-triggered
- `Services/PowerFlex525EtherNetIpClient.cs` - trace raw EtherNet/IP spostati a `Debug` e log monitor CIP throttled
- `Docs/Manual/*`, `Documentation/MachineHardware/*`, `Properties/AssemblyInfo.cs` - manuali/documentazione e release allineati a `3.0.0.9`

**Motivo:** Consolidare gli aggiornamenti fatti con Claude in una baseline unica e leggibile. I fix riducono timeout/mismatch MultiShot, evitano interferenze COM VisionPro nel thread sbagliato, rendono l'Overview coerente con il VPP attivo e puliscono i log PowerFlex in produzione.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Ricette/VPP: nessuna modifica obbligatoria
- Database: nessuna modifica nuova
- Runtime: migliorata robustezza pairing Top+companions, meno warning LiveView e meno rumore log PowerFlex
- Release software: `3.0.0.9`

## [2026-06-17] Fix: PowerFlex525 — throttle log "CIP monitor read completed" ogni 1 minuto o a cambio stato

**File modificati:**
- `Services/PowerFlex525EtherNetIpClient.cs` — aggiunto metodo `LogCipMonitorReadCompleted(available, total)` con throttle a `RepeatedStatusLogInterval` (1 min); il log appare solo se il numero di valori disponibili cambia oppure è passato almeno 1 minuto dall'ultimo log; aggiunti campi `_lastCipMonitorReadLogAt` e `_lastCipMonitorAvailableCount`

**Motivo:** il messaggio "CIP monitor read completed. Available values: 20/20" veniva emesso ogni ~3 secondi (intervallo poll del monitor), sporcando il log in produzione senza portare informazione utile

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: il log appare al massimo 1 volta al minuto se il conteggio non cambia; appare immediatamente se il numero di valori disponibili varia (degradazione o ripristino del drive)

## [2026-06-17] Fix: MultiShot — disabilitato StitchingStatusProvider per COMPANION_TIMEOUT arrived=[]

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — commentate le due chiamate `AttachStitchingStatusProvider(...)`: nel costruttore (riga ~1999) e in `RecreateMultiShotController()` (riga ~4892)

**Motivo (root cause):** `AttachStitchingStatusProvider` assegnava al `MultiShotTriggerController` una lambda che chiama `ImageStitchingStatusReader.ReadFromJob(job)` — ovvero accede ai COM objects di VisionPro (`CogToolBlock.Outputs["isReady"].Value`) da un thread del ThreadPool (Task.Run). VisionPro usa STA COM apartment; l'accesso da thread MTA senza marshaling corrompe lo stato interno del job manager. L'effetto osservato: VisionPro perdeva il 9° trigger hardware → `ImageStitching.frameIndex` si fermava a 8 → `isReady` restava False → `ShouldIgnoreIntermediateSideLeftMultiShotResult` scartava TUTTI e 9 gli eventi → `_sideQueue` sempre vuota → `COMPANION_TIMEOUT|arrived=[]`

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `IMultiShotTriggerController.StitchingStatusProvider` resta l'interfaccia; solo l'assegnazione è disabilitata
- Comportamento runtime: i log `MULTISHOT_FRAME_ACK` non vengono più prodotti; il multishot riprende a consegnare il risultato LEFT all'orchestrator (isReady=True sul 9° frame); la feature può essere re-abilitata in futuro usando `Application.Current.Dispatcher.InvokeAsync` invece di `Task.Run`

## [2026-06-17] Fix: PowerFlex525 — messaggi protocollo EtherNet/IP abbassati da Info a Debug

**File modificati:**
- `Services/PowerFlex525EtherNetIpClient.cs` — in `ReadParameterCore`, `RegisterSession`, `SendRrData`, `WriteFaultClearCommandCore`: 7 chiamate `?.Info(...)` sui frame EIP (CIP request/response, RegisterSession req/resp, SendRRData req) cambiate in `?.Debug(...)`

**Motivo:** i messaggi di protocollo raw (hex dump dei frame TCP EtherNet/IP) vengono emessi per ogni parametro PowerFlex letto → flooding del log `app.log` in produzione. Sono trace di debug utili solo durante sviluppo/diagnosi del driver EIP

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: questi messaggi non appaiono più in produzione (NLog `minLevel=Info`); per riabilitarli abbassare temporaneamente il livello NLog a `Debug` nel file `Nlog.config`

## [2026-06-17] Fix: MultiShot — FrameAck diagnostic reso fire-and-forget per non bloccare la sequenza encoder

**File modificati:**
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` — in `PulseOutputAndContinueAsync`: il blocco `lock(_sync) { PulseInFlight = false; ... }` ora avviene PRIMA del read diagnostico FrameAck. Il `Task.Delay(150ms)` + lettura ToolBlock VisionPro vengono ora eseguiti in un `Task.Run` fire-and-forget, senza bloccare il loop di valutazione posizione encoder

**Motivo (root cause):** la feature diagnostica `MULTISHOT_FRAME_ACK` (aggiunta in commit b161ad8) eseguiva `await Task.Delay(150ms)` PRIMA di `PulseInFlight = false`, bloccando ogni impulso per 150 ms. Con l'encoder a ~5000 p/s e distanza target 237 pulsi (~47 ms tra scatti), questo produceva MULTISHOT_SIDE_TARGET_TOO_LATE da shot 2/9 fino a 9/9 con overshoot crescente (+920 impulsi/shot). L'immagine veniva catturata quando il prodotto era già passato

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna; `IMultiShotTriggerController` immutata
- Comportamento runtime: la sequenza MultiShot torna a rispettare le posizioni encoder target; il log `MULTISHOT_FRAME_ACK` continua a essere prodotto (con 150 ms di ritardo rispetto all'impulso, come progettato) senza impatto sul timing degli scatti

## [2026-06-17] Fix: LiveView — rimossa scrittura TriggerMode=Off su camera hardware-triggered

**File modificati:**
- `Cls_Vpro/IgigaCameraAccess.cs` — in `EnableLivePreviewMode`: rimossa la chiamata `TrySetStringFeature(gigEAccess, "TriggerMode", "Off", ...)`. Il block GigE Vision scrive ancora `AcquisitionMode=Continuous` e `TriggerSelector=FrameStart` ma non tocca più `TriggerMode`

**Motivo:** la telecamera Teledyne DALSA Nano-M2020 rifiuta la scrittura `TriggerMode=Off` con `ccGigEVisionCamera::FeatureError: Node is not writable`. Il nodo non è scrivibile mentre la camera è in stato di acquisizione attiva (VisionPro ha già eseguito `AcquisitionStart`). La norma GigE Vision richiede che `TriggerMode` venga cambiato solo da stato `AcquisitionStop`. VisionPro `CogAcqTriggerModelConstants.Auto` impostato nello stesso metodo è già sufficiente per il LiveView: gestisce l'acquisizione continua a livello VisionPro senza dover portare la camera in free-run a livello GigE hardware

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: il LiveView si avvia senza WARN "impossibile impostare TriggerMode a Off"; la camera rimane nel suo modo trigger configurato nel VPP (hardware trigger); VisionPro Auto mode gestisce i frame per la preview

## [2026-06-17] Fix: Overview mostra solo le camere presenti nel VPP attivo

**File modificati:**
- `Views/UserControls/DisplayRecord/CameraContainer.xaml.cs` — in `OnViewportLoaded`: se `JobMapping.Count > 0` (VisionPro già inizializzato) chiama subito `RefreshFromRuntimeConfiguration()` prima di `RefreshViewportLayout()`. Questo si attiva sia al primo render che a ogni rientro nella vista con la navigazione cached
- `ViewModels/CameraContainerViewModel.cs` — aggiunto metodo privato `HasHardwareCamera(int jobId)` (`AcqFifo.FrameGrabber != null`) e filtro nel loop runtime per escludere job VPP senza camera fisica

**Motivo (root cause):** `CameraContainerViewModel` viene costruito durante la splash (prima che VisionPro finisca l'init) → `JobMapping.Count == 0` → XML fallback → tutte e 4 le telecamere da `CameraConfig.xml`. VisionPro si inizializza 30+ secondi dopo, `loadVsionPro()` non chiama `RefreshFromRuntimeConfiguration()`. Anche `InitializeVisionSystem()` che la chiama non risolve se l'utente ha già navigato via. Il trigger `OnViewportLoaded` garantisce che ogni volta che il pannello diventa visibile usi i dati runtime aggiornati

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna; `JobMapping` resta immutato
- Comportamento runtime: l'Overview mostra solo i pannelli dei job VPP attivi; se VisionPro non è ancora inizializzato mostra il fallback XML (transitorio); dopo l'init mostra i pannelli corretti anche navigando avanti-indietro

## [2026-06-16] Feature: CameraContainer singleton — immagini Overview persistenti tra navigazioni

**File modificati:**
- `Services/ViewFactoryService.cs` — aggiunto campo `_cachedCameraContainer` + metodo `GetOrCreateCameraContainer()`; i case `"Channel1"` e `default` ora restituiscono sempre la stessa istanza invece di creare `new CameraContainer()` ad ogni navigazione

**Motivo:** ogni navigazione verso Overview/AllChannel distruggeva i controlli `CogRecordDisplay` e ricreava un'istanza vuota, perdendo le immagini dell'ultimo prodotto ispeionato

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: `CameraContainer` creato una sola volta all'avvio; le immagini dell'ultimo prodotto rimangono visibili su Overview anche dopo aver navigato su altre pagine e tornato; il FileWatcher su `CameraConfig.xml` continua a funzionare (aggiorna la configurazione camera se la ricetta cambia)

## [2026-06-16] Feature: MULTISHOT_FRAME_ACK — feedback per-impulso stato stitching VisionPro

**File modificati:**
- `Services/MultiShotTrigger/ImageStitchingStatusReader.cs` — nuovo file: `ImageStitchingStatus` (struct readonly) + `ImageStitchingStatusReader` (classe statica); legge gli output `isReady`, `frameIndex`, `status`, `errorMessage` dal ToolBlock `ImageStitching` del job VisionPro tramite reflection ricorsiva sulla gerarchia `Tools`; accetta `object` per il job così i chiamanti non richiedono il riferimento diretto a `Cognex.VisionPro.QuickBuild`
- `Services/MultiShotTrigger/IMultiShotTriggerController.cs` — aggiunta proprietà `Func<string, ImageStitchingStatus> StitchingStatusProvider { get; set; }` (parametro string = profile key)
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` — aggiunto campo `_stitchingStatusProvider` (volatile); costante `FrameAckReadDelayMs = 150`; in `PulseOutputAndContinueAsync`, dopo il pulse: `await Task.Delay(150ms)` + chiama il provider + logga `MULTISHOT_FRAME_ACK|OK|...` o `MULTISHOT_FRAME_ACK|MISMATCH|...` con `shot=N/9`, `vp_frame=X`, `ready=`, `status=[]`
- `ViewModels/DigitalIOViewModel.cs` — aggiunto `AttachStitchingStatusProvider(controller)` (static); collega il provider ai job CogJob di MainWindow per profilo (left/side → `_sideJob`, front → `_frontJob`, ecc.); chiamato in entrambi i punti di creazione: costruttore e `RecreateMultiShotController()`
- `QtisVisionPanel.csproj` — aggiunta entry `<Compile Include="Services\MultiShotTrigger\ImageStitchingStatusReader.cs" />`

**Motivo:** dopo ogni impulso IO della scheda (DO02), leggere il ToolBlock `ImageStitching` di VisionPro per verificare che il `frameIndex` corrisponda al numero di impulso generato. Diagnostica per isolare se il problema `missing=[left]` è lato VisionPro (frame non acquisito/stitching fallito) o lato C# (logica di pairing)

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `IMultiShotTriggerController` ha nuova proprietà opzionale (`get; set;`)
- Comportamento runtime: ogni impulso MultiShot aggiunge un log `MULTISHOT_FRAME_ACK|OK|profile=left shot=N/9 vp_frame=N ready=False status=[Frame N/9 at X=...]` (150ms dopo il pulse). Se `vp_frame ≠ shot`, appare `MISMATCH`. Nessun impatto sul timing della macchina (il delay è dentro un task fire-and-forget)

## [2026-06-16] Fix: InspectionOrchestrator — companion pairing FIFO corrotto (cross-product mismatch)

**File modificati:**
- `Services/InspectionOrchestrator.cs` — aggiunta costante `MaxCompanionLagMs = 5000.0`; `WaitForExpectedCompanionResultsAsync` ora riceve `topEnqueuedAtUtc` e usa `RoleQueueHasValidResult` (lag check) invece di `RoleQueueHasResult`; nuovo metodo `RoleQueueHasValidResult`: rifiuta risultati companion con `EnqueuedAtUtc - topEnqueuedAtUtc > 5000ms` (appartengono a un prodotto futuro, lasciati in coda); nella fase di dequeue, peek+lag check prima di `TryDequeue`: se lag > soglia, logga `COMPANION_PRODUCT_MISMATCH` e lascia il risultato in coda per il TOP successivo; il campo `topPeek` sostituisce il wildcard `_` nel `while (_topQueue.TryPeek(...))`
- `MainWindow.xaml.cs` — aggiunto `logger.Debug("VISION_EVENT|UserResultAvailable fired")` all'ingresso del handler VisionPro; aggiunto `logger.Debug($"VISION_EVENT|job=... role=... — evaluating multishot filter")` prima di `ShouldIgnoreIntermediateSideLeftMultiShotResult`; questi log Debug permettono di distinguere se VisionPro non spara l'evento o se il filtro scarta il risultato

**Root cause confermata dai log 2026-06-16:**
- `COMPANION_ARRIVED|role=left waited=25500ms timeout=30s` (Prodotto 10): il fix del timeout 30s era corretto, ma il LEFT che arrivava apparteneva al **Prodotto 11** (lag=+25491ms). VisionPro non aveva prodotto alcun risultato LEFT per il Prodotto 10. Il LEFT[11] veniva consumato dal wait del TOP[10] → poi TOP[11] aspettava 30s senza LEFT → `COMPANION_TIMEOUT|missing=[left] arrived=[]`.
- Il nuovo lag check impedisce questo: LEFT[11] (lag=+25491ms > 5000ms) NON viene conteggiato come "arrived" per TOP[10], né dequeued. TOP[10] aspetta il timeout completo con `missing=[left]`. Poi TOP[11] preleva LEFT[11] dalla coda (lag=+425ms < 5000ms) → pairing corretto.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna — firma pubblica `ProcessQueuedPairsAsync(CancellationToken)` invariata
- Comportamento runtime: il companion LEFT di un prodotto non viene più consumato dall'orchestratore del prodotto precedente se VisionPro non ha prodotto risultato per quest'ultimo; ogni prodotto riceve il proprio LEFT se disponibile; prodotti con LEFT mancante ricevono `missing=[left]` in modo isolato senza impattare i prodotti successivi

---

## [2026-06-16] Feature: rinomina lettera vista Left S→F nel filename immagini + sync cartella condivisa

**File modificati:**
- `Cls_Config/Calss_structure/ConfigClassStructure.cs` — aggiunta proprietà `SharedImageDir` (string, default `""`) nella classe `Configuration`; quando non vuota, abilita il mirroring automatico delle immagini salvate verso la cartella condivisa
- `MainWindow.xaml.cs` — chiave del dizionario `_recordDisplays` per la SideCamera cambiata da `"S"` a `"F"`; chiave per la FrontCamera cambiata da `"F"` a `"FR"` (evita collisione)
- `SaveImage/ISaveImage.cs` — `GetSavedRecord()`: ramo `"S"` → `"F"` (Side/Left), ramo `"F"` → `"FR"` (Front); `TryGetImageFromToolBlock()`: stesso swap; aggiunto metodo `SyncPieceFolderToShared()` (fire-and-forget background); in `_SaveImage()` dopo il salvataggio locale, se `SharedImageDir` è configurato, lancia `SyncPieceFolderToShared` in background

**Motivo:** l'operatore voleva che i filename della telecamera Left usassero la lettera `F` anziché `S` (es. `CH1_00000063_F_A.bmp` invece di `CH1_00000063_S_A.bmp`). La lettera `FR` è stata assegnata alla FrontCamera per evitare collisioni nel dizionario. La sync automatica verso la cartella condivisa elimina il passo manuale di copia dopo ogni aggiornamento.

**Impatto:**
- Config.xml: nuovo campo `<SharedImageDir></SharedImageDir>` (opzionale, lasciarlo vuoto disabilita la sync); se il campo non è presente nell'XML esistente, viene deserializzato come stringa vuota senza errori
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: filename Left = `CH1_XXXXXXXX_F_*.bmp/.jpg`; filename Front (se presente) = `CH1_XXXXXXXX_FR_*.bmp/.jpg`; dopo ogni salvataggio immagine, se `SharedImageDir` è valorizzato, tutti i file del piece-folder vengono copiati in background nella cartella condivisa preservando la struttura data/session/batch/piece

---

## [2026-06-16] Fix: InspectionOrchestrator — timeout 5s invece di 30s + timing trace multi-camera

**File modificati:**
- `Models/CameraResult.cs` — aggiunta proprietà `EnqueuedAtUtc` (DateTime UTC impostato prima di ogni enqueue)
- `MainWindow.xaml.cs` — aggiunto campo `_isMultiShotConfigured` (volatile bool); aggiunto `RefreshMultiShotConfiguredState()` che legge la config dal disco UNA SOLA VOLTA all'avvio della macchina; `IsAnyMultiShotRuntimeActive()` ora legge il campo cached anziché creare un nuovo `MachineConfigurationService().Load()` ad ogni chiamata; `RefreshMultiShotConfiguredState()` chiamato in `runVisionPro()` prima di `StartVisionHealthMonitoring()`; set di `cameraResult.EnqueuedAtUtc = DateTime.UtcNow` prima di ogni enqueue nella switch dei 5 ruoli camera
- `Services/InspectionOrchestrator.cs` — `WaitForExpectedCompanionResultsAsync` ora traccia l'arrivo di ogni companion con `COMPANION_ARRIVED|role=X waited=Yms`; in caso di timeout logga `COMPANION_TIMEOUT` con lista missing/arrived; `ProcessQueuedPairsAsync` logga `CAMERA_TIMING|role=X lag-from-top=+Yms` per ogni companion dequeued; il log finale di "Processing inspection group" include ora `waited=Xms`

**Root cause:** `IsAnyMultiShotRuntimeActive()` creava `new MachineConfigurationService().Load()` ad ogni invocazione (inclusa ogni iterazione del wait-loop da 20ms in `WaitForExpectedCompanionResultsAsync`). Durante la scrittura atomica di `AsyncConfigManagerXml` (temp-file → rename), la lettura poteva fallire silenziosamente → il catch restituiva `false` → `GetInspectionGroupTimeout()` usava `VisionResultTimeout = 5s` invece di `VisionMultiShotResultTimeout = 30s` → il risultato VisionPro stitched (9 immagini, arriva ~300-800ms dopo l'ultimo pulse) superava il timeout → `companions=[] missing=[left]`. Evidenza matematica nei log: Queue size=3, gap=14.879s → 14.879/3 ≈ 4.96s = esattamente 5s per prodotto.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna — `InspectionOrchestrator` ctor signature invariata; `ProcessQueuedPairsAsync` invariata
- Comportamento runtime: ispezioni non più saltate per `missing=[left]` su macchine MultiShot; log timing per ogni camera nei file NLog

---

## [2026-06-15] Fix: "Prec." mostra valori errati — _lastPersistedMultiShotConfig

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — aggiunto campo `_lastPersistedMultiShotConfig`; settato in `InitializeMachineRuntimeAsync` e `LoadConfiguration` PRIMA di `BuildRuntimeConfigurationFromView()`; aggiornato in `SaveConfiguration` e `PersistEncoderCalibration` dopo il salvataggio su disco; usato come `previousMultiShot` in `SaveConfiguration` al posto di `_machineConfiguration?.MachineMultiShotTrigger`

**Root cause:** il `_runtimePreviewApplyTimer` (debounce 450ms) chiama `_machineConfiguration = BuildRuntimeConfigurationFromView()` ogni volta che l'utente cambia un campo nell'editor, in modo da applicare i parametri al controller runtime in tempo reale. Di conseguenza, al momento del salvataggio, `_machineConfiguration.MachineMultiShotTrigger` conteneva già i valori **correnti** (modificati dall'utente) e non quelli precedenti. Il campo separato `_lastPersistedMultiShotConfig` viene aggiornato SOLO quando si scrive effettivamente su disco (XML), quindi riflette sempre l'ultimo stato persistito indipendentemente dalle variazioni in-memory del timer.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: "Prec.: X" ora mostra correttamente il valore che era salvato su disco PRIMA della modifica corrente

## [2026-06-15] Fix: FormatException binding int→TextBox nei campi MultiShot editor

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` — aggiunto `ValidatesOnExceptions=True` ai 5 TextBox con binding su proprietà `int` nel pannello MultiShot (MaxShots, Timeout, ShotCount, PulseMs, MinIntervalMs)

**Motivo:** quando il campo veniva svuotato completamente, WPF tentava di convertire `""` → `int` e lanciava FormatException nel log di binding. `ValidatesOnExceptions=True` fa intercettare l'eccezione al framework che mostra il bordo di validazione rosso senza propagare il valore vuoto alla sorgente.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: svuotare un campo int mostra il bordo rosso di validazione; il valore precedente rimane nella proprietà VM finché non si digita un numero valido

## [2026-06-15] Feature: storico parametri MultiShot in DB con visualizzazione "Prec." nell'editor

**File modificati:**
- `Models/MultiShotTrigger/MultiShotProfileSnapshot.cs` — nuova classe snapshot (POCO) con tutti i parametri editor come stringhe; factory methods `FromOptions` e `FromDictionary`; metodo `ToDictionary` per la persistenza DB
- `Database/MultiShotParamHistoryRepository.cs` — nuovo repository: `CreateTableIfNotExistsAsync`, `SaveSnapshotAsync` (UPSERT), `LoadAllAsync`; `OpenConnectionAsync` helper interno
- `Database/Cls_InitializzeDb.cs` — aggiunta chiamata `MultiShotParamHistoryRepository.CreateTableIfNotExistsAsync(conn)` nella migrazione schema
- `QtisVisionPanel.csproj` — aggiunti `<Compile Include>` per i due nuovi file
- `ViewModels/DigitalIOViewModel.cs` — aggiunto campo `_multiShotPreviousSnapshots`, property `CurrentMultiShotPreviousSnapshot`, notify in `SelectedMultiShotProfileKey` setter, chiamate `PersistMultiShotHistoryToDbAsync`+`RefreshMultiShotPreviousSnapshots` in `SaveConfiguration`, `LoadMultiShotHistoryFromDbAsync` in `LoadConfiguration`; using `QtisVisionPanel.Database` aggiunto
- `Views/UserControls/DigitalIOControl.xaml` — aggiunta TextBlock "Prec.: X" sotto ogni campo editabile MultiShot (MaxShots, Timeout, TriggerOutput, ShotCount, PulseMs, MinIntervalMs, StepMm, MmPerPixel); ogni TextBox avvolto in StackPanel, visibilità legata a `CurrentMultiShotPreviousSnapshot.HasValues`

**Motivo:** l'utente richiedeva la possibilità di vedere i valori precedenti direttamente nell'editor per poter ripristinare rapidamente i parametri senza dover ricordare o cercare i valori a tentativi dopo un cambio che non funziona.

**Impatto:**
- Config.xml: nessuno
- Database: nuova tabella `tbl_multishot_param_history` (profilo + param_name + prev_value + changed_at + changed_by); creata automaticamente alla prima avvio tramite `Cls_InitializzeDb`
- API pubblica: `DigitalIOViewModel.CurrentMultiShotPreviousSnapshot` (nuova property pubblica); `MultiShotProfileSnapshot` (nuova classe pubblica)
- Comportamento runtime: dopo ogni salvataggio configurazione, i valori precedenti di ciascun profilo (Side/Right/Bottom) vengono scritti nel DB e visualizzati sotto i campi nell'editor (grigio-azzurro, font 10pt). Prima dell'apertura del pannello la UI è vuota (nessun salvataggio precedente nel DB); dopo il primo salvataggio i "Prec.:" compaiono. Il caricamento dal DB è fire-and-forget; un fallimento MySQL non blocca il salvataggio XML.

## [2026-06-15] Fix: velocità encoder 250 m/min — delta-mode accumulation in ReadEncoderAsync

**File modificati:**
- `Models/AdvantechDeviceManager.cs` — rimossa la logica `_encoderReadAsDeltaMode` che si attivava quando il board restituiva `count=0` transitoriamente (VisionPro startup). Il codice precedente, dopo l'attivazione del flag, sommava ogni lettura cumulativa al valore accumulato invece di sostituirlo, causando crescita esponenziale di `CounterValue` (raw speed 322→816→1231→2054 m/min in ticks consecutivi). Il fix tratta `count=0` after non-zero come transiente board da ignorare silenziosamente.

**Causa confermata dai log (15/06/2026):**
Il fix della sessione precedente (warmup 3s sul primo sample) non bastava: il vero trigger era il transiente `count=0` emesso dalla board ~12 secondi dopo l'avvio durante l'inizializzazione VisionPro. Il reset hardware funzionava perché `ResetEncoderAsync` ripristinava `_encoderReadAsDeltaMode=false` (riga 1376) e azzerava `_lastEncoderRawSamples`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: la velocità encoder si stabilizza correttamente senza reset manuale dopo l'avvio con macchina in moto. I log mostreranno `ENCODER_TRANSIENT_ZERO` quando il transiente viene rilevato.

## [2026-06-15] Fix: _rearDisplaySaveRecord e _bottomDisplaySaveRecord mai scritti in UpdateDisplayParallelAsync

**File modificati:**
- `Services/CameraDisplayManager.cs` — aggiunti `MainWindow._rearDisplaySaveRecord = tmpRecord` e `MainWindow._bottomDisplaySaveRecord = tmpRecord` nei rami "rear" e "bottom" di `UpdateDisplayParallelAsync`, allineando il comportamento a top/top3d/side/left/front

**Motivo:** Per top, top2d, side, left, front il metodo `UpdateDisplayParallelAsync` assegnava il sub-record estratto al corrispondente campo `_*DisplaySaveRecord` (usato da `ISaveImage.GetSavedRecord` come source primaria per il salvataggio). Per rear e bottom i rami erano presenti ma mancava l'assegnazione — i campi restavano always-null, forzando `GetSavedRecord` a ricadere sempre su `_rearSaveRecord`/`_bottomSaveRecord` (il record grezzo dell'ispezione, meno preciso del sub-record del display).

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: il salvataggio immagini per Rear e Bottom usa ora il sub-record del display (stesso frame visualizzato sull'HMI), coerente con Top/Side/Front

## [2026-06-15] Feature: salvataggio immagini viste Rear e Bottom

**File modificati:**
- `Views/UserControls/DisplayRecord/RearCameraView.xaml.cs` — aggiunta property pubblica `ImageDisplay` che espone `CogRecordsDisplay1`
- `Views/UserControls/DisplayRecord/BottomCameraView.xaml.cs` — aggiunta property pubblica `ImageDisplay` che espone `CogRecordsDisplay1`
- `MainWindow.xaml.cs` — registrazione `_recordDisplays["R"]` e `_recordDisplays["B"]` in `Window_Loaded`; aggiunta sincronizzazione `_rearSaveRecord`, `_bottomSaveRecord` e relativi display-record in `SyncInspectionRecordsForSave`; cleanup in `ClearPendingVisionResults` e blocco shutdown
- `SaveImage/ISaveImage.cs` — estesi `GetSavedRecord` e `TryGetImageFromToolBlock` con i casi "R" (Rear) e "B" (Bottom)

**Motivo:** Le viste Rear e Bottom erano già implementate con display live delle immagini VisionPro, ma il sistema di salvataggio immagini (`_SaveImage`, `GetSavedRecord`, `TryGetImageFromToolBlock`) gestiva solo le viste T/S/F. Le immagini delle telecamere Rear e Bottom non venivano salvate durante le ispezioni.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `RearCameraView.ImageDisplay` e `BottomCameraView.ImageDisplay` aggiunte (property read-only)
- Comportamento runtime: le immagini Rear e Bottom vengono ora salvate nella cartella di archiviazione immagini durante ogni ciclo di ispezione, coerentemente con Top/Side/Front

## [2026-06-12] Fix: velocità encoder 250 m/min all'avvio — warmup spike filter non inizializzato

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — aggiunta inizializzazione `_encoderMetricWarmupUntilUtc[i]` nel branch "primo campione" di `UpdateEncoderMetrics()`; aggiunto log WARN `ENCODER_SPEED_CAP` quando il cap da 250 m/min viene raggiunto

**Causa confermata:**
La scheda Advantech emette un evento `CounterChanged` con `count=0` durante l'inizializzazione (stale board init — documentato nel codice stesso a riga 4766). Il primo tick del timer 100ms leggeva `CounterValue=0` e lo salvava come riferimento (`_lastEncoderCounts[i]=0`) impostando `_encoderMetricReferenceReady[i]=true`, ma **senza inizializzare** `_encoderMetricWarmupUntilUtc[i]` (che restava a `DateTime.MinValue`). Quando la scheda poi emetteva il contatore reale accumulato (es. 5.000.000 counts), il tick successivo calcolava un delta enorme → velocità astronomica → capped a 250 m/min visibili sul display. Il filtro spike (>120 m/min nei primi 3s) era inattivo perché `now <= DateTime.MinValue` è sempre falso.

**Fix:**
Aggiunta una sola riga nel branch "primo campione" (uguale a `ResetAllEncodersAsync`):
```csharp
_encoderMetricWarmupUntilUtc[i] = now.Add(TimeSpan.FromSeconds(EncoderStartupWarmupSeconds));
```
Ora all'avvio il filtro warmup è attivo per 3 secondi, esattamente come dopo un reset manuale. Il conteggio stale da board init viene scartato anziché generare una lettura falsa a 250 m/min. Il reset manuale non è più necessario.

**Secondo layer:** aggiunto `logger.Warn(ENCODER_SPEED_CAP|...)` quando il cap viene raggiunto, per diagnostica futura nei log NLog.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: all'avvio, la velocità encoder mostra 0 per ≤3s (warmup) poi la velocità reale misurata. Nessun impatto su regime stazionario. Il reset manuale encoder non è più necessario dopo un riavvio applicazione.

## [2026-06-12] Fix: popup VisionPro ripetuto all'avvio — logica "intervento manuale richiesto"

**File modificati:**
- `MainWindow.xaml.cs` — aggiunto campo `_visionManualInterventionRequired`, guard in `AttemptVisionAutoRecoveryAsync`, reset in `runVisionPro()`

**Motivo:** Quando VisionPro non riesce ad avviarsi, il watchdog (timer 1500 ms) tentava il recovery ogni 8 secondi. Dopo 3 tentativi falliti mostrava un popup, poi resettava il contatore a 0 e ricominciava il ciclo: il popup riappariva ogni ~24 secondi indefinitamente fino alla chiusura dell'app. L'operatore non riusciva a chiudere tutti i popup prima che ne apparissero altri.

**Fix applicato:**
1. `_visionManualInterventionRequired` (volatile bool): latch che si attiva quando il popup viene mostrato per la prima volta.
2. Guard all'inizio di `AttemptVisionAutoRecoveryAsync`: se il latch è attivo, ritorna immediatamente senza recovery e senza popup.
3. Il popup appare esattamente una volta (alla terza recovery fallita); dopo di ché il hold-reason indicator nella top bar mostra lo stato di errore in corso.
4. Il latch si azzera in `runVisionPro()` (start manuale o cambio ricetta) e quando il recovery riesce, permettendo al watchdog di riprendere normalmente.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: popup VisionPro mostrato al massimo una volta per sequenza di errore; notifica successiva tramite top-bar hold-reason (già esistente)

## [2026-06-11] Consolidamento produzione — Fase 3: OPC UA health nodes + recipe validation

**File modificati:**
- `Services/IntegratedAlarmCardService.cs` — aggiunto `GetFirstTriggeredAlarmCode()`: espone il nome del primo allarme attivo per la pubblicazione OPC UA
- `Services/OpcUaClientService.cs` — aggiunto `WriteHealthSnapshotAsync(systemHealthStatus, lastAlarmCode, activeHoldReasons)`: pubblica i 3 nodi di stato macchina read-only (`SystemHealthStatus`, `LastAlarmCode`, `ActiveHoldReasons`)
- `MainWindow.xaml.cs` — chiamata a `WriteHealthSnapshotAsync` aggiunta nel ciclo snapshot ispezione; aggiunto `ValidateRecipeForMachine()` e chiamata in `ChangeRecipeFromOpcUaAsync` prima di applicare la ricetta OPC UA

**Motivo:**
- F3-A: SCADA/MES potevano sapere solo se la macchina era in Run ma non se era in stato degradato (camera mancante, allarme attivo, hold reasons). I 3 nodi read-only danno visibilità sullo stato reale senza modificare la logica di ispezione.
- F3-B: Un cambio ricetta OPC UA poteva caricare una ricetta con `BottomSealing=true` su una macchina senza job Bottom caricato in VisionPro. La validazione pre-apply blocca il cambio con `CommandAckOk=false` e un messaggio esplicito.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- OPC UA: 3 nuovi nodi read-only `SystemHealthStatus` (string), `LastAlarmCode` (string), `ActiveHoldReasons` (string) — additivi, non rompono client esistenti
- API pubblica: `IntegratedAlarmCardService.GetFirstTriggeredAlarmCode()` (additiva), `OpcUaClientService.WriteHealthSnapshotAsync()` (additiva)
- Comportamento runtime: il cambio ricetta OPC UA con camera Bottom abilitata in ricetta ma job Bottom non caricato ora restituisce `CommandAckOk=false` invece di applicare una ricetta incompatibile

## [2026-06-11] Consolidamento produzione — Fase 2: InspectionOrchestrator estratto

**File modificati:**
- `Services/InspectionOrchestrator.cs` — NUOVO FILE: gestione code multi-camera (Top/Side/Front/Rear/Bottom), attesa companion results, dispatch al pipeline di ispezione tramite delegate
- `MainWindow.xaml.cs` — rimosso il corpo di `ProcessQueuedPairAsync` (65 righe → 1 riga di delega), rimossi `WaitForExpectedCompanionResultsAsync` e `RoleQueueHasResult` ora nell'orchestrator; aggiunti `_inspectionOrchestrator` field e `EnsureInspectionOrchestratorInitialized()`
- `Models/CameraResult.cs` — NUOVO FILE: `CameraResult` promosso da class privata annidata in MainWindow a `internal sealed class` standalone
- `QtisVisionPanel.csproj` — registrati `Models\CameraResult.cs` e `Services\InspectionOrchestrator.cs`

**Motivo:** `ProcessQueuedPairAsync` era un metodo centrale non testabile in MainWindow (~65 righe). L'estrazione in `InspectionOrchestrator` con delegate injection mantiene il comportamento identico e prepara il terreno per testare la logica di accoppiamento Top+companions isolatamente.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `InspectionOrchestrator.ProcessQueuedPairsAsync(CancellationToken)` — nuovo tipo interno
- Comportamento runtime: identico — il semaforo, le code e la logica di timeout sono gli stessi

## [2026-06-11] Release 3.0.0.5 — Commenti architetturali e documentazione

**File modificati:**
- `Database/Cls_InitializzeDb.cs` — aggiunto commento di classe che descrive il pattern di migrazione incrementale schema e la safe-by-default approach (nullable columns, idempotent methods)
- `Database/AlarmCardRepository.cs` — commento di classe riscritto per documentare il pattern dual-storage MySQL+XML e la relazione con `IntegratedAlarmCardService` per la risoluzione del canale fisico
- `ViewModels/DigitalIOViewModel.cs` — aggiunto commento di classe che descrive i tre domini gestiti (I/O fisico, encoder/MultiShot, alarm output) e la relazione con `machine_runtime_config.xml`
- `Services/PowerFlex525Service.cs` — aggiunto commento di classe che descrive la strategia best-effort connectivity e il retry-once pattern per evitare flooding del log
- `Documentation/MachineHardware/architecture-and-modules.md` — aggiornata ultima revisione, aggiunta nota consolidamento 2026-06-11, chiusa issue C3, aggiornato stato A3 (parziale), aggiornati metodi `IToolBlockValidator`, aggiornato piano refactoring Fase 3
- `Documentation/MachineHardware/software-version-archive.md` — aggiunta release `3.0.0.5`
- `Properties/AssemblyInfo.cs` — versione aggiornata a `3.0.0.5`

**Motivo:** Passata sistematica di commentatura sulle aree prioritarie identificate nella guida `source-code-commenting-guide-2026-05-28.md`. I commenti aggiungono il "perche'" su pattern non ovvi (dual-storage, best-effort connectivity, migrazione incrementale schema) senza descrivere cosa fa il codice.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: nessuno

## [2026-06-11] Consolidamento produzione — Fase 1: audit trail completo + cache autorizzazioni

**File modificati:**
- `Database/CounterManager.cs` — aggiunto audit `COUNTER_RESET_SHIFT` in `ResetForShiftAsync` dopo il reset contatori
- `ViewModels/EjectionAlarmCardViewModel.cs` — aggiunto audit `ALARM_CARD_SAVE` in `SaveConfigurationAsync` dopo salvataggio riuscito
- `ViewModels/DigitalIOViewModel.cs` — aggiunto audit `MACHINE_CONFIG_SAVE` in `SaveConfiguration` dopo `MarkConfigurationSaved()`
- `Services/AuthorizationService.cs` — aggiunto metodo pubblico `InvalidateCache()` che svuota `_roleFeaturesCache` e azzera `_lastCacheUpdate`
- `ViewModels/TopMenuBarViewModel.cs` — chiamata a `ServiceLocator.Authorization?.InvalidateCache()` dopo login riuscito e dopo logout, così il prossimo controllo permessi ricarica sempre dal DB

**Motivo:** Completamento del piano di consolidamento Fase 1.
- F1-A: `AuditLogService` tracciava solo login/logout; ora ogni azione operativa rilevante (reset turno, salvataggio config I/O, salvataggio allarmi scarto) produce una riga nella tabella `audit_log` con attore, evento e dettagli.
- F1-B: La cache permessi a 5 minuti manteneva un ruolo revocato attivo fino alla scadenza; ora l'invalidazione è immediata a ogni cambio di sessione utente.

**Impatto:**
- Config.xml: nessuno
- Database: nuove righe nella tabella `audit_log` per eventi `COUNTER_RESET_SHIFT`, `ALARM_CARD_SAVE`, `MACHINE_CONFIG_SAVE` (tabella già esistente)
- API pubblica: `AuthorizationService` aggiunge `InvalidateCache()` (additive)
- Comportamento runtime: i permessi vengono ricaricati dal DB immediatamente dopo ogni login/logout invece di aspettare fino a 5 minuti

## [2026-06-11] Consolidamento produzione — Fase 0: eccezioni async + race condition + audit parziale

**File modificati:**
- `ViewModels/EjectionAlarmCardViewModel.cs` — aggiunto try/catch con log NLog a 4 metodi `async void` (`AddNewAlarm`, `RemoveSelectedAlarm`, `DuplicateSelectedAlarm`, `AddNewAlarmOfType`)
- `ViewModels/RecipeManagerViewModel.cs` — eliminati 2 catch silenziosi (`UpdatePermissionsFromStaticRoles`, `CheckDatabaseAsync`); aggiunti audit `RECIPE_SAVE`, `RECIPE_LOAD_PRODUCTION`, `RECIPE_DELETE`
- `MainWindow.xaml.cs` — `_isContinuousRunActive` convertito da campo statico a property che delega a `MachineRuntimeService` (zero modifiche ai call site); aggiunto audit `COUNTER_RESET_ALL` dopo reset contatori globale
- `ViewModels/MainViewModel.cs` — 3 catch `ObjectDisposedException {}` silenti ora loggano a Debug level
- `ViewModels/InspectionCounterViewModel.cs` — aggiunto audit `COUNTER_RESET_ALL` dopo `ResetAllCountersAsync`

**Motivo:** Eliminare le 3 criticità verificate nel codice che impattavano sicurezza e tracciabilità in produzione: (1) eccezioni async non gestite che andavano al SynchronizationContext senza traccia NLog; (2) race condition C3 — doppio stato `_isContinuousRunActive` (MainWindow public static vs MachineRuntimeService lock-protected) con rischio divergenza su pipeline 5-camere; (3) assenza di audit trail per azioni operative critiche.

**Impatto:**
- Config.xml: nessuno
- Database: nuove righe in `audit_log` per `RECIPE_SAVE`, `RECIPE_LOAD_PRODUCTION`, `RECIPE_DELETE`, `COUNTER_RESET_ALL`
- API pubblica: `MainWindow._isContinuousRunActive` rimane pubblicamente accessibile ma ora è una property (breaking solo se qualcuno prendeva l'indirizzo del campo — non applicabile in C# managed)
- Comportamento runtime: le eccezioni nei command handler alarm ora appaiono in NLog invece di crashare silenziosamente

## [2026-06-10] Versione 3.0.0.4 - Stabilizzazione velocita encoder all'avvio e tachimetro persistente

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - aggiunta sincronizzazione iniziale della misura velocita encoder: il primo campione dopo auto-start/re-arm non genera velocita; durante il warmup un salto non plausibile viene scartato invece di pubblicare `250 m/min`
- `Models/MachineRuntimeConfiguration.cs` / `Services/MachineConfigurationService.cs` - aggiunto campo opzionale `RuntimeBindings/EncoderCalibrationTachometerSpeedMetersPerMinute` con fallback compatibile a `40.0`
- `ConfigurationTemplates/machine_runtime_config.template.xml` - template aggiornato con il nuovo campo tachimetro persistente
- `Localization/messages_eng.json` / `Localization/messages_ita.json` / `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunta label diagnostica per riferimento encoder sincronizzato all'avvio
- `Properties/AssemblyInfo.cs` / `Documentation/MachineHardware/software-version-archive.md` - release aggiornata a `3.0.0.4`

**Motivo:** In alcuni avvii la PCIE-1884 riportava un contatore gia avanzato prima che il riferimento velocita HMI fosse allineato. Il delta iniziale veniva interpretato come velocita reale e saturava il display a `250 m/min`; premendo `Reset` il riferimento veniva riallineato manualmente. Inoltre il campo `Tachometer speed` era inizializzato sempre a `40.0` e non veniva ricaricato dal file macchina.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nuovo campo opzionale `EncoderCalibrationTachometerSpeedMetersPerMinute`; file esistenti compatibili con fallback `40.0`
- Ricette: nessuna modifica
- Database: nessuna modifica
- Runtime: la velocita encoder pubblicata nel top menu non usa piu il primo salto contatore di startup; la taratura mm/rev resta invariata
- Release software: `3.0.0.4`

## [2026-06-10] Versione 3.0.0.3 - Bottom dedicata a saldatura inferiore e carta intrappolata

**File modificati:**
- `DataManage/IToolBlockValidator.cs` - `GetDetailedBottomValidationAsync` sostituisce la validazione top-like con due controlli booleani dedicati: `BottomSealing` e `TrappedPaper`; lettura booleani VisionPro resa robusta per `OK/NOK/KO/FAIL/NOREAD`
- `DataManage/InspectionConfigService.cs` - Bottom non abilita piu `Logo`, `PrintCentering`, `OpenFlaps`, `SurfaceCheck`, `ShapeTop`; aggiunte feature runtime `BottomSealing` e `TrappedPaper`
- `MainWindow.xaml.cs` - mappatura difetti/scarto aggiornata per i due nuovi difetti Bottom; camera mancante Bottom marca entrambi i controlli come critici
- `Database/CounterManager.cs` / `Database/Cls_InitializzeDb.cs` / `Database/ProductionRecord/ProduzioneRecord.cs` / `Database/ProductionAnalyticsRepository.cs` - aggiunti contatori, colonne `NC_BottomSealing` / `NC_TrappedPaper` e distribuzione Data Analysis
- `Models/EjectionAlarmConfig.cs` / `Models/SelectableDefect.cs` - aggiunti enum difetti e label operatore
- `ViewModels/RecipeManagerViewModel.cs` / `ViewModels/InspectionConfigViewModel.cs` / `ViewModels/InspectionCounterViewModel.cs` / `ViewModels/EjectionAlarmCardViewModel.cs` - UI ricetta, configurazione, contatori e allarmi scarto allineate ai nuovi difetti
- `Services/IntegratedAlarmCardService.cs` / `Services/CameraDisplayManager.cs` - allarmi e indicatori camera Bottom aggiornati
- `Views/UserControls/DisplayRecord/BottomCameraView.xaml` / `.xaml.cs` - rimosse icone top-like, aggiunti indicatori dedicati BottomSealing/TrappedPaper
- `ServerMessage/*`, `Localization/*`, `scripts/UpdateRuntimeLanguageFiles.ps1` - label lingua aggiornate
- `Docs/Manual/08_MultiShot_Right_Bottom.md` e `Documentation/MachineHardware/right-bottom-multishot-expansion-2026-06-09.md` - documentazione operativa aggiornata
- `Properties/AssemblyInfo.cs` / `Documentation/MachineHardware/software-version-archive.md` - release aggiornata a `3.0.0.3`

**Motivo:** La camera Bottom controlla saldatura inferiore e carta intrappolata nella saldatura, non le feature della Top. Il vecchio modello poteva rendere poco chiaro il motivo NOK e misclassificare gli scarti Bottom.

**Impatto:**
- Config.xml: nessuna nuova chiave
- Ricette: aggiunte flag XML compatibili `BottomSealing` e `TrappedPaper` in `inspectionStatus` / `ejectionStatus`
- VisionPro: job `Bottom` deve esporre output booleani compatibili (`BottomSealingOk`, `NoTrappedPaper` consigliati)
- Database: aggiunte colonne opzionali `NC_BottomSealing`, `NC_TrappedPaper` e contatori globali dedicati
- Runtime: se un controllo Bottom e abilitato ma l'output manca, il pezzo viene marcato NOK
- Release software: `3.0.0.3`

## [2026-06-10] Versione 3.0.0.2 - Aggregazione esito multi-camera Left/Right/Bottom

**File modificati:**
- `MainWindow.xaml.cs` - il pairing risultati VisionPro passa da Top + una sola secondaria a Top + camere companion attese (`Side/Left`, `Front`, `Right/Rear`, `Bottom`); aggiunte code dedicate Rear/Bottom, timeout camera mancante e difetto tecnico `CameraMissing`
- `DataManage/InspectionProcessor.cs` - `InspectionResult` esteso con `RearResult`, `BottomResult`, risultati camera mancante e calcolo `IsValid` aggregato
- `DataManage/IToolBlockValidator.cs` - aggiunti validatori `GetDetailedRearValidationAsync` e `GetDetailedBottomValidationAsync`
- `DataManage/InspectionConfigService.cs` - feature runtime aggiornate per includere `Rear` e `Bottom`
- `Services/IntegratedAlarmCardService.cs` - estrazione difetti estesa a `RearResult`, `BottomResult` e timeout camera
- `ViewModels/EjectionAlarmCardViewModel.cs` - mappatura allarmi scarto aggiornata per i nuovi risultati
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.0.2`
- `Documentation/MachineHardware/right-bottom-multishot-expansion-2026-06-09.md` - documentata integrazione esito finale multi-camera
- `Docs/Manual/08_MultiShot_Right_Bottom.md` - nota operativa aggiornata: Right/Rear e Bottom partecipano al NOK finale
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.0.2`

**Motivo:** La release `3.0.0.1` configurava, triggerava e visualizzava `Right/Rear` e `Bottom`, ma il calcolo finale NOK/scarto restava limitato alla baseline Top + una secondaria. Ora le camere caricate nel job attivo partecipano alla decisione finale del pezzo.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna nuova sezione; restano usati i profili `MachineMultiShotTrigger` gia introdotti
- VisionPro: `Right/Rear` deve esporre un output di saldatura laterale compatibile; `Bottom` deve esporre gli output dei controlli che deve eseguire
- Database: nessuna modifica schema; i record usano l'esito aggregato multi-camera
- Runtime: se una camera prevista non produce risultato entro timeout, il pezzo viene marcato NOK e puo comandare scarto
- Release software: `3.0.0.2`

## [2026-06-09] Versione 3.0.0.1 - Fondazione MultiShot multi-profilo per Left/Right/Bottom

**File modificati:**
- `Models/MultiShotTrigger/MultiShotTriggerModels.cs` - `MachineMultiShotTriggerConfiguration` estesa con profili `Side`, `Right`, `Bottom`; aggiunti helper di risoluzione ruolo runtime
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - controller reso multi-profilo, con sessioni indipendenti e log `MULTISHOT_<PROFILE>_*`
- `Services/MultiShotTrigger/MultiShotTriggerPlanBuilder.cs` - messaggi errore resi generici per profilo camera
- `Services/MultiShotTrigger/VisionProStitchingParameterApplier.cs` - applicazione parametri stitching basata sul profilo compatibile con il ruolo runtime
- `Services/MachineConfigurationService.cs` - default e riallineamento schema per `Right` e `Bottom`
- `Models/CameraModel.cs` - normalizzazione `Right/SideRight` verso ruolo runtime `rear`
- `ViewModels/DigitalIOViewModel.cs` - selettore profilo MultiShot HMI, salvataggio separato `Side`/`Right`/`Bottom`, mapping trigger aggiuntivi
- `Views/UserControls/DigitalIOControl.xaml` - aggiunto selettore profilo nella card MultiShot
- `MainWindow.xaml.cs` - filtro risultati intermedi `ImageStitching.isReady` esteso ai profili MultiShot abilitati; visualizzazione runtime `Rear` e `Bottom`
- `Services/CameraDisplayManager.cs` - routing display per `RearCameraView` e `BottomCameraView`
- `ConfigurationTemplates/machine_runtime_config.template.xml` - aggiunti output/punti intervento e nodi MultiShot `Right`/`Bottom`
- `Localization/messages_eng.json` / `Localization/messages_ita.json` / `scripts/UpdateRuntimeLanguageFiles.ps1` - label HMI MultiShot multi-profilo aggiornate
- `Docs/Manual/08_MultiShot_Right_Bottom.md` - manuale operativo per configurare Left/Right/Bottom MultiShot
- `Documentation/MachineHardware/right-bottom-multishot-expansion-2026-06-09.md` - documentata implementazione 3.0.0.1 e passaggi commissioning
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.0.1`
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.0.1`

**Motivo:** Iniziare l'estensione multi-camera richiesta mantenendo la baseline stabile: Right usa la vista Rear, Bottom usa la vista gia esistente, e MultiShot diventa configurabile per profilo macchina senza duplicare campi in ricetta.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: aggiunti nodi opzionali `MachineMultiShotTrigger/Right` e `MachineMultiShotTrigger/Bottom`, output `OUT_CAMERA_REAR_TRIGGER` / `OUT_CAMERA_BOTTOM_TRIGGER` e punti `CAMERA_TRIGGER_REAR` / `CAMERA_TRIGGER_BOTTOM`
- VisionPro: i job compatibili ricevono `expectedFrames`, `stepMm`, `mmPerPixel` quando il profilo MultiShot e' abilitato; resta richiesto `ImageStitching.isReady=true` sul risultato finale
- Database: nessuna modifica
- Runtime: supporto trigger/stitching/display per Right/Rear e Bottom; il calcolo NOK multi-camera completo resta demandato allo step successivo
- Release software: `3.0.0.1`

## [2026-06-09] Versione 3.0.0.0 - Apertura major release per estensione Right/Bottom MultiShot

**File modificati:**
- `Properties/AssemblyInfo.cs` - release aggiornata a `3.0.0.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `3.0.0.0` con riferimento al backup `2.0.2.5`
- `Documentation/MachineHardware/right-bottom-multishot-expansion-2026-06-09.md` - aggiunta nota tecnica di perimetro per Right, Bottom e profili macchina Vision/Quatis

**Backup creato:**
- `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\SoftwareArchives\QtisVisionPanel_2.0.2.5_pre_right_bottom_multishot_20260609_134704.zip`

**Motivo:** Aprire una nuova major release prima di implementare i prossimi task: camera `Right` come ruolo complementare alla vista `Rear`, supporto `Bottom` single-shot/MultiShot, e profili macchina `Vision` / `Quatis` con numero camere variabile.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema in questa release
- VisionPro: nessuna modifica in questa release
- Database: nessuna modifica
- Runtime: nessun comportamento camera nuovo ancora introdotto
- Release software: `3.0.0.0`

## [2026-06-09] Versione 2.0.2.5 - Offset Left MultiShot e intervallo minimo tra scatti

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - `First shot offset` risolto da `CAMERA_TRIGGER_LEFT` con fallback `CAMERA_TRIGGER_SIDE`; aggiunto binding HMI `SideLeftMultiShotMinimumInterShotIntervalMs`
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - aggiunta guardia runtime che attende il tempo minimo tra due fronti MultiShot consecutivi
- `Services/MultiShotTrigger/MultiShotTriggerPlanBuilder.cs` / `Models/MultiShotTrigger/MultiShotTriggerModels.cs` - aggiunto parametro macchina `MinimumInterShotIntervalMs`
- `Views/UserControls/DigitalIOControl.xaml` - aggiunto campo `Tempo minimo tra scatti` nella card Side/Left MultiShot
- `ConfigurationTemplates/machine_runtime_config.template.xml` - aggiunto default `MinimumInterShotIntervalMs=30`
- `Localization/messages_eng.json` / `Localization/messages_ita.json` / `scripts/UpdateRuntimeLanguageFiles.ps1` - label e riepiloghi MultiShot aggiornati
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.2.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.2.5`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - documentato offset Left e tuning trigger camera

**Motivo:** In macchina il pannello poteva mostrare/generare il MultiShot usando ancora la quota `CAMERA_TRIGGER_SIDE`, mentre l'operatore stava regolando il trim del punto `CAMERA_TRIGGER_LEFT`. Inoltre i log mostravano 10 impulsi DO generati, ma la camera contava meno frame: con impulsi troppo brevi o troppo ravvicinati l'ingresso camera puo ignorare alcuni fronti anche se la scheda li ha commutati.

**Impatto:**
- Machine runtime config: aggiunto nodo opzionale `MachineMultiShotTrigger/Side/MinimumInterShotIntervalMs`, default `30 ms`
- Config.xml principale: nessuna modifica
- VisionPro: nessuna modifica; continua a ricevere N trigger e deve pubblicare il risultato finale stitched
- Database: nessuna modifica
- Runtime: `Trim +/- mm` su `CAMERA_TRIGGER_LEFT` influenza ora il primo scatto Left; gli impulsi MultiShot non vengono piu emessi sotto l'intervallo minimo configurato
- Release software: `2.0.2.5`

## [2026-06-08] Versione 2.0.2.4 - Watchdog VisionPro compatibile con Left MultiShot

**File modificati:**
- `MainWindow.xaml.cs` - aggiunto timeout watchdog dedicato `VisionMultiShotResultTimeout=30s`; quando Side/Left MultiShot e' attivo il recovery non parte piu per semplice attesa/desync Top-Left o pair processing stalled
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.2.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.2.4`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - documentata la grace watchdog per stitching MultiShot

**Motivo:** Con MultiShot il risultato Left arriva in ritardo per progetto: VisionPro deve acquisire N frame, fare stitching e pubblicare solo il risultato finale. Il watchdog a 5 secondi interpretava l'attesa come blocco e avviava recovery automatico durante produzione.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- VisionPro: nessuna modifica; resta richiesto `ImageStitching.isReady=true` sul risultato finale
- Database: nessuna modifica
- Runtime: meno recovery automatici falsi durante MultiShot; recovery ancora attivo per RunContinuous realmente fermo, UserResult vuoti ripetuti o RunStatus error ripetuti
- Release software: `2.0.2.4`

## [2026-06-08] Versione 2.0.2.3 - MultiShot late come diagnostica non bloccante

**File modificati:**
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` non cancella piu la sessione; la sequenza continua fino a `ShotCount`
- `Models/AdvantechDeviceManager.cs` - polling hardware I/O/encoder ridotto da `50 ms` a `10 ms` per diminuire l'overshoot encoder durante MultiShot
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.2.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.2.3`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - aggiornata diagnosi late e nota su polling encoder

**Motivo:** Nei log macchina il MultiShot continuava a fermarsi al secondo/quarto scatto con overshoot appena sopra soglia (`544 > 500`, `661 > 600`, `672 > 600`). Il polling encoder software non puo garantire target stretti a step piccoli e velocita elevate; il filtro era quindi ancora troppo forte per l'uso reale.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema rispetto a `2.0.2.2`
- VisionPro: nessuna modifica; continua a ricevere gli impulsi e deve pubblicare il risultato finale con `isReady=true`
- Database: nessuna modifica
- Runtime: MultiShot non viene piu annullato dai ritardi di polling; `TARGET_TOO_LATE` resta warning per collaudo e tuning
- Release software: `2.0.2.3`

## [2026-06-08] Versione 2.0.2.2 - Tolleranza late MultiShot Side/Left su polling reale

**File modificati:**
- `Models/MultiShotTrigger/MultiShotTriggerModels.cs` - aggiunto `TargetLateTolerancePulses` alle opzioni macchina e al piano runtime MultiShot
- `Services/MultiShotTrigger/MultiShotTriggerPlanBuilder.cs` - copia la tolleranza late nel piano sessione
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` usa `max(StepPulses, TargetLateTolerancePulses)`; il log include `overshoot` e `allowed`; dopo un impulso non rivaluta lo stesso sample encoder gia usato
- `Services/MachineConfigurationService.cs` - default retrocompatibile `TargetLateTolerancePulses=500` quando il nodo manca o vale zero
- `ViewModels/DigitalIOViewModel.cs` - preserva la tolleranza esistente al salvataggio configurazione dalla HMI
- `ConfigurationTemplates/machine_runtime_config.template.xml` - aggiunto `TargetLateTolerancePulses`
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.2.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.2.2`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - documentata la nuova tolleranza late

**Motivo:** Nei log macchina il MultiShot veniva cancellato al primo/secondo frame con overshoot di circa `164..361` impulsi. La soglia precedente `StepPulses/2` era troppo stretta rispetto al polling encoder reale e faceva scattare `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` anche in condizioni operative normali.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: aggiunto nodo opzionale `MachineMultiShotTrigger/Side/TargetLateTolerancePulses`, default `500`
- VisionPro: nessuna modifica
- Database: nessuna modifica
- Runtime: meno cancellazioni premature; la protezione resta attiva quando il target e' superato oltre la tolleranza ammessa
- Release software: `2.0.2.2`

## [2026-06-08] Versione 2.0.2.1 - MultiShot Side/Left senza catch-up burst

**File modificati:**
- `Models/MultiShotTrigger/MultiShotTriggerModels.cs` - aggiunto stato sessione `LastObservedEncoderCount`, `LastPulseStartedAt`, `PulseInFlight` e nuovo evento `TargetTooLate`
- `Services/MultiShotTrigger/MultiShotTriggerController.cs` - sostituito il recupero con loop/catch-up con una macchina stati a singolo impulso; il target successivo viene valutato solo dopo la fine dell'impulso precedente
- `Services/MultiShotTrigger/VisionProStitchingParameterApplier.cs` - ricerca ricorsiva del ToolBlock `ImageStitching` dentro ToolBlock/Gruppi annidati
- `MainWindow.xaml.cs` - il filtro `ImageStitching.isReady` usa la ricerca ricorsiva, cosi funziona anche con job VisionPro annidati
- `ViewModels/DigitalIOViewModel.cs` - evento `TargetTooLate` visualizzato come warning diagnostico nella pagina I/O
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.2.1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.2.1`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - documentata la protezione anti raffica e la diagnostica `MULTISHOT_SIDELEFT_TARGET_TOO_LATE`

**Motivo:** Se il polling encoder arrivava quando piu target erano gia stati superati, il controller poteva recuperare piu scatti nello stesso ciclo. Questo e' pericoloso per lo stitching VisionPro perche' i frame non rappresentano piu posizioni encoder distanziate correttamente.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema
- VisionPro: la HMI continua a scrivere `expectedFrames`, `stepMm`, `mmPerPixel`; ora trova `ImageStitching` anche se annidato
- Database: nessuna modifica
- Runtime: un solo impulso MultiShot puo essere in volo; se il target successivo e' troppo in ritardo la sessione viene annullata e loggata con `MULTISHOT_SIDELEFT_TARGET_TOO_LATE`
- Release software: `2.0.2.1`

## [2026-06-08] Versione 2.0.2.0 - Filtro risultati intermedi VisionPro Left MultiShot

**File modificati:**
- `MainWindow.xaml.cs` - prima di accodare risultati `Left` con MultiShot attivo legge `ImageStitching.isReady`; ignora i risultati intermedi con `isReady=false` e accetta solo il risultato finale `isReady=true`
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.2.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.2.0`

**Motivo:** Nel log allegato il risultato `LEFT` veniva accodato dopo lo scatto `4/12` (`LEFT result queued on Side queue`) e la HMI processava/salvava il pezzo prima che gli scatti `6/12..12/12` fossero completati. Questo produceva immagini spezzate e scarti apparentemente casuali perche' lo stitching non era ancora finale.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema
- VisionPro: il ToolBlock `ImageStitching` deve esporre `isReady`; `frameIndex` e `status` vengono letti solo per diagnostica
- Database: nessuna modifica
- Runtime: i frame intermedi non aggiornano contatori, allarmi, salvataggio immagini o scarto; solo il frame stitched finale entra nella coda Top/Left
- Release software: `2.0.2.0`

## [2026-06-08] Versione 2.0.1.9 - MultiShot Side/Left guidato da quota camera e step mm

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - calcolo automatico `InitialOffsetPulses` da `CAMERA_TRIGGER_SIDE` e counts/mm del main encoder; calcolo `StepPulses` da `VisionProStitching.StepMm`; refresh live dei parametri VisionPro dopo preview/salvataggio/caricamento/taratura encoder
- `Views/UserControls/DigitalIOControl.xaml` - campi impulsi MultiShot resi read-only/calcolati e aggiunto riepilogo geometria quota camera/step/counts-mm
- `MainWindow.xaml.cs` - `ApplyVisionProStitchingParametersForLeftJobs` esposto con configurazione runtime opzionale, cosi la pagina I/O puo applicare parametri al job gia caricato
- `Localization/messages_eng.json` / `Localization/messages_ita.json` / `scripts/UpdateRuntimeLanguageFiles.ps1` - messaggi MultiShot aggiornati per la configurazione guidata
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - aggiornata procedura operativa MultiShot
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.9`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.9`

**Motivo:** Con `Shots=1`, `InitialOffsetPulses=10` e `StepPulses=10`, il controller generava un solo impulso quasi subito dopo la fotocellula. L'aspettativa macchina e' invece fotocellula -> conteggio encoder fino alla quota camera -> N impulsi alla distanza reale di stitching.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; i campi esistenti `InitialOffsetPulses` e `StepPulses` vengono salvati come valori derivati dalla quota `CAMERA_TRIGGER_SIDE`, dallo step mm e dalla taratura encoder
- VisionPro: `expectedFrames`, `stepMm` e `mmPerPixel` vengono applicati anche dopo modifiche HMI se il job Left e' gia caricato
- Database: nessuna modifica
- Runtime: il controller MultiShot resta encoder-driven e continua a non fare stitching in HMI
- Release software: `2.0.1.9`

## [2026-06-08] Versione 2.0.1.8 - Taratura tachimetro encoder con fattori grandi

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - range accettato del fattore taratura encoder esteso da `0.05..20` a `0.001..1000`
- `Localization/messages_eng.json` / `Localization/messages_ita.json` - testo warning aggiornato al nuovo range
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.8`

**Motivo:** Con una configurazione gia molto fuori scala, per esempio `mm/rev=5.885` e velocita reale `44.7 m/min` contro HMI `0.26 m/min`, il fattore corretivo richiesto e circa `173x`. Il blocco precedente a `20x` impediva di applicare la correzione anche se il tachimetro era valido.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; viene aggiornato il campo esistente `EncoderTemplates/MillimetersPerRevolution`
- Database: nessuna modifica
- Runtime: nessuna modifica alla formula di velocita; cambia solo la possibilita di applicare tarature grandi in commissioning
- Release software: `2.0.1.8`

## [2026-06-08] Versione 2.0.1.7 - Encoder startup ErrorFuncBusy declassato a transitorio

**File modificati:**
- `Models/AdvantechDeviceManager.cs` - avvio encoder idempotente, retry breve su `ErrorFuncBusy`, lettura busy gestita mantenendo ultimo conteggio valido
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.7`

**Motivo:** All'avvio automatico l'HMI poteva chiedere alla PCIE-1884 di abilitare/leggere il contatore encoder mentre la libreria Advantech era ancora occupata. Il messaggio `Errore nell'avvio dell'encoder 0: ErrorFuncBusy` veniva registrato come errore applicativo pur essendo spesso uno stato transitorio.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Database: nessuna modifica
- Runtime: l'encoder resta configurato per il polling; se la board e' temporaneamente busy viene usato l'ultimo conteggio disponibile e il ciclo successivo riprova
- Log eventi: `ErrorFuncBusy` di startup/lettura encoder non genera piu evento `ERROR/WARN`
- Release software: `2.0.1.7`

## [2026-06-08] Versione 2.0.1.6 - QuickBuild Left visibile nella overview

**File modificati:**
- `ViewModels/CameraContainerViewModel.cs` - aggiunto `left` ai ruoli camera ammessi dalla lista viste runtime
- `ViewModels/CameraTemplateSelector.cs` - il ruolo `left` usa il template `SideCameraView`
- `Services/CameraDisplayManager.cs` - aggiornamento display e label coerenti per runtime role `left`
- `MainWindow.xaml.cs` - propagato il ruolo Side/Left all'aggiornamento display secondario
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.6`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.6`

**Motivo:** Un VPP con job `Top` e `Left` veniva caricato, ma la overview mostrava solo Top perche la UI filtrava `left` dai ruoli visualizzabili e il display secondario veniva forzato come `side`.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Database: nessuna modifica
- Runtime: il job `Left` continua a essere gestito come secondaria nella pipeline Side/Left
- UI: la overview mostra anche la vista Left usando la view laterale esistente
- Release software: `2.0.1.6`

## [2026-06-05] Versione 2.0.1.5 - Riduzione rumore nello storico eventi

**File modificati:**
- `DataManage/InspectionProcessor.cs` - esiti, misure e motivi di scarto spostati da `INFO/WARN` a diagnostica `DEBUG`; errori di esecuzione reali restano `ERROR`
- `Services/NLogEventBridge.cs` - esclusi da `tbllogevent` i messaggi operativi attesi: validazioni prodotto, cancellazioni durante stop/cambio vista e cleanup marcati `non-critical`
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.5`

**Motivo:** I normali prodotti scartati generavano un warning tecnico per ogni ispezione e venivano duplicati nello storico eventi, pur essendo gia rappresentati nei contatori difetto, nei messaggi UI e negli allarmi configurati. Questo rendeva piu difficile individuare guasti macchina reali.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Database: nessuna modifica schema; diminuisce il numero di nuove righe non operative in `tbllogevent`
- Runtime: nessuna modifica alla validazione, ai contatori, agli allarmi o all'esito Good/No Good
- Diagnostica: eccezioni, guasti hardware, timeout, errori VisionPro, DB, OPC UA e allarmi restano registrati
- Release software: `2.0.1.5`

## [2026-06-05] Versione 2.0.1.4 - Encoder automatico, taratura persistente e quote trigger coerenti

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - avvio automatico canali encoder, sincronizzazione iniziale, blocco fotocellula finche il riferimento non e pronto e salvataggio automatico della taratura tachimetro
- `Services/MachineController.cs` - conversione mm/conteggi allineata al moltiplicatore `TrackingMode` `x1/x2/x4`
- `Documentation/MachineHardware/encoder-automatic-start-and-trigger-flow-2026-06-05.md` - documentato flusso encoder e differenza rispetto al trigger temporizzato
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.4`

**Motivo:** Il contatore Advantech veniva configurato ma richiedeva ancora il pulsante manuale Start per essere armato. Inoltre la velocita HMI includeva Quadrature x4 mentre il controller quote non applicava lo stesso moltiplicatore, anticipando trigger e scarto. La taratura tachimetro restava solo in memoria fino al salvataggio manuale.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; `MillimetersPerRevolution` viene salvato automaticamente dopo la taratura
- Database: nessuna modifica
- Runtime: encoder avviato al preload; la fotocellula in modalita encoder e accettata solo dopo sincronizzazione del riferimento
- Trigger: quote TOP/SIDE/scarto calcolate con la stessa scala `x1/x2/x4` usata dalla velocita
- Release software: `2.0.1.4`

## [2026-06-05] Versione 2.0.1.3 - Autoswitch ricetta: soglia fail letta da configurazione

**File modificati:**
- `Services/AutoRecipeSwitchService.cs` - `MaxConsecutiveFailures` viene letto da `Config.AutoSwitchSettings.MaxConsecutiveFailures`; fallback `5` solo se config assente o valore non valido
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.3`

**Motivo:** La soglia di attivazione autoswitch ricetta deve restare parametrizzabile dal file configurazione/HMI come nel flusso precedente, senza valore fisso interno al service.

**Impatto:**
- Config.xml: nessuna modifica schema; viene usato il nodo esistente `AutoSwitchSettings.MaxConsecutiveFailures`
- Machine runtime config: nessuna modifica
- Database: nessuna modifica
- Runtime: il numero di scarti consecutivi richiesto per tentare l'autoswitch segue il valore configurato
- Release software: `2.0.1.3`

## [2026-06-05] Versione 2.0.1.2 - Layout responsivo card taratura tachimetro encoder

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` - sostituita la griglia fissa della card taratura tachimetro con `WrapPanel` e campi/pulsanti a capo automatico
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.2`

**Motivo:** Su monitor/pannelli da 15 pollici i componenti della card taratura tachimetro non entravano nella larghezza disponibile e il pulsante di applicazione veniva tagliato.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica
- Database: nessuna modifica
- UI: la card si adatta alla larghezza disponibile mandando a capo pulsanti e campi
- Release software: `2.0.1.2`

## [2026-06-05] Versione 2.0.1.1 - Taratura encoder da tachimetro in HMI

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - aggiunti campi e comandi per calcolare e applicare `MillimetersPerRevolution` dall confronto tra velocita tachimetro e velocita encoder HMI
- `Views/UserControls/DigitalIOControl.xaml` - aggiunto pannello `Taratura tachimetro` nella pagina Encoder Setup
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte chiavi lingua ITA/ENG per la nuova sezione
- `Localization/messages_eng.json` / `Localization/messages_ita.json` - rigenerati dallo script lingua
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.1`

**Motivo:** In collaudo l'unico riferimento affidabile disponibile puo' essere la velocita reale misurata con tachimetro. Serviva quindi uno strumento HMI per riallineare la scala encoder senza calcoli manuali sul file XML.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; viene aggiornato il campo esistente `EncoderTemplates/MillimetersPerRevolution`
- Database: nessuna modifica
- Runtime: il nuovo `mm/rev` modifica la scala encoder usata da velocita, quote intervento, trigger encoder e MultiShot
- Operativo: dalla release `2.0.1.4` la taratura viene salvata automaticamente
- Release software: `2.0.1.1`

## [2026-06-04] Versione 2.0.1.0 - PowerFlex ping retry limitato e log piu' pulito

**File modificati:**
- `Services/PowerFlex525EtherNetIpClient.cs` - aggiunto stato di retry ping: primo tentativo, secondo tentativo dopo 10 minuti, poi blocco ping fino al riavvio HMI se l'inverter resta non raggiungibile
- `Services/PowerFlex525Service.cs` - se lo snapshot connessione non e' disponibile non viene piu' avviata la lettura parametri nello stesso ciclo
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.1.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.1.0`

**Motivo:** Quando l'IP inverter non era configurato correttamente o l'inverter non rispondeva, il polling PowerFlex continuava a generare ping e warning ripetuti. Questo rendeva il log operativo troppo rumoroso e meno utile in macchina.

**Impatto:**
- Config.xml: nessuna modifica
- Database: nessuna modifica
- Runtime: PowerFlex non e' vincolante per RunContinuous; se non risponde viene marcato non disponibile senza bloccare la macchina
- Log: dopo due tentativi falliti, il ping PowerFlex viene fermato fino al riavvio HMI
- Diagnostica: evento `POWERFLEX525_IP_UNREACHABLE_DISABLED` quando l'endpoint viene disabilitato dopo retry
- Release software: `2.0.1.0`

## [2026-06-04] Versione 2.0.0.9 - Input decimale VisionPro stitching Side/Left

**File modificati:**
- `Converters/DecimalTextBoxConverter.cs` - nuovo converter WPF per accettare decimali con punto o virgola e ignorare stati intermedi di digitazione
- `Views/UserControls/DigitalIOControl.xaml` - applicato il converter ai campi `SideLeftVisionProStepMm` e `SideLeftVisionProMmPerPixel`
- `QtisVisionPanel.csproj` - incluso il nuovo converter nel progetto
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.9`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.9`

**Motivo:** I campi `Stitching step` e `Left calibration` erano collegati direttamente a proprieta' `double` con `UpdateSourceTrigger=PropertyChanged`; durante la digitazione valori intermedi come campo vuoto, `.`, `..` o `0.` generavano errori `ConvertBack cannot convert value`.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; i valori continuano a essere salvati in `VisionProStitching.StepMm` e `VisionProStitching.MmPerPixel`
- Database: nessuna modifica
- UI: il tecnico puo' inserire la calibrazione Left con punto o virgola, ad esempio `0.035` o `0,035`
- Runtime: input non numerici non aggiornano la configurazione e non generano eccezioni di binding
- Release software: `2.0.0.9`

## [2026-06-04] Versione 2.0.0.8 - Encoder PCIE-1884: canali attivi, frequency throttle e velocita' top menu

**File modificati:**
- `Models/IIODeviceManager.cs` - aggiunto contratto `ConfigureActiveEncoderChannels`
- `Models/AdvantechDeviceManager.cs` - limitata la lettura ai soli canali encoder configurati; aggiunto throttle sulla frequency meter; `ErrorFuncBusy` non viene piu' loggato come errore continuo
- `Services/MachineSpeedService.cs` - nuovo service condiviso per pubblicare la velocita' macchina corrente
- `ServiceLocator.cs` - registrato `MachineSpeedService`
- `ViewModels/DigitalIOViewModel.cs` - risolve l'encoder principale dal file macchina, pubblica la velocita' encoder al top menu e calcola `counts/mm` includendo `TrackingMode` (`x4`, `x2`, `x1`)
- `ViewModels/TopMenuBarViewModel.cs` - visualizza la velocita' encoder recente nel top menu con fallback PowerFlex
- `QtisVisionPanel.csproj` - incluso il nuovo service nel progetto
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.8`

**Motivo:** Su macchina con un solo encoder fisico la HMI interrogava comunque la frequency meter dei 4 canali PCIE-1884, producendo log continui `ErrorFuncBusy`. In piu' la velocita' calcolata nella pagina I/O non veniva mostrata nel top menu, che usava solo il valore PowerFlex. Il calcolo `counts/mm` non considerava il moltiplicatore `Quadrature x4`.

**Impatto:**
- Config.xml: nessuna modifica
- Machine runtime config: nessuna modifica schema; i canali encoder attivi derivano da `EncoderTemplates/Channel`
- Database: nessuna modifica
- Runtime: con solo `Counter0` configurato, `Counter1..Counter3` non vengono piu' letti in continuo
- Diagnostica: `ErrorFuncBusy` frequency meter viene mantenuto come debug/throttle e non satura il log operativo
- Velocita': formula aggiornata a `counts/mm = PulsesPerRevolution * trackingMultiplier / MillimetersPerRevolution`; `m/min = abs(deltaCounts / countsPerMm) * 60 / (1000 * elapsedSeconds)`
- Release software: `2.0.0.8`

## [2026-06-04] Versione 2.0.0.7 - Fix binding System.Threading.Tasks.Extensions per caricamento VPP

**File modificati:**
- `App.config` - aggiunto binding redirect esplicito per `System.Threading.Tasks.Extensions` verso assembly version `4.2.4.0`; riallineati anche `System.Memory` `4.0.5.0` e `System.Runtime.CompilerServices.Unsafe` `6.0.3.0`
- `QtisVisionPanel.csproj` - aggiunto `PackageReference` diretto a `System.Threading.Tasks.Extensions` `4.6.3` e rimosso il vecchio riferimento assembly `4.2.0.1`
- `packages.config` - rimossa la vecchia voce `System.Threading.Tasks.Extensions` `4.5.4`
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.7`

**Motivo:** Su una macchina il VPP veniva caricato da VisionPro ma non dalla HMI con errore `Unable to load one or more of the requested types` e `Could not load file or assembly 'System.Threading.Tasks.Extensions, Version=4.2.4.0'`. La causa e' un mismatch tra la DLL richiesta dal job/dipendenze VisionPro e la DLL risolta dal processo HMI.

**Impatto:**
- Config.xml: nessuna modifica
- Ricette/VPP: nessuna modifica richiesta
- Database: nessuna modifica
- Deploy: copiare sempre `QtisVisionPanel.exe.config` insieme all'eseguibile e verificare che `System.Threading.Tasks.Extensions.dll` nella cartella HMI sia assembly version `4.2.4.0`
- Runtime: il caricamento VPP dalla HMI usa un binding stabile verso `System.Threading.Tasks.Extensions` `4.2.4.0`
- Release software: `2.0.0.7`

## [2026-06-04] Versione 2.0.0.6 - Autoswitch ricetta spostato in service

**File modificati:**
- `Services/AutoRecipeSwitchService.cs` - nuovo service per autoswitch ricetta dopo scarti consecutivi
- `ServiceLocator.cs` - aggiunto singleton `AutoRecipeSwitchService` con dispose in `Reset`
- `MainWindow.xaml.cs` - rimossi campi/metodi diretti del cambio ricetta automatico; `OnInspectionResult` delega al service
- `QtisVisionPanel.csproj` - incluso il nuovo file service nel progetto WPF legacy
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.6`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.6`

**Motivo:** Ridurre la lunghezza e la responsabilita' di `MainWindow.xaml.cs` spostando la regia autoswitch in un servizio dedicato. Il main window resta il punto di ingresso dell'evento VisionPro, ma non gestisce piu' contatori, timer, ricerca ricetta e dialog di conferma.

**Impatto:**
- Config.xml: nessuna modifica
- Ricette: nessuna modifica
- Database: nessuna modifica
- Runtime macchina: comportamento autoswitch invariato; soglia preservata a 20 scarti consecutivi
- Manutenzione: log dedicati `AUTO_RECIPE_SWITCH_*` / `AUTO_RECIPE_CHANGE_*`
- Release software: `2.0.0.6`

## [2026-06-04] Versione 2.0.0.5 - Switch HMI UseEncoderTrigger per trigger encoder reale / temporizzato

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs` - aggiunto `RuntimeBindings.UseEncoderTrigger` con pattern `UseEncoderTriggerSpecified` per compatibilita' XML legacy
- `ConfigurationTemplates/machine_runtime_config.template.xml` - aggiunto default `<UseEncoderTrigger>true</UseEncoderTrigger>`
- `ViewModels/DigitalIOViewModel.cs` - aggiunta proprieta' HMI `UseEncoderTrigger`, sincronizzazione con `TriggerSchedulingMode` / `VirtualConveyorEnabled`, validazione MultiShot e log runtime
- `Views/UserControls/DigitalIOControl.xaml` - aggiunto switch operativo nella sezione trigger acquisizione con riepilogo del modo attivo
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte label ITA/ENG per lo switch trigger e i riepiloghi runtime
- `Localization/messages_ita.json`, `Localization/messages_eng.json` - rigenerati con le nuove chiavi lingua
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.5`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - chiarito che MultiShot Side/Left richiede trigger encoder reale

**Motivo:** Il flusso precedente supportava gia' encoder reale, trigger temporizzato e virtual conveyor, ma la scelta era distribuita tra `TriggerSchedulingMode` e `VirtualConveyorEnabled`. Per l'uso macchina serviva uno switch chiaro: se l'encoder e' collegato si usa encoder; se non si usa encoder si resta sul temporizzato da fotocellula.

**Impatto:**
- Machine runtime config: nuovo nodo additivo `RuntimeBindings/UseEncoderTrigger`
- Config.xml principale: nessuna modifica
- Ricette: nessuna modifica
- Database: nessuna modifica
- Runtime macchina:
  - `UseEncoderTrigger=true` forza il modo encoder reale (`VirtualConveyor`, virtual conveyor disabilitato)
  - `UseEncoderTrigger=false` forza il modo `TimedFromPhotocell`
  - Side/Left MultiShot non viene armato se `UseEncoderTrigger=false`
- Release software: `2.0.0.5`

## [2026-06-04] Versione 2.0.0.4 - Fix binding ComboBoxItem pagina I/O

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` - aggiunto stile implicito locale `ComboBoxItem` basato su `DigitalIoComboBoxItemStyle`
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.4`

**Motivo:** Evitare errori binding WPF su `ComboBoxItem.HorizontalContentAlignment` e `ComboBoxItem.VerticalContentAlignment` quando il popup delle ComboBox viene creato senza risolvere l'antenato `ItemsControl`.

**Impatto:**
- UI: eliminati errori binding non funzionali nella diagnostica I/O
- Config.xml / machine runtime config: nessuna modifica
- Ricette: nessuna modifica
- Runtime macchina: nessuna modifica
- Release software: `2.0.0.4`

## [2026-06-04] Versione 2.0.0.3 - Configurazione HMI Side/Left MultiShot e stitching VisionPro

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` - aggiunte proprieta' HMI per `MachineMultiShotTrigger.Side`, salvataggio nel file macchina e riconfigurazione del controller MultiShot su preview/save/load
- `Views/UserControls/DigitalIOControl.xaml` - aggiunta sezione guidata `Side/Left MultiShot` nella pagina commissioning I/O
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte label ITA/ENG per la nuova sezione MultiShot
- `Localization/messages_ita.json`, `Localization/messages_eng.json` - rigenerati con le nuove chiavi lingua
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.3`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - aggiunta mini guida HMI/IO per operatori e collaudatori

**Motivo:** Permettere di abilitare e parametrizzare il MultiShot Side/Left direttamente dal pannello, inclusi impulsi encoder/I/O e parametri VisionPro `ImageStitching`, senza edit manuale del file XML macchina.

**Impatto:**
- Ricetta: nessun campo aggiunto
- Machine runtime config: valori `MachineMultiShotTrigger/Side` modificabili da HMI
- Database: nessuna modifica schema
- Runtime macchina: dopo preview/salvataggio il controller MultiShot viene riconfigurato con i valori correnti
- VisionPro: invariato; continua a gestire acquisizione multiframe, stitching e risultato finale
- Release software: `2.0.0.3`

## [2026-06-04] Versione 2.0.0.2 - Parametri VisionPro ImageStitching per Side/Left MultiShot

**File modificati:**
- `Models/MultiShotTrigger/MultiShotTriggerModels.cs` - aggiunto sotto-nodo macchina `VisionProStitching` su `MachineMultiShotTrigger.Side`
- `ConfigurationTemplates/machine_runtime_config.template.xml` - aggiunti default XML per `ImageStitching`, `expectedFrames`, `stepMm`, `mmPerPixel`
- `Services/MachineConfigurationService.cs` - normalizzazione additiva per vecchi file macchina senza `VisionProStitching`
- `Services/MultiShotTrigger/VisionProStitchingParameterApplier.cs` - nuovo applicatore parametri VisionPro, senza modificare controller trigger
- `MainWindow.xaml.cs` - applicazione parametri dopo load/reload VPP/job e prima del `RunContinuous`
- `QtisVisionPanel.csproj` - incluso nuovo servizio nel progetto legacy WPF
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.2`

**Motivo:** Il job script VisionPro del Left/SideLeft deve conoscere quanti frame attendere e la calibrazione per costruire l'immagine stitched finale. La HMI continua a generare solo impulsi hardware; VisionPro resta responsabile di acquisizione N frame, stitching e pubblicazione del solo risultato finale.

**Impatto:**
- Config.xml ricetta: nessuno
- Machine runtime config: aggiunto sotto-nodo additivo `MachineMultiShotTrigger/Side/VisionProStitching`
- Database: nessuna modifica schema
- Runtime macchina: se MultiShot e' disabilitato non viene scritto nulla; se abilitato ma config/toolblock/input sono incompleti vengono generati log chiari e la HMI non crasha
- VisionPro: il ToolBlock `ImageStitching` deve esporre input `expectedFrames`, `stepMm`, `mmPerPixel` o nomi configurati nel nodo macchina
- Release software: `2.0.0.2`

## [2026-06-03] Versione 2.0.0.1 - Camera Left come ruolo runtime Side/Left con esiti booleani VisionPro

**File modificati:**
- `Models/CameraModel.cs` - aggiunto ruolo camera `left`; il job QuickBuild chiamato `Left` / `SideLeft` vince sul fallback `CameraConfig.xml` come gia' avviene per `Top3D`
- `MainWindow.xaml.cs` - il ruolo `left` usa la stessa coda/display fisica della Side, ma resta semanticamente `Left` durante validazione, display e log
- `DataManage/IToolBlockValidator.cs` - aggiunta validazione Left dedicata: VisionPro restituisce booleani per saldatura laterale e conteggio rotoli
- `DataManage/InspectionConfigService.cs`, `Services/CameraDisplayManager.cs`, `Views/UserControls/DisplayRecord/SideCameraView.*` - visibilita' e indicatori UI aggiornati per `SideRollCount`
- `Database/CounterManager.cs`, `Models/EjectionAlarmConfig.cs`, `Models/SelectableDefect.cs`, `Services/IntegratedAlarmCardService.cs`, `ViewModels/EjectionAlarmCardViewModel.cs`, `Database/AlarmCardRepository.cs` - aggiunto difetto/contatore/allarme `SideRollCount`
- `Cls_Config/Calss_structure/ConfigClassStructure.cs` - aggiunti output opzionali `LeftSideSealingOutput` e `LeftRollCountOutput`
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte label lingua per conteggio rotoli
- `Properties/AssemblyInfo.cs` - release aggiornata a `2.0.0.1`
- `Documentation/MachineHardware/software-version-archive.md` - completata release `2.0.0.1`

**Motivo:** Permettere alla telecamera fisica Side di essere trattata come camera `Left` quando il job VisionPro espone quel ruolo, con la stessa filosofia gia' usata da `Top` / `Top3D`: il ruolo deriva dal job, mentre hardware e display restano coerenti con la macchina.

**Impatto:**
- Config.xml: nuovi campi opzionali sotto `<Configuration>`: `LeftSideSealingOutput` default `LeftSideSealingOk`, `LeftRollCountOutput` default `LeftRollCountOk`
- VisionPro: il toolblock `Results` del job Left deve esporre booleani `LeftSideSealingOk` e `LeftRollCountOk` oppure alias supportati `SideSealingOk` / `SealingOk` e `RollCountOk` / `RollsCountOk` / `RollCount`
- Database: nessuna nuova tabella; `cfg_inspection` riceve default additivo `SideRollCount`
- Ricette: nessun nuovo campo ricetta; `SideRollCount` e' abilitato quando il ruolo Left e l'output VisionPro sono presenti
- Runtime macchina: con job Side standard resta invariato; con job Left la validazione Side standard non cerca piu' Height/ShapeSide e usa solo booleani Left
- UI: la vista Side mostra label Left e indicatore conteggio rotoli quando la ricetta/job lo supportano
- Release software: `2.0.0.1`

## [2026-06-03] Versione 2.0.0.0 - Side/Left encoder-driven MultiShot trigger

**File modificati:**
- `Models/MultiShotTrigger/MultiShotTriggerModels.cs` - aggiunti modelli opzioni, piano, target, sessione ed eventi diagnostici MultiShot
- `Services/MultiShotTrigger/*` - aggiunti plan builder e controller runtime encoder-driven per generare impulsi hardware Side/Left
- `Models/MachineRuntimeConfiguration.cs` - aggiunto nodo macchina `MachineMultiShotTrigger`
- `Services/MachineConfigurationService.cs` - upgrade schema additivo per aggiungere il nodo multishot con default disabilitato
- `ConfigurationTemplates/machine_runtime_config.template.xml` - aggiunta sezione XML esempio `MachineMultiShotTrigger/Side`
- `ViewModels/DigitalIOViewModel.cs` - agganciato il controller a fotocellula, encoder reale/virtuale, reload config, stop runtime e cambio modalita' IO; il single-shot Side viene saltato quando MultiShot e' attivo
- `Services/MachineRuntimeService.cs` - aggiunto evento `ContinuousRunStateChanged` per cancellare sessioni multishot quando RunContinuous si ferma
- `MainWindow.xaml.cs`, `Views/UserControls/DisplayRecord/SideCameraView.xaml.cs` - la vista software Side puo' mostrare label operatore Left quando configurato
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunta label lingua `lbLeftCamera`
- `QtisVisionPanel.csproj` - inclusi i nuovi file nel progetto legacy WPF
- `Properties/AssemblyInfo.cs` - nuova versione `2.0.0.0`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md` - guida tecnica/configurazione/test
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `2.0.0.0`

**Motivo:** Integrare una prima logica MultiShot conservativa per la camera fisica Left mantenendo il ruolo software `Side`: la HMI genera solo impulsi trigger hardware distanziati da encoder, mentre VisionPro resta responsabile di acquisizione multiframe, stitching e ispezione finale.

**Impatto:**
- Config.xml principale: nessuno
- Machine runtime config: aggiunto nodo additivo `MachineMultiShotTrigger/Side`, default `Enabled=false`
- Database: nessuna modifica schema
- Ricette: nessuna modifica schema o parametro ricetta
- Runtime macchina: con `Enabled=false` comportamento invariato; con `Enabled=true` Side/Left genera N impulsi hardware encoder-driven e il single-shot Side viene disabilitato per evitare doppio trigger
- UI: la vista esistente Side puo' mostrare `Left Camera Features`; nessuna nuova view
- Release software: `2.0.0.0`

## [2026-05-29] Data Analysis - fix grafico torta difetti in PDF

**File modificati:**
- `Services/VisualPdfExportService.cs` - la cattura delle card PDF ora renderizza l'elemento tramite `VisualBrush` su una superficie bianca locale, evitando immagini nere/vuote quando la card e' posizionata in una colonna o ha offset di layout
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.8`

**Motivo:** Il grafico a torta dei difetti poteva non comparire nel PDF Data Analysis perche' la cattura diretta di una card posizionata nella seconda colonna della griglia manteneva l'offset del layout e produceva una bitmap nera o vuota.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': stesso pulsante `Esporta PDF`; il PDF include correttamente anche la card distribuzione difetti/grafico a torta
- Runtime macchina: nessun cambio a ricette, I/O, acquisizione o ciclo automatico
- Release software: `1.2.1.8`

## [2026-05-29] Data Analysis - correzione PDF vuoto

**File modificati:**
- `Services/VisualPdfExportService.cs` - sostituita la cattura via `VisualBrush`/`FlateDecode` con cattura diretta `RenderTargetBitmap` e immagini JPEG alta qualita'
- `QtisVisionPanel.csproj` - rimosso riferimento non piu' necessario a `System.IO.Compression`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.7`

**Motivo:** Il PDF Data Analysis generato con la prima impaginazione a card poteva contenere pagine bianche/senza dati perche' il percorso di rendering produceva immagini vuote o non correttamente visualizzate nel PDF reader.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': stesso pulsante `Esporta PDF`, ma i grafici vengono renderizzati nel PDF
- Runtime macchina: nessun cambio a ricette, I/O, acquisizione o ciclo automatico
- Release software: `1.2.1.7`

## [2026-05-29] Data Analysis - PDF professionale a card singole

**File modificati:**
- `Views/UserControls/DataAnalysisView.xaml` - aggiunti nomi alle card produzione/difetti e all'ItemsControl trend per esportare le card visibili come elementi separati
- `Views/UserControls/DataAnalysisView.xaml.cs` - export PDF aggiornato per raccogliere le singole card visibili invece dell'intero contenitore grafici
- `Services/VisualPdfExportService.cs` - generatore PDF rivisto con una card per pagina, rendering alta risoluzione, compressione lossless e impaginazione A4 orizzontale piu' professionale
- `QtisVisionPanel.csproj` - aggiunto riferimento a `System.IO.Compression` per immagini PDF lossless
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.6`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.6`

**Motivo:** Il primo export PDF Data Analysis impaginava una lunga immagine unica della dashboard, con possibile taglio dei grafici e scala poco leggibile. Ora ogni grafico/card viene archiviato in modo ordinato e leggibile.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': stesso pulsante `Esporta PDF`, ma output PDF piu' leggibile e professionale
- Runtime macchina: nessun cambio a ricette, I/O, acquisizione o ciclo automatico
- Release software: `1.2.1.6`

## [2026-05-29] Data Analysis - export PDF grafici visualizzati

**File modificati:**
- `Views/UserControls/DataAnalysisView.xaml` - aggiunto pulsante export PDF e raggruppata l'area grafici in un contenitore dedicato alla cattura
- `Views/UserControls/DataAnalysisView.xaml.cs` - aggiunta gestione salvataggio PDF con `SaveFileDialog`, logo Pulsar e popup esito
- `ViewModels/DataAnalysisViewModel.cs` - aggiunte label/localizzazione export e sottotitolo report con filtri periodo/ricetta
- `Services/VisualPdfExportService.cs` - nuovo generatore PDF locale che renderizza un `FrameworkElement` WPF e lo impagina in A4 orizzontale multipagina
- `QtisVisionPanel.csproj` - incluso il nuovo servizio PDF
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunti messaggi lingua per export Data Analysis
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.5`

**Motivo:** Consentire all'operatore/manutentore di archiviare in PDF i grafici Data Analysis attualmente visualizzati, senza esportare filtri o controlli di configurazione della pagina.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': Data Analysis mostra `Esporta PDF`; il PDF contiene solo grafici visibili, periodo/ricetta e intestazione
- Runtime macchina: nessun cambio a ricette, I/O, acquisizione o ciclo automatico
- Release software: `1.2.1.5`

## [2026-05-29] Manuale PDF - testo tabelle visibile

**File modificati:**
- `Services/ManualPdfExportService.cs` - `DrawText` forza il colore del testo prima di scrivere nel content stream PDF, evitando che erediti il colore dello sfondo tabella
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.4`

**Motivo:** Nel PDF generato le immagini erano presenti ma le tabelle sembravano vuote: il testo veniva scritto con un colore uguale/molto vicino al riempimento dei rettangoli di sfondo.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': rigenerando il PDF dal pannello le tabelle risultano leggibili
- Runtime macchina: nessun cambio a ricette, I/O, acquisizione o ciclo automatico
- Release software: `1.2.1.4`

## [2026-05-29] Manuale - colonna ID allineata ai numeri immagine

**File modificati:**
- `Docs/Manual/00_Documentazione_Tecnica_Globale.md` - aggiunta colonna `ID` a tutte le tabelle, con numerazione per collegare immagine e descrizione
- `ViewModels/ManualViewModel.cs` - righe tabella manuale estese con campo `Id`; parser compatibile con tabelle vecchie e nuove
- `Views/UserControls/ManualView.xaml` - tabella manuale aggiornata con colonna ID evidenziata
- `Services/ManualPdfExportService.cs` - export PDF aggiornato con colonna ID
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunta label lingua per intestazione ID
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.3`

**Motivo:** Rendere immediato il collegamento tra i numeri presenti nelle immagini del manuale e le descrizioni tabellari, cosi' l'operatore trova subito il campo corretto.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': manuale e PDF mostrano la colonna ID come primo riferimento
- Runtime macchina: nessun cambio a ricette, I/O, acquisizione o ciclo automatico
- Release software: `1.2.1.3`

## [2026-05-29] Manuale pannello zoomabile con export PDF

**File modificati:**
- `Views/UserControls/ManualView.xaml` - aggiunti controlli zoom, pulsante export PDF e rendering tabellare delle funzioni del manuale
- `ViewModels/ManualViewModel.cs` - aggiunti zoom level, comandi zoom/export PDF e parser tabelle Markdown per sezioni manuale
- `Services/ManualPdfExportService.cs` - aggiunto generatore PDF locale con logo Pulsar, immagini e tabelle funzione/azione/supporto
- `Docs/Manual/00_Documentazione_Tecnica_Globale.md` - riallineato al formato titolo pagina, immagine e tabella descrittiva per ogni area operativa
- `Docs/Manual/Images/Pulsar_Engineering_logo.png` - aggiunto logo usato nell'impaginazione PDF
- `ServerMessage/ServerMessageStructure.cs`, `Localization/messages_*.json`, `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte label lingua per export PDF e intestazioni tabella
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.2`

**Motivo:** Il manuale deve essere leggibile direttamente dal pannello anche su display piccoli, con struttura piu' professionale e possibilita' di generare un PDF impaginato da consegnare o consultare fuori linea.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': la pagina Manuale supporta zoom e export PDF; il manuale principale ora mostra title/image/table per le funzioni operative
- Runtime macchina: nessun cambio a ricette, I/O, acquisizione o ciclo automatico
- Release software: `1.2.1.2`

## [2026-05-29] Manuale operatore illustrato con screenshot numerati

**File modificati:**
- `Docs/Manual/00_Documentazione_Tecnica_Globale.md` - riscritto come manuale operatore illustrato con descrizione dei campi numerati, passaggi operativi e casi di intervento
- `Docs/Manual/Images/*.png` - aggiunte schermate numerate: main window, counters, recipe manager, preferences, alarms, OPC UA, DataInspector, Data Analysis e PC Diagnostics

**Motivo:** Rendere il manuale comprensibile a qualunque operatore, usando le schermate reali del pannello e spiegando cosa controllare, cosa fare e quando chiamare manutenzione.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': la pagina Manuale mostra una guida piu' intuitiva e visuale; nessun cambio runtime
- Release software: nessun incremento, modifica documentale

## [2026-05-28] Shutdown - dispose idempotente token e servizi

**File modificati:**
- `MainWindow.xaml.cs` - aggiunti helper sicuri per cancel/dispose dei `CancellationTokenSource` durante shutdown
- `ServiceLocator.cs` - `Reset()` ora usa dispose sicuro per evitare warning su servizi gia' chiusi; `ShutdownAllServicesAsync()` azzera i riferimenti ai servizi spenti
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.1`

**Motivo:** Durante la chiusura venivano loggati warning non critici come `CANCELLATION_TOKEN_CANCEL_FAILED` e `Errore reset servizi: Il semaforo e' stato eliminato`, causati da risorse gia' disposte da un altro percorso di cleanup.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: shutdown piu' pulito e idempotente; nessun cambio al ciclo produzione
- Release software: `1.2.1.1`

## [2026-05-28] DB init - indice tblgenerale EsitoClass corretto

**File modificati:**
- `Database/Cls_InitializzeDb.cs` - corretto l'indice `idx_EsitoClass` da `EsitoClassificazione` a `Esito_Classificazione`, allineandolo alla colonna reale usata da `tblgenerale`
- `Database/Cls_InitializzeDb.cs` - `EnsureIndexAsync` ora verifica che le colonne richieste esistano prima di eseguire `ALTER TABLE ADD INDEX`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.1.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.1.0`

**Motivo:** Lo startup loggava `ENSURE_INDEX_FAILED|table=tblgenerale|index=idx_EsitoClass` perche' provava a creare un indice su `EsitoClassificazione`, colonna non presente nello schema reale. La colonna corretta e' `Esito_Classificazione`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione distruttiva; su installazioni esistenti l'indice viene creato solo se la colonna esiste
- Runtime: eliminato warning startup DB non necessario; inizializzazione piu' robusta su schema legacy
- Release software: `1.2.1.0`

## [2026-05-28] Commenti tecnici - prima passata su flussi critici

**File modificati:**
- `MainWindow.xaml.cs` - commentati flussi OPC UA command/ack, output fisico allarme e publish snapshot ispezione
- `Services/OpcUaClientService.cs` - aggiunti commenti su comunicazione opzionale/vincolante, security endpoint e handshake
- `Services/OpcUaConfigurationService.cs` - documentata la separazione tra config OPC UA e `Config.xml`
- `Services/AuthorizationService.cs` - commentato il gate centrale permessi e il blocco safety per Operator
- `Services/RecipeTransitionService.cs` - commentato il rollback cambio ricetta e rimosso testo corrotto nel log
- `ViewModels/RecipeManagerViewModel.cs` - commentato ordinamento ricetta in produzione e refresh dopo cambio esterno
- `Database/CounterManager.cs` - commentata distinzione tra contatori storici e messaggi difetto correnti
- `Services/IntegratedAlarmCardService.cs` - commentato ACK allarme come reset runtime, non solo chiusura popup
- `Models/EjectionAlarmManager.cs` - commentati reset memoria allarme, contatori consecutivi e buffer percentuale
- `Documentation/MachineHardware/source-code-commenting-guide-2026-05-28.md` - aggiunta guida per continuare la commentatura senza rumore

**Motivo:** Rendere piu' comprensibili i flussi piu' delicati per manutentori e sviluppatori senza introdurre commenti riga-per-riga inutili o fragili.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: nessun cambio intenzionale; commenti e pulizia testo/log
- Release software: nessun incremento, modifica non funzionale

## [2026-05-28] Documentazione applicazione - guida operatore riallineata

**File modificati:**
- `Docs/Manual/00_Documentazione_Tecnica_Globale.md` - riscritta come guida completa e leggibile dal pannello HMI, con flussi operatore, ricette, allarmi, OPC UA, diagnostica e glossario
- `Docs/Manual/README.md` - aggiunto indice manuali per ruolo e tipo di intervento
- `README.md` - aggiornata la sezione documentazione con il nuovo punto di ingresso operatore
- `Documentation/MachineHardware/README.md` - riallineato il punto di ingresso tecnico e rimosso testo corrotto

**Motivo:** La documentazione principale caricata dal pannello era troppo tecnica e conteneva testo corrotto; serviva una guida comprensibile anche da chi apre il software per la prima volta.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': la pagina Manuale mostra un documento iniziale piu' chiaro, ordinato e orientato al lavoro reale dell'operatore
- Release software: nessun incremento, modifica solo documentale

## [2026-05-28] Data Analysis - fix binding ToleranceBandTop

**File modificati:**
- `Views/UserControls/DataAnalysisView.xaml` - sostituito il binding su `TranslateTransform.Y` con `Canvas.Top` sulla `Rectangle` della banda tolleranza
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.9`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.9`

**Motivo:** `TranslateTransform` non eredita sempre il `DataContext` dell'elemento visuale, generando il warning WPF su `ToleranceBandTop`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': nessun cambio funzionale; la banda tolleranza resta visivamente uguale ma senza errore binding

## [2026-05-27] Defects Counters - la UI non conserva messaggi cancellati

**File modificati:**
- `ViewModels/InspectionCounterViewModel.cs` - `UpdateDefectCounter` cancella `LastErrorMessage` quando l'aggiornamento contatore non ha un messaggio difetto corrente
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.8`

**Motivo:** Anche dopo la pulizia nel `CounterManager`, la ViewModel preservava localmente il vecchio testo se il contatore storico era maggiore di zero. Questo lasciava visibili motivi vecchi su pezzi `GOOD`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': il messaggio rosso resta visibile solo se esiste un messaggio difetto corrente; i conteggi storici non vengono modificati

## [2026-05-27] Defects Counters - pulizia motivi a inizio ciclo ispezione

**File modificati:**
- `Database/CounterManager.cs` - aggiunto `BeginInspectionCycle()` che cancella i messaggi ultimo difetto e notifica la UI prima del nuovo ciclo
- `MainWindow.xaml.cs` - chiamata a `CounterManager.BeginInspectionCycle()` subito prima della validazione del pezzo
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.7`

**Motivo:** Pulire i messaggi solo quando il risultato finale era `GOOD` poteva arrivare troppo tardi, perche' i validatori scrivono gia' i messaggi difetto durante la validazione. La UI deve mostrare un motivo solo quando un difetto viene rilevato nel ciclo corrente.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': all'inizio di ogni pezzo i vecchi motivi difetto vengono rimossi; se il pezzo corrente genera un difetto, il nuovo motivo viene mostrato normalmente

## [2026-05-27] Defects Counters - pulizia motivi su pezzo conforme

**File modificati:**
- `Database/CounterManager.cs` - quando l'ispezione e' conforme, cancella tutti i messaggi ultimo difetto prima di inviare `CountersUpdated` alla UI
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.6`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.6`

**Motivo:** Dopo il ripristino dei messaggi difetto, un vecchio motivo poteva restare visibile fino al prossimo difetto/clear specifico. L'operatore deve vedere la riga motivo solo mentre l'ultimo stato utile e' difettoso.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': al primo pezzo `GOOD` i testi motivo difetto vengono puliti; i contatori numerici restano invariati

## [2026-05-27] Defects Counters - ripristino motivo ultimo difetto

**File modificati:**
- `Database/CounterManager.cs` - messaggi ultimo difetto memorizzati e recuperati con chiavi normalizzate/case-insensitive
- `ViewModels/InspectionCounterViewModel.cs` - recupero robusto del messaggio difetto durante il refresh contatori e preservazione dell'ultimo motivo quando il contatore resta maggiore di zero
- `Views/UserControls/StatisticsView.xaml` - la riga motivo difetto viene mostrata solo se `LastErrorMessage` e' valorizzato
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.5`

**Motivo:** Dopo gli ultimi aggiornamenti la pagina `DEFECTS COUNTERS` continuava a mostrare icona, nome e conteggio, ma non piu' il motivo ultimo difetto rilevato. Il refresh poteva cancellare il testo quando arrivava un aggiornamento contatori senza nuovo messaggio.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- UI/operativita': l'operatore vede di nuovo il motivo tecnico sotto il difetto rilevato; i messaggi vengono comunque azzerati dai flussi esistenti di validazione/azzeramento

## [2026-05-27] Banner notifiche adattivo e piu' professionale

**File modificati:**
- `MainWindow.xaml` - banner ridisegnato con segmenti compatti: singola notifica centrata a tutta larghezza, doppia notifica divisa in due segmenti distinti
- `ViewModels/MainViewModel.cs` - aggiunte proprieta' `HasDiagnosticsNotification`, `HasBothShellNotifications`, `HasOnlyOperatorNotification`, `HasOnlyDiagnosticsNotification`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.4`

**Motivo:** Il banner precedente mostrava aree vuote o due messaggi poco distinti; per una macchina automatica serve una notifica piu' leggibile, compatta e professionale.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nessuno
- Database: nessuno
- UI/operativita': una notifica singola occupa tutto il banner; con OPC UA + diagnostica il banner si divide automaticamente con colori diversi

## [2026-05-27] Banner unico per eventi diagnostici e cambio ricetta OPC UA

**File modificati:**
- `MainWindow.xaml` - sostituite le due barre separate con un unico banner giallo diviso: OPC UA/MES a sinistra, diagnostica/eventi a destra
- `ViewModels/MainViewModel.cs` - aggiunta `HasShellNotification` per mostrare il banner se esiste almeno una notifica, e propagazione cambi diagnostici
- `ViewModels/SystemDiagnosticsViewModel.cs` - `SystemWarningText` viene notificato anche quando cambia il conteggio degli alert
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.3`

**Motivo:** Evitare due banner sovrapposti e mantenere in una sola riga le informazioni operative importanti.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nessuno
- Database: nessuno
- UI/operativita': il cambio ricetta da MES/OPC UA resta visibile a sinistra del banner; gli eventi diagnostici restano a destra

## [2026-05-27] Notifica operatore per cambio ricetta OPC UA

**File modificati:**
- `MainWindow.xaml.cs` - dopo un cambio ricetta OPC UA riuscito e dopo la scrittura dell'ack verso OPC UA, mostra una notifica operatore con timeout 1 minuto e logga ACK/timeout del popup
- `MainWindow.xaml` - aggiunta barra notifica operatore sopra il contenuto macchina, separata dal banner diagnostico PC
- `ViewModels/MainViewModel.cs` - aggiunte proprieta' `OperatorNotificationText` / `HasOperatorNotification` e metodi per aggiornare la barra notifiche
- `Views/SystemNotificationWindow.xaml.cs` - aggiunto supporto a testo pulsante personalizzato e auto-chiusura temporizzata senza bloccare il runtime
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte stringhe `ServerMessage` per popup/barra OPC UA e ACK
- `Localization/messages_eng.json`, `Localization/messages_ita.json` - cataloghi lingua aggiornati tramite script
- `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md` - documentata la notifica operatore dopo cambio ricetta remoto
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.2`

**Motivo:** Quando MES/SCADA cambia la ricetta tramite OPC UA, l'operatore di linea deve ricevere un avviso evidente senza dover aprire Recipe Management.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nessuno
- Database: nessuna modifica schema; vengono solo registrati eventi applicativi per popup confermato o chiuso per timeout
- Runtime: il popup e' modeless e non blocca RunContinuous; il cambio ricetta resta gestito dal flusso OPC UA esistente
- UI/operativita': popup informativo con `ACKNOWLEDGE` e auto-close dopo 1 minuto; la barra notifica indica che la ricetta in produzione e' stata cambiata da MES / OPC UA

## [2026-05-27] Recipe Manager riallineato dopo cambio ricetta OPC UA

**File modificati:**
- `MainWindow.xaml.cs` - dopo cambio ricetta OPC UA riuscito, ricetta gia' attiva o rollback, richiama il refresh del Recipe Manager
- `ViewModels/RecipeManagerViewModel.cs` - aggiunto `RefreshProductionRecipeOrderAsync` per aggiornare lo stato `InProduction` e riordinare la lista dalla `LastRecipe` corrente
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.1`

**Motivo:** La ricetta in produzione doveva restare in cima alla lista anche quando il cambio arriva da OPC UA/PLC/SCADA e non dal pulsante HMI `Load`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: nessun cambio al contratto OPC UA; dopo il cambio confermato viene solo riallineata la lista ricette gia' caricata in memoria
- UI/operativita': Recipe Management mostra subito in alto la ricetta attiva anche dopo cambio remoto

## [2026-05-27] Recipe Manager - ricetta in produzione sempre in cima

**File modificati:**
- `ViewModels/RecipeManagerViewModel.cs` - ordinamento lista ricette con priorita' a `RecipeStatus.InProduction`, riordino dopo caricamento in produzione e confronto `LastRecipe` normalizzato
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.2.0.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.2.0.0`

**Motivo:** L'operatore deve individuare subito la ricetta realmente in produzione quando entra nella pagina Recipe Management, senza scorrere la lista.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: nessun cambio al caricamento ricetta; cambia solo l'ordinamento visuale della lista HMI
- UI/operativita': la card `InProduction` resta sempre in alto e dopo un nuovo load viene riportata subito al primo posto

## [2026-05-27] Salvataggio immagini allineato allo snapshot pezzo corrente

**File modificati:**
- `SaveImage/ISaveImage.cs` - il salvataggio immagini salta le viste senza record attivo del pezzo corrente e usa il record salvato come sorgente prioritaria anche per il raw `_A`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.9`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.9`

**Motivo:** In simulazione/produzione potevano essere salvate immagini da display o cache precedenti, ad esempio una vista `F` anche quando il pezzo corrente era processato solo da Top+Side. Questo creava cartelle pezzo non coerenti con quanto visto a monitor.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: la cartella immagine contiene solo le viste con record attivo per lo snapshot corrente; le viste non presenti vengono tracciate con `SAVE_IMAGE_VIEW_SKIPPED`
- Operativita': `_A` e `_Z` risultano piu' coerenti con il prodotto appena ispezionato

## [2026-05-27] OPC UA certificato applicazione client auto-generato

**File modificati:**
- `Services/OpcUaClientService.cs` - aggiunto `ApplicationInstance.CheckApplicationInstanceCertificatesAsync` dopo la validazione configurazione per creare/caricare il certificato applicazione nello store `OPC\pki\own`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.8`

**Motivo:** Con endpoint sicuri Ignition `Basic256Sha256`, lo stack OPC UA richiede un certificato applicazione client valido. Senza check/creazione certificato la connessione falliva con `ApplicationCertificate for the security profile ... Basic256Sha256 cannot be found`.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nessuno
- Database: nessuno
- Runtime: alla prima connessione sicura il client puo' creare automaticamente il proprio certificato applicazione; il certificato generato va poi approvato nel server Ignition secondo la policy security impostata

## [2026-05-27] OPC UA PKI store completo per server Ignition

**File modificati:**
- `Services/OpcUaClientService.cs` - configurati gli store OPC UA completi (`own`, `trusted`, `issuers`, `rejected`, `trusted_user`, `user_issuers`) e aggiunti `TrustedIssuerCertificates`, `TrustedUserCertificates`, `UserIssuerCertificates`
- `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md` - documentata la struttura PKI runtime e i passi per collegarsi a server strict come Ignition
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.7`

**Motivo:** Su Ignition lo stack OPC UA puo' rifiutare la configurazione client prima della connessione se manca lo store `TrustedIssuerCertificates`, generando `BadConfigurationError`.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nessuno
- Database: nessuno
- Runtime: alla prima esecuzione vengono create le cartelle PKI mancanti sotto `OPC\pki`; la connessione con server OPC UA strict puo' procedere alla normale negoziazione certificati/security

## [2026-05-27] Pagina configurazione OPC UA con tag e direzione scambio

**File modificati:**
- `Views/UserControls/OpcUaConfigurationView.xaml` - nuova vista tecnica per parametri OPC UA, tabella tag e diagramma direzionale server/HMI
- `Views/UserControls/OpcUaConfigurationView.xaml.cs` - inizializzazione e dispose del view model
- `ViewModels/OpcUaConfigurationViewModel.cs` - caricamento, validazione, salvataggio su XML/DB e reload runtime OPC UA
- `Database/OpcUaConfigurationRepository.cs` - aggiunto upsert setting/nodi per salvare `cfg_opcua_configuration`
- `Services/OpcUaConfigurationService.cs` - aggiunto salvataggio `OpcUaConfig.xml` + DB
- `Services/ViewFactoryService.cs` - registrata vista `OpcUaConfiguration`
- `ViewModels/NavigationViewModel.cs` - aggiunta voce menu OPC UA nell'area impostazioni macchina
- `Services/AuthorizationService.cs` e `Database/Cls_CheckUser.cs` - aggiunta feature `opcUaConfigurationView` per ruoli tecnici
- `scripts/UpdateRuntimeLanguageFiles.ps1` e `Localization/messages_*.json` - aggiunte label lingua per la nuova pagina
- `QtisVisionPanel.csproj` - inclusi nuovi XAML/code-behind/view model nella build
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.6`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.6`

**Motivo:** OPC UA era configurabile tramite file e tabella DB, ma mancava una pagina HMI per manutentori/collaudatori che mostrasse tutti i parametri, i tag scambiati e la direzione dei dati.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: modificabile dalla nuova pagina
- Database: nessuna nuova tabella; `cfg_opcua_configuration` viene aggiornata con upsert
- Runtime: dopo salvataggio il client OPC UA viene disconnesso e reinizializzato con la nuova configurazione
- UI/operativita': nuova voce `OPC UA` sotto impostazioni macchina, accessibile a Expert/Installer/Administrator

## [2026-05-27] OPC UA audit operativo - stati comunicazione, comandi e failure

**File modificati:**
- `Services/ApplicationEventLogger.cs` - aggiunto logging dedicato stato OPC UA con deduplica degli stati ripetuti (`Enabled`, `RequiredForMachineRun`, `Connected`)
- `Services/OpcUaClientService.cs` - aggiunti eventi audit per configurazione caricata, OPC disabilitato, richiesta connessione, connessione riuscita/fallita, disconnessione, keepalive fallito, subscription, comandi ricevuti, ACK comando, ACK dati e write fallite
- `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md` - documentata la tracciabilita' degli eventi OPC UA nello storico operativo
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.5`

**Motivo:** Durante collaudo e produzione serve capire se OPC UA e' usato, non usato, opzionale, vincolante, connesso, disconnesso o fallito, e serve vedere nello storico operativo quali comandi OPC UA sono stati ricevuti e quale ACK e' stato restituito.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nessuno
- Database: nessuna nuova tabella; gli eventi finiscono nello storico esistente tramite `EventLogRepository`
- Runtime: nessun nuovo vincolo macchina; `RequiredForMachineRun` resta l'unico flag che rende OPC UA bloccante
- Operativita': gli snapshot dati OK non vengono loggati a ogni pezzo per evitare crescita eccessiva DB; vengono loggati failure snapshot e ACK dati ricevuti

## [2026-05-27] OPC UA cambio ricetta per nome o ID database

**File modificati:**
- `Cls_Config/Calss_structure/OpcUaConfig.cs` - aggiunti nodi default `RecipeId` e `CurrentRecipeId`
- `Database/Cls_InitializzeDb.cs` - aggiunto lookup `IdProduzione -> Ricetta` per i comandi OPC UA
- `Services/IOpcUaClientService.cs` - aggiunta pubblicazione `CurrentRecipeId`
- `Services/OpcUaClientService.cs` - gestione comando `RecipeId` server->HMI e scrittura stato `CurrentRecipeId`
- `MainWindow.xaml.cs` - il cambio ricetta OPC UA accetta `RecipeName` oppure `RecipeId`; l'ACK viene scritto dopo risoluzione DB e caricamento reale della ricetta
- `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md` - documentato flusso cambio ricetta per ID e snapshot con `CurrentRecipeId`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.4`

**Motivo:** Il sistema esterno puo' identificare la ricetta sia con il nome file sia con l'ID database `tblproduzione.IdProduzione`. OPC UA deve supportare entrambi i contratti senza duplicare il flusso runtime di caricamento ricetta.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nuovi nodi default `RecipeId` e `CurrentRecipeId`
- Database: nessuna nuova tabella; lettura da `tblproduzione.IdProduzione` e `tblproduzione.Ricetta`
- Runtime: il cambio per ID usa lo stesso caricamento del cambio per nome, con rollback esistente in caso di errore
- Integrazione OPC UA: `CommandAckOk=false` se l'ID non esiste o il caricamento fallisce; `CurrentRecipeId` viene pubblicato con lo stato ricetta

## [2026-05-26] OPC UA handshake - ACK comandi e conferma snapshot dati

**File modificati:**
- `Cls_Config/Calss_structure/OpcUaConfig.cs` - aggiunti nodi default `CommandId`, `CommandAck*`, `DataSequence`, `DataPublish*`, `DataAck*`
- `Services/IOpcUaClientService.cs` - eventi comando arricchiti con `OpcUaCommandEventArgs`; aggiunti metodi per ACK comando e snapshot dati
- `Services/OpcUaConfigurationService.cs` - merge in memoria dei nodi default mancanti per configurazioni OPC UA gia' esistenti
- `Services/OpcUaClientService.cs` - implementato handshake comandi server->HMI, snapshot dati HMI->server e ricezione ACK dati dal server
- `MainWindow.xaml.cs` - Start/Stop/RecipeName confermano ACK solo dopo verifica dell'esito reale; pubblicazione ispezione usa snapshot unico con `DataSequence`
- `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md` - documentata logica handshake e ordine di scrittura variabili
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.3`

**Motivo:** Lo scambio OPC UA aveva write e subscription funzionanti, ma mancava un protocollo esplicito per confermare che il comando fosse stato realmente processato e che lo snapshot dati fosse stato consumato dal server.

**Impatto:**
- Config.xml: nessuno
- OpcUaConfig.xml: nuovi nodi handshake default; le configurazioni esistenti vengono integrate in memoria e in DB con righe mancanti
- Database: nessuna nuova tabella; `cfg_opcua_configuration` riceve nuove righe nodo con `INSERT IGNORE`
- Runtime: nessun blocco macchina aggiuntivo; il vincolo resta controllato solo da `RequiredForMachineRun`
- Integrazione OPC UA: usare `CommandId` monotono per i comandi e `DataAckSequence` per confermare gli snapshot HMI

## [2026-05-26] OPC UA client configurabile - file separato, tabella DB e run vincolante

**File modificati:**
- `QtisVisionPanel.csproj` - aggiunti riferimenti NuGet `OPCFoundation.NetStandard.Opc.Ua` e `OPCFoundation.NetStandard.Opc.Ua.Client`; inclusi i nuovi file OPC UA nella build
- `Cls_Config/AsyncConfigManagerXml.cs` - esposto il path del `Config.xml` per derivare file companion senza aggiungere sezioni alla configurazione principale
- `Cls_Config/Calss_structure/OpcUaConfig.cs` - nuovo modello XML per `OpcUaConfig.xml` e nodi OPC UA default
- `Database/OpcUaConfigurationRepository.cs` - nuova repository per tabella `cfg_opcua_configuration`, seed default e merge XML/DB
- `Database/Cls_InitializzeDb.cs` - creazione automatica tabella `cfg_opcua_configuration` durante inizializzazione DB
- `Services/IOpcUaClientService.cs` - contratto del servizio OPC UA
- `Services/OpcUaConfigurationService.cs` - loader del file separato `OpcUaConfig.xml` e applicazione override DB
- `Services/OpcUaClientService.cs` - client OPC UA, connessione, subscription comandi, write stati, reconnect e gate runtime
- `ServiceLocator.cs` - registrazione e shutdown del servizio OPC UA
- `Services/MachineRuntimeService.cs` - start continuo bloccato quando OPC UA e' abilitato, richiesto e non connesso; pubblicazione stato running
- `MainWindow.xaml.cs` - inizializzazione OPC UA dopo DB, gestione comandi Start/Stop/RecipeName da OPC UA e pubblicazione contatori/esito/ricetta
- `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md` - guida completa configurazione e test
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.2`

**Motivo:** Consentire scambio dati industriale OPC UA con PLC/SCADA/simulatore senza alterare il contratto del `Config.xml` macchina. La comunicazione puo' restare opzionale per banco/sviluppo oppure diventare vincolante in produzione tramite configurazione.

**Impatto:**
- Config.xml: nessuna nuova sezione; viene solo usato il path per creare `OpcUaConfig.xml` nella stessa cartella
- File config: nuovo `OpcUaConfig.xml`, default sicuro con OPC disabilitato
- Database: nuova tabella `cfg_opcua_configuration`; nessuna modifica alle tabelle produzione esistenti
- Runtime: se `Enabled=true` e `RequiredForMachineRun=true`, la macchina non entra in run continuo senza sessione OPC UA connessa
- UI/operativita': nessuna nuova label; comandi OPC UA usano i servizi runtime esistenti
- Collaudo: procedura di test documentata in `opc-ua-integration-2026-05-26.md`

## [2026-05-26] Allarmi scarto - ACK resetta buffer e contatori runtime

**File modificati:**
- `Models/EjectionAlarmManager.cs` - `ResetAlarm` ora azzera `IsTriggered`, `TriggerCount`, `LastTriggered`, contatori consecutivi e buffer percentuale dei difetti monitorati dall'allarme
- `Services/IntegratedAlarmCardService.cs` - `AcknowledgeAlarm` riallinea l'allarme del servizio allo stato resettato dal manager
- `ViewModels/DigitalIOViewModel.cs` - la vista diagnostica I/O aggiorna anche `TriggerCount` quando riceve `AlarmAcknowledged`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.1`

**Motivo:** Il pulsante `ACKNOWLEDGE` spegneva l'uscita e marcava l'allarme come non triggerato, ma lasciava pieni i contatori consecutivi e il buffer percentuale. Per allarmi come `Alarm 3 - Percentage`, il difetto successivo poteva quindi riaprire subito il popup perche' il buffer storico era ancora sopra soglia.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema
- Runtime: ACK resetta lo stato runtime dell'allarme riconosciuto; non azzera i contatori produzione globali
- UI/operativita': dopo ACK il popup non deve ripresentarsi immediatamente al primo difetto successivo se la nuova soglia non e' stata ricostruita
- Localizzazione: nessuna nuova label; non serve aggiornare `ServerMessage` o rigenerare i file lingua

## [2026-05-20] Login operatore - template scuro completo per username history

**File modificati:**
- `Views/LoginWindow.xaml` - aggiunto template locale completo per la `ComboBox` username: area chiusa, textbox interna, freccia e popup restano nello stile scuro della login
- `Views/LoginWindow.xaml.cs` - applicazione template anticipata e riallineata anche dopo il caricamento del controllo
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.9.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.9.0`

**Motivo:** Il solo stile colori non bastava per il template standard della `ComboBox` editabile: dopo la selezione l'area chiusa del controllo restava bianca, rendendo lo username selezionato non leggibile.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema
- Runtime: nessun cambio al flusso autorizzativo o alla history username
- UI/operativita': username digitato o selezionato visibile prima dell'inserimento password
- Localizzazione: nessuna nuova label; non serve aggiornare `ServerMessage` o rigenerare i file lingua

## [2026-05-20] Login operatore - stile dropdown username leggibile

**File modificati:**
- `Views/LoginWindow.xaml` - stile del campo username history allineato al campo password; dropdown scuro con testo bianco e selezione azzurra
- `Views/LoginWindow.xaml.cs` - riallineamento del template editabile interno della `ComboBox` per evitare sfondo bianco/testo invisibile
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.9`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.9`

**Motivo:** Il template standard della `ComboBox` editabile mostrava area testo e lista con sfondo bianco, rendendo lo username poco o per nulla leggibile rispetto allo stile scuro della finestra login.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema
- Runtime: nessun cambio al flusso autorizzativo o alla history username
- UI/operativita': username digitato e suggerimenti recenti restano visibili con lo stesso stile del campo password
- Localizzazione: nessuna nuova label; non serve aggiornare `ServerMessage` o rigenerare i file lingua

## [2026-05-20] Login operatore - history username locale

**File modificati:**
- `Views/LoginWindow.xaml` - campo username convertito in `ComboBox` editabile con lista a discesa al click/focus
- `Views/LoginWindow.xaml.cs` - caricamento history username, salvataggio dopo login riuscito e mantenimento comportamento Enter verso password
- `Services/LoginUserHistoryService.cs` - nuovo servizio locale che persiste solo username recenti, mai password
- `QtisVisionPanel.csproj` - inclusione del nuovo servizio nella build
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.8`

**Motivo:** Rendere piu' rapido il login degli operatori ricorrenti: chi rientra puo' selezionare l'utente gia' usato e inserire solo la password.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema
- Runtime: nessun cambio al flusso autorizzativo; la password resta verificata dal gestore utenti esistente
- UI/operativita': la history e' locale alla postazione/utente Windows e conserva al massimo 20 username recenti

## [2026-05-20] Hardening operatore completato - Job Editor, I/O manuale e permessi fallback

**File modificati:**
- `Services/AuthorizationService.cs` - fallback `Viewer`/`Operator` ristretto: rimossi accessi non tecnici residui e `recipeManagement` dal ruolo `Operator`
- `ViewModels/DigitalIOViewModel.cs` - trigger manuale, uscite generali e comandi tecnici privilegiati richiedono ruolo tecnico, conferma e stop manuale macchina; rimossi testi mojibake residui in log/diagnostica
- `ViewModels/JobToolEditorViewModel.cs` - aggiunte proprieta' per banner hold runtime e visibilita' comandi tecnici; `Save` richiede ruolo tecnico
- `Views/JobToolEditorView.xaml` - banner "macchina ferma per modifica job" e comandi RunOnce/Live/Save/QuickBuild nascosti ai ruoli non tecnici
- `ViewModels/MainViewModel.cs` - navigazione fuori dal Job Tool Editor bloccabile con popup se ci sono modifiche non salvate
- `ServerMessage/ServerMessageStructure.cs` - aggiunte chiavi per banner/prompt Job Tool Editor e stop manuale richiesto
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte traduzioni ENG/ITA e rigenerazione cataloghi lingua runtime/repo
- `Localization/messages_eng.json` / `Localization/messages_ita.json` - aggiornati con le nuove chiavi
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.7`

**Motivo:** Chiudere le voci residue della revisione UI operatore: ridurre i permessi per ruoli non tecnici, rendere piu' guidato il Job Tool Editor e impedire forzature I/O se la macchina non e' stata fermata manualmente.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema; eventuali permessi legacy nel DB vengono filtrati lato autorizzazione per `Viewer`/`Operator`
- Runtime: nessuna modifica al ciclo automatico; i comandi manuali tecnici sono bloccati se non e' attivo lo stop manuale
- UI/operativita': Operator/Viewer vedono una UI produzione piu' pulita; Expert/Installer vedono banner e conferme piu' esplicite nelle aree tecniche

## [2026-05-20] Localizzazione UI operatore - server messages e file lingua riallineati

**File modificati:**
- `ServerMessage/ServerMessageStructure.cs` - aggiunte chiavi messaggio per autorizzazioni tecniche, diagnostica PC e tooltip/banner ricette
- `scripts/UpdateRuntimeLanguageFiles.ps1` - aggiunte traduzioni ENG/ITA e riallineamento anche dei cataloghi versionati `Localization`
- `Localization/messages_eng.json` / `Localization/messages_ita.json` - rigenerati con le nuove chiavi
- `ViewModels/TopMenuBarViewModel.cs` - messaggi diagnostica PC non autorizzata letti da `ServerMessage`
- `ViewModels/AlarmsViewModel.cs` / `Views/UserControls/AlarmsView.xaml` - tooltip e popup diagnostica PC non autorizzata letti da `ServerMessage`
- `ViewModels/DigitalIOViewModel.cs` - messaggio blocco azione tecnica letto da `ServerMessage`
- `ViewModels/PowerFlex525ViewModel.cs` - messaggio blocco azione tecnica letto da `ServerMessage`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.6`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.6`

**Motivo:** Le migliorie operatore precedenti hanno introdotto nuove label e messaggi di permesso. Per mantenere il pannello coerente con il sistema multilingua, le stringhe sono state portate nel catalogo messaggi e generate con lo script lingua.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema
- Runtime: nessun cambio al ciclo macchina
- UI/operativita': testi ENG/ITA allineati tra runtime `C:\QtisVision\Language` e baseline repo `Localization`

## [2026-05-20] Ricette operatore - messaggi permesso e tooltip dinamici

**File modificati:**
- `ViewModels/RecipeManagerViewModel.cs` - aggiunte proprieta' pubbliche per permessi specifici ricetta, banner dinamico e tooltip contestuali; `CanSaveRecipeCommand` ora richiede `saveRecipe`
- `Views/RecipeManagerView.xaml` - banner sola lettura legato al testo dinamico; tooltip dei pulsanti ricetta visibili anche da disabilitati; elimina/duplica legati ai permessi specifici
- `Views/RecipeManagerView.xaml.cs` - rimossa la sovrascrittura code-behind dei testi/tooltip ora gestiti dal viewmodel
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.5`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.5`

**Motivo:** Dopo la separazione tra operatore e ruoli tecnici, la pagina ricette poteva ancora comunicare in modo ambiguo che bastasse il ruolo `Operator` per modificare. Ora la UI distingue tra consultare, caricare in produzione e modificare/salvare ricette.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema; usa permessi esistenti `loadRecipeToProduction`, `saveRecipe`, `deleteRecipe`, `createRecipe`
- Runtime: nessun cambio al caricamento ricetta o al ciclo macchina
- UI/operativita': l'operatore vede perche' un comando e' disabilitato e non viene invitato a fare login con un ruolo insufficiente per la modifica

## [2026-05-19] UI operatore - guardia anti-permessi legacy per ruoli non tecnici

**File modificati:**
- `Services/AuthorizationService.cs` - la pagina ricette viene separata dai permessi di modifica; `Operator` / `Expert` mantengono accesso alla vista, mentre le funzioni tecniche vengono bloccate a `Operator` / `Viewer` anche se presenti nel DB permessi legacy
- `ViewModels/TopMenuBarViewModel.cs` - pulsante diagnostica PC abilitato solo se il ruolo puo' aprire `SystemDiagnostics`
- `Views/UserControls/TopMenuBar.xaml` - diagnostica PC disabilitata con tooltip quando il ruolo non e' tecnico
- `ViewModels/AlarmsViewModel.cs` - pulsante "Apri diagnostica PC" soggetto allo stesso permesso
- `Views/UserControls/AlarmsView.xaml` - pulsante diagnostica PC disabilitato per ruoli non autorizzati
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.4`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.4`

**Motivo:** Dopo il primo hardening, il fallback locale era sicuro ma un DB permessi rimasto largo poteva ancora esporre funzioni tecniche a un operatore non esperto. La guardia runtime rende questi blocchi indipendenti dallo stato storico del DB.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema; eventuali autorizzazioni legacy troppo ampie per `Operator` / `Viewer` vengono ignorate sulle funzioni tecniche protette
- Runtime: nessun cambio al ciclo macchina o mapping I/O
- UI/operativita': l'operatore puo' consultare la pagina ricette e caricare ricette solo se autorizzato, ma non modificare ricette/tool/configurazioni o aprire diagnostiche tecniche

## [2026-05-19] UI operatore - hardening permessi e conferme azioni critiche

**File modificati:**
- `Services/AuthorizationService.cs` - fallback permessi ristretto: `Viewer` solo viste operative/assistenza; `Operator` solo flusso produzione e caricamento ricetta, senza configurazioni tecniche
- `ViewModels/RecipeManagerViewModel.cs` - caricamento ricetta in produzione ora usa `CanLoadRecipeToProduction()` invece di `CanEditRecipe()`
- `ViewModels/ControlButtonsViewModel.cs` - pulsante chiusura HMI abilitato solo per `Expert`, `Installer` o `Administrator`; tooltip esplicito per ruoli non autorizzati
- `ViewModels/DigitalIOViewModel.cs` - conferme e controllo ruolo tecnico per `All outputs HIGH/LOW`, restore backup configurazione, trigger temporizzati manuali e simulazione fotocellula
- `ViewModels/PowerFlex525ViewModel.cs` - conferme e controllo ruolo tecnico per apply parametri drive, reset fault e clear fault history
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.3`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.3`

**Motivo:** Rendere il pannello piu' sicuro per operatori non esperti, riducendo la probabilita' di accesso accidentale a funzioni di commissioning/manutenzione e aggiungendo un secondo passo esplicito sulle azioni che possono muovere uscite, modificare parametri macchina o alterare lo stato del drive.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema; i permessi DB restano autoritativi quando disponibili
- Runtime: nessun cambio al mapping macchina; solo guardie UI/comando
- UI/operativita': Viewer/Operator fallback vedono meno aree tecniche; le azioni critiche richiedono login tecnico e conferma

## [2026-05-14] Job Tool Editor - fix hold residuo dopo modifica tool

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs` - aggiunto stato di unload della view; `OnToolBlockModified()` ignora gli eventi Cognex ricevuti mentre la pagina sta uscendo, evitando di riattivare `job-editor` dopo il rilascio
- `Views/JobToolEditorView.xaml.cs` - durante `Unloaded` gli handler `SubjectChanged` / `Changed` non propagano piu' modifiche tardive al viewmodel
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.2`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.2`

**Motivo:** Se un tool veniva modificato, lo scarico del controllo `CogToolBlockEditV2` poteva generare un evento `Changed` mentre la pagina veniva chiusa. Questo richiamava `OnToolBlockModified()` dopo il rilascio dell'hold e riaggiungeva `job-editor`, lasciando la macchina in `STOPPED` anche sulla pagina `All Channel`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: l'hold `job-editor` non viene piu' riaggiunto dagli eventi tardivi durante unload/dispose
- UI/operativita': dopo modifica tool, uscire dalla pagina torna in `RunContinuous` salvo `Stop` manuale

## [2026-05-14] Job Tool Editor - auto resume RunContinuous all'uscita salvo stop manuale

**File modificati:**
- `Services/MachineRuntimeService.cs` - aggiunto helper `IsContinuousRunHoldActive(reason)` per verificare un hold specifico senza parsing della summary
- `ViewModels/JobToolEditorViewModel.cs` - l'uscita dall'editor rilascia `job-editor` e richiede il ritorno a `RunContinuous` quando non e' attivo `manual-stop`; se l'operatore ha premuto `Stop`, l'auto-resume resta bloccato
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.1`

**Motivo:** Entrando in `Job Tool Editor` la macchina viene fermata con hold runtime per permettere modifica/salvataggio tool, ma uscendo dalla pagina restava necessario premere manualmente `Start`. Per la macchina automatica, l'hold editor deve essere temporaneo e deve ripristinare il running quando l'operatore lascia la pagina, salvo stop manuale esplicito.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: uscita da `Job Tool Editor` -> auto `RunContinuous` se non esiste `manual-stop`
- UI/operativita': il pulsante `Stop` resta autoritativo; dopo stop manuale serve ancora premere `Start`

## [2026-05-14] ACK popup allarmi scarto - reset stato runtime e uscita fisica

**File modificati:**
- `Views/AlarmNotificationWindow.xaml.cs` - `ACKNOWLEDGE` spegne sempre il canale fisico dell'allarme se valido e chiama il reset runtime sul servizio condiviso
- `Services/IntegratedAlarmCardService.cs` - aggiunto `AcknowledgeAlarm(...)` e evento `AlarmAcknowledged` per resettare `IsTriggered` nel singleton e notificare le view
- `Models/EjectionAlarmManager.cs` - aggiunto reset per nome allarme
- `Models/EjectionAlarmConfig.cs` - aggiunto `AlarmAcknowledgedEventArgs`
- `ViewModels/DigitalIOViewModel.cs` - la sub-tab `Allarmi Scarto` aggiorna subito stato e riepilogo quando arriva l'ACK, senza richiedere `Ricarica`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.8.0`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.8.0`

**Motivo:** Il popup veniva mostrato correttamente, ma premendo `ACKNOWLEDGE` veniva chiusa la finestra e, al massimo, resettata l'uscita fisica manuale. Lo stato `IsTriggered` nel singleton degli allarmi restava attivo, quindi lo stesso allarme non poteva scattare di nuovo finche' la pagina `Allarmi Scarto` non veniva ricaricata.

**Impatto:**
- Config.xml: nessuno
- Database: nessuna migrazione schema; audit ACK invariato best-effort
- Runtime: `ACKNOWLEDGE` resetta lo stato attivo dell'allarme e forza bassa l'uscita fisica associata
- UI: griglia e badge `Allarmi Scarto` si aggiornano immediatamente dopo ACK

## [2026-05-14] Revert allarmi scarto config-driven e mapping IO DO02/DO03

**File modificati:**
- `MainWindow.xaml.cs` - `OnAlarmTriggered` torna a usare direttamente `OutputChannelId` salvato sull'allarme; rimossa la risoluzione runtime da `Config.xml` per `BlockingAlarmOutput` / `NonBlockingAlarmOutput`
- `Services/IntegratedAlarmCardService.cs` - rimossa la normalizzazione degli allarmi caricati/aggiunti/aggiornati sui canali configurati; default database inline riportati a `DO0`
- `ViewModels/EjectionAlarmCardViewModel.cs` - creazione nuovi allarmi e test manuale output tornano al canale salvato sull'allarme
- `ViewModels/DigitalIOViewModel.cs` - helper uscita allarme torna a usare `OutputChannelId`; default/migrazione camera riportati a `SIDE=DO01`, `TOP=DO02`, `REJECT=DO03`
- `Database/AlarmCardRepository.cs` - default legacy riportati a `DO0`
- `Models/EjectionAlarmConfig.cs` - default legacy riportato a `DO0`
- `ConfigurationTemplates/machine_runtime_config.template.xml` - template riportato a `OUT_CAMERA_SIDE_TRIGGER=DO01`, `OUT_CAMERA_TOP_TRIGGER=DO02`, `OUT_REJECT_SOLENOID=DO03`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.7.9`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.7.9`

**Motivo:** Le modifiche `1.1.7.7` e `1.1.7.8` non hanno dato il comportamento atteso sull'applicazione. La baseline torna quindi al flusso precedente per allarmi scarto e mapping IO template.

**Impatto:**
- Config.xml: nessuna rottura schema; `BlockingAlarmOutput` e `NonBlockingAlarmOutput` non sono piu' la sorgente autoritativa runtime degli allarmi scarto
- Machine runtime config: template/default riportati alla baseline precedente; i file macchina esistenti non vengono sovrascritti automaticamente
- Database: nessuna migrazione schema; `cfg_alarm_cards.SignalID` / `OutputChannelId` tornano a guidare l'uscita fisica dell'allarme
- Runtime: eventuali allarmi abilitati con `OutputChannelId=DO0` possono nuovamente comandare fisicamente `DO0`, come nella baseline precedente

## [2026-05-13] Allarmi scarto config-driven - uscita fisica risolta da Config.xml

**File modificati:**
- `MainWindow.xaml.cs` - `OnAlarmTriggered` non usa piu' direttamente `OutputChannelId` salvato sull'allarme; risolve l'uscita effettiva da `Config.xml` in base a `SignalType` (`BlockingAlarmOutput` / `NonBlockingAlarmOutput`) e logga canale salvato vs canale configurato
- `Services/IntegratedAlarmCardService.cs` - gli allarmi caricati da DB/XML, aggiunti o aggiornati vengono normalizzati alla configurazione corrente prima del sync con `EjectionAlarmManager`
- `ViewModels/EjectionAlarmCardViewModel.cs` - il test manuale uscita allarme usa il canale configurato; validazione canale aggiornata a `DO0..DO63` ma solo come valore letto da configurazione
- `ViewModels/DigitalIOViewModel.cs` - helper di uscita allarme riallineato alla stessa risoluzione da `Config.xml`
- `Database/AlarmCardRepository.cs` - default legacy rimossi da `DO0`; default allarmi bloccanti/non bloccanti riallineati a `DO40` / `DO41`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.7.8`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.7.8`

**Motivo:** Se l'uscita veniva modificata dal pannello/config, alcune parti potevano continuare a considerare il vecchio valore salvato in `cfg_alarm_cards.SignalID` / `OutputChannelId`. Ora la sorgente autorevole e' il `Config.xml`, cosi' un cambio di uscita configurata viene rispettato dal runtime e non resta vincolato a righe DB storiche.

**Impatto:**
- Config.xml: nessuna rottura schema; `BlockingAlarmOutput` e `NonBlockingAlarmOutput` diventano la sorgente runtime degli output allarme
- Database: nessuna migrazione schema; le righe allarme vengono riallineate in memoria e al prossimo save/update
- Runtime: eventuali `DO0` / `DO1` storici non possono piu' comandare uscite fisiche se non sono esplicitamente configurati nel `Config.xml`

## [2026-05-13] Fix IO DO0/DO1 - guardia allarmi e mapping trigger camere DO02/DO03

**File modificati:**
- `MainWindow.xaml.cs` - `OnAlarmTriggered` ora scrive l'uscita fisica solo se `OutputChannelId` corrisponde a `Config.xml -> BlockingAlarmOutput` o `NonBlockingAlarmOutput`; canali legacy/stale come `DO0` e `DO1` vengono saltati e loggati con `ALARM_IO_SKIPPED_UNCONFIGURED_CHANNEL`
- `ConfigurationTemplates/machine_runtime_config.template.xml` - template riallineato alla macchina reale: `OUT_CAMERA_SIDE_TRIGGER=DO02`, `OUT_CAMERA_TOP_TRIGGER=DO03`, `OUT_REJECT_SOLENOID=DO04`; note aggiornate per lasciare `DO00`/`DO01` libere
- `ViewModels/DigitalIOViewModel.cs` - default runtime e migrazione legacy aggiornati a `SIDE=DO02`, `TOP=DO03`, `REJECT=DO04`, evitando che un recovery/config nuova ripristini il vecchio `SIDE=DO01`
- `Models/EjectionAlarmConfig.cs` - default legacy `DO0` sostituito con `DO40`
- `ViewModels/EjectionAlarmCardViewModel.cs` - creazione nuovi allarmi: `SignalID` e `OutputChannelId` vengono valorizzati dalla configurazione (`BlockingAlarmOutput` / `NonBlockingAlarmOutput`)
- `Services/IntegratedAlarmCardService.cs` - allarmi default database inizializzati con `DO40` / `DO41` invece di ereditare `DO0`
- `Properties/AssemblyInfo.cs` - release aggiornata a `1.1.7.7`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release `1.1.7.7`

**Motivo:** In macchina `DO0` restava alta e `DO1` impulsava quando arrivava un trigger/prodotto perché il flusso allarmi scarto aveva ancora default storici `Blocking=DO0`, `NotBlocking=DO1`. Il blocking con `PulseDurationMs=0` resta latchato alto per progetto. In parallelo alcuni default/template camera erano ancora `SIDE=DO01`, creando confusione rispetto al cablaggio reale `SIDE=DO02`, `TOP=DO03`.

**Impatto:**
- Config.xml: nessuna rottura schema; `BlockingAlarmOutput` e `NonBlockingAlarmOutput` diventano il gate runtime per autorizzare scritture fisiche degli allarmi
- Machine runtime config: template/default aggiornati; i file macchina esistenti vanno verificati in commissioning ma non vengono sovrascritti automaticamente
- Database: nessuna migrazione schema; eventuali righe `cfg_alarm_cards` con `DO0`/`DO1` non alzano piu' uscite fisiche se non corrispondono alle uscite allarme configurate
- Runtime: i trigger camera devono uscire solo dai segnali configurati `OUT_CAMERA_SIDE_TRIGGER` e `OUT_CAMERA_TOP_TRIGGER`; gli allarmi stale vengono tracciati nei log e non energizzano `DO0`/`DO1`

## [2026-05-13] Sostituzione globale MessageBox.Show con SystemNotificationWindow

**File modificati:**
- `Views/SystemNotificationWindow.xaml` — aggiunto `NoButton` nel footer per modalità conferma (Sì/No)
- `Views/SystemNotificationWindow.xaml.cs` — aggiunto `bool Confirmed`, costruttore overload `(title, message, severity, isConfirmation)`, handler `NoButton_Click`, `CloseButton_Click` ora setta `Confirmed=true`
- `App.xaml.cs` — 3 sostituzioni (Info, Error, Error)
- `SplashWindow.xaml.cs` — 1 sostituzione (Error)
- `Views/AboutVersionWindow.xaml.cs` — 2 sostituzioni (Info, Error)
- `Views/RecipeManagerView.xaml.cs` — 3 sostituzioni (Info, YesNo→Confirmed, Info)
- `Views/JobToolEditorView.xaml.cs` — 1 sostituzione (Error)
- `Views/DbQuatis.xaml.cs` — 1 sostituzione (Error)
- `Views/NavigationMenu.xaml.cs` — 1 sostituzione (Error)
- `Views/UserControls/DataInspectorView.xaml.cs` — 1 sostituzione (Warning)
- `Views/UserControls/StatisticsView.xaml.cs` — 1 sostituzione (Error)
- `ViewModels/AssistanceViewModel.cs` — 2 sostituzioni (Info, Error)
- `ViewModels/AlarmsViewModel.cs` — 3 sostituzioni YesNo→Confirmed (Warning)
- `ViewModels/AuthorizedViewModel.cs` — 1 sostituzione (Warning)
- `ViewModels/ControlButtonsViewModel.cs` — 4 sostituzioni (YesNo→Confirmed, Warning, Error, Error)
- `ViewModels/InspectionCounterViewModel.cs` — 3 sostituzioni (YesNo→Confirmed, Info, Error)
- `ViewModels/EjectionAlarmCardViewModel.cs` — 2 sostituzioni (Info, YesNo→Confirmed); linea 730 YesNoCancel mantenuta
- `ViewModels/TopMenuBarViewModel.cs` — 6 sostituzioni (Info×3, Error, YesNo→Confirmed, Info)
- `ViewModels/NavigationViewModel.cs` — 4 sostituzioni (Error, Warning, Warning, Error)
- `ViewModels/ManualViewModel.cs` — 2 sostituzioni (Info, Error)
- `ViewModels/JobToolEditorViewModel.cs` — 9 sostituzioni (Error×8, Info)
- `ViewModels/PreferenceViewModel.cs` — 2 sostituzioni (YesNo→Confirmed, Info/Warning)
- `ViewModels/RecipeManagerViewModel.cs` — 12 sostituzioni (Warning, Info, Error, YesNo→Confirmed, Info, Error, Warning, Warning, Warning, Info, Error, Info)
- `Services/RecipeAuthorizationService.cs` — 1 sostituzione (Warning)
- `MainWindow.xaml.cs` — 7 sostituzioni (YesNo→Confirmed+Info, Error×3, YesNo→Confirmed nel Dispatcher)

**Motivo:** Uniformare tutti i dialoghi applicativi al design system dark-themed introdotto con `SystemNotificationWindow`, eliminando le finestre Windows native (`MessageBox.Show`) dall'intera codebase.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `SystemNotificationWindow` ora espone `bool Confirmed` e costruttore overload per modalità conferma
- Comportamento runtime: tutti i popup usano ora il popup dark-themed; i dialoghi YesNo/conferma usano `isConfirmation=true` con bottoni Sì/No; un solo dialog YesNoCancel in `EjectionAlarmCardViewModel.cs:730` mantenuto invariato

## [2026-05-12] Release 1.1.7.6 - consolidamento baseline e sync OneDrive

**File modificati:**
- `Properties/AssemblyInfo.cs` - versione assembly aggiornata da `1.1.7.5` a `1.1.7.6` tramite script canonico `scripts/IncrementSoftwareRelease.ps1`
- `Documentation/MachineHardware/software-version-archive.md` - aggiunta release ufficiale `1.1.7.6` con riepilogo consolidato di PreferenceView Config.xml editor, DB hardening, alarm/ejection persistence, notification windows, diagnostics, audit trail, recipe transition e counter scale-up
- `Documentation/MachineHardware/code-changes-log.md` - aggiunta traccia di release e riallineamento baseline condivisa

**Motivo:** Chiudere gli aggiornamenti locali maturi in una release ufficiale prima del riallineamento verso la cartella condivisa OneDrive usata dal collega.

**Impatto:**
- Config.xml: nessuna rottura schema; i campi applicativi esistenti sono ora documentati come editabili da PreferenceView
- Database: documentati `alarm_events`, indice `tblgenerale.idx_DataeOra`, migrazione `tblglobalcounters.Counter` a `BIGINT` e pooling connessioni
- Runtime/UI: release cumulativa per notifiche stilizzate, health check, watchdog IO, audit, transizioni ricetta e diagnostica
- Baseline condivisa: il sync locale -> OneDrive esclude `.git`, `.vs`, `bin`, `obj`, file binari/generati e mantiene intatte le cartelle backup presenti solo in OneDrive

## [2026-05-12] PreferenceView — editor completo Config.xml (tutte le sezioni)

**File modificati:**
- `Views/PreferenceView.xaml` — aggiunte 8 nuove sezioni nella Config Editor card: VisionPro outputs (12 campi), Auto-switch settings (7 campi), Diagnostics thresholds (14 campi); IO/Ejection (3 campi), Image save policy (8 campi), PowerFlex 525 (9 campi), DB archive & cleanup (13 campi), Analytics dashboard (4 campi + itemscontrol card visibility); espanse sezioni esistenti: Percorsi (+Compagny, +DataHostnames, +NumCamera, +DayToCleanData), Database (+AuthPlugin, +SslDisabled); ogni sezione ha accent border colorato per distinzione visiva
- `ViewModels/PreferenceViewModel.cs` — aggiunte 6 proprietà pass-through: `RuntimeIO`, `RuntimeSaveImage`, `RuntimeAutoSwitch`, `RuntimeDiagnostics`, `RuntimePowerFlex`, `RuntimeAnalytics`; `LoadMainConfigFileAsync` e `RefreshAdministratorState` notificano le 6 nuove proprietà via `OnPropertyChanged`

**Motivo:** Solo ~20 dei 60+ campi dell'AppConfig erano editabili dalla UI; l'operatore doveva aprire Config.xml direttamente per modificare IO timing, diagnostics, PowerFlex, analytics e policy salvataggio immagini

**Impatto:**
- Config.xml: tutti i campi ora scrivibili dalla UI tramite il pulsante "Salva Config.xml" esistente
- Database: nessuno
- API pubblica: 6 nuove proprietà read-only nel `PreferenceViewModel`
- Comportamento runtime: la PreferenceView è ora l'editor completo di Config.xml; la sezione Analytics card visibility permette di abilitare/disabilitare i grafici del dashboard

## [2026-05-11] PreferenceView — refactoring layout card (WrapPanel → Grid asimmetrico)

**File modificati:**
- `Views/PreferenceView.xaml` — sostituito `WrapPanel` con `Grid` a 3 colonne (`290px | * | 290px`); card Language e UserMgmt hanno larghezza fissa, card ConfigEditor prende lo spazio restante (`*`); rimossi i DataTrigger `Grid.ColumnSpan` che non funzionavano in WrapPanel; flag preview nella card Language resa compatta (orizzontale, 40×30 px, al posto della preview verticale 64×48 px); aggiunto accent border sinistro colorato per ogni sezione della Config card (purple, teal, green, amber, blue); padding e margini uniformati
- `Views/PreferenceView.xaml.cs` — `UpdateResponsiveLayout` riscritta: ora imposta `ColumnDefinition.Width` sul `Grid` invece di chiamare `ApplyCardWidth` (rimossa); `ViewmodelOnPropertyChanged` reagisce anche a `CanManageConfigFile` e `IsAdministrator` per ricalcolare le colonne alla visibilità delle card; rimossa `ApplyCardWidth`

**Motivo:** Le card ricevevano tutte la stessa larghezza calcolata dal WrapPanel responsive; la Config Editor (più complessa, con 5 sezioni e 2 colonne interne) richiedeva la maggior parte dello spazio disponibile, mentre Language e UserMgmt sono card laterali fisse; il WrapPanel causava wrapping delle card a risoluzioni medie

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: layout PreferenceView ora fisso a 3 colonne asimmetriche; column widths ricalcolati dinamicamente al resize e al cambio ruolo utente

## [2026-05-11] SystemNotificationWindow — popup stilizzato per sostituire MessageBox

**File modificati:**
- `Views/SystemNotificationWindow.xaml` — nuova Window styled: dark theme, angoli arrotondati, drop shadow; supporta tre severità (Info/Warning/Error) con palette colore distinta per header, icona, bordo e pulsante OK
- `Views/SystemNotificationWindow.xaml.cs` — code-behind; enum `NotificationSeverity`; colori applicati a runtime su elementi con nome; `CloseButton.Tag` usato come binding source per il colore del pulsante nel ControlTemplate
- `Services/DialogService.cs` — `ShowInfo`, `ShowWarning`, `ShowError` sostituiti: `MessageBox.Show` → `new SystemNotificationWindow(...).ShowDialog()`
- `QtisVisionPanel.csproj` — aggiunto `<Compile>` e `<Page>` per i due nuovi file

**Motivo:** I popup di sistema (`ShowWarning` — "Recupero VisionPro fallito", "Uscita fisica non disponibile") avevano lo stile nativo Windows, in contrasto con il popup allarmi (`AlarmNotificationWindow`) già stilizzato; il nuovo componente mantiene coerenza visiva

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `NotificationSeverity` enum aggiunto in `QtisVisionPanel.Views`; firma `DialogService.Show*` invariata
- Comportamento runtime: tutti i popup informativi/warning/error passano per `SystemNotificationWindow` al posto di `MessageBox.Show`

## [2026-05-11] Consolidamento F2-2, F2-4, F2-6 — transazioni DB, contatori long, validazione nome database

**File modificati:**
- `Database/Cls_CheckUser.cs` — aggiunta validazione regex `^[a-zA-Z0-9_]+$` in `EnsureDatabaseExistsAsync` prima di `CREATE DATABASE`; blocca nomi di database non validi invece di passarli direttamente nella DDL (F2-6)
- `Database/CounterManager.cs` — tutti i campi privati e le proprietà pubbliche dei contatori cambiati da `int` a `long` (`_total`, `_good`, `_noGood`, tutti i `_*Defects`); `GetAllCounters()` restituisce ora `Dictionary<string, long>`; deserializzazione JSON aggiornata; `CounterDbFlusher` aggiornato con generics `long`; `ShiftSummary.Total/Good/NoGood` da `int` a `long` (F2-4)
- `Database/Cls_InitializzeDb.cs` — `SaveCountersAsync` e `SaveCountersToDatabaseInternalAsync` aggiornati a `Dictionary<string, long>`; rimosso il loop `for` spezzato (iterava tutti i contatori invece del solo batch); aggiunta `MySqlTransaction` che avvolge l'intera operazione con `Commit`/`Rollback` atomico; colonna `tblglobalcounters.Counter` cambiata da `int(11)` a `BIGINT`; aggiunto `ALTER TABLE ... MODIFY COLUMN Counter BIGINT` per migrare installazioni esistenti; usa `DbConnectionStringHelper.Build` (F2-2 + F2-4)
- `Models/CounterData.cs` — `CounterData.Value` e `DefectItem.Count` da `int` a `long` (F2-4)
- `ViewModels/InspectionCounterViewModel.cs` — `LoadCountersFromDatabaseAsync` restituisce `Dictionary<string, long>`, `reader.GetInt64` al posto di `GetInt32`; `UpdateCountersFromDictionary`, `UpdateFromGlobalCounters`, `UpdateSelectionCounter`, `UpdateDefectCounter`, `GetSelectionCounterValueByIndex`, variabili locali `totalInspections/goodInspections/noGoodInspections/totalDefectEvents` e `FormatPercentage` aggiornati da `int` a `long` (F2-4)
- `ViewModels/TopMenuBarViewModel.cs` — `UpdateQualityIndex(int, int)` → `(long, long)`; variabili `out long` nel `TryGetValue` (F2-4)
- `Views/UserControls/StatisticsView.xaml.cs` — `UpdateCountersFromDictionary`, `UpdateCountersFromGlobal`, `UpdateSelectionCounter`, `UpdateDefectCounter` aggiornati a `long` (F2-4)

**Motivo:** Chiudere i 6 punti architetturali post-prioritari: validazione sicura DDL nome database, prevenzione SQL injection via DDL; contatori a `long` per futuro scale-up; salvataggio contatori atomico con rollback su errore

**Impatto:**
- Config.xml: nessuno
- Database: colonna `tblglobalcounters.Counter` migrata da `int(11)` a `BIGINT` (idempotente via `ALTER TABLE MODIFY COLUMN`)
- API pubblica: `CounterManager.GetAllCounters()` ora restituisce `Dictionary<string, long>`; proprietà `Total/Good/NoGood/…Defects` ora `long`; `CounterData.Value` e `DefectItem.Count` ora `long`; `UpdateSelectionCounter`/`UpdateDefectCounter` accettano `long`
- Comportamento runtime: salvataggio contatori DB ora atomico; nome database validato all'inizializzazione utente

## [2026-05-11] Consolidamento F0-1, F0-4, F0-6, F1-5, F2-1 — catch silenti, pooling, indici, CogImage dispose

**File modificati:**
- `Database/Cls_InitializzeDb.cs` — aggiunto `await EnsureIndexAsync(..., "idx_DataeOra", "ALTER TABLE tblgenerale ADD INDEX idx_DataeOra (DataeOra)")` nella sequenza di `CreateTblGenerale`; colma il gap di query lente su `DataeOra` dopo mesi di produzione (F0-6)
- `Database/clsConnection.cs` — aggiunta classe statica `DbConnectionStringHelper.Build(...)`: costruisce stringhe di connessione standardizzate con `Pooling=true`, `MinimumPoolSize=2`, `MaximumPoolSize=10`, `ConnectionLifeTime=300`, `ConnectionTimeout=5`; tutti i callers critici devono usare questo helper invece di stringhe manuali (F2-1 + F0-4)
- `Database/AlarmCardRepository.cs` — usa `DbConnectionStringHelper.Build(...)` al posto della stringa raw (F2-1 + F0-4)
- `Database/GlobalCounterService.cs` — idem (F2-1 + F0-4)
- `Database/Cls_CheckUser.cs` — usa `DbConnectionStringHelper.Build(...)` per `_connectionString`; in `EnsureDatabaseExistsAsync` usa l'helper + `CancellationTokenSource(5s)` per evitare hang di 30 s se MySQL è offline (F0-4)
- `Services/IntegratedAlarmCardService.cs` — usa `DbConnectionStringHelper.Build(...)` nella copia interna di `AlarmCardRepository`; aggiunto `using QtisVisionPanel.Database` (F2-1)
- `MainWindow.xaml.cs` — `SetRecord` chiama `(field as IDisposable)?.Dispose()` prima di sovrascrivere il riferimento; evita accumulo memoria Cognex nativa a 180 ppm (F1-5)
- `Models/AdvantechDeviceManager.cs` — catch vuoto nel loop di scan PCIE-1884 (line 323) sostituito con `catch (Exception ex) { _logger.Warn(ex, "HW_SCAN_1884_ENUM_ERROR") }`; catch vuoti in `Dispose()` per polling wait e interrupt unsubscribe ora loggano a Warn (F0-1)
- `Views/UserControls/DisplayRecord/TopCameraView.xaml.cs` — catch vuoto su `StopLiveDisplay()` ora logga `TOP_LIVE_DISPLAY_STOP_FAILED` via `MainWindow.logger?.Warn` (F0-1)
- `Views/UserControls/DisplayRecord/SideCameraView.xaml.cs` — idem `SIDE_LIVE_DISPLAY_STOP_FAILED` (F0-1)
- `Views/UserControls/DisplayRecord/FrontCameraView.xaml.cs` — idem `FRONT_LIVE_DISPLAY_STOP_FAILED` (F0-1)
- `Views/UserControls/DisplayRecord/BottomCameraView.xaml.cs` — idem `BOTTOM_LIVE_DISPLAY_STOP_FAILED` (F0-1)
- `Views/UserControls/DisplayRecord/RearCameraView.xaml.cs` — idem `REAR_LIVE_DISPLAY_STOP_FAILED` (F0-1)

**Motivo:** Chiudere i 5 punti aperti prioritari per il deploy in produzione 24/7: indice query lente, pooling connessioni, dispose memoria Cognex, resilienza startup MySQL offline, catch silenti che nascondono guasti hardware

**Impatto:**
- Config.xml: nessuno
- Database: nuovo indice `idx_DataeOra` su `tblgenerale` (creato idempotente via `EnsureIndexAsync`)
- API pubblica: `DbConnectionStringHelper` nuova classe pubblica statica; nessuna firma esistente cambiata
- Comportamento runtime: connessioni DB ora con timeout 5 s e pool dimensionato; `SetRecord` dispone CogRecord precedente; errori hardware visibili nei log; HMI non si blocca 30 s se MySQL è offline

## [2026-05-11] Consolidamento F1-4, F3-3, F1-6 — persistenza allarmi, banner warning, counter recovery visione

**File modificati:**
- `Database/Cls_InitializzeDb.cs` — aggiunta chiamata `await CreateAlarmEventsTableAsync(dbManager)` nella sequenza di `Initialize()`, subito dopo `CreateCfgInspectionTable`; la tabella `alarm_events` (già definita come metodo privato) viene ora creata al primo avvio
- `Database/AlarmCardRepository.cs` — aggiunto metodo `LogAlarmEventAsync(AlarmTriggeredEventArgs)`: INSERT best-effort in `alarm_events` con i campi `AlarmName`, `TriggerTime`, `DefectsJson` (serializzazione manuale da `DefectCounts`), `OutputChannel`, `DurationMs`; usa connessione dedicata con timeout 5 s; eccezioni catturate via NLog senza propagazione
- `Services/IntegratedAlarmCardService.cs` — `OnAlarmTriggered` chiama ora `_repository.LogAlarmEventAsync(e).SafeFireAndForget()` per persistere ogni evento di allarme su DB senza bloccare il thread; aggiunto `using QtisVisionPanel.Extensions`
- `ViewModels/MainViewModel.cs` — aggiunta proprietà `public SystemDiagnosticsViewModel DiagnosticsVM { get; }` inizializzata nel costruttore dopo `SystemDiagnosticsService.Start()`; espone i dati diagnostici al binding XAML della shell
- `MainWindow.xaml` — aggiunto `xmlns:conv` (namespace `QtisVisionPanel.Converters`); aggiunto `BooleanToVisibilityConverter` nelle risorse della Window; inserita nuova `RowDefinition Height="Auto"` tra TopMenuBar e contenuto principale; aggiunto `Border` (banner giallo ambra) nella Row 2 con binding `Visibility` su `DiagnosticsVM.HasSystemWarning` e testo su `DiagnosticsVM.SystemWarningText`; spostata la Grid del contenuto a `Grid.Row="3"`
- `MainWindow.xaml.cs` — aggiunto campo `private int _visionRecoveryAttemptCount = 0`; in `AttemptVisionAutoRecoveryAsync`: incremento del contatore all'inizio di ogni tentativo; reset a 0 dopo un recupero riuscito; se il recupero fallisce per la terza volta (`>= 3`) mostra dialog di allerta operatore tramite `ServiceLocator.DialogService.ShowWarning` su Dispatcher con priorità Normal, poi azzera il contatore

**Motivo:**
- F1-4: gli allarmi non venivano mai persistiti su DB; al riavvio tutta la storia degli scatti allarme andava persa; ora ogni evento è tracciabile e interrogabile
- F3-3: l'infrastruttura dati diagnostici era già pronta in `SystemDiagnosticsService`/`SystemDiagnosticsViewModel` ma non collegata al layout della shell
- F1-6: il sistema poteva ciclare indefinitamente nei tentativi di recupero VisionPro senza mai avvisare l'operatore

**Impatto:**
- Config.xml: nessuno
- Database: nuova tabella `alarm_events` creata alla prima inizializzazione
- API pubblica: `MainViewModel` espone nuova proprietà `DiagnosticsVM` (non breaking — aggiunta)
- Comportamento runtime: ogni allarme scattato viene ora scritto su DB in background; banner di avviso compare automaticamente in shell se i diagnostici rilevano stato non `Healthy`; dopo 3 recuperi VisionPro falliti consecutivi l'operatore riceve un avviso esplicito

## [2026-05-08] Consolidamento F5 — JSON logging, turno produttivo, RecipeTransitionService

**File modificati:**
- `Nlog.config` — aggiunto target `jsonfile`: file JSON giornaliero rotante (`events.json`, max 90 file); tutte le righe WARN+ vengono anche scritte in JSON; layout `JsonLayout` con campi `ts`, `level`, `logger`, `msg`, `ex`; permette query programmatiche sui log di macchina
- `Database/CounterManager.cs` — aggiunte proprietà pubbliche `CurrentShiftName`, `CurrentShiftOperator`, `ShiftStartTime`; aggiornato `ResetForShiftAsync(string, string?)` con parametro operatore opzionale; aggiunto `GetShiftSummary()` → `ShiftSummary` (snapshot immutabile turno corrente); aggiunta classe `ShiftSummary` nello stesso file
- `Services/RecipeTransitionService.cs` — nuovo file; `ChangeRecipeAsync(newRecipeName, progressWindow?, ct)` con snapshot della ricetta corrente prima del cambio, rollback automatico se il cambio fallisce (`InitializeRecipeAsync(previousRecipe)` + `InitializeComponentforChangeRecipe`), log `RECIPE_CHANGE_START/SUCCESS/FAILED/ROLLBACK_*` via NLog, audit `RECIPE_CHANGE` via `AuditLogService`; non sposta codice da MainWindow — la decomposizione completa (F5-1 full) rimane lavoro futuro
- `ServiceLocator.cs` — aggiunto `RecipeTransitionService` lazy singleton + reset in `Reset()`
- `QtisVisionPanel.csproj` — registrato `Services\RecipeTransitionService.cs`

**Motivo:** F5-2 (logging strutturato JSON), F5-3 (concetto turno produttivo), F5-1 prima estrazione sicura da MainWindow

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `CounterManager.ResetForShiftAsync` ha nuovo parametro opzionale (compatibile); aggiunti `CurrentShiftName`, `CurrentShiftOperator`, `ShiftStartTime`, `GetShiftSummary()`; `ServiceLocator.RecipeTransitionService` aggiunto
- Comportamento runtime: cambio ricetta fallito ora tenta rollback automatico alla ricetta precedente; log WARN+ duplicati anche in `events.json`

## [2026-05-08] Bugfix — EnsureIndexAsync chiudeva la connessione condivisa

**File modificati:**
- `Database/Cls_InitializzeDb.cs` — `EnsureIndexAsync`: rimosso `using` da `var conn = dbManager.GetConnection()`; la chiamata `using var conn` eseguiva `Dispose()` sulla `MySqlConnection` condivisa, chiudendola; tutti i `Create*` chiamati dopo `CreateTblGenerale` trovavano la connessione chiusa con errore "Connection must be valid and open"

**Motivo:** `dbManager.GetConnection()` restituisce un riferimento alla connessione di proprietà di `DbConnectionManager`; il chiamante non deve disporne

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: `Initialize()` non fallisce più a `CreateTblProduzione` dopo che `CreateTblGenerale` ha chiamato `EnsureIndexAsync`

## [2026-05-08] Consolidamento F4 — audit trail, BCrypt password migration

**File modificati:**
- `Services/AuditLogService.cs` — nuovo file; scrive eventi di sicurezza nella tabella `audit_log`; `LogAsync(eventType, actor, details, oldValue?, newValue?, CancellationToken)` best-effort con timeout 5 s; eccezioni catturate via NLog, mai propagate; eventi attesi: LOGIN_SUCCESS, LOGIN_FAILURE, ALARM_ACKNOWLEDGE, CONFIG_CHANGE (estendibile da qualsiasi servizio tramite `ServiceLocator.AuditLogService`)
- `Database/Cls_InitializzeDb.cs` — aggiunto `CreateAuditLogTableAsync(DbConnectionManager)`: crea tabella `audit_log` (Id, Timestamp datetime(3), EventType, Actor, Details, OldValue, NewValue + indici su Timestamp e EventType); chiamato in `Initialize()` dopo `CreateCfgInspectionTable`
- `Database/Cls_CheckUser.cs` — `GetUserRoleAsync`: dual-verify BCrypt (hash `$2...`) poi fallback SHA1; se SHA1 corrisponde → auto-upgrade silenzioso a BCrypt sulla stessa connessione (UPDATE users); log `BCRYPT_UPGRADE_OK/FAILED` via NLog; audit `LOGIN_SUCCESS`/`LOGIN_FAILURE` via `ServiceLocator.AuditLogService.LogAsync` (SafeFireAndForget); `InsertNewUsersAsync`: nuovi utenti hashed con BCrypt (se hash già BCrypt o SHA1 passa invariato); helper statici aggiunti: `HashPasswordBCrypt`, `VerifyBCrypt`, `LooksLikeBCryptHash`; usando BouncyCastle `OpenBsdBCrypt` (dipendenza già presente, no nuovo NuGet)
- `Views/AlarmNotificationWindow.xaml.cs` — `AcknowledgeButton_Click`: dopo il reset uscita I/O chiama `ServiceLocator.AuditLogService.LogAsync("ALARM_ACKNOWLEDGE", UserSession.CurrentUser, "alarm=...|channel=...|trigger=...")` SafeFireAndForget
- `ServiceLocator.cs` — aggiunto campo `_auditLogService`, proprietà `AuditLogService` (lazy double-check), warm-up in `Initialize()`, reset in `Reset()`
- `QtisVisionPanel.csproj` — registrato `Services\AuditLogService.cs`

**Motivo:** tracciabilità degli accessi e delle azioni critiche; password SHA1 deboli rilevate nel codice esistente

**Impatto:**
- Config.xml: nessuno
- Database: nuova tabella `audit_log` nel DB principale (IF NOT EXISTS — non distruttivo su upgrade)
- API pubblica: `ServiceLocator.AuditLogService` aggiunto; firma `GetUserRoleAsync` invariata — solo comportamento interno cambiato
- Comportamento runtime: primo login con password SHA1 aggiorna automaticamente l'hash a BCrypt (UPDATE users); nessun impatto visibile sull'utente; log `BCRYPT_UPGRADE_OK` su NLog

## [2026-05-08] Consolidamento F3 — auto-diagnosi e health check startup

**File modificati:**
- `Services/StartupHealthChecker.cs` — nuovo file; `StartupHealthChecker` esegue 4 check al lancio: DB connectivity (3 s timeout), VPP file (cerca `LastRecipe` su disco), spazio disco (< 5 GB = Critical, < 20 GB = Warning), scheda I/O (IsInitialized); log `STARTUP_HEALTH_CHECK` con tutti gli stati; mai blocca l'avvio — restituisce `HealthCheckReport` con `SummaryText`; `HealthStatus` enum (OK/Warning/Degraded/Critical/Unavailable/Unknown)
- `SplashWindow.xaml.cs` — aggiunto step health check dopo `InitializeDatabaseAsync`: instanzia `StartupHealthChecker`, chiama `RunAsync` con `Progress<string>`, mostra `healthReport.SummaryText` nella barra splash
- `DataManage/RecipeValidator.cs` — nuovo file; `RecipeValidator` con overload `Validate()` per `General_Info`, `CameraSetting`, `RecipeParamTop3D`, `RecipeParamSide`; restituisce `ValidationResult` (immutabile, `IsValid`, `IReadOnlyList<string> Errors`); nessuna eccezione — solo raccolta errori; da usare prima di applicare parametri ricetta alla macchina
- `ViewModels/SystemDiagnosticsViewModel.cs` — aggiunte proprietà: `CurrentPpm` (legge `ServiceLocator.CounterManager.GetCurrentPpm()`), `CurrentPpmText` (formattato), `HasSystemWarning` (true se `OverallSeverity != Healthy`), `SystemWarningText` (testo banner per MainWindow); le nuove proprietà si aggiornano su ogni tick del `SystemDiagnosticsService` tramite `DiagnosticsService_PropertyChanged`
- `QtisVisionPanel.csproj` — registrati: `DataManage\RecipeValidator.cs`, `Services\StartupHealthChecker.cs`

**Motivo:** Fase F3 — il sistema deve sapere quando qualcosa non va all'avvio e durante la produzione. Tre punti critici: (1) startup silenzioso anche con DB offline o disco quasi pieno; (2) nessun modo di misurare la velocità di produzione in tempo reale; (3) parametri ricetta potenzialmente invalidi (valori negativi) applicati senza validazione.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `SystemDiagnosticsViewModel` aggiunge 4 proprietà read-only; `RecipeValidator` è nuovo servizio standalone; `StartupHealthChecker` è standalone
- Comportamento runtime: ogni avvio logga `STARTUP_HEALTH_CHECK` con tutti i sottosistemi; `SplashWindow` mostra stato DB/disco/ricetta; `SystemDiagnosticsView` ora espone `CurrentPpmText` e `SystemWarningText` disponibili per binding XAML

---

## [2026-05-08] Consolidamento F2 — solidità database e configurazione

**File modificati:**
- `Database/Cls_InitializzeDb.cs`
  - Aggiunto `using System.Text.RegularExpressions`
  - Aggiunto metodo statico `IsValidIdentifier(string)` — whitelist `^[a-zA-Z0-9_]+$`, max 64 caratteri
  - `CreateDatabase`: valida `dbName` con `IsValidIdentifier` prima del DDL; se non valido logga `DB_INVALID_NAME` e lancia `InvalidOperationException` (previene SQL injection in DDL)
  - Aggiunto `CreateRetentionEventAsync`: crea MySQL EVENT `qtis_archive_old_inspections` (mensile, cancella righe `tblgenerale` più vecchie di 6 mesi); best-effort con catch+warn se `event_scheduler` non è abilitato
  - Aggiunto `CheckTableSizeAsync`: legge `information_schema.TABLES.TABLE_ROWS` e logga `TABLE_SIZE_WARNING` se `tblgenerale` supera 10M righe
  - `Initialize()`: chiama `CreateRetentionEventAsync` e `CheckTableSizeAsync` dopo `CreateCfgInspectionTable`
- `Database/CounterManager.cs`
  - Aggiunto campo `private DateTime _shiftStartTime = DateTime.Now`
  - Aggiunto `GetCurrentPpm()`: restituisce ppm basato su `_total / minuti_dal_turno`; ritorna 0 se meno di 10 secondi dall'inizio (evita spike)
  - Aggiunto `ResetForShiftAsync(string shiftName)`: logga snapshot pre-reset con `SHIFT_RESET_START`, chiama `ResetAllCountersAsync`, resetta `_shiftStartTime`
- `Cls_Config/AsyncConfigManagerXml.cs`
  - Aggiunto `using System.Linq`
  - `SaveConfigAsync`: prima di `WriteTextAtomicallyInternalAsync`, crea backup versionato in `cfg_backups/Config_YYYYMMDD_HHmmss.xml` nella stessa cartella di `Config.xml`; mantiene ultimi 30 backup (pruning automatico per data discendente); best-effort con warn su errore

**Motivo:** Fase F2 del piano di consolidamento — rendere i dati resistenti a corruzione e il sistema auto-gestito su disco. Tre rischi reali: (1) nome DB da Config.xml usato raw in DDL senza validazione; (2) `tblgenerale` cresce senza limiti a 180 ppm; (3) Config.xml sovrascritto senza storico → nessun recovery locale se anche MySQL è offline.

**Impatto:**
- Config.xml: nessuno
- Database: MySQL EVENT `qtis_archive_old_inspections` creato (richiede `event_scheduler=ON` in my.cnf per essere attivo); nessuna modifica schema
- API pubblica: `CounterManager` aggiunge `GetCurrentPpm()` e `ResetForShiftAsync(string)` (additive); `IsValidIdentifier` static internal
- Comportamento runtime: se il nome DB in Config.xml contiene caratteri non alfanumerici lo startup lancia eccezione esplicita invece di SQL injection; Config.xml mantiene storico ultime 30 versioni in `cfg_backups/`; `CheckTableSizeAsync` logga avviso se la tabella supera 10M righe ad ogni avvio

---

## [2026-05-08] Consolidamento F0+F1 — robustezza produzione, watchdog IO, race condition allarmi

**File modificati:**
- `Nlog.config` — riscritta configurazione completa: rotazione giornaliera archivio (30 gg app, 60 gg errori, 90 gg audit); nuovo target `auditfile` per tracciare operatori/ricette; structured layout con `${date}|${level}|${logger}|${message}`; layout NLog corretto (attributi separati, non `<layout>` annidato)
- `Extensions/TaskExtensions.cs` — nuovo file; metodo estensione `SafeFireAndForget(this Task, ...)` che registra ogni eccezione non gestita con NLog invece di inghiottirla silenziosamente; overload con `Logger` esplicito e `eventCode`
- `MainWindow.xaml.cs`
  - Aggiunto `using QtisVisionPanel.Extensions;`
  - 4 blocchi `catch { }` silenziosi → `catch (Exception ex) { logger.Warn/Error(ex, "EVENT_CODE"); }`
  - `OnAlarmTriggered`: guard esplicita se `IoManager == null` (dialog + log) anziché NullReferenceException silenziosa
  - Auto-reset IO → `Task.Run(...).SafeFireAndForget(logger, "IO_AUTO_RESET_FAILED")` anziché `async void` fire-and-forget implicito
  - Aggiunto helper `SetRecord(ref ICogRecord field, ICogRecord newRecord)` per gestione corretta memoria CogImage (rilascia il riferimento precedente prima di assegnare il nuovo)
  - `CacheLatestInspectionArtifacts` e callback camera frontale usano `SetRecord`
  - `OnAlarmTriggered`: `ServiceLocator.OutputWatchdog?.Track(outputChannel, true/false)` dopo ogni scrittura IO
- `Services/MachineRuntimeService.cs`
  - Aggiunto `private static readonly Logger _log = LogManager.GetCurrentClassLogger()`
  - `catch { }` in lettura stato Cognex → `catch (Exception ex) { _log.Warn(ex, "COGNEX_STATE_READ_FAILED"); }`
- `Database/clsConnection.cs` — `DbConnectionManager`:
  - Timeout connessione ridotto da 30 s a 5 s (`ConnectTimeoutSeconds = 5`)
  - Connection string aggiornata: `MinimumPoolSize=2; MaximumPoolSize=15; Connection Timeout=5`
  - `OpenConnectionAsync`: `CancellationTokenSource.CreateLinkedTokenSource` con timeout 6 s; catch separati per `OperationCanceledException` (log `DB_CONNECT_TIMEOUT`) e `MySqlException` (log `DB_CONNECT_FAILED`)
- `Database/Cls_InitializzeDb.cs`
  - `AddNewColumnsToTblGenerale`: aggiunto `Connection Timeout=5;` nella stringa di connessione e `CancellationTokenSource(6s)` per il timeout
  - `CreateTblGenerale`: aggiunti due indici nella DDL (`KEY idx_EsitoClass (EsitoClassificazione)`, `KEY idx_IdProduzione (IdProduzione)`)
  - Aggiunto `EnsureIndexAsync` (idempotente, controlla `information_schema.STATISTICS` prima di creare): chiamato dopo `CreateTblGenerale` per aggiungere indici su DB esistenti
- `Models/EjectionAlarmManager.cs` — `TriggerAlarm` riscritto con `lock (_lock)` + guard `IsTriggered` + guard 500 ms (`DateTime.Now - alarm.LastTriggered < 500 ms`) per eliminare race condition in caso di ispezioni parallele; `AlarmTriggered?.Invoke` spostato fuori dal lock per prevenire deadlock
- `Services/IntegratedAlarmCardService.cs` — `ProcessInspectionResultAsync`: aggiunta guard `if (!ServiceLocator.MachineRuntimeService.IsContinuousRunActive) return;` per non processare allarmi durante startup/shutdown
- `Services/OutputWatchdogService.cs` — nuovo file; `OutputWatchdogService` monitora ogni 10 s le uscite digitali registrate; se un canale è HIGH da più di 60 s quando `ExpectedHigh == false` (possibile guasto hardware) forza `WriteOutputAsync(ch, false)` con log `IO_OUTPUT_STUCK_HIGH` e `IO_OUTPUT_FORCED_RESET`
- `ServiceLocator.cs`
  - Aggiunto campo `private static OutputWatchdogService _outputWatchdog`
  - Proprietà `public static OutputWatchdogService OutputWatchdog => _outputWatchdog`
  - Setter `IoManager`: crea/dispose `OutputWatchdogService` automaticamente quando il device manager viene (ri)inizializzato
- `Views/AlarmNotificationWindow.xaml.cs` — `AcknowledgeButton_Click`: dopo `WriteOutputAsync(ch, false)` chiama `ServiceLocator.OutputWatchdog?.Track(_outputChannel, expectedHigh: false)` per aggiornare lo stato atteso nel watchdog
- `Views/AlarmNotificationWindow.xaml` — rimossi 3 attributi `LetterSpacing` non esistenti in WPF .NET 4.8; aggiunto `DefectsPanel` (pannello verde con lista difetti da `e.DefectCounts`); `ResetNotePanel` spostato su Row 3; aggiunta quarta `RowDefinition`
- `QtisVisionPanel.csproj` — registrati nuovi file: `Extensions\TaskExtensions.cs`, `Services\OutputWatchdogService.cs`, `Views\AlarmNotificationWindow.xaml` (Page), `Views\AlarmNotificationWindow.xaml.cs` (Compile DependentUpon)

**Motivo:** Preparazione al deploy in produzione (Fase F0 + F1 del piano di consolidamento). Obiettivo: eliminare le cause più probabili di crash/blocco silenzioso in ambiente macchina reale — task senza gestione errori, race condition allarmi, timeout DB infiniti, uscite IO non monitorate, catch silenziosi che nascondono guasti.

**Impatto:**
- Config.xml: nessuno
- Database: nuovi indici su `tblgenerale` (idempotenti — non rompono installazioni esistenti); timeout connessione ridotto a 5 s
- API pubblica: `OutputWatchdogService` aggiunto al `ServiceLocator`; firma `TriggerAlarm` interna invariata; `OpenConnectionAsync` ora accetta `CancellationToken`
- Comportamento runtime: allarmi non si triggerano più in doppio su ispezioni parallele; uscite IO bloccate HIGH vengono auto-resettate dopo 60 s; connessioni DB timeout in 5 s anziché 30 s; eccezioni in task background vengono loggate

---

## [2026-05-08] AlarmNotificationWindow — popup professionale allarme scarto + IO write su MainWindow

**File modificati:**
- `Views/AlarmNotificationWindow.xaml` — nuova finestra popup con tema dark industriale: header rosso, icona allarme, dettagli canale/durata, badge tipo allarme, pulsante ACKNOWLEDGE
- `Views/AlarmNotificationWindow.xaml.cs` — code-behind: popola i campi dall'evento `AlarmTriggeredEventArgs`; mostra pannello avviso arancione (`ResetNotePanel`) quando `PulseDurationMs == 0`; `AcknowledgeButton_Click` chiama `_ioManager.WriteOutputAsync(ch, false)` per resettare l'uscita latched, poi chiude
- `MainWindow.xaml.cs` — `OnAlarmTriggered`: sostituisce `DialogService.ShowWarning` con la nuova popup; prima di mostrarla scrive `IoManager.WriteOutputAsync(ch, true)` per alzare fisicamente l'uscita; se `PulseDurationMs > 0` avvia `Task.Delay` per auto-spegnere dopo la durata configurata
- `QtisVisionPanel.csproj` — aggiunto `<Page>` per `Views\AlarmNotificationWindow.xaml` e `<Compile DependentUpon>` per il code-behind

**Motivo:** l'uscita fisica non veniva mai alzata perché `DigitalIOViewModel` è istanziato solo quando si apre la tab IO (mai aperta in produzione). Spostando la scrittura IO in `MainWindow.OnAlarmTriggered` (sempre attivo) l'uscita viene alzata ad ogni allarme indipendentemente dalla navigazione UI.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: allarme scarto alza fisicamente l'uscita DO configurata; si auto-spegne dopo `PulseDurationMs` ms (se > 0) oppure rimane alta fino al click su ACKNOWLEDGE (se = 0); il popup è non-bloccante sul thread UI grazie a `Dispatcher.BeginInvoke`

## [2026-05-07] Fix uscita fisica allarmi — singleton sync, IO write su e.Alarm, reset output

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`
  - `OnEjectionAlarmTriggered`: IO write spostato da `TriggerAlarmOutputAsync(match)` a `TriggerAlarmOutputAsync(e.Alarm)`. `match` è lo specchio UI (dati stale di startup); `e.Alarm` è l'oggetto autorevole dal singleton con `OutputChannelId` corrente. `match.OutputChannelId` e `match.PulseDurationMs` vengono ora aggiornati dal `e.Alarm` live prima di ogni trigger.
  - `ResetSelectedEjectionAlarmAsync`: convertito in `async Task`; dopo aver resettato i flag UI chiama `_ioManager.WriteOutputAsync(ch, false)` per spegnere fisicamente l'uscita che rimane latchata quando `PulseDurationMs == 0`.
- `ViewModels/EjectionAlarmCardViewModel.cs`
  - `SaveConfigurationAsync`: dopo il salvataggio sulla istanza privata, chiama `ServiceLocator.AlarmService.ReloadAlarmsAsync()` per sincronizzare il singleton con i nuovi valori di `OutputChannelId`/`PulseDurationMs` appena persistiti su DB.

**Motivo:** `EjectionAlarmCardViewModel` usa una istanza privata di `IntegratedAlarmCardService`; il salvataggio aggiornava DB e istanza privata ma il SINGLETON (usato da `DigitalIOViewModel`) manteneva i valori originali di startup (tipicamente `OutputChannelId = ""`). Risultato: `TriggerAlarmOutputAsync` usciva subito al primo guard (`string.IsNullOrEmpty`) senza mai scrivere l'output fisico.

**Comportamento `PulseDurationMs`:**
- `> 0` → output HIGH per N ms poi LOW automaticamente
- `== 0` → output rimane HIGH finché l'utente clicca Reset (che ora chiama `WriteOutputAsync(ch, false)`)

**Impatto:**
- Database: nessuno
- API pubblica: `ResetSelectedEjectionAlarmAsync` è ora `async Task` (signature invariata esternamente)
- Comportamento runtime: uscita fisica viene attivata al primo allarme dopo save; reset disattiva l'uscita hardware

---

## [2026-05-07] Difetti per-card indipendenti — SelectableDefects spostati nel modello

**File modificati:**
- `Models/SelectableDefect.cs` — nuovo file; classe `SelectableDefect` spostata da `ViewModels/EjectionAlarmCardViewModel.cs` a `QtisVisionPanel.Models`; registrata in `QtisVisionPanel.csproj`
- `Models/EjectionAlarmConfig.cs` — aggiunta `ObservableCollection<SelectableDefect> SelectableDefects` (`[XmlIgnore]`); la collezione è inizializzata nel costruttore via `SyncSelectableDefects()` e aggiornata ogni volta che `MonitoredDefects` viene assegnato; `OnSelectableDefectChanged` riporta i cambiamenti di `IsSelected` su `MonitoredDefects` usando il flag `_syncingDefects` per evitare loop
- `Views/UserControls/EjectionAlarmControl.xaml` — `ListBox ItemsSource` cambiato da `{Binding DataContext.AvailableDefects, RelativeSource=...}` a `{Binding SelectableDefects}` (binding diretto sull'item della card)
- `ViewModels/EjectionAlarmCardViewModel.cs` — rimossa `ObservableCollection<SelectableDefect> AvailableDefects`; rimossi `SyncDefectsFromSelectedAlarm`, `OnDefectSelectionChanged`, `GetSelectedDefects`, `SetSelectedDefects`; rimossa la classe `SelectableDefect` duplicata in fondo al file; `AddNewAlarmOfType` usa direttamente `GetEjectionEnabledDefects()`

**Motivo:** La `AvailableDefects` era una collezione unica nel ViewModel condivisa da tutti i card. Spuntare "Logo" su una card lo spuntava visivamente su tutte le card. Con `SelectableDefects` dentro ogni `EjectionAlarmConfig`, ogni card gestisce i propri checkbox in modo completamente indipendente.

**Impatto:**
- Config.xml / Database: nessuno — `MonitoredDefects` continua a essere la sorgente di verità serializzata; `SelectableDefects` è `[XmlIgnore]`
- API pubblica: rimossi `GetSelectedDefects()` e `SetSelectedDefects()` (non usati esternamente)
- Comportamento runtime: ogni card mostra e modifica solo i propri difetti; cambiare card non altera le selezioni delle altre

---

## [2026-05-07] IntegratedAlarmCardService — fix OutputChannelId nel layer DB inline

**File modificati:**
- `Services/IntegratedAlarmCardService.cs` (classe `AlarmCardRepository` inline)
  - `GetAllAlarmsAsync`: aggiunto `OutputChannelId = signalId` alla costruzione dell'oggetto (il campo veniva letto dalla colonna `SignalID` e assegnato solo a `SignalID`, non a `OutputChannelId` — causava ComboBox "Uscita Fisica" sempre vuota al reload)
  - `InsertAlarmAsync`: `@SignalID` ora usa `alarm.OutputChannelId` anziché `alarm.SignalID` (la selezione del canale non veniva mai salvata)
  - `UpdateAlarmAsync`: idem — `alarm.OutputChannelId` anziché `alarm.SignalID`

**Motivo:** L'`AlarmCardRepository` usato da `IntegratedAlarmCardService` è la classe inline definita in fondo allo stesso file (namespace `QtisVisionPanel`), NON il file separato in `Database/AlarmCardRepository.cs`. Le fix applicate nel precedente session al file separato erano quindi senza effetto. Il bug originale: UI scrive `OutputChannelId`, DB legge/scrive `SignalID` (non più sincronizzato).

**Impatto:**
- Database: `cfg_alarm_cards.SignalID` ora memorizza il valore di `OutputChannelId` selezionato dalla ComboBox "Uscita Fisica"
- Comportamento runtime: "Uscita Fisica" mantiene il valore dopo salva+naviga+torna; "Difetti" mantiene le selezioni per card indipendentemente (tramite fix `SyncDefectsFromSelectedAlarm` già applicato)

---

## [2026-05-07] EjectionAlarmCardViewModel — sync bidirezionale MonitoredDefects e ripristino selezione post-save

**File modificati:**
- `ViewModels/EjectionAlarmCardViewModel.cs`
  - `SelectedAlarm` setter: aggiunta chiamata `SyncDefectsFromSelectedAlarm()` per aggiornare i checkbox "Difetti" ogni volta che cambia l'allarme selezionato.
  - Costruttore: dopo il popolamento di `AvailableDefects`, sottoscrizione `d.PropertyChanged += OnDefectSelectionChanged` per ogni item.
  - Nuovo metodo `SyncDefectsFromSelectedAlarm()`: disiscrive i listener, imposta `IsSelected` di ogni `SelectableDefect` in base a `SelectedAlarm.MonitoredDefects`, reiscrive i listener.
  - Nuovo metodo `OnDefectSelectionChanged()`: quando `IsSelected` cambia su un `SelectableDefect`, riscrive `_selectedAlarm.MonitoredDefects` con la lista filtrata corrente.
  - `RefreshAlarmsAsync()`: memorizza `_selectedAlarm?.Name` prima di `Alarms.Clear()`; dopo il ripopolamento cerca l'allarme per nome e lo ri-seleziona (o sceglie `Alarms[0]` se non trovato). In questo modo salvare + refresh non perde né la selezione né i checkbox dei difetti.

**Motivo:** Selezionare un allarme non aggiornava i checkbox "Difetti"; modificare i checkbox non scriveva `MonitoredDefects`; dopo salva+naviga i campi tornavano al valore originale perché `RefreshAlarmsAsync` non ripristinava la selezione e `SyncDefects` non era mai chiamata.

**Impatto:**
- Config.xml / Database: nessuno — `MonitoredDefects` è già serializzato come JSON in `cfg_alarm_cards.monitored_defects`
- API pubblica: nessuna
- Comportamento runtime: i checkbox "Difetti" riflettono ora la configurazione dell'allarme selezionato; le modifiche sono scritte immediatamente in `alarm.MonitoredDefects` e quindi persistono al salvataggio successivo; dopo save+reload la selezione e i checkbox vengono ripristinati correttamente.

---

## [2026-05-07] "Uscita Fisica" popolata da MachineOutputs — AlarmView e Allarmi Scarto sincronizzati

**File modificati:**
- `ViewModels/EjectionAlarmCardViewModel.cs` — `AvailableOutputChannels` (lista hardcoded DO40-DO63) rimpiazzata con `AvailableOutputSignals` (`ObservableCollection<MachineSignalDefinition>`); nuovo metodo `LoadAvailableOutputSignals()` che legge `MachineConfigurationService.Load().MachineOutputs`, ordinate per `SignalCode`.
- `Views/UserControls/EjectionAlarmControl.xaml` — ComboBox "Uscita Fisica": `ItemsSource` → `AvailableOutputSignals`, `SelectedValuePath="Channel"`, `SelectedValue="{Binding OutputChannelId}"`, `ItemTemplate` mostra `SignalCode — Channel`.
- `Views/UserControls/DigitalIOControl.xaml` — colonna "Canale Output" in Allarmi Scarto: editing template aggiornato a `MachineOutputs` con stesso pattern `SelectedValuePath`/`SelectedValue`/`ItemTemplate`.

**Motivo:** Le due viste mostravano canali raw "DO40"-"DO63" senza nome. Con questa modifica le ComboBox mostrano `OUT_BLOCKING_ALARMS — DO40`, `OUT_NON_BLOCKING_ALARMS — DO41` ecc. — gli stessi segnali già configurati in Digital IO. Selezionare un segnale scrive automaticamente il `Channel` corretto nell'`OutputChannelId`.

**Impatto:**
- Config.xml / Database: nessuno — `OutputChannelId` continua a memorizzare il canale raw (es. "DO40")
- API pubblica: nessuna — `AvailableOutputChannels` rimossa (non usata altrove)
- Comportamento runtime: la ComboBox "Uscita Fisica" si pre-seleziona automaticamente sull'elemento corretto al caricamento se il canale memorizzato corrisponde a un segnale configurato

---

## [2026-05-07] DigitalIOViewModel — IO output fisico e TriggerCount per allarmi scarto (fase 4)

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — `OnEjectionAlarmTriggered`: aggiunto `match.TriggerCount++`; chiamata fire-and-forget `TriggerAlarmOutputAsync` con `SafeFireAndForget`. Nuovo metodo `TriggerAlarmOutputAsync(EjectionAlarmConfig)`: valida `OutputChannelId` (formato DO + numero 0-63), chiama `_ioManager.WriteOutputAsync(channel, true)`, se `PulseDurationMs > 0` attende e chiama `WriteOutputAsync(channel, false)`, eccezioni loggate con `Logger.Error`.

**Motivo:** La sub-tab Allarmi Scarto configurava `OutputChannelId` e `PulseDurationMs` ma il `DigitalIOViewModel` non scriveva mai l'uscita fisica quando un allarme scattava. `TriggerCount` non veniva incrementato nonostante INPC fosse ora attivo.

**Impatto:**
- Config.xml / Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: quando `IntegratedAlarmCardService.AlarmTriggered` scatta, `DigitalIOViewModel` scrive `DO<N>` via `_ioManager`; se board assente (`_isSimulationMode = true`) `WriteOutputAsync` aggiorna solo il buffer interno senza crash. `TriggerCount` nella DataGrid Allarmi Scarto si incrementa in tempo reale.

---

## [2026-05-07] Fix DataGrid InterventionPoints — NewItemPlaceholder binding error

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` — aggiunto `CanUserAddRows="False" CanUserDeleteRows="False"` alla DataGrid `InterventionPoints` (riga Commissioning).

**Motivo:** Con `CanUserAddRows="True"` (default da `MappingDataGridStyle`) la DataGrid aggiunge una riga `{NewItemPlaceholder}`. WPF tentava di assegnarla a `SelectedInterventionPoint` (tipo `MachineInterventionPoint`) causando un `NotSupportedException` visibile nel log di binding. La DataGrid usa comandi dedicati per add/remove righe, quindi il native placeholder non serve.

**Impatto:**
- Config.xml / Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: errori di binding eliminati; selezione righe sulla DataGrid InterventionPoints funziona correttamente

---

## [2026-05-07] EjectionAlarmConfig — implementazione INotifyPropertyChanged (fase 3)

**File modificati:**
- `Models/EjectionAlarmConfig.cs` — classe riscritta: implementa `INotifyPropertyChanged` con `[CallerMemberName]`; tutte le proprietà convertite a backing field con `OnPropertyChanged()`; `IsTriggered` e `TriggerCount` (prima auto-props `[XmlIgnore]`) ora notificano l'UI quando cambiano; `[XmlArray]`/`[XmlArrayItem]` spostati dal campo privato alla proprietà pubblica `MonitoredDefects` (erano ignorati da XmlSerializer); aggiunto `using System.Runtime.CompilerServices`.

**Motivo:** `IsTriggered` e `TriggerCount` venivano impostati da `OnEjectionAlarmTriggered` nel ViewModel ma l'ellisse di stato nella DataGrid non si aggiornava mai — senza INPC, WPF non riceve notifica del cambio. Stesso problema per ogni modifica programmatica alle proprietà dell'allarme.

**Impatto:**
- Config.xml / Database: nessuno — serializzazione invariata
- API pubblica: `EjectionAlarmConfig` ora implementa `INotifyPropertyChanged`; compatibile con tutti i consumer esistenti
- Comportamento runtime: l'ellisse Stato nella sub-tab Allarmi Scarto si aggiorna in tempo reale quando un allarme viene triggerato; `TriggerCount` incrementa visivamente senza reload

---

## [2026-05-07] Fix OutputChannelId non persistito e non letto dal database

**File modificati:**
- `Database/AlarmCardRepository.cs` — `GetAllAlarmsAsync()`: aggiunto `OutputChannelId = signalIdValue` accanto a `SignalID` alla lettura da DB; gestione NULL su colonne `SignalID` e `MonitoredDefects`. `InsertAlarmAsync()` e `UpdateAlarmAsync()`: `@SignalID` ora prende `alarm.OutputChannelId` (autorità) invece di `alarm.SignalID`.
- `Models/EjectionAlarmConfig.cs` — `Clone()`: aggiunto `OutputChannelId = this.OutputChannelId`.

**Motivo:** `OutputChannelId` è la proprietà primaria usata dall'UI e dalla logica di triggering, ma il layer DB leggeva/scriveva solo `SignalID` (la proprietà legacy). Risultato: qualsiasi valore impostato dall'utente nell'"Uscita Fisica" veniva perso al successivo reload.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno — colonna `SignalID` già esistente; ora mappa su `OutputChannelId`
- API pubblica: nessuna
- Comportamento runtime: `OutputChannelId` sopravvive a save/reload. Il valore visualizzato nella AlarmView e nella sub-tab Allarmi Scarto è ora coerente.

---

## [2026-05-07] Digital IO view Phase 2 — sub-tab "Allarmi Scarto" nel tab Diagnostics

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — aggiunto supporto `EjectionAlarmConfig`: campi privati `_alarmService`, `_selectedEjectionAlarm`, `_ejectionAlarmsSummary`; proprietà pubbliche `EjectionAlarms`, `SelectedEjectionAlarm`, `EjectionAlarmsSummary`, `AlarmTypeValues`, `SignalTypeValues`; comandi `SaveEjectionAlarmsCommand`, `ReloadEjectionAlarmsCommand`, `ResetSelectedEjectionAlarmCommand`; metodi `LoadEjectionAlarmsAsync`, `SaveEjectionAlarmsAsync`, `ResetSelectedEjectionAlarmAsync`, `OnEjectionAlarmTriggered`, `RefreshEjectionAlarmsSummary`; chiamata `await LoadEjectionAlarmsAsync()` in `InitializeAsync()`; unsubscribe evento in `Dispose()`.
- `Views/UserControls/DigitalIOControl.xaml` — aggiunto sub-tab "Allarmi Scarto" all'interno del tab Diagnostics con DataGrid (colonne: stato triggered, abilitato, nome, tipo allarme, soglia, canale output, impulso ms, conteggio), barra azioni Save/Reload, badge EjectionAlarmsSummary, pulsante Reset Selezionato.

**Motivo:** Necessità di esporre la configurazione IO degli allarmi scarto (`OutputChannelId`, `PulseDurationMs`) e lo stato live di triggering direttamente nella vista Digital IO, senza dover navigare alla pagina separata degli allarmi.

**Impatto:**
- Config.xml: nessuno (usa `IntegratedAlarmCardService` esistente)
- Database: nessuno
- API pubblica: nessuna — il ViewModel espone nuove proprietà e comandi, nessuna interfaccia modificata
- Comportamento runtime: al caricamento del DigitalIOViewModel vengono caricati gli allarmi scarto da `IntegratedAlarmCardService`; l'evento `AlarmTriggered` aggiorna la collection in UI thread via `Dispatcher.BeginInvoke`

---

## [2026-05-07] Digital IO view Phase 1 — pulizia visiva, riduzione tab 7→5, abilitazione add/remove righe

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml` — rimozione intro card (PageIntroCardStyle) da tutti i tab; rimozione pannello "Global Actions + Operational Notes" vuoto dal Dashboard; rimozione sezione "Usage" ridondante dal pannello Primary Signals; semplificazione Signals intro card (rimasti solo pulsanti azione); rimozione pannello "Operational View" vuoto da Commissioning; merge tab Machine Events + Test I/O + Log in un unico tab "Diagnostics" con 3 sub-tab; abilitazione `CanUserAddRows/CanUserDeleteRows` per DataGrid MachineInputs e MachineOutputs (rimosso override `False`).

**Motivo:** La vista aveva 7 tab con molto testo statico di guida che occupava spazio senza aggiungere valore operativo. L'utente ha chiesto una pulizia professionale con riduzione a 5 tab e la possibilità di aggiungere/rimuovere righe di configurazione I/O.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: puramente visivo; i DataGrid Inputs/Outputs ora permettono add/remove tramite l'interfaccia nativa WPF (riga vuota in fondo + tasto Delete). Tutta la logica ViewModel invariata.

## [2026-05-07] ApplyCameraTriggerDelayToHardwareAtStartup configurabile + default 0 trigger delay

**File modificati:**
- `Cls_Config/Calss_structure/ConfigClassStructure.cs` — aggiunta property `ApplyCameraTriggerDelayToHardwareAtStartup` (bool, default `false`) alla classe `Configuration`. Serializzata in `Config.xml` sotto `<Configuration>`.
- `MainWindow.xaml.cs` — `ApplyCameraTriggerDelayToHardwareAtStartup` convertita da `const bool` a static property expression che legge `configManager?.Config?.Configuration?.ApplyCameraTriggerDelayToHardwareAtStartup ?? false`. Tutti i consumer esistenti (flag check in `ApplyRecipeTriggerDelaysAsync`, `ICognexJobManager.RestoreHardwareMode`, ecc.) rimangono invariati.
- `MainWindow.xaml.cs` — Fallback trigger delay: `700000` → `0` μs quando `cameraSetting` è null.
- `Cls_Vpro/ICognexJobManager.cs` — `GetTriggerDelayForJob`: fallback `70000` → `0` μs.

**Motivo:** Il flag era un `const = false` hardcoded, impossibile da abilitare senza ricompilare. Ora è leggibile dal file `Config.xml`: impostare `<ApplyCameraTriggerDelayToHardwareAtStartup>true</ApplyCameraTriggerDelayToHardwareAtStartup>` dentro `<Configuration>` abilita l'assegnazione seriale telecamera + scrittura TriggerDelay sulla camera GigE ad ogni caricamento ricetta. Il default `false` garantisce che su installazioni esistenti senza il campo il comportamento sia invariato.

**Impatto:**
- Config.xml: nuovo campo opzionale `<ApplyCameraTriggerDelayToHardwareAtStartup>` sotto `<Configuration>` (default `false` se assente)
- Database: nessuno
- API pubblica: `MainWindow.ApplyCameraTriggerDelayToHardwareAtStartup` non è più `const` ma `static` property — source-compatible, nessun impatto
- Comportamento runtime: controllato da Config.xml; di default identico a prima

## [2026-05-06] Fix crash avvio senza DAQNavi (biodaq.dll assente)

**File modificati:**
- `Models/AdvantechDeviceManager.cs` — aggiunto `IsBioDaqNativeAvailable()` (P/Invoke `LoadLibrary`/`FreeLibrary` su `kernel32.dll`). Il costruttore chiama questo metodo: se `biodaq.dll` non è trovabile dai path di sistema, imposta `_forceSimulationMode = true` prima ancora di chiamare qualsiasi tipo BDaq. Aggiunto `using System.Runtime.InteropServices`.

**Motivo:**
Su PC da fiera (senza DAQNavi installato) l'applicazione crashava ~500 ms dopo l'avvio con "Unhandled AppDomain exception". La `DllNotFoundException` originale era catturata correttamente, ma il wrapper managed `Automation.BDaq.dll` avviava thread/callback interni durante la creazione di `InstantDiCtrl`; questi thread chiamavano poi `biodaq.dll` da un contesto senza try-catch, generando un'eccezione non gestita fatale.

**Meccanismo del fix:**
`LoadLibrary("biodaq.dll")` restituisce `IntPtr.Zero` se la DLL non è nei path di sistema — senza eccezioni. Se assente, `_forceSimulationMode = true` nel costruttore; in `InitializeAsync()` il cortocircuito `!_forceSimulationMode && await CheckForHardwareDevicesAsync()` salta completamente `CheckForHardwareDevicesAsync()`, quindi nessun oggetto BDaq viene mai istanziato e il wrapper managed non avvia thread interni.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `IsBioDaqNativeAvailable()` resa `public static` (utile per diagnostica)
- Runtime: su PC senza DAQNavi la modalità simulazione è attivata immediatamente al costruttore senza tentare hardware; su PC con DAQNavi il comportamento non cambia

## [2026-05-05] Auto start VisionPro quando si apre All Channel

**File modificati:**
- `ViewModels/MainViewModel.cs`
- `Services/MachineRuntimeService.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

**Motivo:**
- quando l'operatore torna sulla vista `All Channel` (`Channel1`) la macchina deve assicurarsi che VisionPro sia in continuous run
- se VisionPro e' gia running, non deve inviare nessun comando aggiuntivo
- se VisionPro e' fermo, deve ripartire automaticamente senza richiedere un click separato sul pulsante Start

**Implementazione:**
- `OnNavigateRequested("Channel1")` chiama ora `EnsureContinuousRunForAllChannelAsync`
- il servizio legge `CognexManager.IsRunningContinuously` fuori dal thread UI con timeout breve
- se lo stato reale e' running, aggiorna la cache e termina senza start
- se e' stopped, rilascia solo l'hold `manual-stop` e avvia `StartContinuousRunAsync`
- gli hold protetti (`recipe-save`, `job-editor`, `shutdown`) restano rispettati e impediscono l'auto-start

**Nuove tracce log:**
- `VISIONPRO_ALLCHANNEL_ALREADY_RUNNING`
- `VISIONPRO_ALLCHANNEL_AUTO_START`
- `VISIONPRO_ALLCHANNEL_START_SKIPPED`
- `VISIONPRO_STATE_READ_TIMEOUT`

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: apertura di All Channel diventa un punto di rientro operativo che riporta VisionPro in running se era fermo manualmente

## [2026-05-05] Filtro date/ora nel dashboard analisi

**File modificati:**
- `ViewModels/DataAnalysisViewModel.cs` — `StartDate`/`EndDate` ora conservano la componente oraria (setter preserva `TimeOfDay` esistente). Aggiunte property `StartTimeText` e `EndTimeText` (formato "HH:mm", parsing con `TryParseTime`). `ResolveRange()` usa i `DateTime` completi senza stripping del tempo. Inizializzazione: `_startDate = DateTime.Today` (00:00), `_endDate = DateTime.Today + 23:59`.
- `Views/UserControls/DataAnalysisView.xaml` — ogni DatePicker è affiancato da un TextBox (62 px, `UpdateSourceTrigger=LostFocus`) per l'ora. ToolTip "HH:mm" guida l'utente sul formato.

**Motivo:** Il filtro date-only non permetteva analisi intra-giornaliere (es. turno mattina 06:00–14:00). Ora si può selezionare un intervallo preciso con ora di inizio e ora di fine.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: aggiunte `StartTimeText`, `EndTimeText`; comportamento `StartDate`/`EndDate` cambiato (preserva `TimeOfDay`)
- Comportamento runtime: le query vengono eseguite sull'intervallo esatto `[StartDate+StartTime … EndDate+EndTime]`

## [2026-05-05] Nuovi grafici KPI + rifinitura dashboard analisi

**File modificati:**
- `Models/DataAnalysisModels.cs` — enum `DataAnalysisCardType`: aggiunti `RejectRateTrend`, `ProductionRateTrend`. `DataAnalysisBarItem`: aggiunto `Percentage`. `DataAnalysisTrendCard`: aggiunti `ShowMinMax` (bool, default true), `ValueUnit` (string).
- `Database/ProductionAnalyticsRepository.cs` — aggiunti `GetRejectRateTrendAsync` (% NC per bucket 15 min) e `GetProductionRateTrendAsync` (pcs/h per bucket 15 min).
- `ViewModels/DataAnalysisViewModel.cs` — registrati i due nuovi card type nei dizionari, aggiunte query parallele in `FetchSnapshotAsync`, aggiunti due `PopulateTrendCard` in `ApplySnapshot`. `CreateBarItem` ora riceve `percentage` e lo imposta sul modello. Summary text usa `ValueUnit` quando presente.
- `Views/UserControls/DataAnalysisView.xaml` — production bars: aggiunta colonna `(XX.X%)`. Min/Max path e legende: `Visibility` legata a `ShowMinMax`.

**Motivo:** Il grafico reject rate è il KPI più critico per un operatore macchina visione; il grafico production rate mostra il throughput. Il % sulle barre dà immediatamente il senso della qualità senza calcoli manuali. I grafici rate non hanno senso con linee min/max (avg=min=max per bucket).

**Impatto:**
- Config.xml: nessuno
- Database: nessuno (query su tabella esistente `tblgenerale`)
- API pubblica: `DataAnalysisCardType` enum — 2 nuovi valori (additive); `DataAnalysisBarItem.Percentage`; `DataAnalysisTrendCard.ShowMinMax`, `ValueUnit`
- Comportamento runtime: due nuovi trend card visibili nella dashboard; barre production overview mostrano % accanto al conteggio

## [2026-05-05] Grafico trend professionale: curve Bezier + data sull'asse X + area fill

**File modificati:**
- `Models/DataAnalysisModels.cs` — aggiunte 4 proprietà `Geometry` a `DataAnalysisTrendCard`: `AveragePathData`, `MinimumPathData`, `MaximumPathData`, `AverageAreaPathData`.
- `ViewModels/DataAnalysisViewModel.cs` — aggiunto `BuildSmoothPathString` (Catmull-Rom→cubic Bezier), `BuildSmoothPathGeometry`, `BuildSmoothAreaGeometry` (area chiusa sotto la media). Aggiunto `FormatXAxisLabel`: quando il range temporale copre più giorni, il label X diventa due righe "HH:mm\nd/M". `PopulateTrendCard` ora popola le nuove proprietà Geometry invece delle vecchie stringhe Polyline per avg/min/max.
- `Views/UserControls/DataAnalysisView.xaml` — sostituiti i tre `<Polyline>` avg/min/max con `<Path Data="{Binding ...}">` con `StrokeLineJoin="Round"`, `StrokeLineCap="Round"`. Aggiunto `<Path>` per l'area fill sotto la media (gradiente `#40→#00 1F6FA7`). Aggiunto `TextWrapping="Wrap"` al TextBlock sull'asse X per mostrare la data su seconda riga.

**Motivo:** Le linee spezzate (Polyline) rendevano il grafico poco leggibile sui picchi. L'asse X mostrava solo l'ora senza la data, rendendo impossibile capire se i dati coprono più giorni. Il colore dell'area fill aiuta a identificare visivamente la linea media.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `DataAnalysisTrendCard` — 4 nuove property Geometry (additive, non breaking)
- Comportamento runtime: le linee del grafico sono ora curve Bezier smooth; l'asse X mostra "HH:mm\nd/M" quando il range copre più giorni

## [2026-05-05] Fix grafico defect distribution + PowerFlex fault come Warning

**File modificati:**
- `Views/UserControls/DataAnalysisView.xaml` — aggiunto `ItemsControl.ItemsPanel = Grid` al pie chart dei difetti. Senza questa impostazione, il default `StackPanel` impilava verticalmente le `Path` delle fette, spostando ogni fetta verso il basso dell'altezza della precedente: la fetta grande (es. 75% = 270°) veniva traslata ~110px verso il basso e fuoriusciva dal canvas 220×220, lasciando un'ampia area grigia visibile.
- `Services/PowerFlex525Service.cs` — cambio da `Logger.Error` a `Logger.Warn` e da `isCritical=true` a `isCritical=false` nella segnalazione fault PowerFlex 525. Il fault viene ora registrato come Warning anziché come Allarme/Errore.

**Motivo:** (1) Il grafico mostrava una grande area grigia perché le fette del pie chart si sovrapponevano al canvas con offset cumulativi invece di renderizzarsi tutte all'origine (0,0) del canvas 220×220. (2) Il fault del PowerFlex 525 (es. sovracorrente motore) è una notifica diagnostica, non un allarme critico di produzione.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: (1) il pie chart mostra correttamente tutte le fette sovrapposte nel canvas; (2) i fault PowerFlex appaiono in log con livello W (Warning) anziché E (Error) e category "Alarm" rimane ma non è più marcata isCritical

## [2026-05-04] Revert compensazione velocità + fix Edit Mode dashboard analisi

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — rimossa la logica di scaling dei delay (`ComputeSpeedCompensationFactor`, `ScaleOutputDelays`): la compensazione automatica basata sull'inter-arrivo fotocellula causava delay errati quando i prodotti vengono passati manualmente a intervalli irregolari. Mantenuto solo il logging diagnostico di `_lastInterArrivalMs` nel payload `TRIGGER_PHOTOCELL_EDGE`.
- `ViewModels/DataAnalysisViewModel.cs` — aggiunta modalità Edit esplicita: `CanEditDashboard` ora è `IsAdministrator && _isEditModeActive` (default=false). Aggiunto `ToggleEditModeCommand` (admin toggle: Enter Edit / Save+Exit). `NotifyEditModeChanged()` aggiorna `CanEditDashboard`, `EditOrSaveLabel`, comandi. Logout/cambio ruolo azzera automaticamente edit mode.
- `Views/UserControls/DataAnalysisView.xaml` — il pulsante `SaveLayoutLabel`+`SaveAppearanceCommand` (visibile sempre per admin) è sostituito con `EditOrSaveLabel`+`ToggleEditModeCommand`: mostra "Edit Dashboard" in modalità normale, "Save Layout" in edit mode. I pulsanti "Remove" rimangono nascosti finché l'admin non entra esplicitamente in edit mode.

**Motivo:** (1) La compensazione velocità aumentava i delay proporzionalmente agli intervalli manuali tra i prodotti, rendendo la macchina inutilizzabile. (2) Il pulsante "Remove" appariva sempre per gli utenti Administrator rendendo il grafico confuso durante la normale operazione.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `DataAnalysisViewModel` — nuove property `IsAdministrator`, `EditOrSaveLabel`, `ToggleEditModeCommand`
- Comportamento runtime: i pulsanti "Remove" della dashboard analisi sono visibili SOLO quando un admin clicca "Edit Dashboard". Cliccando "Save Layout" il layout viene salvato ed edit mode si chiude automaticamente.

## [2026-05-04] Compensazione velocità nastro via inter-arrivo fotocellula

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — aggiunto rolling buffer (`_photocellInterArrivalBuffer`, finestra 8 campioni) per stimare la velocità istantanea del nastro dall'intervallo tra due fronti consecutivi della fotocellula. Per ogni prodotto viene calcolato `speedFactor = interArrivalMs / rollingAverage`: se il nastro è più veloce della media (intervallo corto), il ritardo trigger viene ridotto proporzionalmente; se è più lento viene aumentato. In questo modo la telecamera scatta sempre nella stessa POSIZIONE fisica anche se la velocità varia. Il fattore viene loggato nel file EventAudit (`speed_factor`, `inter_arrival_ms`, `speed_compensation_active`) per ogni fotocellula accettata.

**Motivo:** Il timing elettrico era già preciso a ±0.1ms (confermato dai log delta≈0ms). Lo shift dell'immagine residuo era causato da variazione di velocità del nastro: con ritardo fisso 490ms, un nastro 5% più veloce sposta il prodotto di ~7mm nella finestra di acquisizione. La compensazione rende la posizione di scatto invariante alla velocità.

**Impatto:**
- Config.xml: nessuno (auto-calibrante: usa media mobile degli ultimi 8 inter-arrivi)
- Database: il campo `speed_factor` appare nel JSON payload di ogni `TRIGGER_PHOTOCELL_EDGE`
- API pubblica: nessuna
- Comportamento runtime: dal 3° prodotto in poi il ritardo viene scalato. Il fattore è limitato a [0.5, 2.0] per sicurezza. Buffer azzerato a ogni reset diagnostici.

## [2026-05-04] Telemetria trigger via ApplicationEventLogger (EventAudit)

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — `OnTimedTriggerServiceEventRaised`: il blocco di logging diretto `Logger.Info/Warn/Error` è sostituito con chiamate a `ServiceLocator.ApplicationEventLogger.LogOperationalEvent(...)`. Gli eventi loggati sono `EdgeAccepted` (logId=`TRIGGER_PHOTOCELL_EDGE`), `OutputOn` (logId=`TRIGGER_OUTPUT_RAISED_TOP` / `TRIGGER_OUTPUT_RAISED_SIDE`, WARN se delta >10ms), `OutputSkipped` (logId=`TRIGGER_OUTPUT_SKIPPED`), `Error` (logId=`TRIGGER_ERROR`). Il payload `metadata` include `edge_to_output_ms`, `target_delay_ms`, `delta_ms`, `dispatch_ms`, `wait_ms` come valori strutturati.

**Motivo:** Il logger `Logger` (classe principale) scrive su target NLog diverso da "EventAudit". `ApplicationEventLogger` usa `LogManager.GetLogger("EventAudit")` — lo stesso target degli altri eventi operativi dell'applicazione, quindi i dati del trigger appaiono nello stesso file log.

**Impatto:**
- Config.xml: nessuno
- Database: ogni trigger scrive una riga in `EventLog` via `EventLogRepository.InsertAsync` (asincrono, no impatto performance)
- API pubblica: nessuna
- Comportamento runtime: nel file log EventAudit ogni prodotto genera righe `TRIGGER_PHOTOCELL_EDGE` + `TRIGGER_OUTPUT_RAISED_TOP` + `TRIGGER_OUTPUT_RAISED_SIDE`. Delta >10ms → livello WARN per individuazione immediata dei casi problematici.

## [2026-05-04] Spin-wait finale e telemetria edge-to-output nel log eventi

**File modificati:**
- `Services/PhotocellTimedTriggerService.cs` — `WaitUntilDelayElapsedAsync` riscritta: coarse `Task.Delay` per tutto tranne gli ultimi 20ms, poi spin-wait CPU puro (`Thread.SpinWait`) per eliminare il jitter del timer Windows (~15.6ms). Aggiunto `TotalEdgeToOutputMs` a `PhotocellTimedTriggerEventArgs` e messaggio `OutputOn` aggiornato con `edgeToOutput=X ms`.
- `ViewModels/DigitalIOViewModel.cs` — `_lastTimedTopActualDelayMs` / `_lastTimedSideActualDelayMs` ora usano `e.TotalEdgeToOutputMs` (tempo completo edge→uscita). `AddMachineOperationalEvent` per `OutputOn` include il breakdown `dispatch + wait + target` nel campo detail.

**Motivo:** Shift occasionali grandi sull'immagine acquisita (fino a ~15ms) causati da `Task.Delay(1)` nel loop spin-wait: su Windows senza multimedia timer attivo, `Task.Delay(1)` dorme 15.6ms, causando overshooting. Lo spin-wait CPU per gli ultimi 20ms elimina questa fonte di jitter. In parallelo, il log eventi ora mostra per ogni trigger i ms totali dal fronte hardware fino all'attivazione dell'uscita.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `PhotocellTimedTriggerEventArgs` — nuova property `TotalEdgeToOutputMs` (backward-compatible, non serializzata)
- Comportamento runtime: spin CPU ~20ms per output per ogni trigger (TOP + SIDE = ~40ms su un core). Nel log eventi ogni `TIMED_TOP_TRIGGER_ON` / `TIMED_SIDE_TRIGGER_ON` mostra `edgeToOutput=X ms (dispatch=Y ms + wait=Z ms) | target=N ms`.

## [2026-04-30] Timed trigger precision hardening - fast path fuori dalla UI

**File modificati:**
- `Services/PhotocellTimedTriggerService.cs`
- `ViewModels/DigitalIOViewModel.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Docs/Commissioning/08_Photocell_Timed_Trigger_Commissioning.md`

**Problema:**
- la fotocellula reale veniva ricevuta dal polling Advantech su thread background, ma il ciclo timed passava poi dal dispatcher UI prima di accodare il batch
- l'uscita fisica TOP/SIDE veniva a sua volta eseguita tramite un secondo dispatcher UI
- quando la UI era occupata, la latenza variabile spostava il momento reale dello scatto e rendeva la posizione foto poco ripetibile

**Fix applicato:**
- aggiunta cache fast-path per `TimedFromPhotocell` con:
  - canale reale fotocellula
  - polarita logica fotocellula
  - route TOP/SIDE gia risolta su board/canale/polarita
- `OnInputChanged` ora puo intercettare il fronte fotocellula e accodare il batch senza aspettare il dispatcher UI
- `PhotocellTimedTriggerService` usa `SourceEventTimestampUtc` per calcolare la compensazione del ritardo gia trascorso dal fronte hardware
- gli output timed usano canale/polarita pre-risolti e scrivono direttamente su `_ioManager.WriteOutputAsync`
- il dispatcher UI resta usato solo per aggiornare pannello, log visuale e diagnostica, non per il tempo critico dello scatto

**Nuove tracce log utili:**
- `FAST_PATH_PHOTOCELL_ACCEPTED`
- `PHOTOCELL_EDGE_ACCEPTED ... eventAge=... ms`
- `TIMED_TRIGGER_SCHEDULED ... effectiveDelay=... ms compensation=... ms`
- `TIMED_TRIGGER_OUTPUT_ON ... actualDelay=... ms executorDelay=... ms`
- `Pulse START ... [board/channel]`
- `Pulse END ... [board/channel]`

**Impatto:**
- Config.xml: nessun cambio schema
- Database: nessuno
- Runtime macchina: timed trigger piu ripetibile per TOP/SIDE, con minore dipendenza dal carico UI

## [2026-04-30] Rimozione completa logica lighting dalla pipeline I/O

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — rimossi tutti i campi, proprietà e rami di codice legati alla gestione luci
- `Models/MachineRuntimeConfiguration.cs` — rimossi `TopLightingSignalCode`, `SideLightingSignalCode`, `LightingMode`, `PhotocellLightingBenchModeEnabled` da `MachineRuntimeBindings`
- `Models/ToolFusionSnapshot.cs` — rimossa proprietà `LightingMode` da `ToolFusionRuntimeSummary`
- `bin/x64/Release/cfg/machine_runtime_config.xml` — rimossi elementi `TopLightingSignalCode`, `SideLightingSignalCode`, `LightingMode`, `PhotocellLightingBenchModeEnabled` e punti intervento `LIGHTS_ON` / `LIGHTS_OFF`
- `Views/UserControls/DigitalIOControl.xaml` — rimossi tutti i controlli UI legati alle luci

**Motivo:** le uscite luce di questa macchina sono pilotate direttamente dall'uscita trigger della telecamera (DO02/DO03). Non esiste un segnale luce separato. Mantenere la logica lighting creava confusione nella configurazione e parametri superflui nel pannello.

**Rimosso:**
- `BoundTopLightingSignalCode` / `BoundSideLightingSignalCode` (proprietà + backing field)
- `MachineLightingMode` (incluso il ramo `HoldUntilLightOff`)
- `PhotocellLightingBenchModeEnabled` / `PhotocellLightingBenchModeSummary`
- `LightingModeSummary` / `ShowOptionalLightingBindings`
- `LightingModeOptions` (ComboBox nel pannello encoder)
- `"LightOn"` / `"LightOff"` da `InterventionActionOptions`
- `LIGHTS_ON` / `LIGHTS_OFF` dal factory dei punti intervento e dalla migrazione
- Tutti i `SetMappedOutputAsync` su segnali luce in `ExecuteTriggerCycleAsync`, `ForceTriggerCycleCommand` e `ExecuteInterventionPointAsync`

**Impatto:**
- Config.xml: campi lighting rimossi; vecchi XML con quei campi vengono caricati ignorando silenziosamente i tag sconosciuti (XML è forward-compatible)
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: il trigger camera invia il pulse direttamente su DO02/DO03 — la luce si alza con l'uscita della camera senza gestione separata

## [2026-04-30] Fix real-photocell timed trigger + ObservableCollection crash + diagnostic logging

**File modificati:**
- `bin/x64/Release/cfg/machine_runtime_config.xml` — correzione `ProductPhotocellSignalCode`, aggiunta campi timed trigger mancanti
- `ViewModels/DigitalIOViewModel.cs` — fix crash DataGrid, semplificazione `BuildTimedTriggerBatchRequest`, logging diagnostico

**Problema 1 — fotocellula reale non attiva le uscite camera:**
- `ProductPhotocellSignalCode` nel config era `IN_EXTERNAL_TRIGGER_ENABLE` (scritto erroneamente dall'agente Codex) invece di `IN_PRODUCT_PHOTOCELL`
- `OnInputChangedCore` confronta `mappedSignal.SignalCode` con `BoundProductPhotocellSignalCode`: il mismatch causava il silenziosa caduta del trigger
- Fix: ripristinato `IN_PRODUCT_PHOTOCELL`; aggiunti `TriggerSchedulingMode=TimedFromPhotocell`, `TopTriggerBaseDelayMs=570`, `SideTriggerBaseDelayMs=575`, `PhotocellDebounceMs=40`, `MinimumRetriggerGapMs=120`, `VirtualConveyorEnabled=false`; canali DO02=SIDE / DO03=TOP corretti

**Problema 2 — crash `An ItemsControl is inconsistent with its items source`:**
- `RefreshMachineSignalStatuses()` chiamava `RefreshSignalViews()` (→ `view.Refresh()`) in modo sincrono, anche durante il layout pass di WPF
- Fix: `RefreshSignalViews()` ora viene schedulata via `Dispatcher.BeginInvoke(DispatcherPriority.Background, ...)` in modo che non interferisca mai con un layout pass in corso

**Miglioramento 3 — `BuildTimedTriggerBatchRequest` semplificato:**
- Rimossa doppia chiamata a `ResolveTimedTriggerInterventionPoint("TOP/SIDE")`; usa direttamente `BoundTopCameraTriggerSignalCode` e `BoundSideCameraTriggerSignalCode` che già incapsulano la stessa logica

**Miglioramento 4 — log diagnostico su mismatch segnale fotocellula:**
- Aggiunto warning in `OnInputChangedCore` quando un input RISING arriva su un canale diverso da quello configurato come fotocellula prodotto; evita debug muto in campo

**Impatto:**
- Config.xml: nuovi campi timed trigger aggiunti; `ProductPhotocellSignalCode` corretto
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: timed trigger funziona con fotocellula reale su DI00; editing DataGrid non crasha più durante aggiornamento live della vista

## [2026-04-29] Machine Configuration UI cleanup for single-photocell timed-trigger flow

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml`
- `ViewModels/DigitalIOViewModel.cs`
- `Localization/messages_eng.json`
- `Localization/messages_ita.json`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

**Problema chiuso:**
- la Machine Configuration mostrava in primo piano binding luci e trigger legacy che nella macchina attuale creavano solo ambiguita
- l'operatore doveva capire da solo che nel timed trigger reale i segnali autoritativi sono quelli dei punti intervento `TOP` / `SIDE`

**Fix applicato:**
- nuova area primaria orientata al flusso reale macchina:
  - fotocellula prodotto
  - uscita camera TOP
  - uscita camera SIDE
- aggiunti riepiloghi dinamici del flusso e guida operativa contestuale
- i binding TOP/SIDE ora sono esposti direttamente tramite i punti intervento usati dallo scheduler timed
- trigger legacy, consenso esterno e luci separate sono stati spostati in un'area opzionale avanzata
- i controlli luci restano nascosti finche non diventano davvero rilevanti per il setup corrente

**Impatto:**
- nessun cambio schema XML / DB
- commissioning piu pulito e coerente con la macchina reale
- meno rischio di configurare campi secondari pensando che governino il timed trigger

## [2026-04-29] Fix DataGrid live-preview collision on ReservedForRealSignal

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

**Problema chiuso:**
- cambiando `ReservedForRealSignal` nella Machine Configuration il runtime preview poteva riattivarsi mentre il `DataGrid` era ancora in edit/add
- questo collideva con il `CollectionView` WPF e poteva generare:
  - `An ItemsControl is inconsistent with its items source`

**Fix applicato:**
- `ReservedForRealSignal` non fa piu partire `QueueRuntimePreviewApply`
- il flag continua a:
  - marcare la configurazione come modificata
  - aggiornare le viste filtrate solo quando le griglie non sono occupate

**Impatto:**
- nessun cambio schema XML / DB
- editing macchina piu stabile
- il flusso timed trigger non va piu corretto agendo su `ReservedForRealSignal`

## [2026-04-29] Timed trigger machine validation and UI-safe live preview hardening

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`
- `Services/MachineConfigurationService.cs`
- `Docs/Commissioning/08_Photocell_Timed_Trigger_Commissioning.md`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

**Problemi chiusi:**
- in macchina la configurazione timed risultava ancora incoerente:
  - fotocellula prodotto mappata sul segnale sbagliato
  - delay salvati con ordini di grandezza errati
  - uscita spare DO00 potenzialmente lasciata alta dal precedente stato hardware
- il runtime preview live poteva riattivarsi mentre i `DataGrid` erano ancora in edit/add, contribuendo al crash `ItemsControl is inconsistent with its items source`

**Fix applicati:**
- il preview runtime ora viene rimandato se una griglia configurazione e' ancora in edit
- aggiunti warning runtime specifici per:
  - `ProductPhotocellSignalCode` / `ExternalTriggerEnableSignalCode`
  - delay timed anomali
  - output incompleti
- aggiunto parcheggio automatico a LOW di `OUT_SPARE_DO00` quando non e' referenziato da nessun binding runtime o punto intervento
- aggiornata la guida commissioning con checklist minima e sequenza log attesa

**Impatto:**
- nessun cambio schema DB
- nessuna rottura compatibilita XML
- commissioning piu' leggibile e meno ambiguo in macchina

## [2026-04-29] Timed trigger finishing pass - schema upgrade, mode coupling, clearer photocell flow diagnostics

**File modificati:**
- `Services/MachineConfigurationService.cs`
- `ViewModels/DigitalIOViewModel.cs`
- `ConfigurationTemplates/machine_runtime_config.template.xml`
- `Localization/messages_eng.json`
- `Localization/messages_ita.json`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

**Problemi chiusi:**
- i nuovi parametri timed trigger non risultavano ancora presenti nel template canonico e in alcune configurazioni runtime gia salvate
- `TriggerSchedulingMode=TimedFromPhotocell` e `VirtualConveyorEnabled=true` potevano convivere in UI, lasciando un assetto ambiguo
- al fronte fotocellula il log non rendeva esplicito se il runtime stesse:
  - accodando impulsi timed
  - usando il virtual conveyor legacy
  - aspettando un encoder reale con virtual conveyor disabilitato

**Fix applicati:**
- il servizio configurazione ora forza i default minimi dei nuovi `RuntimeBindings` timed trigger prima di serializzare
- aggiunto controllo schema su file attivo e template:
  - se mancano i nodi `TriggerSchedulingMode`, `PhotocellDebounceMs`, `MinimumRetriggerGapMs`, `TopTriggerBaseDelayMs`, `SideTriggerBaseDelayMs`, `TopTriggerPulseMs`, `SideTriggerPulseMs`
  - il file viene riscritto con la schema aggiornata
- `TriggerSchedulingMode=TimedFromPhotocell` disattiva automaticamente `VirtualConveyorEnabled` nel view model runtime
- il log della fotocellula ora dichiara il percorso effettivo:
  - batch timed TOP/SIDE
  - flow legacy con virtual conveyor
  - flow legacy in attesa encoder reale
- aggiunta validazione runtime della configurazione trigger:
  - warning se `VirtualConveyorEnabled=false` ma `TriggerSchedulingMode=VirtualConveyor`
  - warning se i delay timed sono anomali
  - warning se `ExternalTriggerEnable` coincide con la fotocellula prodotto
  - warning se il file macchina contiene output incompleti
- aggiunto trace completo della rotta timed con:
  - segnale logico
  - uscita fisica `Board/Channel`
  - delay
  - pulse
- aggiornato il template macchina baseline con i nuovi nodi timed trigger
- aggiornato il testo localizzato del summary timed trigger, eliminando il riferimento alla "next integration step"

**Impatto:**
- `machine_runtime_config.xml` / template: schema estesa e auto-riparabile
- runtime: meno ambiguita tra modalita timed e modalita legacy
- commissioning: diagnostica piu leggibile quando la fotocellula viene vista ma non parte uno scatto immediato

## [2026-04-29] Fix timed trigger: OnInputChanged dispatched UI, ExecuteTimedOutput UI-safe, debounce per-mode, VC auto-stop

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — quattro fix distinti

**Bug 1 — `OnInputChanged` su thread Advantech**
Il polling Advantech chiama `InputChanged` su un thread background. `OnInputChanged` accedeva a `MachineInputs`, `MachineOutputs`, `InterventionPoints` (tutti `ObservableCollection` solo UI-thread) e impostava proprietà bindabili, causando eccezioni cross-thread silenziose o valori null. Risultato: la fotocellula veniva vista nel log ma nessuna azione seguiva.
Fix: split in `OnInputChanged` (dispatch) + `OnInputChangedCore` (logica). Dispatch con `Dispatcher.BeginInvoke(new System.Action(...))` — `System.Action` qualificato per evitare ambiguità con `QtisVisionPanel.Models.Advantech.Action`.

**Bug 2 — `ExecuteTimedTriggerOutputAsync` chiama `PulseMappedOutputAsync` da `Task.Run`**
`PhotocellTimedTriggerService.QueueBatch` usa `Task.Run`. Il lambda `outputExecutor` (= `ExecuteTimedTriggerOutputAsync`) veniva quindi eseguito da un thread pool. Dentro, `PulseMappedOutputAsync` chiama `ResolveOutputSignalByCode` che fa `.FirstOrDefault` su `MachineOutputs` — non thread-safe. Il segnale tornava null e l'uscita non si alzava.
Fix: `Dispatcher.InvokeAsync(() => PulseMappedOutputAsync(...)).Task.Unwrap()` — signal resolution e I/O write girano sul UI dispatcher (async, non blocca), il `Task` unwrapped completa quando il pulse fisico termina.

**Bug 3 — Debounce `||` bloccava tutti i trigger successivi in TimedFromPhotocell**
In modalità timed, l'encoder non avanza tra un prodotto e l'altro (virtual conveyor disabilitato). `_lastAcceptedPhotocellEncoderCount` rimane uguale → `deltaMillimeters = 0`. Con `||`, `0 <= 8mm` → sempre vero → tutti i trigger successivi soppressi.
Fix: branch esplicito `if (IsTimedTriggerSchedulingActive)` → debounce solo temporale. In VirtualConveyor mode rimane `||` (time OR distance).

**Bug 4 — Virtual conveyor non veniva fermato in TimedFromPhotocell**
Il timer 20ms continuava ad avanzare `_virtualConveyorCount` e chiamare `UpdateEncoderPosition`, consumando CPU inutilmente e potenzialmente interferendo con la diagnostica encoder.
Fix: in `UpdateVirtualConveyorTimerState`, blocco esplicito: se `IsTimedTriggerSchedulingActive` → stop timer e return.

**Bonus — Testo TriggerSchedulingModeSummary aggiornato**
Il testo precedente diceva "next integration step, while the current runtime still follows the existing tracking flow" — riferimento a una fase di sviluppo già completata. Ora dice: "TimedFromPhotocell active: photocell edge starts a deterministic scheduler..."

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: in modalità TimedFromPhotocell la fotocellula reale (e il pulsante simula) ora triggerano correttamente le uscite camera; virtual conveyor si ferma automaticamente; debounce non blocca più i trigger successivi

---

## [2026-04-29] Fase 5+6 timed trigger - commissioning tools, metriche e procedura test

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`
- `Views/UserControls/DigitalIOControl.xaml`
- `Localization/messages_eng.json`
- `Localization/messages_ita.json`
- `Docs/Commissioning/08_Photocell_Timed_Trigger_Commissioning.md`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/photocell-timed-trigger-development-plan-2026-04-29.md`

**Scopo:**
- completare la parte commissioning e tracciabilita del timed trigger
- portare la macchina a uno stato pronto per test campo e finitura taratura

**Implementazione:**
- aggiunti strumenti manuali nella UI:
  - simulazione fronte fotocellula
  - force TOP
  - force SIDE
  - reset diagnostica timed
- aggiunte metriche runtime:
  - batch queued/completed/cancelled/error
  - conteggio scatti TOP/SIDE
  - ultimo delay osservato TOP/SIDE
  - delay medio osservato TOP/SIDE
  - riepilogo uscite TOP/SIDE effettivamente collegate
- esteso l'handler eventi del timed scheduler per aggiornare la diagnostica live del pannello
- aggiunta procedura dedicata di commissioning e collaudo:
  - `Docs/Commissioning/08_Photocell_Timed_Trigger_Commissioning.md`

**Nota baseline:**
- le fasi architetturali del timed trigger risultano sostanzialmente completate
- il prossimo lavoro e' soprattutto macchina/test:
  - taratura delay
  - verifica dispersione scatti
  - qualifica finale del processo

## [2026-04-29] Fase 3+4 timed trigger - scheduler dedicato e integrazione runtime fotocellula

**File modificati:**
- `Services/PhotocellTimedTriggerService.cs`
- `ViewModels/DigitalIOViewModel.cs`
- `Views/UserControls/DigitalIOControl.xaml`
- `Localization/messages_eng.json`
- `Localization/messages_ita.json`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`
- `Documentation/MachineHardware/photocell-timed-trigger-development-plan-2026-04-29.md`

**Scopo:**
- attivare il primo percorso runtime reale della modalita `TimedFromPhotocell`
- collegare la fotocellula reale a due trigger camera temporizzati indipendenti TOP/SIDE
- mantenere il flusso legacy esistente come fallback esplicito

**Implementazione:**
- introdotto `PhotocellTimedTriggerService` con:
  - clock monotono `Stopwatch`
  - batch trigger per singolo fronte fotocellula
  - scheduling indipendente TOP/SIDE
  - eventi diagnostici strutturati
- integrata la nuova modalita nel flusso `OnInputChanged`:
  - se `TriggerSchedulingMode=TimedFromPhotocell`, il fronte valido della fotocellula non crea piu il prodotto per il trigger camera legacy
  - viene invece accodata una batch temporizzata con gli output reali configurati
- gli impulsi fisici restano eseguiti tramite la logica gia robusta di `PulseMappedOutputAsync`, quindi:
  - serializzazione per canale fisico
  - logging `Pulse START` / `Pulse END`
- aggiunto pannello diagnostico runtime nella view con:
  - stato ultimo batch
  - ultimo fronte fotocellula
  - ultimo trigger TOP
  - ultimo trigger SIDE
- aggiunta cancellazione esplicita dei batch pendenti durante:
  - preview runtime
  - save/load configurazione
  - switch hardware/simulazione
  - dispose

**Nota baseline:**
- il timed trigger ora e' attivo davvero a runtime
- il vecchio flusso `VirtualConveyor` resta disponibile e non viene rimosso in questa release
- questa fase si concentra sul trigger camera deterministico; eventuali estensioni ulteriori restano demandate alle fasi successive

## [2026-04-29] Fase 1+2 timed trigger - modalita' macchina e parametri base persistenti

**File modificati:**
- `Models/MachineRuntimeConfiguration.cs`
- `ViewModels/DigitalIOViewModel.cs`
- `Views/UserControls/DigitalIOControl.xaml`
- `Localization/messages_eng.json`
- `Localization/messages_ita.json`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

**Scopo:**
- iniziare l'integrazione pulita del nuovo flusso `TimedFromPhotocell`
- aggiungere al file macchina i parametri canonici del trigger temporizzato senza ancora deviare il runtime produttivo legacy

**Implementazione:**
- aggiunta in `MachineRuntimeBindings` della nuova modalita' persistente:
  - `TriggerSchedulingMode`
- aggiunti i parametri macchina persistenti:
  - `PhotocellDebounceMs`
  - `MinimumRetriggerGapMs`
  - `TopTriggerBaseDelayMs`
  - `SideTriggerBaseDelayMs`
  - `TopTriggerPulseMs`
  - `SideTriggerPulseMs`
- aggiornato `DigitalIOViewModel` con:
  - save/load completo dei nuovi campi nel `machine_runtime_config.xml`
  - riepilogo operativo della modalita' di scheduling
  - riepilogo sintetico dei parametri timed trigger
  - uso runtime dei nuovi tempi per la finestra anti-doppio fronte fotocellula
- aggiornata la UI `DigitalIOControl` con una sezione dedicata al trigger temporizzato da fotocellula
- aggiunte le nuove label al catalogo localizzazione tool

**Nota importante baseline:**
- in questa fase il nuovo blocco e' configurabile, localizzato e persistente
- il runtime produttivo resta ancora sul flusso legacy attuale finche' non verra' introdotto il servizio scheduler dedicato della fase successiva

## [2026-04-29] Piano tecnico documentato - timed trigger da fotocellula senza PLC

**File modificati:**
- `Documentation/MachineHardware/photocell-timed-trigger-development-plan-2026-04-29.md`

**Scopo:**
- formalizzare il piano di sviluppo per abbandonare il trigger produttivo basato su `virtual conveyor`
- definire la soluzione raccomandata per questa macchina:
  - `fotocellula reale -> delay temporali deterministici -> uscite trigger reali`
- chiarire cosa si intende per `hardware reale` nel contesto Advantech / ADAM / PC industriale

**Nota:**
- nessuna modifica runtime in questa voce
- nessun cambio release software da questa sola documentazione
- il documento serve come baseline di sviluppo prima dell'implementazione pulita del nuovo scheduler trigger

## [2026-04-29] Trigger mapping source of truth restored to machine configuration

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`
- `Properties/AssemblyInfo.cs`

**Problema analizzato:**
- il runtime continuava a inferire `OUT_CAMERA_TOP_TRIGGER` / `OUT_CAMERA_SIDE_TRIGGER` dal nome del punto intervento quando `SignalCode` era vuoto
- questo rendeva ambiguo il comportamento in commissioning e rischiava di sembrare che il software ignorasse la configurazione salvata a video

**Fix applicato:**
- in `MigrateLegacyTwoCameraRuntimeConfiguration()` il runtime non ricostruisce più il `SignalCode` di un punto `TriggerCamera` partendo dal nome `TOP/SIDE`
- restano solo:
  - creazione default su configurazioni nuove
  - migrazione di codici legacy già noti (`OUT_LIGHT_TOP`, `OUT_LIGHT_SIDE`, `OUT_CAMERA_TRIGGER`)
- il `SignalCode` salvato nel file macchina resta quindi la fonte di verità del collegamento logico -> uscita

**Impatto:**
- nessun cambio schema XML / DB
- se un punto `TriggerCamera` ha `SignalCode` vuoto nella configurazione attiva, ora viene saltato con warning esplicito invece di essere reinterpretato automaticamente

## [2026-04-29] Fix avvio I/O: DigitalIOViewModel creato a startup, non alla prima navigazione Setting

**File modificati:**
- `App.xaml.cs` — `ViewModelCache.DigitalIOVM` aggiunto; VM creato in `PreloadViewModelsAsync`; dispose in `OnExit`
- `Services/ViewFactoryService.cs` — case "Setting" riusa `ViewModelCache.DigitalIOVM` invece di `new DigitalIOViewModel()`
- `ViewModels/DigitalIOViewModel.cs` — aggiunto `_disposed` guard idempotente a `Dispose()`

**Motivo:** L'I/O hardware (scheda Advantech, polling fotocellula, machine controller) veniva inizializzato solo alla prima navigazione alla pagina Setting. Se l'operatore non apriva quella pagina, il software non reagiva ai prodotti in linea.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `App.ViewModelCache.DigitalIOVM` aggiunto (nuovo campo statico)
- Comportamento runtime: I/O e machine controller attivi immediatamente all'avvio dell'app, indipendente dalla navigazione

---

## [2026-04-29] Fix concorrenza doppi scatti: lock trigger, debounce OR, Interlocked encoder, semaforo canale output

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — lock su `_executedTriggerOutputsByProduct`, debounce fotocellula OR, semaforo per-canale in `PulseMappedOutputAsync`
- `Services/MachineController.cs` — `Interlocked.Exchange/Read` per `_lastMainEncoderCount`

**Motivo:** Quattro race condition indipendenti producevano doppi scatti e posizioni incongruenti in modalità hardware reale.

**Bug 1 — `_executedTriggerOutputsByProduct` non thread-safe**
`OnCounterChanged` chiama `UpdateEncoderPosition` direttamente dal thread Advantech (background), che a sua volta spara `InterventionPointReached` e poi `ExecuteInterventionPointAsync` → `TryRegisterTriggerOutputForProduct`. Senza lock, check-and-add sul dizionario è una race condition: due thread possono entrambi passare il controllo e pulsare lo stesso output.
Fix: aggiunto `_triggerOutputsLock` (object) attorno a tutto il corpo di `TryRegisterTriggerOutputForProduct`, `ClearTriggerOutputRegistrationsForProduct` e `ClearTriggerDiagnostics`.

**Bug 2 — Debounce fotocellula usava AND invece di OR**
`ShouldIgnorePhotocellDuplicate` restituiva `true` (ignora) solo se ENTRAMBE: elapsed ≤ 80ms E distanza ≤ 8mm. Ad alta velocità la distanza supera 8mm in pochi ms e il duplicato passava. OR logic: ignora se elapsed < 80ms OPPURE distanza < 8mm.

**Bug 3 — `_lastMainEncoderCount` senza visibilità cross-thread**
Il valore veniva scritto dal thread Advantech via `UpdateEncoderPosition` e letto senza garanzie di visibilità dalla fotocellula (UI thread). Sostituito con `Interlocked.Exchange` in scrittura e `Interlocked.Read` in lettura. Aggiunto `using System.Threading` a `MachineController.cs`.

**Bug 4 — `PulseMappedOutputAsync` senza serializzazione per canale**
Due pulse concurrent sullo stesso canale fisico potevano interfoliare ON/OFF, lasciando il canale in stato indefinito. Aggiunto `_outputChannelSemaphores` (Dictionary keyed by channel) con `_outputChannelSemaphoresLock`. Ogni canale ha un `SemaphoreSlim(1,1)` che serializza i pulse.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: doppi scatti soppressi per race condition; encoder position letta sempre fresca; output channel ON/OFF sempre atomici

---

## [2026-04-29] Fix SignalCode non persiste in intervention points + fix NormalizeOutputSignal sovrascrive valori utente

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — `CommitPendingSignalEdits`, `CommitEditableView`, `NormalizeOutputSignal`
- `Views/UserControls/DigitalIOControl.xaml` — `PreviewKeyDown` aggiunto al DataGrid intervention points

**Bug 1 — SignalCode intervention point non persisteva dopo salvataggio**

`CommitPendingSignalEdits()` committava solo `MachineInputsView` e `MachineOutputsView`.
Il DataGrid degli intervention points usa `ItemsSource="{Binding InterventionPoints}"` senza
un named `ICollectionView`, quindi la sua transazione `EditItem` rimaneva aperta quando
l'utente cliccava Save. Il binding `Text="{Binding SignalCode}"` sul `ComboBox` non aveva
scritto il valore nell'oggetto `MachineInterventionPoint` — `BuildRuntimeConfigurationFromView`
leggeva il valore precedente (vuoto) e lo serializzava nell'XML. Al riavvio l'XML corretto
non c'era, la cella appariva bianca.

**Fix:**
- `CommitPendingSignalEdits` ora chiama `CommitEditableView` anche su
  `CollectionViewSource.GetDefaultView(InterventionPoints)` e `CollectionViewSource.GetDefaultView(AdditionalRuntimeBindings)`
- Aggiunto helper privato `CommitEditableView(IEditableCollectionView)` per evitare duplicazioni
- Aggiunto `PreviewKeyDown="OnSignalDataGridPreviewKeyDown"` al DataGrid intervention points
  così Enter commita la riga anche lì, simmetrico agli altri DataGrid

**Bug 2 — `NormalizeOutputSignal` sovrascriveva Board/Channel dell'utente ad ogni load**

`NormalizeOutputSignal` cercava il segnale per OLD code **oppure** NEW code (con `?? ...`).
Se trovava il NEW code (già migrato), sovrascriveva ugualmente Board, Channel ecc. con valori
hardcoded ("DO01", "DO02"). Qualsiasi personalizzazione (es. cambio di canale fisico) veniva
annullata al riavvio, rendendo il file XML di configurazione inaffidabile perché la migrazione
vinceva sempre sui valori salvati.

**Fix:** rimosso il branch `?? MachineOutputs.FirstOrDefault(newSignalCode)`. `NormalizeOutputSignal`
ora agisce SOLO quando trova il codice LEGACY (vecchia stringa). Se il segnale è già al codice
moderno la funzione ritorna subito — i valori salvati dall'utente rimangono intatti. La
funzione `EnsureOutputSignal` (chiamata subito dopo) gestisce il caso in cui il segnale non
esiste affatto.

**Impatto:**
- Config.xml: il file è ora la fonte di verità per Board/Channel dei segnali migrati
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: SignalCode dell'intervention point persiste correttamente; i canali fisici personalizzati sopravvivono al riavvio

## [2026-04-29] DataGrid signal edit UX — Enter commit, no blank row, default real-signals filter

**File modificati:**
- `Views/UserControls/DigitalIOControl.xaml.cs` — aggiunto `OnSignalDataGridPreviewKeyDown`
- `Views/UserControls/DigitalIOControl.xaml` — Inputs/Outputs DataGrid: `CanUserAddRows=False`, `CanUserDeleteRows=False`, `PreviewKeyDown` handler
- `ViewModels/DigitalIOViewModel.cs` — `_showOnlyRealSignals` default cambiato a `true`

**Bug: row in edit mode dopo Enter**
Il `MappingDataGridStyle` aveva `CanUserAddRows=True` / `CanUserDeleteRows=True`. WPF
DataGrid default: Enter su una cella commita la cella ma lascia la riga in `EditItem`;
nell'ultima riga, navigava sulla riga "nuova" avviando un `AddNew`. La riga rimaneva
visivamente in edit e bloccava `CollectionView.Refresh()` al salvataggio successivo.

**Fix 1 — `CanUserAddRows="False" CanUserDeleteRows="False"`** sui DataGrid di input e
output: i segnali sono gestiti dal migration service, non vanno aggiunti manualmente dalla
tabella. Rimuove la riga vuota in fondo e impedisce le transazioni `AddNew`.

**Fix 2 — `OnSignalDataGridPreviewKeyDown`**: quando l'utente preme Enter dentro un
DataGrid segnali, chiama `CommitEdit(DataGridEditingUnit.Row, exitEditingMode:true)` e
sposta il focus sul DataGrid stesso. La riga esce dall'edit, il campo viene confermato,
e il salvataggio può procedere senza blocchi.

**UX: filtro "solo segnali reali" attivo di default**
L'utente usa solo fotocellula (IN) + camera TOP/SIDE (OUT). Le altre uscite (scarto,
allarme, heartbeat, spare) non sono collegate fisicamente. Il campo `_showOnlyRealSignals`
è ora inizializzato a `true`: all'avvio la tabella mostra solo i segnali marcati come
`ReservedForRealSignal=true`. Per vedere tutti i segnali basta deselezionare il filtro.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: nessuno; solo UX della pagina configurazione IO

## [2026-04-29] Fix save crash root cause — CollectionView.Refresh() in AddNew/EditItem mode

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — `RefreshSignalViews`, `SaveConfiguration`, nuovo metodo `CommitPendingSignalEdits`

**Root cause:** La segnalazione precedente attribuiva il crash a `OnMachineSignalPropertyChanged`
(reentrant). Il crash si ripresentava perché la chiamata diretta a `RefreshSignalViews()`
alla fine di `RefreshMachineSignalStatuses` eseguiva `CollectionView.Refresh()` mentre il
DataGrid delle uscite aveva ancora una transazione `AddNew` aperta (l'utente aveva cliccato
sulla riga vuota in fondo alla tabella per aggiungere un'uscita, senza confermarla).
`ICollectionView.Refresh()` lancia `InvalidOperationException` quando `IsAddingNew == true`
o `IsEditingItem == true`, indipendentemente da quale codice la invoca.

**Fix 1 — `SafeRefreshCollectionView`:** `RefreshSignalViews()` ora delega a un helper
statico che controlla `IEditableCollectionView.IsEditingItem` / `IsAddingNew` prima di
chiamare `Refresh()`. Se una transazione è aperta, il refresh viene saltato senza eccezione.
Questo rende ogni chiamata a `RefreshSignalViews()` sicura in qualunque contesto.

**Fix 2 — `CommitPendingSignalEdits`:** nuovo metodo privato chiamato all'inizio di
`SaveConfiguration`. Esegue `CommitEdit()` / `CommitNew()` sui CollectionView di input e
output prima di costruire la configurazione. Questo ha un duplice effetto:
1. chiude la transazione → `Refresh()` non lancia più
2. cattura il valore dell'eventuale riga ancora in modifica, così viene incluso nel salvataggio

**Motivo:** il workflow normale è: utente modifica un'uscita → clicca Salva senza premere
Invio/Tab per uscire dalla riga → DataGrid è in AddNew/EditItem → crash.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: save non lancia più eccezione; righe parzialmente compilate vengono incluse nel salvataggio

## [2026-04-29] Fix save crash, CAMERA_TRIGGER_SIDE double-shot, and SignalCode fallback

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs` — tre fix distinti (vedi sotto)

**Bug 1 — Save crash: `'Refresh' is not allowed during an AddNew or EditItem transaction`**

`RefreshMachineSignalStatuses()` imposta `IsRuntimeBound`, `IsAdditionalBinding`,
`RuntimeUsageLabel`, `SignalModeLabel` sugli oggetti `MachineSignalDefinition`. Queste
proprietà sparano `PropertyChanged`, che invocava `OnMachineSignalPropertyChanged` →
`RefreshSignalViews()` → `CollectionView.Refresh()`. Se il DataGrid era in modalità
AddNew/EditItem (l'utente stava modificando una riga), la refresh lanciava
`InvalidOperationException`, abortendo il blocco `try` di `SaveConfiguration`.
La configurazione veniva comunque salvata su disco (il file XML era già scritto prima
della chiamata a `RefreshMachineSignalStatuses`), ma `MarkConfigurationSaved()` non
veniva eseguita e veniva loggato un errore ingannevole.

**Fix:** aggiunto early-return in `OnMachineSignalPropertyChanged` per
`IsRuntimeBound`, `IsAdditionalBinding`, `RuntimeUsageLabel`, `SignalModeLabel`. Queste
sono proprietà di stato calcolate, non modificate dall'utente; non devono mai attivare
`RefreshSignalViews()` o `MarkConfigurationDirty()`.

**Bug 2 — CAMERA_TRIGGER_SIDE mappa sullo stesso output fisico di CAMERA_TRIGGER_TOP**

Quando `CAMERA_TRIGGER_SIDE.SignalCode` era vuoto (config salvata prima della
migrazione, o campo cancellato dall'utente), `ResolveInterventionSignalCode` faceva
fallback a `BoundCameraTriggerSignalCode` = `"OUT_CAMERA_TOP_TRIGGER"` (DO02). Sia TOP
sia SIDE finivano sullo stesso canale fisico: `ValidateCameraTriggerOutputMappings`
rilevava l'overlap e lo sopprimeva, ma il trigger veniva comunque inviato due volte
per prodotto (doppio scatto).

**Fix a:** nel loop di migrazione (`MigrateLegacyTwoCameraRuntimeConfiguration`), prima
di `MapLegacyCameraSignalCode`, se il punto è `TriggerCamera` con `SignalCode` vuoto,
viene assegnato il codice canonico in base al `PointCode`: "SIDE" → `OUT_CAMERA_SIDE_TRIGGER`,
"TOP" → `OUT_CAMERA_TOP_TRIGGER`. Questo ripristina automaticamente i codici errati al
prossimo avvio senza richiedere un salvataggio manuale.

**Fix b:** `ResolveInterventionSignalCode` — se `SignalCode` è vuoto e il `PointCode`
contiene "SIDE", restituisce `string.Empty` invece di `BoundCameraTriggerSignalCode`,
impedendo che il runtime usi l'uscita TOP per il punto SIDE.

**Fix c:** `ExecuteInterventionPointAsync` caso `TriggerCamera` — se `signalCode` è
vuoto dopo la risoluzione, il punto viene saltato con un warning nel log invece di
sparare `BoundCameraTriggerSignalCode` come fallback (rimosso il `? BoundCameraTriggerSignalCode`
inline, che era dead-code post-risoluzione ma pericoloso).

**Motivo:** produzione con due telecamere: DO1 = camera 1 (SIDE), DO2 = camera 2 (TOP).
L'overlap causava un doppio scatto su DO2 per ogni prodotto.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime: save non lancia più eccezione; CAMERA_TRIGGER_SIDE non attiva più OUT_CAMERA_TOP_TRIGGER; la migrazione auto-ripristina SignalCode mancanti al caricamento

## [2026-04-28] Trigger commissioning hardening - photocell debounce, output trace, live runtime reapply

**File modificati:**
- `ViewModels/DigitalIOViewModel.cs`
- `Properties/AssemblyInfo.cs`
- `Documentation/MachineHardware/software-version-archive.md`

**Problemi analizzati:**
- doppio scatto residuo dopo la separazione dei punti `CAMERA_TRIGGER_TOP` / `CAMERA_TRIGGER_SIDE`
- variazioni di `Machine zero` / `Photocell mm` visibili in UI ma non immediatamente riflesse nel `MachineController`
- assenza di log fisico dettagliato per capire quale uscita ha realmente generato il trigger

**Fix applicati:**
- aggiunto filtro anti-rimbalzo sulla fotocellula prodotto:
  - un nuovo trigger viene ignorato se arriva entro `80 ms` e con avanzamento inferiore a `8 mm` rispetto all'ultimo trigger accettato
  - ogni trigger accettato/scartato viene loggato con encoder e quota macchina
- aggiunto logging completo sugli output trigger:
  - `Pulse START`
  - `Pulse END`
  - `Board / Channel`
  - motivazione / intervention point
- aggiunta protezione contro doppio trigger sulla stessa uscita fisica:
  - se due `TriggerCamera` attivi per lo stesso prodotto risolvono lo stesso `Board:Channel`, il secondo impulso viene soppresso e loggato
- aggiunto `live runtime reapply` debounced:
  - quando cambiano quote encoder/fotocellula, punti intervento o binding runtime, il `MachineController` viene reinizializzato in memoria
  - i prodotti tracciati attivi vengono svuotati per evitare quote obsolete ancora armate

**Impatto:**
- Config.xml / ricette / DB: nessun cambio schema
- Runtime: piu' robusto in commissioning, con diagnostica piu' leggibile e aggiornamento immediato del controller dopo modifiche macchina

## [2026-04-28] Fix doppio scatto e posizione scatto errata - virtual conveyor intervention points

**File modificati:**
- `Services/MachineController.cs` — aggiunto `ClearActiveProducts()`
- `ViewModels/DigitalIOViewModel.cs` — 3 fix: `SaveConfiguration`, `OnCounterChanged`, timer virtuale

**Bug 1 — Doppio scatto alla fotocellula (output alzati due volte):**
`UpdateEncoderPosition` confronta `encoderCount >= TargetEncoderCount` per tutti i punti nella stessa chiamata. Con `CAMERA_TRIGGER_TOP` a 74 mm e `CAMERA_TRIGGER_SIDE` a 76 mm (gap = 41 count), il timer da 50 ms a velocità ≥ 2.4 m/min copre > 41 count per tick → entrambi trovati già superati → due output pulsano nello stesso tick (scatto simultaneo).
**Fix:** timer ridotto da 50 ms a 20 ms → ~1 mm/tick a 3 m/min, il gap da 2 mm occupa tick separati fino a ~6 m/min.

**Bug 2 — Posizione scatto errata dopo cambio punti intervento:**
`SaveConfiguration` chiama `InitializeAsync(newConfig)` che aggiorna `Configuration` ma NON svuota `ActiveProducts`. I prodotti già tracciati con i vecchi target encoder continuano ad avanzare e sparano gli output alle vecchie posizioni (74/76 mm) anche dopo aver salvato i nuovi valori.
**Fix:** chiamata a `_machineController.ClearActiveProducts()` dopo `InitializeAsync` in `SaveConfiguration`.

**Bug 3 — Race difensivo: OnCounterChanged corrompe encoder base in virtual conveyor:**
In modalità real-HW + virtual conveyor, eventi `CounterChanged` reali (es. init board con count=0) sovrascrivono `_lastMainEncoderCount` nel controller. Al prossimo rilevamento fotocellula, il prodotto viene registrato con il count reale (basso), i target diventano piccoli, e il primo tick virtuale li supera tutti → tutti gli output sparano istantaneamente.
**Fix:** guard in `OnCounterChanged` che skips `UpdateEncoderPosition` per il canale virtuale quando `VirtualConveyorEnabled && !IsSimulationMode`.

**Impatto:**
- Config.xml / DB / API pubblica: nessuno
- Comportamento runtime: interventi sparano alle posizioni configurate; `ClearActiveProducts` fa sì che prodotti in volo al momento del save vengano abortiti (accettabile in fase commissioning)

## [2026-04-28] I/O camera trigger alignment - shared baseline 1.1.6.3

- restored the operational parts of the previous commissioning hotfix on top of the colleague-shared `1.1.6.2` baseline:
  - intervention-point `SignalCode` editable ComboBoxes now bind to `Text`, so custom/renamed output codes persist after save/reopen
  - `Photocell -> lighting bench mode` no longer bypasses product tracking when active camera intervention points are configured
- cleaned the default two-camera I/O mapping:
  - `OUT_SPARE_DO00 -> DO00` and not used by normal runtime
  - `OUT_CAMERA_SIDE_TRIGGER -> DO01`
  - `OUT_CAMERA_TOP_TRIGGER -> DO02`
- replaced the old default `LIGHTS_ON / CAMERA_TRIGGER / LIGHTS_OFF` flow with explicit `CAMERA_TRIGGER_TOP` and `CAMERA_TRIGGER_SIDE` intervention points
- added a runtime compatibility migration so older configs using `OUT_LIGHT_TOP`, `OUT_LIGHT_SIDE`, and `OUT_CAMERA_TRIGGER` are normalized in memory to the new two-camera mapping
- updated commissioning documentation and the signal mapping template to match the machine wiring currently under test

## [2026-04-28] Bilateral baseline merge local + shared lastRelease

- merged the local runtime/database branch with the colleague shared `lastRelease` branch into one unified production baseline
- preserved the commissioning hotfixes already promoted in shared baseline:
  - intervention-point `SignalCode` persistence
  - guided quick machine I/O setup
  - safer bench-mode behavior when camera intervention points are configured
- preserved the local runtime/database stabilizations:
  - `tblproduzione.TimeFine` legacy zero-date recovery path
  - I/O-driven startup policy that skips recipe camera trigger-delay writes
- aligned both repositories to a single merged release so future syncs can continue from one shared baseline instead of two diverging hotfix lines

## [2026-04-28] Fix SanitizeTblProduzioneTimeFineAsync - MySql.Data @variable parameter clash

**File modificati:**
- `Database/Cls_InitializzeDb.cs` — riscritta `SanitizeTblProduzioneTimeFineAsync`

**Motivo:** Le query `SET @qtis_old_sql_mode := @@SESSION.sql_mode` e `SET SESSION sql_mode = @qtis_old_sql_mode` usavano la sintassi `@nome` che MySql.Data interpreta come placeholder di parametro. Non avendo parametri legati al comando, il driver lanciava "Fatal error encountered during command execution" alla prima query, invalidando la connessione e bloccando l'intero startup del database. Il `finally` tentava il restore sulla connessione già rotta → WARN, poi l'eccezione originale propagava come ERROR "Failed to initialize database".

**Fix:**
- `sql_mode` letto via `ExecuteScalarAsync()` C# (`SELECT @@SESSION.sql_mode`) — nessun `@variable` MySQL nel testo SQL
- modalità rilassata calcolata in C# con `string.Split/Join` + LINQ, poi iniettata come stringa letterale nella query `SET SESSION sql_mode = '...'`
- ripristino originale con la stessa interpolazione letterale nel `finally`
- tutta la sanitizzazione avvolta in try-catch: se fallisce per qualsiasi motivo logga WARN e non blocca lo startup

**Impatto:**
- Config.xml: nessuno
- Database: nessuno (comportamento runtime invariato)
- API pubblica: nessuna
- Comportamento runtime: startup del database non fallisce più per zero-date legacy

## [2026-04-28] Commissioning runtime - intervention signal persistence and bench-mode guard

- hardened the intervention-point `SignalCode` editor so editable ComboBox values are saved as text and do not disappear after saving/reopening the machine configuration
- changed the legacy `Photocell -> lighting bench mode` guard:
  - simple bench validation still mirrors the photocell to the configured top output when no camera intervention points are active
  - if TOP/SIDE camera trigger intervention points are configured, standard product tracking remains active even if the bench checkbox is accidentally left enabled
- kept the runtime configuration schema unchanged:
  - no new XML fields
  - no recipe contract change
  - no database change

## [2026-04-28] Commissioning UI - guided I/O setup and encoder position refresh

- added a guided `Quick machine I/O setup` card in the Machine Configuration page for the current two-camera commissioning flow:
  - product photoeye input
  - TOP camera output pulse
  - SIDE camera output pulse
- kept the detailed input/output DataGrid tables available for advanced editing and diagnostics
- improved machine-position refresh when `Photoeye from zero` / `PhotocellMachineOffsetMm` changes so encoder and intervention summaries update immediately
- reduced DataGrid editing disruption when a `SignalCode` is renamed by avoiding a full collection-view refresh during the active edit
- kept runtime logic unchanged: the machine still uses the configured signal bindings and intervention points

## [2026-04-28] Database zero-date recovery and I/O-driven trigger-delay startup skip

- hardened `tblproduzione.TimeFine` initialization so legacy `0000-00-00 00:00:00` rows are normalized to `NULL` before enforcing the nullable datetime schema
- temporarily relaxes the MySQL session `sql_mode` during that migration and restores it afterwards, avoiding startup failure on strict servers
- stopped writing recipe `TriggerDelay` values into DALSA camera hardware during application startup and recipe reload
- preserved the trigger-delay values inside recipe data for historical/configuration continuity, but the runtime now logs them as skipped because the machine trigger is generated by digital outputs
- aligned Cognex hardware restore/reinitialize paths to the same startup rule so delayed background reinitializations do not silently write the camera delay later in the session

## [2026-04-28] Commissioning runtime - real I/O with virtual conveyor

- added `Virtual conveyor in HW` to the fused Digital I/O commissioning page so real `PCIE-1756` inputs and outputs can be used without a physical encoder connected
- persisted the manual conveyor speed and piece-rate parameters inside `machine_runtime_config.xml`
- advanced encoder channel 0 from the configured virtual speed while keeping photocell-triggered product tracking and millimeter-based intervention points active
- kept the output flow routed through the existing machine signal bindings:
  - top/side lighting outputs
  - camera trigger output
  - reject output
  - general alarm output
- documented the operating flow in `Documentation/MachineHardware/virtual-conveyor-io-trigger-2026-04-27.md`
- preserved the colleague shared-baseline UI fixes already present in `1.1.5.8`, including `BindingProxy` DataGrid header binding and ComboBox popup container hardening

## [2026-04-27] Commissioning UI - DigitalIOControl ComboBox popup container hardening

- hardened the previous `ComboBoxItem` alignment cleanup by assigning the item style directly through `ComboBox.ItemContainerStyle`
- added a shared `DigitalIoComboBoxStyle` in `DigitalIOControl.xaml` and made `CompactComboStyle` inherit from it
- ensured both regular and compact combo boxes use deterministic popup item alignment without depending on WPF ancestor lookup inside the drop-down popup
- kept the change scoped to UI rendering only:
  - no ViewModel change
  - no machine configuration change
  - no recipe or database contract change

## [2026-04-27] Commissioning UI - DigitalIOControl ComboBoxItem alignment binding cleanup

- fixed the WPF runtime binding noise generated by `ComboBoxItem.HorizontalContentAlignment` and `ComboBoxItem.VerticalContentAlignment` inside the merged `DigitalIOControl` page
- added an explicit `ComboBoxItem` style in `DigitalIOControl.xaml` so combo drop-down items no longer depend on `RelativeSource FindAncestor, AncestorType=ItemsControl`
- kept the change scoped to the commissioning control:
  - no ViewModel change
  - no machine configuration change
  - no recipe or DB contract change

## [2026-04-27] Commissioning UI - DigitalIOControl server-message integration

- moved the fused `DigitalIOControl` localization path onto the global `ServerMessagePersonalize` runtime message system used by the rest of the HMI
- extended the `Messages` model with JSON extension-data support so server-message files can carry tool-specific keys such as `Tool_Commissioning_*`, `Tool_Signals_*`, `Tool_TestIO_*` and `Tool_Log_*` without adding a C# property for every future label
- updated `ToolLocalizationService` so existing `L10n[key]` bindings now resolve from `ServerMessage` first and use the local tool catalog only as a compatibility fallback
- added a `ServerMessagePersonalize.MessagesUpdated` event and made the tool localization facade notify WPF indexer bindings when the active server-message language is reloaded
- updated `scripts/UpdateRuntimeLanguageFiles.ps1` to import the existing tool localization catalogs into the runtime language files in `C:\QtisVision\Language`
- refreshed the runtime language files so the Digital I/O labels are now present in:
  - `C:\QtisVision\Language\messages_eng.json`
  - `C:\QtisVision\Language\messages_ita.json`
- kept machine/config/recipe boundaries unchanged:
  - no `Config.xml` schema change
  - no recipe XML schema change
  - no MySQL schema change

## [2026-04-27] Commissioning UI - DigitalIOControl binding hardening and grid cleanup

- fixed the `DigitalIOControl` commissioning `DataGrid` header localization bindings that were generating WPF runtime errors such as `Cannot find source: RelativeSource FindAncestor, AncestorType='UserControl'`
- aligned the commissioning grid headers with the same `BindingProxy` strategy already used by the other machine-configuration grids in the page
- kept the mature standalone-tool localization contract intact:
  - no runtime/config path change
  - no DB contract change
  - no recipe XML contract change
- confirmed that the page already uses the dedicated `ToolLocalizationService` catalogs, so the fix avoids mixing a second localization mechanism into this fused commissioning baseline
- retained the industrial styling already introduced in the merged view:
  - consistent column-header palette
  - alternating rows
  - clearer cell spacing for touch/maintenance readability

## [2026-04-27] Tooling - shared OneDrive repo alignment workflow

- added `scripts/SyncSharedBaseline.ps1` to compare and realign the local engineering baseline with the colleague shared OneDrive folder
- the script is preview-first by default and only performs file copies when `-Apply` is passed
- added an optional `-Mirror` mode for controlled target cleanup when the shared folder must become a strict mirror
- excluded local-only and generated artifacts from sync:
  - `.git`
  - `.vs`
  - `.vscode`
  - `.claude`
  - `bin`
  - `obj`
  - `packages`
  - generated WPF outputs
  - local logs
- documented the agreed operating flow in `Documentation/MachineHardware/shared-repo-alignment-process-2026-04-27.md`

## [2026-04-27] Commissioning - fused standalone I/O and encoder runtime into lastRelease baseline

- promoted the standalone commissioning/runtime slice into the correct colleague baseline at `lastRelease\QtisVisionPanel`
- added the machine commissioning runtime contracts:
  - `MachineRuntimeConfiguration`
  - `MachineHardwareTemplate`
  - `TrackedProduct`
  - `MachineConfigurationBackupInfo`
  - `ToolFusionSnapshot`
  - `ToolMessageCatalog`
- added the supporting services used by the mature tool/runtime flow:
  - `MachineConfigurationService`
  - `MachineController`
  - `ToolFusionSnapshotService`
  - `ToolLocalizationService`
  - `ToolMainProjectEventMapper`
- replaced the older full-project commissioning UI slice with the newer standalone-derived implementation:
  - `DigitalIOViewModel`
  - `DigitalIOControl.xaml`
  - `DigitalIOControl.xaml.cs`
- aligned the low-level Advantech integration layer with the standalone version so the fused commissioning view can compile and run:
  - `AdvantechDeviceManager`
  - `IIODeviceManager`
  - `IOChannel`
  - `Support\TaskExtensions`
- added the runtime assets required by the commissioning subsystem:
  - `ConfigurationTemplates\machine_runtime_config.template.xml`
  - `Localization\messages_eng.json`
  - `Localization\messages_ita.json`
  - `Docs\Commissioning\01..07`
- moved the I/O commissioning connection bar inside `DigitalIOControl.xaml` so the `HW/SIM` switch remains visible when the control is hosted by the full QtisVisionPanel shell instead of the standalone tool window
- hardened the `PostBuildEvent` so the project can build from the share without failing on the VisionPro junction creation step
- resolved an integration conflict with the pre-existing `TaskExtensions` helper in `MainWindow.xaml.cs` by keeping the shared helper in `Support\TaskExtensions.cs` as the canonical implementation
- verified the updated `lastRelease` baseline with `MSBuild Debug|x64`; build is green and the remaining output is warning-only

## [2026-04-24] Analytics - cached dashboard snapshots, stricter recipe XML resolution and richer measurement trends

- introduced an in-memory `DataAnalysisSnapshotCacheService` so the analytics page can reopen using the last available snapshot instead of waiting for all MySQL queries before the first render
- `DataAnalysisViewModel` now applies the latest cached snapshot immediately, then refreshes the dashboard asynchronously in the background
- hardened the recipe resolver used by analytics:
  - only `.xml` candidates are accepted
  - the file must expose the expected `Recipedata` root node
  - unsupported or non-recipe files are skipped before reaching `AsyncRecipeParam`
- tightened runtime fallback behavior for recipe filtering:
  - the current runtime recipe is reused only when it really matches the requested recipe name
  - if the selected recipe cannot be resolved safely, defect filtering no longer falls back to an unrelated active recipe
- enriched measurement trend cards with operator-readable plotting aids:
  - Y axis values
  - X axis time labels
  - nominal reference line
  - tolerance band and upper/lower tolerance lines
  - expanded trend summary with nominal/tolerance information when available from the recipe
- connected trend reference values to the recipe model:
  - `Height trend` uses `recipeParamSide.Min_Heigth_value` and `recipeParamSide.Heigth_toll`
  - `3D height/width/length` use `recipeParamTop3D` nominal and tolerance values
- kept the dashboard non-invasive for runtime vision flow:
  - no inspection-cycle logic was moved into the UI
  - no machine/rich recipe contracts were changed
  - analytics remains read-only over the production database

## [2026-04-23] Runtime - demo auto-login and PowerFlex machine conversion editing

- added a new machine-level config flag `Configuration.AutoLoginAdministratorForDemo`
- when the flag is `true`, startup now forces the panel session to `Administrator` without opening the login flow, intended for supervised fair/demo usage
- when the flag is `false`, startup continues with the standard `Guest / Viewer` session
- centralized the startup-session decision in `UserSession` so splash boot and top-bar session bootstrap stay aligned
- added a new `PowerFlex 525` UI card for machine conversion parameters:
  - `Meters/min per Hz`
  - `Pulley diameter`
  - `Gear ratio`
- the three values are now editable directly from the panel and saved back to `Config.xml`
- saving the machine conversion settings recalculates displayed speed immediately and writes an operational event to the application event log

## [2026-04-23] Integration - PowerFlex 525 speed fallback and fault reset commands

- corrected PowerFlex parameter scaling so `P032 Motor NP Hertz` is no longer divided by `100` and now reflects the nominal frequency value read from the drive
- improved `m/min` rendering logic:
  - first uses `PowerFlex525.MetersPerMinutePerHz` when configured
  - otherwise uses pulley/mechanical data when available
  - otherwise falls back to live `b001` output frequency so the operator still sees a real moving value during commissioning instead of `0` / `--`
- added dedicated PowerFlex UI commands for:
  - `Reset fault` via `A551 = 1`
  - `Clear fault history` via `A551 = 2`
- extended the EtherNet/IP CIP client to support parameter instances above `255`, required to reach `A551 [Fault Clear]`
- added raw log traces for the new CIP write path and audit/history entries for both fault commands
- updated operator/server-message texts and manual notes for the new commands and temporary speed fallback behavior

## [2026-04-23] UI - PowerFlex belt speed shown in top menu

- connected `TopMenuBarViewModel` directly to the optional `PowerFlex525Service`
- starts PowerFlex polling from the top menu so belt speed updates even when the dedicated PowerFlex page has not been opened
- top menu speed now displays the unit explicitly as `m/min`
- subscribed to PowerFlex speed changes so the top menu refreshes immediately when a new inverter value is read

## [2026-04-23] Integration - PowerFlex 525 full read and fault event logging

- extended PowerFlex CIP reads from monitor-only to all tracked parameters:
  - monitor `b001`, `b003`, `b004`, `b005`, `b017`
  - diagnostics `b006`, `b007`, `b008`, `b009`
  - motor `P031..P037`
  - ramps/limits `P041..P044`
- added UI fault status card based on `b007` most recent fault code, with `b008` and `b009` shown as recent history
- updated drive state rendering to derive RUN/STOP from live output frequency and include `b006` raw status code
- added critical alarm event generation through `ApplicationEventLogger.LogAlarmEvent` when `b007` becomes non-zero
- fault events are written to the normal application event logging path and database when DB logging is available
- kept parameter writes disabled until readback and fault diagnostics are validated during commissioning

## [2026-04-23] Integration - PowerFlex 525 CIP monitor read prototype

- added a first real EtherNet/IP explicit messaging adapter inside `PowerFlex525EtherNetIpClient`
- implemented `RegisterSession`, `SendRRData`, `UnregisterSession` and `Get Attribute Single`
- first CIP read path uses:
  - service `0x0E`
  - class `0x0F` Parameter Object
  - instance equal to the PowerFlex parameter number
  - attribute `0x01`
- enabled read attempts for monitor parameters `b001`, `b003`, `b004`, `b005` and `b017`
- added raw hex logging for RegisterSession, SendRRData, CIP request and CIP response to support commissioning on the real inverter
- kept parameter writes blocked until read values are validated on the real PowerFlex 525

## [2026-04-23] Diagnostics - PowerFlex 525 connection status and logs

- added a dedicated PowerFlex connection status in the UI so operators can distinguish:
  - missing/disabled IP configuration
  - EtherNet/IP TCP `44818` unreachable
  - EtherNet/IP reachable but CIP parameter adapter not connected
- added NLog entries for PowerFlex endpoint reachability, timeout, rejected connection and unavailable parameter reads
- throttled repeated PowerFlex connectivity logs to avoid flooding machine log files during periodic polling
- kept the integration non-blocking: reachable ping without available CIP parameters is reported as diagnostics, not as machine-fatal state

## [2026-04-23] Fix - PowerFlex 525 startup remains optional without inverter

- stopped `ServiceLocator.Initialize()` from creating and polling the PowerFlex 525 service during application bootstrap
- changed `PowerFlex525Service.Start()` so opening/starting the page does not immediately force a network refresh before the first timer tick or manual operator refresh
- replaced the EtherNet/IP reachability check with a guarded `BeginConnect` / `EndConnect` path that catches rejected connections and cancellations as unavailable drive state instead of allowing development-PC startup failures
- preserved the non-blocking contract: machines or development PCs without the inverter connected continue to start and operate normally

## [2026-04-23] Machine integration - PowerFlex 525 inverter settings page

- added a guarded `Setting -> PowerFlex 525` page for Administrator, Installer and Expert users
- introduced `PowerFlex525` runtime configuration in `Config.xml` for IP address, polling, timeout, local history path and m/min conversion data
- added a non-blocking PowerFlex service and EtherNet/IP boundary so missing or unreachable inverter communication does not stop the machine runtime
- replaced the random top-bar transport speed with the PowerFlex-derived `m/min` value when available, falling back to `0` when unavailable
- added parameter catalog coverage for `b001`, `b003`, `b004`, `b005`, `b017`, `P031..P037`, `P041..P044`
- added a `Modified parameters` section for the PowerFlex Modified M group, guarded behind the same CIP adapter boundary
- marked `P031`, `P032`, `P036`, `P043` and `P044` as stop-required parameters so Apply can be blocked while the drive is running
- added local JSON and MySQL history support through `tblpowerflex525_history`
- updated server-message keys and runtime language files for the new page labels
- added operator and technical documentation for configuration, usage and collaudo

## [2026-04-22] Fix - responsive shell resources no longer crash WPF parsing

- corrected the responsive-shell implementation after a startup crash caused by assigning `double` resources to WPF properties that require typed `GridLength` / `CornerRadius` values
- `MainWindow.xaml` no longer binds `DynamicResource` doubles directly to:
  - `RowDefinition.Height`
  - `ColumnDefinition.Width`
  - outer shell `Border.CornerRadius`
- `MainWindow.xaml.cs` now applies those sensitive shell metrics with the correct runtime types through direct code updates:
  - `GridLength` for row/column definitions
  - `CornerRadius` for the outer shell border
- kept the rest of the responsive system active, so adaptive behavior from 15-inch to 23-inch panels remains available without the XAML parse exception

## [2026-04-22] UI - centralized multi-panel responsive layout profiles

- introduced centralized responsive shell resources in `MainWindow.xaml` and `MainWindow.xaml.cs`
- the main window now recalculates layout metrics on load and on resize using three practical HMI profiles:
  - compact for 15-inch / low-resolution panels
  - medium for mid-size panels
  - large for 23-inch / wide operator displays
- moved the key shell dimensions to dynamic resources so the same runtime can adapt without maintaining separate XAML variants:
  - shell margins and corner radii
  - navigation width and spacing
  - top bar paddings and typography
  - camera panel heights and feature-icon sizing
- updated `TopMenuBar.xaml`, `NavigationMenu.xaml`, `CameraContainer.xaml`, `TopCameraView.xaml` and `SideCameraView.xaml` to consume the shared responsive resources instead of fixed values
- preserved page-local responsive logic already added to `PreferenceView`, so the settings page now works together with the new global profiles instead of fighting them

## [2026-04-22] UI - compact rendering pass for 15-inch operator panels

- tightened the main shell proportions in `MainWindow.xaml` so the navigation rail and outer paddings consume less space on smaller HMI displays
- reduced the visual density of `TopMenuBar.xaml` with lighter metric-card spacing, slightly smaller status typography and a narrower machine-status block
- compacted `NavigationMenu.xaml` with a slimmer left menu baseline, smaller paddings and a lighter header block
- compacted `TopCameraView.xaml`, `SideCameraView.xaml` and `CameraContainer.xaml` so the overview consumes less vertical space and feels less oversized on 15-inch panels
- updated `PreferenceView.xaml` and `PreferenceView.xaml.cs` so the preferences page now wraps its cards responsively and recalculates their width from the real viewport, instead of staying locked to a desktop-like 3-column layout

## [2026-04-22] Fix - camera-role/runtime warnings hardened by serial matching and generic GigE support

- extended `Models/CameraModel.cs` so runtime role resolution can now use the physical camera serial from `Config.xml` when the QuickBuild job name is generic and `CameraConfig.xml` is stale or incomplete
- updated `Services/CameraDisplayManager.cs` and `ViewModels/CameraContainerViewModel.cs` to prefer:
  - explicit semantic role from the active job name
  - runtime serial match against the configured machine serials
  - legacy fallback role mapping from `CameraConfig.xml`
- reduced false warning noise in the camera overview:
  - a runtime job without explicit `CameraConfig.xml` role match is no longer treated as drift when its serial already matches the configured machine role
  - leftover camera roles are downgraded from warning to info when their configured serial is already present in another runtime job or shared across multiple configured roles
- updated `Cls_Vpro/IgigaCameraAccess.cs` so trigger configuration no longer rejects non-DALSA GigE sensors only because the frame-grabber name does not contain `Teledyne DALSA`
- the runtime now logs generic GigE detection for supported non-DALSA sensors and keeps the trigger feature path guarded by actual `OwnedGigEAccess` availability instead of brand-name string matching

## [2026-04-22] Operator documentation - Top3D / Top2D visualization workflow

- added `Docs/Manual/04_Controllo_3D_Top3D.md`
- added `Docs/Manual/04_Controllo_3D_Top3D.txt`
- documented the operator steps required to:
  - configure recipe-level `Top3D primary view` and `Top2D secondary view`
  - understand the fallback to `Config.xml -> LasRunParam`
  - verify `Top3D` and `Top2D` image rendering in overview
  - interpret `Measured`, `Target`, `GOOD`, `NO GOOD`, `WAIT`
  - gather the right support information when the 3D images are missing or invalid
- linked the new guide from the existing recipe/alarm/operator manual

## [2026-04-22] Feature - recipe-level Top3D/Top2D runtime view overrides

- added optional per-recipe `recipeParamTop3D.Top3DPrimaryLastRunView` and `recipeParamTop3D.Top3DSecondaryLastRunView` fields so each 3D recipe can override the displayed VisionPro record paths without breaking existing XML files
- updated runtime display resolution to prefer recipe-level Top3D overrides and fall back to the existing `Config.xml -> LasRunParam` contract when recipe fields are empty
- mirrored `Top3D` inspections on a second operator view labeled `Top2D`, using the top record itself to show planar width/length imagery while keeping the machine-role model unchanged
- hid the standard top/side feature ribbons when those panes are repurposed for `Top3D` / `Top2D` display-only usage to avoid misleading operators
- fixed the `3DCheck` product-properties overlap caused by the generic `Other` machine template still being visible together with the dedicated `3DCheck` layout
- widened the `Top3D profilometer checks` editor card so nominal values and tolerances remain readable instead of clipping on the panel
- synchronized the new Top3D/Top2D labels and measurement-summary keys into `ServerMessageStructure` and the runtime JSON language packs under `C:\QtisVision\Language`
- when only one `Top3D` runtime job exists, the overview now adds a virtual `Top2D` pane and shows `GOOD / NO GOOD` measurement summaries for height, width and length

## [2026-04-21] Fix - 3DCheck recipe editor product properties restored and Top3D output lookup hardened

**File modificati:**
- `Views/RecipeManagerView.xaml`
  - aggiunta una sezione esplicita `3DCheck` dentro `Product properties`
  - la ricetta `Top3D` puo' ora mostrare e modificare direttamente:
    - `Product_Height`
    - `Product_Width`
    - `Product_Length`
    - `Product_Depth`
- `Converters/BooleanConverters.cs`
  - `MachineTypeToVisibilityConverter` ora gestisce anche il caso `Other` come fallback reale:
    tutto cio' che non e' `Packs` o `Rolls`
- `DataManage/IToolBlockValidator.cs`
  - `TryGetOutputValue(...)` non indicizza piu' `toolBlock.Outputs[outputName]` prima di verificare `Contains`
  - quando un output `Top3D` manca, il messaggio diagnostico ora include anche gli output realmente disponibili nel toolblock

**Motivo:**
Nel flusso `3DCheck` erano emerse due regressioni:
- la card `Product properties` restava vuota perche' `MachineType = 3DCheck` non attivava nessuno dei layout previsti (`Packs`, `Rolls`, `Other`)
- il validator `Top3D` lanciava eccezioni del tipo
  `Collection does not contain an index for ThreeDHeight`
  perche' il codice tentava di leggere un output mancante tramite indicizzazione diretta

**Impatto:**
- Config.xml: nessuna modifica
- Database: nessuna modifica schema
- Ricette XML:
  - nessuna migrazione richiesta
  - la UI torna a rendere editabili le proprieta' prodotto gia' presenti nel contratto `general_Info`
- Runtime:
  - `Top3D` non fallisce piu' con eccezione grezza quando un output configurato manca
  - il log indica in modo piu' utile quali output sono davvero esposti dal toolblock attivo

**Note operative:**
- se il log riporta ancora:
  - `Output='ThreeDHeight'`
  - `AvailableOutputs='...'`
  allora il VPP attivo non sta ancora pubblicando l'output col nome atteso dal pannello
- in quel caso verificare:
  - `Config.xml -> ThreeDHeightOutput / ThreeDWidthOutput / ThreeDLengthOutput`
  - output finali realmente esposti dal toolblock `Results` o dal fallback toolblock `Top3D`

---

## [2026-04-21] Top3D - L38-300 artifact capture integrated into piece save pipeline

**File modificati:**
- `SaveImage/ISaveImage.cs`
  - il salvataggio immagini standard ora esegue anche un export `Top3D` sidecar quando il job top attivo e' semanticamente `Top3D`
  - aggiunti helper per:
    - riconoscere il runtime `top3d`
    - leggere output del `Results` toolblock configurati da `Config.xml`
    - salvare immagine 2D renderizzata
    - salvare immagine 3D/range
    - esportare una nuvola punti `CSV` da collezioni enumerabili `X/Y/Z`
- `Cls_Config/Calss_structure/ConfigClassStructure.cs`
  - aggiunte nuove chiavi opzionali di configurazione per il contratto VisionPro ↔ pannello:
    - `Top3DRerenderResultOutput`
    - `Top3DPointCloudOutput`
    - `SaveTop3DRendered2DImage`
    - `SaveTop3DRangeImage`
    - `SaveTop3DPointCloudCsv`

**Motivo:**
Per la macchina `3DCheck` con sensore Cognex `L38-300`, il pannello doveva maturare dal solo salvataggio immagine top standard a una raccolta evidenze piu' adatta al collaudo e all'analisi:
- 2D render leggibile dall'operatore
- immagine/range 3D
- nuvola punti esportabile

La richiesta era integrare tutto senza rompere la baseline:
- stesso `Piece_xxxxxxxx`
- stessi path guidati da `Config.xml`
- stesso flusso percentuale di salvataggio gia' esistente

**Impatto:**
- Config.xml:
  - nuove chiavi opzionali, con default runtime se mancanti
- Database:
  - nessuna nuova tabella o colonna
- Runtime macchina:
  - nessun cambio al flusso di acquisizione o validazione
  - il pannello aggiunge solo sidecar file quando gli output `Top3D` sono davvero presenti
- Robustezza:
  - se un output manca o non ha il tipo atteso, il pezzo continua ad essere salvato normalmente
  - viene scritto un warning invece di interrompere la produzione

**Note operative:**
- il VPP `Top3D` deve esporre nel `Results` toolblock:
  - `Top3DRerenderResult` con tipo `Cog3DVisionDataRerenderResult` o `Cog3DVisionDataStitchResult`
  - `Top3DPointCloud` come collezione enumerabile di punti `X/Y/Z`
- i file salvati nel pezzo seguono i nomi:
  - `CH1_<piece>_T3D_2D_A.bmp`
  - `CH1_<piece>_T3D_3D_Range_A.bmp`
  - `CH1_<piece>_T3D_PointCloud.csv`

---

## [2026-04-17] Compatibility - Align VisionPro assembly metadata to installed Cognex baseline

**File modificati:**
- `QtisVisionPanel.csproj`
  - aggiornati i riferimenti `Cognex.VisionPro*` da `79.0.0.0` a `93.0.0.0`
- `Properties/AssemblyInfo.cs`
  - bump release applicativa per tracciare il riallineamento compatibilita'

**Motivo:**
Il progetto utilizzava gia' i path corretti verso:
- `C:\\Program Files\\Cognex\\VisionPro\\ReferencedAssemblies`
- `C:\\Program Files\\Cognex\\VisionPro\\bin`

ma dichiarava ancora metadata assembly molto piu' vecchie nel `.csproj`.

Il controllo incrociato effettuato oggi ha dato questo risultato:
- documentazione ufficiale Cognex: `VisionPro 9.25 SR1`, release date `2025-09-12`
- installazione locale: `Cognex.VisionPro*.dll` con assembly version `93.0.0.0`
- installazione locale: `Cognex.Vision.*.Net.dll` con assembly version `9.25.0.0`

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime macchina: nessuna variazione logica
- Build / riferimenti:
  - il progetto e' ora coerente con la baseline Cognex attualmente installata sulla macchina di sviluppo
  - chi compila su altre postazioni deve avere una installazione VisionPro coerente con la stessa baseline

---

## [2026-04-15] Fix — CognexManager_JobStopped spurious stop on Job Editor → Recipe Manager navigation

**File modificati:**
- `MainWindow.xaml.cs`
  - `CognexManager_JobStopped`: aggiunto guard iniziale che restituisce early quando:
    `shouldBeRunning == true && IsVisionAutoRecoverySuppressed() == true`
    Questo impedisce che eventi `StoppedSingle` ritardati (residui di un run singolo del
    Job Tool Editor, ~500 ms dopo che il run continuo è già ripartito) facciano
    scattare il flip di stato a Stopped.
  - Rimosso il check ridondante `if (!_isContinuousRunActive)` attorno al
    `Dispatcher.BeginInvoke` (la variabile è appena impostata a `false` sulla riga sopra;
    la guardia era sempre vera).

**Motivo:**
Al cambio navigazione JobToolEditor → RecipeManager, `ExitEditorPauseAsync` riavviava il
run continuo (`ManageJobStateAsync(true)` → `StartContinuousRunAsync`), che a sua volta
chiama `ForceStopAllJobs()` dentro `RunContinuous`. Ciò provocava l'evento Cognex
`StoppedSingle` (residuo del preview run dell'editor). L'handler `CognexManager_JobStopped`
riceveva l'evento ~510 ms dopo l'avvio e, senza la guard, eseguiva incondizionatamente:
- `UpdateContinuousRunState(false)` — portava `IsContinuousRunActive` a false
- `_isContinuousRunActive = false`
- Dispatcher update: `IsRunning = false`
causando la transizione `MACHINE_STATUS_CHANGED → Stopped` immediatamente dopo `Running`.
La finestra di soppressione di 4 s (`IsVisionAutoRecoverySuppressed`) proteggeva già il
percorso di auto-recovery ma non il flip di stato — ora protegge entrambi.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna
- Comportamento runtime:
  - Stop spurio immediato dopo riavvio VisionPro durante navigazione JTE → RM eliminato
  - Log `VISIONPRO_JOB_STOPPED` / `MACHINE_STATUS_CHANGED Stopped` non compaiono più
    per eventi StoppedSingle dentro la finestra di transizione con run ancora richiesto
  - Stop legittimi (fuori dalla finestra di soppressione o quando il run non è richiesto)
    rimangono gestiti normalmente

---

## [2026-04-15] Fix — LoadingProgressValue binding exception + VisionPro status false-stop warning

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs`
  - `LoadingProgressValue`: `private set` → `set`
    WPF's `ProgressBar.Value` ha `BindsTwoWayByDefault = true`. Il motore di binding chiama
    `CheckReadOnly` sulla sorgente anche quando il XAML dichiara `Mode=OneWay`. Con `private set`,
    WPF considera la property read-only e lancia `InvalidOperationException`.
    Rendendo il setter pubblico WPF non lancia l'eccezione; la logica interna è invariata.
- `Services/MachineRuntimeService.cs`
  - `StartContinuousRunAsync`: sostituito `SyncStateFromManager()` con lettura diretta di
    `CognexManager.IsRunningContinuously` + `UpdateContinuousRunState(isNowRunning)`.
    `SyncStateFromManager` ha una guard che restituisce senza fare nulla quando
    `IsContinuousRunCommandInProgress == true` — ma è chiamato dall'interno della stessa
    sequenza di start, mentre il flag è ancora alzato. Risultato: `IsContinuousRunActive`
    restava `false` dopo un avvio riuscito → `RECIPE_SAVE_AUTORESTART_NOT_RUNNING`
    veniva loggato come falso positivo.

**Motivo:**
- Binding exception: `ProgressBar.Value` + `private set` = crash al cambio DataContext.
- False-stop warning: `SyncStateFromManager` era un no-op nel contesto in cui veniva chiamato,
  lasciando lo stato stale.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `LoadingProgressValue.set` promosso da `private` a `public`
- Comportamento runtime:
  - L'eccezione nel Job Tool Editor non viene più lanciata al cambio DataContext
  - Dopo un avvio VisionPro riuscito, `IsContinuousRunActive` è aggiornato correttamente;
    `RECIPE_SAVE_AUTORESTART_NOT_RUNNING` non viene più loggato come falso positivo

---

## [2026-04-15] Audit payload refinement - Structured metadata for Job Tool Editor hold release

**File modificati:**
- `ViewModels/MainViewModel.cs`
  - i log `JOBEDITOR_HOLD_AUTO_RELEASED_ON_NAVIGATION` e `JOBEDITOR_HOLD_AUTO_RELEASED_ON_START` ora allegano metadata strutturati nel payload JSON
  - aggiunti campi diagnostici su hold attivi prima/dopo, vista target e modalita' di rilascio

**Motivo:**
I log espliciti introdotti nel passaggio precedente erano gia' utili, ma per analisi DB e troubleshooting evoluto
restavano ancora troppo dipendenti dal testo libero.

Con metadata strutturati e' ora possibile capire subito:
- verso quale vista stava navigando l'operatore
- se il resume del runtime era atteso
- se il rilascio e' stato difensivo
- quali hold erano attivi prima e dopo il cleanup

**Impatto:**
- Config.xml: nessuno
- Database: nessuna modifica schema
- Audit / diagnostica:
  - maggiore leggibilita' del payload JSON
  - maggiore filtrabilita' lato DB / tooling / analisi AI

---

## [2026-04-15] Auditability - Explicit logs for automatic Job Tool Editor hold release

**File modificati:**
- `ViewModels/MainViewModel.cs`
  - aggiunto evento audit `JOBEDITOR_HOLD_AUTO_RELEASED_ON_NAVIGATION`
  - aggiunto evento audit `JOBEDITOR_HOLD_AUTO_RELEASED_ON_START`
  - introdotto helper locale per verificare se il hold `job-editor` e' ancora attivo

**Motivo:**
Anche dopo il fix runtime del rilascio hold, dall'esterno non era immediato capire se:
- il fermo protetto dell'editor era stato davvero liberato
- il `Start` manuale aveva sbloccato un hold editor stantio

L'operatore e il manutentore dovevano ancora dedurre il comportamento osservando solo
gli stati macchina o il mancato blocco successivo.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime / audit:
  - il rilascio automatico del hold `job-editor` e' ora visibile esplicitamente nel log eventi
  - il troubleshooting del flusso `Job Tool Editor -> uscita pagina -> Start` diventa piu' rapido

---

## [2026-04-15] Hotfix - Defensive release of stale Job Tool Editor runtime holds

**File modificati:**
- `ViewModels/JobToolEditorViewModel.cs`
  - `ExitEditorPauseAsync(...)` ora prova sempre a rilasciare il hold globale `job-editor`
  - il resume del runtime dopo uscita/salvataggio usa il percorso centrale `ManageJobStateAsync(true)`
  - aggiunta esposizione dello stato `ShouldResumeContinuousRunAfterEditor` per la logica runtime editor-aware
- `ViewModels/MainViewModel.cs`
  - la navigazione fuori da `JobToolEditor` rilascia anche in modo difensivo il hold `job-editor`
  - il comando `Start` rilascia il hold `job-editor` quando non siamo piu' dentro l'editor

**Motivo:**
In alcuni casi il fermo protetto dell'editor VisionPro restava attivo anche dopo:
- uscita dalla pagina `Job Tool Editor`
- navigazione verso `Channel1` o `RecipeManager`
- tentativi manuali di `Start`

Il risultato era un runtime apparentemente fermo senza motivo visibile per l'operatore,
con log del tipo:
- `Start request blocked by active hold(s): job-editor`

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime:
  - il hold `job-editor` non deve piu' sopravvivere all'uscita pagina
  - il `Start` manuale fuori dall'editor non deve piu' essere bloccato da un hold editor stantio

---

## [2026-04-15] UI/runtime observability - Top bar indicator for active runtime holds

**File modificati:**
- `ViewModels/TopMenuBarViewModel.cs`
  - aggiunto refresh periodico dello stato `ContinuousRunHoldSummary`
  - introdotte proprieta' UI per presenza hold, testo compatto, tooltip e colore stato
  - traduzione dei motivi runtime (`manual-stop`, `recipe-save`, `job-editor`, `shutdown`) verso label leggibili da operatore
- `Views/UserControls/TopMenuBar.xaml`
  - aggiunta pill diagnostica nella card stato macchina
  - la pill e' visibile solo quando esiste almeno un hold runtime attivo
- `Views/UserControls/TopMenuBar.xaml.cs`
  - aggiunta localizzazione della label `Runtime hold`
- `ServerMessage/ServerMessageStructure.cs`
  - aggiunte nuove chiavi lingua per label, tooltip e motivi hold
- `scripts/UpdateRuntimeLanguageFiles.ps1`
  - allineato il generatore dei file lingua runtime con le nuove chiavi

**Motivo:**
Quando il runtime rimane volutamente fermo, l'operatore vedeva solo `Stopped` ma non il perche'.
Questo rendeva poco chiaro se il blocco fosse anomalo oppure protetto da logiche intenzionali come:
- stop manuale
- salvataggio ricetta
- editing dei tool VisionPro
- chiusura applicazione

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime:
  - il motivo del fermo protetto e' ora visibile direttamente in top bar
  - l'operatore puo' capire subito se `Start` e' bloccato da un hold ancora attivo

---

## [2026-04-15] Hotfix - Release Job Tool Editor hold during navigation

**File modificati:**
- `ViewModels/MainViewModel.cs`
  - aggiunto `PrepareCurrentViewForNavigationAsync(...)`
  - se la vista corrente e' `JobToolEditor`, la navigazione:
    - ferma eventuale live preview
    - rilascia esplicitamente il hold `job-editor`
    - consente il resume del continuous run prima di aprire la vista successiva

**Motivo:**
Il solo `Unloaded` della view non era abbastanza deterministico per una HMI industriale.
In alcuni casi il hold `job-editor` restava attivo anche dopo l'uscita dal `Job Tool Editor`,
bloccando poi:
- il ritorno in `RunContinuous`
- la navigazione verso `RecipeManager`
- il comando manuale `Start`, che risultava correttamente respinto dal runtime

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime:
  - il cambio vista lascia il `Job Tool Editor` in modo pulito
  - il fermo protetto per editing non deve piu' contaminare le viste successive

---

## [2026-04-15] Hotfix - Recipe save hold release and verified start behavior

**File modificati:**
- `ViewModels/MainViewModel.cs`
  - il comando `StartVision` rilascia in modo difensivo anche il hold `recipe-save`
  - `StartVision - Completed` viene scritto solo se VisionPro risulta davvero tornato in continuous run
  - aggiunto log operativo dedicato quando la richiesta di start termina ma il runtime resta fermo
- `ViewModels/RecipeManagerViewModel.cs`
  - il riavvio automatico dopo `SaveRecipeAsync` usa ora `ManageJobStateAsync(true)`
  - il refresh UI dopo il save usa lo stato runtime reale
  - aggiunto warning esplicito se il restart post-save non riporta il sistema in continuous run

**Motivo:**
Durante il salvataggio di una ricetta in produzione il sistema poteva fermare VisionPro correttamente
ma non rientrare piu' in `RunContinuous`, oppure accettare un nuovo comando `Start` lasciando comunque
la macchina ferma e loggando ugualmente `StartVision - Completed`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime:
  - il hold `recipe-save` non deve piu' bloccare un riavvio manuale post-save
  - il restart post-save usa lo stesso percorso centrale dello start operatore
  - i log riflettono meglio i casi in cui la ripartenza richiesta non diventa realmente attiva

---

## [2026-04-15] Hotfix - Progress binding and VisionPro status reporting

**File modificati:**
- `Views/JobToolEditorView.xaml`
  - il `ProgressBar` dell'overlay ora usa binding `Mode=OneWay` per `Value` e `IsIndeterminate`
- `Services/ApplicationEventLogger.cs`
  - la risoluzione di `visionpro_status` privilegia lo stato runtime reale di `MachineRuntimeService`
  - il fallback sulla cache interna del logger resta solo come seconda scelta

**Motivo:**
- il `Job Tool Editor` generava una `InvalidOperationException` WPF perche' il `ProgressBar`
  cercava una semantica `TwoWay` verso proprieta' di sola lettura/lato viewmodel
- nei payload audit/eventi il campo `visionpro_status` poteva restare `Stopped` anche quando
  VisionPro stava gia' lavorando, perche' il logger riusava prima il vecchio cache interno

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime:
  - eliminato il crash/binding error del feedback overlay nel `Job Tool Editor`
  - i log operativi e diagnostici riflettono meglio lo stato VisionPro reale

---

## [2026-04-14] Operator feedback layer for tool editor and recipe operations

**File modificati:**
- `OperationProgressWindow.xaml` - NUOVA finestra popup di avanzamento per operazioni lunghe lato operatore
- `OperationProgressWindow.xaml.cs` - logica di aggiornamento stato/fase/progresso del popup
- `Services/DialogService.cs` - aggiunto factory `CreateOperationProgressWindow()`
- `ViewModels/JobToolEditorViewModel.cs`
  - aggiunte proprietà di stato e progresso per l'overlay locale del tool editor
  - caricamento tool ora espone fasi leggibili (`prepare`, `stop runtime`, `read tool`, `open editor`)
  - salvataggio tool ora usa il popup operativo con avanzamento
- `Views/JobToolEditorView.xaml`
  - overlay di caricamento ridisegnato in stile piu' industriale
  - aggiunti messaggio principale, dettaglio e barra progresso
- `Views/JobToolEditorView.xaml.cs`
  - rimossa assegnazione diretta del testo overlay per non rompere il binding runtime
- `ViewModels/RecipeManagerViewModel.cs`
  - `LoadSelectedRecipeAsync()` ora mostra popup con avanzamento
  - `SaveRecipeAsync()` ora mostra popup con avanzamento
- `MainWindow.xaml.cs`
  - `InitializeComponentforChangeRecipe(...)` ora puo' aggiornare il popup con le fasi di runtime reload
- `ServerMessage/ServerMessageStructure.cs`
- `scripts/UpdateRuntimeLanguageFiles.ps1`

**Motivo:**
Nel `Job Tool Editor` e nelle operazioni ricetta l'operatore vedeva spesso schermate ferme o aree
vuote senza capire se il software stesse lavorando, fosse bloccato o avesse fallito.
Per una HMI industriale questo crea incertezza operativa e aumenta il rischio di click ripetuti
o interventi non necessari.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Runtime: nessuna modifica al contratto macchina/ricetta; cambia solo il livello di feedback operatore
- UX operatore: le azioni lunghe ora mostrano stato, fase attuale e avanzamento visibile

---

## [2026-04-14] Hotfix - Fix StackOverflow in CameraDisplayManager bootstrap

**File modificati:**
- `MainWindow.xaml.cs`
  - corretto `EnsureCameraDisplayManagerInitialized()`
  - rimossa la chiamata ricorsiva infinita
  - ripristinata la costruzione reale di `CameraDisplayManager`

**Motivo:**
Il bootstrap lazy introdotto per proteggere l'avvio da `NullReferenceException` conteneva una
regressione: invece di creare il manager, il metodo richiamava se stesso, causando
`System.StackOverflowException` all'avvio.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Comportamento runtime: nessuna modifica funzionale; viene corretto solo il percorso di inizializzazione
- Effetto operativo: l'applicazione non deve piu' fermarsi con stack overflow durante il bootstrap del display manager

---

## [2026-04-14] Fase 3 Step 1.1 - Hardened bootstrap for CameraDisplayManager

**File modificati:**
- `MainWindow.xaml.cs`
  - aggiunto `EnsureCameraDisplayManagerInitialized()`
  - tutti i thin delegator (`ResolveCameraRole`, `GetDisplayForRole`, `GetLastRunViewForRole`,
    `RefreshCameraFeatureVisibility`, `GetCurrentTopCameraImage`,
    `UpdateCameraStatusIndicators`, `ProcessToolResultsAsyncTop/side/Front`,
    `UpdateDisplayParallelAsync`) ora passano dalla lazy initialization
  - `Window_Loaded` non costruisce piu' il manager in modo esclusivo: richiama lo stesso bootstrap condiviso

**Motivo:**
Dopo l'estrazione della logica display in `CameraDisplayManager`, alcuni percorsi di startup
(`InitializeRecipeAsync`, `InitializeVisionSystem`) potevano chiamare
`RefreshRuntimeInspectionFeatures()` prima che `Window_Loaded` avesse inizializzato il manager.
Questo causava una `NullReferenceException` in `BuildRuntimeSupportedFeaturesByRole()`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- Comportamento runtime: nessun cambio funzionale lato macchina; il bootstrap e' solo piu' robusto
- Effetto operativo: eliminato il crash di avvio causato dall'ordine di inizializzazione tra runtime visione e helper UI

---

## [2026-04-14] Fase 3 Step 1 — Estrazione CameraDisplayManager da MainWindow

**File modificati:**
- `Services/CameraDisplayManager.cs` — NUOVO FILE. Contiene la logica di display display
  delle telecamere estratta da MainWindow: `ResolveCameraRole`, `GetDisplayForRole`,
  `GetLastRunViewForRole`, `RefreshCameraFeatureVisibility`, `UpdateCameraStatusIndicators`,
  `GetCurrentTopCameraImage`, `UpdateDisplayParallelAsync`,
  `ProcessToolResultsAsyncTop/side/Front`.
- `MainWindow.xaml.cs`
  - Aggiunto campo `private CameraDisplayManager _cameraDisplayManager`
  - Aggiunta inizializzazione in `Window_Loaded`
  - `private ICogRecord _topRecord` → `public static ICogRecord _topRecord` (accessibilità richiesta da CameraDisplayManager)
  - 9 metodi sostituiti con thin delegator a `_cameraDisplayManager` (da ~170 righe a 9 righe)
- `QtisVisionPanel.csproj` — aggiunto `<Compile Include="Services\CameraDisplayManager.cs" />`

**Motivo:**
MainWindow.xaml.cs aveva ~170 righe di logica display telecamere mista al codice di
orchestrazione. L'estrazione in `CameraDisplayManager` isola la responsabilità "aggiornare
i display Cognex e gli indicatori di stato UI" in una classe testabile con dipendenze esplicite.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `MainWindow._topRecord` promosso da `private` a `public static` per accesso da CameraDisplayManager
- Comportamento runtime: nessuno — stessa logica, stessi percorsi di esecuzione

---

## [2026-04-14] Fase 4 — AuthorizationService singleton nel ServiceLocator

**File modificati:**
- `ServiceLocator.cs`
  - Aggiunto campo `private static AuthorizationService _authorizationService`
  - Aggiunta property `public static AuthorizationService Authorization` con double-check locking
  - Aggiunto warm-up in `Initialize()`: `var authorization = Authorization`
  - Aggiunto `_authorizationService = null` in `Reset()`
- `ViewModels/AuthorizedViewModel.cs` — `new AuthorizationService()` → `ServiceLocator.Authorization`
- `ViewModels/MainViewModel.cs` — stesso
- `ViewModels/NavigationViewModel.cs` — stesso
- `ViewModels/RecipeManagerViewModel.cs` — stesso
- `Services/RecipeAuthorizationService.cs` — stesso

**Motivo:**
`AuthorizationService` veniva istanziata 5 volte (in 5 classi diverse), ognuna con la propria
`_roleFeaturesCache`, `_lastCacheUpdate` e `SemaphoreSlim`. Questo causava incoerenza della
cache tra ViewModel (uno poteva avere permessi freschi, un altro stale), e 5 connessioni MySQL
indipendenti per gli stessi dati. Con il singleton, la cache è condivisa e `LoadAllPermissionsAsync`
è protetta da un solo lock — il refresh avviene una volta per tutti i consumer.

**Impatto:**
- Config.xml: nessuno
- Database: riduzione connessioni MySQL (da N per cambio ruolo a 1)
- API pubblica: nessuna — i consumer usano lo stesso campo `_authService`, solo il valore cambia
- Comportamento runtime: cache permessi ora coerente tra tutti i ViewModel; `UserSession.OnRoleChanged` aggiorna tutti i consumer simultaneamente con la stessa istanza

---

## [2026-04-13] M2/M4 — Rimossi displayCounter, _globalCounterService, GlobalCounterAdapter

**File modificati:**
- `MainWindow.xaml.cs`
  - Rimosso campo `public static displayCounter _displayCounter`
  - Rimosso campo `private GlobalCounterService _globalCounterService` (mai inizializzato)
  - Rimossi 2 blocchi di creazione `new displayCounter(counterUpdater)` (righe ~484 e ~1802)
  - Rimosso commento `// await _displayCounter.DisplayCountersAsync()` (era già commentato)
  - Rimosso metodo `GetGlobalCounterService()` (restituiva sempre null)
  - Semplificato `ReconnectToDatabaseAsync()`: rimossa branch `_globalCounterService != null` (era sempre false)
  - Rimosso blocco dispose `_displayCounter` nel cleanup
- `Cls_Vpro/ICounterUpdater.cs` — rimossa chiamata `MainWindow._displayCounter.DisplayCountersAsync()` da `ResetAllCountersAsync()`
- `Cls_Vpro/displayCounter.cs` — svuotato: `UpdateCounterUI()` era 100% commentata, `StartAutoRefresh()` mai chiamato
- `Cls_Vpro/GlobalCounterAdapter.cs` — svuotato: mai istanziata in nessun consumer, causava doppia scrittura GlobalCounterService

**Motivo:**
`displayCounter` aveva tutta la logica UI commentata e il timer da 100ms non veniva mai avviato — effetto netto zero ma costo di creazione oggetti ad ogni cambio ricetta.
`_globalCounterService` in MainWindow era dichiarato ma mai inizializzato → sempre null → `GetGlobalCounterService()` e `ReconnectToDatabaseAsync()` erano no-op.
`GlobalCounterAdapter` non era mai istanziato → dead code puro.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno — `GlobalCounterService` rimane attiva internamente a `CounterUpdater` (percorso live invariato)
- API pubblica: `GetGlobalCounterService()` rimosso da MainWindow; `ReconnectToDatabaseAsync()` ora restituisce sempre false (era già sempre false)
- Comportamento runtime: nessuno — i contatori continuano a scrivere su JSON e MySQL via `CounterUpdater._globalCounterService`

---

## [2026-04-13] M1 — RelayCommand consolidata in Models/

**File modificati:**
- `Models/RelayCommand.cs` — aggiunto overload `Action<object>` e metodo `public void RaiseCanExecuteChanged()`
- `ViewModels/MainViewModel.cs` — rimossa classe inline `RelayCommand` (era l'unica con `Action<object>`); rimosso using `System.Windows.Controls` diventato inutilizzato
- `ViewModels/NavigationViewModel.cs` — rimossa classe inline `RelayCommand<T>`
- `ViewModels/ControlButtonsViewModel.cs` — aggiunto `using QtisVisionPanel.Models;`
- `Inspector/ViewModels/RelayCommand.cs` — svuotato (classi `RelayCommand` e `RelayCommand<T>` erano duplicate da `Models/`)
- `Inspector/ViewModels/DataInspectorViewModel.cs` — aggiunto `using QtisVisionPanel.Models;`

**Motivo:**
`RelayCommand` era definita in almeno 3 posti con firme diverse:
`Models/RelayCommand.cs` (Action), `ViewModels/MainViewModel.cs` inline (Action<object> + internal RaiseCanExecuteChanged), `Inspector/ViewModels/RelayCommand.cs` (Action, sealed). La versione di `Models/` è ora canonica e copre entrambe le firme. `PreferenceViewModel` usa `RaiseCanExecuteChanged()` via cast — ora il metodo è `public` e nella classe giusta.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: `RelayCommand.RaiseCanExecuteChanged()` promosso da `internal` a `public`; aggiunto overload costruttore `Action<object>`
- Comportamento runtime: nessuno — stessa logica, `CommandManager.InvalidateRequerySuggested()` invariato

---

## [2026-04-13] Fix errori compilazione — InspectionStatus non qualificato

**File modificati:**
- `ViewModels/InspectionConfigViewModel.cs`
  - Righe 354, 408, 463: `InspectionStatus.FromEjectionStatus(...)` → `RecipeParameters.InspectionStatus.FromEjectionStatus(...)`
  - `InspectionStatus` è una classe annidata dentro `RecipeParameters`; senza il qualificatore completo il compilatore non la trova nel namespace corrente.

**Motivo:**
Separazione in corso tra `InspectionStatus` (quali ispezioni sono attive) e `EjectionStatus`
(quali difetti causano l'espulsione fisica del pezzo). Le due classi sono semanticamente distinte:
si può ispezionare senza espellere, e un pezzo non ispezionato non può essere espulso.
Il bug di compilazione emergeva perché i tre call site usavano il nome breve `InspectionStatus`
invece del nome qualificato `RecipeParameters.InspectionStatus`.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna — solo correzione riferimento tipo
- Comportamento runtime: nessuno — logica invariata, solo fix compilazione

---

## [2026-04-13] Fase 1 — Fix critici C1, C2, A1

**File modificati:**

- `Services/MachineStatusService.cs`
  - Aggiunto guard `Dispatcher.BeginInvoke` all'inizio del setter `CurrentStatus`: qualunque chiamante (incluso `SetMaintenanceMode`) è ora sicuro da thread background. `StatusHistory.Add()` e `RemoveAt()` vengono sempre eseguiti sul UI thread.
  - Stesso guard aggiunto al setter `IsSystemInitializing`: `OnPropertyChanged` non viene mai invocato da un thread background.

- `Database/clsConnection.cs`
  - Rimossi 5 static field initializer (`sHost`, `sDatabase`, `sUser`, `sPassword`, `sPort`) che leggevano `MainWindow.configManager` al momento del caricamento del tipo .NET. Erano dead code (nessun consumer nel codebase) e causavano `NullReferenceException` al type-load se il config non era ancora caricato.

- `ViewModels/MainViewModel.cs`
  - Aggiunto `try/catch` attorno al corpo di `OnUserChanged` — era l'unico handler `async void` senza protezione. Un'eccezione non catturata in un handler `async void` viene propagata al `SynchronizationContext` e può terminare il processo senza log.

**Motivo:** Fase 1 del piano di refactoring architetturale. Fix critici senza rischio di regressione sulle funzionalità esistenti.

**Impatto:**
- Config.xml: nessuno
- Database: nessuno
- API pubblica: nessuna — i setter rimangono `public` con la stessa firma
- Comportamento runtime: `StatusHistory` non causa più crash se `CurrentStatus` viene aggiornato da thread non-UI (timer, `SetMaintenanceMode`). Il type-load di `clsConnection` non crasha se il config non è ancora disponibile.

---

Questo documento traccia tutte le modifiche al codice apportate durante le sessioni
di sviluppo assistito. Le voci sono in ordine cronologico inverso (la più recente in testa).

Il file viene aggiornato automaticamente a ogni sessione di modifica.

---

## [2026-04-13] Fix routing ispezione Top vs Top3D — basato su nome job VPP

**File modificati:**

- `DataManage/InspectionProcessor.cs`
  - Rimosso costruttore duplicato (con `GlobalCounterService`)
  - Aggiunto parametro `bool isTop3DInspection = false` a `ProcessInspectionAsync`
  - Guard `!hasSideInspection && !hasFrontInspection` ora usa `isTop3DInspection` invece di `isThreeDCheckMachine`
  - Routing validator: `isTop3DInspection ? GetDetailedTop3DValidationAsync : GetDetailedTopValidationAsync`

- `MainWindow.xaml.cs`
  - `ProcessInspectionPairAsync` riga ~1824: `isTop3DInspection` ora deriva da `topResult.CameraRole == "top3d"` invece di `IsThreeDCheckMachineType()`
  - `ProcessInspectionPairAsync` riga ~1874: aggiunto `isTop3DInspection` come 4° argomento nella chiamata a `ProcessInspectionAsync`
  - `ProcessQueuedPairAsync`: rimosso blocco condizionale `if (IsThreeDCheckMachineType())` — ora il top queue viene sempre drenato se non c'è camera secondaria, con log che indica se il job è Top o Top3D

**Motivo:**
Il campo `MachineType` in `Config.xml` era usato come chiave di routing al posto
del nome reale del job nel file VPP. Una macchina con `MachineType = 3DCheck` ma
job chiamato `Top` eseguiva la validazione 3D (che non trovava output e passava vuota).
Una macchina standard senza camera secondaria non processava mai le ispezioni top.

**Impatto:**
- Config.xml: nessuno — `MachineType` rimane in config ma non influenza più il routing ispettivo
- Database: nessuno
- API pubblica: firma di `InspectionProcessor.ProcessInspectionAsync` cambiata (parametro aggiunto con default = false — backward compatible)
- Comportamento runtime: le ispezioni Top standard ora vengono sempre processate; le ispezioni Top3D vengono processate solo se il job nel VPP si chiama `Top3D` (normalizzato)

---

## [2026-04-13] Analisi architetturale completa

**Attività:** Review tecnica completa del progetto come Senior Software Architect.

Documenti prodotti in questa sessione:
- `Documentation/MachineHardware/architecture-and-modules.md` — documento di architettura live

Issue critiche identificate (da risolvere in sessioni successive):

| Priorità | Issue | File |
|---|---|---|
| CRITICO | `StatusHistory.Add()` su ObservableCollection da Timer background thread | `Services/MachineStatusService.cs` |
| CRITICO | `clsConnection` static fields inizializzati prima che config sia caricata | `Database/clsConnection.cs` |
| CRITICO | `_isContinuousRunActive` duplicato: `MainWindow.static` + `MachineRuntimeService.property` | Entrambi |
| ALTO | `async void` in command handlers (eccezioni silenziate) | `ViewModels/MainViewModel.cs` |
| ALTO | `ProcessorAffinity = 0x0003` hardcoded — solo 2 core | `MainWindow.xaml.cs` |
| MEDIO | `RelayCommand` definita in 3 posti | `Models/`, `Inspector/`, inline `MainViewModel` |
| MEDIO | `AlarmCardRepository` duplicata | `Database/` e `IntegratedAlarmCardService.cs` |

---

## [2026-04-24] PowerFlex ping guard e dashboard analisi dati interno

**File principali:**

- `Services/PowerFlex525EtherNetIpClient.cs`
- `Services/PowerFlex525Service.cs`
- `ViewModels/PowerFlex525ViewModel.cs`
- `ViewModels/DataAnalysisViewModel.cs`
- `Views/UserControls/DataAnalysisView.xaml`
- `Database/ProductionAnalyticsRepository.cs`
- `Cls_Config/Calss_structure/ConfigClassStructure.cs`

**Aggiornamenti principali:**

- PowerFlex 525:
  - prima di ogni lettura parametro viene eseguito un `ping` verso l'inverter
  - se il dispositivo non risponde, la vista segnala la non disponibilita e non avvia la lettura CIP
  - la velocita nastro usa solo il fattore `K` `MetersPerMinutePerHz`
- Data Analysis:
  - nuova vista WPF interna disponibile da `Overview -> Data Analysis`
  - grafico barre `Total / Good / NoGood`
  - grafico difetti filtrato sulle ispezioni abilitate nella ricetta selezionata
  - trend `Min / Avg / Max` per altezza, larghezza e lunghezza quando la misura e disponibile
  - layout card e colori barre modificabili solo da `Administrator`

**Impatto baseline:**

- `Config.xml`
  - introdotta e usata la sezione `AnalyticsDashboard`
  - per PowerFlex il calcolo `m/min` usa solo `MetersPerMinutePerHz`
- database
  - nessuna migrazione richiesta
- runtime
  - la pagina analytics lavora in asincrono con refresh lento per non interferire con l'ispezione

---

## [2026-04-24] Fix semantica analytics e blocco auto-logout demo

**File principali:**

- `Database/ProductionAnalyticsRepository.cs`
- `Services/OperatorInactivityService.cs`

**Correzioni:**

- `Production overview`
  - usa `Esito_Classificazione`
  - `1 = Good`
  - `0 = NoGood`
- `Defect distribution`
  - i flag `NC_*` vengono ora interpretati correttamente:
    - `0 = difetto rilevato`
    - `1 = controllo OK`
    - `4 = controllo non eseguito`
- `AutoLoginAdministratorForDemo`
  - quando il flag e `true`, l'auto-logout per inattivita non viene eseguito

---

## [2026-04-24] Compatibilita colonne misura analytics e card 3D dedicate

**File principali:**

- `ViewModels/DataAnalysisViewModel.cs`
- `Database/ProductionAnalyticsRepository.cs`
- `Models/DataAnalysisModels.cs`
- `Cls_Config/Calss_structure/ConfigClassStructure.cs`

**Aggiornamenti:**

- la vista analytics ora risolve le colonne misura reali presenti in `tblgenerale`
- `Height trend` usa alias compatibili:
  - `HeightMeasureValue`
  - `HeigthMeasureValue`
- aggiunta card separata `3D height trend`
- i trend vengono mostrati solo se:
  - la relativa ispezione e abilitata in ricetta
  - la colonna misura esiste davvero nel database macchina

---

## [2026-04-24] Fix refresh UI trend analytics e fallback ricetta effettiva

**File principali:**

- `Models/DataAnalysisModels.cs`
- `ViewModels/DataAnalysisViewModel.cs`

**Correzioni:**

- le card trend ora notificano correttamente:
  - `SummaryText`
  - `AveragePolylinePoints`
  - `MinimumPolylinePoints`
  - `MaximumPolylinePoints`
  - `Subtitle`
  - `ValueColumn`
- il dashboard analytics usa una ricetta effettiva di fallback:
  - ricetta selezionata
  - altrimenti `Configuration.LastRecipe`
  - altrimenti la prima ricetta disponibile nel range

**Effetto runtime:**

- i trend non restano piu bloccati sul valore iniziale `N/D`
- difetti e misure lavorano sulla ricetta corretta anche quando il filtro non viene scelto manualmente

---

## [2026-04-24] Fix matching ricetta analytics, difetti abilitati-only e apertura pagina meno bloccante

**File principali:**

- `Database/ProductionAnalyticsRepository.cs`
- `ViewModels/DataAnalysisViewModel.cs`

**Correzioni:**

- le query analytics confrontano ora la ricetta con varianti compatibili:
  - nome esatto
  - `trim`
  - nome senza estensione
  - nome con `.xml`
- il filtro difetti mostra solo le ispezioni abilitate nella ricetta effettivamente risolta
- se la ricetta analytics non viene trovata su disco:
  - fallback alla ricetta runtime corrente gia caricata
- l'inizializzazione pagina cede subito il rendering alla UI prima del primo refresh dati

---

## [2026-04-24] Fix sintassi MySQL query trend analytics

**File principali:**

- `Database/ProductionAnalyticsRepository.cs`

**Correzioni:**

- la query trend usa ora una costruzione SQL compatibile con il server MySQL macchina
- il bucket temporale e stato riscritto con `TIMESTAMP + MAKETIME`
- la colonna misura e racchiusa in backtick
- gli alias aggregati sono stati resi espliciti:
  - `AvgMeasureValue`
  - `MinMeasureValue`
  - `MaxMeasureValue`
## [2026-09-29] RIGHT e REAR indipendenti (3.1.5.8)

- VPP TOP/LEFT/RIGHT/REAR: ruoli, code, ticket, risultato, display e matrice delle ispezioni separati.
- Ricette: RIGHT e REAR hanno trigger, offset, delay e soglie distinti; la ricetta solo-REAR mantiene il fallback storico. Nuovo punto e output RIGHT senza canale assegnato e disabilitato.
- MultiShot: profilo REAR aggiuntivo, profilo RIGHT storico preservato finche' non e' verificato il mapping, blocco di profili con ruolo/uscita in collisione.
- ONNX e DB: etichette e immagini per camera, tre colonne MySQL nullable RIGHT. Verifiche e passaggi di commissioning in `four-camera-independent-roles-2026-09-29.md`.

## [2026-10-01] Trigger camere coerenti con i job VPP (3.1.6.1)

- `MainWindow`: dopo il caricamento VPP attende il provisioning I/O, registra i ruoli dei job e aggiorna il runtime; al cambio ricetta azzera i ruoli attivi prima di rilasciare il vecchio VPP.
- `ActiveVppCameraTriggerGate` e `DigitalIOViewModel`: mascherano nell'istanza runtime punti trigger e MultiShot per camere assenti o con ruoli contraddittori; ricontrollano il ruolo prima degli impulsi ordinari e temporizzati, anche dopo l'attesa del canale. RIGHT e REAR sono distinti; SIDE usa il ruolo fisico LEFT.
- Nessuna modifica persistente a mapping macchina, ricette, `Config.xml` o DB. La build Release x64 e i controlli in memoria TOP/SIDE/REAR, TOP/LEFT/RIGHT/REAR e nessuna camera passano. Verificare sul banco VPP e DO reali; canali condivisi tra camere richiedono correzione del cablaggio/mapping.

## [2026-09-29] Setup macchina: mappatura fisica I/O in primo piano (3.1.5.9)

- `DigitalIOControl.xaml`: due tabelle compatte DI/DO con scheda, canale, polarita' e flag fisico editabili. Usano le stesse collezioni del setup completo e il comando Salva configurazione esistente.
- Tabella avanzata dei punti e Configurazione macchina avanzata inizialmente chiuse. Rimossi quattro riquadri encoder duplicati nella pagina Setup; restano nella panoramica. Nessuna variazione al runtime, alla serializzazione XML, alle ricette o al DB.
- `messages_ita.json` e `messages_eng.json`: guida breve al percorso di commissioning. Verifica: build Release x64, parsing XAML e JSON; controllo a video e I/O sulla macchina ancora necessario.

## [2026-09-29] Trigger ricetta per vista fisica (3.1.6.0)

- `RecipeManagerViewModel`: riconoscimento camere anche dal profilo ispezioni esplicito della ricetta selezionata; refresh dei ruoli dopo il caricamento VPP. SIDE mantiene ruolo fisico LEFT ma mostra il nome del job nel trigger.
- `RecipeManagerView`: RIGHT e REAR sono righe distinte sempre visibili; il controllo resta disabilitato se il ruolo non e' riconosciuto. Il vecchio profilo MultiShot `Right` mostra il suo `CameraRole` effettivo, cosi' un profilo assegnato a REAR non appare come RIGHT.
- Etichetta del profilo storico aggiornata nei cataloghi lingua e nella generazione delle stringhe runtime.
- Nessuna migrazione XML, `Config.xml` o DB. Verificare a video con VPP TOP/SIDE/REAR e TOP/LEFT/RIGHT/REAR, poi provare gli impulsi su canali DO distinti.
