using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views.UserControls.DisplayRecord;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using QtisVisionPanel.Services;
using System.Windows.Input;

using MyMenuItem = QtisVisionPanel.Models.MenuItem;

namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Logica di interazione per NavigationMenu.xaml
    /// </summary>
    public partial class NavigationMenu : UserControl
    {
        private static NavigationMenu _instance;
        public static NavigationMenu Instance => _instance;
        public NavigationMenu()
        {
            InitializeComponent();
            LoadServerMessages();
            _instance = this;

            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }
        }
        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            HandleExpanderEvent(sender, true);
        }

        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            HandleExpanderEvent(sender, false);
        }
        private void HandleExpanderEvent(object sender, bool isExpanded)
        {
            if (sender is Expander expander && expander.DataContext is MyMenuItem menuItem)
            {
                // Aggiorna lo stato IsExpanded (dovrebbe già essere aggiornato dal binding)
                menuItem.IsExpanded = isExpanded;

                if (DataContext is NavigationViewModel viewModel)
                {
                    // Notifica il ViewModel
                    viewModel.OnExpanderClicked(menuItem);
                }

                Debug.WriteLine($"Expander: {menuItem.Header} - Expanded: {isExpanded}");
            }
        }

        private void LoadServerMessages()
        {
            Sub_entry_CompanyPanelTitle.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_CompanyPanelTitle", "Industrial Vision Control");
            Sub_entry_NavigationSummary.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_NavigationSummary", "Quick navigation of machine areas");
            PulsarInfo.ToolTip = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_OpenAboutTooltip", "Software version");
        }

        private void PulsarInfo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var aboutWindow = new AboutVersionWindow();
                if (owner != null)
                {
                    aboutWindow.Owner = owner;
                }

                aboutWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Unable to open software version window: {ex.Message}");
                new SystemNotificationWindow("Software Version",
                    $"Unable to open software version window:{Environment.NewLine}{ex.Message}",
                    NotificationSeverity.Error).ShowDialog();
            }
        }
    }

}
