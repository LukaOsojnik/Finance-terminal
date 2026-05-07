using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("countries")]
public class CountriesController : Controller
{
    private readonly ICountryRepository _countries;
    private readonly ICountryDetailsRepository _countryDetails;
    private readonly ICompanyRepository _companies;

    // Java: @Autowired constructor injection — identical pattern in ASP.NET Core
    public CountriesController(
        ICountryRepository countries,
        ICountryDetailsRepository countryDetails,
        ICompanyRepository companies)
    {
        _countries = countries;
        _countryDetails = countryDetails;
        _companies = companies;
    }

    [Route("")]
    public IActionResult Index()
    {
        return View(_countries.GetAll().Select(CountryRowViewModel.From));
    }

    [Route("{id:long}/overview")]
    public IActionResult Details(long id)
    {
        var country = _countries.GetById(id);
        if (country is null)
            return NotFound();

        var details = _countryDetails.GetByCountryId(id);

        // TopCompanies: pulled from ICompanyRepository to avoid circular dep
        // (Country.Companies stays empty — company→country nav is wired, not country→company)
        var topCompanies = _companies.GetAll()
            .Where(c => c.CountryId == id)
            .ToList();

        var viewModel = new CountryDetailsViewModel
        {
            Country = country,
            MarketPosition = details?.MarketPosition,
            Advantages = details?.Advantages ?? [],
            Challenges = details?.Challenges ?? [],
            TopCompanies = topCompanies,
            TradeBlocs = country.TradeBlocs.ToList(),
            GdpHistory = details?.GdpHistory ?? [],
        };

        return View(viewModel);
    }
}
