using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenAgent.Data;

public interface IUserSettingsRepository
{
    string DatabasePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // App Settings
    Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    // Explicit User Memory
    Task SetMemoryAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> GetMemoryAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMemoryEntry>> GetAllMemoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMemoryEntry>> SearchMemoriesAsync(string pattern, CancellationToken cancellationToken = default);
    Task<bool> DeleteMemoryAsync(string key, CancellationToken cancellationToken = default);
    Task ClearMemoriesAsync(CancellationToken cancellationToken = default);
}
