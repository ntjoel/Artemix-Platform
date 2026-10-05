using QtisVisionPanel.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace QtisVisionPanel.Services
{
    internal enum CameraTriggerClaimStatus
    {
        TrackingInactive,
        Matched,
        Unsolicited,
        DuplicateUserResult
    }

    internal sealed class CameraTriggerTicket
    {
        public long ProductId { get; set; }
        public string CameraRole { get; set; }
        public string PointCode { get; set; }
        public string SignalCode { get; set; }
        public DateTime IssuedAtUtc { get; set; }
    }

    /// <summary>
    /// Correlates HMI-owned physical trigger pulses with VisionPro results. This keeps
    /// duplicate or delayed camera events from being consumed by the following product.
    /// </summary>
    internal static class CameraTriggerCorrelationTracker
    {
        private sealed class RoleState
        {
            public readonly object Sync = new object();
            public readonly Queue<CameraTriggerTicket> Pending = new Queue<CameraTriggerTicket>();
            public DateTime LastRegistrationUtc;
            public long LastRegisteredProductId;
            public readonly Queue<string> RecentUserResultTags = new Queue<string>();
            public readonly HashSet<string> RecentUserResultTagSet =
                new HashSet<string>(StringComparer.Ordinal);
        }

        // Profondita' della pipeline di acquisizione: quanti trigger possono essere in volo
        // prima che torni il primo risultato. In campo il giro trigger -> risultato misura
        // ~1,5 s mentre i prodotti passano anche ogni 0,7 s, quindi servono almeno 3 ticket
        // contemporanei. Con capacita' 1 ogni prodotto ravvicinato distruggeva il ticket del
        // precedente e quel pezzo usciva senza ispezione.
        private const int MaxPendingTicketsPerRole = 4;
        private const int MaxRecentUserResultTagsPerRole = 64;

        private static readonly ConcurrentDictionary<string, RoleState> States =
            new ConcurrentDictionary<string, RoleState>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan TrackingActiveWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TicketExpiry = TimeSpan.FromSeconds(20);
        public static CameraTriggerTicket Register(string cameraRole, long productId, string pointCode, string signalCode)
        {
            string normalizedRole = NormalizeCorrelationRole(cameraRole);
            if (string.IsNullOrWhiteSpace(normalizedRole) || productId <= 0)
            {
                return null;
            }

            var nowUtc = DateTime.UtcNow;
            RoleState state = States.GetOrAdd(normalizedRole, _ => new RoleState());
            lock (state.Sync)
            {
                RemoveExpiredTickets(state, nowUtc);

                // A MultiShot session can pulse the same camera several times for one
                // product but emits only one final VisionPro result. Si confronta l'ultimo
                // prodotto REGISTRATO e non la testa della coda: con piu' ticket in volo la
                // testa e' il prodotto piu' vecchio, non quello che si sta pulsando ora.
                if (state.LastRegisteredProductId == productId)
                {
                    state.LastRegistrationUtc = nowUtc;
                    return null;
                }

                state.Pending.Enqueue(new CameraTriggerTicket
                {
                    ProductId = productId,
                    CameraRole = normalizedRole,
                    PointCode = pointCode,
                    SignalCode = signalCode,
                    IssuedAtUtc = nowUtc
                });
                state.LastRegisteredProductId = productId;
                state.LastRegistrationUtc = nowUtc;

                // Non esiste un riscontro hardware di avvenuta acquisizione: se una camera
                // salta un trigger, il suo ticket resterebbe in coda e sfaserebbe di uno
                // tutti i prodotti successivi. Il tetto sulla profondita' e' la protezione:
                // oltre la capacita' si scarta il ticket PIU' VECCHIO (quello che non ha
                // prodotto risultato) e il chiamante logga il riallineamento. Prima si
                // scartava tutta la coda a ogni nuovo trigger, buttando via anche i ticket
                // legittimi dei prodotti ancora in lavorazione.
                CameraTriggerTicket superseded = null;
                if (state.Pending.Count > MaxPendingTicketsPerRole)
                {
                    superseded = state.Pending.Dequeue();
                }

                return superseded;
            }
        }

        public static CameraTriggerClaimStatus TryClaim(string cameraRole, string userResultTag, out CameraTriggerTicket ticket)
        {
            ticket = null;
            string normalizedRole = NormalizeCorrelationRole(cameraRole);
            if (string.IsNullOrWhiteSpace(normalizedRole) || !States.TryGetValue(normalizedRole, out RoleState state))
            {
                return CameraTriggerClaimStatus.TrackingInactive;
            }

            var nowUtc = DateTime.UtcNow;
            lock (state.Sync)
            {
                if (!string.IsNullOrWhiteSpace(userResultTag))
                {
                    if (!state.RecentUserResultTagSet.Add(userResultTag))
                    {
                        return CameraTriggerClaimStatus.DuplicateUserResult;
                    }
                    state.RecentUserResultTags.Enqueue(userResultTag);
                    if (state.RecentUserResultTags.Count > MaxRecentUserResultTagsPerRole)
                    {
                        state.RecentUserResultTagSet.Remove(state.RecentUserResultTags.Dequeue());
                    }
                }
                RemoveExpiredTickets(state, nowUtc);
                if (state.Pending.Count > 0)
                {
                    ticket = state.Pending.Dequeue();
                    return CameraTriggerClaimStatus.Matched;
                }

                return nowUtc - state.LastRegistrationUtc <= TrackingActiveWindow
                    ? CameraTriggerClaimStatus.Unsolicited
                    : CameraTriggerClaimStatus.TrackingInactive;
            }
        }

        /// <summary>
        /// Ritira il ticket di un prodotto il cui fronte di trigger non e' mai arrivato in
        /// scheda. Senza ritiro il ticket resterebbe in coda e la prossima immagine non
        /// richiesta (es. un doppione) verrebbe attribuita a quel prodotto. Si confronta anche
        /// il punto: se un altro punto dello stesso ruolo ha gia' registrato il ticket del
        /// prodotto (Register non ne accoda un secondo), quel ticket resta valido.
        /// </summary>
        public static bool Withdraw(string cameraRole, long productId, string pointCode)
        {
            string normalizedRole = NormalizeCorrelationRole(cameraRole);
            if (string.IsNullOrWhiteSpace(normalizedRole) || productId <= 0 ||
                !States.TryGetValue(normalizedRole, out RoleState state))
            {
                return false;
            }

            lock (state.Sync)
            {
                bool removed = false;
                int count = state.Pending.Count;
                for (int i = 0; i < count; i++)
                {
                    CameraTriggerTicket ticket = state.Pending.Dequeue();
                    if (!removed && ticket.ProductId == productId &&
                        string.Equals(ticket.PointCode, pointCode, StringComparison.OrdinalIgnoreCase))
                    {
                        removed = true;
                        continue;
                    }

                    state.Pending.Enqueue(ticket);
                }

                if (removed && state.LastRegisteredProductId == productId)
                {
                    state.LastRegisteredProductId = 0;
                }

                return removed;
            }
        }

        /// <summary>
        /// True se alla camera sono stati inviati trigger di recente, cioe' se ci si puo'
        /// legittimamente aspettare dei risultati da lei.
        /// </summary>
        public static bool IsTriggerTrackingActive(string cameraRole)
        {
            string normalizedRole = NormalizeCorrelationRole(cameraRole);
            if (string.IsNullOrWhiteSpace(normalizedRole) ||
                !States.TryGetValue(normalizedRole, out RoleState state))
            {
                return false;
            }

            lock (state.Sync)
            {
                return DateTime.UtcNow - state.LastRegistrationUtc <= TrackingActiveWindow;
            }
        }

        /// <summary>
        /// True se la camera ha un trigger emesso da piu' di <paramref name="olderThan"/>
        /// al quale non ha ancora risposto. E' il solo indizio affidabile di camera che non
        /// risponde: il silenzio in assenza di trigger pendenti e' atteso, non un guasto.
        /// </summary>
        public static bool IsAwaitingResult(string cameraRole, TimeSpan olderThan)
        {
            string normalizedRole = NormalizeCorrelationRole(cameraRole);
            if (string.IsNullOrWhiteSpace(normalizedRole) ||
                !States.TryGetValue(normalizedRole, out RoleState state))
            {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            lock (state.Sync)
            {
                RemoveExpiredTickets(state, nowUtc);
                return state.Pending.Count > 0 &&
                       nowUtc - state.Pending.Peek().IssuedAtUtc > olderThan;
            }
        }

        public static void Clear()
        {
            States.Clear();
        }

        private static void RemoveExpiredTickets(RoleState state, DateTime nowUtc)
        {
            while (state.Pending.Count > 0 && nowUtc - state.Pending.Peek().IssuedAtUtc > TicketExpiry)
            {
                state.Pending.Dequeue();
            }
        }

        private static string NormalizeCorrelationRole(string cameraRole)
        {
            return CameraConfigurationHelper.NormalizePhysicalCameraRole(cameraRole);
        }
    }
}
