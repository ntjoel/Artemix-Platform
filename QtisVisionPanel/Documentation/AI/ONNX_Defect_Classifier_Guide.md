# ONNX Defect Classifier Guide

## Scopo

Il classificatore ONNX della HMI lavora in shadow-mode: confronta la predizione AI con il risultato VisionPro gia' calcolato, ma non cambia scarto, contatori, allarmi o esito prodotto.

Il modello `.onnx` viene creato dai campioni raccolti dalla funzione `TrainingDataCollectionEnabled`. Da release `3.0.6.2` il training puo' essere lanciato direttamente dal pannello (pulsante `Addestra modello`, vedi sezione dedicata): il calcolo resta comunque in un processo Python esterno e il modello prodotto NON si attiva mai da solo. Da release `3.0.6.3` il pannello permette anche di configurare/testare Python e scrive il modello in modo atomico; da `3.0.6.4` il training espone heartbeat e progressi piu' dettagliati. Da `3.0.7.1` la validation e' cronologica e lo stato `provisional` impedisce di interpretare pochi campioni recenti come prova di affidabilita'. Da `3.0.7.2` l'inferenza ha un budget CPU/memoria esplicito e il percorso disabilitato e' un no-op. In alternativa resta valido il training manuale offline descritto piu' sotto.

## Contratto Del Modello

Il modello deve rispettare questo contratto:

- input immagine in formato tensor `NCHW`
- batch size `1`
- canali `3` per RGB oppure `1` se in HMI e' abilitata la scala di grigi
- dimensioni uguali ai campi HMI `DefectClassifierInputWidth` e `DefectClassifierInputHeight`
  dopo resize
- output: vettore di score/logit
- classe `0 = OK`
- qualsiasi classe `> 0 = NOK/difetto`

Importante: `DefectClassifierInputWidth` e `DefectClassifierInputHeight` NON sono la
risoluzione nativa della camera. Sono la dimensione a cui la HMI ridimensiona l'immagine prima
dell'inferenza. Anche se la camera TOP e' 1456x1088 o la SIDE e' 1800x1500, qui va inserita la
dimensione usata durante il training del modello, ad esempio 224x224, 320x320, 512x512 o 640x640.
Da release 3.0.6.1 la HMI limita questi valori a 16..1024 px per lato.

Il preprocess HMI applica:

```text
pixel_normalizzato = (pixel - DefectClassifierNormalizeMean) / DefectClassifierNormalizeStd
```

Configurazione consigliata iniziale:

- `InputWidth=224`
- `InputHeight=224`
- `Grayscale=false`
- `NormalizeMean=0`
- `NormalizeStd=255`

Con questi valori il pixel 0..255 diventa 0..1, coerente con un training Python basato su `transforms.ToTensor()`.

## Dataset

1. Abilitare da PC Diagnostics:
   - `Raccolta dati training`
   - salvataggio immagini pezzo nella configurazione esistente
2. Far passare prodotti OK e NOK reali, cercando un dataset bilanciato.
3. Ogni cartella pezzo deve contenere:
   - immagini salvate dal runtime
   - `label.json`

La HMI supporta due profili indipendenti:

- TOP: preferita `*_T_A.bmp`, fallback `*_T_Z.jpg`, etichetta `labels.top`;
- SIDE / LEFT: preferita `*_F_A.bmp` o `*_L_A.bmp`, fallback `*_F_Z.jpg` / `*_L_Z.jpg`,
  etichetta `labels.side`. Un job Side o Left mostrato nella vista Left viene salvato con la
  sigla `_L_`; i dataset storici della vista Side usano `_F_`. Il supporto `_L_` è presente da
  2026-09-28: prima i campioni `_L_` venivano scartati come "senza immagine".

Per i sidecar legacy senza un blocco `labels` non vuoto, solo TOP puo' usare la vecchia label
globale. Se il blocco `labels` esiste ma la camera richiesta manca, il campione viene scartato:
non si deve assegnare alla Side il difetto rilevato da un'altra camera.

Da 2026-09-28 anche le altre camere hanno un profilo proprio (card espandibile sotto SIDE in
`PC Diagnostics -> Controlli AI`), con training e statistiche shadow separati:

- REAR / RIGHT: preferita `*_RI_A.bmp` o `*_R_A.bmp`, fallback `*_Z.jpg`, etichetta
  `labels.rear`, modello `defect_classifier_rear_vN.onnx`;
- FRONT: `*_FR_A.bmp` / `*_FR_Z.jpg`, etichetta `labels.front`, modello `defect_classifier_front_vN.onnx`;
- BOTTOM: `*_B_A.bmp` / `*_B_Z.jpg`, etichetta `labels.bottom`, modello `defect_classifier_bottom_vN.onnx`.

La configurazione delle camere aggiuntive è in `RuntimeBindings/AdditionalDefectClassifierProfiles`
di `machine_runtime_config.xml` (un `DefectClassifierCameraProfile` per camera). Il training
offline usa `--camera rear|front|bottom`. Non usare la risoluzione nativa di una camera nei campi
di un'altra: ogni profilo ha modello, resize e preprocessing propri.

## Training Integrato Dal Pannello (da 3.0.6.2, validation hardening 3.0.7.1)

Pulsante `Addestra modello` in `PC Diagnostics -> Controlli AI -> Classificatore ONNX`
(riservato a Installer/Administrator, macchina FERMA).

Cosa fa:

1. valida il dataset (`label.json` sotto la cartella immagini): minimo 10 campioni per classe;
2. lancia `Scripts\AI\train_defect_classifier.py` come processo esterno, passando il preprocessing
   corrente della HMI (dimensioni, grayscale, mean/std) cosi' il modello esce coerente col contratto;
3. la CNN e' leggera e addestrata DA ZERO (nessun download di pesi: funziona anche senza internet);
4. separa train/validation con holdout cronologico stratificato: per ogni classe i campioni piu'
   recenti vengono usati solo per validation, riducendo il rischio che immagini consecutive quasi
   identiche finiscano su entrambi i lati;
5. esporta `defect_classifier_vN.onnx` (v1, poi v2, ...) nella cartella modelli configurata
   (`C:\QtisVision\AI\Models` di default) + un sidecar `.metrics.json` con accuracy di
   validazione, recall NOK, matrice di confusione, strategia e numerosita' holdout;
6. a fine training verifica che il file sia caricabile da ONNX Runtime e PRECOMPILA percorso/versione/note
   nel pannello: il modello si attiva solo quando l'operatore preme `Salva`.
7. durante il training aggiorna lo stato live e registra `AI_TRAINING_PROGRESS` nei log; gli eventi
   start/completed/failed/cancel/timeout sono tracciati nell'Events Monitor.
   Da `3.0.6.4` lo stato include anche import dipendenze, split dataset, epoca/batch, validazione
   e heartbeat `stage=running` ogni 15 secondi se il processo Python e' vivo ma non ha ancora
   prodotto nuovo output. Da `3.0.7.3` il valore loss per progressi/media viene estratto con
   `detach().item()`: non deve piu' comparire il warning `requires_grad=True`; anche la sola
   deprecazione informativa dell'exporter legacy PyTorch e' filtrata.

Prerequisiti sulla macchina (una tantum):

```powershell
python -m venv C:\QtisVision\AI\.venv
C:\QtisVision\AI\.venv\Scripts\python.exe -m pip install torch pillow numpy onnx onnxscript
```

Campi di configurazione (machine_runtime_config.xml):

- `DefectClassifierTrainingPythonPath` (default `python`): eseguibile Python, accetta percorso completo
- `DefectClassifierTrainingOutputDir` (default `C:\QtisVision\AI\Models`)
- `DefectClassifierTrainingEpochs` (default `20`)

Da `3.0.6.3` questi tre campi sono editabili direttamente nella sezione ONNX del pannello. Usare
`Test Python` prima del primo training: il test esegue lo script con `--self-test` e verifica che
Python, `torch`, `pillow`, `numpy` e `onnx` siano disponibili.

Note operative:

- il training e' CPU-intensivo: il pulsante rifiuta di partire con la produzione attiva;
- `Annulla` termina il processo Python senza toccare la HMI;
- `validation=qualified` richiede almeno 10 OK e 10 NOK nel solo holdout recente;
- `validation=provisional` non annulla l'export, ma genera popup/audit warning: raccogliere altri
  campioni recenti, soprattutto NOK, prima di considerare affidabili accuracy e recall;
- log eventi: `AI_TRAINING_START/COMPLETED/COMPLETED_PROVISIONAL/FAILED/CANCELLED/TIMEOUT` e
  `AI_MODEL_TRAINED` nell'Events Monitor;
- `AI_TRAINING_STDERR` non deve comparire per la normale conversione della loss o per il messaggio
  sul futuro exporter PyTorch; eventuali altre righe stderr restano visibili e vanno analizzate;
- valutare SEMPRE le metriche (soprattutto `NOK recall`) prima di abilitare, e collaudare in shadow-mode.

### Macchina Senza Python

Python NON serve per eseguire un modello ONNX gia' generato: l'inferenza usa `Microsoft.ML.OnnxRuntime`
dentro la HMI. Python serve solo se si vuole premere `Addestra modello` direttamente dal pannello.

Opzioni operative:

1. installare una venv locale e impostare `DefectClassifierTrainingPythonPath` al relativo
   `python.exe`;
2. addestrare il modello offline su PC tecnico, copiare `defect_classifier_vN.onnx` nella cartella
   modelli e configurarlo nel pannello;
3. in una fase futura, pacchettizzare un trainer `.exe` con Python/dependencies incluse, lasciando
   invariato il contratto HMI.

## Training Offline

Esempio ambiente Python:

```powershell
python -m venv .venv
.\.venv\Scripts\activate
pip install torch torchvision pillow onnx onnxruntime scikit-learn
```

Schema minimo di training:

1. leggere ricorsivamente le cartelle pezzo
2. aprire `label.json`
3. usare `label == "OK"` come classe `0`
4. usare `label != "OK"` come classe `1`
5. caricare l'immagine `*_T_A.bmp` o, se manca, `*_T_Z.jpg`
6. addestrare una CNN leggera, ad esempio ResNet18/MobileNet
7. esportare ONNX con input name stabile, ad esempio `input`

Esempio export PyTorch:

```python
import torch

model.eval()
dummy = torch.randn(1, 3, 224, 224)
torch.onnx.export(
    model,
    dummy,
    "defect_classifier_v1.onnx",
    input_names=["input"],
    output_names=["logits"],
    opset_version=13,
    dynamic_axes=None,
)
```

Validare sempre con ONNX Runtime prima di portare il file sulla macchina:

```python
import onnxruntime as ort

session = ort.InferenceSession("defect_classifier_v1.onnx")
print(session.get_inputs()[0].name, session.get_inputs()[0].shape)
print(session.get_outputs()[0].name, session.get_outputs()[0].shape)
```

## Installazione In HMI

1. Copiare il modello in un percorso stabile della macchina, ad esempio:

```text
C:\QtisVision\AI\Models\defect_classifier_v1.onnx
```

2. Aprire `PC Diagnostics -> Controlli AI`.
3. Impostare:
   - `Classificatore ONNX -> Abilitato`
   - `Percorso modello`: percorso completo del file `.onnx`
   - `Versione modello`: es. `v1.0`
   - `Note modello`: dataset/ricetta/macchina usati per training
   - dimensioni di resize input e normalizzazione coerenti col training
   - `Confidenza minima NOK`: `0` per comportamento invariato; sopra `0` i NOK sotto soglia
     diventano `incerti` nello shadow-mode
4. Premere `Salva`.

Da release `3.0.4.4`, il pannello valida il file `.onnx` e ricarica subito il classificatore dopo il salvataggio. Non serve riavviare per cambiare modello/preprocess.

## Isolamento Prestazioni E Memoria (da 3.0.7.2)

Il classificatore non e' un requisito della macchina. Il contratto runtime e':

- profilo TOP/SIDE disabilitato: nessun caricamento modello, task, accesso immagini, bitmap o tensor;
- profilo abilitato senza modello valido: un warning durante il load/reload, poi nessuna analisi per
  pezzo e nessuna dipendenza del ciclo macchina;
- profilo operativo: l'analisi parte solo dopo che VisionPro ha gia' prodotto l'esito e resta
  shadow/advisory;
- massimo un campione in volo per camera e una sola inferenza globale tra TOP e SIDE;
- la camera in attesa non alloca ancora bitmap/tensor: la coda non cresce con i prodotti;
- ONNX Runtime usa un thread intra-op e uno inter-op, esecuzione sequenziale e worker a priorita'
  bassa; arena CPU e memory pattern sono disabilitati per ridurre memoria nativa trattenuta;
- il preprocessing usa accesso diretto `LockBits`; non usa `GetPixel` sul percorso pixel-per-pixel;
- gli accordi ordinari sono loggati a livello DEBUG. A livello INFO/WARN restano inizializzazione,
  cambi contesto, disaccordi, timeout ed errori.

La memoria virtuale di Windows non va usata per accelerare l'inferenza. Se il working set viene
paginato su disco, la latenza diventa piu' alta e meno prevedibile. La protezione corretta per la
macchina e' mantenere bounded la concorrenza e ridurre le allocazioni, lasciando l'AI disabilitata
sulle installazioni che non hanno un modello validato.

Le statistiche shadow mostrano:

- `AI avg/max`: tempo effettivo di preprocessing + inferenza;
- `attesa risorsa avg`: tempo trascorso aspettando che l'altra camera liberi il gate globale;
- `saltati busy`: campioni non accodati perche' quella camera ha gia' un confronto in corso;
- `timeout immagine`: immagine salvata non diventata leggibile entro il limite.

Nei log WARN di disaccordo sono disponibili anche `inference_ms` e `resource_wait_ms`. Gli stessi
campi sono presenti nel messaggio DEBUG `VISION_ML_SHADOW` quando il livello DEBUG e' abilitato.

## Collaudo

1. Lasciare il classificatore in shadow-mode.
2. Verificare nei log:
   - `VISION_ML_INIT|loaded`
   - `VISION_ML_SHADOW` solo se il livello DEBUG e' abilitato
   - eventuale `VISION_ML_SHADOW_DISAGREE`
   - assenza o numero motivato di `VISION_ML_SHADOW_IMAGE_TIMEOUT`
3. Confrontare per almeno un turno produzione:
   - accordo AI vs VisionPro
   - falsi OK
   - falsi NOK
   - tempo inferenza
4. Non usare il modello per comandare scarto finche' non e' stato validato con dati macchina reali e firmato come release modello.

Da `3.0.7.1`, la card statistiche mostra il contesto attivo (`set` stabile, file/versione, soglia,
resize e ora inizio). Un cambio di file, versione, preprocess o soglia azzera automaticamente la
finestra, cosi' le statistiche di due modelli non vengono mai sommate. Lo stesso identificatore
`set` e i campi `model_version` / `min_conf` sono scritti negli eventi `VISION_ML_SHADOW`, quindi
anche l'analisi storica dei log puo' separare le tarature. Un semplice reload della stessa
configurazione conserva invece i campioni gia' raccolti.
