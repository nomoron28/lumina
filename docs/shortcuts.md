# Keyboard and mouse shortcuts

These are the defaults. Rebind any of them in **Settings, Controls**; Lumina warns about conflicts and can reset a key.

## Keyboard

| Key | Action |
|---|---|
| `Space` | Play or pause |
| `Left` / `Right` | Skip back / forward (10 s by default; set in Settings) |
| `Up` / `Down` | Volume up / down (5) |
| `m` | Mute |
| `f`, `F11` | Fullscreen |
| `Esc` | Leave fullscreen; then back to the page you came from |
| `N` / `P` | Next / previous item (also the media keys) |
| `v` or `j` | Next subtitle track |
| `b` or `#` | Next audio track |
| `Ctrl+B` | Search OpenSubtitles (shows a list of matches) |
| `c` | Next crop ratio (Default, 16:10, 16:9, 4:3, 1.85:1, 2.21:1, 2.35:1, 2.39:1, 5:3, 5:4, 1:1) |
| `a` | Next aspect ratio |
| `]` / `[` | Faster / slower in steps of 0.1 |
| `Backspace` | Normal speed |
| `.` / `,` | Step one frame forward / back |
| `Page Down` / `Page Up` | Next / previous chapter |
| `s` | Snapshot |
| `y` | Add or remove a bookmark at this moment |
| `h` | Hide or show the controller |
| `w` | Miniplayer |
| `i` | Playback statistics |
| `q` | Close the video |
| `Ctrl+U` | Open a network stream (outside the player) |

## Mouse

| Action | Result |
|---|---|
| Click, or hold and drag, on the seek bar | Seek, and scrub while held |
| Wheel over the seek bar | Step 5 s back or forward |
| Wheel elsewhere over the picture | Volume (or seek; set in Settings) |
| Click or drag on the volume slider | Set the volume |
| Double-click the picture | Fullscreen (set in Settings) |
| Click the picture | Nothing by default (can be set to play or pause) |
| Right-click a poster | Library menu |

## Touch

Tap to press, drag along the seek bar or volume slider to scrub, drag a finger to scroll.

## Command line

```
Lumina.exe [options] [files or addresses]
  --fullscreen, -f      start fullscreen
  --paused              start paused
  --start-time=SECONDS  start at a position
  --sub-file=PATH       load a subtitle file (repeatable)
  --background          start quietly in the tray or minimised
```

If Lumina is already running, the files are sent to the running window.
