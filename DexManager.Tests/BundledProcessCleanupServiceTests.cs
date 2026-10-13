using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using DexManager.Services;
using Xunit;

namespace DexManager.Tests
{
    public class BundledProcessCleanupServiceTests
    {
        [Fact]
        public void NormalizePathForComparison_StripsPrivatePrefixOnMac()
        {
            var normMethod = typeof(BundledProcessCleanupService).GetMethod(
                "NormalizePathForComparison",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(normMethod);

            if (OperatingSystem.IsMacOS())
            {
                var input = "/private/tmp/test.exe";
                var result = (string)normMethod.Invoke(null, new object[] { input });
                Assert.Equal("/tmp/test.exe", result);
            }

            var empty = (string)normMethod.Invoke(null, new object[] { "" });
            Assert.Equal(string.Empty, empty);
        }

        [Fact]
        public void AddExecutablePath_IgnoresNullOrEmpty()
        {
            var logService = new LogService();
            var service = new BundledProcessCleanupService(logService);

            service.AddExecutablePath(null);
            service.AddExecutablePath("");
            service.AddExecutablePath("   ");

            var terminated = service.TerminateRemainingProcesses();
            Assert.Equal(0, terminated);
        }

        [Fact]
        public void AddExecutablePath_DeduplicatesSamePath()
        {
            var logService = new LogService();
            var service = new BundledProcessCleanupService(logService);

            var path = "/tmp/test_binary";
            service.AddExecutablePath(path);
            service.AddExecutablePath(path);
            service.AddExecutablePath("/tmp/test_binary");

            var field = typeof(BundledProcessCleanupService).GetField(
                "_executablePaths",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);

            var list = (System.Collections.IList)field.GetValue(service);
            Assert.Single(list);
        }

        [Fact]
        public void TerminateRemainingProcesses_TerminatesOnlyConfiguredExecutablePath()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "DXManager.TestProcessCleanup." + Guid.NewGuid().ToString("N"));
            var ownedDir = Path.Combine(root, "owned");
            var unrelatedDir = Path.Combine(root, "unrelated");
            Directory.CreateDirectory(ownedDir);
            Directory.CreateDirectory(unrelatedDir);

            // Deliberately exceed 15 characters to detect Unix name truncation
            // accidentally applied to Windows process lookups.
            var scriptName = "dxmtest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (OperatingSystem.IsWindows()) scriptName += ".exe";
            var ownedScript = Path.Combine(ownedDir, scriptName);
            var unrelatedScript = Path.Combine(unrelatedDir, scriptName);
            var arguments = OperatingSystem.IsWindows() ? "127.0.0.1 -n 30" : "30";

            if (OperatingSystem.IsWindows())
            {
                var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
                File.Copy(ping, ownedScript);
                File.Copy(ping, unrelatedScript);
            }
            else
            {
                File.Copy("/bin/sleep", ownedScript);
                File.Copy("/bin/sleep", unrelatedScript);
                File.SetUnixFileMode(ownedScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(unrelatedScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Process ownedProcess = null;
            Process unrelatedProcess = null;

            try
            {
                ownedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = ownedScript,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                unrelatedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = unrelatedScript,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                Thread.Sleep(300);
                Assert.False(ownedProcess.HasExited, "Owned test process must start before cleanup.");
                Assert.False(unrelatedProcess.HasExited, "Unrelated test process must start before cleanup.");

                var cleanup = new BundledProcessCleanupService(new LogService());
                cleanup.AddExecutablePath(ownedScript);

                var count = cleanup.TerminateRemainingProcesses();
                Assert.Equal(1, count);

                Assert.True(ownedProcess.WaitForExit(3000), "Configured owned process must be terminated.");
                Assert.False(unrelatedProcess.HasExited, "Unrelated process in different path must remain running.");
            }
            finally
            {
                if (ownedProcess != null)
                {
                    try { if (!ownedProcess.HasExited) ownedProcess.Kill(); }
                    catch { }
                    ownedProcess.Dispose();
                }
                if (unrelatedProcess != null)
                {
                    try
                    {
                        if (!unrelatedProcess.HasExited)
                        {
                            unrelatedProcess.Kill();
                            unrelatedProcess.WaitForExit(2000);
                        }
                    }
                    catch { }
                    unrelatedProcess.Dispose();
                }
                try { Directory.Delete(root, true); }
                catch { }
            }
        }
    }
}
