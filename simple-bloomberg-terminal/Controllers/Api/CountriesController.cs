using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class CountriesController : ControllerBase
{
    private readonly ICountryRepository _repo;
    private readonly IMapper _mapper;

    public CountriesController(ICountryRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<CountryDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<CountryDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<CountryDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<CountryDto>(entity));
    }

    [HttpPost]
    public ActionResult<CountryDto> Create(CountryRequestDto dto)
    {
        var entity = _mapper.Map<Country>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<CountryDto>(entity));
    }

    [HttpPut("{id:long}")]
    public ActionResult<CountryDto> Update(long id, CountryRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<CountryDto>(entity));
    }

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        _repo.SoftDelete(id);
        return NoContent();
    }
}
