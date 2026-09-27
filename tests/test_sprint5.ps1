# Test Suite for OpenAgent Sprint 5: Settings Persistence, User Memory, Dynamic Allowlist, Memory Tools & E2E Validation

param(
    [switch]$SkipLiveServer = $false
)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "         OPENAGENT SPRINT 5 VALIDATION SUITE            " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

# Configure Native Runtimes and Preload Assemblies
$dataBin = (Resolve-Path "src/OpenAgent.Data/bin/Debug/net8.0").Path
$runtimesPath = Join-Path $dataBin "runtimes\win-x64\native"
$env:Path = "$runtimesPath;$dataBin;$env:Path"

Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.core.dll")
Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.provider.e_sqlite3.dll")
Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.batteries_v2.dll")
Add-Type -Path (Join-Path $dataBin "Microsoft.Data.Sqlite.dll")

[SQLitePCL.Batteries_V2]::Init()

$toolsBin = (Resolve-Path "src/OpenAgent.Tools/bin/Debug/net8.0").Path
Add-Type -Path (Join-Path $toolsBin "HtmlAgilityPack.dll")

$modelDll = Resolve-Path "src/OpenAgent.Model/bin/Debug/net8.0/OpenAgent.Model.dll"
$toolsDll = Resolve-Path "src/OpenAgent.Tools/bin/Debug/net8.0/OpenAgent.Tools.dll"
$dataDll  = Resolve-Path "src/OpenAgent.Data/bin/Debug/net8.0/OpenAgent.Data.dll"
$agentDll = Resolve-Path "src/OpenAgent.Agent/bin/Debug/net8.0/OpenAgent.Agent.dll"

$appDllPath = Join-Path "src/OpenAgent.App/bin/Debug/net8.0-windows" "OpenAgent.dll"
if (-not (Test-Path $appDllPath)) {
    $appDllPath = Join-Path "src/OpenAgent.App/bin/Debug/net8.0-windows" "OpenAgent.App.dll"
}
$appDll = (Resolve-Path $appDllPath).Path

Add-Type -Path $modelDll
Add-Type -Path $toolsDll
Add-Type -Path $dataDll
Add-Type -Path $agentDll
Add-Type -Path $appDll

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

$tempTestDir = Join-Path $workspaceRoot "tests\temp\temp_test_sprint5"
if (Test-Path $tempTestDir) {
    Remove-Item -Path $tempTestDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $tempTestDir -Force | Out-Null

$testSettingsDb = Join-Path $tempTestDir "test_settings.db"
$testAuditDb = Join-Path $tempTestDir "test_audit.db"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 1: App Settings Persistence (SQLite)
# ------------------------------------------------------------------------------------------------
Write-Host "--- TEST GROUP 1: App Settings Persistence (SqliteUserSettingsRepository) ---" -ForegroundColor Yellow

$settingsRepo = New-Object OpenAgent.Data.SqliteUserSettingsRepository($testSettingsDb)
$settingsRepo.InitializeAsync().GetAwaiter().GetResult() | Out-Null

# 1.1 Default settings validation
$defaultSettings = $settingsRepo.GetSettingsAsync().GetAwaiter().GetResult()
Assert-Condition ($defaultSettings.ModelProfile -eq "Spark-X 2.5 1.7B") "Default model profile is 'Spark-X 2.5 1.7B'"
Assert-Condition ($defaultSettings.CpuThreads -eq 6) "Default CPU threads is 6"
Assert-Condition ($defaultSettings.ContextSize -eq 4096) "Default context size is 4096 tokens"
Assert-Condition ($defaultSettings.Port -eq 8080) "Default server port is 8080"
Assert-Condition ($defaultSettings.Device -eq "none") "Default acceleration device is 'none'"
Assert-Condition ($defaultSettings.ExecutionTimeoutSeconds -eq 180) "Default execution timeout is 180s"
Assert-Condition ($defaultSettings.CustomAllowedDirectories.Count -eq 0) "Default custom allowed directories list is empty"

# 1.2 Modify and save settings
$customFolder = Join-Path $tempTestDir "CustomWorkspace"
New-Item -ItemType Directory -Path $customFolder | Out-Null

$defaultSettings.CpuThreads = 8
$defaultSettings.ContextSize = 8192
$defaultSettings.ModelProfile = "Spark-X 4B"
$defaultSettings.ModelPath = "models/Spark-X4B-Q4_K_M.gguf"
$defaultSettings.Device = "Vulkan0"
$defaultSettings.ExecutionTimeoutSeconds = 240
$defaultSettings.CustomAllowedDirectories.Add($customFolder)
$settingsRepo.SaveSettingsAsync($defaultSettings).GetAwaiter().GetResult() | Out-Null

# 1.3 Re-open repository from fresh instance and reload
$reloadedRepo = New-Object OpenAgent.Data.SqliteUserSettingsRepository($testSettingsDb)
$reloadedSettings = $reloadedRepo.GetSettingsAsync().GetAwaiter().GetResult()

Assert-Condition ($reloadedSettings.CpuThreads -eq 8) "Persisted CPU threads reloaded accurately (8)"
Assert-Condition ($reloadedSettings.ContextSize -eq 8192) "Persisted context size reloaded accurately (8192)"
Assert-Condition ($reloadedSettings.ModelProfile -eq "Spark-X 4B") "Persisted model profile reloaded accurately ('Spark-X 4B')"
Assert-Condition ($reloadedSettings.Device -eq "Vulkan0") "Persisted acceleration device reloaded accurately ('Vulkan0')"
Assert-Condition ($reloadedSettings.ExecutionTimeoutSeconds -eq 240) "Persisted execution timeout reloaded accurately (240s)"
Assert-Condition ($reloadedSettings.CustomAllowedDirectories.Count -eq 1 -and $reloadedSettings.CustomAllowedDirectories[0] -eq $customFolder) "Persisted custom allowed directories reloaded accurately"

# 1.4 Switch to Qwen2.5 3B profile and verify persistence
$reloadedSettings.ModelProfile = [OpenAgent.Data.AppSettings]::ProfileQwen3B
$reloadedSettings.ModelPath = [OpenAgent.Data.AppSettings]::DefaultModelPathQwen3B
$settingsRepo.SaveSettingsAsync($reloadedSettings).GetAwaiter().GetResult() | Out-Null

$qwenRepo = New-Object OpenAgent.Data.SqliteUserSettingsRepository($testSettingsDb)
$qwenSettings = $qwenRepo.GetSettingsAsync().GetAwaiter().GetResult()
Assert-Condition ($qwenSettings.ModelProfile -eq "Qwen2.5 3B Instruct") "Persisted model profile reloaded accurately ('Qwen2.5 3B Instruct')"
Assert-Condition ($qwenSettings.ModelPath -eq "models/qwen2.5-3b-instruct-q4_k_m.gguf") "Persisted model path reloaded accurately ('models/qwen2.5-3b-instruct-q4_k_m.gguf')"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 2: Explicit User Memory Persistence (user_memory)
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 2: Explicit User Memory Persistence ---" -ForegroundColor Yellow

# 2.1 Set memory items
$settingsRepo.SetMemoryAsync("stile_risposta", "Rispondi in modo sintetico con elenchi puntati.").GetAwaiter().GetResult() | Out-Null
$settingsRepo.SetMemoryAsync("linguaggio_preferito", "C# e PowerShell.").GetAwaiter().GetResult() | Out-Null
$settingsRepo.SetMemoryAsync("progetto_default", "OpenAgent Desktop.").GetAwaiter().GetResult() | Out-Null

# 2.2 Get specific memory by key
$val1 = $settingsRepo.GetMemoryAsync("stile_risposta").GetAwaiter().GetResult()
Assert-Condition ($val1 -eq "Rispondi in modo sintetico con elenchi puntati.") "GetMemoryAsync retrieved correct value for 'stile_risposta'"

$valMissing = $settingsRepo.GetMemoryAsync("chiave_inesistente").GetAwaiter().GetResult()
Assert-Condition ($null -eq $valMissing) "GetMemoryAsync returns null for non-existing key"

# 2.3 Search memories by pattern
$searchResults = $settingsRepo.SearchMemoriesAsync("linguaggio").GetAwaiter().GetResult()
Assert-Condition ($searchResults.Count -eq 1 -and $searchResults[0].Key -eq "linguaggio_preferito") "SearchMemoriesAsync found memory matching pattern 'linguaggio'"

# 2.4 Update existing memory key
$settingsRepo.SetMemoryAsync("stile_risposta", "Rispondi sempre in italiano accademico.").GetAwaiter().GetResult() | Out-Null
$valUpdated = $settingsRepo.GetMemoryAsync("stile_risposta").GetAwaiter().GetResult()
Assert-Condition ($valUpdated -eq "Rispondi sempre in italiano accademico.") "SetMemoryAsync successfully updated existing key value"

# 2.5 Delete single memory item
$deleted = $settingsRepo.DeleteMemoryAsync("progetto_default").GetAwaiter().GetResult()
Assert-Condition ($deleted -eq $true) "DeleteMemoryAsync returns true on successful deletion"
$valDeleted = $settingsRepo.GetMemoryAsync("progetto_default").GetAwaiter().GetResult()
Assert-Condition ($null -eq $valDeleted) "Deleted memory is no longer found in repository"

# 2.6 Get all memories
$allMemories = $settingsRepo.GetAllMemoriesAsync().GetAwaiter().GetResult()
Assert-Condition ($allMemories.Count -eq 2) "GetAllMemoriesAsync returned expected count (2)"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 3: Dynamic Allowlist & Security Sandbox (PermissionManager)
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 3: Dynamic Allowlist in PermissionManager ---" -ForegroundColor Yellow

$permMgr = New-Object OpenAgent.Agent.Security.PermissionManager($workspaceRoot)

# 3.1 Check system default folders allowed
$docs = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::MyDocuments)
Assert-Condition ($permMgr.IsPathAllowed($docs, [OpenAgent.Tools.Security.PathAccessType]::Read)) "Documents folder allowed by default"
Assert-Condition ($permMgr.IsPathAllowed($workspaceRoot, [OpenAgent.Tools.Security.PathAccessType]::Read)) "Workspace root allowed by default"

# 3.2 Check system blocked folders denied
$winDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Windows)
$checkWin = $permMgr.CheckPathAccess($winDir, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($checkWin.IsAllowed -eq $false) "Windows folder blocked by veto policy"

# 3.3 Add new custom folder dynamically (outside Documents, Downloads, Desktop and Workspace)
$customFolder2 = "C:\OpenAgent_Test_DynamicFolder"
if (Test-Path $customFolder2) { Remove-Item -Path $customFolder2 -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $customFolder2 | Out-Null
$testFileInCustom = Join-Path $customFolder2 "appunti.txt"
[System.IO.File]::WriteAllText($testFileInCustom, "Dati cartella autorizzata.")

Assert-Condition ($permMgr.IsPathAllowed($testFileInCustom, [OpenAgent.Tools.Security.PathAccessType]::Read) -eq $false) "File in new folder initially denied before adding to allowlist"

$permMgr.AddAllowedDirectory($customFolder2)
Assert-Condition ($permMgr.IsPathAllowed($testFileInCustom, [OpenAgent.Tools.Security.PathAccessType]::Read) -eq $true) "File in new folder allowed after AddAllowedDirectory"

# 3.4 Security check: Veto prevents adding Windows or root directly
$threwVeto = $false
try {
    $permMgr.AddAllowedPath("C:\Windows\System32")
} catch {
    $threwVeto = $true
}
Assert-Condition ($threwVeto -eq $true) "AddAllowedPath threw exception when attempting to allow protected system folder"

# 3.5 Remove allowed directory dynamically
$removed = $permMgr.RemoveAllowedDirectory($customFolder2)
Assert-Condition ($removed -eq $true) "RemoveAllowedDirectory returned true"
Assert-Condition ($permMgr.IsPathAllowed($testFileInCustom, [OpenAgent.Tools.Security.PathAccessType]::Read) -eq $false) "Access revoked after RemoveAllowedDirectory"

# 3.6 Reset to default policies
$permMgr.AddAllowedDirectory($customFolder2)
Assert-Condition ($permMgr.CustomAllowedPaths.Count -gt 0) "Custom allowed paths present before reset"
$permMgr.ResetDefaultPolicies()
Assert-Condition ($permMgr.CustomAllowedPaths.Count -eq 0) "ResetDefaultPolicies cleared all custom allowed paths"
Remove-Item -Path $customFolder2 -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# TEST GROUP 4: Explicit Memory Tools (remember_user_preference & recall_user_memory)
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 4: Memory Tools Execution ---" -ForegroundColor Yellow

$rememberTool = New-Object OpenAgent.Tools.Memory.RememberPreferenceTool($settingsRepo)
$recallTool = New-Object OpenAgent.Tools.Memory.RecallMemoryTool($settingsRepo)

# 4.1 Tool definitions and contracts
Assert-Condition ($rememberTool.Name -eq "remember_user_preference") "RememberPreferenceTool name is 'remember_user_preference'"
Assert-Condition ($recallTool.Name -eq "recall_user_memory") "RecallMemoryTool name is 'recall_user_memory'"
Assert-Condition ($rememberTool.RiskLevel -eq [OpenAgent.Tools.ToolRiskLevel]::Read) "RememberPreferenceTool RiskLevel is Read (non-destructive autonomous memory)"

# 4.2 Execute remember_user_preference
$remArgsJson = [System.Text.Json.JsonDocument]::Parse('{"key": "editor_preferito", "value": "Visual Studio Code"}')
$remResult = $rememberTool.ExecuteAsync($remArgsJson).GetAwaiter().GetResult()
Assert-Condition ($remResult.Success -eq $true -and $remResult.Content -match "memorizzata con successo") "remember_user_preference executed successfully"

$storedEditor = $settingsRepo.GetMemoryAsync("editor_preferito").GetAwaiter().GetResult()
Assert-Condition ($storedEditor -eq "Visual Studio Code") "Preference saved in SQLite database: 'Visual Studio Code'"

# 4.3 Execute recall_user_memory with search pattern
$recArgsJson = [System.Text.Json.JsonDocument]::Parse('{"pattern": "editor"}')
$recResult = $recallTool.ExecuteAsync($recArgsJson).GetAwaiter().GetResult()
Assert-Condition ($recResult.Success -eq $true -and $recResult.Content -match "Visual Studio Code") "recall_user_memory found and returned 'editor_preferito'"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 5: Dynamic System Prompt Injection in AgentController
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 5: Dynamic System Prompt Injection ---" -ForegroundColor Yellow

$registry = New-Object OpenAgent.Tools.ToolRegistry
$registry.Register((New-Object OpenAgent.Tools.FileSystem.ReadFileTool($permMgr)))
$registry.Register($rememberTool)
$registry.Register($recallTool)

$mockProvider = New-Object OpenAgent.Model.MockModelProvider
$stateMachine = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$auditRepo = New-Object OpenAgent.Data.SqliteAuditRepository($testAuditDb)
$auditRepo.InitializeAsync().GetAwaiter().GetResult() | Out-Null

$agentController = New-Object OpenAgent.Agent.AgentController(
    $mockProvider,
    $registry,
    $permMgr,
    $stateMachine,
    $auditRepo,
    $settingsRepo
)

# 5.1 Refresh system prompt with memories
$agentController.RefreshSystemPromptWithUserMemoriesAsync().GetAwaiter().GetResult() | Out-Null
$systemMessage = $agentController.History[0].Content
Assert-Condition ($systemMessage -match "editor_preferito" -and $systemMessage -match "Visual Studio Code") "System prompt contains injected user preference 'editor_preferito: Visual Studio Code'"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 6: Windows Autostart Manager Verification
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 6: Windows Autostart Manager ---" -ForegroundColor Yellow

# Safe check without corrupting user setup
$initialAutostart = [OpenAgent.App.Native.WindowsAutostartManager]::IsAutostartEnabled()
Write-Host "  -> Stato autostart iniziale Windows: $initialAutostart" -ForegroundColor Cyan

# Test toggle
$setResult = [OpenAgent.App.Native.WindowsAutostartManager]::SetAutostart($false, $false)
Assert-Condition ($setResult -eq $true) "SetAutostart($false) executed cleanly without security errors"
$checkDisabled = [OpenAgent.App.Native.WindowsAutostartManager]::IsAutostartEnabled()
Assert-Condition ($checkDisabled -eq $false) "IsAutostartEnabled correctly returns false when disabled"

# Restore original state if it was originally enabled
if ($initialAutostart) {
    [OpenAgent.App.Native.WindowsAutostartManager]::SetAutostart($true, $false) | Out-Null
}

# ------------------------------------------------------------------------------------------------
# TEST GROUP 7: End-to-End Multi-Step Agent Integration with Memory
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 7: End-to-End Multi-Step Agent with Memory ---" -ForegroundColor Yellow

# Clear memories for fresh scenario
$settingsRepo.ClearMemoriesAsync().GetAwaiter().GetResult() | Out-Null

# Scenario:
# Step 1: Model receives user prompt "Imposta per l'ambiente il tema scuro."
# Step 1 Response: Model calls remember_user_preference(key="tema", value="scuro")
$mockProvider.EnqueueToolCall("remember_user_preference", '{"key": "tema", "value": "scuro"}', "call_mem_1")

# Step 2 Response: Model confirms to user
$mockProvider.EnqueueAssistantReply("Ho memorizzato che preferisci il tema scuro per tutte le prossime sessioni.")

$reply = $agentController.ProcessPromptAsync("Imposta per l'ambiente il tema scuro.").GetAwaiter().GetResult()

Assert-Condition ($reply -match "tema scuro") "Agent returned final confirmation reply"
Assert-Condition ($agentController.StepsExecutedCount -eq 1) "Agent executed exactly 1 tool turn before answering"

$savedTema = $settingsRepo.GetMemoryAsync("tema").GetAwaiter().GetResult()
Assert-Condition ($savedTema -eq "scuro") "Memory persisted in SQLite repository: 'tema' = 'scuro'"

# Check system prompt refreshed dynamically
$agentController.RefreshSystemPromptWithUserMemoriesAsync().GetAwaiter().GetResult() | Out-Null
$updatedSysMsg = $agentController.History[0].Content
Assert-Condition ($updatedSysMsg -match "tema" -and $updatedSysMsg -match "scuro") "System prompt immediately contains the newly remembered preference"

# Verify Audit Trail recorded the memory tool execution
$auditLogs = $auditRepo.GetRecentLogsAsync(10).GetAwaiter().GetResult()
$memoryAudit = $auditLogs | Where-Object { $_.ToolName -eq "remember_user_preference" }
Assert-Condition ($null -ne $memoryAudit) "Execution of remember_user_preference recorded in SQLite audit trail"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 8: Distributable Release Package Verification (Sprint 5 Deliverable)
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 8: Release Package Verification ---" -ForegroundColor Yellow

$releaseExe = Join-Path $workspaceRoot "dist\OpenAgent_v1.0\OpenAgent.exe"
$releaseLlama = Join-Path $workspaceRoot "dist\OpenAgent_v1.0\runtime\llama\llama-server.exe"
$releaseVulkan = Join-Path $workspaceRoot "dist\OpenAgent_v1.0\runtime\llama\ggml-vulkan.dll"
$releaseZip = Join-Path $workspaceRoot "dist\OpenAgent_v1.0_win-x64.zip"

Assert-Condition (Test-Path $releaseExe) "dist/OpenAgent_v1.0/OpenAgent.exe exists and is built"
Assert-Condition (Test-Path $releaseLlama) "dist/OpenAgent_v1.0/runtime/llama/llama-server.exe exists"
Assert-Condition (Test-Path $releaseVulkan) "dist/OpenAgent_v1.0/runtime/llama/ggml-vulkan.dll exists"
Assert-Condition (Test-Path $releaseZip) "dist/OpenAgent_v1.0_win-x64.zip package archive exists"

# Cleanup temporary test files
Remove-Item -Path $tempTestDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# SUMMARY
# ------------------------------------------------------------------------------------------------
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "                SPRINT 5 TEST RESULTS                   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  Passed: $passCount" -ForegroundColor Green
Write-Host "  Failed: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
