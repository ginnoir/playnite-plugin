using Playnite.SDK.Models;
using RomM.Models.RomM.Sync;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace RomM.SaveSync
{
    internal class SaveStatusViewModel : INotifyPropertyChanged
    {
        private readonly IRomM _romM;
        private CancellationTokenSource _cts;
        private bool _suppressSelectionLoad;

        public ObservableCollection<SaveStatusItem> SaveItems { get; } = new ObservableCollection<SaveStatusItem>();
        public ObservableCollection<SaveStatusItem> StateItems { get; } = new ObservableCollection<SaveStatusItem>();
        public ObservableCollection<Game> AvailableGames { get; } = new ObservableCollection<Game>();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; Notify(nameof(IsLoading)); Notify(nameof(ShowContent)); }
        }

        private string _errorText;
        public string ErrorText
        {
            get => _errorText;
            private set { _errorText = value; Notify(nameof(ErrorText)); Notify(nameof(HasError)); Notify(nameof(ShowContent)); }
        }

        private string _statusNote;
        public string StatusNote
        {
            get => _statusNote;
            private set { _statusNote = value; Notify(nameof(StatusNote)); Notify(nameof(HasStatusNote)); Notify(nameof(ShowContent)); }
        }

        private bool _isRomMGame;
        public bool IsRomMGame
        {
            get => _isRomMGame;
            private set { _isRomMGame = value; Notify(nameof(IsRomMGame)); }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorText);
        public bool HasStatusNote => !string.IsNullOrEmpty(StatusNote);
        public bool ShowContent => !IsLoading && !HasError && !HasStatusNote && IsRomMGame;
        public bool ShowSavesSection => _romM.Settings.EnableSaveSync && SaveItems.Count > 0;
        public bool ShowStatesSection => _romM.Settings.EnableStateSync && StateItems.Count > 0;

        public ICommand RefreshCommand { get; }

        private Game _currentGame;
        public Game SelectedGame
        {
            get => _currentGame;
            set
            {
                if (ReferenceEquals(value, _currentGame))
                    return;
                if (_suppressSelectionLoad)
                {
                    _currentGame = value;
                    Notify(nameof(SelectedGame));
                    return;
                }
                Load(value);
            }
        }

        public SaveStatusViewModel(IRomM romM)
        {
            _romM = romM;
            RefreshCommand = new RelayCommand(() =>
            {
                RefreshAvailableGames();
                Load(_currentGame);
            });
            RefreshAvailableGames();
        }

        /// <summary>Rebuild the RomM-game picker list (call when the sidebar opens).</summary>
        public void RefreshAvailableGames()
        {
            var games = _romM.Playnite.Database.Games
                .Where(g => g.PluginId == _romM.Id)
                .OrderBy(g => g.Name)
                .ToList();

            var selectedId = _currentGame?.Id;
            AvailableGames.Clear();
            foreach (var g in games)
                AvailableGames.Add(g);

            if (selectedId.HasValue)
            {
                var match = AvailableGames.FirstOrDefault(g => g.Id == selectedId.Value);
                if (match != null && !ReferenceEquals(match, _currentGame))
                {
                    _suppressSelectionLoad = true;
                    try
                    {
                        _currentGame = match;
                        Notify(nameof(SelectedGame));
                    }
                    finally
                    {
                        _suppressSelectionLoad = false;
                    }
                }
            }
        }

        public void Load(Game game)
        {
            _currentGame = game;
            Notify(nameof(SelectedGame));

            // Keep ComboBox selection on the same instance that lives in AvailableGames.
            if (game != null)
            {
                var inList = AvailableGames.FirstOrDefault(g => g.Id == game.Id);
                if (inList == null)
                {
                    RefreshAvailableGames();
                    inList = AvailableGames.FirstOrDefault(g => g.Id == game.Id);
                }
                if (inList != null && !ReferenceEquals(inList, _currentGame))
                {
                    _suppressSelectionLoad = true;
                    try
                    {
                        _currentGame = inList;
                        Notify(nameof(SelectedGame));
                    }
                    finally
                    {
                        _suppressSelectionLoad = false;
                    }
                    game = inList;
                }
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            IsLoading = true;
            ErrorText = null;
            StatusNote = null;
            SaveItems.Clear();
            StateItems.Clear();
            Notify(nameof(ShowSavesSection));
            Notify(nameof(ShowStatesSection));

            if (game == null)
            {
                IsRomMGame = false;
                IsLoading = false;
                StatusNote = "Select a RomM game to see save sync status.";
                return;
            }

            if (game.PluginId != _romM.Id)
            {
                IsRomMGame = false;
                IsLoading = false;
                StatusNote = "Not a RomM game — pick one from the list, or select a RomM title in the library.";
                return;
            }

            IsRomMGame = true;

            if (!_romM.Settings.EnableSaveSync && !_romM.Settings.EnableStateSync)
            {
                IsLoading = false;
                StatusNote = "Save sync is disabled — enable it in Settings → RomM → Save sync.";
                return;
            }

            if (!SaveSyncGameResolve.TryGetRomId(game, out var romId))
            {
                IsLoading = false;
                StatusNote = "No RomM ID on this game. Run a library update.";
                return;
            }

            if (!SaveSyncGameResolve.TryGetMapping(game, _romM, out var mapping, out var mapErr))
            {
                IsLoading = false;
                StatusNote = mapErr;
                return;
            }

            if (!mapping.SyncSaves)
            {
                IsLoading = false;
                StatusNote = "Save sync is disabled for this emulator mapping (enable 'Sync saves' on the mapping).";
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => LoadBackground(game, romId, mapping, ct));
        }

        private void LoadBackground(Game game, int romId, EmulatorMapping mapping, CancellationToken ct)
        {
            try
            {
                var locator = new SaveLocator(_romM.Logger);
                var paths = locator.Resolve(mapping, game);
                var profile = PlatformSaveProfiles.Get(mapping.RomMPlatform?.Slug, mapping.RomMPlatform?.FsSlug);
                var client = new SaveSyncClient(_romM.Settings.RomMHost, _romM.Logger);
                var hasAuth = !string.IsNullOrEmpty(_romM.Settings.RomMHost) && _romM.Settings.HasAnyAuth;

                var saveItems = BuildSaveItems(locator, paths, profile, client, romId, hasAuth, ct);
                var stateItems = BuildStateItems(locator, paths, client, romId, hasAuth, ct);

                if (ct.IsCancellationRequested) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    SaveItems.Clear();
                    foreach (var item in saveItems) SaveItems.Add(item);
                    StateItems.Clear();
                    foreach (var item in stateItems) StateItems.Add(item);
                    Notify(nameof(ShowSavesSection));
                    Notify(nameof(ShowStatesSection));
                    IsLoading = false;

                    if (saveItems.Count == 0 && stateItems.Count == 0)
                    {
                        StatusNote = paths.Resolved
                            ? "No local or server saves found for this game yet."
                            : "Could not resolve the local save folder for this emulator mapping.";
                    }
                });
            }
            catch (Exception ex)
            {
                _romM.Logger.Warn(ex, "SaveStatusViewModel: load failed");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        IsLoading = false;
                        ErrorText = "Failed to load save status.";
                    }
                });
            }
        }

        private List<SaveStatusItem> BuildSaveItems(
            SaveLocator locator, ResolvedSavePaths paths, PlatformSaveProfile profile,
            SaveSyncClient client, int romId, bool hasAuth, CancellationToken ct)
        {
            var items = new List<SaveStatusItem>();
            if (!_romM.Settings.EnableSaveSync) return items;

            DateTime? localNewest = null;
            if (paths.Resolved && !string.IsNullOrEmpty(paths.SaveDir))
            {
                foreach (var path in locator.EnumerateByExtensions(paths.SaveDir, paths.RomBaseName, profile.SaveExtensions))
                {
                    try
                    {
                        var t = File.GetLastWriteTimeUtc(path);
                        if (!localNewest.HasValue || t > localNewest) localNewest = t;
                    }
                    catch { }
                }
            }

            List<RomMSave> serverSaves = new List<RomMSave>();
            if (hasAuth && !ct.IsCancellationRequested)
            {
                var r = client.GetSaves(romId);
                if (r.Ok) serverSaves = r.Value;
            }

            var bySlot = serverSaves
                .Where(s => !string.IsNullOrEmpty(s.Slot))
                .GroupBy(s => s.Slot, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

            var liveHandled = false;
            foreach (var kvp in bySlot.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var slot = kvp.Key;
                var srv = kvp.Value;
                if (slot.Equals(SyncSlots.Live, StringComparison.OrdinalIgnoreCase))
                {
                    liveHandled = true;
                    items.Add(CompareSaveRow(slot, localNewest, srv.UpdatedAt));
                }
                else
                {
                    items.Add(new SaveStatusItem
                    {
                        Label = slot,
                        LocalTime = "—",
                        ServerTime = FormatTime(srv.UpdatedAt),
                        Status = SyncStatus.ServerOnly,
                        StatusText = "↓ Server only",
                    });
                }
            }

            if (!liveHandled)
            {
                items.Add(new SaveStatusItem
                {
                    Label = SyncSlots.Live,
                    LocalTime = localNewest.HasValue ? FormatTime(localNewest.Value) : "—",
                    ServerTime = "—",
                    Status = localNewest.HasValue ? SyncStatus.LocalOnly : SyncStatus.Unknown,
                    StatusText = localNewest.HasValue ? "↑ Local only" : "No save found",
                });
            }

            return items;
        }

        private List<SaveStatusItem> BuildStateItems(
            SaveLocator locator, ResolvedSavePaths paths,
            SaveSyncClient client, int romId, bool hasAuth, CancellationToken ct)
        {
            var items = new List<SaveStatusItem>();
            if (!_romM.Settings.EnableStateSync) return items;

            var localPaths = (paths.Resolved && !string.IsNullOrEmpty(paths.StateDir))
                ? locator.EnumerateRetroArchStates(paths.StateDir, paths.RomBaseName)
                : (IList<string>)new List<string>();

            List<RomMState> serverStates = new List<RomMState>();
            if (hasAuth && !ct.IsCancellationRequested)
            {
                var r = client.GetStates(romId);
                if (r.Ok) serverStates = r.Value;
            }

            var serverByName = serverStates.ToDictionary(s => s.FileName, s => s, StringComparer.OrdinalIgnoreCase);
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in localPaths)
            {
                if (ct.IsCancellationRequested) break;
                var name = Path.GetFileName(path);
                handled.Add(name);

                DateTime? localTime = null;
                long localSize = 0;
                try { var info = new FileInfo(path); localTime = info.LastWriteTimeUtc; localSize = info.Length; }
                catch { }

                if (serverByName.TryGetValue(name, out var srv))
                {
                    SyncStatus status;
                    string statusText;
                    if (localSize > 0 && localSize == srv.FileSizeBytes)
                    {
                        status = SyncStatus.Synced;
                        statusText = "✓ Synced";
                    }
                    else if (localTime.HasValue && localTime.Value > srv.UpdatedAt)
                    {
                        status = SyncStatus.LocalAhead;
                        statusText = "↑ Local ahead";
                    }
                    else
                    {
                        status = SyncStatus.ServerAhead;
                        statusText = "↓ Server ahead";
                    }
                    items.Add(new SaveStatusItem
                    {
                        Label = name,
                        LocalTime = localTime.HasValue ? FormatTime(localTime.Value) : "—",
                        ServerTime = FormatTime(srv.UpdatedAt),
                        Status = status,
                        StatusText = statusText,
                        IsState = true,
                    });
                }
                else
                {
                    items.Add(new SaveStatusItem
                    {
                        Label = name,
                        LocalTime = localTime.HasValue ? FormatTime(localTime.Value) : "—",
                        ServerTime = "—",
                        Status = SyncStatus.LocalOnly,
                        StatusText = "↑ Local only",
                        IsState = true,
                    });
                }
            }

            foreach (var state in serverStates)
            {
                if (!handled.Contains(state.FileName))
                {
                    items.Add(new SaveStatusItem
                    {
                        Label = state.FileName,
                        LocalTime = "—",
                        ServerTime = FormatTime(state.UpdatedAt),
                        Status = SyncStatus.ServerOnly,
                        StatusText = "↓ Server only",
                        IsState = true,
                    });
                }
            }

            return items;
        }

        private static SaveStatusItem CompareSaveRow(string slot, DateTime? localTime, DateTime serverTime)
        {
            if (!localTime.HasValue)
            {
                return new SaveStatusItem
                {
                    Label = slot,
                    LocalTime = "—",
                    ServerTime = FormatTime(serverTime),
                    Status = SyncStatus.ServerOnly,
                    StatusText = "↓ Server only",
                };
            }

            var diffSec = (localTime.Value - serverTime).TotalSeconds;
            SyncStatus status;
            string statusText;

            if (Math.Abs(diffSec) < 5)
            {
                status = SyncStatus.Synced;
                statusText = "✓ Synced";
            }
            else if (diffSec > 0)
            {
                status = SyncStatus.LocalAhead;
                statusText = "↑ Local ahead";
            }
            else
            {
                status = SyncStatus.ServerAhead;
                statusText = "↓ Server ahead";
            }

            return new SaveStatusItem
            {
                Label = slot,
                LocalTime = FormatTime(localTime.Value),
                ServerTime = FormatTime(serverTime),
                Status = status,
                StatusText = statusText,
            };
        }

        private static string FormatTime(DateTime utc) =>
            utc == default ? "—" : utc.ToLocalTime().ToString("MMM d, HH:mm");

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) { _execute = execute; }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute();
    }
}
