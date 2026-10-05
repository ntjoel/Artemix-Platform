# Offset Camera E MultiShot Per Ricetta

## Scopo

Questa implementazione permette di adattare le quote camera e la geometria MultiShot a prodotti di
dimensioni diverse senza duplicare o modificare la configurazione fisica della macchina.

Il contratto resta:

- configurazione macchina = geometria nominale, I/O, encoder e limiti di sicurezza;
- ricetta = sole correzioni prodotto-specifiche;
- runtime = configurazione effettiva calcolata in memoria.

## Separazione Dei Dati

| Livello | Parametri | Persistenza |
|---|---|---|
| Macchina | base position, trim commissioning, board, channel, polarity, encoder, counts/mm | `machine_runtime_config.xml` |
| Macchina MultiShot | output trigger, durata impulso, intervallo minimo, direzione, timeout, max shots, calibrazione mm/px | `machine_runtime_config.xml` |
| Ricetta | offset posizione per camera, modo MultiShot, delta scatti, delta step, delta primo scatto | XML ricetta |
| Runtime | quote e profili effettivi ottenuti dalla fusione dei due livelli | memoria, mai salvato come config macchina |

Dalla release `3.1.1.8` la ricetta puo' anche ereditare, abilitare o disabilitare ogni trigger
camera senza contenere mapping fisici. La procedura aggiornata e il collaudo sono descritti in
`machine-output-recipe-trigger-and-tracking-commissioning-2026-09-01.md`.

La configurazione macchina caricata non viene modificata dal cambio ricetta. Il servizio
`RecipeMachineRuntimeResolver` crea una copia runtime delle sole strutture che devono essere
corrette.

## Formule Runtime

Quota camera:

```text
effectiveCameraMm = machineBaseMm + machineTrimMm + recipeCameraOffsetMm
```

Numero scatti:

```text
effectiveShots = clamp(machineShots + recipeShotCountOffset, 1, machineMaxShots)
```

Passo MultiShot:

```text
effectiveStepMm = machineStepMm + recipeStepOffsetMm
effectiveStepPulses = round(effectiveStepMm * mainEncoderCountsPerMm)
```

Primo scatto MultiShot:

```text
effectiveFirstShotMm = effectiveCameraMm + recipeFirstShotOffsetMm
effectiveFirstShotPulses = round(effectiveFirstShotMm * mainEncoderCountsPerMm)
```

`recipeCameraOffsetMm` sposta sia il punto camera single-shot sia l'origine del profilo MultiShot
associato. `recipeFirstShotOffsetMm` trasla l'origine dell'intera sequenza MultiShot senza cambiare
la distanza tra gli scatti.

## Anteprima Globale Ed Effettiva In HMI

Dalla release `3.0.7.6`, sotto ogni campo della sezione `Trigger configuration` viene mostrato un
riepilogo calcolato con lo stesso resolver del ciclo produttivo:

```text
Globale: 76 mm | Effettiva: 86 mm
```

Il significato e preciso:

- `Globale` = `BaseOffsetMm + TrimOffsetMm` letto dal file macchina attivo;
- `Effettiva` = quota globale dopo gli offset della ricetta selezionata;
- i campi Left/Side e Right/Rear mostrano il punto fisico compatibile realmente risolto;
- l'anteprima viene aggiornata al cambio ricetta, al rientro nella pagina e quando il campo perde il
  focus.

Per ogni profilo MultiShot vengono mostrati anche:

| Campo ricetta | Valori mostrati |
|---|---|
| modo | stato Enabled globale ed effettivo |
| delta scatti | scatti globali, scatti effettivi e `MaxShotCount` |
| delta step | step globale ed effettivo in mm |
| delta primo scatto | origine globale ed effettiva in mm, convertita con counts/mm encoder |

Se la calibrazione encoder non consente una conversione affidabile, l'origine viene indicata come
non disponibile. Se il resolver rileva un valore non valido, la pagina mostra un avviso e presenta
il fallback sicuro che il runtime userebbe; il salvataggio resta comunque bloccato dalla validazione
gia esistente.

## Nuovo Nodo XML Ricetta

Le ricette nuove o risalvate possono contenere:

```xml
<machineRuntimeAdjustments>
  <CameraPositions>
    <TopOffsetMm>0</TopOffsetMm>
    <SideOffsetMm>0</SideOffsetMm>
    <LeftOffsetMm>-8.5</LeftOffsetMm>
    <FrontOffsetMm>0</FrontOffsetMm>
    <RightOffsetMm>0</RightOffsetMm>
    <RearOffsetMm>0</RearOffsetMm>
    <BottomOffsetMm>0</BottomOffsetMm>
  </CameraPositions>
  <MultiShot>
    <SideLeft>
      <EnabledMode>Enabled</EnabledMode>
      <ShotCountOffset>2</ShotCountOffset>
      <StepOffsetMm>1.5</StepOffsetMm>
      <FirstShotOffsetMm>-3</FirstShotOffsetMm>
    </SideLeft>
    <RightRear>
      <EnabledMode>Machine</EnabledMode>
      <ShotCountOffset>0</ShotCountOffset>
      <StepOffsetMm>0</StepOffsetMm>
      <FirstShotOffsetMm>0</FirstShotOffsetMm>
    </RightRear>
    <Bottom>
      <EnabledMode>Disabled</EnabledMode>
      <ShotCountOffset>0</ShotCountOffset>
      <StepOffsetMm>0</StepOffsetMm>
      <FirstShotOffsetMm>0</FirstShotOffsetMm>
    </Bottom>
  </MultiShot>
</machineRuntimeAdjustments>
```

Ricette storiche senza il nodo continuano a usare la configurazione macchina senza alcuna
correzione. Tutti i valori mancanti hanno default zero oppure `Machine`.

## Modi MultiShot Ricetta

| Valore | Comportamento |
|---|---|
| `Machine` | eredita lo stato Enabled globale |
| `Enabled` | abilita il profilo per questa ricetta, mantenendo I/O e limiti macchina |
| `Disabled` | forza single-shot/nessun MultiShot per questa ricetta |

L'abilitazione ricetta non puo' superare i limiti macchina. Una configurazione non valida viene
bloccata al salvataggio; il runtime applica comunque un fallback sicuro e registra l'evento.

## Profili Supportati

| Profilo ricetta | Ruoli compatibili | Priorita quota macchina |
|---|---|---|
| `SideLeft` | `Left`, `Side`, `SideLeft` | `CAMERA_TRIGGER_LEFT`, poi `CAMERA_TRIGGER_SIDE` |
| `RightRear` | `Right`, `Rear` | `CAMERA_TRIGGER_RIGHT`, poi `CAMERA_TRIGGER_REAR` |
| `Bottom` | `Bottom` | `CAMERA_TRIGGER_BOTTOM` |

Anche gli offset posizione usano la compatibilita fisica: se una ricetta `Left` non trova un punto
LEFT, usa il punto globale SIDE; `Right` usa REAR nello stesso modo. Se due campi ricetta tentano
di correggere lo stesso punto fisico, la validazione blocca il salvataggio invece di sommare i
delta in modo ambiguo.

## Flusso Di Applicazione

1. La HMI carica l'XML ricetta.
2. Applica i trigger delay ricetta gia' esistenti.
3. `RecipeMachineRuntimeResolver` unisce macchina e correzioni ricetta.
4. `MachineController` viene inizializzato con le quote effettive.
5. `MultiShotTriggerController` riceve i profili effettivi.
6. La HMI aggiorna `expectedFrames`, `stepMm` e `mmPerPixel` nel ToolBlock VisionPro.
7. I prodotti in tracking precedenti vengono eliminati per evitare di mescolare due geometrie.

Lo stesso flusso viene eseguito per cambio ricetta manuale, autoswitch e cambio OPC UA, perche'
tutti convergono su `MainWindow.InitializeRecipeAsync`.

## Modalita TimedFromPhotocell

Gli offset posizione in millimetri governano il flusso encoder/tracked-position. In
`TimedFromPhotocell` le camere continuano a usare i trigger delay temporali della ricetta; la HMI
non converte automaticamente millimetri in millisecondi perche' la conversione dipenderebbe dalla
velocita istantanea del nastro.

Il MultiShot encoder-driven richiede l'encoder reale e non viene trasformato in una sequenza a
tempo dalla ricetta.

## Validazioni

- offset posizione e primo scatto: intervallo tecnico `-5000..+5000 mm`;
- quota effettiva camera: non negativa;
- scatti effettivi: `1..MaxShotCount` macchina;
- step effettivo: maggiore di zero quando gli scatti sono piu di uno;
- encoder principale: calibrazione counts/mm valida per convertire i delta in impulsi;
- punto intervento: deve esistere quando viene impostato un offset relativo a quel ruolo.

## Eventi E Diagnostica

| Evento | Significato |
|---|---|
| `RECIPE_MACHINE_RUNTIME_APPLIED` | correzioni applicate senza errori |
| `RECIPE_MACHINE_RUNTIME_APPLIED_WITH_FALLBACK` | almeno un valore non valido e' stato sostituito con un valore sicuro |
| `RECIPE_MACHINE_ADJUSTMENT_INVALID` | salvataggio ricetta bloccato dalla validazione |
| `RECIPE_MACHINE_RUNTIME_RESOLVE_FALLBACK` | risoluzione richiesta fuori dal ViewModel con fallback sicuro |

## Collaudo Minimo

1. Salvare un backup della configurazione macchina.
2. Annotare base e trim del punto camera.
3. Caricare una ricetta storica: verificare comportamento invariato.
4. Impostare `+10 mm`: verificare che il trigger si sposti di `+10 mm` encoder.
5. Impostare `-10 mm`: verificare lo spostamento opposto.
6. Verificare che tornando alla ricetta storica la quota torni globale.
7. Sul profilo MultiShot impostare `ShotCountOffset=+1` e controllare un impulso aggiuntivo.
8. Impostare `StepOffsetMm` e verificare target encoder e `ImageStitching.stepMm`.
9. Impostare `FirstShotOffsetMm`: tutti i target devono traslare dello stesso delta, mentre la
   distanza tra target deve restare invariata.
10. Cambiare ricetta tramite OPC UA e ripetere la lettura dei target effettivi.
11. Riavviare la HMI e verificare persistenza nella sola ricetta.
12. Controllare che `machine_runtime_config.xml` sia rimasto invariato.
