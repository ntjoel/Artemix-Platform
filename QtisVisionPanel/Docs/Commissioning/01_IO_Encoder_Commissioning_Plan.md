# I/O And Encoder Commissioning Plan

## Purpose

Questo documento serve per riprendere in modo ordinato il commissioning della:

- scheda I/O
- lettura encoder
- logica quote/intervention points
- trigger e scarto

È scritto per essere usato anche da chi non ha mai messo mano prima a questa macchina o a questo software.

Obiettivo finale:

- verificare che il cablaggio sia corretto
- verificare che il tool e il progetto leggano correttamente i segnali
- verificare che encoder, quote, trigger e scarto si comportino in modo affidabile
- raccogliere evidenze pulite per la futura fusione definitiva della logica nel progetto completo

---

## Related Documents In This Repository

Prima di iniziare i test, tenere come riferimento anche questi documenti già presenti nel progetto:

- [../Manual/01_Panoramica_Pannello.md](../Manual/01_Panoramica_Pannello.md)
- [../Manual/02_Avvio_Stop_Produzione.md](../Manual/02_Avvio_Stop_Produzione.md)
- [../Manual/03_Ricette_Allarmi_Assistenza.md](../Manual/03_Ricette_Allarmi_Assistenza.md)
- [./03_First_Power_On_Sequence.md](./03_First_Power_On_Sequence.md)

Questi documenti sono utili perché spiegano:

- la struttura della shell operatore
- dove leggere stato macchina e sessione
- come usare `Start` e `Stop`
- dove controllare `Events Monitor`
- come verificare ricetta attiva, immagini e allarmi

Per il commissioning I/O ed encoder non bastano da soli, ma aiutano molto a orientarsi nel pannello reale.

`03_First_Power_On_Sequence.md` va usato appena si collegano i connettori o si riaccende la macchina dopo modifiche hardware. Questo piano invece e' il flusso completo da seguire una volta passato il primo controllo `GO / NO-GO`.

---

## Real Technical References Found In The Project

Durante la preparazione del piano sono emersi questi riferimenti reali già usati dal software:

- hardware digitale previsto:
  - `PCIE-1756` per Digital I/O
  - `PCIE-1884` per encoder e I/O digitali dedicati
- canali digital input principali:
  - `DI00` - `DI31` sul ramo `PCIE-1756` (32 DI; canali 32-63 rifiutati dal software)
- canali digital input aggiuntivi scheda encoder:
  - `DI100` - `DI103` sul ramo `PCIE-1884`
- canali digital output principali:
  - `DO00` - `DO31` sul ramo `PCIE-1756` (32 DO; canali 32-63 rifiutati dal software)
- canali digital output aggiuntivi scheda encoder:
  - `DO100` - `DO103` sul ramo `PCIE-1884`
- canali encoder previsti:
  - `ENC1` - `ENC4`
  - corrispondenti ai contatori `0` - `3`
- uscite allarme bloccante / non bloccante:
  - assegnate per singola alarm card (`OutputChannelId`), nel range `DO00` - `DO31`
  - `BlockingAlarmOutput` / `NonBlockingAlarmOutput` di `Config.xml` sono campi legacy non letti
    dal runtime; i vecchi default `DO40` / `DO41` sono fuori range per la PCIE-1756 e generano un
    warning all'avvio
- trigger delay camera:
  - `TopCameraTriggerDelay`
  - `SideCameraTriggerDelay`
  - letti dalla ricetta attiva
- file mapping logico I/O:
  - `Recipe_Folder\\cfg\\io_mapping.xml`

Questo significa che il commissioning deve sempre verificare:

- cablaggio fisico
- corrispondenza col canale software
- eventuale configurazione presente in `io_mapping.xml`
- canale di uscita assegnato alle alarm card attive (allarmi/ejection logic)
- corretto caricamento dei trigger delay da ricetta

---

## Safety First

Prima di qualsiasi prova:

- non collegare carichi critici o attuatori pericolosi senza sapere esattamente quale uscita si sta testando
- se il comando può muovere parti meccaniche, pneumatiche o gruppi di espulsione, mettere prima la macchina in sicurezza
- verificare la tensione reale con multimetro prima di dare per scontato il cablaggio
- evitare prove “a intuito” su ingressi NPN/PNP senza conferma del tipo di scheda
- segnare sempre le modifiche fatte a morsetti, ponticelli, parametri e configurazioni

Se durante un test c’è uno di questi sintomi, fermarsi:

- output non atteso
- input che cambia su canale sbagliato
- encoder che oscilla da fermo
- heartbeat instabile
- quota che scatta in punti incoerenti
- movimento meccanico non previsto

---

## What You Need Before Starting

Preparare prima del test:

- PC di test con software pronto
- accesso al pannello principale `QtisVisionPanel`
- accesso al progetto e al tool I/O/encoder usato finora
- schema elettrico o almeno tabella segnali
- elenco morsetti realmente cablati
- alimentazione disponibile e verificata
- multimetro
- eventuale spia di test o piccolo carico sicuro per uscite
- eventuale generatore manuale di impulsi o movimento controllato per encoder
- foglio test o file note

Materiale da tenere vicino:

- nome canale
- morsetto fisico
- funzione attesa
- stato a riposo
- stato attivo
- note anomalia

Template minimo consigliato per gli appunti:

| Signal | Terminal | Expected | Observed | Result | Notes |
|---|---|---|---|---|---|
| IN_xxx | X1.xx | OFF at rest / ON active | ... | OK / KO | ... |

---

## Physical Kit Confirmed For This Session

Componenti fisici confermati per il commissioning attuale:

- collegamento PC -> scheda I/O:
  - cavo `PCL-10250-2E`
- breakout / terminal board I/O:
  - `ADAM-3951-BE`
  - sono presenti `2` schede
- collegamento PC -> scheda encoder:
  - cavo `PCL-10137H-3E`
- breakout / terminal board encoder:
  - `ADAM-3937-BE`

Catena fisica da considerare durante i test:

- PC / scheda Advantech I/O -> `PCL-10250-2E` -> `ADAM-3951-BE`
- PC / scheda Advantech encoder -> `PCL-10137H-3E` -> `ADAM-3937-BE`

Questo significa che durante il commissioning vanno verificati due livelli:

- coerenza software del canale
- coerenza fisica del percorso cavo -> terminal board -> morsetto macchina

Prima di testare un segnale, confermare sempre:

- cavo corretto
- terminal board corretta
- morsetto corretto
- canale software corretto

---

## Software Orientation Before Hardware Tests

Prima di toccare i segnali, chi esegue il test deve sapersi orientare nella UI.

Verificare nel pannello:

- stato macchina in top bar
- ricetta attiva
- sessione utente
- pulsanti `Start`, `Stop`, `Close`
- `Events Monitor`
- pagine di ricetta e diagnostica utili

Controlli consigliati all’avvio:

1. aprire il pannello
2. confermare che la UI sia stabile
3. leggere `Machine Status`
4. leggere eventuali eventi recenti
5. verificare che la ricetta attiva sia quella prevista per il test
6. verificare che le immagini camera, se previste, siano disponibili o almeno che il sistema non sia in fault bloccante

Questo passaggio evita di iniziare il commissioning hardware con:

- ricetta sbagliata
- macchina in stato allarme
- sessione utente non corretta
- software non ancora pronto

---

## Test Philosophy

L’ordine corretto non è partire da trigger e scarto.

L’ordine corretto è:

1. alimentazione e sicurezza
2. riconoscimento hardware
3. input digitali
4. output digitali
5. encoder
6. stabilità runtime e heartbeat
7. quote/intervention points
8. trigger
9. scarto
10. robustezza e ripetibilità

Questo evita di perdere tempo su logica avanzata quando la base elettrica non è ancora valida.

---

## Phase 0 - Electrical Preparation

### 0.1 Visual and Wiring Check

Controllare:

- connettori inseriti correttamente
- cavi serrati
- nessun filo spelato o in trazione
- morsetti numerati o almeno riconoscibili
- schermature presenti dove previste
- masse comuni presenti dove necessarie

### 0.2 Power Check

Verificare con multimetro:

- tensione alimentazione scheda I/O
- tensione alimentazione encoder
- presenza GND
- riferimento comune corretto

Annotare:

- valore nominale atteso
- valore misurato reale
- eventuale instabilità

### 0.3 Input Type Check

Confermare:

- tipo ingresso della scheda
- segnale NPN o PNP
- livelli logici di attivazione

Non andare avanti se questo punto non è chiaro.

---

## Phase 1 - Hardware Bring-Up

Scopo:

- capire se PC e software vedono davvero la scheda I/O e l’encoder

Procedura:

1. collegare solo ciò che serve al riconoscimento base
2. avviare software/tool
3. verificare che la scheda venga rilevata
4. verificare che non ci siano errori in apertura
5. verificare heartbeat o stato runtime base
6. collegare encoder
7. verificare che il canale encoder venga inizializzato
8. aprire `Events Monitor` e controllare se il sistema segnala errori di device, database, VisionPro o macchina
9. annotare quali dispositivi vengono effettivamente visti:
   - `PCIE-1756`
   - `PCIE-1884`

Esito atteso:

- nessun crash
- nessun freeze
- nessuna perdita connessione immediata
- stato device leggibile

Se fallisce:

- fermarsi e correggere prima di testare segnali

---

## Phase 2 - Digital Input Test

Scopo:

- validare cablaggio e mapping degli input

Procedura:

1. scegliere un input alla volta
2. osservare stato a riposo nel software
3. attivare manualmente il segnale
4. osservare il canale che cambia
5. disattivare
6. ripetere almeno tre volte

Per ogni input registrare:

- nome logico
- morsetto fisico
- stato a riposo
- stato attivo
- eventuale inversione logica
- eventuali rimbalzi o instabilità

Range reali da verificare:

- `DI00` - `DI31` per `PCIE-1756`
- `DI100` - `DI103` per `PCIE-1884`

Esito atteso:

- cambia solo il canale corretto
- lo stato è stabile
- nessun canale “fantasma”
- nessuna inversione non prevista

Se trovi un problema:

- canale sbagliato: correggere mapping o cablaggio
- instabilità: controllare massa, schermatura, rumore
- inversione: verificare tipo ingresso e logica configurata

---

## Phase 3 - Digital Output Test

Scopo:

- verificare che le uscite comandino il canale corretto

Procedura sicura:

1. scollegare o isolare il carico pericoloso, se possibile
2. usare un test manuale o manutenzione
3. attivare una sola uscita alla volta
4. verificare su morsetto e, se sicuro, sul carico
5. disattivare
6. confermare il ritorno a riposo

Esito atteso:

- uscita giusta
- nessuna uscita secondaria che cambia da sola
- nessun ON bloccato
- nessun ritardo inspiegabile

Attenzione particolare per:

- attuatori pneumatici
- espulsori
- comandi macchina
- luci o trigger camera

Range reali da verificare:

- `DO00` - `DO31` per `PCIE-1756`
- `DO100` - `DO103` per `PCIE-1884`

Controlli extra importanti:

- annotare il canale assegnato a ciascuna alarm card attiva (bloccante e non bloccante)
- verificare che nessuna alarm card usi un canale gia' assegnato a trigger camera o heartbeat
- `DO40` / `DO41` di `Config.xml` sono legacy e fuori range: non vanno cablati

---

## Phase 4 - Encoder Test

Scopo:

- validare conteggio, verso, stabilità e conversione

Range reale previsto:

- `ENC1` - `ENC4`
- contatori `0` - `3`
- modalità prevista nel software: `Quadrature`

### 4.1 Encoder At Rest

Procedura:

1. lasciare encoder fermo
2. osservare il valore per almeno 30-60 secondi

Esito atteso:

- valore stabile
- nessun drift
- nessuno spike

Se oscilla da fermo:

- controllare cablaggio
- controllare schermatura
- controllare riferimento di massa
- controllare rumore elettrico

### 4.2 Encoder In Motion

Procedura:

1. muovere lentamente
2. verificare incremento o decremento
3. ripetere nel verso opposto
4. aumentare leggermente velocità

Esito atteso:

- conteggio coerente
- verso coerente
- nessuna perdita evidente

### 4.3 Conversion Check

Se il sistema usa mm/pulse o una scala equivalente:

1. muovere una distanza nota
2. confrontare la distanza teorica con quella letta
3. annotare errore

Esito atteso:

- errore piccolo e spiegabile
- nessuna scala grossolanamente errata

---

## Phase 5 - Heartbeat And Runtime Stability

Scopo:

- verificare che il sistema sia stabile nel tempo

Procedura:

1. lasciare software acceso 10-15 minuti
2. osservare heartbeat
3. osservare log ed eventuali warning
4. ripetere con encoder collegato
5. ripetere con qualche input che cambia
6. controllare anche `Events Monitor` nel pannello principale

Esito atteso:

- heartbeat continuo
- nessun reset inatteso
- nessuna perdita device
- nessun degrado progressivo

---

## Phase 6 - Intervention Points And Runtime Distances

Scopo:

- validare quote, offset e punti evento

Procedura:

1. definire chiaramente lo zero macchina o riferimento attivo
2. impostare quote di test semplici e conosciute
3. simulare passaggio o avanzamento
4. osservare il momento in cui ogni punto scatta
5. confrontare atteso vs reale

Da controllare:

- quota trigger
- quota scarto
- ordine eventi
- coerenza con encoder
- eventuali offset macchina
- eventuale logica configurata in `io_mapping.xml`

Esito atteso:

- eventi nell’ordine corretto
- nessuna attivazione anticipata o ritardata senza spiegazione
- ripetibilità del comportamento

---

## Phase 7 - Trigger Test

Scopo:

- verificare che il trigger avvenga nel punto corretto

Procedura:

1. usare prodotto reale o simulazione controllata
2. generare presenza prodotto
3. osservare trigger
4. verificare relazione con quota encoder
5. ripetere più volte
6. se il sistema mostra immagini o stato ispezione nel pannello, controllare che la vista operativa resti coerente con il trigger generato
7. verificare che il trigger delay reale caricato dalla ricetta sia coerente con:
   - `TopCameraTriggerDelay`
   - `SideCameraTriggerDelay`

Esito atteso:

- trigger coerente
- nessuna doppia attivazione
- nessun trigger mancato

---

## Phase 8 - Reject / Ejection Test

Scopo:

- verificare la catena completa esito KO -> comando scarto

Procedura:

1. eseguire test in sicurezza
2. generare o simulare pezzo KO
3. verificare che il sistema marchi il KO
4. seguire la quota fino al punto di scarto
5. verificare il comando uscita
6. ripetere con pezzo OK

Esito atteso:

- OK non scarta
- KO scarta
- la quota di scarto è coerente
- nessuna doppia espulsione

---

## Phase 9 - Robustness Test

Scopo:

- trovare problemi intermittenti

Eseguire:

- più cicli consecutivi
- input rapidi ON/OFF
- stop/start software
- riavvio sessione
- encoder fermo e poi movimento
- serie di KO e OK alternati

Da osservare:

- perdita sincronismo
- stati bloccati
- contatori non coerenti
- ritardi progressivi
- warning ripetuti

---

## What To Save During Tests

Alla fine di ogni sessione salvare:

- configurazione usata
- backup prima del test
- backup dopo modifiche stabili
- screenshot mapping segnali
- screenshot encoder
- screenshot quote/intervention points
- screenshot di `Events Monitor` se ci sono warning o anomalie
- screenshot di stato macchina / top bar quando emerge un problema
- log eventi o note errori
- elenco anomalie aperte
- elenco correzioni fatte
- copia o screenshot dei valori reali trovati in:
  - `Config.xml`
  - ricetta attiva
  - eventuale `io_mapping.xml`

Questo materiale servirà poi per:

- consolidare la configurazione macchina
- evitare regressioni
- facilitare la futura fusione nel progetto completo

---

## Recommended One-Day Test Flow

### Block 1 - Electrical and hardware check

Durata indicativa: 30-45 min

- orientamento software e stato pannello
- cablaggio
- tensioni
- masse
- sicurezza
- riconoscimento schede

### Block 2 - Inputs and outputs

Durata indicativa: 45-60 min

- test input uno a uno
- test output uno a uno

### Block 3 - Encoder

Durata indicativa: 45 min

- fermo
- movimento
- verso
- scala

### Block 4 - Runtime logic

Durata indicativa: 60 min

- heartbeat
- quote
- intervention points
- trigger

### Block 5 - Reject logic

Durata indicativa: 45-60 min

- pezzi OK
- pezzi KO
- quota scarto
- ripetibilità

### Block 6 - Wrap-up

Durata indicativa: 20-30 min

- raccolta screenshot
- salvataggio config
- note aperte
- decisione su prossimi fix

---

## Beginner-Friendly Practical Checklist

### Before power on

- schema disponibile
- morsetti identificati
- alimentazione verificata
- masse verificate
- carichi pericolosi sotto controllo
- multimetro pronto
- file note pronto
- ricetta di test definita
- persona che esegue sa dove vedere `Machine Status` e `Events Monitor`

### After software start

- scheda I/O rilevata
- encoder rilevato
- heartbeat presente
- nessun errore immediato
- `Events Monitor` leggibile
- stato macchina coerente con la condizione attesa di test

### Inputs

- ogni input cambia solo il proprio canale
- nessun input invertito
- nessun input rumoroso

### Outputs

- ogni output attiva il canale giusto
- nessun output inatteso
- nessun blocco ON

### Encoder

- fermo stabile
- verso corretto
- conteggio coerente

### Runtime distances

- quote coerenti
- trigger corretto
- scarto corretto

### End of session

- config salvata
- backup salvato
- anomalie annotate
- screenshot raccolti

---

## Stop Criteria

Non andare avanti alla fase successiva se:

- il cablaggio non è chiaro
- uno o più input sono instabili
- gli output non corrispondono ai canali attesi
- l’encoder non è stabile
- heartbeat/runtime non sono affidabili
- quote e trigger non sono ripetibili

Prima si chiude la base elettrica e di lettura, poi si valida la logica macchina.

---

## Final Expected Output

Al termine del commissioning dovremmo avere:

- scheda I/O validata
- encoder validato
- mapping segnali validato
- quote base validate
- trigger validato
- scarto validato
- report anomalie residuo
- materiale pronto per il consolidamento e la futura integrazione completa

In aggiunta, chiunque riprenda il lavoro dopo deve poter capire subito:

- in che stato era la macchina
- quale ricetta era attiva
- quali prove sono state completate
- quali anomalie sono ancora aperte
- quali screenshot o log spiegano i punti critici
- quali canali reali risultano effettivamente utilizzati fra:
  - `DI00` - `DI31`
  - `DI100` - `DI103`
  - `DO00` - `DO31`
  - `DO100` - `DO103`
  - `ENC1` - `ENC4`

---

## Encoder Manual References Already Confirmed

Per il ramo encoder `PCIE-1884 + PCL-10137H-3E + ADAM-3937-BE`, i riferimenti gia' confermati dal manuale sono:

### Counter0 / encoder channel 0

- `CNT0_CLK+/A+` -> pin `2`
- `CNT0_CLK-/A-` -> pin `20`
- `CNT0_AUX+/B+` -> pin `3`
- `CNT0_AUX-/B-` -> pin `21`
- `CNT0_GATE+/Z+` -> pin `4`
- `CNT0_GATE-/Z-` -> pin `22`
- `GND` -> pin `1`, `14`, `17`, `32`, `35`

### I/O digitali ausiliari presenti sulla `PCIE-1884`

- `IDI0/CNT0_SCLK` -> pin `15`
- `IDI1/CNT1_SCLK` -> pin `33`
- `IDI2/CNT2_SCLK` -> pin `16`
- `IDI3/CNT3_SCLK` -> pin `34`
- `IDO0/CNT0_OUT` -> pin `18`
- `IDO1/CNT1_OUT` -> pin `36`
- `IDO2/CNT2_OUT` -> pin `19`
- `IDO3/CNT3_OUT` -> pin `37`

Regola pratica da mantenere:

- alimentare l'encoder secondo il suo datasheet
- usare `ADAM-3937-BE` come terminal board del ramo segnali
- non portare tensioni ai pin segnale della `PCIE-1884`

Per il primo bring-up banco usare:

- `07_PCIE1884_ADAM3937_First_Encoder_Test.md`
