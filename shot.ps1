param([string]$name = "s", [string]$extra = "")
# Launches the built game windowed with auto-screenshot, then prints the stats line and any errors from the player log.
$b = "F:\Unity\Projects\ShaChang\Build"; $shot = "F:\Unity\Projects\ShaChang\shots\$name.png"
New-Item -ItemType Directory -Force "F:\Unity\Projects\ShaChang\shots" | Out-Null
Remove-Item $shot, "$shot.txt" -ErrorAction SilentlyContinue
$p = Start-Process "$b\ShaChang.exe" -ArgumentList "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -shot $shot $extra" -PassThru
if (-not $p.WaitForExit(150000)) { $p.Kill(); "killed (timeout)" }
Get-Content "$shot.txt" -ErrorAction SilentlyContinue
$log = "$env:USERPROFILE\AppData\LocalLow\ShaChang\沙场\Player.log"
Get-Content $log -ErrorAction SilentlyContinue | Select-String -Pattern "Exception|error|missing" | Select-Object -Unique -First 12 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(220, $_.Line.Length)) }
