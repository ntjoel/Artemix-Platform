using NLog;
using QtisVisionPanel.Models;
using System;
using System.Collections.Concurrent;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Diagnostica per camera fisica. Ogni camera ha un proprio logger NLog
    /// (<c>Camera.top</c>, <c>Camera.left</c>, <c>Camera.right</c>, <c>Camera.front</c>,
    /// <c>Camera.bottom</c>) scritto in <c>camera-&lt;ruolo&gt;.log</c> e anche nel log principale,
    /// cosi' la catena quota -> ticket -> fronte -> risultato di una sola camera si legge senza filtri.
    ///
    /// Il ruolo passa sempre da <see cref="CameraConfigurationHelper.NormalizePhysicalCameraRole"/>:
    /// Side e Left finiscono nello stesso file; Right e Rear hanno file distinti.
    ///
    /// Non va chiamata dal thread di polling: tutti i punti di chiamata sono sul worker
    /// interventi, sulle continuation degli impulsi o sul thread dei risultati VisionPro.
    /// </summary>
    internal static class CameraDiagnostics
    {
        // Riepilogo periodico per camera: abbastanza frequente da vedere una deriva durante un
        // collaudo da 100 pezzi, abbastanza raro da non appesantire il log.
        private const int SummaryEveryTriggers = 50;

        private sealed class CameraChannel
        {
            public readonly object Sync = new object();
            public Logger Log;
            public string Role;

            // Contatori cumulativi dall'avvio.
            public long Triggers;
            public long EdgesFailed;
            public long TicketsWithdrawn;
            public long ResultsMatched;
            public long ResultsUnsolicited;
            public long ResultsDuplicateTag;
            public long ResultsStaleDropped;
            public long ResultsMissing;
            public long ProcessingErrors;

            // Massimi e somme della finestra corrente (azzerati a ogni riepilogo).
            public long WindowTriggers;
            public long WindowMatched;
            public double WindowMaxLateMm;
            public double WindowMaxRaiseLatencyMs;
            public double WindowMaxHighMs;
            public double WindowSumTriggerToResultMs;
            public double WindowMaxTriggerToResultMs;
        }

        private static readonly ConcurrentDictionary<string, CameraChannel> Channels =
            new ConcurrentDictionary<string, CameraChannel>(StringComparer.OrdinalIgnoreCase);

        public static string ResolveRole(string cameraRole)
        {
            string role = CameraConfigurationHelper.NormalizePhysicalCameraRole(cameraRole);
            return string.IsNullOrWhiteSpace(role) ? "unknown" : role.Trim().ToLowerInvariant();
        }

        private static CameraChannel For(string cameraRole)
        {
            string role = ResolveRole(cameraRole);
            return Channels.GetOrAdd(role, key => new CameraChannel
            {
                Role = key,
                Log = LogManager.GetLogger("Camera." + key)
            });
        }

        public static void TriggerPointReached(
            string cameraRole, long productId, string pointCode, string signalCode,
            long targetCounts, long reachedCounts, long lateCounts, double lateMm)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Info(
                $"CAM_TRIGGER_POINT|role={channel.Role}|product={productId}|point={pointCode}|signal={signalCode}" +
                $"|target={targetCounts}|reached={reachedCounts}|lateCounts={lateCounts}|lateMm={lateMm:0.0}");

            bool summary;
            lock (channel.Sync)
            {
                channel.Triggers++;
                channel.WindowTriggers++;
                if (lateMm > channel.WindowMaxLateMm) channel.WindowMaxLateMm = lateMm;
                summary = channel.WindowTriggers >= SummaryEveryTriggers;
            }

            if (summary)
            {
                LogSummary(channel);
            }
        }

        public static void TicketRegistered(string cameraRole, long productId, string pointCode, long? supersededProductId)
        {
            CameraChannel channel = For(cameraRole);
            if (supersededProductId.HasValue)
            {
                channel.Log.Warn(
                    $"CAM_TICKET_REALIGNED|role={channel.Role}|product={productId}|point={pointCode}" +
                    $"|discardedProduct={supersededProductId.Value}|reason=previous trigger produced no result");
                return;
            }

            channel.Log.Info($"CAM_TICKET_REGISTERED|role={channel.Role}|product={productId}|point={pointCode}");
        }

        public static void PulseCompleted(
            string cameraRole, long productId, string signalCode, string channelName,
            double raiseLatencyMs, int requestedMs, double actualHighMs)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Info(
                $"CAM_PULSE|role={channel.Role}|product={productId}|signal={signalCode}|channel={channelName}" +
                $"|raiseLatencyMs={raiseLatencyMs:0.###}|requestedMs={requestedMs}|actualHighMs={actualHighMs:0.###}");

            lock (channel.Sync)
            {
                if (raiseLatencyMs > channel.WindowMaxRaiseLatencyMs) channel.WindowMaxRaiseLatencyMs = raiseLatencyMs;
                if (actualHighMs > channel.WindowMaxHighMs) channel.WindowMaxHighMs = actualHighMs;
            }
        }

        public static void PulseFailed(string cameraRole, long productId, string signalCode, bool edgeGenerated, Exception exception)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Error(
                exception,
                $"CAM_PULSE_FAILED|role={channel.Role}|product={productId}|signal={signalCode}|edgeGenerated={edgeGenerated}");

            lock (channel.Sync)
            {
                channel.EdgesFailed++;
            }
        }

        public static void TicketWithdrawn(string cameraRole, long productId, string pointCode, bool withdrawn)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Error(
                $"CAM_TRIGGER_NOT_EXECUTED|role={channel.Role}|product={productId}|point={pointCode}|ticketWithdrawn={withdrawn}");

            lock (channel.Sync)
            {
                if (withdrawn) channel.TicketsWithdrawn++;
            }
        }

        public static void ResultClaimed(
            string cameraRole, long sequence, string jobName, long productId, string pointCode,
            double triggerToResultMs, double? runTotalMs, double? runProcessingMs, bool processingError,
            string userResultTag)
        {
            CameraChannel channel = For(cameraRole);
            string message =
                $"CAM_RESULT|role={channel.Role}|product={productId}|sequence={sequence}|job={jobName}|point={pointCode}" +
                $"|tag={userResultTag ?? "-"}|triggerToResultMs={triggerToResultMs:0.0}" +
                $"|runTotalMs={FormatOptional(runTotalMs)}|runProcessingMs={FormatOptional(runProcessingMs)}" +
                $"|processingError={processingError}";
            if (processingError)
            {
                channel.Log.Warn(message);
            }
            else
            {
                channel.Log.Info(message);
            }

            lock (channel.Sync)
            {
                channel.ResultsMatched++;
                channel.WindowMatched++;
                channel.WindowSumTriggerToResultMs += triggerToResultMs;
                if (triggerToResultMs > channel.WindowMaxTriggerToResultMs) channel.WindowMaxTriggerToResultMs = triggerToResultMs;
                if (processingError) channel.ProcessingErrors++;
            }
        }

        public static void ResultUnsolicited(string cameraRole, long sequence, string jobName, string userResultTag)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Warn(
                $"CAM_RESULT_UNSOLICITED|role={channel.Role}|sequence={sequence}|job={jobName}|tag={userResultTag ?? "-"}" +
                "|reason=no pending trigger ticket (duplicate acquisition or delayed result)");

            lock (channel.Sync)
            {
                channel.ResultsUnsolicited++;
            }
        }

        public static void ResultDuplicateTag(string cameraRole, long sequence, string jobName, string userResultTag)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Warn(
                $"CAM_RESULT_DUPLICATE_TAG|role={channel.Role}|sequence={sequence}|job={jobName}|tag={userResultTag}");
            lock (channel.Sync)
            {
                channel.ResultsDuplicateTag++;
            }
        }

        public static void ResultStaleDropped(string cameraRole, long sequence, long? resultProductId, long expectedProductId)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Warn(
                $"CAM_RESULT_STALE_DROPPED|role={channel.Role}|sequence={sequence}" +
                $"|resultProduct={resultProductId?.ToString() ?? "uncorrelated"}|expectedProduct={expectedProductId}");

            lock (channel.Sync)
            {
                channel.ResultsStaleDropped++;
            }
        }

        public static void ResultMissing(string cameraRole, long? productId, double waitedMs)
        {
            CameraChannel channel = For(cameraRole);
            channel.Log.Warn(
                $"CAM_RESULT_MISSING|role={channel.Role}|product={productId?.ToString() ?? "unknown"}|waitedMs={waitedMs:0}");

            lock (channel.Sync)
            {
                channel.ResultsMissing++;
            }
        }

        private static void LogSummary(CameraChannel channel)
        {
            string message;
            lock (channel.Sync)
            {
                double avgTriggerToResult = channel.WindowMatched > 0
                    ? channel.WindowSumTriggerToResultMs / channel.WindowMatched
                    : 0.0;
                message =
                    $"CAM_SUMMARY|role={channel.Role}|triggers={channel.Triggers}|matched={channel.ResultsMatched}" +
                    $"|unsolicited={channel.ResultsUnsolicited}|duplicateTags={channel.ResultsDuplicateTag}" +
                    $"|staleDropped={channel.ResultsStaleDropped}" +
                    $"|missing={channel.ResultsMissing}|edgesFailed={channel.EdgesFailed}" +
                    $"|ticketsWithdrawn={channel.TicketsWithdrawn}|processingErrors={channel.ProcessingErrors}" +
                    $"|window={channel.WindowTriggers}|maxLateMm={channel.WindowMaxLateMm:0.0}" +
                    $"|maxRaiseLatencyMs={channel.WindowMaxRaiseLatencyMs:0.###}|maxHighMs={channel.WindowMaxHighMs:0.###}" +
                    $"|avgTriggerToResultMs={avgTriggerToResult:0.0}|maxTriggerToResultMs={channel.WindowMaxTriggerToResultMs:0.0}";

                channel.WindowTriggers = 0;
                channel.WindowMatched = 0;
                channel.WindowMaxLateMm = 0.0;
                channel.WindowMaxRaiseLatencyMs = 0.0;
                channel.WindowMaxHighMs = 0.0;
                channel.WindowSumTriggerToResultMs = 0.0;
                channel.WindowMaxTriggerToResultMs = 0.0;
            }

            channel.Log.Info(message);
        }

        private static string FormatOptional(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.0") : "-";
        }
    }
}
