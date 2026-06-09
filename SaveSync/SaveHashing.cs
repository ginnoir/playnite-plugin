using SharpCompress.Archives.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RomM.SaveSync
{
    /// <summary>
    /// Computes the RomM content_hash. MUST match the server (CONTRACT.md §4):
    ///   - plain file  -> md5(raw bytes) hex
    ///   - valid zip   -> for each non-dir entry sorted by name: "name:md5(entryBytes)",
    ///                    joined by '\n', then md5(that UTF-8 string) hex.
    /// A mismatch makes every save read as a conflict, so this is correctness-critical.
    /// </summary>
    internal static class SaveHashing
    {
        public static string Md5Hex(byte[] bytes)
        {
            using (var md5 = MD5.Create())
            {
                return ToHex(md5.ComputeHash(bytes));
            }
        }

        public static string Md5HexStream(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                return ToHex(md5.ComputeHash(stream));
            }
        }

        /// <summary>content_hash for a file on disk (zip-aware).</summary>
        public static string ComputeContentHash(string filePath)
        {
            if (IsZip(filePath))
            {
                return ComputeZipHash(filePath);
            }
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Md5HexStream(fs);
            }
        }

        /// <summary>content_hash for in-memory bytes we are about to upload. Zip-aware so it matches what the server will compute.</summary>
        public static string ComputeContentHash(byte[] bytes)
        {
            if (LooksLikeZip(bytes))
            {
                using (var ms = new MemoryStream(bytes, writable: false))
                {
                    return ComputeZipHash(ms);
                }
            }
            return Md5Hex(bytes);
        }

        private static string ComputeZipHash(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return ComputeZipHash(fs);
            }
        }

        private static string ComputeZipHash(Stream stream)
        {
            var lines = new List<string>();
            using (var archive = ZipArchive.Open(stream))
            {
                // Mirror Python's sorted(zf.namelist()) using the entry key, ordinal sort.
                var entries = archive.Entries
                    .Where(e => !e.IsDirectory)
                    .OrderBy(e => e.Key, StringComparer.Ordinal);

                foreach (var entry in entries)
                {
                    using (var es = entry.OpenEntryStream())
                    using (var ms = new MemoryStream())
                    {
                        es.CopyTo(ms);
                        lines.Add($"{entry.Key}:{Md5Hex(ms.ToArray())}");
                    }
                }
            }
            var combined = string.Join("\n", lines);
            return Md5Hex(Encoding.UTF8.GetBytes(combined));
        }

        private static bool IsZip(string filePath)
        {
            try
            {
                return ZipArchive.IsZipFile(filePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeZip(byte[] bytes)
        {
            // Local file header / empty-archive / spanned signatures all start with "PK".
            return bytes != null && bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
        }

        private static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
