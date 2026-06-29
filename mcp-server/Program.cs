using McpServer;

var builder = WebApplication.CreateBuilder(args);

// Typed HttpClient to the terminal's read-only API. On Railway, Terminal__BaseUrl points at the
// terminal service's private URL and Terminal__ApiKey matches the terminal's Mcp__ApiKey. The key
// authenticates as the role-less MCP service principal — reads only, no writes.
builder.Services.AddHttpClient<TerminalClient>(c =>
{
    var baseUrl = builder.Configuration["Terminal:BaseUrl"]
        ?? throw new InvalidOperationException("Terminal:BaseUrl is not configured.");
    c.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    var apiKey = builder.Configuration["Terminal:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        c.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

// MCP server over Streamable HTTP. Stateless: these tools are pure request/response reads, so we need
// no per-session server-to-client channel. Tools are discovered from this assembly ([McpServerToolType]).
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

// Maps the Streamable HTTP MCP endpoint. Port comes from the environment (Railway $PORT via the
// Dockerfile's ASPNETCORE_HTTP_PORTS), so it is not hardcoded here.
app.MapMcp();

app.Run();
