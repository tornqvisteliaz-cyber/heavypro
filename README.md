# HeavyFeel

Windows desktop app for Microsoft Flight Simulator 2024.

- Connects to MSFS 2024 with SimConnect
- Detects the loaded aircraft (Fenix A320 first)
- Reads live flight data
- With **Master Enable** on, writes inertia to the user aircraft:
  - `ROTATION VELOCITY BODY X/Y/Z`
  - `AXIS_ELEVATOR_SET` / `AXIS_AILERONS_SET` / `AXIS_RUDDER_SET`
- Writes pause when Master is off or the autopilot is on
- Header tabs for feel: Normal, Medium, Realistic

Fenix uses its own fly-by-wire. AXIS events may be ignored there; the rate writes still go out.

## What you need

1. Windows 11
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
5. Confirm this file exists:

`C:\MSFS 2024 SDK\SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll`

The folder name can vary slightly. The setup script searches the usual places.

## Build and run (do this in order)

1. Copy the whole `HeavyFeel` folder to your PC, for example `C:\Work\HeavyFeel`.
2. Double-click `Setup-SimConnect.bat` next to `HeavyFeel.sln`.
   - There is also `scripts\Setup-SimConnect.ps1` if you want the PowerShell version.
   - If Windows hides extensions, the file may appear only as `Setup-SimConnect`.
3. Double-click `HeavyFeel.sln`.
4. At the top of Visual Studio choose **Debug** and **Any CPU**.
5. Press **F5**.
6. Start MSFS 2024 if it is not already running, load the Fenix A320, and click **Connect** if it did not auto-connect.

If the setup script cannot find the SDK, copy these two files into the `lib` folder yourself, then build:

- `Microsoft.FlightSimulator.SimConnect.dll`
- `SimConnect.dll`

## What a good run looks like

- Connection label shows **LIVE** or **CONNECTED**
- Aircraft title shows the Fenix name
- Profile detection shows **Fenix A320**
- IAS, pitch, weight, N1, and yoke values change when you move the aircraft
- Log lines appear at the bottom
- Log files are written to:

`C:\Users\<you>\AppData\Roaming\HeavyFeel\logs\`

Settings are saved to:

`C:\Users\<you>\AppData\Roaming\HeavyFeel\settings.json`

## If it does not connect

- MSFS must be running first (or start it and wait — Auto-connect retries every 3 seconds).
- Rebuild after the `lib` DLLs are in place. If you built first, the app stays in Offline mode until you rebuild.
- Run the same bitness as the sim (64-bit). Any CPU on a 64-bit Windows machine is fine.
- Windows may block an unsigned exe. Allow it if asked.

## If some numbers stay at zero

Write down which field stays at zero. That SimVar may use a different unit name on Fenix, or Fenix may not publish it. Phase 1 will still run; we fix individual fields in Phase 2.

## Project layout

```
HeavyFeel/
  HeavyFeel.sln
  lib/                  SimConnect DLLs go here
  scripts/              Setup-SimConnect.ps1
  src/HeavyFeel.App     Window
  src/HeavyFeel.Core    Settings and data types
  src/HeavyFeel.SimConnect
  src/HeavyFeel.Logging
```

## Next phase (not built yet)

Only after you confirm live Fenix data is showing:

- Measure whether Fenix accepts control events
- Then add one axis of the inertia model
