# Test Suite for OpenAgent: Hybrid Cloud Integration (Google Gemini 3.8 Flash + 3.5 Flash-Lite Fallback)

param(
    [switch]$SkipLiveServer = $true
)

$ErrorActionPreference = "Stop"
$env:Path = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:Path"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "     OPENAGENT GEMINI CLOUD INTEGRATION TEST SUITE      " -ForegroundColor Cyan
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
# TEST GROUP 1: AppSettings & Presets for Gemini 3.8 Flash
# ------------------------------------------------------------------------------------------------
Write-Host "--- TEST GROUP 1: AppSettings & Model Presets ---" -ForegroundColor Yellow

$settings = New-Object OpenAgent.Data.AppSettings
Assert-Condition ($settings.CloudModelName -eq "gemini-3.8-flash") "Default CloudModelName is gemini-3.8-flash"
Assert-Condition ($settings.CloudFallbackModelName -eq "gemini-3.5-flash-lite") "Default CloudFallbackModelName is gemini-3.5-flash-lite"
Assert-Condition ($settings.EnableCloudEscalation -eq $true) "Default EnableCloudEscalation is true"
Assert-Condition ($settings.AutoCloudOnComplexity -eq $true) "Default AutoCloudOnComplexity is true"

$preset38 = [OpenAgent.Data.AppSettings]::GetModelPreset([OpenAgent.Data.AppSettings]::ProfileGemini38Flash)
Assert-Condition ($preset38.ProfileName -eq "Google Gemini 3.8 Flash (Cloud)") "Preset ProfileName matches ProfileGemini38Flash"
Assert-Condition ($preset38.ContextSize -eq 1048576) "Gemini 3.8 Flash context window is 1,048,576 tokens"
Assert-Condition ($preset38.ModelPath -eq "cloud:gemini-3.8-flash") "Preset ModelPath indicates cloud:gemini-3.8-flash"

$preset35 = [OpenAgent.Data.AppSettings]::GetModelPreset([OpenAgent.Data.AppSettings]::ProfileGemini35FlashLite)
Assert-Condition ($preset35.ContextSize -eq 1048576) "Gemini 3.5 Flash-Lite context window is 1,048,576 tokens"

# Clone check
$settings.GeminiApiKey = "test-api-key-123"
$cloned = $settings.Clone()
Assert-Condition ($cloned.GeminiApiKey -eq "test-api-key-123") "AppSettings.Clone() correctly duplicates GeminiApiKey"
Assert-Condition ($cloned.CloudModelName -eq "gemini-3.8-flash") "AppSettings.Clone() duplicates CloudModelName"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 2: ModelTaskRouter Complexity Detection
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 2: ModelTaskRouter Complexity Detection ---" -ForegroundColor Yellow

# 2.1 Standard local prompts should NOT trigger cloud
$sugFile = [OpenAgent.Agent.ModelTaskRouter]::AnalyzePrompt("mostra i file sul desktop", [OpenAgent.Data.AppSettings]::ProfileCoder15B)
Assert-Condition ($sugFile.Domain -eq [OpenAgent.Agent.TaskDomain]::DesktopToolCalling) "Desktop file prompt routed to DesktopToolCalling"
Assert-Condition ($sugFile.IsCloud -eq $false) "Desktop file prompt IsCloud is false"

$sugWeb = [OpenAgent.Agent.ModelTaskRouter]::AnalyzePrompt("visita https://github.com", [OpenAgent.Data.AppSettings]::ProfileQwen3B)
Assert-Condition ($sugWeb.Domain -eq [OpenAgent.Agent.TaskDomain]::WebResearch) "URL navigation prompt routed to WebResearch"
Assert-Condition ($sugWeb.IsCloud -eq $false) "Web prompt IsCloud is false"

# 2.2 Software Architecture / Deep Refactoring triggers Gemini 3.8 Flash
$archPrompt = "Analizza l'architettura dell'applicazione e progetta il refactoring dei moduli secondo il pattern CQRS con dependency injection e thread safety."
$sugArch = [OpenAgent.Agent.ModelTaskRouter]::AnalyzePrompt($archPrompt, [OpenAgent.Data.AppSettings]::DefaultProfile17B)
Assert-Condition ($sugArch.Domain -eq [OpenAgent.Agent.TaskDomain]::ComplexReasoningCloud) "Architecture prompt routed to ComplexReasoningCloud"
Assert-Condition ($sugArch.IsCloud -eq $true) "Architecture prompt IsCloud is true"
Assert-Condition ($sugArch.RecommendedProfile -eq [OpenAgent.Data.AppSettings]::ProfileGemini38Flash) "Architecture prompt recommends Gemini 3.8 Flash"

# 2.3 Formal Logic / Multi-step complex planning
$logicPrompt = "Dimostra la correttezza formale di questo algoritmo avanzato ed effettua una analisi comparativa delle prestazioni a tappe."
$sugLogic = [OpenAgent.Agent.ModelTaskRouter]::AnalyzePrompt($logicPrompt, [OpenAgent.Data.AppSettings]::DefaultProfile17B)
Assert-Condition ($sugLogic.Domain -eq [OpenAgent.Agent.TaskDomain]::ComplexReasoningCloud) "Formal logic prompt routed to ComplexReasoningCloud"
Assert-Condition ($sugLogic.IsCloud -eq $true) "Formal logic prompt IsCloud is true"

# 2.4 Payload size exceeding threshold (> 750 characters)
$longPrompt = "Questo è un testo estremamente lungo e articolato che supera la soglia di caratteri di una macchina con risorse limitate. " * 15
Assert-Condition ($longPrompt.Length -gt 750) "Test prompt length ($($longPrompt.Length)) is above 750 chars"
$sugLong = [OpenAgent.Agent.ModelTaskRouter]::AnalyzePrompt($longPrompt, [OpenAgent.Data.AppSettings]::DefaultProfile17B)
Assert-Condition ($sugLong.Domain -eq [OpenAgent.Agent.TaskDomain]::ComplexReasoningCloud) "Long text payload routed to ComplexReasoningCloud"
Assert-Condition ($sugLong.IsCloud -eq $true) "Long text payload IsCloud is true"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 3: AgentStateMachine & CloudProcessing State
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 3: AgentStateMachine & CloudProcessing State ---" -ForegroundColor Yellow

$sm = New-Object OpenAgent.Agent.StateMachine.AgentStateMachine
$transitionDetail = ""
$sm.add_StateChanged({
    param($oldState, $newState, $detail)
    $script:transitionDetail = "$oldState -> $newState ($detail)"
})

$sm.TransitionTo([OpenAgent.Agent.StateMachine.AgentState]::CloudProcessing, "Elaborazione Gemini 3.8 Flash...")
Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::CloudProcessing) "CurrentState is CloudProcessing"
Assert-Condition ($transitionDetail -like "*CloudProcessing*") "StateChanged event fired with CloudProcessing state"

$sm.TransitionTo([OpenAgent.Agent.StateMachine.AgentState]::Idle, "Pronto")
Assert-Condition ($sm.CurrentState -eq [OpenAgent.Agent.StateMachine.AgentState]::Idle) "Transition back to Idle successful"

# ------------------------------------------------------------------------------------------------
# TEST GROUP 4: GeminiClient Configuration & Contracts
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 4: GeminiClient Configuration & Contracts ---" -ForegroundColor Yellow

$geminiClient = New-Object OpenAgent.Model.GeminiClient("fake-key-for-unit-test", "gemini-3.8-flash", "gemini-3.5-flash-lite")
Assert-Condition ($geminiClient.PrimaryModel -eq "gemini-3.8-flash") "GeminiClient PrimaryModel is gemini-3.8-flash"
Assert-Condition ($geminiClient.FallbackModel -eq "gemini-3.5-flash-lite") "GeminiClient FallbackModel is gemini-3.5-flash-lite"
Assert-Condition ($geminiClient.EnableFallback -eq $true) "GeminiClient EnableFallback is true"

$fallbackFired = $false
$geminiClient.add_FallbackTriggered({
    param($pri, $fb, $reason)
    $script:fallbackFired = $true
})

# Missing API key check
$emptyKeyClient = New-Object OpenAgent.Model.GeminiClient("", "gemini-3.8-flash", "gemini-3.5-flash-lite")
$health = $emptyKeyClient.HealthCheckAsync().GetAwaiter().GetResult()
Assert-Condition ($health -eq $false) "HealthCheckAsync returns false when API key is empty"

$geminiClient.Dispose()
$emptyKeyClient.Dispose()

# ------------------------------------------------------------------------------------------------
# TEST GROUP 5: AgentController 2-Failure Escalation & Privacy Confirmation
# ------------------------------------------------------------------------------------------------
Write-Host "`n--- TEST GROUP 5: AgentController 2-Failure Escalation ---" -ForegroundColor Yellow

$rootDir = (Get-Item .).FullName
$pm = New-Object OpenAgent.Agent.Security.PermissionManager($rootDir)
$tr = New-Object OpenAgent.Tools.ToolRegistry

# Use MockModelProvider simulating local failure (empty choices)
$localMock = New-Object OpenAgent.Model.MockModelProvider
$emptyResp1 = New-Object OpenAgent.Model.Contracts.ChatCompletionResponse
$emptyResp2 = New-Object OpenAgent.Model.Contracts.ChatCompletionResponse
$localMock.EnqueueResponse($emptyResp1)
$localMock.EnqueueResponse($emptyResp2)

# Mock cloud provider with valid final answer
$cloudMock = New-Object OpenAgent.Model.MockModelProvider
$cloudMock.EnqueueAssistantReply("Risposta risolutiva fornita da Gemini 3.8 Flash Cloud.")

$controller = New-Object OpenAgent.Agent.AgentController($localMock, $tr, $pm, $sm, $null, $null, $cloudMock)

$confirmationAsked = $false
$confirmationMessage = ""
$controller.CloudConfirmationHandler = [Func[string, System.Threading.Tasks.Task[bool]]]{
    param($msg)
    $script:confirmationAsked = $true
    $script:confirmationMessage = $msg
    # User confirms escalation!
    return [System.Threading.Tasks.Task]::FromResult($true)
}

$escalationNotified = $false
$controller.add_CloudFallbackOccurred({
    param($orig, $cloud)
    $script:escalationNotified = $true
})

# First run: returns empty response -> failure count reaches 1
$res1 = $controller.ProcessPromptAsync("Richiesta 1").GetAwaiter().GetResult()
Assert-Condition ($controller.LocalConsecutiveFailures -eq 1) "First failed attempt recorded (LocalConsecutiveFailures = 1)"
Assert-Condition ($confirmationAsked -eq $false) "Confirmation NOT asked on 1st failure"

# Second run: returns empty response -> failure count reaches 2 -> escalation triggers!
$res2 = $controller.ProcessPromptAsync("Richiesta 2").GetAwaiter().GetResult()
Assert-Condition ($confirmationAsked -eq $true) "Confirmation WAS requested on 2nd consecutive failure"
Assert-Condition ($confirmationMessage -like "*2 passaggi consecutivi*") "Confirmation prompt mentions 2 consecutive failures"
Assert-Condition ($escalationNotified -eq $true) "CloudFallbackOccurred event was fired"
Assert-Condition ($res2 -like "*Gemini 3.8 Flash Cloud*") "Final answer was completed by Gemini Cloud provider"
Assert-Condition ($controller.LocalConsecutiveFailures -eq 0) "LocalConsecutiveFailures reset to 0 after successful Cloud completion"

# ------------------------------------------------------------------------------------------------
# TEST SUMMARY
# ------------------------------------------------------------------------------------------------
Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "                TEST RESULTS SUMMARY                    " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  PASSED: $passCount" -ForegroundColor Green
Write-Host "  FAILED: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })
Write-Host "========================================================`n" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
} else {
    exit 0
}
