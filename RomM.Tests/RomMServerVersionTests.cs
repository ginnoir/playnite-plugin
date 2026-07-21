using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMServerVersionTests
    {
        [Theory]
        // Supported: 4.9 and newer.
        [InlineData("4.9", true)]
        [InlineData("4.9.0", true)]
        [InlineData("4.10", true)]
        [InlineData("5.0", true)]
        [InlineData("10.0.0", true)]
        // Pre-release / build suffixes are stripped, then compared.
        [InlineData("4.9.0-beta", true)]
        [InlineData("4.9.0-beta.1", true)]
        [InlineData("4.9.0+build7", true)]
        // Dev / non-numeric / missing versions are assumed compatible.
        [InlineData("development", true)]
        [InlineData("", true)]
        [InlineData(null, true)]
        // Positively-too-old versions are blocked.
        [InlineData("4.8", false)]
        [InlineData("4.8.9", false)]
        [InlineData("3.0", false)]
        [InlineData("4.8.0-beta", false)]
        public void SupportsImport_classifies_versions(string version, bool expected)
        {
            Assert.Equal(expected, RomMServerVersion.SupportsImport(version));
        }
    }
}
