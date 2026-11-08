# Development

## Prerequisites

- Windows 10 or 11, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **libmpv**: a `libmpv-2.dll` (x64) placed in `third_party/libmpv/`. It is not in the repository because of its size. Take it from the `mpv-dev-x86_64-*` archive of a Windows mpv build, for example <https://github.com/zhongfly/mpv-winbuild/releases> or <https://github.com/shinchiro/mpv-winbuild-cmake/releases>. The project builds without it (the player then reports that it is missing), and the tests that need it skip themselves.
- Optional: Python 3 (design-token generator and contrast check), FFmpeg on `PATH` or at `C:\ffmpeg\bin` (generates the test videos), [Inno Setup 6](https://jrsoftware.org/isinfo.php) (installer).
- Optional: the Garet typeface in `src/Lumina/Assets/Fonts/` (see the README). Without it the app uses Segoe UI.

## Build and run

```powershell
dotnet build -c Release
dotnet run --project src/Lumina
```

## Tests

```powershell
dotnet test
```

Nearly a thousand xUnit tests cover name parsing, the database and its migrations, settings, the Trakt, TMDB and OpenSubtitles clients against fake servers, the shortcut and key logic, and the controller (drawing at many sizes, hit regions, drag and click behaviour, driven through a real headless libmpv). Tests that need libmpv or FFmpeg skip themselves when they are absent. Tests redirect all data with the `LUMINA_*` environment variables, so they never touch your real library.

## The regression gate

`scripts/gate.ps1` is run before every release:

```powershell
powershell -File scripts\gate.ps1 -SkipUi            # build, tokens, tests, budgets
powershell -File scripts\gate.ps1 -Soak 30 -Package  # adds a 30 minute playback soak and the installer test
```

Stages: a warning-free build, the design-token and contrast check (dark and light), fixtures, unit and headless tests, a **real-input tour** (`scripts/e2e.ps1`), start-up and memory budgets, an optional soak, and an install, upgrade, downgrade-refusal and uninstall test of the packaged installer.

> **The real-input tour drives your actual mouse and keyboard** (it clicks every control of the app). Run it only when you are not using the computer, and do not touch the mouse while it runs. Use `-SkipUi` to leave it out.

## Design tokens

Edit `replica/design/tokens.json`, then:

```powershell
python replica/design/build_wpf.py        # regenerates Theme/Tokens.xaml and Settings/Palette.g.cs
python replica/design/contrast.py replica/design/tokens.json
```

## Brand and logos

`scripts/brand/make_brand.py` generates the logo, app icon, tray icons and splash from one geometry (Python with Pillow and fontTools), into `assets/brand`. Do not edit the outputs by hand. See [brand/asset-manifest.md](brand/asset-manifest.md).

## Packaging

```powershell
powershell -File scripts\package.ps1 -NoKeys
```

Writes `artifacts/Lumina-<version>-setup.exe`, `Lumina-<version>-portable.zip`, `SHA256SUMS.txt` and `latest.json` (the feed the in-app update check reads). The version comes from `src/Lumina/Lumina.csproj`. `-NoKeys` leaves out any baked-in API keys; use it for anything you publish.

## Conventions

- Every setting is a row in `Settings/Catalog.cs`; never read ad-hoc config keys.
- Database changes are appended migrations; config keys are only added.
- Every interactive control has an `AutomationProperties.Name`.
- A bug gets a regression test first, then the fix.
- Logic is unit tested; anything with libmpv gets a headless test (see `ModernStyleTests` for the pattern); anything clickable is covered by the real-input tour.
- Keep the build at zero warnings.

See also [CONTRIBUTING.md](../CONTRIBUTING.md).
