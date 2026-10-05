using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database.ProductionRecord
{
    public class ProduzioneRecord
    {
        public long IdProduzione { get; set; }
        public string DataEOra { get; set; }
        public string Operatore { get; set; }
        public string Prodotto { get; set; }
        public string Ricetta { get; set; }
        public int? EsitoClassificazione { get; set; }
        public string TopClassificationLabel { get; set; }
        public double? TopClassificationScore { get; set; }
        public string SideClassificationLabel { get; set; }
        public double? SideClassificationScore { get; set; }
        public string FrontClassificationLabel { get; set; }
        public double? FrontClassificationScore { get; set; }
        public string RearClassificationLabel { get; set; }
        public double? RearClassificationScore { get; set; }
        public string RightClassificationLabel { get; set; }
        public double? RightClassificationScore { get; set; }
        public string BottomClassificationLabel { get; set; }
        public double? BottomClassificationScore { get; set; }
        public int? NCTopAiClassification { get; set; }
        public int? NCSideAiClassification { get; set; }
        public int? NCFrontAiClassification { get; set; }
        public int? NCRearAiClassification { get; set; }
        public int? NCRightAiClassification { get; set; }
        public int? NCBottomAiClassification { get; set; }
        public double? MinValueLogoToll { get; set; }
        public double? LogoMatchPerc { get; set; }
        public int? NcLoghiImmagini { get; set; }
        public double? TopValueToMarker { get; set; }
        public double? LeftValueToMarker { get; set; }
        public double? RigthValueToMarker { get; set; }
        public double? BottomValueToMarker { get; set; }
        public int? NcCentraturaLogo { get; set; }
        public double? Area1FlapsDetected { get; set; }
        public double? Area2FlapsDetected { get; set; }
        public int? NcAletteAperte { get; set; }
        public double? HeigthNominalValue { get; set; }
        public double? HeigthMeasureValue { get; set; }
        public double? HeigthMinimum { get; set; }
        public double? HeigthMaximum { get; set; }
        public int? NcHeigth { get; set; }
        public double? MinAreaSealing { get; set; }
        public double? AreaSealing { get; set; }
        public int? NcSaldaturaLaterale { get; set; }
        public double? ATValue { get; set; }
        public double? ATToll { get; set; }
        public double? ATTopLeft { get; set; }
        public double? ATTopRigth { get; set; }
        public double? ATBottomRigth { get; set; }
        public double? ATBottomLeft { get; set; }
        public int? NcShapeTop { get; set; }
        public double? ABValue { get; set; }
        public double? ABToll { get; set; }
        public double? ABTopLeft { get; set; }
        public double? ABTopRigth { get; set; }
        public double? ABBottomRigth { get; set; }
        public double? ABBottomLeft { get; set; }
        public int? NcShapeBottom { get; set; }
        public int? EspulsioneComandata { get; set; }
        public string DataHostnames { get; set; }
        public string PieceData { get; set; }
        public string InspectionStatusDetails { get; set; }
        public int? NCSurfaceCheck { get; set; }
        public int? TraceabilityDetected { get; set; }
        public string TraceabilityCode { get; set; }
        public string TraceabilityExpectedPrefix { get; set; }
        public int? NCTraceability { get; set; }
        public double? ThreeDHeightNominalValue { get; set; }
        public double? ThreeDHeightMeasureValue { get; set; }
        public double? ThreeDHeightMinimum { get; set; }
        public double? ThreeDHeightMaximum { get; set; }
        public int? NCThreeDHeight { get; set; }
        public double? ThreeDHeightMedianValue { get; set; }
        public double? ThreeDHeightHighTailValue { get; set; }
        public double? ThreeDHeightMaximumValue { get; set; }
        public double? ThreeDHeightBulgeValue { get; set; }
        public double? ThreeDHeightValidPixelRatio { get; set; }
        public int? NCThreeDProfile { get; set; }
        public double? ThreeDWidthNominalValue { get; set; }
        public double? ThreeDWidthMeasureValue { get; set; }
        public double? ThreeDWidthMinimum { get; set; }
        public double? ThreeDWidthMaximum { get; set; }
        public int? NCThreeDWidth { get; set; }
        public double? ThreeDLengthNominalValue { get; set; }
        public double? ThreeDLengthMeasureValue { get; set; }
        public double? ThreeDLengthMinimum { get; set; }
        public double? ThreeDLengthMaximum { get; set; }
        public int? NCThreeDLength { get; set; }
        public int? NCBottomSealing { get; set; }
        public int? NCTrappedPaper { get; set; }

        public ProduzioneRecord Clone()
        {
            return (ProduzioneRecord)MemberwiseClone();
        }
    }
}
