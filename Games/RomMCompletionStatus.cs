using RomM.Models.RomM.Rom;

namespace RomM.Games
{
    // Maps a server-side rom_user to the Playnite completion-status name. Pure so the precedence and
    // fallback rules are unit-testable without the Playnite database (the name -> status-id lookup
    // stays in the importer).
    internal static class RomMCompletionStatus
    {
        // "now playing" and "backlogged" take precedence over the reported status; an unknown/missing
        // status falls back to "Not Played" rather than throwing.
        public static string ResolvePlayniteStatusName(RomMRomUser user)
        {
            if (user.Backlogged || user.NowPlaying)
                return user.NowPlaying
                    ? RomMRomUser.CompletionStatusMap["now_playing"]
                    : RomMRomUser.CompletionStatusMap["backlogged"];

            var romMStatus = user.Status ?? "not_played";
            if (!RomMRomUser.CompletionStatusMap.TryGetValue(romMStatus, out var name))
                name = RomMRomUser.CompletionStatusMap["not_played"];

            return name;
        }
    }
}
