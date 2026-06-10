using RomM.SaveSync;
using System;
using System.IO;
using Xunit;

namespace RomM.Tests
{
    /// <summary>
    /// Covers the RetroArch sort_*_enable (per-core subfolder) resolution added for Scoop installs:
    /// core-token extraction from emulator args, corename lookup via the shared cfg parser, and the
    /// pure subfolder picker (auto-detect existing folder, disambiguate, fresh-machine fallback).
    /// </summary>
    public sealed class SaveLocatorRetroArchSortTests
    {
        // ---- ExtractLibretroCoreToken --------------------------------------

        [Fact]
        public void ExtractLibretroCoreToken_SimpleCore_ReturnsToken()
        {
            var args = "-L \"C:\\scoop\\apps\\retroarch\\current\\cores\\mgba_libretro.dll\" \"{ImagePath}\"";
            Assert.Equal("mgba_libretro", SaveLocator.ExtractLibretroCoreToken(args));
        }

        [Fact]
        public void ExtractLibretroCoreToken_MultiUnderscoreCore_KeepsFullToken()
        {
            // The token must not stop at the first underscore (mupen64plus_next_libretro, not next_libretro).
            var args = "-L cores\\mupen64plus_next_libretro.dll rom.n64";
            Assert.Equal("mupen64plus_next_libretro", SaveLocator.ExtractLibretroCoreToken(args));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("--fullscreen \"rom.gba\"")]
        public void ExtractLibretroCoreToken_NoCore_ReturnsNull(string args)
        {
            Assert.Null(SaveLocator.ExtractLibretroCoreToken(args));
        }

        // ---- corename via ParseRetroArchConfig (.info reuse) ---------------

        [Fact]
        public void ParseRetroArchConfig_InfoFile_ExposesCoreName()
        {
            var info = new[]
            {
                "# Mupen-style .info header comment",
                "display_name = \"Nintendo - Game Boy Advance (mGBA)\"",
                "corename = \"mGBA\"",
                "systemname = \"Game Boy Advance\"",
            };
            var dict = SaveLocator.ParseRetroArchConfig(info);
            Assert.Equal("mGBA", dict["corename"]);
        }

        // ---- DetectCoreSubfolder -------------------------------------------

        [Fact]
        public void DetectCoreSubfolder_SingleMatchingFolder_ReturnsIt()
        {
            WithTempBase((baseDir, baseName) =>
            {
                MakeSave(baseDir, "mGBA", baseName);
                Assert.Equal("mGBA", SaveLocator.DetectCoreSubfolder(baseDir, baseName, preferredCore: null));
            });
        }

        [Fact]
        public void DetectCoreSubfolder_MultipleFolders_PrefersDerivedCore()
        {
            WithTempBase((baseDir, baseName) =>
            {
                // A stale by-content folder beside the live core folder — both hold the save.
                MakeSave(baseDir, baseName, baseName);   // content-named folder
                MakeSave(baseDir, "mGBA", baseName);     // core-named folder
                Assert.Equal("mGBA", SaveLocator.DetectCoreSubfolder(baseDir, baseName, preferredCore: "mGBA"));
            });
        }

        [Fact]
        public void DetectCoreSubfolder_MultipleFolders_NoPreference_UsesNewest()
        {
            WithTempBase((baseDir, baseName) =>
            {
                var older = MakeSave(baseDir, "OldCore", baseName);
                var newer = MakeSave(baseDir, "NewCore", baseName);
                File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(newer, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                Assert.Equal("NewCore", SaveLocator.DetectCoreSubfolder(baseDir, baseName, preferredCore: null));
            });
        }

        [Fact]
        public void DetectCoreSubfolder_NoExistingFolder_FallsBackToPreferred()
        {
            WithTempBase((baseDir, baseName) =>
            {
                // Never launched here: no subfolder yet — use the derived core name for the write target.
                Assert.Equal("mGBA", SaveLocator.DetectCoreSubfolder(baseDir, baseName, preferredCore: "mGBA"));
            });
        }

        [Fact]
        public void DetectCoreSubfolder_NothingResolvable_ReturnsNull()
        {
            WithTempBase((baseDir, baseName) =>
            {
                Assert.Null(SaveLocator.DetectCoreSubfolder(baseDir, baseName, preferredCore: null));
            });
        }

        // ---- helpers -------------------------------------------------------

        /// <summary>Creates "&lt;baseDir&gt;\&lt;folder&gt;\&lt;baseName&gt;.srm" and returns its path.</summary>
        private static string MakeSave(string baseDir, string folder, string baseName)
        {
            var dir = Path.Combine(baseDir, folder);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, baseName + ".srm");
            File.WriteAllBytes(path, new byte[] { 0x00 });
            return path;
        }

        private static void WithTempBase(Action<string, string> test)
        {
            const string baseName = "Pokemon - Radical Red (Hack)";
            var baseDir = Path.Combine(Path.GetTempPath(), "romm-savelocator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            try
            {
                test(baseDir, baseName);
            }
            finally
            {
                try { Directory.Delete(baseDir, recursive: true); } catch { /* best-effort cleanup */ }
            }
        }
    }
}
