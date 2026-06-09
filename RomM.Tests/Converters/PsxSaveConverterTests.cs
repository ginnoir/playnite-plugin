using RomM.SaveSync.Converters;
using System;
using System.Collections.Generic;
using Xunit;

namespace RomM.Tests.Converters
{
    /// <summary>
    /// PSX memory cards: raw 128 KB image is canonical; .vmp (128 B header) and .gme (3904 B header)
    /// are containers we strip but never regenerate (their headers carry signatures we can't synthesize).
    /// </summary>
    public sealed class PsxSaveConverterTests
    {
        private const int RawCardSize = 131072;
        private const int VmpHeaderSize = 128;
        private const int GmeHeaderSize = 3904;

        private readonly PsxSaveConverter _converter = new PsxSaveConverter();

        private static ConversionContext Ctx(string targetExt = "mcd")
        {
            return new ConversionContext
            {
                BaseName = "game",
                CanonicalExtension = "srm",
                TargetNativeExtension = targetExt,
                Logger = null,
            };
        }

        [Fact]
        public void ToCanonical_RawCard_RenamesWithoutTouchingBytes()
        {
            var raw = Filled(RawCardSize, 0x5C);
            var native = new List<SaveFile> { new SaveFile("game.mcd", raw) };

            var canonical = _converter.ToCanonical(native, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal("game.srm", canonical.Name);
            Assert.Equal(raw, canonical.Bytes);
        }

        [Fact]
        public void ToCanonical_Vmp_StripsHeaderToRawImage()
        {
            var card = BuildContainer(VmpHeaderSize, headerByte: 0xAA, bodyByte: 0x5C);
            var native = new List<SaveFile> { new SaveFile("game.vmp", card) };

            var canonical = _converter.ToCanonical(native, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal(RawCardSize, canonical.Bytes.Length);
            Assert.All(canonical.Bytes, b => Assert.Equal(0x5C, b));
        }

        [Fact]
        public void ToCanonical_Gme_StripsHeaderToRawImage()
        {
            var card = BuildContainer(GmeHeaderSize, headerByte: 0xBB, bodyByte: 0x6D);
            var native = new List<SaveFile> { new SaveFile("game.gme", card) };

            var canonical = _converter.ToCanonical(native, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal(RawCardSize, canonical.Bytes.Length);
            Assert.All(canonical.Bytes, b => Assert.Equal(0x6D, b));
        }

        [Fact]
        public void ToCanonical_UnexpectedSize_ReturnsNullSoEngineSyncsAsIs()
        {
            var native = new List<SaveFile> { new SaveFile("game.mcd", Filled(0x1000, 0x01)) };

            Assert.Null(_converter.ToCanonical(native, Ctx()));
        }

        [Theory]
        [InlineData("mcd")]
        [InlineData("mcr")]
        [InlineData("srm")]
        [InlineData("mc")]
        public void FromCanonical_RawTargets_RenameOnly(string target)
        {
            var canonical = new SaveFile("game.srm", Filled(RawCardSize, 0x5C));

            var outputs = _converter.FromCanonical(canonical, Ctx(targetExt: target));

            Assert.NotNull(outputs);
            var file = Assert.Single(outputs);
            Assert.Equal("game." + target, file.Name);
            Assert.Equal(canonical.Bytes, file.Bytes);
        }

        [Theory]
        [InlineData("vmp")]
        [InlineData("gme")]
        public void FromCanonical_SignedContainerTargets_RefuseWithNull(string target)
        {
            var canonical = new SaveFile("game.srm", Filled(RawCardSize, 0x5C));

            Assert.Null(_converter.FromCanonical(canonical, Ctx(targetExt: target)));
        }

        private static byte[] BuildContainer(int headerSize, byte headerByte, byte bodyByte)
        {
            var bytes = new byte[headerSize + RawCardSize];
            for (int i = 0; i < headerSize; i++)
            {
                bytes[i] = headerByte;
            }
            for (int i = headerSize; i < bytes.Length; i++)
            {
                bytes[i] = bodyByte;
            }
            return bytes;
        }

        private static byte[] Filled(int length, byte value)
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
