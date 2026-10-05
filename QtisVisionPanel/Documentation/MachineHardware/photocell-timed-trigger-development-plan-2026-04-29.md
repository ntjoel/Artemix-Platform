# Photocell Timed Trigger Development Plan 2026-04-29

## Scopo

Definire un piano di sviluppo pulito e industriale per sostituire il modello attuale:

- `fotocellula + virtual conveyor speed + punti in mm`

con un modello piu' robusto per questa macchina reale:

- `fotocellula reale + ritardi temporali deterministici + uscite trigger reali`

senza PLC, senza encoder fisico attivo e senza dipendere dalla lettura continua della velocita' inverter per la generazione del trigger.

Questo documento nasce come baseline tecnica prima dello sviluppo ed e' stato poi aggiornato con lo stato reale di implementazione delle fasi completate.

---

## Stato implementazione aggiornato

Alla release `1.1.6.8` risultano completate:

- Fase 1 - congelamento semantica corrente
- Fase 2 - modello configurazione
- Fase 3 - servizio scheduler
- Fase 4 - integrazione runtime fotocellula
- Fase 5 - UI commissioning
- Fase 6 - logging e tracciabilita'

Gia' implementato nella repo:

- modalita' macchina `TriggerSchedulingMode`
- persistenza dei parametri timed trigger nel file macchina
- servizio `PhotocellTimedTriggerService`
- aggancio del fronte fotocellula reale al nuovo scheduler quando la macchina e' in `TimedFromPhotocell`
- generazione indipendente dei due impulsi:
  - `TOP`
  - `SIDE`
- log strutturato del nuovo flusso
- diagnostica commissioning nella pagina `DigitalIOControl`
- pulsanti manuali:
  - simulazione fronte fotocellula
  - trigger manuale TOP
  - trigger manuale SIDE
  - reset diagnostica timed
- metriche e ritardi osservati per la taratura
- procedura dedicata:
  - `Docs/Commissioning/08_Photocell_Timed_Trigger_Commissioning.md`

Ancora da completare nelle fasi successive:

- eventuale governance piu' esplicita delle uscite luci nel timed flow
- eventuali trim ricetta-specifici se richiesti
- Fase 7 - collaudo macchina esteso e taratura finale sul campo

Nota di coerenza runtime chiusa in release `1.1.6.9`:

- `TimedFromPhotocell` e `VirtualConveyorEnabled` non devono piu' restare in conflitto
- se il modo scheduler e' `TimedFromPhotocell`, il virtual conveyor viene disattivato automaticamente
- il file macchina e il template vengono auto-riparati se mancano i nodi timed trigger
- il log fotocellula dichiara adesso il percorso effettivo:
  - scheduler timed
  - tracking legacy con virtual conveyor
  - tracking legacy in attesa di encoder reale

---

## Contesto macchina reale

Hardware disponibile in questa macchina:

- PC industriale Advantech
- scheda I/O digitale `PCIE-1756`
- eventuali terminal board / ADAM di interfaccia
- scheda encoder `PCIE-1884` disponibile a livello piattaforma ma non usata per questa soluzione
- inverter PowerFlex 525 usato per il trasporto
- fotocellula reale prodotto
- telecamera TOP e telecamera SIDE con ingressi trigger pilotati da uscite digitali

Vincoli dichiarati:

- nessun PLC macchina
- non si vuole dipendere dalla velocita' letta dall'inverter per il trigger produttivo
- si vuole massima ripetibilita' del punto di scatto
- il trigger deve nascere dal fronte della fotocellula reale
- dopo il trigger input devono essere generati due eventi uscita:
  - `TOP`
  - `SIDE`

---

## Cosa intendiamo qui per hardware reale

In questo progetto, per `hardware reale` intendiamo:

- input reali letti dalla `PCIE-1756`
- output reali scritti sulla `PCIE-1756`
- segnali 24V realmente cablati verso fotocellula e camere
- latenze reali del sistema operativo, driver Advantech e cablaggio

Quindi:

- non e' simulazione
- non e' virtual conveyor
- non e' modello derivato dall'inverter

Il tempo di trigger viene comunque generato dal PC industriale, ma usando:

- fronte fotocellula reale
- clock monotono locale
- scheduler dedicato
- code di uscita controllate

---

## Perche' il virtual conveyor non e' adatto qui

Anche impostando la velocita' reale del trasporto, il virtual conveyor resta una stima.

Cause di instabilita':

- jitter del timer WPF / Dispatcher
- latenza variabile del thread applicativo
- latenza driver I/O
- rimbalzo o rumore del fronte fotocellula
- differenza tra velocita' media e velocita' istantanea del pezzo
- micro slittamenti del prodotto rispetto al nastro
- possibili ritardi di polling nel caso di I/O non completamente event-driven

Effetto osservabile:

- il prodotto viene visto dalla fotocellula in un punto coerente
- ma la posizione stimata del trigger varia
- quindi lo scatto si sposta
- e l'ispezione a volte esce dall'area utile

Conclusione:

- per questa macchina, senza encoder reale, la soluzione piu' robusta non e' la posizione virtuale
- e' un trigger temporale deterministico costruito direttamente a partire dal fronte fotocellula

---

## Soluzione scelta

### Soluzione 3 industrializzata

`Fotocellula reale -> scheduler temporale deterministico -> due impulsi uscita reali`

Logica:

1. La fotocellula reale genera il fronte di riferimento prodotto.
2. Il runtime cattura subito un timestamp monotono.
3. Vengono pianificati due eventi separati:
   - `TopTrigger`
   - `SideTrigger`
4. Ogni evento attiva la sua uscita reale dopo un ritardo configurato.
5. Ogni uscita genera un impulso di durata configurata.

Il sistema non usa:

- velocita' virtuale per decidere quando sparare
- velocita' inverter come riferimento principale di trigger

La velocita' inverter puo' restare:

- diagnostica
- supporto operatore
- check secondario

ma non sorgente di sincronizzazione per il trigger produttivo.

---

## Obiettivo ingegneristico

Garantire che:

- ogni pezzo che attiva la fotocellula produca sempre gli stessi due scatti
- lo scatto `TOP` avvenga sempre allo stesso ritardo rispetto al fronte fotocellula
- lo scatto `SIDE` avvenga sempre allo stesso ritardo rispetto al fronte fotocellula
- i due impulsi siano indipendenti
- il runtime non introduca doppi trigger, inversioni o conflitti

---

## Architettura proposta

### 1. Sorgente evento

Sorgente unica:

- `IN_PRODUCT_PHOTOCELL`

Evento rilevante:

- solo il fronte logico attivo configurato

Protezione minima obbligatoria:

- debounce hardware-logico software
- lock anti-rientro sul fronte
- `minimum re-trigger gap`

### 2. Trigger scheduler dedicato

Introdurre un piccolo servizio dedicato, per esempio:

- `Services/PhotocellTimedTriggerService.cs`

Responsabilita':

- ricevere il timestamp del fronte fotocellula
- creare una `trigger batch` per quel prodotto
- pianificare:
  - `TopDelayMs`
  - `SideDelayMs`
- garantire ON/OFF impulsi ordinati e non sovrapposti sullo stesso canale
- loggare in modo dettagliato:
  - edge ricevuto
  - delay pianificato
  - orario effettivo di attivazione
  - uscita fisica usata
  - esito completato / scartato / annullato

### 3. Clock di riferimento

Non usare `DateTime.Now` per la precisione.

Usare:

- `Stopwatch`
- oppure `Stopwatch.GetTimestamp()`

Motivo:

- clock monotono
- migliore stabilita' per ritardi relativi

### 4. Strategia di scheduling

Per questa baseline la scelta raccomandata e':

- task scheduler dedicato con coda interna
- attese basate su `Task.Delay` solo per coarse delay
- controllo finale del tempo con `Stopwatch` prima di attivare l'uscita

Nota:

- non stiamo costruendo un motion controller hard real-time
- ma possiamo rendere il trigger molto piu' stabile di un virtual conveyor basato su posizione stimata

### 5. Output executor dedicato

Separare la parte di uscita in un esecutore chiaro:

- un solo punto che comanda `WriteOutputAsync`
- serializzazione per canale
- controllo anti-overlap

Esempio logico:

- `OUT_CAMERA_TOP_TRIGGER`
- `OUT_CAMERA_SIDE_TRIGGER`

Ogni uscita deve avere:

- `PulseWidthMs`
- `DelayFromPhotocellMs`
- `Enabled`

---

## Configurazione: macchina vs ricetta vs runtime

Seguire rigorosamente la baseline di questo progetto.

### Deve stare in configurazione macchina

- mappatura input fotocellula
- mappatura output top trigger
- mappatura output side trigger
- polarita' segnali
- modalita' trigger scheduler:
  - `TimedFromPhotocell`
- debounce fotocellula macchina
- `MinimumRetriggerGapMs`
- pulse width default per uscita
- delay base macchina se la distanza macchina e' fissa e indipendente dal prodotto

### Deve stare in ricetta

Se il punto di scatto dipende dal prodotto:

- `TopTriggerDelayMs`
- `SideTriggerDelayMs`

oppure:

- `TopTriggerTrimMs`
- `SideTriggerTrimMs`

Scelta raccomandata:

- mantenere in macchina un `base delay`
- mettere in ricetta un `trim delay`

Formula runtime:

- `EffectiveTopDelayMs = MachineTopBaseDelayMs + RecipeTopTrimDelayMs`
- `EffectiveSideDelayMs = MachineSideBaseDelayMs + RecipeSideTrimDelayMs`

Questo mantiene chiaro:

- macchina = geometria impianto
- ricetta = centraggio prodotto

### Deve essere solo runtime

- timestamp dell'ultimo fronte fotocellula
- trigger batch attivi
- eventi completati
- stato code uscita
- diagnostica live

---

## Requisiti funzionali

1. Il sistema deve reagire solo al fronte valido della fotocellula configurata.
2. Il sistema deve poter generare due trigger distinti per ogni pezzo:
   - TOP
   - SIDE
3. Ogni trigger deve avere delay indipendente.
4. Ogni trigger deve avere impulso indipendente.
5. Il sistema deve loggare chiaramente:
   - edge ricevuto
   - delay programmato
   - canale fisico
   - istante effettivo di ON
   - istante effettivo di OFF
6. Il sistema deve bloccare doppi trigger spuri sullo stesso edge.
7. Il sistema deve consentire configurazione da pannello coerente con il file macchina.
8. Il sistema non deve dipendere dalla lettura continua della velocita' inverter.
9. Il sistema deve poter essere disabilitato o riarmato senza riavvio macchina.

---

## Requisiti non funzionali

1. Nessun blocco UI durante il trigger.
2. Nessuna dipendenza dal dispatcher WPF per la temporizzazione critica.
3. Nessuna scrittura hardcoded che scavalchi la configurazione utente salvata.
4. Logging leggibile per commissioning e diagnostica campo.
5. Comportamento deterministico anche dopo salvataggio configurazione.
6. Compatibilita' con le configurazioni macchina esistenti.

---

## Modalita' operative da supportare

### Modalita' A - Timed trigger production

Uso reale macchina:

- fronte fotocellula
- delay top
- delay side
- due impulsi uscita

### Modalita' B - Manual test

Uso commissioning:

- pulsante pannello per forzare trigger top
- pulsante pannello per forzare trigger side
- pulsante pannello per simulare fronte fotocellula

### Modalita' C - Legacy / fallback

Solo per compatibilita':

- il vecchio flusso encoder/virtual conveyor deve poter restare presente ma non attivo sulla macchina che usa il timed trigger

---

## Piano di sviluppo consigliato

### Fase 1 - Congelamento semantica corrente

Obiettivo:

- dichiarare esplicitamente che su questa macchina il trigger produttivo canonico non e' `virtual conveyor`
- introdurre una modalita' macchina chiara:
  - `TriggerSchedulingMode = TimedFromPhotocell`

Output attesi:

- enum o string mode stabile
- documentazione aggiornata
- UI che mostra la modalita' attiva

### Fase 2 - Modello configurazione

Aggiungere in configurazione macchina:

- `TriggerSchedulingMode`
- `PhotocellDebounceMs`
- `MinimumRetriggerGapMs`
- `TopTriggerBaseDelayMs`
- `SideTriggerBaseDelayMs`
- `TopTriggerPulseMs`
- `SideTriggerPulseMs`

Opzionale in ricetta:

- `TopTriggerTrimDelayMs`
- `SideTriggerTrimDelayMs`

Output attesi:

- XML compatibile
- default ragionevoli
- save/load senza perdita

### Fase 3 - Servizio scheduler

Implementare `PhotocellTimedTriggerService` o equivalente.

Responsabilita':

- start / stop
- enqueue trigger batch
- pianificazione indipendente TOP/SIDE
- cancellazione batch in shutdown / recipe reload / stop macchina
- callback di esecuzione verso le uscite reali

Output attesi:

- servizio isolato
- testabile
- nessuna logica timing critica dentro la view

### Fase 4 - Integrazione runtime

Integrare il servizio nel flusso I/O:

- `OnInputChanged` fotocellula
- filtro anti-rimbalzo
- creazione batch
- log

Disattivare per questa modalita':

- uso di `MachineController.UpdateEncoderPosition` per i trigger camera
- virtual conveyor come sorgente di posizione trigger

Output attesi:

- trigger camera guidato solo da timing fotocellula

### Fase 5 - UI commissioning

Aggiornare `DigitalIOControl` con una sezione dedicata:

- modalita' trigger attiva
- top delay
- side delay
- top pulse
- side pulse
- debounce
- re-trigger gap
- ultimo edge fotocellula
- ultimo top shot
- ultimo side shot
- scostamento medio osservato

Output attesi:

- configurazione chiara
- diagnostica leggibile

### Fase 6 - Logging e tracciabilita'

Aggiungere log strutturati per:

- `PHOTOCELL_EDGE_ACCEPTED`
- `PHOTOCELL_EDGE_REJECTED_BOUNCE`
- `TOP_TRIGGER_SCHEDULED`
- `SIDE_TRIGGER_SCHEDULED`
- `TOP_TRIGGER_OUTPUT_ON`
- `TOP_TRIGGER_OUTPUT_OFF`
- `SIDE_TRIGGER_OUTPUT_ON`
- `SIDE_TRIGGER_OUTPUT_OFF`
- `TRIGGER_BATCH_CANCELLED`

### Fase 7 - Collaudo macchina

Test minimi:

1. 100 trigger consecutivi con prodotto fermo/riferimento ottico ripetibile
2. verifica coerenza quota immagine TOP
3. verifica coerenza quota immagine SIDE
4. verifica assenza doppi scatti
5. verifica assenza scatti mancati
6. test stop/start macchina
7. test salvataggio configurazione e riavvio software
8. test rumore fotocellula / occupazione rapida

---

## Rischi e mitigazioni

### Rischio 1 - Jitter software ancora troppo alto

Mitigazione:

- ridurre carico nel percorso trigger
- usare servizio dedicato
- usare clock monotono
- serializzare uscite per canale

Se non basta:

- step successivo industriale = modulo timer hardware esterno o encoder reale

### Rischio 2 - Configurazione ambigua TOP/SIDE

Mitigazione:

- il file macchina resta source of truth
- UI con vista esplicita:
  - punto logico
  - signal code
  - board
  - channel
  - telecamera collegata

### Rischio 3 - Ritardi diversi al variare velocita' nastro

Mitigazione:

- questa soluzione funziona bene se la macchina lavora a velocita' davvero costante o con poca deriva
- se in futuro la velocita' varia in modo sensibile, la soluzione definitiva torna a essere:
  - encoder reale
  - oppure compare hardware

---

## Decisione architetturale raccomandata

Per questa macchina specifica la raccomandazione e':

1. abbandonare il trigger produttivo basato su `virtual conveyor`
2. introdurre `TimedFromPhotocell` come modalita' canonica macchina
3. usare la `PCIE-1756` come hardware reale di input/output
4. usare l'inverter solo come diagnostica, non come base di sincronizzazione trigger
5. mantenere nel file macchina i binding fisici
6. mantenere in ricetta solo gli eventuali trim di prodotto

---

## Deliverable sviluppo

Prima milestone tecnica:

- config model aggiornato
- servizio scheduler creato
- integrazione minima fotocellula -> due trigger
- log strutturato

Second milestone:

- UI commissioning completa
- save/load config
- test macchina

Terza milestone:

- pulizia del vecchio flow virtual conveyor per evitare sovrapposizioni
- documentazione operatore / commissioning aggiornata

---

## Nota finale

Questa soluzione e' la migliore praticabile con l'hardware attualmente disponibile se non si vuole usare:

- encoder reale
- PLC
- compare position hardware

Non e' hard real-time, ma puo' essere resa molto piu' stabile del virtual conveyor se:

- il percorso trigger e' dedicato
- la configurazione e' chiara
- il runtime non contiene fallback o inferenze nascoste
- il trigger nasce da un solo fronte fotocellula e governa direttamente due impulsi uscita configurati
