using RomM.SaveSync.Converters;
using System.Collections.Generic;
using Xunit;

namespace RomM.Tests.Converters
{
    public sealed class IdentityAndGbaConverterTests
    {
        private static ConversionContext Ctx(string targetExt = "sav")
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
        public void Identity_ToCanonical_PicksLargestFileAndRenames()
        {
            var small = new SaveFile("game.sav", Filled(0x100, 0x01));
            var large = new SaveFile("game.srm", Filled(0x8000, 0x02));
            var converter = new IdentitySaveConverter();

            var canonical = converter.ToCanonical(new List<SaveFile> { small, large }, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal("game.srm", canonical.Name);
            Assert.Equal(large.Bytes, canonical.Bytes);
        }

        [Fact]
        public void Identity_ToCanonical_EmptySet_ReturnsNull()
        {
            var converter = new IdentitySaveConverter();

            Assert.Null(converter.ToCanonical(new List<SaveFile>(), Ctx()));
        }

        [Fact]
        public void Identity_FromCanonical_RenamesToNativeTarget()
        {
            var canonical = new SaveFile("game.srm", Filled(0x8000, 0x42));
            var converter = new IdentitySaveConverter();

            var outputs = converter.FromCanonical(canonical, Ctx(targetExt: "sav"));

            Assert.NotNull(outputs);
            var file = Assert.Single(outputs);
            Assert.Equal("game.sav", file.Name);
            Assert.Equal(canonical.Bytes, file.Bytes);
        }

        [Theory]
        [InlineData(512)]     // EEPROM 4k
        [InlineData(8192)]    // EEPROM 64k
        [InlineData(32768)]   // SRAM
        [InlineData(65536)]   // Flash 512k
        [InlineData(131072)]  // Flash 1M
        [InlineData(131088)]  // Flash 1M + 16-byte inline RTC tail (Pokémon RTC carts)
        public void Gba_ToCanonical_KnownSizes_RenameVerbatim(int size)
        {
            var bytes = Filled(size, 0x3C);
            var converter = new GbaSaveConverter();

            var canonical = converter.ToCanonical(new List<SaveFile> { new SaveFile("game.sav", bytes) }, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal("game.srm", canonical.Name);
            Assert.Equal(bytes, canonical.Bytes); // no RTC surgery, no re-padding — bytes untouched
        }

        [Fact]
        public void Gba_ToCanonical_OddSize_StillSyncsVerbatim()
        {
            // Odd sizes warn (logger) but must never block or mutate the save.
            var bytes = Filled(12345, 0x3C);
            var converter = new GbaSaveConverter();

            var canonical = converter.ToCanonical(new List<SaveFile> { new SaveFile("game.sav", bytes) }, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal(bytes, canonical.Bytes);
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
