using System;
using System.Collections.Generic;
using DexManager.Models;
using DexManager.Services;
using DexManager.Utils;
using Xunit;

namespace DexManager.Tests
{
    // 회장님 2026-10-27: a phone plugged into this PC kept coming up as
    // "charging only". LocalDex switches it to file transfer once per plug-in;
    // the switch itself drops and restores the adb transport, which must not
    // trigger a second switch or a loop.
    public class UsbFileTransferModeServiceTests
    {
        private static UsbFileTransferModeService CreateService()
        {
            var log = new LogService();
            var adb = new AdbService("adb", 5000, new ProcessRunner(log), log);
            return new UsbFileTransferModeService(adb, () => true, log);
        }

        private static DeviceRegistrySnapshot Snapshot(params DeviceTransportInfo[] transports)
        {
            var device = new PhysicalDeviceInfo { Identity = "serial:R5KL8024M9A", DisplayName = "Fold8" };
            foreach (var t in transports) device.Transports.Add(t);
            return new DeviceRegistrySnapshot { Devices = new List<PhysicalDeviceInfo> { device } };
        }

        private static DeviceTransportInfo Usb(string serial, AdbDeviceStatus status = AdbDeviceStatus.Device)
        {
            return new DeviceTransportInfo { Serial = serial, Kind = DeviceTransportKind.Usb, Status = status };
        }

        [Theory]
        [InlineData("adb", true)]
        [InlineData("mtp,adb", true)]
        [InlineData("none", true)]
        [InlineData("ptp,adb\n", true)]
        [InlineData("", false)]
        [InlineData("/system/bin/sh: svc: not found", false)]
        [InlineData("Unknown command: getFunctions", false)]
        public void RecognisesUsbFunctionLists(string output, bool expected)
        {
            Assert.Equal(expected, UsbFileTransferModeService.LooksLikeUsbFunctions(output));
        }

        [Fact]
        public void SwitchesOnlyWhenNeededAndGivesUpIfItDoesNotStick()
        {
            Assert.Equal(UsbFileTransferModeService.UsbModeAction.Switch,
                UsbFileTransferModeService.Decide("adb", false, false));
            Assert.Equal(UsbFileTransferModeService.UsbModeAction.None,
                UsbFileTransferModeService.Decide("mtp,adb", false, false));
            // back after our own switch but still charging: stop, no loop
            Assert.Equal(UsbFileTransferModeService.UsbModeAction.GiveUp,
                UsbFileTransferModeService.Decide("adb", true, false));
            Assert.Equal(UsbFileTransferModeService.UsbModeAction.None,
                UsbFileTransferModeService.Decide("adb", false, true));
        }

        [Fact]
        public void OnlyNewlyAuthorizedUsbTransportsAreHandled()
        {
            var service = CreateService();
            var t0 = new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

            Assert.Empty(service.TakeNewUsbSerials(Snapshot(Usb("R5KL8024M9A", AdbDeviceStatus.Unauthorized)), t0));
            Assert.Equal(new[] { "R5KL8024M9A" }, service.TakeNewUsbSerials(Snapshot(Usb("R5KL8024M9A")), t0));
            // unchanged snapshot, and a wireless transport, are not "plugged in"
            Assert.Empty(service.TakeNewUsbSerials(Snapshot(Usb("R5KL8024M9A"),
                new DeviceTransportInfo { Serial = "192.168.0.5:5555", Kind = DeviceTransportKind.Wireless, Status = AdbDeviceStatus.Device }), t0));
        }

        [Fact]
        public void ReenumerationKeepsGiveUpButARealReplugClearsIt()
        {
            var service = CreateService();
            var t0 = new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);
            service.TakeNewUsbSerials(Snapshot(Usb("R5KL8024M9A")), t0);
            service.MarkGaveUp("R5KL8024M9A");

            // the switch drops the transport for a couple of seconds
            service.TakeNewUsbSerials(Snapshot(), t0.AddSeconds(1));
            service.TakeNewUsbSerials(Snapshot(Usb("R5KL8024M9A")), t0.AddSeconds(3));
            Assert.True(service.HasGivenUp("R5KL8024M9A"));

            // unplugged for a while and plugged in again: Android is back on
            // charging, so try again
            service.TakeNewUsbSerials(Snapshot(), t0.AddSeconds(10));
            Assert.Equal(new[] { "R5KL8024M9A" }, service.TakeNewUsbSerials(Snapshot(Usb("R5KL8024M9A")), t0.AddSeconds(40)));
            Assert.False(service.HasGivenUp("R5KL8024M9A"));
        }
    }
}
