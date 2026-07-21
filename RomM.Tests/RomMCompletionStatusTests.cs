using RomM.Games;
using RomM.Models.RomM.Rom;
using Xunit;

namespace RomM.Tests
{
    public class RomMCompletionStatusTests
    {
        [Fact]
        public void Now_playing_takes_precedence_over_status_and_backlog()
        {
            var name = RomMCompletionStatus.ResolvePlayniteStatusName(
                new RomMRomUser { NowPlaying = true, Backlogged = true, Status = "finished" });

            Assert.Equal("Playing", name);
        }

        [Fact]
        public void Backlogged_when_not_now_playing()
        {
            var name = RomMCompletionStatus.ResolvePlayniteStatusName(
                new RomMRomUser { Backlogged = true, Status = "finished" });

            Assert.Equal("Plan to Play", name);
        }

        [Theory]
        [InlineData("finished", "Beaten")]
        [InlineData("completed_100", "Completed")]
        [InlineData("retired", "Played")]
        [InlineData("incomplete", "On Hold")]
        [InlineData("never_playing", "Abandoned")]
        [InlineData("not_played", "Not Played")]
        public void Maps_known_status(string status, string expected)
        {
            Assert.Equal(expected, RomMCompletionStatus.ResolvePlayniteStatusName(new RomMRomUser { Status = status }));
        }

        [Fact]
        public void Unknown_status_falls_back_to_not_played()
        {
            Assert.Equal("Not Played",
                RomMCompletionStatus.ResolvePlayniteStatusName(new RomMRomUser { Status = "something_new" }));
        }

        [Fact]
        public void Null_status_falls_back_to_not_played()
        {
            Assert.Equal("Not Played",
                RomMCompletionStatus.ResolvePlayniteStatusName(new RomMRomUser { Status = null }));
        }
    }
}
