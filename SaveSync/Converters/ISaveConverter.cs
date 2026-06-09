using Playnite.SDK;
using System.Collections.Generic;

namespace RomM.SaveSync.Converters
{
    /// <summary>An in-memory save file: a filename (with extension) and its bytes.</summary>
    internal sealed class SaveFile
    {
        public string Name { get; }
        public byte[] Bytes { get; }

        public SaveFile(string name, byte[] bytes)
        {
            Name = name;
            Bytes = bytes;
        }
    }

    internal sealed class ConversionContext
    {
        public string BaseName { get; set; }              // ROM basename (no extension)
        public string CanonicalExtension { get; set; }    // e.g. "srm"
        public string TargetNativeExtension { get; set; } // emulator's native ext for FromCanonical (e.g. "sav")
        public ILogger Logger { get; set; }
    }

    /// <summary>
    /// Translates a save between an emulator's native on-disk layout and the single canonical file the
    /// plugin stores in RomM. Converters MUST be lossless and reversible; a converter that cannot
    /// guarantee a clean round-trip should return null so the engine falls back to "sync as-is + warn"
    /// rather than risk corrupting a save.
    /// </summary>
    internal interface ISaveConverter
    {
        /// <summary>ConverterFamily this handles ("raw", "gba", "psx", "n64", …) — see PlatformSaveProfiles.</summary>
        string Family { get; }

        /// <summary>
        /// Collapse the emulator's native file(s) into one canonical file for RomM, or null if it
        /// cannot be done safely. <paramref name="nativeFiles"/> is the set the locator found for one game.
        /// </summary>
        SaveFile ToCanonical(IList<SaveFile> nativeFiles, ConversionContext ctx);

        /// <summary>
        /// Expand the canonical RomM file into the native file(s) the target emulator expects, or null
        /// if it cannot be done safely.
        /// </summary>
        IList<SaveFile> FromCanonical(SaveFile canonical, ConversionContext ctx);
    }
}
