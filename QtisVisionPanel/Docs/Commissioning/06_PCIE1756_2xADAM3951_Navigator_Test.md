# PCIE-1756 + 2x ADAM-3951-BE Navigator Test

Questa guida e' il riferimento pratico per il primo test reale della `PCIE-1756` usando:

- cavo `PCL-10250-2E`
- `2` terminal board `ADAM-3951-BE`
- software Advantech Navigator / DAQNavi

Serve per confermare:

- lettura input
- pilotaggio output
- corretto uso dei due connettori `CON1` e `CON2`
- corretto uso di `24VDC` e `0V`

---

## Obiettivo del test

Configurazione voluta:

- `CON1` del `PCL-10250-2E` su ADAM dedicato agli input
- `CON2` del `PCL-10250-2E` su ADAM dedicato agli output

Non serve un rele' per il test base.

Serve invece:

- alimentatore `24VDC`
- `0V / GND`
- multimetro
- meglio anche `LED + resistenza 1k` oppure piccola lampada `24V`

Logica da tenere in testa:

- input: tu porti `+24V` su `IDIx`, con `0V` collegato a `ECOM`
- output: la scheda e' di tipo `sink`, quindi:
  - `PCOM` va collegato a `+24V`
  - `IGND` va collegato a `0V`
  - il carico va tra `PCOM / +24V` e `IDOx`
  - quando l'output va `ON`, `IDOx` chiude verso `0V`

---

## Come usare i due ADAM

### ADAM input

Usa solo:

- `CON1`
- un `ADAM-3951-BE` dedicato agli input

### ADAM output

Usa solo:

- `CON2`
- un secondo `ADAM-3951-BE` dedicato agli output

Per questo primo test non mischiare input e output sullo stesso ADAM.

---

## Riferimenti morsetti dal wiring fornito

### CON1 - Input

Banco 0:

- `IDI0` = terminale `1`
- `IDI1` = terminale `2`
- `ECOM0` = terminali `17, 18, 19, 20`

Banco 1:

- `IDI16` = terminale `26`
- `ECOM1` = terminali `42, 43, 44, 45`

Per il primo test usare solo banco 0.

### CON2 - Output

Banco 0:

- `IDO0` = terminale `1`
- `IDO1` = terminale `2`
- `PCOM0` = terminali `17, 18, 19, 20`

Banco 1:

- `IDO16` = terminale `26`
- `PCOM1` = terminali `42, 43, 44, 45`

Per il primo test usare solo banco 0.

---

## Cablaggio minimo da fare ora

### 1. ADAM input

Collega:

- `0V` alimentatore -> `ECOM0`, ad esempio terminale `17`
- tieni pronto un cavetto volante con `+24V`

Test base:

- tocca con il `+24V` il terminale `1` (`IDI0`)

Risultato atteso:

- in Navigator `DI0` va `ON`
- togliendo il filo torna `OFF`

Poi ripeti su:

- terminale `2` (`IDI1`)

### 2. ADAM output

Collega:

- `+24V` alimentatore -> `PCOM0`, ad esempio terminale `17`
- `0V` alimentatore -> `IGND`

Prepara un carico test:

- `PCOM / +24V -> resistenza 1k -> LED -> IDO0`

oppure:

- `PCOM / +24V -> lampadina 24V -> IDO0`

Collegamento completo:

- `+24V` alimentatore -> `PCOM0`
- `0V` alimentatore -> `IGND`
- lato positivo del carico -> `PCOM / +24V`
- lato negativo del carico -> `IDO0` terminale `1`

Risultato atteso:

- in Navigator forzi `DO0 = ON`
- il LED o la lampada si accende
- `DO0 = OFF`
- il LED o la lampada si spegne

Poi ripeti su:

- `IDO1` terminale `2`

---

## Procedura esatta in Navigator

### Fase A - Test input

1. Collega solo l'ADAM input su `CON1`
2. Collega `0V` a `ECOM0`
3. Apri Navigator
4. Vai alla pagina Digital Input della `PCIE-1756`
5. Verifica che `DI0` sia `OFF`
6. Applica `+24V` a `IDI0` terminale `1`
7. Verifica che `DI0` diventi `ON`
8. Rimuovi `+24V`
9. Verifica che `DI0` torni `OFF`
10. Ripeti su `IDI1`

Se non funziona, le cause piu' probabili sono:

- `ECOM` non collegato al `0V`
- `+24V` non presente o non corretto
- banco sbagliato
- connettore sbagliato
- numerazione terminali letta male

### Fase B - Test output

1. Collega solo l'ADAM output su `CON2`
2. Collega `+24V` a `PCOM0`
3. Collega `0V` a `IGND`
4. Collega il carico test tra `PCOM / +24V` e `IDO0`
4. Apri Navigator
5. Vai alla pagina Digital Output della `PCIE-1756`
6. Forza `DO0 = ON`
7. Verifica accensione LED/lampada
8. Forza `DO0 = OFF`
9. Verifica spegnimento
10. Ripeti su `DO1`

Se non funziona, le cause piu' probabili sono:

- `PCOM` non collegato al `+24V`
- `IGND` non collegato al `0V`
- assenza `24V`
- carico test collegato male
- aspettativa sbagliata sul tipo di output

---

## Regole pratiche

- partire solo da `DI0`, `DI1`, `DO0`, `DO1`
- non collegare ancora attuatori macchina veri
- non collegare `+24V` direttamente su `IDO`
- non testare output senza `PCOM` a `+24V`
- non testare output senza `IGND` a `0V`
- non testare input senza `ECOM` a `0V`
- se usi solo i canali `0-15`, basta il banco `ECOM0 / PCOM0`
- `ECOM1 / PCOM1` servono dopo per i canali `16-31`

---

## Bench result already validated

Risultato gia' confermato in banco:

- ADAM input:
  - `0V` su `ECOM`
  - portando `+24V` su un `IDIx`, il LED sull'ADAM si accende
  - lo stato input cambia correttamente in Navigator
- ADAM output:
  - `0V` su `IGND`
  - `+24V` su `PCOM`
  - con illuminatore `24V` usato come carico:
    - positivo illuminatore a `+24V`
    - enable illuminatore a `+24V`
    - ritorno `0V` illuminatore su `IDOx`
  - forzando l'uscita in Navigator, l'illuminatore si spegne e si accende correttamente

Questo conferma che il comportamento sink isolato della `PCIE-1756` e' coerente con lo schema Advantech.

---

## Criteri di successo

Il test base e' superato se:

- `IDI0` e `IDI1` cambiano correttamente in Navigator
- `IDO0` e `IDO1` pilotano correttamente il carico test
- non ci sono attivazioni spurie
- non servono ancora rele' per questi test

---

## Assunzioni operative

- alimentatore `24VDC` disponibile
- multimetro disponibile
- due `ADAM-3951-BE`, uno input e uno output
- cavo `PCL-10250-2E` sdoppiato in `CON1` e `CON2`
- per ora si testa solo la `PCIE-1756`

---

## Dopo il test Navigator

Se questo test passa:

1. segnare i risultati in `02_Commissioning_Test_Sheet.md`
2. aggiornare `05_Preconfigured_Signal_Matrix.md` con i morsetti reali confermati
3. passare al tool standalone I/O per rifare gli stessi test via software applicativo
