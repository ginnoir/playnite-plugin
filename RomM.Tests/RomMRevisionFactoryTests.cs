using System.Collections.Generic;
using RomM.Games;
using RomM.Models.RomM.Rom;
using Xunit;

namespace RomM.Tests
{
    public class RomMRevisionFactoryTests
    {
        private const string Host = "https://romm.example.com";

        [Fact]
        public void SelectPrimaryFile_picks_shallowest_path()
        {
            var files = new List<RomMFile>
            {
                new RomMFile { FileName = "deep.gba", FullPath = "a/b/c/deep.gba" },
                new RomMFile { FileName = "shallow.gba", FullPath = "a/shallow.gba" },
            };

            Assert.Equal("shallow.gba", RomMRevisionFactory.SelectPrimaryFile(files).FileName);
        }

        [Fact]
        public void SelectPrimaryFile_null_when_empty_or_null()
        {
            Assert.Null(RomMRevisionFactory.SelectPrimaryFile(new List<RomMFile>()));
            Assert.Null(RomMRevisionFactory.SelectPrimaryFile(null));
        }

        [Fact]
        public void Single_file_with_id_uses_files_content_endpoint()
        {
            var rom = new RomMRom
            {
                Id = 32,
                HasMultipleFiles = false,
                Files = new List<RomMFile> { new RomMFile { Id = 7, FileName = "game.gba", FullPath = "game.gba" } },
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.NotNull(rev);
            Assert.False(rev.HasMultipleFiles);
            Assert.Equal("game.gba", rev.FileName);
            Assert.Equal(Host + "/api/roms/7/files/content/game.gba", rev.DownloadURL);
        }

        [Fact]
        public void Single_file_without_id_falls_back_to_rom_endpoint()
        {
            var rom = new RomMRom
            {
                Id = 32,
                HasMultipleFiles = false,
                Files = new List<RomMFile> { new RomMFile { Id = null, FileName = "game.gba", FullPath = "game.gba" } },
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.Equal(Host + "/api/roms/32/content/game.gba", rev.DownloadURL);
        }

        [Fact]
        public void Single_file_returns_null_when_no_files()
        {
            var rom = new RomMRom { Id = 32, HasMultipleFiles = false, Files = new List<RomMFile>() };

            Assert.Null(RomMRevisionFactory.Build(rom, Host));
        }

        [Fact]
        public void Multi_file_uses_rom_content_endpoint()
        {
            var rom = new RomMRom
            {
                Id = 40,
                HasMultipleFiles = true,
                FileName = "Game (Disc).zip",
                Files = new List<RomMFile>(),
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.True(rev.HasMultipleFiles);
            Assert.Equal("Game (Disc).zip", rev.FileName);
            Assert.Equal(Host + "/api/roms/40/content/Game (Disc).zip", rev.DownloadURL);
        }
    }
}
