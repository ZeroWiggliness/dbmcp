# Tools

DbMcp exposes seven MCP tools. Tool names are `snake_case`; argument names are `camelCase`. Results come back as JSON text.

> [!WARNING]
> Tools marked **Destructive** change data. `execute_sql` runs any SQL it is given against relational databases, including `DELETE`, `DROP` and `TRUNCATE`. Use a least-privilege account and review every call before approving it. See [Safety](Home#safety).

| Tool | Purpose | Destructive |
| --- | --- | --- |
| [`list_tables`](#list_tables) | List tables or collections | No |
| [`get_schema`](#get_schema) | Columns and primary keys of a table, or an inferred MongoDB schema | No |
| [`get_row`](#get_row) | Fetch one row by primary key or by a column value | No |
| [`get_page`](#get_page) | Read one page of rows in key order | No |
| [`execute_sql`](#execute_sql) | Run SQL (relational) or a limited `SELECT` (MongoDB) | **Yes** |
| [`insert`](#insert) | Insert one row or document | **Yes** |
| [`delete`](#delete) | Delete one row or document by key | **Yes** |

## Common rules

**`database`** (string, optional on every tool). The configured alias to use. It can be left out only when exactly one database is configured. See [[Configuration]].

**Table names (SQL Server, MySQL, PostgreSQL)**
- Written as `table` or `schema.table`.
- Only letters, digits and `_` are allowed in each part; anything else fails with `Invalid table name.`
- Without a schema, the default is `dbo` (SQL Server), `public` (PostgreSQL) or the configured database (MySQL).

**Collection names (MongoDB)**
- Must not be blank, start with `system.`, or contain `$` or a null character (`Invalid collection name.`).

**Values** are plain JSON values. For relational databases only scalars (string, number, boolean, `null`) are accepted; objects and arrays fail with `Only scalar JSON values are accepted for SQL fields.` MongoDB accepts nested objects and arrays.

**MongoDB `_id`**. A 24-character hex string is converted to an `ObjectId`. Other values are used as they are. Documents are returned as MongoDB relaxed extended JSON, so an `ObjectId` looks like `{ "$oid": "..." }`.

**Limits**. `get_page` and `execute_sql` never return more than `MaxItems` rows.

---

## list_tables

Lists base tables (as `schema.table`) or MongoDB collection names.

| Argument | Type | Required |
| --- | --- | --- |
| `database` | string | See above |

**Example**

```json
{ "database": "local" }
```

```json
["public.customers", "public.orders"]
```

For MySQL the list contains every schema the account can see.

---

## get_schema

Returns the columns of a table and marks the primary key columns. For MongoDB the schema is **inferred** from up to 100 documents and `inferred` is `true`; `_id` is always reported as the key.

| Argument | Type | Required |
| --- | --- | --- |
| `table` | string | Yes |
| `database` | string | See above |

**Example**

```json
{ "table": "orders", "database": "local" }
```

```json
{
  "name": "public.orders",
  "columns": [
    { "name": "id", "dataType": "integer", "nullable": false, "primaryKey": true },
    { "name": "customer_id", "dataType": "integer", "nullable": false, "primaryKey": false },
    { "name": "total", "dataType": "numeric", "nullable": true, "primaryKey": false }
  ],
  "inferred": false
}
```

**Fails when** the table or collection doesn't exist (`Table not found.` / `Collection not found.`) or the name is invalid.

---

## get_row

Finds one row. Use either:

- `keys`: an object holding **every** primary key column and nothing else; or
- `lookupColumn` + `lookupValue`: the first row where that column equals the value.

Returns the row as an object, or `null` if nothing matches.

| Argument | Type | Required |
| --- | --- | --- |
| `table` | string | Yes |
| `keys` | object | When not using a lookup |
| `lookupColumn` | string | No |
| `lookupValue` | any JSON value | With `lookupColumn` |
| `database` | string | See above |

**Examples**

By primary key:

```json
{ "table": "orders", "keys": { "id": 42 } }
```

```json
{ "id": 42, "customer_id": 7, "total": 19.99 }
```

By another column:

```json
{ "table": "customers", "lookupColumn": "email", "lookupValue": "ada@example.com" }
```

MongoDB by `_id`:

```json
{ "table": "products", "keys": { "_id": "507f1f77bcf86cd799439011" }, "database": "docs" }
```

```json
{ "_id": { "$oid": "507f1f77bcf86cd799439011" }, "name": "Widget", "price": 2.5 }
```

**Fails when**
- `keys` doesn't contain exactly the primary key columns (`All primary key columns must be supplied.`).
- MongoDB `keys` is anything other than `{ "_id": ... }`.
- `lookupColumn` isn't a column of the table, or `lookupValue` is missing.
- A MongoDB `lookupColumn` starts with `$`.

---

## get_page

Reads one page of rows ordered by primary key (relational) or `_id` (MongoDB).

| Argument | Type | Required |
| --- | --- | --- |
| `table` | string | Yes |
| `pageNumber` | integer, starting at 1 | Yes |
| `database` | string | See above |

Each page holds up to `ItemsPerPage` rows, and paging stops at `MaxItems` rows in total: pages past that limit are empty. `nextPage` is the next page number, or `null` when there is nothing more to read.

**Example** (`ItemsPerPage` = 2)

```json
{ "table": "orders", "pageNumber": 1 }
```

```json
{
  "items": [
    { "id": 1, "customer_id": 7, "total": 10.00 },
    { "id": 2, "customer_id": 9, "total": 25.50 }
  ],
  "pageNumber": 1,
  "itemsPerPage": 2,
  "maxItems": 500,
  "nextPage": 2
}
```

**Fails when** `pageNumber` is less than 1 (`Page numbers start at 1.`) or a relational table has no primary key (`Pagination requires a primary key.`).

---

## execute_sql

> [!CAUTION]
> **Destructive.** For relational databases this runs the SQL text exactly as given, with the configured account's permissions.

| Argument | Type | Required |
| --- | --- | --- |
| `sql` | string | Yes |
| `parameters` | object of scalar values | No (relational only) |
| `database` | string | See above |

### SQL Server, MySQL, PostgreSQL

- Statements that start with `SELECT` or `WITH` return `rows` (at most `MaxItems`) and `affectedRows` of `0`.
- Anything else returns `rows: null` and the number of affected rows.
- Parameters are named; refer to them as `@name` in the SQL.

```json
{
  "sql": "SELECT id, total FROM orders WHERE customer_id = @customer AND total > @min",
  "parameters": { "customer": 7, "min": 10 }
}
```

```json
{ "rows": [ { "id": 42, "total": 19.99 } ], "affectedRows": 0 }
```

```json
{ "sql": "UPDATE orders SET total = @total WHERE id = @id", "parameters": { "total": 21.50, "id": 42 } }
```

```json
{ "rows": null, "affectedRows": 1 }
```

Always pass user-supplied values as `parameters` rather than putting them into the SQL text.

### MongoDB

MongoDB is not a SQL engine. It accepts exactly one read-only query of this shape, with no `parameters`:

```text
SELECT * | field[, field ...]
FROM collection
[WHERE field (= | > | >= | < | <=) literal [AND ...]]
[ORDER BY field [ASC | DESC][, ...]]
[LIMIT positive_integer]
```

- Literals: single-quoted strings, numbers, `TRUE`/`FALSE`. Parentheses around conditions are allowed.
- Not supported: `OR`, `<>`, `NULL`, functions, aliases, joins, `DISTINCT`, `GROUP BY`, `HAVING`, `OFFSET`, sub-queries, and several statements at once. These fail with an error before anything is sent to the database.
- Results are capped at `MaxItems`, even if `LIMIT` is higher.

```json
{
  "sql": "SELECT name, price FROM products WHERE price >= 2 AND active = TRUE ORDER BY price DESC LIMIT 5",
  "database": "docs"
}
```

```json
{ "rows": [ { "_id": { "$oid": "507f1f77bcf86cd799439011" }, "name": "Widget", "price": 2.5 } ], "affectedRows": 0 }
```

---

## insert

> [!CAUTION]
> **Destructive.** Adds data to the database.

Inserts one row or document and returns the number of rows inserted (`1`).

| Argument | Type | Required |
| --- | --- | --- |
| `table` | string | Yes |
| `item` | object | Yes, not empty |
| `database` | string | See above |

```json
{ "table": "customers", "item": { "id": 8, "name": "Ada", "email": "ada@example.com" } }
```

```json
1
```

MongoDB accepts nested values, and `_id` is optional:

```json
{ "table": "products", "item": { "name": "Widget", "price": 2.5, "tags": ["new"], "size": { "w": 3, "h": 4 } }, "database": "docs" }
```

**Fails when**
- `item` is empty.
- A key isn't a column of the table (`Unknown column name.`).
- A relational value is an object or array.
- A MongoDB field name starts with `$` (`Invalid document.`).

---

## delete

> [!CAUTION]
> **Destructive.** Permanently removes a row or document.

Deletes one row by its complete primary key, or one MongoDB document by `_id`. Returns the number deleted (`0` if nothing matched).

| Argument | Type | Required |
| --- | --- | --- |
| `table` | string | Yes |
| `keys` | object | Yes |
| `database` | string | See above |

```json
{ "table": "orders", "keys": { "id": 42 } }
```

```json
1
```

```json
{ "table": "products", "keys": { "_id": "507f1f77bcf86cd799439011" }, "database": "docs" }
```

**Fails when** `keys` doesn't contain exactly the primary key columns, the table has no primary key, or MongoDB `keys` is anything other than `{ "_id": ... }`.
