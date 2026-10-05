using QtisVisionPanel.ViewModels;
using System;
using System.Windows.Controls;

namespace QtisVisionPanel.Views.UserControls
{
    public partial class OpcUaConfigurationView : UserControl, IDisposable
    {
        public OpcUaConfigurationView()
        {
            InitializeComponent();
            DataContext = new OpcUaConfigurationViewModel();
        }

        public void Dispose()
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
