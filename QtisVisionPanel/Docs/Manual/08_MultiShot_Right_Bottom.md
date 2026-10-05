---
title: 08 MultiShot camere Left, Right e Bottom
description: Commissioning guidato dei profili MultiShot globali e delle correzioni per ricetta.
image: Images/02_camera_monitor.png
image_caption: Vista camere usata per verificare il risultato finale prodotto da VisionPro dopo la sequenza MultiShot.
---

## Risultato atteso

![Vista camere per il controllo del risultato MultiShot](Images/02_camera_monitor.png)

| ID | Verifica | Esito corretto | Se non e corretto |
|---|---|---|---|
| 1 | Impulsi HMI | Il controller genera il numero configurato di trigger hardware. | Controllare encoder, uscita, durata impulso e intervallo minimo. |
| 2 | Frame VisionPro | `expectedFrames` coincide con gli scatti effettivi. | Ricaricare il VPP e verificare il ToolBlock `ImageStitching`. |
| 3 | Stitching | VisionPro pubblica soltanto il risultato finale con `isReady=true`. | Verificare script PostAcquisitionRef e timeout. |
| 4 | Esito prodotto | La camera partecipa una sola volta al gruppo del prodotto corretto. | Controllare lag companion, sequenza trigger e velocita linea. |

# MultiShot Camere - Left, Right e Bottom

## Scopo

Questa pagina spiega come impostare dal pannello i profili MultiShot camera. La configurazione
fisica e i limiti sono globali macchina; ogni ricetta puo poi ereditare il profilo oppure applicare
le sole correzioni richieste dalla geometria del prodotto.

## Quando usarlo

Usare MultiShot quando VisionPro deve ricevere piu scatti della stessa camera per costruire una immagine finale stitched. L'HMI genera solo gli impulsi hardware; VisionPro acquisisce i frame e pubblica il risultato finale.

## Campi della pagina

| ID | Campo | Significato | Cosa deve fare l'operatore/tecnico |
|---|---|---|---|
| 1 | Profilo camera | Seleziona quale camera configurare: `Left / Side`, `Right / Rear`, `Bottom`. | Scegliere il profilo prima di modificare gli altri campi. |
| 2 | Enable | Abilita il MultiShot per il profilo selezionato. | Abilitare solo se il job VisionPro della camera gestisce piu frame. |
| 3 | Trigger only when machine is running | Evita trigger manuali/automatici quando la macchina non e in produzione. | Lasciare abilitato in produzione. |
| 4 | Encoder positive direction | Direzione conteggio encoder usata per raggiungere i target. | Lasciare come da commissioning; cambiare solo se l'encoder conta al contrario. |
| 5 | Max shots | Limite massimo ammesso per evitare configurazioni errate. | Impostare un valore maggiore o uguale agli scatti richiesti. |
| 6 | Session timeout | Tempo massimo per completare una sequenza MultiShot. | Aumentare se la linea e lenta o se gli scatti sono distanti. |
| 7 | Trigger output | Uscita fisica che genera gli impulsi camera. | Verificare il cablaggio: Left/Side, Right/Rear o Bottom devono avere uscite diverse. |
| 8 | Shots | Numero globale di impulsi da inviare alla camera. | Deve combaciare con il prodotto standard; una ricetta puo applicare un delta. |
| 9 | Pulse duration | Durata di ogni impulso digitale. | Usare un valore che la camera conta sempre; aumentare se perde frame. |
| 10 | Minimum time between shots | Tempo minimo tra due impulsi consecutivi. | Usare un valore compatibile con camera e acquisizione. |
| 11 | First shot offset | Posizione encoder del primo scatto, calcolata dal punto intervento camera. | Per tutte le ricette modificare base/trim macchina; per un solo prodotto usare gli offset ricetta. |
| 12 | Distance between shots | Distanza encoder tra scatti, calcolata dallo step stitching. | Cambiare `Stitching step` se serve modificare la distanza. |
| 13 | Stitching step | Distanza globale in mm tra due scatti successivi. | Impostare il passo del prodotto standard; una ricetta puo applicare un delta. |
| 14 | Camera calibration | Calibrazione mm/pixel della camera selezionata. | Impostare il valore misurato in commissioning. |
| 15 | Enable alternating illumination | Abilita i due Cycling Preset DALSA per alternare Front e Backlight. | Abilitare solo sulla camera con le due luci cablate e con job VisionPro duale. |
| 16 | Preset 1 uses front light | Definisce quale luce deve essere usata sul primo frame dopo il reset di fase. | Lasciare attivo quando VisionPro considera dispari i frame Front. |
| 17 | Camera exposure | Esposizione comune ai due preset, in microsecondi. | Partire da `500 us` e validare luminosita e assenza di mosso sulla macchina reale. |
| 18 | Front-light output | Uscita camera collegata alla luce frontale. | Selezionare la linea verificata sul cablaggio multipolare. |
| 19 | Backlight output | Uscita camera collegata alla backlight. | Deve essere diversa dall'uscita Front. |
| 20 | Active output source | Finestra temporale dell'uscita luce rispetto all'esposizione. | Usare `ExposureActive` come baseline; cambiare solo dopo prova elettrica/camera. |
| 21 | Riepilogo preset | Mostra ordine, esposizione, linee e sorgente effettivi. | Leggerlo prima di salvare; un avviso sugli scatti dispari deve essere corretto. |

## Procedura semplice

| ID | Passo | Descrizione |
|---|---|---|
| A | Seleziona profilo | Aprire `I/O Diagnostics -> Machine Configuration`, andare su `Camera MultiShot` e scegliere il profilo. |
| B | Controlla punto camera | In `Intervention Points`, verificare `CAMERA_TRIGGER_LEFT/SIDE`, `CAMERA_TRIGGER_RIGHT/REAR` o `CAMERA_TRIGGER_BOTTOM`. |
| C | Controlla uscita | Verificare che `Trigger output` corrisponda a una sola riga `Output / CameraTrigger` con scheda e canale DO cablati. Il file attivo e' quello sotto `<cartella applicazione>\cfg`. |
| D | Imposta scatti | Scrivere il numero di immagini richieste da VisionPro. |
| E | Imposta passo | Scrivere lo step stitching in mm e la calibrazione mm/px. |
| F | Salva | Premere `Save configuration`. |
| G | Ricarica job | Ricaricare la ricetta/VPP per scrivere i parametri nel ToolBlock `ImageStitching`. |
| H | Prova lenta | Far passare un prodotto a bassa velocita e verificare che la camera conti tutti gli scatti. |
| I | Configura doppia luce | Abilitare la sezione DALSA, impostare `500 us`, Front e Backlight su due linee diverse e usare un numero totale di scatti pari. |
| J | Verifica fase | Dopo il reload cercare `CAMERA_CYCLING_PRESETS_CONFIGURED`; al primo prodotto cercare `CAMERA_CYCLING_PRESET_RESET_ARMED`. |

## Doppia Illuminazione Front E Backlight

Quando una sola camera deve controllare sia la saldatura sia il rotolo girato, il numero `Shots`
indica il totale dei trigger fisici. Deve essere pari: ad esempio `10` significa 5 immagini Front
e 5 immagini Backlight. Ogni impulso produce sempre una sola immagine; e' la camera DALSA che
alterna le uscite luce tramite i Cycling Preset.

Al caricamento del job la HMI configura i due preset. Prima della prima sessione invia il reset di
fase. Se la sessione viene annullata, scade, perde l'impulso I/O oppure VisionPro entra in recovery,
la HMI non resetta immediatamente una sequenza ancora attiva: prepara il riallineamento e lo esegue
prima del prodotto successivo.

| ID | Controllo | Esito corretto | Azione in caso di errore |
|---|---|---|---|
| L1 | Numero scatti | Pari e almeno 2. | Correggere `Shots`; la configurazione non viene salvata se non valida. |
| L2 | Uscite luce | Front e Backlight su linee differenti (`Line3`/`Line4` nella baseline). | Verificare pinout del cavo e selezioni HMI. |
| L3 | Primo frame | Corrisponde a `Preset 1 uses front light`. | Verificare `frontFirst` nel ToolBlock e ricaricare il VPP. |
| L4 | Sequenza | Front, Backlight, Front, Backlight senza salti. | Verificare trigger camera, esposizione, intervallo minimo e log di recovery. |
| L5 | Risultato finale | VisionPro pubblica l'esito soltanto dopo tutti i frame. | Verificare `expectedFrames`, `isReady` e lo script duale. |

## Correzioni Nella Ricetta

Percorso: `Recipe Management -> Trigger configuration -> Recipe MultiShot corrections`.

| ID | Campo ricetta | Significato |
|---|---|---|
| R1 | Camera position offset | Sposta il punto camera solo per il prodotto selezionato. Puo essere positivo o negativo. |
| R2 | Mode: Use machine setting | Eredita lo stato MultiShot globale. E' il default delle ricette storiche. |
| R3 | Mode: Enabled for this recipe | Abilita MultiShot per questo prodotto usando I/O e limiti globali. |
| R4 | Mode: Disabled for this recipe | Mantiene il prodotto in single-shot anche se il profilo globale e' abilitato. |
| R5 | Shot count delta | Aggiunge o sottrae scatti al numero globale, senza superare `Max shots`. Con doppia illuminazione il totale effettivo deve restare pari e almeno 2. |
| R6 | Step delta | Aggiunge o sottrae millimetri allo `Stitching step` globale. |
| R7 | First-shot delta | Trasla l'origine di tutta la sequenza senza cambiare la distanza tra scatti. |
| R8 | Riepilogo Globale / Effettivo | Sotto ogni campo mostra il valore macchina, il risultato ricetta e, per gli scatti, anche il limite massimo. |

Esempio: macchina `Shots=8`, `Step=30 mm`; ricetta con `Shot count delta=+2` e
`Step delta=-1.5 mm` usa 10 scatti a 28.5 mm. VisionPro riceve automaticamente
`expectedFrames=10` e `stepMm=28.5` al caricamento ricetta.

Se il profilo globale usa l'illuminazione alternata, la HMI blocca il salvataggio
di una ricetta il cui delta produce un totale dispari: ogni prodotto deve sempre
contenere coppie complete Front/Backlight.

Leggere sempre il riepilogo azzurro prima di salvare. Per il primo scatto la HMI converte gli
impulsi globali in millimetri con la calibrazione dell'encoder principale. Se compare `valore
macchina globale non disponibile`, verificare punto intervento e counts/mm in `PCIE settings` senza
tentare di compensare il problema con un offset ricetta.

## Regola Per Operatore E Tecnico

| Caso | Dove intervenire |
|---|---|
| Tutti i prodotti scattano presto o tardi | `Machine Setup`: base/trim globale |
| Una sola ricetta scatta presto o tardi | offset posizione camera della ricetta |
| La camera perde impulsi su ogni prodotto | durata impulso/intervallo minimo globali |
| Un prodotto richiede piu o meno immagini | delta numero scatti della ricetta |
| Un prodotto richiede sovrapposizione diversa | delta step della ricetta |
| Output fisico errato | configurazione macchina, mai ricetta |

## Nomi job VisionPro

| ID | Job VisionPro | Come viene gestito dalla HMI |
|---|---|---|
| J1 | `Left` | Viene riconosciuto come ruolo `left`, mostrato nella `SideCameraView` e validato come `SideResult` con ramo Left. |
| J2 | `Right` | Viene riconosciuto come ruolo `rear`, mostrato nella `RearCameraView` e validato come `RearResult`. |
| J3 | `Bottom` | Viene riconosciuto come ruolo `bottom`, mostrato nella `BottomCameraView` e validato come `BottomResult`. |

Usare `Left` e `Right` come nomi job QuickBuild. Gli alias `Side` e `Rear` restano compatibili, ma non sono il nome preferito per nuovi job macchina.

## Note importanti

| ID | Nota | Dettaglio |
|---|---|---|
| N1 | Encoder obbligatorio | Il MultiShot usa la quota encoder reale. Se si usa solo tempo da fotocellula, il MultiShot viene saltato. |
| N2 | VisionPro fa stitching | L'HMI non crea immagini composite. Deve arrivare un risultato finale con `ImageStitching.isReady=true`. |
| N3 | Right usa Rear | La camera Right viene mostrata nella vista `RearCameraView`, senza creare una nuova vista. |
| N4 | Bottom flessibile | Bottom puo restare single-shot se MultiShot e disabilitato, oppure usare MultiShot se il profilo e abilitato. I controlli attesi sono saldatura inferiore e carta intrappolata nella saldatura. |
| N5 | Scarto finale | Da `3.0.0.2`, le camere caricate nel job attivo partecipano al NOK finale: `Left/Side`, `Right/Rear`, `Bottom` e `Front` vengono attese insieme a `Top`. |
| N6 | Timeout camera | Se una camera prevista non restituisce risultato entro timeout, il pezzo viene marcato NOK con motivo camera mancante. |
| N7 | Diagnostica FrameAck | Da `3.0.0.9`, il log `MULTISHOT_FRAME_ACK` non e piu atteso: la lettura diretta del ToolBlock VisionPro da thread di background e stata disabilitata per sicurezza COM. |
| N8 | Overview camere | L'Overview mostra le camere del VPP attivo dopo l'inizializzazione VisionPro. Se aperta troppo presto puo mostrare il fallback XML, poi si riallinea al rientro nella pagina. |
| N9 | Popup timeout | Da `3.0.1.0`, un timeout companion genera anche una notifica popup non modale per l'operatore. Il popup si chiude da solo dopo circa 10 secondi ed e limitato per non disturbare la produzione con messaggi ripetuti. |
| N10 | Reset fase DALSA | Da `3.0.8.5`, il reset Cycling Preset viene armato prima della prima sessione e dopo anomalie, mai durante una sessione MultiShot attiva. |

## Logica esito finale

| ID | Caso | Comportamento |
|---|---|---|
| F1 | Top + Left/Side | Il risultato Left/Side entra in `SideResult`; saldatura laterale e conteggio rotoli possono generare NOK/scarto. |
| F2 | Top + Right/Rear | Il risultato Right/Rear entra in `RearResult`; la saldatura laterale destra puo generare NOK/scarto. |
| F3 | Top + Bottom | Il risultato Bottom entra in `BottomResult`; saldatura inferiore e carta intrappolata possono generare NOK/scarto. |
| F4 | Top + piu camere | Il pezzo viene chiuso solo dopo avere raccolto tutte le camere attese o dopo timeout. |
| F5 | Camera non arrivata | Viene creato difetto tecnico `CameraMissing`; il pezzo e NOK e puo essere scartato. |

## Diagnostica dopo release 3.0.0.9

| ID | Messaggio log | Significato | Cosa controllare |
|---|---|---|---|
| D1 | `COMPANION_ARRIVED` | Una camera companion e arrivata entro la finestra di attesa del prodotto Top. | Verificare `role`, `waited` e che il valore sia compatibile con il tempo reale di stitching. |
| D2 | `COMPANION_TIMEOUT` | Una camera prevista non ha prodotto il risultato finale in tempo. Da `3.0.1.0` viene mostrato anche un popup warning temporaneo. | Controllare `missing` e `queues`: `empty` indica nessun risultato; `count/headSequence/lagMs/valid` mostrano se un risultato e' presente ma non appartiene al TOP corrente. Poi verificare `ImageStitching.isReady`, scatti e trigger. |
| D3 | `COMPANION_PRODUCT_MISMATCH` | Il risultato companion appartiene probabilmente al prodotto successivo e non viene consumato dal Top corrente. | Verificare sequenza trigger, tempo di stitching e velocita linea. |
| D4 | `CAMERA_TIMING` | Mostra il ritardo tra Top e ogni camera companion usata nel gruppo. | Usare il valore per tarare timeout, step MultiShot e velocita linea. |
| D5 | `MULTISHOT_*` | Diagnostica del controller impulsi encoder. | Verificare `shot`, `target`, `current`, `overshoot`, durata impulso e intervallo minimo. |
| D6 | `CAMERA_CYCLING_PRESETS_CONFIGURED` | I due preset DALSA sono stati scritti dopo il caricamento VPP. | Verificare esposizione, linee Front/Back e nome camera. |
| D7 | `CAMERA_CYCLING_PRESET_RESET_ARMED` | La fase e' stata riallineata per la sessione successiva. | Deve comparire al primo prodotto e dopo cancel/recovery. |
| D8 | `CAMERA_CYCLING_PRESET_*_FAILED` | Configurazione o reset GenICam non riusciti. | Fermare la prova; verificare modello camera, feature firmware, job/FIFO e cablaggio. |
| D9 | `MULTISHOT_<PROFILE>_OUTPUT_MAPPING_RESOLVED` | Il trigger del profilo e' stato associato a una sola uscita fisica. Per Left il profilo nei log e' normalmente `SIDE`. | Verificare che scheda e DO nel messaggio corrispondano al cablaggio. |
| D10 | `MULTISHOT_<PROFILE>_OUTPUT_MAPPING_INVALID` | Il profilo e' abilitato ma l'uscita e' assente, duplicata o non e' una `Output / CameraTrigger` valida. | Correggere la tabella Machine outputs e salvare; non compensare con offset ricetta. |

Il messaggio `MULTISHOT_FRAME_ACK` era una diagnostica sperimentale per leggere `frameIndex/isReady` dopo ogni impulso. Nella baseline corrente non viene collegata al job VisionPro per evitare accessi COM non sicuri. Questo non cambia il funzionamento produttivo: VisionPro continua a restituire solo il risultato finale stitched.

## Output VisionPro attesi

| ID | Camera | Output supportati |
|---|---|---|
| V1 | Left/Side | `LeftSideSealingOk`, `LeftRollCountOk`, `SideSealingOk`, `SealingOk`, `RollCountOk`, `RollsCountOk`, `RollCount` oppure output legacy laterali. |
| V2 | Right/Rear | `RightSideSealingOk`, `RearSideSealingOk`, `RightSealingOk`, `RearSealingOk`, `SideSealingOk`, `SealingOk` oppure `SealingArea`. |
| V3 | Bottom | Consigliati: `BottomSealingOk` e `NoTrappedPaper`. Alias pass saldatura: `BottomSealOk`, `LowerSealingOk`, `SaldaturaInferioreOk`. Alias carta: `TrappedPaperOk`, `PaperClear`, `CartaIntrappolataOk`. |

## Controlli Bottom

| ID | Controllo | Significato | Output VisionPro consigliato |
|---|---|---|---|
| B1 | Saldatura inferiore | Verifica che la saldatura sotto il prodotto sia presente e conforme. | `BottomSealingOk = true` |
| B2 | Carta intrappolata | Verifica che non ci sia carta/prodotto intrappolato nella saldatura inferiore. | `NoTrappedPaper = true` |

Se una di queste ispezioni e abilitata in ricetta ma l'output non arriva dal job VisionPro, il pezzo viene considerato NOK. In commissioning verificare quindi prima i nomi output nel ToolBlock Bottom.
