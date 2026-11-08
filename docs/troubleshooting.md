# Troubleshooting and FAQ

## Playback stutters

Lumina is tuned for smooth playback on modest graphics hardware, but a few settings matter most:

1. **Settings, Performance, Keep the picture in step with**: leave it on *The sound* (the default). The *screen* option redraws on every refresh, which is five times the work on a 120 Hz display and cannot lock at 1.5x speed.
2. **Hardware decoding**: leave it on *Direct3D 11, zero-copy (recommended)*. If a driver has trouble, choose *copy*.
3. **Settings, Video, Picture scaling**: *Fast* (default) keeps playback smooth while other programs are busy. *High quality* looks sharper but can stutter on integrated graphics.
4. **Settings, Interface, Frosted-glass sidebar**: off if you suspect it. It is switched off by itself while a video plays.
5. Smooth motion (frame blending) costs graphics time; keep it off on integrated graphics.

Press `i` during playback for statistics (dropped frames, decoder, and so on) when reporting a problem.

## No posters or summaries

- Add a TMDB key in **Settings, Accounts** (see [getting started](getting-started.md#keys-for-artwork-ratings-and-subtitles)), then **Folders, Refresh metadata**.
- Check the file names: `Movie Name (2014).mkv`, `Show Name S01E02.mkv`. Right-click a wrong match and choose **Edit metadata...**.
- Some networks block `api.themoviedb.org`. Lumina automatically retries on TMDB's alias `api.tmdb.org`; if both are blocked, use a VPN or another DNS server.

## No subtitles found

Add an OpenSubtitles key in **Settings, Accounts**, check **Settings, Subtitles, Preferred languages**, and press `Ctrl+B` in the player to see the list of matches.

## SmartScreen says "Windows protected your PC"

The installer is not code-signed. Choose **More info**, then **Run anyway**. Compare the file's SHA-256 with `SHA256SUMS.txt` on the release page if you want to be sure it is the published file:

```powershell
Get-FileHash .\Lumina-0.1.0-setup.exe -Algorithm SHA256
```

## The auto-hiding taskbar does not appear in fullscreen

Rest the pointer at the screen edge where the taskbar lives for a moment; Lumina brings it up and gives focus back when you move away.

## Where Lumina keeps its data

| What | Installed | Portable build |
|---|---|---|
| Settings (`config.json`), library (`lumina.db`), logs | `%APPDATA%\Lumina` | `data\` beside the program |
| Artwork, downloaded subtitles, seek-bar thumbnails | `%LOCALAPPDATA%\Lumina` | `data\cache\` |

Delete `lumina.db` to start the library afresh (your files are never touched). Logs are in the `logs` folder; **Settings, Advanced, Log files** opens it.

## FAQ

**Does Lumina change my files?** No. It reads them. It never moves, renames, tags or uploads them.

**What does Lumina send over the network?** Only what you set up: titles and years to TMDB (artwork and details) and OMDb (ratings), a file hash and title to OpenSubtitles when you search, watched status to Trakt if you connect it, and, only if you turn update checks on, a request to the release address. No account, no analytics, no crash reports.

**Is my TMDB key safe?** It is stored in `config.json` on your computer and sent only to TMDB. Network share passwords are stored in Windows Credential Manager.

**Why is there no bundled API key?** Free keys are rate-limited per key. A shared key would be limited for everyone and could be revoked. Getting your own takes a minute.

**Can I use it without any keys?** Yes: the library still lists and plays everything, with titles from the file names.

**Which formats play?** Anything libmpv and FFmpeg open: all common video and audio formats, DVD and Blu-ray ISOs, network streams and HDR10.

**Does it support HDR displays?** Yes. With Windows HDR switched on, HDR video is passed to the display; on a normal display it is tone mapped (choose the method under Settings, Video).

**Can I make it look different?** Nine accents, dark or light, four controller styles, a compact sidebar and adjustable poster sizes.

**Windows 7, 8, macOS or Linux?** Not supported. Lumina needs Windows 10 version 1809 or newer.
