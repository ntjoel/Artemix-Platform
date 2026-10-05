using Cognex.VisionPro.ToolBlock;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;

namespace QtisVisionPanel.Views
{
    /// <summary>
    /// Interaction logic for JobToolEditorView.xaml
    ///
    /// The view now hosts a single shared WindowsFormsHost while the tab list is
    /// generated dynamically from the runtime QuickBuild job set.
    /// </summary>
    public partial class JobToolEditorView : UserControl
    {
        private JobToolEditorViewModel _viewModel;
        private WindowsFormsHost _currentHost;
        private bool _isChangingTab;
        private bool _isUnloading;

        public JobToolEditorView()
        {
            VisionProRuntimeBootstrap.TryPrepareJobEditor(out _);
            InitializeComponent();
            DataContext = new JobToolEditorViewModel();
            LoadServerMessages();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            Loaded += (s, e) => DisableTabAnimations();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isUnloading = false;
            LoadServerMessages();
            _viewModel = DataContext as JobToolEditorViewModel;
            if (_viewModel != null)
            {
                _viewModel.ToolBlockLoaded -= OnToolBlockLoaded;
                _viewModel.ToolBlockLoaded += OnToolBlockLoaded;
                _viewModel.RefreshLocalizedTexts();
                _viewModel.OnEditorViewLoadedAsync().SafeFireAndForget();
                _ = _viewModel.ReloadSelectedToolAsync();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isUnloading = true;

            if (_viewModel != null)
            {
                _viewModel.ToolBlockLoaded -= OnToolBlockLoaded;
                if (_viewModel.StopLiveDisplayCommand?.CanExecute(null) == true)
                {
                    _viewModel.StopLiveDisplayCommand.Execute(null);
                }

                _viewModel.OnEditorViewUnloadedAsync().SafeFireAndForget();
            }

            DisposeCurrentHost();
        }

        private void DisableTabAnimations()
        {
            var template = MainTabControl.Template;
            if (template == null)
            {
                return;
            }

            var contentPresenter = template.FindName("PART_SelectedContentHost", MainTabControl) as ContentPresenter;
            if (contentPresenter != null)
            {
                contentPresenter.RenderTransform = null;
            }
        }

        private void OnToolBlockLoaded(JobToolEditorPanelItem panel, string toolName, CogToolBlock toolBlock)
        {
            try
            {
                if (_viewModel == null || panel == null || toolBlock == null)
                {
                    return;
                }

                // Ignore late results from a job that is no longer the selected one.
                if (!ReferenceEquals(panel, _viewModel.SelectedJobPanel))
                {
                    return;
                }

                if (_currentHost?.Child != null)
                {
                    _currentHost.Child.Enabled = false;
                }

                Grid targetContainer = GetCurrentToolEditorContainer();
                if (targetContainer == null)
                {
                    Dispatcher.BeginInvoke(new Action(() => OnToolBlockLoaded(panel, toolName, toolBlock)),
                        System.Windows.Threading.DispatcherPriority.Loaded);
                    return;
                }

                DisposeCurrentHost();
                targetContainer.Children.Clear();

                var toolBlockEdit = new Cognex.VisionPro.ToolBlock.CogToolBlockEditV2
                {
                    Subject = toolBlock,
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    AllowDrop = false
                };

                _currentHost = new WindowsFormsHost
                {
                    Child = toolBlockEdit,
                    Height = targetContainer.ActualHeight,
                    Width = targetContainer.ActualWidth,
                    Focusable = false
                };

                System.Windows.Forms.Application.EnableVisualStyles();
                targetContainer.Children.Add(_currentHost);

                toolBlockEdit.SubjectChanged += (s, args) =>
                {
                    if (!_isUnloading)
                    {
                        _viewModel?.OnToolBlockModified();
                    }
                };

                toolBlock.Changed += (s, args) =>
                {
                    if (!_isUnloading)
                    {
                        _viewModel?.OnToolBlockModified();
                    }
                };

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_currentHost?.Child != null)
                    {
                        _currentHost.Child.Enabled = true;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                new SystemNotificationWindow("Errore", $"Errore nel caricamento del tool: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        private Grid GetCurrentToolEditorContainer()
        {
            return FindVisualChildByName<Grid>(MainTabControl, "ToolEditorContainer");
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
            {
                return null;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && typedChild.Name == name)
                {
                    return typedChild;
                }

                T descendant = FindVisualChildByName<T>(child, name);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private void DisposeCurrentHost()
        {
            if (_currentHost == null)
            {
                return;
            }

            try
            {
                if (_currentHost.Child != null)
                {
                    _currentHost.Child.Enabled = false;
                    if (_currentHost.Child is Cognex.VisionPro.ToolBlock.CogToolBlockEditV2 toolEdit)
                    {
                        toolEdit.Subject = null;
                    }

                    if (_currentHost.Child is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }

                _currentHost.Child = null;
                _currentHost.Dispose();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nella disposizione dell'host tool editor: {ex.Message}");
            }
            finally
            {
                _currentHost = null;
            }
        }

        private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isChangingTab || _viewModel == null || e.AddedItems.Count == 0)
            {
                return;
            }

            try
            {
                _isChangingTab = true;

                if (_currentHost?.Child != null)
                {
                    _currentHost.Child.Enabled = false;
                }

                MainTabControl.Focus();
                await System.Threading.Tasks.Task.Delay(50);
                await _viewModel.ReloadSelectedToolAsync();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel cambio tab del Job Tool Editor: {ex.Message}");
            }
            finally
            {
                _isChangingTab = false;
            }
        }

        private void JobToolEditorView_Loaded(object sender, RoutedEventArgs e)
        {
            MainTabControl.Focus();
        }

        private void MainTabControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentHost?.Child != null)
            {
                _currentHost.Child.Enabled = false;
            }

            MainTabControl.Focus();
        }

        private void LoadServerMessages()
        {
            if (DataContext is JobToolEditorViewModel viewModel)
            {
                viewModel.RefreshLocalizedTexts();
            }

            Sub_entry_RefreshToolsButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RefreshTools", "Refresh tools");
            Sub_entry_RunOnceButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RunOnce", "Run once");
            Sub_entry_LivePreviewIntervalLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_LivePreviewIntervalMs", "Interval (ms):");
            Sub_entry_LivePreviewPulseLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_LivePreviewPulseMs", "Pulse (ms):");
            Sub_entry_LivePreviewExposureLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_LivePreviewExposureUs", "Exposure (us):");
            Sub_entry_ReadExposureButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ReadExposure", "Read");
            Sub_entry_ApplyExposureButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ApplyExposure", "Apply exposure");
            Sub_entry_Top3DDetectionSensitivityLabel.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Top3DDetectionSensitivityLabel", "Detection sensitivity:");
            Sub_entry_ReadDetectionSensitivityButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ReadDetectionSensitivity", "Read");
            Sub_entry_ApplyDetectionSensitivityButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ApplyDetectionSensitivity", "Apply sensitivity");
            Sub_entry_LivePreviewButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_LivePreview", "Live preview");
            Sub_entry_StopPreviewButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_StopPreview", "Stop preview");
            Sub_entry_SaveToolButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Save", "Save");
            Sub_entry_QuickBuildButton.Content = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_QuickBuild", "QuickBuild");
        }
    }
}
