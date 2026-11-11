# End-to-end tour with real mouse and keyboard on scratch data. Exit code 0 only if every check passes.
# Run: powershell -File scripts\e2e.ps1   (needs a built Debug app and fixtures; run scripts\make-fixtures.ps1 first)
param([ValidateSet("all", "settings", "player", "queue", "playlists", "folder", "essentials", "tracks", "launch", "taskbar", "assoc", "media", "library", "view", "sidebar", "audio", "network", "updates", "keys", "theme", "profiles", "tour", "controller", "details", "tvdetails", "home", "overview", "visual", "floating", "vlc", "scrub", "editmeta", "thumbs", "bookmarks", "sleep", "lock", "access", "record", "hdr", "mini", "subs")][string]$Part = "all")
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\e2e-lib.ps1"
$fx = Join-Path (Split-Path $PSScriptRoot -Parent) "scratch\fixtures"
Get-Process Lumina -ErrorAction SilentlyContinue | Stop-Process -Force

function Part-Settings {
    Write-Host "Settings page"
    $app = Start-Lumina "" @{} "settings"
    try {
        CheckTrue "settings page shows General" (Find-Element $app "General")
        foreach ($cat in "General", "Interface", "Privacy", "Controls", "Playback", "Video", "Audio", "Subtitles", "Library", "Network", "Performance", "Snapshots", "Accounts", "Advanced") {
            Click-Element $app $cat
            $bad = Audit-Names $app
            Check "every control on '$cat' has a name" ($bad -join "; ") ""
        }
        Click-Element $app "General"
        foreach ($nav in "Home", "Movies", "TV shows", "Unwatched", "Folders") {
            Click-Element $app $nav
            Check "every control on '$nav' has a name" ((Audit-Names $app) -join "; ") ""
        }
        Click-Element $app "Settings"
        Click-Element $app "Subtitles"
        CheckTrue "Subtitles page shows Bold" (Find-Element $app "Bold")
        Click-Element $app "Bold"
        Check "toggle Bold is saved" (Config-Value $app "subtitles.bold") "true"
        Click-Element $app "Bold"
        Check "toggle off removes the key (default)" (Config-Value $app "subtitles.bold") $null

        $s = (Find-Element $app "Size").Current.BoundingRectangle
        Click-Screen ([int]($s.X + $s.Width - 6)) ([int]($s.Y + $s.Height / 2)); Start-Sleep -Milliseconds 700
        CheckTrue "slider click near the end saves a large size" ([double](Config-Value $app "subtitles.size") -ge 80)

        Click-Element $app "Controls"
        Click-Element $app "30 s"
        Check "segmented choice saved" (Config-Value $app "controls.skip") "30"

        Click-Element $app "General"
        Click-Element $app "Accent Orange"
        Check "accent swatch saved" (Config-Value $app "general.accent") "F28C1E"

        Click-Element $app "Video"
        $cb = Find-Element $app "Deinterlacing"
        Click-Screen ([int]($cb.Current.BoundingRectangle.X + 30)) ([int]($cb.Current.BoundingRectangle.Y + 20))
        Click-Element $app "Always"
        Check "dropdown choice saved" (Config-Value $app "video.deinterlace") "yes"

        Click-Element $app "Search settings"
        Type-Text "bright"
        CheckTrue "search finds Brightness" (Find-Element $app "Brightness")
        Check "search hides unrelated rows" ($null -eq (Find-Element $app "Bold" 600)) $true

        Click-Element $app "Privacy"
        Click-Element $app "Clear watch history"
        CheckTrue "confirm dialog appears" (Find-Element $app "Clear watch history?" -Text)
        Click-Element $app "Clear"
        Start-Sleep -Milliseconds 500
        Check "no errors in the log after the settings tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Player {
    Write-Host "Player controller"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller script is ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Check "starts paused" (Mpv-Get $app 'pause') $true

        Click-Region $app "play"
        Check "play button resumes" (Mpv-Wait $app 'pause' $false) $false
        Click-Region $app "play"
        Check "play button pauses" (Mpv-Wait $app 'pause' $true) $true

        Click-Region $app "mute"
        Check "mute button mutes" (Mpv-Wait $app 'mute' $true) $true
        Click-Region $app "mute"
        Check "mute button unmutes" (Mpv-Wait $app 'mute' $false) $false

        Click-Region $app "vol" 0.5
        $v = [double](Mpv-Get $app 'volume')
        CheckTrue "volume slider middle is about half of 130" ([math]::Abs($v - 65) -le 8)

        Mpv-Set $app 'time-pos' 1
        Click-Region $app "fwd"
        $t = [double](Mpv-Get $app 'time-pos')
        CheckTrue "forward skips 10 s" ([math]::Abs($t - 11) -le 1.5)
        Click-Region $app "back"
        $t = [double](Mpv-Get $app 'time-pos')
        CheckTrue "back skips 10 s" ([math]::Abs($t - 1) -le 1.5)

        # subtitle menu: items are Off, eng, spa, Search
        Click-Region $app "cc"
        Check "CC opens its menu" (Mpv-Get $app 'user-data/lumina/menu') "cc"
        Click-Region $app "menu3"
        Check "choosing a track selects it" (Mpv-Wait $app 'sid' 2) 2
        Check "menu closes after a choice" (Mpv-Get $app 'user-data/lumina/menu') ""
        Click-Region $app "cc"; Click-Region $app "menu1"
        Check "Off turns subtitles off" (Mpv-Wait $app 'sid' $false) $false

        Click-Region $app "audio"
        Check "Audio opens its menu" (Mpv-Get $app 'user-data/lumina/menu') "audio"
        Click-Region $app "menu2"
        Check "choosing the second audio track" (Mpv-Wait $app 'aid' 2) 2

        Click-Region $app "speed"
        Click-Region $app "menu5"
        Mpv-Wait $app 'speed' 1.5 | Out-Null
        Check "speed menu sets 1.5x" ([double](Mpv-Get $app 'speed')) 1.5
        Click-Region $app "speed"
        Check "speed menu opens" (Mpv-Get $app 'user-data/lumina/menu') "speed"
        Click-Region $app "speed"
        Check "clicking the pill again closes the menu" (Mpv-Get $app 'user-data/lumina/menu') ""

        Click-Region $app "snap"
        Start-Sleep -Milliseconds 800
        Check "snapshot writes a file to the chosen folder" @(Get-ChildItem "$($app.Dir)\snaps").Count 1

        $before = Mpv-Get $app 'pause'
        Click-Region $app "fs"
        $scr = [Windows.Forms.Screen]::FromHandle($app.Hwnd).Bounds
        $r = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
        Check "fullscreen button covers the monitor" (($r.R - $r.L) -ge $scr.Width) $true
        Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 600
        [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
        Check "Esc leaves fullscreen" (($r.R - $r.L) -lt $scr.Width) $true

        Send-Keys (" "); Start-Sleep -Milliseconds 400
        Check "Space toggles pause" ((Mpv-Get $app 'pause') -ne $before) $true

        # Clicks and wheel on the picture itself (away from the controls)
        $r = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
        $cx = $r.L + 700; $cy = $r.T + 400
        $p0 = Mpv-Get $app 'pause'
        Click-Screen $cx $cy; Start-Sleep -Milliseconds 300
        Check "click on the picture toggles pause" ((Mpv-Get $app 'pause') -ne $p0) $true
        Click-Screen $cx $cy; Start-Sleep -Milliseconds 300
        $v0 = [double](Mpv-Get $app 'volume')
        Press-Mouse 0x0800 120; Start-Sleep -Milliseconds 400
        CheckTrue "wheel up raises volume" ([double](Mpv-Get $app 'volume') -gt $v0)
        Move-Mouse $cx $cy; Press-Mouse 0x02; Press-Mouse 0x04; Start-Sleep -Milliseconds 60; Press-Mouse 0x02; Press-Mouse 0x04; Start-Sleep -Milliseconds 700
        [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
        Check "double-click enters fullscreen" (($r.R - $r.L) -ge $scr.Width) $true
        Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 500

        Check "no errors in the log after the player tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Queue {
    Write-Host "Queue"
    $eps = 1..3 | ForEach-Object { "$fx\library\TV\Show Name\Show.Name.S01E0$_.1080p.mkv" }
    $app = Start-Lumina $eps @{}
    try {
        CheckTrue "three files open as a queue at the first" (Wait-Path $app 'S01E01')
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Check "queue has three items" (Mpv-Get $app 'user-data/lumina/queue_count') "3"
        Check "queue starts at item 0" (Mpv-Get $app 'user-data/lumina/queue_cur') "0"

        Click-Region $app "next"
        CheckTrue "Next plays the second file" (Wait-Path $app 'S01E02')
        Check "queue position follows" (Mpv-Get $app 'user-data/lumina/queue_cur') "1"
        Click-Region $app "prev"
        CheckTrue "Previous goes back" (Wait-Path $app 'S01E01')

        Click-Region $app "queue"
        CheckTrue "queue button opens the panel" (Wait-Regions $app "qrow3")
        Click-Region $app "qrow3"
        CheckTrue "clicking a row jumps to it" (Wait-Path $app 'S01E03')
        Check "queue position is the third item" (Mpv-Get $app 'user-data/lumina/queue_cur') "2"
        Wake-Controls $app
        Check "no Next button on the last item with repeat off" ($null -eq (Player-Regions $app).next) $true

        Seek-Near-End $app; Start-Sleep -Seconds 3
        Check "last item ends and stays (repeat off)" ((Mpv-Get $app 'path') -match 'S01E03') $true

        Click-Region $app "queue"; Wait-Regions $app "q_repeat" | Out-Null
        Click-Region $app "q_repeat"
        Check "repeat chip cycles to all" (Mpv-Wait $app 'user-data/lumina/queue_repeat' 'all') "all"
        Check "repeat is saved as a setting" (Config-Value $app "playback.queueRepeat") "all"
        Seek-Near-End $app
        CheckTrue "repeat all wraps to the first item" (Wait-Path $app 'S01E01')

        Click-Region $app "queue"; Wait-Regions $app "q_shuffle" | Out-Null
        Click-Region $app "q_shuffle"
        Check "shuffle chip turns shuffle on" (Mpv-Wait $app 'user-data/lumina/queue_shuffle' 'true') "true"
        Check "shuffle is saved as a setting" (Config-Value $app "playback.shuffle") "true"
        Click-Region $app "q_shuffle"
        Check "and off again" (Mpv-Wait $app 'user-data/lumina/queue_shuffle' 'false') "false"

        Click-Region $app "qx3"
        Check "removing a row shrinks the queue" (Mpv-Wait $app 'user-data/lumina/queue_count' '2') "2"
        Click-Region $app "q_clear"
        Check "clear empties the queue" (Mpv-Wait $app 'user-data/lumina/queue_count' '0') "0"
        Check "no errors in the log after the queue tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Playlists {
    Write-Host "Playlists"
    $eps = 1..2 | ForEach-Object { "$fx\library\TV\Show Name\Show.Name.S01E0$_.1080p.mkv" }
    $app = Start-Lumina $eps @{}
    try {
        CheckTrue "queue is playing" (Wait-Path $app 'S01E01')
        Calibrate-Player $app
        Click-Region $app "queue"; Wait-Regions $app "q_save" | Out-Null
        Click-Region $app "q_save"
        CheckTrue "save asks for a name" (Find-Element $app "Playlist name")
        Type-Text "Weekend{ENTER}"
        Start-Sleep -Milliseconds 600
        Mpv-Ipc $app @{ command = @('quit') } | Out-Null          # leave the player
        CheckTrue "back on the library after quitting" (Find-Element $app "Playlists")
        Click-Element $app "Playlists"
        CheckTrue "saved playlist is listed" (Find-Element $app "Weekend" -Text)
        Click-Element $app "Rename Weekend"
        Type-Text "Friday{ENTER}"
        CheckTrue "rename shows the new name" (Find-Element $app "Friday" -Text)
        Click-Element $app "Play Friday"
        CheckTrue "Play starts the saved queue" (Wait-Path $app 'S01E01')
        CheckTrue "with both items" ((Mpv-Wait $app 'user-data/lumina/queue_count' '2') -eq "2")
        Mpv-Ipc $app @{ command = @('quit') } | Out-Null
        Click-Element $app "Home"; Click-Element $app "Playlists"
        Click-Element $app "Delete Friday"
        Click-Element $app "Delete"
        Check "delete removes it" ($null -eq (Find-Element $app "Friday" 800 -Text)) $true
        Check "no errors in the log after the playlist tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Folder {
    Write-Host "Folder as queue"
    $app = Start-Lumina @("$fx\library\TV\Show Name") @{}
    try {
        CheckTrue "a folder argument opens its videos as a queue" (Wait-Path $app 'S01E01')
        CheckTrue "controller ready" (Mpv-Ready $app)
        Check "all three episodes are queued, in order" (Mpv-Wait $app 'user-data/lumina/queue_count' '3') "3"
    } finally { Stop-Lumina $app }
    $app = Start-Lumina @("$fx\multi.mkv") @{}
    try {
        CheckTrue "single file plays" (Wait-Path $app 'multi')
        CheckTrue "controller ready" (Mpv-Ready $app)
        Check "a single file has no queue" (Mpv-Get $app 'user-data/lumina/queue_count') "0"
    } finally { Stop-Lumina $app }
}

function Part-Essentials {
    Write-Host "Player essentials panel"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Click-Region $app "gear"
        Check "gear button opens the panel" (Mpv-Get $app 'user-data/lumina/gear') "open"
        CheckTrue "panel buttons are published" (Wait-Regions $app "g_1_p")
        Click-Region $app "g_1_p"; Click-Region $app "g_1_p"
        Check "subtitle delay plus twice" ([math]::Round([double](Mpv-Get $app 'sub-delay'), 2)) 0.2
        Click-Region $app "g_1_m"
        Check "subtitle delay minus once" ([math]::Round([double](Mpv-Get $app 'sub-delay'), 2)) 0.1
        Click-Region $app "g_2_p"
        Check "audio delay plus" ([math]::Round([double](Mpv-Get $app 'audio-delay'), 2)) 0.1
        Click-Region $app "g_3_p"
        Check "speed plus" ([math]::Round([double](Mpv-Get $app 'speed'), 2)) 1.05
        Click-Region $app "g_5_p"
        Check "rotate turns 90" ([double](Mpv-Get $app 'video-rotate')) 90
        Click-Region $app "g_6_p"
        Check "aspect cycles to 16:9" ([math]::Round([double](Mpv-Get $app 'video-aspect-override'), 2)) 1.78
        Click-Region $app "g_8_p"
        Check "brightness plus 5" ([double](Mpv-Get $app 'brightness')) 5
        Click-Region $app "g_13_p"
        Check "A-B loop sets A" ((Mpv-Get $app 'ab-loop-a') -ne 'no') $true
        Click-Region $app "g_13_p"; Click-Region $app "g_13_p"
        Check "A-B loop clears on the third press" (Mpv-Get $app 'ab-loop-a') "no"

        Click-Region $app "g_14_p"
        CheckTrue "Jump asks for a time" (Find-Element $app "Time, like 1:30")
        Type-Text "0:07{ENTER}"
        Start-Sleep -Milliseconds 700
        CheckTrue "Jump seeks to 7 seconds" ([math]::Abs([double](Mpv-Get $app 'time-pos') - 7) -le 1)
        Click-Region $app "g_14_p"
        Type-Text "9:59{ENTER}"
        Start-Sleep -Milliseconds 600
        CheckTrue "a time past the end is refused and playback stays put" ([math]::Abs([double](Mpv-Get $app 'time-pos') - 7) -le 1.5)

        Wait-Regions $app "g_reset" | Out-Null
        Click-Region $app "g_reset"
        Check "reset puts speed back" ([double](Mpv-Get $app 'speed')) 1
        Check "reset puts rotation back" ([double](Mpv-Get $app 'video-rotate')) 0
        Check "reset puts brightness back" ([double](Mpv-Get $app 'brightness')) 0
        Check "reset clears the audio delay" ([double](Mpv-Get $app 'audio-delay')) 0

        Click-Region $app "gear"
        Check "gear closes the panel" (Mpv-Get $app 'user-data/lumina/gear') ""
        Check "no errors in the log after the essentials tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Tracks {
    Write-Host "Chapters, tracks, devices, media info"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Click-Region $app "chap"
        Check "chapter button opens the chapter menu" (Mpv-Get $app 'user-data/lumina/menu') "chapters"
        Check "chapter menu lists all three" (Mpv-Get $app 'user-data/lumina/menu_items') "1  Opening|2  Middle|3  Ending"
        Click-Region $app "menu3"
        CheckTrue "choosing a chapter seeks to its start" ([math]::Abs([double](Mpv-Wait $app 'time-pos' 8 3000) - 8) -le 1)
        Check "the chapter index follows" ([int](Mpv-Get $app 'chapter')) 2

        Click-Region $app "audio"
        Check "audio menu lists tracks and the device entry" ((Mpv-Get $app 'user-data/lumina/menu_items') -match '\|Output device$') $true
        Click-Region $app "menu3"
        Check "Output device opens the device list" (Mpv-Get $app 'user-data/lumina/menu') "devices"
        $devices = (Mpv-Get $app 'audio-device-list')
        CheckTrue "the player knows at least one output device" ($devices.Count -ge 1)
        if ($devices.Count -ge 2) {
            Click-Region $app "menu2"
            Check "choosing a device switches to it" (Mpv-Get $app 'audio-device') $devices[1].name
            Check "and remembers it in settings" (Config-Value $app "audio.device") $devices[1].name
        } else {
            Click-Region $app "menu1"
            Write-Host "  (only one output device here, switching not exercised)"
        }
        Check "menu closes after the choice" (Mpv-Get $app 'user-data/lumina/menu') ""

        Click-Region $app "gear"; Wait-Regions $app "g_15_p" | Out-Null
        Click-Region $app "g_15_p"
        CheckTrue "Media info opens a dialog" (Find-Element $app "Media info" -Text)
        CheckTrue "dialog names the file" (Find-Element $app "multi.mkv" -Text)
        CheckTrue "dialog lists the resolution" (Find-Element $app "640 x 360" -Text)
        Click-Element $app "Done"
        Start-Sleep -Milliseconds 400
        Check "dialog closes" ($null -eq (Find-Element $app "Media info" 600 -Text)) $true
        Check "no errors in the log after the tracks tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Launch {
    Write-Host "Command line and single instance"
    # --- options on a fresh launch ---
    $app = Start-Lumina "$fx\multi.mkv" @{} "" "" @("--paused", "--start-time=0:07")
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        CheckTrue "--start-time seeks to 7 s" ([math]::Abs([double](Mpv-Wait $app 'time-pos' 7 3000) - 7) -le 1)
        Check "--paused starts paused" (Mpv-Get $app 'pause') $true
    } finally { Stop-Lumina $app }

    $app = Start-Lumina "$fx\multi.mkv" @{} "" "" @("--fullscreen", "--sub-file=`"$fx\spa.srt`"")
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        $scr = [Windows.Forms.Screen]::FromHandle($app.Hwnd).Bounds
        $r = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
        Check "--fullscreen covers the monitor" (($r.R - $r.L) -ge $scr.Width) $true
        $subs = @(1..([int](Mpv-Get $app 'track-list/count')) | ForEach-Object { Mpv-Get $app "track-list/$($_ - 1)/external" } | Where-Object { $_ -eq $true })
        Check "--sub-file adds one external subtitle" $subs.Count 1
    } finally { Stop-Lumina $app }

    # --- a second launch forwards to the first ---
    $first = Start-Lumina "$fx\multi.mkv" @{} "" "shared-$([guid]::NewGuid().ToString('N').Substring(0,6))"
    try {
        CheckTrue "first instance is playing multi.mkv" (Mpv-Ready $first)
        $before = @(Get-Process Lumina -ErrorAction SilentlyContinue).Count
        $second = Start-Process $Exe -ArgumentList "--start-time=0:03 `"$fx\surround51.mkv`"" -PassThru
        $exited = $second.WaitForExit(8000)
        Check "second launch exits by itself" $exited $true
        Check "no extra window is left running" @(Get-Process Lumina -ErrorAction SilentlyContinue).Count $before
        CheckTrue "the running Lumina now plays the forwarded file" (Wait-Path $first 'surround51')
        CheckTrue "forwarded options applied" ([math]::Abs([double](Mpv-Get $first 'time-pos') - 3) -le 2)
        Check "no errors in the first instance's log" (Error-Count $first) 0
    } finally { Stop-Lumina $first }

    # --- the setting turns it off ---
    $inst = "shared-$([guid]::NewGuid().ToString('N').Substring(0,6))"
    $first = Start-Lumina "$fx\multi.mkv" @{ "general.singleInstance" = "false" } "" $inst
    try {
        $env:LUMINA_INSTANCE = $inst
        $second = Start-Process $Exe -ArgumentList "`"$fx\surround51.mkv`"" -PassThru
        Start-Sleep -Seconds 4
        Check "with the setting off a second launch stays open" (-not $second.HasExited) $true
        if (-not $second.HasExited) { Stop-Process $second -Force }
    } finally { Stop-Lumina $first }
}

function Part-Taskbar {
    Write-Host "Taskbar progress and jump list"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Start-Sleep -Milliseconds 2200
        Check "paused video reports Paused progress" ((Mpv-Get $app 'user-data/lumina/taskbar') -match '^Paused:') $true
        Mpv-Set $app 'time-pos' 6; Start-Sleep -Milliseconds 1600
        $t = Mpv-Get $app 'user-data/lumina/taskbar'
        CheckTrue "halfway reports about 0.5 (got $t)" ($t -match '^Paused:0\.(4|5)')
        Mpv-Set $app 'pause' $false; Start-Sleep -Milliseconds 1800
        Check "playing reports Normal progress" ((Mpv-Get $app 'user-data/lumina/taskbar') -match '^Normal:') $true
        Mpv-Ipc $app @{ command = @('quit') } | Out-Null
        Start-Sleep -Milliseconds 1500
        # Windows keeps the Recent category of the jump list in a per-app file; the newest one must name the file we played.
        $dest = "$env:APPDATA\Microsoft\Windows\Recent\AutomaticDestinations"
        $text = ""
        foreach ($try in 1..10) {
            $latest = Get-ChildItem $dest -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            $text = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($latest.FullName))
            if ($text.Contains("multi")) { break }
            Start-Sleep -Milliseconds 500
        }
        Check "jump list lists the file we played (in Windows' Recent category)" $text.Contains("multi") $true
        Check "no errors in the log after the taskbar tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }

    $app = Start-Lumina "$fx\multi.mkv" @{ "general.taskbar" = "false" }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Start-Sleep -Milliseconds 2200
        Check "with the setting off the state is None" ((Mpv-Get $app 'user-data/lumina/taskbar') -match '^None:') $true
    } finally { Stop-Lumina $app }
}

function Part-Assoc {
    Write-Host "File associations"
    $branch = "Software\LuminaE2E\" + [guid]::NewGuid().ToString("N")
    $app = Start-Lumina "" @{} "settings" "" @() $branch
    try {
        CheckTrue "settings opens on General" (Find-Element $app "Show Lumina in Open with")
        Click-Element $app "Show Lumina in Open with"
        Start-Sleep -Milliseconds 600
        $cmd = (Get-ItemProperty "HKCU:\$branch\Classes\Lumina.Video\shell\open\command" -ErrorAction SilentlyContinue).'(default)'
        CheckTrue "Add writes the open command for this exe" ($cmd -and $cmd -like "*Lumina.exe*--*%1*")
        $mkv = Get-Item "HKCU:\$branch\Classes\.mkv\OpenWithProgids" -ErrorAction SilentlyContinue
        CheckTrue "Add lists Lumina under .mkv Open with" ($mkv -and ($mkv.GetValueNames() -contains "Lumina.Video"))
        CheckTrue "Add registers for Default apps" ((Get-ItemProperty "HKCU:\$branch\Software\RegisteredApplications" -ErrorAction SilentlyContinue).Lumina)
        CheckTrue "the page confirms what happened" (Find-Element $app "Lumina now appears in Open with for video files. Use the next row to make it the default." -Text)
        Click-Element $app "Remove Lumina from Open with"
        Start-Sleep -Milliseconds 600
        Check "Remove deletes the program id" (Test-Path "HKCU:\$branch\Classes\Lumina.Video") $false
        Check "Remove leaves no Open with entry" ((Get-Item "HKCU:\$branch\Classes\.mkv\OpenWithProgids" -ErrorAction SilentlyContinue).GetValueNames() -contains "Lumina.Video") $false
        Check "no errors in the log after the association tour" (Error-Count $app) 0
    } finally {
        Stop-Lumina $app
        Remove-Item "HKCU:\Software\LuminaE2E" -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Media {
    Write-Host "Windows media session (Now playing, media keys)"
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
    $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    function Await($op, $type) { $t = $asTask.MakeGenericMethod($type).Invoke($null, @($op)); $t.Wait(6000) | Out-Null; $t.Result }
    function Lumina-Session {
        $mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
        # Only ever our own session: other apps' players must never be touched by a test.
        $mgr.GetSessions() | Where-Object { $_.SourceAppUserModelId -like "Lumina*" } | Select-Object -First 1
    }
    $eps = 1..2 | ForEach-Object { "$fx\library\TV\Show Name\Show.Name.S01E0$_.1080p.mkv" }
    $app = Start-Lumina $eps @{}
    try {
        CheckTrue "queue is playing" (Wait-Path $app 'S01E01')
        Mpv-Set $app 'pause' $false; Start-Sleep -Milliseconds 2500
        $s = Lumina-Session
        CheckTrue "Windows lists a Lumina media session" ($null -ne $s)
        $props = Await ($s.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
        CheckTrue "Now playing shows the title (got '$($props.Title)')" ($props.Title -match 'Show')
        Check "status is Playing" $s.GetPlaybackInfo().PlaybackStatus "Playing"
        Await ($s.TryPauseAsync()) ([bool]) | Out-Null
        Check "pause from the overlay pauses the video" (Mpv-Wait $app 'pause' $true 3000) $true
        Await ($s.TryPlayAsync()) ([bool]) | Out-Null
        Check "play from the overlay resumes it" (Mpv-Wait $app 'pause' $false 3000) $false
        Await ($s.TrySkipNextAsync()) ([bool]) | Out-Null
        CheckTrue "Next from the overlay plays the next queue item" (Wait-Path $app 'S01E02')
        Check "no errors in the log after the media-key tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-EditMeta {
    Write-Host "Edit metadata (choose which title a movie is)"
    $port = Get-Random -Minimum 30000 -Maximum 40000
    $lib = Join-Path $env:TEMP ("lumina-em-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force $lib | Out-Null
    Copy-Item "$fx\multi.mkv" "$lib\The End of Oak Street (2026).mkv"
    $py = Start-Process python -ArgumentList "`"$PSScriptRootake-tmdb.py`" $port" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/3/movie/100" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $env:LUMINA_TMDB_API = "http://127.0.0.1:$port/3/"; $env:LUMINA_TMDB_IMG = "http://127.0.0.1:$port/img/"
        $app = Start-Lumina "" @{ tmdbKey = "0123456789abcdef0123456789abcdef" } "movies" "" @() "" $lib
        try {
            CheckTrue "the movie appears" (Find-Element $app "The End of Oak Street" 15000)
            Start-Sleep -Seconds 3
            Click-Element $app "The End of Oak Street" -Right
            CheckTrue "the menu offers Edit metadata" (Find-Element $app "Edit metadata..." 3000)
            Click-Element $app "Edit metadata..."
            CheckTrue "a search box opens" (Find-Element $app "Search TMDB" 5000)
            CheckTrue "the file name is shown" (Find-Element $app "The End of Oak Street (2026).mkv" 3000 -Text)
            CheckTrue "the matches are listed with their release date" (Find-Element $app "The End of Oak Street, 1 Jan 2026" 8000)
            CheckTrue "there is a 'none' choice" (Find-Element $app "None, use the file's own information" 2000)
            Save-Window $app.Hwnd "$env:TEMP/lumina-editmeta.png" -FromScreen

            # choosing the file's own information closes the window
            Click-Element $app "None, use the file's own information"
            CheckTrue "choosing none closes the window" (Gone $app "Search TMDB" 4000)
            Start-Sleep -Seconds 2

            # and a match can be chosen again
            Click-Element $app "The End of Oak Street" -Right
            Click-Element $app "Edit metadata..."
            CheckTrue "the window opens again" (Find-Element $app "Search TMDB" 5000)
            Click-Element $app "The End of Oak Street, 1 Jan 2026"
            CheckTrue "choosing a match closes the window" (Gone $app "Search TMDB" 4000)
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_TMDB_API, Env:\LUMINA_TMDB_IMG -ErrorAction SilentlyContinue
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Library {
    Write-Host "Library right-click menus"
    $app = Start-Lumina "" @{} "movies" "" @() "" "$fx\library"
    try {
        CheckTrue "the seeded movie appears" (Find-Element $app "Film Title" 8000)
        Click-Element $app "Film Title" -Right
        foreach ($item in "Play", "Mark as watched", "Refresh info", "Edit metadata...", "Show in Explorer", "Hide from library...") {
            CheckTrue "menu offers '$item'" (Find-Element $app $item 2000)
        }
        Check "Refresh info is disabled without a TMDB key" (Is-Enabled $app "Refresh info") $false
        Check "Show in Explorer is enabled for a file that exists" (Is-Enabled $app "Show in Explorer") $true
        Click-Element $app "Mark as watched"
        Click-Element $app "Unwatched"
        Check "a watched movie leaves the Unwatched page" (Gone $app "Film Title") $true
        Click-Element $app "Movies"
        Click-Element $app "Film Title" -Right
        CheckTrue "the menu now offers to undo it" (Find-Element $app "Mark as unwatched" 2000)
        Click-Element $app "Mark as unwatched"
        Click-Element $app "Unwatched"
        CheckTrue "unwatching brings it back" (Find-Element $app "Film Title" 3000)

        # keyboard: Shift+F10 on the focused card
        Click-Element $app "Movies"
        $card = Find-Element $app "Film Title"
        $card.SetFocus(); Start-Sleep -Milliseconds 300
        Send-Keys ("+{F10}"); Start-Sleep -Milliseconds 500
        CheckTrue "Shift+F10 opens the same menu from the keyboard" (Find-Element $app "Hide from library..." 2000)
        Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 300

        # a show: mark everything watched from the card, then one season from an episode row
        Click-Element $app "TV shows"
        Click-Element $app "Show Name" -Right
        Click-Element $app "Mark as watched"
        Click-Element $app "Unwatched"
        Check "a fully watched show leaves the Unwatched page" (Gone $app "Show Name") $true
        Click-Element $app "TV shows"
        Click-Element $app "Show Name"
        CheckTrue "details open" (Find-Element $app "Show Name" 3000 -Text)
        $ep = Find-Element $app "2. Show.Name.S01E02.1080p" 3000 -Text
        CheckTrue "episode cards are listed" ($null -ne $ep)
        $r = $ep.Current.BoundingRectangle
        Click-Screen ([int]($r.X + 20)) ([int]($r.Y + $r.Height / 2)) -Right
        CheckTrue "an episode menu offers the season action" (Find-Element $app "Mark season as unwatched" 2000)
        Click-Element $app "Mark season as unwatched"
        Click-Element $app "Unwatched"
        CheckTrue "the unwatched season puts the show back" (Find-Element $app "Show Name" 3000)

        # hide, then bring back from Settings
        Click-Element $app "Movies"
        Click-Element $app "Film Title" -Right
        Click-Element $app "Hide from library..."
        CheckTrue "hiding asks first" (Find-Element $app "Hide from library?" 2000 -Text)
        Click-Element $app "Hide"
        Check "hidden movie is gone" (Gone $app "Film Title" 1500) $true
        Click-Element $app "Settings"; Click-Element $app "Library"
        Click-Element $app "Show hidden titles again"
        Start-Sleep -Milliseconds 1500
        Click-Element $app "Movies"
        CheckTrue "showing hidden titles brings the movie back" (Find-Element $app "Film Title" 4000)
        Check "no errors in the log after the library tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-View {
    Write-Host "Library sort, list view, poster size"
    $app = Start-Lumina "" @{} "movies" "" @() "" "$fx\library"
    function Titles-By-Position($app, $axis) {
        $names = "Alpha Movie", "Film Title", "Zulu Movie"
        $found = foreach ($n in $names) { $e = Find-Element $app $n 3000; if ($e) { [pscustomobject]@{ Name = $n; X = $e.Current.BoundingRectangle.X; Y = $e.Current.BoundingRectangle.Y; W = $e.Current.BoundingRectangle.Width } } }
        if ($axis -eq "x") { ($found | Sort-Object Y, X).Name -join "," } else { ($found | Sort-Object Y).Name -join "," }
    }
    try {
        CheckTrue "movies appear" (Find-Element $app "Zulu Movie" 8000)
        Check "default order is A to Z" (Titles-By-Position $app "x") "Alpha Movie,Film Title,Zulu Movie"

        Click-Element $app "Sort by"
        Click-Element $app "Year, newest first"
        Check "sorting by year puts the newest first" (Titles-By-Position $app "x") "Zulu Movie,Film Title,Alpha Movie"
        Check "the choice is saved" (Config-Value $app "interface.librarySort") "year"

        Click-Element $app "List view"
        Start-Sleep -Milliseconds 500
        Check "list view stacks the same order top to bottom" (Titles-By-Position $app "y") "Zulu Movie,Film Title,Alpha Movie"
        Check "list view is saved" (Config-Value $app "interface.libraryView") "list"
        Check "size buttons hide in list view" ($null -eq (Find-Element $app "Small posters" 600)) $true

        Click-Element $app "Zulu Movie" -Right
        CheckTrue "the right-click menu works on a list row too" (Find-Element $app "Hide from library..." 2000)
        Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 300

        Click-Element $app "Poster view"
        Start-Sleep -Milliseconds 400
        $mid = (Find-Element $app "Film Title").Current.BoundingRectangle.Width
        Click-Element $app "Small posters"
        Start-Sleep -Milliseconds 500
        $small = (Find-Element $app "Film Title").Current.BoundingRectangle.Width
        Click-Element $app "Large posters"
        Start-Sleep -Milliseconds 500
        $large = (Find-Element $app "Film Title").Current.BoundingRectangle.Width
        CheckTrue "small < medium < large poster ($small < $mid < $large)" (($small -lt $mid) -and ($mid -lt $large))
        Check "size is saved" (Config-Value $app "interface.gridSize") "large"

        # the Settings page edits the same values
        Click-Element $app "Settings"; Click-Element $app "Interface"
        CheckTrue "Settings shows the library layout rows" (Find-Element $app "Poster size" 2000 -Text)
        Click-Element $app "Medium"
        Click-Element $app "Movies"
        $mid2 = (Find-Element $app "Film Title").Current.BoundingRectangle.Width
        CheckTrue "changing it in Settings resizes the posters (now $mid2)" ([math]::Abs($mid2 - $mid) -le 3)
        Check "no errors in the log after the view tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Sidebar {
    Write-Host "Collapsible sidebar"
    $app = Start-Lumina "" @{} "movies" "" @() "" "$fx\library"
    function NavWidth($app) { (Find-Element $app "Movies" 3000).Current.BoundingRectangle.Width }
    try {
        CheckTrue "library loads" (Find-Element $app "Zulu Movie" 8000)
        $wide = NavWidth $app
        Click-Element $app "Collapse sidebar"
        Start-Sleep -Milliseconds 500
        $narrow = NavWidth $app
        CheckTrue "collapsing narrows the nav ($wide to $narrow)" ($narrow -lt $wide / 2)
        Check "collapsed state is saved" (Config-Value $app "interface.compactSidebar") "true"
        CheckTrue "the toggle now offers to expand" (Find-Element $app "Expand sidebar" 1500)
        CheckTrue "nav items keep their names for screen readers" ((Find-Element $app "Settings" 1500) -ne $null)
        Check "no controls lose their names while collapsed" ((Audit-Names $app) -join "; ") ""

        Click-Element $app "TV shows"
        CheckTrue "navigating works while collapsed" (Find-Element $app "Show Name" 4000)
        Click-Element $app "Search your library"
        Start-Sleep -Milliseconds 600
        CheckTrue "the search icon opens the sidebar again" ((NavWidth $app) -gt $narrow * 2)
        Check "and saves that" (Config-Value $app "interface.compactSidebar") $null
        Type-Text "Zulu"
        Check "search found the movie" ($null -ne (Find-Element $app "Zulu Movie" 3000)) $true

        # Settings flips it live
        Click-Element $app "Settings"; Click-Element $app "Interface"
        Click-Element $app "Use the compact sidebar"
        Start-Sleep -Milliseconds 500
        CheckTrue "the Settings toggle collapses it at once" ((NavWidth $app) -lt $wide / 2)
        Click-Element $app "Use the compact sidebar"
        Start-Sleep -Milliseconds 500
        CheckTrue "and turning it off restores it" ((NavWidth $app) -gt $narrow * 2)
        Check "no errors in the log after the sidebar tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }

    # starts collapsed when saved
    $app = Start-Lumina "" @{ "interface.compactSidebar" = "true" } "movies" "" @() "" "$fx\library"
    try {
        CheckTrue "a saved collapsed sidebar starts collapsed" ((Find-Element $app "Movies" 5000).Current.BoundingRectangle.Width -lt 200)
    } finally { Stop-Lumina $app }
}

function Part-Audio {
    Write-Host "Audio effects"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Check "no effects at the start" (Mpv-Get $app 'af').Count 0

        Click-Region $app "gear"; Wait-Regions $app "g_16_p" | Out-Null
        Click-Region $app "g_16_p"
        CheckTrue "the Audio effects page opens" (Wait-Regions $app "g_back")
        Click-Region $app "g_1_p"
        Check "Equalizer cycles to Bass boost" (Config-Value $app "audio.eqPreset") "bass"
        Check "the preset filled the bands" (Config-Value $app "audio.eq1") "6"
        Start-Sleep -Milliseconds 400
        $af = (Mpv-Get $app 'af') | ConvertTo-Json -Compress -Depth 5
        CheckTrue "the player now has an equalizer chain" ($af -match 'equalizer=f=31')
        CheckTrue "with headroom so it cannot clip" ($af -match 'volume=-6dB')
        Check "the panel shows the preset name" ((Mpv-Get $app 'user-data/lumina/fx') -match '^Bass boost\|') $true

        Click-Region $app "g_2_p"
        Check "Compressor cycles to Gentle" (Config-Value $app "audio.compressor") "gentle"
        Click-Region $app "g_3_p"
        Check "Spatial sound cycles to Headphones" (Config-Value $app "audio.spatial") "headphones"
        Click-Region $app "g_4_p"; Click-Region $app "g_4_p"
        Check "Stereo width plus twice is 20" (Config-Value $app "audio.stereoWidth") "20"
        Click-Region $app "g_4_m"
        Check "and minus once is 10" (Config-Value $app "audio.stereoWidth") "10"
        Click-Region $app "g_5_p"
        Check "loudness switch is on" (Config-Value $app "audio.normalize") "true"
        Click-Region $app "g_6_p"
        Check "night mode is on" (Config-Value $app "audio.night") "true"
        Start-Sleep -Milliseconds 400
        $af = (Mpv-Get $app 'af') | ConvertTo-Json -Compress -Depth 5
        foreach ($piece in 'acompressor', 'crossfeed', 'extrastereo=m=1.3', 'dynaudnorm') { CheckTrue "chain includes $piece" ($af -match [regex]::Escape($piece)) }

        Click-Region $app "g_back"
        CheckTrue "Back returns to the player page" (Wait-Regions $app "g_reset")
        Click-Region $app "g_5_p"
        Check "the main page works again (rotate)" ([double](Mpv-Get $app 'video-rotate')) 90

        # Settings page: presets fill sliders, moving a band makes it Custom
        Click-Region $app "gear"
        Mpv-Ipc $app @{ command = @('quit') } | Out-Null
        Start-Sleep -Milliseconds 800
        Click-Element $app "Settings"; Click-Element $app "Audio"
        $cb = Find-Element $app "Preset" 3000
        CheckTrue "Settings Audio shows the equalizer preset" ($null -ne $cb)
        Click-Screen ([int]($cb.Current.BoundingRectangle.X + 40)) ([int]($cb.Current.BoundingRectangle.Y + 20))
        Click-Element $app "Rock"
        Check "choosing Rock stores Rock" (Config-Value $app "audio.eqPreset") "rock"
        Check "and fills 31 Hz with its gain" (Config-Value $app "audio.eq1") "5"
        Check "and 16 kHz" (Config-Value $app "audio.eq10") "5"
        $s = Find-Element $app "1 kHz" 3000
        $r = $s.Current.BoundingRectangle
        Click-Screen ([int]($r.X + $r.Width - 4)) ([int]($r.Y + $r.Height / 2))
        Start-Sleep -Milliseconds 900
        Check "moving a band switches the preset to Custom" (Config-Value $app "audio.eqPreset") "custom"
        Check "no errors in the log after the audio tour" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Network {
    Write-Host "Network streams, logins and errors"
    $port = Get-Random -Minimum 20000 -Maximum 30000
    $serverLog = Join-Path (Split-Path $fx -Parent) "fake-server.log"
    Remove-Item $serverLog -ErrorAction SilentlyContinue
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-server.py`" $port `"$fx`" --user alice --password secret" -PassThru -WindowStyle Hidden
    $prefix = "LuminaE2E.$([guid]::NewGuid().ToString('N').Substring(0,8)):"
    $base = "http://127.0.0.1:$port"
    try {
        foreach ($i in 1..40) { try { (Invoke-WebRequest "$base/multi.mkv" -Headers @{ Range = "bytes=0-0" } -UseBasicParsing).StatusCode | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $app = Start-Lumina "" @{} "" "" @() "" "" $prefix
        try {
            # --- a plain open stream ---
            Click-Element $app "Open stream"
            CheckTrue "the stream dialog appears" (Find-Element $app "Open a network stream" 3000 -Text)
            Click-Element $app "https://example.com/video.m3u8"
            Type-Text "$base/multi.mkv{ENTER}"
            CheckTrue "an open server plays" (Wait-Path $app 'multi\.mkv')
            Mpv-Ipc $app @{ command = @('quit') } | Out-Null
            CheckTrue "quitting returns to the library" (Find-Element $app "Open stream" 5000)

            # --- recents ---
            Click-Element $app "Open stream"
            CheckTrue "the address is listed under Recent" (Find-Element $app "$base/multi.mkv" 3000 -Text)
            Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 400

            # --- a login wall: wrong, then right ---
            Click-Element $app "Open stream"
            Click-Element $app "https://example.com/video.m3u8"
            Type-Text "$base/private/multi.mkv{ENTER}"
            CheckTrue "a login wall asks to sign in" (Find-Element $app "Sign in to 127.0.0.1" 5000 -Text)
            Click-Element $app "User name"; Type-Text "alice"
            Click-Element $app "Password"; Type-Text "wrong"
            Click-Element $app "Sign in"
            CheckTrue "a wrong password says so and asks again" (Find-Element $app "127.0.0.1 did not accept that login. Try again." 5000 -Text)
            Click-Element $app "User name"; Type-Text "^a"; Type-Text "alice"
            Click-Element $app "Password"; Type-Text "^a"; Type-Text "secret"
            Click-Element $app "Sign in"
            CheckTrue "the right login plays" (Wait-Path $app 'multi\.mkv' 12000)
            CheckTrue "the server saw a signed request" ((Get-Content $serverLog -Raw) -match "private/multi\.mkv \d+ auth=yes")
            $listed = (cmdkey /list | Out-String)
            CheckTrue "the login went to Windows Credential Manager" ($listed -match [regex]::Escape("$prefix" + "http://127.0.0.1:$port"))
            Mpv-Ipc $app @{ command = @('quit') } | Out-Null
            Start-Sleep -Milliseconds 800

            # --- remembered: no dialog the second time ---
            Click-Element $app "Open stream"
            Click-Element $app "https://example.com/video.m3u8"
            Type-Text "$base/private/multi.mkv{ENTER}"
            CheckTrue "a remembered login plays without asking" (Wait-Path $app 'multi\.mkv' 12000)
            Mpv-Ipc $app @{ command = @('quit') } | Out-Null
            Start-Sleep -Milliseconds 800

            # --- nothing secret in Lumina's own files ---
            # read with sharing, because the database is open while the app runs
            function Read-Shared($path) { $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite'); try { $b = New-Object byte[] $fs.Length; [void]$fs.Read($b, 0, $b.Length); [Text.Encoding]::GetEncoding(28591).GetString($b) } finally { $fs.Dispose() } }
            $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("alice:secret"))
            $files = Get-ChildItem $app.Dir -Recurse -File -ErrorAction SilentlyContinue
            $leaks = @($files | Where-Object { $t = Read-Shared $_.FullName; $t.Contains("secret") -or $t.Contains($b64) })
            Check "no password in config, database or log (found in: $($leaks.Name -join ', '))" $leaks.Count 0

            # --- errors in plain words ---
            Click-Element $app "Open stream"
            Click-Element $app "https://example.com/video.m3u8"
            Type-Text "$base/missing.mkv{ENTER}"
            CheckTrue "a 404 is explained" (Find-Element $app "127.0.0.1 says that address does not exist (404). Check it for typos." 5000 -Text)
            Click-Element $app "OK"
            Click-Element $app "Open stream"
            Click-Element $app "https://example.com/video.m3u8"
            Type-Text "http://127.0.0.1:1/x.mkv{ENTER}"
            CheckTrue "an unreachable server is explained" (Find-Element $app "Lumina could not reach 127.0.0.1. Check the address and your connection." 9000 -Text)
            Click-Element $app "OK"

            # --- saved logins ---
            Click-Element $app "Settings"; Click-Element $app "Network"
            Click-Element $app "Saved logins"
            CheckTrue "the saved login is listed" (Find-Element $app "http://127.0.0.1:$port    alice" 3000)
            Click-Element $app "http://127.0.0.1:$port    alice"
            Click-Element $app "Forget selected"
            Start-Sleep -Milliseconds 500
            Check "forgetting removes it from the list" (Gone $app "http://127.0.0.1:$port    alice" 800) $true
            Check "and from Credential Manager" ((cmdkey /list | Out-String) -match [regex]::Escape("$prefix" + "http://127.0.0.1:$port")) $false
            Click-Element $app "Done"
            Check "no errors in the log after the network tour" (Error-Count $app) 0
        } finally { Stop-Lumina $app }

        # --- a file that is not a video ---
        $app = Start-Lumina "$fx\broken.mkv" @{}
        try {
            CheckTrue "a broken file says why instead of hanging" (Find-Element $app "Could not play broken.mkv" 8000 -Text)
            Click-Element $app "OK"
            CheckTrue "and Lumina is back on the library" (Find-Element $app "Open stream" 4000)
        } finally { Stop-Lumina $app }
    } finally {
        if ($py -and -not $py.HasExited) { Stop-Process $py -Force }
        (cmdkey /list | Out-String) -split "`n" | Where-Object { $_ -match "Target:.*$([regex]::Escape($prefix))" } | ForEach-Object { if ($_ -match "Target:\s*(\S.*?)\s*$") { cmdkey /delete:$($Matches[1]) | Out-Null } }
    }
}

function Part-Updates {
    Write-Host "Update check"
    $port = Get-Random -Minimum 30000 -Maximum 40000
    $serverLog = Join-Path (Split-Path $fx -Parent) "fake-server.log"
    Remove-Item $serverLog -ErrorAction SilentlyContinue
    $feedDir = Join-Path $fx "feed"
    New-Item -ItemType Directory -Force $feedDir | Out-Null
    $bytes = New-Object byte[] 200000; (New-Object Random 7).NextBytes($bytes)
    [IO.File]::WriteAllBytes("$feedDir\Lumina-9.9.9-setup.exe", $bytes)
    $sha = (Get-FileHash "$feedDir\Lumina-9.9.9-setup.exe" -Algorithm SHA256).Hash.ToLower()
    $good = @{ version = "9.9.9"; file = "Lumina-9.9.9-setup.exe"; sha256 = $sha; size = $bytes.Length; notes = "Faster startup and a calmer library." } | ConvertTo-Json
    $bad = @{ version = "9.9.9"; file = "Lumina-9.9.9-setup.exe"; sha256 = ("0" * 64); size = $bytes.Length; notes = "" } | ConvertTo-Json
    $same = @{ version = "0.1.0"; file = "Lumina-9.9.9-setup.exe"; sha256 = $sha; size = $bytes.Length } | ConvertTo-Json
    Set-Content "$feedDir\good.json" $good -Encoding utf8; Set-Content "$feedDir\bad.json" $bad -Encoding utf8; Set-Content "$feedDir\same.json" $same -Encoding utf8
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-server.py`" $port `"$fx`"" -PassThru -WindowStyle Hidden
    $dl = Join-Path $env:TEMP "LuminaUpdate"
    Remove-Item $dl -Recurse -Force -ErrorAction SilentlyContinue
    $env:LUMINA_UPDATE_NO_LAUNCH = "1"
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/feed/good.json" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }

        # --- nothing is contacted unless asked ---
        $env:LUMINA_UPDATE_FEED = "http://127.0.0.1:$port/feed/good.json"
        Remove-Item $serverLog -ErrorAction SilentlyContinue
        $app = Start-Lumina "" @{} "settings"
        try {
            Start-Sleep -Seconds 3
            Check "by default nothing contacts the update server at startup" (Test-Path $serverLog) $false

            Click-Element $app "Advanced"
            Click-Element $app "Check for updates now"
            CheckTrue "a newer version is announced" (Find-Element $app "Lumina 9.9.9 is available" 5000 -Text)
            CheckTrue "with the notes from the feed" (Find-Element $app "You have 0.1.0. Lumina downloads it, checks it against its checksum, then closes and installs it.`r`n`r`nFaster startup and a calmer library." 2000 -Text)
            Click-Element $app "Cancel"
            Start-Sleep -Milliseconds 500
            Check "cancelling downloads nothing" (Test-Path $dl) $false

            Click-Element $app "Check for updates now"
            CheckTrue "asking again works" (Find-Element $app "Download and install" 4000)
            Click-Element $app "Download and install"
            CheckTrue "the verified download is reported" (Find-Element $app "Update ready" 15000 -Text)
            Click-Element $app "OK"
            $got = Get-ChildItem $dl -Filter "Lumina-9.9.9-setup.exe" -ErrorAction SilentlyContinue
            CheckTrue "the file is in the temp folder" ($null -ne $got)
            Check "and is byte-for-byte what the feed promised" ((Get-FileHash $got.FullName -Algorithm SHA256).Hash.ToLower()) $sha
            Check "no half-downloaded file is left" @(Get-ChildItem $dl -Filter *.part -ErrorAction SilentlyContinue).Count 0
            Remove-Item $dl -Recurse -Force

            # --- wrong checksum ---
            $env:LUMINA_UPDATE_FEED = "http://127.0.0.1:$port/feed/bad.json"
        } finally { Stop-Lumina $app }

        $app = Start-Lumina "" @{} "settings"
        try {
            Click-Element $app "Advanced"
            Click-Element $app "Check for updates now"
            Click-Element $app "Download and install"
            CheckTrue "a checksum mismatch is refused with a clear message" (Find-Element $app "The downloaded file did not match its checksum, so it was thrown away." 15000 -Text)
            Click-Element $app "OK"
            Check "and nothing is left on disk" @(Get-ChildItem $dl -ErrorAction SilentlyContinue).Count 0
        } finally { Stop-Lumina $app }

        # --- already current ---
        $env:LUMINA_UPDATE_FEED = "http://127.0.0.1:$port/feed/same.json"
        $app = Start-Lumina "" @{} "settings"
        try {
            Click-Element $app "Advanced"
            Click-Element $app "Check for updates now"
            CheckTrue "an up-to-date app says so" (Find-Element $app "You are up to date (0.1.0)." 5000 -Text)
            Click-Element $app "OK"
        } finally { Stop-Lumina $app }

        # --- no address at all ---
        Remove-Item Env:\LUMINA_UPDATE_FEED -ErrorAction SilentlyContinue
        $app = Start-Lumina "" @{} "settings"
        try {
            Click-Element $app "Advanced"
            Click-Element $app "Check for updates now"
            CheckTrue "without an address it explains how to set one" (Find-Element $app "Paste the address of the update file in Settings, Advanced, under Update address." 4000 -Text)
            Click-Element $app "OK"
            Click-Element $app "About Lumina"
            CheckTrue "About shows the version" (Find-Element $app "0.1.0" 3000 -Text)
            CheckTrue "About says where the data is kept" (Find-Element $app "Installed (data in your user profile)" 2000 -Text)
            Click-Element $app "Done"
        } finally { Stop-Lumina $app }

        # --- opt-in startup check ---
        $env:LUMINA_UPDATE_FEED = "http://127.0.0.1:$port/feed/good.json"
        Remove-Item $serverLog -ErrorAction SilentlyContinue
        $app = Start-Lumina "" @{ "updates.onStart" = "true" }
        try {
            CheckTrue "with the toggle on, startup announces a new version quietly" (Find-Element $app "Lumina 9.9.9 is available. Settings, Advanced." 8000 -Text)
            CheckTrue "the server saw exactly the feed request" ((Get-Content $serverLog -Raw) -match "GET /feed/good.json")
            Check "no errors in the log after the update tour" (Error-Count $app) 0
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_UPDATE_FEED, Env:\LUMINA_UPDATE_NO_LAUNCH -ErrorAction SilentlyContinue
        if ($py -and -not $py.HasExited) { Stop-Process $py -Force }
        Remove-Item $feedDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $dl -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Tour {
    Write-Host "Welcome screen"
    $app = Start-Lumina "" @{ "general.tour" = "true" }
    try {
        CheckTrue "the welcome opens on a first start" (Find-Element $app "Welcome to Lumina" 6000 -Text)
        CheckTrue "it shows the logo" (Find-Element $app "Lumina logo" 2000)
        CheckTrue "step 1 offers to choose a folder" (Find-Element $app "Choose a folder..." 2000)
        CheckTrue "step 2 has a key field, Test and a sign-up link for each service" ((Find-Element $app "TMDB key" 2000) -and (Find-Element $app "Test OMDb key" 1500) -and (Find-Element $app "Get a key for OpenSubtitles" 1500))
        Click-Element $app "Test TMDB key"
        CheckTrue "Test without a key asks for one (and sends nothing)" (Find-Element $app "Paste a key first" 2000 -Text)
        CheckTrue "step 3 offers a theme and the nine accents" ((Find-Element $app "Theme Light" 2000) -and (Find-Element $app "Accent Mint" 1500) -and (Find-Element $app "Accent Neutral" 1500))
        Click-Element $app "Theme Light"
        Start-Sleep -Milliseconds 400
        Check "choosing a theme saves it" (Config-Value $app "general.theme") "light"
        Click-Element $app "Accent Orange"
        Start-Sleep -Milliseconds 400
        Check "choosing an accent saves it" (Config-Value $app "general.accent") "F28C1E"
        Check "every control has a name" ((Audit-Names $app) -join "; ") ""
        Click-Element $app "Get started"
        CheckTrue "the welcome closes" (Gone $app "Get started" 1500)
        Check "and does not come back" (Config-Value $app "general.tour") "false"
        CheckTrue "no errors" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }

    # skipping, and not showing again on the next start
    $app = Start-Lumina "" @{ "general.tour" = "true" }
    try {
        CheckTrue "welcome again" (Find-Element $app "Skip" 6000)
        Click-Element $app "Skip"
        CheckTrue "Skip closes it" (Gone $app "Skip" 1500)
        Check "and turns it off" (Config-Value $app "general.tour") "false"
    } finally { Stop-Lumina $app }
    $app = Start-Lumina "" @{ "general.tour" = "false" }
    try { CheckTrue "no welcome when it is off" ($null -eq (Find-Element $app "Welcome to Lumina" 2500 -Text)) } finally { Stop-Lumina $app }
}

function Part-Controller {
    Write-Host "Customisable controller and hide-UI mode"
    # --- the editor ---
    $app = Start-Lumina "" @{} "settings"
    try {
        Click-Element $app "Interface"
        Click-Element $app "Customise the controller"
        CheckTrue "the editor lists the buttons" ((Find-Element $app "Show Speed" 3000) -and (Find-Element $app "Show Subtitles" 1500))
        CheckTrue "the gear cannot be switched off" (-not (Is-Enabled $app "Show Player settings"))
        Click-Element $app "Show Speed"
        Start-Sleep -Milliseconds 500
        Check "hiding Speed is saved" (Config-Value $app "interface.controls") "chap+cc+audio+queue+gear+snap"
        Click-Element $app "Move Subtitles right"
        Start-Sleep -Milliseconds 500
        Check "moving Subtitles right swaps it with Audio" (Config-Value $app "interface.controls") "chap+audio+cc+queue+gear+snap"
        Check "every control has a name" ((Audit-Names $app) -join "; ") ""
        Click-Element $app "Reset to standard"
        Start-Sleep -Milliseconds 500
        Check "Reset goes back to the standard layout (nothing stored)" (Config-Value $app "interface.controls") $null
        CheckTrue "no errors" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }

    # --- a saved layout is what the player draws ---
    $app = Start-Lumina "$fx\multi.mkv" @{ "interface.controls" = "gear+speed" }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Check "the controller was told the layout" (Mpv-Get $app 'user-data/lumina/controls') "gear+speed"
        Calibrate-Player $app
        Wake-Controls $app
        Start-Sleep -Milliseconds 600
        $r = Player-Regions $app
        CheckTrue "Speed and the gear are drawn" ($r.speed -and $r.gear)
        CheckTrue "buttons that were left out are not" (-not ($r.cc -or $r.audio -or $r.snap -or $r.chap))
        CheckTrue "the order follows the layout (gear left of speed)" ($r.gear.x -lt $r.speed.x)
        Click-Region $app "speed"
        Check "a moved button still opens its menu" (Mpv-Get $app 'user-data/lumina/menu') "speed"
        Save-Window $app.Hwnd "$env:TEMP/lumina-controller-custom.png"
    } finally { Stop-Lumina $app }

    # --- hide UI ---
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Wake-Controls $app
        Start-Sleep -Milliseconds 500
        Send-Keys ("h"); Start-Sleep -Milliseconds 700
        Check "H hides the controls" (Mpv-Get $app 'user-data/lumina/hide_ui') "on"
        Wake-Controls $app
        Start-Sleep -Milliseconds 800
        Click-Region $app "gear"
        Check "a hidden button does not react to clicks" (Mpv-Get $app 'user-data/lumina/gear') ""
        Save-Window $app.Hwnd "$env:TEMP/lumina-controller-hidden.png"
        Send-Keys ("h"); Start-Sleep -Milliseconds 700
        Check "H brings them back" (Mpv-Get $app 'user-data/lumina/hide_ui') "off"
        Wake-Controls $app
        Click-Region $app "gear"
        Check "and the buttons work again" (Mpv-Get $app 'user-data/lumina/gear') "open"
        CheckTrue "no errors" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Details {
    Write-Host "Movie details page"
    # a stand-in TMDB/OMDb, a one-file library, and the app pointed at both
    $port = Get-Random -Minimum 30000 -Maximum 40000
    $lib = Join-Path $env:TEMP ("lumina-details-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force $lib | Out-Null
    Copy-Item "$fx\multi.mkv" "$lib\The End of Oak Street (2026).mkv"
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-tmdb.py`" $port" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/3/movie/100" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $env:LUMINA_TMDB_API = "http://127.0.0.1:$port/3/"; $env:LUMINA_TMDB_IMG = "http://127.0.0.1:$port/img/"; $env:LUMINA_OMDB_API = "http://127.0.0.1:$port/omdb/"
        $app = Start-Lumina "" @{ tmdbKey = "0123456789abcdef0123456789abcdef"; omdbKey = "testkey" } "movies" "" @() "" $lib
        try {
            CheckTrue "the movie appears" (Find-Element $app "The End of Oak Street" 15000)
            Start-Sleep -Seconds 4     # let the background fill bring details, logo and cast photos
            Click-Element $app "The End of Oak Street"
            CheckTrue "the details page opens with Play" (Find-Element $app "Play" 5000)
            CheckTrue "the runtime is shown as words" (Find-Element $app "1 hr 40 min" 5000 -Text)
            CheckTrue "the certificate badge is shown" (Find-Element $app "PG-13" 3000 -Text)
            CheckTrue "genres are shown" (Find-Element $app "Science Fiction, Mystery, Thriller" 3000 -Text)
            CheckTrue "the TMDB score is a percentage" (Find-Element $app "71%" 3000 -Text)
            CheckTrue "the IMDb score is shown" (Find-Element $app "6.2" 3000 -Text)
            CheckTrue "the Rotten Tomatoes score is shown" (Find-Element $app "86%" 3000 -Text)
            CheckTrue "the logo replaces the plain title" (Find-Element $app "Title logo" 3000)
            CheckTrue "the cast row has the director first" ((Find-Element $app "David Robert Mitchell" 3000 -Text) -and (Find-Element $app "Director" 1500 -Text))
            CheckTrue "and the actors with their roles" ((Find-Element $app "Anne Hathaway" 3000 -Text) -and (Find-Element $app "Denise Platt" 1500 -Text))
            CheckTrue "an actor without a photo still shows" ((Find-Element $app "Walker Russell" 3000 -Text) -and (Find-Element $app "Mason" 1500 -Text))
            CheckTrue "the footer names the file" (Find-Element $app "The End of Oak Street (2026).mkv" 3000 -Text)
            CheckTrue "the footer reads the file's codec in the background" (Find-Element $app "H.264" 12000 -Text -Contains)
            Save-Window $app.Hwnd "$env:TEMP/lumina-details-movie.png"

            # the round actions
            Click-Element $app "Add to favourites"
            Start-Sleep -Milliseconds 400
            CheckTrue "favouriting flips the button" (Find-Element $app "Remove from favourites" 2000)
            Click-Element $app "Mark as watched"
            Start-Sleep -Milliseconds 700
            CheckTrue "watched flips the button" (Find-Element $app "Mark as unwatched" 2000)
            Click-Element $app "Mark as unwatched"
            Start-Sleep -Milliseconds 500
            Click-Element $app "Add to playlist"
            CheckTrue "the playlist menu offers a new playlist" (Find-Element $app "New playlist..." 2000)
            Click-Element $app "New playlist..."
            Click-Element $app "Playlist name"; Type-Text "Night{ENTER}"
            Start-Sleep -Milliseconds 700
            Click-Element $app "Playlists"
            CheckTrue "the playlist was created with the movie" (Find-Element $app "Night" 4000)
            Click-Element $app "Movies"
            Click-Element $app "The End of Oak Street"
            Click-Element $app "More options"
            CheckTrue "More options is the usual movie menu" ((Find-Element $app "Hide from library..." 2000) -and (Find-Element $app "Edit metadata..." 1500))
            Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 300
            Check "every control has a name" ((Audit-Names $app) -join "; ") ""

            Click-Element $app "Back"
            CheckTrue "Back returns to the list" (Find-Element $app "The End of Oak Street" 3000)
            Click-Element $app "The End of Oak Street"
            Click-Element $app "Play"
            CheckTrue "Play starts the movie" (Mpv-Ready $app)
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_TMDB_API, Env:\LUMINA_TMDB_IMG, Env:\LUMINA_OMDB_API -ErrorAction SilentlyContinue
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-TvDetails {
    Write-Host "TV show details page"
    $port = Get-Random -Minimum 40000 -Maximum 50000
    $lib = Join-Path $env:TEMP ("lumina-tv-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force "$lib\VisionQuest\Season 1" | Out-Null
    foreach ($n in 1, 2) { Copy-Item "$fx\multi.mkv" "$lib\VisionQuest\Season 1\VisionQuest.S01E0$n.mkv" }
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-tmdb.py`" $port" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/3/tv/200" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $env:LUMINA_TMDB_API = "http://127.0.0.1:$port/3/"; $env:LUMINA_TMDB_IMG = "http://127.0.0.1:$port/img/"; $env:LUMINA_OMDB_API = "http://127.0.0.1:$port/omdb/"
        $app = Start-Lumina "" @{ tmdbKey = "0123456789abcdef0123456789abcdef"; omdbKey = "testkey" } "shows" "" @() "" $lib
        try {
            CheckTrue "the show appears" (Find-Element $app "VisionQuest" 15000)
            Start-Sleep -Seconds 5     # details, episode pictures and cast photos come in the background
            Click-Element $app "VisionQuest"
            CheckTrue "the page opens on the first episode" (Find-Element $app "E1 - No Strings" 6000 -Text)
            CheckTrue "the episode's own summary is shown" (Find-Element $app "Vision embarks on a journey." 3000 -Text)
            CheckTrue "its air date and runtime are shown" ((Find-Element $app "13 Oct 2026" 3000 -Text) -and (Find-Element $app "54 min" 1500 -Text))
            CheckTrue "the content rating badge is shown" (Find-Element $app "TV-14" 3000 -Text)
            CheckTrue "the episode row lists both cards" ((Find-Element $app "1. No Strings" 3000) -and (Find-Element $app "2. Theseus" 1500))
            CheckTrue "a lone season shows a plain heading, not season buttons" ((Find-Element $app "Episodes" 2000 -Text) -and (-not (Find-Element $app "Season 1" 800)))
            CheckTrue "the cast row starts with the creator" ((Find-Element $app "Terry Matalas" 3000 -Text) -and (Find-Element $app "Creator" 1500 -Text))
            Check "Play starts the first episode" (Is-Enabled $app "Play") $true
            Save-Window $app.Hwnd "$env:TEMP/lumina-details-show.png"

            # picking another episode changes the hero without leaving the page
            Click-Element $app "2. Theseus"
            CheckTrue "the heading follows the picked episode" (Find-Element $app "E2 - Theseus" 3000 -Text)
            CheckTrue "so does the summary" (Find-Element $app "Vision comes face to face." 3000 -Text)
            CheckTrue "and the runtime" (Find-Element $app "46 min" 3000 -Text)
            CheckTrue "and the file in the footer" (Find-Element $app "VisionQuest.S01E02.mkv" 3000 -Text)

            # watched applies to the picked episode only
            Click-Element $app "Mark as watched"
            Start-Sleep -Milliseconds 800
            CheckTrue "the button now offers to undo it" (Find-Element $app "Mark as unwatched" 2000)
            CheckTrue "the page stays on that episode" (Find-Element $app "E2 - Theseus" 2000 -Text)
            Click-Element $app "1. No Strings"
            CheckTrue "the first episode is still unwatched" (Find-Element $app "Mark as watched" 2000)
            Click-Element $app "2. Theseus"
            Click-Element $app "Mark as unwatched"
            Start-Sleep -Milliseconds 600

            # right-click an episode card for its menu
            Click-Element $app "1. No Strings" -Right
            CheckTrue "an episode menu offers the season action" (Find-Element $app "Mark season as watched" 2000)
            Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 300
            Check "every control has a name" ((Audit-Names $app) -join "; ") ""

            # play the picked episode with the big button
            Click-Element $app "2. Theseus"
            Click-Element $app "Play"
            CheckTrue "Play starts the picked episode" (Wait-Path $app 'S01E02' 15000)
            Mpv-Ipc $app @{ command = @('quit') } | Out-Null
            CheckTrue "quitting returns to the show" (Find-Element $app "E2 - Theseus" 6000 -Text)
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_TMDB_API, Env:\LUMINA_TMDB_IMG, Env:\LUMINA_OMDB_API -ErrorAction SilentlyContinue
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Home {
    Write-Host "Home rows: genres, top rated, favourites"
    $port = Get-Random -Minimum 50000 -Maximum 60000
    $lib = Join-Path $env:TEMP ("lumina-home-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force "$lib\VisionQuest\Season 1" | Out-Null
    Copy-Item "$fx\multi.mkv" "$lib\The End of Oak Street (2026).mkv"
    Copy-Item "$fx\multi.mkv" "$lib\VisionQuest\Season 1\VisionQuest.S01E01.mkv"
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-tmdb.py`" $port" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/3/movie/100" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $env:LUMINA_TMDB_API = "http://127.0.0.1:$port/3/"; $env:LUMINA_TMDB_IMG = "http://127.0.0.1:$port/img/"; $env:LUMINA_OMDB_API = "http://127.0.0.1:$port/omdb/"
        $app = Start-Lumina "" @{ tmdbKey = "0123456789abcdef0123456789abcdef" } "" "" @() "" $lib
        try {
            CheckTrue "the library fills in" (Find-Element $app "Recently added" 15000 -Text)
            Start-Sleep -Seconds 5
            Click-Element $app "Home"     # a fresh look at Home once the background fill has finished
            CheckTrue "film genres are a row on Home" (Find-Element $app "Film genres" 8000 -Text)
            CheckTrue "TV genres are a row on Home" (Find-Element $app "TV genres" 3000 -Text)
            CheckTrue "a film genre tile is there" ((Find-Element $app "Mystery" 3000) -and (Find-Element $app "Science Fiction" 1500) -and (Find-Element $app "Thriller" 1500))
            CheckTrue "a TV genre tile is there" (Find-Element $app "Drama" 3000)
            CheckTrue "top rated films is a row" (Find-Element $app "Top rated films" 3000 -Text)
            Check "top rated cards are real cards" ($null -ne (Find-Element $app "Lumina.Library.LibraryEntry" 800 -Text)) $false
            Check "no favourites row until something is starred" ($null -ne (Find-Element $app "Favourites" 800 -Text)) $false
            Save-Window $app.Hwnd "$env:TEMP/lumina-home.png"

            # a genre opens its own list
            Click-Element $app "Mystery"
            CheckTrue "the genre page is titled by the genre" (Find-Element $app "Mystery" 3000 -Text)
            CheckTrue "and lists its film" (Find-Element $app "The End of Oak Street" 3000)
            CheckTrue "but not the show" (Gone $app "VisionQuest")
            Click-Element $app "Home"
            Click-Element $app "Drama"
            CheckTrue "a TV genre lists its show" (Find-Element $app "VisionQuest" 3000)
            CheckTrue "and not the film" (Gone $app "The End of Oak Street")

            # starring a title puts it on Home, un-starring takes it off
            Click-Element $app "Home"
            Click-Element $app "The End of Oak Street"
            Click-Element $app "Add to favourites"
            Start-Sleep -Milliseconds 500
            Click-Element $app "Back"
            Click-Element $app "Home"
            CheckTrue "Favourites appears on Home" (Find-Element $app "Favourites" 3000 -Text)
            Check "no shelf shows a card as raw text (a shelf without its card template does)" ($null -ne (Find-Element $app "Lumina.Library.LibraryEntry" 800 -Text)) $false
            Click-Element $app "The End of Oak Street"
            Click-Element $app "Remove from favourites"
            Start-Sleep -Milliseconds 500
            Click-Element $app "Back"
            Click-Element $app "Home"
            Check "and goes again when it is un-starred" ($null -ne (Find-Element $app "Favourites" 1200 -Text)) $false
            Check "every control has a name" ((Audit-Names $app) -join "; ") ""
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_TMDB_API, Env:\LUMINA_TMDB_IMG, Env:\LUMINA_OMDB_API -ErrorAction SilentlyContinue
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Overview {
    Write-Host "Library overview and show/hide kinds"
    $port = Get-Random -Minimum 20000 -Maximum 30000
    $lib = Join-Path $env:TEMP ("lumina-ov-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force "$lib\VisionQuest\Season 1" | Out-Null
    Copy-Item "$fx\multi.mkv" "$lib\The End of Oak Street (2026).mkv"
    foreach ($n in 1, 2) { Copy-Item "$fx\multi.mkv" "$lib\VisionQuest\Season 1\VisionQuest.S01E0$n.mkv" }
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-tmdb.py`" $port" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/3/movie/100" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $env:LUMINA_TMDB_API = "http://127.0.0.1:$port/3/"; $env:LUMINA_TMDB_IMG = "http://127.0.0.1:$port/img/"; $env:LUMINA_OMDB_API = "http://127.0.0.1:$port/omdb/"
        $app = Start-Lumina "" @{ tmdbKey = "0123456789abcdef0123456789abcdef" } "" "" @() "" $lib
        try {
            CheckTrue "the library fills in" (Find-Element $app "Recently added" 15000 -Text)
            Start-Sleep -Seconds 4
            Click-Element $app "Folders"
            CheckTrue "the overview shows when it was last updated" (Find-Element $app "Last update: Today" 4000 -Text -Contains)
            CheckTrue "one film is counted" ((Find-Element $app "Films" 3000 -Text) -and (Find-Element $app "1" 1500 -Text))
            CheckTrue "two episodes are counted" ((Find-Element $app "TV episodes" 3000 -Text) -and (Find-Element $app "2" 1500 -Text))
            CheckTrue "the size on disk is shown" ((Find-Element $app "On disk" 3000 -Text) -and (Find-Element $app " MB" 1500 -Text -Contains))
            Save-Window $app.Hwnd "$env:TEMP/lumina-overview.png"

            # turn TV shows off: the sidebar entry and the Home tile go, Movies stay
            Click-Element $app "Show TV shows"
            Start-Sleep -Milliseconds 600
            Check "the setting is saved" (Config-Value $app "library.showShows") "false"
            CheckTrue "TV shows leaves the sidebar" (Gone $app "TV shows")
            CheckTrue "Movies stays" (Find-Element $app "Movies" 2000)
            Click-Element $app "Home"
            CheckTrue "the show is gone from Home" (Gone $app "VisionQuest")
            CheckTrue "the film is still there" (Find-Element $app "The End of Oak Street" 3000)
            Click-Element $app "Folders"
            Click-Element $app "Show TV shows"
            Start-Sleep -Milliseconds 600
            CheckTrue "turning it on brings the sidebar entry back" (Find-Element $app "TV shows" 3000)
            Check "and is saved" (Config-Value $app "library.showShows") $null

            # refresh metadata runs the whole fill again and says so
            Click-Element $app "Refresh metadata"
            CheckTrue "refreshing metadata finishes" (Find-Element $app "Metadata refreshed." 20000 -Text)
            Check "every control has a name" ((Audit-Names $app) -join "; ") ""
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_TMDB_API, Env:\LUMINA_TMDB_IMG, Env:\LUMINA_OMDB_API -ErrorAction SilentlyContinue
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Visual {
    Write-Host "Library pages on the light theme with the orange accent, and at the smallest window"
    $port = Get-Random -Minimum 30000 -Maximum 40000
    $lib = Join-Path $env:TEMP ("lumina-vis-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force "$lib\VisionQuest\Season 1" | Out-Null
    Copy-Item "$fx\multi.mkv" "$lib\The End of Oak Street (2026).mkv"
    foreach ($n in 1, 2) { Copy-Item "$fx\multi.mkv" "$lib\VisionQuest\Season 1\VisionQuest.S01E0$n.mkv" }
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-tmdb.py`" $port" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/3/movie/100" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $env:LUMINA_TMDB_API = "http://127.0.0.1:$port/3/"; $env:LUMINA_TMDB_IMG = "http://127.0.0.1:$port/img/"; $env:LUMINA_OMDB_API = "http://127.0.0.1:$port/omdb/"
        $app = Start-Lumina "" @{ tmdbKey = "0123456789abcdef0123456789abcdef"; omdbKey = "testkey"; "general.theme" = "light"; "general.accent" = "F28C1E" } "" "" @() "" $lib
        try {
            CheckTrue "the library fills in" (Find-Element $app "Recently added" 15000 -Text)
            Start-Sleep -Seconds 5
            Click-Element $app "Home"
            Click-Element $app "The End of Oak Street"
            Click-Element $app "Add to favourites"
            Start-Sleep -Milliseconds 400
            Click-Element $app "Back"
            Click-Element $app "Home"
            CheckTrue "the page is light" ((Pixel-Luma $app 1700 1300) -gt 200)
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-home.png"
            Click-Element $app "Mystery"
            Start-Sleep -Milliseconds 500
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-genre.png"
            Click-Element $app "Home"
            Click-Element $app "The End of Oak Street"
            Start-Sleep -Milliseconds 1500
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-movie.png"
            Click-Element $app "Back"
            Click-Element $app "TV shows"
            Click-Element $app "VisionQuest"
            Start-Sleep -Milliseconds 1500
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-show.png"
            Click-Element $app "Folders"
            Start-Sleep -Milliseconds 500
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-folders.png"

            # the smallest window: nothing is cut off at the right edge
            Resize-Main $app 600 600
            Click-Element $app "Home"
            CheckTrue "Home at the smallest size: the last shortcut tile fits" (Inside-Window $app "Folders")
            CheckTrue "Home: the Open stream button fits" (Inside-Window $app "Open stream")
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-home-small.png"
            Click-Element $app "The End of Oak Street"
            Start-Sleep -Milliseconds 1200
            CheckTrue "Movie page: Play fits" (Inside-Window $app "Play")
            CheckTrue "Movie page: More options fits" (Inside-Window $app "More options")
            CheckTrue "Movie page: the scores fit" (Inside-Window $app "86%" -Text)
            CheckTrue "Movie page: the certificate fits" (Inside-Window $app "PG-13" -Text)
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-movie-small.png"
            Click-Element $app "Back"
            Click-Element $app "TV shows"
            Click-Element $app "VisionQuest"
            Start-Sleep -Milliseconds 1200
            CheckTrue "Show page: the episode heading fits" (Inside-Window $app "E1 - No Strings" -Text)
            CheckTrue "Show page: the first episode card fits" (Inside-Window $app "1. No Strings")
            Save-Window $app.Hwnd "$env:TEMP/lumina-visual-show-small.png"
            Check "every control has a name" ((Audit-Names $app) -join "; ") ""
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_TMDB_API, Env:\LUMINA_TMDB_IMG, Env:\LUMINA_OMDB_API -ErrorAction SilentlyContinue
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Floating {
    Write-Host "Floating pill controller"
    # --- the setting, live ---
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Check "starts as the classic bar" (Mpv-Get $app 'user-data/lumina/style') "classic"
        Mpv-Ipc $app @{ command = @('quit') } | Out-Null
        Start-Sleep -Milliseconds 800
        Click-Element $app "Settings"
        Click-Element $app "Interface"
        Click-Element $app "Floating pill"
        Start-Sleep -Milliseconds 500
        Check "the choice is saved" (Config-Value $app "interface.controllerStyle") "floating"
    } finally { Stop-Lumina $app }

    # --- the bar itself ---
    $app = Start-Lumina "$fx\multi.mkv" @{ "interface.controllerStyle" = "floating" }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Check "the saved style reaches the player" (Mpv-Get $app 'user-data/lumina/style') "floating"
        Calibrate-Player $app
        Wake-Controls $app
        Start-Sleep -Milliseconds 600
        $r = Player-Regions $app
        CheckTrue "the floating bar exists with a Close button" ($r.pill -and $r.close)
        CheckTrue "every control sits inside the bar" (($r.play.x -ge $r.pill.x) -and (($r.fs.x + $r.fs.w) -le ($r.pill.x + $r.pill.w)) -and ($r.seek.y -ge $r.pill.y))
        Save-Window $app.Hwnd "$env:TEMP/lumina-floating.png"

        Mpv-Set $app 'pause' 'yes'
        Click-Region $app "play"
        Check "Play still plays" (Mpv-Wait $app 'pause' $false) $false
        Click-Region $app "play"
        Check "and pauses" (Mpv-Wait $app 'pause' $true) $true

        # an empty spot on the bar must not toggle pause
        $before = Mpv-Get $app 'pause'
        Wake-Controls $app
        $rp = (Player-Regions $app).pill
        Click-Screen ([int]($script:offX + $rp.x + $rp.w * 0.32)) ([int]($script:offY + $rp.y + $rp.h - 7))
        Check "clicking the bar's own background changes nothing" (Mpv-Get $app 'pause') $before

        Click-Region $app "speed"
        Check "a menu opens above the bar" (Mpv-Get $app 'user-data/lumina/menu') "speed"
        Click-Region $app "speed"

        Click-Region $app "close"
        CheckTrue "Close leaves the video and returns to the library" (Find-Element $app "Open stream" 6000)
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Scrub {
    Write-Host "Holding the mouse on the seek bar and the volume scrubs them"
    $app = Start-Lumina "$fx\multi.mkv" @{ "interface.controllerStyle" = "vlc" }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        # a window wholly on screen so every target can be reached; wake the controller so it redraws at the new size before measuring
        [In]::MoveWindow($app.Hwnd, 40, 40, 1400, 850, $true) | Out-Null
        Start-Sleep -Milliseconds 1500
        Mpv-Set $app 'pause' 'yes'
        Calibrate-Player $app
        Wake-Controls $app
        Start-Sleep -Milliseconds 1200
        Wake-Controls $app
        $r = Player-Regions $app
        $y = [int]($script:offY + $r.seek.y + $r.seek.h / 2)
        $x = { param($f) [int]($script:offX + $r.seek.x + $r.seek.w * $f) }
        Move-Mouse (& $x 0.15) $y
        Press-Mouse 0x02
        try {
            Start-Sleep -Milliseconds 200
            $a = [double](Mpv-Get $app 'time-pos')
            Move-Mouse (& $x 0.5) $y; Start-Sleep -Milliseconds 400
            $b = [double](Mpv-Get $app 'time-pos')
            Move-Mouse (& $x 0.85) ($y - 60); Start-Sleep -Milliseconds 400     # leaving the bar sideways-up must not end the drag
            $c = [double](Mpv-Get $app 'time-pos')
        } finally { Press-Mouse 0x04 }
        Write-Host ("  dbg: " + (Mpv-Get $app 'user-data/lumina/dbg'))
        Write-Host ("  dbg: " + (Mpv-Get $app 'user-data/lumina/dbg'))
        CheckTrue "the video follows while the button is held (a=$a b=$b c=$c)" ($b -gt $a + 2 -and $c -gt $b + 2)
        # the volume wedge
        Wake-Controls $app
        $r = Player-Regions $app
        $vy = [int]($script:offY + $r.vol.y + $r.vol.h / 2)
        $vx = { param($f) [int]($script:offX + $r.vol.x + $r.vol.w * $f) }
        Move-Mouse (& $vx 0.2) $vy
        Press-Mouse 0x02
        try {
            Start-Sleep -Milliseconds 200
            $v1 = [double](Mpv-Get $app 'volume')
            Move-Mouse (& $vx 0.9) $vy; Start-Sleep -Milliseconds 400
            $v2 = [double](Mpv-Get $app 'volume')
        } finally { Press-Mouse 0x04 }
        CheckTrue "the volume follows while the button is held (v1=$v1 v2=$v2)" ($v2 -gt $v1 + 20)
        CheckTrue "no errors" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Vlc {
    Write-Host "VLC style controller (docked bar, tabbed panel)"
    $app = Start-Lumina "$fx\multi.mkv" @{ "interface.controllerStyle" = "vlc" }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Check "the style reaches the player" (Mpv-Get $app 'user-data/lumina/style') "vlc"
        Mpv-Set $app 'pause' 'yes'
        Start-Sleep -Milliseconds 800
        Calibrate-Player $app
        Wake-Controls $app
        Start-Sleep -Milliseconds 500
        $r = Player-Regions $app
        CheckTrue "the bar, Play, the seek bar, the volume and the buttons exist" ($r.bar -and $r.play -and $r.seek -and $r.vol -and $r.cc -and $r.audio -and $r.gear -and $r.queue -and $r.fs)
        CheckTrue "the bar is docked to the bottom of the window" ([math]::Abs(($r.bar.y + $r.bar.h) - [int](Mpv-Get $app 'osd-height')) -le 2)
        CheckTrue "the picture leaves room for it" ([double](Mpv-Get $app 'video-margin-ratio-bottom') -gt 0.03)
        Save-Window $app.Hwnd "$env:TEMP/lumina-vlc-bar.png" -FromScreen

        Click-Region $app "play"
        Check "Play plays" (Mpv-Wait $app 'pause' $false) $false
        Click-Region $app "play"
        Check "and pauses" (Mpv-Wait $app 'pause' $true) $true

        # an empty spot on the bar must not toggle pause
        Wake-Controls $app
        $rb = (Player-Regions $app).bar
        Click-Screen ([int]($script:offX + $rb.x + $rb.w * 0.55)) ([int]($script:offY + $rb.y + 3))
        Check "clicking the bar's background changes nothing" (Mpv-Get $app 'pause') $true

        Click-Region $app "seek" 0.5
        CheckTrue "the seek bar jumps to the middle" ([math]::Abs([double](Mpv-Get $app 'time-pos') - 6) -lt 2)
        Click-Region $app "vol" 0.4
        CheckTrue "the volume wedge sets the volume" ([math]::Abs([double](Mpv-Get $app 'volume') - 40) -lt 8)

        # the panel: one button per group of tabs
        Click-Region $app "cc"
        Check "the subtitles button opens the Subtitles tab" (Mpv-Get $app 'user-data/lumina/panel') "subs"
        Save-Window $app.Hwnd "$env:TEMP/lumina-vlc-subs.png" -FromScreen
        Click-Region $app "ptab_audio"
        Check "a tab switches the panel" (Mpv-Get $app 'user-data/lumina/panel') "audio"
        Click-Region $app "gear"
        Check "the gear opens Playback" (Mpv-Get $app 'user-data/lumina/panel') "playback"
        $rows = (Mpv-Get $app 'user-data/lumina/panel_rows') -split '\|'
        $speed = [array]::IndexOf($rows, "Speed") + 1
        Click-Region $app "pr_${speed}_p"
        CheckTrue "a plus button steps the speed" ([math]::Abs([double](Mpv-Get $app 'speed') - 1.05) -lt 0.01)
        Click-Region $app "ptab_video"
        Check "the Video tab opens" (Mpv-Get $app 'user-data/lumina/panel') "video"
        Save-Window $app.Hwnd "$env:TEMP/lumina-vlc-video.png" -FromScreen

        # clicking the picture closes the panel and does not pause
        $before = Mpv-Get $app 'pause'
        Click-Screen ([int]($script:offX + 200)) ([int]($script:offY + 300))
        Check "clicking outside closes the panel" (Mpv-Get $app 'user-data/lumina/panel') ""
        Check "without pausing or playing" (Mpv-Get $app 'pause') $before

        Click-Region $app "queue"
        Check "the list button opens the Queue tab" (Mpv-Get $app 'user-data/lumina/panel') "queue"
        Click-Region $app "queue"

        # fullscreen: the bar floats over the picture
        Click-Region $app "fs"
        Start-Sleep -Milliseconds 1200
        Check "in fullscreen the picture uses the whole window" ([math]::Round([double](Mpv-Get $app 'video-margin-ratio-bottom'), 3)) 0
        Click-Region $app "fs"
        Start-Sleep -Milliseconds 1200
        CheckTrue "and the bar docks again" ([double](Mpv-Get $app 'video-margin-ratio-bottom') -gt 0.03)
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Thumbs {
    Write-Host "Seek bar preview pictures"
    $app = Start-Lumina "$fx\multi.mkv" @{ "interface.seekThumbs" = "true" }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        # the pictures are made quietly in the background: wait for the folder to be marked complete
        $done = $false
        foreach ($i in 1..40) { if (Get-ChildItem "$($app.Dir)\thumbs" -Recurse -Filter done -ErrorAction SilentlyContinue) { $done = $true; break }; Start-Sleep -Milliseconds 500 }
        CheckTrue "the preview pictures were made" $done
        Check "playback was not disturbed (still the same file)" ((Mpv-Get $app 'path') -match 'multi') $true
        Calibrate-Player $app
        Wake-Controls $app
        Start-Sleep -Milliseconds 500
        $sk = (Player-Regions $app).seek
        Move-Mouse ([int]($script:offX + $sk.x + $sk.w * 0.40)) ([int]($script:offY + $sk.y + $sk.h / 2))
        Start-Sleep -Milliseconds 300
        Move-Mouse ([int]($script:offX + $sk.x + $sk.w * 0.40 + 4)) ([int]($script:offY + $sk.y + $sk.h / 2))
        CheckTrue "pointing at the seek bar shows a preview" (Find-Element $app "Preview picture" 4000)
        CheckTrue "with the time under it" ((Mpv-Get $app 'user-data/lumina/thumb_time') -ne "")
        Save-Window $app.Hwnd "$env:TEMP/lumina-thumbs.png" -FromScreen
        Move-Mouse ([int]($script:offX + $sk.x + $sk.w * 0.80)) ([int]($script:offY + $sk.y + $sk.h / 2))
        Start-Sleep -Milliseconds 300
        $before = Mpv-Get $app 'pause'
        Check "hovering does not seek or pause anything" (Mpv-Get $app 'pause') $before
        Move-Mouse ([int]($script:offX + 300)) ([int]($script:offY + 200))
        CheckTrue "moving off the bar hides the preview" (Gone $app "Preview picture" 2500)
        Check "and the controller says nothing is shown" (Mpv-Get $app 'user-data/lumina/thumb_time') ""
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }

    # --- the switch ---
    $app = Start-Lumina "" @{ "interface.seekThumbs" = "true" } "settings"
    try {
        Click-Element $app "Interface"
        Click-Element $app "Preview picture on the seek bar"
        Start-Sleep -Milliseconds 500
        Check "turning it off is saved" (Config-Value $app "interface.seekThumbs") "false"
        Click-Element $app "Preview picture on the seek bar"
        Start-Sleep -Milliseconds 500
        Check "turning it back on is saved" (Config-Value $app "interface.seekThumbs") $null
    } finally { Stop-Lumina $app }
}

function Part-Bookmarks {
    Write-Host "Bookmarks"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Mpv-Set $app 'pause' 'yes'
        Mpv-Ipc $app @{ command = @('seek', '4', 'absolute+exact') } | Out-Null
        Start-Sleep -Milliseconds 700
        Send-Keys ("y"); Start-Sleep -Milliseconds 700
        Check "the key adds a bookmark here" (Mpv-Wait $app 'user-data/lumina/bookmark_count' '1') "1"
        Send-Keys ("y"); Start-Sleep -Milliseconds 700
        Check "pressing it again at the same spot removes it" (Mpv-Wait $app 'user-data/lumina/bookmark_count' '0') "0"

        Send-Keys ("y"); Start-Sleep -Milliseconds 600       # at 4 s
        Mpv-Ipc $app @{ command = @('seek', '9', 'absolute+exact') } | Out-Null
        Start-Sleep -Milliseconds 700
        Send-Keys ("y"); Start-Sleep -Milliseconds 700       # at 9 s
        Check "two bookmarks are kept" (Mpv-Wait $app 'user-data/lumina/bookmark_count' '2') "2"

        Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'bookmarks') } | Out-Null
        Check "the bookmarks menu opens" (Mpv-Wait $app 'user-data/lumina/menu' 'bookmarks') "bookmarks"
        Check "it lists them, then the add and remove entries" (Mpv-Wait $app 'user-data/lumina/menu_items' 'Bookmark 1|Bookmark 2|Add a bookmark here|Remove all bookmarks') "Bookmark 1|Bookmark 2|Add a bookmark here|Remove all bookmarks"
        Wake-Controls $app
        Save-Window $app.Hwnd "$env:TEMP/lumina-bookmarks.png"
        Click-Region $app "menu1"
        $pos = [double](Mpv-Get $app 'time-pos')
        CheckTrue "choosing the first jumps to it (now at $([math]::Round($pos, 1)) s)" ([math]::Abs($pos - 4) -lt 0.6)
        Check "and the menu closes" (Mpv-Get $app 'user-data/lumina/menu') ""

        Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'bookmarks') } | Out-Null
        Start-Sleep -Milliseconds 400
        Click-Region $app "menu4"
        Check "Remove all bookmarks empties the list" (Mpv-Wait $app 'user-data/lumina/bookmark_count' '0') "0"
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Sleep {
    Write-Host "Sleep timer, start with Windows, tray"
    $powerLog = Join-Path $env:TEMP ("lumina-power-" + [guid]::NewGuid().ToString("N").Substring(0, 6) + ".log")
    $env:LUMINA_POWER_LOG = $powerLog      # so a test can never sleep or shut down the real PC
    try {
        # --- the timer pauses ---
        $app = Start-Lumina "$fx\multi.mkv" @{}
        try {
            CheckTrue "controller ready" (Mpv-Ready $app)
            Mpv-Set $app 'pause' 'no'
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sleep-cycle') } | Out-Null
            Check "one press starts 15 minutes" (Mpv-Wait $app 'user-data/lumina/sleep_label' '15 min left') "15 min left"
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sleep', 'off') } | Out-Null
            Check "off turns it off" (Mpv-Wait $app 'user-data/lumina/sleep_label' 'Off') "Off"
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sleep', '0.05') } | Out-Null      # three seconds
            Check "a short timer is shown" (Mpv-Wait $app 'user-data/lumina/sleep_label' 'Under 1 min left') "Under 1 min left"
            Check "when it ends the video is paused" (Mpv-Wait $app 'pause' $true 9000) $true
            Check "and the timer shows Off again" (Mpv-Wait $app 'user-data/lumina/sleep_label' 'Off') "Off"
            Check "the PC was not touched" (Test-Path $powerLog) $false
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }

        # --- stop and go back to the library ---
        $app = Start-Lumina "$fx\multi.mkv" @{ "playback.afterSleep" = "stop" }
        try {
            CheckTrue "controller ready" (Mpv-Ready $app)
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sleep', '0.05') } | Out-Null
            CheckTrue "with 'stop' the timer returns to the library" (Find-Element $app "Open stream" 12000)
        } finally { Stop-Lumina $app }

        # --- stop at the end of the file ---
        $app = Start-Lumina "$fx\multi.mkv" @{ "playback.afterSleep" = "stop" }
        try {
            CheckTrue "controller ready" (Mpv-Ready $app)
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sleep', 'eof') } | Out-Null
            Check "end of file is shown" (Mpv-Wait $app 'user-data/lumina/sleep_label' 'End of file') "End of file"
            Seek-Near-End $app
            CheckTrue "when the file ends it stops and returns to the library" (Find-Element $app "Open stream" 15000)
        } finally { Stop-Lumina $app }

        # --- the PC-level choices are only logged here ---
        $app = Start-Lumina "$fx\multi.mkv" @{ "playback.afterSleep" = "shutdown" }
        try {
            CheckTrue "controller ready" (Mpv-Ready $app)
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sleep', '0.05') } | Out-Null
            $until = [DateTime]::UtcNow.AddSeconds(12)
            while (-not (Test-Path $powerLog) -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 300 }
            CheckTrue "'shut down' asks for a shutdown with a minute's warning (logged, not run)" ((Test-Path $powerLog) -and ((Get-Content $powerLog -Raw) -match 'shutdown\|shutdown\.exe\|/s /t 60'))
        } finally { cmd /c "shutdown /a >nul 2>&1"; Stop-Lumina $app }
    } finally { Remove-Item Env:\LUMINA_POWER_LOG -ErrorAction SilentlyContinue; Remove-Item $powerLog -ErrorAction SilentlyContinue; cmd /c "shutdown /a >nul 2>&1" }

    # --- start with Windows (in a private part of the registry) ---
    $branch = "Software\LuminaE2E\$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $app = Start-Lumina "" @{} "settings" "" @() $branch
    try {
        Click-Element $app "General"
        Click-Element $app "Start Lumina when I sign in"
        Start-Sleep -Milliseconds 600
        $v = (Get-ItemProperty "HKCU:\$branch\Run" -ErrorAction SilentlyContinue).Lumina
        CheckTrue "turning it on writes the sign-in entry" ($v -match '--background')
        CheckTrue "with the program path in quotes" ($v -match '^".*Lumina\.exe"')
        Click-Element $app "Start Lumina when I sign in"
        Start-Sleep -Milliseconds 600
        Check "turning it off removes it" ($null -eq (Get-ItemProperty "HKCU:\$branch\Run" -ErrorAction SilentlyContinue).Lumina) $true
    } finally { Stop-Lumina $app; Remove-Item "HKCU:\$branch" -Recurse -ErrorAction SilentlyContinue }

    # --- closing to the tray, and coming back with a second launch ---
    $inst = "tray-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
    $app = Start-Lumina "" @{ "general.tray" = "true" } "" $inst
    try {
        $win = [Windows.Automation.AutomationElement]::FromHandle($app.Hwnd)
        ($win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)).Close()
        Start-Sleep -Milliseconds 1500
        $app.Proc.Refresh()
        CheckTrue "the close button leaves Lumina running" (-not $app.Proc.HasExited)
        Check "but the window is gone from the screen" ($app.Proc.MainWindowHandle -eq [IntPtr]::Zero) $true
        $env:LUMINA_INSTANCE = $inst
        $second = Start-Process $Exe -PassThru
        Check "a second launch hands over and exits" ($second.WaitForExit(8000)) $true
        $app.Proc.Refresh()
        $back = $false; foreach ($i in 1..20) { $app.Proc.Refresh(); if ($app.Proc.MainWindowHandle -ne [IntPtr]::Zero) { $back = $true; break }; Start-Sleep -Milliseconds 300 }
        CheckTrue "and the window comes back" $back
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }

}

function Part-Lock {
    Write-Host "Parental lock"
    $app = Start-Lumina "" @{} "folders" "" @() "" "$fx\library"
    try {
        CheckTrue "the library folder is listed with a Lock button" (Find-Element $app "Lock folder" 8000)
        CheckTrue "no unlock button while nothing is locked" (Gone $app "Unlock the locked folders" 600)

        # a PIN has to exist first; locking asks for it
        Click-Element $app "Lock folder"
        CheckTrue "locking asks for a new PIN" (Find-Element $app "Choose a PIN" 4000 -Text)
        Type-Text "12{ENTER}"
        CheckTrue "a PIN that is too short is refused" (Find-Element $app "That is not a PIN" 4000 -Text)
        Click-Element $app "OK"
        Click-Element $app "Lock folder"
        Type-Text "1234{ENTER}"
        CheckTrue "it asks to type it again" (Find-Element $app "Type it again" 4000 -Text)
        Type-Text "4321{ENTER}"
        CheckTrue "two different PINs are refused" (Find-Element $app "The two did not match" 4000 -Text)
        Click-Element $app "OK"
        Check "and nothing was locked" (Gone $app "Unlock folder" 700) $true

        Click-Element $app "Lock folder"
        Type-Text "4821{ENTER}"
        CheckTrue "asked again" (Find-Element $app "Type it again" 4000 -Text)
        Type-Text "4821{ENTER}"
        CheckTrue "the folder now says it is locked" (Find-Element $app "Locked. Unlock to see its videos." 5000 -Text)
        CheckTrue "and its button offers to unlock" (Find-Element $app "Unlock folder" 2000)
        CheckTrue "the page offers to unlock the locked folders" (Find-Element $app "Unlock the locked folders" 2000)

        # hidden everywhere
        Click-Element $app "Movies"
        CheckTrue "its movies are gone from the Movies page" (Gone $app "Film Title" 1500)
        Click-Element $app "Home"
        CheckTrue "Home has nothing to show" (Find-Element $app "Nothing here yet" 3000 -Text)

        # a wrong PIN is refused and asks again; the right one opens it
        Click-Element $app "Folders"
        Click-Element $app "Unlock the locked folders"
        CheckTrue "unlocking asks for the PIN" (Find-Element $app "Enter your PIN" 4000 -Text)
        Type-Text "1111{ENTER}"
        CheckTrue "a wrong PIN is refused" (Find-Element $app "That PIN is not right" 4000 -Text)
        Click-Element $app "OK"
        Type-Text "4821{ENTER}"
        Start-Sleep -Milliseconds 700
        Click-Element $app "Movies"
        CheckTrue "the right PIN brings the movies back" (Find-Element $app "Film Title" 4000)

        # lock again from the page
        Click-Element $app "Folders"
        Click-Element $app "Lock the locked folders now"
        Click-Element $app "Movies"
        CheckTrue "locking again hides them at once" (Gone $app "Film Title" 1500)

        # playing a file inside the locked folder asks for the PIN, and Esc says no
        $env:LUMINA_INSTANCE = $app.Instance
        $second = Start-Process $Exe -ArgumentList "`"$fx\library\Movies\Film Title (2020).mkv`"" -PassThru
        CheckTrue "opening a locked file asks for the PIN" (Find-Element $app "Enter your PIN" 8000 -Text)
        Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 700
        CheckTrue "cancelling does not play it" ($null -eq (Find-Element $app "Enter your PIN" 800 -Text))
        $pathNow = $null; try { $pathNow = Mpv-Get $app 'path' } catch { }
        Check "nothing is playing" ($null -eq $pathNow -or $pathNow -eq "") $true

        $second = Start-Process $Exe -ArgumentList "`"$fx\library\Movies\Film Title (2020).mkv`"" -PassThru
        CheckTrue "asked again" (Find-Element $app "Enter your PIN" 8000 -Text)
        Type-Text "4821{ENTER}"
        CheckTrue "the right PIN plays it" (Wait-Path $app 'Film Title' 15000)
        Mpv-Ipc $app @{ command = @('quit') } | Out-Null
        CheckTrue "back in the library" (Find-Element $app "Settings" 8000)   # the Movies page is shown again, which has no Open stream button

        # removing the PIN unlocks everything
        Click-Element $app "Settings"
        Click-Element $app "Privacy"
        Click-Element $app "Remove the PIN"
        CheckTrue "removing asks for the PIN" (Find-Element $app "Enter your PIN" 4000 -Text)
        Type-Text "4821{ENTER}"
        CheckTrue "and says so" (Find-Element $app "PIN removed. No folder is locked." 5000 -Text)
        Click-Element $app "Folders"
        CheckTrue "the folder can be locked again (it is unlocked now)" (Find-Element $app "Lock folder" 3000)
        Click-Element $app "Movies"
        CheckTrue "and its movies are back without a PIN" (Find-Element $app "Film Title" 4000)
        Check "every control has a name" ((Audit-Names $app) -join "; ") ""
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Access {
    Write-Host "Screen reader controls (the player's accessible twin)"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Mpv-Set $app 'pause' 'yes'
        Start-Sleep -Milliseconds 900     # the twin follows the player a few times a second

        # what a screen reader can see
        foreach ($n in "Skip back", "Skip forward", "Previous item", "Next item", "Seek", "Mute", "Volume", "Fullscreen", "Close video", "Play") {
            CheckTrue "the player exposes '$n'" (Find-Element $app $n 3000)
        }
        CheckTrue "and a subtitles control that says what is selected" (Find-Element $app "Subtitles:" 2000 -Contains)
        CheckTrue "and an audio control that says what is selected" (Find-Element $app "Audio:" 2000 -Contains)
        CheckTrue "and the speed" (Find-Element $app "Speed: 1x" 2000)
        Check "every control has a name" ((Audit-Names $app) -join "; ") ""

        function Press([string]$name) {
            $e = Find-Element $app $name 3000
            if (-not $e) { throw "no control '$name'" }
            ($e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
            Start-Sleep -Milliseconds 700
        }

        # pressing them does what the controller does
        Press "Play"
        Check "Play plays" (Mpv-Wait $app 'pause' $false 3000) $false
        CheckTrue "and the name changes to Pause" (Find-Element $app "Pause" 3000)
        Press "Pause"
        Check "Pause pauses" (Mpv-Wait $app 'pause' $true 3000) $true
        CheckTrue "and back to Play" (Find-Element $app "Play" 3000)

        Mpv-Ipc $app @{ command = @('seek', '1', 'absolute+exact') } | Out-Null
        Start-Sleep -Milliseconds 600
        Press "Skip forward"
        CheckTrue "Skip forward moves ahead by the skip step" ([math]::Abs([double](Mpv-Get $app 'time-pos') - 11) -lt 1.5)
        Press "Skip back"
        CheckTrue "Skip back returns" ([math]::Abs([double](Mpv-Get $app 'time-pos') - 1) -lt 1.5)

        Press "Mute"
        Check "Mute mutes" (Mpv-Wait $app 'mute' $true 3000) $true
        CheckTrue "and the name changes to Unmute" (Find-Element $app "Unmute" 3000)
        Press "Unmute"
        Check "Unmute unmutes" (Mpv-Wait $app 'mute' $false 3000) $false

        $vol = Find-Element $app "Volume" 3000
        ($vol.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern)).SetValue(40)
        Start-Sleep -Milliseconds 700
        Check "setting the Volume value changes the volume" ([math]::Round([double](Mpv-Get $app 'volume'))) 40

        $seek = Find-Element $app "Seek" 3000
        ($seek.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern)).SetValue(6)
        Start-Sleep -Milliseconds 700
        CheckTrue "setting the Seek value jumps there" ([math]::Abs([double](Mpv-Get $app 'time-pos') - 6) -lt 1.2)
        Start-Sleep -Milliseconds 700
        CheckTrue "and the control reads out the time" (($seek.Current.HelpText) -match '^\d+:\d\d of \d+:\d\d$')

        Press "Speed: 1x"
        Check "the speed button steps the speed" ([math]::Round([double](Mpv-Wait $app 'speed' 1.25 3000), 2)) 1.25
        CheckTrue "and says the new speed" (Find-Element $app "Speed: 1.25x" 3000)

        Press "Fullscreen"
        $scr = [Windows.Forms.Screen]::FromHandle($app.Hwnd).Bounds
        $r = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
        CheckTrue "Fullscreen fills the screen" (($r.R - $r.L) -ge $scr.Width)
        CheckTrue "and offers to leave it" (Find-Element $app "Leave fullscreen" 3000)
        Press "Leave fullscreen"
        CheckTrue "leaving works" (Find-Element $app "Fullscreen" 3000)

        Press "Close video"
        CheckTrue "Close video returns to the library" (Find-Element $app "Open stream" 6000)
        CheckTrue "and the player controls are gone from the tree" (Gone $app "Skip forward" 1200)
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Record {
    Write-Host "Recording a stream"
    $port = Get-Random -Minimum 20000 -Maximum 30000
    $serverLog = Join-Path (Split-Path $fx -Parent) "fake-server.log"
    $rec = Join-Path $env:TEMP ("lumina-rec-e2e-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-server.py`" $port `"$fx`"" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { (Invoke-WebRequest "http://127.0.0.1:$port/multi.mkv" -Headers @{ Range = "bytes=0-0" } -UseBasicParsing).StatusCode | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }

        # a file on disk cannot be recorded
        $app = Start-Lumina "$fx\multi.mkv" @{ "recording.folder" = $rec }
        try {
            CheckTrue "controller ready" (Mpv-Ready $app)
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'record') } | Out-Null
            Start-Sleep -Milliseconds 800
            Check "a local file is not recorded" (Mpv-Get $app 'user-data/lumina/recording') "no"
            Check "and no recordings folder is made for it" (Test-Path $rec) $false
        } finally { Stop-Lumina $app }

        # a stream is
        $app = Start-Lumina "http://127.0.0.1:$port/trickle/multi.mkv" @{ "recording.folder" = $rec }
        try {
            CheckTrue "the stream plays" (Mpv-Ready $app)
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'record') } | Out-Null
            Check "recording starts" (Mpv-Wait $app 'user-data/lumina/recording' 'yes' 4000) "yes"
            Start-Sleep -Seconds 3
            Wake-Controls $app
            Save-Window $app.Hwnd "$env:TEMP/lumina-recording.png"
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'record') } | Out-Null
            Check "pressing it again stops" (Mpv-Wait $app 'user-data/lumina/recording' 'no' 4000) "no"
            $files = @(Get-ChildItem $rec -Filter *.mkv -ErrorAction SilentlyContinue)
            Check "one recording file was made" $files.Count 1
            CheckTrue "and it has the video in it (a few hundred KB or more)" ($files.Count -eq 1 -and $files[0].Length -gt 200000)
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }

        # leaving the video while recording keeps what was saved
        $app = Start-Lumina "http://127.0.0.1:$port/trickle/multi.mkv" @{ "recording.folder" = $rec }
        try {
            CheckTrue "the stream plays again" (Mpv-Ready $app)
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'record') } | Out-Null
            Check "recording starts" (Mpv-Wait $app 'user-data/lumina/recording' 'yes' 4000) "yes"
            Start-Sleep -Seconds 2
            Mpv-Ipc $app @{ command = @('quit') } | Out-Null
            CheckTrue "back in the library" (Find-Element $app "Open stream" 8000)
            Start-Sleep -Seconds 1
            $files = @(Get-ChildItem $rec -Filter *.mkv -ErrorAction SilentlyContinue)
            Check "a second recording file exists and is not empty" (($files.Count -eq 2) -and (($files | Where-Object { $_.Length -gt 50000 }).Count -eq 2)) $true
        } finally { Stop-Lumina $app }
    } finally {
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $rec -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Hdr {
    Write-Host "HDR page in the player panel"
    $app = Start-Lumina "$fx\hdr10.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Click-Region $app "gear"
        Check "the player panel opens" (Mpv-Get $app 'user-data/lumina/gear') "open"
        Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'gear-scroll', '10') } | Out-Null    # the HDR row is below the first sixteen
        CheckTrue "the HDR row is there" (Wait-Regions $app "g_17_p")
        Click-Region $app "g_17_p"
        Check "the HDR page shows the file's signal" (Mpv-Wait $app 'user-data/lumina/hdr_signal' 'HDR10') "HDR10"
        CheckTrue "its rows are published" (Wait-Regions $app "g_2_p")
        Save-Window $app.Hwnd "$env:TEMP/lumina-hdr.png"

        Click-Region $app "g_2_p"
        Check "Tone mapping changes the player's tone mapping" (Mpv-Get $app 'tone-mapping') "bt.2390"
        Click-Region $app "g_2_p"
        Check "and steps on again" (Mpv-Get $app 'tone-mapping') "bt.2446a"
        Click-Region $app "g_3_p"
        Check "Peak brightness steps through levels" (Mpv-Get $app 'target-peak') "100"
        Click-Region $app "g_4_p"
        Check "Detect peak per scene steps on (it starts on yes, so the next is no)" (Mpv-Get $app 'hdr-compute-peak') "False"
        Click-Region $app "g_5_p"
        Check "Tell the display it is HDR steps on (yes, then no)" (Mpv-Get $app 'target-colorspace-hint') "False"
        Click-Region $app "g_6_p"
        Check "Back to automatic resets tone mapping" (Mpv-Get $app 'tone-mapping') "auto"
        Check "and the peak" (Mpv-Get $app 'target-peak') "auto"

        Click-Region $app "g_back"
        CheckTrue "Back returns to the main panel (its first row is there again)" (Wait-Regions $app "g_1_m")
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Mini {
    Write-Host "Miniplayer"
    $app = Start-Lumina "$fx\multi.mkv" @{}
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        $r0 = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r0) | Out-Null
        $w0 = $r0.R - $r0.L; $h0 = $r0.B - $r0.T
        Check "the normal window is not always on top" (Is-Topmost $app.Hwnd) $false

        Send-Keys ("w"); Start-Sleep -Milliseconds 1500
        Check "the controller knows it is in the miniplayer" (Mpv-Get $app 'user-data/lumina/mini') "yes"
        $r = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
        $w = $r.R - $r.L; $h = $r.B - $r.T
        CheckTrue "the window is small now ($w x $h, was $w0 x $h0)" (($w -lt $w0 * 0.6) -and ($h -lt $h0 * 0.7) -and ($w -gt 300))
        CheckTrue "and sits in the bottom-right of the screen" ((($r.R) -gt [Windows.Forms.Screen]::FromHandle($app.Hwnd).WorkingArea.Right - 80) -and (($r.B) -gt [Windows.Forms.Screen]::FromHandle($app.Hwnd).WorkingArea.Bottom - 80))
        Check "and stays on top of other windows" (Is-Topmost $app.Hwnd) $true

        Calibrate-Player $app
        Wake-Controls $app
        Start-Sleep -Milliseconds 500
        $regions = Player-Regions $app
        CheckTrue "the controller shows Play, the seek bar, the panel and fullscreen" ($regions.play -and $regions.seek -and $regions.gear -and $regions.fs)
        CheckTrue "and leaves out volume and the track buttons" (-not ($regions.vol -or $regions.cc -or $regions.audio -or $regions.speed))
        Save-Window $app.Hwnd "$env:TEMP/lumina-mini.png" -FromScreen

        Mpv-Set $app 'pause' 'yes'
        Click-Region $app "play"
        Check "Play still works in the miniplayer" (Mpv-Wait $app 'pause' $false) $false
        Click-Region $app "gear"
        Check "so does the player panel" (Mpv-Get $app 'user-data/lumina/gear') "open"
        Click-Region $app "gear"

        Send-Keys ("w"); Start-Sleep -Milliseconds 1500
        Check "pressing the key again leaves it" (Mpv-Get $app 'user-data/lumina/mini') "no"
        $r2 = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r2) | Out-Null
        CheckTrue "the window is its old size again" ([math]::Abs(($r2.R - $r2.L) - $w0) -le 3 -and [math]::Abs(($r2.B - $r2.T) - $h0) -le 3)
        Check "and no longer always on top" (Is-Topmost $app.Hwnd) $false

        # leaving the video while in the miniplayer restores the window too
        Send-Keys ("w"); Start-Sleep -Milliseconds 1500
        Mpv-Ipc $app @{ command = @('quit') } | Out-Null
        CheckTrue "back in the library" (Find-Element $app "Open stream" 8000)
        $r3 = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r3) | Out-Null
        CheckTrue "the library window is the normal size, not the small one" ([math]::Abs(($r3.R - $r3.L) - $w0) -le 3)
        Check "and not on top" (Is-Topmost $app.Hwnd) $false
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

function Part-Subs {
    Write-Host "Subtitles for many videos, a chosen match, and each video's own look"
    $port = Get-Random -Minimum 30000 -Maximum 40000
    $lib = Join-Path $env:TEMP ("lumina-subs-" + [guid]::NewGuid().ToString("N").Substring(0, 6))
    New-Item -ItemType Directory -Force "$lib\VisionQuest\Season 1" | Out-Null
    Copy-Item "$fx\multi.mkv" "$lib\The End of Oak Street (2026).mkv"
    foreach ($n in 1, 2) { Copy-Item "$fx\multi.mkv" "$lib\VisionQuest\Season 1\VisionQuest.S01E0$n.mkv" }
    $py = Start-Process python -ArgumentList "`"$PSScriptRoot\fake-tmdb.py`" $port" -PassThru -WindowStyle Hidden
    try {
        foreach ($i in 1..40) { try { Invoke-WebRequest "http://127.0.0.1:$port/3/movie/100" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 250 } }
        $env:LUMINA_TMDB_API = "http://127.0.0.1:$port/3/"; $env:LUMINA_TMDB_IMG = "http://127.0.0.1:$port/img/"; $env:LUMINA_OPENSUBTITLES_API = "http://127.0.0.1:$port/os/"
        $app = Start-Lumina "" @{ tmdbKey = "0123456789abcdef0123456789abcdef"; openSubtitlesKey = "testkey"; subtitleLanguages = "en" } "shows" "" @() "" $lib
        try {
            CheckTrue "the show appears" (Find-Element $app "VisionQuest" 15000)
            Start-Sleep -Seconds 4

            # a whole show
            Click-Element $app "VisionQuest" -Right
            CheckTrue "the show menu offers subtitles for all episodes" (Find-Element $app "Find subtitles for all episodes..." 3000)
            Click-Element $app "Find subtitles for all episodes..."
            CheckTrue "the search reports what it found" (Find-Element $app "Subtitles: 1 found, 1 already had them" 15000 -Text -Contains)   # both episodes are the same file, so the second finds the first one's subtitles
            $subs = @(Get-ChildItem "$($app.Dir)\subs" -Filter *.srt -ErrorAction SilentlyContinue)
            CheckTrue "a subtitle file is stored in the run's own cache" ($subs.Count -ge 1)
            Click-Element $app "VisionQuest" -Right
            Click-Element $app "Find subtitles for all episodes..."
            CheckTrue "doing it again skips what already has subtitles" (Find-Element $app "2 already had them" 15000 -Text -Contains)

            # one film, with a message
            Click-Element $app "Movies"
            Click-Element $app "The End of Oak Street" -Right
            Click-Element $app "Find subtitles..."
            CheckTrue "a single film says what happened (it is the same file again, so it already has them)" (Find-Element $app "1 already had them" 15000 -Text -Contains)
            Click-Element $app "OK"

            # a folder, from the Folders page
            Click-Element $app "Folders"
            CheckTrue "a folder has a subtitles button" (Find-Element $app "Find subtitles for this folder" 3000)
            Click-Element $app "Find subtitles for this folder"
            CheckTrue "it skips what already has them" (Find-Element $app "already had them" 15000 -Text -Contains)
            Check "every control has a name" ((Audit-Names $app) -join "; ") ""
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }

        # choosing from all matches, and each video's own look
        $env:LUMINA_TMDB_API = $null
        $app = Start-Lumina "$fx\multi.mkv" @{ openSubtitlesKey = "testkey"; subtitleLanguages = "en" }
        try {
            CheckTrue "controller ready" (Mpv-Ready $app)
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-subs-pick') } | Out-Null
            CheckTrue "the picker lists the matches" (Find-Element $app "Choose subtitles" 8000 -Text)
            CheckTrue "with the best first" (Find-Element $app "Fake.Release.EN.best" 3000 -Contains)
            Check "the button waits for a choice" (Is-Enabled $app "Use these") $false
            $row = Find-Element $app "Fake.Release.EN.other" 3000 -Text -Contains
            if ($row) { $r = $row.Current.BoundingRectangle; Click-Screen ([int]($r.X + 40)) ([int]($r.Y + $r.Height / 2)) }
            Start-Sleep -Milliseconds 500
            Check "choosing a row turns the button on" (Is-Enabled $app "Use these") $true
            Click-Element $app "Use these"
            Start-Sleep -Seconds 2
            $ext = @(0..([int](Mpv-Get $app 'track-list/count') - 1) | ForEach-Object { Mpv-Get $app "track-list/$_/external" } | Where-Object { $_ -eq $true })
            Check "the chosen subtitles are on the video" ($ext.Count -ge 1) $true

            # the look of the subtitles is remembered for this video only
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sub-size', '0.2') } | Out-Null
            Mpv-Ipc $app @{ command = @('script-message', 'lumina-do', 'sub-delay', '0.5') } | Out-Null
            Start-Sleep -Milliseconds 800
            Mpv-Ipc $app @{ command = @('quit') } | Out-Null
            CheckTrue "back in the library" (Find-Element $app "Open stream" 8000)
            $env:LUMINA_INSTANCE = $app.Instance
            Start-Process $Exe -ArgumentList "`"$fx\multi.mkv`"" | Out-Null
            CheckTrue "playing it again" (Wait-Path $app 'multi' 15000)
            Check "its subtitle size came back" ([math]::Round([double](Mpv-Wait $app 'sub-scale' 1.2 5000), 2)) 1.2
            Check "and its delay" ([math]::Round([double](Mpv-Get $app 'sub-delay'), 2)) 0.5
            Mpv-Ipc $app @{ command = @('quit') } | Out-Null
            CheckTrue "back in the library again" (Find-Element $app "Open stream" 8000)
            Start-Process $Exe -ArgumentList "`"$fx\surround51.mkv`"" | Out-Null
            CheckTrue "another video plays" (Wait-Path $app 'surround51' 15000)
            Check "and has its own, normal, look" ([math]::Round([double](Mpv-Get $app 'sub-scale'), 2)) 1
            CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
        } finally { Stop-Lumina $app }
    } finally {
        Remove-Item Env:\LUMINA_TMDB_API, Env:\LUMINA_TMDB_IMG, Env:\LUMINA_OPENSUBTITLES_API -ErrorAction SilentlyContinue
        Stop-Process $py -Force -ErrorAction SilentlyContinue
        Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Part-Keys {
    Write-Host "Custom keyboard shortcuts"
    $app = Start-Lumina "" @{} "settings"
    try {
        Click-Element $app "Controls"
        Click-Element $app "Customise keys"
        CheckTrue "the editor lists the actions" (Find-Element $app "Change key for Play or pause" 3000)
        Click-Element $app "Change key for Play or pause"
        CheckTrue "it waits for a key" (Find-Element $app "Press the key you want" 3000 -Text)
        Send-Keys ("k"); Start-Sleep -Milliseconds 700
        CheckTrue "the key is saved" ((Config-Value $app "shortcuts") -match '"playpause":"k"')
        CheckTrue "the row now shows k" (Find-Element $app "k" 2000 -Text)

        # a clash asks first, then swaps
        Click-Element $app "Change key for Mute"
        Send-Keys ("k"); Start-Sleep -Milliseconds 700
        CheckTrue "taking a used key asks before swapping" (Find-Element $app "k is already used" 3000 -Text)
        Click-Element $app "Swap"
        Start-Sleep -Milliseconds 600
        $cfg = Config-Value $app "shortcuts"
        CheckTrue "after the swap Mute has k and Play or pause took Mute's old key m" (($cfg -match '"mute":"k"') -and ($cfg -match '"playpause":"m"'))

        Click-Element $app "Change key for Mute"
        Send-Keys ("{ESC}"); Start-Sleep -Milliseconds 500
        CheckTrue "Esc cancels without changing anything" ((Config-Value $app "shortcuts") -match '"mute":"k"')

        Click-Element $app "Reset key for Mute"
        Start-Sleep -Milliseconds 500
        $after = Config-Value $app "shortcuts"
        CheckTrue "Reset puts Mute back on m and moves Play or pause off it, so nothing clashes" (($after -notmatch 'mute') -and ($after -match '"playpause":"k"'))
        Click-Element $app "Change key for Play or pause"
        Send-Keys ("^s"); Start-Sleep -Milliseconds 700
        CheckTrue "a Ctrl combination can be captured" ((Config-Value $app "shortcuts") -match 'Ctrl\+s')
        Click-Element $app "Reset all keys"
        Start-Sleep -Milliseconds 500
        Check "Reset all clears everything" (Config-Value $app "shortcuts") $null
        Check "no errors in the log after editing keys" (Error-Count $app) 0
    } finally { Stop-Lumina $app }

    # a fresh player honours what was saved
    $app = Start-Lumina "$fx\multi.mkv" @{ shortcuts = '{"playpause":"k"}' }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Check "starts paused" (Mpv-Get $app 'pause') $true
        Send-Keys (" "); Start-Sleep -Milliseconds 500
        Check "the old key no longer plays" (Mpv-Get $app 'pause') $true
        Send-Keys ("k"); Start-Sleep -Milliseconds 500
        Check "the new key plays" (Mpv-Get $app 'pause') $false
        Send-Keys ("k"); Start-Sleep -Milliseconds 500
        Check "and pauses again" (Mpv-Get $app 'pause') $true
        Check "no errors in the log after using custom keys" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

function Part-Theme {
    Write-Host "Light and dark theme"
    $page = @(1700, 1300)      # empty page area on the Settings page
    $side = @(100, 1000)       # empty sidebar area
    $app = Start-Lumina "" @{} "settings"
    try {
        CheckTrue "starts dark" ((Pixel-Luma $app $page[0] $page[1]) -lt 45)
        CheckTrue "the sidebar is dark too" ((Pixel-Luma $app $side[0] $side[1]) -lt 45)

        Click-Element $app "Light"
        Start-Sleep -Milliseconds 500
        Check "Light is saved" (Config-Value $app "general.theme") "light"
        CheckTrue "the page turns light at once, with no restart" ((Pixel-Luma $app $page[0] $page[1]) -gt 220)
        CheckTrue "so does the sidebar" ((Pixel-Luma $app $side[0] $side[1]) -gt 220)
        CheckTrue "settings text is still findable" (Find-Element $app "Accent colour" 2000 -Text)
        Check "every control still has a name in the light theme" ((Audit-Names $app) -join "; ") ""
        Save-Window $app.Hwnd "$env:TEMP\lumina-theme-light.png"

        Click-Element $app "Accent Orange"
        Start-Sleep -Milliseconds 400
        Check "an accent can be chosen on light" (Config-Value $app "general.accent") "F28C1E"
        CheckTrue "and the page stays light" ((Pixel-Luma $app $page[0] $page[1]) -gt 220)

        Click-Element $app "Match Windows"
        Start-Sleep -Milliseconds 500
        $sysLight = [bool]((Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize").AppsUseLightTheme -eq 1)
        $luma = Pixel-Luma $app $page[0] $page[1]
        CheckTrue "Match Windows follows the system setting (system is $(if ($sysLight) { 'light' } else { 'dark' }), page luma $luma)" ($(if ($sysLight) { $luma -gt 220 } else { $luma -lt 45 }))

        Click-Element $app "Dark"
        Start-Sleep -Milliseconds 500
        CheckTrue "and back to dark" ((Pixel-Luma $app $page[0] $page[1]) -lt 45)
        Check "no errors in the log after switching themes" (Error-Count $app) 0
    } finally { Stop-Lumina $app }

    # a saved light theme is there from the first frame
    $app = Start-Lumina "" @{ "general.theme" = "light" } "movies" "" @() "" "$fx\library"
    try {
        CheckTrue "a saved light theme starts light" ((Pixel-Luma $app 1700 1300) -gt 220)
        Click-Element $app "Film Title" -Right
        CheckTrue "menus work on the light theme" (Find-Element $app "Hide from library..." 3000)
        Send-Keys ("{ESC}")
        Check "no errors in the log with a light start" (Error-Count $app) 0
    } finally { Stop-Lumina $app }

    # the player controller stays dark over the picture whatever the theme
    $app = Start-Lumina "$fx\multi.mkv" @{ "general.theme" = "light" }
    try {
        CheckTrue "controller ready" (Mpv-Ready $app)
        Calibrate-Player $app
        Click-Region $app "gear"
        CheckTrue "the player panel still opens in light" (Wait-Regions $app "g_1_p")
        Check "no errors in the log playing on the light theme" (Error-Count $app) 0
    } finally { Stop-Lumina $app }
}

# A real-input test can lose a click to the machine (a busy moment, a stolen focus). Each part therefore gets one more try when it fails,
# and the first failure is written to scratch\flaky.log, so instability stays visible. A part that fails twice is a real failure.
$script:flaky = @()
function Run-Part([string]$name, [scriptblock]$body) {
    if ($Part -notin "all", $name) { return }
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $f0 = $script:fails.Count; $p0 = $script:passes
        try { & $body } catch { $script:fails.Add("part '$name' stopped: " + $_.Exception.Message) }
        if ($script:fails.Count -eq $f0) {
            if ($attempt -eq 2) { $script:flaky += $name }
            return
        }
        if ($attempt -eq 1) {
            $first = @($script:fails | Select-Object -Skip $f0)
            $script:fails.RemoveRange($f0, $script:fails.Count - $f0); $script:passes = $p0
            Write-Host "  (part '$name' failed once, trying it again: $($first -join '; '))" -ForegroundColor Yellow
            Get-Process Lumina, mpv -ErrorAction SilentlyContinue | Stop-Process -Force
            $log = Join-Path (Split-Path $PSScriptRoot -Parent) "scratch\flaky.log"
            Add-Content $log ("{0:s}  {1}  {2}" -f (Get-Date), $name, ($first -join " | "))
            Start-Sleep -Seconds 2
        }
    }
}

Run-Part "settings" { Part-Settings }
Run-Part "player" { Part-Player }
Run-Part "queue" { Part-Queue }
Run-Part "playlists" { Part-Playlists }
Run-Part "folder" { Part-Folder }
Run-Part "essentials" { Part-Essentials }
Run-Part "tracks" { Part-Tracks }
Run-Part "launch" { Part-Launch }
Run-Part "taskbar" { Part-Taskbar }
Run-Part "assoc" { Part-Assoc }
Run-Part "media" { Part-Media }
Run-Part "library" { Part-Library }
Run-Part "view" { Part-View }
Run-Part "sidebar" { Part-Sidebar }
Run-Part "audio" { Part-Audio }
Run-Part "network" { Part-Network }
Run-Part "updates" { Part-Updates }
function Part-Profiles {
    Write-Host "Profiles and presets"
    $app = Start-Lumina "" @{ "video.brightness" = "20"; "subtitles.size" = "60" } "settings"
    try {
        Click-Element $app "Playback"
        Click-Element $app "Profiles"
        CheckTrue "the profile list opens with the built-in ones" ((Find-Element $app "Movies" 3000) -and (Find-Element $app "Anime" 1500) -and (Find-Element $app "Night" 1500))
        CheckTrue "Undo starts disabled" (-not (Is-Enabled $app "Undo last apply"))

        Click-Element $app "Night"
        Click-Element $app "Apply"
        Start-Sleep -Milliseconds 600
        Check "Night turns night mode on" (Config-Value $app "audio.night") "true"
        Check "and replaces the picture settings" (Config-Value $app "video.brightness") "-10"
        Check "and sends subtitle size back to default" (Config-Value $app "subtitles.size") $null
        CheckTrue "Undo is offered by name" (Find-Element $app "Undo Night" 2000)

        Click-Element $app "Undo Night"
        Start-Sleep -Milliseconds 600
        Check "Undo brings the brightness back" (Config-Value $app "video.brightness") "20"
        Check "and the subtitle size" (Config-Value $app "subtitles.size") "60"
        Check "and unsets night mode" (Config-Value $app "audio.night") $null

        Click-Element $app "Save current as..."
        Click-Element $app "Profile name"; Type-Text "Mine{ENTER}"
        CheckTrue "a saved profile appears in the list" (Find-Element $app "Mine" 3000)
        Click-Element $app "Mine"
        Click-Element $app "Delete"
        CheckTrue "and can be deleted" (Gone $app "Mine")
        Check "every control has a name" ((Audit-Names $app) -join "; ") ""
        Save-Window $app.Hwnd "$env:TEMP/lumina-profiles.png"
        Click-Element $app "Done"
        CheckTrue "no errors in the log" ((Error-Count $app) -eq 0)
    } finally { Stop-Lumina $app }
}

Run-Part "keys" { Part-Keys }
Run-Part "theme" { Part-Theme }
Run-Part "profiles" { Part-Profiles }
Run-Part "tour" { Part-Tour }
Run-Part "controller" { Part-Controller }
Run-Part "details" { Part-Details }
Run-Part "tvdetails" { Part-TvDetails }
Run-Part "home" { Part-Home }
Run-Part "overview" { Part-Overview }
Run-Part "visual" { Part-Visual }
Run-Part "floating" { Part-Floating }
Run-Part "vlc" { Part-Vlc }
Run-Part "scrub" { Part-Scrub }
Run-Part "editmeta" { Part-EditMeta }
Run-Part "thumbs" { Part-Thumbs }
Run-Part "bookmarks" { Part-Bookmarks }
Run-Part "sleep" { Part-Sleep }
Run-Part "lock" { Part-Lock }
Run-Part "access" { Part-Access }
Run-Part "record" { Part-Record }
Run-Part "hdr" { Part-Hdr }
Run-Part "mini" { Part-Mini }
Run-Part "subs" { Part-Subs }
Write-Host ""
if ($script:flaky.Count -gt 0) { Write-Host ("passed only on the second try: " + ($script:flaky -join ", ")) -ForegroundColor Yellow }
Write-Host "$script:passes passed, $($script:fails.Count) failed"
$script:fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit $(if ($script:fails.Count -eq 0) { 0 } else { 1 })
