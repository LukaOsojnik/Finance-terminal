using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GraphController : ControllerBase
{
    private readonly ICompanyRepository _companies;
    private readonly IMapper _mapper;

    public GraphController(ICompanyRepository companies, IMapper mapper)
    {
        _companies = companies;
        _mapper = mapper;
    }

    // GET api/graph/company?cik=0000320193
    // Returns the same hub-and-spoke GraphResponse the MVC graph renders, keyed by CIK.
    [HttpGet("company")]
    public ActionResult<GraphResponse> CompanyGraph([FromQuery] string cik)
    {
        if (string.IsNullOrWhiteSpace(cik)) return BadRequest("cik is required.");

        var company = _companies.GetWithGraphRelationsByCik(cik);
        if (company is null) return NotFound();

        return Ok(_mapper.Map<GraphResponse>(company));
    }
}
