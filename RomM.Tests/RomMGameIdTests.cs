using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMGameIdTests
    {
        [Fact]
        public void Parses_valid_id_and_sha1()
        {
            bool ok = RomMGameId.TryParse("123:abcdef0123", out int id, out string sha1);

            Assert.True(ok);
            Assert.Equal(123, id);
            Assert.Equal("abcdef0123", sha1);
        }

        [Fact]
        public void Accepts_numeric_id_with_empty_sha1()
        {
            bool ok = RomMGameId.TryParse("123:", out int id, out string sha1);

            Assert.True(ok);
            Assert.Equal(123, id);
            Assert.Equal("", sha1);
        }

        [Theory]
        [InlineData(null)]            // null
        [InlineData("")]             // empty
        [InlineData("123")]          // no separator
        [InlineData("abc:def")]      // non-numeric id
        [InlineData("12:34:56")]     // too many parts
        [InlineData("!0Q0FCRQ==")]   // legacy protobuf id
        public void Rejects_malformed_ids(string gameId)
        {
            bool ok = RomMGameId.TryParse(gameId, out int id, out string sha1);

            Assert.False(ok);
            // sha1 is only assigned once parsing succeeds, so it stays null on every rejection path.
            Assert.Null(sha1);
        }
    }
}
