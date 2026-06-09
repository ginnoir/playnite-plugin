using RomM.SaveSync.Converters;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RomM.Tests.Converters
{
    /// <summary>
    /// The N64 .srm layout is reverse-engineered from libretro mupen64plus-next, so these tests pin
    /// the verified offsets (eep@0x0/0x800, mpk@0x800/0x20000, sra@0x20800/0x8000, fla@0x28800/0x20000)
    /// and the split/join round-trip guarantee.
    /// </summary>
    public sealed class N64SaveConverterTests
    {
        private const int EepLen = 0x00800;
        private const int MpkOff = 0x00800, MpkLen = 0x20000;
        private const int SraOff = 0x20800, SraLen = 0x08000;
        private const int FlaOff = 0x28800, FlaLen = 0x20000;
        private const int SrmSize = 0x48800; // 296960

        private readonly N64SaveConverter _converter = new N64SaveConverter();

        private static ConversionContext Ctx(string targetExt = "srm")
        {
            return new ConversionContext
            {
                BaseName = "game",
                CanonicalExtension = "srm",
                TargetNativeExtension = targetExt,
                Logger = null,
            };
        }

        [Fact]
        public void ToCanonical_FromComponents_PlacesDataAtVerifiedOffsets()
        {
            var eep = Filled(EepLen, 0x11);
            var sra = Filled(SraLen, 0x33);
            var native = new List<SaveFile>
            {
                new SaveFile("game.eep", eep),
                new SaveFile("game.sra", sra),
            };

            var canonical = _converter.ToCanonical(native, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal("game.srm", canonical.Name);
            Assert.Equal(SrmSize, canonical.Bytes.Length);
            Assert.Equal(0x11, canonical.Bytes[0]);                  // eep starts at 0x0
            Assert.Equal(0x11, canonical.Bytes[EepLen - 1]);         // eep ends at 0x7FF
            Assert.Equal(0x00, canonical.Bytes[MpkOff]);             // no mpk supplied -> zeros
            Assert.Equal(0x33, canonical.Bytes[SraOff]);             // sra starts at 0x20800
            Assert.Equal(0x33, canonical.Bytes[SraOff + SraLen - 1]);
            Assert.Equal(0x00, canonical.Bytes[FlaOff]);             // no fla supplied -> zeros
        }

        [Fact]
        public void RoundTrip_AllComponentsToSrmAndBack_PreservesEveryByte()
        {
            var eep = Filled(EepLen, 0x11);
            var mpk = Filled(MpkLen, 0x22);
            var sra = Filled(SraLen, 0x33);
            var fla = Filled(FlaLen, 0x44);
            var native = new List<SaveFile>
            {
                new SaveFile("game.eep", eep),
                new SaveFile("game.mpk", mpk),
                new SaveFile("game.sra", sra),
                new SaveFile("game.fla", fla),
            };

            var canonical = _converter.ToCanonical(native, Ctx());
            Assert.NotNull(canonical);

            var split = _converter.FromCanonical(canonical, Ctx(targetExt: "eep"));
            Assert.NotNull(split);
            Assert.Equal(4, split.Count);
            Assert.Equal(eep, split.First(f => f.Name == "game.eep").Bytes);
            Assert.Equal(mpk, split.First(f => f.Name == "game.mpk").Bytes);
            Assert.Equal(sra, split.First(f => f.Name == "game.sra").Bytes);
            Assert.Equal(fla, split.First(f => f.Name == "game.fla").Bytes);
        }

        [Fact]
        public void ToCanonical_CanonicalSizeSrm_PassesThroughVerbatim()
        {
            var srmBytes = Filled(SrmSize, 0x77);
            var native = new List<SaveFile> { new SaveFile("Some Game (USA).srm", srmBytes) };

            var canonical = _converter.ToCanonical(native, Ctx());

            Assert.NotNull(canonical);
            Assert.Equal("game.srm", canonical.Name);
            Assert.Equal(srmBytes, canonical.Bytes);
        }

        [Fact]
        public void ToCanonical_OversizedComponent_FailsRoundTripAndReturnsNull()
        {
            // An .eep longer than its 0x800 window means our layout assumption is wrong for this
            // save; the converter must refuse rather than truncate silently.
            var native = new List<SaveFile> { new SaveFile("game.eep", Filled(EepLen + 0x100, 0x11)) };

            Assert.Null(_converter.ToCanonical(native, Ctx()));
        }

        [Fact]
        public void ToCanonical_NoRecognisedComponents_ReturnsNull()
        {
            var native = new List<SaveFile> { new SaveFile("game.weird", Filled(0x100, 0x01)) };

            Assert.Null(_converter.ToCanonical(native, Ctx()));
        }

        [Fact]
        public void FromCanonical_SrmTarget_KeepsSingleFile()
        {
            var canonical = new SaveFile("game.srm", Filled(SrmSize, 0x55));

            var outputs = _converter.FromCanonical(canonical, Ctx(targetExt: "srm"));

            Assert.NotNull(outputs);
            var file = Assert.Single(outputs);
            Assert.Equal("game.srm", file.Name);
            Assert.Equal(canonical.Bytes, file.Bytes);
        }

        [Fact]
        public void FromCanonical_NonCanonicalSize_FallsBackToSrmAsIs()
        {
            var canonical = new SaveFile("game.srm", Filled(0x1234, 0x55));

            var outputs = _converter.FromCanonical(canonical, Ctx(targetExt: "eep"));

            Assert.NotNull(outputs);
            var file = Assert.Single(outputs);
            Assert.Equal("game.srm", file.Name);
            Assert.Equal(canonical.Bytes, file.Bytes);
        }

        [Fact]
        public void FromCanonical_SplitTarget_EmitsOnlyComponentsWithData()
        {
            var canonicalBytes = new byte[SrmSize];
            // Only the SRAM window carries data.
            for (int i = 0; i < SraLen; i++)
            {
                canonicalBytes[SraOff + i] = 0x33;
            }
            var canonical = new SaveFile("game.srm", canonicalBytes);

            var outputs = _converter.FromCanonical(canonical, Ctx(targetExt: "sra"));

            Assert.NotNull(outputs);
            var file = Assert.Single(outputs);
            Assert.Equal("game.sra", file.Name);
            Assert.Equal(SraLen, file.Bytes.Length);
            Assert.All(file.Bytes, b => Assert.Equal(0x33, b));
        }

        private static byte[] Filled(int length, byte value)
        {
            var buffer = new byte[length];
            for (int i = 0; i < length; i++)
            {
                buffer[i] = value;
            }
            return buffer;
        }
    }
}
