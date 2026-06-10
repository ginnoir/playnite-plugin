namespace RomM.SaveSync
{
    internal enum SyncStatus
    {
        Synced,
        LocalAhead,
        ServerAhead,
        Conflict,
        LocalOnly,
        ServerOnly,
        Unknown,
    }

    internal class SaveStatusItem
    {
        public string Label { get; set; }
        public string LocalTime { get; set; }
        public string ServerTime { get; set; }
        public string StatusText { get; set; }
        public SyncStatus Status { get; set; }
        public bool IsState { get; set; }
    }
}
