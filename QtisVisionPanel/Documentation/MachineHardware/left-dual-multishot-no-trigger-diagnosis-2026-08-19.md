# Left Dual MultiShot - Diagnosi Camera Senza Acquisizioni

## Sintomo

Il VPP contiene il job `Left`, ma la camera non acquisisce i frame della
sequenza dual MultiShot.

## Causa verificata il 2026-08-19

Il file preparato in Documenti non era il file letto dall'eseguibile. Nel file
operativo precedente il profilo `MachineMultiShotTrigger.Side` risultava
`Enabled=false` e usava ancora `OUT_CAMERA_SIDE_TRIGGER`.

Nel file preparato era inoltre presente due volte
`OUT_CAMERA_LEFT_TRIGGER`:

- una riga corretta `Output / CameraTrigger / PCIE-1756-BE / DO02`;
- una riga non valida `Input / PresenceSensor / DO06`.

La riga non valida e' stata rimossa dal file di commissioning dopo averne
creato una copia di backup. Il file operativo locale e' stato a sua volta
salvato in backup prima dell'allineamento.

## File Che Comanda Davvero La Macchina

Il servizio carica:

```text
<cartella dell'eseguibile>\cfg\machine_runtime_config.xml
```

Per una build locale Release il percorso e' normalmente:

```text
bin\x64\Release\cfg\machine_runtime_config.xml
```

`machine_runtime_config.template.xml` e' un modello. Anche se contiene i valori
corretti, non produce alcun trigger finche' non viene caricato o copiato nel
percorso operativo. La sostituzione manuale va eseguita solo con HMI chiusa e
dopo un backup.

## Valori Verificati Per Il Profilo Left

| Parametro | Valore | Interpretazione |
|---|---:|---|
| Profilo XML | `Side` | Alias runtime del ruolo fisico Left. |
| Enabled | `true` | Il controller arma la sequenza alla fotocellula. |
| Trigger output | `OUT_CAMERA_LEFT_TRIGGER` | Segnale logico richiesto. |
| Mapping fisico | `PCIE-1756-BE / DO02` | Unica uscita CameraTrigger ammessa. |
| Shots | `8` | 8 trigger totali: 4 Front + 4 Backlight. |
| Initial offset | `3034` count | Circa 376 mm con 8,068 count/mm. |
| Step | `245` count | Circa 30,4 mm tra trigger. |
| Pulse | `15 ms` | Tempo alto dell'uscita camera. |
| Minimum interval | `50 ms` | Distanza temporale minima tra impulsi. |
| Dual illumination | `true` | Cycling Preset Front/Backlight richiesti. |
| Exposure | `500 us` | Esposizione comune dei due preset. |
| Camera lines | `Line3 / Line4` | Front e Backlight. |

Le quote sono coerenti tra loro. Questa correzione non modifica la logica
encoder o la posizione del primo scatto.

## Correzione Sessione Ferma Al Primo Frame - 3.0.9.5

Il caso osservato con uscita fisica attiva una sola volta e VisionPro fermo a
`Dual frame 1/8` ha evidenziato un difetto nel watchdog software. Il timeout di
`3000 ms` veniva sempre misurato dal fronte fotocellula. Se il primo target
veniva raggiunto vicino alla scadenza, il primo impulso era valido ma la
sessione poteva essere cancellata prima del secondo.

Da `3.0.9.5` il comportamento e':

1. prima del primo impulso, il timeout parte dalla fotocellula;
2. dopo ogni impulso, il timeout riparte dall'ultimo scatto;
3. prima di cancellare, il timer ricontrolla che nel frattempo non sia iniziato
   un nuovo impulso;
4. il log riporta `before first shot` oppure `after shot N/8`, insieme a
   `lastEncoder`.

La correzione non genera scatti a tempo: ogni impulso continua a richiedere il
raggiungimento del relativo target encoder. Non modifica neppure l'alternanza
Front/Backlight, che resta gestita dai Cycling Preset della camera.

## Causa Aggiuntiva Confermata Sul Campo - 3.0.9.6

Dopo la 3.0.9.5, il sintomo (uscita fisica su `DO02` attiva una sola volta, VisionPro fermo
a `Dual frame 1/8`) e' stato osservato di nuovo. Questo esclude che il solo timeout scorrevole
della 3.0.9.5 fosse la causa completa.

Causa individuata: `ArmProfile` (che arma la sessione e, in Dual Illumination, invoca
`DualIlluminationPhaseCoordinator.PrepareSession` per il reset del cycling preset
Front/Backlight della camera) veniva chiamato in modo SINCRONO da `OnProductTriggerReceived`,
sullo STESSO thread hardware che consegna anche tutti gli eventi encoder della macchina
(`DigitalIOViewModel.OnCounterChanged`, per TUTTI i profili MultiShot, non solo Left). Il reset
del cycling preset e' un comando GenICam reale verso la camera: se lento o bloccato, quel
thread resta fermo e nessun ulteriore evento encoder viene consegnato, per nessun profilo,
finche' il comando non ritorna. Il primo scatto (gia' pianificato) parte comunque, ma i
successivi restano fermi finche' il blocco persiste — anche oltre il timeout scorrevole,
perche' senza eventi encoder il timer non ha nulla da rivalutare fino a quando il thread non
si libera.

Coerente con l'osservazione "single fa tutti gli scatti, dual si ferma al primo": in
modalita' single-shot `PrepareSession` e' un no-op immediato (nessun comando camera), quindi
il thread di polling non viene mai trattenuto.

Da `3.0.9.6`, `OnProductTriggerReceived` non chiama piu' `ArmProfile` inline: l'armo del
profilo (incluso l'eventuale reset camera) e la prima valutazione encoder girano su un task
in background, per profilo, cosi' il thread di polling condiviso resta libero indipendentemente
dai tempi di risposta della camera. Il calcolo di target/encoder/timeout non e' cambiato.

Verifica log aggiuntiva: se il sintomo si ripresenta ancora dopo la 3.0.9.6, il thread di
polling NON e' piu' la causa; concentrarsi allora su `CAMERA_CYCLING_PRESET_*_FAILED` o
`SESSION_PREPARATION_FAILED` (fase DALSA non pronta, fail-safe attivo) come prossimo sospetto.

## Causa Aggiuntiva Confermata Sul Campo - 3.0.9.8

Dopo la 3.0.9.7, sul campo sono comparsi due errori distinti da
`DualIlluminationPhaseCoordinator` per il profilo `Side` (Left):

1. A macchina IN MARCIA, durante una modifica live delle impostazioni Dual
   Illumination dall'interfaccia: `CAMERA_CYCLING_PRESET_CONFIG_FAILED|...
   Node is not writable. (feature: cyclingPresetMode)`. Comportamento GenICam
   atteso: la camera rifiuta la riprogrammazione dei Cycling Preset mentre e'
   in acquisizione. Non e' un difetto software; e' un fail-safe corretto.
   Effetto pratico: se una impostazione Dual Illumination viene modificata a
   macchina in marcia, il profilo Left resta senza dual MultiShot per tutti i
   prodotti successivi finche' la macchina non si ferma e la configurazione
   viene riapplicata con successo (VPP ricaricato o riavvio).
2. A macchina FERMA, lo stesso profilo ha continuato a fallire con un errore
   diverso: `CAMERA_CYCLING_PRESET_CONFIG_FAILED|...FeatureError: feature not
   found: cP_ExposureTime`. Questo NON dipende dallo stato di acquisizione
   della camera (macchina ferma) - e' un problema di sequenza nella
   configurazione GenICam.

**Causa (2):** confrontando con l'esempio ufficiale Teledyne ("Multi-Exposure
Cycling Example Setup", manuale Genie/M640 Cycling Preset), la sequenza
corretta e': selezionare prima `cP_PresetConfigurationSelector`, POI
`cP_FeaturesActivationSelector`/`cP_FeaturesActivationMode`, poi scrivere il
valore (`cP_ExposureTime`, ecc.) per quel preset. Il codice in
`IgigaCameraAccess.ConfigureDualIlluminationCyclingPresets` attivava
`cP_FeaturesActivationSelector`/`Mode` per tre feature (ExposureTime, linea
Front, linea Back) PRIMA di selezionare un preset con
`cP_PresetConfigurationSelector`, in ordine invertito rispetto all'esempio
Teledyne. Confermato anche via CamExpert sulla camera Left reale (Nano-M1450,
firmware `10CA18.0008`): `cP_ExposureTime` risultava `access=RO` con
`cP_FeaturesActivationMode=Off` per il selettore corrente, coerente con
un'attivazione che non si "aggancia" correttamente senza un preset gia'
selezionato.

Da `3.0.9.8`, `ConfigureDualIlluminationCyclingPresets` imposta
`cP_PresetConfigurationSelector=1` prima delle chiamate di attivazione
(`ActivateCyclingFeature`), allineando l'ordine a quello documentato da
Teledyne. Nessun'altra modifica alla sequenza esistente (contatore preset,
sorgente incrementale, reset, ecc.).

Verifica sul campo richiesta: ricaricare il job Left a macchina ferma e
controllare che compaia `CAMERA_CYCLING_PRESETS_CONFIGURED` (non piu'
`CAMERA_CYCLING_PRESET_CONFIG_FAILED` con `feature not found`), poi ripetere
la prova di commissioning con 8 scatti fisici su DO02.

Nota separata sul punto 1: evitare di modificare le impostazioni Dual
Illumination mentre la macchina e' in marcia; farlo a macchina ferma, oppure
ricaricare il job/VPP dopo la modifica prima di rimettere in produzione.

## Contratto Obbligatorio

Per avviare la sessione devono essere veri tutti i punti seguenti:

1. Il file operativo contiene `Side.Enabled=true`.
2. `OUT_CAMERA_LEFT_TRIGGER` compare una sola volta in `MachineOutputs` come
   `Output / CameraTrigger`, con DO valido.
3. Il job QuickBuild e' riconosciuto come `Left` o alias Side/Left compatibile.
4. Il ToolBlock `ImageStitching` contiene gli input richiesti.
5. La camera DALSA espone e accetta le feature Cycling Preset configurate.
6. Il reset di fase riesce prima del prodotto.
7. Il main encoder avanza nella direzione configurata e raggiunge i target.

La doppia illuminazione usa un fail-safe intenzionale: se ToolBlock, camera o
reset di fase non sono pronti, l'intera sessione Left viene bloccata. In quel
caso non devono comparire trigger fisici parziali.

## Sequenza Log Attesa

Riavviare la HMI, caricare la ricetta e cercare gli eventi in questo ordine:

1. `MULTISHOT_SIDE_CONFIG_LOADED` con `enabled=True`, `shots=8` e output Left.
2. `MULTISHOT_SIDE_OUTPUT_MAPPING_RESOLVED` con `PCIE-1756-BE` e `DO02`.
3. `VISIONPRO_STITCHING_PARAMS_APPLIED` per il job Left.
4. `CAMERA_CYCLING_PRESETS_CONFIGURED` per la camera Left.
5. Al primo prodotto: `CAMERA_CYCLING_PRESET_RESET_ARMED`.
6. `MULTISHOT_SIDE_PLAN_CREATED` e `MULTISHOT_SIDE_SESSION_STARTED`.
7. Otto eventi `MULTISHOT_SIDE_TRIGGER_PULSE`, da `1/8` a `8/8`.
8. `MULTISHOT_SIDE_SESSION_COMPLETED`.

Fermarsi al primo passo mancante:

| Ultimo evento presente | Area da controllare |
|---|---|
| Nessun `CONFIG_LOADED` | File non caricato o servizio non inizializzato. |
| `enabled=False` | File operativo ancora vecchio. |
| `OUTPUT_MAPPING_INVALID` | Tabella Machine outputs duplicata o incompleta. |
| Manca `STITCHING_PARAMS_APPLIED` | Nome job, ToolBlock o input VisionPro. |
| `CAMERA_CYCLING_PRESET_*_FAILED` | Modello/firmware camera, FIFO, accesso GenICam o feature preset. |
| `SESSION_PREPARATION_FAILED` | Fase DALSA non pronta; trigger bloccati in fail-safe. |
| `SESSION_STARTED` senza 8 pulse | Encoder, direzione, target, intervallo minimo o timeout. |
| 8 pulse ma meno di 8 frame | Cablaggio DO02, polarita', durata impulso, input trigger camera o limite frame-rate. |

Dettaglio dei nuovi timeout:

| Evento | Interpretazione | Azione |
|---|---|---|
| `SESSION_TIMEOUT ... before first shot` | Il prodotto non ha raggiunto il primo target entro `SessionTimeoutMs`. | Verificare velocita', quota `CAMERA_TRIGGER_LEFT`, direzione encoder e aumentare il timeout solo se fisicamente necessario. |
| `SESSION_TIMEOUT ... after shot 1/8` | Il primo fronte e' uscito, ma non e' iniziato il secondo entro il limite. | Controllare che `lastEncoder` aumenti, che la macchina resti Running e che non avvenga un reload/cambio configurazione. |
| `CANCELLED ... machine stopped` o `continuous run stopped` | La sessione e' stata annullata dal runtime, non dal calcolo delle quote. | Risolvere prima eventuali errori VisionPro/Rear o stop macchina. |
| `TRIGGER_PULSE_FAILED` | La scrittura fisica non e' riuscita. | Controllare scheda PCIE-1756, DO02 e stato hardware. |

## Prova Di Commissioning

1. Macchina ferma: verificare elettricamente che `DO02` raggiunga l'ingresso
   trigger della DALSA Left.
2. Avviare con un solo prodotto e velocita' ridotta.
3. Verificare prima i quattro eventi di preparazione.
4. Contare 8 fronti su DO02 con oscilloscopio o diagnostica scheda.
5. Contare 8 frame camera e verificare alternanza 4 Front / 4 Backlight.
6. Verificare un solo risultato finale VisionPro con `isReady=true`.
7. Ripetere su almeno 100 prodotti alla velocita' massima prevista.

Con i valori verificati (`3034` count iniziali e `245` count di step), i target
sono `R+3034`, `R+3279`, `R+3524`, `R+3769`, `R+4014`, `R+4259`, `R+4504` e
`R+4749`, dove `R` e' il conteggio encoder al fronte fotocellula.

La build software puo' verificare configurazione e sequenza log, ma non puo'
certificare cablaggio, ricezione fisica dei fronti o commutazione delle luci.
