# Save-Sync Implementation Progress

Working through `.claude/plans/save-sync.plan.md` task briefs in order.
Repo: fork `ginnoir/playnite-plugin`, branch `feat/save-sync`, upstream `rommapp/playnite-plugin`.

---

## ▶ RESUME HERE (next session) — 2026-06-09 evening (tests green, golden hash run, server too old)

**State:** build GREEN, **RomM.Tests suite added — 44/44 passing** (`dotnet test RomM.Tests\RomM.Tests.csproj`),
golden hash test executed against the live server. **Blocking discovery: the user's RomM is 4.8.1, which
has NO `/api/sync/*` endpoints** (negotiate protocol ships in 4.9.0-beta.x) **and a server bug that stores
`content_hash = NULL` for all non-zip saves** (fixed in 4.9.0-beta.2). Full details: CONTRACT.md §9
"Live verification round 1". The zip-composite golden hash MATCHED exactly (our algorithm is correct);
the raw case can't be verified until the server runs 4.9.

**Code added this session (commits `82d8c25` + next):**
- `RomM.Tests/` — xunit net462 suite (hash vectors, zip composite, N64 round-trip, PSX headers, GBA,
  registry). `RomM.csproj`: `InternalsVisibleTo` + `DefaultItemExcludes=RomM.Tests\**` (root-level
  project was globbing test sources into the WPF markup compile).
- `SaveSyncController.SyncGame`: negotiate-404 now disambiguates stale device (body contains "Device")
  from missing endpoint (pre-4.9 server) → clear "requires RomM 4.9 or newer" message.
- `SaveSyncClient.CheckSyncSupported()` (GET /api/sync/sessions probe) + settings "Test connection"
  now warns when the server is pre-4.9.
- `docs/save-sync/tools/golden-hash-test.ps1` — repeatable golden test (uploads raw+zip fixtures to a
  saveless rom, compares against the SHIPPED SaveHashing via reflection, deletes the test saves).

**Do this, in order:**
1. ~~Build~~ ✓ 2. ~~Unit tests~~ ✓ 44/44 3. ~~Golden hash (zip)~~ ✓ MATCH (raw blocked by 4.8.1 bug)
4. **DECISION (user):** upgrade `roms.ginnoir.com` to RomM `4.9.0-beta.2`+ (sync endpoints + hash fix)
   — it's a Docker deploy (Portainer available). Without it, end-to-end sync cannot be tested.
5. After upgrade: re-run `golden-hash-test.ps1` (expect RAW match too), exercise "Test connection"
   (devices.write + sync probe), then work the manual QA matrix in `docs/save-sync/QA.md`.
6. Re-verify remaining CONTRACT.md §9 items (1, 3, 4, 5, 6) against 4.9.x.
7. When green, open the upstream PR (held intentionally):
   `gh pr create --repo rommapp/playnite-plugin --head ginnoir:feat/save-sync --title "feat: save & state sync with RomM" --draft --body-file <notes>`
   Note in the PR that save sync requires RomM >= 4.9 (and that 4.8.x has the content_hash bug worth
   reporting upstream to rommapp/romm if not already fixed on master).

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
