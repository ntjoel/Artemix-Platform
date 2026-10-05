# Backup pre-integrazione macchina pacchi a quattro telecamere

## Scopo

Il 3 agosto 2026 e' stato creato e verificato un punto di ripristino completo
prima di iniziare l'integrazione della macchina pacchi con ruoli VisionPro:

- `Top`;
- `Left`;
- `Right`;
- `Bottom`.

La creazione del backup non modifica il runtime, il mapping I/O, le ricette, i
job VisionPro o il database.

## Baseline congelata

| Voce | Valore |
|---|---|
| Versione software | `3.0.8.4` |
| Branch | `master` |
| Commit | `8ba46362f6658f2ea8c0fb547204bcb558e07c55` |
| Tag Git | `pre-four-camera-packages-multishot-3.0.8.4-20260803` |
| Stato working tree | incluso integralmente nel backup, anche se non committato |

## Copie create

### Backup locale

Percorso:

`D:\Pulsar\Backups\QtisVisionPanel\QtisVisionPanel_pre_four_camera_packages_20260803_122240`

Contiene:

- `SourceFull`: sorgente completo, `.git`, build, pacchetti e stato di lavoro;
- `RuntimeQtisVision`: copia di `C:\QtisVision`, inclusi VPP, XML, lingue e dati AI;
- `Git`: bundle completo, patch e inventario del working tree;
- `Manifests`: inventari SHA-256 e report di confronto;
- log `robocopy`, procedura di ripristino e requisiti della fase successiva.

Verifica locale:

- sorgente: `7115` file, `5302096379` byte, zero differenze SHA-256;
- runtime: `187` file, `746369036` byte, zero differenze SHA-256;
- bundle Git: cronologia completa verificata.

La cache `.vs` non e' inclusa. I junction `VisionProDependencies` non sono
duplicati: puntano a `C:\Program Files\Cognex\VisionPro\bin` e vengono forniti
dall'installazione Cognex.

### Snapshot condiviso OneDrive

Percorso:

`C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\lastRelease_backups\QtisVisionPanel_shared_pre_four_camera_packages_20260803_122240`

Verifica: `6557` file e `5805895223` byte; confronto `robocopy /MIR /L /XJ`
con zero file differenti e codice uscita `0`.

## Limite del backup

Non e' stato eseguito un dump dei dati MySQL live. Sono inclusi sorgente,
inizializzazione schema, migrazioni e configurazioni applicative. Il backup dei
dati di produzione richiede un dump coerente specifico della macchina e delle
sue credenziali.

## Perimetro della prossima integrazione

| Ruolo / job | Acquisizione prevista | Ispezioni richieste |
|---|---|---|
| `Top` | comportamento esistente | controlli superiori gia definiti dal job e dalla ricetta |
| `Left` | MultiShot per i profili applicabili | saldatura laterale e rotolo girato nel pacco |
| `Right` | MultiShot per i profili applicabili | saldatura laterale e rotolo girato nel pacco |
| `Bottom` | scatto singolo o MultiShot secondo pacco e saldatura | saldatura trasversale/inferiore e carta intrappolata nella saldatura |

Vincoli da preservare:

- I/O fisico, encoder, counts/mm, zero e quote nominali restano macchina;
- dimensioni, ispezioni e correzioni prodotto-specifiche restano ricetta;
- i limiti fisici e di sicurezza MultiShot restano globali macchina;
- l'HMI genera i pulse hardware, mentre VisionPro acquisisce, esegue lo
  stitching e restituisce soltanto l'esito finale;
- le ricette esistenti e le macchine con meno telecamere devono restare
  compatibili;
- ogni camera abilitata deve contribuire una sola volta a esito prodotto,
  contatori e scarto;
- i nomi esatti degli output ToolBlock devono essere letti dai VPP reali e non
  dedotti prima dell'implementazione.

## Primo controllo prima di sviluppare

La fase successiva deve iniziare con un inventario read-only di job, ruoli,
segnali, quote, profili MultiShot e output ToolBlock attuali. Solo dopo quel
confronto si definiscono le estensioni compatibili a config, ricetta,
orchestrazione risultati e diagnostica di commissioning.

Inventario completato il 3 agosto 2026:

- `four-camera-read-only-inventory-2026-08-03.md`.

Il documento distingue la capacita' gia' presente nel software dagli asset
VisionPro e I/O realmente commissionati e contiene i criteri di ingresso alla
fase di modifica runtime.
