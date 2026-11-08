# Getting started

## Install

Download from the [latest release](https://github.com/nomoron28/lumina/releases/latest):

- **`Lumina-<version>-setup.exe`**: a per-user installer. It needs no administrator rights and installs to `%LOCALAPPDATA%\Programs\Lumina`. Uninstalling asks whether to keep your library and settings.
- **`Lumina-<version>-portable.zip`**: unzip anywhere. A file named `portable.txt` sits next to the program, which makes Lumina keep everything (settings, library, artwork cache) in a `data` folder beside it, so the whole thing can live on a USB stick.

Requirements: Windows 10 version 1809 or newer, 64-bit. The .NET runtime is included in both.

The installer is not code-signed yet. Windows SmartScreen will say "Windows protected your PC": choose **More info**, then **Run anyway**. Each release lists SHA-256 checksums in `SHA256SUMS.txt`.

## First run

A short welcome screen offers three optional steps:

1. **Choose a folder** where your films and shows live. A network share works too (`\\nas\media`). Lumina only reads it.
2. **Keys** for posters and subtitles (below). You can skip this and add them later.
3. **Appearance**: dark, light or follow Windows, and one of nine accent colours.

Lumina then scans the folder. Movies and shows are told apart from file names such as `Movie Name (2014).mkv` and `Show Name S02E05.mkv`; nested folders are fine.

## Keys for artwork, ratings and subtitles

The services Lumina uses are free but ask each user for their own key, so none is bundled. Open **Settings, Accounts**:

| Service | What you get | Key |
|---|---|---|
| TMDB | posters, backdrops, summaries, cast, episode stills | <https://www.themoviedb.org/settings/api> |
| OMDb | IMDb and Rotten Tomatoes scores | <https://www.omdbapi.com/apikey.aspx> |
| OpenSubtitles | subtitle search and download | <https://www.opensubtitles.com/consumers> |
| Trakt | two-way watched status; sign in with a device code | <https://trakt.tv/oauth/applications> (client id) |

Paste the key, and Lumina fills in what is missing the next time it scans (or choose **Refresh metadata** on the Folders page). Keys are stored in `config.json` on your computer.

## Playing something

- Double-click a poster, or press **Play** on a details page. Lumina resumes where you left off.
- **Open file** and **Open stream** (Ctrl+U, a web address) on the Home page play anything without adding it to the library.
- Right-click a video in Explorer, **Open with Lumina**, or make it the default player from **Settings, General, Windows**.
- From a command prompt: `Lumina.exe movie.mkv --fullscreen --start-time=95 --sub-file=movie.en.srt --paused`.

Next: the [user guide](user-guide.md) and the [shortcut list](shortcuts.md).
