using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using QtisVisionPanel.ViewModels;
using QtisVisionPanel.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace QtisVisionPanel.Views.UserControls
{
    /// <summary>
    /// Logica di interazione per StatisticsView.xaml
    /// </summary>
    public partial class StatisticsView : UserControl
    {
        public static StatisticsView Instance { get; private set; }
        public InspectionCounterViewModel ViewModel { get; set; }

        public StatisticsView()
        {
            InitializeComponent();
            Instance = this;

            loadServerMessage();

            if (DesignModeHelper.IsInDesignMode)
            {
                return;
            }

            ViewModel = new InspectionCounterViewModel();
            DataContext = ViewModel;
            SetupEventHandlers();
            UpdateCountersFromManager();
        }

        private void SetupEventHandlers()
        {
            // Subscribe on every Loaded so that navigation away-and-back re-wires the handler.
            Loaded += (s, e) =>
            {
                var manager = ServiceLocator.CounterManager;
                if (manager != null)
                {
                    manager.CountersUpdated -= OnCountersUpdated;   // dedup
                    manager.CountersUpdated += OnCountersUpdated;
                }
                ViewModel?.Resubscribe();
                UpdateCountersFromManager();
            };

            Unloaded += (s, e) =>
            {
                var counterManager = ServiceLocator.CounterManager;
                if (counterManager != null)
                    counterManager.CountersUpdated -= OnCountersUpdated;
                ViewModel?.Dispose();
            };
        }

        public void loadServerMessage()
        {
            if (ServerMessagePersonalize.CurrentMessages?.messages == null)
            {
                MainWindow.logger?.Warn("ServerMessage non inizializzato, uso testi di default");
                return;
            }

            var messages = ServerMessagePersonalize.CurrentMessages.messages;

            if (Sub_entry_SelectionCounters_Header != null)
                Sub_entry_SelectionCounters_Header.Text = messages.Sub_entry_SelectionCounters_Header;

            if (Sub_entry_DefectsCounters_Header != null)
                Sub_entry_DefectsCounters_Header.Text = messages.Sub_entry_DefectsCounters_Header;

            if (Sub_entry_SelectionCounters_Hint != null)
                Sub_entry_SelectionCounters_Hint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_SelectionCountersHint", "Production totals and quality trend");

            if (Sub_entry_DefectsCounters_Hint != null)
                Sub_entry_DefectsCounters_Hint.Text = ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_DefectsCountersHint", "Icon, inspection, reject reason and counter");

            if (Sub_entry_TotalDefects_Label != null)
                Sub_entry_TotalDefects_Label.Text = messages.Sub_entry_TotalDefects_Label;

            if (Sub_entry_btn_Reset != null)
                Sub_entry_btn_Reset.ToolTip = messages.Sub_entry_btn_Reset;

            if (Sub_entry_QualityIndex != null)
                Sub_entry_QualityIndex.Text = messages.Sub_entry_QualityIndex;

            if (Sub_entry_GoodTotalLabel != null)
                Sub_entry_GoodTotalLabel.Text = messages.Sub_entry_GoodTotalLabel;

            if (Sub_entry_btn_ToggleDisplayMode != null)
                Sub_entry_btn_ToggleDisplayMode.ToolTip = ViewModel != null
                    ? ViewModel.CounterDisplayModeToolTip
                    : ServerMessagePersonalize.GetMessageOrDefault("Sub_entry_ShowPercentageCounters", "Show counters in percentage");
        }

        private void OnCountersUpdated()
        {
            if (!IsLoaded || Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                Dispatcher.BeginInvoke(new Action(UpdateCountersFromManager));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void UpdateCountersFromManager()
        {
            var counterManager = ServiceLocator.CounterManager;
            var counters = counterManager.GetAllCounters();

            ViewModel.UpdateFromGlobalCounters(counters);
        }

        public void UpdateCountersFromDictionary(Dictionary<string, long> globalCounters)
        {
            if (ViewModel != null)
                ViewModel.UpdateFromGlobalCounters(globalCounters);
        }

        // Fase 0 stabilita': niente async void fuori dagli event handler — wrapper void +
        // core async Task con SafeFireAndForget (eccezioni sempre osservate e loggate).
        public void ResetCounters()
        {
            ResetCountersCoreAsync().SafeFireAndForget();
        }

        private async Task ResetCountersCoreAsync()
        {
            try
            {
                await ServiceLocator.CounterManager.ResetAllCountersAsync();
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Error resetting counters: {ex.Message}");
                new SystemNotificationWindow("Error", $"Error resetting counters: {ex.Message}", NotificationSeverity.Error).ShowDialog();
            }
        }

        private void CalculatePercentages()
        {
            var totalCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Header == "Total");
            var goodCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Header == "Good");
            var noGoodCounter = InspectionData.SelectionCounters.FirstOrDefault(c => c.Header == "No Good");

            if (totalCounter != null && totalCounter.Value > 0)
            {
                GoodPercentage = Math.Round((double)goodCounter.Value / totalCounter.Value * 100, 2);
                NoGoodPercentage = Math.Round((double)noGoodCounter.Value / totalCounter.Value * 100, 2);
            }
            else
            {
                GoodPercentage = 0;
                NoGoodPercentage = 0;
            }

            OnPropertyChanged(nameof(GoodPercentage));
            OnPropertyChanged(nameof(NoGoodPercentage));
        }

        // Fase 0 stabilita': era async void SENZA try/catch chiamato dal flusso di ispezione —
        // un'eccezione sul thread di background poteva abbattere il processo. Ora wrapper void
        // + core async Task con SafeFireAndForget (eccezione loggata, processo intatto).
        public void ProcessInspectionResult(bool isCompliant, Dictionary<string, bool> defects = null)
        {
            ProcessInspectionResultCoreAsync(isCompliant, defects).SafeFireAndForget();
        }

        private async Task ProcessInspectionResultCoreAsync(bool isCompliant, Dictionary<string, bool> defects)
        {
            if (ViewModel != null)
                await ViewModel.IncrementCountersAsync(isCompliant, defects);
        }

        public void UpdateSelectionCounter(string header, long newValue)
        {
            var counter = ViewModel.InspectionData.SelectionCounters.FirstOrDefault(c => c.Key == header)
                       ?? ViewModel.InspectionData.SelectionCounters.FirstOrDefault(c => c.Header == header);
            if (counter != null)
                counter.Value = newValue;
        }

        public void UpdateDefectCounter(string feature, long newCount)
        {
        }

        public void UpdateCountersFromGlobal(Dictionary<string, long> globalCounters)
        {
            if (ViewModel != null)
                ViewModel.UpdateFromGlobalCounters(globalCounters);
        }

        public void AddDefect(string feature, string iconPath, string toolTip, int initialCount = 0)
        {
            ViewModel.InspectionData.DefectsCounters.Add(new DefectItem
            {
                Feature = feature,
                IconPath = iconPath,
                ToolTip = toolTip,
                Count = initialCount
            });
        }

        public InspectionData InspectionData { get; } = new InspectionData();
        public double GoodPercentage { get; private set; }
        public double NoGoodPercentage { get; private set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
