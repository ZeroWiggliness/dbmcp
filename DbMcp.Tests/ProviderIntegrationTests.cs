using DbMcp.Configuration;
using DbMcp.Data;
using DbMcp.Tools;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System.Text.Json;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DbMcp.Tests;

public class ProviderIntegrationTests
{
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static DatabaseTools Tools(DatabaseOptions options)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DbMcp:Databases:local:Provider"] = options.Provider,
            ["DbMcp:Databases:local:ConnectionString"] = options.ConnectionString,
            ["DbMcp:Databases:local:DefaultDatabase"] = options.DefaultDatabase,
            ["DbMcp:Databases:local:ItemsPerPage"] = options.ItemsPerPage.ToString(),
            ["DbMcp:Databases:local:MaxItems"] = options.MaxItems.ToString()
        }).Build();
        return new DatabaseTools(new DatabaseRegistry(config));
    }

    private static async Task ExerciseSqlAsync(DatabaseOptions options)
    {
        await using (var connection = DatabaseRegistry.OpenSqlConnection(options))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE items (id INT PRIMARY KEY, name VARCHAR(100) NOT NULL)";
            await command.ExecuteNonQueryAsync();
        }

        var adapter = new SqlDatabaseAdapter(options);
        Assert.Contains(await adapter.ListTablesAsync(CancellationToken.None), name => name.EndsWith(".items", StringComparison.OrdinalIgnoreCase));
        var schema = await adapter.GetSchemaAsync("items", CancellationToken.None);
        Assert.Contains(schema.Columns, column => column.Name == "id" && column.PrimaryKey);
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.GetSchemaAsync("missing", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.GetRowAsync("items", [], null, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.GetRowAsync("items", [], "bad", Json(1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.InsertAsync("items", new() { ["bad"] = Json(1) }, CancellationToken.None));

        for (var index = 1; index <= 3; index++)
            Assert.Equal(1, await adapter.InsertAsync("items", new() { ["id"] = Json(index), ["name"] = Json($"item{index}") }, CancellationToken.None));

        var page = await adapter.GetPageAsync("items", 1, CancellationToken.None);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.NextPage);
        Assert.Single((await adapter.GetPageAsync("items", 2, CancellationToken.None)).Items);
        Assert.Null((await adapter.GetPageAsync("items", 2, CancellationToken.None)).NextPage);
        Assert.Empty((await adapter.GetPageAsync("items", 3, CancellationToken.None)).Items);

        var keys = new Dictionary<string, JsonElement> { ["id"] = Json(1) };
        Assert.NotNull(await adapter.GetRowAsync("items", keys, null, null, CancellationToken.None));
        Assert.NotNull(await adapter.GetRowAsync("items", [], "name", Json("item2"), CancellationToken.None));
        Assert.Null(await adapter.GetRowAsync("items", [], "name", Json("missing"), CancellationToken.None));
        Assert.Single((await adapter.ExecuteSqlAsync("SELECT * FROM items WHERE id = @id", keys, CancellationToken.None)).Rows!);
        Assert.Equal(1, (await adapter.ExecuteSqlAsync("DELETE FROM items WHERE id = @id", keys, CancellationToken.None)).AffectedRows);
        Assert.Equal(1, await adapter.DeleteAsync("items", new() { ["id"] = Json(2) }, CancellationToken.None));
        Assert.Equal(0, await adapter.DeleteAsync("items", new() { ["id"] = Json(2) }, CancellationToken.None));

        var tools = Tools(options);
        Assert.Contains(await tools.ListTables("local", CancellationToken.None), name => name.EndsWith(".items", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty((await tools.GetSchema("items", "local", CancellationToken.None)).Columns);
        Assert.NotNull(await tools.GetRow("items", new() { ["id"] = Json(3) }, database: "local"));
        Assert.Single((await tools.GetPage("items", 1, "local", CancellationToken.None)).Items);
        Assert.Single((await tools.ExecuteSql("SELECT * FROM items", database: "local")).Rows!);
        Assert.Equal(1, await tools.Insert("items", new() { ["id"] = Json(4), ["name"] = Json("four") }, "local", CancellationToken.None));
        Assert.Equal(1, await tools.Delete("items", new() { ["id"] = Json(4) }, "local", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.InsertAsync("items", new() { ["id"] = Json(new[] { 1, 2 }) }, CancellationToken.None));

        var qualified = (await adapter.ListTablesAsync(CancellationToken.None)).First(name => name.EndsWith(".items", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty((await adapter.GetSchemaAsync(qualified, CancellationToken.None)).Columns);
        Assert.NotNull((await adapter.ExecuteSqlAsync("SELECT * FROM items WHERE id < @limit", new() { ["limit"] = Json(3.5) }, CancellationToken.None)).Rows);
        Assert.NotNull(await tools.GetRow("items", null, "name", Json("item3"), "local"));

        await using (var connection = DatabaseRegistry.OpenSqlConnection(options))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE logs (message VARCHAR(100) NULL, flag {(options.Provider == "SqlServer" ? "BIT" : "BOOLEAN")} NOT NULL)";
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(1, await adapter.InsertAsync("logs", new() { ["message"] = Json((string?)null), ["flag"] = Json(true) }, CancellationToken.None));
        Assert.Equal(1, await adapter.InsertAsync("logs", new() { ["message"] = Json("done"), ["flag"] = Json(false) }, CancellationToken.None));
        var noKey = await Assert.ThrowsAsync<ArgumentException>(() => adapter.GetPageAsync("logs", 1, CancellationToken.None));
        Assert.Equal("Pagination requires a primary key.", noKey.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.DeleteAsync("logs", new() { ["message"] = Json("done") }, CancellationToken.None));
    }

    [Fact]
    public async Task SqlServer_CrudAndPagination()
    {
        await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await container.StartAsync();
        await ExerciseSqlAsync(new DatabaseOptions { Provider = "SqlServer", ConnectionString = container.GetConnectionString(), DefaultDatabase = "master", ItemsPerPage = 2, MaxItems = 3 });
    }

    [Fact]
    public async Task MySql_CrudAndPagination()
    {
        await using var container = new MySqlBuilder("mysql:8.0").WithDatabase("test").Build();
        await container.StartAsync();
        await ExerciseSqlAsync(new DatabaseOptions { Provider = "MySql", ConnectionString = container.GetConnectionString(), DefaultDatabase = "test", ItemsPerPage = 2, MaxItems = 3 });

        var fromConnectionString = new SqlDatabaseAdapter(new DatabaseOptions { Provider = "MySql", ConnectionString = container.GetConnectionString() });
        Assert.Equal("test.items", (await fromConnectionString.GetSchemaAsync("items", CancellationToken.None)).Name);
    }

    [Fact]
    public async Task Postgres_CrudAndPagination()
    {
        await using var container = new PostgreSqlBuilder("postgres:15.1").WithDatabase("test").Build();
        await container.StartAsync();
        await ExerciseSqlAsync(new DatabaseOptions { Provider = "Postgres", ConnectionString = container.GetConnectionString(), DefaultDatabase = "test", ItemsPerPage = 2, MaxItems = 3 });
    }

    [Fact]
    public async Task MongoDb_CrudAndSelect()
    {
        await using var container = new MongoDbBuilder("mongo:6.0").Build();
        await container.StartAsync();
        var adapter = new MongoDatabaseAdapter(new DatabaseOptions
        {
            Provider = "MongoDb",
            ConnectionString = container.GetConnectionString(),
            DefaultDatabase = "test",
            ItemsPerPage = 2,
            MaxItems = 3
        });
        var ids = new[] { "507f1f77bcf86cd799439011", "507f1f77bcf86cd799439012", "507f1f77bcf86cd799439013" };
        for (var index = 0; index < ids.Length; index++)
            Assert.Equal(1, await adapter.InsertAsync("items", new() { ["_id"] = Json(ids[index]), ["price"] = Json(index + 1) }, CancellationToken.None));

        Assert.Contains("items", await adapter.ListTablesAsync(CancellationToken.None));
        var schema = await adapter.GetSchemaAsync("items", CancellationToken.None);
        Assert.True(schema.Inferred);
        Assert.Contains(schema.Columns, column => column.Name == "price");
        var keys = new Dictionary<string, JsonElement> { ["_id"] = Json(ids[0]) };
        Assert.NotNull(await adapter.GetRowAsync("items", keys, null, null, CancellationToken.None));
        Assert.NotNull(await adapter.GetRowAsync("items", [], "price", Json(2), CancellationToken.None));
        Assert.Null(await adapter.GetRowAsync("items", [], "price", Json(99), CancellationToken.None));
        Assert.NotNull(await adapter.GetRowAsync("items", [], "_id", Json(ids[1]), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.GetSchemaAsync("missing", CancellationToken.None));
        Assert.Equal(3, (await adapter.ExecuteSqlAsync("SELECT * FROM items ORDER BY price ASC LIMIT 10", [], CancellationToken.None)).Rows!.Count);
        Assert.Equal(2, (await adapter.GetPageAsync("items", 1, CancellationToken.None)).NextPage);
        Assert.Single((await adapter.GetPageAsync("items", 2, CancellationToken.None)).Items);
        Assert.Empty((await adapter.GetPageAsync("items", 3, CancellationToken.None)).Items);
        var selected = await adapter.ExecuteSqlAsync("SELECT price FROM items WHERE price >= 2 ORDER BY price DESC LIMIT 1", null, CancellationToken.None);
        Assert.Single(selected.Rows!);
        Assert.Equal(1, await adapter.DeleteAsync("items", keys, CancellationToken.None));
        Assert.Equal(0, await adapter.DeleteAsync("items", keys, CancellationToken.None));

        var tools = Tools(new DatabaseOptions { Provider = "MongoDb", ConnectionString = container.GetConnectionString(), DefaultDatabase = "test" });
        Assert.Contains("items", await tools.ListTables("local", CancellationToken.None));
        Assert.NotEmpty((await tools.GetSchema("items", "local", CancellationToken.None)).Columns);
        Assert.NotNull(await tools.GetRow("items", new() { ["_id"] = Json(ids[1]) }, database: "local"));
        Assert.NotEmpty((await tools.GetPage("items", 1, "local", CancellationToken.None)).Items);
        Assert.NotEmpty((await tools.ExecuteSql("SELECT * FROM items", database: "local")).Rows!);
        Assert.Equal(1, await tools.Insert("items", new() { ["_id"] = Json(ids[0]) }, "local", CancellationToken.None));
        Assert.Equal(1, await tools.Delete("items", keys, "local", CancellationToken.None));

        Assert.Equal(1, await adapter.InsertAsync("items", new()
        {
            ["name"] = Json("extra"),
            ["price"] = Json(2.5),
            ["active"] = Json(true),
            ["deleted"] = Json((string?)null),
            ["details"] = Json(new { code = 1 }),
            ["tags"] = Json(new[] { "a", "b" })
        }, CancellationToken.None));
        Assert.Single((await adapter.ExecuteSqlAsync("SELECT name FROM items WHERE name = 'extra'", null, CancellationToken.None)).Rows!);
        Assert.Equal(2, (await adapter.ExecuteSqlAsync("SELECT * FROM items WHERE (price > 2 AND price <= 3)", null, CancellationToken.None)).Rows!.Count);
        Assert.Equal(2, (await adapter.ExecuteSqlAsync("SELECT * FROM items WHERE price < 3 AND price >= 2", null, CancellationToken.None)).Rows!.Count);
    }
}