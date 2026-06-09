using Playnite.SDK;
using RomM.Models.RomM.Sync;
using RomM.Settings;
using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;

namespace RomM.SaveSync
{
    /// <summary>
    /// Registers this Playnite install as a RomM "device" (sync_mode = api) and persists the
    /// returned device_id. RomM fingerprints on (mac_address, hostname, platform) with
    /// allow_existing=true, so repeated registration is idempotent. See CONTRACT.md §2.
    /// </summary>
    internal static class DeviceIdentity
    {
        private const string ClientId = "playnite-plugin";
        private const string PlatformName = "Windows";

        /// <summary>
        /// Returns a usable device_id, registering on first use. A persisted id is trusted as-is
        /// (the engine re-registers if the server later 404s it).
        /// </summary>
        public static string EnsureRegistered(SaveSyncClient client, SettingsViewModel settings, ILogger logger)
        {
            if (!string.IsNullOrEmpty(settings.DeviceId))
            {
                return settings.DeviceId;
            }
            return Register(client, settings, logger);
        }

        /// <summary>Force a (re-)registration and persist the result. Returns null on failure.</summary>
        public static string Register(SaveSyncClient client, SettingsViewModel settings, ILogger logger)
        {
            var name = string.IsNullOrWhiteSpace(settings.DeviceName) ? Environment.MachineName : settings.DeviceName;
            var payload = new RomMDeviceCreatePayload
            {
                Name = name,
                Platform = PlatformName,
                Client = ClientId,
                ClientVersion = PluginVersion(),
                Hostname = Environment.MachineName,
                MacAddress = GetPrimaryMacAddress(),
                SyncMode = SyncModes.Api,
                AllowExisting = true,
            };

            var result = client.RegisterDevice(payload);
            if (!result.Ok || result.Value == null)
            {
                if (result.Forbidden)
                {
                    logger.Error("RomM device registration was forbidden (403). The API token is missing the 'devices.write' scope.");
                }
                else
                {
                    logger.Error($"RomM device registration failed ({(int)result.Status}): {result.Error}");
                }
                return null;
            }

            settings.DeviceId = result.Value.DeviceId;
            if (string.IsNullOrWhiteSpace(settings.DeviceName))
            {
                settings.DeviceName = result.Value.Name ?? name;
            }
            settings.Save();
            logger.Info($"Registered RomM device '{settings.DeviceName}' ({settings.DeviceId}).");
            return settings.DeviceId;
        }

        /// <summary>Clear the persisted id and register fresh (settings UI "Re-register device").</summary>
        public static string ReRegister(SaveSyncClient client, SettingsViewModel settings, ILogger logger)
        {
            settings.DeviceId = "";
            return Register(client, settings, logger);
        }

        private static string PluginVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }

        /// <summary>
        /// Best-effort stable MAC for fingerprinting. Picks the first operational, non-loopback,
        /// non-virtual interface with a physical address; formats as AA:BB:CC:DD:EE:FF. Returns null
        /// if none found (registration still works — hostname+platform fingerprint).
        /// </summary>
        private static string GetPrimaryMacAddress()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .Where(n => n.GetPhysicalAddress() != null && n.GetPhysicalAddress().GetAddressBytes().Length == 6)
                    .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
                    .FirstOrDefault();

                var bytes = nic?.GetPhysicalAddress()?.GetAddressBytes();
                if (bytes == null || bytes.Length != 6)
                {
                    return null;
                }
                return string.Join(":", bytes.Select(b => b.ToString("X2")));
            }
            catch
            {
                return null;
            }
        }
    }
}
