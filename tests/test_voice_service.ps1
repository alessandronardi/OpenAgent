# Test Suite for OpenAgent Voice Feature (Whisper locale + VoiceInputService)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "         OPENAGENT VOICE & WHISPER VALIDATION           " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

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

# 1. Model existence check
$modelFile = Resolve-Path "models/ggml-tiny.bin" -ErrorAction SilentlyContinue
Assert-Condition ($modelFile -ne $null -and (Test-Path $modelFile)) "Whisper model file 'models/ggml-tiny.bin' exists on disk"

if ($modelFile) {
    $size = (Get-Item $modelFile).Length
    Assert-Condition ($size -gt 70000000) "Whisper model file size is valid ($([Math]::Round($size / 1MB, 1)) MB)"
}

# 2. Verify assembly dependencies
$appDll = Resolve-Path "src/OpenAgent.App/bin/Debug/net8.0-windows/OpenAgent.dll"
Assert-Condition (Test-Path $appDll) "OpenAgent.App assembly exists"

$whisperDll = Resolve-Path "src/OpenAgent.App/bin/Debug/net8.0-windows/Whisper.net.dll"
Assert-Condition (Test-Path $whisperDll) "Whisper.net assembly exists"

$naudioDll = Resolve-Path "src/OpenAgent.App/bin/Debug/net8.0-windows/NAudio.dll"
Assert-Condition (Test-Path $naudioDll) "NAudio assembly exists"

# 3. Test Whisper Factory loading
try {
    Add-Type -Path $whisperDll
    $factory = [Whisper.net.WhisperFactory]::FromPath($modelFile.Path)
    Assert-Condition ($factory -ne $null) "WhisperFactory successfully initialized from model"
    $factory.Dispose()
} catch {
    Assert-Condition $false "Failed to initialize WhisperFactory: $($_.Exception.Message)"
}

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "TEST RESULTS: Passed: $passCount, Failed: $failCount" -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
exit 0
