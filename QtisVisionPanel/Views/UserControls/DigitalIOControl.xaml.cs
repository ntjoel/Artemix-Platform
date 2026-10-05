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
using QtisVisionPanel.Models;

namespace QtisVisionPanel.Views.UserControls
{
    public partial class DigitalIOControl : UserControl
    {
        public DigitalIOControl()
        {
            InitializeComponent();
        }

        // Commits the current row and clears focus when the user presses Enter inside
        // a signal DataGrid. Without this, WPF keeps the row in EditItem mode after
        // Enter, which blocks CollectionView.Refresh() during SaveConfiguration.
        private void OnSignalDataGridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var grid = sender as DataGrid;
            if (grid == null) return;
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            // Move focus to the DataGrid itself so the row is visually deselected from edit
            grid.Focus();
            e.Handled = true;
        }

        private void OnMachineOutputInitializingNewItem(object sender, InitializingNewItemEventArgs e)
        {
            var signal = e.NewItem as MachineSignalDefinition;
            if (signal == null) return;

            signal.Direction = MachineSignalDirection.Output;
            signal.Category = MachineSignalCategory.CameraTrigger;
            signal.Board = "PCIE-1756-BE";
            signal.Polarity = MachineSignalPolarity.ActiveHigh;
            signal.ReservedForRealSignal = true;
        }
    }
}
