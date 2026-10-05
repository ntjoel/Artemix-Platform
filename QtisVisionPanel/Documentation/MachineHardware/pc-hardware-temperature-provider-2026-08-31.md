# PC Hardware Temperature Provider

## Scopo

La diagnostica PC legge le temperature realmente esposte dal computer senza
rendere questa funzione necessaria al ciclo macchina. Il servizio e' solo
diagnostico: non entra nel percorso di acquisizione, ispezione, encoder, I/O,
scarto o salvataggio del risultato.

## Provider

`SystemDiagnostics.HardwareTemperatureProvider` in `Config.xml` accetta:

| Valore | Comportamento |
|---|---|
| `Auto` | Usa LibreHardwareMonitor e completa i gruppi mancanti con i provider WMI legacy. Default raccomandato. |
| `LibreHardwareMonitor` | Usa solo il provider hardware integrato. Utile per commissioning e diagnosi. |
| `WindowsWmi` | Non apre LibreHardwareMonitor e mantiene il comportamento precedente. |

La libreria viene aperta una sola volta, protetta da lock e chiusa durante il
dispose del servizio. Le letture sono eseguite nel task di refresh diagnostico e
restano in cache per almeno cinque secondi. Dopo un errore di inizializzazione il
tentativo successivo avviene dopo cinque minuti, evitando rumore nei log.

## Configurazione

I campi sono in `AppConfig.SystemDiagnostics`:

- `TemperatureMonitoringEnabled`;
- `HardwareTemperatureProvider`;
- `HardwareTemperaturePollingSeconds`, range 5-300 s;
- `CpuTemperatureWarningC` / `CpuTemperatureCriticalC`;
- `DiskTemperatureWarningC` / `DiskTemperatureCriticalC`;
- `MemoryTemperatureWarningC` / `MemoryTemperatureCriticalC`;
- `MotherboardTemperatureWarningC` / `MotherboardTemperatureCriticalC`.

I file `Config.xml` precedenti restano compatibili: `XmlSerializer` assegna i
default del modello quando i nuovi elementi non sono presenti. Non sono stati
aggiunti campi ricetta o tabelle database.

## UI e severita

PC Diagnostics mostra quattro card: archivio DB, CPU, disco piu' caldo e
sistema/memoria. Ogni misura disponibile usa le soglie specifiche del proprio
gruppo. Un sensore assente resta `Sensor not available`; non equivale a zero e
non genera un arresto macchina.

System Preferences espone provider, polling e soglie CPU/motherboard insieme
alle soglie termiche esistenti. I testi sono presenti nei file lingua ENG/ITA.

## Log

- `SYSTEM_DIAGNOSTICS_TEMPERATURE_PROVIDER_ACTIVE`: provider inizializzato e
  numero di letture rappresentative disponibili;
- `SYSTEM_DIAGNOSTICS_TEMPERATURE_PROVIDER_FALLBACK`: provider non disponibile
  o nessuna lettura, con motivo sintetico.

La firma dell'ultimo stato viene memorizzata: lo stesso evento non viene scritto
a ogni refresh.

## Collaudo

1. Verificare che la HMI sia avviata come amministratore.
2. Impostare provider `Auto` e polling `10 s`.
3. Aprire PC Diagnostics e premere Refresh.
4. Controllare CPU, disco e sistema/memoria.
5. Verificare nel log la transizione ACTIVE o FALLBACK una sola volta.
6. Impostare temporaneamente `WindowsWmi`, salvare e verificare il fallback.
7. Ripristinare `Auto`.
8. Eseguire almeno 20 prodotti: tempi VisionPro, encoder, I/O e contatori devono
   restare invariati.
9. Riavviare la HMI e verificare la persistenza dei parametri.

Su alcuni BIOS, controller RAID, NVMe e moduli DDR5 il sensore puo' non essere
pubblicato. Questo e' un limite hardware/driver e non un errore di installazione.

## Distribuzione e licenza

Il NuGet `LibreHardwareMonitorLib` 0.9.6 e le dipendenze runtime sono copiati
nella Release x64 e quindi in `ApplicationBin` dell'installer differenziale. Il
file `ThirdPartyNotices\LibreHardwareMonitor.md` riporta progetto, versione,
licenza MPL-2.0 e riferimenti al sorgente.
