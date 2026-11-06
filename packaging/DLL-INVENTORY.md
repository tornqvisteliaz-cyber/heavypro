# HeavyPro binary inventory and release procedure

What ships in HeavyPro, where each file comes from, and how a release is built, signed and
verified. Sizes are the 0.9.3 Release build (x64, .NET Framework 4.8); they move a little
between versions.

## Assemblies in the installer

### Proprietary — Rotaidem code
| File | ~Size | Purpose | Depends on |
|------|------:|---------|-----------|
| `HeavyPro.exe` | 245 KB | WPF app, SimConnect loop, UI, tray | Core, SimConnect, Logging, Licensing, System.Text.Json |
| `HeavyPro.Core.dll` | 224 KB | Flight model, REAL1, response tuning, settings, SimBrief, update check | System.Text.Json |
| `HeavyPro.SimConnect.dll` | 46 KB | SimConnect transport (reads the stick, writes shaped axes) | Core, Logging, MSFS SimConnect |
| `Rotaidem.Licensing.dll` | 39 KB | Licence keys and signed online activation (hand-rolled JSON, no deps) | — |
| `HeavyPro.Logging.dll` | 7 KB | File logger | Core |

The licensing code is also compiled directly into `HeavyProSetup.exe` (one-file installer).

### Third-party — redistributed, not ours
| File | Origin | Notes |
|------|--------|-------|
| `Microsoft.FlightSimulator.SimConnect.dll` | MSFS 2024 SDK | Managed SimConnect wrapper. `lib/` at build time; `NOTICE-MSFS-SimConnect.txt` ships beside it. |
| `SimConnect.dll` | MSFS 2024 SDK | Native SimConnect runtime. |
| `System.Text.Json.dll` + 8 support DLLs | NuGet `System.Text.Json` 10.0.12 | Settings read/write and the SimBrief / update-check JSON parsers. The support DLLs (`System.Memory`, `System.Buffers`, `System.Numerics.Vectors`, `System.Text.Encodings.Web`, `System.IO.Pipelines`, `System.Threading.Tasks.Extensions`, `Microsoft.Bcl.AsyncInterfaces`, `System.Runtime.CompilerServices.Unsafe`) are its own dependencies on .NET Framework. |

`System.Text.Json` is the whole third-party runtime footprint (~1.3 MB). It is a real
dependency — the settings model and three API parsers use it — so it is kept, not stripped.
`Rotaidem.Licensing` deliberately avoids it (its own small JSON) so the installer stays one file.

## Dependencies
- Runtime NuGet: **`System.Text.Json` 10.0.12** only. `dotnet list package --vulnerable
  --include-transitive` reports none.
- Build-only (never shipped): `Microsoft.NETFramework.ReferenceAssemblies` (`PrivateAssets=all`).
- Test-only: `xunit`, `xunit.runner.visualstudio`.
- `.NET Framework 4.8` is the runtime; it ships with Windows 10/11, so nothing bundles it.

## Build settings (`Directory.Build.props`)
- `Deterministic=true` — a clean rebuild of a commit gives byte-identical DLLs (verified: the
  Core DLL hash is unchanged across rebuilds).
- `ContinuousIntegrationBuild=true` when `CI=true` — normalises source paths in the PDBs so they
  don't carry the runner's folder layout.
- `DebugType=portable` — symbols are separate `.pdb` files, never embedded in the DLLs.
- Metadata: Company `Rotaidem`, Product `HeavyPro`, Copyright, Trademark, Version (the tag on CI).

## Release pipeline (`.github/workflows/setup-exe.yml`)
1. Tests, then build the app (x64 Release) and refresh the payload zip **without obfuscation**
   (`New-AppPayload.ps1 -SkipObfuscation`): plain DLLs. Obfuscation broke 0.9.2 at start-up and
   makes antivirus heuristics worse.
2. `Sign-File.ps1` signs `HeavyPro.exe`, then the payload is zipped (PDBs excluded) and embedded
   in `HeavyProSetup.exe`, which is signed too.
3. `SHA256SUMS.txt` covers **every** published file (setup exe, zip, RotaHub package, `update.jws`,
   `rotahub.jws`). Verify with `sha256sum -c SHA256SUMS.txt`.
4. PDBs for the proprietary assemblies go to a **login-only CI artifact**
   (`HeavyPro-symbols-<version>`), never to the public release or the installer.
5. Tags publish a versioned GitHub release with notes from `CHANGELOG.md`; `main` refreshes the
   `beta` release.

## Code signing
The signing steps are wired but inert until these repo secrets exist:
- `CODE_SIGNING_PFX_BASE64` — the Authenticode certificate (.pfx), base64-encoded.
- `CODE_SIGNING_PFX_PASSWORD` — its password.

`Sign-File.ps1` signs with SHA-256 and an RFC-3161 timestamp (so signatures stay valid after the
certificate expires) and verifies the signature after signing. Keep the certificate only in GitHub
secrets, never in the repo. Until a certificate is added, Windows SmartScreen and some antivirus
products warn on first run; a certificate is the only real fix (see the Norton note in the release
discussion). This is intellectual-property and integrity protection, not obfuscation — plain,
signed binaries are what security products trust.

## Verifying a download
```
sha256sum -c SHA256SUMS.txt          # every file matches its listed hash
signtool verify /pa HeavyProSetup.exe # once a signing certificate is in use
```
