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
using QtisVisionPanel.Services;

namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Logica di interazione per StatisticsView.xaml
    /// </summary>
    public partial class DbQuatis : UserControl
    {
        public DbQuatis()
        {
            InitializeComponent();

            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }

            InitializeAsync().SafeFireAndForget();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await Dashboard.EnsureCoreWebView2Async();

                var config = MainWindow.configManager?.Config?.Configuration;
                if (config != null && !string.IsNullOrWhiteSpace(config.Dashboard))
                {
                    Dashboard.Source = new Uri(config.Dashboard);
                }
                else
                {
                    // Fallback a un URL di default
                    Dashboard.Source = new Uri("http://localhost:8088/data/perspective/client/samplequickstart");
                }

                //await ImageShow.EnsureCoreWebView2Async();
                //if (config != null && !string.IsNullOrWhiteSpace(config.LogshowImage))
                //{
                //    ImageShow.Source = new Uri(config.LogshowImage);
                //}
                //else
                //{
                //    Dashboard.Source = new Uri("http://localhost");
                //}
            }
            catch (Exception ex)
            {
                new SystemNotificationWindow("Errore", $"Errore durante l'inizializzazione della dashboard: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }
    }
}
