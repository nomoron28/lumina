# Third-party notices

Lumina is released under the GNU General Public License v3.0 or later (see [LICENSE](LICENSE)). It uses and ships the following. Each remains under its own licence.

## Bundled in the installer and the portable zip

| Component | Used for | Licence |
|---|---|---|
| [mpv / libmpv](https://mpv.io/) (`libmpv-2.dll`) and the libraries inside the Windows build ([FFmpeg](https://ffmpeg.org/), libass, libplacebo, and others) | the playback engine | GPL-2.0-or-later or LGPL-2.1-or-later depending on the build; the build Lumina ships is a GPL build. Source: <https://github.com/mpv-player/mpv> and the build recipe of the distributor you take the DLL from |
| [.NET runtime and WPF](https://dotnet.microsoft.com/) | the application platform (self-contained) | MIT |
| [WPF-UI](https://github.com/lepoco/wpfui) 4.x | window chrome, icons, the Fluent look | MIT |
| [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) and SQLitePCLRaw / SQLite | the library database | MIT / public domain |
| [Garet](https://spacetype.co) typeface (Book and Heavy), *official releases only* | the app's typeface | Spacetype EULA for Free Fonts: allowed in applications, embedded in software products; the font files must not be redistributed on their own. **The font files are not in this repository** |
| [Inno Setup](https://jrsoftware.org/isinfo.php) | builds the installer (not shipped inside it) | Inno Setup licence |

If you received a Lumina binary, you may ask for the corresponding source of every GPL component in it; for Lumina itself it is this repository at the matching tag, and for libmpv and FFmpeg it is the project linked above for the build that was bundled.

## Web services (used only if you configure them)

- **TMDB**: *This product uses the TMDB API but is not endorsed or certified by TMDB.* Movie and TV data and images come from [The Movie Database](https://www.themoviedb.org/). Use of the API is subject to the [TMDB terms](https://www.themoviedb.org/api-terms-of-use).
- **OMDb**: ratings from the [Open Movie Database](https://www.omdbapi.com/).
- **OpenSubtitles**: subtitles from [OpenSubtitles](https://www.opensubtitles.com/), subject to their terms.
- **Trakt**: watched sync with [Trakt](https://trakt.tv/).

## Marks and logos

The IMDb, TMDB and Rotten Tomatoes marks shown beside ratings on details pages belong to their owners and are used only to identify where a score comes from. Rotten Tomatoes' Fresh and Rotten marks are shown only with a score that qualifies (60% and above, or 59% and below, respectively), following its usage rules. Lumina is not affiliated with or endorsed by any of these companies. If you redistribute Lumina, check the current brand guidelines of each.

## Design and artwork

The Lumina name, aperture logo, icons and splash are original work released with the project under the same licence as the code, except that the Lumina name and logo may not be used to suggest that a modified version is the official release.
