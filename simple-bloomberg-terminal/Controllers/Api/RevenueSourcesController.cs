using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class RevenueSourcesController : ControllerBase
{
    private readonly IRevenueSourceRepository _repo;
    private readonly IMapper _mapper;

    public RevenueSourcesController(IRevenueSourceRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<RevenueSourceDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<RevenueSourceDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<RevenueSourceDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<RevenueSourceDto>(entity));
    }

    [HttpPost]
    public ActionResult<RevenueSourceDto> Create(RevenueSourceRequestDto dto)
    {
        var entity = _mapper.Map<RevenueSource>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<RevenueSourceDto>(entity));
    }

    [HttpPut("{id:long}")]
    public ActionResult<RevenueSourceDto> Update(long id, RevenueSourceRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<RevenueSourceDto>(entity));
    }

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        _repo.SoftDelete(id);
        return NoContent();
    }
}
