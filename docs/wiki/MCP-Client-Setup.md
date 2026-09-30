# MCP Client Setup

DbMcp runs only as a Docker container: `ghcr.io/zerowiggliness/dbmcp:latest`. Every client starts it with `docker run --rm -i ...` and talks to it over stdin/stdout.

> [!WARNING]
> Any client you connect can read and change data with the configured credentials. Read [Safety](Home#safety) first and use a least-privilege account.

## Before you start

- Install Docker Desktop (or another Docker engine) and make sure `docker` is on your `PATH`.
- Pull the image once, so the first start doesn't time out while it downloads:

  ```powershell
  docker pull ghcr.io/zerowiggliness/dbmcp:latest
  ```

- Pin a version tag from [Releases](https://github.com/ZeroWiggliness/dbmcp/releases) instead of `latest` if you want updates only when you choose.

### Required Docker arguments

| Argument | Why |
| --- | --- |
| `-i` | Keeps stdin open. Without it the MCP protocol can't reach the server. |
| `--rm` | Removes the container when the client stops it. |
| **no** `-t` | A TTY corrupts the stdio protocol. |
| `-e NAME=value` | Sets a non-secret setting. |
| `-e NAME` | Passes `NAME` through from the client's environment (use this for secrets). |
| `--env-file path` | Loads settings from a file. |
| `--network name` | Joins a Docker network so the server can reach database containers by name. See [Docker on Windows](Docker-on-Windows). |

Setting names are described in [[Configuration]].

## VS Code

Add a `.vscode/mcp.json` to your workspace (or use **MCP: Open User Configuration** for all workspaces).

### Prompted password

VS Code asks for the password the first time the server starts and stores it securely. The `env` block hands it to `docker`, and `-e NAME` passes it into the container, so it never appears in the arguments.

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
        "-e", "DbMcp__Databases__local__Username=dbmcp_reader",
        "-e", "DbMcp__Databases__local__Password",
        "ghcr.io/zerowiggliness/dbmcp:latest"
      ],
      "env": { "DbMcp__Databases__local__Password": "${input:db-password}" }
    }
  }
}
```

### Env file

Keep every setting in a file outside the repository (format in [[Configuration]]):

```json
{
  "servers": {
    "DbMcp": {
      "type": "stdio",
      "command": "docker",
      "args": ["run", "--rm", "-i", "--env-file", "C:/Users/me/.dbmcp/dbmcp.env", "ghcr.io/zerowiggliness/dbmcp:latest"]
    }
  }
}
```

### Several databases

```json
{
  "inputs": [
    { "type": "promptString", "id": "orders-password", "description": "Orders DB password", "password": true },
    { "type": "promptString", "id": "docs-connection", "description": "MongoDB connection string", "password": true }
  ],
  "servers": {
    "DbMcp": {
      "type": "stdio",
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-e", "DbMcp__Databases__orders__Provider=SqlServer",
        "-e", "DbMcp__Databases__orders__Address=host.docker.internal",
        "-e", "DbMcp__Databases__orders__DefaultDatabase=Orders",
        "-e", "DbMcp__Databases__orders__Username=dbmcp_reader",
        "-e", "DbMcp__Databases__orders__Password",
        "-e", "DbMcp__Databases__docs__Provider=MongoDb",
        "-e", "DbMcp__Databases__docs__DefaultDatabase=catalog",
        "-e", "DbMcp__Databases__docs__ConnectionString",
        "ghcr.io/zerowiggliness/dbmcp:latest"
      ],
      "env": {
        "DbMcp__Databases__orders__Password": "${input:orders-password}",
        "DbMcp__Databases__docs__ConnectionString": "${input:docs-connection}"
      }
    }
  }
}
```

With more than one alias, the agent must pass `database` (`orders` or `docs`) on every tool call.

## Claude Desktop

Edit `claude_desktop_config.json` (**Settings → Developer → Edit Config**):

- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

Restart Claude Desktop after saving.

### Env file (recommended)

Claude Desktop has no password prompt, so keep secrets in a protected env file:

```json
{
  "mcpServers": {
    "dbmcp": {
      "command": "docker",
      "args": ["run", "--rm", "-i", "--env-file", "C:/Users/me/.dbmcp/dbmcp.env", "ghcr.io/zerowiggliness/dbmcp:latest"]
    }
  }
}
```

### Inline environment

The `env` block is stored in plain text in the config file. Only use it for non-sensitive settings or on a machine you control.

```json
{
  "mcpServers": {
    "dbmcp": {
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-e", "DbMcp__Databases__local__Provider",
        "-e", "DbMcp__Databases__local__Address",
        "-e", "DbMcp__Databases__local__DefaultDatabase",
        "-e", "DbMcp__Databases__local__Username",
        "-e", "DbMcp__Databases__local__Password",
        "ghcr.io/zerowiggliness/dbmcp:latest"
      ],
      "env": {
        "DbMcp__Databases__local__Provider": "MySql",
        "DbMcp__Databases__local__Address": "host.docker.internal",
        "DbMcp__Databases__local__DefaultDatabase": "shop",
        "DbMcp__Databases__local__Username": "dbmcp_reader",
        "DbMcp__Databases__local__Password": "change-me"
      }
    }
  }
}
```

## Mounted config file

Any client can mount a JSON config file instead of passing settings one by one (details in [[Configuration]]):

```json
"args": [
  "run", "--rm", "-i",
  "-v", "C:/Users/me/.dbmcp/appsettings.json:/app/appsettings.json:ro",
  "-e", "DbMcp__Databases__local__Password",
  "ghcr.io/zerowiggliness/dbmcp:latest"
]
```

## Check it works

1. Start the server from the client (VS Code: **MCP: List Servers → DbMcp → Start Server**).
2. Confirm the seven tools are listed: `list_tables`, `get_schema`, `get_row`, `get_page`, `execute_sql`, `insert`, `delete`.
3. Ask the agent to list tables. A result means the configuration and network path are right.

## Troubleshooting

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Server starts then immediately stops | `-i` missing, or `-t` present | Use `run --rm -i` and never `-t`. |
| Client times out on first start | Image still downloading | Run `docker pull` first. |
| `docker` not found | Docker isn't on the client's `PATH` | Start Docker Desktop, or use the full path to `docker.exe` as `command`. |
| `Unknown database name.` | `database` doesn't match any alias | Check the alias in the `DbMcp__Databases__<alias>__...` names. |
| `Database name is required ...` | Several aliases, `database` omitted | Pass `database` on each call. |
| `Database address and default database are required.` | A variable wasn't passed into the container | `-e NAME` only works if `NAME` is set in the client's `env`. |
| Connection refused or timeout | `localhost` points at the container itself | See [Docker on Windows](Docker-on-Windows). |
| Login failed / permission denied | Wrong credentials or insufficient grants | Test the account with your normal database tool first. |

Server logs go to stderr. In VS Code, open them with **MCP: List Servers → DbMcp → Show Output**.
