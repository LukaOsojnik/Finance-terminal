using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class CostSourcesController : ControllerBase
{
    private readonly ICostSourceRepository _repo;
    private readonly IMapper _mapper;

    public CostSourcesController(ICostSourceRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<CostSourceDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<CostSourceDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<CostSourceDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<CostSourceDto>(entity));
    }

    [HttpPost]
    public ActionResult<CostSourceDto> Create(CostSourceRequestDto dto)
    {
        var entity = _mapper.Map<CostSource>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<CostSourceDto>(entity));
    }

    [HttpPut("{id:long}")]
    public ActionResult<CostSourceDto> Update(long id, CostSourceRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<CostSourceDto>(entity));
    }

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        _repo.SoftDelete(id);
        return NoContent();
    }
}
