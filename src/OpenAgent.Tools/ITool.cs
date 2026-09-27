using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolRiskLevel RiskLevel { get; }
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(JsonDocument arguments, CancellationToken cancellationToken = default);
}
