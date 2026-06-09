using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RomM.Settings
{
    /// <summary>How to resolve a save the RomM server reports as a conflict (both sides changed).</summary>
    public enum ConflictPolicy
    {
        Ask,          // prompt the user (default)
        PreferLocal,  // keep local, upload with overwrite
        PreferRemote, // keep server, download over local
        KeepBoth      // archive local server-side (null-slot row), pull remote as the live save
    }

    public class SettingsViewModel : ObservableObject, ISettings
    {
        private readonly Plugin _plugin;

        private SettingsViewModel editingClone { get; set; }

        [JsonIgnore]
        internal readonly IPlayniteAPI PlayniteAPI;

        [JsonIgnore]
        internal readonly IRomM RomM;

        public static SettingsViewModel Instance { get; private set; }

        // RomM client API tokens are "rmm_" + 64 lowercase hex chars (secrets.token_hex(32) on the server).
        private static readonly Regex ApiTokenPattern = new Regex(@"^rmm_[0-9a-f]{64}$", RegexOptions.Compiled);

        public static bool IsValidApiToken(string token)
        {
            return !string.IsNullOrEmpty(token) && ApiTokenPattern.IsMatch(token);
        }

        [JsonIgnore]
        public bool HasAnyAuth =>
            IsValidApiToken(RomMApiToken?.Trim()) ||
            (!string.IsNullOrEmpty(RomMUsername) && !string.IsNullOrEmpty(RomMPassword));

        public bool ScanGamesInFullScreen { get; set; } = false;
        public bool NotifyOnInstallComplete { get; set; } = false;
        public bool KeepRomMSynced { get; set; } = false;
        public string RomMHost { get; set; } = "";
        public string RomMUsername { get; set; } = "";
        public string RomMPassword { get; set; } = "";
        private string _romMApiToken = "";
        public string RomMApiToken
        {
            get => _romMApiToken;
            set
            {
                if (_romMApiToken == value) return;
                _romMApiToken = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasValidApiToken));
            }
        }

        [JsonIgnore]
        public bool HasValidApiToken => IsValidApiToken(RomMApiToken?.Trim());
        public ObservableCollection<EmulatorMapping> Mappings { get; set; }

        public bool Use7z { get; set; } = false;
        public string PathTo7z { get; set; } = "";
        public bool MergeRevisions { get; set; } = false;

        // ---- Save sync (Task 3) ----
        public bool EnableSaveSync { get; set; } = false;
        public bool EnableStateSync { get; set; } = false;
        public bool SyncScreenshots { get; set; } = true;
        public bool SyncOnGameStart { get; set; } = true;
        public bool SyncOnGameStop { get; set; } = true;
        public bool ReconcileOnStartup { get; set; } = false;
        public ConflictPolicy SaveConflictPolicy { get; set; } = ConflictPolicy.Ask;
        public bool AutoCleanupSlots { get; set; } = true;
        public int AutoCleanupLimit { get; set; } = 10;

        // Stable RomM device identity (registered once, persisted in config.json). See SaveSync/DeviceIdentity.cs.
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";

        public SettingsViewModel()
        {
        }

        internal SettingsViewModel(Plugin plugin, IRomM romM)
        {
            RomM = romM;
            PlayniteAPI = plugin.PlayniteApi;
            Instance = this;
            _plugin = plugin;

            bool forceSave = false;
            var savedSettings = plugin.LoadPluginSettings<SettingsViewModel>();

            if (savedSettings == null) {
                forceSave = true;
            } else {
                ScanGamesInFullScreen = savedSettings.ScanGamesInFullScreen;
                NotifyOnInstallComplete = savedSettings.NotifyOnInstallComplete;
                RomMHost = savedSettings.RomMHost;
                RomMUsername = savedSettings.RomMUsername;
                RomMPassword = savedSettings.RomMPassword;
                RomMApiToken = savedSettings.RomMApiToken ?? "";
                Mappings = savedSettings.Mappings;
                KeepRomMSynced = savedSettings.KeepRomMSynced;
                Use7z = savedSettings.Use7z;
                PathTo7z = savedSettings.PathTo7z;
                MergeRevisions = savedSettings.MergeRevisions;
                EnableSaveSync = savedSettings.EnableSaveSync;
                EnableStateSync = savedSettings.EnableStateSync;
                SyncScreenshots = savedSettings.SyncScreenshots;
                SyncOnGameStart = savedSettings.SyncOnGameStart;
                SyncOnGameStop = savedSettings.SyncOnGameStop;
                ReconcileOnStartup = savedSettings.ReconcileOnStartup;
                SaveConflictPolicy = savedSettings.SaveConflictPolicy;
                AutoCleanupSlots = savedSettings.AutoCleanupSlots;
                AutoCleanupLimit = savedSettings.AutoCleanupLimit;
                DeviceId = savedSettings.DeviceId ?? "";
                DeviceName = savedSettings.DeviceName ?? "";
            }
            
            if (Mappings == null)
            {
                Mappings = new ObservableCollection<EmulatorMapping>();
            }

            var mappingsWithoutId = Mappings.Where(m => m.MappingId == default);
            if (mappingsWithoutId.Any())
            {
                mappingsWithoutId.ForEach(m => m.MappingId = Guid.NewGuid());
                forceSave = true;
            }

            if (forceSave)
            {
                SavePluginSettings(this);
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = JsonConvert.DeserializeObject<SettingsViewModel>(JsonConvert.SerializeObject(Instance));
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            SavePluginSettings(editingClone);
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            SavePluginSettings(this);
            HttpClientSingleton.ConfigureAuth(this);
        }

        /// <summary>Persist current settings to config.json outside the edit flow (e.g. after registering a device).</summary>
        internal void Save()
        {
            SavePluginSettings(this);
        }

        private void SavePluginSettings<SettingsViewModel>(SettingsViewModel settings)
        {
            var setDir = _plugin.GetPluginUserDataPath();
            var setFile = Path.Combine(setDir, "config.json");
            if (!Directory.Exists(setDir))
            {
                Directory.CreateDirectory(setDir);
            }

            var strConf = JsonConvert.SerializeObject(settings);
            File.WriteAllText(setFile, strConf);
        }

        public bool VerifySettings(out List<string> errors)
        {
            var mappingErrors = new List<string>();

            if (!string.IsNullOrWhiteSpace(RomMApiToken) && !IsValidApiToken(RomMApiToken.Trim()))
            {
                mappingErrors.Add("API Token must start with 'rmm_' followed by 64 lowercase hex characters.");
            }

            Mappings.Where(m => m.Enabled)?.ForEach(m =>
            {
                if (string.IsNullOrEmpty(m.DestinationPathResolved))
                {
                    mappingErrors.Add($"{m.MappingId}: No destination path specified.");
                }
                else if (!Directory.Exists(m.DestinationPathResolved))
                {
                    mappingErrors.Add($"{m.MappingId}: Destination path doesn't exist ({m.DestinationPathResolved}).");
                }
            });

            errors = mappingErrors;
            return errors.Count == 0;
        }
    }
}
