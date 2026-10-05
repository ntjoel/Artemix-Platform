# Photocell Timed Trigger Commissioning

## Modalita operative da non confondere

- `TriggerSchedulingMode=TimedFromPhotocell`
  - la fotocellula accoda direttamente gli impulsi temporizzati `TOP` e `SIDE`
  - `VirtualConveyorEnabled` viene disattivato automaticamente
- `TriggerSchedulingMode=VirtualConveyor`
  - la fotocellula crea `PRODUCT_ZERO`
  - i punti intervento scattano solo quando avanzano con encoder reale o virtual conveyor

Nota importante:
- disattivare solo `VirtualConveyorEnabled` non attiva il timed trigger
- se lasci `TriggerSchedulingMode=VirtualConveyor` e spegni il virtual conveyor, il runtime aspetta un encoder reale

## Checklist minima configurazione macchina

Per usare davvero il timed trigger su questa macchina:

1. `TriggerSchedulingMode=TimedFromPhotocell`
2. `ProductPhotocellSignalCode=IN_PRODUCT_PHOTOCELL`
3. `ExternalTriggerEnableSignalCode` deve restare separato dalla fotocellula prodotto
4. `VirtualConveyorEnabled=false`
5. `CAMERA_TRIGGER_TOP` e `CAMERA_TRIGGER_SIDE` devono avere `SignalCode` valorizzato
6. i delay devono essere in millisecondi reali:
   - esempio iniziale `560 ms`
   - non `560000 ms`
7. `OUT_SPARE_DO00` deve restare non referenziato se non viene usato per diagnostica

## Nuova logica UI in Machine Configuration

La schermata Machine Configuration ora separa:

- area primaria:
  - fotocellula prodotto
  - uscita camera TOP
  - uscita camera SIDE
- area opzionale avanzata:
  - consenso trigger esterno
  - trigger legacy
  - binding luci separati

Per la macchina attuale, la taratura standard deve essere fatta quasi sempre solo nell'area primaria.

## Tracce log attese nel flusso corretto

Quando la macchina e' configurata correttamente, dopo il fronte fotocellula devono comparire in ordine:

- `FAST_PATH_PHOTOCELL_ACCEPTED ...`
- `Timed trigger route prepared ...`
- `PHOTOCELL_EDGE_ACCEPTED ... eventAge=... ms`
- `TIMED_TRIGGER_SCHEDULED ... effectiveDelay=... ms compensation=... ms`
- `TIMED_TRIGGER_OUTPUT_ON ... actualDelay=... ms executorDelay=... ms`
- `Pulse START OUT_CAMERA_* ...`
- `Pulse END OUT_CAMERA_* ...`
- `TIMED_TRIGGER_OUTPUT_OFF ...`
- `TRIGGER_BATCH_COMPLETED ...`

Nota:
- nel percorso fast-path il runtime non aspetta il dispatcher UI per accodare lo scatto
- `compensation` e' il tempo gia trascorso dal fronte fotocellula al momento della coda scheduler
- `effectiveDelay` e' il delay residuo realmente atteso prima di alzare l'uscita

## Scopo

Questa procedura guida il collaudo macchina della modalita:

- `TriggerSchedulingMode = TimedFromPhotocell`

L'obiettivo e' verificare:

- ripetibilita' del fronte fotocellula
- corretto scatto indipendente `TOP` / `SIDE`
- assenza di doppi impulsi
- coerenza dei delay impostati nel file macchina

---

## Prerequisiti

Verificare prima:

- fotocellula prodotto cablata e letta su `IN_PRODUCT_PHOTOCELL`
- uscita trigger TOP cablata e mappata nel file macchina
- uscita trigger SIDE cablata e mappata nel file macchina
- VisionPro pronto a ricevere trigger hardware
- macchina in condizioni sicure per i test

Parametri macchina minimi da controllare in `DigitalIOControl`:

- `TriggerSchedulingMode`
- `PhotocellDebounceMs`
- `MinimumRetriggerGapMs`
- `TopTriggerBaseDelayMs`
- `SideTriggerBaseDelayMs`
- `TopTriggerPulseMs`
- `SideTriggerPulseMs`

---

## Sequenza consigliata

### 1. Verifica mapping

Controllare nella sezione timed trigger:

- riepilogo uscite `TOP=...`
- riepilogo uscite `SIDE=...`

Il nome logico deve corrispondere all'uscita fisica reale della telecamera.

### 2. Test manuale uscite singole

Usare i pulsanti:

- `Force timed TOP`
- `Force timed SIDE`

Verificare:

- impulso presente solo sull'uscita prevista
- nessun impulso sull'altra uscita
- durata coerente col `PulseMs` configurato

### 3. Test batch simulata

Usare:

- `Simulate photocell edge`

Verificare nel log:

- `FAST_PATH_PHOTOCELL_ACCEPTED` solo con fotocellula reale
- `PHOTOCELL_EDGE_ACCEPTED`
- `TIMED_TRIGGER_SCHEDULED`
- `TIMED_TRIGGER_OUTPUT_ON`
- `TIMED_TRIGGER_OUTPUT_OFF`
- `TRIGGER_BATCH_COMPLETED`

Verificare in pagina:

- ultimo edge aggiornato
- ultimo TOP aggiornato
- ultimo SIDE aggiornato
- metriche batch e scatti incrementate

### 4. Test fotocellula reale

Con prodotto reale:

- generare almeno 20 passaggi a velocita' nominale
- osservare il punto di scatto TOP
- osservare il punto di scatto SIDE

Verificare:

- nessun doppio scatto
- nessuno scatto mancante
- posizione coerente nel campo utile

### 5. Taratura delay

Se il punto immagine e' anticipato o ritardato:

- correggere `TopTriggerBaseDelayMs`
- correggere `SideTriggerBaseDelayMs`

Usare piccoli step progressivi e ripetere sempre almeno 10 scatti per conferma.

### 6. Verifica stabilita'

Eseguire:

- stop/start macchina
- save configurazione
- reload configurazione
- cambio vista pannello

Verificare:

- nessun batch pendente residuo
- nessun impulso spurio dopo save/load
- timed diagnostics coerente dopo i test

---

## Criteri di accettazione

La macchina puo' passare al collaudo finale se:

- TOP resta nello stesso punto immagine utile
- SIDE resta nello stesso punto immagine utile
- non compaiono doppi impulsi
- i log mostrano una sola batch per ogni fronte reale valido
- la dispersione dei tempi osservati resta compatibile con il processo macchina

---

## Log da osservare

Chiavi principali:

- `FAST_PATH_PHOTOCELL_ACCEPTED`
- `PHOTOCELL_EDGE_ACCEPTED`
- `TIMED_TRIGGER_SCHEDULED`
- `TIMED_TRIGGER_OUTPUT_ON`
- `TIMED_TRIGGER_OUTPUT_OFF`
- `TRIGGER_BATCH_COMPLETED`
- `TRIGGER_BATCH_CANCELLED`

Se compaiono:

- `TIMED_TRIGGER_OUTPUT_SKIPPED`
- `TRIGGER_BATCH_ERROR`

fermare il collaudo e verificare mapping, signal code e stato delle uscite.
