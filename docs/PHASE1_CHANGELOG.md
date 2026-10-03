# Phase 1 changelog

Created the HeavyFeel prototype. Reads live MSFS state and, with Master Enable on, writes inertia to the user aircraft.

## Added

- Solution and four app projects plus unit tests for settings / Fenix detection
- Official MSFS 2024 SimConnect client (compiled only when the SDK DLLs are in `lib`)
- Offline fallback so a missing SDK does not crash the UI
- Aircraft identity: TITLE, ATC MODEL, ATC TYPE, ATC ID, CATEGORY
- Live flight data listed in the feasibility report
- Dark desktop UI with Master Enable, sliders, profile box, telemetry, log
- JSON settings in `%AppData%\HeavyFeel\settings.json`
- Daily logs in `%AppData%\HeavyFeel\logs`
- Setup script to copy SimConnect DLLs from the SDK
- Header feel tabs: Normal, Medium, Realistic — these sliders now feed the write model when Master is on
- Ground/air label uses official MSFS 2024 `IS ON GROUND` plus `PLANE ALT ABOVE GROUND` / `RADIO HEIGHT`, so a stuck `SIM ON GROUND` flag no longer freezes State on GROUND in the air
- Autopilot label reads `AUTOPILOT MASTER` and, in a separate data definition, Fenix FCU locals `L:S_FCU_AP1` / `L:S_FCU_AP2` (Fenix often leaves the stock master at 0)
- Bool flags requested as FLOAT64 `number` to avoid INT32 packing shifting the rest of the packet

## Writes (Master Enable on)

- `SetDataOnSimObject` on `ROTATION VELOCITY BODY X/Y/Z` (radians per second)
- `TransmitClientEvent` for `AXIS_ELEVATOR_SET`, `AXIS_AILERONS_SET`, `AXIS_RUDDER_SET`
- Paused when Master is off or autopilot is on
- Not a stick-delay buffer — first-order lag + rate bleed from live state

## Still not added

- Event masking (would steal hardware axes; too risky without a live Fenix test)
- Editing Fenix or MSFS aircraft files
- Writing weight / MOI / G-force

## Compile note

This environment cannot run Visual Studio or MSFS. After you copy the SDK DLLs into `lib` and build on Windows, `HAS_SIMCONNECT` is defined and the real client is compiled.
