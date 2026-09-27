# Spike Test for OpenAgent: Hardware benchmark & Function calling validation

param (
    [string]$ModelPath = "models/Spark-X2.5-1.7B-Q4_K_M.gguf",
    [int]$Port = 8080,
    [int]$ContextSize = 4096,
    [int]$GpuLayers = 99
)

$ErrorActionPreference = "Stop"

Write-Host "=== 1. Hardware Benchmark via llama-bench (CPU -t 6) ===" -ForegroundColor Cyan
$benchCmd = ".\runtime\llama\llama-bench.exe -m `"$ModelPath`" --device none -t 6 -n 64 -p 128"
Write-Host "Executing: $benchCmd"
Invoke-Expression $benchCmd

Write-Host "`n=== 2. Starting llama-server (CPU Fallback, -c $ContextSize, -t 6) ===" -ForegroundColor Cyan
$serverExe = Resolve-Path "runtime/llama/llama-server.exe"
$modelAbs = Resolve-Path $ModelPath

$tempDir = Join-Path (Get-Item .).FullName "tests\temp"
if (-not (Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir -Force | Out-Null }
$spikeLog = Join-Path $tempDir "server_spike.log"

$serverProcess = Start-Process -FilePath $serverExe `
    -ArgumentList "-m `"$modelAbs`" --port $Port -c $ContextSize --device none -t 6 --jinja --log-file `"$spikeLog`"" `
    -PassThru `
    -WindowStyle Hidden

Write-Host "llama-server PID: $($serverProcess.Id). Waiting for readiness..."

$ready = $false
$retries = 35
while (-not $ready -and $retries -gt 0) {
    Start-Sleep -Seconds 1
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($health.status -eq "ok" -or $health.status -eq "ready" -or $health.status -eq "loading model") {
            if ($health.status -eq "ok" -or $health.status -eq "ready") {
                $ready = $true
            } else {
                Write-Host "Server status: $($health.status)..."
            }
        }
    } catch {
        # Waiting for port to open
    }
    $retries--
}

if (-not $ready) {
    Write-Error "llama-server failed to become ready within timeout. Check server_spike.log."
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "Server is ready! Checking memory footprint..." -ForegroundColor Green
$procInfo = Get-Process -Id $serverProcess.Id
$ramMB = [math]::Round($procInfo.WorkingSet64 / 1MB, 2)
Write-Host "llama-server WorkingSet RAM: $ramMB MB" -ForegroundColor Yellow

Write-Host "`n=== 3. Testing Function Calling via OpenAI compatible /v1/chat/completions ===" -ForegroundColor Cyan

$modelName = [System.IO.Path]::GetFileNameWithoutExtension($ModelPath)

$payload = @{
    model = $modelName
    messages = @(
        @{
            role = "system"
            content = "Sei un assistente desktop. Se l'utente chiede informazioni su file o cartelle, usa i tool a tua disposizione."
        },
        @{
            role = "user"
            content = "Mostrami i file presenti nella cartella Documents."
        }
    )
    tools = @(
        @{
            type = "function"
            function = @{
                name = "list_directory"
                description = "Elenca i file e le cartelle all'interno di un percorso specificato."
                parameters = @{
                    type = "object"
                    properties = @{
                        path = @{
                            type = "string"
                            description = "Percorso assoluto o relativo della directory da elencare."
                        }
                    }
                    required = @("path")
                }
            }
        },
        @{
            type = "function"
            function = @{
                name = "read_file"
                description = "Legge il contenuto di un file fino a max_bytes."
                parameters = @{
                    type = "object"
                    properties = @{
                        path = @{
                            type = "string"
                            description = "Percorso del file da leggere."
                        }
                        max_bytes = @{
                            type = "integer"
                            description = "Numero massimo di byte da leggere."
                        }
                    }
                    required = @("path")
                }
            }
        }
    )
    tool_choice = "auto"
    temperature = 0.1
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/v1/chat/completions" `
        -Method Post `
        -Body $payload `
        -ContentType "application/json; charset=utf-8"

    Write-Host "Raw response from model:" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 5 | Write-Host

    $message = $response.choices[0].message
    if ($message.tool_calls) {
        Write-Host "`n[SUCCESS] Model emitted tool_calls correctly!" -ForegroundColor Green
        foreach ($call in $message.tool_calls) {
            Write-Host "Tool: $($call.function.name)" -ForegroundColor Magenta
            Write-Host "Arguments: $($call.function.arguments)" -ForegroundColor Magenta
        }
    } else {
        Write-Host "`n[WARNING] Model responded with text instead of tool_call:" -ForegroundColor Yellow
        Write-Host $message.content
    }
} catch {
    Write-Error "Request to llama-server failed: $_"
} finally {
    Write-Host "`nStopping llama-server (PID $($serverProcess.Id))..."
    Stop-Process -Id $serverProcess.Id -Force
    if (Test-Path $spikeLog) {
        Remove-Item -Path $spikeLog -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Cleaned up." -ForegroundColor Green
}
