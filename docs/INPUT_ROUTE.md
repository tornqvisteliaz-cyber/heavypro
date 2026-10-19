# Input route

HeavyPro cannot treat a successful `AXIS_*_SET` or `YOKE POSITION` write as ownership of a physical joystick. MSFS 2024 can still apply the original hardware value.

## Options checked

### A. SimConnect event masking

Documented. `SIMCONNECT_GROUP_PRIORITY_HIGHEST_MASKABLE` is 10000000. A notification group at or below that priority can mask events if `AddClientEventToNotificationGroup` is called with the maskable flag set. The simulator itself is priority `DEFAULT` (2000000000). Masking is the supported way to stop the sim seeing the original event, then send a processed one.

The previous code passed `false` for the maskable flag, so it did not mask. It also rewrote the event inside the callback at high priority, which froze the sim.

This proof of concept sets the maskable flag, does not rewrite inside the callback, and sends the processed value at standard priority. Status stays `INITIALIZING` until a masked axis event arrives, then `ACTIVE`.

Limitation: this only sees axes that MSFS emits as `AXIS_ELEVATOR_SET`, `AXIS_AILERONS_SET`, or `AXIS_RUDDER_SET`. If the WinWing is bound another way, the route stays `UNAVAILABLE` and the physical input bypasses HeavyPro.

Tested in MSFS 2024 1.9.18 (L-39, on the ground) by a second SimConnect client sending `AXIS_ELEVATOR_SET` at `HIGHEST` priority, which reaches every maskable group before the sim:

- HeavyPro not running: `YOKE Y POSITION` jumps to the commanded value in one frame.
- HeavyPro, Master off: identical — the event passes straight through.
- HeavyPro, Master on: the raw event is masked; `YOKE Y POSITION` ramps to the shaped value over ~1 s and returns to exactly 0 on release.

Not yet confirmed with a physical joystick as the event source.

### B. Virtual controller

`physical WinWing → HeavyPro → virtual joystick → MSFS`. This is the only route that can stop the original device if masking does not see it. It needs a signed virtual HID driver such as vJoy, plus the user binding only the virtual device in MSFS. HidHide can hide the physical device from the sim. Not installed by this build. Requires explicit user approval.

### C. Other Windows routing

Exclusive HID open plus a virtual device is the same architecture as B. Kernel interception drivers are out of scope.

## Recovery

Master off passes the original axis through and does not process it. Disconnect stops writes. A crash does not uninstall a driver because none is installed. If masking was armed and the app dies, MSFS should resume normal input on the next connection cycle. That recovery is not proven here.

## Manual test

1. Bind the WinWing pitch to Elevator Axis in MSFS.
2. Start HeavyPro, Master on, autopilot off, Asobo aircraft.
3. Move the stick. Status must change from `INITIALIZING` to `ACTIVE`.
4. Hold the physical stick at one position. The aircraft should follow the slower processed value, not the raw stick.
5. Turn Master off. The stick must control the aircraft immediately.
