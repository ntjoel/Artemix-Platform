using QtisVisionPanel.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    public class AlarmService
    {
        private readonly List<AlarmInfo> _alarms = new List<AlarmInfo>();
        private const int MAX_ALARMS = 100;

        public AlarmInfo GetLastAlarm()
        {
            return _alarms.OrderByDescending(a => a.Timestamp).FirstOrDefault();
        }

        public int GetActiveAlarmCount()
        {
            return _alarms.Count(a => a.Level >= AlarmLevel.Warning);
        }

        public void AddAlarm(string message, AlarmLevel level)
        {
            var alarm = new AlarmInfo
            {
                Message = message,
                Level = level,
                Timestamp = DateTime.Now
            };

            _alarms.Add(alarm);

            // Mantieni solo gli ultimi MAX_ALARMS
            if (_alarms.Count > MAX_ALARMS)
            {
                _alarms.RemoveAt(0);
            }

            // Log dell'allarme
            MainWindow.logger?.Warn($"Alarm added: {message} (Level: {level})");
        }

        public void ClearAlarms()
        {
            _alarms.Clear();
        }

        public List<AlarmInfo> GetAllAlarms()
        {
            return new List<AlarmInfo>(_alarms);
        }
    }
}