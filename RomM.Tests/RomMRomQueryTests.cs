using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMRomQueryTests
    {
        private const string Base = "https://romm.example.com/api/roms";

        [Fact]
        public void No_options_returns_base_url()
        {
            Assert.Equal(Base, RomMRomQuery.Build(Base, false, null));
        }

        [Fact]
        public void Skip_missing_files_only()
        {
            Assert.Equal(Base + "?missing=false", RomMRomQuery.Build(Base, true, null));
        }

        [Fact]
        public void Single_excluded_genre()
        {
            Assert.Equal(Base + "?genres=Adventure", RomMRomQuery.Build(Base, false, "Adventure"));
        }

        [Fact]
        public void Multiple_excluded_genres_with_missing_files()
        {
            Assert.Equal(
                Base + "?missing=false&genres=Adventure&genres=RPG",
                RomMRomQuery.Build(Base, true, "Adventure;RPG"));
        }

        [Fact]
        public void Trims_surrounding_whitespace_and_trailing_semicolons()
        {
            Assert.Equal(Base + "?genres=Adventure", RomMRomQuery.Build(Base, false, "  Adventure;  "));
        }

        [Fact]
        public void Url_encodes_spaces_in_genre_names()
        {
            Assert.Equal(Base + "?genres=Role+Playing", RomMRomQuery.Build(Base, false, "Role Playing"));
        }

        [Fact]
        public void Blank_genre_string_adds_no_query()
        {
            Assert.Equal(Base, RomMRomQuery.Build(Base, false, ";;"));
        }
    }
}
