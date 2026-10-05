using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace QtisVisionPanel.Models
{
    /// <summary>
    /// Canonical camera entry loaded from CameraConfig.xml.
    ///
    /// The current production baseline still lets the VPP define the real job set,
    /// but CameraConfig.xml remains the source of truth for the semantic role of
    /// each configured camera (Top, Side, Front, ...).
    /// </summary>
    public class CameraModel
    {
        /// <summary>
        /// Runtime role used by acquisition and inspection orchestration.
        /// RIGHT and REAR retain separate physical identities.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Semantic role shown by the HMI. Unlike <see cref="Type"/>, this keeps
        /// Left and Right distinct so each physical camera has its own panel.
        /// </summary>
        public string DisplayType { get; set; }

        public int Id { get; set; }
    }

    /// <summary>
    /// Shared helper used by both runtime orchestration and UI container code to
    /// interpret camera-role configuration consistently.
    ///
    /// Current baseline rule:
    /// - if the active VPP job name already exposes a semantic role (Top, Front, ...)
    ///   that role wins
    /// - CameraConfig.xml remains the fallback semantic source when the VPP job name
    ///   is generic or not descriptive enough
    /// </summary>
    public static class CameraConfigurationHelper
    {
        private static readonly HashSet<string> SupportedCameraTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "top",
            "top3d",
            "side",
            "left",
            "front",
            "right",
            "rear",
            "bottom"
        };

        public static IReadOnlyList<CameraModel> LoadConfiguredCameras(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<CameraModel>();
            }

            try
            {
                var document = XDocument.Load(path);
                return document.Descendants("Camera")
                    .Select(camera =>
                    {
                        string configuredType = camera.Attribute("type")?.Value;
                        return new CameraModel
                        {
                            Type = NormalizeCameraType(configuredType),
                            DisplayType = NormalizeCameraDisplayType(configuredType),
                            Id = int.Parse(camera.Attribute("id")?.Value ?? "0")
                        };
                    })
                    .Where(camera => !string.IsNullOrWhiteSpace(camera.Type) && SupportedCameraTypes.Contains(camera.Type))
                    .OrderBy(camera => camera.Id)
                    .ToList();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Impossibile leggere CameraConfig.xml da '{path}': {ex.Message}");
                return Array.Empty<CameraModel>();
            }
        }

        public static Dictionary<int, string> BuildRoleMapping(string path, IDictionary<int, string> jobMapping = null)
        {
            var configuredCameras = LoadConfiguredCameras(path);
            var configuredRolesById = configuredCameras.ToDictionary(camera => camera.Id, camera => camera.Type, EqualityComparer<int>.Default);

            if (configuredRolesById.Count == 0 && (jobMapping == null || jobMapping.Count == 0))
            {
                return new Dictionary<int, string>();
            }

            if (jobMapping == null || jobMapping.Count == 0)
            {
                return configuredRolesById;
            }

            var resolved = new Dictionary<int, string>();

            foreach (var job in jobMapping.OrderBy(entry => entry.Key))
            {
                string resolvedRole = NormalizeCameraType(job.Value);
                bool resolvedFromJobName = IsSupportedCameraRole(resolvedRole);

                if (!resolvedFromJobName)
                {
                    string roleFromSerial = ResolveCameraRoleFromRuntimeSerial(job.Key);
                    if (IsSupportedCameraRole(roleFromSerial))
                    {
                        resolvedRole = NormalizeCameraType(roleFromSerial);
                        resolvedFromJobName = true;

                        MainWindow.logger?.Info(
                            $"Camera role resolved by serial for job {job.Key} ('{job.Value}'): '{resolvedRole}' (serial {GetRuntimeJobSerial(job.Key) ?? "n/a"}).");
                    }
                }

                if (!resolvedFromJobName && configuredRolesById.TryGetValue(job.Key, out string configuredRole))
                {
                    resolvedRole = NormalizeCameraType(configuredRole);
                }

                if (!IsSupportedCameraRole(resolvedRole))
                {
                    continue;
                }

                resolved[job.Key] = resolvedRole;

                if (resolvedFromJobName &&
                    configuredRolesById.TryGetValue(job.Key, out string roleFromConfigId) &&
                    !string.Equals(NormalizeCameraType(roleFromConfigId), resolvedRole, StringComparison.OrdinalIgnoreCase))
                {
                    MainWindow.logger?.Info(
                        $"Camera role override da QuickBuild per job {job.Key} ('{job.Value}'): '{NormalizeCameraType(roleFromConfigId)}' -> '{resolvedRole}'.");
                }
            }

            return resolved;
        }

        public static Dictionary<string, string> BuildConfiguredSerialMapping()
        {
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var configuration = MainWindow.configManager?.Config?.Configuration;
                if (configuration == null)
                {
                    return mapping;
                }

                AddConfiguredSerial(mapping, "top", configuration.TopCameraSerial);
                AddConfiguredSerial(mapping, "side", configuration.SideCameraSerial);
                AddConfiguredSerial(mapping, "front", configuration.FrontCameraSerial);
                AddConfiguredSerial(mapping, "rear", configuration.RearCameraSerial);
                AddConfiguredSerial(mapping, "right", configuration.RightCameraSerial);
                AddConfiguredSerial(mapping, "bottom", configuration.BottomCameraSerial);
                AddConfiguredSerial(mapping, "top3d", configuration.TopCameraSerial);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Impossibile leggere la mappatura seriali da Config.xml: {ex.Message}");
            }

            return mapping;
        }

        public static string GetConfiguredSerialForRole(string role)
        {
            string normalizedRole = NormalizeCameraType(role);
            if (string.IsNullOrWhiteSpace(normalizedRole))
            {
                return null;
            }

            var configuredSerials = BuildConfiguredSerialMapping();
            return configuredSerials.TryGetValue(normalizedRole, out string serial)
                ? NormalizeSerial(serial)
                : null;
        }

        public static string GetRuntimeJobSerial(int jobId)
        {
            try
            {
                var job = MainWindow._cognexManager?.GetJob(jobId);
                return NormalizeSerial(job?.AcqFifo?.FrameGrabber?.SerialNumber);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Impossibile leggere il seriale runtime del job {jobId}: {ex.Message}");
                return null;
            }
        }

        public static string ResolveCameraRoleFromRuntimeSerial(int jobId)
        {
            string runtimeSerial = GetRuntimeJobSerial(jobId);
            if (string.IsNullOrWhiteSpace(runtimeSerial))
            {
                return string.Empty;
            }

            var configuredSerials = BuildConfiguredSerialMapping()
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .GroupBy(entry => NormalizeSerial(entry.Value), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(entry => NormalizeCameraType(entry.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            if (!configuredSerials.TryGetValue(runtimeSerial, out List<string> matchingRoles) ||
                matchingRoles == null ||
                matchingRoles.Count != 1)
            {
                return string.Empty;
            }

            return NormalizeCameraType(matchingRoles[0]);
        }

        public static string NormalizeCameraType(string rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType))
            {
                return string.Empty;
            }

            string normalized = rawType.Trim().ToLowerInvariant();
            if (normalized.Contains("top3d") ||
                normalized.Contains("top_3d") ||
                normalized.Contains("top-3d") ||
                (normalized.Contains("top") && normalized.Contains("3d")) ||
                normalized.Contains("profilomet"))
            {
                return "top3d";
            }

            if (normalized.Contains("left") ||
                normalized.Contains("sinistra") ||
                normalized.Contains("laterale_sx") ||
                normalized.Contains("sideleft") ||
                normalized.Contains("side_left") ||
                normalized.Contains("side-left"))
            {
                return "left";
            }

            if (ContainsRightKeyword(normalized))
            {
                return "right";
            }

            foreach (string supportedRole in SupportedCameraTypes)
            {
                if (normalized.Contains(supportedRole))
                {
                    return supportedRole;
                }
            }

            return normalized;
        }

        /// <summary>
        /// Normalizes a role for UI selection without changing the established
        /// runtime aliases. In particular, Right remains a dedicated display
        /// while retaining the VPP job label for the operator.
        /// </summary>
        public static string NormalizeCameraDisplayType(string rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType))
            {
                return string.Empty;
            }

            string normalized = rawType.Trim().ToLowerInvariant();
            if (normalized.Contains("top3d") ||
                normalized.Contains("top_3d") ||
                normalized.Contains("top-3d") ||
                (normalized.Contains("top") && normalized.Contains("3d")) ||
                normalized.Contains("profilomet"))
            {
                return "top3d";
            }

            if (normalized.Contains("left") ||
                normalized.Contains("sinistra") ||
                normalized.Contains("laterale_sx") ||
                normalized.Contains("sideleft") ||
                normalized.Contains("side_left") ||
                normalized.Contains("side-left"))
            {
                return "left";
            }

            if (ContainsRightKeyword(normalized))
            {
                return "right";
            }

            string legacyRole = NormalizeCameraType(rawType);
            if (string.Equals(legacyRole, "side", StringComparison.OrdinalIgnoreCase)) return "left";
            return legacyRole;
        }

        private static bool ContainsRightKeyword(string normalized)
        {
            return normalized.Contains("right") ||
                   normalized.Contains("destra") ||
                   normalized.Contains("laterale_dx") ||
                   normalized.Contains("sideright") ||
                   normalized.Contains("side_right") ||
                   normalized.Contains("side-right");
        }

        /// <summary>
        /// Etichetta operatore di una vista: il nome della camera come appare nel VPP
        /// (SIDE, REAR, LEFT, RIGHT, ...), non il ruolo fisico interno. Un job Side viene
        /// gestito a runtime come camera "left"; Rear e Right restano separati. Senza VPP caricato
        /// restituisce il nome del ruolo richiesto.
        /// </summary>
        public static string GetCameraRoleDisplayLabel(string role)
        {
            string requested = NormalizeCameraType(role);
            if (string.IsNullOrWhiteSpace(requested))
            {
                return string.Empty;
            }

            var labels = new List<string>();
            try
            {
                Dictionary<int, string> roleMapping = MainWindow.JobRoleMapping;
                Dictionary<int, string> jobMapping = MainWindow.JobMapping;
                if (roleMapping != null)
                {
                    foreach (KeyValuePair<int, string> entry in roleMapping.OrderBy(item => item.Key).ToList())
                    {
                        if (!IsSameCameraView(entry.Value, requested))
                        {
                            continue;
                        }

                        string jobName = null;
                        jobMapping?.TryGetValue(entry.Key, out jobName);
                        string label = DescribeCameraLabel(jobName, entry.Value);
                        if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                        {
                            labels.Add(label);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn(ex, $"CAMERA_LABEL_RESOLVE_FAILED|role={role}");
            }

            return labels.Count > 0
                ? string.Join(" / ", labels)
                : DescribeCameraLabel(role, role);
        }

        private static bool IsSameCameraView(string mappedRole, string requestedRole)
        {
            bool mappedIs3D = string.Equals(NormalizeCameraType(mappedRole), "top3d", StringComparison.OrdinalIgnoreCase);
            bool requestedIs3D = string.Equals(requestedRole, "top3d", StringComparison.OrdinalIgnoreCase);
            if (mappedIs3D || requestedIs3D)
            {
                return mappedIs3D && requestedIs3D;
            }

            return string.Equals(
                NormalizePhysicalCameraRole(mappedRole),
                NormalizePhysicalCameraRole(requestedRole),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeCameraLabel(string jobName, string mappedRole)
        {
            // Il nome del job vince sul ruolo risolto (seriale o CameraConfig.xml) quando
            // contiene gia' un ruolo riconoscibile.
            string source = IsSupportedCameraRole(jobName) ? jobName : mappedRole;
            string labelRole = NormalizeCameraType(source);
            return string.Equals(labelRole, "top3d", StringComparison.OrdinalIgnoreCase)
                ? "TOP 3D"
                : (labelRole ?? string.Empty).ToUpperInvariant();
        }

        /// <summary>
        /// Identita' della camera fisica nel nuovo flusso a quattro viste. I nomi
        /// SIDE e' l'alias storico di LEFT; RIGHT e REAR sono viste fisiche distinte.
        /// </summary>
        public static string NormalizePhysicalCameraRole(string rawType)
        {
            string role = NormalizeCameraType(rawType);
            if (string.Equals(role, "side", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "left", StringComparison.OrdinalIgnoreCase)) return "left";
            if (string.Equals(role, "right", StringComparison.OrdinalIgnoreCase)) return "right";
            if (string.Equals(role, "rear", StringComparison.OrdinalIgnoreCase)) return "rear";
            if (string.Equals(role, "top3d", StringComparison.OrdinalIgnoreCase)) return "top";
            return role;
        }

        public static bool IsSupportedCameraRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            return SupportedCameraTypes.Contains(NormalizeCameraType(role));
        }

        public static bool HasCameraRole(IDictionary<int, string> roleMapping, string role)
        {
            if (roleMapping == null || roleMapping.Count == 0 || string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            // Top3D e' una modalita' di ispezione distinta di Top: per la matrice
            // delle feature non basta la sola presenza della camera Top 2D.
            if (string.Equals(NormalizeCameraType(role), "top3d", StringComparison.OrdinalIgnoreCase))
            {
                return roleMapping.Values.Any(value =>
                    string.Equals(NormalizeCameraType(value), "top3d", StringComparison.OrdinalIgnoreCase));
            }

            string normalizedRole = NormalizePhysicalCameraRole(role);
            return roleMapping.Values.Any(value =>
                string.Equals(NormalizePhysicalCameraRole(value), normalizedRole, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddConfiguredSerial(IDictionary<string, string> mapping, string role, string serial)
        {
            string normalizedRole = NormalizeCameraType(role);
            string normalizedSerial = NormalizeSerial(serial);

            if (string.IsNullOrWhiteSpace(normalizedRole) || string.IsNullOrWhiteSpace(normalizedSerial))
            {
                return;
            }

            mapping[normalizedRole] = normalizedSerial;
        }

        private static string NormalizeSerial(string serial)
        {
            return string.IsNullOrWhiteSpace(serial)
                ? null
                : serial.Trim();
        }
    }
}
