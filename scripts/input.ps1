# Real mouse and keyboard input for UI tests, dot-sourced by shot.ps1 and gate.ps1.
# Uses SendInput (what a physical mouse produces) and confirms the cursor really moved before clicking.
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class In {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] i, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, flags, time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Explicit)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }
  public static void Mouse(uint flags, uint data = 0) {
    var i = new INPUT[1]; i[0].type = 0; i[0].mi.flags = flags; i[0].mi.mouseData = data;
    SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
  }
}
"@
[In]::SetProcessDPIAware() | Out-Null

function Focus-Window($h) {
  [In]::ShowWindow($h, 9) | Out-Null          # restore if minimised
  # Windows refuses SetForegroundWindow from a background process; a synthetic Alt press lifts the restriction.
  [In]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [In]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [In]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 300
  return ([In]::GetForegroundWindow() -eq $h)
}

# Real input goes to whatever window is in front. If that is not the program under test (the user clicked somewhere, a notification
# took focus) typing would land in another program, even a chat box. So every keystroke and click first checks that the window in
# front belongs to the program being tested, tries once to bring it forward, and otherwise refuses.
$script:GuardPid = 0          # the process under test; 0 means no guard (scripts that do not start one)
$script:GuardWindow = [IntPtr]::Zero

function Set-InputTarget($proc, $hwnd) { $script:GuardPid = if ($proc) { $proc.Id } else { 0 }; $script:GuardWindow = $hwnd }

function Test-TargetInFront {
  if ($script:GuardPid -eq 0) { return $true }
  $front = [In]::GetForegroundWindow()
  $owner = [uint32]0; [In]::GetWindowThreadProcessId($front, [ref]$owner) | Out-Null
  return ($owner -eq $script:GuardPid)
}

function Assert-TargetInFront {
  if (Test-TargetInFront) { return }
  if ($script:GuardWindow -ne [IntPtr]::Zero) { Focus-Window $script:GuardWindow | Out-Null }
  if (-not (Test-TargetInFront)) { throw "The program under test is not the window in front, so no keys or clicks are sent (they could land in another program)." }
}

function Press-Mouse([uint32]$flags, [uint32]$data = 0) { Assert-TargetInFront; [In]::Mouse($flags, $data) }

function Send-Keys([string]$keys) { Assert-TargetInFront; [Windows.Forms.SendKeys]::SendWait($keys) }

function Move-Mouse([int]$x, [int]$y) {
  # Step toward the target so hover handlers and mpv's mouse_move see real motion.
  # Anything else that moves the pointer meanwhile (a touchpad, a hand on the mouse) is corrected a few times before giving up;
  # five pixels is close enough, because every target is a control far bigger than that.
  for ($attempt = 1; $attempt -le 6; $attempt++) {
    $p = New-Object In+POINT; [In]::GetCursorPos([ref]$p) | Out-Null
    foreach ($k in 1..6) { [In]::SetCursorPos([int]($p.X + ($x - $p.X) * $k / 6), [int]($p.Y + ($y - $p.Y) * $k / 6)) | Out-Null; Start-Sleep -Milliseconds 15 }
    Start-Sleep -Milliseconds 30
    $q = New-Object In+POINT; [In]::GetCursorPos([ref]$q) | Out-Null
    if ([math]::Abs($q.X - $x) -le 5 -and [math]::Abs($q.Y - $y) -le 5) { return }
  }
  throw "cursor did not reach ($x,$y), is at ($($q.X),$($q.Y))"
}

# Click at a point relative to the window's top-left, in physical pixels.
function Click-At($h, [int]$dx, [int]$dy, [switch]$Right, [switch]$Double) {
  $r = New-Object In+RECT; [In]::GetWindowRect($h, [ref]$r) | Out-Null
  Move-Mouse ($r.L + $dx) ($r.T + $dy)
  $down, $up = if ($Right) { 0x08, 0x10 } else { 0x02, 0x04 }
  foreach ($n in 1..$(if ($Double) { 2 } else { 1 })) { Press-Mouse $down; Start-Sleep -Milliseconds 40; Press-Mouse $up; Start-Sleep -Milliseconds 60 }
  Start-Sleep -Milliseconds 400
}

function Drag-To($h, [int]$x1, [int]$y1, [int]$x2, [int]$y2) {
  $r = New-Object In+RECT; [In]::GetWindowRect($h, [ref]$r) | Out-Null
  Move-Mouse ($r.L + $x1) ($r.T + $y1); Press-Mouse 0x02; Start-Sleep -Milliseconds 80
  Move-Mouse ($r.L + $x2) ($r.T + $y2); Start-Sleep -Milliseconds 80; Press-Mouse 0x04; Start-Sleep -Milliseconds 300
}

function Wheel-At($h, [int]$dx, [int]$dy, [int]$notches) {
  $r = New-Object In+RECT; [In]::GetWindowRect($h, [ref]$r) | Out-Null
  Move-Mouse ($r.L + $dx) ($r.T + $dy)
  Press-Mouse 0x0800 ([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]($notches * 120)), 0)); Start-Sleep -Milliseconds 300
}

function Save-Window($h, [string]$path, [switch]$FromScreen) {
  $r = New-Object In+RECT; [In]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $hh; $g = [System.Drawing.Graphics]::FromImage($bmp)
  # A screen copy shows whatever is in that rectangle, so only take one while this window is in front and fully on screen;
  # otherwise capture the window's own contents.
  $front = ([In]::GetForegroundWindow() -eq $h)
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $onScreen = ($r.L -ge $vs.Left -and $r.T -ge $vs.Top -and $r.R -le $vs.Right + 20 -and $r.B -le $vs.Bottom + 20)
  if ($FromScreen -and $front -and $onScreen) { $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size) } else { $dc = $g.GetHdc(); [In]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc) }
  $bmp.Save($path); $g.Dispose(); $bmp.Dispose()
}
