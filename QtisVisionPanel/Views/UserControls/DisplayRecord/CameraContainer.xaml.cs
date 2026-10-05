using Cognex.VisionPro;
using QtisVisionPanel.ViewModels;
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
    /// Vista contenitore delle telecamere.
    /// Aggiorna il profilo responsive in base allo spazio reale disponibile
    /// cosi' la pagina resta leggibile anche su HMI da 15" con piu' camere attive.
    /// </summary>
    public partial class CameraContainer : UserControl
    {
        private static CameraContainer _instance;
        public static CameraContainer Instance => _instance;

        // True after the first Loaded cycle completes. On subsequent Loaded events
        // (navigation away → back), skip the camera rebuild so that VisionPro
        // display controls and the last inspection images stay intact.
        private bool _hasCompletedInitialLoad = false;

        public CameraContainer()
        {
            InitializeComponent();

            DataContext = new CameraContainerViewModel();
            _instance = this;
        }

        private void OnViewportLoaded(object sender, RoutedEventArgs e)
        {
            if (!_hasCompletedInitialLoad)
            {
                // First load: populate camera views from the live VisionPro job
                // mapping if it is already available (fast-start or late-navigation).
                // If VisionPro hasn't initialized yet, MainWindow.RefreshFromRuntimeConfiguration()
                // is called explicitly after VP init — nothing needed here.
                if (MainWindow.JobMapping != null && MainWindow.JobMapping.Count > 0)
                {
                    if (DataContext is CameraContainerViewModel viewModel)
                        viewModel.RefreshFromRuntimeConfiguration();
                }
                _hasCompletedInitialLoad = true;
            }
            // On navigation returns (_hasCompletedInitialLoad == true): skip rebuild —
            // only re-apply the layout profile so the grid columns stay correct.

            RefreshViewportLayout();
        }

        private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshViewportLayout();
        }

        private void RefreshViewportLayout()
        {
            if (DataContext is CameraContainerViewModel viewModel)
            {
                viewModel.UpdateLayoutProfile(ActualWidth, ActualHeight);
            }
        }

        /// <summary>
        /// Forces the camera container to rebuild its content using the latest
        /// runtime job mapping after a VisionPro init or recipe reload.
        /// </summary>
        public void RefreshFromRuntimeConfiguration()
        {
            if (DataContext is CameraContainerViewModel viewModel)
            {
                viewModel.RefreshFromRuntimeConfiguration();
                viewModel.UpdateLayoutProfile(ActualWidth, ActualHeight);
            }
        }
    }
}

