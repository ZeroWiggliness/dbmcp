namespace DbMcp.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "DbMcp:Databases";

    public string Provider { get; set; } = "";
    public string? ConnectionString { get; set; }
    public string? Address { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? DefaultDatabase { get; set; }
    public int ItemsPerPage { get; set; } = 50;
    public int MaxItems { get; set; } = 500;
}