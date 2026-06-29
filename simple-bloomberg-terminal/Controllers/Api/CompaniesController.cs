using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyRepository _repo;
    private readonly IMapper _mapper;

    public CompaniesController(ICompanyRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<CompanyDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<CompanyDto>>(items));
    }

    // Uses the graph-loading repo method so nested revenue/cost sources are populated.
    [HttpGet("{id:long}")]
    public ActionResult<CompanyDto> GetById(long id)
    {
        var entity = _repo.GetWithGraphRelations(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<CompanyDto>(entity));
    }

    // Read-only stored weekly trading-volume series for a company (no SEC/Yahoo call — just what's
    // persisted). Added for the MCP server's volume tool; the ingest path that fills it stays a
    // separate Admin/Manager POST on the MVC controller.
    [HttpGet("{id:long}/volume")]
    public IActionResult GetVolume(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        var series = _repo.GetVolumeHistory(id)
            .Select(v => new { weekStart = v.WeekStart.ToString("yyyy-MM-dd"), volume = v.Volume });
        return Ok(series);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<CompanyDto> Create(CompanyRequestDto dto)
    {
        var entity = _mapper.Map<Company>(dto);
        _repo.Add(entity);
        var created = _repo.GetWithGraphRelations(entity.Id);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<CompanyDto>(created));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<CompanyDto> Update(long id, CompanyRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<CompanyDto>(_repo.GetWithGraphRelations(id)));
    }

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public IActionResult Delete(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        try
        {
            _repo.SoftDelete(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }
}
