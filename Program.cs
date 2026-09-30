using DbMcp.Configuration;
using DbMcp.Data;
using DbMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

// stdout carries the MCP protocol, so all logs must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.Configure<Dictionary<string, DatabaseOptions>>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddSingleton<DatabaseRegistry>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DatabaseTools>();

await builder.Build().RunAsync();

