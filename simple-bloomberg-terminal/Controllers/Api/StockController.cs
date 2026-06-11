using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/stock")]
[Authorize]
public class StockController : ControllerBase
{
    private readonly ICompanyRepository _companies;
    private readonly IStockService _stock;
    private readonly IStockApiClient _client;

    public StockController(ICompanyRepository companies, IStockService stock, IStockApiClient client)
    {
        _companies = companies;
        _stock = stock;
        _client = client;
    }

    // Fetch EDGAR data for a seeded company and (re)persist its EDGAR-tagged source/event rows.
    [HttpPost("refresh/{companyId:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CompanyDto>> Refresh(long companyId)
    {
        var company = _companies.GetById(companyId);
        if (company is null) return NotFound();
        if (string.IsNullOrWhiteSpace(company.Cik))
            return Conflict("Company has no CIK — not an SEC filer.");

        try
        {
            return Ok(await _stock.RefreshAsync(company));
        }
        catch (EdgarException ex)
        {
            return StatusCode(ex.StatusCode, ex.Message);
        }
    }

    // Read-only ticker -> CIK helper.
    [HttpGet("resolve/{ticker}")]
    public async Task<IActionResult> Resolve(string ticker)
    {
        var cik = await _client.ResolveCik(ticker);
        return cik is null ? NotFound() : Ok(new { ticker, cik });
    }

    // ---- read-only EDGAR browser (right pane of /extraction) — no persistence ----

    // Raw XBRL company facts JSON, straight from SEC.
    [HttpGet("facts/{companyId:long}")]
    public async Task<IActionResult> Facts(long companyId)
    {
        if (TryGetFilerCik(companyId, out var cik10, out var fail)) return fail!;
        try
        {
            var json = await _client.GetCompanyFactsJson(cik10!);
            if (json is null) return UnprocessableEntity("CIK is not an SEC filer.");
            return Content(json, "application/json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
        }
    }

    // The company's filing list (form, dates, accession, primary document + a ready URL).
    [HttpGet("filings/{companyId:long}")]
    public async Task<IActionResult> Filings(long companyId)
    {
        var company = _companies.GetById(companyId);
        if (company is null) return NotFound();
        if (string.IsNullOrWhiteSpace(company.Cik)) return Conflict("Company has no CIK — not an SEC filer.");

        EdgarSubmissions? subs;
        try { subs = await _client.GetSubmissions(company.Cik.PadLeft(10, '0')); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
        }
        if (subs?.Filings?.Recent is not { Form: { } forms }) return UnprocessableEntity("No filings.");

        var cikNoPad = company.Cik.TrimStart('0');
        var r = subs.Filings.Recent;
        var list = Enumerable.Range(0, forms.Count).Select(i =>
        {
            var accession = r.AccessionNumber?.ElementAtOrDefault(i);
            var doc = r.PrimaryDocument?.ElementAtOrDefault(i);
            return new
            {
                form = forms[i],
                filingDate = r.FilingDate?.ElementAtOrDefault(i),
                reportDate = r.ReportDate?.ElementAtOrDefault(i),
                accessionNumber = accession,
                primaryDocument = doc,
                description = r.PrimaryDocDescription?.ElementAtOrDefault(i),
                documentUrl = accession is not null && doc is not null
                    ? $"https://www.sec.gov/Archives/edgar/data/{cikNoPad}/{accession.Replace("-", "")}/{doc}"
                    : null
            };
        }).ToList();
        return Ok(list);
    }

    // Proxy one filing's primary document text into the pane so proof can be selected.
    [HttpGet("filing/{companyId:long}")]
    public async Task<IActionResult> Filing(long companyId, [FromQuery] string accession, [FromQuery] string doc)
    {
        var company = _companies.GetById(companyId);
        if (company is null) return NotFound();
        if (string.IsNullOrWhiteSpace(company.Cik)) return Conflict("Company has no CIK — not an SEC filer.");
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");

        try
        {
            var body = await _client.GetFilingDocument(company.Cik.TrimStart('0'), accession.Replace("-", ""), doc);
            if (body is null) return NotFound("Filing document not found.");
            return Content(body, "text/plain");   // raw text; client renders it for selection
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
        }
    }

    // Shared lookup: company exists + is an SEC filer -> its padded CIK; else the failure result.
    private bool TryGetFilerCik(long companyId, out string? cik10, out IActionResult? fail)
    {
        cik10 = null; fail = null;
        var company = _companies.GetById(companyId);
        if (company is null) { fail = NotFound(); return true; }
        if (string.IsNullOrWhiteSpace(company.Cik)) { fail = Conflict("Company has no CIK — not an SEC filer."); return true; }
        cik10 = company.Cik.PadLeft(10, '0');
        return false;
    }
}
