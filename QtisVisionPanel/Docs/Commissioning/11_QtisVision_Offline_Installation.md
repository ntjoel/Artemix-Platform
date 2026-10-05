# Installazione offline Qtis Vision Panel

## Scopo

Questa procedura guida il tecnico nell'installazione di un nuovo PC macchina o
nell'aggiornamento di una HMI esistente.

## Prima di iniziare

| ID | Controllo | Esito richiesto |
|---|---|---|
| 1 | Backup macchina | Copia verificata di `C:\QtisVision` e database. |
| 2 | Stato produzione | Macchina ferma in condizione sicura. |
| 3 | HMI | `QtisVisionPanel.exe` chiuso. |
| 4 | Account Windows | Tecnico con diritti Administrator. |
| 5 | Licenze | Licenza Cognex disponibile o gia attiva. |
| 6 | Supporto | Intera cartella installer copiata su disco locale. |

Non eseguire l'installer direttamente da una cartella OneDrive in
sincronizzazione o da una chiavetta lenta.

## Installazione

1. Avviare `QtisVisionSetup.exe`.
2. Controllare la versione indicata nella testata.
3. Premere `Verifica`.
4. Non proseguire se compare un errore SHA-256 o payload mancante.
5. Lasciare selezionati i componenti mancanti sul PC.
6. Lasciare selezionato `Ottimizzazione rete camere Cognex GigE` solo se si
   desidera configurare le porte camera durante questo commissioning.
7. Lasciare selezionato `Avvio automatico HMI ritardato` sul PC macchina.
8. Premere `Installa / aggiorna`.
9. Completare le finestre vendor nell'ordine presentato.
10. Attendere il messaggio finale del bootstrapper.
11. Leggere il report rete Cognex e riavviare Windows.

I componenti opzionali indicati come `Gia installato` sono deselezionati
automaticamente e non vengono reinstallati. UltraVNC resta invece selezionato:
una versione compatibile non viene sostituita, ma servizio e configurazione di
accesso vengono verificati e riallineati. Per raccogliere lo stato del PC prima
di aprire la UI:

```powershell
.\QtisVisionSetup.exe --inventory
```

Il risultato viene scritto in `installer-inventory.json`.

Quando la HMI e' gia installata, la riga applicazione indica chiaramente
`Aggiorna`, `Ripara` o `Downgrade bloccato`. L'update confronta gli SHA-256 e
tocca solo i file applicativi cambiati. Mapping macchina, XML, ricette, VPP,
OPC, modelli AI e cache WebView2 restano protetti.

## Aggiornamento di una HMI esistente

Usare la cartella autonoma `QtisVisionPanel_Update_3.1.5.4-r18` e mantenere
insieme `QtisVisionUpdate.exe`, manifest e cartella `Payloads`.

1. Eseguire il backup di configurazioni, ricette e database; fermare la macchina
   e chiudere la HMI.
2. Copiare l'intera cartella del supporto sul disco locale.
3. Avviare `QtisVisionUpdate.exe` come amministratore e premere `Verifica`.
4. Confermare la versione e avviare l'aggiornamento; conservare il report finale.
5. Riavviare la HMI, verificare versione `3.1.5.4`, caricamento ricetta,
   comunicazioni e testi personalizzati.
6. Collaudare un campione GOOD, uno NOK e uno sotto soglia AI per ogni vista;
   verificare la correlazione delle immagini `_A.bmp` e `_Z.jpg`.

Il supporto UpdateOnly richiede una HMI gia' installata e include solo software
applicativo. Non installa prerequisiti e non sostituisce configurazioni macchina,
ricette, VPP, Language runtime o modelli AI. Per un PC nuovo usare il supporto
completo. La release comprende l'aggiunta idempotente DB
`cfg_view_classification.MinimumScore` (default `0`), introdotta in `3.1.5.3`:
verificare al primo avvio l'assenza di errori di inizializzazione database.

## Parametri vendor

### VisionPro

| ID | Controllo | Azione |
|---|---|---|
| V1 | Versione | Installare x64 9.25 o versione compatibile validata. |
| V2 | Driver camera | Installare i driver richiesti dalle camere del progetto. |
| V3 | Licenza | Attivare con la procedura Cognex prevista per il sito. |
| V4 | QuickBuild | Aprire un VPP campione senza errori di assembly. |
| V5 | Patch Windows | Completare `VisionPro Windows Update KB5077181 Patch` dopo VisionPro 9.25. |
| V6 | Verifica patch | In App e funzionalita deve comparire la patch Cognex versione `1.0` o successiva. |
| V7 | Dipendenze HMI | Verificare che `C:\QtisVision\bin\VisionProDependencies` sia una junction e apra la cartella `bin` Cognex. |

Il bootstrapper salta automaticamente V5 quando rileva la patch nella chiave:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\
{17F14A9A-E830-42B8-9E1C-049A4CA78758}
```

### Rete camere Cognex GigE

Per un'associazione automatica deterministica, aggiungere nel
`CameraConfig.xml` macchina gli attributi della porta dedicata. Il formato
storico resta valido e non richiede migrazione.

```xml
<Camera type="Top"
        id="0"
        adapterName="Camera Top"
        adapterMac="001122AABBCC"
        hostIpAddress="192.168.21.203"
        cameraIpAddress="192.168.21.2"
        prefixLength="24" />
```

Il nome, il GUID o il MAC identificano la NIC; IP host, IP camera e prefisso
definiscono la subnet. Una porta camera deve essere dedicata e non deve avere
un default gateway.

| ID | Controllo | Esito richiesto |
|---|---|---|
| G1 | Report installer | Ultimo JSON in `C:\ProgramData\Pulsar\QtisVision\Installer\NetworkResults` con stato generale `Configured`; le singole porte possono risultare `ConfiguredWithWarnings`. |
| G2 | Backup | JSON presente in `C:\ProgramData\Pulsar\QtisVision\Installer\NetworkBackups`. |
| G3 | Associazione | Nome/GUID/MAC del report coincide con la porta fisicamente cablata alla camera prevista. |
| G4 | Sicurezza rete | Nessun default gateway sulla NIC camera; LAN aziendale e Wi-Fi non risultano modificati. |
| G5 | Binding | IPv4 e Cognex eBUS attivi; binding non necessari disabilitati sulla sola porta dedicata. |
| G6 | Velocita | Link negoziato a `1 Gbps` o superiore. |
| G7 | Driver | Energy Efficient Ethernet Off, Interrupt Moderation On/Extreme, Jumbo Packet almeno 9000, buffer e RSS al massimo quando disponibili. |
| G8 | Risparmio energia | Sospensione selettiva disabilitata; verificare manualmente anche `Consenti al computer di spegnere il dispositivo` se il driver non espone l'opzione a PowerShell. |
| G9 | VisionPro | Nel VPP, `Packet Size >= 8000` e `Latency` uguale a `0` o `1`. |
| G10 | Acquisizione | Almeno 100 trigger consecutivi per camera senza frame persi, timeout o recovery. |

`NoCandidates` non e' un errore distruttivo: indica che il bootstrapper non ha
trovato un'associazione sicura e non ha modificato alcuna scheda. Completare in
questo caso IP e ottimizzazione con `Cognex GigE Vision Configuration Tool`,
poi riportare gli identificatori nel `CameraConfig.xml` per le installazioni
successive. Firewall e piano energia globale restano verifiche manuali del
sito.

### Advantech

| ID | Controllo | Azione |
|---|---|---|
| A1 | DAQNavi preinstallato | Se compare `Gia installato`, mantenere il runtime OEM presente. |
| A2 | Runtime API | Verificare `Automation.BDaq4.dll` versione `4.0.0.0` o successiva. |
| A3 | Driver schede | In DAQNavi Navigator verificare i driver delle schede PCIE-1756 e PCIE-1884 effettivamente montate. |
| A4 | Device Manager | Verificare le schede senza errori o dispositivi sconosciuti. |
| A5 | Solo Navigator | XNavi/Navigator senza runtime Automation.BDaq non e' sufficiente; installare DAQNavi. |
| A6 | Offline | Se il PC non ha rete e manca DAQNavi, usare il cache package generato da XNavi. |

### MySQL

| ID | Controllo | Azione |
|---|---|---|
| M1 | Server | Installare MySQL Server 8.0 x64 come servizio Windows. |
| M2 | Credenziali | Definire l'account previsto dal `Config.xml` del sito. |
| M3 | Rete | Limitare l'ascolto a localhost quando il DB e' locale. |
| M4 | Avvio | Verificare che il servizio riparta dopo reboot. |

### Python AI

| ID | Controllo | Esito |
|---|---|---|
| P1 | Percorso | `C:\QtisVision\AI\Python\python.exe` esiste. |
| P2 | Moduli | PyTorch, ONNX, ONNX Script, Pillow e NumPy importati. |
| P3 | Export | Nel log compare `AI_ENVIRONMENT_OK`. |
| P4 | HMI | Il check moduli Python non segnala `onnxscript` mancante. |
| P5 | PATH macchina | `where.exe python` include `C:\QtisVision\AI\Python\python.exe` in una nuova console. |
| P6 | Variabili Qtis | `QTIS_PYTHON_ROOT` e `QTIS_PYTHON_EXE` puntano all'ambiente dedicato. |
| P7 | Isolamento | `PYTHONHOME` non viene creato dall'installer. |

### Avvio automatico HMI

Il setup crea un collegamento nello Startup comune e non nel profilo del solo
tecnico. La macchina deve arrivare automaticamente alla HMI dopo il login
Windows previsto dal sito.

| ID | Controllo | Esito richiesto |
|---|---|---|
| S1 | Collegamento | `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\Qtis Vision Panel (Delayed).lnk` presente. |
| S2 | Ritardo | `initialDelaySeconds=45` salvo valore specifico validato per la macchina. |
| S3 | Dipendenze | `serviceNamePatterns` contiene almeno `MySQL*` quando il DB e' locale. |
| S4 | Porte camera | Il launcher usa l'ultimo report Cognex e attende che le NIC configurate risultino `Up`. |
| S5 | Timeout | `launchWhenDependenciesTimeout=true` mantiene l'avvio HMI non bloccante; la HMI mostra poi la diagnostica reale. |
| S6 | Istanza unica | Un secondo lancio scrive `STARTUP_SKIPPED_ALREADY_RUNNING` e non apre una seconda HMI. |
| S7 | Log | `C:\ProgramData\Pulsar\QtisVision\Startup\Logs\startup.log` contiene `STARTUP_LAUNCHED`. |

Impostazioni modificabili dal tecnico:

```text
C:\ProgramData\Pulsar\QtisVision\Startup\startup-settings.json
```

Per attendere altri servizi Windows, aggiungere i relativi nomi o pattern a
`serviceNamePatterns`; non inserire processi che possono restare legittimamente
fermi. Il launcher limita ritardi e timeout a 600 secondi.

### Identita hardware e temperature

`--inventory` registra produttore, modello, baseboard, BIOS e CPU da SMBIOS.
Per il riferimento `AVS-532-EH1Q1 / i7` verificare che CPU, RAM e NIC
corrispondano al capitolato, anche se il BIOS espone un nome prodotto generico.

```powershell
Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer, Model, TotalPhysicalMemory
Get-CimInstance Win32_BaseBoard | Select-Object Manufacturer, Product
Get-CimInstance Win32_Processor | Select-Object Name
```

Le temperature visualizzate dalla HMI non derivano dal nome PC. Con la modalita'
consigliata `Auto`, l'applicazione prova prima il provider integrato
`LibreHardwareMonitorLib` per CPU, scheda madre/Super I/O, memoria e storage;
usa poi i provider WMI seguenti come fallback per i gruppi non disponibili:

- SSD/dischi: `MSFT_StorageReliabilityCounter`;
- zona termica ACPI: `MSAcpi_ThermalZoneTemperature`.

```powershell
Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_StorageReliabilityCounter | Select-Object DeviceId, Temperature, TemperatureMax
Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature | Select-Object InstanceName, CurrentTemperature
```

Il provider integrato non e' un programma da installare: verificare in
`C:\QtisVision\bin` la presenza di `LibreHardwareMonitorLib.dll`,
`DiskInfoToolkit.dll`, `HidSharp.dll` e `RAMSPDToolkit-NDD.dll`. La HMI viene
eseguita elevata; senza privilegi amministrativi alcune CPU Intel espongono i
sensori ma non restituiscono il valore.

In `System Preferences -> Diagnostics thresholds` usare:

- `Auto`: provider integrato con fallback WMI, scelta raccomandata;
- `LibreHardwareMonitor`: solo provider integrato, utile per diagnosi;
- `WindowsWmi`: comportamento legacy senza accesso hardware diretto.

Se provider e comandi WMI non restituiscono sensori, `Sensor not available` e'
il risultato corretto. Su controller RAID, NVMe e DDR5 industriali puo' servire
un provider OEM o SMART supportato; non usare valori stimati come protezione
termica macchina. Nei log verificare una sola transizione
`SYSTEM_DIAGNOSTICS_TEMPERATURE_PROVIDER_ACTIVE` oppure
`SYSTEM_DIAGNOSTICS_TEMPERATURE_PROVIDER_FALLBACK`, non messaggi ripetuti a ogni
refresh.

### UltraVNC

| ID | Controllo | Esito |
|---|---|---|
| U1 | Versione | `1.6.4.0` o successiva rilevata dal bootstrapper. |
| U2 | Servizio | `uvnc_service` presente, avvio `Automatico`, stato `In esecuzione`. |
| U3 | Configurazione | `%ProgramData%\UltraVNC\ultravnc.ini` contiene un valore `passwd` non vuoto. Non riportare il valore cifrato nei report. |
| U4 | Accesso | Connessione da un PC di assistenza autorizzato con la credenziale custodita dal sito. |
| U5 | Rete | TCP 5900 raggiungibile solo dalla VLAN macchina, rete assistenza o VPN prevista. |
| U6 | Riavvio | Dopo reboot il servizio riparte e accetta nuovamente la connessione. |

Il bootstrapper non registra la password in chiaro nei log. Se il sito impone
credenziali univoche, cambiarla dopo l'installazione e registrarla nel gestore
password aziendale.

### Tool tecnici

| ID | Controllo | Esito |
|---|---|---|
| T1 | Notepad++ | Versione `8.9.3` o successiva disponibile. |
| T2 | File XML | Apertura di un XML di configurazione senza associazioni obbligatorie o modifiche automatiche. |
| T3 | Log | Apertura di un log HMI di grandi dimensioni senza bloccare la HMI. |

## Collaudo dopo reboot

| ID | Prova | Esito richiesto |
|---|---|---|
| C1 | Versione HMI | Coincide con quella del supporto. |
| C2 | Ricetta | La ricetta attiva e i VPP vengono caricati. |
| C3 | Lingua | Testi visualizzati senza chiavi mancanti. |
| C4 | DB | Connessione e inizializzazione tabelle riuscite. |
| C5 | Camere | Tutti i job previsti sono presenti e acquisiscono. |
| C6 | I/O | Mapping verificato in diagnostica, senza forzare uscite in produzione. |
| C7 | Encoder | Conteggio, direzione e velocita coerenti. |
| C8 | OPC UA | Stato coerente con `Enabled` e `Required`. |
| C9 | AI | Se disabilitata, nessun costo nel ciclo; se abilitata, modello valido. |
| C10 | Ciclo | Prova con almeno 20 prodotti e controllo scarto fisico. |
| C11 | Config macchina | `bin\cfg\machine_runtime_config.xml` conserva mapping, encoder e quote precedenti. |
| C12 | Patch VisionPro | KB5077181 versione `1.0` o successiva risulta installata. |
| C13 | Probing Cognex | `bin\VisionProDependencies\Cognex.Vision.Startup.Net.dll` esiste e la HMI carica il VPP senza errori assembly. |
| C14 | UltraVNC | Servizio automatico in `RUNNING` e prova remota riuscita dalla sola rete autorizzata. |
| C15 | Notepad++ | Versione 8.9.3 o successiva avviabile dal tecnico. |
| C16 | Update differenziale | `install-state.json` riporta modalita e conteggi file aggiunti/aggiornati/rimossi; `application-files.json` e' presente. |
| C17 | Rete camere | Report Cognex GigE controllato e ogni camera supera la prova G10. |
| C18 | Python ambiente | Nuova console vede `QTIS_PYTHON_EXE` e il Python Qtis nel PATH. |
| C19 | Startup ritardato | Dopo reboot la HMI parte una sola volta; il log riporta dipendenze e PID. |
| C20 | Inventario PC | `installer-inventory.json` contiene identita SMBIOS coerente oppure segnala esplicitamente un modello generico. |
| C21 | Temperature hardware | PC Diagnostics mostra i sensori esposti oppure `Sensor not available`; nessun errore ripetuto e nessun arresto del ciclo macchina. |

## Ripristino

Se la nuova HMI non parte:

1. chiudere ogni processo Qtis;
2. aprire `C:\QtisVision\InstallerBackups`;
3. scegliere il timestamp precedente;
4. consultare prima il log installer: un update interrotto viene ripristinato
   automaticamente al successivo avvio grazie a `pending-core-update.json`;
5. per un ripristino manuale, ricopiare i file presenti in
   `ApplicationChanged` nelle rispettive posizioni sotto `C:\QtisVision\bin`;
6. rimuovere solo i file elencati come aggiunti nello stato dell'update;
7. raccogliere log HMI, journal e log installer prima di una nuova prova.

Log installer:

```text
C:\ProgramData\Pulsar\QtisVision\Installer\Logs
```
