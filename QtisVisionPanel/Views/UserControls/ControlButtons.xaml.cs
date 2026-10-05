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

namespace QtisVisionPanel.Views.UserControls
{
    /// <summary>
    /// Logica di interazione per ControlButtons.xaml
    /// </summary>
    public partial class ControlButtons : UserControl
    {
        private static ControlButtons _instance;
        public static ControlButtons Instance => _instance;
        public ControlButtons()
        {
            InitializeComponent();

            _instance = this;
        }
    }
}
