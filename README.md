# HeavyPro (HeavyFeel)

> **Private and confidential.** © 2025-2026 Rotaidem. All rights reserved. This is a private program: no one may use, copy, modify or share this code without Rotaidem's written permission. See [LICENSE](LICENSE).

Windows desktop app for **Microsoft Flight Simulator 2024** that makes your stick feel like it is moving real control surfaces on a heavy aircraft: the sim follows your stick with weight, momentum and a damped return instead of snapping instantly.

## Install (players)

1. Download [HeavyProSetup.exe](https://github.com/tornqvisteliaz-cyber/heavypro/releases/download/beta/HeavyProSetup.exe) (newest beta; also on [rotaidem.com/downloads](https://rotaidem.com/downloads)), run it, enter your licence key and press Install. Setup also adds the HeavyPro panel to your MSFS 2024 Community folder (a white R in the in-game toolbar) and removes the toolbar package older versions installed there.
   Windows may say *"Windows protected your PC"* because the installer is not code-signed yet: click **More info → Run anyway**.
2. Start HeavyPro. Under **Stick setup** press **Detect** next to Pitch and pull the stick fully back, then **Detect** next to Roll and roll fully right. Do the same for your rudder pedals if you want HeavyPro to shape them.
3. In MSFS **Controls**, clear **Elevator Axis** and **Aileron Axis** (and **Rudder Axis** if HeavyPro has your pedals) for those devices. HeavyPro now feeds them itself.
4. Load a flight and tick **MASTER ENABLE**.

The **REAL1** and **Direct** routes work with every aircraft we tested, including the PMDG 777, the BlueBird 757 and other add-ons that run their own control model. The **MSFS bindings** route keeps the stick bound in MSFS and HeavyPro shapes it from there.

**Fix MSFS bindings for me** (Setup, REAL1 and Direct only) clears only the stick axes in your MSFS 2024 profiles and keeps a backup; **Restore MSFS bindings** puts that backup back. The MSFS bindings route needs those bindings, so Fix is not offered there.

If you leave an axis bound in MSFS while on the Direct route, MSFS and HeavyPro both drive the yoke and it twitches.

The header badge tells you what is happening:

| Badge | Meaning |
|-------|---------|
| `SHAPING` (green) | Working. The gold bar in **Stick → Aircraft** is what the aircraft gets, the white tick is your stick. |
| `SET UP STICK` | Direct route, but no stick picked yet. Use **Detect** under Stick setup. |
| `MOVE STICK` | Connected, waiting for the stick. |
| `PASS-THROUGH` | Master Enable is off. Your stick reaches the sim untouched. |
| `AP ON · PAUSED` | Autopilot engaged. HeavyPro hands the stick back until you disconnect it. |
| `OFFLINE` | MSFS is not running yet. HeavyPro connects on its own. |

## Beta testers and creators

HeavyPro is in beta. On first start it asks you to accept the [Terms of Service](TERMS.md) (again whenever they change; **Terms** at the bottom of the app shows them). Found a problem? Press **Copy beta report** at the bottom of the app and paste it into your report to Eliaz@rotaidem.com: it holds your version, aircraft, route and recent log, without your Windows user name or licence key.

Creators may stream, record and publish videos of HeavyPro, monetised ones included (Terms section 3). Please credit "HeavyPro by Rotaidem" and keep your licence key off screen.

HeavyPro is © 2026 Rotaidem, all rights reserved. The source code is private: see [LICENSE](LICENSE).

## What's new in 0.4.0

Tested live in MSFS 2024 with the PMDG 777-300ER:

- **Fixed: PMDG yoke "does not move", twitches, or sticks at full deflection.** PMDG runs its own control model from a SimConnect group further down the event chain. HeavyPro used to take the stick's events and re-send them below that group, so PMDG never saw them: the sim applied each one for a frame and PMDG put its own value back (measured: ~90 direction changes in 6 s), and PMDG kept the last real stick position (the aileron stayed at full right after the stick was centred). The new **Direct** route reads the stick from Windows and sends where hardware enters. A test client sending that way to the 777 gave zero direction changes; the full Direct route still needs a flight with a real stick to confirm.
- **Fixed:** turning shaping on, or the autopilot off, with the stick deflected no longer jumps the yoke to centre first.
- **Fixed:** heavy jets sit 15–20 ft above the ground on their gear, so the 777 on the runway was treated as airborne ("Approach"). The sim's own on-ground flag is now trusted near the ground.
- **Realistic feel per control system** (see below).
- Full travel is capped at 2.5 s and the deadzone at 10 %: past that a heavy jet feels dead rather than heavy. Settings from 0.3 are reset once to Auto Detect profile and a 3 % deadzone.

## How the feel is modelled

A desktop stick has a fixed spring, so HeavyPro renders weight as how quickly the aircraft's controls follow your stick. On top of the weight and inertia model it applies the real control system of the aircraft you fly:

| Control system | Aircraft | Real-world feel | In HeavyPro |
|---|---|---|---|
| Artificial feel | Boeing yokes (737, 747, 767, 777, 787), A300/A310, E-Jets | A feel unit fed by airspeed (737: tail pitot and stabiliser position) makes the column heavier as speed builds; hydraulic surfaces have a top speed | Stiffens with dynamic pressure: light while taxiing, heavy at cruise. Never faster than 0.4 s to full travel |
| FBW sidestick | Airbus A320 family, A330, A340, A350, A380, A220 | Same spring at every speed; the stick commands g and roll rate, so the weight is in the aircraft's response | Light shaping only (at most 0.6 s), no airspeed scaling, which avoids pilot-induced oscillation |
| Mechanical | Light aircraft, most turboprops and bizjets | Stick force grows with dynamic pressure (V²) | Stiffens with dynamic pressure on a light-aircraft scale |

**Jet rudder limiter:** like the 747's rudder ratio changer (about 30° of rudder at low speed down to about 5° near 500 kt), rudder throw shrinks from full at 160 kt to a fifth by 320 kt. It is skipped for PMDG, Fenix, iniBuilds and FlyByWire aircraft, which model it themselves, and can be turned off.

## Licensing

HeavyPro needs a licence key (`HPRO-XXXX-XXXX-XXXX`), issued with the Rotaidem licence generator.

- **Installer:** asks for the key and activates it online before installing. A key already saved on the PC is filled in.
- **App:** with no saved key it opens an **Activate HeavyPro** window (this also covers the installer running as a different Windows user). A saved, still valid signed answer starts at once and is re-checked in the background. If the server says the key is revoked or removed from this PC, HeavyPro closes and asks for a key next time. Offline it keeps working until the signed answer's lease ends (14 days, `LICENSE_LEASE_DAYS` on the website), and warns only in the last 3.
- **Signed answers:** HeavyPro trusts a "valid" only if it carries a token signed by rotaidem.com's private key, naming this PC, this key and the random nonce HeavyPro just sent. A fake licence server, an edited `license.json`, a licence copied to another PC, a replayed answer and a clock turned back are all refused. The public key is built in: the build fetches it from `https://rotaidem.com/api/licenses/jwks.json` (or uses `packaging/licence-public-key.json` if committed). Publishing builds stop if it is missing, so **deploy the website before merging licensing changes**.
- **Server:** `https://rotaidem.com` (the Rotaidem website on STRATO), set in [src/Shared/HeavyProLicense.cs](src/Shared/HeavyProLicense.cs) through `LicenseClient.DefaultBaseUrl`.
- **Debug builds skip the check** so Visual Studio F5 works without a key. The installer always ships a Release build.
- `src/Rotaidem.Licensing` is copied from `rsg.com` (`licensing/`, branch `claude/lucid-tesla-liazv2`, commit `40221c1`, where its tests live), with its own `Directory.Build.props` so it doesn't inherit this repo's x64 settings. It has no package dependencies, so the setup compiles its source in.
- `HeavyProSetup.exe` targets .NET Framework 4.8, which ships with Windows 10 and 11, so it needs no bundled runtime and is about 1.5 MB (it was ~69 MB as a self-contained .NET 8 exe).

Keys are saved in `%LOCALAPPDATA%\Rotaidem\heavypro\license.json`. To move a licence to another PC, remove the old computer under Admin → Licences on the website.

## 0.3.0

Tested against a live MSFS 2024 (1.9.18) session:

- **Fixed: shaping never ran.** Two SimVars (`IS ON GROUND`, and `DYNAMIC PRESSURE` with an invalid unit) were rejected by MSFS 2024. SimConnect drops a rejected field, which shifted every later value. "Autopilot" ended up reading height above ground, so HeavyPro thought AP was on and paused itself. Weight, yoke and surface readouts were wrong too. HeavyPro now names any rejected SimVar in the log and refuses to use a misaligned packet.
- **Fixed: second launch crashed** on the locked log file. HeavyPro is now single-instance; launching it again brings the open window forward.
- **Fixed: blank window** while MSFS holds the GPU. The panel now draws on the CPU (`HeavyPro.exe --gpu-render` opts back in).
- **Fixed:** after the stick is released the exact stick value is sent again, so the yoke no longer rests a few counts off centre.
- **Fixed:** the Curve / Stick release / Aircraft drop-downs rendered white-on-white.
- **New:** header status badge and live **Stick → Aircraft** meters.
- Crashes are written to `%AppData%\HeavyFeel\logs\crash.log`.

Measured on the L-39 with the Realistic preset: a 0 → 73 % pitch step reaches the sim over ~1.0 s instead of 0.2 s, and returns to exactly centre on release. With Master off the same step passes straight through.

- Connects to MSFS 2024 with the official SimConnect SDK
- Detects the loaded aircraft and selects a tunable input-dynamics preset (Fenix A320, PMDG 737/777, ASOBO 787, generic GA/airliner)
- Reads live flight data every frame
- Captures the physical stick and rudder through a **maskable** SimConnect notification group on `AXIS_ELEVATOR_SET` / `AXIS_AILERONS_SET` / `AXIS_RUDDER_SET`
- With **Master Enable** on, shapes the captured input and writes **AXIS events only** (never yoke/rudder simvars) while the stick is deflected or settling
- With Master off, autopilot on, a flight loading, or the app not stepping, the physical value is passed straight through unchanged
- Only axes actually captured are written. An axis bound some other way is left alone (status shows `UNAVAILABLE`/`INITIALIZING` until a stick event arrives)
- Separates raw, curved/filtered, and final inputs in the debug panel
- Uses delta-time-based response, acceleration/deceleration limits, damping, airspeed scaling, and stick-release behavior
- Input tuning (curve, expo, deadzone, sensitivity, per-axis rate limits, release mode/delay, airspeed response) is applied live
- Preserves full endpoint authority; dynamics change how quickly the commanded endpoint is reached
- **Body-rate writes** (`ROTATION VELOCITY BODY X/Y/Z`) are registered but **disabled** by design — they fight the sim flight model and Fenix FBW
- Pauses all writes when Master is off or autopilot is on
- Header feel presets: **Normal**, **Medium**, **Realistic** (plus Custom when you move sliders)
- Input-dynamics profiles are starting presets, not manufacturer control-system data, and can be tuned in the UI

Fenix uses its own fly-by-wire. AXIS events may be weakly coupled or ignored; HeavyPro does not change aerodynamic forces or aircraft files.

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
8. In MSFS, bind your stick and pedals to **Elevator Axis**, **Aileron Axis** and **Rudder Axis**. Move the stick once; the debug panel's `Route` line changes to `Active`.

If the setup script cannot find the SDK, copy these two files into the `lib` folder yourself, then rebuild:

- `Microsoft.FlightSimulator.SimConnect.dll`
- `SimConnect.dll`

## What a good run looks like

- Connection label shows **LIVE** or **CONNECTED**
- Aircraft title shows the loaded aircraft name
- Profile detection shows **Fenix A320** (or the matching class)
- IAS, pitch, weight, N1, and yoke values change when you move the aircraft
- Debug panel `PHYSICAL` shows your stick values and `Route` shows `Active` after the first stick movement
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
  docs/                 Capabilities, feasibility, changelog, input dynamics
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
| Aircraft-specific input-dynamics presets | Working; illustrative defaults |
| Per-axis response and rate tuning | Working |
| Raw / filtered / final input debug values | Working |
| AXIS writes while input is held or settling | Working (Master Enable), captured axes only |
| Body-rate writes | **Disabled** (by design) |
| View-cue (camera offset) | Optional, off by default |
| SimConnect axis-event capture and rewrite | Working for axes bound to Elevator/Aileron/Rudder Axis; pass-through when not shaping. Verified in MSFS 2024 with injected AXIS events |
| Unit tests (`dotnet test src/HeavyFeel.Core.Tests`) | Run in CI |

## Next ideas (not built)

- Per-aircraft write-path validation on a live Fenix / PMDG session
- Optional gentle rate assist only on aircraft that accept it
- More profiles (e.g. specific 737, A220, turboprops)
- Optional WASM companion for deeper Fenix integration (out of current scope)

## License

MIT — see [LICENSE](LICENSE).
