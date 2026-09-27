using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;
using OpenAgent.Tools.Security;

namespace OpenAgent.Tools.FileSystem;

public sealed class DeleteFileTool : ITool
{
    private readonly IPermissionManager _permissionManager;

    public string Name => "delete_file";
    public string Description =>
        "Elimina in modo permanente un file dal filesystem locale all'interno della sandbox autorizzata. " +
        "Questa operazione è DISTRUTTIVA e richiede sempre l'autorizzazione esplicita dell'utente prima dell'esecuzione.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Destructive;

    public ToolDefinition Definition => ToolDefinition.CreateFunction(
        Name,
        Description,
        new
        {
            type = "object",
            properties = new
            {
                path = new
                {
                    type = "string",
                    description = "Percorso del file da eliminare definitivamente."
                }
            },
            required = new[] { "path" }
        }
    );

    public DeleteFileTool(IPermissionManager permissionManager)
    {
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
    }

    public Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.RootElement.TryGetProperty("path", out var pathElement))
        {
            return Task.FromResult(ToolResult.Fail("Parametro obbligatorio 'path' mancante."));
        }

        string rawPath = pathElement.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Task.FromResult(ToolResult.Fail("Il parametro 'path' non può essere vuoto."));
        }

        var check = _permissionManager.CheckPathAccess(rawPath, PathAccessType.Destructive);
        if (!check.IsAllowed)
        {
            return Task.FromResult(ToolResult.Fail($"Accesso negato all'eliminazione di '{rawPath}': {check.Reason}"));
        }

        string canonical = check.CanonicalPath;
        if (!File.Exists(canonical))
        {
            return Task.FromResult(ToolResult.Fail($"Impossibile eliminare: il file non esiste in '{canonical}'."));
        }

        try
        {
            File.Delete(canonical);
            return Task.FromResult(ToolResult.Ok($"File '{Path.GetFileName(canonical)}' eliminato con successo da '{canonical}'."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(ToolResult.Fail($"Permesso OS negato durante l'eliminazione: {ex.Message}"));
        }
        catch (IOException ex)
        {
            return Task.FromResult(ToolResult.Fail($"Errore I/O durante l'eliminazione del file: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Errore imprevisto durante l'eliminazione di '{canonical}': {ex.Message}"));
        }
    }
}
