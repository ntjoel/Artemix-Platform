# Display Multi-Camera Responsive

## Scopo

La release `3.0.8.6` rende esplicite nell'overview le viste `Left` e `Right` e
adatta il numero di colonne allo spazio realmente disponibile. La modifica e'
solo di presentazione e routing display: trigger, code risultati, validatori,
scarto, ricette e mapping I/O restano invariati.

## Ruolo Runtime E Ruolo Visuale

| Job VPP | Ruolo runtime compatibile | Vista HMI | Cache record |
|---|---|---|---|
| `Top` / `Top3D` | `top` / `top3d` | `TopCameraView` | Top |
| `Side` | `side` | `SideCameraView` | Side |
| `Left` / `SideLeft` | `left` | `LeftCameraView` | Side |
| `Front` | `front` | `FrontCameraView` | Front |
| `Rear` | `rear` | `RearCameraView` | Rear |
| `Right` / `SideRight` | `rear` | `RightCameraView` | Rear |
| `Bottom` | `bottom` | `BottomCameraView` | Bottom |

`Right` continua intenzionalmente a essere normalizzata come `rear` nel ciclo
macchina. `CameraModel.DisplayType` e
`CameraConfigurationHelper.NormalizeCameraDisplayType()` conservano invece il
nome visuale. Questo evita di cambiare contratti gia' collaudati mentre rende
chiara all'operatore la posizione fisica della camera.

## Regola Di Layout

`CameraContainerViewModel` calcola il layout sulla larghezza effettiva del
container, non sul solo modello del monitor:

| Spazio utile | Colonne massime |
|---|---|
| meno di 620 px | 1 |
| da 620 a 899 px | 2 |
| almeno 900 px, 3 camere | 3 |
| almeno 900 px, 4 o piu' camere | 4 |

Con cinque camere, le prime quattro occupano la prima riga e la quinta apre la
seconda. Ogni cella mantiene la stessa larghezza; altezza e altezza minima del
`CogRecordDisplay` vengono ricalcolate tra 145 e 265 px in funzione di numero
camere e spazio disponibile. Sotto 900 px il layout riduce le colonne per non
rendere il contenuto illeggibile.

## Indicatori Left E Right

Le viste laterali dedicate espongono:

- saldatura laterale (`SealingSide`);
- controllo rotolo (`SideRollCount`).

La visibilita' segue le abilitazioni dell'`InspectionConfigService`. Lo stato
Left usa il risultato Side/Left; lo stato Right usa il risultato Rear/Right.
Un controllo disabilitato non viene mostrato e non modifica l'esito macchina.

## Ciclo Vita

- il cambio ricetta pulisce record e indicatori Left/Right;
- lo shutdown arresta eventuale live display e rilascia i controlli Cognex;
- il Job Tool Editor conserva `LEFT` e `RIGHT` nei titoli, ma normalizza il
  ruolo quando deve risolvere il trigger fisico;
- i file lingua vengono aggiornati dallo script
  `scripts/UpdateRuntimeLanguageFiles.ps1`.

## Impatti Compatibilita'

- `Config.xml`: nessun nuovo campo;
- `CameraConfig.xml`: nessuna migrazione; i tipi `Left` e `Right` esistenti
  vengono riconosciuti;
- machine runtime config: nessun nuovo campo;
- ricette: nessun nuovo campo;
- database: nessuna modifica;
- VisionPro: i nomi job semanticamente espliciti restano raccomandati.

## Collaudo

1. Caricare un VPP con job `Top`, `Left`, `Right`, `Bottom`.
2. Verificare quattro pannelli distinti nella prima riga su un viewport di
   almeno 900 px.
3. Aggiungere un quinto job e verificare l'apertura della seconda riga.
4. Eseguire un prodotto e verificare che ogni immagine arrivi nel pannello con
   lo stesso nome del job.
5. Generare un NOK Left e un NOK Right separatamente e verificare l'indicatore
   della vista corretta.
6. Cambiare ricetta e verificare che immagini e indicatori precedenti siano
   azzerati.
7. Ridimensionare la finestra o provare il pannello da 15 pollici: non devono
   apparire tagli orizzontali o sovrapposizioni.

Il collaudo con acquisizione reale resta obbligatorio, perche' una build non puo'
validare FIFO, record VisionPro e proporzioni effettive del pannello installato.
