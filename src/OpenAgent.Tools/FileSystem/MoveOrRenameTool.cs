using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;
using OpenAgent.Tools.Security;

namespace OpenAgent.Tools.FileSystem;

public sealed class MoveOrRenameTool : ITool
{
    private readonly IPermissionManager _permissionManager;

    public string Name => "move_or_rename";
    public string Description => "Sposta o rinomina un file o una directory da un percorso sorgente a una destinazione controllata.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Modify;

    public ToolDefinition Definition { get; }

    public MoveOrRenameTool(IPermissionManager permissionManager)
    {
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));

        Definition = ToolDefinition.CreateFunction(
            Name,
            Description,
            new
            {
                type = "object",
                properties = new
                {
                    src = new
                    {
                        type = "string",
                        description = "Percorso del file o cartella sorgente da spostare o rinominare."
                    },
                    dest = new
                    {
                        type = "string",
                        description = "Percorso della destinazione finale (inclusivo del nuovo nome)."
                    },
                    overwrite = new
                    {
                        type = "boolean",
                        description = "Se true, consente di sovrascrivere un file esistente a destinazione (default: false)."
                    }
                },
                required = new[] { "src", "dest" }
            }
        );
    }

    public Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.RootElement.TryGetProperty("src", out var srcElem) || srcElem.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("Parametro obbligatorio 'src' mancante o non stringa."));
        }

        if (!arguments.RootElement.TryGetProperty("dest", out var destElem) || destElem.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("Parametro obbligatorio 'dest' mancante o non stringa."));
        }

        string rawSrc = srcElem.GetString()?.Trim() ?? string.Empty;
        string rawDest = destElem.GetString()?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(rawSrc) || string.IsNullOrEmpty(rawDest))
        {
            return Task.FromResult(ToolResult.Fail("I parametri 'src' o 'dest' non possono essere vuoti."));
        }

        bool overwrite = false;
        if (arguments.RootElement.TryGetProperty("overwrite", out var overwriteElem) &&
            (overwriteElem.ValueKind == JsonValueKind.True || overwriteElem.ValueKind == JsonValueKind.False))
        {
            overwrite = overwriteElem.GetBoolean();
        }

        // Verify permissions on both src and dest
        var srcCheck = _permissionManager.CheckPathAccess(rawSrc, PathAccessType.Modify);
        if (!srcCheck.IsAllowed)
        {
            return Task.FromResult(ToolResult.Fail($"Accesso negato alla sorgente '{rawSrc}': {srcCheck.Reason}"));
        }

        var destCheck = _permissionManager.CheckPathAccess(rawDest, PathAccessType.Modify);
        if (!destCheck.IsAllowed)
        {
            return Task.FromResult(ToolResult.Fail($"Accesso negato alla destinazione '{rawDest}': {destCheck.Reason}"));
        }

        string canonicalSrc = srcCheck.CanonicalPath;
        string canonicalDest = destCheck.CanonicalPath;

        bool isFile = File.Exists(canonicalSrc);
        bool isDirectory = Directory.Exists(canonicalSrc);

        if (!isFile && !isDirectory)
        {
            return Task.FromResult(ToolResult.Fail($"L'elemento sorgente non esiste: '{canonicalSrc}'"));
        }

        try
        {
            // Ensure target directory exists
            string? destParent = Path.GetDirectoryName(canonicalDest);
            if (!string.IsNullOrEmpty(destParent) && !Directory.Exists(destParent))
            {
                Directory.CreateDirectory(destParent);
            }

            if (isFile)
            {
                if (File.Exists(canonicalDest))
                {
                    if (!overwrite)
                    {
                        return Task.FromResult(ToolResult.Fail($"Il file di destinazione '{canonicalDest}' esiste già. Specificare 'overwrite: true' per sovrascriverlo."));
                    }
                    File.Delete(canonicalDest);
                }
                File.Move(canonicalSrc, canonicalDest);
            }
            else
            {
                if (Directory.Exists(canonicalDest))
                {
                    return Task.FromResult(ToolResult.Fail($"La directory di destinazione '{canonicalDest}' esiste già. Impossibile spostare."));
                }
                Directory.Move(canonicalSrc, canonicalDest);
            }

            return Task.FromResult(ToolResult.Ok($"Operazione completata: '{Path.GetFileName(canonicalSrc)}' spostato/rinominato in '{canonicalDest}'"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(ToolResult.Fail($"Permesso OS negato: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Errore durante lo spostamento/rinomina da '{canonicalSrc}' a '{canonicalDest}': {ex.Message}"));
        }
    }
}
