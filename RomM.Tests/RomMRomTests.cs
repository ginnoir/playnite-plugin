using Newtonsoft.Json;
using RomM.Models.RomM.Rom;
using Xunit;

namespace RomM.Tests
{
    public class RomMRomTests
    {
        // Mirrors the real /api/roms payload shape, including the keys that have drifted historically
        // (sibling_roms, sha1_hash) and the Screenscraper media we map to the icon.
        private const string FullRomJson = @"{
            ""id"": 32,
            ""name"": ""Advance Wars"",
            ""sha1_hash"": ""deadbeef"",
            ""fs_name"": ""Advance Wars (USA).gba"",
            ""platform_name"": ""Game Boy Advance"",
            ""metadatum"": {
                ""genres"": [""Strategy"", ""Tactics""],
                ""first_release_date"": 1280793600,
                ""average_rating"": 8.5
            },
            ""ss_metadata"": {
                ""miximage_path"": ""roms/2/32/miximage/miximage.png"",
                ""miximage_url"": ""https://neoclone.screenscraper.fr/api2/mediaJeu.php?media=mixrbv1""
            },
            ""sibling_roms"": [
                { ""id"": 33, ""name"": ""Advance Wars 2"", ""fs_name_no_ext"": ""Advance Wars 2"" }
            ],
            ""rom_user"": {
                ""rating"": 8,
                ""last_played"": ""2024-01-02T03:04:05Z""
            }
        }";

        [Fact]
        public void Deserializes_core_nested_and_drifted_fields()
        {
            var rom = JsonConvert.DeserializeObject<RomMRom>(FullRomJson);

            Assert.Equal(32, rom.Id);
            Assert.Equal("Advance Wars", rom.Name);
            Assert.Equal("deadbeef", rom.SHA1);

            Assert.Contains("Strategy", rom.Metadatum.Genres);
            Assert.Contains("Tactics", rom.Metadatum.Genres);
            Assert.Equal(8.5f, rom.Metadatum.Average_Rating);

            Assert.Equal("roms/2/32/miximage/miximage.png", rom.SSMetadata.MiximagePath);
            Assert.StartsWith("https://", rom.SSMetadata.MiximageUrl);

            Assert.Single(rom.Siblings);
            Assert.Equal(33, rom.Siblings[0].Id);
            Assert.Equal("Advance Wars 2", rom.Siblings[0].Name);

            Assert.Equal(8, rom.RomUser.Rating);
            Assert.True(rom.RomUser.LastPlayed.HasValue);
        }

        [Fact]
        public void Normalize_fills_missing_collections_and_objects()
        {
            var rom = JsonConvert.DeserializeObject<RomMRom>(@"{ ""id"": 5, ""name"": ""x"" }");

            // RomM 4.9+ can omit these entirely; they deserialize as null.
            Assert.Null(rom.RomUser);
            Assert.Null(rom.Metadatum);
            Assert.Null(rom.Siblings);

            rom.Normalize();

            Assert.NotNull(rom.Metadatum);
            Assert.NotNull(rom.RomUser);
            Assert.NotNull(rom.Regions);
            Assert.NotNull(rom.Tags);
            Assert.NotNull(rom.Files);
            Assert.NotNull(rom.Siblings);
            // metadatum initialises its own collections, so consumers never null-check them either.
            Assert.NotNull(rom.Metadatum.Genres);
        }
    }
}
