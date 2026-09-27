using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace OpenAgent.Data;

public sealed class SqliteAuditRepository : IAuditRepository
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public string DatabasePath => _databasePath;

    static SqliteAuditRepository()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
        }
        catch
        {
            // Ignore if already initialized or managed by host
        }
    }

    public SqliteAuditRepository(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string agentDir = Path.Combine(appData, "OpenAgent");
            _databasePath = Path.Combine(agentDir, "audit_trail.db");
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

            // Enable WAL mode for high concurrency & low overhead
            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS tool_audit_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp_utc TEXT NOT NULL,
                    session_id TEXT,
                    tool_name TEXT NOT NULL,
                    risk_level TEXT NOT NULL,
                    arguments_json TEXT NOT NULL,
                    is_success INTEGER NOT NULL,
                    result_preview TEXT,
                    error_message TEXT,
                    duration_ms INTEGER NOT NULL,
                    user_confirmed INTEGER
                );

                CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON tool_audit_logs(timestamp_utc DESC);
                CREATE INDEX IF NOT EXISTS idx_audit_tool ON tool_audit_logs(tool_name);
            ";

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = createTableSql;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task LogExecutionAsync(ToolAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string insertSql = @"
            INSERT INTO tool_audit_logs (
                timestamp_utc,
                session_id,
                tool_name,
                risk_level,
                arguments_json,
                is_success,
                result_preview,
                error_message,
                duration_ms,
                user_confirmed
            ) VALUES (
                @timestamp_utc,
                @session_id,
                @tool_name,
                @risk_level,
                @arguments_json,
                @is_success,
                @result_preview,
                @error_message,
                @duration_ms,
                @user_confirmed
            );
            SELECT last_insert_rowid();
        ";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = insertSql;

        cmd.Parameters.AddWithValue("@timestamp_utc", entry.TimestampUtc.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@session_id", (object?)entry.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tool_name", entry.ToolName);
        cmd.Parameters.AddWithValue("@risk_level", entry.RiskLevel);
        cmd.Parameters.AddWithValue("@arguments_json", entry.ArgumentsJson ?? "{}");
        cmd.Parameters.AddWithValue("@is_success", entry.IsSuccess ? 1 : 0);
        cmd.Parameters.AddWithValue("@result_preview", (object?)entry.ResultPreview ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", (object?)entry.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@duration_ms", entry.ExecutionDurationMs);
        cmd.Parameters.AddWithValue("@user_confirmed", entry.UserConfirmed.HasValue ? (entry.UserConfirmed.Value ? 1 : 0) : DBNull.Value);

        var idResult = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (idResult != null && idResult != DBNull.Value)
        {
            entry.Id = Convert.ToInt64(idResult, CultureInfo.InvariantCulture);
        }
    }

    public async Task<IReadOnlyList<ToolAuditEntry>> GetRecentLogsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var results = new List<ToolAuditEntry>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string selectSql = @"
            SELECT
                id,
                timestamp_utc,
                session_id,
                tool_name,
                risk_level,
                arguments_json,
                is_success,
                result_preview,
                error_message,
                duration_ms,
                user_confirmed
            FROM tool_audit_logs
            ORDER BY id DESC
            LIMIT @limit;
        ";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = selectSql;
        cmd.Parameters.AddWithValue("@limit", Math.Max(1, limit));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var entry = new ToolAuditEntry
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow,
                SessionId = reader.IsDBNull(2) ? null : reader.GetString(2),
                ToolName = reader.GetString(3),
                RiskLevel = reader.GetString(4),
                ArgumentsJson = reader.GetString(5),
                IsSuccess = reader.GetInt32(6) == 1,
                ResultPreview = reader.IsDBNull(7) ? null : reader.GetString(7),
                ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                ExecutionDurationMs = reader.GetInt64(9),
                UserConfirmed = reader.IsDBNull(10) ? null : reader.GetInt32(10) == 1
            };
            results.Add(entry);
        }

        return results;
    }

    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tool_audit_logs;";
        var count = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return count != null && count != DBNull.Value ? Convert.ToInt32(count) : 0;
    }

    public async Task ClearLogsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM tool_audit_logs;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
