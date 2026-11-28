# Changelog

All notable changes to Lumina are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Box sets**: when TMDB puts your films in a set, such as a trilogy, Lumina groups the ones you own in a box set tile in Home's Collections row, in release order. A set needs at least two of your films to show. Right-click a box set tile, **Hide this box set**, to get rid of one; **Show hidden box sets again** is in Settings, Library. After updating, Lumina asks TMDB about your films once more to learn their sets.
- Sort the library by **File size, largest first** (a show counts all its episodes), in Settings, Interface or the sort menu.
- **Use my own mpv.conf** (Settings, Advanced, off by default): Lumina also reads `%APPDATA%\mpv\mpv.conf`, after its own settings and before Extra mpv options. Lines that would break the window or controller are skipped and noted in the log.
- Drag the window from the empty space along the top of any page, not just the thin strip above it; double-click there to maximise or restore.

### Fixed
- The window now fits small screens: on a 1366×768 screen it used to open taller than the screen, with the title bar out of reach ([#34](https://github.com/nomoron28/lumina/issues/34)). It opens inside the usable area, and a saved size or position that hangs off the screen is pulled back in.
- **No ratings when TMDB is silently blocked**: if a network just drops api.themoviedb.org without refusing the connection, Lumina now gives up after a few seconds and tries api.tmdb.org instead of waiting 30 seconds and reporting "TMDB timed out".

## [0.3.0] - 2026-11-24

### Added
- **Skip intro, recap and credits**: a Skip button shows in the player while one plays. Lumina finds them from chapter names (Opening, Previously on, Ending) and from marks you set in the player's Bookmarks menu, **Skip marks**; a mark set on one episode counts for its whole season. Turn on **Skip intros and recaps on their own** in Settings, Playback, Skipping to skip without pressing anything (credits are never skipped on their own).
- **Up Next**: when an episode's credits start (or 30 seconds before the end), a card names the next episode and counts down 10 seconds before playing it. **Play now** goes straight away; **Cancel** stays on this one. It follows Settings, Playback, After an episode.
- **Touch swipes** over the picture: sideways to seek (a full width is 90 seconds), up and down on the right for volume, on the left for brightness.
- **Extras** on a title's page: trailers and bonus videos named the way Plex and Jellyfin name them (`Inception-trailer.mkv`, `trailer.mkv` in the film's folder, or files in `Extras`, `Trailers`, `Featurettes`, `Behind The Scenes`, `Deleted Scenes` and similar folders). Press one to play it.
- Right-click a show or an episode and choose **Move into show** to put it in another show's Specials or one of its seasons, for example specials that Lumina filed as a show of their own ([#16](https://github.com/nomoron28/lumina/issues/16)). The move is kept across rescans; **Make this its own show again** undoes it.
- **Collections**: right-click a film or show, **Add to collection**, and group titles your way, such as a Terminator collection ([#16](https://github.com/nomoron28/lumina/issues/16)). Collections get their own row on Home and list their titles in release order.
- **Customise Home**: hide Home's rows or change their order, from the link at the bottom of Home or in Settings, Interface ([#15](https://github.com/nomoron28/lumina/issues/15)).

### Fixed
- Trailers, featurettes and release samples no longer appear in the library as films of their own. They leave on the next scan.
- A show named like another show plus "Specials" or "OVA" (`One Punch Man Specials`) now becomes that show's Specials instead of a separate show. Your library is re-read once after updating.
- When TMDB turns down your key, Lumina now says so ("TMDB did not accept your key. Check it in Settings, Accounts") instead of "Response status code does not indicate success: 401 (Unauthorized)". Edit metadata's search says the same.
- Posters and backdrops were never downloaded when a show had a season TMDB numbers differently (common for anime): the "404 (Not Found)" for that season stopped the rest. Now that season is skipped and the artwork arrives.
- With an auto-hiding taskbar, a window you opened from the taskbar while Lumina was maximized or fullscreen was pushed back behind it a moment later. Lumina now takes the front back only when you look at the taskbar and move away without opening anything.

## [0.2.0] - 2026-11-20

### Added
- Install with Scoop: `scoop bucket add lumina https://github.com/nomoron28/lumina`, then `scoop install lumina/lumina`. Manifests for winget and Chocolatey are ready for submission.
- **Play with another player**: choose VLC, MPC-HC, MPC-BE, PotPlayer, mpv, mpv.net or any other program in Settings, Playback, Player, or right-click a title and pick **Play with** for one time. The player starts where you left off with your subtitle. VLC and mpv report where you stopped; with the others Lumina asks whether you finished.
- The welcome tour now sets up the TMDB, OMDb and OpenSubtitles keys: paste each one, press **Test** to see whether it works, or follow **Get a key** to sign up for free. A TMDB key added after a folder dresses the library straight away.

### Changed
- Lumina now checks for a newer version when it starts, at most once a day. It asks the release feed on GitHub and sends nothing else; a new version is only downloaded and installed if you say yes. Turn it off in Settings, Advanced, Updates. If you upgrade from 0.1, this is switched on for you as well.

## [0.1.1] - 2026-11-10

### Fixed
- TV episodes named without an `S01E02` tag no longer land in Movies ([#1](https://github.com/nomoron28/lumina/issues/1)). Lumina now reads season folders (`Season 2`, `S03`, `Specials`), names like `Episode 12`, `Ep 03` or `E05`, and folders of three or more files numbered `01`, `02`, `03`. Your library is re-read once after updating.
- A long file name no longer pushes the minimise, maximise and close buttons out of the window during playback; it is shortened with "…" instead.

### Added
- **Report a problem** in Settings, Advanced: opens a new GitHub issue with your Lumina and Windows versions filled in, and copies the end of today's log with file paths, your user name and any keys removed. Nothing is sent until you submit the issue.
- Right-click a title and choose **This is a TV show, not a movie** (or the reverse) when Lumina still files something wrongly. The choice is remembered across rescans.

### Changed
- A quieter interface around your artwork: lighter section headings, a small dot instead of the coloured corner on titles you have not started, an off-white Resume button, a neutral ring on the selected episode, and neutral step numbers in the welcome tour.
- The player shows both times in one format set by the length (`0:40:00 / 2:23:02` for a film, `4:05 / 21:30` for an episode). Episode headings read `E5 · Name`, and the Settings search has its magnifier.
- The player bar's buttons are grouped by kind: subtitles, audio and speed, then the queue and chapters (the same order as the side panel's tabs), then snapshot and settings, with fullscreen last. A custom order from Customise the controller is kept.

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

[0.3.0]: https://github.com/nomoron28/lumina/releases/tag/v0.3.0
[0.2.0]: https://github.com/nomoron28/lumina/releases/tag/v0.2.0
[0.1.1]: https://github.com/nomoron28/lumina/releases/tag/v0.1.1
[0.1.0]: https://github.com/nomoron28/lumina/releases/tag/v0.1.0
