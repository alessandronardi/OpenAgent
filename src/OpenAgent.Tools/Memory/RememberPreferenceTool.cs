using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Data;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Tools.Memory;

public sealed class RememberPreferenceTool : ITool
{
    private readonly IUserSettingsRepository _settingsRepository;

    public string Name => "remember_user_preference";
    public string Description => "Memorizza esplicitamente una preferenza, nota, abitudine o nome attribuito all'agente o all'utente nel database locale persistente per le sessioni future.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Read;

    public ToolDefinition Definition { get; }

    public RememberPreferenceTool(IUserSettingsRepository settingsRepository)
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
                    key = new
                    {
                        type = "string",
                        description = "Chiave identificativa della preferenza (es. 'nome_agente', 'nome_utente', 'stile_risposta', 'cartella_progetti', 'linguaggio_preferito')."
                    },
                    value = new
                    {
                        type = "string",
                        description = "Valore o contenuto dettagliato da memorizzare."
                    }
                },
                required = new[] { "key", "value" }
            }
        );
    }

    public async Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.RootElement.TryGetProperty("key", out var keyElem) ||
            keyElem.ValueKind != JsonValueKind.String)
        {
            return ToolResult.Fail("Parametro obbligatorio 'key' mancante o non valido.");
        }

        if (!arguments.RootElement.TryGetProperty("value", out var valElem) ||
            valElem.ValueKind != JsonValueKind.String)
        {
            return ToolResult.Fail("Parametro obbligatorio 'value' mancante o non valido.");
        }

        string key = keyElem.GetString()?.Trim() ?? string.Empty;
        string value = valElem.GetString()?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return ToolResult.Fail("La chiave 'key' non può essere vuota.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return ToolResult.Fail("Il valore 'value' non può essere vuoto.");
        }

        if (key.Equals("nome", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("agent_name", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("nome_assistente", StringComparison.OrdinalIgnoreCase))
        {
            key = "nome_agente";
        }
        else if (key.Equals("user_name", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("nome_utente", StringComparison.OrdinalIgnoreCase))
        {
            key = "nome_utente";
        }

        try
        {
            await _settingsRepository.SetMemoryAsync(key, value, cancellationToken).ConfigureAwait(false);
            return ToolResult.Ok($"Preferenza '{key}' memorizzata con successo: \"{value}\".");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Errore durante il salvataggio della memoria: {ex.Message}");
        }
    }
}
