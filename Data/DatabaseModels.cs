using System.Text.Json;

namespace DbMcp.Data;

public sealed record ColumnInfo(string Name, string DataType, bool Nullable, bool PrimaryKey);
public sealed record TableSchema(string Name, IReadOnlyList<ColumnInfo> Columns, bool Inferred = false);
public sealed record PageResult(IReadOnlyList<Dictionary<string, object?>> Items, int PageNumber, int ItemsPerPage, int MaxItems, int? NextPage);
public sealed record QueryResult(IReadOnlyList<Dictionary<string, object?>>? Rows, int AffectedRows);

public interface IDatabaseAdapter
{
    Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken);
    Task<TableSchema> GetSchemaAsync(string table, CancellationToken cancellationToken);
    Task<Dictionary<string, object?>?> GetRowAsync(string table, Dictionary<string, JsonElement> keys, string? lookupColumn, JsonElement? lookupValue, CancellationToken cancellationToken);
    Task<PageResult> GetPageAsync(string table, int pageNumber, CancellationToken cancellationToken);
    Task<QueryResult> ExecuteSqlAsync(string sql, Dictionary<string, JsonElement>? parameters, CancellationToken cancellationToken);
    Task<int> InsertAsync(string table, Dictionary<string, JsonElement> item, CancellationToken cancellationToken);
    Task<int> DeleteAsync(string table, Dictionary<string, JsonElement> keys, CancellationToken cancellationToken);
}