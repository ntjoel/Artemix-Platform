# VisionPro ViDi EL Classify In Embedded Job Editor

## Scope

Questa nota descrive il supporto al tool `CogClassifyTool` di VisionPro ViDi EL
quando il job viene modificato dal `Job Tool Editor` integrato in QtisVisionPanel.

Il problema osservato era specifico dell'HMI:

- il template Classify si aggiungeva correttamente in VisionPro QuickBuild;
- nell'editor incorporato compariva `Failed to Load Tool Template`;
- il debugger mostrava `CogPathException`, `Cognex.Vision.SerializationException`
  e `TargetInvocationException`.

Non erano coinvolti il trigger camera, la ricetta, il mapping I/O o il ciclo di
ispezione.

## Root cause

Il sample Cognex installato `ViDiEL/ClassifyApp` richiede che
`Startup.Initialize(Startup.ProductKey.VProX)` sia eseguito il prima possibile,
prima della creazione di qualsiasi oggetto VisionPro, ToolBlock o template ViDi
EL.

La baseline precedente inizializzava Cognex in piu punti e troppo tardi:

- alcuni ViewModel potevano caricare tipi VisionPro prima dello splash;
- lo splash usava il valore enum vuoto di `ProductKey` e disabilitava
  l'inizializzazione security;
- il caricatore VPP inizializzava poi `VProX`, quando alcune assembly Cognex
  erano gia state caricate;
- la palette embedded non preparava esplicitamente le estensioni editor ViDi EL.

Questa sequenza rendeva possibile la creazione dei tool classici, ma non
garantiva la deserializzazione RBBT richiesta da `CogClassifyTool` e dai modelli
Classify persistiti nel VPP.

## Implemented solution

La release `3.1.0.0` introduce `VisionProRuntimeBootstrap` come unico punto di
inizializzazione:

1. `App.Application_Startup` inizializza `VProX` prima dello splash e del preload
   dei ViewModel.
2. L'inizializzazione security viene attesa per un massimo di 30 secondi.
3. `SplashWindow` e il caricatore VPP riutilizzano lo stesso bootstrap
   idempotente.
4. Prima di costruire il `Job Tool Editor` vengono caricate le estensioni:
   - `Cognex.Vision.ViDiELClassify.Net`;
   - `Cognex.VisionPro.ViDiEL.Controls`;
   - `Cognex.VisionProUI.ViDiELClassify.Controls`.
5. Gli errori VPP riportano ora tutta la catena delle inner exception, HResult e
   `LoaderExceptions`, invece del solo messaggio esterno.
6. La vecchia esecuzione del processo esterno `cogtool --print` e stata rimossa:
   non inizializzava il processo HMI e poteva terminare in modo anomalo.

## Runtime logs

Avvio corretto:

```text
VISIONPRO_RUNTIME_INITIALIZED|product=VProX; security=True; runtime=...
```

Apertura corretta del Job Tool Editor:

```text
VISIONPRO_JOB_EDITOR_EXTENSIONS_READY|assemblies=...
```

Diagnostica in caso di problema:

```text
VISIONPRO_RUNTIME_INITIALIZATION_FAILED|...
VISIONPRO_JOB_EDITOR_EXTENSIONS_FAILED|...
```

Gli errori durante il load VPP includono ora ogni livello della catena, per
esempio assembly mancante, mismatch di versione, licenza o errore RBBT.

## Commissioning test

1. Chiudere completamente QtisVisionPanel e VisionPro QuickBuild.
2. Verificare che VisionPro 9.25 e la patch prevista dalla baseline installer
   siano installati.
3. Verificare la licenza `VProX` richiesta dal tool ViDi EL.
4. Avviare QtisVisionPanel e controllare il log
   `VISIONPRO_RUNTIME_INITIALIZED`.
5. Aprire `System Editors -> Job Tool Editor` e controllare
   `VISIONPRO_JOB_EDITOR_EXTENSIONS_READY`.
6. Selezionare il ToolBlock del job desiderato e aggiungere
   `ViDiEL -> CogClassifyTool`.
7. Collegare `InputImage`, configurare il modello e salvare il VPP.
8. Uscire dall'editor, ricaricare la stessa ricetta e verificare che il VPP sia
   deserializzato senza `Failed to Load Tool Template`.
9. Eseguire prima `Run Once`, poi `RunContinuous`, verificando output Classify e
   assenza di recovery VisionPro.
10. Riavviare l'HMI e ripetere il caricamento del VPP salvato: il test verifica
    anche la deserializzazione del modello persistito.

## Interpretation of debugger exceptions

VisionPro usa internamente path terminali e reflection per popolare la palette.
Singoli `CogPathException` mostrati come first-chance dal debugger possono essere
gestiti internamente e non indicano da soli un guasto.

Il test e fallito solo se si verifica almeno una di queste condizioni:

- compare il popup `Failed to Load Tool Template`;
- il tool non viene aggiunto al ToolBlock;
- il VPP salvato non viene ricaricato;
- compare un log `VISIONPRO_*_FAILED`;
- il job non raggiunge RunContinuous.

## Compatibility and impact

- `Config.xml`: nessuna chiave nuova o modificata.
- Machine runtime XML: nessuna modifica.
- Recipe XML: nessuna modifica.
- Database: nessuna migrazione.
- I/O, encoder e trigger: invariati.
- Ciclo produzione: invariato; il bootstrap viene eseguito una sola volta
  all'avvio del processo.

Il collaudo finale della palette embedded richiede la macchina con la licenza e
il modello Classify effettivamente usati in produzione.

## Follow-up 3.1.0.1: native ONNX isolation

Il bootstrap anticipato della `3.1.0.0` e' necessario, ma il collaudo successivo ha
individuato una seconda causa indipendente. Il classificatore ONNX shadow-mode HMI
copiava `Microsoft.ML.OnnxRuntime.dll` e la relativa `onnxruntime.dll` nella stessa
directory di `QtisVisionPanel.exe`. Windows dava precedenza a questa DLL nativa rispetto
alla variante distribuita da Cognex. Di conseguenza ViDi EL falliva durante la
deserializzazione anche con `VProX` gia' inizializzato.

La `3.1.0.1` esegue l'ONNX HMI nel processo separato
`AiRuntime\QtisVisionPanel.OnnxWorker.exe`. La directory principale resta riservata alle
dipendenze VisionPro; i dettagli e il collaudo sono in
`visionpro-vidiel-onnx-runtime-isolation-2026-08-21.md`.

## Verification performed on the development station

Con VisionPro 9.25 locale e bootstrap `VProX` anticipato sono stati eseguiti
senza acquisizione e senza RunContinuous i seguenti test:

- deserializzazione del template ufficiale
  `Templates/Tools/ViDiEL/CogClassifyTool.vtt`: riuscita;
- deserializzazione di `Lucart_Test_sample.vpp` con 2 job: riuscita;
- deserializzazione di `Index_Fiera_sample.vpp` con 2 job: riuscita;
- shutdown esplicito di ogni `CogJobManager` dopo il test: riuscito.

Questi test coprono il costruttore del tool, il layer RBBT e il modello
persistito. Non sostituiscono il test visuale di aggiunta dalla palette embedded.
