using System.Collections.Generic;
using RomM.Models.RomM.Rom;

namespace RomM.Games
{
    // Classifies a ROM within a merged-revisions group. Pure so the main-sibling resolution is
    // unit-testable; the importer passes its id->ROM index for the cross-sibling lookups.
    internal static class RomMSiblings
    {
        public static MainSibling ClassifyMain(RomMRom rom, IReadOnlyDictionary<int, RomMRom> romsById)
        {
            // Is this ROM itself the main sibling?
            if (rom.RomUser.IsMainSibling)
                return MainSibling.Current;

            // Is another sibling (present in this batch) the main one?
            foreach (var sibling in rom.Siblings)
            {
                if (romsById.TryGetValue(sibling.Id, out var siblingRom) &&
                    siblingRom.RomUser != null && siblingRom.RomUser.IsMainSibling)
                {
                    return MainSibling.Other;
                }
            }

            return MainSibling.None;
        }
    }
}
