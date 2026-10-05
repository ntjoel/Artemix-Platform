# DALSA Dual-Illumination Cycling Presets

## Scopo

Questa baseline sincronizza una camera DALSA usata per due acquisizioni alternate:

- luce frontale per controllo saldatura;
- backlight per controllo rotolo girato.

La HMI continua a generare la sequenza MultiShot guidata da encoder. Ogni impulso
hardware produce una sola immagine. La camera cambia autonomamente illuminazione
tra un frame e il successivo tramite due `Cycling Preset`; VisionPro separa e
stitcha i due flussi.

## Confine configurazione

La configurazione e' globale macchina e si trova in
`machine_runtime_config.xml` sotto il profilo MultiShot `Side`, `Right` o
`Bottom`. Non vengono aggiunti campi ricetta e non cambia il controller trigger.

Il file effettivamente letto e' sempre
`<cartella applicazione>\cfg\machine_runtime_config.xml`. Un file chiamato
`machine_runtime_config.template.xml` salvato in Documenti o in una cartella di
commissioning e' solo una sorgente di preparazione: modificarlo non cambia la
macchina finche' non viene caricato dalla HMI o copiato nel percorso attivo con
l'applicazione chiusa e dopo avere creato un backup.

Il blocco resta disabilitato per default, quindi le macchine esistenti continuano
a usare il MultiShot standard.

```xml
<DualIllumination>
  <Enabled>true</Enabled>
  <FrontFirst>true</FrontFirst>
  <ExposureTimeUs>500</ExposureTimeUs>
  <FrontOutputLine>Line3</FrontOutputLine>
  <BackOutputLine>Line4</BackOutputLine>
  <ActiveOutputSource>ExposureActive</ActiveOutputSource>
  <DualIlluminationEnabledInputName>dualIlluminationEnabled</DualIlluminationEnabledInputName>
  <FrontFirstInputName>frontFirst</FrontFirstInputName>
</DualIllumination>
```

## Sequenza runtime

1. Dopo il caricamento o reload del VPP, la HMI risolve il job del profilo.
2. La HMI scrive nel ToolBlock `ImageStitching` i parametri di stitching e gli
   input `dualIlluminationEnabled` e `frontFirst`.
3. Tramite `OwnedGigEAccess` configura due Cycling Preset DALSA:
   `cyclingPresetCount=2`, incremento `StartOfFrame`, repeater `1` e reset
   software.
4. Il preset 1 abilita una sola uscita luce; il preset 2 abilita l'altra. Entrambi
   usano l'esposizione configurata, default `500 us`.
5. La configurazione viene marcata come da riallineare. Non viene avviata una
   sessione in questa fase.
6. Alla fotocellula, prima di creare la prima sessione MultiShot, la HMI esegue
   `cyclingPresetResetCmd`.
7. Il reset DALSA con sorgente `StartOfFrame` diventa effettivo sul successivo
   evento frame. Per questo la lettura immediata di
   `cyclingPresetCurrentActiveSet` e' diagnostica e puo' ancora mostrare il set
   precedente.
8. Il controller genera gli stessi N impulsi encoder-driven della baseline.
9. Con N pari, VisionPro riceve coppie alternate Front/Backlight e restituisce
   l'esito solo quando i due stitching sono completi.

## Riallineamento e sicurezza

La fase viene marcata per reset prima del prodotto successivo quando avviene uno
dei seguenti eventi:

- sessione MultiShot annullata;
- timeout sessione;
- errore nella scrittura dell'impulso trigger;
- errore di configurazione;
- recovery automatica VisionPro.

Il comando di reset non viene mai inviato mentre il coordinatore considera attiva
la sessione precedente. Un nuovo prodotto sovrapposto viene quindi bloccato invece
di cambiare preset a meta' sequenza.

Un trigger perso fisicamente dalla camera, pur essendo stato scritto correttamente
dalla scheda I/O, non e' osservabile direttamente dalla HMI. In quel caso il
timeout/stallo VisionPro attiva la recovery e marca il riallineamento prima del
prodotto seguente.

Se camera, feature GenICam o input VisionPro richiesti non sono disponibili, il
profilo doppia illuminazione non parte e viene registrato un evento chiaro. La HMI
non va in crash e gli altri profili non vengono modificati.

## Parametri HMI

Aprire `Configuration > PCIE settings > Machine Setup > Camera MultiShot`, quindi
selezionare il profilo camera corretto.

| Campo | Valore iniziale | Significato |
|---|---:|---|
| Enable alternating illumination | Off | Abilita i due Cycling Preset solo per il profilo selezionato. |
| Preset 1 uses front light | On | Il primo frame dopo il reset usa la luce frontale. |
| Camera exposure | 500 us | Esposizione applicata a entrambi i preset. |
| Front-light output | Line3 | Uscita multipolare collegata alla luce frontale. |
| Backlight output | Line4 | Uscita multipolare collegata alla backlight. |
| Active output source | ExposureActive | Mantiene la luce attiva durante l'esposizione. |

Vincoli di salvataggio:

- MultiShot deve essere abilitato;
- `Shots` deve essere pari e almeno 2;
- esposizione maggiore di zero;
- Front e Backlight devono usare uscite diverse;
- la sorgente deve essere supportata dalla camera.

Il numero base resta configurazione macchina. Una ricetta puo' applicare il
delta MultiShot gia' previsto dalla baseline, ma il resolver rifiuta il
salvataggio quando il numero effettivo diventa dispari o minore di 2. Non viene
eseguito alcun arrotondamento silenzioso, perche' cambierebbe la geometria del
prodotto e lascerebbe incompleta una coppia Front/Backlight.

## Contratto VisionPro

Il job fisico deve chiamarsi con un ruolo compatibile (`Left`, `Side`, `Right`,
`Rear` o `Bottom`) e contenere il ToolBlock `ImageStitching`.

Input obbligatori quando il doppio profilo e' abilitato:

- `expectedFrames` (`Double`): numero totale di trigger, sempre pari;
- `stepMm` (`Double`): distanza tra due trigger fisici consecutivi;
- `mmPerPixel` (`Double`): calibrazione camera;
- `dualIlluminationEnabled` (`Boolean`);
- `frontFirst` (`Boolean`).

Lo script completo di riferimento e':

- `visionpro-alternating-dual-multishot-job-script.vb`

La selezione luce avviene nella camera prima dell'esposizione. Lo script
`PostAcquisitionRef` non deve comandare le luci: riceve frame gia' illuminati,
li separa per fase e restituisce il risultato finale.

## Eventi diagnostici

| Evento | Significato |
|---|---|
| `MULTISHOT_SIDE_OUTPUT_MAPPING_RESOLVED` | Il profilo Left/Side ha risolto una sola uscita fisica CameraTrigger; il messaggio riporta scheda e DO. |
| `MULTISHOT_SIDE_OUTPUT_MAPPING_INVALID` | Uscita assente, duplicata o non coerente; nessuna sessione Left deve partire. |
| `CAMERA_CYCLING_PRESETS_CONFIGURED` | Due preset programmati dopo il caricamento job. |
| `CAMERA_CYCLING_PRESET_RESET_ARMED` | Reset software inviato prima della sessione successiva. |
| `CAMERA_CYCLING_PRESET_ACTIVE_SET` | Set corrente letto quando la feature e' disponibile. |
| `CAMERA_CYCLING_PHASE_REALIGN_REQUIRED` | Cancel, timeout, errore trigger o recovery richiedono reset. |
| `CAMERA_CYCLING_PRESET_RESET_DEFERRED_ACTIVE_SESSION` | Reset/configurazione rifiutati per sessione ancora attiva. |
| `CAMERA_CYCLING_PRESET_CONFIG_INVALID` | Parametri macchina non validi. |
| `CAMERA_CYCLING_PRESET_CONFIG_FAILED` | Feature DALSA assente o scrittura GenICam fallita. |
| `CAMERA_CYCLING_PRESET_RESET_FAILED` | Comando reset non eseguito; il prodotto non viene armato. |
| `CAMERA_CYCLING_PRESET_SESSION_BLOCKED` | Job o ToolBlock non conformi al contratto. |

## Collaudo macchina

1. Fermare la macchina e verificare cablaggio, massa e polarita' delle due luci.
2. Verificare che `Line3` e `Line4` corrispondano realmente ai due conduttori del
   cavo camera installato.
3. Impostare `Shots=2` per la prima prova, esposizione `500 us` e una velocita'
   bassa.
4. Salvare la configurazione e ricaricare il VPP.
5. Verificare prima `MULTISHOT_SIDE_OUTPUT_MAPPING_RESOLVED`, poi
   `VISIONPRO_STITCHING_PARAMS_APPLIED` e
   `CAMERA_CYCLING_PRESETS_CONFIGURED`.
6. Passare un solo prodotto e verificare la sequenza Front, Backlight e l'esito
   finale VisionPro.
7. Portare gradualmente gli scatti al valore di produzione, sempre pari.
8. Durante una prova, arrestare la sessione o forzare una recovery. Al prodotto
   seguente verificare prima `CAMERA_CYCLING_PHASE_REALIGN_REQUIRED`, poi
   `CAMERA_CYCLING_PRESET_RESET_ARMED`.
9. Eseguire almeno 100 prodotti consecutivi alla velocita' massima prevista e
   verificare che ogni sessione abbia N trigger, N frame e una sola coppia di
   risultati finali.

## Rollback

Disabilitare `Enable alternating illumination`, salvare e ricaricare il VPP. La
HMI scrive `dualIlluminationEnabled=false` quando l'input opzionale esiste e non
tocca piu' i Cycling Preset. Il controller MultiShot encoder-driven resta quello
della baseline precedente.
