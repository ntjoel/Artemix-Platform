using System.Windows;

namespace QtisVisionPanel.Models
{
    /// <summary>
    /// WPF binding proxy used when a target object is not part of the visual tree,
    /// for example DataGridColumn headers. The proxy exposes the parent DataContext
    /// as a bindable object reachable from column definitions.
    /// </summary>
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }
}
