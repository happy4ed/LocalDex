using System;
using System.Collections.Generic;
using DexManager.Models;
using DexManager.Services;

namespace DexManager.Forms
{
    public partial class MainForm
    {
        private InputHandoffService _inputHandoff;

        private void InitializeInputHandoff()
        {
            try
            {
                _inputHandoff = new InputHandoffService(
                    _logService,
                    _settings,
                    delegate
                    {
                        return _settingsService.ResolvePath(
                            _settings.Paths.ScrcpyPath);
                    },
                    delegate { return _adbService.AdbPath; },
                    GetInputHandoffUsbSerial,
                    GetInputHandoffIdentities);
                _inputHandoff.ApplySettings();
            }
            catch (Exception ex)
            {
                _logService.Error(
                    LocalizationService.Get("Log.Handoff.HookFailed"),
                    ex);
            }
        }

        private void ApplyInputHandoffSettings()
        {
            if (_inputHandoff == null) return;
            TryCleanup("input handoff settings", _inputHandoff.ApplySettings);
        }

        private void RefreshInputHandoff()
        {
            if (_inputHandoff == null) return;
            TryCleanup("input handoff refresh", _inputHandoff.Refresh);
        }

        private void ShutdownInputHandoff()
        {
            if (_inputHandoff == null) return;
            var handoff = _inputHandoff;
            _inputHandoff = null;
            TryCleanup("input handoff", handoff.Dispose);
        }

        // Authorized USB serial of a phone, or empty.  The handoff is a USB
        // feature: a wireless-only connection waits.
        private string GetInputHandoffUsbSerial(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return string.Empty;
            var snapshot = _physicalDeviceRegistry.Current;
            if (snapshot == null || snapshot.Devices == null)
                return string.Empty;
            foreach (var device in snapshot.Devices)
            {
                if (device == null || !string.Equals(
                        device.Identity,
                        identity,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var usb = device.SelectAuthorizedTransport(
                    DeviceTransportKind.Usb,
                    null);
                return usb == null || string.IsNullOrWhiteSpace(usb.Serial)
                    ? string.Empty
                    : usb.Serial;
            }
            return string.Empty;
        }

        // Phones in the same order as the device tabs (for "next phone").
        // DeX or single-app windows on the target phone do not pause the
        // handoff: they are separate windows.
        private IList<string> GetInputHandoffIdentities()
        {
            return _devicePresentationOrder.GetIdentities();
        }
    }
}
