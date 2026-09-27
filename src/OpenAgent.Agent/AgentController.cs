using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Agent.Security;
using OpenAgent.Agent.StateMachine;
using OpenAgent.Data;
using OpenAgent.Model;
using OpenAgent.Model.Contracts;
using OpenAgent.Tools;
using OpenAgent.Tools.Security;

namespace OpenAgent.Agent;

public sealed record ToolConfirmationRequest(
    string ToolName,
    ToolRiskLevel RiskLevel,
    string ArgumentsJson
);

public sealed class AgentController
{
    private readonly IModelProvider _modelProvider;
    private readonly ToolRegistry _toolRegistry;
    private readonly IPermissionManager _permissionManager;
    private readonly AgentStateMachine _stateMachine;
    private readonly IAuditRepository? _auditRepository;
    private readonly IUserSettingsRepository? _settingsRepository;
    private readonly List<ChatMessage> _messages = [];
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private CancellationTokenSource? _currentExecutionCts;

    public int MaxConsecutiveToolTurns { get; set; } = 4;
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(180);

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];
    public AgentStateMachine StateMachine => _stateMachine;
    public ToolRegistry ToolRegistry => _toolRegistry;
    public IPermissionManager PermissionManager => _permissionManager;
    public IAuditRepository? AuditRepository => _auditRepository;
    public IUserSettingsRepository? SettingsRepository => _settingsRepository;
    public IReadOnlyList<ChatMessage> History => _messages.AsReadOnly();

    public Func<ToolConfirmationRequest, Task<bool>>? ConfirmationHandler { get; set; }
    public Func<string, Task<bool>>? CloudConfirmationHandler { get; set; }
    public IModelProvider? CloudProvider { get; set; }

    public bool IsCancelled { get; private set; }
    public int StepsExecutedCount { get; private set; }
    public bool WasLoopDetected => !string.IsNullOrEmpty(LastLoopDetectedMessage);
    public string? LastLoopDetectedMessage { get; private set; }
    public int LocalConsecutiveFailures => _localConsecutiveFailures;
    private int _localConsecutiveFailures = 0;
    public string AgentCustomName { get; private set; } = "OpenAgent";

    // Multi-Step Progress & Agent Lifecycle Events
    public event Action<string>? SpeechReceived;
    public event Action<string, string>? ToolExecuting; // (toolName, arguments)
    public event Action<string, ToolResult>? ToolCompleted; // (toolName, result)
    public event Action<string>? NetworkAccessOccurred; // (url or detail)
    public event Action<int, int>? StepStarted; // (stepNumber, maxSteps)
    public event Action<int, string, string>? StepExecuting; // (stepNumber, toolName, argsJson)
    public event Action<int, string, ToolResult>? StepCompleted; // (stepNumber, toolName, result)
    public event Action<string, string>? LoopDetected; // (toolName, loopMessage)
    public event Action<string, string>? CloudFallbackOccurred; // (originalModel, cloudModel)
    public event Action? ExecutionCancelled;

    public string SystemPrompt { get; set; } =
        "Sei OpenAgent, un assistente desktop agile, sicuro ed efficiente. " +
        "Hai a disposizione tool per interagire con file, consultare il web e gestire preferenze persistenti: " +
        "- Quando l'utente menziona un URL o una pagina web (http:// o https://), invoca sempre il tool 'fetch_url_text'. " +
        "- Quando l'utente ti chiede di cercare qualcosa su Internet, invoca il tool 'search_web'. " +
        "- Quando l'utente chiede di cartelle o file locali, usa 'list_directory' o 'read_file'. " +
        "- REGOLA TASSATIVA SULLA MEMORIA: Quando l'utente ti chiede di ricordare o salvare note, abitudini, preferenze o nomi, DEVI OBBLIGATORIAMENTE invocare 'remember_user_preference'. Non limitarti mai a rispondere a parole senza chiamare il tool! " +
        "- Quando hai bisogno di verificare informazioni passate dell'utente, invoca 'recall_user_memory'. " +
        "Puoi concatenare più tool in sequenza per portare a termine compiti complessi (ad es. esplora cartella -> leggi file -> sintetizza). " +
        "Non inventare mai risposte, percorsi o contenuti web: invoca sempre il tool appropriato prima di rispondere. " +
        "Rispondi in italiano in modo chiaro, utile e sintetico.";

    public AgentController(
        IModelProvider modelProvider,
        ToolRegistry toolRegistry,
        IPermissionManager permissionManager,
        AgentStateMachine? stateMachine = null,
        IAuditRepository? auditRepository = null,
        IUserSettingsRepository? settingsRepository = null,
        IModelProvider? cloudProvider = null)
    {
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
        _stateMachine = stateMachine ?? new AgentStateMachine();
        _auditRepository = auditRepository;
        _settingsRepository = settingsRepository;
        CloudProvider = cloudProvider;

        ResetConversation();
    }

    public void ResetConversation()
    {
        _messages.Clear();
        _messages.Add(ChatMessage.System(SystemPrompt));
        _stateMachine.TransitionTo(AgentState.Idle, "In ascolto");
        IsCancelled = false;
        StepsExecutedCount = 0;
        LastLoopDetectedMessage = null;
        _localConsecutiveFailures = 0;
    }

    public async Task RefreshSystemPromptWithUserMemoriesAsync(CancellationToken cancellationToken = default)
    {
        string agentName = "OpenAgent";
        IReadOnlyList<UserMemoryEntry> memories = [];

        if (_settingsRepository != null)
        {
            try
            {
                memories = await _settingsRepository.GetAllMemoriesAsync(cancellationToken).ConfigureAwait(false);
                var nameEntry = memories.FirstOrDefault(m =>
                    m.Key.Equals(MemoryIntentHandler.AgentNameKey, StringComparison.OrdinalIgnoreCase) ||
                    m.Key.Equals("nome", StringComparison.OrdinalIgnoreCase) ||
                    m.Key.Equals("agent_name", StringComparison.OrdinalIgnoreCase));

                if (nameEntry != null && !string.IsNullOrWhiteSpace(nameEntry.Value))
                {
                    agentName = nameEntry.Value.Trim();
                }
            }
            catch
            {
                // Fallback to base prompt on repository error
            }
        }

        AgentCustomName = agentName;

        string prompt;
        if (!agentName.Equals("OpenAgent", StringComparison.OrdinalIgnoreCase))
        {
            prompt =
                $"Sei {agentName}, un assistente desktop agile, sicuro ed efficiente basato su OpenAgent.\n" +
                $"L'utente ti ha assegnato il nome '{agentName}'. Presentati sempre come {agentName} e rispondi con questo nome quando ti viene chiesto chi sei o come ti chiami.\n" +
                "Hai a disposizione tool per interagire con file, consultare il web e gestire preferenze persistenti:\n" +
                "- Quando l'utente menziona un URL o una pagina web (http:// o https://), invoca sempre il tool 'fetch_url_text'.\n" +
                "- Quando l'utente ti chiede di cercare qualcosa su Internet, invoca il tool 'search_web'.\n" +
                "- Quando l'utente chiede di cartelle o file locali, usa 'list_directory' o 'read_file'.\n" +
                "- REGOLA TASSATIVA SULLA MEMORIA: Quando l'utente ti chiede di ricordare o salvare note, abitudini, preferenze o nomi, DEVI OBBLIGATORIAMENTE invocare 'remember_user_preference'. Non limitarti mai a rispondere a parole senza chiamare il tool!\n" +
                "- Quando hai bisogno di verificare informazioni passate dell'utente, invoca 'recall_user_memory'.\n" +
                "Puoi concatenare più tool in sequenza per portare a termine compiti complessi.\n" +
                "Non inventare mai risposte, percorsi o contenuti web: invoca sempre il tool appropriato prima di rispondere.\n" +
                "Rispondi in italiano in modo chiaro, utile e sintetico.";
        }
        else
        {
            prompt =
                "Sei OpenAgent, un assistente desktop agile, sicuro ed efficiente.\n" +
                "Hai a disposizione tool per interagire con file, consultare il web e gestire preferenze persistenti:\n" +
                "- Quando l'utente menziona un URL o una pagina web (http:// o https://), invoca sempre il tool 'fetch_url_text'.\n" +
                "- Quando l'utente ti chiede di cercare qualcosa su Internet, invoca il tool 'search_web'.\n" +
                "- Quando l'utente chiede di cartelle o file locali, usa 'list_directory' o 'read_file'.\n" +
                "- REGOLA TASSATIVA SULLA MEMORIA: Quando l'utente ti chiede di ricordare o salvare note, abitudini, preferenze o nomi, DEVI OBBLIGATORIAMENTE invocare 'remember_user_preference'. Non limitarti mai a rispondere a parole senza chiamare il tool!\n" +
                "- Quando hai bisogno di verificare informazioni passate dell'utente, invoca 'recall_user_memory'.\n" +
                "Puoi concatenare più tool in sequenza per portare a termine compiti complessi.\n" +
                "Non inventare mai risposte, percorsi o contenuti web: invoca sempre il tool appropriato prima di rispondere.\n" +
                "Rispondi in italiano in modo chiaro, utile e sintetico.";
        }

        if (memories.Count > 0)
        {
            prompt += "\n\nPreferenze e memorie persistenti salvate dell'utente:\n" +
                      string.Join("\n", memories.Select(m => $"- [{m.Key}]: {m.Value}"));
        }

        if (_messages.Count > 0 && _messages[0].Role == "system")
        {
            _messages[0] = ChatMessage.System(prompt);
        }
        else
        {
            _messages.Insert(0, ChatMessage.System(prompt));
        }
    }

    public void CancelCurrentExecution()
    {
        try
        {
            _currentExecutionCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS already completed or disposed
        }
    }

    public async Task<string> ProcessPromptAsync(string prompt, bool forceCloud = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        await _executionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var masterCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentExecutionCts = masterCts;

        try
        {
            IsCancelled = false;
            StepsExecutedCount = 0;
            LastLoopDetectedMessage = null;

            IModelProvider currentProvider = (forceCloud && CloudProvider != null) ? CloudProvider : _modelProvider;
            bool isCloudRunning = (forceCloud && CloudProvider != null);

            if (isCloudRunning)
            {
                _stateMachine.TransitionTo(AgentState.CloudProcessing, "Elaborazione Gemini 3.8 Flash (Cloud)...");
            }
            else
            {
                _stateMachine.TransitionTo(AgentState.Thinking, "Elaborazione richiesta...");
            }

            // Dynamically refresh system prompt with stored user preferences
            using (var initStepCts = CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token))
            {
                initStepCts.CancelAfter(ExecutionTimeout);
                await RefreshSystemPromptWithUserMemoriesAsync(initStepCts.Token).ConfigureAwait(false);
            }

            // Append user prompt to history
            _messages.Add(ChatMessage.User(prompt.Trim()));

            // Check for explicit memory intent (name assignment, preferences, memory queries)
            var memoryIntent = MemoryIntentHandler.DetectIntent(prompt);
            if (memoryIntent.Type != MemoryIntentType.None && _settingsRepository != null)
            {
                var directReply = await HandleMemoryIntentDirectlyAsync(memoryIntent, prompt, masterCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(directReply))
                {
                    _messages.Add(ChatMessage.Assistant(directReply));
                    _stateMachine.TransitionTo(AgentState.Speaking, "Risposta");
                    SafeInvoke(SpeechReceived, directReply);
                    _stateMachine.TransitionTo(AgentState.Idle, "In ascolto");
                    return directReply;
                }
            }

            string activeModel = isCloudRunning ? "gemini-3.8-flash" : "Qwen2.5-3B-Instruct";
            if (!isCloudRunning && _settingsRepository != null)
            {
                try
                {
                    var settings = await _settingsRepository.GetSettingsAsync(masterCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(settings.ModelProfile))
                    {
                        activeModel = settings.ModelProfile;
                    }
                }
                catch
                {
                    // Fallback to default
                }
            }

            int toolTurns = 0;
            var callTracker = new SemanticCallTracker();

            while (true)
            {
                masterCts.Token.ThrowIfCancellationRequested();

                SafeInvoke(StepStarted, toolTurns + 1, MaxConsecutiveToolTurns);

                var request = new ChatCompletionRequest
                {
                    Model = activeModel,
                    Messages = [.. _messages],
                    Tools = _toolRegistry.GetToolDefinitions(),
                    ToolChoice = "auto",
                    Temperature = 0.1
                };

                ChatCompletionResponse response;
                using (var inferenceCts = CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token))
                {
                    inferenceCts.CancelAfter(ExecutionTimeout);
                    try
                    {
                        response = await currentProvider.GenerateChatCompletionAsync(request, inferenceCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!isCloudRunning)
                        {
                            _localConsecutiveFailures++;
                            if (_localConsecutiveFailures >= 2 && CloudProvider != null && CloudConfirmationHandler != null)
                            {
                                bool userWantsCloud = await CloudConfirmationHandler(
                                    $"Il modello locale ha riscontrato un errore per 2 passaggi consecutivi ({ex.Message}). Vuoi passare a Google Gemini 3.8 Flash nel Cloud per completare la richiesta?")
                                    .ConfigureAwait(false);

                                if (userWantsCloud)
                                {
                                    CloudFallbackOccurred?.Invoke(activeModel, "gemini-3.8-flash");
                                    currentProvider = CloudProvider;
                                    isCloudRunning = true;
                                    activeModel = "gemini-3.8-flash";
                                    _localConsecutiveFailures = 0;
                                    _stateMachine.TransitionTo(AgentState.CloudProcessing, "Passaggio a Gemini 3.8 Flash (Cloud)...");
                                    continue;
                                }
                            }
                        }

                        _stateMachine.TransitionTo(AgentState.Error, $"Errore inferenza: {ex.Message}");
                        throw;
                    }
                }

                var choice = response.Choices.FirstOrDefault();
                if (choice == null || choice.Message == null)
                {
                    if (!isCloudRunning)
                    {
                        _localConsecutiveFailures++;
                        if (_localConsecutiveFailures >= 2 && CloudProvider != null && CloudConfirmationHandler != null)
                        {
                            bool userWantsCloud = await CloudConfirmationHandler(
                                "Il modello locale ha fornito una risposta non valida per 2 passaggi consecutivi. Vuoi passare a Google Gemini 3.8 Flash nel Cloud per completare la richiesta?")
                                .ConfigureAwait(false);

                            if (userWantsCloud)
                            {
                                CloudFallbackOccurred?.Invoke(activeModel, "gemini-3.8-flash");
                                currentProvider = CloudProvider;
                                isCloudRunning = true;
                                activeModel = "gemini-3.8-flash";
                                _localConsecutiveFailures = 0;
                                _stateMachine.TransitionTo(AgentState.CloudProcessing, "Passaggio a Gemini 3.8 Flash (Cloud)...");
                                continue;
                            }
                        }
                    }

                    _stateMachine.TransitionTo(AgentState.Error, "Risposta vuota dal modello.");
                    return "Non sono riuscito a elaborare la richiesta (risposta vuota dal motore di inferenza).";
                }

                var assistantMsg = choice.Message;

                // Fallback extraction: check if model emitted tool call in content
                if ((assistantMsg.ToolCalls == null || assistantMsg.ToolCalls.Count == 0) &&
                    !string.IsNullOrWhiteSpace(assistantMsg.Content))
                {
                    var fallbackCalls = TryExtractToolCallsFromContent(assistantMsg.Content);
                    if (fallbackCalls.Count > 0)
                    {
                        assistantMsg.ToolCalls = fallbackCalls;
                    }
                }

                // Check for tool calls
                if (assistantMsg.ToolCalls != null && assistantMsg.ToolCalls.Count > 0)
                {
                    toolTurns++;
                    _messages.Add(assistantMsg);

                    bool shouldHaltLoop = false;

                    foreach (var toolCall in assistantMsg.ToolCalls)
                    {
                        masterCts.Token.ThrowIfCancellationRequested();

                        string toolName = toolCall.Function.Name;
                        string argsJson = toolCall.Function.Arguments;

                        // Advanced Semantic Loop & Ping-Pong detection
                        var outcome = callTracker.RecordAndCheck(toolName, argsJson);

                        if (outcome.Result == LoopDetectionResult.Halt)
                        {
                            LastLoopDetectedMessage = outcome.Message;
                            SafeInvoke(LoopDetected, toolName, outcome.Message);
                            var haltResult = ToolResult.Fail(outcome.Message);
                            SafeInvoke(ToolCompleted, toolName, haltResult);
                            SafeInvoke(StepCompleted, toolTurns, toolName, haltResult);
                            _messages.Add(ChatMessage.Tool(toolCall.Id, haltResult.Content, toolName));
                            shouldHaltLoop = true;
                            break;
                        }

                        if (outcome.Result == LoopDetectionResult.Warning)
                        {
                            LastLoopDetectedMessage = outcome.Message;
                            SafeInvoke(LoopDetected, toolName, outcome.Message);
                            var warnResult = ToolResult.Fail(outcome.Message);
                            SafeInvoke(ToolCompleted, toolName, warnResult);
                            SafeInvoke(StepCompleted, toolTurns, toolName, warnResult);
                            _messages.Add(ChatMessage.Tool(toolCall.Id, warnResult.Content, toolName));
                            continue;
                        }

                        // Normal tool execution
                        _stateMachine.TransitionTo(AgentState.ToolActive, $"Passo {toolTurns}/{MaxConsecutiveToolTurns}: {toolName}...");
                        SafeInvoke(ToolExecuting, toolName, argsJson);
                        SafeInvoke(StepExecuting, toolTurns, toolName, argsJson);

                        var tool = _toolRegistry.GetTool(toolName);
                        var risk = tool?.RiskLevel ?? ToolRiskLevel.Read;

                        // Check network indicator for web tools
                        if (toolName.Contains("web", StringComparison.OrdinalIgnoreCase) ||
                            toolName.Contains("url", StringComparison.OrdinalIgnoreCase))
                        {
                            SafeInvoke(NetworkAccessOccurred, argsJson);
                        }

                        // Security Confirmation check for MODIFY or DESTRUCTIVE actions
                        // Confirmation wait does NOT count against execution timeout
                        bool? userConfirmed = null;
                        if (risk >= ToolRiskLevel.Modify)
                        {
                            if (ConfirmationHandler != null)
                            {
                                bool approved = await ConfirmationHandler(
                                    new ToolConfirmationRequest(toolName, risk, argsJson)
                                ).ConfigureAwait(false);

                                userConfirmed = approved;

                                if (!approved)
                                {
                                    var rejectResult = ToolResult.Fail("Azione annullata dall'utente: operazione non autorizzata.");
                                    SafeInvoke(ToolCompleted, toolName, rejectResult);
                                    SafeInvoke(StepCompleted, toolTurns, toolName, rejectResult);
                                    _messages.Add(ChatMessage.Tool(toolCall.Id, rejectResult.Content, toolName));

                                    if (_auditRepository != null)
                                    {
                                        await LogAuditEntrySafeAsync(toolName, risk.ToString(), argsJson, rejectResult, 0, false).ConfigureAwait(false);
                                    }
                                    continue;
                                }
                            }
                            else if (risk == ToolRiskLevel.Destructive)
                            {
                                var unconfirmedResult = ToolResult.Fail("Operazione distruttiva bloccata: nessun gestore di conferma autorizzato.");
                                SafeInvoke(ToolCompleted, toolName, unconfirmedResult);
                                SafeInvoke(StepCompleted, toolTurns, toolName, unconfirmedResult);
                                _messages.Add(ChatMessage.Tool(toolCall.Id, unconfirmedResult.Content, toolName));
                                continue;
                            }
                        }

                        // Execute tool with duration measurement and per-step cancellation support
                        var sw = Stopwatch.StartNew();
                        ToolResult result;
                        using (var toolStepCts = CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token))
                        {
                            toolStepCts.CancelAfter(ExecutionTimeout);
                            result = await _toolRegistry.ExecuteToolCallAsync(
                                toolName,
                                argsJson,
                                toolStepCts.Token
                            ).ConfigureAwait(false);
                        }
                        sw.Stop();

                        SafeInvoke(ToolCompleted, toolName, result);
                        SafeInvoke(StepCompleted, toolTurns, toolName, result);
                        StepsExecutedCount++;

                        // Feed tool result back to chat context
                        _messages.Add(ChatMessage.Tool(
                            toolCall.Id,
                            result.Success ? result.Content : $"Errore tool '{toolName}': {result.Content}",
                            toolName
                        ));

                        // If user memory was added/updated, immediately refresh system prompt for next steps
                        if (result.Success && toolName.Equals("remember_user_preference", StringComparison.OrdinalIgnoreCase))
                        {
                            using var memStepCts = CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token);
                            memStepCts.CancelAfter(ExecutionTimeout);
                            await RefreshSystemPromptWithUserMemoriesAsync(memStepCts.Token).ConfigureAwait(false);
                        }

                        // Audit Trail persistence
                        if (_auditRepository != null)
                        {
                            await LogAuditEntrySafeAsync(toolName, risk.ToString(), argsJson, result, sw.ElapsedMilliseconds, userConfirmed).ConfigureAwait(false);
                        }
                    }

                    if (shouldHaltLoop)
                    {
                        if (!isCloudRunning)
                        {
                            _localConsecutiveFailures++;
                            if (_localConsecutiveFailures >= 2 && CloudProvider != null && CloudConfirmationHandler != null)
                            {
                                bool userWantsCloud = await CloudConfirmationHandler(
                                    "Il modello locale è entrato in un loop ripetuto per 2 passaggi consecutivi. Vuoi passare a Google Gemini 3.8 Flash nel Cloud per risolvere la richiesta?")
                                    .ConfigureAwait(false);

                                if (userWantsCloud)
                                {
                                    CloudFallbackOccurred?.Invoke(activeModel, "gemini-3.8-flash");
                                    currentProvider = CloudProvider;
                                    isCloudRunning = true;
                                    activeModel = "gemini-3.8-flash";
                                    _localConsecutiveFailures = 0;
                                    _stateMachine.TransitionTo(AgentState.CloudProcessing, "Passaggio a Gemini 3.8 Flash (Cloud)...");
                                    continue;
                                }
                            }
                        }

                        _stateMachine.TransitionTo(AgentState.Idle, "Loop interrotto");
                        string loopNotice = "L'operazione è stata interrotta per prevenire un loop infinito di chiamate tool ripetute. Di seguito i dettagli raccolti prima dell'interruzione.";
                        _messages.Add(ChatMessage.Assistant(loopNotice));
                        SafeInvoke(SpeechReceived, loopNotice);
                        return loopNotice;
                    }

                    if (toolTurns >= MaxConsecutiveToolTurns)
                    {
                        // Exceeded tool loop limit
                        if (!isCloudRunning)
                        {
                            _localConsecutiveFailures++;
                            if (_localConsecutiveFailures >= 2 && CloudProvider != null && CloudConfirmationHandler != null)
                            {
                                bool userWantsCloud = await CloudConfirmationHandler(
                                    $"Il modello locale ha raggiunto il limite massimo di passaggi consecutivi ({MaxConsecutiveToolTurns}). Vuoi passare a Google Gemini 3.8 Flash nel Cloud per concludere la richiesta?")
                                    .ConfigureAwait(false);

                                if (userWantsCloud)
                                {
                                    CloudFallbackOccurred?.Invoke(activeModel, "gemini-3.8-flash");
                                    currentProvider = CloudProvider;
                                    isCloudRunning = true;
                                    activeModel = "gemini-3.8-flash";
                                    _localConsecutiveFailures = 0;
                                    _stateMachine.TransitionTo(AgentState.CloudProcessing, "Passaggio a Gemini 3.8 Flash (Cloud)...");
                                    toolTurns = 0;
                                    continue;
                                }
                            }
                        }

                        string limitWarning = $"Ho interrotto l'elaborazione perché è stato raggiunto il limite massimo di passaggi consecutivi ({MaxConsecutiveToolTurns}).";
                        _messages.Add(ChatMessage.Assistant(limitWarning));
                        _stateMachine.TransitionTo(AgentState.Idle, "In ascolto");
                        SafeInvoke(SpeechReceived, limitWarning);
                        return limitWarning;
                    }

                    // Loop back to model with tool observations
                    if (isCloudRunning)
                    {
                        _stateMachine.TransitionTo(AgentState.CloudProcessing, $"Sintesi Cloud passo {toolTurns}/{MaxConsecutiveToolTurns}...");
                    }
                    else
                    {
                        _stateMachine.TransitionTo(AgentState.Thinking, $"Sintesi passo {toolTurns}/{MaxConsecutiveToolTurns}...");
                    }
                    continue;
                }

                // Normal textual response received
                _localConsecutiveFailures = 0;
                string finalReply = assistantMsg.Content?.Trim() ?? string.Empty;
                _messages.Add(ChatMessage.Assistant(finalReply));

                _stateMachine.TransitionTo(AgentState.Speaking, "Risposta");
                SafeInvoke(SpeechReceived, finalReply);

                _stateMachine.TransitionTo(AgentState.Idle, "In ascolto");
                return finalReply;
            }
        }
        catch (OperationCanceledException)
        {
            IsCancelled = true;
            _stateMachine.TransitionTo(AgentState.Idle, "Operazione annullata");
            SafeInvoke(ExecutionCancelled);
            return masterCts.IsCancellationRequested
                ? "Operazione annullata dall'utente."
                : $"Operazione interrotta per timeout (superato limite di {(int)ExecutionTimeout.TotalSeconds}s per passaggio).";
        }
        catch (Exception ex)
        {
            _stateMachine.TransitionTo(AgentState.Error, $"Errore: {ex.Message}");
            return $"Si è verificato un errore: {ex.Message}";
        }
        finally
        {
            _currentExecutionCts = null;
            _executionLock.Release();
        }
    }

    private static void SafeInvoke(Action? action)
    {
        try { action?.Invoke(); } catch { }
    }

    private static void SafeInvoke<T>(Action<T>? action, T arg)
    {
        try { action?.Invoke(arg); } catch { }
    }

    private static void SafeInvoke<T1, T2>(Action<T1, T2>? action, T1 arg1, T2 arg2)
    {
        try { action?.Invoke(arg1, arg2); } catch { }
    }

    private static void SafeInvoke<T1, T2, T3>(Action<T1, T2, T3>? action, T1 arg1, T2 arg2, T3 arg3)
    {
        try { action?.Invoke(arg1, arg2, arg3); } catch { }
    }

    private async Task LogAuditEntrySafeAsync(
        string toolName,
        string riskLevel,
        string argsJson,
        ToolResult result,
        long durationMs,
        bool? userConfirmed)
    {
        if (_auditRepository == null) return;

        try
        {
            string preview = result.Content ?? string.Empty;
            if (preview.Length > 400)
            {
                preview = preview[..400] + "...";
            }

            var entry = new ToolAuditEntry
            {
                SessionId = SessionId,
                TimestampUtc = DateTime.UtcNow,
                ToolName = toolName,
                RiskLevel = riskLevel,
                ArgumentsJson = argsJson,
                IsSuccess = result.Success,
                ResultPreview = preview,
                ErrorMessage = result.Error,
                ExecutionDurationMs = durationMs,
                UserConfirmed = userConfirmed
            };

            await _auditRepository.LogExecutionAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Logging failure should never crash the agent runtime
        }
    }

    private async Task<string?> HandleMemoryIntentDirectlyAsync(
        MemoryIntentMatch intent,
        string rawPrompt,
        CancellationToken cancellationToken)
    {
        if (_settingsRepository == null) return null;

        var sw = Stopwatch.StartNew();

        switch (intent.Type)
        {
            case MemoryIntentType.SetAgentName:
            {
                string toolName = "remember_user_preference";
                string argsJson = $"{{\"key\": \"{MemoryIntentHandler.AgentNameKey}\", \"value\": \"{intent.Value}\"}}";
                _stateMachine.TransitionTo(AgentState.ToolActive, "Memorizzazione nome agente...");
                SafeInvoke(ToolExecuting, toolName, argsJson);
                SafeInvoke(StepExecuting, 1, toolName, argsJson);

                await _settingsRepository.SetMemoryAsync(MemoryIntentHandler.AgentNameKey, intent.Value, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                var okResult = ToolResult.Ok($"Nome agente '{intent.Value}' memorizzato con successo.");
                SafeInvoke(ToolCompleted, toolName, okResult);
                SafeInvoke(StepCompleted, 1, toolName, okResult);
                StepsExecutedCount++;

                if (_auditRepository != null)
                {
                    await LogAuditEntrySafeAsync(toolName, ToolRiskLevel.Read.ToString(), argsJson, okResult, sw.ElapsedMilliseconds, true).ConfigureAwait(false);
                }

                await RefreshSystemPromptWithUserMemoriesAsync(cancellationToken).ConfigureAwait(false);

                return $"Ho memorizzato con successo il mio nuovo nome: **{intent.Value}**! 🐙 Da adesso in poi mi ricorderò sempre di chiamarmi così nelle nostre conversazioni e nelle prossime sessioni.";
            }

            case MemoryIntentType.SetUserName:
            {
                string toolName = "remember_user_preference";
                string argsJson = $"{{\"key\": \"{MemoryIntentHandler.UserNameKey}\", \"value\": \"{intent.Value}\"}}";
                _stateMachine.TransitionTo(AgentState.ToolActive, "Memorizzazione nome utente...");
                SafeInvoke(ToolExecuting, toolName, argsJson);
                SafeInvoke(StepExecuting, 1, toolName, argsJson);

                await _settingsRepository.SetMemoryAsync(MemoryIntentHandler.UserNameKey, intent.Value, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                var okResult = ToolResult.Ok($"Nome utente '{intent.Value}' memorizzato con successo.");
                SafeInvoke(ToolCompleted, toolName, okResult);
                SafeInvoke(StepCompleted, 1, toolName, okResult);
                StepsExecutedCount++;

                if (_auditRepository != null)
                {
                    await LogAuditEntrySafeAsync(toolName, ToolRiskLevel.Read.ToString(), argsJson, okResult, sw.ElapsedMilliseconds, true).ConfigureAwait(false);
                }

                await RefreshSystemPromptWithUserMemoriesAsync(cancellationToken).ConfigureAwait(false);

                return $"Piacere di conoscerti, **{intent.Value}**! Ho memorizzato il tuo nome nella mia memoria persistente per tutte le prossime sessioni.";
            }

            case MemoryIntentType.SetPreference:
            {
                string toolName = "remember_user_preference";
                string argsJson = $"{{\"key\": \"{intent.Key}\", \"value\": \"{intent.Value}\"}}";
                _stateMachine.TransitionTo(AgentState.ToolActive, "Memorizzazione preferenza...");
                SafeInvoke(ToolExecuting, toolName, argsJson);
                SafeInvoke(StepExecuting, 1, toolName, argsJson);

                await _settingsRepository.SetMemoryAsync(intent.Key, intent.Value, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                var okResult = ToolResult.Ok($"Preferenza '{intent.Key}' memorizzata con successo.");
                SafeInvoke(ToolCompleted, toolName, okResult);
                SafeInvoke(StepCompleted, 1, toolName, okResult);
                StepsExecutedCount++;

                if (_auditRepository != null)
                {
                    await LogAuditEntrySafeAsync(toolName, ToolRiskLevel.Read.ToString(), argsJson, okResult, sw.ElapsedMilliseconds, true).ConfigureAwait(false);
                }

                await RefreshSystemPromptWithUserMemoriesAsync(cancellationToken).ConfigureAwait(false);

                return $"Ho memorizzato con successo la tua preferenza **[{intent.Key}]**: \"{intent.Value}\" nel database locale per le sessioni future.";
            }

            case MemoryIntentType.RecallAll:
            {
                string toolName = "recall_user_memory";
                _stateMachine.TransitionTo(AgentState.ToolActive, "Consultazione memoria...");
                SafeInvoke(ToolExecuting, toolName, "{}");
                SafeInvoke(StepExecuting, 1, toolName, "{}");

                var memories = await _settingsRepository.GetAllMemoriesAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                var okResult = ToolResult.Ok($"Recuperate {memories.Count} memorie.");
                SafeInvoke(ToolCompleted, toolName, okResult);
                SafeInvoke(StepCompleted, 1, toolName, okResult);
                StepsExecutedCount++;

                if (_auditRepository != null)
                {
                    await LogAuditEntrySafeAsync(toolName, ToolRiskLevel.Read.ToString(), "{}", okResult, sw.ElapsedMilliseconds, null).ConfigureAwait(false);
                }

                if (memories.Count == 0)
                {
                    return "Al momento non ci sono preferenze o note memorizzate nel database locale. Puoi chiedermi di ricordare qualcosa dicendo ad esempio: 'Ricordati che il tuo nome è Octy' oppure 'Memorizza che preferisco il tema scuro'.";
                }

                return $"Ecco le {memories.Count} note e preferenze salvate nella mia memoria persistente:\n" +
                       string.Join("\n", memories.Select(m => $"• **[{m.Key}]**: {m.Value} *(aggiornato: {m.UpdatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm})*"));
            }

            case MemoryIntentType.AskAgentName:
            {
                if (!string.IsNullOrWhiteSpace(AgentCustomName) && !AgentCustomName.Equals("OpenAgent", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Mi chiamo **{AgentCustomName}**! 🐙 Sono il tuo assistente desktop OpenAgent pronto ad aiutarti.";
                }
                return "Sono **OpenAgent**, il tuo assistente desktop agile e sicuro. Se vuoi, puoi assegnarmi un nome personalizzato dicendo ad esempio: 'Ricordati che il tuo nome è Octy'.";
            }

            case MemoryIntentType.ClearMemory:
            {
                string toolName = "clear_user_memory";
                _stateMachine.TransitionTo(AgentState.ToolActive, "Svuotamento memoria...");
                SafeInvoke(ToolExecuting, toolName, "{}");
                SafeInvoke(StepExecuting, 1, toolName, "{}");

                await _settingsRepository.ClearMemoriesAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                var okResult = ToolResult.Ok("Tutte le memorie sono state cancellate.");
                SafeInvoke(ToolCompleted, toolName, okResult);
                SafeInvoke(StepCompleted, 1, toolName, okResult);
                StepsExecutedCount++;

                if (_auditRepository != null)
                {
                    await LogAuditEntrySafeAsync(toolName, ToolRiskLevel.Destructive.ToString(), "{}", okResult, sw.ElapsedMilliseconds, true).ConfigureAwait(false);
                }

                await RefreshSystemPromptWithUserMemoriesAsync(cancellationToken).ConfigureAwait(false);

                return "Ho svuotato completamente la memoria persistente dell'agente.";
            }

            default:
                return null;
        }
    }

    private List<ToolCall> TryExtractToolCallsFromContent(string content)
    {
        var result = new List<ToolCall>();
        if (string.IsNullOrWhiteSpace(content)) return result;

        // 1. Try extracting markdown code blocks
        var codeBlockMatches = Regex.Matches(content, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        foreach (Match m in codeBlockMatches)
        {
            string blockContent = m.Groups[1].Value.Trim();
            if (TryParseToolCallJson(blockContent, out var tc) && tc != null)
            {
                result.Add(tc);
            }
        }

        if (result.Count > 0) return result;

        // 2. Balanced brace scanner for JSON objects anywhere in text
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                int braceCount = 0;
                int start = i;
                for (int j = i; j < content.Length; j++)
                {
                    if (content[j] == '{') braceCount++;
                    else if (content[j] == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            string candidate = content.Substring(start, j - start + 1);
                            if (TryParseToolCallJson(candidate, out var tc) && tc != null)
                            {
                                result.Add(tc);
                            }
                            i = j; // Advance outer loop
                            break;
                        }
                    }
                }
            }
        }

        return result;
    }

    private bool TryParseToolCallJson(string json, out ToolCall? toolCall)
    {
        toolCall = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("name", out var nameProp) &&
                nameProp.ValueKind == JsonValueKind.String)
            {
                string toolName = nameProp.GetString()!;
                if (_toolRegistry.GetTool(toolName) != null)
                {
                    string argsStr = "{}";
                    if (root.TryGetProperty("arguments", out var argsProp))
                    {
                        argsStr = argsProp.ValueKind == JsonValueKind.String
                            ? argsProp.GetString()!
                            : argsProp.GetRawText();
                    }

                    toolCall = new ToolCall
                    {
                        Id = "call_" + Guid.NewGuid().ToString("N")[..8],
                        Type = "function",
                        Function = new FunctionCall
                        {
                            Name = toolName,
                            Arguments = argsStr
                        }
                    };
                    return true;
                }
            }
        }
        catch
        {
            // Ignore parse errors on candidate snippets
        }

        return false;
    }
}
