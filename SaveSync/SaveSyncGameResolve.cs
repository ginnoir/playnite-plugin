using Newtonsoft.Json;
using Playnite.SDK.Models;
using RomM.Games;
using RomM.Models.RomM.Rom;
using RomM.Settings;
using System;
using System.IO;
using System.Linq;

namespace RomM.SaveSync
{
    /// <summary>
    /// Resolves rom_id + emulator mapping for both GameId formats:
    /// current <c>{romId}:{sha1}</c> (mapping in Games/{sha1}.json sidecar) and legacy
    /// protobuf <c>!0…</c> GameIds (mapping embedded in the id).
    /// </summary>
    internal static class SaveSyncGameResolve
    {
        public static bool TryGetRomId(Game game, out int romId)
        {
            romId = -1;
            if (game == null)
                return false;

            if (RomMGameId.TryParse(game.GameId, out romId, out _))
                return true;

            var version = game.Version;
            if (!string.IsNullOrEmpty(version) && version.StartsWith("RomM:") &&
                int.TryParse(version.Split(':')[1], out romId))
                return true;

            return false;
        }

        public static bool TryGetMapping(Game game, IRomM romM, out EmulatorMapping mapping, out string error)
        {
            mapping = null;
            error = null;
            if (game == null)
            {
                error = "No game selected.";
                return false;
            }

            // Current format: sidecar holds MappingID.
            if (RomMGameId.TryParse(game.GameId, out _, out var sha1))
            {
                var sidecar = Path.Combine(romM.ROMDataPath, sha1 + ".json");
                if (!File.Exists(sidecar))
                {
                    error = "Missing RomM sidecar for this game. Run a library update.";
                    return false;
                }

                try
                {
                    var local = JsonConvert.DeserializeObject<RomMRomLocal>(File.ReadAllText(sidecar));
                    if (local == null || local.MappingID == Guid.Empty)
                    {
                        error = "Sidecar has no emulator mapping. Re-import the game.";
                        return false;
                    }

                    mapping = romM.Settings.Mappings?.FirstOrDefault(m => m.MappingId == local.MappingID);
                    if (mapping == null)
                    {
                        error = "Emulator mapping from import no longer exists in Settings.";
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    romM.Logger.Warn(ex, $"SaveSync: failed reading sidecar {sidecar}");
                    error = "Could not read RomM game sidecar.";
                    return false;
                }
            }

            // Legacy protobuf GameId.
            if (!string.IsNullOrEmpty(game.GameId) && game.GameId.StartsWith("!0"))
            {
                try
                {
                    mapping = game.GetRomMGameInfo()?.Mapping;
                }
                catch (Exception ex)
                {
                    romM.Logger.Warn(ex, "SaveSync: legacy GameId parse failed");
                    mapping = null;
                }

                if (mapping == null)
                {
                    error = "No emulator mapping for this game. Run a library update.";
                    return false;
                }

                return true;
            }

            // Last resort: match by play-action emulator + platform name.
            mapping = MatchByPlayAction(game, romM);
            if (mapping == null)
            {
                error = "No emulator mapping for this game.";
                return false;
            }

            return true;
        }

        private static EmulatorMapping MatchByPlayAction(Game game, IRomM romM)
        {
            var action = game.GameActions?.FirstOrDefault(a => a.IsPlayAction && a.Type == GameActionType.Emulator)
                         ?? game.GameActions?.FirstOrDefault(a => a.Type == GameActionType.Emulator);
            if (action == null || romM.Settings.Mappings == null)
                return null;

            var platformName = game.Platforms?.FirstOrDefault()?.Name;
            return romM.Settings.Mappings.FirstOrDefault(m =>
                m.Enabled &&
                m.EmulatorId == action.EmulatorId &&
                (string.IsNullOrEmpty(platformName) ||
                 string.Equals(m.RomMPlatform?.PlayniteName, platformName, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
