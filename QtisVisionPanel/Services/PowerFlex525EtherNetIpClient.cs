using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Safe EtherNet/IP boundary for the PowerFlex 525.
    /// The current implementation verifies that the drive endpoint is reachable on
    /// the EtherNet/IP port and intentionally keeps CIP parameter read/write behind
    /// this interface, so machine runtime never depends on an unfinished driver.
    /// </summary>
    public class PowerFlex525EtherNetIpClient : IPowerFlex525Client
    {
        private const int EtherNetIpPort = 44818;
        private const ushort EncapsulationRegisterSession = 0x0065;
        private const ushort EncapsulationUnregisterSession = 0x0066;
        private const ushort EncapsulationSendRrData = 0x006F;
        private const byte CipGetAttributeSingle = 0x0E;
        private const byte CipSetAttributeSingle = 0x10;
        private const byte CipParameterObjectClass = 0x0F;
        private const byte CipParameterValueAttribute = 0x01;
        private const int FaultClearParameterNumber = 551;
        private static readonly TimeSpan RepeatedStatusLogInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan PingRetryDelayAfterStartupFailure = TimeSpan.FromMinutes(10);
        private readonly string _ipAddress;
        private readonly int _timeoutMs;
        private bool? _lastReachableLogged;
        private DateTime _lastEndpointLogAt = DateTime.MinValue;
        private DateTime _lastCipAdapterWarningAt = DateTime.MinValue;
        private DateTime _lastCipMonitorReadLogAt = DateTime.MinValue;
        private int _lastCipMonitorAvailableCount = -1;
        private int _pingFailureCount;
        private DateTime _nextPingAttemptAt = DateTime.MinValue;
        private bool _endpointDisabledAfterRetry;
        private bool _ipNotConfiguredLogged;

        public PowerFlex525EtherNetIpClient(string ipAddress, int timeoutMs)
        {
            _ipAddress = ipAddress;
            _timeoutMs = timeoutMs <= 0 ? 1000 : timeoutMs;
        }

        public async Task<PowerFlex525DriveSnapshot> ReadDriveSnapshotAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_ipAddress))
            {
                LogIpNotConfiguredOnce();
                return new PowerFlex525DriveSnapshot
                {
                    IsAvailable = false,
                    StatusMessage = "PowerFlex IP non configurato"
                };
            }

            string skippedMessage;
            if (TrySkipEndpointAttempt(out skippedMessage))
            {
                return new PowerFlex525DriveSnapshot
                {
                    IsAvailable = false,
                    IsRunning = false,
                    StatusMessage = skippedMessage
                };
            }

            bool pingReachable = await IsPingReachableAsync(cancellationToken).ConfigureAwait(false);
            if (!pingReachable)
            {
                var failureMessage = RegisterPingFailure();
                return new PowerFlex525DriveSnapshot
                {
                    IsAvailable = false,
                    IsRunning = false,
                    StatusMessage = failureMessage
                };
            }

            RegisterPingSuccess();
            bool reachable = await IsEtherNetIpEndpointReachableAsync(cancellationToken).ConfigureAwait(false);
            LogEndpointState(reachable, reachable ? "Ping OK" : "Ping OK, TCP non raggiungibile");
            return new PowerFlex525DriveSnapshot
            {
                IsAvailable = reachable,
                IsRunning = false,
                StatusMessage = reachable
                    ? $"Ping OK. EtherNet/IP TCP {EtherNetIpPort} OK. Adapter CIP parametri da collegare."
                    : $"Ping OK. EtherNet/IP TCP {EtherNetIpPort} non raggiungibile."
            };
        }

        public async Task<IReadOnlyList<PowerFlex525ParameterValue>> ReadParametersAsync(IEnumerable<int> parameterNumbers, CancellationToken cancellationToken)
        {
            var requestedParameters = (parameterNumbers ?? Enumerable.Empty<int>()).ToList();
            string skippedMessage;
            if (TrySkipEndpointAttempt(out skippedMessage))
            {
                return BuildUnavailableParameterValues(requestedParameters, skippedMessage);
            }

            bool pingReachable = await IsPingReachableAsync(cancellationToken).ConfigureAwait(false);
            if (!pingReachable)
            {
                var failureMessage = RegisterPingFailure();
                return BuildUnavailableParameterValues(requestedParameters, failureMessage);
            }

            RegisterPingSuccess();
            bool reachable = await IsEtherNetIpEndpointReachableAsync(cancellationToken).ConfigureAwait(false);
            LogEndpointState(reachable, reachable ? "Ping OK" : "Ping OK, TCP non raggiungibile");
            if (!reachable)
            {
                return BuildUnavailableParameterValues(requestedParameters, $"TCP {EtherNetIpPort} non raggiungibile");
            }

            var results = new List<PowerFlex525ParameterValue>();
            foreach (var number in requestedParameters)
            {
                var value = await TryReadParameterAsync(number, cancellationToken).ConfigureAwait(false);
                results.Add(value);
            }

            bool anyAvailable = results.Any(r => r.IsAvailable);
            if (reachable)
            {
                if (anyAvailable)
                {
                    LogCipMonitorReadCompleted(results.Count(r => r.IsAvailable), results.Count);
                }
                else
                {
                    LogCipAdapterMissing(requestedParameters.Count);
                }
            }

            return results;
        }

        public Task<bool> WriteParameterAsync(int parameterNumber, double value, CancellationToken cancellationToken)
        {
            // Rockwell PowerFlex 525 supports explicit CIP parameter writes, but
            // enabling writes without a validated adapter is not safe on a machine.
            MainWindow.logger?.Warn($"PowerFlex525 write blocked: parameter {parameterNumber}, value {value}. CIP adapter not connected.");
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<PowerFlex525ParameterValue>> ReadModifiedParametersAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<PowerFlex525ParameterValue> empty = new List<PowerFlex525ParameterValue>();
            return Task.FromResult(empty);
        }

        public async Task<bool> ResetFaultAsync(CancellationToken cancellationToken)
        {
            return await TryWriteFaultClearCommandAsync(1, "Reset Fault", cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> ClearFaultHistoryAsync(CancellationToken cancellationToken)
        {
            return await TryWriteFaultClearCommandAsync(2, "Clear Buffer", cancellationToken).ConfigureAwait(false);
        }

        private bool TrySkipEndpointAttempt(out string message)
        {
            message = null;

            if (_endpointDisabledAfterRetry)
            {
                message = "IP inverter non configurato o non raggiungibile. Ping disabilitato fino al riavvio HMI.";
                return true;
            }

            if (_pingFailureCount == 1 && DateTime.Now < _nextPingAttemptAt)
            {
                message = $"Ping inverter non risponde. Secondo tentativo previsto alle {_nextPingAttemptAt:HH:mm}.";
                return true;
            }

            return false;
        }

        private string RegisterPingFailure()
        {
            _pingFailureCount++;
            if (_pingFailureCount <= 1)
            {
                _nextPingAttemptAt = DateTime.Now.Add(PingRetryDelayAfterStartupFailure);
                LogEndpointState(false, $"Ping non risponde. Retry tra {PingRetryDelayAfterStartupFailure.TotalMinutes:0} minuti");
                MainWindow.logger?.Warn($"PowerFlex525 ping failed for {_ipAddress}. Next retry scheduled at {_nextPingAttemptAt:HH:mm:ss}.");
                return $"Ping inverter non risponde. Secondo tentativo previsto alle {_nextPingAttemptAt:HH:mm}.";
            }

            _endpointDisabledAfterRetry = true;
            LogEndpointState(false, "Ping non risponde dopo secondo tentativo");
            MainWindow.logger?.Warn($"PowerFlex525 endpoint disabled after {_pingFailureCount} ping attempts. DriveIp={_ipAddress}.");
            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                LogLevel.Warn,
                "POWERFLEX525_IP_UNREACHABLE_DISABLED",
                "PowerFlex525",
                "PowerFlex 525 IP not reachable after retry",
                nameof(PowerFlex525EtherNetIpClient),
                $"DriveIp={_ipAddress}; Attempts={_pingFailureCount}; RetryDelayMinutes={PingRetryDelayAfterStartupFailure.TotalMinutes:0}");
            return "IP inverter non configurato o non raggiungibile. Ping disabilitato fino al riavvio HMI.";
        }

        private void RegisterPingSuccess()
        {
            if (_pingFailureCount > 0)
            {
                MainWindow.logger?.Info($"PowerFlex525 ping recovered for {_ipAddress}.");
            }

            _pingFailureCount = 0;
            _nextPingAttemptAt = DateTime.MinValue;
            _endpointDisabledAfterRetry = false;
        }

        private void LogIpNotConfiguredOnce()
        {
            if (_ipNotConfiguredLogged)
            {
                return;
            }

            _ipNotConfiguredLogged = true;
            MainWindow.logger?.Warn("PowerFlex525 connection skipped: IP address is not configured in Config.xml.");
            ServiceLocator.ApplicationEventLogger?.LogOperationalEvent(
                LogLevel.Warn,
                "POWERFLEX525_IP_NOT_CONFIGURED",
                "PowerFlex525",
                "PowerFlex 525 IP address is not configured",
                nameof(PowerFlex525EtherNetIpClient),
                "Config.xml PowerFlex525.IpAddress is empty");
        }

        private static IReadOnlyList<PowerFlex525ParameterValue> BuildUnavailableParameterValues(
            IEnumerable<int> requestedParameters,
            string error)
        {
            return (requestedParameters ?? Enumerable.Empty<int>())
                .Select(number => new PowerFlex525ParameterValue
                {
                    Number = number,
                    IsAvailable = false,
                    Error = error
                })
                .ToList();
        }

        private async Task<bool> IsPingReachableAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(_ipAddress, _timeoutMs).ConfigureAwait(false);
                    bool ok = reply?.Status == IPStatus.Success;
                    if (ok)
                    {
                        MainWindow.logger?.Debug($"PowerFlex525 ping OK: {_ipAddress} time={reply.RoundtripTime} ms.");
                    }
                    else
                    {
                        MainWindow.logger?.Warn($"PowerFlex525 ping failed: {_ipAddress} status={reply?.Status.ToString() ?? "Unknown"}.");
                    }

                    return ok;
                }
            }
            catch (PingException ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 ping exception on {_ipAddress}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 ping check failed on {_ipAddress}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> IsEtherNetIpEndpointReachableAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectResult = client.BeginConnect(_ipAddress, EtherNetIpPort, null, null);
                    var completed = await Task.Run(() => connectResult.AsyncWaitHandle.WaitOne(_timeoutMs), cancellationToken).ConfigureAwait(false);
                    if (!completed)
                    {
                        MainWindow.logger?.Debug($"PowerFlex525 endpoint timeout: {_ipAddress}:{EtherNetIpPort} after {_timeoutMs} ms.");
                        return false;
                    }

                    try
                    {
                        client.EndConnect(connectResult);
                    }
                    catch (Exception ex)
                    {
                        MainWindow.logger?.Debug($"PowerFlex525 endpoint connect rejected on {_ipAddress}:{EtherNetIpPort}: {ex.Message}");
                        return false;
                    }

                    return client.Connected;
                }
            }
            catch (OperationCanceledException)
            {
                MainWindow.logger?.Debug($"PowerFlex525 endpoint check cancelled for {_ipAddress}:{EtherNetIpPort}.");
                return false;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Debug($"PowerFlex525 endpoint check failed on {_ipAddress}:{EtherNetIpPort}: {ex.Message}");
                return false;
            }
        }

        private void LogEndpointState(bool reachable, string context)
        {
            var now = DateTime.Now;
            if (_lastReachableLogged.HasValue &&
                _lastReachableLogged.Value == reachable &&
                (now - _lastEndpointLogAt) < RepeatedStatusLogInterval)
            {
                return;
            }

            _lastReachableLogged = reachable;
            _lastEndpointLogAt = now;

            if (reachable)
            {
                MainWindow.logger?.Info($"PowerFlex525 endpoint reachable: {_ipAddress}:{EtherNetIpPort}. Context: {context}.");
            }
            else
            {
                MainWindow.logger?.Warn($"PowerFlex525 endpoint not reachable: {_ipAddress}:{EtherNetIpPort}. Context: {context}. Timeout {_timeoutMs} ms.");
            }
        }

        private void LogCipAdapterMissing(int parameterCount)
        {
            var now = DateTime.Now;
            if ((now - _lastCipAdapterWarningAt) < RepeatedStatusLogInterval)
            {
                return;
            }

            _lastCipAdapterWarningAt = now;
            MainWindow.logger?.Warn(
                $"PowerFlex525 parameter read unavailable: TCP endpoint is reachable, but CIP parameter adapter is not connected. Requested parameters: {parameterCount}.");
        }

        private void LogCipMonitorReadCompleted(int available, int total)
        {
            var now = DateTime.Now;
            bool countChanged = available != _lastCipMonitorAvailableCount;
            if (!countChanged && (now - _lastCipMonitorReadLogAt) < RepeatedStatusLogInterval)
            {
                return;
            }

            _lastCipMonitorAvailableCount = available;
            _lastCipMonitorReadLogAt = now;
            MainWindow.logger?.Info($"PowerFlex525 CIP monitor read completed. Available values: {available}/{total}.");
        }

        private async Task<PowerFlex525ParameterValue> TryReadParameterAsync(int parameterNumber, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() => ReadParameterCore(parameterNumber), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new PowerFlex525ParameterValue
                {
                    Number = parameterNumber,
                    IsAvailable = false,
                    Error = "Lettura CIP annullata"
                };
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 CIP read failed for parameter {parameterNumber}: {ex.Message}");
                return new PowerFlex525ParameterValue
                {
                    Number = parameterNumber,
                    IsAvailable = false,
                    Error = ex.Message
                };
            }
        }

        private PowerFlex525ParameterValue ReadParameterCore(int parameterNumber)
        {
            using (var client = new TcpClient())
            {
                client.ReceiveTimeout = _timeoutMs;
                client.SendTimeout = _timeoutMs;
                client.Connect(_ipAddress, EtherNetIpPort);

                using (var stream = client.GetStream())
                {
                    uint sessionHandle = RegisterSession(stream);
                    try
                    {
                        byte[] cipRequest = BuildGetAttributeSingleRequest(parameterNumber);
                        MainWindow.logger?.Debug(
                            $"PowerFlex525 CIP request parameter {parameterNumber}: service=0x{CipGetAttributeSingle:X2}, class=0x{CipParameterObjectClass:X2}, instance={parameterNumber}, attribute=0x{CipParameterValueAttribute:X2}, raw={ToHex(cipRequest)}");

                        byte[] response = SendRrData(stream, sessionHandle, cipRequest);
                        MainWindow.logger?.Debug($"PowerFlex525 CIP response parameter {parameterNumber}: raw={ToHex(response)}");

                        byte[] data = ExtractCipData(response);
                        double scaledValue = ScaleMonitorValue(parameterNumber, DecodeNumericValue(data));
                        return new PowerFlex525ParameterValue
                        {
                            Number = parameterNumber,
                            Value = scaledValue,
                            RawValue = ToHex(data),
                            IsAvailable = true
                        };
                    }
                    finally
                    {
                        UnregisterSession(stream, sessionHandle);
                    }
                }
            }
        }

        private static byte[] BuildGetAttributeSingleRequest(int parameterNumber)
        {
            var path = BuildParameterPath(parameterNumber);
            var request = new List<byte>
            {
                CipGetAttributeSingle,
                (byte)(path.Count / 2)
            };
            request.AddRange(path);
            return request.ToArray();
        }

        private static byte[] BuildSetAttributeSingleRequest(int parameterNumber, ushort value)
        {
            var path = BuildParameterPath(parameterNumber);
            var request = new List<byte>
            {
                CipSetAttributeSingle,
                (byte)(path.Count / 2)
            };
            request.AddRange(path);
            request.Add((byte)(value & 0xFF));
            request.Add((byte)((value >> 8) & 0xFF));
            return request.ToArray();
        }

        private static List<byte> BuildParameterPath(int parameterNumber)
        {
            var path = new List<byte>
            {
                0x20,
                CipParameterObjectClass
            };

            if (parameterNumber <= byte.MaxValue)
            {
                path.Add(0x24);
                path.Add((byte)parameterNumber);
            }
            else
            {
                path.Add(0x25);
                path.Add(0x00);
                path.Add((byte)(parameterNumber & 0xFF));
                path.Add((byte)((parameterNumber >> 8) & 0xFF));
            }

            path.Add(0x30);
            path.Add(CipParameterValueAttribute);
            return path;
        }

        private uint RegisterSession(NetworkStream stream)
        {
            byte[] payload =
            {
                0x01, 0x00,
                0x00, 0x00
            };

            byte[] packet = BuildEncapsulationPacket(EncapsulationRegisterSession, 0, payload);
            MainWindow.logger?.Debug($"PowerFlex525 EIP RegisterSession request: raw={ToHex(packet)}");
            stream.Write(packet, 0, packet.Length);
            byte[] response = ReadEncapsulationResponse(stream);
            MainWindow.logger?.Debug($"PowerFlex525 EIP RegisterSession response: raw={ToHex(response)}");

            ushort command = ReadUInt16(response, 0);
            uint status = ReadUInt32(response, 8);
            if (command != EncapsulationRegisterSession || status != 0)
            {
                throw new InvalidOperationException($"RegisterSession failed. command=0x{command:X4}, status=0x{status:X8}");
            }

            return ReadUInt32(response, 4);
        }

        private static void UnregisterSession(NetworkStream stream, uint sessionHandle)
        {
            try
            {
                byte[] packet = BuildEncapsulationPacket(EncapsulationUnregisterSession, sessionHandle, new byte[0]);
                stream.Write(packet, 0, packet.Length);
            }
            catch
            {
                // Session cleanup must never hide the read result.
            }
        }

        private byte[] SendRrData(NetworkStream stream, uint sessionHandle, byte[] cipRequest)
        {
            var payload = new List<byte>();
            AddUInt32(payload, 0); // Interface handle: CIP
            AddUInt16(payload, (ushort)Math.Max(1, _timeoutMs / 1000));
            AddUInt16(payload, 2); // Item count
            AddUInt16(payload, 0x0000); // Null address item
            AddUInt16(payload, 0);
            AddUInt16(payload, 0x00B2); // Unconnected data item
            AddUInt16(payload, (ushort)cipRequest.Length);
            payload.AddRange(cipRequest);

            byte[] packet = BuildEncapsulationPacket(EncapsulationSendRrData, sessionHandle, payload.ToArray());
            MainWindow.logger?.Debug($"PowerFlex525 EIP SendRRData request: raw={ToHex(packet)}");
            stream.Write(packet, 0, packet.Length);

            byte[] response = ReadEncapsulationResponse(stream);
            ushort command = ReadUInt16(response, 0);
            uint status = ReadUInt32(response, 8);
            if (command != EncapsulationSendRrData || status != 0)
            {
                throw new InvalidOperationException($"SendRRData failed. command=0x{command:X4}, status=0x{status:X8}");
            }

            return response;
        }

        private static byte[] ExtractCipData(byte[] encapsulationResponse)
        {
            ushort payloadLength = ReadUInt16(encapsulationResponse, 2);
            int payloadStart = 24;
            int payloadEnd = payloadStart + payloadLength;
            if (encapsulationResponse.Length < payloadEnd || payloadLength < 10)
            {
                throw new InvalidOperationException("Invalid EtherNet/IP response length.");
            }

            int offset = payloadStart;
            offset += 4; // Interface handle
            offset += 2; // Timeout
            ushort itemCount = ReadUInt16(encapsulationResponse, offset);
            offset += 2;

            for (int i = 0; i < itemCount; i++)
            {
                ushort itemType = ReadUInt16(encapsulationResponse, offset);
                ushort itemLength = ReadUInt16(encapsulationResponse, offset + 2);
                offset += 4;
                if (itemType == 0x00B2)
                {
                    byte[] cip = new byte[itemLength];
                    Array.Copy(encapsulationResponse, offset, cip, 0, itemLength);
                    return ExtractCipServiceData(cip);
                }

                offset += itemLength;
            }

            throw new InvalidOperationException("No unconnected CIP data item found.");
        }

        private static byte[] ExtractCipServiceData(byte[] cip)
        {
            if (cip.Length < 4)
            {
                throw new InvalidOperationException("Invalid CIP response length.");
            }

            byte responseService = cip[0];
            byte generalStatus = cip[2];
            byte additionalStatusWords = cip[3];
            int dataOffset = 4 + additionalStatusWords * 2;

            if (responseService != (CipGetAttributeSingle | 0x80))
            {
                throw new InvalidOperationException($"Unexpected CIP service response 0x{responseService:X2}.");
            }

            if (generalStatus != 0)
            {
                throw new InvalidOperationException($"CIP general status 0x{generalStatus:X2}.");
            }

            if (cip.Length < dataOffset)
            {
                throw new InvalidOperationException("Invalid CIP additional status length.");
            }

            byte[] data = new byte[cip.Length - dataOffset];
            Array.Copy(cip, dataOffset, data, 0, data.Length);
            return data;
        }

        private static double DecodeNumericValue(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new InvalidOperationException("Empty CIP value payload.");
            }

            if (data.Length >= 4)
            {
                return BitConverter.ToInt32(data, 0);
            }

            if (data.Length >= 2)
            {
                return BitConverter.ToInt16(data, 0);
            }

            return data[0];
        }

        private static double ScaleMonitorValue(int parameterNumber, double rawValue)
        {
            switch (parameterNumber)
            {
                case 1:  // b001 Output Frequency, display 0.01 Hz
                case 3:  // b003 Output Current, display 0.01 A
                case 17: // b017 Output Power, commonly displayed with decimals
                case 37: // P037 Motor NP Power
                    return rawValue / 100.0;
                case 4:  // b004 Output Voltage, display 0.1 V
                case 33: // P033 Motor OL Current
                case 34: // P034 Motor NP FLA
                case 41: // P041 Accel Time 1
                case 42: // P042 Decel Time 1
                    return rawValue / 10.0;
                case 43: // P043 Minimum Freq
                case 44: // P044 Maximum Freq
                    return rawValue / 100.0;
                case 5:  // b005 DC Bus Voltage, display 1 V
                case 6:  // b006 Drive Status
                case 7:  // b007 Fault 1 Code
                case 8:  // b008 Fault 2 Code
                case 9:  // b009 Fault 3 Code
                case 31: // P031 Motor NP Volts
                case 32: // P032 Motor NP Hertz
                case 35: // P035 Motor NP Poles
                case 36: // P036 Motor NP RPM
                    return rawValue;
                default:
                    return rawValue;
            }
        }

        private async Task<bool> TryWriteFaultClearCommandAsync(ushort commandValue, string operationName, CancellationToken cancellationToken)
        {
            bool reachable = await IsEtherNetIpEndpointReachableAsync(cancellationToken).ConfigureAwait(false);
            LogEndpointState(reachable, reachable ? "Ping OK" : "Ping OK, TCP non raggiungibile");
            if (!reachable)
            {
                MainWindow.logger?.Warn($"PowerFlex525 {operationName} skipped: endpoint {_ipAddress}:{EtherNetIpPort} not reachable.");
                return false;
            }

            try
            {
                return await Task.Run(() => WriteFaultClearCommandCore(commandValue, operationName), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                MainWindow.logger?.Warn($"PowerFlex525 {operationName} cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"PowerFlex525 {operationName} failed: {ex.Message}");
                return false;
            }
        }

        private bool WriteFaultClearCommandCore(ushort commandValue, string operationName)
        {
            using (var client = new TcpClient())
            {
                client.ReceiveTimeout = _timeoutMs;
                client.SendTimeout = _timeoutMs;
                client.Connect(_ipAddress, EtherNetIpPort);

                using (var stream = client.GetStream())
                {
                    uint sessionHandle = RegisterSession(stream);
                    try
                    {
                        byte[] cipRequest = BuildSetAttributeSingleRequest(FaultClearParameterNumber, commandValue);
                        MainWindow.logger?.Debug(
                            $"PowerFlex525 CIP write request A551 [{operationName}]: service=0x{CipSetAttributeSingle:X2}, class=0x{CipParameterObjectClass:X2}, instance={FaultClearParameterNumber}, attribute=0x{CipParameterValueAttribute:X2}, value={commandValue}, raw={ToHex(cipRequest)}");

                        byte[] response = SendRrData(stream, sessionHandle, cipRequest);
                        MainWindow.logger?.Debug($"PowerFlex525 CIP write response A551 [{operationName}]: raw={ToHex(response)}");
                        ExtractCipData(response);
                        return true;
                    }
                    finally
                    {
                        UnregisterSession(stream, sessionHandle);
                    }
                }
            }
        }

        private static byte[] ReadEncapsulationResponse(NetworkStream stream)
        {
            byte[] header = ReadExact(stream, 24);
            ushort length = ReadUInt16(header, 2);
            byte[] payload = length > 0 ? ReadExact(stream, length) : new byte[0];
            byte[] response = new byte[header.Length + payload.Length];
            Array.Copy(header, 0, response, 0, header.Length);
            Array.Copy(payload, 0, response, header.Length, payload.Length);
            return response;
        }

        private static byte[] ReadExact(NetworkStream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    throw new InvalidOperationException("Socket closed while reading EtherNet/IP response.");
                }

                offset += read;
            }

            return buffer;
        }

        private static byte[] BuildEncapsulationPacket(ushort command, uint sessionHandle, byte[] payload)
        {
            var packet = new List<byte>();
            AddUInt16(packet, command);
            AddUInt16(packet, (ushort)(payload?.Length ?? 0));
            AddUInt32(packet, sessionHandle);
            AddUInt32(packet, 0); // Status
            AddUInt64(packet, 0); // Sender context
            AddUInt32(packet, 0); // Options
            if (payload != null)
            {
                packet.AddRange(payload);
            }

            return packet.ToArray();
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return BitConverter.ToUInt16(buffer, offset);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return BitConverter.ToUInt32(buffer, offset);
        }

        private static void AddUInt16(List<byte> buffer, ushort value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        private static void AddUInt32(List<byte> buffer, uint value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        private static void AddUInt64(List<byte> buffer, ulong value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        private static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(data.Length * 3);
            foreach (byte b in data)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
