using System;
using System.Windows;
using System.Windows.Controls;

namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// Selects the camera panel from the semantic display role. Runtime aliases
    /// are resolved before this point, so Left and Right can remain distinct.
    /// </summary>
    public class CameraTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TopCameraTemplate { get; set; }
        public DataTemplate SideCameraTemplate { get; set; }
        public DataTemplate LeftCameraTemplate { get; set; }
        public DataTemplate FrontCameraTemplate { get; set; }
        public DataTemplate RearCameraTemplate { get; set; }
        public DataTemplate RightCameraTemplate { get; set; }
        public DataTemplate BottomCameraTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is CameraViewModel camera)
            {
                switch (camera.CameraType?.ToLower())
                {
                    case "top":
                    case "top3d":
                        return TopCameraTemplate;
                    case "side":
                        return SideCameraTemplate;
                    case "left":
                        return LeftCameraTemplate;
                    case "front":
                        return FrontCameraTemplate;
                    case "rear":
                        return RearCameraTemplate;
                    case "right":
                        return RightCameraTemplate;
                    case "bottom":
                        return BottomCameraTemplate;
                    default:
                        return base.SelectTemplate(item, container);
                }
            }
            return base.SelectTemplate(item, container);
        }
    }
}
