using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DexManager.Models;

namespace DexManager.Services
{
    // Write-ahead recovery record: never change a phone before its original value
    // is durable. Device identity, rather than USB/Wi-Fi transport, owns the record.
    public sealed class DexFreeformJournal
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Active = new HashSet<string>();
        private readonly string _directory;
        private readonly Func<string, string, ProcessResult> _shell;
        private readonly Func<string, string> _identity;
        private readonly Action<string> _warning;

        public DexFreeformJournal(string directory,
            Func<string, string, ProcessResult> shell,
            Func<string, string> identity, Action<string> warning)
        {
            _directory = directory;
            _shell = shell;
            _identity = identity;
            _warning = warning;
        }

        public string Acquire(string serial)
        {
            lock (Gate)
            {
                var identity = RequireIdentity(serial);
                var path = RecordPath(identity);
                if (Active.Contains(path))
                    throw new InvalidOperationException("A LocalDex desktop is already active for this phone.");
                RestoreCore(serial, identity);
                var original = ReadSetting(serial);
                Directory.CreateDirectory(_directory);
                // CreateNew also refuses to overwrite unresolved recovery evidence.
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(identity + "\n" + original);
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                try
                {
                    if (WriteAndReadSetting(serial, "settings put global enable_freeform_support 1") != "1")
                        throw new InvalidOperationException("The phone did not enable freeform support.");
                    Active.Add(path);
                    return identity;
                }
                catch
                {
                    try { RestoreCore(serial, identity); }
                    catch (Exception ex) { _warning("Freeform recovery is pending: " + ex.Message); }
                    throw;
                }
            }
        }

        public void ConfigureDisplay(string serial, string identity, int displayId)
        {
            if (displayId <= 0) throw new ArgumentOutOfRangeException("displayId");
            if (RequireIdentity(serial) != identity)
                throw new InvalidOperationException("The connected phone changed during startup.");
            var id = displayId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var result = _shell(serial, "wm set-display-windowing-mode -d " + id +
                " 5 && echo LOCALDEX_READBACK && wm get-display-windowing-mode -d " + id);
            RequireSuccess(result);
            if (!Regex.IsMatch(ReadbackOutput(result.StandardOutput), @"windowing mode\s*=\s*(freeform|5)\b", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("This phone did not accept freeform mode for the LocalDex display. " + result.StandardOutput);
        }

        // Called only after the owning scrcpy process has actually stopped.
        public bool Release(string serial, string identity)
        {
            lock (Gate)
            {
                if (string.IsNullOrWhiteSpace(identity)) return false;
                Active.Remove(RecordPath(identity));
                return TryRestore(serial, identity);
            }
        }

        // Safe on reconnect, on application restart, or from the Settings button.
        // Never restores a phone while one of our desktops is still running.
        public bool Recover(string serial)
        {
            lock (Gate)
            {
                try
                {
                    var identity = RequireIdentity(serial);
                    if (Active.Contains(RecordPath(identity)))
                        throw new InvalidOperationException("Disconnect this phone's LocalDex desktop before resetting its freeform settings.");
                    RestoreCore(serial, identity);
                    return true;
                }
                catch (Exception ex) { _warning("Freeform recovery is pending: " + ex.Message); return false; }
            }
        }

        private bool TryRestore(string serial, string expectedIdentity)
        {
            try
            {
                if (RequireIdentity(serial) != expectedIdentity)
                    throw new InvalidOperationException("Freeform recovery refused: the phone identity does not match.");
                RestoreCore(serial, expectedIdentity);
                return true;
            }
            catch (Exception ex) { _warning("Freeform recovery is pending: " + ex.Message); return false; }
        }

        private void RestoreCore(string serial, string identity)
        {
            var path = RecordPath(identity);
            if (!File.Exists(path)) return;
            var record = File.ReadAllText(path, Encoding.UTF8).Split('\n');
            if (record.Length != 2 || record[0] != identity || !ValidValue(record[1]))
                throw new InvalidDataException("Invalid freeform recovery record; it has been retained for inspection.");
            var original = record[1];
            var restored = WriteAndReadSetting(serial, original == "null"
                ? "settings delete global enable_freeform_support"
                : "settings put global enable_freeform_support " + original);
            if (restored != original)
                throw new InvalidOperationException("Freeform restoration was not confirmed by the phone.");
            File.Delete(path);
        }

        private string RequireIdentity(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) throw new ArgumentException("Select a connected phone first.");
            var identity = _identity(serial);
            if (string.IsNullOrWhiteSpace(identity) || identity.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                !(identity.StartsWith("serial:", StringComparison.Ordinal) || identity.StartsWith("android:", StringComparison.Ordinal)))
                throw new InvalidOperationException("The physical phone identity could not be verified.");
            return identity;
        }

        private string RecordPath(string identity)
        {
            using (var hash = SHA256.Create())
                return Path.Combine(_directory, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "") + ".recovery");
        }

        private string ReadSetting(string serial)
        {
            return ReadSettingResult(_shell(serial, "settings get global enable_freeform_support"));
        }

        private string WriteAndReadSetting(string serial, string writeCommand)
        {
            // One remote shell, with the write-ahead record already on disk.
            // A failed write must not be hidden by a successful verification read.
            return ReadSettingResult(_shell(serial, writeCommand +
                " && echo LOCALDEX_READBACK && settings get global enable_freeform_support"), true);
        }

        private static string ReadSettingResult(ProcessResult result, bool hasWriteOutput = false)
        {
            RequireSuccess(result);
            // Keep write stdout for error detection (some Android commands exit 0
            // on errors). Only text after the marker belongs to the readback.
            var value = hasWriteOutput ? ReadbackOutput(result.StandardOutput)
                : (result.StandardOutput ?? "").Trim();
            if (!ValidValue(value)) throw new InvalidDataException("Could not read the original freeform setting.");
            return value;
        }

        private static string ReadbackOutput(string output)
        {
            const string marker = "LOCALDEX_READBACK";
            var text = output ?? "";
            var offset = text.IndexOf(marker, StringComparison.Ordinal);
            if (offset < 0)
                throw new InvalidDataException("Android command did not reach its verification read.");
            return text.Substring(offset + marker.Length).Trim();
        }

        private static bool ValidValue(string value)
        {
            return value == "null" || Regex.IsMatch(value, @"\A-?\d+\z");
        }

        private static void RequireSuccess(ProcessResult result)
        {
            if (result == null || !result.IsSuccess ||
                !string.IsNullOrWhiteSpace(result.StandardError) ||
                Regex.IsMatch(result.StandardOutput ?? "", @"(^|\n)\s*(Error|Exception|SecurityException):", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Android command failed: " + (result == null ? "no response" : result.StandardError + " " + result.StandardOutput));
        }
    }
}
