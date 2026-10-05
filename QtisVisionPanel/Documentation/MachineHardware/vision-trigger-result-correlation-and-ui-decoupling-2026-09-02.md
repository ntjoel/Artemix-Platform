# Vision Trigger, Result Correlation And UI Decoupling 2026-09-02

## Scope

Questa nota descrive la correzione introdotta in `3.1.2.0` dopo il collaudo reale a tre camere
Top, Side/Left e Rear. Non modifica XML macchina, ricette o database.

## Evidenze dai log

- `IO_POLL_OVERRUN` mostrava cicli da 70 a 175 ms durante le transizioni delle uscite;
- i trigger successivi arrivavano fino a 118 mm oltre la quota prevista;
- la coda Rear conteneva piu risultati e il FIFO associava un risultato vecchio al prodotto dopo;
- il salvataggio di T, F e R impiegava diversi secondi perche `CreateContentBitmap` lavorava sul
  Dispatcher WPF;
- durante quel lavoro il watchdog interpretava la pipeline occupata come uno stallo VisionPro.

## Correzione runtime

### Percorso realtime

`MachineController.UpdateEncoderPosition` continua a rilevare le quote sul worker Advantech, ma
l'evento raggiunto viene soltanto accodato. Un worker `Qtis.InterventionOutput` a priorita'
`AboveNormal` esegue le scritture e avvia i timer di spegnimento degli impulsi.

La scrittura di un singolo canale PCIE-1756 aggiorna ora solo il port byte interessato invece dei
quattro port byte della scheda. `IO_OUTPUT_WRITE_SLOW` segnala chiamate DAQNavi oltre 20 ms.
`INTERVENTION_DISPATCH_DELAY` segnala invece il tempo trascorso nella coda software.

### Identita' prodotto

Quando la HMI genera un trigger camera, registra un ticket con ruolo camera, `ProductId`, punto
intervento, segnale e timestamp del trigger. Il callback `UserResultAvailable` consuma il ticket
del proprio ruolo e copia il `ProductId` nel `CameraResult`.

L'orchestratore abbina Top e companion per questo ID, non piu' soltanto per ordine di arrivo. Un
evento hardware aggiuntivo senza ticket viene scartato con `VISION_RESULT_UNSOLICITED_DROPPED`;
un risultato vecchio trovato in coda produce `COMPANION_STALE_DROPPED`.

Per ogni ruolo resta claimabile un solo ticket per prodotto, anche durante un MultiShot. Se una
camera perde davvero un'acquisizione e arriva il trigger del prodotto seguente, il ticket rimasto
viene sostituito e il log `VISION_TRIGGER_TICKET_REALIGNED` evita che l'intera produzione resti
spostata di un ciclo.

I job sintetici, i trigger esterni e i percorsi legacy senza ticket conservano il fallback
compatibile. In tale fallback il controllo timestamp usa il valore assoluto del lag, quindi non
accetta piu' automaticamente risultati molto vecchi.

### UI e immagini

- contatori e indicatori usano priorita' WPF `DataBind`;
- i display camera lavorano a priorita' `Background`;
- ogni ruolo conserva la sequenza piu' recente e ignora aggiornamenti UI completati fuori ordine;
- l'orchestratore non attende il rendering dei display prima di passare al gruppo successivo;
- il salvataggio cattura una mappa immutabile ruolo/record in una coda limitata a 8 pezzi;
- conversione immagini e scrittura disco lavorano su worker dedicati, con cattura a priorita'
  `BelowNormal`;
- il BMP `_A.bmp` resta sempre l'immagine grezza. Per NOK e non classificati il JPEG `_Z.jpg`
  viene renderizzato con gli overlay VisionPro da un display ActiveX dedicato fuori schermo;
- la richiesta di rendering e' elaborata dal worker bounded e non blocca la pipeline d'ispezione.
  Se il render non parte entro il timeout o fallisce, `_Z.jpg` ricade sul raw;
- se la coda e' piena, `IMAGE_CAPTURE_QUEUE_FULL` rende esplicita la perdita diagnostica senza
  rallentare il ciclo macchina.

## Collaudo obbligatorio

1. Avviare una ricetta hardware con almeno Top, Side/Left e Rear.
2. Eseguire almeno 100 prodotti alla velocita' massima prevista.
3. Verificare che `INTERVENTION_DISPATCH_DELAY` resti normalmente sotto 5 ms.
4. Verificare l'assenza di `IO_OUTPUT_WRITE_SLOW`; se presente, controllare driver, scheda e
   contesa con heartbeat/altre uscite.
5. Controllare nel log che `topProduct` e tutti i `companionProducts` coincidano.
6. Verificare che ogni prodotto incrementi il totale una sola volta e che difetti, immagini e
   classificazione appartengano allo stesso passaggio fisico.
7. Forzare un errore tool VisionPro: esito `3 - Non classificato`, immagini di tutte le viste e
   riga consultabile nel Data Inspector.
8. Verificare che non parta recovery automatica mentre la sola coda immagini sta lavorando.

## Limite fisico

Windows, DAQNavi e una scrittura software DO non forniscono una garanzia hard real-time. Se il
collaudo mostra ancora una dispersione incompatibile con il processo nonostante tempi software
regolari, il trigger camera deve passare a compare encoder hardware; gli offset ricetta correggono
una deriva costante, non il jitter.
