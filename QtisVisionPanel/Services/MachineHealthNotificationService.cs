using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Integrazione AI (Fase 8) — Monitoraggio derive + notifica preallarmi al cliente.
    ///
    /// FONDAZIONE (questo file, additiva e sicura):
    /// aggrega in tempo reale i segnali gia' prodotti dai servizi advisory esistenti —
    /// <see cref="ProcessControlService.DriftDetected"/> (deriva ispezioni/SPC) e
    /// <see cref="PredictiveMaintenanceService.MaintenancePredicted"/> (deriva salute PC) —
    /// li normalizza in <see cref="EarlyWarning"/> con severita' e stima "quanto manca al limite",
    /// deduplica/throttla il rumore e li instrada su uno o piu' <see cref="INotificationChannel"/>.
    ///
    /// Cadenza: severita' CRITICA inviata SUBITO; il resto accumulato e inviato come DIGEST periodico
    /// (fine turno). Tutto off-thread e degradation-safe: un errore di canale NON ferma la macchina e
    /// non blocca il ciclo di ispezione. Il canale locale (<see cref="LocalEventLogNotificationChannel"/>)
    /// e' sempre attivo e scrive nell'Events Monitor: i preallarmi non si perdono mai, anche senza rete.
    ///
    /// I canali esterni (MES via OPC UA, Email/SMTP) sono aggiunti in step successivi implementando
    /// <see cref="INotificationChannel"/>: la logica di rilevamento e aggregazione qui NON cambia.
    ///
    /// Attivazione via <c>MachineRuntimeBindings.MachineHealthNotificationsEnabled</c>
    /// (default disabilitato sulle nuove configurazioni).
    /// </summary>
    public class MachineHealthNotificationService : IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly object _lock = new object();

        // Canali di uscita. Il canale locale (Events Monitor) e' sempre presente come rete di sicurezza.
        private readonly List<INotificationChannel> _channels = new List<INotificationChannel>();

        // Buffer del digest (preallarmi non-critici in attesa dell'invio periodico).
        private readonly List<EarlyWarning> _pendingDigest = new List<EarlyWarning>();

        // Storico recente per la UI (pannello preallarmi in PC Diagnostics).
        private const int MaxRecent = 100;
        private readonly List<EarlyWarning> _recent = new List<EarlyWarning>();

        // Badge proattivo: ultimo momento in cui l'operatore ha aperto/aggiornato il pannello
        // preallarmi. Solo Warning/Critical contano per il badge (Info non genera rumore).
        private DateTime _lastAcknowledgedUtc = DateTime.MinValue;

        // Anti-spam: ultimo invio per chiave; entro il cooldown lo stesso preallarme non si ripete.
        private readonly Dictionary<string, DateTime> _lastNotifiedUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private ProcessControlService _processControl;
        private PredictiveMaintenanceService _predictiveMaintenance;
        private Timer _digestTimer;

        private volatile bool _enabled;
        private volatile bool _started;
        private bool _disposed;

        // Soglie (lette dalla config, con default sensati).
        private int _digestIntervalMinutes = 480;   // ~1 turno
        private int _criticalEtaProducts = 50;       // deriva ispezione: ETA <= X pezzi => critica
        private double _diskCriticalHours = 24.0;    // salute PC: disco a soglia entro X ore => critica

        /// <summary>
        /// Raised whenever the unseen warning count changes. Subscribers must marshal to
        /// their UI thread when needed because advisory events can arrive on worker threads.
        /// </summary>
        public event EventHandler WarningStateChanged;

        public MachineHealthNotificationService()
        {
            ReadConfiguration();
            _channels.Add(new LocalEventLogNotificationChannel());
            // Canale MES: pubblica i preallarmi via OPC UA (nodi read-only, opt-in per mappatura).
            // Risolve OpcUaClientService a runtime, quindi non dipende dall'ordine di init.
            _channels.Add(new OpcUaNotificationChannel());
            // Canale Email: invia i preallarmi via SMTP a uno o piu' destinatari (opt-in).
            _channels.Add(new EmailNotificationChannel());
            _logger.Info($"MACHINE_HEALTH_NOTIFY_INIT|enabled={_enabled}|digest_min={_digestIntervalMinutes}|channels={_channels.Count}");
        }

        public bool Enabled => _enabled;

        /// <summary>
        /// Snapshot dei preallarmi recenti (piu' recente per primo) per la UI. Copia difensiva.
        /// </summary>
        public IReadOnlyList<EarlyWarning> GetRecentWarnings()
        {
            lock (_lock)
            {
                var list = _recent.ToList();
                list.Reverse();
                return list;
            }
        }

        /// <summary>
        /// Numero di preallarmi Warning/Critical comparsi dopo l'ultimo <see cref="MarkWarningsSeen"/>.
        /// Usato dal badge proattivo in HMI (non richiede che l'operatore apra il pannello per vedere
        /// che c'e' qualcosa di nuovo).
        /// </summary>
        public int UnseenCount
        {
            get
            {
                lock (_lock)
                {
                    return _recent.Count(w =>
                        w.Severity != EarlyWarningSeverity.Info && w.DetectedAtUtc > _lastAcknowledgedUtc);
                }
            }
        }

        public bool HasUnseenWarnings => UnseenCount > 0;

        /// <summary>Azzera il badge: chiamato quando l'operatore apre/aggiorna il pannello preallarmi.</summary>
        public void MarkWarningsSeen()
        {
            bool changed;
            lock (_lock)
            {
                changed = _recent.Any(w =>
                    w.Severity != EarlyWarningSeverity.Info && w.DetectedAtUtc > _lastAcknowledgedUtc);
                _lastAcknowledgedUtc = DateTime.UtcNow;
            }

            if (changed)
                RaiseWarningStateChanged();
        }

        /// <summary>
        /// Registra un canale di uscita aggiuntivo (es. OPC UA, Email). Idempotente per nome.
        /// </summary>
        public void RegisterChannel(INotificationChannel channel)
        {
            if (channel == null)
                return;

            lock (_lock)
            {
                if (_channels.Any(c => string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase)))
                    return;
                _channels.Add(channel);
            }

            _logger.Info($"MACHINE_HEALTH_NOTIFY_CHANNEL_ADDED|name={channel.Name}");
        }

        /// <summary>
        /// Si aggancia agli eventi advisory esistenti e avvia il timer del digest.
        /// Chiamato una volta dopo la creazione dei servizi AI (ServiceLocator).
        /// </summary>
        public void Start()
        {
            if (_started || _disposed)
                return;

            _started = true;

            try
            {
                _processControl = ServiceLocator.ProcessControlService;
                _predictiveMaintenance = ServiceLocator.PredictiveMaintenanceService;

                if (_processControl != null)
                    _processControl.DriftDetected += OnInspectionDrift;
                if (_predictiveMaintenance != null)
                    _predictiveMaintenance.MaintenancePredicted += OnMaintenanceAlert;

                // Primo digest differito di un intervallo pieno.
                var period = TimeSpan.FromMinutes(Math.Max(1, _digestIntervalMinutes));
                _digestTimer = new Timer(OnDigestTick, null, period, period);

                _logger.Info("MACHINE_HEALTH_NOTIFY_STARTED");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "MACHINE_HEALTH_NOTIFY_START_FAILED");
            }
        }

        public void RefreshConfiguration()
        {
            ReadConfiguration();

            // Riallinea la cadenza del timer al nuovo intervallo, se attivo.
            try
            {
                var period = TimeSpan.FromMinutes(Math.Max(1, _digestIntervalMinutes));
                _digestTimer?.Change(period, period);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // ---- Ingresso segnali (event handler: leggeri, mai lanciano) ----

        private void OnInspectionDrift(ProcessControlService.DriftInfo drift)
        {
            if (!_enabled || drift == null)
                return;

            try
            {
                bool hasEta = drift.EstimatedProductsToLimit >= 0;
                bool critical = hasEta && drift.EstimatedProductsToLimit <= _criticalEtaProducts;

                var warning = new EarlyWarning
                {
                    DetectedAtUtc = drift.DetectedAtUtc,
                    Severity = critical ? EarlyWarningSeverity.Critical : EarlyWarningSeverity.Warning,
                    Category = "Inspection",
                    Key = string.Concat("insp|", drift.Key, "|", drift.Rule ?? string.Empty),
                    Title = $"Deriva ispezione: {drift.Feature} ({drift.CameraRole}) — {drift.Direction}",
                    Detail = string.Format(
                        "Ricetta '{0}', camera {1}, feature {2}: regola {3}, direzione {4}. Valore {5:0.###}, media {6:0.###}, sigma {7:0.###}.",
                        drift.Recipe, drift.CameraRole, drift.Feature, drift.Rule, drift.Direction,
                        drift.CurrentValue, drift.Mean, drift.Sigma),
                    EstimatedToLimit = hasEta ? (double?)drift.EstimatedProductsToLimit : null,
                    EstimatedToLimitUnit = "pezzi",
                    SuggestedUpstreamCause =
                        "Deriva sistematica di una quota/misura: verificare il processo A MONTE " +
                        "(formatura, sigillatura, alimentazione o registro del prodotto) prima che generi scarti."
                };

                Ingest(warning);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "MACHINE_HEALTH_NOTIFY_INSPECTION_INGEST_FAILED");
            }
        }

        private void OnMaintenanceAlert(PredictiveMaintenanceService.MaintenanceAlert alert)
        {
            if (!_enabled || alert == null)
                return;

            try
            {
                bool isDiskProjection = string.Equals(alert.Kind, "projection", StringComparison.OrdinalIgnoreCase)
                    && alert.EstimatedHoursToThreshold.HasValue;
                bool critical = isDiskProjection && alert.EstimatedHoursToThreshold.Value <= _diskCriticalHours;

                var warning = new EarlyWarning
                {
                    DetectedAtUtc = alert.DetectedAtUtc,
                    Severity = critical ? EarlyWarningSeverity.Critical : EarlyWarningSeverity.Warning,
                    Category = "PcHealth",
                    Key = string.Concat("pc|", alert.Component, "|", alert.Kind),
                    Title = $"Salute PC: {alert.Component} — {alert.Kind}",
                    Detail = alert.Message,
                    EstimatedToLimit = alert.EstimatedHoursToThreshold,
                    EstimatedToLimitUnit = "ore",
                    SuggestedUpstreamCause =
                        "Degrado risorse PC: verificare spazio disco/archiviazione immagini, processi in background e raffreddamento."
                };

                Ingest(warning);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "MACHINE_HEALTH_NOTIFY_MAINTENANCE_INGEST_FAILED");
            }
        }

        // ---- Aggregazione + routing ----

        private void Ingest(EarlyWarning warning)
        {
            // Anti-spam: entro il cooldown (intervallo digest) la stessa chiave non si ripete.
            var nowUtc = DateTime.UtcNow;
            var cooldown = TimeSpan.FromMinutes(Math.Max(1, _digestIntervalMinutes));
            bool dispatchImmediately = false;

            lock (_lock)
            {
                // Storico recente per la UI: sempre aggiornato, indipendentemente dal throttle di invio.
                _recent.Add(warning);
                if (_recent.Count > MaxRecent)
                    _recent.RemoveRange(0, _recent.Count - MaxRecent);

                if (_lastNotifiedUtc.TryGetValue(warning.Key, out var last) && (nowUtc - last) < cooldown)
                {
                    // Aggiorna comunque il buffer digest col dato piu' recente ma non rinvia subito.
                    _pendingDigest.RemoveAll(w => string.Equals(w.Key, warning.Key, StringComparison.OrdinalIgnoreCase));
                    _pendingDigest.Add(warning);
                }
                else
                {
                    _lastNotifiedUtc[warning.Key] = nowUtc;

                    if (warning.Severity == EarlyWarningSeverity.Critical)
                    {
                        dispatchImmediately = true;
                    }
                    else
                    {
                        _pendingDigest.Add(warning);
                    }
                }
            }

            RaiseWarningStateChanged();

            if (!dispatchImmediately)
                return;

            // Invio immediato dei soli critici, off-thread.
            DispatchAsync(new List<EarlyWarning> { warning }, isDigest: false)
                .SafeFireAndForget(ex => _logger.Warn(ex, "MACHINE_HEALTH_NOTIFY_CRITICAL_DISPATCH_FAILED"));
        }

        private void RaiseWarningStateChanged()
        {
            try
            {
                WarningStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "MACHINE_HEALTH_NOTIFY_STATE_EVENT_FAILED");
            }
        }

        private void OnDigestTick(object state)
        {
            if (!_enabled)
                return;

            List<EarlyWarning> batch;
            lock (_lock)
            {
                if (_pendingDigest.Count == 0)
                    return;
                batch = new List<EarlyWarning>(_pendingDigest);
                _pendingDigest.Clear();
            }

            DispatchAsync(batch, isDigest: true)
                .SafeFireAndForget(ex => _logger.Warn(ex, "MACHINE_HEALTH_NOTIFY_DIGEST_DISPATCH_FAILED"));
        }

        private async Task DispatchAsync(IReadOnlyList<EarlyWarning> warnings, bool isDigest)
        {
            if (warnings == null || warnings.Count == 0)
                return;

            List<INotificationChannel> channels;
            lock (_lock)
            {
                channels = _channels.ToList();
            }

            foreach (var channel in channels)
            {
                if (channel == null || !channel.IsEnabled)
                    continue;

                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        await channel.SendAsync(warnings, isDigest, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    // Un canale che fallisce non deve impedire agli altri di inviare.
                    _logger.Warn(ex, $"MACHINE_HEALTH_NOTIFY_CHANNEL_FAILED|name={channel.Name}|digest={isDigest}");
                }
            }
        }

        private void ReadConfiguration()
        {
            try
            {
                var bindings = new MachineConfigurationService().Load()?.RuntimeBindings;
                _enabled = bindings?.MachineHealthNotificationsEnabled ?? false;

                if (bindings != null)
                {
                    if (bindings.MachineHealthDigestIntervalMinutes > 0)
                        _digestIntervalMinutes = bindings.MachineHealthDigestIntervalMinutes;
                    if (bindings.MachineHealthCriticalEtaProducts > 0)
                        _criticalEtaProducts = bindings.MachineHealthCriticalEtaProducts;
                    if (bindings.MachineHealthDiskCriticalHours > 0)
                        _diskCriticalHours = bindings.MachineHealthDiskCriticalHours;
                }
            }
            catch
            {
                _enabled = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (_processControl != null)
                    _processControl.DriftDetected -= OnInspectionDrift;
                if (_predictiveMaintenance != null)
                    _predictiveMaintenance.MaintenancePredicted -= OnMaintenanceAlert;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "MACHINE_HEALTH_NOTIFY_UNSUBSCRIBE_FAILED");
            }

            try
            {
                _digestTimer?.Dispose();
            }
            catch
            {
            }
        }
    }

    public enum EarlyWarningSeverity
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>Preallarme normalizzato pronto per l'invio al cliente (canale-agnostico).</summary>
    public sealed class EarlyWarning
    {
        public DateTime DetectedAtUtc { get; set; }
        public EarlyWarningSeverity Severity { get; set; }
        public string Category { get; set; }                 // "Inspection" | "PcHealth"
        public string Key { get; set; }                      // chiave di deduplica
        public string Title { get; set; }
        public string Detail { get; set; }
        public string SuggestedUpstreamCause { get; set; }
        public double? EstimatedToLimit { get; set; }        // quanto manca al limite
        public string EstimatedToLimitUnit { get; set; }     // "pezzi" | "ore"

        // Helper di sola lettura per il binding UI (nessuna dipendenza WPF nel modello).
        public string SeverityText => Severity.ToString();
        public string CategoryText => Category ?? string.Empty;
        public string DetectedAtLocalText => DetectedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        public string EtaText => EstimatedToLimit.HasValue
            ? string.Format("{0:0.#} {1}", EstimatedToLimit.Value, EstimatedToLimitUnit)
            : "—";

        public string FormatLine()
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(Severity.ToString().ToUpperInvariant()).Append("] ").Append(Title);
            if (EstimatedToLimit.HasValue)
                sb.Append($" — stima al limite: {EstimatedToLimit.Value:0.#} {EstimatedToLimitUnit}");
            if (!string.IsNullOrWhiteSpace(Detail))
                sb.Append(" | ").Append(Detail);
            if (!string.IsNullOrWhiteSpace(SuggestedUpstreamCause))
                sb.Append(" | Azione: ").Append(SuggestedUpstreamCause);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Canale di uscita per i preallarmi. Le implementazioni devono essere non bloccanti e
    /// degradation-safe: un errore di invio non deve MAI propagare fino al ciclo macchina.
    /// </summary>
    public interface INotificationChannel
    {
        string Name { get; }
        bool IsEnabled { get; }
        Task SendAsync(IReadOnlyList<EarlyWarning> warnings, bool isDigest, CancellationToken ct);
    }

    /// <summary>
    /// Canale locale sempre attivo: scrive i preallarmi nell'Events Monitor (tbllogevent) e nei log.
    /// E' la rete di sicurezza — i preallarmi restano tracciati anche se non c'e' rete o i canali
    /// esterni non sono configurati.
    /// </summary>
    public sealed class LocalEventLogNotificationChannel : INotificationChannel
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public string Name => "LocalEventLog";
        public bool IsEnabled => true;

        public Task SendAsync(IReadOnlyList<EarlyWarning> warnings, bool isDigest, CancellationToken ct)
        {
            try
            {
                var eventLogger = ServiceLocator.ApplicationEventLogger;
                bool anyCritical = warnings.Any(w => w.Severity == EarlyWarningSeverity.Critical);
                var level = anyCritical ? LogLevel.Warn : LogLevel.Info;
                string logId = isDigest ? "AI_HEALTH_DIGEST" : "AI_EARLY_WARNING";

                var body = new StringBuilder();
                body.AppendLine(isDigest
                    ? $"Riepilogo preallarmi salute macchina ({warnings.Count})."
                    : "Preallarme salute macchina.");
                foreach (var w in warnings)
                    body.AppendLine(w.FormatLine());

                string message = body.ToString().Trim();

                if (eventLogger != null)
                {
                    var metadata = new Dictionary<string, object>
                    {
                        { "count", warnings.Count },
                        { "digest", isDigest },
                        { "critical", warnings.Count(w => w.Severity == EarlyWarningSeverity.Critical) },
                        { "warning", warnings.Count(w => w.Severity == EarlyWarningSeverity.Warning) }
                    };

                    eventLogger.LogOperationalEvent(
                        level, logId, "AI Health", message, nameof(LocalEventLogNotificationChannel), null, metadata);
                }
                else
                {
                    _logger.Warn($"{logId}|{message}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "LOCAL_EVENTLOG_NOTIFY_FAILED");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Canale MES: pubblica i preallarmi verso SCADA/MES tramite i nodi read-only OPC UA
    /// (<c>EarlyWarning*</c>). Air-gap friendly: riusa la connessione OPC UA gia' presente, nessuna
    /// credenziale nuova. Ogni nodo pubblica solo se mappato nella config OPC UA (altrimenti no-op);
    /// se OPC UA e' disabilitato/disconnesso l'invio e' un no-op silenzioso.
    /// </summary>
    public sealed class OpcUaNotificationChannel : INotificationChannel
    {
        public string Name => "OpcUa";

        public bool IsEnabled => ServiceLocator.OpcUaClientService?.IsEnabled == true;

        public async Task SendAsync(IReadOnlyList<EarlyWarning> warnings, bool isDigest, CancellationToken ct)
        {
            var client = ServiceLocator.OpcUaClientService;
            if (client == null || !client.IsEnabled || warnings == null || warnings.Count == 0)
                return;

            // Preallarme "guida": severita' piu' alta, poi ETA piu' vicino, poi piu' recente.
            var lead = warnings
                .OrderByDescending(w => (int)w.Severity)
                .ThenBy(w => w.EstimatedToLimit ?? double.MaxValue)
                .ThenByDescending(w => w.DetectedAtUtc)
                .First();

            var maxSeverity = warnings.Max(w => w.Severity);
            int criticalCount = warnings.Count(w => w.Severity == EarlyWarningSeverity.Critical);

            string message = isDigest
                ? $"Digest {warnings.Count} preallarmi ({criticalCount} critici): {lead.Title}"
                : lead.FormatLine();

            await client.WriteEarlyWarningAsync(
                maxSeverity.ToString(),
                lead.Category,
                message,
                lead.EstimatedToLimit,
                lead.EstimatedToLimitUnit,
                warnings.Count).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Canale Email/SMTP: invia i preallarmi a uno o piu' destinatari via <see cref="SmtpClient"/>.
    /// Legge la configurazione al volo ad ogni invio (nessuno stato in memoria da re-inizializzare
    /// su <see cref="MachineHealthNotificationService.RefreshConfiguration"/>). No-op silenzioso se
    /// disabilitato o se host/mittente/destinatari non sono configurati.
    ///
    /// Nota: su .NET Framework <see cref="SmtpClient.SendMailAsync(MailMessage)"/> non supporta un
    /// <see cref="CancellationToken"/> nativo; il timeout di 30s gia' applicato dal chiamante
    /// (<see cref="MachineHealthNotificationService.DispatchAsync"/>) non interrompe forzatamente
    /// una connessione SMTP bloccata. Non e' un problema per il ciclo macchina (l'invio gira sempre
    /// fuori dal thread di ispezione), ma un server SMTP irraggiungibile puo' far attendere il task
    /// di dispatch piu' a lungo del timeout nominale prima che l'eccezione/i timeout interni di
    /// SmtpClient la interrompano.
    /// </summary>
    public sealed class EmailNotificationChannel : INotificationChannel
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public string Name => "Email";

        public bool IsEnabled
        {
            get
            {
                var bindings = ReadBindings();
                return bindings != null &&
                       bindings.EmailNotificationsEnabled &&
                       !string.IsNullOrWhiteSpace(bindings.EmailSmtpHost) &&
                       !string.IsNullOrWhiteSpace(bindings.EmailFromAddress) &&
                       !string.IsNullOrWhiteSpace(bindings.EmailToAddresses);
            }
        }

        public async Task SendAsync(IReadOnlyList<EarlyWarning> warnings, bool isDigest, CancellationToken ct)
        {
            var bindings = ReadBindings();
            if (bindings == null ||
                !bindings.EmailNotificationsEnabled ||
                string.IsNullOrWhiteSpace(bindings.EmailSmtpHost) ||
                string.IsNullOrWhiteSpace(bindings.EmailFromAddress) ||
                string.IsNullOrWhiteSpace(bindings.EmailToAddresses))
            {
                return;
            }

            await SendWithBindingsAsync(bindings, warnings, isDigest).ConfigureAwait(false);
        }

        /// <summary>
        /// Invia usando i binding forniti esplicitamente invece di rileggerli da disco - usato dal
        /// pulsante "Invia email di prova" del pannello Controlli AI per testare esattamente i
        /// valori appena digitati, anche se non ancora salvati su file. Ignora
        /// <see cref="MachineRuntimeBindings.EmailNotificationsEnabled"/>: una prova esplicita deve
        /// poter verificare host/credenziali anche prima di abilitare il canale.
        /// </summary>
        public Task SendTestAsync(MachineRuntimeBindings bindings, EarlyWarning testWarning)
        {
            if (bindings == null || testWarning == null)
                return Task.CompletedTask;

            return SendWithBindingsAsync(bindings, new List<EarlyWarning> { testWarning }, isDigest: false);
        }

        private async Task SendWithBindingsAsync(
            MachineRuntimeBindings bindings, IReadOnlyList<EarlyWarning> warnings, bool isDigest)
        {
            if (warnings == null || warnings.Count == 0 ||
                string.IsNullOrWhiteSpace(bindings?.EmailSmtpHost) ||
                string.IsNullOrWhiteSpace(bindings.EmailFromAddress) ||
                string.IsNullOrWhiteSpace(bindings.EmailToAddresses))
            {
                return;
            }

            var recipients = bindings.EmailToAddresses
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .ToList();
            if (recipients.Count == 0)
                return;

            int criticalCount = warnings.Count(w => w.Severity == EarlyWarningSeverity.Critical);
            string subjectPrefix = criticalCount > 0 ? "[CRITICO]" : "[Avviso]";
            string subject = isDigest
                ? $"{subjectPrefix} Riepilogo preallarmi macchina ({warnings.Count})"
                : $"{subjectPrefix} {warnings[0].Title}";

            var body = new StringBuilder();
            body.AppendLine(isDigest
                ? $"Riepilogo preallarmi salute macchina ({warnings.Count})."
                : "Preallarme salute macchina.");
            body.AppendLine();
            foreach (var w in warnings)
                body.AppendLine(w.FormatLine());

            using (var message = new MailMessage())
            {
                message.From = new MailAddress(bindings.EmailFromAddress);
                foreach (var recipient in recipients)
                    message.To.Add(recipient);
                message.Subject = subject;
                message.Body = body.ToString().Trim();

                using (var client = new SmtpClient(bindings.EmailSmtpHost, bindings.EmailSmtpPort))
                {
                    client.EnableSsl = bindings.EmailUseSsl;
                    if (!string.IsNullOrWhiteSpace(bindings.EmailSmtpUsername))
                    {
                        client.Credentials = new NetworkCredential(
                            bindings.EmailSmtpUsername, bindings.EmailSmtpPassword ?? string.Empty);
                    }

                    await client.SendMailAsync(message).ConfigureAwait(false);
                }
            }

            _logger.Info($"MACHINE_HEALTH_NOTIFY_EMAIL_SENT|to={recipients.Count}|count={warnings.Count}|digest={isDigest}");
        }

        private static MachineRuntimeBindings ReadBindings()
        {
            try
            {
                return new MachineConfigurationService().Load()?.RuntimeBindings;
            }
            catch
            {
                return null;
            }
        }
    }
}
