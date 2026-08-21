using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilingsController : ControllerBase
{
    private readonly IFilingRepository _repo;
    private readonly IMapper _mapper;
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly IMemoryCache _cache;

    public FilingsController(
        IFilingRepository repo, IMapper mapper, ICompanyRepository companies,
        IStockApiClient client, IMemoryCache cache)
    {
        _repo = repo;
        _mapper = mapper;
        _companies = companies;
        _client = client;
        _cache = cache;
    }

    [HttpGet]
    public ActionResult<IEnumerable<FilingDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<FilingDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<FilingDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<FilingDto>(entity));
    }

    // The filing's own document, proxied from EDGAR for the in-app evidence viewer: a saved row knows
    // only its FilingId, and the browser cannot fetch sec.gov itself (no CORS header, and EDGAR
    // rejects requests without the declared User-Agent this HttpClient carries). Reads and writes the
    // same cache entry the scan pipeline uses, so a filing just scanned opens with no round trip.
    [HttpGet("{id:long}/document")]
    public async Task<IActionResult> Document(long id)
    {
        var filing = _repo.GetById(id);
        if (filing is null || filing.DeletedAt != null) return NotFound();

        var company = _companies.GetById(filing.CompanyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik))
            return Conflict("Company has no CIK — not an SEC filer.");

        // The primary document's file name is the last segment of the archive URL — the one piece of
        // the EDGAR path Filing does not store on its own. Rows saved before the batch-save carried
        // the document name have no URL at all; ask EDGAR which document the accession points at and
        // repair the row, rather than dead-ending on proof the user can never open.
        var doc = filing.PrimaryDocUrl?.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(doc))
        {
            try { doc = await BackfillPrimaryDocAsync(filing, company); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
            }
            if (string.IsNullOrWhiteSpace(doc))
                return Conflict("Filing has no primary document on record, and EDGAR does not list one " +
                                "for this accession number.");
        }

        var key = FilingExtractionService.RawKey(filing.AccessionNumber, doc);
        if (_cache.TryGetValue(key, out string? cached) && cached is not null)
            return Content(cached, "text/plain");

        try
        {
            var body = await _client.GetFilingDocument(
                Cik.Trim(company.Cik), filing.AccessionNumber.Replace("-", ""), doc);
            if (string.IsNullOrWhiteSpace(body)) return NotFound("Filing document not found.");
            _cache.Set(key, body, TimeSpan.FromMinutes(30));
            return Content(body, "text/plain");   // raw markup; the viewer sanitises and renders it
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
        }
    }

    // Ask EDGAR which document this accession's filing leads with, store the resulting archive URL on
    // the row, and return the file name. Uses the submissions feed (the same source the filings
    // browser lists from), where accession and primaryDocument are parallel arrays.
    private async Task<string?> BackfillPrimaryDocAsync(Filing filing, Company company)
    {
        var recent = (await _client.GetSubmissions(Cik.Pad(company.Cik!)))?.Filings?.Recent;
        if (recent?.AccessionNumber is not { } accessions) return null;

        var wanted = filing.AccessionNumber.Replace("-", "");
        var i = accessions.FindIndex(a => a is not null && a.Replace("-", "") == wanted);
        if (i < 0) return null;

        var doc = recent.PrimaryDocument?.ElementAtOrDefault(i);
        var url = EdgarArchive.DocUrl(company.Cik, filing.AccessionNumber, doc);
        if (url is null) return null;

        filing.PrimaryDocUrl = url;
        if (string.IsNullOrWhiteSpace(filing.Form)) filing.Form = recent.Form?.ElementAtOrDefault(i);
        if (filing.FilingDate is null &&
            DateTime.TryParse(recent.FilingDate?.ElementAtOrDefault(i), out var filed)) filing.FilingDate = filed;
        _repo.Update(filing);
        return doc;
    }

    // Accession number is globally unique (and the unique index spans soft-deleted rows), so
    // creation goes through Upsert: an existing row for the same accession is revived/refreshed
    // instead of colliding with the unique index.
    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<FilingDto> Create(FilingRequestDto dto)
    {
        var entity = _repo.Upsert(dto.CompanyId!.Value, dto.AccessionNumber, dto.Form, dto.FilingDate, dto.PrimaryDocUrl);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<FilingDto>(entity));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<FilingDto> Update(long id, FilingRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<FilingDto>(entity));
    }

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public IActionResult Delete(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        _repo.SoftDelete(id);
        return NoContent();
    }
}
