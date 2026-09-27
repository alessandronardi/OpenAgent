# OpenAgent Unified Test Suite Runner (PowerShell 7+)
param(
    [switch]$SkipLiveServer = $false,
    [string]$Sprint = "all"
)

$ErrorActionPreference = "Stop"
$rootDir = (Get-Item .).FullName
$testsDir = Join-Path $rootDir "tests"
$tempDir = Join-Path $testsDir "temp"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "             OPENAGENT TEST SUITE RUNNER                " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

# Ensure tests/temp directory exists and is clean
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$suites = @()

if ($Sprint -eq "all" -or $Sprint -eq "process") {
    $suites += @{ Name = "Process Manager Lifecycle"; Script = "test_process_manager.ps1"; Args = @() }
}
if ($Sprint -eq "all" -or $Sprint -eq "2") {
    $s2Args = if ($SkipLiveServer) { @("-SkipLiveServer") } else { @() }
    $suites += @{ Name = "Sprint 2: Tools & Sandbox"; Script = "test_sprint2.ps1"; Args = $s2Args }
}
if ($Sprint -eq "all" -or $Sprint -eq "3") {
    $suites += @{ Name = "Sprint 3: Audit & Web Safety"; Script = "test_sprint3.ps1"; Args = @() }
}
if ($Sprint -eq "all" -or $Sprint -eq "4") {
    $s4Args = if ($SkipLiveServer) { @("-SkipLiveServer") } else { @() }
    $suites += @{ Name = "Sprint 4: Multi-Step & Loops"; Script = "test_sprint4.ps1"; Args = $s4Args }
}
if ($Sprint -eq "all" -or $Sprint -eq "5") {
    $suites += @{ Name = "Sprint 5: Memory & Settings"; Script = "test_sprint5.ps1"; Args = @() }
}
if ($Sprint -eq "all" -or $Sprint -eq "memory") {
    $suites += @{ Name = "Memory System & Identity Persistence"; Script = "test_memory_system.ps1"; Args = @() }
}
if ($Sprint -eq "all" -or $Sprint -eq "hardware" -or $Sprint -eq "hw") {
    $suites += @{ Name = "Hardware Profiler & Auto-Tune"; Script = "test_hardware_profiler.ps1"; Args = @() }
}
if ($Sprint -eq "all" -or $Sprint -eq "cloud" -or $Sprint -eq "gemini") {
    $suites += @{ Name = "Gemini Cloud Integration"; Script = "test_gemini_cloud_integration.ps1"; Args = @() }
}
if ($Sprint -eq "all" -or $Sprint -eq "voice" -or $Sprint -eq "whisper") {
    $suites += @{ Name = "Voice & Local Whisper STT"; Script = "test_voice_service.ps1"; Args = @() }
}

$results = @()
$totalStart = Get-Date

foreach ($suite in $suites) {
    Write-Host "`n>>> Running: $($suite.Name) ($($suite.Script))" -ForegroundColor Yellow
    $scriptPath = Join-Path $testsDir $suite.Script
    $suiteStart = Get-Date
    
    & pwsh -File "$scriptPath" @($suite.Args)
    $exitCode = $LASTEXITCODE
    $elapsed = (Get-Date) - $suiteStart
    
    $success = ($exitCode -eq 0)
    $results += [PSCustomObject]@{
        Suite    = $suite.Name
        Script   = $suite.Script
        ExitCode = $exitCode
        Passed   = $success
        Duration = "$([math]::Round($elapsed.TotalSeconds, 1))s"
    }
}

# Cleanup tests/temp directory if empty or clean up residues
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Print Summary Table
$totalDuration = "$([math]::Round(((Get-Date) - $totalStart).TotalSeconds, 1))s"
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "                TEST SUITE RUN REPORT                   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Total Execution Time: $totalDuration`n" -ForegroundColor Gray

$allPassed = $true
foreach ($res in $results) {
    $statusColor = if ($res.Passed) { "Green" } else { "Red" }
    $statusText = if ($res.Passed) { "[PASS]" } else { "[FAIL]" }
    Write-Host ("  {0,-8} {1,-35} ({2})" -f $statusText, $res.Suite, $res.Duration) -ForegroundColor $statusColor
    if (-not $res.Passed) { $allPassed = $false }
}

Write-Host "========================================================" -ForegroundColor Cyan
if ($allPassed) {
    Write-Host " ALL TEST SUITES PASSED SUCCESSFULLY! " -ForegroundColor Green
    Write-Host "========================================================`n" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host " SOME TEST SUITES FAILED. PLEASE CHECK LOGS ABOVE. " -ForegroundColor Red
    Write-Host "========================================================`n" -ForegroundColor Cyan
    exit 1
}
