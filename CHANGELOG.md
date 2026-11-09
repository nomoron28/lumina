# Changelog

All notable changes to Lumina are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- TV episodes named without an `S01E02` tag no longer land in Movies ([#1](https://github.com/nomoron28/lumina/issues/1)). Lumina now reads season folders (`Season 2`, `S03`, `Specials`), names like `Episode 12`, `Ep 03` or `E05`, and folders of three or more files numbered `01`, `02`, `03`. Your library is re-read once after updating.

### Added
- **Report a problem** in Settings, Advanced: opens a new GitHub issue with your Lumina and Windows versions filled in, and copies the end of today's log with file paths, your user name and any keys removed. Nothing is sent until you submit the issue.
- Right-click a title and choose **This is a TV show, not a movie** (or the reverse) when Lumina still files something wrongly. The choice is remembered across rescans.

### Changed
- A quieter interface around your artwork: lighter section headings, a small dot instead of the coloured corner on titles you have not started, an off-white Resume button, a neutral ring on the selected episode, and neutral step numbers in the welcome tour.
- The player shows both times in one format set by the length (`0:40:00 / 2:23:02` for a film, `4:05 / 21:30` for an episode). Episode headings read `E5 · Name`, and the Settings search has its magnifier.

## [0.1.0] - first public release

### Library
- Scans local folders and network shares; tells movies and TV shows apart from file names; rescans when folders change.
- Posters, backdrops, title logos, summaries, genres, certificates, cast and episode stills from TMDB; IMDb and Rotten Tomatoes scores through OMDb; falls back to TMDB's alias host when `api.themoviedb.org` is blocked.
- Home with continue watching, favourites, recently added, genres, unwatched and top rated; Movies and TV pages with filters, sort, grid and list views and poster sizes.
- Details pages with a resume button, round actions, episode row, cast row and file information.
- **Edit metadata** to fix a wrong match; mark watched, favourites, playlists, bookmarks, resume points; per-folder PIN lock.
- Optional two-way watched sync with Trakt (device-code sign-in with a client id only).

### Player
- libmpv engine with zero-copy hardware decoding (with fallback), HDR tone mapping, and bit-perfect high-resolution audio (WASAPI exclusive).
- A new **Modern controller**: one slim floating glass bar (transport on the left, seek bar in the middle, volume and tools on the right), with drag-to-scrub, touch support, seek-bar preview thumbnails (switchable in Settings and in the player), notes drawn in the app's typeface, and a Back button. VLC, Classic and Floating styles remain as options.
- OpenSubtitles search that shows a list of matches; subtitle size, position and delay remembered per video.
- Aspect ratio and crop pick-lists as in VLC, speed in 0.1 steps, A-B loop, chapters, equaliser and audio effects, recording, sleep timer, miniplayer.
- Playback kept in step with the sound by default, which is smooth at 1.5x to 2x speed and in 4K HDR; a cheap-scaler default for integrated graphics.

### App
- Nine named accent colours, dark, light or follow Windows, a frosted-glass sidebar (switchable), compact sidebar, profiles (Movies, Anime, Night and your own).
- Every keyboard shortcut can be rebound; command-line options (`--fullscreen`, `--paused`, `--start-time`, `--sub-file`).
- Touch scrolling and tapping throughout, including over the video.
- Per-user installer and portable zip; file associations, jump list, taskbar progress, media keys, tray, start with Windows, single instance, an auto-hide taskbar that can be revealed in fullscreen.
- Update check against a release feed (off by default), verified with a checksum.

[0.1.0]: https://github.com/nomoron28/lumina/releases/tag/v0.1.0
