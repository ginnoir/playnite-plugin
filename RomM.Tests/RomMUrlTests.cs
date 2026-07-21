using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMUrlTests
    {
        [Theory]
        [InlineData("https://h", "api/roms", "https://h/api/roms")]
        [InlineData("https://h/", "api/roms", "https://h/api/roms")]
        [InlineData("https://h", "/api/roms", "https://h/api/roms")]
        [InlineData("https://h/", "/api/roms", "https://h/api/roms")]
        [InlineData("https://h///", "///api/roms", "https://h/api/roms")]
        public void Combine_joins_with_exactly_one_slash(string baseUrl, string relative, string expected)
        {
            Assert.Equal(expected, RomMUrl.Combine(baseUrl, relative));
        }

        [Fact]
        public void Null_relative_yields_base_with_trailing_slash()
        {
            Assert.Equal("https://h/", RomMUrl.Combine("https://h", null));
        }
    }
}
