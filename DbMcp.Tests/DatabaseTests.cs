using DbMcp.Configuration;
using DbMcp.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace DbMcp.Tests;

public class DatabaseTests
{
    private static readonly DatabaseOptions Mongo = new()
    {
        Provider = "MongoDb",
        Address = "localhost",
        DefaultDatabase = "test",
        ItemsPerPage = 2,
        MaxItems = 5
    };

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("Postgres")]
    [InlineData("MongoDb")]
    public void Resolve_ConfiguredProvider_ReturnsOptions(string provider)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DbMcp:Databases:local:Provider"] = provider,
            ["DbMcp:Databases:local:Address"] = "localhost",
            ["DbMcp:Databases:local:DefaultDatabase"] = "test"
        }).Build();

        Assert.Equal(provider, new DatabaseRegistry(config).Resolve("local").Provider);
    }

    [Theory]
    [InlineData(null, "Unknown database name.")]
    [InlineData("Bad", "Unsupported database provider.")]
    [InlineData("MongoDb", "Database address and default database are required.")]
    public void Resolve_InvalidConfig_Throws(string? provider, string message)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(provider is null ? [] : new Dictionary<string, string?>
        {
            ["DbMcp:Databases:local:Provider"] = provider
        }).Build();
        var error = Assert.Throws<ArgumentException>(() => new DatabaseRegistry(config).Resolve("local"));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Resolve_NoName_UsesSingleConfiguredDatabase()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DbMcp:Databases:local:Provider"] = "Postgres",
            ["DbMcp:Databases:local:Address"] = "localhost",
            ["DbMcp:Databases:local:DefaultDatabase"] = "test"
        }).Build();

        Assert.Equal("Postgres", new DatabaseRegistry(config).Resolve(null).Provider);
    }

    [Fact]
    public void Resolve_NoName_WithMultipleDatabases_Throws()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DbMcp:Databases:a:Provider"] = "Postgres",
            ["DbMcp:Databases:b:Provider"] = "Postgres"
        }).Build();

        Assert.Throws<ArgumentException>(() => new DatabaseRegistry(config).Resolve(null));
    }

    [Fact]
    public void Resolve_InvalidPageSize_Throws()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DbMcp:Databases:local:Provider"] = "Postgres",
            ["DbMcp:Databases:local:ItemsPerPage"] = "0"
        }).Build();
        Assert.Throws<ArgumentException>(() => new DatabaseRegistry(config).Resolve("local"));
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("Postgres")]
    public void OpenSqlConnection_BuildsProviderConnection(string provider)
    {
        var options = new DatabaseOptions { Provider = provider, Address = "localhost", DefaultDatabase = "test", Port = 1234, Username = "user", Password = "password" };
        using var connection = DatabaseRegistry.OpenSqlConnection(options);
        Assert.Equal(provider switch
        {
            "SqlServer" => typeof(SqlConnection),
            "MySql" => typeof(MySqlConnection),
            _ => typeof(NpgsqlConnection)
        }, connection.GetType());
        Assert.Contains("1234", connection.ConnectionString);
    }

    [Fact]
    public void OpenSqlConnection_UnsupportedProvider_Throws()
    {
        Assert.Throws<ArgumentException>(() => DatabaseRegistry.OpenSqlConnection(new DatabaseOptions { Provider = "MongoDb" }));
    }

    [Theory]
    [InlineData("SELECT * FROM items JOIN other ON items.id = other.id")]
    [InlineData("SELECT * FROM items WHERE price = 1 OR price = 2")]
    [InlineData("SELECT COUNT(*) FROM items")]
    [InlineData("SELECT * FROM items LIMIT -1")]
    [InlineData("DELETE FROM items")]
    [InlineData("SELECT * FROM items; DELETE FROM items")]
    [InlineData("SELECT * FROM items OFFSET 2")]
    public async Task ExecuteSqlAsync_UnsupportedQuery_Throws(string sql)
    {
        var adapter = new MongoDatabaseAdapter(Mongo);
        await Assert.ThrowsAnyAsync<Exception>(() => adapter.ExecuteSqlAsync(sql, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetPageAsync_InvalidPage_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).GetPageAsync("items", 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlDatabaseAdapter(new DatabaseOptions()).GetPageAsync("items", 0, CancellationToken.None));
    }

    [Theory]
    [InlineData("bad-table")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public async Task GetSchemaAsync_InvalidName_Throws(string table)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlDatabaseAdapter(new DatabaseOptions()).GetSchemaAsync(table, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteSqlAsync_EmptySql_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlDatabaseAdapter(new DatabaseOptions()).ExecuteSqlAsync("  ", null, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WithoutId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).DeleteAsync("items", [], CancellationToken.None));
    }

    [Fact]
    public async Task GetRowAsync_WithoutId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).GetRowAsync("items", [], null, null, CancellationToken.None));
    }

    [Fact]
    public async Task InsertAsync_EmptyDocument_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).InsertAsync("items", [], CancellationToken.None));
    }
}