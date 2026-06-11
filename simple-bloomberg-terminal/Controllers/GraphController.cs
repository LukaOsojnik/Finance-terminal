using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("graph")]
[AllowAnonymous]
public class GraphController : Controller
{
    private readonly ICompanyRepository _companies;
    private readonly IMapper _mapper;

    public GraphController(ICompanyRepository companies, IMapper mapper)
    {
        _companies = companies;
        _mapper = mapper;
    }

    [HttpGet, Route("")]
    public IActionResult Index(long? companyId)
    {
        var vm = new GraphIndexViewModel
        {
            Companies = _companies.GetAll(),
            SelectedCompanyId = companyId
        };
        return View(vm);
    }

    [HttpGet, Route("data/company/{id:long}")]
    public IActionResult CompanyGraph(long id)
    {
        var company = _companies.GetWithGraphRelations(id);
        if (company == null) return NotFound();

        return Json(_mapper.Map<GraphResponse>(company));
    }
}
