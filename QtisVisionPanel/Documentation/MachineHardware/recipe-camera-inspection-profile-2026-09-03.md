# Profilo Camera E Ispezioni Per Ricetta

## Scopo

Dalla release `3.1.5.1`, ogni ricetta puo' dichiarare quali viste camera usa,
quali ispezioni esegue ciascuna vista e quali classi VisionPro Classify sono
conformi. Il profilo evita che una Rear venga validata per un output presente
soltanto sulla Side o che una ricetta a due camere attenda risultati da una
terza camera non utilizzata.

## Confini Di Configurazione

Restano dati macchina:

- scheda, canale e polarita' I/O;
- quota base e trim di commissioning;
- encoder e counts/mm;
- durata impulso e default dei punti di intervento;
- matrice e classi per `MachineType`, usate come fallback legacy.

Restano dati ricetta:

- modo camera `Machine`, `Enabled` o `Disabled`;
- offset prodotto del punto camera;
- matrice vista x ispezione;
- classi Classify accettate per vista;
- soglia minima Classify per vista;
- abilitazione ispezione e abilitazione scarto.

Restano dati runtime:

- job VisionPro caricati e ruoli risolti;
- code risultati, companion attesi e watchdog;
- viste mostrate, contatori ed esito del prodotto.

## Contratto XML

Il nodo e' opzionale e viene serializzato dentro `RecipeData`:

```xml
<inspectionViewConfiguration IsConfigured="true">
  <Views>
    <View CameraRole="left">
      <AcceptedClassificationClasses>OK, Sealing Good</AcceptedClassificationClasses>
      <MinimumClassificationScore>0.65</MinimumClassificationScore>
      <Inspections>
        <Inspection Feature="SideSealing" Enabled="true" />
        <Inspection Feature="AIClassification" Enabled="true" />
      </Inspections>
    </View>
    <View CameraRole="rear">
      <AcceptedClassificationClasses>OK</AcceptedClassificationClasses>
      <MinimumClassificationScore>0.75</MinimumClassificationScore>
      <Inspections>
        <Inspection Feature="SideSealing" Enabled="false" />
        <Inspection Feature="AIClassification" Enabled="true" />
      </Inspections>
    </View>
  </Views>
</inspectionViewConfiguration>
```

`IsConfigured=false`, nodo assente o lista non ancora salvata indicano una
ricetta legacy. In quel caso il runtime usa i default macchina. Con
`IsConfigured=true` la matrice e' strict: una cella assente vale disabilitata.
Il primo salvataggio strict materializza comunque tutti i ruoli e tutte le
feature risolte, comprese le viste non mostrate in quel momento, per evitare
perdite future di configurazione o tassonomie Classify.

## Abilitazione Camera

Non esiste un secondo flag. La pagina `Inspection Configuration` legge e salva
lo stesso `machineRuntimeAdjustments.CameraTriggers` gia' usato dal resolver
dei punti di intervento:

- `Machine`: eredita lo stato del punto globale;
- `Enabled`: forza attivo il punto nella configurazione effettiva della ricetta;
- `Disabled`: disabilita il punto e il relativo profilo MultiShot.

`TOP/TOP3D` e' il risultato primario dell'orchestratore corrente e non puo'
essere disabilitato. Una macchina con la sola TOP attiva e' invece valida.

Il salvataggio e il reset sono consentiti solo a macchina ferma e senza prodotti
ancora tracciati; un hold impedisce un riavvio concorrente durante la scrittura.
Dopo il salvataggio il runtime ricalcola feature e camere effettive e riapplica
la configurazione I/O di ricetta. Una camera
disabilitata non viene attesa dall'orchestratore, non alimenta le code, non
genera timeout risultato e non viene mostrata nel container.

## Policy Classify

Le classi sono confrontate senza distinzione tra maiuscole e minuscole. Sono
ammessi separatori virgola, punto e virgola e a capo. Una lista vuota mantiene
il comportamento storico `OK/GOOD/PASS/PASSED/COMPLIANT`; una lista compilata
diventa autorevole per quella vista.

`MinimumClassificationScore` usa la stessa scala dell'output score del job,
tipicamente `0..1`, ed e' specifico per vista. `0` disabilita il gate. Se la
classe e' accettata ma lo score e' inferiore alla soglia, la HMI archivia classe
`Unclassify`, conserva lo score originale e assegna esito AI `0` (NoGood). Se
una soglia positiva e' configurata ma lo score manca, vale la stessa regola.

## Errori Database

Le tabelle `cfg_inspection_view` e `cfg_view_classification` restano default
macchina. `cfg_view_classification.MinimumScore` e' aggiunta in modo idempotente
con default `0`, quindi i database esistenti mantengono il comportamento. Se la lettura fallisce, il servizio conserva
l'ultimo snapshot valido invece di interpretare il guasto come configurazione
vuota. Le ricette con profilo esplicito non dipendono da queste due letture per
la propria matrice.

## Procedura Di Commissioning

1. Fermare la macchina e caricare la ricetta da qualificare.
2. Aprire `Inspection Configuration` e verificare il nome ricetta mostrato.
3. Per ogni job del VPP scegliere `Machine`, `Enabled` o `Disabled`.
4. Spuntare soltanto le ispezioni i cui output esistono nel ToolBlock di vista.
5. Inserire le classi Classify conformi usando i nomi esatti del modello.
6. Inserire la soglia minima di ogni vista usando la scala reale dello score.
7. Salvare, ricaricare la ricetta e verificare che le stesse scelte ricompaiano.
8. Controllare i log `RECIPE_INSPECTION_VIEW_PROFILE_ACTIVE` e
   `RECIPE_INSPECTION_RUNTIME_REFRESHED`.
9. Eseguire almeno un campione GOOD, uno NOK e uno sotto soglia per ogni vista AI.
10. Verificare assenza di `COMPANION_TIMEOUT` per camere disabilitate.
11. Cambiare a una seconda ricetta con un insieme camere diverso e ripetere il
    controllo di trigger, display, contatori e scarto.

## Criteri Di Accettazione

- una vista disabilitata non riceve trigger HMI e non viene attesa;
- una ispezione non assegnata non richiede il relativo output VisionPro;
- classi e score visualizzati appartengono alla stessa vista e allo stesso ciclo;
- uno score sotto soglia produce `Unclassify`, conserva il valore e rende il pezzo NoGood;
- il cambio ricetta aggiorna display e aspettative senza riavvio applicazione;
- una ricetta legacy continua a funzionare con i default macchina;
- un errore DB non amplia silenziosamente l'insieme dei controlli attivi.
