using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenAgent.Agent;

public enum LoopDetectionResult
{
    None,
    Warning,
    Halt
}

public sealed record LoopCheckOutcome(LoopDetectionResult Result, string Message);

public sealed class SemanticCallTracker
{
    private readonly List<string> _callHistory = [];
    private readonly Dictionary<string, int> _callCounts = new(StringComparer.Ordinal);
    private string? _lastCallKey;
    private int _consecutiveCount;

    public int WarningThreshold { get; set; } = 2;
    public int HaltThreshold { get; set; } = 3;

    public void Reset()
    {
        _callHistory.Clear();
        _callCounts.Clear();
        _lastCallKey = null;
        _consecutiveCount = 0;
    }

    public static string NormalizeCallKey(string toolName, string argsJson)
    {
        string normalizedTool = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        string normalizedArgs = NormalizeJson(argsJson);
        return $"{normalizedTool}:{normalizedArgs}";
    }

    public static string NormalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return CanonicalizeElement(doc.RootElement);
        }
        catch
        {
            return json.Trim();
        }
    }

    private static string CanonicalizeElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var props = element.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => $"\"{p.Name.ToLowerInvariant()}\":{CanonicalizeElement(p.Value)}");
                return "{" + string.Join(",", props) + "}";

            case JsonValueKind.Array:
                var items = element.EnumerateArray().Select(CanonicalizeElement);
                return "[" + string.Join(",", items) + "]";

            case JsonValueKind.String:
                string val = element.GetString() ?? string.Empty;
                // Normalize file path separators and trim whitespace for robust equivalence
                string normalizedVal = val.Replace('\\', '/').Trim();
                return JsonSerializer.Serialize(normalizedVal);

            case JsonValueKind.Number:
                return element.GetRawText().Trim();

            case JsonValueKind.True:
                return "true";

            case JsonValueKind.False:
                return "false";

            case JsonValueKind.Null:
            default:
                return "null";
        }
    }

    public LoopCheckOutcome RecordAndCheck(string toolName, string argsJson)
    {
        string key = NormalizeCallKey(toolName, argsJson);
        _callHistory.Add(key);

        // Track consecutive identical call
        if (string.Equals(_lastCallKey, key, StringComparison.Ordinal))
        {
            _consecutiveCount++;
        }
        else
        {
            _lastCallKey = key;
            _consecutiveCount = 1;
        }

        // Track overall counts
        _callCounts.TryGetValue(key, out int count);
        count++;
        _callCounts[key] = count;

        // Check 1: Consecutive identical call repeating >= HaltThreshold
        if (_consecutiveCount >= HaltThreshold || count >= HaltThreshold)
        {
            return new LoopCheckOutcome(
                LoopDetectionResult.Halt,
                $"Rilevato loop persistente: il tool '{toolName}' è stato invocato {Math.Max(_consecutiveCount, count)} volte con gli stessi argomenti. Esecuzione arrestata per evitare cicli infiniti.");
        }

        // Check 2: Ping-Pong pattern (A -> B -> A -> B)
        if (_callHistory.Count >= 4)
        {
            int last = _callHistory.Count - 1;
            string a1 = _callHistory[last - 3];
            string b1 = _callHistory[last - 2];
            string a2 = _callHistory[last - 1];
            string b2 = _callHistory[last];

            if (a1 == a2 && b1 == b2 && !string.Equals(a1, b1, StringComparison.Ordinal))
            {
                return new LoopCheckOutcome(
                    LoopDetectionResult.Halt,
                    $"Rilevato loop alternato (ping-pong) tra chiamate ripetute ({toolName}). Esecuzione arrestata per prevenire oscillazioni.");
            }
        }

        // Check 3: First repetition warning
        if (_consecutiveCount >= WarningThreshold || count >= WarningThreshold)
        {
            return new LoopCheckOutcome(
                LoopDetectionResult.Warning,
                $"Attenzione: hai già eseguito il tool '{toolName}' con questi stessi parametri. Non ripetere la stessa chiamata: formula la risposta finale o prova un approccio differente.");
        }

        return new LoopCheckOutcome(LoopDetectionResult.None, string.Empty);
    }
}
