# Piano di miglioramento industriale - QtisVisionPanel

Ultima revisione: 2026-09-02

Baseline di partenza: release 3.0.7.7, audit completo descritto in
`MachineHardware/full-application-audit-2026-07-21.md`.

## 1. Obiettivo

Portare la baseline verso esercizio industriale 24/7 con interventi misurabili e reversibili,
senza riscrivere il runtime che oggi funziona in macchina.

Ogni pacchetto deve:

- dichiarare se modifica macchina, ricetta o solo runtime;
- avere test di caratterizzazione prima del refactor;
- avere rollback tramite commit/tag e backup configurazione;
- passare build Release x64, smoke test e test hardware quando tocca SDK/I/O/VisionPro;
- aggiornare manuale, changelog e archivio release;
- essere copiato e verificato nella baseline OneDrive.

## 2. Stato consolidato

Legenda: `[x]` completato, `[~]` parziale, `[ ]` aperto.

| Area | Stato | Risultato corrente |
|---|---|---|
| Config macchina atomica | [x] | temp + replace + lock; recovery mantenuto |
| Inferenza ONNX fuori dal percorso caldo | [x] | advisory, single-flight, bounded e disabilitabile |
| Code immagini/eventi bounded | [x] | drop consentito solo per dati best-effort, con log |
| Pairing multi-camera per prodotto | [x] | ticket trigger `ProductId`; fallback compatibile per synthetic/external |
| Rendering fuori dal percorso esito | [x] | display anti-stale e cattura record immagini su worker bounded |
| Async command piu' rischiosi | [x] | wrapper `Task` + `SafeFireAndForget` |
| `GC.Collect` nel ciclo ricetta | [x] | resta solo lo shutdown deliberato |
| Timeout lock config e init DB | [x] | presenti sulla baseline corrente |
| Contatore fittizio al load ricetta | [x] | rimosso nell'audit 2026-07-21 |
| Codice morto e manuale duplicato | [x] | 2.048 righe nette rimosse; 1.894 righe documentali archiviate |
| Lifecycle Cognex senza `Thread.Sleep` | [~] | tre attese residue richiedono prova hardware |
| Code risultati ispezione bounded | [ ] | non applicare drop silenzioso |
| Test automatici | [ ] | nessun progetto test presente |
| Riduzione God-class | [~] | `InspectionOrchestrator` estratto; molto resta aperto |
| Audit `catch` vuoti | [~] | inventario eseguito, classificazione ancora aperta |

## 3. Priorita' P0 - Protezione regressioni

Orizzonte: 1-2 settimane. Rischio implementativo basso.

### P0.1 Progetto test di caratterizzazione

**Scopo:** bloccare il comportamento corrente prima di estrarre altro codice.

**Azioni:**

1. creare `QtisVisionPanel.Tests` senza dipendenza da hardware;
2. testare `RecipeMachineRuntimeResolver`, normalizzazione config, alias camere, clamp MultiShot,
   autorizzazioni e mapping difetti;
3. testare il contratto contatori: load ricetta = 0 prodotti, GOOD = un incremento, NOK = un
   incremento prodotto e uno per difetto presente;
4. aggiungere fixture XML storiche per verificare retrocompatibilita';
5. aggiungere smoke test della composizione servizi senza aprire VisionPro.

**Criterio uscita:** test verdi in locale e in CI; nessun accesso a schede/DB reale.

**Rollback:** rimozione del solo progetto test, nessun impatto runtime.

### P0.2 Warning compiler e dipendenze

**Scopo:** rendere il warning budget controllato.

**Azioni:**

1. portare i 22 `CS1998` a metodi sincroni o `Task.CompletedTask` solo quando il contratto lo
   consente;
2. creare issue separata per le 16 API obsolete Advantech/OPC UA;
3. risolvere il conflitto assembly `System.Memory`/dipendenze NuGet con una matrice di versioni
   compatibile con VisionPro;
4. introdurre una baseline warning: nessun nuovo warning ammesso.

**Criterio uscita:** 0 nuovi warning; elenco residuo approvato e documentato.

### P0.3 Classificazione `catch` vuoti

**Scopo:** evitare errori invisibili senza trasformare i cleanup best-effort in allarmi continui.

**Azioni:**

1. classificare i 94 blocchi: cleanup/dispose, probing SDK, fallback UI, percorso produttivo;
2. nei percorsi produttivi aggiungere log strutturato throttled e contatore diagnostico;
3. nei cleanup catturare almeno `Exception ex` e scrivere DEBUG/WARN quando utile;
4. lasciare vuoto solo il dispose realmente idempotente, con commento del motivo.

**Criterio uscita:** nessun `catch { }` nel percorso decisione/trigger/scarto/DB autorevole.

### P0.4 Retention e timeout database

**Scopo:** impedire crescita illimitata o blocchi lunghi quando MySQL degrada.

**Azioni:**

1. verificare `event_scheduler` e retention di `tblgenerale` all'avvio;
2. eseguire cleanup applicativo a batch come fallback, con limite temporale;
3. usare un'unica factory connection string con connect/command timeout;
4. esporre in diagnostica retention attiva, ultima pulizia, righe eliminate e spazio DB.

**Criterio uscita:** test con `event_scheduler=OFF`, DB offline e DB lento; ciclo visione non
bloccato.

## 4. Priorita' P1 - Determinismo del ciclo

Orizzonte: 2-5 settimane. Richiede banco prova macchina.

### P1.1 Code risultati ispezione con back-pressure

**Problema:** TOP/SIDE/FRONT/REAR/BOTTOM usano `ConcurrentQueue<CameraResult>` senza capienza.

**Soluzione proposta:**

- assegnare ID prodotto/ciclo e capienza coerente con il massimo WIP fisico;
- quando la soglia warning e' superata, emettere `INSPECTION_QUEUE_PRESSURE`;
- a capienza massima non usare `drop-oldest`: mettere la macchina in stato controllato,
  finalizzare il prodotto come esito non affidabile o fermare l'accettazione di nuovi trigger
  secondo la safety policy approvata;
- esporre depth, oldest age, enqueue/dequeue e overflow in Diagnostics;
- svuotare le code solo su transizione ricetta/stop con audit del motivo.

**Criterio uscita:** soak con camera companion lenta; zero pairing cross-prodotto e zero esiti
persi senza evento.

### P1.2 Lifecycle VisionPro/Cognex

**Problema:** tre `Thread.Sleep` nel job manager e ownership delle risorse ancora complessa.

**Sequenza obbligatoria:**

1. test di caratterizzazione load/start/stop/unload/recovery;
2. inventario eventi Cognex registrati e relativa deregistrazione;
3. ownership esplicita per job, record, immagini, display e FIFO;
4. sostituire una attesa alla volta con timeout/cancellation compatibile SDK;
5. verificare Job Editor, Live Preview hardware trigger, MultiShot e recovery stall.

**Criterio uscita:** 500 cicli cambio ricetta e 72h RunContinuous senza crescita handle/memoria,
freeze o job rimasti registrati.

### P1.3 Profilazione per stadio

Misurare con istogrammi, non solo media:

- acquisizione;
- attesa companion/MultiShot;
- validazione toolblock;
- finalizzazione contatori;
- DB;
- salvataggio immagini;
- AI advisory;
- aggiornamento UI.

**Criterio uscita:** p50/p95/p99 disponibili in Diagnostics e log; DB, immagini e AI non
allungano il percorso decisionale.

### P1.4 Encoder e trigger deterministici

- conservare snapshot encoder sull'edge fotocellula;
- misurare `target`, `actual`, `late counts`, `late mm` per ogni punto;
- definire soglie per velocita' e distanza minima tra trigger;
- valutare latch/comparatore hardware quando il polling software non puo' rispettare la finestra;
- testare prodotti ravvicinati, wrap counter, reset scheda e restart applicazione.

**Criterio uscita:** distribuzione jitter approvata per velocita' massima macchina.

## 5. Priorita' P2 - Resilienza dati e sicurezza

Orizzonte: 3-6 settimane.

### P2.1 Persistenza produzione offline

- coda locale durevole per record DB non scritti;
- replay ordinato e idempotente con chiave prodotto/ciclo;
- limiti disco e allarme prima della saturazione;
- nessun blocco del ciclo VisionPro per indisponibilita' MySQL.

### P2.2 OPC UA industriale

- trust store gestito, auto-accept solo esplicitamente in commissioning;
- RBAC/policy per Start, Stop e cambio ricetta remoto;
- handshake, sequence e timeout gia' presenti da coprire con test integrazione Ignition;
- migrazione API sincrone obsolete a API async;
- audit completo di attore, endpoint, comando, valore, esito e correlation ID.

### P2.3 Credenziali e TLS

- nessuna password amministrativa di default in una release cliente;
- provisioning iniziale obbligatorio o credenziale per-sito;
- TLS MySQL quando DB non e' localhost/rete macchina isolata;
- segreti fuori dai log e rotazione documentata.

**Criterio uscita P2:** threat review firmata, test certificati OPC UA, DB offline/replay e nessun
segreto nei pacchetti/log.

## 6. Priorita' P3 - Riduzione architetturale incrementale

Orizzonte: 2-4 mesi. Mai big-bang.

### P3.1 Ridurre `DigitalIOViewModel`

Estrarre nell'ordine:

1. proiezione UI diagnostica encoder;
2. servizio commissioning/taratura;
3. repository/configurazione I/O;
4. scheduler punti intervento;
5. coordinatore profili MultiShot.

Il ViewModel deve restare compositore di comandi/proprieta' UI, non proprietario del ciclo fisico.

### P3.2 Ridurre `MainWindow`

Continuare dal lavoro gia' fatto con `InspectionOrchestrator`:

1. `RecipeLifecycleService`;
2. `VisionRuntimeCoordinator`;
3. `AlarmOutputCoordinator`;
4. `ShutdownCoordinator`;
5. `DisplaySessionCoordinator`.

Ogni estrazione deve conservare log/eventi e avere test di caratterizzazione.

### P3.3 Dipendenze esplicite

- introdurre interfacce ai confini, non per ogni classe;
- passare config/logger/repository via costruttore nei nuovi servizi;
- ridurre gradualmente `MainWindow.*` e `ServiceLocator`;
- mantenere un solo composition root.

### P3.4 Accesso dati

- una factory connessioni;
- repository senza SQL nei ViewModel;
- query parametrizzate e cancellation/timeout;
- migrazioni versionate e testate su schema storico.

## 7. Priorita' P4 - Qualifica 24/7

### Automazione build

- CI Release x64;
- test unitari e parser XAML/Markdown;
- warning budget;
- controllo version/archive/changelog;
- pacchetto deploy riproducibile.

### Soak test

Durata minima 72 ore con:

- flusso prodotti nominale e burst;
- cambio ricetta periodico;
- DB online/offline;
- OPC UA reconnect;
- immagini campionate;
- AI ON/OFF;
- recovery VisionPro controllato.

Metriche: working set, private bytes, handle, thread, GC pause, queue depth, p99 ciclo, errori DB,
recovery, prodotti senza esito.

### Watchdog esterno

Un watchdog di processo puo' riavviare l'HMI solo dopo che shutdown, uscite sicure, stato ricetta e
recovery dati sono deterministici. Non deve mascherare un leak o un deadlock non diagnosticato.

## 8. Metriche di accettazione

| Metrica | Obiettivo |
|---|---|
| Errori build Release x64 | 0 |
| Nuovi warning | 0 |
| Test config/ricetta/contatori | 100% verdi |
| Ispezioni perse senza evento | 0 |
| Incrementi contatore per prodotto | esattamente 1 finalizzazione |
| Freeze UI nel ciclo nominale | nessuno >100 ms attribuibile alla HMI |
| Memoria/handle soak 72h | plateau stabile, nessuna crescita monotona |
| DB offline | ciclo non bloccato, dati recuperabili |
| Cambio ricetta | nessun esito del prodotto precedente abbinato al nuovo |
| Trigger encoder | jitter entro specifica macchina documentata |

## 9. Ordine raccomandato

1. P0.1 test di caratterizzazione.
2. P0.2 warning/dependency baseline.
3. P0.3 catch vuoti.
4. P0.4 retention/timeout DB.
5. P1.1 code risultati con policy approvata.
6. P1.2 lifecycle Cognex su banco prova.
7. P1.3/P1.4 profiling e trigger.
8. P2 sicurezza e persistenza offline.
9. P3 estrazioni architetturali.
10. P4 qualifica 72h e release industriale.

Questo ordine evita di smontare le classi grandi prima di avere una rete di protezione misurabile.
