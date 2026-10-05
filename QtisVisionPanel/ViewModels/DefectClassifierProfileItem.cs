using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace QtisVisionPanel.ViewModels
{
    /// <summary>
    /// Una camera aggiuntiva (REAR/RIGHT, FRONT, BOTTOM) nel pannello Controlli AI: il profilo
    /// ONNX editabile, i testi con il nome della camera come nel VPP e le statistiche shadow.
    /// </summary>
    public sealed class DefectClassifierProfileItem : INotifyPropertyChanged
    {
        private ShadowModeStats _stats;
        private bool _isExpanded;

        public DefectClassifierProfileItem(DefectClassifierCameraProfile profile)
        {
            Profile = profile;
            string role = profile?.CameraRole ?? string.Empty;
            CameraLabel = CameraConfigurationHelper.GetCameraRoleDisplayLabel(role);
            ImageTagsText = string.Join(" / ", OnnxDefectClassifier.GetImageTags(role));
            IsCameraInVpp = MainWindow.JobRoleMapping != null &&
                            CameraConfigurationHelper.HasCameraRole(MainWindow.JobRoleMapping, role);
            // Aperta se la camera e' nel VPP attivo o se ha gia' un modello abilitato:
            // le camere che la macchina non usa restano chiuse ma configurabili.
            _isExpanded = IsCameraInVpp || profile?.Enabled == true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public DefectClassifierCameraProfile Profile { get; }

        public string CameraRole => Profile?.CameraRole ?? string.Empty;

        /// <summary>Nome della camera come nel VPP (REAR, RIGHT, FRONT, BOTTOM).</summary>
        public string CameraLabel { get; }

        public string ImageTagsText { get; }

        public bool IsCameraInVpp { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public string Title => Format(
            "Sub_entry_AiCameraClassifierTitle",
            "{0} classifier (image {1})",
            CameraLabel,
            ImageTagsText);

        public string PresenceText => IsCameraInVpp
            ? Format("Sub_entry_AiCameraInVpp", "Camera present in the active VPP.")
            : Format("Sub_entry_AiCameraNotInVpp", "Camera not present in the active VPP: the model can be prepared, it runs only when the camera is in use.");

        public string ModelPathLabel => Format("Sub_entry_AiCameraModelPath", "{0} model path (.onnx)", CameraLabel);

        public string TrainButtonText => Format("Sub_entry_AiTrainCameraModel", "Train {0} model", CameraLabel);

        public string TrainHint => Format(
            "Sub_entry_AiTrainCameraModelHint",
            "Creates defect_classifier_{0}_vN.onnx from image {1} and labels.{0}. Stop production first, then verify the model and press Save to enable shadow-mode.",
            CameraRole,
            ImageTagsText);

        public string ShadowCameraTitle => Format(
            "Sub_entry_AiCameraShadowTitle",
            "{0} camera (image {1})",
            CameraLabel,
            ImageTagsText);

        public ShadowModeStats Stats
        {
            get => _stats;
            set
            {
                _stats = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStats));
            }
        }

        public bool HasStats => _stats?.HasData == true;

        private static string Format(string key, string fallback, params object[] args)
        {
            string template = ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, args);
            }
            catch (System.FormatException)
            {
                // Traduzione con segnaposto errati: si usa il testo di default, sempre valido.
                return string.Format(CultureInfo.CurrentCulture, fallback, args);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Etichette localizzate comuni dei campi ONNX, condivise dalle card per camera.</summary>
    public sealed class OnnxFieldLabels
    {
        public string Enabled { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Enabled", "Enabled");
        public string ModelVersion { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxModelVersion", "Model version");
        public string ModelNotes { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxModelNotes", "Model notes");
        public string InputWidth { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxInputWidth", "Resize width");
        public string InputHeight { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxInputHeight", "Resize height");
        public string NormalizeMean { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxNormalizeMean", "Norm. mean");
        public string NormalizeStd { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxNormalizeStd", "Norm. std.dev.");
        public string Grayscale { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiOnnxGrayscale", "Grayscale");
        public string MinConfidence { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiMinNokConfidence", "Minimum NOK confidence (0-1, 0=off)");
        public string Agreement { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowAgreement", "Agreement");
        public string NokRecall { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowNokRecall", "Defect recall (NOK)");
        public string NokPrecision { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowNokPrecision", "NOK precision");
        public string FalseAlarms { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowFalseAlarms", "False alarms");
        public string MeanConfidence { get; } = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_AiShadowMeanConfidence", "Mean confidence");
    }
}
