using System.Collections.Generic;
using DexManager.Models;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    // Regression for 2.1.0-localdex.11: a Fold8 that reconnected while its
    // serial property could not be read yet was registered again under its
    // android_id identity.  The device list showed it twice, the old entry
    // never reconnected and the keyboard/mouse handoff lost its target.
    public class DeviceIdentityStabilityTests
    {
        [Theory]
        [InlineData("R5KL8024M9A", true)]
        [InlineData("6d1a5c63dcda671b", true)]
        [InlineData("emulator-5554", true)]
        [InlineData("", false)]
        [InlineData("/bin/bash: line 1: getprop: command not found", false)]
        [InlineData("has space", false)]
        public void SerialPlausibility(string value, bool expected)
        {
            Assert.Equal(expected, AdbService.IsPlausibleDeviceSerial(value));
        }

        [Theory]
        [InlineData("9774d56d682e549c", true)]
        [InlineData("/bin/bash: line 1: settings: command not found", false)]
        [InlineData("null", false)]
        public void AndroidIdPlausibility(string value, bool expected)
        {
            Assert.Equal(expected, AdbService.IsPlausibleAndroidId(value));
        }

        [Fact]
        public void ShellErrorIsNotADeviceValue()
        {
            Assert.True(AdbService.LooksLikeShellError("/bin/bash: line 1: settings: command not found"));
            Assert.True(AdbService.LooksLikeShellError("/system/bin/sh: getprop: inaccessible or not found"));
            Assert.False(AdbService.LooksLikeShellError("승표의 Z Fold8"));
        }

        private static AppSettings SettingsFor(string identity)
        {
            var settings = AppSettings.CreateDefault();
            settings.PreferredDeviceIdentity = identity;
            settings.DeviceRunSettingsProfiles = new List<DeviceRunSettingsProfile>
            {
                new DeviceRunSettingsProfile { DeviceIdentity = identity }
            };
            settings.DeviceWirelessConnectionProfiles = new List<DeviceWirelessConnectionProfile>
            {
                new DeviceWirelessConnectionProfile { DeviceIdentity = identity, WirelessHost = "192.168.0.10", WirelessPort = 5555 }
            };
            settings.InputHandoff = settings.InputHandoff ?? InputHandoffSettings.CreateDefault();
            settings.InputHandoff.DeviceHotkeys = new List<InputHandoffDeviceHotkey>
            {
                new InputHandoffDeviceHotkey { DeviceIdentity = identity, Hotkey = "Ctrl+Alt+1" }
            };
            return settings;
        }

        [Fact]
        public void AliasSettingsMoveToSerialIdentity()
        {
            var settings = SettingsFor("android:9774d56d682e549c");
            Assert.True(settings.ReplaceDeviceIdentity("android:9774d56d682e549c", "serial:R5KL8024M9A"));
            Assert.Equal("serial:R5KL8024M9A", settings.PreferredDeviceIdentity);
            Assert.Equal("serial:R5KL8024M9A", settings.DeviceRunSettingsProfiles[0].DeviceIdentity);
            Assert.Equal("serial:R5KL8024M9A", settings.DeviceWirelessConnectionProfiles[0].DeviceIdentity);
            Assert.Equal("serial:R5KL8024M9A", settings.InputHandoff.DeviceHotkeys[0].DeviceIdentity);
            Assert.False(settings.ReplaceDeviceIdentity("android:9774d56d682e549c", "serial:R5KL8024M9A"));
        }

        [Fact]
        public void ExistingSerialEntryWinsOverAlias()
        {
            var settings = SettingsFor("serial:R5KL8024M9A");
            settings.DeviceRunSettingsProfiles.Add(new DeviceRunSettingsProfile { DeviceIdentity = "android:9774d56d682e549c" });
            Assert.True(settings.ReplaceDeviceIdentity("android:9774d56d682e549c", "serial:R5KL8024M9A"));
            Assert.Single(settings.DeviceRunSettingsProfiles);
            Assert.Equal("serial:R5KL8024M9A", settings.DeviceRunSettingsProfiles[0].DeviceIdentity);
        }

        [Fact]
        public void ForgetRemovesEverythingSavedForThePhone()
        {
            var settings = SettingsFor("serial:OLDPHONE01");
            settings.DeviceRunSettingsProfiles.Add(new DeviceRunSettingsProfile { DeviceIdentity = "serial:R5KL8024M9A" });
            Assert.True(settings.ForgetDeviceIdentity("serial:OLDPHONE01"));
            Assert.Equal(string.Empty, settings.PreferredDeviceIdentity);
            Assert.Single(settings.DeviceRunSettingsProfiles);
            Assert.Empty(settings.DeviceWirelessConnectionProfiles);
            Assert.Empty(settings.InputHandoff.DeviceHotkeys);
            Assert.False(settings.ForgetDeviceIdentity("serial:OLDPHONE01"));
        }
    }
}
