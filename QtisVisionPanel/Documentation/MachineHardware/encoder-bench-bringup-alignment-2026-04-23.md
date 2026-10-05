# Encoder Bench Bring-Up Alignment - 2026-04-23

## Scopo

Allineare il progetto completo locale con il ramo commissioning reale che stiamo seguendo sul banco per:

- `PCIE-1884`
- `PCL-10137H-3E`
- `ADAM-3937-BE`

Questo documento non introduce ancora la logica encoder completa nel runtime del main project.

Serve a fissare:

- riferimenti elettrici e di pinout confermati dal manuale
- ordine corretto dei test
- regole di prudenza da mantenere prima di passare al software completo

---

## Hardware confermato per il ramo encoder

- scheda encoder/counter:
  - `PCIE-1884`
- cavo:
  - `PCL-10137H-3E`
- terminal board:
  - `ADAM-3937-BE`

Canale di prima validazione:

- `ENC1 -> Counter0`

---

## Pin Counter0 confermati dal manuale

- `CNT0_CLK+/A+` -> pin `2`
- `CNT0_CLK-/A-` -> pin `20`
- `CNT0_AUX+/B+` -> pin `3`
- `CNT0_AUX-/B-` -> pin `21`
- `CNT0_GATE+/Z+` -> pin `4`
- `CNT0_GATE-/Z-` -> pin `22`
- `GND` -> pin `1`, `14`, `17`, `32`, `35`

---

## Regola tecnica importante

Per il ramo encoder, il comportamento prudente da mantenere e':

- alimentare l'encoder secondo il suo datasheet
- usare `ADAM-3937-BE` come breakout del connettore `PCIE-1884`
- non trattare la terminal board come una scheda attiva da alimentare "a banco" senza conferma di pin dedicati

Finche' non abbiamo il datasheet completo dell'encoder reale usato:

- niente `24V` portati a caso sulla morsettiera
- niente assunzioni su pin di alimentazione non confermati

---

## Flusso commissioning corretto

Ordine stabilito per il banco:

1. identificare il tipo encoder:
   - single-ended
   - differenziale / line driver
   - pulse/direction
2. cablare solo `Counter0`
3. alimentare l'encoder con la sua tensione corretta
4. verificare il conteggio in `DAQNavi / Navigator`
5. verificare stabilita' a fermo
6. verificare conteggio in movimento
7. verificare il verso
8. solo dopo passare al tool standalone I/O
9. solo dopo passare a tracking prodotto e intervention points

---

## Allineamento documentale

I documenti da usare come riferimento operativo sono ora:

- `Docs/Commissioning/07_PCIE1884_ADAM3937_First_Encoder_Test.md`
- `Docs/Commissioning/01_IO_Encoder_Commissioning_Plan.md`
- `Docs/Commissioning/02_Commissioning_Test_Sheet.md`
- `Docs/Commissioning/05_Preconfigured_Signal_Matrix.md`

Lato tool standalone, il documento parallelo e':

- `C:\Users\chouikha\QuatisVisionPanelIO\Documentation\MachineHardware\encoder-bench-bringup-pcie1884-adam3937-2026-04-23.md`

---

## Stato

Ad oggi:

- ramo I/O digitale validato in banco
- catena `fotocellula -> software -> DO -> illuminatore` validata nello standalone
- ramo encoder documentato e pronto per il primo bring-up banco
- integrazione runtime encoder nel progetto completo ancora da validare dopo il test banco reale

---

## Allineamento runtime encoder

Dal debug reale sul banco e' emerso un punto importante sul ramo `PCIE-1884`:

- non c'e' evidenza di una condizione macchina nascosta richiesta prima di avviare `ENC1`
- il comportamento osservato dipendeva invece dall'arming del controller encoder nello standalone

Learning confermato:

- se `DAQNavi Navigator` avviava prima `Counter0`, il tool standalone riusciva poi a leggere `ENC1`
- questo indicava che il tool non stava inizializzando il controller `UdCounterCtrl` nello stesso modo di Navigator

Fix allineato:

- la gestione encoder deve impostare esplicitamente `ChannelStart`
- deve usare lo stato `Enabled` del controller `UdCounterCtrl`
- reset / preset / start / stop / read devono sempre lavorare sul canale selezionato

Questo fix e' stato applicato subito nello standalone e riportato anche nel progetto completo locale sul manager Advantech, per mantenere allineato il ramo encoder tra i due progetti.

Hardening successivo emerso dal banco:

- un primo fix faceva reagire `ENC1`, ma il count poteva ancora comparire per un attimo e poi tornare a zero
- la causa piu' probabile era la concorrenza tra polling manager e refresh UI sullo stesso controller encoder

Direzione confermata:

- serializzare l'accesso al controller `UdCounterCtrl`
- evitare letture encoder duplicate dalla UI in modalita' hardware reale
- usare il polling del manager e gli eventi `CounterChanged` come sorgente principale per il live update

Allineamento successivo emerso dal banco:

- il ramo encoder del progetto completo locale e' stato riallineato anche sul set esplicito di `Channel` e `ChannelStart`
- questo segue il comportamento che stiamo validando nello standalone dopo il mismatch osservato tra:
  - `DAQNavi Navigator`, capace di far reagire `Counter0`
  - tool standalone, che ancora non manteneva il count encoder accumulato in modo stabile

Interpretazione aggiornata:

- il wiring encoder non e' il sospetto principale
- il punto aperto resta il path software di arming / read continuo del contatore nel tool standalone
- il progetto completo locale mantiene lo stesso allineamento low-level per non divergere dal ramo che stiamo ancora chiudendo sul banco

Aggiornamento successivo dal banco:

- il ramo standalone ora prova la lettura encoder tramite `UdCounterCtrl.Read(out data)` prima del fallback alla proprieta' `Value`
- questo e' stato introdotto per avvicinare il tool al path di lettura nativo del driver

Nota di configurazione banco:

- il runtime standalone attivo e' ancora configurato con `2048 PPR`
- l'encoder banco Lika collegato in questo test e' `500 PPR`
- di conseguenza la velocita' calcolata nel tool puo' risultare molto piu' bassa del reale finche' il template encoder non viene allineato al banco di prova

Estensione diagnostica successiva:

- nello standalone e' stata aggiunta anche la lettura raw della frequenza impulsi encoder tramite `FreqMeterCtrl`
- questa metrica resta separata dal count cumulativo usato dal runtime macchina

Regola da mantenere allineata:

- `count` cumulativo = base del tracking posizione
- `pulse frequency Hz` = diagnostica raw per confronto con `DAQNavi Navigator`

---

## Aggiornamento 2026-04-24 - come viene convertita la velocita'

Learning consolidato dal banco:

- nella `v0.6.26` dello standalone il valore `m/min` ha ripreso a muoversi
- nella `v0.6.27` la diagnostica raw `pulse frequency` non e' ancora risultata variabile nel setup banco corrente

Interpretazione corretta:

- il valore `m/min` oggi usato nel commissioning deriva dal count cumulativo encoder campionato nel tempo
- non dipende in modo primario dalla diagnostica raw `FreqMeterCtrl`

Formula corrente da tenere come riferimento:

- `countsPerMillimeter = PulsesPerRevolution / MillimetersPerRevolution`
- `deltaMillimeters = deltaCount / countsPerMillimeter`
- `speedMetersPerMinute = abs(deltaMillimeters) * 60 / (1000 * elapsedSeconds)`

Note pratiche:

- `count` resta la sorgente canonica per tracking, trigger e reject
- `m/min` e' una grandezza derivata dal count
- la velocita' mostrata oggi e' un modulo, non una velocita' signed
- la `pulse frequency Hz` resta utile per confronto diagnostico con `DAQNavi Navigator`, ma non e' ancora la sorgente primaria del runtime macchina

Conseguenza per il progetto completo:

- quando porteremo il ramo encoder nel runtime finale, la sorgente principale dovra' restare il count cumulativo
- la diagnostica raw in `Hz` potra' restare disponibile per debug, ma non deve guidare da sola il tracking linea
