using Npgsql;

namespace MeetingWeb.Tests.Postgres;

public class PostgresRoleTests
{
    [PostgresTheory]
    [InlineData("CREATE TABLE public.briefyn_permission_probe (id integer)")]
    [InlineData("CREATE TABLE hangfire.briefyn_permission_probe (id integer)")]
    [InlineData("CREATE SCHEMA briefyn_permission_probe")]
    public async Task App_role_cannot_change_schema(string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsRoleAsync("briefyn_app", sql));

        Assert.Equal("42501", error.SqlState);
    }

    [PostgresTheory]
    [InlineData("PasswordHash")]
    [InlineData("SecurityStamp")]
    [InlineData("ConcurrencyStamp")]
    public async Task Readonly_role_cannot_select_credentials(string column)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsRoleAsync("briefyn_okur", $"SELECT \"{column}\" FROM public.\"AspNetUsers\" LIMIT 0"));

        Assert.Equal("42501", error.SqlState);
    }

    [PostgresFact]
    public async Task Readonly_role_cannot_select_user_tokens()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsRoleAsync("briefyn_okur", "SELECT * FROM public.\"AspNetUserTokens\" LIMIT 0"));

        Assert.Equal("42501", error.SqlState);
    }

    [PostgresTheory]
    [InlineData("INSERT INTO public.\"Workspaces\" (\"Name\", \"IsPersonal\", \"CreatedAtUtc\") SELECT 'Yetki testi', FALSE, CURRENT_TIMESTAMP WHERE FALSE")]
    [InlineData("UPDATE public.\"Workspaces\" SET \"Name\" = 'Yetki testi' WHERE FALSE")]
    [InlineData("DELETE FROM public.\"Workspaces\" WHERE FALSE")]
    public async Task Readonly_role_cannot_write_workspaces(string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsRoleAsync("briefyn_okur", sql));

        Assert.Equal("42501", error.SqlState);
    }

    [PostgresTheory]
    [InlineData("briefyn_app", "SELECT \"Id\" FROM public.\"AspNetUsers\" LIMIT 0")]
    [InlineData("briefyn_app", "DELETE FROM public.\"Workspaces\" WHERE FALSE")]
    [InlineData("briefyn_okur", "SELECT \"Email\" FROM public.\"AspNetUsers\" LIMIT 0")]
    public Task Allowed_operations_succeed(string role, string sql) => ExecuteAsRoleAsync(role, sql);

    private static async Task ExecuteAsRoleAsync(string role, string sql)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(PostgresTestServer.RequiredConnectionString) { Database = "briefyn" }.ToString();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var setRole = new NpgsqlCommand($"SET LOCAL ROLE \"{role}\"", connection, transaction);
            await setRole.ExecuteNonQueryAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
