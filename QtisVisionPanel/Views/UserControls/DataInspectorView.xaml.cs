using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QtisVisionPanel.Inspector.Services;
using QtisVisionPanel.Inspector.ViewModels;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;

namespace QtisVisionPanel.Views.UserControls
{
    /// <summary>
    /// WPF host for the native in-HMI DataInspector experience.
    ///
    /// The control owns its own inspector view model on purpose, instead of
    /// inheriting the parent shell DataContext. This avoids binding collisions
    /// with <see cref="ViewModels.MainViewModel"/> and keeps the inspector slice
    /// self-contained.
    /// </summary>
    public partial class DataInspectorView : UserControl, INotifyPropertyChanged
    {
        private DataInspectorViewModel _inspectorVm;

        /// <summary>
        /// Builds the user control. Runtime initialization is deferred to Loaded
        /// so the designer remains stable.
        /// </summary>
        public DataInspectorView()
        {
            InitializeComponent();

            if (DesignModeHelper.IsInDesignMode)
                return;
        }

        /// <summary>
        /// Native inspector view model bound by the XAML.
        /// </summary>
        public DataInspectorViewModel InspectorVm
        {
            get { return _inspectorVm; }
            private set
            {
                if (ReferenceEquals(_inspectorVm, value))
                    return;

                _inspectorVm = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Late initialization entry point used once the control is actually hosted.
        /// </summary>
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DesignModeHelper.IsInDesignMode)
                return;

            if (InspectorVm != null)
                return;

            InitializeNativeInspector();
        }

        /// <summary>
        /// Creates the native inspector profile, repository and view model
        /// directly from the runtime configuration already loaded in the HMI.
        /// </summary>
        private void InitializeNativeInspector()
        {
            try
            {
                var appConfig = MainWindow.configManager != null ? MainWindow.configManager.Config : null;
                var profileFactory = new InspectorProfileFactory();
                var profile = profileFactory.CreateFromMainConfiguration(appConfig);

                var repository = new MySqlPieceHistoryRepository(profile);
                var evidenceResolver = new LocalPieceEvidenceResolver();

                InspectorVm = new DataInspectorViewModel(
                    repository,
                    repository,
                    evidenceResolver,
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataInspectorNativeMode", "Native inspector read-side"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataInspectorNativeNote", "Native inspector mode active. Review recent rejected pieces directly inside the HMI."));
            }
            catch (System.Exception ex)
            {
                new SystemNotificationWindow(
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataInspectorInitErrorTitle", "Inspector initialization"),
                    ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DataInspectorInitErrorMessage", "Unable to initialize the native inspector view:") + " " + ex.Message,
                    NotificationSeverity.Warning).ShowDialog();
            }
        }

        private void ZoomScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (InspectorVm == null) return;
            double delta = e.Delta > 0 ? 0.15 : -0.15;
            InspectorVm.ZoomLevel = Math.Max(0.25, Math.Min(8.0, InspectorVm.ZoomLevel + delta));
            e.Handled = true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
