# Classificazione AI VisionPro come ispezione autonoma

## Scopo

Dalla release `3.1.1.0`, il risultato Classify pubblicato da un job VisionPro
e' una famiglia di ispezione autonoma chiamata `AIClassification`.

La classificazione non eredita piu' l'abilitazione, il contatore o lo scarto di
`SurfaceCheck`, `SideSealing` o di altri controlli immagine. La stessa logica e'
disponibile per i ruoli camera `Top`, `Top3D`, `Side`, `Left`, `Front`, `Rear`,
`Right` e `Bottom`.

## Confine di configurazione

| Livello | Responsabilita' |
|---|---|
| Macchina | Nessuna nuova impostazione. I/O, encoder, trigger, quote e MultiShot restano invariati. |
| Ricetta | `inspectionStatus.AIClassification` abilita la validazione; `ejectionStatus.AIClassification` autorizza lo scarto. |
| Runtime | Classe, score, card camera, esito del pezzo, messaggio difetto, allarme e contatore. |

Le ricette XML precedenti non contengono i due nuovi campi. Il valore di
default e' `false`, quindi il loro comportamento resta invariato fino a una
abilitazione esplicita e al successivo salvataggio della ricetta.

## Contratto VisionPro

Il ToolBlock radice del job camera deve pubblicare almeno uno degli output
classe seguenti:

- `EL_Classify`
- `Classify`
- `Classification`
- `Class`

Lo score e' opzionale. Gli alias riconosciuti sono:

- `EL_Score`
- `Score`
- `ClassificationScore`
- `Confidence`

Gli alias sono confrontati senza distinzione tra maiuscole e minuscole. La HMI
legge soltanto gli output gia' calcolati da VisionPro; non esegue una seconda
inferenza nel percorso di ispezione.

## Regole di esito

Quando `AIClassification` e' abilitata e il job espone un output classe:

- `OK`, `GOOD`, `PASS`, `PASSED` e `COMPLIANT` producono esito conforme;
- `NOK`, `NG`, `KO`, `FAIL`, `FAILED`, `BAD`, `REJECT` e `NOREAD` producono
  esito non conforme;
- una classe libera non inclusa nelle etichette conformi produce esito non
  conforme. Esempio: `Sealing Open`;
- un output presente ma vuoto o illeggibile produce esito non conforme;
- lo score viene mostrato e archiviato quando disponibile, ma non sostituisce
  la decisione della classe.

Se il job non espone alcun output classe, il controllo non viene applicato a
quella camera. Se la funzione ricetta e' disabilitata, classe e score possono
restare disponibili nello storico per diagnosi, ma non modificano esito,
scarto, allarmi o contatori e la card operatore viene nascosta.

## Contatore e scarto

`AIClassification` ha una riga dedicata nei contatori difetto, con icona e
ultimo messaggio. Il contatore globale e' persistito con chiave DB
`AI_CLASSIFICATION`.

Il `CounterManager` deduplica la famiglia difetto nel ciclo pezzo: se due o piu'
camere classificano NOK lo stesso prodotto, il contatore AI aumenta una sola
volta. I dettagli per camera restano disponibili nei risultati e nello storico.

Lo scarto fisico avviene solo quando sono vere entrambe le condizioni:

1. `inspectionStatus.AIClassification = true`;
2. `ejectionStatus.AIClassification = true`.

Con ispezione abilitata e scarto disabilitato, il pezzo puo' risultare No Good
e incrementare il contatore senza autorizzare l'attuatore di scarto per questa
famiglia.

## Allarmi

La famiglia `DefectType.AIClassification` e' selezionabile nelle regole allarme
scarto. I messaggi che iniziano con `AI classification failed` vengono mappati
solo a questa famiglia e non a sealing o surface.

## Database

L'inizializzazione idempotente aggiunge, se mancanti:

- la voce `AIClassification` in `cfg_inspection`, disabilitata di default per i
  tipi macchina supportati;
- il contatore `AI_CLASSIFICATION` in `tblglobalcounters` tramite il mapping
  esistente dei contatori;
- cinque esiti per vista in `tblgenerale`:
  `NC_TopAiClassification`, `NC_SideAiClassification`,
  `NC_FrontAiClassification`, `NC_RearAiClassification` e
  `NC_BottomAiClassification`.

Gli esiti usano la convenzione comune `0=NOK`, `1=GOOD` e `4=non
abilitato/non eseguito`. `Top3D` confluisce in Top, `Left` in Side e `Right` in
Rear. Label e score restano nelle colonne dedicate e non vengono sostituiti
dall'esito numerico. Le righe storiche non vengono aggiornate automaticamente.

## Collaudo minimo

| Test | Configurazione | Risultato atteso |
|---|---|---|
| 1 | Ispezione OFF, classe NOK | Nessun No Good AI, nessun contatore, nessuno scarto, card nascosta. |
| 2 | Ispezione ON, classe GOOD | Pezzo conforme rispetto all'AI, card verde, contatore invariato. |
| 3 | Ispezione ON, scarto OFF, classe NOK | Pezzo No Good, card rossa e contatore +1; nessuno scarto attribuito all'AI. |
| 4 | Ispezione ON, scarto ON, classe NOK | Pezzo No Good, contatore +1 e richiesta di scarto. |
| 5 | Due camere NOK sullo stesso pezzo | Un solo incremento `AIClassification`. |
| 6 | Output classe assente | Nessun errore AI per quella camera. |
| 7 | Output classe vuoto | Errore AI chiaro e contatore +1. |
| 8 | Ricetta XML storica | Caricamento riuscito, AI disabilitata fino al salvataggio esplicito. |
| 9 | Verifica DB per vista | Esito `1` per una classe GOOD, `0` per NOK/errore classe e `4` per vista non eseguita o AI disabilitata. |

Verificare inoltre che `SurfaceCheck` e `SideSealing` non cambino quando varia
soltanto l'output Classify.
