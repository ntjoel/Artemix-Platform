# AGENTS.md

## Progetto

`QtisVisionPanel` e' il progetto principale HMI / visione della macchina.

Questa repo deve essere trattata come baseline di produzione matura.
Non e' una sandbox libera: ogni modifica deve preservare la stabilita' del runtime macchina e la compatibilita' con ricette, file XML, DB e flusso operativo.

## Regola di baseline di questa repo

La baseline di riferimento e' quella del progetto corrente:

- struttura del main project attuale
- logica runtime attuale
- comportamento guidato da `Config.xml`
- nessuna standardizzazione forzata di cartelle o path fuori dal contratto del file di configurazione

Questo significa:

- non introdurre path hardcoded al posto di quelli letti dal config
- non creare automaticamente nuove cartelle come vincolo architetturale se il runtime attuale non lo richiede
- non spostare il confine tra macchina, ricetta e runtime solo per comodita'

## Obiettivo del prodotto

Costruire e mantenere un sistema visione industriale stabile, mantenibile e tracciabile che gestisca:

- caricamento ricette
- acquisizione camere
- parametri ispezione
- esiti visione
- ciclo macchina reale
- diagnostica locale
- persistenza DB e log
- recovery dei file di configurazione

## Regola fondamentale: macchina != ricetta != runtime

Questa distinzione resta obbligatoria.

### Deve restare macchina

- mapping I/O
- board / channel / polarita'
- encoder fisico
- counts/mm
- zero macchina
- quota fotocellula macchina
- quote standard eventi
- trigger / reject defaults macchina

### Deve stare in ricetta

- parametri visione
- tolleranze prodotto
- dimensioni prodotto
- trigger delay camera
- abilitazioni difetti
- eventuali offset prodotto-specifici

### Deve essere solo runtime

- stato pannello
- stato macchina
- prodotti tracciati
- risultati ispezione
- eventi operativi
- UI state

## Linea guida di fusione

Questa repo assorbe miglioramenti maturi da altri rami o companion, ma con questa priorita':

1. mantenere la logica runtime del progetto corrente
2. integrare solo i miglioramenti davvero piu' maturi
3. adattare i miglioramenti al contratto locale, non il contrario

Esempi:

- il `DataInspector` nativo puo' essere portato dentro la HMI
- il fix DB su `TimeFine` puo' essere adottato
- la documentazione puo' essere elevata al livello del collega
- ma il runtime continua a seguire il `Config.xml` del progetto corrente

## Principi guida

1. Non rompere il flusso ricette esistente senza motivo forte.
2. Non spostare logica macchina complessa dentro le view.
3. Non duplicare configurazioni macchina dentro il file ricetta.
4. Introdurre nuovi modelli in modo compatibile con XML e database esistenti.
5. Favorire estensioni progressive, non refactor gratuiti.
6. Documentare sempre cosa cambia per sviluppatori, manutentori e collaudatori.

## Documentazione obbligatoria

Ogni modifica sostanziale deve lasciare traccia in almeno uno di questi livelli:

- AI-oriented:
  - `AGENTS.md`
  - note di fusione e baseline
- human-oriented:
  - manuali, note operative, schermate e uso lato pannello
- tecnica:
  - `Documentation\MachineHardware`
  - note DB, XML, build, runtime, integrazione

## Regola versioning e release

Ogni aggiornamento software che modifica comportamento, UI, integrazioni, config, DB o runtime deve generare una nuova release incrementale.

Baseline corrente:

- versione ufficiale archiviate nel popup software e in `Documentation\MachineHardware\software-version-archive.md`

Regola pratica:

1. incrementare la versione assembly
2. aggiornare l'archivio versioni
3. descrivere eventuali impatti su:
   - `Config.xml`
   - tabelle / colonne MySQL
   - procedura operativa
4. riallineare la repo del collega se il file fa parte della baseline canonica

## Regola sincronizzazione OneDrive

La cartella condivisa OneDrive e' una baseline operativa, non una copia facoltativa.

Ogni modifica locale fatta in questa repo deve essere trasferita nella cartella condivisa prima di chiudere il lavoro:

- sorgente locale: `D:\Pulsar\Developer\NewPanel\QtisVisionPanel`
- destinazione condivisa: `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\lastRelease\QtisVisionPanel`

Comando obbligatorio a fine modifica:

- `powershell -ExecutionPolicy Bypass -File .\scripts\SyncSharedBaseline.ps1 -Apply -VerifyCopiedFiles`

Note:

- non usare `-Mirror` come default; la modalita' normale copia/aggiorna senza cancellare backup storici presenti nella cartella condivisa
- gli output build/runtime esclusi (`bin`, `obj`, `.git`, `.vs`, log, dll, exe, cache) restano fuori dal sync sorgente
- se il comando fallisce, il lavoro non e' chiuso

Script canonico:

- `scripts\IncrementSoftwareRelease.ps1`

Default:

- incremento dell'ultimo segmento versione (`Revision`)
- rollover decimale dei segmenti:
  - `1.1.0.9 -> 1.1.1.0`
  - `1.1.9.9 -> 1.2.0.0`

## Read First

Leggere prima di modificare aree sensibili:

- `README.md`
- `TASK_REQUEST_TEMPLATE.md`
- `Documentation/MachineHardware/README.md`
- `Documentation/MachineHardware/current-unified-baseline-2026-03-27.md`
- `Documentation/MachineHardware/repo-alignment-execution-plan-2026-03-27.md`
- `Documentation/MachineHardware/developer-and-tester-onboarding-2026-03-27.md`

## Aree sensibili

Prestare attenzione speciale a:

- `MainWindow.xaml.cs`
- `RecipeManagerViewModel.cs`
- `Cls_Config\*`
- `Database\Cls_InitializzeDb.cs`
- `ICognexJobManager.cs`
- `Services\MachineRuntimeService.cs`
- `Services\ConfigurationRecoveryService.cs`
- `Views\UserControls\DataInspectorView.xaml`
- `Inspector\*`

## Definition of Ready

Una modifica e' pronta per essere sviluppata solo se:

- e' chiaro se tocca macchina, ricetta o runtime
- e' chiaro l'impatto su XML, DB, config o solo memoria runtime
- e' chiaro se la modifica segue la baseline corrente o richiede migrazione
- e' stato definito se servono aggiornamenti documentali

## Definition of Done

Una modifica e' chiusa bene quando:

- non rompe il caricamento ricette esistenti
- non rompe i file config esistenti
- non rompe il DB attuale
- il codice resta leggibile e coerente
- la documentazione tecnica e operativa e' aggiornata se il flusso cambia
