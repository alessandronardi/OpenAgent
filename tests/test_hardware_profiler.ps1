# Test Suite for OpenAgent Hardware Detection & Automatic Profiling Engine

param(
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "    OPENAGENT HARDWARE DETECTION & PROFILING TEST       " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

# Load assemblies
$dataBin = (Resolve-Path "src/OpenAgent.Data/bin/Debug/net8.0").Path
$runtimesPath = Join-Path $dataBin "runtimes\win-x64\native"
$env:Path = "$runtimesPath;$dataBin;$env:Path"

Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.core.dll")
Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.provider.e_sqlite3.dll")
Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.batteries_v2.dll")
Add-Type -Path (Join-Path $dataBin "Microsoft.Data.Sqlite.dll")

[SQLitePCL.Batteries_V2]::Init()

$dataDll = Resolve-Path "src/OpenAgent.Data/bin/Debug/net8.0/OpenAgent.Data.dll"
Add-Type -Path $dataDll

$workspaceRoot = (Get-Item .).FullName
$passCount = 0
$failCount = 0

function Assert-Condition($condition, [string]$testName) {
    if ($condition) {
        Write-Host "  [PASS] $testName" -ForegroundColor Green
        $script:passCount++
    } else {
        Write-Host "  [FAIL] $testName" -ForegroundColor Red
        $script:failCount++
    }
}

# ------------------------------------------------------------------------------------------------
# TEST GROUP 1: Live Hardware Specs Detection
# ------------------------------------------------------------------------------------------------
Write-Host "--- TEST GROUP 1: Live Hardware Specs Detection (HardwareDetector) ---" -ForegroundColor Yellow

$specs = [OpenAgent.Data.Hardware.HardwareDetector]::DetectSpecs($workspaceRoot)

Assert-Condition (![string]::IsNullOrWhiteSpace($specs.CpuName)) "Detected CPU Name: '$($specs.CpuName)'"
Assert-Condition ($specs.PhysicalCores -gt 0) "Detected Physical Cores count > 0 ($($specs.PhysicalCores))"
Assert-Condition ($specs.LogicalCores -ge $specs.PhysicalCores) "Logical Cores ($($specs.LogicalCores)) >= Physical Cores ($($specs.PhysicalCores))"
Assert-Condition ($specs.TotalRamGb -gt 1.0) "Total RAM is detected ($($specs.TotalRamGb) GB)"
Assert-Condition ($specs.AvailableRamGb -gt 0.5) "Available RAM is detected ($($specs.AvailableRamGb) GB)"
Assert-Condition (![string]::IsNullOrWhiteSpace($specs.Summary)) "Hardware summary generated: '$($specs.Summary)'"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 2: Heuristic Profiling Matrix
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 2: Heuristic Profiling Matrix (HardwareProfiler) ---" -ForegroundColor Yellow

$baseSettings = New-Object OpenAgent.Data.AppSettings

# Scenario 2.1: Entry-level machine (2 cores, 4 GB RAM, 1.8 GB free, no Vulkan)
$entrySpecs = New-Object OpenAgent.Data.Hardware.HardwareSpecs -Property @{
    CpuName = "Intel Celeron N4020"
    PhysicalCores = 2
    LogicalCores = 2
    TotalPhysicalMemoryBytes = [ulong](4L * 1024 * 1024 * 1024)
    AvailablePhysicalMemoryBytes = [ulong]([long](1.8 * 1024 * 1024 * 1024))
    HasVulkanSupport = $false
    Tier = [OpenAgent.Data.Hardware.HardwareTier]::Entry
}
$entryOpt = [OpenAgent.Data.Hardware.HardwareProfiler]::OptimizeSettings($baseSettings, $entrySpecs, $workspaceRoot)
Assert-Condition ($entryOpt.OptimizedSettings.CpuThreads -eq 2) "Entry tier: CPU threads = 2"
Assert-Condition ($entryOpt.OptimizedSettings.ContextSize -eq 2048) "Entry tier: Context size = 2048 tokens (safeguarding RAM)"
Assert-Condition ($entryOpt.OptimizedSettings.Device -eq "none") "Entry tier: Device = none (CPU inference)"
Assert-Condition ($entryOpt.OptimizedSettings.ExecutionTimeoutSeconds -eq 240) "Entry tier: Timeout = 240s (generous for slower CPU)"

# Scenario 2.2: Balanced machine with Vulkan (4 cores, 8 GB RAM, 3.8 GB free, Vulkan support)
$balancedSpecs = New-Object OpenAgent.Data.Hardware.HardwareSpecs -Property @{
    CpuName = "AMD Ryzen 5 3500U"
    PhysicalCores = 4
    LogicalCores = 8
    TotalPhysicalMemoryBytes = [ulong](8L * 1024 * 1024 * 1024)
    AvailablePhysicalMemoryBytes = [ulong]([long](3.8 * 1024 * 1024 * 1024))
    HasVulkanSupport = $true
    GpuName = "AMD Radeon Vega 8"
    Tier = [OpenAgent.Data.Hardware.HardwareTier]::Balanced
}
$balancedOpt = [OpenAgent.Data.Hardware.HardwareProfiler]::OptimizeSettings($baseSettings, $balancedSpecs, $workspaceRoot)
Assert-Condition ($balancedOpt.OptimizedSettings.CpuThreads -eq 4) "Balanced tier (4 cores): CPU threads = 4 (matches physical cores)"
Assert-Condition ($balancedOpt.OptimizedSettings.ContextSize -eq 4096) "Balanced tier: Context size = 4096 tokens"
Assert-Condition ($balancedOpt.OptimizedSettings.Device -eq "Vulkan0") "Balanced tier: Device = Vulkan0"
Assert-Condition ($balancedOpt.OptimizedSettings.ExecutionTimeoutSeconds -eq 180) "Balanced tier: Timeout = 180s"

# Scenario 2.3: High-End Workstation (16 cores, 32 GB RAM, 22 GB free, Vulkan support)
$highEndSpecs = New-Object OpenAgent.Data.Hardware.HardwareSpecs -Property @{
    CpuName = "AMD Ryzen 9 7950X"
    PhysicalCores = 16
    LogicalCores = 32
    TotalPhysicalMemoryBytes = [ulong](32L * 1024 * 1024 * 1024)
    AvailablePhysicalMemoryBytes = [ulong](22L * 1024 * 1024 * 1024)
    HasVulkanSupport = $true
    GpuName = "NVIDIA GeForce RTX 4080"
    Tier = [OpenAgent.Data.Hardware.HardwareTier]::HighPerformance
}
$highEndOpt = [OpenAgent.Data.Hardware.HardwareProfiler]::OptimizeSettings($baseSettings, $highEndSpecs, $workspaceRoot)
Assert-Condition ($highEndOpt.OptimizedSettings.CpuThreads -eq 12) "High-End tier: CPU threads = 12 (high throughput without memory bus thrashing)"
Assert-Condition ($highEndOpt.OptimizedSettings.ContextSize -eq 8192) "High-End tier: Context size extended to 8192 tokens"
Assert-Condition ($highEndOpt.OptimizedSettings.Device -eq "Vulkan0") "High-End tier: Device = Vulkan0"
Assert-Condition ($highEndOpt.OptimizedSettings.ExecutionTimeoutSeconds -eq 120) "High-End tier: Timeout = 120s"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 3: Settings Persistence with Auto-Tune Flags
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 3: Settings Persistence (SqliteUserSettingsRepository) ---" -ForegroundColor Yellow

$tempTestDir = Join-Path $workspaceRoot "tests\temp\temp_test_hardware_profiler"
if (Test-Path $tempTestDir) {
    Remove-Item -Path $tempTestDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $tempTestDir -Force | Out-Null
$testDbPath = Join-Path $tempTestDir "test_hw_settings.db"

$repo = New-Object OpenAgent.Data.SqliteUserSettingsRepository($testDbPath)
$repo.InitializeAsync().GetAwaiter().GetResult() | Out-Null

$settings = $repo.GetSettingsAsync().GetAwaiter().GetResult()
Assert-Condition ($settings.AutoTuneOnStartup -eq $true) "Default AutoTuneOnStartup is true"
Assert-Condition ($settings.HasCompletedInitialAutoTune -eq $false) "Default HasCompletedInitialAutoTune is false"

# Modify flags and persist
$settings.AutoTuneOnStartup = $false
$settings.HasCompletedInitialAutoTune = $true
$settings.LastDetectedHardware = "Test Hardware Profile (8C/16T, 16GB)"
$repo.SaveSettingsAsync($settings).GetAwaiter().GetResult() | Out-Null

# Reload from fresh repository instance
$repoReloaded = New-Object OpenAgent.Data.SqliteUserSettingsRepository($testDbPath)
$reloaded = $repoReloaded.GetSettingsAsync().GetAwaiter().GetResult()
Assert-Condition ($reloaded.AutoTuneOnStartup -eq $false) "Persisted AutoTuneOnStartup reloaded accurately (false)"
Assert-Condition ($reloaded.HasCompletedInitialAutoTune -eq $true) "Persisted HasCompletedInitialAutoTune reloaded accurately (true)"
Assert-Condition ($reloaded.LastDetectedHardware -eq "Test Hardware Profile (8C/16T, 16GB)") "Persisted LastDetectedHardware reloaded accurately"

# Clean up temp
Remove-Item -Path $tempTestDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "             HARDWARE PROFILER TEST RESULTS             " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  Passed: $passCount" -ForegroundColor Green
Write-Host "  Failed: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
