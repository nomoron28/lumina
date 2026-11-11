# Helpers for the real-input end-to-end tour: launch Lumina on scratch data, find controls with UI Automation,
# click them with SendInput, and read player state back over mpv's IPC pipe.
. "$PSScriptRoot\input.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$script:fails = New-Object System.Collections.Generic.List[string]
$script:passes = 0
$Exe = Join-Path (Split-Path $PSScriptRoot -Parent) "src\Lumina\bin\Debug\net10.0-windows\Lumina.exe"

function Check($name, $actual, $expected) {
    if ("$actual" -eq "$expected") { $script:passes++; Write-Host "  ok   $name" }
    else { $script:fails.Add("$name : expected '$expected', got '$actual'"); Write-Host "  FAIL $name : expected '$expected', got '$actual'" -ForegroundColor Red }
}
function CheckTrue($name, $cond) { Check $name ([bool]$cond) $true }

function Start-Lumina([string[]]$File, [hashtable]$Config = @{}, [string]$Page = "", [string]$Instance = "", [string[]]$Options = @(), [string]$RegBranch = "", [string]$SeedRoot = "", [string]$CredPrefix = "") {
    $dir = Join-Path $env:TEMP ("lumina-e2e-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
    New-Item -ItemType Directory -Force "$dir\snaps" | Out-Null
    $pipe = "lumina-e2e-" + [guid]::NewGuid().ToString("N").Substring(0, 6)
    $cfg = @{ mpvOptions = "input-ipc-server=\\.\pipe\$pipe`npause=yes"; "snapshots.folder" = "$dir\snaps" }
    $cfg["general.tour"] = "false"; $cfg["interface.seekThumbs"] = "false"; $cfg["interface.controllerStyle"] = "classic"; $cfg["updates.onStart"] = "false"   # tests turn the extras (and the network) off unless a part is about them
    foreach ($k in $Config.Keys) { $cfg[$k] = $Config[$k] }
    ($cfg | ConvertTo-Json) | Set-Content "$dir\config.json" -Encoding utf8
    $env:LUMINA_THUMBS = "$dir\thumbs"; $env:LUMINA_SUBS = "$dir\subs"       # downloaded subtitles stay in the run's own folder, never the real cache
    $env:LUMINA_DB = "$dir\lumina.db"; $env:LUMINA_ART = "$dir\art"; $env:LUMINA_CONFIG = "$dir\config.json"; $env:LUMINA_LOG = "$dir\logs"
    if ($RegBranch) { $env:LUMINA_REG_BRANCH = $RegBranch } else { Remove-Item Env:\LUMINA_REG_BRANCH -ErrorAction SilentlyContinue }
    if ($CredPrefix) { $env:LUMINA_CRED_PREFIX = $CredPrefix } else { Remove-Item Env:\LUMINA_CRED_PREFIX -ErrorAction SilentlyContinue }
    if ($SeedRoot) { $env:LUMINA_SEED_ROOT = $SeedRoot } else { Remove-Item Env:\LUMINA_SEED_ROOT -ErrorAction SilentlyContinue }
    if ($Page) { $env:LUMINA_PAGE = $Page } else { Remove-Item Env:\LUMINA_PAGE -ErrorAction SilentlyContinue }
    # every launch gets its own instance name unless the test asks to share one, so tours never meet a real Lumina or each other
    $env:LUMINA_INSTANCE = if ($Instance) { $Instance } else { "e2e-" + [guid]::NewGuid().ToString("N").Substring(0, 8) }
    $argText = (($Options + ($File | ForEach-Object { "`"$_`"" })) | Where-Object { $_ }) -join " "
    $p = if ($argText) { Start-Process $Exe -ArgumentList $argText -PassThru } else { Start-Process $Exe -PassThru }
    $h = [IntPtr]::Zero
    foreach ($i in 1..60) { Start-Sleep -Milliseconds 250; $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break } }
    if ($h -eq [IntPtr]::Zero) { throw "Lumina window never appeared" }
    Start-Sleep -Milliseconds 800
    Set-InputTarget $p $h
    if (-not (Focus-Window $h)) { Write-Host "  (warning: could not take foreground)" }
    [pscustomobject]@{ Proc = $p; Hwnd = $h; Instance = $env:LUMINA_INSTANCE; Dir = $dir; Pipe = $pipe; Config = "$dir\config.json"; Log = "$dir\logs" }
}

function Stop-Lumina($app) {
    if ($app.Proc -and -not $app.Proc.HasExited) { Stop-Process $app.Proc -Force }
    Start-Sleep -Milliseconds 300
    Remove-Item $app.Dir -Recurse -Force -ErrorAction SilentlyContinue
}

function Error-Count($app) {
    $f = Get-ChildItem $app.Log -Filter *.log -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $f) { return 0 }
    return @(Get-Content $f.FullName | Where-Object { $_ -match '^\S+ ERROR ' }).Count
}

function Config-Value($app, $key) {
    $j = Get-Content $app.Config -Raw | ConvertFrom-Json
    return $j.$key
}

# ---- UI Automation ----
function Find-Element($app, [string]$name, [int]$timeoutMs = 3000, [switch]$Text, [switch]$Contains) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty), $name
    # Row labels share their control's name ("Size", "Bold"); skip plain text unless asked for.
    if (-not $Text) {
        $notText = New-Object Windows.Automation.NotCondition (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty), ([Windows.Automation.ControlType]::Text))
        $cond = New-Object Windows.Automation.AndCondition $cond, $notText
    }
    $until = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    do {
        # Every top-level window of the app: the main window and any dialog it opened.
        $pidCond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ProcessIdProperty), $app.Proc.Id
        foreach ($w in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $pidCond)) {
            if ($Contains) {   # a part of the name is enough (text that is built from several pieces)
                foreach ($c in $w.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) {
                    if ($c.Current.Name -like "*$name*") { return $c }
                }
                continue
            }
            $e = $w.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
            if ($e) { return $e }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $until)
    return $null
}

function Click-Element($app, [string]$name, [switch]$Right) {
    $e = Find-Element $app $name
    if (-not $e) { throw "control '$name' not found" }
    # A control below the fold is scrolled into view first, as a user would with the wheel.
    $p = $null
    if ($e.TryGetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern, [ref]$p)) { try { $p.ScrollIntoView(); Start-Sleep -Milliseconds 250 } catch { } }
    # UI Automation reports the position the control would have if the page were taller, so judge by the window, not the screen.
    $wr = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$wr) | Out-Null
    foreach ($n in 1..8) {
        $b = $e.Current.BoundingRectangle
        if ($b.Y + $b.Height -le $wr.B - 12) { break }
        Wheel-At $app.Hwnd 1400 900 -3; Start-Sleep -Milliseconds 800
    }
    $r = $e.Current.BoundingRectangle
    if ($Right) { Click-Screen ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2)) -Right } else { Click-Screen ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2)) }
}

function Click-Screen([int]$x, [int]$y, [switch]$Right) {
    Move-Mouse $x $y
    $d, $u = if ($Right) { 0x08, 0x10 } else { 0x02, 0x04 }
    # the pointer must still be on the target at the moment of the press, or the click lands somewhere else
    $q = New-Object In+POINT; [In]::GetCursorPos([ref]$q) | Out-Null
    if ([math]::Abs($q.X - $x) -gt 5 -or [math]::Abs($q.Y - $y) -gt 5) { Move-Mouse $x $y }
    Press-Mouse $d; Start-Sleep -Milliseconds 40; Press-Mouse $u; Start-Sleep -Milliseconds 450
}

function Type-Text($text) { Send-Keys $text; Start-Sleep -Milliseconds 300 }

# ---- mpv IPC ----
function Mpv-Ipc($app, $cmd) {
    $c = New-Object IO.Pipes.NamedPipeClientStream '.', $app.Pipe, 'InOut'
    $c.Connect(4000)
    try {
        $w = New-Object IO.StreamWriter $c; $w.AutoFlush = $true; $r = New-Object IO.StreamReader $c
        $w.WriteLine(($cmd | ConvertTo-Json -Compress))
        while ($true) { $line = $r.ReadLine(); if ($null -eq $line) { return $null }; $o = $line | ConvertFrom-Json; if ($o.PSObject.Properties.Name -contains 'error') { return $o } }
    } finally { $c.Dispose() }
}
function Mpv-Get($app, [string]$prop) { $o = Mpv-Ipc $app @{ command = @('get_property', $prop) }; if ($o.error -eq 'success') { $o.data } else { $null } }
function Mpv-Set($app, [string]$prop, $value) { Mpv-Ipc $app @{ command = @('set_property', $prop, $value) } | Out-Null }
function Mpv-Wait($app, [string]$prop, $expected, [int]$timeoutMs = 4000) {
    $until = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    do { $v = Mpv-Get $app $prop; if ("$v" -eq "$expected") { return $v }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $until)
    return $v
}
function Mpv-Ready($app) {
    $until = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $until) {
        try { if ((Mpv-Get $app 'user-data/lumina/ready') -eq 'yes') { return $true } } catch { }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

# Controller regions are in the player window's pixels; measure where that window sits by moving the real mouse
# to a known screen point and asking mpv where it thinks the mouse is.
function Calibrate-Player($app) {
    $r = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null
    $px = $r.L + [int](($r.R - $r.L) / 2); $py = $r.T + [int](($r.B - $r.T) / 2)
    Move-Mouse $px $py; Start-Sleep -Milliseconds 400; Move-Mouse ($px + 3) ($py + 3); Start-Sleep -Milliseconds 400
    $m = Mpv-Get $app 'mouse-pos'
    if (-not $m) { throw "mpv reports no mouse position" }
    $script:offX = ($px + 3) - $m.x; $script:offY = ($py + 3) - $m.y
}
function Player-Regions($app) { $j = Mpv-Get $app 'user-data/lumina/regions'; if ($j) { $j | ConvertFrom-Json } }
function Wake-Controls($app) { $r = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$r) | Out-Null; Move-Mouse ($r.L + 300) ($r.T + 400); Start-Sleep -Milliseconds 250; Move-Mouse ($r.L + 320) ($r.T + 410); Start-Sleep -Milliseconds 300 }
function Click-Region($app, [string]$name, [double]$fracX = 0.5) {
    Wake-Controls $app
    # the controller publishes a region a frame after drawing it, so a busy machine can be a moment late: wait for it
    $reg = $null
    foreach ($try in 1..20) { $reg = (Player-Regions $app).$name; if ($reg) { break }; Start-Sleep -Milliseconds 150 }
    if (-not $reg) { throw "region '$name' not published" }
    Click-Screen ([int]($script:offX + $reg.x + $reg.w * $fracX)) ([int]($script:offY + $reg.y + $reg.h / 2))
}

# Every interactive control must have a name a screen reader can say. Returns the unnamed ones.
function Audit-Names($app) {
    $types = @([Windows.Automation.ControlType]::Button, [Windows.Automation.ControlType]::CheckBox, [Windows.Automation.ControlType]::ComboBox,
               [Windows.Automation.ControlType]::Edit, [Windows.Automation.ControlType]::Slider, [Windows.Automation.ControlType]::RadioButton,
               [Windows.Automation.ControlType]::ListItem)
    $or = New-Object Windows.Automation.OrCondition ([Windows.Automation.Condition[]]($types | ForEach-Object { New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty), $_ }))
    $pidCond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ProcessIdProperty), $app.Proc.Id
    $bad = @()
    foreach ($w in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $pidCond)) {
        foreach ($e in $w.FindAll([Windows.Automation.TreeScope]::Descendants, $or)) {
            $c = $e.Current
            if ($c.IsOffscreen) { continue }
            if ($c.AutomationId -match '^(Decrease|Increase)(Large|Small)$') { continue }   # the scroll bars own arrow buttons, not Lumina controls
            if ([string]::IsNullOrWhiteSpace($c.Name)) { $bad += "$($c.ControlType.ProgrammaticName) at $($c.BoundingRectangle) id='$($c.AutomationId)'" }
        }
    }
    return $bad
}

# The player restarts for each queue item, so wait for the new instance to report the file we expect.
function Wait-Path($app, [string]$pattern, [int]$timeoutMs = 9000) {
    $until = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    do { try { $p = Mpv-Get $app 'path'; if ("$p" -match $pattern) { Start-Sleep -Milliseconds 600; return $p } } catch { } ; Start-Sleep -Milliseconds 250 } while ([DateTime]::UtcNow -lt $until)
    return $null
}
function Wait-Regions($app, [string]$name, [int]$timeoutMs = 4000) {
    $until = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    do { try { if ((Player-Regions $app).$name) { return $true } } catch { } ; Start-Sleep -Milliseconds 200 } while ([DateTime]::UtcNow -lt $until)
    return $false
}
# Seek first, then unpause: at the end of a file mpv re-pauses, so the other order leaves it paused.
function Seek-Near-End($app) {
    $d = [double](Mpv-Get $app 'duration')
    Mpv-Ipc $app @{ command = @('seek', ($d - 0.7), 'absolute+exact') } | Out-Null
    Start-Sleep -Milliseconds 300
    Mpv-Set $app 'pause' $false
}

function Is-Enabled($app, [string]$name) { $e = Find-Element $app $name 1500; if ($e) { return $e.Current.IsEnabled } return $null }
function Gone($app, [string]$name, [int]$ms = 900) { return ($null -eq (Find-Element $app $name $ms)) }

# Brightness (0 to 255) of one pixel of the window, for tests that need to know what colour something really is.
function Pixel-Luma($app, [int]$x, [int]$y) {
    $tmp = Join-Path $env:TEMP ("pix-" + [guid]::NewGuid().ToString("N") + ".png")
    Save-Window $app.Hwnd $tmp
    $bmp = [System.Drawing.Bitmap]::FromFile($tmp)
    try { $c = $bmp.GetPixel($x, $y); return [int](0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B) }
    finally { $bmp.Dispose(); Remove-Item $tmp -ErrorAction SilentlyContinue }
}

# Resize the main window through UI Automation (the window keeps its own minimum size, so asking for less gives the minimum).
function Resize-Main($app, [int]$w, [int]$h) {
    $win = [Windows.Automation.AutomationElement]::FromHandle($app.Hwnd)
    $t = $win.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern)
    $t.Resize($w, $h)
    Start-Sleep -Milliseconds 900
}

# True when the element is on screen and wholly inside the main window (nothing is cut off at the edge).
function Inside-Window($app, [string]$name, [switch]$Text, [int]$timeoutMs = 3000) {
    $e = Find-Element $app $name $timeoutMs -Text:$Text
    if (-not $e) { return $false }
    $r = $e.Current.BoundingRectangle
    $wr = New-Object In+RECT; [In]::GetWindowRect($app.Hwnd, [ref]$wr) | Out-Null
    return ($r.Left -ge $wr.L - 1) -and ($r.Right -le $wr.R + 1) -and ($r.Top -ge $wr.T - 1) -and ($r.Width -gt 0)
}

Add-Type -Namespace Win -Name Ex -MemberDefinition '[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);'
# True when the window has the "always on top" style
function Is-Topmost($hwnd) { return (([Win.Ex]::GetWindowLong($hwnd, -20) -band 0x8) -ne 0) }
