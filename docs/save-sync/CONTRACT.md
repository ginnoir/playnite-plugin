# RomM Save-Sync Contract (verified from source)

> Source of truth: `rommapp/romm@master` backend, read 2026-06-09. Endpoints/algorithms below are
> transcribed from server code, not assumed. **Before shipping, re-verify the live items marked
> ⚠️LIVE against the user's actual RomM version** (a pinned `image: rommapp/romm:<tag>` is ideal),
> since the sync protocol is server-defined and still evolving.

This document is the authoritative contract for Tasks 2–13. The single most important fact:
**`content_hash` = MD5** (details below). If the client computes any other hash, every save is
reported as `conflict`.

---

## 1. Authentication & scopes

- Auth header reuses the existing plugin pattern (`HttpClientSingleton`): `Bearer rmm_<64 hex>` or
  Basic `user:pass`.
- The token **must** carry these scopes (dotted strings, from `handler/auth/constants.py::Scope`):
  - `assets.read`, `assets.write` — saves/states/screenshots
  - `devices.read`, `devices.write` — device registry + sync tracking
- A token created with only the default READ set will 403 on writes. The settings UI must offer a
  "Test connection & scopes" action that calls a write-scoped probe (e.g. `GET /api/devices` for
  read, and surfaces a clear error if device registration 403s).

---

## 2. Device registration — `POST /api/devices`  (scope `devices.write`)

Request body (`DeviceCreatePayload`):
```jsonc
{
  "name": "MyDesktop",            // display name; we use Environment.MachineName
  "platform": "Windows",          // part of the fingerprint
  "client": "playnite-plugin",    // stable client id for this plugin
  "client_version": "0.7.0",      // from extension.yaml
  "hostname": "MYDESKTOP",        // part of the fingerprint
  "mac_address": "AA:BB:CC:DD:EE:FF", // 17 chars max; part of the fingerprint
  "sync_mode": "api",             // REQUIRED choice — see note
  "sync_config": null,
  "allow_existing": true,         // idempotent: return existing device on fingerprint match
  "allow_duplicate": false,
  "reset_syncs": false
}
```
- **`sync_mode` = `api`.** Confirmed correct: `KNOWN_DEVICES` registers web / muOS(`grout`) /
  Android(`argosy-launcher`) all as `SyncMode.API`. `file_transfer` auto-creates server-side sync
  folders (SSH/file push); `push_pull` is server-driven via an RQ job. The negotiate/execute model
  we build is the `api` mode.
- **Fingerprint = (`mac_address`, `hostname`, `platform`)**. With `allow_existing:true`, a repeat
  registration returns the existing device (HTTP 200) instead of creating a duplicate (HTTP 201).
  Send a *stable* mac + hostname so re-registration is idempotent.
- Response (`DeviceCreateResponse`): `{ "device_id": "<uuid>", "name": "...", "created_at": "..." }`.
  **Persist `device_id` in `config.json`.** If the persisted id later 404s (server DB reset), re-register.
- `sync_enabled` defaults to `true` server-side; negotiate 400s if it's been disabled.

---

## 3. The conflict engine — `POST /api/sync/negotiate`  (scopes `assets.read`,`devices.read`)

**The server decides every action. The plugin reports state and executes the plan.**

Request:
```jsonc
{
  "device_id": "<uuid>",
  "saves": [
    {
      "rom_id": 123,
      "file_name": "Game.srm",     // canonical name (see §6)
      "slot": null,                // null = the live battery save (our canonical lane)
      "emulator": null,            // provenance only; null keeps one shared save (see §5)
      "content_hash": "<md5hex>",  // §4 — omit only if unreadable
      "updated_at": "2026-06-09T12:34:56Z", // UTC; drives the decision (see below)
      "file_size_bytes": 32768
    }
  ]
}
```

Response (`SyncNegotiateResponse`):
```jsonc
{
  "session_id": 42,
  "operations": [
    { "action": "upload|download|conflict|no_op", "rom_id": 123, "save_id": 7,
      "file_name": "Game.srm", "slot": null, "emulator": null, "reason": "...",
      "server_updated_at": "...", "server_content_hash": "<md5hex>" }
  ],
  "total_upload": 1, "total_download": 0, "total_conflict": 0, "total_no_op": 0
}
```

### Exact decision logic (`handler/sync/comparison.py::compare_save_state`)
Given `client_hash, client_updated_at, server_hash, server_updated_at, device_last_synced_at`:
1. `client_hash == server_hash` (both present) → **no_op** ("identical").
2. Else if `device_last_synced_at` exists:
   - `client_changed = client_updated_at > last_synced`; `server_changed = server_updated_at > last_synced`
   - both → **conflict**; client only → **upload**; server only → **download**; neither → **no_op**.
3. Else (no sync history): newer `updated_at` wins → **upload**/**download**; equal ts & different hash → **conflict**.

**Implications for the plugin:**
- `updated_at` must be a **stable, monotonic, UTC** timestamp. Use the local save file's last-write
  time converted to UTC. Do **not** send "now" on every negotiate or you'll spuriously beat the
  server's timestamp and force uploads.
- `device_last_synced_at` is tracked server-side and advanced whenever this `device_id` uploads or
  downloads that save (`upsert_sync`). So after a clean sync, an unchanged save returns `no_op`.
- Pairing key is **`(rom_id, slot)`** — `emulator` does not split the diff.

### ❗ Slot semantics (REVISED — live-verified on 4.9.0-beta.2, supersedes the master-snapshot policy)

The original policy ("null-slot = live save; slots = history") is **inverted** in the shipped 4.9
protocol (`endpoints/sync.py`, verified live 2026-06-09):

- **Negotiate only considers NAMED slots** (`get_saves(slot_not_null=True)`); null-slot rows are
  **archival-only** — they never pair, never download, never conflict, on any device.
- **The live battery-save lane is `slot = "default"`** (the same lane decky-romm-sync uses).
  The plugin encodes this as `SyncSlots.Live`.
- A slot accrues many rows (each slotted upload's stored `file_name` gets a server datetime tag
  ` [YYYY-MM-DD_HH-MM-SS]`); negotiate pairs against the **newest row per (rom_id, slot)**.
  Never derive local filenames from the server `file_name` — use the canonical name.
- **Negotiate returns operations for the user's ENTIRE slotted library**, not just the saves the
  client mentioned (downloads for every slotted save the device has never synced). A per-game sync
  MUST filter operations to `(rom_id == game, slot == "default")` or it will pull every other
  game's saves.
- Client-deletion heuristic: a slotted save the client doesn't mention, that this device previously
  synced, and that hasn't changed since, is treated as deleted-on-client → silently skipped (no
  re-download). If it HAS changed since last sync, it comes back as a download.
- `POST /api/sync/sessions/{id}/complete` with `play_sessions[]` verified live: 200, ingest
  `{"status":"created"}` per entry; `save_slot` should be `"default"` too.

---

## 4. content_hash algorithm — `handler/filesystem/assets_handler.py::compute_content_hash`

⚠️ **THE blocker. Replicate exactly.**

```
if file is a valid zip (zipfile.is_zipfile):
    for each non-directory entry, sorted by entry name:
        entry_md5 = md5(entry_bytes).hexdigest()
        line = f"{entry_name}:{entry_md5}"
    combined = "\n".join(lines)          # LF, in sorted-name order
    content_hash = md5(combined.utf8).hexdigest()
else:
    content_hash = md5(raw_file_bytes).hexdigest()   # streamed in 8192-byte chunks; result identical to one-shot
```
- Plain raw saves (`.srm`, `.sav`, `.mcd`, …): standard MD5 of the bytes. C#:
  `BitConverter.ToString(MD5.Create().ComputeHash(bytes)).Replace("-","").ToLowerInvariant()`.
- **Order of operations matters for our converters**: hash the bytes we actually upload (post-conversion,
  i.e. the canonical bytes), so the client hash matches what the server stores.
- States have **no** content_hash (server scans states with `should_hash=False`).

### Golden test (Task 5 acceptance)
Upload a known file, read back `SaveSchema.content_hash`, assert it equals the client's MD5 of the
same bytes. Add a zip fixture (multi-entry) to cover the composite path.

---

## 5. Saves endpoints — `/api/saves`  (scopes `assets.read` / `assets.write`)

| Method | Path | Notes |
|---|---|---|
| POST | `/api/saves?rom_id=&emulator=&slot=&device_id=&session_id=&overwrite=&autocleanup=&autocleanup_limit=` | multipart `saveFile`(+`screenshotFile?`). **409** if (with `device_id`, no `overwrite`) the slot/save changed since this device's last sync. Content-hash dedup within a slot returns the existing save. Returns `SaveSchema`. |
| GET | `/api/saves?rom_id=&platform_id=&device_id=&slot=` | list; with `device_id` each item carries `device_syncs[].is_current`. |
| GET | `/api/saves/{id}` | one `SaveSchema`. |
| GET | `/api/saves/{id}/content?device_id=&session_id=&optimistic=true` | download bytes; with `device_id`+`optimistic` advances this device's last_synced (marks current). |
| POST | `/api/saves/{id}/downloaded` body `{device_id}` | explicit "I have this" sync mark. |
| PUT | `/api/saves/{id}` | multipart `saveFile?`,`screenshotFile?` — replace content of an existing save. |
| POST | `/api/saves/delete` body `{saves:[ids]}` | bulk delete. |
| POST | `/api/saves/{id}/track` \| `/untrack` body `{device_id}` | per-device sync tracking toggle. |

`SaveSchema`: `id, rom_id, user_id, file_name, file_name_no_tags, file_name_no_ext, file_extension,
file_path, file_size_bytes, full_path, download_path, content_hash, emulator?, slot?, screenshot?,
origin_device_id?, device_syncs[], missing_from_fs, created_at, updated_at`.

**`emulator` tagging:** it becomes a server subfolder (`…/saves/{slug}/{rom_id}/{emulator}/`). Two
uploads with different `emulator` values are distinct files. For the cross-emulator-portable battery
save we therefore **omit `emulator` (or use one fixed canonical tag)** so a save written by mGBA and
one by RetroArch resolve to the same logical save. Provenance is still available via `origin_device_id`.

---

## 6. Canonical filename

- Server `sanitize_filename` (utils/filesystem.py): replaces `\ / : | ` → `-`; strips `* ? " < > +`;
  removes null bytes; trims; rejects empty. The stored `file_name` may differ from what we sent if the
  ROM name contains those chars — read it back from `SaveSchema.file_name`, don't assume.
- **Canonical save filename = ROM basename + canonical extension per platform** (e.g. GBA → `.srm`),
  chosen so every emulator's locator resolves the same logical save and the negotiate `(rom_id, slot)`
  pairing is stable across machines. Converters (Task 6) translate native↔canonical on the way in/out.

---

## 7. States endpoints — `/api/states`  (scopes `assets.read` / `assets.write`)

Parallel to saves but **no slot / device / conflict logic** server-side: POST (multipart `stateFile`,
`screenshotFile?`, `?rom_id=&emulator=`), GET list/by-id, PUT `{id}`, POST `/delete`. `StateSchema` =
`BaseAsset + emulator? + screenshot?` (no `content_hash`, no `device_syncs`). **States are opaque,
emulator+version specific — tag by `emulator`, never convert. Client-side diff by `(rom_id, file_name,
emulator)` + size + mtime.**

---

## 8. Session completion — `POST /api/sync/sessions/{id}/complete`  (scope `devices.write`)

```jsonc
{ "operations_completed": 3, "operations_failed": 0,
  "play_sessions": [ { "rom_id": 123, "save_slot": null,
                       "start_time": "...Z", "end_time": "...Z", "duration_ms": 600000 } ] }
```
- Close the `session_id` returned by negotiate. Optional `play_sessions[]` ingests playtime
  (`end_time > start_time`, seconds-truncated). Emit one entry when the game was launched via the plugin.
- Also `GET /api/sync/sessions[/{id}]` to inspect; `POST /api/sync/devices/{id}/push-pull` is
  push_pull-mode only (not us).

---

## 9. ⚠️LIVE items to re-verify against the user's instance before release

> **Live verification round 1 — 2026-06-09 against `roms.ginnoir.com` (RomM 4.8.1):**
> - **❗ RomM < 4.9 has NO `/api/sync/*` endpoints.** `POST /api/sync/negotiate` and
>   `GET /api/sync/sessions` both 404 on 4.8.1 (confirmed live + absent from the 4.8.1 source tree).
>   The negotiate protocol ships in **4.9.0-beta.x** (`backend/endpoints/sync.py` +
>   `handler/sync/comparison.py` present in tag `4.9.0-beta.2`). The plugin now detects this:
>   endpoint-404 (generic `"Not Found"` body) → "requires RomM 4.9+" message; device-404
>   (`"Device with ID … not found"`) → re-register. Settings "Test connection" probes
>   `GET /api/sync/sessions` and warns on pre-4.9 servers.
> - **❗ 4.8.1 server bug: raw (non-zip) saves get `content_hash = NULL`.** `_scan_asset` passes an
>   absolute path to `compute_content_hash`; the raw branch goes through `stream_file → validate_path`
>   which rejects absolute paths, so the exception handler returns `None`. The zip branch opens the
>   path directly and works. **Fixed in 4.9.0-beta.2** (paths made relative throughout).
> - **✅ Golden hash test (zip composite): MATCH.** A 2-entry zip uploaded to 4.8.1 returned exactly
>   our `SaveHashing.ComputeContentHash` value (per-entry md5, ordinal name sort, `\n` join, md5 of
>   UTF-8). Raw-path MD5 is covered by RFC 1321 vectors in `RomM.Tests`. Repeatable via
>   `docs/save-sync/tools/golden-hash-test.ps1`.
> - **✅ Item 2 verified live:** multipart field `saveFile` + query-string `rom_id` accepted;
>   `SaveSchema` response carried `id/file_name/file_size_bytes/content_hash/slot/emulator`.
>   `POST /api/saves/delete {"saves":[ids]}` works.
> - **Partially verified item 4:** the user's token has `devices.read` (GET /api/devices 200),
>   `assets.write` (upload + delete 200), `assets.read` (GET saves 200). `devices.write`
>   (device registration) not yet exercised.
> - **Item 5 (409 body) from the running version's source:** `{"detail": "Slot has a newer save since
>   your last sync"}` / `{"detail": "Save has been updated since your last sync"}` (4.8.1 saves.py).

> **Live verification round 2 — 2026-06-09 against `roms.ginnoir.com` upgraded to RomM 4.9.0-beta.2**
> (tools: `verify-contract.ps1`, `probe-negotiate.ps1`, `slot-lane-test.ps1`). ALL ITEMS CLOSED:
> 1. **✅ `updated_at` tolerance:** negotiate accepted `…Z` seconds, `…Z` fractional (µs), `+00:00`
>    offset, and even tz-naive — all HTTP 200. We send `yyyy-MM-ddTHH:mm:ssZ`.
> 2. **✅ Multipart** (round 1, unchanged on 4.9).
> 3. **✅ `overwrite=true`** on the slot lane replaces the NEWEST row in the slot (same save id) and
>    **advances this device's sync mark** — an immediately following negotiate with the same bytes
>    returns `no_op`. This is the correct KeepLocal primitive. `PUT /api/saves/{id}` also works
>    (replaces content, bumps `updated_at`) but targets a fixed id; POST+overwrite is preferred.
> 4. **✅ `devices.write`:** register 201 + `device_id` UUID; same-fingerprint re-register 200 with
>    the SAME id (idempotent, `allow_existing:true`); `DELETE /api/devices/{id}` exists → 204.
> 5. **✅ 409 bodies (both live-triggered):** per-save `{"detail":"Save has been updated since your
>    last sync"}`; slot-lane `{"detail":"Slot has a newer save since your last sync"}`.
> 6. **✅ Golden hash on 4.9.0-beta.2: RAW *and* ZIP match** (the 4.8.1 NULL-hash bug is fixed; the
>    server also backfilled all 27 legacy NULL hashes via a startup task, `errors=0`).
>
> **❗ Round-2 discovery — slot redesign (see §3):** negotiate ignores null-slot saves entirely
> (`slot_not_null=True`; archival-only) and returns ops for the whole slotted library. The plugin
> was reworked accordingly: live lane = `SyncSlots.Live` ("default"), per-game op filter
> `(rom_id, slot)`, KeepBoth archives to a null-slot row with a unique `[conflict …]`-suffixed name.
> Full slot-lane flow (upload→no_op→behind-back change→download→409→overwrite→no_op→complete with
> playtime ingest) verified end-to-end live by `slot-lane-test.ps1`.

## 10. Net effect on plugin design
The plugin does **not** implement conflict resolution math — RomM's `compare_save_state` does. The
plugin's responsibilities reduce to: register device → locate local saves/states/screenshots → hash
(MD5, §4) + read mtime as UTC → negotiate → execute returned ops (with format conversion + backups +
atomic writes) → complete (+ playtime). Conflicts are surfaced to the user (default policy: Ask).
