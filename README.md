# DbMcp

A stdio MCP server for SQL Server, MySQL, PostgreSQL, and MongoDB. Configure named databases on the server; clients choose a name, never a connection string. Database permissions control what the tools can read or change. Only connect trusted MCP clients, since they run with the configured database credentials.

> [!WARNING]
> **This tool can be dangerous. Use it with caution.**
> DbMcp gives an AI agent direct access to your databases. It can read, insert, change and delete data, and `execute_sql` runs any SQL it is given, including `DELETE`, `DROP` and `TRUNCATE`, using the credentials you configure. AI agents can misinterpret requests, and data returned from a database can contain instructions that manipulate the agent (prompt injection).
>
> - Use a dedicated, least-privilege database account; prefer read-only access.
> - Never point it at production data without careful review, and keep tested backups.
> - Only connect MCP clients you trust, and review tool calls before approving them.
>
> This software is provided "AS IS", without warranty of any kind. The authors and copyright holders accept no liability for any damage, data loss or other consequences of its use. You use it entirely at your own risk. See [LICENSE](LICENSE).

Full documentation is in the [wiki](https://github.com/ZeroWiggliness/dbmcp/wiki).

## Run

DbMcp runs as a Docker container: `ghcr.io/zerowiggliness/dbmcp:latest`. Pin a version tag from [Releases](https://github.com/ZeroWiggliness/dbmcp/releases) for repeatable setups. Configure databases with environment variables named `DbMcp__Databases__<alias>__<Setting>`. Do not commit credentials.

| Setting | Required | Notes |
| --- | --- | --- |
| `Provider` | Yes | `SqlServer`, `MySql`, `Postgres`, or `MongoDb` |
| `Address` | Yes, unless `ConnectionString` is set | Host name as seen from inside the container |
| `Port` | No | Defaults: MySQL 3306, Postgres 5432, MongoDB 27017 |
| `DefaultDatabase` | Yes, unless `ConnectionString` is set | MongoDB may take it from the connection string instead |
| `Username` / `Password` | Usually | Keep the password in a secret store |
| `ConnectionString` | No | Replaces address, port, and credentials |
| `ItemsPerPage` / `MaxItems` | No | Defaults 50 / 500; must be positive |

The server talks MCP over stdin/stdout and writes logs to stderr. In VS Code, add to `.vscode/mcp.json`:

```json
{
  "inputs": [
    { "type": "promptString", "id": "db-password", "description": "Database password", "password": true }
  ],
  "servers": {
    "DbMcp": {
      "type": "stdio",
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-e", "DbMcp__Databases__local__Provider=Postgres",
        "-e", "DbMcp__Databases__local__Address=host.docker.internal",
        "-e", "DbMcp__Databases__local__Port=5432",
        "-e", "DbMcp__Databases__local__DefaultDatabase=sample",
        "-e", "DbMcp__Databases__local__Username=app",
        "-e", "DbMcp__Databases__local__Password",
        "ghcr.io/zerowiggliness/dbmcp:latest"
      ],
      "env": { "DbMcp__Databases__local__Password": "${input:db-password}" }
    }
  }
}
```

`-e NAME` without a value passes the variable through from the client, so the password never appears in the arguments. Inside the container `localhost` is the container itself: use `host.docker.internal` for databases on your machine, or a shared Docker network for databases in other containers. See [Docker on Windows](https://github.com/ZeroWiggliness/dbmcp/wiki/Docker-on-Windows) and [MCP client setup](https://github.com/ZeroWiggliness/dbmcp/wiki/MCP-Client-Setup) for Claude Desktop, `--env-file`, and networking examples.

## Tools

All tools take `database`, a configured alias; it may be omitted only when exactly one database is configured. `list_tables` lists tables/collections; `get_schema` returns columns and primary keys (MongoDB samples up to 100 documents and marks the result inferred). `get_row` accepts `table` and `keys` containing the complete primary key, or `lookupColumn` and `lookupValue`. MongoDB uses `_id` as its key and accepts an ObjectId as a string. `get_page` accepts a 1-based `pageNumber`, orders by primary key or `_id`, and returns one page plus `nextPage` when present. `insert` accepts a JSON `item`; `delete` requires complete primary `keys` and deletes one MongoDB document.

`execute_sql` accepts `sql` and optional named `parameters` for relational providers. SQL text is executed with database credentials, including writes; returned result sets are capped at `MaxItems`. Only scalar JSON parameter values are supported for relational SQL. MongoDB accepts one `SELECT fields FROM collection [WHERE field =|>|>=|<|<= literal [AND ...]] [ORDER BY field [ASC|DESC]] [LIMIT positive_integer]` query, with no parameters. Unsupported SQL clauses and expressions are rejected; results are capped at `MaxItems`. MongoDB is not a general SQL engine.

## Development

For contributors only. Run `dotnet test DbMcp.Tests/DbMcp.Tests.csproj -c Release --settings .runsettings --collect:"XPlat Code Coverage"` with Docker running. The suite starts disposable SQL Server, MySQL, PostgreSQL, and MongoDB instances using Testcontainers. CI fails below 95% line coverage (`Program.cs` excluded), then publishes `ghcr.io/zerowiggliness/dbmcp:latest` and a release-version tag after a passing push to the default branch.

## License

[MIT](LICENSE). Provided "AS IS", without warranty or liability.
