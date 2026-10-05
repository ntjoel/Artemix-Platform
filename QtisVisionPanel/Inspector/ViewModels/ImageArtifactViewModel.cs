using QtisVisionPanel.Inspector.Models;
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace QtisVisionPanel.Inspector.ViewModels
{
    public sealed class ImageArtifactViewModel
    {
        public ImageArtifactViewModel(PieceImageArtifact artifact)
        {
            Artifact = artifact;
            Label = string.IsNullOrWhiteSpace(artifact.ViewCode)
                ? artifact.FileName
                : artifact.ViewCode + " - " + artifact.Variant;
            SecondaryLabel = artifact.FileName;

            if (artifact.Exists && File.Exists(artifact.FullPath))
            {
                try
                {
                    Image = new BitmapImage();
                    Image.BeginInit();
                    Image.CacheOption = BitmapCacheOption.OnLoad;
                    Image.UriSource = new Uri(artifact.FullPath, UriKind.Absolute);
                    Image.EndInit();
                    Image.Freeze();
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn(ex, "DATAINSPECTOR_IMAGE_PREVIEW_FAILED|path={0}", artifact.FullPath);
                    Image = null;
                }
            }
        }

        public PieceImageArtifact Artifact { get; private set; }
        public string Label { get; private set; }
        public string SecondaryLabel { get; private set; }
        public BitmapImage Image { get; private set; }
        public bool HasPreview { get { return Image != null; } }
    }
}
