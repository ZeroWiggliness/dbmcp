using DbMcp.Configuration;
using DbMcp.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Npgsql;
using System.Text.Json;
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

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Resolve_BlankName_UsesSingleConfiguredDatabase()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DbMcp:Databases:local:Provider"] = "MySql",
            ["DbMcp:Databases:local:ConnectionString"] = "Server=localhost;Database=test"
        }).Build();

        Assert.Equal("MySql", new DatabaseRegistry(config).Resolve("  ").Provider);
    }

    [Fact]
    public void OpenSqlConnection_ConnectionStringWithMongoProvider_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() => DatabaseRegistry.OpenSqlConnection(new DatabaseOptions { Provider = "MongoDb", ConnectionString = "mongodb://localhost" }));
        Assert.Equal("Not a SQL provider.", error.Message);
    }

    [Fact]
    public void OpenSqlConnection_SqlServerWithoutPort_OmitsPort()
    {
        using var connection = DatabaseRegistry.OpenSqlConnection(new DatabaseOptions { Provider = "SqlServer", Address = "localhost", DefaultDatabase = "test", Username = "user", Password = "password" });
        Assert.Equal("localhost", new SqlConnectionStringBuilder(connection.ConnectionString).DataSource);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void OpenSqlConnection_SqlServerWithoutUsername_ThrowsClearError(string? username)
    {
        var error = Assert.Throws<ArgumentException>(() => DatabaseRegistry.OpenSqlConnection(new DatabaseOptions { Provider = "SqlServer", Address = "localhost", DefaultDatabase = "test", Username = username }));
        Assert.Equal("SQL Server requires a username unless a connection string is configured.", error.Message);
    }

    [Fact]
    public void OpenSqlConnection_SqlServerWithoutPassword_BuildsConnection()
    {
        using var connection = DatabaseRegistry.OpenSqlConnection(new DatabaseOptions { Provider = "SqlServer", Address = "localhost", DefaultDatabase = "test", Username = "user" });
        Assert.Equal("user", new SqlConnectionStringBuilder(connection.ConnectionString).UserID);
    }

    [Theory]
    [InlineData(null, "mongodb://localhost/fromurl", "fromurl")]
    [InlineData("test", null, "test")]
    public void OpenMongoDatabase_ResolvesDatabaseName(string? defaultDatabase, string? connectionString, string expected)
    {
        var database = DatabaseRegistry.OpenMongoDatabase(new DatabaseOptions
        {
            Provider = "MongoDb",
            Address = "localhost",
            Username = "user",
            Password = "password",
            DefaultDatabase = defaultDatabase,
            ConnectionString = connectionString
        });
        Assert.Equal(expected, database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void OpenMongoDatabase_WithoutDatabaseName_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() => DatabaseRegistry.OpenMongoDatabase(new DatabaseOptions { Provider = "MongoDb", ConnectionString = "mongodb://localhost" }));
        Assert.Equal("MongoDB requires a default database.", error.Message);
    }

    [Theory]
    [InlineData("system.users")]
    [InlineData("a$b")]
    [InlineData("a\0b")]
    [InlineData(" ")]
    public async Task MongoCollection_InvalidName_Throws(string table)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).GetPageAsync(table, 1, CancellationToken.None));
        Assert.Equal("Invalid collection name.", error.Message);
    }

    [Fact]
    public async Task MongoInsertAsync_ConvertsValuesBeforeValidatingCollection()
    {
        var item = new Dictionary<string, JsonElement>
        {
            ["_id"] = Json("not-an-object-id"),
            ["active"] = Json(false),
            ["price"] = Json(1.5)
        };
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).InsertAsync("system.items", item, CancellationToken.None));
        Assert.Equal("Invalid collection name.", error.Message);
    }

    [Fact]
    public async Task MongoInsertAsync_UndefinedValue_Throws()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).InsertAsync("items", new() { ["value"] = default }, CancellationToken.None));
        Assert.Equal("Unsupported JSON value.", error.Message);
    }

    [Theory]
    [InlineData("$set")]
    [InlineData("a\0b")]
    public async Task MongoInsertAsync_InvalidField_Throws(string field)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).InsertAsync("items", new() { [field] = Json(1) }, CancellationToken.None));
    }

    [Fact]
    public async Task MongoDeleteAsync_NonStringId_ValidatesCollection()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).DeleteAsync("system.items", new() { ["_id"] = Json(5) }, CancellationToken.None));
    }

    [Theory]
    [InlineData("price", false)]
    [InlineData("$where", true)]
    [InlineData("a\0b", true)]
    public async Task MongoGetRowAsync_InvalidLookup_Throws(string column, bool hasValue)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).GetRowAsync("items", [], column, hasValue ? Json(1) : null, CancellationToken.None));
    }

    [Fact]
    public async Task MongoExecuteSqlAsync_WithParameters_Throws()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).ExecuteSqlAsync("SELECT * FROM items", new() { ["id"] = Json(1) }, CancellationToken.None));
        Assert.Equal("MongoDB SQL does not support query parameters.", error.Message);
    }

    [Theory]
    [InlineData("SELECT * FROM items WHERE price <> 1")]
    [InlineData("SELECT * FROM items WHERE price = NULL")]
    [InlineData("SELECT * FROM items WHERE price + 1 = 2")]
    [InlineData("SELECT * FROM items WHERE price")]
    [InlineData("SELECT * FROM items ORDER BY price + 1")]
    [InlineData("SELECT price AS cost FROM items")]
    [InlineData("SELECT * FROM items LIMIT 0")]
    [InlineData("SELECT * FROM items LIMIT 'x'")]
    public async Task MongoExecuteSqlAsync_UnsupportedExpression_ThrowsBeforeQuerying(string sql)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new MongoDatabaseAdapter(Mongo).ExecuteSqlAsync(sql, null, CancellationToken.None));
    }

    [Fact]
    public async Task SqlConnectAsync_Unreachable_Throws()
    {
        var adapter = new SqlDatabaseAdapter(new DatabaseOptions { Provider = "Postgres", ConnectionString = "Host=127.0.0.1;Port=1;Database=test;Username=user;Password=password;Timeout=2" });
        await Assert.ThrowsAnyAsync<Exception>(() => adapter.ListTablesAsync(CancellationToken.None));
    }
}