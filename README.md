# HeavyPro (HeavyFeel)

Windows desktop app for **Microsoft Flight Simulator 2024**.

- Connects to MSFS 2024 with the official SimConnect SDK
- Detects the loaded aircraft (Fenix A320 first-class support, plus PMDG 777, GA, regional, wide-body, heavy)
- Reads live flight data every frame
- With **Master Enable** on, writes **AXIS only** while the stick is actually held:
  - `AXIS_ELEVATOR_SET` / `AXIS_AILERONS_SET` / `AXIS_RUDDER_SET`
  - Optional yoke position write
- **Body-rate writes** (`ROTATION VELOCITY BODY X/Y/Z`) are registered but **disabled** by design — they fight the sim flight model and Fenix FBW
- Pauses all writes when Master is off or autopilot is on
- Header feel presets: **Normal**, **Medium**, **Realistic** (plus Custom when you move sliders)
- Live mass + aircraft-class scaling so a heavy jet feels heavier than a Cessna

Fenix uses its own fly-by-wire. AXIS events may be weakly coupled or ignored; the app still only writes while the stick is deflected so it does not force a zero-stick.

## What you need

1. Windows 11 (64-bit)
2. Microsoft Flight Simulator 2024
3. Fenix A320 (or any aircraft — detection works generally)
4. The **MSFS 2024 SDK** (free, installed from inside the sim)
5. **Visual Studio 2022 Community** with the **.NET desktop development** workload
6. **.NET 8 SDK** (Visual Studio usually installs this)

## Install the MSFS 2024 SDK

1. Start MSFS 2024.
2. On the main menu open **Options**.
3. Enable **Developer Mode**.
4. From the Developer menu install / open the SDK.
5. Confirm this file exists (folder name can vary slightly):

   `C:\MSFS 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll`

The setup script searches the usual places.

## Build and run (do this in order)

1. Copy the whole folder to your PC, for example `C:\Work\HeavyFeel`.
2. Double-click `Setup-SimConnect.bat` next to `HeavyFeel.sln`.
   - There is also `scripts\Setup-SimConnect.ps1` if you prefer PowerShell.
   - If Windows hides extensions, the file may appear only as `Setup-SimConnect`.
3. Double-click `HeavyFeel.sln`.
4. In Visual Studio choose **Debug** and **Any CPU** (or x64).
5. Right-click **HeavyFeel.App** → **Set as Startup Project**.
6. Press **F5** (or the green Play button).
7. Start MSFS 2024 if it is not already running, load an aircraft (Fenix A320 recommended), and click **Connect** if it did not auto-connect.

If the setup script cannot find the SDK, copy these two files into the `lib` folder yourself, then rebuild:

- `Microsoft.FlightSimulator.SimConnect.dll`
- `SimConnect.dll`

## What a good run looks like

- Connection label shows **LIVE** or **CONNECTED**
- Aircraft title shows the loaded aircraft name
- Profile detection shows **Fenix A320** (or the matching class)
- IAS, pitch, weight, N1, and yoke values change when you move the aircraft
- Log lines appear at the bottom
- Log files: `%AppData%\HeavyFeel\logs\`
- Settings: `%AppData%\HeavyFeel\settings.json`

## If it does not connect

- MSFS must be running first (or start it and wait — Auto-connect retries every 3 seconds).
- Rebuild after the `lib` DLLs are in place. If you built first, the app stays in offline mode until you rebuild.
- Run 64-bit. Any CPU on a 64-bit Windows machine is fine.
- Windows may block an unsigned exe. Allow it if asked.

## If some numbers stay at zero

Write down which field stays at zero. That SimVar may use a different unit name on the aircraft, or the aircraft may not publish it. Phase 1 still runs; individual fields can be fixed later.

## Project layout

```
HeavyFeel/
  HeavyFeel.sln
  lib/                  SimConnect DLLs go here (gitignored)
  scripts/              Setup-SimConnect.ps1
  docs/                 Capabilities, feasibility, changelog
  src/HeavyFeel.App     WPF window + view-model
  src/HeavyFeel.Core    Settings, aircraft catalog, inertia engine
  src/HeavyFeel.SimConnect
  src/HeavyFeel.Logging
  src/HeavyFeel.Core.Tests
```

## Current behaviour (accurate)

| Feature | Status |
|---------|--------|
| Connect + read telemetry | Working |
| Aircraft identity + class detection | Working |
| Live weight / MOI scaling | Working |
| Feel presets + per-axis sliders | Working |
| AXIS writes while stick held | Working (Master Enable) |
| Body-rate writes | **Disabled** (by design) |
| View-cue (camera offset) | Optional, off by default |
| Event masking / input interception | Not implemented (too risky) |

## Next ideas (not built)

- Per-aircraft write-path validation on a live Fenix / PMDG session
- Optional gentle rate assist only on aircraft that accept it
- More profiles (e.g. specific 737, A220, turboprops)
- Optional WASM companion for deeper Fenix integration (out of current scope)

## License

MIT — see [LICENSE](LICENSE).
