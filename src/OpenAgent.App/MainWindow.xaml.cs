using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using OpenAgent.Agent;
using OpenAgent.Agent.Security;
using OpenAgent.Agent.StateMachine;
using OpenAgent.App.Native;
using OpenAgent.App.Tray;
using OpenAgent.Data;
using OpenAgent.Data.Hardware;
using OpenAgent.Model;
using OpenAgent.Model.ProcessManager;
using OpenAgent.Tools;
using OpenAgent.Tools.FileSystem;
using OpenAgent.Tools.Memory;
using OpenAgent.Tools.Web;
using OpenAgent.App.Voice;

namespace OpenAgent.App;

public partial class MainWindow : Window
{
    private TrayIconManager? _trayIconManager;
    private GlobalHotKeyManager? _hotKeyManager;
    private VoiceInputService? _voiceInputService;
    private LlamaProcessManager? _llamaProcessManager;
    private LlamaCppClient? _modelClient;
    private GeminiClient? _geminiClient;
    private PermissionManager? _permissionManager;
    private ToolRegistry? _toolRegistry;
    private AgentStateMachine? _stateMachine;
    private AgentController? _agentController;
    private SqliteAuditRepository? _auditRepository;
    private SqliteUserSettingsRepository? _settingsRepository;
    private AppSettings _appSettings = new();
    private DispatcherTimer? _blinkTimer;
    private const double GhostVisualSize = 72;
    private DispatcherTimer? _grimoireTimer;
    private int _grimoireFrame;
    private DispatcherTimer? _speechMouthTimer;
    private bool _mouthOpen;
    private string? _currentActiveOutfit;

    private TaskCompletionSource<bool>? _pendingConfirmationTcs;
    private bool _isClickThroughEnabled;
    private IntPtr _hwnd;
    private bool _isLoaded;
    private bool _isDragging;
    private Point? _ghostDragStartPoint;
    private bool _isGhostDragging;
    private bool _isBalloonAbove = true;
    private bool _isBalloonToLeft = true;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;

        PromptInput.TextChanged += (_, _) =>
        {
            PromptPlaceholder.Visibility = string.IsNullOrEmpty(PromptInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        };
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        UpdateLayout();

        // Position window at bottom right of the primary work area
        double initialWidth = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? 412 : Width);
        double initialHeight = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? 350 : Height);

        var workArea = GetCurrentWorkArea();
        Left = workArea.Right - initialWidth - 30;
        Top = workArea.Bottom - initialHeight - 30;

        _isLoaded = true;
        UpdateBalloonPlacement();

        // Initialize Tray Icon
        _trayIconManager = new TrayIconManager();
        _trayIconManager.ToggleVisibilityRequested += ToggleWindowVisibility;
        _trayIconManager.ClickThroughToggled += SetClickThroughState;
        _trayIconManager.SettingsRequested += OpenSettings;
        _trayIconManager.ExitRequested += ExitApplication;

        // Initialize Voice Input Service (Whisper locale + NAudio)
        try
        {
            _voiceInputService = new VoiceInputService();
            _voiceInputService.StatusChanged += status => Dispatcher.Invoke(() => SetSpeechText(status));
            _voiceInputService.ErrorOccurred += err => Dispatcher.Invoke(() => SetSpeechText($"Avviso Voce: {err}"));

            // Precarica il modello Whisper in memoria se disponibile per risposta immediata
            _ = Task.Run(() => _voiceInputService.PreloadModel());
        }
        catch (Exception ex)
        {
            SetSpeechText($"Inizializzazione servizio vocale non riuscita: {ex.Message}");
        }

        // Initialize Global HotKeys (Ctrl + Space per Chat, Ctrl + Shift + V per Microfono)
        try
        {
            _hotKeyManager = new GlobalHotKeyManager(_hwnd, 9001);
            bool registered = _hotKeyManager.Register(
                NativeMethods.KeyModifiers.Control | NativeMethods.KeyModifiers.NoRepeat,
                0x20 // VK_SPACE
            );

            if (!registered)
            {
                // Fallback to Alt + Space if Ctrl + Space is occupied
                _hotKeyManager.Register(
                    NativeMethods.KeyModifiers.Alt | NativeMethods.KeyModifiers.NoRepeat,
                    0x20
                );
            }

            _hotKeyManager.HotKeyPressed += OnGlobalHotKeyPressed;

            // Global HotKey per Microfono / Input Vocale (Ctrl + Shift + V, fallback: Ctrl + Alt + V, fallback: Alt + M)
            bool voiceRegistered = _hotKeyManager.Register(
                9002,
                NativeMethods.KeyModifiers.Control | NativeMethods.KeyModifiers.Shift | NativeMethods.KeyModifiers.NoRepeat,
                0x56, // 'V'
                () => Dispatcher.Invoke(ToggleVoiceRecording)
            );

            if (!voiceRegistered)
            {
                voiceRegistered = _hotKeyManager.Register(
                    9002,
                    NativeMethods.KeyModifiers.Control | NativeMethods.KeyModifiers.Alt | NativeMethods.KeyModifiers.NoRepeat,
                    0x56, // 'V'
                    () => Dispatcher.Invoke(ToggleVoiceRecording)
                );
            }

            if (!voiceRegistered)
            {
                _hotKeyManager.Register(
                    9002,
                    NativeMethods.KeyModifiers.Alt | NativeMethods.KeyModifiers.NoRepeat,
                    0x4D, // 'M'
                    () => Dispatcher.Invoke(ToggleVoiceRecording)
                );
            }
        }
        catch (Exception ex)
        {
            SetSpeechText($"Avviso Hotkey: {ex.Message}");
        }

        // Start Ghost idle floating animation
        if (FindResource("GhostFloatingAnimation") is Storyboard sb)
        {
            sb.Begin(this, true);
        }

        // Ghost idle eye blinking timer (every 4 seconds)
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _blinkTimer.Tick += (_, _) =>
        {
            if (_stateMachine?.CurrentState == AgentState.Idle)
            {
                BlinkEyes();
            }
        };
        _blinkTimer.Start();

        // Initialize LlamaProcessManager and start llama-server asynchronously
        await InitializeLlamaServerAsync();
    }

    private void BlinkEyes()
    {
        GhostLeftEye.Height = 1;
        GhostRightEye.Height = 1;
        Canvas.SetTop(GhostLeftEye, 11);
        Canvas.SetTop(GhostRightEye, 11);
        GhostLeftTwinkle.Visibility = Visibility.Collapsed;
        GhostRightTwinkle.Visibility = Visibility.Collapsed;

        var restoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        restoreTimer.Tick += (s, e) =>
        {
            restoreTimer.Stop();
            GhostLeftEye.Height = 2;
            GhostRightEye.Height = 2;
            Canvas.SetTop(GhostLeftEye, 11);
            Canvas.SetTop(GhostRightEye, 11);
            GhostLeftTwinkle.Visibility = Visibility.Visible;
            GhostRightTwinkle.Visibility = Visibility.Visible;
        };
        restoreTimer.Start();
    }

    private async Task InitializeLlamaServerAsync()
    {
        try
        {
            UpdateVisualState("Starting", "Avvio modello...");
            _trayIconManager?.UpdateModelStatus(ServerStatus.Starting);

            // Configure relative to app base or workspace
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = FindProjectRootDir(baseDir);

            string dataDir = Path.Combine(rootDir, "data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

            // Initialize user settings & memory repository
            string settingsDbPath = Path.Combine(dataDir, "openagent_data.db");
            _settingsRepository = new SqliteUserSettingsRepository(settingsDbPath);
            await _settingsRepository.InitializeAsync();
            _appSettings = await _settingsRepository.GetSettingsAsync();

            // Hardware Detection and Automatic Profiling on Startup
            var hardwareSpecs = HardwareDetector.DetectSpecs(rootDir);
            bool shouldAutoTune = _appSettings.AutoTuneOnStartup || !_appSettings.HasCompletedInitialAutoTune;
            if (shouldAutoTune)
            {
                var opt = HardwareProfiler.OptimizeSettings(_appSettings, hardwareSpecs, rootDir);
                _appSettings = opt.OptimizedSettings;
                _appSettings.HasCompletedInitialAutoTune = true;
                await _settingsRepository.SaveSettingsAsync(_appSettings);
            }

            // Handle start minimized option
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) || _appSettings.StartMinimizedToTray)
            {
                SetBalloonVisibility(false);
            }

            string modelPathAbs = Path.IsPathRooted(_appSettings.ModelPath)
                ? _appSettings.ModelPath
                : Path.Combine(rootDir, _appSettings.ModelPath);

            var config = new LlamaProcessConfig
            {
                ExecutablePath = Path.Combine(rootDir, "runtime", "llama", "llama-server.exe"),
                ModelPath = modelPathAbs,
                Port = _appSettings.Port,
                Host = _appSettings.Host,
                ContextSize = _appSettings.ContextSize,
                Threads = _appSettings.CpuThreads,
                Device = _appSettings.Device,
                EnableJinja = true,
                StartupTimeoutSeconds = 45,
                LogFilePath = Path.Combine(dataDir, "llama_server.log")
            };

            _llamaProcessManager = new LlamaProcessManager(config);
            _llamaProcessManager.StatusChanged += OnLlamaStatusChanged;
            _llamaProcessManager.LogMessageReceived += msg =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_llamaProcessManager != null && _llamaProcessManager.IsReady)
                    {
                        double ram = _llamaProcessManager.GetWorkingSetMegaBytes();
                        ModelMetricsText.Text = $" · {_appSettings.ModelProfile} ({ram:F0} MB)";
                    }
                });
            };

            bool started = await _llamaProcessManager.StartAsync();
            if (started)
            {
                // Initialize Model Client, Tools, Permissions, SQLite Audit, User Memory and Agent
                _modelClient = new LlamaCppClient($"http://{config.Host}:{config.Port}");
                if (!string.IsNullOrWhiteSpace(_appSettings.GeminiApiKey))
                {
                    _geminiClient = new GeminiClient(_appSettings.GeminiApiKey, _appSettings.CloudModelName, _appSettings.CloudFallbackModelName);
                }

                _permissionManager = new PermissionManager(rootDir);
                _permissionManager.SetCustomAllowedPaths(_appSettings.CustomAllowedDirectories);

                string auditDbPath = Path.Combine(rootDir, "data", "audit_trail.db");
                _auditRepository = new SqliteAuditRepository(auditDbPath);
                await _auditRepository.InitializeAsync();

                _toolRegistry = new ToolRegistry();
                _toolRegistry.Register(new ListDirectoryTool(_permissionManager));
                _toolRegistry.Register(new ReadFileTool(_permissionManager));
                _toolRegistry.Register(new MoveOrRenameTool(_permissionManager));
                _toolRegistry.Register(new DeleteFileTool(_permissionManager));
                _toolRegistry.Register(new FetchUrlTextTool());
                _toolRegistry.Register(new WebSearchTool());
                _toolRegistry.Register(new RememberPreferenceTool(_settingsRepository));
                _toolRegistry.Register(new RecallMemoryTool(_settingsRepository));

                _stateMachine = new AgentStateMachine();
                _stateMachine.StateChanged += (oldState, newState, detail) =>
                {
                    Dispatcher.Invoke(() => UpdateVisualState(newState, detail));
                };

                _agentController = new AgentController(_modelClient, _toolRegistry, _permissionManager, _stateMachine, _auditRepository, _settingsRepository, _geminiClient);
                await _agentController.RefreshSystemPromptWithUserMemoriesAsync();
                _agentController.ExecutionTimeout = TimeSpan.FromSeconds(_appSettings.ExecutionTimeoutSeconds);
                _agentController.ConfirmationHandler = OnToolConfirmationRequestedAsync;
                _agentController.CloudConfirmationHandler = OnCloudConfirmationRequestedAsync;
                _agentController.CloudFallbackOccurred += (originalModel, cloudModel) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _trayIconManager?.ShowNotification("Escalation Cloud", $"Passaggio a {cloudModel} nel Cloud per completare la richiesta.");
                    });
                };

                // Wire up speech, tool and multi-step progress events
                _agentController.SpeechReceived += text =>
                {
                    Dispatcher.Invoke(() => SetSpeechText(text));
                };

                _agentController.StepStarted += (stepNum, maxSteps) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StepBadge.Visibility = Visibility.Visible;
                        StepBadgeText.Text = $"Passo {stepNum}/{maxSteps}";
                    });
                };

                _agentController.StepExecuting += (stepNum, toolName, argsJson) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        SetGhostOutfitForTool(toolName);
                        SetSpeechText($"[Passo {stepNum}] Esecuzione tool '{toolName}'...\nArgomenti: {argsJson}");
                    });
                };

                _agentController.ToolExecuting += (toolName, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        SetGhostOutfitForTool(toolName);
                    });
                };

                _agentController.LoopDetected += (toolName, message) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        SetSpeechText($"⚠️ {message}");
                    });
                };

                _agentController.ExecutionCancelled += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StepBadge.Visibility = Visibility.Collapsed;
                        BtnCancelExecution.Visibility = Visibility.Collapsed;
                    });
                };

                _agentController.NetworkAccessOccurred += _ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        NetworkBadge.Visibility = Visibility.Visible;
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                        timer.Tick += (s, e) =>
                        {
                            NetworkBadge.Visibility = Visibility.Collapsed;
                            timer.Stop();
                        };
                        timer.Start();
                    });
                };

                UpdateVisualState(AgentState.Idle, "Pronto");
                string effectiveName = !string.IsNullOrWhiteSpace(_agentController?.AgentCustomName) &&
                                       !_agentController.AgentCustomName.Equals("OpenAgent", StringComparison.OrdinalIgnoreCase)
                    ? _agentController.AgentCustomName
                    : "OpenAgent";

                if (shouldAutoTune)
                {
                    SetSpeechText($"Ciao! Sono {effectiveName}. Sistema ottimizzato per {hardwareSpecs.PhysicalCores} core e {hardwareSpecs.AvailableRamGb:F1} GB RAM ({_appSettings.Device}). Pronto!");
                }
                else
                {
                    SetSpeechText($"Ciao! Sono {effectiveName}. Posso esplorare cartelle, leggere file, effettuare ricerche ed ho a disposizione le tue preferenze salvate!");
                }
                _trayIconManager?.ShowNotification($"{effectiveName} Pronto", $"Modello locale {_appSettings.ModelProfile} attivo ({_appSettings.CpuThreads} thread, {_appSettings.Device}).");
            }
            else
            {
                UpdateVisualState("Error", "Errore avvio");
                SetSpeechText("Non è stato possibile avviare llama-server. Controlla i log in runtime/llama.");
            }
        }
        catch (Exception ex)
        {
            UpdateVisualState("Error", "Errore");
            SetSpeechText($"Errore avvio runtime: {ex.Message}");
        }
    }

    public static string FindProjectRootDir(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenAgent.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "runtime", "llama")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return startDir;
    }

    private void OnLlamaStatusChanged(ServerStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            double? ram = _llamaProcessManager?.GetWorkingSetMegaBytes();
            _trayIconManager?.UpdateModelStatus(status, ram);

            switch (status)
            {
                case ServerStatus.Ready:
                    UpdateVisualState(AgentState.Idle, "In ascolto");
                    break;
                case ServerStatus.LoadingModel:
                    UpdateVisualState("Starting", "Caricamento pesi...");
                    break;
                case ServerStatus.Starting:
                    UpdateVisualState("Starting", "Avvio...");
                    break;
                case ServerStatus.Error:
                    UpdateVisualState("Error", "Errore server");
                    break;
                case ServerStatus.Stopped:
                    UpdateVisualState("Stopped", "Disconnesso");
                    break;
            }
        });
    }

    public void UpdateVisualState(AgentState state, string badge)
    {
        StatusBadgeText.Text = badge;
        var glow = GhostContainer.Effect as DropShadowEffect;

        switch (state)
        {
            case AgentState.Idle:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald Green
                SetGhostBodyColor(Color.FromRgb(14, 165, 233)); // Sky Blue / Cyan Octopus
                ResetGhostEyes();
                HideAllCostumes();
                if (glow != null) glow.Color = Color.FromRgb(14, 165, 233);
                break;

            case AgentState.Thinking:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(168, 85, 247)); // Purple
                SetGhostBodyColor(Color.FromRgb(168, 85, 247)); // Violet / Psionic Octopus
                SetGhostEyesLookingUp();
                SetGhostOutfit("thinking");
                if (glow != null) glow.Color = Color.FromRgb(168, 85, 247);
                break;

            case AgentState.ToolActive:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber / Warning
                SetGhostBodyColor(Color.FromRgb(245, 158, 11)); // Amber / Gold Octopus
                ResetGhostEyes();
                if (glow != null && string.IsNullOrEmpty(_currentActiveOutfit)) glow.Color = Color.FromRgb(245, 158, 11);
                break;

            case AgentState.CloudProcessing:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Google Royal Blue (#2563EB)
                SetGhostBodyColor(Color.FromRgb(30, 58, 138)); // Deep Celestial Indigo / Blue (#1E3A8A)
                SetGhostEyesLookingUp();
                SetGhostOutfit("cloud");
                if (glow != null)
                {
                    glow.Color = Color.FromRgb(59, 130, 246); // Electric Celestial Glow (#3B82F6)
                    glow.BlurRadius = 24;
                }
                break;

            case AgentState.Speaking:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(6, 182, 212)); // Cyan
                SetGhostBodyColor(Color.FromRgb(6, 182, 212)); // Electric Teal Octopus
                ResetGhostEyes();
                SetGhostOutfit("speaking");
                if (glow != null) glow.Color = Color.FromRgb(6, 182, 212);
                break;

            case AgentState.Error:
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                SetGhostBodyColor(Color.FromRgb(239, 68, 68)); // Crimson / Glitch Octopus
                SetGhostEyesError();
                SetGhostOutfit("error");
                if (glow != null) glow.Color = Color.FromRgb(239, 68, 68);
                break;
        }
    }

    private void UpdateVisualState(string state, string badge)
    {
        StatusBadgeText.Text = badge;
        switch (state)
        {
            case "Idle":
                UpdateVisualState(AgentState.Idle, badge);
                break;
            case "Thinking":
                UpdateVisualState(AgentState.Thinking, badge);
                break;
            case "Starting":
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(234, 179, 8)); // Yellow
                HideAllCostumes();
                break;
            case "Error":
                UpdateVisualState(AgentState.Error, badge);
                break;
            case "Stopped":
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(107, 114, 128)); // Gray
                HideAllCostumes();
                break;
        }
    }

    public void HideAllCostumes()
    {
        _currentActiveOutfit = null;
        _grimoireTimer?.Stop();
        _speechMouthTimer?.Stop();

        if (GhostMouth != null) GhostMouth.Visibility = Visibility.Collapsed;
        if (LayerGrimoire != null) LayerGrimoire.Visibility = Visibility.Collapsed;
        if (LayerScryingOrb != null) LayerScryingOrb.Visibility = Visibility.Collapsed;
        if (LayerReadFile != null) LayerReadFile.Visibility = Visibility.Collapsed;
        if (LayerExplorer != null) LayerExplorer.Visibility = Visibility.Collapsed;
        if (LayerCourier != null) LayerCourier.Visibility = Visibility.Collapsed;
        if (LayerCauldron != null) LayerCauldron.Visibility = Visibility.Collapsed;
        if (LayerMemoryGem != null) LayerMemoryGem.Visibility = Visibility.Collapsed;
        if (LayerRecallMemory != null) LayerRecallMemory.Visibility = Visibility.Collapsed;
        if (LayerThinking != null) LayerThinking.Visibility = Visibility.Collapsed;
        if (LayerConfirmation != null) LayerConfirmation.Visibility = Visibility.Collapsed;
        if (LayerError != null) LayerError.Visibility = Visibility.Collapsed;
        if (LayerDragDrop != null) LayerDragDrop.Visibility = Visibility.Collapsed;
        if (LayerCloud != null) LayerCloud.Visibility = Visibility.Collapsed;
        if (LayerHeadset != null) LayerHeadset.Visibility = Visibility.Collapsed;
    }

    public void SetGhostOutfitForTool(string toolName)
    {
        HideAllCostumes();
        _currentActiveOutfit = toolName;
        var glow = GhostContainer?.Effect as DropShadowEffect;

        switch (toolName.ToLowerInvariant())
        {
            case "search_web":
                if (LayerGrimoire != null) LayerGrimoire.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(99, 102, 241); // Indigo
                StartGrimoirePageFlipAnimation();
                break;

            case "fetch_url_text":
                if (LayerScryingOrb != null) LayerScryingOrb.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(6, 182, 212); // Electric Cyan
                break;

            case "read_file":
                if (LayerReadFile != null) LayerReadFile.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(16, 185, 129); // Emerald
                break;

            case "list_directory":
                if (LayerExplorer != null) LayerExplorer.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(245, 158, 11); // Amber
                break;

            case "move_or_rename":
            case "move_rename_file":
                if (LayerCourier != null) LayerCourier.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(139, 92, 246); // Purple
                break;

            case "delete_file":
                if (LayerCauldron != null) LayerCauldron.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(225, 29, 72); // Rose/Crimson
                break;

            case "remember_user_preference":
                if (LayerMemoryGem != null) LayerMemoryGem.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(236, 72, 153); // Pink
                break;

            case "recall_user_memory":
                if (LayerRecallMemory != null) LayerRecallMemory.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(234, 179, 8); // Gold
                break;

            default:
                if (LayerThinking != null) LayerThinking.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(245, 158, 11);
                break;
        }
    }

    public void SetGhostOutfit(string outfitName)
    {
        HideAllCostumes();
        _currentActiveOutfit = outfitName;
        var glow = GhostContainer?.Effect as DropShadowEffect;

        switch (outfitName.ToLowerInvariant())
        {
            case "thinking":
                if (LayerThinking != null) LayerThinking.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(168, 85, 247);
                break;

            case "speaking":
                if (GhostMouth != null) GhostMouth.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(6, 182, 212);
                StartSpeechMouthAnimation();
                break;

            case "confirmation":
                if (LayerConfirmation != null) LayerConfirmation.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(234, 179, 8);
                break;

            case "error":
                if (LayerError != null) LayerError.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(239, 68, 68);
                break;

            case "dragdrop":
                if (LayerDragDrop != null) LayerDragDrop.Visibility = Visibility.Visible;
                if (glow != null) glow.Color = Color.FromRgb(168, 85, 247);
                break;

            case "cloud":
                if (LayerCloud != null) LayerCloud.Visibility = Visibility.Visible;
                if (glow != null)
                {
                    glow.Color = Color.FromRgb(59, 130, 246);
                    glow.BlurRadius = 24;
                }
                break;

            case "listening":
                if (LayerHeadset != null) LayerHeadset.Visibility = Visibility.Visible;
                if (glow != null)
                {
                    glow.Color = Color.FromRgb(239, 68, 68); // Red recording glow
                    glow.BlurRadius = 22;
                }
                break;
        }
    }

    private void StartGrimoirePageFlipAnimation()
    {
        _grimoireTimer?.Stop();
        _grimoireFrame = 0;
        _grimoireTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _grimoireTimer.Tick += (s, e) =>
        {
            _grimoireFrame = (_grimoireFrame + 1) % 4;
            if (GrimoireFlipA != null && GrimoireFlipB != null)
            {
                GrimoireFlipA.Visibility = (_grimoireFrame % 2 == 0) ? Visibility.Visible : Visibility.Collapsed;
                GrimoireFlipB.Visibility = (_grimoireFrame % 2 == 1) ? Visibility.Visible : Visibility.Collapsed;
            }
            if (GrimoireSparkle1 != null && GrimoireSparkle2 != null && GrimoireSparkle3 != null)
            {
                GrimoireSparkle1.Opacity = (_grimoireFrame % 3 == 0) ? 1.0 : 0.3;
                GrimoireSparkle2.Opacity = (_grimoireFrame % 3 == 1) ? 1.0 : 0.3;
                GrimoireSparkle3.Opacity = (_grimoireFrame % 3 == 2) ? 1.0 : 0.3;
            }
        };
        _grimoireTimer.Start();
    }

    private void StartSpeechMouthAnimation()
    {
        _speechMouthTimer?.Stop();
        _mouthOpen = true;
        if (GhostMouth != null)
        {
            GhostMouth.Visibility = Visibility.Visible;
            GhostMouth.Height = 1;
        }

        _speechMouthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _speechMouthTimer.Tick += (s, e) =>
        {
            _mouthOpen = !_mouthOpen;
            if (GhostMouth != null)
            {
                GhostMouth.Height = _mouthOpen ? 2 : 1;
            }
        };
        _speechMouthTimer.Start();
    }

    private static Color AdjustBrightness(Color color, double factor)
    {
        byte r = (byte)Math.Clamp((int)(color.R * factor), 0, 255);
        byte g = (byte)Math.Clamp((int)(color.G * factor), 0, 255);
        byte b = (byte)Math.Clamp((int)(color.B * factor), 0, 255);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static Color BlendWithWhite(Color color, double whiteRatio)
    {
        byte r = (byte)Math.Clamp((int)(color.R + (255 - color.R) * whiteRatio), 0, 255);
        byte g = (byte)Math.Clamp((int)(color.G + (255 - color.G) * whiteRatio), 0, 255);
        byte b = (byte)Math.Clamp((int)(color.B + (255 - color.B) * whiteRatio), 0, 255);
        return Color.FromArgb(color.A, r, g, b);
    }

    private void ResetGhostEyes()
    {
        GhostLeftEye.Height = 2;
        GhostRightEye.Height = 2;
        Canvas.SetTop(GhostLeftEye, 11);
        Canvas.SetTop(GhostRightEye, 11);
        Canvas.SetLeft(GhostLeftEye, 8);
        Canvas.SetLeft(GhostRightEye, 14);
        GhostLeftEye.Fill = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        GhostRightEye.Fill = new SolidColorBrush(Color.FromRgb(15, 23, 42));

        GhostLeftTwinkle.Visibility = Visibility.Visible;
        GhostRightTwinkle.Visibility = Visibility.Visible;
        Canvas.SetTop(GhostLeftTwinkle, 10);
        Canvas.SetTop(GhostRightTwinkle, 10);
        Canvas.SetLeft(GhostLeftTwinkle, 7);
        Canvas.SetLeft(GhostRightTwinkle, 16);
    }

    private void SetGhostEyesLookingUp()
    {
        GhostLeftEye.Height = 2;
        GhostRightEye.Height = 2;
        Canvas.SetTop(GhostLeftEye, 10);
        Canvas.SetTop(GhostRightEye, 10);
        Canvas.SetLeft(GhostLeftEye, 8);
        Canvas.SetLeft(GhostRightEye, 14);
        GhostLeftEye.Fill = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        GhostRightEye.Fill = new SolidColorBrush(Color.FromRgb(15, 23, 42));

        GhostLeftTwinkle.Visibility = Visibility.Visible;
        GhostRightTwinkle.Visibility = Visibility.Visible;
        Canvas.SetTop(GhostLeftTwinkle, 10);
        Canvas.SetTop(GhostRightTwinkle, 10);
        Canvas.SetLeft(GhostLeftTwinkle, 7);
        Canvas.SetLeft(GhostRightTwinkle, 16);
    }

    private void SetGhostEyesError()
    {
        GhostLeftEye.Height = 1;
        GhostRightEye.Height = 1;
        Canvas.SetTop(GhostLeftEye, 11);
        Canvas.SetTop(GhostRightEye, 11);
        GhostLeftEye.Fill = new SolidColorBrush(Color.FromRgb(153, 27, 27));
        GhostRightEye.Fill = new SolidColorBrush(Color.FromRgb(153, 27, 27));

        GhostLeftTwinkle.Visibility = Visibility.Collapsed;
        GhostRightTwinkle.Visibility = Visibility.Collapsed;
    }

    private void SetGhostBodyColor(Color baseColor)
    {
        var mainBrush = new SolidColorBrush(baseColor);
        var shadowBrush = new SolidColorBrush(AdjustBrightness(baseColor, 0.78));
        var deepShadowBrush = new SolidColorBrush(AdjustBrightness(baseColor, 0.58));
        var highlightBrush = new SolidColorBrush(BlendWithWhite(baseColor, 0.35));
        var suckerBrush = new SolidColorBrush(BlendWithWhite(baseColor, 0.75));

        // Mantle main
        if (OctoDome1 != null) OctoDome1.Fill = mainBrush;
        if (OctoDome2 != null) OctoDome2.Fill = mainBrush;
        if (OctoDome3 != null) OctoDome3.Fill = mainBrush;
        if (OctoBodyMain != null) OctoBodyMain.Fill = mainBrush;
        if (OctoDomeBase != null) OctoDomeBase.Fill = mainBrush;

        // Mantle highlights & shadows
        if (OctoHighlight1 != null) OctoHighlight1.Fill = highlightBrush;
        if (OctoHighlight2 != null) OctoHighlight2.Fill = highlightBrush;
        if (OctoShadow1 != null) OctoShadow1.Fill = shadowBrush;
        if (OctoShadow2 != null) OctoShadow2.Fill = shadowBrush;
        if (OctoBrowLeft != null) OctoBrowLeft.Fill = shadowBrush;
        if (OctoBrowRight != null) OctoBrowRight.Fill = shadowBrush;

        // Siphon
        if (OctoSiphon != null) OctoSiphon.Fill = shadowBrush;
        if (OctoSiphonShadow != null) OctoSiphonShadow.Fill = deepShadowBrush;

        // Outer tentacles
        if (OctoT1A != null) OctoT1A.Fill = mainBrush;
        if (OctoT1B != null) OctoT1B.Fill = mainBrush;
        if (OctoT1Tip != null) OctoT1Tip.Fill = shadowBrush;
        if (OctoS1 != null) OctoS1.Fill = suckerBrush;

        if (OctoT2A != null) OctoT2A.Fill = mainBrush;
        if (OctoT2Tip != null) OctoT2Tip.Fill = shadowBrush;
        if (OctoS2 != null) OctoS2.Fill = suckerBrush;

        if (OctoT3A != null) OctoT3A.Fill = mainBrush;
        if (OctoT3Tip != null) OctoT3Tip.Fill = deepShadowBrush;
        if (OctoS3 != null) OctoS3.Fill = suckerBrush;

        // Front tentacles (shadowed depth)
        if (OctoT4A != null) OctoT4A.Fill = shadowBrush;
        if (OctoT4Tip != null) OctoT4Tip.Fill = deepShadowBrush;
        if (OctoS4A != null) OctoS4A.Fill = suckerBrush;
        if (OctoS4B != null) OctoS4B.Fill = suckerBrush;

        if (OctoT5A != null) OctoT5A.Fill = shadowBrush;
        if (OctoT5Tip != null) OctoT5Tip.Fill = deepShadowBrush;
        if (OctoS5A != null) OctoS5A.Fill = suckerBrush;
        if (OctoS5B != null) OctoS5B.Fill = suckerBrush;

        if (OctoT6A != null) OctoT6A.Fill = mainBrush;
        if (OctoT6Tip != null) OctoT6Tip.Fill = deepShadowBrush;
        if (OctoS6 != null) OctoS6.Fill = suckerBrush;

        if (OctoT7A != null) OctoT7A.Fill = mainBrush;
        if (OctoT7Tip != null) OctoT7Tip.Fill = shadowBrush;
        if (OctoS7 != null) OctoS7.Fill = suckerBrush;

        if (OctoT8A != null) OctoT8A.Fill = mainBrush;
        if (OctoT8B != null) OctoT8B.Fill = mainBrush;
        if (OctoT8Tip != null) OctoT8Tip.Fill = shadowBrush;
        if (OctoS8 != null) OctoS8.Fill = suckerBrush;
    }

    private void SetSpeechText(string text)
    {
        SpeechText.Text = text;
        if (BalloonBorder.Visibility != Visibility.Visible)
        {
            SetBalloonVisibility(true);
        }
    }

    private async Task<bool> OnToolConfirmationRequestedAsync(ToolConfirmationRequest req)
    {
        return await Dispatcher.InvokeAsync(async () =>
        {
            _pendingConfirmationTcs = new TaskCompletionSource<bool>();

            bool isDestructive = req.RiskLevel == ToolRiskLevel.Destructive;
            ConfirmationBadgeText.Text = isDestructive
                ? "⚠️ CONFERMA OPERAZIONE DISTRUTTIVA"
                : "⚠️ CONFERMA OPERAZIONE DI MODIFICA";
            ConfirmationBadgeText.Foreground = isDestructive
                ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) // Red
                : new SolidColorBrush(Color.FromRgb(253, 224, 71));  // Yellow

            ConfirmationPanel.Background = isDestructive
                ? new SolidColorBrush(Color.FromRgb(42, 24, 16))
                : new SolidColorBrush(Color.FromRgb(38, 30, 22));

            BtnConfirmAccept.Background = isDestructive
                ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                : new SolidColorBrush(Color.FromRgb(234, 179, 8));

            ConfirmationPromptText.Text =
                $"Tool: {req.ToolName}\n" +
                $"Livello: {req.RiskLevel}\n" +
                $"Parametri: {req.ArgumentsJson}";

            ConfirmationPanel.Visibility = Visibility.Visible;
            SetGhostOutfit("confirmation");
            if (BalloonBorder.Visibility != Visibility.Visible)
            {
                SetBalloonVisibility(true);
            }

            return await _pendingConfirmationTcs.Task;
        }).Task.Unwrap();
    }

    private async Task<bool> OnCloudConfirmationRequestedAsync(string reason)
    {
        return await Dispatcher.InvokeAsync(async () =>
        {
            _pendingConfirmationTcs = new TaskCompletionSource<bool>();

            ConfirmationBadgeText.Text = "☁️ CONFERMA ESCALATION CLOUD";
            ConfirmationBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)); // Sky Blue

            ConfirmationPanel.Background = new SolidColorBrush(Color.FromRgb(24, 34, 56)); // Deep Navy
            BtnConfirmAccept.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Google Blue

            ConfirmationPromptText.Text = $"{reason}\n\nNota Privacy: il prompt e il contesto della conversazione verranno inviati ai server Google (Gemini 3.8 Flash).";

            ConfirmationPanel.Visibility = Visibility.Visible;
            SetGhostOutfit("cloud");
            if (BalloonBorder.Visibility != Visibility.Visible)
            {
                SetBalloonVisibility(true);
            }

            return await _pendingConfirmationTcs.Task;
        }).Task.Unwrap();
    }

    private void OnConfirmAcceptClicked(object sender, RoutedEventArgs e)
    {
        ConfirmationPanel.Visibility = Visibility.Collapsed;
        HideAllCostumes();
        _pendingConfirmationTcs?.TrySetResult(true);
    }

    private void OnConfirmRejectClicked(object sender, RoutedEventArgs e)
    {
        ConfirmationPanel.Visibility = Visibility.Collapsed;
        HideAllCostumes();
        _pendingConfirmationTcs?.TrySetResult(false);
    }

    private void OnGlobalHotKeyPressed()
    {
        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }

        SetBalloonVisibility(true);
    }

    private void ToggleWindowVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Visibility = Visibility.Collapsed;
        }
        else
        {
            Visibility = Visibility.Visible;
            Activate();
            PromptInput.Focus();
        }
    }

    private void SetClickThroughState(bool enable)
    {
        _isClickThroughEnabled = enable;
        NativeMethods.SetClickThrough(_hwnd, enable);

        ClickThroughIconText.Text = enable ? "🔒" : "🔓";
        ClickThroughIconText.Foreground = enable
            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
            : new SolidColorBrush(Color.FromRgb(156, 163, 175));

        if (enable)
        {
            _trayIconManager?.ShowNotification(
                "Click-Through Attivato",
                "I click del mouse attraversano l'assistente. Usa Ctrl+Space per riprendere il controllo.");
        }
    }

    private void OnClickThroughToggleClicked(object sender, RoutedEventArgs e)
    {
        SetClickThroughState(!_isClickThroughEnabled);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (!_isLoaded || _isDragging) return;

        if (sizeInfo.PreviousSize.Width > 0 && sizeInfo.PreviousSize.Height > 0)
        {
            double deltaW = sizeInfo.NewSize.Width - sizeInfo.PreviousSize.Width;
            double deltaH = sizeInfo.NewSize.Height - sizeInfo.PreviousSize.Height;

            if (_isBalloonToLeft && Math.Abs(deltaW) > 0.001)
            {
                Left -= deltaW;
            }
            if (_isBalloonAbove && Math.Abs(deltaH) > 0.001)
            {
                Top -= deltaH;
            }
        }
    }

    private void OnGhostMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _ghostDragStartPoint = e.GetPosition(this);
            _isGhostDragging = false;
            GhostContainer.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnGhostMouseMove(object sender, MouseEventArgs e)
    {
        if (_ghostDragStartPoint.HasValue && e.LeftButton == MouseButtonState.Pressed && !_isGhostDragging)
        {
            Point current = e.GetPosition(this);
            Vector diff = current - _ghostDragStartPoint.Value;
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isGhostDragging = true;
                _isDragging = true;
                if (GhostContainer.IsMouseCaptured)
                {
                    GhostContainer.ReleaseMouseCapture();
                }
                _ghostDragStartPoint = null;
                try
                {
                    DragMove();
                }
                finally
                {
                    _isDragging = false;
                    _isGhostDragging = false;
                    UpdateBalloonPlacement();
                }
            }
        }
    }

    private void OnGhostMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (GhostContainer.IsMouseCaptured)
        {
            GhostContainer.ReleaseMouseCapture();
        }

        if (_ghostDragStartPoint.HasValue && !_isGhostDragging)
        {
            _ghostDragStartPoint = null;
            ToggleBalloon();
            e.Handled = true;
        }
        else
        {
            _ghostDragStartPoint = null;
            _isGhostDragging = false;
        }
    }

    private Rect GetCurrentWorkArea()
    {
        try
        {
            var screen = _hwnd != IntPtr.Zero
                ? System.Windows.Forms.Screen.FromHandle(_hwnd)
                : System.Windows.Forms.Screen.PrimaryScreen;

            if (screen != null)
            {
                var source = PresentationSource.FromVisual(this);
                double scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                return new Rect(
                    screen.WorkingArea.Left / scaleX,
                    screen.WorkingArea.Top / scaleY,
                    screen.WorkingArea.Width / scaleX,
                    screen.WorkingArea.Height / scaleY
                );
            }
        }
        catch
        {
        }

        return SystemParameters.WorkArea;
    }

    public void UpdateBalloonPlacement()
    {
        if (!_isLoaded || GhostRowGrid == null || BalloonBorder == null) return;

        Point ghostScreen;
        double scaleX = 1.0;
        double scaleY = 1.0;
        try
        {
            Point p = GhostContainer.PointToScreen(new Point(0, 0));
            var source = PresentationSource.FromVisual(this);
            scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            ghostScreen = new Point(p.X / scaleX, p.Y / scaleY);
        }
        catch
        {
            ghostScreen = new Point(Left, Top);
        }

        var workArea = GetCurrentWorkArea();

        double estimatedBalloonHeight = BalloonBorder.ActualHeight > 0 ? BalloonBorder.ActualHeight : 260;
        double balloonWidth = 380;

        double spaceAbove = ghostScreen.Y - workArea.Top;
        double spaceBelow = workArea.Bottom - (ghostScreen.Y + GhostVisualSize);

        double spaceToLeft = (ghostScreen.X + GhostVisualSize) - workArea.Left;
        double spaceToRight = workArea.Right - ghostScreen.X;

        bool placeAbove;
        if (spaceAbove >= estimatedBalloonHeight + 20)
        {
            placeAbove = true;
        }
        else if (spaceBelow >= estimatedBalloonHeight + 20)
        {
            placeAbove = false;
        }
        else
        {
            placeAbove = spaceAbove >= spaceBelow;
        }

        bool placeToLeft;
        if (spaceToLeft < balloonWidth + 20 && spaceToRight >= balloonWidth + 20)
        {
            placeToLeft = false;
        }
        else if (spaceToRight < balloonWidth + 20 && spaceToLeft >= balloonWidth + 20)
        {
            placeToLeft = true;
        }
        else
        {
            placeToLeft = (ghostScreen.X + GhostVisualSize / 2) >= (workArea.Left + workArea.Width / 2);
        }

        _isBalloonAbove = placeAbove;
        _isBalloonToLeft = placeToLeft;

        // Apply vertical layout
        if (placeAbove)
        {
            Grid.SetRow(BalloonBorder, 0);
            Grid.SetRow(GhostRowGrid, 1);
            BalloonBorder.Margin = new Thickness(0, 0, 0, 10);
        }
        else
        {
            Grid.SetRow(GhostRowGrid, 0);
            Grid.SetRow(BalloonBorder, 1);
            BalloonBorder.Margin = new Thickness(0, 10, 0, 0);
        }

        // Apply horizontal layout
        if (placeToLeft)
        {
            double rightInset = Math.Max(0, Math.Min(16, workArea.Right - (ghostScreen.X + GhostVisualSize)));
            GhostRowGrid.HorizontalAlignment = HorizontalAlignment.Right;
            GhostRowGrid.Margin = new Thickness(0, 0, rightInset, 0);
        }
        else
        {
            double leftInset = Math.Max(0, Math.Min(16, ghostScreen.X - workArea.Left));
            GhostRowGrid.HorizontalAlignment = HorizontalAlignment.Left;
            GhostRowGrid.Margin = new Thickness(leftInset, 0, 0, 0);
        }

        UpdateLayout();

        // Restore ghost screen position
        try
        {
            Point newP = GhostContainer.PointToScreen(new Point(0, 0));
            Point newGhostScreen = new Point(newP.X / scaleX, newP.Y / scaleY);

            double diffX = newGhostScreen.X - ghostScreen.X;
            double diffY = newGhostScreen.Y - ghostScreen.Y;

            if (Math.Abs(diffX) > 0.001) Left -= diffX;
            if (Math.Abs(diffY) > 0.001) Top -= diffY;
        }
        catch
        {
        }
    }

    public void SetBalloonVisibility(bool visible)
    {
        var newVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (BalloonBorder.Visibility == newVisibility) return;

        Point? ghostPosBefore = null;
        if (_isLoaded && GhostContainer.IsVisible)
        {
            try
            {
                ghostPosBefore = GhostContainer.PointToScreen(new Point(0, 0));
            }
            catch
            {
            }
        }

        if (visible)
        {
            UpdateBalloonPlacement();
        }

        BalloonBorder.Visibility = newVisibility;
        UpdateLayout();

        if (ghostPosBefore.HasValue)
        {
            try
            {
                Point ghostPosAfter = GhostContainer.PointToScreen(new Point(0, 0));
                double diffX = ghostPosAfter.X - ghostPosBefore.Value.X;
                double diffY = ghostPosAfter.Y - ghostPosBefore.Value.Y;

                var source = PresentationSource.FromVisual(this);
                double scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                if (Math.Abs(diffX) >= 0.5)
                {
                    Left -= diffX / scaleX;
                }
                if (Math.Abs(diffY) >= 0.5)
                {
                    Top -= diffY / scaleY;
                }
            }
            catch
            {
            }
        }

        if (visible)
        {
            Activate();
            PromptInput.Focus();
        }
    }

    public void ToggleBalloon()
    {
        SetBalloonVisibility(BalloonBorder.Visibility != Visibility.Visible);
    }

    private void OnGhostClicked(object sender, MouseButtonEventArgs e)
    {
        ToggleBalloon();
    }

    private void OnHideBalloonClicked(object sender, RoutedEventArgs e)
    {
        SetBalloonVisibility(false);
    }

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _isDragging = true;
            try
            {
                DragMove();
            }
            finally
            {
                _isDragging = false;
                UpdateBalloonPlacement();
            }
        }
    }

    private void OnWindowDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            DropZoneOverlay.Visibility = Visibility.Visible;
            SetGhostOutfit("dragdrop");
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindowDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            DropZoneOverlay.Visibility = Visibility.Visible;
            if (_currentActiveOutfit != "dragdrop")
            {
                SetGhostOutfit("dragdrop");
            }
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropZoneOverlay.Visibility = Visibility.Collapsed;
        HideAllCostumes();
    }

    private void OnWindowDrop(object sender, System.Windows.DragEventArgs e)
    {
        DropZoneOverlay.Visibility = Visibility.Collapsed;
        HideAllCostumes();

        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            string[]? items = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (items != null && items.Length > 0)
            {
                if (items.Length == 1)
                {
                    string path = items[0];
                    if (Directory.Exists(path))
                    {
                        PromptInput.Text = $"Elenca i file nella cartella \"{path}\" ed esamina la struttura.";
                    }
                    else
                    {
                        PromptInput.Text = $"Leggi ed esamina il file \"{path}\".";
                    }
                }
                else
                {
                    PromptInput.Text = $"Esamina i seguenti percorsi: \"{string.Join("\", \"", items)}\".";
                }

                SetBalloonVisibility(true);
                PromptInput.CaretIndex = PromptInput.Text.Length;
            }
        }
    }

    private void OnPromptInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitPrompt();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (BtnCancelExecution.Visibility == Visibility.Visible)
            {
                OnCancelExecutionClicked(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else
            {
                SetBalloonVisibility(false);
                e.Handled = true;
            }
        }
    }

    private void OnVoiceInputClicked(object sender, RoutedEventArgs e)
    {
        ToggleVoiceRecording();
    }

    private async void ToggleVoiceRecording()
    {
        if (_voiceInputService == null)
        {
            SetSpeechText("Servizio vocale non inizializzato.");
            return;
        }

        if (!_voiceInputService.IsRecording)
        {
            if (!_voiceInputService.IsModelAvailable())
            {
                SetSpeechText("Scaricamento del modello Whisper locale (75 MB)...");
                bool ok = await _voiceInputService.EnsureModelAvailableAsync();
                if (!ok) return;
            }

            bool started = _voiceInputService.StartRecording();
            if (started)
            {
                StatusBadgeText.Text = "🎙️ In ascolto...";
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red recording dot
                BtnVoiceInput.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red active button
                VoiceButtonIcon.Text = "⏹";
                BtnVoiceInput.ToolTip = "Ferma registrazione e invia (Ctrl+Shift+V)";

                SetGhostOutfit("listening");
                if (BalloonBorder.Visibility != Visibility.Visible)
                {
                    SetBalloonVisibility(true);
                }
                SetSpeechText("🎙️ Ti ascolto... Parla pure al microfono, poi premi nuovamente Ctrl+Shift+V o clicca ⏹ per inviare.");
            }
        }
        else
        {
            // Stop recording & transcribe
            StatusBadgeText.Text = "🎙️ Trascrizione...";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
            BtnVoiceInput.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            VoiceButtonIcon.Text = "🎙️";
            BtnVoiceInput.ToolTip = "Parla con l'avatar (Ctrl+Shift+V)";

            SetGhostOutfit("thinking");
            SetSpeechText("Elaborazione del parlato in corso con Whisper locale...");

            string transcription = await _voiceInputService.StopRecordingAndTranscribeAsync();

            StatusBadgeText.Text = "In ascolto";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green dot

            if (!string.IsNullOrWhiteSpace(transcription))
            {
                PromptInput.Text = transcription;
                SetGhostOutfit("idle");
                // Invia automaticamente la richiesta trascritta all'agente
                SubmitPrompt();
            }
            else
            {
                SetGhostOutfit("idle");
                SetSpeechText("Nessun audio o voce rilevata. Riprova tenendo premuto o parlando vicino al microfono.");
            }
        }
    }

    private void OnSendPromptClicked(object sender, RoutedEventArgs e)
    {
        SubmitPrompt();
    }

    private void OnCancelExecutionClicked(object sender, RoutedEventArgs e)
    {
        _agentController?.CancelCurrentExecution();
        SetSpeechText("Richiesta di annullamento inviata all'agente...");
        BtnCancelExecution.IsEnabled = false;
    }

    private async void SubmitPrompt()
    {
        string text = PromptInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (_agentController == null)
        {
            SetSpeechText("L'agente non è ancora pronto o si sta avviando. Attendi qualche istante.");
            return;
        }

        var controller = _agentController;

        PromptInput.Clear();
        PromptInput.IsEnabled = false;
        BtnSendPrompt.IsEnabled = false;
        BtnCancelExecution.IsEnabled = true;
        BtnCancelExecution.Visibility = Visibility.Visible;

        try
        {
            var suggestion = ModelTaskRouter.AnalyzePrompt(text, _appSettings.ModelProfile);
            bool forceCloud = false;
            bool hasApiKey = !string.IsNullOrWhiteSpace(_appSettings.GeminiApiKey);

            if (hasApiKey)
            {
                if (_geminiClient == null)
                {
                    _geminiClient = new GeminiClient(_appSettings.GeminiApiKey, _appSettings.CloudModelName, _appSettings.CloudFallbackModelName);
                }
                if (controller.CloudProvider == null)
                {
                    controller.CloudProvider = _geminiClient;
                }
            }

            if (suggestion.IsCloud)
            {
                if (!hasApiKey)
                {
                    _trayIconManager?.ShowNotification("Google Gemini Cloud", "Richiesta complessa o richiesta Cloud rilevata. Inserisci la tua API Key nelle Impostazioni per delegarla a Gemini 3.8 Flash.");
                    SetSpeechText($"Hai chiesto: \"{text}\"\n\n💡 Suggerimento: Questa richiesta è complessa o richiede il Cloud. Per delegarla a Google Gemini 3.8 Flash, inserisci la tua API Key nelle Impostazioni (⚙️).\n\nProcedo con il modello locale...");
                    await Task.Delay(1400);
                }
                else if (_appSettings.AutoCloudOnComplexity || _appSettings.EnableCloudEscalation || suggestion.Reason.Contains("esplicita", StringComparison.OrdinalIgnoreCase))
                {
                    // Privacy-First: Richiedi sempre la conferma prima di inviare i dati al cloud
                    string confirmReason = suggestion.Reason.Contains("esplicita", StringComparison.OrdinalIgnoreCase)
                        ? "Hai richiesto l'impiego del modello Cloud. Desideri delegare l'elaborazione a Google Gemini 3.8 Flash?"
                        : "Rilevata richiesta ad alta complessità (ragionamento avanzato / programmazione / contesto esteso). Desideri elaborarla tramite Google Gemini 3.8 Flash nel Cloud?";

                    bool userConfirmed = await OnCloudConfirmationRequestedAsync(confirmReason);
                    if (userConfirmed)
                    {
                        forceCloud = true;
                    }
                }
            }
            else if (suggestion.ShouldSwitch)
            {
                _trayIconManager?.ShowNotification("Suggerimento Modello", suggestion.Reason);
            }

            if (forceCloud)
            {
                SetSpeechText($"Hai chiesto: \"{text}\"\n\n☁️ Elaborazione con Google Gemini 3.8 Flash nel Cloud...");
            }
            else
            {
                if (_llamaProcessManager == null || !_llamaProcessManager.IsReady)
                {
                    SetSpeechText("Il modello locale non è ancora pronto o si sta avviando. Attendi qualche istante.");
                    return;
                }
                SetSpeechText($"Hai chiesto: \"{text}\"\n\nElaborazione in corso con modello locale...");
            }

            string reply = await controller.ProcessPromptAsync(text, forceCloud);

            if (suggestion.ShouldSwitch)
            {
                reply = $"💡 Suggerimento: {suggestion.Reason}\n(Clicca sul nome del modello in alto per cambiare profilo al volo)\n\n" + reply;
            }

            SetSpeechText(reply);
        }
        catch (Exception ex)
        {
            SetSpeechText($"Errore elaborazione richiesta: {ex.Message}");
        }
        finally
        {
            PromptInput.IsEnabled = true;
            BtnSendPrompt.IsEnabled = true;
            BtnCancelExecution.Visibility = Visibility.Collapsed;
            StepBadge.Visibility = Visibility.Collapsed;
            PromptInput.Focus();
        }
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _blinkTimer?.Stop();
        _hotKeyManager?.Dispose();
        _voiceInputService?.Dispose();
        _trayIconManager?.Dispose();
        _modelClient?.Dispose();
        _geminiClient?.Dispose();

        if (_llamaProcessManager != null)
        {
            await _llamaProcessManager.StopAsync();
            _llamaProcessManager.Dispose();
        }
    }

    private async void OnQuickModelProfileClicked(object sender, RoutedEventArgs e)
    {
        string nextProfile = _appSettings.ModelProfile switch
        {
            AppSettings.ProfileCoder15B => AppSettings.ProfileQwen3B,
            AppSettings.ProfileQwen3B => AppSettings.ProfileGemma2B,
            AppSettings.ProfileGemma2B => AppSettings.DefaultProfile17B,
            AppSettings.DefaultProfile17B => AppSettings.ProfileCoder15B,
            _ => AppSettings.ProfileCoder15B
        };

        await SwitchModelProfileAsync(nextProfile);
    }

    public async Task<bool> SwitchModelProfileAsync(string newProfile, string? newModelPath = null)
    {
        string path = newModelPath ?? (newProfile switch
        {
            AppSettings.ProfileCoder15B => AppSettings.DefaultModelPathCoder15B,
            AppSettings.ProfileQwen3B => AppSettings.DefaultModelPathQwen3B,
            AppSettings.ProfileGemma2B => AppSettings.DefaultModelPathGemma2B,
            AppSettings.DefaultProfile17B => AppSettings.DefaultModelPath17B,
            _ => _appSettings.ModelPath
        });

        string rootDir = FindProjectRootDir(AppContext.BaseDirectory);
        string absPath = Path.IsPathRooted(path) ? path : Path.Combine(rootDir, path);

        if (!File.Exists(absPath))
        {
            SetSpeechText($"Pesi modello non trovati per {newProfile}: {path}");
            return false;
        }

        _appSettings.ModelProfile = newProfile;
        _appSettings.ModelPath = path;

        if (_settingsRepository != null)
        {
            await _settingsRepository.SaveSettingsAsync(_appSettings);
        }

        SetSpeechText($"Hot-swap modello in corso: passaggio a '{newProfile}'...");
        UpdateVisualState("Starting", "Cambio modello...");

        if (_llamaProcessManager != null)
        {
            try
            {
                bool ok = await _llamaProcessManager.SwitchModelAsync(absPath);
                if (ok)
                {
                    double ram = _llamaProcessManager.GetWorkingSetMegaBytes();
                    ModelMetricsText.Text = $" · {newProfile} ({ram:F0} MB)";
                    UpdateVisualState(AgentState.Idle, "In ascolto");
                    SetSpeechText($"Modello attivo: {newProfile}. Pronto!");
                    return true;
                }
            }
            catch (Exception ex)
            {
                UpdateVisualState("Error", "Errore swap");
                SetSpeechText($"Errore hot-swap modello: {ex.Message}");
                return false;
            }
        }

        UpdateVisualState("Error", "Errore swap");
        return false;
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    public void OpenSettings()
    {
        if (_settingsRepository == null || _permissionManager == null)
        {
            SetSpeechText("Inizializzazione in corso. Riprova tra un istante.");
            return;
        }

        var settingsWindow = new SettingsWindow(_settingsRepository, _permissionManager, _appSettings, OnSettingsApplied);
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    private async void OnSettingsApplied(AppSettings newSettings)
    {
        bool modelOrHardwareChanged =
            !string.Equals(newSettings.ModelPath, _appSettings.ModelPath, StringComparison.OrdinalIgnoreCase) ||
            newSettings.Port != _appSettings.Port ||
            !string.Equals(newSettings.Host, _appSettings.Host, StringComparison.OrdinalIgnoreCase) ||
            newSettings.CpuThreads != _appSettings.CpuThreads ||
            newSettings.ContextSize != _appSettings.ContextSize ||
            !string.Equals(newSettings.Device, _appSettings.Device, StringComparison.OrdinalIgnoreCase);

        _appSettings = newSettings;

        if (!string.IsNullOrWhiteSpace(_appSettings.GeminiApiKey))
        {
            _geminiClient?.Dispose();
            _geminiClient = new GeminiClient(_appSettings.GeminiApiKey, _appSettings.CloudModelName, _appSettings.CloudFallbackModelName);
            if (_agentController != null)
            {
                _agentController.CloudProvider = _geminiClient;
            }
        }
        else
        {
            _geminiClient?.Dispose();
            _geminiClient = null;
            if (_agentController != null)
            {
                _agentController.CloudProvider = null;
            }
        }

        if (_agentController != null)
        {
            _agentController.ExecutionTimeout = TimeSpan.FromSeconds(_appSettings.ExecutionTimeoutSeconds);
            await _agentController.RefreshSystemPromptWithUserMemoriesAsync();
        }

        if (_permissionManager != null)
        {
            _permissionManager.SetCustomAllowedPaths(_appSettings.CustomAllowedDirectories);
        }

        if (modelOrHardwareChanged && _llamaProcessManager != null)
        {
            SetSpeechText("Impostazioni salvate. Riavvio motore locale di inferenza per applicare le modifiche...");
            try
            {
                await _llamaProcessManager.StopAsync();
                await InitializeLlamaServerAsync();
            }
            catch (Exception ex)
            {
                SetSpeechText($"Errore durante il riavvio del modello: {ex.Message}");
            }
        }
        else
        {
            SetSpeechText("Impostazioni salvate ed applicate con successo.");
        }
    }

    private void ExitApplication()
    {
        Close();
    }
}