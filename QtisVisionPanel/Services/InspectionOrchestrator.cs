using NLog;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Manages the multi-camera inspection queue: waits for all expected companion
    /// camera results to arrive within the configured timeout, then dispatches a
    /// complete group (Top + companions + missing roles) to the inspection pipeline.
    ///
    /// The orchestrator holds shared references to the five <see cref="ConcurrentQueue{T}"/>
    /// instances that live in <see cref="MainWindow"/>.  MainWindow continues to own
    /// the queues and to enqueue camera results; the orchestrator owns dequeue and
    /// the wait-for-companion logic.
    ///
    /// All operations that touch machine state (display, counters, alarms, DB record)
    /// are delegated back to MainWindow via <see cref="_processGroup"/>, keeping the
    /// refactoring incremental and low-risk.
    /// </summary>
    internal sealed class InspectionOrchestrator
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Raised when WaitForExpectedCompanionResultsAsync times out before all
        /// companion camera results arrive.  Args: (missingRoles, waitedMs).
        /// Always invoked from the orchestrator background thread — subscribers that
        /// touch the UI must marshal to the Dispatcher.
        /// </summary>
        public event Action<IReadOnlyList<string>, double> CompanionTimeoutOccurred;

        /// <summary>
        /// Floor/fallback single-shot per il lag "stesso-prodotto" (ms) tra TOP e companion.
        ///
        /// Questa e' la soglia di DEFAULT usata quando il provider iniettato
        /// (<see cref="_getCompanionSameProductLagMs"/>) non e' disponibile o restituisce un
        /// valore non valido. E' un concetto DISTINTO dal timeout di attesa risultato: qui conta
        /// la durata FISICA plausibile della cattura, non quanto siamo disposti ad aspettare.
        /// Per il MultiShot il valore fisico (piu' grande) arriva dal provider, non da questa
        /// costante, e in nessun caso e' pari al timeout di sicurezza (30s).
        /// </summary>
        private const double MaxCompanionLagMs = 5_000.0;
        private const int SyntheticQueueRetainedResults = 3;
        private static readonly TimeSpan SyntheticQueueLogInterval = TimeSpan.FromSeconds(2);

        private readonly SemaphoreSlim _processSemaphore;
        private readonly ConcurrentQueue<CameraResult> _topQueue;
        private readonly ConcurrentQueue<CameraResult> _leftQueue;
        private readonly ConcurrentQueue<CameraResult> _frontQueue;
        private readonly ConcurrentQueue<CameraResult> _rightQueue;
        private readonly ConcurrentQueue<CameraResult> _rearQueue;
        private readonly ConcurrentQueue<CameraResult> _bottomQueue;

        private readonly Func<bool> _shouldAcceptVisionResults;
        private readonly Action<string, bool> _clearPendingVisionResults;
        private readonly Func<IReadOnlyList<string>> _getExpectedCompanionRoles;
        private readonly Func<TimeSpan> _getInspectionGroupTimeout;
        // Lag "stesso-prodotto" (ms), FISICO e distinto dal timeout di attesa. Fornito da MainWindow
        // (single-shot: 5s; MultiShot: valore da config). Vedi GetCompanionSameProductLagMs().
        private readonly Func<double> _getCompanionSameProductLagMs;

        // Dispatches a completed inspection group to MainWindow processing.
        // Signature matches ProcessInspectionGroupAsync(topResult, companionResults, missingRoles).
        private readonly Func<CameraResult, IReadOnlyDictionary<string, CameraResult>, IEnumerable<string>, Task> _processGroup;
        private DateTime _lastSyntheticQueueLogAtUtc = DateTime.MinValue;
        private long _syntheticResultsDropped;
        private int _syntheticPairingModeLogged;
        private int _processingRequested;

        public InspectionOrchestrator(
            SemaphoreSlim processSemaphore,
            ConcurrentQueue<CameraResult> topQueue,
            ConcurrentQueue<CameraResult> leftQueue,
            ConcurrentQueue<CameraResult> frontQueue,
            ConcurrentQueue<CameraResult> rightQueue,
            ConcurrentQueue<CameraResult> rearQueue,
            ConcurrentQueue<CameraResult> bottomQueue,
            Func<bool> shouldAcceptVisionResults,
            Action<string, bool> clearPendingVisionResults,
            Func<IReadOnlyList<string>> getExpectedCompanionRoles,
            Func<TimeSpan> getInspectionGroupTimeout,
            Func<double> getCompanionSameProductLagMs,
            Func<CameraResult, IReadOnlyDictionary<string, CameraResult>, IEnumerable<string>, Task> processGroup)
        {
            _processSemaphore = processSemaphore ?? throw new ArgumentNullException(nameof(processSemaphore));
            _topQueue = topQueue ?? throw new ArgumentNullException(nameof(topQueue));
            _leftQueue = leftQueue ?? throw new ArgumentNullException(nameof(leftQueue));
            _frontQueue = frontQueue ?? throw new ArgumentNullException(nameof(frontQueue));
            _rightQueue = rightQueue ?? throw new ArgumentNullException(nameof(rightQueue));
            _rearQueue = rearQueue ?? throw new ArgumentNullException(nameof(rearQueue));
            _bottomQueue = bottomQueue ?? throw new ArgumentNullException(nameof(bottomQueue));
            _shouldAcceptVisionResults = shouldAcceptVisionResults ?? throw new ArgumentNullException(nameof(shouldAcceptVisionResults));
            _clearPendingVisionResults = clearPendingVisionResults ?? throw new ArgumentNullException(nameof(clearPendingVisionResults));
            _getExpectedCompanionRoles = getExpectedCompanionRoles ?? throw new ArgumentNullException(nameof(getExpectedCompanionRoles));
            _getInspectionGroupTimeout = getInspectionGroupTimeout ?? throw new ArgumentNullException(nameof(getInspectionGroupTimeout));
            _getCompanionSameProductLagMs = getCompanionSameProductLagMs ?? throw new ArgumentNullException(nameof(getCompanionSameProductLagMs));
            _processGroup = processGroup ?? throw new ArgumentNullException(nameof(processGroup));
        }

        /// <summary>
        /// Entry point called by MainWindow after each camera result is enqueued.
        /// Non-reentrant: if a previous call is still running, this call returns
        /// immediately without processing (the semaphore prevents double-dispatch).
        /// </summary>
        public async Task ProcessQueuedPairsAsync(CancellationToken ct)
        {
            Interlocked.Exchange(ref _processingRequested, 1);
            if (!await _processSemaphore.WaitAsync(0, ct).ConfigureAwait(false))
            {
                _log.Debug("Processing already in progress, skipping this attempt.");
                return;
            }

            Interlocked.Exchange(ref _processingRequested, 0);

            try
            {
                if (!_shouldAcceptVisionResults())
                {
                    _clearPendingVisionResults("queued pair processing while machine is stopped", false);
                    return;
                }

                var expectedCompanionRoles = _getExpectedCompanionRoles();
                if (expectedCompanionRoles.Count == 0)
                {
                    // No companion camera — process Top/Top3D standalone.
                    // Routing (Top vs Top3D) is driven by CameraResult.CameraRole, not MachineType config.
                    while (true)
                    {
                        TrimSyntheticBacklog(_topQueue, "top");
                        if (!_topQueue.TryDequeue(out var topOnly))
                            break;

                        bool isTop3D = string.Equals(topOnly?.CameraRole, "top3d", StringComparison.OrdinalIgnoreCase);
                        _log.Info($"Processing {(isTop3D ? "Top3D" : "Top")} standalone — queue remaining: {_topQueue.Count}");
                        await _processGroup(topOnly, new Dictionary<string, CameraResult>(), Enumerable.Empty<string>())
                              .ConfigureAwait(false);
                    }
                    return;
                }

                while (true)
                {
                    TrimSyntheticBacklog(_topQueue, "top");
                    if (!_topQueue.TryPeek(out var topPeek))
                        break;

                    ct.ThrowIfCancellationRequested();

                    if (!_shouldAcceptVisionResults())
                    {
                        _clearPendingVisionResults("queued inspection group processing aborted after runtime stop", false);
                        break;
                    }

                    var timeout = _getInspectionGroupTimeout();
                    // Soglia "stesso-prodotto" FISICA fornita da MainWindow, DISTINTA dal timeout di
                    // attesa (`timeout`). In MultiShot riflette la durata plausibile della scansione,
                    // non i 30s di tetto di sicurezza. Fallback prudente al floor single-shot (5s) se
                    // il provider restituisce un valore non valido.
                    double maxLagMs = _getCompanionSameProductLagMs?.Invoke() ?? MaxCompanionLagMs;
                    if (maxLagMs <= 0)
                        maxLagMs = MaxCompanionLagMs;
                    var waitStart = DateTime.UtcNow;

                    if (topPeek.IsSyntheticAcquisition)
                    {
                        foreach (string role in expectedCompanionRoles)
                        {
                            TrimSyntheticBacklog(GetQueueForRole(role), role);
                        }
                    }

                    var missingRoles = await WaitForExpectedCompanionResultsAsync(
                        expectedCompanionRoles, timeout, waitStart, topPeek, maxLagMs, ct).ConfigureAwait(false);

                    var waitedMs = (DateTime.UtcNow - waitStart).TotalMilliseconds;

                    if (!_topQueue.TryDequeue(out var topResult))
                        break;

                    var companions = new Dictionary<string, CameraResult>(StringComparer.OrdinalIgnoreCase);
                    foreach (string role in expectedCompanionRoles)
                    {
                        var queue = GetQueueForRole(role);
                        if (queue == null) continue;

                        if (!queue.TryPeek(out var candidate)) continue;

                        // Hardware jobs use acquisition timing to protect product identity.
                        // Synthetic QuickBuild jobs have no common trigger or product clock,
                        // so their simulation-only path consumes results in FIFO order.
                        if (!UsesSyntheticOrdinalPairing(topResult, candidate) &&
                            candidate.EnqueuedAtUtc != DateTime.MinValue)
                        {
                            var candidateLag = (candidate.EnqueuedAtUtc - topResult.EnqueuedAtUtc).TotalMilliseconds;
                            if (candidateLag > maxLagMs)
                            {
                                _log.Warn(
                                    $"COMPANION_PRODUCT_MISMATCH|role={CameraConfigurationHelper.NormalizePhysicalCameraRole(candidate.CameraRole)}" +
                                    $" lag={candidateLag:F0}ms > {maxLagMs:F0}ms threshold" +
                                    $" — result belongs to a future product, leaving in queue for next TOP");
                                continue;
                            }
                        }

                        if (TryDequeueCompanion(queue, role, topResult, out var companion))
                        {
                            companions[CameraConfigurationHelper.NormalizePhysicalCameraRole(companion.CameraRole)] = companion;
                        }
                    }

                    // Timing trace: lag from TOP enqueue to each companion enqueue.
                    foreach (var companion in companions)
                    {
                        var lagMs = (companion.Value.EnqueuedAtUtc - topResult.EnqueuedAtUtc).TotalMilliseconds;
                        _log.Info($"CAMERA_TIMING|role={companion.Key} lag-from-top={lagMs:+0;-0;0}ms");
                    }

                    _log.Info(
                        $"Processing inspection group | topSequence={topResult.ResultSequence}" +
                        $" topProduct={topResult.TriggeredProductId?.ToString() ?? "uncorrelated"}" +
                        $" companionSequences=[{string.Join(",", companions.Select(item => item.Key + ":" + item.Value.ResultSequence))}]" +
                        $" companionProducts=[{string.Join(",", companions.Select(item => item.Key + ":" + (item.Value.TriggeredProductId?.ToString() ?? "uncorrelated")))}]" +
                        $" waited={waitedMs:F0}ms companions=[{string.Join(",", companions.Keys)}] missing=[{string.Join(",", missingRoles)}]");

                    await _processGroup(topResult, companions, missingRoles).ConfigureAwait(false);
                }
            }
            finally
            {
                _processSemaphore.Release();

                // Close the lost-wakeup window. A producer can enqueue TOP while this
                // consumer still owns the gate, observe it busy and return. Rechecking
                // after Release guarantees that the queued result gets a new drain.
                bool drainRequested = Interlocked.Exchange(ref _processingRequested, 0) != 0;
                if (!ct.IsCancellationRequested &&
                    _shouldAcceptVisionResults() &&
                    (drainRequested || !_topQueue.IsEmpty))
                {
                    ProcessQueuedPairsAsync(ct).SafeFireAndForget(
                        _log,
                        "INSPECTION_QUEUE_RESTART_FAILED");
                }
            }
        }

        private async Task<IReadOnlyList<string>> WaitForExpectedCompanionResultsAsync(
            IReadOnlyList<string> expectedRoles,
            TimeSpan timeout,
            DateTime waitStart,
            CameraResult topResult,
            double maxLagMs,
            CancellationToken ct)
        {
            var expected = (expectedRoles ?? Array.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => CameraConfigurationHelper.NormalizePhysicalCameraRole(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (expected.Count == 0)
                return Array.Empty<string>();

            var alreadyArrived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime deadlineUtc = waitStart + timeout;

            while (DateTime.UtcNow < deadlineUtc)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var role in expected)
                {
                    if (!alreadyArrived.Contains(role) && RoleQueueHasValidResult(role, topResult, maxLagMs))
                    {
                        alreadyArrived.Add(role);
                        var arrivedMs = (DateTime.UtcNow - waitStart).TotalMilliseconds;
                        _log.Info($"COMPANION_ARRIVED|role={role} waited={arrivedMs:F0}ms timeout={timeout.TotalSeconds:F0}s");
                    }
                }

                if (alreadyArrived.Count == expected.Count)
                    return Array.Empty<string>();

                await Task.Delay(20, ct).ConfigureAwait(false);
            }

            var missing = expected.Where(r => !RoleQueueHasValidResult(r, topResult, maxLagMs)).ToList();
            var elapsedMs = (DateTime.UtcNow - waitStart).TotalMilliseconds;
            _log.Warn(
                $"COMPANION_TIMEOUT|topSequence={topResult?.ResultSequence ?? 0}" +
                $"|waited={elapsedMs:F0}ms|timeout={timeout.TotalMilliseconds:F0}ms" +
                $"|missing=[{string.Join(",", missing)}]|arrived=[{string.Join(",", alreadyArrived)}]" +
                $"|queues={DescribeQueueState(expected, topResult, maxLagMs)}");

            foreach (string missingRole in missing)
            {
                CameraDiagnostics.ResultMissing(missingRole, topResult?.TriggeredProductId, elapsedMs);
            }

            if (missing.Count > 0)
                CompanionTimeoutOccurred?.Invoke(missing, elapsedMs);

            return missing;
        }

        private string DescribeQueueState(IEnumerable<string> roles, CameraResult topResult, double maxLagMs)
        {
            var states = new List<string>();
            foreach (string role in roles ?? Enumerable.Empty<string>())
            {
                string normalizedRole = CameraConfigurationHelper.NormalizePhysicalCameraRole(role);
                var queue = GetQueueForRole(normalizedRole);
                if (queue == null)
                {
                    states.Add(normalizedRole + ":unmapped");
                    continue;
                }

                if (!queue.TryPeek(out var head) || head == null)
                {
                    states.Add(normalizedRole + ":empty");
                    continue;
                }

                double lagMs = head.EnqueuedAtUtc == DateTime.MinValue || topResult == null
                    ? double.NaN
                    : (head.EnqueuedAtUtc - topResult.EnqueuedAtUtc).TotalMilliseconds;
                string lag = double.IsNaN(lagMs) ? "n/a" : lagMs.ToString("F0");
                states.Add(
                    normalizedRole + ":count=" + queue.Count +
                    ",headSequence=" + head.ResultSequence +
                    ",lagMs=" + lag +
                    ",valid=" + RoleQueueHasValidResult(normalizedRole, topResult, maxLagMs));
            }

            return "[" + string.Join(";", states) + "]";
        }

        /// <summary>
        /// For hardware acquisition, returns true only when the companion timestamp plausibly
        /// belongs to the current TOP product. For two synthetic QuickBuild jobs, timestamps are
        /// not a shared product identity, so queue presence is accepted and FIFO order is used.
        /// </summary>
        private bool RoleQueueHasValidResult(string role, CameraResult topResult, double maxLagMs)
        {
            var queue = GetQueueForRole(role);
            if (queue == null || queue.IsEmpty) return false;

            if (topResult?.TriggeredProductId != null)
            {
                long productId = topResult.TriggeredProductId.Value;
                return queue.Any(candidate => candidate?.TriggeredProductId == productId);
            }

            if (!queue.TryPeek(out var candidate)) return false;

            if (UsesSyntheticOrdinalPairing(topResult, candidate))
            {
                if (Interlocked.Exchange(ref _syntheticPairingModeLogged, 1) == 0)
                {
                    _log.Info(
                        "VISION_SIMULATION_PAIRING_ACTIVE|Synthetic VisionPro jobs are paired by FIFO order; " +
                        "cross-camera timestamps are not a product identity in QuickBuild simulation.");
                }

                return true;
            }

            // If the timestamp was never set (MinValue), fall back to presence-only check so that
            // any result without timing info is still accepted (safe backward-compatible default).
            if (candidate.EnqueuedAtUtc == DateTime.MinValue)
                return true;

            var lag = (candidate.EnqueuedAtUtc - topResult.EnqueuedAtUtc).TotalMilliseconds;
            return Math.Abs(lag) <= maxLagMs;
        }

        private bool TryDequeueCompanion(
            ConcurrentQueue<CameraResult> queue,
            string role,
            CameraResult topResult,
            out CameraResult companion)
        {
            companion = null;
            if (queue == null)
            {
                return false;
            }

            if (topResult?.TriggeredProductId == null)
            {
                return queue.TryDequeue(out companion);
            }

            long productId = topResult.TriggeredProductId.Value;
            int itemsToScan = queue.Count;
            for (int i = 0; i < itemsToScan && queue.TryDequeue(out CameraResult candidate); i++)
            {
                if (candidate?.TriggeredProductId == productId)
                {
                    companion = candidate;
                    return true;
                }

                if (candidate?.TriggeredProductId != null && candidate.TriggeredProductId.Value > productId)
                {
                    queue.Enqueue(candidate);
                    break;
                }

                _log.Warn(
                    $"COMPANION_STALE_DROPPED|role={CameraConfigurationHelper.NormalizeCameraType(role)}" +
                    $"|sequence={candidate?.ResultSequence ?? 0}" +
                    $"|resultProduct={candidate?.TriggeredProductId?.ToString() ?? "uncorrelated"}" +
                    $"|expectedProduct={productId}");
                CameraDiagnostics.ResultStaleDropped(
                    role, candidate?.ResultSequence ?? 0, candidate?.TriggeredProductId, productId);
                ReleaseDroppedResult(candidate);
            }

            return false;
        }

        private void TrimSyntheticBacklog(ConcurrentQueue<CameraResult> queue, string role)
        {
            if (queue == null)
                return;

            int droppedNow = 0;
            while (queue.Count > SyntheticQueueRetainedResults &&
                   queue.TryPeek(out var oldest) &&
                   oldest?.IsSyntheticAcquisition == true)
            {
                if (!queue.TryDequeue(out var dropped))
                    break;

                ReleaseDroppedResult(dropped);
                droppedNow++;
            }

            if (droppedNow == 0)
                return;

            long droppedTotal = Interlocked.Add(ref _syntheticResultsDropped, droppedNow);
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastSyntheticQueueLogAtUtc >= SyntheticQueueLogInterval)
            {
                _lastSyntheticQueueLogAtUtc = nowUtc;
                _log.Info(
                    $"VISION_SIM_QUEUE_COALESCED|role={CameraConfigurationHelper.NormalizeCameraType(role)}" +
                    $"|dropped={droppedNow}|droppedTotal={droppedTotal}|retained={queue.Count}");
            }
        }

        private static bool UsesSyntheticOrdinalPairing(CameraResult topResult, CameraResult companionResult)
        {
            return topResult?.IsSyntheticAcquisition == true &&
                   companionResult?.IsSyntheticAcquisition == true;
        }

        private static void ReleaseDroppedResult(CameraResult result)
        {
            result?.ReleaseOutputSnapshot();

            try
            {
                (result?.Record as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Unable to release a coalesced synthetic VisionPro record.");
            }
        }

        private ConcurrentQueue<CameraResult> GetQueueForRole(string role)
        {
            switch (CameraConfigurationHelper.NormalizePhysicalCameraRole(role))
            {
                case "top":
                case "top3d":  return _topQueue;
                case "left":   return _leftQueue;
                case "front":  return _frontQueue;
                case "right":  return _rightQueue;
                case "rear":   return _rearQueue;
                case "bottom": return _bottomQueue;
                default:       return null;
            }
        }
    }
}
