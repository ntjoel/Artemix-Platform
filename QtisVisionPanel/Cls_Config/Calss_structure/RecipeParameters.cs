using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace QtisVisionPanel.Cls_Config.Calss_structure
{
    public class RecipeParameters
    {
        public class RecipeParamTop
        {
            public double LogoToll { get; set; }
            public double Print_centering_Toll { get; set; }
            public double MinAreaOpenFlas { get; set; }
            public double TopValueToLogo { get; set; }
            public double LeftValueToLogo { get; set; }
            public double RigthValueToLogo { get; set; }
            public double BottomValueToLogo { get; set; }
            public double ShapeValue { get; set; }
            public double ShapeToll { get; set; }

        }
        public class General_Info
        {
            public string RecipeName { get; set; }= string.Empty;
            public string RecipeDescription { get; set; }= string.Empty;
            public double Product_Length { get; set; } = 0;
            public double Product_Width { get; set; } = 0;
            public string Product_Image { get; set; } = string.Empty;
            // Proprietà per tutti i tipi di prodotto
            public double Product_Height { get; set; } = 0;
            public double Product_Depth { get; set; } = 0;

            // Proprietà specifiche per rotoli
            public double Roll_Diameter { get; set; } = 0;
            public double Core_Diameter { get; set; } = 0;
            public string Roll_Axis { get; set; } = "Orizzontale";

        }
        public class CameraSetting
        {
            public double TopCameraTriggerDelay { get; set; } = 0;
            public double SideCameraTriggerDelay { get; set; } = 0;
            public double LeftCameraTriggerDelay { get; set; } = 0;
            public double FrontCameraTriggerDelay { get; set; } = 0;
            public double RightCameraTriggerDelay { get; set; } = 0;
            public double RearCameraTriggerDelay { get; set; } = 0;
            public double BottomCameraTriggerDelay { get; set; } = 0;

            public double ResolveLeftCameraTriggerDelay() =>
                Math.Abs(LeftCameraTriggerDelay) > 0.0000001
                    ? LeftCameraTriggerDelay
                    : SideCameraTriggerDelay;
        }

        /// <summary>
        /// Product-specific corrections applied on top of the machine intervention
        /// positions. The physical base positions remain in machine_runtime_config.xml.
        /// Missing values deserialize as zero, preserving every existing recipe.
        /// </summary>
        public class RecipeCameraPositionAdjustments
        {
            public double TopOffsetMm { get; set; } = 0;
            public double SideOffsetMm { get; set; } = 0;
            public double LeftOffsetMm { get; set; } = 0;
            public double FrontOffsetMm { get; set; } = 0;
            public double RightOffsetMm { get; set; } = 0;
            public double RearOffsetMm { get; set; } = 0;
            public double BottomOffsetMm { get; set; } = 0;
        }

        /// <summary>
        /// Recipe deltas for one machine-level MultiShot profile.
        /// EnabledMode accepts Machine, Enabled or Disabled. Hardware output,
        /// pulse timing, direction, calibration and safety limits remain global.
        /// </summary>
        public class RecipeMultiShotAdjustment
        {
            public string EnabledMode { get; set; } = "Machine";
            public int ShotCountOffset { get; set; } = 0;
            public double StepOffsetMm { get; set; } = 0;
            public double FirstShotOffsetMm { get; set; } = 0;
        }

        public class RecipeMultiShotAdjustments
        {
            public RecipeMultiShotAdjustment SideLeft { get; set; } = new RecipeMultiShotAdjustment();
            public RecipeMultiShotAdjustment RightRear { get; set; } = new RecipeMultiShotAdjustment();
            public RecipeMultiShotAdjustment Rear { get; set; } = new RecipeMultiShotAdjustment();
            public RecipeMultiShotAdjustment Bottom { get; set; } = new RecipeMultiShotAdjustment();
        }

        /// <summary>
        /// Per-recipe enable override for camera intervention points. Values accept
        /// Machine, Enabled or Disabled; physical I/O mapping remains machine-level.
        /// </summary>
        public class RecipeCameraTriggerAdjustments
        {
            public string Top { get; set; } = "Machine";
            public string Side { get; set; } = "Machine";
            public string Left { get; set; } = "Machine";
            public string Front { get; set; } = "Machine";
            public string Right { get; set; } = "Machine";
            public string Rear { get; set; } = "Machine";
            public string Bottom { get; set; } = "Machine";
        }

        /// <summary>
        /// Per-recipe assignment of one inspection to one semantic camera role.
        /// The physical camera and I/O mapping remain machine-level data.
        /// </summary>
        public class RecipeInspectionFeatureAssignment
        {
            [XmlAttribute]
            public string Feature { get; set; } = string.Empty;

            [XmlAttribute]
            public bool Enabled { get; set; }
        }

        /// <summary>
        /// Inspections and accepted Classify labels for one camera view.
        /// Camera acquisition enablement continues to use CameraTriggers so the
        /// recipe cannot contain two conflicting camera-enable switches.
        /// </summary>
        public class RecipeCameraInspectionView
        {
            [XmlAttribute]
            public string CameraRole { get; set; } = string.Empty;

            public string AcceptedClassificationClasses { get; set; } = string.Empty;

            /// <summary>
            /// Punteggio minimo di confidenza perche' una classe accettata valga come OK su
            /// questa vista. Stessa scala dell'output Score del job (tipicamente 0..1).
            ///
            /// 0 = soglia disattivata: vale solo la tassonomia, come prima. Il valore e' per
            /// vista perche' ogni modello Edge Learning e' addestrato per camera e la
            /// separazione tra le classi non e' la stessa su tutte.
            /// </summary>
            public double MinimumClassificationScore { get; set; }

            [XmlArray("Inspections")]
            [XmlArrayItem("Inspection")]
            public List<RecipeInspectionFeatureAssignment> Inspections { get; set; } =
                new List<RecipeInspectionFeatureAssignment>();
        }

        /// <summary>
        /// Product-specific camera-view inspection profile. IsConfigured is false
        /// for legacy XML files, which preserves the machine-level fallback until
        /// an operator explicitly saves the matrix for that recipe.
        /// </summary>
        public class RecipeInspectionViewConfiguration
        {
            [XmlAttribute]
            public bool IsConfigured { get; set; }

            [XmlArray("Views")]
            [XmlArrayItem("View")]
            public List<RecipeCameraInspectionView> Views { get; set; } =
                new List<RecipeCameraInspectionView>();
        }

        /// <summary>
        /// Recipe-only machine runtime corrections. This object never contains
        /// physical board/channel mapping or machine calibration data.
        /// </summary>
        public class RecipeMachineRuntimeAdjustments
        {
            public RecipeCameraTriggerAdjustments CameraTriggers { get; set; } = new RecipeCameraTriggerAdjustments();
            public RecipeCameraPositionAdjustments CameraPositions { get; set; } = new RecipeCameraPositionAdjustments();
            public RecipeMultiShotAdjustments MultiShot { get; set; } = new RecipeMultiShotAdjustments();
        }

        public class RecipeParamSide
        {
            public double Min_Heigth_value { get; set; }

            /// <summary>
            /// Soglia di area sigillatura della vista SIDE. Storicamente questo unico valore
            /// veniva usato ANCHE dalla validazione REAR, quindi due camere con ottica,
            /// distanza e inquadratura diverse condividevano la stessa soglia in pixel/mm2.
            /// Resta il valore di riferimento e il fallback per le viste non configurate.
            /// </summary>
            public double MinAreaSealing { get; set; }

            /// <summary>
            /// Soglia di area sigillatura specifica della vista REAR / RIGHT.
            /// Zero significa "non configurata": in quel caso si eredita
            /// <see cref="MinAreaSealing"/>, cosi' le ricette gia' in campo mantengono
            /// esattamente il comportamento precedente. Stesso sentinella gia' usato per
            /// Top3DDetectionSensitivity.
            /// </summary>
            public double MinAreaSealingRear { get; set; }
            public double MinAreaSealingRight { get; set; }

            /// <summary>
            /// Soglia di area sigillatura specifica della vista LEFT. Zero = eredita
            /// <see cref="MinAreaSealing"/>.
            /// </summary>
            public double MinAreaSealingLeft { get; set; }

            public double Heigth_toll { get; set; }

            /// <summary>
            /// Soglia effettiva per una vista, applicando l'ereditarieta'. Unico punto in cui
            /// si decide quale valore vale per quale camera: i validatori chiamano questo e
            /// non leggono piu' direttamente lo scalare condiviso.
            /// </summary>
            public double ResolveMinAreaSealing(string cameraRole)
            {
                string role = (cameraRole ?? string.Empty).Trim().ToLowerInvariant();
                switch (role)
                {
                    case "rear":
                        return MinAreaSealingRear > 0 ? MinAreaSealingRear : MinAreaSealing;
                    case "right":
                        return MinAreaSealingRight > 0 ? MinAreaSealingRight
                            : MinAreaSealingRear > 0 ? MinAreaSealingRear : MinAreaSealing;
                    case "left":
                        return MinAreaSealingLeft > 0 ? MinAreaSealingLeft : MinAreaSealing;
                    default:
                        return MinAreaSealing;
                }
            }
        }
        public class DimensionThresholds
        {
            public double LengthValue { get; set; } = 0;
            public double LengthTolerance { get; set; } = 0;
            public double WidthValue { get; set; } = 0;
            public double WidthTolerance { get; set; } = 0;
            public double DiameterValue { get; set; } = 0;
            public double DiameterTolerance { get; set; } = 0;
        }

        public class InspectionThresholdItem
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Tolerance { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty; // Aggiunto
            public bool IsEnabled { get; set; } = true;
            // Nuove proprietà per il calcolo delle ispezioni
            public double NominalValue { get; set; } // Valore nominale (per Shape, Height, Print Centering)
            public double ToleranceValue { get; set; } // Valore di tolleranza ±
            public double ThresholdValue { get; set; } // Valore di soglia minima ≥
        }

        public class InspectionThresholds
        {
            public List<InspectionThresholdItem> TopInspections { get; set; } = new List<InspectionThresholdItem>();
            public List<InspectionThresholdItem> SideInspections { get; set; } = new List<InspectionThresholdItem>();
            public List<InspectionThresholdItem> FrontInspections { get; set; } = new List<InspectionThresholdItem>();
        }
        public class RecipeParamFront
        {
            public bool RequireTraceability { get; set; } = true;
            public string ExpectedCodePrefix { get; set; } = string.Empty;
          
        }
        public class RecipeParamTop3D
        {
            public double ThreeDHeightNominalValue { get; set; } = 0;
            public double ThreeDHeightTolerance { get; set; } = 0;
            public double ThreeDWidthNominalValue { get; set; } = 0;
            public double ThreeDWidthTolerance { get; set; } = 0;
            public double ThreeDLengthNominalValue { get; set; } = 0;
            public double ThreeDLengthTolerance { get; set; } = 0;
            public string Top3DPrimaryLastRunView { get; set; } = string.Empty;
            public string Top3DSecondaryLastRunView { get; set; } = string.Empty;

            // Product-specific profile monitoring. Missing fields in legacy recipes
            // deserialize to disabled/default values and preserve the previous cycle.
            public bool ThreeDProfileEnabled { get; set; } = false;
            public double ThreeDHeightMinimumValidPixelRatio { get; set; } = 0.75;
            public double ThreeDHeightMaximumBulge { get; set; } = 0;

            // Sensibilita' di rilevamento profilo del sensore 3D (es. testa L38), tipicamente 0..1.
            // Valore <= 0 => "non configurata": non viene applicata alla testa e il comportamento
            // resta identico alle ricette esistenti (retrocompatibile: campo assente su vecchi XML = 0).
            public double Top3DDetectionSensitivity { get; set; } = 0;
        }
        public class EjectionStatus
        {
            public bool logo { get; set; }
            public bool Print_centering { get; set; }
            public bool OpenFlaps { get; set; }
            public bool SurfaceCheck { get; set; }
            public bool Height { get; set; }
            public bool Side_sealing { get; set; }
            public bool ShapeTop { get; set; }
            public bool ShapeSide { get; set; }
            public bool FrontTraceability { get; set; }
            public bool ThreeDHeight { get; set; }
            public bool ThreeDWidth { get; set; }
            public bool ThreeDLength { get; set; }
            public bool BottomSealing { get; set; } = true;
            public bool TrappedPaper { get; set; } = true;
            public bool AIClassification { get; set; } = false;
        }
        public class InspectionStatus
        {
            public bool logo { get; set; }
            public bool Print_centering { get; set; }
            public bool OpenFlaps { get; set; }
            public bool SurfaceCheck { get; set; }
            public bool Height { get; set; }
            public bool Side_sealing { get; set; }
            public bool ShapeTop { get; set; }
            public bool ShapeSide { get; set; }
            public bool FrontTraceability { get; set; }
            public bool ThreeDHeight { get; set; }
            public bool ThreeDWidth { get; set; }
            public bool ThreeDLength { get; set; }
            public bool BottomSealing { get; set; } = true;
            public bool TrappedPaper { get; set; } = true;
            public bool AIClassification { get; set; } = false;

            public static InspectionStatus FromEjectionStatus(EjectionStatus source)
            {
                if (source == null)
                {
                    return new InspectionStatus();
                }

                return new InspectionStatus
                {
                    logo = source.logo,
                    Print_centering = source.Print_centering,
                    OpenFlaps = source.OpenFlaps,
                    SurfaceCheck = source.SurfaceCheck,
                    Height = source.Height,
                    Side_sealing = source.Side_sealing,
                    ShapeTop = source.ShapeTop,
                    ShapeSide = source.ShapeSide,
                    FrontTraceability = source.FrontTraceability,
                    ThreeDHeight = source.ThreeDHeight,
                    ThreeDWidth = source.ThreeDWidth,
                    ThreeDLength = source.ThreeDLength,
                    BottomSealing = source.BottomSealing,
                    TrappedPaper = source.TrappedPaper,
                    AIClassification = source.AIClassification
                };
            }
        }
        public class Counter
        {
            public double Total { get; set; }
            public double Compliant { get; set; }
            public double Not_Compliant { get; set; }
            public double logo { get; set; }
            public double Print_centering { get; set; }
            public double OpenFlaps { get; set; }
            public double SurfaceCheck { get; set; }
            public double Height { get; set; }
            public double Side_sealing { get; set; }
            public double ShapeTop { get; set; }
            public double ShapeSide { get; set; }
            public double FrontTraceability { get; set; }
            public double ThreeDHeight { get; set; }
            public double ThreeDWidth { get; set; }
            public double ThreeDLength { get; set; }
            public double BottomSealing { get; set; }
            public double TrappedPaper { get; set; }
            public double AIClassification { get; set; }


        }
        [Serializable]

        [XmlRoot("Recipedata")]
        public class RecipeData
        {
            public RecipeParamTop recipeParamTop { get; set; } = new RecipeParamTop();
            public RecipeParamSide recipeParamSide { get; set; } = new RecipeParamSide();
            public RecipeParamFront recipeParamFront { get; set; } = new RecipeParamFront();
            public RecipeParamTop3D recipeParamTop3D { get; set; } = new RecipeParamTop3D();
            public Counter Counter { get; set; } = new Counter();

            [XmlArray("topInspectionTool")]
            [XmlArrayItem("item")]
            public List<string> topInspectionTool { get; } = new List<string>();
            [XmlArray("sideInspectionTool")]
            [XmlArrayItem("item")]
            public List<string> sideInspectionTool { get; } = new List<string>();
            public General_Info general_Info { get; set; } = new General_Info();
            public InspectionStatus inspectionStatus { get; set; } = new InspectionStatus();
            public EjectionStatus ejectionStatus { get; set; } = new EjectionStatus();
            public CameraSetting cameraSetting { get; set; } = new CameraSetting();
            public RecipeMachineRuntimeAdjustments machineRuntimeAdjustments { get; set; } = new RecipeMachineRuntimeAdjustments();
            public RecipeInspectionViewConfiguration inspectionViewConfiguration { get; set; } =
                new RecipeInspectionViewConfiguration();
            // Aggiungi queste proprietà per le soglie
            public DimensionThresholds DimensionThresholds { get; set; } = new DimensionThresholds();
            public InspectionThresholds InspectionThresholds { get; set; } = new InspectionThresholds();
        }
    }
}
