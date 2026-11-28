# User guide

## The library

**Home** shows what to watch next: *Continue watching*, favourites, *Recently added*, film and TV genres, unwatched and top rated. Each row has a **See all** link. **Customise Home**, at the bottom of Home or in Settings, Interface, hides rows and changes their order.

**Collections** group titles your way, such as every Terminator film. Right-click a film or show, **Add to collection**, then pick one or make a **New collection**. Collections appear as a row on Home, in release order inside; right-click one to rename or delete it (the titles stay in your library).

**Movies** and **TV shows** list your titles as posters. A small dot in the corner means you have not started it yet. Use the filter chips (All, Unwatched, In progress), the sort menu (A to Z, recently added or watched, year, rating, file size), and the grid or list and S, M, L size buttons.

**Search** (top of the sidebar) finds titles as you type.

**Folders** lists the folders Lumina watches, with counts, a **Scan now** button, **Refresh metadata**, and per-folder **Subtitles** and **Lock** (a PIN) buttons. Turn the Movies and TV shows sections on or off here.

**Playlists** holds named playlists; the queue in the player can be saved as one.

### Right-click menu

On a poster: play, **Play with** (Lumina or another player found on your PC, just this once), mark watched or unwatched, add to favourites, add to a playlist, **Refresh info**, **Edit metadata...**, find subtitles, show in Explorer, remove.

**This is a TV show, not a movie** (and **This is a movie, not a show**) fixes a title filed under the wrong kind. For a film it marks the file's whole folder as a show, numbering episodes from their names or, failing that, in name order; the choice sticks across rescans.

**Edit metadata...** fixes a wrong match. Search TMDB by hand, see the candidates with their release dates, and pick one (or *None* to use only what the file name says).

## Details pages

A details page shows the title art, a **Play** or **Resume** button with progress, round actions (mark watched, favourite, add to playlist, more), runtime, year, certificate, genres, the summary, ratings from TMDB, IMDb and Rotten Tomatoes, the episodes (stills with a selection ring and an unwatched marker), the cast and crew, and the technical details of the file.

Scroll the episode and cast rows with the mouse wheel, Shift+wheel, or a touchpad swipe.

## The player

Move the mouse to show the controller; it hides itself when idle, and so does the pointer. **Back** (top left, or Esc) leaves the video and returns to the page you came from. Esc first leaves fullscreen.

The bar has three parts:

- **Left**: previous, back 10 s, Play/Pause, forward 10 s, next.
- **Middle**: the seek bar with elapsed and total time. Click or hold and drag to scrub; scroll the wheel over it to step. With **Seek-bar preview** on, a thumbnail shows the picture at the pointer.
- **Right**: volume (click the speaker to mute, drag the slider), subtitles, audio, speed, queue, settings, snapshot and fullscreen.

The **settings panel** (gear) has four tabs:

- *Playback*: speed, A-B loop, jump to time, sleep timer, recording, miniplayer, media information, bookmarks, and the seek-bar preview switch.
- *Video*: zoom, rotate, **aspect ratio** and **crop** pick-lists as in VLC, colour, HDR options.
- *Audio*: tracks, output device, delay, equaliser, compressor, spatial sound, loudness.
- *Subtitles*: tracks, online search, size, position, delay.

**Skip intro, recap and credits.** A **Skip intro** (or recap, or credits) button shows at the bottom right while one plays. Lumina finds them from chapter names such as *Opening*, *Previously on* and *Ending*. For files without such chapters, open *Bookmarks*, **Skip marks**, and press **Intro starts here** and **Intro ends here** at the right moments (and the same for the recap and the credits); the marks count for every episode of that season. **Settings, Playback, Skipping** can skip intros and recaps without asking; credits are always left to you.

**Up Next.** Where an episode's credits start (or 30 seconds before the end), a card shows the next episode and counts down 10 seconds of playback; pausing holds it. **Play now** skips the wait and **Cancel** stays on this episode. Set **Settings, Playback, After an episode** to *Stop and stay* to turn it off.

Already have an mpv setup? Turn on **Settings, Advanced, Use my own mpv.conf** and Lumina also reads `%APPDATA%\mpv\mpv.conf` the next time it starts. Lines about windows, scripts, the controller and `[profile]` sections are skipped (see the log); **Extra mpv options** still win.

Controller styles (**Settings, Interface**): **Modern** (default), VLC (a flat bar under the picture), Classic, and a Floating pill. The set of buttons can be customised.

### Subtitles

Pick an embedded or sidecar track from the panel, or press `v` to cycle. **Find subtitles** (`Ctrl+B`) searches OpenSubtitles and shows a **list** of matches for you to choose from. If one is out of sync or for another release, search again and pick a different one. The one you pick is remembered for that video.

### Speed

`]` and `[` change speed in steps of 0.1, and `Backspace` returns to normal. Playback is kept in step with the sound, which stays smooth at 1.5x and 2x.

### Touch

On a touchscreen: tap the controls, drag along the seek bar and the volume slider, and drag a finger to scroll pages and rows. Over the picture, swipe sideways to seek (the whole width is 90 seconds), up or down on the right third for volume, and on the left third for brightness.

### Fullscreen

`F` or `F11`, or double-click the picture. The controller stays hidden until the pointer moves. If your taskbar is set to auto-hide, resting the pointer at its screen edge brings it up.

### Miniplayer

`W` shrinks the player to a small always-on-top window with a queue card; press it again to return.

### Playing in another player

Prefer VLC, MPC-HC, MPC-BE, PotPlayer, mpv or mpv.net? Choose it in **Settings, Playback, Player, Play videos with**, and Lumina hands every video from the library to that player, starting where you left off and passing a downloaded subtitle along. Lumina finds the player itself; if it is somewhere unusual, set **Player program**. **Another program** takes any player with a command line you write (`{file}`, `{start}`, `{startms}`, `{sub}`).

VLC and mpv tell Lumina where you stopped, so resume points and watched marks work as usual. MPC, PotPlayer and other programs cannot, so when they close Lumina asks whether you finished. Lumina's own player is still used for streams and inside the player (next episode, the queue).

## Privacy

There is no account and no telemetry. Lumina contacts only the services you configure (TMDB for artwork, OMDb for ratings, OpenSubtitles, Trakt) and, once a day at most, the release feed to check for a newer version (you can turn this off in Settings, Advanced). See the [FAQ](troubleshooting.md#faq).
