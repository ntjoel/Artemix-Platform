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

namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Logica di interazione per PreferenceView.xaml
    /// </summary>
    public partial class PreferenceView : UserControl
    {
        public PreferenceViewModel _viewmodel;
        public PreferenceView()
        {
            InitializeComponent();
            _viewmodel = new PreferenceViewModel();
            DataContext = _viewmodel;
            _viewmodel.PropertyChanged += ViewmodelOnPropertyChanged;
            Loaded += PreferenceView_Loaded;
            Unloaded += PreferenceView_Unloaded;
            inizializelanguage();
        }

        public void inizializelanguage()
        {
           Sub_entry_PreferenceView.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_PreferenceView;
            Sub_entry_PreferenceSubtitle.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_PreferenceSubtitle;
            Sub_entry_Language.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Language;
            Sub_entry_LanguageCardHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_LanguageCardHint;
            Sub_entry_CurrentLanguage.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_CurrentLanguage;
            Sub_entry_LanguagePreviewHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_LanguagePreviewHint;
            Sub_entry_ConfigPaths.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigPaths;
            Sub_entry_FlagImagesPath.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_FlagImagesPath;
            Sub_entry_LanguageFilesPath.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_LanguageFilesPath;
            Sub_entry_TestLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_TestLabel;
            sub_entry_saveLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SaveLabel;
            Sub_entry_ConfigEditorTitle.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigEditorTitle;
            Sub_entry_ConfigEditorCardHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigEditorCardHint;
            Sub_entry_ConfigFilePathLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigFilePathLabel;
            Sub_entry_ConfigFilePathHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigFilePathHint;
            Sub_entry_ConfigSectionPaths.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionPaths;
            Sub_entry_ConfigSectionPathsHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionPathsHint;
            Sub_entry_ConfigMachineTypeLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigMachineTypeLabel;
            Sub_entry_ConfigRecipeFolderLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigRecipeFolderLabel;
            Sub_entry_ConfigImageDirLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigImageDirLabel;
            Sub_entry_ConfigBackupRecipeLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigBackupRecipeLabel;
            Sub_entry_ConfigDashboardLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigDashboardLabel;
            Sub_entry_ConfigSectionCameras.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionCameras;
            Sub_entry_ConfigSectionCamerasHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionCamerasHint;
            Sub_entry_ConfigTopCameraSerialLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTopCameraSerialLabel;
            Sub_entry_ConfigSideCameraSerialLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSideCameraSerialLabel;
            Sub_entry_ConfigFrontCameraSerialLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigFrontCameraSerialLabel;
            Sub_entry_ConfigRearCameraSerialLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigRearCameraSerialLabel;
            Sub_entry_ConfigBottomCameraSerialLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigBottomCameraSerialLabel;
            Sub_entry_ConfigTop3DMeasurementOffsets.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTop3DMeasurementOffsets;
            Sub_entry_ConfigTop3DMeasurementOffsetsHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTop3DMeasurementOffsetsHint;
            Sub_entry_ConfigTop3DHeightOffsetLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTop3DHeightOffsetLabel;
            Sub_entry_ConfigTop3DWidthOffsetLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTop3DWidthOffsetLabel;
            Sub_entry_ConfigTop3DLengthOffsetLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTop3DLengthOffsetLabel;
            Sub_entry_ConfigTop3DMeasurementOffsetsFormula.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTop3DMeasurementOffsetsFormula;
            Sub_entry_ConfigTemperatureProviderLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTemperatureProviderLabel;
            Sub_entry_ConfigTemperaturePollingLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigTemperaturePollingLabel;
            Sub_entry_ConfigCpuTemperatureWarningLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigCpuTemperatureWarningLabel;
            Sub_entry_ConfigCpuTemperatureCriticalLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigCpuTemperatureCriticalLabel;
            Sub_entry_ConfigMotherboardTemperatureWarningLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigMotherboardTemperatureWarningLabel;
            Sub_entry_ConfigMotherboardTemperatureCriticalLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigMotherboardTemperatureCriticalLabel;
            Sub_entry_ConfigSectionApps.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionApps;
            Sub_entry_ConfigSectionAppsHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionAppsHint;
            Sub_entry_ConfigAutomationLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigAutomationLabel;
            Sub_entry_ConfigToolsLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigToolsLabel;
            Sub_entry_ConfigReportsLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigReportsLabel;
            Sub_entry_ConfigLogShowImageLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigLogShowImageLabel;
            Sub_entry_ConfigSectionAutomation.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionAutomation;
            Sub_entry_ConfigSectionAutomationHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionAutomationHint;
            Sub_entry_ConfigBlockingAlarmOutputLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigBlockingAlarmOutputLabel;
            Sub_entry_ConfigNonBlockingAlarmOutputLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigNonBlockingAlarmOutputLabel;
            Sub_entry_ConfigAutoResumeLabel.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigAutoResumeLabel;
            Sub_entry_ConfigAutoResumeSecondsLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigAutoResumeSecondsLabel;
            Sub_entry_ConfigRetryCooldownLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigRetryCooldownLabel;
            Sub_entry_ConfigAutoLogoutLabel.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigAutoLogoutLabel;
            Sub_entry_ConfigAutoLogoutMinutesLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigAutoLogoutMinutesLabel;
            Sub_entry_ConfigSectionDatabase.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionDatabase;
            Sub_entry_ConfigSectionDatabaseHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigSectionDatabaseHint;
            Sub_entry_ConfigDbHostLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigDbHostLabel;
            Sub_entry_ConfigDbNameLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigDbNameLabel;
            Sub_entry_ConfigDbUserLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigDbUserLabel;
            Sub_entry_ConfigDbPasswordLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigDbPasswordLabel;
            Sub_entry_ConfigDbPortLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigDbPortLabel;
            Sub_entry_ConfigEditorInfo.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfigEditorInfo;
            Sub_entry_ReloadConfigButton.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ReloadConfigButton;
            Sub_entry_SaveConfigButton.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_SaveConfigButton;
            sub_entry_userManger.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_userManger;
            Sub_entry_UserCardHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_UserCardHint;
            Sub_entry_NewUser.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_NewUser;
            Sub_entry_NewUserHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_NewUserHint;
            Sub_entry_Username.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Username;
            Sub_entry_Password.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Password;
            Sub_entry_ConfirmPassword.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfirmPassword;
            Sub_entry_Role.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Role;
            Sub_entry_User.ToolTip = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_UserToolTip;
            PwdBox1.ToolTip = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_PasswordToolTip;
             PwdBox2.ToolTip = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ConfirmPasswordToolTip;
            Sub_entry_role.ToolTip = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Role;
            
            Sub_entry_addUser.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_addUser;
            Sub_Entry_existingUser.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_Entry_existingUser;
            Sub_entry_UserListHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_UserListHint;
            Sub_netry_updateList.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_netry_updateList;
            Sub_entry_LoadingPreferences.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_LoadingPreferences;

        }

        private void PwdBox1_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewmodel?.UpdateNewPassword(PwdBox1.Password);
        }

        private void PwdBox2_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewmodel?.UpdateConfirmPassword(PwdBox2.Password);
        }

        private void ViewmodelOnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_viewmodel == null) return;

            if (e.PropertyName == nameof(PreferenceViewModel.NewPassword) &&
                PwdBox1.Password != (_viewmodel.NewPassword ?? string.Empty))
                PwdBox1.Password = _viewmodel.NewPassword ?? string.Empty;

            if (e.PropertyName == nameof(PreferenceViewModel.ConfirmPassword) &&
                PwdBox2.Password != (_viewmodel.ConfirmPassword ?? string.Empty))
                PwdBox2.Password = _viewmodel.ConfirmPassword ?? string.Empty;

            if (e.PropertyName == nameof(PreferenceViewModel.CanManageConfigFile) ||
                e.PropertyName == nameof(PreferenceViewModel.IsAdministrator))
                UpdateResponsiveLayout();
        }

        private void PreferenceView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_viewmodel != null)
            {
                _viewmodel.PropertyChanged -= ViewmodelOnPropertyChanged;
                _viewmodel.Dispose();
            }

            Unloaded -= PreferenceView_Unloaded;
        }

        private void PreferenceView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private void PreferenceView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private void UpdateResponsiveLayout()
        {
            if (!IsLoaded || _viewmodel == null) return;

            bool hasConfig   = _viewmodel.CanManageConfigFile;
            bool hasUserMgmt = _viewmodel.IsAdministrator;

            var cols = PreferenceCardsGrid.ColumnDefinitions;
            var star = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
            var zero = new System.Windows.GridLength(0);

            if (!hasConfig)
            {
                // Only Language card — fill everything
                cols[0].Width = star;
                cols[1].Width = zero;
                cols[2].Width = zero;
            }
            else if (!hasUserMgmt)
            {
                // Language + Config
                double available  = Math.Max(ActualWidth - 36, 500);
                double sideWidth  = Math.Max(260, Math.Min(320, available * 0.24));
                cols[0].Width = new System.Windows.GridLength(sideWidth);
                cols[1].Width = star;
                cols[2].Width = zero;
            }
            else
            {
                // All 3 cards
                double available = Math.Max(ActualWidth - 36, 700);
                double sideWidth = Math.Max(260, Math.Min(320, available * 0.21));
                cols[0].Width = new System.Windows.GridLength(sideWidth);
                cols[1].Width = star;
                cols[2].Width = new System.Windows.GridLength(sideWidth);
            }
        }
    }
}
