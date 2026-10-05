# Linea Guida Implementazione Macchina Pacchi A Quattro Telecamere

## Scopo

Questa guida definisce l'ordine sicuro per completare la macchina pacchi con i
job VisionPro:

- `Top`;
- `Left`;
- `Right`;
- `Bottom`.

La baseline di partenza del piano era la release `3.0.8.5`, commit `a526d26`.
La release `3.0.8.6` aggiunge viste HMI dedicate e responsive per Left/Right. Sono gia'
disponibili il backup pre-integrazione, l'inventario read-only, i profili
MultiShot per Left/Right/Bottom, le correzioni ricetta e la sincronizzazione dei
Cycling Preset DALSA Front/Backlight.

Questa guida non autorizza un refactor del ciclo macchina. Ogni fase deve essere
chiusa con una prova e un punto di rollback prima di passare alla successiva.

Documenti da tenere aperti durante il lavoro:

- `pre-four-camera-package-integration-backup-2026-08-03.md`;
- `four-camera-read-only-inventory-2026-08-03.md`;
- `recipe-camera-and-multishot-offsets-2026-07-14.md`;
- `side-left-multishot-trigger-2026-06-03.md`;
- `right-bottom-multishot-expansion-2026-06-09.md`;
- `dalsa-dual-illumination-cycling-presets-2026-08-04.md`;
- `visionpro-alternating-dual-multishot-job-script.vb`.

## Legenda Stato

| Stato | Significato |
|---|---|
| `COMPLETATO` | Presente nel codice e verificato con build. |
| `VPP DA PREPARARE` | Richiede modifica e prova in VisionPro QuickBuild. |
| `COMMISSIONING` | Richiede cablaggio, seriale, quota o misura sulla macchina reale. |
| `CODICE DA COMPLETARE` | Manca ancora un comportamento HMI esplicito. |
| `GATE` | La fase successiva non deve iniziare finche' il criterio non e' soddisfatto. |

## Stato Della Baseline

| Area | Stato | Nota |
|---|---|---|
| Backup locale, Git, runtime e OneDrive | `COMPLETATO` | Punto di ripristino verificato del 3 agosto 2026. |
| Ruoli, queue e viste Top/Left/Right/Bottom | `COMPLETATO` | Left e Right hanno viste dedicate; Right resta normalizzato internamente come `rear` solo nel runtime. |
| Layout overview multi-camera | `COMPLETATO` | Fino a quattro camere per riga; dalla quinta viene usata una seconda riga con display ridimensionati. |
| Orchestrazione risultati companion | `COMPLETATO` | Top resta l'ancora prodotto; collaudo quattro camere ancora necessario. |
| Offset camera per ricetta | `COMPLETATO` | Quote globali macchina e correzioni prodotto restano separate. |
| Correzioni MultiShot per ricetta | `COMPLETATO` | Profili `SideLeft`, `RightRear`, `Bottom`. |
| Scrittura parametri `ImageStitching` | `COMPLETATO` | `expectedFrames`, `stepMm`, `mmPerPixel` e input dual opzionali. |
| Cycling Preset DALSA | `COMPLETATO` | Configurazione/reset implementati; prova elettrica reale ancora richiesta. |
| Validator Left | Parziale | Saldatura e rotolo esistono, ma il ramo Left e' ancora dedotto dagli output. |
| Validator Right | Parziale | Saldatura presente; controllo rotolo Right assente. |
| Validator Bottom | `COMPLETATO` | Saldatura inferiore e carta intrappolata con fail-safe su output mancanti. |
| VPP quattro job produttivo | `VPP DA PREPARARE` | Nessun VPP verificato contiene insieme i quattro job richiesti. |
| Mapping fisico Left/Right | `COMMISSIONING` | Gli alias attuali devono essere sostituiti da una scelta fisica univoca. |
| Contatori per lato | `CODICE DA COMPLETARE` | Left e Right condividono ancora alcune chiavi difetto e flag produzione. |
| Preflight ricetta manuale/OPC | `CODICE DA COMPLETARE` | Deve diventare un controllo unico per job e feature richiesti. |

## Architettura Target

```text
Fotocellula prodotto
        |
        v
PRODUCT_ZERO + quota encoder del prodotto
        |
        +--> punto TOP ------> impulso camera Top ------> job Top
        +--> punto LEFT -----> N impulsi MultiShot -----> job Left
        +--> punto RIGHT ----> N impulsi MultiShot -----> job Right
        +--> punto BOTTOM ---> 1 oppure N impulsi ------> job Bottom
        |
        v
queue per ruolo -> orchestratore con Top come ancora -> validatori
        |
        v
un solo esito prodotto -> contatori -> DB -> eventuale scarto
```

Vincoli:

1. Un impulso fisico corrisponde a una sola immagine acquisita.
2. Lo stitching resta nel Job Script VisionPro.
3. La HMI non raccoglie e non compone immagini.
4. VisionPro pubblica un risultato solo dopo l'ultimo frame della sessione.
5. Left e Right hanno un mapping I/O fisico univoco, senza alias reali
   concorrenti.
6. Una sola camera tra Left e Right esegue il controllo rotolo con backlight.
7. Un prodotto genera un solo incremento del totale e un solo comando di
   scarto, anche quando piu' camere rilevano difetti.

## Confine Macchina, Ricetta E Runtime

### Configurazione macchina

Devono restare in `machine_runtime_config.xml`:

- board, channel, direzione e polarita' di ogni segnale;
- seriale e ruolo fisico camera;
- encoder, PPR, counts/mm e direzione;
- quota nominale dei punti Top, Left, Right, Bottom e Reject;
- output trigger e durata impulso;
- scatti nominali, step nominale, intervallo minimo e timeout MultiShot;
- calibrazione `mmPerPixel`;
- configurazione Cycling Preset, uscite luce ed esposizione;
- lato fisico che possiede la backlight.

La baseline attuale puo' ricavare il lato backlight dal solo profilo
`DualIllumination.Enabled`. La configurazione deve rispettare l'invariante:
**al massimo uno tra Side/Left e Right/Rear puo' avere il dual illumination
abilitato**. Una futura validazione HMI incrociata deve bloccare una seconda
abilitazione invece di accettarla silenziosamente.

### Configurazione ricetta

Devono restare nell'XML ricetta:

- dimensioni e tolleranze del prodotto;
- feature di ispezione e di scarto abilitate;
- offset camera specifici del formato;
- modo MultiShot `Machine`, `Enabled` o `Disabled`;
- delta numero scatti, step e primo scatto;
- eventuale abilitazione del controllo rotolo per quel prodotto.

La ricetta non deve contenere board, canali, seriali, polarita', esposizione
elettrica delle luci o counts/mm.

### Solo runtime

Restano in memoria:

- prodotti tracciati e relativo `productId`;
- sessioni MultiShot attive;
- fase Front/Backlight corrente;
- risultati in attesa per ruolo;
- esito aggregato, notifiche e diagnostica temporanea.

## Matrice Funzionale Target

| Job | Acquisizione | Immagine finale | Controlli minimi | Output canonici |
|---|---|---|---|---|
| `Top` | Single-shot | Top | controlli Top esistenti | contratto esistente |
| `Left` | MultiShot | stitching front; opzionale stitching backlight | saldatura laterale; rotolo solo se lato designato | `LeftSideSealingOk`; opzionale `LeftRollCountOk` |
| `Right` | MultiShot | stitching front; opzionale stitching backlight | saldatura laterale; rotolo solo se lato designato | `RightSideSealingOk`; opzionale `RightRollCountOk` |
| `Bottom` | Single-shot o MultiShot per ricetta | frame o stitching Bottom | saldatura inferiore; carta intrappolata | `BottomSealingOk`; `NoTrappedPaper` |

Tutti gli output sono `Boolean` e usano la semantica `true = controllo OK`.
Non usare per lo stesso significato un output che a volte indica `OK` e a volte
indica `difetto rilevato`.

## Fase 1 - Congelare Il Contratto VisionPro

### Attivita'

- [ ] Creare un VPP di commissioning con i job chiamati esattamente `Top`,
  `Left`, `Right`, `Bottom`.
- [ ] Assegnare a ogni job la FIFO della camera fisica corretta.
- [ ] Inserire `ImageStitching` solo nei job che possono usare MultiShot.
- [ ] Pubblicare gli output canonici della matrice funzionale.
- [ ] Verificare tipo e direzione di ogni terminale nel ToolBlock `Results`.
- [ ] Fare in modo che i frame intermedi restituiscano `False` dal Job Script.
- [ ] Pubblicare il risultato del job solo dopo lo stitching finale.

### Contratto `ImageStitching`

Input minimi:

| Nome | Tipo | Significato |
|---|---|---|
| `expectedFrames` | `Double` | Numero totale di trigger fisici. |
| `stepMm` | `Double` | Distanza fisica tra due trigger consecutivi. |
| `mmPerPixel` | `Double` | Calibrazione della camera. |
| `dualIlluminationEnabled` | `Boolean` | Abilita separazione Front/Backlight. |
| `frontFirst` | `Boolean` | Definisce la fase del primo frame dopo il reset. |

Output diagnostici raccomandati:

- `isReady`;
- `frameIndex`;
- `status`;
- `errorMessage`;
- `sealingImage`;
- `rollImage`;
- `frontFrameIndex`;
- `backFrameIndex`;
- `frontReady`;
- `backReady`;
- `phase`.

Nel dual mode:

```text
expectedFrames = trigger fisici totali = 2 * frame per illuminazione
stepMm         = distanza tra due trigger fisici consecutivi
phaseStepMm    = 2 * stepMm
```

Lo script di riferimento instrada `sealingImage` al ToolBlock
`SealingInspection` e `rollImage` al ToolBlock `RollInspection`. I nomi possono
essere configurati negli input opzionali dello script, ma devono restare uguali
tra tutti i VPP della macchina.

### Gate Fase 1

Aprendo il VPP in QuickBuild, per ogni job devono risultare:

- FIFO valida;
- ruolo e nome corretti;
- terminali presenti e tipizzati;
- nessun risultato sui frame intermedi;
- un solo risultato finale per sessione.

Salvare hash SHA-256 e copia del VPP validato prima di collegarlo alla HMI.

## Fase 2 - Definire Ruoli, Seriali E Mapping I/O

### Decisioni elettriche obbligatorie

- [ ] Scegliere il canale definitivo di `OUT_CAMERA_LEFT_TRIGGER`.
- [ ] Scegliere il canale definitivo di `OUT_CAMERA_RIGHT_TRIGGER`.
- [ ] Confermare `OUT_CAMERA_TOP_TRIGGER` e `OUT_CAMERA_BOTTOM_TRIGGER`.
- [ ] Verificare che tutti siano `Direction=Output`, categoria
  `CameraTrigger`, polarita' corretta e `RealSignal=true`.
- [ ] Eliminare l'ambiguita' Side/Left e Rear/Right: una camera deve avere un
  solo output reale autorevole.
- [ ] Registrare seriale e ruolo di Top, Left, Right e Bottom in
  `CameraConfig.xml`.
- [ ] Verificare che `Config.xml -> NumCamera` e il VPP descrivano lo stesso
  numero di job.

Le uscite camera `Line3` e `Line4` per le luci non sono uscite PCIE-1756. Sono
uscite GenICam della camera DALSA e devono essere collegate ai rispettivi
ingressi di controllo delle luci.

### Gate Fase 2

Da diagnostica manuale, con macchina ferma:

- ogni comando trigger deve commutare una sola uscita fisica;
- deve acquisire una sola camera;
- nessun canale di allarme, reject o heartbeat deve cambiare;
- la vista HMI deve mostrare il job nel pannello previsto.

## Fase 3 - Commissionare Encoder E Quote Globali

### Ordine

1. Verificare conteggio encoder stabile all'avvio.
2. Calibrare counts/mm con tachimetro e salvare il valore macchina.
3. Confermare direzione positiva.
4. Fissare `PRODUCT_ZERO` sulla fotocellula reale.
5. Misurare le quote nominali Top, Left, Right, Bottom e Reject.
6. Impostare base e trim macchina senza usare offset ricetta.
7. Verificare un prodotto singolo a bassa velocita'.

Formula quota runtime:

```text
quota effettiva = base macchina + trim commissioning + offset ricetta
```

Gli offset ricetta non devono compensare counts/mm errati, una fotocellula
instabile o un punto macchina mal misurato.

### Gate Fase 3

Per 20 prodotti singoli, ogni camera deve scattare nella stessa zona del
prodotto senza deriva progressiva. Il secondo prodotto deve avere la stessa
precisione del primo.

## Fase 4 - Commissionare Il MultiShot Standard

Eseguire separatamente Left, Right e Bottom. Non abilitare ancora il dual
illumination.

Per ogni profilo:

- [ ] scegliere l'output trigger definitivo;
- [ ] impostare `ShotCount` iniziale a 2;
- [ ] impostare durata impulso compatibile con l'ingresso camera;
- [ ] misurare il primo scatto dalla quota camera;
- [ ] impostare step fisico e `mmPerPixel`;
- [ ] impostare un intervallo minimo superiore al tempo reale di riarmo camera;
- [ ] verificare `SessionTimeoutMs` con margine sulla durata completa;
- [ ] verificare N impulsi HMI, N frame camera, un risultato VisionPro;
- [ ] aumentare gradualmente N fino al valore produttivo.

Relazioni utili:

```text
velocita_mm_s = velocita_m_min * 1000 / 60
intervallo_trigger_ms = stepMm / velocita_mm_s * 1000
frequenza_trigger_Hz = 1000 / intervallo_trigger_ms
durata_finestra_mm = (expectedFrames - 1) * stepMm
```

Con 60 m/min la velocita' nastro e' 1000 mm/s. Uno step di 10 mm produce un
trigger ogni 10 ms, cioe' 100 Hz. L'esposizione di `500 us` non garantisce da
sola questa frequenza: vanno considerati readout, trasferimento GigE, buffer e
riarmo. Per la camera dichiarata a 76 fps standard, il periodo teorico e'
circa 13,2 ms; a 150 fps TurboDrive e' circa 6,7 ms. Usare sempre un margine e
verificare il modo realmente attivo sulla camera.

Per la produzione continua:

```text
prodotti_s = velocita_mm_s / passo_prodotto_mm
trigger_medi_s = prodotti_s * expectedFrames
```

La sessione deve terminare prima che il prodotto successivo richieda la stessa
camera. Se le finestre si sovrappongono, non ridurre i controlli di sicurezza:
ridurre N, aumentare lo step compatibilmente con la copertura, aumentare il
passo prodotto o usare una camera/modalita' piu' veloce.

### Gate Fase 4

Eseguire 100 sessioni per profilo. Non sono ammessi:

- frame mancanti;
- sessioni `TARGET_TOO_LATE`;
- immagini spezzate per indice errato;
- risultati intermedi;
- recovery VisionPro durante il normale ciclo.

## Fase 5 - Abilitare Il Dual MultiShot Sul Solo Lato Rotolo

### Regola

Scegliere in commissioning `Left` oppure `Right`. L'altro lato resta
MultiShot standard con sola luce frontale.

### Procedura

1. Collegare luce frontale a `Line3` e backlight a `Line4`, o registrare i nomi
   reali se diversi.
2. Impostare esposizione iniziale `500 us`.
3. Abilitare `DualIllumination` sul solo profilo scelto.
4. Impostare un numero di scatti totale pari.
5. Ricaricare il VPP e verificare
   `CAMERA_CYCLING_PRESETS_CONFIGURED`.
6. Al primo prodotto verificare
   `CAMERA_CYCLING_PRESET_RESET_ARMED`.
7. Confermare sequenza Front, Backlight, Front, Backlight.
8. Verificare che ogni immagine frontale entri nello stitching saldatura e
   ogni immagine backlight nello stitching rotolo.
9. Forzare cancel, timeout e recovery; il prodotto seguente deve ripartire
   dalla fase configurata senza reset durante una sessione attiva.

### Gate Fase 5

Per 100 prodotti consecutivi:

```text
trigger fisici totali: N
frame acquisiti dalla camera: N
frame assegnati alla saldatura: N/2
frame assegnati al rotolo: N/2
immagini stitched finali: 2
risultati finali del job: 1
```

## Fase 6 - Completare I Contratti HMI Mancanti

Questa fase richiede codice, release incrementale, build e documentazione.

### 6.1 Riconoscimento Left dal ruolo

Oggi `IToolBlockValidator` entra nel ramo Left principalmente riconoscendo gli
output. Modificare il contratto affinche' il ruolo risolto del job sia passato
esplicitamente al validator. Se il job e' `Left` e manca un output obbligatorio,
deve produrre un NOK tecnico chiaro, non ricadere nel validator Side legacy.

### 6.2 Controllo rotolo su Right

Se Right e' il lato backlight:

- aggiungere output canonico `RightRollCountOk`;
- aggiungere discovery feature per ruolo `rear/right`;
- validare il booleano fail-safe;
- riportare camera role e output usato nel dettaglio difetto;
- mantenere compatibilita' con ricette storiche senza abilitazione.

### 6.3 Identita' difetti per lato

Non usare un solo stato mutabile per due camere. La chiave interna minima deve
essere:

```text
productId + cameraRole + feature
```

L'esito prodotto resta aggregato, ma il dettaglio deve distinguere almeno:

- Left side sealing;
- Right side sealing;
- Left roll count oppure Right roll count;
- Bottom sealing;
- Trapped paper.

I contatori visualizzati possono restare aggregati per categoria se richiesto,
ma ogni contributo deve essere deduplicato prima dell'incremento.

### 6.4 Preflight unico ricetta/job

Creare un solo servizio usato da:

- caricamento manuale;
- autoswitch;
- cambio OPC UA;
- avvio produzione.

Il preflight deve bloccare l'avvio quando una feature richiesta dalla ricetta
non ha job, ruolo o output obbligatorio. Il messaggio deve indicare ricetta,
job, ruolo, feature e terminale mancante.

### Gate Fase 6

Test automatici o di integrazione devono coprire:

- output Left mancante -> NOK tecnico Left;
- output Right roll mancante quando richiesto -> NOK tecnico Right;
- feature Bottom richiesta senza job Bottom -> preflight fallito;
- due difetti dello stesso tipo su due camere -> un solo esito prodotto, due
  dettagli ruolo, nessun doppio incremento accidentale;
- ricetta legacy Top/Side -> comportamento invariato.

## Fase 7 - Completare La Configurazione Ricetta

Usare i campi gia' presenti per:

- offset Top, Left, Right e Bottom;
- modo MultiShot per SideLeft, RightRear e Bottom;
- delta scatti, step e primo scatto;
- abilitazione Bottom sealing e Trapped paper.

Prima di aggiungere nuovi campi, decidere se l'operatore deve abilitare
separatamente saldatura Left e saldatura Right. Se la risposta e' si, aggiungere
campi espliciti e retrocompatibili, con fallback dal vecchio `Side_sealing`.
Non riutilizzare lo stesso checkbox con significati diversi.

Per il controllo rotolo, il lato fisico resta macchina; la ricetta decide solo
se il controllo e' richiesto per quel prodotto.

### Gate Fase 7

- una ricetta storica deve deserializzare senza migrazione obbligatoria;
- cambiare ricetta non deve modificare il file macchina;
- cambio manuale, autoswitch e OPC UA devono produrre gli stessi parametri
  effettivi;
- tornando alla ricetta precedente, quote e MultiShot devono tornare ai valori
  precedenti.

## Fase 8 - Diagnostica Di Commissioning

La pagina tecnica deve mostrare per ogni camera:

- ruolo, nome job e seriale;
- output logico, board, channel e polarita';
- quota globale, offset ricetta e quota effettiva;
- profilo single/MultiShot/dual;
- scatti richiesti, generati e completati;
- frame attesi e risultato finale ricevuto;
- step mm, step impulsi e intervallo teorico alla velocita' corrente;
- fase luce attesa e ultimo reset preset;
- durata sessione, timeout e ultima anomalia;
- output `Results` obbligatori trovati/mancanti.

Non reintrodurre polling COM frame-by-frame da worker background. La
diagnostica deve usare snapshot prodotti sul thread VisionPro, eventi del
controller e risultati finali gia' disponibili.

## Fase 9 - Collaudo Integrato

### Sequenza Incrementale

1. Top soltanto.
2. Top + Left standard.
3. Top + Right standard.
4. Top + Bottom single-shot.
5. Top + Bottom MultiShot.
6. Top + Left + Right.
7. Abilitazione dual sul lato rotolo.
8. Tutte e quattro le camere.
9. Prodotti distanziati.
10. Prodotti consecutivi al passo minimo reale.

### Matrice Prove Minima

| Caso | Risultato atteso |
|---|---|
| Tutti i controlli OK | Total +1, Good +1, nessun reject. |
| NOK Left sealing | Total +1, No Good +1, un reject, dettaglio Left. |
| NOK Right sealing | Total +1, No Good +1, un reject, dettaglio Right. |
| NOK roll | Total +1, No Good +1, un reject, dettaglio del lato backlight. |
| NOK Bottom sealing | Total +1, No Good +1, un reject, dettaglio Bottom. |
| Carta intrappolata | Total +1, No Good +1, un reject, dettaglio Bottom. |
| Due difetti sullo stesso prodotto | Total +1, No Good +1, un reject, due motivi. |
| Companion mancante | NOK tecnico o stop controllato secondo policy, mai Good. |
| Cambio ricetta OPC UA | Stessa risoluzione parametri del cambio manuale. |
| Cancel MultiShot | Nessun risultato parziale; preset riallineato al prodotto seguente. |
| Riavvio HMI | Mapping, quote, profili e calibrazioni persistenti. |

### Soak Test

Eseguire almeno:

- 500 prodotti alla velocita' nominale;
- 100 prodotti alla velocita' massima prevista;
- un cambio ricetta manuale;
- un cambio ricetta OPC UA;
- uno stop/start macchina;
- una recovery VisionPro controllata.

Criteri:

- un risultato aggregato per prodotto;
- nessun mismatch tra Total e Good + No Good;
- nessun doppio incremento difetto;
- nessuna crescita continua delle queue;
- nessun frame nero o spezzato;
- nessun trigger perso;
- nessuna fase luce invertita dopo recovery;
- reject associato al prodotto corretto.

## Fase 10 - Release, Installer E Documentazione

Per ogni slice software:

1. incrementare la release con lo script canonico;
2. aggiornare `code-changes-log.md` e `software-version-archive.md`;
3. indicare impatto su config macchina, ricetta, DB e procedura operativa;
4. compilare `Release x64`;
5. aggiornare il manuale operatore soltanto per i controlli realmente esposti;
6. aggiornare le procedure commissioning;
7. rigenerare/verificare l'installer quando cambiano EXE, DLL, template o
   script;
8. sincronizzare la baseline OneDrive;
9. creare un commit circoscritto e un nuovo punto di rollback.

## Strategia Di Rollback

| Fase | Rollback |
|---|---|
| VPP | Ripristinare VPP e hash precedenti; non modificare XML ricetta. |
| Mapping I/O | Ripristinare backup `machine_runtime_config.xml`. |
| MultiShot | Disabilitare il singolo profilo e tornare al single-shot/asset precedente. |
| Dual illumination | Disabilitare `DualIllumination`, salvare e ricaricare VPP. |
| Ricetta | Ripristinare XML ricetta precedente; la config macchina resta invariata. |
| Codice HMI | Tornare al tag/commit di fase precedente e ripristinare il relativo installer. |

Non fare rollback parziali mescolando EXE nuovo, VPP vecchio e template nuovi
senza ripetere il preflight.

## Ordine Di Lavoro Raccomandato

Il prossimo passo concreto non e' modificare ancora il trigger controller. E':

1. preparare il VPP di commissioning `Top/Left/Right/Bottom`;
2. congelare i terminali `Results` e `ImageStitching`;
3. scegliere il lato fisico backlight;
4. definire i quattro mapping trigger reali;
5. rieseguire l'inventario read-only sul nuovo VPP;
6. solo dopo completare validator Right/Left, preflight e identita' difetti;
7. collaudare una camera alla volta prima del ciclo quattro camere.

Questo ordine evita di costruire codice su nomi ToolBlock, output o cablaggi
ancora variabili e mantiene utilizzabile la baseline Top/Side durante tutta
l'integrazione.
