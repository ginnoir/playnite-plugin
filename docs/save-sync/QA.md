# Save Sync — Edge Cases & QA Matrix

> No .NET build toolchain is available in the dev environment used to author this feature, so the
> code is written to compile under the project's existing PlayniteSDK 6.16 / net462 setup but has
> **not been compiled or run here**. Build in Visual Studio (or CI) and work this matrix before release.

## Hardening already in code
| Concern | Where handled |
|---|---|
| Partial/cancelled write corrupts a save | `SafeFile.WriteAtomic` (temp file + `File.Replace`/move) |
| Overwriting loses the previous save | `SafeFile.Backup` → `SaveSync/backups/{romId}/<name>.<UTC>.bak` before every write |
| Emulator still holding the file | `SafeFile.IsLocked` → skip write with a warning |
| Wrong content hash → false conflicts | `SaveHashing` MD5 (+ zip composite) matches server; golden test below |
| Stale device_id after server reset | `SaveSyncController` re-registers on negotiate 404 and retries once |
| Token missing scopes | `ApiResult.Forbidden`; settings "Test connection / Register device" reports it |
| Offline RomM | All calls return `ApiResult.Failure`; sync is non-fatal, never blocks launch (push is async) |
| Portable Playnite paths | Locator uses `*Resolved` props that expand `{PlayniteDir}` |
| Bad cross-emulator conversion | Converters return null → engine syncs as-is + warns; N64 has a split/join round-trip self-check |
| Savestate corruption | States are never converted; opaque + emulator-tagged; backup before overwrite |

## Known gaps / follow-ups
- **Save flushed after process exit**: `OnGameStopped` fires post-exit; most emulators flush on close, but a brief settle delay before push would be safer. Manual "Push local → RomM" is the fallback.
- **RetroArch `sort_savefiles_enable`** (per-core subfolder) isn't resolved (we can't map Playnite→core name); document "use a Custom save dir override" for those users.
- **Clock skew**: negotiate compares the local file mtime (UTC) to the server timestamp; large skew can mis-order. Document NTP.
- **N64 `.mpk` size convention** varies by standalone emulator (single 0x8000 vs full 0x20000); the round-trip guard protects the canonical blob, but loading on a specific standalone is a manual check.
- **PSX `.vmp`/`.gme` write**: intentionally unsupported (signed header) → sync as-is + warn.

## Golden test (correctness-critical, do first)
1. Upload a known save via the plugin; GET the `SaveSchema`; assert `content_hash` == client `SaveHashing.Md5Hex(bytes)`.
2. Repeat with a multi-entry zip fixture (covers the composite hash path).
If these don't match, the negotiate diff is meaningless — stop and fix hashing before anything else.

## Converter round-trip unit tests (pure logic — no Playnite needed)
- N64: native (.eep/.sra/.fla/.mpk fixtures) → ToCanonical → FromCanonical(target≠srm) → bytes equal originals; RetroArch .srm (296960B) → passthrough.
- PSX: raw 128K identity; .vmp (131200B) → strip → 131072B; oversize → null.
- GBA/SNES: identity rename; GBA odd-size warning fires.
- RetroArch cfg parse: `savefile_directory`, `*_in_content_dir`, `sort_*_by_content`, `:`-relative paths.

## Manual end-to-end matrix (≥3 emulators × ≥3 platforms)
| Scenario | Expect |
|---|---|
| RetroArch GBA: play → stop | `.srm` uploaded, hash matches, playtime recorded |
| Standalone mGBA GBA: install (seed) | RomM `.srm` → local `.sav` written |
| RetroArch ↔ mGBA same game | Save round-trips and loads in both |
| SNES (RetroArch) | `.srm` identity sync |
| PSX (Beetle ↔ DuckStation) | 128K card round-trips |
| N64 (RetroArch ↔ standalone) | `.srm` ↔ `.eep`/`.sra`/`.fla`/`.mpk`, loads both sides |
| Edit save on two devices | conflict → dialog (Ask) / policy resolves, backup exists, no data loss |
| Unchanged save, re-sync | `no_op`, no spurious upload |
| Savestate (RetroArch) | state + `.png` thumbnail upload/download, never converted |
| Cancel/offline mid-sync | local save intact, partial files cleaned |
| Token without devices.* scope | clear settings error, no crash |
