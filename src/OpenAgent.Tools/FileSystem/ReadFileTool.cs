using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;
using OpenAgent.Tools.Security;

namespace OpenAgent.Tools.FileSystem;

public sealed class ReadFileTool : ITool
{
    private readonly IPermissionManager _permissionManager;
    public const int HardCapMaxBytes = 8192; // Rigido limite massimo a 8 KB per proteggere il contesto LLM

    public string Name => "read_file";
    public string Description => "Legge il contenuto di un file di testo fino a max_bytes (limite massimo invalicabile di 8192 byte).";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Read;

    public ToolDefinition Definition { get; }

    public ReadFileTool(IPermissionManager permissionManager)
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
                        description = "Percorso del file da leggere."
                    },
                    max_bytes = new
                    {
                        type = "integer",
                        description = "Numero massimo di byte da leggere (default 8192, max 8192)."
                    }
                },
                required = new[] { "path" }
            }
        );
    }

    public async Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.RootElement.TryGetProperty("path", out var pathElement) ||
            pathElement.ValueKind != JsonValueKind.String)
        {
            return ToolResult.Fail("Parametro obbligatorio 'path' mancante o non stringa.");
        }

        string rawPath = pathElement.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(rawPath))
        {
            return ToolResult.Fail("Il percorso 'path' specificato è vuoto.");
        }

        int requestedMaxBytes = HardCapMaxBytes;
        if (arguments.RootElement.TryGetProperty("max_bytes", out var maxBytesElement) &&
            maxBytesElement.TryGetInt32(out int customMax) && customMax > 0)
        {
            requestedMaxBytes = Math.Min(customMax, HardCapMaxBytes);
        }

        var permCheck = _permissionManager.CheckPathAccess(rawPath, PathAccessType.Read);
        if (!permCheck.IsAllowed)
        {
            return ToolResult.Fail($"Accesso negato: {permCheck.Reason}");
        }

        string targetFile = permCheck.CanonicalPath;
        if (!File.Exists(targetFile))
        {
            return ToolResult.Fail($"Il file specificato non esiste: '{targetFile}'");
        }

        try
        {
            var fileInfo = new FileInfo(targetFile);
            long totalLength = fileInfo.Length;
            int bytesToRead = (int)Math.Min(totalLength, (long)requestedMaxBytes);

            byte[] buffer = new byte[bytesToRead];
            await using (var stream = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken).ConfigureAwait(false);
                bytesToRead = bytesRead;
            }

            // Quick binary check (null byte check in first 512 bytes)
            int checkLen = Math.Min(bytesToRead, 512);
            for (int i = 0; i < checkLen; i++)
            {
                if (buffer[i] == 0)
                {
                    return ToolResult.Fail($"Il file '{Path.GetFileName(targetFile)}' sembra essere un file binario e non può essere visualizzato come testo puro.");
                }
            }

            string text = Encoding.UTF8.GetString(buffer, 0, bytesToRead);
            if (totalLength > bytesToRead)
            {
                text += $"\n\n[... Troncato: letti {bytesToRead} di {totalLength} byte totali per protezione contesto ...]";
            }

            return ToolResult.Ok(text);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResult.Fail($"Permesso OS negato per il file '{targetFile}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Errore durante la lettura del file '{targetFile}': {ex.Message}");
        }
    }
}
