using System;
using System.Collections.Generic;

namespace RomM.SaveSync
{
    /// <summary>
    /// Per-platform save knowledge, keyed on Playnite's stable platform SpecificationId
    /// (e.g. "nintendo_gameboyadvance") rather than RomM's user-mutable fs_slug.
    ///
    /// <list type="bullet">
    /// <item><see cref="CanonicalSaveExtension"/> — the format the plugin standardises ON for RomM (so a
    ///   save written by mGBA and one by RetroArch resolve to the same logical save).</item>
    /// <item><see cref="SaveExtensions"/> — battery-save extensions to scan for locally (native + canonical).</item>
    /// <item><see cref="ConverterFamily"/> — selects the converter (Task 6); "raw" = identity/rename only.</item>
    /// </list>
    /// RetroArch broadly uses .srm, so .srm is the canonical hub format for SRAM systems.
    /// </summary>
    internal sealed class PlatformSaveProfile
    {
        public string CanonicalSaveExtension { get; }
        public IReadOnlyList<string> SaveExtensions { get; }
        public string ConverterFamily { get; }

        public PlatformSaveProfile(string canonical, string[] saveExts, string family)
        {
            CanonicalSaveExtension = canonical;
            SaveExtensions = saveExts;
            ConverterFamily = family;
        }
    }

    internal static class PlatformSaveProfiles
    {
        // Conservative default for unknown platforms: scan the common battery extensions,
        // canonicalise to .srm, identity-convert only. Avoids grabbing savestates.
        public static readonly PlatformSaveProfile Default =
            new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw");

        private static readonly Dictionary<string, PlatformSaveProfile> ByKey =
            new Dictionary<string, PlatformSaveProfile>(StringComparer.OrdinalIgnoreCase)
            {
                // Playnite SpecificationId keys (legacy EmulatedPlatform.Id) + RomM slug/fs_slug aliases.
                // Nintendo handhelds — raw SRAM; mGBA writes .sav, RetroArch .srm (identity rename, + GBA RTC).
                ["nintendo_gameboyadvance"] = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "gba"),
                ["gba"]                     = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "gba"),
                ["nintendo_gameboy"]        = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["gb"]                      = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["nintendo_gameboycolor"]   = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["gbc"]                     = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),

                // Nintendo DS — melonDS/DeSmuME standalone .sav/.dsv, RetroArch .srm.
                ["nintendo_ds"]             = new PlatformSaveProfile("srm", new[] { "srm", "sav", "dsv" }, "nds"),
                ["nds"]                     = new PlatformSaveProfile("srm", new[] { "srm", "sav", "dsv" }, "nds"),

                // SNES / NES / Genesis — raw SRAM, .srm everywhere; some standalones .sav.
                ["nintendo_super_nes"]      = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["super_nintendo_entertainment_system"] = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["snes"]                    = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["nintendo_entertainment_system"] = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["nes"]                     = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["sega_genesis"]            = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["sega_mega_drive"]         = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["genesis"]                 = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),
                ["megadrive"]               = new PlatformSaveProfile("srm", new[] { "srm", "sav" }, "raw"),

                // N64 — RetroArch concatenates a single .srm; standalone splits to .eep/.sra/.fla/.mpk.
                ["nintendo_64"]             = new PlatformSaveProfile("srm", new[] { "srm", "eep", "sra", "fla", "mpk" }, "n64"),
                ["n64"]                     = new PlatformSaveProfile("srm", new[] { "srm", "eep", "sra", "fla", "mpk" }, "n64"),

                // PlayStation — memory cards; Beetle PSX .srm, DuckStation .mcd, others .mcr/.mc.
                ["sony_playstation"]        = new PlatformSaveProfile("srm", new[] { "srm", "mcd", "mcr", "mc", "vmp", "gme" }, "psx"),
                ["psx"]                     = new PlatformSaveProfile("srm", new[] { "srm", "mcd", "mcr", "mc", "vmp", "gme" }, "psx"),
            };

        /// <summary>Look up by Playnite SpecificationId or RomM platform slug/fs_slug.</summary>
        public static PlatformSaveProfile Get(string key)
        {
            if (!string.IsNullOrEmpty(key) && ByKey.TryGetValue(key, out var profile))
            {
                return profile;
            }
            return Default;
        }

        /// <summary>Resolve profile from RomM platform slug, falling back to fs_slug.</summary>
        public static PlatformSaveProfile Get(string slug, string fsSlug)
        {
            var hit = Get(slug);
            if (!ReferenceEquals(hit, Default))
            {
                return hit;
            }
            return Get(fsSlug);
        }
    }
}
