using System;

namespace OpenAgent.Data;

public sealed class ToolAuditEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? SessionId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public bool IsSuccess { get; set; }
    public string? ResultPreview { get; set; }
    public string? ErrorMessage { get; set; }
    public long ExecutionDurationMs { get; set; }
    public bool? UserConfirmed { get; set; }
}
