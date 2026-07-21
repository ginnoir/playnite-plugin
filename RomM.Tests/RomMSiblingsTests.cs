using System.Collections.Generic;
using RomM.Games;
using RomM.Models.RomM.Rom;
using Xunit;

namespace RomM.Tests
{
    public class RomMSiblingsTests
    {
        private static RomMRom Rom(int id, bool isMain, params int[] siblingIds)
        {
            var rom = new RomMRom
            {
                Id = id,
                RomUser = new RomMRomUser { IsMainSibling = isMain },
                Siblings = new List<RomMSibling>(),
            };
            foreach (var sid in siblingIds)
                rom.Siblings.Add(new RomMSibling { Id = sid });
            return rom;
        }

        [Fact]
        public void Current_when_rom_itself_is_main()
        {
            Assert.Equal(MainSibling.Current,
                RomMSiblings.ClassifyMain(Rom(1, true), new Dictionary<int, RomMRom>()));
        }

        [Fact]
        public void Other_when_a_sibling_in_batch_is_main()
        {
            var main = Rom(2, true);
            var byId = new Dictionary<int, RomMRom> { { 2, main } };

            Assert.Equal(MainSibling.Other, RomMSiblings.ClassifyMain(Rom(1, false, 2), byId));
        }

        [Fact]
        public void None_when_sibling_not_present_in_batch()
        {
            Assert.Equal(MainSibling.None,
                RomMSiblings.ClassifyMain(Rom(1, false, 99), new Dictionary<int, RomMRom>()));
        }

        [Fact]
        public void None_when_no_sibling_is_main()
        {
            var sibling = Rom(2, false);
            var byId = new Dictionary<int, RomMRom> { { 2, sibling } };

            Assert.Equal(MainSibling.None, RomMSiblings.ClassifyMain(Rom(1, false, 2), byId));
        }
    }
}
