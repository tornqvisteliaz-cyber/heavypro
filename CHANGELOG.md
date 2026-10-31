# HeavyPro changelog

Each release's lines show in HeavyPro as "What's new" and on its GitHub release page.

## 0.6.9

- **No more jumping on the 777 and the BlueBird 757:** HeavyPro never puts an aircraft on Direct while your stick is still bound in MSFS (the aircraft got the raw stick and HeavyPro's in turn). The 777, 787, A320neo and GA keep the MSFS bindings route.
- **The Aerosoft A330/A340 move again, and the 757 is smooth:** they read the MSFS binding themselves, so HeavyPro now hands them the stick untouched (PASS-THROUGH, without HeavyPro's weight). Give them an MSFS controls profile without the stick axes and pick REAL1 to get the weight.
- **Back to the usual route at the main menu** after one of those, before the next aircraft loads, so the PMDG 777 gets the stick in the right order.
- **Pick your own route:** Stick setup has Auto (recommended), REAL1, Direct and MSFS bindings again, and shows which route is in use and why.

## 0.6.8

- **Major payware aircraft recognised by name:** HeavyPro shows which aircraft you are flying ("PMDG 777-300ER", "iniBuilds A350-1000", "Fenix A320"…) and REAL1 gives each the feel of its real controls: Boeing columns for the PMDG 737 and 777, iFly 737 MAX, CSS 737, BlueBird 757, 727, MD-82, DC-9 and E-Jets; heavy columns for the A300, A310, TriStar, MD-11 and Concorde; Airbus sidesticks for Fenix, FSLabs, iniBuilds and Aerosoft Airbuses; a regional feel for the HondaJet, Citation, F406, Islander, Kodiak and Black Square; light cables for A2A, Just Flight and Carenado. The full list is under Setup.
- Every other aircraft still gets a feel from its type, as before.

## 0.6.7

- **One route, picked for you:** no more choosing between REAL1, Direct and MSFS bindings. With your stick bound in MSFS, HeavyPro shapes it through that binding, as in 0.5.3; with it unbound, REAL1 (or Direct with your own profile if you linked one to the aircraft). If the stick never reaches HeavyPro through MSFS in an aircraft, that aircraft switches to REAL1 by itself. Setup shows which route is in use and why.
- **BlueBird 757:** it reads the stick itself, so with your stick bound in MSFS it now gets the stick directly from HeavyPro, and HeavyPro's in-sim bridge answers your MSFS binding with the shaped value. This is new: if the 757 does not feel right, press Copy beta report after the flight and send it to us.
- **Real 777 feel:** the PMDG 777 switch (and its feel) is now called "PMDG 777: Real 777 feel".
- **Runs in the background:** closing the window keeps HeavyPro shaping from the tray; right-click its icon to open or exit. "Start with Windows (in the background)" under Aircraft & App starts it there when you log in.
- The Terms of Service were updated for the new name; HeavyPro asks you to accept them once more.

## 0.6.6

- **The MSFS bindings route works like before again:** "Fix MSFS bindings" is no longer offered on that route. The route needs your stick bound in MSFS, and Fix there cleared it from every airplane profile, so the GA planes got no stick. Fix still appears on REAL1 and Direct, where it belongs; if you pressed it, Restore MSFS bindings (Setup) puts your stick axes back.
- **No more wrong USE REAL1 badge:** the PMDG 777 and other add-ons fly on the MSFS bindings route as they did in 0.5.3.

## 0.6.5

- **BlueBird 757:** REAL1 now recognises every BlueBird 757-200 and gives it its own feel: a big Boeing column, heavier than the 737 and lighter than the 777, crisp in roll. It works on REAL1 and Direct; BlueBird models its own rudder ratio changer, so HeavyPro's rudder limiter stays out of the way.
- **Restore MSFS bindings (Setup):** undoes "Fix MSFS bindings" for flying without HeavyPro. It puts back only the stick axes; your buttons and anything you changed since stay as they are.
- **USE REAL1 badge:** on the MSFS bindings route, an add-on that route can't reach (PMDG, iniBuilds, BlueBird) now says so instead of a yoke that silently doesn't move.
- **Beta:** HeavyPro asks you to accept its Terms of Service on first start (Terms at the bottom of the app shows them). Copy beta report puts what we need for a bug report on the clipboard, without your Windows user name or licence key. Creators may stream and publish videos of HeavyPro, monetised ones included (Terms section 3).
- HeavyPro is © 2026 Rotaidem, all rights reserved.

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
