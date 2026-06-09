using System;
using System.Collections.Generic;

namespace RomM.SaveSync.Converters
{
    /// <summary>
    /// Maps a <see cref="PlatformSaveProfile.ConverterFamily"/> to its converter. Unknown families
    /// fall back to the lossless identity/rename converter.
    /// </summary>
    internal static class ConverterRegistry
    {
        private static readonly IdentitySaveConverter Identity = new IdentitySaveConverter();

        private static readonly Dictionary<string, ISaveConverter> ByFamily =
            new Dictionary<string, ISaveConverter>(StringComparer.OrdinalIgnoreCase)
            {
                ["raw"] = Identity,
                ["gba"] = new GbaSaveConverter(),
                ["psx"] = new PsxSaveConverter(),
                ["n64"] = new N64SaveConverter(),
                ["nds"] = Identity, // melonDS/DeSmuME .sav/.dsv vs RetroArch .srm are raw — rename only
            };

        public static ISaveConverter For(string family)
        {
            if (!string.IsNullOrEmpty(family) && ByFamily.TryGetValue(family, out var converter))
            {
                return converter;
            }
            return Identity;
        }
    }
}
