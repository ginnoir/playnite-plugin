using System.IO;
using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMInstallPathsTests
    {
        private const string Root = "GAMES_ROOT";

        [Fact]
        public void InstallDir_is_root_plus_filename_without_extension()
        {
            Assert.Equal(
                Path.Combine(Root, "Advance Wars (USA)"),
                RomMInstallPaths.InstallDir(Root, "Advance Wars (USA).gba"));
        }

        [Fact]
        public void GamePath_is_install_dir_plus_full_filename()
        {
            const string fileName = "Advance Wars (USA).gba";
            Assert.Equal(
                Path.Combine(Root, "Advance Wars (USA)", fileName),
                RomMInstallPaths.GamePath(Root, fileName));
        }

        [Fact]
        public void Handles_filename_without_extension()
        {
            Assert.Equal(Path.Combine(Root, "game"), RomMInstallPaths.InstallDir(Root, "game"));
            Assert.Equal(Path.Combine(Root, "game", "game"), RomMInstallPaths.GamePath(Root, "game"));
        }
    }
}
