# Generates small, deterministic media fixtures for tests into scratch\fixtures. Safe to re-run; skips what exists.
# Needs ffmpeg (default C:\ffmpeg\bin\ffmpeg.exe, or set LUMINA_FFMPEG).
param([switch]$Force)
$ErrorActionPreference = "Stop"
$ff = if ($env:LUMINA_FFMPEG) { $env:LUMINA_FFMPEG } else { "C:\ffmpeg\bin\ffmpeg.exe" }
if (-not (Test-Path $ff)) { throw "ffmpeg not found at $ff" }
$root = Join-Path (Split-Path $PSScriptRoot -Parent) "scratch\fixtures"
New-Item -ItemType Directory -Force $root, "$root\library\Movies", "$root\library\TV\Show Name" | Out-Null

function Make($path, [string[]]$ffArgs) {
    if ((Test-Path $path) -and -not $Force) { return }
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    & $ff -hide_banner -loglevel error -y @ffArgs $path
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $path" }
    "made $path"
}

# Two subtitle files and a chapter list used by multi.mkv
Set-Content "$root\eng.srt" "1`n00:00:00,500 --> 00:00:02,500`nHello from English`n`n2`n00:00:03,000 --> 00:00:05,000`nSecond English line`n" -Encoding utf8
Set-Content "$root\spa.srt" "1`n00:00:00,500 --> 00:00:02,500`nHola en Espanol`n`n2`n00:00:03,000 --> 00:00:05,000`nSegunda linea`n" -Encoding utf8
Set-Content "$root\chapters.txt" ";FFMETADATA1`n[CHAPTER]`nTIMEBASE=1/1000`nSTART=0`nEND=4000`ntitle=Opening`n[CHAPTER]`nTIMEBASE=1/1000`nSTART=4000`nEND=8000`ntitle=Middle`n[CHAPTER]`nTIMEBASE=1/1000`nSTART=8000`nEND=12000`ntitle=Ending`n" -Encoding ascii

$v = "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=24:duration=12"
# 12 s, 2 audio tracks (eng 440 Hz, jpn 880 Hz), 2 subtitle tracks (eng, spa), 3 chapters
Make "$root\multi.mkv" ($v + @("-f", "lavfi", "-i", "sine=frequency=440:duration=12", "-f", "lavfi", "-i", "sine=frequency=880:duration=12",
    "-i", "$root\eng.srt", "-i", "$root\spa.srt", "-i", "$root\chapters.txt",
    "-map", "0:v", "-map", "1:a", "-map", "2:a", "-map", "3:s", "-map", "4:s", "-map_metadata", "5",
    "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=jpn", "-metadata:s:s:0", "language=eng", "-metadata:s:s:1", "language=spa",
    "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-c:s", "srt"))

# 5.1 audio
Make "$root\surround51.mkv" ($v + @("-f", "lavfi", "-i", "sine=frequency=330:duration=12", "-af", "pan=5.1|FL=c0|FR=c0|FC=c0|LFE=c0|BL=c0|BR=c0",
    "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "ac3"))

# Interlaced broadcast-style transport stream (the stutter case)
Make "$root\interlaced.ts" ("-f", "lavfi", "-i", "testsrc2=size=720x576:rate=25:duration=8", "-f", "lavfi", "-i", "sine=frequency=500:duration=8",
    "-vf", "interlace=scan=tff", "-c:v", "mpeg2video", "-flags", "+ilme+ildct", "-top", "1", "-b:v", "4M", "-c:a", "mp2", "-f", "mpegts")

# HDR10-tagged HEVC (tags only; enough to exercise the tone-mapping path)
Make "$root\hdr10.mkv" ("-f", "lavfi", "-i", "testsrc2=size=640x360:rate=24:duration=6", "-pix_fmt", "yuv420p10le",
    "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:log-level=error", "-an")

# A tiny library: one movie, three episodes of one show (names exercise the parser)
foreach ($ep in 1..3) {
    Make "$root\library\TV\Show Name\Show.Name.S01E0$ep.1080p.mkv" ("-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24:duration=4",
        "-f", "lavfi", "-i", "sine=frequency=$(300 + 100 * $ep):duration=4", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac")
}
foreach ($m in @(@("Alpha Movie (1999)", 440), @("Zulu Movie (2021)", 520))) {
    Make "$root\library\Movies\$($m[0]).mkv" ("-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24:duration=3",
        "-f", "lavfi", "-i", "sine=frequency=$($m[1]):duration=3", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac")
}
Make "$root\library\Movies\Film Title (2020).mkv" ("-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24:duration=6",
    "-f", "lavfi", "-i", "sine=frequency=600:duration=6", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac")

# A file that is not a video at all, for the "could not play" message
$broken = Join-Path $root "broken.mkv"
if (-not (Test-Path $broken)) { [IO.File]::WriteAllBytes($broken, (1..4096 | ForEach-Object { [byte](($_ * 37) % 251) })) }

"fixtures ready in $root"
