using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class GdpSnapshotsController : ControllerBase
{
    private readonly IGdpSnapshotRepository _repo;
    private readonly IMapper _mapper;

    public GdpSnapshotsController(IGdpSnapshotRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<GdpSnapshotDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<GdpSnapshotDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<GdpSnapshotDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<GdpSnapshotDto>(entity));
    }

    [HttpPost]
    public ActionResult<GdpSnapshotDto> Create(GdpSnapshotRequestDto dto)
    {
        var entity = _mapper.Map<GdpSnapshot>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<GdpSnapshotDto>(entity));
    }

    [HttpPut("{id:long}")]
    public ActionResult<GdpSnapshotDto> Update(long id, GdpSnapshotRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<GdpSnapshotDto>(entity));
    }

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        _repo.SoftDelete(id);
        return NoContent();
    }
}
