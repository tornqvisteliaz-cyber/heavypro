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
- Write (Master Enable, while an input is deflected or settling): `AXIS_ELEVATOR_SET` / `AXIS_AILERONS_SET` / `AXIS_RUDDER_SET`.
- Write (registered but **disabled** in the inertia engine): `ROTATION VELOCITY BODY X/Y/Z` — left off because continuous rate writes fight the sim and Fenix FBW.
- Read physical control axes, apply HeavyPro's time-based input dynamics, and send the resulting axes through the official SimConnect events.
- Detect aircraft family from title/model and choose a configurable starting dynamics profile.

## Partially possible

- Heavier control feel: can change how quickly control inputs reach their target. Add-on aircraft may process or ignore the standard AXIS events differently.
- Different types feeling different: input response profiles only. We cannot give a 747 a different lift/drag model than MSFS already uses.
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
2. Aircraft-specific input dynamics control how quickly stick input reaches its target.
3. Writes continue while a deflected input is settling, then stop at center.
4. Keep the simulator's aerodynamic and flight-control model in charge; HeavyPro modifies only the incoming axis commands.

## Input dynamics

The control path keeps three values for elevator, aileron, and rudder: raw input, input after deadzone/curve shaping, and final dynamically filtered input. Profiles set independent response, acceleration, deceleration, rate, damping, and return behavior. Settings can tune response, curves, deadzone, sensitivity, rates, and return delay. Airspeed scaling is optional.

The example GA, Fenix A320, PMDG 737/777, ASOBO 787, and generic airliner values are starting presets only. They are not manufacturer data. The engine uses delta time and bounded second-order updates, prevents overshoot, and does not add input noise or reduce endpoint authority.
