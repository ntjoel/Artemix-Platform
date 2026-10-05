using QtisVisionPanel.Models;
using QtisVisionPanel.Services;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QtisVisionPanel.Views.UserControls
{
    /// <summary>
    /// Logica di interazione per EjectionAlarmControl.xaml
    /// </summary>
    public partial class EjectionAlarmControl : UserControl
    {
        private object _viewModel;

        public EjectionAlarmControl()
        {
            InitializeComponent();

            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }

            var viewModelType = Type.GetType("QtisVisionPanel.ViewModels.EjectionAlarmCardViewModel, QtisVisionPanel", throwOnError: false);
            if (viewModelType != null)
            {
                _viewModel = Activator.CreateInstance(viewModelType);
                DataContext = _viewModel;
            }

            Loaded += UserControl_Loaded;
            Unloaded += UserControl_Unloaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("EjectionAlarmControl caricato");
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
        }

        public async Task ProcessInspectionResultAsync(object result)
        {
            if (_viewModel == null || result == null)
            {
                return;
            }

            MethodInfo processMethod = _viewModel.GetType().GetMethod("ProcessInspectionResultAsync", new[] { result.GetType() });
            if (processMethod?.Invoke(_viewModel, new[] { result }) is Task task)
            {
                await task;
            }
        }

        public void Refresh()
        {
            if (_viewModel == null)
            {
                return;
            }

            MethodInfo refreshMethod = _viewModel.GetType().GetMethod("RefreshAlarmsAsync", Type.EmptyTypes);
            if (refreshMethod?.Invoke(_viewModel, null) is Task task)
            {
                task.SafeFireAndForget();
            }
        }

        private void AddAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void AlarmCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(sender is Border border) || !(border.DataContext is EjectionAlarmConfig alarm))
            {
                return;
            }

            DataContext?.GetType().GetProperty("SelectedAlarm")?.SetValue(DataContext, alarm);

            var itemsControl = FindParent<ItemsControl>(border);
            if (itemsControl != null)
            {
                foreach (var child in FindVisualChildren<Border>(itemsControl))
                {
                    if (child != border && child.Tag is EjectionAlarmConfig childAlarm)
                    {
                        child.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                            childAlarm.IsTriggered ? "#E05353" : "#D7E3EF"));
                        child.BorderThickness = new Thickness(childAlarm.IsTriggered ? 2 : 1);
                    }
                }
            }

            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C79B8"));
            border.BorderThickness = new Thickness(3);
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            return parent as T;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null)
            {
                yield break;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }
}
