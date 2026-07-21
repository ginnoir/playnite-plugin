using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;
using RomM.Games;
using RomM.Models.RomM.Rom;
using Xunit;

namespace RomM.Tests
{
    public class RomMMetadataMapperTests
    {
        private const string Host = "https://romm.example.com";
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Minimal rom with the non-null members BuildBaseMetadata dereferences.
        private static RomMRom NewRom()
        {
            return new RomMRom
            {
                Name = "Advance Wars",
                Summary = "A turn-based strategy game.",
                Metadatum = new metadatum(),
                RomUser = new RomMRomUser(),
                Regions = new List<string>(),
            };
        }

        private static string[] Names(IEnumerable<MetadataProperty> props)
        {
            if (props == null)
                return new string[0];

            return props.OfType<MetadataNameProperty>().Select(p => p.Name).OrderBy(n => n).ToArray();
        }

        private static GameMetadata Map(RomMRom rom, string board = "ESRB")
        {
            return RomMMetadataMapper.BuildBaseMetadata(rom, Host, board);
        }

        [Fact]
        public void Maps_name_and_description()
        {
            var md = Map(NewRom());

            Assert.Equal("Advance Wars", md.Name);
            Assert.Equal("A turn-based strategy game.", md.Description);
        }

        [Fact]
        public void Icon_uses_miximage_path_under_resource_mount()
        {
            var rom = NewRom();
            rom.SSMetadata = new RomMSSMetadata { MiximagePath = "roms/2/32/miximage/miximage.png" };

            var md = Map(rom);

            Assert.NotNull(md.Icon);
            Assert.Equal(Host + "/assets/romm/resources/roms/2/32/miximage/miximage.png", md.Icon.Path);
        }

        [Fact]
        public void Icon_falls_back_to_miximage_url_when_no_path()
        {
            var rom = NewRom();
            rom.SSMetadata = new RomMSSMetadata { MiximageUrl = "https://ss.example/mix.png" };

            var md = Map(rom);

            Assert.NotNull(md.Icon);
            Assert.Equal("https://ss.example/mix.png", md.Icon.Path);
        }

        [Fact]
        public void Icon_prefers_path_over_url()
        {
            var rom = NewRom();
            rom.SSMetadata = new RomMSSMetadata
            {
                MiximagePath = "roms/2/32/miximage/miximage.png",
                MiximageUrl = "https://ss.example/mix.png",
            };

            var md = Map(rom);

            Assert.Equal(Host + "/assets/romm/resources/roms/2/32/miximage/miximage.png", md.Icon.Path);
        }

        [Fact]
        public void Icon_is_null_without_ss_metadata()
        {
            Assert.Null(Map(NewRom()).Icon);
        }

        [Fact]
        public void Icon_is_null_when_miximage_absent()
        {
            var rom = NewRom();
            rom.SSMetadata = new RomMSSMetadata();

            Assert.Null(Map(rom).Icon);
        }

        [Fact]
        public void Cover_uses_host_plus_path_without_resource_mount()
        {
            var rom = NewRom();
            rom.PathCoverL = "/assets/romm/resources/roms/2/32/cover/big.png";

            var md = Map(rom);

            Assert.NotNull(md.CoverImage);
            Assert.Equal(Host + "/assets/romm/resources/roms/2/32/cover/big.png", md.CoverImage.Path);
        }

        [Fact]
        public void Cover_is_null_without_path()
        {
            Assert.Null(Map(NewRom()).CoverImage);
        }

        [Fact]
        public void Maps_collection_fields_and_skips_empty_strings()
        {
            var rom = NewRom();
            rom.Metadatum.Genres = new List<string> { "Strategy", "", "Tactics" };
            rom.Metadatum.Franchises = new List<string> { "Advance Wars" };
            rom.Metadatum.Gamemodes = new List<string> { "Single player" };
            rom.Metadatum.Collections = new List<string> { "Nintendo" };
            rom.Regions = new List<string> { "USA", "" };

            var md = Map(rom);

            Assert.Equal(new[] { "Strategy", "Tactics" }, Names(md.Genres));
            Assert.Equal(new[] { "Advance Wars" }, Names(md.Series));
            Assert.Equal(new[] { "Single player" }, Names(md.Features));
            Assert.Equal(new[] { "Nintendo" }, Names(md.Categories));
            Assert.Equal(new[] { "USA" }, Names(md.Regions));
        }

        [Fact]
        public void Age_ratings_keep_only_preferred_board()
        {
            var rom = NewRom();
            rom.Metadatum.Age_Ratings = new List<string> { "ESRB:Everyone", "PEGI:3" };

            var md = Map(rom, board: "ESRB");

            Assert.Equal(new[] { "ESRB:Everyone" }, Names(md.AgeRatings));
        }

        [Fact]
        public void Age_ratings_null_when_none_present()
        {
            Assert.Null(Map(NewRom()).AgeRatings);
        }

        [Fact]
        public void Builds_external_links_for_present_ids()
        {
            var rom = NewRom();
            rom.SSId = 190935;
            rom.HasheousId = 42;
            rom.RAId = 7;
            rom.HLTBId = 99;

            var md = Map(rom);

            Assert.Equal(4, md.Links.Count);
            Assert.Contains(md.Links, l => l.Name == "Screenscraper" && l.Url.Contains("gameid=190935"));
            Assert.Contains(md.Links, l => l.Name == "Hasheous" && l.Url.Contains("id=42"));
            Assert.Contains(md.Links, l => l.Name == "RetroAchievements" && l.Url.EndsWith("/game/7"));
            Assert.Contains(md.Links, l => l.Name == "HowLongToBeat" && l.Url.EndsWith("/game/99"));
        }

        [Fact]
        public void No_links_when_no_ids()
        {
            Assert.Empty(Map(NewRom()).Links);
        }

        [Fact]
        public void Maps_scores_and_last_activity()
        {
            var rom = NewRom();
            rom.Metadatum.Average_Rating = 7.5f;
            rom.RomUser.Rating = 8;
            rom.RomUser.LastPlayed = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            var md = Map(rom);

            Assert.Equal(7, md.CommunityScore);  // 1-10 float truncated to int
            Assert.Equal(80, md.UserScore);      // 1-10 rating scaled to Playnite's 1-100
            Assert.Equal(rom.RomUser.LastPlayed, md.LastActivity);
        }

        [Fact]
        public void Maps_release_date_from_epoch_milliseconds()
        {
            var rom = NewRom();
            long ms = 1_700_000_000_000L;
            rom.Metadatum.Release_Date = ms;

            var md = Map(rom);

            // Recompute identically (incl. ToLocalTime) so the assertion is timezone-stable.
            var expected = new ReleaseDate(UnixEpoch.AddMilliseconds(ms).ToLocalTime());
            Assert.True(md.ReleaseDate.HasValue);
            Assert.Equal(expected, md.ReleaseDate.Value);
        }
    }
}
