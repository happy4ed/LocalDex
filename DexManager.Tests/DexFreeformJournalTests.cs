using DexManager.Models;
using DexManager.Services;

namespace DexManager.Tests;

public sealed class DexFreeformJournalTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "localdex-freeform-" + Guid.NewGuid());
    private readonly Dictionary<string, string> values = new() { ["serial:phone-a"] = "null", ["serial:phone-b"] = "0" };
    private readonly List<string> commands = new();
    private readonly List<string> shellCalls = new();
    private bool offline;
    private bool denyEnable;
    private bool rejectMode;
    private bool unverified;
    private bool writeAheadObserved;
    private bool ignoreEnable;
    private bool ignoreRestore;
    private bool failEnable;
    private bool stdoutEnableError;
    private bool emptyModeReadback;

    private string Identity(string serial) => offline || unverified ? "" : serial == "USB-B" ? "serial:phone-b" : "serial:phone-a";
    private DexFreeformJournal Journal() => new(folder, Shell, Identity, _ => { });
    private ProcessResult Shell(string serial, string command)
    {
        shellCalls.Add(command);
        var output = "";
        var error = "";
        foreach (var part in command.Split(" && "))
        {
            var result = Execute(serial, part);
            output += result.StandardOutput;
            error += result.StandardError;
            if (!result.IsSuccess)
                return new() { ExitCode = result.ExitCode, StandardOutput = output, StandardError = error };
        }
        return new() { StandardOutput = output, StandardError = error };
    }

    private ProcessResult Execute(string serial, string command)
    {
        commands.Add(serial + ":" + command);
        if (offline) return new() { ExitCode = 1, StandardError = "offline" };
        if (command == "echo LOCALDEX_READBACK") return new() { StandardOutput = "LOCALDEX_READBACK\n" };
        var identity = Identity(serial);
        if (command == "settings get global enable_freeform_support") return new() { StandardOutput = values[identity] + "\n" };
        if (command.StartsWith("settings put global enable_freeform_support "))
        {
            writeAheadObserved = Directory.GetFiles(folder, "*.recovery").Length > 0;
            if (denyEnable && command.EndsWith(" 1")) return new() { ExitCode = 0, StandardError = "SecurityException: denied" };
            if (failEnable && command.EndsWith(" 1")) return new() { ExitCode = 1, StandardError = "write failed" };
            if (stdoutEnableError && command.EndsWith(" 1")) return new() { StandardOutput = "Error: write failed\n" };
            if (ignoreEnable && command.EndsWith(" 1")) return new();
            values[identity] = command.Split(' ').Last();
        }
        if (command == "settings delete global enable_freeform_support")
        {
            if (!ignoreRestore) values[identity] = "null";
            return new() { StandardOutput = "Deleted 1 rows\n" };
        }
        if (command.StartsWith("wm set-display-windowing-mode"))
            return new() { StandardOutput = "display windowing mode=freeform\n" };
        if (command.StartsWith("wm get-display-windowing-mode"))
        {
            if (emptyModeReadback) return new();
            return new() { StandardOutput = "display windowing mode=" + (rejectMode ? "fullscreen" : "freeform") + " for displayId=17" };
        }
        return new() { StandardOutput = "" };
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void RestoresExactOriginalValueIncludingMissingSetting(string original)
    {
        values["serial:phone-a"] = original;
        var journal = Journal();
        var identity = journal.Acquire("USB-A");
        Assert.True(writeAheadObserved);
        Assert.Equal("1", values[identity]);
        journal.ConfigureDisplay("USB-A", identity, 17);
        Assert.Contains("USB-A:wm set-display-windowing-mode -d 17 5", commands);
        Assert.True(journal.Release("USB-A", identity));
        Assert.Equal(original, values[identity]);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void OfflineStopRetainsJournalAndRecoversOverDifferentTransport()
    {
        var journal = Journal(); var identity = journal.Acquire("USB-A");
        offline = true;
        Assert.False(journal.Release("USB-A", identity));
        Assert.Single(Directory.GetFiles(folder));
        offline = false;
        Assert.True(Journal().Recover("192.168.1.2:5555"));
        Assert.Equal("null", values[identity]);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void RecoverAndDuplicateStartCannotResetAnActiveDesktop()
    {
        var journal = Journal(); var identity = journal.Acquire("USB-A");
        Assert.False(Journal().Recover("192.168.1.2:5555"));
        Assert.Throws<InvalidOperationException>(() => Journal().Acquire("USB-A"));
        Assert.Equal("1", values[identity]);
        Assert.True(journal.Release("USB-A", identity));
    }

    [Fact]
    public void RecoveryAndConcurrentSessionsAreIsolatedPerPhysicalPhone()
    {
        var journal = Journal(); var a = journal.Acquire("USB-A"); var b = journal.Acquire("USB-B");
        Assert.False(journal.Release("USB-B", a));
        Assert.Equal("1", values[b]);
        Assert.True(journal.Release("USB-B", b));
        Assert.Equal("0", values[b]);
        Assert.Equal("1", values[a]);
        Assert.True(journal.Recover("USB-A"));
        Assert.Equal("null", values[a]);
    }

    [Fact]
    public void FailedEnableRollsBackAndDoesNotLoseOriginal()
    {
        denyEnable = true;
        Assert.Throws<InvalidOperationException>(() => Journal().Acquire("USB-A"));
        Assert.Equal("null", values["serial:phone-a"]);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void EnableAndDisplayVerificationUseSingleShellWithoutDroppingChecks()
    {
        var journal = Journal();
        var identity = journal.Acquire("USB-A");
        Assert.Equal(2, shellCalls.Count); // Original read, then write+verify.
        journal.ConfigureDisplay("USB-A", identity, 17);
        Assert.Equal(3, shellCalls.Count);
        Assert.True(journal.Release("USB-A", identity));
        Assert.Equal(4, shellCalls.Count);
        Assert.Equal("null", values[identity]);
    }

    [Fact]
    public void SuccessfulExitWithoutSettingChangeStillFailsVerification()
    {
        ignoreEnable = true;
        Assert.Throws<InvalidOperationException>(() => Journal().Acquire("USB-A"));
        Assert.Equal("null", values["serial:phone-a"]);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void NonzeroWriteCannotBeMaskedByFollowingRead()
    {
        failEnable = true;
        Assert.Throws<InvalidOperationException>(() => Journal().Acquire("USB-A"));
        Assert.Equal("null", values["serial:phone-a"]);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void StdoutWriteErrorCannotBeMaskedByAlreadyEnabledValue()
    {
        values["serial:phone-a"] = "1";
        stdoutEnableError = true;
        Assert.Throws<InvalidOperationException>(() => Journal().Acquire("USB-A"));
        Assert.Single(Directory.GetFiles(folder)); // Restore also failed: retain evidence.
        stdoutEnableError = false;
        Assert.True(Journal().Recover("USB-A"));
    }

    [Fact]
    public void EmptyReadbackCannotReuseSetCommandSuccessOutput()
    {
        var journal = Journal(); var identity = journal.Acquire("USB-A");
        emptyModeReadback = true;
        Assert.Throws<InvalidOperationException>(() => journal.ConfigureDisplay("USB-A", identity, 17));
        Assert.True(journal.Release("USB-A", identity));
    }

    [Fact]
    public void FailedRestoreVerificationKeepsOriginalForNextRecovery()
    {
        var journal = Journal(); var identity = journal.Acquire("USB-A");
        ignoreRestore = true;
        Assert.False(journal.Release("USB-A", identity));
        Assert.Single(Directory.GetFiles(folder));
        ignoreRestore = false;
        Assert.True(journal.Recover("USB-A"));
        Assert.Equal("null", values[identity]);
    }

    [Fact]
    public void AcquireRecoversPendingJournalBeforeTakingNewSnapshot()
    {
        var journal = Journal(); var identity = journal.Acquire("USB-A");
        offline = true; Assert.False(journal.Release("USB-A", identity)); offline = false;
        var restarted = Journal();
        var nextIdentity = restarted.Acquire("192.168.1.2:5555");
        Assert.True(restarted.Release("192.168.1.2:5555", nextIdentity));
        Assert.Equal("null", values[identity]);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void RejectedDisplayModeFailsInsteadOfClaimingSuccess()
    {
        var journal = Journal(); var identity = journal.Acquire("USB-A"); rejectMode = true;
        Assert.Throws<InvalidOperationException>(() => journal.ConfigureDisplay("USB-A", identity, 17));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.ConfigureDisplay("USB-A", identity, 0));
        Assert.True(journal.Release("USB-A", identity));
    }

    [Fact]
    public void UnverifiedIdentityCannotMutateSettings()
    {
        unverified = true;
        Assert.Throws<InvalidOperationException>(() => Journal().Acquire("USB-A"));
        Assert.Empty(commands);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void CorruptRecordIsPreservedAndNeverExecuted()
    {
        var journal = Journal(); var identity = journal.Acquire("USB-A");
        offline = true; Assert.False(journal.Release("USB-A", identity)); offline = false;
        var path = Directory.GetFiles(folder).Single();
        File.WriteAllText(path, identity + "\n0; reboot");
        commands.Clear();
        Assert.False(journal.Recover("USB-A"));
        Assert.Empty(commands);
        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        offline = false; unverified = false;
        var j = Journal(); j.Release("USB-A", "serial:phone-a"); j.Release("USB-B", "serial:phone-b");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
