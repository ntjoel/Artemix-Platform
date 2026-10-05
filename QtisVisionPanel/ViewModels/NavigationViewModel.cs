
using FontAwesome.WPF;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;

namespace QtisVisionPanel.ViewModels
{
    public class NavigationViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<MenuItem> MenuItems { get; set; }
        public ControlButtonsViewModel ControlButtonsVM { get; set; }
        public ICommand MenuItemCommand { get; }

        public event Action<string> NavigateRequested;
        private MenuItem _selectedMenuItem;
        private bool _isInitialized = false;
        private readonly AuthorizationService _authService;
        private readonly HashSet<string> _accessibleViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public NavigationViewModel()
        {
            MenuItems = new ObservableCollection<MenuItem>();
            MenuItemCommand = new RelayCommand<string>(OnMenuItemClicked);
            ControlButtonsVM = new ControlButtonsViewModel();

            UserSession.OnRoleChanged += (s, e) => ReloadMenu();
            _authService = ServiceLocator.Authorization;
            _ = InitializeMenuAsync(); // fire-and-forget con gestione errori
            _isInitialized = true;
        }
        private async Task InitializeMenuAsync()
        {
            try
            {
                await LoadMenuItemsAsync();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore inizializzazione menu: {ex.Message}");
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    MenuItems.Clear();
                    MenuItems.Add(new MenuItem
                    {
                        Header = "⚠ Database non raggiungibile",
                        Icon = FontAwesomeIcon.ExclamationTriangle
                    });
                }));
            }
        }
        private async Task ExecuteExternalApp(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path))
                {
                    await Task.Run(() =>
                    {
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                    });

                    MainWindow.logger?.Info($"External app executed: {path}");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error executing external app: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nell'avvio dell'applicazione: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }
        // Metodo per ricaricare il menu quando il ruolo cambia.
        // Fase 0 stabilita': era async void con il primo await FUORI dal try/catch —
        // un errore in LoadMenuItemsAsync sfuggiva a ogni protezione. Ora wrapper + core Task.
        public void ReloadMenu()
        {
            ReloadMenuCoreAsync().SafeFireAndForget();
        }

        private async Task ReloadMenuCoreAsync()
        {
            if (!_isInitialized) return;
            await LoadMenuItemsAsync();
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    
                    // Forza refresh dei testi del menu
                    foreach (var item in MenuItems)
                    {
                        RefreshMenuItemTexts(item);
                    }
                    MainWindow.logger?.Info($"Menu ricaricato per {UserSession.CurrentUser}");
                });
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel ricaricamento del menu: {ex.Message}");
            }
        }
        // Aggiungi questo metodo helper
        private void RefreshMenuItemTexts(MenuItem item)
        {
            // Ricarica il testo dal ServerMessagePersonalize
            if (!string.IsNullOrEmpty(item.ViewName))
            {
                string localizedHeader = ServerMessagePersonalize.GetMessage($"Sub_entry_{item.ViewName}_NAv");
                if (localizedHeader != $"Sub_entry_{item.ViewName}_NAv")
                {
                    item.Header = localizedHeader;
                }
            }

            // Ricorsione per i figli
            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    RefreshMenuItemTexts(child);
                }
            }
        }
        public void OnExpanderClicked(MenuItem clickedItem)
        {
            if (clickedItem == null) return;

            Debug.WriteLine($"Expander clicked: {clickedItem.Header}, IsExpanded: {clickedItem.IsExpanded}");

            // Se l'item è stato espanso, collassa tutti gli altri
            if (clickedItem.IsExpanded)
            {
                CollapseAllExcept(clickedItem);
            }
        }

        private void CollapseAllExcept(MenuItem itemToKeepExpanded)
        {
            foreach (var menuItem in MenuItems)
            {
                CollapseAllChildrenExcept(menuItem, itemToKeepExpanded);
            }
        }

        private void CollapseAllChildrenExcept(MenuItem currentItem, MenuItem itemToKeepExpanded)
        {
            if (currentItem == null) return;

            // Se non è l'item da mantenere espanso e non è un suo parent, collassalo
            if (currentItem != itemToKeepExpanded && !IsAncestorOf(itemToKeepExpanded, currentItem))
            {
                currentItem.IsExpanded = false;
            }

            foreach (var child in currentItem.Children ?? new ObservableCollection<MenuItem>())
            {
                CollapseAllChildrenExcept(child, itemToKeepExpanded);
            }
        }

        private bool IsAncestorOf(MenuItem potentialAncestor, MenuItem item)
        {
            if (potentialAncestor == null || item == null) return false;

            // Verifica se potentialAncestor è un antenato di item
            foreach (var child in potentialAncestor.Children ?? new ObservableCollection<MenuItem>())
            {
                if (child == item || IsAncestorOf(child, item))
                    return true;
            }
            return false;
        }

        private void OnMenuItemClicked(string viewName)
        {
            if (_accessibleViews.Contains(viewName) || _authService.CanAccessViewCached(viewName))
            {
                SetSelectedMenuItem(viewName);
                NavigateRequested?.Invoke(viewName);
            }
            else
            {
                new SystemNotificationWindow("Accesso negato", "Non hai i permessi per accedere a questa vista", NotificationSeverity.Warning).ShowDialog();
            }
        }

        private void SetSelectedMenuItem(string viewName)
        {
            // Reset della selezione precedente
            if (_selectedMenuItem != null)
            {
                _selectedMenuItem.IsSelected = false;
            }

            // Trova il nuovo item da selezionare
            var newSelectedItem = FindMenuItemByViewName(MenuItems, viewName);
            if (newSelectedItem != null)
            {
                newSelectedItem.IsSelected = true;
                _selectedMenuItem = newSelectedItem;

                // Espandi tutti i parent dell'item selezionato
                ExpandParents(newSelectedItem);
            }
        }
        private MenuItem FindMenuItemByViewName(ObservableCollection<MenuItem> items, string viewName)
        {
            foreach (var item in items)
            {
                if (item.ViewName == viewName)
                    return item;

                var foundInChildren = FindMenuItemByViewName(item.Children, viewName);
                if (foundInChildren != null)
                    return foundInChildren;
            }
            return null;
        }

        private void ExpandParents(MenuItem item)
        {
            var parent = FindParent(MenuItems, item);
            while (parent != null)
            {
                parent.IsExpanded = true;
                parent = FindParent(MenuItems, parent);
            }
        }
        private MenuItem FindParent(ObservableCollection<MenuItem> items, MenuItem child)
        {
            foreach (var item in items)
            {
                if (item.Children.Contains(child))
                    return item;

                var parent = FindParent(item.Children, child);
                if (parent != null)
                    return parent;
            }
            return null;
        }

        private async Task LoadMenuItemsAsync()
        {
            try
            {
                await _authService.EnsurePermissionsLoadedAsync();

                var newMenuItems = new List<MenuItem>();
                var accessibleViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Header sezione (sempre visibile)
                newMenuItems.Add(CreateSectionHeader(ServerMessagePersonalize.CurrentMessages.messages.lbVisionSystem));

                // Overview - sempre visibile perché i suoi figli hanno i permessi
                var overview = new MenuItem
                {
                    Header = ServerMessagePersonalize.CurrentMessages.messages.lbOverview,
                    Icon = FontAwesomeIcon.Eye,
                    IsExpanded = false,
                };

                if (_authService.CanAccessViewCached("Channel1"))
                {
                    overview.Children.Add(CreateMenuItem("Channel1", "lb100", FontAwesomeIcon.Camera));
                    accessibleViews.Add("Channel1");
                }

                if (_authService.CanAccessViewCached("Statistics"))
                {
                    overview.Children.Add(CreateMenuItem("Statistics", "lb101", FontAwesomeIcon.Dashboard));
                    accessibleViews.Add("Statistics");
                }

                if (_authService.CanAccessViewCached("Counters"))
                {
                    overview.Children.Add(CreateMenuItem("Counters", "Sub_entry_Counter_NAv", FontAwesomeIcon.AreaChart));
                    accessibleViews.Add("Counters");
                }

                if (overview.Children.Any())
                {
                    newMenuItems.Add(overview);
                }

                // System Editors - solo se almeno un figlio è accessibile
                var visionSystem = new MenuItem
                {
                    Header = ServerMessagePersonalize.CurrentMessages.messages.lbSystemEditor,
                    Icon = FontAwesomeIcon.Edit,
                    IsExpanded = false
                };

                if (_authService.CanAccessViewCached("RecipeManager"))
                {
                    visionSystem.Children.Add(CreateMenuItem("RecipeManager", "Sub_entry_RecipeManager_NAv", FontAwesomeIcon.File));
                    accessibleViews.Add("RecipeManager");
                }

                if (_authService.CanAccessViewCached("InspectionConfig"))
                {
                    visionSystem.Children.Add(CreateMenuItem("InspectionConfig", "Sub_entry_Ispection_Conf_NAv", FontAwesomeIcon.Cogs));
                    accessibleViews.Add("InspectionConfig");
                }

                if (_authService.CanAccessViewCached("JobToolEditor"))
                {
                    visionSystem.Children.Add(CreateMenuItem("JobToolEditor", "Sub_entry_Tool_edit_NAv", FontAwesomeIcon.Wrench));
                    accessibleViews.Add("JobToolEditor");
                }

                if (visionSystem.Children.Any())
                {
                    newMenuItems.Add(visionSystem);
                }

                // Sezione Dati Macchina
                newMenuItems.Add(CreateSectionHeader(ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_MachineDataSection));

                var settings = new MenuItem
                {
                    Header = ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_Setting ?? "Impostazioni",
                    Icon = FontAwesomeIcon.Wrench,
                    IsExpanded = false
                };

                if (_authService.CanAccessViewCached("Preferences"))
                {
                    settings.Children.Add(CreateMenuItem("Preferences", "Sub_entry_PreferenceView", FontAwesomeIcon.Cog));
                    accessibleViews.Add("Preferences");
                }

                if (_authService.CanAccessViewCached("Setting"))
                {
                    settings.Children.Add(CreateMenuItem("Setting", "Sub_entry_PCIE_IO_Setting", FontAwesomeIcon.Wrench));
                    accessibleViews.Add("Setting");
                }

                if (_authService.CanAccessViewCached("EjectionAlarms"))
                {
                    settings.Children.Add(CreateMenuItem("EjectionAlarms", "Sub_entry_Alarms_Rules", FontAwesomeIcon.ExclamationTriangle));
                    accessibleViews.Add("EjectionAlarms");
                }

                if (_authService.CanAccessViewCached("PowerFlex525"))
                {
                    settings.Children.Add(CreateMenuItem("PowerFlex525", "Sub_entry_PowerFlex525Menu", FontAwesomeIcon.Plug));
                    accessibleViews.Add("PowerFlex525");
                }

                if (_authService.CanAccessViewCached("OpcUaConfiguration"))
                {
                    settings.Children.Add(CreateMenuItem("OpcUaConfiguration", "Sub_entry_OpcUaConfigurationMenu", FontAwesomeIcon.Exchange));
                    accessibleViews.Add("OpcUaConfiguration");
                }

                if (settings.Children.Any())
                {
                    newMenuItems.Add(settings);
                }

                if (_authService.CanAccessViewCached("Automation") && HasAutomationExecutableConfigured())
                {
                    newMenuItems.Add(CreateAutomationMenuItem());
                    accessibleViews.Add("Automation");
                }

                // Sezione Diagnostica
                newMenuItems.Add(CreateSectionHeader(ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_DiagnosticSection));

                if (_authService.CanAccessViewCached("Alarmsview"))
                {
                    newMenuItems.Add(CreateMenuItem("Alarmsview", "sub_entry_alarms", FontAwesomeIcon.ExclamationCircle));
                    accessibleViews.Add("Alarmsview");
                }

                if (_authService.CanAccessViewCached("DataInspector"))
                {
                    newMenuItems.Add(CreateMenuItem("DataInspector", "sub_entry_dataInspector", FontAwesomeIcon.Database));
                    accessibleViews.Add("DataInspector");
                }

                if (_authService.CanAccessViewCached("SystemDiagnostics"))
                {
                    newMenuItems.Add(new MenuItem
                    {
                        Header = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_SystemDiagnosticsMenu", "PC Diagnostics"),
                        ViewName = "SystemDiagnostics",
                        Icon = FontAwesomeIcon.Desktop,
                        Command = MenuItemCommand,
                        CommandParameter = "SystemDiagnostics"
                    });
                    accessibleViews.Add("SystemDiagnostics");
                }

                // Sezione Help
                newMenuItems.Add(CreateSectionHeader(ServerMessagePersonalize.CurrentMessages.messages.Sub_entry_HelpSection));

                if (_authService.CanAccessViewCached("Manual"))
                {
                    newMenuItems.Add(CreateMenuItem("Manual", "lbManual", FontAwesomeIcon.Book));
                    accessibleViews.Add("Manual");
                }

                if (_authService.CanAccessViewCached("Assistance"))
                {
                    newMenuItems.Add(CreateMenuItem("Assistance", "lbAssistance", FontAwesomeIcon.Laptop));
                    accessibleViews.Add("Assistance");
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MenuItems.Clear();
                    foreach (var item in newMenuItems)
                    {
                        MenuItems.Add(item);
                    }
                });

                _accessibleViews.Clear();
                foreach (var viewName in accessibleViews)
                {
                    _accessibleViews.Add(viewName);
                }

                MainWindow.logger?.Debug($"Menu caricato con {newMenuItems.Count} voci principali.");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel caricamento del menu: {ex.Message}");
            }
        }
        private MenuItem CreateMenuItem(string viewName, string messageKey, FontAwesomeIcon icon)
        {
            return new MenuItem
            {
                Header = ServerMessagePersonalize.GetMessage(messageKey) ?? viewName,
                ViewName = viewName,
                Icon = icon,
                Command = MenuItemCommand,
                CommandParameter = viewName
            };
        }

        private MenuItem CreateAutomationMenuItem()
        {
            string automationPath = GetAutomationExecutablePath();

            if (!string.IsNullOrWhiteSpace(automationPath))
            {
                return new MenuItem
                {
                    Header = ServerMessagePersonalize.GetMessage("Sub_entry_automationBtn") ?? "Automation",
                    ViewName = "Automation",
                    Icon = FontAwesomeIcon.Television,
                    Command = new RelayCommand(async _ => await OpenAutomationExternalAppAsync()),
                    CommandParameter = "Automation",
                    IsExternalCommand = true,
                    ExternalAppPath = automationPath
                };
            }

            return CreateMenuItem("Automation", "Sub_entry_automationBtn", FontAwesomeIcon.Television);
        }

        private async Task OpenAutomationExternalAppAsync()
        {
            try
            {
                string automationPath = GetAutomationExecutablePath();
                if (string.IsNullOrWhiteSpace(automationPath))
                {
                    new SystemNotificationWindow("Configurazione mancante", "Percorso applicazione Automation non configurato.", NotificationSeverity.Warning).ShowDialog();
                    return;
                }

                SetSelectedMenuItem("Automation");
                CollapseAllExcept(_selectedMenuItem);

                await ExecuteExternalApp(automationPath);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore apertura Automation: {ex.Message}");
                new SystemNotificationWindow("Errore", $"Errore nell'avvio dell'applicazione Automation: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        private string GetAutomationExecutablePath()
        {
            return MainWindow.configManager?.Config?.Configuration?.externalApp_automation?.Trim();
        }

        private bool HasAutomationExecutableConfigured()
        {
            string automationPath = GetAutomationExecutablePath();
            if (string.IsNullOrWhiteSpace(automationPath))
            {
                return false;
            }

            if (File.Exists(automationPath))
            {
                return true;
            }

            MainWindow.logger?.Debug($"Automation menu nascosto: file configurato non trovato '{automationPath}'.");
            return false;
        }

        private MenuItem CreateSectionHeader(string title)
        {
            return new MenuItem
            {
                Header = title,
                IsSectionHeader = true,
                Icon = FontAwesomeIcon.None, // O un'icona specifica se preferisci
                SectionColor = "#FF0000" // Rosso come nell'immagine
            };
        }
       
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }

}
