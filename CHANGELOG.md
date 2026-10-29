# HeavyPro changelog

Each release's lines show in HeavyPro as "What's new" and on its GitHub release page.

## 0.6.4

- **PMDG 777: FF777-style flight model (new switch under Setup):** turn it on and HeavyPro builds a copy of your PMDG 777's own flight model with 15% more pitch and roll inertia and damping, so the 777-300ER and 777F feel big, settled and planted, and MSFS loads it after PMDG's. PMDG's files are never changed; turn the switch off to get PMDG's flight model back. It rebuilds by itself after a PMDG update, and waits until MSFS is closed to change anything. Turn it off before contacting PMDG support.

## 0.6.3

- **Fix MSFS bindings keeps your buttons:** it now removes only the stick axis from your MSFS 2024 controls and never a button, not even a button bound to the elevator, aileron or rudder itself. Everything else in your profiles stays exactly as it was, and each profile is still backed up.

## 0.6.2

- **The 777 flies FF777-style on REAL1, by itself:** load the PMDG 777-300ER or 777F on the REAL1 route and HeavyPro gives it a feel modelled on the FlightFactor 777: big and settled, a calm wheel around centre so a resting hand never rolls it, precise small corrections in pitch, a touch more control mass, and a column that firms up with airspeed. Nothing to set up. HeavyPro changes how your stick reaches the aircraft; the PMDG 777's own flight model and fly-by-wire stay PMDG's.

## 0.6.1

- **REAL1 no longer twitches or feels like nothing changed on the PMDG 777:** MSFS 2024 picks a control profile per aircraft, and HeavyPro only checked the one you changed last. An older airplane profile that still bound your stick (used by the PMDG 777, the iniBuilds A350-900, the 747 and many more) sent the raw stick next to REAL1's. HeavyPro now checks every airplane profile, shows UNBIND IN MSFS when any of them binds your stick or pedals, and "Fix MSFS bindings" clears them all (close MSFS first; each profile is backed up).

## 0.6.0

- **REAL1, the new best route:** pick "REAL1" next to Direct and MSFS bindings. HeavyPro then tunes the feel for each aircraft by itself, the way the best add-ons do it: a linear, planted sidestick for the Airbus fly-by-wire (iniBuilds A320 and A350; a lighter touch on the Fenix, which models its own), a soft-centred column that firms up with airspeed for the PMDG 777 and 737, and light, direct cables for Asobo GA. Like the best flight-model mods on flightsim.to, REAL1 gives the controls real mass: the response builds up and carries on a touch, small corrections stay smooth and quick, and full stick still gives full authority. Normal / Medium / Realistic (also in the in-game panel) set how strong it is. Every family gets realistic rudder pedals and a spring return to centre. New installs start on REAL1; future versions will be REAL2, REAL3 and so on.
- **In-game panel in the MSFS 2024 style:** smoked glass, white type and white selected buttons, like the sim's own menus.

## 0.5.3

- **Fix MSFS bindings for me:** when MSFS still sends your stick or pedals on its own (the reason the PMDG 777 and iniBuilds A350 twitched, jumped back or felt like paper), one button (at the top, next to UNBIND IN MSFS, and under Stick setup) clears exactly those axes in your MSFS 2024 controls, keeps a backup, and switches HeavyPro to the Direct route. Close MSFS first.
- **PMDG 777 gets its own feel again:** MSFS 2024 names it just "777-300ER", so HeavyPro treated it as a generic airliner.

## 0.5.2

- **RotaHub:** install and update HeavyPro from RotaHub, Rotaidem's add-on manager. It also links the in-game panel into MSFS 2024 for you. The Update button inside HeavyPro is gone; RotaHub handles updates.
- HeavyPro shows its real version number (some builds said 0.4.0).
- **Realistic answers sooner:** it no longer sits at the slowest possible stick travel, so small inputs get a response right away. It is still clearly heavier than Medium. If you used Realistic, your settings move to the new values by themselves.
- **No more twitching on Direct:** when MSFS still has your stick bound, HeavyPro passes the stick through untouched instead of fighting the binding, and tells you exactly which MSFS profile to clear.
- **MSFS bindings route:** the app shows your pitch and roll even when an add-on like the PMDG 777 takes the stick before HeavyPro.

## 0.5.1

- **Profiles per aircraft:** in Profiles, pick a profile and press "Use for this aircraft". From then on it switches on by itself whenever that aircraft loads.
- **What's new:** after an update, HeavyPro shows what changed, and the welcome window shows the version you run.
- Release pages on GitHub now list what changed in plain words.

## 0.5.0

- **Update button:** when a new HeavyPro is out, "Update to …" appears at the top. One click downloads it, checks it is genuinely from Rotaidem, installs it and reopens HeavyPro.
- **In-game panel:** a HeavyPro button in the MSFS 2024 toolbar to switch HeavyPro on or off, pick Normal / Medium / Realistic and change inertia without leaving the cockpit.
- **Realistic rudder (option):** pedals get heavier as airspeed builds and stay light while taxiing.
- **Ready-made profiles** for the 777, 737, 787, 747, A320, A350, turboprops, business jets and the C172.
- **PMDG 777:** the MSFS bindings route gives the 777 its heavy feel back.
- HeavyPro warns when an MSFS control profile still binds the stick axes it handles.
- The C172 is recognised as a light aircraft (it was treated as a C-17).
