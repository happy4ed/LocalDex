using System;
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
                    GetInputHandoffTargetSerial,
                    IsInputHandoffTargetBusy);
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

        // Authorized USB serial of the saved default phone, or empty.  The
        // handoff is a USB feature: a wireless-only connection waits.
        private string GetInputHandoffTargetSerial()
        {
            var identity = _settings.PreferredDeviceIdentity;
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

        // Rule: while the default phone runs DeX or a single-app window,
        // the handoff pauses (its session is stopped).  Those windows
        // already forward the PC keyboard and mouse, and a second UHID
        // mouse would make the target display ambiguous.
        private bool IsInputHandoffTargetBusy()
        {
            var identity = _settings.PreferredDeviceIdentity;
            if (string.IsNullOrWhiteSpace(identity)) return false;
            DeviceUiContext context;
            lock (_deviceContextsSync)
            {
                if (!_deviceContexts.TryGetValue(identity, out context))
                    return false;
            }
            if (context == null || context.Runtime == null) return false;
            if (context.Runtime.Dex != null && context.Runtime.Dex.IsRunning)
                return true;
            if (context.Runtime.SingleWindows == null) return false;
            for (var slot = 1; slot <= 3; slot++)
            {
                if (context.Runtime.SingleWindows.IsRunning(slot)) return true;
            }
            return false;
        }
    }
}
