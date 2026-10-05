# CLAUDE.md — QtisVisionPanel

Leggere anche: `AGENTS.md` (regole baseline), `Documentation/MachineHardware/README.md`.

---

## Regola Documentazione (OBBLIGATORIA)

Dopo ogni modifica al codice — una singola riga o un refactoring completo — aggiornare
i documenti nella cartella `Documentation/MachineHardware/` prima di considerare il
task concluso.

Il hook in `.claude/settings.json` sincronizza automaticamente
`Documentation/MachineHardware/` → `bin/x64/Release/Documentation/MachineHardware/`
dopo ogni operazione Edit o Write. Non è necessario copiare file manualmente.

### Documento 1 — Changelog modifiche codice

File: `Documentation/MachineHardware/code-changes-log.md`

Aggiungere sempre una nuova sezione in testa con:

```
## [YYYY-MM-DD] <titolo breve>

**File modificati:**
- `Percorso/File.cs` — descrizione della modifica

**Motivo:** perché la modifica era necessaria

**Impatto:**
- Config.xml: nessuno / nuovi campi / campi rimossi
- Database: nessuno / nuove colonne / nuove tabelle
- API pubblica: nessuna / metodi aggiunti / firme cambiate
- Comportamento runtime: descrizione
```

### Documento 2 — Architettura moduli (solo se la modifica è architetturale)

File: `Documentation/MachineHardware/architecture-and-modules.md`

Aggiornare quando cambia:
- la firma pubblica di un'interfaccia
- il grafo delle dipendenze tra moduli
- la responsabilità di una classe (classe divisa, unita, spostata)
- l'aggiunta o rimozione di un servizio nel ServiceLocator

### Documento 3 — Archivio versioni (solo a rilascio)

File: `Documentation/MachineHardware/software-version-archive.md`

Aggiornare solo quando viene emessa una nuova versione ufficiale (incremento
AssemblyVersion + AssemblyFileVersion in `Properties/AssemblyInfo.cs`).

---

## Regole di Modifica Sicura

### Prima di modificare

1. Leggere il file da modificare con Read — mai editare codice non letto.
2. Verificare i consumer di ogni metodo pubblico modificato con Grep.
3. Per servizi registrati in ServiceLocator: `grep -r "ServiceLocator\." --include="*.cs"`.
4. Per `MainWindow.xaml.cs`: ogni modifica richiede review dell'impatto elevato.

### Pattern obbligatori

- Operazioni async: sempre `CancellationToken` come parametro finale.
- ObservableCollection modificata da background: `Application.Current.Dispatcher.BeginInvoke`.
- Log eccezioni: `_logger.Error(exception, "EVENT_CODE|key=value")` — mai `ex.Message` inline.
- `async void` vietato nei command handlers — usare `async Task` + `.SafeFireAndForget()`.
- Nessun `catch (Exception) { }` silenzioso.

### Aree ad alto impatto (attenzione speciale)

- `MainWindow.xaml.cs` — God Class, 3600+ righe
- `Services/MachineRuntimeService.cs` — semaforo, hold-reasons, recovery
- `Cls_Config/AsyncConfigManagerXml.cs` — scrittura atomica config
- `Database/Cls_InitializzeDb.cs` — schema DB
- `Cls_Vpro/ICognexJobManager.cs` — VisionPro lifecycle
- `Services/IntegratedAlarmCardService.cs` — allarmi duali MySQL+XML
- `DataManage/IToolBlockValidator.cs` — validazione ispezione per ruolo camera

---

## Routing Ispezione Top / Top3D

Regola fondamentale (implementata in `InspectionProcessor.cs` e `MainWindow.xaml.cs`):

- `CameraRole == "top"` → `GetDetailedTopValidationAsync` (Logo, PrintCentering, OpenFlaps, SurfaceCheck, ShapeTop)
- `CameraRole == "top3d"` → `GetDetailedTop3DValidationAsync` (ThreeDHeight, ThreeDWidth, ThreeDLength)

Il routing usa **il nome del job nel file VPP** (normalizzato da `CameraConfigurationHelper.NormalizeCameraType`),
**non** il campo `MachineType` in `Config.xml`. Non invertire questa logica.

---

## Definition of Done per ogni modifica

- [ ] File modificati letti prima dell'edit
- [ ] Nessuna regressione sulle aree ad alto impatto
- [ ] `code-changes-log.md` aggiornato
- [ ] `architecture-and-modules.md` aggiornato se architetturale
- [ ] Nessun `async void`, nessun catch silenzioso introdotto
