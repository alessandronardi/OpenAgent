using System;

namespace OpenAgent.Data;

public sealed record UserMemoryEntry
{
    public long Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
}
