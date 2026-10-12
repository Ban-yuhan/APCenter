$unityExe = "C:\Program Files\UnityEditor\6000.0.62f1\Editor\Unity.exe"
$projectDir = "D:\Project\Portfolio\APCenter"
$logFile = "D:\Project\Portfolio\APCenter\unity_init.log"

Write-Host "Starting Unity to create project..."
$proc = Start-Process -FilePath $unityExe -ArgumentList "-batchmode", "-createProject", $projectDir, "-quit", "-logFile", $logFile -PassThru
$proc.WaitForExit()
Write-Host "Unity finished with code: $($proc.ExitCode)"
if (Test-Path $logFile) {
    Get-Content $logFile -Tail 30
}
