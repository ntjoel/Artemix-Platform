# VisionPro ViDi EL And ONNX Runtime Isolation

## Scope

Questa nota descrive i fix `3.1.0.1` e `3.1.0.2` per i VPP contenenti `CogClassifyTool` che si
aprivano in VisionPro QuickBuild ma fallivano in QtisVisionPanel con:

```text
TargetInvocationException
  -> Cognex.Vision.SerializationException: Error loading object
```

## Root cause verified

VisionPro ViDi EL e il classificatore AI shadow-mode usano due distribuzioni native
diverse di `onnxruntime.dll`:

- Cognex ViDi EL: fornita con VisionPro 9.25;
- HMI shadow-mode: Microsoft ONNX Runtime 1.19.2.

Quando entrambe erano nello stesso processo, la DLL Microsoft nella directory dell'EXE
veniva risolta per prima dal loader Windows. ViDi EL riceveva quindi una ABI diversa da
quella prevista e la deserializzazione RBBT falliva. La prova comparativa ha confermato
che lo stesso VPP viene caricato da una build priva delle DLL Microsoft in root.

## Implemented architecture

```text
QtisVisionPanel.exe
  VisionPro 9.25 + ViDi EL + Cognex onnxruntime.dll
  |
  +-- stdin/stdout protocol --> AiRuntime\QtisVisionPanel.OnnxWorker.exe
                                  Microsoft ONNX Runtime 1.19.2
```

Il worker:

- viene avviato solo se un profilo ONNX Top/Side e' abilitato e il modello esiste;
- usa inferenza sequenziale, un thread intra-op e un thread inter-op;
- gira a priorita' `BelowNormal` quando il sistema operativo lo consente;
- mantiene al massimo quattro sessioni modello;
- viene arrestato quando non restano modelli registrati;
- viene ricreato dopo crash, timeout o working set superiore a 512 MB;
- non invia alcun comando a VisionPro, I/O, encoder o scarto.

## Required installation layout

```text
C:\QtisVision\bin\QtisVisionPanel.exe
C:\QtisVision\bin\VisionProDependencies -> Cognex VisionPro bin
C:\QtisVision\bin\AiRuntime\QtisVisionPanel.OnnxWorker.exe
C:\QtisVision\bin\AiRuntime\Microsoft.ML.OnnxRuntime.dll
C:\QtisVision\bin\AiRuntime\onnxruntime.dll
```

Questi file non devono esistere direttamente in `C:\QtisVision\bin`:

```text
Microsoft.ML.OnnxRuntime.dll
onnxruntime.dll
onnxruntime_providers_shared.dll
```

Il target MSBuild li elimina dalla root e il builder installer `r7` blocca il media se
li trova. La `onnxruntime.dll` Cognex resta disponibile attraverso
`VisionProDependencies` e non deve essere rimossa dal runtime VisionPro.

Anche questo file non deve esistere direttamente in `C:\QtisVision\bin`:

```text
Cognex.Vision.Startup.Net.dll
```

La `3.1.0.2` ha confrontato la build corrente con la copia `3.0.2.4` che caricava
lo stesso VPP. Con la DLL Startup accanto all'EXE il processo si fermava prima di
caricare `Cognex.Vision.ViDiELClassify.dll` e la `onnxruntime.dll` Cognex. Eliminando
solo quella copia privata, il VPP veniva deserializzato con i job `Top,Side` e tutti
i moduli nativi provenivano dalla junction `VisionProDependencies`.

MSBuild elimina ora anche eventuali copie residue della DLL Startup; il builder
installer `r7` rifiuta sia la Release sorgente sia lo ZIP staged se la trova in root.

## Logs

```text
VISION_ML_WORKER_STARTED
VISION_ML_INIT|camera=...|loaded_out_of_process
VISION_ML_WORKER_TIMEOUT
VISION_ML_WORKER_MEMORY_LIMIT
VISION_ML_WORKER_START_FAILED
VISION_ML_WORKER_STDERR
```

Con classificatore disabilitato e' previsto solo:

```text
VISION_ML_INIT|camera=...|enabled=false
```

e `QtisVisionPanel.OnnxWorker.exe` non deve comparire nei processi.

## Commissioning test

1. Chiudere completamente la vecchia HMI prima di sostituire la `bin`.
2. Verificare la struttura cartelle indicata sopra.
   `Cognex.Vision.Startup.Net.dll` deve comparire sotto `VisionProDependencies`,
   non direttamente accanto a `QtisVisionPanel.exe`.
3. Lasciare i classificatori ONNX disabilitati.
4. Avviare la HMI e caricare il VPP con `CogClassifyTool`.
5. Verificare assenza di `SerializationException` e presenza di tutti i job attesi.
6. Eseguire `Run Once`, poi `RunContinuous`.
7. Verificare in Gestione attivita' che il worker ONNX non sia avviato.
8. Abilitare un solo profilo ONNX valido e applicare la configurazione.
9. Verificare `VISION_ML_WORKER_STARTED` e `loaded_out_of_process`.
10. Far transitare almeno 20 prodotti e confermare che esito, contatori, scarto e tempo
    ciclo restino governati unicamente da VisionPro.
11. Disabilitare il profilo e verificare l'arresto del worker quando nessun altro profilo
    e' attivo.

## Compatibility

- `Config.xml`: invariato.
- Machine runtime XML: schema e valori invariati.
- Ricette: invariate.
- Database: invariato.
- VisionPro VPP: invariato, nessuna riscrittura del file.
- I/O, encoder, trigger e RunContinuous: invariati.
- Classificatore ONNX: resta shadow-mode advisory; un suo errore non blocca la macchina.
