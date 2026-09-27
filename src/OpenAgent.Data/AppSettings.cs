using System;
using System.Collections.Generic;

namespace OpenAgent.Data;

public sealed class AppSettings
{
    public const string DefaultProfile17B = "Spark-X 2.5 1.7B";
    public const string ProfileCoder15B = "Qwen2.5-Coder 1.5B (Desktop & Tools)";
    public const string ProfileQwen3B = "Qwen2.5 3B Instruct";
    public const string ProfileGemma2B = "Gemma-2 2B Instruct (Logica & Sintesi)";
    public const string Profile4B = "Spark-X 4B";
    public const string ProfileCustom = "Personalizzato GGUF";
    public const string ProfileGemini38Flash = "Google Gemini 3.8 Flash (Cloud)";
    public const string ProfileGemini35FlashLite = "Google Gemini 3.5 Flash-Lite (Cloud)";

    public const string DefaultModelPath17B = "models/Spark-X2.5-1.7B-Q4_K_M.gguf";
    public const string DefaultModelPathCoder15B = "models/qwen2.5-coder-1.5b-instruct-q4_k_m.gguf";
    public const string DefaultModelPathQwen3B = "models/qwen2.5-3b-instruct-q4_k_m.gguf";
    public const string DefaultModelPathGemma2B = "models/gemma-2-2b-it-q4_k_m.gguf";
    public const string DefaultModelPath4B = "models/Spark-X4B-Q4_K_M.gguf";

    public string ModelProfile { get; set; } = DefaultProfile17B;
    public string ModelPath { get; set; } = DefaultModelPath17B;
    public int CpuThreads { get; set; } = 6;
    public int ContextSize { get; set; } = 4096;
    public int Port { get; set; } = 8080;
    public string Host { get; set; } = "127.0.0.1";
    public string Device { get; set; } = "none";
    public int ExecutionTimeoutSeconds { get; set; } = 180;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool AutoTuneOnStartup { get; set; } = true;
    public bool HasCompletedInitialAutoTune { get; set; } = false;
    public string? LastDetectedHardware { get; set; }
    public List<string> CustomAllowedDirectories { get; set; } = [];

    // Cloud Provider Settings (Google Gemini)
    public string GeminiApiKey { get; set; } = string.Empty;
    public string CloudModelName { get; set; } = "gemini-3.8-flash";
    public string CloudFallbackModelName { get; set; } = "gemini-3.5-flash-lite";
    public bool EnableCloudEscalation { get; set; } = true;
    public bool AutoCloudOnComplexity { get; set; } = true;

    public AppSettings Clone()
    {
        return new AppSettings
        {
            ModelProfile = ModelProfile,
            ModelPath = ModelPath,
            CpuThreads = CpuThreads,
            ContextSize = ContextSize,
            Port = Port,
            Host = Host,
            Device = Device,
            ExecutionTimeoutSeconds = ExecutionTimeoutSeconds,
            StartWithWindows = StartWithWindows,
            StartMinimizedToTray = StartMinimizedToTray,
            AutoTuneOnStartup = AutoTuneOnStartup,
            HasCompletedInitialAutoTune = HasCompletedInitialAutoTune,
            LastDetectedHardware = LastDetectedHardware,
            CustomAllowedDirectories = new List<string>(CustomAllowedDirectories),
            GeminiApiKey = GeminiApiKey,
            CloudModelName = CloudModelName,
            CloudFallbackModelName = CloudFallbackModelName,
            EnableCloudEscalation = EnableCloudEscalation,
            AutoCloudOnComplexity = AutoCloudOnComplexity
        };
    }

    public static ModelPreset GetModelPreset(string? profileName)
    {
        return profileName switch
        {
            ProfileCoder15B => new ModelPreset
            {
                ProfileName = ProfileCoder15B,
                ModelPath = DefaultModelPathCoder15B,
                CpuThreads = 6,
                ContextSize = 4096,
                ExecutionTimeoutSeconds = 120,
                Specialization = "Specialista per operazioni su File System, scripting, chiamate tool e comandi desktop.",
                RamFootprintEstimate = "~1.7 GB RAM",
                SpeedEstimate = "~23.5 tok/s (Ultra-rapido)"
            },
            ProfileQwen3B => new ModelPreset
            {
                ProfileName = ProfileQwen3B,
                ModelPath = DefaultModelPathQwen3B,
                CpuThreads = 6,
                ContextSize = 4096,
                ExecutionTimeoutSeconds = 180,
                Specialization = "Specialista per ricerca Web, estrazione testi da URL e sintesi di contenuti estesi.",
                RamFootprintEstimate = "~3.3 GB RAM",
                SpeedEstimate = "~12.2 tok/s (Ponderato)"
            },
            ProfileGemma2B => new ModelPreset
            {
                ProfileName = ProfileGemma2B,
                ModelPath = DefaultModelPathGemma2B,
                CpuThreads = 6,
                ContextSize = 4096,
                ExecutionTimeoutSeconds = 150,
                Specialization = "Specialista per sintesi testi, redazione fluida, reasoning concettuale ed efficienza energetica.",
                RamFootprintEstimate = "~2.1 GB RAM",
                SpeedEstimate = "~18.0 tok/s (Bilanciato)"
            },
            ProfileGemini38Flash => new ModelPreset
            {
                ProfileName = ProfileGemini38Flash,
                ModelPath = "cloud:gemini-3.8-flash",
                CpuThreads = 0,
                ContextSize = 1048576,
                ExecutionTimeoutSeconds = 120,
                Specialization = "Modello Cloud d'avanguardia per agenti autonomi, coding avanzato e contesti estesi (1M token).",
                RamFootprintEstimate = "0 GB RAM Locale (Cloud)",
                SpeedEstimate = "Cloud Ultra-Rapido"
            },
            ProfileGemini35FlashLite => new ModelPreset
            {
                ProfileName = ProfileGemini35FlashLite,
                ModelPath = "cloud:gemini-3.5-flash-lite",
                CpuThreads = 0,
                ContextSize = 1048576,
                ExecutionTimeoutSeconds = 90,
                Specialization = "Modello Cloud leggero e ultra-veloce per task agentici a latenza minima.",
                RamFootprintEstimate = "0 GB RAM Locale (Cloud)",
                SpeedEstimate = "Cloud Instantaneo"
            },
            Profile4B => new ModelPreset
            {
                ProfileName = Profile4B,
                ModelPath = DefaultModelPath4B,
                CpuThreads = 6,
                ContextSize = 4096,
                ExecutionTimeoutSeconds = 240,
                Specialization = "Modello opzionale ad alta capacità per compiti complessi.",
                RamFootprintEstimate = "~3.8 GB RAM",
                SpeedEstimate = "~8.0 tok/s"
            },
            _ => new ModelPreset
            {
                ProfileName = DefaultProfile17B,
                ModelPath = DefaultModelPath17B,
                CpuThreads = 6,
                ContextSize = 4096,
                ExecutionTimeoutSeconds = 180,
                Specialization = "Modello generico e conversazione quotidiana a basso impatto energetico.",
                RamFootprintEstimate = "~1.9 GB RAM",
                SpeedEstimate = "~20.0 tok/s"
            }
        };
    }
}

public sealed class ModelPreset
{
    public string ProfileName { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public int CpuThreads { get; init; } = 6;
    public int ContextSize { get; init; } = 4096;
    public int ExecutionTimeoutSeconds { get; init; } = 180;
    public string Specialization { get; init; } = string.Empty;
    public string RamFootprintEstimate { get; init; } = string.Empty;
    public string SpeedEstimate { get; init; } = string.Empty;
}
