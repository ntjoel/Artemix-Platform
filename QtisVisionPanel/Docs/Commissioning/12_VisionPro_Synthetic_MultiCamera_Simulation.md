# VisionPro Synthetic Multi-Camera Simulation

## Scopo

Questa procedura serve a provare un VPP con due o piu' job VisionPro usando
immagini salvate sul PC e `CogAcqFifoSynthetic`, senza schede I/O o camere reali.

Una FIFO sintetica non riceve il trigger prodotto comune della macchina. Ogni
job gira alla velocita' dei propri tool: Top puo' produrre molti risultati mentre
Side sta ancora completando una singola elaborazione. Questa differenza non e'
automaticamente un guasto della camera Side.

## Comportamento della HMI

Quando tutti i job caricati sono sintetici, la HMI:

1. usa 30 s come timeout di attesa risultato e watchdog;
2. accoppia Top e companion in ordine FIFO, non tramite timestamp tra job;
3. mantiene al massimo tre risultati sintetici arretrati per coda;
4. rilascia i record piu' vecchi se un job e' molto piu' veloce;
5. non modifica ricette, contatori salvati, mapping I/O o parametri MultiShot.

La modalita' si disattiva automaticamente se almeno un job attivo usa una FIFO
hardware. In quel caso tornano le protezioni di produzione: identita' temporale
del prodotto e timeout single-shot di 5 s, oppure 30 s per MultiShot.

## Preparazione VPP

1. Aprire il VPP in QuickBuild.
2. Verificare che ogni job richiesto sia presente e abbia un nome/ruolo valido,
   per esempio `Top` e `Side`.
3. Configurare per ogni job l'immagine o la sequenza sintetica prevista.
4. Eseguire ciascun job singolarmente e annotare il tempo ciclo massimo.
5. Salvare il VPP e selezionarlo come ricetta di produzione nella HMI.

Non abilitare MultiShot solo per compensare un job sintetico lento. MultiShot
resta una configurazione della sequenza hardware reale.

## Collaudo

1. Avviare la HMI e portarla in Running.
2. Cercare nel log:

```text
VISION_WATCHDOG_MODE|synthetic=True|...|result_timeout_ms=30000
VISION_SIMULATION_PAIRING_ACTIVE
```

3. Lasciare girare almeno 3 minuti.
4. Verificare che vengano registrati gruppi con `companions=[side]` e
   `missing=[]`.
5. Se Top e' piu' veloce, e' ammesso il log:

```text
VISION_SIM_QUEUE_COALESCED|role=top|...
```

6. Verificare che non compaiano recovery ripetute con la sola motivazione
   `Inspection pair processing stalled` o `Side camera heartbeat missing` entro
   i normali 30 s della simulazione.

## Interpretazione anomalie

| Log | Significato | Azione |
|---|---|---|
| `VISION_SIM_QUEUE_COALESCED` | Un job sintetico e' piu' veloce dell'altro | Normale in sviluppo; confrontare i tempi tool |
| `COMPANION_TIMEOUT` dopo circa 30 s | Il companion non ha prodotto alcun risultato utile | Eseguire il job da solo e controllare RunStatus/script/tool |
| `Continuous run ... jobs are no longer running` | Un job VisionPro si e' fermato, non e' un semplice ritardo | Leggere l'errore del job e il RunStatus completo |
| `synthetic=False` con VPP atteso sintetico | Almeno un job usa una FIFO non sintetica o non risolta | Controllare AcqFifo e FrameGrabber di tutti i job |
| `No secondary inspection tool block initialized` | Il risultato companion arriva, ma manca il ToolBlock atteso | Verificare il ToolBlock `Results` e il mapping ruolo |

## Limite della simulazione

L'ordine FIFO permette di collaudare UI, validazioni e stabilita' delle code, ma
non dimostra la sincronizzazione fisica dello stesso prodotto. Il collaudo finale
di pairing prodotto, trigger, encoder e scarto deve essere eseguito con camere e
I/O reali.
