# Quick runner shortcut for OpenAgent test suite
param(
    [switch]$SkipLiveServer = $false,
    [string]$Sprint = "all"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$runAll = Join-Path $scriptDir "run_all_tests.ps1"

& pwsh -File "$runAll" -SkipLiveServer:$SkipLiveServer -Sprint $Sprint
exit $LASTEXITCODE
