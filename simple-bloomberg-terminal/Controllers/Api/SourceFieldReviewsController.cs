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
public class SourceFieldReviewsController : ControllerBase
{
    private readonly ISourceFieldReviewRepository _repo;
    private readonly IMapper _mapper;

    public SourceFieldReviewsController(ISourceFieldReviewRepository repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    [HttpGet]
    public ActionResult<IEnumerable<SourceFieldReviewDto>> GetAll(string? q = null)
    {
        var items = string.IsNullOrWhiteSpace(q) ? _repo.GetAll() : _repo.Search(q);
        return Ok(_mapper.Map<List<SourceFieldReviewDto>>(items));
    }

    [HttpGet("{id:long}")]
    public ActionResult<SourceFieldReviewDto> GetById(long id)
    {
        var entity = _repo.GetById(id);
        return entity is null ? NotFound() : Ok(_mapper.Map<SourceFieldReviewDto>(entity));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<SourceFieldReviewDto> Create(SourceFieldReviewRequestDto dto)
    {
        var entity = _mapper.Map<SourceFieldReview>(dto);
        _repo.Add(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<SourceFieldReviewDto>(entity));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public ActionResult<SourceFieldReviewDto> Update(long id, SourceFieldReviewRequestDto dto)
    {
        var entity = _repo.GetById(id);
        if (entity is null) return NotFound();
        _mapper.Map(dto, entity);
        _repo.Update(entity);
        return Ok(_mapper.Map<SourceFieldReviewDto>(entity));
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
