using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Data;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Tools.Memory;

public sealed class RecallMemoryTool : ITool
{
    private readonly IUserSettingsRepository _settingsRepository;

    public string Name => "recall_user_memory";
    public string Description => "Cerca e richiama le preferenze o note memorizzate dell'utente nel database tramite una parola chiave o pattern.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Read;

    public ToolDefinition Definition { get; }

    public RecallMemoryTool(IUserSettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));

        Definition = ToolDefinition.CreateFunction(
            Name,
            Description,
            new
            {
                type = "object",
                properties = new
                {
                    pattern = new
                    {
                        type = "string",
                        description = "Parola chiave o nome della preferenza da cercare (opzionale: lasciare vuoto o '*' per richiamare tutte le note memorizzate)."
                    }
                }
            }
        );
    }

    public async Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string pattern = string.Empty;
        if (arguments.RootElement.TryGetProperty("pattern", out var patElem) &&
            patElem.ValueKind == JsonValueKind.String)
        {
            pattern = patElem.GetString()?.Trim() ?? string.Empty;
        }

        try
        {
            var memories = (pattern.Length == 0 || pattern == "*")
                ? await _settingsRepository.GetAllMemoriesAsync(cancellationToken).ConfigureAwait(false)
                : await _settingsRepository.SearchMemoriesAsync(pattern, cancellationToken).ConfigureAwait(false);

            if (memories.Count == 0)
            {
                return ToolResult.Ok(string.IsNullOrEmpty(pattern) || pattern == "*"
                    ? "Nessuna preferenza memorizzata finora."
                    : $"Nessuna memoria o preferenza trovata per il filtro '{pattern}'.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Trovate {memories.Count} note/preferenze memorizzate:");
            foreach (var mem in memories)
            {
                sb.AppendLine($"- [{mem.Key}]: {mem.Value} (aggiornato: {mem.UpdatedAtUtc:yyyy-MM-dd HH:mm})");
            }

            return ToolResult.Ok(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Errore durante il recupero della memoria: {ex.Message}");
        }
    }
}
