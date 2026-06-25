namespace simple_bloomberg_terminal.Services.Clients.Edgar;

/// <summary>
/// Carries the HTTP status the controller should return for an EDGAR failure
/// (503 unreachable, 422 not an SEC filer). One type instead of a hierarchy â€” the
/// status code is the only thing that varies.
/// </summary>
public class EdgarException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
