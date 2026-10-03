# HeavyFeel — Technical Feasibility Report
**Target:** Microsoft Flight Simulator 2024  
**Platform:** Windows 11, C# / .NET, SimConnect, desktop GUI  
**First aircraft:** Fenix A320  
**Status:** Research complete. No application code written. Awaiting approval.

Sources used: official MSFS 2024 SDK documentation (SimConnect SDK, Simulation Variables, Key Events, Input Events, managed-code client notes), plus documented developer reports about settable rotation-velocity SimVars and Fenix control architecture. No undocumented APIs are assumed.

---

## 1. Technical feasibility

### 1.1 What is clearly feasible

An out-of-process Windows desktop app can:

- Connect to a running MSFS 2024 session with SimConnect.
- Detect the loaded aircraft (`TITLE`, `ATC MODEL`, `ATC TYPE`, `ATC ID`).
- Subscribe to real-time flight data on the user aircraft at sim-frame or visual-frame rate.
- Display live telemetry and write logs.
- Persist settings (sliders, enable state, selected profile).
- Provide a modern desktop UI.

This first-prototype scope (connect, detect, read, display, log) is **fully supported** by the official SDK. Microsoft documents C# / .NET as a supported client language and ships the SimvarWatcher sample as the reference pattern.

### 1.2 What is only partially feasible

Making an aircraft “feel heavier” **without editing aircraft files** is possible only by influencing values the sim already exposes for write, or by intercepting control events before the sim consumes them.

Documented external influence paths:

| Path | Official support | Effect | Risk |
|---|---|---|---|
| Read SimVars | Full | Telemetry, model inputs | None |
| Write settable control SimVars (`ELEVATOR POSITION`, `AILERON POSITION`, `RUDDER POSITION`, yoke positions) | Documented as settable in control-variable tables | Changes commanded surface / stick position | Fights user hardware and aircraft systems |
| Transmit `AXIS_ELEVATOR_SET`, `AXIS_AILERONS_SET`, `AXIS_RUDDER_SET` | Official Key Events | Same as sending an axis command | Does not work if gyro controls are active; may be ignored by custom FBW |
| Mask those events and re-send filtered values | Official SimConnect notification-group masking | Lets the app reshape raw axis commands using live aircraft state | Hardware routing in MSFS 2024 is not guaranteed to raise the same events; Fenix may not use them |
| Write `ROTATION VELOCITY BODY X/Y/Z` | Documented as settable; used by some custom-physics add-ons | Directly changes body rates for one frame | Sim flight model overwrites next frame; must write continuously; can jitter or fight FBW |
| Write `VELOCITY BODY X/Y/Z` | Historically settable | Linear velocity overlay | Same fight-with-sim problem |
| MSFS 2024 Input Events API (`EnumerateInputEvents`, `SetInputEvent`) | Official | Aircraft-specific cockpit/axis input events (B-events) | Names/hashes are per aircraft and must be discovered at runtime, not assumed |
| Edit `flight_model.cfg` (mass, MOI, elasticity tables) | Official aircraft-author path | True inertia change | Explicitly out of scope unless later approved |

There is **no official SimConnect API** that says “increase aircraft inertia” or “scale pitch damping.” Mass, MOI, lift-curve, and control elasticity live in the aircraft package and are read-only from an external exe.

### 1.3 Verdict

- **Prototype 1 (connect + detect + read + UI + log): YES.**
- **State-based inertia / damping overlay: CONDITIONALLY YES**, as a real-time controller that uses live SimVars, not as a fake input delay.
- **Guaranteed Fenix A320 sidestick-law rewrite: NO, not with public SimConnect alone.** Fenix implements its own fly-by-wire. Sidestick commands load factor and roll rate inside Fenix software. Standard elevator/aileron events may be ignored or only weakly coupled.
- **True flight-model inertia change without touching aircraft files: NO.**

HeavyFeel should be designed as a **layered influence engine** with per-aircraft profiles that enable only methods proven to work on that aircraft.

---

## 2. Architecture

Recommended shape: out-of-process WPF (or WinUI 3) app on .NET 8, using the official managed SimConnect wrapper from the MSFS 2024 SDK.

```
┌─────────────────────────────────────────────────────────────┐
│ HeavyFeel.exe  (.NET 8, WPF)                                │
│                                                             │
│  UI  ── sliders, master enable, profile, telemetry pane     │
│   │                                                         │
│  App Services                                               │
│   ├── SettingsStore (JSON in %AppData%\HeavyFeel)           │
│   ├── Logger                                                │
│   ├── AircraftProfileService                                │
│   └── PhysicsEngine (disabled in prototype 1)               │
│           │                                                 │
│  SimConnectClient                                           │
│   ├── Connection / reconnect                                │
│   ├── Data definitions + frame subscription                 │
│   ├── Aircraft identity watch                               │
│   ├── Event map (later)                                     │
│   └── Optional event mask + filtered retransmit (later)     │
└──────────────────────────┬──────────────────────────────────┘
                           │ named pipe / SimConnect
                           ▼
                 MSFS 2024 SimConnect server
                           │
                           ▼
                 User aircraft (Fenix A320 first)
```

### Design rules

1. The physics engine only runs when Master Enable is on **and** a profile allows the active method.
2. The engine consumes **current aircraft state**, not a delayed copy of the stick.
3. No method is enabled by default until it is proven on that aircraft.
4. Prototype 1 contains **zero writes** to the sim.
5. Later writes are isolated behind an `IControlInfluence` interface so Fenix, default aircraft, and future airliners can use different backends.

### Proposed influence backends (later phases, not prototype 1)

**Backend A — Control-command filter (preferred when it works)**  
Subscribe to `AXIS_ELEVATOR_SET` / `AXIS_AILERONS_SET` / `AXIS_RUDDER_SET` with a maskable notification group. Compute a filtered command from:

- raw axis value
- current dynamic pressure / IAS
- total weight vs empty/max
- configuration (flaps, gear, ground)
- current body rates and G

Then transmit a new event to the sim. This is not a fixed delay. It is a state-dependent gain / rate-limit / lag-on-rate model.

**Backend B — Rate damping overlay (experimental)**  
Each sim frame, read `ROTATION VELOCITY BODY *` and write a lightly attenuated value:

`ω_out = ω_in * (1 - k_damp * mass_factor * config_factor)`

Only when Master Enable is on. Must be tested for jitter and FBW conflict.

**Backend C — Fenix-specific Input Events / LVars (only if discovery succeeds)**  
At runtime, enumerate Input Events for the loaded aircraft. If Fenix exposes a sidestick input event, bind to that instead of generic AXIS events. Do not hard-code unverified Fenix LVar names in v1.

Prototype 1 implements none of A/B/C.

---

## 3. SimConnect variables and events required

### 3.1 Identity and session (read)

| SimVar | Units | Purpose |
|---|---|---|
| `TITLE` | string | Detect loaded aircraft / livery name |
| `ATC MODEL` | string | Model code |
| `ATC TYPE` | string | Type |
| `ATC ID` | string | Tail number |
| `CATEGORY` | string | Airplane vs other |
| `SIM CONNECT ON` / system events | — | Connection lifecycle |
| System event `Sim` / `Frame` / `AircraftLoaded` / `FlightLoaded` | — | Lifecycle |

### 3.2 Motion and atmosphere (read) — all documented

| SimVar | Units | Notes |
|---|---|---|
| `AIRSPEED INDICATED` | knots | |
| `AIRSPEED TRUE` | knots | |
| `AIRSPEED MACH` | mach | |
| `GROUND VELOCITY` | knots | |
| `VERTICAL SPEED` | feet/minute or feet/second | |
| `PLANE PITCH DEGREES` | degrees | |
| `PLANE BANK DEGREES` | degrees | |
| `PLANE HEADING DEGREES TRUE` | degrees | |
| `PLANE LATITUDE` | degrees | |
| `PLANE LONGITUDE` | degrees | |
| `PLANE ALTITUDE` | feet | |
| `INCIDENCE ALPHA` | radians | Angle of attack |
| `INCIDENCE BETA` | radians | Sideslip |
| `G FORCE` | GForce | Read-only |
| `ROTATION VELOCITY BODY X` | radians/second | Roll rate (body X). Official 2024 docs now list rad/s. |
| `ROTATION VELOCITY BODY Y` | radians/second | Yaw rate |
| `ROTATION VELOCITY BODY Z` | radians/second | Pitch rate |
| `ACCELERATION BODY X/Y/Z` | ft/s² | |
| `VELOCITY BODY X/Y/Z` | ft/s | |
| `DYNAMIC PRESSURE` | psf | Useful for speed-dependent response |
| `SIM ON GROUND` | bool | |
| `SURFACE TYPE` | enum | Ground inertia later |

Axis convention to confirm in prototype 1 against live Fenix data before any write logic is built. Body-axis mapping must be measured, not assumed from names alone.

### 3.3 Mass, fuel, configuration (read)

| SimVar | Units | Notes |
|---|---|---|
| `TOTAL WEIGHT` | pounds | Read-only |
| `EMPTY WEIGHT` | pounds | Read-only |
| `MAX GROSS WEIGHT` | pounds | Read-only |
| `TOTAL WEIGHT PITCH MOI` | slug·ft² | Read-only |
| `TOTAL WEIGHT ROLL MOI` | slug·ft² | Read-only |
| `TOTAL WEIGHT YAW MOI` | slug·ft² | Read-only |
| `FUEL TOTAL QUANTITY WEIGHT` | pounds | Prefer EX1 variant if the aircraft uses modern fuel system |
| `FUEL TOTAL QUANTITY WEIGHT EX1` | pounds | Documented as fuel-system agnostic |
| `FLAPS HANDLE INDEX` | number | |
| `FLAPS HANDLE PERCENT` | percent | |
| `TRAILING EDGE FLAPS LEFT PERCENT` | percent | |
| `GEAR HANDLE POSITION` | percent/bool | |
| `GEAR TOTAL PCT EXTENDED` | percent | |
| `SPOILERS HANDLE POSITION` | percent | |
| `GENERAL ENG THROTTLE LEVER POSITION:1` | percent | |
| `GENERAL ENG THROTTLE LEVER POSITION:2` | percent | |
| `TURB ENG N1:1` / `:2` | percent | Thrust proxy for jets |
| `ENG THRUST:1` / `:2` if available | pounds | Verify in SimvarWatcher; do not assume every jet exposes it |

### 3.4 Control inputs (read now, write later)

| SimVar | Units | Typical use |
|---|---|---|
| `YOKE X POSITION` | position | Roll stick/yoke |
| `YOKE Y POSITION` | position | Pitch stick/yoke |
| `YOKE X POSITION WITH AP` | position | Includes autopilot |
| `YOKE Y POSITION WITH AP` | position | Includes autopilot |
| `RUDDER PEDAL POSITION` | position | |
| `ELEVATOR POSITION` | position | Commanded elevator |
| `AILERON POSITION` | position | Commanded aileron |
| `RUDDER POSITION` | position | Commanded rudder |
| `ELEVATOR DEFLECTION PCT` | percent | Actual surface |
| `AILERON LEFT DEFLECTION PCT` | percent | Actual surface |
| `RUDDER DEFLECTION PCT` | percent | Actual surface |
| `ELEVATOR TRIM PCT` | percent | |
| `AUTOPILOT MASTER` | bool | Freeze influence when AP is flying |
| `FLY BY WIRE ELAC SWITCH` | bool | Default Airbus-like FBW flags; Fenix may not drive these |

### 3.5 Events (prototype 1: subscribe only if needed for lifecycle)

Prototype 1 needs no control events.

Later phases, only after live testing:

| Event | Purpose |
|---|---|
| `AXIS_ELEVATOR_SET` | Intercept / send pitch axis |
| `AXIS_AILERONS_SET` | Intercept / send roll axis |
| `AXIS_RUDDER_SET` | Intercept / send yaw axis |
| `ELEVATOR_SET` / `AILERON_SET` / `RUDDER_SET` | Aliases; gyro-control caveat |
| System: `AircraftLoaded`, `FlightLoaded`, `SimStart`, `SimStop`, `Pause` | Session handling |

MSFS 2024 also has `SimConnect_EnumerateInputEvents` / `SetInputEvent` for aircraft-specific B-events. These are discovered at runtime, never hard-coded until listed from a live Fenix session.

### 3.6 Variables we will **not** invent or rely on

- Any “INERTIA SCALAR”, “CONTROL FEEL”, or “DAMPING FACTOR” SimVar — they do not exist.
- Undocumented Fenix LVars until they are observed in a live session or official Fenix docs.
- Writing `G FORCE`, `TOTAL WEIGHT`, or MOI — not settable.
- Writing `PLANE PITCH DEGREES` as a feel system — that is teleportation, not inertia.

---

## 4. Limitations (hard)

1. **No official inertia API.** External apps cannot change empty weight, payload physics, or MOI without editing the aircraft package.
2. **The sim owns the flight model every frame.** Any written rate or velocity is a suggestion for that frame. The native model recomputes immediately. Continuous writes can work; they can also oscillate.
3. **Fenix A320 fly-by-wire is internal.** In an Airbus, sidestick is not an elevator. Fenix computes surface commands itself. Generic `AXIS_ELEVATOR_SET` may have little or no effect on hand-flying feel.
4. **Event masking is not a guarantee for hardware axes.** SimConnect can mask client events. Whether MSFS 2024 delivers physical yoke/sidestick movement as `AXIS_*_SET` to external clients must be measured. Some bindings stay inside the sim.
5. **Gyro-control caveat.** Official docs: `AXIS_ELEVATOR_SET` / `ELEVATOR_SET` / `RUDDER_SET` do not work when gyro controls are active.
6. **Autopilot and FBW conflict.** Influencing controls while AP or Fenix Normal Law is active will fight the aircraft. Profiles must disable or reduce influence when `AUTOPILOT MASTER` is on.
7. **SimConnect is not thread-safe.** All SimConnect calls must stay on one thread (typically the UI thread with `WM_USER_SIMCONNECT`, or a dedicated dispatcher).
8. **MSFS 2020 vs 2024 managed DLLs differ.** Build against the 2024 SDK (`Microsoft.FlightSimulator.SimConnect` from the 2024 SDK managed folder) and ship the matching native `SimConnect.dll`.
9. **LVars work over SimConnect since MSFS 2020 SU12**, but names are aircraft-defined. Do not guess Fenix names.
10. **No modification of MSFS or Fenix files in this plan.**
11. **This research environment cannot run MSFS.** Compile checks can be done later on a Windows machine with the SDK. Live SimConnect tests require your PC with MSFS 2024 running.
12. **Kids-mode / general safety:** this is a flight-sim utility only. No aircraft-file tampering, no network attack surface beyond local SimConnect.

---

## 5. Proposed physics model (design only — not in prototype 1)

Goal: change **response**, not add a dumb delay.

Define dimensionless factors from live state:

- Mass factor  
  `m̂ = clamp((W - W_empty) / (W_max - W_empty), 0, 1)`
- Speed / q-bar factor  
  `q̂ = clamp(dynamic_pressure / q_ref, 0, q_max)`  
  Controls get heavier as q rises (real aeroelasticity analogue), but the airframe also has more authority. The slider chooses which side dominates.
- Config factor  
  flaps, gear, spoiler, and on-ground add to rotational inertia feel and reduce available rate.
- Ground factor  
  `SIM ON GROUND == 1` enables a separate ground-inertia path (nose-wheel / rotation inertia), not the airborne damper.

Per-axis first-order rate model (concept):

Let `u` be the pilot command in [-1, 1]  
Let `ω` be measured body rate  
Let `ω_cmd` be the rate the pilot is asking for, scaled by speed and mass:

`ω_cmd = u * ω_max(q, config) * (1 - k_inertia * m̂)`

Then apply damping toward that command using the **actual** current rate:

`ω_target = ω + (ω_cmd - ω) * (1 - exp(-dt / τ))`  
`τ = τ0 * (1 + k_damp * m̂) / max(q̂, q_floor)`

Implementation mapping:

- **Backend A:** convert `ω_target` back into a filtered axis command and send `AXIS_*_SET`.
- **Backend B:** write `ROTATION VELOCITY BODY *` toward `ω_target` with a small blend so the native model still leads.
- Never hold the last stick value for N milliseconds as the primary effect. That is the FSRealistic-style approach we are not using.

Sliders map to:

| UI slider | Model term |
|---|---|
| Inertia | `k_inertia`, `τ0` |
| Pitch / Roll / Yaw Damping | per-axis `k_damp` |
| Control Response | `ω_max` scale |
| Ground Inertia | ground `τ` and yaw/pitch blend |
| Turbulence Response | attenuation of high-frequency rate error (not fake shake) |

Master Enable gates every write. When off, the app is read-only.

Fenix profile default (until proven otherwise): all write backends **off**. Telemetry on. We enable a backend only after a live test shows the sim actually accepts it.

---

## 6. Project folder structure

```
HeavyFeel/
  HeavyFeel.sln
  README.md
  docs/
    HeavyFeel_Feasibility_Report.md
  src/
    HeavyFeel.App/                 WPF UI
      Views/
      ViewModels/
      Themes/
    HeavyFeel.Core/                models, profiles, settings, physics interfaces
    HeavyFeel.SimConnect/          connection, data defs, mapping
    HeavyFeel.Logging/
  tests/
    HeavyFeel.Tests/
  artifacts/
    logs/
```

Tech choices for prototype 1:

- .NET 8
- WPF (widest SimConnect + `Hwnd` message compatibility; Microsoft samples use WinForms/WPF-style `Handle`)
- Official `Microsoft.FlightSimulator.SimConnect` from MSFS 2024 SDK
- JSON settings via `System.Text.Json`
- NLog or simple file logger
- MVVM so the UI can be designed without coupling to SimConnect

---

## 7. Development roadmap

### Phase 0 — this document  
Research only. **You are here.**

### Phase 1 — read-only prototype (next, after approval)
- Create solution and projects
- SimConnect connect / disconnect / reconnect
- Aircraft detection
- Subscribe to the verified SimVar list
- Live telemetry window
- File logging
- Master Enable switch present but **does nothing to the sim**
- Sliders present but **not connected to any write path**
- Settings persistence for window + slider values
- Compile on Windows; you run it against MSFS 2024 + Fenix

Exit criteria: with Fenix loaded, every listed input either shows a plausible live value or is marked “unavailable on this aircraft.”

### Phase 2 — measurement
- Log whether `AXIS_*_SET` events arrive from your hardware
- Compare `YOKE * POSITION` vs `ELEVATOR/AILERON POSITION` vs surface deflection on Fenix
- Try a **manual, user-triggered, one-shot** write test behind a debug button (not automatic)
- Decide Backend A vs B vs Fenix Input Event for this aircraft

### Phase 3 — one axis only
- Pitch damping/response only
- Default aircraft first if Fenix rejects writes
- Then Fenix if a working path was found

### Phase 4 — roll, yaw, ground, config, profiles
### Phase 5 — polish, installer, safety limits, AP lockout

No phase starts until the previous exit criteria are met.

---

## 8. What you will need on your PC (when we implement)

1. Windows 11
2. MSFS 2024 installed and running
3. Fenix A320 installed
4. MSFS 2024 SDK installed from Developer Mode (provides `SimConnect.dll` and managed wrapper)
5. Visual Studio 2022 (Community is enough) with .NET 8 desktop workload  
   I can generate all project files here; you compile and run them on Windows. This environment cannot launch MSFS.

---

## 9. Honest recommendation

Build HeavyFeel. Keep the UI and telemetry ambitious. Keep the physics **humble**.

The product that can actually ship is:

1. A high-quality live flight-state monitor and aircraft profiler.
2. A state-based control-response filter **where the aircraft still listens to SimConnect axes**.
3. An optional experimental rate damper for aircraft that accept `ROTATION VELOCITY BODY *` writes.
4. A Fenix profile that starts read-only and unlocks a method only after we prove it.

That is not FSRealistic. It is also not a replacement for Fenix’s own flight model. It is an external response layer constrained by the real SDK.

---

**Waiting for approval to implement Phase 1 only** (connect, detect aircraft, read verified flight data, display, log).
