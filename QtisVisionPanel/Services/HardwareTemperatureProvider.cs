using LibreHardwareMonitor.Hardware;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    internal sealed class HardwareTemperatureReading
    {
        public DiagnosticsTemperatureSensorType SensorType { get; set; }
        public string HardwareName { get; set; }
        public string SensorName { get; set; }
        public double TemperatureC { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(HardwareName)
            ? SensorName
            : HardwareName;

        public string Details => string.Format(
            "LibreHardwareMonitor | {0}",
            string.IsNullOrWhiteSpace(SensorName) ? "Temperature" : SensorName);
    }

    internal interface IHardwareTemperatureProvider : IDisposable
    {
        IReadOnlyList<HardwareTemperatureReading> Read(int minimumIntervalSeconds, out string status);
    }

    /// <summary>
    /// Owns the LibreHardwareMonitor lifecycle and exposes only representative
    /// temperatures. Results are cached so diagnostics refreshes cannot turn into
    /// repeated low-level hardware scans.
    /// </summary>
    internal sealed class LibreHardwareTemperatureProvider : IHardwareTemperatureProvider
    {
        private static readonly TimeSpan InitializationRetryDelay = TimeSpan.FromMinutes(5);
        private readonly object _sync = new object();

        private Computer _computer;
        private List<HardwareTemperatureReading> _cachedReadings = new List<HardwareTemperatureReading>();
        private DateTime _lastReadUtc = DateTime.MinValue;
        private DateTime _nextInitializationAttemptUtc = DateTime.MinValue;
        private string _lastError;
        private bool _disposed;

        public IReadOnlyList<HardwareTemperatureReading> Read(int minimumIntervalSeconds, out string status)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    status = "Provider disposed";
                    return Array.Empty<HardwareTemperatureReading>();
                }

                var nowUtc = DateTime.UtcNow;
                var pollingInterval = TimeSpan.FromSeconds(Math.Max(5, minimumIntervalSeconds));
                if (_lastReadUtc != DateTime.MinValue && nowUtc - _lastReadUtc < pollingInterval)
                {
                    status = string.IsNullOrWhiteSpace(_lastError) ? "Cached" : _lastError;
                    return _cachedReadings.ToArray();
                }

                if (!EnsureComputerOpen(nowUtc, out status))
                {
                    return Array.Empty<HardwareTemperatureReading>();
                }

                try
                {
                    var rawReadings = new List<HardwareTemperatureReading>();
                    foreach (var hardware in _computer.Hardware)
                    {
                        CollectReadings(hardware, rawReadings, 0);
                    }

                    _cachedReadings = SelectRepresentativeReadings(rawReadings);
                    _lastReadUtc = nowUtc;
                    _lastError = null;
                    status = _cachedReadings.Count > 0
                        ? string.Format("Active; sensors={0}", _cachedReadings.Count)
                        : "No temperature sensors exposed";
                    return _cachedReadings.ToArray();
                }
                catch (Exception ex)
                {
                    _lastError = ex.GetBaseException().Message;
                    status = _lastError;
                    CloseComputer();
                    _nextInitializationAttemptUtc = nowUtc.Add(InitializationRetryDelay);
                    return Array.Empty<HardwareTemperatureReading>();
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                CloseComputer();
                _cachedReadings.Clear();
            }
        }

        private bool EnsureComputerOpen(DateTime nowUtc, out string status)
        {
            if (_computer != null)
            {
                status = "Active";
                return true;
            }

            if (nowUtc < _nextInitializationAttemptUtc)
            {
                status = string.IsNullOrWhiteSpace(_lastError)
                    ? "Provider initialization retry pending"
                    : _lastError;
                return false;
            }

            try
            {
                var computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true,
                    IsGpuEnabled = false,
                    IsNetworkEnabled = false,
                    IsControllerEnabled = false,
                    IsBatteryEnabled = false,
                    IsPsuEnabled = false,
                    IsPowerMonitorEnabled = false
                };

                computer.Open();
                _computer = computer;
                _nextInitializationAttemptUtc = DateTime.MinValue;
                _lastError = null;
                status = "Active";
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.GetBaseException().Message;
                _nextInitializationAttemptUtc = nowUtc.Add(InitializationRetryDelay);
                status = _lastError;
                CloseComputer();
                return false;
            }
        }

        private static void CollectReadings(IHardware hardware,
            ICollection<HardwareTemperatureReading> readings, int depth)
        {
            if (hardware == null || depth > 8)
                return;

            hardware.Update();
            var sensorType = MapHardwareType(hardware.HardwareType.ToString());
            if (sensorType.HasValue)
            {
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                        continue;

                    var temperatureC = Convert.ToDouble(sensor.Value.Value);
                    if (temperatureC < -20 || temperatureC > 140)
                        continue;

                    readings.Add(new HardwareTemperatureReading
                    {
                        SensorType = sensorType.Value,
                        HardwareName = hardware.Name,
                        SensorName = sensor.Name,
                        TemperatureC = temperatureC
                    });
                }
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectReadings(subHardware, readings, depth + 1);
            }
        }

        private static DiagnosticsTemperatureSensorType? MapHardwareType(string hardwareType)
        {
            if (string.Equals(hardwareType, "Cpu", StringComparison.OrdinalIgnoreCase))
                return DiagnosticsTemperatureSensorType.Cpu;
            if (string.Equals(hardwareType, "Storage", StringComparison.OrdinalIgnoreCase))
                return DiagnosticsTemperatureSensorType.Disk;
            if (string.Equals(hardwareType, "Memory", StringComparison.OrdinalIgnoreCase))
                return DiagnosticsTemperatureSensorType.Memory;
            if (string.Equals(hardwareType, "Motherboard", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hardwareType, "SuperIO", StringComparison.OrdinalIgnoreCase))
                return DiagnosticsTemperatureSensorType.Motherboard;

            return null;
        }

        private static List<HardwareTemperatureReading> SelectRepresentativeReadings(
            IEnumerable<HardwareTemperatureReading> readings)
        {
            var all = readings.ToList();
            var selected = new List<HardwareTemperatureReading>();

            AddHottest(selected, all.Where(x => x.SensorType == DiagnosticsTemperatureSensorType.Cpu),
                PreferCpuSummarySensor);
            AddHottest(selected, all.Where(x => x.SensorType == DiagnosticsTemperatureSensorType.Memory));
            AddHottest(selected, all.Where(x => x.SensorType == DiagnosticsTemperatureSensorType.Motherboard));

            foreach (var diskGroup in all
                .Where(x => x.SensorType == DiagnosticsTemperatureSensorType.Disk)
                .GroupBy(x => x.HardwareName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                AddHottest(selected, diskGroup);
            }

            return selected;
        }

        private static void AddHottest(ICollection<HardwareTemperatureReading> target,
            IEnumerable<HardwareTemperatureReading> candidates,
            Func<HardwareTemperatureReading, int> preference = null)
        {
            var selected = preference == null
                ? candidates.OrderByDescending(x => x.TemperatureC).FirstOrDefault()
                : candidates.OrderByDescending(preference).ThenByDescending(x => x.TemperatureC).FirstOrDefault();

            if (selected != null)
            {
                target.Add(selected);
            }
        }

        private static int PreferCpuSummarySensor(HardwareTemperatureReading reading)
        {
            var name = (reading.SensorName ?? string.Empty).ToLowerInvariant();
            if (name.Contains("package")) return 100;
            if (name.Contains("core max")) return 90;
            if (name.Contains("average")) return 80;
            if (name.Contains("tctl") || name.Contains("tdie")) return 70;
            return 10;
        }

        private void CloseComputer()
        {
            var computer = _computer;
            _computer = null;
            if (computer == null)
                return;

            try
            {
                computer.Close();
            }
            catch
            {
                // Diagnostics must never block application shutdown.
            }
        }
    }
}
