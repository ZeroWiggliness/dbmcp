using ModelContextProtocol.Client;
using Xunit;

namespace DbMcp.Tests;

public class McpEndpointTests
{
    private static Task<McpClient> StartAsync() =>
        McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DbMcp",
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "DbMcp.dll")]
        }));

    [Fact]
    public async Task ToolsList_ExposesDatabaseOperations()
    {
        await using var client = await StartAsync();
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains("list_tables", names);
        Assert.Contains("execute_sql", names);
        Assert.Contains("get_row", names);
        Assert.DoesNotContain("get_random_number", names);
    }

    [Fact]
    public async Task ToolsCall_UnknownAlias_ReturnsToolError()
    {
        await using var client = await StartAsync();
        var result = await client.CallToolAsync("list_tables", new Dictionary<string, object?> { ["database"] = "unknown" });
        Assert.True(result.IsError);
    }
}
