# HeavyPro licensing: what holds, what doesn't, and the ceiling

An honest white-box assessment of the licence enforcement, from attacking it directly. Read the
first line twice: **no licence scheme on software that runs on the pirate's own PC can be made
unpiratable.** Anything the code checks, the pirate can patch out, because the code is theirs to
edit. What follows is about raising the cost of that, not pretending it is impossible.

## How enforcement works
The stick-shaping output (the thing customers pay for) is gated in one place —
`OfficialSimClient.ApplyInfluence`, by `ControlLimits.Unlocked`. `App` calls
`ControlLimits.Unlock(lease)` only after the licence server returns a token signed with its private
key (RS256; HeavyPro holds only the public key). The unlock carries the server's lease time and is
re-armed every 20 minutes; a revoked/expired licence, or the recheck loop simply stopping, relocks
it on its own. `#if DEBUG` skips the check; Release (what ships) always enforces.

## Attacks that FAIL (these genuinely hold)
- **Forge a licence token.** The private key never leaves the server and the public key is built
  in, so a fake server or a hand-made token is rejected (signature, machine id, nonce, lease and
  clock are all checked). Verified by the `Rotaidem.Licensing` tests.
- **Copy `license.json` to another PC.** The token is bound to a hashed machine id; another PC's id
  will not match.
- **Replay an old answer.** Each request carries a random nonce the token must repeat.
- **Edit the local config / licence file.** Validity is decided by the server, not by anything on
  disk; editing local files cannot grant access, only invalidate it.
- **Freeze the clock to stretch offline use.** The stored "last seen" time catches a clock turned
  back, and the signed lease caps offline running regardless.

## The attack that WORKS (the ceiling)
Patch the gate in the DLL. `ControlLimits.IsUnlockedAt` → `return true;`, or drop the
`&& ControlLimits.Unlocked` term in `ApplyInfluence`, plus stub `EnsureLicensedAsync` to skip the
window. On an **unsigned** build this is a few minutes in dnSpy/ILSpy and gives a fully offline,
keyless copy. There is no way to make this impossible on the client — only harder.

## What raises the cost: signature-pinned tamper check (added, dormant)
`AssemblyIntegrity` + `IntegrityPolicy` run at start-up. **Once the binaries are Authenticode-signed
by Rotaidem** (see `Sign-File.ps1` / `DLL-INVENTORY.md`):
- Each shipped HeavyPro assembly must have a valid signature whose subject names Rotaidem.
- Patching any DLL breaks its signature → the check fails → shaping stays **locked**.
- The pirate must then also strip the check from a *signed* `HeavyPro.exe` and re-sign the whole set
  with a certificate that shows "Rotaidem" and chains to a trusted root — which they cannot obtain.

That turns the two-minute edit into real reverse-engineering work. It does **not** make it
impossible: a skilled attacker patches the verifier itself. Defence in depth raises cost; it never
reaches zero on client-run software.

Until a certificate exists the build is unsigned, so the check is **dormant** (it allows start-up) —
exactly like the signing step. It only starts enforcing the day a certificate is added.

## What would actually move the needle further (and the trade-offs)
- **Buy the code-signing certificate** (OV ~€200–400/yr, or Azure Trusted Signing ~$10/mo). This is
  what activates the tamper check above and stops the SmartScreen/Norton warnings. It is the single
  highest-value step and the only one with a price tag.
- **Online-only mode** (validate every launch, short lease) narrows the offline-patch window but
  punishes honest customers with flaky internet; not worth it for a €5 desktop add-on.
- **Server-side execution of the shaping** would be truly unpiratable but is impossible for a
  real-time flight controller (latency), so it is not on the table.

Obfuscation is deliberately **not** used: it broke start-up in 0.9.2 and makes antivirus heuristics
worse, for a protection a determined attacker removes anyway.
