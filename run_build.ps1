$unityExe = "C:\Program Files\UnityEditor\6000.0.62f1\Editor\Unity.exe"
$projectDir = "D:\Project\Portfolio\APCenter"
$logFile = "D:\Project\Portfolio\APCenter\build.log"

if (Test-Path $logFile) {
    Remove-Item $logFile -Force
}

Write-Host "Running Unity batch build..."
$proc = Start-Process -FilePath $unityExe -ArgumentList "-batchmode", "-projectPath", $projectDir, "-executeMethod", "APCenter.Editor.GameSceneBuilder.PerformBuild", "-quit", "-logFile", $logFile -PassThru
$proc.WaitForExit()

Write-Host "Unity build exited with code: $($proc.ExitCode)"

if (Test-Path $logFile) {
    Write-Host "=== BUILD LOG TAIL ==="
    Get-Content $logFile -Tail 40
}
