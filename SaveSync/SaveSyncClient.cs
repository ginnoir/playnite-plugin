using Newtonsoft.Json;
using Playnite.SDK;
using RomM.Models.RomM.Sync;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;

namespace RomM.SaveSync
{
    /// <summary>
    /// Result envelope for save-sync HTTP calls. Non-2xx outcomes (esp. 409 conflict and 403 scope)
    /// are surfaced as flags rather than thrown, so callers branch instead of catching control flow.
    /// </summary>
    public class ApiResult<T>
    {
        public bool Ok { get; set; }
        public HttpStatusCode Status { get; set; }
        public string Error { get; set; }
        public T Value { get; set; }

        public bool Conflict => Status == HttpStatusCode.Conflict;          // 409 — save changed since last sync
        public bool Forbidden => Status == HttpStatusCode.Forbidden;        // 403 — token missing a scope
        public bool Unauthorized => Status == HttpStatusCode.Unauthorized;  // 401 — bad/expired auth
        public bool NotFound => Status == HttpStatusCode.NotFound;          // 404 — device/save gone server-side

        public static ApiResult<T> Success(T value, HttpStatusCode status) =>
            new ApiResult<T> { Ok = true, Value = value, Status = status };

        public static ApiResult<T> Failure(HttpStatusCode status, string error) =>
            new ApiResult<T> { Ok = false, Status = status, Error = error };
    }

    /// <summary>
    /// Thin typed wrapper over RomM's device / saves / states / sync endpoints.
    /// Reuses <see cref="HttpClientSingleton"/> (auth header already configured).
    /// Contract: docs/save-sync/CONTRACT.md.
    /// </summary>
    internal class SaveSyncClient
    {
        // Serialize outgoing datetimes as UTC ISO-8601 (…Z) so negotiate timestamps are unambiguous.
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            NullValueHandling = NullValueHandling.Ignore,
        };

        private readonly string _host;
        private readonly ILogger _logger;

        public SaveSyncClient(string host, ILogger logger)
        {
            _host = host;
            _logger = logger;
        }

        private string Url(string relativePath, NameValueCollection query = null)
        {
            var baseUrl = $"{_host?.TrimEnd('/')}/{relativePath?.TrimStart('/') ?? ""}";
            if (query == null || query.Count == 0)
                return baseUrl;

            var builder = new UriBuilder(baseUrl);
            var existing = HttpUtility.ParseQueryString(builder.Query);
            foreach (string key in query)
            {
                if (query[key] != null)
                    existing[key] = query[key];
            }
            builder.Query = existing.ToString();
            return builder.Uri.ToString();
        }

        private static StringContent JsonBody(object payload) =>
            new StringContent(JsonConvert.SerializeObject(payload, JsonSettings), Encoding.UTF8, "application/json");

        private ApiResult<T> Send<T>(HttpRequestMessage request, Func<string, T> parse)
        {
            try
            {
                using (var response = HttpClientSingleton.Instance.SendAsync(request).GetAwaiter().GetResult())
                {
                    var body = response.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn($"RomM sync {request.Method} {request.RequestUri.AbsolutePath} -> {(int)response.StatusCode}: {Trim(body)}");
                        return ApiResult<T>.Failure(response.StatusCode, body);
                    }
                    return ApiResult<T>.Success(parse != null ? parse(body) : default, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"RomM sync {request.Method} {request.RequestUri} failed");
                return ApiResult<T>.Failure(0, ex.Message);
            }
        }

        private static string Trim(string s) => string.IsNullOrEmpty(s) || s.Length <= 500 ? s : s.Substring(0, 500) + "…";

        // ---- Devices --------------------------------------------------------

        public ApiResult<RomMDeviceCreateResponse> RegisterDevice(RomMDeviceCreatePayload payload)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Url("api/devices")) { Content = JsonBody(payload) };
            return Send(req, b => JsonConvert.DeserializeObject<RomMDeviceCreateResponse>(b));
        }

        /// <summary>Read-scope probe used by the settings "test connection" action.</summary>
        public ApiResult<bool> CheckDevicesReadable()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Url("api/devices"));
            return Send(req, _ => true);
        }

        // ---- Sync negotiation ----------------------------------------------

        /// <summary>
        /// Probe for the /api/sync endpoints, which only exist on RomM >= 4.9.
        /// 200 = supported; 404 = server too old for save sync.
        /// </summary>
        public ApiResult<bool> CheckSyncSupported()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Url("api/sync/sessions"));
            return Send(req, _ => true);
        }

        public ApiResult<RomMSyncNegotiateResponse> Negotiate(SyncNegotiatePayload payload)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Url("api/sync/negotiate")) { Content = JsonBody(payload) };
            return Send(req, b => JsonConvert.DeserializeObject<RomMSyncNegotiateResponse>(b));
        }

        public ApiResult<bool> CompleteSession(int sessionId, SyncCompletePayload payload)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Url($"api/sync/sessions/{sessionId}/complete")) { Content = JsonBody(payload) };
            return Send(req, _ => true);
        }

        // ---- Saves ----------------------------------------------------------

        public ApiResult<List<RomMSave>> GetSaves(int? romId = null, string deviceId = null, string slot = null)
        {
            var q = new NameValueCollection();
            if (romId.HasValue) q["rom_id"] = romId.Value.ToString();
            if (deviceId != null) q["device_id"] = deviceId;
            if (slot != null) q["slot"] = slot;
            var req = new HttpRequestMessage(HttpMethod.Get, Url("api/saves", q));
            return Send(req, b => JsonConvert.DeserializeObject<List<RomMSave>>(b) ?? new List<RomMSave>());
        }

        public ApiResult<byte[]> DownloadSaveContent(int saveId, string deviceId = null, int? sessionId = null, bool optimistic = true)
        {
            var q = new NameValueCollection { { "optimistic", optimistic ? "true" : "false" } };
            if (deviceId != null) q["device_id"] = deviceId;
            if (sessionId.HasValue) q["session_id"] = sessionId.Value.ToString();
            var req = new HttpRequestMessage(HttpMethod.Get, Url($"api/saves/{saveId}/content", q));
            return SendBytes(req);
        }

        /// <summary>POST /api/saves (new save). Returns 409 (see <see cref="ApiResult{T}.Conflict"/>) when the
        /// slot/save changed since this device last synced and <paramref name="overwrite"/> is false.</summary>
        public ApiResult<RomMSave> UploadSave(
            int romId, byte[] content, string fileName,
            string emulator = null, string slot = null, string deviceId = null, int? sessionId = null,
            bool overwrite = false, bool autocleanup = false, int autocleanupLimit = 10,
            byte[] screenshot = null, string screenshotName = null)
        {
            var q = new NameValueCollection { { "rom_id", romId.ToString() } };
            if (emulator != null) q["emulator"] = emulator;
            if (slot != null) q["slot"] = slot;
            if (deviceId != null) q["device_id"] = deviceId;
            if (sessionId.HasValue) q["session_id"] = sessionId.Value.ToString();
            if (overwrite) q["overwrite"] = "true";
            if (autocleanup) { q["autocleanup"] = "true"; q["autocleanup_limit"] = autocleanupLimit.ToString(); }

            var form = BuildAssetForm("saveFile", content, fileName, screenshot, screenshotName);
            var req = new HttpRequestMessage(HttpMethod.Post, Url("api/saves", q)) { Content = form };
            return Send(req, b => JsonConvert.DeserializeObject<RomMSave>(b));
        }

        /// <summary>PUT /api/saves/{id} — replace content of an existing save (used for "keep local" resolution).</summary>
        public ApiResult<RomMSave> UpdateSave(int saveId, byte[] content, string fileName,
            string deviceId = null, byte[] screenshot = null, string screenshotName = null)
        {
            var q = new NameValueCollection();
            if (deviceId != null) q["device_id"] = deviceId;
            var form = BuildAssetForm("saveFile", content, fileName, screenshot, screenshotName);
            var req = new HttpRequestMessage(HttpMethod.Put, Url($"api/saves/{saveId}", q)) { Content = form };
            return Send(req, b => JsonConvert.DeserializeObject<RomMSave>(b));
        }

        public ApiResult<bool> DeleteSaves(IEnumerable<int> ids)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Url("api/saves/delete"))
            {
                Content = JsonBody(new { saves = new List<int>(ids) })
            };
            return Send(req, _ => true);
        }

        // ---- States (opaque, emulator-tagged, never converted) --------------

        public ApiResult<List<RomMState>> GetStates(int? romId = null)
        {
            var q = new NameValueCollection();
            if (romId.HasValue) q["rom_id"] = romId.Value.ToString();
            var req = new HttpRequestMessage(HttpMethod.Get, Url("api/states", q));
            return Send(req, b => JsonConvert.DeserializeObject<List<RomMState>>(b) ?? new List<RomMState>());
        }

        public ApiResult<RomMState> UploadState(int romId, byte[] content, string fileName,
            string emulator = null, byte[] screenshot = null, string screenshotName = null)
        {
            var q = new NameValueCollection { { "rom_id", romId.ToString() } };
            if (emulator != null) q["emulator"] = emulator;
            var form = BuildAssetForm("stateFile", content, fileName, screenshot, screenshotName);
            var req = new HttpRequestMessage(HttpMethod.Post, Url("api/states", q)) { Content = form };
            return Send(req, b => JsonConvert.DeserializeObject<RomMState>(b));
        }

        /// <summary>States have no /content endpoint; download via the asset's <c>download_path</c>.</summary>
        public ApiResult<byte[]> DownloadByPath(string downloadPath)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Url(downloadPath));
            return SendBytes(req);
        }

        // ---- Helpers --------------------------------------------------------

        private ApiResult<byte[]> SendBytes(HttpRequestMessage request)
        {
            try
            {
                using (var response = HttpClientSingleton.Instance.SendAsync(request).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = response.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                        _logger.Warn($"RomM sync GET {request.RequestUri.AbsolutePath} -> {(int)response.StatusCode}: {Trim(body)}");
                        return ApiResult<byte[]>.Failure(response.StatusCode, body);
                    }
                    var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    return ApiResult<byte[]>.Success(bytes, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"RomM sync download {request.RequestUri} failed");
                return ApiResult<byte[]>.Failure(0, ex.Message);
            }
        }

        private static MultipartFormDataContent BuildAssetForm(
            string fileField, byte[] content, string fileName, byte[] screenshot, string screenshotName)
        {
            var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, fileField, fileName);

            if (screenshot != null && !string.IsNullOrEmpty(screenshotName))
            {
                var shot = new ByteArrayContent(screenshot);
                shot.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                form.Add(shot, "screenshotFile", screenshotName);
            }
            return form;
        }
    }
}
