# QtisVisionPanel - AI Integration Architecture

**Data:** 2026-07-10
**Stato:** implementazione AI consolidata fino alla release 3.1.1.2
**Contesto:** HMI industriale WPF/.NET per ispezione visiva, produzione 24/7

Questo documento descrive lo stato reale dell'integrazione AI nel progetto. Le funzioni AI sono additive: non decidono lo scarto, non comandano uscite, non cambiano ricette e non devono diventare un vincolo per il ciclo macchina.

## Principi

1. Advisory prima di tutto: AI segnala e suggerisce, ma VisionPro e la logica esistente restano autorevoli per esito, scarto e contatori.
2. Niente apprendimento online: training e retraining sono processi offline, supervisionati e versionati.
3. Degradazione sicura: se DB, modello ONNX o config non sono disponibili, la macchina continua a produrre.
4. Configurazione esplicita: ogni livello AI e' abilitato da `machine_runtime_config.xml`.
5. Tracciabilita': warning e disaccordi rilevanti vengono loggati in modo controllato, senza rumore continuo.

## Stato Implementato

| Fase | Stato | File principali | Default nuove configurazioni |
|------|-------|-----------------|------------------------------|
| Fase 0 - fondazione dati | Implementata | `MeasurementCaptureService`, `HealthSnapshotService`, repository AI | `false` |
| Fase 1 - SPC / deriva processo | Implementata advisory | `ProcessControlService` | `false` |
| Fase 2 - manutenzione predittiva | Implementata advisory | `PredictiveMaintenanceService` | `false` |
| Fase 3a - dataset training | Implementata | `TrainingDataCollectionService`, `tbl_training_samples` | `false` |
| Fase 3 - ONNX shadow-mode | Implementata scaffold | `OnnxDefectClassifier` | `false` |
| Fase 3b - training ONNX supervisionato | Implementata on-demand | `DefectClassifierTrainingService`, `Scripts/AI/train_defect_classifier.py` | n/d |
| Fase 4 - assistente Claude/LLM | Non implementata | solo concept documentale | n/d |
| Fase 5 - timing optimizer | Implementata advisory + apply confermato | `IoTimingOptimizerService`, `SystemDiagnosticsViewModel` | `false` |
| Fase 6 - performance monitor | Implementata advisory | `AiPerformanceMonitorService` | `false` |
| Fase 7 - advisor prodotto/ricetta | Implementata advisory | `RecipeProductAdvisorService` | `false` |
| Fase 8 - notifica derive/salute macchina | Implementata | `MachineHealthNotificationService` | `false` |
| Fase 9 - canale Email/SMTP | Implementata | `EmailNotificationChannel` (in `MachineHealthNotificationService.cs`) | `false` |
| Fase 9b - badge preallarmi in HMI | Implementata | `MachineHealthNotificationService` (`UnseenCount`), `SystemDiagnosticsService`, `TopMenuBarViewModel`, `TopMenuBar.xaml` | n/d (segue Fase 8) |

Nota importante: i file macchina esistenti che hanno gia' flag a `true` mantengono quel valore. I default `false` proteggono nuove configurazioni o config rigenerate.

## Configurazione

Le chiavi AI sono sotto `MachineRuntimeConfiguration.RuntimeBindings`:

- `DataFoundationCaptureEnabled`
- `ProcessControlEnabled`
- `PredictiveMaintenanceEnabled`
- `TrainingDataCollectionEnabled`
- `DefectClassifierEnabled`
- `DefectClassifierModelPath`
- `DefectClassifierModelVersion`
- `DefectClassifierModelNotes`
- `DefectClassifierInputWidth`
- `DefectClassifierInputHeight`
- `DefectClassifierGrayscale`
- `DefectClassifierNormalizeMean`
- `DefectClassifierNormalizeStd`
- `DefectClassifierTrainingPythonPath`
- `DefectClassifierTrainingOutputDir`
- `DefectClassifierTrainingEpochs`
- `IoTimingOptimizerEnabled`
- `AiPerformanceMonitorEnabled`
- `RecipeProductAdvisorEnabled`
- `MachineHealthNotificationsEnabled`
- `MachineHealthDigestIntervalMinutes`
- `MachineHealthCriticalEtaProducts`
- `MachineHealthDiskCriticalHours`
- `EmailNotificationsEnabled`
- `EmailSmtpHost`
- `EmailSmtpPort`
- `EmailUseSsl`
- `EmailFromAddress`
- `EmailToAddresses`
- `EmailSmtpUsername`
- `EmailSmtpPassword`
- `AiInspectionMeasurementRetentionDays`
- `AiHealthSnapshotRetentionDays`
- `AiTrainingSampleRetentionDays`

Retention:

- `AiInspectionMeasurementRetentionDays`: default 180 giorni.
- `AiHealthSnapshotRetentionDays`: default 90 giorni.
- `AiTrainingSampleRetentionDays`: default 0, cioe' nessuna cancellazione automatica dell'indice training.
- Il valore `0` disabilita la pulizia automatica per la tabella relativa.

## Database

Tabelle aggiunte:

- `tbl_inspection_measurements`: una riga per misura/camera/feature/prodotto.
- `tbl_health_snapshots`: snapshot periodici CPU/RAM/disco.
- `tbl_training_samples`: indice dei campioni etichettati collegati alle cartelle immagini.

Le tabelle vengono create con `CREATE TABLE IF NOT EXISTS`. I repository AI sono fire-and-forget e non bloccano il ciclo ispezione.

Dal consolidamento 3.0.3.8:

- gli errori DB AI non restano piu' solo in `Debug.WriteLine`;
- viene emesso un warning throttled, massimo uno ogni 10 minuti per tipo;
- la pulizia retention viene tentata al massimo ogni 12 ore per tabella.

## Runtime

Le chiamate AI sono agganciate dopo la finalizzazione dell'ispezione:

- cattura misure strutturate;
- salvataggio label training se esiste la cartella immagini del pezzo;
- confronto ONNX shadow-mode se abilitato e se esiste un modello;
- osservazione timing multi-camera.
- osservazione prestazioni post-acquisizione;
- advisor prodotto/ricetta basato su esiti/difetti recenti.

Non vengono modificati:

- pairing VisionPro;
- contatori produzione/difetto;
- esito OK/NOK;
- uscita scarto;
- ricette.

Le Fasi 6 e 7 sono agganciate nello stesso punto sicuro: dopo validazione, contatori, allarmi e pubblicazione OPC UA. Se sono disabilitate, il loro costo operativo resta limitato all'inizializzazione/no-op.

## ONNX Shadow-Mode

Il classificatore ONNX e' solo uno scaffold di validazione:

- lavora in shadow-mode;
- non decide lo scarto;
- oggi classifica l'immagine TOP salvata (`*_T_A.bmp`, fallback `*_T_Z.jpg`);
- i campi `DefectClassifierInputWidth/Height` sono il resize del modello ONNX, non la
  risoluzione camera; da `3.0.6.1` sono limitati a `16..1024 px` per lato;
- registra un evento `VISION_ML_SHADOW_DISAGREE` solo quando il modello non concorda con le regole VisionPro;
- logga `model_path` e `model_version` quando disponibili.

Prima di usarlo in campo servono:

- modello addestrato offline;
- dataset validato;
- metriche note: recall difetti, falsi scarti, confidenza minima;
- conferma che preprocessing, layout tensore e classi corrispondano al modello reale.

## Training ONNX Integrato

Da `3.0.6.2` il pannello `PC Diagnostics -> Controlli AI` puo' lanciare il training dal dataset
raccolto. Da `3.0.6.3` sono disponibili anche:

- configurazione HMI di `Python executable`, cartella output modelli ed epoche;
- pulsante `Test Python` (`--self-test`) per verificare `torch`, `pillow`, `numpy` e `onnx`;
- training eseguito con i valori correnti della UI, non con una rilettura vecchia da file;
- export atomico del modello (`.tmp` + replace finale) e log `AI_TRAINING_PROGRESS`.

Da `3.0.6.4` il training espone anche heartbeat e progressi granulari:

- `stage=python_import` prima/dopo caricamento `torch/pillow/numpy/onnx`;
- `stage=split`, `stage=model`, `stage=train`, `stage=validation`, `stage=export`;
- `stage=running` ogni 15 secondi se il processo Python e' vivo ma non emette nuovo output.

Python e' necessario solo per addestrare dal pannello. L'inferenza shadow-mode di un modello
`.onnx` gia' generato usa ONNX Runtime dentro la HMI e non richiede Python installato sulla
macchina.

## Dataset Training

Quando `TrainingDataCollectionEnabled=true`, il servizio scrive:

- `label.json` nella cartella del pezzo salvato;
- una riga indice in `tbl_training_samples`.

Il sidecar contiene:

- timestamp;
- productId;
- ricetta;
- label `OK` / `NOK`;
- `labelSource=VisionProRules`;
- versione software;
- cartella pezzo;
- elenco difetti rilevati.

Limite attuale: il dataset segue la politica esistente di salvataggio immagini. Se le immagini sono salvate a percentuale, anche il dataset sara' campionato secondo quella percentuale.

## SPC E Manutenzione Predittiva

`ProcessControlService` usa finestre mobili in memoria per ricetta/camera/feature e genera warning di deriva. I limiti statistici vengono calcolati sulla baseline precedente al campione corrente, cosi' il punto appena misurato non sposta subito il limite usato per valutarlo.

`PredictiveMaintenanceService` usa snapshot salute PC e valuta:

- proiezione riempimento disco;
- CPU sostenuta;
- RAM sostenuta.

Entrambi sono advisory e non attivano allarmi bloccanti.

## Timing Optimizer

`IoTimingOptimizerService` osserva lag companion, missing-rate e margine timeout. Le raccomandazioni sono advisory.

L'applicazione dei parametri:

- richiede azione esplicita dalla UI;
- e' abilitata solo per Installer/Administrator;
- valida whitelist e range;
- crea backup reale del file macchina prima del salvataggio;
- registra audit `IO_TIMING_APPLY`.

Nota operativa: il servizio salva il file macchina; il valore diventa effettivo dopo il refresh/reload runtime previsto dalla pagina di configurazione. Non viene forzato un reload automatico nascosto da dentro il servizio.

## Performance Monitor

`AiPerformanceMonitorService` misura, su finestra mobile, i tempi del ciclo post-acquisizione:

- display camere;
- validazione VisionPro/ToolBlock;
- contatori, allarmi e pubblicazione OPC UA;
- decisione salvataggio immagine;
- servizi AI/advisory;
- queue DB del record produzione.

Se un percentile p95 supera le soglie interne, viene loggato `AI_PERFORMANCE_ADVISORY` con stage, p50, p95 e numero campioni. Il monitor e' solo diagnostico: non cambia timeout, non disabilita servizi e non applica parametri.

Uso consigliato:

1. Abilitare `AiPerformanceMonitorEnabled=true` durante commissioning o prove macchina.
2. Lasciare girare almeno qualche centinaio di pezzi.
3. Leggere gli advisory per capire dove intervenire: VisionPro/job, display, DB, salvataggio immagini, OPC UA o servizi AI.
4. Disabilitare se non serve monitoraggio continuo.

## Advisor Prodotto/Ricetta

`RecipeProductAdvisorService` osserva esiti e difetti recenti della ricetta attiva. Se vede scarti consecutivi o fail-rate alto con un difetto dominante, genera:

- `AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED`: possibile formato o ricetta non coerente;
- `AI_RECIPE_CANDIDATE_FOUND`: eventuale ricetta candidata trovata in background riusando il matcher autoswitch esistente.

Il servizio non cambia ricetta, non ferma la macchina e non modifica i contatori. L'eventuale candidato deve essere verificato e caricato tramite i flussi gia' autorizzati dell'HMI.

Uso consigliato:

1. Abilitare `RecipeProductAdvisorEnabled=true` solo dopo aver verificato che il flusso autoswitch esistente e' stabile.
2. Usarlo come preallarme: avvisa prima o insieme agli scarti consecutivi, ma non sostituisce la conferma operatore.
3. Per prodotti nuovi senza ricetta, usare l'advisory come indicazione per creare una bozza da ricetta simile, non come generatore automatico di ricetta.

## Notifica Derive E Salute Macchina (Fase 8) E Canale Email (Fase 9)

`MachineHealthNotificationService` aggrega in tempo reale i segnali gia' prodotti da
`ProcessControlService.DriftDetected` (deriva ispezioni/SPC) e
`PredictiveMaintenanceService.MaintenancePredicted` (deriva salute PC), li normalizza in
un `EarlyWarning` (severita' Info/Warning/Critical, stima "quanto manca al limite" in
pezzi o ore) e li instrada su uno o piu' `INotificationChannel`. I critici vengono inviati
subito; il resto viene accumulato e inviato come digest periodico (default ogni 8 ore,
`MachineHealthDigestIntervalMinutes`). Anti-spam: la stessa chiave non ri-notifica prima
della fine del periodo di digest.

Canali disponibili:

- `LocalEventLogNotificationChannel`: sempre attivo, scrive in Events Monitor
  (`AI_EARLY_WARNING` / `AI_HEALTH_DIGEST`). Rete di sicurezza: i preallarmi restano
  tracciati anche senza rete o canali esterni configurati.
- `OpcUaNotificationChannel`: pubblica nodi read-only `EarlyWarning*` per un MES/SCADA che
  li legga gia'. No-op se OPC UA e' disabilitato/disconnesso.
- `EmailNotificationChannel` (Fase 9): invia via SMTP (`System.Net.Mail`) a uno o piu'
  destinatari. Nessun pacchetto aggiuntivo richiesto. Attivato da
  `EmailNotificationsEnabled` + host/mittente/destinatari valorizzati; se uno di questi
  manca il canale resta no-op silenzioso, coerente con gli altri canali. Password SMTP
  salvata in chiaro in `machine_runtime_config.xml`, stessa convenzione gia' in uso per
  la password del database in questo progetto (nessuna cifratura DPAPI).

Pulsante "Invia email di prova" nel pannello PC Diagnostics -> Controlli AI: usa
`EmailNotificationChannel.SendTestAsync`, che invia con i valori correnti del pannello
anche se non ancora salvati su file (a differenza dell'invio reale, che rilegge sempre la
configurazione da disco). Riservato a Installer/Administrator, stesso ruolo richiesto per
"Test Python".

Uso consigliato:

1. Abilitare `MachineHealthNotificationsEnabled` solo dopo aver verificato che
   `ProcessControlEnabled` e/o `PredictiveMaintenanceEnabled` producono gia' warning
   advisory coerenti (il canale locale resta comunque la rete di sicurezza minima).
2. Per il canale email: compilare host/porta/mittente/destinatari, salvare, poi premere
   "Invia email di prova" per verificare l'intera catena SMTP prima di fare affidamento sul
   digest reale.
3. Tenere il digest a un valore ragionevole per turno (default 480 minuti) per evitare
   sia rumore eccessivo sia ritardi lunghi su warning non critici.

## Badge Preallarmi In HMI (Fase 9b)

Prima della Fase 9b i preallarmi erano visibili solo aprendo manualmente
`PC Diagnostics -> Controlli AI` (pannello riservato a Expert/Installer/Administrator,
aggiornato solo su Refresh) oppure cercandoli in Events Monitor mescolati a tutti gli altri
eventi. Un preallarme poteva quindi restare inosservato per l'intero turno.

`MachineHealthNotificationService` tiene ora un timestamp `_lastAcknowledgedUtc` sopra la
lista `_recent` gia' esistente ed espone:

- `UnseenCount`: preallarmi `Warning`/`Critical` con `DetectedAtUtc` successivo all'ultimo
  ack (gli `Info` non generano badge, per non fare rumore);
- `HasUnseenWarnings`;
- `MarkWarningsSeen()`: azzera il contatore.

Il servizio emette `WarningStateChanged` quando arriva un nuovo warning o quando la lista
viene marcata come vista. `TopMenuBarViewModel` si sottoscrive all'evento e aggiorna il
badge sul dispatcher WPF, senza polling aggiuntivo; `SystemDiagnosticsService` mantiene
anche le proprieta' di snapshot `AiAdvisoryCount`/`HasAiAdvisories` nel proprio refresh.
`TopMenuBar.xaml` mostra un secondo badge arancione (`#D68A2C`) accanto a quello rosso
esistente.

Distinzione voluta tra i due badge:

| Badge | Cosa conta | Perche' |
|---|---|---|
| Rosso (esistente) | solo diagnostiche PC `Critical` | errore di sistema che richiede intervento subito |
| Arancione (Fase 9b) | preallarmi AI `Warning` + `Critical` non ancora visti | le derive sono advisory per natura: il badge serve proprio a farle notare prima che diventino scarto |

Il badge si azzera quando `SystemDiagnosticsViewModel.RefreshEarlyWarnings()` viene eseguito,
cioe' aprendo o aggiornando il pannello dei preallarmi. Il badge e' visibile a chiunque veda
la barra superiore; la navigazione al pannello resta soggetta al permesso esistente
`CanOpenSystemDiagnostics` (nessuna elevazione di privilegi introdotta).

## Assistente Claude / LLM

Non e' implementato nel codice corrente.

Se in futuro si decide di aggiungerlo, deve restare:

- read-only;
- advisory;
- senza tool che scrivono config, DB macchina o uscite;
- vincolato a una decisione esplicita sulla connettivita' internet/proxy;
- con secret/API key fuori dal repository.

## Procedura Di Collaudo Consigliata

1. Lasciare tutti i flag AI a `false` e verificare che la macchina produca identica alla baseline.
2. Abilitare solo `DataFoundationCaptureEnabled` e verificare crescita DB e retention.
3. Abilitare `ProcessControlEnabled` e controllare che produca solo warning advisory.
4. Abilitare `PredictiveMaintenanceEnabled` e verificare Events Monitor/log.
5. Abilitare `TrainingDataCollectionEnabled` solo se il salvataggio immagini e' gia' governato.
6. Abilitare `DefectClassifierEnabled` solo con modello ONNX versionato e validato. Da release `3.0.4.4`, il pannello PC Diagnostics valida il file `.onnx` e ricarica subito il classificatore dopo il salvataggio.
7. Abilitare `IoTimingOptimizerEnabled` solo per commissioning o diagnostica avanzata; applicare parametri solo dopo backup e conferma.
8. Abilitare `AiPerformanceMonitorEnabled` per misurare i colli di bottiglia senza cambiare il ciclo.
9. Abilitare `RecipeProductAdvisorEnabled` per preallarmi ricetta/formato, verificando che restino solo advisory.
10. Abilitare `MachineHealthNotificationsEnabled` e verificare che i preallarmi compaiano nel pannello "Controlli AI" e in Events Monitor.
11. Configurare `EmailNotificationsEnabled` con host/mittente/destinatari, premere "Invia email di prova" e confermare la ricezione prima di considerare il canale operativo.

## Rischi Residui

- Le tabelle AI possono crescere se retention e percentuale salvataggio immagini non sono configurate.
- Il dataset training puo' essere sbilanciato se dipende solo dal salvataggio immagini percentuale.
- ONNX oggi copre solo TOP: Left/Right/Bottom richiedono estensione esplicita del dataset e della logica di selezione immagini.
- Le advisory statistiche devono essere validate con dati reali prima di essere considerate affidabili per decisioni operative.
- L'advisor ricetta usa pattern di scarto e, quando disponibile, il matcher autoswitch esistente: puo' suggerire, ma non deve essere trattato come prova assoluta.
- Il monitor prestazioni misura il post-acquisizione: non sostituisce i log dettagliati di trigger, encoder o VisionPro quando il problema nasce prima del risultato.
- La password SMTP del canale Email e' salvata in chiaro in `machine_runtime_config.xml`: chi ha accesso al file/alla macchina la vede. Nessuna cifratura implementata in questo giro (decisione esplicita, coerente con la password DB gia' in chiaro nello stesso tipo di file).
- Su .NET Framework `SmtpClient.SendMailAsync` non supporta un `CancellationToken` nativo: un server SMTP irraggiungibile puo' far attendere il dispatch piu' a lungo del timeout nominale di 30s prima che l'eccezione interna di `SmtpClient` lo interrompa. Non blocca il ciclo macchina (l'invio gira sempre fuori dal thread di ispezione).
