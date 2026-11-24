# Roadmap

Where Lumina is heading, release by release. A new version ships roughly every 2-3 weeks; dates are targets, not
promises, and the order may change based on [issues](https://github.com/nomoron28/lumina/issues) and feedback.
Each release keeps room for bug fixes. Finished work moves to the [CHANGELOG](CHANGELOG.md).

| Version | Theme | Highlights | Target |
|---|---|---|---|
| **0.1.1** | Steady | Better TV-show detection for loosely named files, "This is a show / movie" switch, Report a problem, release builds in CI | Released 11 Nov 2026 |
| **0.2** | Easy start | First-run setup for TMDB, OMDb and OpenSubtitles keys; **play with another player** (VLC, PotPlayer, MPC-HC/BE, mpv, custom); winget, Scoop and Chocolatey (Scoop works now; winget and Chocolatey are with their moderators); signed installer (waiting on SignPath) | Released 20 Nov 2026 |
| **0.3** | Skip the boring bits | Skip intro, recap and credits (from chapters and marks you set); Up Next countdown; local extras and trailers; touch swipe gestures; reorder and hide Home rows ([#15](https://github.com/nomoron28/lumina/issues/15)); move specials into their show and make your own collections ([#16](https://github.com/nomoron28/lumina/issues/16)) | Released 24 Nov 2026 |
| **0.4** | Collections | Automatic box sets from TMDB, multiple versions of a film, smart playlists, cast pages; edit any title's metadata by hand (title, year, summary, genres, poster and backdrop), kept when Lumina refreshes from TMDB | Jan 2027 |
| **0.5** | Speaks your language | Translatable interface with community translations; an **Anime** section (anime found from TMDB, with a right-click switch for the odd one out) that plays with Japanese audio and anime subtitles | Feb 2027 |
| **0.6** | Your servers, part 1 | Jellyfin and Emby libraries, with progress synced back | Feb 2027 |
| **0.7** | Your servers, part 2 | Plex and DLNA/UPnP libraries | Mar 2027 |
| **0.8** | Cloud and debrid | OneDrive and Google Drive; **TorBox, Real-Debrid, AllDebrid and Premiumize** accounts | Apr 2027 |
| **0.9** | Cast it | Chromecast and DLNA TVs | Apr 2027 |
| **0.10** | Music | Music library, gapless playback, ReplayGain, lyrics, visualiser | May 2027 |
| **1.0** | Trust | Profiles for each person, backup and restore, history import from Plex, Jellyfin and Kodi | Jun 2027 |
| **1.1** | **Lumina for Linux** (preview) | AppImage and Flatpak, same player and controller | Jun 2027 |
| **1.2** | Picture quality | FSR, Anime4K and RTX/Intel video upscaling, frame interpolation, refresh-rate and HDR switching | Jul 2027 |
| **1.3** | Linux at parity | Every feature on Linux, published on Flathub | Aug 2027 |
| **1.4** | Together | Watch together, phone remote | Aug 2027 |
| **1.5** | Live TV | IPTV (M3U) with a programme guide, recording | Sep 2027 |
| **1.6** | Discover | Upcoming-episodes calendar, notifications, private recommendations from your own library | Oct 2027 |
| **1.7** | Couch | TV mode for living-room PCs, gamepad and remote control | Oct 2027 |
| **1.8** | Everywhere | Sync progress and settings between your own computers, no account needed | Nov 2027 |
| **1.9** | One UI beta | The new cross-platform interface on Windows, beta update channel | Dec 2027 |
| **2.0** | One Lumina | One app for Windows and Linux, design refresh, TV mode | Jan 2028 |

1.6 to 2.0 are sketched and will be re-planned after 1.3.

## A few details

- **Skipping.** 0.3 finds intros, recaps and credits from chapter names and from marks you set once for a season.
  Finding them by listening to the audio comes in a later release.
- **Other players.** Lumina's own player stays the default. You can choose VLC, PotPlayer, MPC-HC/BE, mpv or any
  program instead, for everything or for one title. Lumina passes the resume point and subtitles, and keeps your
  progress where the player allows it.
- **Debrid.** Lumina plays files that are already in your own debrid account, like any other cloud source. It will not
  include torrent search, indexers or scrapers.
- **Linux.** The library, metadata and the player controller are shared. The interface is rebuilt in
  [Avalonia](https://avaloniaui.net/) and runs on X11 and Wayland.

## Not planned for now

A plugin API (features are built into Lumina instead), torrent search, DVD and Blu-ray discs, converting videos,
and podcasts. If one of these matters to you, open an issue: these are revisited at 2.0.
