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
public class CountryDetailsController : ControllerBase
{
    private readonly ICountryDetailsRepository _repo;
    private readonly IMapper _mapper;

    public CountryDetailsController(ICountryDetailsRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<CountryDetailsDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<CountryDetailsDto>>(items));
    }

    // CountryDetails is keyed 1:1 by CountryId (no identity of its own).
    [HttpGet("{countryId:long}")]
    public ActionResult<CountryDetailsDto> GetById(long countryId)
    {
        var entity = _repo.GetById(countryId);
        return entity is null ? NotFound() : Ok(_mapper.Map<CountryDetailsDto>(entity));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<CountryDetailsDto> Create(CountryDetailsRequestDto dto)
    {
        var entity = _mapper.Map<CountryDetails>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { countryId = entity.CountryId }, _mapper.Map<CountryDetailsDto>(entity));
    }

    [HttpPut("{countryId:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<CountryDetailsDto> Update(long countryId, CountryDetailsRequestDto dto)
    {
        var entity = _repo.GetById(countryId);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<CountryDetailsDto>(entity));
    }

    [HttpDelete("{countryId:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public IActionResult Delete(long countryId)
    {
        if (_repo.GetById(countryId) is null) return NotFound();
        _repo.SoftDelete(countryId);
        return NoContent();
    }
}
