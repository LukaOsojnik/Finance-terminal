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
public class ScenarioShocksController : ControllerBase
{
    private readonly IScenarioShockRepository _repo;
    private readonly IMapper _mapper;

    public ScenarioShocksController(IScenarioShockRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<ScenarioShockDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<ScenarioShockDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<ScenarioShockDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<ScenarioShockDto>(entity));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<ScenarioShockDto> Create(ScenarioShockRequestDto dto)
    {
        var entity = _mapper.Map<ScenarioShock>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<ScenarioShockDto>(entity));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<ScenarioShockDto> Update(long id, ScenarioShockRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<ScenarioShockDto>(entity));
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
