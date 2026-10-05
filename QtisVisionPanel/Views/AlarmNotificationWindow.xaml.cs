using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace QtisVisionPanel.Views
{
    public partial class AlarmNotificationWindow : Window
    {
        private readonly AlarmTriggeredEventArgs _event;
        private readonly IIODeviceManager _ioManager;
        private readonly int _outputChannel = -1;
        private readonly bool _needsManualReset;

        public AlarmNotificationWindow(AlarmTriggeredEventArgs e, IIODeviceManager ioManager)
        {
            InitializeComponent();
            _event = e ?? throw new ArgumentNullException(nameof(e));
            _ioManager = ioManager;

            // Parse output channel index from e.g. "DO10" (PCIE-1756: DO00-DO31)
            if (e.Alarm != null &&
                !string.IsNullOrEmpty(e.Alarm.OutputChannelId) &&
                e.Alarm.OutputChannelId.StartsWith("DO", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(e.Alarm.OutputChannelId.Substring(2), out int ch) &&
                Pcie1756ChannelMap.IsValidDoChannel(ch))
            {
                _outputChannel = ch;
            }

            _needsManualReset = e.Alarm != null && e.Alarm.PulseDurationMs == 0 && _outputChannel >= 0 && _ioManager != null;

            PopulateUI();
        }

        private void PopulateUI()
        {
            var alarm = _event.Alarm;

            AlarmNameText.Text = alarm?.Name ?? "Allarme";
            TriggerTimeText.Text = _event.TriggerTime.ToString("HH:mm:ss");
            MessageText.Text = _event.Message ?? string.Empty;

            if (alarm != null)
            {
                ChannelText.Text = string.IsNullOrEmpty(alarm.OutputChannelId) ? "—" : alarm.OutputChannelId;

                if (alarm.PulseDurationMs > 0)
                    DurationText.Text = $"{alarm.PulseDurationMs} ms  (auto-reset)";
                else
                    DurationText.Text = "Manuale (ACKNOWLEDGE)";

                switch (alarm.AlarmType)
                {
                    case AlarmType.ConsecutiveEvents:
                        AlarmTypeBadge.Text = "Consecutivi";
                        break;
                    case AlarmType.ConsecutiveEventsAlt:
                        AlarmTypeBadge.Text = "Consecutivi Alt";
                        break;
                    case AlarmType.PercentageInBuffer:
                        AlarmTypeBadge.Text = "% Buffer";
                        break;
                    default:
                        AlarmTypeBadge.Text = alarm.AlarmType.ToString();
                        break;
                }

                CountText.Text = $"#{alarm.TriggerCount}";
            }
            else
            {
                ChannelText.Text = "—";
                DurationText.Text = "—";
                AlarmTypeBadge.Text = "—";
                CountText.Text = "#0";
            }

            ResetNotePanel.Visibility = _needsManualReset ? Visibility.Visible : Visibility.Collapsed;

            PopulateDefects(_event.DefectCounts);
        }

        private static readonly Dictionary<DefectType, string> _defectLabels = new Dictionary<DefectType, string>
        {
            { DefectType.Logo,              "Logo" },
            { DefectType.PrintCentering,    "Centratura stampa" },
            { DefectType.OpenFlaps,         "Alette aperte" },
            { DefectType.SurfaceCheck,      "Controllo superficie" },
            { DefectType.Height,            "Altezza" },
            { DefectType.SideSealing,       "Sigillatura laterale" },
            { DefectType.ShapeTop,          "Forma top" },
            { DefectType.ShapeSide,         "Forma laterale" },
            { DefectType.FrontTraceability, "Tracciabilità fronte" },
            { DefectType.ThreeDHeight,      "Altezza 3D" },
            { DefectType.ThreeDWidth,       "Larghezza 3D" },
            { DefectType.ThreeDLength,      "Lunghezza 3D" },
            { DefectType.BottomSealing,     "Saldatura inferiore" },
            { DefectType.TrappedPaper,      "Carta intrappolata" },
            { DefectType.AIClassification,  "Classificazione AI" },
        };

        private void PopulateDefects(Dictionary<DefectType, int> defectCounts)
        {
            if (defectCounts == null || defectCounts.Count == 0)
            {
                DefectsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var sb = new StringBuilder();
            foreach (var kv in defectCounts)
            {
                string label = _defectLabels.TryGetValue(kv.Key, out string l) ? l : kv.Key.ToString();
                sb.AppendLine($"{label}  —  {kv.Value} rilevazioni");
            }

            DefectsText.Text = sb.ToString().TrimEnd();
            DefectsPanel.Visibility = Visibility.Visible;
        }

        private async void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
        {
            AcknowledgeButton.IsEnabled = false;

            if (_outputChannel >= 0 && _ioManager != null)
            {
                try
                {
                    await _ioManager.WriteOutputAsync(_outputChannel, false);
                    ServiceLocator.OutputWatchdog?.Track(_outputChannel, expectedHigh: false);
                }
                catch (Exception ex)
                {
                    MainWindow.logger?.Warn(ex, "ALARM_ACK_OUTPUT_RESET_FAILED|alarm={0}|channel=DO{1}",
                        _event.Alarm?.Name,
                        _outputChannel);
                }
            }

            var alarmName = _event.Alarm?.Name ?? "unknown";
            var channel   = _event.Alarm?.OutputChannelId ?? "-";
            ServiceLocator.AlarmService?.AcknowledgeAlarm(alarmName, UserSession.CurrentUser ?? "unknown");
            ServiceLocator.AuditLogService?.LogAsync(
                "ALARM_ACKNOWLEDGE",
                UserSession.CurrentUser ?? "unknown",
                $"alarm={alarmName}|channel={channel}|trigger={_event.TriggerTime:HH:mm:ss}")
                .SafeFireAndForget();

            Close();
        }
    }
}
