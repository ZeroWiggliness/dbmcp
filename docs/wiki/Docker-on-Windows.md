# Docker on Windows

DbMcp runs inside a Linux container. How it reaches your database depends on where the database runs. This page covers Docker Desktop on Windows (WSL 2 backend).

> [!IMPORTANT]
> Inside the container, `localhost` and `127.0.0.1` mean **the DbMcp container itself**, not your PC and not other containers. A database address that works in SSMS, DBeaver or `psql` on Windows usually won't work unchanged here.

## Which address to use

| Where the database runs | `Address` | `Port` | Extra `docker run` args |
| --- | --- | --- | --- |
| Installed directly on Windows | `host.docker.internal` | The port it listens on | None |
| In a container that **publishes** a port (`-p 5433:5432`) | `host.docker.internal` | The **published** port (`5433`) | None |
| In a container on a shared Docker network | The container name (`pg`) | The **internal** port (`5432`) | `--network <name>` |
| In a Docker Compose project | The Compose service name (`db`) | The **internal** port | `--network <project>_default` |
| Installed directly inside a WSL distro | `host.docker.internal` | The port it listens on | None (see [WSL](#databases-installed-inside-wsl)) |
| On another machine or in the cloud | Its DNS name or IP | Its port | None |

## Database on the Windows host

`host.docker.internal` resolves to your Windows machine from inside any Docker Desktop container.

```json
"args": [
  "run", "--rm", "-i",
  "-e", "DbMcp__Databases__local__Provider=SqlServer",
  "-e", "DbMcp__Databases__local__Address=host.docker.internal",
  "-e", "DbMcp__Databases__local__Port=1433",
  "-e", "DbMcp__Databases__local__DefaultDatabase=Sales",
  "-e", "DbMcp__Databases__local__Username=dbmcp_reader",
  "-e", "DbMcp__Databases__local__Password",
  "ghcr.io/zerowiggliness/dbmcp:latest"
]
```

For **SQL Server installed on Windows** (including SQL Server Express and Developer edition):

1. **Enable TCP/IP.** In *SQL Server Configuration Manager → SQL Server Network Configuration → Protocols*, enable **TCP/IP**, then restart the service.
2. **Use a fixed port.** Named instances (`.\SQLEXPRESS`) default to a dynamic port. Set **TCP Port = 1433** under *IPAll* (clear *TCP Dynamic Ports*), or put the dynamic port in `Port`. The SQL Browser service is not reachable from the container.
3. **Enable SQL Server authentication.** Windows (integrated) authentication is not available from a Linux container. Turn on *SQL Server and Windows Authentication mode* and create a SQL login.
4. **Allow the port through Windows Firewall:**

   ```powershell
   New-NetFirewallRule -DisplayName "SQL Server 1433" -Direction Inbound -Protocol TCP -LocalPort 1433 -Action Allow
   ```

For **PostgreSQL or MySQL installed on Windows**, make sure they listen on more than `127.0.0.1` (`listen_addresses` in `postgresql.conf`, `bind-address` in `my.ini`). For PostgreSQL, also allow the Docker subnet in `pg_hba.conf`.

## Database in another container

This is the most reliable setup: put the database and DbMcp on the same user-defined Docker network and use the container name as the address.

1. Create a network once:

   ```powershell
   docker network create dbmcp
   ```

2. Start (or connect) the database container on it:

   ```powershell
   docker run -d --name pg --network dbmcp -e POSTGRES_PASSWORD=change-me -e POSTGRES_DB=sample postgres:16
   ```

   For a container that already exists: `docker network connect dbmcp pg`.

3. Add `--network dbmcp` to the DbMcp args and use the container name and **internal** port:

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
           "--network", "dbmcp",
           "-e", "DbMcp__Databases__local__Provider=Postgres",
           "-e", "DbMcp__Databases__local__Address=pg",
           "-e", "DbMcp__Databases__local__Port=5432",
           "-e", "DbMcp__Databases__local__DefaultDatabase=sample",
           "-e", "DbMcp__Databases__local__Username=postgres",
           "-e", "DbMcp__Databases__local__Password",
           "ghcr.io/zerowiggliness/dbmcp:latest"
         ],
         "env": { "DbMcp__Databases__local__Password": "${input:db-password}" }
       }
     }
   }
   ```

Container names only resolve on user-defined networks, not on the default `bridge` network. The database doesn't need to publish any ports for this to work.

### Several database containers

Connect each database to the same network and give each one an alias:

```powershell
docker network connect dbmcp my-sqlserver
docker network connect dbmcp my-mongo
```

```ini
DbMcp__Databases__sales__Provider=SqlServer
DbMcp__Databases__sales__Address=my-sqlserver
DbMcp__Databases__sales__DefaultDatabase=Sales
DbMcp__Databases__sales__Username=sa
DbMcp__Databases__sales__Password=change-me
DbMcp__Databases__docs__Provider=MongoDb
DbMcp__Databases__docs__ConnectionString=mongodb://my-mongo:27017/?directConnection=true
DbMcp__Databases__docs__DefaultDatabase=catalog
```

```json
"args": ["run", "--rm", "-i", "--network", "dbmcp", "--env-file", "C:/Users/me/.dbmcp/dbmcp.env", "ghcr.io/zerowiggliness/dbmcp:latest"]
```

If your databases are on different networks, connecting them all to one shared network, as shown above, is the simplest option.

## Docker Compose databases

Compose puts services on a network named `<project>_default` (the project is usually the folder name). Find it with:

```powershell
docker network ls
```

Then use that network and the **service name** as the address:

```json
"args": [
  "run", "--rm", "-i",
  "--network", "myapp_default",
  "-e", "DbMcp__Databases__local__Provider=MySql",
  "-e", "DbMcp__Databases__local__Address=db",
  "-e", "DbMcp__Databases__local__DefaultDatabase=shop",
  "-e", "DbMcp__Databases__local__Username=dbmcp_reader",
  "-e", "DbMcp__Databases__local__Password",
  "ghcr.io/zerowiggliness/dbmcp:latest"
]
```

If the Compose file sets a custom `networks:` entry, use that network's full name as shown by `docker network ls`.

## MongoDB replica sets

A replica set tells clients to connect to the member host names it was configured with, which often don't resolve from the DbMcp container. Add `directConnection=true` to connect to one member only:

```ini
DbMcp__Databases__docs__ConnectionString=mongodb://user:change-me@host.docker.internal:27017/?authSource=admin&directConnection=true
DbMcp__Databases__docs__DefaultDatabase=catalog
```

## Databases installed inside WSL

If the database runs directly in a WSL 2 distro (not in Docker):

- It must listen on all interfaces (`0.0.0.0`), not only `127.0.0.1`.
- With WSL **mirrored networking** (`networkingMode=mirrored` in `%UserProfile%\.wslconfig`), use `host.docker.internal`.
- Otherwise use the distro's IP address from `wsl hostname -I`. This address changes when WSL restarts.

## Windows-specific tips

- **Paths in JSON.** Use forward slashes (`C:/Users/me/dbmcp.env`) or escape backslashes (`C:\\Users\\me\\dbmcp.env`).
- **Passing secrets.** `-e NAME` (no `=value`) copies `NAME` from the environment of the process that runs `docker`. In VS Code and Claude Desktop that environment comes from the server's `env` block.
- **Env files.** One `NAME=value` per line, no quotes. Save them as UTF-8 without a BOM; a BOM can make the first variable's name invalid.
- **Docker Desktop must be running** before the MCP client starts the server.
- **Corporate VPNs** can block `host.docker.internal` or container DNS. If name lookups fail only on the VPN, try the database's IP address.

## Troubleshooting

Test connectivity from a throwaway container on the same network before blaming DbMcp:

```powershell
# Is the port reachable?
docker run --rm --network dbmcp busybox nc -zv pg 5432
docker run --rm busybox nc -zv host.docker.internal 1433
```

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Connection refused | Using `localhost`, wrong port, or the database listens only on loopback | Use the address from the table above; check the listen address. |
| Timeout | Windows Firewall, VPN, or SQL Server TCP/IP disabled | Open the port; enable TCP/IP; try without the VPN. |
| Name or service not known | Container name used without `--network`, or on the default `bridge` network | Create a user-defined network and pass `--network`. |
| Works with published port but not by name | Using the published port with the container name | Container name + internal port, or `host.docker.internal` + published port. |
| SQL Server login failed | Windows authentication, or SQL auth disabled | Enable mixed mode and use a SQL login. |
| MongoDB server selection timeout | Replica set member names don't resolve | Add `directConnection=true`. |
