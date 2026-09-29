using DbMcp.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using SqlParser;
using SqlParser.Ast;
using System.Text.Json;

namespace DbMcp.Data;

public sealed class MongoDatabaseAdapter(DatabaseOptions options) : IDatabaseAdapter
{
    private readonly IMongoDatabase database = DatabaseRegistry.OpenMongoDatabase(options);

    private IMongoCollection<BsonDocument> Collection(string table)
    {
        if (string.IsNullOrWhiteSpace(table) || table.StartsWith("system.", StringComparison.OrdinalIgnoreCase) ||
            table.Contains('$') || table.Contains('\0'))
            throw new ArgumentException("Invalid collection name.");
        return database.GetCollection<BsonDocument>(table);
    }

    private static BsonValue ToBson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new BsonString(value.GetString()),
        JsonValueKind.Number => value.TryGetInt64(out var number) ? new BsonInt64(number) : new BsonDouble(value.GetDouble()),
        JsonValueKind.True => BsonBoolean.True,
        JsonValueKind.False => BsonBoolean.False,
        JsonValueKind.Null => BsonNull.Value,
        JsonValueKind.Object => new BsonDocument(value.EnumerateObject().Select(field => new BsonElement(field.Name, ToBson(field.Value)))),
        JsonValueKind.Array => new BsonArray(value.EnumerateArray().Select(ToBson)),
        _ => throw new ArgumentException("Unsupported JSON value.")
    };

    private static BsonValue Id(JsonElement value)
    {
        var bson = ToBson(value);
        return bson is BsonString text && ObjectId.TryParse(text.Value, out var id) ? new BsonObjectId(id) : bson;
    }

    private static Dictionary<string, object?> Row(BsonDocument document) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(document.ToJson())!;

    public async Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken) =>
        (await (await database.ListCollectionNamesAsync(cancellationToken: cancellationToken)).ToListAsync(cancellationToken)).ToArray();

    public async Task<TableSchema> GetSchemaAsync(string table, CancellationToken cancellationToken)
    {
        var collection = Collection(table);
        var names = await ListTablesAsync(cancellationToken);
        if (!names.Contains(table, StringComparer.Ordinal)) throw new ArgumentException("Collection not found.");
        var fields = new Dictionary<string, (string Type, bool Nullable)> { ["_id"] = ("ObjectId or scalar", false) };
        var samples = await collection.Find(FilterDefinition<BsonDocument>.Empty).Limit(100).ToListAsync(cancellationToken);
        foreach (var document in samples)
            foreach (var element in document)
                fields[element.Name] = (element.Value.BsonType.ToString(), element.Value.IsBsonNull);
        return new TableSchema(table, fields.Select(field => new ColumnInfo(field.Key, field.Value.Type, field.Value.Nullable, field.Key == "_id")).ToArray(), true);
    }

    public async Task<Dictionary<string, object?>?> GetRowAsync(string table, Dictionary<string, JsonElement> keys, string? lookupColumn, JsonElement? lookupValue, CancellationToken cancellationToken)
    {
        var collection = Collection(table);
        BsonDocument filter;
        if (lookupColumn is not null)
        {
            if (lookupValue is null || lookupColumn.StartsWith('$') || lookupColumn.Contains('\0')) throw new ArgumentException("Invalid lookup field or value.");
            filter = new BsonDocument(lookupColumn, lookupColumn == "_id" ? Id(lookupValue.Value) : ToBson(lookupValue.Value));
        }
        else
        {
            if (keys.Count != 1 || !keys.TryGetValue("_id", out var key)) throw new ArgumentException("MongoDB lookup requires _id.");
            filter = new BsonDocument("_id", Id(key));
        }
        var document = await collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Row(document);
    }

    public async Task<PageResult> GetPageAsync(string table, int pageNumber, CancellationToken cancellationToken)
    {
        if (pageNumber < 1) throw new ArgumentException("Page numbers start at 1.");
        var offset = (long)(pageNumber - 1) * options.ItemsPerPage;
        if (offset >= options.MaxItems) return new PageResult([], pageNumber, options.ItemsPerPage, options.MaxItems, null);
        var count = (int)Math.Min(options.ItemsPerPage, options.MaxItems - offset);
        var rows = await Collection(table).Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id")).Skip((int)offset).Limit(count + 1).ToListAsync(cancellationToken);
        var nextPage = rows.Count > count && offset + count < options.MaxItems ? pageNumber + 1 : (int?)null;
        return new PageResult(rows.Take(count).Select(Row).ToArray(), pageNumber, options.ItemsPerPage, options.MaxItems, nextPage);
    }

    public async Task<QueryResult> ExecuteSqlAsync(string sql, Dictionary<string, JsonElement>? parameters, CancellationToken cancellationToken)
    {
        if (parameters is { Count: > 0 }) throw new ArgumentException("MongoDB SQL does not support query parameters.");
        var statements = new SqlQueryParser().Parse(sql);
        if (statements.Count != 1 || statements[0] is not Statement.Select statement ||
            statement.Query is not { Body: SetExpression.SelectExpression body } query || query.With is not null ||
            body.Select.From is not { Count: 1 } tables || tables[0].Relation is not TableFactor.Table table ||
            tables[0].Joins is { Count: > 0 } || table.Alias is not null || body.Select.Distinct is not null ||
            body.Select.GroupBy is not null || body.Select.Having is not null || body.Select.Into is not null ||
            body.Select.Top is not null || body.Select.PreWhere is not null || body.Select.QualifyBy is not null ||
            body.Select.LateralViews is { Count: > 0 } || body.Select.ClusterBy is { Count: > 0 } ||
            body.Select.DistributeBy is { Count: > 0 } || body.Select.SortBy is { Count: > 0 } ||
            body.Select.NamedWindow is { Count: > 0 } || body.Select.ConnectBy is not null ||
            body.Select.ValueTableMode is not null || table.Args is not null || table.WithHints is { Count: > 0 } ||
            table.Version is not null || table.WithOrdinality || table.Partitions is { Count: > 0 } ||
            query.Offset is not null || query.Fetch is not null || query.ForClause is not null ||
            query.Locks is { Count: > 0 } || query.LimitBy is { Count: > 0 } ||
            query.Settings is { Count: > 0 } || query.FormatClause is not null)
            throw new ArgumentException("Only single-collection SELECT queries are supported for MongoDB.");

        var collectionName = table.Name.ToString();
        var collection = Collection(collectionName);
        var filter = body.Select.Selection is null ? FilterDefinition<BsonDocument>.Empty : Filter(body.Select.Selection);
        var result = collection.Find(filter);
        if (query.OrderBy?.Expressions is { Count: > 0 } order)
        {
            if (query.OrderBy.Interpolate is not null) throw new ArgumentException("Unsupported ORDER BY.");
            var sort = order.Select(entry => entry.Asc == false
                ? Builders<BsonDocument>.Sort.Descending(Field(entry.Expression))
                : Builders<BsonDocument>.Sort.Ascending(Field(entry.Expression))).ToArray();
            result = result.Sort(Builders<BsonDocument>.Sort.Combine(sort));
        }
        var take = query.Limit is null ? options.MaxItems : Math.Min(options.MaxItems, PositiveLimit(query.Limit));
        result = result.Limit(take);
        if (body.Select.Projection.Count != 1 || body.Select.Projection[0] is not SelectItem.Wildcard)
        {
            var columns = body.Select.Projection.Select(item => item is SelectItem.UnnamedExpression field
                ? Builders<BsonDocument>.Projection.Include(Field(field.Expression))
                : throw new ArgumentException("Unsupported SELECT projection.")).ToArray();
            result = result.Project<BsonDocument>(Builders<BsonDocument>.Projection.Combine(columns));
        }
        var rows = await result.ToListAsync(cancellationToken);
        return new QueryResult(rows.Select(Row).ToArray(), 0);
    }

    private static int PositiveLimit(Expression expression) => expression is Expression.LiteralValue { Value: Value.Number number } &&
        int.TryParse(number.Value, out var count) && count > 0 ? count : throw new ArgumentException("LIMIT must be a positive integer.");

    private static string Field(Expression expression) => expression is Expression.Identifier identifier &&
        !identifier.Ident.Value.StartsWith('$') && !identifier.Ident.Value.Contains('\0')
        ? identifier.Ident.Value : throw new ArgumentException("Only plain field identifiers are supported.");

    private static BsonValue Literal(Expression expression) => expression switch
    {
        Expression.LiteralValue { Value: Value.SingleQuotedString text } => new BsonString(text.Value),
        Expression.LiteralValue { Value: Value.Number number } when long.TryParse(number.Value, out var integer) => new BsonInt64(integer),
        Expression.LiteralValue { Value: Value.Number number } when double.TryParse(number.Value, System.Globalization.CultureInfo.InvariantCulture, out var fraction) => new BsonDouble(fraction),
        Expression.LiteralValue { Value: Value.Boolean boolean } => new BsonBoolean(boolean.Value),
        _ => throw new ArgumentException("Unsupported SQL literal.")
    };

    private static FilterDefinition<BsonDocument> Filter(Expression expression) => expression switch
    {
        Expression.Nested nested => Filter(nested.Expression),
        Expression.BinaryOp { Op: BinaryOperator.And } and => Builders<BsonDocument>.Filter.And(Filter(and.Left), Filter(and.Right)),
        Expression.BinaryOp binary => binary.Op switch
        {
            BinaryOperator.Eq => Builders<BsonDocument>.Filter.Eq(Field(binary.Left), Literal(binary.Right)),
            BinaryOperator.Gt => Builders<BsonDocument>.Filter.Gt(Field(binary.Left), Literal(binary.Right)),
            BinaryOperator.GtEq => Builders<BsonDocument>.Filter.Gte(Field(binary.Left), Literal(binary.Right)),
            BinaryOperator.Lt => Builders<BsonDocument>.Filter.Lt(Field(binary.Left), Literal(binary.Right)),
            BinaryOperator.LtEq => Builders<BsonDocument>.Filter.Lte(Field(binary.Left), Literal(binary.Right)),
            _ => throw new ArgumentException("Unsupported WHERE operator.")
        },
        _ => throw new ArgumentException("Unsupported WHERE expression.")
    };

    public async Task<int> InsertAsync(string table, Dictionary<string, JsonElement> item, CancellationToken cancellationToken)
    {
        if (item.Count == 0 || item.Keys.Any(key => key.StartsWith('$') || key.Contains('\0')))
            throw new ArgumentException("Invalid document.");
        var document = new BsonDocument(item.Select(field => new BsonElement(field.Key,
            field.Key == "_id" ? Id(field.Value) : ToBson(field.Value))));
        await Collection(table).InsertOneAsync(document, cancellationToken: cancellationToken);
        return 1;
    }

    public async Task<int> DeleteAsync(string table, Dictionary<string, JsonElement> keys, CancellationToken cancellationToken)
    {
        if (keys.Count != 1 || !keys.TryGetValue("_id", out var key)) throw new ArgumentException("MongoDB delete requires _id.");
        var result = await Collection(table).DeleteOneAsync(new BsonDocument("_id", Id(key)), cancellationToken);
        return (int)result.DeletedCount;
    }
}