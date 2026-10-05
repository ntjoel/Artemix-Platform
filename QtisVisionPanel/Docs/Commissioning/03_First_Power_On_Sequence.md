# First Power-On Sequence

Questo documento serve per la prima accensione controllata dopo il collegamento dei connettori della scheda I/O e dell'encoder.

Va usato prima del piano commissioning completo.

Obiettivo:

- evitare prove disordinate
- fermarsi subito se c'e' un'anomalia elettrica o software evidente
- arrivare rapidamente a capire se il sistema e' in uno stato buono per i test di dettaglio

---

## Quando usare questa sequenza

Usare questa sequenza quando:

- i connettori sono appena arrivati o appena montati
- il cablaggio e' stato appena completato
- si sta riaccendendo la macchina dopo modifiche hardware
- si vuole fare un primo sanity check prima del commissioning completo

---

## Prerequisiti minimi

Prima di alimentare:

- cavo I/O corretto disponibile: `PCL-10250-2E`
- terminal board I/O corretta disponibile: `ADAM-3951-BE`
- cavo encoder corretto disponibile: `PCL-10137H-3E`
- terminal board encoder corretta disponibile: `ADAM-3937-BE`
- schema segnali disponibile oppure almeno tabella morsetti/funzioni
- connettori inseriti e serrati
- controllo visivo fatto su masse, alimentazioni e schermature
- nessun attuatore pericoloso lasciato in stato non controllato
- pannello principale pronto
- documenti aperti:
  - `01_IO_Encoder_Commissioning_Plan.md`
  - `02_Commissioning_Test_Sheet.md`
  - `06_PCIE1756_2xADAM3951_Navigator_Test.md`

---

## Esito atteso della prima accensione

Alla fine di questa sequenza dobbiamo sapere:

- se il software parte senza fault bloccanti anomali
- se la macchina vede la scheda I/O
- se la macchina vede la scheda encoder
- se gli input principali sono leggibili
- se l'encoder non deriva da fermo
- se possiamo passare al commissioning completo

---

## Sequenza operativa 10-15 minuti

### 1. Verifica finale prima di dare alimentazione

Controllare:

- cavo I/O `PCL-10250-2E` inserito nel ramo corretto
- cavo encoder `PCL-10137H-3E` inserito nel ramo corretto
- `ADAM-3951-BE` usate sul ramo I/O corretto
- `ADAM-3937-BE` usata sul ramo encoder corretto
- nessun filo allentato
- nessun morsetto esposto
- alimentazioni coerenti con il tipo di scheda
- alimentazione encoder coerente con il datasheet dell'encoder
- riferimento GND presente dove richiesto
- uscite verso carichi critici in stato sicuro

Nota importante per il ramo encoder:

- non alimentare la `ADAM-3937-BE` "a intuito"
- alimentare l'encoder con la sua tensione corretta
- usare la terminal board come breakout del ramo segnali / `GND`

Se qui qualcosa non convince, fermarsi.

### 2. Dare alimentazione in sicurezza

Appena si alimenta:

- osservare se compaiono odori, rumori o led anomali
- verificare che non ci siano attuazioni meccaniche inattese
- verificare che la macchina resti in stato controllato

Se compare un comportamento fisico inatteso, togliere alimentazione e annotare.

### 3. Avviare il pannello

Aprire `QtisVisionPanel` e controllare:

- top bar visibile
- `Machine Status` leggibile
- ricetta attiva leggibile
- sessione utente leggibile
- nessun crash all'avvio

Annotare il primo stato macchina visualizzato.

### 4. Aprire il monitor eventi

Controllare subito il flusso eventi:

- errori dispositivo
- errori database
- errori VisionPro
- errori scheda I/O
- errori encoder

Se c'e' un errore ricorrente, non andare avanti alla cieca.

### 5. Verificare rilevamento hardware

Con il pannello avviato, verificare che risultino coerenti i riferimenti reali del progetto:

- `PCIE-1756`
- `PCIE-1884`

Se una delle due schede non viene vista, fermarsi e correggere prima di procedere con trigger o scarto.

### 6. Verificare stato input a riposo

Controllare che gli input a riposo siano sensati:

- `DI00` - `DI63`
- `DI100` - `DI103`

Da cercare:

- canali sempre attivi senza motivo
- fluttuazioni
- ingressi fantasma

### 7. Verificare encoder a fermo

Controllare:

- `ENC1` - `ENC4` se presenti
- valore stabile a macchina ferma
- nessuna deriva spontanea

Se l'encoder cambia valore da fermo, non passare ancora alle quote.

### 8. Verificare heartbeat e stabilita' breve

Lasciare il sistema acceso qualche minuto e osservare:

- heartbeat
- eventi ricorrenti
- disconnessioni
- freeze UI

Se il sistema non e' stabile nei primi minuti, il commissioning completo va rimandato.

### 9. Verificare una prima azione manuale sicura

Fare una sola prova semplice e sicura:

- un input manuale noto
- oppure un piccolo movimento controllato encoder

Scopo:

- capire se il software reagisce sul canale giusto

Non fare ancora test completi output, trigger o espulsione in questo punto.

### 10. Decidere il go/no-go

Se tutto e' coerente:

- aprire il piano completo
- iniziare il commissioning strutturato

Se c'e' una criticita':

- fermarsi
- annotare l'anomalia
- correggere prima di proseguire

---

## Go / No-Go rapido

### Go

Possiamo passare al commissioning completo se:

- nessun problema elettrico evidente
- software stabile
- `PCIE-1756` rilevata
- `PCIE-1884` rilevata se prevista
- input a riposo credibili
- encoder stabile da fermo

### No-Go

Non passare oltre se c'e' uno di questi sintomi:

- output o attuatore che parte da solo
- allarme hardware non compreso
- input incoerenti a riposo
- encoder instabile
- errori continui nel monitor eventi
- freeze o riavvii del pannello

---

## Cosa annotare subito

Nel `02_Commissioning_Test_Sheet.md` segnare almeno:

- data e tecnico
- ricetta attiva
- schede rilevate
- stato macchina iniziale
- eventuali errori visti in `Events Monitor`
- esito `GO` o `NO-GO`

---

## Passo successivo

Se la prima accensione e' buona:

1. proseguire con `01_IO_Encoder_Commissioning_Plan.md`
2. compilare `02_Commissioning_Test_Sheet.md`
3. salvare screenshot di top bar, menu eventi e schermate test rilevanti
