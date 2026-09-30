using DbMcp.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using MySqlConnector;
using Npgsql;
using System.Data.Common;

namespace DbMcp.Data;

public sealed class DatabaseRegistry(IConfiguration configuration)
{
    public DatabaseOptions Resolve(string? name)
    {
        var children = configuration.GetSection(DatabaseOptions.SectionName).GetChildren().ToList();
        if (string.IsNullOrWhiteSpace(name))
        {
            if (children.Count != 1)
                throw new ArgumentException("Database name is required when zero or multiple databases are configured.");
            name = children[0].Key;
        }
        else if (!children.Any(child => string.Equals(child.Key, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Unknown database name.");

        var options = configuration.GetSection($"{DatabaseOptions.SectionName}:{name}").Get<DatabaseOptions>()!;
        if (options.ItemsPerPage <= 0 || options.MaxItems <= 0)
            throw new ArgumentException("ItemsPerPage and MaxItems must be positive.");
        if (options.Provider is not ("SqlServer" or "MySql" or "Postgres" or "MongoDb"))
            throw new ArgumentException("Unsupported database provider.");
        if (string.IsNullOrWhiteSpace(options.ConnectionString) &&
            (string.IsNullOrWhiteSpace(options.Address) || string.IsNullOrWhiteSpace(options.DefaultDatabase)))
            throw new ArgumentException("Database address and default database are required.");
        return options;
    }

    public static DbConnection OpenSqlConnection(DatabaseOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.Provider switch
            {
                "SqlServer" => new SqlConnection(options.ConnectionString),
                "MySql" => new MySqlConnection(options.ConnectionString),
                "Postgres" => new NpgsqlConnection(options.ConnectionString),
                _ => throw new ArgumentException("Not a SQL provider.")
            };

        return options.Provider switch
        {
            "SqlServer" when string.IsNullOrWhiteSpace(options.Username) =>
                throw new ArgumentException("SQL Server requires a username unless a connection string is configured."),
            "SqlServer" => new SqlConnection(new SqlConnectionStringBuilder
            {
                DataSource = options.Address + (options.Port is > 0 ? $",{options.Port}" : ""),
                InitialCatalog = options.DefaultDatabase,
                UserID = options.Username,
                Password = options.Password ?? "",
                Encrypt = true,
                TrustServerCertificate = true
            }.ConnectionString),
            "MySql" => new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = options.Address,
                Port = (uint)(options.Port ?? 3306),
                Database = options.DefaultDatabase,
                UserID = options.Username,
                Password = options.Password
            }.ConnectionString),
            "Postgres" => new NpgsqlConnection(new NpgsqlConnectionStringBuilder
            {
                Host = options.Address,
                Port = options.Port ?? 5432,
                Database = options.DefaultDatabase,
                Username = options.Username,
                Password = options.Password
            }.ConnectionString),
            _ => throw new ArgumentException("Not a SQL provider.")
        };
    }

    public static IMongoDatabase OpenMongoDatabase(DatabaseOptions options)
    {
        var url = new MongoUrlBuilder(options.ConnectionString ?? $"mongodb://{options.Address}:{options.Port ?? 27017}");
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            url.Username = options.Username;
            url.Password = options.Password;
        }
        var databaseName = options.DefaultDatabase ?? url.DatabaseName;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("MongoDB requires a default database.");
        return new MongoClient(url.ToMongoUrl()).GetDatabase(databaseName);
    }
}