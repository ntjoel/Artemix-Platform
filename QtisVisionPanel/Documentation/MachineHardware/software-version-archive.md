# Software Version Archive

## Scope

This document is the canonical archive of officially released application versions for `QtisVisionPanel`.

For every version we track:

- software version identifier
- main functional integrations
- configuration-file additions or required checks
- database tables/columns involved
- operator / maintainer notes when relevant

## Current Official Version

- `3.1.6.1`
- release baseline date: `2026-10-01`
- baseline type: verifica ruoli VPP e mascheramento runtime dei trigger delle camere assenti
- last installer media: full `3.1.5.5-r19` and update-only
  `3.1.5.5-r19` (`2026-09-24`)
- installer `r19`: distribuisce i worker encoder separati, lo scheduler impulsi
  monotono e il contratto compare PCIE-1884; updater senza seed/configurazioni macchina;
- installer `r18`: distribuisce la release 3.1.5.4, inclusi cataloghi lingua
  software, soglia AI per vista e pipeline annotata; l'updater continua a
  proteggere configurazioni macchina, Language runtime, ricette, VPP e modelli;
- installer `r17`: genera insieme al media completo un updater separato che
  contiene solo `ApplicationBin`, rifiuta le installazioni nuove e non include
  `RuntimeSeed`, prerequisiti, Programs, ricette, VPP o configurazioni macchina;
- installer `r16`: include il provider temperature PC, dipendenze NuGet e
  notice MPL-2.0 nell'aggiornamento differenziale;
- installer `r15`: machine Python PATH and Qtis variables, delayed Common
  Startup launcher with service/camera-NIC readiness checks, single-instance
  protection, startup log and SMBIOS hardware inventory
- installer-only `r14`: SHA-256 differential HMI update with persistent
  rollback journal, protected machine/runtime paths and optional safe Cognex
  GigE adapter configuration from backward-compatible `CameraConfig.xml`
  metadata; application behavior and assembly version remain `3.1.1.2`

## Version 3.1.6.0

- Release date: `2026-09-29`; previous baseline: `3.1.5.9`.
- Recipe trigger setup keeps RIGHT and REAR visible as separate rows. A role absent from both the selected recipe's explicit inspection profile and the active VPP remains visible but disabled. SIDE is labeled from the active job name while continuing to use the LEFT physical role.
- When a different recipe is selected, its explicit camera-view profile contributes roles independently of the currently loaded VPP. After VPP load/reload, the recipe screen refreshes camera visibility and adjustment previews.
- The historical MultiShot `Right` profile is labeled with its actual configured physical `CameraRole` to avoid presenting a REAR-bound profile as RIGHT. Existing machine XML, recipe XML, `Config.xml` and MySQL schemas remain unchanged.
- Release x64 build passed. Confirm the role rows and effective outputs on the machine before enabling RIGHT and REAR triggers.

## Version 3.1.5.9

- Release date: `2026-09-29`; previous baseline: `3.1.5.8`.
- Machine Setup shows a compact physical DI/DO mapping at the top. The full intervention table and specialist machine tables start collapsed. Repeated encoder summary cards were removed from this page; Overview still contains the live summary.
- The compact table edits the existing machine signals and uses the existing Save configuration action and XML file. No `Config.xml`, machine XML schema, recipe or MySQL migration is required.
- Commissioning: verify board, channel, polarity and physical-signal flag with the machine stopped. Save and reload to check persistence; validate output wiring before enabling a point.
- Release x64 build passed. Visual layout and electrical I/O still require machine verification.

## Version 3.0.7.8

### Release metadata

- release date: `2026-07-21`
- previous baseline: `3.0.7.7`

### Release intent

Consolidare la baseline prima dei successivi interventi di stabilita' industriale: eliminare solo
codice dimostrabilmente non usato, riallineare documentazione e manuale al software corrente e
rendere verificabili i rischi residui senza modificare il contratto macchina.

### Main functional integrations included

- tag di backup `pre-industrial-audit-3.0.7.7-20260721` creato prima della pulizia
- rimozione di file, converter, modelli, metodi privati e copie commentate senza consumer
- rimozione dell'ispezione GOOD artificiale eseguita durante il caricamento ricetta
- manuale HMI consolidato in otto capitoli Markdown numerati, ciascuno con immagine e tabella ID
- documenti tecnici/piani esclusi dall'elenco Manuale e spostati nell'archivio tecnico
- nuovo audit completo e piano industriale con priorita', criteri di accettazione e rollback
- correzione dei documenti live che descrivevano componenti o issue gia' rimossi/chiusi

### Configuration-file notes

- nessuna nuova chiave in `Config.xml`, `machine_runtime_config.xml` o XML ricetta
- mapping I/O, encoder, quote macchina e profili MultiShot globali invariati

### Database notes

- nessuna tabella, colonna o migrazione richiesta
- persistenza e valori dei contatori invariati; cambia solo la rimozione del falso evento GOOD al
  load ricetta

### Operator / maintainer notes

- caricare una ricetta non deve piu' incrementare `Total` o `Good`
- la pagina Manuale mostra solo i documenti operatore `NN_*.md`
- la build Release x64 deve chiudersi con 0 errori; i warning residui sono inventariati nell'audit
- prima della qualifica 24/7 eseguire la checklist in
  `full-application-audit-2026-07-21.md`

## Version 3.0.7.7

### Release metadata

- release date: `2026-07-14`
- previous baseline: `3.0.7.6`

### Release intent

Rendere la pagina Recipe Management piu compatta e professionale su pannelli industriali,
eliminando la frammentazione tra area ricetta e parametri e mantenendo leggibili tutti i controlli
su schermi con risoluzioni differenti.

### Main functional integrations included

- area dettagli e area parametri raccolte in un unico workspace con una sola scrollbar verticale
- layout a due colonne quando lo spazio utile e sufficiente
- passaggio automatico a colonna singola sotto `1040 px` utili: i parametri vengono spostati sotto
  i dati ricetta senza nascondere controlli
- panoramica ricetta e stato database convertiti in griglie proprieta compatte
- stile piu industriale: raggi ridotti, header tecnici, spaziature uniformi, immagine prodotto e
  selettori ispezione meno ingombranti
- binding, permessi, comandi, validazione e persistenza esistenti invariati

### Configuration-file notes

- `Config.xml`, `machine_runtime_config.xml` e XML ricetta: nessuna modifica

### Database notes

- nessuna tabella, colonna o migrazione richiesta

### Operator / maintainer notes

- su monitor larghi i parametri restano a destra dei dati prodotto
- su pannelli compatti, inclusi i 15 pollici a `1366 px`, scorrere verso il basso per trovare le
  stesse sezioni parametri sotto ai dati principali
- il cambio layout e esclusivamente visivo e non modifica la ricetta finche non viene usato il
  comando di salvataggio esistente

## Version 3.0.7.6

### Release metadata

- release date: `2026-07-14`
- previous baseline: `3.0.7.5`

### Release intent

Rendere immediata e verificabile la taratura degli offset ricetta mostrando, accanto a ogni campo,
il valore globale macchina e il valore effettivo che verra usato dal runtime.

### Main functional integrations included

- riepilogo `Globale -> Effettivo` per le quote camera Top, Side, Left, Front, Right, Rear e Bottom
- riepilogo globale/effettivo per stato MultiShot, numero scatti, limite massimo, step e origine del
  primo scatto
- aggiornamento automatico al cambio ricetta, al rientro nella pagina e all'uscita da ogni campo
- uso dello stesso `RecipeMachineRuntimeResolver` del ciclo produttivo, inclusi alias fisici
  Left/Side e Right/Rear e fallback sicuri di validazione
- messaggi runtime localizzati in inglese e italiano

### Configuration-file notes

- `machine_runtime_config.xml`: nessun campo nuovo; viene letto in sola lettura per l'anteprima
- XML ricetta: nessun campo nuovo rispetto alla release `3.0.7.5`

### Database notes

- nessuna tabella, colonna o migrazione richiesta

### Operator / maintainer notes

- `Globale` indica la quota macchina gia comprensiva di base e trim commissioning
- `Effettivo` indica il valore globale dopo l'applicazione della correzione della ricetta selezionata
- se la calibrazione encoder non e valida, l'origine MultiShot in millimetri viene indicata come non
  disponibile invece di mostrare un valore stimato
- un avviso giallo segnala quando i dati inseriti non sono validi e l'anteprima sta mostrando il
  fallback runtime sicuro

## Version 3.0.7.5

### Release metadata

- release date: `2026-07-14`
- previous baseline: `3.0.7.4`

### Release intent

Mantenere globali le quote nominali e i vincoli fisici della macchina, consentendo a ogni ricetta
di applicare correzioni camera e MultiShot necessarie per la geometria del prodotto.

### Main functional integrations included

- offset posizione ricetta per `Top`, `Side`, `Left`, `Front`, `Right`, `Rear` e `Bottom`
- modo MultiShot per profilo: eredita macchina, abilitato per ricetta o disabilitato per ricetta
- delta ricetta per numero scatti, step in mm e origine della sequenza
- resolver runtime non persistente condiviso da tracking encoder, trigger MultiShot e parametri
  VisionPro `ImageStitching`
- validazione prima del salvataggio, fallback sicuro e logging operativo
- UI e messaggi runtime inglese/italiano aggiornati

### Configuration-file notes

- `machine_runtime_config.xml`: invariato; resta la sorgente di I/O, encoder, quote base, trim,
  timing, calibrazione e limiti
- XML ricetta: nodo opzionale `machineRuntimeAdjustments`; ricette precedenti restano compatibili
  e usano offset zero / modo `Machine`

### Database notes

- nessuna tabella o migrazione richiesta

### Operator / maintainer notes

- impostare prima la geometria nominale in `PCIE settings -> Machine Setup`
- usare l'editor ricetta solo per differenze dovute al prodotto
- un delta primo scatto trasla l'intera sequenza MultiShot, mentre il delta step cambia la distanza
  tra immagini
- in `TimedFromPhotocell` restano autorevoli i trigger delay temporali della ricetta

## Version 3.0.7.4

### Release metadata

- release date: `2026-07-14`
- previous baseline: `3.0.7.3`

### Release intent

Correggere i difetti reali emersi dalla review avversariale completa dello stack AI (32 segnalazioni → 23 respinte → 5 corrette) e documentare i rischi accettati.

### Main functional integrations included

- gate anti doppio-training (`TrainingGate` + ricontrollo post-dialog contro la re-entrancy WPF): chiusa la corsa TOCTOU sul nome `defect_classifier_vN.onnx`
- "Salva"/"Ricarica" controlli AI bloccati durante il training (config swap sotto training)
- validazione esplicita [0,1] delle soglie di confidenza NOK (Top e Side) con messaggio
- simmetria marker schema-upgrade: aggiunti i 6 marker TOP mancanti
- buffer coda stderr sempre ≤ 4000 caratteri

### Configuration-file notes

- nessun campo nuovo; le config vecchie vengono ri-salvate una volta all'avvio con i tag TOP espliciti

### Database notes

- nessuna modifica

### Operator / maintainer notes

- durante un training i pulsanti Salva/Ricarica dei controlli AI sono disabilitati: attendere il completamento
- un secondo "Addestra" durante un training riceve un messaggio esplicito, non parte in parallelo
- rischi accettati documentati nel changelog (mix top/top3d nel dataset, matrice confusione solo su .metrics.json)

## Version 3.0.7.0

### Release metadata

- release date: `2026-07-13`
- previous baseline: `3.0.6.9`

### Release intent

Ridurre i falsi allarmi del classificatore (visti sulla TOP) con una confidenza minima per dichiarare NOK, per camera, senza riaddestrare e senza nascondere i casi soppressi.

### Main functional integrations included

- config `DefectClassifierMinConfidence` / `SideDefectClassifierMinConfidence` (0..1, default 0 = off), clamp [0,1]
- gate in `OnnxDefectClassifier.Classify`: NOK sotto soglia -> "incerto" (non-difetto); `DefectClassificationResult.IsUncertain`
- statistiche shadow: contatore `Uncertain` (incerti) nel riepilogo Top/Side; tag `uncertain=` nel log
- UI: campo "Confidenza min NOK" per Top e Side

### Configuration-file notes

- machine_runtime_config.xml: 2 nuovi campi `*DefectClassifierMinConfidence` (default 0)

### Database notes

- nessuna modifica

### Operator / maintainer notes

- soglia 0 = comportamento invariato; alzarla riduce i falsi allarmi ma puo' ridurre il recall se un difetto reale ha confidenza bassa
- usare il contatore "incerti (sotto soglia)" nelle statistiche per capire quanto la soglia sta sopprimendo prima di fidarsi
- resta advisory: nessun impatto su scarto/contatori

## Version 3.0.6.9

### Release metadata

- release date: `2026-07-13`
- previous baseline: `3.0.6.8`

### Release intent

Correggere i difetti confermati dalla review avversariale della 3.0.6.8: coerenza del contratto etichette training/inference per-camera e robustezza del semaforo shadow allo shutdown.

### Main functional integrations included

- trainer Python: fallback alla label globale solo per sidecar legacy (senza blocco `labels`); se il blocco esiste ma manca la camera → skip (niente contaminazione)
- `OnnxDefectClassifier.ResolveRuleValidity` simmetrico per top/side (per-camera, skip se assente; niente fallback globale del top)
- `_shadowGate.Release()` protetto da ObjectDisposedException allo shutdown
- respinto un falso positivo (File.ReadAllText usa gia' UTF-8)

### Configuration-file notes / Database notes

- nessuna modifica

### Operator / maintainer notes

- shadow-mode ora sempre per-camera coerente col training; il contatore `senza_etichetta_camera` mostra i pezzi saltati perche' quella camera non era etichettata
- dataset reale: TOP 596/187, SIDE 312/36 (legacy saltati per la Side) → training Side gia' fattibile
- riavviare l'app per aggiornare l'exe in bin (era bloccato dall'app in esecuzione)

## Version 3.0.6.8

### Release metadata

- release date: `2026-07-13`
- previous baseline: `3.0.6.7`

### Release intent

Portare il classificatore difetti ONNX (shadow-mode) anche sulla camera SIDE, non solo TOP: seconda istanza indipendente con modello, preprocessing, training e statistiche propri.

### Main functional integrations included

- `OnnxDefectClassifier` parametrico per camera (top/side): suffisso immagine `_T_`/`_F_`, config per camera, confronto vs risultato-regole della camera (TopResult/SideResult)
- istanza `ServiceLocator.SideDefectClassifier` + secondo hook shadow in MainWindow
- 9 campi config `SideDefectClassifier*`; training `--camera` con output `defect_classifier_side_vN.onnx`
- `label.json` con etichette per-camera (`labels`); Side richiede `labels.side`, Top con fallback storico
- UI: sezione Side (config + Addestra Side) e statistiche shadow Top/Side separate

### Configuration-file notes

- machine_runtime_config.xml: nuovi `SideDefectClassifier*` (Enabled/ModelPath/Version/Notes/InputWidth/InputHeight/Grayscale/NormalizeMean/NormalizeStd)
- label.json: nuovo oggetto `labels` per-camera (retrocompatibile)

### Database notes

- nessuna modifica

### Operator / maintainer notes

- Side disabilitata di default: nessun cambiamento finche' non si abilita + si fornisce un modello Side
- per il training Side servono pezzi con `labels.side` (dataset raccolto dopo questa release); i sidecar storici valgono solo per il TOP
- il TOP ora confronta lo shadow vs TopResult (non piu' l'esito globale): statistiche advisory piu' accurate
- verificato: compilazione exit 0 (OutDir separato, exe bloccato da app in esecuzione); riavviare l'app per aggiornare l'exe in bin

## Version 3.0.6.7

### Release metadata

- release date: `2026-07-10`
- previous baseline: `3.0.6.6`

### Release intent

Valutare il classificatore ONNX con numeri invece che leggendo migliaia di righe di log: riquadro shadow-mode in PC Diagnostics che aggrega ML vs regole VisionPro.

### Main functional integrations included

- `OnnxDefectClassifier`: aggregatore finestra mobile (2000) + `GetShadowStats()`/`ResetShadowStats()`; classe `ShadowModeStats` (matrice di confusione + accordo, recall NOK, precisione NOK, falsi allarmi, confidenza media)
- card "Statistiche shadow-mode AI" in PC Diagnostics con 5 metriche + riepilogo + Aggiorna/Azzera
- verita' di riferimento = regole VisionPro; recall difetti evidenziata come metrica chiave

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica

### Operator / maintainer notes

- la metrica chiave e' la RECALL difetti (con dataset sbilanciato l'accuracy inganna)
- "Azzera" (Installer/Administrator) riparte la finestra di valutazione dopo un nuovo modello
- statistiche in memoria: ripartono a zero al riavvio dell'app

## Version 3.0.6.6

### Release metadata

- release date: `2026-07-10`
- previous baseline: `3.0.6.5`

### Release intent

Fix macchina: "Addestra modello" segnalava dataset vuoto (OK=0, NOK=0) pur con 165 campioni su disco. Causa: i sidecar `label.json` sono scritti con BOM UTF-8 e il trainer Python li scartava in silenzio.

### Main functional integrations included

- trainer Python: lettura sidecar con `utf-8-sig` (compatibile con file CON e SENZA BOM)
- contatori di scarto espliciti nello scan (`json_illeggibile`, `label_ignota`, `senza_immagine`): un dataset vuoto ora mostra subito il perche'
- writer C#: nuovi sidecar UTF-8 senza BOM

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica

### Operator / maintainer notes

- non serve toccare i file gia' raccolti: vengono letti correttamente
- verificato sul campo: scan reale ora `ok=125, nok=40`
- la copia dell'exe in bin richiede l'app chiusa (lock del processo); la compilazione e' ok

## Version 3.0.6.5

### Release metadata

- release date: `2026-07-10`
- previous baseline: `3.0.6.4`

### Release intent

Fix macchina: "Addestra modello"/"Test Python" fallivano con "Impossibile trovare il file specificato" pur con Python installato. Ora l'eseguibile Python viene risolto automaticamente a percorso assoluto.

### Main functional integrations included

- `ResolvePythonExecutable`: percorso esplicito rispettato; nome nudo cercato in PATH processo+utente+macchina (salta l'alias Store WindowsApps) e nelle cartelle d'installazione tipiche (per-utente e sistema); fallback al py launcher; log del percorso risolto
- risoluzione a runtime (config invariata), punto unico per training e Test Python

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica

### Operator / maintainer notes

- non serve piu' mettere il percorso completo di python nel campo config: "python" viene risolto da solo (installazione per-utente inclusa)
- se il modello non parte ancora: verificare con "Test Python" che torch/pillow/numpy siano installati (`pip install torch pillow numpy`)

## Version 3.0.6.4

### Release metadata

- release date: `2026-07-10`
- previous baseline: `3.0.6.3`

### Release intent

Migliorare la diagnostica live del training AI integrato: se il processo Python resta in esecuzione
senza produrre output immediato, il pannello mostra un heartbeat periodico invece di sembrare fermo.

### Main functional integrations included

- `DefectClassifierTrainingService`: `PYTHONUNBUFFERED=1` e heartbeat ogni 15 secondi durante il
  processo Python
- `train_defect_classifier.py`: progressi espliciti per import dipendenze, split dataset,
  inizializzazione modello, batch/epoche e validazione
- log `AI_TRAINING_PROGRESS` piu' leggibili per distinguere import lento, training CPU-bound,
  validazione ed export ONNX

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica

### Operator / maintainer notes

- durante il training e' normale vedere messaggi `stage=running` se Python sta lavorando ma non ha
  ancora concluso una fase
- se il campo Python resta `python`, il comando deve essere realmente disponibile nel PATH della
  macchina; in campo e' preferibile indicare il path completo del virtual environment

## Version 3.0.6.3

### Release metadata

- release date: `2026-07-10`
- previous baseline: `3.0.6.2`

### Release intent

Rendere il training ONNX integrato piu' robusto e usabile in campo: il pannello usa i parametri
correnti della UI, permette di configurare/testare Python senza edit XML manuale, traccia meglio
lo stato di avanzamento e protegge il file modello da scritture parziali.

### Main functional integrations included

- `SystemDiagnosticsView`: nuovi campi `Python executable`, `Model output folder`, `Epochs` e pulsante `Test Python`
- `SystemDiagnosticsViewModel`: training avviato con i binding correnti della schermata, non con una rilettura vecchia da file; notifica finale di successo/fallimento
- `DefectClassifierTrainingService`: pre-scan dataset fuori dal thread UI, logging `AI_TRAINING_PROGRESS`, test Python `--self-test`, audit start/completed/failed/cancel/timeout
- `Scripts/AI/train_defect_classifier.py`: self-test dipendenze, export ONNX su temporaneo + `os.replace`, validazione `onnx.checker`
- localizzazione ENG/ITA aggiornata per tutti i nuovi controlli

### Configuration-file notes

- nessuna nuova chiave rispetto a `3.0.6.2`
- i campi `DefectClassifierTrainingPythonPath`, `DefectClassifierTrainingOutputDir` e `DefectClassifierTrainingEpochs` sono ora editabili dal pannello e clampati lato config

### Database notes

- nessuna modifica

### Operator / maintainer notes

- su macchine senza Python installato usare `Test Python` per verificare subito l'ambiente
- installazione minima consigliata: `python -m venv C:\QtisVision\AI\.venv` e `pip install torch pillow numpy onnx`
- in alternativa il modello puo' essere addestrato offline su PC tecnico e copiato nella cartella modelli: la HMI non richiede Python per usare un `.onnx` gia' creato
- il modello resta shadow-mode e non viene attivato automaticamente

## Version 3.0.6.2

### Release metadata

- release date: `2026-07-10`
- previous baseline: `3.0.6.1`

### Release intent

Chiudere la pipeline dati AI: pulsante "Addestra modello" in PC Diagnostics che addestra il classificatore difetti dal dataset `label.json` raccolto e produce `defect_classifier_vN.onnx` in `C:\QtisVision\AI\Models`, tramite script Python in processo esterno.

### Main functional integrations included

- `Scripts/AI/train_defect_classifier.py`: CNN leggera da zero (air-gapped, CPU-only), split stratificato, pesi di classe, best-epoch su validazione, export ONNX coerente col contratto HMI + `.metrics.json`
- `DefectClassifierTrainingService`: pre-scan dataset (min 10/classe), versionamento automatico v1/v2/..., progresso live, cancel/timeout, verifica caricabilita' ONNX
- pannello ONNX: pulsanti Addestra/Annulla + stato; a successo precompila percorso/versione/note (attivazione solo con "Salva")
- guardie: ruolo Installer/Administrator, rifiuto con produzione attiva, conferma esplicita

### Configuration-file notes

- machine_runtime_config.xml: nuovi `DefectClassifierTrainingPythonPath` (`python`), `DefectClassifierTrainingOutputDir` (`C:\QtisVision\AI\Models`), `DefectClassifierTrainingEpochs` (20)

### Database notes

- nessuna modifica

### Operator / maintainer notes

- prerequisito una tantum sulla macchina: `pip install torch pillow numpy onnx`
- addestrare a macchina ferma; valutare le metriche (soprattutto NOK recall) prima di abilitare in shadow-mode
- il modello NON si attiva da solo: il pannello viene precompilato, l'attivazione richiede "Salva"
- guida completa: `Documentation/AI/ONNX_Defect_Classifier_Guide.md` sezione "Training Integrato Dal Pannello"

## Version 3.0.6.1

### Release metadata

- release date: `2026-07-10`
- previous baseline: `3.0.6.0`

### Release intent

Chiarire e proteggere la configurazione del classificatore ONNX dopo il feedback di collaudo:
le dimensioni `Width/Height` non sono la risoluzione nativa delle camere, ma il resize richiesto
dal modello AI. La baseline corrente resta TOP-only in shadow-mode.

### Main functional integrations included

- UI PC Diagnostics: titolo `ONNX classifier (TOP shadow-mode)`, label `Resize width/height`
  e messaggio esplicito che la risoluzione camera non va inserita in quei campi
- localizzazione ENG/ITA aggiornata per i nuovi testi ONNX
- validazione salvataggio e clamp configurazione: input ONNX ammesso 16..1024 px per lato
- commento runtime su `MachineRuntimeBindings`: il classificatore usa solo immagine TOP salvata
  e ridimensiona secondo il modello

### Configuration-file notes

- nessuna nuova chiave XML
- i campi esistenti `DefectClassifierInputWidth` e `DefectClassifierInputHeight` ora sono
  normalizzati a `16..1024`; se il file contiene valori camera-native come `1800x1500`,
  il salvataggio/normalizzazione li riporta nel range sicuro

### Database notes

- nessuna modifica schema

### Operator / maintainer notes

- per il modello ONNX usare valori tipici di training/inferenza come `224x224`, `320x320`,
  `512x512` o `640x640`, non la risoluzione camera
- Top e Side possono avere risoluzioni diverse: nella baseline attuale il classificatore ONNX
  non e' per-camera e non analizza Side/Left/Right/Bottom
- per classificare piu' camere servira' una futura estensione con profili/modelli per ruolo
  camera, dataset separato e selezione immagine esplicita

## Version 3.0.6.0

### Release metadata

- release date: `2026-07-09`
- previous baseline: `3.0.5.9`

### Release intent

Rifinitura produzione della Fase 8 AI/preallarmi: rendere il pannello completamente localizzabile, evitare valori numerici fuori range nel runtime config e proteggere la pubblicazione OPC UA advisory da reti lente.

### Main functional integrations included

- localizzazione completa della nuova UI "Controlli AI" e "Preallarmi salute macchina" tramite `ServerMessageStructure`, `ServerMessagePersonalize`, `Localization/messages_*.json` e `scripts/UpdateRuntimeLanguageFiles.ps1`
- messaggio operativo esplicito: i preallarmi richiedono SPC e/o manutenzione predittiva abilitati per ricevere segnali in ingresso
- validazione lato pannello e clamp lato `MachineConfigurationService` per soglie preallarmi, dimensioni/preprocessing ONNX e retention AI
- hardening leggero OPC UA: `WriteEarlyWarningAsync` e' single-flight, usa timeout 5s, taglia il messaggio a 1000 caratteri e non accumula scritture advisory se il server e' lento

### Configuration-file notes

- nessuna nuova chiave XML; vengono solo validati/clampati campi gia' introdotti nelle release AI precedenti
- range applicati: digest preallarmi 1..1440 min, ETA critica 1..1000000 pezzi, disco critico 0.5..8760 ore, retention AI 0..3650 giorni, input ONNX 16..4096 px, normalizzazione ONNX entro range sicuro

### Database notes

- nessuna modifica schema

### Operator / maintainer notes

- il pannello resta role-gated Installer/Administrator per il salvataggio
- con `MachineHealthNotificationsEnabled=false` non cambia nulla nel runtime
- se le notifiche sono abilitate ma SPC e manutenzione predittiva sono entrambe spente, il servizio resta in attesa e non genera preallarmi
- OPC UA lento o non mappato non blocca la macchina: un write preallarme occupato viene saltato come advisory

## Version 3.0.5.9

### Release metadata

- release date: `2026-07-09`
- previous baseline: `3.0.5.8`

### Release intent

Interfaccia in PC Diagnostics per abilitare le notifiche salute macchina e vedere i preallarmi, senza dover leggere i log.

### Main functional integrations included

- pannello "Controlli AI": checkbox "Notifiche salute macchina" + soglie (digest minuti, ETA critica pezzi, disco ore critiche), bind diretto a RuntimeBindings, salvataggio col flusso esistente (role-gated)
- nuova card "Preallarmi salute macchina": lista colorata per severita' con categoria, titolo, causa a monte, ETA e timestamp locale; pulsante "Aggiorna"
- `MachineHealthNotificationService.GetRecentWarnings()` (storico ultimi 100) + helper display su EarlyWarning

### Configuration-file notes

- nessuna modifica (i campi esistevano gia' da 3.0.5.7)

### Database notes

- nessuna modifica

### Operator / maintainer notes

- accensione da PC Diagnostics → Controlli AI; richiede anche SPC/manutenzione predittiva per avere segnali
- la lista preallarmi e' uno snapshot: usare "Aggiorna" per ricaricare
- prossimo step: canale Email/SMTP

## Version 3.0.5.8

### Release metadata

- release date: `2026-07-09`
- previous baseline: `3.0.5.7`

### Release intent

Primo canale esterno per i preallarmi salute macchina: pubblicazione verso MES/SCADA via OPC UA (canale scelto dal cliente, air-gap friendly).

### Main functional integrations included

- `OpcUaNotificationChannel` (implementa `INotificationChannel`), registrato nel servizio Fase 8 accanto al canale locale
- `IOpcUaClientService.WriteEarlyWarningAsync` + implementazione: scrive nodi read-only `EarlyWarning*` (Active/Severity/Code/Message/EtaValue/EtaUnit/Count/Timestamp/Sequence) con sequenza monotona
- 9 nodi `EarlyWarning*` (ClientToServer) nei default OpcUaConfig

### Configuration-file notes

- config OPC UA: nuove config includono i nodi `EarlyWarning*`; le esistenti (specie DB-backed) vanno integrate dal pannello di configurazione. Nodo non mappato = no-op sicuro

### Database notes

- nessuna modifica di schema (i nodi OPC UA seguono la persistenza config OPC UA esistente)

### Operator / maintainer notes

- richiede sia `MachineHealthNotificationsEnabled` sia OPC UA abilitato e connesso
- il MES puo' monitorare `EarlyWarningSequence` per rilevare nuovi preallarmi anche a testo invariato
- prossimi step: Email/SMTP e UI di abilitazione + pannello preallarmi

## Version 3.0.5.7

### Release metadata

- release date: `2026-07-09`
- previous baseline: `3.0.5.6`

### Release intent

Fondazione del monitoraggio derive in tempo reale (salute PC + ispezioni) con preallarmi al cliente per anticipare i problemi di macchina a monte. Riusa i rilevatori advisory gia' presenti; nessun canale esterno ancora attivo.

### Main functional integrations included

- nuovo `MachineHealthNotificationService`: aggrega `DriftDetected` (SPC ispezioni) e `MaintenancePredicted` (salute PC), normalizza in `EarlyWarning` con severita' e stima "quanto manca al limite" (pezzi/ore)
- cadenza: critico immediato + digest periodico (timer configurabile, default ~1 turno)
- astrazione `INotificationChannel` + canale locale `LocalEventLogNotificationChannel` (Events Monitor) sempre attivo come rete di sicurezza
- de-duplica/throttle anti-rumore; tutto off-thread e degradation-safe
- 4 nuovi flag config (default sicuri), refresh a caldo

### Configuration-file notes

- machine_runtime_config.xml: nuovi `MachineHealthNotificationsEnabled` (false), `MachineHealthDigestIntervalMinutes` (480), `MachineHealthCriticalEtaProducts` (50), `MachineHealthDiskCriticalHours` (24)

### Database notes

- nessuna modifica

### Operator / maintainer notes

- disabilitato di default: nessun cambiamento finche' non si abilita
- per avere segnali servono anche `ProcessControlEnabled` e `PredictiveMaintenanceEnabled`
- da abilitato, i preallarmi appaiono nell'Events Monitor (`AI_EARLY_WARNING`, `AI_HEALTH_DIGEST`)
- canali esterni (MES/OPC UA, Email/SMTP) e UI dedicata: step successivi

## Version 3.0.5.6

### Release metadata

- release date: `2026-07-09`
- previous baseline: `3.0.5.5`

### Release intent

Completare la release Top3D `3.0.5.5` allineando la nuova sezione
"Detection sensitivity" al sistema messaggi HMI e ai file lingua runtime.

### Main functional integrations included

- aggiunte le chiavi `Sub_entry_*DetectionSensitivity*` in `ServerMessageStructure`
- `JobToolEditorView` localizza label, pulsante Read e pulsante Apply sensitivity
  come il resto della Live Preview
- gli stati di lettura/applicazione sensitivity usano messaggi dedicati invece di
  riusare le chiavi dell'esposizione
- `scripts/UpdateRuntimeLanguageFiles.ps1` aggiorna le nuove chiavi nei cataloghi
  ENG/ITA e nei file runtime `C:\QtisVision\Language`

### Configuration-file notes

- nessuna nuova modifica oltre a quanto introdotto in `3.0.5.5`

### Database notes

- nessuna modifica

### Operator / maintainer notes

- nessun cambiamento nel funzionamento della macchina
- la sezione Top3D Detection Sensitivity resta visibile solo nel Job Tool Editor
  quando il pannello selezionato e' Top3D
- resta da validare su macchina il nome feature GigE `Top3DDetectionSensitivityFeature`
  introdotto in `3.0.5.5`

## Version 3.0.5.5

### Release metadata

- release date: `2026-07-09`
- previous baseline: `3.0.5.4`
- stato: su branch `feature/top3d-detection-sensitivity`, in attesa di collaudo su macchina

### Release intent

Macchina Top3D con testa Cognex L38-300R: dare la regolazione per-ricetta della Detection Sensitivity del sensore 3D dall'HMI e togliere il warning fuorviante `ExposureAuto` (feature assente sull'L38).

### Main functional integrations included

- fix rumore ExposureAuto sui sensori 3D: assenza feature loggata a Debug, non piu' Warn (l'esposizione era gia' applicata correttamente)
- Detection Sensitivity per-ricetta (`recipeParamTop3D/Top3DDetectionSensitivity`), regolabile dal Job Tool Editor solo quando il pannello selezionato e' Top3D (Read/Apply verso la testa + salvataggio ricetta)
- nome feature GigE configurabile (`Top3DDetectionSensitivityFeature`, default `DetectionSensitivity`)
- applicazione automatica alla testa 3D al caricamento ricetta (no-op se top non 3D o valore non configurato)

### Configuration-file notes

- Config.xml: nuovo `Top3DDetectionSensitivityFeature` (default `DetectionSensitivity`) — DA VERIFICARE col nome reale del sensore
- Ricetta: nuovo `recipeParamTop3D/Top3DDetectionSensitivity` (default 0 = non applicata)

### Database notes

- nessuna modifica

### Operator / maintainer notes

- collaudo obbligatorio su macchina: verificare che il nome feature scriva davvero sulla testa (altrimenti correggere `Top3DDetectionSensitivityFeature` in Config.xml, nessuna ricompilazione)
- la sezione "Detection sensitivity" compare solo aprendo il job 3D nel Job Tool Editor
- ricette senza valore (0) → comportamento identico a prima
- NON risolve l'errore display 3D `Index out of bounds` / `Measure ... Results.Item[0] is Nothing`: e' un problema di job VPP, separato

## Version 3.0.5.4

### Release metadata

- release date: `2026-07-08`
- previous baseline: `3.0.5.3`

### Release intent

Proseguire la Fase 0 dell'improvement plan riducendo attese bloccanti non critiche
senza modificare ciclo macchina, ricette, mapping I/O o logica VisionPro di produzione.

### Main functional integrations included

- `JobToolEditorViewModel`: l'attesa di completamento frame della Live Preview non usa
  piu' `Thread.Sleep(10)`; resta nello stesso `Task.Run`, ma ora usa `Task.Delay`
  cancellabile con lo stesso timeout esistente
- `SystemDiagnosticsService`: il primo campionamento del PerformanceCounter CPU non
  blocca piu' il refresh con `Thread.Sleep(100)`; il primo snapshot puo' mostrare 0%
  e il valore reale arriva dal refresh successivo
- `Documentation/improvement-plan-2026-07.md`: stato Fase 0 aggiornato con il batch
  3.0.5.4 e con nota esplicita sui tre `Thread.Sleep` ancora aperti in
  `ICognexJobManager`

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica

### Operator / maintainer notes

- nessun cambiamento nell'uso macchina
- la Live Preview mantiene gli stessi parametri di intervallo, pulse, exposure e timeout
- resta aperto il batch dedicato al lifecycle VisionPro/Cognex (`ICognexJobManager`):
  va provato su macchina reale perche' tocca start/stop/load job

## Version 3.0.5.3

### Release metadata

- release date: `2026-07-08`
- previous baseline: `3.0.5.2`

### Release intent

Rendere robusta la visualizzazione Top3D/Top2D quando la ricetta usa path VisionPro
annidati come `Measure.Height.InputImage` o `Measure.CogPixelMapTool1.OutputImage`.

### Main functional integrations included

- `CameraDisplayManager` non accede piu' direttamente a `record.SubRecords[subRecordKey]`
  per il display produzione
- il resolver display prova chiave esatta, path annidato separato da `.`, path senza
  prefisso wrapper/toolblock (`Measure.Height.InputImage` -> `Height.InputImage`),
  ricerca ricorsiva nel record LastRun e fallback al primo record contenente `ICogImage`
- `GetCurrentTopCameraImage()` non usa piu' `SubRecords[0]`, evitando errori
  `Index was outside the bounds of the array` su record Top3D che non hanno layout 2D standard
- il log display ora conserva stack/contesto completo se il refresh del record fallisce

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica di schema

### Operator / maintainer notes

- nessun cambiamento nell'uso macchina
- per Top3D sono validi sia path completi da QuickBuild sia path relativi al record LastRun:
  `Measure.Height.InputImage`, `Height.InputImage`, `LastRun.Height.InputImage`
- il warning `Top3D artifact export skipped: output 'Top3DRerenderResult'...` riguarda solo
  il salvataggio opzionale dei sidecar 3D, non la visualizzazione live/overview

## Version 3.0.5.2

### Release metadata

- release date: `2026-07-07`
- previous baseline: `3.0.5.1`

### Release intent

Completare la metrica Fase 0 "0 GC.Collect su percorsi runtime": rimozione del GC residuo di `ReleaseImageLock` dopo verifica che il caricamento immagini ricetta era gia' in-memory (`OnLoad`+`Freeze`) su tutti i percorsi.

### Main functional integrations included

- rimosso `GC.Collect()` da `ReleaseImageLock` (verificato: nessun file-lock WPF sulle immagini ricetta — `RecipeImagePathConverter` carica in-memory; retry di `TryDeleteFileAsync` come rete di sicurezza)
- catch del converter immagini ora logga a Debug le immagini illeggibili
- improvement plan aggiornato: riga GC.Collect a `[x]`; restano i batch VisionPro/Cognex e code ispezione (richiedono macchina)

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica

### Operator / maintainer notes

- la sostituzione/cancellazione dell'immagine ricetta non esegue piu' un full GC bloccante
- nessun cambiamento nell'uso della macchina

## Version 3.0.5.1

### Release metadata

- release date: `2026-07-07`
- previous baseline: `3.0.5.0`

### Release intent

Quarto batch leggero della Fase 0 dell'improvement plan: chiudere altri `async void`
non necessari nei command handler e rendere esplicito lo stato di avanzamento del piano.

### Main functional integrations included

- convertiti i command handler runtime di `MainViewModel` (`Start`, `Stop`, `Restart`,
  navigazione) al pattern wrapper `void` + core `async Task` con `SafeFireAndForget`
- convertiti i command handler configurazione allarmi scarto di
  `EjectionAlarmCardViewModel` al pattern osservabile/loggato
- convertita la Live View del `JobToolEditorViewModel` allo stesso pattern
- `DbQuatis` inizializza la vista tramite `Task` osservata invece di `async void`
- `Documentation/improvement-plan-2026-07.md` ora indica cosa e' completato,
  parziale o ancora aperto nella Fase 0

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica di schema

### Operator / maintainer notes

- nessun cambiamento nell'uso macchina
- la modifica riduce il rischio di eccezioni non osservate nei comandi UI senza cambiare
  i flussi operatore
- restano aperti i batch VisionPro/Cognex da provare su macchina reale e il bounding
  delle code ispezione con strategia senza drop silenzioso

## Version 3.0.5.0

### Release metadata

- release date: `2026-07-07`
- previous baseline: `3.0.4.9`

### Release intent

Terzo batch leggero della Fase 0: evitare il costo di conversione bitmap/byte array quando la coda di salvataggio immagini e' gia' piena.

### Main functional integrations included

- `SaveImage` mantiene la coda bounded da 32 elementi introdotta in `3.0.4.9`
- prima di creare `Bitmap`/`byte[]` per JPEG/BMP viene prenotato uno slot non bloccante con `SemaphoreSlim`
- se non c'e' spazio, il salvataggio best-effort viene scartato subito con warning `IMAGE_QUEUE_FULL|stage=pre-capture`, evitando CPU e memoria temporanea inutili
- il writer rilascia lo slot solo dopo la scrittura o l'errore su disco, cosi' la capienza limita queue + write in corso
- lo shutdown della coda evita di disporre risorse ancora usate se il writer non termina entro il timeout, lasciandole al teardown del processo e loggando `IMAGE_QUEUE_SHUTDOWN_TIMEOUT`

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica di schema

### Operator / maintainer notes

- nessun cambiamento nell'uso macchina
- se compaiono `IMAGE_QUEUE_FULL` con `stage=pre-capture`, il disco o la share immagini non stanno assorbendo il ritmo di salvataggio; ridurre percentuale immagini salvate o verificare storage/rete
- i risultati ispezione non vengono droppati da questa modifica

## Version 3.0.4.9

### Release metadata

- release date: `2026-07-07`
- previous baseline: `3.0.4.8`

### Release intent

Secondo batch della Fase 0: limitare la crescita di memoria delle code best-effort (scrittura immagini, mirror DB degli eventi) quando disco o MySQL sono lenti o in stallo, con drop sempre esplicito e tracciato.

### Main functional integrations included

- coda scrittura immagini limitata a 32 elementi; `TryAdd` non bloccante + warning `IMAGE_QUEUE_FULL` con contatore (primo drop e ogni 25)
- mirror DB eventi: coda limitata a 500 elementi + drainer singolo in background (al posto di un `Task.Run` per evento); warning throttled `EVENTLOG_DB_QUEUE_FULL` (max 1/min); `ApplicationEventLogger` ora `IDisposable` con drain finale allo shutdown
- le 5 code risultati ispezione restano invariate (bounding nel batch Cognex/VisionPro, senza drop silenzioso)

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica di schema

### Operator / maintainer notes

- con disco o DB in stallo prolungato la memoria resta limitata; si perdono solo scritture best-effort (immagini campionate, mirror DB di eventi che restano nei file di log), sempre con warning nei log
- se compaiono `IMAGE_QUEUE_FULL` ripetuti verificare disco/share immagini; se compaiono `EVENTLOG_DB_QUEUE_FULL` verificare MySQL
- nessun cambiamento nell'uso della macchina

## Version 3.0.4.8

### Release metadata

- release date: `2026-07-07`
- previous baseline: `3.0.4.7`

### Release intent

Primo batch della Fase 0 dell'improvement plan (stabilita' 24/7): eliminare i freeze UI da GC forzato al cambio/salvataggio ricetta, il codice morto rischioso, gli `async void` non protetti e gli hang silenziosi su lock config / DB lento.

### Main functional integrations included

- rimossi i `GC.Collect()` forzati su cambio e salvataggio ricetta (freeze UI); mantenuto e documentato quello di `ReleaseImageLock` (file-lock BitmapImage, follow-up in Fase 1)
- rimosso il metodo morto `Cls_InitializzeDb.Initialize1()` (177 righe, `async void`, schema divergente)
- convertiti gli `async void` a rischio reale (StatisticsView `ProcessInspectionResult`/`ResetCounters`, NavigationViewModel `ReloadMenu`/`OnExternalButtonClicked`, RecipeManagerViewModel `UpdatePermissionsFromStaticRoles`/`CheckDatabaseAsync`, `Cls_CheckUser.InitializeUserAsync`)
- timeout 30s sui lock di `AsyncConfigManagerXml` (log `CONFIG_LOCK_TIMEOUT`), `CommandTimeout` su init DB (60s) e `ALTER TABLE` (120s)
- catch silenzioso di `EventLogEntry` ora logga a Debug

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica di schema (rimosso solo codice morto mai eseguito)

### Operator / maintainer notes

- il cambio e il salvataggio ricetta non congelano piu' la UI per il GC forzato
- un DB lento all'avvio o un lock config bloccato ora producono un errore visibile nei log invece di un blocco silenzioso
- nessun cambiamento nell'uso della macchina
- rimandati al batch successivo (documentati nel changelog): `Thread.Sleep` in ICognexJobManager (Fase 1), code bounded senza drop silenzioso, restanti `async void` gia' protetti da try/catch

## Version 3.0.4.7

### Release metadata

- release date: `2026-07-07`
- previous baseline: `3.0.4.6`

### Release intent

Correggere il ritardo osservato sul secondo prodotto in fila: il count encoder associato alla fotocellula viene congelato nel ciclo polling I/O, non letto dopo il passaggio sul dispatcher UI.

### Main functional integrations included

- `IOEvent` contiene `EncoderCountsSnapshot` e `EncoderSnapshotTimestamp`
- `AdvantechDeviceManager.PollingLoopAsync` legge gli encoder prima degli input e passa lo snapshot agli eventi input
- `DigitalIOViewModel.OnInputChanged` risolve subito il main encoder count dallo snapshot evento e lo passa a `OnInputChangedCore`
- il fast-path `TimedFromPhotocell` usa lo stesso snapshot encoder evento
- il log `Photocell trigger accepted` mostra `snapshotAge` per diagnosticare quanto era distante la lettura encoder dal fronte input

### Configuration-file notes

- nessuna nuova chiave
- nessuna migrazione `machine_runtime_config.xml`
- nessuna modifica a quote intervento, pulse ms, trigger mode o mapping I/O

### Database notes

- nessuna modifica

### Operator / maintainer notes

- se il secondo prodotto resta in ritardo, controllare nei log `snapshotAge`, `target`, `actual` e `late=... mm`
- un ritardo residuo pari a piu' cicli polling indica il bisogno di latch/compare hardware encoder-fotocellula o di una modalita' trigger deterministica piu' stretta
- su TOP/SIDE molto vicini, la separazione temporale a velocita' linea alta puo' essere inferiore al tempo di polling software

## Version 3.0.4.6

### Release metadata

- release date: `2026-07-06`
- previous baseline: `3.0.4.5`

### Release intent

Rendere misurabile il ritardo reale tra quota encoder target e momento in cui il runtime esegue il punto camera/reject. La release serve per diagnosticare immagini nere o fuori centro quando passano prodotti in fila senza cambiare il comportamento macchina.

### Main functional integrations included

- `ProductInterventionState` registra `ReachedEncoderCount`, `ReachedTimeUtc`, `ReachedLateCounts` e `ReachedLateMm`
- `MachineController.UpdateEncoderPosition` calcola e logga target encoder, encoder reale e ritardo in count/mm per ogni punto intervento raggiunto
- `DigitalIOViewModel.ExecuteInterventionPointAsync` riporta gli stessi dati nel log `TRACK` del trigger camera; oltre 5 mm il log diventa warning operativo

### Configuration-file notes

- nessuna nuova chiave
- nessuna migrazione `machine_runtime_config.xml`
- nessuna modifica a quote, trigger mode, pulse ms o mapping I/O

### Database notes

- nessuna modifica

### Operator / maintainer notes

- se TOP e SIDE sono configurate a quote molto vicine, per esempio 364 mm e 376 mm, a circa 45 m/min la separazione temporale e' nell'ordine di 16 ms; un trigger software basato su polling encoder puo' arrivare tardi rispetto a una soluzione hardware compare
- se i nuovi log mostrano ritardi sopra 5 mm sui trigger camera, prima di modificare VisionPro verificare calibrazione encoder, quote macchina, posizione fisica camera e stato del modo trigger
- se VisionPro segnala `CogBlobTool...Item[0] is Nothing`, il job deve gestire il caso blob assente come NOK controllato, ma il timing immagine va comunque risolto lato macchina/configurazione

## Version 3.0.4.5

### Release metadata

- release date: `2026-07-06`
- previous baseline: `3.0.4.4`

### Release intent

Ridurre il rischio di trigger camera in ritardo quando passano prodotti ravvicinati e la UI e' occupata da diagnostica, VisionPro, log o salvataggio immagini.

### Main functional integrations included

- `DigitalIOViewModel.OnCounterChanged`: l'aggiornamento visuale del conteggio encoder viene accodato alla UI senza bloccare il callback encoder
- `DigitalIOViewModel.OnInterventionPointReached`: il punto intervento viene eseguito subito; lista eventi e audit UI vengono aggiornati in modo asincrono
- `DigitalIOViewModel.AddLogEntry` e `AddMachineOperationalEvent`: passaggio da `Dispatcher.Invoke()` a accodamento UI asincrono per evitare che il logging blocchi i pulse fisici

### Configuration-file notes

- nessuna nuova chiave
- nessuna migrazione `machine_runtime_config.xml`
- i parametri macchina da controllare restano `EncoderTemplates/MillimetersPerRevolution`, quote intervento camera, `PulseMs` e mapping `MachineOutputs`

### Database notes

- nessuna modifica

### Operator / maintainer notes

- il fix non cambia le quote: se l'immagine resta nera, verificare prima che la calibrazione encoder caricata sia quella corretta e che siano abilitate solo le camere realmente presenti nel job VisionPro
- da log allegato, quando piu' punti camera risultano raggiunti quasi insieme, controllare se il sistema sta processando quote gia' superate oppure se il `counts/mm` non corrisponde alla macchina reale
- per VisionPro, aggiungere nel job una guardia sul blob mancante (`blob count = 0`) per restituire NOK controllato invece di un errore `Item[0] is Nothing`

## Version 3.0.4.4

### Release metadata

- release date: `2026-07-06`
- previous baseline: `3.0.4.3`

### Release intent

Rendere piu' robusta la configurazione del classificatore ONNX dal pannello PC Diagnostics: validazione percorso modello, salvataggio piu' affidabile dei binding WPF e ricarica immediata del modello senza riavvio.

### Main functional integrations included

- `OnnxDefectClassifier.RefreshConfiguration()`: scarica l'eventuale sessione ONNX corrente e rilegge configurazione/modello dal file macchina
- `SystemDiagnosticsViewModel.SaveAiSettings`: forza l'aggiornamento dei binding prima del salvataggio, mostra il path del file macchina attivo e valida percorso `.onnx`, dimensioni input e normalizzazione quando il classificatore e' abilitato
- `SystemDiagnosticsView.xaml`: testo pannello aggiornato per indicare reload immediato
- nuova guida `Documentation/AI/ONNX_Defect_Classifier_Guide.md` con contratto modello, dataset, training offline, export ONNX e collaudo

### Configuration-file notes

- `machine_runtime_config.xml`: nessuna nuova chiave
- il pannello salva i campi ONNX gia' esistenti (`DefectClassifier*`) e normalizza il path modello espandendo eventuali variabili ambiente

### Database notes

- nessuna modifica

### Operator / maintainer notes

- se `Classificatore ONNX` e' abilitato, il salvataggio richiede un file `.onnx` esistente
- il modello viene ricaricato subito dopo `Salva`; non serve piu' riavviare l'app per cambiare modello/preprocessing
- il modello resta shadow-mode: non modifica scarto, contatori, allarmi o esito prodotto

## Version 3.0.4.3

### Release metadata

- release date: `2026-07-03`
- previous baseline: `3.0.4.2`

### Release intent

Permettere al tecnico di abilitare/configurare da pannello (pagina PC Diagnostics) tutti i parametri dei servizi AI di `machine_runtime_config.xml`, senza modificare il file XML a mano.

### Main functional integrations included

- nuova sezione "Controlli AI" in PC Diagnostics: toggle dei 7 servizi + parametri classificatore ONNX (percorso/versione/note modello, dimensioni input, normalizzazione, scala di grigi) + retention dati (misure/salute/training)
- salvataggio con conferma, riservato a Installer/Administrator; editor disabilitati per gli altri ruoli
- salvataggio tramite il Save atomico di `MachineConfigurationService` (con clamp dei valori)
- flag riapplicati a caldo ai servizi (`RefreshConfiguration`); classificatore ONNX attivo al riavvio
- ogni salvataggio tracciato su Events Monitor (`AI_SETTINGS_APPLY`)

### Configuration-file notes

- `machine_runtime_config.xml`: nessuna nuova chiave; il pannello scrive le chiavi AI esistenti

### Database notes

- nessuna modifica

### Operator / maintainer notes

- la sezione e' in fondo alla pagina PC Diagnostics, sotto "Ottimizzazione timing"
- "Ricarica" rilegge i valori dal file; "Salva" chiede conferma e riapplica subito i flag
- per il classificatore ONNX (modello e preprocessing) serve il riavvio dell'applicazione dopo il salvataggio

## Version 3.0.4.2

### Release metadata

- release date: `2026-07-03`
- previous baseline: `3.0.4.1`

### Release intent

Irrigidire sicurezza e tracciabilita' senza cambiare il ciclo macchina: default OPC UA piu' conservativi per nuove configurazioni, policy configurabile per comandi remoti, audit esteso e warning espliciti su credenziali/default TLS.

### Main functional integrations included

- `OpcUaConfig`: nuovi flag `RemoteCommandsEnabled`, `RemoteStartStopEnabled`, `RemoteRecipeChangeEnabled`, `AuditRemoteCommandsToDatabase`
- default nuovo `AutoAcceptUntrustedCertificates=false` per configurazioni OPC UA nuove; i file/record esistenti mantengono il valore gia' salvato
- `OpcUaClientService`: policy sui comandi remoti Start/Stop/Cambio ricetta; un comando disabilitato viene rifiutato con ACK negativo e audit, senza eccezioni runtime
- `Cls_CheckUser`: i seed utenti di default vengono creati solo se non esistono utenti interattivi; login con credenziale nota/default e credenziali default presenti vengono loggati come warning/audit
- audit aggiunto per `OPCUA_COMMAND_RECEIVED`, `OPCUA_COMMAND_REJECTED_BY_POLICY`, `OPCUA_CONFIGURATION_SAVED`, `IO_TIMING_APPLY`, `USER_CREATED`, `USER_DELETED`, warning MySQL default/TLS
- connection string centralizzata: i servizi toccati rispettano `MySqlConnection.SslDisabled` (`SslMode=Preferred` quando e' `false`)

### Configuration-file notes

- `Config.xml`: nessuna nuova chiave
- `OpcUaConfig.xml`: nuovi campi opzionali:
  - `RemoteCommandsEnabled` (default `true`)
  - `RemoteStartStopEnabled` (default `true`)
  - `RemoteRecipeChangeEnabled` (default `true`)
  - `AuditRemoteCommandsToDatabase` (default `true`)
  - `AutoAcceptUntrustedCertificates` per file nuovi parte da `false`
- i file OPC UA esistenti restano compatibili; i nuovi campi mancanti usano default runtime conservativi/additivi

### Database notes

- nessuna nuova tabella e nessuna nuova colonna
- `cfg_opcua_configuration` riceve/aggiorna le nuove chiavi setting OPC UA
- `audit_log` viene usata per i nuovi eventi sicurezza/audit

### Operator / maintainer notes

- per collaudo OPC UA in rete reale preferire certificati trusted e lasciare `AutoAcceptUntrustedCertificates=false`
- se un server MES/SCADA non deve comandare la macchina, disabilitare `RemoteCommandsEnabled`; per bloccare solo Start/Stop o cambio ricetta usare i flag dedicati
- i warning su credenziali default e MySQL TLS non bloccano la macchina: servono a rendere visibile la postura di sicurezza prima della messa in rete
- il comportamento operatore e il RunContinuous restano invariati salvo policy OPC UA configurate esplicitamente

## Version 3.0.4.1

### Release metadata

- release date: `2026-07-03`
- previous baseline: `3.0.4.0`

### Release intent

Separare, nel pairing MultiShot, la soglia "quanto attendo il risultato" (timeout) dalla soglia "quanto considero ancora lo stesso prodotto" (lag fisico), come da revisione.

### Main functional integrations included

- nuovo parametro config `RuntimeBindings.MultiShotCompanionMaxLagMs` (default 15000ms)
- `InspectionOrchestrator` usa un lag "stesso-prodotto" iniettato (fisico) invece del timeout di attesa
- valore cachato all'avvio in MainWindow (nessuna lettura config nel percorso caldo)

### Configuration-file notes

- `machine_runtime_config.xml`: nuovo campo `<MultiShotCompanionMaxLagMs>` (default 15000); upgrade schema automatico; letto una volta all'avvio

### Database notes

- nessuna modifica

### Operator / maintainer notes

- single-shot invariato (5s); MultiShot ora usa una soglia fisica dedicata (default 15s), regolabile in config
- il timeout di attesa risultato (30s) resta un tetto di sicurezza separato

## Version 3.0.4.0

## Version 3.0.4.0

### Release metadata

- release date: `2026-07-03`
- previous baseline: `3.0.3.9`

### Release intent

Irrigidire i punti piu' vicini al ciclo reale di ispezione emersi dalla revisione architetturale, senza cambiare l'uso operatore.

### Main functional integrations included

- InspectionOrchestrator: finestra di accettazione companion allineata al timeout (fix MultiShot marcato erroneamente `missing`)
- OnnxDefectClassifier: inferenza shadow eseguita off-thread + single-flight gate
- CounterManager: `Dispose` sincrono con flush finale a timeout (niente piu' `async void`), dispose del flusher
- MachineConfigurationService: `Save` atomico (temp + `File.Replace`) e serializzato con lock

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica di schema

### Operator / maintainer notes

- nessun cambiamento nell'uso della macchina; correzioni di robustezza runtime
- cleanup futuro noto (non incluso): rimozione del metodo morto `Cls_InitializzeDb.Initialize1()`

## Version 3.0.3.9

### Release metadata

- release date: `2026-07-03`
- previous baseline: `3.0.3.8`

### Release intent

Integrare due funzioni AI advisory a basso rischio: monitor prestazioni del ciclo ispezione e advisor prodotto/ricetta. Entrambe osservano solo dati gia' prodotti dal runtime, restano disabilitate di default e non modificano scarto, ricette, uscite, contatori o RunContinuous.

### Main functional integrations included

- nuovo `AiPerformanceMonitorService`: raccoglie tempi aggregati di display, validazione, contatori/allarmi/OPC UA, salvataggio decisione, advisory AI e queue DB; emette `AI_PERFORMANCE_ADVISORY` solo su finestra campioni e con cooldown
- nuovo `RecipeProductAdvisorService`: osserva esiti e difetti recenti della ricetta in produzione; se vede scarti consecutivi o fail-rate alto con difetto dominante, emette `AI_RECIPE_PRODUCT_MISMATCH_SUSPECTED`
- ricerca candidata ricetta solo in background e solo come suggerimento (`AI_RECIPE_CANDIDATE_FOUND`), riusando il matcher autoswitch esistente; nessun cambio automatico
- `MainWindow.ProcessInspectionGroupAsync`: aggiunti timestamp `Stopwatch` attorno ai blocchi esistenti senza cambiare ordine operativo
- `ServiceLocator`: registrati i due nuovi servizi come singleton, inizializzati con gli altri moduli AI
- default nuove configurazioni: `AiPerformanceMonitorEnabled=false` e `RecipeProductAdvisorEnabled=false`

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovi campi opzionali sotto `RuntimeBindings`:
  - `AiPerformanceMonitorEnabled` (default `false`)
  - `RecipeProductAdvisorEnabled` (default `false`)
- i file macchina esistenti mantengono i valori precedenti; se manca il campo, la serializzazione/upgrade schema lo aggiunge con default conservativo

### Database notes

- nessuna nuova tabella
- nessuna nuova colonna
- advisory loggate tramite `ApplicationEventLogger` / `tbllogevent` solo quando i rispettivi flag sono abilitati

### Operator / maintainer notes

- lasciare i due flag a `false` in produzione finche' non si vuole collaudare la diagnostica AI
- abilitare `AiPerformanceMonitorEnabled` per capire dove il ciclo perde tempo: display, validazione, contatori/allarmi/OPC UA, advisory o DB queue
- abilitare `RecipeProductAdvisorEnabled` per ricevere preallarmi su possibile formato/ricetta non coerente; il servizio non carica ricette e non blocca macchina
- eventuali suggerimenti di ricetta candidata devono essere confermati da operatore/Expert tramite i flussi gia' esistenti

## Version 3.0.3.8

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.7`

### Release intent

Rendere le funzioni AI piu' adatte a una baseline macchina di produzione: default conservativi, disattivazione sicura se la config non e' leggibile, retention per le nuove tabelle, warning DB controllati e metadati piu' chiari per training/ONNX/timing.

### Main functional integrations included

- default nuove configurazioni: `DataFoundationCaptureEnabled`, `ProcessControlEnabled`, `PredictiveMaintenanceEnabled`, `TrainingDataCollectionEnabled` e `IoTimingOptimizerEnabled` passano a `false`
- fail-safe config: se `machine_runtime_config.xml` non e' leggibile, i servizi AI restano disabilitati
- nuovo helper `AiDataRepositorySupport` per connessione MySQL condivisa, warning throttled e retention periodica
- `ProcessControlService`: limiti statistici calcolati sulla finestra precedente al campione corrente
- `PredictiveMaintenanceService`: conteggio campioni corretto anche per memoria
- `TrainingDataCollectionService`: `label.json` serializzato con Newtonsoft.Json, con `labelSource` e versione software
- `OnnxDefectClassifier`: metadati modello (`ModelVersion`, `ModelNotes`) e log `VISION_ML_SHADOW_DISAGREE` solo sui disaccordi
- `IoTimingOptimizerService`: backup reale del file macchina prima dell'apply e messaggio piu' esplicito sul refresh/reload runtime

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovi campi opzionali:
  - `DefectClassifierModelVersion`
  - `DefectClassifierModelNotes`
  - `AiInspectionMeasurementRetentionDays` (default 180)
  - `AiHealthSnapshotRetentionDays` (default 90)
  - `AiTrainingSampleRetentionDays` (default 0 = conserva)
- i file macchina esistenti mantengono i valori AI gia' presenti; il nuovo default vale per config nuove/rigenerate

### Database notes

- nessuna nuova tabella rispetto alla Fase AI gia' introdotta
- retention automatica sulle tabelle AI esistenti:
  - `tbl_inspection_measurements`
  - `tbl_health_snapshots`
  - `tbl_training_samples`
- gli errori DB AI sono loggati come warning throttled, senza bloccare produzione

### Operator / maintainer notes

- le funzioni AI restano advisory e non modificano scarto, contatori, pairing VisionPro o uscite fisiche
- per attivare una fase AI impostare esplicitamente il relativo flag a `true` e riavviare/ricaricare secondo il flusso della pagina configurazione
- prima di abilitare training/ONNX verificare politica salvataggio immagini, spazio disco e modello validato

## Version 3.0.3.7

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.6`

### Release intent

Correggere l'eccezione all'apertura della vista PC Diagnostics introdotta con la UI della Fase 5 (binding TwoWay su proprieta' di sola lettura via `Run.Text`).

### Main functional integrations included

- `TimingParameterItem.CurrentSummary` (sola lettura) + `TextBlock` OneWay al posto dei `Run`

### Configuration-file notes

- nessuna modifica

### Database notes

- nessuna modifica

### Operator / maintainer notes

- risolve `XamlParseException` all'apertura di PC Diagnostics; nessun cambiamento funzionale

## Version 3.0.3.6

## Version 3.0.3.6

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.5`

### Release intent

Completare la Fase 5 con la superficie UI: la vista PC Diagnostics mostra le raccomandazioni di timing e consente l'applicazione dei parametri trigger solo su conferma esplicita e con ruolo autorizzato.

### Main functional integrations included

- sezione "Ottimizzazione timing IO/encoder" in `SystemDiagnosticsView` (raccomandazioni + parametri applicabili)
- `SystemDiagnosticsViewModel`: comandi refresh/apply, gate ruolo (Installer/Administrator), conferma e feedback via dialog
- riuso del metodo guardrailato `IoTimingOptimizer.ApplyTimingParameter` (3.0.3.5)

### Configuration-file notes

- nessuna modifica a Config.xml o machine_runtime_config.xml

### Database notes

- nessuna nuova tabella; l'apply resta auditato in `tbllogevent` (`IO_TIMING_APPLY`)

### Operator / maintainer notes

- la pagina PC Diagnostics ora include, in fondo, la sezione timing: raccomandazioni a sinistra, parametri a destra
- il bottone "Applica" e' attivo solo per Installer/Administrator; ogni modifica chiede conferma, valida il range e salva il valore precedente come backup
- restano advisory-only i timeout risultato (costanti di codice)

## Version 3.0.3.5

## Version 3.0.3.5

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.4`

### Release intent

Analizzare il comportamento temporale della catena IO/encoder multi-camera e proporre ottimizzazioni, adattandosi al numero di camere del progetto VisionPro. Tocca il loop di controllo: advisory automatiche, applicazione solo su conferma operatore.

### Main functional integrations included

- nuovo `IoTimingOptimizerService`: lag risultato per ruolo, percentili/jitter, missing-rate, advisory su margine timeout
- alimentato in-memory da MainWindow (nessuna modifica al codice real-time)
- apply guardrailato dei parametri trigger (whitelist + range + backup + audit) via `ApplyTimingParameter`
- flag `RuntimeBindings.IoTimingOptimizerEnabled` (default abilitato)

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovo campo `<IoTimingOptimizerEnabled>` (default `true`); i parametri trigger vengono modificati solo tramite l'apply guardrailato su conferma

### Database notes

- nessuna nuova tabella o colonna
- advisory su `tbllogevent` (codice `IO_TIMING_ADVISORY`); audit apply come `IO_TIMING_APPLY`

### Operator / maintainer notes

- l'analisi e' advisory: descrive il timing per camera e segnala margini risicati/risultati mancanti
- **nessun parametro del loop di controllo viene cambiato in automatico**: ogni modifica passa da `ApplyTimingParameter` (whitelist, range di sicurezza, backup del valore precedente, audit) e richiede conferma esplicita di un operatore autorizzato
- i timeout risultato sono costanti di codice: per essi la Fase 5 avvisa soltanto (richiedono modifica codice, non config)
- per disattivare l'analisi impostare `IoTimingOptimizerEnabled` a `false` e riavviare
- resta da collegare la UI di conferma (bottone "Applica") che invoca il metodo di apply gia' pronto

## Version 3.0.3.4

## Version 3.0.3.4

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.3`

### Release intent

Predisporre l'inferenza ML sulle immagini (Fase 3): un classificatore ONNX gira in shadow-mode accanto a VisionPro e la sua predizione viene confrontata e loggata, senza incidere sullo scarto, in attesa di validazione sul campo.

### Main functional integrations included

- nuova interfaccia `IDefectClassifier` + `OnnxDefectClassifier` (ONNX Runtime)
- dipendenza `Microsoft.ML.OnnxRuntime 1.19.2` (nativa x64)
- shadow-mode: confronto predizione ML vs esito a regole loggato come advisory
- preprocessing configurabile; flag/parametri `RuntimeBindings.DefectClassifier*` (default disabilitato)

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovi campi opzionali `DefectClassifierEnabled/ModelPath/InputWidth/InputHeight/Grayscale/NormalizeMean/NormalizeStd` (default disabilitato); letti una volta all'avvio

### Database notes

- nessuna nuova tabella o colonna

### Operator / maintainer notes

- funzione **disabilitata di default**: senza modello e' un no-op, zero impatto
- per attivarla: fornire un modello `.onnx`, impostare `DefectClassifierModelPath` e `DefectClassifierEnabled=true`, poi riavviare
- in questa fase e' **solo advisory** (shadow-mode): il classificatore NON decide lo scarto; confronta e logga (`VISION_ML_SHADOW`)
- assunzioni dello scaffold da verificare col modello reale: layout tensore NCHW e classe 0 = pezzo conforme; preprocessing regolabile da config
- il modello deve essere addestrato offline sul dataset raccolto nella Fase 3a

## Version 3.0.3.3

## Version 3.0.3.3

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.2`

### Release intent

Preparare il dataset per il futuro classificatore difetti (Fase 3): agganciare l'etichetta di esito/difetto alle immagini gia' salvate, accumulando nel tempo un set etichettato per l'addestramento offline. Non addestra alcun modello.

### Main functional integrations included

- nuovo `TrainingDataCollectionService`: sidecar `label.json` per pezzo + indice `tbl_training_samples`
- etichetta derivata dall'esito ispezione (OK/NOK) e dai difetti rilevati
- alimentato da `MainWindow` solo quando le immagini del pezzo vengono salvate
- flag di configurazione `RuntimeBindings.TrainingDataCollectionEnabled` (default abilitato)

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovo campo opzionale `<TrainingDataCollectionEnabled>` (default `true`); letto una volta all'avvio

### Database notes

- **nuova tabella** `tbl_training_samples` (indice queryable dei campioni etichettati)
- create automatica con `CREATE TABLE IF NOT EXISTS`; nessuna migrazione manuale

### Operator / maintainer notes

- il dataset autorevole per l'addestramento e' il `label.json` accanto alle immagini di ogni pezzo salvato
- la raccolta segue il salvataggio immagini esistente (a percentuale): non aumenta il numero di immagini salvate
- per disattivare impostare `TrainingDataCollectionEnabled` a `false` e riavviare
- la Fase 3 completa (inferenza del modello) richiedera' un modello ONNX addestrato su questo dataset, ancora da produrre offline

## Version 3.0.3.2

## Version 3.0.3.2

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.1`

### Release intent

Aggiungere la manutenzione predittiva (Fase 2 del piano AI): usare la serie storica di salute PC (Fase 0) per anticipare i degradi hardware con statistica trasparente. Advisory, senza modifiche alla logica di ispezione.

### Main functional integrations included

- nuovo `PredictiveMaintenanceService`: proiezione riempimento disco (ore-al-limite), EWMA CPU/RAM sostenute
- alimentato in-memory da `HealthSnapshotService` (nessuna lettura DB)
- avvisi loggati come Warning nell'Events Monitor ed esposti via evento `MaintenancePredicted` / `GetActiveAlerts()`
- flag di configurazione `RuntimeBindings.PredictiveMaintenanceEnabled` (default abilitato)

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovo campo opzionale `<PredictiveMaintenanceEnabled>` (default `true`); letto una volta all'avvio (cambiarlo richiede riavvio)

### Database notes

- nessuna nuova tabella o colonna
- gli avvisi predittivi sono registrati in `tbllogevent` a livello Warning (codice evento `PREDICTIVE_MAINTENANCE`)

### Operator / maintainer notes

- gli avvisi compaiono nell'Events Monitor come Warning: sono anticipazioni, NON guasti in corso
- l'avviso piu' utile e' la proiezione disco ("il disco raggiungera' il 95% tra ~X ore"): pianificare pulizia/archiviazione
- per disattivare impostare `PredictiveMaintenanceEnabled` a `false` e riavviare
- la stima temporale assume la cadenza degli snapshot salute (60s); e' un ordine di grandezza, non una scadenza esatta

## Version 3.0.3.1

## Version 3.0.3.1

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.3.0`

### Release intent

Aggiungere il controllo statistico di processo (Fase 1 del piano AI): usare le misure strutturate della Fase 0 per rilevare le derive di processo prima che generino scarto, con statistica trasparente. Funzionalita' advisory, senza modifiche alla logica di ispezione.

### Main functional integrations included

- nuovo `ProcessControlService`: SPC in-memory per chiave ricetta|camera|feature (limiti I-MR, regole di Nelson, carta EWMA, stima pezzi-al-limite)
- alimentato in-memory da `MeasurementCaptureService` (nessuna lettura DB nel percorso caldo)
- le derive rilevate sono loggate come Warning nell'Events Monitor ed esposte via evento `DriftDetected` / `GetActiveDrifts()`
- flag di configurazione `RuntimeBindings.ProcessControlEnabled` (default abilitato)

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovo campo opzionale `<ProcessControlEnabled>` (default `true`); letto una volta all'avvio (cambiarlo richiede riavvio)

### Database notes

- nessuna nuova tabella o colonna
- le derive di processo sono registrate in `tbllogevent` a livello Warning (codice evento `PROCESS_DRIFT`)

### Operator / maintainer notes

- le derive compaiono nell'Events Monitor come Warning: sono avvisi anticipati, NON scarti e NON allarmi bloccanti
- il badge errori (in alto) resta dedicato ai soli Critical: le derive non lo attivano
- per disattivare l'SPC impostare `ProcessControlEnabled` a `false` e riavviare
- i limiti di controllo sono statistici (derivati dal processo), non le tolleranze di ricetta: una deriva segnalata indica che il processo si sta spostando, anche se i pezzi sono ancora conformi

## Version 3.0.3.0

## Version 3.0.3.0

### Release metadata

- release date: `2026-07-02`
- previous baseline: `3.0.2.4`

### Release intent

Introdurre la fondazione dati per l'integrazione AI (Fase 0 del piano `Documentation/AI/AI_Integration_Architecture.md`): persistere in forma strutturata i valori misurati di ispezione e gli snapshot di salute del PC, abilitando le fasi successive (controllo statistico di processo, manutenzione predittiva) senza modificare la logica di ispezione esistente.

### Main functional integrations included

- nuovo `MeasurementCaptureService`: dopo ogni ispezione estrae dai `ValidationResult` i valori misurati per feature/camera e li scrive in `tbl_inspection_measurements` (fire-and-forget, non bloccante)
- nuovo `HealthSnapshotService`: ogni 60s scrive uno snapshot CPU/RAM/disco in `tbl_health_snapshots`
- due nuovi repository (`InspectionMeasurementRepository`, `HealthSnapshotRepository`) con lo stesso pattern resiliente di `EventLogRepository`
- flag di configurazione `RuntimeBindings.DataFoundationCaptureEnabled` (default abilitato) per attivare/disattivare la cattura
- registrazione dei due servizi in `ServiceLocator`

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nuovo campo opzionale `<DataFoundationCaptureEnabled>` (default `true`). I file esistenti privi del campo restano abilitati per default del modello; il campo viene aggiunto in fase di upgrade schema
- il flag e' letto una volta all'avvio: cambiarlo richiede il riavvio dell'applicazione

### Database notes

- **due nuove tabelle**: `tbl_inspection_measurements` e `tbl_health_snapshots`
- create automaticamente all'avvio con `CREATE TABLE IF NOT EXISTS`; nessuna migrazione manuale richiesta
- nessuna modifica alle tabelle esistenti
- se MySQL non e' disponibile, la cattura degrada silenziosamente senza impatto su produzione

### Operator / maintainer notes

- funzionalita' completamente additiva: produzione, scarto e contatori sono invariati
- per disattivare la raccolta dati impostare `DataFoundationCaptureEnabled` a `false` in `machine_runtime_config.xml` e riavviare
- le nuove tabelle crescono nel tempo; la strategia di retention/pulizia sara' definita nelle fasi AI successive

## Version 3.0.2.4

### Release metadata

- release date: `2026-07-01`
- previous baseline: `3.0.2.3`

### Release intent

Allineare i contatori difetto ai contatori produzione eliminando il doppio incremento per ogni pezzo non conforme.

### Main functional integrations included

- `CounterManager.ProcessInspectionAsync` aggiorna solo i contatori pezzo `Total`, `Good` e `NoGood`
- i contatori per-difetto restano incrementati dal flusso validator tramite `CounterManager.IncrementDefectAsync`
- rimosso il metodo privato ridondante `UpdateDefectCounters`, che reincrementava gli stessi difetti alla fine dell'ispezione
- aggiunta deduplica runtime per ciclo pezzo: la stessa chiave difetto normalizzata puo aggiornare il messaggio operatore piu volte, ma incrementa il contatore numerico una sola volta
- il parametro `defects` di `ProcessInspectionAsync` resta disponibile per compatibilita dei call site esistenti, ma non e' piu usato per incrementare i contatori difetto
- aggiunta documentazione tecnica dedicata al flusso autorevole dei contatori

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nessuna modifica

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione schema richiesta
- se la macchina ha prodotto con la release precedente, i contatori difetto storici possono essere gia gonfiati; dopo il deploy e' consigliato un reset contatori autorizzato se si vuole ripartire con statistiche pulite

### Operator / maintainer notes

- `Total` aumenta una volta per pezzo ispezionato
- `Good` o `NoGood` aumenta una volta per pezzo in base all'esito finale
- ogni contatore difetto aumenta al massimo una volta quando quel difetto viene realmente rilevato dal validator del pezzo
- se la pagina Counters mostra ancora valori non coerenti dopo il deploy, azzerare i contatori da utente autorizzato e riprovare con una sequenza controllata di pezzi Good/NOK

## Version 3.0.2.3

### Release metadata

- release date: `2026-07-01`
- previous baseline: `3.0.2.2`

### Release intent

Migliorare l'usabilita su monitor industriali da 15 pollici e rendere piu chiara la calibrazione esposizione camera nella Live Preview tecnica.

### Main functional integrations included

- `RecipeManagerView.xaml`: le tre colonne principali non usano piu larghezze fisse `300/*/440`, ma proporzioni adattive con limiti min/max piu adatti a schermi compatti
- `RecipeManagerView.xaml`: ridotti padding, margini, dimensioni immagini/card e vincoli rigidi nelle card ricetta, nelle sezioni prodotto e nell'immagine aggiuntiva
- `JobToolEditorView.xaml`: la toolbar tecnica ora usa `WrapPanel`, cosi i comandi vanno a capo invece di uscire fuori su display piccoli
- `JobToolEditorViewModel.cs`: aggiunto comando `Read exposure` e testo separato `Current: ... us` per leggere il valore esposizione reale della camera prima di modificarlo
- `JobToolEditorViewModel.cs`: dopo `Apply exposure`, la HMI rilegge il valore effettivo dalla camera e aggiorna sia il campo editabile sia l'indicazione corrente
- `ServerMessage`, `Localization` e `scripts/UpdateRuntimeLanguageFiles.ps1`: aggiunte label EN/IT per lettura esposizione e stati operativi

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: nessuna nuova chiave rispetto a `3.0.2.2`
- resta usata la chiave opzionale `RuntimeBindings.LivePreviewExposureUs`

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- su 15 pollici la pagina ricette resta a tre colonne ma con proporzioni piu compatte e card meno rigide
- prima di cambiare esposizione, premere `Read` per leggere il valore reale impostato nella camera selezionata
- il valore `Current` mostra l'esposizione effettiva letta dalla camera; il campo `Exposure (us)` e' il valore che verra scritto con `Apply exposure`

## Version 3.0.2.2

### Release metadata

- release date: `2026-07-01`
- previous baseline: `3.0.2.1`

### Release intent

Rendere visibile nella finestra `Live Preview` lo stesso record immagine usato dai display produzione e aggiungere la regolazione esposizione camera durante la calibrazione tecnica.

### Main functional integrations included

- `JobToolEditorViewModel.cs`: la finestra live risolve il sub-record display usando la stessa configurazione `Config.xml -> LasRunParam` usata dalle viste produzione (`LastRunView1/2/3`)
- `JobToolEditorViewModel.cs`: se il sub-record non e' disponibile, la preview degrada al record completo e poi al primo `ICogImage` trovato nell'albero record
- `JobToolEditorViewModel.cs`: aggiunto comando tecnico per leggere/applicare l'esposizione camera e salvarla nella configurazione runtime
- `IgigaCameraAccess.cs`: aggiunti helper per leggere/scrivere `ExposureTime`, `ExposureTimeAbs` o `ExposureTimeRaw`, disattivando `ExposureAuto` quando disponibile
- `JobToolEditorView.xaml`: aggiunti campo `Exposure (us)`, pulsante `Apply exposure` e stato operazione nella barra live
- `ServerMessage`, `Localization` e `scripts/UpdateRuntimeLanguageFiles.ps1`: aggiunte label EN/IT per i nuovi controlli esposizione

### Configuration-file notes

- `Config.xml`: nessuna modifica
- `machine_runtime_config.xml`: aggiunta chiave opzionale `RuntimeBindings.LivePreviewExposureUs`
- default: `0`, cioe' nessuna esposizione forzata all'avvio live

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- la Live Preview continua a usare il loop tecnico `RunOnce -> pulse IO -> display result`; non cambia il ciclo produzione
- per calibrare la camera, inserire il tempo esposizione in microsecondi e premere `Apply exposure`; il valore valido resta salvato per le sessioni successive
- se il popup resta blu, verificare che `Config.xml -> LasRunParam` punti a un record immagine esistente nel job VisionPro

## Version 3.0.2.1

### Release metadata

- release date: `2026-07-01`
- previous baseline: `3.0.2.0`

### Release intent

Correggere la Live Preview hardware-trigger quando il job VisionPro produce risultati mentre la macchina e' intenzionalmente ferma per editing.

### Main functional integrations included

- `MainWindow.xaml.cs`: aggiunto stato globale `IsJobEditorLivePreviewActive` per distinguere i run tecnici della Live Preview dai run produzione
- `MainWindow.xaml.cs`: `UserResultAvailable` e `JobStopped` non svuotano code, non cambiano stato macchina e non scrivono audit produzione mentre la Live Preview del Job Editor e' attiva
- `JobToolEditorViewModel.cs`: il loop live attende il completamento reale del job prima di leggere il record
- `JobToolEditorViewModel.cs`: il record live viene preso prima dalla `UserResult` queue del `CogJobManager`, poi come fallback dal `CreateLastRunRecord()` del tool VisionPro

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna nuova chiave in `machine_runtime_config.xml`

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- durante la Live Preview non devono piu comparire log ripetitivi `Pending VisionPro results cleared` / `VisionPro job stopped` causati dai run tecnici
- la finestra live deve aggiornarsi usando il `LastRun` del job selezionato dopo ogni impulso IO

## Version 3.0.2.0

### Release metadata

- release date: `2026-07-01`
- previous baseline: `3.0.1.9`

### Release intent

Rendere funzionante la Live Preview tecnica del `Job Tool Editor` anche quando VisionPro non e' in `RunContinuous` e la camera lavora con trigger hardware esterno.

### Main functional integrations included

- `JobToolEditorViewModel.cs`: il loop Live Preview ora arma il job VisionPro selezionato con un run singolo prima di pulsare l'uscita IO camera
- `JobToolEditorViewModel.cs`: dopo il completamento del run, la finestra live viene aggiornata direttamente con il record `ShowLastRunRecordForUserQueue -> LastRun`, o con il primo `LastRun` compatibile trovato
- `JobToolEditorViewModel.cs`: aggiunto timeout di sicurezza del run live; in caso di mancata acquisizione il job viene fermato e viene loggato un warning chiaro
- `JobToolEditorViewModel.cs`: mantenuta la scelta di non usare `CogRecordDisplay.StartLiveDisplay()` e di non reinizializzare la FIFO live VisionPro

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna nuova chiave in `machine_runtime_config.xml`
- restano validi `RuntimeBindings.LivePreviewIntervalMs` e `RuntimeBindings.LivePreviewPulseMs`

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- la Live Preview puo funzionare anche con stato macchina `Stopped`, perche esegue solo il job selezionato in modo tecnico e non avvia la produzione
- il trigger fisico viene ancora risolto dal mapping macchina e riportato inattivo a stop/chiusura finestra
- se non arriva immagine, verificare camera, cablaggio trigger, durata impulso e acquisizione VisionPro; il log mostrera un timeout `LIVE_IO`

## Version 3.0.1.9

### Release metadata

- release date: `2026-07-01`
- previous baseline: `3.0.1.8`

### Release intent

Semplificare la pagina `PCIE settings` per commissioning e manutenzione macchina, riducendo le schede principali visibili e portando encoder, tachimetro, I/O macchina, binding runtime, punti intervento e backup configurazione dentro una sola vista `Machine Setup`.

### Main functional integrations included

- `DigitalIOControl.xaml`: la vecchia scheda top-level `Encoder Setup` resta presente ma nascosta, per non cambiare la struttura runtime e mantenere una via di fallback durante manutenzione
- `DigitalIOControl.xaml`: la vista `Machine Setup` ora mostra in alto il riepilogo encoder, velocita linea, pezzi/min, zero macchina, quote evento, configurazione simulazione, calibrazione tachimetro e diagnostica encoder
- `DigitalIOControl.xaml`: nella stessa vista restano disponibili il riepilogo `intervention point -> physical output`, le tabelle modificabili `Machine outputs`, `Machine inputs`, `Encoder and tracking` e la sezione `Backup / restore`
- `Localization/messages_eng.json`, `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1`: label aggiornate da `Machine I/O Setup` a `Machine Setup`

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica schema a `machine_runtime_config.xml`
- la pagina continua a leggere e salvare gli stessi dati macchina gia presenti

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- l'operatore/manutentore trova ora encoder, I/O e punti intervento nella stessa pagina `Machine Setup`
- la logica runtime di trigger, encoder, scarto, multishot e binding fisici non e' stata modificata
- le vecchie sezioni complete restano raggruppate nella parte avanzata della stessa pagina

## Version 3.0.1.8

### Release metadata

- release date: `2026-06-30`
- previous baseline: `3.0.1.7`

### Release intent

Eliminare un errore non critico in chiusura applicazione quando il timer di diagnostica PC richiama `RefreshNowAsync()` mentre il servizio `SystemDiagnosticsService` e' gia in fase di shutdown/dispose.

### Main functional integrations included

- `SystemDiagnosticsService.cs`: flag `_isShuttingDown` e `_isDisposed` resi `volatile` per lettura coerente tra callback timer e thread di shutdown
- `SystemDiagnosticsService.cs`: `Start()`, `OnRefreshTimerTick()` e `ConfigureTimer()` ora escono subito se il servizio e' in shutdown o gia disposto
- `SystemDiagnosticsService.cs`: `RefreshNowAsync()` gestisce il caso di semaforo non disponibile durante shutdown senza generare log di errore applicativo
- `SystemDiagnosticsService.cs`: il semaforo `_refreshLock` non viene piu disposto durante `Dispose()`, evitando race con callback asincrone ancora in ritorno dal `finally`

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- il log `Unhandled exception in OnRefreshTimerTick` con `ObjectDisposedException: Il semaforo e' stato eliminato` non deve piu comparire durante chiusura HMI
- il comportamento della pagina diagnostica PC e del refresh periodico resta invariato durante uso normale

## Version 3.0.1.7

### Release metadata

- release date: `2026-06-30`
- previous baseline: `3.0.1.6`

### Release intent

Correggere la Live Preview hardware-trigger del Job Tool Editor eliminando la dipendenza da `CogRecordDisplay.StartLiveDisplay()` e dalla FIFO live VisionPro.

### Main functional integrations included

- `JobToolEditorViewModel.cs`: `LiveView()` non chiama piu `StartLiveDisplay()` e non richiede piu `IsLiveDisplayRunning`
- `JobToolEditorViewModel.cs`: la Live Preview viene considerata attiva quando il loop IO hardware-trigger viene avviato correttamente
- `JobToolEditorViewModel.cs`: se il mapping dell'uscita trigger non viene trovato, la HMI ripristina il modo normale invece di lasciare il runtime in hold
- `JobToolEditorViewModel.cs`: lo stop della preview chiama `StopLiveDisplay()` solo se una live FIFO VisionPro era stata effettivamente avviata, evitando warning su controlli gia rilasciati

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna nuova chiave in `machine_runtime_config.xml`
- restano validi `LivePreviewIntervalMs` e `LivePreviewPulseMs` introdotti in `3.0.1.6`

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- la Live Preview tecnica ora pulsa l'uscita camera senza tentare la reinizializzazione FIFO live VisionPro
- i log `FIFO non compatibile`, `Tentativo di reinizializzazione hardware` e `FIFO di acquisizione non inizializzata` non devono piu comparire quando si usa questa modalita
- il ciclo produzione, il MultiShot, l'encoder e lo scarto restano invariati

## Version 3.0.1.6

### Release metadata

- release date: `2026-06-30`
- previous baseline: `3.0.1.5`

### Release intent

Rendere la `Live Preview` del Job Tool Editor utilizzabile con camere configurate a trigger hardware esterno, senza modificare il flusso produzione, il MultiShot o il `RunContinuous`.

### Main functional integrations included

- `JobToolEditorViewModel.cs`: la Live Preview avvia ancora il display VisionPro, ma il loop IO ora pulsa l'uscita fisica della camera solo se il live display risulta realmente attivo
- `JobToolEditorViewModel.cs`: l'uscita viene riportata allo stato inattivo in `finally`, durante `Stop Preview` e anche quando viene chiusa direttamente la finestra live
- `JobToolEditorViewModel.cs`: risoluzione robusta del trigger camera con alias `Left/Side` e `Right/Rear`, usando `InterventionPoints`, `MachineMultiShotTrigger` e `MachineOutputs`
- `MachineRuntimeConfiguration.cs`: aggiunti parametri macchina `LivePreviewIntervalMs` e `LivePreviewPulseMs`
- `MachineConfigurationService.cs`: aggiunti default e upgrade schema runtime per i nuovi parametri live
- `JobToolEditorView.xaml`: aggiunto campo `Pulse (ms)` accanto all'intervallo live
- `ServerMessageStructure.cs`, `Localization/messages_eng.json`, `Localization/messages_ita.json`, `scripts/UpdateRuntimeLanguageFiles.ps1`: label live preview aggiornate nel server messaggi

### Configuration-file notes

- nessuna modifica a `Config.xml`
- `machine_runtime_config.xml` aggiunge due campi opzionali in `RuntimeBindings`: `LivePreviewIntervalMs` default `1000` e `LivePreviewPulseMs` default `20`
- i vecchi file senza questi campi restano caricabili; il runtime usa fallback sicuri e salva i campi al prossimo update schema/salvataggio configurazione
- `ConfigurationTemplates/machine_runtime_config.template.xml` aggiornato con i nuovi default

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- la Live Preview del Job Tool Editor e' una funzione tecnica: ferma il run continuo se necessario, apre il live e simula lo scatto camera pulsando la stessa uscita fisica usata dalla macchina
- `Interval (ms)` definisce il tempo tra due inizi impulso live; `Pulse (ms)` definisce per quanto tempo l'uscita resta attiva
- il funzionamento normale della macchina, i punti intervento, l'encoder, lo scarto e il MultiShot restano invariati

## Version 3.0.1.5

### Release metadata

- release date: `2026-06-29`
- previous baseline: `3.0.1.4`

### Release intent

Correggere la risoluzione dei segnali nella tabella `Intervention point to physical output` e ridurre il rumore WPF sui `ComboBoxItem`.

### Main functional integrations included

- `DigitalIOViewModel.cs`: `ResolveOutputSignalByCode` ora normalizza gli spazi dei `SignalCode`
- `DigitalIOViewModel.cs`: se esistono duplicati con lo stesso `SignalCode`, il resolver preferisce il segnale reale con `Board` e `Channel` compilati
- `DigitalIOViewModel.cs`: aggiunti alias compatibili `Left/Side` e `Right/Rear` per i trigger camera, cosi un punto `OUT_CAMERA_RIGHT_TRIGGER` puo risolversi sull'uscita fisica `OUT_CAMERA_REAR_TRIGGER` quando la macchina usa Rear come vista Right
- `DigitalIOViewModel.cs`: la tabella riepilogo usa lo stesso resolver anche dopo modifiche alle tabelle I/O
- `App.xaml`: aggiunto stile globale sicuro per `ComboBoxItem`, evitando i warning binding WPF su `HorizontalContentAlignment` e `VerticalContentAlignment`

### Configuration-file notes

- nessuna modifica schema `Config.xml`
- nessuna nuova chiave in `machine_runtime_config.xml`
- nessuna modifica automatica delle righe `MachineOutputs`; la correzione risolve meglio le righe gia presenti

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- la tabella `Intervention point to physical output` prende i dati da `InterventionPoints` e li risolve contro `MachineOutputs` / `MachineInputs`
- campi vuoti nel riepilogo indicavano un `SignalCode` non trovato, un duplicato test vuoto scelto prima della riga reale, oppure il caso `RIGHT`/`REAR`
- dopo questa release il riepilogo deve mostrare il canale reale quando esiste una riga fisica valida nella tabella `Machine outputs`

## Version 3.0.1.4

### Release metadata

- release date: `2026-06-29`
- previous baseline: `3.0.1.3`

### Release intent

Rendere piu evidente che la pagina unificata `Machine I/O Setup` contiene davvero tutta la precedente `Machine Configuration`, incluse le tabelle modificabili `MachineInputs`, `MachineOutputs`, encoder e backup. La correzione nasce dal feedback operativo: le tabelle erano integrate, ma chiuse dentro un expander avanzato e quindi potevano sembrare eliminate.

### Main functional integrations included

- `DigitalIOControl.xaml`: la sezione `Complete machine configuration` ora si apre di default nella pagina unificata
- `DigitalIOControl.xaml`: le tabelle `Machine outputs` e `Machine inputs` mostrano anche la colonna `Signal type`, allineandosi alla precedente vista `Machine Configuration`
- `DigitalIOControl.xaml`: riportata nella pagina unificata anche la tabella `Additional runtime bindings`
- `DigitalIOControl.xaml`: riportati i filtri rapidi sulle tabelle segnali (`runtime`, `unassigned`, `real`)
- `DigitalIOControl.xaml`: aggiunto testo guida vicino alle tabelle editabili per spiegare modifica celle, aggiunta riga, cancellazione riga e salvataggio con `Save config`
- `scripts/UpdateRuntimeLanguageFiles.ps1`: aggiornate le label inglese/italiano della sezione completa e della guida editabile
- `Localization/messages_eng.json` / `Localization/messages_ita.json`: rigenerati tramite script lingua

### Configuration-file notes

- nessuna modifica schema `Config.xml`
- nessuna nuova chiave in `machine_runtime_config.xml`
- le tabelle editabili continuano a scrivere gli stessi blocchi esistenti: `MachineInputs`, `MachineOutputs`, `EncoderTemplates`, `RuntimeBindings`, `AdditionalRuntimeBindings`

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- `Machine inputs`, `Machine outputs` e `Additional runtime bindings` non sono stati eliminati: sono nella pagina `Machine I/O Setup` sotto `Complete machine configuration`
- le righe sono modificabili come prima; per aggiungere un segnale usare l'ultima riga vuota del DataGrid
- dopo ogni modifica premere `Save config` per scrivere il file macchina attivo e ricaricare il runtime
- runtime invariato: fotocellula -> PRODUCT_ZERO -> encoder/timer -> intervention point -> SignalCode -> MachineOutputs -> canale fisico

## Version 3.0.1.3

### Release metadata

- release date: `2026-06-29`
- previous baseline: `3.0.1.2`

### Release intent

Ridurre le schede operative nella pagina `PCIE settings` e rendere piu professionale il commissioning I/O senza cambiare la logica macchina gia validata. La nuova pagina unificata mette insieme punti intervento, binding runtime, salvataggio configurazione, output fisici e tabelle avanzate.

### Main functional integrations included

- `DigitalIOControl.xaml`: la tab `Intervention Points` diventa `Machine I/O Setup`
- `DigitalIOControl.xaml`: aggiunto header operativo con `Save config`, `Load config`, `Create backup` e apertura file
- `DigitalIOControl.xaml`: aggiunta tabella in sola lettura `Intervention point to physical output`
- `DigitalIOControl.xaml`: la vecchia tab `Machine Configuration` viene nascosta dal tab principale
- `DigitalIOControl.xaml`: aggiunta sezione espandibile `Advanced machine configuration` con:
  - binding runtime
  - output macchina
  - input macchina
  - encoder
  - backup / restore
- `Models/MachineHardwareTemplate.cs`: aggiunto modello UI `InterventionSignalAssignment`
- `DigitalIOViewModel.cs`: aggiunta collezione calcolata `InterventionSignalAssignments`, aggiornata quando cambiano punti, segnali, encoder o mapping
- `scripts/UpdateRuntimeLanguageFiles.ps1`: aggiunte chiavi lingua per la nuova vista e rigenerati i file lingua
- `Docs/Commissioning/09_Machine_IO_Setup_Unified_View.md`: nuova guida pratica di commissioning
- `Documentation/MachineHardware/pci-io-unified-setup-2026-06-29.md`: nuova documentazione tecnica del flusso

### Configuration-file notes

- nessuna modifica schema `Config.xml`
- nessuna nuova sezione in `machine_runtime_config.xml`
- la vista lavora sugli stessi dati gia esistenti:
  - `MachineInputs`
  - `MachineOutputs`
  - `EncoderTemplates`
  - `RuntimeBindings`
  - `InterventionPoints`
  - `MachineMultiShotTrigger`

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- la differenza pratica e':
  - `InterventionPoints` = quando e quale azione fare sul prodotto
  - `Machine Configuration` = dove esce fisicamente il segnale e con quale polarita'
- la tabella punto -> uscita fisica mostra nello stesso posto quota, azione, segnale logico, board, canale e polarita'
- le tabelle avanzate restano accessibili nella stessa pagina, ma chiuse in un expander per ridurre rumore visivo
- backup pre-modifica locale verificato: `D:\Pulsar\Backups\QtisVisionPanel\QtisVisionPanel_source_pre_pcie_unified_20260629_111149`
- backup pre-modifica condiviso verificato: `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\lastRelease_backups\QtisVisionPanel_shared_source_pre_pcie_unified_20260629_111149`

## Version 3.0.1.2

### Release metadata

- release date: `2026-06-29`
- previous baseline: `3.0.1.1`

### Release intent

Avviare la pulizia conservativa della gestione I/O/encoder senza cambiare il comportamento macchina. La prima fase centralizza le regole comuni di parsing canali, polarita' segnali e conversioni encoder, cosi' `MachineController`, `DigitalIOViewModel` e `MultiShotTriggerController` usano lo stesso punto di verita'.

### Main functional integrations included

- nuovo helper `Services/MachineRuntimeIo.cs` per:
  - parsing canali fisici/logici come `DI00`, `DO01`, `Counter0`
  - conversione stato logico/elettrico in base alla polarita' del segnale
  - calcolo `counts/mm`
  - conversione millimetri -> conteggi encoder
  - conversione conteggi encoder -> millimetri
  - risoluzione del moltiplicatore encoder `x1`, `x2`, `x4`
- `MachineController` usa l'helper condiviso per quote prodotto, target intervento e posizione macchina
- `MultiShotTriggerController` usa l'helper condiviso per canale output e polarita' elettrica
- `DigitalIOViewModel` mantiene i wrapper locali ma delega i calcoli all'helper condiviso

### Configuration-file notes

- nessuna nuova chiave `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`
- nessuna modifica a ricette o VPP richiesta

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- comportamento operativo invariato
- la pulizia riduce il rischio che velocita', quote trigger, scarto e MultiShot usino formule diverse in punti diversi del software
- backup applicativo verificato creato prima delle modifiche in `D:\Pulsar\Backups\QtisVisionPanel\QtisVisionPanel_application_verified_20260629_092528`

## Version 3.0.1.1

### Release metadata

- release date: `2026-06-22`
- previous baseline: `3.0.1.0`

### Release intent

Consolidare gli aggiornamenti Claude rimasti non ancora promossi nell'archivio ufficiale: migliorare l'analisi visiva degli scarti dal `DataInspector` e impedire che la `Overview / All Channel` ricostruisca le viste camera quando l'operatore rientra dalla navigazione.

### Main functional integrations included

- `DataInspectorViewModel`: aggiunti comandi di zoom immagine, reset zoom, chiusura overlay e download evidenza
- `DataInspectorView.xaml` / code-behind: overlay fullscreen per zoom immagini source/processed con rotella mouse e pulsanti `+`, `-`, `1:1`, chiusura
- `DataInspectorViewModel.DownloadEvidenceAsync()`: esporta immagini e `detail.txt` in una cartella scelta dall'operatore, con timestamp, PieceId, ricetta, operatore e dettagli ispezione
- `CameraContainer.xaml.cs`: `RefreshFromRuntimeConfiguration()` viene eseguito automaticamente solo al primo `Loaded`; ai ritorni pagina aggiorna il layout senza fare `CameraViews.Clear()`

### Configuration-file notes

- nessuna nuova chiave `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`
- nessuna modifica a ricette o VPP richiesta

### Database notes

- nessuna nuova tabella o colonna
- il download evidenza usa i dati e i percorsi immagini gia disponibili dal DataInspector

### Operator / maintainer notes

- cliccando una miniatura del DataInspector l'operatore puo controllare il difetto a pieno schermo con zoom
- il pulsante `Scarica evidenza` crea un pacchetto locale con immagini e file descrittivo per qualita, manutenzione o supporto
- tornando alla Overview le ultime immagini camera restano visibili; il refresh runtime completo resta disponibile quando MainWindow lo richiama esplicitamente dopo init VisionPro o cambio ricetta

## Version 3.0.1.0

### Release metadata

- release date: `2026-06-18`
- previous baseline: `3.0.0.9`

### Release intent

Consolidare gli aggiornamenti Claude del 18 giugno 2026 relativi a contatori, refresh UI e notifica operatore quando una camera companion non consegna il risultato entro timeout. La modifica chiude i casi in cui i contatori restavano congelati durante burst di ispezioni o dopo navigazione tra pagine.

### Main functional integrations included

- `InspectionOrchestrator`: aggiunto evento `CompanionTimeoutOccurred` quando una camera attesa non arriva entro la finestra di attesa multi-camera
- `MainWindow`: sottoscrizione al timeout companion e visualizzazione di popup warning non modale, auto-close 10 secondi, con rate limit di 30 secondi
- `CounterManager`: gli eventi `CountersUpdated` vengono pubblicati con `Dispatcher.BeginInvoke` invece di `Dispatcher.Invoke`, evitando blocchi del thread orchestrator durante burst rapidi
- `TopMenuBarViewModel`: il top bar si aggiorna anche sugli eventi `CountersUpdated`, non solo tramite timer a 1 secondo
- `StatisticsView` / `InspectionCounterViewModel`: subscription dei contatori ripristinata correttamente a ogni ciclo `Loaded` / `Unloaded`
- `CounterData`: aggiunta chiave canonica `Key` per aggiornare `Total`, `Good` e `No Good` senza dipendere dal testo localizzato del campo `Header`

### Configuration-file notes

- nessuna nuova chiave `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`
- nessuna modifica a ricette o VPP richiesta

### Database notes

- nessuna nuova tabella o colonna
- nessuna migrazione richiesta

### Operator / maintainer notes

- i contatori Overview e top bar devono aggiornarsi durante la produzione senza dover cambiare pagina o aspettare refresh lunghi
- se una camera companion manca entro timeout, l'operatore riceve un popup warning temporaneo; l'evento resta comunque tracciato nei log
- in caso di burst dopo un timeout, l'elaborazione ispezioni non viene piu serializzata dal Dispatcher UI

## Version 3.0.0.9

### Release metadata

- release date: `2026-06-17`
- previous baseline: `3.0.0.8`

### Release intent

Consolidare gli aggiornamenti Claude del 16-17 giugno 2026 in una baseline unica, documentata e riallineata: stabilita MultiShot multi-camera, diagnostica camera/Overview piu coerente e log PowerFlex piu puliti in produzione.

### Main functional integrations included

- `InspectionOrchestrator`: protezione FIFO contro risultati companion appartenenti a un prodotto successivo (`COMPANION_PRODUCT_MISMATCH`) e attesa multi-camera coerente con `CameraResult.EnqueuedAtUtc`
- `MultiShotTriggerController`: diagnostica FrameAck resa non bloccante e poi disabilitata lato provider HMI per evitare accessi COM VisionPro da thread MTA
- `DigitalIOViewModel`: `AttachStitchingStatusProvider(...)` non viene piu collegato al controller finche non sara disponibile un marshaling STA sicuro
- `CameraContainer`: Overview/All Channel usa un'istanza persistente e rinfresca i pannelli visibili dai job VPP runtime, mostrando solo camere con hardware effettivo dopo init VisionPro
- `IgigaCameraAccess`: LiveView non forza piu `TriggerMode=Off` sulle camere hardware-triggered DALSA, evitando warning su nodi non scrivibili
- `PowerFlex525EtherNetIpClient`: trace raw EtherNet/IP spostati a `Debug` e log `CIP monitor read completed` throttled a un minuto o a cambio stato

### Configuration-file notes

- nessuna nuova chiave `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`
- nessuna modifica a ricette o VPP richiesta

### Database notes

- nessuna nuova tabella o colonna
- resta valida la tabella `tbl_multishot_param_history` introdotta in `3.0.0.7`

### Operator / maintainer notes

- in produzione i log PowerFlex sono meno rumorosi; i frame raw CIP restano disponibili portando temporaneamente NLog a `Debug`
- `MULTISHOT_FRAME_ACK` non e piu un log atteso nella baseline corrente; per debug VisionPro usare `COMPANION_ARRIVED`, `COMPANION_TIMEOUT`, `CAMERA_TIMING` e i log MultiShot encoder
- se l'Overview viene aperta prima che VisionPro finisca il caricamento puo mostrare il fallback XML; al rientro o dopo init aggiorna i pannelli dai job VPP attivi
- il LiveView mantiene il modo trigger configurato nel VPP e usa VisionPro Auto mode per la preview

## Version 3.0.0.8

### Release metadata

- release date: `2026-06-16`
- previous baseline: `3.0.0.7`

### Release intent

Correggere il bug critico che causava `companions=[] missing=[left]` nelle ispezioni multi-camera con telecamera MultiShot: `IsAnyMultiShotRuntimeActive()` leggeva la config da disco ad ogni iterazione del wait-loop (ogni 20ms), e una lettura fallita durante la scrittura atomica XML restituiva `false`, riducendo il timeout di attesa da 30s a 5s. Il risultato VisionPro stitched (9 immagini) arrivava dopo il timeout, causando ispezioni mancate e pairing errato con il prodotto successivo.

### Main functional integrations included

- `IsAnyMultiShotRuntimeActive()`: ora legge `_isMultiShotConfigured` (campo cached) invece di `new MachineConfigurationService().Load()` â€” eliminato il disk I/O nel hot-path del polling da 20ms
- `RefreshMultiShotConfiguredState()`: legge la config una sola volta all'avvio macchina (`runVisionPro()`) e popola il cache; in caso di errore mantiene il valore precedente (fail-safe)
- `CameraResult.EnqueuedAtUtc`: timestamp UTC impostato prima di ogni enqueue nelle 5 code camera
- `InspectionOrchestrator`: timing trace completo â€” `COMPANION_ARRIVED|role=X waited=Yms` quando ogni companion arriva; `COMPANION_TIMEOUT` con lista missing/arrived in caso di timeout; `CAMERA_TIMING|role=X lag-from-top=+Yms` per il lag tra TOP e ogni companion; `waited=Xms` nel log di "Processing inspection group"

### Configuration / database / API impact

- Config.xml: nessuna modifica
- Database: nessuna modifica
- API pubblica: nessuna â€” `InspectionOrchestrator` ctor e `ProcessQueuedPairsAsync` invariati

---

## Version 3.0.0.7

### Release metadata

- release date: `2026-06-15`
- previous baseline: `3.0.0.6`

### Release intent

Aggiungere la visualizzazione del valore precedente di ogni parametro dell'editor MultiShot (Side/Right/Bottom), con persistenza nel database MySQL, per consentire il ripristino rapido dei parametri in caso di configurazione errata.

### Main functional integrations included

- nuova tabella MySQL `tbl_multishot_param_history`: memorizza l'ultimo valore salvato per ogni parametro di ogni profilo MultiShot (Side, Right, Bottom); aggiornata tramite UPSERT ad ogni salvataggio configurazione
- `DigitalIOViewModel.CurrentMultiShotPreviousSnapshot`: property che espone il precedente snapshot del profilo attualmente visualizzato nell'editor; si aggiorna quando si cambia profilo e dopo ogni salvataggio
- `DigitalIOControl.xaml`: ogni campo editabile MultiShot (MaxShots, Timeout, TriggerOutput, ShotCount, PulseMs, MinIntervalMs, StepMm, MmPerPixel) mostra sotto di sÃ© il valore precedente in formato "Prec.: X" (testo azzurro-grigio); la riga Ã¨ visibile solo dopo il primo salvataggio
- la tabella DB viene creata automaticamente alla prima avvio tramite il sistema di migrazione schema in `Cls_InitializzeDb`

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`

### Database notes

- nuova tabella `tbl_multishot_param_history` (PK composite `profile`+`param_name`); creata con `CREATE TABLE IF NOT EXISTS` â€” sicura su aggiornamento

### Operator / maintainer notes

- dopo il primo salvataggio configurazione, i valori precedenti compaiono sotto ogni campo dell'editor MultiShot come riferimento rapido
- se MySQL non Ã¨ disponibile, il salvataggio/caricamento dello storico Ã¨ silenziosamente ignorato (fire-and-forget); la configurazione XML funziona normalmente

## Version 3.0.0.6

### Release metadata

- release date: `2026-06-15`
- previous baseline: `3.0.0.5`

### Release intent

Correggere il fenomeno "velocitÃ  encoder 250 m/min all'avvio" (root cause rimossa, non mitigata con warmup) e completare il supporto al salvataggio immagini per le viste Rear e Bottom, incluse le viste multi-scatto con le sigle "R" e "B".

### Main functional integrations included

- `AdvantechDeviceManager.ReadEncoderAsync`: rimossa logica `_encoderReadAsDeltaMode`. Il ritorno `count=0` dalla scheda Advantech durante lo startup VisionPro (~12s) ora preserva l'ultimo valore noto (`_encoderValues[channel]`) invece di innescare accumulo cumulativo. Il campo `_encoderReadAsDeltaMode` e la relativa modalitÃ  delta non esistono piÃ¹.
- `RearCameraView.ImageDisplay` e `BottomCameraView.ImageDisplay`: nuove property pubbliche che espongono `CogRecordsDisplay1`; rimuovono la dipendenza dall'accesso diretto al campo `internal` generato da XAML.
- `CameraDisplayManager.UpdateDisplayParallelAsync`: aggiunti assignment `_rearDisplaySaveRecord` e `_bottomDisplaySaveRecord` nei branch "rear" e "bottom" â€” in precedenza erano sempre `null`.
- `ISaveImage`: metodi `GetSavedRecord` e `TryGetImageFromToolBlock` estesi con i casi "R" (Rear) e "B" (Bottom).
- `MainWindow`: `SyncInspectionRecordsForSave` e `ClearPendingVisionResults` ora sincronizzano e puliscono i record per tutti e cinque i ruoli camera (top, side, front, rear, bottom).

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`

### Database notes

- nessuna modifica schema DB

### Operator / maintainer notes

- la velocitÃ  encoder all'avvio non mostrerÃ  piÃ¹ il picco a ~250 m/min che richiedeva il reset manuale
- il salvataggio immagini funziona ora anche per le viste Rear, Bottom e le relative viste multi-scatto (sigle "R", "B")

## Version 3.0.0.5

### Release metadata

- release date: `2026-06-11`
- previous baseline: `3.0.0.4`

### Release intent

Consolidare la baseline di produzione 3.x risolvendo le criticita di sicurezza runtime verificate nel codice: eccezioni async silenziate, race condition sullo stato running, tracciabilita assente per le azioni operative chiave.

### Main functional integrations included

- `async void` command handlers nei ViewModel convertiti a `async Task` + `.SafeFireAndForget()`: le eccezioni nei handler alarm, recipe, counter ora appaiono in NLog invece di essere silenziate
- race condition C3 eliminata: `MainWindow._isContinuousRunActive` (public static) convertito a property che delega a `MachineRuntimeService.IsContinuousRunActive` (lock-protected)
- audit trail completo: `AuditLogService` ora registra `RECIPE_SAVE`, `RECIPE_LOAD_PRODUCTION`, `RECIPE_DELETE`, `COUNTER_RESET_ALL`, `COUNTER_RESET_SHIFT`, `ALARM_CARD_SAVE`, `MACHINE_CONFIG_SAVE`
- `AuthorizationService.InvalidateCache()` aggiunto e invocato da `TopMenuBarViewModel` dopo ogni login e logout: i permessi vengono ricaricati immediatamente dal DB invece di aspettare il timeout da 5 minuti
- `Models/CameraResult.cs` estratto da class privata annidata in MainWindow a `internal sealed class` standalone: prerequisito per l'estrazione futura di `InspectionOrchestrator`
- commenti architetturali aggiunti su file prioritari: `Cls_InitializzeDb`, `AlarmCardRepository`, `DigitalIOViewModel`, `PowerFlex525Service`

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`
- nessuna nuova sezione XML

### Database notes

- nessuna modifica schema DB
- nuove righe nella tabella `audit_log` esistente per gli eventi operativi citati sopra

### Operator / maintainer notes

- nessun cambiamento visibile all'operatore
- per i manutentori: ogni salvataggio ricetta, reset contatori o cambio configurazione I/O produce ora una riga in `audit_log` con attore, timestamp e dettagli
- il cambio ruolo (login/logout) invalida immediatamente la cache permessi; non serve attendere 5 minuti per vedere i permessi aggiornati

## Version 3.0.0.4

### Release metadata

- release date: `2026-06-10`
- previous baseline: `3.0.0.3`

### Release intent

Stabilizzare la velocita encoder visualizzata all'avvio HMI ed evitare che la card taratura tachimetro torni sempre al valore fallback `40 m/min`.

### Main functional integrations included

- il primo campione encoder dopo auto-start/re-arm viene usato solo per sincronizzare il riferimento velocita
- durante i primi secondi di warmup, un salto contatore non plausibile viene scartato e usato come nuova base invece di pubblicare `250 m/min`
- il valore `Tachometer speed` della card commissioning viene salvato in `MachineRuntimeConfiguration.RuntimeBindings`
- il template macchina include `EncoderCalibrationTachometerSpeedMetersPerMinute`
- aggiunta label diagnostica localizzata per la risincronizzazione riferimento encoder all'avvio

### Configuration-file notes

- `machine_runtime_config.xml`: nuovo campo opzionale `RuntimeBindings/EncoderCalibrationTachometerSpeedMetersPerMinute`
- file esistenti restano compatibili; se il campo manca viene usato fallback `40.0` e il campo viene scritto al successivo salvataggio/schema upgrade
- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- dopo aver inserito una velocita tachimetro diversa da `40` e applicato/salvato, il valore resta disponibile al riavvio
- se al primo avvio il contatore fisico Advantech riporta un valore gia alto, la HMI non lo usa piu come delta velocita iniziale
- il pulsante `Reset` resta una recovery manuale, ma non deve piu essere necessario per riportare la velocita da `250 m/min` alla velocita tarata

## Version 3.0.0.3

### Release metadata

- release date: `2026-06-10`
- previous baseline: `3.0.0.2`

### Release intent

Correggere il modello della camera `Bottom`: la Bottom non valida piu controlli top-like, ma i due controlli reali della macchina, cioe saldatura inferiore e carta intrappolata nella saldatura.

### Main functional integrations included

- aggiunti difetti dedicati `BottomSealing` e `TrappedPaper`
- `GetDetailedBottomValidationAsync` ora legge output booleani VisionPro Bottom invece di riusare Logo/PrintCentering/OpenFlaps/ShapeTop/SurfaceCheck
- la mappa runtime feature abilita `BottomSealing` e `TrappedPaper` solo quando il ruolo `bottom` e presente
- contatori, allarmi scarto, popup allarme e Data Analysis riconoscono separatamente i due difetti Bottom
- `BottomCameraView` mostra due indicatori dedicati invece delle vecchie icone top-like
- la lettura output booleani VisionPro interpreta correttamente stringhe `OK`, `NOK`, `KO`, `FAIL`, `NOREAD`

### Configuration-file notes

- nessuna nuova chiave `Config.xml`
- non vengono aggiunti campi macchina o MultiShot
- le nuove abilitazioni restano in ricetta (`inspectionStatus` / `ejectionStatus`) con default compatibile per ricette esistenti

### Database notes

- `tblgenerale`: aggiunte colonne opzionali `NC_BottomSealing` e `NC_TrappedPaper`
- `tblglobalcounters`: aggiunti contatori `BOTTOM_SEALING` e `TRAPPED_PAPER`
- `cfg_inspection`: aggiunte feature `BottomSealing` e `TrappedPaper`

### Operator / maintainer notes

- nel job VisionPro `Bottom` usare preferibilmente `BottomSealingOk` e `NoTrappedPaper`
- alias supportati per saldatura inferiore: `BottomSealOk`, `LowerSealingOk`, `SaldaturaInferioreOk`, oppure output difetto `BottomSealingDefect`/`BottomSealingNok`
- alias supportati per carta intrappolata: `TrappedPaperOk`, `PaperClear`, `CartaIntrappolataOk`, oppure output difetto `TrappedPaperDetected`/`PaperTrapped`/`CartaIntrappolata`
- se un controllo Bottom e abilitato in ricetta ma l'output VisionPro manca, il pezzo diventa NOK per evitare falsi OK

## Version 3.0.0.2

### Release metadata

- release date: `2026-06-10`
- previous baseline: `3.0.0.1`

### Release intent

Chiudere la fase successiva della `3.0.0.x`: integrare `Left`, `Right/Rear` e `Bottom` nella decisione finale del pezzo, non solo nel trigger/display.

### Main functional integrations included

- il ciclo ispezione ora aggrega `Top` con tutte le camere companion caricate: `Side/Left`, `Front`, `Right/Rear`, `Bottom`
- `InspectionResult` espone anche `RearResult`, `BottomResult` e risultati tecnici per camere mancanti
- `IToolBlockValidator` aggiunge validatori dedicati per:
  - `Rear/Right`: saldatura laterale con output booleani o `SealingArea`
  - `Bottom`: controlli top-like solo se il ToolBlock espone i relativi output
- `InspectionConfigService` riconosce `Rear` e `Bottom` nella mappa feature runtime
- contatori, allarmi scarto e dettaglio DB includono gli errori `Rear/Right`, `Bottom` e timeout camera
- se una camera prevista non arriva entro timeout, il pezzo diventa NOK con difetto tecnico `CameraMissing`
- il timeout gruppo ispezione usa la finestra MultiShot lunga quando almeno un profilo MultiShot e abilitato

### Configuration-file notes

- nessuna nuova sezione XML richiesta
- restano validi i nodi `MachineMultiShotTrigger/Side`, `/Right`, `/Bottom` introdotti in `3.0.0.1`
- i job VisionPro devono esporre ruoli riconoscibili (`Left/Side`, `Right/Rear`, `Bottom`) tramite nome job, seriale o `CameraConfig.xml`

### Database notes

- nessuna modifica schema DB
- i campi esito/scarto esistenti vengono valorizzati usando l'esito aggregato multi-camera

### Operator / maintainer notes

- per collaudare Quatis 3..5 camere verificare che ogni camera prevista produca un risultato per pezzo
- un timeout di `Right/Rear` o `Bottom` ora genera NOK invece di restare solo diagnostico
- per `Right/Rear` lo script VisionPro deve pubblicare almeno uno tra `RightSideSealingOk`, `RearSideSealingOk`, `RightSealingOk`, `RearSealingOk`, `SideSealingOk`, `SealingOk` oppure `SealingArea`
- per `Bottom` vengono validati solo gli output effettivamente presenti: `LogoPosition`, `D_*_Logo`, `OpenFlapsArea`, `EL_Classify`/`EL_Score`, `A_T_L`/`A_T_R`/`A_B_L`/`A_B_R`

## Version 3.0.0.1

### Release metadata

- release date: `2026-06-09`
- previous baseline: `3.0.0.0`

### Release intent

Prima fase implementativa per l'estensione multi-camera `Right/Rear` e `Bottom`, mantenendo stabile la logica di scarto esistente.

### Main functional integrations included

- `MachineMultiShotTrigger` ora contiene tre profili macchina: `Side`, `Right`, `Bottom`
- la pagina I/O mostra un selettore profilo MultiShot: `Left / Side`, `Right / Rear`, `Bottom`
- il controller MultiShot puo armare piu sessioni encoder-driven sullo stesso fronte fotocellula
- il filtro VisionPro `ImageStitching.isReady` vale per ogni profilo MultiShot abilitato, non solo per Left
- i parametri VisionPro `expectedFrames`, `stepMm`, `mmPerPixel` vengono applicati al job compatibile con il profilo abilitato
- i job `Right` vengono normalizzati come `rear` e visualizzati nella `RearCameraView`
- i job `Bottom` vengono visualizzati nella `BottomCameraView`
- il template runtime aggiunge output e punti intervento per `OUT_CAMERA_REAR_TRIGGER` / `CAMERA_TRIGGER_REAR` e `OUT_CAMERA_BOTTOM_TRIGGER` / `CAMERA_TRIGGER_BOTTOM`
- aggiunto manuale operativo `Docs/Manual/08_MultiShot_Right_Bottom.md`

### Configuration-file notes

- `machine_runtime_config.xml`: aggiunti nodi opzionali `MachineMultiShotTrigger/Right` e `MachineMultiShotTrigger/Bottom`
- i file esistenti restano compatibili; lo schema viene riallineato dal servizio di configurazione quando mancano i nuovi nodi
- default output:
  - `OUT_CAMERA_SIDE_TRIGGER`
  - `OUT_CAMERA_REAR_TRIGGER`
  - `OUT_CAMERA_BOTTOM_TRIGGER`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- configurare un profilo alla volta nella pagina I/O
- per ogni profilo MultiShot verificare uscita, posizione intervento, numero scatti, durata impulso, intervallo minimo, step stitching e calibrazione camera
- questa release non inserisce ancora `Right/Rear` e `Bottom` nel calcolo finale NOK multi-camera; la fase successiva dovra introdurre un aggregatore risultati per prodotto

## Version 3.0.0.0

### Release metadata

- release date: `2026-06-09`
- previous baseline archived as: `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\SoftwareArchives\QtisVisionPanel_2.0.2.5_pre_right_bottom_multishot_20260609_134704.zip`

### Release intent

Aprire una nuova major release per l'estensione multi-camera della piattaforma, partendo dalla baseline stabile `2.0.2.5` prima di implementare:

- camera `Right` come ruolo complementare alla vista fisica/visuale `Rear`, con supporto MultiShot
- camera `Bottom` configurabile in single-shot o MultiShot in base alla tipologia prodotto
- profili macchina da usare sulla stessa applicazione: `Vision` da 1 a 3 camere e `Quatis` da 3 a 5 camere

### Main functional integrations included

- nessuna nuova logica camera implementata in questa release
- aggiornamento versione assembly a `3.0.0.0`
- backup sorgente della baseline precedente `2.0.2.5` creato prima del cambio major
- tracciata la direzione tecnica per i prossimi task Right/Bottom MultiShot
- aggiunta nota tecnica `right-bottom-multishot-expansion-2026-06-09.md`

### Configuration-file notes

- nessuna modifica schema XML in questa release di apertura
- i prossimi task dovranno mantenere la distinzione: mapping I/O e numero/ruolo camere come configurazione macchina; tolleranze e parametri prodotto in ricetta; stato corrente solo runtime

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- questa release serve come punto di partenza pulito per lo sviluppo `3.x`
- la baseline precedente puo essere ripristinata dall'archivio ZIP indicato sopra
- prima di abilitare Right/Bottom MultiShot su macchina reale, servira validare cablaggi trigger, ruoli VisionPro, output booleani attesi e tempi minimi camera

## Version 2.0.2.5

### Release metadata

- release date: `2026-06-09`

### Release intent

Rendere piu robusto il MultiShot Side/Left su macchina reale quando la camera conta meno trigger rispetto agli impulsi generati dalla scheda e quando il trim viene applicato al punto `CAMERA_TRIGGER_LEFT`.

### Main functional integrations included

- il calcolo del `First shot offset` usa prima `CAMERA_TRIGGER_LEFT`, se presente e abilitato, e mantiene fallback su `CAMERA_TRIGGER_SIDE` per compatibilita
- il riepilogo geometria indica il punto intervento realmente usato per calcolare l'offset
- aggiunto parametro macchina `MinimumInterShotIntervalMs` per imporre un tempo minimo tra due fronti MultiShot consecutivi
- la pagina I/O mostra il nuovo campo `Tempo minimo tra scatti`
- il controller MultiShot aspetta il tempo residuo se il target encoder e' gia raggiunto ma la camera non deve ancora ricevere un nuovo fronte

### Configuration-file notes

- aggiunto nodo opzionale `MachineMultiShotTrigger/Side/MinimumInterShotIntervalMs`
- default operativo: `30 ms`
- i file macchina esistenti restano compatibili; se il nodo manca viene usato il default del modello runtime

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- `Trim +/- mm` va modificato sul punto `CAMERA_TRIGGER_LEFT` quando il job lavora in ruolo Left; se il punto non esiste, il sistema usa ancora `CAMERA_TRIGGER_SIDE`
- se il log HMI mostra tutti gli impulsi ma la camera ne conta meno, aumentare `Pulse duration` e/o `Tempo minimo tra scatti`
- se aumentando l'intervallo minimo gli scatti arrivano troppo in ritardo rispetto alla posizione reale, aumentare lo step mm, ridurre la velocita linea o verificare i tempi di acquisizione/esposizione camera

## Version 2.0.2.4

### Release metadata

- release date: `2026-06-08`

### Release intent

Rendere il watchdog VisionPro compatibile con il ritardo fisiologico del job Left MultiShot. Durante il MultiShot il risultato Left stitched arriva dopo N frame, quindi l'assenza temporanea di risultati non deve avviare subito il recovery automatico.

### Main functional integrations included

- quando `MachineMultiShotTrigger/Side` e' attivo in modalita Left/SideLeft, il timeout watchdog passa da `5 s` a `30 s`
- in modalita Left MultiShot il watchdog non considera piu la semplice desincronizzazione Top/Left o l'attesa della coppia come motivo sufficiente per recovery
- il recovery automatico resta attivo per segnali piu forti: job non piu in RunContinuous, UserResult ripetutamente vuoto, RunStatus error ripetuti
- i dettagli diagnostici indicano quando la grace MultiShot e' attiva

### Configuration-file notes

- nessuna modifica XML
- il comportamento si attiva automaticamente se il nodo macchina `MachineMultiShotTrigger/Side` e' abilitato e il ruolo/display e' Left o SideLeft

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- i log `VisionPro appears stalled. Side camera heartbeat missing. Inspection pair processing stalled.` non devono piu comparire solo per il ritardo dello stitching Left
- se VisionPro e' realmente fermo, il recovery resta possibile tramite stato RunContinuous o errori ripetuti
- se il tempo reale di stitching supera `30 s`, rivedere performance del VPP o aumentare in codice la finestra `VisionMultiShotResultTimeout`

## Version 2.0.2.3

### Release metadata

- release date: `2026-06-08`

### Release intent

Evitare che il MultiShot Side/Left si fermi al secondo o quarto scatto quando il polling encoder rileva il target con ritardo. La soglia late resta utile come diagnostica, ma non deve bloccare la sequenza se l'obiettivo operativo e' completare tutti i frame richiesti da VisionPro.

### Main functional integrations included

- `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` diventa warning diagnostico e non cancella piu la sessione
- il messaggio late indica che il target e' stato raggiunto in ritardo rispetto al campione encoder, ma la sequenza continua fino a `ShotCount`
- il polling hardware di `AdvantechDeviceManager` passa da `50 ms` a `10 ms` per ridurre l'overshoot encoder in MultiShot

### Configuration-file notes

- nessuna modifica schema aggiuntiva rispetto a `2.0.2.2`
- `TargetLateTolerancePulses` resta presente come soglia diagnostica per capire quanto il campione encoder e' in ritardo rispetto al target

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- dai log macchina: a velocita linea alta e step MultiShot piccolo, il polling encoder puo arrivare con `overshoot` superiore alla soglia
- da questa release l'evento non ferma piu il ciclo; serve a capire se la sequenza e' troppo fitta per il polling software
- per precisione encoder reale a step molto piccoli e velocita elevate, la soluzione piu robusta resta un trigger hardware/counter compare o una generazione impulsi dedicata lato board/PLC

## Version 2.0.2.2

### Release metadata

- release date: `2026-06-08`

### Release intent

Correggere la cancellazione troppo aggressiva del MultiShot Side/Left introdotta con la protezione anti catch-up. In macchina reale il polling encoder puo arrivare qualche centinaio di impulsi dopo il target, soprattutto a velocita linea elevate; questo non deve fermare subito la sessione al primo o secondo frame.

### Main functional integrations included

- aggiunto parametro macchina `TargetLateTolerancePulses` sotto `MachineMultiShotTrigger/Side`
- default compatibile: `500` impulsi
- la sessione viene cancellata con `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` solo se l'overshoot supera `max(StepPulses, TargetLateTolerancePulses)`
- il log `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` ora riporta anche `overshoot` e `allowed`
- dopo un impulso, il controller non rivaluta subito usando lo stesso identico conteggio encoder che ha fatto partire l'impulso; aspetta un nuovo conteggio reale, evitando recuperi ravvicinati sullo stesso sample

### Configuration-file notes

- nuovo nodo opzionale e retrocompatibile:

```xml
<TargetLateTolerancePulses>500</TargetLateTolerancePulses>
```

- se il nodo manca nei file macchina esistenti, viene valorizzato a `500`
- per macchine con polling encoder piu lento o linea molto veloce, in commissioning si puo aumentare il valore, per esempio `800` o `1000`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- nel log analizzato erano presenti cancellazioni con overshoot `164`, `172`, `287`, `361` impulsi
- con la soglia precedente `StepPulses / 2`, questi valori potevano annullare subito il MultiShot anche se erano compatibili con il polling reale
- la protezione contro sequenze palesemente perse resta attiva, ma non blocca piu i normali ritardi di campionamento

## Version 2.0.2.1

### Release metadata

- release date: `2026-06-08`

### Release intent

Evitare che il controller MultiShot Side/Left generi una raffica di impulsi nello stesso ciclo quando l'encoder ha gia superato piu target, situazione che puo produrre frame fuori sequenza, stitching incoerente e scarti apparentemente casuali.

### Main functional integrations included

- il controller Side/Left MultiShot emette al massimo un impulso per volta
- durante la durata dell'impulso la sessione resta in stato `PulseInFlight` e non arma altri target
- al termine dell'impulso il controller rivaluta l'ultimo conteggio encoder osservato e decide se generare il target successivo
- se il target successivo e' troppo in ritardo rispetto allo step configurato, la sessione viene cancellata e viene loggato `MULTISHOT_SIDELEFT_TARGET_TOO_LATE`
- la ricerca del ToolBlock VisionPro `ImageStitching` ora e' ricorsiva anche dentro gruppi/toolblock annidati
- la guardia runtime su `ImageStitching.isReady` usa la stessa ricerca ricorsiva, cosi filtra correttamente anche VPP strutturati con tool annidati

### Configuration-file notes

- nessuna modifica schema XML
- restano validi `MachineMultiShotTrigger/Side/ShotCount`, `InitialOffsetPulses`, `StepPulses`, `TriggerPulseMs` e `SessionTimeoutMs`
- se si verifica `MULTISHOT_SIDELEFT_TARGET_TOO_LATE`, in commissioning controllare velocita linea, durata impulso, `ShotCount`, step mm e counts/mm encoder

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- il comportamento corretto resta: fotocellula -> conteggio encoder fino a `CAMERA_TRIGGER_SIDE` -> N impulsi distanziati dallo step encoder -> VisionPro restituisce solo il risultato finale stitched
- la HMI non fa stitching e non acquisisce frame multipli in C#
- un errore `MULTISHOT_SIDELEFT_TARGET_TOO_LATE` indica che la sequenza non e' piu affidabile; il pezzo va trattato come sessione MultiShot annullata invece di accettare un'immagine stitched potenzialmente falsa

## Version 2.0.2.0

### Release metadata

- release date: `2026-06-08`

### Release intent

Evitare che i risultati intermedi del job Left MultiShot vengano trattati come ispezioni complete, causando immagini spezzate, salvataggi prematuri e scarti non coerenti.

### Main functional integrations included

- quando `MachineMultiShotTrigger/Side/Enabled=true` e il ruolo runtime e' `Left`, la HMI legge `ImageStitching.isReady`
- i risultati Left con `isReady=false` vengono ignorati e non entrano nella coda di accoppiamento Top/Left
- il risultato Left viene accettato solo quando `isReady=true`, cioe' quando VisionPro dichiara pronta l'immagine stitched finale
- il log runtime indica `LEFT MultiShot intermediate VisionPro result ignored` per i frame intermedi e `LEFT MultiShot final stitching result accepted` per il frame finale

### Configuration-file notes

- nessuna modifica schema XML
- il ToolBlock VisionPro `ImageStitching` deve esporre l'output booleano `isReady`
- sono letti anche `frameIndex` e `status` quando presenti, solo per diagnostica

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- nel log analizzato il risultato Left entrava in coda dopo lo scatto `4/12`, mentre gli scatti `6/12..12/12` avvenivano dopo il salvataggio immagini
- dopo questa release quel risultato parziale viene scartato lato HMI e non puo' aggiornare contatori, allarmi, salvataggio immagini o scarto
- se `isReady` manca, la HMI accetta il risultato per non bloccare produzione ma scrive un warning: in commissioning va corretto il VPP

## Version 2.0.1.9

### Release metadata

- release date: `2026-06-08`

### Release intent

Rendere il MultiShot Side/Left configurabile in modo coerente con il flusso macchina reale: fotocellula, quota camera da `CAMERA_TRIGGER_SIDE`, N scatti distanziati da encoder e parametri VisionPro allineati subito.

### Main functional integrations included

- il primo scatto MultiShot Side/Left viene calcolato dalla quota `Intervention Points -> CAMERA_TRIGGER_SIDE` convertita con i counts/mm del main encoder
- la distanza tra scatti viene calcolata da `VisionProStitching.StepMm` convertito in impulsi encoder
- i campi HMI in impulsi diventano valori calcolati/di verifica, non conversioni da fare manualmente
- salvataggio, caricamento, preview runtime e taratura encoder applicano subito i parametri `ImageStitching` al job VisionPro Left se gia caricato
- la schermata I/O mostra il riepilogo: quota camera, step reale, impulsi calcolati e counts/mm

### Configuration-file notes

- nessuna modifica schema XML
- i campi esistenti `MachineMultiShotTrigger/Side/InitialOffsetPulses` e `StepPulses` restano presenti e vengono salvati con i valori derivati dalla configurazione macchina
- il commissioning deve impostare la quota camera in `CAMERA_TRIGGER_SIDE` e lo step reale in `VisionProStitching.StepMm`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- `Shots` deve essere uguale al numero di frame attesi da VisionPro (`ImageStitching.expectedFrames`)
- se `CAMERA_TRIGGER_SIDE = 386 mm` e `counts/mm = 8.021`, il primo impulso verra generato circa a `3096` impulsi dopo la fotocellula
- se `StepMm = 10 mm`, la distanza tra impulsi sara circa `80` impulsi con la stessa taratura
- dopo modifica da HMI non e piu necessario ricaricare manualmente il VPP solo per aggiornare `expectedFrames`, `stepMm` e `mmPerPixel`

## Version 2.0.1.8

### Release metadata

- release date: `2026-06-08`

### Release intent

Consentire alla taratura tachimetrica encoder di recuperare configurazioni molto fuori scala senza modifica manuale del file XML.

### Main functional integrations included

- range accettato del fattore taratura esteso da `0.05..20` a `0.001..1000`
- messaggi lingua ITA/ENG aggiornati per il nuovo range
- la formula resta invariata: `mm/rev nuovo = mm/rev attuale * tachimetro / velocita HMI`

### Configuration-file notes

- nessuna modifica schema
- il valore corretto viene scritto nel campo esistente `EncoderTemplates/MillimetersPerRevolution`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- se la velocita HMI e' molto diversa dal tachimetro, il pannello puo' ora applicare fattori grandi come quelli richiesti in commissioning
- verificare sempre che il tachimetro sia stabile e che l'encoder stia contando prima di premere `Apply to mm/rev`

## Version 2.0.1.7

### Release metadata

- release date: `2026-06-08`

### Release intent

Eliminare il falso errore operativo `ErrorFuncBusy` durante l'avvio automatico dell'encoder Advantech.

### Main functional integrations included

- `StartEncoderAsync` reso idempotente: se un canale encoder e' gia armato non viene riavviato
- retry breve quando la libreria Advantech risponde `ErrorFuncBusy` durante l'abilitazione del contatore
- `ErrorFuncBusy` in lettura encoder viene trattato come stato transitorio e mantiene l'ultimo conteggio disponibile
- i messaggi busy di startup encoder passano a diagnostica `DEBUG`, quindi non popolano piu lo storico eventi operatore

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- all'avvio HMI l'encoder puo' risultare occupato per pochi istanti mentre la board Advantech completa l'inizializzazione/polling
- questo stato non e' un guasto macchina se il conteggio encoder riprende nei cicli successivi

## Version 2.0.1.6

### Release metadata

- release date: `2026-06-08`

### Release intent

Rendere visibile in HMI il job QuickBuild `Left` usando la view laterale esistente, senza creare una nuova pagina camera.

### Main functional integrations included

- `Left` aggiunto ai ruoli camera visualizzabili nella pagina overview
- il ruolo `Left` usa la stessa `SideCameraView`, come previsto dalla baseline Side/Left
- il template selector associa `left` al template della camera laterale
- l'aggiornamento record VisionPro mantiene il label `Left Camera Features` invece di riscriverlo come `Side`

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`
- un VPP con job `Top` e `Left` e' ora sufficiente per mostrare entrambe le viste se i job vengono caricati correttamente

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- in QuickBuild il job puo' chiamarsi `Left`, `SideLeft`, `Side_Left` o alias compatibile
- la camera Left resta gestita dalla vista laterale e dalla pipeline secondaria Top+Side/Left

## Version 2.0.1.5

### Release metadata

- release date: `2026-06-05`

### Release intent

Ridurre il rumore nello storico eventi separando i normali esiti di ispezione dagli eventi tecnici e operativi della macchina.

### Main functional integrations included

- i motivi di scarto prodotto non vengono piu registrati come warning applicativi per ogni pezzo
- misure e dettagli di validazione restano disponibili solo a livello diagnostico `DEBUG`
- il bridge verso `tbllogevent` ignora cancellazioni attese durante stop, cambio vista e shutdown
- i cleanup dichiarati `non-critical` non vengono inseriti nello storico operatore
- guasti, eccezioni, timeout, allarmi e cambi stato importanti restano invariati

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica a `machine_runtime_config.xml`

### Database notes

- nessuna modifica schema
- ridotta la crescita di `tbllogevent` dovuta a messaggi ripetitivi non operativi

### Operator / maintainer notes

- contatori difetto, descrizioni UI, popup allarme ed esiti Good/No Good non cambiano
- consultare lo storico eventi per guasti e cambi stato macchina, non per il dettaglio di ogni singolo scarto

## Version 2.0.1.4

### Release metadata

- release date: `2026-06-05`

### Release intent

Rendere completamente automatico e persistente il runtime encoder della macchina e correggere l'esecuzione anticipata delle quote trigger.

### Main functional integrations included

- avvio automatico dei canali encoder configurati durante il preload HMI
- sincronizzazione del contatore principale prima di accettare fronti fotocellula in modalita encoder
- protezione contro prodotti creati con riferimento encoder iniziale non valido
- salvataggio automatico del nuovo `MillimetersPerRevolution` dopo taratura tachimetro
- conversione quote camera/scarto allineata al `TrackingMode` `x1/x2/x4`
- evento diagnostico `ENCODER_AUTOSTART_READY`

### Configuration-file notes

- nessuna modifica schema
- la taratura aggiorna automaticamente `EncoderTemplates/MillimetersPerRevolution` in `machine_runtime_config.xml`
- per usare le quote encoder: `UseEncoderTrigger=true`, `TriggerSchedulingMode=VirtualConveyor`, `VirtualConveyorEnabled=false`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- non serve piu premere manualmente `Start` per iniziare il campionamento encoder
- il pulsante `Start` resta disponibile per diagnostica/recovery
- con `UseEncoderTrigger=false` la macchina resta intenzionalmente in modalita temporizzata e usa i delay in millisecondi

## Version 2.0.1.3

### Release metadata

- release date: `2026-06-05`

### Release intent

Ripristinare la lettura da configurazione della soglia autoswitch ricetta per scarti consecutivi.

### Main functional integrations included

- `AutoRecipeSwitchService` usa `Config.AutoSwitchSettings.MaxConsecutiveFailures` invece di una costante interna
- il valore viene riletto a runtime durante il conteggio fail, quindi resta allineato al file configurazione e alla pagina Preferences
- fallback interno `5` solo se il nodo config manca o contiene un valore non valido

### Configuration-file notes

- nessuna modifica schema
- parametro usato: `AutoSwitchSettings.MaxConsecutiveFailures`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- la soglia autoswitch torna configurabile da HMI/config come prima

## Version 2.0.1.2

### Release metadata

- release date: `2026-06-05`

### Release intent

Rendere responsiva la card di taratura tachimetro encoder per pannelli piccoli, inclusi monitor da 15 pollici.

### Main functional integrations included

- sostituita la griglia a colonne fisse della taratura tachimetro con layout a capo automatico
- campi velocita e pulsanti restano visibili anche con larghezza ridotta

### Configuration-file notes

- nessuna modifica schema o valori config

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- su pannelli piccoli i pulsanti `Usa HMI corrente` e `Applica a mm/rev` possono andare a capo sotto i campi invece di uscire dalla card

## Version 2.0.1.1

### Release metadata

- release date: `2026-06-05`

### Release intent

Integrare in HMI una taratura encoder basata sulla velocita reale misurata con tachimetro durante il collaudo.

### Main functional integrations included

- aggiunto pannello `Tachometer calibration` nella pagina Encoder Setup
- il tecnico inserisce velocita tachimetro e velocita encoder HMI
- la HMI calcola `MillimetersPerRevolution` nuovo con la formula `mm/rev nuovo = mm/rev attuale * tachimetro / HMI`
- possibilita di acquisire automaticamente la velocita encoder HMI corrente dal pannello
- log applicativo `ENCODER_TACHOMETER_CALIBRATION_APPLIED`

### Configuration-file notes

- nessuna modifica schema `machine_runtime_config.xml`
- viene aggiornato il campo esistente `EncoderTemplates/MillimetersPerRevolution` dell'encoder principale

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- dalla release `2.0.1.4` l'applicazione della taratura salva automaticamente il nuovo `mm/rev`
- la taratura corregge anche quote encoder, trigger e MultiShot perche agisce sulla scala macchina, non solo sulla visualizzazione velocita

## Version 2.0.1.0

### Release metadata

- release date: `2026-06-04`

### Release intent

Ridurre il rumore log quando l'inverter PowerFlex 525 non e' raggiungibile all'avvio.

### Main functional integrations included

- il client PowerFlex esegue un primo ping all'avvio/refresh
- se il ping fallisce, pianifica un solo secondo tentativo dopo 10 minuti
- se anche il secondo tentativo fallisce, l'endpoint viene marcato non raggiungibile fino al riavvio HMI e non vengono piu' generati ping continui
- il refresh PowerFlex non tenta piu' la lettura parametri quando lo snapshot di connessione e' gia' non disponibile

### Configuration-file notes

- nessuna modifica schema `Config.xml`
- la logica usa ancora `PowerFlex525.Enabled`, `PowerFlex525.IpAddress`, `PowerFlex525.TimeoutMs` e `PowerFlex525.PollIntervalMs`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- se l'indirizzo IP inverter e' errato o l'inverter non risponde, viene notificato una volta e il ping continuo viene fermato
- per riattivare il controllo dopo correzione IP/cablaggio, riavviare la HMI o ricostruire il client dalla configurazione

## Version 2.0.0.9

### Release metadata

- release date: `2026-06-04`

### Release intent

Correggere l'inserimento dei valori decimali nella configurazione HMI dello stitching VisionPro Side/Left.

### Main functional integrations included

- aggiunto `Converters/DecimalTextBoxConverter.cs` per accettare valori decimali con punto o virgola
- applicato il converter ai campi `Stitching step` e `Left calibration` nella pagina Digital I/O
- gli stati intermedi di digitazione (`.`, `0.`, `0.0`, campo vuoto) non generano piu' errori di binding WPF

### Configuration-file notes

- nessuna modifica schema `Config.xml` o `machine_runtime_config.xml`
- i valori salvati restano `VisionProStitching.StepMm` e `VisionProStitching.MmPerPixel`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- la calibrazione Left puo' essere inserita sia come `0.035` sia come `0,035`
- se il testo non e' un numero valido, la HMI mantiene il valore precedente senza generare errore runtime

## Version 2.0.0.8

### Release metadata

- release date: `2026-06-04`

### Release intent

Stabilizzare la diagnostica encoder su PCIE-1884 quando la macchina usa un solo encoder fisico e pubblicare la velocita' encoder reale nel top menu.

### Main functional integrations included

- aggiunto `Services/MachineSpeedService.cs` per condividere la velocita' macchina tra Digital I/O e top menu
- il top menu usa la velocita' encoder recente quando la sorgente e' `Encoder`, con fallback al PowerFlex
- `AdvantechDeviceManager` legge solo i canali encoder configurati nel file macchina
- la lettura raw frequency encoder viene limitata e `ErrorFuncBusy` viene trattato come scheda occupata non critica
- il calcolo `counts/mm` applica il moltiplicatore `TrackingMode` (`x4`, `x2`, `x1`)

### Configuration-file notes

- `Config.xml` invariato
- `machine_runtime_config.xml` invariato nello schema; il canale attivo deriva dagli `EncoderTemplates`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- se e' configurato solo `Counter0`, la HMI non interroga piu' in continuo `Counter1..Counter3`
- formula velocita': `counts/mm = PulsesPerRevolution * trackingMultiplier / MillimetersPerRevolution`; `m/min = abs(deltaCounts / countsPerMm) * 60 / (1000 * elapsedSeconds)`
- per `PulsesPerRevolution=2048`, `MillimetersPerRevolution=100`, `TrackingMode=Quadrature x4`, il riferimento e' `81.92 counts/mm`

## Version 2.0.0.7

### Release metadata

- release date: `2026-06-04`

### Release intent

Correggere il caricamento VPP/HMI su macchine dove il job VisionPro richiede `System.Threading.Tasks.Extensions` assembly version `4.2.4.0` e il runtime trovava una DLL diversa.

### Main functional integrations included

- aggiunto binding redirect esplicito in `App.config` per `System.Threading.Tasks.Extensions` verso `4.2.4.0`
- riallineati i binding redirect di `System.Memory` e `System.Runtime.CompilerServices.Unsafe` alle versioni realmente copiate in output
- aggiornato `QtisVisionPanel.csproj` per usare `System.Threading.Tasks.Extensions` `4.6.3` via `PackageReference`
- rimosso il vecchio riferimento `packages.config` a `System.Threading.Tasks.Extensions` `4.5.4`

### Configuration-file notes

- `Config.xml` invariato
- in deploy devono restare insieme `QtisVisionPanel.exe`, `QtisVisionPanel.exe.config`, `System.Threading.Tasks.Extensions.dll`, `System.Memory.dll` e `System.Runtime.CompilerServices.Unsafe.dll`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- fix mirato al messaggio `Unable to load one or more of the requested types` durante il caricamento VPP dalla HMI
- la DLL corretta in output deve risultare `System.Threading.Tasks.Extensions, Version=4.2.4.0`

## Version 2.0.0.6

### Release metadata

- release date: `2026-06-04`

### Release intent

Spostare la regia del cambio ricetta automatico fuori da `MainWindow.xaml.cs` dentro un service dedicato, mantenendo invariato il comportamento operativo e riducendo la lunghezza/responsabilita' del main window.

### Main functional integrations included

- aggiunto `Services/AutoRecipeSwitchService.cs`
- il service gestisce contatore scarti consecutivi, timer timeout, stop/ripartenza `RunContinuous`, ricerca visiva ricetta, conferma operatore e reload ricetta
- `MainWindow.OnInspectionResult` ora delega al service
- `ServiceLocator` espone `AutoRecipeSwitchService` e lo dispone in shutdown/reset
- `QtisVisionPanel.csproj` include il nuovo service

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina, ricetta o runtime XML

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- comportamento autoswitch invariato: la soglia resta 20 scarti consecutivi come nel flusso precedente
- la conferma operatore prima del cambio ricetta resta obbligatoria
- il service logga eventi `AUTO_RECIPE_SWITCH_*` e `AUTO_RECIPE_CHANGE_*`

## Version 2.0.0.5

### Release metadata

- release date: `2026-06-04`

### Release intent

Rendere esplicita da HMI la scelta tra trigger camera/scarto guidati da encoder reale e trigger temporizzati da fotocellula, riducendo l'ambiguita' tra `TriggerSchedulingMode` e `VirtualConveyorEnabled`.

### Main functional integrations included

- aggiunto campo macchina `RuntimeBindings/UseEncoderTrigger`
- aggiunto switch nella pagina Digital I/O per selezionare:
  - `UseEncoderTrigger=true`: encoder reale, `TriggerSchedulingMode=VirtualConveyor`, `VirtualConveyorEnabled=false`
  - `UseEncoderTrigger=false`: trigger temporizzato da fotocellula, `TriggerSchedulingMode=TimedFromPhotocell`
- preservata compatibilita' con XML legacy senza `UseEncoderTrigger` tramite deduzione dai vecchi campi
- aggiunta validazione: Side/Left MultiShot richiede `UseEncoderTrigger=true`
- il fronte fotocellula non arma piu' il MultiShot Side/Left quando il trigger encoder e' disabilitato
- aggiornati testi lingua ITA/ENG e script `scripts/UpdateRuntimeLanguageFiles.ps1`

### Configuration-file notes

- `Config.xml` principale invariato
- machine runtime config: aggiunto nodo additivo `RuntimeBindings/UseEncoderTrigger`
- nessun campo ricetta aggiunto
- vecchi file macchina restano caricabili; al salvataggio il nuovo campo viene scritto esplicitamente

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- con encoder collegato abilitare `Usa trigger da encoder reale`
- senza encoder usare il modo temporizzato da fotocellula
- MultiShot Side/Left deve lavorare con encoder reale; se il trigger encoder e' disabilitato viene segnalato warning e il MultiShot non viene armato

## Version 1.2.1.1

### Release metadata

- release date: `2026-05-28`

### Release intent

Rendere la chiusura applicazione idempotente, evitando warning non utili quando token o servizi sono gia' stati disposti da un altro percorso di shutdown.

### Main functional integrations included

- cancellazione e dispose dei `CancellationTokenSource` gestiti con helper tolleranti a `ObjectDisposedException`
- `ServiceLocator.Reset()` usa dispose sicuro per i singleton gia' chiusi
- `ShutdownAllServicesAsync()` azzera i riferimenti ai servizi gia' spenti per evitare doppio dispose successivo

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- riduce warning di shutdown come `CANCELLATION_TOKEN_CANCEL_FAILED` e `Errore reset servizi: Il semaforo e' stato eliminato`
- non cambia il flusso operativo macchina

## Version 1.2.1.0

### Release metadata

- release date: `2026-05-28`

### Release intent

Correggere l'inizializzazione indici di `tblgenerale` per evitare warning startup quando lo schema reale usa la colonna `Esito_Classificazione`.

### Main functional integrations included

- `idx_EsitoClass` ora punta alla colonna reale `Esito_Classificazione`
- `EnsureIndexAsync` verifica l'esistenza delle colonne richieste prima di eseguire `ALTER TABLE ADD INDEX`
- se una colonna manca su uno schema legacy, l'indice viene saltato con log controllato invece di generare eccezione MySQL rumorosa

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica distruttiva
- nessuna rinomina colonna automatica
- indice creato solo se la colonna richiesta esiste

### Operator / maintainer notes

- riduce warning di startup DB legati a `ENSURE_INDEX_FAILED`
- non cambia il flusso operativo macchina

## Version 1.2.0.9

### Release metadata

- release date: `2026-05-28`

### Release intent

Eliminare il binding warning WPF sulla banda tolleranza della pagina Data Analysis.

### Main functional integrations included

- la banda tolleranza del grafico Data Analysis non usa piu' `TranslateTransform.Y` con binding diretto
- il posizionamento verticale ora usa `Canvas.Top="{Binding ToleranceBandTop}"`, risolto sull'elemento `Rectangle` che eredita correttamente il `DataContext`
- la visualizzazione della banda mantiene altezza e posizione calcolate dal modello dati, senza generare errori binding in output

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- nessun cambio operativo; viene solo pulito un errore binding visibile in diagnostica/debug

## Version 1.2.0.8

### Release metadata

- release date: `2026-05-27`

### Release intent

Fare in modo che la UI dei contatori difetto non conservi piu' messaggi vecchi quando il manager contatori non ha un messaggio attivo.

### Main functional integrations included

- `InspectionCounterViewModel.UpdateDefectCounter` cancella `LastErrorMessage` quando il contatore viene aggiornato senza un messaggio difetto corrente
- il conteggio storico del difetto puo' restare alto, ma la descrizione rossa viene mostrata solo se il ciclo corrente ha prodotto un messaggio
- completa la pulizia di inizio ciclo introdotta in `1.2.0.7`, evitando che la ViewModel preservi localmente un testo gia' cancellato dal `CounterManager`

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- se un pezzo e' `GOOD`, o se un difetto non viene rilevato nel ciclo corrente, il messaggio rosso precedente non resta piu' visibile sotto il contatore

## Version 1.2.0.7

### Release metadata

- release date: `2026-05-27`

### Release intent

Pulire i motivi difetto all'inizio di ogni nuovo ciclo pezzo, prima della validazione e prima di qualunque nuovo aggiornamento contatori.

### Main functional integrations included

- aggiunto `CounterManager.BeginInspectionCycle()` per cancellare i messaggi ultimo difetto all'avvio del ciclo ispezione
- `MainWindow.ProcessInspectionPairAsync` chiama la pulizia subito prima della validazione VisionPro
- i vecchi motivi difetto non restano visibili mentre il pezzo corrente e' gia' visualizzato come conforme
- eventuali nuovi difetti del pezzo corrente riscrivono i messaggi durante la validazione, quindi la UI mostra solo motivi appena rilevati

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- nella pagina `DEFECTS COUNTERS`, il messaggio rosso sotto il difetto appartiene solo all'ultimo difetto rilevato nel ciclo corrente; all'inizio del pezzo successivo viene pulito

## Version 1.2.0.6

### Release metadata

- release date: `2026-05-27`

### Release intent

Pulire automaticamente i motivi difetto quando il pezzo successivo e' conforme.

### Main functional integrations included

- quando `CounterManager.ProcessInspectionAsync` riceve un esito conforme, cancella tutti i messaggi ultimo difetto prima di notificare l'aggiornamento UI
- la pagina `DEFECTS COUNTERS` non mantiene piu' un vecchio motivo difetto dopo un ciclo `GOOD`
- il comportamento resta centralizzato nel manager contatori, senza duplicare logica nelle view

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- dopo un pezzo conforme, i contatori storici restano invariati ma il testo del motivo difetto viene pulito dalla vista operatore

## Version 1.2.0.5

### Release metadata

- release date: `2026-05-27`

### Release intent

Ripristinare nella pagina contatori difetti la visualizzazione del motivo ultimo difetto rilevato.

### Main functional integrations included

- i messaggi difetto vengono recuperati usando la chiave normalizzata del difetto, anche quando il validatore usa nomi legacy come `print_centering`, `shapetop` o `traceability`
- il refresh dei contatori non cancella piu' il motivo difetto quando il contatore e' maggiore di zero ma il ciclo di refresh non porta un nuovo testo
- il testo motivo nella card difetto e' visibile solo quando esiste realmente un messaggio da mostrare

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- nella sezione `DEFECTS COUNTERS` l'operatore vede di nuovo immagine, descrizione difetto e ultimo motivo rilevato sotto il nome del difetto
- il messaggio viene pulito dai flussi gia' esistenti di validazione/azzeramento contatori, senza cambiare il conteggio storico dei difetti

## Version 1.2.0.4

### Release metadata

- release date: `2026-05-27`

### Release intent

Rendere il banner notifiche piu' professionale e adattivo per l'uso su macchina automatica.

### Main functional integrations included

- il banner ora usa segmenti visivi compatti invece di una fascia piena a tutta altezza
- con una sola notifica, il messaggio occupa tutta la larghezza e resta centrato
- con due notifiche, il banner si divide automaticamente in due segmenti
- il cambio ricetta MES/OPC UA usa sfondo azzurro e testo blu tecnico
- la diagnostica/eventi usa sfondo ambra e testo marrone scuro
- aggiunte proprieta' shell dedicate per distinguere notifica singola, diagnostica singola e doppia notifica

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` invariato

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- l'operatore vede un unico banner ordinato; quando c'e' solo un messaggio non resta una meta' vuota
- quando OPC UA e diagnostica sono presenti insieme, le due informazioni restano distinguibili per posizione e colore

## Version 1.2.0.3

### Release metadata

- release date: `2026-05-27`

### Release intent

Unificare la notifica cambio ricetta OPC UA nel banner giallo gia' usato per gli eventi diagnostici.

### Main functional integrations included

- rimossa la seconda barra blu dedicata all'OPC UA
- il banner giallo HMI ora e' diviso in due zone: OPC UA/MES a sinistra, diagnostica/eventi a destra
- la barra resta visibile se e' presente almeno una notifica tra OPC UA e diagnostica
- il testo diagnostico del banner viene aggiornato anche quando cambia il conteggio eventi senza cambiare severita'

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` invariato

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- l'operatore vede cambio ricetta remoto ed eventi macchina in una singola barra compatta
- il popup `ACKNOWLEDGE` del cambio ricetta OPC UA resta invariato

## Version 1.2.0.2

### Release metadata

- release date: `2026-05-27`

### Release intent

Notificare all'operatore quando la ricetta in produzione viene cambiata da MES/SCADA tramite OPC UA.

### Main functional integrations included

- dopo un cambio ricetta OPC UA riuscito viene mostrato un popup informativo con pulsante `ACKNOWLEDGE`
- il popup si chiude automaticamente dopo 1 minuto se nessun operatore conferma
- la barra notifiche HMI resta aggiornata con il messaggio che la ricetta corrente e' stata cambiata da MES / OPC UA
- l'evento popup viene tracciato nei log come confermato dall'operatore o chiuso per timeout
- i nuovi testi sono gestiti da `ServerMessage` tramite `scripts/UpdateRuntimeLanguageFiles.ps1`
- la documentazione OPC UA descrive anche la notifica operatore successiva al cambio ricetta remoto

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` invariato

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- il cambio remoto ricetta resta non bloccante per la macchina
- l'operatore riceve un avviso visivo immediato anche se non si trova nella pagina Recipe Management
- se il popup non viene confermato, la barra notifiche continua a indicare che la ricetta in corsa e' stata cambiata da MES o altro server OPC UA

## Version 1.2.0.1

### Release metadata

- release date: `2026-05-27`

### Release intent

Allineare il Recipe Manager anche quando la ricetta in produzione viene cambiata da OPC UA.

### Main functional integrations included

- aggiunto refresh pubblico del Recipe Manager per aggiornare lo stato `InProduction` dalla configurazione runtime
- il cambio ricetta OPC UA richiama il refresh dopo cambio riuscito, ricetta gia' attiva e rollback
- la lista ricette mantiene la ricetta in produzione in cima anche se il cambio non parte dalla pagina HMI

### Configuration-file notes

- `Config.xml` invariato
- viene letta la `Configuration.LastRecipe` gia' aggiornata dal flusso OPC UA

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- se PLC/SCADA cambia ricetta via OPC UA, aprendo Recipe Management l'operatore vede subito in alto la nuova ricetta in produzione

## Version 1.2.0.0

### Release metadata

- release date: `2026-05-27`

### Release intent

Migliorare l'ergonomia operatore nel Recipe Manager mostrando sempre la ricetta in produzione in cima alla lista.

### Main functional integrations included

- ordinamento lista ricette con priorita' alla ricetta `InProduction`
- mantenuto l'ordinamento storico per le altre ricette
- riordino immediato dopo caricamento nuova ricetta in produzione
- confronto ricetta produzione normalizzato anche se `LastRecipe` contiene un path invece del solo nome file

### Configuration-file notes

- `Config.xml` invariato
- lettura esistente di `Configuration.LastRecipe` invariata

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- quando l'operatore apre Recipe Management, la ricetta attualmente in produzione e' la prima card visibile
- dopo un caricamento ricetta riuscito, la nuova ricetta passa subito in cima alla lista

## Version 1.1.9.9

### Release metadata

- release date: `2026-05-27`

### Release intent

Correggere il salvataggio immagini pezzo evitando che viste/camere non appartenenti all'ispezione corrente vengano salvate da cache o display precedenti.

### Main functional integrations included

- il salvataggio immagini ora richiede un record attivo per la vista corrente prima di creare file `_A` / `_Z`
- le viste senza record nello snapshot del pezzo vengono saltate invece di usare il display come fallback
- l'immagine raw `_A` privilegia il record salvato dello stesso pezzo prima del display, riducendo mismatch tra immagine a monitor e cartella

### Configuration-file notes

- `Config.xml` invariato
- nessuna modifica ai percorsi configurati; resta usato `Configuration.ImageDir`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- per un'ispezione Top+Side la cartella pezzo deve contenere solo le viste attive del pezzo, non immagini Front vecchie
- nei log `SAVE_IMAGE_VIEW_SKIPPED` indica una vista non salvata perche' non presente nello snapshot corrente

## Version 1.1.9.8

### Release metadata

- release date: `2026-05-27`

### Release intent

Creare e validare automaticamente il certificato applicazione OPC UA client richiesto dagli endpoint sicuri, inclusi endpoint Ignition con security policy `Basic256Sha256`.

### Main functional integrations included

- aggiunto check certificato applicazione dopo la validazione della configurazione OPC UA
- se il certificato client non esiste, viene creato nello store `OPC\pki\own`
- se il certificato non puo' essere creato o caricato, viene generato un errore esplicito con il percorso dello store

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` invariato
- la prima connessione sicura puo' generare nuovi file certificato sotto `OPC\pki\own`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- dopo la prima generazione del certificato client, approvarlo/trustarlo nel gateway Ignition se si usa un endpoint sicuro
- se Ignition richiede security, l'errore `ApplicationCertificate ... Basic256Sha256 cannot be found` non deve piu' comparire

## Version 1.1.9.7

### Release metadata

- release date: `2026-05-27`

### Release intent

Rendere completa la configurazione PKI del client OPC UA per server strict come Ignition, evitando l'errore `TrustedIssuerCertificates StorePath must be specified` durante la connessione.

### Main functional integrations included

- creazione automatica degli store directory OPC UA sotto `OPC\pki`: `own`, `trusted`, `issuers`, `rejected`, `trusted_user`, `user_issuers`
- configurazione esplicita di `TrustedIssuerCertificates`, `TrustedUserCertificates` e `UserIssuerCertificates`
- mantenimento degli store gia' esistenti per certificato applicazione, peer trusted e rejected

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` invariato
- la struttura certificati viene creata nel percorso runtime dell'applicazione

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- con Ignition e security attiva, approvare il certificato client della HMI nel gateway Ignition
- se `AutoAcceptUntrustedCertificates=false`, distribuire i certificati server/CA negli store `trusted` e `issuers` secondo la policy del cliente
- per primo test banco resta possibile usare `UseSecurity=false` se il server lo consente

## Version 1.1.9.6

### Release metadata

- release date: `2026-05-27`

### Release intent

Aggiungere una pagina HMI tecnica per configurare OPC UA, vedere tutti i tag scambiati e visualizzare la direzione dati tra server OPC UA e HMI.

### Main functional integrations included

- nuova vista `OpcUaConfigurationView` nell'area impostazioni macchina
- nuovo `OpcUaConfigurationViewModel` con caricamento, validazione, salvataggio e reload runtime OPC UA
- visualizzazione parametri connessione: abilitazione, `RequiredForMachineRun`, security, URL server, timeout e intervalli
- tabella completa dei tag OPC UA con `NodeId`, direzione, tipo dato, abilitazione, required e descrizione
- diagramma direzionale `Server -> HMI` e `HMI -> Server` con conteggio tag
- salvataggio su `OpcUaConfig.xml` e su `cfg_opcua_configuration`
- accesso limitato a ruoli tecnici tramite feature `opcUaConfigurationView`

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` puo' essere modificato dalla nuova pagina

### Database notes

- nessuna nuova tabella
- `cfg_opcua_configuration` viene aggiornata con upsert per setting e nodi OPC UA
- aggiunta autorizzazione default `opcUaConfigurationView` per `Expert` e `Installer`

### Operator / maintainer notes

- la pagina e' pensata per manutentori/collaudatori, non per operatori produzione
- se si abilita `RequiredForMachineRun`, la pagina mostra conferma per ricordare che RunContinuous puo' essere bloccato quando OPC UA e' disconnesso
- dopo il salvataggio il client OPC UA viene ricaricato automaticamente

## Version 1.1.9.5

### Release metadata

- release date: `2026-05-27`

### Release intent

Rendere tracciabile nello storico operativo lo stato della comunicazione OPC UA e gli eventi principali di scambio dati/comandi.

### Main functional integrations included

- aggiunto logging operativo dello stato OPC UA: disabilitato, opzionale, vincolante, connesso, disconnesso e fallito
- aggiunto audit dei comandi OPC UA ricevuti e degli ACK restituiti dalla HMI
- aggiunto audit degli ACK dati ricevuti dal server e delle write OPC UA fallite
- la richiesta di connessione ripetuta viene limitata a un log al minuto per non riempire lo storico durante server spento
- gli snapshot dati OK non vengono loggati a ogni pezzo; restano loggati gli snapshot con errori

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` invariato

### Database notes

- nessuna nuova tabella
- gli eventi vengono salvati nello storico esistente tramite `ApplicationEventLogger` / `EventLogRepository`

### Operator / maintainer notes

- cercare log con prefisso `OPCUA_` nello storico operativo per diagnosi comunicazione
- se OPC UA e' opzionale, una connessione fallita viene registrata ma non blocca la macchina
- se `RequiredForMachineRun=true`, i failure di connessione vengono evidenziati come eventi piu' severi e lo start continuo resta bloccato

## Version 1.1.9.4

### Release metadata

- release date: `2026-05-27`

### Release intent

Estendere il cambio ricetta OPC UA permettendo al sistema esterno di richiedere la ricetta sia per nome sia per ID database `tblproduzione.IdProduzione`.

### Main functional integrations included

- aggiunto comando OPC UA `RecipeId` server->HMI
- aggiunto stato OPC UA `CurrentRecipeId` HMI->server
- il cambio ricetta per ID risolve `tblproduzione.IdProduzione -> Ricetta` e usa lo stesso flusso runtime del cambio per nome
- `CommandAckOk` resta legato all'esito reale: ID trovato, ricetta caricata e runtime riallineato
- lo snapshot dati pubblica anche `CurrentRecipeId`

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` riceve i nodi default `RecipeId` e `CurrentRecipeId`
- i nodi possono essere parametrizzati o disabilitati tramite `cfg_opcua_configuration`

### Database notes

- nessuna nuova tabella
- lettura da `tblproduzione.IdProduzione` e `tblproduzione.Ricetta`
- `cfg_opcua_configuration` riceve eventuali righe nodo mancanti con `INSERT IGNORE`

### Operator / maintainer notes

- il server OPC UA puo' usare `RecipeName` oppure `RecipeId`
- se un ID non esiste o e' marcato deleted, la HMI risponde con `CommandAckOk=false`
- la logica completa e' documentata in `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md`

## Version 1.1.9.3

### Release metadata

- release date: `2026-05-26`

### Release intent

Aggiungere un handshake esplicito allo scambio OPC UA per confermare che comandi e snapshot dati siano stati processati correttamente dai due lati.

### Main functional integrations included

- aggiunti nodi comando `CommandId` e ACK HMI `CommandAckId`, `CommandAckOk`, `CommandAckMessage`, `CommandAckTimestamp`
- aggiunti nodi dati `DataSequence`, `DataPublishOk`, `DataPublishMessage`, `DataPublishTimestamp`
- aggiunti nodi ACK server `DataAckSequence`, `DataAckOk`, `DataAckMessage`
- Start, Stop e cambio ricetta scrivono ACK solo dopo verifica dell'esito reale del comando
- gli snapshot ispezione vengono pubblicati in blocco e chiusi con `DataSequence`
- la HMI logga gli ACK dati ricevuti dal server con `OPCUA_DATA_ACK_RECEIVED`

### Configuration-file notes

- `Config.xml` invariato
- `OpcUaConfig.xml` riceve nuovi nodi default handshake
- i file OPC UA gia' esistenti vengono integrati in memoria con i nodi default mancanti

### Database notes

- nessuna nuova tabella
- `cfg_opcua_configuration` riceve le nuove righe nodo via `INSERT IGNORE`

### Operator / maintainer notes

- per scambio robusto il server deve scrivere il payload comando e poi incrementare `CommandId`
- la HMI scrive `CommandAckId` per confermare il comando processato
- il server deve confermare gli snapshot HMI scrivendo `DataAckSequence = DataSequence`
- logica completa in `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md`

## Version 1.1.9.2

### Release metadata

- release date: `2026-05-26`

### Release intent

Integrare la comunicazione OPC UA come canale opzionale ma vincolante quando abilitato, mantenendo `Config.xml` separato dalla configurazione OPC e rendendo parametrizzabili nodi e setting da MySQL.

### Main functional integrations included

- aggiunto client OPC UA basato su `OPCFoundation.NetStandard.Opc.Ua`
- aggiunto file separato `OpcUaConfig.xml` nella stessa cartella del `Config.xml`
- aggiunta tabella `cfg_opcua_configuration` per setting e nodi OPC UA
- Start/Stop e cambio ricetta possono arrivare da nodi OPC UA server-to-client
- contatori produzione, ultimo esito, ricetta corrente, stato running e heartbeat vengono pubblicati su nodi OPC UA client-to-server
- lo start continuo VisionPro viene bloccato con `OPCUA_RUN_BLOCKED` se OPC UA e' abilitato, richiesto per il run macchina e non connesso

### Configuration-file notes

- nessuna nuova sezione in `Config.xml`
- nuovo file companion `OpcUaConfig.xml`
- default sicuro: `Enabled=false`, `RequiredForMachineRun=false`

### Database notes

- nuova tabella MySQL `cfg_opcua_configuration`
- nessuna modifica alle tabelle produzione esistenti
- le righe default vengono inserite con `INSERT IGNORE`, quindi i valori gia' parametrizzati non vengono sovrascritti

### Operator / maintainer notes

- per tornare al comportamento storico impostare `Enabled=false`
- per test vincolante impostare `Enabled=true` e `RequiredForMachineRun=true`; se il server OPC UA non e' connesso, la macchina non deve entrare in run continuo
- procedura completa in `Documentation/MachineHardware/opc-ua-integration-2026-05-26.md`

## Version 1.1.9.1

### Release metadata

- release date: `2026-05-26`

### Release intent

Correggere il comportamento di `ACKNOWLEDGE` sugli allarmi scarto: il riconoscimento deve resettare anche lo stato runtime usato per valutare consecutivi e percentuali, non solo chiudere il popup.

### Main functional integrations included

- `EjectionAlarmManager.ResetAlarm` ora azzera lo stato trigger dell'allarme, il contatore trigger popup, i contatori consecutivi e i buffer percentuale dei difetti monitorati
- `IntegratedAlarmCardService.AcknowledgeAlarm` riallinea l'istanza servizio con lo stato realmente resettato dal manager
- la vista diagnostica I/O recepisce anche il contatore trigger resettato dopo `AlarmAcknowledged`

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica alla configurazione degli allarmi scarto

### Database notes

- nessuna modifica schema DB
- nessuna migrazione richiesta

### Operator / maintainer notes

- dopo `ACKNOWLEDGE`, un allarme percentuale non deve riaprire subito il popup al primo difetto successivo solo perche' il buffer precedente era ancora sopra soglia
- il reset non azzera i contatori produzione globali; azzera solo lo stato runtime dell'allarme riconosciuto

## Version 1.1.9.0

### Release metadata

- release date: `2026-05-20`

### Release intent

Rendere definitivamente coerente il campo username della login con lo stile scuro del campo password, anche dopo la selezione dalla lista history.

### Main functional integrations included

- aggiunto template locale scuro per la `ComboBox` username della finestra login
- l'area chiusa del controllo, la textbox editabile interna e il pulsante freccia non usano piu' il template bianco standard Windows
- la lista utenti recenti resta scura e leggibile con selezione evidenziata

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB
- nessuna modifica alla history username locale introdotta nella release precedente

### Operator / maintainer notes

- lo username selezionato resta visibile nel campo login prima dell'inserimento password
- non sono state aggiunte nuove label, quindi non serve rigenerare i cataloghi lingua

## Version 1.1.8.9

### Release metadata

- release date: `2026-05-20`

### Release intent

Correggere la leggibilita' del campo username nella finestra login dopo l'introduzione della history utenti.

### Main functional integrations included

- il campo username editabile mantiene lo stesso stile visivo del campo password
- la lista a discesa degli username recenti usa sfondo scuro, testo bianco e selezione azzurra
- il testo digitato e il cursore restano visibili anche nel template interno della `ComboBox`

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB
- nessuna modifica alla persistenza password/credenziali

### Operator / maintainer notes

- l'operatore puo' leggere correttamente username digitato e username suggeriti
- non sono state aggiunte nuove label, quindi non serve rigenerare i cataloghi lingua

## Version 1.1.8.8

### Release metadata

- release date: `2026-05-20`

### Release intent

Migliorare l'ergonomia del login operatore ricordando gli username gia' autenticati con successo, senza salvare password.

### Main functional integrations included

- la casella username della finestra login e' diventata una `ComboBox` editabile
- al click/focus sul campo username viene mostrata la lista degli utenti gia' loggati su quella postazione
- aggiunto `LoginUserHistoryService` per salvare solo gli username in uno stato locale utente Windows
- la lista viene aggiornata solo dopo un login riuscito

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB
- nessuna password o credenziale viene salvata nella history locale

### Operator / maintainer notes

- l'operatore puo' scegliere un username gia' usato e inserire solo la password
- la history e' locale alla postazione/utente Windows e contiene al massimo 20 username recenti

## Version 1.1.8.7

### Release metadata

- release date: `2026-05-20`

### Release intent

Completamento hardening UI operatore: chiusura delle voci residue su permessi fallback, Job Tool Editor, comandi I/O tecnici e pulizia encoding nelle finestre operative.

### Main functional integrations included

- fallback `Viewer` / `Operator` ristretto alla modalita' produzione pulita: overview/canali, statistiche, contatori, allarmi, manuale e caricamento ricetta autorizzato per `Operator`
- comandi tecnici I/O privilegiati richiedono ruolo `Expert` / `Installer` / `Administrator`, conferma esplicita e macchina in stop manuale
- Job Tool Editor mostra un banner di hold runtime, nasconde i comandi tecnici ai ruoli non tecnici e chiede conferma se si esce con modifiche non salvate
- messaggi Job Tool Editor e blocco stop manuale aggiunti al catalogo `ServerMessage` e ai file lingua runtime/repo tramite script
- rimossi pattern mojibake dalle finestre operative e dai testi/log diagnostici controllati

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB
- permessi legacy presenti in DB restano filtrati lato servizio per ruoli non tecnici

### Operator / maintainer notes

- per usare forzature I/O o trigger manuali: effettuare login tecnico, premere `Stop` macchina, poi confermare il comando
- uscendo dal Job Tool Editor con modifiche non salvate il pannello chiede conferma; annullando resta nella pagina e mantiene l'hold protetto
- dopo modifiche future alle label continuare a usare `scripts\UpdateRuntimeLanguageFiles.ps1`

## Version 1.1.8.6

### Release metadata

- release date: `2026-05-20`

### Release intent

Allineamento localizzazione delle migliorie operatore: le nuove label e i messaggi di autorizzazione sono stati portati nel catalogo `ServerMessage` e nei file lingua generati dallo script.

### Main functional integrations included

- aggiunte chiavi lingua per banner/tooltip ricette operatore e messaggi di autorizzazione tecnica
- `TopMenuBarViewModel`, `AlarmsViewModel`, `DigitalIOViewModel` e `PowerFlex525ViewModel` usano `ServerMessagePersonalize` per i messaggi introdotti nell'hardening UI
- `scripts/UpdateRuntimeLanguageFiles.ps1` aggiorna sia i file runtime `C:\QtisVision\Language\*.json` sia i cataloghi versionati `Localization\*.json`

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB

### Operator / maintainer notes

- le nuove stringhe operative sono disponibili in ENG/ITA senza hardcode locale nelle view principali
- dopo modifiche future alle label, usare `scripts\UpdateRuntimeLanguageFiles.ps1` per riallineare runtime e baseline

## Version 1.1.8.5

### Release metadata

- release date: `2026-05-20`

### Release intent

Chiarezza operativa sulla pagina ricette dopo l'hardening permessi: distinguere in UI tra consultazione, caricamento in produzione e modifica tecnica della ricetta.

### Main functional integrations included

- banner ricette aggiornato: per ruoli senza modifica mostra testo dinamico in base al permesso di caricamento produzione
- tooltip dei pulsanti ricetta resi dinamici e visibili anche quando il pulsante e' disabilitato
- `CanSaveRecipeCommand` ora richiede il permesso specifico `saveRecipe`, non solo il generico `recipeManagement`
- pulsanti elimina/duplica ricetta legati ai permessi specifici `deleteRecipe` / `createRecipe`

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB
- vengono usati i permessi esistenti `loadRecipeToProduction`, `saveRecipe`, `deleteRecipe`, `createRecipe`

### Operator / maintainer notes

- un operatore autorizzato al cambio produzione vede chiaramente che puo' caricare ricette, ma non modificarle
- se un pulsante e' disabilitato, il tooltip indica se manca una selezione, manca il permesso o non ci sono modifiche da salvare

## Version 1.1.8.4

### Release metadata

- release date: `2026-05-19`

### Release intent

Secondo step hardening UI operatore: le funzioni tecniche restano bloccate per `Operator` e `Viewer` anche se il database permessi contiene autorizzazioni legacy troppo ampie.

### Main functional integrations included

- `AuthorizationService` separa l'accesso alla pagina ricette (`viewRecipeDetails` / ruolo operativo) dal permesso di modifica ricetta (`recipeManagement`)
- aggiunta guardia runtime per impedire a `Operator` / `Viewer` l'accesso a modifica ricetta, salvataggio/creazione/eliminazione ricette, editor tool, configurazioni macchina, allarmi scarto, diagnostica PC, DataInspector, PowerFlex e Automation
- pulsanti "diagnostica PC" nella top-bar e nella pagina eventi disabilitati per ruoli non tecnici con tooltip esplicito

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB
- eventuali permessi legacy presenti in `pulsarsdk_auth.roles_authorizations` non possono piu' abilitare funzioni tecniche a `Operator` o `Viewer`

### Operator / maintainer notes

- `Operator` puo' continuare ad aprire la pagina ricette per consultazione e flusso produzione; il caricamento resta controllato da `loadRecipeToProduction`
- per modificare ricette, tool, configurazioni macchina o aprire diagnostiche tecniche serve login `Expert`, `Installer` o `Administrator`

## Version 1.1.8.3

### Release metadata

- release date: `2026-05-19`

### Release intent

Hardening dell'operativita' pannello per utenti non esperti: riduzione dei permessi fallback, separazione tra modifica ricetta e caricamento in produzione, e conferme esplicite sulle azioni macchina piu' sensibili.

### Main functional integrations included

- fallback `Viewer` limitato a overview, contatori/statistiche, allarmi, manuale e assistenza
- fallback `Operator` limitato al flusso produzione e al caricamento ricetta autorizzato; escluse configurazioni, diagnostica profonda, DataInspector, Automation e PowerFlex
- `RecipeManagerViewModel` usa il permesso specifico `loadRecipeToProduction` per caricare una ricetta in produzione
- chiusura HMI dal pulsante top-bar abilitata solo per `Expert`, `Installer` o `Administrator`
- azioni I/O critiche (`All outputs HIGH/LOW`, restore config, trigger temporizzati manuali, simulazione fotocellula) richiedono ruolo tecnico e conferma
- azioni PowerFlex critiche (`Apply`, reset fault, clear fault history) richiedono ruolo tecnico e conferma

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica schema DB; i permessi DB restano autoritativi quando disponibili
- fallback locale usato solo quando il DB permessi non e' disponibile o cache non caricata

### Operator / maintainer notes

- operatori e viewer vedono meno pagine tecniche quando il sistema usa i permessi fallback
- per modifiche macchina, I/O, drive o diagnostica tecnica serve login tecnico
- il cambio ricetta in produzione resta possibile solo se il ruolo dispone di `loadRecipeToProduction`

## Version 1.1.8.2

### Release metadata

- release date: `2026-05-14`

### Release intent

Fix del caso `Job Tool Editor` con tool modificato: uscendo dalla pagina non deve restare attivo l'hold `job-editor` generato da eventi tardivi del controllo Cognex durante unload/dispose.

### Main functional integrations included

- `JobToolEditorViewModel` marca la view come in uscita prima di rilasciare l'hold editor
- `OnToolBlockModified()` ignora gli eventi di modifica ricevuti durante lo scarico della pagina
- `JobToolEditorView` blocca gli handler `SubjectChanged` / `Changed` durante `Unloaded`, evitando che il dispose del controllo tool riaggiunga l'hold

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- anche dopo una modifica tool, uscire da `Job Tool Editor` deve riportare la macchina in `RunContinuous` salvo `Stop` manuale
- nei log eventuali eventi tardivi ignorati compaiono come `JOBEDITOR_TOOL_CHANGED_IGNORED`

## Version 1.1.8.1

### Release metadata

- release date: `2026-05-14`

### Release intent

Ripristino automatico della produzione continua all'uscita da `Job Tool Editor`, mantenendo pero' lo stop manuale come intenzione operatore da rispettare.

### Main functional integrations included

- `JobToolEditorViewModel` distingue l'hold temporaneo dell'editor da `manual-stop`
- all'uscita dalla pagina editor viene rilasciato l'hold `job-editor` e viene richiesto il ritorno a `RunContinuous` se non e' attivo `manual-stop`
- lo stop effettuato dal pulsante `Stop` resta protetto: in quel caso serve ancora premere `Start`
- `MachineRuntimeService` espone un helper mirato per verificare un hold specifico senza parsing testuale

### Configuration-file notes

- nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- entrare nel `Job Tool Editor` ferma temporaneamente VisionPro per modifica/salvataggio tool
- uscire dalla pagina riporta automaticamente la macchina in stato running salvo stop manuale
- se l'operatore preme `Stop`, il ritorno in running richiede esplicitamente `Start`

## Version 1.1.8.0

### Release metadata

- release date: `2026-05-14`

### Release intent

Fix ACK degli allarmi scarto: il pulsante `ACKNOWLEDGE` del popup deve chiudere il ciclo dell'allarme senza richiedere `Ricarica` nella pagina `Allarmi Scarto`.

### Main functional integrations included

- `AlarmNotificationWindow` forza lo spegnimento del canale fisico dell'allarme quando l'operatore preme `ACKNOWLEDGE`
- `IntegratedAlarmCardService` espone il riconoscimento del singolo allarme e resetta `IsTriggered` nel singleton runtime
- `DigitalIOViewModel` riceve l'evento di acknowledge e aggiorna subito griglia e riepilogo `Allarmi Scarto`

### Configuration-file notes

- nessuna modifica a `Config.xml`
- nessuna modifica al template runtime macchina

### Database notes

- nessuna migrazione schema
- l'ACK resta tracciato via audit log best-effort quando il servizio e' disponibile

### Operator / maintainer notes

- dopo `ACKNOWLEDGE`, lo stesso allarme puo' scattare nuovamente senza ricaricare manualmente la pagina
- l'uscita fisica associata all'allarme viene riportata bassa anche per allarmi con durata impulso configurata

## Version 1.1.7.9

### Release metadata

- release date: `2026-05-14`

### Release intent

Rollback mirato dei due interventi del `2026-05-13` sugli allarmi scarto config-driven e sul remapping IO `DO02/DO03`, per tornare al comportamento precedente in cui il runtime usa il canale salvato sull'allarme e la baseline template resta `SIDE=DO01`, `TOP=DO02`, `REJECT=DO03`.

### Main functional integrations included

- `MainWindow.OnAlarmTriggered` torna a comandare l'uscita fisica letta da `EjectionAlarmConfig.OutputChannelId`
- `IntegratedAlarmCardService` non normalizza piu' gli allarmi caricati/aggiunti/aggiornati sui valori `BlockingAlarmOutput` / `NonBlockingAlarmOutput`
- default legacy degli alarm-card riportati a `DO0`
- creazione e test manuale degli allarmi scarto riportati sul canale salvato sull'allarme
- template/default machine runtime riportati a `OUT_CAMERA_SIDE_TRIGGER -> DO01`, `OUT_CAMERA_TOP_TRIGGER -> DO02`, `OUT_REJECT_SOLENOID -> DO03`

### Configuration-file notes

- nessuna migrazione schema `Config.xml`
- i file macchina esistenti non vengono sovrascritti automaticamente
- per tornare alla baseline precedente, verificare il file runtime macchina in commissioning

### Database notes

- nessuna migrazione schema
- le righe esistenti in `cfg_alarm_cards.SignalID` / `OutputChannelId` tornano a essere usate come sorgente del canale fisico per gli allarmi scarto

### Operator / maintainer notes

- questa release ripristina il comportamento pre-`1.1.7.7`/`1.1.7.8` per gli allarmi scarto
- verificare fisicamente i canali `DO0`, `DO1`, `DO2` e `DO3` prima della produzione se il database contiene allarmi abilitati

## Version 1.1.7.8

### Release metadata

- release date: `2026-05-13`

### Release intent

Make ejection/alarm physical outputs fully configuration-driven: the active output is always resolved from `Config.xml` (`BlockingAlarmOutput` / `NonBlockingAlarmOutput`) instead of trusting stale `SignalID` / `OutputChannelId` values from DB or XML backup.

### Main functional integrations included

- `MainWindow.OnAlarmTriggered` now recalculates the effective physical output from `Config.xml` at trigger time and logs the stored channel versus the configured channel
- `IntegratedAlarmCardService` normalizes loaded, added and updated alarm-card rows to the current configured outputs before syncing with the alarm manager or saving backup XML
- manual alarm-output tests in `EjectionAlarmCardViewModel` use the configured output for the alarm `SignalType`
- legacy `Database.AlarmCardRepository` defaults moved from `DO0` to safe alarm defaults `DO40` / `DO41`
- `DigitalIOViewModel` alarm-output helper also resolves the configured output before any physical write

### Configuration-file notes

- no schema change
- `Config.xml` is now the runtime authority for alarm outputs:
  - `BlockingAlarmOutput`
  - `NonBlockingAlarmOutput`
- changing one of these values from the configuration UI takes effect after the alarm service reload/save path without requiring edits to individual DB rows

### Database notes

- no schema change
- existing `cfg_alarm_cards.SignalID` rows with obsolete channels are overwritten in memory and on the next save/update with the configured channel for their `SignalType`

### Operator / maintainer notes

- do not use alarm-card `SignalID` as the machine authority; it is now treated as a persisted mirror of `Config.xml`
- if a stale row still contains `DO0` / `DO1`, logs will show the correction through `ALARM_CONFIG_OUTPUT_APPLIED` or the runtime skip through `ALARM_IO_SKIPPED_UNCONFIGURED_CHANNEL`

## Version 1.1.7.7

### Release metadata

- release date: `2026-05-13`

### Release intent

IO safety correction for the two-camera timed-trigger machine: prevent legacy alarm defaults from driving `DO0`/`DO1`, and align camera trigger defaults with the real wiring where SIDE is on `DO02` and TOP is on `DO03`.

### Main functional integrations included

- machine runtime template updated: `OUT_CAMERA_SIDE_TRIGGER -> DO02`, `OUT_CAMERA_TOP_TRIGGER -> DO03`, `OUT_REJECT_SOLENOID -> DO04`
- `DigitalIOViewModel` default/migration paths updated to the same physical trigger mapping, avoiding reintroduction of the old `SIDE=DO01`, `TOP=DO02` baseline on new or recovered configs
- ejection/alarm-card default outputs moved away from `DO0`/`DO1` to the configured alarm defaults `DO40` / `DO41`
- new alarm creation now writes both `SignalID` and `OutputChannelId` from `Config.xml` (`BlockingAlarmOutput` / `NonBlockingAlarmOutput`)
- `MainWindow.OnAlarmTriggered` now skips physical alarm writes when the alarm channel is not one of the configured alarm outputs, preventing stale DB rows such as `DO0` or `DO1` from energizing spare/camera-adjacent outputs

### Configuration-file notes

- existing machine configs are not overwritten automatically
- for this machine, verify `machine_runtime_config.xml` has:
  - `OUT_CAMERA_SIDE_TRIGGER` on `DO02`
  - `OUT_CAMERA_TOP_TRIGGER` on `DO03`
  - no alarm-card output configured as `DO0` or `DO1`
- verify `Config.xml` alarm outputs if the alarm-card feature is used:
  - `BlockingAlarmOutput=DO40`
  - `NonBlockingAlarmOutput=DO41`

### Database notes

- no schema change
- existing rows in `cfg_alarm_cards` may still contain legacy `SignalID` / `OutputChannelId` values `DO0` or `DO1`; these rows are now ignored for physical output writes unless they match the configured alarm outputs

### Operator / maintainer notes

- `DO0` and `DO1` must remain free unless explicitly assigned during commissioning
- if `DO0` is already latched high from a previous run, reset the physical output or power-cycle the IO card after deploying this release
- after deployment, trigger logs should show camera pulses on `DO02` and `DO03`; alarm logs with stale `DO0` / `DO1` will show `ALARM_IO_SKIPPED_UNCONFIGURED_CHANNEL`

## Version 1.1.7.6

### Release metadata

- release date: `2026-05-12`

### Release intent

Consolidated production-hardening release for the mature machine baseline: database resilience, IO/runtime watchdogs, alarm persistence, styled operator notifications, full `Config.xml` editing from PreferenceView, diagnostics, audit trail, recipe transition safety, counter scale-up and OneDrive baseline alignment.

### Main functional integrations included

- PreferenceView is now the complete `Config.xml` editor for the main application sections: VisionPro outputs, auto-switch, diagnostics thresholds, IO/ejection, image save policy, PowerFlex 525, DB archive/cleanup and analytics dashboard visibility
- PreferenceView layout changed to an asymmetric three-column layout so the configuration editor receives the available working space while language and user cards remain compact
- native `MessageBox` dialogs replaced by the styled `SystemNotificationWindow`; eject/alarm flows use the industrial `AlarmNotificationWindow`
- alarm card and ejection alarm handling persisted through `alarm_events`, with dedicated services/repository and operator-facing notification flow
- database layer hardened with pooled connection-string helper, short connection timeout, database-name validation, `idx_DataeOra` on `tblgenerale` and safer initialization paths
- production counters migrated from `int` to `long`; `tblglobalcounters.Counter` is migrated to `BIGINT`; counter writes are wrapped in an atomic transaction
- startup health checker, diagnostics banner and warning surfaces added for better commissioning visibility
- IO/runtime safety improved with output watchdog support, better Advantech scan/dispose logging and Cognex image disposal before record replacement
- audit trail and password migration hardening added, including BCrypt migration support
- recipe transition flow isolated in `RecipeTransitionService` with rollback/audit behavior
- recipe validation and global technical/manual documentation added for maintainers and operators

### Configuration-file notes

- no breaking `Config.xml` schema change is required for existing machines
- existing application configuration fields are now exposed and managed from PreferenceView instead of requiring manual XML editing
- operators and maintainers should still keep the separation defined in `AGENTS.md`: machine mapping in machine config, product vision/tolerances in recipe, transient state only in runtime memory

### Database notes

- `alarm_events` is used for persisted ejection/alarm-card history
- `tblgenerale` receives `idx_DataeOra` for faster daily/range queries
- `tblglobalcounters.Counter` is migrated to `BIGINT` for long-running production counters
- connection pooling and timeout settings are standardized through `DbConnectionStringHelper`

### Operator / maintainer notes

- use PreferenceView as the normal editor for application configuration instead of editing `Config.xml` directly
- check the styled warning/error notifications and startup diagnostics banner during commissioning
- use JSON/event logs and `alarm_events` when investigating ejection, alarm-card or runtime issues
- OneDrive synchronization keeps shared backup folders intact and excludes build outputs, IDE caches and binaries

## Version 1.1.7.5

### Release metadata

- release date: `2026-05-05`

### Release intent

Auto-resume VisionPro continuous run when the operator opens the All Channel overview.

### Main functional integrations included

- `Channel1` / All Channel navigation now performs a dedicated VisionPro run-state check
- the check reads `IsRunningContinuously` from the Cognex manager off the UI thread
- if VisionPro is already running, no start command is sent
- if VisionPro is stopped and no protected runtime hold is active, continuous run is started automatically
- a previous manual-stop hold is released because the All Channel click is treated as an explicit operator request to return to the overview running state

### Configuration-file notes

- no XML schema changes

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- use All Channel as the normal production return point: if VisionPro was stopped manually, opening All Channel will request continuous run again
- protected holds such as recipe save, job editor and shutdown remain respected
- logs include `VISIONPRO_ALLCHANNEL_ALREADY_RUNNING`, `VISIONPRO_ALLCHANNEL_AUTO_START` or `VISIONPRO_ALLCHANNEL_START_SKIPPED`

## Version 1.1.7.4

### Release metadata

- release date: `2026-04-30`

### Release intent

Improve repeatability of the single-photocell timed-trigger acquisition by removing UI dispatcher latency from the critical trigger path.

### Main functional integrations included

- added a timed-trigger fast path for `TimedFromPhotocell`
- cached the real photocell channel and TOP/SIDE output route outside the UI thread
- added source timestamp compensation so scheduler delays subtract the time already elapsed since the hardware edge
- pre-resolved output channel, board, physical channel and polarity before scheduling
- writes timed camera pulses directly from the background scheduler instead of dispatching output writes back to the UI
- added trace messages for `FAST_PATH_PHOTOCELL_ACCEPTED`, `effectiveDelay`, `compensation`, and direct pulse start/end

### Configuration-file notes

- no XML schema changes
- existing timed-trigger fields remain valid: `TriggerSchedulingMode`, `PhotocellDebounceMs`, `MinimumRetriggerGapMs`, TOP/SIDE delay and pulse fields

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- for repeatability tests, check that the log shows `FAST_PATH_PHOTOCELL_ACCEPTED` before `TIMED_TRIGGER_SCHEDULED`
- the configured delay remains the target delay from photocell edge; the runtime now compensates UI/software latency before arming the output pulse
- if the fast path is disabled by an incomplete configuration, the system falls back to the older UI-safe path and the log will make the route visible

## Version 1.1.7.3

### Release metadata

- release date: `2026-04-29`

### Release intent

Make the Machine Configuration page clearer for the real single-photocell / timed-trigger machine by promoting the true production bindings and demoting optional lighting and legacy fields.

### Main functional integrations included

- introduced a new primary signal-assignment area focused on:
  - product photocell
  - TOP camera output
  - SIDE camera output
- added dynamic flow summaries that show the effective photocell and physical TOP/SIDE/Reject outputs
- connected TOP/SIDE assignment directly to the intervention points actually used by the timed scheduler
- moved legacy trigger binding, external trigger consent, and lighting outputs into an optional advanced area
- optional lighting controls now stay collapsed unless they are really relevant for the current machine setup

### Configuration-file notes

- no XML schema changes
- the active machine file still stores the same runtime bindings and intervention points, but the UI now exposes the production-relevant pieces in a clearer way

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- for the current machine, the authoritative timed-trigger outputs are the TOP/SIDE intervention-point signal assignments
- `BoundCameraTriggerSignalCode` remains only as a legacy/fallback binding and should not be the main field used during standard commissioning
- separate lighting bindings are now treated as optional because the current machine does not rely on dedicated lighting outputs in the normal flow

## Version 1.1.7.2

### Release metadata

- release date: `2026-04-29`

### Release intent

Stabilize the Machine Configuration editing flow by preventing `ReservedForRealSignal` toggles from reapplying the live runtime while the WPF grids are still editing.

### Main functional integrations included

- changing `ReservedForRealSignal` no longer queues a runtime preview reapply
- the flag still marks the configuration as dirty and refreshes filtered views only when the grids are not busy
- this removes a critical collision between `DataGrid` edit transactions and live machine-preview reconfiguration

### Configuration-file notes

- no XML schema changes
- no DB schema changes

### Operator / maintainer notes

- `ReservedForRealSignal` is a machine-classification flag and should not be used to control the timed-trigger flow
- for the single-photocell machine, the trigger behavior must be governed by:
  - `ProductPhotocellSignalCode`
  - `TriggerSchedulingMode`
  - `VirtualConveyorEnabled`
  - timed delays in milliseconds

## Version 1.1.7.1

### Release metadata

- release date: `2026-04-29`

### Release intent

Eliminate the remaining commissioning blind spots around the timed trigger flow by adding safer live-preview behavior, explicit configuration warnings, and automatic parking of the unused spare output.

### Main functional integrations included

- runtime preview is now deferred while the Digital I/O grids are still editing rows, reducing the risk of `ItemsControl is inconsistent with its items source`
- added explicit trigger-flow diagnostics for the most common misconfigurations seen on the machine:
  - product photocell bound to the wrong signal
  - external trigger enable bound to the same signal
  - timed delays saved with the wrong order of magnitude
- added parking logic for `OUT_SPARE_DO00`:
  - if the spare output is not referenced anywhere in runtime bindings or intervention points
  - it is forced LOW during init / preview / save / load

### Configuration-file notes

- `machine_runtime_config.xml` remains backward compatible
- the machine file is now validated more aggressively at runtime and the warnings are written to the standard log

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- on the machine configuration you attached, the timed trigger still cannot work correctly until these fields are corrected:
  - `ProductPhotocellSignalCode=IN_PRODUCT_PHOTOCELL`
  - `TriggerSchedulingMode=TimedFromPhotocell`
  - `TopTriggerBaseDelayMs` / `SideTriggerBaseDelayMs` must be in realistic milliseconds, not hundreds of thousands
- `OUT_SPARE_DO00` is not part of the TOP/SIDE timed trigger flow and should remain a spare diagnostic output only

## Version 1.1.7.0

### Release metadata

- release date: `2026-04-29`

### Release intent

Strengthen the timed-trigger diagnostics and configuration validation so field commissioning can immediately distinguish between legacy tracking flow, timed scheduler flow, and machine-configuration errors.

### Main functional integrations included

- added timed-trigger route tracing with:
  - logical output name
  - configured signal code
  - physical `Board/Channel`
  - delay
  - pulse width
- added trigger-flow configuration validation warnings for:
  - `VirtualConveyorEnabled=false` with `TriggerSchedulingMode=VirtualConveyor`
  - unusually large timed delays
  - `ExternalTriggerEnable` mapped to the same signal as the product photocell
  - incomplete output rows saved in the machine file
- filtered incomplete signal / intervention rows out of the persisted machine runtime configuration

### Configuration-file notes

- incomplete blank output rows are no longer persisted into `machine_runtime_config.xml`
- loading / saving now produces explicit warnings when the trigger flow configuration is inconsistent with the selected runtime mode

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- if the machine file shows `TriggerSchedulingMode=VirtualConveyor`, the timed scheduler will not arm from the photocell
- on the configuration you provided, the following values are suspicious for timed commissioning:
  - `TriggerSchedulingMode=VirtualConveyor`
  - `TopTriggerBaseDelayMs=560000`
  - `SideTriggerBaseDelayMs=570000`
  - `ExternalTriggerEnableSignalCode=IN_PRODUCT_PHOTOCELL`

## Version 1.1.6.9

### Release metadata

- release date: `2026-04-29`

### Release intent

Close the remaining ambiguity between `TriggerSchedulingMode` and `VirtualConveyorEnabled`, persist the timed-trigger runtime bindings in the machine XML/template, and make the photocell flow diagnosable directly from the logs.

### Main functional integrations included

- `TimedFromPhotocell` now forces `VirtualConveyorEnabled=false` inside the runtime view model so the machine cannot stay in a mixed state
- active machine configuration and template configuration are auto-upgraded when the timed-trigger runtime-binding fields are missing
- the photocell flow now logs the effective execution path:
  - deterministic timed scheduler
  - legacy tracked-position flow with virtual conveyor
  - legacy tracked-position flow waiting for a real encoder
- updated localization text for the timed-trigger scheduling mode so it reflects the current implemented runtime

### Configuration-file notes

- `machine_runtime_config.xml` and `machine_runtime_config.template.xml` now persist / expose:
  - `TriggerSchedulingMode`
  - `PhotocellDebounceMs`
  - `MinimumRetriggerGapMs`
  - `TopTriggerBaseDelayMs`
  - `SideTriggerBaseDelayMs`
  - `TopTriggerPulseMs`
  - `SideTriggerPulseMs`
- if these nodes are missing in an older machine runtime configuration, the software rewrites the file with the current schema at load/init

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- disabling `VirtualConveyorEnabled` does not activate timed pulses by itself: the production mode must be `TriggerSchedulingMode=TimedFromPhotocell`
- if the scheduling mode remains `VirtualConveyor` and the virtual conveyor is disabled, the machine will wait for a real encoder progression before firing intervention points

## Version 1.1.6.8

### Release metadata

- release date: `2026-04-29`

### Release intent

Complete the commissioning and traceability layer of the timed photocell trigger flow so the machine can move into structured field testing and final tuning.

### Main functional integrations included

- added timed-trigger commissioning commands in the Digital I/O page:
  - simulate photocell edge
  - force TOP trigger
  - force SIDE trigger
  - reset timed diagnostics
- added timed-trigger runtime diagnostics:
  - batch counters
  - TOP/SIDE shot counters
  - last observed TOP/SIDE delays
  - average observed TOP/SIDE delays
  - output mapping summary
- extended the timed-trigger runtime event handling so the diagnostics stay aligned with every scheduled / executed batch
- added a dedicated commissioning procedure:
  - `Docs/Commissioning/08_Photocell_Timed_Trigger_Commissioning.md`

### Configuration-file notes

- no XML schema changes compared with `1.1.6.7`
- the release adds commissioning diagnostics and commands on top of the existing timed-trigger machine configuration

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- the machine is now ready for structured timed-trigger field validation and delay tuning
- remaining work is mainly machine-side tuning and production qualification, not baseline architecture

## Version 1.1.6.7

### Release metadata

- release date: `2026-04-29`

### Release intent

Integrate the first active runtime path for deterministic photocell-timed TOP/SIDE trigger scheduling while preserving the legacy tracking flow as fallback.

### Main functional integrations included

- added `PhotocellTimedTriggerService` based on a monotonic `Stopwatch` clock
- integrated `TimedFromPhotocell` inside the real photocell input flow:
  - accepted photocell edge
  - timed batch creation
  - independent TOP / SIDE scheduling
  - trigger output pulse execution on the configured real signals
- added structured runtime diagnostics for the timed trigger flow:
  - edge accepted
  - output scheduled
  - output ON
  - output OFF
  - batch completed / cancelled / error
- added timed-trigger runtime summary and last-event diagnostics inside the Digital I/O commissioning page
- automatic cancellation of pending timed batches on:
  - runtime preview apply
  - configuration save
  - configuration load
  - connection mode switch
  - initialization
  - dispose / shutdown

### Configuration-file notes

- no schema break: the `RuntimeBindings` fields introduced in `1.1.6.6` are now actively used by the runtime when `TriggerSchedulingMode=TimedFromPhotocell`
- TOP/SIDE trigger output routing still follows the machine configuration signal mapping

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- `TimedFromPhotocell` now activates a real runtime path for TOP/SIDE timed pulses from the product photocell
- the legacy tracked-position flow remains available when the machine stays on `VirtualConveyor`
- the timed path currently focuses on deterministic camera trigger generation; the broader legacy tracking flow is intentionally preserved as fallback for the rest of the machine runtime

## Version 1.1.6.6

### Release metadata

- release date: `2026-04-29`

### Release intent

Prepare the machine baseline for the upcoming deterministic photocell-timed trigger flow by persisting the new scheduling mode and its machine-level timing parameters.

### Main functional integrations included

- added the new machine-level scheduling mode `TriggerSchedulingMode`
- added persistent machine parameters for the timed trigger baseline:
  - `PhotocellDebounceMs`
  - `MinimumRetriggerGapMs`
  - `TopTriggerBaseDelayMs`
  - `SideTriggerBaseDelayMs`
  - `TopTriggerPulseMs`
  - `SideTriggerPulseMs`
- updated the Digital I/O commissioning view with a dedicated configuration block for the future `TimedFromPhotocell` scheduler
- kept the current legacy runtime trigger flow active until the dedicated scheduler service is integrated in a following release

### Configuration-file notes

- `machine_runtime_config.xml` now persists the new timed-trigger preparation fields inside `RuntimeBindings`
- the new fields are machine configuration, not recipe configuration

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- this release prepares the machine baseline and the UI for the future timed trigger rollout
- selecting `TimedFromPhotocell` currently stores and displays the machine settings but does not yet replace the active legacy runtime trigger path

## Version 1.1.6.5

### Release metadata

- release date: `2026-04-29`

### Release intent

Restore the machine configuration file as the single source of truth for camera-trigger signal routing during commissioning.

### Main functional integrations included

- removed runtime inference that rebuilt `TriggerCamera` signal routing from the intervention-point name (`TOP` / `SIDE`)
- kept only safe legacy normalization for old signal codes:
  - `OUT_LIGHT_TOP`
  - `OUT_LIGHT_SIDE`
  - `OUT_CAMERA_TRIGGER`
- preserved default-point generation only for brand-new configurations with missing standard points

### Configuration-file notes

- no XML schema changes
- the `SignalCode` stored in `machine_runtime_config.xml` now directly governs the runtime mapping for `TriggerCamera` intervention points

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- if a `TriggerCamera` point has no `SignalCode`, the runtime now logs a warning and skips that point
- when validating TOP/SIDE shot positions, always check the saved `SignalCode` of the intervention point together with the `Machine outputs` board/channel mapping

## Version 1.1.6.4

### Release metadata

- release date: `2026-04-28`

### Release intent

Stabilize the commissioning trigger flow after the first virtual-conveyor intervention-point rollout by blocking duplicated photocell detections, tracing every trigger pulse on the physical output, and reapplying edited encoder/photocell/intervention settings directly to the live machine controller.

### Main functional integrations included

- added photocell duplicate protection in the Digital I/O runtime:
  - repeated photocell edges inside `80 ms` and within `8 mm` are ignored as bounce/noise
  - accepted and ignored photocell events are now logged with encoder position and machine quota
- added explicit trigger-output trace logs:
  - pulse start
  - pulse end
  - board / channel
  - intervention-point reason
- added duplicate physical-output protection for camera trigger points:
  - if two enabled `TriggerCamera` intervention points resolve to the same physical output for the same product, the second pulse is suppressed and logged
- added debounced live runtime reapply for machine commissioning edits:
  - encoder zero / photocell quota
  - intervention-point positions
  - runtime signal bindings
  - trigger/reject/lighting modes
  - the controller is reinitialized in memory and stale tracked products are cleared without requiring a manual reload first

### Configuration-file notes

- no XML schema changes were introduced
- existing `machine_runtime_config.xml` files remain compatible
- operators still need `Save configuration` to persist edits to disk; the new live reapply only updates the active runtime immediately

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- if one photocell activation still generates two different trigger pulses, verify whether `CAMERA_TRIGGER_TOP` and `CAMERA_TRIGGER_SIDE` are intentionally mapped to two distinct machine outputs
- if both camera trigger points are mapped to the same physical output, the runtime now logs the overlap and suppresses the repeated pulse for the same product
- after editing `Machine zero` or `Photocell mm`, the runtime now clears active tracked products on purpose so old quotas cannot remain armed

## Version 1.1.6.3

### Release metadata

- release date: `2026-04-28`

### Release intent

Incremental commissioning cleanup on top of the colleague-shared `1.1.6.2` baseline. The goal is to keep the merged production branch while removing the remaining mismatch between the I/O machine configuration, recipe trigger policy, and two-camera hardware trigger wiring.

### Main functional integrations included

- restored robust intervention-point `SignalCode` persistence by binding editable ComboBox text directly to the saved value
- restored the bench-mode guard so `Photocell -> lighting bench mode` cannot bypass camera intervention tracking when TOP/SIDE camera points are configured
- migrated the default I/O flow to the real two-camera machine mapping:
  - `IN_PRODUCT_PHOTOCELL -> DI00`
  - `OUT_CAMERA_SIDE_TRIGGER -> DO01`
  - `OUT_CAMERA_TOP_TRIGGER -> DO02`
  - `OUT_SPARE_DO00 -> DO00` and not used by the normal flow
- replaced the old default `LIGHTS_ON / CAMERA_TRIGGER / LIGHTS_OFF` sequence with two explicit `TriggerCamera` intervention points:
  - `CAMERA_TRIGGER_TOP` at `74.0 mm`
  - `CAMERA_TRIGGER_SIDE` at `76.0 mm`
- added runtime migration of legacy signal names so older machine configs using `OUT_LIGHT_TOP`, `OUT_LIGHT_SIDE`, or `OUT_CAMERA_TRIGGER` are normalized in memory to the two-camera mapping
- kept recipe camera trigger-delay fields for compatibility, while preserving the existing I/O-driven policy that does not write those delays to camera hardware at startup

### Configuration-file notes

- no new XML schema fields are introduced
- existing `machine_runtime_config.xml` files can still load
- legacy I/O names are migrated in memory; operators should save the machine configuration once after verification to persist the cleaned mapping

### Database notes

- no MySQL schema changes are required

### Operator / maintainer notes

- for this machine test, keep `Virtual conveyor in HW` enabled when no encoder is connected
- keep `TriggerMode=Internal` during commissioning unless `IN_EXTERNAL_TRIGGER_ENABLE` is intentionally wired and tested
- do not use `LIGHTS_ON` / `LIGHTS_OFF` for the two-camera trigger test; camera outputs are pulsed by `CAMERA_TRIGGER_TOP` and `CAMERA_TRIGGER_SIDE`

## Version 1.1.6.2

### Release metadata

- release date: `2026-04-28`

### Release intent

Bilateral merge release generated to unify the local engineering baseline and the colleague shared `lastRelease` baseline into one aligned production branch.

### Main functional integrations included

- preserved the commissioning hotfixes from shared baseline:
  - intervention-point `SignalCode` persistence
  - guided quick machine I/O setup
  - safer bench-mode behavior with active camera intervention points
- preserved the local runtime/database stabilizations:
  - legacy `tblproduzione.TimeFine` zero-date recovery path
  - startup policy that skips recipe camera trigger-delay writes because trigger generation is driven by machine I/O
- re-aligned both repositories on the same merged release for future sync operations

### Configuration-file notes

- no new `Config.xml` schema field is required by the merge itself
- recipe trigger-delay fields remain present for compatibility, but startup/runtime policy now treats them as non-applied on I/O-driven machines

### Database notes

- `tblproduzione.TimeFine` remains nullable
- legacy zero-date production rows are still expected to be normalized to `NULL`
- no new table or column is introduced by this merge release

### Operator / maintainer notes

- after this merge, local repo and shared `lastRelease` should be treated again as one synchronized baseline
- startup logs can legitimately show trigger-delay skip entries on machines using digital-output trigger generation

## Version 1.1.6.1

### Release metadata

- release date: `2026-04-28`

### Release intent

Commissioning hotfix for the real-machine two-camera I/O flow.

### Main functional integrations included

- made intervention-point `SignalCode` editing more robust by binding the editable ComboBox text directly to the saved signal code
- prevented the legacy `Photocell -> lighting bench mode` from bypassing product tracking when active camera intervention points are configured
- kept the bench mode available for simple sensor-to-output tests when no camera intervention points are active

### Configuration-file notes

- no new XML schema fields are introduced
- existing `PhotocellLightingBenchModeEnabled` remains supported, but it no longer blocks camera intervention tracking when configured TOP/SIDE trigger points have valid signal codes

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- for the production commissioning flow keep TOP/SIDE trigger intervention points enabled and assigned to the real camera output signals
- if the bench checkbox is accidentally left enabled, the camera intervention flow remains active as long as those camera points have valid signal codes

## Version 1.1.6.0

### Release metadata

- release date: `2026-04-28`

### Release intent

Incremental runtime-stability release generated to recover legacy MySQL zero-date production rows at startup and to stop pushing obsolete recipe trigger-delay values into camera hardware when the trigger is now generated by machine I/O.

### Main functional integrations included

- normalizes legacy `tblproduzione.TimeFine = '0000-00-00 00:00:00'` rows to `NULL` during database initialization
- temporarily relaxes the MySQL session `sql_mode` only for that migration step and restores the previous mode afterwards
- stops writing recipe `TriggerDelay` values into DALSA camera hardware during application startup and recipe reload
- keeps recipe trigger-delay values available in the XML/UI contract, but logs them as intentionally skipped by the runtime
- aligns Cognex hardware restore/reinitialize flows to the same I/O-driven trigger policy so delayed background hardware rebinds do not reapply the delay unexpectedly

### Configuration-file notes

- no `Config.xml` schema change is required
- no recipe XML schema change is required
- recipe trigger-delay fields remain present for backward compatibility, but they are no longer applied to camera hardware during startup in this baseline

### Database notes

- `tblproduzione.TimeFine` must remain nullable
- startup migration now explicitly sanitizes legacy zero-date rows before enforcing the nullable datetime definition
- no new table or column is introduced by this release

### Operator / maintainer notes

- if startup previously failed with `Incorrect datetime value: '0000-00-00 00:00:00' for column 'TimeFine'`, this release recovers the old production rows automatically
- when checking logs, expect `RECIPE_TRIGGER_DELAY_SKIPPED` entries during startup/recipe reload on machines where trigger generation is handled by digital outputs

## Version 1.1.5.9

### Release metadata

- release date: `2026-04-28`

### Release intent

Incremental alignment release generated to bring the commissioned real-I/O plus virtual-conveyor flow into the shared OneDrive baseline without overwriting the colleague `1.1.5.8` commissioning UI stabilization work.

### Main functional integrations included

- added `Virtual conveyor in HW` mode to the fused `DigitalIOControl` page
- real `PCIE-1756` digital inputs and outputs remain active while the conveyor position advances from a configured manual speed
- product photocell transitions still create the product zero event, while intervention points in millimeters are evaluated against the virtual encoder count
- camera trigger, lighting, reject and alarm outputs continue to use the configured machine signal bindings and intervention positions
- preserved the colleague `BindingProxy` DataGrid header fix and ComboBox popup hardening already present in the shared `1.1.5.8` baseline

### Configuration-file notes

- `machine_runtime_config.xml` gains the virtual-conveyor runtime fields:
  - `VirtualConveyorEnabled`
  - `VirtualConveyorSpeedMetersPerMinute`
  - `VirtualConveyorUsePiecesPerMinute`
  - `VirtualConveyorPiecesPerMinute`
  - `VirtualConveyorProductPitchMm`
- no `Config.xml` schema change is required
- no recipe XML schema change is required in this release; recipe-level intervention distances continue to drive the synchronized actions

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- use this mode when the machine has real I/O wired but no physical encoder for the current commissioning phase
- set the manual conveyor speed to the measured belt speed before validating camera-trigger and lighting positions
- keep `HW` mode enabled in the I/O page: the virtual conveyor replaces only the encoder progression, not the digital board communication

## Version 1.1.5.5

### Release metadata

- release date: `2026-04-27`

### Release intent

Incremental commissioning-UI stabilization release generated to remove WPF binding errors from the merged `DigitalIOControl` baseline and keep the fused machine-configuration pages readable and localization-safe.

### Main functional integrations included

- fixed the commissioning `DataGrid` column-header bindings in `DigitalIOControl.xaml` so they use the page `BindingProxy` instead of `RelativeSource AncestorType=UserControl`
- removed the runtime binding-noise generated by `DataGridColumn.Header` objects that are outside the WPF visual tree
- kept the page aligned with the existing standalone-tool localization flow based on `ToolLocalizationService` and `Localization/messages_*.json`
- preserved the industrial visual language already used by the merged control:
  - structured headers
  - alternating rows
  - touch-friendly cell spacing

### Configuration-file notes

- no `Config.xml` changes are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release is intended to clean the commissioning and machine-configuration pages from avoidable WPF binding errors during runtime and diagnostic use
- when aligning the colleague shared baseline, include:
  - `Views/UserControls/DigitalIOControl.xaml`
  - `Models/BindingProxy.cs`
  - release and technical archive updates linked to this version

## Version 1.1.3.6

### Release metadata

- release date: `2026-04-22`

### Release intent

Incremental runtime-hardening release generated to reduce false camera-drift warnings on mixed-sensor machines and to stop rejecting valid GigE cameras only because their frame-grabber name is not DALSA-branded.

### Main functional integrations included

- runtime camera-role resolution now supports a serial-based fallback using the machine serials configured in `Config.xml`
- when a QuickBuild job name is generic, the panel can now infer the semantic role from the active frame-grabber serial before falling back to the legacy `CameraConfig.xml` ID mapping
- the camera overview now declassifies several false-positive warnings:
  - active runtime jobs whose serial already matches the configured machine role
  - configured leftover roles whose serial is already present in another runtime job
  - configured leftover roles that still share the same placeholder serial across multiple machine roles
- trigger configuration in `IgigaCameraAccess` no longer blocks generic GigE cameras with the legacy `DALSA only` string check
- non-DALSA GigE sensors are now logged as supported GigE devices and continue through the same trigger-feature attempt path when `OwnedGigEAccess` is available

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- the existing machine serial keys are now also used to stabilize runtime role resolution and drift diagnostics:
  - `TopCameraSerial`
  - `SideCameraSerial`
  - `FrontCameraSerial`
  - `RearCameraSerial`
  - `BottomCameraSerial`
- `CameraConfig.xml` remains supported as semantic fallback and operator-facing role contract; this release only changes how drift is interpreted when runtime serial evidence is already available

### Database notes

- no MySQL schema changes are required by this release
- no recipe XML schema changes are required by this release

### Operator / maintainer notes

- on mixed-sensor machines, keep the machine serials in `Config.xml` aligned with the physically installed cameras: the panel now uses them both for acquisition assignment and for camera-view/runtime diagnostics
- if the VPP still exposes a job-level VisionPro script that depends on a specific vendor API, that script may still fail independently of this HMI change; this release removes the extra DALSA-only rejection in the panel-side trigger helper, not inside custom QuickBuild scripts
- if recurring drift warnings remain after this release, capture together:
  - active runtime job name
  - job ID
  - frame-grabber serial
  - configured machine serials
  so the residual mismatch can be distinguished between HMI mapping and VPP-side script/tool assumptions

## Version 1.1.3.4

### Release metadata

- release date: `2026-04-22`

### Release intent

Incremental Top3D operator-view update generated to let each 3D recipe define its own primary and secondary display record paths while preserving the existing `Config.xml` fallback contract.

### Main functional integrations included

- each recipe can now optionally store two Top3D-specific display paths in `recipeParamTop3D`:
  - `Top3DPrimaryLastRunView`
  - `Top3DSecondaryLastRunView`
- when the active top job runs as `Top3D`, the panel now renders:
  - the primary Top3D image on the top pane
  - a second planar mirror view on the side pane labeled `Top2D`
- when the active runtime exposes only one `Top3D` job, the overview now injects a virtual second operator pane so the planar `Top2D` mirror can still be shown without inventing a new machine camera role
- the runtime still falls back to `Config.xml -> LasRunParam` whenever the recipe fields are empty, preserving compatibility with existing recipes and machine baselines
- the Top3D recipe editor now exposes the two optional display-path fields directly in the Top3D section
- the Top3D recipe editor layout was widened so nominal values, tolerances and 3D product properties stay readable on the panel
- the reused top/side operator panes now hide their standard feature ribbons when repurposed for Top3D/Top2D display-only usage to avoid misleading status semantics
- Top3D and Top2D operator panes now show direct `GOOD / NO GOOD` measurement summaries using the measured values already persisted in the production record

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- the existing `LasRunParam.LastRunView1/2/3` contract remains valid and acts as fallback when recipe overrides are not populated
- recipe XML schema is extended in a backward-compatible way with two optional fields under `recipeParamTop3D`:
  - `Top3DPrimaryLastRunView`
  - `Top3DSecondaryLastRunView`
- runtime language packs under `C:\QtisVision\Language` must include the added Top3D/Top2D labels and measurement-summary keys; the canonical update path remains `scripts\UpdateRuntimeLanguageFiles.ps1`

### Database notes

- no MySQL schema changes are required by this release
- the serialized full-recipe payload stored in the existing recipe parameter fields can now include the new optional Top3D view-path properties

### Operator / maintainer notes

- for 3D recipes, leave the new Top3D view-path fields empty when the machine should continue using the standard `Config.xml` display paths
- populate the fields only when a specific recipe or machine variant requires different VisionPro record strings, for example:
  - `Measure.CogIPOneImageTool1.OutputImage`
  - `Measure.CogPixelMapTool1.OutputImage`
- the second operator pane is intentionally reused as `Top2D` only during Top3D runtime; no machine camera-role remapping is introduced by this release

## Version 1.1.3.3

### Release metadata

- release date: `2026-04-21`

### Release intent

Incremental Top3D artifact-capture update generated to make the `Top3D` profilometer flow save operator-usable 2D and 3D evidence plus point-cloud data from the existing piece-save pipeline.

### Main functional integrations included

- the `SaveImage` pipeline now detects when the active top job is semantically `Top3D`
- when the active `Top3D` results toolblock exposes the configured outputs, the piece folder now also stores:
  - a rendered 2D image
  - a rendered 3D/range image
  - a point-cloud CSV
- the export reuses the existing `Piece_xxxxxxxx` save contract instead of creating a separate storage hierarchy
- the implementation is config-driven and does not force a different runtime layout than the one already defined by `Config.xml`
- a dedicated L38-300 integration note and updated Top3D machine guide now document how VisionPro should expose the outputs expected by the panel

### Configuration-file notes

- new optional `Config.xml` keys under `Configuration`:
  - `Top3DRerenderResultOutput`
  - `Top3DPointCloudOutput`
  - `SaveTop3DRendered2DImage`
  - `SaveTop3DRangeImage`
  - `SaveTop3DPointCloudCsv`
- default values used by the runtime when keys are missing:
  - `Top3DRerenderResultOutput = Top3DRerenderResult`
  - `Top3DPointCloudOutput = Top3DPointCloud`
  - `SaveTop3DRendered2DImage = true`
  - `SaveTop3DRangeImage = true`
  - `SaveTop3DPointCloudCsv = true`
- no recipe XML schema change is required by this release

### Database notes

- no MySQL schema changes are required by this release
- the existing production-record persistence for `ThreeDHeight`, `ThreeDWidth` and `ThreeDLength` remains unchanged
- the new Top3D evidence files are attached to the existing piece folder already referenced by `tblgenerale.PieceData`

### Operator / maintainer notes

- for the panel to save Top3D sidecar files, the VisionPro `Top3D` results toolblock should expose:
  - a `Cog3DVisionDataRerenderResult` or `Cog3DVisionDataStitchResult` output for `Top3DRerenderResult`
  - an enumerable X/Y/Z point collection for `Top3DPointCloud`
- if the configured outputs are missing, the runtime keeps the normal piece save flow active and writes a warning log instead of interrupting production
- saved Top3D sidecar file names follow this pattern inside the current piece folder:
  - `CH1_<piece>_T3D_2D_A.bmp`
  - `CH1_<piece>_T3D_3D_Range_A.bmp`
  - `CH1_<piece>_T3D_PointCloud.csv`

## Version 1.1.3.2

### Release metadata

- release date: `2026-04-17`

### Release intent

Incremental compatibility update generated to align project assembly references with the currently installed Cognex VisionPro baseline.

### Main functional integrations included

- official Cognex support documentation was checked against the current VisionPro documentation portal
- the project references for `Cognex.VisionPro*` assemblies were aligned from the stale `79.0.0.0` metadata to the locally installed assembly version `93.0.0.0`
- the update preserves the existing config-driven runtime and the same Cognex installation paths already used by the project

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- Cognex official documentation currently shows `VisionPro 9.25 SR1` as the latest VisionPro documentation baseline, with release date `2025-09-12`
- on this development machine, the installed `Cognex.VisionPro*.dll` assemblies resolve to version `93.0.0.0` and the `.NET` support assemblies in `bin` resolve to `9.25.0.0`
- the project file is now coherent with that installed Cognex baseline instead of carrying older reference metadata
- if another machine still has an older VisionPro installation, its local Cognex runtime should be updated before building this project there

## Version 1.1.3.1

### Release metadata

- release date: `2026-04-15`

### Release intent

Incremental audit-payload refinement generated to add structured runtime-hold metadata to automatic `Job Tool Editor` hold release events.

### Main functional integrations included

- `JOBEDITOR_HOLD_AUTO_RELEASED_ON_NAVIGATION` now includes structured metadata such as:
  - `target_view`
  - `resume_requested`
  - `defensive_release`
  - `had_editor_hold_before_exit`
  - `hold_cleared_by_navigation`
  - `active_holds_before`
  - `active_holds_after`
- `JOBEDITOR_HOLD_AUTO_RELEASED_ON_START` now includes structured metadata such as:
  - `current_view`
  - `active_holds_before`
  - `active_holds_after`
  - `released_reason`

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- the event payload JSON is now directly filterable for stale-hold troubleshooting
- maintainers no longer need to parse the free-text message alone to understand whether the editor hold was released by navigation logic or by a later start request

## Version 1.1.3.0

### Release metadata

- release date: `2026-04-15`

### Release intent

Incremental auditability hotfix generated to trace automatic release of stale `Job Tool Editor` runtime holds.

### Main functional integrations included

- the runtime now writes an explicit audit event when navigation out of `Job Tool Editor` clears the protected editor hold
- the runtime also writes an explicit audit event when a manual `Start` clears a stale editor hold outside the editor page
- these logs make it possible to distinguish hold-release recovery from ordinary start/stop activity directly in the audit trail

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- look for `JOBEDITOR_HOLD_AUTO_RELEASED_ON_NAVIGATION` when leaving `Job Tool Editor`
- look for `JOBEDITOR_HOLD_AUTO_RELEASED_ON_START` when `Start` clears a stale editor hold outside the editor
- these events help confirm that a blocked restart was caused by a stale editor hold and that the runtime recovered it automatically

## Version 1.1.2.9

### Release metadata

- release date: `2026-04-15`

### Release intent

Incremental runtime hotfix generated to prevent `Job Tool Editor` holds from surviving page exit and blocking production restart.

### Main functional integrations included

- leaving `Job Tool Editor` now releases the `job-editor` protected hold more defensively
- the editor exit path now restores continuous run through the central `ManageJobStateAsync(true)` flow
- manual `Start` outside the editor now also clears any stale `job-editor` hold before attempting restart

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- after leaving `Job Tool Editor`, the machine should no longer remain blocked by `job-editor`
- if the operator presses `Start` on another page, a stale editor hold should no longer prevent the return to `RunContinuous`

## Version 1.1.2.8

### Release metadata

- release date: `2026-04-15`

### Release intent

Incremental UI/runtime observability release generated to expose active protected runtime holds directly in the operator top bar.

### Main functional integrations included

- the top status area now shows an explicit runtime-hold indicator when continuous run is intentionally blocked
- the indicator summarizes active hold reasons such as manual stop, recipe save, job editor or shutdown
- the tooltip exposes the translated hold reasons so the operator can understand why `Start` may be blocked without opening the event log

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- if the machine is stopped intentionally, the top bar now shows the protected-stop reason directly
- when `Start` does not resume the machine, the operator can first check whether a runtime hold is still active before opening diagnostics or logs

## Version 1.1.2.7

### Release metadata

- release date: `2026-04-15`

### Release intent

Incremental hotfix generated to release `Job Tool Editor` runtime holds deterministically during navigation.

### Main functional integrations included

- leaving `JobToolEditor` now executes explicit exit logic before the view is replaced
- the navigation pipeline stops any active live preview and releases the `job-editor` runtime hold before opening another page
- this prevents unrelated views such as `RecipeManager` from inheriting the protected stopped state created for VisionPro tool editing

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- after leaving `Job Tool Editor`, the machine should no longer remain blocked by `job-editor` hold
- manual `Start` should no longer be rejected with `blocked by active hold(s): job-editor` after navigating away from the editor

## Version 1.1.2.6

### Release metadata

- release date: `2026-04-15`

### Release intent

Incremental hotfix generated to harden continuous-run recovery after recipe save in production.

### Main functional integrations included

- the manual `Start` command now releases stale `recipe-save` holds before attempting a new continuous-run start
- `StartVision` is no longer treated as fully completed when the runtime remains stopped after the start request
- automatic restart after `SaveRecipeAsync` now uses the central `ManageJobStateAsync(true)` path instead of a raw low-level start call, so UI state, watchdog and runtime orchestration stay aligned
- recipe-save restart failures now leave explicit warning traces in the log instead of silently reporting success

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- after saving a production recipe, the system should return to continuous run more reliably
- if VisionPro still does not resume, the log now reports the condition more explicitly instead of showing only `StartVision - Completed`

## Version 1.1.2.5

### Release metadata

- release date: `2026-04-15`

### Release intent

Incremental hotfix generated to stabilize operator progress bindings and correct runtime VisionPro status reporting.

### Main functional integrations included

- fixed the `Job Tool Editor` progress overlay so the progress bar binds in `OneWay` mode and no longer throws a WPF `InvalidOperationException` against the read-only loading properties
- corrected event/log payload resolution of `visionpro_status` so it reflects the current runtime state first, instead of reusing a stale cached `Stopped` value after VisionPro has already resumed

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- the job editor should no longer trigger the `TwoWay / read-only LoadingProgressValue` exception while opening or saving tools
- operational and audit logs should now show `visionpro_status=Running` when the machine runtime is actually back in continuous execution
## Version 1.1.2.4

### Release metadata

- release date: `2026-04-14`

### Release intent

Incremental release generated to add operator-facing progress feedback for tool loading and recipe operations.

### Main functional integrations included

- `Job Tool Editor` now shows a richer industrial loading overlay while a selected tool is being prepared and opened
- long operations now expose stage-based feedback with progress popup windows for:
  - loading a production recipe
  - saving a recipe
  - saving tool changes from the VisionPro editor
- recipe-change runtime reinitialization now reports internal phases such as:
  - stop runtime
  - reload configuration
  - reload inspection configuration
  - initialize vision system
  - restore continuous run
- the operator now receives explicit visual confirmation that the system is working instead of seeing blank waiting states

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- the new feedback layer is especially important on industrial panels where recipe and editor actions can take several seconds
- blank or static screens during tool load/save should now be replaced by explicit progress feedback
- the new visible messages are integrated into `ServerMessage` and runtime language files

## Version 1.1.2.3

### Release metadata

- release date: `2026-04-14`

### Release intent

Incremental hotfix generated to correct the lazy bootstrap of `CameraDisplayManager`.

### Main functional integrations included

- fixed a regression in `MainWindow.EnsureCameraDisplayManagerInitialized()`
- the helper now constructs `CameraDisplayManager` correctly instead of recursively calling itself
- startup/runtime display delegation keeps the intended lazy behavior without causing `System.StackOverflowException`

### Configuration-file notes

- no `Config.xml` changes
- no recipe XML changes

### Database notes

- no MySQL changes

### Operator / maintainer notes

- this release specifically fixes an application-start crash caused by infinite recursion in the display-manager bootstrap path

## Version 1.1.2.2

### Release metadata

- release date: `2026-04-14`

### Release intent

Incremental release generated to harden MainWindow startup after the CameraDisplayManager extraction.

### Main functional integrations included

- `MainWindow` now initializes `CameraDisplayManager` lazily instead of assuming it already exists during early runtime bootstrap
- startup paths such as `InitializeRecipeAsync` and `InitializeVisionSystem` can now refresh runtime-supported inspection features without depending on `Window_Loaded` having already constructed the display helper
- all thin delegator methods extracted in Fase 3 Step 1 now route through the same lazy bootstrap guard, so the display service remains available consistently during startup, recipe reload and vision reinitialization

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- no recipe XML changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release fixes a startup crash (`NullReferenceException`) that could occur when the runtime feature map was rebuilt before `Window_Loaded` completed
- the change does not alter camera-role logic or recipe semantics; it only makes the bootstrap sequence robust against call-order differences

## Version 1.1.2.1

### Release metadata

- release date: `2026-04-13`

### Release intent

Incremental release generated to separate recipe-level inspection enablement from recipe-level reject behaviour.

### Main functional integrations included

- recipe XML now supports a dedicated `inspectionStatus` section
- `inspectionStatus` defines which inspections are active and therefore validated, shown in counters and shown in operator panels
- `ejectionStatus` is now reserved for deciding which detected defects must command product reject
- the recipe page now exposes two distinct groups:
  - enabled inspections
  - inspections that trigger reject
- disabling an inspection automatically disables the related reject flag
- enabling reject on a defect automatically ensures the related inspection is active
- runtime no longer assumes that every enabled inspection must also generate reject
- production snapshots now distinguish:
  - `EsitoClassificazione` based on detected defects
  - `Espulsione_Comandata` based on reject-enabled defects only

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- recipe XML now persists a new `inspectionStatus` node
- backward compatibility is preserved: if an older recipe has no `inspectionStatus`, the runtime derives it from `ejectionStatus`

### Database notes

- no new MySQL columns are required by this release
- existing `tblgenerale.Espulsione_Comandata` is now driven explicitly by reject-enabled defects instead of implicitly by every failed inspection

### Operator / maintainer notes

- a defect can now be measured and counted without necessarily ejecting the product
- if a product shows a defect on an inspection that is active but not reject-enabled, counters and status will still show the defect but the reject command will remain off
- this is the correct contract for machines where some inspections are informational/process-monitoring checks and not reject criteria

## Version 1.1.2.0

### Release metadata

- release date: `2026-04-13`

### Release intent

Incremental release generated to remove the mismatch between the machine-level inspection profile and the active recipe/runtime feature set shown to the operator.

### Main functional integrations included

- the runtime feature map no longer hides recipe-enabled inspections just because a toolblock-output hint is missing
- the active recipe `ejectionStatus` is now the primary source for operator-facing counters and feature visibility once a recipe is loaded
- the `Inspection Configuration` page now aligns its enabled/disabled state with the active recipe/runtime when a production recipe is loaded
- saving or resetting inspections from `Inspection Configuration` now keeps the active recipe XML and the machine profile database aligned
- the inspection configuration page now filters feature cards by the camera roles that are really active on the current runtime (`Top`, `Side`, `Front`, `Top3D`)

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- the active recipe XML remains the canonical runtime source for enabled inspections through `ejectionStatus`
- if the operator expects more counters than the runtime shows, the first thing to verify is now the active recipe `ejectionStatus`, not only the `cfg_inspection` table

### Database notes

- no new MySQL columns are required by this release
- `cfg_inspection` remains the machine-profile store, but the active recipe is now kept aligned when inspections are saved/reset from the dedicated configuration page

### Operator / maintainer notes

- the counters page now matches the active recipe/runtime more closely
- the `Inspection Configuration` page no longer risks showing a broad machine default set while the active recipe enables only a subset
- on the current `Napco_sample` recipe, only `Logo` was enabled in `ejectionStatus`; this is why the counters previously showed only the `Logo` defect card
- if you want all enabled top/side inspections visible in counters, they must be enabled in the active recipe as well

## Version 1.1.1.9

### Release metadata

- release date: `2026-04-13`

### Release intent

Incremental release generated to separate standard `Top` inspections from `Top3D` profilometer inspections and to make operator-facing visibility follow the active recipe + runtime jobs + toolblock outputs.

### Main functional integrations included

- `Top` and `Top3D` are now treated as two separate runtime inspection contracts
- when the VPP job is named `Top`, the standard top validation path is used
- when the VPP job is named `Top3D`, only the dedicated 3D validation path is used
- runtime feature visibility is now derived from:
  - the current recipe `ejectionStatus`
  - the active semantic job roles loaded from the VPP
  - the outputs really exposed by the active toolblocks
- counters now stay aligned with the effective runtime feature map instead of showing stale or irrelevant inspections
- camera feature panels now hide inspections that are not active for the current recipe/runtime
- the recipe editor inspection checklist now shows only the inspection families relevant to the active camera roles (`Top`, `Side`, `Front`, `Top3D`)

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- the effective inspection set is now driven primarily by the recipe XML `ejectionStatus` section
- job semantic names in the active `.vpp` remain the priority source for deciding whether the top path is `Top` or `Top3D`

### Database notes

- no new MySQL columns are required by this release
- `tblgenerale` and `tblproduzione.RecipeParamerterTop3D` continue to be the persistence targets for the `Top3D` flow already introduced in `1.1.1.8`

### Operator / maintainer notes

- if a machine runs a normal top camera job, the top 2D inspections are evaluated again through the standard path
- if a machine runs a `Top3D` profilometer job, only the 3D controls remain active in runtime visibility/counters
- this release reduces operator confusion because the software no longer mixes `Top` and `Top3D` checks inside the same visible runtime set

## Version 1.1.1.8

### Release metadata

- release date: `2026-04-13`

### Release intent

Incremental release generated to turn the profilometer configuration into a dedicated `Top3D` runtime/recipe contract instead of reusing the historical `Front` recipe section.

### Main functional integrations included

- the runtime now recognizes `Top3D` as a semantic camera/job role
- `Top3D` reuses the standard `TopCameraView` display surface instead of the front traceability panel
- the recipe editor now stores 3D thresholds in a dedicated `recipeParamTop3D` section
- the validator now runs 3D checks through a dedicated `Top3D` validation path instead of mixing them into front traceability validation
- single-sensor `3DCheck` machines can now process top-only inspections without waiting for a secondary `Side` or `Front` queue
- the `Top3D` result lifecycle now follows the same runtime pattern as the other inspections:
  - measured values and NC flags written into the production snapshot
  - persistence into `tblgenerale`
  - counter/alarm integration through the standard defect pipeline
- the recipe archive now stores a dedicated `RecipeParamerterTop3D` JSON snapshot
- older recipes that still hold 3D thresholds under `recipeParamFront` are migrated in memory for backward compatibility

### Configuration-file notes

- `Configuration.MachineType = 3DCheck` remains the switch for the profilometer machine variant
- `ThreeDHeightOutput`, `ThreeDWidthOutput` and `ThreeDLengthOutput` continue to define the VisionPro output names used by the 3D validator
- the active QuickBuild job should expose a semantic `Top3D`-style name so the runtime resolves the role correctly

### Database notes

- `tblproduzione` now includes `RecipeParamerterTop3D`
- the existing `tblgenerale` 3D measurement columns continue to be used for runtime persistence of height, width and length results
- 3D defect counters continue to use:
  - `THREED_HEIGHT`
  - `THREED_WIDTH`
  - `THREED_LENGTH`

### Operator / maintainer notes

- on `3DCheck` machines the recipe page shows a dedicated `Top3D` section and keeps the front traceability section hidden
- the profilometer image/result display stays on the top panel, so operators do not need to look for a separate front camera page
- for commissioning details use `Documentation/MachineHardware/top3d-machine-configuration-guide-2026-04-13.md`

## Version 1.1.1.7

### Release metadata

- release date: `2026-04-08`

### Release intent

Incremental release generated to make the counters page show only the inspections that are currently enabled, keeping the operator view cleaner and aligned with the active configuration.

### Main functional integrations included

- the defects counters list is now filtered against the enabled features held by `InspectionConfigService`
- the counters page refreshes automatically when the inspection configuration changes
- the same refresh path is triggered when the runtime reloads the inspection configuration during recipe/system reinitialization
- hidden inspections no longer contribute to the operator-facing total-defects badge shown in the counters page
- the filtering logic now uses canonical defect keys internally, so it stays stable across localized UI labels

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- production counters (`Total`, `Good`, `No Good`) remain always visible
- only the inspection-specific defect counters are filtered
- if an inspection is disabled from `Inspection Configuration`, its counter disappears from the panel as soon as the configuration cache refreshes
- when a recipe/runtime reload refreshes the inspection configuration, the counters page follows that new enabled set automatically

## Version 1.1.1.6

### Release metadata

- release date: `2026-04-08`

### Release intent

Incremental release generated to make continuous-run supervision robust around intentional stops, recipe saves, job editing and idle auto-resume.

### Main functional integrations included

- the runtime now tracks explicit continuous-run hold reasons such as `manual-stop`, `recipe-save` and `job-editor`
- automatic VisionPro resume after panel inactivity is now allowed only when no intentional hold is active
- the Start button clears only the manual stop hold, so explicit operator restarts work without accidentally bypassing maintenance/editing holds
- recipe save now places a temporary runtime hold, stops VisionPro safely, saves the XML, then re-arms continuous mode automatically when no other hold is still active
- the Job Tool Editor now keeps VisionPro intentionally stopped while the operator is editing tools and re-enables continuous execution after a successful save or when the editor is left cleanly
- if the operator modifies a tool again after a save, the editor automatically re-enters the protected stopped state

### Configuration-file notes

- no new configuration keys are required by this release
- the existing idle auto-resume settings remain valid:
  - `AutoResumeVisionWhenIdle`
  - `AutoResumeVisionIdleSeconds`
  - `AutoResumeVisionRetryCooldownSeconds`

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- the machine is now expected to stay in `RunContinuous` in all normal production states
- intentional stops are now treated differently from unexpected stops, which prevents idle auto-resume from interfering with maintenance or editing actions
- when leaving the Job Tool Editor, the machine can return to continuous mode automatically if no other hold is still active
- after recipe save, the machine is re-armed automatically unless the operator had explicitly kept it stopped for another reason

## Version 1.1.1.5

### Release metadata

- release date: `2026-04-08`

### Release intent

Incremental release generated to harden machine-status coherence when VisionPro still has stale queued results after a stop or transition.

### Main functional integrations included

- pending VisionPro results are now accepted only when continuous run is really active and not transitioning
- Top, Side and Front runtime queues are now cleared centrally when the machine is stopped, when VisionPro reports `JobStopped`, when auto-recovery starts, and when recipe/runtime reload resets the vision state
- stale queued results can no longer keep updating camera displays, counters or downstream inspection processing while the HMI status is already `Stopped`
- the overview now resumes from a clean queue state when the operator presses `Start` again after a stop or transition

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- if the machine status is `Stopped`, new UI refreshes caused only by buffered VisionPro results should no longer appear
- if a stop happens while VisionPro still had data in queue, that buffered data is intentionally discarded
- this release is specifically meant to eliminate the inconsistent field behavior where the HMI showed `Stopped` while inspections still seemed to elaborate in the background

## Version 1.1.1.4

### Release metadata

- release date: `2026-04-07`

### Release intent

Incremental release generated to make the Job Tool Editor adapt automatically to the real VisionPro jobs loaded by the active QuickBuild.

### Main functional integrations included

- the Job Tool Editor no longer exposes only the historical `Top` and `Side` tabs
- the editor now builds its tab list directly from the active QuickBuild job manager, so `Front` and any other loaded editable jobs become visible automatically
- the active tab header now mirrors the real QuickBuild job name, reducing ambiguity between HMI and VisionPro
- the tool list, tool loading, live preview and save flow now follow the selected runtime job instead of a fixed tab index assumption
- the release-bump script now updates the `Current Official Version` block even when the archive contains the extra `baseline type` line

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- if the active `.vpp` contains `Top + Front`, the Job Tool Editor now shows both jobs directly
- a stale `CameraConfig.xml` role assignment no longer hides a valid editable job from the tool editor
- the editor keeps using the current runtime logic and saved QuickBuild file; only the job discovery/presentation path has been generalized

## Version 1.1.1.1

### Release metadata

- release date: `2026-04-07`

### Release intent

Incremental release generated to improve the camera overview layout on compact industrial HMIs with more than two active cameras.

### Main functional integrations included

- the camera overview now uses a vertical outer scroll so all camera panels and statistics remain reachable on 15" screens
- the camera container now switches automatically between one, two or three columns depending on the real viewport width and the number of configured cameras
- the camera section now applies a more compact padding profile on smaller viewports to reduce wasted space while preserving the existing runtime camera logic

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- with three or more cameras the overview remains scrollable instead of clipping lower panels
- on narrower screens the view can collapse to a single-column layout automatically
- no changes are required to `CameraConfig.xml`; the layout adapts only at runtime based on the screen size and the currently available camera views

## Version 1.1.1.0

### Release metadata

- release date: `2026-04-01`

### Release intent

Incremental release generated to formalize decimal rollover in the software versioning policy.

### Main functional integrations included

- the canonical release-bump script now applies decimal carry logic on version segments
- when the revision reaches `10`, the build segment is incremented and the revision returns to `0`
- the same rollover logic also applies from build to minor and from minor to major if a segment reaches `10`

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- starting from this release, versions no longer continue as `...10` on the rightmost segment
- example: `1.1.0.9 -> 1.1.1.0`

## Version 1.1.0.9

### Release metadata

- release date: `2026-04-01`

### Release intent

Incremental release generated to fix the WPF/Cognex naming collision introduced by the shutdown hardening patch.

### Main functional integrations included

- the application now calls the Cognex runtime shutdown through an explicit alias instead of the ambiguous `Startup` identifier
- this removes the compiler error caused by the conflict between `Cognex.Vision.Startup` and the WPF `Application.Startup` event inside `App.xaml.cs`

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this is a compile-fix release only
- the intended shutdown behavior introduced in `1.1.0.8` remains unchanged

## Version 1.1.0.8

### Release metadata

- release date: `2026-04-01`

### Release intent

Incremental release generated to harden full application shutdown so the HMI process does not remain alive after the window closes.

### Main functional integrations included

- the shutdown pipeline now schedules an explicit forced-process fallback if native acquisition/runtime resources do not release within the grace timeout
- the shutdown pipeline now closes secondary windows, disposes the asynchronous image-writer worker and resets singleton services explicitly
- the application now releases the global Cognex runtime with `Cognex.Vision.Startup.Shutdown()` during exit
- the startup flow now registers the real production shell as `Application.MainWindow`, which aligns WPF shutdown semantics with the actual runtime shell
- the main shell view model now disposes the current active view and its disposable view model during shutdown to reduce leaked timers or hardware managers from auxiliary screens

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release is intended to remove the condition where `QtisVisionPanel.exe` stays visible in Task Manager after closing the application
- if the graceful shutdown still stalls because of native acquisition modules, the fail-safe timeout now terminates the process so the machine PC is not left with a ghost HMI instance

## Version 1.1.0.7

### Release metadata

- release date: `2026-04-01`

### Release intent

Incremental release generated to stabilize the recipe-change lifecycle by using a single clean teardown/rebuild of the VisionPro runtime.

### Main functional integrations included

- recipe change now performs a controlled teardown of the active VisionPro runtime before loading the new recipe
- the teardown stops watchdog/recovery, stops continuous run, deregisters Cognex events, disposes the old job manager, clears queues/records and resets display state
- recipe change no longer performs a double Cognex initialization in the same flow
- the new recipe runtime is rebuilt once, then `RunContinuous` is started again on the clean manager instance

### Configuration-file notes

- no new configuration keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release is intended to remove stale camera/job state after recipe change, which could previously surface as delayed updates, `StoppedSingle`, or side-job instability after the reload
- if a tool block still fails after this release, the failure should now be interpreted as a real recipe/tool condition rather than a double-initialization side effect of the HMI runtime

## Version 1.1.0.6

### Release metadata

- release date: `2026-04-01`

### Release intent

Incremental release generated to stabilize DALSA trigger-delay application after the first hardening pass of version `1.1.0.5`.

### Main functional integrations included

- trigger-delay application no longer forces `job.Stop()` before writing the DALSA camera feature
- trigger-delay write now waits for the acquisition FIFO and GigE access to become ready before applying the new value
- `TriggerDelay` write now retries automatically a few short times before the runtime reports the node as not writable
- trigger-configuration readback now downgrades missing framegrabber/camera conditions from hard error to warning, because those cases can happen transiently while the job is still settling after reload

### Configuration-file notes

- no new `Config.xml` or recipe XML keys are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release is the follow-up stabilization of `1.1.0.5`
- if a camera still reports `TriggerDelay` as not writable after this release, the next diagnostic step is to capture the new `RECIPE_TRIGGER_DELAY_APPLIED` line together with the surrounding DALSA logs to identify whether the node becomes writable only after a specific Cognex state transition

## Version 1.1.0.5

### Release metadata

- release date: `2026-04-01`

### Release intent

Incremental release generated to harden immediate trigger-delay application for direct-to-camera DALSA hardware trigger lines.

### Main functional integrations included

- the runtime now waits for the recipe XML to finish loading before reading `TopCameraTriggerDelay` and `SideCameraTriggerDelay`
- the active recipe trigger-delay defaults are now consistent across startup and recipe reload paths
- trigger-delay application now performs a coordinated `stop / queue flush / apply / readback verify` sequence on the DALSA jobs
- queue flush is executed both on the individual Cognex job and on the job-manager user/failure queues to reduce stale acquisitions after a delay change
- trigger configuration readback no longer writes back into the recipe/config file as a side effect
- saving a recipe no longer tries to re-read or reapply trigger delay before the XML file has been persisted
- when the active production recipe is reloaded, the runtime now logs a `RECIPE_TRIGGER_DELAY_APPLIED` verification line with requested and readback values for top and side

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- the source of truth for camera trigger delay remains `cameraSetting.TopCameraTriggerDelay` and `cameraSetting.SideCameraTriggerDelay` inside the active recipe XML

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- after changing trigger delay on the active recipe, the new value must become effective on the next production cycle after the controlled stop/start sequence, rather than only after the third or fourth product
- the runtime no longer persists camera readback into the recipe file implicitly; recipe XML remains the source of truth and hardware readback is now only used for verification/logging

## Version 1.1.0.4

### Release metadata

- release date: `2026-03-31`

### Release intent

Incremental release generated to extend PC diagnostics with database-archive retention control and thermal monitoring.

### Main functional integrations included

- PC diagnostics now monitor the configured MySQL archive table size using `information_schema`
- when the archive exceeds the configured start size, the runtime can delete old rows in batches until the configured target size is reached
- archive cleanup respects a retention window that defaults to 90 days, so the table keeps about 3 months of data for statistics before cleanup becomes eligible
- PC diagnostics now attempt to read disk temperatures from Windows storage reliability counters
- PC diagnostics now attempt to read memory/system thermal-zone temperature through ACPI WMI
- warning and critical thermal conditions now generate operational events the same way as CPU, RAM and disk-space alerts
- the diagnostics view now exposes archive size, hottest disk temperature, memory temperature and archive cleanup policy

### Configuration-file notes

Inside `AppConfig.SystemDiagnostics`, verify these keys when deploying or migrating a runtime:

- `DatabaseArchiveMonitoringEnabled`
- `DatabaseArchiveAutoCleanupEnabled`
- `DatabaseArchiveTableName`
- `DatabaseArchiveTimestampColumn`
- `DatabaseArchiveCleanupStartMb`
- `DatabaseArchiveCleanupTargetMb`
- `DatabaseArchiveRetentionDays`
- `DatabaseArchiveDeleteBatchSize`
- `DatabaseArchiveCleanupCooldownMinutes`
- `TemperatureMonitoringEnabled`
- `DiskTemperatureWarningC`
- `DiskTemperatureCriticalC`
- `MemoryTemperatureWarningC`
- `MemoryTemperatureCriticalC`

Default policy introduced by this release:

- database archive retention: `90` days
- archive cleanup is eligible only for rows older than retention
- archive cleanup starts when the configured table reaches the configured `StartMb`
- cleanup stops when the table goes below the configured `TargetMb`

### Database notes

- no new MySQL tables or columns are required by this release
- the monitored archive table defaults to `tblgenerale`
- the monitored timestamp column defaults to `DataeOra`
- cleanup uses batched `DELETE` statements ordered by the configured timestamp column

### Operator / maintainer notes

- disk temperature and memory temperature are best-effort Windows sensor readings and may be unavailable on some industrial PCs even when the rest of the diagnostics remain valid
- if the archive table exceeds its target but there are no rows older than the configured retention, the runtime logs a cleanup-skipped result instead of deleting recent production history
- after deploying this version, verify the new `SystemDiagnostics` keys once and then save the configuration so runtime XML and MySQL snapshot stay aligned

## Version 1.1.0.3

### Release metadata

- release date: `2026-03-31`

### Release intent

Incremental release generated to harden XML startup recovery after application updates and runtime file rewrites.

### Main functional integrations included

- fixed `Config.xml` serialization so the file is now written with a coherent UTF-8 declaration and UTF-8 bytes
- fixed recipe XML serialization with the same canonical UTF-8 output
- load path now retries by re-reading XML as text and normalizing legacy `utf-16` declarations before falling back to MySQL recovery
- recovery restore now rewrites canonical XML back to disk so invalid legacy payloads are self-healed

### Configuration-file notes

- no new `Config.xml` keys introduced by this release
- existing runtime config files may be automatically rewritten once to normalize the XML encoding after startup

### Database notes

- no new MySQL tables or columns required by this release
- `tblconfigsnapshot` continues to be used for config and recipe recovery, but restored payloads are now rewritten to canonical UTF-8 XML

### Operator / maintainer notes

- after deploying this version, startup warnings about `Errore nel documento XML (0, 0)` should disappear for valid legacy files affected only by encoding mismatch
- the first startup or first save after the update may rewrite `Config.xml` and the active recipe XML to normalize their XML declaration

## Version 1.1.0.2

### Release metadata

- release date: `2026-03-30`

### Release intent

Incremental release generated to enrich the operator-facing About popup with release traceability data.

### Main functional integrations included

- the About popup now shows the official release date of the current software version
- the About popup now shows the last software-update timestamp resolved from the deployed executable
- release archive and release-bump script updated so future versions keep the release-date field in a stable format

### Configuration-file notes

- no new `Config.xml` sections required by this release
- runtime behavior remains fully driven by the existing machine configuration

### Database notes

- no new MySQL tables or columns required by this release
- database contract remains compatible with version `1.1.0.1`

### Operator / maintainer notes

- operators can validate both software version and release date directly from the navigation-logo popup
- maintainers can compare the release date shown in the popup with the canonical version archive when deploying updates

## Version 1.1.0.1

### Release metadata

- release date: `2026-03-30`

### Release intent

Incremental release generated after the first post-archive software updates on the unified baseline.

### Main functional integrations included

- raw-image export normalized for mono simulation use:
  - monochrome images are now saved as grayscale-ready raw evidence
  - RGB images are preserved as they are
- software version popup stabilized after the VisionPro type-reference correction
- formal incremental release workflow introduced through the release bump script

### Configuration-file notes

- no new `Config.xml` sections required by this release
- the runtime contract remains fully driven by the existing machine `Config.xml`

### Database notes

- no new MySQL tables or columns required by this release
- database contract remains compatible with version `1.1.0.0`

### Operator / maintainer notes

- every software update must now generate a new incremental release number
- the canonical script is:
  - `scripts\IncrementSoftwareRelease.ps1`
- default behavior:
  - increment the last version segment (`Revision`)
- example:
  - `1.1.0.1` -> `1.1.0.2`
- the script updates:
  - `Properties\AssemblyInfo.cs`
  - `Documentation\MachineHardware\software-version-archive.md`

## Version 1.1.0.0

### Release metadata

- release date: `2026-03-27`

### Release intent

This is the first explicitly archived version of the unified main project after the baseline alignment between:

- `D:\Pulsar\Developer\NewPanel\QtisVisionPanel`
- `D:\Pulsar\Developer\QtisVisionPanel`

The target was to keep the current runtime logic of the main project while integrating the strongest mature work from the companion baseline.

### Main functional integrations included

- native `DataInspector` embedded inside the HMI
- MySQL-backed configuration snapshot and recovery
- runtime-safe `Config.xml` normalization and restore flow
- structured application configuration editor in `Preferences`
- system diagnostics for CPU, RAM, mounted disks and automatic cleanup
- top-bar diagnostics badge and filtered alarm counter
- improved recipe save / duplicate / runtime refresh flow
- production image save hardening from Cognex records
- event monitor with controlled clear actions
- user-management hardening and role visibility rules
- operator inactivity handling:
  - automatic VisionPro resume when panel is idle
  - automatic logout after inactivity
- VisionPro continuous-run recovery hardening to reduce UI freeze scenarios
- software version popup opened from the company logo in the navigation panel

### Configuration-file notes

The application keeps following the current `Config.xml` contract of the main project.

No forced runtime-folder standardization is introduced by this version.

#### Configuration keys to verify

Inside `AppConfig.Configuration`:

- `CurrentUserRole`
- `externalApp_automation`
- `externalApp_tools`
- `externalApp_reports`
- `BlockingAlarmOutput`
- `NonBlockingAlarmOutput`
- `TopCameraSerial`
- `SideCameraSerial`
- `FrontCameraSerial`
- `RearCameraSerial`
- `BottomCameraSerial`
- `AutoResumeVisionWhenIdle`
- `AutoResumeVisionIdleSeconds`
- `AutoResumeVisionRetryCooldownSeconds`
- `AutoLogoutWhenIdle`
- `AutoLogoutIdleMinutes`

Inside `AppConfig.AutoSwitchSettings`:

- `SizeTolerancePercent`
- `AspectRatioTolerance`
- `MinImageWidth`
- `MinImageHeight`
- `MaxConsecutiveFailures`
- `MinimumMatchScore`
- `RequireSizeCheck`

Inside `AppConfig.SystemDiagnostics`:

- `Enabled`
- `RefreshIntervalSeconds`
- `CpuWarningPercent`
- `CpuCriticalPercent`
- `RamWarningPercent`
- `RamCriticalPercent`
- `DiskWarningPercent`
- `DiskCriticalPercent`
- `AutoCleanupEnabled`
- `DiskCleanupStartPercent`
- `DiskCleanupTargetPercent`
- `CleanupCooldownMinutes`
- `MaxDeletedFoldersPerCycle`
- `AdditionalCleanupFolders`

#### Configuration procedure

When moving to a machine or updating a runtime:

1. Keep using the active `C:\QtisVision\cfg\Config.xml` contract.
2. Do not replace configured paths with hardcoded folders.
3. If a key above is missing, let the application normalize defaults at load time.
4. Save the configuration once from the HMI so the file and MySQL snapshot stay aligned.

### Database notes

#### Main tables used by this version

- `tblgenerale`
- `tblproduzione`
- `tbllogevent`
- `tblglobalcounters`
- `tblconfigsnapshot`
- `cfginspection`
- `pulsarsdk_auth.roles_authorizations`

#### Important table details

`tblgenerale`

- stores inspection outcomes and reject details
- `PieceData` is used by `DataInspector` to locate saved image folders
- `InspectionStatusDetails` stores richer status detail
- `NC_SurfaceCheck` supports additional reject detail persistence

`tblproduzione`

- recipe persistence and production sessions
- JSON columns:
  - `RecipeParamerterTop`
  - `RecipeParamerterSide`
  - `LastParameter`
  - `ConfigParameter`
- soft-delete support:
  - `IsDeleted`
  - `CreatedAt`
  - `UpdatedAt`
- `TimeFine` must remain nullable for compatibility with the aligned baseline

`tbllogevent`

- central application / runtime / diagnostics event stream
- top-bar alarm badge now counts only `Error` and `Critical`

`tblconfigsnapshot`

- stores XML snapshots for configuration recovery
- used by `ConfigurationRecoveryService`

### Operator / maintainer notes

- the version popup is opened by clicking the Pulsar company logo in the left navigation panel
- the popup shows:
  - current software version
  - assembly version
  - runtime configuration targets
  - main integrated module versions such as VisionPro, WebView2, MySQL driver and NLog
- `DataInspector` visibility and image review still depend on real `PieceData` persistence in `tblgenerale`
- runtime remains config-driven: do not introduce machine-folder assumptions outside `Config.xml`


## Version 1.1.1.2

### Release metadata

- release date: `2026-04-07`

### Release intent

Incremental release generated to extend the runtime from the fixed `Top + Side` assumption to a role-driven camera architecture that also supports `Top + Front` traceability inspection.

### Main functional integrations included

- the VisionPro runtime now resolves the semantic role of each job from `CameraConfig.xml` plus the active VPP job set, instead of assuming that the secondary inspection is always `Side`
- the inspection pipeline now supports `Top + Front` pairing, where the `Front` camera performs traceability presence/code validation while `Top` keeps the product-inspection workflow
- the front camera now updates its own runtime status panel with traceability state, received code and expected prefix
- image save flow, inspection counters, alarm-card mapping and production snapshots now include the new `FrontTraceability` defect family
- recipe persistence now treats the front camera as a first-class recipe block and stores `recipeParamFront` both in XML and in the recipe metadata stored in MySQL
- the camera overview container now rebuilds its visible camera views from the runtime job-role mapping, which makes future camera additions less invasive than the historical fixed `Top/Side` branching
- the canonical release script was fixed so `IncrementSoftwareRelease.ps1` can be executed directly in PowerShell sessions without parameter-binding errors

### Configuration-file notes

- `Config.xml` now formally exposes the front-traceability output names used by the validator:
  - `FrontTraceabilityPresenceOutput`
  - `FrontTraceabilityCodeOutput`
- recipe XML now includes:
  - `cameraSetting.FrontCameraTriggerDelay`
  - `recipeParamFront.RequireTraceability`
  - `recipeParamFront.ExpectedCodePrefix`
  - `ejectionStatus.FrontTraceability`
  - `Counter.FrontTraceability`
  - `InspectionThresholds.FrontInspections`
- `CameraConfig.xml` remains the semantic source of truth for camera roles (`Top`, `Side`, `Front`, ...), while the active `.vpp` decides which of those roles are actually instantiated at runtime

### Database notes

- `tblgenerale` includes the front traceability result columns used by the runtime snapshot:
  - `TraceabilityDetected`
  - `TraceabilityCode`
  - `TraceabilityExpectedPrefix`
  - `NC_Traceability`
- `tblproduzione` now also stores `RecipeParamerterFront` so the recipe metadata archived in MySQL remains aligned with the XML recipe model
- the database bootstrap keeps the migration backward-compatible by adding the new column only when it is missing

### Operator / maintainer notes

- on machines configured as `Top + Front`, the main overview and processing pipeline now ignore side-specific pairing and bind the secondary inspection to the front traceability camera
- if a VPP exposes jobs that are not present in `CameraConfig.xml`, the application logs a warning and does not render unmatched camera panels
- if `CameraConfig.xml` contains configured roles that are not present in the active VPP, those views are also skipped at runtime and logged as configuration drift
- for future extensions, new cameras should be integrated by assigning a semantic role in `CameraConfig.xml` and then adding role-specific validation only where needed, instead of cloning the historical `Top/Side` path end-to-end

## Version 1.1.1.3

### Release metadata

- release date: `2026-04-07`

### Release intent

Incremental release generated to give priority to the active QuickBuild job names over `CameraConfig.xml` numeric IDs when resolving camera roles and visible camera panels.

### Main functional integrations included

- camera-role resolution now prefers the semantic role inferred from the active VPP job name (`Top`, `Front`, `Side`, ...) when that role is explicit
- `CameraConfig.xml` remains available as a semantic fallback, but it no longer forces the wrong secondary camera just because its numeric IDs were left in the historical `0/1/2/...` order
- the camera overview now renders the runtime job roles exposed by QuickBuild even when the matching role is missing or stale in `CameraConfig.xml`, logging the mismatch instead of hiding the panel
- role-mapping diagnostics now log when a QuickBuild semantic role overrides the historical role that would have been inferred from the config file ID

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- `CameraConfig.xml` is still supported and still useful as a fallback semantic contract
- practical rule from this release onward:
  - if the VPP job name is already descriptive (`Top`, `Front`, `Side`, ...)
  - that role has priority over the camera ID mapping found in `CameraConfig.xml`

### Database notes

- no new MySQL schema changes are required by this release

### Operator / maintainer notes

- if a machine loads a QuickBuild with jobs `Top` and `Front`, the HMI now shows `Top` and `Front` even if `CameraConfig.xml` still contains `Top` on `0` and `Side` on `1`
- this reduces the risk of misleading runtime behavior when a commissioning update changes the QuickBuild but the camera-config file is not updated at the same time
- `CameraConfig.xml` should still be kept coherent, but a stale camera ID assignment no longer silently hides a valid runtime view

## Version 1.1.1.4

### Release metadata

- release date: `2026-04-07`

### Release intent

Incremental release generated after a software update on 2026-04-07.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.1.5

### Release metadata

- release date: `2026-04-08`

### Release intent

Incremental release generated after a software update on 2026-04-08.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.1.6

### Release metadata

- release date: `2026-04-08`

### Release intent

Incremental release generated after a software update on 2026-04-08.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.1.7

### Release metadata

- release date: `2026-04-08`

### Release intent

Incremental release generated after a software update on 2026-04-08.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.1.8

### Release metadata

- release date: `2026-04-13`

### Release intent

Incremental release generated after a software update on 2026-04-13.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers



## Version 1.1.3.5

### Release metadata

- release date: `2026-04-22`

### Release intent

Incremental operator-documentation release generated to align the machine release with the latest `Top3D / Top2D` panel behavior and with the recipe-driven 3D image-view workflow.

### Main functional integrations included

- added a dedicated operator manual for the `3DCheck` / `Top3D` flow under `Docs/Manual/04_Controllo_3D_Top3D.md`
- documented the operator steps to:
  - load the correct 3D recipe
  - configure `Top3D primary view` and `Top2D secondary view`
  - understand the fallback to `Config.xml -> LasRunParam`
  - read `Measured`, `Target`, `GOOD`, `NO GOOD`, `WAIT`
  - troubleshoot missing `Top3D` / `Top2D` images before escalating
- linked the 3D procedure from the existing recipe/alarm/operator guidance so the manual path remains coherent for commissioning and production operators

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- the documented fallback remains:
  - `LastRunView1` for `Top3D`
  - `LastRunView2` for `Top2D`

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- operators now have a dedicated, step-by-step manual page for 3D image visualization and measurement interpretation
- maintainers should keep the operator manual aligned whenever the `Top3D` recipe fields, view paths or runtime labels change


## Version 1.1.3.7

### Release metadata

- release date: `2026-04-22`

### Release intent

Incremental operator-UI refinement release generated to make the panel render more proportionally on 15-inch HMI displays without changing the machine/runtime contract.

### Main functional integrations included

- compacted the main shell spacing and navigation width so more horizontal space remains available for runtime pages
- reduced the visual footprint of the top KPI/status bar while keeping the same information set
- reduced the vertical footprint of the camera overview cards so Top3D / Top2D runtime panes feel less oversized on small displays
- `PreferenceView` now uses responsive card wrapping and card-width recalculation instead of staying fixed in a wide 3-column desktop composition
- the same operator page can now degrade more gracefully from 3 columns to 2 and then to 1 when the available viewport width becomes tight

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- no recipe XML schema changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release changes layout density only; it does not change recipe, database or machine-side semantics
- validate the refreshed UI on the actual 15-inch target panel in at least:
  - overview with two camera panes active
  - system preferences
  - recipe pages with long labels
- if a page still feels oversized after this release, prefer page-local responsive tuning over whole-window zoom so Cognex displays and operator hit areas stay stable

## Version 1.1.3.8

### Release metadata

- release date: `2026-04-22`

### Release intent

Incremental responsive-layout release generated to let the same HMI adapt more coherently across operator panels from roughly 15-inch to 23-inch displays.

### Main functional integrations included

- introduced centralized responsive layout profiles inside `MainWindow`
- the shell now recalculates layout metrics at runtime from the real viewport size instead of relying only on fixed compact values
- the left navigation, top KPI/status bar and camera overview now read shared dynamic resources that change with the active panel size
- practical runtime profiles now cover:
  - compact 15-inch style panels
  - intermediate mid-size panels
  - larger 23-inch style panels
- `PreferenceView` keeps its own wrapping behavior and now benefits from the new shell-level responsiveness around it

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- no recipe XML schema changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release changes UI rendering behavior only; it does not change machine logic, recipe semantics or database contracts
- validate the panel on at least one compact screen and one larger operator monitor because the layout now reacts to the actual runtime viewport
- for future UI work, prefer binding important layout sizes to the centralized dynamic resources before introducing new fixed widths or heights

## Version 1.1.3.9

### Release metadata

- release date: `2026-04-22`

### Release intent

Incremental stabilization release generated to fix a WPF startup crash introduced during the new multi-panel responsive-layout work.

### Main functional integrations included

- fixed the responsive shell so startup no longer fails with:
  - `XamlParseException` on `RowDefinition.Height`
  - inner `InvalidCastException`
- replaced the fragile `DynamicResource -> double` usage on WPF layout-definition properties with explicit typed runtime updates from `MainWindow.xaml.cs`
- preserved the adaptive layout behavior across compact, medium and large operator panels after the crash fix
- kept the preference-page wrapping and the camera/topbar/navigation responsiveness added in the previous release

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- no recipe XML schema changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release is a UI/runtime stabilization fix only
- if a similar exception reappears in future responsive work, verify first whether the target WPF property expects:
  - `double`
  - `Thickness`
  - `CornerRadius`
  - `GridLength`
  before wiring it to a shared resource

## Version 1.1.4.0

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental machine-integration release generated to add a guarded PowerFlex 525 inverter page for conveyor speed monitoring, parameter review and controlled technical edits without making inverter communication mandatory for production runtime.

### Main functional integrations included

- added `Setting -> PowerFlex 525` for Administrator, Installer and Expert users
- introduced a PowerFlex 525 parameter catalog for:
  - monitor values `b001`, `b003`, `b004`, `b005`, `b017`
  - motor parameters `P031..P037`
  - ramp and limit parameters `P041..P044`
- added pending-change rendering with `old -> new` values and Apply / Cancel / Restore actions
- added a dedicated Modified M UI section ready to show drive parameters changed from default when the validated CIP adapter exposes them
- marked `P031`, `P032`, `P036`, `P043` and `P044` as stop-required parameters so Apply can be blocked while the drive is running
- replaced the random top-bar transport speed with the PowerFlex-derived `m/min` value when available
- added a non-blocking EtherNet/IP service boundary: missing IP, unreachable drive or unavailable CIP adapter are shown in the UI and do not stop the machine runtime
- added local JSON and MySQL history for PowerFlex parameter change attempts
- updated server-message labels and runtime language files for the new page

### Configuration-file notes

- new optional `Config.xml` section:
  - `PowerFlex525.Enabled`
  - `PowerFlex525.IpAddress`
  - `PowerFlex525.TimeoutMs`
  - `PowerFlex525.PollIntervalMs`
  - `PowerFlex525.HistoryFilePath`
  - `PowerFlex525.MetersPerMinutePerHz`
  - `PowerFlex525.DrivePulleyDiameterMm`
  - `PowerFlex525.GearRatio`
- speed in `m/min` uses the direct `MetersPerMinutePerHz` factor when configured; otherwise it can be calculated from frequency, nominal motor data, pulley diameter and gear ratio

### Database notes

- added `tblpowerflex525_history`
- table creation is best-effort and non-blocking:
  - startup migration checks create it when DB is available
  - the PowerFlex history repository also creates it before insert if needed
- no recipe XML migration is required

### Operator / maintainer notes

- use `Docs/Manual/05_PowerFlex525_Inverter_Nastro.md` for the operator workflow
- verify the PowerFlex IP and the m/min conversion during commissioning
- if the drive is disconnected, the page reports unavailable values but the machine continues to operate
- the current safe EtherNet/IP client checks reachability and keeps parameter reads/writes behind the `IPowerFlex525Client` adapter until the plant-approved CIP implementation is validated on the real drive

## Version 1.1.4.1

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental runtime-safety fix generated after validating the PowerFlex 525 integration on a development PC without the inverter connected.

### Main functional integrations included

- PowerFlex 525 is no longer created or polled during `ServiceLocator.Initialize()`
- `PowerFlex525Service.Start()` no longer forces an immediate network refresh
- EtherNet/IP endpoint probing now uses a guarded `BeginConnect` / `EndConnect` path and converts rejected or cancelled connections to an unavailable-drive state
- development PCs and machines without inverter network access can start the HMI without the PowerFlex integration becoming startup-critical

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- existing `PowerFlex525` keys remain optional

### Database notes

- no MySQL schema changes are introduced by this release
- `tblpowerflex525_history` remains the table introduced by `1.1.4.0`

### Operator / maintainer notes

- on development PCs without the inverter, the HMI should start normally
- PowerFlex values remain unavailable until the page is opened and the drive network is reachable
- if the drive is disconnected, the page should show unavailable state instead of terminating the application

## Version 1.1.4.2

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental diagnostics release generated to make PowerFlex 525 network and parameter-read status visible in the HMI and machine logs during inverter commissioning.

### Main functional integrations included

- added a dedicated `Connection` status card to the PowerFlex 525 page
- the page now distinguishes EtherNet/IP reachability from CIP parameter adapter availability
- added log entries for:
  - configured IP missing
  - EtherNet/IP endpoint reachable on TCP `44818`
  - EtherNet/IP endpoint timeout or rejected connection
  - parameter read unavailable because the CIP adapter is not connected
- repeated connectivity logs are throttled to avoid log flooding during periodic polling

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release
- existing `PowerFlex525.IpAddress` and `PowerFlex525.TimeoutMs` are now also reported in connection diagnostics

### Database notes

- no MySQL schema changes are introduced by this release

### Operator / maintainer notes

- a successful `ping` only proves the IP responds; parameter values require EtherNet/IP endpoint access and the validated CIP parameter adapter
- if the HMI shows `EtherNet/IP OK - CIP adapter non collegato`, the drive is reachable but the parameter read layer is not yet active
- check the NLog files for `PowerFlex525` entries during commissioning

## Version 1.1.4.3

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental integration release generated to add the first real EtherNet/IP explicit messaging read path for PowerFlex 525 monitor parameters.

### Main functional integrations included

- implemented EtherNet/IP `RegisterSession`, `SendRRData` and `UnregisterSession`
- implemented CIP `Get Attribute Single` requests for PowerFlex parameter reads
- first read path uses service `0x0E`, class `0x0F`, instance equal to the parameter number and attribute `0x01`
- enabled read attempts for monitor values:
  - `b001` Output Frequency
  - `b003` Output Current
  - `b004` Output Voltage
  - `b005` DC Bus Voltage
  - `b017` Output Power
- added raw hex logs for request and response frames to support real-drive commissioning
- kept writes disabled until read behavior is validated

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release

### Database notes

- no MySQL schema changes are introduced by this release

### Operator / maintainer notes

- validate `b001` first on the real drive and inspect `PowerFlex525 CIP request` / `PowerFlex525 CIP response` entries in the NLog files
- if the drive returns CIP general status errors, use the raw logged request/response to adjust class, instance or attribute according to the installed firmware/manual revision
- do not enable parameter writes until readback values are confirmed against the inverter keypad or Connected Components Workbench

## Version 1.1.4.4

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental PowerFlex 525 diagnostics release generated after validating monitor reads, extending readback to all tracked parameters and adding fault-to-event logging.

### Main functional integrations included

- extended CIP readback to all tracked PowerFlex parameters:
  - monitor `b001`, `b003`, `b004`, `b005`, `b017`
  - diagnostics `b006`, `b007`, `b008`, `b009`
  - motor `P031..P037`
  - ramps/limits `P041..P044`
- added fault-status rendering in the PowerFlex page
- `b007` is treated as the most recent fault code; non-zero values generate a critical alarm event
- `b008` and `b009` are retained as recent fault history in the displayed fault summary
- drive RUN/STOP is derived from live `b001` output frequency and annotated with raw `b006` status when available
- fault events are routed through `ApplicationEventLogger.LogAlarmEvent` so they are visible in event history and database logging

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release

### Database notes

- no new table is introduced by this release
- PowerFlex fault events use the existing application event logging/database path

### Operator / maintainer notes

- compare PowerFlex HMI values with the drive keypad or Connected Components Workbench during commissioning
- if `Stato fault` shows `FAULT Fxxx`, check the event monitor and machine logs for the generated `PowerFlex 525 fault Fxxx` entry
- parameter writes remain disabled by design

## Version 1.1.4.5

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental UI integration release generated to show live PowerFlex 525 belt speed in the top menu.

### Main functional integrations included

- `TopMenuBarViewModel` now starts the optional PowerFlex polling service
- top menu speed refreshes when `PowerFlex525Service.MetersPerMinuteText` changes
- speed is displayed with explicit `m/min` unit
- the dedicated PowerFlex page is no longer required before the top menu can receive inverter speed

### Configuration-file notes

- no new `Config.xml` keys are introduced by this release

### Database notes

- no MySQL schema changes are introduced by this release

### Operator / maintainer notes

- the top bar speed card should now show values such as `51.29 m/min` when the inverter read succeeds and conversion is configured
- when the inverter is unavailable or conversion is incomplete, the top bar shows `0 m/min`

## Version 1.1.4.6

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental PowerFlex 525 runtime release generated to restore visible conveyor speed feedback and add safe fault reset / fault-history clear commands on the real drive.

### Main functional integrations included

- corrected the inverter scaling used for `P032 Motor NP Hertz`, which was previously displayed with an invalid `x0.01` reduction
- updated belt-speed rendering so the panel now resolves speed in this order:
  - `PowerFlex525.MetersPerMinutePerHz`
  - mechanical conversion from `b001`, `P032`, `P036`, `DrivePulleyDiameterMm` and `GearRatio`
  - commissioning fallback using live `b001` when machine conversion data is still missing
- added two dedicated PowerFlex operator actions:
  - `Reset fault` using `A551 = 1`
  - `Clear fault history` using `A551 = 2`
- extended the EtherNet/IP explicit-messaging client to support parameter instances above `255`, required for `A551`
- added command logging and history persistence for the new fault-reset operations in the same local JSON / MySQL flow already used by the PowerFlex page

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- the existing speed-conversion keys remain the authoritative machine contract:
  - `PowerFlex525.MetersPerMinutePerHz`
  - `PowerFlex525.DrivePulleyDiameterMm`
  - `PowerFlex525.GearRatio`
- when these values are not yet commissioned, the HMI now falls back to `b001` only for visualization so runtime diagnostics stay visible without moving machine conversion logic into recipe/runtime state

### Database notes

- no new MySQL tables or columns are introduced by this release
- the existing `tblpowerflex525_history` table now also stores the dedicated `A551` fault-reset and fault-history-clear command entries

### Operator / maintainer notes

- operators can now reset the active drive fault from the PowerFlex page after removing the fault cause
- operators can now clear the drive fault history buffer from the same page
- maintainers should still configure the real `m/min` conversion in `Config.xml`; the live `b001` fallback is a commissioning aid, not the final calibrated speed model

## Version 1.1.4.7

### Release metadata

- release date: `2026-04-23`

### Release intent

Incremental runtime release generated to support supervised demo/fair auto-login and direct panel editing of PowerFlex speed-conversion parameters.

### Main functional integrations included

- added a new startup config flag `Configuration.AutoLoginAdministratorForDemo`
- when the flag is `true`, the HMI now starts directly with an `Administrator` session for supervised exhibition/demo usage
- when the flag is `false`, the existing `Guest / Viewer` startup behavior remains unchanged
- centralized startup-session bootstrap so splash startup and top-bar session initialization use the same config-driven decision
- added a new machine-settings card inside `Setting -> PowerFlex 525` to edit directly from the panel:
  - `MetersPerMinutePerHz`
  - `DrivePulleyDiameterMm`
  - `GearRatio`
- saving those values now updates `Config.xml`, recalculates displayed speed and logs the operation through the application event logger

### Configuration-file notes

- new optional key under `Configuration`:
  - `AutoLoginAdministratorForDemo`
- existing PowerFlex machine-speed keys are now editable directly from panel and remain machine-level config:
  - `PowerFlex525.MetersPerMinutePerHz`
  - `PowerFlex525.DrivePulleyDiameterMm`
  - `PowerFlex525.GearRatio`
- keep `AutoLoginAdministratorForDemo = false` on normal production machines

### Database notes

- no new MySQL tables or columns are introduced by this release
- the existing application event log path records PowerFlex machine-settings saves as operational events

### Operator / maintainer notes

- use demo auto-login only on supervised fair/demo systems where bypassing the password is an explicit operating choice
- after the fair/demo, disable `AutoLoginAdministratorForDemo` to restore normal authentication behavior
- for conveyor speed commissioning, if you already know the real measured `m/min` at a known frequency, populate `MetersPerMinutePerHz`; otherwise use pulley diameter and gear ratio

## Version 1.1.4.8

### Release metadata

- release date: `2026-04-24`

### Release intent

Analytics MySQL trend-query compatibility fix.

### Main functional integrations included

- fixed the SQL syntax used by analytics trend queries on the machine MySQL server
- measurement columns are now quoted and aggregate aliases are explicit
- 15-minute bucketing now uses a more compatible timestamp expression

### Configuration-file notes

- `PowerFlex525.MetersPerMinutePerHz` remains the only runtime speed-conversion parameter used for m/min calculation
- `AnalyticsDashboard` stores:
  - `RefreshMinutes`
  - `TotalBarColor`
  - `GoodBarColor`
  - `NoGoodBarColor`
  - `Cards` visibility list

### Database notes

- no schema change
- analytics reads existing production data from `tblgenerale`

### Operator / maintainer notes

- if a measurement column is missing on a machine database, the dashboard shows the reason instead of rendering a fake empty trend
- mixed machine baselines with old/new height column spellings are now supported by the same runtime

## Version 1.1.4.9

### Release metadata

- release date: `2026-04-24`

### Release intent

Incremental release generated after a software update on 2026-04-24.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.5.0

### Release metadata

- release date: `2026-04-24`

### Release intent

Incremental release generated after a software update on 2026-04-24.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.5.1

### Release metadata

- release date: `2026-04-24`

### Release intent

Incremental release generated after a software update on 2026-04-24.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.5.2

### Release metadata

- release date: `2026-04-24`

### Release intent

Incremental release generated after a software update on 2026-04-24.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.5.3

### Release metadata

- release date: `2026-04-24`

### Release intent

Incremental release generated after a software update on 2026-04-24.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.5.4

### Release metadata

- release date: `2026-04-24`

### Release intent

Incremental analytics-hardening release generated to make the internal dashboard easier to read on the panel and less intrusive on UI responsiveness.

### Main functional integrations included

- added an in-memory analytics snapshot cache so reopening `Overview -> Data Analysis` can reuse the most recent dashboard state before the next database refresh completes
- hardened the recipe file resolver used by analytics so only valid recipe XML files with the expected `Recipedata` root are accepted
- prevented unsafe fallback to unrelated runtime recipes when defect filtering is requested for a different selected recipe
- upgraded measurement trend cards with:
  - X axis time labels
  - Y axis value labels
  - nominal reference line
  - tolerance range band and limit lines
  - richer summary text including nominal/tolerance values when available from the recipe
- connected trend reference values directly to the selected recipe data for:
  - side `Height`
  - `Top3D` height
  - `Top3D` width
  - `Top3D` length

### Configuration-file notes

- no new `Config.xml` keys are required by this release
- the existing `AnalyticsDashboard` section remains valid
- the cache is runtime-memory only and does not introduce new persisted configuration contracts

### Database notes

- no MySQL schema changes are required by this release
- the dashboard continues to read the existing `tblgenerale` columns already introduced by the analytics baseline
- recipe-based trend references come from recipe XML, not from new database tables or migrations

### Operator / maintainer notes

- the first dashboard opening after application startup may still need a full refresh if no snapshot has been produced yet; subsequent openings reuse the cached snapshot and feel lighter
- if a selected recipe does not resolve to a valid XML file, the dashboard now prefers hiding unsafe recipe-specific analytics instead of guessing from unrelated files
- the new trend rendering is intended to help operators judge dispersion quickly by comparing measured min/avg/max curves against the nominal line and the tolerance band

## Version 1.1.5.6

### Release metadata

- release date: `2026-04-27`

### Release intent

Incremental commissioning-localization release generated to align the merged `DigitalIOControl` page with the global server-message language system used by the HMI.

### Main functional integrations included

- `DigitalIOControl` keeps its existing `L10n[key]` bindings, but the resolver now reads from `ServerMessagePersonalize` first
- `ServerMessageStructure.Messages` now supports JSON extension data, allowing runtime language files to include tool-specific labels without requiring a dedicated C# property for each label
- `ServerMessagePersonalize` now exposes a `MessagesUpdated` event so indexer-based WPF bindings can refresh when the runtime language is reloaded
- `scripts\UpdateRuntimeLanguageFiles.ps1` imports the tool label catalogs from `Localization\messages_eng.json` and `Localization\messages_ita.json` into the runtime server-message files
- runtime language files were refreshed with the Digital I/O / commissioning labels:
  - `Tool_Commissioning_*`
  - `Tool_Signals_*`
  - `Tool_TestIO_*`
  - `Tool_Log_*`

### Configuration-file notes

- no `Config.xml` changes are required by this release
- the active language continues to follow the existing application language configuration and runtime files under `C:\QtisVision\Language`

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- after this release, Digital I/O / commissioning labels are maintained through the same server-message JSON files as the rest of the panel
- when adding future labels for this page, add them to the server-message runtime files or to the imported tool catalog and rerun `scripts\UpdateRuntimeLanguageFiles.ps1`
- the local `Localization\messages_*.json` files remain as source/compatibility catalogs for the commissioning module, but runtime display now prefers `ServerMessage`


## Version 1.1.5.7

### Release metadata

- release date: `2026-04-27`

### Release intent

Incremental commissioning-UI cleanup release generated to remove `ComboBoxItem` alignment binding warnings from the merged `DigitalIOControl` page.

### Main functional integrations included

- added an explicit `ComboBoxItem` style in `DigitalIOControl.xaml`
- eliminated runtime binding warnings on:
  - `ComboBoxItem.HorizontalContentAlignment`
  - `ComboBoxItem.VerticalContentAlignment`
- kept combo-box behavior unchanged while making item alignment deterministic in popup/drop-down templates

### Configuration-file notes

- no `Config.xml` changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release only reduces WPF binding noise in the commissioning UI; it does not change machine I/O mapping, recipe behavior, or runtime automation logic


## Version 1.1.5.8

### Release metadata

- release date: `2026-04-27`

### Release intent

Incremental commissioning-UI hardening release generated to remove residual `ComboBoxItem` alignment binding warnings that still appeared from drop-down popup item containers.

### Main functional integrations included

- added a shared `DigitalIoComboBoxStyle` in `DigitalIOControl.xaml`
- assigned `DigitalIoComboBoxItemStyle` through `ComboBox.ItemContainerStyle`
- made `CompactComboStyle` inherit from the shared combo style so compact and regular combo boxes behave consistently
- kept combo-box behavior and data bindings unchanged

### Configuration-file notes

- no `Config.xml` changes are required by this release

### Database notes

- no MySQL schema changes are required by this release

### Operator / maintainer notes

- this release only reduces WPF binding noise in the commissioning UI; it does not change machine I/O mapping, recipe behavior, or runtime automation logic


## Version 1.1.6.6

### Release metadata

- release date: `2026-04-29`

### Release intent

Incremental release generated after a software update on 2026-04-29.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers


## Version 1.1.7.6

### Release metadata

- release date: `2026-05-12`

### Release intent

Incremental release generated after a software update on 2026-05-12.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers


## Version 1.1.8.9

### Release metadata

- release date: `2026-05-20`

### Release intent

Incremental release generated after a software update on 2026-05-20.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.1.9.0

### Release metadata

- release date: `2026-05-20`

### Release intent

Incremental release generated after a software update on 2026-05-20.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers


## Version 1.2.1.0

### Release metadata

- release date: `2026-05-28`

### Release intent

Incremental release generated after a software update on 2026-05-28.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 1.2.1.2

### Release metadata

- release date: `2026-05-29`

### Release intent

Rendere il manuale operatore consultabile dal pannello in modo piu' professionale, leggibile su pannelli piccoli e scaricabile in PDF impaginato con logo Pulsar.

### Main functional integrations included

- pagina Manuale con controlli zoom in/out/reset sul contenuto
- parser manuale esteso: le tabelle Markdown diventano tabelle strutturate nella UI
- esportazione PDF del manuale selezionato con logo Pulsar, immagini e tabelle funzione/azione/supporto
- manuale principale riscritto in formato titolo, immagine e tabella per ogni area operativa
- nuove chiavi lingua per esportazione PDF e intestazioni tabella

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- dalla pagina Manuale l'operatore puo' ingrandire la guida e salvare un PDF
- il PDF viene generato localmente dal pannello e usa le immagini presenti in `Docs\Manual\Images`
- la modifica non cambia il ciclo macchina, le ricette, gli I/O o la logica runtime


## Version 1.2.1.3

### Release metadata

- release date: `2026-05-29`

### Release intent

Allineare le tabelle del manuale ai numeri presenti nelle immagini, aggiungendo una colonna ID visibile sia nel pannello sia nel PDF esportato.

### Main functional integrations included

- modello righe manuale esteso con campo `ID`
- rendering HMI delle tabelle manuale con colonna ID come primo riferimento
- export PDF aggiornato con colonna ID
- manuale principale aggiornato: tutte le tabelle usano ID numerico coerente con i riferimenti visivi della schermata
- nuove chiavi lingua per intestazione ID

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- l'operatore puo' leggere il numero sull'immagine e trovare la stessa voce nella colonna ID della tabella
- la modifica riguarda solo manuale, UI manuale e PDF; non cambia il runtime macchina


## Version 1.2.1.4

### Release metadata

- release date: `2026-05-29`

### Release intent

Correggere l'export PDF del manuale: il testo delle tabelle risultava invisibile perche' ereditava il colore di riempimento dello sfondo.

### Main functional integrations included

- generatore PDF aggiornato per forzare il colore testo prima di ogni scrittura
- tabelle PDF del manuale nuovamente leggibili, inclusa la colonna ID
- immagini e impaginazione PDF invariati

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- rigenerare il PDF dal pannello dopo l'aggiornamento; i PDF creati con `1.2.1.3` possono mostrare tabelle apparentemente vuote
- la modifica riguarda solo export PDF manuale; nessun impatto su runtime macchina


## Version 1.2.1.5

### Release metadata

- release date: `2026-05-29`

### Release intent

Aggiungere alla pagina Data Analysis l'esportazione PDF dei soli grafici visualizzati, cosi' i dati filtrati possono essere archiviati senza includere controlli o campi di configurazione della pagina.

### Main functional integrations included

- pulsante `Esporta PDF` nella testata Data Analysis
- cattura dell'area grafici visibile: riepilogo produzione, distribuzione difetti e trend attivi
- generatore PDF locale A4 orizzontale con logo Pulsar, periodo/ricetta filtrati e impaginazione multipagina se l'area grafici e' lunga
- messaggi lingua aggiornati per export completato o fallito

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- l'export archivia solo i grafici attualmente visualizzati nella pagina Data Analysis, rispettando filtri e layout corrente
- non cambia il runtime macchina, il ciclo automatico, ricette, I/O o acquisizione


## Version 1.2.1.6

### Release metadata

- release date: `2026-05-29`

### Release intent

Rendere professionale e leggibile l'export PDF Data Analysis, evitando tagli a meta' pagina e migliorando la qualita' grafica del report.

### Main functional integrations included

- export PDF basato sulle singole card grafico visibili invece che su una sola immagine lunga della dashboard
- impaginazione A4 orizzontale con una card per pagina, intestazione, logo, filtro periodo/ricetta, contatore grafico e numero pagina
- rendering ad alta risoluzione e immagini lossless per preservare testo, linee e assi dei grafici
- cropping controllato delle card molto larghe per evitare eccessivo ridimensionamento dei trend

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- rigenerare il PDF dal pannello per ottenere la nuova impaginazione; i PDF creati con `1.2.1.5` possono risultare poco leggibili
- non cambia il runtime macchina, il ciclo automatico, ricette, I/O o acquisizione


## Version 1.2.1.7

### Release metadata

- release date: `2026-05-29`

### Release intent

Correggere il PDF Data Analysis vuoto generato dalla release precedente, sostituendo il percorso di rendering che produceva immagini bianche con una cattura diretta compatibile con i viewer PDF.

### Main functional integrations included

- cattura diretta del controllo WPF tramite `RenderTargetBitmap`
- immagini grafico esportate in JPEG alta qualita' invece di stream lossless `FlateDecode`
- mantenuta l'impaginazione a card singole introdotta in `1.2.1.6`
- rimosso il riferimento non necessario a `System.IO.Compression`

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- rigenerare il PDF dal pannello; i PDF creati con `1.2.1.6` possono contenere pagine senza grafici
- non cambia il runtime macchina, il ciclo automatico, ricette, I/O o acquisizione


## Version 1.2.1.8

### Release metadata

- release date: `2026-05-29`

### Release intent

Correggere l'export PDF Data Analysis quando la card distribuzione difetti/grafico a torta viene renderizzata vuota o nera.

### Main functional integrations included

- la cattura PDF delle card ora usa una superficie bianca locale e un `VisualBrush` centrato sull'elemento da esportare
- le card posizionate in colonne laterali o con offset di layout vengono esportate dal proprio rettangolo visibile, senza trascinare l'offset della griglia contenitore
- mantenuta l'impaginazione PDF a card singole, A4 orizzontale, con immagini JPEG ad alta qualita'

### Configuration-file notes

- `Config.xml` invariato
- nessun nuovo parametro macchina o ricetta

### Database notes

- nessuna modifica DB

### Operator / maintainer notes

- rigenerare il PDF Data Analysis dal pannello; i PDF creati con `1.2.1.7` possono non mostrare il grafico a torta difetti
- non cambia il runtime macchina, il ciclo automatico, ricette, I/O o acquisizione

## Version 2.0.0.0

### Release metadata

- release date: `2026-06-03`
- previous baseline archived as: `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\SoftwareArchives\QtisVisionPanel_1.2.1.8_pre_multishot_20260603_135710.zip`

### Release intent

Aprire la nuova major release `2.0.0.0` e integrare il Side/Left encoder-driven MultiShot Trigger in modo conservativo, disabilitato di default e senza spostare logica immagine fuori da VisionPro.

### Main functional integrations included

- nuovo controller `MultiShotTriggerController` dedicato alla generazione di impulsi hardware encoder-driven
- nuovo `MultiShotTriggerPlanBuilder` per validare configurazione e calcolare target encoder
- supporto al ruolo software `Side` visualizzato come `Left` per questa macchina
- aggancio a fronte fotocellula, encoder reale, encoder virtuale, timeout e stop `RunContinuous`
- skip del single-shot Side quando MultiShot Side/Left e' abilitato, per evitare doppio impulso
- eventi diagnostici `MULTISHOT_SIDELEFT_*` con metadata strutturati
- label lingua `lbLeftCamera`

### Configuration-file notes

- `Config.xml` principale invariato
- aggiunto nodo additivo in `machine_runtime_config.xml`:
  - `MachineMultiShotTrigger/Side/Enabled`
  - `CameraRole`
  - `DisplayName`
  - `TriggerOutputName`
  - `TriggerPulseMs`
  - `ShotCount`
  - `InitialOffsetPulses`
  - `StepPulses`
  - `MaxShotCount`
  - `SessionTimeoutMs`
  - `RequireMachineRunning`
  - `PositiveDirection`
- default: `Enabled=false`, quindi il comportamento precedente resta invariato finche' il commissioning non abilita esplicitamente la funzione

### Database notes

- nessuna modifica schema MySQL
- nessuna migrazione dati

### Operator / maintainer notes

- VisionPro resta responsabile di acquisizione multiframe, stitching e ispezione finale
- la HMI genera solo N impulsi trigger hardware su Left/Side
- per testare 5 scatti: impostare `Enabled=true`, `ShotCount=5`, `InitialOffsetPulses=0`, `StepPulses=4500`
- verificare con oscilloscopio/DAQNavi che `OUT_CAMERA_SIDE_TRIGGER` generi gli impulsi attesi
- se la macchina viene fermata o l'encoder non avanza entro timeout, la sessione viene cancellata e l'uscita viene forzata bassa

## Version 2.0.0.1

### Release metadata

- release date: `2026-06-03`

### Release intent

Allineare la camera fisica Side al ruolo runtime `Left` quando il job QuickBuild espone quel nome/ruolo, mantenendo la stessa logica gia' usata per `Top` e `Top3D`: il job VisionPro decide il ruolo semantico, mentre HMI, display e sincronizzazione usano il canale macchina esistente.

### Main functional integrations included

- `Left` aggiunto ai ruoli camera supportati
- il job `Left`, `SideLeft`, `Side_Left` o equivalenti viene normalizzato a ruolo `left`
- il ruolo `left` usa la stessa coda secondaria e lo stesso display fisico della Side
- validazione Left dedicata dentro `ToolBlockValidator`
- output VisionPro booleani attesi:
  - `LeftSideSealingOk`
  - `LeftRollCountOk`
- alias supportati per compatibilita':
  - saldatura: `SideSealingOk`, `SealingOk`, `LeftSealingOk`
  - conteggio rotoli: `RollCountOk`, `RollsCountOk`, `RollCount`
- nuovo difetto runtime `SideRollCount` per contatori e allarmi scarto
- indicatore operatore aggiunto nel pannello Side/Left

### Configuration-file notes

- nuovi campi opzionali in `Config.xml` sotto `<Configuration>`:
  - `LeftSideSealingOutput`, default `LeftSideSealingOk`
  - `LeftRollCountOutput`, default `LeftRollCountOk`
- se i campi non sono presenti nei Config.xml esistenti, il codice usa i default e mantiene compatibilita'

### Database notes

- nessuna nuova tabella e nessuna nuova colonna obbligatoria
- `cfg_inspection` riceve il default additivo `SideRollCount`
- i contatori runtime/backup includono la nuova chiave `SideRollCount`

### Operator / maintainer notes

- Per usare la camera fisica Side come Left, nominare il job VisionPro `Left` o `SideLeft`
- Il toolblock `Results` del job Left deve restituire solo esiti booleani per:
  - saldatura laterale
  - conteggio rotoli dentro la busta
- Con job Side standard il comportamento resta invariato
- Con job Left la validazione non cerca `Heigth`, `SealingArea` numerica o `ShapeSide`; si aspetta i booleani configurati sopra

## Version 2.0.0.4

### Release metadata

- release date: `2026-06-04`

### Release intent

Eliminare errori binding WPF generati dai `ComboBoxItem` nella pagina diagnostica I/O quando il popup ComboBox viene creato senza trovare temporaneamente l'antenato `ItemsControl`.

### Main functional integrations included

- aggiunto stile implicito locale `ComboBoxItem` in `DigitalIOControl.xaml`
- allineamenti `HorizontalContentAlignment` e `VerticalContentAlignment` fissati localmente, senza binding al `ItemsControl`

### Configuration-file notes

- nessuna modifica a file macchina, ricette o database
- nessuna modifica alla logica MultiShot, VisionPro o runtime I/O

## Version 2.0.0.3

### Release metadata

- release date: `2026-06-04`

### Release intent

Rendere configurabile da HMI il Side/Left MultiShot e i parametri VisionPro `ImageStitching`, evitando che l'operatore o il collaudatore debba modificare manualmente il file macchina XML.

### Main functional integrations included

- aggiunta sezione HMI `Side/Left MultiShot` nella pagina `I/O Diagnostics` / commissioning
- configurazione da pannello di:
  - abilitazione MultiShot
  - uscita trigger Side/Left
  - numero scatti
  - durata impulso
  - offset primo scatto in impulsi encoder
  - distanza tra scatti in impulsi encoder
  - numero massimo scatti
  - timeout sessione
  - direzione encoder
  - guardia macchina in running
  - `VisionProStitching.StepMm`
  - `VisionProStitching.MmPerPixel`
- il salvataggio e la preview runtime riconfigurano anche `MultiShotTriggerController`, non solo `MachineController`
- il segnale trigger MultiShot viene marcato come binding runtime nella tabella I/O
- aggiunte label lingua tramite `scripts/UpdateRuntimeLanguageFiles.ps1`

### Configuration-file notes

- nessun campo ricetta aggiunto
- nessuna modifica database
- i valori restano nel file macchina `machine_runtime_config.xml`, nodo `MachineMultiShotTrigger/Side`
- il ToolBlock VisionPro resta fisso come `ImageStitching` con input `expectedFrames`, `stepMm`, `mmPerPixel`

### Operator / commissioning notes

Configurazione semplice dal pannello:

1. aprire `Diagnostics -> I/O Diagnostics`
2. aprire la pagina commissioning/intervention points
3. nella sezione `Side/Left MultiShot`, abilitare `Enable Side/Left MultiShot`
4. scegliere l'uscita reale della camera Left/Side, normalmente `OUT_CAMERA_SIDE_TRIGGER`
5. impostare `Shots` con il numero di frame richiesto dal job VisionPro
6. impostare `First shot offset` e `Distance between shots` in impulsi encoder
7. impostare `Stitching step` in mm e `Left calibration` in mm/pixel
8. premere `Save configuration`
9. caricare o ricaricare la ricetta/job Left e avviare produzione

### Maintainer notes

- La HMI continua a non fare stitching immagine.
- VisionPro acquisisce N frame e restituisce solo il risultato finale.
- Il controller trigger hardware esistente non e' stato riscritto.

## Version 2.0.0.2

### Release metadata

- release date: `2026-06-04`

### Release intent

Applicare automaticamente al ToolBlock VisionPro `ImageStitching` i parametri macchina necessari al job script Left/SideLeft dopo il caricamento del VPP e prima del `RunContinuous`, senza cambiare la logica trigger MultiShot e senza implementare stitching nella HMI.

### Main functional integrations included

- aggiunto applicatore `VisionProStitchingParameterApplier`
- il job runtime con ruolo `left` riceve i parametri solo quando `MachineMultiShotTrigger.Side.Enabled=true`
- il ToolBlock `ImageStitching` viene cercato ricorsivamente nel job Left/SideLeft
- input scritti:
  - `expectedFrames = MachineMultiShotTrigger.Side.ShotCount`
  - `stepMm = MachineMultiShotTrigger.Side.VisionProStitching.StepMm`
  - `mmPerPixel = MachineMultiShotTrigger.Side.VisionProStitching.MmPerPixel`
- logging operativo aggiunto:
  - `VISIONPRO_STITCHING_PARAMS_APPLIED`
  - `VISIONPRO_STITCHING_TOOLBLOCK_NOT_FOUND`
  - `VISIONPRO_STITCHING_INPUT_MISSING`
  - `VISIONPRO_STITCHING_CONFIG_INVALID`

### Configuration-file notes

- `Config.xml` ricetta invariato
- `MachineMultiShotTrigger/Side` riceve sotto-nodo macchina additivo:
  - `VisionProStitching/ToolBlockName`, default `ImageStitching`
  - `VisionProStitching/ExpectedFramesInputName`, default `expectedFrames`
  - `VisionProStitching/StepMmInputName`, default `stepMm`
  - `VisionProStitching/MmPerPixelInputName`, default `mmPerPixel`
  - `VisionProStitching/StepMm`
  - `VisionProStitching/MmPerPixel`
- `StepMm` e `MmPerPixel` devono essere tarati in commissioning; se non sono > 0 viene loggato `VISIONPRO_STITCHING_CONFIG_INVALID`

### Database notes

- nessuna nuova tabella
- nessuna nuova colonna MySQL

### Operator / maintainer notes

- il job VisionPro Left/SideLeft deve contenere il ToolBlock `ImageStitching`
- il ToolBlock deve esporre input `expectedFrames`, `stepMm`, `mmPerPixel` o i nomi configurati
- VisionPro resta responsabile di acquisire N frame, stitchare nel job script e pubblicare solo il risultato finale
- HMI non raccoglie immagini, non crea macro immagini e non cambia il controller trigger

## Version 2.0.1.9

### Release metadata

- release date: `2026-06-08`

### Release intent

Incremental release generated after a software update on 2026-06-08.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 2.0.2.0

### Release metadata

- release date: `2026-06-08`

### Release intent

Incremental release generated after a software update on 2026-06-08.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers







## Version 3.0.0.1

### Release metadata

- release date: `2026-06-09`

### Release intent

Incremental release generated after a software update on 2026-06-09.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 3.0.0.2

### Release metadata

- release date: `2026-06-10`

### Release intent

Incremental release generated after a software update on 2026-06-10.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers


## Version 3.0.7.1

### Release metadata

- release date: `2026-07-13`

### Release intent

Hardening del classificatore ONNX shadow-mode e del training per impedire statistiche
mescolate o metriche di validazione sovrastimate.

### Main functional integrations included

- lo shadow classifier attende in un worker background fino a 10 secondi che l'immagine
  accodata sia stata chiusa dal writer, senza bloccare ispezione, scarto o UI;
- le statistiche shadow vengono separate automaticamente quando cambiano file/versione
  modello, resize, normalizzazione, modalita' colore o soglia minima NOK;
- ogni gruppo riceve un identificatore stabile `set`, riportato nella UI e negli eventi shadow
  insieme a versione modello e soglia, per separare anche le analisi storiche dei log;
- i confronti avviati con una configurazione precedente vengono scartati tramite una
  generazione runtime e non contaminano la nuova finestra statistica;
- PC Diagnostics mostra il contesto statistico attivo e rende osservabili campioni saltati
  per single-flight e timeout immagine;
- pre-scan C# e trainer Python applicano la stessa regola per label globali legacy e label
  per-camera;
- il training usa un holdout cronologico stratificato; con meno di 10 OK o 10 NOK nel solo
  holdout il modello viene creato ma marcato esplicitamente come validazione provvisoria.

### Configuration-file notes

- nessuna modifica a `Config.xml` e nessun campo ricetta;
- il template macchina include ora esplicitamente `DefectClassifierMinConfidence` e
  `SideDefectClassifierMinConfidence`, entrambi con default `0` (filtro disabilitato);
- i file macchina esistenti restano compatibili e vengono completati dal normale upgrade
  dello schema `machine_runtime_config.xml`.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- il classificatore resta esclusivamente advisory: non cambia l'esito VisionPro, i contatori
  produzione o il comando di scarto;
- dopo un cambio modello o soglia, la finestra statistiche riparte automaticamente da zero e
  mostra il nuovo contesto;
- una validazione `provisional` richiede altri campioni recenti, soprattutto NOK, prima di
  considerare il modello affidabile per valutazioni operative.


## Version 3.0.7.2

### Release metadata

- release date: `2026-07-13`

### Release intent

Isolare il classificatore ONNX shadow-mode dalle risorse usate dal ciclo macchina, rendere il
percorso disabilitato un no-op reale e limitare in modo esplicito CPU e memoria nativa.

### Main functional integrations included

- fast path volatile per profilo disabilitato o sessione non pronta: nessun task, ricerca file,
  bitmap o tensor per pezzo;
- una sola inferenza globale tra TOP e SIDE, oltre al single-flight gia' presente per camera;
- ONNX Runtime sequenziale con un thread intra-op e uno inter-op, worker a priorita' bassa,
  CPU memory arena e memory pattern disabilitati;
- preprocessing bitmap tramite `LockBits` BGR al posto di `GetPixel`;
- statistiche shadow estese con tempo inferenza medio/massimo e attesa risorsa media;
- accordi ordinari spostati a DEBUG; disaccordi, errori e timeout restano WARN/audit.

### Configuration-file notes

- nessuna modifica a `Config.xml`, `machine_runtime_config.xml` o file ricetta;
- i profili classificatore restano disabilitati per default e indipendenti per TOP/SIDE;
- un profilo abilitato senza modello valido viene segnalato al load e resta no-op nel ciclo.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- l'AI resta advisory: non modifica esito VisionPro, trigger, scarto, contatori o ricette;
- sulle macchine senza modello lasciare i profili ONNX disabilitati; Python non e' richiesto per il
  normale funzionamento HMI;
- la memoria virtuale non e' usata come acceleratore: il runtime limita concorrenza e allocazioni
  RAM per mantenere la latenza prevedibile;
- il test raccomandato confronta 15 minuti AI OFF e 15 minuti AI ON con la stessa ricetta/cadenza;
- eventuale backlog SIDE visibile in `CAMERA_TIMING` resta una diagnostica separata dal servizio AI.

## Version 3.0.7.3

### Release metadata

- release date: `2026-07-13`

### Release intent

Eliminare i falsi warning stderr durante il training ONNX mantenendo invariati algoritmo e modello.

### Main functional integrations included

- conversione loss tramite `loss.detach().item()` per progressi batch e media epoca;
- filtro mirato della deprecazione PyTorch 2.8 sul futuro cambio dell'exporter ONNX;
- exporter TorchScript, opset 12, validazione e scrittura atomica modello invariati.

### Configuration-file notes

- nessuna modifica a `Config.xml`, configurazione macchina o ricette.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- il training in corso puo' continuare anche in presenza del vecchio warning: non indicava un
  modello corrotto;
- dalla `3.0.7.3` le normali righe loss/export non producono `AI_TRAINING_STDERR`;
- eventuali altre righe stderr restano intenzionalmente visibili e devono essere analizzate.



## Version 3.0.7.9

### Release metadata

- release date: `2026-07-22`

### Release intent

Allineare i nominali delle ispezioni Top3D alle dimensioni prodotto della ricetta e rendere piu'
leggibili e proporzionate le card dei trend nella pagina di analisi statistica.

### Main functional integrations included

- per macchine `3DCheck`, al salvataggio ricetta `Product_Height`, `Product_Width` e
  `Product_Length` sono propagati rispettivamente nei nominali Top3D altezza, larghezza e lunghezza
- i nominali Top3D sono mostrati in sola lettura come proiezione delle proprieta' prodotto; le
  tolleranze Top3D restano modificabili separatamente
- le card dei trend usano un grafico responsivo, un pannello riepilogo piu' compatto e unita' `mm`
  leggibili, senza esporre il nome tecnico della colonna DB

### Configuration-file notes

- nessuna nuova chiave e nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica a tabelle o colonne; i valori gia' archiviati in `RecipeParamerterTop3D` vengono
  aggiornati attraverso il normale salvataggio ricetta

### Operator / maintainer notes

- dopo la modifica delle dimensioni prodotto, salvare la ricetta e verificare che i nominali Top3D
  coincidano con altezza, larghezza e lunghezza prodotto
- ricette XML esistenti restano compatibili e non richiedono migrazione preventiva


## Version 3.0.8.0

### Release metadata

- release date: `2026-07-22`

### Release intent

Rendere l'esportazione PDF della pagina Data Analysis piu' compatta, leggibile e adatta alla
stampa, eliminando le grandi aree vuote rilevate nel report a una card per pagina.

### Main functional integrations included

- esportazione A4 orizzontale con massimo due card per pagina
- riduzione tipica da sette a quattro pagine per la dashboard completa
- scaling proporzionale separato per ogni slot, senza deformare grafici o testi
- intestazione aggiornata con intervallo dei grafici contenuti nella pagina

### Configuration-file notes

- nessuna nuova chiave e nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica a schema, query o dati MySQL

### Operator / maintainer notes

- riesportare il report dalla pagina Data Analysis; i PDF prodotti in precedenza non vengono
  modificati automaticamente
- verificare leggibilita' di titoli, assi, legenda e riepiloghi su tutte le pagine generate


## Version 3.0.8.1

### Release metadata

- release date: `2026-07-22`

### Release intent

Separare chiaramente l'analisi globale dalla vista delle sole ispezioni pertinenti alla ricetta
attualmente in produzione.

### Main functional integrations included

- nuovo filtro predefinito `Solo ispezioni della ricetta attiva`
- `Tutte le ricette` esegue ora una vera aggregazione globale e non ricade implicitamente sulla
  ricetta corrente
- la distribuzione difetti della ricetta attiva interseca stato ricetta e feature realmente
  disponibili nel runtime/VPP
- controlli Bottom non disponibili non compaiono più in una dashboard Top3D

### Configuration-file notes

- nessuna nuova chiave e nessuna modifica a `Config.xml`

### Database notes

- nessuna modifica a schema o dati MySQL; cambia soltanto il filtro applicato alle query esistenti

### Operator / maintainer notes

- usare `Solo ispezioni della ricetta attiva` per la diagnostica della produzione corrente
- usare `Tutte le ricette` soltanto quando si vuole confrontare intenzionalmente l'intero periodo
- le categorie con conteggio difetti zero restano nascoste dal grafico a torta


## Version 3.0.8.2

### Release metadata

- release date: `2026-07-23`

### Release intent

Compensare in modo tracciabile un errore residuo di calibrazione Top3D senza modificare nominali e
tolleranze delle ricette.

### Main functional integrations included

- tre offset globali macchina per altezza, larghezza e lunghezza Top3D
- formula autorevole `misura corretta = misura grezza VisionPro + offset`
- valore corretto usato in modo coerente per esito, contatori, Overview, storico DB e Data Analysis
- offset attivo visibile nelle card operatore
- validazione valori finiti e limite di sicurezza `-1000..+1000 mm`
- inserimento decimale compatibile con punto o virgola
- audit `TOP3D_MEASUREMENT_OFFSETS_SAVED`
- UI e manuale aggiornati in italiano e inglese

### Configuration-file notes

- nuove chiavi opzionali sotto `Configuration`:
  - `Top3DHeightMeasurementOffsetMm`
  - `Top3DWidthMeasurementOffsetMm`
  - `Top3DLengthMeasurementOffsetMm`
- i file esistenti restano compatibili: campo assente equivale a `0`
- nessuna modifica al file ricetta

### Database notes

- nessuna migrazione e nessuna nuova colonna
- le colonne `ThreeD*MeasureValue` continuano a contenere il valore effettivamente validato, ora
  corretto quando un offset e configurato

### Operator / maintainer notes

- configurare da `System Preferences -> Config.xml -> VisionPro outputs`
- accesso riservato a Installer/Administrator
- calcolare `offset = riferimento certificato - media VisionPro`
- dopo il salvataggio ripetere il test con il riferimento e controllare l'evento audit


## Version 3.0.8.3

### Release metadata

- release date: `2026-07-24`
- previous baseline: `3.0.8.2`

### Release intent

Integrare il controllo profilo Top3D Essity come monitoraggio di processo tracciabile, mantenendo
inalterata la decisione di scarto fisico della macchina e la compatibilita' con ricette, VPP e
database precedenti.

### Main functional integrations included

- contratto configurabile per gli output `HeightMedian`, `HeightHighTail`,
  `HeightValidPixelRatio`, `HeightBulge` e `ThreeDHeight_MAX`
- altezza dimensionale risolta da `HeightMedian`, con fallback a `ThreeDHeight`
- applicazione dell'offset macchina solo all'altezza ufficiale; valori profilo archiviati grezzi
- controllo profilo per ricetta con stati `GOOD`, `NO GOOD`, `INVALID`, `MONITOR` e `WAIT`
- separazione generale tra difetto rilevato e difetto reject-enabled
- classificazione, contatori, immagini e storico aggiornati per anomalie profilo senza comando di
  espulsione o allarme di scarto
- Recipe Management, Overview Top3D e Data Inspector aggiornati
- localizzazioni italiane e inglesi e manuale operatore aggiornati

### Configuration-file notes

- nuove chiavi opzionali in `Config.xml -> Configuration`:
  - `ThreeDHeightMedianOutput` (default `HeightMedian`)
  - `ThreeDHeightHighTailOutput` (default `HeightHighTail`)
  - `ThreeDHeightValidPixelRatioOutput` (default `HeightValidPixelRatio`)
  - `ThreeDHeightBulgeOutput` (default `HeightBulge`)
  - `ThreeDHeightMaximumOutput` (default `ThreeDHeight_MAX`)
- nuovi campi opzionali in `RecipeParamTop3D`:
  - `ThreeDProfileEnabled` (default `false`)
  - `ThreeDHeightMinimumValidPixelRatio` (default `0.75`)
  - `ThreeDHeightMaximumBulge` (default `0`, soglia non configurata)
- nessuna modifica a I/O, encoder, trigger, quote intervento o `ejectionStatus`

### Database notes

- migrazione incrementale di `tblgenerale` con colonne nullable:
  - `ThreeDHeightMedianValue FLOAT(10,3)`
  - `ThreeDHeightHighTailValue FLOAT(10,3)`
  - `ThreeDHeightMaximumValue FLOAT(10,3)`
  - `ThreeDHeightBulgeValue FLOAT(10,3)`
  - `ThreeDHeightValidPixelRatio FLOAT(10,5)`
  - `NCThreeDProfile INT`
- semantica `NCThreeDProfile`: `1=GOOD`, `0=NO GOOD/INVALID`, `4=MONITOR/WAIT`
- insert e Data Inspector verificano la presenza delle colonne per tollerare database legacy

### Operator / maintainer notes

- il controllo nasce disabilitato e non modifica il ciclo delle ricette esistenti
- configurare per ricetta la percentuale minima di pixel validi e l'indice pancia massimo
- un profilo `NO GOOD` o `INVALID` viene registrato come prodotto non conforme ma non comanda lo
  scarto fisico in questa baseline
- collaudare con VPP Top3D aggiornato e verificare i cinque output nel ToolBlock
- procedura completa in `top3d-profile-monitoring-baseline-2026-07-24.md`

## Version 3.0.8.4

### Release metadata

- release date: `2026-07-24`

### Release intent

Eliminare la race di inizializzazione tra il polling Advantech e il caricamento della
configurazione di `MachineController`, senza modificare la logica macchina dopo l'avvio.

### Main functional integrations included

- stato di inizializzazione del controller pubblicato e letto in modo thread-safe
- eventi input ed encoder iniziali mantenuti fuori dal tracking finche' il runtime macchina non
  e' configurato
- callback provenienti da un manager I/O precedente ignorati durante il cambio HW/SIM
- sincronizzazione autorevole del contatore preservata tramite l'arm automatico degli encoder
  configurati

### Configuration-file notes

- nessuna nuova chiave e nessuna modifica a `Config.xml` o
  `machine_runtime_config.xml`

### Database notes

- nessuna modifica MySQL

### Operator / maintainer notes

- all'avvio non deve piu' comparire
  `Errore nel polling loop: MachineController not initialized.`
- non e' richiesto alcun reset encoder: il riferimento viene acquisito automaticamente al termine
  dell'inizializzazione runtime
- collaudo consigliato sia in simulazione sia con schede reali, includendo un cambio HW/SIM


## Version 3.0.8.5

### Release metadata

- release date: `2026-08-04`

### Release intent

Sincronizzare la fase dei due Cycling Preset DALSA usati dal doppio MultiShot
Front/Backlight, mantenendo invariata la sequenza trigger encoder esistente.

### Main functional integrations included

- configurazione automatica dei preset camera 1/2 tramite l'accesso GenICam
  `OwnedGigEAccess` gia' disponibile nel job VisionPro;
- preset alternati a ogni `StartOfFrame`, con esposizione macchina configurabile
  (default `500 us`) e uscite separate `Line3` / `Line4`;
- reset di fase `cyclingPresetResetCmd` prima della prima sessione e prima della
  sessione successiva a cancel, timeout, errore impulso o recovery VisionPro;
- blocco del reset mentre una sessione MultiShot precedente risulta attiva;
- lettura diagnostica di `cyclingPresetCurrentActiveSet`, quando supportata;
- scrittura degli input VisionPro `dualIlluminationEnabled` e `frontFirst` nel
  ToolBlock `ImageStitching`;
- pagina Machine Setup estesa con parametri e validazione della doppia illuminazione;
- log applicativi e audit dedicati agli eventi di configurazione, reset e riallineamento.

### Configuration-file notes

- nuovo blocco opzionale macchina
  `MachineMultiShotTrigger/<profilo>/DualIllumination` per `Side`, `Right` e
  `Bottom`;
- il blocco e' aggiunto automaticamente con `Enabled=false`, quindi i file
  esistenti e il MultiShot standard restano compatibili;
- nessun campo aggiunto alle ricette e nessuna modifica a `Config.xml`.

### Database notes

- nessuna tabella, colonna o migrazione MySQL.

### Operator / maintainer notes

- usare un numero totale di scatti pari e almeno 2 quando la doppia illuminazione
  e' abilitata;
- collegare luce frontale e backlight alle due uscite camera configurate e
  verificare la polarita' elettrica prima della prova prodotto;
- ricaricare il VPP dopo il salvataggio e verificare nei log
  `CAMERA_CYCLING_PRESETS_CONFIGURED` e
  `CAMERA_CYCLING_PRESET_RESET_ARMED`;
- il collaudo reale della temporizzazione e delle uscite DALSA resta obbligatorio
  sulla testa camera installata.

## Version 3.0.8.6

### Release metadata

- release date: `2026-08-05`

### Release intent

Rendere immediata l'identificazione delle camere nelle applicazioni da una a
cinque teste e mantenere l'overview leggibile sui pannelli industriali senza
modificare il ciclo macchina.

### Main functional integrations included

- viste `LeftCameraView` e `RightCameraView` dedicate;
- distinzione tra ruolo runtime e ruolo visuale, con compatibilita'
  `Right -> rear` preservata;
- template selector esteso a Left e Right;
- griglia responsive fino a quattro camere per riga, con seconda riga dalla
  quinta camera;
- ridimensionamento dinamico dell'altezza dei display Cognex;
- routing risultati, indicatori, cambio ricetta e shutdown allineati;
- label Right aggiunta ai cataloghi lingua e allo script server messaggi.

### Configuration-file notes

- nessun nuovo campo in `Config.xml`, machine runtime config o ricetta;
- nessuna migrazione di `CameraConfig.xml`; i tipi Left/Right esistenti sono
  usati per scegliere la vista.

### Database notes

- nessuna tabella, colonna o migrazione MySQL.

### Operator / maintainer notes

- con quattro camere e almeno 900 px utili, le quattro viste occupano la prima
  riga; dalla quinta camera viene usata una seconda riga;
- su pannelli piu' stretti il numero di colonne diminuisce automaticamente;
- verificare in commissioning che i job VPP siano nominati semanticamente e
  che ogni record compaia nella vista fisica corretta.

## Version 3.0.8.7

### Release metadata

- release date: `2026-08-06`

### Release intent

Rendere immediatamente riconoscibili le ispezioni dimensionali e di profilo 3D nelle viste
operatore, mantenendo invariati il contratto VisionPro e la logica di classificazione.

### Main functional integrations included

- added dedicated industrial icons for 3D height (`Z`), width (`X`), length (`Y`) and profile;
- integrated the icons in the Top3D and Top2D runtime measurement cards;
- aligned inspection configuration and defect-counter icon mapping for height, width and length;
- updated the Top3D operator manual and technical profile-monitoring baseline.

### Configuration-file notes

- no new `Config.xml`, machine runtime or recipe keys are required;
- existing XML files remain compatible without migration.

### Database notes

- no MySQL schema or persistence changes are required.

### Operator / maintainer notes

- the icon identifies the inspection type only;
- the existing `WAIT`, `GOOD`, `NO GOOD`, `INVALID` and `MONITOR` badges remain authoritative;
- VisionPro output reading, validation, counters and physical reject behavior are unchanged;
- resource/XAML validation and `Release x64` build completed with 0 errors;
- verify icon legibility on the target HMI resolution during commissioning.


## Version 3.0.8.8

### Release metadata

- release date: `2026-08-06`

### Release intent

Rendere risolvibili le metriche responsive delle icone camera anche quando una vista viene
caricata autonomamente dal designer XAML, senza perdere l'adattamento dinamico al monitor.

### Main functional integrations included

- moved the three camera-feature defaults from `MainWindow.Resources` to application resources;
- kept screen-size overrides active by updating shared application resources at runtime;
- removed duplicate window-scoped definitions that were invisible to standalone camera views.

### Configuration-file notes

- no `Config.xml`, machine runtime or recipe changes are required.

### Database notes

- no database changes are required.

### Operator / maintainer notes

- no operator workflow changes;
- camera labels and feature icons retain the existing small/medium/large responsive sizing;
- the fix removes XAML resource-resolution errors for standalone camera views;
- XAML parsing and `Rebuild Release x64` completed with 0 errors;
- VisionPro, inspection results, I/O and reject behavior remain unchanged.


## Version 3.0.8.9

### Release metadata

- release date: `2026-08-06`

### Release intent

Completare lo scope applicativo delle risorse responsive usate da `CameraContainer` e
riallineare gli output di sviluppo caricati dal designer XAML.

### Main functional integrations included

- moved `CameraInfoPadding` and `CameraInfoFontSize` to application resources;
- removed the duplicate `MainWindow` definitions;
- removed a local-name collision in `InspectionProcessor` that prevented the `Debug | Any CPU`
  designer assembly from being rebuilt;
- kept the existing responsive runtime overrides through the shared-resource update path.

### Configuration-file notes

- no `Config.xml`, machine runtime or recipe changes are required.

### Database notes

- no database changes are required.

### Operator / maintainer notes

- no operator workflow or camera-routing changes;
- `CameraContainer` and all seven camera templates can resolve their shared metrics independently;
- rebuild the active Visual Studio configuration after a version increment so the XAML designer
  does not load a stale `QtisVisionPanel` assembly;
- `Debug | Any CPU`, `Debug x64` and `Release x64` completed with 0 errors; all seven camera
  generated classes plus `CameraContainer` are present in every verified output;
- all three application outputs report assembly version `3.0.8.9`;
- VisionPro acquisition, inspection results, I/O and reject behavior remain unchanged.


## Version 3.0.9.0

### Release metadata

- release date: `2026-08-06`

### Release intent

Rendere caricabile dal designer XAML l'intero catalogo dei controlli custom,
senza modificare il probing VisionPro usato dal runtime macchina.

### Main functional integrations included

- `Cognex.Vision.Startup.Net` is copied locally as the single VisionPro bootstrap
  dependency required while Visual Studio reflects the project assembly;
- the full VisionPro runtime continues to be resolved through the existing
  `VisionProDependencies` junction;
- all camera views, `StatisticsView` and `CameraTemplateSelector` remain in their
  existing namespaces and require no XAML routing change.

### Configuration-file notes

- no `Config.xml`, machine runtime or recipe changes are required.

### Database notes

- no database changes are required.

### Operator / maintainer notes

- no operator workflow, acquisition, inspection, I/O or reject behavior changes;
- rebuild `Debug | Any CPU`, `Debug x64` and `Release x64` completed with 0 errors;
- all three outputs report `3.0.9.0` and contain the local bootstrap DLL;
- full assembly reflection completed for 1515 types with all reported XAML types present.


## Version 3.0.9.1

### Release metadata

- release date: `2026-08-19`

### Release intent

Eliminare la dipendenza dal secondo caricamento ricetta per un VPP con job
`Top`, `Left` e `Rear`, mantenendo la macchina fuori produzione se una delle
camere richieste non entra realmente in esecuzione continua.

### Main functional integrations included

- camera panels are generated from every supported semantic job in the active
  VPP even while its GigE FrameGrabber is still being enumerated;
- each VisionPro job receives a bounded FIFO-ready wait, up to three start
  attempts and an explicit running-state verification;
- partial starts are rejected and all jobs are stopped if one camera remains
  unavailable;
- diagnostics identify job index/name, acquisition state, FIFO type, grabber
  name and serial;
- automatic recovery reports the latest per-job start failure instead of only
  the generic stopped state;
- multi-camera start timeout increased from 12 to 20 seconds to contain the
  bounded retry sequence without blocking indefinitely.

### Configuration-file notes

- no `Config.xml`, machine runtime or recipe XML changes are required;
- verify that `RearCameraSerial` identifies the camera used by the `Rear` job.

### Database notes

- no database changes are required.

### Operator / maintainer notes

- the Blackfly S model is not filtered by the HMI; VisionPro must expose its
  acquisition FIFO and the configured camera must be reachable on the GigE NIC;
- expected successful sequence ends with
  `VISIONPRO_CONTINUOUS_START_COMPLETE|jobs=3`;
- a transient `CAMERA_VIEW_HARDWARE_PENDING` warning no longer removes the Rear
  panel;
- build `Release x64` completed with 0 errors;
- cold-start validation is documented in
  `visionpro-three-camera-startup-hardening-2026-08-19.md`.


## Version 3.0.9.2

### Release metadata

- release date: `2026-08-19`

### Release intent

Eliminare il cambio ricetta manuale ancora necessario quando il primo
caricamento del VPP avviene prima che la terza camera GigE sia enumerata.

### Main functional integrations included

- the first VPP load identifies all GigE acquisition jobs and the ones whose
  FIFO or FrameGrabber is not ready;
- when at least one job is invalid, the HMI waits up to 20 seconds for a stable
  Cognex FrameGrabber inventory;
- after all expected cameras are visible, the same VPP is shut down and loaded
  once again so VisionPro can bind every acquisition FIFO;
- no reload and no extra wait occur when the first VPP load is already valid;
- inventory logs contain camera names and serials, separating network discovery
  failures from a stale VPP camera binding.

### Configuration-file notes

- no `Config.xml`, machine runtime or recipe XML changes are required;
- `RearCameraSerial` should match the Blackfly used by the `Rear` job for
  unambiguous commissioning diagnostics.

### Database notes

- no database changes are required.

### Operator / maintainer notes

- success requires `VISIONPRO_GIGE_DISCOVERY_READY|available=3|expected=3`,
  `VISIONPRO_VPP_RELOAD_RESULT|status=ready` and
  `VISIONPRO_CONTINUOUS_START_COMPLETE|jobs=3`;
- `available=2|expected=3` followed by `VISIONPRO_GIGE_DISCOVERY_TIMEOUT`
  indicates that the Blackfly is not visible to the Cognex GigE acquisition
  layer and requires NIC, IP, power, driver or camera-ownership checks;
- inventory `3/3` followed by reload `status=invalid` indicates a VPP FIFO,
  serial or video-format binding problem;
- build `Release x64` completed with 0 errors.


## Version 3.0.9.3

### Release metadata

- release date: `2026-08-19`

### Release intent

Impedire che il profilo Left dual MultiShot resti senza trigger a causa di un
file non attivo, di un'uscita duplicata o di una riga I/O con direzione e
categoria non compatibili.

### Main functional integrations included

- risoluzione centralizzata delle uscite fisiche mappate;
- preflight di ogni profilo MultiShot abilitato con log di scheda, canale e
  stato elettrico risolti;
- rifiuto delle ambiguita' con piu' mapping fisici validi;
- validazione HMI prima del salvataggio per uscita trigger duplicata o non
  definita come `Output / CameraTrigger`;
- guida di diagnosi ordinata per file attivo, I/O, VisionPro, Cycling Preset e
  sequenza encoder.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione XML;
- il runtime legge `<cartella applicazione>\cfg\machine_runtime_config.xml`;
- un file `.template.xml` esterno non e' operativo finche' non viene caricato o
  copiato nel percorso attivo;
- ogni `TriggerOutputName` MultiShot deve identificare una sola riga fisica
  `Output / CameraTrigger` con canale DO valido.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- per Left dual MultiShot, `Shots=8` significa 8 trigger totali: 4 Front e 4
  Backlight quando l'alternanza e' abilitata;
- prima della prova verificare in ordine
  `MULTISHOT_SIDE_OUTPUT_MAPPING_RESOLVED`,
  `VISIONPRO_STITCHING_PARAMS_APPLIED` e
  `CAMERA_CYCLING_PRESETS_CONFIGURED`;
- al primo prodotto deve comparire `CAMERA_CYCLING_PRESET_RESET_ARMED`, seguito
  da 8 eventi `MULTISHOT_SIDE_TRIGGER_PULSE` e da
  `MULTISHOT_SIDE_SESSION_COMPLETED`;
- il collaudo elettrico/camera resta necessario: la build non puo' confermare
  ricezione fisica dei trigger o commutazione delle luci.


## Version 3.0.9.4

### Release metadata

- release date: `2026-08-20`

### Release intent

Rendere deterministico l'avvio del job `Rear` quando la Blackfly e' gia'
presente nell'inventario Cognex ma la FIFO FGGigE non ha ancora associato
FrameGrabber e seriale.

### Main functional integrations included

- attesa separata tra discovery globale dei dispositivi e binding delle FIFO
  deserializzate dal VPP;
- fino a tre reload completi e limitati del VPP se un job GigE resta `Invalid`;
- rilascio del manager Cognex e azzeramento dei riferimenti statici a job,
  ToolBlock e code risultati prima di ogni nuovo caricamento;
- tempo di settle tra rilascio manager, nuovo caricamento e verifica FIFO;
- diagnostica per distinguere camera non enumerata, binding ancora pending e
  tentativi esauriti.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione;
- `RearCameraSerial`, dispositivo di acquisizione e video format salvati nel
  VPP restano i riferimenti da verificare se tutti i reload falliscono.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- `available=3|expected=3` conferma solo l'inventario globale; il job e' pronto
  quando `VISIONPRO_VPP_RELOAD_RESULT|status=ready` oppure
  `VISIONPRO_GIGE_BINDING_READY` non riporta piu' job invalidi;
- la sequenza corretta termina con `VISIONPRO_JOB_RUNNING` per Top, Left e Rear,
  poi `VISIONPRO_CONTINUOUS_START_COMPLETE|jobs=3`;
- `VISIONPRO_VPP_RELOAD_EXHAUSTED` richiede verifica di seriale, VPP, formato
  video, IP/NIC e proprieta' esclusiva della Blackfly;
- la macchina resta ferma se anche una sola camera del VPP non parte.


## Version 3.0.9.5

### Release metadata

- release date: `2026-08-20`
- previous baseline: `3.0.9.4`

### Release intent

Evitare che una sequenza Left dual MultiShot valida venga annullata dopo il
primo frame quando il tempo trascorso dalla fotocellula supera il timeout, pur
continuando a ricevere avanzamenti encoder e impulsi regolari.

### Main functional integrations included

- il timeout resta ancorato alla nascita prodotto fino al primo impulso;
- dopo il primo impulso, il timeout viene rinnovato a ogni scatto e misura
  quindi l'inattivita' della sessione, non la sua durata totale;
- il timer ricontrolla sotto lock che la sessione sia ancora scaduta prima di
  rimuoverla, eliminando la race con un impulso appena iniziato;
- il log di timeout include fase, scatto raggiunto, intervallo trascorso,
  limite e ultimo valore encoder;
- regression test isolato completato con 8 impulsi, una sessione completata e
  zero timeout oltre la precedente scadenza globale.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione XML;
- `SessionTimeoutMs=3000` conserva lo stesso formato e diventa un timeout di
  mancato avanzamento dopo il primo impulso;
- mapping verificato per il commissioning corrente:
  `OUT_CAMERA_LEFT_TRIGGER -> PCIE-1756-BE/DO02`.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- il ToolBlock deve avanzare da `frameIndex=1` fino a `frameIndex=8` e infine
  pubblicare `isReady=true`;
- se compare `SESSION_TIMEOUT before first shot`, controllare velocita', quota
  primo target e valore del timeout;
- se compare `SESSION_TIMEOUT after shot N/8`, controllare che il main encoder
  continui ad avanzare e che il runtime non venga fermato;
- il collaudo fisico deve confermare otto fronti su DO02 e l'alternanza 4 Front
  / 4 Backlight.
## Version 3.1.0.0

### Release metadata

- release date: `2026-08-21`
- previous baseline: `3.0.9.9`

### Release intent

Rendere affidabili l'aggiunta e la deserializzazione di `CogClassifyTool` ViDi EL nel
Job Tool Editor incorporato, applicando la sequenza di bootstrap richiesta da Cognex
senza modificare il ciclo macchina.

### Main functional integrations included

- inizializzazione `Startup.ProductKey.VProX` prima di splash, ViewModel e oggetti
  VisionPro;
- attesa limitata del completamento security e bootstrap idempotente condiviso;
- caricamento preventivo delle estensioni Classify/ViDi EL dell'editor embedded;
- riferimento al controllo ufficiale `Cognex.VisionPro.ViDiEL.Controls`;
- diagnostica VPP estesa a inner exception, HResult e LoaderExceptions;
- rimozione dell'inizializzazione Cognex duplicata e del processo `cogtool --print`.

### Configuration-file notes

- nessuna modifica a `Config.xml`, machine runtime XML o XML ricetta

### Database notes

- nessuna tabella, colonna o migrazione MySQL

### Operator / maintainer notes

- nessuna modifica al flusso operatore o al ciclo produzione;
- dopo l'aggiornamento riavviare completamente l'HMI prima del test;
- verificare `VISIONPRO_RUNTIME_INITIALIZED` e
  `VISIONPRO_JOB_EDITOR_EXTENSIONS_READY` nei log;
- aggiungere e salvare un `CogClassifyTool`, ricaricare il VPP e verificare RunOnce e
  RunContinuous seguendo
  `visionpro-vidiel-classify-embedded-editor-2026-08-21.md`.
- template Classify ufficiale e VPP reali `Lucart_Test_sample.vpp` e
  `Index_Fiera_sample.vpp` gia verificati in deserializzazione read-only con bootstrap
  `VProX` anticipato.


## Version 3.1.0.1

### Release metadata

- release date: `2026-08-21`

### Release intent

Eliminare il conflitto nativo che impediva all'HMI di deserializzare VPP contenenti
`CogClassifyTool`, mantenendo il classificatore ONNX shadow-mode disponibile ma
completamente separato dal processo VisionPro.

### Main functional integrations included

- rimosso `Microsoft.ML.OnnxRuntime` dal processo principale QtisVisionPanel;
- introdotto `QtisVisionPanel.OnnxWorker.exe`, processo x64 dedicato in `AiRuntime`;
- inferenza, caricamento e validazione post-training ONNX eseguiti tramite protocollo
  locale stdin/stdout, con un solo thread ONNX e priorita' processo ridotta;
- limite monitorato di 512 MB sul working set del worker e riavvio controllato in caso
  di timeout, crash o superamento limite;
- worker non avviato quando i classificatori sono disabilitati o privi di modello;
- build e installer rimuovono/bloccano le DLL ONNX Microsoft nella directory dell'EXE,
  dove avrebbero precedenza sulla `onnxruntime.dll` Cognex usata da ViDi EL;
- bootstrap `VProX` e supporto editor ViDi EL della `3.1.0.0` mantenuti.

### Configuration-file notes

- nessuna nuova chiave; i profili ONNX Top/Side esistenti restano compatibili;
- `DefectClassifierEnabled=false` continua a essere un no-op e non avvia il worker.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- `C:\QtisVision\bin` non deve contenere `Microsoft.ML.OnnxRuntime.dll` o la relativa
  `onnxruntime.dll`; questi file devono esistere solo in `bin\AiRuntime`;
- dopo l'aggiornamento chiudere ogni istanza HMI precedente e avviare la `3.1.0.1`;
- verificare prima il caricamento del VPP Classify e poi, separatamente, lo shadow-mode
  ONNX seguendo `visionpro-vidiel-onnx-runtime-isolation-2026-08-21.md`.

## Version 3.1.0.2

### Release metadata

- release date: `2026-08-21`

### Release intent

Ripristinare il caricamento affidabile dei VPP contenenti `CogClassifyTool` allineando
la posizione della DLL bootstrap Cognex alla baseline `3.0.2.4` funzionante.

### Main functional integrations included

- `Cognex.Vision.Startup.Net.dll` non viene piu copiata accanto all'EXE;
- Startup, RBBT, ViDi EL e ONNX Cognex vengono risolti dalla stessa junction
  `VisionProDependencies`;
- MSBuild elimina copie residue della DLL Startup dalle cartelle Release, Debug e
  output designer;
- il bootstrap blocca con un messaggio esplicito una distribuzione non valida;
- i log VPP riportano posizione Startup, runtime risolto e moduli nativi caricati;
- installer `r7` blocca la DLL Startup locale sia nella sorgente sia nello staging.

### Configuration-file notes

- nessuna modifica a `Config.xml`, machine runtime XML o XML ricetta.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- chiudere completamente la vecchia HMI prima di distribuire la nuova `bin`;
- verificare che `Cognex.Vision.Startup.Net.dll` non sia direttamente in
  `C:\QtisVision\bin`, ma sia raggiungibile in `VisionProDependencies`;
- il probe sul VPP reale `Lucart_Test_sample.vpp` ha caricato i job `Top,Side` in
  Release e Debug x64; acquisizione e RunContinuous restano da collaudare in macchina.

## Version 3.1.0.3

### Release metadata

- release date: `2026-08-21`
- previous baseline: `3.1.0.2`

### Release intent

Isolare il comando tecnico RunOnce sul job VisionPro selezionato e rendere
autorevoli gli override MultiShot della ricetta gia' durante il primo avvio.

### Main functional integrations included

- `RunOnce` non esegue piu' l'intero `CogJobManager`: usa soltanto il job della
  tab selezionata;
- attesa limitata della FIFO GigE e diagnostica con job, indice, stato
  acquisizione, grabber, seriale ed eccezione interna;
- errore propagato alla UI, senza falso messaggio di esecuzione riuscita;
- applicazione degli aggiustamenti runtime della ricetta prima della validazione
  VisionPro MultiShot al primo startup;
- accesso RunOnce limitato agli utenti tecnici e disabilitato durante Live Preview.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione;
- una ricetta con `MultiShot/SideLeft/EnabledMode=Disabled` viene rispettata dal
  primo caricamento anche se il profilo macchina globale e' abilitato;
- se il profilo effettivo e' abilitato, il relativo job deve contenere il
  ToolBlock `ImageStitching` con gli input previsti.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- selezionare la tab camera prima di premere `RunOnce`;
- `RunOnce` arma il solo job selezionato ma non genera automaticamente un impulso
  fisico; usare `Live preview` per la sequenza run singolo + impulso I/O;
- verificare in macchina i log `VISIONPRO_RUN_ONCE_START`,
  `VISIONPRO_RUN_ONCE_STARTED` o `VISIONPRO_RUN_ONCE_FAILED`.

## Version 3.1.0.4

### Release metadata

- release date: `2026-08-24`

### Release intent

Stabilizzare l'esecuzione multi-camera in ambiente di sviluppo quando i job
QuickBuild usano immagini sintetiche locali e hanno tempi ciclo differenti,
senza allargare le soglie del runtime macchina reale.

### Main functional integrations included

- identificazione esplicita dei risultati prodotti da `CogAcqFifoSynthetic`
- pairing FIFO per job sintetici, poiche' non condividono trigger o timestamp prodotto
- coalescing limitato del backlog sintetico con rilascio dei record Cognex non processati
- timeout simulazione dedicato di 30 s per companion e watchdog
- margine di 2 s tra scadenza attesa companion e dichiarazione di pipeline bloccata
- log `VISION_WATCHDOG_MODE`, `VISION_SIMULATION_PAIRING_ACTIVE` e
  `VISION_SIM_QUEUE_COALESCED`

### Configuration-file notes

- nessuna nuova chiave o migrazione XML
- `Config.xml`, configurazione macchina, ricette e profili MultiShot restano invariati

### Database notes

- nessuna tabella, colonna o migrazione MySQL

### Operator / maintainer notes

- in simulazione verificare `VISION_WATCHDOG_MODE|synthetic=True|result_timeout_ms=30000`
- una crescita temporanea diversa delle code Top/Side e' normale con file sintetici;
  `VISION_SIM_QUEUE_COALESCED` indica che il pannello evita un backlog illimitato
- sulle camere reali il log deve riportare `synthetic=False`; il pairing hardware
  continua a usare timestamp e la soglia single-shot di 5 s
- procedura completa: `Docs/Commissioning/12_VisionPro_Synthetic_MultiCamera_Simulation.md`
- installer offline validato: `3.1.0.4-r9`, 30 payload, bootstrapper e hash
  dell'EXE applicativo verificati


## Version 3.1.0.5

### Release metadata

- release date: `2026-08-24`

### Release intent

Eliminare la restrizione storica che assegnava l'intera HMI a due soli logical
processor e riallineare le prestazioni del classificatore VisionPro ViDi EL a
QuickBuild senza modificare il contratto di ispezione.

### Main functional integrations included

- rimosso `ProcessorAffinity=0x0003` da `MainWindow`;
- il processo usa tutti i logical processor messi a disposizione da Windows;
- minimo ThreadPool mantenuto almeno pari ai processori disponibili, senza il
  precedente raddoppio arbitrario;
- log `VISION_RUNTIME_SCHEDULER` con affinity e valori ThreadPool effettivi;
- guida di commissioning e benchmark ripetibile del `CogClassifyTool`.

### Configuration-file notes

- nessuna chiave nuova o modificata in `Config.xml`, machine runtime XML o XML
  ricetta

### Database notes

- nessuna tabella, colonna o migrazione MySQL

### Operator / maintainer notes

- testare la Release x64 senza debugger e con QuickBuild chiuso;
- verificare che `VISION_RUNTIME_SCHEDULER` esponga tutti i logical processor;
- escludere il primo ciclo di warm-up dalla misura di regime;
- nessun cambiamento a esito, soglia, trigger, scarto o flusso operatore.

### Installer media

- media offline validato: `3.1.0.5-r10`, 30 payload, `7.787.781.213` byte;
- SHA-256 bootstrapper:
  `6D0C8712031F005C15766EF53D90D7C5087D0E66BE74FE94D901D37FC0BFC320`;
- SHA-256 Release x64 e copia nello ZIP applicativo:
  `03E9DB94248C03A6617BFB5CB1AFFBC3B258079A547BBB53397E531332978574`.


## Version 3.1.0.6

### Release metadata

- release date: `2026-08-24`

### Release intent

Visualizzare classe e score dei classificatori VisionPro in modo uniforme e
responsive in tutte le viste camera, senza modificare le regole macchina.

### Main functional integrations included

- lettura comune degli output classe e score dal ToolBlock radice;
- risultato informativo distinto per ogni ruolo camera;
- card XAML riutilizzabile con icona AI, classe, score e badge localizzato;
- routing compatibile `Side`/`Left` e `Rear`/`Right`;
- pulizia automatica della card quando la classificazione non e' disponibile.

### Configuration-file notes

- nessuna chiave nuova o modificata in `Config.xml`, configurazione macchina o ricetta

### Database notes

- nessuna tabella, colonna o migrazione MySQL

### Operator / maintainer notes

- il job VisionPro deve esportare almeno la classe (`EL_Classify` consigliato);
- lo score (`EL_Score` consigliato) e' opzionale;
- la card e' informativa e non aggiunge regole di scarto;
- collaudo: `Docs/Commissioning/14_VisionPro_AI_Classification_Card.md`.

### Installer media

- media offline validato: `3.1.0.6-r11`, 30 payload, `7.787.792.768` byte;
- SHA-256 bootstrapper:
  `C13AB420BA2F752C3E03785D8305EB7B12C4C1C2E6515BB416D5840EB876F406`;
- SHA-256 Release x64 e copia nello ZIP applicativo:
  `E330AA84CB5783E5FD483EA72DC35C462DEB91E81EAD1D819233EC543A2F4159`.


## Version 3.1.0.7

### Release metadata

- release date: `2026-08-24`

### Release intent

Garantire che record grafico, misure, classificazione, validazione e card HMI
appartengano allo stesso evento VisionPro anche quando il job successivo ha gia'
aggiornato il proprio ToolBlock.

### Main functional integrations included

- snapshot output scalari catturato sincronicamente in `UserResultAvailable`;
- separazione tra ToolBlock live per immagini e snapshot per validazione;
- identificatore `ResultSequence` per TOP e companion nei log;
- rilascio deterministico degli snapshot processati, svuotati o coalesciati;
- colore card allineato alla misura `Classification.Passed` del validator.

### Configuration-file notes

- nessuna nuova chiave o modifica a `Config.xml`, configurazione macchina o
  ricetta

### Database notes

- nessuna tabella, colonna o migrazione MySQL

### Operator / maintainer notes

- verificare su campioni consecutivi che immagine, classe, score e messaggio
  difetto siano coerenti;
- usare `topSequence` e `companionSequences` per correlare i gruppi nei log;
- `VISION_RESULT_SNAPSHOT_FAILED` indica che il risultato non e' stato accettato
  per evitare una validazione basata su dati potenzialmente successivi;
- procedura aggiornata in
  `Docs/Commissioning/14_VisionPro_AI_Classification_Card.md`.

### Installer media

- media: `D:\QtisInstallerOutput\QtisVisionPanel_3.1.0.7-r12`;
- payload verificati: `30`, dimensione `7.787.797.041` byte;
- SHA-256 `QtisVisionSetup.exe`:
  `76A527322E0AAC33A2C4327EA72C56C34C939D3E22DA7165F4321B26DAFE22EE`;
- SHA-256 Release x64 e copia nello ZIP applicativo:
  `B80FF0B3DE07BFCAA55341E2F9BAA4F1BA7EA23488AFE8AA9F6B2EC02B8BE127`.


## Version 3.1.0.8

### Release metadata

- release date: `2026-08-25`

### Release intent

Eliminare comandi fisici legacy non configurati su DO00/DO01 e rimuovere una
finestra di concorrenza che poteva lasciare risultati TOP in coda fino al
watchdog/timeout.

### Main functional integrations included

- fallback vuoto e sicuro quando `Recipe_Folder\cfg\io_mapping.xml` e' assente,
  nullo o non deserializzabile;
- supporto invariato per mapping legacy esplicitamente caricati;
- riavvio garantito del drain multi-camera quando un enqueue avviene mentre il
  gate dell'orchestratore e' occupato;
- diagnostica `COMPANION_TIMEOUT` estesa con sequenza TOP e stato delle code;
- manuale MultiShot e matrice commissioning aggiornati.

### Configuration-file notes

- nessuna nuova chiave o migrazione XML;
- `io_mapping.xml` resta opzionale e non viene creato automaticamente;
- mapping macchina, runtime binding, punti intervento e ricette invariati.

### Database notes

- nessuna tabella, colonna o migrazione MySQL.

### Operator / maintainer notes

- senza `io_mapping.xml`, il log atteso all'avvio e'
  `IO_MAPPING_NOT_CONFIGURED`; non devono seguire scritture DO00/DO01 al cambio
  stato della fotocellula salvo mapping esplicito;
- in caso di timeout usare i nuovi campi `queues`, `headSequence`, `lagMs` e
  `valid` per identificare immediatamente il ruolo mancante;
- build Release x64 verificata con zero errori;
- media offline `3.1.0.8-r13` validato con 30 payload; SHA-256 setup
  `3B21D9C5A90EDC00DB23E079C4C916B8244819217B375157219E081671A134B8`;
- SHA-256 HMI Release e HMI nello ZIP applicativo:
  `C40E18776342EBB99808BCDEB1A9C14DFD1987A268FD62F900C1230564C16E7E`.

## Version 3.1.0.9

### Release metadata

- release date: `2026-08-26`

### Release intent

Correggere l'associazione delle ispezioni alla ricetta di produzione e rendere
storici label e score dei classificatori VisionPro per ogni vista camera.

### Main functional integrations included

- `IdProduzione` risolto prima del RunContinuous e copiato in ogni snapshot
  `ProduzioneRecord`;
- normalizzazione del nome ricetta con una sola estensione `.vpp`;
- persistenza Classify separata per Top, Side/Left, Front, Rear/Right e Bottom;
- diagnostica `PRODUCTION_ID_RESOLVED` e `PRODUCTION_ID_UNRESOLVED`;
- una sola lettura dello schema `tblgenerale` per insert, mantenendo il fallback
  compatibile per colonne opzionali.

### Configuration-file notes

- nessuna nuova chiave o migrazione in `Config.xml`, configurazione macchina o
  file ricetta.

### Database notes

- migrazione incrementale idempotente di `tblgenerale` con colonne nullable:
  `TopClassificationLabel`, `TopClassificationScore`,
  `SideClassificationLabel`, `SideClassificationScore`,
  `FrontClassificationLabel`, `FrontClassificationScore`,
  `RearClassificationLabel`, `RearClassificationScore`,
  `BottomClassificationLabel`, `BottomClassificationScore`;
- nessun backfill automatico delle righe storiche con `IdProduzione=0`.

### Operator / maintainer notes

- al primo avvio il servizio DB aggiunge soltanto le colonne mancanti;
- verificare nel log `PRODUCTION_ID_RESOLVED|...|id=<valore positivo>`;
- una vista priva di output Classify salva correttamente `NULL` nei propri due
  campi;
- build Release x64 verificata con zero errori; warning preesistenti invariati;
- collaudo SQL documentato in
  `tblgenerale-production-id-and-classification-2026-08-26.md`.


## Version 3.1.1.0

### Release metadata

- release date: `2026-08-26`

### Release intent

Separare completamente il risultato Classify VisionPro dai controlli surface e
sealing, rendendolo una ispezione ricetta autonoma con scarto, allarme e
contatore dedicati.

### Main functional integrations included

- nuovi flag ricetta `inspectionStatus.AIClassification` ed
  `ejectionStatus.AIClassification`;
- contratto Classify comune per Top, Top3D, Side, Left, Front, Rear/Right e
  Bottom;
- validazione delle etichette conformi e dei nomi difetto liberi;
- card camera subordinata all'abilitazione ricetta;
- contatore dedicato, deduplicato una volta per pezzo;
- famiglia `DefectType.AIClassification` disponibile nelle regole allarme;
- rimozione dei mapping Classify verso `SurfaceCheck` e `SideSealing`.

### Configuration-file notes

- nessuna modifica a `Config.xml` o `machine_runtime_config.xml`;
- i file ricetta possono contenere i due nuovi flag booleani;
- ricette storiche: entrambi i flag assumono `false`, senza migrazione
  obbligatoria.

### Database notes

- `cfg_inspection` riceve la feature `AIClassification`, disabilitata di
  default;
- `tblglobalcounters` usa la chiave `AI_CLASSIFICATION` tramite il mapping
  contatori esistente;
- nessuna nuova colonna in `tblgenerale`; label e score per vista della release
  `3.1.0.9` restano la sorgente di dettaglio.

### Operator / maintainer notes

- abilitare prima il controllo, verificare campioni GOOD/NOK e autorizzare lo
  scarto soltanto dopo il collaudo;
- un NOK su piu' camere aumenta il contatore AI una sola volta per pezzo;
- con controllo disabilitato, classe e score non influenzano esito, scarto,
  allarmi o contatori;
- nessuna modifica a I/O, encoder, trigger, quote o MultiShot;
- procedura completa in
  `ai-classification-standalone-inspection-2026-08-26.md`.


## Version 3.1.1.1

### Release metadata

- release date: `2026-08-26`

### Release intent

Archiviare per ogni vista camera l'esito della classificazione AI con la stessa
semantica NC usata dalle altre ispezioni.

### Main functional integrations included

- esiti distinti per Top/Top3D, Side/Left, Front, Rear/Right e Bottom;
- valori `0=NOK`, `1=GOOD`, `4=non abilitato/non eseguito`;
- output classe vuoto o illeggibile archiviato come `0` quando il controllo e'
  abilitato;
- aggiornamento dello snapshot dello stesso ciclo VisionPro, senza riletture del
  ToolBlock;
- DataInspector compatibile sia con il nuovo schema sia con database storici.

### Configuration-file notes

- nessuna modifica a `Config.xml`, `machine_runtime_config.xml` o file ricetta;
- l'abilitazione continua a dipendere da
  `inspectionStatus.AIClassification` della ricetta.

### Database notes

- migrazione idempotente di `tblgenerale` con le colonne nullable:
  `NC_TopAiClassification`, `NC_SideAiClassification`,
  `NC_FrontAiClassification`, `NC_RearAiClassification` e
  `NC_BottomAiClassification`;
- nessun backfill delle righe storiche;
- INSERT degradabile: le colonne vengono usate soltanto quando presenti nello
  schema letto da `information_schema.COLUMNS`.

### Operator / maintainer notes

- nessun cambiamento nel flusso operatore o nel contatore AI;
- `Left` condivide l'esito Side, `Right` condivide Rear e `Top3D` condivide Top;
- verificare almeno un campione GOOD, uno NOK e una ricetta con AI disabilitata;
- procedura SQL in
  `tblgenerale-production-id-and-classification-2026-08-26.md`.


## Version 3.1.1.2

### Release metadata

- release date: `2026-08-28`

### Release intent

Complete the pending diagnostics and Data Analysis work without changing the
machine cycle, recipe contract or runtime I/O scheduling.

### Main functional integrations included

- AI health warnings update the orange top-bar badge immediately when an
  advisory arrives and when the operator marks the list as seen;
- the badge tooltip is available in Italian and English through the canonical
  runtime language update script;
- Data Analysis defaults to the active production recipe while retaining an
  explicit all-recipes aggregate option;
- defect categories for the active recipe are filtered against recipe and
  runtime/VPP feature availability;
- measurement charts adapt to the available panel width, include their units
  and PDF export places up to two visible charts on each landscape page;
- image-save fallback accepts the established Side key `F` plus `S`/`L`, and
  Rear/Right keys `R`/`RI`, preventing missing fallback images on dynamic
  camera layouts;
- the Python training guide includes the required `onnxscript` package.

### Configuration-file notes

- no new or changed `Config.xml`, machine-runtime XML or recipe fields;
- all existing machine, recipe, encoder, trigger and MultiShot values retain
  their current authority.

### Database notes

- no database schema or migration changes.

### Operator / maintainer notes

- opening or refreshing the AI early-warning panel acknowledges the current
  orange badge; later warnings raise it again;
- `Active recipe inspections` is the normal Data Analysis filter; choose
  `All recipes` only for an intentional mixed-production aggregate;
- verify one Side/Left and one Rear/Right saved-image cycle after deployment;
- no behavior change to inspection decisions, reject, I/O or continuous run.

### Installer media

- offline media regenerated as `3.1.1.2-r13` in
  `D:\QtisInstallerOutput\QtisVisionPanel_3.1.1.2-r13`;
- builder validation and bootstrapper `--validate` completed successfully with
  30 payloads and no errors;
- packaged HMI SHA-256 matches the Release x64 binary:
  `92A52EA233028C33BFB47BE9DB05A7C0F0EA2B0ACD8715E62396086AF554B7D4`;
- setup SHA-256:
  `772D09BB70D3479FE41376942F51043367439435820303C6291481DAE1FA3B34`;
- this packaging refresh does not change machine configuration, recipes,
  database schema or runtime behavior.


## Version 3.1.1.3

### Release metadata

- release date: `2026-08-31`
- previous baseline: `3.1.1.2`

### Release intent

Preservare durante avvio, anteprima e salvataggio I/O i campi di
`MachineRuntimeBindings` non posseduti dalla vista PCIE Settings.

### Main functional integrations included

- `DigitalIOViewModel.BuildRuntimeConfigurationFromView()` aggiorna l'istanza
  caricata invece di ricreare l'intero blocco runtime;
- restano persistenti configurazioni AI, email, Live Preview,
  `MultiShotCompanionMaxLagMs` e gli altri campi non esposti dalla vista.

### Configuration-file notes

- nessun campo nuovo o rimosso;
- `machine_runtime_config.xml` non perde piu' i binding non gestiti dalla UI.

### Database notes

- nessuna modifica MySQL.

### Operator / maintainer notes

- la correzione non modifica mapping I/O, quote, ricette o flusso macchina;
- build Release x64 verificata e media `3.1.1.3-r15` archiviato.

## Version 3.1.1.4

### Release metadata

- release date: `2026-08-31`

### Release intent

Integrare temperature CPU, motherboard/system, memoria e storage nella
diagnostica HMI senza introdurre dipendenze nel ciclo macchina.

### Main functional integrations included

- provider `LibreHardwareMonitorLib` 0.9.6 aperto una sola volta e interrogato
  nel refresh diagnostico background;
- cache e retry limitato per evitare polling hardware continuo e rumore nei log;
- modalita `Auto`, `LibreHardwareMonitor` e `WindowsWmi`, con fallback per
  gruppo di sensori;
- nuova card CPU e soglie configurabili CPU/motherboard;
- notice MPL-2.0 distribuito con i binari.

### Configuration-file notes

- nuovi campi opzionali in `AppConfig.SystemDiagnostics`:
  `HardwareTemperatureProvider`, `HardwareTemperaturePollingSeconds`,
  `CpuTemperatureWarningC`, `CpuTemperatureCriticalC`,
  `MotherboardTemperatureWarningC`, `MotherboardTemperatureCriticalC`;
- nessuna migrazione: i file esistenti ricevono default compatibili.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- `Sensor not available` indica che BIOS/driver non espongono la misura e non
  blocca la macchina;
- usare `Auto` in produzione e collaudare i sensori con HMI elevata sul PC AVS;
- le temperature non partecipano a VisionPro, encoder, I/O, scarto o contatori.

### Installer media

- media: `D:\QtisInstallerOutput\QtisVisionPanel_3.1.1.4-r16`;
- builder e bootstrapper validation: `PASSED`;
- 30 payload, `7.768.131.851` byte;
- SHA-256 `QtisVisionSetup.exe`:
  `5B8393E0C972512A0095EFDBF8BD1312837AEB42E10F15AD59945E9530660C3E`.

## Version 3.1.1.5

### Release metadata

- release date: `2026-09-01`

### Release intent

Rendere la Live Preview adatta alla regolazione camera mostrando l'immagine
grezza del ToolGroup e separare l'aggiornamento software dal provisioning di
una nuova macchina.

### Main functional integrations included

- la Live Preview usa prioritariamente `[ToolGroup Inputs] -> Image Source -> OutputImage`;
- fallback compatibile al LastRun configurato quando il ramo raw non esiste;
- il builder genera sia `QtisVisionPanel_3.1.1.5-r17` sia
  `QtisVisionPanel_Update_3.1.1.5-r17`;
- il pacchetto `UpdateOnly` contiene esclusivamente `ApplicationBin`, confronta
  SHA-256 e mantiene backup, journal, rollback e rimozione deterministica dei
  soli file software precedentemente gestiti;
- il bootstrapper rifiuta un updater con RuntimeSeed/configurazioni o un target
  privo di una HMI gia' installata.

### Configuration-file notes

- nessuna nuova chiave `Config.xml` o `machine_runtime_config.xml`;
- `Programs`, `cfg`, OPC, Language macchina, ricette, VPP, immagini e modelli AI
  non sono inclusi nel media update e non vengono modificati.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- usare l'installer completo sui PC nuovi e l'eseguibile
  `QtisVisionUpdate.exe` solo sulle macchine gia' installate;
- chiudere la HMI prima dell'update e verificare nel log il riepilogo
  `added/updated/removed/unchanged`;
- collaudare la sorgente raw della Live Preview con una camera reale e verificare
  il log `LIVE_IO|Live preview display source selected`.

## Version 3.1.1.6

### Release metadata

- release date: `2026-09-01`

### Release intent

Correggere definitivamente la sorgente della Live Preview tecnica: l'immagine
grezza e' un dato del `CogInputImageTool` QuickBuild e non un ramo stabile del
LastRun record del `CogToolGroup`.

### Main functional integrations included

- `JobToolEditorViewModel` converte il `VisionTool` del job selezionato in
  `CogToolGroup`;
- usa direttamente `Tools[0] as CogInputImageTool` e legge `InputImage`;
- cerca un `CogInputImageTool` successivo se il VPP non mantiene `Image Source`
  al primo indice;
- consuma ancora il `UserResult` tecnico per mantenere limitata la coda
  QuickBuild, senza usarlo come sorgente raw;
- azzera record e graphics prima di mostrare l'immagine grezza;
- registra sorgente selezionata e motivo dettagliato dell'eventuale fallback.

### Configuration-file notes

- nessuna nuova chiave o modifica a `Config.xml`, `machine_runtime_config.xml`
  o XML ricetta

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL

### Operator / maintainer notes

- verificare nel log `source=CogInputImageTool.InputImage`;
- la finestra deve mostrare il frame grezzo senza overlay delle ispezioni;
- `Raw live preview source unavailable` identifica cast, tool o immagine nulli;
- in caso di fallback resta valido `Config.xml -> LasRunParam`;
- collaudare con camera hardware-trigger reale prima della distribuzione.


## Version 3.1.1.7

### Release metadata

- release date: `2026-09-01`

### Release intent

Ridurre il rumore del log operativo prodotto dagli output digitali ad alta
frequenza, senza perdere errori hardware o la tracciabilita' semantica degli
impulsi macchina.

### Main functional integrations included

- le righe raw `Output PCIE-1756 <channel> set to HIGH/LOW` passano da `Info`
  a `Debug`;
- la stessa regola viene applicata agli output PCIE-1884;
- warning ed errori di scrittura Advantech mantengono il livello attuale;
- `Pulse START/END` e gli eventi con nome segnale/motivo restano nel log `Info`.

### Configuration-file notes

- nessuna nuova chiave o modifica a file di configurazione

### Database notes

- nessuna modifica al database

### Operator / maintainer notes

- nel log operativo non devono piu' comparire sequenze ripetitive del solo
  canale fisico HIGH/LOW;
- per una diagnosi elettrica dettagliata impostare temporaneamente NLog a
  livello `Debug`;
- gli errori delle schede e i log degli impulsi applicativi restano visibili.

## Version 3.1.1.8

### Release metadata

- release date: `2026-09-01`

### Release intent

Rendere affidabile la persistenza delle uscite macchina, configurare il mapping fisico dallo stesso
editor dei punti di intervento e selezionare per ricetta quali camere devono essere triggerate,
riducendo nel contempo la latenza del tracking encoder software.

### Main functional integrations included

- nuove righe Machine Outputs inizializzate come Output/CameraTrigger/ActiveHigh/segnale fisico;
- notifica WPF completa per direction, category e polarity;
- save con commit DataGrid, normalizzazione e verifica XML tramite read-back;
- scheda, canale, polarita' e flag fisico modificabili dall'Intervention Point Editor;
- modo trigger per ricetta `Machine`, `Enabled`, `Disabled` per sette ruoli camera;
- polling encoder target `2 ms`, cache dell'abilitazione trigger esterna e scrittura DO prima dei log;
- serializzazione delle scritture sulla scheda per non perdere transizioni concorrenti;
- log diagnostico limitato `IO_POLL_OVERRUN` per cicli I/O di almeno 10 ms.

### Configuration-file notes

- nessuna nuova chiave nel `Config.xml` principale;
- il file macchina mantiene il mapping fisico globale e viene verificato dopo ogni salvataggio;
- nuovo nodo XML ricetta additivo `machineRuntimeAdjustments/CameraTriggers`;
- ricette senza il nuovo nodo mantengono il comportamento precedente tramite modo `Machine`.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL

### Operator / maintainer notes

- eseguire il collaudo di persistenza e la prova ricette 2/3/4 camere descritti in
  `machine-output-recipe-trigger-and-tracking-commissioning-2026-09-01.md`;
- acquisire almeno 100 eventi `target/actual/late` alla velocita' massima prima del rilascio in
  produzione;
- il polling Windows riduce il jitter ma non sostituisce un compare hardware real-time;
- il media installer piu' recente resta `3.1.1.7-r17` finche' non viene generato il nuovo pacchetto.

## Version 3.1.1.9

### Release metadata

- release date: `2026-09-02`

### Release intent

Ridurre il jitter del trigger software e separare gli errori di elaborazione VisionPro dai normali
esiti NOK, mantenendo rapidi gli aggiornamenti HMI su una linea di ispezione continua.

### Main functional integrations included

- polling I/O spostato su worker dedicato con target 2 ms, timer Windows a 1 ms e diagnostica
  `IO_POLL_STARTED`/`IO_POLL_OVERRUN`;
- stato `RunStatus` catturato insieme al risultato camera per evitare letture appartenenti alla
  lavorazione successiva;
- `CogToolResultConstants.Error` e gli errori di snapshot/record producono l'esito
  `3 - Non classificato`; un normale `Reject` resta `0 - NOK`;
- validazione delle uscite saltata dopo un errore VisionPro, evitando classificazioni basate su
  valori vecchi;
- contatore e statistica dedicati `Unclassified`, visibili in HMI e Data Analysis;
- aggiornamenti contatori/indicatori accodati correttamente sul Dispatcher e backup contatori
  aggregato in background;
- salvataggio diagnostico forzato delle immagini annotate e grezze di tutte le viste attive;
- Data Inspector mostra il codice 3 con stato dedicato e conserva il dettaglio dell'errore.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione dei file ricetta o `Config.xml`

### Database notes

- nessuna nuova tabella o colonna;
- nuova riga globale additiva `UNCLASSIFIED` nella tabella contatori;
- la colonna esistente `tblgenerale.Esito_Classificazione` usa il valore `3` per gli errori di
  elaborazione.

### Operator / maintainer notes

- eseguire la procedura `vision-runtime-timing-and-unclassified-2026-09-02.md` prima della messa
  in produzione;
- verificare su almeno 100 prodotti reali i valori `target/actual/late` separati per camera;
- il worker dedicato migliora la regolarita' del polling, ma la garanzia real-time richiede un
  compare encoder hardware;
- un pezzo `Non classificato` viene scartato e le sue immagini vengono conservate anche quando la
  percentuale normale di salvataggio non lo avrebbe selezionato.

## Version 3.1.2.0

### Release metadata

- release date: `2026-09-02`

### Release intent

Eliminare le principali cause software di trigger shiftato, risultati camera associati al prodotto
successivo e aggiornamenti HMI ritardati durante il salvataggio immagini.

### Main functional integrations included

- worker dedicato `AboveNormal` per l'esecuzione dei punti intervento, separato dal polling encoder;
- scrittura PCIE-1756 limitata al port byte interessato e diagnostica delle chiamate lente;
- ticket trigger con `ProductId` e pairing Top/companion per identita' prodotto;
- scarto esplicito di risultati duplicati o obsoleti;
- display a priorita' bassa con protezione contro completion fuori ordine;
- cattura immagini da record immutabile tramite coda bounded e worker fuori dal thread UI.

### Configuration-file notes

- nessuna nuova chiave in `Config.xml`, `machine_runtime_config.xml` o ricetta;
- quote, mapping I/O, pulse e offset restano invariati e compatibili.

### Database notes

- nessuna tabella, colonna o migrazione nuova.

### Operator / maintainer notes

- eseguire almeno 100 passaggi hardware verificando `topProduct`/`companionProducts` nei log;
- investigare `INTERVENTION_DISPATCH_DELAY`, `IO_OUTPUT_WRITE_SLOW`,
  `VISION_RESULT_UNSOLICITED_DROPPED` e `IMAGE_CAPTURE_QUEUE_FULL` se presenti;
- `_Z.jpg` e' salvato dal record catturato senza render overlay durante la produzione continua;
- resta necessario il compare encoder hardware se il jitter residuo non e' compatibile con il
  processo.

## Version 3.1.5.1

### Release metadata

- release date: `2026-09-03`

### Release intent

Rendere camera, matrice vista x ispezione e tassonomia Classify realmente
specifiche per ricetta, mantenendo globali mapping fisici e default macchina.

### Main functional integrations included

- nuovo nodo ricetta opzionale `inspectionViewConfiguration`;
- `CameraTriggers` usato come unica sorgente per abilitazione camera;
- camere disabilitate escluse da trigger, pairing, code, watchdog e display;
- matrice strict per ricette salvate e fallback macchina per XML legacy;
- policy Classify e reset separati per ricetta;
- conservazione dell'ultimo snapshot valido in caso di errore DB;
- confronto minimo area sigillatura Side/Rear corretto;
- code NLog asincrone separate e limitate.

### Configuration-file notes

- nessuna modifica a `Config.xml` o `machine_runtime_config.xml`;
- il file ricetta puo' contenere il nuovo nodo opzionale;
- mapping board/channel/polarita' e quote macchina restano invariati.

### Database notes

- nessuna migrazione richiesta;
- `cfg_inspection_view` e `cfg_view_classification` restano default macchina.

### Operator / maintainer notes

- configurare e salvare ogni ricetta dalla pagina `Inspection Configuration`;
- spuntare soltanto controlli realmente pubblicati dal ToolBlock della vista;
- collaudare il cambio fra ricette con insiemi camera differenti a macchina ferma;
- usare `Reset Default` solo per tornare consapevolmente al fallback macchina.

## Version 3.1.5.2

### Release metadata

- release date: `2026-09-10`

### Release intent

Ripristinare il salvataggio delle immagini annotate dei pezzi NOK e non classificati senza
reintrodurre attese nel percorso che elabora il risultato di ispezione.

### Main functional integrations included

- corretta l'inizializzazione ActiveX del `CogRecordDisplay` dedicato alla cattura off-screen;
- eliminata `AxHost.InvalidActiveXStateException` causata dall'assegnazione di `AutoFit` prima
  di `BeginInit()`;
- rendering annotato spostato nella coda immagini bounded e disaccoppiato dal thread
  d'ispezione;
- il renderer usa l'esatto record catturato per il pezzo, non il contenuto corrente del display
  operatore;
- fallback raw mantenuto se Cognex non renderizza entro il timeout;
- aggiunta diagnostica di inizializzazione display e tipo completo degli errori di cattura.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione; i file esistenti restano compatibili.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- al primo NOK verificare `SAVE_IMAGE_CAPTURE_DISPLAY_READY` una sola volta;
- per ogni vista annotata verificare `SAVE_IMAGE_OK` con
  `annotated_source=display-record-graphics`;
- aprire `_Z.jpg` e confermare la presenza degli overlay VisionPro; `_A.bmp` deve restare raw;
- `SAVE_IMAGE_ANNOTATED_FAILED` o `SAVE_IMAGE_ANNOTATED_TIMEOUT` indicano fallback raw e non
  devono arrestare la produzione.

## Version 3.1.5.3

### Release metadata

- release date: `2026-09-11`

### Release intent

Chiudere la corsa fra aggiornamento display e salvataggio annotato e rendere la
soglia di confidenza Classify una policy per vista realmente tracciabile.

### Main functional integrations included

- rendezvous asincrono fra record radice, display VisionPro e worker immagini;
- cattura annotata con `PrintWindow` e fallback `CreateContentBitmap`;
- timeout condiviso da 1500 ms e fallback raw senza bloccare il ciclo;
- soglia minima Classify per vista in ricetta e default macchina;
- stato visuale ambra e classe archiviata `Unclassify` sotto soglia, mantenendo
  lo score misurato;
- esito AI vista `0` sotto soglia; codice `3` riservato agli errori di elaborazione.

### Configuration-file notes

- nessuna nuova chiave `Config.xml` o `machine_runtime_config.xml`;
- il nodo ricetta `inspectionViewConfiguration/View` puo' contenere
  `MinimumClassificationScore`; `0` mantiene il comportamento storico.

### Database notes

- `cfg_view_classification.MinimumScore` viene aggiunta in modo idempotente con
  default `0`; nessuna altra tabella o colonna nuova;
- le colonne label/score e `NC_<Vista>AiClassification` gia' esistenti vengono
  valorizzate secondo la policy per vista.

### Operator / maintainer notes

- verificare un campione sopra soglia e uno sotto soglia per ogni vista AI;
- sotto soglia attendersi `Unclassify`, score originale, pezzo NoGood e codice vista `0`;
- per i NOK verificare `_Z.jpg` annotata e `_A.bmp` raw dello stesso prodotto;
- `SAVE_IMAGE_ANNOTATED_TIMEOUT` indica fallback diagnostico, non arresto produzione.

## Version 3.1.5.4

### Release metadata

- release date: `2026-09-14`

### Release intent

Completare la localizzazione delle nuove funzioni e distribuire una baseline
installabile coerente con le protezioni delle macchine gia' configurate.

### Main functional integrations included

- tutte le label della matrice ispezioni, camera mode, classi e soglia AI usano
  `ServerMessagePersonalize`;
- nomi e descrizioni delle feature dinamiche vengono localizzati senza cambiare
  i codici tecnici persistiti;
- i cataloghi runtime personalizzati restano autorevoli; le sole chiavi mancanti
  sono completate in memoria dal catalogo `Localization` incluso nell'app;
- `PrintWindow` verifica handle/esito, rilascia sempre HDC e usa fallback Cognex;
- la cattura rifiuta un display gia' passato al record successivo;
- media full e update-only rigenerati come `3.1.5.4-r18`.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione dei file macchina;
- i cataloghi `C:\QtisVision\Language` non vengono sovrascritti dall'updater.

### Database notes

- nessuna nuova modifica schema rispetto alla release `3.1.5.3`.

### Operator / maintainer notes

- verificare `Inspection Configuration` sia in italiano sia in inglese;
- dopo update, confermare che eventuali testi personalizzati del sito restino invariati;
- eseguire il collaudo AI e immagini annotate descritto nelle guide dedicate.

## Version 3.1.5.5

### Release metadata

- release date: `2026-09-24`

### Release intent

Ridurre i ritardi sporadici dei trigger Top/Rear/Side separando il percorso encoder
critico dalla telemetria e rendendo deterministica la durata degli impulsi camera.

### Main functional integrations included

- il polling Advantech copia i campioni in ring buffer fissi e non esegue callback applicative;
- tracking e MultiShot usano un worker `Highest`, mentre la UI usa un worker separato;
- `MachineController` evita raccolte e LINQ quando nessun punto viene raggiunto;
- gli impulsi camera usano un worker I/O dedicato, coda fissa e deadline monotona;
- gli impulsi VirtualConveyor e TimedFromPhotocell non usano piu `Task.Delay`;
- aggiunto il contratto per validare e armare la FIFO position-compare PCIE-1884 dopo
  verifica di cablaggio e collaudo.

### Configuration-file notes

- nessuna nuova chiave e nessuna migrazione di `Config.xml`, ricette o
  `machine_runtime_config.xml`;
- il compare PCIE-1884 non viene armato automaticamente.

### Database notes

- nessuna modifica a tabelle, colonne o migrazioni MySQL.

### Operator / maintainer notes

- ripetere almeno 100 prodotti con Top/Rear/Side e conservare i log timing;
- verificare assenza di `ENCODER_DISPATCH_QUEUE_OVERFLOW`;
- attendersi trigger normalmente entro 12 count e impulsi da 40 ms entro circa 2 ms;
- prima del compare hardware verificare COUT/DO in DAQNavi, cablaggio, polarita e
  ampiezza impulso con oscilloscopio.


## Version 3.1.5.6

### Release metadata

- release date: `2026-09-28`

### Release intent

Separare i ruoli fisici delle camere e impedire che un comando di scarto parziale venga dichiarato eseguito.

### Main functional integrations included

- Le code e i ticket di correlazione usano Left e Right come camere fisiche; i nomi VPP storici Side e Rear restano accettati in ingresso.
- `UserResultTag` di VisionPro viene registrato e i pacchetti con tag gia' visto per la stessa camera vengono scartati senza consumare il ticket successivo.
- Lo scarto OPEN/CLOSE distingue assenza del fronte, impulso incompleto e impulso completato. Un prodotto con azionamento parziale non viene marcato come scartato ne' azionato automaticamente una seconda volta.
- Il worker I/O richiede che il driver BDaq accetti una nuova scrittura LOW prima di riutilizzare un canale dopo una discesa fallita; il livello elettrico va verificato sulla macchina.
- La ricetta espone ritardi hardware distinti per Top, Left, Right e Bottom; Front resta supportata nelle macchine che la usano.

### Configuration-file notes

- Nessuna nuova chiave `Config.xml` o modifica del mapping macchina. In XML ricetta, `LeftCameraTriggerDelay`, `RightCameraTriggerDelay` e `BottomCameraTriggerDelay` sono facoltativi; il vecchio `SideCameraTriggerDelay` resta il fallback di Left.
- I vecchi campi ricetta Side/Rear per offset, trigger e matrice ispezioni vengono letti; una modifica dalla pagina ricetta scrive il campo Left/Right corrispondente.

### Database notes

- Nessuna migrazione DB. Le colonne Side/Rear esistenti restano il formato di persistenza per compatibilita'.

### Operator / maintainer notes

- Build x64 Release e test isolati di alias ricetta e deduplicazione ticket passati. Collaudare in macchina la sequenza OPEN/CLOSE e verificare che ogni prodotto abbia un risultato per Top, Left e Right (Bottom se installata).
- I 15 risultati Rear extra del log del 25 settembre non provano tag duplicati: se i nuovi `UserResultTag` risultano diversi, controllare acquisizione VPP, trigger hardware e cablaggio Right. Il solo software non puo' identificare con certezza il prodotto di un frame extra senza un identificatore di acquisizione condiviso.


## Version 3.1.5.7

### Release metadata

- release date: `2026-09-28`

### Release intent

Revisione delle configurazioni per camera e dell'attuatore di scarto.

### Main functional integrations included

- TOP, SIDE e REAR mantengono il nome dei job VPP nelle abilitazioni ispezione e nei menu ONNX, con profili indipendenti per camera; aggiunto il selettore rapido dell'uscita trigger REAR nella pagina I/O.
- Scarto a soffio e monostabile: impulso singolo con durata configurata e ritorno LOW. Bistabile bloccato finche' non saranno presenti i sensori OPEN/CLOSE; nessun comando OPEN/CLOSE automatico.

### Configuration-file notes

- Nuovo campo facoltativo `RejectActuatorType` in `machine_runtime_config.xml`. Se manca e sono assegnate entrambe le uscite OPEN/CLOSE, il tipo viene considerato `Bistable` e lo scarto resta bloccato. Negli altri casi il default e' `Monostable`.
- Nessuna modifica a `Config.xml` o alle tabelle MySQL.

### Operator / maintainer notes

- Verificare sulla macchina polarita', durata del soffio o dell'impulso monostabile e ritorno LOW. Le posizioni del bistabile non sono ancora sorvegliate.


## Version 3.1.5.8

### Release metadata

- release date: `2026-09-29`

### Release intent

Separare RIGHT e REAR su un VPP a quattro camere fisiche senza cambiare automaticamente le uscite cablate delle installazioni precedenti.

### Main functional integrations included

- Identita' RIGHT e REAR distinte in VPP, code, correlazione, validazione, UI e profili ricetta.
- Trigger RIGHT dedicato; profilo MultiShot REAR separato e controllo collisione ruolo/uscita.
- Risultati, classificatori ONNX, immagini e campioni AI distinti per RIGHT e REAR.

### Configuration-file notes

- `Config.xml`: chiave opzionale `RightCameraSerial`; nessun path nuovo imposto.
- XML macchina: profilo MultiShot `Rear`; il vecchio `Right` mantiene il proprio ruolo e canale salvati.
- XML ricetta: delay, soglia sigillatura e correzione MultiShot REAR opzionali; vecchie ricette leggibili.

### Database notes

- `tblgenerale`: tre colonne nullable aggiunte al primo avvio, `RightClassificationLabel`, `RightClassificationScore`, `NC_RightAiClassification`. Colonne REAR conservate.

### Operator / maintainer notes

- Assegnare un DO distinto a `OUT_CAMERA_RIGHT_TRIGGER` e verificare la quota di `CAMERA_TRIGGER_RIGHT` prima di abilitarlo. Collaudare le quattro viste con risultati indipendenti.
- Procedura dettagliata: `four-camera-independent-roles-2026-09-29.md`.

## Version 3.1.5.9

### Release metadata

- release date: `2026-09-29`

### Release intent

Incremental release generated after a software update on 2026-09-29.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 3.1.6.0

### Release metadata

- release date: `2026-09-29`

### Release intent

Incremental release generated after a software update on 2026-09-29.

### Main functional integrations included

- update summary to be completed with the functional changes of this release

### Configuration-file notes

- document here any new or changed Config.xml keys introduced by this release

### Database notes

- document here any new or changed MySQL tables, columns or migration checks introduced by this release

### Operator / maintainer notes

- document here runtime-facing notes for operators, maintainers and testers

## Version 3.1.6.1

### Release metadata

- release date: `2026-10-01`

### Release intent

Camera trigger outputs follow the physical camera roles loaded from the active VPP.

### Main functional integrations included

- After VPP load, automatic camera intervention points and MultiShot profiles for absent roles are masked in the effective runtime configuration. SIDE maps to LEFT; RIGHT and REAR remain independent.
- Timed triggers and queued camera pulses check the active role again before writing a DO. Recipe reload clears the active role set and cancels pending timed batches.
- Conflicting point/output roles are masked and logged. The saved machine I/O mapping is preserved.

### Configuration-file notes

- No `Config.xml`, machine XML or recipe XML migration. Runtime masking is transient and recalculated for each VPP.

### Database notes

- No MySQL schema changes.

### Operator / maintainer notes

- Build Release x64 and in-memory role checks passed. Verify job roles and pulses on the actual machine before production use.
- Different cameras require distinct physical DO channels: software cannot isolate cameras sharing one wired output. Manual I/O diagnostics remain available.
## Legacy Baseline 1.0.0.0

### Note

This version existed before a formal archive was maintained.

Treat it as the historical pre-archive baseline:

- main HMI structure already active
- no formal version archive
- no explicit software-information popup
- partial or evolving integrations later consolidated in `1.1.0.0`

## Procedure For Future Versions

When releasing a new official version:

1. Update `Properties\AssemblyInfo.cs`
   - `AssemblyVersion`
   - `AssemblyFileVersion`
   - `AssemblyInformationalVersion`
2. Add a new section in this archive.
3. Update:
   - `Documentation\MachineHardware\local-baseline-change-log-<date>.md`
   - `README.md` if the user-facing flow changes
4. If new strings are introduced:
   - update `ServerMessage\ServerMessageStructure.cs`
   - update `scripts\UpdateRuntimeLanguageFiles.ps1`
   - resync runtime language files
5. If new config sections or DB tables are added:
   - document them here
   - keep backward compatibility where possible
6. Mirror the canonical files to the aligned colleague repo so both work on the same mature baseline.
