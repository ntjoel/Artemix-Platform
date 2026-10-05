# Qtis Vision Offline Installer

Questa cartella contiene il builder e il bootstrapper dell'installer offline di
`QtisVisionPanel`.

Il supporto generato usa tre sorgenti:

| Contenuto | Sorgente predefinita | Destinazione target |
|---|---|---|
| Configurazioni, VPP, XML job, lingue, ricette e modelli | `C:\QtisVision` | `C:\QtisVision` |
| Applicazione e DLL x64 | `bin\x64\Release` | `C:\QtisVision\bin` |
| Prerequisiti offline | `D:\QtisInstallerPayloads` | Installazione vendor / ambiente AI |

## Generazione

Da PowerShell nella root della repo:

```powershell
powershell -ExecutionPolicy Bypass -File .\Installer\Build-QtisInstallerMedia.ps1
```

Per rimuovere password, token e segreti XML dal seed:

```powershell
powershell -ExecutionPolicy Bypass -File .\Installer\Build-QtisInstallerMedia.ps1 -RedactSecrets
```

Il comando genera due output autonomi:

```text
D:\QtisInstallerOutput\QtisVisionPanel_<versione>-r<revisione-media>
D:\QtisInstallerOutput\QtisVisionPanel_Update_<versione>-r<revisione-media>
```

La versione e' letta da `bin\x64\Release\QtisVisionPanel.exe`; il builder non
modifica la versione della HMI. `MediaRevision` identifica invece una nuova
composizione del supporto a parita' di HMI, ad esempio l'aggiunta di un
prerequisito:

```powershell
powershell -ExecutionPolicy Bypass -File .\Installer\Build-QtisInstallerMedia.ps1 -MediaRevision 2
```

Il default corrente e' `r18`. Oltre ai controlli precedenti, il builder richiede
`AiRuntime\QtisVisionPanel.OnnxWorker.exe` e impedisce di creare il media se le
DLL Microsoft ONNX sono presenti direttamente accanto alla HMI. VisionPro ViDi
EL e il classificatore HMI devono usare processi e runtime nativi separati. Il
builder esclude anche la cache runtime `QtisVisionPanel.exe.WebView2`, che non
fa parte della release applicativa e non deve essere trasferita tra macchine.

## Supporto verificato corrente

La baseline dei media correnti e' la HMI `3.1.5.4`, release del `2026-09-14`.
Per la rigenerazione dai sorgenti del `2026-09-18`, consultare il report
`Documentation/MachineHardware/installer-release-verification-2026-09-18.md`.

| Campo | Valore |
|---|---|
| HMI | `3.1.5.4` |
| Revisione media | `r19` |
| Installer completo | `D:\QtisInstallerOutput\QtisVisionPanel_3.1.5.5-r19` |
| Updater protetto | `D:\QtisInstallerOutput\QtisVisionPanel_Update_3.1.5.5-r19` |
| Payload full/update | `30` / `1` |
| Validazione builder | `PASSED` per full e update |
| Validazione bootstrapper full | `PASSED`, `installer-validation.json -> isValid=true` |
| Gate updater su PC senza HMI | `PASSED`, installazione rifiutata prima di ogni modifica |

La revisione media passa a `r18` per distribuire la release `3.1.5.4`, i
cataloghi lingua aggiornati, la configurazione AI per vista e il salvataggio
annotato correlato. Il pacchetto `UpdateOnly` resta privo di seed e prerequisiti.

Il controllo finale ha confermato che:

- l'EXE dentro `ApplicationBin-3.1.5.4.zip` riporta versione file `3.1.5.4`;
- lo ZIP applicativo contiene `LibreHardwareMonitorLib.dll`, `DiskInfoToolkit.dll`,
  `HidSharp.dll`, `RAMSPDToolkit-NDD.dll` e il notice di licenza;
- lo ZIP contiene il worker e il runtime Microsoft in `AiRuntime`, con zero DLL
  ONNX Microsoft direttamente accanto all'EXE;
- lo ZIP contiene i cataloghi `Localization` aggiornati; sulle macchine gia'
  installate completano in memoria soltanto le chiavi mancanti dei file lingua
  runtime, senza sovrascrivere le personalizzazioni in `C:\QtisVision\Language`;
- sono presenti script AI, `onnxscript` e l'intero wheelhouse Python offline;
- il runtime seed contiene `cfg`, `Programs`, `Language` e i modelli AI correnti;
- VisionPro 9.25 e la patch KB5077181 rispettano gli SHA-256 approvati;
- UltraVNC 1.6.4.0 x64 e Notepad++ 8.9.3 x64 hanno firma Authenticode
  valida e SHA-256 bloccato nel builder;
- `VisionProDependencies` non e' duplicata nello ZIP e viene ricreata come
  junction dal bootstrapper;
- `Cognex.Vision.Startup.Net.dll` non e' presente accanto all'EXE: viene risolta
  dalla stessa junction di RBBT, ViDi EL e del runtime ONNX Cognex.
- la cache `QtisVisionPanel.exe.WebView2` non e' presente nello ZIP;
- il manifest include il componente opzionale, selezionato di default,
  `cognex-gige-network`;
- il manifest include `qtis-autostart` e il bootstrapper incorpora il launcher
  PowerShell ritardato;
- `--inventory` include identita SMBIOS e stato update della HMI.

## Contenuto del media

```text
QtisVisionPanel_<versione>-r<revisione-media>\
  QtisVisionSetup.exe
  installer-manifest.json
  payload-integrity.sha256
  README-INSTALLAZIONE.txt
  Payloads\
    Core\
      ApplicationBin-<versione>.zip
      RuntimeSeed-<versione>.zip
    VisionPro\
      VisionPro_9_25_64-bit.zip
      KB5077181\
    Advantech\
    MySQL\
    Python\
    Tools\
      UltraVNC_1640_x64_Setup.exe
      npp.8.9.3.Installer.x64.exe

QtisVisionPanel_Update_<versione>-r<revisione-media>\
  QtisVisionUpdate.exe
  installer-manifest.json
  payload-integrity.sha256
  README-AGGIORNAMENTO.txt
  Payloads\
    Core\
      ApplicationBin-<versione>.zip
```

Ogni file usato dall'installer ha dimensione e SHA-256 registrati nel manifest.
Il pulsante `Verifica` ricalcola gli hash prima di modificare il PC.

## Rilevamento prerequisiti gia installati

All'apertura il bootstrapper rileva i prerequisiti compatibili. I componenti
opzionali con stato `Gia installato` vengono deselezionati e non sono
reinstallati. L'ambiente Python AI resta l'eccezione: quando e' selezionato
viene riparato e ricontrollato anche se `python.exe` esiste.

UltraVNC e' una seconda eccezione intenzionale: se selezionato resta attivo
anche quando e' gia installato. Il bootstrapper non effettua downgrade di una
versione compatibile, ma verifica il servizio e riapplica la configurazione di
accesso prevista dalla baseline.

Per DAQNavi il controllo non dipende piu' da una singola cartella fissa. Il
bootstrapper verifica, in ordine:

1. il marker ufficiale
   `HKLM\SOFTWARE\Advantech\Components\cmp_daqnavi_runtime\Automation.BDaq4.dll`;
2. il percorso `Path` registrato da Advantech e la `FileVersion`;
3. il runtime `Automation.BDaq4.dll` nelle root DAQNavi standard sotto
   `%SystemDrive%`, `%ProgramFiles%` e `%ProgramFiles(x86)%`.

La versione minima accettata e' `4.0.0.0`. La sola presenza di XNavi o del
Navigator non basta: il runtime Automation.BDaq deve essere realmente
installato. La presenza dei driver specifici PCIE-1756 e PCIE-1884 resta una
verifica di commissioning.

L'inventario puo' essere generato senza aprire la UI:

```powershell
.\QtisVisionSetup.exe --inventory
```

Il comando scrive `installer-inventory.json` accanto al setup. Oltre ai
prerequisiti riporta produttore e modello SMBIOS, baseboard, BIOS, CPU e lo
stato `Fresh` / `Upgrade` / `Repair` della HMI. Se il BIOS restituisce stringhe
generiche, `modelIsGeneric=true`: il setup non inventa un modello commerciale.

## Assistenza remota e tool tecnici

UltraVNC viene installato in modalita silenziosa con:

- componenti Server e Viewer;
- servizio Windows `uvnc_service`;
- avvio servizio automatico;
- task firewall creati dall'installer vendor;
- password di controllo remoto configurata tramite `setpasswd.exe`.

Il valore in chiaro non viene scritto in `installer-manifest.json`, nel README
generato o nei log del bootstrapper. Il provisioning aggiorna
`%ProgramData%\UltraVNC\ultravnc.ini` in modo atomico, mantiene un backup del
file precedente, riavvia il servizio e richiede lo stato `RUNNING`. Sono
supportate sia le installazioni 1.6.x sia le versioni piu recenti che salvano
la configurazione in ProgramData.

Notepad++ 8.9.3 x64 viene installato o aggiornato con lo switch silent `/S`.
Serve per leggere XML, log e configurazioni durante commissioning e
manutenzione; non e' una dipendenza del ciclo macchina.

## Regola fresh install

Su un PC nuovo:

1. vengono installati i prerequisiti selezionati;
2. viene creato `C:\QtisVision\bin`;
3. il runtime seed inizializza le cartelle mancanti sotto `C:\QtisVision`;
4. vengono creati i collegamenti desktop e menu Start;
5. viene scritto lo stato in
   `C:\ProgramData\Pulsar\QtisVision\install-state.json`.

## Regola aggiornamento

Per la manutenzione ordinaria usare il supporto separato
`QtisVisionPanel_Update_<versione>-r<revisione>`. Il relativo manifest
`packageType=UpdateOnly`:

- rifiuta un PC senza `C:\QtisVision\bin\QtisVisionPanel.exe`;
- contiene esclusivamente il componente `qtis-core` e `ApplicationBin`;
- non contiene o esegue `RuntimeSeed`, prerequisiti, rete, Python o shortcut;
- non trasporta `Programs`, VPP, ricette, configurazioni, Language macchina,
  immagini o modelli AI.

Su una macchina esistente:

- la HMI deve essere chiusa;
- la versione del file `QtisVisionPanel.exe` determina `Nuova installazione`,
  `Aggiorna`, `Ripara` oppure `Downgrade bloccato`;
- il bootstrapper costruisce un inventario SHA-256 del payload applicativo e lo
  confronta con i file realmente installati;
- vengono copiati soltanto i file aggiunti o diversi e rimossi soltanto i file
  non piu presenti che appartenevano a un precedente manifest affidabile;
- prima di toccare un file, il bootstrapper salva solo i file coinvolti sotto
  `C:\QtisVision\InstallerBackups\<timestamp>\ApplicationChanged`;
- `C:\QtisVision\bin\cfg`, `OPC`, `QtisVisionPanel.exe.WebView2` e
  `VisionProDependencies` sono esclusi dal confronto e restano protetti;
- `VisionProDependencies` viene verificata e riutilizzata quando punta gia alla
  cartella `bin` Cognex corretta;
- configurazioni, VPP, ricette, immagini e modelli AI esistenti non vengono
  sovrascritti;
- i file lingua, considerati parte della release software, vengono aggiornati
  dopo averne creato il backup;
- un journal persistente in
  `C:\ProgramData\Pulsar\QtisVision\pending-core-update.json` permette il
  rollback automatico e il recovery al successivo avvio dell'installer;
- al termine viene scritto
  `C:\ProgramData\Pulsar\QtisVision\application-files.json`, usato come
  contratto dei file software gestiti per l'aggiornamento successivo.

Il primo aggiornamento proveniente da un installer precedente a `r14` non ha
ancora un manifest affidabile. In quel solo passaggio i file sconosciuti vengono
preservati per sicurezza; vengono comunque rimossi dalla root i duplicati ONNX
e `Cognex.Vision.Startup.Net.dll` noti come incompatibili. Dall'aggiornamento
successivo anche la rimozione dei file software obsoleti e' deterministica.

## Configurazione automatica rete Cognex GigE

Il componente `cognex-gige-network` viene eseguito dopo la HMI e usa il
`CameraConfig.xml` risolto dal `Config.xml` attivo. Gli attributi rete sono
opzionali e retrocompatibili; il formato storico con i soli `type` e `id`
continua a essere valido.

Esempio di associazione deterministica:

```xml
<CameraConfiguration>
  <Camera type="Top"
          id="0"
          adapterName="Camera Top"
          adapterMac="001122AABBCC"
          hostIpAddress="192.168.21.203"
          cameraIpAddress="192.168.21.2"
          prefixLength="24" />
</CameraConfiguration>
```

`adapterGuid` puo' essere usato al posto di nome o MAC. Se gli attributi non
sono presenti, il bootstrapper considera soltanto adattatori Ethernet fisici
con binding Cognex eBUS attivo, IPv4 privata statica, nessun default gateway e
collegamento almeno Gigabit quando la porta e' connessa. Un match assente o
ambiguo produce `NoCandidates` e non modifica la rete.

Prima delle modifiche viene salvato lo stato di IPv4, binding, proprieta
avanzate e power management. Sulla sola porta camera riconosciuta vengono
applicati, quando esposti dal driver OEM:

- DHCP disabilitato e IP host esplicito validato nella stessa subnet camera;
- Energy Efficient Ethernet disabilitato;
- Interrupt Moderation abilitato e rate `Extreme`;
- Jumbo Packet almeno `9000`;
- Receive/Transmit Buffers e numero code RSS al massimo;
- binding limitati a IPv4 e Cognex eBUS;
- sospensione selettiva e opzioni equivalenti di risparmio energetico
  disabilitate quando supportate dal cmdlet Windows.

Una porta con default gateway non viene mai modificata. Il bootstrapper non
disabilita globalmente Windows Firewall e non cambia il piano energia del PC.
Backup e report si trovano in:

```text
C:\ProgramData\Pulsar\QtisVision\Installer\NetworkBackups
C:\ProgramData\Pulsar\QtisVision\Installer\NetworkResults
```

Dopo il reboot verificare la porta con `Cognex GigE Vision Configuration Tool`.
Nel VPP controllare inoltre `Packet Size >= 8000` e `Latency` pari a `0` o `1`:
sono parametri del job VisionPro e non della scheda Windows, quindi non vengono
modificati dall'installer.

## Python AI offline

Python viene installato in:

```text
C:\QtisVision\AI\Python
```

Il wheelhouse contiene versioni bloccate di:

- Python 3.13.14 x64;
- PyTorch 2.8.0 CPU;
- Pillow 11.3.0;
- NumPy 2.3.2;
- ONNX 1.19.1;
- ONNX Script 0.5.2;
- dipendenze transitive.

Il componente Python viene sempre rieseguito quando selezionato. Questo rende
l'installazione riparabile: se `python.exe` esiste ma manca `onnxscript`, il
builder reinstalla i wheel mancanti e lancia un export ONNX reale come smoke
test. Il percorso Python viene scritto nel
`C:\QtisVision\bin\cfg\machine_runtime_config.xml` effettivamente letto dal
servizio macchina.

Il bootstrapper aggiunge in modo idempotente al `PATH` macchina:

```text
C:\QtisVision\AI\Python
C:\QtisVision\AI\Python\Scripts
```

Imposta inoltre `QTIS_PYTHON_ROOT` e `QTIS_PYTHON_EXE`. Non imposta
`PYTHONHOME`, per non interferire con Python installati da Cognex o da altri
software. Il riallineamento viene eseguito anche durante un update HMI quando
l'ambiente Qtis esiste gia e il componente Python non deve essere reinstallato.

## Avvio automatico HMI ritardato

Il componente `qtis-autostart`, selezionato di default, crea il collegamento:

```text
%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\Qtis Vision Panel (Delayed).lnk
```

Il collegamento avvia senza finestra lo script:

```text
C:\ProgramData\Pulsar\QtisVision\Startup\Start-QtisVisionPanel.ps1
```

Il launcher:

1. impedisce una seconda istanza di `QtisVisionPanel`;
2. attende il ritardo iniziale, `45 s` per default;
3. attende fino a `120 s` i servizi che corrispondono ai pattern configurati,
   `MySQL*` per default;
4. attende lo stato `Up` delle porte camera presenti nell'ultimo report Cognex;
5. avvia la HMI con la directory di lavoro corretta;
6. in caso di timeout avvia comunque la HMI per default, lasciando alla
   diagnostica applicativa la gestione delle dipendenze non disponibili.

La politica macchina e' modificabile senza ricompilare:

```text
C:\ProgramData\Pulsar\QtisVision\Startup\startup-settings.json
```

Campi disponibili: `initialDelaySeconds`, `dependencyWaitSeconds`,
`pollIntervalSeconds`, `launchWhenDependenciesTimeout` e
`serviceNamePatterns`. Il log ruotato e' in
`C:\ProgramData\Pulsar\QtisVision\Startup\Logs\startup.log`.

## Identita PC e sensori temperatura

L'inventario legge l'identita dal firmware SMBIOS. Sul PC industriale
`AVS-532-EH1Q1 / i7` ci si aspetta almeno CPU Intel i7-13700TE, Q670E e 32 GB;
il nome commerciale compare soltanto se il costruttore lo ha scritto nel BIOS.

Le card temperatura HMI usano per default `HardwareTemperatureProvider=Auto`:

1. `LibreHardwareMonitorLib` legge i sensori realmente esposti di CPU,
   motherboard/Super I/O, memoria e storage;
2. se il provider o un singolo gruppo di sensori non e' disponibile, la HMI usa
   il fallback Windows `MSFT_StorageReliabilityCounter` per i dischi e
   `MSAcpi_ThermalZoneTemperature` per la zona termica di sistema.

Il provider e le sue dipendenze fanno parte di `ApplicationBin`; non serve
installare LibreHardwareMonitor come programma separato. La HMI deve mantenere
l'avvio elevato gia' previsto dal manifest. Controller RAID, alcuni SSD NVMe,
firmware industriali e moduli DDR5 possono comunque non pubblicare il dato. In
quel caso `Sensor not available` e' corretto: non viene mostrata una stima come
misura reale e il ciclo macchina non viene bloccato.

## Prerequisiti e limiti

- VisionPro viene avviato dal setup Cognex ufficiale e richiede gestione
  licenza secondo la procedura Cognex.
- `Qtis Vision Panel` dipende da VisionPro: il bootstrapper richiede che il
  componente sia gia' installato oppure selezionato nello stesso flusso.
- La cartella `VisionProDependencies` vista accanto alla HMI non contiene una
  seconda copia delle DLL Cognex. E' una junction NTFS creata dal setup verso
  `%VPRO_ROOT%\bin` e validata tramite
  `Cognex.Vision.Startup.Net.dll`. Anche la DLL bootstrap deve provenire
  esclusivamente dalla junction: una copia accanto a `QtisVisionPanel.exe`
  interrompe la risoluzione nativa RBBT/ViDi EL dei VPP Classify.
- Dopo VisionPro 9.25 il bootstrapper propone
  `Cognex VisionPro Windows Update KB5077181 Patch`. La patch viene rilevata
  tramite la chiave uninstall
  `{17F14A9A-E830-42B8-9E1C-049A4CA78758}` e non viene riproposta se e' gia'
  installata in versione `1.0` o successiva.
- MySQL usa il setup ufficiale firmato. Account, password, servizio e policy
  TLS restano parametri del sito e non sono hardcoded nel bootstrapper.
- I PC forniti con un runtime DAQNavi compatibile vengono riconosciuti e il
  componente non viene reinstallato. Verificare comunque in DAQNavi Navigator
  che i driver delle schede effettivamente montate siano presenti.
- UltraVNC espone un servizio di accesso remoto. Limitare TCP 5900 alla rete
  macchina o VPN autorizzata e sostituire la credenziale baseline quando la
  policy del sito richiede una password univoca.
- La password UltraVNC e' incorporata nel profilo di provisioning per ottenere
  una configurazione automatica. La media installer deve quindi essere
  custodita come materiale tecnico riservato.
- `XNavi.exe` e' firmato ma puo' scaricare i pacchetti DAQNavi. Per un PC
  completamente isolato occorre generare prima il cache package offline con
  XNavi e aggiungerlo ai payload.
- L'eseguibile interno della patch Cognex non espone una firma Authenticode.
  Per questo costituisce un'eccezione esplicita e circoscritta: il builder
  accetta esclusivamente lo ZIP con SHA-256
  `1E766579810017201E8FC9A931C0D683CBD1DBA333284EA2D4E6E36BA00E2F13`
  e l'EXE estratto con SHA-256
  `774BFA603A4ED4AB16E6C27B36D1853DB1C0A9528EDA39CC18D1BC0AEE0731F9`.
- Il vecchio MySQL Installer 8.0.18 con `HashMismatch` non viene incluso.
- Il seed deriva dalla macchina sorgente e puo' contenere dati sensibili. Il
  supporto deve essere custodito come materiale tecnico riservato.

## Log

I log del bootstrapper sono scritti in:

```text
C:\ProgramData\Pulsar\QtisVision\Installer\Logs
```

La validazione non interattiva:

```powershell
.\QtisVisionSetup.exe --validate
```

scrive anche `installer-validation.json` accanto all'eseguibile.
