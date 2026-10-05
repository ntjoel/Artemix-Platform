# Source Code Commenting Guide - 2026-05-28

Revisione 2026-07-21: l'audit completo ha applicato questa guida rimuovendo copie commentate e
note che descrivevano codice non piu' esistente. Il report e' in
`full-application-audit-2026-07-21.md`.

Questa nota spiega come commentare il codice di `QtisVisionPanel` senza rendere il progetto piu' difficile da mantenere.

Il software e' una baseline di produzione: i commenti devono aiutare manutentori, collaudatori e sviluppatori a capire flussi critici, non descrivere ogni riga ovvia.

## Regola pratica

Commentare quando il codice risponde a una domanda importante:

- perche' questa scelta e' necessaria in macchina;
- quale vincolo di sicurezza o runtime protegge;
- quale parte e' macchina, ricetta o runtime;
- quale fallback esiste quando DB, I/O, VisionPro o OPC UA non sono disponibili;
- perche' non si puo' semplificare un flusso senza cambiare comportamento.

Evitare commenti come:

- `incrementa contatore` sopra `counter++`;
- `assegna valore` sopra una proprieta';
- spiegazioni che ripetono il nome del metodo;
- commenti storici non piu' veri.

## Aree commentate in questa passata

### Runtime e shell macchina

File:

- `MainWindow.xaml.cs`

Commenti aggiunti su:

- gestione comandi OPC UA Start/Stop/Ricetta;
- handshake OPC UA dopo comando esterno;
- uscita fisica allarme e popup operatore;
- pubblicazione snapshot ispezione verso OPC UA.

Motivo:

- `MainWindow` e' ancora l'orchestratore runtime piu' delicato. Qui i commenti devono spiegare perche' i comandi passano dal dispatcher, perche' lo Stop remoto diventa hold manuale e perche' il popup allarme resta indipendente dal fallimento I/O.

### OPC UA

File:

- `Services/OpcUaClientService.cs`
- `Services/OpcUaConfigurationService.cs`

Commenti aggiunti su:

- differenza tra `Enabled` e `RequiredForMachineRun`;
- endpoint/security/certificati;
- handshake comandi;
- config OPC UA separata dal `Config.xml` principale;
- fallback solo per startup/recovery.

Motivo:

- la comunicazione esterna non deve diventare automaticamente un vincolo macchina. Il parametro `RequiredForMachineRun` e' la separazione esplicita tra monitoraggio e interlock.

### Ricette

File:

- `ViewModels/RecipeManagerViewModel.cs`
- `Services/RecipeTransitionService.cs`

Commenti aggiunti su:

- ricetta in produzione sempre in alto;
- refresh lista dopo cambio esterno da OPC UA/MES;
- sort operatore;
- cambio ricetta con rollback;
- ripristino ricetta precedente tramite lo stesso flusso runtime.

Motivo:

- il cambio ricetta tocca VisionPro, config, UI e produzione. I commenti servono a evitare fix parziali che cambiano solo una stringa di configurazione.

### Contatori e difetti

File:

- `Database/CounterManager.cs`

Commenti aggiunti su:

- differenza tra contatori storici e messaggi ultimo difetto;
- pulizia messaggi a inizio ciclo;
- pulizia messaggi quando il pezzo e' conforme;
- salvataggio asincrono per non bloccare ispezione/UI.

Motivo:

- il numero del contatore puo' restare alto, ma il messaggio rosso deve descrivere solo il difetto corrente. Questa distinzione e' essenziale per l'operatore.

### Allarmi scarto

File:

- `Services/IntegratedAlarmCardService.cs`
- `Models/EjectionAlarmManager.cs`

Commenti aggiunti su:

- ponte tra DB/XML e manager runtime;
- acknowledge come reset dello stato runtime, non solo chiusura popup;
- reset contatori consecutivi e buffer percentuale;
- differenza tra allarmi consecutivi e allarmi percentuali.

Motivo:

- se ACK non pulisce anche la memoria runtime dell'allarme, il popup puo' ripresentarsi subito al difetto successivo.

### Autorizzazioni

File:

- `Services/AuthorizationService.cs`

Commenti aggiunti su:

- servizio come gate centrale;
- mappa vista -> feature DB;
- blocco hard safety per Operator su funzioni tecniche.

Motivo:

- la UI non deve decidere permessi in modo sparso. Le pagine operative e i comandi pericolosi devono passare da un punto coerente.

## Prossime aree consigliate

Per completare gradualmente la commentatura utile del software, seguire questo ordine:

1. `Cls_Config/AsyncConfigManagerXml.cs`
2. `Cls_Config/AsyncRecipeParam.cs`
3. `Database/Cls_InitializzeDb.cs`
4. `Database/AlarmCardRepository.cs`
5. `Database/OpcUaConfigurationRepository.cs`
6. `Services/MachineRuntimeService.cs`
7. `Services/ConfigurationRecoveryService.cs`
8. `Services/PowerFlex525Service.cs`
9. `ViewModels/DigitalIOViewModel.cs`
10. `ViewModels/JobToolEditorViewModel.cs`
11. `Inspector/*`

## Criterio di chiusura

Una passata di commenti e' valida quando:

- non cambia il comportamento runtime;
- non introduce path o default nuovi;
- non nasconde warning o log importanti;
- chiarisce un vincolo reale;
- aiuta una persona nuova a non rompere il flusso macchina.
