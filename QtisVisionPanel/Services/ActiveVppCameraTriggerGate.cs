using System;
using System.Collections.Generic;
using System.Linq;
using QtisVisionPanel.Models;
using QtisVisionPanel.Models.MultiShotTrigger;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Masks automatic camera triggers in an effective runtime copy after the VPP
    /// job roles have been resolved. The saved machine wiring is never edited.
    /// </summary>
    public static class ActiveVppCameraTriggerGate
    {
        private static readonly HashSet<string> KnownRoles = new HashSet<string>(
            new[] { "top", "left", "right", "rear", "front", "bottom" },
            StringComparer.OrdinalIgnoreCase);

        public static string NormalizeRole(string value)
        {
            string role = CameraConfigurationHelper.NormalizePhysicalCameraRole(value);
            return KnownRoles.Contains(role) ? role : null;
        }

        public static IReadOnlyList<string> Apply(
            MachineRuntimeConfiguration effective,
            IEnumerable<string> activeVppRoles)
        {
            var active = new HashSet<string>(
                (activeVppRoles ?? Enumerable.Empty<string>())
                    .Select(NormalizeRole)
                    .Where(role => role != null),
                StringComparer.OrdinalIgnoreCase);
            var changes = new List<string>();
            if (effective == null) return changes;

            foreach (MachineInterventionPoint point in effective.InterventionPoints ??
                     new List<MachineInterventionPoint>())
            {
                if (point?.Enabled != true ||
                    !string.Equals(point.ActionType, "TriggerCamera", StringComparison.OrdinalIgnoreCase))
                    continue;

                string pointRole = NormalizeRole(point.PointCode);
                string signalRole = NormalizeRole(point.SignalCode);
                bool conflict = pointRole != null && signalRole != null &&
                    !string.Equals(pointRole, signalRole, StringComparison.OrdinalIgnoreCase);
                string role = pointRole ?? signalRole;
                if (!conflict && role != null && active.Contains(role)) continue;

                point.Enabled = false;
                changes.Add($"point={point.PointCode}|signal={point.SignalCode}|role={role ?? "unknown"}" +
                            $"|reason={(conflict ? "role-signal-conflict" : role == null ? "unresolved-role" : "camera-absent")}");
            }

            foreach (KeyValuePair<string, MultiShotTriggerOptions> profile in
                     effective.MachineMultiShotTrigger?.EnumerateProfiles() ??
                     Enumerable.Empty<KeyValuePair<string, MultiShotTriggerOptions>>())
            {
                MultiShotTriggerOptions options = profile.Value;
                if (options?.Enabled != true) continue;

                string configuredRole = NormalizeRole(options.CameraRole);
                string outputRole = NormalizeRole(options.TriggerOutputName);
                bool conflict = configuredRole != null && outputRole != null &&
                    !string.Equals(configuredRole, outputRole, StringComparison.OrdinalIgnoreCase);
                string role = configuredRole ?? outputRole;
                if (!conflict && role != null && active.Contains(role)) continue;

                options.Enabled = false;
                changes.Add($"multishot={profile.Key}|output={options.TriggerOutputName}|role={role ?? "unknown"}" +
                            $"|reason={(conflict ? "role-output-conflict" : role == null ? "unresolved-role" : "camera-absent")}");
            }

            return changes;
        }
    }
}
