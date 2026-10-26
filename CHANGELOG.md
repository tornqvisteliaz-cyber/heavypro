# HeavyPro changelog

Each release's lines show in HeavyPro as "What's new" and on its GitHub release page.

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
