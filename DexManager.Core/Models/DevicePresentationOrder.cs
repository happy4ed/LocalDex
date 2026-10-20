using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DexManager.Models
{
    public sealed class DevicePresentationOrder
    {
        private readonly List<string> _identities = new List<string>();
        private bool _initialized;

        public void Reconcile(IList<PhysicalDeviceInfo> devices)
        {
            var connected = new List<PhysicalDeviceInfo>();
            if (devices != null)
            {
                foreach (var device in devices)
                {
                    if (device == null || !device.IsConnected ||
                        string.IsNullOrWhiteSpace(device.Identity))
                    {
                        continue;
                    }
                    connected.Add(device);
                }
            }

            if (!_initialized && connected.Count > 0)
            {
                _initialized = true;
                if (connected.Count >= 2)
                    connected.Sort(CompareNewestFirst);
            }

            foreach (var device in connected)
            {
                if (!ContainsIdentity(device.Identity))
                    _identities.Add(device.Identity);
            }
        }

        public IList<string> GetIdentities()
        {
            return new List<string>(_identities);
        }

        // Automatic selection rule shared by startup, reconnect and the
        // settings default: the saved default phone wins whenever it is
        // connected; otherwise the first connected phone in presentation
        // order is used.  The fallback never replaces the saved default.
        public static string ChooseIdentity(
            IList<string> orderedIdentities,
            Func<string, bool> isConnected,
            string preferredIdentity)
        {
            if (orderedIdentities == null || isConnected == null)
                return string.Empty;
            var preferred = string.IsNullOrWhiteSpace(preferredIdentity)
                ? string.Empty
                : preferredIdentity.Trim();
            if (preferred.Length > 0)
            {
                foreach (var identity in orderedIdentities)
                {
                    if (string.Equals(
                            identity,
                            preferred,
                            StringComparison.OrdinalIgnoreCase) &&
                        isConnected(identity))
                    {
                        return identity;
                    }
                }
            }
            foreach (var identity in orderedIdentities)
            {
                if (!string.IsNullOrWhiteSpace(identity) &&
                    isConnected(identity))
                {
                    return identity;
                }
            }
            return string.Empty;
        }

        // Returns the authorized ADB serial (USB first, then wireless) of the
        // saved default phone, or an empty string when it is not connected.
        public static string FindPreferredTransportSerial(
            IList<PhysicalDeviceInfo> devices,
            string preferredIdentity)
        {
            if (devices == null ||
                string.IsNullOrWhiteSpace(preferredIdentity))
            {
                return string.Empty;
            }
            var preferred = preferredIdentity.Trim();
            foreach (var device in devices)
            {
                if (device == null || !string.Equals(
                        device.Identity,
                        preferred,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var transport = device.SelectPreferredTransport(null);
                return transport != null && transport.IsAuthorized &&
                    !string.IsNullOrWhiteSpace(transport.Serial)
                    ? transport.Serial.Trim()
                    : string.Empty;
            }
            return string.Empty;
        }

        public static int CompareNewestFirst(
            PhysicalDeviceInfo left,
            PhysicalDeviceInfo right)
        {
            var leftScore = GetGenerationScore(
                left == null ? null : left.DisplayName);
            var rightScore = GetGenerationScore(
                right == null ? null : right.DisplayName);
            var scoreResult = rightScore.CompareTo(leftScore);
            if (scoreResult != 0) return scoreResult;

            var nameResult = string.Compare(
                left == null ? string.Empty : left.DisplayName,
                right == null ? string.Empty : right.DisplayName,
                StringComparison.CurrentCultureIgnoreCase);
            if (nameResult != 0) return nameResult;

            return string.Compare(
                left == null ? string.Empty : left.Identity,
                right == null ? string.Empty : right.Identity,
                StringComparison.OrdinalIgnoreCase);
        }

        private bool ContainsIdentity(string identity)
        {
            foreach (var existing in _identities)
            {
                if (string.Equals(
                        existing,
                        identity,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetGenerationScore(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return 0;

            var match = Regex.Match(
                displayName,
                @"(?:Galaxy\s+)?S\s*(\d{1,2})(?:\D|$)",
                RegexOptions.IgnoreCase);
            if (match.Success)
                return ScoreGalaxyS(ParseNumber(match.Groups[1].Value));

            match = Regex.Match(
                displayName,
                @"(?:Galaxy\s+)?Note\s*(\d{1,2})(?:\D|$)",
                RegexOptions.IgnoreCase);
            if (match.Success)
                return ScoreGalaxyS(ParseNumber(match.Groups[1].Value));

            match = Regex.Match(
                displayName,
                @"(?:Galaxy\s+)?Z\s*(?:Fold|Flip)\s*(\d{1,2})(?:\D|$)",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var generation = ParseNumber(match.Groups[1].Value);
                return generation <= 0 ? 0 : 2018 + generation;
            }

            match = Regex.Match(
                displayName,
                @"(?:Galaxy\s+)?A\s*(\d{2})(?:\D|$)",
                RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var generation = ParseNumber(match.Groups[1].Value);
                return generation < 50 ? 0 : 1969 + generation;
            }

            return 0;
        }

        private static int ScoreGalaxyS(int generation)
        {
            if (generation >= 20) return 2000 + generation;
            if (generation >= 8 && generation <= 10)
                return 2009 + generation;
            return 0;
        }

        private static int ParseNumber(string value)
        {
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }
    }
}
