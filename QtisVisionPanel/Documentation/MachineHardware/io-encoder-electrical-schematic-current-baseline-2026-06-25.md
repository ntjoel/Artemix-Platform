# IO And Encoder Electrical Schematic - Current Runtime Baseline

Data: 2026-06-25  
Progetto: `QtisVisionPanel`  
Scopo: schema elettrico operativo per schede I/O, encoder, trigger camera e scarto secondo il funzionamento attuale dell'applicazione.

Questo documento e' pensato per collaudo, cablaggio e manutenzione. Non sostituisce i manuali Advantech e i datasheet dei sensori/attuatori: prima di collegare la macchina verificare sempre serigrafia morsetti, tensioni, correnti massime e schema elettrico finale del quadro.

---

## 1. Baseline applicativa usata

Riferimenti letti nella repo:

- `ConfigurationTemplates/machine_runtime_config.template.xml`
- `Docs/Commissioning/05_Preconfigured_Signal_Matrix.md`
- `Docs/Commissioning/06_PCIE1756_2xADAM3951_Navigator_Test.md`
- `Docs/Commissioning/07_PCIE1884_ADAM3937_First_Encoder_Test.md`
- `Documentation/MachineHardware/virtual-conveyor-io-trigger-2026-04-27.md`
- `Documentation/MachineHardware/right-bottom-multishot-expansion-2026-06-09.md`
- `Documentation/MachineHardware/side-left-multishot-trigger-2026-06-03.md`
- `Models/AdvantechDeviceManager.cs`
- `Services/MachineController.cs`
- `Services/MultiShotTrigger/MultiShotTriggerController.cs`

Schede previste:

| Funzione | Scheda | Terminal board | Cavo | Uso |
|---|---|---|---|---|
| I/O digitali macchina | `PCIE-1756-BE` | `ADAM-3951-BE` | `PCL-10250-2E` | fotocellule, trigger camera, scarto, allarmi, heartbeat |
| Encoder trasporto | `PCIE-1884-AE` | `ADAM-3937-BE` | `PCL-10137H-3E` | conteggio encoder principale `Counter0 / ENC1` |

Regola importante:

- il file reale macchina puo' essere diverso dal template;
- prima di cablare, verificare il file runtime effettivo caricato dall'HMI;
- il template e' la baseline applicativa, non la prova finale del cablaggio reale.

---

## 2. Schema a blocchi

```text
+24Vdc macchina
  |
  +-- Fotocellule / sensori ingresso
  |      |
  |      +-- PCIE-1756-BE DI00..DI31 tramite ADAM-3951-BE CON1
  |
  +-- Carichi trigger / rele' / ingressi optoisolati camera / solenoide scarto
  |      |
  |      +-- PCIE-1756-BE DO00..DO31 tramite ADAM-3951-BE CON2
  |          Uscite sink: il carico va tra +24V e IDOx
  |
  +-- Encoder trasporto alimentato dal suo alimentatore/datasheet
         |
         +-- A/B/Z e GND verso PCIE-1884-AE Counter0 tramite ADAM-3937-BE

QtisVisionPanel
  |
  +-- DI00 fotocellula crea PRODUCT_ZERO
  +-- PCIE-1884 Counter0 aggiorna posizione macchina
  +-- intervention points generano trigger camera/scarto alla quota configurata
  +-- MultiShot genera N impulsi encoder-driven su Side/Left, Right/Rear o Bottom
```

---

## 3. Note elettriche obbligatorie

1. Le uscite `PCIE-1756-BE` sono usate come uscite **sink**.
   - `PCOM` va a `+24V`.
   - `IGND` va a `0V`.
   - il carico va tra `+24V / PCOM` e `IDOx`.
   - quando il software mette l'uscita `ON`, `IDOx` chiude verso `0V`.

2. Non collegare direttamente un ingresso camera se richiede un comando PNP/source a `+24V`.
   - Usare rele' di interfaccia, optoisolatore o convertitore NPN-to-PNP.
   - Verificare il tipo di ingresso trigger della camera e del controller luci.

3. Per solenoidi, rele' e carichi induttivi:
   - usare modulo rele' o interfaccia adeguata alla corrente;
   - aggiungere diodo/soppressore secondo polarita' e componente;
   - proteggere l'alimentazione con fusibile dedicato.

4. L'emergenza e le sicurezze macchina non devono dipendere dall'HMI.
   - Il circuito di sicurezza deve togliere potenza agli attuatori per via hardware.
   - Le uscite HMI comandano funzioni macchina, non sono safety-rated.

5. L'encoder va alimentato secondo il suo datasheet.
   - Non portare `24V` sui pin segnale della `PCIE-1884`.
   - Portare alla scheda solo segnali `A/B/Z` e riferimento `GND`.

---

## 4. Ingressi digitali PCIE-1756

### 4.1 Cablaggio banco input validato

Terminal board: `ADAM-3951-BE` su `CON1`.

```text
Alimentatore 24Vdc:
  +24V  -> sensore marrone / positivo sensore
  0V    -> sensore blu/grigio
  0V    -> ADAM-3951 ECOM0 terminale 17

Fotocellula PNP:
  uscita sensore -> ADAM-3951 IDI0 terminale 1 -> PCIE-1756 DI00
```

Risultato atteso:

- sensore libero: `DI00 = OFF`;
- prodotto davanti alla fotocellula: `DI00 = ON`;
- HMI: nasce un prodotto tracciato `PRODUCT_ZERO`.

Se si usa sensore NPN o contatto pulito, adeguare il cablaggio e la polarita' nel file macchina. La baseline applicativa e' `ActiveHigh`.

### 4.2 Tabella ingressi baseline

| Funzione | Segnale software | Scheda | Canale | Morsetto ADAM | Polarita' | Note |
|---|---|---|---|---|---|---|
| Fotocellula prodotto | `IN_PRODUCT_PHOTOCELL` | `PCIE-1756-BE` | `DI00` | CON1 `IDI0` terminale `1` | `ActiveHigh` | evento master per tracking prodotto |
| Consenso trigger esterno | `IN_EXTERNAL_TRIGGER_ENABLE` | `PCIE-1756-BE` | `DI01` | CON1 `IDI1` terminale `2` | `ActiveHigh` | opzionale, usato se logica esterna abilita trigger |

Parametri runtime collegati:

| Parametro | Valore baseline | Effetto |
|---|---|---|
| `ProductPhotocellSignalCode` | `IN_PRODUCT_PHOTOCELL` | ingresso che crea `PRODUCT_ZERO` |
| `ExternalTriggerEnableSignalCode` | `IN_EXTERNAL_TRIGGER_ENABLE` | consenso esterno, se usato |
| `PhotocellDebounceMs` | `80 ms` | filtro rimbalzi fotocellula |
| `MinimumRetriggerGapMs` | `120 ms` | distanza minima temporale tra due prodotti |

---

## 5. Uscite digitali PCIE-1756

### 5.1 Cablaggio uscita sink validato

Terminal board: `ADAM-3951-BE` su `CON2`.

```text
Alimentatore 24Vdc:
  +24V -> ADAM-3951 PCOM0 terminale 17
  0V   -> ADAM-3951 IGND

Carico generico:
  +24V / PCOM0 -> carico + 
  carico -     -> ADAM-3951 IDOx

Quando DOx = ON:
  IDOx chiude verso 0V e il carico si attiva.
```

Per trigger camera:

```text
+24V camera/opto input comune
  |
  +-- ingresso trigger camera / opto / rele' interfaccia
        |
        +-- IDOx PCIE-1756
```

Se la camera richiede impulso PNP, TTL, 5V o un ingresso differente, non collegare direttamente `IDOx`: usare interfaccia dedicata.

### 5.2 Tabella uscite baseline corrente

| Funzione | Segnale software | Scheda | Canale | Morsetto ADAM atteso | Polarita' | Note runtime |
|---|---|---|---|---|---|---|
| Spare diagnostico | `OUT_SPARE_DO00` | `PCIE-1756-BE` | `DO00` | CON2 `IDO0` terminale `1` | `ActiveHigh` | lasciare libero salvo test esplicito |
| Trigger Side / Left | `OUT_CAMERA_SIDE_TRIGGER` | `PCIE-1756-BE` | `DO01` | CON2 `IDO1` terminale `2` | `ActiveHigh` | camera Side, oppure Left se il job runtime e' Left |
| Trigger Top | `OUT_CAMERA_TOP_TRIGGER` | `PCIE-1756-BE` | `DO02` | CON2 `IDO2` terminale `3` | `ActiveHigh` | camera Top |
| Scarto monoattuatore | `OUT_REJECT_SOLENOID` | `PCIE-1756-BE` | `DO03` | CON2 `IDO3` terminale `4` da verificare su serigrafia | `ActiveHigh` | uscita scarto NOK |
| Trigger Rear / Right | `OUT_CAMERA_REAR_TRIGGER` | `PCIE-1756-BE` | `DO04` | CON2 `IDO4` terminale `5` da verificare su serigrafia | `ActiveHigh` | camera Rear, oppure Right se il job runtime e' Right |
| Trigger Bottom | `OUT_CAMERA_BOTTOM_TRIGGER` | `PCIE-1756-BE` | `DO05` | CON2 `IDO5` terminale `6` da verificare su serigrafia | `ActiveHigh` | camera Bottom, singolo scatto o MultiShot |
| Allarme generale | `OUT_GENERAL_ALARM` | `PCIE-1756-BE` | `DO06` | CON2 `IDO6` terminale `7` da verificare su serigrafia | `ActiveHigh` | allarme macchina generale |
| Heartbeat diagnostico | `OUT_HEARTBEAT` | `PCIE-1756-BE` | `DO07` | CON2 `IDO7` terminale `8` da verificare su serigrafia | `ActiveHigh` | lampeggio comunicazione/hardware |

Nota sui morsetti:

- `IDO0=1`, `IDO1=2`, `IDO2=3` sono gia' usati nei documenti di banco;
- per `IDO3` e successivi la numerazione e' indicata come riferimento operativo, ma va confermata sulla serigrafia `ADAM-3951-BE` e sul manuale della morsettiera installata.

### 5.3 Uscite allarme scarto configurate da Config.xml

La parte allarmi scarto puo' usare uscite configurate in `Config.xml`, non solo nel template runtime.

Valori tipici della baseline documentata:

| Funzione | Configurazione | Canale tipico | Nota |
|---|---|---|---|
| Allarme bloccante | alarm card (`OutputChannelId`) | `DO00`-`DO31` | `BlockingAlarmOutput` = `DO40` in `Config.xml` e' legacy e fuori range |
| Allarme non bloccante | alarm card (`OutputChannelId`) | `DO00`-`DO31` | `NonBlockingAlarmOutput` = `DO41` in `Config.xml` e' legacy e fuori range |

Regola pratica:

- non cablare `DO40/DO41`: sono i vecchi default legacy di `Config.xml`, fuori range per la PCIE-1756 (32 DO); il canale reale e' quello della alarm card;
- se gli allarmi comandano torretta, PLC o rele', usare interfaccia adeguata;
- la pagina allarmi scarto puo' mostrare il canale configurato, ma il cablaggio finale deve seguire il file realmente caricato.

---

## 6. Encoder PCIE-1884

### 6.1 Encoder principale previsto

| Funzione | Segnale software | Scheda | Canale | Note |
|---|---|---|---|---|
| Encoder trasporto principale | `ENC_CONVEYOR_MAIN` | `PCIE-1884-AE` | `Counter0 / ENC1` | unico canale attivo di default nel device manager |

Parametri baseline:

| Parametro | Valore template | Effetto |
|---|---:|---|
| `PulsesPerRevolution` | `2048` | impulsi encoder per giro |
| `TrackingMode` | `Quadrature x4` | moltiplica gli impulsi per 4 |
| `MillimetersPerRevolution` | `100` | sviluppo meccanico per giro, tarabile con tachimetro |
| `PhotocellMachineOffsetMm` | `35.5` | posizione fotocellula rispetto a zero macchina |
| `RejectOffsetMm` | `350` | default punto scarto |

Conversione usata dal runtime:

```text
counts/mm = PulsesPerRevolution * trackingMultiplier / MillimetersPerRevolution

Con template:
counts/mm = 2048 * 4 / 100 = 81.92 counts/mm
```

Se durante commissioning si usa tachimetro, l'HMI ricalcola `MillimetersPerRevolution`. Quel valore e' macchina, non ricetta.

### 6.2 Cablaggio encoder differenziale consigliato

Terminal board: `ADAM-3937-BE` su `PCIE-1884-AE`.

| Segnale encoder | Pin / morsetto PCIE-1884 | Note |
|---|---|---|
| `A+` | `CNT0_CLK+/A+` pin `2` | canale A positivo |
| `A-` | `CNT0_CLK-/A-` pin `20` | canale A negativo |
| `B+` | `CNT0_AUX+/B+` pin `3` | canale B positivo |
| `B-` | `CNT0_AUX-/B-` pin `21` | canale B negativo |
| `Z+` opzionale | `CNT0_GATE+/Z+` pin `4` | zero encoder, se usato |
| `Z-` opzionale | `CNT0_GATE-/Z-` pin `22` | zero encoder, se usato |
| `GND` | pin `1`, `14`, `17`, `32` o `35` | riferimento segnali |

Schema:

```text
Encoder line driver
  +V encoder  -> alimentatore encoder secondo datasheet
  0V encoder  -> 0V alimentatore -> GND PCIE-1884
  A+          -> pin 2
  A-          -> pin 20
  B+          -> pin 3
  B-          -> pin 21
  Z+ opz.     -> pin 4
  Z- opz.     -> pin 22
```

### 6.3 Cablaggio encoder single-ended

Usare solo se l'encoder non ha uscite differenziali.

| Segnale encoder | Pin / morsetto PCIE-1884 | Note |
|---|---|---|
| `A` | `CNT0_CLK+/A+` pin `2` | positivo A |
| `B` | `CNT0_AUX+/B+` pin `3` | positivo B |
| `Z` opzionale | `CNT0_GATE+/Z+` pin `4` | zero opzionale |
| `GND` | pin `1` o altro `GND` disponibile | riferimento |

Non usare i pin `20`, `21`, `22` come ingressi positivi. Seguire il manuale Advantech se serve polarizzazione/terminazione specifica.

---

## 7. Flusso applicativo attuale

### 7.1 Con encoder reale

Configurazione macchina:

| Parametro | Valore atteso |
|---|---|
| `UseEncoderTrigger` | `true` |
| `MainEncoderAxisCode` | `ENC_CONVEYOR_MAIN` |
| encoder fisico | `PCIE-1884 Counter0 / ENC1` |

Sequenza:

1. `DI00 / IN_PRODUCT_PHOTOCELL` rileva il prodotto.
2. Il runtime crea un prodotto tracciato con riferimento `PRODUCT_ZERO`.
3. Il conteggio `Counter0` avanza con il nastro.
4. Ogni `MachineInterventionPoint` viene convertito da millimetri a conteggi encoder.
5. Quando la posizione target viene raggiunta, il runtime genera l'impulso sull'uscita configurata.
6. Se il prodotto e' NOK, alla quota scarto viene impulsato `OUT_REJECT_SOLENOID`.

Punti intervento template:

| Punto | Quota da `PRODUCT_ZERO` | Uscita | Impulso |
|---|---:|---|---:|
| `CAMERA_TRIGGER_TOP` | `74 mm` | `OUT_CAMERA_TOP_TRIGGER` | `40 ms` |
| `CAMERA_TRIGGER_SIDE` | `76 mm` | `OUT_CAMERA_SIDE_TRIGGER` | `40 ms` |
| `CAMERA_TRIGGER_REAR` | `76 mm` | `OUT_CAMERA_REAR_TRIGGER` | `40 ms` |
| `CAMERA_TRIGGER_BOTTOM` | `76 mm` | `OUT_CAMERA_BOTTOM_TRIGGER` | `40 ms` |
| `REJECT` | `350 mm` | `OUT_REJECT_SOLENOID` | `80 ms` |

### 7.2 Senza encoder reale, solo per commissioning

Per test senza encoder:

- usare `UseEncoderTrigger=true`;
- abilitare `VirtualConveyorEnabled=true`;
- impostare velocita' virtuale in `m/min`;
- la fotocellula reale resta su `DI00`, ma la quota macchina avanza virtualmente.

Questa modalita' e' utile per banco e commissioning. In produzione con encoder installato, la posizione deve arrivare dal conteggio reale.

### 7.3 Fallback a tempo

Se si usa logica a tempo da fotocellula:

- `UseEncoderTrigger=false`;
- `TriggerSchedulingMode=TimedFromPhotocell`;
- i delay camera vengono interpretati come tempo, non come quota encoder.

Questa modalita' non e' quella consigliata per MultiShot encoder-driven.

---

## 8. MultiShot camera

La HMI non fa stitching immagine. La HMI genera solo N impulsi hardware; VisionPro acquisisce, stitchea e restituisce il risultato finale.

Profili macchina:

| Profilo HMI | Ruolo runtime compatibile | Uscita trigger | Vista usata |
|---|---|---|---|
| Side / Left | `Side`, `Left`, `SideLeft` | `OUT_CAMERA_SIDE_TRIGGER` | `SideCameraView` |
| Rear / Right | `Rear`, `Right`, `SideRight` | `OUT_CAMERA_REAR_TRIGGER` | `RearCameraView` |
| Bottom | `Bottom` | `OUT_CAMERA_BOTTOM_TRIGGER` | `BottomCameraView` |

Parametri elettrici importanti:

| Parametro | Significato |
|---|---|
| `TriggerPulseMs` | durata ON di ogni impulso camera |
| `MinimumInterShotIntervalMs` | distanza minima temporale tra impulsi, protezione per camera/VisionPro |
| `ShotCount` | numero di foto richieste |
| `InitialOffsetPulses` | distanza encoder tra fotocellula/prodotto e primo scatto |
| `StepPulses` | distanza encoder tra uno scatto e il successivo |
| `TargetLateTolerancePulses` | tolleranza se il target e' gia' passato |

Note cablaggio:

- il MultiShot usa la stessa uscita fisica del trigger camera;
- il carico elettrico vede piu' impulsi ravvicinati;
- verificare con oscilloscopio o DAQNavi che ampiezza e durata siano compatibili con la camera;
- se camera o controller luci non contano tutti gli impulsi, aumentare `TriggerPulseMs` o `MinimumInterShotIntervalMs`.

---

## 9. Schema morsetti consigliato per quadro

Questa tabella e' la proposta ordinata per la baseline attuale. Confermare sempre sul quadro reale.

### 9.1 Input

| Morsetto quadro | ADAM / canale | Collegare a | Descrizione |
|---|---|---|---|
| `XDI-00` | CON1 `IDI0` / `DI00` | uscita fotocellula prodotto | crea prodotto |
| `XDI-01` | CON1 `IDI1` / `DI01` | consenso trigger esterno opzionale | abilita logica esterna |
| `XDI-COM0` | CON1 `ECOM0` | `0V` sensori | comune input banco 0 |

### 9.2 Output

| Morsetto quadro | ADAM / canale | Collegare a | Descrizione |
|---|---|---|---|
| `XDO-COM0` | CON2 `PCOM0` | `+24V` output | comune positivo uscite banco 0 |
| `XDO-GND` | CON2 `IGND` | `0V` output | riferimento output |
| `XDO-01` | CON2 `IDO1` / `DO01` | trigger camera Side/Left tramite interfaccia | `OUT_CAMERA_SIDE_TRIGGER` |
| `XDO-02` | CON2 `IDO2` / `DO02` | trigger camera Top tramite interfaccia | `OUT_CAMERA_TOP_TRIGGER` |
| `XDO-03` | CON2 `IDO3` / `DO03` | rele' scarto / solenoide tramite protezione | `OUT_REJECT_SOLENOID` |
| `XDO-04` | CON2 `IDO4` / `DO04` | trigger camera Rear/Right tramite interfaccia | `OUT_CAMERA_REAR_TRIGGER` |
| `XDO-05` | CON2 `IDO5` / `DO05` | trigger camera Bottom tramite interfaccia | `OUT_CAMERA_BOTTOM_TRIGGER` |
| `XDO-06` | CON2 `IDO6` / `DO06` | rele' allarme generale | `OUT_GENERAL_ALARM` |
| `XDO-07` | CON2 `IDO7` / `DO07` | lampada diagnostica opzionale | `OUT_HEARTBEAT` |

### 9.3 Encoder

| Morsetto quadro | ADAM-3937 / pin | Collegare a | Descrizione |
|---|---|---|---|
| `XENC-A+` | pin `2` | encoder `A+` | Counter0 |
| `XENC-A-` | pin `20` | encoder `A-` | Counter0 |
| `XENC-B+` | pin `3` | encoder `B+` | Counter0 |
| `XENC-B-` | pin `21` | encoder `B-` | Counter0 |
| `XENC-Z+` | pin `4` | encoder `Z+` opzionale | zero opzionale |
| `XENC-Z-` | pin `22` | encoder `Z-` opzionale | zero opzionale |
| `XENC-GND` | pin `1` o altro `GND` | `0V` encoder / riferimento segnale | comune segnali |
| `XENC-+V` | fuori scheda PCIE-1884 | alimentatore encoder | tensione secondo datasheet |

---

## 10. Collaudo minimo dopo cablaggio

1. Test alimentazioni:
   - `+24V` stabile;
   - `0V` comune coerente;
   - fusibili e protezioni presenti.

2. Test input:
   - in DAQNavi/Navigator `DI00` cambia con la fotocellula;
   - nell'HMI `IN_PRODUCT_PHOTOCELL` cambia stato;
   - non ci sono rimbalzi multipli a prodotto fermo.

3. Test output:
   - forzare un'uscita per volta da diagnostica;
   - verificare con multimetro/oscilloscopio sul morsetto;
   - non collegare subito attuatori reali senza rele'/protezione.

4. Test encoder:
   - a nastro fermo il conteggio resta stabile;
   - muovendo il nastro il conteggio avanza nel verso atteso;
   - la velocita' HMI e' coerente dopo taratura tachimetro.

5. Test trigger:
   - passare un prodotto;
   - verificare `DI00`;
   - controllare che il trigger camera arrivi alla quota configurata, non subito sulla fotocellula;
   - verificare durata impulso.

6. Test MultiShot:
   - abilitare solo il profilo necessario;
   - controllare `ShotCount`;
   - verificare gli impulsi con oscilloscopio;
   - verificare che VisionPro riceva tutti i frame prima del risultato.

7. Test scarto:
   - simulare NOK;
   - verificare che `OUT_REJECT_SOLENOID` si attivi solo alla quota scarto;
   - verificare che E-stop e sicurezze taglino potenza attuatore.

---

## 11. Diagnostica rapida

| Sintomo | Controllo elettrico | Controllo software |
|---|---|---|
| Fotocellula vista da LED ma non da HMI | `ECOM0` a `0V`, uscita sensore su `IDI0` | `IN_PRODUCT_PHOTOCELL -> DI00`, polarita' `ActiveHigh` |
| Uscita camera non triggera | `PCOM0=+24V`, `IGND=0V`, carico tra `+24V` e `IDOx` | canale `OUT_CAMERA_*`, punto intervento abilitato |
| Camera perde impulsi MultiShot | impulso troppo corto, interfaccia lenta | aumentare `TriggerPulseMs` / `MinimumInterShotIntervalMs` |
| Trigger arriva subito dopo fotocellula | encoder non avanza o offset nullo | `UseEncoderTrigger`, `VirtualConveyorEnabled`, `InitialOffsetPulses`, punto intervento |
| Velocita' encoder errata | A/B rumorosi, GND, schermatura, verso | `MillimetersPerRevolution`, `TrackingMode`, taratura tachimetro |
| Scarto fuori posizione | cablaggio uscita giusta, rele' lento | `REJECT BaseOffsetMm`, `TrimOffsetMm`, `RejectPulseMs` |
| Allarme su canale inatteso | controllare il morsetto del canale assegnato alla alarm card | `OutputChannelId` della alarm card (i campi `Config.xml` sono legacy) |

---

## 12. Regola finale per modifiche future

Se in macchina il cablaggio reale differisce da questa baseline:

1. non cambiare significato dei segnali a memoria;
2. aggiornare prima il file macchina reale;
3. aggiornare la documentazione di commissioning;
4. verificare con DAQNavi e HMI;
5. solo dopo considerare il cablaggio come baseline confermata.

