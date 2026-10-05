# Output Macchina, Trigger Per Ricetta E Precisione Tracking

## Scopo

Questa guida descrive il commissioning introdotto dalla release `3.1.1.8` per:

- salvare e rileggere in modo verificabile le uscite macchina;
- assegnare scheda, canale e polarita' direttamente dall'Intervention Point Editor;
- abilitare o disabilitare i trigger camera per singola ricetta;
- misurare la precisione encoder-trigger e riconoscere i limiti del polling software.

## Confine Dei Dati

| Livello | Dati | Persistenza |
|---|---|---|
| Macchina | signal code, scheda, canale DO, polarita', quota base, trim, pulse ms | file macchina attivo indicato dalla HMI |
| Ricetta | modo trigger `Machine`, `Enabled` o `Disabled`, offset prodotto | XML ricetta |
| Runtime | punti effettivi ottenuti dalla fusione macchina + ricetta | solo memoria |

La ricetta non contiene mai la scheda o il canale fisico. Questo evita che il cambio prodotto
rimappi accidentalmente un'uscita reale.

## Configurazione Uscita Dall'Intervention Point Editor

1. Fermare la macchina e aprire `Configuration > PCIe settings > Machine Setup`.
2. Selezionare o creare il punto, per esempio `CAMERA_TRIGGER_REAR`.
3. Impostare `Action = TriggerCamera` e il `Signal code` desiderato.
4. Nella sezione `Global output mapping` scegliere scheda, canale e polarita'.
5. Attivare `Use physical signal` soltanto per un canale realmente cablato.
6. Impostare quota, trim e durata impulso.
7. Premere `Save configuration`.

Se il signal code non esiste nella tabella Machine Outputs, il primo campo di mapping compilato
crea automaticamente una riga Output/CameraTrigger. Le nuove righe create direttamente dalla
tabella partono come `Output`, `CameraTrigger`, `ActiveHigh` e segnale fisico, quindi non vengono
nascoste dal filtro `Only real machine signals`.

Al salvataggio la HMI:

- chiude la riga DataGrid ancora in modifica;
- elimina soltanto le righe completamente vuote;
- normalizza signal code, scheda e canale;
- salva atomicamente il file macchina;
- ricarica il file e verifica ogni uscita scritta.

Se il read-back non corrisponde, il salvataggio viene segnalato come errore e non deve essere
considerato concluso.

## Trigger Per Ricetta

In Recipe Management, sezione `Camera triggers for this recipe`, ogni camera configurata espone:

- `Use machine setting`: conserva lo stato Enabled globale;
- `Enabled for this recipe`: abilita il punto nella copia runtime;
- `Disabled for this recipe`: disabilita il punto nella copia runtime.

Esempio XML:

```xml
<machineRuntimeAdjustments>
  <CameraTriggers>
    <Top>Enabled</Top>
    <Side>Enabled</Side>
    <Left>Machine</Left>
    <Front>Machine</Front>
    <Right>Machine</Right>
    <Rear>Disabled</Rear>
    <Bottom>Disabled</Bottom>
  </CameraTriggers>
</machineRuntimeAdjustments>
```

Le ricette precedenti che non contengono `CameraTriggers` vengono caricate con tutti i valori
`Machine`: il comportamento precedente resta invariato.

Se una ricetta disabilita il punto camera associato a un profilo MultiShot, il profilo viene
disabilitato anche nella configurazione runtime. Il software non deve generare una sequenza
MultiShot su una camera esclusa dal prodotto.

## Miglioramenti Del Timing

Il ciclo I/O usa ora un target di polling encoder di `2 ms` invece di `10 ms`. Inoltre:

- lo stato dell'abilitazione trigger esterna viene mantenuto in cache e non riletto fisicamente
  quando il prodotto ha gia' raggiunto la quota;
- la transizione hardware HIGH viene eseguita prima dei log applicativi;
- le scritture contemporanee sulla stessa scheda sono serializzate per evitare la perdita di bit
  quando due canali dello stesso byte cambiano insieme;
- in simulazione il file di stato viene scritto al massimo una volta al secondo;
- un ciclo I/O di almeno `10 ms` genera il log limitato `IO_POLL_OVERRUN`.

Questi cambi riducono latenza e jitter, ma Windows, `Task.Delay`, BDaq e il polling software non
costituiscono un sistema hard real-time. Per precisione deterministica sub-millimetrica ad alta
velocita' il passo successivo e' usare compare/latch hardware encoder o un trigger motion/PLC.

## Collaudo Persistenza

1. Eseguire un backup dalla pagina `Backup / restore`.
2. Aggiungere un'uscita di test su un canale libero e salvarla.
3. Premere `Reload`, cambiare pagina e riavviare l'applicazione.
4. Verificare signal code, scheda, canale, categoria, polarita' e flag fisico.
5. Ripetere con il filtro `Only real machine signals` attivo.
6. Verificare nel log la riga `I/O configuration saved and verified` con il path effettivo.

Esito atteso: la riga resta visibile e identica dopo tutti e tre i reload. Non devono apparire
errori `did not pass save/read-back verification`.

## Collaudo Ricette 2/3/4 Camere

Preparare almeno due ricette di prova:

| Ricetta | TOP | LEFT/SIDE | REAR | BOTTOM |
|---|---:|---:|---:|---:|
| Prodotto A | Enabled | Enabled | Disabled | Disabled |
| Prodotto B | Enabled | Enabled | Enabled | Enabled o Machine |

Per ogni ricetta:

1. salvare, uscire dalla pagina e ricaricare la ricetta;
2. avviare a bassa velocita' con oscilloscopio o ingressi diagnostici collegati alle DO;
3. verificare che partano soltanto i trigger abilitati;
4. verificare che le viste disabilitate non generino attese companion o MultiShot;
5. controllare il caricamento di una vecchia ricetta: tutti i modi devono risultare `Machine`.

## Collaudo Precisione Encoder

Eseguire almeno 100 prodotti alla velocita' massima prevista, senza modificare quota o ricetta.
Raccogliere dalle righe `Trigger request executed`:

- `target`;
- `actual`;
- `late` in counts;
- ritardo convertito in mm.

Calcolare media, percentile 95 e massimo per ogni camera. Come obiettivo iniziale di commissioning
a 40 m/min si puo' usare:

- nessun trigger perso o duplicato;
- percentile 95 del ritardo non superiore a `3 mm`;
- massimo non superiore a `5 mm`;
- nessuna sequenza ripetuta di `IO_POLL_OVERRUN`.

Le soglie finali devono essere inferiori alla tolleranza ottica ammessa dal campo visivo. Se il
ritardo resta variabile oltre queste soglie, non compensarlo con un offset ricetta: un offset
corregge un errore costante, non il jitter. In quel caso acquisire una traccia encoder/DO e passare
al trigger hardware compare.

## Rollback

- ripristinare il backup del file macchina dalla pagina `Backup / restore`;
- impostare tutti i nuovi modi ricetta su `Machine`, oppure usare la ricetta XML precedente;
- non copiare mapping fisici da una ricetta a un'altra;
- in caso di instabilita' del polling conservare i log `IO_POLL_OVERRUN` e le righe con
  `target/actual/late` prima di tornare alla release precedente.
