using QtisVisionPanel.ViewModels;
using QtisVisionPanel.ServerMessage;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;


namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Logica di interazione per RecipeManagerView.xaml
    /// </summary>
    public partial class RecipeManagerView : UserControl
    {
        private const double CompactWorkspaceThreshold = 1040.0;
        private bool? _isCompactWorkspace;

        public RecipeManagerView()
        {
            InitializeComponent();
            // Inizializza i converter
            InitializeMessagelabel();
            this.Loaded += RecipeManagerView_Loaded;
            this.IsVisibleChanged += RecipeManagerView_IsVisibleChanged;

        }
        private  async void RecipeManagerView_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyRecipeWorkspaceLayout(RecipeWorkspaceGrid.ActualWidth);

            var vm = DataContext as RecipeManagerViewModel;
            if (vm != null)
            {
                // Aggiorna i permessi quando la vista viene caricata
                await vm.UpdatePermissionsAsync();
                vm.RefreshRecipeMachineRuntimeOverview();

                // Aggiorna i permessi quando la vista
               // await vm.InitializeAsync();
            }
        }
        private async void RecipeManagerView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                var vm = DataContext as RecipeManagerViewModel;
                if (vm != null)
                {
                    // Aggiorna i permessi quando la vista diventa visibile
                    await vm.UpdatePermissionsAsync();
                    vm.RefreshRecipeMachineRuntimeOverview();
                }
            }
        }
        private void CheckIntegrity_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as RecipeManagerViewModel;
            if (vm?.SelectedRecipe != null)
            {
                bool vppExists = System.IO.File.Exists(vm.SelectedRecipe.FilePath);
                bool xmlExists = System.IO.File.Exists(vm.SelectedRecipe.XmlFilePath);

                string message = $"Nome: {vm.SelectedRecipe.Name}\n\n" +
                               $"File VPP: {(vppExists ? "✓ PRESENTE" : "✗ MANCANTE")}\n" +
                               $"File XML: {(xmlExists ? "✓ PRESENTE" : "✗ MANCANTE")}\n" +
                               $"In Database: {(vm.SelectedRecipe.ExistsInDatabase ? "✓ PRESENTE" : "✗ NON PRESENTE")}\n" +
                               $"Ultima Modifica: {vm.SelectedRecipe.LastModified:dd/MM/yyyy HH:mm:ss}\n" +
                               $"Stato: {(vm.SelectedRecipe.Status == RecipeStatus.InProduction ? "IN PRODUZIONE" : "NORMALE")}";

                new SystemNotificationWindow("Verifica Integrità", message, NotificationSeverity.Info).ShowDialog();
            }
            }

        private void InspectionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as RecipeManagerViewModel;
            if (vm != null)
            {
                vm.IsModified = true;
            }

        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var vm = DataContext as RecipeManagerViewModel;
            if (vm != null &&
                (vm.CanEditRecipe || vm.CanEditTolerances || vm.CanEditProductInfo || vm.CanEditTriggerDelay))
            {
                vm.IsModified = true;
            }
        }

        private void RecipeWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyRecipeWorkspaceLayout(e.NewSize.Width);
        }

        private void ApplyRecipeWorkspaceLayout(double availableWidth)
        {
            if (availableWidth <= 0 || double.IsNaN(availableWidth))
            {
                return;
            }

            bool useCompactLayout = availableWidth < CompactWorkspaceThreshold;
            if (_isCompactWorkspace == useCompactLayout)
            {
                return;
            }

            _isCompactWorkspace = useCompactLayout;

            if (useCompactLayout)
            {
                RecipeWorkspaceMainColumn.Width = new GridLength(1, GridUnitType.Star);
                RecipeWorkspaceParameterColumn.Width = new GridLength(0);

                Grid.SetRow(RecipeWorkspaceMainPanel, 0);
                Grid.SetColumn(RecipeWorkspaceMainPanel, 0);
                Grid.SetColumnSpan(RecipeWorkspaceMainPanel, 2);
                RecipeWorkspaceMainPanel.Margin = new Thickness(0);

                Grid.SetRow(RecipeWorkspaceParameterPanel, 1);
                Grid.SetColumn(RecipeWorkspaceParameterPanel, 0);
                Grid.SetColumnSpan(RecipeWorkspaceParameterPanel, 2);
                RecipeWorkspaceParameterPanel.Margin = new Thickness(0, 8, 0, 0);
                return;
            }

            RecipeWorkspaceMainColumn.Width = new GridLength(1.8, GridUnitType.Star);
            RecipeWorkspaceParameterColumn.Width = new GridLength(1, GridUnitType.Star);

            Grid.SetRow(RecipeWorkspaceMainPanel, 0);
            Grid.SetColumn(RecipeWorkspaceMainPanel, 0);
            Grid.SetColumnSpan(RecipeWorkspaceMainPanel, 1);
            RecipeWorkspaceMainPanel.Margin = new Thickness(0, 0, 5, 0);

            Grid.SetRow(RecipeWorkspaceParameterPanel, 0);
            Grid.SetColumn(RecipeWorkspaceParameterPanel, 1);
            Grid.SetColumnSpan(RecipeWorkspaceParameterPanel, 1);
            RecipeWorkspaceParameterPanel.Margin = new Thickness(5, 0, 0, 0);
        }

        private void RecipeMultiShotMode_DropDownClosed(object sender, System.EventArgs e)
        {
            var vm = DataContext as RecipeManagerViewModel;
            if (vm != null && vm.CanEditTriggerDelay)
            {
                vm.IsModified = true;
                vm.RefreshRecipeViewAssignments();
            }

            RefreshRecipeMachineRuntimeOverviewDeferred();
        }

        private void RecipeRuntimeAdjustment_LostFocus(object sender, RoutedEventArgs e)
        {
            RefreshRecipeMachineRuntimeOverviewDeferred();
        }

        private void RefreshRecipeMachineRuntimeOverviewDeferred()
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.DataBind,
                new System.Action(() =>
                    (DataContext as RecipeManagerViewModel)?.RefreshRecipeMachineRuntimeOverview()));
        }

        private void RecipeCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is RecipeItem recipeItem)
            {
                var vm = DataContext as RecipeManagerViewModel;
                if (vm != null)
                {
                    vm.SelectedRecipe = recipeItem;
                }
            }

        }
        private void InitializeMessagelabel()
        {
            Sub_entry_RecipeReadOnlyLoginButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeLoginAction", "Login");
            Sub_entry_RecipesAvailableTitleText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipesAvailableTitle", "Available recipes");
            Sub_entry_RecipesAvailableCountText.Text = string.Format(
                ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipesAvailableCount", "{0} recipes available in archive"),
                (DataContext as RecipeManagerViewModel)?.Recipes?.Count ?? 0);
            Sub_entry_RecipeLoadText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeLoad", "Load");
            Sub_entry_RecipeDeleteText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeDelete", "Delete");
            Sub_entry_RecipeSaveText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeSave", "Save");
            Sub_entry_RecipeDuplicateText.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeDuplicate", "Duplicate");
          Sub_entry_ProductInfoCart.Header=  ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ProductInfoCart;
            Sub_entry_Nominal_Length.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Nominal_Length;
            Sub_entry_Navigation.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Navigation;
            Sub_entry_InfoRicetta.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_InfoRicetta;
            Sub_entry_InfoRicetta1.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_InfoRicetta1;
            Sub_entry_Cart_recipeInfo.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Cart_recipeInfo;
            Sub_entry_Code_recipe.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Code_recipe;
            Sub_entry_Recipe_Name.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Recipe_Name;
            Sub_entry_Recipe_descrption.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Recipe_descrption;
            Sub_entry_Recipe_path.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Recipe_path;
            
            Sub_entry_RecipeConfig.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_RecipeConfig;
            Sub_entry_RecipeMachineType.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_RecipeMachineType;
           
            Sub_entry_Height.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Height;
            Sub_entry_Width.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Width;
            Sub_entry_Length.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Length;
            Sub_entry_Depth.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Depth;
            Sub_entry_Height3DCheck.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Height;
            Sub_entry_Width3DCheck.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Width;
            Sub_entry_Length3DCheck.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Length;
            Sub_entry_Depth3DCheck.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Depth;
            Sub_entry_Nominal_height.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Nominal_height;
            Sub_entry_Roll_diameter.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Roll_diameter;
            Sub_entry_Roll_axis.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Roll_axis;
            Sub_entry_Core_diameter.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Core_diameter;
            Sub_entry_Nominal_width.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Nominal_width;
            Sub_entry_Additional_Inf.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Additional_Inf;
            Sub_entry_Select_Image_Button.Content= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Select_Image_Button;
            Sub_entry_Select_Image_path.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Select_Image_path;
            Sub_entry_Width1.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Width;
            Sub_entry_Length1.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Length;
            NoImageText.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_NoImageText;
            Sub_entry_Inspection_Enable.Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Inspection_Enable", "Ispezioni abilitate");
            Sub_entry_Ejection_Enable.Header= ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Ejection_Enable", "Ispezioni che generano scarto");
            Sub_entry_Cart_Db_inf.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Cart_Db_inf;
            Sub_entry_CheckInDb.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_CheckInDb;
            Sub_entry_ID_Db.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ID_Db;
            Sub_entry_LastModif.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_LastModif;
            Sub_entry_setting_altezza.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_toll_altezza;
            Sub_entry_toll_altezza.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_toll_altezza;
            Sub_entry_Print_Centering.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Print_Centering;
            Sub_entry_Top.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Top;
            Sub_entry_bottom.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_bottom;
            Sub_entry_Right.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Right;
            Sub_entry_Left.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Left;
            Sub_entry_Toll_printcenterint.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Toll_printcenterint;
            Sub_entry_Top_Inspection_Thresholds.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Top_Inspection_Thresholds;
            Sub_entry_Logo.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Logo;
            Sub_entry_Flaps.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Flaps;
            Sub_entry_ShapeValue.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ShapeValue;
            Sub_entry_Toll_shape.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Toll_shape;
            Sub_entry_Side_Inspection_Thresholds.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Side_Inspection_Thresholds;
            Sub_entry_Sealing.Text= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Sealing;
            Sub_entry_TopTriggerDelayLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_TopTriggerDelay;
            Sub_entry_SideTriggerDelayLabel.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_LeftTriggerDelay", "Trigger Delay Left:");
            Sub_entry_RightTriggerDelayLabel.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_RightTriggerDelay", "Trigger Delay Right:");
            Sub_entry_BottomTriggerDelayLabel.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_BottomTriggerDelay", "Trigger Delay Bottom:");
            Sub_entry_FrontTriggerDelayLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_FrontTriggerDelay;
            Sub_entry_TriggerDelayNote.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_TriggerDelayNote;
            Sub_entry_RecipeCameraTriggersExpander.Header = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_RecipeCameraTriggersTitle", "Camera triggers for this recipe");
            Sub_entry_RecipeCameraTriggersHint.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_RecipeCameraTriggersHint", "The board, channel and polarity remain global. Select only whether each camera trigger inherits, is enabled, or is disabled for this product.");
            Sub_entry_RecipeTopTriggerModeLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeTopTriggerMode", "TOP trigger");
            Sub_entry_RecipeFrontTriggerModeLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeFrontTriggerMode", "FRONT trigger");
            Sub_entry_RecipeRightTriggerModeLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeRightTriggerMode", "RIGHT trigger");
            Sub_entry_RecipeBottomTriggerModeLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeBottomTriggerMode", "BOTTOM trigger");
            Sub_entry_RecipeRuntimeCorrectionsTitle.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_RecipeRuntimeCorrectionsTitle",
                "Recipe camera position corrections");
            Sub_entry_RecipeRuntimeCorrectionsHint.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_RecipeRuntimeCorrectionsHint",
                "The base position remains global in Machine Setup. Enter only the positive or negative correction required by this product.");
            Sub_entry_RecipeTopPositionOffsetLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeTopPositionOffset", "TOP offset");
            Sub_entry_RecipeLeftPositionOffsetLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeLeftPositionOffset", "LEFT offset");
            Sub_entry_RecipeFrontPositionOffsetLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeFrontPositionOffset", "FRONT offset");
            Sub_entry_RecipeRightPositionOffsetLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeRightPositionOffset", "RIGHT offset");
            Sub_entry_RecipeBottomPositionOffsetLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeBottomPositionOffset", "BOTTOM offset");
            Sub_entry_RecipeMultiShotCorrectionsExpander.Header = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_RecipeMultiShotCorrectionsTitle",
                "Recipe MultiShot corrections");
            Sub_entry_RecipeMultiShotCorrectionsHint.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_RecipeMultiShotCorrectionsHint",
                "Channel, pulse, minimum interval, calibration and safety limits remain global. The recipe can inherit, enable or disable the profile and correct shot geometry.");
            Sub_entry_RecipeSideLeftMultiShotLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeMultiShotSideLeft", "SIDE / LEFT");
            Sub_entry_RecipeBottomMultiShotLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeMultiShotBottom", "BOTTOM");

            string multiShotMode = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeMultiShotMode", "Mode");
            string shotOffset = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeMultiShotShotOffset", "Shot count delta");
            string stepOffset = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeMultiShotStepOffset", "Step delta");
            string firstShotOffset = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RecipeMultiShotFirstShotOffset", "First shot delta");
            Sub_entry_RecipeSideLeftMultiShotModeLabel.Text = multiShotMode;
            Sub_entry_RecipeRightRearMultiShotModeLabel.Text = multiShotMode;
            Sub_entry_RecipeBottomMultiShotModeLabel.Text = multiShotMode;
            Sub_entry_RecipeSideLeftShotOffsetLabel.Text = shotOffset;
            Sub_entry_RecipeRightRearShotOffsetLabel.Text = shotOffset;
            Sub_entry_RecipeBottomShotOffsetLabel.Text = shotOffset;
            Sub_entry_RecipeSideLeftStepOffsetLabel.Text = stepOffset;
            Sub_entry_RecipeRightRearStepOffsetLabel.Text = stepOffset;
            Sub_entry_RecipeBottomStepOffsetLabel.Text = stepOffset;
            Sub_entry_RecipeSideLeftFirstShotOffsetLabel.Text = firstShotOffset;
            Sub_entry_RecipeRightRearFirstShotOffsetLabel.Text = firstShotOffset;
            Sub_entry_RecipeBottomFirstShotOffsetLabel.Text = firstShotOffset;
            Sub_entry_FrontTraceabilitySection.Header = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_FrontTraceabilitySection;
            Sub_entry_TraceabilityRequiredLabel.Content = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_TraceabilityRequired;
            Sub_entry_ExpectedCodePrefixLabel.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ExpectedCodePrefix;
            Sub_entry_ExpectedCodePrefixHint.Text = ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ExpectedCodePrefixHint;
            Sub_entry_ThreeDSection.Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DSection", "Top3D profilometer checks");
            Sub_entry_ThreeDHeightLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDHeight", "3D height");
            Sub_entry_ThreeDWidthLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDWidth", "3D width");
            Sub_entry_ThreeDLengthLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ThreeDLength", "3D length");
            string nominalLabel = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_NominalValue", "Nominal");
            string toleranceLabel = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ToleranceValue", "Tolerance");
            Sub_entry_Top3DNominalLabel1.Text = nominalLabel;
            Sub_entry_Top3DNominalLabel2.Text = nominalLabel;
            Sub_entry_Top3DNominalLabel3.Text = nominalLabel;
            Sub_entry_Top3DToleranceLabel1.Text = toleranceLabel;
            Sub_entry_Top3DToleranceLabel2.Text = toleranceLabel;
            Sub_entry_Top3DToleranceLabel3.Text = toleranceLabel;
            Sub_entry_Top3DProfileTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfile", "Top3D profile");
            Sub_entry_Top3DProfileEnableLabel.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DProfileEnabled", "Enable monitoring");
            Sub_entry_Top3DProfileDescription.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_Top3DProfileDescription",
                "Bulge index = High Tail - Median across the full ROI. It does not locate the defect and never commands physical reject in this baseline.");
            Sub_entry_Top3DMinimumValidPixelsLabel.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_Top3DMinimumValidPixels",
                "Minimum valid pixels");
            Sub_entry_Top3DMaximumBulgeLabel.Text = ServerMessagePersonalize.GetMessageOrDefault(
                "Sub_entry_Top3DMaximumBulge",
                "Maximum bulge index");
            Sub_entry_Top3DPrimaryViewLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DPrimaryView", "Top3D primary view");
            Sub_entry_Top3DSecondaryViewLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DSecondaryView", "Top2D secondary view");
            Sub_entry_Rapid_action.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Rapid_action;
            Sub_entry_Check_Integrity_Button.Content= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Check_Integrity_Button;
            Sub_entry_Recipe_Name.ToolTip= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_ToolTip;
            Sub_entry_Select_Image_path.ToolTip= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Recipe_nameToltip;
            Sub_entry_CartToll_dimenssion.Header= ServerMessage.ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_CartToll_dimenssion;




        }

       

    }
  
}
