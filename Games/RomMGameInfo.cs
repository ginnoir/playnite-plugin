using Playnite.SDK.Models;
using System;
using System.Diagnostics;
using System.IO;
using ProtoBuf;

namespace RomM.Games
{
    // Pure serialization half of RomMGameInfo: the protobuf contract behind the legacy "!0..." game
    // ids, plus the (de)serialization round-trip. Split from the plugin-coupled half (Mapping, install
    // controllers) so it can be unit-tested without the Playnite/plugin runtime. The protobuf member
    // numbers MUST stay stable — existing installs encode their game ids with them.
    [ProtoContract]
    internal partial class RomMGameInfo
    {
        [ProtoMember(1)]
        public Guid MappingId { get; set; }

        [ProtoMember(2)]
        public string DownloadUrl { get; set; }

        [ProtoMember(3)]
        public string FileName { get; set; }

        [ProtoMember(4)]
        public bool HasMultipleFiles { get; set; }

        public string AsGameId()
        {
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, this);
                return $"!0{Convert.ToBase64String(ms.ToArray())}";
            }
        }

        public static T FromGame<T>(Game game) where T : RomMGameInfo
        {
            return FromGameIdString<T>(game.GameId);
        }

        public static T FromGameMetadata<T>(GameMetadata game) where T : RomMGameInfo
        {
            return FromGameIdString<T>(game.GameId);
        }

        private static T FromGameIdString<T>(string gameId) where T : RomMGameInfo
        {
            Debug.Assert(gameId != null, "GameId is null");
            Debug.Assert(gameId.Length > 0, "GameId is empty");
            Debug.Assert(gameId[0] == '!', "GameId is not in expected format. (Legacy game that didn't get converted?)");
            Debug.Assert(gameId.Length > 2, $"GameId is too short ({gameId.Length} chars)");
            Debug.Assert(gameId[1] == '0', $"GameId is marked as being serialized ProtoBuf, but of invalid version. (Expected 0, got {gameId[1]})");

            var gameInfoStr = Convert.FromBase64String(gameId.Substring(2));
            using (var ms = new MemoryStream(gameInfoStr))
            {
                return Serializer.Deserialize<T>(ms);
            }
        }
    }
}
