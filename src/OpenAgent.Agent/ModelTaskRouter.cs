using System;
using System.Text.RegularExpressions;
using OpenAgent.Data;

namespace OpenAgent.Agent;

public enum TaskDomain
{
    DesktopToolCalling,
    WebResearch,
    TextReasoningSynthesis,
    GeneralConversation,
    ComplexReasoningCloud,
    MemoryManagement
}

public sealed class ModelSuggestion
{
    public TaskDomain Domain { get; init; }
    public string RecommendedProfile { get; init; } = string.Empty;
    public string RecommendedModelPath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool ShouldSwitch { get; init; }
    public bool IsCloud { get; init; }
}

public static class ModelTaskRouter
{
    public const int HighComplexityCharacterThreshold = 350;

    private static readonly Regex ExplicitCloudPattern = new(
        @"\b(cloud|gemini|google\s+gemini|gemini[- ]*3\.[58]|modello\s+cloud|passa\s+a\s+gemini|usa\s+(il\s+)?cloud|chiedi\s+a\s+gemini|usa\s+gemini|nel\s+cloud|server\s+google|provider\s+cloud)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ComplexSoftwarePattern = new(
        @"\b(architett\w+|refactor\w+|design\s+pattern|microserviz\w+|cqrs|thread[- ]safety|concorrenz\w+|multithread\w*|ottimizz\w+\s+algoritm\w+|analizza\s+codice|analizza\s+progetto|crea\s+un'?\s*applicazione|full[- ]*stack|astrazione|dependency\s+injection)\b|" +
        @"\b(scrivi|crea|sviluppa|implementa|programma|realizza|costruisci|genera|codifica|correggi|debugga|ottimizza)\b.*\b(codice|script|programma|software|applicazione|app|funzione|classe|metodo|algoritmo|parser|bot|api|backend|frontend|query|endpoint|libreria)\b|" +
        @"\b(python|csharp|c#|\.net|javascript|typescript|java|c\+\+|rust|golang|php|react|vue|angular|node\.?js|fastapi|flask|django|asp\.net|sqlite|postgresql|docker|kubernetes)\b|" +
        @"\b(algoritm\w+|struttur\w+\s+dati|ricorsiv\w+|alber\w+\s+binar\w+|graf\w+|sorting|ordinament\w+|complessit\w+\s+(computazionale|temporale|spaziale)|o\(n\w*\)|unit\s+test\w*|stack\s*trace)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ComplexReasoningPattern = new(
        @"\b(dimostra\w*|teorem\w+|confronta\s+a\s+fondo|analisi\s+comparativa|piano\s+strategico|workflow\s+complesso|algoritmo\s+avanzato|valutazione\s+euristica|pianifica\s+a\s+tappe)\b|" +
        @"\b(spiega\w*\s+in\s+dettaglio|spiega\w*\s+(il\s+funzionamento|come\s+funziona)|analisi\s+(approfondita|comparativa|critica|tecnica|scientifica)|valutazione\s+(euristica|rischi)|confront\w+\s+(i\s+pro|vantaggi|prestazioni|differenze))\b|" +
        @"\b(problem\s*solving|risolvi\s+(questo\s+problema|l'enigma|il\s+rompicapo|l'equazione)|equazion\w+|calcol\w+\s+(probabilit\w+|integrale|derivat\w+|statistica)|logica\s+formale|deduzion\w+|induzion\w+)\b|" +
        @"\b(piano\s+strategico|strategia\s+aziendale|business\s+plan|workflow\s+complesso|roadmap|analisi\s+swot)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WebPattern = new(
        @"(https?://[^\s]+|www\.[^\s]+|\b(visita|naviga|sito|web|url|link|scarica pagina|leggi pagina|scraping|ricerca online|cerca su internet)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DesktopToolPattern = new(
        @"\b(cartell[ae]|document[io]|desktop|sposta|rinomina|directory|path|estensione|crea\s+file|cancella\s+file|elimina\s+file|elenca|trova\s+file|ispeziona\s+file|mostra\s+i\s+file|apri\s+cartella)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TextSynthesisPattern = new(
        @"\b(riassumi|riassunto|sintetizza|parafrasa|correggi|traduci|traduci in|riscrivi|analisi del testo|saggio|argomenta|bozza|email|lettera)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MemoryPattern = new(
        @"\b(ricorda|ricordati|memorizza|salva in memoria|tieni a mente|dimentica|memoria|preferenze|come ti chiami|chi sei|il tuo nome|ti chiami|chiamati)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultiStepPromptPattern = new(
        @"(?m)^\s*(\d+\.|[-*])\s+",
        RegexOptions.Compiled);

    public static ModelSuggestion AnalyzePrompt(string prompt, string? currentProfile = null)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ModelSuggestion
            {
                Domain = TaskDomain.GeneralConversation,
                RecommendedProfile = AppSettings.DefaultProfile17B,
                RecommendedModelPath = AppSettings.DefaultModelPath17B,
                Reason = "Conversazione generale o richiesta non specificata.",
                ShouldSwitch = false,
                IsCloud = false
            };
        }

        string cleanPrompt = prompt.Trim();

        // 1. Explicit user request for Cloud / Gemini
        if (ExplicitCloudPattern.IsMatch(cleanPrompt))
        {
            bool isCurrent = string.Equals(currentProfile, AppSettings.ProfileGemini38Flash, StringComparison.OrdinalIgnoreCase);
            return new ModelSuggestion
            {
                Domain = TaskDomain.ComplexReasoningCloud,
                RecommendedProfile = AppSettings.ProfileGemini38Flash,
                RecommendedModelPath = "cloud:gemini-3.8-flash",
                Reason = "Richiesta esplicita per il modello Cloud (Google Gemini 3.8 Flash).",
                ShouldSwitch = !isCurrent,
                IsCloud = true
            };
        }

        // 2. High Complexity / Software Engineering / Multi-step Reasoning -> Gemini 3.8 Flash Cloud
        if (IsHighComplexityPrompt(cleanPrompt))
        {
            bool isCurrent = string.Equals(currentProfile, AppSettings.ProfileGemini38Flash, StringComparison.OrdinalIgnoreCase);
            return new ModelSuggestion
            {
                Domain = TaskDomain.ComplexReasoningCloud,
                RecommendedProfile = AppSettings.ProfileGemini38Flash,
                RecommendedModelPath = "cloud:gemini-3.8-flash",
                Reason = "Rilevata richiesta ad alta complessità (ragionamento avanzato / programmazione / contesto esteso): consigliato Gemini 3.8 Flash nel Cloud.",
                ShouldSwitch = !isCurrent,
                IsCloud = true
            };
        }

        // 3. Web research & long context synthesis
        if (WebPattern.IsMatch(cleanPrompt))
        {
            bool isCurrent = string.Equals(currentProfile, AppSettings.ProfileQwen3B, StringComparison.OrdinalIgnoreCase);
            return new ModelSuggestion
            {
                Domain = TaskDomain.WebResearch,
                RecommendedProfile = AppSettings.ProfileQwen3B,
                RecommendedModelPath = AppSettings.DefaultModelPathQwen3B,
                Reason = "Rilevata richiesta Web/URL: Qwen2.5 3B offre capacità superiori di sintesi e finestra di contesto ampia.",
                ShouldSwitch = !isCurrent,
                IsCloud = false
            };
        }

        // 4. Desktop, File System & Tool calling
        if (DesktopToolPattern.IsMatch(cleanPrompt))
        {
            bool isCurrent = string.Equals(currentProfile, AppSettings.ProfileCoder15B, StringComparison.OrdinalIgnoreCase);
            return new ModelSuggestion
            {
                Domain = TaskDomain.DesktopToolCalling,
                RecommendedProfile = AppSettings.ProfileCoder15B,
                RecommendedModelPath = AppSettings.DefaultModelPathCoder15B,
                Reason = "Rilevata operazione File/Desktop: Qwen2.5-Coder 1.5B è iperspecializzato per tool calling e percorsi Windows a velocità massima (~20 tok/s).",
                ShouldSwitch = !isCurrent,
                IsCloud = false
            };
        }

        // 5. Text synthesis, rewriting & conceptual reasoning
        if (TextSynthesisPattern.IsMatch(cleanPrompt))
        {
            bool isCurrent = string.Equals(currentProfile, AppSettings.ProfileGemma2B, StringComparison.OrdinalIgnoreCase);
            return new ModelSuggestion
            {
                Domain = TaskDomain.TextReasoningSynthesis,
                RecommendedProfile = AppSettings.ProfileGemma2B,
                RecommendedModelPath = AppSettings.DefaultModelPathGemma2B,
                Reason = "Rilevata richiesta di sintesi/redazione/ragionamento: Gemma-2 2B Instruct offre eccellente comprensione concettuale e fluidità espositiva con minimo consumo di risorse.",
                ShouldSwitch = !isCurrent,
                IsCloud = false
            };
        }

        // 6. Memory & Identity Management
        if (MemoryPattern.IsMatch(cleanPrompt))
        {
            return new ModelSuggestion
            {
                Domain = TaskDomain.MemoryManagement,
                RecommendedProfile = currentProfile ?? AppSettings.ProfileCoder15B,
                RecommendedModelPath = AppSettings.DefaultModelPathCoder15B,
                Reason = "Rilevata operazione di memoria, preferenze o identità dell'agente.",
                ShouldSwitch = false,
                IsCloud = false
            };
        }

        // 7. Fallback to general conversation
        return new ModelSuggestion
        {
            Domain = TaskDomain.GeneralConversation,
            RecommendedProfile = currentProfile ?? AppSettings.DefaultProfile17B,
            RecommendedModelPath = AppSettings.DefaultModelPath17B,
            Reason = "Richiesta generale: il modello attualmente caricato è appropriato.",
            ShouldSwitch = false,
            IsCloud = false
        };
    }

    public static bool IsHighComplexityPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;

        if (prompt.Length >= HighComplexityCharacterThreshold)
        {
            return true;
        }

        if (ComplexSoftwarePattern.IsMatch(prompt))
        {
            return true;
        }

        if (ComplexReasoningPattern.IsMatch(prompt))
        {
            return true;
        }

        if (prompt.Contains("```") || prompt.Contains("SELECT ") || prompt.Contains("def ") || prompt.Contains("public class"))
        {
            return true;
        }

        var multiStepMatches = MultiStepPromptPattern.Matches(prompt);
        if (multiStepMatches.Count >= 3)
        {
            return true;
        }

        return false;
    }
}
