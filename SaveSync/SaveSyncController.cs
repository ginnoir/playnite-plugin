using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Games;
using RomM.Models.RomM.Sync;
using RomM.SaveSync.Converters;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RomM.SaveSync
{
    internal sealed class SyncResult
    {
        public bool Ran { get; set; }
        public int Uploaded { get; set; }
        public int Downloaded { get; set; }
        public int Conflicts { get; set; }
        public int NoOps { get; set; }
        public int Failed { get; set; }
        public string Message { get; set; }

        public int Completed => Uploaded + Downloaded + Conflicts + NoOps;
    }

    /// <summary>
    /// The save-sync engine. For one game it reports local state to RomM's /api/sync/negotiate, then
    /// executes the server's plan (upload/download/conflict/no_op) with format conversion, backups and
    /// atomic writes, and closes the session (optionally logging playtime). RomM owns the conflict
    /// math (CONTRACT.md §3); this class owns the local file work.
    /// </summary>
    internal sealed class SaveSyncController
    {
        private readonly IRomM _romM;
        private ILogger Logger => _romM.Logger;

        public SaveSyncController(IRomM romM)
        {
            _romM = romM;
        }

        private SettingsViewModel Settings => _romM.Settings;

        private string SaveSyncRoot => Path.Combine(_romM.GetPluginUserDataPath(), "SaveSync");

        public SyncResult SyncGame(Game game, IConflictResolver resolver, CancellationToken ct, DateTime? sessionStartUtc = null)
        {
            var result = new SyncResult();
            if (!Settings.EnableSaveSync)
            {
                return result;
            }

            if (!TryGetRomId(game, out var romId))
            {
                return result;
            }

            var info = game.GetRomMGameInfo();
            var mapping = info?.Mapping;
            if (mapping == null || !mapping.SyncSaves)
            {
                return result;
            }

            if (string.IsNullOrEmpty(Settings.RomMHost) || !Settings.HasAnyAuth)
            {
                Logger.Warn("Save sync skipped: RomM host/auth not configured.");
                return result;
            }

            var client = new SaveSyncClient(Settings.RomMHost, Logger);
            var deviceId = DeviceIdentity.EnsureRegistered(client, Settings, Logger);
            if (string.IsNullOrEmpty(deviceId))
            {
                result.Message = "Device registration failed (check token scopes).";
                return result;
            }

            var profile = PlatformSaveProfiles.Get(mapping.Platform?.Id);
            var locator = new SaveLocator(Logger);
            var paths = locator.Resolve(mapping, game);
            if (!paths.Resolved)
            {
                Logger.Warn($"Could not resolve save location for '{game.Name}'.");
                return result;
            }

            var converter = ConverterRegistry.For(profile.ConverterFamily);
            var nativeFiles = LoadNativeSaveFiles(locator, paths, profile, out var newestUtc, out var primaryExt);
            var targetNativeExt = InferTargetNativeExt(mapping, profile, primaryExt);

            // Build the canonical client save (if any local save exists).
            ClientSaveState clientSave = null;
            byte[] canonicalBytes = null;
            string canonicalName = $"{paths.RomBaseName}.{profile.CanonicalSaveExtension}";

            if (nativeFiles.Count > 0)
            {
                var ctx = new ConversionContext
                {
                    BaseName = paths.RomBaseName,
                    CanonicalExtension = profile.CanonicalSaveExtension,
                    TargetNativeExtension = targetNativeExt,
                    Logger = Logger,
                };
                var canonical = converter.ToCanonical(nativeFiles, ctx);
                if (canonical == null)
                {
                    // No safe conversion: sync the primary native file as-is, tagged by emulator.
                    var primary = ConverterHelpers.PickPrimary(nativeFiles);
                    canonicalBytes = primary.Bytes;
                    canonicalName = primary.Name;
                    Logger.Warn($"Syncing '{game.Name}' save as-is (no safe converter result).");
                }
                else
                {
                    canonicalBytes = canonical.Bytes;
                    canonicalName = canonical.Name;
                }

                clientSave = new ClientSaveState
                {
                    RomId = romId,
                    FileName = canonicalName,
                    Slot = null,
                    Emulator = null,
                    ContentHash = SaveHashing.ComputeContentHash(canonicalBytes),
                    UpdatedAt = newestUtc,
                    FileSizeBytes = canonicalBytes.Length,
                };
            }

            var payload = new SyncNegotiatePayload { DeviceId = deviceId };
            if (clientSave != null)
            {
                payload.Saves.Add(clientSave);
            }

            var negotiation = client.Negotiate(payload);

            // The persisted device_id can go stale if the server DB was reset: re-register once and retry.
            if (negotiation.NotFound)
            {
                Logger.Warn("RomM device not found; re-registering.");
                deviceId = DeviceIdentity.ReRegister(client, Settings, Logger);
                if (string.IsNullOrEmpty(deviceId))
                {
                    result.Message = "Device re-registration failed.";
                    return result;
                }
                payload.DeviceId = deviceId;
                negotiation = client.Negotiate(payload);
            }

            if (!negotiation.Ok || negotiation.Value == null)
            {
                result.Message = $"Negotiate failed: {negotiation.Status}";
                Logger.Warn(result.Message);
                return result;
            }

            result.Ran = true;
            var sessionId = negotiation.Value.SessionId;

            foreach (var op in negotiation.Value.Operations)
            {
                if (ct.IsCancellationRequested) break;

                // We manage only the live (null-slot) battery save; slotted rows are history.
                if (!string.IsNullOrEmpty(op.Slot))
                {
                    continue;
                }

                try
                {
                    ExecuteOperation(client, op, romId, deviceId, sessionId, paths, profile, converter,
                        targetNativeExt, canonicalBytes, canonicalName, newestUtc, game, resolver, result);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    Logger.Error(ex, $"Sync op '{op.Action}' failed for '{game.Name}'.");
                }
            }

            var complete = new SyncCompletePayload
            {
                OperationsCompleted = result.Completed,
                OperationsFailed = result.Failed,
            };
            if (sessionStartUtc.HasValue)
            {
                var endUtc = DateTime.UtcNow;
                var startUtc = sessionStartUtc.Value;
                if (endUtc > startUtc)
                {
                    complete.PlaySessions = new List<SyncPlaySessionEntry>
                    {
                        new SyncPlaySessionEntry
                        {
                            RomId = romId,
                            SaveSlot = null,
                            StartTime = startUtc,
                            EndTime = endUtc,
                            DurationMs = (long)(endUtc - startUtc).TotalMilliseconds,
                        }
                    };
                }
            }
            client.CompleteSession(sessionId, complete);

            return result;
        }

        /// <summary>
        /// Sync savestates for one game. States are opaque and emulator+version specific, so they are
        /// NEVER converted — uploaded/downloaded verbatim, tagged by emulator, with the sidecar
        /// thumbnail (".png") attached when present. No server-side conflict engine exists for states,
        /// so we diff by (file name, size) and only upload genuinely new/changed local states and pull
        /// states we don't have locally.
        /// </summary>
        public void SyncStates(Game game, CancellationToken ct)
        {
            if (!Settings.EnableStateSync || !TryGetRomId(game, out var romId))
            {
                return;
            }

            var mapping = game.GetRomMGameInfo()?.Mapping;
            if (mapping == null || !mapping.SyncSaves)
            {
                return;
            }
            if (string.IsNullOrEmpty(Settings.RomMHost) || !Settings.HasAnyAuth)
            {
                return;
            }

            var client = new SaveSyncClient(Settings.RomMHost, Logger);
            var emulatorTag = SanitizeTag(mapping.Emulator?.Name);
            var locator = new SaveLocator(Logger);
            var paths = locator.Resolve(mapping, game);
            if (!paths.Resolved)
            {
                return;
            }

            var localStates = locator.EnumerateRetroArchStates(paths.StateDir, paths.RomBaseName);

            var serverResult = client.GetStates(romId);
            var serverStates = serverResult.Ok ? serverResult.Value : new List<RomMState>();
            var serverByName = serverStates
                .GroupBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Upload local states the server lacks or that differ in size.
            foreach (var path in localStates)
            {
                if (ct.IsCancellationRequested) return;
                var name = Path.GetFileName(path);
                var bytes = TryRead(path);
                if (bytes == null) continue;

                if (serverByName.TryGetValue(name, out var existing) && existing.FileSizeBytes == bytes.Length)
                {
                    continue; // already on the server, same size — treat as present
                }

                var screenshot = FindStateScreenshot(path, out var shotName);
                client.UploadState(romId, bytes, name, emulator: emulatorTag,
                    screenshot: Settings.SyncScreenshots ? screenshot : null,
                    screenshotName: Settings.SyncScreenshots ? shotName : null);
            }

            // Download server states we don't have locally.
            var localNames = new HashSet<string>(localStates.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            foreach (var state in serverStates)
            {
                if (ct.IsCancellationRequested) return;
                if (localNames.Contains(state.FileName) || string.IsNullOrEmpty(state.DownloadPath))
                {
                    continue;
                }
                var dl = client.DownloadByPath(state.DownloadPath);
                if (dl.Ok && dl.Value != null)
                {
                    var dest = Path.Combine(paths.StateDir, state.FileName);
                    if (!SafeFile.IsLocked(dest))
                    {
                        SafeFile.WriteAtomic(dest, dl.Value, Path.Combine(SaveSyncRoot, "backups", romId.ToString()));
                    }
                }
            }
        }

        private static byte[] TryRead(string path)
        {
            try { return SafeFile.ReadAllBytesShared(path); }
            catch { return null; }
        }

        private static string SanitizeTag(string emulatorName)
        {
            if (string.IsNullOrWhiteSpace(emulatorName)) return null;
            var cleaned = new string(emulatorName.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            return string.IsNullOrEmpty(cleaned) ? null : cleaned.ToLowerInvariant();
        }

        private static byte[] FindStateScreenshot(string statePath, out string screenshotName)
        {
            // RetroArch writes a "<state>.png" thumbnail next to the savestate.
            screenshotName = null;
            var candidate = statePath + ".png";
            if (!File.Exists(candidate))
            {
                return null;
            }
            try
            {
                screenshotName = Path.GetFileName(candidate);
                return SafeFile.ReadAllBytesShared(candidate);
            }
            catch
            {
                screenshotName = null;
                return null;
            }
        }

        /// <summary>Manual "Push local → RomM": upload the local save with overwrite, ignoring server state.</summary>
        public SyncResult ForcePush(Game game, CancellationToken ct)
        {
            return ForceDirection(game, push: true, ct);
        }

        /// <summary>Manual "Pull RomM → local": download the server's live (null-slot) save over local.</summary>
        public SyncResult ForcePull(Game game, CancellationToken ct)
        {
            return ForceDirection(game, push: false, ct);
        }

        private SyncResult ForceDirection(Game game, bool push, CancellationToken ct)
        {
            var result = new SyncResult();
            if (!TryGetRomId(game, out var romId)) return result;
            var mapping = game.GetRomMGameInfo()?.Mapping;
            if (mapping == null || string.IsNullOrEmpty(Settings.RomMHost) || !Settings.HasAnyAuth) return result;

            var client = new SaveSyncClient(Settings.RomMHost, Logger);
            var deviceId = DeviceIdentity.EnsureRegistered(client, Settings, Logger);
            if (string.IsNullOrEmpty(deviceId)) return result;

            var profile = PlatformSaveProfiles.Get(mapping.Platform?.Id);
            var locator = new SaveLocator(Logger);
            var paths = locator.Resolve(mapping, game);
            if (!paths.Resolved) return result;

            var converter = ConverterRegistry.For(profile.ConverterFamily);
            var nativeFiles = LoadNativeSaveFiles(locator, paths, profile, out _, out var primaryExt);
            var targetNativeExt = InferTargetNativeExt(mapping, profile, primaryExt);

            if (push)
            {
                if (nativeFiles.Count == 0) { result.Message = "No local save to push."; return result; }
                var ctx = new ConversionContext { BaseName = paths.RomBaseName, CanonicalExtension = profile.CanonicalSaveExtension, TargetNativeExtension = targetNativeExt, Logger = Logger };
                var canonical = converter.ToCanonical(nativeFiles, ctx);
                var bytes = canonical?.Bytes ?? ConverterHelpers.PickPrimary(nativeFiles).Bytes;
                var name = canonical?.Name ?? $"{paths.RomBaseName}.{profile.CanonicalSaveExtension}";
                var up = client.UploadSave(romId, bytes, name, emulator: null, slot: null, deviceId: deviceId, overwrite: true);
                if (up.Ok) { result.Uploaded++; result.Ran = true; } else result.Failed++;
            }
            else
            {
                var saves = client.GetSaves(romId, deviceId, slot: null);
                var live = saves.Ok ? saves.Value.Where(s => string.IsNullOrEmpty(s.Slot)).OrderByDescending(s => s.UpdatedAt).FirstOrDefault() : null;
                if (live == null) { result.Message = "No server save to pull."; return result; }
                DownloadAndWrite(client, live.Id, deviceId, null, paths, profile, converter, targetNativeExt, romId);
                result.Downloaded++; result.Ran = true;
            }
            return result;
        }

        private void ExecuteOperation(
            SaveSyncClient client, RomMSyncOperation op, int romId, string deviceId, int sessionId,
            ResolvedSavePaths paths, PlatformSaveProfile profile, ISaveConverter converter, string targetNativeExt,
            byte[] canonicalBytes, string canonicalName, DateTime newestUtc,
            Game game, IConflictResolver resolver, SyncResult result)
        {
            switch (op.Action)
            {
                case SyncActions.NoOp:
                    result.NoOps++;
                    break;

                case SyncActions.Upload:
                    if (canonicalBytes != null)
                    {
                        var up = client.UploadSave(romId, canonicalBytes, canonicalName, emulator: null, slot: null,
                            deviceId: deviceId, sessionId: sessionId, overwrite: false,
                            autocleanup: Settings.AutoCleanupSlots, autocleanupLimit: Settings.AutoCleanupLimit);
                        if (up.Ok) result.Uploaded++;
                        else if (up.Conflict) ResolveConflict(client, op, romId, deviceId, sessionId, paths, profile, converter, targetNativeExt, canonicalBytes, canonicalName, newestUtc, game, resolver, result);
                        else { result.Failed++; Logger.Warn($"Upload failed for '{game.Name}': {up.Status}"); }
                    }
                    break;

                case SyncActions.Download:
                    if (op.SaveId.HasValue)
                    {
                        DownloadAndWrite(client, op.SaveId.Value, deviceId, sessionId, paths, profile, converter, targetNativeExt, romId);
                        result.Downloaded++;
                    }
                    break;

                case SyncActions.Conflict:
                    ResolveConflict(client, op, romId, deviceId, sessionId, paths, profile, converter, targetNativeExt, canonicalBytes, canonicalName, newestUtc, game, resolver, result);
                    break;
            }
        }

        private void ResolveConflict(
            SaveSyncClient client, RomMSyncOperation op, int romId, string deviceId, int sessionId,
            ResolvedSavePaths paths, PlatformSaveProfile profile, ISaveConverter converter, string targetNativeExt,
            byte[] canonicalBytes, string canonicalName, DateTime newestUtc, Game game, IConflictResolver resolver, SyncResult result)
        {
            var info = new ConflictInfo
            {
                GameName = game.Name,
                RomBaseName = paths.RomBaseName,
                Operation = op,
                LocalSizeBytes = canonicalBytes?.Length ?? 0,
                LocalUpdatedUtc = newestUtc,
                RemoteUpdatedUtc = op.ServerUpdatedAt,
            };

            var choice = resolver?.Resolve(info) ?? ConflictChoice.KeepBoth;
            result.Conflicts++;

            switch (choice)
            {
                case ConflictChoice.KeepLocal:
                    if (canonicalBytes != null)
                    {
                        client.UploadSave(romId, canonicalBytes, canonicalName, emulator: null, slot: null,
                            deviceId: deviceId, sessionId: sessionId, overwrite: true);
                    }
                    break;

                case ConflictChoice.KeepRemote:
                    if (op.SaveId.HasValue)
                    {
                        DownloadAndWrite(client, op.SaveId.Value, deviceId, sessionId, paths, profile, converter, targetNativeExt, romId);
                    }
                    break;

                case ConflictChoice.KeepBoth:
                    // Push local into a tagged slot (server datetime-tags it), then pull remote as live.
                    if (canonicalBytes != null)
                    {
                        client.UploadSave(romId, canonicalBytes, canonicalName, emulator: null, slot: "conflict",
                            deviceId: deviceId, sessionId: sessionId, overwrite: false);
                    }
                    if (op.SaveId.HasValue)
                    {
                        DownloadAndWrite(client, op.SaveId.Value, deviceId, sessionId, paths, profile, converter, targetNativeExt, romId);
                    }
                    break;

                case ConflictChoice.Skip:
                default:
                    break;
            }
        }

        private void DownloadAndWrite(
            SaveSyncClient client, int saveId, string deviceId, int? sessionId,
            ResolvedSavePaths paths, PlatformSaveProfile profile, ISaveConverter converter, string targetNativeExt, int romId)
        {
            var dl = client.DownloadSaveContent(saveId, deviceId, sessionId, optimistic: true);
            if (!dl.Ok || dl.Value == null)
            {
                throw new Exception($"Download of save {saveId} failed: {dl.Status}");
            }

            var canonical = new SaveFile($"{paths.RomBaseName}.{profile.CanonicalSaveExtension}", dl.Value);
            var ctx = new ConversionContext
            {
                BaseName = paths.RomBaseName,
                CanonicalExtension = profile.CanonicalSaveExtension,
                TargetNativeExtension = targetNativeExt,
                Logger = Logger,
            };

            var outputs = converter.FromCanonical(canonical, ctx);
            if (outputs == null || outputs.Count == 0)
            {
                // No safe conversion: write the canonical as-is using the target extension.
                outputs = new List<SaveFile> { new SaveFile($"{paths.RomBaseName}.{targetNativeExt}", dl.Value) };
            }

            var backupRoot = Path.Combine(SaveSyncRoot, "backups", romId.ToString());
            foreach (var file in outputs)
            {
                var dest = Path.Combine(paths.SaveDir, file.Name);
                if (SafeFile.IsLocked(dest))
                {
                    Logger.Warn($"Save '{dest}' is locked (emulator running?); skipping write.");
                    continue;
                }
                SafeFile.WriteAtomic(dest, file.Bytes, backupRoot);
            }
        }

        private IList<SaveFile> LoadNativeSaveFiles(SaveLocator locator, ResolvedSavePaths paths, PlatformSaveProfile profile,
            out DateTime newestUtc, out string primaryExt)
        {
            newestUtc = DateTime.UtcNow;
            primaryExt = null;
            var files = new List<SaveFile>();

            var savePaths = locator.EnumerateByExtensions(paths.SaveDir, paths.RomBaseName, profile.SaveExtensions);
            DateTime newest = DateTime.MinValue;
            long largest = -1;

            foreach (var path in savePaths)
            {
                try
                {
                    var bytes = SafeFile.ReadAllBytesShared(path);
                    files.Add(new SaveFile(Path.GetFileName(path), bytes));
                    var mtime = File.GetLastWriteTimeUtc(path);
                    if (mtime > newest) newest = mtime;
                    if (bytes.Length > largest)
                    {
                        largest = bytes.Length;
                        primaryExt = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Could not read save file '{path}'.");
                }
            }

            if (newest > DateTime.MinValue)
            {
                newestUtc = newest;
            }
            return files;
        }

        private static string InferTargetNativeExt(EmulatorMapping mapping, PlatformSaveProfile profile, string existingPrimaryExt)
        {
            // If the emulator already has a save on disk, mirror whatever extension it used.
            if (!string.IsNullOrEmpty(existingPrimaryExt))
            {
                return existingPrimaryExt;
            }

            var strategy = mapping.SaveStrategy;
            if (strategy == SaveLocatorStrategy.RetroArch)
            {
                return profile.CanonicalSaveExtension; // RetroArch uses .srm
            }

            // Standalone defaults per family when nothing exists yet.
            switch (profile.ConverterFamily)
            {
                case "gba":
                case "nds": return "sav";
                case "psx": return "mcr";
                case "n64": return "eep"; // sentinel: any non-"srm" makes N64 converter split into components
                default: return profile.CanonicalSaveExtension;
            }
        }

        private static bool TryGetRomId(Game game, out int romId)
        {
            romId = -1;
            var version = game?.Version;
            if (string.IsNullOrEmpty(version) || !version.StartsWith("RomM:"))
            {
                return false;
            }
            return int.TryParse(version.Split(':')[1], out romId);
        }
    }
}
