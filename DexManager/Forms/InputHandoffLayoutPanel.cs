using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DexManager.Models;
using DexManager.Services;

namespace DexManager.Forms
{
    // Visual monitor-layout picker for the handoff.  It draws the real
    // Windows monitors (Screen.AllScreens) to scale and lets the user drag a
    // phone rectangle onto a monitor edge; snapping to an edge attaches the
    // handoff to that monitor + edge (auto), dropping it away detaches it
    // (manual, shortcut only).  The geometry is resolved by the pure
    // InputHandoffLayout so it is unit-tested.
    internal sealed class InputHandoffLayoutPanel : Control
    {
        private const int Pad = 16;
        private ThemePalette _theme = ThemeColors.Current;
        private Screen[] _screens = Screen.AllScreens;
        private HandoffRect[] _monitors = new HandoffRect[0];
        private HandoffRect _phone;
        private int _phoneW;
        private int _phoneH;
        private double _scale = 1;
        private double _offX;
        private double _offY;
        private bool _dragging;
        private Point _dragStart;
        private HandoffRect _phoneAtDragStart;
        private string _deviceName = string.Empty;
        private InputHandoffEdge _edge = InputHandoffEdge.Right;
        private bool _attached = true;

        public InputHandoffLayoutPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            Height = 220;
        }

        public event EventHandler LayoutChanged;

        public string SelectedDeviceName { get { return _attached ? _deviceName : string.Empty; } }
        public InputHandoffEdge SelectedEdge { get { return _edge; } }
        public bool Attached { get { return _attached; } }

        public void ApplyTheme(ThemePalette theme)
        {
            if (theme != null) _theme = theme;
            Invalidate();
        }

        public void ReloadMonitors()
        {
            _screens = Screen.AllScreens;
            BuildMonitors();
            RecomputeTransform();
            Invalidate();
        }

        public void LoadFrom(InputHandoffSettings settings)
        {
            _screens = Screen.AllScreens;
            BuildMonitors();
            var primaryHeight = Screen.PrimaryScreen.Bounds.Height;
            _phoneH = Math.Max(120, primaryHeight / 3);
            _phoneW = Math.Max(60, (int)(_phoneH * 0.5));

            _edge = settings.Edge;
            _attached = settings.MonitorAttached;
            _deviceName = settings.MonitorDeviceName ?? string.Empty;

            var index = FindScreenIndex(_deviceName);
            if (_attached && index >= 0)
            {
                _phone = InputHandoffLayout.SnapFlush(
                    _monitors[index], _edge, _phoneW, _phoneH);
            }
            else if (_attached && string.IsNullOrEmpty(_deviceName))
            {
                // "Cursor monitor" default from localdex.9: dock to the
                // primary monitor's saved edge for a sensible picture.
                var primary = FindPrimaryIndex();
                _phone = InputHandoffLayout.SnapFlush(
                    _monitors[primary], _edge, _phoneW, _phoneH);
            }
            else
            {
                PlaceDetached();
            }
            RecomputeTransform();
            Invalidate();
        }

        public void ResetPhone()
        {
            _attached = true;
            var primary = FindPrimaryIndex();
            _edge = InputHandoffEdge.Right;
            _deviceName = _screens[primary].DeviceName;
            _phone = InputHandoffLayout.SnapFlush(
                _monitors[primary], _edge, _phoneW, _phoneH);
            RecomputeTransform();
            Invalidate();
            OnLayoutChanged();
        }

        private void PlaceDetached()
        {
            // Off to the right of the whole layout, not touching.
            var union = UnionBounds(false);
            _phone = new HandoffRect(
                union.Right + Math.Max(40, _phoneW),
                union.Top + (union.Height - _phoneH) / 2,
                _phoneW,
                _phoneH);
        }

        private void BuildMonitors()
        {
            _monitors = new HandoffRect[_screens.Length];
            for (var i = 0; i < _screens.Length; i++)
            {
                var b = _screens[i].Bounds;
                _monitors[i] = new HandoffRect(b.Left, b.Top, b.Width, b.Height);
            }
        }

        private int FindScreenIndex(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return -1;
            for (var i = 0; i < _screens.Length; i++)
            {
                if (_screens[i].DeviceName == deviceName) return i;
            }
            return -1;
        }

        private int FindPrimaryIndex()
        {
            for (var i = 0; i < _screens.Length; i++)
            {
                if (_screens[i].Primary) return i;
            }
            return 0;
        }

        private HandoffRect UnionBounds(bool includePhone)
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;
            foreach (var m in _monitors)
            {
                left = Math.Min(left, m.Left);
                top = Math.Min(top, m.Top);
                right = Math.Max(right, m.Right);
                bottom = Math.Max(bottom, m.Bottom);
            }
            if (includePhone)
            {
                left = Math.Min(left, _phone.Left);
                top = Math.Min(top, _phone.Top);
                right = Math.Max(right, _phone.Right);
                bottom = Math.Max(bottom, _phone.Bottom);
            }
            if (left == int.MaxValue)
                return new HandoffRect(0, 0, 1920, 1080);
            return new HandoffRect(left, top, right - left, bottom - top);
        }

        private void RecomputeTransform()
        {
            if (_monitors.Length == 0) return;
            var union = UnionBounds(true);
            var w = Math.Max(1, Width - 2 * Pad);
            var h = Math.Max(1, Height - 2 * Pad);
            _scale = Math.Min(w / (double)union.Width, h / (double)union.Height);
            if (_scale <= 0 || double.IsInfinity(_scale)) _scale = 1;
            _offX = Pad + (w - union.Width * _scale) / 2 - union.Left * _scale;
            _offY = Pad + (h - union.Height * _scale) / 2 - union.Top * _scale;
        }

        private RectangleF ToCanvas(HandoffRect r)
        {
            return new RectangleF(
                (float)(r.Left * _scale + _offX),
                (float)(r.Top * _scale + _offY),
                (float)(r.Width * _scale),
                (float)(r.Height * _scale));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_dragging) RecomputeTransform();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var canvas = ToCanvas(_phone);
            if (canvas.Contains(e.Location))
            {
                _dragging = true;
                _dragStart = e.Location;
                _phoneAtDragStart = _phone;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            var dvx = (int)Math.Round((e.X - _dragStart.X) / _scale);
            var dvy = (int)Math.Round((e.Y - _dragStart.Y) / _scale);
            _phone = new HandoffRect(
                _phoneAtDragStart.Left + dvx,
                _phoneAtDragStart.Top + dvy,
                _phoneW,
                _phoneH);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
            ResolveAndSnap();
            RecomputeTransform();
            Invalidate();
            OnLayoutChanged();
        }

        private void ResolveAndSnap()
        {
            int index;
            InputHandoffEdge edge;
            var snapVirtual = Math.Max(24, (int)Math.Ceiling(
                InputHandoffLayout.DefaultSnapPx / _scale));
            var attached = InputHandoffLayout.ResolveAttachment(
                _monitors, _phone, snapVirtual, out index, out edge);
            if (index < 0) return;
            _edge = edge;
            _attached = attached;
            _deviceName = _screens[index].DeviceName;
            if (attached)
            {
                _phone = InputHandoffLayout.SnapFlush(
                    _monitors[index], edge, _phoneW, _phoneH);
            }
        }

        private void OnLayoutChanged()
        {
            var handler = LayoutChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_theme.CardSoft);
            if (_monitors.Length == 0) return;

            var attachedIndex = _attached ? FindScreenIndex(_deviceName) : -1;
            for (var i = 0; i < _monitors.Length; i++)
            {
                var rect = ToCanvas(_monitors[i]);
                using (var fill = new SolidBrush(_theme.CardBackground))
                using (var path = Rounded(rect, 8))
                {
                    g.FillPath(fill, path);
                    var isAttached = i == attachedIndex;
                    using (var pen = new Pen(
                        isAttached ? _theme.Accent : _theme.CardBorder,
                        isAttached ? 2.5f : 1.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                    if (isAttached) HighlightEdge(g, rect, _edge);
                }
                DrawMonitorLabel(g, rect, i);
            }

            var phoneRect = ToCanvas(_phone);
            using (var fill = new SolidBrush(_theme.CardBackground))
            using (var path = Rounded(phoneRect, 6))
            {
                g.FillPath(fill, path);
                using (var pen = new Pen(
                    _attached ? _theme.Accent : _theme.TextTertiary, 2f))
                {
                    g.DrawPath(pen, path);
                }
            }
            DrawCenteredText(g, phoneRect,
                LocalizationService.Get(_attached
                    ? "Settings.HandoffLayoutPhoneAuto"
                    : "Settings.HandoffLayoutPhoneManual"),
                _attached ? _theme.Accent : _theme.TextSecondary, 8f);
        }

        private void HighlightEdge(Graphics g, RectangleF rect, InputHandoffEdge edge)
        {
            using (var pen = new Pen(_theme.Accent, 3f))
            {
                switch (edge)
                {
                    case InputHandoffEdge.Right:
                        g.DrawLine(pen, rect.Right, rect.Top + 4, rect.Right, rect.Bottom - 4);
                        break;
                    case InputHandoffEdge.Left:
                        g.DrawLine(pen, rect.Left, rect.Top + 4, rect.Left, rect.Bottom - 4);
                        break;
                    case InputHandoffEdge.Top:
                        g.DrawLine(pen, rect.Left + 4, rect.Top, rect.Right - 4, rect.Top);
                        break;
                    default:
                        g.DrawLine(pen, rect.Left + 4, rect.Bottom, rect.Right - 4, rect.Bottom);
                        break;
                }
            }
        }

        private void DrawMonitorLabel(Graphics g, RectangleF rect, int index)
        {
            var b = _screens[index].Bounds;
            var title = LocalizationService.Format(
                "Settings.HandoffLayoutMonitor", index + 1);
            var sub = b.Width + "x" + b.Height;
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 8f))
            using (var brush = new SolidBrush(_theme.TextPrimary))
            using (var subBrush = new SolidBrush(_theme.TextSecondary))
            using (var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                var top = new RectangleF(rect.X, rect.Y + rect.Height / 2 - 20, rect.Width, 18);
                var mid = new RectangleF(rect.X, rect.Y + rect.Height / 2 - 2, rect.Width, 16);
                g.DrawString(title, titleFont, brush, top, fmt);
                g.DrawString(sub, subFont, subBrush, mid, fmt);
                if (_screens[index].Primary)
                {
                    var badge = LocalizationService.Get("Settings.HandoffLayoutPrimary");
                    var bottom = new RectangleF(rect.X, rect.Y + rect.Height / 2 + 14, rect.Width, 16);
                    using (var accent = new SolidBrush(_theme.Accent))
                        g.DrawString(badge, subFont, accent, bottom, fmt);
                }
            }
        }

        private void DrawCenteredText(Graphics g, RectangleF rect, string text, Color color, float size)
        {
            using (var font = new Font("Segoe UI", size))
            using (var brush = new SolidBrush(color))
            using (var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                g.DrawString(text, font, brush, rect, fmt);
            }
        }

        private static GraphicsPath Rounded(RectangleF rect, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
