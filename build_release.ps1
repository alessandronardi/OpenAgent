# Packaging & Release Automation Script for OpenAgent (v1.0)
param(
    [bool]$IncludeModel = $false,
    [bool]$SelfContained = $false,
    [bool]$CreateZip = $true,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

$rootDir = (Get-Item .).FullName
$distDir = Join-Path $rootDir "dist\OpenAgent_v1.0"
$zipPath = Join-Path $rootDir "dist\OpenAgent_v1.0_win-x64.zip"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "         OPENAGENT v1.0 PACKAGING & RELEASE BUILD       " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

# 1. Clean previous dist
if (Test-Path $distDir) {
    Write-Host "[1/5] Pulizia directory di release precedente: $distDir" -ForegroundColor Yellow
    Remove-Item -Path $distDir -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

# 2. Publish WPF Application
Write-Host "[2/5] Compilazione e pubblicazione OpenAgent.App ($Configuration, win-x64)..." -ForegroundColor Yellow
$appProj = Join-Path $rootDir "src\OpenAgent.App\OpenAgent.App.csproj"

$selfContainedArg = if ($SelfContained) { "true" } else { "false" }
& dotnet publish "$appProj" -c $Configuration -r win-x64 --self-contained $selfContainedArg -o "$distDir"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERRORE: dotnet publish terminato con codice $LASTEXITCODE" -ForegroundColor Red
    exit 1
}
Write-Host "  -> OpenAgent.App pubblicato con successo." -ForegroundColor Green

# Update root OpenAgent.exe single-file launcher
$rootExePublishDir = Join-Path $rootDir "publish_tmp_root"
& dotnet publish "$appProj" -c $Configuration -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$rootExePublishDir"
if (Test-Path "$rootExePublishDir\OpenAgent.exe") {
    Copy-Item "$rootExePublishDir\OpenAgent.exe" "$rootDir\OpenAgent.exe" -Force
}
if (Test-Path "$distDir\runtimes") {
    Copy-Item -Path "$distDir\runtimes" -Destination "$rootDir" -Recurse -Force
}
Remove-Item "$rootExePublishDir" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  -> Eseguibile di root (OpenAgent.exe) e runtimes aggiornati con successo." -ForegroundColor Green

# 3. Copy Local llama-server & Vulkan Runtime
Write-Host "[3/5] Confezionamento runtime locale llama.cpp (Vulkan)..." -ForegroundColor Yellow
$runtimeSrc = Join-Path $rootDir "runtime\llama"
$runtimeDest = Join-Path $distDir "runtime\llama"
New-Item -ItemType Directory -Path $runtimeDest -Force | Out-Null

# Essential files for llama-server with Vulkan / CPU
$essentialPatterns = @(
    "llama-server.exe",
    "llama-server-impl.dll",
    "llama.dll",
    "llama-common.dll",
    "ggml.dll",
    "ggml-vulkan.dll",
    "ggml-cpu-*.dll",
    "ggml-base.dll",
    "libomp.dll",
    "mtmd.dll"
)

foreach ($pattern in $essentialPatterns) {
    $matched = Get-ChildItem -Path $runtimeSrc -Filter $pattern -File
    foreach ($file in $matched) {
        Copy-Item -Path $file.FullName -Destination $runtimeDest -Force
    }
}

$copiedRuntimeCount = (Get-ChildItem -Path $runtimeDest -File).Count
Write-Host "  -> Runtime copiato con successo ($copiedRuntimeCount binari e librerie Vulkan/CPU)." -ForegroundColor Green

# 4. Prepare Models & Data Folders
Write-Host "[4/5] Preparazione cartelle models e data..." -ForegroundColor Yellow
$modelsDest = Join-Path $distDir "models"
$dataDest = Join-Path $distDir "data"
New-Item -ItemType Directory -Path $modelsDest -Force | Out-Null
New-Item -ItemType Directory -Path $dataDest -Force | Out-Null

$sourceModel = Join-Path $rootDir "models\Spark-X2.5-1.7B-Q4_K_M.gguf"
if (Test-Path $sourceModel) {
    if ($IncludeModel) {
        Write-Host "  -> Copia modello Spark-X 1.7B GGUF in corso (~1.1 GB)..." -ForegroundColor Yellow
        Copy-Item -Path $sourceModel -Destination $modelsDest -Force
        Write-Host "  -> Modello Spark-X 1.7B incluso nel pacchetto." -ForegroundColor Green
    } else {
        $modelInfoFile = Join-Path $modelsDest "README_MODEL.txt"
        $modelInfo = @"
OpenAgent v1.0 - Modelli GGUF
Posiziona qui il modello GGUF predefinito:
  Nome file: Spark-X2.5-1.7B-Q4_K_M.gguf
  Modello testato: Spark-X 2.5 1.7B (Q4_K_M)

Per includere automaticamente il file nella release, esegui:
  .\build_release.ps1 -IncludeModel
"@
        Set-Content -Path $modelInfoFile -Value $modelInfo -Encoding utf8
        Write-Host "  -> Cartella models predisposta (file modello omesso per velocizzare la build; usa -IncludeModel per includerlo)." -ForegroundColor Cyan
    }
}

# Add Readme instructions for user
$readmePath = Join-Path $distDir "README.txt"
$readmeContent = @"
========================================================
             OPENAGENT (v1.0) - Release Locale
========================================================

Avvio Rapido:
1. Fai doppio clic su 'OpenAgent.exe'.
2. L'assistente si posizionerà nell'angolo in basso a destra dello schermo
   e nella System Tray (icona fantasmino).
3. Usa la combinazione Ctrl + Spazio (oppure Alt + Spazio) per attivare
   la barra di digitazione da qualsiasi applicazione.
4. Fai clic destro sull'icona nella barra delle notifiche o sul pulsante ⚙️
   per configurare cartelle autorizzate (Allowlist), modello e memoria.

Cartelle:
- models/  : Contiene i pesi quantizzati Spark-X GGUF
- runtime/ : Motore di inferenza locale llama-server Vulkan/CPU
- data/    : Database SQLite locale per log di audit, preferenze e impostazioni
========================================================
"@
Set-Content -Path $readmePath -Value $readmeContent -Encoding utf8

# 5. Create ZIP Archive
if ($CreateZip) {
    Write-Host "[5/5] Creazione archivio ZIP finale: $zipPath..." -ForegroundColor Yellow
    Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath -CompressionLevel Optimal -Force
    $zipSizeMb = (Get-Item $zipPath).Length / (1024 * 1024)
    Write-Host "  -> Archivio creato con successo ($($zipSizeMb.ToString("F1")) MB)." -ForegroundColor Green
} else {
    Write-Host "[5/5] Creazione archivio ZIP saltata (-CreateZip `$false)." -ForegroundColor Gray
}

Write-Host "`nRelease v1.0 completata con successo!" -ForegroundColor Green
Write-Host "Cartella distribuibile: $distDir" -ForegroundColor Cyan
if ($CreateZip) {
    Write-Host "Pacchetto ZIP:          $zipPath" -ForegroundColor Cyan
}
Write-Host ""
