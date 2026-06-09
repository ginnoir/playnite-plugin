using System.Collections.Generic;
using System.Linq;

namespace RomM.SaveSync.Converters
{
    /// <summary>Shared helpers for single-file, raw-bytes converters.</summary>
    internal static class ConverterHelpers
    {
        /// <summary>Pick the most save-like file from a native set: prefer a non-zero-length file, largest wins.</summary>
        public static SaveFile PickPrimary(IList<SaveFile> files)
        {
            return files?
                .Where(f => f?.Bytes != null)
                .OrderByDescending(f => f.Bytes.Length)
                .FirstOrDefault();
        }

        public static SaveFile Renamed(byte[] bytes, string baseName, string extension)
        {
            return new SaveFile($"{baseName}.{extension.TrimStart('.')}", bytes);
        }
    }

    /// <summary>
    /// Default converter for raw-SRAM systems (GB/GBC/SNES/NES/Genesis and unknown platforms).
    /// The bytes are identical across emulators; only the extension differs, so this renames and
    /// copies bytes verbatim — a trivially lossless round-trip.
    /// </summary>
    internal class IdentitySaveConverter : ISaveConverter
    {
        public virtual string Family => "raw";

        public virtual SaveFile ToCanonical(IList<SaveFile> nativeFiles, ConversionContext ctx)
        {
            var primary = ConverterHelpers.PickPrimary(nativeFiles);
            if (primary == null)
            {
                return null;
            }
            return ConverterHelpers.Renamed(primary.Bytes, ctx.BaseName, ctx.CanonicalExtension);
        }

        public virtual IList<SaveFile> FromCanonical(SaveFile canonical, ConversionContext ctx)
        {
            if (canonical?.Bytes == null)
            {
                return null;
            }
            return new List<SaveFile> { ConverterHelpers.Renamed(canonical.Bytes, ctx.BaseName, ctx.TargetNativeExtension) };
        }
    }

    /// <summary>
    /// Game Boy Advance.
    ///
    /// mGBA (.sav) and RetroArch's mGBA/VBA cores (.srm) both store the raw battery SRAM, and for
    /// RTC carts (Pokémon R/S/E, G/S/C) the 16-byte RTC block is kept inline by both — so the files
    /// are byte-compatible and a rename is the correct, lossless conversion. We deliberately do NOT
    /// strip/append the RTC block or re-pad to chip size: those mutations are the classic way GBA
    /// saves get corrupted, and the round-trip guard would reject them anyway. We only warn when a
    /// save size is outside the known GBA range so the user can investigate a genuinely odd file.
    /// </summary>
    internal sealed class GbaSaveConverter : IdentitySaveConverter
    {
        public override string Family => "gba";

        // 512B/8KB EEPROM, 32KB SRAM, 64KB/128KB Flash, plus up to a 16-byte RTC tail.
        private static readonly int[] KnownSizes = { 512, 8192, 32768, 65536, 131072 };

        public override SaveFile ToCanonical(IList<SaveFile> nativeFiles, ConversionContext ctx)
        {
            var primary = ConverterHelpers.PickPrimary(nativeFiles);
            if (primary != null)
            {
                WarnOnOddSize(primary.Bytes.Length, ctx);
            }
            return base.ToCanonical(nativeFiles, ctx);
        }

        private static void WarnOnOddSize(int length, ConversionContext ctx)
        {
            // Accept exact known sizes and known size + small RTC tail (<= 16 bytes).
            var ok = KnownSizes.Any(s => length == s || (length > s && length - s <= 16));
            if (!ok)
            {
                ctx.Logger?.Warn($"GBA save for '{ctx.BaseName}' has an unusual size ({length} bytes); syncing as-is.");
            }
        }
    }
}
