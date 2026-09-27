using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Model;

public interface IModelProvider
{
    Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}
