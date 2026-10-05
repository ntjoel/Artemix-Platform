# Hardening Temporale Trigger Encoder

## Evidenza Di Campo

Gli shift SIDE osservati il 3 settembre 2026 coincidono con cicli I/O in cui
`work` sale a 40-60 ms. REAR e TOP possono ancora partire quasi in quota, mentre
il target SIDE, raggiunto durante lo stesso blocco, viene rilevato decine di
millimetri dopo. Esistono anche cicli con `cycle` alto e `work` basso: in quel
caso e' il thread di polling a non essere schedulato, non il corpo del poll.

## Correzioni 3.1.5.1

- tutti gli `AsyncWrapper` NLog raggiungibili dal runtime usano `Discard` e
  `batchSize=200`; nessun audit o errore attende disco o MySQL;
- il frequency meter BDaq non viene piu' letto nel polling da 2 ms ne' dal
  refresh UI: la frequenza diagnostica e' stimata dai delta del contatore;
- `IO_POLL_OVERRUN` aggiunge `slowest` e `slowestMs` per distinguere
  `encoder-read`, letture input, callback input, logica custom, trigger contatori
  e salvataggio simulazione;
- `IO_OUTPUT_WRITE_SLOW` separa `lockWaitMs`, `hardwareMs` e `totalMs`;
- `IO_PULSE_TIMING_OVERRUN` registra impulso richiesto, durata HIGH reale e
  attesa sul semaforo del canale.

## Collaudo Richiesto

1. Avviare la macchina con la ricetta reale e attendere la stabilizzazione.
2. Eseguire almeno 30 prodotti alla velocita' massima prevista.
3. Estrarre `IO_POLL_OVERRUN`, `IO_OUTPUT_WRITE_SLOW`,
   `IO_PULSE_TIMING_OVERRUN` e le righe `Trigger request executed`.
4. Separare `cycle` alto con `work` basso dai casi con `work` alto.
5. Nei casi `work` alto verificare `slowest`: solo quel sottosistema deve essere
   candidato al successivo intervento.
6. Confrontare `actualHighMs` con la durata richiesta e `hardwareMs` con il
   tempo totale della scrittura.
7. Verificare fisicamente la posizione su immagine: il valore `late` misura il
   campione encoder che ha rilevato il superamento quota, non l'esposizione.

## Criteri Di Accettazione

- nessun target NLog usa `Block`;
- polling ordinario vicino a 2 ms e nessun `work` oltre 10 ms durante i trigger;
- scarto trigger stabile entro la tolleranza di commissioning;
- nessun impulso resta HIGH oltre richiesta +25% (minimo margine 5 ms);
- nessun recovery VisionPro e' causato da heartbeat condivisi tra Side, Rear e
  Bottom.

Se resta prevalente `cycle` alto con `work` basso, il passo successivo riguarda
scheduling/affinita' del worker sul PC industriale. Se resta prevalente
`encoder-read`, va misurato il solo `UdCounterCtrl.Read` prima di cambiare lock
o driver: non si modifica il cuore real-time su una supposizione.
