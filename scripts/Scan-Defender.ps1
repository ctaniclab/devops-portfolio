param([Parameter(Mandatory=$true)][string]$ScanPath, [Parameter(Mandatory=$true)][string]$ReportPath)
$ErrorActionPreference = 'Stop'
$stage = 'Initialize'
trap {
    $failure = "${stage}: $($_.Exception.Message)"
    if (-not (Test-Path $ReportPath)) {
        @{ Scanner = 'Microsoft Defender Antivirus'; Clean = $false; Stage = $stage; Error = $failure } |
            ConvertTo-Json | Set-Content -Encoding utf8 $ReportPath
    }
    Write-Output "::error title=Microsoft Defender verification failed::$failure"
    throw
}
$root = (Resolve-Path $ScanPath).Path
$files = @(Get-ChildItem $root -File -Recurse)
if ($files.Count -eq 0) { throw 'No files to scan.' }
$hashes = @{}
foreach ($file in $files) { $hashes[$file.FullName] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash }
$stage = 'Start Defender service'
# Hosted Windows images may have the service stopped. Querying the Defender
# provider before starting it can itself fail with an RPC error.
$service = Get-Service WinDefend -ErrorAction Stop
if ($service.StartType -eq 'Disabled') { Set-Service WinDefend -StartupType Automatic -ErrorAction Stop }
if ($service.Status -ne 'Running') { Start-Service WinDefend -ErrorAction Stop }
$service.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
$stage = 'Read Defender status'
$status = Get-MpComputerStatus -ErrorAction Stop
if (-not $status.AMServiceEnabled) { throw 'Microsoft Defender service is unavailable; publication is blocked.' }
$stage = 'Update Defender definitions'
Update-MpSignature -ErrorAction Stop
$status = Get-MpComputerStatus -ErrorAction Stop
if (-not $status.AntivirusSignatureVersion -or $status.AntivirusSignatureAge -gt 2) {
    throw 'Current Microsoft Defender definitions are unavailable; publication is blocked.'
}
$stage = 'Read existing detections'
$before = @(Get-MpThreatDetection -ErrorAction Stop | ForEach-Object { $_.DetectionID })
$candidate = Get-ChildItem "$env:ProgramData/Microsoft/Windows Defender/Platform/*/MpCmdRun.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
$scanner = if ($candidate) { $candidate.FullName } else { "$env:ProgramFiles/Windows Defender/MpCmdRun.exe" }
if (-not (Test-Path $scanner)) { throw 'Defender scan executable was not found.' }
$started = [DateTime]::UtcNow
$stage = 'Scan published files'
& $scanner -Scan -ScanType 3 -File $root 2>&1 | Tee-Object -FilePath "$ReportPath.txt"
$scanExit = $LASTEXITCODE
$stage = 'Verify scan results'
$newDetections = @(Get-MpThreatDetection -ErrorAction Stop | Where-Object { $_.DetectionID -notin $before })
$unchanged = $true
foreach ($file in $files) {
    if (-not (Test-Path $file.FullName) -or (Get-FileHash $file.FullName -Algorithm SHA256).Hash -ne $hashes[$file.FullName]) { $unchanged = $false }
}
$clean = $scanExit -eq 0 -and $newDetections.Count -eq 0 -and $unchanged
@{
    Scanner = 'Microsoft Defender Antivirus'; StartedUtc = $started.ToString('O'); FinishedUtc = [DateTime]::UtcNow.ToString('O')
    SignatureVersion = $status.AntivirusSignatureVersion; ExitCode = $scanExit
    NewDetections = @($newDetections | Select-Object DetectionID, ThreatID, Resources)
    FilesUnchanged = $unchanged; Clean = $clean; FileSHA256 = $hashes
} | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $ReportPath
if (-not $clean) {
    $names = @(Get-MpThreat -ErrorAction SilentlyContinue | Where-Object { $_.ThreatID -in $newDetections.ThreatID } | ForEach-Object { $_.ThreatName }) -join ', '
    throw "Defender scan did not establish a clean result (exit=$scanExit, new detections=$($newDetections.Count), files unchanged=$unchanged, threats=$names). No build will be published."
}
Write-Host 'Microsoft Defender scan completed, no new detections, all scanned file hashes unchanged.'
