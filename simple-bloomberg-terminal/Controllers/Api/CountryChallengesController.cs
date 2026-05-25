using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class CountryChallengesController : ControllerBase
{
    private readonly ICountryChallengeRepository _repo;
    private readonly IMapper _mapper;

    public CountryChallengesController(ICountryChallengeRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<CountryChallengeDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<CountryChallengeDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<CountryChallengeDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<CountryChallengeDto>(entity));
    }

    [HttpPost]
    public ActionResult<CountryChallengeDto> Create(CountryChallengeRequestDto dto)
    {
        var entity = _mapper.Map<CountryChallenge>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<CountryChallengeDto>(entity));
    }

    [HttpPut("{id:long}")]
    public ActionResult<CountryChallengeDto> Update(long id, CountryChallengeRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<CountryChallengeDto>(entity));
    }

    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
    {
        if (_repo.GetById(id) is null) return NotFound();
        _repo.SoftDelete(id);
        return NoContent();
    }
}
