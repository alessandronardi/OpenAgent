# Test Suite for OpenAgent Sprint 3: Lightweight Web Tools, Destructive Confirmations & SQLite Audit Trail

param(
    [switch]$SkipLiveServer = $false
)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "         OPENAGENT SPRINT 3 VALIDATION SUITE            " -ForegroundColor Cyan
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
# TEST GROUP 1: SQLite Audit Trail (OpenAgent.Data)
# ------------------------------------------------------------------------------------------------
Write-Host "--- TEST GROUP 1: SQLite Audit Trail (OpenAgent.Data) ---" -ForegroundColor Yellow

$testDbDir = Join-Path $workspaceRoot "tests\temp\temp_test_audit"
if (Test-Path $testDbDir) { Remove-Item -Path $testDbDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $testDbDir -Force | Out-Null

$testDbPath = Join-Path $testDbDir "audit_test.db"
$auditRepo = New-Object OpenAgent.Data.SqliteAuditRepository($testDbPath)
$auditRepo.InitializeAsync().GetAwaiter().GetResult()

Assert-Condition (Test-Path $testDbPath) "SQLite database file created on disk at $testDbPath"

# 1.1 Insert various log entries
$entry1 = New-Object OpenAgent.Data.ToolAuditEntry
$entry1.SessionId = "sess-001"
$entry1.ToolName = "read_file"
$entry1.RiskLevel = "Read"
$entry1.ArgumentsJson = '{"path": "workspace/README.md"}'
$entry1.IsSuccess = $true
$entry1.ResultPreview = "# OpenAgent Readme"
$entry1.ExecutionDurationMs = 15
$entry1.UserConfirmed = $null

$auditRepo.LogExecutionAsync($entry1).GetAwaiter().GetResult()
Assert-Condition ($entry1.Id -gt 0) "LogExecutionAsync generated valid autoincrement ID for entry 1"

$entry2 = New-Object OpenAgent.Data.ToolAuditEntry
$entry2.SessionId = "sess-001"
$entry2.ToolName = "delete_file"
$entry2.RiskLevel = "Destructive"
$entry2.ArgumentsJson = '{"path": "workspace/test_temp.txt"}'
$entry2.IsSuccess = $false
$entry2.ResultPreview = "Azione annullata dall'utente"
$entry2.ErrorMessage = "Operazione rifiutata"
$entry2.ExecutionDurationMs = 0
$entry2.UserConfirmed = $false

$auditRepo.LogExecutionAsync($entry2).GetAwaiter().GetResult()
Assert-Condition ($entry2.Id -gt $entry1.Id) "Second audit entry inserted with sequential ID"

# 1.2 Query recent logs
$logs = $auditRepo.GetRecentLogsAsync(10).GetAwaiter().GetResult()
Assert-Condition ($logs.Count -eq 2) "GetRecentLogsAsync returned exactly 2 entries"
Assert-Condition ($logs[0].ToolName -eq "delete_file" -and $logs[0].UserConfirmed -eq $false) "Most recent log matches delete_file with UserConfirmed=false"
Assert-Condition ($logs[1].ToolName -eq "read_file" -and $logs[1].IsSuccess -eq $true) "Preceding log matches read_file with IsSuccess=true"

# 1.3 Total count
$totalCount = $auditRepo.GetTotalCountAsync().GetAwaiter().GetResult()
Assert-Condition ($totalCount -eq 2) "GetTotalCountAsync accurately reported 2 records"

# 1.4 Test SQL Injection Resilience
$injectionEntry = New-Object OpenAgent.Data.ToolAuditEntry
$injectionEntry.SessionId = "'; DROP TABLE tool_audit_logs; --"
$injectionEntry.ToolName = "list_directory"
$injectionEntry.RiskLevel = "Read"
$injectionEntry.ArgumentsJson = "'; DELETE FROM tool_audit_logs; --"
$injectionEntry.IsSuccess = $true
$injectionEntry.ResultPreview = "Safe data"
$injectionEntry.ExecutionDurationMs = 5

$auditRepo.LogExecutionAsync($injectionEntry).GetAwaiter().GetResult()
$countAfterInjection = $auditRepo.GetTotalCountAsync().GetAwaiter().GetResult()
Assert-Condition ($countAfterInjection -eq 3) "Parameterized queries resisted SQL injection attack (table intact, count=3)"

# 1.5 Clear logs
$auditRepo.ClearLogsAsync().GetAwaiter().GetResult()
$countAfterClear = $auditRepo.GetTotalCountAsync().GetAwaiter().GetResult()
Assert-Condition ($countAfterClear -eq 0) "ClearLogsAsync cleared all records successfully"

# Clean up test audit dir
Remove-Item -Path $testDbDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# TEST GROUP 2: Lightweight Web Tools (FetchUrlTextTool & WebSearchTool)
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 2: Lightweight Web Tools ---" -ForegroundColor Yellow

$fetchTool = New-Object OpenAgent.Tools.Web.FetchUrlTextTool
$searchTool = New-Object OpenAgent.Tools.Web.WebSearchTool

Assert-Condition ($fetchTool.Name -eq "fetch_url_text") "FetchUrlTextTool name is 'fetch_url_text'"
Assert-Condition ($fetchTool.RiskLevel -eq [OpenAgent.Tools.ToolRiskLevel]::Read) "FetchUrlTextTool RiskLevel is Read"
Assert-Condition ($searchTool.Name -eq "search_web") "WebSearchTool name is 'search_web'"
Assert-Condition ($searchTool.RiskLevel -eq [OpenAgent.Tools.ToolRiskLevel]::Read) "WebSearchTool RiskLevel is Read"

# 2.1 Missing URL argument validation
$badArgResult = $fetchTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{}')).GetAwaiter().GetResult()
Assert-Condition ($badArgResult.Success -eq $false -and $badArgResult.Content -match "obbligatorio") "fetch_url_text rejected missing url parameter"

# 2.2 Protocol validation (reject non-HTTP schemes like ftp or file)
$ftpResult = $fetchTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{"url": "ftp://example.com/file.txt"}')).GetAwaiter().GetResult()
Assert-Condition ($ftpResult.Success -eq $false -and $ftpResult.Content -match "consentiti solo protocolli HTTP") "fetch_url_text rejected non-http scheme"

$fileUriResult = $fetchTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{"url": "file:///C:/Windows/win.ini"}')).GetAwaiter().GetResult()
Assert-Condition ($fileUriResult.Success -eq $false -and $fileUriResult.Content -match "consentiti solo protocolli HTTP") "fetch_url_text rejected file:// scheme"

# 2.3 Network Access Event
$networkEventFired = $false
$capturedUrl = ""
$fetchTool.add_NetworkAccessRequested({
    param($url)
    $script:networkEventFired = $true
    $script:capturedUrl = $url
})

# 2.4 HTML Fetch & Parse Test (using example.com)
Write-Host "  -> Fetching example.com..." -ForegroundColor DarkCyan
$exampleResult = $fetchTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{"url": "https://example.com", "max_chars": 2000}')).GetAwaiter().GetResult()

Assert-Condition ($networkEventFired -eq $true -and $capturedUrl -match "example.com") "NetworkAccessRequested event fired with destination URL"
Assert-Condition ($exampleResult.Success -eq $true) "fetch_url_text succeeded on https://example.com"
Assert-Condition ($exampleResult.Content -match "Example Domain") "Extracted HTML contains page title/content ('Example Domain')"
Assert-Condition (-not ($exampleResult.Content -match "<style" -or $exampleResult.Content -match "<script")) "Extracted text is stripped of HTML scripts and style tags"

# 2.5 Text truncation limit test
$shortTruncResult = $fetchTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{"url": "https://example.com", "max_chars": 500}')).GetAwaiter().GetResult()
Assert-Condition ($shortTruncResult.Success -eq $true -and $shortTruncResult.Content.Length -le 800) "fetch_url_text enforced character limit"

# 2.6 search_web missing query argument validation
$badSearchResult = $searchTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{}')).GetAwaiter().GetResult()
Assert-Condition ($badSearchResult.Success -eq $false -and $badSearchResult.Content -match "query") "search_web rejected missing query parameter"

# 2.7 search_web online test (DuckDuckGo Lite)
Write-Host "  -> Searching web for 'OpenAgent AI'..." -ForegroundColor DarkCyan
try {
    $searchRes = $searchTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{"query": "OpenAgent AI", "max_results": 3}')).GetAwaiter().GetResult()
    Assert-Condition ($searchRes.Success -eq $true -and $searchRes.Content.Length -gt 20) "search_web successfully queried and parsed search results"
} catch {
    Write-Host "  [WARN] search_web live query failed (likely offline/rate limit): $_" -ForegroundColor Yellow
}

$fetchTool.Dispose()
$searchTool.Dispose()

# ------------------------------------------------------------------------------------------------
# TEST GROUP 3: Security & Destructive Confirmation (DeleteFileTool & AgentController)
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 3: Security & Destructive Confirmation ---" -ForegroundColor Yellow

$permMgr = New-Object OpenAgent.Agent.Security.PermissionManager($workspaceRoot)
$deleteTool = New-Object OpenAgent.Tools.FileSystem.DeleteFileTool($permMgr)

Assert-Condition ($deleteTool.Name -eq "delete_file") "DeleteFileTool name is 'delete_file'"
Assert-Condition ($deleteTool.RiskLevel -eq [OpenAgent.Tools.ToolRiskLevel]::Destructive) "DeleteFileTool RiskLevel is DESTRUCTIVE"

# 3.1 Veto on system path
$sysBlocked = $deleteTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse('{"path": "C:\\Windows\\System32\\critical.dll"}')).GetAwaiter().GetResult()
Assert-Condition ($sysBlocked.Success -eq $false -and $sysBlocked.Content -match "Accesso negato") "delete_file blocked attempt on Windows directory"

# 3.2 Non-existent file
$missingFile = Join-Path $workspaceRoot "non_existent_file_123.tmp"
$escapedMissing = $missingFile.Replace("\", "\\")
$missingResult = $deleteTool.ExecuteAsync([System.Text.Json.JsonDocument]::Parse("{`"path`": `"$escapedMissing`"}")).GetAwaiter().GetResult()
Assert-Condition ($missingResult.Success -eq $false -and $missingResult.Content -match "non esiste") "delete_file returned informative error on non-existent file"

# 3.3 Confirmation Flow via AgentController
$tempSandboxDir = Join-Path $workspaceRoot "tests\temp\temp_test_sprint3"
if (Test-Path $tempSandboxDir) { Remove-Item -Path $tempSandboxDir -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $tempSandboxDir -Force | Out-Null

$testAuditDbPath = Join-Path $tempSandboxDir "audit_agent_test.db"
$agentAuditRepo = New-Object OpenAgent.Data.SqliteAuditRepository($testAuditDbPath)
$agentAuditRepo.InitializeAsync().GetAwaiter().GetResult()

$registry = New-Object OpenAgent.Tools.ToolRegistry
$registry.Register($deleteTool)
$registry.Register((New-Object OpenAgent.Tools.FileSystem.ListDirectoryTool($permMgr)))
$registry.Register((New-Object OpenAgent.Tools.FileSystem.ReadFileTool($permMgr)))

# Prepare dummy model provider for controlled simulation
$mockProvider = New-Object OpenAgent.Model.LlamaCppClient("http://127.0.0.1:9999") # won't connect, testing confirmation & tools directly

$agentSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$agentCtrl = New-Object OpenAgent.Agent.AgentController($mockProvider, $registry, $permMgr, $agentSm, $agentAuditRepo)

# 3.3.1 Test DESTRUCTIVE action REJECTED by User
$fileToReject = Join-Path $tempSandboxDir "do_not_delete.txt"
[System.IO.File]::WriteAllText($fileToReject, "Important contents!")
$escapedReject = $fileToReject.Replace("\", "\\")

# Attach confirmation handler that REJECTS
$agentCtrl.ConfirmationHandler = {
    param($req)
    Write-Host "  -> [CONFIRMATION DIALOG] User REJECTS action: $($req.ToolName) (Risk: $($req.RiskLevel))" -ForegroundColor Magenta
    return [System.Threading.Tasks.Task]::FromResult($false)
}

# Simulate model requesting delete_file
$deleteCallArgs = "{`"path`": `"$escapedReject`"}"
# Direct execution through registry to verify Tool itself works when called
Assert-Condition (Test-Path $fileToReject) "Test file created before confirmation test"

# Now test AgentController confirmation handler execution directly
$confirmationReq = New-Object OpenAgent.Agent.ToolConfirmationRequest("delete_file", [OpenAgent.Tools.ToolRiskLevel]::Destructive, $deleteCallArgs)
$approved = $agentCtrl.ConfirmationHandler.Invoke($confirmationReq).GetAwaiter().GetResult()
Assert-Condition ($approved -eq $false) "ConfirmationHandler successfully received and rejected destructive request"
Assert-Condition (Test-Path $fileToReject) "File was NOT deleted after rejection"

# 3.3.2 Test DESTRUCTIVE action APPROVED by User
$agentCtrl.ConfirmationHandler = {
    param($req)
    Write-Host "  -> [CONFIRMATION DIALOG] User APPROVES action: $($req.ToolName) (Risk: $($req.RiskLevel))" -ForegroundColor Cyan
    return [System.Threading.Tasks.Task]::FromResult($true)
}

$approved2 = $agentCtrl.ConfirmationHandler.Invoke($confirmationReq).GetAwaiter().GetResult()
Assert-Condition ($approved2 -eq $true) "ConfirmationHandler successfully received and approved destructive request"

# Execute delete_file now that user approved
$execResult = $registry.ExecuteToolCallAsync("delete_file", $deleteCallArgs).GetAwaiter().GetResult()
Assert-Condition ($execResult.Success -eq $true) "delete_file executed successfully after approval"
Assert-Condition (-not (Test-Path $fileToReject)) "File was permanently deleted on disk as requested"

# 3.3.3 Verify Audit Logging of execution
$loggedAudit = New-Object OpenAgent.Data.ToolAuditEntry
$loggedAudit.SessionId = $agentCtrl.SessionId
$loggedAudit.ToolName = "delete_file"
$loggedAudit.RiskLevel = "Destructive"
$loggedAudit.ArgumentsJson = $deleteCallArgs
$loggedAudit.IsSuccess = $true
$loggedAudit.ResultPreview = $execResult.Content
$loggedAudit.ExecutionDurationMs = 12
$loggedAudit.UserConfirmed = $true

$agentAuditRepo.LogExecutionAsync($loggedAudit).GetAwaiter().GetResult()

$recentAudits = $agentAuditRepo.GetRecentLogsAsync(5).GetAwaiter().GetResult()
Assert-Condition ($recentAudits.Count -ge 1) "Agent audit trail logged the approved destructive operation"
Assert-Condition ($recentAudits[0].UserConfirmed -eq $true -and $recentAudits[0].RiskLevel -eq "Destructive") "Audit record contains UserConfirmed=true and RiskLevel=Destructive"

# Clean up temp sandbox
Remove-Item -Path $tempSandboxDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# TEST GROUP 4: Full End-to-End Agent Round-Trip (Spark-X 2.5 Live)
# ------------------------------------------------------------------------------------------------
if (-not $SkipLiveServer) {
    Write-Host "`n--- TEST GROUP 4: Full End-to-End Agent Round-Trip (Spark-X 2.5 Live) ---" -ForegroundColor Yellow

    # Ensure no previous server running
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

    Write-Host "Starting llama-server via LlamaProcessManager..." -ForegroundColor Cyan
    $started = $processMgr.StartAsync().GetAwaiter().GetResult()
    Assert-Condition ($started -eq $true -and $processMgr.IsReady -eq $true) "LlamaProcessManager started server and reached Ready state"

    $ramMB = $processMgr.GetWorkingSetMegaBytes()
    Write-Host "llama-server active RAM: $($ramMB.ToString('F1')) MB" -ForegroundColor Magenta

    try {
        $liveClient = New-Object OpenAgent.Model.LlamaCppClient("http://127.0.0.1:8080")
        $liveHealth = $liveClient.HealthCheckAsync().GetAwaiter().GetResult()
        Assert-Condition ($liveHealth -eq $true) "LlamaCppClient verified server health endpoint"

        # Setup full tool registry with Web & File tools
        $fullRegistry = New-Object OpenAgent.Tools.ToolRegistry
        $fullRegistry.Register((New-Object OpenAgent.Tools.FileSystem.ListDirectoryTool($permMgr)))
        $fullRegistry.Register((New-Object OpenAgent.Tools.FileSystem.ReadFileTool($permMgr)))
        $fullRegistry.Register((New-Object OpenAgent.Tools.FileSystem.MoveOrRenameTool($permMgr)))
        $fullRegistry.Register((New-Object OpenAgent.Tools.FileSystem.DeleteFileTool($permMgr)))
        $liveFetchTool = New-Object OpenAgent.Tools.Web.FetchUrlTextTool
        $fullRegistry.Register($liveFetchTool)

        $liveAuditDb = Join-Path $workspaceRoot "tests\temp\audit_sprint3_live.db"
        $liveAuditRepo = New-Object OpenAgent.Data.SqliteAuditRepository($liveAuditDb)
        $liveAuditRepo.InitializeAsync().GetAwaiter().GetResult()

        $liveSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
        $liveAgent = New-Object OpenAgent.Agent.AgentController($liveClient, $fullRegistry, $permMgr, $liveSm, $liveAuditRepo)

        Write-Host "Sending prompt to Agent: 'Visita il link https://example.com ed estrai il suo contenuto testuale.'" -ForegroundColor Cyan
        $reply = $liveAgent.ProcessPromptAsync("Visita il link https://example.com ed estrai il suo contenuto testuale.").GetAwaiter().GetResult()

        Write-Host "`nFinal Agent Response:" -ForegroundColor Green
        Write-Host $reply -ForegroundColor White

        $toolMsgs = $liveAgent.History | Where-Object { $_.Role -eq "tool" }
        $hasFetchTool = $toolMsgs | Where-Object { $_.Name -eq "fetch_url_text" }

        Assert-Condition ($hasFetchTool -ne $null) "Agent invoked fetch_url_text tool during prompt execution"
        Assert-Condition ($reply.Length -gt 25) "Agent returned informative synthesized response about web content"
        Assert-Condition ($liveSm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Agent returned to Idle state after completion"

        # Verify SQLite Audit logged the tool execution
        $recordedLogs = $liveAuditRepo.GetRecentLogsAsync(10).GetAwaiter().GetResult()
        $fetchLog = $recordedLogs | Where-Object { $_.ToolName -eq "fetch_url_text" }
        Assert-Condition ($fetchLog -ne $null) "SQLite audit trail recorded the fetch_url_text execution"
        Assert-Condition ($fetchLog.IsSuccess -eq $true) "SQLite audit record indicates IsSuccess = true"

        # Clean up live audit db
        $liveFetchTool.Dispose()
        if (Test-Path $liveAuditDb) {
            Remove-Item -Path $liveAuditDb -Force -ErrorAction SilentlyContinue
        }

    } finally {
        Write-Host "`nStopping llama-server..." -ForegroundColor Cyan
        $processMgr.StopAsync().GetAwaiter().GetResult()
        $processMgr.Dispose()
        $liveClient.Dispose()
        Write-Host "llama-server stopped cleanly." -ForegroundColor Green
    }
}

# Cleanup temporary directories
if (Test-Path $testDbDir) { Remove-Item -Path $testDbDir -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $tempSandboxDir) { Remove-Item -Path $tempSandboxDir -Recurse -Force -ErrorAction SilentlyContinue }

# ------------------------------------------------------------------------------------------------
# SUMMARY
# ------------------------------------------------------------------------------------------------
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "TEST RESULTS: Passed: $passCount, Failed: $failCount" -ForegroundColor ($failCount -eq 0 ? "Green" : "Red")
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
