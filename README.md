# DbMcp

A stdio MCP server for SQL Server, MySQL, PostgreSQL, and MongoDB. Configure named databases on the server; clients choose a name, never a connection string. Database permissions control what the tools can read or change. Only connect trusted MCP clients, since they run with the configured database credentials.

## Run

Requires the .NET 10 SDK or Docker. Set configuration in `appsettings.json`, user secrets, or environment variables. Do not commit credentials.

```json
{
  "DbMcp": {
    "Databases": {
      "local": {
        "Provider": "Postgres",
        "Address": "localhost",
        "Port": 5432,
        "DefaultDatabase": "sample",
        "Username": "app",
        "Password": "set-outside-source-control",
        "ItemsPerPage": 50,
        "MaxItems": 500
      }
    }
  }
}
```

`Provider` is `SqlServer`, `MySql`, `Postgres`, or `MongoDb`. `ConnectionString` may replace address, port, and credentials. MongoDB still needs `DefaultDatabase` unless it is included in the connection string. Override individual fields using environment variables such as `DbMcp__Databases__local__Password`. Keep the connection string or password in a secret store. `ItemsPerPage` and `MaxItems` must be positive.

The server talks MCP over stdin/stdout and writes logs to stderr. In VS Code, add:

```json
{"servers":{"DbMcp":{"type":"stdio","command":"dotnet","args":["run","--project","C:/Projects/DbMcp/DbMcp.csproj"]}}}
```

For Docker, build with `docker build -t dbmcp .` and use `"command":"docker"` with `"args":["run","--rm","-i","-e","DbMcp__Databases__local__Provider=Postgres","-e","DbMcp__Databases__local__ConnectionString","dbmcp"]`; pass credentials through your secret mechanism rather than embedding them in the image or command history.

## Tools

All tools take `database`, a configured alias; it may be omitted only when exactly one database is configured. `list_tables` lists tables/collections; `get_schema` returns columns and primary keys (MongoDB samples up to 100 documents and marks the result inferred). `get_row` accepts `table` and `keys` containing the complete primary key, or `lookupColumn` and `lookupValue`. MongoDB uses `_id` as its key and accepts an ObjectId as a string. `get_page` accepts a 1-based `pageNumber`, orders by primary key or `_id`, and returns one page plus `nextPage` when present. `insert` accepts a JSON `item`; `delete` requires complete primary `keys` and deletes one MongoDB document.

`execute_sql` accepts `sql` and optional named `parameters` for relational providers. SQL text is executed with database credentials, including writes; returned result sets are capped at `MaxItems`. Only scalar JSON parameter values are supported for relational SQL. MongoDB accepts one `SELECT fields FROM collection [WHERE field =|>|>=|<|<= literal [AND ...]] [ORDER BY field [ASC|DESC]] [LIMIT positive_integer]` query, with no parameters. Unsupported SQL clauses and expressions are rejected; results are capped at `MaxItems`. MongoDB is not a general SQL engine.

## Verification

Run `dotnet test DbMcp.Tests/DbMcp.Tests.csproj --collect:"XPlat Code Coverage"` with Docker running. The suite starts disposable SQL Server, MySQL, PostgreSQL, and MongoDB instances using Testcontainers. CI checks formatting, requires 95% line coverage, then publishes `ghcr.io/<owner>/<repo>:latest` after a passing push to the default branch. Local Release coverage measured **96.03%** across 33 passing tests; publishing still depends on the workflow passing in GitHub Actions.
