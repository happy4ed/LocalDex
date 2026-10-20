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
        private readonly Native.LowLevelProc _mouseProc;
        private readonly Native.LowLevelProc _keyboardProc;
        private readonly IntPtr _job;
        private IntPtr _mouseHook;
        private IntPtr _keyboardHook;
        private bool _rawInputRegistered;
        private Process _process;
        private IntPtr _sessionWindow;
        private IntPtr _foregroundBeforeStart;
        private long _enteredAtMs;
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
            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
            _job = CreateKillOnCloseJob();
            CreateHandle(new CreateParams());
            _timer = new System.Windows.Forms.Timer { Interval = 250 };
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
            _controller.SetPhoneSpeed(config.PhonePointerSpeedPercent / 100.0);
            _controller.Configure(config.Enabled, config.Edge, GetBounds());
            if (config.Enabled) InstallHooks();
            else RemoveHooks();
            Refresh();
        }

        // Called on device changes and by the timer.
        public void Refresh()
        {
            if (_disposed) return;
            _controller.UpdateBounds(GetBounds());
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
            var bounds = GetBounds();
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
            var bounds = GetBounds();
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
            return true;
        }

        void IInputHandoffHost.ReleaseToPc(HandoffPoint point)
        {
            // Every step is independent so that one failure cannot leave
            // the PC without its pointer.
            try { Native.ClipCursor(IntPtr.Zero); } catch { }
            try
            {
                var window = _sessionWindow;
                if (window != IntPtr.Zero && Native.IsWindow(window))
                    Native.ShowWindow(window, Native.SwHide);
            }
            catch
            {
            }
            try { Native.ClipCursor(IntPtr.Zero); } catch { }
            try { Native.SetCursorPos(point.X, point.Y); } catch { }
        }

        void IInputHandoffHost.Log(string resourceKey)
        {
            _logService.Info(LocalizationService.Get(resourceKey));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WmInput &&
                _controller.State == InputHandoffState.OnPhone)
            {
                int dx;
                int dy;
                if (Native.TryReadRelativeMouse(m.LParam, out dx, out dy) &&
                    (dx != 0 || dy != 0) &&
                    _controller.OnPhoneMouseDelta(
                        dx,
                        dy,
                        _clock.ElapsedMilliseconds,
                        Control.MouseButtons != MouseButtons.None))
                {
                    PostReturn(true);
                }
            }
            base.WndProc(ref m);
        }

        private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 &&
                wParam.ToInt32() == Native.WmMouseMove &&
                _controller.State == InputHandoffState.Ready &&
                _sessionWindow != IntPtr.Zero)
            {
                var data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam,
                    typeof(Native.MSLLHOOKSTRUCT));
                var injected = (data.flags & Native.LlmhfInjected) != 0;
                if (_controller.OnPointerMoved(
                        data.pt.X,
                        data.pt.Y,
                        _clock.ElapsedMilliseconds,
                        injected,
                        Control.MouseButtons != MouseButtons.None) &&
                    Interlocked.Exchange(ref _enterPosted, 1) == 0)
                {
                    var x = data.pt.X;
                    var y = data.pt.Y;
                    _ui.Post(delegate
                    {
                        Interlocked.Exchange(ref _enterPosted, 0);
                        if (!_disposed) _controller.EnterPhone(x, y);
                    }, null);
                }
            }
            return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && _bindings.Count > 0)
            {
                var data = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam,
                    typeof(Native.KBDLLHOOKSTRUCT));
                var message = wParam.ToInt32();
                var key = (Keys)data.vkCode;
                var down = message == Native.WmKeyDown ||
                    message == Native.WmSysKeyDown;
                if ((data.flags & Native.LlkhfInjected) == 0)
                {
                    if (!down && _swallowedKeys.Remove(key))
                        return new IntPtr(1);
                    if (down)
                    {
                        var onPhone =
                            _controller.State == InputHandoffState.OnPhone;
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

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_disposed) return;
            var process = _process;
            if (process != null && _sessionWindow == IntPtr.Zero)
                FindAndHideSessionWindow(process);
            if (_controller.State == InputHandoffState.OnPhone)
                WatchOnPhone();
            Refresh();
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
                    // Orientation is not known; use the long side for both
                    // axes so that the virtual pointer never reaches the
                    // return edge before the real one.
                    var side = Math.Max(width, height);
                    _controller.SetPhoneExtent(side, side);
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
            if (_mouseHook == IntPtr.Zero)
            {
                _mouseHook = Native.SetWindowsHookEx(
                    Native.WhMouseLl, _mouseProc, module, 0);
                if (_mouseHook == IntPtr.Zero)
                    LogWin32("Log.Handoff.HookFailed");
            }
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
            if (_mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
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

        private static HandoffRect GetBounds()
        {
            var screen = SystemInformation.VirtualScreen;
            return new HandoffRect(screen.Left, screen.Top, screen.Width, screen.Height);
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
            internal static readonly IntPtr HwndTopmost = new IntPtr(-1);
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
}
