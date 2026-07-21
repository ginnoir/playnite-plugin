using System;

namespace RomM.Games
{
    /// <summary>
    /// Decides whether a reported RomM server version is new enough to import from. Extracted as a
    /// pure helper so the rule is unit-testable without the plugin/Playnite runtime.
    /// </summary>
    internal static class RomMServerVersion
    {
        private static readonly Version Minimum = new Version(4, 9);

        /// <summary>
        /// Returns true unless the version positively parses to something older than 4.9. Dev or
        /// non-numeric versions (e.g. "development") are assumed compatible; pre-release suffixes
        /// (e.g. "4.9.0-beta", "4.9.0+build") are stripped before parsing.
        /// </summary>
        public static bool SupportsImport(string version)
        {
            string raw = (version ?? string.Empty).Split('-', '+')[0];
            if (Version.TryParse(raw, out Version parsed))
                return parsed.CompareTo(Minimum) >= 0;

            return true;
        }
    }
}
