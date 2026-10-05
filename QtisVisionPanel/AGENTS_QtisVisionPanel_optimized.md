# AGENTS.md — QtisVisionPanel

## Scopo

`QtisVisionPanel` è il progetto principale HMI / visione della macchina.

Trattare questa repository come baseline di produzione matura, non come sandbox. Ogni modifica deve preservare stabilità runtime, compatibilità con ricette, `Config.xml`, XML esistenti, database, logica macchina e flusso operativo.

## Priorità operative

In ordine:

1. Correttezza e sicurezza runtime macchina.
2. Compatibilità con baseline corrente.
3. Modifiche minime e localizzate.
4. Qualità codice e manutenibilità.
5. Uso efficiente di token, file e comandi.

Non sacrificare correttezza o sicurezza per risparmiare token.

---

## Regole anti-spreco token per Codex

- Leggere solo i file direttamente rilevanti alla richiesta.
- Non esplorare tutta la repo se non serve.
- Usare ricerche mirate prima di aprire file grandi.
- Non incollare file interi nella risposta finale.
- Non spiegare codice ovvio.
- Non produrre piani lunghi per task semplici.
- Non proporre refactor ampi se non richiesti.
- Non modificare file non necessari.
- Non riformattare codice non toccato dalla modifica.
- Non generare boilerplate, wrapper, astrazioni o test non necessari.
- Prima di intervenire, identificare la modifica minima sicura.
- Se manca contesto, ispezionare il codice esistente invece di ipotizzare.
- Se il task è piccolo, lavorare direttamente e poi dare un riepilogo breve.
- Se il task è rischioso, dare un piano massimo di 3-5 punti.

Output finale consigliato:

1. Cosa è cambiato.
2. File modificati.
3. Test/check eseguiti.
4. Rischi o note.

Risposta breve, tecnica, senza ripetere tutto il ragionamento.

---

## Baseline della repo

La baseline di riferimento è il progetto corrente:

- struttura attuale del main project;
- logica runtime attuale;
- comportamento guidato da `Config.xml`;
- nessuna standardizzazione forzata di cartelle, path o contratti fuori da ciò che il runtime usa già.

Regole:

- Non introdurre path hardcoded al posto dei path letti da config.
- Non creare nuove cartelle come vincolo architetturale se il runtime attuale non lo richiede.
- Non spostare confini tra macchina, ricetta e runtime per comodità.
- Non cambiare contratti XML, DB, ricette o config senza motivo forte e compatibilità chiara.

---

## Obiettivo prodotto

Mantenere un sistema visione industriale stabile, mantenibile e tracciabile per:

- caricamento ricette;
- acquisizione camere;
- parametri ispezione;
- esiti visione;
- ciclo macchina reale;
- diagnostica locale;
- persistenza DB e log;
- recovery file di configurazione.

---

## Regola fondamentale: macchina != ricetta != runtime

Questa distinzione è obbligatoria.

### Macchina

Restano dati macchina:

- mapping I/O;
- board, channel, polarità;
- encoder fisico;
- counts/mm;
- zero macchina;
- quota fotocellula macchina;
- quote standard eventi;
- trigger / reject default macchina.

### Ricetta

Restano dati ricetta:

- parametri visione;
- tolleranze prodotto;
- dimensioni prodotto;
- trigger delay camera;
- abilitazioni difetti;
- offset prodotto-specifici.

### Runtime

Restano dati runtime:

- stato pannello;
- stato macchina;
- prodotti tracciati;
- risultati ispezione;
- eventi operativi;
- UI state.

Non duplicare configurazioni macchina dentro la ricetta. Non spostare logica macchina complessa dentro le view.

---

## Integrazione da altri rami o companion

La repo può assorbire miglioramenti maturi, ma con questa priorità:

1. mantenere la logica runtime corrente;
2. integrare solo miglioramenti davvero maturi;
3. adattare i miglioramenti al contratto locale, non il contrario.

Esempi ammessi se compatibili:

- portare il `DataInspector` nativo dentro la HMI;
- adottare il fix DB su `TimeFine`;
- migliorare documentazione tecnica e operativa.

Il runtime deve continuare a seguire il `Config.xml` del progetto corrente.

---

## Regole di sviluppo

- Mantenere modifiche piccole, progressive e reversibili.
- Riutilizzare pattern, naming, servizi e convenzioni già presenti.
- Evitare nuove dipendenze se non strettamente necessarie.
- Non cambiare API pubbliche, schema DB, XML, ricette o configurazioni senza indicare impatto e compatibilità.
- Gestire in modo esplicito errori, null, timeout, dati mancanti e recovery.
- Non bypassare controlli, interlock, validazioni, log, diagnostica o allarmi esistenti.
- Preservare compatibilità con ricette e config già esistenti.
- Documentare solo quando il cambio è sostanziale o cambia il flusso operativo/tecnico.

---

## Read First

Prima di modificare aree sensibili, leggere solo i documenti necessari tra:

- `README.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation/MachineHardware/README.md`
- `Documentation/MachineHardware/current-unified-baseline-2026-03-27.md`
- `Documentation/MachineHardware/repo-alignment-execution-plan-2026-03-27.md`
- `Documentation/MachineHardware/developer-and-tester-onboarding-2026-03-27.md`

Non aprire tutti questi file automaticamente per task piccoli. Usarli quando la modifica tocca macchina, ricette, XML, DB, recovery, runtime o flusso operativo.

---

## Aree sensibili

Prestare attenzione speciale a:

- `MainWindow.xaml.cs`
- `RecipeManagerViewModel.cs`
- `Cls_Config/*`
- `Database/Cls_InitializzeDb.cs`
- `ICognexJobManager.cs`
- `Services/MachineRuntimeService.cs`
- `Services/ConfigurationRecoveryService.cs`
- `Views/UserControls/DataInspectorView.xaml`
- `Inspector/*`

Per queste aree:

- leggere il contesto minimo necessario;
- evitare refactor gratuiti;
- preservare comportamento esistente;
- indicare impatto su macchina, ricetta, runtime, XML, DB o UI.

---

## Definition of Ready

Una modifica è pronta quando è chiaro:

- se tocca macchina, ricetta o runtime;
- se impatta XML, DB, config o solo memoria runtime;
- se segue la baseline corrente o richiede migrazione;
- se servono aggiornamenti documentali.

Se questi punti non sono chiari, Codex deve chiarirli ispezionando il codice o segnalarli brevemente.

---

## Definition of Done

Una modifica è chiusa bene quando:

- non rompe caricamento ricette esistenti;
- non rompe file config esistenti;
- non rompe DB attuale;
- non cambia contratti runtime non richiesti;
- il codice resta leggibile e coerente;
- i check minimi rilevanti sono stati eseguiti o dichiarati come non eseguibili;
- la documentazione è aggiornata solo se il flusso tecnico/operativo cambia.

---

## Stile risposta finale

Usare formato breve:

```text
Modificato:
- ...

File:
- ...

Check:
- ...

Note:
- ...
```

Non includere lunghi diff, file interi o spiegazioni teoriche se non richiesto.
