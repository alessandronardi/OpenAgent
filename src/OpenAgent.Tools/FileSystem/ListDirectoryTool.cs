using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;
using OpenAgent.Tools.Security;

namespace OpenAgent.Tools.FileSystem;

public sealed class ListDirectoryTool : ITool
{
    private readonly IPermissionManager _permissionManager;
    private const int MaxEntries = 60;

    public string Name => "list_directory";
    public string Description => "Elenca i file e le cartelle all'interno di un percorso specificato.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Read;

    public ToolDefinition Definition { get; }

    public ListDirectoryTool(IPermissionManager permissionManager)
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
                    path = new
                    {
                        type = "string",
                        description = "Percorso della cartella da elencare (es. 'Documents', 'C:\\Users\\aless\\Documents', o percorso relativo al workspace)."
                    }
                },
                required = new[] { "path" }
            }
        );
    }

    public Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.RootElement.TryGetProperty("path", out var pathElement) ||
            pathElement.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(ToolResult.Fail("Parametro obbligatorio 'path' mancante o non stringa."));
        }

        string rawPath = pathElement.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(rawPath))
        {
            return Task.FromResult(ToolResult.Fail("Il percorso 'path' specificato è vuoto."));
        }

        var permCheck = _permissionManager.CheckPathAccess(rawPath, PathAccessType.Read);
        if (!permCheck.IsAllowed)
        {
            return Task.FromResult(ToolResult.Fail($"Accesso negato: {permCheck.Reason}"));
        }

        string targetDir = permCheck.CanonicalPath;
        if (!Directory.Exists(targetDir))
        {
            return Task.FromResult(ToolResult.Fail($"La cartella non esiste o non è una directory: '{targetDir}'"));
        }

        try
        {
            var dirInfo = new DirectoryInfo(targetDir);
            var subDirs = dirInfo.GetDirectories().OrderBy(d => d.Name).ToList();
            var files = dirInfo.GetFiles().OrderBy(f => f.Name).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Contenuto di: {targetDir}");
            sb.AppendLine($"Totale cartelle: {subDirs.Count}, Totale file: {files.Count}");
            sb.AppendLine("----------------------------------------");

            int count = 0;
            foreach (var dir in subDirs)
            {
                if (count++ >= MaxEntries) break;
                sb.AppendLine($"[DIR]  {dir.Name,-30} (Modificato: {dir.LastWriteTime:yyyy-MM-dd HH:mm})");
            }

            foreach (var file in files)
            {
                if (count++ >= MaxEntries) break;
                string sizeStr = FormatFileSize(file.Length);
                sb.AppendLine($"[FILE] {file.Name,-30} ({sizeStr,10}, Modificato: {file.LastWriteTime:yyyy-MM-dd HH:mm})");
            }

            if (subDirs.Count + files.Count > MaxEntries)
            {
                sb.AppendLine($"----------------------------------------");
                sb.AppendLine($"[Attenzione: elenco limitato ai primi {MaxEntries} elementi su {subDirs.Count + files.Count} totali]");
            }

            return Task.FromResult(ToolResult.Ok(sb.ToString().TrimEnd()));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(ToolResult.Fail($"Permesso negato a livello di sistema operativo per '{targetDir}': {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Errore durante la lettura della cartella '{targetDir}': {ex.Message}"));
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
