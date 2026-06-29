using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace McpServer;

// Typed HttpClient wrapper over the terminal's read-only API. The base address and the X-Api-Key header
// are configured once in Program.cs; this class just shapes the calls and deserialization. All reads
// hit [Authorize] GETs the MCP service principal is allowed; it can never reach the Admin/Manager writes.
public sealed class TerminalClient(HttpClient http)
{
    // Web defaults = camelCase + case-insensitive matching against the terminal's JSON. Numeric enum
    // values deserialize into our mirrored enums automatically.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<TCompany>> SearchCompanies(string query, CancellationToken ct)
        => await http.GetFromJsonAsync<List<TCompany>>(
               $"api/companies?q={Uri.EscapeDataString(query)}", Json, ct) ?? [];

    public async Task<TCompany?> GetCompany(long id, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"api/companies/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<TCompany>(Json, ct);
    }

    public async Task<List<TFinancial>> SearchFinancials(string companyName, CancellationToken ct)
        => await http.GetFromJsonAsync<List<TFinancial>>(
               $"api/companyfinancials?q={Uri.EscapeDataString(companyName)}", Json, ct) ?? [];

    public async Task<List<TRisk>> SearchRisks(string companyName, CancellationToken ct)
        => await http.GetFromJsonAsync<List<TRisk>>(
               $"api/companyrisks?q={Uri.EscapeDataString(companyName)}", Json, ct) ?? [];

    // null = company not found (404); otherwise the stored series (possibly empty).
    public async Task<List<TVolumePoint>?> GetVolume(long id, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"api/companies/{id}/volume", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<TVolumePoint>>(Json, ct) ?? [];
    }

    // The live SEC filings list. Returns the raw status so the tool can map 404/409/422/503 → the right
    // section state (not_found / unavailable / missing / error) instead of throwing.
    public async Task<FilingsFetch> GetFilings(long id, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"api/stock/filings/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var list = await resp.Content.ReadFromJsonAsync<List<TFiling>>(Json, ct) ?? [];
            return new FilingsFetch(resp.StatusCode, list);
        }
        return new FilingsFetch(resp.StatusCode, null);
    }
}

public record FilingsFetch(HttpStatusCode Status, List<TFiling>? Filings);
