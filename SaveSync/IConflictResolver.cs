using System;
using RomM.Models.RomM.Sync;
using RomM.Settings;

namespace RomM.SaveSync
{
    internal enum ConflictChoice
    {
        KeepLocal,   // upload local with overwrite
        KeepRemote,  // download server over local
        KeepBoth,    // archive local server-side (null-slot row), pull remote as the live save
        Skip         // leave both sides untouched this run
    }

    /// <summary>Everything the UI/policy needs to decide a conflict.</summary>
    internal sealed class ConflictInfo
    {
        public string GameName { get; set; }
        public string RomBaseName { get; set; }
        public RomMSyncOperation Operation { get; set; }
        public long LocalSizeBytes { get; set; }
        public DateTime LocalUpdatedUtc { get; set; }
        public long? RemoteSizeBytes { get; set; }
        public DateTime? RemoteUpdatedUtc { get; set; }
    }

    internal interface IConflictResolver
    {
        ConflictChoice Resolve(ConflictInfo info);
    }

    /// <summary>
    /// Non-interactive resolver driven by <see cref="ConflictPolicy"/>. Used when there is no UI
    /// (e.g. a background sync on game start) or when the user chose an automatic policy.
    /// "Ask" falls back to <see cref="ConflictChoice.KeepBoth"/> here — the only zero-data-loss
    /// default — so an unattended sync never silently discards a save.
    /// </summary>
    internal sealed class PolicyConflictResolver : IConflictResolver
    {
        private readonly ConflictPolicy _policy;

        public PolicyConflictResolver(ConflictPolicy policy)
        {
            _policy = policy;
        }

        public ConflictChoice Resolve(ConflictInfo info)
        {
            switch (_policy)
            {
                case ConflictPolicy.PreferLocal: return ConflictChoice.KeepLocal;
                case ConflictPolicy.PreferRemote: return ConflictChoice.KeepRemote;
                case ConflictPolicy.KeepBoth: return ConflictChoice.KeepBoth;
                case ConflictPolicy.Ask:
                default:
                    return ConflictChoice.KeepBoth;
            }
        }
    }
}
