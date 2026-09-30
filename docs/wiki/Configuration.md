# Configuration

DbMcp reads its database list from the `DbMcp:Databases` configuration section. Each child is an **alias** that MCP clients use to pick a database. With Docker you set it with environment variables, or by mounting a JSON file.

> [!TIP]
> Give DbMcp its own database account with the fewest permissions possible, ideally read-only. See [Safety](Home#safety).

## Environment variables

Every setting is an environment variable named:

```text
DbMcp__Databases__<alias>__<Setting>
```

The double underscore `__` separates levels. For example, `DbMcp__Databases__local__Provider=Postgres` sets `Provider` for the alias `local`.

| Setting | Required | Default | Notes |
| --- | --- | --- | --- |
| `Provider` | Yes | | Exactly one of `SqlServer`, `MySql`, `Postgres`, `MongoDb` (case-sensitive). |
| `Address` | Yes, unless `ConnectionString` is set | | Host name or IP **as seen from inside the container**. See [[Docker on Windows|Docker-on-Windows]]. |
| `Port` | No | MySQL `3306`, Postgres `5432`, MongoDB `27017`, SQL Server driver default (`1433`) | |
| `DefaultDatabase` | Yes, unless `ConnectionString` is set | | For MongoDB it can come from the connection string instead. |
| `Username` | Yes for SQL Server | | SQL Server uses SQL authentication; Windows authentication is not available from a Linux container. |
| `Password` | Usually | | Never put it in source control or in `args`. |
| `ConnectionString` | No | | Replaces `Address`, `Port`, `Username` and `Password`. Use the provider's native format. |
| `ItemsPerPage` | No | `50` | Rows per `get_page` call. Must be positive. |
| `MaxItems` | No | `500` | Hard cap on rows returned by `get_page` (across all pages) and `execute_sql`. Must be positive. |

When SQL Server settings are built from `Address`/`Username`/`Password`, the connection uses `Encrypt=true` and `TrustServerCertificate=true`. Provide a `ConnectionString` if you need stricter certificate validation.

## Validation

Settings are checked each time a tool runs. A tool call fails when:

| Condition | Error |
| --- | --- |
| `database` is omitted and zero or several aliases exist | `Database name is required when zero or multiple databases are configured.` |
| `database` doesn't match an alias (case-insensitive) | `Unknown database name.` |
| `ItemsPerPage` or `MaxItems` is zero or negative | `ItemsPerPage and MaxItems must be positive.` |
| `Provider` is not one of the four values | `Unsupported database provider.` |
| No `ConnectionString`, and `Address` or `DefaultDatabase` is missing | `Database address and default database are required.` |
| MongoDB has no database in `DefaultDatabase` or the connection string | `MongoDB requires a default database.` |
| SQL Server has no `ConnectionString` and no `Username` | `SQL Server requires a username unless a connection string is configured.` |

## Examples per provider

Each block is the content of an env file (see [Env files](#env-files)) for one alias.

### PostgreSQL

```ini
DbMcp__Databases__local__Provider=Postgres
DbMcp__Databases__local__Address=host.docker.internal
DbMcp__Databases__local__Port=5432
DbMcp__Databases__local__DefaultDatabase=sample
DbMcp__Databases__local__Username=dbmcp_reader
DbMcp__Databases__local__Password=change-me
```

### SQL Server

```ini
DbMcp__Databases__sales__Provider=SqlServer
DbMcp__Databases__sales__Address=host.docker.internal
DbMcp__Databases__sales__Port=1433
DbMcp__Databases__sales__DefaultDatabase=Sales
DbMcp__Databases__sales__Username=dbmcp_reader
DbMcp__Databases__sales__Password=change-me
```

### MySQL

```ini
DbMcp__Databases__shop__Provider=MySql
DbMcp__Databases__shop__Address=host.docker.internal
DbMcp__Databases__shop__DefaultDatabase=shop
DbMcp__Databases__shop__Username=dbmcp_reader
DbMcp__Databases__shop__Password=change-me
```

For MySQL, `DefaultDatabase` is also the default schema used for unqualified table names.

### MongoDB

```ini
DbMcp__Databases__docs__Provider=MongoDb
DbMcp__Databases__docs__ConnectionString=mongodb://dbmcp_reader:change-me@host.docker.internal:27017/?authSource=admin&directConnection=true
DbMcp__Databases__docs__DefaultDatabase=catalog
```

### Several databases

Add as many aliases as you like. Clients must then pass `database` on every tool call.

```ini
DbMcp__Databases__orders__Provider=Postgres
DbMcp__Databases__orders__Address=pg
DbMcp__Databases__orders__DefaultDatabase=orders
DbMcp__Databases__orders__Username=dbmcp_reader
DbMcp__Databases__orders__Password=change-me
DbMcp__Databases__audit__Provider=MongoDb
DbMcp__Databases__audit__Address=mongo
DbMcp__Databases__audit__DefaultDatabase=audit
```

## Env files

Keep settings in a file outside the repository and pass it with `--env-file`:

```json
"args": ["run", "--rm", "-i", "--env-file", "C:/Users/me/.dbmcp/dbmcp.env", "ghcr.io/zerowiggliness/dbmcp:latest"]
```

- One `NAME=value` per line, no quotes, no `export`.
- Use forward slashes (or escaped `\\`) for Windows paths inside JSON.
- Restrict the file's permissions: it contains passwords.

## Mounting a JSON config file

The image does not contain an `appsettings.json`, but the server reads one from `/app` if present. Mount it read-only:

```json
"args": [
  "run", "--rm", "-i",
  "-v", "C:/Users/me/.dbmcp/appsettings.json:/app/appsettings.json:ro",
  "-e", "DbMcp__Databases__local__Password",
  "ghcr.io/zerowiggliness/dbmcp:latest"
]
```

```json
{
  "DbMcp": {
    "Databases": {
      "local": {
        "Provider": "Postgres",
        "Address": "host.docker.internal",
        "Port": 5432,
        "DefaultDatabase": "sample",
        "Username": "dbmcp_reader",
        "ItemsPerPage": 50,
        "MaxItems": 500
      }
    }
  }
}
```

Environment variables override values from the file, so the password can stay out of it, as above.

## Creating a read-only account

Examples only; adapt them to your own security policies.

```sql
-- PostgreSQL
CREATE ROLE dbmcp_reader LOGIN PASSWORD 'change-me';
GRANT CONNECT ON DATABASE sample TO dbmcp_reader;
GRANT USAGE ON SCHEMA public TO dbmcp_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO dbmcp_reader;

-- SQL Server
CREATE LOGIN dbmcp_reader WITH PASSWORD = 'change-me';
USE Sales;
CREATE USER dbmcp_reader FOR LOGIN dbmcp_reader;
ALTER ROLE db_datareader ADD MEMBER dbmcp_reader;

-- MySQL
CREATE USER 'dbmcp_reader'@'%' IDENTIFIED BY 'change-me';
GRANT SELECT ON shop.* TO 'dbmcp_reader'@'%';
```

```javascript
// MongoDB
db.getSiblingDB("admin").createUser({
  user: "dbmcp_reader",
  pwd: "change-me",
  roles: [{ role: "read", db: "catalog" }]
});
```

With a read-only account, `insert`, `delete` and write statements in `execute_sql` fail with a permission error from the database.
