# Test Suite for OpenAgent Persistent Memory System & MemoryIntentHandler

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "       OPENAGENT MEMORY SYSTEM VALIDATION SUITE         " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

# Preload Native Runtimes and Assemblies
$dataBin = (Resolve-Path "src/OpenAgent.Data/bin/Debug/net8.0").Path
$runtimesPath = Join-Path $dataBin "runtimes\win-x64\native"
$env:Path = "$runtimesPath;$dataBin;$env:Path"

Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.core.dll")
Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.provider.e_sqlite3.dll")
Add-Type -Path (Join-Path $dataBin "SQLitePCLRaw.batteries_v2.dll")
Add-Type -Path (Join-Path $dataBin "Microsoft.Data.Sqlite.dll")

[SQLitePCL.Batteries_V2]::Init()

$modelDll = Resolve-Path "src/OpenAgent.Model/bin/Debug/net8.0/OpenAgent.Model.dll"
$toolsDll = Resolve-Path "src/OpenAgent.Tools/bin/Debug/net8.0/OpenAgent.Tools.dll"
$dataDll  = Resolve-Path "src/OpenAgent.Data/bin/Debug/net8.0/OpenAgent.Data.dll"
$agentDll = Resolve-Path "src/OpenAgent.Agent/bin/Debug/net8.0/OpenAgent.Agent.dll"

Add-Type -Path $modelDll
Add-Type -Path $toolsDll
Add-Type -Path $dataDll
Add-Type -Path $agentDll

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

$tempDir = Join-Path $workspaceRoot "tests\temp\temp_test_memory"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$testDbPath = Join-Path $tempDir "openagent_mem_test.db"
$testAuditDbPath = Join-Path $tempDir "audit_mem_test.db"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 1: MemoryIntentHandler Pattern Detection
# ------------------------------------------------------------------------------------------------
Write-Host "--- TEST GROUP 1: MemoryIntentHandler Pattern Detection ---" -ForegroundColor Yellow

# 1.1 Agent Name variations
$p1 = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Ricordati che il tuo nome è Octy")
Assert-Condition ($p1.Type -eq [OpenAgent.Agent.MemoryIntentType]::SetAgentName -and $p1.Value -eq "Octy") "Detected: 'Ricordati che il tuo nome è Octy' -> SetAgentName, Value=Octy"

$p2 = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Da oggi ti chiami Octy")
Assert-Condition ($p2.Type -eq [OpenAgent.Agent.MemoryIntentType]::SetAgentName -and $p2.Value -eq "Octy") "Detected: 'Da oggi ti chiami Octy' -> SetAgentName, Value=Octy"

$p3 = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Chiamati Octy")
Assert-Condition ($p3.Type -eq [OpenAgent.Agent.MemoryIntentType]::SetAgentName -and $p3.Value -eq "Octy") "Detected: 'Chiamati Octy' -> SetAgentName, Value=Octy"

$p4 = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Ti chiami Octy, ricordalo")
Assert-Condition ($p4.Type -eq [OpenAgent.Agent.MemoryIntentType]::SetAgentName -and $p4.Value -eq "Octy") "Detected: 'Ti chiami Octy, ricordalo' -> SetAgentName, Value=Octy"

# 1.2 User Name
$pUser = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Ricordati che mi chiamo Alessandro")
Assert-Condition ($pUser.Type -eq [OpenAgent.Agent.MemoryIntentType]::SetUserName -and $pUser.Value -eq "Alessandro") "Detected: 'Ricordati che mi chiamo Alessandro' -> SetUserName, Value=Alessandro"

# 1.3 Asking Agent Name
$pAsk = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Come ti chiami?")
Assert-Condition ($pAsk.Type -eq [OpenAgent.Agent.MemoryIntentType]::AskAgentName) "Detected: 'Come ti chiami?' -> AskAgentName"

$pWho = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Chi sei?")
Assert-Condition ($pWho.Type -eq [OpenAgent.Agent.MemoryIntentType]::AskAgentName) "Detected: 'Chi sei?' -> AskAgentName"

# 1.4 Recall All Memories Query
$pRec = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Cosa ricordi di me?")
Assert-Condition ($pRec.Type -eq [OpenAgent.Agent.MemoryIntentType]::RecallAll) "Detected: 'Cosa ricordi di me?' -> RecallAll"

$pRec2 = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Cosa hai in memoria?")
Assert-Condition ($pRec2.Type -eq [OpenAgent.Agent.MemoryIntentType]::RecallAll) "Detected: 'Cosa hai in memoria?' -> RecallAll"

# 1.5 Structured Preference
$pPrefStruct = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Memorizza: editor_preferito = Visual Studio Code")
Assert-Condition ($pPrefStruct.Type -eq [OpenAgent.Agent.MemoryIntentType]::SetPreference -and $pPrefStruct.Key -eq "editor_preferito" -and $pPrefStruct.Value -eq "Visual Studio Code") "Detected structured preference 'editor_preferito = Visual Studio Code'"

# 1.6 Natural Language Preference
$pPrefNat = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Ricordati che preferisco il tema scuro")
Assert-Condition ($pPrefNat.Type -eq [OpenAgent.Agent.MemoryIntentType]::SetPreference -and $pPrefNat.Key -eq "tema_preferito") "Detected natural language preference -> Key=tema_preferito"

# 1.7 Clear Memory
$pClear = [OpenAgent.Agent.MemoryIntentHandler]::DetectIntent("Svuota la memoria")
Assert-Condition ($pClear.Type -eq [OpenAgent.Agent.MemoryIntentType]::ClearMemory) "Detected: 'Svuota la memoria' -> ClearMemory"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 2: End-to-End Agent Execution with SQLite Persistence
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 2: End-to-End Agent Execution with SQLite Persistence ---" -ForegroundColor Yellow

$settingsRepo = New-Object OpenAgent.Data.SqliteUserSettingsRepository($testDbPath)
$settingsRepo.InitializeAsync().GetAwaiter().GetResult() | Out-Null

$auditRepo = New-Object OpenAgent.Data.SqliteAuditRepository($testAuditDbPath)
$auditRepo.InitializeAsync().GetAwaiter().GetResult() | Out-Null

$permMgr = New-Object OpenAgent.Agent.Security.PermissionManager($workspaceRoot)
$registry = New-Object OpenAgent.Tools.ToolRegistry
$registry.Register((New-Object OpenAgent.Tools.Memory.RememberPreferenceTool($settingsRepo)))
$registry.Register((New-Object OpenAgent.Tools.Memory.RecallMemoryTool($settingsRepo)))

$mockProvider = New-Object OpenAgent.Model.MockModelProvider
$sm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine

$controller = New-Object OpenAgent.Agent.AgentController(
    $mockProvider,
    $registry,
    $permMgr,
    $sm,
    $auditRepo,
    $settingsRepo
)

# 2.1 Send "Ricordati che il tuo nome è Octy"
Write-Host "  -> Processing prompt: 'Ricordati che il tuo nome è Octy'..." -ForegroundColor Cyan
$replyName = $controller.ProcessPromptAsync("Ricordati che il tuo nome è Octy").GetAwaiter().GetResult()

Assert-Condition ($replyName -match "Octy") "Agent confirmed remembering its new name 'Octy'"
Assert-Condition ($controller.AgentCustomName -eq "Octy") "controller.AgentCustomName updated immediately to 'Octy'"

# 2.2 Verify SQLite database persistence
$savedDbName = $settingsRepo.GetMemoryAsync("nome_agente").GetAwaiter().GetResult()
Assert-Condition ($savedDbName -eq "Octy") "SQLite user_memory table accurately contains 'nome_agente' = 'Octy'"

# 2.3 Verify SystemPrompt reflects 'Octy'
$sysPromptContent = $controller.History[0].Content
Assert-Condition ($sysPromptContent -match "Sei Octy" -and $sysPromptContent -match "\[nome_agente\]:\s*Octy") "System Prompt dynamically injected with 'Sei Octy' and memory list"

# 2.4 Query: "Come ti chiami?"
Write-Host "  -> Processing prompt: 'Come ti chiami?'..." -ForegroundColor Cyan
$replyAskName = $controller.ProcessPromptAsync("Come ti chiami?").GetAwaiter().GetResult()
Assert-Condition ($replyAskName -match "Octy") "Agent replied to 'Come ti chiami?' stating its name is 'Octy'"

# 2.5 Save general preference: "Memorizza: stile_risposta = sintetico"
Write-Host "  -> Processing prompt: 'Memorizza: stile_risposta = sintetico'..." -ForegroundColor Cyan
$replyPref = $controller.ProcessPromptAsync("Memorizza: stile_risposta = sintetico").GetAwaiter().GetResult()
Assert-Condition ($replyPref -match "stile_risposta") "Agent confirmed saving preference 'stile_risposta'"

$savedStile = $settingsRepo.GetMemoryAsync("stile_risposta").GetAwaiter().GetResult()
Assert-Condition ($savedStile -eq "sintetico") "SQLite contains 'stile_risposta' = 'sintetico'"

# 2.6 Query: "Cosa ricordi di me?"
Write-Host "  -> Processing prompt: 'Cosa ricordi di me?'..." -ForegroundColor Cyan
$replyRecall = $controller.ProcessPromptAsync("Cosa ricordi di me?").GetAwaiter().GetResult()
Assert-Condition ($replyRecall -match "nome_agente" -and $replyRecall -match "Octy" -and $replyRecall -match "stile_risposta") "Recall returned both 'nome_agente' (Octy) and 'stile_risposta' (sintetico)"

# 2.7 Verify SQLite Audit Trail logged the memory operations
$audits = $auditRepo.GetRecentLogsAsync(10).GetAwaiter().GetResult()
$remAudits = @($audits | Where-Object { $_.ToolName -eq "remember_user_preference" })
$recAudits = @($audits | Where-Object { $_.ToolName -eq "recall_user_memory" })
Assert-Condition ($remAudits.Count -ge 2) "Audit trail recorded remember_user_preference executions (Count: $($remAudits.Count))"
Assert-Condition ($recAudits.Count -ge 1) "Audit trail recorded recall_user_memory execution"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 3: Multi-Session Persistence Simulation
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 3: Multi-Session Persistence Simulation ---" -ForegroundColor Yellow

# Create a completely brand new AgentController simulating the NEXT session with the SAME SQLite database
$newController = New-Object OpenAgent.Agent.AgentController(
    $mockProvider,
    $registry,
    $permMgr,
    (New-Object OpenAgent.Agent.StateMachine.AgentStateMachine),
    $auditRepo,
    $settingsRepo
)

# Initialize memories
$newController.RefreshSystemPromptWithUserMemoriesAsync().GetAwaiter().GetResult() | Out-Null

Assert-Condition ($newController.AgentCustomName -eq "Octy") "New session controller restored AgentCustomName = 'Octy' from SQLite"
Assert-Condition ($newController.History[0].Content -match "Sei Octy") "New session System Prompt initialized with 'Sei Octy'"

$nextSessionAsk = $newController.ProcessPromptAsync("Qual è il tuo nome?").GetAwaiter().GetResult()
Assert-Condition ($nextSessionAsk -match "Octy") "Next session agent still knows its name is Octy upon inquiry"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 4: Robust JSON Extraction from Content (Fallback Tool Calling)
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 4: Robust Tool Extraction from Model Text ---" -ForegroundColor Yellow

# Simulate an LLM outputting text before and after a ```json block
$embeddedModelOutput = "Certamente! Ho preso nota. ```json {`"name`": `"remember_user_preference`", `"arguments`": {`"key`": `"progetto`", `"value`": `"OpenAgent`"}} ``` Fammi sapere se serve altro."

$mockProvider.EnqueueAssistantReply($embeddedModelOutput)

$replyEmbedded = $controller.ProcessPromptAsync("Procedi con il prossimo passaggio.").GetAwaiter().GetResult()
Assert-Condition ($controller.StepsExecutedCount -gt 0) "Agent extracted and executed embedded tool call from model markdown output"

$savedProj = $settingsRepo.GetMemoryAsync("progetto").GetAwaiter().GetResult()
Assert-Condition ($savedProj -eq "OpenAgent") "Embedded tool call successfully persisted 'progetto' = 'OpenAgent' in SQLite"

# ------------------------------------------------------------------------------------------------
# CLEANUP
# ------------------------------------------------------------------------------------------------
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# SUMMARY
# ------------------------------------------------------------------------------------------------
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "TEST RESULTS: Passed: $passCount, Failed: $failCount" -ForegroundColor ($failCount -eq 0 ? "Green" : "Red")
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
