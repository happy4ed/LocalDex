using System.Diagnostics;
using System.Reflection;
using DexManager.Models;
using DexManager.Services;
using DexManager.Utils;

namespace DexManager.Tests;

public sealed class LocalDexLaunchTests
{
    private static void WithService(Action<ScrcpyService> test)
    {
        var settings = AppSettings.CreateDefault();
        var log = new LogService();
        var runner = new ProcessRunner(log);
        var adb = new AdbService(Path.Combine(Path.GetTempPath(), "localdex-no-adb"), 1000, runner, log);
        using var transfers = new FileTransferCoordinator(adb.AdbPath, settings, log, new DeviceRuntimeSessionRegistry());
        using var scrcpy = new ScrcpyService(Path.Combine(Path.GetTempPath(), "localdex-no-scrcpy"), 1000,
            runner, adb, new ScrcpyLaunchCoordinator(), transfers, log);
        test(scrcpy);
    }

    [Fact]
    public void DesktopUsesNewDisplayAndDefersAppUntilWindowingModeIsApplied()
    {
        WithService(scrcpy =>
        {
            var settings = AppSettings.CreateDefault().Scrcpy;
            settings.StartAppPackage = "com.android.settings";
            var args = scrcpy.BuildDesktopArguments(settings,
                new VirtualDisplaySettings { Width = 1920, Height = 1080, Dpi = 180 }, "USB-A");
            Assert.Contains("--new-display=1920x1080/180", args);
            Assert.Contains("USB-A", args);
            Assert.Contains("LocalDex", args);
            Assert.DoesNotContain("--display-id", args);
            Assert.DoesNotContain("--start-app", args);
            Assert.Contains("--start-app=com.android.settings", scrcpy.BuildArguments(settings, 17, "USB-A"));
        });
    }

    [Fact]
    public void ExtraArgumentsCannotOverrideManagedDisplayOrDevice()
    {
        foreach (var option in new[] { "--new-display=1x1", "--display-id=0", "--serial=USB-B", "-s USB-B", "--new-d=2x2", "--start-app=com.example.app" })
            Assert.Throws<InvalidOperationException>(() => ScrcpyService.ValidateAdditionalArguments(option));
    }

    [Fact]
    public void DisplayIdMustComeFromTheOwnedProcessAndItsCreationLog()
    {
        WithService(scrcpy =>
        {
            using var own = new Process(); using var other = new Process();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(ScrcpyService).GetField("_process", flags)!.SetValue(scrcpy, own);
            var observe = typeof(ScrcpyService).GetMethod("ObserveNewDisplay", flags)!;
            var id = typeof(ScrcpyService).GetField("_newDisplayId", flags)!;
            observe.Invoke(scrcpy, new object[] { other, "[server] INFO: New display: 1920x1080/180 (id=99)" });
            Assert.Equal(0, id.GetValue(scrcpy));
            observe.Invoke(scrcpy, new object[] { own, "Display id=99 in a list" });
            Assert.Equal(0, id.GetValue(scrcpy));
            observe.Invoke(scrcpy, new object[] { own, "[server] INFO: New display: 1920x1080/180 (id=17)" });
            Assert.Equal(17, id.GetValue(scrcpy));
            typeof(ScrcpyService).GetField("_process", flags)!.SetValue(scrcpy, null);
        });
    }
}
