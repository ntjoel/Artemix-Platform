# Offline Installer Architecture And Build - 2026-07-23

## Obiettivo

Creare un supporto ripetibile per installare o aggiornare Qtis Vision Panel su
PC macchina senza ricostruire manualmente cartelle, DLL e ambiente AI.

La soluzione mantiene il contratto corrente:

- configurazione macchina sotto `C:\QtisVision`;
- job e ricette separati dai binari;
- applicazione x64 sotto `C:\QtisVision\bin`;
- nessun cambio alla logica I/O, encoder, VisionPro o ciclo produzione.

## Architettura

Il supporto usa un bootstrapper self-contained `.NET 8` x64 con manifest
esterno. I payload restano file separati per evitare un singolo eseguibile da
oltre 8 GB, difficile da copiare, verificare e riparare.

Il manifest descrive per ogni componente:

- nome e tipo installer;
- percorso di rilevamento sul target;
- versione minima;
- modalita automatica o guidata;
- dimensione;
- SHA-256.

## Componenti

| ID | Componente | Modalita | Nota |
|---|---|---|---|
| `visionpro` | Cognex VisionPro 9.25 x64 | Guidata | Setup ufficiale dal relativo ZIP. |
| `visionpro-kb5077181` | VisionPro KB5077181 | Guidata | Patch Cognex eseguita dopo VisionPro e rilevata dal registro Windows. |
| `daqnavi` | Advantech DAQNavi | Guidata | Mantiene runtime OEM compatibili; XNavi resta disponibile quando manca il runtime. |
| `mysql` | MySQL Community 8.0.46 | Guidata | MSI Oracle firmato. Configurazione account locale al sito. |
| `heidisql` | HeidiSQL 12.11 | Opzionale | Tool di manutenzione, non server database. |
| `ultravnc` | UltraVNC 1.6.4.0 x64 | Automatica | Server/Viewer, servizio automatico e provisioning password; riconfigura anche versioni compatibili gia presenti. |
| `notepad-plus-plus` | Notepad++ 8.9.3 x64 | Automatica | Editor tecnico per XML, configurazioni e log. |
| `python-ai` | Python e wheel AI | Automatica | Installazione isolata e smoke test ONNX. |
| `qtis-core` | HMI e runtime seed | Automatica | Installazione o update differenziale SHA-256, junction VisionPro e rollback. |
| `cognex-gige-network` | Porte camera Cognex GigE | Automatica opzionale | Associazione conservativa, backup NIC e ottimizzazione driver dopo la HMI. |
| `qtis-autostart` | Startup HMI macchina | Automatica opzionale | Launcher nascosto, ritardo configurabile, attesa servizi/NIC camera e istanza unica. |

## Confine macchina, software e runtime

### Macchina

Il seed puo' contenere:

- `cfg\Config.xml`;
- mapping I/O ed encoder;
- seriali camera;
- configurazioni OPC UA e database;
- job VPP e XML associati.

Questi file inizializzano un PC nuovo, ma non vengono sovrascritti su una
macchina gia configurata.

### Software

Da `r14` la cartella `bin` non viene piu sostituita integralmente su una
macchina esistente. Il bootstrapper genera un manifest SHA-256 dei file
software e classifica ogni elemento come aggiunto, modificato, invariato o
rimosso. Copia e archivia soltanto i file coinvolti.

Restano sempre esclusi dal dominio differenziale:

- `C:\QtisVision\bin\cfg`;
- `C:\QtisVision\bin\OPC`;
- `C:\QtisVision\bin\QtisVisionPanel.exe.WebView2`;
- `C:\QtisVision\bin\VisionProDependencies`.

Questa regola mantiene mapping I/O, encoder, quote macchina, configurazioni
OPC, parametri AI e cache runtime del PC. I file lingua distribuiti con la
release restano software-owned: vengono confrontati, aggiornati solo se
diversi e salvati in backup prima della scrittura.

Il contratto persistente e' composto da:

```text
C:\ProgramData\Pulsar\QtisVision\application-files.json
C:\ProgramData\Pulsar\QtisVision\install-state.json
C:\ProgramData\Pulsar\QtisVision\pending-core-update.json
```

Il journal viene scritto prima della prima modifica. Se il processo viene
interrotto, la successiva esecuzione ripristina i file cambiati e lo stato
installer prima di costruire un nuovo piano. Il downgrade resta bloccato.
Quando il manifest precedente non esiste ancora, i file sconosciuti vengono
preservati; dalla seconda installazione `r14` anche le rimozioni sono basate su
un inventario affidabile.

`App.config` usa il probing path `VisionProDependencies`. Nella Release di
sviluppo questo nome e' una junction verso il runtime Cognex e non deve essere
trasformato in una copia privata di centinaia di DLL. Il builder verifica che
la sorgente esponga `Cognex.Vision.Startup.Net.dll`, la esclude dallo ZIP e il
bootstrapper ricrea sul target:

```text
C:\QtisVision\bin\VisionProDependencies
    -> %VPRO_ROOT%\bin
```

Anche `Cognex.Vision.Startup.Net.dll` deve essere risolta attraverso
`VisionProDependencies`. Non deve esistere una copia privata accanto all'EXE:
il confronto con la baseline funzionante ha dimostrato che quella posizione
altera la risoluzione delle dipendenze native RBBT/ViDi EL e impedisce il
caricamento dei VPP contenenti `CogClassifyTool`.

Da `3.1.0.1` il classificatore HMI usa un processo separato:

```text
C:\QtisVision\bin\AiRuntime\QtisVisionPanel.OnnxWorker.exe
```

Le DLL Microsoft ONNX devono esistere esclusivamente in `AiRuntime`. Il builder
`r6` verifica la presenza del worker e interrompe la generazione se trova
`Microsoft.ML.OnnxRuntime.dll`, `onnxruntime.dll` o
`onnxruntime_providers_shared.dll` direttamente accanto a `QtisVisionPanel.exe`.
Questa separazione evita il conflitto con la `onnxruntime.dll` usata da Cognex
ViDi EL durante il caricamento dei VPP Classify.

La junction viene verificata prima di completare l'installazione. Se VisionPro
non e' installato, la DLL sentinella manca o `mklink` fallisce, il nuovo `bin`
non viene accettato e scatta il rollback. Anche la selezione componenti
impone `visionpro` come dipendenza di `qtis-core`.

### Runtime

Non vengono acquisiti o modificati come parte della procedura:

- stato macchina in memoria;
- code prodotti;
- risultati ispezione;
- stato uscite fisiche.

L'installer non comanda I/O e rifiuta l'update se la HMI e' aperta.

## Configurazione porte camera Cognex GigE

Il componente `cognex-gige-network` esegue uno script PowerShell incorporato
nel bootstrapper. La sorgente macchina e' il `CameraConfig.xml` risolto dal
`Recipe_Folder` del `Config.xml` attivo, con fallback a
`C:\QtisVision\Programs\CameraConfig.xml`.

Sono accettati come attributi opzionali dei nodi `Camera`:

- `adapterName`, `adapterGuid` oppure `adapterMac`;
- `hostIpAddress`;
- `cameraIpAddress`;
- `prefixLength` oppure `subnetMask`.

Il formato esistente con soli ruolo e ID resta valido. Senza metadati
espliciti, una scheda viene selezionata soltanto se e' fisica, non wireless o
virtuale, usa IPv4 privata statica, non ha default gateway, espone il binding
Cognex eBUS e negozia almeno 1 Gbps quando connessa. In caso di zero o piu
match non sicuri lo script restituisce `NoCandidates` senza modifiche.

Prima dell'applicazione viene serializzato un backup di:

- IPv4 e prefissi;
- binding di protocollo;
- proprieta avanzate OEM;
- power management;
- nome, GUID, MAC e velocita della porta.

Il profilo applicato segue la guida hardware Cognex GigE: EEE Off, Interrupt
Moderation On/Extreme, Jumbo Packet almeno 9000, buffer RX/TX e RSS al massimo,
IPv4 ed eBUS come soli binding della porta dedicata e risparmio energetico
disabilitato quando esposto dal driver. Un default gateway blocca sempre la
modifica, anche con associazione esplicita. Host e camera IP devono essere
privati, distinti e nella stessa subnet.

Per scelta di sicurezza il bootstrapper non disabilita Windows Firewall e non
cambia il piano energia globale. `Packet Size` e `Latency` sono parametri
VisionPro/AcqFifo e restano nel VPP. Backup e risultati sono conservati sotto:

```text
C:\ProgramData\Pulsar\QtisVision\Installer\NetworkBackups
C:\ProgramData\Pulsar\QtisVision\Installer\NetworkResults
```

## Python e ONNX

Il target non deve avere Python preinstallato. Il supporto crea:

```text
C:\QtisVision\AI\Python\python.exe
```

e installa i wheel con `--no-index`. Al termine esegue:

1. import di PyTorch, Pillow, NumPy, ONNX e ONNX Script;
2. export di un piccolo modello con `torch.onnx.export`;
3. validazione del modello con `onnx.checker`.

Questo controllo copre anche il problema `ModuleNotFoundError: onnxscript`.

Al termine dello smoke test il bootstrapper riallinea anche un ambiente gia
presente e registra a livello macchina:

```text
PATH += C:\QtisVision\AI\Python
PATH += C:\QtisVision\AI\Python\Scripts
QTIS_PYTHON_ROOT=C:\QtisVision\AI\Python
QTIS_PYTHON_EXE=C:\QtisVision\AI\Python\python.exe
```

L'operazione e' idempotente e non imposta `PYTHONHOME`: Cognex e altri prodotti
possono continuare a usare i propri runtime Python. Il processo setup riceve
subito le stesse variabili e Windows viene notificato tramite
`WM_SETTINGCHANGE`.

## Avvio HMI ritardato

`qtis-autostart` estrae lo script incorporato in:

```text
C:\ProgramData\Pulsar\QtisVision\Startup\Start-QtisVisionPanel.ps1
```

e crea un collegamento PowerShell nascosto nello Startup comune Windows. Il
launcher non modifica il runtime macchina e non comanda I/O. Esegue questa
sequenza:

1. rifiuta il doppio avvio se `QtisVisionPanel` e' gia in esecuzione;
2. applica il ritardo iniziale, default `45 s`;
3. attende fino a `120 s` i servizi Windows configurati, default `MySQL*`;
4. attende lo stato `Up` delle NIC riportate dall'ultima configurazione Cognex;
5. ricontrolla l'istanza e avvia l'EXE con `C:\QtisVision\bin` come working directory.

La politica e' in
`C:\ProgramData\Pulsar\QtisVision\Startup\startup-settings.json`. I campi
numerici sono limitati a intervalli sicuri; un JSON non valido viene archiviato
prima di ripristinare i default. Per default un timeout non blocca l'avvio HMI:
la diagnostica applicativa resta la sorgente autorevole sullo stato di camere,
DB e servizi. Il log ruota a 2 MB sotto `Startup\Logs`.

## Identita PC e sensori

`--inventory` legge dal registro SMBIOS produttore, modello, baseboard, BIOS e
CPU. Registra inoltre `modelIsGeneric` quando il firmware espone stringhe come
`Default string` o `To be filled by O.E.M.`. Il profilo commerciale
`AVS-532-EH1Q1 / i7` non viene hardcodato: deve essere confermato dal BIOS o dal
report di commissioning.

Le card termiche HMI usano `LibreHardwareMonitorLib` integrato nel pacchetto
applicativo per CPU, motherboard/Super I/O, memoria e storage. La modalita'
`Auto` mantiene il fallback `MSFT_StorageReliabilityCounter` per i dischi e
`MSAcpi_ThermalZoneTemperature` per la zona ACPI quando il provider o una
famiglia di sensori non e' disponibile. Il provider non richiede un installer
separato: DLL e dipendenze sono inventariate dentro `ApplicationBin`.

Questi provider non garantiscono una temperatura RAM e possono non attraversare
controller RAID o driver NVMe OEM. L'assenza del sensore resta esplicita; un
valore stimato non viene presentato come misura industriale. Il file
`ThirdPartyNotices\LibreHardwareMonitor.md` accompagna i binari nel media.

## Sicurezza supply-chain

Il builder:

- accetta solo installer EXE/MSI con firma Authenticode valida;
- blocca la build se l'hash del pacchetto VisionPro non coincide con quello
  approvato;
- esclude il MySQL 8.0.18 con `HashMismatch`;
- tratta la patch KB5077181 come unica eccezione Cognex esplicita: verifica
  prima lo SHA-256 dello ZIP approvato e poi quello dell'EXE estratto;
- registra SHA-256 e dimensione di ogni payload;
- verifica nuovamente il media dopo la generazione.

La patch viene installata solo dopo il componente `visionpro`. Il bootstrapper
la considera gia' presente quando trova:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\
{17F14A9A-E830-42B8-9E1C-049A4CA78758}
DisplayVersion >= 1.0
```

La dipendenza viene validata prima dell'installazione: se VisionPro non e'
presente e non e' selezionato, la patch non puo' essere avviata.

Gli installer UltraVNC e Notepad++ devono avere sia firma Authenticode valida
sia SHA-256 uguale al valore approvato nel builder:

```text
UltraVNC 1.6.4.0 x64
434853E116EEB132CFDF47FDF6BA489D30C67A38147AFF6B9BD0EC2F4D0F1919

Notepad++ 8.9.3 x64
8DB87A458551371E911BEA5243840C783B5E7D41B7F867A2A6F74971881FB1E4
```

### Provisioning UltraVNC

Il componente `ultravnc` usa una fase dedicata, separata dal normale lancio
EXE:

1. se la versione e' mancante o precedente a `1.6.4.0`, esegue in silent mode
   i componenti `UltraVNC_Server` e `UltraVNC_Viewer` con i task
   `installservice,startservice`;
2. se una versione compatibile e' gia presente, evita il downgrade;
3. arresta in modo controllato `uvnc_service`;
4. genera il valore cifrato con `setpasswd.exe`, senza riportare argomenti o
   output sensibili nel log;
5. aggiorna atomicamente `%ProgramData%\UltraVNC\ultravnc.ini` e, se presente,
   il file legacy nella cartella applicativa;
6. imposta il servizio su avvio automatico, lo riavvia e attende `RUNNING`.

La password non e' presente nel manifest o nei file di testo del media. Poiche'
la credenziale e' comunque parte del profilo automatico, il supporto resta
materiale riservato. In rete reale TCP 5900 deve essere limitato a VLAN o VPN
di assistenza.

### Rilevamento DAQNavi OEM

Il supporto `r4` riconosce i PC forniti con DAQNavi gia installato senza
dipendere dalla cartella versione `4.0.0.0`. La prova primaria usa:

```text
HKLM\SOFTWARE\Advantech\Components\cmp_daqnavi_runtime\
Automation.BDaq4.dll
```

Il marker e' valido solo se:

- il valore `Path` punta a un file esistente;
- `FileVersion` e' almeno `4.0.0.0`.

Se il marker non e' presente, il bootstrapper cerca
`Automation.BDaq4.dll` nelle root standard DAQNavi sotto `%SystemDrive%`,
`%ProgramFiles%` e `%ProgramFiles(x86)%`. Una prova valida classifica il
componente come `Gia installato`, lo deseleziona nella UI e il motore lo salta
anche se fosse rimasto selezionato.

Questo controllo certifica il runtime API usato dalla HMI, non la presenza di
ogni driver hardware. PCIE-1756 e PCIE-1884 devono essere verificati in
DAQNavi Navigator sul PC macchina.

## Comando canonico

```powershell
powershell -ExecutionPolicy Bypass -File .\Installer\Build-QtisInstallerMedia.ps1
```

Il nome del supporto include anche `-r<MediaRevision>`. La revisione media puo'
essere incrementata senza cambiare la versione della HMI quando cambia solo la
composizione dei prerequisiti, come per l'introduzione della patch KB5077181.

Il parametro `-RedactSecrets` svuota i nodi XML con nomi password, token,
secret o private key. Anche con questa opzione il seed puo' contenere seriali,
IP e quote macchina, quindi non diventa un template universale.

## Media corrente HMI 3.1.5.4

La baseline corrente usa i supporti completo e UpdateOnly `3.1.5.4-r18`.
Consultare `../../Installer/README.md` e
`installer-release-verification-2026-09-18.md` per rigenerazione, verifiche e
limiti. L'updater separato contiene soltanto ApplicationBin e preserva anche
Language runtime; i cataloghi Localization dell'app completano in memoria le
chiavi mancanti. Il comportamento del seed del supporto completo resta distinto.

## Media storico HMI 3.1.1.4 verificato

Il `2026-08-31` e' stato generato e validato il supporto:

```text
D:\QtisInstallerOutput\QtisVisionPanel_3.1.1.4-r16
```

Esito della verifica:

- `build-summary.json`: `validation=PASSED`;
- `installer-validation.json`: `isValid=true`, zero errori e undici warning
  operativi attesi;
- 30 payload verificati tramite dimensione e SHA-256;
- dimensione builder circa `7,23 GiB`;
- hash `QtisVisionSetup.exe`:
  `5B8393E0C972512A0095EFDBF8BD1312837AEB42E10F15AD59945E9530660C3E`;
- hash Release HMI:
  `C728C4A1C42030B51CA375F074A507EB641A4CB1811C802F6A445F6048B6EEF2`;
- `ApplicationBin-3.1.1.4.zip` include il provider temperature, le dipendenze
  transitive e il notice MPL-2.0;
- worker ONNX presente in `AiRuntime`; zero DLL Microsoft ONNX nella root;
- cache runtime WebView2 esclusa dallo ZIP applicativo;
- `VisionProDependencies` esclusa dallo ZIP e ricreata come junction sul
  target;
- seed runtime completo di `cfg`, `Programs`, `Language` e `AI`;
- VisionPro 9.25, patch KB5077181, DAQNavi, MySQL, HeidiSQL, UltraVNC,
  Notepad++ e ambiente Python AI inclusi secondo il manifest `r16`;
- componente rete Cognex GigE incluso, selezionato di default e verificato in
  audit e `-Apply` sul PC sviluppo con esito `NoCandidates` e zero modifiche;
- PATH/variabili Python e launcher ritardato inclusi; inventario SMBIOS
  verificato sul PC sviluppo.

Gli undici warning non invalidano il media: ricordano i controlli di
commissioning, la gestione riservata del seed macchina, la junction Cognex,
l'eventuale generazione offline DAQNavi, la configurazione sito di MySQL e
UltraVNC, il comportamento differenziale e il commissioning della rete camera.

## Media storico HMI 3.0.8.4

Il `2026-07-27` e' stato generato il supporto:

```text
D:\QtisInstallerOutput\QtisVisionPanel_3.1.0.1-r6
```

Esito della verifica:

- `build-summary.json`: `validation=PASSED`;
- `installer-validation.json`: `isValid=true`;
- 30 payload verificati tramite dimensione e SHA-256;
- dimensione totale `7,25 GiB` (`7.780.492.018` byte);
- bootstrapper e HMI con versione `3.0.8.4`;
- hash EXE HMI nel pacchetto verificato uguale alla Release x64; il valore
  autorevole resta nel manifest del media;
- VisionPro 9.25, KB5077181, DAQNavi, MySQL, HeidiSQL, UltraVNC,
  Notepad++ e Python AI presenti;
- seed macchina completo di `cfg`, `Programs`, `Language` e modelli AI;
- `VisionProDependencies` esclusa dallo ZIP applicativo e demandata alla
  junction verificata sul target;
- inventario bootstrapper: DAQNavi e UltraVNC rilevati `Installed`;
  Notepad++ 8.8.3 rilevato `InstalledOlder` rispetto al payload 8.9.3;
- SHA-256 `QtisVisionSetup.exe`:
  `C428187BB48F3287B7E800FD8AED031227033B5806608B43821FEA112F8162E2`.

Il media `r6` accompagna la release `3.1.0.1`: oltre ai prerequisiti della
baseline precedente include il worker ONNX isolato e verifica che il runtime
Microsoft non possa interferire con Cognex ViDi EL nella directory principale.

## Limite DAQNavi

`XNavi.exe` e' il gestore ufficiale Advantech, ma non contiene necessariamente
tutti i driver. Se il runtime compatibile e' gia presente, il bootstrapper lo
mantiene e non apre XNavi. Prima di consegnare un supporto per un sito senza
rete e senza DAQNavi:

1. aprire XNavi su un PC con rete;
2. selezionare le versioni driver usate da PCIE-1756 e PCIE-1884;
3. generare il pacchetto offline;
4. aggiungerlo sotto `D:\QtisInstallerPayloads\Advantech_DAQNavi`;
5. estendere il manifest per lanciarne il setup locale.

Fino a quel momento il media e' offline per HMI, VisionPro, MySQL e Python, ma
DAQNavi puo' richiedere accesso alla rete.

## Inventario non interattivo

Il comando:

```powershell
.\QtisVisionSetup.exe --inventory
```

scrive `installer-inventory.json` accanto all'eseguibile con stato e versione
minima di ogni componente, modalita update della HMI e identita SMBIOS del PC.
Serve per raccogliere lo stato prima del commissioning; non modifica il
sistema.

## Rollback

Il rollback automatico copre i file applicativi aggiunti, aggiornati o rimossi
dal piano differenziale. I file software-owned sovrascritti dal runtime seed
sono copiati nel backup prima dell'aggiornamento. Per un rollback manuale:

1. chiudere la HMI;
2. riaprire prima lo stesso installer: se trova
   `pending-core-update.json`, esegue il recovery automatico;
3. se serve un intervento manuale, ripristinare i file presenti in
   `InstallerBackups\<timestamp>\ApplicationChanged` nelle stesse posizioni;
4. ripristinare i file lingua da `RuntimeOverwritten` solo se necessario;
5. per la rete usare il JSON in `NetworkBackups` come stato autorevole e
   ripristinare le proprieta con gli strumenti Windows/Cognex;
6. riavviare e verificare versione, job e comunicazioni.
