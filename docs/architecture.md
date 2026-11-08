# Architecture

Lumina is a single WPF application (`src/Lumina`, `net10.0-windows`, x64) that embeds the mpv player (libmpv) and keeps its library in SQLite.

```
 ┌────────────────────────── Lumina.exe (WPF, .NET 10) ───────────────────────────┐
 │  MainWindow ── pages: Home · Library · Details · Folders · Playlists · Settings  │
 │       │                                                                          │
 │  Library ── Scanner → NameParser → Db (SQLite)  ◄──  Metadata: TMDB · OMDb       │
 │       │                                          ◄──  Sync: Trakt                │
 │  Player ── MpvHost (child HWND) ─ Mpv.cs (libmpv P/Invoke) ─ libmpv-2.dll        │
 │       │         ▲                                                                │
 │       │         └── lumina-osc.lua  (the on-screen controller, drawn with ASS)   │
 │  Settings ── Catalog (one table of every setting) → config.json → mpv options    │
 └──────────────────────────────────────────────────────────────────────────────────┘
```

## Source layout

| Folder | What lives there |
|---|---|
| `src/Lumina/Library` | `Scanner` (folder walking), `NameParser` (titles, years, seasons from file names), `Db` (SQLite with versioned migrations and a backup before each), `HomeShelves`, `PlaybackQueue`, `ParentalLock`, sorting and statistics |
| `src/Lumina/Metadata` | `Tmdb` (search, details, images, with a fallback host), `TitleDetail` (OMDb ratings), `Config` (reads `config.json`; baked-in keys fall back here) |
| `src/Lumina/Player` | `Mpv` (libmpv wrapper with a pump thread), `MpvHost` (the child window mpv draws in), `MouseForwarder` (WPF pointer to mpv), `Fullscreen`, `Shortcuts`, `Thumbnails`, `Recorder`, `SleepTimer`, `AudioFx` |
| `src/Lumina/scripts/lumina-osc.lua` | The on-screen controller: layout, drawing (ASS vector shapes), hit testing, panels, notes |
| `src/Lumina/Settings` | `Catalog` (every setting, its UI and the mpv options it maps to), `AppSettings`, `Accent`, `Profiles`, `SettingsView` (built from the catalog) |
| `src/Lumina/Subtitles`, `Sync` | OpenSubtitles client and the Trakt device-code flow |
| `src/Lumina/Ui`, `Theme` | Windows and controls, `SmoothScroll`, touch bridge helpers, generated `Tokens.xaml` and brand drawings |
| `src/Lumina/Windows` | Single instance, command-line parsing, file associations, taskbar and tray, `TaskbarReveal`, portable mode (`AppPaths`) |
| `installer/Lumina.iss` | The Inno Setup script (per-user, upgrade and downgrade handling, optional data removal) |
| `replica/design` | The design tokens (`tokens.json`) and the generator and contrast checker for `Tokens.xaml` and `Palette.g.cs` |
| `scripts` | `gate.ps1` (the regression gate), `e2e.ps1` (real-input UI tour), `package.ps1`, fixtures, fake servers |
| `tests/Lumina.Tests` | xUnit tests: logic, headless libmpv, fake servers, migrations, controller drawing |

## Ideas worth knowing

**One catalog for settings.** Every setting is a row in `Settings/Catalog.cs`: its key, label, control type, default, help text and the mpv option lines it produces. The Settings page, the config file handling, the mpv startup options and several tests are all driven from it, so a setting cannot exist in one place only.

**The controller is a script, not WPF.** mpv's video window is a native child window, and WPF cannot draw above it (the "airspace" problem). So the controller is a Lua script that draws into mpv's on-screen display as vector shapes. WPF forwards pointer, wheel and touch input to it (`MouseForwarder`, and a hook for touch), and it talks back with script messages (`lumina-*`). Every action is also reachable as `lumina-do <action>`, which is how the tests drive it without a window. mpv's own text banner is switched off; the script draws its own notes.

**Library data is versioned.** `Db` applies migrations in order and copies the database before upgrading. Config keys are only ever added, never repurposed.

**Design tokens, generated.** `replica/design/tokens.json` is the source of truth for colours, radii and type; `build_wpf.py` writes `Theme/Tokens.xaml` and `Palette.g.cs`, and `contrast.py` fails the gate if any text pair drops below WCAG AA in dark or light. The accent is recoloured at runtime from nine named swatches.

**Smooth playback defaults.** mpv is configured to sync video to audio (not to the display) and to use cheap scalers, because that stays smooth on integrated graphics at 4K, in HDR, and at 1.5x to 2x speed. High quality scaling and display sync are options.

**Keys.** TMDB, OMDb and OpenSubtitles keys are read from `config.json`. A local, untracked `secrets.local.json` can bake keys into a private build (`scripts/package.ps1` without `-NoKeys`); public builds use `-NoKeys`.

## Data

`config.json` and `lumina.db` live under `%APPDATA%\Lumina`; artwork, subtitles and thumbnails under `%LOCALAPPDATA%\Lumina`. A `portable.txt` beside the program moves everything into a `data` folder beside it. Tests redirect all of it with the `LUMINA_*` environment variables (`LUMINA_DB`, `LUMINA_CONFIG`, `LUMINA_ART`, `LUMINA_LOG`, `LUMINA_INSTANCE`, ...).
