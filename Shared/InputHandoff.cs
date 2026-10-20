using System;

namespace DexManager.Models
{
    // Side of the PC desktop where the phone is placed.
    public enum InputHandoffEdge
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3
    }

    public enum InputHandoffState
    {
        // Feature disabled in Settings.
        Off = 0,
        // Enabled, but no default phone connected, the phone runs DeX or a
        // single-app window, or the control session is (re)starting.
        Waiting = 1,
        // Control-only session is running in the background; the PC owns
        // the pointer and edge detection is active.
        Ready = 2,
        // Keyboard and mouse are handed to the phone.
        OnPhone = 3
    }

    public struct HandoffRect
    {
        public HandoffRect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
        }

        public int Left { get; private set; }
        public int Top { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Right { get { return Left + Width; } }
        public int Bottom { get { return Top + Height; } }
    }

    public struct HandoffPoint
    {
        public HandoffPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; private set; }
        public int Y { get; private set; }
    }
}

namespace DexManager.Services
{
    using DexManager.Models;

    // Pure edge-push detector.  It is fed from the low-level mouse hook and
    // must never block: it only compares numbers and returns whether the
    // pointer has dwelt on the configured outer edge and kept pushing.
    public sealed class InputHandoffEdgeDetector
    {
        public const int DefaultDwellMs = 200;
        public const int DefaultPushEvents = 4;
        // After a return the pointer must move this far from the edge
        // before another handoff can trigger (prevents ping-pong).
        public const int RearmDistance = 16;

        private long _touchStartMs = -1;
        private int _pushEvents;
        private bool _armed = true;

        public InputHandoffEdgeDetector()
        {
            DwellMs = DefaultDwellMs;
            PushEvents = DefaultPushEvents;
        }

        public int DwellMs { get; set; }
        public int PushEvents { get; set; }

        public void Reset(bool requireLeaveEdge)
        {
            _touchStartMs = -1;
            _pushEvents = 0;
            _armed = !requireLeaveEdge;
        }

        public static bool IsAtEdge(
            HandoffRect bounds,
            InputHandoffEdge edge,
            int x,
            int y)
        {
            switch (edge)
            {
                case InputHandoffEdge.Left: return x <= bounds.Left;
                case InputHandoffEdge.Right: return x >= bounds.Right - 1;
                case InputHandoffEdge.Top: return y <= bounds.Top;
                default: return y >= bounds.Bottom - 1;
            }
        }

        public static int DistanceFromEdge(
            HandoffRect bounds,
            InputHandoffEdge edge,
            int x,
            int y)
        {
            switch (edge)
            {
                case InputHandoffEdge.Left: return x - bounds.Left;
                case InputHandoffEdge.Right: return bounds.Right - 1 - x;
                case InputHandoffEdge.Top: return y - bounds.Top;
                default: return bounds.Bottom - 1 - y;
            }
        }

        // Returns true once per push: the pointer stayed on the edge for at
        // least DwellMs and then produced PushEvents more movements against
        // it.  Injected input and drags (button held) never trigger.
        public bool Feed(
            HandoffRect bounds,
            InputHandoffEdge edge,
            int x,
            int y,
            long timeMs,
            bool injected,
            bool buttonDown)
        {
            if (injected) return false;
            if (!_armed)
            {
                if (DistanceFromEdge(bounds, edge, x, y) >= RearmDistance)
                    _armed = true;
                return false;
            }
            if (buttonDown || !IsAtEdge(bounds, edge, x, y))
            {
                _touchStartMs = -1;
                _pushEvents = 0;
                return false;
            }
            if (_touchStartMs < 0)
            {
                _touchStartMs = timeMs;
                _pushEvents = 0;
                return false;
            }
            if (timeMs - _touchStartMs < DwellMs) return false;
            _pushEvents++;
            if (_pushEvents < PushEvents) return false;
            Reset(true);
            return true;
        }

        // Fraction (0..1) of the position along the edge.
        public static double RatioAlongEdge(
            HandoffRect bounds,
            InputHandoffEdge edge,
            int x,
            int y)
        {
            double ratio = edge == InputHandoffEdge.Left ||
                edge == InputHandoffEdge.Right
                ? (y - bounds.Top) / (double)Math.Max(1, bounds.Height - 1)
                : (x - bounds.Left) / (double)Math.Max(1, bounds.Width - 1);
            return Math.Max(0, Math.Min(1, ratio));
        }

        // Where the PC pointer reappears after a return: the same edge, at
        // the same height (or width ratio), moved inward so that the return
        // itself cannot re-trigger a handoff.
        public static HandoffPoint ReturnPoint(
            HandoffRect bounds,
            InputHandoffEdge edge,
            double ratio,
            int inset)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            var along = edge == InputHandoffEdge.Left ||
                edge == InputHandoffEdge.Right
                ? bounds.Top + (int)Math.Round(ratio * (bounds.Height - 1))
                : bounds.Left + (int)Math.Round(ratio * (bounds.Width - 1));
            switch (edge)
            {
                case InputHandoffEdge.Left:
                    return new HandoffPoint(bounds.Left + inset, along);
                case InputHandoffEdge.Right:
                    return new HandoffPoint(bounds.Right - 1 - inset, along);
                case InputHandoffEdge.Top:
                    return new HandoffPoint(along, bounds.Top + inset);
                default:
                    return new HandoffPoint(along, bounds.Bottom - 1 - inset);
            }
        }
    }

    // Virtual phone pointer.  While the keyboard and mouse are on the
    // phone, the raw mouse movement LocalDex hands over is accumulated
    // inside the phone screen size and clamped at its edges, like the real
    // Android pointer.  Both stop at the edges, so every edge contact
    // re-synchronises them and errors do not build up.  The start position
    // is the side farthest from the return edge (unless the phone pointer
    // is known to sit on the return edge), and Android pointer acceleration
    // only makes the real pointer faster, so the virtual pointer reaches
    // the return edge no earlier than the real one: a return can need a
    // little extra push but is never premature.  A phone pointer speed
    // below normal is compensated with SpeedMultiplier (< 1).
    public sealed class PhonePointerTracker
    {
        private double _x;
        private double _y;
        private bool _knownAtReturnEdge;
        private long _touchStartMs = -1;
        private int _pushEvents;

        public PhonePointerTracker()
        {
            SpeedMultiplier = 1.0;
            DwellMs = InputHandoffEdgeDetector.DefaultDwellMs;
            PushEvents = InputHandoffEdgeDetector.DefaultPushEvents;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public double SpeedMultiplier { get; set; }
        public int DwellMs { get; set; }
        public int PushEvents { get; set; }
        public InputHandoffEdge PcEdge { get; set; }
        public double X { get { return _x; } }
        public double Y { get { return _y; } }
        public bool HasExtent { get { return Width > 0 && Height > 0; } }

        public void SetExtent(int width, int height)
        {
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
        }

        // The phone edge that leads back to the PC is the one facing it.
        public static InputHandoffEdge ReturnEdgeFor(InputHandoffEdge pcEdge)
        {
            switch (pcEdge)
            {
                case InputHandoffEdge.Left: return InputHandoffEdge.Right;
                case InputHandoffEdge.Right: return InputHandoffEdge.Left;
                case InputHandoffEdge.Top: return InputHandoffEdge.Bottom;
                default: return InputHandoffEdge.Top;
            }
        }

        public void Begin()
        {
            _touchStartMs = -1;
            _pushEvents = 0;
            var edge = ReturnEdgeFor(PcEdge);
            var atEdge = _knownAtReturnEdge;
            _knownAtReturnEdge = false;
            _x = Width / 2.0;
            _y = Height / 2.0;
            switch (edge)
            {
                case InputHandoffEdge.Left: _x = atEdge ? 0 : Width; break;
                case InputHandoffEdge.Right: _x = atEdge ? Width : 0; break;
                case InputHandoffEdge.Top: _y = atEdge ? 0 : Height; break;
                default: _y = atEdge ? Height : 0; break;
            }
        }

        // Call after a return by edge push: the phone pointer is then known
        // to rest on the return edge.
        public void RememberAtReturnEdge()
        {
            _knownAtReturnEdge = true;
        }

        public void Forget()
        {
            _knownAtReturnEdge = false;
        }

        // Feeds one raw relative movement.  Returns true when the virtual
        // pointer rests on the return edge for DwellMs and keeps pushing
        // against it for PushEvents more movements.
        public bool Feed(int dx, int dy, long timeMs, bool buttonDown)
        {
            if (!HasExtent) return false;
            var scale = SpeedMultiplier > 0 ? SpeedMultiplier : 1.0;
            _x = Math.Max(0, Math.Min(Width, _x + dx * scale));
            _y = Math.Max(0, Math.Min(Height, _y + dy * scale));
            var edge = ReturnEdgeFor(PcEdge);
            bool atEdge;
            bool pushing;
            switch (edge)
            {
                case InputHandoffEdge.Left: atEdge = _x <= 0; pushing = dx < 0; break;
                case InputHandoffEdge.Right: atEdge = _x >= Width; pushing = dx > 0; break;
                case InputHandoffEdge.Top: atEdge = _y <= 0; pushing = dy < 0; break;
                default: atEdge = _y >= Height; pushing = dy > 0; break;
            }
            if (buttonDown || !atEdge)
            {
                _touchStartMs = -1;
                _pushEvents = 0;
                return false;
            }
            if (_touchStartMs < 0)
            {
                _touchStartMs = timeMs;
                _pushEvents = 0;
                return false;
            }
            if (!pushing || timeMs - _touchStartMs < DwellMs) return false;
            _pushEvents++;
            if (_pushEvents < PushEvents) return false;
            _touchStartMs = -1;
            _pushEvents = 0;
            return true;
        }
    }

    // Which phone the handoff targets.  A phone picked with a device or
    // "next phone" shortcut overrides the saved default for this run only;
    // the saved default phone (Settings) is never changed by shortcuts.
    public static class InputHandoffTargets
    {
        public static string Resolve(
            string overrideIdentity,
            string preferredIdentity,
            Func<string, bool> isConnected)
        {
            if (isConnected == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(overrideIdentity) &&
                isConnected(overrideIdentity))
            {
                return overrideIdentity.Trim();
            }
            return !string.IsNullOrWhiteSpace(preferredIdentity) &&
                isConnected(preferredIdentity)
                ? preferredIdentity.Trim()
                : string.Empty;
        }

        // Next connected phone after current in presentation order, wrapping
        // around; the first connected phone when current is not listed.
        public static string Next(
            System.Collections.Generic.IList<string> ordered,
            Func<string, bool> isConnected,
            string current)
        {
            if (ordered == null || isConnected == null) return string.Empty;
            var start = -1;
            for (var index = 0; index < ordered.Count; index++)
            {
                if (string.Equals(
                        ordered[index],
                        current,
                        StringComparison.OrdinalIgnoreCase))
                {
                    start = index;
                    break;
                }
            }
            for (var step = 1; step <= ordered.Count; step++)
            {
                var candidate = ordered[(start + step + ordered.Count) % ordered.Count];
                if (!string.IsNullOrWhiteSpace(candidate) && isConnected(candidate))
                    return candidate;
            }
            return string.Empty;
        }
    }

    public static class PhoneScreenSize
    {
        // Parses "adb shell wm size"; an override size wins over the
        // physical size.
        public static bool TryParseWmSize(
            string output,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(output)) return false;
            var match = System.Text.RegularExpressions.Regex.Match(
                output,
                @"Override size:\s*(\d+)x(\d+)");
            if (!match.Success)
            {
                match = System.Text.RegularExpressions.Regex.Match(
                    output,
                    @"Physical size:\s*(\d+)x(\d+)");
            }
            if (!match.Success) return false;
            int parsedWidth;
            int parsedHeight;
            if (!int.TryParse(match.Groups[1].Value, out parsedWidth) ||
                !int.TryParse(match.Groups[2].Value, out parsedHeight))
            {
                return false;
            }
            width = parsedWidth;
            height = parsedHeight;
            return width > 0 && height > 0;
        }
    }

    // Platform side of the handoff (Windows hooks and the scrcpy process).
    // Every call is made on the UI thread, never from inside a hook.
    public interface IInputHandoffHost
    {
        // Starts the background control-only scrcpy session for the serial.
        bool StartSession(string serial);
        void StopSession();
        // Brings the session window forward and captures the pointer.
        bool EnterPhone();
        // Releases any cursor clip, hides the session window and moves the
        // PC pointer to the given point.  Must always succeed.
        void ReleaseToPc(HandoffPoint point);
        void Log(string resourceKey);
    }

    // State machine: Off -> Waiting -> Ready -> OnPhone -> Ready.  Any loss
    // of the session or of the target while OnPhone returns the pointer to
    // the PC first, before anything else happens.
    public sealed class InputHandoffController
    {
        public const int ReturnInset = 24;
        public const int RestartDelayMs = 3000;
        public const int MaxQuickFailures = 3;

        private readonly IInputHandoffHost _host;
        private readonly InputHandoffEdgeDetector _detector =
            new InputHandoffEdgeDetector();
        private bool _enabled;
        private InputHandoffEdge _edge = InputHandoffEdge.Right;
        private HandoffRect _bounds = new HandoffRect(0, 0, 1920, 1080);
        private string _sessionSerial = string.Empty;
        private double _exitRatio = 0.5;
        private bool _hasExactReturn;
        private HandoffPoint _exactReturn;
        private long _restartNotBeforeMs;
        private int _failures;
        private bool _gaveUpLogged;
        private long _sessionStartedMs;
        // A session that ran this long counts as healthy again.
        public const int HealthySessionMs = 60000;
        private readonly PhonePointerTracker _phone = new PhonePointerTracker();

        public InputHandoffController(IInputHandoffHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            _host = host;
            State = InputHandoffState.Off;
        }

        public InputHandoffState State { get; private set; }
        public InputHandoffEdge Edge { get { return _edge; } }
        public HandoffRect Bounds { get { return _bounds; } }
        public string SessionSerial { get { return _sessionSerial; } }
        public PhonePointerTracker Phone { get { return _phone; } }

        // Phone screen size (read once per session) and the speed factor
        // of the virtual phone pointer.  Without a size, returning by edge
        // push is unavailable and only the hotkey returns.
        public void SetPhoneExtent(int width, int height)
        {
            _phone.SetExtent(width, height);
        }

        public void SetPhoneSpeed(double multiplier)
        {
            _phone.SpeedMultiplier = multiplier > 0 ? multiplier : 1.0;
        }

        public void Configure(
            bool enabled,
            InputHandoffEdge edge,
            HandoffRect bounds)
        {
            var changed = enabled != _enabled || edge != _edge;
            _bounds = bounds;
            _edge = edge;
            _phone.PcEdge = edge;
            _enabled = enabled;
            if (!changed) return;
            _detector.Reset(true);
            if (!enabled) Shutdown();
        }

        public void UpdateBounds(HandoffRect bounds)
        {
            _bounds = bounds;
        }

        // Called periodically and on every device change.  targetSerial is
        // the authorized USB serial of the current target phone (empty when
        // it is not connected).  DeX or single-app windows on the same phone
        // do not pause the handoff: they are separate windows.
        public void UpdateTarget(string targetSerial, long nowMs)
        {
            if (!_enabled)
            {
                Shutdown();
                return;
            }
            var serial = string.IsNullOrWhiteSpace(targetSerial)
                ? string.Empty
                : targetSerial.Trim();
            if (serial.Length == 0)
            {
                // Reconnecting the phone allows new attempts.
                _failures = 0;
                _gaveUpLogged = false;
                if (State != InputHandoffState.Waiting ||
                    _sessionSerial.Length > 0)
                {
                    StopAll("Log.Handoff.TargetMissing");
                }
                State = InputHandoffState.Waiting;
                return;
            }
            if (_sessionSerial.Length > 0 && !string.Equals(
                    _sessionSerial,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                // Target device changed (other phone, or USB <-> wireless).
                StopAll("Log.Handoff.TargetChanged");
                _failures = 0;
            }
            if (_sessionSerial.Length > 0) return;
            if (nowMs < _restartNotBeforeMs) return;
            if (_failures >= MaxQuickFailures)
            {
                if (!_gaveUpLogged)
                {
                    _gaveUpLogged = true;
                    _host.Log("Log.Handoff.GaveUp");
                }
                return;
            }
            State = InputHandoffState.Waiting;
            if (_host.StartSession(serial))
            {
                _sessionSerial = serial;
                _sessionStartedMs = nowMs;
                _detector.Reset(true);
                State = InputHandoffState.Ready;
                _host.Log("Log.Handoff.Ready");
            }
            else
            {
                _failures++;
                _restartNotBeforeMs = nowMs + RestartDelayMs;
            }
        }

        // Called from the low-level mouse hook.  Pure: no host calls.  When
        // it returns true the caller posts EnterPhone to the UI thread.
        public bool OnPointerMoved(
            int x,
            int y,
            long timeMs,
            bool injected,
            bool buttonDown)
        {
            if (State != InputHandoffState.Ready) return false;
            return _detector.Feed(
                _bounds,
                _edge,
                x,
                y,
                timeMs,
                injected,
                buttonDown);
        }

        public bool EnterPhone(int x, int y)
        {
            return EnterPhone(x, y, true);
        }

        // fromEdge = false for the "go to phone" and device shortcuts: the
        // pointer then comes back to exactly where it was (except after a
        // return by edge push, which uses the edge).
        public bool EnterPhone(int x, int y, bool fromEdge)
        {
            if (State != InputHandoffState.Ready) return false;
            _exitRatio = InputHandoffEdgeDetector.RatioAlongEdge(
                _bounds,
                _edge,
                x,
                y);
            _hasExactReturn = !fromEdge;
            _exactReturn = new HandoffPoint(x, y);
            State = InputHandoffState.OnPhone;
            _phone.Begin();
            bool entered;
            try
            {
                entered = _host.EnterPhone();
            }
            catch
            {
                entered = false;
            }
            if (!entered)
            {
                ReturnToPc("Log.Handoff.EnterFailed");
                return false;
            }
            _host.Log("Log.Handoff.Entered");
            return true;
        }

        // Raw relative mouse movement while on the phone (not a hook; still
        // pure).  True when the caller should post ReturnByEdge.
        public bool OnPhoneMouseDelta(
            int dx,
            int dy,
            long timeMs,
            bool buttonDown)
        {
            if (State != InputHandoffState.OnPhone) return false;
            return _phone.Feed(dx, dy, timeMs, buttonDown);
        }

        public bool ReturnByEdge()
        {
            _hasExactReturn = false;
            if (!ReturnToPc("Log.Handoff.ReturnedByEdge")) return false;
            _phone.RememberAtReturnEdge();
            return true;
        }

        // Hotkey, capture released on the phone side, or watchdog.
        public bool ReturnToPc(string reasonKey)
        {
            if (State != InputHandoffState.OnPhone) return false;
            _phone.Forget();
            _host.ReleaseToPc(_hasExactReturn
                ? _exactReturn
                : InputHandoffEdgeDetector.ReturnPoint(
                    _bounds,
                    _edge,
                    _exitRatio,
                    ReturnInset));
            _hasExactReturn = false;
            _detector.Reset(true);
            State = _sessionSerial.Length > 0
                ? InputHandoffState.Ready
                : InputHandoffState.Waiting;
            if (!string.IsNullOrEmpty(reasonKey)) _host.Log(reasonKey);
            return true;
        }

        // scrcpy ended (crash, cable pulled, phone locked out...).
        public void OnSessionExited(long nowMs)
        {
            ReturnToPc("Log.Handoff.SessionEnded");
            if (_sessionSerial.Length == 0) return;
            _sessionSerial = string.Empty;
            _phone.SetExtent(0, 0);
            _host.StopSession();
            if (nowMs - _sessionStartedMs >= HealthySessionMs) _failures = 0;
            _failures++;
            _restartNotBeforeMs = nowMs + RestartDelayMs;
            if (State != InputHandoffState.Off)
                State = InputHandoffState.Waiting;
        }

        public void Shutdown()
        {
            StopAll(null);
            State = InputHandoffState.Off;
            _failures = 0;
            _restartNotBeforeMs = 0;
            _gaveUpLogged = false;
        }

        private void StopAll(string reasonKey)
        {
            // The pointer always comes back before the session goes away.
            ReturnToPc(reasonKey);
            if (_sessionSerial.Length > 0)
            {
                _sessionSerial = string.Empty;
                _phone.Forget();
                _phone.SetExtent(0, 0);
                _host.StopSession();
            }
            if (State == InputHandoffState.Ready)
                State = InputHandoffState.Waiting;
        }
    }
}
