using Newtonsoft.Json;
using Playnite.SDK.Models;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;

namespace RomM.Settings
{
    /// <summary>How the plugin locates an emulator's per-game save/state files. See SaveSync/SaveLocator.cs.</summary>
    public enum SaveLocatorStrategy
    {
        Auto,               // infer from the emulator (RetroArch cfg, else next-to-ROM)
        RetroArch,          // parse retroarch.cfg savefile/savestate directories
        NextToRom,          // saves sit beside the ROM, matched by extension
        EmulatorSaveFolder, // a fixed save folder under the emulator install dir
        Custom              // use SaveDirOverride verbatim
    }

    public class EmulatorMapping : ObservableObject
    {
        public EmulatorMapping()
        {
            MappingId = Guid.NewGuid();
        }

        public Guid MappingId { get; set; }

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled { get; set; }

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool AutoExtract { get; set; }

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool UseM3u { get; set; }

        // ---- Save sync (Task 3) ----

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool SyncSaves { get; set; }

        [DefaultValue(SaveLocatorStrategy.Auto)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public SaveLocatorStrategy SaveStrategy { get; set; }

        /// <summary>Explicit save directory (supports the {PlayniteDir} portable token). Required for <see cref="SaveLocatorStrategy.Custom"/>.</summary>
        public string SaveDirOverride { get; set; }

        [JsonIgnore]
        public Emulator Emulator
        {
            get => AvailableEmulators.FirstOrDefault(e => e.Id == EmulatorId);
            set { EmulatorId = value.Id; }
        }
        public Guid EmulatorId { get; set; }

        [JsonIgnore]
        public EmulatorProfile EmulatorProfile
        {
            get => Emulator?.SelectableProfiles.FirstOrDefault(p => p.Id == EmulatorProfileId);
            set { EmulatorProfileId = value.Id; }
        }

        public string EmulatorProfileId { get; set; }

        [JsonIgnore]
        public EmulatedPlatform Platform
        {
            get => AvailablePlatforms.FirstOrDefault(p => p.Id == PlatformId);
            set { PlatformId = value.Id; }
        }
        public string PlatformId { get; set; }
        public string DestinationPath { get; set; }

        public static IEnumerable<Emulator> AvailableEmulators => SettingsViewModel.Instance.PlayniteAPI.Database.Emulators?.OrderBy(x => x.Name) ?? Enumerable.Empty<Emulator>();

        [JsonIgnore]
        public IEnumerable<EmulatorProfile> AvailableProfiles => Emulator?.SelectableProfiles;

        [JsonIgnore]
        public IEnumerable<EmulatedPlatform> AvailablePlatforms
        {
            get
            {
                var playnite = SettingsViewModel.Instance.PlayniteAPI;
                HashSet<string> validPlatforms;

                if (EmulatorProfile is CustomEmulatorProfile)
                {
                    var customProfile = EmulatorProfile as CustomEmulatorProfile;
                    validPlatforms = new HashSet<string>(playnite.Database.Platforms.Where(p => customProfile.Platforms.Contains(p.Id)).Select(p => p.SpecificationId));
                }
                else if (EmulatorProfile is BuiltInEmulatorProfile)
                {
                    var builtInProfile = (EmulatorProfile as BuiltInEmulatorProfile);
                    validPlatforms = new HashSet<string>(
                        playnite.Emulation.Emulators
                        .FirstOrDefault(e => e.Id == Emulator.BuiltInConfigId)?
                        .Profiles
                        .FirstOrDefault(p => p.Name == builtInProfile.Name)?
                        .Platforms
                        );
                }
                else
                {
                    validPlatforms = new HashSet<string>();
                }

                return playnite.Emulation.Platforms?.Where(p => validPlatforms.Contains(p.Id)) ?? Enumerable.Empty<EmulatedPlatform>();
            }
        }

        [JsonIgnore]
        [XmlIgnore]
        public string DestinationPathResolved
        {
            get
            {
                var playnite = SettingsViewModel.Instance.PlayniteAPI;
                return playnite.Paths.IsPortable ? DestinationPath?.Replace(ExpandableVariables.PlayniteDirectory, playnite.Paths.ApplicationPath) : DestinationPath;
            }
        }

        /// <summary><see cref="SaveDirOverride"/> with the {PlayniteDir} portable token expanded, mirroring <see cref="DestinationPathResolved"/>.</summary>
        [JsonIgnore]
        [XmlIgnore]
        public string SaveDirOverrideResolved
        {
            get
            {
                var playnite = SettingsViewModel.Instance.PlayniteAPI;
                return playnite.Paths.IsPortable
                    ? SaveDirOverride?.Replace(ExpandableVariables.PlayniteDirectory, playnite.Paths.ApplicationPath)
                    : SaveDirOverride;
            }
        }

        [JsonIgnore]
        [XmlIgnore]
        public string EmulatorBasePath => Emulator?.InstallDir;

        [JsonIgnore]
        [XmlIgnore]
        public string EmulatorBasePathResolved
        {
            get
            {
                var playnite = SettingsViewModel.Instance.PlayniteAPI;
                var ret = Emulator?.InstallDir;
                if (playnite.Paths.IsPortable)
                {
                    ret = ret?.Replace(ExpandableVariables.PlayniteDirectory, playnite.Paths.ApplicationPath);
                }
                return ret;
            }
        }


        public IEnumerable<string> GetDescriptionLines()
        {
            yield return $"{nameof(EmulatorId)}: {EmulatorId}";
            yield return $"{nameof(Emulator)}*: {Emulator?.Name ?? "<Unknown>"}";
            yield return $"{nameof(EmulatorProfileId)}: {EmulatorProfileId ?? "<Unknown>"}";
            yield return $"{nameof(EmulatorProfile)}*: {EmulatorProfile?.Name ?? "<Unknown>"}";
            yield return $"{nameof(PlatformId)}: {PlatformId ?? "<Unknown>"}";
            yield return $"{nameof(Platform)}*: {Platform?.Name ?? "<Unknown>"}";
            yield return $"{nameof(DestinationPath)}: {DestinationPath ?? "<Unknown>"}";
            yield return $"{nameof(DestinationPathResolved)}*: {DestinationPathResolved ?? "<Unknown>"}";
            yield return $"{nameof(EmulatorBasePathResolved)}*: {EmulatorBasePathResolved ?? "<Unknown>"}";
        }
    }
}
