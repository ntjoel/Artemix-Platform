# Machine Hardware And Baseline Notes

Questa cartella contiene la documentazione tecnica della baseline macchina e applicazione.

Se stai aprendo il software come operatore, non partire da qui. Parti dalla guida:

- `Docs/Manual/00_Documentazione_Tecnica_Globale.md`

Se sei manutentore, sviluppatore, collaudatore o supporto tecnico, questa cartella serve per capire come e' costruita la baseline e quali regole non devono essere rotte.

## Cosa contiene questa cartella

- note architetturali;
- storico modifiche;
- baseline runtime;
- integrazione VisionPro;
- configurazione macchina;
- database e recovery;
- OPC UA e sistemi esterni;
- I/O, encoder, commissioning;
- note su lingua, build e rilascio.

## Documenti live da aggiornare sempre

- `architecture-and-modules.md`: architettura corrente, flussi principali, issue aperte e piano tecnico.
- `full-application-audit-2026-07-21.md`: inventario corrente, pulizia codice, warning, rischi
  residui e checklist di collaudo.
- `../improvement-plan-2026-07.md`: piano industriale prioritizzato con criteri di accettazione e
  rollback.
- `code-changes-log.md`: log cronologico delle modifiche a codice, UI, config, DB e documentazione.
- `software-version-archive.md`: archivio release software visibile anche nel popup versione.
- `counter-defect-single-source-2026-07-01.md`: flusso autorevole dei contatori produzione e difetto dopo la correzione doppio incremento.
- `new-implementations-test-guide-2026-07-10.md`: guida di collaudo per le nuove implementazioni AI/preallarmi, Top3D e Fase 0 stabilita'.
- `recipe-camera-and-multishot-offsets-2026-07-14.md`: separazione tra quote globali macchina e correzioni camera/MultiShot specifiche della ricetta.
- `machine-output-recipe-trigger-and-tracking-commissioning-2026-09-01.md`: mapping globale delle uscite dall'Intervention Point Editor, abilitazione trigger per ricetta, verifica read-back e collaudo della precisione encoder-trigger.
- `vision-runtime-timing-and-unclassified-2026-09-02.md`: collaudo del worker encoder dedicato,
  latenza risultati, esito `3 - Non classificato`, statistiche e immagini diagnostiche per tutte
  le viste attive.
- `vision-trigger-result-correlation-and-ui-decoupling-2026-09-02.md`: separazione tra polling e
  scritture DO, ticket `ProductId` camera, protezione dai risultati obsoleti, display anti-stale e
  salvataggio immagini fuori dal Dispatcher WPF.
- `encoder-trigger-deterministic-workers-and-pcie1884-compare-2026-09-24.md`: ring buffer encoder
  critico/telemetria, scheduler impulsi senza `Task.Delay`, risultati del test a tre camere e
  predisposizione della FIFO compare hardware PCIE-1884.
- `top3d-profile-monitoring-baseline-2026-07-24.md`: contratto VisionPro, fallback legacy,
  persistenza, stati UI e collaudo del profilo Top3D non reject-enabled.
- `pre-four-camera-package-integration-backup-2026-08-03.md`: punto di ripristino verificato e
  perimetro della futura macchina pacchi Top/Left/Right/Bottom.
- `four-camera-read-only-inventory-2026-08-03.md`: inventario verificato di job, ruoli, I/O,
  quote, profili MultiShot e ToolBlock prima della futura integrazione quattro camere.
- `dalsa-dual-illumination-cycling-presets-2026-08-04.md`: configurazione GenICam dei due
  Cycling Preset DALSA, reset di fase, recovery, log e collaudo Front/Backlight.
- `four-camera-package-integration-guideline-2026-08-05.md`: roadmap eseguibile per completare
  VPP, mapping I/O, MultiShot, doppia illuminazione, validatori, ricette e collaudo della macchina
  pacchi Top/Left/Right/Bottom.
- `responsive-multi-camera-display-2026-08-05.md`: viste HMI dedicate Left/Right, separazione
  tra ruolo runtime e ruolo visuale, griglia fino a quattro camere per riga e collaudo display.
- `visionpro-three-camera-startup-hardening-2026-08-19.md`: bootstrap robusto, discovery GigE e reload automatico per VPP con
  `Top`, `Left`, `Rear`, attesa limitata delle FIFO GigE, avvio verificato per ogni job e
  diagnostica di commissioning della Blackfly S.
- `left-dual-multishot-no-trigger-diagnosis-2026-08-19.md`: diagnosi ordinata del profilo
  Left senza acquisizioni, distinzione tra template e file attivo, mapping DO, contratto
  VisionPro/Cycling Preset e sequenza log di collaudo.
- `visionpro-vidiel-classify-embedded-editor-2026-08-21.md`: bootstrap `VProX` anticipato,
  dipendenze ViDi EL dell'editor integrato, diagnostica della deserializzazione Classify e
  collaudo del `CogClassifyTool` dentro la HMI.
- `tblgenerale-production-id-and-classification-2026-08-26.md`: identita' ricetta nelle righe
  archivio, colonne classe/score per ogni vista camera, migrazione DB e query di collaudo.
- `ai-classification-standalone-inspection-2026-08-26.md`: abilitazione e scarto per ricetta,
  contratto output VisionPro, contatore dedicato, allarmi e collaudo della classificazione AI
  separata da sealing e surface.
- `recipe-camera-inspection-profile-2026-09-03.md`: contratto XML della matrice per ricetta,
  fallback dei profili macchina, abilitazione camera condivisa con i trigger e checklist di
  collaudo multi-vista.
- `visionpro-annotated-image-save-2026-09-14.md`: correlazione record/display, rendezvous
  asincrono, cattura `PrintWindow` con fallback Cognex, log e collaudo dei file `_A.bmp` e
  `_Z.jpg` senza bloccare la produzione.
- `trigger-timing-hardening-2026-09-03.md`: correzioni real-time del polling encoder,
  telemetria per fase, durata impulso reale e procedura di collaudo degli shift residui.
- `offline-installer-architecture-and-build-2026-07-23.md`: architettura del media offline,
  aggiornamento differenziale, rollback, prerequisiti, rete Cognex GigE, PATH Python, avvio HMI
  ritardato e inventario SMBIOS; procedura operativa in
  `../../Docs/Commissioning/11_QtisVision_Offline_Installation.md`.
- `pc-hardware-temperature-provider-2026-08-31.md`: provider integrato
  LibreHardwareMonitor, fallback WMI, configurazione, impatto prestazionale e
  collaudo dei sensori del PC industriale.
- `../../Docs/Commissioning/13_VisionPro_ViDi_Classify_Performance.md`: confronto prestazioni
  QuickBuild/HMI, affinity CPU, warm-up, debug e collaudo del classificatore ViDi EL.

## Documento canonico da leggere per primo

Per la baseline attuale leggere:

- `current-unified-baseline-2026-03-27.md`

Questo documento descrive:

- struttura corrente del progetto;
- regole runtime guidate da `Config.xml`;
- cosa e' stato integrato da altre baseline;
- quali parti restano canoniche nel progetto corrente.

## Regola fondamentale

La documentazione tecnica deve rispettare la separazione:

- macchina;
- ricetta;
- runtime.

Macchina:

- mapping I/O;
- schede, canali e polarita';
- encoder;
- quote macchina;
- trigger e reject defaults;
- configurazione letta da `Config.xml`.

Ricetta:

- prodotto;
- tolleranze;
- ispezioni abilitate;
- trigger delay camera;
- parametri VisionPro;
- offset prodotto-specifici delle quote camera;
- modo e delta prodotto-specifici dei profili MultiShot;
- dati prodotto-specifici.

Runtime:

- stato pannello;
- stato macchina;
- contatori;
- prodotti tracciati;
- risultati ispezione;
- popup, notifiche e stato temporaneo UI.

## Documenti operatore

I documenti per l'uso quotidiano sono in:

- `Docs/Manual`

Questa cartella viene letta dalla pagina Manuale dell'HMI.

Il documento principale e':

- `Docs/Manual/00_Documentazione_Tecnica_Globale.md`

La pagina HMI carica esclusivamente i capitoli numerati `NN_*.md`. Il file
`Docs/Manual/README.md` e' l'indice editoriale e non deve comparire all'operatore.

## Documenti commissioning

Le procedure di commissioning sono in:

- `Docs/Commissioning/01_IO_Encoder_Commissioning_Plan.md`
- `Docs/Commissioning/02_Commissioning_Test_Sheet.md`
- `Docs/Commissioning/03_First_Power_On_Sequence.md`
- `Docs/Commissioning/04_Bench_Quick_Checklist.md`
- `Docs/Commissioning/05_Preconfigured_Signal_Matrix.md`
- `Docs/Commissioning/06_PCIE1756_2xADAM3951_Navigator_Test.md`
- `Docs/Commissioning/07_PCIE1884_ADAM3937_First_Encoder_Test.md`
- `Docs/Commissioning/09_Machine_IO_Setup_Unified_View.md`
- `Docs/Commissioning/10_Job_Editor_Live_Preview_Hardware_Trigger.md`

Questi documenti vanno letti insieme a:

- `runtime-foundation.md`
- `encoder-integration-template.md`
- `architecture-and-modules.md`
- `code-changes-log.md`

## Template macchina

- `signal-mapping-template.csv`: tabella da compilare con segnali reali.
- `encoder-integration-template.md`: regole iniziali per velocita', quota prodotto e punto di scarto.
- `runtime-foundation.md`: configurazione persistente e tracking prodotto.
- `side-left-multishot-trigger-2026-06-03.md`: logica MultiShot encoder-driven per camera fisica Left mantenendo ruolo software Side.
- `dalsa-dual-illumination-cycling-presets-2026-08-04.md`: estensione opzionale del MultiShot
  per alternare luce frontale e backlight senza cambiare la sequenza trigger HMI.
- `encoder-automatic-start-and-trigger-flow-2026-06-05.md`: avvio automatico encoder, persistenza taratura e sequenza fotocellula/quote/trigger.
- `io-encoder-electrical-schematic-current-baseline-2026-06-25.md`: schema elettrico applicativo corrente per `PCIE-1756-BE`, `PCIE-1884-AE`, fotocellula, trigger camera, scarto, allarmi ed encoder.
- `pci-io-unified-setup-2026-06-29.md`: differenza tra punti intervento e configurazione macchina, nuova vista unificata `Machine I/O Setup` e flusso segnali runtime.

## Note storiche e di fusione

Alcuni documenti sono stati importati da baseline precedenti o companion project.

Restano utili come contesto, ma possono contenere assunzioni non piu' valide, per esempio:

- path runtime standardizzati;
- riferimenti a cartelle locali di altri sviluppatori;
- snapshot validi solo per una baseline intermedia;
- scelte poi superate dal contratto attuale.

Usarli come supporto storico, non come contratto canonico, salvo quando un documento corrente li richiama esplicitamente.

Documenti principali:

- `repo-alignment-execution-plan-2026-03-27.md`
- `developer-and-tester-onboarding-2026-03-27.md`
- `local-baseline-change-log-2026-03-27.md`
- `main-project-refresh-alignment-2026-03-23.md`
- `main-project-refresh-alignment-2026-03-25.md`
- `baseline-build-restoration-2026-03-26.md`
- `fusion-readiness-assessment-2026-03-25.md`
- `updated-main-project-technical-reconnaissance.md`
- `main-project-language-vs-tool-localization-comparison.md`
- `new-baseline-fusion-preparation-plan.md`
- `inspector-standalone-preparation.md`
- `inspector-native-fusion-slice-2026-03-26.md`
- `main-project-datainspector-configuration-and-usage.md`
- `post-fusion-main-project-health-check-2026-03-26.md`
- `local-baseline-change-log-2026-03-26.md`
- `main-project-language-audit-2026-03-26.md`
- `top3d-machine-configuration-guide-2026-04-13.md`
- `top3d-l38-300-visionpro-panel-integration-2026-04-21.md`
- `lastrelease-tool-commissioning-fusion-2026-04-27.md`

## Lingua e testi operatore

Le nuove viste e i testi esposti all'operatore devono usare il sistema lingua condiviso quando sono testi UI:

- `ServerMessagePersonalize`
- `ServerMessageStructure`
- file runtime lingua sotto `C:\QtisVision\Language`

I file Markdown documentali devono restare leggibili e senza testo corrotto. Preferire ASCII semplice quando si modifica documentazione operativa.
