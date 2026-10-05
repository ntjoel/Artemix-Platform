using Cognex.VisionPro.ToolBlock;
using NLog;
using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.DataManage
{
    public enum InspectionOutcomeCode
    {
        NoGood = 0,
        Good = 1,
        Unclassified = 3
    }

    public class InspectionProcessor
    {
        private readonly IToolBlockValidator _validator;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Track inspection state
        public bool LastInspectionPassed { get; private set; }
        public DateTime LastInspectionTime { get; private set; }
        public string LastInspectionMessage { get; private set; }

        public InspectionProcessor(IToolBlockValidator validator, ILogger logger)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _logger = (Logger)(logger ?? throw new ArgumentNullException(nameof(logger)));
            ResetInspectionState();
        }

        private void ResetInspectionState()
        {
            LastInspectionPassed = false;
            LastInspectionTime = DateTime.MinValue;
            LastInspectionMessage = "Inspection not started";
        }

        /// <summary>
        /// Processes a complete inspection cycle for one piece.
        /// </summary>
        /// <param name="topToolBlock">ToolBlock from the top/top3D camera job.</param>
        /// <param name="sideToolBlock">ToolBlock from the side camera job, or null if not present.</param>
        /// <param name="frontToolBlock">ToolBlock from the front camera job, or null if not present.</param>
        /// <param name="isTop3DInspection">
        ///   True when the top job loaded from the VPP is named "Top3D".
        ///   Drives 3D validation instead of standard top validation.
        ///   This is determined by the actual job name in the VPP, NOT by MachineType in config.
        /// </param>
        /// <param name="cancellationToken">Token to cancel in-flight validation.</param>
        public async Task<InspectionResult> ProcessInspectionAsync(
            CogToolBlock topToolBlock,
            CogToolBlock sideToolBlock,
            CogToolBlock frontToolBlock = null,
            bool isTop3DInspection = false,
            CancellationToken cancellationToken = default)
        {
            return await ProcessInspectionAsync(
                topToolBlock,
                sideToolBlock,
                frontToolBlock,
                null,
                null,
                null,
                isTop3DInspection,
                cancellationToken);
        }

        public async Task<InspectionResult> ProcessInspectionAsync(
            CogToolBlock topToolBlock,
            CogToolBlock sideToolBlock,
            CogToolBlock frontToolBlock,
            CogToolBlock rearToolBlock,
            CogToolBlock bottomToolBlock,
            IEnumerable<string> missingCameraRoles,
            bool isTop3DInspection = false,
            CancellationToken cancellationToken = default,
            CogToolBlock rightToolBlock = null)
        {
            ResetInspectionState();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (topToolBlock == null)
                {
                    LastInspectionMessage = "Top tool block not initialized";
                    _logger.Warn(LastInspectionMessage);
                    return new InspectionResult { IsValid = false, Message = LastInspectionMessage };
                }

                bool hasSideInspection = sideToolBlock != null;
                bool hasFrontInspection = frontToolBlock != null;
                bool hasRearInspection = rearToolBlock != null;
                bool hasRightInspection = rightToolBlock != null;
                bool hasBottomInspection = bottomToolBlock != null;
                var missingRoles = (missingCameraRoles ?? Enumerable.Empty<string>())
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Select(role => role.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (_validator == null)
                {
                    LastInspectionMessage = "Validator not initialized";
                    _logger.Error(LastInspectionMessage);
                    return new InspectionResult { IsValid = false, Message = LastInspectionMessage };
                }

                // Route to the correct top validator based on the actual job name
                // loaded from the VPP — not from MachineType in config.
                var topResult = await SafeValidateAsync(
                    () => isTop3DInspection
                        ? _validator.GetDetailedTop3DValidationAsync(topToolBlock)
                        : _validator.GetDetailedTopValidationAsync(topToolBlock),
                    "Top Inspection", cancellationToken);
                CameraClassificationResult topClassification = ReadClassificationResult(
                    topToolBlock,
                    topResult);

                ValidationResult sideResult = null;
                CameraClassificationResult sideClassification = null;
                if (hasSideInspection)
                {
                    sideResult = await SafeValidateAsync(
                        () => _validator.GetDetailedSideValidationAsync(sideToolBlock),
                        "Side Inspection", cancellationToken);
                    sideClassification = ReadClassificationResult(
                        sideToolBlock,
                        sideResult);
                }

                ValidationResult frontResult = null;
                CameraClassificationResult frontClassification = null;
                if (hasFrontInspection)
                {
                    frontResult = await SafeValidateAsync(
                        () => _validator.GetDetailedFrontValidationAsync(frontToolBlock),
                        "Front Inspection", cancellationToken);
                    frontClassification = ReadClassificationResult(
                        frontToolBlock,
                        frontResult);
                }

                ValidationResult rearResult = null;
                CameraClassificationResult rearClassification = null;
                ValidationResult rightResult = null;
                CameraClassificationResult rightClassification = null;
                if (hasRightInspection)
                {
                    rightResult = await SafeValidateAsync(
                        () => _validator.GetDetailedRearValidationAsync(rightToolBlock, "right"),
                        "Right Inspection", cancellationToken);
                    rightClassification = ReadClassificationResult(rightToolBlock, rightResult);
                }
                if (hasRearInspection)
                {
                    rearResult = await SafeValidateAsync(
                        () => _validator.GetDetailedRearValidationAsync(rearToolBlock, "rear"),
                        "Rear Inspection", cancellationToken);
                    rearClassification = ReadClassificationResult(
                        rearToolBlock,
                        rearResult);
                }

                ValidationResult bottomResult = null;
                CameraClassificationResult bottomClassification = null;
                if (hasBottomInspection)
                {
                    bottomResult = await SafeValidateAsync(
                        () => _validator.GetDetailedBottomValidationAsync(bottomToolBlock),
                        "Bottom Inspection", cancellationToken);
                    bottomClassification = ReadClassificationResult(
                        bottomToolBlock,
                        bottomResult);
                }

                var missingCameraResults = BuildMissingCameraValidationResults(missingRoles);

                bool isOverallValid = topResult.IsValid &&
                    (sideResult?.IsValid ?? true) &&
                    (frontResult?.IsValid ?? true) &&
                    (rearResult?.IsValid ?? true) &&
                    (rightResult?.IsValid ?? true) &&
                    (bottomResult?.IsValid ?? true) &&
                    missingCameraResults.All(cameraResult => cameraResult.IsValid);

                UpdateInspectionState(topResult, sideResult, frontResult, rearResult, bottomResult, missingCameraResults, isOverallValid, rightResult);
                var result = new InspectionResult
                {
                    IsValid = isOverallValid,
                    HasDefects = !isOverallValid,
                    RejectRequired = !isOverallValid,
                    Message = LastInspectionMessage,
                    TopResult = topResult,
                    SideResult = sideResult,
                    FrontResult = frontResult,
                    RearResult = rearResult,
                    RightResult = rightResult,
                    BottomResult = bottomResult,
                    TopClassification = topClassification,
                    SideClassification = sideClassification,
                    FrontClassification = frontClassification,
                    RearClassification = rearClassification,
                    RightClassification = rightClassification,
                    BottomClassification = bottomClassification,
                    MissingCameraResults = missingCameraResults
                };

                return result;

            }
            catch (Exception ex)
            {
                HandleInspectionError(ex);
                return new InspectionResult { IsValid = false, Message = LastInspectionMessage };
            }
        }

        private static List<ValidationResult> BuildMissingCameraValidationResults(IEnumerable<string> missingCameraRoles)
        {
            var results = new List<ValidationResult>();
            if (missingCameraRoles == null)
            {
                return results;
            }

            foreach (string role in missingCameraRoles.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                results.Add(new ValidationResult
                {
                    IsValid = false,
                    Measurements = new Dictionary<string, (double, double, bool)>(),
                    ErrorMessages = new List<string> { $"{role} camera result missing before inspection timeout" }
                });
            }

            return results;
        }

        private static bool? ResolveClassificationPassed(ValidationResult validationResult)
        {
            if (validationResult?.Measurements != null &&
                validationResult.Measurements.TryGetValue("Classification", out var classification))
            {
                return classification.Passed;
            }

            return null;
        }

        private static CameraClassificationResult ReadClassificationResult(
            CogToolBlock toolBlock,
            ValidationResult validationResult)
        {
            CameraClassificationResult classification = VisionClassificationResultReader.Read(
                toolBlock,
                ResolveClassificationPassed(validationResult));

            if (classification != null && validationResult?.IsAiClassificationUnclassified == true)
            {
                classification.State = CameraClassificationState.Unclassified;

                // La classe viene sostituita, il punteggio NO: a database finiscono
                // "Unclassify" e la confidenza effettivamente rilevata, cosi' l'analisi
                // distingue un pezzo mai riconosciuto da uno riconosciuto e scartato. La
                // classe originale resta comunque nel messaggio di difetto prodotto dal
                // validatore ("Class=OK, Score=... below minimum ...").
                classification.ClassName = CameraClassificationResult.UnclassifiedClassName;
            }

            return classification;
        }

        private void UpdateInspectionState(
            ValidationResult topResult,
            ValidationResult sideResult,
            ValidationResult frontResult,
            ValidationResult rearResult,
            ValidationResult bottomResult,
            IEnumerable<ValidationResult> missingCameraResults,
            bool isOverallValid,
            ValidationResult rightResult = null)
        {
            LastInspectionPassed = isOverallValid;
            LastInspectionTime = DateTime.Now;

            if (LastInspectionPassed)
            {
                LastInspectionMessage = "Inspection passed successfully";
                _logger.Info(LastInspectionMessage);
            }
            else
            {
                var errors = new List<string>();
                if (topResult.ErrorMessages != null)
                    errors.AddRange(topResult.ErrorMessages);
                if (sideResult?.ErrorMessages != null)
                    errors.AddRange(sideResult.ErrorMessages);
                if (frontResult?.ErrorMessages != null)
                    errors.AddRange(frontResult.ErrorMessages);
                if (rearResult?.ErrorMessages != null)
                    errors.AddRange(rearResult.ErrorMessages);
                if (rightResult?.ErrorMessages != null)
                    errors.AddRange(rightResult.ErrorMessages);
                if (bottomResult?.ErrorMessages != null)
                    errors.AddRange(bottomResult.ErrorMessages);
                if (missingCameraResults != null)
                {
                    foreach (var missingResult in missingCameraResults)
                    {
                        if (missingResult?.ErrorMessages != null)
                            errors.AddRange(missingResult.ErrorMessages);
                    }
                }

                LastInspectionMessage = errors.Count > 0
                    ? $"Inspection failed: {string.Join("; ", errors)}"
                    : "Inspection failed (unknown reason)";
                _logger.Warn(LastInspectionMessage);
            }

        }

        private async Task<ValidationResult> SafeValidateAsync(
            Func<Task<ValidationResult>> validateFunc,
            string inspectionType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await validateFunc().ConfigureAwait(false);

                if (result == null)
                {
                    _logger.Warn($"{inspectionType} returned null result");
                    return new ValidationResult
                    {
                        IsValid = false,
                        Measurements = new Dictionary<string, (double, double, bool)>(),
                        ErrorMessages = new List<string> { $"{inspectionType} returned null" }
                    };
                }

                // Ensure collections are initialized
                result.Measurements = result.Measurements ?? new Dictionary<string, (double, double, bool)>();
                result.ErrorMessages = result.ErrorMessages ?? new List<string>();

                LogValidationResult(inspectionType, result);
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.Debug($"{inspectionType} validation was cancelled");
                return new ValidationResult
                {
                    IsValid = false,
                    Measurements = new Dictionary<string, (double, double, bool)>(),
                    ErrorMessages = new List<string> { $"{inspectionType} validation was cancelled" }
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{inspectionType} validation failed");
                return new ValidationResult
                {
                    IsValid = false,
                    Measurements = new Dictionary<string, (double, double, bool)>(),
                    ErrorMessages = new List<string> { $"{inspectionType} validation error: {ex.Message}" }
                };
            }
        }

        private void HandleInspectionError(Exception ex)
        {
            LastInspectionPassed = false;
            LastInspectionMessage = $"Inspection error: {ex.Message}";
            _logger.Error(ex, "Inspection processing failed");
            if (MainWindow.logger != null)
                MainWindow.logger.Error(LastInspectionMessage);
        }

        private void LogValidationResult(string inspectionType, ValidationResult result)
        {
            if (!_logger.IsDebugEnabled)
                return;

            foreach (var measurement in result.Measurements)
            {
                _logger.Debug($"{inspectionType} measurement {measurement.Key}: {measurement.Value.Item1} " +
                              $"(Tolerance: {measurement.Value.Item2}, Passed: {measurement.Value.Item3})");
            }

            if (!result.IsValid && result.ErrorMessages.Count > 0)
                _logger.Debug($"{inspectionType} rejected: {string.Join("; ", result.ErrorMessages)}");
        }

        public async Task ShutdownAsync()
        {
            try
            {
                
                // Ferma qualsiasi operazione in corso
                if (MainWindow._cts != null && !MainWindow._cts.IsCancellationRequested)
                {
                    MainWindow. _cts.Cancel();
                }

                // Attendi che le operazioni terminino
                await Task.Delay(500);

                // Pulisci le risorse
                MainWindow. _cts?.Dispose();
              //  _validator?.Dispose();

                // Log della chiusura
                _logger?.Info("InspectionProcessor shutdown completato");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Errore nello shutdown di InspectionProcessor: {ex.Message}");
            }
        }
    }

    public class InspectionResult
    {
        public InspectionOutcomeCode Outcome { get; set; } = InspectionOutcomeCode.Good;
        public bool IsUnclassified => Outcome == InspectionOutcomeCode.Unclassified;
        public List<string> ProcessingErrors { get; set; } = new List<string>();
        public List<string> UnclassifiedCameraRoles { get; set; } = new List<string>();
        public bool IsValid { get; set; }
        public bool HasDefects { get; set; }
        public bool RejectRequired { get; set; }
        public string Message { get; set; }
        public ValidationResult TopResult { get; set; }
        public ValidationResult SideResult { get; set; }
        public ValidationResult FrontResult { get; set; }
        public ValidationResult RearResult { get; set; }
        public ValidationResult RightResult { get; set; }
        public ValidationResult BottomResult { get; set; }
        public CameraClassificationResult TopClassification { get; set; }
        public CameraClassificationResult SideClassification { get; set; }
        public CameraClassificationResult FrontClassification { get; set; }
        public CameraClassificationResult RearClassification { get; set; }
        public CameraClassificationResult RightClassification { get; set; }
        public CameraClassificationResult BottomClassification { get; set; }
        public string TopCameraRole { get; set; }
        public string SideCameraRole { get; set; }
        public string FrontCameraRole { get; set; }
        public string RearCameraRole { get; set; }
        public string RightCameraRole { get; set; }
        public string BottomCameraRole { get; set; }
        public List<ValidationResult> MissingCameraResults { get; set; } = new List<ValidationResult>();
        public Dictionary<string, bool> DetectedDefects { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> RejectingDefects { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }
}
