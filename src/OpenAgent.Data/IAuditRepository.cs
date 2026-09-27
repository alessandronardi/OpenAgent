using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenAgent.Data;

public interface IAuditRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task LogExecutionAsync(ToolAuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ToolAuditEntry>> GetRecentLogsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
    Task ClearLogsAsync(CancellationToken cancellationToken = default);
}
