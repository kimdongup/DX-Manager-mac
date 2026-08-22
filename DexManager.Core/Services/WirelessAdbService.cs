using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DexManager.Models;

namespace DexManager.Services
{
    public sealed class WirelessAdbService
    {
        private readonly AdbService _adbService;
        private readonly SettingsService _settingsService;
        private readonly AppSettings _settings;
        private readonly LogService _logService;
        private readonly object _reconnectSync = new object();
        private DateTime _lastReconnectAttemptUtc = DateTime.MinValue;
        private long _transitionGeneration;
        private string _selectedSerial = string.Empty;

        public WirelessAdbService(
            AdbService adbService,
            SettingsService settingsService,
            AppSettings settings,
            LogService logService)
        {
            _adbService = adbService ??
                throw new ArgumentNullException("adbService");
            _settingsService = settingsService ??
                throw new ArgumentNullException("settingsService");
            _settings = settings ??
                throw new ArgumentNullException("settings");
            _logService = logService ??
                throw new ArgumentNullException("logService");
        }

        public bool IsWirelessMode
        {
            get
            {
                var profiles =
                    _settings.DeviceWirelessConnectionProfiles;
                if (profiles != null && profiles.Count > 0)
                {
                    return profiles.Any(profile =>
                        profile != null &&
                        profile.Mode == AdbConnectionMode.Wireless);
                }
                var connection = _settings.Connection;
                return connection != null && connection.Mode ==
                    AdbConnectionMode.Wireless;
            }
        }

        public string SavedEndpoint
        {
            get
            {
                var connection = _settings.Connection;
                if (connection == null) return string.Empty;
                return BuildEndpoint(
                    connection.WirelessHost,
                    connection.WirelessPort);
            }
        }

        public string SelectedSerial
        {
            get
            {
                lock (_reconnectSync)
                    return _selectedSerial;
            }
        }

        public void InitializeTarget()
        {
            SynchronizeTargetWithSettings();
        }

        public void SynchronizeTargetWithSettings()
        {
            lock (_reconnectSync)
            {
                var connection = GetConnectionSnapshot();
                _transitionGeneration++;
                if (connection.Mode == AdbConnectionMode.Wireless)
                {
                    SetSelectedSerial(connection.Endpoint);
                    return;
                }

                var current = _selectedSerial;
                if (AdbService.IsTcpIpSerial(current) ||
                    AdbService.IsEmulatorSerial(current))
                {
                    SetSelectedSerial(string.Empty);
                }
            }
        }

        public AdbDeviceInfo SelectPreferredDevice(
            IList<AdbDeviceInfo> devices)
        {
            return SelectPreferredDeviceWithGeneration(devices).Device;
        }

        public WirelessDeviceSelection SelectPreferredDeviceWithGeneration(
            IList<AdbDeviceInfo> devices)
        {
            return SelectPreferredDeviceWithGeneration(
                devices,
                string.Empty);
        }

        public WirelessDeviceSelection SelectPreferredDeviceWithGeneration(
            IList<AdbDeviceInfo> devices,
            string targetWhenUnavailable)
        {
            lock (_reconnectSync)
            {
                var connection = GetConnectionSnapshot();
                var preferred = FindPreferredDeviceCore(
                    devices,
                    _selectedSerial,
                    connection);
                var unavailableTarget = string.IsNullOrWhiteSpace(
                    targetWhenUnavailable)
                    ? string.Empty
                    : targetWhenUnavailable.Trim();
                var target = preferred == null
                    ? (unavailableTarget.Length > 0
                        ? unavailableTarget
                        : connection.Mode == AdbConnectionMode.Wireless
                        ? connection.Endpoint
                        : string.Empty)
                    : preferred.Serial;
                SetSelectedSerial(target);
                return new WirelessDeviceSelection(
                    preferred,
                    _transitionGeneration);
            }
        }

        public bool IsTransitionGenerationCurrent(long generation)
        {
            lock (_reconnectSync)
            {
                return generation == _transitionGeneration;
            }
        }

        public AdbDeviceInfo FindPreferredDevice(
            IList<AdbDeviceInfo> devices,
            string currentSerial)
        {
            lock (_reconnectSync)
            {
                return FindPreferredDeviceCore(
                    devices,
                    currentSerial,
                    GetConnectionSnapshot());
            }
        }

        public bool TryReconnect(bool writeLog)
        {
            lock (_reconnectSync)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastReconnectAttemptUtc).TotalSeconds < 5)
                    return false;
                var connections = GetReconnectSnapshots();
                if (connections.Count == 0) return false;
                _lastReconnectAttemptUtc = now;

                var anyConnected = false;
                foreach (var connection in connections)
                {
                    var endpoint = connection.Endpoint;
                    if (string.IsNullOrWhiteSpace(endpoint)) continue;
                    if (IsConnected(endpoint))
                    {
                        anyConnected = true;
                        continue;
                    }

                    if (writeLog)
                        _logService.Info(LocalizationService.Format(
                            "Log.Wireless.ReconnectAttempt",
                            endpoint));
                    var result = _adbService.Connect(endpoint, writeLog);
                    var connected = result.IsSuccess &&
                        IsConnected(endpoint);
                    anyConnected = anyConnected || connected;
                    if (writeLog)
                    {
                        if (connected)
                            _logService.Info(LocalizationService.Format(
                                "Log.Wireless.ReconnectSucceeded",
                                endpoint));
                        else
                            _logService.Warning(LocalizationService.Format(
                                "Log.Wireless.ReconnectFailed",
                                GetResultMessage(result)));
                    }
                }
                return anyConnected;
            }
        }

        public DeviceWirelessConnectionProfile GetDeviceProfile(
            string deviceIdentity,
            bool seedFromLegacyConnection)
        {
            lock (_reconnectSync)
            {
                return _settings.GetOrCreateDeviceWirelessConnection(
                    deviceIdentity,
                    seedFromLegacyConnection);
            }
        }

        public WirelessConnectionResult ConnectForDevice(
            string deviceIdentity,
            string host,
            int port,
            bool autoReconnect)
        {
            var identity = NormalizeDeviceIdentity(deviceIdentity);
            lock (_reconnectSync)
            {
                var normalizedHost = NormalizeHost(host);
                var endpoint = BuildEndpoint(normalizedHost, port);
                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.DeviceConnectAttempt",
                    identity,
                    endpoint));

                _adbService.StartServer();
                var wasConnected = IsConnected(endpoint);
                var result = _adbService.Connect(endpoint, true);
                if (!result.IsSuccess || !WaitForConnection(endpoint, 3000))
                {
                    RollbackDeviceConnection(endpoint, wasConnected);
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Format(
                            "Wireless.ConnectFailed",
                            GetResultMessage(result)));
                }

                var connectedIdentity =
                    _adbService.GetDeviceIdentity(endpoint);
                if (!DeviceIdentityMatches(identity, connectedIdentity))
                {
                    RollbackDeviceConnection(endpoint, wasConnected);
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Format(
                            "Wireless.DeviceMismatch",
                            endpoint));
                }

                try
                {
                    _settingsService.UpdateAndSave(_settings, delegate(
                        AppSettings settings)
                    {
                        var profile = settings
                            .GetOrCreateDeviceWirelessConnection(
                                identity,
                                false);
                        profile.Mode = AdbConnectionMode.Wireless;
                        profile.WirelessHost = normalizedHost;
                        profile.WirelessPort = port;
                        profile.AutoReconnect = autoReconnect;
                    });
                }
                catch
                {
                    RollbackDeviceConnection(endpoint, wasConnected);
                    throw;
                }
                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.DeviceConnectSucceeded",
                    identity,
                    endpoint));
                return WirelessConnectionResult.Succeeded(
                    endpoint,
                    LocalizationService.Get("Wireless.Connected"));
            }
        }

        public WirelessConnectionResult EnableFromUsbForDevice(
            string deviceIdentity,
            string usbSerial,
            string host,
            int port,
            bool autoReconnect)
        {
            var identity = NormalizeDeviceIdentity(deviceIdentity);
            var serial = (usbSerial ?? string.Empty).Trim();
            if (serial.Length == 0)
            {
                return WirelessConnectionResult.Failed(
                    LocalizationService.Get("Wireless.SelectedDeviceNoUsb"));
            }

            lock (_reconnectSync)
            {
                var usbDevice = _adbService.GetDevices().FirstOrDefault(
                    device => device.IsAuthorized &&
                        string.Equals(
                            device.Serial,
                            serial,
                            StringComparison.OrdinalIgnoreCase) &&
                        !AdbService.IsTcpIpSerial(device.Serial) &&
                        !AdbService.IsEmulatorSerial(device.Serial));
                if (usbDevice == null)
                {
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Get(
                            "Wireless.SelectedDeviceNoUsb"));
                }

                var usbIdentity = _adbService.GetDeviceIdentity(serial);
                if (!DeviceIdentityMatches(identity, usbIdentity))
                {
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Format(
                            "Wireless.DeviceMismatch",
                            serial));
                }

                var detectedHost = DetectWifiAddress(serial);
                var normalizedHost = string.IsNullOrWhiteSpace(host)
                    ? detectedHost
                    : NormalizeHost(host);
                if (string.IsNullOrWhiteSpace(normalizedHost))
                {
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Get("Wireless.NoWifiIp"));
                }

                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.DeviceEnableFromUsb",
                    identity,
                    serial,
                    port));
                var tcpipResult = _adbService.EnableTcpIp(serial, port);
                if (!tcpipResult.IsSuccess)
                {
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Format(
                            "Wireless.TcpipFailed",
                            GetResultMessage(tcpipResult)));
                }

                Thread.Sleep(800);
                var connection = ConnectForDevice(
                    identity,
                    normalizedHost,
                    port,
                    autoReconnect);
                if (connection.Success ||
                    string.IsNullOrWhiteSpace(detectedHost) ||
                    string.Equals(
                        normalizedHost,
                        detectedHost,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return connection;
                }

                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.RetryDetectedAddress",
                    identity,
                    detectedHost));
                return ConnectForDevice(
                    identity,
                    detectedHost,
                    port,
                    autoReconnect);
            }
        }

        public WirelessConnectionResult DisconnectForDevice(
            string deviceIdentity)
        {
            var identity = NormalizeDeviceIdentity(deviceIdentity);
            lock (_reconnectSync)
            {
                var profile = _settings.FindDeviceWirelessConnection(
                    identity);
                var endpoint = profile == null
                    ? string.Empty
                    : BuildEndpoint(
                        profile.WirelessHost,
                        profile.WirelessPort);
                _settingsService.UpdateAndSave(_settings, delegate(
                    AppSettings settings)
                {
                    var saved = settings
                        .GetOrCreateDeviceWirelessConnection(
                            identity,
                            false);
                    saved.Mode = AdbConnectionMode.Usb;
                });
                ProcessResult disconnectResult = null;
                if (!string.IsNullOrWhiteSpace(endpoint))
                    disconnectResult = _adbService.Disconnect(endpoint);
                if (disconnectResult != null &&
                    !disconnectResult.IsSuccess)
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.Wireless.DisconnectCommandFailed",
                        GetResultMessage(disconnectResult)));
                }
                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.DeviceDisconnected",
                    identity,
                    endpoint));
                return WirelessConnectionResult.Succeeded(
                    string.Empty,
                    LocalizationService.Get("Wireless.Disconnected"));
            }
        }

        public WirelessConnectionResult Connect(
            string host,
            int port)
        {
            lock (_reconnectSync)
            {
                _transitionGeneration++;
                var normalizedHost = NormalizeHost(host);
                var endpoint = BuildEndpoint(normalizedHost, port);
                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.ConnectAttempt",
                    endpoint));

                _adbService.StartServer();
                var wasConnected = IsConnected(endpoint);
                var previousTarget = _selectedSerial;
                var result = _adbService.Connect(endpoint, true);
                if (!result.IsSuccess || !WaitForConnection(endpoint, 3000))
                {
                    RollbackConnection(
                        endpoint,
                        wasConnected,
                        previousTarget);
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Format(
                            "Wireless.ConnectFailed",
                            GetResultMessage(result)));
                }

                try
                {
                    _settingsService.UpdateAndSave(_settings, delegate(
                        AppSettings settings)
                    {
                        settings.Connection.Mode = AdbConnectionMode.Wireless;
                        settings.Connection.WirelessHost = normalizedHost;
                        settings.Connection.WirelessPort = port;
                    });
                }
                catch
                {
                    RollbackConnection(
                        endpoint,
                        wasConnected,
                        previousTarget);
                    throw;
                }
                SetSelectedSerial(endpoint);
                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.ConnectSucceeded",
                    endpoint));
                return WirelessConnectionResult.Succeeded(
                    endpoint,
                    LocalizationService.Get("Wireless.Connected"));
            }
        }

        public WirelessConnectionResult EnableFromUsb(
            string host,
            int port)
        {
            lock (_reconnectSync)
            {
                var devices = _adbService.GetDevices();
                var usbDevices = devices
                    .Where(device =>
                        device.IsAuthorized &&
                        !AdbService.IsTcpIpSerial(device.Serial) &&
                        !AdbService.IsEmulatorSerial(device.Serial))
                    .ToList();
                if (usbDevices.Count != 1)
                {
                    return WirelessConnectionResult.Failed(
                        usbDevices.Count == 0
                            ? LocalizationService.Get("Wireless.NoUsb")
                            : LocalizationService.Get(
                                "Wireless.MultipleUsb"));
                }

                var usbSerial = usbDevices[0].Serial;
                var normalizedHost = string.IsNullOrWhiteSpace(host)
                    ? DetectWifiAddress(usbSerial)
                    : NormalizeHost(host);
                if (string.IsNullOrWhiteSpace(normalizedHost))
                {
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Get("Wireless.NoWifiIp"));
                }

                _logService.Info(LocalizationService.Format(
                    "Log.Wireless.EnableFromUsb",
                    usbSerial,
                    port));
                var tcpipResult = _adbService.EnableTcpIp(
                    usbSerial,
                    port);
                if (!tcpipResult.IsSuccess)
                {
                    return WirelessConnectionResult.Failed(
                        LocalizationService.Format(
                            "Wireless.TcpipFailed",
                            GetResultMessage(tcpipResult)));
                }

                Thread.Sleep(800);
                return Connect(normalizedHost, port);
            }
        }

        public WirelessConnectionResult Pair(
            string host,
            int port,
            string pairingCode)
        {
            if (!Regex.IsMatch(
                pairingCode ?? string.Empty,
                @"^\d{6}$"))
            {
                return WirelessConnectionResult.Failed(
                    LocalizationService.Get(
                        "Wireless.InvalidPairCode"));
            }
            var endpoint = BuildEndpoint(
                NormalizeHost(host),
                port);
            var result = _adbService.Pair(endpoint, pairingCode);
            if (!result.IsSuccess ||
                !ContainsIgnoreCase(
                    (result.StandardOutput ?? string.Empty) +
                    "\n" +
                    (result.StandardError ?? string.Empty),
                    "successfully paired"))
            {
                return WirelessConnectionResult.Failed(
                    LocalizationService.Format(
                        "Wireless.PairFailed",
                        GetResultMessage(result)));
            }

            return WirelessConnectionResult.Succeeded(
                endpoint,
                LocalizationService.Get("Wireless.Paired"));
        }

        public WirelessConnectionResult Disconnect()
        {
            lock (_reconnectSync)
            {
                _transitionGeneration++;
                var connection = GetConnectionSnapshot();
                var endpoint = connection.Endpoint;
                _settingsService.UpdateAndSave(_settings, delegate(
                    AppSettings settings)
                {
                    settings.Connection.Mode = AdbConnectionMode.Usb;
                });
                ProcessResult disconnectResult = null;
                if (!string.IsNullOrWhiteSpace(endpoint))
                    disconnectResult = _adbService.Disconnect(endpoint);
                SetSelectedSerial(string.Empty);
                if (disconnectResult != null && !disconnectResult.IsSuccess)
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.Wireless.DisconnectCommandFailed",
                        GetResultMessage(disconnectResult)));
                }
                _logService.Info(LocalizationService.Get(
                    "Log.Wireless.Disconnected"));
                return WirelessConnectionResult.Succeeded(
                    string.Empty,
                    LocalizationService.Get("Wireless.Disconnected"));
            }
        }

        public void UseUsb()
        {
            lock (_reconnectSync)
            {
                _transitionGeneration++;
                _settingsService.UpdateAndSave(_settings, delegate(
                    AppSettings settings)
                {
                    settings.Connection.Mode = AdbConnectionMode.Usb;
                });
                SetSelectedSerial(string.Empty);
            }
        }

        private AdbDeviceInfo FindPreferredDeviceCore(
            IList<AdbDeviceInfo> devices,
            string currentSerial,
            ConnectionSnapshot connection)
        {
            var candidates = devices ?? new List<AdbDeviceInfo>();
            if (connection.Mode == AdbConnectionMode.Wireless)
            {
                return candidates.FirstOrDefault(
                    device => string.Equals(
                        device.Serial,
                        connection.Endpoint,
                        StringComparison.OrdinalIgnoreCase) &&
                        device.IsAuthorized);
            }

            var current = candidates.FirstOrDefault(device =>
                string.Equals(
                    device.Serial,
                    currentSerial,
                    StringComparison.OrdinalIgnoreCase) &&
                !AdbService.IsTcpIpSerial(device.Serial) &&
                !AdbService.IsEmulatorSerial(device.Serial) &&
                device.IsAuthorized);
            if (current != null) return current;

            return candidates.FirstOrDefault(
                    device => !AdbService.IsTcpIpSerial(device.Serial) &&
                        !AdbService.IsEmulatorSerial(device.Serial) &&
                        device.IsAuthorized) ??
                candidates.FirstOrDefault(
                    device => !AdbService.IsTcpIpSerial(device.Serial) &&
                        !AdbService.IsEmulatorSerial(device.Serial));
        }

        private void RollbackConnection(
            string endpoint,
            bool wasConnected,
            string previousTarget)
        {
            try
            {
                if (!wasConnected)
                {
                    var result = _adbService.Disconnect(endpoint);
                    if (!result.IsSuccess)
                    {
                        _logService.Warning(LocalizationService.Format(
                            "Log.Wireless.DisconnectCommandFailed",
                            GetResultMessage(result)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.Wireless.DisconnectCommandFailed",
                    ex.Message));
            }
            finally
            {
                SetSelectedSerial(previousTarget);
            }
        }

        private void RollbackDeviceConnection(
            string endpoint,
            bool wasConnected)
        {
            if (wasConnected || string.IsNullOrWhiteSpace(endpoint)) return;
            try
            {
                var result = _adbService.Disconnect(endpoint);
                if (!result.IsSuccess)
                {
                    _logService.Warning(LocalizationService.Format(
                        "Log.Wireless.DisconnectCommandFailed",
                        GetResultMessage(result)));
                }
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.Wireless.DisconnectCommandFailed",
                    ex.Message));
            }
        }

        private void SetSelectedSerial(string serial)
        {
            var normalized = string.IsNullOrWhiteSpace(serial)
                ? string.Empty
                : serial.Trim();
            if (string.Equals(
                _selectedSerial,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedSerial = normalized;
            _logService.Info(
                normalized.Length == 0
                    ? LocalizationService.Get("Log.Adb.TargetCleared")
                    : LocalizationService.Format(
                        "Log.Adb.TargetSelected",
                        normalized));
        }

        private ConnectionSnapshot GetConnectionSnapshot()
        {
            var connection = _settings.Connection;
            if (connection == null)
            {
                return new ConnectionSnapshot(
                    AdbConnectionMode.Usb,
                    false,
                    string.Empty,
                    5555);
            }
            return new ConnectionSnapshot(
                connection.Mode,
                connection.AutoReconnect,
                connection.WirelessHost,
                connection.WirelessPort);
        }

        private IList<ConnectionSnapshot> GetReconnectSnapshots()
        {
            var result = new List<ConnectionSnapshot>();
            var endpoints = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var profiles = _settings.DeviceWirelessConnectionProfiles;
            if (profiles == null || profiles.Count == 0)
            {
                AddReconnectSnapshot(
                    result,
                    endpoints,
                    GetConnectionSnapshot());
                return result;
            }
            foreach (var profile in profiles)
            {
                if (profile == null) continue;
                AddReconnectSnapshot(
                    result,
                    endpoints,
                    new ConnectionSnapshot(
                        profile.Mode,
                        profile.AutoReconnect,
                        profile.WirelessHost,
                        profile.WirelessPort));
            }
            return result;
        }

        private static bool DeviceIdentityMatches(
            string expectedIdentity,
            string actualIdentity)
        {
            var expected = (expectedIdentity ?? string.Empty).Trim();
            var actual = (actualIdentity ?? string.Empty).Trim();
            if (expected.Length == 0 || actual.Length == 0) return true;
            if (PhysicalDeviceRegistry.IsTemporaryIdentity(expected))
                return true;
            return string.Equals(
                expected,
                actual,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void AddReconnectSnapshot(
            IList<ConnectionSnapshot> result,
            ISet<string> endpoints,
            ConnectionSnapshot snapshot)
        {
            if (snapshot == null ||
                snapshot.Mode != AdbConnectionMode.Wireless ||
                !snapshot.AutoReconnect ||
                string.IsNullOrWhiteSpace(snapshot.Endpoint) ||
                !endpoints.Add(snapshot.Endpoint))
            {
                return;
            }
            result.Add(snapshot);
        }

        private static string NormalizeDeviceIdentity(
            string deviceIdentity)
        {
            var identity = (deviceIdentity ?? string.Empty).Trim();
            if (identity.Length == 0)
            {
                throw new ArgumentException(
                    "Device identity is empty.",
                    "deviceIdentity");
            }
            return identity;
        }

        public static string BuildEndpoint(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) return string.Empty;
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException("port");

            var value = NormalizeHost(host);
            if (value.StartsWith("[", StringComparison.Ordinal) &&
                value.EndsWith("]", StringComparison.Ordinal))
            {
                return value + ":" + port;
            }
            if (value.IndexOf(':') >= 0)
                return "[" + value + "]:" + port;
            return value + ":" + port;
        }

        private string DetectWifiAddress(string usbSerial)
        {
            var result = _adbService.ShellForSerial(
                usbSerial,
                "ip route",
                true);
            if (!result.IsSuccess) return string.Empty;

            var output = result.StandardOutput ?? string.Empty;
            var wifiRoute = output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .FirstOrDefault(line =>
                    line.IndexOf(
                        "wlan",
                        StringComparison.OrdinalIgnoreCase) >= 0);
            var wifiMatch = Regex.Match(
                wifiRoute ?? string.Empty,
                @"\bsrc\s+(?<ip>\d{1,3}(?:\.\d{1,3}){3})\b",
                RegexOptions.IgnoreCase);
            if (wifiMatch.Success)
                return wifiMatch.Groups["ip"].Value;

            var sourceMatch = Regex.Match(
                output,
                @"\bsrc\s+(?<ip>\d{1,3}(?:\.\d{1,3}){3})\b",
                RegexOptions.IgnoreCase);
            if (sourceMatch.Success)
                return sourceMatch.Groups["ip"].Value;

            var addressMatch = Regex.Match(
                output,
                @"\b(?<ip>\d{1,3}(?:\.\d{1,3}){3})\b");
            return addressMatch.Success
                ? addressMatch.Groups["ip"].Value
                : string.Empty;
        }

        private bool WaitForConnection(
            string endpoint,
            int timeoutMs)
        {
            var started = Environment.TickCount;
            do
            {
                if (IsConnected(endpoint)) return true;
                Thread.Sleep(150);
            }
            while (unchecked(Environment.TickCount - started) < timeoutMs);
            return false;
        }

        private bool IsConnected(string endpoint)
        {
            return _adbService.GetDevices(false).Any(
                device => device.IsAuthorized &&
                    string.Equals(
                        device.Serial,
                        endpoint,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException(
                    LocalizationService.Get("Wireless.HostEmpty"),
                    "host");

            var value = host.Trim();
            if (value.StartsWith("[", StringComparison.Ordinal) &&
                value.EndsWith("]", StringComparison.Ordinal))
            {
                value = value.Substring(1, value.Length - 2);
            }
            if (value.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0 ||
                !Regex.IsMatch(value, @"^[A-Za-z0-9._:%-]+$"))
                throw new ArgumentException(
                    LocalizationService.Get("Wireless.HostInvalid"),
                    "host");
            return value;
        }

        private static string GetResultMessage(ProcessResult result)
        {
            if (result == null)
                return LocalizationService.Get(
                    "Wireless.NoResult");
            var text = !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError
                : result.StandardOutput;
            return string.IsNullOrWhiteSpace(text)
                ? "ExitCode=" + result.ExitCode
                : text.Trim();
        }

        private static bool ContainsIgnoreCase(
            string value,
            string text)
        {
            return (value ?? string.Empty).IndexOf(
                text,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class ConnectionSnapshot
        {
            public ConnectionSnapshot(
                AdbConnectionMode mode,
                bool autoReconnect,
                string host,
                int port)
            {
                Mode = mode;
                AutoReconnect = autoReconnect;
                Host = host ?? string.Empty;
                Port = port;
                Endpoint = string.Empty;
                if (Mode == AdbConnectionMode.Wireless &&
                    !string.IsNullOrWhiteSpace(Host))
                {
                    Endpoint = BuildEndpoint(Host, Port);
                }
            }

            public AdbConnectionMode Mode { get; private set; }
            public bool AutoReconnect { get; private set; }
            public string Host { get; private set; }
            public int Port { get; private set; }
            public string Endpoint { get; private set; }
        }
    }

    public sealed class WirelessDeviceSelection
    {
        internal WirelessDeviceSelection(
            AdbDeviceInfo device,
            long transitionGeneration)
        {
            Device = device;
            TransitionGeneration = transitionGeneration;
        }

        public AdbDeviceInfo Device { get; private set; }
        public long TransitionGeneration { get; private set; }
    }

    public sealed class WirelessConnectionResult
    {
        private WirelessConnectionResult(
            bool success,
            string endpoint,
            string message)
        {
            Success = success;
            Endpoint = endpoint ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Success { get; private set; }
        public string Endpoint { get; private set; }
        public string Message { get; private set; }

        public static WirelessConnectionResult Succeeded(
            string endpoint,
            string message)
        {
            return new WirelessConnectionResult(
                true,
                endpoint,
                message);
        }

        public static WirelessConnectionResult Failed(string message)
        {
            return new WirelessConnectionResult(
                false,
                string.Empty,
                message);
        }
    }
}
