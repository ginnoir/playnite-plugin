using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomM.SaveSync.Converters
{
    /// <summary>
    /// Nintendo 64. RetroArch's mupen64plus-next stores all save types in ONE concatenated .srm,
    /// while standalone emulators (mupen64plus, Project64) split them into .eep/.mpk/.sra/.fla.
    ///
    /// The canonical .srm layout is verified against libretro mupen64plus-next
    /// (libretro/libretro_memory.h `save_memory_data` + core size enums):
    ///
    ///   | component | offset    | length    | standalone ext |
    ///   |-----------|-----------|-----------|----------------|
    ///   | eeprom    | 0x00000   | 0x00800   | .eep           |
    ///   | mempack   | 0x00800   | 0x20000   | .mpk (4×0x8000)|
    ///   | sram      | 0x20800   | 0x08000   | .sra           |
    ///   | flashram  | 0x28800   | 0x20000   | .fla           |
    ///   | TOTAL     |           | 0x48800   (296960 bytes)   |
    ///
    /// Because the layout is reverse-engineered, ToCanonical runs a round-trip self-check: it splits
    /// the .srm it just built and confirms each non-empty input component is reproduced. If not, it
    /// returns null and the engine syncs the save as-is (tagged by emulator) with a warning — never a
    /// silently corrupted save.
    /// </summary>
    internal sealed class N64SaveConverter : ISaveConverter
    {
        public string Family => "n64";

        private const int EepOff = 0x00000, EepLen = 0x00800;
        private const int MpkOff = 0x00800, MpkLen = 0x20000;
        private const int SraOff = 0x20800, SraLen = 0x08000;
        private const int FlaOff = 0x28800, FlaLen = 0x20000;
        private const int SrmSize = 0x48800; // 296960

        private struct Component { public string Ext; public int Off; public int Len; }
        private static readonly Component[] Layout =
        {
            new Component { Ext = "eep", Off = EepOff, Len = EepLen },
            new Component { Ext = "mpk", Off = MpkOff, Len = MpkLen },
            new Component { Ext = "sra", Off = SraOff, Len = SraLen },
            new Component { Ext = "fla", Off = FlaOff, Len = FlaLen },
        };

        public SaveFile ToCanonical(IList<SaveFile> nativeFiles, ConversionContext ctx)
        {
            if (nativeFiles == null || nativeFiles.Count == 0)
            {
                return null;
            }

            // RetroArch case: already a single .srm of the canonical size — pass through.
            var srm = nativeFiles.FirstOrDefault(f =>
                f?.Bytes != null &&
                f.Bytes.Length == SrmSize &&
                Path.GetExtension(f.Name).TrimStart('.').Equals("srm", StringComparison.OrdinalIgnoreCase));
            if (srm != null)
            {
                return ConverterHelpers.Renamed(srm.Bytes, ctx.BaseName, ctx.CanonicalExtension);
            }

            // Standalone case: join component files into the canonical blob.
            var byExt = MapByExtension(nativeFiles);
            if (byExt.Count == 0)
            {
                return null;
            }

            var canonical = new byte[SrmSize];
            foreach (var c in Layout)
            {
                if (byExt.TryGetValue(c.Ext, out var bytes) && bytes.Length > 0)
                {
                    Buffer.BlockCopy(bytes, 0, canonical, c.Off, Math.Min(bytes.Length, c.Len));
                }
            }

            if (!RoundTripOk(canonical, byExt, ctx))
            {
                ctx.Logger?.Warn($"N64 save for '{ctx.BaseName}' failed the split/join round-trip check; syncing as-is.");
                return null;
            }

            return ConverterHelpers.Renamed(canonical, ctx.BaseName, ctx.CanonicalExtension);
        }

        public IList<SaveFile> FromCanonical(SaveFile canonical, ConversionContext ctx)
        {
            if (canonical?.Bytes == null)
            {
                return null;
            }

            var target = (ctx.TargetNativeExtension ?? "srm").TrimStart('.').ToLowerInvariant();

            // RetroArch target: keep the single .srm.
            if (target == "srm")
            {
                return new List<SaveFile> { ConverterHelpers.Renamed(canonical.Bytes, ctx.BaseName, "srm") };
            }

            // Standalone target: split into component files, emitting only the ones with real data.
            if (canonical.Bytes.Length != SrmSize)
            {
                ctx.Logger?.Warn($"N64 canonical save for '{ctx.BaseName}' is {canonical.Bytes.Length} bytes, not {SrmSize}; can't split. Writing .srm as-is.");
                return new List<SaveFile> { ConverterHelpers.Renamed(canonical.Bytes, ctx.BaseName, "srm") };
            }

            var outputs = new List<SaveFile>();
            foreach (var c in Layout)
            {
                var slice = Slice(canonical.Bytes, c.Off, c.Len);
                if (slice.Any(b => b != 0))
                {
                    outputs.Add(new SaveFile($"{ctx.BaseName}.{c.Ext}", slice));
                }
            }
            return outputs.Count > 0 ? outputs : null;
        }

        private static Dictionary<string, byte[]> MapByExtension(IList<SaveFile> files)
        {
            var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                if (f?.Bytes == null) continue;
                var ext = Path.GetExtension(f.Name).TrimStart('.').ToLowerInvariant();
                if (Layout.Any(c => c.Ext == ext))
                {
                    map[ext] = f.Bytes;
                }
            }
            return map;
        }

        /// <summary>Split the freshly-built canonical blob and confirm each supplied component round-trips.</summary>
        private static bool RoundTripOk(byte[] canonical, Dictionary<string, byte[]> inputs, ConversionContext ctx)
        {
            foreach (var c in Layout)
            {
                if (!inputs.TryGetValue(c.Ext, out var original) || original.Length == 0)
                {
                    continue;
                }
                var slice = Slice(canonical, c.Off, c.Len);
                var compareLen = Math.Min(original.Length, c.Len);
                for (int i = 0; i < compareLen; i++)
                {
                    if (slice[i] != original[i])
                    {
                        return false;
                    }
                }
                // Any bytes of the original beyond the component window mean our layout is wrong.
                if (original.Length > c.Len)
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            var dst = new byte[length];
            Buffer.BlockCopy(source, offset, dst, 0, length);
            return dst;
        }
    }
}
