using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DexManager.Models;
using DexManager.Services;

namespace DexManager.Forms
{
    public sealed partial class MainForm : Form
    {
        // Phones removed from the device list for this run.  A removed phone
        // shows up again by itself when the same identity connects.
        private readonly HashSet<string> _forgottenDeviceIdentities =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void DeviceMonitor_DeviceIdentityAliasFound(
            object sender,
            DeviceIdentityAliasEventArgs e)
        {
            if (e == null) return;
            RunOnUi(delegate { MergeDeviceIdentityAlias(e.Alias, e.Identity); });
        }

        // The phone now known by its serial identity may have settings and a
        // stale tab under its android_id identity (an older build fell back to
        // it when a property read failed).  Move the settings back so the
        // default phone and the handoff target match it again.
        private void MergeDeviceIdentityAlias(string alias, string identity)
        {
            if (_exitInProgress || IsDisposed) return;
            if (string.IsNullOrWhiteSpace(alias) ||
                string.IsNullOrWhiteSpace(identity)) return;

            var changed = false;
            try
            {
                var probe = _settingsService.Clone(_settings);
                if (probe.ReplaceDeviceIdentity(alias, identity))
                {
                    _settingsService.UpdateAndSave(_settings, delegate(
                        AppSettings candidate)
                    {
                        candidate.ReplaceDeviceIdentity(alias, identity);
                    });
                    _logService.Info(LocalizationService.Format(
                        "Log.DeviceIdentity.Merged",
                        alias,
                        identity));
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                _logService.Error(
                    "Could not move saved phone settings to " + identity + ".",
                    ex);
            }

            if (!IsDeviceIdentityConnected(alias) && HasDeviceContext(alias) &&
                _forgottenDeviceIdentities.Add(alias))
            {
                changed = true;
            }
            if (changed) RefreshAfterDeviceListChange();
        }

        private bool HasDeviceContext(string identity)
        {
            lock (_deviceContextsSync)
                return _deviceContexts.ContainsKey(identity);
        }

        private bool IsDeviceForgotten(DeviceUiContext context)
        {
            return context != null &&
                !string.IsNullOrWhiteSpace(context.Identity) &&
                _forgottenDeviceIdentities.Contains(context.Identity) &&
                !IsDeviceIdentityConnected(context.Identity);
        }

        private ContextMenuStrip CreateForgetDeviceMenu(DeviceUiContext context)
        {
            var menu = new ContextMenuStrip();
            var item = new ToolStripMenuItem(
                LocalizationService.Get("Device.Forget.Menu"));
            item.Click += delegate { ForgetDeviceContext(context); };
            menu.Items.Add(item);
            return menu;
        }

        // Settings → removes a phone that is not connected from the list and
        // deletes what was saved for it.  A connected phone cannot be removed.
        private void ForgetDeviceContext(DeviceUiContext context)
        {
            if (_exitInProgress || IsDisposed || context == null ||
                string.IsNullOrWhiteSpace(context.Identity) ||
                IsDeviceIdentityConnected(context.Identity))
            {
                return;
            }

            var identity = context.Identity;
            var name = GetContextDisplayName(context);
            var answer = MessageBox.Show(
                this,
                LocalizationService.Format("Device.Forget.Confirm", name),
                LocalizationService.Get("Device.Forget.Title"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;

            try
            {
                _settingsService.UpdateAndSave(_settings, delegate(
                    AppSettings candidate)
                {
                    candidate.ForgetDeviceIdentity(identity);
                });
            }
            catch (Exception ex)
            {
                _logService.Error(
                    "Could not delete saved settings for " + identity + ".",
                    ex);
            }
            _forgottenDeviceIdentities.Add(identity);
            _logService.Info(LocalizationService.Format(
                "Log.DeviceForgotten",
                name,
                identity));
            RefreshAfterDeviceListChange();
        }

        private void RefreshAfterDeviceListChange()
        {
            if (IsDeviceForgotten(_selectedDeviceContext))
            {
                var automatic = DevicePresentationOrder.ChooseIdentity(
                    _devicePresentationOrder.GetIdentities(),
                    IsDeviceIdentityConnected,
                    _settings.PreferredDeviceIdentity ?? string.Empty);
                DeviceUiContext next = null;
                if (automatic.Length > 0)
                {
                    lock (_deviceContextsSync)
                        _deviceContexts.TryGetValue(automatic, out next);
                }
                if (next == null)
                {
                    foreach (var item in GetDeviceContextEntries())
                    {
                        if (!IsDeviceForgotten(item.Value))
                        {
                            next = item.Value;
                            break;
                        }
                    }
                }
                if (next != null) SelectDeviceContext(next);
            }
            RebuildDeviceTabs();
            RefreshSelectedDeviceState();
            RefreshInputHandoff();
            if (_settingsForm != null && !_settingsForm.IsDisposed)
                _settingsForm.RefreshSelectedDeviceContext();
        }
    }
}
