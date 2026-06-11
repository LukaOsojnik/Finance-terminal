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
public class TradeBlocsController : ControllerBase
{
    private readonly ITradeBlocRepository _repo;
    private readonly IMapper _mapper;

    public TradeBlocsController(ITradeBlocRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<TradeBlocDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<TradeBlocDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<TradeBlocDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<TradeBlocDto>(entity));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<TradeBlocDto> Create(TradeBlocRequestDto dto)
    {
        var entity = _mapper.Map<TradeBloc>(dto);
        _repo.Add(entity, dto.CountryIds);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<TradeBlocDto>(entity));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<TradeBlocDto> Update(long id, TradeBlocRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity, dto.CountryIds);
        return Ok(_mapper.Map<TradeBlocDto>(_repo.GetById(id)));
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
