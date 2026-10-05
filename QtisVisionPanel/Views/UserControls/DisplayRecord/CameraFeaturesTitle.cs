using System.Globalization;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    /// <summary>
    /// Titolo "Funzioni camera {nome}" dei pannelli feature, con il nome del job VPP
    /// (Side, Rear, Left, ...) invece del nome fisso della vista.
    /// </summary>
    internal static class CameraFeaturesTitle
    {
        public static string Format(string viewLabel, string fallbackKey, string fallbackText)
        {
            if (string.IsNullOrWhiteSpace(viewLabel))
            {
                return ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(fallbackKey, fallbackText);
            }

            string template = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault(
                "lbCameraFeaturesFormat",
                "{0} Camera Features");
            if (!template.Contains("{0}"))
            {
                template = "{0} Camera Features";
            }

            string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(viewLabel.Trim().ToLowerInvariant());
            return string.Format(CultureInfo.InvariantCulture, template, name);
        }
    }
}
