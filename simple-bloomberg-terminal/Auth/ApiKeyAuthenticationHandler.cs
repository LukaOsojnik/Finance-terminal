using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace simple_bloomberg_terminal.Auth;

// Service-to-service authentication for the read-only MCP server. The terminal's only existing scheme
// is the Identity cookie (browser sessions); a server can't carry that. This scheme reads a shared
// secret from config (Mcp:ApiKey, set on Railway as Mcp__ApiKey) and, when a request presents it in
// the X-Api-Key header, authenticates the caller as the role-less "mcp-service" principal. Role-less
// is deliberate: it satisfies plain [Authorize] GETs but fails every [Authorize(Roles="Admin,Manager")]
// write, so the MCP key is structurally read-only.
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly string? _configuredKey;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration config)
        : base(options, logger, encoder)
    {
        _configuredKey = config["Mcp:ApiKey"];
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No key configured, or no header presented: defer to the other scheme (the cookie) rather
        // than failing — NoResult lets a browser request still authenticate via Identity.
        if (string.IsNullOrEmpty(_configuredKey))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (!Request.Headers.TryGetValue(HeaderName, out var provided) || provided.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var a = Encoding.UTF8.GetBytes(provided.ToString());
        var b = Encoding.UTF8.GetBytes(_configuredKey);
        // Constant-time compare so a wrong key can't be guessed byte-by-byte via timing.
        if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "mcp-service"),
            new Claim(ClaimTypes.NameIdentifier, "mcp-service"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
