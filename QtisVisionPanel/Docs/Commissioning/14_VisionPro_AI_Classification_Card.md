# VisionPro AI Classification Card

## Scopo

Questa procedura verifica la card HMI comune che mostra classe e score prodotti
da un classificatore interno al job VisionPro. L'integrazione e' disponibile per
tutti i ruoli camera. Quando `AIClassification` e' abilitata sulla vista, classe,
score, classi accettate e soglia partecipano alla validazione e all'eventuale
scarto configurato nella ricetta.

## Contratto ToolBlock

Esportare nel ToolBlock radice del job almeno un output classe. I nomi vengono
risolti senza distinzione tra maiuscole e minuscole.

| Dato | Nome consigliato | Alias supportati | Tipo consigliato |
|---|---|---|---|
| Classe | `EL_Classify` | `Classify`, `Classification`, `Class` | `System.String` |
| Score | `EL_Score` | `Score`, `ClassificationScore`, `Confidence` | `System.Double` |

Lo score e' opzionale soltanto quando la soglia e' `0`: se una soglia positiva
e' configurata e lo score manca, il risultato e' `Unclassify`. La classe e'
obbligatoria; se manca o e' vuota, il controllo non puo' produrre un GOOD.

## Stati visuali

| Stato | Etichette riconosciute | Colore |
|---|---|---|
| Conforme | Il validator restituisce `Classification.Passed=true`; in assenza della misura vengono riconosciuti `OK`, `GOOD`, `PASS`, `PASSED`, `COMPLIANT` | Verde |
| Non conforme | Il validator restituisce `Classification.Passed=false`; in assenza della misura vengono riconosciuti `NOK`, `NG`, `KO`, `FAIL`, `FAILED`, `BAD`, `REJECT`, `NOREAD` | Rosso |
| Non riconosciuto | La classe e' accettata ma lo score manca o e' inferiore alla soglia della vista; la classe archiviata diventa `Unclassify` e lo score resta quello misurato | Ambra |
| Informativo | Classe disponibile ma nessuna misura `Classification` e nessuna etichetta GOOD/NOK nota | Blu |

La policy e' per vista. La card viene mostrata solo se `AIClassification` e'
abilitata per quella camera nella matrice della ricetta. Una classe non accettata
o sotto soglia genera NOK; lo scarto fisico dipende dal flag di scarto AI della
ricetta.

## Configurazione HMI

In `Inspection Configuration`, per ogni vista attiva:

1. abilitare `Classificazione AI` soltanto se il ToolBlock pubblica la classe;
2. inserire le classi GOOD esattamente come addestrate, separate da virgola;
3. impostare `Confidenza minima` nella stessa scala di `EL_Score`;
4. usare `0` per mantenere il comportamento senza gate di confidenza;
5. salvare e ricaricare la ricetta prima del collaudo.

Il salvataggio scrive `AcceptedClassificationClasses` e
`MinimumClassificationScore` nel profilo vista della ricetta. Il DB
`cfg_view_classification` resta il default macchina usato dalle ricette legacy.

## Correlazione per ciclo

Dal runtime `3.1.0.7`, il callback `UserResultAvailable` cattura immediatamente
una copia scalare degli output del ToolBlock. La coda conserva quindi:

- record grafico VisionPro del ciclo;
- snapshot classe, score e misure dello stesso ciclo;
- `ResultSequence` progressiva HMI;
- timestamp e ruolo camera.

Il ToolBlock vivo continua a essere usato solo per i fallback immagine e gli
artefatti. Non viene piu' riletto in fase di validazione, quando potrebbe gia'
contenere il prodotto successivo.

## Procedura di test

1. Aprire il VPP in QuickBuild e collegare classe e score agli output radice.
2. Eseguire almeno un campione GOOD e uno NOK e verificare i valori nei terminali.
3. Salvare il VPP e caricare la ricetta nella HMI.
4. Avviare una singola ispezione o RunContinuous.
5. Verificare che la card compaia sotto la vista camera corretta.
6. Verificare classe, score e badge su almeno due classi differenti.
7. Portare temporaneamente la soglia sopra lo score di un campione GOOD e
   verificare badge ambra, classe `Unclassify`, prodotto No Good e score invariato.
8. Disabilitare AI su una sola vista e verificare che la relativa card scompaia
   senza disabilitare l'AI sulle altre camere.
9. Caricare un job senza output classe e verificare che non possa produrre GOOD.
10. Per `Left` e `Right`, verificare anche il routing visuale rispettivamente dai
   risultati `Side` e `Rear` usati dal runtime compatibile.
11. Eseguire almeno dieci risultati consecutivi e verificare nei log che ogni
   riga `Processing inspection group` riporti `topSequence` e le corrispondenti
   `companionSequences`.
12. Su un campione difettoso confrontare immagine, terminale `EL_Classify`,
    `EL_Score`, badge e messaggio `Inspection failed`: devono descrivere lo
    stesso ciclo.
13. Interrogare l'ultima riga di `tblgenerale` e verificare che label e score
    coincidano con la card della stessa vista. Per la mappa completa e la query
    pronta usare `Documentation/MachineHardware/tblgenerale-production-id-and-classification-2026-08-26.md`.

## Prestazioni e confini

`VisionToolBlockOutputSnapshot` copia soltanto terminali scalari gia' calcolati;
`VisionClassificationResultReader` legge la copia dopo la validazione. Non viene
eseguita inferenza, non vengono copiati oggetti immagine e non viene percorso il
grafo dei tool.

Non vengono aggiunti campi a `Config.xml`. Dalla versione `3.1.0.9`,
`tblgenerale` contiene colonne nullable label/score per le cinque viste runtime;
le colonne `NC_<Vista>AiClassification` usano `0=NOK`, `1=GOOD`, `3=errore di
elaborazione` e `4=disabilitata/non eseguita`. Lo score sotto soglia usa `0`,
non `3`, perche' l'inferenza e' stata eseguita correttamente.

## Diagnostica

| Sintomo | Verifica |
|---|---|
| Card assente | Output classe presente nel ToolBlock radice e valore non vuoto. |
| Score `--` | Output score assente, non numerico o non collegato. |
| Badge ambra / `Unclassify` | Confrontare score e soglia della stessa vista; verificare immagini di training, luce e prodotto. |
| Badge blu | Nome classe libero e validator privo della misura `Classification`. |
| Card nella vista laterale errata | Nome ruolo del job (`Side`/`Left`, `Rear`/`Right`) e mappatura runtime. |
| Immagine e card appartengono a cicli diversi | Cercare `VISION_RESULT_SNAPSHOT_FAILED`; verificare le sequenze nel log e allegare il blocco completo al supporto. |
| Classe difetto mostrata GOOD | Verificare che il validator aggiunga la misura `Classification`; dalla `3.1.0.7` il suo campo `Passed` governa il colore. |
| Card presente su vista disabilitata | Verificare la matrice vista x ispezione caricata dalla ricetta e il log `RECIPE_INSPECTION_VIEW_PROFILE_ACTIVE`. |
