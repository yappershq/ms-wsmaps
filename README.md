# WSMaps

Workshop map manager for CS2 servers running [ModSharp](https://github.com/Kxnrl/modsharp-public). Handles downloading, name resolution, and mapgroup setup so you don't have to deal with it manually.

## What it does

- Downloads all workshop maps on server startup by cycling through them
- Resolves BSP names automatically and saves them to `maplist.json`
- Generates `gamemodes_server.txt` and sets the `workshop` mapgroup
- Optionally switches to a default or random map after everything is ready
- Can reload the current map on a timer when the server is empty (fixes CS2 movement desync on long-running servers)

## Setup

Drop the module into your ModSharp installation. Add your workshop IDs to `configs/wsmaps/maplist.json`:

```json
[
  { "WorkshopId": 3431383387 },
  { "WorkshopId": 3201173924 },
  { "WorkshopId": 3310414870 }
]
```

That's it. On first boot the module will cycle through each map, download it, resolve the BSP name, and save it back. Subsequent startups only download new/missing maps.

## Config

Optional. Create `configs/wsmaps/config.json` if you want any of these:

```json
{
  "DefaultMap": "ttt_golf",
  "RandomMap": true,
  "EmptyMapSwitcher": true
}
```

| Key | Type | Description |
|-----|------|-------------|
| `DefaultMap` | string | Switch to this map after startup init completes |
| `RandomMap` | bool | Pick a random workshop map instead (overrides `DefaultMap`) |
| `EmptyMapSwitcher` | bool | Reload current map every 15 min when server is empty |

All fields are optional. If the file doesn't exist, everything works like before.

`RandomMap` picks from maps that already have their BSP name resolved. On a fresh install with no resolved names it silently skips — names get resolved during the first download cycle and random works on the next startup.

`EmptyMapSwitcher` is meant for 24/7 servers with `sv_hibernate_when_empty 0`. CS2 has a known issue where movement desyncs after the server sits idle for a while, reloading the map fixes it.

## Commands

| Command | Description |
|---------|-------------|
| `ms_wsmaps_download` | Force re-download all workshop maps |
| `ms_wsmaps_gamemodes` | Regenerate `gamemodes_server.txt` |
| `ms_wsmaps_maplist` | Generate `maplist.jsonc` for MapManager |
