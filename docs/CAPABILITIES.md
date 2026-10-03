# What SimConnect can and cannot do (MSFS 2024)

This is the honest map for HeavyFeel. No invented APIs.

## Possible (used)

- Connect as a SimConnect client and read the user aircraft every frame.
- Read identity: TITLE, ATC MODEL, ATC TYPE, ATC ID, CATEGORY.
- Read flight state: IAS, VS, pitch, bank, heading, lat/lon/alt, AoA, G, body rates, body accelerations, Q-bar.
- Read `IS ON GROUND`, `SIM ON GROUND`, `PLANE ALT ABOVE GROUND`, `RADIO HEIGHT`.
- Read `AUTOPILOT MASTER` as its own INT32 packet (Fenix often leaves the stock master at 0).
- Read Fenix FCU locals in a separate packet: `L:S_FCU_AP1`, `L:S_FCU_AP2`, `L:I_FCU_AP1`, `L:I_FCU_AP2`.
- Read mass and inertia: TOTAL WEIGHT, EMPTY WEIGHT, MAX GROSS WEIGHT, fuel weight, pitch/roll/yaw MOI.
- Read configuration: flaps, gear, spoilers, throttle, N1, yoke, pedals, surface positions, trim.
- Write (optional, Master Enable): `AXIS_ELEVATOR_SET` / `AXIS_AILERONS_SET` / `AXIS_RUDDER_SET`.
- Write (optional, currently disabled in the engine): `ROTATION VELOCITY BODY X/Y/Z`.
- Detect aircraft family from title/model (Cessna → A320 → 777 → 747) and scale feel.

## Partially possible

- Heavier control feel: only as a small mix on AXIS events while the stick is deflected. Fenix FBW owns the sidestick. Writing AXIS at high priority previously locked the jet.
- Different types feeling different: scale only. We cannot give a 747 a different lift/drag model than MSFS already uses.
- Turbulence as air mass: we can read ambient wind. We cannot replace MSFS weather. Adding extra body rates is a fake force and is not enabled.
- Landing weight: we can read radio height and gear. We cannot simulate oleo compression or change how the sim solves gear contact.
- Ground friction / brakes / steering: owned by the aircraft CFG + Fenix. We can only lag nosewheel/rudder input slightly.

## Impossible through SimConnect alone

- Replacing the MSFS flight model (lift, drag, ground effect, flap aerodynamics).
- Applying a documented, stable external force/moment to every aircraft.
- Changing Fenix fly-by-wire laws.
- Editing mass so the sim integrates a different weight (Fenix payload/fuel owns that; writing TOTAL WEIGHT breaks the addon).
- Making reverse thrust, spoilers and brakes “interact” beyond what the aircraft already does.
- Camera-free “physical turbulence” that is actually new aerodynamics.
- One binary that rewrites every aircraft’s .cfg / WASM.

Those need either:

- the stock / Fenix flight model as-is (correct approach), or
- a WASM gauge inside a specific aircraft, or
- aircraft file edits (out of scope).

## Architecture we use

Windows WPF app + official `Microsoft.FlightSimulator.SimConnect`.

No WASM module, no aircraft file edits, no camera shake.

If a write fails or an LVar is missing, the app keeps reading and stops that write path.

## What “more realistic” means here

1. Correct GROUND/AIR and AP ON/OFF (read path).
2. Live weight + aircraft class scale how much extra stick inertia is applied.
3. Writes only while the sidestick is actually deflected, so Fenix is not sent a zero stick.
4. No rate rewrite on takeoff (that made the nose drop).
