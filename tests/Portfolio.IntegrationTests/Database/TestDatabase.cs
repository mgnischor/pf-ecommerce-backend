using Npgsql;

namespace Portfolio.IntegrationTests.Database;

/// <summary>A database of one test (or one test class), dropped when disposed.</summary>
internal sealed class TestDatabase(string name, string connectionString, string adminConnection) : IDisposable
{
    /// <summary>Name of the database.</summary>
    public string Name { get; } = name;

    /// <summary>Connection string of the database; the throw-away server's credentials, never a secret.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <summary>Opens a pooled data source on the database; the caller disposes it.</summary>
    public NpgsqlDataSource OpenDataSource() => new NpgsqlDataSourceBuilder(ConnectionString).Build();

    /// <summary>Runs one statement as the database owner and returns the first column of the first row.</summary>
    /// <param name="sql">The statement; test code only, never built from input.</param>
    public async Task<object?> ScalarAsync(string sql)
    {
        await using var dataSource = OpenDataSource();
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Runs one query as the database owner and returns the first column of every row as text.</summary>
    /// <param name="sql">The query; test code only, never built from input.</param>
    public async Task<List<string>> StringsAsync(string sql)
    {
        await using var dataSource = OpenDataSource();
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(
                Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            );
        }

        return rows;
    }

    /// <summary>Runs one statement as the database owner.</summary>
    /// <param name="sql">The statement; test code only, never built from input.</param>
    public async Task ExecuteAsync(string sql)
    {
        await using var dataSource = OpenDataSource();
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Drops the database, closing whatever connections the test left open.</summary>
    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();

        using var connection = new NpgsqlConnection(adminConnection);
        connection.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Name} WITH (FORCE)", connection);
        drop.ExecuteNonQuery();
    }
}
