using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Views;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace QtisVisionPanel.ViewModels
{
    public class AssistanceViewModel : INotifyPropertyChanged
    {
        public string CompanyName => !string.IsNullOrWhiteSpace(MainWindow.configManager?.Config?.Configuration?.Compagny)
            ? MainWindow.configManager.Config.Configuration.Compagny
            : "Pulsar Engineering";
        public string CompanyArea => GetMessage("Sub_entry_AssistanceCompanyArea", "Industrial Vision Support");
        public string SupportSummary => GetMessage("Sub_entry_AssistanceSummary", "Quick collection of references, operational contacts and suggested support procedure.");
        public string ProductionSupport => GetMessage("Sub_entry_AssistanceProductionText", "Involve the line supervisor with active recipe, batch and event timestamp.");
        public string TechnicalSupport => GetMessage("Sub_entry_AssistanceTechnicalText", "Note VisionPro message, screenshot, event timestamp and machine status.");
        public string ITSupport => GetMessage("Sub_entry_AssistanceITText", "Verify database connection, machine network and remote access before opening a ticket.");
        public string SparePartsSupport => GetMessage("Sub_entry_AssistanceSpareText", "Prepare product code, recipe, affected camera side and reject photo.");
        public string ServiceChecklist => GetMessage("Sub_entry_AssistanceChecklist",
@"Before requesting support:
1. Open AlarmsView and identify the message.
2. Note the active recipe and the last event timestamp.
3. Save a screenshot of the current screen.
4. Verify whether VisionPro and database are Running / Connected.
5. Attach any image of the defective product.");

        public ICommand OpenManualCommand { get; }
        public ICommand OpenCompanyPortalCommand { get; }

        public AssistanceViewModel()
        {
            OpenManualCommand = new QtisVisionPanel.Models.RelayCommand(OpenManualView);
            OpenCompanyPortalCommand = new QtisVisionPanel.Models.RelayCommand(OpenCompanyPortal);
        }

        private void OpenManualView()
        {
            if (MainWindow.MainView != null && MainWindow.MainView.DataContext is MainViewModel mainVm)
            {
                mainVm.OnNavigateRequested("Manual");
            }
        }

        private void OpenCompanyPortal()
        {
            try
            {
                string supportPortalUrl = ResolveCompanyPortalTarget();

                if (string.IsNullOrWhiteSpace(supportPortalUrl))
                {
                    new SystemNotificationWindow(
                        GetMessage("Sub_entry_AssistancePortalCaption", "Assistance"),
                        GetMessage("Sub_entry_AssistancePortalUnavailable", "Company portal is not configured in the application.\nThe Assistance view still shows the main operational references."),
                        NotificationSeverity.Info).ShowDialog();
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = supportPortalUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error("Errore apertura portale assistenza: " + ex.Message);
                new SystemNotificationWindow(
                    GetMessage("Sub_entry_AssistancePortalCaption", "Assistance"),
                    GetMessage("Sub_entry_AssistancePortalOpenError", "Error while opening support portal:") + " " + ex.Message,
                    NotificationSeverity.Error).ShowDialog();
            }
        }

        private string ResolveCompanyPortalTarget()
        {
            var configuration = MainWindow.configManager?.Config?.Configuration;
            if (configuration == null)
            {
                return null;
            }

            string[] candidates =
            {
                configuration.Dashboard,
                configuration.ExternalAppPath,
                configuration.externalApp_reports
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
