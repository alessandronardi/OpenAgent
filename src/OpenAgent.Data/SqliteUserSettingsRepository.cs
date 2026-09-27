using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace OpenAgent.Data;

public sealed class SqliteUserSettingsRepository : IUserSettingsRepository
{
    private const string SettingsConfigKey = "app_config";
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public string DatabasePath => _databasePath;

    static SqliteUserSettingsRepository()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
        }
        catch
        {
            // Ignore if already initialized
        }
    }

    public SqliteUserSettingsRepository(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string agentDir = Path.Combine(appData, "OpenAgent");
            _databasePath = Path.Combine(agentDir, "openagent_data.db");
        }
        else
        {
            _databasePath = Path.GetFullPath(databasePath);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized) return;

            string? dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Enable WAL mode
            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string createTablesSql = @"
                CREATE TABLE IF NOT EXISTS user_memory (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    memory_key TEXT UNIQUE NOT NULL,
                    memory_value TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_user_memory_key ON user_memory(memory_key);

                CREATE TABLE IF NOT EXISTS app_settings (
                    setting_key TEXT PRIMARY KEY,
                    setting_value TEXT NOT NULL
                );
            ";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = createTablesSql;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = @key;";
        cmd.Parameters.AddWithValue("@key", SettingsConfigKey);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result != null && result != DBNull.Value)
        {
            string json = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<AppSettings>(json);
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }
                catch
                {
                    // Fall back to default on parse error
                }
            }
        }

        return new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES (@key, @value)
            ON CONFLICT(setting_key) DO UPDATE SET
                setting_value = excluded.setting_value;
        ";

        cmd.Parameters.AddWithValue("@key", SettingsConfigKey);
        cmd.Parameters.AddWithValue("@value", json);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetMemoryAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO user_memory (memory_key, memory_value, updated_at)
            VALUES (@key, @value, @updated_at)
            ON CONFLICT(memory_key) DO UPDATE SET
                memory_value = excluded.memory_value,
                updated_at = excluded.updated_at;
        ";

        cmd.Parameters.AddWithValue("@key", key.Trim());
        cmd.Parameters.AddWithValue("@value", value.Trim());
        cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetMemoryAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT memory_value FROM user_memory WHERE memory_key = @key;";
        cmd.Parameters.AddWithValue("@key", key.Trim());

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result != null && result != DBNull.Value ? Convert.ToString(result, CultureInfo.InvariantCulture) : null;
    }

    public async Task<IReadOnlyList<UserMemoryEntry>> GetAllMemoriesAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var results = new List<UserMemoryEntry>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, memory_key, memory_value, updated_at
            FROM user_memory
            ORDER BY memory_key ASC;
        ";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new UserMemoryEntry
            {
                Id = reader.GetInt64(0),
                Key = reader.GetString(1),
                Value = reader.GetString(2),
                UpdatedAtUtc = DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<UserMemoryEntry>> SearchMemoriesAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var results = new List<UserMemoryEntry>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        string searchPattern = string.IsNullOrWhiteSpace(pattern) ? "%" : $"%{pattern.Trim()}%";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, memory_key, memory_value, updated_at
            FROM user_memory
            WHERE memory_key LIKE @pattern OR memory_value LIKE @pattern
            ORDER BY memory_key ASC;
        ";
        cmd.Parameters.AddWithValue("@pattern", searchPattern);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new UserMemoryEntry
            {
                Id = reader.GetInt64(0),
                Key = reader.GetString(1),
                Value = reader.GetString(2),
                UpdatedAtUtc = DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow
            });
        }

        return results;
    }

    public async Task<bool> DeleteMemoryAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM user_memory WHERE memory_key = @key;";
        cmd.Parameters.AddWithValue("@key", key.Trim());

        int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task ClearMemoriesAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM user_memory;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
