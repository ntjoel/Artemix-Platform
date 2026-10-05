# Presentazione completa QtisVisionPanel

Documento base per preparare una presentazione PowerPoint sull'applicazione completa, sul suo funzionamento, sui vantaggi, sui limiti e sui punti di forza.

Applicazione: `QtisVisionPanel`  
Contesto: HMI industriale per sistema di visione Pulsar  
Versione documentata: `1.1.9.0`  
Data: `2026-05-20`

---

## Come usare questo documento

Questo file e' pensato come materiale sorgente per costruire una presentazione.

Per ogni sezione sono presenti:

- **Messaggio slide**: frase sintetica da usare come idea principale.
- **Contenuto slide**: punti da mettere nella slide.
- **Note relatore**: testo piu' discorsivo da usare durante la spiegazione.

La presentazione puo' essere adattata a tre pubblici diversi:

- **Direzione / cliente**: usare soprattutto panoramica, benefici, punti di forza e limiti.
- **Produzione / operatori**: usare flusso operativo, ricette, allarmi, data inspector e sicurezza.
- **Tecnici / manutenzione / collaudo**: usare architettura, configurazione, I/O, VisionPro, database e commissioning.

---

# 1. Titolo e posizionamento

## Slide 1 - Titolo

**Messaggio slide**  
QtisVisionPanel e' il pannello HMI per controllare, monitorare e tracciare il sistema di ispezione visiva industriale Pulsar.

**Contenuto slide**

- QtisVisionPanel
- Sistema HMI per ispezione visiva industriale
- Controllo ricette, telecamere, risultati, allarmi, scarti e diagnostica
- Integrazione con Cognex VisionPro, I/O Advantech, MySQL e configurazione macchina

**Note relatore**  
L'applicazione non e' solo una schermata di visualizzazione. E' il punto centrale dove operatore, manutentore e tecnico vedono lo stato della macchina, controllano il runtime VisionPro, caricano ricette, leggono allarmi, analizzano dati e verificano la tracciabilita' dei pezzi.

## Slide 2 - Obiettivo dell'applicazione

**Messaggio slide**  
L'obiettivo e' rendere il controllo qualita' automatico piu' stabile, leggibile e tracciabile durante la produzione.

**Contenuto slide**

- Ispezione automatica dei prodotti in movimento
- Validazione visiva e dimensionale
- Rilevamento difetti e gestione scarto
- Tracciabilita' immagini, misure e risultati
- Supporto operativo per operatori e tecnici

**Note relatore**  
Il pannello serve a ridurre l'incertezza in produzione. L'operatore deve capire rapidamente se la macchina sta lavorando, quale ricetta e' attiva, se le telecamere stanno acquisendo, quanti pezzi sono buoni o scartati e quale difetto sta causando problemi.

---

# 2. Visione generale del sistema

## Slide 3 - Dove si colloca nel processo macchina

**Messaggio slide**  
QtisVisionPanel collega macchina fisica, sistema di visione, ricette prodotto e storico dati.

**Contenuto slide**

- Prodotto in ingresso
- Trigger hardware da encoder/fotocellula
- Acquisizione immagini da telecamere o sensore 3D
- Elaborazione Cognex VisionPro
- Validazione risultati e classificazione difetti
- Aggiornamento contatori, allarmi e scarto
- Salvataggio database e immagini

**Note relatore**  
Il flusso parte da un evento fisico sulla macchina. Il sistema acquisisce immagini, esegue le logiche VisionPro, interpreta i risultati, decide se il pezzo e' buono o no, aggiorna contatori e allarmi, e salva le informazioni utili per analisi successive.

## Slide 4 - Componenti principali

**Messaggio slide**  
Il pannello integra piu' sottosistemi industriali in un'unica interfaccia.

**Contenuto slide**

- HMI WPF su Windows
- Cognex VisionPro per acquisizione e ispezione
- DALSA / camere GigE o sensore profilometro Top3D
- Advantech per I/O digitale ed encoder
- MySQL per produzione, ricette, contatori e storico
- File XML per configurazione macchina e ricette
- NLog e ApplicationEventLogger per eventi e diagnosi

**Note relatore**  
Uno dei punti forti e' l'integrazione. L'operatore non deve aprire molti strumenti separati: stato macchina, ricetta, immagini, contatori, allarmi e dati storici sono disponibili dentro la stessa applicazione.

## Slide 5 - Regola fondamentale: macchina, ricetta, runtime

**Messaggio slide**  
Il sistema separa cio' che appartiene alla macchina, cio' che appartiene al prodotto e cio' che e' solo stato temporaneo.

**Contenuto slide**

- **Macchina**: I/O, encoder, trigger, quote, board, polarita', configurazione fisica
- **Ricetta**: dimensioni prodotto, tolleranze, difetti abilitati, delay camera, parametri visione
- **Runtime**: stato pannello, stato macchina, risultati live, sessione utente, allarmi temporanei

**Note relatore**  
Questa separazione e' importante per evitare regressioni. Un cambio prodotto non deve cambiare il cablaggio macchina. Una configurazione macchina non deve essere duplicata dentro ogni ricetta. Uno stato runtime non deve diventare un parametro persistente senza motivo.

---

# 3. Interfaccia operatore

## Slide 6 - Struttura del pannello

**Messaggio slide**  
La UI e' organizzata per dare all'operatore le informazioni principali sempre visibili.

**Contenuto slide**

- Top bar con stato macchina, ricetta, velocita', qualita', contatori e sessione utente
- Menu laterale per navigazione tra viste
- Area centrale per telecamere, ricette, allarmi, diagnostica e dati
- Pulsanti Start/Stop e comandi principali

**Note relatore**  
La top bar e' pensata come cruscotto costante. Anche cambiando pagina, l'operatore mantiene visibili informazioni critiche come stato macchina, ricetta attiva e contatori.

## Slide 7 - Modalita' produzione pulita

**Messaggio slide**  
La UI e' stata rinforzata per ridurre il rischio di errore da parte di utenti non tecnici.

**Contenuto slide**

- Viewer e Operator vedono solo le viste necessarie alla produzione
- Funzioni tecniche riservate a Expert, Installer o Administrator
- Comandi I/O critici richiedono ruolo tecnico, conferma e stop manuale
- Job Tool Editor protetto da runtime hold e prompt modifiche non salvate
- Login con history username locale, senza salvataggio password

**Note relatore**  
Un punto importante e' la separazione tra uso produttivo e uso tecnico. Un operatore puo' monitorare, cambiare ricetta se autorizzato, leggere allarmi e consultare manuali; non deve pero' avere accesso libero a configurazioni macchina o comandi hardware pericolosi.

## Slide 8 - Ruoli utente

**Messaggio slide**  
I ruoli limitano le funzioni disponibili in base alla responsabilita' dell'utente.

**Contenuto slide**

- Viewer: consultazione base, overview, statistiche, allarmi, manuale
- Operator: produzione, cambio ricetta autorizzato, allarmi, manuale
- Expert: modifica ricette, Job Editor, diagnostiche tecniche
- Installer: configurazione macchina, I/O, PowerFlex, commissioning
- Administrator: gestione completa e funzioni avanzate

**Note relatore**  
La logica dei ruoli serve sia alla sicurezza sia alla chiarezza. Un operatore non esperto non deve trovarsi davanti funzioni che possono fermare la macchina, modificare parametri critici o alterare il comportamento di scarto.

---

# 4. Flusso operativo in produzione

## Slide 9 - Prima dell'avvio

**Messaggio slide**  
Prima di produrre, l'operatore verifica ricetta, stato macchina, telecamere e assenza di allarmi bloccanti.

**Contenuto slide**

- Ricetta corretta caricata
- Stato macchina non bloccato
- VisionPro pronto
- Immagini camera visibili e aggiornate
- Contatori azzerati o coerenti con il turno
- Nessun allarme critico non gestito

**Note relatore**  
Il sistema fornisce informazioni per evitare partenze alla cieca. Se la ricetta e' sbagliata, se VisionPro non riceve risultati o se un allarme e' ancora attivo, la produzione puo' generare scarti o dati non affidabili.

## Slide 10 - Avvio e stop produzione

**Messaggio slide**  
Start e Stop controllano il runtime VisionPro e lo stato continuo della macchina.

**Contenuto slide**

- Start: richiesta di RunContinuous
- Stop: stop intenzionale e hold manuale
- Stato visibile in top bar
- Recovery automatico solo dove previsto
- Hold intenzionali non vengono interpretati come guasti

**Note relatore**  
Il sistema distingue uno stop voluto da un arresto inatteso. Questo e' essenziale: se l'operatore ha premuto Stop o se il Job Editor ha messo la macchina in hold, il pannello non deve riavviare automaticamente come se fosse un errore.

## Slide 11 - Ciclo di ispezione

**Messaggio slide**  
Ogni prodotto genera un ciclo completo: trigger, immagine, validazione, decisione e salvataggio.

**Contenuto slide**

- Trigger da encoder/fotocellula
- Acquisizione camera
- Esecuzione job VisionPro
- Lettura risultati
- Merge risultati Top, Side, Front o Top3D
- Aggiornamento contatori
- Attivazione allarmi/scarto se necessario
- Salvataggio record e immagini

**Note relatore**  
Il cuore del sistema e' la trasformazione di un evento fisico in una decisione di qualita'. La decisione deve essere veloce, coerente con la ricetta e tracciata nel database.

---

# 5. Telecamere e controlli qualita'

## Slide 12 - Ruoli camera

**Messaggio slide**  
Il sistema supporta piu' ruoli camera e li risolve dal runtime reale.

**Contenuto slide**

- Top: logo, centratura stampa, alette, forma superiore, superficie
- Side: altezza, sigillatura, forma laterale
- Front: tracciabilita' e codice
- Top3D: altezza, larghezza, lunghezza con profilometro
- Rear e Bottom: predisposizioni opzionali

**Note relatore**  
La logica moderna non si basa solo su un ordine fisso di camere. Il pannello legge i job VisionPro attivi e ne ricava il ruolo semantico. Questo riduce errori quando una macchina usa Top + Front invece di Top + Side, oppure quando il job principale e' Top3D.

## Slide 13 - VisionPro come motore di ispezione

**Messaggio slide**  
Cognex VisionPro esegue i tool di visione, mentre il pannello orchestra, mostra e storicizza i risultati.

**Contenuto slide**

- File VPP come progetto ispezione
- Job semantici: Top, Side, Front, Top3D
- ToolBlock per calcolo risultati
- Output nominati per misure e stati
- Display con record e overlay
- Runtime restart/reload controllato durante cambio ricetta

**Note relatore**  
VisionPro resta il motore di ispezione. Il pannello non sostituisce gli algoritmi VisionPro, ma li controlla, li collega alla ricetta e ne rende i risultati disponibili all'operatore e al database.

## Slide 14 - Controllo Top3D

**Messaggio slide**  
La variante Top3D integra misure 3D nel flusso standard di produzione e tracciabilita'.

**Contenuto slide**

- Job semantico Top3D
- Misure: altezza, larghezza, lunghezza
- Parametri dedicati in ricetta `recipeParamTop3D`
- Salvataggio valori misurati in `tblgenerale`
- Possibile archiviazione 2D render, 3D range e point cloud CSV
- Dashboard con trend 3D

**Note relatore**  
Top3D non e' trattato come sistema esterno separato. E' integrato nel ciclo di ispezione, nei contatori, negli allarmi, nella ricetta e nello storico dati.

---

# 6. Gestione ricette

## Slide 15 - Perche' le ricette sono centrali

**Messaggio slide**  
La ricetta descrive il prodotto da controllare e le soglie con cui giudicarlo.

**Contenuto slide**

- Dimensioni e tolleranze
- Abilitazione difetti
- Parametri camera e trigger delay
- Soglie Top, Side, Front e Top3D
- Immagine prodotto e informazioni operative
- Archivio ricetta nel database di produzione

**Note relatore**  
Cambiare ricetta significa cambiare il criterio di qualita'. Il sistema protegge questo passaggio per evitare che parametri di un prodotto restino attivi su un altro prodotto.

## Slide 16 - Cambio ricetta in produzione

**Messaggio slide**  
Il cambio ricetta ferma e riallinea il runtime in modo controllato.

**Contenuto slide**

- Hold runtime durante save/load
- Stop job VisionPro
- Salvataggio XML ricetta
- Reinizializzazione runtime
- Applicazione trigger delay
- Restart RunContinuous se la macchina era in automatico

**Note relatore**  
Il cambio ricetta e' delicato perche' coinvolge file XML, VisionPro, camera delay, contatori e UI. Il pannello usa hold intenzionali per non lasciare VisionPro in uno stato intermedio.

## Slide 17 - Permessi sulle ricette

**Messaggio slide**  
Consultare, caricare e modificare una ricetta sono privilegi diversi.

**Contenuto slide**

- Operator puo' caricare in produzione solo se autorizzato
- Modifica e salvataggio riservati a ruoli tecnici
- Tooltip e banner spiegano perche' un comando e' disabilitato
- Eliminazione e duplicazione ricette protette da permessi specifici

**Note relatore**  
Questa separazione riduce il rischio che l'operatore modifichi accidentalmente tolleranze o parametri VisionPro. Il cambio ricetta resta possibile per produzione, ma la modifica tecnica resta protetta.

---

# 7. Allarmi, scarto e contatori

## Slide 18 - Sistema allarmi scarto

**Messaggio slide**  
Gli allarmi trasformano i difetti rilevati in eventi operativi e, se previsto, in segnali fisici di scarto.

**Contenuto slide**

- Allarmi per difetti consecutivi, percentuali o condizioni specifiche
- Distinzione blocking e non-blocking
- Popup operatore con Acknowledge
- Stato allarme visibile nella pagina Allarmi Scarto
- Integrazione con uscita fisica configurata

**Note relatore**  
Gli allarmi non sono solo messaggi. Possono influire su uscite fisiche, scarto e comportamento macchina. Per questo la configurazione e la gestione dell'Acknowledge devono essere chiare.

## Slide 19 - Contatori produzione

**Messaggio slide**  
I contatori traducono il flusso pezzi in indicatori immediati di qualita' e prestazione.

**Contenuto slide**

- Totale pezzi
- Good
- No Good
- Contatori per difetto
- Quality index
- Defect distribution
- Filtri coerenti con ispezioni abilitate

**Note relatore**  
Un buon pannello industriale deve far capire subito se il problema e' generale o legato a un difetto specifico. I contatori per difetto aiutano a indirizzare manutenzione e regolazioni macchina.

## Slide 20 - Event Monitor

**Messaggio slide**  
Il monitor eventi e' la memoria operativa del pannello.

**Contenuto slide**

- Eventi macchina
- Stato VisionPro
- Stato database
- Allarmi e warning
- Operatore e ricetta attivi
- Supporto a diagnosi e assistenza

**Note relatore**  
Quando qualcosa non funziona, l'Event Monitor e' la prima pagina da aprire. Aiuta a capire se il problema riguarda visione, database, ricetta, I/O, autorizzazioni o configurazione.

---

# 8. Diagnostica e manutenzione

## Slide 21 - Diagnostica I/O ed encoder

**Messaggio slide**  
La diagnostica I/O aiuta commissioning e manutenzione senza sostituire la logica runtime principale.

**Contenuto slide**

- Visualizzazione input e output digitali
- Test controllati su segnali
- Configurazione encoder e quote
- Tracking prodotto tramite fotocellula + encoder
- Comandi critici protetti da ruolo tecnico e stop manuale

**Note relatore**  
La pagina I/O e' potente e per questo va protetta. Serve a collaudare cablaggi, trigger, quote e uscite, ma non deve essere liberamente usata durante produzione da utenti non esperti.

## Slide 22 - PowerFlex 525

**Messaggio slide**  
La pagina PowerFlex permette diagnosi e regolazione inverter senza rendere la produzione dipendente dal drive.

**Contenuto slide**

- Lettura frequenza, corrente, tensione, DC bus e potenza
- Diagnostica fault
- Calcolo velocita' nastro con fattore m/min per Hz
- Parametri editabili protetti
- Storico modifiche locale e database
- Se il drive non risponde, la macchina non viene bloccata

**Note relatore**  
La pagina e' utile per manutenzione e collaudo. La filosofia e' robusta: se l'inverter non risponde via rete, la vista segnala il problema ma non ferma il ciclo macchina.

## Slide 23 - Job Tool Editor

**Messaggio slide**  
Il Job Tool Editor permette interventi tecnici VisionPro mantenendo protetto il runtime macchina.

**Contenuto slide**

- Accesso riservato a ruoli tecnici
- Hold runtime quando l'editor e' aperto
- Banner operativo di macchina ferma per modifica job
- Prompt uscita con modifiche non salvate
- Ripristino RunContinuous quando si esce, salvo Stop manuale

**Note relatore**  
Modificare tool VisionPro mentre la macchina produce e' rischioso. Per questo il pannello esplicita l'hold, guida l'uscita dalla pagina e prova a ritornare allo stato precedente quando la modifica e' terminata.

---

# 9. DataInspector e analisi dati

## Slide 24 - DataInspector

**Messaggio slide**  
DataInspector consente di rivedere pezzi scartati e immagini associate direttamente dentro l'HMI.

**Contenuto slide**

- Lista pezzi recenti scartati
- Collegamento record database e cartella immagini
- Immagini raw/source
- Immagini processate/inspected
- Zoom fullscreen delle immagini per controllare piccoli difetti
- Download pacchetto evidenza con immagini e dettaglio pezzo
- Dettaglio esito e difetti
- Vista read-only sicura per supporto e produzione

**Note relatore**  
Prima era piu' difficile collegare un difetto a una prova visiva. Con DataInspector, il supporto puo' vedere rapidamente il pezzo, le immagini e il contesto di ispezione senza modificare dati o ricette. Da release `3.0.1.1`, le immagini si possono aprire in zoom fullscreen e scaricare come evidenza per qualita' o assistenza.

## Slide 25 - Data Analysis Dashboard

**Messaggio slide**  
La dashboard dati trasforma lo storico produzione in indicatori leggibili.

**Contenuto slide**

- Volume produzione Total, Good, NoGood
- Distribuzione difetti
- Trend misure
- Trend 3D altezza, larghezza e lunghezza
- Filtro per data e ricetta
- Refresh manuale e automatico

**Note relatore**  
La dashboard non influenza la macchina. Legge il database e aiuta a capire andamento qualita', derive misura e concentrazione difetti nel tempo.

## Slide 26 - Tracciabilita'

**Messaggio slide**  
Il sistema conserva il legame tra ricetta, pezzo, immagine, misura, difetto e produzione.

**Contenuto slide**

- Record produzione in MySQL
- Snapshot ricetta associato alla produzione
- Cartelle immagini per pezzo
- Misure e flag NC
- Eventi e allarmi storicizzati
- Supporto a indagine post-produzione

**Note relatore**  
La tracciabilita' e' un valore forte per qualita' e assistenza. Quando c'e' un problema, non si guarda solo il numero di scarti: si puo' risalire al pezzo, alla ricetta e alle immagini.

---

# 10. Configurazione, affidabilita' e recovery

## Slide 27 - Configurazione guidata da XML

**Messaggio slide**  
La configurazione macchina resta guidata dal `Config.xml`, evitando path e valori hardcoded.

**Contenuto slide**

- Configurazione runtime centralizzata
- Path immagini e database letti dal config
- PowerFlex configurato da sezione dedicata
- Output VisionPro e I/O risolti da configurazione
- Ricette separate dalla configurazione macchina

**Note relatore**  
Il principio e' importante: non si deve ricompilare il software per cambiare un path macchina o un endpoint configurabile. Questo rende l'applicazione piu' adattabile a macchine e installazioni diverse.

## Slide 28 - Persistenza e recovery

**Messaggio slide**  
Il pannello usa salvataggi atomici e snapshot database per aumentare la robustezza dei file configurazione.

**Contenuto slide**

- Salvataggio XML atomico
- Retry su scrittura file
- Recovery da snapshot MySQL
- Gestione config mancante o corrotta
- Compatibilita' con ricette esistenti

**Note relatore**  
In un ambiente industriale, spegnimenti, permessi file o corruzioni possono capitare. Il sistema cerca di recuperare in modo controllato invece di fallire immediatamente.

## Slide 29 - Logging e diagnostica

**Messaggio slide**  
Eventi e log rendono osservabile il comportamento del pannello.

**Contenuto slide**

- NLog per log applicativi
- ApplicationEventLogger per eventi HMI
- Event Monitor per operatore e tecnico
- Log strutturati per recovery e hold runtime
- Storico modifiche su PowerFlex

**Note relatore**  
Un sistema industriale deve spiegare cosa sta facendo. Quando un hold viene rilasciato o un recovery viene eseguito, deve esserci una traccia leggibile.

---

# 11. Punti di forza

## Slide 30 - Punti di forza principali

**Messaggio slide**  
QtisVisionPanel unisce controllo operativo, visione, tracciabilita' e diagnostica in un'unica applicazione.

**Contenuto slide**

- Integrazione completa con VisionPro e runtime macchina
- Ricette e configurazione separate in modo corretto
- Supporto Top/Side/Front e Top3D
- DataInspector nativo
- Dashboard dati interna
- Recovery configurazione e salvataggi robusti
- Ruoli utente e hardening operatore
- Documentazione tecnica e manuale operatore disponibili

**Note relatore**  
Il vantaggio non e' solo una singola funzione. Il valore sta nell'insieme: produzione, manutenzione, qualita' e assistenza lavorano sulla stessa base dati e sulla stessa interfaccia.

## Slide 31 - Vantaggi per l'operatore

**Messaggio slide**  
L'operatore lavora con meno ambiguita' e piu' informazioni contestuali.

**Contenuto slide**

- Stato macchina sempre visibile
- Ricetta attiva evidente
- Contatori e qualita' leggibili
- Allarmi con popup e Acknowledge
- Pagine tecniche nascoste se non autorizzate
- Login piu' rapido con username history locale

**Note relatore**  
La UI deve essere comprensibile anche sotto pressione. Un allarme o un calo qualita' si leggono meglio se il pannello mostra solo cio' che serve e protegge funzioni non operative.

## Slide 32 - Vantaggi per manutenzione e collaudo

**Messaggio slide**  
I tecnici hanno strumenti integrati per capire I/O, VisionPro, inverter, dati e configurazione.

**Contenuto slide**

- Diagnostica I/O e encoder
- Job Tool Editor
- PowerFlex 525
- Event Monitor
- DataInspector
- Recovery config
- Documentazione commissioning

**Note relatore**  
Molte verifiche che prima richiedevano strumenti separati ora possono partire dal pannello. Questo riduce tempi di diagnosi e rende piu' standardizzato il collaudo.

## Slide 33 - Vantaggi per qualita' e supporto

**Messaggio slide**  
La tracciabilita' facilita analisi difetti, assistenza e miglioramento continuo.

**Contenuto slide**

- Collegamento tra difetto e immagine
- Trend misure e difetti nel tempo
- Ricetta archiviata con la produzione
- Storico eventi
- Evidence Top3D opzionale
- Dati utili per report e analisi cliente

**Note relatore**  
Il supporto non deve basarsi solo su descrizioni verbali. Puo' chiedere ricetta, orario, evento e immagine, poi analizzare il caso in modo piu' oggettivo.

---

# 12. Limiti e svantaggi attuali

## Slide 34 - Limiti tecnici

**Messaggio slide**  
La baseline e' matura, ma contiene ancora debito tecnico tipico di un'applicazione industriale evoluta nel tempo.

**Contenuto slide**

- `MainWindow.xaml.cs` e' ancora molto grande e centrale
- MVVM non e' applicato in modo uniforme
- Alcuni servizi dipendono ancora da riferimenti statici
- Dipendenze hardware rendono difficile testare tutto senza macchina
- Alcune aree richiedono attenzione alta prima di modifiche

**Note relatore**  
Il sistema funziona, ma l'architettura non e' ancora ideale. La priorita' e' preservare la stabilita' macchina, quindi i refactor devono essere progressivi e validati.

## Slide 35 - Limiti operativi

**Messaggio slide**  
Alcune funzioni restano potenti e richiedono formazione tecnica.

**Contenuto slide**

- Configurazione I/O e VisionPro non adatta a operatori inesperti
- Cambio parametri PowerFlex richiede conoscenza inverter
- Top3D richiede output VisionPro nominati correttamente
- DataInspector dipende da DB e immagini salvate correttamente
- Qualita' dashboard dipende dalla completezza dello storico

**Note relatore**  
Il pannello aiuta, ma non elimina la necessita' di una corretta configurazione macchina. Dove si lavora con I/O, VisionPro e inverter, servono ruoli tecnici e procedure di collaudo.

## Slide 36 - Rischi se usato male

**Messaggio slide**  
Il rischio principale e' confondere macchina, ricetta e runtime.

**Contenuto slide**

- Duplicare configurazioni macchina dentro ricette
- Modificare output VisionPro senza aggiornare il contratto pannello
- Usare comandi I/O manuali durante produzione
- Cambiare ricetta senza verificare delay e tolleranze
- Ignorare eventi o allarmi ricorrenti

**Note relatore**  
Molti problemi nascono quando una modifica viene fatta nel posto sbagliato. La regola mentale e' semplice: cablaggio e quote stanno in macchina, prodotto e tolleranze stanno in ricetta, stati temporanei stanno nel runtime.

---

# 13. Miglioramenti recenti

## Slide 37 - Hardening operatore

**Messaggio slide**  
Le ultime release hanno rafforzato sicurezza operativa e chiarezza UI.

**Contenuto slide**

- Permessi Viewer/Operator ridotti
- Comandi tecnici protetti
- Pulsanti critici con conferme
- Job Editor con hold e prompt modifiche
- Tooltip e banner piu' chiari
- Pulizia testi e localizzazione ServerMessage

**Note relatore**  
Questa evoluzione nasce dall'uso reale: se una funzione e' troppo accessibile, puo' diventare un rischio. Il pannello e' stato reso piu' adatto a operatori non esperti.

## Slide 38 - Migliorie ergonomiche

**Messaggio slide**  
Sono stati aggiunti dettagli che riducono attrito nell'uso quotidiano.

**Contenuto slide**

- Username history nella login
- Stile login coerente e leggibile
- Ricette con banner e tooltip dinamici
- Allarmi e diagnostica piu' guidati
- Dashboard e DataInspector integrati

**Note relatore**  
Piccole migliorie UI possono avere impatto reale, perche' il pannello viene usato molte volte al giorno e spesso in momenti in cui l'operatore deve decidere rapidamente.

---

# 14. Roadmap consigliata

## Slide 39 - Evoluzioni future suggerite

**Messaggio slide**  
La direzione futura e' aumentare modularita', testabilita' e supporto operativo senza rompere il runtime esistente.

**Contenuto slide**

- Continuare estrazione logica da `MainWindow`
- Rafforzare test su ricette, config e runtime holds
- Migliorare simulazione offline di VisionPro/I/O
- Estendere dashboard qualita'
- Rafforzare workflow assistenza remota
- Standardizzare schermate operatore per turno produzione

**Note relatore**  
La roadmap deve essere prudente. In un sistema macchina reale, il refactor migliore e' quello che riduce rischio senza cambiare contratti validati.

## Slide 40 - Messaggio finale

**Messaggio slide**  
QtisVisionPanel e' una piattaforma HMI completa per rendere l'ispezione visiva industriale piu' controllata, tracciabile e gestibile.

**Contenuto slide**

- Una sola interfaccia per produzione, qualita' e manutenzione
- Runtime macchina protetto
- Ricette e configurazione separate
- Tracciabilita' completa di risultati e immagini
- Diagnostica integrata
- Baseline documentata e in evoluzione controllata

**Note relatore**  
Il valore principale e' l'unione tra controllo macchina e informazione di qualita'. Il pannello non si limita a dire se un pezzo e' buono o scarto: aiuta a capire perche', quando, con quale ricetta e con quale evidenza.

---

# Sintesi esecutiva

QtisVisionPanel e' l'HMI industriale principale per il sistema di visione Pulsar. Gestisce il ciclo completo dell'ispezione: caricamento ricette, acquisizione immagini, esecuzione VisionPro, validazione difetti, aggiornamento contatori, gestione allarmi, scarto, salvataggio dati e supporto diagnostico.

I suoi punti forti sono l'integrazione completa con la macchina, la tracciabilita' dati/immagini, la separazione tra macchina e ricetta, il supporto Top3D, il DataInspector nativo, la dashboard dati e l'hardening dei ruoli operatore.

I limiti principali sono legati al debito tecnico storico: una `MainWindow` ancora molto centrale, dipendenze statiche e test difficili senza hardware reale. La direzione corretta e' continuare a modularizzare con prudenza, mantenendo stabile il contratto runtime guidato da `Config.xml`.

---

# Fonti documentali usate

- `README.md`
- `Docs/Manual/00_Documentazione_Tecnica_Globale.md`
- `Docs/Manual/01_Panoramica_Pannello.md`
- `Docs/Manual/02_Avvio_Stop_Produzione.md`
- `Docs/Manual/03_Ricette_Allarmi_Assistenza.md`
- `Docs/Manual/04_Controllo_3D_Top3D.md`
- `Docs/Manual/05_PowerFlex525_Inverter_Nastro.md`
- `Docs/Manual/06_Data_Analysis_Dashboard.md`
- `Documentation/MachineHardware/current-unified-baseline-2026-03-27.md`
- `Documentation/MachineHardware/architecture-and-modules.md`
- `Documentation/MachineHardware/main-project-datainspector-configuration-and-usage.md`
- `Documentation/MachineHardware/top3d-machine-configuration-guide-2026-04-13.md`
- `Documentation/MachineHardware/software-version-archive.md`
