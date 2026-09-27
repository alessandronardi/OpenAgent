using System.Text.Json.Serialization;

namespace OpenAgent.Tools;

public sealed class ToolResult
{
    [JsonPropertyName("success")]
    public bool Success { get; }

    [JsonPropertyName("content")]
    public string Content { get; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; }

    private ToolResult(bool success, string content, string? error)
    {
        Success = success;
        Content = content;
        Error = error;
    }

    public static ToolResult Ok(string content) => new(true, content, null);

    public static ToolResult Fail(string error, string? partialContent = null) =>
        new(false, partialContent ?? error, error);
}
