using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RomM.SaveSync
{
    /// <summary>Resolved on-disk locations for one game's saves/states under one emulator mapping.</summary>
    internal class ResolvedSavePaths
    {
        public string SaveDir { get; set; }      // directory battery saves live in (may equal ContentDir)
        public string StateDir { get; set; }     // directory savestates live in
        public string ContentDir { get; set; }   // directory containing the ROM file
        public string RomBaseName { get; set; }  // file name without extension the emulator keys saves on
        public bool Resolved => !string.IsNullOrEmpty(RomBaseName) && (SaveDir != null || ContentDir != null);
    }

    /// <summary>
    /// Determines WHERE an emulator stores a game's saves/states. It does not decide WHICH extensions
    /// are saves (the caller supplies those from the format/converter layer, Task 5/6) — it only
    /// resolves directories + the ROM basename and enumerates matching files.
    /// </summary>
    internal class SaveLocator
    {
        private readonly ILogger _logger;

        public SaveLocator(ILogger logger)
        {
            _logger = logger;
        }

        public ResolvedSavePaths Resolve(EmulatorMapping mapping, Game game)
        {
            var romPath = game?.Roms?.FirstOrDefault()?.Path;
            // For .m3u/multi-disc or missing rom records, fall back to the install dir.
            var contentDir = !string.IsNullOrEmpty(romPath) && File.Exists(romPath)
                ? Path.GetDirectoryName(romPath)
                : game?.InstallDirectory;

            var baseName = !string.IsNullOrEmpty(romPath)
                ? Path.GetFileNameWithoutExtension(romPath)
                : (game?.Name);

            var result = new ResolvedSavePaths { ContentDir = contentDir, RomBaseName = baseName };

            var strategy = mapping.SaveStrategy;
            if (strategy == SaveLocatorStrategy.Auto)
            {
                strategy = InferStrategy(mapping);
            }

            switch (strategy)
            {
                case SaveLocatorStrategy.RetroArch:
                case SaveLocatorStrategy.Scoop:
                    // Scoop is RetroArch with the per-core sort subfolder always in play; the cfg's
                    // ":\saves" points at Scoop's persisted save root via the current\ junction.
                    ResolveRetroArch(mapping, contentDir, result);
                    break;

                case SaveLocatorStrategy.Custom:
                    result.SaveDir = mapping.SaveDirOverrideResolved;
                    result.StateDir = mapping.SaveDirOverrideResolved;
                    break;

                case SaveLocatorStrategy.EmulatorSaveFolder:
                    result.SaveDir = FindEmulatorSaveFolder(mapping) ?? contentDir;
                    result.StateDir = result.SaveDir;
                    break;

                case SaveLocatorStrategy.NextToRom:
                default:
                    result.SaveDir = contentDir;
                    result.StateDir = contentDir;
                    break;
            }

            // A Custom override with no value falls back to content dir rather than null-crashing.
            if (string.IsNullOrEmpty(result.SaveDir)) result.SaveDir = contentDir;
            if (string.IsNullOrEmpty(result.StateDir)) result.StateDir = result.SaveDir;

            return result;
        }

        /// <summary>Existing files in <paramref name="dir"/> named "<basename>.<ext>" for any supplied extension (case-insensitive, no dot).</summary>
        public IList<string> EnumerateByExtensions(string dir, string baseName, IEnumerable<string> extensions)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName) || !Directory.Exists(dir))
            {
                return found;
            }

            var exts = new HashSet<string>(extensions.Select(e => e.TrimStart('.').ToLowerInvariant()));
            foreach (var file in Directory.EnumerateFiles(dir, baseName + ".*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                if (exts.Contains(ext))
                {
                    found.Add(file);
                }
            }
            return found;
        }

        /// <summary>RetroArch savestates: "<base>.state", "<base>.state1".."<base>.stateN", "<base>.state.auto".</summary>
        public IList<string> EnumerateRetroArchStates(string dir, string baseName)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName) || !Directory.Exists(dir))
            {
                return found;
            }

            var pattern = new Regex(@"\.state(\d+|\.auto)?$", RegexOptions.IgnoreCase);
            foreach (var file in Directory.EnumerateFiles(dir, baseName + ".state*", SearchOption.TopDirectoryOnly))
            {
                var tail = file.Substring(Path.Combine(dir, baseName).Length);
                if (pattern.IsMatch(tail))
                {
                    found.Add(file);
                }
            }
            return found;
        }

        private static SaveLocatorStrategy InferStrategy(EmulatorMapping mapping)
        {
            var name = (mapping.Emulator?.Name ?? "") + " " + (mapping.EmulatorBasePath ?? "");
            if (name.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SaveLocatorStrategy.RetroArch;
            }
            return SaveLocatorStrategy.NextToRom;
        }

        // ---- RetroArch -----------------------------------------------------

        private void ResolveRetroArch(EmulatorMapping mapping, string contentDir, ResolvedSavePaths result)
        {
            var cfg = LoadRetroArchConfig(mapping);
            if (cfg == null)
            {
                // No cfg found: RetroArch's out-of-box default keeps saves beside the content.
                result.SaveDir = contentDir;
                result.StateDir = contentDir;
                return;
            }

            result.SaveDir = ResolveRetroArchDir(cfg, contentDir, result.RomBaseName, "savefile_directory", "savefiles_in_content_dir",
                "sort_savefiles_enable", "sort_savefiles_by_content_enable", mapping);
            result.StateDir = ResolveRetroArchDir(cfg, contentDir, result.RomBaseName, "savestate_directory", "savestates_in_content_dir",
                "sort_savestates_enable", "sort_savestates_by_content_enable", mapping);
        }

        private string ResolveRetroArchDir(IDictionary<string, string> cfg, string contentDir, string romBaseName,
            string dirKey, string inContentKey, string sortEnableKey, string sortByContentKey, EmulatorMapping mapping)
        {
            cfg.TryGetValue(dirKey, out var dir);

            var inContent = IsTrue(cfg, inContentKey);
            if (inContent || string.IsNullOrWhiteSpace(dir) || dir.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                dir = contentDir;
            }
            else
            {
                dir = ExpandRetroArchPath(dir, mapping);
            }

            if (string.IsNullOrEmpty(dir)) return contentDir;

            // RetroArch optionally nests saves a level deeper. by-content takes precedence over
            // by-core when both flags are set, so test it first.
            if (IsTrue(cfg, sortByContentKey) && !string.IsNullOrEmpty(contentDir))
            {
                dir = Path.Combine(dir, new DirectoryInfo(contentDir).Name);
            }
            else if (IsTrue(cfg, sortEnableKey))
            {
                // sort_*_enable nests by the core's display name, e.g. "saves\mGBA\<rom>.srm".
                var coreFolder = ResolveCoreSortFolder(dir, romBaseName, mapping);
                if (!string.IsNullOrEmpty(coreFolder))
                {
                    dir = Path.Combine(dir, coreFolder);
                }
                else
                {
                    _logger.Warn($"RetroArch sort-by-core is enabled but the core subfolder under '{dir}' " +
                        $"could not be determined for '{romBaseName}'; using the base save dir. " +
                        "Launch the game once, or set a Custom save dir override, if saves aren't found.");
                }
            }

            return dir;
        }

        private IDictionary<string, string> LoadRetroArchConfig(EmulatorMapping mapping)
        {
            var basePath = mapping.EmulatorBasePathResolved;
            if (string.IsNullOrEmpty(basePath))
            {
                return null;
            }

            // Common cfg locations relative to the RetroArch install dir.
            var candidates = new[]
            {
                Path.Combine(basePath, "retroarch.cfg"),
                Path.Combine(basePath, "config", "retroarch.cfg"),
            };

            var cfgPath = candidates.FirstOrDefault(File.Exists);
            if (cfgPath == null)
            {
                _logger.Warn($"RetroArch cfg not found under {basePath}; assuming saves beside content.");
                return null;
            }

            try
            {
                return ParseRetroArchConfig(File.ReadAllLines(cfgPath));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to parse {cfgPath}.");
                return null;
            }
        }

        // Lines look like: savefile_directory = "C:\RetroArch\saves"
        private static readonly Regex CfgLine = new Regex(@"^\s*([A-Za-z0-9_]+)\s*=\s*""?(.*?)""?\s*$");

        internal static IDictionary<string, string> ParseRetroArchConfig(IEnumerable<string> lines)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                {
                    continue;
                }
                var m = CfgLine.Match(line);
                if (m.Success)
                {
                    dict[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
            return dict;
        }

        private static bool IsTrue(IDictionary<string, string> cfg, string key) =>
            cfg.TryGetValue(key, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

        private static string ExpandRetroArchPath(string dir, EmulatorMapping mapping)
        {
            if (string.IsNullOrEmpty(dir)) return dir;
            // RetroArch uses ":" / ":\" to mean its own install dir.
            if (dir.StartsWith(":"))
            {
                var rel = dir.TrimStart(':').TrimStart('\\', '/');
                return Path.Combine(mapping.EmulatorBasePathResolved ?? "", rel);
            }
            return dir;
        }

        // ---- RetroArch sort-by-core subfolder ------------------------------

        /// <summary>
        /// The subfolder under <paramref name="baseDir"/> that RetroArch's sort_*_enable nesting uses
        /// for this game (e.g. "mGBA"): prefer an existing subdir already holding "&lt;base&gt;.*", then
        /// the core name derived from the mapping's libretro core info. Null when undeterminable.
        /// </summary>
        private string ResolveCoreSortFolder(string baseDir, string romBaseName, EmulatorMapping mapping)
        {
            return DetectCoreSubfolder(baseDir, romBaseName, TryResolveCoreName(mapping));
        }

        /// <summary>
        /// Pure core-subfolder picker (filesystem only, no Playnite types) so it is unit-testable.
        /// Prefers an existing subdir of <paramref name="baseDir"/> containing "&lt;romBaseName&gt;.*";
        /// when several match (e.g. a stale by-content folder beside the live core folder) prefers
        /// <paramref name="preferredCore"/>, else the most-recently-written; when none exist (game
        /// never launched here) falls back to <paramref name="preferredCore"/>. Null if all of that fails.
        /// </summary>
        internal static string DetectCoreSubfolder(string baseDir, string romBaseName, string preferredCore)
        {
            if (!string.IsNullOrEmpty(baseDir) && !string.IsNullOrEmpty(romBaseName) && Directory.Exists(baseDir))
            {
                var matches = new List<KeyValuePair<string, DateTime>>();
                foreach (var sub in Directory.EnumerateDirectories(baseDir))
                {
                    var newest = DateTime.MinValue;
                    var has = false;
                    foreach (var f in Directory.EnumerateFiles(sub, romBaseName + ".*", SearchOption.TopDirectoryOnly))
                    {
                        has = true;
                        var m = File.GetLastWriteTimeUtc(f);
                        if (m > newest) newest = m;
                    }
                    if (has) matches.Add(new KeyValuePair<string, DateTime>(new DirectoryInfo(sub).Name, newest));
                }

                if (matches.Count == 1)
                {
                    return matches[0].Key;
                }
                if (matches.Count > 1)
                {
                    if (!string.IsNullOrEmpty(preferredCore))
                    {
                        var hit = matches.FirstOrDefault(x => string.Equals(x.Key, preferredCore, StringComparison.OrdinalIgnoreCase));
                        if (hit.Key != null) return hit.Key;
                    }
                    return matches.OrderByDescending(x => x.Value).First().Key;
                }
            }

            return string.IsNullOrEmpty(preferredCore) ? null : preferredCore;
        }

        /// <summary>RetroArch core display name (e.g. "mGBA") for the mapping's profile, or null.</summary>
        private string TryResolveCoreName(EmulatorMapping mapping)
        {
            var coreFile = TryResolveRetroArchCoreFile(mapping);
            return string.IsNullOrEmpty(coreFile) ? null : TryReadCoreName(mapping?.EmulatorBasePathResolved, coreFile);
        }

        /// <summary>The libretro core base file name (e.g. "mgba_libretro") referenced by the profile's args.</summary>
        private string TryResolveRetroArchCoreFile(EmulatorMapping mapping)
        {
            string args = null;
            var profile = mapping?.EmulatorProfile;
            var custom = profile as CustomEmulatorProfile;
            var builtIn = profile as BuiltInEmulatorProfile;
            if (custom != null)
            {
                args = custom.Arguments;
            }
            else if (builtIn != null)
            {
                try
                {
                    // The bundled definition usually carries "-L ...<core>_libretro.dll"; fall back to
                    // the user's overridden built-in args if it doesn't.
                    args = SettingsViewModel.Instance.PlayniteAPI.Emulation.Emulators
                        .FirstOrDefault(e => e.Id == mapping.Emulator?.BuiltInConfigId)?
                        .Profiles
                        .FirstOrDefault(p => p.Name == builtIn.Name)?
                        .StartupArguments;
                    if (ExtractLibretroCoreToken(args) == null)
                    {
                        args = builtIn.CustomArguments;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not resolve the built-in RetroArch core from the profile.");
                }
            }
            return ExtractLibretroCoreToken(args);
        }

        private static readonly Regex LibretroCoreToken = new Regex(@"([A-Za-z0-9_]+_libretro)", RegexOptions.IgnoreCase);

        /// <summary>Pull the "&lt;core&gt;_libretro" token out of an emulator argument string, or null.</summary>
        internal static string ExtractLibretroCoreToken(string args)
        {
            if (string.IsNullOrEmpty(args)) return null;
            var m = LibretroCoreToken.Match(args);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        /// <summary>Read "corename" from "&lt;install&gt;\info\&lt;coreFile&gt;.info" (RetroArch key=value), or null.</summary>
        private string TryReadCoreName(string basePath, string coreFile)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(coreFile)) return null;
            var infoPath = Path.Combine(basePath, "info", coreFile + ".info");
            if (!File.Exists(infoPath)) return null;
            try
            {
                var info = ParseRetroArchConfig(File.ReadAllLines(infoPath));
                return info.TryGetValue("corename", out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Could not read corename from '{infoPath}'.");
                return null;
            }
        }

        private string FindEmulatorSaveFolder(EmulatorMapping mapping)
        {
            var basePath = mapping.EmulatorBasePathResolved;
            if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            {
                return null;
            }
            foreach (var name in new[] { "saves", "Save", "Saves", "SaveData", "battery" })
            {
                var candidate = Path.Combine(basePath, name);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
