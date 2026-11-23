using System.Data.Common;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace MeetingWeb.Tests.Postgres;

public static class PostgresTestServer
{
    public const string MissingInCiMessage = "BRIEFYN_PG_TEST is required in CI.";
    public const string SkipReason = "BRIEFYN_PG_TEST tanımlı değil; PostgreSQL testleri atlandı.";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable("BRIEFYN_PG_TEST") is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static bool IsCi => string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    public static string RequiredConnectionString => ConnectionString
        ?? throw new InvalidOperationException(IsCi ? MissingInCiMessage : SkipReason);
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (PostgresTestServer.ConnectionString == null && !PostgresTestServer.IsCi) Skip = PostgresTestServer.SkipReason;
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (PostgresTestServer.ConnectionString == null && !PostgresTestServer.IsCi) Skip = PostgresTestServer.SkipReason;
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private string? _databaseName;
    private string? _adminConnectionString;
    private string? _connectionString;
    private BriefynFactory? _factory;

    public BriefynFactory Factory => _factory ?? throw new InvalidOperationException("PostgreSQL fixture is not initialized.");
    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("PostgreSQL fixture is not initialized.");
    public ActionItemUpdateInterceptor Interceptor { get; } = new();

    public async Task InitializeAsync()
    {
        if (PostgresTestServer.ConnectionString == null && !PostgresTestServer.IsCi) return;
        var source = PostgresTestServer.RequiredConnectionString;
        _adminConnectionString = new NpgsqlConnectionStringBuilder(source) { Database = "postgres", Pooling = false }.ToString();
        var name = "briefyn_test_" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var connection = new NpgsqlConnection(_adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE template0", connection);
                await command.ExecuteNonQueryAsync();
                _databaseName = name;
            }
            _connectionString = new NpgsqlConnectionStringBuilder(source) { Database = name }.ToString();
            _factory = new BriefynFactory { PostgresConnectionString = _connectionString, CommandInterceptor = Interceptor };
            await _factory.InitializeAsync();
        }
        catch
        {
            // xUnit does not dispose a fixture whose initialization failed.
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_factory != null) await _factory.DisposeAsync();
        }
        finally
        {
            if (_connectionString != null)
            {
                using var poolConnection = new NpgsqlConnection(_connectionString);
                NpgsqlConnection.ClearPool(poolConnection);
            }
            if (_databaseName != null)
            {
                await using var connection = new NpgsqlConnection(_adminConnectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
                await command.ExecuteNonQueryAsync();
                _databaseName = null;
            }
        }
    }
}

public sealed class ActionItemUpdateInterceptor : DbCommandInterceptor
{
    private Action? _beforeUpdate;

    public Action? BeforeUpdate
    {
        get => _beforeUpdate;
        set => Interlocked.Exchange(ref _beforeUpdate, value);
    }

    private void BeforeCommand(DbCommand command)
    {
        if (command.CommandText.Contains("UPDATE \"ActionItems\"", StringComparison.Ordinal))
            Interlocked.Exchange(ref _beforeUpdate, null)?.Invoke();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        BeforeCommand(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        BeforeCommand(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        BeforeCommand(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        BeforeCommand(command);
        return ValueTask.FromResult(result);
    }
}
