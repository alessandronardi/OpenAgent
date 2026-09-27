using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenAgent.Model.Contracts;

public sealed class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; set; } = new();

    public static ToolDefinition CreateFunction(string name, string description, object parameters) =>
        new()
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = name,
                Description = description,
                Parameters = parameters
            }
        };
}

public sealed class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public object Parameters { get; set; } = new();
}
