# Audit completo applicazione - 2026-07-21

## Scopo

Questo documento fotografa la baseline `QtisVisionPanel` dopo una revisione statica completa del
progetto principale. L'obiettivo non e' riscrivere il runtime macchina, ma ridurre ambiguita',
codice morto e documentazione duplicata senza cambiare il contratto di produzione.

Aggiornamento runtime `2026-09-02` (`3.1.2.0`): il rischio residuo di pairing multi-camera basato
solo su FIFO/timestamp e' stato ridotto con ticket `ProductId` per i trigger HMI. Rendering display
e conversione immagini non fanno piu' parte del percorso seriale dell'esito. Resta obbligatorio il
collaudo hardware, perche' Windows e DAQNavi non costituiscono un sistema hard real-time.

La revisione ha verificato:

- file C# e XAML inclusi nel progetto;
- riferimenti a classi, metodi, converter, handler e modelli;
- percorso ricetta, ispezione, contatori, VisionPro, I/O, encoder, OPC UA e AI advisory;
- warning della build Release x64;
- documentazione tecnica corrente e manuale mostrato nell'HMI;
- separazione obbligatoria tra configurazione macchina, ricetta e stato runtime.

## Punto di ripristino

Prima della pulizia e' stato creato il tag Git annotato:

`pre-industrial-audit-3.0.7.7-20260721`

Il tag permette di confrontare o ripristinare esattamente la baseline precedente all'audit.

## Inventario verificato

| Elemento | Quantita' dopo la pulizia | Nota |
|---|---:|---|
| File C# applicativi | 212 | esclusi `bin`, `obj`, `.vs`, copie e archivi |
| File XAML | 38 | viste e risorse WPF |
| File Markdown | 165 | inclusi documenti storici e archivio |
| Progetti test automatici | 0 | lacuna da chiudere prima dei refactor strutturali |
| `async void` reali | 17 | startup, eventi WPF/timer/SDK e `SafeFireAndForget` |
| `Thread.Sleep` runtime | 3 | tutti nel lifecycle Cognex/VisionPro |
| `GC.Collect` runtime | 1 | solo nello shutdown deliberato |
| `catch` vuoti | 94 | prevalentemente cleanup UI/SDK; da classificare e ridurre |

Le classi piu' grandi restano:

| File | Righe circa | Valutazione |
|---|---:|---|
| `ViewModels/DigitalIOViewModel.cs` | 7.493 | responsabilita' I/O, encoder, tracking e commissioning da estrarre gradualmente |
| `MainWindow.xaml.cs` | 5.067 | ancora coordinatore troppo ampio del ciclo macchina |
| `ViewModels/JobToolEditorViewModel.cs` | 2.610 | lifecycle VisionPro e UI tecnica molto accoppiati |
| `ViewModels/RecipeManagerViewModel.cs` | 2.498 | gestione ricetta, DB e UI ancora concentrata |
| `Services/SystemDiagnosticsService.cs` | 1.971 | diagnostica ampia ma separata dal ciclo produttivo |

## Pulizia eseguita

Sono stati rimossi solo elementi dimostrabilmente non usati: file non referenziati, classi mai
istanziate, metodi privati senza chiamanti, handler senza collegamento XAML e blocchi storici gia'
commentati. Non sono state eliminate API pubbliche o callback risolte dinamicamente da VisionPro,
WPF, serializzazione XML o database quando non era possibile provarne l'inutilizzo.

### File rimossi

- `Cls_Vpro/displayCounter.cs`
- `Cls_Vpro/GlobalCounterAdapter.cs`
- `Inspector/ViewModels/InspectionResultBadgeViewModel.cs`
- `Converters/EjectionStatusToItemsConverter.cs`
- `Events/Event_Window.cs`
- `Events/CounterEvents.cs`
- `DataManage/RecipeValidator.cs`
- `Models/ExternalButtonItem.cs`

### Riduzioni interne principali

- converter WPF non referenziati rimossi da `BooleanConverters` e
  `AlarmCardAdditionalConverters`;
- modelli contatore duplicati/non istanziati rimossi;
- vecchio percorso Advantech completamente commentato rimosso;
- vecchi metodi di aggiornamento contatori/UI non chiamati rimossi da `InspectionProcessor`;
- comandi di navigazione, pulsanti esterni e handler ricetta non collegati rimossi;
- metodi privati senza chiamanti rimossi da `MainWindow`, `DigitalIOViewModel`, camera access,
  allarmi e validatori;
- commenti che descrivevano implementazioni gia' eliminate sostituiti da note brevi sugli
  invarianti ancora validi.

Il commit elimina 2.928 righe e ne aggiunge 880, con una riduzione netta di 2.048 righe. Due
documenti tecnici (1.894 righe complessive) sono stati spostati senza perdita nell'archivio e Git li
riconosce come rename, quindi non vengono contati come codice eliminato.

## Correzione funzionale emersa dall'audit

Durante il caricamento ricetta `MainWindow` chiamava
`CounterManager.ProcessInspectionAsync(true, null)` come test di inizializzazione. La chiamata
registrava una vera ispezione conforme e incrementava `Total` e `Good` anche senza passaggio di un
prodotto.

La chiamata e' stata rimossa. La regola corrente e':

`un prodotto finalizzato dal gruppo ispezione -> un solo aggiornamento dei contatori produzione`

Il caricamento ricetta aggiorna la UI leggendo lo stato dei contatori, ma non crea piu' un esito.

## Build e warning

Build verificata:

`MSBuild QtisVisionPanel.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64`

Risultato finale della release `3.0.7.8`:

- 0 errori;
- 38 warning sorgente unici;
- 22 `CS1998`: contratti `Task` legacy che oggi completano sincronicamente;
- 16 `CS0618`: API obsolete Advantech e OPC UA da migrare con test dedicati;
- 0 task ignorati `CS4014`.

I warning `CS1998` non vengono convertiti in blocco: alcuni metodi implementano contratti async o
mantengono compatibilita' tra simulazione e hardware. I warning Advantech/OPC UA richiedono una
prova su scheda/server reale prima di sostituire le API.

## Commenti e leggibilita'

La revisione ha adottato queste regole:

- commentare il motivo, il vincolo hardware o la scelta di sicurezza, non tradurre ogni riga;
- rimuovere codice commentato quando Git conserva gia' la storia;
- mantenere espliciti i confini `macchina`, `ricetta`, `runtime`;
- usare nomi evento stabili nei log per i flussi difficili da osservare;
- non trasformare callback WPF/SDK in normali metodi `Task` senza verificare il contratto del
  chiamante.

Non e' utile aggiungere commenti a ogni istruzione: aumenterebbe il rumore e renderebbe piu'
difficile individuare le note davvero importanti per chi fa manutenzione.

## Manuale operatore

La pagina Manuale ora mostra solo i capitoli numerati `NN_*`. README, piani editoriali e
presentazioni tecniche non entrano piu' nel menu HMI.

I capitoli operativi sono stati uniformati al formato:

1. titolo della pagina;
2. immagine reale disponibile;
3. tabella con ID, campo/comando, significato e azione operatore;
4. sequenze passo-passo;
5. condizioni di arresto e richiesta assistenza.

I `.txt` duplicati sono stati rimossi: il Markdown e' la sorgente canonica. I documenti tecnici
che erano nella cartella operatore sono stati spostati nell'archivio tecnico.

## Confini preservati

### Macchina

Nessuna modifica a mapping I/O, board, channel, polarita', encoder, counts/mm, zero macchina,
quote standard, pulse o parametri globali MultiShot.

### Ricetta

Nessuna modifica allo schema ricetta o ai delta prodotto-specifici gia' introdotti per camera e
MultiShot.

### Runtime

La sola correzione comportamentale e' la rimozione dell'ispezione GOOD artificiale al caricamento
ricetta. Trigger, pairing, VisionPro, scarto, OPC UA e AI advisory restano invariati.

## Rischi residui verificati

1. Le cinque code `CameraResult` sono `ConcurrentQueue` non limitate. Non devono usare
   `drop-oldest`: un overflow deve fermare/segregare il prodotto, generare allarme e conservare la
   tracciabilita'.
2. I tre `Thread.Sleep` Cognex sono nel lifecycle job. Vanno sostituiti solo con test hardware di
   load, stop, unload e recovery VPP.
3. I 94 `catch` vuoti richiedono classificazione: cleanup best-effort, probing SDK e fallback UI
   non hanno la stessa criticita'. I punti del percorso produttivo devono almeno produrre log
   throttled o metriche.
4. La soluzione non ha test automatici. Ogni futura estrazione da `MainWindow` o
   `DigitalIOViewModel` deve iniziare da test di caratterizzazione.
5. L'accesso a `MainWindow.*` e `ServiceLocator` resta diffuso e limita testabilita' e ownership
   delle risorse.
6. Restano API obsolete Advantech e OPC UA: funzionano nella baseline corrente ma aumentano il
   costo di aggiornamento dipendenze.
7. Retention MySQL, TLS e timeout devono essere verificati per ogni installazione reale.

Il piano operativo per chiudere questi rischi e' in
`Documentation/improvement-plan-2026-07.md`.

## Esclusioni intenzionali

- Nessun refactor massivo del ciclo macchina.
- Nessuna cancellazione basata solo sul conteggio testuale di una classe pubblica.
- Nessuna modifica automatica a XML macchina/ricetta o schema DB.
- Nessuna modifica alla logica hardware senza banco prova.
- La modifica preesistente in `SaveImage/ISaveImage.cs` e' stata preservata e non fa parte di
  questo audit.

## Checklist collaudo release

- avvio con configurazione storica;
- caricamento ricetta senza incremento `Total/Good`;
- un prodotto GOOD incrementa una volta `Total` e `Good`;
- un prodotto NOK incrementa una volta `Total`, `No Good` e ogni difetto realmente presente;
- cambio ricetta manuale e OPC UA;
- RunContinuous, Stop e ritorno dal Job Editor;
- trigger TOP/SIDE e profili MultiShot configurati;
- ACK allarme e reset previsto dal tipo allarme;
- apertura Manuale, zoom e generazione PDF;
- verifica log, shutdown e riavvio;
- soak test con hardware prima di dichiarare la release adatta al 24/7.
