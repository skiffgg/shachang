param([string]$method = "Builder.Build")
# Opens the project headless (compiles scripts), runs the given Builder method, prints compile errors / result.
$u = "F:\Unity\Editors\6000.3.22f1\Editor\Unity.exe"; $log = "F:\Unity\Projects\ShaChang\Logs\build.log"; $t = Get-Date
& $u -batchmode -nographics -quit -projectPath "F:\Unity\Projects\ShaChang" -executeMethod $method -logFile $log | Out-Null
"exit $LASTEXITCODE in $([int]((Get-Date)-$t).TotalSeconds)s"
Select-String -Path $log -Pattern "error CS|\[Builder\]|Scripts have compiler errors|Error building" | ForEach-Object { $_.Line.Substring(0, [Math]::Min(230, $_.Line.Length)) } | Select-Object -Unique -First 30
