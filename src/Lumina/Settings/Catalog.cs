namespace Lumina.Settings;

/// <summary>
/// Every setting Lumina offers, in the order the page shows them. A row is only here if it does something:
/// either it maps to an mpv option (see Mpv) or an app behaviour that reads it through AppSettings.
/// </summary>
public static class Catalog
{
    /// <summary>The nine interface accents, in the order the picker shows them. The aperture logo never takes any of these colours.</summary>
    public static readonly (string Hex, string Name)[] AccentThemes =
    {
        ("5CE1C6", "Mint"), ("6CB8FF", "Sky"), ("F5B84B", "Amber"), ("F28C1E", "Orange"), ("FF8A7A", "Coral"),
        ("FF7BAC", "Pink"), ("B8E65A", "Lime"), ("A99BFF", "Lilac"), ("D9DEE8", "Neutral"),
    };

    public static readonly string[] AccentSwatches = AccentThemes.Select(t => t.Hex).ToArray();

    public static string AccentName(string hex) => AccentThemes.FirstOrDefault(t => string.Equals(t.Hex, hex, StringComparison.OrdinalIgnoreCase)).Name ?? "#" + hex;

    public static readonly IReadOnlyList<Category> Categories = new[]
    {
        new Category("general", "App", "General", "General", "Options24"),
        new Category("interface", "App", "Interface", "Interface", "Cursor24"),
        new Category("privacy", "App", "Privacy", "Privacy", "ShieldKeyhole24"),
        new Category("controls", "App", "Controls", "Mouse, keyboard and skipping", "Keyboard24"),
        new Category("playback", "Media", "Playback", "Playback", "Play24"),
        new Category("video", "Media", "Video", "Video", "Video24"),
        new Category("audio", "Media", "Audio", "Audio", "Speaker224"),
        new Category("subtitles", "Media", "Subtitles", "Subtitles", "ClosedCaption24"),
        new Category("library", "Media", "Library", "Library", "FolderOpen24"),
        new Category("network", "System", "Network", "Network", "Wifi124"),
        new Category("performance", "System", "Performance", "Performance", "Flash24"),
        new Category("snapshots", "System", "Snapshots", "Snapshots", "Camera24"),
        new Category("accounts", "System", "Accounts", "Accounts and keys", "Person24"),
        new Category("advanced", "System", "Advanced", "Advanced", "Wrench24"),
    };

    static Choice[] Ch(params string[] pairs) =>
        pairs.Select(p => { var i = p.IndexOf('|'); return new Choice(p[..i], p[(i + 1)..]); }).ToArray();

    static Func<string, string[]> Opt(string name, Func<string, string>? f = null) => v => new[] { $"{name}={f?.Invoke(v) ?? v}" };
    static Func<string, string[]> YesNo(string name) => v => new[] { $"{name}={(v == "true" ? "yes" : "no")}" };

    static SettingDef Toggle(string key, string cat, string sec, string label, bool def, string? help = null, Func<string, string[]>? mpv = null) =>
        new(key, cat, sec, label, Kind.Toggle, def ? "true" : "false", help, Mpv: mpv);

    static SettingDef Choice(string key, string cat, string sec, string label, string def, Choice[] choices, string? help = null,
        Func<string, string[]>? mpv = null, bool segmented = false) =>
        new(key, cat, sec, label, segmented ? Kind.Segmented : Kind.Choice, def, help, choices, Mpv: mpv);

    static SettingDef Slider(string key, string cat, string sec, string label, double def, double min, double max, double step,
        string unit = "", string? help = null, Func<string, string[]>? mpv = null) =>
        new(key, cat, sec, label, Kind.Slider, Num(def), help, null, min, max, step, unit, mpv);

    static SettingDef Text(string key, string cat, string sec, string label, string def, string? help = null, string? placeholder = null,
        Func<string, string[]>? mpv = null, Kind kind = Kind.Text) =>
        new(key, cat, sec, label, kind, def, help, Mpv: mpv, Placeholder: placeholder);

    static SettingDef Color(string key, string cat, string sec, string label, string def, Func<string, string[]>? mpv = null) =>
        new(key, cat, sec, label, Kind.Color, def, Mpv: mpv);

    static SettingDef Button(string key, string cat, string sec, string label, string action, string? help = null) =>
        new(key, cat, sec, label, Kind.Button, "", help, Action: action);

    public static string Num(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    public static readonly IReadOnlyList<SettingDef> All = new SettingDef[]
    {
        // ---------------- General ----------------
        Choice("general.theme", "general", "Appearance", "Theme", "dark", Ch("dark|Dark", "light|Light", "system|Match Windows"),
            "Matches the Windows setting when set to Match Windows. The player controls always stay dark over the picture.", segmented: true),
        new("general.accent", "general", "Appearance", "Accent colour", Kind.Swatches, "5CE1C6",
            "Used for progress, selection and the player controls. The Lumina logo keeps its amber whichever you choose."),
        Toggle("general.tour", "general", "Dialogs", "Show the welcome tour at next start", true,
            "Turns itself off after you have seen it. Switch it on to see it again."),
        Toggle("general.confirm", "general", "Dialogs", "Ask before removing things", true,
            "Folders and watch history ask first. Turn off to skip the prompt."),
        Toggle("general.tray", "general", "Windows", "Keep Lumina in the tray when its window is closed", false,
            "The close button hides the window and pauses the video. Click the tray icon to open it again, or use its menu to quit."),
        Toggle("general.startWithWindows", "general", "Windows", "Start Lumina when I sign in", false,
            "Starts quietly in the tray (or minimised if the tray option is off). Turn this off to remove it again."),
        Toggle("general.singleInstance", "general", "Startup", "Open videos in the Lumina that is already running", true,
            "Double-clicking a video while Lumina is open plays it there instead of opening a second window. Takes effect next launch."),
        Toggle("general.taskbar", "general", "Windows", "Show progress on the taskbar button", true,
            "The button fills as the video plays and turns yellow when paused."),
        Toggle("general.jumpList", "general", "Windows", "List recent videos on the taskbar icon", true,
            "Right-click the taskbar icon to reopen something you played. Turning this off clears the list."),
        Button("general.assocAdd", "general", "Windows", "Show Lumina in Open with", "assoc-add",
            "Adds Lumina to the Open with list for video files. Only for your Windows account, no admin rights needed."),
        Button("general.assocDefault", "general", "Windows", "Choose Lumina as the default player", "assoc-default",
            "Opens Windows Default apps, where you pick Lumina. Windows does not let an app do this for you."),
        Button("general.assocRemove", "general", "Windows", "Remove Lumina from Open with", "assoc-remove"),
        Toggle("general.scanOnStart", "general", "Startup", "Scan folders when Lumina starts", true,
            "Off means new files appear only after you press Rescan."),

        // ---------------- Interface ----------------
        Toggle("interface.translucency", "interface", "Look", "Frosted-glass sidebar", true,
            "Lets the desktop show through the sidebar, blurred, as in Windows 11. It is switched off by itself while a video plays. If anything stutters, turn it off here."),
        Toggle("interface.compactSidebar", "interface", "Layout", "Use the compact sidebar", false,
            "Icons only. The button at the top of the sidebar does the same."),
        Choice("interface.librarySort", "interface", "Library", "Sort titles by", "title",
            Lumina.Library.LibrarySort.Choices, "Also on the Movies and TV shows pages."),
        Choice("interface.libraryView", "interface", "Library", "Show titles as", "grid", Ch("grid|Posters", "list|List"), segmented: true),
        Choice("interface.gridSize", "interface", "Library", "Poster size", "medium", Ch("small|Small", "medium|Medium", "large|Large"),
            "Used for the poster view.", segmented: true),
        Toggle("interface.rememberWindow", "interface", "Window", "Remember window size and position", true),
        Toggle("interface.keepOnTop", "interface", "Window", "Keep the window above others", false),
        Toggle("interface.showFileName", "interface", "Window", "Show the file name in the title bar", true),
        Choice("interface.autoHide", "interface", "Player controls", "Hide the controls", "always",
            Ch("always|Always", "fullscreen|In fullscreen only", "never|Never"),
            "Controls always show while paused."),
        Toggle("interface.seekThumbs", "interface", "Controller", "Preview picture on the seek bar", true,
            "Point at the seek bar, or drag along it, to see the picture at that time. Turn this off to scrub with just the time. It can also be switched from the player: Settings panel, Playback, Seek-bar preview. Lumina makes the pictures quietly in the background the first time you play a file, and keeps up to 300 MB."),
        Choice("interface.controllerStyle", "interface", "Controller", "Controller style", "modern", Ch("modern|Modern", "vlc|VLC", "classic|Classic bar", "floating|Floating pill"),
            "Modern is a floating glass panel over the picture that fades away when idle. VLC is a flat bar under the picture with a tabbed panel on the right. Classic is a full-width bar over the picture. Floating is a rounded bar at the bottom centre with a Close button at the top left.", segmented: true),
        Button("interface.controller", "interface", "Controller", "Customise the controller", "edit-controller",
            "Choose which buttons the on-screen controller shows and in what order."),
        Text("interface.controls", "interface", "Controller", "Controller buttons (advanced)", "cc+audio+speed+queue+chap+snap+gear",
            "What Customise the controller saves. Names joined with +, left to right. The gear always stays.", "chap+cc+audio"),
        Slider("interface.hideAfter", "interface", "Player controls", "Hide after", 2.6, 1, 10, 0.5, "s"),

        // ---------------- Privacy ----------------
        Button("privacy.pin", "privacy", "Parental lock", "PIN", "set-pin",
            "A four to eight digit PIN. Lock folders from the Folders page: their videos disappear from Lumina and ask for the PIN to play."),
        Button("privacy.removePin", "privacy", "Parental lock", "Remove the PIN", "remove-pin", "Needs your PIN. Every folder is unlocked."),
        Button("privacy.lockNow", "privacy", "Parental lock", "Lock now", "lock-now", "Hides the locked folders again until the PIN is entered."),
        Toggle("privacy.rememberPosition", "privacy", "History", "Remember where you stopped", true,
            "Off means every title starts from the beginning and nothing is saved."),
        Toggle("privacy.fetchArtwork", "privacy", "Network", "Fetch artwork and details from TMDB", true,
            "Off keeps Lumina fully offline for metadata. File names are never sent anywhere else."),
        Toggle("privacy.scrobble", "privacy", "Network", "Send what you watch to Trakt", true,
            "Only applies while Trakt is connected."),
        Button("privacy.clearHistory", "privacy", "History", "Clear watch history", "clear-history",
            "Marks everything unwatched and forgets resume positions. Files and artwork stay."),

        // ---------------- Controls ----------------
        Choice("controls.skip", "controls", "Skipping", "Skip amount", "10", Ch("5|5 s", "10|10 s", "15|15 s", "30|30 s"),
            "Used by the skip buttons and the left and right arrow keys.", segmented: true),
        Choice("controls.click", "controls", "Mouse", "Click on the video", "none", Ch("pause|Play or pause", "none|Nothing")),
        Choice("controls.doubleClick", "controls", "Mouse", "Double-click on the video", "fullscreen",
            Ch("fullscreen|Toggle fullscreen", "pause|Play or pause", "none|Nothing")),
        Choice("controls.wheel", "controls", "Mouse", "Scroll wheel", "volume", Ch("volume|Volume", "seek|Seek", "none|Nothing")),
        Button("controls.customKeys", "controls", "Keyboard", "Customise keys", "edit-keys", "Choose your own key for each action. Clashes are caught for you."),
        Text("shortcuts", "controls", "Keyboard", "Custom keys (advanced)", "", "What Customise keys saves, as text. Clear it to go back to the standard keys.", "{\"playpause\":\"k\"}", kind: Kind.Multiline),
        Button("controls.shortcuts", "controls", "Keyboard", "Keyboard shortcuts", "show-shortcuts", "See every key Lumina understands."),

        // ---------------- Playback ----------------
        Button("playback.profiles", "playback", "Profiles", "Profiles", "show-profiles",
            "Movies, Anime, Night and your own. One click sets picture, sound, subtitles and playback together, and Undo puts everything back."),
        Choice("playback.resume", "playback", "Startup", "Continue where you left off", "always", Ch("always|Always", "never|Never")),
        Choice("player.external", "playback", "Player", "Play videos with", "lumina",
            Ch("lumina|Lumina", "vlc|VLC", "mpc-hc|MPC-HC", "mpc-be|MPC-BE", "potplayer|PotPlayer", "mpv|mpv", "mpvnet|mpv.net", "custom|Another program"),
            "Lumina keeps your library and progress either way. VLC and mpv tell Lumina where you stopped; with the others Lumina asks whether you finished when the player closes. Right-click a title and choose Play with to use another player once."),
        Text("player.externalPath", "playback", "Player", "Player program", "",
            "Leave empty and Lumina finds VLC, MPC, PotPlayer or mpv itself. Needed for Another program.", @"C:\Program Files\Player\player.exe"),
        Text("player.externalArgs", "playback", "Player", "Command line for another program", "\"{file}\"",
            "{file} is the video, {start} the resume point in seconds ({startms} in milliseconds), {sub} a subtitle file.", "\"{file}\""),
        Toggle("playback.startPaused", "playback", "Startup", "Start paused", false, mpv: YesNo("pause")),
        Slider("playback.speed", "playback", "Startup", "Default speed", 1, 0.25, 2, 0.25, "x", mpv: Opt("speed")),
        Choice("playback.queueRepeat", "playback", "Queue", "Repeat the queue", "off", Ch("off|Off", "all|All", "one|One item"),
            "Applies when you play several files or a playlist.", segmented: true),
        Toggle("playback.shuffle", "playback", "Queue", "Shuffle the queue", false),
        Choice("playback.afterSleep", "playback", "Sleep timer", "When the sleep timer ends", "pause",
            Ch("pause|Pause the video", "stop|Stop and go back to the library", "sleep|Put the PC to sleep", "shutdown|Shut the PC down"),
            "Set the timer from the player panel. Shut down gives you a minute of Windows' own warning, and shutdown /a cancels it."),
        Choice("playback.afterEpisode", "playback", "When something ends", "After an episode", "next",
            Ch("next|Play the next one", "stop|Stop and stay"), "Movies always stay on the last frame."),
        Choice("playback.loop", "playback", "When something ends", "Repeat the current file", "no", Ch("no|Off", "inf|On"),
            mpv: Opt("loop-file"), segmented: true),
        Choice("playback.seek", "playback", "Seeking", "Seek to", "exact", Ch("exact|The exact frame", "fast|The nearest keyframe"),
            "The exact frame is slower on some files.", v => new[] { $"hr-seek={(v == "exact" ? "yes" : "no")}" }),
        Text("playback.alang", "playback", "Tracks", "Preferred audio language", "",
            "Codes in order, such as en,ja. Empty means the file's default.", "en,ja", Opt("alang")),

        // ---------------- Video ----------------
        Toggle("video.fullscreenStart", "video", "Output", "Start videos in fullscreen", false),
        Toggle("video.stopScreensaver", "video", "Output", "Keep the screen awake during playback", true, mpv: YesNo("stop-screensaver")),
        Slider("video.brightness", "video", "Picture", "Brightness", 0, -100, 100, 1, mpv: Opt("brightness")),
        Slider("video.contrast", "video", "Picture", "Contrast", 0, -100, 100, 1, mpv: Opt("contrast")),
        Slider("video.saturation", "video", "Picture", "Saturation", 0, -100, 100, 1, mpv: Opt("saturation")),
        Slider("video.gamma", "video", "Picture", "Gamma", 0, -100, 100, 1, mpv: Opt("gamma")),
        Slider("video.hue", "video", "Picture", "Hue", 0, -100, 100, 1, mpv: Opt("hue")),
        Button("video.resetPicture", "video", "Picture", "Reset picture", "reset-picture"),
        Choice("video.deinterlace", "video", "Deinterlacing", "Deinterlacing", "auto", Ch("auto|Automatic", "yes|Always", "no|Never"),
            "Automatic catches interlaced broadcast video and leaves everything else alone.", Opt("deinterlace")),
        Choice("video.aspect", "video", "Framing", "Aspect ratio", "auto",
            Ch("auto|Automatic", "16:9|16:9", "4:3|4:3", "21:9|21:9", "2.35:1|2.35:1"),
            mpv: v => new[] { $"video-aspect-override={(v == "auto" ? "-1" : v)}" }),
        Choice("video.tonemap", "video", "HDR", "HDR tone mapping", "auto",
            Ch("auto|Automatic", "bt.2390|BT.2390", "hable|Hable", "reinhard|Reinhard", "mobius|Mobius"),
            "How bright HDR is squeezed onto a screen that is not HDR.", Opt("tone-mapping")),
        Choice("video.scaling", "video", "Quality", "Picture scaling", "fast",
            Ch("fast|Fast (smoothest, recommended)", "high|High quality (needs a strong graphics card)"),
            "Fast keeps playback smooth while other programs are busy. High quality sharpens upscaling and gradients but can stutter on integrated graphics.",
            v => v == "high" ? new[] { "profile=gpu-hq" } : Array.Empty<string>()),
        Toggle("video.smooth", "video", "Motion", "Smooth motion", false,
            "Blends frames to match your screen's refresh rate. Costs GPU time, and keeps the picture in step with the screen instead of the sound.",
            v => v == "true" ? new[] { "interpolation=yes", "video-sync=display-resample" } : new[] { "interpolation=no" }),

        // ---------------- Audio ----------------
        Slider("audio.volumeMax", "audio", "Volume", "Maximum volume", 130, 100, 300, 10, "%", mpv: Opt("volume-max")),
        Choice("audio.startVolume", "audio", "Volume", "Startup volume", "remember", Ch("remember|Remember the last one", "fixed|Always the same"),
            segmented: true),
        Slider("audio.fixedVolume", "audio", "Volume", "Always start at", 100, 0, 100, 5, "%"),
        Choice("audio.eqPreset", "audio", "Equalizer", "Preset", "off", Lumina.Player.AudioFx.PresetChoices,
            "Picking a preset fills the bands below. Moving a band switches to Custom."),
        Slider("audio.eq1", "audio", "Equalizer", "31 Hz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq2", "audio", "Equalizer", "62 Hz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq3", "audio", "Equalizer", "125 Hz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq4", "audio", "Equalizer", "250 Hz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq5", "audio", "Equalizer", "500 Hz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq6", "audio", "Equalizer", "1 kHz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq7", "audio", "Equalizer", "2 kHz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq8", "audio", "Equalizer", "4 kHz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq9", "audio", "Equalizer", "8 kHz", 0, -12, 12, 1, " dB"),
        Slider("audio.eq10", "audio", "Equalizer", "16 kHz", 0, -12, 12, 1, " dB"),
        Choice("audio.compressor", "audio", "Effects", "Compressor", "off", Lumina.Player.AudioFx.CompressorChoices,
            "Evens out loud and quiet moments. Stronger means a flatter sound."),
        Choice("audio.spatial", "audio", "Effects", "Spatial sound", "off", Lumina.Player.AudioFx.SpatialChoices,
            "Headphones blends left and right for a more natural feel. Room and Hall add an echo."),
        Slider("audio.stereoWidth", "audio", "Effects", "Stereo width", 0, 0, 100, 5, "%", "0 leaves the stereo image as recorded."),
        Toggle("audio.normalize", "audio", "Loudness", "Even out loudness", false,
            "Keeps quiet scenes and loud scenes closer together."),
        Toggle("audio.night", "audio", "Loudness", "Night mode", false, "Squashes loud peaks so you can watch quietly."),
        Choice("audio.replayGain", "audio", "Loudness", "Replay gain", "no", Ch("no|Off", "track|Per track", "album|Per album"),
            mpv: Opt("replaygain")),
        Choice("audio.channels", "audio", "Output", "Speaker setup", "auto", Ch("auto|Automatic", "stereo|Stereo", "5.1|5.1", "7.1|7.1"),
            mpv: v => new[] { $"audio-channels={(v == "auto" ? "auto-safe" : v)}" }),
        Text("audio.device", "audio", "Output", "Output device", "auto",
            "Pick one from Audio, Output device in the player. Empty or auto follows the Windows default.", "auto", Opt("audio-device")),
        Toggle("audio.hires", "audio", "Output", "High-resolution audio (bit-perfect)", false,
            "Plays 24-bit and up to 384 kHz files exactly as recorded through a DAC or sound card that supports it, with no Windows mixing or resampling. Other programs are silent while a video plays.",
            v => v == "true" ? new[] { "audio-exclusive=yes", "audio-samplerate=0" } : Array.Empty<string>()),
        Toggle("audio.passthrough", "audio", "Output", "Pass surround sound to your receiver", false,
            "Sends Dolby and DTS untouched over HDMI. Needs a receiver that supports them.",
            v => v == "true" ? new[] { "audio-spdif=ac3,eac3,dts,dts-hd,truehd", "audio-exclusive=yes" } : Array.Empty<string>()),

        // ---------------- Subtitles ----------------
        Toggle("subtitles.enable", "subtitles", "Tracks and languages", "Show subtitles by default", true,
            mpv: v => new[] { $"sid={(v == "true" ? "auto" : "no")}" }),
        Text("subtitleLanguages", "subtitles", "Tracks and languages", "Preferred languages", "en",
            "Codes in order, such as en,es. Also used when searching OpenSubtitles.", "en,es", Opt("slang")),
        Choice("subtitles.autoLoad", "subtitles", "Loading", "Load subtitle files next to the video", "fuzzy",
            Ch("no|Never", "exact|Same name only", "fuzzy|Name contains the title", "all|Every subtitle file"), mpv: Opt("sub-auto")),
        Text("subtitles.encoding", "subtitles", "Loading", "Text encoding", "", "Leave empty for automatic.", "utf-8", Opt("sub-codepage")),
        Text("subtitles.font", "subtitles", "Appearance", "Font", "Segoe UI Semibold", mpv: Opt("sub-font")),
        Slider("subtitles.size", "subtitles", "Appearance", "Size", 46, 20, 90, 2, mpv: Opt("sub-font-size")),
        Toggle("subtitles.bold", "subtitles", "Appearance", "Bold", false, mpv: YesNo("sub-bold")),
        Color("subtitles.color", "subtitles", "Appearance", "Text colour", "FFFFFF", mpv: v => new[] { $"sub-color=#{v}" }),
        Slider("subtitles.outline", "subtitles", "Appearance", "Outline thickness", 2.2, 0, 6, 0.2, mpv: Opt("sub-border-size")),
        Color("subtitles.outlineColor", "subtitles", "Appearance", "Outline colour", "000000", mpv: v => new[] { $"sub-border-color=#{v}" }),
        Toggle("subtitles.shadow", "subtitles", "Appearance", "Shadow", true, mpv: v => new[] { $"sub-shadow-offset={(v == "true" ? "1" : "0")}" }),
        Slider("subtitles.position", "subtitles", "Appearance", "Position", 100, 0, 100, 1, "%", "0 is the top of the picture, 100 the bottom.", Opt("sub-pos")),

        // ---------------- Library ----------------
        Toggle("library.showMovies", "library", "Show in library", "Show movies", true, "Off hides movies from Home, the sidebar and the lists. Your files stay and are still scanned."),
        Toggle("library.showShows", "library", "Show in library", "Show TV shows", true, "Off hides TV shows from Home, the sidebar and the lists. Your files stay and are still scanned."),
        Button("library.folders", "library", "Folders", "Manage folders", "open-folders", "Add or remove the folders Lumina scans."),
        Button("library.unhide", "library", "Folders", "Show hidden titles again", "unhide",
            "Brings back everything you hid with Hide from library. Your files are never touched."),
        Button("library.rescan", "library", "Folders", "Scan now", "rescan"),

        // ---------------- Network ----------------
        Button("network.logins", "network", "Logins", "Saved logins", "show-logins",
            "Servers Lumina has remembered. Passwords live in Windows Credential Manager. You can add or forget them here."),
        Text("recording.folder", "network", "Recordings", "Save recordings to", "",
            "Empty uses Videos\\Lumina recordings. A recording starts when you press Record in the player panel and keeps what arrives from then on. It needs at least 500 MB free.",
            "D:\\Recordings"),
        Slider("network.buffer", "network", "Buffering", "Buffer ahead", 60, 5, 300, 5, "s",
            "How much video is read in advance. More helps on slow shares and costs memory.", Opt("cache-secs")),
        Text("network.proxy", "network", "Proxy", "HTTP proxy", "", "Leave empty to connect directly.", "http://host:port", Opt("http-proxy")),
        Toggle("network.verifyTls", "network", "Security", "Verify secure connections", true,
            "Turn off only for a server with a self-signed certificate that you trust.", YesNo("tls-verify")),

        // ---------------- Performance ----------------
        Choice("performance.hwdec", "performance", "Decoding", "Hardware decoding", "d3d11va,d3d11va-copy",
            Ch("d3d11va,d3d11va-copy|Direct3D 11, zero-copy (recommended)", "d3d11va-copy|Direct3D 11, copy (safest)", "d3d11va|Direct3D 11, zero-copy only", "auto-safe|Let mpv choose", "no|Off, use the CPU"),
            "Zero-copy keeps the picture on the graphics card and uses the least CPU, which matters most when other programs are busy. If a driver cannot do it, Lumina falls back to copy by itself.", Opt("hwdec")),
        Slider("performance.threads", "performance", "Decoding", "Decoding threads", 0, 0, 16, 1,
            help: "0 lets the decoder choose.", mpv: Opt("vd-lavc-threads")),
        Choice("performance.sync", "performance", "Smooth playback", "Keep the picture in step with", "audio",
            Ch("audio|The sound (light on graphics, recommended)", "display-resample|The screen (steadiest motion, needs a strong graphics card)"),
            "The screen option redraws on every refresh, which is five times the work on a 120 Hz display, and struggles at 1.5x speed or in 4K.", Opt("video-sync")),
        Choice("performance.frameDrop", "performance", "Smooth playback", "When the system cannot keep up", "vo",
            Ch("vo|Drop frames when showing", "decoder|Drop frames when decoding", "decoder+vo|Both", "no|Never drop"), mpv: Opt("framedrop")),
        Choice("performance.renderer", "performance", "Renderer", "Video renderer", "gpu-next", Ch("gpu-next|Modern (gpu-next)", "gpu|Classic (gpu)"),
            "Switch to Classic if a driver shows glitches.", Opt("vo")),

        // ---------------- Snapshots ----------------
        Text("snapshots.folder", "snapshots", "Screenshots", "Save snapshots to", "",
            "Empty uses Pictures\\Lumina.", "C:\\Users\\you\\Pictures", Opt("screenshot-directory")),
        Choice("snapshots.format", "snapshots", "Screenshots", "Format", "png", Ch("png|PNG", "jpg|JPEG", "webp|WebP"),
            mpv: Opt("screenshot-format"), segmented: true),

        // ---------------- Accounts ----------------
        Text("tmdbKey", "accounts", "Artwork", "TMDB key", "", "Free from themoviedb.org. Posters, summaries and episode names.", kind: Kind.Secret),
        Text("openSubtitlesKey", "accounts", "Subtitles", "OpenSubtitles key", "", "Press B while a video plays to search.", kind: Kind.Secret),
        Text("omdbKey", "accounts", "Ratings", "OMDb key", "", "Free from omdbapi.com. Adds IMDb and Rotten Tomatoes scores to the details page.", kind: Kind.Secret),
        Text("traktClientId", "accounts", "Trakt", "Client id", "", "From your app at trakt.tv/oauth/applications.", "Paste your client id"),
        Text("traktClientSecret", "accounts", "Trakt", "Client secret (optional)", "", "Only if your Trakt app has one. Changing either value signs you out of Trakt.", kind: Kind.Secret),

        // ---------------- Advanced ----------------
        Toggle("updates.onStart", "advanced", "Updates", "Check for updates when Lumina starts", true,
            "At most once a day Lumina asks the update address whether there is a newer version. Nothing is downloaded or installed without asking you. Turn this off and Lumina only checks when you press the button."),
        Text("updates.feedUrl", "advanced", "Updates", "Update address", "https://github.com/nomoron28/lumina/releases/latest/download/latest.json",
            "Where Lumina looks for a newer version: the latest GitHub release by default. Must start with https://. Leave empty to turn updates off.", "https://github.com/nomoron28/lumina/releases/latest/download/latest.json"),
        Button("updates.check", "advanced", "Updates", "Check for updates now", "check-updates", "Shows what changed before anything is downloaded. The file is checked against its checksum before it runs."),
        Button("advanced.about", "advanced", "Support", "About Lumina", "show-about", "Version, where your data is kept, and what the player is built on."),
        Button("advanced.report", "advanced", "Support", "Report a problem", "report-problem", "Opens a new GitHub issue with your Lumina and Windows versions filled in, and copies the end of today's log (file paths removed) for you to paste. Nothing is sent until you submit it."),
        Button("advanced.logs", "advanced", "Support", "Log files", "open-logs", "Opens the folder with Lumina's log. Attach today's file when reporting a problem."),
        Text("mpvOptions", "advanced", "Player", "Extra mpv options", "",
            "One key=value per line. These win over everything above. Lines mpv rejects are skipped and named on screen.",
            "hwdec=auto-safe", kind: Kind.Multiline),
    };

    static readonly Dictionary<string, SettingDef> ByKeyMap = All.ToDictionary(d => d.Key);

    public static SettingDef? Find(string key) => ByKeyMap.TryGetValue(key, out var d) ? d : null;

    public static IEnumerable<SettingDef> In(string category) => All.Where(d => d.Category == category);
}
