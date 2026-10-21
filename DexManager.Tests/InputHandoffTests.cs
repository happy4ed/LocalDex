using System.Collections.Generic;
using System.IO;
using DexManager.Models;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class InputHandoffTests
    {
        private static readonly HandoffRect Screen =
            new HandoffRect(0, 0, 1920, 1080);

        [Theory]
        [InlineData(InputHandoffEdge.Left, 0, 500, true)]
        [InlineData(InputHandoffEdge.Left, -5, 500, true)]
        [InlineData(InputHandoffEdge.Left, 1, 500, false)]
        [InlineData(InputHandoffEdge.Right, 1919, 500, true)]
        [InlineData(InputHandoffEdge.Right, 1918, 500, false)]
        [InlineData(InputHandoffEdge.Top, 900, 0, true)]
        [InlineData(InputHandoffEdge.Bottom, 900, 1079, true)]
        [InlineData(InputHandoffEdge.Bottom, 900, 1078, false)]
        public void EdgeIsTheOuterScreenBoundary(
            InputHandoffEdge edge,
            int x,
            int y,
            bool expected)
        {
            Assert.Equal(expected,
                InputHandoffEdgeDetector.IsAtEdge(Screen, edge, x, y));
        }

        [Fact]
        public void APointJustInsideTheEdgeNeverCounts()
        {
            // The bug: on scaled displays the lower part of the screen was
            // treated as the bottom edge.  Only the narrow band counts now.
            var detector = Armed();
            for (var i = 0; i < 40; i++)
                Assert.False(Feed(detector, 1919 - 5, 500, 20, i * 20));
            Assert.False(detector.IsTouching);
        }

        [Fact]
        public void DwellWithoutPushingDoesNotHandOver()
        {
            var detector = Armed();
            // Parked on the edge for well over the dwell, but no outward push.
            for (var i = 0; i < 60; i++)
                Assert.False(Feed(detector, 1919, 500, 0, i * 20));
        }

        [Fact]
        public void ProgressClimbsWhileRestingOnTheEdge()
        {
            var detector = Armed();
            Feed(detector, 1919, 500, 0, 0);
            Assert.Equal(0.0, detector.Progress(0), 3);
            Assert.True(detector.Progress(InputHandoffEdgeDetector.DefaultDwellMs / 2) > 0.3);
            Assert.Equal(1.0, detector.Progress(InputHandoffEdgeDetector.DefaultDwellMs * 2), 3);
            Feed(detector, 1900, 500, 0, 5000);
            Assert.Equal(0.0, detector.Progress(5000), 3);
        }

        [Theory]
        [InlineData(InputHandoffEdge.Left, -3, 5, 3)]
        [InlineData(InputHandoffEdge.Right, 4, 5, 4)]
        [InlineData(InputHandoffEdge.Bottom, 0, 7, 7)]
        [InlineData(InputHandoffEdge.Top, 0, -6, 6)]
        public void OutwardPushIsTheComponentAgainstTheEdge(
            InputHandoffEdge edge, int dx, int dy, int expected)
        {
            Assert.Equal(expected, InputHandoffController.OutwardPush(edge, dx, dy));
        }

        [Fact]
        public void TouchingTheEdgeAloneDoesNotHandOver()
        {
            var detector = Armed();
            Assert.False(Feed(detector, 1919, 500, 0));
            // Many movements, but all inside the dwell time.
            for (var t = 10; t < InputHandoffEdgeDetector.DefaultDwellMs; t += 10)
                Assert.False(Feed(detector, 1919, 500, t));
        }

        [Fact]
        public void DwellThenPushHandsOverOnce()
        {
            var detector = Armed();
            Assert.False(Feed(detector, 1919, 500, 0));
            var triggered = 0;
            for (var i = 0; i < 10; i++)
            {
                if (Feed(detector, 1925, 500, 250 + i * 10)) triggered++;
            }
            Assert.Equal(1, triggered);
        }

        [Fact]
        public void LeavingTheEdgeOrDraggingResetsTheDwell()
        {
            var detector = Armed();
            Feed(detector, 1919, 500, 0);
            Feed(detector, 1900, 500, 100);
            for (var i = 0; i < 4; i++)
                Assert.False(Feed(detector, 1919, 500, 210 + i));

            detector = Armed();
            Feed(detector, 1919, 500, 0);
            for (var i = 0; i < 10; i++)
            {
                Assert.False(detector.Feed(
                    Screen, InputHandoffEdge.Right, 1919, 500,
                    20, 300 + i, false, true));
            }
        }

        [Fact]
        public void InjectedMovementIsIgnored()
        {
            var detector = Armed();
            for (var i = 0; i < 20; i++)
            {
                Assert.False(detector.Feed(
                    Screen, InputHandoffEdge.Right, 1919, 500,
                    20, i * 50, true, false));
            }
        }

        [Fact]
        public void AfterAReturnThePointerMustLeaveTheEdgeFirst()
        {
            var detector = new InputHandoffEdgeDetector();
            detector.Reset(true);
            for (var i = 0; i < 20; i++)
                Assert.False(Feed(detector, 1919, 500, i * 50));
            Assert.False(Feed(detector, 1919 - InputHandoffEdgeDetector.RearmDistance, 500, 2000));
            Feed(detector, 1919, 500, 2100);
            var triggered = false;
            for (var i = 0; i < 8; i++)
                triggered |= Feed(detector, 1919, 500, 20, 2200 + i * 80);
            Assert.True(triggered);
        }

        [Theory]
        [InlineData(InputHandoffEdge.Right, 0.25, 1919 - 24, 270)]
        [InlineData(InputHandoffEdge.Left, 1.0, 24, 1079)]
        [InlineData(InputHandoffEdge.Top, 0.5, 960, 24)]
        [InlineData(InputHandoffEdge.Bottom, 0.0, 0, 1079 - 24)]
        public void ReturnPointKeepsTheHeightOrWidthRatio(
            InputHandoffEdge edge,
            double ratio,
            int x,
            int y)
        {
            var point = InputHandoffEdgeDetector.ReturnPoint(Screen, edge, ratio, 24);
            Assert.Equal(x, point.X);
            Assert.Equal(y, point.Y);
            Assert.False(InputHandoffEdgeDetector.IsAtEdge(Screen, edge, point.X, point.Y));
        }

        [Fact]
        public void PhonePointerReturnsOnlyAtTheRealFacingEdgeNotTheMiddle()
        {
            // Phone right of the PC -> facing (return) edge is the phone's
            // left.  Extent is the real resolution (1080x2340), so the
            // estimate covers the whole phone and the left edge is x=0.
            var phone = new PhonePointerTracker { PcEdge = InputHandoffEdge.Right };
            phone.SetExtent(1080, 2340);
            phone.Begin();
            Assert.Equal(0, phone.X);
            // Move deep into the phone (right), near the far side.
            phone.Feed(1000, 0, 0, false);
            // Halfway back is NOT the edge: no return even while pushing.
            phone.Feed(-540, 0, 100, false);
            Assert.True(phone.ReturnDistance() > 100);
            var mid = false;
            for (var i = 0; i < 20; i++)
                mid |= phone.Feed(-10, 0, 200 + i * 40, false);
            Assert.False(mid);
            // All the way to the real left edge, hold the dwell + push.
            phone.Feed(-2000, 0, 2000, false);
            Assert.Equal(0, phone.X);
            var returned = false;
            for (var i = 0; i < 20; i++)
                returned |= phone.Feed(-10, 0, 2100 + i * 40, false);
            Assert.True(returned);
        }

        [Fact]
        public void PhonePointerEdgesResynchroniseAndOffAxisDoesNotReturn()
        {
            var phone = new PhonePointerTracker { PcEdge = InputHandoffEdge.Right };
            phone.SetExtent(1000, 1000);
            phone.Begin();
            // Overshoot the far edge clamps (re-anchors), not every move.
            phone.Feed(5000, 0, 0, false);
            Assert.Equal(1000, phone.X);
            Assert.True(phone.Reanchors > 0);
            var anchored = phone.Reanchors;
            for (var i = 0; i < 20; i++)
                Assert.False(phone.Feed(50, 0, 100 + i * 50, false));
            Assert.Equal(anchored, phone.Reanchors);
            phone.Feed(-5000, 0, 2000, false);
            Assert.Equal(0, phone.X);
            // Resting on the return edge but pushing only off-axis (no
            // outward push) never returns.
            for (var i = 0; i < 20; i++)
                Assert.False(phone.Feed(0, 5, 2300 + i * 40, false));
        }

        [Fact]
        public void PhonePointerSpeedMultiplierAndUnknownSize()
        {
            // Phone left of the PC -> facing edge is the phone's right.
            var phone = new PhonePointerTracker { PcEdge = InputHandoffEdge.Left };
            phone.Begin();
            Assert.False(phone.Feed(10000, 0, 0, false));
            phone.SetExtent(1000, 1000);
            phone.SpeedMultiplier = 0.5;
            phone.Begin();
            Assert.Equal(1000, phone.X);
            phone.Feed(-1000, 0, 0, false);
            Assert.Equal(500, phone.X);
        }

        [Fact]
        public void ParsesWmSizePreferringOverride()
        {
            int width;
            int height;
            Assert.True(PhoneScreenSize.TryParseWmSize(
                "Physical size: 1080x2340\nOverride size: 900x1950\n",
                out width, out height));
            Assert.Equal(900, width);
            Assert.Equal(1950, height);
            Assert.True(PhoneScreenSize.TryParseWmSize(
                "Physical size: 1080x2340", out width, out height));
            Assert.Equal(2340, height);
            Assert.False(PhoneScreenSize.TryParseWmSize(
                "error: no devices", out width, out height));
        }

        private static List<HandoffRect> TwoMonitors()
        {
            // Monitor 0 primary 1920x1080 at origin, monitor 1 1920x1080 to
            // its right.
            return new List<HandoffRect>
            {
                new HandoffRect(0, 0, 1920, 1080),
                new HandoffRect(1920, 0, 1920, 1080)
            };
        }

        [Fact]
        public void DraggingThePhoneOntoAMonitorRightEdgeAttachesThere()
        {
            var monitors = TwoMonitors();
            // A phone just off monitor 1's right edge, vertically overlapping.
            var phone = new HandoffRect(1920 + 1920 + 10, 400, 200, 400);
            int index;
            InputHandoffEdge edge;
            var attached = InputHandoffLayout.ResolveAttachment(
                monitors, phone, 24, out index, out edge);
            Assert.True(attached);
            Assert.Equal(1, index);
            Assert.Equal(InputHandoffEdge.Right, edge);
        }

        [Fact]
        public void DraggingThePhoneUnderMonitorZeroAttachesToItsBottom()
        {
            var monitors = TwoMonitors();
            var phone = new HandoffRect(800, 1080 + 8, 200, 400);
            int index;
            InputHandoffEdge edge;
            var attached = InputHandoffLayout.ResolveAttachment(
                monitors, phone, 24, out index, out edge);
            Assert.True(attached);
            Assert.Equal(0, index);
            Assert.Equal(InputHandoffEdge.Bottom, edge);
        }

        [Fact]
        public void DroppingThePhoneFarAwayIsDetachedButReportsTheNearest()
        {
            var monitors = TwoMonitors();
            var phone = new HandoffRect(5000, 400, 200, 400);
            int index;
            InputHandoffEdge edge;
            var attached = InputHandoffLayout.ResolveAttachment(
                monitors, phone, 24, out index, out edge);
            Assert.False(attached);
            Assert.Equal(1, index);
            Assert.Equal(InputHandoffEdge.Right, edge);
        }

        [Fact]
        public void SingleMonitorAttachesOnItsChosenEdge()
        {
            var monitors = new List<HandoffRect> { new HandoffRect(0, 0, 2560, 1440) };
            var phone = new HandoffRect(-205, 500, 200, 400); // just left
            int index;
            InputHandoffEdge edge;
            Assert.True(InputHandoffLayout.ResolveAttachment(
                monitors, phone, 24, out index, out edge));
            Assert.Equal(0, index);
            Assert.Equal(InputHandoffEdge.Left, edge);
        }

        [Fact]
        public void SnapFlushDocksThePhoneAgainstTheEdge()
        {
            var monitor = new HandoffRect(0, 0, 1920, 1080);
            var right = InputHandoffLayout.SnapFlush(
                monitor, InputHandoffEdge.Right, 200, 400);
            Assert.Equal(1920, right.Left);
            Assert.Equal((1080 - 400) / 2, right.Top);
            var bottom = InputHandoffLayout.SnapFlush(
                monitor, InputHandoffEdge.Bottom, 200, 400);
            Assert.Equal(1080, bottom.Top);
            Assert.Equal((1920 - 200) / 2, bottom.Left);
        }

        [Fact]
        public void EdgeDetectionWorksOnASecondMonitorBottom()
        {
            // A second monitor to the right, taller, with its own origin:
            // handoff must use that monitor's own bottom edge, not the
            // virtual desktop. Bounds are passed per-monitor by the host.
            var monitorB = new HandoffRect(1920, 0, 2560, 1440);
            var detector = new InputHandoffEdgeDetector();
            detector.Reset(false);
            // A point near the virtual-desktop bottom (1079) but well inside
            // monitor B must NOT count as that monitor's bottom edge.
            Assert.False(detector.Feed(
                monitorB, InputHandoffEdge.Bottom, 3000, 1079, 20, 0, false, false));
            Assert.False(detector.IsTouching);
            // Monitor B's own bottom (1439) does.
            Assert.False(detector.Feed(
                monitorB, InputHandoffEdge.Bottom, 3000, 1439, 0, 10, false, false));
            var returned = false;
            for (var i = 0; i < 12; i++)
            {
                returned |= detector.Feed(
                    monitorB, InputHandoffEdge.Bottom, 3000, 1439, 20,
                    10 + 400 + i * 40, false, false);
            }
            Assert.True(returned);
        }

        [Fact]
        public void PhoneNearReturnEdgeTracksTheVirtualPointer()
        {
            var host = new FakeHost();
            var controller = ReadyController(host);  // edge = Right
            controller.SetPhoneExtent(1000, 1000);
            controller.EnterPhone(1919, 500);
            // Arm by going into the phone; now far from the facing edge.
            controller.OnPhoneMouseDelta(400, 0, 100, false);
            Assert.False(controller.PhoneNearReturnEdge);
            // Move back to the facing (left) edge.
            controller.OnPhoneMouseDelta(-500, 0, 200, false);
            Assert.True(controller.PhoneNearReturnEdge);
            Assert.Contains("returnDist=", controller.PhoneReturnDebug());
        }

        [Fact]
        public void StatesGoWaitingReadyOnPhoneAndBack()
        {
            var host = new FakeHost();
            var controller = new InputHandoffController(host);
            Assert.Equal(InputHandoffState.Off, controller.State);
            controller.Configure(true, InputHandoffEdge.Right, Screen);
            controller.UpdateTarget(string.Empty, 0);
            Assert.Equal(InputHandoffState.Waiting, controller.State);
            Assert.Empty(host.Started);

            controller.UpdateTarget("usb-b", 10);
            Assert.Equal(InputHandoffState.Ready, controller.State);
            Assert.Equal(new[] { "usb-b" }, host.Started);

            PushToEdge(controller, 270);
            Assert.True(controller.EnterPhone(1919, 270));
            Assert.Equal(InputHandoffState.OnPhone, controller.State);
            Assert.False(controller.OnPointerMoved(1919, 270, 0, 5000, false, false));

            Assert.True(controller.ReturnToPc("Log.Handoff.ReturnedByHotkey"));
            Assert.Equal(InputHandoffState.Ready, controller.State);
            Assert.Equal(1919 - InputHandoffController.ReturnInset, host.Released[0].X);
            Assert.Equal(270, host.Released[0].Y);
            Assert.Equal(0, host.Stopped);
        }

        [Fact]
        public void ReturnByPhoneEdgeUsesTheVirtualPointer()
        {
            var host = new FakeHost();
            var controller = ReadyController(host);
            controller.SetPhoneExtent(1000, 1000);
            PushToEdge(controller, 540);
            controller.EnterPhone(1919, 540);
            // Arm by moving into the phone (right), then push back to the
            // facing (left) edge to return.
            controller.OnPhoneMouseDelta(300, 0, 9000, false);
            Assert.False(controller.PhoneNearReturnEdge);
            var returned = false;
            for (var i = 0; i < 60 && !returned; i++)
                returned = controller.OnPhoneMouseDelta(-100, 0, 10000 + i * 40, false);
            Assert.True(returned);
            Assert.True(controller.ReturnByEdge());
            Assert.Equal(InputHandoffState.Ready, controller.State);
            Assert.Single(host.Released);
        }

        [Fact]
        public void SessionExitWhileOnPhoneReleasesThePointerFirst()
        {
            var host = new FakeHost();
            var controller = ReadyController(host);
            PushToEdge(controller, 100);
            controller.EnterPhone(1919, 100);

            controller.OnSessionExited(20000);
            Assert.Equal(new[] { "release", "stop" }, host.Calls.GetRange(host.Calls.Count - 2, 2));
            Assert.Equal(InputHandoffState.Waiting, controller.State);
            // Restarted only after the delay.
            controller.UpdateTarget("usb-b", 20000 + 100);
            Assert.Single(host.Started);
            controller.UpdateTarget("usb-b", 20000 + InputHandoffController.RestartDelayMs);
            Assert.Equal(2, host.Started.Count);
            Assert.Equal(InputHandoffState.Ready, controller.State);
        }

        [Fact]
        public void DeviceRemovedOrChangedWhileOnPhoneReleasesThePointer()
        {
            var host = new FakeHost();
            var controller = ReadyController(host);
            PushToEdge(controller, 100);
            controller.EnterPhone(1919, 100);
            controller.UpdateTarget(string.Empty, 30000);
            Assert.Equal("release", host.Calls[host.Calls.Count - 2]);
            Assert.Equal("stop", host.Calls[host.Calls.Count - 1]);
            Assert.Equal(InputHandoffState.Waiting, controller.State);

            controller.UpdateTarget("usb-b", 30010);
            PushToEdge(controller, 100);
            controller.EnterPhone(1919, 100);
            host.Calls.Clear();
            controller.UpdateTarget("usb-c", 40000);
            Assert.Equal(new[] { "release", "stop", "start:usb-c" }, host.Calls);
            Assert.Equal(InputHandoffState.Ready, controller.State);
        }

        [Fact]
        public void ShortcutEntryReturnsToTheExactPointButEdgeReturnUsesTheEdge()
        {
            var host = new FakeHost();
            var controller = ReadyController(host);
            Assert.True(controller.EnterPhone(700, 400, false));
            Assert.Equal(InputHandoffState.OnPhone, controller.State);
            controller.ReturnToPc("Log.Handoff.ReturnedByHotkey");
            Assert.Equal(700, host.Released[0].X);
            Assert.Equal(400, host.Released[0].Y);

            controller.SetPhoneExtent(1000, 1000);
            Assert.True(controller.EnterPhone(700, 540, false));
            Assert.True(controller.ReturnByEdge());
            Assert.Equal(1919 - InputHandoffController.ReturnInset, host.Released[1].X);
            Assert.Equal(540, host.Released[1].Y);
        }

        [Fact]
        public void ShortcutTargetOverridesOnlyWhileConnected()
        {
            var connected = new HashSet<string> { "phone-a", "phone-b" };
            Assert.Equal("phone-b", InputHandoffTargets.Resolve(
                "phone-b", "phone-a", connected.Contains));
            Assert.Equal("phone-a", InputHandoffTargets.Resolve(
                string.Empty, "phone-a", connected.Contains));
            connected.Remove("phone-b");
            Assert.Equal("phone-a", InputHandoffTargets.Resolve(
                "phone-b", "phone-a", connected.Contains));
            connected.Remove("phone-a");
            Assert.Equal(string.Empty, InputHandoffTargets.Resolve(
                "phone-b", "phone-a", connected.Contains));
        }

        [Fact]
        public void NextPhoneCyclesThroughConnectedPhones()
        {
            var ordered = new List<string> { "phone-a", "phone-b", "phone-c" };
            var connected = new HashSet<string>(ordered);
            Assert.Equal("phone-b", InputHandoffTargets.Next(ordered, connected.Contains, "phone-a"));
            Assert.Equal("phone-a", InputHandoffTargets.Next(ordered, connected.Contains, "phone-c"));
            Assert.Equal("phone-a", InputHandoffTargets.Next(ordered, connected.Contains, string.Empty));
            connected.Remove("phone-b");
            Assert.Equal("phone-c", InputHandoffTargets.Next(ordered, connected.Contains, "phone-a"));
            connected.Clear();
            connected.Add("phone-c");
            Assert.Equal("phone-c", InputHandoffTargets.Next(ordered, connected.Contains, "phone-c"));
            connected.Clear();
            Assert.Equal(string.Empty, InputHandoffTargets.Next(ordered, connected.Contains, "phone-c"));
        }

        [Fact]
        public void HandoffShortcutsKeepNoneAndPerPhoneValues()
        {
            var root = Path.Combine(Path.GetTempPath(), "localdex-hotkeys-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var service = new SettingsService(new LogService(), root);
                var settings = AppSettings.CreateDefault();
                settings.EnsureDefaults();
                Assert.Equal(InputHandoffSettings.DefaultEnterHotkey, settings.InputHandoff.EnterHotkey);
                Assert.Equal(InputHandoffSettings.DefaultNextDeviceHotkey, settings.InputHandoff.NextDeviceHotkey);
                service.UpdateAndSave(settings, s =>
                {
                    s.InputHandoff.ReturnHotkey = string.Empty;
                    s.InputHandoff.SetDeviceHotkey("phone-b", "Ctrl+Alt+F2");
                    s.InputHandoff.SetDeviceHotkey("phone-c", "Ctrl+Alt+F3");
                    s.InputHandoff.SetDeviceHotkey("phone-c", string.Empty);
                });
                var reloaded = service.Load();
                Assert.Equal(string.Empty, reloaded.InputHandoff.ReturnHotkey);
                Assert.Equal("Ctrl+Alt+F2", reloaded.InputHandoff.GetDeviceHotkey("PHONE-B"));
                Assert.Equal(string.Empty, reloaded.InputHandoff.GetDeviceHotkey("phone-c"));
                Assert.Single(reloaded.InputHandoff.DeviceHotkeys);

                // Settings written by localdex.5 have no enter/next fields.
                var old = new InputHandoffSettings { ReturnHotkey = "Ctrl+Alt+Home" };
                old.Normalize();
                Assert.Equal(InputHandoffSettings.DefaultEnterHotkey, old.EnterHotkey);
                Assert.Empty(old.DeviceHotkeys);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void FailedEnterKeepsThePointerOnThePc()
        {
            var host = new FakeHost { EnterResult = false };
            var controller = ReadyController(host);
            PushToEdge(controller, 100);
            Assert.False(controller.EnterPhone(1919, 100));
            Assert.Equal(InputHandoffState.Ready, controller.State);
            Assert.Single(host.Released);
        }

        [Fact]
        public void RepeatedStartFailuresStopRetrying()
        {
            var host = new FakeHost { StartResult = false };
            var controller = new InputHandoffController(host);
            controller.Configure(true, InputHandoffEdge.Right, Screen);
            for (var i = 0; i < 10; i++)
                controller.UpdateTarget("usb-b", i * InputHandoffController.RestartDelayMs);
            Assert.Equal(InputHandoffController.MaxQuickFailures, host.Started.Count);
            Assert.Equal(InputHandoffState.Waiting, controller.State);
        }

        [Fact]
        public void DisablingWhileOnPhoneReleasesAndStops()
        {
            var host = new FakeHost();
            var controller = ReadyController(host);
            PushToEdge(controller, 100);
            controller.EnterPhone(1919, 100);
            controller.Configure(false, InputHandoffEdge.Right, Screen);
            Assert.Equal(InputHandoffState.Off, controller.State);
            Assert.Equal(new[] { "release", "stop" }, host.Calls.GetRange(host.Calls.Count - 2, 2));
        }

        [Fact]
        public void HandoffSettingsDefaultAndSurviveSaveAndReload()
        {
            var root = Path.Combine(Path.GetTempPath(), "localdex-handoff-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var log = new LogService();
                var service = new SettingsService(log, root);
                var settings = AppSettings.CreateDefault();
                settings.EnsureDefaults();
                Assert.False(settings.InputHandoff.Enabled);
                Assert.Equal(InputHandoffEdge.Right, settings.InputHandoff.Edge);
                Assert.Equal(InputHandoffSettings.DefaultReturnHotkey, settings.InputHandoff.ReturnHotkey);
                service.UpdateAndSave(settings, s =>
                {
                    s.InputHandoff.Enabled = true;
                    s.InputHandoff.Edge = InputHandoffEdge.Top;
                    s.InputHandoff.PhonePointerSpeedPercent = 5000;
                });
                var reloaded = service.Load();
                Assert.True(reloaded.InputHandoff.Enabled);
                Assert.Equal(InputHandoffEdge.Top, reloaded.InputHandoff.Edge);
                Assert.Equal(100, reloaded.InputHandoff.PhonePointerSpeedPercent);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static InputHandoffEdgeDetector Armed()
        {
            var detector = new InputHandoffEdgeDetector();
            detector.Reset(false);
            return detector;
        }

        private static bool Feed(InputHandoffEdgeDetector detector, int x, int y, long t)
        {
            return Feed(detector, x, y, 20, t);
        }

        private static bool Feed(InputHandoffEdgeDetector detector, int x, int y, int push, long t)
        {
            return detector.Feed(
                Screen, InputHandoffEdge.Right, x, y, push, t, false, false);
        }

        private static InputHandoffController ReadyController(FakeHost host)
        {
            var controller = new InputHandoffController(host);
            controller.Configure(true, InputHandoffEdge.Right, Screen);
            controller.UpdateTarget("usb-b", 0);
            Assert.Equal(InputHandoffState.Ready, controller.State);
            return controller;
        }

        // The detector starts disarmed after a (re)start: leave the edge,
        // then dwell and push.
        private static void PushToEdge(InputHandoffController controller, int y)
        {
            // Leave the edge, then dwell and keep pushing outward.
            controller.OnPointerMoved(1800, y, 0, 100000, false, false);
            controller.OnPointerMoved(1919, y, 0, 100010, false, false);
            var triggered = false;
            for (var i = 0; i < 20 && !triggered; i++)
                triggered = controller.OnPointerMoved(1919, y, 20, 100400 + i * 20, false, false);
            Assert.True(triggered);
        }

        private sealed class FakeHost : IInputHandoffHost
        {
            public bool StartResult = true;
            public bool EnterResult = true;
            public readonly List<string> Started = new List<string>();
            public readonly List<HandoffPoint> Released = new List<HandoffPoint>();
            public readonly List<string> Calls = new List<string>();
            public int Stopped;

            public bool StartSession(string serial)
            {
                Started.Add(serial);
                Calls.Add("start:" + serial);
                return StartResult;
            }

            public void StopSession()
            {
                Stopped++;
                Calls.Add("stop");
            }

            public bool EnterPhone()
            {
                Calls.Add("enter");
                return EnterResult;
            }

            public void ReleaseToPc(HandoffPoint point)
            {
                Released.Add(point);
                Calls.Add("release");
            }

            public void Log(string resourceKey)
            {
            }
        }
    }
}
