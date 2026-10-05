using Cognex.VisionPro;
using System;
using System.Windows;

namespace QtisVisionPanel.Views
{
    public partial class LiveDisplayWindow : Window
    {
        public LiveDisplayWindow()
        {
            InitializeComponent();
        }

        public string LiveTitle
        {
            get => Title;
            set => Title = value;
        }

        public CogRecordDisplay LiveDisplay => LiveCogDisplay;

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                if (LiveCogDisplay.LiveDisplayRunning)
                {
                    LiveCogDisplay.StopLiveDisplay();
                }

                LiveCogDisplay.StaticGraphics?.Clear();
                LiveCogDisplay.InteractiveGraphics?.Clear();
                LiveCogDisplay.Image = null;
                LiveCogDisplay.Record = null;
                LiveCogDisplay.Dispose();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Errore chiusura LiveDisplayWindow: {ex.Message}");
            }

            base.OnClosed(e);
        }
    }
}
