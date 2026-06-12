using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilingsController : ControllerBase
{
    private readonly IFilingRepository _repo;
    private readonly IMapper _mapper;

    public FilingsController(IFilingRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
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
