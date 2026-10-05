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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using QtisVisionPanel.Services;

namespace QtisVisionPanel.Views.UserControls
{
    /// <summary>
    /// Logica di interazione per TopMenuBar.xaml
    /// </summary>
    public partial class TopMenuBar : UserControl
    {
        private Storyboard _rotateAnimation;
     //   public TopMenuBarViewModel viewModel => DataContext as TopMenuBarViewModel;
        public TopMenuBar()
        {
            InitializeComponent();
            loadServerMessage();

            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
        private void loadServerMessage()
        {
            Sub_entry_MachineStatusLabel.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_MachineStatusLabel", "Machine Status");
            Sub_entry_Recipe.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Recipe", "Active recipe");
            Sub_entry_speed.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_speed", "Speed");
            Sub_entry_Production.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Production", "Production");
            Sub_entry_QualityIndexTopBar.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_QualityIndex", "Quality Index");
            Sub_entry_GoodTotalLabelTopBar.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_GoodTotalLabel", "(Good/Total)");
            Sub_entry_SessionLabel.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Session", "Session");
            Sub_entry_RuntimeHoldLabel.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_RuntimeHold", "Runtime hold");
            Sub_entry_GuestLabel.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_Guest", "Guest");
            Sub_entry_LimitedAccessLabel.Text = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_LimitedAccess", "Limited access");
            Sub_entry_login.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_login", "Login");
            Sub_entry_logout.Content = ServerMessage.ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_logout", "Logout");
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Crea l'animazione di rotazione
            _rotateAnimation = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(2)),
                RepeatBehavior = RepeatBehavior.Forever
            };

            Storyboard.SetTarget(animation, StatusIcon);
            Storyboard.SetTargetProperty(animation,
                new PropertyPath("RenderTransform.Angle"));

            _rotateAnimation.Children.Add(animation);

            // Sottoscrivi agli eventi di cambio stato
            if (DataContext is TopMenuBarViewModel viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;

                // Controlla lo stato iniziale
                UpdateAnimationState(viewModel.CurrentMachineStatus);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is TopMenuBarViewModel viewModel)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _rotateAnimation?.Stop(StatusIcon);
            _rotateAnimation = null;
        }

        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnViewModelPropertyChanged(sender, e)));
                return;
            }

            if (e.PropertyName == nameof(TopMenuBarViewModel.CurrentMachineStatus) &&
                 DataContext is TopMenuBarViewModel viewModel)
            {
                UpdateAnimationState(viewModel.CurrentMachineStatus);
            }
        }
        private void UpdateAnimationState(TopMenuBarViewModel.MachineStatus status)
        {
            try
            {
                if (status == TopMenuBarViewModel.MachineStatus.Connecting || status == TopMenuBarViewModel.MachineStatus.Running)
                {
                    // Avvia l'animazione
                    if (_rotateAnimation != null)
                    {
                        _rotateAnimation.Begin(StatusIcon, true);
                    }
                }
                else
                {
                    // Ferma l'animazione e resetta la rotazione
                    if (_rotateAnimation != null)
                    {
                        _rotateAnimation.Stop(StatusIcon);
                    }

                    // Resetta la rotazione
                    var transform = StatusIcon.RenderTransform as RotateTransform;
                    if (transform != null)
                    {
                        transform.Angle = 0;
                    }
                    else
                    {
                        StatusIcon.RenderTransform = new RotateTransform(0);
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error updating animation state: {ex.Message}");
            }
        }
    }
}
