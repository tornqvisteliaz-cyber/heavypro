# HeavyPro toolbar (MSFS 2024 project)

An in-game toolbar panel for HeavyPro: Master Enable, feel preset (Normal / Medium / Realistic),
connection status and a live yoke-follow pad (ring = your stick, dot = what HeavyPro sends to the sim).

The panel only talks to the HeavyPro Windows app through `L:HEAVYPRO_*` variables over SimConnect.
No network, no WASM. The app must be running and connected for the buttons to work.

## Build

Either:

1. Run `build.bat` (needs the MSFS 2024 SDK installed; uses `fspackagetool.exe`), or
2. In MSFS 2024 enable Developer Mode → *Tools → Project Editor* → open `HeavyProToolbar.xml` → *Build All*.

The output is `Packages\heavypro-toolbar`. Copy that folder into your Community folder and restart the sim.
A HeavyPro icon appears in the toolbar.

## L:var contract

| Variable | Direction | Meaning |
|----------|-----------|---------|
| `L:HEAVYPRO_ALIVE` | app → panel | Counter, changes ~10×/s while the app is connected |
| `L:HEAVYPRO_MASTER` | app → panel | 1 = Master Enable on |
| `L:HEAVYPRO_FEEL` | app → panel | 1 Normal, 2 Medium, 3 Realistic |
| `L:HEAVYPRO_MODE` | app → panel | 0 pass-through, 1 shaping, 2 no stick input seen |
| `L:HEAVYPRO_RAW_X/Y`, `OUT_X/Y` | app → panel | Stick vs sent position, −1…1 |
| `L:HEAVYPRO_REQ_MASTER`, `REQ_FEEL` | panel → app | Requested state |
| `L:HEAVYPRO_REQ_SEQ` | panel → app | Incremented last; the app applies the request when it changes |
