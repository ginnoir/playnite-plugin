using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Models.RomM.Rom;
using System;

namespace RomM
{
	internal interface IRomM
	{
        ILogger Logger { get; }
		IPlayniteAPI Playnite { get; }
        Guid Id { get; }

        Settings.SettingsViewModel Settings { get; }
        MetadataProperty Source { get; }
        Downloads.DownloadQueueController DownloadQueueController { get; }
        string GetPluginUserDataPath();
        /// <summary>Directory of per-ROM sidecar JSON files (<c>{sha1}.json</c>) written on import.</summary>
        string ROMDataPath { get; }
        RomMRom FetchRom(string romId);

    }
}