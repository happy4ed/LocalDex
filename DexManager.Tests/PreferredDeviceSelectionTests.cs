using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DexManager.Models;
using DexManager.Services;
using DexManager.Utils;
using Xunit;

namespace DexManager.Tests
{
    // Regression for 2.1.0-localdex.3: the default phone saved in Settings
    // was not used; connections fell back to the first listed phone.
    public class PreferredDeviceSelectionTests
    {
        private static readonly string[] Identities =
        {
            "phone-a", "phone-b", "phone-c"
        };

        private static readonly string[] Serials =
        {
            "USBSERIAL0001", "USBSERIAL0002", "USBSERIAL0003"
        };

        [Fact]
        public void ChooseIdentityPrefersSavedDefaultOverListOrder()
        {
            var connected = new HashSet<string>(Identities);
            Assert.Equal("phone-b", DevicePresentationOrder.ChooseIdentity(
                Identities, connected.Contains, "phone-b"));
            Assert.Equal("phone-c", DevicePresentationOrder.ChooseIdentity(
                Identities, connected.Contains, "PHONE-C"));
        }

        [Fact]
        public void ChooseIdentityFallsBackToFirstOnlyWhenDefaultMissing()
        {
            var connected = new HashSet<string>(Identities);
            Assert.Equal("phone-a", DevicePresentationOrder.ChooseIdentity(
                Identities, connected.Contains, string.Empty));
            connected.Remove("phone-b");
            Assert.Equal("phone-a", DevicePresentationOrder.ChooseIdentity(
                Identities, connected.Contains, "phone-b"));
            connected.Add("phone-b");
            Assert.Equal("phone-b", DevicePresentationOrder.ChooseIdentity(
                Identities, connected.Contains, "phone-b"));
        }

        [Fact]
        public void PreferredTransportSerialUsesWirelessWhenUsbIsGone()
        {
            var devices = CreatePhysicalDevices(3);
            Assert.Equal(Serials[1],
                DevicePresentationOrder.FindPreferredTransportSerial(
                    devices, "phone-b"));

            devices[1].Transports = new List<DeviceTransportInfo>
            {
                new DeviceTransportInfo
                {
                    Serial = "wireless-endpoint-b",
                    Kind = DeviceTransportKind.Wireless,
                    Status = AdbDeviceStatus.Device
                }
            };
            Assert.Equal("wireless-endpoint-b",
                DevicePresentationOrder.FindPreferredTransportSerial(
                    devices, "phone-b"));
            Assert.Equal(string.Empty,
                DevicePresentationOrder.FindPreferredTransportSerial(
                    devices, "phone-z"));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void ConnectionTargetIsSecondPhoneWhenItIsTheDefault(int count)
        {
            using (var fixture = new Fixture(count))
            {
                fixture.Settings.PreferredDeviceIdentity = "phone-b";

                Assert.Equal(Serials[1], fixture.Service
                    .SelectPreferredDevice(fixture.AdbDevices).Serial);
                // Repeated attempts (reconnect, monitor polls) keep it.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    Assert.Equal(Serials[1], fixture.Service
                        .SelectPreferredDeviceWithGeneration(
                            fixture.AdbDevices).Device.Serial);
                    Assert.Equal(Serials[1], fixture.Service
                        .FindPreferredDevice(
                            fixture.AdbDevices,
                            fixture.Service.SelectedSerial).Serial);
                }
            }
        }

        [Fact]
        public void DefaultSurvivesSaveAndReload()
        {
            using (var fixture = new Fixture(3))
            {
                fixture.SettingsService.UpdateAndSave(
                    fixture.Settings,
                    settings => settings.PreferredDeviceIdentity = "phone-b");
                var reloaded = fixture.SettingsService.Load();
                Assert.Equal("phone-b", reloaded.PreferredDeviceIdentity);

                using (var second = new Fixture(3, reloaded))
                {
                    Assert.Equal(Serials[1], second.Service
                        .SelectPreferredDevice(second.AdbDevices).Serial);
                }
            }
        }

        [Fact]
        public void DefaultReturnsAfterReconnectAndFallbackIsNotSaved()
        {
            using (var fixture = new Fixture(3))
            {
                fixture.Settings.PreferredDeviceIdentity = "phone-b";
                Assert.Equal(Serials[1], fixture.Service
                    .SelectPreferredDevice(fixture.AdbDevices).Serial);

                fixture.Disconnect(1);
                Assert.Equal(Serials[0], fixture.Service
                    .SelectPreferredDevice(fixture.AdbDevices).Serial);
                Assert.Equal(Serials[0], fixture.Service.SelectedSerial);
                Assert.Equal("phone-b",
                    fixture.Settings.PreferredDeviceIdentity);
                Assert.Equal(1, fixture.Log.Count(line =>
                    line.Contains(LocalizationService.Get(
                        "Log.Adb.PreferredDeviceMissing"))));

                // A second poll while still missing logs nothing new.
                fixture.Service.SelectPreferredDevice(fixture.AdbDevices);
                Assert.Equal(1, fixture.Log.Count(line =>
                    line.Contains(LocalizationService.Get(
                        "Log.Adb.PreferredDeviceMissing"))));

                fixture.Reconnect(1);
                Assert.Equal(Serials[1], fixture.Service
                    .SelectPreferredDevice(fixture.AdbDevices).Serial);
                Assert.Equal(Serials[1], fixture.Service
                    .FindPreferredDevice(
                        fixture.AdbDevices,
                        fixture.Service.SelectedSerial).Serial);
            }
        }

        [Fact]
        public void WithoutDefaultFirstPhoneIsStillChosen()
        {
            using (var fixture = new Fixture(3))
            {
                Assert.Equal(string.Empty,
                    fixture.Settings.PreferredDeviceIdentity);
                Assert.Equal(Serials[0], fixture.Service
                    .SelectPreferredDevice(fixture.AdbDevices).Serial);
                Assert.DoesNotContain(fixture.Log, line =>
                    line.Contains(LocalizationService.Get(
                        "Log.Adb.PreferredDeviceMissing")));
            }
        }

        private static List<PhysicalDeviceInfo> CreatePhysicalDevices(
            int count)
        {
            var devices = new List<PhysicalDeviceInfo>();
            for (var index = 0; index < count; index++)
            {
                devices.Add(new PhysicalDeviceInfo
                {
                    Identity = Identities[index],
                    DisplayName = "Phone " + index,
                    Transports = new List<DeviceTransportInfo>
                    {
                        new DeviceTransportInfo
                        {
                            Serial = Serials[index],
                            Kind = DeviceTransportKind.Usb,
                            Status = AdbDeviceStatus.Device
                        }
                    }
                });
            }
            return devices;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _root;
            private readonly List<PhysicalDeviceInfo> _all;
            private readonly HashSet<int> _disconnected = new HashSet<int>();

            public Fixture(int count, AppSettings settings = null)
            {
                _root = Path.Combine(
                    Path.GetTempPath(),
                    "localdex-preferred-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                LogService = new LogService();
                SettingsService = new SettingsService(LogService, _root);
                Settings = settings ?? AppSettings.CreateDefault();
                Settings.EnsureDefaults();
                _all = CreatePhysicalDevices(count);
                Service = new WirelessAdbService(
                    new AdbService(
                        "adb",
                        1000,
                        new ProcessRunner(LogService),
                        LogService),
                    SettingsService,
                    Settings,
                    LogService);
                Service.SetPreferredDeviceResolver(() =>
                    DevicePresentationOrder.FindPreferredTransportSerial(
                        Connected,
                        Settings.PreferredDeviceIdentity));
            }

            public AppSettings Settings { get; }
            public SettingsService SettingsService { get; }
            public LogService LogService { get; }
            public WirelessAdbService Service { get; }
            public List<string> Log
            {
                get { return LogService.GetSessionEntries().ToList(); }
            }

            private List<PhysicalDeviceInfo> Connected
            {
                get
                {
                    return _all.Where((device, index) =>
                        !_disconnected.Contains(index)).ToList();
                }
            }

            public List<AdbDeviceInfo> AdbDevices
            {
                get
                {
                    return Connected.Select(device => new AdbDeviceInfo
                    {
                        Serial = device.Transports[0].Serial,
                        Status = AdbDeviceStatus.Device,
                        RawStatus = "device"
                    }).ToList();
                }
            }

            public void Disconnect(int index)
            {
                _disconnected.Add(index);
            }

            public void Reconnect(int index)
            {
                _disconnected.Remove(index);
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(_root, true);
                }
                catch
                {
                }
            }
        }
    }
}
