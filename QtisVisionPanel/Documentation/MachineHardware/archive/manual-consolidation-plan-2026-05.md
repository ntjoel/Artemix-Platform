# Piano di Consolidamento — QtisVisionPanel
## Hardening per Produzione Industriale 24/7

**Data analisi**: Maggio 2026  
**Versione applicazione analizzata**: master (commit b161ad8)  
**Criticità totali identificate**: 55  
**Verdetto**: ⚠️ NON PRONTO PER PRODUZIONE — interventi necessari prima del deploy

---

## Indice

1. [Executive Summary](#1-executive-summary)
2. [Fase 0 — Blocchi Critici Pre-Deploy (1–2 giorni)](#2-fase-0--blocchi-critici-pre-deploy)
3. [Fase 1 — Stabilità Hardware e Allarmi (1 settimana)](#3-fase-1--stabilità-hardware-e-allarmi)
4. [Fase 2 — Solidità Database e Configurazione (1 settimana)](#4-fase-2--solidità-database-e-configurazione)
5. [Fase 3 — Auto-Diagnosi e Health Check (2 settimane)](#5-fase-3--auto-diagnosi-e-health-check)
6. [Fase 4 — Sicurezza e Tracciabilità Audit (2 settimane)](#6-fase-4--sicurezza-e-tracciabilità-audit)
7. [Fase 5 — Architettura e Refactoring Strutturale (1 mese)](#7-fase-5--architettura-e-refactoring-strutturale)
8. [Riepilogo Tabellare](#8-riepilogo-tabellare)
9. [Regole di Sicurezza per le Modifiche](#9-regole-di-sicurezza-per-le-modifiche)

---

## 1. Executive Summary

L'analisi completa del codice sorgente rivela un'applicazione **funzionante ma fragile** per l'ambiente di produzione industriale 24/7. La logica di business — ispezione visiva, routing Top/Top3D, allarmi a card configurabili, contatori, ricette — è **corretta e ben strutturata**. I problemi non riguardano la logica ma la **solidità operativa**: gestione errori silenziosa, mancanza di watchdog hardware, stato non persistito, assenza di auto-recovery, sicurezza minima.

### Rischi reali in produzione

| Rischio | Scenario Concreto | Probabilità |
|---------|------------------|-------------|
| **Uscita espulsione bloccata HIGH** | Guasto valvola pneumatica, tutti i pezzi espulsi anche buoni | Alta |
| **Freeze UI 30+ secondi** | MySQL timeout durante query su thread UI | Alta |
| **Counter overflow** | Dopo ~6 mesi a 180 ppm il contatore int va negativo | Media |
| **Allarmi duplicati** | Race condition: stessa difettosità trigghera 2 impulsi in 10ms | Media |
| **Nessuna traccia accessi** | Chi ha modificato la ricetta? Chi ha azzerato i counter? Nessuna risposta | Alta |
| **Crash a riavvio con DB down** | MySQL spento → HMI non parte → produzione ferma | Alta |
| **Memory leak VisionPro** | CogImage non dismesse → 2GB RAM dopo 8h → crash | Media |
| **Disco pieno per log** | Nlog senza rotazione → 50GB dopo 6 mesi → crash app | Alta |

### Principio guida del consolidamento

> **Non si rompe ciò che funziona.** Ogni modifica è additiva o sostitutiva di un pattern specifico, con API pubblica invariata. Zero riscritture architetturali nelle fasi 0–3.

---

## 2. FASE 0 — Blocchi Critici Pre-Deploy

**Durata**: 1–2 giorni  
**Obiettivo**: Eliminare i difetti che causano crash o perdita silenziosa di dati in produzione fin dal primo giorno.

---

### F0-1 · Tutti i `catch {}` silenti devono loggare

**Problema**: Numerosi blocchi `catch { }` senza logging in `MainWindow.xaml.cs` (linee ~515, 989, 1145 e altri). Eccezioni inghiottite silenziosamente.

**Scenario fallimento**: Corruzione stato display VisionPro durante cambio ricetta → UI bloccata senza traccia → impossibile diagnosticare.

**Fix**: Ricerca globale `catch { }` e `catch (Exception) { }` vuoti → aggiungere almeno:
```csharp
// PRIMA
catch { }

// DOPO
catch (Exception ex) { logger.Warn(ex, "Display cleanup failed — non-critical"); }
```
Per i catch nei loop critici (vision callbacks, timer): usare `logger.Error`.

**File coinvolti**: `MainWindow.xaml.cs`, `Services/*.cs`, `ViewModels/*.cs`

---

### F0-2 · NLog: rotazione file obbligatoria

**Problema**: Se `Nlog.config` non imposta limiti di dimensione/archivio, il log cresce senza limite → disco pieno → crash app.

**Scenario fallimento**: Dopo 6 mesi di produzione il file log occupa 50 GB. Il disco si riempie. L'applicazione non riesce a scrivere → crash → produzione ferma.

**Fix** in `Nlog.config`:
```xml
<target name="file" xsi:type="File"
        fileName="${basedir}/Logs/app.log"
        archiveFileName="${basedir}/Logs/Archives/app_{#}.log"
        archiveEvery="Day"
        archiveNumbering="Date"
        maxArchiveFiles="30"
        archiveDateFormat="yyyyMMdd"
        concurrentWrites="true"
        keepFileOpen="false" />
```

**File coinvolti**: `Nlog.config`

---

### F0-3 · Guard null su IoManager prima di ogni scrittura output

**Problema**: `MainWindow.OnAlarmTriggered` controlla `ioManager != null` ma se la scheda Advantech non si è inizializzata, il write fallisce silenziosamente → uscita non si alza.

**Fix**: Aggiungere log diagnostico esplicito quando IoManager è null:
```csharp
var ioManager = ServiceLocator.IoManager;
if (ioManager == null)
{
    logger.Error("ALARM_IO_UNAVAILABLE|alarm={0} — IoManager not initialized, output skipped", e.Alarm?.Name);
    // mostra avviso all'operatore
    Dispatcher.BeginInvoke(() =>
        ServiceLocator.DialogService.ShowWarning(
            $"ATTENZIONE: uscita fisica non disponibile per allarme '{e.Alarm?.Name}'\nScheda I/O non inizializzata.", "Hardware"));
}
```

**File coinvolti**: `MainWindow.xaml.cs`

---

### F0-4 · Startup non deve crashare se MySQL è offline

**Problema**: `CounterManager`, `AuthorizationService` e `Cls_InitializzeDb` chiamano `await conn.OpenAsync()` senza timeout né fallback. Se MySQL è offline, lo startup va in eccezione non gestita.

**Scenario fallimento**: Riavvio del DB server durante cambio turno → HMI non parte → operatori bloccati.

**Fix — pattern da applicare ovunque si apra una connessione**:
```csharp
private async Task<MySqlConnection> OpenConnectionWithTimeoutAsync()
{
    var conn = new MySqlConnection(_connectionString);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try
    {
        await conn.OpenAsync(cts.Token);
        return conn;
    }
    catch (OperationCanceledException)
    {
        logger.Error("DB_CONNECT_TIMEOUT|host={0}", _host);
        throw new TimeoutException($"Database non raggiungibile entro 5 s: {_host}");
    }
    catch (MySqlException ex)
    {
        logger.Error(ex, "DB_CONNECT_FAILED|host={0}", _host);
        throw;
    }
}
```

**Per lo startup**: separare la fase di caricamento config (solo XML, no DB) dalla fase di connessione DB (asincrona in background):
```csharp
// In SplashWindow: Config.xml carica sempre
await configManager.EnsureLoadedAsync();

// DB in background — non blocca startup
_ = Task.Run(async () =>
{
    try { await ServiceLocator.CounterManager.EnsureDatabaseConnectedAsync(); }
    catch (Exception ex) { logger.Warn(ex, "DB not available at startup, using local fallback"); }
});
```

**File coinvolti**: `Database/CounterManager.cs`, `Database/Cls_InitializzeDb.cs`, `Services/AuthorizationService.cs`, `Database/Cls_CheckUser.cs`

---

### F0-5 · Task fire-and-forget devono avere exception handler

**Problema**: Pattern `_ = Task.Run(async () => { ... })` in `MainWindow.OnAlarmTriggered` (auto-turnoff impulso). Se l'`await WriteOutputAsync` lancia eccezione, la task termina silenziosamente.

**Fix**: Creare un metodo estensione `SafeFireAndForget`:
```csharp
// File: Extensions/TaskExtensions.cs
public static class TaskExtensions
{
    public static void SafeFireAndForget(this Task task, Action<Exception> onException = null)
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                onException?.Invoke(t.Exception?.InnerException);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}

// Uso:
Task.Run(async () =>
{
    await Task.Delay(duration);
    await ioManager.WriteOutputAsync(outputChannel, false);
}).SafeFireAndForget(ex => logger.Error(ex, "ALARM_IO_AUTO_RESET_FAILED|channel=DO{0}", outputChannel));
```

**File coinvolti**: nuovo file `Extensions/TaskExtensions.cs`, `MainWindow.xaml.cs`

---

### F0-6 · Indici MySQL su colonne di query frequente

**Problema**: `tblgenerale.DataeOra` interrogata senza indice → full table scan → query lente dopo pochi mesi.

**Fix**: Aggiungere in `Cls_InitializzeDb.cs` subito dopo la CREATE TABLE:
```sql
-- Aggiungere a InitializeTablesAsync()
CREATE INDEX IF NOT EXISTS idx_tblgenerale_data ON tblgenerale (DataeOra);
CREATE INDEX IF NOT EXISTS idx_tblgenerale_esito ON tblgenerale (EsitoClassificazione);
CREATE INDEX IF NOT EXISTS idx_tblgenerale_recipe ON tblgenerale (RecipeName);
```

**File coinvolti**: `Database/Cls_InitializzeDb.cs`

---

## 3. FASE 1 — Stabilità Hardware e Allarmi

**Durata**: 1 settimana  
**Obiettivo**: Comportamento affidabile dell'hardware fisico e del sistema allarmi. Zero uscite bloccate, zero trigger duplicati.

---

### F1-1 · Output Watchdog — uscite bloccate HIGH

**Problema**: Nessun meccanismo rileva un'uscita digitale bloccata in stato HIGH. Un guasto del solenoide pneumatico causa espulsione continua di tutti i pezzi.

**Fix**: Aggiungere un `OutputWatchdogService` che legge periodicamente le uscite fisiche e confronta con lo stato atteso:

```csharp
// Services/OutputWatchdogService.cs
public class OutputWatchdogService : IDisposable
{
    private readonly IIODeviceManager _io;
    private readonly Dictionary<int, (bool expectedState, DateTime setAt)> _outputRegistry = new();
    private readonly Timer _watchdogTimer;
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private const int STUCK_HIGH_THRESHOLD_SECONDS = 60;

    public OutputWatchdogService(IIODeviceManager io)
    {
        _io = io;
        _watchdogTimer = new Timer(OnWatchdogTick, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void RegisterExpectedOutput(int channel, bool expectedState)
    {
        _outputRegistry[channel] = (expectedState, DateTime.Now);
    }

    private async void OnWatchdogTick(object state)
    {
        try
        {
            foreach (var kv in _outputRegistry)
            {
                int ch = kv.Key;
                bool expected = kv.Value.expectedState;
                DateTime setAt = kv.Value.setAt;

                bool actual = await _io.ReadOutputStateAsync(ch);
                if (actual && !expected && (DateTime.Now - setAt).TotalSeconds > STUCK_HIGH_THRESHOLD_SECONDS)
                {
                    _logger.Error("IO_OUTPUT_STUCK_HIGH|channel=DO{0}|setAt={1}", ch, setAt);
                    // Force reset
                    await _io.WriteOutputAsync(ch, false);
                    _logger.Warn("IO_OUTPUT_FORCED_RESET|channel=DO{0}", ch);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "OUTPUT_WATCHDOG_ERROR");
        }
    }

    public void Dispose() => _watchdogTimer?.Dispose();
}
```

Registrare in `ServiceLocator` e chiamare `RegisterExpectedOutput(ch, false)` dopo ogni reset allarme.

---

### F1-2 · Race condition allarmi — trigger duplicato

**Problema**: Se due risultati di ispezione arrivano in 5 ms e entrambi superano la soglia, l'allarme può scattare due volte (due impulsi → sovraccarico solenoide).

**Fix** in `EjectionAlarmManager.TriggerAlarm()`:
```csharp
private void TriggerAlarm(EjectionAlarmConfig alarm, Dictionary<DefectType, int> defectCounts, string reason)
{
    lock (_lock)
    {
        // Guard: non triggerare di nuovo se già attivo o se troppo presto
        if (alarm.IsTriggered) return;
        if ((DateTime.Now - alarm.LastTriggered).TotalMilliseconds < 500) return;

        alarm.IsTriggered = true;
        alarm.LastTriggered = DateTime.Now;
        alarm.TriggerCount++;
    }

    // Solleva evento fuori dal lock
    string message = $"Ejection Alarm Triggered: {alarm.Name} - {reason}";
    _logger.Warn(message);
    AlarmTriggered?.Invoke(this, new AlarmTriggeredEventArgs
    {
        Alarm = alarm,
        DefectCounts = defectCounts,
        TriggerTime = DateTime.Now,
        Message = message
    });
}
```

**File coinvolti**: `Models/EjectionAlarmManager.cs`

---

### F1-3 · Allarmi non scattano quando macchina è ferma

**Problema**: `ProcessInspectionResultAsync` non verifica se la macchina è in stato di produzione. Durante avvio/stop residuo, allarmi possono scattare e attivare uscite.

**Fix** in `IntegratedAlarmCardService.ProcessInspectionResultAsync`:
```csharp
public async Task ProcessInspectionResultAsync(InspectionResult result)
{
    // Non elaborare allarmi se la macchina non è in produzione
    if (!ServiceLocator.MachineRuntimeService.IsContinuousRunActive)
    {
        _logger.Debug("Alarm processing skipped — machine not in continuous run");
        return;
    }
    // ... logica esistente
}
```

**File coinvolti**: `Services/IntegratedAlarmCardService.cs`

---

### F1-4 · Log eventi allarme persistito su DB

**Problema**: Quando un allarme scatta, non viene scritto nulla su DB. Al riavvio, tutta la storia degli allarmi è persa.

**Fix**: Aggiungere tabella `alarm_events` e scrivere ogni trigger:
```sql
-- In Cls_InitializzeDb.cs
CREATE TABLE IF NOT EXISTS `alarm_events` (
    Id          INT AUTO_INCREMENT PRIMARY KEY,
    AlarmName   VARCHAR(100) NOT NULL,
    TriggerTime DATETIME NOT NULL,
    DefectsJson TEXT,
    OutputChannel VARCHAR(20),
    AcknowledgedBy VARCHAR(100),
    AcknowledgedAt DATETIME,
    DurationMs  INT,
    INDEX idx_alarm_time (TriggerTime)
);
```

In `IntegratedAlarmCardService.OnAlarmTriggered`:
```csharp
private async void OnAlarmTriggered(object sender, AlarmTriggeredEventArgs e)
{
    AlarmTriggered?.Invoke(this, e);
    _logger.Warn($"ALLARME TRIGGERED: {e.Alarm.Name} - {e.Message}");

    // Persist to DB
    try
    {
        await _repository.LogAlarmEventAsync(new AlarmEvent
        {
            AlarmName = e.Alarm.Name,
            TriggerTime = e.TriggerTime,
            DefectsJson = JsonConvert.SerializeObject(e.DefectCounts),
            OutputChannel = e.Alarm.OutputChannelId,
            DurationMs = e.Alarm.PulseDurationMs
        });
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "ALARM_EVENT_LOG_FAILED|alarm={0}", e.Alarm.Name);
    }
}
```

**File coinvolti**: `Database/Cls_InitializzeDb.cs`, `Services/IntegratedAlarmCardService.cs`, nuovo `Models/AlarmEvent.cs`

---

### F1-5 · Memory leak CogImage — dispose esplicito

**Problema**: `_topRecord`, `_sideRecord`, `_frontRecord` in `MainWindow` sono referenze a oggetti Cognex mai dismesse. Dopo 8 ore, il processo può consumare 2+ GB di RAM.

**Fix**: Prima di assegnare il nuovo record, dismettere il precedente:
```csharp
// Helper da usare ogni volta che si aggiorna un record
private void SetTopRecord(CogRecord newRecord)
{
    try { (_topRecord as IDisposable)?.Dispose(); } catch { /* best effort */ }
    _topRecord = newRecord;
}
```

Applicare lo stesso pattern a `_sideRecord` e `_frontRecord`.

**File coinvolti**: `MainWindow.xaml.cs`

---

### F1-6 · Recovery telecamera — non solo log ma azione

**Problema**: Il watchdog VisionPro rileva la mancanza di risultati e logga, ma l'auto-recovery (`RecoverContinuousRunAsync`) viene chiamato solo dopo N fallimenti senza timeout esplicito.

**Fix**: Aggiungere counter di tentativi recovery e alert se recovery ripetuto fallisce:
```csharp
private int _recoveryAttempts = 0;
private const int MAX_RECOVERY_ATTEMPTS = 3;

// Nel watchdog, dopo trigger recovery:
_recoveryAttempts++;
if (_recoveryAttempts > MAX_RECOVERY_ATTEMPTS)
{
    logger.Error("VISION_RECOVERY_EXHAUSTED|attempts={0} — manual intervention required", _recoveryAttempts);
    Dispatcher.BeginInvoke(() =>
        ServiceLocator.DialogService.ShowWarning(
            "Il sistema di visione non risponde dopo 3 tentativi di recovery.\nVerificare la connessione telecamera.", "Errore Sistema"));
    _recoveryAttempts = 0; // reset per permettere nuovi tentativi manuali
}
```

**File coinvolti**: `MainWindow.xaml.cs`

---

## 4. FASE 2 — Solidità Database e Configurazione

**Durata**: 1 settimana  
**Obiettivo**: Dati mai persi, configurazione mai corrotta, DB resiliente.

---

### F2-1 · Connection pooling MySQL

**Problema**: Ogni operazione DB apre una nuova connessione TCP. Con 10 ispezioni/secondo e 3 thread che scrivono contemporaneamente → esaurimento connessioni.

**Fix**: Aggiungere parametri pool nella stringa di connessione:
```csharp
// In Cls_InitializzeDb.cs o nel metodo che costruisce la connection string
private string BuildConnectionString()
{
    return $"Server={_host};Database={_db};Uid={_user};Pwd={_password};Port={_port};" +
           $"Min Pool Size=3;Max Pool Size=15;Connection Lifetime=300;" +
           $"Connection Timeout=10;SslMode=None;";
}
```

**File coinvolti**: `Database/Cls_InitializzeDb.cs`, `Database/clsConnection.cs`

---

### F2-2 · Transazioni per operazioni multi-step

**Problema**: Salvataggio contatore globale + record pezzo avviene in due query separate. Se la seconda fallisce, i dati sono inconsistenti.

**Fix**: Wrappare in transazione:
```csharp
public async Task SaveInspectionResultAsync(InspectionResult result, ProduzioneRecord record)
{
    await using var conn = await OpenConnectionWithTimeoutAsync();
    await using var transaction = await conn.BeginTransactionAsync();
    try
    {
        await SaveProduzioneRecordAsync(conn, transaction, record);
        await IncrementCountersAsync(conn, transaction, result);
        await transaction.CommitAsync();
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        _logger.Error(ex, "DB_SAVE_INSPECTION_FAILED|rollback executed");
        throw;
    }
}
```

**File coinvolti**: `Database/ProduzioneRecord/ProduzioneRecord.cs`, `Database/CounterManager.cs`

---

### F2-3 · Retention policy tblgenerale

**Problema**: `tblgenerale` cresce senza limiti. A 180 ppm = 259.200 righe/giorno. Dopo 1 anno: ~95 milioni di righe. Query diventano lente.

**Fix**: Aggiungere in `Cls_InitializzeDb.cs` la creazione di un evento MySQL mensile:
```sql
CREATE EVENT IF NOT EXISTS archive_old_inspections
ON SCHEDULE EVERY 1 MONTH
STARTS '2026-06-01 02:00:00'
DO BEGIN
    CREATE TABLE IF NOT EXISTS tblgenerale_archive LIKE tblgenerale;
    INSERT INTO tblgenerale_archive
        SELECT * FROM tblgenerale
        WHERE DataeOra < DATE_SUB(NOW(), INTERVAL 6 MONTH);
    DELETE FROM tblgenerale
        WHERE DataeOra < DATE_SUB(NOW(), INTERVAL 6 MONTH);
END;
```

Aggiungere anche in C# un check che avvisa se la tabella supera N righe:
```csharp
private async Task CheckTableSizeAsync()
{
    long count = await GetTableRowCountAsync("tblgenerale");
    if (count > 10_000_000)
        _logger.Warn("TABLE_SIZE_WARNING|tblgenerale={0} rows — consider archival", count);
}
```

---

### F2-4 · Contatori: tipo long + shift reset + rate ppm

**Problema**: Contatori `int` → overflow dopo ~6 mesi a max throughput. Nessun reset per turno. Nessun calcolo velocità.

**Fix** in `CounterManager.cs`:
```csharp
// 1. Cambiare tutti i contatori da int a long
private long _total = 0;
private long _good = 0;
private long _noGood = 0;
// ... tutti i contatori difetti

// 2. Aggiungere reset turno
private DateTime _shiftStartTime = DateTime.Now;

public async Task ResetForNewShiftAsync(string shiftName, string operatorName)
{
    var snapshot = GetAllCounters();
    await ArchiveShiftDataAsync(snapshot, shiftName, operatorName);
    ResetAllCounters();
    _shiftStartTime = DateTime.Now;
    _logger.Info("SHIFT_RESET|shift={0}|operator={1}|total={2}", shiftName, operatorName, snapshot.Total);
}

// 3. Rate di produzione
public double GetCurrentPpm()
{
    var elapsedMinutes = (DateTime.Now - _shiftStartTime).TotalMinutes;
    return elapsedMinutes < 0.1 ? 0 : _total / elapsedMinutes;
}
```

**File coinvolti**: `Database/CounterManager.cs`

---

### F2-5 · Backup versionato Config.xml

**Problema**: Backup su MySQL ma se sia il file che il DB si corrompono contemporaneamente (raro ma possibile), nessun recovery.

**Fix**: Aggiungere backup su filesystem versionato nel metodo `SaveConfigAsync`:
```csharp
public async Task SaveConfigAsync()
{
    // 1. Backup versionato prima di sovrascrivere
    if (File.Exists(_configFilePath))
    {
        string backupDir = Path.Combine(Path.GetDirectoryName(_configFilePath), "cfg_backups");
        Directory.CreateDirectory(backupDir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupPath = Path.Combine(backupDir, $"Config_{stamp}.xml");
        File.Copy(_configFilePath, backupPath, overwrite: true);

        // Mantieni solo ultimi 30 backup
        var old = Directory.GetFiles(backupDir, "Config_*.xml")
                           .OrderByDescending(f => f)
                           .Skip(30);
        foreach (var f in old) File.Delete(f);
    }

    // 2. Scrittura atomica (logica esistente)
    await WriteTextAtomicallyInternalAsync(_configFilePath, content);

    // 3. Backup MySQL (logica esistente)
    await BackupToMySqlAsync();
}
```

**File coinvolti**: `Cls_Config/AsyncConfigManagerXml.cs`

---

### F2-6 · Validazione nome database — prevenzione SQL injection DDL

**Problema**: `Cls_InitializzeDb.cs` usa il nome DB da Config.xml in una stringa SQL senza validazione.

**Fix**: Whitelist sui nomi prima dell'uso in DDL:
```csharp
private static bool IsValidIdentifier(string name)
{
    // Solo lettere, cifre, underscore — max 64 caratteri
    return !string.IsNullOrEmpty(name) &&
           name.Length <= 64 &&
           System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_]+$");
}

// Usare prima di ogni DDL con nome variabile
if (!IsValidIdentifier(dbName))
    throw new InvalidOperationException($"Nome database non valido: '{dbName}'");
```

**File coinvolti**: `Database/Cls_InitializzeDb.cs`

---

## 5. FASE 3 — Auto-Diagnosi e Health Check

**Durata**: 2 settimane  
**Obiettivo**: Il sistema sa quando qualcosa non va e lo dice — senza che l'operatore debba scoprirlo dal difetto sul prodotto.

---

### F3-1 · Startup Health Check — tutti i sottosistemi

**Problema**: All'avvio, nessun test verifica che telecamere, scheda I/O e DB siano operativi. L'HMI "parte" ma potrebbe non fare nulla di utile.

**Fix**: Nuovo `StartupHealthChecker` chiamato in `SplashWindow` prima di mostrare MainWindow:

```csharp
// Services/StartupHealthChecker.cs
public class StartupHealthChecker
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public async Task<HealthCheckReport> RunAsync(IProgress<string> progress)
    {
        var report = new HealthCheckReport();

        // 1. Database
        progress?.Report("Verifica connessione database...");
        report.Database = await CheckDatabaseAsync();

        // 2. Scheda I/O
        progress?.Report("Verifica scheda I/O Advantech...");
        report.IoBoard = await CheckIoBoardAsync();

        // 3. File VPP
        progress?.Report("Verifica file VisionPro...");
        report.VppFile = await CheckVppFileAsync();

        // 4. Cartella ricette
        progress?.Report("Verifica cartella ricette...");
        report.RecipeFolder = await CheckRecipeFolderAsync();

        // 5. Spazio disco
        progress?.Report("Verifica spazio disco...");
        report.DiskSpace = await CheckDiskSpaceAsync();

        _logger.Info("STARTUP_HEALTH_CHECK|db={0}|io={1}|vpp={2}|recipe={3}|disk={4}",
            report.Database, report.IoBoard, report.VppFile, report.RecipeFolder, report.DiskSpace);

        return report;
    }

    private async Task<HealthStatus> CheckDatabaseAsync()
    {
        try
        {
            await ServiceLocator.CounterManager.PingAsync();
            return HealthStatus.OK;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "HEALTH_DB_FAILED");
            return HealthStatus.Degraded; // non blocca avvio — usa fallback locale
        }
    }

    private async Task<HealthStatus> CheckIoBoardAsync()
    {
        var io = ServiceLocator.IoManager;
        if (io == null) return HealthStatus.Unavailable;
        if (io.IsSimulationMode) return HealthStatus.Simulated;
        try
        {
            await io.ReadAllInputsAsync(); // ping lettura
            return HealthStatus.OK;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HEALTH_IO_FAILED");
            return HealthStatus.Unavailable;
        }
    }

    private Task<HealthStatus> CheckVppFileAsync()
    {
        var path = MainWindow.configManager?.Config?.Configuration?.LastRecipePath;
        return Task.FromResult(
            File.Exists(path) ? HealthStatus.OK : HealthStatus.Unavailable);
    }

    private Task<HealthStatus> CheckDiskSpaceAsync()
    {
        var imageDir = MainWindow.configManager?.Config?.Configuration?.ImageDir ?? @"D:\";
        var drive = new DriveInfo(Path.GetPathRoot(imageDir));
        double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
        if (freeGb < 5) return Task.FromResult(HealthStatus.Critical);
        if (freeGb < 20) return Task.FromResult(HealthStatus.Warning);
        return Task.FromResult(HealthStatus.OK);
    }
}

public enum HealthStatus { OK, Simulated, Degraded, Warning, Critical, Unavailable }

public class HealthCheckReport
{
    public HealthStatus Database { get; set; }
    public HealthStatus IoBoard { get; set; }
    public HealthStatus VppFile { get; set; }
    public HealthStatus RecipeFolder { get; set; }
    public HealthStatus DiskSpace { get; set; }
    public bool CanStart => VppFile != HealthStatus.Unavailable; // VPP è l'unico bloccante
}
```

La splash screen mostra i risultati con semaforo verde/giallo/rosso. Solo VPP mancante è bloccante.

---

### F3-2 · Dashboard diagnostica runtime

**Problema**: Durante la produzione non c'è visibilità sullo stato di salute dei sottosistemi in tempo reale.

**Fix**: Aggiungere in `SystemDiagnosticsView` (già esistente) un pannello con:

```
┌─────────────────────────────────────────────────────┐
│  STATO SISTEMA                              [LIVE]  │
├───────────────┬──────────────┬──────────────────────┤
│ 🟢 Visione    │ 🟢 I/O Board │ 🟢 Database          │
│  TOP: 23 ms   │  DI: OK      │  Latenza: 2 ms       │
│  SIDE: 18 ms  │  DO: OK      │  Connesso            │
├───────────────┴──────────────┴──────────────────────┤
│ 🟢 Velocità: 145 ppm    🟡 Disco: 18 GB liberi      │
│ 🟢 RAM: 512 MB          🟢 CPU: 23%                 │
│                                                     │
│ Ultimo allarme: 14:23:05 — Alarm 1 (Logo x5)       │
└─────────────────────────────────────────────────────┘
```

Binding a `SystemDiagnosticsViewModel` già esistente — aggiungere:
- `VisionLatencyMs` (media ultime 100 ispezioni)
- `CurrentPpm` (da CounterManager)
- `LastAlarmInfo` (da AlarmService)
- `DatabaseLatencyMs` (ping periodico)

---

### F3-3 · Alert operatore su stato degradato

**Problema**: La diagnostica di sistema (`SystemDiagnosticsService`) monitora CPU/RAM/disco ma non integra warning nell'UI principale.

**Fix**: Aggiungere un banner persistente in `MainWindow.xaml` che appare quando CPU > 80%, RAM > 85%, disco < 20GB:

```xml
<!-- In MainWindow.xaml, sopra il navigation menu -->
<Border x:Name="SystemWarningBanner"
        Background="#FFF3CD" BorderBrush="#FFC107" BorderThickness="0,0,0,2"
        Visibility="Collapsed" Padding="12,6">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="⚠" FontSize="14" Margin="0,0,8,0" Foreground="#856404"/>
        <TextBlock x:Name="SystemWarningText" Foreground="#856404" FontSize="12" VerticalAlignment="Center"/>
    </StackPanel>
</Border>
```

---

### F3-4 · Validazione ricetta prima del caricamento

**Problema**: Se il file XML di ricetta è corrotto o manca un campo, il caricamento crashsenza un messaggio chiaro.

**Fix**: Aggiungere `RecipeValidator` invocato prima del load:
```csharp
public class RecipeValidator
{
    public ValidationResult Validate(RecipeParameters recipe)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(recipe.RecipeName))
            errors.Add("RecipeName è obbligatorio");

        if (recipe.ProductDimensions == null)
            errors.Add("ProductDimensions mancanti");
        else
        {
            if (recipe.ProductDimensions.NominalHeight <= 0)
                errors.Add("NominalHeight deve essere > 0");
            if (recipe.ProductDimensions.NominalWidth <= 0)
                errors.Add("NominalWidth deve essere > 0");
        }

        if (recipe.CameraSetting?.TopCameraTriggerDelay < 0)
            errors.Add("TopCameraTriggerDelay non può essere negativo");

        return new ValidationResult(errors);
    }
}
```

---

### F3-5 · Rollback su cambio ricetta fallito

**Problema**: Se il caricamento ricetta fallisce a metà (es. dopo aver aggiornato i validator ma prima del VPP reload), il sistema rimane in stato ibrido.

**Fix**: Snapshot dello stato corrente prima del cambio, restore in caso di errore:
```csharp
private async Task ChangeRecipeWithRollbackAsync(string newRecipeName)
{
    // Snapshot
    var previousRecipe = _currentRecipeName;
    var previousConfig = _currentInspectionConfig?.Clone();

    try
    {
        await LoadRecipeAsync(newRecipeName);
        _logger.Info("RECIPE_CHANGE_SUCCESS|from={0}|to={1}|user={2}",
            previousRecipe, newRecipeName, UserSession.CurrentUser);
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "RECIPE_CHANGE_FAILED|recipe={0}|rollback to={1}", newRecipeName, previousRecipe);

        // Rollback
        try
        {
            if (previousConfig != null)
                await RestoreInspectionConfigAsync(previousConfig);
            _currentRecipeName = previousRecipe;
        }
        catch (Exception rollbackEx)
        {
            _logger.Fatal(rollbackEx, "RECIPE_ROLLBACK_FAILED — system in unknown state");
        }

        throw; // propaga per mostrare errore all'operatore
    }
}
```

---

## 6. FASE 4 — Sicurezza e Tracciabilità Audit

**Durata**: 2 settimane  
**Obiettivo**: Ogni azione significativa ha un'impronta: chi, quando, cosa. Credenziali sicure.

---

### F4-1 · Audit log per operazioni critiche

**Problema**: Cambio ricetta, reset counter, acknowledge allarme, login/logout non sono tracciati su DB.

**Fix**: Aggiungere tabella `audit_log` e scrivere all'evento:
```sql
CREATE TABLE IF NOT EXISTS `audit_log` (
    Id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    Timestamp   DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    EventType   VARCHAR(50) NOT NULL,
    Username    VARCHAR(100),
    UserRole    VARCHAR(50),
    Details     TEXT,
    OldValue    TEXT,
    NewValue    TEXT,
    INDEX idx_audit_time (Timestamp),
    INDEX idx_audit_event (EventType)
);
```

Eventi da loggare:
- `LOGIN_SUCCESS`, `LOGIN_FAILURE`
- `LOGOUT`
- `RECIPE_CHANGE` (da, a, utente)
- `COUNTER_RESET` (snapshot valori precedenti, utente)
- `ALARM_ACKNOWLEDGE` (nome allarme, utente, durata attivo)
- `CONFIG_CHANGE` (campo, vecchio valore, nuovo valore)
- `ALARM_CONFIG_CHANGE` (allarme modificato, utente)

```csharp
// Services/AuditLogService.cs
public class AuditLogService
{
    public async Task LogAsync(string eventType, string details,
                               string oldValue = null, string newValue = null)
    {
        try
        {
            await using var conn = await _dbFactory.OpenAsync();
            await conn.ExecuteAsync(
                @"INSERT INTO audit_log (EventType, Username, UserRole, Details, OldValue, NewValue)
                  VALUES (@et, @user, @role, @det, @old, @new)",
                new {
                    et = eventType,
                    user = UserSession.CurrentUser ?? "SYSTEM",
                    role = UserSession.CurrentRole ?? "UNKNOWN",
                    det = details,
                    old = oldValue,
                    @new = newValue
                });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "AUDIT_LOG_WRITE_FAILED|event={0}", eventType);
        }
    }
}
```

Registrare in `ServiceLocator` e iniettare dove necessario.

---

### F4-2 · Log login/logout con conteggio fallimenti

**Problema**: Nessuna traccia di chi accede, quando, e quanti tentativi falliti.

**Fix** in `Cls_CheckUser.GetUserRoleAsync`:
```csharp
public async Task<string> GetUserRoleAsync(string username, string password)
{
    var role = await VerifyPasswordAsync(username, password);

    if (role == null)
    {
        await ServiceLocator.AuditLog.LogAsync("LOGIN_FAILURE", $"username={username}");
        _logger.Warn("LOGIN_FAILED|user={0}", username);
        return null;
    }

    await ServiceLocator.AuditLog.LogAsync("LOGIN_SUCCESS", $"username={username}|role={role}");
    _logger.Info("LOGIN_SUCCESS|user={0}|role={1}", username, role);
    return role;
}
```

---

### F4-3 · Hash password con BCrypt

**Problema**: Il metodo `HashCode()` è probabilmente un hash debole (SHA1/MD5). In un contesto industriale dove i PC sono accessibili ai tecnici, le credenziali nel DB devono essere sicure.

**Fix**: Aggiungere `BCrypt.Net-Next` (NuGet), aggiornare hash/verify:
```csharp
// Hashing al momento della creazione/cambio password
string hash = BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);

// Verifica al login
bool isValid = BCrypt.Net.BCrypt.Verify(inputPassword, storedHash);
```

**Nota**: Richiede migrazione one-shot delle password esistenti al primo login.

---

### F4-4 · Acknowledge allarme tracciato

**Fix** in `AlarmNotificationWindow.AcknowledgeButton_Click`:
```csharp
private async void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
{
    AcknowledgeButton.IsEnabled = false;
    var ackTime = DateTime.Now;
    var activeDuration = (ackTime - _event.TriggerTime).TotalSeconds;

    // Log audit
    await ServiceLocator.AuditLog.LogAsync(
        "ALARM_ACKNOWLEDGE",
        $"alarm={_event.Alarm?.Name}|channel={_event.Alarm?.OutputChannelId}|activeDuration={activeDuration:F1}s",
        newValue: $"acknowledgedBy={UserSession.CurrentUser}");

    // Reset uscita fisica se latch
    if (_needsManualReset) { ... }

    Close();
}
```

---

## 7. FASE 5 — Architettura e Refactoring Strutturale

**Durata**: 1 mese  
**Obiettivo**: Rendere il codice manutenibile, testabile, estensibile. Nessuna funzionalità cambia — solo riorganizzazione.

> ⚠️ Questa fase NON è necessaria per andare in produzione. Si esegue solo dopo che le fasi 0–4 sono stabili e verificate in campo.

---

### F5-1 · Decomporre MainWindow — estrazione servizi

**Problema**: `MainWindow.xaml.cs` ha ~3600 righe con responsabilità miste: VisionPro lifecycle, IO hardware, allarmi, contatori, ricette, UI.

**Piano di estrazione** (in ordine di sicurezza):

1. **`VisionOrchestrationService`** — start/stop/recovery vision, gestione watchdog  
2. **`InspectionCoordinator`** — riceve risultati camera, coordina debounce, chiama InspectionProcessor  
3. **`AlarmCoordinationService`** — gestisce evento triggered, scrive IO, mostra popup  
4. **`RecipeTransitionService`** — cambio ricetta con snapshot/rollback

Ogni estrazione: crea il servizio → sposta codice → MainWindow delega → verifica comportamento invariato.

---

### F5-2 · Logging strutturato JSON per machine events

**Fix** — aggiungere target JSON a `Nlog.config`:
```xml
<target name="jsonFile" xsi:type="File"
        fileName="${basedir}/Logs/events.json"
        archiveEvery="Day" maxArchiveFiles="90">
    <layout xsi:type="JsonLayout" includeAllProperties="true">
        <attribute name="ts"       layout="${longdate}" />
        <attribute name="level"    layout="${level}" />
        <attribute name="logger"   layout="${logger:shortName=true}" />
        <attribute name="msg"      layout="${message}" />
        <attribute name="ex"       layout="${exception:format=ToString}" />
    </layout>
</target>
```

Questo permette query programmatiche su tutti gli eventi macchina.

---

### F5-3 · Concetto di turno produttivo

Aggiungere a `CounterManager`:
- `CurrentShift` (nome turno: Mattino/Pomeriggio/Notte)
- `ShiftStartTime`
- UI pulsante "Nuovo Turno" con campo nome operatore
- Report turno: contatori + velocità media + numero allarmi

---

### F5-4 · API diagnostica locale (opzionale)

Per supporto remoto senza accesso desktop:
```csharp
// Piccolo HttpListener locale (non esposto su rete, solo localhost:18080)
public class LocalDiagnosticsApi
{
    public DiagnosticsSnapshot GetSnapshot() => new DiagnosticsSnapshot
    {
        Timestamp = DateTime.Now,
        VisionRunning = ServiceLocator.MachineRuntimeService.IsContinuousRunActive,
        CurrentRecipe = _currentRecipe,
        Counters = ServiceLocator.CounterManager.GetAllCounters(),
        LastAlarm = ServiceLocator.AlarmService.GetLastAlarm(),
        SystemHealth = _healthChecker.GetLastReport(),
        RecentErrors = _recentErrors.GetLast(50)
    };
}
```

---

## 8. Riepilogo Tabellare

### Per Fase

| Fase | Descrizione | Durata | Blocca deploy? |
|------|-------------|--------|----------------|
| **F0** | Blocchi critici — catch silenti, NLog, null IoManager, startup DB, fire-and-forget, indici | 1–2 gg | ✅ SÌ |
| **F1** | Hardware e allarmi — output watchdog, race condition, macchina ferma, log DB, memory leak | 1 sett | ✅ SÌ |
| **F2** | Database e config — pooling, transazioni, retention, contatori long, backup versionato | 1 sett | ⚠️ Alto rischio |
| **F3** | Auto-diagnosi — health check startup, dashboard, alert, validazione ricetta, rollback | 2 sett | ⚠️ Fortemente consigliato |
| **F4** | Sicurezza e audit — audit log, login tracking, BCrypt, ack tracciato | 2 sett | ⚠️ Richiesto per compliance |
| **F5** | Architettura strutturale — decomposizione, logging JSON, turni, API diagnostica | 1 mese | ❌ NO (solo manutenibilità) |

### Per Gravità

| Categoria | Critici | Alti | Totale |
|-----------|---------|------|--------|
| Error handling | 5 | 3 | 8 |
| Threading/Concorrenza | 4 | 2 | 6 |
| Config/Startup | 4 | 3 | 7 |
| Hardware I/O | 3 | 2 | 5 |
| VisionPro Lifecycle | 4 | 2 | 6 |
| Sistema Allarmi | 4 | 1 | 5 |
| Sistema Ricette | 3 | 1 | 4 |
| Contatori | 4 | 0 | 4 |
| Autorizzazioni | 4 | 0 | 4 |
| Logging | 4 | 0 | 4 |
| UI/UX | 2 | 2 | 4 |
| Database | 5 | 0 | 5 |
| Deployment | 3 | 2 | 5 |
| **Totale** | **49** | **18** | **67** |

---

## 9. Regole di Sicurezza per le Modifiche

Ogni modifica nel piano di consolidamento deve rispettare:

1. **Leggere prima di modificare** — mai editare codice non letto nella sessione
2. **API pubbliche invariate nelle fasi 0–3** — nessuna firma di metodo cambia
3. **Nessuna regressione sulla logica di ispezione** — routing Top/Top3D, threshold allarmi, contatori: mai toccare senza test
4. **Un commit per punto** — ogni fix F0-x è un commit separato con descrizione
5. **`code-changes-log.md` aggiornato** dopo ogni modifica (regola CLAUDE.md)
6. **Test smoke dopo ogni fase** — eseguire un ciclo completo di ispezione simulato prima di passare alla fase successiva
7. **Rollback plan** — per ogni modifica, sapere come tornare indietro in < 5 minuti

---

*Documento generato dall'analisi automatica del codice sorgente — Maggio 2026*  
*Non modificare le sezioni "Scenario fallimento" — servono al team di QA per validare i fix.*
