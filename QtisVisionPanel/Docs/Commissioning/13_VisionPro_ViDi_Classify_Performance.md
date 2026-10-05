# VisionPro ViDi EL Classify Performance

## Scopo

Questa procedura serve a confrontare in modo corretto il tempo di un
`CogClassifyTool` ViDi EL tra VisionPro QuickBuild e QtisVisionPanel.

Il confronto deve usare lo stesso PC, lo stesso VPP, la stessa immagine e lo
stesso modello. Un tempo di pochi millisecondi in QuickBuild e di diversi
secondi nella HMI non e' una normale differenza hardware: indica contesa CPU,
debugger, risoluzione runtime o configurazione di processo.

## Correzione runtime 3.1.0.5

La baseline storica limitava l'intero processo HMI ai soli logical processor 0
e 1 (`ProcessorAffinity=0x0003`). Nello stesso tempo impostava 24 thread minimi
del ThreadPool su un PC da 12 logical processor. VisionPro, UI e servizi macchina
erano quindi costretti a contendersi due soli processori.

La release 3.1.0.5:

1. non forza piu' l'affinita' del processo;
2. lascia a Windows tutti i logical processor disponibili;
3. mantiene un minimo ThreadPool non inferiore al numero di processori, senza
   raddoppiarlo arbitrariamente;
4. registra la configurazione effettiva nel log:

```text
VISION_RUNTIME_SCHEDULER|logicalProcessors=12|affinity=0xFFF|threadPoolMinWorker=12|threadPoolMinIo=12|updated=True
```

Il valore dell'affinita' dipende dal PC. Tutti i bit disponibili devono essere
abilitati; su un PC da 12 logical processor il valore atteso e' `0xFFF`.

## Evidenza del benchmark

Sul PC di sviluppo i7-1355U, con `Lucart_Test_sample.vpp` e la stessa istanza di
`CogClassifyTool`:

| Prova | Affinita' | Tempo classificatore caldo |
|---|---:|---:|
| Runtime libero | 12 logical processor (`0xFFF`) | 11-25 ms |
| Vecchia HMI | 2 logical processor (`0x003`) | 489-628 ms |

Il job Side completo sintetico include anche acquisizione e altri tool: circa
279 ms con tutti i core e 560-790 ms con due core. Il primo ciclo puo' richiedere
1-2 s per inizializzare modello e cache e non deve essere usato come misura di
regime.

Nella HMI completa la vecchia restrizione era ancora piu' penalizzante, perche'
Top, Side, rendering, DB e servizi condividevano gli stessi due processori. Da
qui erano possibili tempi osservati di 6-8 s.

## Collaudo corretto

1. Chiudere QuickBuild e altre applicazioni che usano VisionPro.
2. Compilare e avviare `Release x64` senza debugger (`Ctrl+F5` se si usa Visual
   Studio).
3. Verificare `VISION_RUNTIME_SCHEDULER` nel log.
4. Controllare in Gestione attivita' che `QtisVisionPanel.exe` possa usare tutti
   i processori.
5. Eseguire almeno 10 cicli del classificatore; escludere il primo warm-up.
6. Confrontare la mediana dei cicli 2-10 con QuickBuild usando la stessa immagine.
7. Separare sempre il tempo del solo `CogClassifyTool` dal tempo totale del job e
   dal ritardo tra Top e Side.

## Se resta lento

| Verifica | Interpretazione / azione |
|---|---|
| Release senza debugger e' veloce | Il rallentamento e' dovuto al debugger o alle first-chance exception Cognex; usare Debug solo per diagnosi mirata |
| Solo il primo ciclo e' lento | Warm-up del modello; eseguire un ciclo tecnico prima della produzione, senza aggiornare contatori |
| Tutti i cicli restano lenti | Controllare carico CPU, piano energia, thermal throttling e processi concorrenti |
| `affinity=0x3` | Una policy esterna o un launcher sta ancora limitando il processo |
| Moduli ONNX da directory HMI root | Installazione non conforme; il runtime Microsoft deve restare in `AiRuntime`, quello Cognex in `VisionProDependencies` |
| QuickBuild e HMI aperti insieme | Chiudere QuickBuild durante la misura: condividono CPU, licenza e runtime Cognex |
| Immagine/modello differenti | Ripetere il test con lo stesso VPP e la stessa immagine di input |

## Criterio di accettazione

Sul PC di sviluppo, a carico stabile e senza debugger, il classificatore nella
HMI deve restare nello stesso ordine di grandezza di QuickBuild. Per il VPP di
questa prova il riferimento e' inferiore a 50 ms dopo il warm-up. Il valore
finale di qualifica va comunque registrato sul PC macchina previsto per la
produzione.

Questa correzione non modifica modello, soglia, output, esito prodotto, ricetta,
DB, I/O, encoder, trigger o scarto.
