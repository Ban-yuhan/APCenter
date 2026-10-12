$exe = "D:\Project\Portfolio\APCenter\Builds\APCenter_Game.exe"
Write-Host "Launching $exe..."
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
if (-not $p.HasExited) {
    Write-Host "STATUS: GAME RUNS SUCCESSFULLY! Process ID: $($p.Id)"
    Stop-Process -Id $p.Id -Force
    Write-Host "Test finished and process safely closed."
} else {
    Write-Host "STATUS: Game exited early with ExitCode $($p.ExitCode)"
}
