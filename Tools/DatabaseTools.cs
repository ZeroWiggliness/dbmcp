using DbMcp.Data;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace DbMcp.Tools;

[McpServerToolType]
public sealed class DatabaseTools(DatabaseRegistry registry)
{
    private const string DatabaseParam = "Configured database name. Optional only when exactly one database is configured.";

    private IDatabaseAdapter Adapter(string? database)
    {
        var options = registry.Resolve(database);
        return options.Provider == "MongoDb" ? new MongoDatabaseAdapter(options) : new SqlDatabaseAdapter(options);
    }

    [McpServerTool]
    [Description("List tables or collections in a configured database.")]
    public Task<IReadOnlyList<string>> ListTables([Description(DatabaseParam)] string? database = null, CancellationToken cancellationToken = default) =>
        Adapter(database).ListTablesAsync(cancellationToken);

    [McpServerTool]
    [Description("Get table columns and primary keys, or an inferred MongoDB collection schema.")]
    public Task<TableSchema> GetSchema(string table, [Description(DatabaseParam)] string? database = null, CancellationToken cancellationToken = default) =>
        Adapter(database).GetSchemaAsync(table, cancellationToken);

    [McpServerTool]
    [Description("Find one row by primary key, or by an optional lookup column and value. MongoDB uses _id as its key.")]
    public Task<Dictionary<string, object?>?> GetRow(string table, Dictionary<string, JsonElement>? keys = null,
        string? lookupColumn = null, JsonElement? lookupValue = null, [Description(DatabaseParam)] string? database = null, CancellationToken cancellationToken = default) =>
        Adapter(database).GetRowAsync(table, keys ?? [], lookupColumn, lookupValue, cancellationToken);

    [McpServerTool]
    [Description("Read one page of rows, ordered by primary key or MongoDB _id.")]
    public Task<PageResult> GetPage(string table, int pageNumber, [Description(DatabaseParam)] string? database = null, CancellationToken cancellationToken = default) =>
        Adapter(database).GetPageAsync(table, pageNumber, cancellationToken);

    [McpServerTool]
    [Description("Run SQL against a relational database; MongoDB supports a limited single-collection SELECT subset.")]
    public Task<QueryResult> ExecuteSql(string sql, Dictionary<string, JsonElement>? parameters = null, [Description(DatabaseParam)] string? database = null, CancellationToken cancellationToken = default) =>
        Adapter(database).ExecuteSqlAsync(sql, parameters, cancellationToken);

    [McpServerTool]
    [Description("Insert one row or document into a table or collection.")]
    public Task<int> Insert(string table, Dictionary<string, JsonElement> item, [Description(DatabaseParam)] string? database = null, CancellationToken cancellationToken = default) =>
        Adapter(database).InsertAsync(table, item, cancellationToken);

    [McpServerTool]
    [Description("Delete one row by its complete primary key, or one MongoDB document by _id.")]
    public Task<int> Delete(string table, Dictionary<string, JsonElement> keys, [Description(DatabaseParam)] string? database = null, CancellationToken cancellationToken = default) =>
        Adapter(database).DeleteAsync(table, keys, cancellationToken);
}