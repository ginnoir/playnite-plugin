using RomM.SaveSync.Converters;
using Xunit;

namespace RomM.Tests.Converters
{
    public sealed class ConverterRegistryTests
    {
        [Theory]
        [InlineData("gba", typeof(GbaSaveConverter))]
        [InlineData("GBA", typeof(GbaSaveConverter))] // case-insensitive
        [InlineData("psx", typeof(PsxSaveConverter))]
        [InlineData("n64", typeof(N64SaveConverter))]
        public void For_KnownFamilies_ReturnsDedicatedConverter(string family, System.Type expected)
        {
            Assert.IsType(expected, ConverterRegistry.For(family));
        }

        [Theory]
        [InlineData("raw")]
        [InlineData("nds")]
        [InlineData("unknown-platform")]
        [InlineData("")]
        [InlineData(null)]
        public void For_RawUnknownOrMissing_FallsBackToIdentity(string family)
        {
            var converter = ConverterRegistry.For(family);

            Assert.IsType<IdentitySaveConverter>(converter);
        }
    }
}
