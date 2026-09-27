# Test Suite for OpenAgent Sprint 2: Model, State Machine & Sandboxed File Tools

param(
    [switch]$SkipLiveServer = $false
)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "         OPENAGENT SPRINT 2 VALIDATION SUITE            " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

# Load assemblies
$modelDll = Resolve-Path "src/OpenAgent.Model/bin/Debug/net8.0/OpenAgent.Model.dll"
$toolsDll = Resolve-Path "src/OpenAgent.Tools/bin/Debug/net8.0/OpenAgent.Tools.dll"
$agentDll = Resolve-Path "src/OpenAgent.Agent/bin/Debug/net8.0/OpenAgent.Agent.dll"

Add-Type -Path $modelDll
Add-Type -Path $toolsDll
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
# TEST GROUP 1: PermissionManager & Sandbox Isolation
# ------------------------------------------------------------------------------------------------
Write-Host "--- TEST GROUP 1: PermissionManager & Sandbox Isolation ---" -ForegroundColor Yellow

$permMgr = New-Object OpenAgent.Agent.Security.PermissionManager($workspaceRoot)

# 1.1 Allowed default locations
$docs = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::MyDocuments)
$resDocs = $permMgr.CheckPathAccess($docs, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resDocs.IsAllowed -eq $true) "Documents folder is allowed"

$resWs = $permMgr.CheckPathAccess($workspaceRoot, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resWs.IsAllowed -eq $true) "Workspace root is allowed"

$resAlias = $permMgr.CheckPathAccess("workspace", [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resAlias.IsAllowed -eq $true -and $resAlias.CanonicalPath -eq $workspaceRoot) "Alias 'workspace' resolves to workspace root"

# 1.2 System Folders Blocklist
$windowsFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Windows)
$resWin = $permMgr.CheckPathAccess($windowsFolder, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resWin.IsAllowed -eq $false) "Windows folder is blocked"

$sys32 = Join-Path $windowsFolder "System32"
$resSys32 = $permMgr.CheckPathAccess($sys32, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resSys32.IsAllowed -eq $false) "System32 folder is blocked"

$progFiles = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ProgramFiles)
$resProg = $permMgr.CheckPathAccess($progFiles, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resProg.IsAllowed -eq $false) "Program Files folder is blocked"

# 1.3 Direct Drive Root access
$resRoot = $permMgr.CheckPathAccess("C:\", [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resRoot.IsAllowed -eq $false) "Direct drive root access (C:\) is blocked"

# 1.4 Path Traversal attack prevention
$traversalAttack = Join-Path $workspaceRoot "..\..\..\..\Windows\System32"
$resTraversal = $permMgr.CheckPathAccess($traversalAttack, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resTraversal.IsAllowed -eq $false) "Directory traversal attack to Windows is blocked"

# 1.5 Sensitive User directories (.ssh, AppData)
$userProfile = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile)
$sshFolder = Join-Path $userProfile ".ssh"
$resSsh = $permMgr.CheckPathAccess($sshFolder, [OpenAgent.Tools.Security.PathAccessType]::Read)
Assert-Condition ($resSsh.IsAllowed -eq $false) "User .ssh credential directory is blocked"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 2: Tool Registry & File Tools
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 2: Tool Registry & File Tools ---" -ForegroundColor Yellow

$registry = New-Object OpenAgent.Tools.ToolRegistry
$listTool = New-Object OpenAgent.Tools.FileSystem.ListDirectoryTool($permMgr)
$readTool = New-Object OpenAgent.Tools.FileSystem.ReadFileTool($permMgr)
$moveTool = New-Object OpenAgent.Tools.FileSystem.MoveOrRenameTool($permMgr)

$registry.Register($listTool)
$registry.Register($readTool)
$registry.Register($moveTool)

$defs = $registry.GetToolDefinitions()
Assert-Condition ($defs.Count -eq 3) "ToolRegistry has 3 registered tool definitions"
Assert-Condition ($registry.GetTool("list_directory") -ne $null) "Tool list_directory retrieved successfully"
Assert-Condition ($registry.GetTool("read_file") -ne $null) "Tool read_file retrieved successfully"
Assert-Condition ($registry.GetTool("move_or_rename") -ne $null) "Tool move_or_rename retrieved successfully"

# 2.1 Test list_directory on workspace
$listResult = $registry.ExecuteToolCallAsync("list_directory", "{`"path`": `"workspace`"}").GetAwaiter().GetResult()
Assert-Condition ($listResult.Success -eq $true -and $listResult.Content -match "OpenAgent.sln") "list_directory executed on workspace and found OpenAgent.sln"

# 2.2 Test list_directory on blocked path
$listBlocked = $registry.ExecuteToolCallAsync("list_directory", "{`"path`": `"C:\\Windows\\System32`"}").GetAwaiter().GetResult()
Assert-Condition ($listBlocked.Success -eq $false -and $listBlocked.Content -match "Accesso negato") "list_directory on C:\Windows\System32 rejected with Accesso negato"

# 2.3 Test read_file with 8192 bytes hard cap
$tempDir = Join-Path $workspaceRoot "tests\temp\temp_test_sandbox"
if (-not (Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir -Force | Out-Null }

$testLargeFilePath = Join-Path $tempDir "large_file.txt"
# Create a 16KB text file
$dummyText = "0123456789ABCDEF" * 1024 # 16384 bytes
[System.IO.File]::WriteAllText($testLargeFilePath, $dummyText)

$escapedLargeFile = $testLargeFilePath.Replace("\", "\\")
$readResult = $registry.ExecuteToolCallAsync("read_file", "{`"path`": `"$escapedLargeFile`", `"max_bytes`": 20000}").GetAwaiter().GetResult()

Assert-Condition ($readResult.Success -eq $true) "read_file succeeded on test file"
Assert-Condition ($readResult.Content -match "Troncato" -and $readResult.Content.Length -ge 8192) "read_file strictly enforced 8192 bytes hard cap"

# 2.4 Test read_file binary file detection
$binaryFilePath = Join-Path $tempDir "sample.bin"
$binBytes = [byte[]]@(0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00)
[System.IO.File]::WriteAllBytes($binaryFilePath, $binBytes)
$escapedBin = $binaryFilePath.Replace("\", "\\")
$binResult = $registry.ExecuteToolCallAsync("read_file", "{`"path`": `"$escapedBin`"}").GetAwaiter().GetResult()
Assert-Condition ($binResult.Success -eq $false -and $binResult.Content -match "binario") "read_file safely rejected binary file"

# 2.5 Test move_or_rename
$sourceFile = Join-Path $tempDir "file_to_rename.txt"
$targetFile = Join-Path $tempDir "renamed_file.txt"
[System.IO.File]::WriteAllText($sourceFile, "Hello OpenAgent")

$escapedSrc = $sourceFile.Replace("\", "\\")
$escapedDest = $targetFile.Replace("\", "\\")

$moveResult = $registry.ExecuteToolCallAsync("move_or_rename", "{`"src`": `"$escapedSrc`", `"dest`": `"$escapedDest`"}").GetAwaiter().GetResult()
Assert-Condition ($moveResult.Success -eq $true) "move_or_rename executed successfully"
Assert-Condition ((-not (Test-Path $sourceFile)) -and (Test-Path $targetFile)) "Source was moved and destination now exists"

# 2.6 Test move_or_rename to blocked path
$destBlocked = "C:\\Windows\\System32\\hacked.txt"
$moveBlocked = $registry.ExecuteToolCallAsync("move_or_rename", "{`"src`": `"$escapedDest`", `"dest`": `"$destBlocked`"}").GetAwaiter().GetResult()
Assert-Condition ($moveBlocked.Success -eq $false -and $moveBlocked.Content -match "Accesso negato") "move_or_rename to C:\Windows rejected with Accesso negato"

# Clean up temp test directory
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue

# ------------------------------------------------------------------------------------------------
# TEST GROUP 3: Agent State Machine
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 3: Agent State Machine ---" -ForegroundColor Yellow

$sm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$stateHistory = [System.Collections.Generic.List[string]]::new()
$sm.add_StateChanged({
    param($oldState, $newState, $detail)
    $stateHistory.Add("$oldState -> $newState ($detail)")
})

Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Initial state is Idle"

$sm.TransitionTo([OpenAgent.Agent.StateMachine.AgentState]::Thinking, "Elaborazione")
Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Thinking) "Transitioned to Thinking"

$sm.TransitionTo([OpenAgent.Agent.StateMachine.AgentState]::ToolActive, "Esecuzione list_directory")
Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::ToolActive) "Transitioned to ToolActive"

$sm.TransitionTo([OpenAgent.Agent.StateMachine.AgentState]::Speaking, "Risposta pronta")
Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Speaking) "Transitioned to Speaking"

$sm.TransitionTo([OpenAgent.Agent.StateMachine.AgentState]::Idle, "In ascolto")
Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Transitioned back to Idle"

Assert-Condition ($stateHistory.Count -eq 4) "StateChanged event fired 4 times"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 4: Full End-to-End Agent Round-Trip with Live llama-server
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
        $modelClient = New-Object OpenAgent.Model.LlamaCppClient("http://127.0.0.1:8080")
        $health = $modelClient.HealthCheckAsync().GetAwaiter().GetResult()
        Assert-Condition ($health -eq $true) "LlamaCppClient verified server health endpoint"

        $agentSm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
        $agent = New-Object OpenAgent.Agent.AgentController($modelClient, $registry, $permMgr, $agentSm)

        Write-Host "Sending prompt to Agent: 'Elenca i file presenti nella cartella workspace.'" -ForegroundColor Cyan
        $reply = $agent.ProcessPromptAsync("Elenca i file presenti nella cartella workspace.").GetAwaiter().GetResult()

        Write-Host "`nFinal Agent Response:" -ForegroundColor Green
        Write-Host $reply -ForegroundColor White

        $toolMsgs = $agent.History | Where-Object { $_.Role -eq "tool" }
        $hasListTool = $toolMsgs | Where-Object { $_.Name -eq "list_directory" }

        Assert-Condition ($hasListTool -ne $null) "Agent invoked list_directory tool during prompt execution"
        Assert-Condition ($reply.Length -gt 15) "Agent returned informative synthesized response"
        Assert-Condition ($agentSm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Agent returned to Idle state after completion"
    } finally {
        Write-Host "`nStopping llama-server..." -ForegroundColor Cyan
        $processMgr.StopAsync().GetAwaiter().GetResult()
        $processMgr.Dispose()
        $modelClient.Dispose()
        Write-Host "llama-server stopped cleanly." -ForegroundColor Green
    }
}

# Cleanup temp files
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------------------------------------
# SUMMARY
# ------------------------------------------------------------------------------------------------
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "TEST RESULTS: Passed: $passCount, Failed: $failCount" -ForegroundColor ($failCount -eq 0 ? "Green" : "Red")
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
