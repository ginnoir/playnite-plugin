using System.Web;

namespace RomM.Games
{
    /// <summary>
    /// Builds the "api/roms" query string (missing-file and genre-exclusion options) onto a base url.
    /// Extracted as a pure helper so the query assembly is unit-testable without the plugin settings.
    /// </summary>
    internal static class RomMRomQuery
    {
        public static string Build(string romsBaseUrl, bool skipMissingFiles, string excludeGenres)
        {
            string url = romsBaseUrl;

            if (skipMissingFiles)
                url += "?missing=false&";

            string genres = (excludeGenres ?? string.Empty).Trim(' ').Trim(';');
            if (!string.IsNullOrEmpty(genres))
            {
                // Add the query separator only if the missing-files option hasn't already.
                if (!skipMissingFiles)
                    url += "?";

                foreach (string genre in genres.Split(';'))
                    url += $"genres={HttpUtility.UrlEncode(genre)}&";
            }

            return url.TrimEnd('&');
        }
    }
}
