using System.ComponentModel;
using System.Windows;

namespace QtisVisionPanel.Services
{
    internal static class DesignModeHelper
    {
        private static readonly DependencyObject Probe = new DependencyObject();

        public static bool IsInDesignMode
        {
            get
            {
                return DesignerProperties.GetIsInDesignMode(Probe) ||
                       LicenseManager.UsageMode == LicenseUsageMode.Designtime;
            }
        }
    }
}
