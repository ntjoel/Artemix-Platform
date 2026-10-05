# Guida Di Test Nuove Implementazioni

Data: 2026-07-10

Versione di riferimento: 3.0.7.2

Questa guida serve per collaudare le implementazioni recenti senza confondere le
funzioni advisory con il ciclo macchina reale.

## Scopo

Validare che le nuove funzioni siano presenti, salvino correttamente la configurazione
e non cambino il comportamento produttivo quando sono disabilitate.

Le aree coperte sono:

- Fase 0 stabilita' 24/7: code bounded, async command wrapper, riduzione blocchi UI.
- AI advisory: fondazione dati, SPC, manutenzione predittiva, training dataset,
  classificatore ONNX shadow-mode, performance monitor e advisor ricetta.
- Controlli AI in PC Diagnostics: localizzazione, validazione parametri e salvataggio.
- Preallarmi salute macchina: UI, storico locale e pubblicazione OPC UA advisory.
- Top3D Detection Sensitivity: lettura/applicazione per ricetta e riduzione rumore log.
- Regressione obbligatoria: ricette, RunContinuous, trigger, scarto, contatori e log.

## Regole Di Sicurezza Durante Il Test

- Non abilitare AI, OPC UA remoto o timing optimizer durante produzione reale senza
  aver prima fatto il test in simulazione o a macchina vuota.
- Le funzioni AI devono restare advisory: non devono comandare scarto, start/stop,
  cambio ricetta o uscite fisiche.
- Se un test modifica `machine_runtime_config.xml`, creare prima un backup dalla pagina
  `Machine I/O Setup` oppure copiare manualmente il file.
- Per prove OPC UA, usare prima un server di test o un namespace dedicato.
- Se compare un errore su trigger camera, reject o contatori, fermare la prova: quelle
  parti sono runtime macchina, non diagnostica.

## Prerequisiti

| Elemento | Richiesto |
|---|---|
| Build | Release x64 compilata senza errori |
| Ruoli | Operator, Installer o Administrator disponibili |
| Config macchina | Backup recente di `machine_runtime_config.xml` |
| Ricette | Almeno una ricetta valida e una ricetta Top3D se si testa L38 |
| OPC UA | Server Ignition/PLC di test, se si validano i nodi `EarlyWarning*` |
| DB | MySQL attivo per test completi AI; test degradazione con DB spento opzionale |
| VisionPro | Job caricabile in QuickBuild e dalla HMI |

## Smoke Test Rapido

Eseguire questo blocco prima di qualunque test lungo.

| ID | Prova | Pass atteso |
|---|---|---|
| S01 | Avviare HMI in simulazione | Nessun crash, pagina principale caricata |
| S02 | Aprire PC Diagnostics | La pagina si apre senza binding error bloccanti |
| S03 | Aprire `Controlli AI` | Label leggibili, nessun testo mancante evidente |
| S04 | Premere `Ricarica` nella sezione AI | I valori tornano coerenti con il file macchina |
| S05 | Caricare ricetta esistente | Ricetta caricata, immagini e parametri visibili |
| S06 | Start/Stop RunContinuous | Stato macchina cambia correttamente |
| S07 | Chiudere HMI | Nessuna eccezione non gestita in shutdown |

Se uno di questi test fallisce, non procedere con test macchina.

## Test 1 - Build E Stato Repo

Comando:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' .\QtisVisionPanel.sln /p:Configuration=Release /p:Platform=x64 /m
```

Pass:

- `0 Error(s)`.
- Warning ammessi solo se gia' presenti nella baseline.
- Nessun nuovo warning critico su `OpcUaClientService`, `SystemDiagnosticsViewModel`,
  `MachineConfigurationService` o `SystemDiagnosticsView`.

## Test 2 - Localizzazione Controlli AI

Obiettivo: verificare che la nuova UI non usi testi hardcoded non traducibili.

Procedura:

1. Avviare HMI.
2. Aprire `PC Diagnostics`.
3. Andare alla sezione `Controlli AI`.
4. Verificare i testi:
   - titolo controlli AI;
   - pulsanti `Ricarica` e `Salva`;
   - checkbox servizi AI;
   - parametri ONNX;
   - retention;
   - sezione `Preallarmi salute macchina`;
   - testo `Nessun preallarme registrato`.
5. Se disponibile, cambiare lingua ENG/ITA e riaprire la pagina.

Pass:

- Tutte le label sono leggibili.
- Nessun testo appare vuoto.
- Nessun testo nuovo resta in una lingua diversa da quella selezionata, salvo dati tecnici
  come `ONNX`, `SPC`, `OPC UA`.

Log o file da controllare:

- `Localization/messages_eng.json`
- `Localization/messages_ita.json`
- file runtime lingua sotto `C:\QtisVision\Language`, se aggiornati dalla macchina.

## Test 3 - Permessi Salvataggio AI

Obiettivo: verificare che solo ruoli autorizzati possano salvare parametri AI.

| Ruolo | Prova | Pass atteso |
|---|---|---|
| Viewer/Operator | Modificare un valore AI e premere Salva | Area disabilitata oppure salvataggio non permesso |
| Installer | Modificare un valore non critico e salvare | Conferma richiesta, salvataggio eseguito |
| Administrator | Modificare un valore non critico e salvare | Conferma richiesta, salvataggio eseguito |

Valore consigliato per prova non invasiva:

- `AiPerformanceMonitorEnabled`: false -> true -> false.

Pass aggiuntivo:

- Evento audit/log `AI_SETTINGS_APPLY` presente dopo il salvataggio autorizzato.
- Dopo `Ricarica`, il valore resta quello salvato.

## Test 4 - Validazione Parametri AI Da UI

Obiettivo: impedire che valori fuori range vengano salvati dal pannello.

Eseguire con ruolo Installer/Administrator.

| Campo | Valore non valido | Pass atteso |
|---|---:|---|
| Digest preallarmi | 0 | Salvataggio bloccato con messaggio chiaro |
| Digest preallarmi | 1441 | Salvataggio bloccato |
| ETA critica pezzi | 0 | Salvataggio bloccato |
| ETA critica pezzi | 1000001 | Salvataggio bloccato |
| Disco critico ore | 0 | Salvataggio bloccato |
| Disco critico ore | 9000 | Salvataggio bloccato |
| Retention AI | -1 | Salvataggio bloccato |
| Retention AI | 3651 | Salvataggio bloccato |
| ONNX input width | 0 | Salvataggio bloccato se classificatore ONNX abilitato |
| ONNX input height | 0 | Salvataggio bloccato se classificatore ONNX abilitato |
| ONNX resize width/height | 1800x1500 | Salvataggio bloccato: non usare la risoluzione nativa camera |
| ONNX normalize std | 0 | Salvataggio bloccato se classificatore ONNX abilitato |
| ONNX confidenza minima NOK | -0.1 oppure 1.1 | Salvataggio bloccato; usare 0..1 |

Pass:

- Il file macchina non viene corrotto.
- La UI mostra un messaggio comprensibile.
- Dopo `Ricarica`, i valori validi precedenti sono ancora presenti.

## Test 5 - Clamp Config Lato File

Obiettivo: verificare che un file XML modificato a mano non lasci valori pericolosi.

Procedura:

1. Chiudere HMI.
2. Fare backup di `machine_runtime_config.xml`.
3. Inserire manualmente valori fuori range in `RuntimeBindings`, ad esempio:
   - `MachineHealthDigestIntervalMinutes=-10`
   - `MachineHealthDiskCriticalHours=0`
   - `AiInspectionMeasurementRetentionDays=99999`
   - `DefectClassifierInputWidth=99999`
4. Avviare HMI.
5. Aprire `PC Diagnostics -> Controlli AI`.
6. Premere `Ricarica`.

Pass:

- La HMI non crasha.
- I valori risultano normalizzati su range sicuro.
- Un successivo `Salva` scrive valori coerenti.

Range attesi:

| Parametro | Range |
|---|---|
| Digest preallarmi | 1..1440 minuti |
| ETA critica | 1..1000000 pezzi |
| Disco critico | 0.5..8760 ore |
| Retention AI | 0 oppure 1..3650 giorni |
| ONNX input resize | 16..1024 px |
| ONNX normalize std | > 0 |
| ONNX confidenza minima NOK | 0..1; 0 = filtro disabilitato |

## Test 6 - Preallarmi Salute Macchina

Obiettivo: verificare che i preallarmi siano visibili ma non bloccanti.

Procedura base:

1. In `Controlli AI`, abilitare:
   - `Notifiche salute macchina`;
   - `SPC / rilevamento deriva` oppure `Manutenzione predittiva`.
2. Salvare.
3. Fare girare una prova controllata o simulare condizioni diagnostiche se disponibili.
4. Aprire la card `Preallarmi salute macchina`.
5. Premere `Aggiorna`.

Pass:

- Se non ci sono derive, compare `Nessun preallarme registrato`.
- Se ci sono derive, la lista mostra severita', categoria, titolo, causa, ETA e timestamp.
- La macchina resta avviabile e fermabile normalmente.
- Gli eventi appaiono in Events Monitor come advisory, non come comando macchina.

Test dipendenza:

1. Disabilitare `SPC / rilevamento deriva`.
2. Disabilitare `Manutenzione predittiva`.
3. Lasciare attiva solo `Notifiche salute macchina`.

Pass:

- Nessun preallarme nuovo viene generato per mancanza di sorgenti dati.
- La UI mostra il messaggio che spiega la dipendenza.

## Test 7 - OPC UA Preallarmi EarlyWarning

Obiettivo: validare il canale MES/SCADA senza rendere la comunicazione un vincolo macchina.

Prerequisiti:

- OPC UA abilitato e connesso.
- Nodi `EarlyWarning*` mappati nel pannello OPC UA o nel DB config OPC UA.

Nodi da controllare:

- `EarlyWarningActive`
- `EarlyWarningSeverity`
- `EarlyWarningCode`
- `EarlyWarningMessage`
- `EarlyWarningEtaValue`
- `EarlyWarningEtaUnit`
- `EarlyWarningCount`
- `EarlyWarningTimestamp`
- `EarlyWarningSequence`

Procedura:

1. Collegare Ignition o altro client OPC UA.
2. Abilitare `MachineHealthNotificationsEnabled`.
3. Generare o attendere un preallarme.
4. Leggere i nodi dal client.

Pass:

- `EarlyWarningSequence` incrementa a ogni nuovo invio.
- `EarlyWarningMessage` resta leggibile e non supera la lunghezza massima prevista.
- Se il server e' lento/non raggiungibile, la HMI non si blocca.
- Log ammessi:
  - `OPCUA_EARLY_WARNING_WRITE_TIMEOUT|timeout_ms=5000`
  - `OPCUA_EARLY_WARNING_WRITE_SKIPPED_BUSY`
- RunContinuous, trigger e scarto restano funzionanti anche con OPC UA lento.

Test nodo mancante:

1. Disabilitare o rimuovere un nodo `EarlyWarning*` dalla config OPC UA di test.
2. Ripetere l'invio.

Pass:

- Il nodo mancante e' un no-op sicuro.
- Nessun crash e nessun blocco ciclo.

## Test 8 - ONNX Shadow-Mode

Obiettivo: verificare che il modello AI non influenzi esito, contatori o scarto.

Test modello non valido:

1. Abilitare `Classificatore ONNX`.
2. Inserire un percorso inesistente o file non `.onnx`.
3. Salvare.

Pass:

- Salvataggio bloccato con messaggio chiaro.
- Il ciclo macchina non cambia.

Test servizio disabilitato:

1. Disabilitare entrambi i profili classificatore TOP e SIDE e premere `Salva`.
2. Riavviare la HMI e far passare prodotti per almeno 5 minuti.
3. Controllare log, Task Manager e statistiche shadow.

Pass:

- compare solo lo stato iniziale `VISION_ML_INIT|...|enabled=false` per ciascun profilo;
- non compaiono analisi `VISION_ML_SHADOW`, timeout immagine o accessi ripetuti al modello;
- i contatori AI non aumentano e la cadenza ispezione resta quella VisionPro;
- una macchina senza file `.onnx` continua a caricare ricetta, avviare RunContinuous e scartare
  secondo le sole regole VisionPro.

Test modello valido:

1. Copiare un modello validato in un percorso stabile, ad esempio:
   `C:\QtisVision\AI\Models\defect_classifier_v1.onnx`.
2. Impostare percorso, versione modello, resize width/height e normalizzazione coerenti per il
   profilo TOP o SIDE in prova. Non usare la risoluzione nativa camera: ogni profilo ridimensiona
   l'immagine alla dimensione usata durante il training del proprio modello.
3. Salvare.
4. Far passare prodotti OK e NOK.
5. Aprire `Statistiche AI shadow-mode` e annotare contesto e numero campioni.
6. Cambiare solo la soglia minima NOK, premere `Salva`, quindi aggiornare le statistiche.

Pass:

- Log attesi:
  - `VISION_ML_INIT|loaded`
  - `VISION_ML_SHADOW` se il livello DEBUG e' abilitato
  - eventuale `VISION_ML_SHADOW_DISAGREE`
- I contatori `Good`, `No Good` e difetti seguono VisionPro, non ONNX.
- Nessuna uscita scarto viene comandata dall'AI.
- La riga contesto mostra `set`, file/versione, soglia, resize e ora inizio.
- Gli eventi `VISION_ML_SHADOW` dello stesso gruppo riportano lo stesso `context`; cambiando soglia
  deve comparire un nuovo valore insieme ai campi `model_version` e `min_conf` aggiornati.
- Dopo il cambio soglia i campioni ripartono da zero; un secondo `Salva` senza cambiare il
  contesto non deve mescolare una nuova configurazione ne' cancellare i campioni.
- Se il disco e' temporaneamente lento, lo shadow worker attende il file senza rallentare il ciclo.
  `VISION_ML_SHADOW_IMAGE_TIMEOUT` e il contatore `timeout immagine` devono comparire solo dopo 10 s.

Nota:

- Il modello ONNX puo' essere generato offline oppure dal pannello con `Addestra modello`.
  Vedere `Documentation/AI/ONNX_Defect_Classifier_Guide.md`.

## Test 8A - Budget Risorse ONNX E Confronto AI OFF/ON

Obiettivo: dimostrare che lo shadow classifier non introduce code crescenti, non satura memoria e
non entra nel percorso decisionale macchina.

Procedura:

1. Con la stessa ricetta e la stessa cadenza prodotti, eseguire 15 minuti con TOP e SIDE AI OFF.
2. Annotare tempi VisionPro, cadenza pezzi, reattivita' UI, CPU e working set della HMI.
3. Abilitare due modelli validi TOP e SIDE e ripetere 15 minuti senza cambiare altri parametri.
4. Aggiornare le statistiche shadow e annotare `AI avg/max`, `attesa risorsa avg`, `saltati busy`
   e `timeout immagine` per entrambe le camere.
5. Aumentare temporaneamente la cadenza in simulazione e verificare che i campioni AI vengano
   saltati dal gate single-flight, senza accumulare una coda e senza crescita continua della RAM.
6. Disabilitare di nuovo entrambi i profili e premere `Salva`: le nuove ispezioni non devono
   avviare worker AI.

Pass:

- `Good`, `No Good`, difetti e scarto sono identici con AI OFF e ON a parita' di input VisionPro;
- TOP e SIDE non eseguono due inferenze contemporaneamente: una puo' mostrare
  `resource_wait_ms > 0`, ma il ciclo macchina non attende quel gate;
- al massimo un confronto per camera e' in volo; `saltati busy` puo' aumentare sotto carico ed e'
  il comportamento previsto per un servizio advisory;
- il working set raggiunge un plateau e non cresce a ogni prodotto;
- nessun freeze UI attribuibile a ONNX e nessuna variazione della cadenza trigger/scarto;
- dopo AI OFF non compaiono nuovi confronti o timeout per le ispezioni successive.

Attenzione: una coda risultati SIDE con `CAMERA_TIMING` di diversi secondi e' un problema separato
di acquisizione/pairing. Non attribuirlo automaticamente a ONNX; ripetere prima la prova con AI OFF.

## Test 8B - Training ONNX Integrato

Obiettivo: verificare che il training integrato sia supervisionato, tracciato e non invasivo.

Procedura:

1. Aprire `PC Diagnostics -> Controlli AI`.
2. Impostare `Python executable`, ad esempio:
   `C:\QtisVision\AI\.venv\Scripts\python.exe`.
3. Impostare `Model output folder`, ad esempio:
   `C:\QtisVision\AI\Models`.
4. Impostare `Epochs` con un valore basso per test, ad esempio `2`.
5. Premere `Test Python`.
6. Se il test fallisce, installare dipendenze:
   `pip install torch pillow numpy onnx`.
7. Con macchina ferma e dataset sufficiente, premere `Addestra modello`.

Pass:

- `Test Python` conferma le versioni disponibili oppure indica chiaramente cosa manca.
- Durante il training la UI mostra progressi e nei log compaiono `AI_TRAINING_PROGRESS`.
  Da `3.0.6.4` devono apparire fasi come `python_import`, `split`, `train`, `validation`,
  `export` oppure heartbeat `running` se Python e' vivo ma sta lavorando senza nuovo output.
- Da `3.0.7.3` non devono comparire `AI_TRAINING_STDERR` con `requires_grad=True`,
  `epoch_loss += float(loss)` o la sola deprecazione dell'exporter TorchScript PyTorch.
- In Events Monitor compaiono start/completed oppure failed/cancel/timeout.
- In cartella modelli appare `defect_classifier_vN.onnx` solo completo; eventuali `.tmp` non sono
  considerati modello valido.
- Il pannello precompila percorso/versione/note, ma il modello si attiva solo dopo `Salva`.
- Nel log `stage=split` compare `strategy=chronological_stratified_holdout`, con `val_ok` e `val_nok`.
- Con almeno 10 OK e 10 NOK nel solo holdout, lo stato e' `qualified`.
- Con meno campioni di validation, l'ONNX viene comunque creato ma popup, note modello e audit
  riportano `provisional`; non interpretare accuracy/recall come qualifica del modello.
- Verificare nel file `.onnx.metrics.json` le sezioni `validation.strategy`, `validation.status`,
  `validation.ok` e `validation.nok`.

## Test 9 - Fondazione Dati AI E Retention

Obiettivo: verificare persistenza dati AI senza blocco produzione.

Procedura:

1. Abilitare solo `Fondazione dati`.
2. Far passare almeno 20 pezzi.
3. Verificare che le tabelle AI ricevano dati, se MySQL e' disponibile:
   - `tbl_inspection_measurements`
   - `tbl_health_snapshots`
4. Spegnere temporaneamente MySQL in una prova controllata.
5. Continuare RunContinuous in simulazione o a macchina vuota.

Pass:

- Con DB attivo, i dati vengono scritti.
- Con DB non disponibile, il ciclo macchina continua.
- Gli errori DB AI sono throttled e non creano log flood continuo.
- Nessun errore DB AI deve bloccare scarto, trigger o contatori.

## Test 10 - Training Dataset

Obiettivo: verificare che il dataset sia generato solo quando esistono immagini pezzo.

Procedura:

1. Abilitare `Raccolta dati training`.
2. Verificare che il salvataggio immagini pezzo sia attivo secondo la configurazione corrente.
3. Far passare almeno 5 OK e 5 NOK.
4. Aprire una cartella `Piece_XXXXXXXX` salvata.

Pass:

- Presente `label.json`.
- Il file contiene:
  - timestamp;
  - productId;
  - ricetta;
  - label OK/NOK;
  - labelSource;
  - versione software;
  - elenco difetti.
- Se le immagini non vengono salvate, il dataset non deve inventare campioni.

## Test 11 - AI Performance Monitor E Advisor Ricetta

Obiettivo: usare AI per diagnosi senza azioni automatiche.

Performance monitor:

1. Abilitare `Monitor performance AI`.
2. Far girare almeno 200 pezzi o una simulazione lunga.
3. Controllare Events Monitor/log.

Pass:

- Eventuali `AI_PERFORMANCE_ADVISORY` sono informativi.
- Nessun timeout o parametro macchina viene cambiato automaticamente.

Advisor ricetta:

1. Abilitare `Advisor prodotto/ricetta`.
2. Usare una ricetta corretta e far passare prodotti buoni.
3. Poi, solo in prova controllata, usare un prodotto chiaramente non coerente con la ricetta.

Pass:

- Con ricetta corretta, nessun rumore continuo.
- Con prodotto errato, possibile evento:
  - `AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED`
  - eventuale `AI_RECIPE_CANDIDATE_FOUND`
- La ricetta non viene cambiata automaticamente.

## Test 12 - Top3D Detection Sensitivity

Obiettivo: validare lettura/scrittura per ricetta su testa Cognex L38 o equivalente.

Prerequisiti:

- Ricetta Top3D.
- Nome feature reale configurato in `Config.xml`:
  `Top3DDetectionSensitivityFeature`.

Procedura:

1. Aprire Job Tool Editor su pannello Top3D.
2. Verificare che la sezione `Detection sensitivity` sia visibile solo per Top3D.
3. Premere `Read`.
4. Annotare il valore letto.
5. Modificare un valore in modo controllato.
6. Premere `Apply`.
7. Salvare ricetta.
8. Ricaricare la stessa ricetta.

Pass:

- `Read` legge il valore corrente oppure mostra un messaggio chiaro se la feature non esiste.
- `Apply` scrive sulla testa e salva il valore nella ricetta.
- Al reload ricetta, il valore viene riapplicato.
- L'assenza di `ExposureAuto` su L38 non genera piu' warning rumorosi; al massimo Debug.
- Nessun effetto su trigger, contatori o scarto.

Se fallisce:

- Verificare il nome reale della feature GigE nel tool Cognex.
- Correggere `Top3DDetectionSensitivityFeature` in Config.xml.
- Ripetere senza ricompilare.

## Test 13 - Regressione Ciclo Macchina

Questo test e' obbligatorio anche se la modifica sembra solo UI.

| ID | Prova | Pass atteso |
|---|---|---|
| R01 | Caricare ricetta in produzione | Ricetta corretta in alto/attiva |
| R02 | Start RunContinuous | Stato Running, VisionPro continuo |
| R03 | Passare 10 pezzi buoni | Total e Good coerenti |
| R04 | Passare 5 pezzi con difetto noto | No Good incrementa di 1 per pezzo |
| R05 | Verificare `Total Defects` | Puo' essere maggiore dei pezzi se un pezzo ha piu' difetti, ma nessun doppio incremento dello stesso evento |
| R06 | Verificare uscita scarto | Un impulso per pezzo NOK atteso |
| R07 | Stop macchina | Ferma senza errori |
| R08 | Shutdown | Nessuna eccezione timer/semaforo non gestita |

Pass finale:

- La somma `Good + No Good` deve essere uguale a `Total`.
- L'AI non cambia nessun contatore.
- Gli allarmi scarto seguono solo le regole esistenti.

## Test 14 - Live Preview E Job Editor

Obiettivo: verificare che le ottimizzazioni Fase 0 non abbiano rotto la preview.

Procedura:

1. Aprire Job Tool Editor.
2. Avviare Live Preview su Top o Side.
3. Se la camera usa trigger hardware, verificare il loop IO live:
   - uscita trigger sale/scende;
   - intervallo configurato rispettato;
   - immagine VisionPro aggiornata.
4. Chiudere la Live Preview.
5. Uscire dal Job Tool Editor.

Pass:

- Nessun freeze evidente della UI.
- Il runtime hold si rilascia quando si esce dalla pagina, salvo Stop manuale.
- Non restano timer o impulsi IO attivi dopo chiusura preview.

## Test 15 - MultiShot E Companion Timeout

Obiettivo: verificare che i companion MultiShot lenti non siano marcati falsamente come
missing, senza accoppiare prodotti diversi.

Da eseguire solo su macchina/profilo con MultiShot.

Procedura:

1. Configurare `MultiShotCompanionMaxLagMs` in modo coerente con la fisica reale.
2. Abilitare MultiShot del profilo usato.
3. Far passare prodotti singoli e poi prodotti ravvicinati.
4. Controllare log:
   - `COMPANION_*`
   - `CAMERA_TIMING`
   - log MultiShot encoder.

Pass:

- Un risultato stitched arrivato entro lag fisico viene accettato.
- Un risultato troppo fuori finestra viene segnalato, non accoppiato al pezzo sbagliato.
- Nessun drop silenzioso di risultati ispezione.

## Test 16 - Degradazione Controllata

Obiettivo: simulare guasti esterni e verificare che la macchina non si blocchi dove non deve.

| Guasto simulato | Pass atteso |
|---|---|
| MySQL spento | Produzione continua; DB warnings throttled |
| OPC UA server spento | Produzione continua; OPC disconnected/fail log controllato |
| Modello ONNX mancante | Classificatore disabilitato/no-op; produzione continua |
| Nodi EarlyWarning mancanti | No-op sicuro; produzione continua |
| Ruolo non autorizzato su AI Save | Salvataggio negato; produzione continua |

## Codici Log Utili

| Codice | Significato |
|---|---|
| `AI_SETTINGS_APPLY` | Parametri AI salvati da ruolo autorizzato |
| `AI_EARLY_WARNING` | Preallarme singolo locale |
| `AI_HEALTH_DIGEST` | Digest periodico preallarmi |
| `OPCUA_EARLY_WARNING_WRITE_TIMEOUT` | Server OPC UA lento oltre 5s |
| `OPCUA_EARLY_WARNING_WRITE_SKIPPED_BUSY` | Scrittura advisory precedente ancora in corso |
| `VISION_ML_INIT` | Stato caricamento modello ONNX |
| `VISION_ML_SHADOW` | Predizione ONNX shadow-mode eseguita (DEBUG per gli accordi) |
| `VISION_ML_SHADOW_DISAGREE` | ONNX non concorda con VisionPro |
| `VISION_ML_SHADOW_STATS_CONTEXT_CHANGED` | Modello/preprocess/soglia cambiati; nuova finestra statistiche |
| `VISION_ML_SHADOW_IMAGE_TIMEOUT` | Immagine accodata non leggibile entro 10 s; produzione non bloccata |
| `AI_TRAINING_COMPLETED_PROVISIONAL` | Modello creato, ma holdout recente insufficiente per qualifica |
| `AI_PERFORMANCE_ADVISORY` | Advisory prestazioni pipeline |
| `AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED` | Possibile prodotto/ricetta non coerente |
| `IO_TIMING_APPLY` | Parametro timing applicato manualmente |

## Criteri Di Accettazione Finale

La release e' accettabile se:

- build Release x64: 0 errori;
- avvio, start, stop, cambio ricetta e shutdown sono stabili;
- `Good + No Good = Total`;
- l'AI non modifica esiti, scarto, contatori, trigger o ricette;
- valori AI fuori range non vengono salvati da UI;
- valori XML fuori range vengono normalizzati;
- OPC UA lento o assente non blocca la macchina;
- Top3D Detection Sensitivity e' validata su macchina oppure lasciata a valore non applicato;
- nessun log flood nuovo durante 30 minuti di prova;
- OneDrive baseline condivisa aggiornata dopo il test.

## Scheda Rapida Da Compilare

| Test | Esito | Note |
|---|---|---|
| Smoke test S01-S07 | Pass / Fail | |
| Build Release x64 | Pass / Fail | |
| Localizzazione UI AI | Pass / Fail | |
| Permessi salvataggio AI | Pass / Fail | |
| Validazione parametri AI | Pass / Fail | |
| Clamp config XML | Pass / Fail | |
| Preallarmi salute macchina | Pass / Fail / N.A. | |
| OPC UA EarlyWarning | Pass / Fail / N.A. | |
| ONNX shadow-mode | Pass / Fail / N.A. | |
| Dataset training | Pass / Fail / N.A. | |
| Top3D Detection Sensitivity | Pass / Fail / N.A. | |
| Regressione RunContinuous | Pass / Fail | |
| Contatori produzione/difetti | Pass / Fail | |
| Shutdown pulito | Pass / Fail | |
