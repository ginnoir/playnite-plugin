using System;
using System.IO;

namespace RomM.SaveSync
{
    /// <summary>
    /// A local save/state file resolved on disk, with the fields RomM's negotiate needs.
    /// <see cref="UpdatedAtUtc"/> is the file's last-write time in UTC — it must be stable and
    /// monotonic, because the server's conflict engine compares it (CONTRACT.md §3).
    /// </summary>
    internal sealed class LocalSave
    {
        public string Path { get; }
        public string FileName { get; }
        public long SizeBytes { get; }
        public DateTime UpdatedAtUtc { get; }
        public string ContentHash { get; }
        public string Slot { get; }
        public string Emulator { get; }

        private LocalSave(string path, long size, DateTime updatedUtc, string hash, string slot, string emulator)
        {
            Path = path;
            FileName = System.IO.Path.GetFileName(path);
            SizeBytes = size;
            UpdatedAtUtc = updatedUtc;
            ContentHash = hash;
            Slot = slot;
            Emulator = emulator;
        }

        /// <summary>Reads size, UTC mtime and the MD5 content hash for a file. Returns null if it can't be read.</summary>
        public static LocalSave FromFile(string path, string emulator = null, string slot = null)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return null;
                }
                var updatedUtc = info.LastWriteTimeUtc;
                var hash = SaveHashing.ComputeContentHash(path);
                return new LocalSave(path, info.Length, updatedUtc, hash, slot, emulator);
            }
            catch
            {
                return null;
            }
        }
    }
}
