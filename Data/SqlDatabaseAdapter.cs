using DbMcp.Configuration;
using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace DbMcp.Data;

public sealed class SqlDatabaseAdapter(DatabaseOptions options) : IDatabaseAdapter
{
    private string DefaultSchema => options.Provider switch
    {
        "SqlServer" => "dbo",
        "Postgres" => "public",
        _ => options.DefaultDatabase ?? new MySqlConnector.MySqlConnectionStringBuilder(options.ConnectionString ?? "").Database
    };

    private (string Schema, string Name) SplitTable(string table)
    {
        var parts = table.Split('.');
        if (parts.Length is < 1 or > 2 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Any(character => !char.IsLetterOrDigit(character) && character != '_')))
            throw new ArgumentException("Invalid table name.");
        return parts.Length == 2 ? (parts[0], parts[1]) : (DefaultSchema, parts[0]);
    }

    private string Quote(string identifier) => options.Provider switch
    {
        "SqlServer" => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]",
        "MySql" => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`",
        _ => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
    };

    private async Task<DbConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var connection = DatabaseRegistry.OpenSqlConnection(options);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static DbParameter Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var number) ? number : value.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ArgumentException("Only scalar JSON values are accepted for SQL fields.")
    };

    private static async Task<List<Dictionary<string, object?>>> ReadAsync(DbCommand command, int limit, CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (rows.Count < limit && await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
                row[reader.GetName(index)] = await reader.IsDBNullAsync(index, cancellationToken) ? null : reader.GetValue(index);
            rows.Add(row);
        }
        return rows;
    }

    public async Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await ConnectAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_schema, table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' ORDER BY table_schema, table_name";
        var rows = await ReadAsync(command, int.MaxValue, cancellationToken);
        return rows.Select(row => $"{row["table_schema"]}.{row["table_name"]}").ToArray();
    }

    public async Task<TableSchema> GetSchemaAsync(string table, CancellationToken cancellationToken)
    {
        var (schema, name) = SplitTable(table);
        await using var connection = await ConnectAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.column_name, c.data_type, c.is_nullable,
                   CASE WHEN pk.column_name IS NULL THEN 0 ELSE 1 END AS is_primary
            FROM information_schema.columns c
            LEFT JOIN (
                SELECT k.table_schema, k.table_name, k.column_name
                FROM information_schema.table_constraints t
                JOIN information_schema.key_column_usage k
                  ON t.constraint_name = k.constraint_name AND t.table_schema = k.table_schema
                 AND t.table_name = k.table_name
                WHERE t.constraint_type = 'PRIMARY KEY'
            ) pk ON pk.table_schema = c.table_schema AND pk.table_name = c.table_name AND pk.column_name = c.column_name
            WHERE c.table_schema = @schema AND c.table_name = @table
            ORDER BY c.ordinal_position
            """;
        Add(command, "schema", schema);
        Add(command, "table", name);
        var rows = await ReadAsync(command, int.MaxValue, cancellationToken);
        if (rows.Count == 0)
            throw new ArgumentException("Table not found.");
        return new TableSchema($"{schema}.{name}", rows.Select(row => new ColumnInfo(
            (string)row["column_name"]!, (string)row["data_type"]!,
            (string)row["is_nullable"]! == "YES", Convert.ToInt32(row["is_primary"]) == 1)).ToArray());
    }

    private async Task<(TableSchema Schema, string Qualified)> TableAsync(string table, CancellationToken cancellationToken)
    {
        var schema = await GetSchemaAsync(table, cancellationToken);
        var (schemaName, name) = SplitTable(table);
        return (schema, $"{Quote(schemaName)}.{Quote(name)}");
    }

    private static void ValidateColumns(TableSchema schema, IEnumerable<string> fields)
    {
        foreach (var field in fields)
            if (!schema.Columns.Any(column => string.Equals(column.Name, field, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Unknown column name.");
    }

    private static void ValidateKeys(TableSchema schema, Dictionary<string, JsonElement> keys)
    {
        var primary = schema.Columns.Where(column => column.PrimaryKey).Select(column => column.Name).ToArray();
        if (primary.Length == 0 || primary.Length != keys.Count || primary.Any(key => !keys.ContainsKey(key)))
            throw new ArgumentException("All primary key columns must be supplied.");
    }

    public async Task<Dictionary<string, object?>?> GetRowAsync(string table, Dictionary<string, JsonElement> keys, string? lookupColumn, JsonElement? lookupValue, CancellationToken cancellationToken)
    {
        var (schema, qualified) = await TableAsync(table, cancellationToken);
        if (lookupColumn is not null)
        {
            ValidateColumns(schema, [lookupColumn]);
            if (lookupValue is null) throw new ArgumentException("Lookup value required.");
            keys = new Dictionary<string, JsonElement> { [lookupColumn] = lookupValue.Value };
        }
        else ValidateKeys(schema, keys);
        await using var connection = await ConnectAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {qualified} WHERE " + string.Join(" AND ", keys.Keys.Select((key, index) => $"{Quote(key)} = @v{index}"));
        var position = 0;
        foreach (var value in keys.Values) Add(command, $"v{position++}", Value(value));
        return (await ReadAsync(command, 1, cancellationToken)).FirstOrDefault();
    }

    public async Task<PageResult> GetPageAsync(string table, int pageNumber, CancellationToken cancellationToken)
    {
        if (pageNumber < 1) throw new ArgumentException("Page numbers start at 1.");
        var offset = (long)(pageNumber - 1) * options.ItemsPerPage;
        if (offset >= options.MaxItems) return new PageResult([], pageNumber, options.ItemsPerPage, options.MaxItems, null);
        var count = (int)Math.Min(options.ItemsPerPage, options.MaxItems - offset);
        var (schema, qualified) = await TableAsync(table, cancellationToken);
        var keys = schema.Columns.Where(column => column.PrimaryKey).Select(column => Quote(column.Name)).ToArray();
        if (keys.Length == 0) throw new ArgumentException("Pagination requires a primary key.");
        await using var connection = await ConnectAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = options.Provider == "SqlServer"
            ? $"SELECT * FROM {qualified} ORDER BY {string.Join(", ", keys)} OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY"
            : $"SELECT * FROM {qualified} ORDER BY {string.Join(", ", keys)} LIMIT @take OFFSET @offset";
        Add(command, "offset", offset);
        Add(command, "take", count + 1);
        var rows = await ReadAsync(command, count + 1, cancellationToken);
        var nextPage = rows.Count > count && offset + count < options.MaxItems ? pageNumber + 1 : (int?)null;
        return new PageResult(rows.Take(count).ToArray(), pageNumber, options.ItemsPerPage, options.MaxItems, nextPage);
    }

    public async Task<QueryResult> ExecuteSqlAsync(string sql, Dictionary<string, JsonElement>? parameters, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL required.");
        await using var connection = await ConnectAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters ?? []) Add(command, parameter.Key, Value(parameter.Value));
        if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            sql.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            var rows = await ReadAsync(command, options.MaxItems, cancellationToken);
            return new QueryResult(rows, 0);
        }
        return new QueryResult(null, await command.ExecuteNonQueryAsync(cancellationToken));
    }

    public async Task<int> InsertAsync(string table, Dictionary<string, JsonElement> item, CancellationToken cancellationToken)
    {
        if (item.Count == 0) throw new ArgumentException("Item cannot be empty.");
        var (schema, qualified) = await TableAsync(table, cancellationToken);
        ValidateColumns(schema, item.Keys);
        await using var connection = await ConnectAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {qualified} ({string.Join(", ", item.Keys.Select(Quote))}) VALUES ({string.Join(", ", item.Keys.Select((_, index) => $"@v{index}"))})";
        var position = 0;
        foreach (var value in item.Values) Add(command, $"v{position++}", Value(value));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteAsync(string table, Dictionary<string, JsonElement> keys, CancellationToken cancellationToken)
    {
        var (schema, qualified) = await TableAsync(table, cancellationToken);
        ValidateKeys(schema, keys);
        await using var connection = await ConnectAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {qualified} WHERE " + string.Join(" AND ", keys.Keys.Select((key, index) => $"{Quote(key)} = @v{index}"));
        var position = 0;
        foreach (var value in keys.Values) Add(command, $"v{position++}", Value(value));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}