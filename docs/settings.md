# Settings reference

Open **Settings** from the sidebar. The search box at the top finds any setting by name. Every change applies immediately, and settings are stored in `config.json` (see [data locations](troubleshooting.md#where-lumina-keeps-its-data)).

## App

**General**: theme (dark, light, or match Windows); accent colour (nine named swatches; the Lumina logo keeps its amber); the welcome tour; confirm before removing things; keep Lumina in the tray when the window closes; start with Windows; taskbar progress and jump list; **Open with** registration and default-player choice; open videos in the running Lumina; scan folders on start.

**Interface**: frosted-glass sidebar (a blurred translucent sidebar; switch it off if graphics performance matters; it is automatically off while a video plays); compact sidebar; library sort, view and poster size; remember window size and position; keep the window on top; show the file name in the title bar; hide the player controls after a delay; **seek-bar preview**; **controller style** (Modern, VLC, Classic, Floating) and which buttons the controller shows.

**Privacy**: parental PIN and folder locks; remember where you stopped and clear history; whether to fetch artwork from TMDB and whether to send what you watch to Trakt.

**Controls**: skip amount; what a click, a double-click and the wheel do on the video; rebind every keyboard shortcut (with conflict detection) or edit the key list as text.

## Media

**Playback**: profiles (Movies, Anime, Night, and your own; export and import as JSON); continue where you left off; start paused; default speed; queue repeat and shuffle; what the sleep timer does; what happens after an episode ends; seek precision; preferred audio language.

**Video**: start in fullscreen; keep the screen awake; picture adjustments; deinterlacing; aspect ratio; HDR tone mapping; **picture scaling** (Fast, the default, or High quality); smooth motion.

**Audio**: maximum and startup volume; a 10-band equaliser with presets; compressor, spatial sound and stereo width; loudness levelling, night mode and replay gain; speaker setup; output device; **high-resolution audio (bit-perfect)** through WASAPI exclusive; pass surround sound to a receiver.

**Subtitles**: show by default; preferred languages; load files next to the video; text encoding; font, size, bold, outline, shadow and position.

**Library**: show movies, show TV shows, manage folders, restore hidden titles, scan now.

## System

**Network**: saved logins for network shares (kept in Windows Credential Manager, not in the config file); where recordings go; buffer size; HTTP proxy; verify secure connections.

**Performance**: hardware decoding (zero-copy Direct3D 11 with fallback, copy, or off); decoding threads; **keep the picture in step with** the sound (default, light on graphics) or the screen; what to do when the system cannot keep up; video renderer (gpu-next or gpu).

**Snapshots**: folder and format for screenshots.

**Accounts**: your TMDB, OpenSubtitles and OMDb keys, and Trakt client id (and an optional secret).

**Advanced**: update checks and the update address; About and log files; extra mpv options, one `key=value` per line, applied last.

## Profiles

A profile is a named bundle of settings (for example *Night*: quieter, more compression). Applying one can be undone, and it restores the exact previous values.

## Editing the file directly

`config.json` is plain JSON with string values; unknown keys are kept, and keys are only ever added in new versions, so a config from an older release still loads. API keys are the only secrets in it.
