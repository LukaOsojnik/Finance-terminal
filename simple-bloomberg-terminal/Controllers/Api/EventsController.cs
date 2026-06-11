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
public class EventsController : ControllerBase
{
    private readonly IEventRepository _repo;
    private readonly IMapper _mapper;

    public EventsController(IEventRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<EventDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<EventDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<EventDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<EventDto>(entity));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<EventDto> Create(EventRequestDto dto)
    {
        var entity = _mapper.Map<Event>(dto);
        _repo.Add(entity, dto.CountryIds, dto.CompanyIds, dto.TradeBlocIds);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<EventDto>(entity));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<EventDto> Update(long id, EventRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity, dto.CountryIds, dto.CompanyIds, dto.TradeBlocIds);
        return Ok(_mapper.Map<EventDto>(_repo.GetById(id)));
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
