# PCIE I/O Unified Setup 2026-06-29

## Scope

Questa nota documenta la razionalizzazione della pagina `PCIE settings`.

La modifica e' solo UI / commissioning:

- non cambia il file `machine_runtime_config.xml`
- non cambia il runtime di trigger, encoder, scarto o MultiShot
- non cambia DB, ricette o `Config.xml`
- non sposta dati macchina dentro la ricetta

## Backup Pre-Modifica

Prima della modifica sono stati creati e verificati due backup canonici della baseline sorgente/operativa:

| Area | Percorso |
|---|---|
| Locale | `D:\Pulsar\Backups\QtisVisionPanel\QtisVisionPanel_source_pre_pcie_unified_20260629_111149` |
| OneDrive condivisa | `C:\Users\ntiegounj\OneDrive - Pulsar Engineering Srl\Pulsar Engineering\Quatis Project\Vision\lastRelease_backups\QtisVisionPanel_shared_source_pre_pcie_unified_20260629_111149` |

I backup usano le stesse esclusioni della sincronizzazione baseline (`bin`, `obj`, `.vs`, cache, exe/dll, log), quindi salvano sorgente, XAML, modelli, script, localizzazione e documentazione senza rumore runtime.

## Differenza Tra Intervention Points E Machine Configuration

### Intervention Points

Gli `InterventionPoints` descrivono la sequenza macchina legata al prodotto.

Rispondono alla domanda:

> Quando il prodotto arriva in una certa quota, quale azione deve avvenire?

Campi principali:

| Campo | Significato |
|---|---|
| `PointCode` | nome logico del punto, per esempio `CAMERA_TRIGGER_TOP` |
| `ReferenceCode` | riferimento quota, oggi normalmente `PRODUCT_ZERO` |
| `BaseOffsetMm` | posizione nominale rispetto a `PRODUCT_ZERO` |
| `TrimOffsetMm` | correzione macchina applicata al punto |
| `EffectiveOffsetMm` | `BaseOffsetMm + TrimOffsetMm` |
| `ActionType` | azione: `TriggerCamera`, `Reject`, `OutputPulse`, ecc. |
| `SignalCode` | segnale logico da usare quando il punto scatta |
| `PulseMs` | durata impulso per azioni pulsate |

Questi dati restano macchina, non ricetta.

### Machine Configuration

La `Machine Configuration` descrive la parte fisica e i binding runtime.

Risponde alla domanda:

> Quel segnale logico su quale scheda, canale e polarita' fisica lavora?

Contiene:

| Sezione | Scopo |
|---|---|
| `MachineInputs` | ingressi fisici/logici, per esempio fotocellula prodotto |
| `MachineOutputs` | uscite fisiche/logiche, per esempio trigger camera o scarto |
| `EncoderTemplates` | scheda encoder, canale, counts/mm, zero e fotocellula |
| `RuntimeBindings` | segnali principali usati dal runtime |
| `AdditionalRuntimeBindings` | binding speciali non standard |
| `MachineMultiShotTrigger` | profili MultiShot macchina |
| `ConfigurationBackups` | copie ripristinabili del file macchina |

## Nuova Vista Unificata

La UI ora espone una sola pagina principale:

`PCIE settings -> Machine Setup`

La pagina raggruppa:

| Sezione | Funzione |
|---|---|
| Header operativo | `Save config`, `Load config`, `Create backup`, apertura file config |
| Encoder setup | riferimento encoder, velocita linea, pezzi/min, zero macchina, fotocellula e calibrazione tachimetro |
| Riepilogo runtime | mostra fotocellula, trigger, scarto, allarme, heartbeat e binding attivi |
| Machine intervention positions | spiega il riferimento `PRODUCT_ZERO` e lo stato live |
| Machine position sequence | cards ordinate per quota macchina |
| Intervention point to physical output | tabella in sola lettura punto -> segnale -> board -> canale -> polarita' |
| Camera MultiShot | configurazione MultiShot gia esistente |
| Intervention point editor | editor dettagliato dei punti |
| Complete machine configuration | sezione aperta di default con binding runtime, tabelle I/O modificabili, encoder e backup |

La vecchia tab `Machine Configuration` resta nel file XAML ma non viene mostrata nel tab principale. Le funzioni operative importanti sono state riportate nella nuova pagina.

### Tabelle Modificabili

Le tabelle `Machine inputs` e `Machine outputs` sono integrate nella sezione `Complete machine configuration`.
Sono le stesse collection usate dalla vista storica:

| Tabella | Collection runtime | Modifica consentita |
|---|---|---|
| `Machine inputs` | `MachineInputsView` | modifica celle, aggiunta riga, cancellazione riga |
| `Machine outputs` | `MachineOutputsView` | modifica celle, aggiunta riga, cancellazione riga |
| `Encoder and tracking` | `EncoderTemplates` | modifica dei parametri encoder/macchina |
| `Additional runtime bindings` | `AdditionalRuntimeBindings` | binding opzionali per segnali macchina non standard |

Per rendere effettive le modifiche occorre premere `Save config`; il salvataggio committa eventuali celle/riga in edit, scrive il file macchina attivo e riallinea il runtime.

I filtri rapidi `Only runtime signals`, `Only unassigned signals` e `Only real signals` sono disponibili nelle tab `Machine inputs` e `Machine outputs` della nuova sezione.

La tabella `Intervention point to physical output` non legge dal DataGrid filtrato, ma da `InterventionPoints` risolti contro le collection complete `MachineOutputs` e `MachineInputs`. Dalla release `3.0.1.5` la risoluzione:

- elimina spazi accidentali nei `SignalCode`
- preferisce righe reali con `Board` e `Channel` compilati quando ci sono duplicati
- accetta alias compatibili `Left/Side` e `Right/Rear` per le camere fisiche usate con nome runtime diverso

## Giro Segnali Runtime Attuale

Il flusso operativo resta quello gia validato:

1. Il runtime legge `RuntimeBindings.ProductPhotocellSignalCode`.
2. Il segnale logico viene risolto in `MachineInputs`.
3. L'ingresso fisico viene letto dalla scheda configurata.
4. Sul fronte valido della fotocellula viene creato `PRODUCT_ZERO`.
5. `MachineController` costruisce gli stati prodotto dagli `InterventionPoints` abilitati.
6. Ogni punto calcola il target encoder da `EffectiveOffsetMm`.
7. Quando il main encoder raggiunge il target, viene generato `InterventionPointReached`.
8. `DigitalIOViewModel.ExecuteInterventionPointAsync` risolve `ActionType` e `SignalCode`.
9. Il `SignalCode` viene risolto in `MachineOutputs`.
10. Il runtime scrive il canale fisico usando board, channel e polarita' del segnale.
11. Per `TriggerCamera` viene emesso l'impulso camera.
12. Per `Reject` viene emesso l'impulso scarto secondo `PulseMs` o binding scarto.
13. Per MultiShot, il profilo macchina genera piu impulsi sulla stessa uscita configurata.

## Perche La Vista E' Piu Sicura

Prima chi configurava la macchina doveva passare mentalmente tra due tab:

- tab punti intervento: quota e azione
- tab machine configuration: segnale, canale e binding

Ora la tabella `Intervention point to physical output` mostra nello stesso posto:

- punto
- azione
- quota macchina
- segnale logico
- board
- canale
- polarita'
- stato segnale reale/test

Questo riduce errori di commissioning come:

- punto camera collegato al `SignalCode` sbagliato
- uscita fisica non configurata
- canale DO diverso da quello cablato
- segnale test lasciato al posto di un segnale reale

## Note Di Compatibilita

- XML: nessuna nuova sezione.
- DB: nessuna modifica.
- Ricette: nessuna modifica.
- Runtime: nessuna modifica comportamentale.
- Localizzazione: nuove chiavi aggiunte tramite `scripts/UpdateRuntimeLanguageFiles.ps1`.
- Release `3.0.1.4`: la sezione completa si apre di default e le tabelle input/output mostrano anche `Signal type`.
