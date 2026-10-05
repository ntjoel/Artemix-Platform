using QtisVisionPanel.ViewModels;
using System;
using System.Windows.Controls;

namespace QtisVisionPanel.Views.UserControls
{
    public partial class PowerFlex525View : UserControl, IDisposable
    {
        public PowerFlex525View()
        {
            InitializeComponent();
            DataContext = new PowerFlex525ViewModel();
        }

        public void Dispose()
        {
            (DataContext as IDisposable)?.Dispose();
        }
    }
}
