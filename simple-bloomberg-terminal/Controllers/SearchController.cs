using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Services.Search;

namespace simple_bloomberg_terminal.Controllers;

// Global search backing the nav-bar command bar and the home hero. One endpoint
// serves both; the front end groups results by the Kind tag each hit carries.
[Route("api/search")]
[AllowAnonymous]
public class SearchController : Controller
{
    private readonly ISearchService _search;

    public SearchController(ISearchService search) => _search = search;

    [HttpGet]
    public IActionResult Index(string? q) => Json(_search.Search(q));
}
