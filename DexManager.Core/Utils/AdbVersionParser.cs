using System;
using System.Text.RegularExpressions;

namespace DexManager.Utils
{
    internal static class AdbVersionParser
    {
        private static readonly Regex ActualVersionRegex = new Regex(
            @"^\s*Version\s+([^\r\n]+?)\s*$",
            RegexOptions.IgnoreCase |
            RegexOptions.Multiline |
            RegexOptions.CultureInvariant);

        internal static string GetDisplayVersion(
            string output,
            string unavailableText)
        {
            var match = ActualVersionRegex.Match(output ?? string.Empty);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            return unavailableText;
        }

        internal static Version GetVersionNumber(string output)
        {
            var match = ActualVersionRegex.Match(output ?? string.Empty);
            if (!match.Success) return null;

            var numeric = Regex.Match(
                match.Groups[1].Value,
                @"^\d+(?:\.\d+){1,3}",
                RegexOptions.CultureInvariant);
            Version version;
            return numeric.Success && Version.TryParse(
                numeric.Value,
                out version)
                ? version
                : null;
        }
    }
}
