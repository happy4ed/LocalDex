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
                    300 + i, false, true));
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
                    i * 50, true, false));
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
            for (var i = 0; i < 6; i++)
                triggered |= Feed(detector, 1919, 500, 2400 + i);
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
        public void PhonePointerReturnsOnlyAfterReachingTheFacingEdge()
        {
            var phone = new PhonePointerTracker { PcEdge = InputHandoffEdge.Right };
            phone.SetExtent(2340, 2340);
            phone.Begin();
            // Phone right of the PC: start on the far (right) side.
            Assert.Equal(2340, phone.X);
            // Moving left 2000 px does not reach the left edge yet.
            for (var i = 0; i < 100; i++)
                Assert.False(phone.Feed(-20, 0, i, false));
            // Reach the edge, dwell, keep pushing.
            Assert.False(phone.Feed(-400, 0, 200, false));
            var returned = false;
            for (var i = 0; i < 8; i++)
                returned |= phone.Feed(-5, 0, 500 + i * 10, false);
            Assert.True(returned);
        }

        [Fact]
        public void PhonePointerEdgesResynchroniseAndRightMovesDoNotReturn()
        {
            var phone = new PhonePointerTracker { PcEdge = InputHandoffEdge.Right };
            phone.SetExtent(1000, 1000);
            phone.Begin();
            // Overshooting the far edge clamps (like the real pointer).
            phone.Feed(5000, 0, 0, false);
            Assert.Equal(1000, phone.X);
            for (var i = 0; i < 20; i++)
                Assert.False(phone.Feed(50, 0, 100 + i * 50, false));
            phone.Feed(-1000, 0, 2000, false);
            Assert.Equal(0, phone.X);
            // Pushing away from the return edge never returns.
            for (var i = 0; i < 20; i++)
                Assert.False(phone.Feed(0, 5, 2300 + i * 20, false));
        }

        [Fact]
        public void PhonePointerSpeedMultiplierAndUnknownSize()
        {
            var phone = new PhonePointerTracker { PcEdge = InputHandoffEdge.Left };
            phone.Begin();
            Assert.False(phone.Feed(10000, 0, 0, false));
            phone.SetExtent(1000, 1000);
            phone.SpeedMultiplier = 0.5;
            phone.Begin();
            Assert.Equal(0, phone.X);
            phone.Feed(1000, 0, 0, false);
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

        [Fact]
        public void StatesGoWaitingReadyOnPhoneAndBack()
        {
            var host = new FakeHost();
            var controller = new InputHandoffController(host);
            Assert.Equal(InputHandoffState.Off, controller.State);
            controller.Configure(true, InputHandoffEdge.Right, Screen);
            controller.UpdateTarget(string.Empty, false, 0);
            Assert.Equal(InputHandoffState.Waiting, controller.State);
            Assert.Empty(host.Started);

            controller.UpdateTarget("usb-b", false, 10);
            Assert.Equal(InputHandoffState.Ready, controller.State);
            Assert.Equal(new[] { "usb-b" }, host.Started);

            PushToEdge(controller, 270);
            Assert.True(controller.EnterPhone(1919, 270));
            Assert.Equal(InputHandoffState.OnPhone, controller.State);
            Assert.False(controller.OnPointerMoved(1919, 270, 5000, false, false));

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
            var returned = false;
            for (var i = 0; i < 40 && !returned; i++)
                returned = controller.OnPhoneMouseDelta(-100, 0, 10000 + i * 50, false);
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
            controller.UpdateTarget("usb-b", false, 20000 + 100);
            Assert.Single(host.Started);
            controller.UpdateTarget("usb-b", false, 20000 + InputHandoffController.RestartDelayMs);
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
            controller.UpdateTarget(string.Empty, false, 30000);
            Assert.Equal("release", host.Calls[host.Calls.Count - 2]);
            Assert.Equal("stop", host.Calls[host.Calls.Count - 1]);
            Assert.Equal(InputHandoffState.Waiting, controller.State);

            controller.UpdateTarget("usb-b", false, 30010);
            PushToEdge(controller, 100);
            controller.EnterPhone(1919, 100);
            host.Calls.Clear();
            controller.UpdateTarget("usb-c", false, 40000);
            Assert.Equal(new[] { "release", "stop", "start:usb-c" }, host.Calls);
            Assert.Equal(InputHandoffState.Ready, controller.State);
        }

        [Fact]
        public void DexOnTheSamePhonePausesTheHandoff()
        {
            var host = new FakeHost();
            var controller = ReadyController(host);
            PushToEdge(controller, 100);
            controller.EnterPhone(1919, 100);
            controller.UpdateTarget("usb-b", true, 50000);
            Assert.Equal(InputHandoffState.Waiting, controller.State);
            Assert.Equal("release", host.Calls[host.Calls.Count - 2]);
            Assert.Equal(1, host.Stopped);
            controller.UpdateTarget("usb-b", true, 60000);
            Assert.Single(host.Started);
            controller.UpdateTarget("usb-b", false, 60010);
            Assert.Equal(InputHandoffState.Ready, controller.State);
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
                controller.UpdateTarget("usb-b", false, i * InputHandoffController.RestartDelayMs);
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
            return detector.Feed(Screen, InputHandoffEdge.Right, x, y, t, false, false);
        }

        private static InputHandoffController ReadyController(FakeHost host)
        {
            var controller = new InputHandoffController(host);
            controller.Configure(true, InputHandoffEdge.Right, Screen);
            controller.UpdateTarget("usb-b", false, 0);
            Assert.Equal(InputHandoffState.Ready, controller.State);
            return controller;
        }

        // The detector starts disarmed after a (re)start: leave the edge,
        // then dwell and push.
        private static void PushToEdge(InputHandoffController controller, int y)
        {
            controller.OnPointerMoved(1800, y, 100000, false, false);
            controller.OnPointerMoved(1919, y, 100010, false, false);
            var triggered = false;
            for (var i = 0; i < 10 && !triggered; i++)
                triggered = controller.OnPointerMoved(1925, y, 100300 + i, false, false);
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
