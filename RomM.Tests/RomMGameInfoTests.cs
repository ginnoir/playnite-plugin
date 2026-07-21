using System;
using Playnite.SDK.Models;
using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMGameInfoTests
    {
        [Fact]
        public void AsGameId_round_trips_through_FromGame()
        {
            var info = new RomMGameInfo
            {
                MappingId = Guid.NewGuid(),
                DownloadUrl = "https://romm.example.com/api/roms/5/content/file.gba",
                FileName = "file.gba",
                HasMultipleFiles = false,
            };

            string id = info.AsGameId();

            // Legacy protobuf ids are base64 prefixed with "!0".
            Assert.StartsWith("!0", id);

            var back = RomMGameInfo.FromGame<RomMGameInfo>(new Game { GameId = id });

            Assert.Equal(info.MappingId, back.MappingId);
            Assert.Equal(info.DownloadUrl, back.DownloadUrl);
            Assert.Equal(info.FileName, back.FileName);
            Assert.Equal(info.HasMultipleFiles, back.HasMultipleFiles);
        }

        [Fact]
        public void AsGameId_round_trips_multifile_flag_and_via_metadata()
        {
            var info = new RomMGameInfo
            {
                MappingId = Guid.Empty,
                FileName = "disc.m3u",
                HasMultipleFiles = true,
            };

            var back = RomMGameInfo.FromGameMetadata<RomMGameInfo>(new GameMetadata { GameId = info.AsGameId() });

            Assert.True(back.HasMultipleFiles);
            Assert.Equal("disc.m3u", back.FileName);
            Assert.Equal(Guid.Empty, back.MappingId);
        }
    }
}
