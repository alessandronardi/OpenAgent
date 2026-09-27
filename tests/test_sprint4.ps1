# Test Suite for OpenAgent Sprint 4: Agent Multi-Step Loop, Semantic Loop Detection, Cancellation & Bounded Turns

param(
    [switch]$SkipLiveServer = $false
)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "         OPENAGENT SPRINT 4 VALIDATION SUITE            " -ForegroundColor Cyan
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

# ------------------------------------------------------------------------------------------------
# TEST GROUP 1: Semantic Call Tracker & Loop Detection
# ------------------------------------------------------------------------------------------------
Write-Host "--- TEST GROUP 1: Semantic Call Tracker & Loop Detection ---" -ForegroundColor Yellow

$tracker = New-Object OpenAgent.Agent.SemanticCallTracker

# 1.1 JSON normalization: equivalence across key order, spacing and path slashes
$key1 = [OpenAgent.Agent.SemanticCallTracker]::NormalizeCallKey("read_file", '{"path": "C:\\Docs\\notes.txt", "max_bytes": 1024}')
$key2 = [OpenAgent.Agent.SemanticCallTracker]::NormalizeCallKey("read_file", '{"max_bytes": 1024, "path": "C:/Docs/notes.txt"}')
Assert-Condition ($key1 -eq $key2) "SemanticCallTracker normalized JSON with different key orders and path slashes to identical keys"

# 1.2 First call returns None
$res1 = $tracker.RecordAndCheck("read_file", '{"path": "test.txt"}')
Assert-Condition ($res1.Result -eq [OpenAgent.Agent.LoopDetectionResult]::None) "First tool invocation returns LoopDetectionResult.None"

# 1.3 Second identical call returns Warning
$res2 = $tracker.RecordAndCheck("read_file", '{"path": "test.txt"}')
Assert-Condition ($res2.Result -eq [OpenAgent.Agent.LoopDetectionResult]::Warning) "Second identical invocation triggers Warning threshold"

# 1.4 Third identical call returns Halt
$res3 = $tracker.RecordAndCheck("read_file", '{"path": "test.txt"}')
Assert-Condition ($res3.Result -eq [OpenAgent.Agent.LoopDetectionResult]::Halt) "Third identical invocation triggers Halt threshold"

# 1.5 Ping-Pong oscillation detection (A -> B -> A -> B)
$pingPongTracker = New-Object OpenAgent.Agent.SemanticCallTracker
$pingPongTracker.RecordAndCheck("tool_a", '{"step": 1}') | Out-Null
$pingPongTracker.RecordAndCheck("tool_b", '{"step": 2}') | Out-Null
$pingPongTracker.RecordAndCheck("tool_a", '{"step": 1}') | Out-Null
$resPingPong = $pingPongTracker.RecordAndCheck("tool_b", '{"step": 2}')
Assert-Condition ($resPingPong.Result -eq [OpenAgent.Agent.LoopDetectionResult]::Halt -and $resPingPong.Message -match "ping-pong") "Ping-Pong oscillation (A -> B -> A -> B) detected and halted"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 2: Multi-Step Chaining & State Machine Integration
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 2: Multi-Step Chaining (Mock Provider) ---" -ForegroundColor Yellow

$tempSandboxDir = Join-Path $workspaceRoot "tests\temp\temp_test_sprint4"
if (Test-Path $tempSandboxDir) { Remove-Item -Path $tempSandboxDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $tempSandboxDir -Force | Out-Null

$testFile = Join-Path $tempSandboxDir "dati.txt"
[System.IO.File]::WriteAllText($testFile, "Contenuto confidenziale Sprint 4.")

$permMgr = New-Object OpenAgent.Agent.Security.PermissionManager($workspaceRoot)
$registry = New-Object OpenAgent.Tools.ToolRegistry
$registry.Register((New-Object OpenAgent.Tools.FileSystem.ListDirectoryTool($permMgr)))
$registry.Register((New-Object OpenAgent.Tools.FileSystem.ReadFileTool($permMgr)))

$mockProvider = New-Object OpenAgent.Model.MockModelProvider

# Turn 1: Model calls list_directory
$normDir = $tempSandboxDir.Replace('\', '/')
$mockProvider.EnqueueToolCall("list_directory", ('{"path":"' + $normDir + '"}'), "call_001")

# Turn 2: Model calls read_file
$normFile = $testFile.Replace('\', '/')
$mockProvider.EnqueueToolCall("read_file", ('{"path":"' + $normFile + '"}'), "call_002")

# Turn 3: Model returns final synthesis
$mockProvider.EnqueueAssistantReply("Ho esaminato la cartella e letto il file: il contenuto riporta 'Contenuto confidenziale Sprint 4'.")

$auditDbPath = Join-Path $tempSandboxDir "audit_multistep.db"
$auditRepo = New-Object OpenAgent.Data.SqliteAuditRepository($auditDbPath)
$auditRepo.InitializeAsync().GetAwaiter().GetResult()

$sm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$agent = New-Object OpenAgent.Agent.AgentController($mockProvider, $registry, $permMgr, $sm, $auditRepo)

$finalResult = $agent.ProcessPromptAsync("Esamina la cartella e leggi dati.txt").GetAwaiter().GetResult()

Assert-Condition ($mockProvider.CallCount -eq 3) "Multi-step loop executed exactly 3 inference turns (2 tool calls + 1 synthesis)"
Assert-Condition ($agent.StepsExecutedCount -eq 2) "AgentController recorded StepsExecutedCount = 2"
Assert-Condition ($finalResult -match "Contenuto confidenziale") "Final agent response synthesized multi-step findings correctly"
Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Agent state returned to Idle after multi-step completion"

# Verify SQLite logged both steps under the same session ID
$loggedAudits = $auditRepo.GetRecentLogsAsync(10).GetAwaiter().GetResult()
Assert-Condition ($loggedAudits.Count -eq 2) "SQLite Audit recorded exactly 2 tool executions"
Assert-Condition ($loggedAudits[0].SessionId -eq $loggedAudits[1].SessionId) "Both multi-step tools share the exact same SessionId"
Assert-Condition ($loggedAudits[0].SessionId -eq $agent.SessionId) "Audit SessionId matches AgentController.SessionId"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 3: Bounded Turn Limits & Loop Halting
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 3: Bounded Turn Limits & Loop Halting ---" -ForegroundColor Yellow

# 3.1 Bounded Turns Enforcement (Infinite tools cutoff at MaxConsecutiveToolTurns = 4)
$boundMock = New-Object OpenAgent.Model.MockModelProvider
for ($i = 1; $i -le 6; $i++) {
    $dirArg = Join-Path $tempSandboxDir "sub_$i"
    $boundMock.EnqueueToolCall("list_directory", ('{"path":"' + $dirArg.Replace('\', '/') + '"}'), "call_$i")
}

$boundSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$boundAgent = New-Object OpenAgent.Agent.AgentController($boundMock, $registry, $permMgr, $boundSm, $auditRepo)
$boundAgent.MaxConsecutiveToolTurns = 4

$boundReply = $boundAgent.ProcessPromptAsync("Esegui un ciclo infinito di chiamate tool").GetAwaiter().GetResult()

Assert-Condition ($boundMock.CallCount -eq 4) "Turn loop stopped strictly at MaxConsecutiveToolTurns (4)"
Assert-Condition ($boundReply -match "limite massimo di passaggi") "Agent returned informative cutoff message regarding maximum turns"
Assert-Condition ($boundSm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "State machine transitioned cleanly to Idle on turn limit cutoff"

# 3.2 Persistent Duplicate Loop Interception
$loopMock = New-Object OpenAgent.Model.MockModelProvider
# Enqueue 3 identical tool calls
for ($i = 1; $i -le 3; $i++) {
    $loopMock.EnqueueToolCall("read_file", ('{"path":"' + $normFile + '"}'), "call_loop_$i")
}

$loopSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$loopAgent = New-Object OpenAgent.Agent.AgentController($loopMock, $registry, $permMgr, $loopSm, $auditRepo)

$loopReply = $loopAgent.ProcessPromptAsync("Loop ripetuto").GetAwaiter().GetResult()

Assert-Condition ($loopAgent.WasLoopDetected -eq $true) "Agent recorded WasLoopDetected = true upon repeated identical calls"
Assert-Condition ($loopReply -match "loop" -or $loopReply -match "interrotta") "Agent gracefully halted loop execution and informed user"
Assert-Condition ($loopSm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "State machine returned to Idle after loop interception"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 4: Cancellation & Timeout
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 4: Cancellation & Timeout ---" -ForegroundColor Yellow

# 4.1 In-flight cancellation via CancelCurrentExecution()
$cancelMock = New-Object OpenAgent.Model.MockModelProvider
$cancelMock.DelayPerCall = [TimeSpan]::FromSeconds(3)

$cancelSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$cancelAgent = New-Object OpenAgent.Agent.AgentController($cancelMock, $registry, $permMgr, $cancelSm, $auditRepo)

$promptTask = $cancelAgent.ProcessPromptAsync("Richiesta lenta da annullare")
Start-Sleep -Milliseconds 150
$cancelAgent.CancelCurrentExecution()

$cancelReply = $promptTask.GetAwaiter().GetResult()

Assert-Condition ($cancelAgent.IsCancelled -eq $true) "AgentController confirmed IsCancelled = true upon CancelCurrentExecution()"
Assert-Condition ($cancelReply -match "annullata") "ProcessPromptAsync returned cancellation notification"
Assert-Condition ($cancelSm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Agent reset to Idle after cancellation"

# 4.2 Timeout enforcement via ExecutionTimeout
$timeoutMock = New-Object OpenAgent.Model.MockModelProvider
$timeoutMock.DelayPerCall = [TimeSpan]::FromSeconds(5)

$timeoutSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$timeoutAgent = New-Object OpenAgent.Agent.AgentController($timeoutMock, $registry, $permMgr, $timeoutSm, $auditRepo)
$timeoutAgent.ExecutionTimeout = [TimeSpan]::FromMilliseconds(300)

$timeoutReply = $timeoutAgent.ProcessPromptAsync("Richiesta che supera il timeout").GetAwaiter().GetResult()
Assert-Condition ($timeoutAgent.IsCancelled -eq $true) "AgentController recorded IsCancelled = true upon timeout expiry"
Assert-Condition ($timeoutReply -match "annullata" -or $timeoutReply -match "timeout") "Agent reported cancellation/timeout notice"
Assert-Condition ($timeoutSm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Agent returned to Idle after timeout"

# Clean up sandbox
Remove-Item -Path $tempSandboxDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# TEST GROUP 5: Drag-and-Drop Path Suggestion Logic
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 5: Drag-and-Drop Path Suggestion Logic ---" -ForegroundColor Yellow

$testDndDir = Join-Path $workspaceRoot "tests\temp\temp_dnd_test"
if (Test-Path $testDndDir) { Remove-Item $testDndDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $testDndDir -Force | Out-Null
$testDndFile = Join-Path $testDndDir "document.pdf"
[System.IO.File]::WriteAllText($testDndFile, "Mock PDF Content")

# Helper function mirroring MainWindow Drag & Drop logic
function Get-SmartPrompt([string[]]$paths) {
    if ($paths.Length -eq 1) {
        $p = $paths[0]
        if ([System.IO.Directory]::Exists($p)) {
            return "Elenca i file nella cartella `"$p`" ed esamina la struttura."
        } else {
            return "Leggi ed esamina il file `"$p`"."
        }
    } else {
        return "Esamina i seguenti percorsi: `"$([string]::Join('", "', $paths))`"."
    }
}

$promptFolder = Get-SmartPrompt @($testDndDir)
Assert-Condition ($promptFolder -match "Elenca i file nella cartella") "Drag-and-Drop generated directory listing prompt for folder"

$promptFile = Get-SmartPrompt @($testDndFile)
Assert-Condition ($promptFile -match "Leggi ed esamina il file") "Drag-and-Drop generated file inspection prompt for single file"

$promptMulti = Get-SmartPrompt @($testDndDir, $testDndFile)
Assert-Condition ($promptMulti -match "Esamina i seguenti percorsi") "Drag-and-Drop generated multi-path prompt for multiple items"

Remove-Item -Path $testDndDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# TEST GROUP 6: Live Multi-Step Round-Trip with Spark-X 2.5
# ------------------------------------------------------------------------------------------------
if (-not $SkipLiveServer) {
    Write-Host "`n--- TEST GROUP 6: Live Multi-Step Round-Trip (Spark-X 2.5) ---" -ForegroundColor Yellow

    # Ensure no old llama-server is running
    Get-Process -Name "llama-server" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    $procConfig = New-Object OpenAgent.Model.ProcessManager.LlamaProcessConfig
    $procConfig.ExecutablePath = (Resolve-Path "runtime/llama/llama-server.exe").Path
    $procConfig.ModelPath = (Resolve-Path "models/Spark-X2.5-1.7B-Q4_K_M.gguf").Path
    $procConfig.Port = 8080
    $procConfig.ContextSize = 4096
    $procConfig.Threads = 6
    $procConfig.Device = "none"
    $procConfig.EnableJinja = $true
    $procConfig.LogFilePath = Join-Path $workspaceRoot "tests\temp\llama_server.log"
    $procConfig.StartupTimeoutSeconds = 45

    $processMgr = New-Object OpenAgent.Model.ProcessManager.LlamaProcessManager($procConfig)

    Write-Host "Starting llama-server..." -ForegroundColor Cyan
    $started = $processMgr.StartAsync().GetAwaiter().GetResult()
    Assert-Condition ($started -eq $true -and $processMgr.IsReady -eq $true) "LlamaProcessManager started server successfully"

    try {
        $liveClient = New-Object OpenAgent.Model.LlamaCppClient("http://127.0.0.1:8080")
        $liveHealth = $liveClient.HealthCheckAsync().GetAwaiter().GetResult()
        Assert-Condition ($liveHealth -eq $true) "Health check confirmed server ready"

        # Create real test directory with 2 files for chained task
        $liveTestDir = Join-Path $workspaceRoot "tests\temp\temp_sprint4_live"
        if (Test-Path $liveTestDir) { Remove-Item -Path $liveTestDir -Recurse -Force -ErrorAction SilentlyContinue }
        New-Item -ItemType Directory -Path $liveTestDir -Force | Out-Null

        $notesFile = Join-Path $liveTestDir "todo.txt"
        [System.IO.File]::WriteAllText($notesFile, "1. Validare Sprint 4. 2. Festeggiare completamento.")

        $liveRegistry = New-Object OpenAgent.Tools.ToolRegistry
        $liveRegistry.Register((New-Object OpenAgent.Tools.FileSystem.ListDirectoryTool($permMgr)))
        $liveRegistry.Register((New-Object OpenAgent.Tools.FileSystem.ReadFileTool($permMgr)))

        $liveAuditDb = Join-Path $liveTestDir "live_audit.db"
        $liveAuditRepo = New-Object OpenAgent.Data.SqliteAuditRepository($liveAuditDb)
        $liveAuditRepo.InitializeAsync().GetAwaiter().GetResult()

        $liveSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
        $liveAgent = New-Object OpenAgent.Agent.AgentController($liveClient, $liveRegistry, $permMgr, $liveSm, $liveAuditRepo)

        $normalizedLiveDir = $liveTestDir.Replace("\", "/")
        $livePrompt = "Elenca i file nella cartella '$normalizedLiveDir' e poi leggi il file todo.txt per dirmi cosa c'è scritto."
        Write-Host "Sending prompt: '$livePrompt'" -ForegroundColor Cyan

        $liveReply = $liveAgent.ProcessPromptAsync($livePrompt).GetAwaiter().GetResult()

        Write-Host "`nAgent Final Reply:" -ForegroundColor Green
        Write-Host $liveReply -ForegroundColor White

        $toolMsgs = @($liveAgent.History | Where-Object { $_.Role -eq "tool" })
        $liveExecutedTools = @($toolMsgs | ForEach-Object { $_.Name })

        Assert-Condition ($liveExecutedTools.Count -ge 2) "Agent invoked at least 2 consecutive tools in a multi-step chain"
        Assert-Condition ($liveExecutedTools -contains "list_directory") "Chain contained 'list_directory'"
        Assert-Condition ($liveExecutedTools -contains "read_file") "Chain contained 'read_file'"
        Assert-Condition ($liveReply -match "Sprint 4" -or $liveReply -match "Validare" -or $liveReply -match "Festeggiare") "Final answer accurately synthesized contents from chained read_file"
        Assert-Condition ($liveSm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Agent returned to Idle state"

        # Verify SQLite Audit
        $liveLogs = $liveAuditRepo.GetRecentLogsAsync(10).GetAwaiter().GetResult()
        Assert-Condition ($liveLogs.Count -ge 2) "SQLite Audit recorded all tools in the live multi-step chain"
        Assert-Condition ($liveLogs[0].SessionId -eq $liveLogs[1].SessionId) "All live steps share identical SessionId in SQLite audit"

        # Cleanup
        Remove-Item -Path $liveTestDir -Recurse -Force -ErrorAction SilentlyContinue

    } finally {
        Write-Host "`nStopping llama-server..." -ForegroundColor Cyan
        $processMgr.StopAsync().GetAwaiter().GetResult()
        $processMgr.Dispose()
        $liveClient.Dispose()
        Write-Host "llama-server stopped cleanly." -ForegroundColor Green
    }
}

# ------------------------------------------------------------------------------------------------
# SUMMARY
# ------------------------------------------------------------------------------------------------
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "SPRINT 4 RESULTS: Passed: $passCount, Failed: $failCount" -ForegroundColor ($failCount -eq 0 ? "Green" : "Red")
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
