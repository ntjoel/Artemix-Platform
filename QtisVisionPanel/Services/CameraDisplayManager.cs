using Cognex.Vision;
using Cognex.VisionPro;
using Cognex.VisionPro.Display;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using NLog;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Models;
using QtisVisionPanel.Views.UserControls.DisplayRecord;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Centralizes camera display management: updating Cognex record displays,
    /// refreshing feature-visibility indicators, resolving camera roles, and
    /// providing the current top-camera image for recipe auto-switching.
    ///
    /// Extracted from MainWindow to separate UI display concerns from the main
    /// orchestration loop. All access to MainWindow global state is explicit
    /// (MainWindow._topJob, etc.) and read-only except for the three display-record
    /// fields (_topDisplaySaveRecord, _sideDisplaySaveRecord, _frontDisplaySaveRecord)
    /// which remain public static on MainWindow for compatibility with ISaveImage.
    /// </summary>
    public class CameraDisplayManager
    {
        private readonly Dispatcher _dispatcher;
        private readonly Func<bool> _shouldAcceptResults;
        private readonly Action<CogJob, ICogRunStatus> _registerRunStatusIssue;
        private readonly Func<CogJob, int> _getJobIdForJob;
        private readonly ConcurrentDictionary<string, long> _latestDisplaySequenceByRole =
            new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // Lazy: safe if CameraDisplayManager is constructed before MainWindow.logger is assigned.
        private Logger _logger => MainWindow.logger;

        public CameraDisplayManager(
            Dispatcher dispatcher,
            Func<bool> shouldAcceptResults,
            Action<CogJob, ICogRunStatus> registerRunStatusIssue,
            Func<CogJob, int> getJobIdForJob)
        {
            _dispatcher = dispatcher ?? throw new System.ArgumentNullException(nameof(dispatcher));
            _shouldAcceptResults = shouldAcceptResults ?? throw new System.ArgumentNullException(nameof(shouldAcceptResults));
            _registerRunStatusIssue = registerRunStatusIssue ?? throw new System.ArgumentNullException(nameof(registerRunStatusIssue));
            _getJobIdForJob = getJobIdForJob ?? throw new System.ArgumentNullException(nameof(getJobIdForJob));
        }

        // -----------------------------------------------------------------------
        // Role & Display Resolution
        // -----------------------------------------------------------------------

        private static string NormalizeDisplayRole(string role)
        {
            if (string.Equals(role?.Trim(), "top2d", StringComparison.OrdinalIgnoreCase))
            {
                return "top2d";
            }

            return CameraConfigurationHelper.NormalizeCameraDisplayType(role);
        }

        private static bool IsActiveRightJob()
        {
            return string.Equals(
                CameraConfigurationHelper.NormalizeCameraDisplayType(MainWindow._rightJob?.Name),
                "right",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsActiveLeftJob()
        {
            return string.Equals(
                CameraConfigurationHelper.NormalizeCameraDisplayType(MainWindow._sideJob?.Name),
                "left",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveActiveRearDisplayRole()
        {
            return "rear";
        }

        /// <summary>
        /// Resolves the physical camera role from a Cognex job ID. Legacy job names
        /// Side is a legacy name for Left; Right and Rear remain distinct.
        /// by consulting the runtime JobMapping and JobRoleMapping dictionaries.
        /// </summary>
        public string ResolveCameraRole(int jobId)
        {
            if (MainWindow.JobMapping != null && MainWindow.JobMapping.TryGetValue(jobId, out string jobNameFromRuntime))
            {
                string roleFromJobName = CameraConfigurationHelper.NormalizeCameraType(jobNameFromRuntime);
                if (CameraConfigurationHelper.IsSupportedCameraRole(roleFromJobName))
                    return CameraConfigurationHelper.NormalizeCameraDisplayType(roleFromJobName);
            }

            string roleFromSerial = CameraConfigurationHelper.ResolveCameraRoleFromRuntimeSerial(jobId);
            if (CameraConfigurationHelper.IsSupportedCameraRole(roleFromSerial))
                return CameraConfigurationHelper.NormalizeCameraDisplayType(roleFromSerial);

            if (MainWindow.JobRoleMapping != null && MainWindow.JobRoleMapping.TryGetValue(jobId, out string configuredRole))
                return CameraConfigurationHelper.NormalizeCameraDisplayType(configuredRole);

            if (MainWindow.JobMapping != null && MainWindow.JobMapping.TryGetValue(jobId, out string jobName))
                return CameraConfigurationHelper.NormalizeCameraDisplayType(jobName);

            return string.Empty;
        }

        /// <summary>
        /// Returns the CogRecordDisplay control associated with a camera role.
        /// </summary>
        public CogRecordDisplay GetDisplayForRole(string role)
        {
            switch (NormalizeDisplayRole(role))
            {
                case "top":
                case "top3d":
                    return TopCameraView.Instance?.CogRecordsDisplay1;
                case "top2d":
                case "side":
                    return SideCameraView.Instance?.CogRecordsDisplay1;
                case "left":
                    return LeftCameraView.Instance?.CogRecordsDisplay1
                        ?? SideCameraView.Instance?.CogRecordsDisplay1;
                case "front":
                    return FrontCameraView.Instance?.CogRecordsDisplay1;
                case "rear":
                    return RearCameraView.Instance?.CogRecordsDisplay1;
                case "right":
                    return RightCameraView.Instance?.CogRecordsDisplay1;
                case "bottom":
                    return BottomCameraView.Instance?.CogRecordsDisplay1;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the VisionPro LastRun record key to display for a camera role,
        /// preferring recipe-level Top3D overrides and falling back to Config.xml.
        /// </summary>
        public string GetLastRunViewForRole(string role)
        {
            var recipeTop3D = MainWindow.ConfigRecipeParam?.Config?.recipeParamTop3D;
            var lastRun = MainWindow.configManager?.Config?.LasRunParam;

            switch (NormalizeDisplayRole(role))
            {
                case "top":
                    return lastRun?.LastRunView1 ?? "LastRun";
                case "top3d":
                    return string.IsNullOrWhiteSpace(recipeTop3D?.Top3DPrimaryLastRunView)
                        ? lastRun?.LastRunView1 ?? "LastRun"
                        : recipeTop3D.Top3DPrimaryLastRunView;
                case "top2d":
                    return string.IsNullOrWhiteSpace(recipeTop3D?.Top3DSecondaryLastRunView)
                        ? lastRun?.LastRunView2 ?? "LastRun"
                        : recipeTop3D.Top3DSecondaryLastRunView;
                case "side":
                case "left":
                    return lastRun?.LastRunView2 ?? "LastRun";
                case "front":
                    return lastRun?.LastRunView3 ?? "LastRun";
                case "rear":
                case "right":
                    return lastRun?.LastRunView3 ?? "LastRun";
                case "bottom":
                    return "LastRun";
                default:
                    return "LastRun";
            }
        }

        // -----------------------------------------------------------------------
        // Feature Visibility
        // -----------------------------------------------------------------------

        /// <summary>
        /// Pushes the current InspectionConfigService feature-enable flags to
        /// each camera view's visibility layer.
        /// </summary>
        public void RefreshCameraFeatureVisibility()
        {
            try
            {
                // Le icone di una vista devono riflettere cio' che QUELLA vista verifica
                // davvero, quindi si usa l'overload per ruolo della matrice vista x ispezione
                // e non il flag globale: altrimenti una camera mostra controlli che non esegue.
                var config = ServiceLocator.InspectionConfigService;

                TopCameraView.Instance?.UpdateFeatureVisibility(new Dictionary<string, bool>
                {
                    { "Logo",           config.IsFeatureEnabled("top", "Logo") },
                    { "PrintCentering", config.IsFeatureEnabled("top", "PrintCentering") },
                    { "OpenFlaps",      config.IsFeatureEnabled("top", "OpenFlaps") },
                    { "ShapeTop",       config.IsFeatureEnabled("top", "ShapeTop") },
                    { "SurfaceCheck",   config.IsFeatureEnabled("top", "SurfaceCheck") }
                });

                SideCameraView.Instance?.UpdateFeatureVisibility(new Dictionary<string, bool>
                {
                    { "Height",        config.IsFeatureEnabled("side", "Height") },
                    { "SealingSide",   config.IsFeatureEnabled("side", "SideSealing") },
                    { "ShapeSide",     config.IsFeatureEnabled("side", "ShapeSide") },
                    { "SideRollCount", config.IsFeatureEnabled("side", "SideRollCount") }
                });

                // La camera fisica Left esegue anche le ispezioni della vista laterale storica:
                // un job "Side" viene mostrato qui, non in SideCameraView.
                LeftCameraView.Instance?.UpdateFeatureVisibility(new Dictionary<string, bool>
                {
                    { "Height",        config.IsFeatureEnabled("left", "Height") },
                    { "SealingSide",   config.IsFeatureEnabled("left", "SideSealing") },
                    { "ShapeSide",     config.IsFeatureEnabled("left", "ShapeSide") },
                    { "SideRollCount", config.IsFeatureEnabled("left", "SideRollCount") }
                });

                RightCameraView.Instance?.UpdateFeatureVisibility(new Dictionary<string, bool>
                {
                    { "SealingSide",   config.IsFeatureEnabled("right", "SideSealing") },
                    { "ShapeSide",     config.IsFeatureEnabled("right", "ShapeSide") },
                    { "SideRollCount", config.IsFeatureEnabled("right", "SideRollCount") }
                });

                // I titoli seguono i job VPP per le viste fisiche LEFT e RIGHT.
                LeftCameraView.Instance?.UpdateCameraTitle(
                    Models.CameraConfigurationHelper.GetCameraRoleDisplayLabel("left"));
                RightCameraView.Instance?.UpdateCameraTitle(
                    Models.CameraConfigurationHelper.GetCameraRoleDisplayLabel("right"));

                // La vista Rear non veniva aggiornata affatto: le sue icone restavano visibili
                // anche con le relative ispezioni disabilitate per quella vista.
                RearCameraView.Instance?.UpdateFeatureVisibility(new Dictionary<string, bool>
                {
                    { "OpenFlaps",   config.IsFeatureEnabled("rear", "OpenFlaps") },
                    { "SideSealing", config.IsFeatureEnabled("rear", "SideSealing") },
                    { "ShapeSide",   config.IsFeatureEnabled("rear", "ShapeSide") }
                });

                FrontCameraView.Instance?.UpdateTraceabilityVisibility(
                    config.IsFeatureEnabled("front", "FrontTraceability"));

                BottomCameraView.Instance?.UpdateFeatureVisibility(new Dictionary<string, bool>
                {
                    { "BottomSealing", config.IsFeatureEnabled("bottom", "BottomSealing") },
                    { "TrappedPaper",  config.IsFeatureEnabled("bottom", "TrappedPaper") }
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"Unable to refresh camera feature visibility: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Status Indicators
        // -----------------------------------------------------------------------

        /// <summary>
        /// Porta tutte le card feature allo stesso stato. Usato per il reset a neutro.
        /// Da chiamare sul thread UI.
        /// </summary>
        public void ApplyOutcomeBackgroundToAllViews(bool? isGood)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(
                    new Action(() => ApplyOutcomeBackgroundToAllViews(isGood)),
                    System.Windows.Threading.DispatcherPriority.DataBind);
                return;
            }

            CameraOutcomeBackground.Apply(TopCameraView.Instance?.FeatureCardBorder, isGood);
            CameraOutcomeBackground.Apply(SideCameraView.Instance?.FeatureCardBorder, isGood);
            CameraOutcomeBackground.Apply(LeftCameraView.Instance?.FeatureCardBorder, isGood);
            CameraOutcomeBackground.Apply(RightCameraView.Instance?.FeatureCardBorder, isGood);
            CameraOutcomeBackground.Apply(RearCameraView.Instance?.FeatureCardBorder, isGood);
            CameraOutcomeBackground.Apply(FrontCameraView.Instance?.FeatureCardBorder, isGood);
            CameraOutcomeBackground.Apply(BottomCameraView.Instance?.FeatureCardBorder, isGood);
        }

        /// <summary>
        /// Colora la card di ogni vista con l'esito di QUELLA vista: l'operatore vede
        /// subito quale camera ha rilevato il difetto, non solo che il pezzo e' scarto.
        /// Da chiamare sul thread UI.
        /// </summary>
        private void ApplyPerViewOutcomeBackground(InspectionResult inspectionResult)
        {
            CameraOutcomeBackground.Apply(
                TopCameraView.Instance?.FeatureCardBorder,
                ResolveViewOutcome(inspectionResult, "top", inspectionResult?.TopResult, inspectionResult?.TopClassification));

            CameraOutcomeBackground.Apply(SideCameraView.Instance?.FeatureCardBorder, null);
            CameraOutcomeBackground.Apply(
                LeftCameraView.Instance?.FeatureCardBorder,
                ResolveViewOutcome(inspectionResult, "left", inspectionResult?.SideResult, inspectionResult?.SideClassification));

            CameraOutcomeBackground.Apply(
                RearCameraView.Instance?.FeatureCardBorder,
                ResolveViewOutcome(inspectionResult, "rear", inspectionResult?.RearResult, inspectionResult?.RearClassification));
            CameraOutcomeBackground.Apply(
                RightCameraView.Instance?.FeatureCardBorder,
                ResolveViewOutcome(inspectionResult, "right", inspectionResult?.RightResult, inspectionResult?.RightClassification));

            CameraOutcomeBackground.Apply(
                FrontCameraView.Instance?.FeatureCardBorder,
                ResolveViewOutcome(inspectionResult, "front", inspectionResult?.FrontResult, inspectionResult?.FrontClassification));

            CameraOutcomeBackground.Apply(
                BottomCameraView.Instance?.FeatureCardBorder,
                ResolveViewOutcome(inspectionResult, "bottom", inspectionResult?.BottomResult, inspectionResult?.BottomClassification));
        }

        /// <returns>
        /// true conforme, false difetto rilevato da questa vista, null nessun esito
        /// attendibile: vista non partecipante al pezzo oppure non classificabile.
        /// </returns>
        private static bool? ResolveViewOutcome(
            InspectionResult inspectionResult,
            string cameraRole,
            ValidationResult validation,
            CameraClassificationResult classification)
        {
            if (inspectionResult == null)
            {
                return null;
            }

            // Una vista che VisionPro non ha saputo classificare non deve mostrare verde
            // ne' rosso: nessuno dei due sarebbe vero.
            if (inspectionResult.UnclassifiedCameraRoles != null &&
                inspectionResult.UnclassifiedCameraRoles.Any(
                    role => string.Equals(
                        CameraConfigurationHelper.NormalizePhysicalCameraRole(role),
                        CameraConfigurationHelper.NormalizePhysicalCameraRole(cameraRole),
                        StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (validation == null && classification == null)
            {
                return null; // la vista non partecipa a questo pezzo
            }

            bool featuresOk = validation?.IsValid ?? true;

            // Informational: la classificazione e' solo indicativa per ricetta e non
            // concorre all'esito, quindi non deve tingere di rosso la card.
            bool classificationOk = classification == null ||
                                    classification.State == CameraClassificationState.Passed ||
                                    classification.State == CameraClassificationState.Informational;

            return featuresOk && classificationOk;
        }

        /// <summary>
        /// Updates the pass/fail status indicator lights on each camera view
        /// based on the completed inspection result.
        /// </summary>
        public void UpdateCameraStatusIndicators(InspectionResult inspectionResult)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(
                    new Action(() => UpdateCameraStatusIndicators(inspectionResult)),
                    System.Windows.Threading.DispatcherPriority.DataBind);
                return;
            }

            try
            {
                RefreshCameraFeatureVisibility();

                // Esito per singola vista: da lontano si legge lo stato del pezzo, da vicino
                // quale camera ha rilevato il difetto. Un pezzo non classificabile porta
                // tutte le card a neutro perche' nessun esito e' attendibile.
                if (inspectionResult == null || inspectionResult.IsUnclassified)
                {
                    ApplyOutcomeBackgroundToAllViews(null);
                }
                else
                {
                    ApplyPerViewOutcomeBackground(inspectionResult);
                }

                UpdateClassificationSummaries(inspectionResult);

                if (inspectionResult?.IsUnclassified == true)
                {
                    // Never paint stale feature values as GOOD when VisionPro could
                    // not produce a trustworthy result for the current piece.
                    TopCameraView.Instance?.ResetAllStatusIndicators();
                    SideCameraView.Instance?.ResetAllStatusIndicators();
                    LeftCameraView.Instance?.ResetAllStatusIndicators();
                    FrontCameraView.Instance?.ResetTraceabilityStatus();
                    BottomCameraView.Instance?.ResetAllStatusIndicators();
                    RightCameraView.Instance?.ResetAllStatusIndicators();
                    return;
                }

                var topResults = new Dictionary<string, bool?>();
                if (inspectionResult.TopResult != null)
                {
                    bool isTop3DActive = string.Equals(
                        MainWindow._topJob != null
                            ? ResolveCameraRole(_getJobIdForJob(MainWindow._topJob))
                            : string.Empty,
                        "top3d",
                        StringComparison.OrdinalIgnoreCase);

                    if (isTop3DActive)
                    {
                        TopCameraView.Instance?.ResetAllStatusIndicators();
                        TopCameraView.Instance?.UpdateThreeDMeasurementSummary(
                            ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDHeight", "3D height"),
                            BuildThreeDMeasurementSummary(
                                MainWindow._produzioneRecord?.ThreeDHeightMeasureValue,
                                MainWindow._produzioneRecord?.ThreeDHeightNominalValue,
                                MainWindow._produzioneRecord?.ThreeDHeightMinimum,
                                MainWindow._produzioneRecord?.ThreeDHeightMaximum,
                                MainWindow.configManager?.Config?.Configuration?.Top3DHeightMeasurementOffsetMm ?? 0),
                            ConvertNcFlagToToleranceState(MainWindow._produzioneRecord?.NCThreeDHeight));

                        ProcessMonitoringResult profileResult = null;
                        inspectionResult.TopResult.MonitoringResults?.TryGetValue("ThreeDProfile", out profileResult);
                        if (profileResult != null)
                        {
                            TopCameraView.Instance?.UpdateThreeDProfileSummary(
                                ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                                    "Sub_entry_Top3DProfile",
                                    "Top3D profile"),
                                BuildTopThreeDProfileDisplaySummary(),
                                profileResult.State);
                        }
                        else
                        {
                            TopCameraView.Instance?.ClearThreeDProfileSummary();
                        }
                    }
                    else
                    {
                        TopCameraView.Instance?.ClearThreeDMeasurementSummary();
                    }

                    var topErrors = (inspectionResult.TopResult.ErrorMessages ?? new List<string>())
                        .Where(error => !IsAiClassificationError(error))
                        .ToList();
                    bool isLogoEnabled          = ServiceLocator.InspectionConfigService.IsFeatureEnabled("Logo");
                    bool isPrintCenteringEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("PrintCentering");
                    bool isOpenFlapsEnabled      = ServiceLocator.InspectionConfigService.IsFeatureEnabled("OpenFlaps");
                    bool isSurfaceCheckEnabled   = ServiceLocator.InspectionConfigService.IsFeatureEnabled("SurfaceCheck");
                    bool isShapeTopEnabled       = ServiceLocator.InspectionConfigService.IsFeatureEnabled("ShapeTop");

                    topResults["Logo"] = isLogoEnabled
                        ? !topErrors.Any(e => e.Contains("Logo"))
                        : (bool?)null;
                    topResults["PrintCentering"] = isPrintCenteringEnabled
                        ? !topErrors.Any(e => e.Contains("Print centering"))
                        : (bool?)null;
                    topResults["OpenFlaps"] = isOpenFlapsEnabled
                        ? !topErrors.Any(e => e.Contains("Open flaps"))
                        : (bool?)null;
                    topResults["SurfaceCheck"] = isSurfaceCheckEnabled
                        ? !topErrors.Any(e => e.Contains("Surface"))
                        : (bool?)null;
                    topResults["ShapeTop"] = isShapeTopEnabled
                        ? !topErrors.Any(e => e.Contains("Shape top"))
                        : (bool?)null;
                }

                var sideResults = new Dictionary<string, bool?>();
                bool isTop3DViewMode = string.Equals(
                    MainWindow._topJob != null
                        ? ResolveCameraRole(_getJobIdForJob(MainWindow._topJob))
                        : string.Empty,
                    "top3d",
                    StringComparison.OrdinalIgnoreCase);

                if (isTop3DViewMode)
                {
                    SideCameraView.Instance?.ResetAllStatusIndicators();
                    SideCameraView.Instance?.UpdateThreeDMeasurementSummary(
                        (
                            ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDWidth", "3D width"),
                            BuildThreeDMeasurementSummary(
                                MainWindow._produzioneRecord?.ThreeDWidthMeasureValue,
                                MainWindow._produzioneRecord?.ThreeDWidthNominalValue,
                                MainWindow._produzioneRecord?.ThreeDWidthMinimum,
                                MainWindow._produzioneRecord?.ThreeDWidthMaximum,
                                MainWindow.configManager?.Config?.Configuration?.Top3DWidthMeasurementOffsetMm ?? 0),
                            ConvertNcFlagToToleranceState(MainWindow._produzioneRecord?.NCThreeDWidth)),
                        (
                            ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDLength", "3D length"),
                            BuildThreeDMeasurementSummary(
                                MainWindow._produzioneRecord?.ThreeDLengthMeasureValue,
                                MainWindow._produzioneRecord?.ThreeDLengthNominalValue,
                                MainWindow._produzioneRecord?.ThreeDLengthMinimum,
                                MainWindow._produzioneRecord?.ThreeDLengthMaximum,
                                MainWindow.configManager?.Config?.Configuration?.Top3DLengthMeasurementOffsetMm ?? 0),
                            ConvertNcFlagToToleranceState(MainWindow._produzioneRecord?.NCThreeDLength)));
                }
                else
                {
                    SideCameraView.Instance?.ClearThreeDMeasurementSummary();
                }

                if (inspectionResult.SideResult != null && !isTop3DViewMode)
                {
                    var sideErrors = (inspectionResult.SideResult.ErrorMessages ?? new List<string>())
                        .Where(error => !IsAiClassificationError(error))
                        .ToList();
                    bool isHeightEnabled      = ServiceLocator.InspectionConfigService.IsFeatureEnabled("Height");
                    bool isSideSealingEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("SideSealing");
                    bool isShapeSideEnabled   = ServiceLocator.InspectionConfigService.IsFeatureEnabled("ShapeSide");
                    bool isSideRollCountEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("SideRollCount");

                    sideResults["Height"] = isHeightEnabled
                        ? !sideErrors.Any(e => e.Contains("Height"))
                        : (bool?)null;
                    sideResults["SealingSide"] = isSideSealingEnabled
                        ? !sideErrors.Any(e => e.Contains("Sealing"))
                        : (bool?)null;
                    sideResults["ShapeSide"] = isShapeSideEnabled
                        ? !sideErrors.Any(e => e.Contains("Shape side"))
                        : (bool?)null;
                    sideResults["SideRollCount"] = isSideRollCountEnabled
                        ? !sideErrors.Any(e => e.Contains("Roll count"))
                        : (bool?)null;
                }

                TopCameraView.Instance?.UpdateStatusIndicators(topResults);
                if (IsActiveLeftJob())
                {
                    LeftCameraView.Instance?.UpdateStatusIndicators(sideResults);
                }
                else
                {
                    SideCameraView.Instance?.UpdateStatusIndicators(sideResults);
                }

                if (inspectionResult.FrontResult != null && FrontCameraView.Instance != null)
                {
                    var frontErrors = (inspectionResult.FrontResult.ErrorMessages ?? new List<string>())
                        .Where(error => !IsAiClassificationError(error))
                        .ToList();
                    bool isTraceabilityEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("FrontTraceability");
                    bool? traceabilityValid = isTraceabilityEnabled
                        ? !frontErrors.Any(error =>
                            error.IndexOf("traceability", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            error.IndexOf("tracciabil", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            error.IndexOf("code", StringComparison.OrdinalIgnoreCase) >= 0)
                        : (bool?)null;
                    bool? traceabilityDetected = MainWindow._produzioneRecord?.TraceabilityDetected.HasValue == true
                        ? MainWindow._produzioneRecord.TraceabilityDetected == 1
                        : (bool?)null;

                    FrontCameraView.Instance.UpdateTraceabilityStatus(
                        traceabilityDetected,
                        MainWindow._produzioneRecord?.TraceabilityCode,
                        MainWindow._produzioneRecord?.TraceabilityExpectedPrefix,
                        traceabilityValid);
                }

                if (inspectionResult.BottomResult != null && BottomCameraView.Instance != null)
                {
                    bool isBottomSealingEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("BottomSealing");
                    bool isTrappedPaperEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("TrappedPaper");
                    var bottomErrors = (inspectionResult.BottomResult.ErrorMessages ?? new List<string>())
                        .Where(error => !IsAiClassificationError(error))
                        .ToList();

                    BottomCameraView.Instance.UpdateStatusIndicators(new Dictionary<string, bool?>
                    {
                        {
                            "BottomSealing",
                            isBottomSealingEnabled
                                ? !bottomErrors.Any(e => e.IndexOf("bottom sealing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         e.IndexOf("bottom seal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         e.IndexOf("saldatura inferiore", StringComparison.OrdinalIgnoreCase) >= 0)
                                : (bool?)null
                        },
                        {
                            "TrappedPaper",
                            isTrappedPaperEnabled
                                ? !bottomErrors.Any(e => e.IndexOf("trapped paper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         e.IndexOf("paper trapped", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         e.IndexOf("carta intrappolata", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         e.IndexOf("carta in saldatura", StringComparison.OrdinalIgnoreCase) >= 0)
                                : (bool?)null
                        }
                    });
                }

                if (inspectionResult.RightResult != null && IsActiveRightJob() && RightCameraView.Instance != null)
                {
                    bool isSideSealingEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("SideSealing");
                    bool isSideRollCountEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("SideRollCount");
                    bool isShapeSideEnabled = ServiceLocator.InspectionConfigService.IsFeatureEnabled("right", "ShapeSide");
                    var rearErrors = (inspectionResult.RightResult.ErrorMessages ?? new List<string>())
                        .Where(error => !IsAiClassificationError(error))
                        .ToList();

                    RightCameraView.Instance.UpdateStatusIndicators(new Dictionary<string, bool?>
                    {
                        {
                            "SealingSide",
                            isSideSealingEnabled
                                ? !rearErrors.Any(e => e.IndexOf("sealing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                       e.IndexOf("saldatura", StringComparison.OrdinalIgnoreCase) >= 0)
                                : (bool?)null
                        },
                        {
                            "ShapeSide",
                            isShapeSideEnabled
                                ? !rearErrors.Any(e => e.IndexOf("shape side", StringComparison.OrdinalIgnoreCase) >= 0)
                                : (bool?)null
                        },
                        {
                            "SideRollCount",
                            isSideRollCountEnabled
                                ? !rearErrors.Any(e => e.IndexOf("roll count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                       e.IndexOf("rotolo", StringComparison.OrdinalIgnoreCase) >= 0)
                                : (bool?)null
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nell'aggiornamento degli indicatori di stato: {ex.Message}");
            }
        }

        private static void UpdateClassificationSummaries(InspectionResult inspectionResult)
        {
            var config = ServiceLocator.InspectionConfigService;
            string topRole = string.IsNullOrWhiteSpace(inspectionResult?.TopCameraRole)
                ? "top"
                : inspectionResult.TopCameraRole;

            TopCameraView.Instance?.ClassificationSummaryCard?.UpdateResult(
                config.IsFeatureEnabled(topRole, "AIClassification")
                    ? inspectionResult?.TopClassification
                    : null);

            bool leftIsActive = IsActiveLeftJob();
            string sideRole = leftIsActive ? "left" : "side";
            CameraClassificationResult sideClassification =
                config.IsFeatureEnabled(sideRole, "AIClassification")
                    ? inspectionResult?.SideClassification
                    : null;
            SideCameraView.Instance?.ClassificationSummaryCard?.UpdateResult(
                leftIsActive ? null : sideClassification);
            LeftCameraView.Instance?.ClassificationSummaryCard?.UpdateResult(
                leftIsActive ? sideClassification : null);

            FrontCameraView.Instance?.ClassificationSummaryCard?.UpdateResult(
                config.IsFeatureEnabled("front", "AIClassification")
                    ? inspectionResult?.FrontClassification
                    : null);
            BottomCameraView.Instance?.ClassificationSummaryCard?.UpdateResult(
                config.IsFeatureEnabled("bottom", "AIClassification")
                    ? inspectionResult?.BottomClassification
                    : null);

            CameraClassificationResult rearClassification =
                config.IsFeatureEnabled("rear", "AIClassification")
                    ? inspectionResult?.RearClassification
                    : null;
            RearCameraView.Instance?.ClassificationSummaryCard?.UpdateResult(
                rearClassification);
            RightCameraView.Instance?.ClassificationSummaryCard?.UpdateResult(
                config.IsFeatureEnabled("right", "AIClassification")
                    ? inspectionResult?.RightClassification
                    : null);
        }

        private static bool IsAiClassificationError(string error)
        {
            return !string.IsNullOrWhiteSpace(error) &&
                   error.IndexOf("AI classification", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool? ConvertNcFlagToToleranceState(int? ncValue)
        {
            if (!ncValue.HasValue)
            {
                return null;
            }

            if (ncValue.Value == 1)
            {
                return true;
            }

            if (ncValue.Value == 0)
            {
                return false;
            }

            return null;
        }

        private static string BuildThreeDMeasurementSummary(
            double? measurementValue,
            double? nominalValue,
            double? minimumValue,
            double? maximumValue,
            double configuredOffsetMm)
        {
            string defaultMeasuredLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Measured", "Measured");
            string correctedLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CorrectedMeasurement", "Corrected");
            string offsetLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_MeasurementOffset", "Offset");
            string targetLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Target", "Target");

            double appliedOffsetMm = TopThreeDMeasurementCorrection.NormalizeOffset(configuredOffsetMm);
            bool correctionActive = Math.Abs(appliedOffsetMm) > 0.0000001;
            string measuredText = measurementValue.HasValue ? $"{measurementValue.Value:0.000} mm" : "--";
            string measuredLabel = correctionActive ? correctedLabel : defaultMeasuredLabel;
            string offsetText = correctionActive
                ? $" | {offsetLabel}: {appliedOffsetMm:+0.000;-0.000;0.000} mm"
                : string.Empty;
            measuredText += offsetText;
            if (!nominalValue.HasValue)
            {
                return $"{measuredLabel}: {measuredText}";
            }

            double toleranceValue = 0;
            if (maximumValue.HasValue)
            {
                toleranceValue = Math.Abs(maximumValue.Value - nominalValue.Value);
            }
            else if (minimumValue.HasValue)
            {
                toleranceValue = Math.Abs(nominalValue.Value - minimumValue.Value);
            }

            return $"{measuredLabel}: {measuredText} | {targetLabel}: {nominalValue.Value:0.000} ± {toleranceValue:0.000} mm";
        }

        private static string BuildTopThreeDProfileDisplaySummary()
        {
            var record = MainWindow._produzioneRecord;
            var recipe = MainWindow.ConfigRecipeParam?.Config?.recipeParamTop3D;

            string medianLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ThreeDHeightMedian",
                "Height median");
            string highTailLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ThreeDHighTail",
                "High tail");
            string maximumLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ThreeDMaximumDiagnostic",
                "Maximum");
            string bulgeLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ThreeDBulge",
                "Bulge index");
            string validPixelsLabel = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_ThreeDValidPixels",
                "Valid pixels");

            string maximumBulge = recipe != null && recipe.ThreeDHeightMaximumBulge > 0
                ? recipe.ThreeDHeightMaximumBulge.ToString("0.###") + " mm"
                : "--";
            string minimumValidPixels = recipe != null
                ? recipe.ThreeDHeightMinimumValidPixelRatio.ToString("P1")
                : "--";

            return
                $"{medianLabel}: {FormatTopThreeDProfileMeasurement(record?.ThreeDHeightMedianValue)} | " +
                $"{highTailLabel}: {FormatTopThreeDProfileMeasurement(record?.ThreeDHeightHighTailValue)} | " +
                $"{maximumLabel}: {FormatTopThreeDProfileMeasurement(record?.ThreeDHeightMaximumValue)} | " +
                $"{bulgeLabel}: {FormatTopThreeDProfileMeasurement(record?.ThreeDHeightBulgeValue)} / {maximumBulge} | " +
                $"{validPixelsLabel}: {FormatTopThreeDProfileRatio(record?.ThreeDHeightValidPixelRatio)} / {minimumValidPixels}";
        }

        private static string FormatTopThreeDProfileMeasurement(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? value.Value.ToString("0.###") + " mm"
                : "--";
        }

        private static string FormatTopThreeDProfileRatio(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? value.Value.ToString("P1")
                : "--";
        }

        // -----------------------------------------------------------------------
        // Image Retrieval
        // -----------------------------------------------------------------------

        /// <summary>
        /// Retrieves the current top-camera ICogImage from the active ToolBlock
        /// inputs or from the latest cached record. Used by RecipeAutoSwitcher.
        /// </summary>
        public ICogImage GetCurrentTopCameraImage()
        {
            try
            {
                if (MainWindow._topToolBlockReults != null &&
                    MainWindow._topToolBlockReults.Inputs.Contains("InputImage"))
                {
                    var input = MainWindow._topToolBlockReults.Inputs["InputImage"];
                    if (input?.Value is ICogImage image)
                        return image;
                }

                if (MainWindow._topRecord != null)
                {
                    var imageRecord = ResolveDisplayRecord(
                                          MainWindow._topRecord,
                                          GetLastRunViewForRole("top3d"),
                                          out _)
                                      ?? ResolveDisplayRecord(
                                          MainWindow._topRecord,
                                          GetLastRunViewForRole("top"),
                                          out _)
                                      ?? TryFindFirstImageRecord(MainWindow._topRecord);

                    if (imageRecord?.Content is ICogImage image)
                        return image;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Errore nel recupero dell'immagine top: {ex.Message}");
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // Display Updates
        // -----------------------------------------------------------------------

        /// <summary>
        /// Updates a single Cognex record display on the UI thread, writing the
        /// display-save record cache and the status text box for the job's view.
        /// </summary>
        internal async Task UpdateDisplayParallelAsync(
            ICogRecord record,
            CogRecordDisplay display,
            string subRecordKey,
            CogJob job,
            ICogRunStatus runStatus,
            string displayRole,
            CancellationToken token,
            CameraResult capturedResult = null)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                if (!_shouldAcceptResults())
                    return;

                if (record == null || display == null || job == null ||
                    (runStatus == null && capturedResult == null) ||
                    string.IsNullOrWhiteSpace(subRecordKey))
                    return;

                string sequencedRole = NormalizeDisplayRole(displayRole);
                long resultSequence = capturedResult?.ResultSequence ?? 0;
                if (resultSequence > 0)
                {
                    long latestSequence = _latestDisplaySequenceByRole.AddOrUpdate(
                        sequencedRole,
                        resultSequence,
                        (_, current) => Math.Max(current, resultSequence));
                    if (latestSequence > resultSequence)
                    {
                        _logger.Debug(
                            $"DISPLAY_STALE_UPDATE_SKIPPED|role={sequencedRole}|sequence={resultSequence}|latest={latestSequence}");
                        return;
                    }
                }

                var (tmpRecord, statusText) = await Task.Run(() =>
                (
                    ResolveDisplayRecord(record, subRecordKey, out _) ?? TryFindFirstImageRecord(record),
                    capturedResult != null
                        ? $"{job.Name} -> Total: {capturedResult.RunTotalTimeMs:0.###}ms | Process: {capturedResult.RunProcessingTimeMs:0.###}ms | Status:{capturedResult.RunStatusMessage}"
                        : $"{job.Name} -> Total: {runStatus.TotalTime}ms | Process: {runStatus.ProcessingTime}ms"
                ), token);

                if (tmpRecord == null)
                {
                    _logger.Warn(
                        $"DISPLAY_RECORD_PATH_NOT_FOUND|role={displayRole}|job={job.Name}|requested='{subRecordKey}'|No displayable record found.");
                    return;
                }

                if (capturedResult == null && !string.IsNullOrEmpty(runStatus.Message) &&
                    runStatus.Result == CogToolResultConstants.Error)
                {
                    _registerRunStatusIssue(job, runStatus);
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    if (!_shouldAcceptResults())
                        return;

                    if (resultSequence > 0 &&
                        _latestDisplaySequenceByRole.TryGetValue(sequencedRole, out long latestSequence) &&
                        latestSequence > resultSequence)
                    {
                        _logger.Debug(
                            $"DISPLAY_STALE_UPDATE_SKIPPED|role={sequencedRole}|sequence={resultSequence}|latest={latestSequence}|stage=dispatcher");
                        return;
                    }

                    string normalizedDisplayRole = NormalizeDisplayRole(displayRole);

                    if (normalizedDisplayRole == "top" || normalizedDisplayRole == "top3d" || (string.IsNullOrWhiteSpace(normalizedDisplayRole) && job == MainWindow._topJob))
                    {
                        if (TopCameraView.Instance != null)
                        {
                            TopCameraView.Instance.ApplyRuntimeRoleLabel(normalizedDisplayRole == "top3d" ? "top3d" : "top");
                            TopCameraView.Instance.RunStatusTextBox.Text = statusText;
                        }

                        MainWindow._topDisplaySaveRecord = tmpRecord;
                    }
                    else if (normalizedDisplayRole == "left")
                    {
                        if (LeftCameraView.Instance != null)
                        {
                            LeftCameraView.Instance.RunStatusTextBox.Text = statusText;
                        }

                        MainWindow._sideDisplaySaveRecord = tmpRecord;
                    }
                    else if (normalizedDisplayRole == "side" || normalizedDisplayRole == "top2d" || (string.IsNullOrWhiteSpace(normalizedDisplayRole) && job == MainWindow._sideJob))
                    {
                        if (SideCameraView.Instance != null)
                        {
                            SideCameraView.Instance.ApplyRuntimeRoleLabel(normalizedDisplayRole == "top2d" ? "top2d" : "side");
                            SideCameraView.Instance.RunStatusTextBox.Text = statusText;
                        }

                        MainWindow._sideDisplaySaveRecord = tmpRecord;
                    }
                    else if (normalizedDisplayRole == "front" || (string.IsNullOrWhiteSpace(normalizedDisplayRole) && job == MainWindow._frontJob))
                    {
                        if (FrontCameraView.Instance != null)
                        {
                            FrontCameraView.Instance.RunStatusTextBox.Text = statusText;
                        }

                        MainWindow._frontDisplaySaveRecord = tmpRecord;
                    }
                    else if (normalizedDisplayRole == "right")
                    {
                        if (RightCameraView.Instance != null)
                        {
                            RightCameraView.Instance.RunStatusTextBox.Text = statusText;
                        }

                        MainWindow._rightDisplaySaveRecord = tmpRecord;
                    }
                    else if (normalizedDisplayRole == "rear" || (string.IsNullOrWhiteSpace(normalizedDisplayRole) && job == MainWindow._rearJob))
                    {
                        if (RearCameraView.Instance != null)
                        {
                            RearCameraView.Instance.RunStatusTextBox.Text = statusText;
                        }

                        MainWindow._rearDisplaySaveRecord = tmpRecord;
                    }
                    else if (normalizedDisplayRole == "bottom" || (string.IsNullOrWhiteSpace(normalizedDisplayRole) && job == MainWindow._bottomJob))
                    {
                        if (BottomCameraView.Instance != null)
                        {
                            BottomCameraView.Instance.RunStatusTextBox.Text = statusText;
                        }

                        MainWindow._bottomDisplaySaveRecord = tmpRecord;
                    }

                    // Prima il record, POI il fit: invertiti, Fit() adattava la vista al record
                    // precedente e il nuovo veniva mostrato con l'inquadratura di quello prima.
                    display.AutoFit = true;
                    display.Record = tmpRecord;
                    display.Fit(true);

                    // La cattura resta un elemento separato della coda UI per mantenere breve
                    // l'aggiornamento del display. Il record radice e' la chiave condivisa con
                    // il salvataggio; tmpRecord e' il sottorecord effettivamente mostrato.
                    var captureDisplayRecord = tmpRecord;
                    var captureResultRecord = record;
                    var captureRole = sequencedRole;
                    var captureDisplay = display;

                    _dispatcher.BeginInvoke(
                        new Action(() => CacheAnnotatedImage(
                            captureRole,
                            captureDisplayRecord,
                            captureResultRecord,
                            captureDisplay)),
                        System.Windows.Threading.DispatcherPriority.Background);

                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (OperationCanceledException e)
            {
                _logger.Warn($"Display update cancelled. {e.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Parallel display update failed for role '{displayRole}', requested record '{subRecordKey}'.");
            }
        }

        /// <summary>
        /// Cattura il JPEG annotato della vista appena aggiornata. PrintWindow conserva la
        /// resa visibile; CreateContentBitmap resta il fallback quando Windows non cattura
        /// l'ActiveX. La chiave e' sempre il record radice dello stesso risultato.
        /// </summary>
        private void CacheAnnotatedImage(
            string role,
            ICogRecord displayRecord,
            ICogRecord resultRecord,
            CogRecordDisplay display)
        {
            if (displayRecord == null || resultRecord == null || display == null)
            {
                _logger.Warn($"ANNOTATED_CACHE_SKIPPED|role={role}|reason=record-or-display-null");
                return;
            }

            try
            {
                // Un aggiornamento successivo potrebbe aver sostituito il record prima di
                // questa callback: non associare mai al pezzo corrente l'immagine seguente.
                if (!object.ReferenceEquals(display.Record, displayRecord))
                {
                    _logger.Warn($"ANNOTATED_CACHE_SKIPPED|role={role}|reason=record-superseded");
                    return;
                }

                string captureSource = "print-window";
                Bitmap bmp = WindowScreenshotHelper.CaptureCogDisplay(display);
                if (bmp == null)
                {
                    captureSource = "cognex-content";
                    string renderFailure;
                    bmp = AnnotatedImageRenderer.CreateContentBitmap(display, out renderFailure);
                    if (bmp == null)
                    {
                        _logger.Warn(
                            $"ANNOTATED_CACHE_FAILED|role={role}|reason=print-window-and-render-failed" +
                            $"|render={renderFailure ?? "bitmap-null"}");
                        return;
                    }
                }

                using (bmp)
                using (var stream = new System.IO.MemoryStream())
                {
                    bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
                    int completedRequests = AnnotatedImageCache.Store(
                        role,
                        resultRecord,
                        stream.ToArray());

                    _logger.Debug(
                        $"ANNOTATED_CACHE_STORED|role={role}|w={bmp.Width}|h={bmp.Height}" +
                        $"|source={captureSource}|completedRequests={completedRequests}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"ANNOTATED_CACHE_FAILED|role={role}|reason=exception");
            }
        }

        private static ICogRecord ResolveDisplayRecord(
            ICogRecord root,
            string subRecordKey,
            out string resolvedRecordPath)
        {
            resolvedRecordPath = null;

            if (root == null || string.IsNullOrWhiteSpace(subRecordKey))
            {
                return null;
            }

            foreach (string candidate in BuildDisplayRecordPathCandidates(subRecordKey))
            {
                var match = ResolveDisplayRecordCandidate(root, candidate, 0);
                if (match != null)
                {
                    resolvedRecordPath = candidate;
                    return match;
                }
            }

            return null;
        }

        private static IEnumerable<string> BuildDisplayRecordPathCandidates(string subRecordKey)
        {
            var candidates = new List<string>();
            AddCandidate(candidates, subRecordKey);

            string[] segments = SplitRecordPath(subRecordKey);
            if (segments.Length > 1)
            {
                AddCandidate(candidates, string.Join(".", segments.Skip(1)));
            }

            if (segments.Length > 2)
            {
                AddCandidate(candidates, string.Join(".", segments.Skip(segments.Length - 2)));
            }

            if (segments.Length > 0 &&
                string.Equals(segments[0], "LastRun", StringComparison.OrdinalIgnoreCase))
            {
                AddCandidate(candidates, string.Join(".", segments.Skip(1)));
            }

            return candidates;
        }

        private static void AddCandidate(ICollection<string> candidates, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            string trimmed = candidate.Trim();
            if (!candidates.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(trimmed);
            }
        }

        private static string[] SplitRecordPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? Array.Empty<string>()
                : path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .ToArray();
        }

        private static ICogRecord ResolveDisplayRecordCandidate(ICogRecord root, string candidate, int depth)
        {
            if (root?.SubRecords == null ||
                string.IsNullOrWhiteSpace(candidate) ||
                depth > 8)
            {
                return null;
            }

            try
            {
                if (root.SubRecords.ContainsKey(candidate))
                {
                    return root.SubRecords[candidate];
                }

                var current = root;
                string[] segments = SplitRecordPath(candidate);
                if (segments.Length > 0)
                {
                    foreach (string segment in segments)
                    {
                        if (current?.SubRecords == null ||
                            !current.SubRecords.ContainsKey(segment))
                        {
                            current = null;
                            break;
                        }

                        current = current.SubRecords[segment];
                    }

                    if (current != null)
                    {
                        return current;
                    }
                }

                foreach (ICogRecord child in root.SubRecords)
                {
                    var nestedMatch = ResolveDisplayRecordCandidate(child, candidate, depth + 1);
                    if (nestedMatch != null)
                    {
                        return nestedMatch;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static ICogRecord TryFindFirstImageRecord(ICogRecord root, int depth = 0)
        {
            if (root == null || depth > 8)
            {
                return null;
            }

            try
            {
                if (root.Content is ICogImage)
                {
                    return root;
                }

                if (root.SubRecords == null)
                {
                    return null;
                }

                foreach (ICogRecord child in root.SubRecords)
                {
                    var imageRecord = TryFindFirstImageRecord(child, depth + 1);
                    if (imageRecord != null)
                    {
                        return imageRecord;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        internal async Task ProcessToolResultsAsyncTop(
            ICogRecord record, CogRecordDisplay view, string subRecordKey, string displayRole, CancellationToken token, CameraResult capturedResult = null)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (record == null) return;
                await UpdateDisplayParallelAsync(
                    record, view, subRecordKey,
                    MainWindow._topJob, MainWindow._topJob.VisionTool.RunStatus, string.IsNullOrWhiteSpace(displayRole) ? "top" : displayRole, token, capturedResult);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Processing cancelled for {MainWindow._topJob?.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing {MainWindow._topJob?.Name} results");
            }
        }

        internal async Task ProcessToolResultsAsyncside(
            ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, string displayRole = "side", CameraResult capturedResult = null)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (record == null) return;
                await UpdateDisplayParallelAsync(
                    record, view, subRecordKey,
                    MainWindow._sideJob, MainWindow._sideJob.VisionTool.RunStatus,
                    string.IsNullOrWhiteSpace(displayRole) ? "side" : displayRole,
                    token,
                    capturedResult);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Processing cancelled for {MainWindow._sideJob?.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing {MainWindow._sideJob?.Name} results");
            }
        }

        internal async Task ProcessToolResultsAsyncFront(
            ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (record == null) return;
                await UpdateDisplayParallelAsync(
                    record, view, subRecordKey,
                    MainWindow._frontJob, MainWindow._frontJob?.VisionTool?.RunStatus, "front", token, capturedResult);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Processing cancelled for {MainWindow._frontJob?.Name ?? "Front"}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing {MainWindow._frontJob?.Name ?? "Front"} results");
            }
        }

        internal async Task ProcessToolResultsAsyncTopSecondary(
            ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (record == null) return;
                await UpdateDisplayParallelAsync(
                    record, view, subRecordKey,
                    MainWindow._topJob, MainWindow._topJob?.VisionTool?.RunStatus, "top2d", token, capturedResult);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Processing cancelled for Top2D mirror view of {MainWindow._topJob?.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing Top2D mirror view for {MainWindow._topJob?.Name}");
            }
        }

        internal async Task ProcessToolResultsAsyncRear(
            ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (record == null) return;
                await UpdateDisplayParallelAsync(
                    record, view, subRecordKey,
                    string.Equals(capturedResult?.CameraRole, "right", StringComparison.OrdinalIgnoreCase)
                        ? MainWindow._rightJob : MainWindow._rearJob,
                    (string.Equals(capturedResult?.CameraRole, "right", StringComparison.OrdinalIgnoreCase)
                        ? MainWindow._rightJob : MainWindow._rearJob)?.VisionTool?.RunStatus,
                    CameraConfigurationHelper.NormalizeCameraType(capturedResult?.CameraRole), token, capturedResult);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Processing cancelled for {MainWindow._rearJob?.Name ?? "Rear"}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing {MainWindow._rearJob?.Name ?? "Rear"} results");
            }
        }

        internal async Task ProcessToolResultsAsyncBottom(
            ICogRecord record, CogRecordDisplay view, string subRecordKey, CancellationToken token, CameraResult capturedResult = null)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (record == null) return;
                await UpdateDisplayParallelAsync(
                    record, view, subRecordKey,
                    MainWindow._bottomJob, MainWindow._bottomJob?.VisionTool?.RunStatus, "bottom", token, capturedResult);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn($"Processing cancelled for {MainWindow._bottomJob?.Name ?? "Bottom"}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing {MainWindow._bottomJob?.Name ?? "Bottom"} results");
            }
        }
    }
}
