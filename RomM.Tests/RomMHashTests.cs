using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMHashTests
    {
        [Fact]
        public void Produces_40_char_lowercase_hex()
        {
            var hash = RomMHash.FallbackSha1Hex(5, "Advance Wars");

            Assert.Equal(40, hash.Length);
            Assert.Matches("^[0-9a-f]{40}$", hash);
        }

        [Fact]
        public void Is_deterministic()
        {
            Assert.Equal(
                RomMHash.FallbackSha1Hex(5, "Advance Wars"),
                RomMHash.FallbackSha1Hex(5, "Advance Wars"));
        }

        [Fact]
        public void Varies_with_both_id_and_name()
        {
            var baseline = RomMHash.FallbackSha1Hex(5, "Advance Wars");

            Assert.NotEqual(baseline, RomMHash.FallbackSha1Hex(6, "Advance Wars"));
            Assert.NotEqual(baseline, RomMHash.FallbackSha1Hex(5, "Advance Wars 2"));
        }
    }
}
