using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Data;

namespace OpenAgent.Agent;

public enum MemoryIntentType
{
    None,
    SetAgentName,
    SetUserName,
    SetPreference,
    RecallAll,
    RecallQuery,
    AskAgentName,
    ClearMemory
}

public sealed class MemoryIntentMatch
{
    public MemoryIntentType Type { get; init; } = MemoryIntentType.None;
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string SearchPattern { get; init; } = string.Empty;
    public string DirectReply { get; init; } = string.Empty;
}

public static class MemoryIntentHandler
{
    public const string AgentNameKey = "nome_agente";
    public const string UserNameKey = "nome_utente";

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "un", "uno", "una", "il", "lo", "la", "i", "gli", "le",
        "in", "a", "da", "di", "per", "con", "su", "tra", "fra"
    };

    // 1. Agent Name Assignment regexes
    private static readonly Regex AgentNameAssignmentRegex = new(
        @"(?:(?:da\s+(?:oggi|ora|adesso|questo\s+momento)|d'ora\s+in\s+poi)\s+)?" +
        @"(?:(?:ricorda(?:ti)?|memorizza|salva(?:\s+in\s+memoria)?|tieni\s+a\s+mente)\s+(?:che\s+)?)?" +
        @"(?:(?:vorrei|desidero|voglio)\s+(?:che\s+)?(?:ti\s+chiamassi|chiamarti)\s+|" +
        @"(?:il\s+tuo\s+(?:nuovo\s+)?nome\s+(?:è|sarà|sia)|ti\s+(?:chiami|chiamerai|chiamassi|chiamo)|ti\s+chiamerò|chiamati(?:\s+pure)?|(?:tu\s+)?(?:sei|sarai)|your\s+name\s+is))\s*" +
        @"(?::\s*)?[""']?([A-Za-z0-9_\-À-ÿ]+)[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2. User Name Assignment regexes
    private static readonly Regex UserNameAssignmentRegex = new(
        @"(?:(?:(?:ricorda(?:ti)?|memorizza|salva(?:\s+in\s+memoria)?|tieni\s+a\s+mente)\s+(?:che\s+)?)?" +
        @"(?:il\s+mio\s+(?:nuovo\s+)?nome\s+(?:è|sarà)|mi\s+(?:chiamo|chiamerò)|io\s+(?:sono|mi\s+chiamo)|my\s+name\s+is)\s*" +
        @"(?::\s*)?[""']?([A-Za-z0-9_\-À-ÿ]+)[""']?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 3. Asking Agent's Name
    private static readonly Regex AskAgentNameRegex = new(
        @"^\s*(?:come\s+ti\s+chiami|qual\s+è\s+il\s+tuo\s+nome|chi\s+sei|dimmi\s+il\s+tuo\s+nome|what\s+is\s+your\s+name|who\s+are\s+you)\s*\??\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 4. Recall memories query
    private static readonly Regex RecallMemoriesRegex = new(
        @"^\s*(?:cosa\s+ricordi(?:\s+di\s+me|\s+su\s+di\s+me)?|cosa\s+sai\s+su\s+di\s+me|quali\s+(?:sono\s+le\s+mie\s+)?(?:preferenze|memorie|note)|mostra(?:\s+la)?\s+memoria|cosa\s+hai\s+in\s+memoria|richiama(?:\s+la)?\s+memoria|elenca\s+(?:le\s+)?memorie|what\s+do\s+you\s+remember)\s*\??\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 5. Explicit structured or natural remember commands
    private static readonly Regex ExplicitRememberPrefixRegex = new(
        @"^\s*(?:ricorda(?:ti)?|memorizza|salva\s+in\s+memoria|tieni\s+a\s+mente|non\s+dimenticare|remember)\s*(?:che|questo|la\s+preferenza|:)?\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 6. Clear memory regex
    private static readonly Regex ClearMemoryRegex = new(
        @"^\s*(?:svuota\s+(?:tutta\s+)?la\s+memoria|cancella\s+tutt[ea]\s+(?:le\s+)?memorie|elimina\s+tutte\s+le\s+memorie|dimentica\s+tutto)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MemoryIntentMatch DetectIntent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new MemoryIntentMatch();
        }

        string clean = prompt.Trim();

        // 1. Clear memory
        if (ClearMemoryRegex.IsMatch(clean))
        {
            return new MemoryIntentMatch
            {
                Type = MemoryIntentType.ClearMemory
            };
        }

        // 2. Asking agent's name
        if (AskAgentNameRegex.IsMatch(clean))
        {
            return new MemoryIntentMatch
            {
                Type = MemoryIntentType.AskAgentName
            };
        }

        // 3. Recall all memories query
        if (RecallMemoriesRegex.IsMatch(clean))
        {
            return new MemoryIntentMatch
            {
                Type = MemoryIntentType.RecallAll
            };
        }

        // 4. Agent Name Assignment (e.g. "ricordati che il tuo nome è Octy", "ti chiami Octy", "chiamati Octy")
        var agentNameMatch = AgentNameAssignmentRegex.Match(clean);
        if (agentNameMatch.Success)
        {
            string extractedName = string.Empty;
            for (int i = 1; i < agentNameMatch.Groups.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(agentNameMatch.Groups[i].Value))
                {
                    extractedName = agentNameMatch.Groups[i].Value.Trim();
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(extractedName) &&
                !StopWords.Contains(extractedName))
            {
                return new MemoryIntentMatch
                {
                    Type = MemoryIntentType.SetAgentName,
                    Key = AgentNameKey,
                    Value = extractedName
                };
            }
        }

        // 5. User Name Assignment (e.g. "ricordati che mi chiamo Alessandro", "il mio nome è Alessandro")
        var userNameMatch = UserNameAssignmentRegex.Match(clean);
        if (userNameMatch.Success)
        {
            string extractedName = userNameMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(extractedName))
            {
                return new MemoryIntentMatch
                {
                    Type = MemoryIntentType.SetUserName,
                    Key = UserNameKey,
                    Value = extractedName
                };
            }
        }

        // 6. Explicit Remember Prefix (e.g. "Ricordati che preferisco il tema scuro")
        var rememberMatch = ExplicitRememberPrefixRegex.Match(clean);
        if (rememberMatch.Success)
        {
            string payload = rememberMatch.Groups[1].Value.Trim();

            // Check if user specified key = value or key : value
            int sepIdx = payload.IndexOfAny(['=', ':']);
            if (sepIdx > 0 && sepIdx < payload.Length - 1)
            {
                string rawKey = payload[..sepIdx].Trim();
                string rawVal = payload[(sepIdx + 1)..].Trim();

                if (!string.IsNullOrWhiteSpace(rawKey) && !string.IsNullOrWhiteSpace(rawVal))
                {
                    string normKey = NormalizeMemoryKey(rawKey);
                    return new MemoryIntentMatch
                    {
                        Type = MemoryIntentType.SetPreference,
                        Key = normKey,
                        Value = rawVal
                    };
                }
            }

            // Natural language deduction of key and value
            var (deducedKey, deducedVal) = DeduceKeyAndValue(payload);
            return new MemoryIntentMatch
            {
                Type = MemoryIntentType.SetPreference,
                Key = deducedKey,
                Value = deducedVal
            };
        }

        return new MemoryIntentMatch();
    }

    public static string NormalizeMemoryKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return "nota";

        string key = rawKey.Trim().ToLowerInvariant();
        key = Regex.Replace(key, @"\s+", "_");
        key = Regex.Replace(key, @"[^a-z0-9_\-]", "");

        if (key.Length > 40) key = key[..40];
        return string.IsNullOrEmpty(key) ? "nota" : key;
    }

    private static (string Key, string Value) DeduceKeyAndValue(string phrase)
    {
        string text = phrase.Trim();

        if (Regex.IsMatch(text, @"\b(tema|color[ie]|dark|light|scuro|chiaro)\b", RegexOptions.IgnoreCase))
        {
            return ("tema_preferito", text);
        }
        if (Regex.IsMatch(text, @"\b(linguaggio|linguaggi|c#|python|rust|javascript)\b", RegexOptions.IgnoreCase))
        {
            return ("linguaggio_preferito", text);
        }
        if (Regex.IsMatch(text, @"\b(editor|ide|vscode|visual studio)\b", RegexOptions.IgnoreCase))
        {
            return ("editor_preferito", text);
        }
        if (Regex.IsMatch(text, @"\b(stile|rispost[ae]|sintetic[ao]|dettagliat[ao]|brev[ei])\b", RegexOptions.IgnoreCase))
        {
            return ("stile_risposta", text);
        }
        if (Regex.IsMatch(text, @"\b(cartella|directory|progett[io]|cartelle|path)\b", RegexOptions.IgnoreCase))
        {
            return ("cartella_progetti", text);
        }

        // Default slug from first few words
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string slugKey = words.Length >= 2
            ? $"{words[0]}_{words[1]}"
            : (words.Length == 1 ? words[0] : "nota");

        return (NormalizeMemoryKey(slugKey), text);
    }
}
