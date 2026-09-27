using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAgent.Data.Hardware;

public sealed class HardwareOptimizationResult
{
    public HardwareSpecs Specs { get; init; } = new();
    public AppSettings OptimizedSettings { get; init; } = new();
    public List<string> AppliedOptimizations { get; init; } = [];
    public string SummaryText { get; init; } = string.Empty;
}

public static class HardwareProfiler
{
    /// <summary>
    /// Ottimizza automaticamente i parametri di configurazione dell'agente (thread, contesto, backend, modello)
    /// in base alle specifiche hardware rilevate per massimizzare throughput e stabilità.
    /// </summary>
    public static HardwareOptimizationResult OptimizeSettings(AppSettings currentSettings, HardwareSpecs specs, string? projectRootDir = null)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(specs);

        var optimized = currentSettings.Clone();
        var notes = new List<string>();

        // 1. OTTIMIZZAZIONE THREAD CPU (In base ai core fisici, evitando thread contention SMT)
        int recommendedThreads = specs.PhysicalCores switch
        {
            <= 2 => 2,
            <= 4 => 4,
            <= 6 => 6,
            <= 10 => 8,
            _ => 12
        };

        if (optimized.CpuThreads != recommendedThreads)
        {
            notes.Add($"Thread CPU impostati a {recommendedThreads} (ottimizzato per {specs.PhysicalCores} core fisici).");
            optimized.CpuThreads = recommendedThreads;
        }

        // 2. OTTIMIZZAZIONE CONTESTO (KV CACHE) IN BASE ALLA RAM DISPONIBILE
        int recommendedContext;
        if (specs.AvailableRamGb < 3.2 || specs.TotalRamGb < 8.0)
        {
            recommendedContext = 2048; // Risparmia ~300 MB di KV Cache per evitare OOM
            notes.Add($"Dimensione contesto impostata a 2048 token (RAM limitata: {specs.AvailableRamGb:F1} GB liberi su {specs.TotalRamGb:F1} GB).");
        }
        else if (specs.AvailableRamGb >= 6.0 && specs.TotalRamGb >= 16.0)
        {
            recommendedContext = 8192; // Ampia finestra per compiti estesi
            notes.Add($"Dimensione contesto estesa a 8192 token (RAM abbondante: {specs.AvailableRamGb:F1} GB liberi).");
        }
        else
        {
            recommendedContext = 4096; // Valore ottimale per compiti standard
            notes.Add($"Dimensione contesto impostata a 4096 token (RAM bilanciata: {specs.AvailableRamGb:F1} GB liberi).");
        }
        optimized.ContextSize = recommendedContext;

        // 3. ACCELERAZIONE GRAFICA (Vulkan vs CPU)
        if (specs.HasVulkanSupport)
        {
            optimized.Device = "Vulkan0";
            notes.Add($"Accelerazione GPU Vulkan abilitata ({specs.GpuName ?? "GPU compatibile"}).");
        }
        else
        {
            optimized.Device = "none";
            notes.Add("Accelerazione GPU non disponibile o driver Vulkan assenti: inferenza configurata su CPU.");
        }

        // 4. TIMEOUT DI ESECUZIONE
        int recommendedTimeout = specs.Tier switch
        {
            HardwareTier.Entry => 240,
            HardwareTier.Balanced => 180,
            HardwareTier.HighPerformance => 120,
            _ => 180
        };
        optimized.ExecutionTimeoutSeconds = recommendedTimeout;

        // 5. PROFILO MODELLO CONSIGLIATO
        // Se il modello attualmente impostato dall'utente esiste già su disco, lo preserviamo senza sovrascriverlo!
        string? root = projectRootDir;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppDomain.CurrentDomain.BaseDirectory;
        }

        string currentAbsPath = Path.IsPathRooted(currentSettings.ModelPath)
            ? currentSettings.ModelPath
            : Path.Combine(root, currentSettings.ModelPath);

        if (File.Exists(currentAbsPath))
        {
            optimized.ModelProfile = currentSettings.ModelProfile;
            optimized.ModelPath = currentSettings.ModelPath;
            notes.Add($"Mantenuto profilo modello selezionato: '{currentSettings.ModelProfile}'.");
        }
        else
        {
            // Solo se il modello configurato non esiste fisicamente sul disco, selezioniamo il migliore disponibile
            string modelsDir = Path.Combine(root, "models");
            if (Directory.Exists(modelsDir))
            {
                var (profileName, modelRelPath) = SelectBestModelProfile(specs, modelsDir);
                if (!string.IsNullOrEmpty(profileName) && !string.IsNullOrEmpty(modelRelPath))
                {
                    optimized.ModelProfile = profileName;
                    optimized.ModelPath = modelRelPath;
                    notes.Add($"File modello precedente non trovato ({currentSettings.ModelPath}). Selezionato automaticamente: '{profileName}' ({modelRelPath}).");
                }
            }
        }

        optimized.LastDetectedHardware = specs.Summary;

        string summary = $"Profilo {specs.Tier}: {optimized.CpuThreads} thread CPU, {optimized.ContextSize} ctx, Device: {optimized.Device}, Modello: {optimized.ModelProfile}";

        return new HardwareOptimizationResult
        {
            Specs = specs,
            OptimizedSettings = optimized,
            AppliedOptimizations = notes,
            SummaryText = summary
        };
    }

    private static (string? ProfileName, string? ModelPath) SelectBestModelProfile(HardwareSpecs specs, string modelsDir)
    {
        // Se RAM limitata (< 3.5 GB liberi o <= 8.0 GB totali): prediligi modelli da 1.5B - 1.7B
        if (specs.AvailableRamGb < 3.5 || specs.TotalRamGb <= 8.5)
        {
            if (File.Exists(Path.Combine(modelsDir, "Spark-X2.5-1.7B-Q4_K_M.gguf")))
            {
                return (AppSettings.DefaultProfile17B, AppSettings.DefaultModelPath17B);
            }
            if (File.Exists(Path.Combine(modelsDir, "qwen2.5-coder-1.5b-instruct-q4_k_m.gguf")))
            {
                return (AppSettings.ProfileCoder15B, AppSettings.DefaultModelPathCoder15B);
            }
            if (File.Exists(Path.Combine(modelsDir, "gemma-2-2b-it-q4_k_m.gguf")))
            {
                return (AppSettings.ProfileGemma2B, AppSettings.DefaultModelPathGemma2B);
            }
        }
        // Se RAM elevata (>= 6.0 GB liberi e >= 16.0 GB totali): prediligi 3B
        else if (specs.AvailableRamGb >= 6.0 && specs.TotalRamGb >= 16.0)
        {
            if (File.Exists(Path.Combine(modelsDir, "qwen2.5-3b-instruct-q4_k_m.gguf")))
            {
                return (AppSettings.ProfileQwen3B, AppSettings.DefaultModelPathQwen3B);
            }
        }
        // Fascia bilanciata standard (8-16 GB RAM)
        else
        {
            if (File.Exists(Path.Combine(modelsDir, "gemma-2-2b-it-q4_k_m.gguf")))
            {
                return (AppSettings.ProfileGemma2B, AppSettings.DefaultModelPathGemma2B);
            }
            if (File.Exists(Path.Combine(modelsDir, "Spark-X2.5-1.7B-Q4_K_M.gguf")))
            {
                return (AppSettings.DefaultProfile17B, AppSettings.DefaultModelPath17B);
            }
            if (File.Exists(Path.Combine(modelsDir, "qwen2.5-coder-1.5b-instruct-q4_k_m.gguf")))
            {
                return (AppSettings.ProfileCoder15B, AppSettings.DefaultModelPathCoder15B);
            }
        }

        return (null, null);
    }
}
