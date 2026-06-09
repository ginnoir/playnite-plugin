using RomM.SaveSync;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace RomM.Tests
{
    /// <summary>
    /// Golden tests for the content_hash algorithm (CONTRACT.md §4). A mismatch with the server
    /// makes every save read as a conflict, so these vectors are correctness-critical.
    /// </summary>
    public sealed class SaveHashingTests
    {
        [Fact]
        public void Md5Hex_EmptyInput_MatchesKnownVector()
        {
            Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", SaveHashing.Md5Hex(new byte[0]));
        }

        [Fact]
        public void Md5Hex_Abc_MatchesKnownVector()
        {
            // RFC 1321 test vector: MD5("abc")
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", SaveHashing.Md5Hex(Encoding.ASCII.GetBytes("abc")));
        }

        [Fact]
        public void ComputeContentHash_PlainBytes_IsRawMd5()
        {
            var bytes = FilledBuffer(0x2000, 0x5A);
            Assert.Equal(SaveHashing.Md5Hex(bytes), SaveHashing.ComputeContentHash(bytes));
        }

        [Fact]
        public void ComputeContentHash_ZipBytes_MatchesCompositePerEntryAlgorithm()
        {
            // Entries deliberately added in non-sorted order; the algorithm must sort by name
            // (ordinal, mirroring Python's sorted(zf.namelist())) before joining.
            var entryA = FilledBuffer(0x800, 0x11);
            var entryB = FilledBuffer(0x100, 0x22);
            var zip = BuildZip(
                new ZipFixtureEntry("b.srm", entryB),
                new ZipFixtureEntry("A.sav", entryA));

            // Expected: md5("A.sav:<md5(entryA)>\nb.srm:<md5(entryB)>") — "A" < "b" ordinal.
            var combined = "A.sav:" + IndependentMd5(entryA) + "\n" + "b.srm:" + IndependentMd5(entryB);
            var expected = IndependentMd5(Encoding.UTF8.GetBytes(combined));

            Assert.Equal(expected, SaveHashing.ComputeContentHash(zip));
        }

        [Fact]
        public void ComputeContentHash_SingleEntryZip_MatchesCompositeAlgorithm()
        {
            var entry = FilledBuffer(0x80, 0x7E);
            var zip = BuildZip(new ZipFixtureEntry("game.srm", entry));

            var combined = "game.srm:" + IndependentMd5(entry);
            var expected = IndependentMd5(Encoding.UTF8.GetBytes(combined));

            Assert.Equal(expected, SaveHashing.ComputeContentHash(zip));
        }

        [Fact]
        public void ComputeContentHash_ZipOnDisk_MatchesInMemoryResult()
        {
            var zip = BuildZip(
                new ZipFixtureEntry("one.sav", FilledBuffer(0x40, 0x01)),
                new ZipFixtureEntry("two.sav", FilledBuffer(0x40, 0x02)));

            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, zip);
                Assert.Equal(SaveHashing.ComputeContentHash(zip), SaveHashing.ComputeContentHash(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ComputeContentHash_PlainFileOnDisk_IsRawMd5()
        {
            var bytes = FilledBuffer(0x123, 0x42);
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, bytes);
                Assert.Equal(SaveHashing.Md5Hex(bytes), SaveHashing.ComputeContentHash(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private sealed class ZipFixtureEntry
        {
            public string Name { get; }
            public byte[] Bytes { get; }

            public ZipFixtureEntry(string name, byte[] bytes)
            {
                Name = name;
                Bytes = bytes;
            }
        }

        private static byte[] BuildZip(params ZipFixtureEntry[] entries)
        {
            using (var archive = ZipArchive.Create())
            using (var output = new MemoryStream())
            {
                foreach (var entry in entries)
                {
                    archive.AddEntry(entry.Name, new MemoryStream(entry.Bytes), true);
                }
                archive.SaveTo(output, new WriterOptions(CompressionType.Deflate));
                return output.ToArray();
            }
        }

        /// <summary>MD5 computed directly with the BCL, independent of SaveHashing's helpers.</summary>
        private static string IndependentMd5(byte[] bytes)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static byte[] FilledBuffer(int length, byte value)
        {
            var buffer = new byte[length];
            for (int i = 0; i < length; i++)
            {
                buffer[i] = value;
            }
            return buffer;
        }
    }
}
