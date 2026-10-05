# Trigger encoder deterministico e predisposizione compare PCIE-1884

Data: 2026-09-24

## Obiettivo

Ridurre lo spostamento delle immagini causato da pause sporadiche del polling encoder e
rendere deterministica la durata degli impulsi camera. La modifica riguarda solo il runtime:
non aggiunge campi a `Config.xml`, ricette o database.

## Evidenza di partenza

Nel collaudo a 40 prodotti del 2026-09-24 sono stati osservati due ritardi SIDE:

- prodotto 8: 120 count, 15,1 mm, con ciclo polling fermo per 24,806 ms;
- prodotto 35: 143 count, 18,0 mm, con ciclo polling fermo per 41,715 ms.

In entrambi i casi il lavoro effettivo del poll era inferiore a 0,4 ms. Il problema era
quindi la mancata schedulazione del thread, non il calcolo della quota.

## Architettura software implementata

`AdvantechDeviceManager` non invoca piu callback applicative dentro la lettura encoder.
Ogni campione viene copiato in due ring buffer fissi da 1024 elementi:

- `Qtis.EncoderCritical`, priorita `Highest`: tracking prodotto e MultiShot;
- `Qtis.EncoderTelemetry`, priorita `BelowNormal`: valori e frequenza mostrati nella UI.

La coda critica non contiene aggiornamenti WPF, log operativi o attese asincrone. Un overflow
genera `ENCODER_DISPATCH_QUEUE_OVERFLOW`; il campione piu vecchio viene scartato per conservare
la posizione piu recente.

`MachineController.UpdateEncoderPosition` non crea piu liste e query LINQ ad ogni campione.
Le raccolte temporanee vengono create soltanto quando un punto viene realmente raggiunto.

## Impulsi digitali

Gli impulsi passano da `IIODeviceManager.PulseOutputAsync` al worker `Qtis.OutputPulse`:

- coda fissa da 128 richieste;
- priorita `Highest`;
- salita e discesa scritte dallo stesso proprietario I/O;
- scadenza calcolata con `Stopwatch` monotono;
- nessun `Task.Delay` nel percorso camera;
- tentativo fail-safe di riportare l'uscita inattiva in caso di errore.

I log `IO_PULSE_TIMING_OVERRUN` restano disponibili e usano la durata misurata dal worker.

## Predisposizione compare hardware PCIE-1884

L'interfaccia espone `SupportsHardwareEncoderCompare` e
`ConfigureEncoderCompareTableAsync`. Con `arm=false` viene validato il piano senza modificare
la scheda. Con `arm=true` vengono usati `CompareClear` e `CompareSetTable` della FIFO compare.

La PCIE-1884 dispone di FIFO compare da 1024 valori e uscite indicate condivise con i quattro
DO. Prima dell'attivazione in produzione servono:

1. verificare in DAQNavi il modo counter e il comportamento COUT/DO del canale scelto;
2. cablare l'uscita compare PCIE-1884 verso il trigger camera o verso un fan-out isolato;
3. verificare polarita e ampiezza impulso con oscilloscopio;
4. impedire che lo stesso DO sia comandato contemporaneamente come uscita digitale normale;
5. provare una sola camera a bassa velocita prima di caricare le tre quote nella FIFO.

Il runtime corrente continua a comandare DO02, DO03 e DO07 della PCIE-1756. Nessuna tabella
compare viene armata automaticamente.

## Collaudo macchina

Ripetere almeno 100 prodotti e conservare i log contenenti:

- `IO_POLL_OVERRUN`;
- `ENCODER_DISPATCH_QUEUE_OVERFLOW`;
- `INTERVENTION_DISPATCH_DELAY`;
- `IO_PULSE_RAISE_LATE`;
- `IO_PULSE_TIMING_OVERRUN`;
- `Trigger request executed`.

Criteri software attesi:

- nessun overflow delle code encoder o impulso;
- ritardo trigger normalmente entro 12 count e nessun caso oltre 5 mm;
- durata impulso 40 ms con scostamento normalmente entro 2 ms;
- nessun risultato camera perso per mancato fronte di trigger.

## Rollback

Il backup precedente alla modifica resta in:

`.backups/pre-io-hardening-20260924-081818`

Non sono richieste migrazioni XML o database per ripristinare la versione precedente.
