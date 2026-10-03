# Phase 1 changelog

Created the HeavyFeel / HeavyPro prototype. Reads live MSFS state and, with Master Enable on, writes **AXIS inertia** to the user aircraft while the stick is held.

## Added

- Solution and four app projects plus unit tests for settings / Fenix detection / inertia engine
- Official MSFS 2024 SimConnect client (compiled only when the SDK DLLs are in `lib`)
- Offline fallback so a missing SDK does not crash the UI
- Aircraft identity: TITLE, ATC MODEL, ATC TYPE, ATC ID, CATEGORY
- Aircraft class catalog (Light GA → Regional → Narrow-body → Wide-body → Heavy wide-body)
- Live flight data listed in the feasibility report and CAPABILITIES.md
- Dark desktop UI with Master Enable, sliders, profile box, telemetry, log
- JSON settings in `%AppData%\HeavyFeel\settings.json`
- Daily logs in `%AppData%\HeavyFeel\logs`
- Setup script to copy SimConnect DLLs from the SDK
- Header feel tabs: Normal, Medium, Realistic — these feed the write model when Master is on
- Ground/air label uses official MSFS 2024 `IS ON GROUND` plus `PLANE ALT ABOVE GROUND` / `RADIO HEIGHT`
- Autopilot label reads `AUTOPILOT MASTER` and, in a separate data definition, Fenix FCU locals `L:S_FCU_AP1` / `L:S_FCU_AP2` (Fenix often leaves the stock master at 0)
- Bool flags requested as FLOAT64 `number` to avoid INT32 packing shifting the rest of the packet
- Live mass + MOI scaling so heavier aircraft get lower command authority / slower slew rates

## Writes (Master Enable on)

- `TransmitClientEvent` for `AXIS_ELEVATOR_SET`, `AXIS_AILERONS_SET`, `AXIS_RUDDER_SET` **only while the stick is deflected**
- Optional yoke position write
- **Body-rate writes (`ROTATION VELOCITY BODY X/Y/Z`) are registered but deliberately disabled** — they fight the sim flight model and Fenix FBW
- Paused when Master is off or autopilot is on
- First-order slew (rate-limited ramp) driven by live weight, class, phase and the feel sliders — not a simple delay buffer

## Still not added

- Event masking (would steal hardware axes; too risky without extensive live testing)
- Editing Fenix or MSFS aircraft files
- Writing weight / MOI / G-force
- Active body-rate overlay

## Compile note

This environment cannot run Visual Studio or MSFS. After you copy the SDK DLLs into `lib` and build on Windows, `HAS_SIMCONNECT` is defined and the real client is compiled.
