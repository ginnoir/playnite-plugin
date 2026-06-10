# Save-Sync Implementation Progress

Working through `.claude/plans/save-sync.plan.md` task briefs in order.
Repo: fork `ginnoir/playnite-plugin`, branch `feat/save-sync`, upstream `rommapp/playnite-plugin`.

---

## ▶ RESUME HERE (next session) — 2026-06-09 night (server on 4.9.0-beta.2, contract closed, slot-lane fix in)

**State:** `roms.ginnoir.com` UPGRADED to **RomM 4.9.0-beta.2** (GitOps: ginnoir/homelabstack commit
`225078af` pins the image tag; Portainer auto-redeployed; alembic migrations clean; server backfilled
all 27 NULL content_hashes at startup). Build GREEN, 44/44 tests, **golden hash RAW+ZIP both MATCH**.
**CONTRACT.md §9: every item verified live (round 2) — see the round-2 block.**

**❗ Round-2 discovery + fix (this session):** 4.9's shipped negotiate only pairs NAMED slots
(null-slot rows are archival-only) and returns ops for the user's whole slotted library. Plugin
reworked: `SyncSlots.Live = "default"` lane everywhere, per-game `(rom_id, slot)` op filter in
`SyncGame`, ForcePush/ForcePull/KeepLocal on the live slot, KeepBoth archives the losing local copy
to a null-slot row named `<base> [conflict <utc>].<ext>`. End-to-end API flow verified live by
`docs/save-sync/tools/slot-lane-test.ps1` (no_op → download → 409 → overwrite → no_op → complete
with playtime ingest `created`). New tools: `verify-contract.ps1`, `probe-negotiate.ps1`,
`slot-lane-test.ps1` (all read the plugin config; all clean up after themselves).

**Live QA done this session (laptop TELLUS, log = `%LOCALAPPDATA%\Playnite\extensions.log`):**
- ✅ device registration (settings "Test connection" → TELLUS on server)
- ✅ cross-device seed-pull: steamdeck `.srm` → laptop standalone mGBA (Radical Red)
- ✅ stop-push: changed save → new `default`-slot row, origin=TELLUS, server hash == local md5
- ✅ `no_op` on unchanged; sessions COMPLETED with playtime ingest
- ✅ conflict → Keep Local resolves correctly (dialog now themed)
- ✅ N64 converter split/join byte-perfect on REAL deck saves (`n64-split-test.ps1`)
- Bugs fixed live (committed): 4.9 named-slot redesign; import NRE on 4.9 slim list
  (`with_files=true` + null-safe); conflict dialog black-on-dark theming; sync-outcome logging.
- Documented gaps (QA.md): portable/scoop emulators need a Custom save-dir override; N64 standalones
  (RMG/mupen64plus) name saves by GoodName+CRC not file basename (RetroArch is the supported N64 path).

**Branch pushed to fork:** `origin/feat/save-sync` @ `4a27880` (2026-06-09 night).

**❗ Addendum (2026-06-10) — RetroArch per-core sort folders + `Scoop` save-location value (built GREEN,
55/55 tests pass via `dotnet test`; live verify pending):** Scoop's `retroarch.cfg` ships `sort_savefiles_enable`/`sort_savestates_enable = "true"`, which
nests saves a level deeper by core display name (`saves\mGBA\<rom>.srm`). The locator previously punted
on this (looked one folder too shallow → reads found nothing, pulls wrote to the wrong folder). Now
`SaveLocator.ResolveRetroArchDir` honors `sort_*_enable`: auto-detects the existing `saves\<core>\`
subfolder, else derives the core name from the mapped `<core>_libretro` arg → `info\<core>_libretro.info`
`corename`. New enum value `SaveLocatorStrategy.Scoop` (inserted before `Custom`; no users yet so order
is free) routes through the same sort-aware RetroArch path. `InferTargetNativeExt` now treats
`Scoop`/`Auto`+RetroArch as `.srm`. New helpers `DetectCoreSubfolder`/`ExtractLibretroCoreToken` are
`internal` + unit-tested in `RomM.Tests/SaveLocatorRetroArchSortTests.cs`. **Verify live on this Scoop box
+ build in VS/CI** (no toolchain here). Files: `Settings/EmulatorMapping.cs`, `SaveSync/SaveLocator.cs`,
`SaveSync/SaveSyncController.cs`, `RomM.Tests/SaveLocatorRetroArchSortTests.cs`, `docs/save-sync/QA.md`.

**Do this, in order (remaining):**
1. ~~Server upgrade~~ ✓ ~~Golden hash~~ ✓ ~~CONTRACT §9~~ ✓ ~~core QA loop~~ ✓ ~~push to fork~~ ✓
2. **Optional extra QA** (not blocking): savestates+screenshot, no_op/offline, more platforms.
3. **Upstream PR (HELD — public, ASK USER FIRST):**
   `gh pr create --repo rommapp/playnite-plugin --head ginnoir:feat/save-sync --title "feat: save & state sync with RomM" --draft`
   PR body must note save sync requires **RomM >= 4.9** (4.8.x: no /api/sync/*, NULL raw hashes).
4. Housekeeping when 4.9.0 stable ships: revert homelabstack pin (`rommapp/romm:4.9.0-beta.2` →
   `:4`) — commit `225078af` on ginnoir/homelabstack.

**Authoring-env constraints to remember:** GateGuard fact-forcing hook gates new-file Write + Bash + .cs/.csproj/.yaml/.md-new edits (present 4 facts, retry; edits to existing .md NOT gated). No SDK/MSBuild/VS here.

**Key design facts (don't re-derive):** RomM owns conflict resolution (`/api/sync/negotiate` →
`compare_save_state`); plugin only reports state + executes ops. content_hash = MD5 (raw; zip = sorted
per-entry `name:md5` joined by \n then md5). Register device `sync_mode="api"`, fingerprint = mac+hostname+platform.
Canonical save = `<romBase>.<canonicalExt>` (srm for SRAM systems), `slot=null`, `emulator` omitted (one shared save).
N64 .srm layout (verified libretro): eep@0x0/0x800, mpk@0x800/0x20000, sra@0x20800/0x8000, fla@0x28800/0x20000 = 0x48800.

**File inventory (all under `SaveSync/` unless noted):** SaveSyncClient, DeviceIdentity, SaveLocator,
SaveHashing, PlatformSaveProfiles, LocalSave, SafeFile, IConflictResolver, DialogConflictResolver,
ConflictResolutionView.xaml(.cs), SaveSyncController, Converters/{ISaveConverter, IdentitySaveConverter,
PsxSaveConverter, N64SaveConverter, ConverterRegistry}; Models/RomM/Sync/SyncModels.cs; edits to RomM.cs,
RomM.csproj, Settings/{Settings.cs, EmulatorMapping.cs, SettingsView.xaml(.cs)}, extension.yaml (0.7.0).
Docs: docs/save-sync/{CONTRACT, QA, README, PROGRESS}.md. Plan: .claude/plans/save-sync.plan.md (gitignored from commit).

---

## Confirmed scope decisions
- v1 = battery saves + savestates + screenshots (everything).
- Converters in v1: identity/rename, GBA RTC, PSX memcard, **N64 split/join** (with round-trip safety net).
- Conflict default policy = **Ask** (PreferLocal/PreferRemote/KeepBoth selectable).
- Register as RomM device with `sync_mode = "api"`. Conflict math is server-side (`compare_save_state`); plugin only reports state + executes ops.
- content_hash = **MD5** (raw bytes; zip = composite of sorted per-entry md5 lines). States have no content_hash.
- Canonical battery save = ROM basename + canonical ext per platform, `slot=null`, `emulator` omitted (one shared logical save).

## Task status
- [x] **Task 1 — Spike: pin contract.** Done from source → `docs/save-sync/CONTRACT.md`. Live re-verification deferred to pre-ship (§9 ⚠️LIVE items).
- [x] **Task 2 — SaveSyncClient.** Done. `Models/RomM/Sync/SyncModels.cs` (DTOs) + `SaveSync/SaveSyncClient.cs` (typed `ApiResult<T>`, 409/403 as flags, multipart upload, states via download_path). csproj is SDK-style → .cs auto-included.
- [x] **Task 3 — Device identity & sync settings.** Done. `Settings/Settings.cs` (sync fields, `ConflictPolicy` enum default Ask, load wiring, `Save()`), `Settings/EmulatorMapping.cs` (`SyncSaves`, `SaveStrategy`/`SaveLocatorStrategy` enum, `SaveDirOverride`+resolved), `SaveSync/DeviceIdentity.cs` (register/persist device_id, mac+hostname fingerprint, 403-scope error). Settings UI wiring deferred to Task 10.
- [x] **Task 4 — SaveLocator.** Done. `SaveSync/SaveLocator.cs`: resolves SaveDir/StateDir/ContentDir/RomBaseName per strategy (RetroArch cfg parse w/ content-dir + by-content-sort fallback; NextToRom; EmulatorSaveFolder; Custom; Auto infer). `EnumerateByExtensions` + `EnumerateRetroArchStates`. core-name sort nesting documented as needing Custom override.
- [x] **Task 5 — Save model + MD5 hashing + format registry.** Done. `SaveSync/SaveHashing.cs` (MD5 raw + zip-composite matching server), `SaveSync/PlatformSaveProfiles.cs` (keyed on Playnite SpecificationId → canonical ext `srm`, scan exts, ConverterFamily), `SaveSync/LocalSave.cs` (path/size/UTC mtime/hash/slot/emulator). Golden hash test deferred to Task 12.
- [x] **Task 6 — Converter framework + converters.** Done. `SaveSync/Converters/`: `ISaveConverter` (SaveFile{Name,Bytes}, ConversionContext{BaseName,CanonicalExtension,TargetNativeExtension,Logger}; ToCanonical(IList<SaveFile>)→SaveFile, FromCanonical(SaveFile)→IList<SaveFile>, null = "can't safely convert, sync as-is"). Converters: Identity/`raw` + `GbaSaveConverter` (rename; RTC kept inline, no surgery, warn on odd size), `PsxSaveConverter` (raw 128K identity; strip .vmp 128B/.gme 3904B header; refuses to write .vmp/.gme), `N64SaveConverter` (**verified libretro layout**: eep@0x0/0x800, mpk@0x800/0x20000, sra@0x20800/0x8000, fla@0x28800/0x20000, total 0x48800=296960; split/join with round-trip self-check). `ConverterRegistry.For(family)` → converter, default Identity.
- [x] **Task 7 — SaveSyncController engine.** Done. `SaveSync/SafeFile.cs` (atomic write+backup, IsLocked), `SaveSync/IConflictResolver.cs` (ConflictChoice + PolicyConflictResolver; Ask→KeepBoth fallback), `SaveSync/SaveSyncController.cs`: `SyncGame(game,resolver,ct,sessionStartUtc?)` = negotiate→execute(upload/download/conflict/no_op, null-slot only)→complete(+playtime); converts via ConverterRegistry w/ as-is fallback; backups+atomic writes; locked-file skip. `SyncStates(game,ct)` = opaque emulator-tagged states, diff by name+size, upload new + sidecar `.png` screenshot, download missing. Public entrypoints: `SyncGame`, `SyncStates`.
- [ ] **Task 8 — Lifecycle triggers + menu actions.** DONE (RomM.cs hooks + menu + ForcePush/ForcePull; references DialogConflictResolver from Task 9). Was: override OnGameStarting (pull), OnGameStopped (push + playtime via sessionStartUtc), OnGameInstalled (seed pull); GetGameMenuItems: Sync now / Push / Pull / Resolve. Construct one SaveSyncController; pick resolver (Ask→Task9 dialog else PolicyConflictResolver). Track per-game start time for playtime. Gate on Settings.EnableSaveSync + Fullscreen.

### Building-block API surfaces (for the engine)
- `SaveSyncClient(host, logger)`: `RegisterDevice`, `CheckDevicesReadable`, `Negotiate(SyncNegotiatePayload)`, `CompleteSession(id, SyncCompletePayload)`, `GetSaves(romId?,deviceId?,slot?)`, `DownloadSaveContent(saveId,deviceId?,sessionId?,optimistic)`, `UploadSave(romId,bytes,fileName,emulator?,slot?,deviceId?,sessionId?,overwrite,autocleanup,limit,screenshot?,shotName?)→ApiResult<RomMSave>` (`.Conflict`=409,`.Forbidden`=403), `UpdateSave(saveId,...)`, `DeleteSaves(ids)`, `GetStates(romId?)`, `UploadState(...)`, `DownloadByPath(downloadPath)`. All return `ApiResult<T>{Ok,Status,Error,Value,Conflict,Forbidden,Unauthorized,NotFound}`.
- `DeviceIdentity.EnsureRegistered/Register/ReRegister(client,settings,logger)→deviceId|null`.
- `SaveLocator(logger).Resolve(mapping,game)→ResolvedSavePaths{SaveDir,StateDir,ContentDir,RomBaseName}`; `EnumerateByExtensions(dir,base,exts)`, `EnumerateRetroArchStates(dir,base)`.
- `PlatformSaveProfiles.Get(specId)→{CanonicalSaveExtension,SaveExtensions,ConverterFamily}` (key on `mapping.Platform.SpecificationId`).
- `LocalSave.FromFile(path,emulator?,slot?)→{Path,FileName,SizeBytes,UpdatedAtUtc,ContentHash,Slot,Emulator}`.
- `SaveHashing.ComputeContentHash(path|bytes)` (MD5, zip-aware), `Md5Hex(bytes)`.
- RomM rom id: `Game.Version` == `"RomM:{id}"` (parse int after colon). Save-sync sidecars/backups go under `ExtensionsDataPath/{pluginId}/SaveSync/`.
- Engine must mirror `Downloads/DownloadQueueController.cs` (semaphore queue, per-item CTS, VM-bound sidebar, retry-delete cleanup). Settings flags: EnableSaveSync/EnableStateSync/SyncScreenshots/SyncOnGameStart/SyncOnGameStop/ReconcileOnStartup/SaveConflictPolicy/AutoCleanupSlots/AutoCleanupLimit; per-mapping SyncSaves/SaveStrategy/SaveDirOverrideResolved.
- [x] **Task 9 — Conflict resolution UX.** Done. `SaveSync/ConflictResolutionView.xaml`(+`.cs`) modal (Keep Local/RomM/Both/Skip, shows size+local-time), `SaveSync/DialogConflictResolver.cs` (UI-dispatcher marshalling, KeepBoth fallback), csproj `<Page>` registered. **Compile graph now closed** (DialogConflictResolver exists).
- [ ] **Task 10 — Settings UI.** DONE (XAML enum providers + per-mapping columns + Save-Sync section; Click_TestSyncConnection registers device & checks scopes). Was: Extend `Settings/SettingsView.xaml` with a Save-Sync section (global toggles, conflict policy combo, device name, Test connection / Re-register button) + per-mapping SyncSaves/SaveStrategy/SaveDirOverride. Wire test/re-register in `SettingsView.xaml.cs` using `SaveSyncClient.CheckDevicesReadable` + `DeviceIdentity.ReRegister`.
- [x] Task 11 — Edge-case hardening (device-404 re-register added; rest baked into 6-9; gaps in QA.md)
- [~] Task 12 — Tests: plan + matrix in QA.md; runnable tests BLOCKED (no build toolchain) -> RomM.Tests in user env
- [~] Task 13 — Docs + version bump + commit/push to fork; public upstream PR held for confirmation

## Build/runtime facts to respect
- C# / .NET Framework 4.6.2 / Playnite SDK 6.16 / Newtonsoft.Json 10 / protobuf-net 2 / SharpCompress 0.36.
- Reuse `HttpClientSingleton` (Bearer rmm_ token or Basic). Mirror `DownloadQueueController` for async queue.
- `Downloads/Converters/` folder already declared in csproj; new code lives under `SaveSync/`.
- GateGuard fact-forcing hook is active: **new-file Write and Bash** need a 4-point fact preamble, then retry. Edits to existing files are NOT gated.
- **No local build toolchain** (no .NET SDK, no SDK-style MSBuild, no VS install found). Compilation/green-check must happen in the user's VS/CI; code is written idiomatically and build-verified later (Task 12).
