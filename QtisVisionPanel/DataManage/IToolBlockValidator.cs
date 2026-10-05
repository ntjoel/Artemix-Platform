using Cognex.VisionPro.ToolBlock;
using QtisVisionPanel.Cls_Config;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views.UserControls.DisplayRecord;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace QtisVisionPanel.DataManage
{
    public interface IToolBlockValidator
    {
        Task<bool> ValidateTopToolBlockAsync(CogToolBlock toolBlock);
        Task<bool> ValidateSideToolBlockAsync(CogToolBlock toolBlock);
        Task<ValidationResult> GetDetailedTopValidationAsync(CogToolBlock toolBlock);
        Task<ValidationResult> GetDetailedTop3DValidationAsync(CogToolBlock toolBlock);
        Task<ValidationResult> GetDetailedSideValidationAsync(CogToolBlock toolBlock);
        Task<ValidationResult> GetDetailedFrontValidationAsync(CogToolBlock toolBlock);
        Task<ValidationResult> GetDetailedRearValidationAsync(CogToolBlock toolBlock);
        Task<ValidationResult> GetDetailedRearValidationAsync(CogToolBlock toolBlock, string cameraRole);
        Task<ValidationResult> GetDetailedBottomValidationAsync(CogToolBlock toolBlock);
        Task<PrintCenteringValidation> ValidatePrintCenteringAsync(CogToolBlock toolBlock);
        Task<ShapeValidation> ValidateShapeTopAsync(CogToolBlock toolBlock);
        Task<ShapeValidation> ValidateShapeSideAsync(CogToolBlock toolBlock);
    }
    public class PrintCenteringValidation
    {
        public bool IsCentered { get; set; }
        public Dictionary<string, CenteringResult> DirectionResults { get; set; }
    }
    public class ShapeValidation
    {
        public bool IsShapeValid { get; set; }
        public Dictionary<string, ShapeResult> DirectionResults { get; set; }
    }
    public class CenteringResult
    {
        public double MeasuredValue { get; set; }
        public double NominalValue { get; set; }
        public double Tolerance { get; set; }
        public bool IsWithinTolerance { get; set; }
    }
    public class ShapeResult
    {
        public double MeasuredValue { get; set; }
        public double NominalValue { get; set; }
        public double Tolerance { get; set; }
        public bool IsWithinTolerance { get; set; }
    }
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public bool IsAiClassificationUnclassified { get; set; }
        public Dictionary<string, (double Value, double Tolerance, bool Passed)> Measurements { get; set; }
        public List<string> ErrorMessages { get; set; }
        public Dictionary<string, ProcessMonitoringResult> MonitoringResults { get; set; } =
            new Dictionary<string, ProcessMonitoringResult>(StringComparer.OrdinalIgnoreCase);
    }

    public enum ProcessMonitoringState
    {
        Wait,
        Monitor,
        Good,
        NoGood,
        Invalid
    }

    /// <summary>
    /// Result of an advisory process check. Monitoring results may classify and
    /// archive a piece without being allowed to command a physical reject.
    /// </summary>
    public sealed class ProcessMonitoringResult
    {
        public string FeatureKey { get; set; }
        public ProcessMonitoringState State { get; set; } = ProcessMonitoringState.Wait;
        public bool RejectEnabled { get; set; }
        public string Summary { get; set; }
        public List<string> Messages { get; } = new List<string>();

        public bool IsDefect =>
            State == ProcessMonitoringState.NoGood ||
            State == ProcessMonitoringState.Invalid;
    }
    public class HeigthResult
    {
        public double MeasuredValue { get; set; }
        public double NominalValue { get; set; }
        public double Tolerance { get; set; }
        public bool IsWithinTolerance { get; set; }
    }

    public class ToolBlockValidator : IToolBlockValidator
    {
        private readonly AsyncRecipeParam _recipeParam;
        private readonly InspectionConfigService _configService;
        // private readonly GlobalCounterService _globalCounterService;
     

        private const string LogoPositionOutputName = "LogoPosition";
         private const string PrintCenteringOutputName = "PrintCentering";
        private const string OpenFlapsAreaOutputName = "OpenFlapsArea";
        private const string HeightOutputName = "Heigth";
        private const string SealingAreaOutputName = "SealingArea";
        private const string DefaultFrontTraceabilityPresenceOutputName = "TraceabilityPresent";
        private const string DefaultFrontTraceabilityCodeOutputName = "TraceabilityCode";
        private const string DefaultThreeDHeightOutputName = "ThreeDHeight";
        private const string DefaultThreeDHeightMedianOutputName = "HeightMedian";
        private const string DefaultThreeDHeightHighTailOutputName = "HeightHighTail";
        private const string DefaultThreeDHeightValidPixelRatioOutputName = "HeightValidPixelRatio";
        private const string DefaultThreeDHeightBulgeOutputName = "HeightBulge";
        private const string DefaultThreeDHeightMaximumOutputName = "ThreeDHeight_MAX";
        private const string DefaultThreeDWidthOutputName = "ThreeDWidth";
        private const string DefaultThreeDLengthOutputName = "ThreeDLength";
        private const string DefaultLeftSideSealingOutputName = "LeftSideSealingOk";
        private const string DefaultLeftRollCountOutputName = "LeftRollCountOk";
        private const string DefaultRightSideSealingOutputName = "RightSideSealingOk";
        private const string DefaultRearSideSealingOutputName = "RearSideSealingOk";
        private const string DefaultBottomSealingOutputName = "BottomSealingOk";
        private const string DefaultTrappedPaperOutputName = "NoTrappedPaper";
        private const double ThreeDProfileDefaultMinimumValidPixelRatio = 0.75;
        private const double ThreeDProfileBulgeNegativeToleranceMm = 0.001;

        public ToolBlockValidator(AsyncRecipeParam recipeParam)
        {
            _recipeParam = recipeParam ?? throw new ArgumentNullException(nameof(recipeParam));
            _configService = ServiceLocator.InspectionConfigService;
        }
      
        // Ruoli camera usati dai gate di validazione. Ogni GetDetailedXxxValidationAsync
        // passa il proprio ruolo in modo esplicito: niente stato implicito su un percorso
        // che decide gli scarti, e nessun problema di continuazioni async su thread diversi.
        private const string RoleTop = "top";
        private const string RoleTop3D = "top3d";
        private const string RoleSide = "side";
        private const string RoleLeft = "left";
        private const string RoleRear = "rear";
        private const string RoleFront = "front";
        private const string RoleBottom = "bottom";

        /// <summary>
        /// Gate globale, senza vista: l'ispezione e' attiva per la macchina/ricetta.
        /// Usato solo dove il ruolo non e' determinabile.
        /// </summary>
        private bool IsFeatureEnabled(string feature)
        {
            return _configService.IsFeatureEnabled(feature);
        }

        /// <summary>
        /// Gate per vista: attivo solo se l'ispezione e' abilitata a livello globale E la
        /// matrice vista x ispezione dice che questa vista la verifica. Senza configurazione
        /// esplicita il default riproduce l'applicabilita' storica, quindi il comportamento
        /// resta invariato finche' l'operatore non interviene.
        /// </summary>
        private bool IsFeatureEnabledForView(string cameraRole, string feature)
        {
            return _configService.IsFeatureEnabled(cameraRole, feature);
        }
        public async Task<bool> ValidateTopToolBlockAsync(CogToolBlock toolBlock)
        {
            var result = await GetDetailedTopValidationAsync(toolBlock);
            return result.IsValid;
        }

        public async Task<bool> ValidateSideToolBlockAsync(CogToolBlock toolBlock)
        {
            var result = await GetDetailedSideValidationAsync(toolBlock);
            return result.IsValid;
        }

        public async Task<ValidationResult> GetDetailedTopValidationAsync(CogToolBlock toolBlock)
        {
            await _recipeParam.EnsureLoadedAsync();
            var recipe = _recipeParam.Config.recipeParamTop;
            var result = new ValidationResult
            {
                Measurements = new Dictionary<string, (double, double, bool)>(),
                ErrorMessages = new List<string>()
            };

            try
            {
                // Logo Position Validation
                if (IsFeatureEnabledForView(RoleTop, "Logo")&&toolBlock.Outputs.Contains(LogoPositionOutputName))
                {
                    if (TryGetOutputValue(toolBlock, LogoPositionOutputName, out double logoPosition))
                    {
                        MainWindow._produzioneRecord.LogoMatchPerc = logoPosition;
                        MainWindow._produzioneRecord.MinValueLogoToll = recipe.LogoToll;
                        // logoPosition e' un punteggio di corrispondenza 0..1 (vedi LogoMatchPerc),
                        // non uno scostamento: LogoToll e' la percentuale MINIMA accettata.
                        bool logoValid = Math.Abs(logoPosition) >= (recipe.LogoToll) / 100;
                        result.Measurements.Add("LogoPosition", (logoPosition, recipe.LogoToll, logoValid));
                        if (!logoValid)
                        {
                            // Il messaggio precedente diceva "position out of tolerance (Max: ±60)"
                            // con un valore di 0,559: incomprensibile per l'operatore, che leggeva
                            // un valore ampiamente dentro un massimo di 60 e lo scarto come guasto.
                            string logoDefect =
                                $"Logo match below minimum: {logoPosition * 100.0:0.0}% (Min: {recipe.LogoToll:0.#}%)";
                            result.ErrorMessages.Add(logoDefect);
                            await ServiceLocator.CounterManager.IncrementDefectAsync("Logo", logoDefect);
                        }
                        else 
                        {
                            await ServiceLocator.CounterManager.ClearDefectMessage("Logo");
                           MainWindow._produzioneRecord.NcLoghiImmagini = 1; // Reset if logo is valid
                        }
                    }
                }
                else if(!IsFeatureEnabledForView(RoleTop, "Logo"))
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("Logo");
                    MainWindow._produzioneRecord.NcLoghiImmagini = 4;//logo not checked
                }

                if(IsFeatureEnabledForView(RoleTop, "PrintCentering"))
                {
                    var centeringResult = await ValidatePrintCenteringAsync(toolBlock);
                    if (centeringResult != null)
                    {
                        foreach (var direction in centeringResult.DirectionResults)
                        {
                            result.Measurements.Add(
                                $"PrintCentering_{direction.Key}",
                                (direction.Value.MeasuredValue,
                                 direction.Value.Tolerance,
                                 direction.Value.IsWithinTolerance));
                        }

                        if (!centeringResult.IsCentered)
                        {
                            var failedDirections = centeringResult.DirectionResults
                                .Where(d => !d.Value.IsWithinTolerance)
                                .Select(d => $"{d.Key} (Value: {d.Value.MeasuredValue}, Expected: {d.Value.NominalValue} ±{d.Value.Tolerance})");

                            result.ErrorMessages.Add(
                                $"Print centering failed in directions: {string.Join(", ", failedDirections)}");
                           
                            await ServiceLocator.CounterManager.IncrementDefectAsync("print_centering",$"Print centering failed in directions: {string.Join(", ", failedDirections)}");
                        }
                        else
                        {
                            await ServiceLocator.CounterManager.ClearDefectMessage("print_centering");
                            MainWindow._produzioneRecord.NcCentraturaLogo = 1;
                        }
                    }
                   
                }
                else if(!IsFeatureEnabledForView(RoleTop, "PrintCentering"))
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("print_centering");
                    MainWindow._produzioneRecord.NcCentraturaLogo = 4; // Print centering not checked
                    MainWindow.logger.Warn("PrintCenteringDirectionResults is null.");
                }



                if (IsFeatureEnabledForView(RoleTop, "ShapeTop"))
                {
                    var shapeResult = await ValidateShapeTopAsync(toolBlock);
                    if (shapeResult.DirectionResults == null)
                    {
                        MainWindow._produzioneRecord.NcShapeTop = 4;
                        MainWindow.logger.Warn("ShapeDirectionResults is null.");
                    }
                    else
                    {
                        foreach (var direction in shapeResult.DirectionResults)
                        {
                            result.Measurements.Add(
                                $"Shape_{direction.Key}",
                                (direction.Value.MeasuredValue,
                                 direction.Value.Tolerance,
                                 direction.Value.IsWithinTolerance));
                        }
                        if (!shapeResult.IsShapeValid)
                        {
                            var failedDirections = shapeResult.DirectionResults
                                .Where(d => !d.Value.IsWithinTolerance)
                                .Select(d => $"{d.Key} (Value: {d.Value.MeasuredValue}, Expected: {d.Value.NominalValue} ±{d.Value.Tolerance})");
                            result.ErrorMessages.Add(
                                $"Shape top validation failed in directions: {string.Join(", ", failedDirections)}");
                            await ServiceLocator.CounterManager.IncrementDefectAsync("shapetop",$"Shape top validation failed in directions: {string.Join(", ", failedDirections)}");
                            MainWindow._produzioneRecord.NcShapeTop = 0;
                        }
                        else
                        {
                            await ServiceLocator.CounterManager.ClearDefectMessage("ShapeTop");
                            MainWindow._produzioneRecord.NcShapeTop = 1;
                        }
                    }
                }
                else if(!IsFeatureEnabledForView(RoleTop, "ShapeTop"))
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("ShapeTop");
                    MainWindow._produzioneRecord.NcShapeTop = 4;
                    MainWindow.logger.Warn("ShapeDirectionResults is null.");
                }






                if (IsFeatureEnabledForView(RoleTop, "OpenFlaps") && toolBlock.Outputs.Contains(OpenFlapsAreaOutputName))
                {
                    if (TryGetOutputValue(toolBlock, OpenFlapsAreaOutputName, out double openFlapsArea))
                    {
                        bool flapsValid = openFlapsArea <= recipe.MinAreaOpenFlas;
                        result.Measurements.Add("OpenFlapsArea", (openFlapsArea, recipe.MinAreaOpenFlas, flapsValid));
                        if (!flapsValid)
                        {
                            string openFlapsMessage =
                                $"Open flaps area too large: {openFlapsArea} (Max: {recipe.MinAreaOpenFlas})";
                            result.ErrorMessages.Add(openFlapsMessage);
                            await ServiceLocator.CounterManager.IncrementDefectAsync("openflaps", openFlapsMessage);
                        }
                        else
                        {
                            await ServiceLocator.CounterManager.ClearDefectMessage("OpenFlaps");
                            MainWindow._produzioneRecord.NcAletteAperte = 1;
                        }
                    }
                }
                else
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("OpenFlaps");
                    MainWindow._produzioneRecord.NcAletteAperte = 4; // Flaps not checked
                    MainWindow.logger.Debug("OpenFlapsArea output not found in tool block.");
                }

                await ValidateAiClassificationAsync(toolBlock, "Top", result);
                result.IsValid = result.ErrorMessages.Count == 0;
            }
            catch (Exception ex)
            {
                result.ErrorMessages.Add($"Validation error: {ex.Message}");
                MainWindow.logger.Error($"Validation error: {ex.Message}");
            }

            return result;
        }

        public async Task<ValidationResult> GetDetailedSideValidationAsync(CogToolBlock toolBlock)
        {
            await _recipeParam.EnsureLoadedAsync();
            var recipe = _recipeParam.Config.recipeParamSide;
            var result = new ValidationResult
            {
                Measurements = new Dictionary<string, (double, double, bool)>(),
                ErrorMessages = new List<string>()
            };

            try
            {
                if (IsLeftInspectionToolBlock(toolBlock))
                {
                    return await GetDetailedLeftValidationAsync(toolBlock, result);
                }

                // Height Validation
                if (IsFeatureEnabledForView(RoleSide, "Height") && toolBlock.Outputs.Contains(HeightOutputName))
                {
                    if (TryGetOutputValue(toolBlock, HeightOutputName, out double height))
                    {
                        var heightValid = new HeigthResult
                        {
                            MeasuredValue = height,
                            NominalValue = recipe.Min_Heigth_value,
                            Tolerance = recipe.Heigth_toll,
                            IsWithinTolerance = Math.Abs(height - recipe.Min_Heigth_value) <= recipe.Heigth_toll
                        };
                        // bool heightValid = height > recipe.Min_Heigth_value;
                        MainWindow._produzioneRecord.HeigthMeasureValue = height;
                        MainWindow._produzioneRecord.HeigthNominalValue = recipe.Min_Heigth_value;
                        MainWindow._produzioneRecord.HeigthMinimum = recipe.Min_Heigth_value - recipe.Heigth_toll;
                        MainWindow._produzioneRecord.HeigthMaximum = recipe.Min_Heigth_value + recipe.Heigth_toll;
                        result.Measurements.Add("Height", (heightValid.MeasuredValue, heightValid.NominalValue, heightValid.IsWithinTolerance));
                        if (!heightValid.IsWithinTolerance)
                        {
                            result.ErrorMessages.Add($"Height out of tolerance: {height} ( Expected: {recipe.Min_Heigth_value} ±{recipe.Heigth_toll})");
                            // MainWindow.MainView.Viewstatistic.lb_Side_height.Background = Brushes.Red;
                
                            await ServiceLocator.CounterManager.IncrementDefectAsync("height",$"Height out of tolerance: {height} ( Expected: {recipe.Min_Heigth_value} ±{recipe.Heigth_toll})");
                           // await MainWindow.counterUpdater.IncrementHeightDefectsAsync();
                        }
                        else
                        {
                            // MainWindow.MainView.Viewstatistic.lb_Side_height.Background = Brushes.Green;
                            //MainWindow._produzioneRecord.NcAltezza = 1; // Reset if height is valid
                            await ServiceLocator.CounterManager.ClearDefectMessage("Height");
                            MainWindow._produzioneRecord.NcHeigth = 1; // Reset if height is valid
                        }
                    }
                }

                else
                {
                    // MainWindow.MainView.Viewstatistic.lb_Side_height.Background = Brushes.White;
                    await ServiceLocator.CounterManager.ClearDefectMessage("Height");
                    MainWindow._produzioneRecord.NcHeigth = 4; // Height not checked
                    MainWindow.logger.Warn("Height output not found in tool block.");
                }


                // Sealing Area Validation
                if (IsFeatureEnabledForView(RoleSide, "SideSealing") && toolBlock.Outputs.Contains(SealingAreaOutputName))
                {
                    if (TryGetOutputValue(toolBlock, SealingAreaOutputName, out double sealingArea))
                    {
                        double minAreaSealing = recipe.ResolveMinAreaSealing(RoleSide);
                        bool sealingValid = sealingArea >= minAreaSealing;
                        result.Measurements.Add("SealingArea", (sealingArea, minAreaSealing, sealingValid));
                        MainWindow._produzioneRecord.AreaSealing = sealingArea;
                        if (!sealingValid)
                        {
                            result.ErrorMessages.Add($"Sealing area too small: {sealingArea} (Min: {minAreaSealing})");

                            await ServiceLocator.CounterManager.IncrementDefectAsync("sidesealing",$" {sealingArea} (Min: {minAreaSealing})");

                        }
                        else
                        {
                            //  MainWindow.MainView.Viewstatistic.lb_Side_sealing.Background = Brushes.Green;
                            await ServiceLocator.CounterManager.ClearDefectMessage("SideSealing");
                            MainWindow._produzioneRecord.NcSaldaturaLaterale = 1; // Reset if sealing area is valid
                        }
                    }

                }
                else
                {
                    // MainWindow.MainView.Viewstatistic.lb_Side_sealing.Background = Brushes.White;
                    await ServiceLocator.CounterManager.ClearDefectMessage("SideSealing");
                    MainWindow._produzioneRecord.NcSaldaturaLaterale = 4; // Sealing not checked
                }

                await ValidateAiClassificationAsync(toolBlock, "Side", result);


                if(IsFeatureEnabledForView(RoleSide, "ShapeSide"))
                {
                    // Enhanced shape Centering Validation (using directional checks)
                    var shapeResult = await ValidateShapeSideAsync(toolBlock);
                    // Add directional results to measurements
                    foreach (var direction in shapeResult.DirectionResults)
                    {
                        result.Measurements.Add(
                            $"Shape_{direction.Key}",
                            (direction.Value.MeasuredValue,
                             direction.Value.Tolerance,
                             direction.Value.IsWithinTolerance));
                    }
                    if (!shapeResult.IsShapeValid)
                    {
                        var failedDirections = shapeResult.DirectionResults
                            .Where(d => !d.Value.IsWithinTolerance)
                            .Select(d => $"{d.Key} (Value: {d.Value.MeasuredValue}, Expected: {d.Value.NominalValue} ±{d.Value.Tolerance})");
                        result.ErrorMessages.Add(
                            $"Shape side validation failed in directions: {string.Join(", ", failedDirections)}");
                       

                        await ServiceLocator.CounterManager.IncrementDefectAsync("shapeside",$"Shape side validation failed in directions: {string.Join(", ", failedDirections)}");
                    }
                    else
                    {
                        await ServiceLocator.CounterManager.ClearDefectMessage("ShapeSide");
                        //MainWindow.MainView.Viewstatistic.lb_Side_shape.Background = Brushes.Green;
                        MainWindow._produzioneRecord.NcShapeBottom = 1; // Reset if shape is valid
                    }
                }
              

                result.IsValid = result.ErrorMessages.Count == 0;
            }
            catch (Exception ex)
            {
                result.ErrorMessages.Add($"Validation error: {ex.Message}");
            }

            return result;
        }

        public Task<ValidationResult> GetDetailedRearValidationAsync(CogToolBlock toolBlock)
            => GetDetailedRearValidationAsync(toolBlock, RoleRear);

        public async Task<ValidationResult> GetDetailedRearValidationAsync(CogToolBlock toolBlock, string cameraRole)
        {
            string inspectionRole = string.Equals(cameraRole, "right", StringComparison.OrdinalIgnoreCase)
                ? "right" : RoleRear;
            await _recipeParam.EnsureLoadedAsync();
            var recipe = _recipeParam.Config.recipeParamSide;
            var result = new ValidationResult
            {
                Measurements = new Dictionary<string, (double, double, bool)>(),
                ErrorMessages = new List<string>()
            };

            try
            {
                if (IsFeatureEnabledForView(inspectionRole, "SideSealing"))
                {
                    bool sideSealingValidated = false;

                    foreach (string outputName in ResolveRightSideSealingOutputNames().Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (TryGetBooleanOutputValue(toolBlock, outputName, out bool sealingOk))
                        {
                            sideSealingValidated = true;
                            result.Measurements.Add("Right side sealing", (sealingOk ? 1 : 0, 1, sealingOk));
                            if (!sealingOk)
                            {
                                const string failureMessage = "Right side sealing failed";
                                result.ErrorMessages.Add(failureMessage);
                                await ServiceLocator.CounterManager.IncrementDefectAsync("SideSealing", failureMessage);
                                MainWindow._produzioneRecord.NcSaldaturaLaterale = 0;
                            }
                            else
                            {
                                await ServiceLocator.CounterManager.ClearDefectMessage("SideSealing");
                                MainWindow._produzioneRecord.NcSaldaturaLaterale = 1;
                            }

                            break;
                        }
                    }

                    if (!sideSealingValidated && TryGetOutputValue(toolBlock, SealingAreaOutputName, out double sealingArea))
                    {
                        sideSealingValidated = true;
                        // Soglia della vista REAR: se non configurata eredita quella SIDE,
                        // cosi' le ricette esistenti non cambiano comportamento.
                        double minAreaSealing = recipe.ResolveMinAreaSealing(inspectionRole);
                        bool sealingValid = sealingArea >= minAreaSealing;
                        result.Measurements.Add("RightSealingArea", (sealingArea, minAreaSealing, sealingValid));
                        MainWindow._produzioneRecord.AreaSealing = sealingArea;
                        if (!sealingValid)
                        {
                            string failureMessage = $"Right side sealing area too small: {sealingArea} (Min: {minAreaSealing})";
                            result.ErrorMessages.Add(failureMessage);
                            await ServiceLocator.CounterManager.IncrementDefectAsync("SideSealing", failureMessage);
                            MainWindow._produzioneRecord.NcSaldaturaLaterale = 0;
                        }
                        else
                        {
                            await ServiceLocator.CounterManager.ClearDefectMessage("SideSealing");
                            MainWindow._produzioneRecord.NcSaldaturaLaterale = 1;
                        }
                    }

                    if (!sideSealingValidated)
                    {
                        string missingOutputMessage =
                            $"Right side sealing output missing. Expected one of: {string.Join(", ", ResolveRightSideSealingOutputNames())}, or {SealingAreaOutputName}. Available outputs: {DescribeAvailableOutputs(toolBlock)}";
                        result.ErrorMessages.Add(missingOutputMessage);
                        await ServiceLocator.CounterManager.IncrementDefectAsync("SideSealing", missingOutputMessage);
                        MainWindow._produzioneRecord.NcSaldaturaLaterale = 0;
                    }
                }
                else
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("SideSealing");
                    MainWindow._produzioneRecord.NcSaldaturaLaterale = 4;
                }

                if (IsFeatureEnabledForView(inspectionRole, "ShapeSide") && HasAnyShapeOutput(toolBlock))
                {
                    var shapeResult = await ValidateShapeSideAsync(toolBlock);
                    foreach (var direction in shapeResult.DirectionResults)
                    {
                        result.Measurements.Add(
                            $"RightShape_{direction.Key}",
                            (direction.Value.MeasuredValue,
                             direction.Value.Tolerance,
                             direction.Value.IsWithinTolerance));
                    }

                    if (!shapeResult.IsShapeValid)
                    {
                        var failedDirections = shapeResult.DirectionResults
                            .Where(d => !d.Value.IsWithinTolerance)
                            .Select(d => $"{d.Key} (Value: {d.Value.MeasuredValue}, Expected: {d.Value.NominalValue} ±{d.Value.Tolerance})");
                        result.ErrorMessages.Add(
                            $"Right shape side validation failed in directions: {string.Join(", ", failedDirections)}");

                        await ServiceLocator.CounterManager.IncrementDefectAsync(
                            "ShapeSide",
                            $"Right shape side validation failed in directions: {string.Join(", ", failedDirections)}");
                    }
                    else
                    {
                        await ServiceLocator.CounterManager.ClearDefectMessage("ShapeSide");
                        MainWindow._produzioneRecord.NcShapeBottom = 1;
                    }
                }

                await ValidateAiClassificationAsync(toolBlock, inspectionRole, result);

                result.IsValid = result.ErrorMessages.Count == 0;
            }
            catch (Exception ex)
            {
                result.ErrorMessages.Add($"Rear validation error: {ex.Message}");
            }

            return result;
        }

        public async Task<ValidationResult> GetDetailedBottomValidationAsync(CogToolBlock toolBlock)
        {
            var result = new ValidationResult
            {
                Measurements = new Dictionary<string, (double, double, bool)>(),
                ErrorMessages = new List<string>()
            };

            try
            {
                if (IsFeatureEnabledForView(RoleBottom, "BottomSealing"))
                {
                    await ValidateBottomBooleanOutputAsync(
                        toolBlock,
                        ResolveBottomSealingOutputNames(),
                        ResolveBottomSealingDefectOutputNames(),
                        "BottomSealing",
                        "Bottom sealing",
                        "Bottom sealing failed",
                        result,
                        ncValue => MainWindow._produzioneRecord.NCBottomSealing = ncValue);
                }
                else
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("BottomSealing");
                    MainWindow._produzioneRecord.NCBottomSealing = 4;
                }

                if (IsFeatureEnabledForView(RoleBottom, "TrappedPaper"))
                {
                    await ValidateBottomBooleanOutputAsync(
                        toolBlock,
                        ResolveTrappedPaperOutputNames(),
                        ResolveTrappedPaperDefectOutputNames(),
                        "TrappedPaper",
                        "Trapped paper",
                        "Trapped paper detected in bottom sealing",
                        result,
                        ncValue => MainWindow._produzioneRecord.NCTrappedPaper = ncValue);
                }
                else
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("TrappedPaper");
                    MainWindow._produzioneRecord.NCTrappedPaper = 4;
                }

                await ValidateAiClassificationAsync(toolBlock, "Bottom", result);

                result.IsValid = result.ErrorMessages.Count == 0;
            }
            catch (Exception ex)
            {
                result.ErrorMessages.Add($"Bottom validation error: {ex.Message}");
            }

            return result;
        }

        private async Task ValidateBottomBooleanOutputAsync(
            CogToolBlock toolBlock,
            IEnumerable<string> passOutputNames,
            IEnumerable<string> defectOutputNames,
            string featureKey,
            string measurementName,
            string failureMessage,
            ValidationResult result,
            Action<int?> setNcFlag)
        {
            string matchedOutputName = null;
            bool passValue = false;
            var passNames = passOutputNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var defectNames = defectOutputNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (string outputName in passNames)
            {
                if (TryGetBooleanOutputValue(toolBlock, outputName, out bool outputValue))
                {
                    matchedOutputName = outputName;
                    passValue = outputValue;
                    break;
                }
            }

            if (matchedOutputName == null)
            {
                foreach (string outputName in defectNames)
                {
                    if (TryGetBooleanOutputValue(toolBlock, outputName, out bool defectDetected))
                    {
                        matchedOutputName = outputName;
                        passValue = !defectDetected;
                        break;
                    }
                }
            }

            if (matchedOutputName == null)
            {
                string missingOutputMessage =
                    $"{measurementName} output missing. Expected pass outputs: {string.Join(", ", passNames)}; defect outputs: {string.Join(", ", defectNames)}. Available outputs: {DescribeAvailableOutputs(toolBlock)}";
                result.ErrorMessages.Add(missingOutputMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, missingOutputMessage);
                setNcFlag?.Invoke(0);
                return;
            }

            result.Measurements.Add(measurementName, (passValue ? 1 : 0, 1, passValue));
            if (!passValue)
            {
                result.ErrorMessages.Add(failureMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, failureMessage);
                setNcFlag?.Invoke(0);
                return;
            }

            await ServiceLocator.CounterManager.ClearDefectMessage(featureKey);
            setNcFlag?.Invoke(1);
        }

        private async Task<ValidationResult> GetDetailedLeftValidationAsync(CogToolBlock toolBlock, ValidationResult result)
        {
            bool sideSealingEnabled = IsFeatureEnabledForView(RoleLeft, "SideSealing");
            bool rollCountEnabled = IsFeatureEnabledForView(RoleLeft, "SideRollCount");

            if (sideSealingEnabled)
            {
                await ValidateLeftBooleanOutputAsync(
                    toolBlock,
                    ResolveLeftSideSealingOutputNames(),
                    "SideSealing",
                    "Left side sealing",
                    "Left side sealing failed",
                    result,
                    () => MainWindow._produzioneRecord.NcSaldaturaLaterale = 1);
            }
            else
            {
                await ServiceLocator.CounterManager.ClearDefectMessage("SideSealing");
                MainWindow._produzioneRecord.NcSaldaturaLaterale = 4;
            }

            if (rollCountEnabled)
            {
                await ValidateLeftBooleanOutputAsync(
                    toolBlock,
                    ResolveLeftRollCountOutputNames(),
                    "SideRollCount",
                    "Roll count",
                    "Roll count failed",
                    result);
            }
            else
            {
                await ServiceLocator.CounterManager.ClearDefectMessage("SideRollCount");
            }

            await ValidateAiClassificationAsync(toolBlock, "Left", result);

            result.IsValid = result.ErrorMessages.Count == 0;
            return result;
        }

        private async Task ValidateLeftBooleanOutputAsync(
            CogToolBlock toolBlock,
            IEnumerable<string> outputNames,
            string featureKey,
            string measurementName,
            string failureMessage,
            ValidationResult result,
            Action onPassed = null)
        {
            string matchedOutputName = null;
            bool outputValue = false;

            foreach (string outputName in outputNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (TryGetBooleanOutputValue(toolBlock, outputName, out outputValue))
                {
                    matchedOutputName = outputName;
                    break;
                }
            }

            if (matchedOutputName == null)
            {
                string missingOutputMessage =
                    $"{measurementName} output missing. Expected one of: {string.Join(", ", outputNames)}. Available outputs: {DescribeAvailableOutputs(toolBlock)}";
                result.ErrorMessages.Add(missingOutputMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, missingOutputMessage);
                return;
            }

            result.Measurements.Add(measurementName, (outputValue ? 1 : 0, 1, outputValue));
            if (!outputValue)
            {
                result.ErrorMessages.Add(failureMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, failureMessage);
                return;
            }

            await ServiceLocator.CounterManager.ClearDefectMessage(featureKey);
            onPassed?.Invoke();
        }

        private static bool IsLeftInspectionToolBlock(CogToolBlock toolBlock)
        {
            return ContainsAnyOutput(toolBlock, ResolveLeftSideSealingOutputNames()) ||
                   ContainsAnyOutput(toolBlock, ResolveLeftRollCountOutputNames());
        }

        private static IEnumerable<string> ResolveLeftSideSealingOutputNames()
        {
            yield return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.LeftSideSealingOutput)
                ? DefaultLeftSideSealingOutputName
                : MainWindow.configManager.Config.Configuration.LeftSideSealingOutput;
            yield return "SideSealingOk";
            yield return "SealingOk";
            yield return "LeftSealingOk";
        }

        private static IEnumerable<string> ResolveLeftRollCountOutputNames()
        {
            yield return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.LeftRollCountOutput)
                ? DefaultLeftRollCountOutputName
                : MainWindow.configManager.Config.Configuration.LeftRollCountOutput;
            yield return "RollCountOk";
            yield return "RollsCountOk";
            yield return "RollCount";
        }

        private static IEnumerable<string> ResolveRightSideSealingOutputNames()
        {
            yield return DefaultRightSideSealingOutputName;
            yield return DefaultRearSideSealingOutputName;
            yield return "RightSealingOk";
            yield return "RearSealingOk";
            yield return "SideSealingOk";
            yield return "SealingOk";
        }

        private static IEnumerable<string> ResolveBottomSealingOutputNames()
        {
            yield return DefaultBottomSealingOutputName;
            yield return "BottomSealOk";
            yield return "LowerSealingOk";
            yield return "SaldaturaInferioreOk";
            yield return "BottomSealing";
        }

        private static IEnumerable<string> ResolveBottomSealingDefectOutputNames()
        {
            yield return "BottomSealingDefect";
            yield return "BottomSealDefect";
            yield return "BottomSealingNg";
            yield return "BottomSealingNok";
            yield return "SaldaturaInferioreKo";
            yield return "SaldaturaInferioreNok";
        }

        private static IEnumerable<string> ResolveTrappedPaperOutputNames()
        {
            yield return DefaultTrappedPaperOutputName;
            yield return "TrappedPaperOk";
            yield return "PaperClear";
            yield return "CartaIntrappolataOk";
            yield return "CartaInSaldaturaOk";
        }

        private static IEnumerable<string> ResolveTrappedPaperDefectOutputNames()
        {
            yield return "TrappedPaperDetected";
            yield return "TrappedPaper";
            yield return "PaperTrapped";
            yield return "CartaIntrappolata";
            yield return "CartaInSaldatura";
        }

        private static bool ContainsAnyOutput(CogToolBlock toolBlock, IEnumerable<string> outputNames)
        {
            return toolBlock?.Outputs != null &&
                   outputNames != null &&
                   outputNames.Any(name => !string.IsNullOrWhiteSpace(name) && toolBlock.Outputs.Contains(name));
        }

        private static bool HasAnyShapeOutput(CogToolBlock toolBlock)
        {
            return ContainsAnyOutput(toolBlock, new[] { "A_T_L", "A_T_R", "A_B_L", "A_B_R" });
        }

        /// <summary>
        /// Validates the optional Front camera traceability step.
        ///
        /// The Front inspection is intentionally lightweight: it checks whether the
        /// traceability mark/code is present and, when present, stores the code
        /// returned by VisionPro inside the production record snapshot.
        /// </summary>
        public async Task<ValidationResult> GetDetailedFrontValidationAsync(CogToolBlock toolBlock)
        {
            await _recipeParam.EnsureLoadedAsync();

            var recipe = _recipeParam.Config?.recipeParamFront ?? new QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters.RecipeParamFront();
            var result = new ValidationResult
            {
                Measurements = new Dictionary<string, (double, double, bool)>(),
                ErrorMessages = new List<string>()
            };

            try
            {
                bool traceabilityEnabled = IsFeatureEnabledForView(RoleFront, "FrontTraceability");

                if (!traceabilityEnabled)
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("FrontTraceability");
                    MainWindow._produzioneRecord.TraceabilityDetected = 4;
                    MainWindow._produzioneRecord.NCTraceability = 4;
                    await ValidateAiClassificationAsync(toolBlock, "Front", result);
                    result.IsValid = result.ErrorMessages.Count == 0;
                    return result;
                }

                string presenceOutputName = ResolveFrontTraceabilityPresenceOutputName();
                string codeOutputName = ResolveFrontTraceabilityCodeOutputName();

                bool hasPresence = TryGetBooleanOutputValue(toolBlock, presenceOutputName, out bool traceabilityPresent);
                bool hasCode = TryGetStringOutputValue(toolBlock, codeOutputName, out string traceabilityCode);

                if (!hasPresence && hasCode)
                {
                    traceabilityPresent = !string.IsNullOrWhiteSpace(traceabilityCode);
                    hasPresence = true;
                }

                MainWindow._produzioneRecord.TraceabilityDetected = hasPresence ? (traceabilityPresent ? 1 : 0) : (int?)null;
                MainWindow._produzioneRecord.TraceabilityCode = traceabilityCode;
                MainWindow._produzioneRecord.TraceabilityExpectedPrefix = recipe.ExpectedCodePrefix;

                result.Measurements.Add(
                    "FrontTraceability",
                    (traceabilityPresent ? 1d : 0d, recipe.RequireTraceability ? 1d : 0d, !recipe.RequireTraceability || traceabilityPresent));

                if (!hasPresence && !hasCode)
                {
                    string missingOutputMessage =
                        $"Front traceability outputs not found in tool block. Presence='{presenceOutputName}', Code='{codeOutputName}'.";
                    result.ErrorMessages.Add(missingOutputMessage);
                    await ServiceLocator.CounterManager.IncrementDefectAsync("traceability", missingOutputMessage);
                    MainWindow._produzioneRecord.NCTraceability = 0;
                }
                else if (recipe.RequireTraceability && !traceabilityPresent)
                {
                    string missingTraceabilityMessage = "Front traceability not detected.";
                    result.ErrorMessages.Add(missingTraceabilityMessage);
                    await ServiceLocator.CounterManager.IncrementDefectAsync("traceability", missingTraceabilityMessage);
                    MainWindow._produzioneRecord.NCTraceability = 0;
                }
                else if (traceabilityPresent && string.IsNullOrWhiteSpace(traceabilityCode))
                {
                    string emptyCodeMessage = "Front traceability detected but VisionPro returned an empty code.";
                    result.ErrorMessages.Add(emptyCodeMessage);
                    await ServiceLocator.CounterManager.IncrementDefectAsync("traceability", emptyCodeMessage);
                    MainWindow._produzioneRecord.NCTraceability = 0;
                }
                else
                {
                    await ServiceLocator.CounterManager.ClearDefectMessage("FrontTraceability");
                    MainWindow._produzioneRecord.NCTraceability = 1;
                }

                await ValidateAiClassificationAsync(toolBlock, "Front", result);

                result.IsValid = result.ErrorMessages.Count == 0;
            }
            catch (Exception ex)
            {
                result.ErrorMessages.Add($"Front traceability validation error: {ex.Message}");
                MainWindow.logger.Error($"Front traceability validation error: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }

        public async Task<ValidationResult> GetDetailedTop3DValidationAsync(CogToolBlock toolBlock)
        {
            await _recipeParam.EnsureLoadedAsync();

            var recipe = _recipeParam.Config?.recipeParamTop3D
                ?? new QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters.RecipeParamTop3D();
            var result = new ValidationResult
            {
                Measurements = new Dictionary<string, (double, double, bool)>(),
                ErrorMessages = new List<string>()
            };

            try
            {
                await ValidateTopThreeDMeasurementsAsync(toolBlock, recipe, result);
                await ValidateAiClassificationAsync(toolBlock, "Top3D", result);
                result.IsValid = result.ErrorMessages.Count == 0;
            }
            catch (Exception ex)
            {
                result.ErrorMessages.Add($"Top3D validation error: {ex.Message}");
                MainWindow.logger.Error($"Top3D validation error: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }

        private async Task ValidateTopThreeDMeasurementsAsync(
            CogToolBlock toolBlock,
            QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters.RecipeParamTop3D recipe,
            ValidationResult result)
        {
            if (toolBlock == null)
            {
                return;
            }

            var machineConfiguration = MainWindow.configManager?.Config?.Configuration;

            await CaptureAndValidateTopThreeDProfileAsync(toolBlock, recipe, result);

            await ValidateTopThreeDHeightMeasurementAsync(
                toolBlock,
                result,
                recipe.ThreeDHeightNominalValue,
                recipe.ThreeDHeightTolerance,
                machineConfiguration?.Top3DHeightMeasurementOffsetMm ?? 0,
                measuredValue =>
                {
                    MainWindow._produzioneRecord.ThreeDHeightMeasureValue = measuredValue;
                    MainWindow._produzioneRecord.ThreeDHeightNominalValue = recipe.ThreeDHeightNominalValue;
                    MainWindow._produzioneRecord.ThreeDHeightMinimum = recipe.ThreeDHeightNominalValue - recipe.ThreeDHeightTolerance;
                    MainWindow._produzioneRecord.ThreeDHeightMaximum = recipe.ThreeDHeightNominalValue + recipe.ThreeDHeightTolerance;
                },
                ncValue => MainWindow._produzioneRecord.NCThreeDHeight = ncValue);

            await ValidateTopThreeDMeasurementAsync(
                toolBlock,
                result,
                "ThreeDWidth",
                "3D width",
                ResolveThreeDWidthOutputName(),
                recipe.ThreeDWidthNominalValue,
                recipe.ThreeDWidthTolerance,
                machineConfiguration?.Top3DWidthMeasurementOffsetMm ?? 0,
                measuredValue =>
                {
                    MainWindow._produzioneRecord.ThreeDWidthMeasureValue = measuredValue;
                    MainWindow._produzioneRecord.ThreeDWidthNominalValue = recipe.ThreeDWidthNominalValue;
                    MainWindow._produzioneRecord.ThreeDWidthMinimum = recipe.ThreeDWidthNominalValue - recipe.ThreeDWidthTolerance;
                    MainWindow._produzioneRecord.ThreeDWidthMaximum = recipe.ThreeDWidthNominalValue + recipe.ThreeDWidthTolerance;
                },
                ncValue => MainWindow._produzioneRecord.NCThreeDWidth = ncValue);

            await ValidateTopThreeDMeasurementAsync(
                toolBlock,
                result,
                "ThreeDLength",
                "3D length",
                ResolveThreeDLengthOutputName(),
                recipe.ThreeDLengthNominalValue,
                recipe.ThreeDLengthTolerance,
                machineConfiguration?.Top3DLengthMeasurementOffsetMm ?? 0,
                measuredValue =>
                {
                    MainWindow._produzioneRecord.ThreeDLengthMeasureValue = measuredValue;
                    MainWindow._produzioneRecord.ThreeDLengthNominalValue = recipe.ThreeDLengthNominalValue;
                    MainWindow._produzioneRecord.ThreeDLengthMinimum = recipe.ThreeDLengthNominalValue - recipe.ThreeDLengthTolerance;
                    MainWindow._produzioneRecord.ThreeDLengthMaximum = recipe.ThreeDLengthNominalValue + recipe.ThreeDLengthTolerance;
                },
                ncValue => MainWindow._produzioneRecord.NCThreeDLength = ncValue);
        }

        private async Task CaptureAndValidateTopThreeDProfileAsync(
            CogToolBlock toolBlock,
            QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters.RecipeParamTop3D recipe,
            ValidationResult result)
        {
            var monitoring = new ProcessMonitoringResult
            {
                FeatureKey = "ThreeDProfile",
                RejectEnabled = false,
                State = ProcessMonitoringState.Wait
            };
            result.MonitoringResults[monitoring.FeatureKey] = monitoring;

            string medianOutputName = ResolveThreeDHeightMedianOutputName();
            string highTailOutputName = ResolveThreeDHeightHighTailOutputName();
            string maximumOutputName = ResolveThreeDHeightMaximumOutputName();
            string bulgeOutputName = ResolveThreeDHeightBulgeOutputName();
            string validPixelRatioOutputName = ResolveThreeDHeightValidPixelRatioOutputName();

            bool medianRead = TryGetOutputValue(toolBlock, medianOutputName, out double medianValue);
            bool highTailRead = TryGetOutputValue(toolBlock, highTailOutputName, out double highTailValue);
            bool maximumRead = TryGetOutputValue(toolBlock, maximumOutputName, out double maximumValue);
            bool bulgeRead = TryGetOutputValue(toolBlock, bulgeOutputName, out double bulgeValue);
            bool ratioRead = TryGetOutputValue(toolBlock, validPixelRatioOutputName, out double validPixelRatio);

            bool medianFinite = medianRead && TopThreeDMeasurementCorrection.IsFinite(medianValue);
            bool highTailFinite = highTailRead && TopThreeDMeasurementCorrection.IsFinite(highTailValue);
            bool maximumFinite = maximumRead && TopThreeDMeasurementCorrection.IsFinite(maximumValue);
            bool bulgeFinite = bulgeRead && TopThreeDMeasurementCorrection.IsFinite(bulgeValue);
            bool ratioFinite = ratioRead && TopThreeDMeasurementCorrection.IsFinite(validPixelRatio);

            if (MainWindow._produzioneRecord != null)
            {
                MainWindow._produzioneRecord.ThreeDHeightMedianValue = medianFinite ? (double?)medianValue : null;
                MainWindow._produzioneRecord.ThreeDHeightHighTailValue = highTailFinite ? (double?)highTailValue : null;
                MainWindow._produzioneRecord.ThreeDHeightMaximumValue = maximumFinite ? (double?)maximumValue : null;
                MainWindow._produzioneRecord.ThreeDHeightBulgeValue = bulgeFinite ? (double?)bulgeValue : null;
                MainWindow._produzioneRecord.ThreeDHeightValidPixelRatio = ratioFinite ? (double?)validPixelRatio : null;
                MainWindow._produzioneRecord.NCThreeDProfile = 4;
            }

            if (medianFinite)
            {
                result.Measurements["ThreeDHeightMedian"] = (medianValue, 0, true);
            }

            if (highTailFinite)
            {
                result.Measurements["ThreeDHeightHighTail"] = (highTailValue, 0, true);
            }

            if (maximumFinite)
            {
                result.Measurements["ThreeDHeightMaximum"] = (maximumValue, 0, true);
            }

            double minimumValidPixelRatio = NormalizeMinimumValidPixelRatio(
                recipe.ThreeDHeightMinimumValidPixelRatio);
            double maximumBulge = recipe.ThreeDHeightMaximumBulge;
            bool thresholdConfigured =
                TopThreeDMeasurementCorrection.IsFinite(maximumBulge) &&
                maximumBulge > 0;

            if (ratioFinite)
            {
                bool ratioInDomain = validPixelRatio >= 0 && validPixelRatio <= 1;
                result.Measurements["ThreeDHeightValidPixelRatio"] =
                    (validPixelRatio, minimumValidPixelRatio, ratioInDomain && validPixelRatio >= minimumValidPixelRatio);
            }

            if (bulgeFinite)
            {
                double normalizedBulge = NormalizeBulgeForComparison(bulgeValue);
                result.Measurements["ThreeDHeightBulge"] =
                    (bulgeValue, maximumBulge, normalizedBulge >= 0 && (!thresholdConfigured || normalizedBulge <= maximumBulge));
            }

            bool anyProfileValueAvailable =
                medianFinite || highTailFinite || maximumFinite || bulgeFinite || ratioFinite;

            if (!recipe.ThreeDProfileEnabled || !thresholdConfigured)
            {
                monitoring.State = anyProfileValueAvailable
                    ? ProcessMonitoringState.Monitor
                    : ProcessMonitoringState.Wait;
                monitoring.Summary = BuildTopThreeDProfileSummary(
                    monitoring.State,
                    medianFinite ? (double?)medianValue : null,
                    highTailFinite ? (double?)highTailValue : null,
                    maximumFinite ? (double?)maximumValue : null,
                    bulgeFinite ? (double?)bulgeValue : null,
                    ratioFinite ? (double?)validPixelRatio : null,
                    maximumBulge,
                    minimumValidPixelRatio);
                return;
            }

            if (!ratioRead)
            {
                AddProfileMonitoringIssue(
                    monitoring,
                    ProcessMonitoringState.Invalid,
                    $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfileAcquisitionInvalid", "Top3D profile acquisition invalid")}: " +
                    $"output '{validPixelRatioOutputName}' not found. AvailableOutputs='{DescribeAvailableOutputs(toolBlock)}'.");
            }
            else if (!ratioFinite || validPixelRatio < 0 || validPixelRatio > 1)
            {
                AddProfileMonitoringIssue(
                    monitoring,
                    ProcessMonitoringState.Invalid,
                    $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfileAcquisitionInvalid", "Top3D profile acquisition invalid")}: " +
                    $"output '{validPixelRatioOutputName}' returned '{validPixelRatio}'. Expected a finite ratio in range 0..1.");
            }

            if (!bulgeRead)
            {
                AddProfileMonitoringIssue(
                    monitoring,
                    ProcessMonitoringState.Invalid,
                    $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfileAcquisitionInvalid", "Top3D profile acquisition invalid")}: " +
                    $"output '{bulgeOutputName}' not found. AvailableOutputs='{DescribeAvailableOutputs(toolBlock)}'.");
            }
            else if (!bulgeFinite || bulgeValue < -ThreeDProfileBulgeNegativeToleranceMm)
            {
                AddProfileMonitoringIssue(
                    monitoring,
                    ProcessMonitoringState.Invalid,
                    $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfileAcquisitionInvalid", "Top3D profile acquisition invalid")}: " +
                    $"bulge '{bulgeValue}' mm from output '{bulgeOutputName}'. Expected a finite value greater than or equal to zero.");
            }

            if (monitoring.State != ProcessMonitoringState.Invalid &&
                validPixelRatio < minimumValidPixelRatio)
            {
                AddProfileMonitoringIssue(
                    monitoring,
                    ProcessMonitoringState.Invalid,
                    $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfileAcquisitionInvalid", "Top3D profile acquisition invalid")}: " +
                    $"valid pixel ratio {validPixelRatio:P1}, minimum {minimumValidPixelRatio:P1}.");
            }

            if (monitoring.State != ProcessMonitoringState.Invalid)
            {
                double normalizedBulge = NormalizeBulgeForComparison(bulgeValue);
                if (normalizedBulge > maximumBulge)
                {
                    AddProfileMonitoringIssue(
                        monitoring,
                        ProcessMonitoringState.NoGood,
                        $"{ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfileOutOfTolerance", "Top3D profile out of tolerance")}: " +
                        $"bulge {normalizedBulge:0.###} mm, maximum {maximumBulge:0.###} mm.");
                }
                else
                {
                    monitoring.State = ProcessMonitoringState.Good;
                }
            }

            if (MainWindow._produzioneRecord != null)
            {
                MainWindow._produzioneRecord.NCThreeDProfile =
                    monitoring.State == ProcessMonitoringState.Good ? 1 : 0;
            }

            monitoring.Summary = BuildTopThreeDProfileSummary(
                monitoring.State,
                medianFinite ? (double?)medianValue : null,
                highTailFinite ? (double?)highTailValue : null,
                maximumFinite ? (double?)maximumValue : null,
                bulgeFinite ? (double?)bulgeValue : null,
                ratioFinite ? (double?)validPixelRatio : null,
                maximumBulge,
                minimumValidPixelRatio);

            if (monitoring.IsDefect)
            {
                MainWindow.logger?.Warn(
                    "TOP3D_PROFILE_MONITOR|state={0}|bulge={1}|maximumBulge={2}|validPixelRatio={3}|minimumValidPixelRatio={4}|rejectEnabled=false|message={5}",
                    monitoring.State,
                    bulgeFinite ? bulgeValue.ToString("0.###", CultureInfo.InvariantCulture) : "<invalid>",
                    maximumBulge.ToString("0.###", CultureInfo.InvariantCulture),
                    ratioFinite ? validPixelRatio.ToString("0.#####", CultureInfo.InvariantCulture) : "<invalid>",
                    minimumValidPixelRatio.ToString("0.#####", CultureInfo.InvariantCulture),
                    string.Join(" ", monitoring.Messages));
            }

            await Task.CompletedTask;
        }

        private async Task ValidateTopThreeDHeightMeasurementAsync(
            CogToolBlock toolBlock,
            ValidationResult result,
            double nominalValue,
            double toleranceValue,
            double configuredOffsetMm,
            Action<double> assignMeasurement,
            Action<int> assignNcValue)
        {
            const string featureKey = "ThreeDHeight";
            const string displayName = "3D height";

            if (!IsFeatureEnabledForView(RoleTop3D, featureKey))
            {
                await ServiceLocator.CounterManager.ClearDefectMessage(featureKey);
                assignNcValue(4);
                return;
            }

            string medianOutputName = ResolveThreeDHeightMedianOutputName();
            string legacyOutputName = ResolveThreeDHeightOutputName();
            bool medianRead = TryGetOutputValue(toolBlock, medianOutputName, out double medianValue);
            bool legacyRead = TryGetOutputValue(toolBlock, legacyOutputName, out double legacyValue);
            bool medianFinite = medianRead && TopThreeDMeasurementCorrection.IsFinite(medianValue);
            bool legacyFinite = legacyRead && TopThreeDMeasurementCorrection.IsFinite(legacyValue);

            double resolvedValue;
            string resolvedOutputName;
            if (medianFinite)
            {
                resolvedValue = medianValue;
                resolvedOutputName = medianOutputName;
            }
            else if (legacyFinite)
            {
                resolvedValue = legacyValue;
                resolvedOutputName = legacyOutputName;
                MainWindow.logger?.Debug(
                    "TOP3D_HEIGHT_LEGACY_FALLBACK|medianOutput={0}|legacyOutput={1}",
                    medianOutputName,
                    legacyOutputName);
            }
            else
            {
                string missingOutputMessage =
                    $"{displayName} has no finite measurement. PreferredOutput='{medianOutputName}', LegacyOutput='{legacyOutputName}', AvailableOutputs='{DescribeAvailableOutputs(toolBlock)}'.";
                result.ErrorMessages.Add(missingOutputMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, missingOutputMessage);
                assignNcValue(0);
                return;
            }

            await ValidateTopThreeDMeasurementValueAsync(
                result,
                featureKey,
                displayName,
                resolvedOutputName,
                resolvedValue,
                nominalValue,
                toleranceValue,
                configuredOffsetMm,
                assignMeasurement,
                assignNcValue);
        }

        private async Task ValidateTopThreeDMeasurementAsync(
            CogToolBlock toolBlock,
            ValidationResult result,
            string featureKey,
            string displayName,
            string outputName,
            double nominalValue,
            double toleranceValue,
            double configuredOffsetMm,
            Action<double> assignMeasurement,
            Action<int> assignNcValue)
        {
            if (!IsFeatureEnabledForView(RoleTop3D, featureKey))
            {
                await ServiceLocator.CounterManager.ClearDefectMessage(featureKey);
                assignNcValue(4);
                return;
            }

            if (!TryGetOutputValue(toolBlock, outputName, out double rawMeasurementValue))
            {
                string missingOutputMessage =
                    $"{displayName} output not found in tool block. Output='{outputName}'. AvailableOutputs='{DescribeAvailableOutputs(toolBlock)}'.";
                result.ErrorMessages.Add(missingOutputMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, missingOutputMessage);
                assignNcValue(0);
                return;
            }

            if (!TopThreeDMeasurementCorrection.IsFinite(rawMeasurementValue))
            {
                string invalidOutputMessage =
                    $"{displayName} returned a non-finite measurement. Output='{outputName}', value='{rawMeasurementValue}'.";
                result.ErrorMessages.Add(invalidOutputMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, invalidOutputMessage);
                assignNcValue(0);
                return;
            }

            await ValidateTopThreeDMeasurementValueAsync(
                result,
                featureKey,
                displayName,
                outputName,
                rawMeasurementValue,
                nominalValue,
                toleranceValue,
                configuredOffsetMm,
                assignMeasurement,
                assignNcValue);
        }

        private async Task ValidateTopThreeDMeasurementValueAsync(
            ValidationResult result,
            string featureKey,
            string displayName,
            string outputName,
            double rawMeasurementValue,
            double nominalValue,
            double toleranceValue,
            double configuredOffsetMm,
            Action<double> assignMeasurement,
            Action<int> assignNcValue)
        {
            double measuredValue = TopThreeDMeasurementCorrection.Apply(
                rawMeasurementValue,
                configuredOffsetMm,
                out double appliedOffsetMm);

            if (!TopThreeDMeasurementCorrection.IsFinite(measuredValue))
            {
                string invalidCorrectionMessage =
                    $"{displayName} correction produced a non-finite measurement. Raw='{rawMeasurementValue}', offset='{appliedOffsetMm}'.";
                result.ErrorMessages.Add(invalidCorrectionMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, invalidCorrectionMessage);
                assignNcValue(0);
                return;
            }

            assignMeasurement(measuredValue);
            bool isWithinTolerance = Math.Abs(measuredValue - nominalValue) <= toleranceValue;
            result.Measurements[featureKey] = (measuredValue, nominalValue, isWithinTolerance);

            if (!isWithinTolerance)
            {
                string correctionDetail = Math.Abs(appliedOffsetMm) > 0.0000001
                    ? $" Raw: {rawMeasurementValue:0.###}, offset: {appliedOffsetMm:+0.###;-0.###;0}."
                    : string.Empty;
                string toleranceMessage =
                    $"{displayName} out of tolerance: {measuredValue:0.###}.{correctionDetail} Expected: {nominalValue:0.###} +/- {toleranceValue:0.###}.";
                result.ErrorMessages.Add(toleranceMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(featureKey, toleranceMessage);
                assignNcValue(0);
                return;
            }

            await ServiceLocator.CounterManager.ClearDefectMessage(featureKey);
            assignNcValue(1);
        }

        private static void AddProfileMonitoringIssue(
            ProcessMonitoringResult monitoring,
            ProcessMonitoringState state,
            string message)
        {
            if (monitoring == null)
            {
                return;
            }

            if (monitoring.State != ProcessMonitoringState.Invalid ||
                state == ProcessMonitoringState.Invalid)
            {
                monitoring.State = state;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                monitoring.Messages.Add(message);
            }
        }

        private static double NormalizeMinimumValidPixelRatio(double configuredValue)
        {
            if (!TopThreeDMeasurementCorrection.IsFinite(configuredValue))
            {
                return ThreeDProfileDefaultMinimumValidPixelRatio;
            }

            return Math.Max(0, Math.Min(1, configuredValue));
        }

        private static double NormalizeBulgeForComparison(double bulgeValue)
        {
            if (bulgeValue < 0 && bulgeValue >= -ThreeDProfileBulgeNegativeToleranceMm)
            {
                return 0;
            }

            return bulgeValue;
        }

        private static string BuildTopThreeDProfileSummary(
            ProcessMonitoringState state,
            double? medianValue,
            double? highTailValue,
            double? maximumValue,
            double? bulgeValue,
            double? validPixelRatio,
            double maximumBulge,
            double minimumValidPixelRatio)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Top3D profile: state={0}; median={1} mm; highTail={2} mm; maximum={3} mm; bulge={4} mm; maximumBulge={5} mm; validPixels={6}; minimumValidPixels={7}; reject=OFF",
                state.ToString().ToUpperInvariant(),
                FormatProfileNumber(medianValue),
                FormatProfileNumber(highTailValue),
                FormatProfileNumber(maximumValue),
                FormatProfileNumber(bulgeValue),
                TopThreeDMeasurementCorrection.IsFinite(maximumBulge) && maximumBulge > 0
                    ? maximumBulge.ToString("0.###", CultureInfo.InvariantCulture)
                    : "<not-configured>",
                validPixelRatio.HasValue
                    ? validPixelRatio.Value.ToString("P1", CultureInfo.InvariantCulture)
                    : "<unavailable>",
                minimumValidPixelRatio.ToString("P1", CultureInfo.InvariantCulture));
        }

        private static string FormatProfileNumber(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "<unavailable>";
        }

        private bool TryGetOutputValue(CogToolBlock toolBlock, string outputName, out double value)
        {
            value = 0;
            if (toolBlock == null || string.IsNullOrWhiteSpace(outputName) || toolBlock.Outputs == null)
            {
                return false;
            }

            if (!toolBlock.Outputs.Contains(outputName))
            {
                return false;
            }

            try
            {
                var outputValue = toolBlock.Outputs[outputName].Value;
                if (outputValue is double numericValue)
                {
                    value = numericValue;
                    return true;
                }
                else if (outputValue is int intValue)
                {
                    value = intValue;
                    return true;
                }

                else
                {
                    return false;
                }

            }
            catch
            {
                return false;
            }
        }

        private static string DescribeAvailableOutputs(CogToolBlock toolBlock)
        {
            try
            {
                if (toolBlock?.Outputs == null)
                {
                    return "<none>";
                }

                var outputNames = new List<string>();
                for (int i = 0; i < toolBlock.Outputs.Count; i++)
                {
                    string name = toolBlock.Outputs[i]?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        outputNames.Add(name);
                    }
                }

                return outputNames.Count == 0
                    ? "<none>"
                    : string.Join(", ", outputNames);
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private bool TryGetBooleanOutputValue(CogToolBlock toolBlock, string outputName, out bool value)
        {
            value = false;
            if (toolBlock == null || toolBlock.Outputs == null || string.IsNullOrWhiteSpace(outputName) || !toolBlock.Outputs.Contains(outputName))
            {
                return false;
            }

            try
            {
                var outputValue = toolBlock.Outputs[outputName].Value;
                switch (outputValue)
                {
                    case bool boolValue:
                        value = boolValue;
                        return true;
                    case int intValue:
                        value = intValue != 0;
                        return true;
                    case double doubleValue:
                        value = Math.Abs(doubleValue) > double.Epsilon;
                        return true;
                    case string stringValue:
                        if (bool.TryParse(stringValue, out bool parsedBool))
                        {
                            value = parsedBool;
                            return true;
                        }

                        if (double.TryParse(stringValue, out double parsedNumeric))
                        {
                            value = Math.Abs(parsedNumeric) > double.Epsilon;
                            return true;
                        }

                        string normalizedStringValue = stringValue.Trim();
                        if (string.Equals(normalizedStringValue, "OK", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "GOOD", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "PASS", StringComparison.OrdinalIgnoreCase))
                        {
                            value = true;
                            return true;
                        }

                        if (string.Equals(normalizedStringValue, "NG", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "NOK", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "KO", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "FAIL", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "BAD", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStringValue, "NOREAD", StringComparison.OrdinalIgnoreCase))
                        {
                            value = false;
                            return true;
                        }

                        value = !string.IsNullOrWhiteSpace(stringValue);
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetStringOutputValue(CogToolBlock toolBlock, string outputName, out string value)
        {
            value = null;
            if (toolBlock == null || string.IsNullOrWhiteSpace(outputName) || !toolBlock.Outputs.Contains(outputName))
            {
                return false;
            }

            try
            {
                var outputValue = toolBlock.Outputs[outputName].Value;
                if (outputValue == null)
                {
                    return false;
                }

                value = outputValue.ToString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveFrontTraceabilityPresenceOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.FrontTraceabilityPresenceOutput)
                ? DefaultFrontTraceabilityPresenceOutputName
                : MainWindow.configManager.Config.Configuration.FrontTraceabilityPresenceOutput;
        }

        private static string ResolveFrontTraceabilityCodeOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.FrontTraceabilityCodeOutput)
                ? DefaultFrontTraceabilityCodeOutputName
                : MainWindow.configManager.Config.Configuration.FrontTraceabilityCodeOutput;
        }

        private static string ResolveThreeDHeightOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDHeightOutput)
                ? DefaultThreeDHeightOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDHeightOutput;
        }

        private static string ResolveThreeDHeightMedianOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDHeightMedianOutput)
                ? DefaultThreeDHeightMedianOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDHeightMedianOutput;
        }

        private static string ResolveThreeDHeightHighTailOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDHeightHighTailOutput)
                ? DefaultThreeDHeightHighTailOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDHeightHighTailOutput;
        }

        private static string ResolveThreeDHeightValidPixelRatioOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDHeightValidPixelRatioOutput)
                ? DefaultThreeDHeightValidPixelRatioOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDHeightValidPixelRatioOutput;
        }

        private static string ResolveThreeDHeightBulgeOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDHeightBulgeOutput)
                ? DefaultThreeDHeightBulgeOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDHeightBulgeOutput;
        }

        private static string ResolveThreeDHeightMaximumOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDHeightMaximumOutput)
                ? DefaultThreeDHeightMaximumOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDHeightMaximumOutput;
        }

        private static string ResolveThreeDWidthOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDWidthOutput)
                ? DefaultThreeDWidthOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDWidthOutput;
        }

        private static string ResolveThreeDLengthOutputName()
        {
            return string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.ThreeDLengthOutput)
                ? DefaultThreeDLengthOutputName
                : MainWindow.configManager.Config.Configuration.ThreeDLengthOutput;
        }
        private async Task ValidateAiClassificationAsync(
            CogToolBlock toolBlock,
            string cameraRole,
            ValidationResult result)
        {
            if (result == null ||
                !IsFeatureEnabledForView(cameraRole, "AIClassification") ||
                !VisionClassificationResultReader.HasClassificationOutput(toolBlock))
            {
                return;
            }

            CameraClassificationResult classification = VisionClassificationResultReader.Read(toolBlock);
            if (classification == null || string.IsNullOrWhiteSpace(classification.ClassName))
            {
                string missingResultMessage =
                    $"AI classification failed [{cameraRole}]: class output is empty or unreadable.";
                result.ErrorMessages.Add(missingResultMessage);
                await ServiceLocator.CounterManager.IncrementDefectAsync(
                    "AIClassification",
                    missingResultMessage);
                return;
            }

            // Tassonomia per vista: un modello Edge Learning e' addestrato per camera, quindi
            // le classi di OK cambiano da vista a vista ("Conforme" sulla Top puo' non
            // esistere sulla Rear). Se per questa vista e' stata dichiarata una lista di
            // classi accettate, decide quella. Senza policy vale il comportamento storico
            // basato sulle etichette standard di VisionClassificationResultReader.
            var policy = _configService.ClassificationPolicy;
            bool hasPolicy = policy.HasPolicyFor(cameraRole);
            bool classificationPassed = hasPolicy
                ? policy.IsAccepted(cameraRole, classification.ClassName)
                : classification.State == CameraClassificationState.Passed;
            double score = classification.Score ?? 0d;

            // Soglia di confidenza per vista: una classe accettata ma riconosciuta con
            // punteggio basso non e' un OK debole, e' un caso non verificato. Puo' essere un
            // prodotto diverso da quello di ricetta, o una condizione che il modello Edge
            // Learning non ha mai visto in addestramento. In entrambi i casi il pezzo non va
            // lasciato passare: si declassa a non classificato e si manda a scarto.
            double minimumScore = policy.GetMinimumScore(cameraRole);
            bool lowConfidence = false;
            if (classificationPassed && minimumScore > 0d)
            {
                // Senza punteggio la soglia non e' verificabile. Trattarlo come conforme
                // vanificherebbe la soglia proprio sui job che non espongono lo Score, quindi
                // l'assenza del valore conta come non verificato.
                lowConfidence = !classification.Score.HasValue ||
                                classification.Score.Value < minimumScore;

                if (lowConfidence)
                {
                    classificationPassed = false;
                    result.IsAiClassificationUnclassified = true;
                }
            }
            result.Measurements["Classification"] = (score, 1d, classificationPassed);
            result.Measurements["AIClassification"] = (score, 1d, classificationPassed);
            if (classification.Score.HasValue)
            {
                result.Measurements["ClassificationScore"] = (score, 1d, classificationPassed);
            }

            if (classificationPassed)
            {
                return;
            }

            string scoreText = classification.Score.HasValue
                ? classification.Score.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "n/a";
            string failureMessage;
            if (lowConfidence)
            {
                // Messaggio distinto da quello di classe non accettata: in diagnostica serve
                // sapere che la classe era giusta ma il modello non era abbastanza sicuro.
                failureMessage =
                    $"AI classification unclassified [{cameraRole}]: Class={classification.ClassName}, " +
                    $"Score={scoreText} below minimum {minimumScore.ToString("0.###", CultureInfo.InvariantCulture)}. " +
                    "Treated as not trained / unverified case.";
            }
            else
            {
                string expectation = hasPolicy
                    ? $"Accepted classes for this view: {policy.GetAcceptedClassesText(cameraRole)}."
                    : "Expected a passing label (OK, GOOD, PASS, PASSED or COMPLIANT).";
                failureMessage =
                    $"AI classification failed [{cameraRole}]: Class={classification.ClassName}, Score={scoreText}. " +
                    expectation;
            }
            result.ErrorMessages.Add(failureMessage);
            await ServiceLocator.CounterManager.IncrementDefectAsync(
                "AIClassification",
                failureMessage);
        }

        public async Task<PrintCenteringValidation> ValidatePrintCenteringAsync(CogToolBlock toolBlock)
        {
            await _recipeParam.EnsureLoadedAsync();
            var recipe = _recipeParam.Config.recipeParamTop;
            var result = new PrintCenteringValidation
            {
                DirectionResults = new Dictionary<string, CenteringResult>()
            };
            MainWindow._produzioneRecord.TopValueToMarker = recipe.TopValueToLogo;
            MainWindow._produzioneRecord.LeftValueToMarker = recipe.LeftValueToLogo;
            MainWindow._produzioneRecord.RigthValueToMarker = recipe.RigthValueToLogo;
            MainWindow._produzioneRecord.BottomValueToMarker = recipe.BottomValueToLogo;
            //MainWindow._produzioneRecord. = recipe.Print_centering_Toll;
            try
            {
                // Validate Top Value
                if (TryGetOutputValue(toolBlock, "D_T_Logo", out double topValue))
                {
                    var topResult = new CenteringResult
                    {

                        MeasuredValue = topValue,
                        NominalValue = recipe.TopValueToLogo,
                        Tolerance = recipe.Print_centering_Toll,
                        IsWithinTolerance = Math.Abs(topValue - recipe.TopValueToLogo) <= recipe.Print_centering_Toll
                    };
                    MainWindow._produzioneRecord.TopValueToMarker = topValue;
                    result.DirectionResults.Add("Top", topResult);
                }

                // Validate Left Value
                if (TryGetOutputValue(toolBlock, "D_L_Logo", out double leftValue))
                {
                    var leftResult = new CenteringResult
                    {
                        MeasuredValue = leftValue,
                        NominalValue = recipe.LeftValueToLogo,
                        Tolerance = recipe.Print_centering_Toll,
                        IsWithinTolerance = Math.Abs(leftValue - recipe.LeftValueToLogo) <= recipe.Print_centering_Toll
                    };
                    MainWindow._produzioneRecord.LeftValueToMarker = leftValue;
                    result.DirectionResults.Add("Left", leftResult);
                }

                // Validate Right Value
                if (TryGetOutputValue(toolBlock, "D_R_Logo", out double rightValue))
                {
                    var rightResult = new CenteringResult
                    {
                        MeasuredValue = rightValue,
                        NominalValue = recipe.RigthValueToLogo,
                        Tolerance = recipe.Print_centering_Toll,
                        IsWithinTolerance = Math.Abs(rightValue - recipe.RigthValueToLogo) <= recipe.Print_centering_Toll
                    };
                    MainWindow._produzioneRecord.RigthValueToMarker = rightValue;
                    result.DirectionResults.Add("Right", rightResult);
                }

                // Validate Bottom Value
                if (TryGetOutputValue(toolBlock, "D_B_Logo", out double bottomValue))
                {
                    var bottomResult = new CenteringResult
                    {
                        MeasuredValue = bottomValue,
                        NominalValue = recipe.BottomValueToLogo,
                        Tolerance = recipe.Print_centering_Toll,
                        IsWithinTolerance = Math.Abs(bottomValue - recipe.BottomValueToLogo) <= recipe.Print_centering_Toll
                    };
                    MainWindow._produzioneRecord.BottomValueToMarker = bottomValue;
                    result.DirectionResults.Add("Bottom", bottomResult);
                }

                result.IsCentered = result.DirectionResults.Count > 0 &&
                                  result.DirectionResults.All(r => r.Value.IsWithinTolerance);
            }
            catch (Exception)
            {
                result.DirectionResults.Clear();
                result.DirectionResults.Add("Error", new CenteringResult
                {
                    MeasuredValue = 0,
                    NominalValue = 0,
                    Tolerance = 0,
                    IsWithinTolerance = false
                });
                result.IsCentered = false;
            }

            return result;
        }

        // Validate shape top
        public async Task<ShapeValidation> ValidateShapeTopAsync(CogToolBlock toolBlock)
        {
            await _recipeParam.EnsureLoadedAsync();

            var recipe = _recipeParam.Config.recipeParamTop;

            var result = new ShapeValidation
            {
                DirectionResults = new Dictionary<string, ShapeResult>()
            };
            MainWindow._produzioneRecord.ATValue = recipe.ShapeValue;
            MainWindow._produzioneRecord.ATToll = recipe.ShapeToll;
            try
            {
                // Validate Top Value
                if (TryGetOutputValue(toolBlock, "A_T_L", out double topValueLeft))
                {
                    var topResultLeft = new ShapeResult
                    {
                        MeasuredValue = topValueLeft,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(topValueLeft) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    MainWindow._produzioneRecord.ATTopLeft = topValueLeft;
                    result.DirectionResults.Add("TopLeft", topResultLeft);
                }

                // Validate Left Value
                if (TryGetOutputValue(toolBlock, "A_T_R", out double topValueRight))
                {
                    var topResultRigth = new ShapeResult
                    {
                        MeasuredValue = topValueRight,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(topValueRight) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    MainWindow._produzioneRecord.ATTopRigth = topValueRight;
                    result.DirectionResults.Add("TopRigth", topResultRigth);
                }

                // Validate Right Value
                if (TryGetOutputValue(toolBlock, "A_B_L", out double BottomValueLeft))
                {
                    var bottomResultLeft = new ShapeResult
                    {
                        MeasuredValue = BottomValueLeft,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(BottomValueLeft) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    result.DirectionResults.Add("Right", bottomResultLeft);
                    MainWindow._produzioneRecord.ATBottomLeft = BottomValueLeft;
                }

                // Validate Bottom Value
                if (TryGetOutputValue(toolBlock, "A_B_R", out double bottomValueRigth))
                {
                    var bottomResultRigth = new ShapeResult
                    {
                        MeasuredValue = bottomValueRigth,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(bottomValueRigth) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    MainWindow._produzioneRecord.ATBottomRigth = bottomValueRigth;
                    result.DirectionResults.Add("Bottom", bottomResultRigth);
                }

                result.IsShapeValid = result.DirectionResults.Count > 0 &&
                                  result.DirectionResults.All(r => r.Value.IsWithinTolerance);
            }
            catch (Exception)
            {
                result.DirectionResults.Clear();
                result.DirectionResults.Add("Error", new ShapeResult
                {
                    MeasuredValue = 0,
                    NominalValue = 0,
                    Tolerance = 0,
                    IsWithinTolerance = false
                });
                result.IsShapeValid = false;
            }

            return result;
        }

        public async Task<ShapeValidation> ValidateShapeSideAsync(CogToolBlock toolBlock)
        {
            await _recipeParam.EnsureLoadedAsync();

            var recipe = _recipeParam.Config.recipeParamTop;

            var result = new ShapeValidation
            {
                DirectionResults = new Dictionary<string, ShapeResult>()
            };
            MainWindow._produzioneRecord.ABValue = recipe.ShapeValue;
            MainWindow._produzioneRecord.ABToll = recipe.ShapeToll;
            try
            {
                // Validate Top Value
                if (TryGetOutputValue(toolBlock, "A_T_L", out double topValueLeft))
                {
                    var topResultLeft = new ShapeResult
                    {
                        MeasuredValue = topValueLeft,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(topValueLeft) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    MainWindow._produzioneRecord.ABTopLeft = topValueLeft;
                    result.DirectionResults.Add("TopLeft", topResultLeft);
                }

                // Validate Left Value
                if (TryGetOutputValue(toolBlock, "A_T_R", out double topValueRight))
                {
                    var topResultRigth = new ShapeResult
                    {
                        MeasuredValue = topValueRight,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(topValueRight) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    MainWindow._produzioneRecord.ABTopRigth = topValueRight;
                    result.DirectionResults.Add("TopRigth", topResultRigth);
                }

                // Validate Right Value
                if (TryGetOutputValue(toolBlock, "A_B_L", out double BottomValueLeft))
                {
                    var bottomResultLeft = new ShapeResult
                    {
                        MeasuredValue = BottomValueLeft,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(BottomValueLeft) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    result.DirectionResults.Add("Right", bottomResultLeft);
                    MainWindow._produzioneRecord.ABBottomLeft = BottomValueLeft;
                }

                // Validate Bottom Value
                if (TryGetOutputValue(toolBlock, "A_B_R", out double bottomValueRigth))
                {
                    var bottomResultRigth = new ShapeResult
                    {
                        MeasuredValue = bottomValueRigth,
                        NominalValue = recipe.ShapeValue,
                        Tolerance = recipe.ShapeToll,
                        IsWithinTolerance = Math.Abs(Math.Abs(bottomValueRigth) - recipe.ShapeValue) <= recipe.ShapeToll
                    };
                    MainWindow._produzioneRecord.ABBottomRigth = bottomValueRigth;
                    result.DirectionResults.Add("Bottom", bottomResultRigth);
                }

                result.IsShapeValid = result.DirectionResults.Count > 0 &&
                                  result.DirectionResults.All(r => r.Value.IsWithinTolerance);
            }
            catch (Exception)
            {
                result.DirectionResults.Clear();
                result.DirectionResults.Add("Error", new ShapeResult
                {
                    MeasuredValue = 0,
                    NominalValue = 0,
                    Tolerance = 0,
                    IsWithinTolerance = false
                });
                result.IsShapeValid = false;
            }

            return result;

        }
        // Update existing GetDetailedTopValidationAsync to use the new method

    }
}
