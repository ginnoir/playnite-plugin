# RomM Save Sync (Playnite plugin)

Bidirectional sync of **battery saves, savestates, and savestate screenshots** between your local
emulators and a RomM server. RomM owns the conflict logic (device-aware `/api/sync/negotiate`); the
plugin reports local state, executes the server's plan, and converts between emulator save formats.

## Setup
1. **API token scopes** — your RomM token must include `assets.read`, `assets.write`,
   `devices.read`, `devices.write`. (Username/password auth also works.)
2. In the plugin settings → **Save Sync**:
   - Enable *Sync battery saves* and/or *Sync savestates*.
   - Click **Test connection / Register device** — this validates scopes and registers this PC as a
     RomM device (idempotent; stored as a device id).
   - Choose an **On conflict** policy (default **Ask**).
3. For each emulator mapping, tick **Sync saves** and pick a **Save location** strategy:
   - **Auto** — RetroArch is detected automatically; everything else looks next to the ROM.
   - **RetroArch** — parses `retroarch.cfg` (`savefile_directory`, `savestate_directory`, content-dir
     and by-content sort). If you use `sort_savefiles_enable` (per-core subfolders), set a **Custom**
     override instead.
   - **NextToRom**, **EmulatorSaveFolder**, **Custom** (with a *Save dir override* path).

## How it syncs
- **Before launch** (optional): pulls the latest save so you always play the newest.
- **After close**: pushes the save you just made and records playtime.
- **On install**: seeds the freshly-installed game with its RomM save.
- **Manually**: right-click a game → *RomM Save Sync* → **Sync saves now / Push local → RomM / Pull RomM → local**.

Every overwrite is backed up first (`…/ExtensionsData/<plugin>/SaveSync/backups/<romId>/`), and writes
are atomic, so an interrupted sync never corrupts a save.

## Format conversion (the `.sav` ↔ `.srm` problem)
Saves are standardised to a canonical format on RomM (usually `.srm`) and converted back to whatever
your emulator expects:
- **GB / GBC / GBA / SNES / NES / Genesis** — raw SRAM; `.sav` ↔ `.srm` is a safe rename. (GBA RTC
  data stays inline; we never perform RTC byte-surgery.)
- **PlayStation** — 128 KB memory cards; `.srm`/`.mcd`/`.mcr`/`.mc` interchange. `.vmp`/`.gme` are read
  (header stripped) but not written back (they need a signature we can't reproduce).
- **Nintendo 64** — RetroArch's single concatenated `.srm` ↔ standalone `.eep`/`.sra`/`.fla`/`.mpk`,
  using the verified libretro layout, with a round-trip self-check.
- **Savestates are never converted** — they're emulator- and version-specific, so they sync as opaque
  blobs tagged by emulator. A savestate made in one emulator won't load in another; that's expected.

If a save can't be safely converted, it's synced **as-is** (tagged by emulator) with a warning rather
than risk corruption.

## Caveats
- Keep clocks roughly correct (NTP): sync ordering uses file modified-times.
- N64 standalone `.mpk` conventions vary; verify loading on your specific emulator.
- See `CONTRACT.md` (server contract), `QA.md` (edge cases + test matrix), `PROGRESS.md` (build status).
