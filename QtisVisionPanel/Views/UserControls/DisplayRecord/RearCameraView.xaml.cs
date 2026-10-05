using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QtisVisionPanel.Views.UserControls.DisplayRecord
{
    /// <summary>
    /// Logica di interazione per ImageView.xaml
    /// </summary>
    public partial class RearCameraView : UserControl
    {
        private static RearCameraView _instance;
        public static RearCameraView Instance => _instance;
        public Cognex.VisionPro.CogRecordDisplay ImageDisplay => CogRecordsDisplay1;

        private Dictionary<string, FrameworkElement> _featureContainers;

        public RearCameraView()
        {
            InitializeComponent();
            _instance = this;
            CogDisplayStatusBarV21.Display = CogRecordsDisplay1;
            lbRearCamera.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.lbRearCamera;

            // Le tre icone erano statiche: restavano visibili anche quando la vista Rear non
            // eseguiva quei controlli (es. con la sola AI Classification abilitata). Le altre
            // viste avevano gia' UpdateFeatureVisibility; la Rear era l'unica a non averlo,
            // e per questo CameraDisplayManager non la aggiornava.
            _featureContainers = new Dictionary<string, FrameworkElement>
            {
                { "OpenFlaps",   OpenFlapsSide },
                { "SideSealing", OpenSealingSide },
                { "ShapeSide",   ShapeSide }
            };
        }

        /// <summary>
        /// Mostra solo le icone delle ispezioni che questa vista verifica davvero.
        /// Una feature assente dal dizionario viene nascosta.
        /// </summary>
        public void UpdateFeatureVisibility(Dictionary<string, bool> visibilityByFeature)
        {
            if (visibilityByFeature == null || _featureContainers == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var featureContainer in _featureContainers)
                {
                    bool isVisible = visibilityByFeature.TryGetValue(featureContainer.Key, out bool enabled) && enabled;
                    featureContainer.Value.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void dispose()
        {
            try { CogDisplayStatusBarV21.Display = null; } catch { }
            try
            {
                if (CogRecordsDisplay1.LiveDisplayRunning)
                {
                    CogRecordsDisplay1.StopLiveDisplay();
                }
            }
            catch (Exception ex) { MainWindow.logger?.Warn(ex, "REAR_LIVE_DISPLAY_STOP_FAILED — non-critical"); }
            try { CogRecordsDisplay1.StaticGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.InteractiveGraphics?.Clear(); } catch { }
            try { CogRecordsDisplay1.Image = null; } catch { }
            try { CogRecordsDisplay1.Record = null; } catch { }
            _instance = null;
        }
    }
}
