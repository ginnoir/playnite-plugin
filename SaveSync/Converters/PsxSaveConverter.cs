using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomM.SaveSync.Converters
{
    /// <summary>
    /// PlayStation memory cards. The on-card data is a raw 128 KB (131072-byte) image, shared across
    /// .srm (Beetle PSX), .mcd (DuckStation/PCSX-Redux), .mcr/.mc (ePSXe/pSX/PCSXR) — those are
    /// identity renames. Container formats carry a header before that raw image:
    ///   - .vmp (PSP/PS3 virtual card): 128-byte header
    ///   - .gme (DexDrive):             3904-byte header
    /// We strip those headers to reach the canonical raw image. We do NOT write .vmp/.gme back,
    /// because their headers include a signature we cannot validly regenerate — emitting one risks a
    /// card the original tool rejects, so the engine syncs as-is and warns instead.
    /// </summary>
    internal sealed class PsxSaveConverter : ISaveConverter
    {
        public string Family => "psx";

        private const int RawCardSize = 131072; // 128 KB
        private const int VmpHeaderSize = 128;
        private const int GmeHeaderSize = 3904;

        private static readonly HashSet<string> RawExts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "srm", "mcd", "mcr", "mc", "bin", "ddf", "psm" };

        public SaveFile ToCanonical(IList<SaveFile> nativeFiles, ConversionContext ctx)
        {
            var primary = ConverterHelpers.PickPrimary(nativeFiles);
            if (primary == null)
            {
                return null;
            }

            var ext = Path.GetExtension(primary.Name).TrimStart('.').ToLowerInvariant();
            var raw = ExtractRaw(primary.Bytes, ext, ctx);
            if (raw == null)
            {
                return null; // unrecognised/odd container -> let the engine sync as-is
            }
            return ConverterHelpers.Renamed(raw, ctx.BaseName, ctx.CanonicalExtension);
        }

        public IList<SaveFile> FromCanonical(SaveFile canonical, ConversionContext ctx)
        {
            if (canonical?.Bytes == null)
            {
                return null;
            }

            var target = (ctx.TargetNativeExtension ?? "").TrimStart('.').ToLowerInvariant();
            if (RawExts.Contains(target))
            {
                // Canonical is already the raw image; just rename.
                return new List<SaveFile> { ConverterHelpers.Renamed(canonical.Bytes, ctx.BaseName, target) };
            }

            // .vmp/.gme require a signed/proprietary header we can't safely synthesize.
            ctx.Logger?.Warn($"PSX target '.{target}' for '{ctx.BaseName}' can't be safely generated from a raw card; syncing as-is.");
            return null;
        }

        private static byte[] ExtractRaw(byte[] bytes, string ext, ConversionContext ctx)
        {
            if (bytes == null)
            {
                return null;
            }

            if (ext == "vmp" && bytes.Length == RawCardSize + VmpHeaderSize)
            {
                return Slice(bytes, VmpHeaderSize, RawCardSize);
            }
            if (ext == "gme" && bytes.Length == RawCardSize + GmeHeaderSize)
            {
                return Slice(bytes, GmeHeaderSize, RawCardSize);
            }
            if (bytes.Length == RawCardSize)
            {
                return bytes; // already raw
            }

            ctx.Logger?.Warn($"PSX card for '{ctx.BaseName}' has unexpected size {bytes.Length} (.{ext}); syncing as-is.");
            return null;
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            var dst = new byte[length];
            Array.Copy(source, offset, dst, 0, length);
            return dst;
        }
    }
}
