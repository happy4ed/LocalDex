using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DexManager.Models;

namespace DexManager.Services
{
    // Windows side of "hand the PC keyboard and mouse to the phone".
    //
    // A control-only scrcpy session (no video, no audio, UHID keyboard and
    // mouse, over USB debugging) is kept running in the background for the
    // saved default phone.  Its tiny borderless window stays hidden until a
    // handoff; then it is shown under the pointer and clicked once, which
    // makes scrcpy capture the mouse and take the keyboard focus.
    //
    // Hook rule (same as HotkeyService): the low-level hook callbacks only
    // read the event, update counters and Post() work to the UI thread.
    // They never wait, start processes or call into scrcpy.
    public sealed class InputHandoffService : NativeWindow, IInputHandoffHost, IDisposable
    {
        private const int SessionWindowSize = 48;
        private const int EnterGraceMs = 1500;
        // Settle window after a return during which raw mouse input is
        // ignored, so scrcpy has time to drop its grab and nothing leaks.
        private const int ReturnGuardMs = 250;
        private readonly LogService _logService;
        private readonly AppSettings _settings;
        private readonly Func<string> _scrcpyPath;
        private readonly Func<string> _adbPath;
        // USB serial of a phone identity (empty when not connected by USB).
        private readonly Func<string, string> _usbSerialFor;
        // Connected phone identities in presentation order.
        private readonly Func<System.Collections.Generic.IList<string>> _orderedIdentities;
        private readonly System.Collections.Generic.List<HotkeyBinding> _bindings =
            new System.Collections.Generic.List<HotkeyBinding>();
        private readonly System.Collections.Generic.HashSet<Keys> _swallowedKeys =
            new System.Collections.Generic.HashSet<Keys>();
        // Phone picked by a device / next-phone shortcut, for this run only.
        private string _overrideIdentity = string.Empty;
        private string _targetIdentity = string.Empty;
        private long _enterPendingUntilMs = -1;
        private readonly InputHandoffController _controller;
        private readonly SynchronizationContext _ui;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Native.LowLevelProc _keyboardProc;
        private readonly IntPtr _job;
        private readonly HandoffDiagnostics _diagnostics;
        private readonly InputHandoffOverlay _overlay;
        private IntPtr _keyboardHook;
        private bool _rawInputRegistered;
        private bool _convertHangul = true;
        private int _hangulVkOverride;
        private string _monitorDeviceName = string.Empty;
        private Process _process;
        private IntPtr _sessionWindow;
        private IntPtr _foregroundBeforeStart;
        private long _enteredAtMs;
        // While returning, input is owned by neither side: raw deltas are
        // ignored so nothing leaks to the phone or re-triggers an entry.
        private long _returnGuardUntilMs = -1;
        private int _outsideTicks;
        private int _enterPosted;
        private int _returnPosted;
        private bool _disposed;

        public InputHandoffService(
            LogService logService,
            AppSettings settings,
            Func<string> scrcpyPath,
            Func<string> adbPath,
            Func<string, string> usbSerialFor,
            Func<System.Collections.Generic.IList<string>> orderedIdentities)
        {
            _logService = logService;
            _settings = settings;
            _scrcpyPath = scrcpyPath;
            _adbPath = adbPath;
            _usbSerialFor = usbSerialFor;
            _orderedIdentities = orderedIdentities;
            _ui = SynchronizationContext.Current ??
                new WindowsFormsSynchronizationContext();
            _controller = new InputHandoffController(this);
            _keyboardProc = KeyboardHookCallback;
            _job = CreateKillOnCloseJob();
            _diagnostics = new HandoffDiagnostics(logService);
            _overlay = new InputHandoffOverlay();
            CreateHandle(new CreateParams());
            _timer = new System.Windows.Forms.Timer { Interval = 60 };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        public InputHandoffState State { get { return _controller.State; } }

        public static string DefaultReturnHotkey
        {
            get { return InputHandoffSettings.DefaultReturnHotkey; }
        }

        // Re-reads Settings.  Safe to call any number of times.
        public void ApplySettings()
        {
            if (_disposed) return;
            var config = _settings.InputHandoff ??
                InputHandoffSettings.CreateDefault();
            BuildBindings(config);
            _convertHangul = config.ConvertHangulToShiftSpace;
            _hangulVkOverride = config.HangulKeyVk;
            _monitorDeviceName = config.MonitorDeviceName ?? string.Empty;
            _diagnostics.Enabled = config.DiagnosticLog;
            _controller.ConfigureDwell(config.DwellMs);
            _controller.SetPhoneSpeed(config.PhonePointerSpeedPercent / 100.0);
            _controller.Configure(config.Enabled, config.Edge, ActiveBoundsForCursor());
            if (config.Enabled) InstallHooks();
            else { RemoveHooks(); _overlay.HideOverlay(); }
            Refresh();
        }

        // Called on device changes and by the timer.
        public void Refresh()
        {
            if (_disposed) return;
            var serial = string.Empty;
            try
            {
                if (_overrideIdentity.Length > 0 &&
                    !IsUsbConnected(_overrideIdentity))
                {
                    // The picked phone left: back to the saved default.
                    _overrideIdentity = string.Empty;
                    _logService.Info(LocalizationService.Get(
                        "Log.Handoff.OverrideCleared"));
                }
                _targetIdentity = InputHandoffTargets.Resolve(
                    _overrideIdentity,
                    _settings.PreferredDeviceIdentity,
                    IsUsbConnected);
                serial = _targetIdentity.Length == 0
                    ? string.Empty
                    : _usbSerialFor(_targetIdentity) ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logService.Warning(ex.Message);
            }
            _controller.UpdateTarget(serial, _clock.ElapsedMilliseconds);
            TryPendingEnter();
        }

        private bool IsUsbConnected(string identity)
        {
            return !string.IsNullOrWhiteSpace(identity) &&
                !string.IsNullOrWhiteSpace(_usbSerialFor(identity));
        }

        private sealed class HotkeyBinding
        {
            public HotkeyService.KeyShortcut Shortcut;
            public bool OnlyOnPhone;
            public Action Action;
        }

        private void BuildBindings(InputHandoffSettings config)
        {
            _bindings.Clear();
            AddBinding(config.ReturnHotkey, true, delegate
            {
                _controller.ReturnToPc("Log.Handoff.ReturnedByHotkey");
            });
            AddBinding(config.EnterHotkey, false, delegate
            {
                RequestEnter();
            });
            AddBinding(config.NextDeviceHotkey, false, delegate
            {
                var ordered = _orderedIdentities() ??
                    new System.Collections.Generic.List<string>();
                SwitchTarget(InputHandoffTargets.Next(
                    ordered,
                    IsUsbConnected,
                    _targetIdentity));
            });
            if (config.DeviceHotkeys == null) return;
            foreach (var item in config.DeviceHotkeys)
            {
                if (item == null) continue;
                var identity = item.DeviceIdentity;
                AddBinding(item.Hotkey, false, delegate
                {
                    SwitchTarget(identity);
                });
            }
        }

        private void AddBinding(string text, bool onlyOnPhone, Action action)
        {
            HotkeyService.KeyShortcut shortcut;
            if (string.IsNullOrWhiteSpace(text) ||
                !HotkeyService.KeyShortcut.TryParse(text, out shortcut))
            {
                return;
            }
            _bindings.Add(new HotkeyBinding
            {
                Shortcut = shortcut,
                OnlyOnPhone = onlyOnPhone,
                Action = action
            });
        }

        // Device shortcut: target that phone for this run and go to it.
        // The saved default phone in Settings is not changed.
        private void SwitchTarget(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity) ||
                !IsUsbConnected(identity))
            {
                _logService.Info(LocalizationService.Get(
                    "Log.Handoff.TargetMissing"));
                return;
            }
            if (!string.Equals(
                    identity,
                    _targetIdentity,
                    StringComparison.OrdinalIgnoreCase))
            {
                // Back on the PC first, then restart for the new phone.
                _controller.ReturnToPc(null);
                _overrideIdentity = string.Equals(
                    identity,
                    _settings.PreferredDeviceIdentity,
                    StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : identity.Trim();
                _logService.Info(LocalizationService.Get(
                    "Log.Handoff.TargetSwitched"));
            }
            RequestEnter();
            Refresh();
        }

        // "Go to phone": now if the session is ready, otherwise as soon as
        // it is (within a few seconds).
        private void RequestEnter()
        {
            if (_controller.State == InputHandoffState.OnPhone) return;
            _enterPendingUntilMs = _clock.ElapsedMilliseconds + 8000;
            TryPendingEnter();
        }

        private void TryPendingEnter()
        {
            if (_enterPendingUntilMs < 0) return;
            if (_clock.ElapsedMilliseconds > _enterPendingUntilMs)
            {
                _enterPendingUntilMs = -1;
                return;
            }
            if (_controller.State != InputHandoffState.Ready ||
                _sessionWindow == IntPtr.Zero)
            {
                return;
            }
            _enterPendingUntilMs = -1;
            Native.POINT cursor;
            if (Native.GetCursorPos(out cursor))
                _controller.EnterPhone(cursor.X, cursor.Y, false);
        }

        // Releases the pointer first, then stops the session.
        public void Shutdown()
        {
            if (_disposed) return;
            _controller.Shutdown();
            RemoveHooks();
            _overlay.HideOverlay();
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                Shutdown();
            }
            finally
            {
                _disposed = true;
                _timer.Stop();
                _timer.Dispose();
                _diagnostics.Flush(_logService.LogDirectory);
                try { _overlay.Dispose(); } catch { }
                if (_job != IntPtr.Zero) Native.CloseHandle(_job);
                DestroyHandle();
            }
        }

        bool IInputHandoffHost.StartSession(string serial)
        {
            var scrcpy = _scrcpyPath();
            if (string.IsNullOrWhiteSpace(scrcpy) || !File.Exists(scrcpy))
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.Handoff.StartFailed",
                    LocalizationService.Get("Log.Handoff.ScrcpyMissing")));
                return false;
            }
            var bounds = ActiveBoundsForCursor();
            var startInfo = new ProcessStartInfo
            {
                FileName = scrcpy,
                Arguments = BuildArguments(serial, bounds),
                WorkingDirectory = Path.GetDirectoryName(scrcpy),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            var adb = _adbPath();
            if (!string.IsNullOrWhiteSpace(adb) && File.Exists(adb))
                startInfo.EnvironmentVariables["ADB"] = adb;
            try
            {
                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                process.Exited += Process_Exited;
                _foregroundBeforeStart = Native.GetForegroundWindow();
                process.Start();
                if (_job != IntPtr.Zero)
                    Native.AssignProcessToJobObject(_job, process.Handle);
                _process = process;
                _sessionWindow = IntPtr.Zero;
                ReadPhoneSizeAsync(serial, adb, process);
                return true;
            }
            catch (Exception ex)
            {
                _logService.Warning(LocalizationService.Format(
                    "Log.Handoff.StartFailed",
                    ex.Message));
                return false;
            }
        }

        void IInputHandoffHost.StopSession()
        {
            var process = _process;
            _process = null;
            _sessionWindow = IntPtr.Zero;
            if (process == null) return;
            process.Exited -= Process_Exited;
            try
            {
                // Process.Kill targets this exact process by its handle/PID.
                if (!process.HasExited) process.Kill();
                process.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                _logService.Warning(ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        bool IInputHandoffHost.EnterPhone()
        {
            var window = _sessionWindow;
            if (window == IntPtr.Zero || !Native.IsWindow(window))
                return false;
            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) return false;
            var bounds = ActiveBounds(cursor.X, cursor.Y);
            var size = SessionWindowSize;
            int x;
            int y;
            switch (_controller.Edge)
            {
                case InputHandoffEdge.Left:
                    x = bounds.Left;
                    y = Clamp(cursor.Y - size / 2, bounds.Top, bounds.Bottom - size);
                    break;
                case InputHandoffEdge.Right:
                    x = bounds.Right - size;
                    y = Clamp(cursor.Y - size / 2, bounds.Top, bounds.Bottom - size);
                    break;
                case InputHandoffEdge.Top:
                    x = Clamp(cursor.X - size / 2, bounds.Left, bounds.Right - size);
                    y = bounds.Top;
                    break;
                default:
                    x = Clamp(cursor.X - size / 2, bounds.Left, bounds.Right - size);
                    y = bounds.Bottom - size;
                    break;
            }
            Native.SetWindowPos(
                window,
                Native.HwndTopmost,
                x,
                y,
                size,
                size,
                Native.SwpShowWindow | Native.SwpNoActivate);
            var centerX = x + size / 2;
            var centerY = y + size / 2;
            Native.SetCursorPos(centerX, centerY);
            var hit = Native.GetAncestor(
                Native.WindowFromPoint(new Native.POINT { X = centerX, Y = centerY }),
                Native.GaRoot);
            if (hit != window)
            {
                // Never click something that is not the session window.
                Native.ShowWindow(window, Native.SwHide);
                return false;
            }
            // One click: it activates the window, and scrcpy captures the
            // mouse on the button release without forwarding the click.
            var inputs = new[]
            {
                Native.MouseButton(Native.MouseEventLeftDown),
                Native.MouseButton(Native.MouseEventLeftUp)
            };
            if (Native.SendInput(
                    (uint)inputs.Length,
                    inputs,
                    Marshal.SizeOf(typeof(Native.INPUT))) != inputs.Length)
            {
                Native.ShowWindow(window, Native.SwHide);
                return false;
            }
            _enteredAtMs = _clock.ElapsedMilliseconds;
            _outsideTicks = 0;
            _diagnostics.Record("enter phone (keyboard+mouse handed over)");
            return true;
        }

        // Return the pointer to the PC atomically.  Order matters so that no
        // input leaks to the phone after the return (both cursors moving):
        //   1. stop raw input reaching scrcpy as "on phone" (guard window),
        //   2. make scrcpy lose focus and its mouse grab (hide + drop
        //      topmost + foreground a PC window),
        //   3. then place the PC pointer.
        // Every step is independent so one failure cannot strand the pointer.
        void IInputHandoffHost.ReleaseToPc(HandoffPoint point)
        {
            _returnGuardUntilMs = _clock.ElapsedMilliseconds + ReturnGuardMs;
            try { Native.ClipCursor(IntPtr.Zero); } catch { }
            var window = _sessionWindow;
            try
            {
                if (window != IntPtr.Zero && Native.IsWindow(window))
                {
                    // Hiding sends focus loss; SDL (scrcpy) then ungrabs the
                    // mouse so movement stops reaching the phone.
                    Native.ShowWindow(window, Native.SwHide);
                    Native.SetWindowPos(
                        window,
                        Native.HwndBottom,
                        0, 0, 0, 0,
                        Native.SwpNoMove | Native.SwpNoSize |
                            Native.SwpNoActivate);
                }
            }
            catch
            {
            }
            try { Native.SetCursorPos(point.X, point.Y); } catch { }
            try
            {
                // Guarantee scrcpy is not the foreground/active window.
                var pcWindow = Native.GetAncestor(
                    Native.WindowFromPoint(new Native.POINT { X = point.X, Y = point.Y }),
                    Native.GaRoot);
                if (pcWindow != IntPtr.Zero && pcWindow != window)
                    Native.SetForegroundWindow(pcWindow);
            }
            catch
            {
            }
            try { Native.ClipCursor(IntPtr.Zero); } catch { }
            try { Native.SetCursorPos(point.X, point.Y); } catch { }
            _diagnostics.Record("release pointer->pc at " +
                point.X + "," + point.Y);
        }

        void IInputHandoffHost.Log(string resourceKey)
        {
            _logService.Info(LocalizationService.Get(resourceKey));
        }

        // Raw mouse input drives both edge detection (Ready) and return
        // detection (OnPhone).  Raw deltas are true even when the cursor is
        // pinned at the edge or captured by scrcpy; the cursor position is
        // read with GetCursorPos, which is in the same logical space as the
        // bounds (unlike the low-level hook point, which is physical and
        // mismatched on scaled displays -- the earlier false-edge bug).
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WmInput)
                HandleRawMouse(m.LParam);
            base.WndProc(ref m);
        }

        private void HandleRawMouse(IntPtr lParam)
        {
            int dx;
            int dy;
            if (!Native.TryReadRelativeMouse(lParam, out dx, out dy))
                return;
            var now = _clock.ElapsedMilliseconds;
            var buttonDown = Control.MouseButtons != MouseButtons.None;
            var state = _controller.State;

            // During the return settle window neither side owns the input.
            if (_returnGuardUntilMs >= 0)
            {
                if (now < _returnGuardUntilMs) return;
                _returnGuardUntilMs = -1;
            }

            if (state == InputHandoffState.OnPhone)
            {
                if ((dx != 0 || dy != 0) &&
                    _controller.OnPhoneMouseDelta(dx, dy, now, buttonDown))
                {
                    PostReturn(true);
                }
                return;
            }
            if (state != InputHandoffState.Ready || _sessionWindow == IntPtr.Zero)
                return;

            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) return;
            // Use the chosen monitor (or the one the cursor is on), so each
            // monitor's own edge triggers the handoff (not the whole virtual
            // desktop).  With a fixed monitor, ignore the cursor elsewhere.
            if (!OnActiveMonitor(cursor.X, cursor.Y)) return;
            _controller.UpdateBounds(ActiveBounds(cursor.X, cursor.Y));
            var push = InputHandoffController.OutwardPush(
                _controller.Edge, dx, dy);
            if (_diagnostics.Enabled)
                RecordEdgeSample(cursor, push, now);
            if (_controller.OnPointerMoved(
                    cursor.X,
                    cursor.Y,
                    push,
                    now,
                    false,
                    buttonDown) &&
                Interlocked.Exchange(ref _enterPosted, 1) == 0)
            {
                var x = cursor.X;
                var y = cursor.Y;
                _ui.Post(delegate
                {
                    Interlocked.Exchange(ref _enterPosted, 0);
                    if (!_disposed) _controller.EnterPhone(x, y);
                }, null);
            }
        }

        private void RecordEdgeSample(Native.POINT cursor, int push, long now)
        {
            var bounds = ActiveBounds(cursor.X, cursor.Y);
            var edge = _controller.Edge;
            var distance = InputHandoffEdgeDetector.DistanceFromEdge(
                bounds, edge, cursor.X, cursor.Y);
            _diagnostics.Record(string.Format(
                "edge x={0} y={1} mon={2} dpi={3} logical-bounds=({4},{5} {6}x{7}) edge={8} dist={9} push={10} progress={11:0.00} state={12}",
                cursor.X,
                cursor.Y,
                MonitorIndexAt(cursor.X, cursor.Y),
                MonitorDpiAt(cursor.X, cursor.Y),
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                edge,
                distance,
                push,
                _controller.EdgeProgress(now),
                _controller.State));
        }

        private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var data = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam,
                    typeof(Native.KBDLLHOOKSTRUCT));
                var message = wParam.ToInt32();
                var key = (Keys)data.vkCode;
                var down = message == Native.WmKeyDown ||
                    message == Native.WmSysKeyDown;
                var injected = (data.flags & Native.LlkhfInjected) != 0;
                // Diagnostic: the exact code of every key, so the Korean/
                // English key can be identified from one press in the log.
                if (_diagnostics.Enabled)
                {
                    _diagnostics.Record(string.Format(
                        "key vk=0x{0:X2} scan=0x{1:X2} ext={2} {3} injected={4} onPhone={5}",
                        data.vkCode,
                        data.scanCode,
                        (data.flags & Native.LlkhfExtended) != 0 ? 1 : 0,
                        down ? "down" : "up",
                        injected ? 1 : 0,
                        _controller.State == InputHandoffState.OnPhone));
                }
                if (!injected)
                {
                    var onPhoneNow =
                        _controller.State == InputHandoffState.OnPhone;
                    // Korean/English key -> Shift+Space for the phone (the
                    // phone's own physical-keyboard toggle).  Only while on
                    // the phone; the injected keys reach the focused scrcpy
                    // window and are ignored by this hook (injected flag).
                    if (_convertHangul && onPhoneNow &&
                        IsHangulKey(data, _hangulVkOverride))
                    {
                        if (down && _swallowedKeys.Add(key))
                        {
                            PostAction(SendShiftSpaceToPhone);
                        }
                        else if (!down)
                        {
                            _swallowedKeys.Remove(key);
                        }
                        return new IntPtr(1);
                    }
                    if (!down && _swallowedKeys.Remove(key))
                        return new IntPtr(1);
                    if (down)
                    {
                        var onPhone = onPhoneNow;
                        ModifierStateHolder state = null;
                        foreach (var binding in _bindings)
                        {
                            if (binding.Shortcut.Key != key ||
                                (binding.OnlyOnPhone && !onPhone))
                            {
                                continue;
                            }
                            if (state == null)
                                state = new ModifierStateHolder(HotkeyService.ModifierState.Read());
                            if (!binding.Shortcut.Matches(key, state.Value))
                                continue;
                            // Swallow it (never reaches the phone or the PC
                            // app) and run the action after the hook returns.
                            _swallowedKeys.Add(key);
                            PostAction(binding.Action);
                            return new IntPtr(1);
                        }
                    }
                }
            }
            return Native.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }

        // Keyboards vary: laptops send VK_HANGUL (0x15)/VK_HANJA (0x19),
        // many externals send the Korean/English key as the extended right
        // Alt (VK_RMENU 0xA5) or a dedicated scan code (0x71/0x72/0xF1/0xF2).
        // An exact VK can also be pinned from the diagnostic log (override).
        private static bool IsHangulKey(Native.KBDLLHOOKSTRUCT data, int overrideVk)
        {
            if (overrideVk > 0) return data.vkCode == (uint)overrideVk;
            if (data.vkCode == Native.VkHangul || data.vkCode == Native.VkHanja)
                return true;
            if (data.scanCode == 0xF2 || data.scanCode == 0xF1 ||
                data.scanCode == 0x71 || data.scanCode == 0x72)
            {
                return true;
            }
            var extended = (data.flags & Native.LlkhfExtended) != 0;
            // Extended right Alt used as the Korean/English key.  Plain
            // (non-extended) Alt is left alone -- it is AltGr elsewhere.
            return extended &&
                (data.vkCode == Native.VkRMenu || data.vkCode == Native.VkHangul) &&
                (data.scanCode == Native.AltScanCode ||
                 data.scanCode == Native.HangulScanCode);
        }

        private void SendShiftSpaceToPhone()
        {
            // keybd_event injects to the focused window; only send when the
            // scrcpy session window is focused, so the keys actually reach
            // the phone (and not some PC window).
            var focused = Native.GetForegroundWindow();
            var toScrcpy = _sessionWindow != IntPtr.Zero && focused == _sessionWindow;
            if (_diagnostics.Enabled)
                _diagnostics.Record("hangul->shift+space injected focus=" +
                    (toScrcpy ? "scrcpy" : "other(skipped)"));
            if (!toScrcpy) return;
            Native.keybd_event((byte)Native.VkShift, 0, 0, UIntPtr.Zero);
            Native.keybd_event((byte)Native.VkSpace, 0, 0, UIntPtr.Zero);
            Native.keybd_event((byte)Native.VkSpace, 0, Native.KeyEventKeyUp, UIntPtr.Zero);
            Native.keybd_event((byte)Native.VkShift, 0, Native.KeyEventKeyUp, UIntPtr.Zero);
        }

        private sealed class ModifierStateHolder
        {
            public ModifierStateHolder(HotkeyService.ModifierState value)
            {
                Value = value;
            }

            public HotkeyService.ModifierState Value { get; private set; }
        }

        private void PostAction(Action action)
        {
            _ui.Post(delegate
            {
                if (_disposed) return;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _logService.Warning(ex.Message);
                }
            }, null);
        }

        private void PostReturn(bool byEdge)
        {
            if (Interlocked.Exchange(ref _returnPosted, 1) != 0) return;
            _ui.Post(delegate
            {
                Interlocked.Exchange(ref _returnPosted, 0);
                if (_disposed) return;
                if (byEdge) _controller.ReturnByEdge();
                else _controller.ReturnToPc("Log.Handoff.ReturnedByHotkey");
            }, null);
        }

        private int _refreshDivider;

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_disposed) return;
            var process = _process;
            if (process != null && _sessionWindow == IntPtr.Zero)
                FindAndHideSessionWindow(process);
            if (_controller.State == InputHandoffState.OnPhone)
            {
                WatchOnPhone();
                if (_diagnostics.Enabled)
                    _diagnostics.Record(_controller.PhoneReturnDebug());
            }
            UpdateOverlay();
            // The timer runs fast for smooth overlay progress; refresh the
            // target and flush diagnostics about four times a second.
            if (++_refreshDivider >= 4)
            {
                _refreshDivider = 0;
                Refresh();
                _diagnostics.Flush(_logService.LogDirectory);
            }
        }

        private void UpdateOverlay()
        {
            var state = _controller.State;
            var bounds = ActiveBoundsForCursor();
            if (state == InputHandoffState.OnPhone)
            {
                _overlay.ShowReturn(
                    bounds, _controller.Edge, _controller.PhoneNearReturnEdge);
                return;
            }
            if (state == InputHandoffState.Ready)
            {
                var progress = _controller.EdgeProgress(
                    _clock.ElapsedMilliseconds);
                if (progress > 0)
                {
                    _overlay.ShowArming(bounds, _controller.Edge, progress);
                    return;
                }
            }
            _overlay.HideOverlay();
        }


        // Safety net while the input is on the phone.  scrcpy keeps a
        // captured pointer inside its window; a pointer outside it, or a
        // different foreground window, means the capture is gone (scrcpy
        // capture key, Alt+Tab, Ctrl+Alt+Del, crash...).  The PC then gets
        // its pointer back at the edge it left from.
        private void WatchOnPhone()
        {
            var process = _process;
            if (process == null || process.HasExited)
            {
                _controller.OnSessionExited(_clock.ElapsedMilliseconds);
                return;
            }
            if (_clock.ElapsedMilliseconds - _enteredAtMs < EnterGraceMs)
                return;
            var window = _sessionWindow;
            var lost = window == IntPtr.Zero ||
                !Native.IsWindow(window) ||
                Native.GetForegroundWindow() != window;
            if (!lost)
            {
                Native.RECT rect;
                Native.POINT cursor;
                if (Native.GetWindowRect(window, out rect) &&
                    Native.GetCursorPos(out cursor))
                {
                    var inside = cursor.X >= rect.Left - 2 &&
                        cursor.X <= rect.Right + 2 &&
                        cursor.Y >= rect.Top - 2 &&
                        cursor.Y <= rect.Bottom + 2;
                    _outsideTicks = inside ? 0 : _outsideTicks + 1;
                    lost = _outsideTicks >= 2;
                }
            }
            if (lost) _controller.ReturnToPc("Log.Handoff.ReturnedReleased");
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            // Exited is raised on a thread-pool thread.
            _ui.Post(delegate
            {
                if (_disposed || !ReferenceEquals(sender, _process)) return;
                _controller.OnSessionExited(_clock.ElapsedMilliseconds);
            }, null);
        }

        private void FindAndHideSessionWindow(Process process)
        {
            int pid;
            try
            {
                if (process.HasExited) return;
                pid = process.Id;
            }
            catch
            {
                return;
            }
            var found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                uint owner;
                Native.GetWindowThreadProcessId(window, out owner);
                if (owner == (uint)pid && Native.IsWindowVisible(window))
                {
                    found = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero) return;
            Native.ShowWindow(found, Native.SwHide);
            var style = Native.GetWindowLong(found, Native.GwlExStyle);
            Native.SetWindowLong(
                found,
                Native.GwlExStyle,
                (style | Native.WsExToolWindow) & ~Native.WsExAppWindow);
            _sessionWindow = found;
            // The new SDL window may have taken the focus at launch; give
            // it back to the window the user was working in.
            var previous = _foregroundBeforeStart;
            _foregroundBeforeStart = IntPtr.Zero;
            var current = Native.GetForegroundWindow();
            if (previous != IntPtr.Zero && Native.IsWindow(previous) &&
                (current == found || current == IntPtr.Zero))
            {
                Native.SetForegroundWindow(previous);
            }
        }

        private void ReadPhoneSizeAsync(string serial, string adb, Process owner)
        {
            if (string.IsNullOrWhiteSpace(adb) || !File.Exists(adb))
            {
                _logService.Info(LocalizationService.Get(
                    "Log.Handoff.PhoneSizeUnknown"));
                return;
            }
            Task.Run(delegate
            {
                int width;
                int height;
                var ok = TryReadPhoneSize(adb, serial, out width, out height);
                _ui.Post(delegate
                {
                    if (_disposed || !ReferenceEquals(owner, _process)) return;
                    if (!ok)
                    {
                        _logService.Info(LocalizationService.Get(
                            "Log.Handoff.PhoneSizeUnknown"));
                        return;
                    }
                    // Use the phone's real resolution per axis so the
                    // virtual pointer covers the whole phone screen and only
                    // reaches the return edge at the phone's actual edge.
                    _controller.SetPhoneExtent(width, height);
                    if (_diagnostics.Enabled)
                        _diagnostics.Record("phone extent set " +
                            width + "x" + height + " from wm size");
                }, null);
            });
        }

        private static bool TryReadPhoneSize(
            string adb,
            string serial,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = adb,
                    Arguments = "-s " + serial + " shell wm size",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(5000))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return PhoneScreenSize.TryParseWmSize(output, out width, out height);
                }
            }
            catch
            {
                return false;
            }
        }

        private static string BuildArguments(string serial, HandoffRect bounds)
        {
            // Control only: no mirroring, no audio, UHID keyboard and mouse.
            // The window is required to capture input; it starts tiny,
            // borderless and off to the edge, and is hidden once found.
            return string.Join(" ", new[]
            {
                "--serial=" + Quote(serial),
                "--no-video",
                "--no-audio",
                "--keyboard=uhid",
                "--mouse=uhid",
                "--window-title=" + Quote("LocalDex Handoff"),
                "--window-borderless",
                "--always-on-top",
                "--window-x=" + (bounds.Right - SessionWindowSize),
                "--window-y=" + (bounds.Bottom - SessionWindowSize),
                "--window-width=" + SessionWindowSize,
                "--window-height=" + SessionWindowSize
            });
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", string.Empty) + "\"";
        }

        private void InstallHooks()
        {
            var module = Native.GetModuleHandle(null);
            // Only a keyboard hook is used (shortcuts and the Korean/English
            // key).  Mouse movement comes from raw input, so there is no
            // low-level mouse hook to slow the pointer down.
            if (_keyboardHook == IntPtr.Zero)
            {
                _keyboardHook = Native.SetWindowsHookEx(
                    Native.WhKeyboardLl, _keyboardProc, module, 0);
                if (_keyboardHook == IntPtr.Zero)
                    LogWin32("Log.Handoff.HookFailed");
            }
            if (!_rawInputRegistered)
            {
                _rawInputRegistered = Native.RegisterMouseRawInput(Handle, true);
                if (!_rawInputRegistered)
                    LogWin32("Log.Handoff.RawInputFailed");
            }
        }

        private void RemoveHooks()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_rawInputRegistered)
            {
                Native.RegisterMouseRawInput(IntPtr.Zero, false);
                _rawInputRegistered = false;
            }
        }

        private void LogWin32(string key)
        {
            _logService.Warning(LocalizationService.Get(key) + " " +
                new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        // Bounds of the monitor the cursor is on (or the primary one when
        // the cursor cannot be read).  Multi-monitor: detection and the
        // overlay are per-monitor, not across the whole virtual desktop, so
        // the toast shows at the bottom of the monitor in use, not in the
        // gap between two monitors.  Logical coordinates, consistent with
        // GetCursorPos.
        private static HandoffRect MonitorBoundsAt(int x, int y)
        {
            var screen = Screen.FromPoint(new System.Drawing.Point(x, y));
            var b = screen.Bounds;
            return new HandoffRect(b.Left, b.Top, b.Width, b.Height);
        }

        private static HandoffRect CursorMonitorBounds()
        {
            Native.POINT cursor;
            if (Native.GetCursorPos(out cursor))
                return MonitorBoundsAt(cursor.X, cursor.Y);
            var b = Screen.PrimaryScreen.Bounds;
            return new HandoffRect(b.Left, b.Top, b.Width, b.Height);
        }

        // The chosen monitor's bounds when one is pinned in Settings and it
        // still exists; otherwise the monitor at the given point.
        private HandoffRect ActiveBounds(int x, int y)
        {
            var chosen = ChosenMonitor();
            if (chosen != null)
            {
                var b = chosen.Bounds;
                return new HandoffRect(b.Left, b.Top, b.Width, b.Height);
            }
            return MonitorBoundsAt(x, y);
        }

        private HandoffRect ActiveBoundsForCursor()
        {
            var chosen = ChosenMonitor();
            if (chosen != null)
            {
                var b = chosen.Bounds;
                return new HandoffRect(b.Left, b.Top, b.Width, b.Height);
            }
            return CursorMonitorBounds();
        }

        // True when no monitor is pinned, or the point is on the pinned one.
        private bool OnActiveMonitor(int x, int y)
        {
            var chosen = ChosenMonitor();
            if (chosen == null) return true;
            return chosen.Bounds.Contains(new System.Drawing.Point(x, y));
        }

        private Screen ChosenMonitor()
        {
            if (string.IsNullOrWhiteSpace(_monitorDeviceName)) return null;
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.DeviceName == _monitorDeviceName) return screen;
            }
            return null; // pinned monitor gone: fall back to cursor monitor
        }

        private static int MonitorIndexAt(int x, int y)
        {
            var screen = Screen.FromPoint(new System.Drawing.Point(x, y));
            var all = Screen.AllScreens;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].DeviceName == screen.DeviceName) return i;
            }
            return 0;
        }

        private static int MonitorDpiAt(int x, int y)
        {
            try
            {
                var monitor = Native.MonitorFromPoint(
                    new Native.POINT { X = x, Y = y }, Native.MonitorDefaultToNearest);
                uint dpiX;
                uint dpiY;
                // GetDpiForMonitor returns the true DPI regardless of the
                // process's DPI-awareness.
                if (Native.GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0)
                    return (int)dpiX;
            }
            catch
            {
            }
            return 0;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(Math.Max(min, max), value));
        }

        private static IntPtr CreateKillOnCloseJob()
        {
            // If LocalDex itself dies, Windows closes the job and ends the
            // control session, which frees a captured pointer.
            try
            {
                var job = Native.CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return IntPtr.Zero;
                var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = Native.JobKillOnClose;
                var length = Marshal.SizeOf(info);
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!Native.SetInformationJobObject(
                            job,
                            Native.JobExtendedLimitInformation,
                            buffer,
                            (uint)length))
                    {
                        Native.CloseHandle(job);
                        return IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
                return job;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static class Native
        {
            internal const int WhKeyboardLl = 13;
            internal const int WhMouseLl = 14;
            internal const int WmMouseMove = 0x0200;
            internal const int WmKeyDown = 0x0100;
            internal const int WmSysKeyDown = 0x0104;
            internal const int WmInput = 0x00FF;
            internal const uint LlmhfInjected = 0x01;
            internal const uint LlkhfInjected = 0x10;
            internal const int SwHide = 0;
            internal const uint SwpNoActivate = 0x0010;
            internal const uint SwpShowWindow = 0x0040;
            internal const uint SwpNoMove = 0x0002;
            internal const uint SwpNoSize = 0x0001;
            internal static readonly IntPtr HwndTopmost = new IntPtr(-1);
            internal static readonly IntPtr HwndBottom = new IntPtr(1);
            internal const int VkShift = 0x10;
            internal const int VkSpace = 0x20;
            internal const int VkHangul = 0x15;
            internal const int VkHanja = 0x19;
            internal const uint VkRMenu = 0xA5;
            internal const uint HangulScanCode = 0xF2;
            internal const uint AltScanCode = 0x38;
            internal const uint LlkhfExtended = 0x01;
            internal const uint KeyEventKeyUp = 0x0002;
            internal const uint GaRoot = 2;
            internal const int GwlExStyle = -20;
            internal const int WsExToolWindow = 0x00000080;
            internal const int WsExAppWindow = 0x00040000;
            internal const uint MouseEventLeftDown = 0x0002;
            internal const uint MouseEventLeftUp = 0x0004;
            // Kill on close; children of scrcpy (an adb server it may start)
            // silently break away so they are not killed with LocalDex.
            internal const uint JobKillOnClose = 0x2000 | 0x1000;
            internal const int JobExtendedLimitInformation = 9;
            private const uint RidInput = 0x10000003;
            private const uint RimTypeMouse = 0;
            private const uint RideVInputSink = 0x00000100;
            private const uint RideVRemove = 0x00000001;
            private const ushort MouseMoveAbsolute = 0x01;

            internal delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
            internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

            [StructLayout(LayoutKind.Sequential)]
            internal struct POINT { public int X; public int Y; }

            [StructLayout(LayoutKind.Sequential)]
            internal struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

            [StructLayout(LayoutKind.Sequential)]
            internal struct MSLLHOOKSTRUCT
            {
                public POINT pt;
                public uint mouseData;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct KBDLLHOOKSTRUCT
            {
                public uint vkCode;
                public uint scanCode;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            // MOUSEINPUT is the largest member of the INPUT union, so this
            // has the native size (28 bytes on x86, 40 on x64).
            [StructLayout(LayoutKind.Sequential)]
            internal struct INPUT
            {
                public uint type;
                public MOUSEINPUT mi;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct RAWINPUTDEVICE
            {
                public ushort usUsagePage;
                public ushort usUsage;
                public uint dwFlags;
                public IntPtr hwndTarget;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct RAWINPUTHEADER
            {
                public uint dwType;
                public uint dwSize;
                public IntPtr hDevice;
                public IntPtr wParam;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            internal static INPUT MouseButton(uint flags)
            {
                return new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = flags } };
            }

            internal static bool RegisterMouseRawInput(IntPtr window, bool add)
            {
                var device = new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage = 0x02,
                    dwFlags = add ? RideVInputSink : RideVRemove,
                    hwndTarget = add ? window : IntPtr.Zero
                };
                return RegisterRawInputDevices(
                    new[] { device },
                    1,
                    (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }

            // Reads one WM_INPUT mouse packet.  Only relative movement is
            // used; absolute devices (pen, remote desktop) are ignored.
            internal static bool TryReadRelativeMouse(IntPtr rawInput, out int dx, out int dy)
            {
                dx = 0;
                dy = 0;
                var headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
                uint size = 0;
                GetRawInputData(rawInput, RidInput, IntPtr.Zero, ref size, headerSize);
                if (size == 0 || size > 1024) return false;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (GetRawInputData(rawInput, RidInput, buffer, ref size, headerSize) != size)
                        return false;
                    var header = (RAWINPUTHEADER)Marshal.PtrToStructure(
                        buffer,
                        typeof(RAWINPUTHEADER));
                    if (header.dwType != RimTypeMouse) return false;
                    var offset = (int)headerSize;
                    var flags = (ushort)Marshal.ReadInt16(buffer, offset);
                    if ((flags & MouseMoveAbsolute) != 0) return false;
                    dx = Marshal.ReadInt32(buffer, offset + 12);
                    dy = Marshal.ReadInt32(buffer, offset + 16);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr SetWindowsHookEx(int hook, LowLevelProc callback, IntPtr module, uint threadId);
            [DllImport("user32.dll")]
            internal static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
            internal const uint MonitorDefaultToNearest = 2;
            [DllImport("user32.dll")]
            internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);
            [DllImport("shcore.dll")]
            internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool UnhookWindowsHookEx(IntPtr hook);
            [DllImport("user32.dll")]
            internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            internal static extern IntPtr GetModuleHandle(string name);
            [DllImport("user32.dll")]
            internal static extern bool GetCursorPos(out POINT point);
            [DllImport("user32.dll")]
            internal static extern bool SetCursorPos(int x, int y);
            [DllImport("user32.dll")]
            internal static extern bool ClipCursor(IntPtr rect);
            [DllImport("user32.dll")]
            internal static extern bool ShowWindow(IntPtr window, int command);
            [DllImport("user32.dll")]
            internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
            [DllImport("user32.dll")]
            internal static extern bool IsWindow(IntPtr window);
            [DllImport("user32.dll")]
            internal static extern bool IsWindowVisible(IntPtr window);
            [DllImport("user32.dll")]
            internal static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")]
            internal static extern bool SetForegroundWindow(IntPtr window);
            [DllImport("user32.dll")]
            internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
            [DllImport("user32.dll")]
            internal static extern IntPtr WindowFromPoint(POINT point);
            [DllImport("user32.dll")]
            internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
            [DllImport("user32.dll")]
            internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
            [DllImport("user32.dll")]
            internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
            [DllImport("user32.dll")]
            internal static extern int GetWindowLong(IntPtr window, int index);
            [DllImport("user32.dll")]
            internal static extern int SetWindowLong(IntPtr window, int index, int value);
            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
            [DllImport("user32.dll")]
            private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            internal static extern IntPtr CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll")]
            internal static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
            [DllImport("kernel32.dll")]
            internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll")]
            internal static extern bool CloseHandle(IntPtr handle);
        }
    }

    // Fixed-size ring buffer of handoff decisions.  Record() is cheap and
    // safe to call from a hook (only enqueues); a timer thread flushes to a
    // file.  Device serials and IP addresses are never recorded.
    internal sealed class HandoffDiagnostics
    {
        private const int Capacity = 2048;
        private readonly LogService _logService;
        private readonly string[] _lines = new string[Capacity];
        private readonly object _sync = new object();
        private int _count;
        private int _head;
        private bool _enabled;
        private bool _wroteHeader;

        public HandoffDiagnostics(LogService logService)
        {
            _logService = logService;
        }

        public bool Enabled
        {
            get { lock (_sync) return _enabled; }
            set { lock (_sync) _enabled = value; }
        }

        public void Record(string line)
        {
            if (line == null) return;
            lock (_sync)
            {
                if (!_enabled) return;
                _lines[(_head + _count) % Capacity] = Stamp() + line;
                if (_count < Capacity) _count++;
                else _head = (_head + 1) % Capacity;
            }
        }

        public void Flush(string directory)
        {
            string[] pending;
            bool header;
            lock (_sync)
            {
                if (!_enabled || _count == 0) return;
                pending = new string[_count];
                for (var i = 0; i < _count; i++)
                    pending[i] = _lines[(_head + i) % Capacity];
                _count = 0;
                _head = 0;
                header = !_wroteHeader;
                _wroteHeader = true;
            }
            try
            {
                var folder = string.IsNullOrWhiteSpace(directory)
                    ? System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "logs")
                    : directory;
                System.IO.Directory.CreateDirectory(folder);
                var path = System.IO.Path.Combine(folder, "handoff-diagnostic.log");
                using (var writer = new System.IO.StreamWriter(path, true))
                {
                    if (header)
                    {
                        writer.WriteLine(
                            "# LocalDex handoff diagnostic log (no serial/IP). " +
                            "Coordinates are logical pixels.");
                    }
                    foreach (var line in pending) writer.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                _logService.Warning(ex.Message);
            }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff") + " ";
        }
    }

    // Small click-through, non-activating, top-most status indicator shown
    // near the configured edge: progress while arming, and a "can return"
    // hint while on the phone.  It replaces the stray scrcpy icon as the
    // only on-screen sign of the handoff state.
    internal sealed class InputHandoffOverlay : NativeWindow, IDisposable
    {
        private const int Width = 190;
        private const int Height = 30;
        private const int Margin = 12;
        private bool _visible;
        private bool _disposed;
        private string _text = string.Empty;
        private double _progress;

        public InputHandoffOverlay()
        {
            var cp = new CreateParams
            {
                X = -2000,
                Y = -2000,
                Width = Width,
                Height = Height,
                Style = unchecked((int)0x80000000), // WS_POPUP
                ExStyle = ExToolWindow | ExTopmost | ExLayered |
                    ExTransparent | ExNoActivate
            };
            CreateHandle(cp);
        }

        private const int ExTopmost = 0x00000008;
        private const int ExTransparent = 0x00000020;
        private const int ExToolWindow = 0x00000080;
        private const int ExLayered = 0x00080000;
        private const int ExNoActivate = 0x08000000;

        public void ShowArming(HandoffRect bounds, InputHandoffEdge edge, double progress)
        {
            _progress = progress;
            Present(bounds, edge,
                LocalizationService.Get("Handoff.Overlay.Arming"));
        }

        public void ShowReturn(HandoffRect bounds, InputHandoffEdge edge, bool nearEdge)
        {
            _progress = 0;
            Present(bounds, edge, LocalizationService.Get(
                nearEdge ? "Handoff.Overlay.ReturnNow" : "Handoff.Overlay.OnPhone"));
        }

        public void HideOverlay()
        {
            if (_disposed || !_visible) return;
            _visible = false;
            ShowWindow(Handle, SwHide);
        }

        private void Present(HandoffRect bounds, InputHandoffEdge edge, string text)
        {
            if (_disposed) return;
            _text = text ?? string.Empty;
            int x;
            int y;
            switch (edge)
            {
                case InputHandoffEdge.Left:
                    x = bounds.Left + Margin;
                    y = bounds.Top + bounds.Height / 2 - Height / 2;
                    break;
                case InputHandoffEdge.Right:
                    x = bounds.Right - Width - Margin;
                    y = bounds.Top + bounds.Height / 2 - Height / 2;
                    break;
                case InputHandoffEdge.Top:
                    x = bounds.Left + bounds.Width / 2 - Width / 2;
                    y = bounds.Top + Margin;
                    break;
                default:
                    x = bounds.Left + bounds.Width / 2 - Width / 2;
                    y = bounds.Bottom - Height - Margin;
                    break;
            }
            SetWindowPos(Handle, HwndTopmost, x, y, Width, Height,
                SwpNoActivate | (_visible ? 0u : SwpShowWindow));
            _visible = true;
            RenderLayered();
        }

        private void RenderLayered()
        {
            using (var bitmap = new System.Drawing.Bitmap(Width, Height))
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode =
                        System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.FromArgb(0, 0, 0, 0));
                    using (var back = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(220, 20, 22, 26)))
                    {
                        FillRounded(g, back, 0, 0, Width, Height, 8);
                    }
                    if (_progress > 0)
                    {
                        var w = (int)Math.Round((Width - 6) * Math.Min(1.0, _progress));
                        using (var bar = new System.Drawing.SolidBrush(
                            System.Drawing.Color.FromArgb(235, 64, 160, 255)))
                        {
                            FillRounded(g, bar, 3, Height - 7, Math.Max(2, w), 4, 2);
                        }
                    }
                    using (var font = new System.Drawing.Font(
                        "Segoe UI", 9f, System.Drawing.FontStyle.Regular))
                    using (var fore = new System.Drawing.SolidBrush(
                        System.Drawing.Color.White))
                    {
                        g.DrawString(_text, font, fore,
                            new System.Drawing.RectangleF(8, 3, Width - 12, Height - 10));
                    }
                }
                ApplyLayered(bitmap);
            }
        }

        private static void FillRounded(
            System.Drawing.Graphics g,
            System.Drawing.Brush brush,
            int x, int y, int w, int h, int r)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var d = r * 2;
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        private void ApplyLayered(System.Drawing.Bitmap bitmap)
        {
            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var hBitmap = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
            var old = SelectObject(memDc, hBitmap);
            try
            {
                var size = new SIZE { cx = Width, cy = Height };
                var src = new POINT { X = 0, Y = 0 };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1 // AC_SRC_ALPHA
                };
                UpdateLayeredWindow(
                    Handle, screenDc, IntPtr.Zero, ref size,
                    memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private const int SwHide = 0;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, IntPtr pos, ref SIZE size, IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { DestroyHandle(); } catch { }
        }
    }

}
