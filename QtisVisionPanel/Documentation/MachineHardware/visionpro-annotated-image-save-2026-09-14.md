# Salvataggio Immagini VisionPro Annotate

## Scopo

Questa nota descrive il percorso autorevole usato dalla release `3.1.5.4` per
salvare le immagini diagnostiche dei pezzi NOK e non classificati senza
bloccare il ciclo di ispezione.

## Contratto File

| Suffisso | Contenuto | Uso |
|---|---|---|
| `_A.bmp` | Immagine grezza del record dello stesso risultato | Analisi della qualita' di acquisizione |
| `_Z.jpg` | Vista VisionPro con grafica e annotazioni, quando disponibile | Diagnosi immediata del difetto |

Il percorso base continua a provenire dalla configurazione runtime. Questa
funzione non introduce cartelle obbligatorie e non cambia naming o percentuali
di salvataggio gia' configurate.

## Flusso Runtime

1. `UserResultAvailable` consegna il record radice correlato al prodotto.
2. `CameraDisplayManager` risolve il sottorecord mostrato e aggiorna il
   `CogRecordDisplay` sul Dispatcher WPF.
3. `SaveImage` crea subito una richiesta per coppia `ruolo + record radice` e
   accoda il lavoro senza attendere il rendering.
4. Dopo l'aggiornamento display, `CameraDisplayManager` cattura il controllo con
   `PrintWindow(PW_CLIENTONLY)`; se Windows non restituisce il bitmap usa
   `CreateContentBitmap` come fallback Cognex.
5. `AnnotatedImageCache.Store` completa soltanto le richieste riferite allo
   stesso oggetto record radice.
6. Il worker immagini attende al massimo 1500 ms complessivi per tutte le viste
   del pezzo. Alla scadenza salva il raw disponibile.

Il thread di ispezione, i contatori e il Dispatcher non attendono mai questo
processo. Un errore di salvataggio non modifica l'esito del prodotto.

## Protezioni Di Correlazione

- una cattura viene scartata se il display mostra gia' un record successivo;
- la cache e' indicizzata con il record radice, non con il sottorecord grafico;
- richieste e byte JPEG vengono rilasciati anche con coda piena o shutdown;
- la scadenza e' condivisa fra le viste, quindi una camera mancante non
  moltiplica il timeout;
- l'alias `side` ha precedenza su `left` quando la vista runtime e' Side.

## Log Di Accettazione

Per ogni vista annotata salvata correttamente devono comparire:

```text
SAVE_IMAGE_ANNOTATED_READY|file=...|role=...|bytes=...
SAVE_IMAGE_OK|view=...|annotated=..._Z.jpg|raw=..._A.bmp|annotated_source=display-record-graphics|raw_source=record
```

I seguenti log indicano un fallback controllato al raw:

```text
ANNOTATED_CACHE_SKIPPED|role=...|reason=record-superseded
ANNOTATED_CACHE_FAILED|role=...|reason=...
SAVE_IMAGE_ANNOTATED_TIMEOUT|view=...|role=...|timeoutMs=...
```

`ANNOTATED_CACHE_STORED` e' diagnostica `Debug` e indica anche la sorgente
`print-window` oppure `cognex-content`.

## Collaudo In Macchina

1. Abilitare il salvataggio NOK e produrre un difetto noto su ogni vista attiva.
2. Verificare che contatore ed esito si aggiornino senza attendere i file.
3. Aprire `_A.bmp` e confermare che sia grezza.
4. Aprire `_Z.jpg` e confermare immagine, overlay, testo e geometrie VisionPro.
5. Confrontare timestamp, numero pezzo e vista fra DB, log e cartella immagini.
6. Eseguire almeno 100 prodotti verificando assenza di immagini appartenenti al
   ciclo precedente o successivo.
7. Se appare un timeout, conservare entrambi i file e il blocco log completo;
   la produzione deve continuare.

## Impatti

- `Config.xml`: nessuna nuova chiave;
- ricetta XML: nessuna modifica;
- MySQL: nessuna modifica schema;
- dipendenze: usa `user32.dll`, gia' presente in Windows, e le API VisionPro
  incluse nel runtime applicazione.
