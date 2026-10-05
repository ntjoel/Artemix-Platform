# Vision Runtime Timing And Unclassified Outcome

## Scopo

Questa guida verifica due modifiche della release successiva alla `3.1.1.8`:

- riduzione del jitter del polling encoder e dei trigger camera;
- gestione esplicita degli errori di elaborazione VisionPro come esito `3 - Non classificato`.

Le prove devono essere eseguite prima in simulazione e poi sulla macchina reale alla velocita'
massima prevista. Il polling software riduce il jitter di Windows, ma non sostituisce un compare
encoder hardware quando e' richiesta una precisione real-time deterministica.

## Contratto esiti

| Codice | Esito | Significato |
|---|---|---|
| `0` | NOK | Ispezione completata e prodotto non conforme |
| `1` | GOOD | Ispezione completata e prodotto conforme |
| `3` | Non classificato | Uno o piu' job VisionPro hanno terminato con errore di elaborazione |

Il risultato VisionPro `Reject` e' un'ispezione completata e resta `NOK`. Solo
`CogToolResultConstants.Error`, oppure un errore nella cattura del risultato, genera il codice `3`.

Per le colonne `NC_<vista>AiClassification` resta inoltre valido `4 = controllo non
abilitato/non eseguito`. Se l'errore riguarda quella vista e la classificazione AI e' abilitata,
la colonna assume `3`.

## Modifiche runtime

- Il polling I/O usa un worker dedicato, priorita' `AboveNormal`, timer Windows a 1 ms e target
  di campionamento 2 ms.
- Encoder e ingressi sono letti nello stesso ciclo; il fronte fotocellula conserva lo snapshot
  encoder associato.
- Gli aggiornamenti contatori e indicatori sono accodati sul Dispatcher senza bloccare
  l'orchestratore.
- Il backup JSON dei contatori e' scritto in background e le richieste ravvicinate sono aggregate.
- Lo stato VisionPro viene catturato insieme al risultato, evitando di leggere il `RunStatus` di
  un'acquisizione successiva.
- In caso di errore non vengono validate uscite potenzialmente vecchie. Il pezzo viene scartato,
  contato come `Non classificato`, archiviato con `Esito_Classificazione=3` e vengono forzate le
  immagini di tutte le viste attive.

## Prova 1 - Polling e precisione trigger

1. Avviare la HMI con le schede reali e verificare il log:

   `IO_POLL_STARTED|worker=dedicated|priority=AboveNormal|timer_resolution=1ms|target=2ms`

2. Eseguire almeno 100 prodotti alla velocita' massima prevista.
3. Esportare le righe `IO_POLL_OVERRUN` e le righe trigger contenenti `target`, `actual` e `late`.
4. Raggruppare i dati per camera, senza mescolare Top, Left/Side, Rear/Right e Bottom.
5. Verificare che non esistano sequenze regolari a 15-17 ms come nella vecchia attesa
   `Task.Delay`.
6. Controllare in Gestione attivita' che il worker non provochi saturazione CPU e che i tempi
   VisionPro restino stabili.

Criterio di accettazione iniziale:

- nessun blocco del polling maggiore di 100 ms;
- warning `IO_POLL_OVERRUN` non continui;
- errore `late` stabile e ripetibile per ogni camera;
- nessun incremento delle code risultati durante produzione stabile;
- scostamento immagine residuo correggibile con il trim macchina o l'offset ricetta, senza
  compensare un jitter casuale.

Se lo scostamento resta casuale e supera la tolleranza meccanica, registrare velocita',
counts/mm, camera, target, actual e late. Il passo industriale successivo e' spostare la
generazione del trigger su compare hardware della scheda encoder.

## Prova 2 - Latenza risultati e contatori

1. Azzerare i contatori e avviare una sequenza di almeno 50 prodotti.
2. Confrontare il timestamp `result queued` con l'aggiornamento visivo del contatore.
3. Verificare che la UI resti utilizzabile durante il salvataggio immagini e il flush MySQL.
4. Controllare che `Total = Good + NoGood + Unclassified`.
5. Arrestare e riavviare l'applicazione e verificare il ripristino degli stessi valori dal backup.

Criterio di accettazione:

- il contatore cambia senza attendere rendering display, scrittura immagini o database;
- nessuna crescita continua delle callback Dispatcher;
- nessun doppio incremento per singolo prodotto.

## Prova 3 - Errore VisionPro controllato

In simulazione creare temporaneamente un errore riproducibile in una sola vista, per esempio un
binding verso un oggetto `Nothing`. Non usare un normale limite di qualita' per questa prova.

Log attesi:

- `VISION_RESULT_UNCLASSIFIED_CANDIDATE` oppure `VISION_RESULT_SNAPSHOT_FAILED`;
- `INSPECTION_UNCLASSIFIED|outcome=3`;
- `UNCLASSIFIED_DIAGNOSTIC_SAVE|policy=forced-all-active-views`;
- una riga `SAVE_IMAGE_OK` per ogni vista attiva che possiede un record corrente.

Verifiche:

- `Total` aumenta di uno;
- `Unclassified` aumenta di uno;
- `Good` e `NoGood` non aumentano;
- il prodotto richiede lo scarto;
- `tblgenerale.Esito_Classificazione = 3`;
- `InspectionStatusDetails` contiene ruolo e messaggio VisionPro;
- nel Data Inspector il pezzo appare come `Non classificato`;
- la cartella `Piece_*` contiene immagine annotata `_Z.jpg` e grezza `_A.bmp` per tutte le viste
  attive disponibili;
- i normali flag difetto non vengono valorizzati artificialmente come NOK.

Ripristinare il tool e verificare che il prodotto seguente torni a `GOOD` o `NOK` e non riutilizzi
lo stato o le immagini del pezzo precedente.

## Prova 4 - Reject valido

Generare un normale difetto che faccia terminare VisionPro con `Reject`, senza eccezioni dei tool.
Il pezzo deve essere registrato con codice `0`, incrementare `NoGood` e il contatore del difetto
specifico. Non deve incrementare `Unclassified`.

## Database e compatibilita'

Non sono aggiunte colonne o tabelle. La release usa:

- la colonna esistente `tblgenerale.Esito_Classificazione` per il valore `3`;
- una nuova riga logica `UNCLASSIFIED` nella tabella contatori globale;
- le colonne AI per vista gia' presenti, quando applicabili.

Ricette e file `Config.xml` precedenti restano compatibili e non richiedono migrazione.

## Dati da allegare a un'anomalia

- versione software;
- ricetta e velocita' linea;
- counts/mm e canale encoder;
- 30 secondi di log prima e dopo l'evento;
- righe `IO_POLL_OVERRUN`, trigger `target/actual/late`, `VISION_RESULT_*` e
  `INSPECTION_UNCLASSIFIED`;
- cartella completa `Piece_*` del prodotto;
- valore DB di `Esito_Classificazione` e `InspectionStatusDetails`.
