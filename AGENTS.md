# AGENTS.md

## Cursor Cloud specific instructions

Quick Shell is a **Windows-only .NET 10 desktop product**: a PowerToys Command Palette
extension (`QuickShell`), a PowerToys Run plugin (`QuickShell.Run`), and a shared
business-logic library (`QuickShell.Core`, tested by `QuickShell.Core.Tests`). Every
runnable component targets `net10.0-windows*` and depends on WinForms/WPF/WinUI, the
Windows App SDK, CsWinRT, and MSIX packaging.

The Cursor Cloud VM is **Linux**, so the GUI product **cannot be run** here and cannot be
fully built or tested here. Full build/test/run happens on Windows — see the
`Building from source` section of `README.md` and the `windows-latest` job in
`.github/workflows/ci.yml` for the authoritative commands (`scripts/deploy.ps1` is the
Windows dev loop).

### What works on this Linux VM

- The **.NET 10 SDK** is installed system-wide at `/usr/share/dotnet` (`dotnet` is on
  `PATH` via `/usr/local/bin/dotnet`). It persists in the VM snapshot; the startup update
  script only runs `dotnet restore` (see below).
- `dotnet restore QuickShell.sln -p:EnableWindowsTargeting=true` succeeds for all projects.
- `dotnet build QuickShell.Core/QuickShell.Core.csproj -c Release -p:Platform=x64 -p:EnableWindowsTargeting=true`
  succeeds. This is the only project that compiles on Linux, and it is **build-only** —
  `net10.0-windows7.0` assemblies cannot execute here (no Windows Desktop runtime).

### What does NOT work on this Linux VM (expected, do not try to "fix")

- Building `QuickShell`, `QuickShell.Run`, or `QuickShell.Core.Tests` fails: CsWinRT runs
  `cswinrt.exe` (a Windows PE binary) during build → `Exec format error`. MSIX tooling and
  the WinUI/Windows App SDK are also Windows-only.
- `dotnet test` cannot run: the test project is `net10.0-windows10.0.26100.0` and references
  the WinUI extension, so it requires the Windows runtime.
- `EnableWindowsTargeting=true` is required for any restore/build of the `-windows` TFMs on
  Linux; without it restore/build errors out with NETSDK1100.

Validate cross-platform changes to shared logic by building `QuickShell.Core` on Linux;
anything touching the extension, Run plugin, tests, or packaging must be verified on Windows.
