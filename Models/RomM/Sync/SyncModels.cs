using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace RomM.Models.RomM.Sync
{
    // Mirrors RomM backend schemas verified in docs/save-sync/CONTRACT.md.
    // All classes are deserialization targets (server -> plugin) unless noted as a request payload.

    /// <summary>Sync action returned by /api/sync/negotiate (server-decided).</summary>
    public static class SyncActions
    {
        public const string Upload = "upload";
        public const string Download = "download";
        public const string Conflict = "conflict";
        public const string NoOp = "no_op";
    }

    /// <summary>RomM SyncMode. The plugin always registers as <see cref="Api"/>.</summary>
    public static class SyncModes
    {
        public const string Api = "api";
        public const string FileTransfer = "file_transfer";
        public const string PushPull = "push_pull";
    }

    /// <summary>Base fields shared by Save/State/Screenshot assets (RomM BaseAsset).</summary>
    public class RomMAsset
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("rom_id")] public int RomId { get; set; }
        [JsonProperty("user_id")] public int UserId { get; set; }
        [JsonProperty("file_name")] public string FileName { get; set; }
        [JsonProperty("file_name_no_tags")] public string FileNameNoTags { get; set; }
        [JsonProperty("file_name_no_ext")] public string FileNameNoExt { get; set; }
        [JsonProperty("file_extension")] public string FileExtension { get; set; }
        [JsonProperty("file_path")] public string FilePath { get; set; }
        [JsonProperty("file_size_bytes")] public long FileSizeBytes { get; set; }
        [JsonProperty("full_path")] public string FullPath { get; set; }
        [JsonProperty("download_path")] public string DownloadPath { get; set; }
        [JsonProperty("missing_from_fs")] public bool MissingFromFs { get; set; }
        [JsonProperty("created_at")] public DateTime CreatedAt { get; set; }
        [JsonProperty("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    public class RomMScreenshot : RomMAsset
    {
    }

    /// <summary>One device's sync record for a save (SaveSchema.device_syncs[]).</summary>
    public class RomMDeviceSync
    {
        [JsonProperty("device_id")] public string DeviceId { get; set; }
        [JsonProperty("device_name")] public string DeviceName { get; set; }
        [JsonProperty("last_synced_at")] public DateTime LastSyncedAt { get; set; }
        [JsonProperty("is_untracked")] public bool IsUntracked { get; set; }
        [JsonProperty("is_current")] public bool IsCurrent { get; set; }
    }

    /// <summary>SaveSchema. content_hash is MD5 (see CONTRACT.md §4).</summary>
    public class RomMSave : RomMAsset
    {
        [JsonProperty("emulator")] public string Emulator { get; set; }
        [JsonProperty("slot")] public string Slot { get; set; }
        [JsonProperty("content_hash")] public string ContentHash { get; set; }
        [JsonProperty("screenshot")] public RomMScreenshot Screenshot { get; set; }
        [JsonProperty("origin_device_id")] public string OriginDeviceId { get; set; }
        [JsonProperty("device_syncs")] public List<RomMDeviceSync> DeviceSyncs { get; set; } = new List<RomMDeviceSync>();
    }

    /// <summary>StateSchema. States have no content_hash and no device_syncs (opaque, emulator-tagged).</summary>
    public class RomMState : RomMAsset
    {
        [JsonProperty("emulator")] public string Emulator { get; set; }
        [JsonProperty("screenshot")] public RomMScreenshot Screenshot { get; set; }
    }

    public class RomMDeviceCreateResponse
    {
        [JsonProperty("device_id")] public string DeviceId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("created_at")] public DateTime CreatedAt { get; set; }
    }

    /// <summary>Request payload for POST /api/devices.</summary>
    public class RomMDeviceCreatePayload
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("platform")] public string Platform { get; set; }
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("client_version")] public string ClientVersion { get; set; }
        [JsonProperty("hostname")] public string Hostname { get; set; }
        [JsonProperty("mac_address")] public string MacAddress { get; set; }
        [JsonProperty("sync_mode")] public string SyncMode { get; set; } = SyncModes.Api;
        [JsonProperty("sync_config")] public object SyncConfig { get; set; }
        [JsonProperty("allow_existing")] public bool AllowExisting { get; set; } = true;
        [JsonProperty("allow_duplicate")] public bool AllowDuplicate { get; set; } = false;
        [JsonProperty("reset_syncs")] public bool ResetSyncs { get; set; } = false;
    }

    /// <summary>One client save row sent in POST /api/sync/negotiate. updated_at must be stable UTC.</summary>
    public class ClientSaveState
    {
        [JsonProperty("rom_id")] public int RomId { get; set; }
        [JsonProperty("file_name")] public string FileName { get; set; }
        [JsonProperty("slot")] public string Slot { get; set; }
        [JsonProperty("emulator")] public string Emulator { get; set; }
        [JsonProperty("content_hash")] public string ContentHash { get; set; }
        [JsonProperty("updated_at")] public DateTime UpdatedAt { get; set; }
        [JsonProperty("file_size_bytes")] public long FileSizeBytes { get; set; }
    }

    public class SyncNegotiatePayload
    {
        [JsonProperty("device_id")] public string DeviceId { get; set; }
        [JsonProperty("saves")] public List<ClientSaveState> Saves { get; set; } = new List<ClientSaveState>();
    }

    /// <summary>One operation in the negotiate response (action is a <see cref="SyncActions"/> value).</summary>
    public class RomMSyncOperation
    {
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("rom_id")] public int RomId { get; set; }
        [JsonProperty("save_id")] public int? SaveId { get; set; }
        [JsonProperty("file_name")] public string FileName { get; set; }
        [JsonProperty("slot")] public string Slot { get; set; }
        [JsonProperty("emulator")] public string Emulator { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
        [JsonProperty("server_updated_at")] public DateTime? ServerUpdatedAt { get; set; }
        [JsonProperty("server_content_hash")] public string ServerContentHash { get; set; }
    }

    public class RomMSyncNegotiateResponse
    {
        [JsonProperty("session_id")] public int SessionId { get; set; }
        [JsonProperty("operations")] public List<RomMSyncOperation> Operations { get; set; } = new List<RomMSyncOperation>();
        [JsonProperty("total_upload")] public int TotalUpload { get; set; }
        [JsonProperty("total_download")] public int TotalDownload { get; set; }
        [JsonProperty("total_conflict")] public int TotalConflict { get; set; }
        [JsonProperty("total_no_op")] public int TotalNoOp { get; set; }
    }

    /// <summary>One play-session entry for POST /api/sync/sessions/{id}/complete.</summary>
    public class SyncPlaySessionEntry
    {
        [JsonProperty("rom_id")] public int? RomId { get; set; }
        [JsonProperty("save_slot")] public string SaveSlot { get; set; }
        [JsonProperty("start_time")] public DateTime StartTime { get; set; }
        [JsonProperty("end_time")] public DateTime EndTime { get; set; }
        [JsonProperty("duration_ms")] public long DurationMs { get; set; }
    }

    public class SyncCompletePayload
    {
        [JsonProperty("operations_completed")] public int OperationsCompleted { get; set; }
        [JsonProperty("operations_failed")] public int OperationsFailed { get; set; }
        [JsonProperty("play_sessions", NullValueHandling = NullValueHandling.Ignore)]
        public List<SyncPlaySessionEntry> PlaySessions { get; set; }
    }
}
