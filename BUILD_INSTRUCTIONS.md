# Build Instructions (Windows)

These commands were **not run** in the environment that produced this project (no
.NET SDK, no network, Linux host — see `README.md`). They are the exact, standard
.NET 8 commands for this project layout; run them on a Windows 10/11 machine.

## Prerequisites

- Windows 10 or 11, 64-bit.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the SDK, not just
  the runtime — you need it to build). The WPF workload is included in the SDK by
  default on Windows; no separate Visual Studio installation is required, though
  Visual Studio 2022 (17.8+) with the ".NET Desktop Development" workload also works
  and can open `ArchiveManager.sln` directly.

## 1. Restore and build (Debug, for development)

```powershell
cd ArchiveManager
dotnet restore
dotnet build
```

This restores `Microsoft.Data.Sqlite` (the only third-party package the main project
depends on) and the xUnit packages for the test project, then compiles both projects.
If the restore step needs to reach the internet the first time (to pull NuGet
packages into the local cache), make sure the machine has normal internet access —
nothing here is a custom or private feed.

## 2. Run it directly (Debug)

```powershell
dotnet run --project src/ArchiveManager/ArchiveManager.csproj
```

On first launch it creates `Data/`, `Config/`, `Backup/`, `Logs/` next to the build
output and prompts for the archive root folder if `Config/settings.json` doesn't
already have one configured.

## 3. Run the tests

```powershell
dotnet test tests/ArchiveManager.Tests/ArchiveManager.Tests.csproj
```

This runs the naming-engine tests and the seven entity-identity scenario tests
(`EntityTransitionServiceTests.cs`) against a real temp-folder SQLite database and a
real temp-folder archive tree — no mocking of the file system or database, so a green
run here is a meaningful signal.

## 4. Produce the portable build (Release, self-contained, single file)

```powershell
cd src/ArchiveManager
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ..\..\publish
```

This is exactly the "portable, no-install" profile requested in the spec: it embeds
the .NET runtime into a single `ArchiveManager.exe`, so the target machine does not
need .NET installed. The `.csproj` already has `SelfContained`, `RuntimeIdentifiers`,
`PublishSingleFile`, and `IncludeNativeLibrariesForSelfExtract` set as defaults, so a
plain `dotnet publish -c Release` from that folder will also work; the explicit flags
above are there for clarity and to override safely if those defaults ever change.

After publishing, assemble the portable folder:

```powershell
mkdir ..\..\ArchiveManager-Portable
copy ..\..\publish\ArchiveManager.exe ..\..\ArchiveManager-Portable\
mkdir ..\..\ArchiveManager-Portable\Data
mkdir ..\..\ArchiveManager-Portable\Config
mkdir ..\..\ArchiveManager-Portable\Backup
mkdir ..\..\ArchiveManager-Portable\Logs
copy Config\settings.default.json ..\..\ArchiveManager-Portable\Config\
xcopy /E /I Localization ..\..\ArchiveManager-Portable\Localization
```

(The `dotnet publish` output already copies `Config/settings.default.json` and
`Localization/*.json` next to the exe because the `.csproj` marks them
`CopyToOutputDirectory` — the manual copy above is only needed if you're assembling
the portable folder from a different location than the publish output.)

Result:

```
ArchiveManager-Portable/
├── ArchiveManager.exe
├── Config/settings.default.json
├── Localization/strings.fa.json
├── Localization/strings.en.json
├── Data/       (created empty; archive.db appears on first run)
├── Backup/
└── Logs/
```

Copy this folder anywhere (including a USB drive) and run `ArchiveManager.exe` — no
installer, no admin rights (the app manifest requests `asInvoker`), no separate .NET
install needed.

## 5. Opening in Visual Studio instead

Double-click `ArchiveManager.sln`. Set `ArchiveManager` (under `src/ArchiveManager`)
as the startup project if it isn't already, then F5 to run, or use
**Build → Publish** with the same self-contained/single-file settings as above for a
GUI equivalent of step 4.

## If the first build produces errors

This project was written and statically reviewed (XML well-formedness, brace/paren
balance, `x:Class` cross-checks, resource-key cross-checks, constructor/call-site
cross-checks — see `README.md`) but never actually compiled, since no compiler was
available in the authoring environment. If `dotnet build` surfaces something the
static review missed — most likely candidates are a XAML binding path typo, a missing
`using`, or a minor type mismatch — those are the kind of small, mechanical fixes a
compiler error message makes straightforward to locate and correct; none of the
project's actual logic depends on anything unusual or hard to diagnose.

## Fonts

The design system (`Resources/Theme/Typography.xaml`) specifies
`Vazirmatn, Segoe UI, Tahoma` as the font stack. If the target machine doesn't have
Vazirmatn installed, WPF falls back to Segoe UI (still readable Persian, just not the
same face). To ship Vazirmatn with the portable build instead of relying on it being
installed: drop the `.ttf` under `Resources/Fonts/` in the project, mark it as
`Resource` in the `.csproj`, and change the `FontFamily` in `Typography.xaml` to
`./Resources/Fonts/#Vazirmatn, Segoe UI, Tahoma`.
