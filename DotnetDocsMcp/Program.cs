using DotnetDocsMcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// HttpClient tipado para el servicio de Microsoft Learn.
// User-Agent identifica al cliente públicamente (buena práctica con APIs públicas).
builder.Services.AddHttpClient<IMicrosoftLearnSearch, MicrosoftLearnSearchService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "DotnetDocsMcp/1.0 (+https://github.com/varocode/dotnet-docs-mcp)");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// MCP server con stdio transport y descubrimiento automático de tools.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Importante: en stdio transport, stdout es para el protocolo JSON-RPC.
// Cualquier log a stdout rompe el cliente. Forzamos todos los logs a stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

var app = builder.Build();
await app.RunAsync();
