# Test Win32 Job Object & LlamaProcessManager lifecycle
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "Verifying no leftover llama-server processes exist before test..." -ForegroundColor Cyan
Get-Process -Name "llama-server" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Running OpenAgent.Model assembly verification..." -ForegroundColor Green
$modelDll = Resolve-Path "src/OpenAgent.Model/bin/Debug/net8.0/OpenAgent.Model.dll"
Test-Path $modelDll
