# PCIE-1884 + ADAM-3937-BE First Encoder Test

Questa guida serve per il primo test banco dell'encoder con:

- `PCIE-1884`
- cavo `PCL-10137H-3E`
- terminal board `ADAM-3937-BE`

Obiettivo:

- collegare in modo prudente il primo encoder
- evitare errori di alimentazione o riferimenti errati
- vedere il primo conteggio reale su `Counter0 / ENC1`
- preparare il passaggio successivo nel software standalone e poi nel progetto completo

---

## Regola chiave prima di iniziare

Per il ramo encoder, il riferimento corretto e':

- **alimentare l'encoder**, non la terminal board a intuito
- usare `ADAM-3937-BE` come breakout / morsettiera del connettore `PCIE-1884`
- portare alla scheda solo:
  - segnali encoder
  - riferimento `GND`
  - eventuale `Z` se presente

Dalle pagine manuale che abbiamo verificato finora:

- sul connettore I/O della `PCIE-1884` risultano i pin segnali e `GND`
- non abbiamo ancora un riferimento confermato che autorizzi a portare direttamente `24V` sulla terminal board come se fosse una scheda attiva

Quindi, fino a conferma dal datasheet completo dell'encoder:

- `ADAM-3937-BE` va trattata come morsettiera passiva
- l'alimentazione va data **all'encoder** dal suo alimentatore corretto

---

## Hardware confermato

- scheda encoder:
  - `PCIE-1884`
- cavo:
  - `PCL-10137H-3E`
- terminal board:
  - `ADAM-3937-BE`

Canale di prima validazione:

- `ENC1 / Counter0`

---

## Pin utili per il primo test su Counter0

Dal manuale `PCIE-1884` condiviso:

### Counter0 / encoder channel 0

- `CNT0_CLK+/A+` -> pin `2`
- `CNT0_CLK-/A-` -> pin `20`
- `CNT0_AUX+/B+` -> pin `3`
- `CNT0_AUX-/B-` -> pin `21`
- `CNT0_GATE+/Z+` -> pin `4`
- `CNT0_GATE-/Z-` -> pin `22`

### GND disponibili

- `GND` -> pin `1`
- `GND` -> pin `14`
- `GND` -> pin `17`
- `GND` -> pin `32`
- `GND` -> pin `35`

Per il primo test basta un solo `GND` ben serrato e coerente.

---

## Prima di cablare: che tipo di encoder hai

Prima di collegare, devi identificare il tipo di uscita encoder.

I casi tipici sono:

### 1. Encoder quadratura single-ended

Segnali tipici:

- `A`
- `B`
- opzionale `Z`
- `GND`

Questo e' il caso piu' semplice per il primo test.

### 2. Encoder differenziale / line driver

Segnali tipici:

- `A+`
- `A-`
- `B+`
- `B-`
- opzionale `Z+`
- opzionale `Z-`
- `GND`

Questo e' il caso migliore industrialmente, ma va cablato completo.

### 3. Encoder pulse/direction o up/down

Il manuale mostra che la `PCIE-1884` li supporta, ma **non e' il primo test consigliato**.

Per iniziare bene, usare quadratura `A/B` su `Counter0`.

---

## Primo cablaggio consigliato

### Caso A: encoder single-ended quadratura

Usare:

- segnale `A` -> `CNT0_CLK+/A+`
- segnale `B` -> `CNT0_AUX+/B+`
- segnale `Z` opzionale -> `CNT0_GATE+/Z+`
- `GND encoder` -> uno dei `GND` della scheda

Per il primo test:

- `A` -> pin `2`
- `B` -> pin `3`
- `Z` opzionale -> pin `4`
- `GND` -> pin `1` oppure `14`

I pin negativi:

- pin `20`
- pin `21`
- pin `22`

nel test single-ended iniziale **non vanno usati come segnali positivi**.

### Caso B: encoder differenziale / line driver

Usare:

- `A+` -> pin `2`
- `A-` -> pin `20`
- `B+` -> pin `3`
- `B-` -> pin `21`
- `Z+` opzionale -> pin `4`
- `Z-` opzionale -> pin `22`
- `GND encoder` -> uno dei `GND`

---

## Alimentazione encoder

L'alimentazione dell'encoder va data secondo il suo datasheet reale.

Quindi:

- se l'encoder richiede `24VDC`, porti `24VDC` all'encoder
- se l'encoder richiede `5VDC`, porti `5VDC` all'encoder
- il `0V / GND` dell'alimentazione encoder deve avere un riferimento coerente col `GND` segnali usato verso la `PCIE-1884`

Da non fare:

- non portare `24V` su pin segnale della `PCIE-1884`
- non cercare di "accendere" `ADAM-3937-BE` come se fosse una scheda attiva
- non improvvisare tensioni sulla morsettiera senza conferma del datasheet encoder

---

## Sequenza pratica di banco

### Step 1 - collega solo Counter0

Per il primo test usa solo:

- `A`
- `B`
- `GND`
- opzionale `Z`

Non cablare ancora altri contatori.

### Step 2 - alimenta solo l'encoder

Accendi l'alimentazione corretta dell'encoder e verifica:

- nessun surriscaldamento
- nessun led anomalo
- nessun assorbimento sospetto

### Step 3 - apri DAQNavi / Navigator

Controlla:

- `PCIE-1884` vista correttamente
- `Counter0` disponibile
- nessun errore hardware

### Step 4 - osserva il valore a fermo

A encoder fermo:

- il conteggio deve restare stabile
- non deve derivare da solo

Se deriva a fermo:

- fermarsi
- controllare `GND`
- controllare schermatura
- controllare tipo encoder e cablaggio `A/B`

### Step 5 - muovi lentamente l'encoder

Ruota lentamente l'albero o il rullo associato.

Atteso:

- il conteggio cambia
- il conteggio non salta in modo casuale
- il verso e' coerente

### Step 6 - verifica il verso

Se ruotando nel verso macchina il conteggio va nella direzione opposta:

- non e' un dramma
- va annotato
- si potra' invertire via cablaggio `A/B` o via configurazione software, secondo la strada piu' pulita

---

## Criteri di successo del primo test

Il primo test encoder e' superato se:

- `PCIE-1884` viene vista dal PC
- `Counter0 / ENC1` e' leggibile
- il valore e' stabile da fermo
- il valore cambia quando l'encoder si muove
- il verso e' comprensibile e ripetibile

---

## Se non funziona

Le cause piu' probabili sono:

- encoder alimentato con tensione sbagliata
- `GND` mancante o non coerente
- tipo encoder non identificato correttamente
- `A` e `B` scambiati o cablati su pin sbagliati
- uso dei pin `-` come se fossero uscite positive
- cablaggio su canale diverso da `Counter0`

---

## Passo successivo

Se questo test passa:

1. ripetere la verifica nel tool standalone `QuatisVisionPanelIO`
2. confermare `ENC1 / Counter0` nella pagina encoder
3. validare verso, reset, preset e stabilita'
4. solo dopo passare a:
   - fotocellula + nascita prodotto
   - intervention points
   - trigger
   - lighting window
   - reject tracked
