using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DexManager.Models;

namespace DexManager.Services
{
    // Galaxy phones start every USB connection as "charging only", even on a
    // PC they have seen before; Android keeps no per-PC USB mode. The trust
    // that does exist per PC is the USB debugging authorization, so when a
    // phone that already trusts this PC (authorized adb transport) is plugged
    // in, switch it to file transfer (MTP) so the PC can browse it too.
    //
    // The switch re-enumerates USB: the adb transport drops and comes back
    // with the same serial. That reconnection must not switch again, and if
    // the phone falls back to charging (e.g. locked), we stop instead of
    // looping until the phone is plugged in again later.
    public sealed class UsbFileTransferModeService
    {
        public enum UsbModeAction
        {
            None,
            Switch,
            GiveUp
        }

        private static readonly TimeSpan ReconnectWindow =
            TimeSpan.FromSeconds(60);
        // gone longer than this = unplugged and plugged in again (the switch
        // itself drops the transport for only a few seconds)
        private static readonly TimeSpan ReplugGap =
            TimeSpan.FromSeconds(10);

        private readonly AdbService _adbService;
        private readonly Func<bool> _isEnabled;
        private readonly LogService _logService;
        private readonly object _sync = new object();
        private HashSet<string> _authorizedUsb =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _switchedAtUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _gaveUp =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lostAtUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public UsbFileTransferModeService(
            AdbService adbService,
            Func<bool> isEnabled,
            LogService logService)
        {
            if (adbService == null) throw new ArgumentNullException("adbService");
            if (isEnabled == null) throw new ArgumentNullException("isEnabled");
            _adbService = adbService;
            _isEnabled = isEnabled;
            _logService = logService;
        }

        public void OnSnapshot(DeviceRegistrySnapshot snapshot)
        {
            var fresh = TakeNewUsbSerials(snapshot);
            if (fresh.Count == 0 || !_isEnabled()) return;
            foreach (var serial in fresh)
            {
                var target = serial;
                Task.Run(delegate { Apply(target); });
            }
        }

        // USB transports that are authorized now but were not in the previous
        // snapshot (a phone just plugged in, or back after re-enumeration).
        public List<string> TakeNewUsbSerials(DeviceRegistrySnapshot snapshot)
        {
            return TakeNewUsbSerials(snapshot, DateTime.UtcNow);
        }

        public List<string> TakeNewUsbSerials(
            DeviceRegistrySnapshot snapshot,
            DateTime nowUtc)
        {
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot != null && snapshot.Devices != null)
            {
                foreach (var device in snapshot.Devices)
                {
                    if (device == null || device.Transports == null) continue;
                    foreach (var transport in device.Transports)
                    {
                        if (transport != null &&
                            transport.Kind == DeviceTransportKind.Usb &&
                            transport.IsAuthorized &&
                            !string.IsNullOrWhiteSpace(transport.Serial))
                        {
                            current.Add(transport.Serial.Trim());
                        }
                    }
                }
            }

            var fresh = new List<string>();
            lock (_sync)
            {
                foreach (var serial in _authorizedUsb)
                {
                    if (!current.Contains(serial)) _lostAtUtc[serial] = nowUtc;
                }
                foreach (var serial in current)
                {
                    if (_authorizedUsb.Contains(serial)) continue;
                    fresh.Add(serial);
                    DateTime lostAt;
                    if (_lostAtUtc.TryGetValue(serial, out lostAt) &&
                        nowUtc - lostAt >= ReplugGap)
                    {
                        // Android resets to charging on unplug: try afresh
                        _gaveUp.Remove(serial);
                        _switchedAtUtc.Remove(serial);
                    }
                    _lostAtUtc.Remove(serial);
                }
                _authorizedUsb = current;
            }
            return fresh;
        }

        public bool HasGivenUp(string serial)
        {
            lock (_sync) { return _gaveUp.Contains(serial); }
        }

        // test hook: pretend our switch for serial just happened / failed
        public void MarkSwitched(string serial, DateTime atUtc)
        {
            lock (_sync) { _switchedAtUtc[serial] = atUtc; }
        }

        public void MarkGaveUp(string serial)
        {
            lock (_sync) { _gaveUp.Add(serial); }
        }

        public static UsbModeAction Decide(
            string functions,
            bool switchedRecently,
            bool gaveUp)
        {
            if (gaveUp || HasFileTransfer(functions)) return UsbModeAction.None;
            // our switch a moment ago did not stick (the phone went back to
            // charging, e.g. while locked): stop rather than loop
            if (switchedRecently) return UsbModeAction.GiveUp;
            return UsbModeAction.Switch;
        }

        public static bool HasFileTransfer(string functions)
        {
            return HasFunction(functions, "mtp");
        }

        // "svc usb getFunctions" prints e.g. "mtp,adb", "adb" or "none".
        public static bool LooksLikeUsbFunctions(string functions)
        {
            return HasFunction(functions, "adb") ||
                HasFunction(functions, "mtp") ||
                HasFunction(functions, "none") ||
                HasFunction(functions, "ptp") ||
                HasFunction(functions, "rndis");
        }

        private static bool HasFunction(string functions, string name)
        {
            if (string.IsNullOrWhiteSpace(functions)) return false;
            foreach (var part in functions.Split(
                new[] { ',', ':', ' ', '\r', '\n', '\t' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(part, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void Apply(string serial)
        {
            try
            {
                var read = _adbService.ShellForSerial(
                    serial,
                    "svc usb getFunctions",
                    false);
                // svc prints the answer on stderr
                var functions = ((read.StandardOutput ?? string.Empty) + " " +
                    (read.StandardError ?? string.Empty)).Trim();
                if (!LooksLikeUsbFunctions(functions))
                {
                    Log("Log.UsbMode.Unknown", serial, functions);
                    return;
                }

                bool switchedRecently;
                bool gaveUp;
                lock (_sync)
                {
                    DateTime at;
                    switchedRecently = _switchedAtUtc.TryGetValue(serial, out at) &&
                        DateTime.UtcNow - at < ReconnectWindow;
                    gaveUp = _gaveUp.Contains(serial);
                }

                switch (Decide(functions, switchedRecently, gaveUp))
                {
                    case UsbModeAction.None:
                        if (switchedRecently && HasFileTransfer(functions))
                        {
                            lock (_sync) { _switchedAtUtc.Remove(serial); }
                            Log("Log.UsbMode.Switched", serial, functions);
                        }
                        return;
                    case UsbModeAction.GiveUp:
                        lock (_sync)
                        {
                            _gaveUp.Add(serial);
                            _switchedAtUtc.Remove(serial);
                        }
                        Log("Log.UsbMode.NotKept", serial, functions);
                        return;
                }

                lock (_sync) { _switchedAtUtc[serial] = DateTime.UtcNow; }
                Log("Log.UsbMode.Switching", serial, functions);
                var result = _adbService.ShellForSerial(
                    serial,
                    "svc usb setFunctions mtp",
                    true);
                if (!result.IsSuccess)
                {
                    Log("Log.UsbMode.Failed", serial,
                        ((result.StandardError ?? string.Empty) + " " +
                            (result.StandardOutput ?? string.Empty)).Trim());
                }
            }
            catch (Exception ex)
            {
                Log("Log.UsbMode.Failed", serial, ex.Message);
            }
        }

        private void Log(string key, string serial, string detail)
        {
            if (_logService == null) return;
            _logService.Info(LocalizationService.Format(key, serial, detail));
        }
    }
}
