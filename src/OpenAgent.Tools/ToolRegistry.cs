using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Tools;

public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
    }

    public bool Unregister(string name)
    {
        return _tools.TryRemove(name, out _);
    }

    public ITool? GetTool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public IReadOnlyList<ITool> GetAllTools()
    {
        return _tools.Values.ToList();
    }

    public List<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(t => t.Definition).ToList();
    }

    public async Task<ToolResult> ExecuteToolCallAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        var tool = GetTool(toolName);
        if (tool == null)
        {
            return ToolResult.Fail($"Tool sconosciuto o non registrato: '{toolName}'. Tool disponibili: {string.Join(", ", _tools.Keys)}");
        }

        JsonDocument doc;
        try
        {
            string cleanJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson.Trim();
            doc = JsonDocument.Parse(cleanJson);
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail($"Argomenti JSON non validi per il tool '{toolName}': {ex.Message} (Ricevuto: '{argumentsJson}')");
        }

        using (doc)
        {
            try
            {
                return await tool.ExecuteAsync(doc, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ToolResult.Fail($"Esecuzione del tool '{toolName}' annullata.");
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Errore interno durante l'esecuzione del tool '{toolName}': {ex.Message}");
            }
        }
    }
}
